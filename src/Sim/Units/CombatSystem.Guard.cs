using System;
using Rts.Sim.Economy;

namespace Rts.Sim.Units;

/// <summary>
/// Guard orders (<see cref="GuardCommand"/>): a soldier keeps within <see cref="GuardFollow"/> columns of its
/// charge (one of its player's units or buildings), walking after it when it moves, and fights the enemies
/// that come within <see cref="GuardRange"/> columns of it, first whoever hits the charge. It lets an enemy
/// go once that enemy is more than <see cref="GuardLeash"/> columns from the charge, and walks back. The order
/// ends when the charge dies, is destroyed or changes hands, or the soldier is given another order.
///
/// Guards of a town hall with a governor (see <see cref="GovernCommand"/>) are the governor's town guard: they
/// defend the whole town, the ground within <see cref="TownReach"/> columns of the hall. They pick fights with
/// enemies anywhere in it, let them go only past <see cref="TownLeash"/> columns, and, with nothing to fight,
/// the governor sends them after the enemy in the town nearest to them that the player can see (see
/// <see cref="Intruder"/>), even out of their own sight. With nobody left in the town they walk back to the
/// hall. When the governor steps off, they are plain guards of the hall again.
/// </summary>
public sealed partial class CombatSystem
{
    /// <summary>A guard picks fights with enemies within this many columns of its charge.</summary>
    public const int GuardRange = 8;

    /// <summary>A guard chasing an enemy lets it go once it is more than this many columns from the charge.</summary>
    public const int GuardLeash = 12;

    /// <summary>A guard with nothing to fight walks back once it is more than this many columns from its charge.</summary>
    public const int GuardFollow = 3;

    /// <summary>Ticks between a guard's looks at where its charge has got to.</summary>
    public const int GuardInterval = 8;

    /// <summary>How far (columns from the hall) a governor's town guard defends: the governor's town.</summary>
    public const int TownReach = Automation.AutomationSystem.GovernRadius;

    /// <summary>A town guard chasing an enemy lets it go once it is more than this many columns from the hall.</summary>
    public const int TownLeash = TownReach + 6;

    /// <summary>How close (blocks) a guard walking to its charge comes before it stops.</summary>
    public static readonly Fix64 GuardArrive = Fix64.FromRatio(3, 2);

    internal void Apply(GuardCommand c)
    {
        var u = U;
        Building? b = null;
        if (c.Unit != 0)
        {
            int ward = u.IndexOf(c.Unit);
            if (ward < 0 || u.Owner[ward] != c.PlayerId || u.Subdued[ward]) return;
        }
        else
        {
            b = Buildings.Get(c.Building);
            if (b == null || b.Owner != c.PlayerId || b.Planned) return;
        }
        foreach (int i in _sim.OwnedUnits(c))
        {
            if (!Def(i).CanAttack || u.Id[i] == c.Unit) continue;
            u.ClearJob(i);
            u.ClearCombat(i);
            u.ClearFormation(i);
            u.Combat[i] = CombatOrder.Guard;
            u.GuardUnit[i] = c.Unit;
            u.GuardBuilding[i] = b?.Id ?? 0;
            Halt(i);
            Follow(i, now: true);
        }
    }

    /// <summary>The guard's charge, if it still has one: the unit's index (else -1) or the building (else null).</summary>
    private bool Charge(int i, out int ward, out Building? b)
    {
        var u = U;
        ward = -1;
        b = null;
        if (u.GuardUnit[i] != 0)
        {
            ward = u.IndexOf(u.GuardUnit[i]);
            return ward >= 0 && u.Owner[ward] == u.Owner[i] && !u.Subdued[ward];
        }
        b = Buildings.Get(u.GuardBuilding[i]);
        return b != null && b.Owner == u.Owner[i] && !b.Planned;
    }

    /// <summary>True if guard i guards a town hall of its player's that has a governor: it is in the town guard.</summary>
    public bool TownGuard(int i) => TownHallGuarded(i) != null;

    /// <summary>The governed town hall guard i guards, or null if it is not in a town guard.</summary>
    private Building? TownHallGuarded(int i)
    {
        var u = U;
        if (u.Combat[i] != CombatOrder.Guard || u.GuardUnit[i] != 0) return null;
        var b = Buildings.Get(u.GuardBuilding[i]);
        return b != null && b.Owner == u.Owner[i] && !b.Planned && b.Def.Kind == BuildingKind.TownHall
               && b.Governor != GovernorFocus.None ? b : null;
    }

    /// <summary>How far from its charge guard i picks fights: the town for a town guard.</summary>
    private int RangeOf(int i) => TownGuard(i) ? TownReach : GuardRange;

    /// <summary>How far from its charge guard i chases an enemy before it lets it go.</summary>
    private int LeashOf(int i) => TownGuard(i) ? TownLeash : GuardLeash;

    /// <summary>The player's units guarding that hall: its town guard while it has a governor.</summary>
    public int TownGuardCount(Building hall)
    {
        var u = U;
        int n = 0;
        for (int i = 0; i < u.Count; i++)
            if (u.Combat[i] == CombatOrder.Guard && u.GuardUnit[i] == 0 && u.GuardBuilding[i] == hall.Id && u.Owner[i] == hall.Owner) n++;
        return n;
    }

    /// <summary>
    /// For town guard i with nothing to fight: the enemy unit in its town (within <see cref="TownReach"/>
    /// columns of the hall) that its player can see and that is nearest to it (ties to the lowest id), or -1.
    /// A beaten monster and a neutral city-state's guards are left alone, as everyone leaves them alone.
    /// </summary>
    private int Intruder(int i, Building hall)
    {
        var u = U;
        int owner = u.Owner[i];
        Fix64 hx = Fix64.FromInt(hall.X) + Fix64.FromRatio(hall.Def.SizeX, 2);
        Fix64 hz = Fix64.FromInt(hall.Z) + Fix64.FromRatio(hall.Def.SizeZ, 2);
        Fix64 r = Fix64.FromInt(TownReach + Math.Max(hall.Def.SizeX, hall.Def.SizeZ));
        Cells(hx, hz, r, out int x0, out int z0, out int x1, out int z1);
        int best = -1;
        Fix64 bestD = Fix64.MaxValue;
        for (int cz = z0; cz <= z1; cz++)
        for (int cx = x0; cx <= x1; cx++)
        {
            int c = (cz * _cellsX + cx) * _players;
            for (int p = 0; p < _players; p++)
            {
                if (!Hostile(owner, p)) continue;
                for (int j = _head[c + p]; j >= 0; j = _next[j])
                {
                    if (u.Subdued[j] || u.Town[j] != 0 && u.Owner[j] == Simulation.WildPlayer) continue;
                    if (hall.ColumnDistance(u.X[j].FloorToInt(), u.Z[j].FloorToInt()) > TownReach) continue;
                    if (!_sim.Vision.SeesUnit(owner, j)) continue;
                    Fix64 dx = u.X[j] - u.X[i], dz = u.Z[j] - u.Z[i];
                    Fix64 d = dx * dx + dz * dz;
                    if (d < bestD || (d == bestD && u.Id[j] < u.Id[best]))
                    {
                        best = j;
                        bestD = d;
                    }
                }
            }
        }
        return best;
    }

    /// <summary>Columns from (x, z) to the charge, counting diagonal steps as one (0 inside a building).</summary>
    private int ChargeDistance(int ward, Building? b, int x, int z) =>
        b != null
            ? b.ColumnDistance(x, z)
            : Math.Max(Math.Abs(U.X[ward].FloorToInt() - x), Math.Abs(U.Z[ward].FloorToInt() - z));

    /// <summary>True if unit t is within <paramref name="range"/> columns of guard i's charge (or the charge is gone).</summary>
    private bool NearCharge(int i, int t, int range) =>
        !Charge(i, out int ward, out var b) || ChargeDistance(ward, b, U.X[t].FloorToInt(), U.Z[t].FloorToInt()) <= range;

    /// <summary>The enemy (index) that hit guard i's charge last tick, if it is one to fight, or -1.</summary>
    private int ChargeAttacker(int i)
    {
        var u = U;
        if (!Charge(i, out int ward, out _) || ward < 0 || u.HitBy[ward] == 0) return -1;
        int by = u.IndexOf(u.HitBy[ward]);
        return by >= 0 && Hostile(u.Owner[i], u.Owner[by]) && !u.Subdued[by] && NearCharge(i, by, LeashOf(i)) ? by : -1;
    }

    /// <summary>
    /// A guard with nothing to fight: if its charge is gone the order ends; otherwise, every
    /// <see cref="GuardInterval"/> ticks (or <paramref name="now"/>), it walks to its charge if it is more than
    /// <see cref="GuardFollow"/> columns from it, or re-aims a walk the charge has moved away from.
    /// </summary>
    private void Follow(int i, bool now = false)
    {
        var u = U;
        if (!Charge(i, out int ward, out var b))
        {
            u.ClearCombat(i);
            return;
        }
        if (!now && (_sim.Tick + u.Id[i]) % GuardInterval != 0) return;
        int gx, gz, gy;
        if (b != null)
        {
            if (!_sim.Economy.AccessSpot(b, out gx, out gz, out gy)) return;
        }
        else
        {
            gx = u.X[ward].FloorToInt();
            gz = u.Z[ward].FloorToInt();
            gy = u.Y[ward];
        }
        bool near = ChargeDistance(ward, b, u.X[i].FloorToInt(), u.Z[i].FloorToInt()) <= GuardFollow;
        if (u.Order[i] == UnitOrder.Move)
        {
            if (Math.Max(Math.Abs(u.OrderX[i] - gx), Math.Abs(u.OrderZ[i] - gz)) <= 1) return;
        }
        else if (near)
        {
            return;
        }
        _sim.SendUnit(i, gx, gz, gy);
        u.ArriveRadius[i] = GuardArrive;
    }
}
