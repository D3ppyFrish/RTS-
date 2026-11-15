using System;
using Rts.Sim.Units;

namespace Rts.Sim.Economy;

/// <summary>
/// Caravans trading with city-states (see <see cref="CityStateSystem"/>). A caravan on the
/// <see cref="UnitJob.Trade"/> job goes round: it fetches a load of the good the town buys from its owner's
/// storage nearest the town that holds a whole load (<see cref="JobPhase.Fetch"/>), carries it to the market
/// (<see cref="JobPhase.ToWork"/>), sells it there for the good the town sells, and carries that to the
/// storage nearest the market (<see cref="JobPhase.ToStore"/>). With no load to be had it waits and looks
/// again. It keeps going until given another order.
/// </summary>
public sealed partial class EconomySystem
{
    /// <summary>Unit i takes what it carries to the storage building, then stops (a city-state's tribute caravan).</summary>
    internal void Deliver(int i, Building store)
    {
        StartJob(i, UnitJob.Deliver, 0);
        U.JobStore[i] = store.Id;
        SetPhase(i, JobPhase.ToStore);
    }

    /// <summary>The caravans among the command's units start trading with the town (those already trading there carry on).</summary>
    internal void Apply(TradeCommand c)
    {
        if (_sim.Cities.Get(c.City) is not { } town) return;
        foreach (int i in OwnedUnits(c))
        {
            if (!Content.Units[U.Def[i]].IsTrader) continue;
            if (U.Job[i] == UnitJob.Trade && U.JobTarget[i] == town.Id) continue;
            StartJob(i, UnitJob.Trade, town.Id);
            DecideTrade(i, town);
        }
    }

    private void StepTrade(int i)
    {
        var u = U;
        if (_sim.Cities.Get(u.JobTarget[i]) is not { } town)
        {
            EndJob(i);
            return;
        }
        switch (u.Phase[i])
        {
            case JobPhase.Fetch:
                StepLoad(i, town);
                return;
            case JobPhase.ToStore:
                if (StepToStore(i)) DecideTrade(i, town);
                return;
            case JobPhase.Wait:
                if (--u.JobTimer[i] <= 0) DecideTrade(i, town);
                return;
            case JobPhase.ToWork:
                switch (Approach(i, AtTown(i, town)))
                {
                    case Travel.Arrived:
                        Sell(i, town);
                        return;
                    case Travel.NeedSpot:
                        if (_sim.Cities.TradeSpot(town, out int tx, out int ty, out int tz)) _sim.SendUnit(i, tx, tz, ty);
                        else Wait(i);
                        return;
                    case Travel.Failed:
                        Wait(i);
                        return;
                }
                return;
            default:
                DecideTrade(i, town);
                return;
        }
    }

    /// <summary>
    /// What the caravan does next: take a load it carries (loaded on this trip) to the town, unload anything
    /// else it carries, or fetch a load; it waits if no storage of its owner's holds a whole load.
    /// </summary>
    private void DecideTrade(int i, CityState town)
    {
        var u = U;
        var def = town.Def;
        if (u.Carry[i] == def.Buys && u.CarryAmount[i] >= def.Load && u.JobX[i] > 0)
        {
            SetPhase(i, JobPhase.ToWork);
            return;
        }
        if (u.CarryAmount[i] > 0)
        {
            GoToStorage(i);
            return;
        }
        var store = StorageHolding(u.Owner[i], def.Buys, def.Load, town.X, town.Z);
        if (store == null)
        {
            Wait(i);
            return;
        }
        u.JobStore[i] = store.Id;
        SetPhase(i, JobPhase.Fetch);
    }

    /// <summary>Walks to storage <see cref="UnitStore.JobStore"/> and takes a load there, noting the route's length.</summary>
    private void StepLoad(int i, CityState town)
    {
        var u = U;
        var def = town.Def;
        var s = Buildings.Get(u.JobStore[i]);
        if (s == null || s.Owner != u.Owner[i] || !s.Active || !s.Def.Storage || s.Stock[(int)def.Buys] < def.Load)
        {
            DecideTrade(i, town);
            return;
        }
        switch (Approach(i, AtBuilding(i, s)))
        {
            case Travel.Arrived:
                s.Stock[(int)def.Buys] -= def.Load;
                u.Carry[i] = def.Buys;
                u.CarryAmount[i] = def.Load;
                u.JobX[i] = Math.Max(1, CityStateSystem.Route(s, town));
                SetPhase(i, JobPhase.ToWork);
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

    /// <summary>At the market: the load is sold for the town's good (more for its holder), and the caravan heads home.</summary>
    private void Sell(int i, CityState town)
    {
        var u = U;
        int owner = u.Owner[i], route = u.JobX[i];
        int gain = _sim.Cities.Earnings(town, owner, route);
        Player(owner).Spent[(int)u.Carry[i]] += u.CarryAmount[i];
        u.Carry[i] = town.Def.Sells;
        u.CarryAmount[i] = gain;
        if (gain > 0) GoToStorage(i);
        else DecideTrade(i, town);
    }

    /// <summary>True if unit i is near enough the town's market to trade: within <see cref="CityStateSystem.TradeReach"/> columns and levels.</summary>
    public bool AtTown(int i, CityState town)
    {
        var u = U;
        int d = Math.Max(Math.Abs(u.X[i].FloorToInt() - town.X), Math.Abs(u.Z[i].FloorToInt() - town.Z));
        return d <= CityStateSystem.TradeReach && Math.Abs(u.Y[i] - town.Y) <= CityStateSystem.TradeReach;
    }

    /// <summary>
    /// The player's finished storage building nearest to column (x, z) (by straight line, then id) holding at
    /// least <paramref name="amount"/> of the good; null if there is none.
    /// </summary>
    public Building? StorageHolding(int player, Good good, int amount, int x, int z)
    {
        Building? best = null;
        long bestD = long.MaxValue;
        foreach (var b in Buildings.All)
        {
            if (b.Owner != player || !b.Active || !b.Def.Storage || b.Stock[(int)good] < amount) continue;
            long d = b.CentreDistance2(x, z);
            if (d >= bestD) continue;
            bestD = d;
            best = b;
        }
        return best;
    }
}
