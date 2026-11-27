using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>City-states: the goblin town, caravan trade and mercenaries (issue #14), and conquering it by its totem (issue #67).</summary>
public class CityStateTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Tps = Simulation.TicksPerSecond;
    private const int Settlers = 0, Orcs = 1, Third = 2;
    // The goblin town's market, and the Settlers' town hall: its middle is 120 blocks west of the market.
    private const int TownX = 131, TownZ = 79;
    private const int HallX = 8, HallZ = 76;

    private static Simulation Flat(int size = 160) => TestWorlds.Sim(TestWorlds.Flat(size, 24, size, Ground));

    private static CityState PlaceTown(Simulation sim, int x = TownX, int z = TownZ) =>
        sim.Cities.Place(sim.Content.CityState("goblins"), x, z) ?? throw new InvalidOperationException("no ground for the town");

    private static Building Hall(Simulation sim, int owner, int x = HallX, int z = HallZ, int food = 0, int wood = 0, int iron = 0, int gold = 0)
    {
        var b = sim.Economy.PlaceFinished(owner, sim.Content.Building("town_hall"), x, z) ?? throw new InvalidOperationException("the hall does not fit");
        b.Stock[(int)Good.Food] = food;
        b.Stock[(int)Good.Wood] = wood;
        b.Stock[(int)Good.Iron] = iron;
        b.Stock[(int)Good.Gold] = gold;
        return b;
    }

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

    private static UnitDef DefOf(Simulation sim, int id) => sim.Content.Units[sim.Units.Def[Index(sim, id)]];

    /// <summary>Applies the command now (in one step) and returns.</summary>
    private static void Do(Simulation sim, Command c)
    {
        sim.Schedule(sim.Tick, c);
        sim.Step();
    }

    /// <summary>Steps until the condition holds; fails the test if it takes longer than maxTicks. Returns the ticks taken.</summary>
    private static int RunUntil(Simulation sim, Func<bool> done, int maxTicks, string what)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return t;
            sim.Step();
        }
        Assert.Fail($"Not done after {maxTicks} ticks: {what}");
        return maxTicks;
    }

    /// <summary>Spawns units of one type in a block of rows of four, from column (x, z) eastward and southward.</summary>
    private static int[] Squad(Simulation sim, int owner, string unit, int x, int z, int count) =>
        Enumerable.Range(0, count).Select(k => sim.SpawnUnit(owner, unit, x + k % 4, z + k / 4 * 2)).ToArray();

    /// <summary>The ids of the player's units of that type, in order.</summary>
    private static List<int> UnitsOf(Simulation sim, int owner, string unit)
    {
        var u = sim.Units;
        return Enumerable.Range(0, u.Count).Where(i => u.Owner[i] == owner && sim.Content.Units[u.Def[i]].Id == unit)
            .Select(i => u.Id[i]).OrderBy(id => id).ToList();
    }

    /// <summary>The town's standing totem (fails the test if it is down).</summary>
    private static Building Totem(Simulation sim, CityState town) =>
        sim.Cities.Totem(town) ?? throw new InvalidOperationException("the totem is down");

    /// <summary>
    /// A squad of the player's spearmen breaks the town's totem, and the totem stands again; the spearmen that
    /// are left are then removed, so they do not count against housing. Returns the ticks it took.
    /// </summary>
    private static int Conquer(Simulation sim, CityState town, int player, int spearmen = 12)
    {
        var squad = Squad(sim, player, "spearman", town.X - 2, town.Z + 3, spearmen);
        Do(sim, new AttackBuildingCommand(player, squad, Totem(sim, town).Id));
        int ticks = RunUntil(sim, () => town.Holder == player && town.TotemId != 0, 90 * Tps, "the squad breaks the totem and it stands again");
        foreach (int id in squad) sim.Units.Remove(id);
        return ticks;
    }

    // --- Issue #67's checks ------------------------------------------------------------------------

    /// <summary>
    /// The goblin town starts neutral: a totem on the market and six guards, all wild. The Settlers' spearmen
    /// break the totem, and the goblins are theirs: the guards change sides on the spot, and the totem stands
    /// again five seconds later, in the Settlers' hands. A minute on, a tribute caravan of 10 gold sets out,
    /// walks it to the Settlers' town hall, and goes home to the market, where it is gone. Neither the guards
    /// nor the caravan count against the Settlers' housing.
    /// </summary>
    [Fact]
    public void BreakingTheTotemMakesTheGoblinsYourVassalsWhoSendTribute()
    {
        var sim = Flat();
        var hall = Hall(sim, Settlers);
        var town = PlaceTown(sim);
        var goblins = town.Def;
        var totem = Totem(sim, town);
        Assert.Equal((-1, Simulation.WildPlayer, BuildingKind.Totem), (town.Holder, totem.Owner, totem.Def.Kind));
        Assert.True(totem.Covers(town.X, town.Z), "the totem stands on the market");
        Assert.Equal(6, town.Guards.Count());
        Assert.All(town.Guards, id => Assert.Equal(Simulation.WildPlayer, sim.Units.Owner[Index(sim, id)]));
        Assert.All(town.Guards, id => Assert.Equal(town.Id, sim.Units.Town[Index(sim, id)]));
        Assert.Equal(new[] { "goblin_cutthroat", "goblin_cutthroat", "goblin_cutthroat", "goblin_slinger", "goblin_slinger", "goblin_slinger" },
            town.Guards.Select(id => DefOf(sim, id).Id));

        var squad = Squad(sim, Settlers, "spearman", town.X - 2, town.Z + 3, 12);
        Do(sim, new AttackBuildingCommand(Settlers, squad, totem.Id));
        RunUntil(sim, () => sim.Buildings.Get(totem.Id) == null, 60 * Tps, "the spearmen break the totem");
        sim.Step(); // the town sees its totem is gone
        Assert.Equal(Settlers, town.Holder);
        Assert.Equal(0, town.TotemId);
        Assert.All(town.Guards, id => Assert.Equal(Settlers, sim.Units.Owner[Index(sim, id)]));
        Assert.Contains(sim.Events.All.Where(e => e.Player == Settlers), e => e.Kind == GameEventKind.TownConquered && e.Subject == town.Id);

        RunUntil(sim, () => town.TotemId != 0, goblins.TotemRespawnTicks + Tps, "the totem stands again");
        var again = Totem(sim, town);
        Assert.NotEqual(totem.Id, again.Id);
        Assert.Equal((Settlers, town.X - 1, town.Z - 1), (again.Owner, again.X, again.Z));
        Assert.Equal(again.Def.Blocks.Count, again.Placed);
        Assert.Equal(again.Def.Wall, sim.World.Get(town.X, town.Y + 3, town.Z)); // the pole is whole, rubble cleared
        foreach (int id in squad) sim.Units.Remove(id);
        sim.Step();

        // Housing: only the Settlers' own units count, not the goblins'.
        Assert.Equal(0, sim.Economy.Population(Settlers));

        // Tribute: a caravan sets out a minute after the town was taken, with 10 gold for the town hall.
        RunUntil(sim, () => town.Caravans.Count > 0, goblins.TributeTicks + Tps, "a tribute caravan sets out");
        int caravan = town.Caravans[0];
        int c = Index(sim, caravan);
        Assert.Equal((Settlers, town.Id, Good.Gold, 10), (sim.Units.Owner[c], sim.Units.Town[c], sim.Units.Carry[c], sim.Units.CarryAmount[c]));
        Assert.Equal(UnitJob.Deliver, sim.Units.Job[c]);
        Assert.Equal(0, sim.Economy.Population(Settlers));
        Assert.Contains(sim.Events.All.Where(e => e.Player == Settlers), e => e.Kind == GameEventKind.TributeSent);
        // It takes no orders.
        Do(sim, new StopCommand(Settlers, new[] { caravan }));
        Assert.Equal(UnitJob.Deliver, sim.Units.Job[Index(sim, caravan)]);

        RunUntil(sim, () => hall.Stock[(int)Good.Gold] == 10, 90 * Tps, "the tribute reaches the town hall");
        RunUntil(sim, () => !sim.Units.Exists(caravan), 90 * Tps, "the empty caravan goes home");
        Assert.DoesNotContain(caravan, town.Caravans);
        Assert.Equal(6, town.Guards.Count());
    }

    /// <summary>
    /// The Settlers hold the goblins until the Orcs break the Settlers' totem: then the town, its guards and
    /// its totem are the Orcs', and the Settlers hear they lost it.
    /// </summary>
    [Fact]
    public void BreakingAHeldTotemTakesTheTownOver()
    {
        var sim = Flat();
        Hall(sim, Settlers);
        Hall(sim, Orcs, HallX, 20);
        var town = PlaceTown(sim);
        Conquer(sim, town, Settlers);
        Assert.Equal(Settlers, Totem(sim, town).Owner);

        Conquer(sim, town, Orcs, spearmen: 16);
        Assert.Equal(Orcs, town.Holder);
        Assert.Equal(Orcs, Totem(sim, town).Owner);
        Assert.All(town.Guards, id => Assert.Equal(Orcs, sim.Units.Owner[Index(sim, id)]));
        Assert.Contains(sim.Events.All.Where(e => e.Player == Settlers), e => e.Kind == GameEventKind.TownLost && e.Subject == town.Id);
        Assert.Contains(sim.Events.All.Where(e => e.Player == Orcs), e => e.Kind == GameEventKind.TownConquered && e.Subject == town.Id);
        // Its own units do not attack their own totem.
        var squad = Squad(sim, Orcs, "spearman", town.X - 2, town.Z + 3, 2);
        Do(sim, new AttackBuildingCommand(Orcs, squad, town.TotemId));
        Assert.All(squad, id => Assert.Equal(0, sim.Units.TargetBuilding[Index(sim, id)]));
    }

    /// <summary>
    /// Neutral guards fight soldiers that come into the town, but let workers and caravans walk through: a
    /// worker and a caravan cross the square unhurt, while a spearman that walks in is set on. Soldiers do not
    /// pick a fight with the guards on their own, and a guard that chased one out goes back to its post.
    /// </summary>
    [Fact]
    public void NeutralGuardsFightSoldiersButLetWorkersAndCaravansThrough()
    {
        var sim = Flat();
        var town = PlaceTown(sim);
        int z = town.Z + 3;
        int worker = sim.SpawnUnit(Settlers, "worker", town.X - 14, z);
        int caravan = sim.SpawnUnit(Settlers, "caravan", town.X - 14, z + 1);
        // A spearman waits outside the guards' reach: nobody fights.
        int outside = sim.SpawnUnit(Settlers, "spearman", town.X - CityStateSystem.TownRadius - town.Def.GuardReach - 3, z - 6);
        Do(sim, new MoveCommand(Settlers, new[] { worker, caravan }, town.X + 14, z));
        RunUntil(sim, () => sim.Units.X[Index(sim, worker)] > Fix64.FromInt(town.X + 12) && sim.Units.X[Index(sim, caravan)] > Fix64.FromInt(town.X + 12),
            40 * Tps, "the worker and the caravan cross the town");
        sim.Run(2 * Tps);
        Assert.Equal(sim.Content.Unit("worker").MaxHp, sim.Units.Hp[Index(sim, worker)]);
        Assert.Equal(sim.Content.Unit("caravan").MaxHp, sim.Units.Hp[Index(sim, caravan)]);
        Assert.Equal(sim.Content.Unit("spearman").MaxHp, sim.Units.Hp[Index(sim, outside)]);
        Assert.Equal(0, sim.Units.Target[Index(sim, outside)]);
        Assert.All(town.Guards, id => Assert.Equal(0, sim.Units.Target[Index(sim, id)]));

        // A spearman walks into the square: the guards set on it.
        int intruder = sim.SpawnUnit(Settlers, "spearman", town.X - 14, z);
        Do(sim, new MoveCommand(Settlers, new[] { intruder }, town.X - 3, z));
        RunUntil(sim, () => !sim.Units.Exists(intruder) || sim.Units.Hp[Index(sim, intruder)] < sim.Content.Unit("spearman").MaxHp,
            20 * Tps, "the guards attack the spearman");
        // Once it is gone, the guards go back to their posts.
        RunUntil(sim, () => !sim.Units.Exists(intruder), 30 * Tps, "the guards kill the spearman");
        RunUntil(sim, () => town.Guards.All(id => sim.Units.Order[Index(sim, id)] == UnitOrder.Idle && sim.Units.Target[Index(sim, id)] == 0), 30 * Tps,
            "the guards settle");
        foreach (int id in town.Guards)
        {
            int g = Index(sim, id);
            Assert.InRange(town.ColumnDistance(sim.Units.X[g].FloorToInt(), sim.Units.Z[g].FloorToInt()), 0, 0);
        }
    }

    /// <summary>
    /// Vassal goblins fight their holder's enemies: once the Settlers hold the town, its guards set on an Orc
    /// spearman that comes in but leave an Orc worker alone. Only the holder hires the town's mercenaries.
    /// </summary>
    [Fact]
    public void VassalGuardsFightTheHoldersEnemiesAndOnlyTheHolderHires()
    {
        var sim = Flat();
        Hall(sim, Settlers, food: 1000, wood: 100, iron: 100, gold: 100);
        Hall(sim, Orcs, HallX, 20, food: 1000, wood: 100, iron: 100, gold: 100);
        var town = PlaceTown(sim);

        // Neutral: nobody hires.
        Do(sim, new HireCommand(Settlers, town.Id, "goblin_cutthroat"));
        Assert.Empty(UnitsOf(sim, Settlers, "goblin_cutthroat"));

        Conquer(sim, town, Settlers);
        Do(sim, new HireCommand(Orcs, town.Id, "goblin_slinger")); // not the holder
        Assert.Empty(UnitsOf(sim, Orcs, "goblin_slinger"));
        Do(sim, new HireCommand(Settlers, town.Id, "goblin_cutthroat"));
        var hired = Assert.Single(UnitsOf(sim, Settlers, "goblin_cutthroat").Where(id => sim.Units.Town[Index(sim, id)] == 0));
        int m = Index(sim, hired);
        Assert.InRange(Math.Abs(sim.Units.X[m].FloorToInt() - town.X), 0, 2);
        Assert.InRange(Math.Abs(sim.Units.Z[m].FloorToInt() - town.Z), 0, 2);
        Assert.Equal(town.Def.Pool - 1, town.Ready);
        Assert.Equal(1, sim.Economy.Population(Settlers)); // the hired one is the Settlers' own: it needs housing
        // ...and takes orders.
        Do(sim, new MoveCommand(Settlers, new[] { hired }, town.X - 20, town.Z));
        Assert.Equal(UnitOrder.Move, sim.Units.Order[Index(sim, hired)]);

        int worker = sim.SpawnUnit(Orcs, "worker", town.X - 14, town.Z + 3);
        int spearman = sim.SpawnUnit(Orcs, "spearman", town.X - 14, town.Z - 3);
        Do(sim, new MoveCommand(Orcs, new[] { worker }, town.X - 3, town.Z + 3));
        Do(sim, new MoveCommand(Orcs, new[] { spearman }, town.X - 3, town.Z - 3));
        RunUntil(sim, () => !sim.Units.Exists(spearman), 40 * Tps, "the Settlers' guards kill the Orc spearman");
        Assert.Equal(sim.Content.Unit("worker").MaxHp, sim.Units.Hp[Index(sim, worker)]);
        // The guards take no orders from their holder.
        int guard = town.Guards.First();
        Do(sim, new MoveCommand(Settlers, new[] { guard }, town.X - 30, town.Z));
        Assert.NotEqual(town.X - 30, sim.Units.OrderX[Index(sim, guard)]);
    }

    /// <summary>
    /// Issue #44: hurt units near a goblin town their owner holds heal there as at home (the home rate, wounds
    /// bound), once out of the fight. Before the town is taken, other players' units there, and the holder's units
    /// past the town's healing reach, heal only at the slow rate.
    /// </summary>
    [Fact]
    public void UnitsHealFastAtAGoblinTownTheirOwnerHolds()
    {
        var sim = Flat();
        Hall(sim, Settlers);
        Hall(sim, Orcs, HallX, 20);
        var town = PlaceTown(sim);
        var rules = sim.Content.Combat;
        int reach = town.Def.HealRange;
        Assert.Equal(6, reach);
        int edge = town.X - CityStateSystem.TownRadius - reach; // the last column the healing reaches, west of the square

        int Hp(int id) => sim.Units.Hp[Index(sim, id)];
        int[] Hurt(params (int owner, int x, int z)[] spots)
        {
            var ids = spots.Select(s => sim.SpawnUnit(s.owner, "worker", s.x, s.z)).ToArray();
            foreach (int id in ids) sim.Units.Hp[Index(sim, id)] = sim.Content.Unit("worker").MaxHp / 2;
            return ids;
        }
        int Gained(int[] ids, int seconds, out int[] after)
        {
            var before = ids.Select(Hp).ToArray();
            sim.Run(rules.RecoveryDelayTicks + seconds * Tps);
            after = ids.Select(Hp).ToArray();
            return after[0] - before[0];
        }

        // Neutral: nobody heals fast at the town.
        Assert.Null(sim.Cities.HealingTown(Settlers, town.X, town.Y, town.Z));
        var neutral = Hurt((Settlers, town.X - 3, town.Z + 3));
        int slow = Gained(neutral, 10, out _);
        foreach (int id in neutral) sim.Units.Remove(id);

        Conquer(sim, town, Settlers);
        Assert.Same(town, sim.Cities.HealingTown(Settlers, edge, town.Y, town.Z));
        Assert.Null(sim.Cities.HealingTown(Settlers, edge - 1, town.Y, town.Z));
        Assert.Null(sim.Cities.HealingTown(Orcs, town.X, town.Y, town.Z));

        // The holder's units in the town and at the edge of its reach, the holder's one step past it, and an Orc's.
        var units = Hurt((Settlers, town.X - 3, town.Z + 3), (Settlers, edge, town.Z + 2), (Settlers, edge - 2, town.Z - 2), (Orcs, town.X + 3, town.Z - 3));
        int fast = Gained(units, 10, out var hp);
        int half = sim.Content.Unit("worker").MaxHp / 2;
        Assert.True(fast > slow * 3, $"in the held town it gained {fast}, neutral {slow}");
        Assert.True(hp[1] - half > (hp[2] - half) * 3, $"at the edge {hp[1] - half}, past it {hp[2] - half}");
        Assert.True(hp[0] - half > (hp[3] - half) * 3, $"the holder's {hp[0] - half}, the Orcs' {hp[3] - half}");
        Assert.True(sim.Combat.Healing(Index(sim, units[2]), out bool past) && !past);
        Assert.True(sim.Combat.Healing(Index(sim, units[0]), out bool there) && there);
    }

    /// <summary>A fallen guard is replaced, one at a time, every 45 seconds, for whoever holds the town.</summary>
    [Fact]
    public void FallenGuardsAreReplaced()
    {
        var sim = Flat();
        var town = PlaceTown(sim);
        var first = town.Guards.ToArray();
        sim.Units.Remove(first[0]);
        sim.Units.Remove(first[4]);
        sim.Step();
        Assert.Equal(4, town.Guards.Count());
        sim.Run(town.Def.GuardRefillTicks);
        Assert.Equal(5, town.Guards.Count());
        int fresh = town.Guards.First();
        Assert.DoesNotContain(fresh, first);
        Assert.Equal(("goblin_cutthroat", Simulation.WildPlayer, town.Id), (DefOf(sim, fresh).Id, sim.Units.Owner[Index(sim, fresh)], sim.Units.Town[Index(sim, fresh)]));
        sim.Run(town.Def.GuardRefillTicks);
        Assert.Equal(6, town.Guards.Count());
        sim.Run(town.Def.GuardRefillTicks);
        Assert.Equal(6, town.Guards.Count());
    }

    /// <summary>
    /// A caravan carries 20 food to the market, 120 blocks away, and brings back 6 iron (5 per 100 blocks);
    /// its holder's caravans bring back half as much again.
    /// </summary>
    [Fact]
    public void ACaravanSellsItsLoadAtTheMarket()
    {
        var sim = Flat();
        var hall = Hall(sim, Settlers, food: 1000);
        var town = PlaceTown(sim);
        Assert.Equal(120, CityStateSystem.Route(hall, town));
        int caravan = sim.SpawnUnit(Settlers, "caravan", HallX + 3, HallZ + 9);
        Do(sim, new TradeCommand(Settlers, new[] { caravan }, town.Id));
        Assert.Equal(UnitJob.Trade, sim.Units.Job[Index(sim, caravan)]);
        RunUntil(sim, () => sim.Units.Carry[Index(sim, caravan)] == Good.Iron, 120 * Tps, "the caravan sells its load");
        int c = Index(sim, caravan);
        Assert.True(sim.Economy.AtTown(c, town), "the caravan sold its load at the market");
        Assert.Equal(6, sim.Units.CarryAmount[c]);
        RunUntil(sim, () => hall.Stock[(int)Good.Iron] == 6, 120 * Tps, "the iron comes home");
        Assert.Equal(sim.Content.Unit("caravan").MaxHp, sim.Units.Hp[Index(sim, caravan)]);
    }

    // --- Trade -------------------------------------------------------------------------------------

    /// <summary>
    /// A caravan waits until a storage holds a whole load. Only caravans take a trade order. One stopped with
    /// a load unloads it before trading again, so every load it sells was carried along the route it is paid for.
    /// </summary>
    [Fact]
    public void ACaravanTradesOnlyWholeLoadsAndWorkersDoNotTrade()
    {
        var sim = Flat();
        var hall = Hall(sim, Settlers, food: 10);
        var town = PlaceTown(sim);
        int caravan = sim.SpawnUnit(Settlers, "caravan", HallX + 3, HallZ + 9);
        int worker = sim.SpawnUnit(Settlers, "worker", HallX + 5, HallZ + 9);
        Do(sim, new TradeCommand(Settlers, new[] { caravan, worker }, town.Id));
        sim.Run(2 * Tps);
        Assert.Equal((UnitJob.Trade, JobPhase.Wait), (sim.Units.Job[Index(sim, caravan)], sim.Units.Phase[Index(sim, caravan)]));
        Assert.Equal(UnitJob.None, sim.Units.Job[Index(sim, worker)]);
        Assert.Equal(10, hall.Stock[(int)Good.Food]);

        hall.Stock[(int)Good.Food] = 30;
        RunUntil(sim, () => sim.Units.CarryAmount[Index(sim, caravan)] == 20, 5 * Tps, "the caravan takes a load");
        Assert.Equal(10, hall.Stock[(int)Good.Food]);
        Assert.Equal(120, sim.Units.JobX[Index(sim, caravan)]); // the route it will be paid for

        // Stopped on the way, it keeps the load but forgets the route: told to trade again, it unloads first.
        sim.Run(5 * Tps);
        Do(sim, new StopCommand(Settlers, new[] { caravan }));
        Assert.Equal((UnitJob.None, 20), (sim.Units.Job[Index(sim, caravan)], sim.Units.CarryAmount[Index(sim, caravan)]));
        Do(sim, new TradeCommand(Settlers, new[] { caravan }, town.Id));
        Assert.Equal(JobPhase.ToStore, sim.Units.Phase[Index(sim, caravan)]);
        RunUntil(sim, () => hall.Stock[(int)Good.Food] == 30, 30 * Tps, "the caravan unloads");
        RunUntil(sim, () => sim.Units.Phase[Index(sim, caravan)] == JobPhase.ToWork, 5 * Tps, "the caravan sets off again");
        Assert.Equal((Good.Food, 20, 120), (sim.Units.Carry[Index(sim, caravan)], sim.Units.CarryAmount[Index(sim, caravan)], sim.Units.JobX[Index(sim, caravan)]));
    }

    /// <summary>What a load fetches grows with the route: 5 iron per 100 blocks, and half as much again for the holder.</summary>
    [Fact]
    public void TradeEarningsGrowWithTheRoute()
    {
        var sim = Flat();
        Hall(sim, Settlers);
        var town = PlaceTown(sim);
        Assert.Equal((0, 5, 10, 25), (sim.Cities.Earnings(town, Settlers, 0), sim.Cities.Earnings(town, Settlers, 100),
            sim.Cities.Earnings(town, Settlers, 200), sim.Cities.Earnings(town, Settlers, 500)));
        Conquer(sim, town, Settlers);
        Assert.Equal((0, 7, 15, 37), (sim.Cities.Earnings(town, Settlers, 0), sim.Cities.Earnings(town, Settlers, 100),
            sim.Cities.Earnings(town, Settlers, 200), sim.Cities.Earnings(town, Settlers, 500)));
        Assert.Equal(10, sim.Cities.Earnings(town, Orcs, 200));
    }

    // --- Mercenaries -------------------------------------------------------------------------------

    /// <summary>
    /// The holder hires the town's own mercenaries only, up to the pool of six, with room for them; one more is
    /// ready every 30 seconds.
    /// </summary>
    [Fact]
    public void TheHolderHiresFromAPoolThatRefills()
    {
        var sim = Flat();
        var hall = Hall(sim, Settlers, food: 2000, wood: 500, iron: 500, gold: 500);
        var town = PlaceTown(sim);
        Conquer(sim, town, Settlers);
        Assert.Equal(0, sim.Economy.Population(Settlers));

        Do(sim, new HireCommand(Settlers, town.Id, "spearman")); // not one of the goblins'
        Do(sim, new HireCommand(Settlers, town.Id, "wolf"));
        Assert.Equal(0, sim.Economy.Population(Settlers));

        for (int k = 0; k < 7; k++) sim.Schedule(sim.Tick, new HireCommand(Settlers, town.Id, k % 2 == 0 ? "goblin_cutthroat" : "goblin_slinger"));
        sim.Step();
        Assert.Equal(0, town.Ready);
        var hired = Enumerable.Range(0, sim.Units.Count).Where(i => sim.Units.Owner[i] == Settlers && sim.Units.Town[i] == 0).ToList();
        Assert.Equal(6, hired.Count);
        Assert.Equal(3, hired.Count(i => sim.Content.Units[sim.Units.Def[i]].Id == "goblin_cutthroat"));
        // Each stepped out on its own spot beside the market.
        Assert.Equal(6, hired.Select(i => (sim.Units.X[i].FloorToInt(), sim.Units.Z[i].FloorToInt())).Distinct().Count());

        sim.Run(town.Def.RefillTicks - 2);
        Assert.Equal(0, town.Ready);
        sim.Step();
        Assert.Equal(1, town.Ready);

        // No room: the hall houses 25.
        Squad(sim, Settlers, "worker", HallX + 2, HallZ + 10, 19);
        Do(sim, new HireCommand(Settlers, town.Id, "goblin_slinger"));
        Assert.Equal(1, town.Ready);
        Assert.Equal(25, sim.Economy.Population(Settlers));
        Assert.Equal(2000 - 6 * 40, hall.Stock[(int)Good.Food]);
    }

    // --- The town ----------------------------------------------------------------------------------

    /// <summary>
    /// Every match has a goblin town about as far from one start as the other: a levelled square with a hut at
    /// each corner, its door toward the open market, and a wolf lair just beyond it. No building may go on it.
    /// The ogres' hold (issue #46) is placed the same way, but well away from the goblins, with a stone giant's
    /// lair beyond it.
    /// </summary>
    [Fact]
    public void EveryMatchHasATownForEachCityStateWithItsLair()
    {
        var sim = new Simulation(1234, 512, 64, 512);
        sim.PlaceStartingBuildings();
        sim.SpawnStartingUnits(24);
        var startLairs = sim.SpawnLairs();
        var towns = sim.SpawnCities();
        Assert.Equal(new[] { "goblins", "ogres", "trolls", "centaurs", "merfolk" }, towns.Select(t => t.Def.Id));
        var town = towns[0];
        Assert.Same(town, sim.Cities.Get(town.Id));
        Assert.Equal("goblins", town.Def.Id);
        var d = sim.Map.Starts.Select(s => IntMath.Distance(s.X, s.Z, town.X, town.Z)).ToArray();
        Assert.InRange(Math.Abs(d[0] - d[1]), 0, 4);
        Assert.InRange(d[0], 150, 250);

        var w = sim.World;
        int r = CityStateSystem.TownRadius;
        for (int dz = -r; dz <= r; dz++)
        for (int dx = -r; dx <= r; dx++)
        {
            int x = town.X + dx, z = town.Z + dz;
            Assert.Same(town, sim.Cities.At(x, z));
            bool hut = Math.Abs(dx) > 3 && Math.Abs(dz) > 3, plinth = Math.Abs(dx) <= 1 && Math.Abs(dz) <= 1;
            if (!hut && !plinth) Assert.Equal(town.Y, Walk.GroundLevel(w, x, z)); // the open square is level
        }
        Assert.Null(sim.Cities.At(town.X + r + 1, town.Z));
        // A hut: walls and a roof of what the data names, and a door toward the market.
        int hx = town.X - r, hz = town.Z - r;
        Assert.Equal(town.Def.Wall, w.Get(hx, town.Y, hz));
        Assert.Equal(town.Def.Roof, w.Get(hx, town.Y + 3, hz));
        Assert.Equal(BlockType.Door, w.Get(hx + 2, town.Y, hz + 3));
        Assert.Equal(BlockType.Air, w.Get(hx + 1, town.Y, hz + 1));

        // The totem on the market, and the guards round it: wild, as nobody holds the town yet.
        var totem = sim.Cities.Totem(town)!;
        Assert.Equal((Simulation.WildPlayer, town.X - 1, town.Y, town.Z - 1, "goblin_totem"), (totem.Owner, totem.X, totem.Y, totem.Z, totem.Def.Id));
        Assert.Equal(6, town.Guards.Count());
        Assert.All(town.Guards, id => Assert.Equal(0, town.ColumnDistance(sim.Units.X[sim.Units.IndexOf(id)].FloorToInt(), sim.Units.Z[sim.Units.IndexOf(id)].FloorToInt())));

        var lair = sim.Monsters.GetLair(town.LairId);
        Assert.NotNull(lair);
        // The two start lairs, the dusk tree's warden and the towns', then those spread across the map (placed last).
        Assert.Same(lair, sim.Monsters.Lairs[3]);
        // (and the cavern's last of all).
        Assert.All(sim.Monsters.Lairs.Skip(5), l => Assert.True(l.Monster.Lair!.Biome != null || l.Monster.Lair.Cavern != CavernSpot.None));
        Assert.DoesNotContain(lair, startLairs);
        Assert.InRange(IntMath.Distance(town.X, town.Z, lair!.X, lair.Z), CityStateSystem.LairNear, CityStateSystem.LairFar);
        Assert.Equal(4, lair.Pack.Count);
        // Straight out past the town from the starts: further from both than the town is.
        foreach (var s in sim.Map.Starts)
            Assert.True(IntMath.Distance(s.X, s.Z, lair.X, lair.Z) > IntMath.Distance(s.X, s.Z, town.X, town.Z));

        var house = sim.Content.Building("house");
        Assert.Equal(PlaceResult.Crowded, sim.Economy.CanPlace(0, house, town.X - 1, town.Z - 1, out _, checkPlayer: false));
        Assert.Equal(PlaceResult.Crowded, sim.Economy.CanPlace(0, house, town.X + r + 1, town.Z, out _, checkPlayer: false));
        Assert.False(sim.Cities.SiteFits(town.X, town.Z));

        // The ogres' hold: fair to both starts too, but well apart from the goblins and further out, its idol on the market,
        // four guards and a stone giant's lair just beyond it.
        var hold = towns[1];
        var o = sim.Map.Starts.Select(s => IntMath.Distance(s.X, s.Z, hold.X, hold.Z)).ToArray();
        Assert.InRange(Math.Abs(o[0] - o[1]), 0, 12);
        Assert.All(o, d => Assert.True(d >= 144, $"the hold is {d} blocks from a start")); // never close to a start
        Assert.True(IntMath.Distance(town.X, town.Z, hold.X, hold.Z) > 120, $"the towns are {IntMath.Distance(town.X, town.Z, hold.X, hold.Z)} blocks apart");
        Assert.Equal("ogre_idol", sim.Cities.Totem(hold)!.Def.Id);
        // Neither town is near the dusk tree's warden.
        var warden = sim.Monsters.Lairs.Single(l => l.Monster.Lair!.DuskTree);
        Assert.All(towns, t => Assert.True(IntMath.Distance(t.X, t.Z, warden.X, warden.Z) >= CityStateSystem.LairNear * 3 / 2,
            $"{t.Def.Id} at {t.X},{t.Z} is near the dusk tree at {warden.X},{warden.Z}; hold at {hold.X},{hold.Z}"));
        Assert.Equal(4, hold.Guards.Count());
        var den = sim.Monsters.GetLair(hold.LairId);
        Assert.NotNull(den);
        Assert.Same(den, sim.Monsters.Lairs[4]);
        Assert.Equal("stone_giant", den!.Monster.Id);
        Assert.InRange(IntMath.Distance(hold.X, hold.Z, den.X, den.Z), CityStateSystem.LairNear, CityStateSystem.LairFar);
    }

    /// <summary>A town needs flat, dry ground without trees, away from buildings and dens.</summary>
    [Fact]
    public void ATownNeedsLevelDryOpenGround()
    {
        var sim = Flat(96);
        Assert.True(sim.Cities.SiteFits(48, 48));
        Assert.False(sim.Cities.SiteFits(10, 48)); // too near the edge
        TestWorlds.Box(sim.World, 40, Ground + 1, 40, 42, Ground + 3, 42, BlockType.Stone); // a rock, 3 high
        Assert.False(sim.Cities.SiteFits(48, 48));
        Assert.True(sim.Cities.SiteFits(48, 62));
        sim.World.Set(50, Ground, 66, BlockType.Water);
        Assert.False(sim.Cities.SiteFits(48, 62));
        TestWorlds.Tree(sim.World, 70, 30, 5, Ground);
        Assert.False(sim.Cities.SiteFits(61, 30)); // the trunk is two columns from the square
        Assert.True(sim.Cities.SiteFits(58, 30));
        sim.Economy.PlaceFinished(0, sim.Content.Building("house"), 30, 20);
        Assert.False(sim.Cities.SiteFits(51, 24)); // 11 columns from the house
        Assert.True(sim.Cities.SiteFits(52, 24));
        sim.Monsters.Place(sim.Content.Unit("wolf"), 75, 75);
        Assert.False(sim.Cities.SiteFits(60, 65)); // the den is 18 blocks from the market
    }

    // --- Command logs and scripted matches ---------------------------------------------------------

    [Fact]
    public void CityStateCommandsRoundTripInCommandLogs()
    {
        var log = new CommandLog { Seed = 1, SizeX = 64, SizeY = 16, SizeZ = 64, Ticks = 10, StartLairs = true, StartCities = true };
        log.Add(2, new TradeCommand(Settlers, new[] { 7, 8 }, 1));
        log.Add(3, new HireCommand(Orcs, 1, "goblin_slinger"));
        string text = log.ToString();
        Assert.Contains("\nstart-cities 1\n", text);
        Assert.Contains("\n2 trade 0 1 7,8\n", text);
        Assert.Contains("\n3 hire 1 1 goblin_slinger\n", text);
        var copy = CommandLog.Parse(text);
        Assert.True(copy.StartCities);
        Assert.Equal(log.Commands, copy.Commands);
        Assert.Equal(text, copy.ToString());
        Assert.False(CommandLog.Parse(text.Replace("start-cities 1\n", "")).StartCities);
        Assert.Throws<FormatException>(() => CommandLog.Parse(text.Replace("trade", "gift")));
        Assert.Throws<FormatException>(() => CommandLog.Parse(text.Replace("goblin_slinger", "goblin-slinger")));
    }

    /// <summary>Scripted matches trade with the goblins and go for their totem, and replay the same.</summary>
    [Fact]
    public void ScriptedMatchesDealWithTheTownsAndReplayTheSame()
    {
        var log = ScriptedMatch.Generate(3, 256, 64, 256, ticks: 6000, startUnits: 12);
        Assert.True(log.StartCities);
        Assert.Contains(log.Commands, c => c.Command is TradeCommand);
        var hashes = new ulong[2];
        int totemAttacks = 0;
        for (int run = 0; run < 2; run++)
        {
            var sim = log.CreateSimulation();
            Assert.Equal(5, sim.Cities.Towns.Count); // the goblins, the ogres, the trolls, the centaurs and the merfolk (no deep caves for the kobolds)
            totemAttacks = log.Commands.Count(c => c.Command is AttackBuildingCommand a && sim.Cities.Towns.Any(t => t.TotemId == a.Building));
            foreach (var c in log.Commands) sim.Schedule(c.Tick, c.Command);
            sim.Run((int)log.Ticks);
            hashes[run] = sim.StateHash();
        }
        Assert.Equal(hashes[0], hashes[1]);
        Assert.True(totemAttacks > 0, "no player went for a town's totem");
    }

    // --- The data ----------------------------------------------------------------------------------

    [Fact]
    public void TheGoblinsAreData()
    {
        var db = ContentDb.Default;
        Assert.Equal(6, db.CityStates.Count);
        var goblins = db.CityStates[0];
        Assert.Same(goblins, db.CityState("goblins"));
        Assert.Same(db.Building("goblin_totem"), goblins.Totem);
        Assert.Equal(BuildingKind.Totem, goblins.Totem!.Kind);
        Assert.Equal(5 * Tps, goblins.TotemRespawnTicks);
        Assert.Equal(new[] { "goblin_cutthroat", "goblin_cutthroat", "goblin_cutthroat", "goblin_slinger", "goblin_slinger", "goblin_slinger" },
            goblins.Guards.Select(u => u.Id));
        Assert.Equal((4, 45 * Tps), (goblins.GuardReach, goblins.GuardRefillTicks));
        Assert.Equal((db.Unit("caravan"), Good.Gold, 10, 60 * Tps), (goblins.TributeCaravan, goblins.TributeGood, goblins.TributeAmount, goblins.TributeTicks));
        Assert.Equal((Good.Food, 20, Good.Iron, 5, 50), (goblins.Buys, goblins.Load, goblins.Sells, goblins.Price, goblins.HolderBonus));
        // Nobody builds a totem.
        Assert.False(new Simulation(1, 32, 16, 32).Economy.Unlocked(0, goblins.Totem));
        Assert.Equal(new[] { "goblin_cutthroat", "goblin_slinger" }, goblins.Mercenaries.Select(u => u.Id));
        Assert.Equal((6, 30 * Tps), (goblins.Pool, goblins.RefillTicks));
        Assert.Equal(6, goblins.HealRange);
        Assert.Same(db.Unit("wolf"), goblins.Lair);
        Assert.Equal((BlockType.StoneBrick, BlockType.Thatch), (goblins.Wall, goblins.Roof));

        var caravan = db.Unit("caravan");
        Assert.True(caravan.IsTrader);
        Assert.False(caravan.CanAttack);
        Assert.Same(db.Building("town_hall"), caravan.TrainedAt);
        Assert.DoesNotContain(caravan, db.BaseUnits);
        Assert.All(goblins.Mercenaries, u => Assert.Same(goblins, u.HiredFrom));
        Assert.DoesNotContain(db.BaseUnits, u => u.HiredFrom != null);
    }

    /// <summary>Issue #46: the ogres' hold is data like the goblins' town, with its own idol, guards, tribute and mercenaries.</summary>
    [Fact]
    public void TheOgresAreData()
    {
        var db = ContentDb.Default;
        var ogres = db.CityState("ogres");
        Assert.Equal(1, ogres.Index);
        Assert.Same(db.Building("ogre_idol"), ogres.Totem);
        Assert.Equal(BuildingKind.Totem, ogres.Totem!.Kind);
        Assert.Equal(new[] { "ogre_brute", "ogre_brute", "ogre_hurler", "ogre_hurler" }, ogres.Guards.Select(u => u.Id));
        Assert.Equal((Good.Stone, 30), (ogres.TributeGood, ogres.TributeAmount));
        Assert.Equal((Good.Food, Good.Stone), (ogres.Buys, ogres.Sells));
        Assert.Equal(new[] { "ogre_brute", "ogre_hurler" }, ogres.Mercenaries.Select(u => u.Id));
        Assert.Equal((3, 60 * Tps), (ogres.Pool, ogres.RefillTicks));
        Assert.Same(db.Unit("stone_giant"), ogres.Lair);
        Assert.Equal((BlockType.StoneBrick, BlockType.RoofTile), (ogres.Wall, ogres.Roof));
        // Big, slow and costly, and they heal even in a fight.
        var spearman = db.Unit("spearman");
        Assert.All(ogres.Mercenaries, u =>
        {
            Assert.Same(ogres, u.HiredFrom);
            Assert.True(u.MaxHp > 3 * spearman.MaxHp / 2);
            Assert.True(u.Speed < spearman.Speed);
            Assert.True(u.Regen > 0);
        });
        // Only the ogres, the swamp's bog horrors, which heal in the muck, and the dark power's blight horror regenerate.
        // (and the trolls, whose wounds close only out of the daylight).
        Assert.All(db.Units.Where(u => u.HiredFrom != ogres && u.Id is not ("bog_horror" or "blight_horror" or "troll") && u.Faction?.Id != "reptilians"), u => Assert.Equal(0, u.Regen));
    }

    /// <summary>
    /// Issue #46: the ogres' hold is won like the goblins' town. Soldiers break the idol past its guards, and
    /// then the holder hires an ogre brute at the market and gets tribute in stone.
    /// </summary>
    [Fact]
    public void BreakingTheIdolMakesTheOgresYourVassals()
    {
        var sim = Flat();
        var hall = Hall(sim, Settlers, food: 1000, iron: 100, gold: 100);
        var hold = sim.Cities.Place(sim.Content.CityState("ogres"), TownX, TownZ)!;
        Assert.Equal(new[] { "ogre_brute", "ogre_brute", "ogre_hurler", "ogre_hurler" }, hold.Guards.Select(id => DefOf(sim, id).Id));
        Conquer(sim, hold, Settlers, spearmen: 24);
        Assert.Equal(Settlers, Totem(sim, hold).Owner);
        Assert.All(hold.Guards, id => Assert.Equal(Settlers, sim.Units.Owner[Index(sim, id)]));

        Do(sim, new HireCommand(Settlers, hold.Id, "goblin_cutthroat")); // not one of the ogres'
        Assert.Empty(UnitsOf(sim, Settlers, "goblin_cutthroat"));
        int ready = hold.Ready;
        Do(sim, new HireCommand(Settlers, hold.Id, "ogre_brute"));
        Assert.Single(UnitsOf(sim, Settlers, "ogre_brute").Where(id => sim.Units.Town[Index(sim, id)] == 0));
        Assert.Equal(ready - 1, hold.Ready);
        Assert.Equal(1000 - 120, hall.Stock[(int)Good.Food]);

        RunUntil(sim, () => hall.Stock[(int)Good.Stone] == 30, hold.Def.TributeTicks + 120 * Tps, "the ogres' stone reaches the town hall");
    }

    /// <summary>Issue #46: an ogre heals a percent of its hit points each second even in a fight; a spearman does not.</summary>
    [Fact]
    public void OgresHealEvenInAFight()
    {
        var sim = Flat(64);
        int ogre = sim.SpawnUnit(Settlers, "ogre_brute", 20, 20);
        int spear = sim.SpawnUnit(Settlers, "spearman", 30, 20);
        var u = sim.Units;
        u.Hp[Index(sim, ogre)] = 100;
        u.Hp[Index(sim, spear)] = 20;
        for (int t = 0; t < 10 * Tps; t++)
        {
            u.LastFight[Index(sim, ogre)] = u.LastFight[Index(sim, spear)] = sim.Tick; // still fighting
            sim.Step();
        }
        // 1% of 260 a second is 2.6: 26 hit points in ten seconds, give or take a second's worth.
        Assert.InRange(u.Hp[Index(sim, ogre)], 100 + 23, 100 + 27);
        Assert.Equal(20, u.Hp[Index(sim, spear)]);
        // Out of the fight it heals at the faster of its own rate and the usual one.
        int was = u.Hp[Index(sim, ogre)];
        for (int t = 0; t < 20 * Tps; t++) sim.Step();
        Assert.True(u.Hp[Index(sim, ogre)] >= was + 50);
    }

    [Fact]
    public void CityStateDataIsChecked()
    {
        var files = ContentLoader.Files.ToDictionary(f => f, f => ContentLoader.EmbeddedText(f)!);
        void Edit(string file, string old, string replacement)
        {
            Assert.Contains(old, files[file]);
            int at = files[file].IndexOf(old, StringComparison.Ordinal);
            files[file] = files[file][..at] + replacement + files[file][(at + old.Length)..];
        }
        Edit("units.json", "\"hiredFrom\": \"goblins\",", "\"hiredFrom\": \"dwarves\",");
        Edit("units.json", "\"hiredFrom\": \"goblins\",", "\"hiredFrom\": \"goblins\", \"trainedAt\": \"barracks\",");
        Edit("units.json", "\"role\": \"trader\",", "\"role\": \"trader\", \"damage\": 3,");
        Edit("citystates.json", "\"sells\": \"iron\"", "\"sells\": \"food\"");
        Edit("citystates.json", "\"lair\": \"wolf\"", "\"lair\": \"spearman\"");
        Edit("citystates.json", "\"building\": \"goblin_totem\"", "\"building\": \"house\"");
        Edit("citystates.json", "\"units\": [\"goblin_cutthroat\",", "\"units\": [\"caravan\",");
        Edit("citystates.json", "\"caravan\": \"caravan\"", "\"caravan\": \"worker\"");
        Edit("citystates.json", "\"respawn\": 5 },", "\"respawn\": 5 }, \"ally\": 1,");
        var e = Assert.Throws<ContentException>(() => ContentLoader.Load(files));
        Assert.Contains("units.json: unit 'goblin_cutthroat': \"hiredFrom\" is \"dwarves\", which is not a city-state", e.Errors);
        Assert.Contains("units.json: unit 'goblin_slinger': is a mercenary, hired at a city-state's town: it cannot have \"trainedAt\", \"evolvesFrom\", \"lair\" or \"trainTime\"", e.Errors);
        Assert.Contains("units.json: unit 'caravan': is a caravan (role trader): it cannot have \"damage\", \"evolvesFrom\", \"lair\" or \"hiredFrom\"", e.Errors);
        Assert.Contains("citystates.json: city-state 'goblins', trade: buys and sells the same good (food)", e.Errors);
        Assert.Contains("citystates.json: city-state 'goblins': \"lair\" is \"spearman\", which keeps no lair (see \"lair\" in units.json)", e.Errors);
        Assert.Contains("citystates.json: city-state 'goblins', totem: \"building\" is \"house\", which is not of kind totem", e.Errors);
        Assert.Contains("citystates.json: city-state 'goblins', guards: \"units\" names \"caravan\", which cannot fight", e.Errors);
        Assert.Contains("citystates.json: city-state 'goblins', tribute: \"caravan\" is \"worker\", which is not a trader", e.Errors);
        Assert.Contains(e.Errors, m => m.StartsWith("citystates.json: city-state 'goblins': unknown field \"ally\""));
        Assert.Equal(9, e.Errors.Count);

        files = ContentLoader.Files.ToDictionary(f => f, f => ContentLoader.EmbeddedText(f)!);
        files.Remove("citystates.json");
        Assert.Contains("citystates.json is missing", Assert.Throws<ContentException>(() => ContentLoader.Load(files)).Errors);
    }

    /// <summary>The content hash covers the city-states' numbers, so a replay with other numbers says so.</summary>
    [Fact]
    public void TheContentHashCoversTheCityStates()
    {
        ulong Hash(string file, string old, string replacement)
        {
            var files = ContentLoader.Files.ToDictionary(f => f, f => ContentLoader.EmbeddedText(f)!);
            Assert.Contains(old, files[file]);
            files[file] = files[file].Replace(old, replacement);
            return ContentLoader.Load(files).Hash;
        }
        ulong original = ContentDb.Default.Hash;
        Assert.NotEqual(original, Hash("citystates.json", "\"respawn\": 5", "\"respawn\": 6"));
        Assert.NotEqual(original, Hash("citystates.json", "\"reach\": 4", "\"reach\": 5"));
        Assert.NotEqual(original, Hash("citystates.json", "\"amount\": 10", "\"amount\": 11"));
        Assert.NotEqual(original, Hash("citystates.json", "\"goblin_slinger\"]", "\"goblin_cutthroat\"]"));
        Assert.NotEqual(original, Hash("citystates.json", "\"holderBonus\": 50", "\"holderBonus\": 51"));
        Assert.NotEqual(original, Hash("citystates.json", "\"heal\": { \"range\": 6 }", "\"heal\": { \"range\": 7 }"));
        Assert.NotEqual(original, Hash("citystates.json", "\"roof\": \"thatch\"", "\"roof\": \"roof_tile\""));
        Assert.Equal(original, Hash("citystates.json", "Goblin traders", "Goblin merchants")); // descriptions do not count
    }

    // --- The kobolds' warren, in the deep caves ----------------------------------------------------

    [Fact]
    public void TheKoboldsAreData()
    {
        var db = ContentDb.Default;
        Assert.Equal(new[] { "goblins", "ogres", "kobolds", "trolls", "centaurs", "merfolk" }, db.CityStates.Select(c => c.Id));
        var kobolds = db.CityState("kobolds");
        Assert.True(kobolds.Deep);
        Assert.False(db.CityState("goblins").Deep);
        Assert.Equal((BlockType.Stone, BlockType.Grass), (kobolds.Ground, db.CityState("goblins").Ground));
        Assert.Same(db.Building("kobold_crystal"), kobolds.Totem);
        Assert.Equal(new[] { "kobold_spear", "kobold_spear", "kobold_spear", "kobold_digger", "kobold_digger" }, kobolds.Guards.Select(u => u.Id));
        Assert.Equal((Good.Crystal, 8, 60 * Tps), (kobolds.TributeGood, kobolds.TributeAmount, kobolds.TributeTicks));
        Assert.Equal((Good.Food, 20, Good.Crystal, 2), (kobolds.Buys, kobolds.Load, kobolds.Sells, kobolds.Price));
        Assert.Null(kobolds.Lair);
        Assert.Equal(new[] { "kobold_digger", "kobold_spear" }, kobolds.Mercenaries.Select(u => u.Id));
        // The diggers are sappers, so whoever holds the warren can tunnel with them; both cost crystal.
        Assert.Equal(UnitRole.Sapper, db.Unit("kobold_digger").Role);
        Assert.All(kobolds.Mercenaries, m => Assert.Contains(m.Cost, c => c.Good == Good.Crystal));
        // The data is checked.
        var files = ContentLoader.Files.ToDictionary(f => f, f => ContentLoader.EmbeddedText(f)!);
        files["citystates.json"] = files["citystates.json"].Replace("\"ground\": \"stone\"", "\"ground\": \"lava\"").Replace("\"deep\": true", "\"deep\": 1");
        var e = Assert.Throws<ContentException>(() => ContentLoader.Load(files));
        Assert.Contains("citystates.json: city-state 'kobolds': \"ground\" is \"lava\"; it should be grass, sand, stone or stone_brick", e.Errors);
        Assert.Contains("citystates.json: city-state 'kobolds': \"deep\" should be true or false", e.Errors);
        // And hashed.
        files = ContentLoader.Files.ToDictionary(f => f, f => ContentLoader.EmbeddedText(f)!);
        files["citystates.json"] = files["citystates.json"].Replace("\"ground\": \"stone\"", "\"ground\": \"stone_brick\"");
        Assert.NotEqual(db.Hash, ContentLoader.Load(files).Hash);
        files = ContentLoader.Files.ToDictionary(f => f, f => ContentLoader.EmbeddedText(f)!);
        files["citystates.json"] = files["citystates.json"].Replace("\"deep\": true", "\"deep\": false");
        Assert.NotEqual(db.Hash, ContentLoader.Load(files).Hash);
    }

    /// <summary>
    /// On the default map the kobolds' warren is dug out of a chamber of the deep caves' upper level, not a nest's:
    /// a level square of stone under a roof of rock, its huts and crystal in it, its guards round the market, far from
    /// both starts and about as far from each; a unit can walk from it up to the open. Building over it, far above,
    /// is fine. A map without deep caves has no warren.
    /// </summary>
    [Theory]
    [InlineData(1234UL)]
    [InlineData(7UL)]
    public void TheKoboldWarrenIsDugIntoADeepCave(ulong seed)
    {
        var sim = CavernTests.DefaultMatch(seed);
        var w = sim.World;
        int deep = Rts.Sim.World.RegionGenerator.DeepLayers(w.SizeY);
        Assert.Equal(new[] { "goblins", "ogres", "kobolds", "trolls", "centaurs", "merfolk" }, sim.Cities.Towns.Select(t => t.Def.Id));
        var warren = sim.Cities.Towns[2];
        Assert.True(warren.Deep);
        int chamber = sim.Map.Deeps.FindIndex(d => d.X == warren.X && d.Z == warren.Z);
        Assert.True(chamber >= 0, "the warren is not at a deep chamber");
        Assert.NotEqual(0, chamber % Rts.Sim.World.RegionGenerator.DeepNestEvery);
        Assert.InRange(warren.Y, deep / 2, deep);
        int r = CityStateSystem.TownRadius;
        for (int dz = -r; dz <= r; dz++)
        for (int dx = -r; dx <= r; dx++)
        {
            int x = warren.X + dx, z = warren.Z + dz;
            Assert.Equal(BlockType.Stone, w.Get(x, warren.Y - 1, z));
            bool hut = Math.Abs(dx) > 3 && Math.Abs(dz) > 3, plinth = Math.Abs(dx) <= 1 && Math.Abs(dz) <= 1;
            if (!hut && !plinth) Assert.Equal(BlockType.Air, w.Get(x, warren.Y, z));
            // Rock over the hall, with the land far above and no water near it.
            int top = w.SizeY - 1;
            while (w.Get(x, top, z) == BlockType.Air) top--;
            Assert.True(top > warren.Y + 8 + CityStateSystem.DeepRoof, $"open above {x},{z}");
            for (int y = warren.Y; y <= warren.Y + 8 + CityStateSystem.DeepRoof; y++) Assert.False(w.Get(x, y, z) is BlockType.Water or BlockType.Lava);
        }
        Assert.Equal(warren.Def.Wall, w.Get(warren.X - r, warren.Y, warren.Z - r));
        var crystal = sim.Cities.Totem(warren)!;
        Assert.Equal(("kobold_crystal", warren.Y, Simulation.WildPlayer), (crystal.Def.Id, crystal.Y, crystal.Owner));
        Assert.Equal(5, warren.Guards.Count());
        Assert.All(warren.Guards, id => Assert.InRange(sim.Units.Y[Index(sim, id)], warren.Y - 1, warren.Y + 1));
        var d = sim.Map.Starts.Select(s => IntMath.Distance(s.X, s.Z, warren.X, warren.Z)).ToArray();
        int want = w.SizeX * 3 / 16;
        Assert.All(d, x => Assert.True(x >= want * 3 / 2, $"the warren is {x} blocks from a start"));
        Assert.InRange(Math.Abs(d[0] - d[1]), 0, 120);
        // No den beside it.
        Assert.All(sim.Monsters.Lairs, l => Assert.True(IntMath.Distance(l.X, l.Z, warren.X, warren.Z) >= CityStateSystem.LairNear || Math.Abs(l.Y - warren.Y) > 16));

        // On foot from the market up into the open.
        long Key(int x, int y, int z) => ((long)x * w.SizeY + y) * w.SizeZ + z;
        var seen = new HashSet<long> { Key(warren.X, warren.Y, warren.Z + 3) };
        var open = new Queue<(int X, int Y, int Z)>();
        open.Enqueue((warren.X, warren.Y, warren.Z + 3));
        bool surfaced = false;
        while (open.Count > 0 && !surfaced)
        {
            var (x, y, z) = open.Dequeue();
            if (Walk.GroundLevel(w, x, z) <= y) surfaced = true;
            foreach (var (sx, sz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                if (w.InBounds(x + sx, 0, z + sz) && Walk.TryStep(w, x, z, y, x + sx, z + sz, out int ny) && seen.Add(Key(x + sx, ny, z + sz)))
                    open.Enqueue((x + sx, ny, z + sz));
        }
        Assert.True(surfaced, "no way up from the warren");

        // Building far over its square is not crowded by it (only straight over its crystal), as it is by a town in the open.
        Assert.NotEqual(PlaceResult.Crowded, sim.Economy.CanPlace(0, sim.Content.Building("house"), warren.X + 3, warren.Z + 3, out _, checkPlayer: false));
        var goblins = sim.Cities.Towns[0];
        Assert.Equal(PlaceResult.Crowded, sim.Economy.CanPlace(0, sim.Content.Building("house"), goblins.X - 1, goblins.Z - 1, out _, checkPlayer: false));
    }

    [Fact]
    public void AMapWithoutDeepCavesHasNoWarren()
    {
        var sim = new Simulation(1234, 512, 64, 512);
        sim.PlaceStartingBuildings();
        sim.SpawnStartingUnits(24);
        sim.SpawnLairs();
        Assert.Equal(new[] { "goblins", "ogres", "trolls", "centaurs", "merfolk" }, sim.SpawnCities().Select(t => t.Def.Id));
    }

    /// <summary>
    /// A deep town's guards and healing reach only its own level: a soldier on a pillar ten levels over the square's
    /// edge is left alone, and does not heal there; one at the square's level beside it is set on.
    /// </summary>
    [Fact]
    public void ADeepTownsGuardsKeepToItsLevel()
    {
        var sim = Flat();
        var warren = sim.Cities.Place(sim.Content.CityState("kobolds"), TownX, TownZ)!;
        Assert.True(warren.Deep);
        Assert.Equal(BlockType.Stone, sim.World.Get(TownX, warren.Y - 1, TownZ + 3));
        int px = TownX - CityStateSystem.TownRadius - 2, pz = TownZ + 3;
        TestWorlds.Box(sim.World, px - 1, Ground + 1, pz - 1, px + 1, Ground + 10, pz + 1, BlockType.Stone);
        int high = sim.SpawnUnit(Settlers, "spearman", px, pz);
        Assert.Equal(Ground + 11, sim.Units.Y[Index(sim, high)]);
        Assert.False(warren.AtLevel(Ground + 11));
        Assert.True(warren.AtLevel(warren.Y));
        sim.Run(5 * Tps);
        Assert.Equal(sim.Content.Unit("spearman").MaxHp, sim.Units.Hp[Index(sim, high)]);
        Assert.All(warren.Guards, id => Assert.Equal(0, sim.Units.Target[Index(sim, id)]));

        int low = sim.SpawnUnit(Settlers, "spearman", px, pz + 3);
        RunUntil(sim, () => !sim.Units.Exists(low) || sim.Units.Hp[Index(sim, low)] < sim.Content.Unit("spearman").MaxHp,
            20 * Tps, "the guards attack the spearman at their level");
        Assert.Equal(sim.Content.Unit("spearman").MaxHp, sim.Units.Hp[Index(sim, high)]);

        // Its healing reaches its own level only (were it held).
        Assert.Null(sim.Cities.HealingTown(Settlers, px, Ground + 11, pz));
    }

    /// <summary>Breaking the kobolds' crystal makes them yours: they send crystal, and sell diggers for food and crystal.</summary>
    [Fact]
    public void BreakingTheCrystalMakesTheKoboldsYourVassals()
    {
        var sim = Flat();
        var hall = Hall(sim, Settlers, food: 1000);
        hall.Stock[(int)Good.Crystal] = 20;
        var warren = sim.Cities.Place(sim.Content.CityState("kobolds"), TownX, TownZ)!;
        Conquer(sim, warren, Settlers);
        Assert.Equal(Settlers, Totem(sim, warren).Owner);
        Assert.All(warren.Guards, id => Assert.Equal(Settlers, sim.Units.Owner[Index(sim, id)]));
        Assert.NotNull(sim.Cities.HealingTown(Settlers, warren.X, warren.Y, warren.Z));
        Do(sim, new HireCommand(Settlers, warren.Id, "kobold_digger"));
        var hired = UnitsOf(sim, Settlers, "kobold_digger").Where(id => sim.Units.Town[Index(sim, id)] == 0).ToList();
        Assert.Single(hired);
        Assert.Equal((1000 - 25, 20 - 5), (hall.Stock[(int)Good.Food], hall.Stock[(int)Good.Crystal]));
        RunUntil(sim, () => hall.Stock[(int)Good.Crystal] == 15 + 8, warren.Def.TributeTicks + 120 * Tps, "the kobolds' crystal reaches the town hall");
    }
}
