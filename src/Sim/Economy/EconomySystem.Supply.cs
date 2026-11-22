using System;
using Rts.Sim.Units;

namespace Rts.Sim.Economy;

/// <summary>
/// Supply rules (the design's "keep storehouses stocked and haulers moving"): a player sets how much of each
/// good one of their storage buildings should keep, and workers on supply duty there carry the good to it
/// from the player's other storage, where there is more of it than that storage's own rule wants. A forward
/// storehouse kept stocked with stone pays for the walls going up beside it (costs come from the storage
/// nearest the work), and is worth raiding. Automatic workers with supply on their priority list take the
/// duty on their own (see <see cref="Automation.AutomationSystem"/>). Carts (role <see cref="UnitRole.Hauler"/>,
/// the design's "carts carry four times more") do nothing but this duty, a cart's load at a time.
/// </summary>
public sealed partial class EconomySystem
{
    /// <summary>The most of one good a supply rule can ask a building to keep.</summary>
    public const int MaxSupply = 500;

    /// <summary>Sets or clears a supply rule on one of the player's storage buildings (not a plan).</summary>
    internal void Apply(SupplyCommand c)
    {
        var b = Buildings.Get(c.Building);
        if (b == null || b.Owner != c.PlayerId || !b.Def.Storage || b.Planned || !Enum.IsDefined(c.Good)) return;
        b.Supply[(int)c.Good] = Math.Clamp(c.Amount, 0, MaxSupply);
    }

    /// <summary>
    /// How much of a good storage building b can spare for supply runs: what it holds beyond its own rule
    /// for the good (all it holds when it has none).
    /// </summary>
    public static int Spare(Building b, Good good) => Math.Max(0, b.Stock[(int)good] - b.Supply[(int)good]);

    /// <summary>How far storage building b is short of its rule for a good, before goods on their way are counted.</summary>
    public static int Short(Building b, Good good) => Math.Max(0, b.Supply[(int)good] - b.Stock[(int)good]);

    /// <summary>
    /// The player's finished storage building, other than <paramref name="target"/>, nearest to column (x, z)
    /// (by straight line, then id) that can spare some of a good; null if none can.
    /// </summary>
    public Building? SupplySource(Building target, Good good, int x, int z)
    {
        Building? best = null;
        long bestD = long.MaxValue;
        foreach (var b in Buildings.All)
        {
            if (b == target || b.Owner != target.Owner || !b.Active || !b.Def.Storage || Spare(b, good) == 0) continue;
            long d = b.CentreDistance2(x, z);
            if (d >= bestD) continue;
            bestD = d;
            best = b;
        }
        return best;
    }

    /// <summary>
    /// True if storage building b is short of some good under its supply rules that another of its owner's
    /// storage buildings can spare: there is supply work there.
    /// </summary>
    public bool WantsSupply(Building b)
    {
        if (!b.Active || !b.Def.Storage || !b.HasSupply) return false;
        foreach (var g in Goods.All)
            if (Short(b, g) > 0 && SupplySource(b, g, b.X, b.Z) != null) return true;
        return false;
    }

    /// <summary>Goods of a kind on their way to storage building b with its other supply workers (not unit <paramref name="except"/>).</summary>
    private int OnTheWay(Building b, Good good, int except)
    {
        var u = U;
        int n = 0;
        for (int k = 0; k < u.Count; k++)
        {
            if (k == except || u.Job[k] != UnitJob.Supply || u.JobTarget[k] != b.Id) continue;
            if (u.CarryAmount[k] > 0) { if (u.Carry[k] == good) n += u.CarryAmount[k]; }
            else if (u.Phase[k] == JobPhase.Fetch && u.JobX[k] == (int)good) n += LoadOf(k);
        }
        return n;
    }

    /// <summary>
    /// A supply worker: fetches a load from the storage that can spare it, brings it to its building, and
    /// looks for the next load; waits while there is nothing to fetch; stops once the building has no rules.
    /// </summary>
    private void StepSupply(int i)
    {
        var u = U;
        var b = Buildings.Get(u.JobTarget[i]);
        if (b == null || b.Owner != u.Owner[i] || !b.Active || !b.Def.Storage)
        {
            // Gone, captured or burned: what it carries goes to the nearest storage instead.
            bool carrying = u.CarryAmount[i] > 0;
            EndJob(i);
            if (carrying)
            {
                u.Job[i] = UnitJob.Deliver;
                GoToStorage(i);
            }
            return;
        }
        switch (u.Phase[i])
        {
            case JobPhase.Wait:
                if (--u.JobTimer[i] <= 0) DecideSupply(i, b);
                return;
            case JobPhase.Fetch:
                StepSupplyFetch(i, b);
                return;
            case JobPhase.ToStore:
                SetPhase(i, JobPhase.ToWork);
                return;
        }
        if (WalkToBuilding(i, b, out bool there)) return;
        if (!there)
        {
            SetPhase(i, JobPhase.ToWork);
            return;
        }
        // At its building: unload, and on to the next load.
        if (u.CarryAmount[i] > 0) b.Stock[(int)u.Carry[i]] += u.CarryAmount[i];
        u.CarryAmount[i] = 0;
        DecideSupply(i, b);
    }

    /// <summary>
    /// Picks supply worker i's next trip for building b: with a load, to b; otherwise to fetch the good b is
    /// shortest of (counting what its other supply workers are bringing) from the nearest storage that can
    /// spare it. With none, an automatic worker stops and any other waits; the job ends once b has no rules.
    /// </summary>
    private void DecideSupply(int i, Building b)
    {
        var u = U;
        if (u.CarryAmount[i] > 0)
        {
            SetPhase(i, JobPhase.ToWork);
            return;
        }
        if (!b.HasSupply)
        {
            EndJob(i);
            return;
        }
        int ux = u.X[i].FloorToInt(), uz = u.Z[i].FloorToInt();
        Building? from = null;
        var need = Good.Food;
        int most = 0;
        foreach (var g in Goods.All)
        {
            int shortBy = Short(b, g);
            if (shortBy <= most) continue;
            shortBy -= OnTheWay(b, g, i);
            if (shortBy <= most) continue;
            var source = SupplySource(b, g, ux, uz);
            if (source == null) continue;
            from = source;
            need = g;
            most = shortBy;
        }
        if (from == null)
        {
            // Nothing to fetch: an automatic worker goes on to other work (automation sends it back when
            // there is some); one the player put on duty waits for more.
            if (u.Auto[i]) EndJob(i);
            else Wait(i);
            return;
        }
        SetPhase(i, JobPhase.Fetch);
        u.JobStore[i] = from.Id;
        u.JobX[i] = (int)need;
    }

    private void StepSupplyFetch(int i, Building b)
    {
        var u = U;
        var good = (Good)u.JobX[i];
        var s = Buildings.Get(u.JobStore[i]);
        if (s == null || s == b || s.Owner != u.Owner[i] || !s.Active || !s.Def.Storage || Spare(s, good) == 0)
        {
            DecideSupply(i, b);
            return;
        }
        switch (Approach(i, AtBuilding(i, s)))
        {
            case Travel.Arrived:
                // No more than is still missing once the other supply workers' loads are in.
                int take = Math.Min(Math.Min(LoadOf(i), Spare(s, good)), Short(b, good) - OnTheWay(b, good, i));
                if (take > 0)
                {
                    s.Stock[(int)good] -= take;
                    u.Carry[i] = good;
                    u.CarryAmount[i] = take;
                }
                DecideSupply(i, b);
                return;
            case Travel.NeedSpot:
                if (AccessSpot(s, out int x, out int z, out int y)) _sim.SendUnit(i, x, z, y);
                else Wait(i);
                return;
            case Travel.Failed:
                Wait(i);
                return;
        }
    }
}
