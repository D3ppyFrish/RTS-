using System;
using Rts.Sim.Economy;

namespace Rts.Sim.Units;

/// <summary>
/// Guard orders (<see cref="GuardCommand"/>): a soldier keeps within <see cref="GuardFollow"/> columns of its
/// charge (one of its player's units or buildings), walking after it when it moves, and fights the enemies
/// that come within <see cref="GuardRange"/> columns of it, first whoever hits the charge. It lets an enemy
/// go once that enemy is more than <see cref="GuardLeash"/> columns from the charge, and walks back. The order
/// ends when the charge dies, is destroyed or changes hands, or the soldier is given another order.
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
        return by >= 0 && u.Owner[by] != u.Owner[i] && !u.Subdued[by] && NearCharge(i, by, GuardLeash) ? by : -1;
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
