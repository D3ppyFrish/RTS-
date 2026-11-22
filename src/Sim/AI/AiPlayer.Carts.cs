using System;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.AI;

// Carts at the smart levels (Easy and up). Once it has a finished sawmill (a cart's planks) and at least
// CartStores storage buildings besides its town hall, it trains up to MostCarts carts at the hall, one at a
// time while it has room for them and the goods. Once the first is out it gives the hall Keep stocked rules
// (HallKeep of each gathered good), so the carts bring the goods piling up in its far storehouses home, where
// raiders reach them last and its workshops, smelter and barracks are. Its workers leave the supply duty to
// the carts: Easy and Normal take supply off their default list (Hard's lists never had it).
public sealed partial class AiPlayer
{
    /// <summary>Storage buildings it has besides the town hall before it trains carts.</summary>
    public const int CartStores = 2;
    /// <summary>Carts it keeps: the two a building takes on supply duty at a time.</summary>
    public const int MostCarts = 2;
    /// <summary>How much of each gathered good its town hall's rules keep there.</summary>
    public const int HallKeep = 300;

    /// <summary>The goods its town hall's rules keep: what its workers gather and leave in far storehouses.</summary>
    public static readonly Good[] HaulGoods = { Good.Food, Good.Wood, Good.Stone, Good.IronOre, Good.Coal, Good.Gold };

    /// <summary>Easy and Normal's default list once its carts do the supplying: the standard one without supply.</summary>
    private static readonly ulong NoSupply = Automation.WorkPriorities.Pack(Automation.WorkKind.Build, Automation.WorkKind.Farm,
        Automation.WorkKind.Smelt, Automation.WorkKind.Needed);

    /// <summary>The smart levels: carts, and Keep stocked rules on the town hall for them (see the top of this file).</summary>
    private void Carts(Simulation sim, Building hall, Action<Command> issue)
    {
        if (!Smart || !hall.Active) return;
        var cart = FirstTrained(sim, hall, UnitRole.Hauler);
        if (cart == null) return;
        bool sawmill = false;
        int stores = 0, queued = 0;
        foreach (var b in sim.Buildings.All)
        {
            if (b.Owner != Player || !b.Active) continue;
            if (b.Def.Kind == BuildingKind.Workshop && b.Def.Makes == Good.Planks) sawmill = true;
            if (b.Def.Storage && b != hall) stores++;
            foreach (int q in b.Queue)
                if (sim.Content.Units[q].IsHauler) queued++;
        }
        int carts = 0;
        var u = sim.Units;
        for (int i = 0; i < u.Count; i++)
            if (u.Owner[i] == Player && sim.Content.Units[u.Def[i]].IsHauler) carts++;

        if (carts > 0) KeepHallStocked(sim, hall, issue);
        if (!sawmill || stores < CartStores || carts + queued >= MostCarts || queued > 0) return;
        var econ = sim.Economy;
        if (econ.Housing(Player) <= econ.Population(Player) + QueuedAll(sim) || !Affordable(sim, cart.Cost)) return;
        issue(new TrainCommand(Player, hall.Id, cart.Id));
        Note(sim, $"trains a {cart.Name.ToLowerInvariant()} to bring goods home from its storehouses");
    }

    /// <summary>Gives the town hall its rules for the carts (once), and takes supply off Easy and Normal's default list.</summary>
    private void KeepHallStocked(Simulation sim, Building hall, Action<Command> issue)
    {
        foreach (var g in HaulGoods)
            if (hall.Supply[(int)g] != HallKeep) issue(new SupplyCommand(Player, hall.Id, g, HallKeep));
        if (!Hard && sim.Economy.Player(Player).DefaultPriorities == Automation.WorkPriorities.Standard)
            issue(new DefaultPriorityCommand(Player, NoSupply));
    }

    /// <summary>Units waiting to be trained in all its buildings.</summary>
    private int QueuedAll(Simulation sim)
    {
        int n = 0;
        foreach (var b in sim.Buildings.All)
            if (b.Owner == Player) n += b.Queue.Count;
        return n;
    }
}
