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
        Ponies(sim, hall, issue);
        var cart = Hauler(sim, hall, pony: false);
        if (cart == null) return;
        bool sawmill = false;
        int stores = 0, queued = 0;
        foreach (var b in sim.Buildings.All)
        {
            if (b.Owner != Player || !b.Active) continue;
            if (b.Def.Kind == BuildingKind.Workshop && b.Def.Makes == Good.Planks) sawmill = true;
            if (b.Def.Storage && b != hall) stores++;
            foreach (int q in b.Queue)
                if (sim.Content.Units[q].IsCart) queued++;
        }
        int carts = 0;
        var u = sim.Units;
        for (int i = 0; i < u.Count; i++)
            if (u.Owner[i] == Player && sim.Content.Units[u.Def[i]].IsCart) carts++;

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

    /// <summary>Pack ponies it keeps once it has Handcarts and a sawmill (#149).</summary>
    public const int MostPonies = 2;

    /// <summary>The first cart (or pack pony) the hall trains that the player has unlocked, or null.</summary>
    private UnitDef? Hauler(Simulation sim, Building hall, bool pony)
    {
        foreach (string id in hall.Def.Trains)
            if (sim.Content.TryGetUnit(id, out var d) && d.IsHauler && d.Runs == pony && sim.Economy.Unlocked(Player, d)) return d;
        return null;
    }

    /// <summary>
    /// Pack ponies: once it has Handcarts (a pony unlocked) and a finished sawmill, it trains up to <see cref="MostPonies"/>
    /// at the hall to run goods to its far sites and its workshops' goods home. They find their work on their own.
    /// </summary>
    private void Ponies(Simulation sim, Building hall, Action<Command> issue)
    {
        var pony = Hauler(sim, hall, pony: true);
        if (pony == null) return;
        bool sawmill = false;
        int ponies = 0;
        foreach (var b in sim.Buildings.All)
        {
            if (b.Owner != Player) continue;
            if (b.Active && b.Def.Kind == BuildingKind.Workshop && b.Def.Makes == Good.Planks) sawmill = true;
            foreach (int q in b.Queue)
                if (sim.Content.Units[q].Runs) ponies++;
        }
        var u = sim.Units;
        for (int i = 0; i < u.Count; i++)
            if (u.Owner[i] == Player && sim.Content.Units[u.Def[i]].Runs) ponies++;
        if (!sawmill || ponies >= MostPonies) return;
        var econ = sim.Economy;
        if (econ.Housing(Player) <= econ.Population(Player) + QueuedAll(sim) || !Affordable(sim, pony.Cost)) return;
        issue(new TrainCommand(Player, hall.Id, pony.Id));
        Note(sim, $"trains a {pony.Name.ToLowerInvariant()} to run goods to its sites and workshops");
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
