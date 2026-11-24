using System;
using System.Collections.Generic;
using Rts.Sim.Units;

namespace Rts.Sim.Economy;

/// <summary>
/// Field repairs (Craft guilds): a player with the <see cref="Content.TechEffectType.FieldRepairs"/> tech sends workers to
/// one of their damaged siege machines (a catapult or a battering ram: a <see cref="UnitDef.Machine"/> that does not
/// sail; see <see cref="MendCommand"/>). They walk up to it, following it if it moves, and give back
/// <see cref="MendPercent"/> of its hit points each <see cref="MendStepTicks"/> of work, under fire or not, each step paid
/// for with its share of half the machine's cost in materials (not food), taken from storage.
/// </summary>
public sealed partial class EconomySystem
{
    /// <summary>Percent of a machine's hit points one step of field repairs gives back.</summary>
    public const int MendPercent = 5;
    /// <summary>Ticks of work (at the usual work speed) one step of field repairs takes.</summary>
    public const int MendStepTicks = Tps;
    /// <summary>How near (columns, either way) a worker must stand to a machine to mend it.</summary>
    public const int MendReach = 2;

    /// <summary>True if unit j is a damaged siege machine of the player's that their workers can mend now (they have the tech).</summary>
    public bool Mendable(int player, int j)
    {
        if (j < 0 || j >= U.Count || U.Owner[j] != player || U.Hp[j] <= 0 || U.Aboard[j] != 0) return false;
        var def = Content.Units[U.Def[j]];
        return def.Machine && !def.IsBoat && U.Hp[j] < MaxHp(player, def) && CanFieldRepair(player);
    }

    /// <summary>Hit points one step of field repairs gives back to a machine of this type.</summary>
    public int MendStep(int player, UnitDef def) => Math.Max(1, MaxHp(player, def) * MendPercent / 100);

    /// <summary>
    /// What one step of <paramref name="h"/> hit points of field repairs to a machine of this type costs: each material of its
    /// cost (food aside) in proportion to half its cost over all its hit points, rounded up.
    /// </summary>
    public GoodAmount[] FieldMendCost(int player, UnitDef def, int h)
    {
        long most = 2L * Math.Max(1, MaxHp(player, def));
        var owed = new List<GoodAmount>(def.Cost.Length);
        foreach (var c in def.Cost)
        {
            if (c.Good == Good.Food) continue;
            long share = (c.Amount * (long)h + most - 1) / most;
            if (share > 0) owed.Add(new GoodAmount(c.Good, (int)Math.Min(c.Amount, share)));
        }
        return owed.ToArray();
    }

    /// <summary>Sends the command's workers to mend the machine, if the player can.</summary>
    internal void Apply(MendCommand c)
    {
        int j = U.IndexOf(c.Target);
        if (!Mendable(c.PlayerId, j)) return;
        foreach (int i in Workers(c))
        {
            if (i == j || U.Job[i] == UnitJob.Mend && U.JobTarget[i] == c.Target) continue;
            StartJob(i, UnitJob.Mend, c.Target);
        }
    }

    private void StepMend(int i)
    {
        var u = U;
        int j = u.IndexOf(u.JobTarget[i]);
        if (!Mendable(u.Owner[i], j))
        {
            EndJob(i);
            return;
        }
        int tx = u.X[j].FloorToInt(), tz = u.Z[j].FloorToInt();
        bool near = Math.Abs(u.X[i].FloorToInt() - tx) <= MendReach && Math.Abs(u.Z[i].FloorToInt() - tz) <= MendReach
                    && Math.Abs(u.Y[i] - u.Y[j]) <= 1;
        if (!near)
        {
            // Walk (again) to where the machine stands now; give up after a few tries that get nowhere.
            if (u.Order[i] == UnitOrder.Move && (_sim.Tick + u.Id[i]) % Tps != 0) return;
            if (u.Order[i] != UnitOrder.Move && ++u.JobRetry[i] > MaxRetries * 4)
            {
                EndJob(i);
                return;
            }
            _sim.SendUnit(i, tx, tz, u.Y[j]);
            return;
        }
        if (u.Order[i] == UnitOrder.Move) Halt(i);
        u.JobRetry[i] = 0;
        u.JobTimer[i] += WorkOf(i);
        if ((_sim.Tick + u.Id[i]) % Tps == 0) _sim.Evolution.Built(i);
        if (u.JobTimer[i] < MendStepTicks * 100) return;
        u.JobTimer[i] = 0;
        var def = Content.Units[u.Def[j]];
        int max = MaxHp(u.Owner[j], def), h = Math.Min(MendStep(u.Owner[j], def), max - u.Hp[j]);
        if (!TrySpend(u.Owner[j], FieldMendCost(u.Owner[j], def, h), tx, tz))
        {
            EndJob(i); // nothing to pay for it with
            return;
        }
        u.Hp[j] += h;
        _sim.Combat.Mend(j, h, u.Hp[j] >= max);
    }

    // --- Yards: docks and siege workshops mending their own machines -------------------------------------

    /// <summary>How near (columns beyond its footprint) a machine must wait to a yard (<see cref="BuildingDef.Mends"/>) to be mended there.</summary>
    public const int YardReach = 4;

    /// <summary>
    /// The player's finished yard unit j waits beside to be mended now, or null: j is a hurt machine of a player's,
    /// not aboard, out of the fight for the recovery delay, within <see cref="YardReach"/> of a finished building of
    /// its owner's that builds its type and mends (<see cref="BuildingDef.Mends"/>). No worker and no tech is needed.
    /// </summary>
    public Building? YardFor(int j)
    {
        var u = U;
        if (j < 0 || j >= u.Count || u.Hp[j] <= 0 || u.Aboard[j] != 0) return null;
        int owner = u.Owner[j];
        if (owner < 0 || owner >= Simulation.WildPlayer) return null;
        var def = Content.Units[u.Def[j]];
        if (!def.Machine || def.TrainedAt is not { Mends: > 0 } || u.Hp[j] >= MaxHp(owner, def)) return null;
        if (_sim.Tick - u.LastFight[j] < Content.Combat.RecoveryDelayTicks) return null;
        int x = u.X[j].FloorToInt(), z = u.Z[j].FloorToInt();
        foreach (var b in Buildings.All)
            if (b.Owner == owner && b.Active && b.Def == def.TrainedAt && b.ColumnDistance(x, z) <= YardReach
                && Math.Abs(u.Y[j] - b.Y) <= YardReach)
                return b;
        return null;
    }

    /// <summary>
    /// Once a second each (spread over units by id), a machine waiting beside its yard (<see cref="YardFor"/>) gets back
    /// the yard's <see cref="BuildingDef.Mends"/> percent of its hit points, paid for like field repairs
    /// (<see cref="FieldMendCost"/>) from storage; with nothing to pay with it waits.
    /// </summary>
    private void StepYards()
    {
        var u = U;
        bool any = false;
        foreach (var b in Buildings.All) any |= b.Active && b.Def.Mends > 0;
        if (!any) return;
        for (int j = 0; j < u.Count; j++)
        {
            if ((_sim.Tick + u.Id[j]) % Tps != 0) continue;
            var yard = YardFor(j);
            if (yard == null) continue;
            var def = Content.Units[u.Def[j]];
            int owner = u.Owner[j], max = MaxHp(owner, def);
            int h = Math.Min(Math.Max(1, max * yard.Def.Mends / 100), max - u.Hp[j]);
            if (!TrySpend(owner, FieldMendCost(owner, def, h), yard.X, yard.Z)) continue;
            u.Hp[j] += h;
            _sim.Combat.Mend(j, h, u.Hp[j] >= max);
        }
    }
}
