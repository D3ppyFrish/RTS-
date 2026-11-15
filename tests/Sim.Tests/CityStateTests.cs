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

    private static Building Hall(Simulation sim, int owner, int x = HallX, int z = HallZ, int food = 0, int wood = 0, int iron = 0)
    {
        var b = sim.Economy.PlaceFinished(owner, sim.Content.Building("town_hall"), x, z) ?? throw new InvalidOperationException("the hall does not fit");
        b.Stock[(int)Good.Food] = food;
        b.Stock[(int)Good.Wood] = wood;
        b.Stock[(int)Good.Iron] = iron;
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
    /// again five seconds later, in the Settlers' hands. A minute on, a tribute caravan of 15 iron sets out,
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
        Assert.Equal(0, sim.Economy.Upkeep(Settlers));

        // Tribute: a caravan sets out a minute after the town was taken, with 15 iron for the town hall.
        RunUntil(sim, () => town.Caravans.Count > 0, goblins.TributeTicks + Tps, "a tribute caravan sets out");
        int caravan = town.Caravans[0];
        int c = Index(sim, caravan);
        Assert.Equal((Settlers, town.Id, Good.Iron, 15), (sim.Units.Owner[c], sim.Units.Town[c], sim.Units.Carry[c], sim.Units.CarryAmount[c]));
        Assert.Equal(UnitJob.Deliver, sim.Units.Job[c]);
        Assert.Equal(0, sim.Economy.Population(Settlers));
        Assert.Contains(sim.Events.All.Where(e => e.Player == Settlers), e => e.Kind == GameEventKind.TributeSent);
        // It takes no orders.
        Do(sim, new StopCommand(Settlers, new[] { caravan }));
        Assert.Equal(UnitJob.Deliver, sim.Units.Job[Index(sim, caravan)]);

        RunUntil(sim, () => hall.Stock[(int)Good.Iron] == 15, 90 * Tps, "the tribute reaches the town hall");
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
        Hall(sim, Settlers, food: 1000, wood: 100, iron: 100);
        Hall(sim, Orcs, HallX, 20, food: 1000, wood: 100, iron: 100);
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
        var hall = Hall(sim, Settlers, food: 2000, wood: 500, iron: 500);
        var town = PlaceTown(sim);
        Conquer(sim, town, Settlers);
        int eaten = sim.Economy.Player(Settlers).Eaten[(int)Good.Food];
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
        Assert.Equal(2000 - 6 * 40, hall.Stock[(int)Good.Food] + sim.Economy.Player(Settlers).Eaten[(int)Good.Food] - eaten);
    }

    // --- The town ----------------------------------------------------------------------------------

    /// <summary>
    /// Every match has a goblin town about as far from one start as the other: a levelled square with a hut at
    /// each corner, its door toward the open market, and a wolf lair just beyond it. No building may go on it.
    /// </summary>
    [Fact]
    public void EveryMatchHasATownForEachCityStateWithItsLair()
    {
        var sim = new Simulation(1234, 512, 64, 512);
        sim.PlaceStartingBuildings();
        sim.SpawnStartingUnits(24);
        var startLairs = sim.SpawnLairs();
        var towns = sim.SpawnCities();
        var town = Assert.Single(towns);
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
        Assert.Equal(3, sim.Monsters.Lairs.Count);
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
            var town = Assert.Single(sim.Cities.Towns);
            totemAttacks = log.Commands.Count(c => c.Command is AttackBuildingCommand a && a.Building == town.TotemId);
            foreach (var c in log.Commands) sim.Schedule(c.Tick, c.Command);
            sim.Run((int)log.Ticks);
            hashes[run] = sim.StateHash();
        }
        Assert.Equal(hashes[0], hashes[1]);
        Assert.True(totemAttacks > 0, "no player went for the goblins' totem");
    }

    // --- The data ----------------------------------------------------------------------------------

    [Fact]
    public void TheGoblinsAreData()
    {
        var db = ContentDb.Default;
        var goblins = Assert.Single(db.CityStates);
        Assert.Same(goblins, db.CityState("goblins"));
        Assert.Same(db.Building("goblin_totem"), goblins.Totem);
        Assert.Equal(BuildingKind.Totem, goblins.Totem!.Kind);
        Assert.Equal(5 * Tps, goblins.TotemRespawnTicks);
        Assert.Equal(new[] { "goblin_cutthroat", "goblin_cutthroat", "goblin_cutthroat", "goblin_slinger", "goblin_slinger", "goblin_slinger" },
            goblins.Guards.Select(u => u.Id));
        Assert.Equal((4, 45 * Tps), (goblins.GuardReach, goblins.GuardRefillTicks));
        Assert.Equal((db.Unit("caravan"), Good.Iron, 15, 60 * Tps), (goblins.TributeCaravan, goblins.TributeGood, goblins.TributeAmount, goblins.TributeTicks));
        Assert.Equal((Good.Food, 20, Good.Iron, 5, 50), (goblins.Buys, goblins.Load, goblins.Sells, goblins.Price, goblins.HolderBonus));
        // Nobody builds a totem.
        Assert.False(new Simulation(1, 32, 16, 32).Economy.Unlocked(0, goblins.Totem));
        Assert.Equal(new[] { "goblin_cutthroat", "goblin_slinger" }, goblins.Mercenaries.Select(u => u.Id));
        Assert.Equal((6, 30 * Tps), (goblins.Pool, goblins.RefillTicks));
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
        Edit("units.json", "\"hiredFrom\": \"goblins\",", "\"hiredFrom\": \"kobolds\",");
        Edit("units.json", "\"hiredFrom\": \"goblins\",", "\"hiredFrom\": \"goblins\", \"trainedAt\": \"barracks\",");
        Edit("units.json", "\"role\": \"trader\",", "\"role\": \"trader\", \"damage\": 3,");
        Edit("citystates.json", "\"sells\": \"iron\"", "\"sells\": \"food\"");
        Edit("citystates.json", "\"lair\": \"wolf\"", "\"lair\": \"spearman\"");
        Edit("citystates.json", "\"building\": \"goblin_totem\"", "\"building\": \"house\"");
        Edit("citystates.json", "\"units\": [\"goblin_cutthroat\",", "\"units\": [\"caravan\",");
        Edit("citystates.json", "\"caravan\": \"caravan\"", "\"caravan\": \"worker\"");
        Edit("citystates.json", "\"respawn\": 5 },", "\"respawn\": 5 }, \"ally\": 1,");
        var e = Assert.Throws<ContentException>(() => ContentLoader.Load(files));
        Assert.Contains("units.json: unit 'goblin_cutthroat': \"hiredFrom\" is \"kobolds\", which is not a city-state", e.Errors);
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
        Assert.NotEqual(original, Hash("citystates.json", "\"amount\": 15", "\"amount\": 16"));
        Assert.NotEqual(original, Hash("citystates.json", "\"goblin_slinger\"]", "\"goblin_cutthroat\"]"));
        Assert.NotEqual(original, Hash("citystates.json", "\"holderBonus\": 50", "\"holderBonus\": 51"));
        Assert.NotEqual(original, Hash("citystates.json", "\"roof\": \"thatch\"", "\"roof\": \"roof_tile\""));
        Assert.Equal(original, Hash("citystates.json", "Goblin traders", "Goblin merchants")); // descriptions do not count
    }
}
