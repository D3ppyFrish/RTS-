using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.Automation;
using Rts.Sim.Economy;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

public class AutomationTests
{
    private const int Tps = Simulation.TicksPerSecond;
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4

    private static Simulation Flat(int size = 64) => TestWorlds.Sim(TestWorlds.Flat(size, 16, size, Ground));

    private static Building Place(Simulation sim, int owner, string def, int x, int z) =>
        sim.Economy.PlaceFinished(owner, sim.Content.Building(def), x, z) ?? throw new InvalidOperationException($"{def} does not fit");

    private static int Worker(Simulation sim, int owner, int x, int z) => sim.SpawnUnit(owner, "worker", x, z);

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

    private static void Automate(Simulation sim, params int[] ids) => sim.Schedule(sim.Tick, new AutomateCommand(0, ids, true));

    /// <summary>Steps until the condition holds; fails the test if it takes longer than maxTicks.</summary>
    private static void RunUntil(Simulation sim, Func<bool> done, int maxTicks, string what)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return;
            sim.Step();
        }
        Assert.Fail($"Not done after {maxTicks} ticks: {what}");
    }

    // --- Issue #11's check -------------------------------------------------------------------------

    private readonly record struct Minute(int Population, int Gathered, int Houses, int Farms);

    /// <summary>
    /// From a generated region's real start (a town hall with the starting goods, and five workers) the
    /// player gives orders only at the start: every worker automatic, the town hall training workers on
    /// repeat, and farming researched. For the next ten minutes nobody gives an order. The economy is
    /// sampled each minute.
    /// </summary>
    private static (Simulation Sim, List<Minute> Minutes, List<(long Tick, Command Command)> Given) RunAutomatedEconomy(
        bool automation = true, List<(long Tick, Command Command)>? replay = null)
    {
        var sim = new Simulation(1234, 512, 64, 512);
        sim.PlaceStartingBuildings();
        var start = sim.Map.Starts[0];
        var hall = Assert.Single(sim.Buildings.All, b => b.Owner == 0);
        for (int k = 0; k < 5; k++) sim.SpawnUnit(0, "worker", start.X - 2 + k, hall.MaxZ + 2);
        sim.Schedule(0, new AutomateAllCommand(0, true));
        sim.Schedule(0, new TrainCommand(0, hall.Id, "worker"));
        sim.Schedule(0, new RepeatCommand(0, hall.Id, true));
        sim.Schedule(0, new ResearchCommand(0, "farming"));
        var given = new List<(long, Command)>();
        sim.Automation.Enabled = automation;
        sim.Automation.Record = given;
        if (replay != null)
            foreach (var (tick, cmd) in replay) sim.Schedule(tick, cmd);

        var minutes = new List<Minute> { Snapshot(sim) };
        for (int m = 1; m <= 10; m++)
        {
            sim.Run(60 * Tps);
            minutes.Add(Snapshot(sim));
        }
        return (sim, minutes, given);
    }

    private static Minute Snapshot(Simulation sim)
    {
        int houses = sim.Buildings.All.Count(b => b.Owner == 0 && b.Active && b.Def.Kind == BuildingKind.House);
        int farms = sim.Buildings.All.Count(b => b.Owner == 0 && b.Active && b.Def.Kind == BuildingKind.Farm);
        return new Minute(sim.Economy.Population(0), sim.Economy.Player(0).Gathered.Sum(), houses, farms);
    }

    /// <summary>
    /// Issue #11's check: an automated economy keeps growing for ten minutes with no orders. Measured (with
    /// seed 1234): 5 people at the start, 10 after a minute and 40 after ten, with 7 houses and 7 farms
    /// built, 2324 goods gathered and never starving. The checks are set a little below that. Replaying the
    /// commands automation gave, with automation off, ends in the same state: it acts only through orders.
    /// </summary>
    [Fact]
    public void AnAutomatedEconomyKeepsGrowingForTenMinutes()
    {
        var (sim, minutes, given) = RunAutomatedEconomy();
        for (int m = 1; m < minutes.Count; m++)
        {
            Assert.True(minutes[m].Population >= minutes[m - 1].Population, $"the population fell in minute {m}");
            Assert.True(minutes[m].Gathered >= minutes[m - 1].Gathered + 80, $"little was gathered in minute {m}");
            if (m >= 2) Assert.True(minutes[m].Population > minutes[m - 2].Population, $"no growth in minutes {m - 1} and {m}");
        }
        Assert.InRange(minutes[10].Population, 30, 200);
        Assert.InRange(minutes[10].Houses, 4, 50);
        Assert.InRange(minutes[10].Farms, 4, 50);
        Assert.InRange(minutes[10].Gathered, 1800, 100000);

        // Automation gave every kind of order it has for workers and training, and nothing else.
        Assert.Contains(given, g => g.Command is GatherCommand);
        Assert.Contains(given, g => g.Command is WorkCommand);
        Assert.Contains(given, g => g.Command is TrainCommand);
        Assert.Contains(given, g => g.Command is BuildCommand { Building: "house" });
        Assert.Contains(given, g => g.Command is BuildCommand { Building: "farm" });
        Assert.All(given, g => Assert.True(g.Command is GatherCommand or WorkCommand or TrainCommand or BuildCommand));

        var (replayed, _, _) = RunAutomatedEconomy(automation: false, replay: given);
        Assert.Equal(sim.StateHash(), replayed.StateHash());
    }

    [Fact]
    public void AutomationIsDeterministic()
    {
        static List<ulong> Run()
        {
            var sim = Flat();
            var hall = Place(sim, 0, "town_hall", 10, 10);
            hall.Stock[(int)Good.Food] = 150;
            hall.Stock[(int)Good.Wood] = 80;
            for (int k = 0; k < 6; k++) TestWorlds.Tree(sim.World, 30 + k % 3 * 6, 30 + k / 3 * 6, 4);
            for (int k = 0; k < 4; k++) sim.World.Set(12 + 2 * k, Ground + 1, 30, BlockType.BerryBush);
            TestWorlds.Box(sim.World, 40, Ground + 1, 12, 42, Ground + 2, 14, BlockType.Stone);
            for (int k = 0; k < 6; k++) Worker(sim, 0, 8 + k, 20);
            sim.Schedule(0, new AutomateAllCommand(0, true));
            sim.Schedule(0, new TrainCommand(0, hall.Id, "worker"));
            sim.Schedule(0, new RepeatCommand(0, hall.Id, true));
            var hashes = new List<ulong>();
            for (int s = 0; s < 6; s++)
            {
                sim.Run(30 * Tps);
                hashes.Add(sim.StateHash());
            }
            return hashes;
        }
        Assert.Equal(Run(), Run());
    }

    // --- Worker jobs -------------------------------------------------------------------------------

    [Fact]
    public void AnAutomaticWorkerFindsItsOwnWork()
    {
        var sim = Flat();
        var store = Place(sim, 0, "storehouse", 10, 10);
        TestWorlds.Tree(sim.World, 30, 30, 4);
        int auto = Worker(sim, 0, 20, 20), manual = Worker(sim, 0, 22, 20);
        Automate(sim, auto);
        sim.Run(2 * Tps);
        Assert.Equal(UnitJob.Gather, sim.Units.Job[Index(sim, auto)]);
        Assert.Equal(BlockType.Wood, sim.Units.JobBlock[Index(sim, auto)]);
        Assert.Equal(UnitJob.None, sim.Units.Job[Index(sim, manual)]);
        RunUntil(sim, () => store.Stock[(int)Good.Wood] > 0, 60 * Tps, "the wood to reach the storehouse");
        Assert.Equal(UnitJob.None, sim.Units.Job[Index(sim, manual)]);
    }

    [Fact]
    public void AWorkerWalksFurtherOutOnceTheGroundNearTownIsBare()
    {
        // Issue #36: with nothing to gather within SearchRadius, automatic workers stood idle by the town
        // hall. The only tree is twice that far away: it is gathered rather than nothing.
        var sim = Flat(160);
        var store = Place(sim, 0, "storehouse", 10, 10);
        int far = 20 + 2 * AutomationSystem.SearchRadius;
        Assert.True(far - 20 <= AutomationSystem.FarSearchRadius);
        TestWorlds.Tree(sim.World, far, far, 4);
        int w = Worker(sim, 0, 20, 20);
        Automate(sim, w);
        sim.Run(2 * Tps);
        Assert.Equal(UnitJob.Gather, sim.Units.Job[Index(sim, w)]);
        Assert.Equal(BlockType.Wood, sim.Units.JobBlock[Index(sim, w)]);
        RunUntil(sim, () => store.Stock[(int)Good.Wood] > 0, 120 * Tps, "the far wood to reach the storehouse");
    }

    [Fact]
    public void ANearGoodStillBeatsAFarOneThatIsNeededMore()
    {
        // Within SearchRadius the most needed good wins; past it, the nearest anything does. Wood is
        // needed more than stone, but the stone is within reach and the tree is not.
        var sim = Flat(160);
        Place(sim, 0, "storehouse", 10, 10);
        sim.World.Set(20 + AutomationSystem.SearchRadius - 5, Ground, 20, BlockType.Stone);
        sim.World.Set(20 + AutomationSystem.SearchRadius - 5, Ground + 1, 20, BlockType.Stone);
        TestWorlds.Tree(sim.World, 20 + 2 * AutomationSystem.SearchRadius, 20, 4);
        int w = Worker(sim, 0, 20, 20);
        Automate(sim, w);
        sim.Run(2 * Tps);
        Assert.Equal(UnitJob.Gather, sim.Units.Job[Index(sim, w)]);
        Assert.Equal(BlockType.Stone, sim.Units.JobBlock[Index(sim, w)]);
    }

    [Fact]
    public void AWorkerDropsOffWhatItCarriesFirst()
    {
        var sim = Flat();
        var store = Place(sim, 0, "storehouse", 10, 10);
        TestWorlds.Tree(sim.World, 30, 30, 4);
        int w = Worker(sim, 0, 30, 20);
        int i = Index(sim, w);
        sim.Units.Carry[i] = Good.Stone;
        sim.Units.CarryAmount[i] = 3;
        Automate(sim, w);
        sim.Run(2 * Tps);
        Assert.Equal(UnitJob.Deliver, sim.Units.Job[Index(sim, w)]);
        RunUntil(sim, () => store.Stock[(int)Good.Stone] == 3, 30 * Tps, "the stone to be dropped off");
        RunUntil(sim, () => sim.Units.Job[Index(sim, w)] == UnitJob.Gather, 3 * Tps, "the worker to go for wood");
    }

    [Fact]
    public void ASiteGetsItsBuilderAndTheOthersGather()
    {
        var sim = Flat();
        var store = Place(sim, 0, "storehouse", 10, 10);
        store.Stock[(int)Good.Wood] = 100;
        TestWorlds.Tree(sim.World, 30, 30, 4);
        TestWorlds.Tree(sim.World, 34, 34, 4);
        var w = new[] { Worker(sim, 0, 20, 20), Worker(sim, 0, 21, 20), Worker(sim, 0, 22, 20) };
        sim.Schedule(0, new BuildCommand(0, Array.Empty<int>(), "house", 20, 30)); // a site with nobody on it
        Automate(sim, w);
        sim.Run(2 * Tps);
        var jobs = w.Select(id => sim.Units.Job[Index(sim, id)]).ToList();
        Assert.Equal(1, AutomationSystem.MaxBuilders(sim.Content.Building("house")));
        Assert.Equal(1, jobs.Count(j => j == UnitJob.Build));
        Assert.Equal(2, jobs.Count(j => j == UnitJob.Gather));
        var site = Assert.Single(sim.Buildings.All, b => b.Def.Id == "house");
        RunUntil(sim, () => site.Active, 60 * Tps, "the house to be built");
    }

    [Fact]
    public void AFarmWithRoomIsWorkedBeforeAnythingIsGathered()
    {
        var sim = Flat();
        Place(sim, 0, "storehouse", 10, 10);
        var farm = Place(sim, 0, "farm", 20, 20);
        TestWorlds.Tree(sim.World, 40, 40, 4);
        var w = new[] { Worker(sim, 0, 12, 30), Worker(sim, 0, 13, 30), Worker(sim, 0, 14, 30) };
        Automate(sim, w);
        sim.Run(2 * Tps);
        var jobs = w.Select(id => sim.Units.Job[Index(sim, id)]).ToList();
        Assert.Equal(farm.Def.WorkerSlots, jobs.Count(j => j == UnitJob.Farm));
        Assert.Equal(1, jobs.Count(j => j == UnitJob.Gather));
    }

    [Fact]
    public void ASmelterGetsAHaulerOnlyWithSomethingToSmelt()
    {
        var sim = Flat();
        var store = Place(sim, 0, "storehouse", 10, 10);
        var smelter = Place(sim, 0, "smelter", 20, 10);
        TestWorlds.Tree(sim.World, 30, 30, 4);
        TestWorlds.Tree(sim.World, 36, 36, 4);
        int a = Worker(sim, 0, 12, 20), b = Worker(sim, 0, 13, 20);
        Automate(sim, a);
        sim.Run(2 * Tps);
        Assert.Equal(UnitJob.Gather, sim.Units.Job[Index(sim, a)]); // no ore or coal: nothing to haul

        store.Stock[(int)Good.IronOre] = 4;
        store.Stock[(int)Good.Coal] = 2;
        Automate(sim, b);
        sim.Run(2 * Tps);
        Assert.Equal(UnitJob.Haul, sim.Units.Job[Index(sim, b)]);
        Assert.Equal(smelter.Id, sim.Units.JobTarget[Index(sim, b)]);
        RunUntil(sim, () => smelter.SmeltTicks > 0, 60 * Tps, "the smelter to get its ore and coal");
    }

    [Theory]
    [InlineData(0, BlockType.BerryBush)]  // food short: berries first
    [InlineData(600, BlockType.Wood)]     // plenty of food: wood
    public void WorkersGatherWhatIsNeededMost(int food, BlockType expected)
    {
        var sim = Flat();
        var store = Place(sim, 0, "storehouse", 10, 10);
        store.Stock[(int)Good.Food] = food;
        TestWorlds.Tree(sim.World, 30, 24, 4);
        sim.World.Set(24, Ground + 1, 30, BlockType.BerryBush); // as far from the worker as the tree
        int w = Worker(sim, 0, 27, 27);
        Automate(sim, w);
        sim.Run(2 * Tps);
        Assert.Equal(UnitJob.Gather, sim.Units.Job[Index(sim, w)]);
        Assert.Equal(expected, sim.Units.JobBlock[Index(sim, w)]);
    }

    [Fact]
    public void WorkersSpreadOverTheGoodsByTheirShares()
    {
        var sim = Flat();
        var store = Place(sim, 0, "storehouse", 10, 10);
        store.Stock[(int)Good.Food] = 100; // enough that food is not short
        for (int k = 0; k < 6; k++) TestWorlds.Tree(sim.World, 30 + k % 3 * 6, 30 + k / 3 * 6, 4);
        for (int k = 0; k < 6; k++) sim.World.Set(12 + 2 * k, Ground + 1, 34, BlockType.BerryBush);
        TestWorlds.Box(sim.World, 40, Ground + 1, 12, 43, Ground + 2, 15, BlockType.Stone);
        var w = Enumerable.Range(0, 8).Select(k => Worker(sim, 0, 16 + k, 22)).ToArray();
        Automate(sim, w);
        sim.Run(2 * Tps);
        var kinds = w.Select(id => sim.Units.JobBlock[Index(sim, id)]).ToList();
        // Shares 3 food, 3 wood, 2 stone: eight workers split 3, 3 and 2.
        Assert.Equal(3, kinds.Count(k => k == BlockType.BerryBush));
        Assert.Equal(3, kinds.Count(k => k == BlockType.Wood));
        Assert.Equal(2, kinds.Count(k => k == BlockType.Stone));
    }

    [Fact]
    public void AMoveOrderHoldsAnAutomaticWorkerUntilItIsGivenAJob()
    {
        var sim = Flat();
        var store = Place(sim, 0, "storehouse", 10, 10);
        TestWorlds.Tree(sim.World, 24, 40, 4);
        TestWorlds.Tree(sim.World, 40, 24, 4); // more than a gatherer's own search away from the first
        int w = Worker(sim, 0, 20, 20);
        Automate(sim, w);
        sim.Schedule(0, new MoveCommand(0, new[] { w }, 50, 50));
        sim.Run(15 * Tps);
        int i = Index(sim, w);
        Assert.True(sim.Units.Held[i]);
        Assert.Equal(UnitJob.None, sim.Units.Job[i]);
        Assert.Equal(UnitOrder.Idle, sim.Units.Order[i]); // it got there and waits

        // Given a job, it is automatic again once the job is done: it finds the other tree by itself.
        sim.Schedule(sim.Tick, new GatherCommand(0, new[] { w }, 24, Ground + 1, 40));
        sim.Step();
        Assert.False(sim.Units.Held[Index(sim, w)]);
        // A felled tree is cleared as rubble, over two loads.
        RunUntil(sim, () => store.Stock[(int)Good.Wood] == 4 * Harvest.WoodPerTreeRubble, 60 * Tps, "the first tree's wood");
        RunUntil(sim, () => sim.Units.Job[Index(sim, w)] == UnitJob.Gather && sim.Units.JobX[Index(sim, w)] == 40,
            10 * Tps, "the worker to go for the second tree");

        // Stopped, it waits again; switched to automatic, it carries on.
        sim.Schedule(sim.Tick, new StopCommand(0, new[] { w }));
        sim.Run(5 * Tps);
        Assert.Equal(UnitJob.None, sim.Units.Job[Index(sim, w)]);
        Automate(sim, w);
        sim.Run(2 * Tps);
        Assert.Equal(UnitJob.Gather, sim.Units.Job[Index(sim, w)]);
    }

    [Fact]
    public void AutomateAllCoversEveryWorkerAndThoseTrainedLater()
    {
        var sim = Flat();
        var hall = Place(sim, 0, "town_hall", 10, 10);
        hall.Stock[(int)Good.Food] = 100;
        int w = Worker(sim, 0, 30, 30);
        int spearman = sim.SpawnUnit(0, "spearman", 32, 30);
        int theirs = Worker(sim, 1, 50, 50);
        sim.Schedule(0, new AutomateAllCommand(0, true));
        sim.Schedule(0, new TrainCommand(0, hall.Id, "worker"));
        sim.Step();
        Assert.True(sim.Economy.AutoWorkers(0));
        Assert.True(sim.Units.Auto[Index(sim, w)]);
        Assert.False(sim.Units.Auto[Index(sim, spearman)]); // soldiers are not workers
        Assert.False(sim.Units.Auto[Index(sim, theirs)]);   // nor are another player's
        RunUntil(sim, () => sim.Economy.Population(0) == 3, 15 * Tps, "the new worker");
        int trained = sim.Units.Id.Take(sim.Units.Count).Max();
        Assert.True(sim.Units.Auto[Index(sim, trained)]);

        sim.Schedule(sim.Tick, new AutomateAllCommand(0, false));
        sim.Step();
        Assert.False(sim.Economy.AutoWorkers(0));
        Assert.False(sim.Units.Auto[Index(sim, w)]);
        Assert.False(sim.Units.Auto[Index(sim, trained)]);
    }

    [Fact]
    public void OnlyThePlayerCanMakeTheirWorkersAutomatic()
    {
        var sim = Flat();
        int mine = Worker(sim, 0, 20, 20), theirs = Worker(sim, 1, 22, 20);
        sim.Schedule(0, new AutomateCommand(1, new[] { mine, theirs }, true));
        sim.Schedule(0, new AutomateAllCommand(7, true)); // a player who is not in the match
        sim.Step();
        Assert.False(sim.Units.Auto[Index(sim, mine)]);
        Assert.True(sim.Units.Auto[Index(sim, theirs)]);
        Assert.False(sim.Economy.AutoWorkers(7));
    }

    [Fact]
    public void AJobThatFailsIsNotGivenAgainForAWhile()
    {
        var sim = Flat();
        Place(sim, 0, "storehouse", 10, 10);
        // A berry bush walled in where nobody can get to it, and another further off in the open.
        sim.World.Set(30, Ground + 1, 20, BlockType.BerryBush);
        TestWorlds.Box(sim.World, 28, Ground + 1, 18, 32, Ground + 3, 18, BlockType.StoneBrick);
        TestWorlds.Box(sim.World, 28, Ground + 1, 22, 32, Ground + 3, 22, BlockType.StoneBrick);
        TestWorlds.Box(sim.World, 28, Ground + 1, 19, 28, Ground + 3, 21, BlockType.StoneBrick);
        TestWorlds.Box(sim.World, 32, Ground + 1, 19, 32, Ground + 3, 21, BlockType.StoneBrick);
        TestWorlds.Box(sim.World, 28, Ground + 4, 18, 32, Ground + 4, 22, BlockType.StoneBrick);
        sim.World.Set(20, Ground + 1, 45, BlockType.BerryBush);
        int w = Worker(sim, 0, 22, 20);
        Automate(sim, w);
        RunUntil(sim, () => sim.Units.Job[Index(sim, w)] == UnitJob.Gather, 2 * Tps, "a job");
        Assert.Equal(30, sim.Units.JobX[Index(sim, w)]); // the nearer one first
        RunUntil(sim, () => sim.Units.Job[Index(sim, w)] == UnitJob.Gather && sim.Units.JobZ[Index(sim, w)] == 45,
            10 * Tps, "the worker to give up on the walled-in bush and go for the other");
        RunUntil(sim, () => sim.World.Get(20, Ground + 1, 45) == BlockType.Air && sim.Units.Job[Index(sim, w)] == UnitJob.None,
            30 * Tps, "the open bush to be picked and its food dropped off");
        // With nothing else to pick, it does not go back to the walled-in bush for a minute.
        long gaveUp = sim.Tick;
        while (sim.Tick < gaveUp + 30 * Tps)
        {
            sim.Step();
            Assert.Equal(UnitJob.None, sim.Units.Job[Index(sim, w)]);
        }
    }

    /// <summary>
    /// A flat world with ground at level 10 and soil instead of stone under (x0..x1, z0..z1), down to
    /// <paramref name="soilFrom"/>, and a pit six blocks deep at 30..33 x 30..33 in it. Workers climb one
    /// block and drop three, so nobody gets into the pit or out of it.
    /// </summary>
    private static Simulation PitWorld(int soilFrom)
    {
        const int ground = 10;
        var sim = TestWorlds.Sim(TestWorlds.Flat(64, 24, 64, ground));
        TestWorlds.Box(sim.World, 26, soilFrom, 26, 37, ground - 1, 37, BlockType.Soil);
        TestWorlds.Box(sim.World, 30, 5, 30, 33, ground, 33, BlockType.Air);
        return sim;
    }

    private static int GatherJobs(Simulation sim, Func<int, int, bool> where) =>
        sim.Automation.Record!.Count(r => r.Command is GatherCommand g && where(g.X, g.Z));

    [Fact]
    public void AWorkerKeepsAwayFromAPitItCannotGetInto()
    {
        // The pit's stone floor is the nearest stone (soil around it, so nothing else there to dig); a
        // small outcrop of stone in the open further off (issue #36).
        var sim = PitWorld(soilFrom: 5);
        TestWorlds.Box(sim.World, 50, 11, 50, 51, 11, 51, BlockType.Stone);
        Place(sim, 0, "storehouse", 18, 46);
        int w = Worker(sim, 0, 26, 31);
        sim.Automation.Record = new();
        Automate(sim, w);
        static bool InPit(int x, int z) => x is >= 30 and <= 33 && z is >= 30 and <= 33;
        RunUntil(sim, () => sim.World.Get(50, 11, 50) == BlockType.Air, 60 * Tps, "the outcrop to be dug");
        // The pit's stone can only be worked from down in the pit, which it could not climb out of again, so
        // it is never sent there.
        Assert.Equal(0, GatherJobs(sim, InPit));
        sim.Run(AutomationSystem.AvoidTicks);
        Assert.Equal(0, GatherJobs(sim, InPit));
    }

    [Fact]
    public void AWorkerCutOffFromItsWorkTriesLessAndLessOften()
    {
        // The worker is on an island in a moat too deep to wade, with its storehouse, and stone out of its
        // reach beyond the water in six places, far enough apart that giving up on one says nothing about
        // the others. (The island is too big to count as a pit it could dig its way out of.)
        const int ground = 10;
        var sim = TestWorlds.Sim(TestWorlds.Flat(64, 24, 64, ground));
        TestWorlds.Box(sim.World, 16, 7, 16, 47, ground, 47, BlockType.Water);
        TestWorlds.Box(sim.World, 24, 1, 24, 39, ground - 1, 39, BlockType.Soil);
        TestWorlds.Box(sim.World, 24, ground, 24, 39, ground, 39, BlockType.Grass);
        foreach (var (x, z) in new[] { (5, 5), (5, 58), (58, 5), (58, 58), (31, 5), (31, 58) })
            sim.World.Set(x, 11, z, BlockType.Stone);
        Place(sim, 0, "storehouse", 26, 26);
        int w = Worker(sim, 0, 31, 31);
        sim.Automation.Record = new();
        Automate(sim, w);
        sim.Run(2 * 60 * Tps);
        // Two quick tries, then a longer wait after each failure (10, 20, 40 seconds...), not a new job
        // every few seconds.
        Assert.InRange(GatherJobs(sim, (_, _) => true), 2, 5);
    }

    [Fact]
    public void AWorkerStuckInAPitDigsItsWayOutAndGetsToWork()
    {
        // The worker is at the bottom of a pit six blocks deep, with soil around and under it, and stone
        // out of its reach up on the ground: it digs steps up out of the pit, then goes to the stone.
        var sim = PitWorld(soilFrom: 1);
        foreach (var (x, z) in new[] { (5, 5), (5, 58), (58, 5), (58, 58), (31, 5), (31, 58) })
            sim.World.Set(x, 11, z, BlockType.Stone);
        Place(sim, 0, "storehouse", 10, 31);
        int w = Worker(sim, 0, 31, 31);
        Assert.True(sim.Units.Y[Index(sim, w)] < 10, "it starts in the pit");
        Automate(sim, w);
        RunUntil(sim, () => sim.Units.Job[Index(sim, w)] == UnitJob.Tunnel, 30 * Tps, "the worker to start digging out");
        RunUntil(sim, () => sim.Units.Y[Index(sim, w)] > 10, 3 * 60 * Tps, "the worker to get out of the pit");
        RunUntil(sim, () => sim.Units.CarryAmount[Index(sim, w)] > 0 || sim.Economy.Player(0).Gathered[(int)Good.Stone] > 0,
            2 * 60 * Tps, "the worker to gather stone");
    }

    // --- Repeat ------------------------------------------------------------------------------------

    [Fact]
    public void ARepeatingQueueTrainsAgainWhileThePlayerCanPay()
    {
        var sim = Flat();
        var hall = Place(sim, 0, "town_hall", 10, 10);
        int Food() => hall.Stock[(int)Good.Food];
        hall.Stock[(int)Good.Food] = 61; // two workers, and one left over
        sim.Schedule(0, new TrainCommand(0, hall.Id, "worker"));
        sim.Schedule(0, new RepeatCommand(0, hall.Id, true));
        RunUntil(sim, () => sim.Economy.Population(0) == 1, 15 * Tps, "the first worker");
        RunUntil(sim, () => hall.Queue.Count == 1, 2 * Tps, "the worker to be queued again");
        Assert.Equal(1, Food());
        Assert.Empty(hall.Owed);

        RunUntil(sim, () => sim.Economy.Population(0) == 2, 15 * Tps, "the second worker");
        sim.Run(2 * Tps);
        Assert.Empty(hall.Queue); // it cannot pay
        Assert.Equal(new[] { sim.Content.Unit("worker").Index }, hall.Owed);

        hall.Stock[(int)Good.Food] = 29; // a food short of a worker
        sim.Run(2 * Tps);
        Assert.Empty(hall.Queue);
        hall.Stock[(int)Good.Food] = 30; // just enough: units eat nothing once trained (issue #21)
        sim.Run(2 * Tps);
        Assert.Single(hall.Queue);
        Assert.Equal(0, Food());
        Assert.Empty(hall.Owed);

        // Off: the worker in training is the last.
        sim.Schedule(sim.Tick, new RepeatCommand(0, hall.Id, false));
        hall.Stock[(int)Good.Food] = 500;
        RunUntil(sim, () => sim.Economy.Population(0) == 3, 15 * Tps, "the third worker");
        sim.Run(2 * Tps);
        Assert.Empty(hall.Queue);
        Assert.Empty(hall.Owed);
        Assert.False(hall.Repeat);
    }

    [Fact]
    public void OnlyTheOwnerSetsRepeatAndOnlyWhereUnitsAreTrained()
    {
        var sim = Flat();
        var hall = Place(sim, 0, "town_hall", 10, 10);
        var house = Place(sim, 0, "house", 30, 10);
        sim.Schedule(0, new RepeatCommand(1, hall.Id, true));
        sim.Schedule(0, new RepeatCommand(0, house.Id, true));
        sim.Schedule(0, new RepeatCommand(0, 999, true));
        sim.Step();
        Assert.False(hall.Repeat);
        Assert.False(house.Repeat);
    }

    // --- Stances -----------------------------------------------------------------------------------

    private static int Hp(Simulation sim, int id) => sim.Units.Hp[Index(sim, id)];

    [Fact]
    public void ASoldierHoldingItsGroundDoesNotChase()
    {
        var sim = Flat();
        int holder = sim.SpawnUnit(0, "spearman", 20, 20), chaser = sim.SpawnUnit(0, "spearman", 20, 40);
        int a = sim.SpawnUnit(1, "spearman", 25, 20), b = sim.SpawnUnit(1, "spearman", 25, 40); // in sight, out of reach
        int full = Hp(sim, holder);
        sim.Schedule(0, new StanceCommand(0, new[] { holder }, UnitStance.Hold));
        sim.Schedule(0, new StanceCommand(1, new[] { a, b }, UnitStance.Hold));
        sim.Run(5 * Tps);
        // The two holding their ground look at each other; the aggressive one goes for its enemy.
        Assert.Equal(Fix64.FromInt(20) + Fix64.Half, sim.Units.X[Index(sim, holder)]);
        Assert.Equal(full, Hp(sim, holder));
        Assert.Equal(full, Hp(sim, a));
        Assert.True(Hp(sim, b) < full);

        // The enemy steps into reach: now the holder fights, where it stands.
        sim.Schedule(sim.Tick, new MoveCommand(1, new[] { a }, 21, 20));
        sim.Run(5 * Tps);
        Assert.True(Hp(sim, a) < full);
        Assert.True(Fix64.Abs(sim.Units.X[Index(sim, holder)] - (Fix64.FromInt(20) + Fix64.Half)) < Fix64.Half);
    }

    [Fact]
    public void ASoldierOnTheRetreatStanceWalksHomeWhenHurt()
    {
        var sim = Flat();
        var hall = Place(sim, 0, "town_hall", 4, 4);
        int hurt = sim.SpawnUnit(0, "spearman", 40, 40);   // retreat stance, hurt
        int fresh = sim.SpawnUnit(0, "spearman", 40, 28);  // retreat stance, not hurt
        int stays = sim.SpawnUnit(0, "spearman", 40, 52);  // aggressive, hurt
        foreach (int id in new[] { hurt, stays }) sim.Units.Hp[Index(sim, id)] = 26; // a third of 80, or less
        var enemies = new[] { sim.SpawnUnit(1, "spearman", 41, 40), sim.SpawnUnit(1, "spearman", 41, 28), sim.SpawnUnit(1, "spearman", 41, 52) };
        sim.Schedule(0, new StanceCommand(0, new[] { hurt, fresh }, UnitStance.Retreat));
        sim.Schedule(0, new StanceCommand(1, enemies, UnitStance.Hold));
        var given = new List<(long Tick, Command Command)>();
        sim.Automation.Record = given;
        sim.Run(3 * Tps);

        var home = Assert.Single(given).Command;
        Assert.Equal(new[] { hurt }, Assert.IsType<MoveCommand>(home).Units);
        Assert.Equal(UnitOrder.Move, sim.Units.Order[Index(sim, hurt)]);
        // The others fight on where they stand.
        Assert.Equal(enemies[1], sim.Units.Target[Index(sim, fresh)]);
        Assert.True(sim.Units.X[Index(sim, fresh)] > Fix64.FromInt(38));
        Assert.True(!sim.Units.Exists(stays) || sim.Units.X[Index(sim, stays)] > Fix64.FromInt(38));

        RunUntil(sim, () => hall.ColumnDistance(sim.Units.X[Index(sim, hurt)].FloorToInt(), sim.Units.Z[Index(sim, hurt)].FloorToInt())
            <= CombatSystem.HomeRadius, 30 * Tps, "the hurt soldier to get home");
        Assert.True(Hp(sim, hurt) > 0);
    }

    [Fact]
    public void StancesAreForSoldiers()
    {
        var sim = Flat();
        int worker = Worker(sim, 0, 20, 20), spearman = sim.SpawnUnit(0, "spearman", 22, 20), theirs = sim.SpawnUnit(1, "spearman", 40, 40);
        sim.Schedule(0, new StanceCommand(0, new[] { worker, spearman, theirs }, UnitStance.Hold));
        sim.Schedule(0, new StanceCommand(0, new[] { spearman }, (UnitStance)7)); // not a stance
        sim.Step();
        Assert.Equal(UnitStance.Aggressive, sim.Units.Stance[Index(sim, worker)]); // workers fight only when ordered to
        Assert.Equal(UnitStance.Hold, sim.Units.Stance[Index(sim, spearman)]);
        Assert.Equal(UnitStance.Aggressive, sim.Units.Stance[Index(sim, theirs)]);
    }

    // --- Houses and farms --------------------------------------------------------------------------

    [Fact]
    public void AutomationPutsUpAHouseWhenHousingHoldsBackTraining()
    {
        var sim = Flat();
        var hall = Place(sim, 0, "town_hall", 10, 10);
        hall.Stock[(int)Good.Food] = 500;
        hall.Stock[(int)Good.Wood] = 100;
        for (int k = 0; k < 25; k++) Worker(sim, 0, 8 + k % 13, 30 + k / 13 * 2); // as many people as the hall houses
        sim.Schedule(0, new AutomateAllCommand(0, true));
        sim.Schedule(0, new TrainCommand(0, hall.Id, "worker"));
        sim.Schedule(0, new RepeatCommand(0, hall.Id, true));
        RunUntil(sim, () => sim.Buildings.All.Any(b => b.Def.Id == "house"), 3 * Tps, "a house site");
        var house = sim.Buildings.All.Single(b => b.Def.Id == "house");
        Assert.Equal(BuildingState.Site, house.State);
        Assert.Equal(70, hall.Stock[(int)Good.Wood]);
        // It keeps clear of the hall's door, and the hall of its door.
        Assert.False(house.X <= hall.MaxX && house.MaxX >= hall.X && house.Z <= hall.MaxZ + 3 && house.MaxZ >= hall.MaxZ + 1);
        Assert.False(hall.X <= house.MaxX && hall.MaxX >= house.X && hall.Z <= house.MaxZ + 3 && hall.MaxZ >= house.MaxZ + 1);

        RunUntil(sim, () => house.Active, 60 * Tps, "the house to be built");
        RunUntil(sim, () => sim.Economy.Population(0) == 26, 15 * Tps, "the waiting worker to be trained");
        Assert.Single(sim.Buildings.All, b => b.Def.Id == "house");
    }

    [Fact]
    public void AutomationLaysOutAFarmWhenFoodRunsShort()
    {
        var sim = Flat();
        var hall = Place(sim, 0, "town_hall", 10, 10);
        hall.Stock[(int)Good.Food] = 20;
        hall.Stock[(int)Good.Wood] = 100;
        for (int k = 0; k < 4; k++) Worker(sim, 0, 8 + k, 30);
        sim.Schedule(0, new AutomateAllCommand(0, true));
        sim.Run(5 * Tps);
        Assert.DoesNotContain(sim.Buildings.All, b => b.Def.Id == "farm"); // not without farming

        sim.Economy.Grant(0, sim.Content.Tech("farming"));
        RunUntil(sim, () => sim.Buildings.All.Any(b => b.Def.Id == "farm"), 3 * Tps, "a farm site");
        var farm = sim.Buildings.All.Single(b => b.Def.Id == "farm");
        RunUntil(sim, () => farm.Active, 30 * Tps, "the farm to be built");
        int Farmers() => Enumerable.Range(0, sim.Units.Count).Count(i => sim.Units.Job[i] == UnitJob.Farm);
        RunUntil(sim, () => Farmers() == farm.Def.WorkerSlots, 3 * Tps, "farmers");
        RunUntil(sim, () => sim.Economy.Player(0).Gathered[(int)Good.Food] > 0, 40 * Tps, "food from the farm");
        Assert.Single(sim.Buildings.All, b => b.Def.Id == "farm"); // four workers: one farm is enough
    }

    [Fact]
    public void OnlyAPlayerWhoseWorkersAreAllAutomaticGetsBuildings()
    {
        var sim = Flat();
        var hall = Place(sim, 0, "town_hall", 10, 10);
        hall.Stock[(int)Good.Food] = 500;
        hall.Stock[(int)Good.Wood] = 100;
        var w = Enumerable.Range(0, 10).Select(k => Worker(sim, 0, 8 + k, 30)).ToArray();
        Automate(sim, w); // each worker, but not the player's setting
        sim.Schedule(0, new TrainCommand(0, hall.Id, "worker"));
        sim.Run(10 * Tps);
        Assert.Single(sim.Buildings.All);
    }

    // --- The command log ---------------------------------------------------------------------------

    [Fact]
    public void AutomationCommandsRoundTripThroughTheLog()
    {
        var log = new CommandLog { Seed = 5, SizeX = 64, SizeY = 32, SizeZ = 64, Ticks = 100 };
        log.Add(1, new AutomateCommand(0, new[] { 1, 2 }, true));
        log.Add(2, new AutomateCommand(1, new[] { 3 }, false));
        log.Add(3, new AutomateAllCommand(0, true));
        log.Add(3, new AutomateAllCommand(1, false));
        log.Add(4, new RepeatCommand(0, 7, true));
        log.Add(5, new RepeatCommand(1, 8, false));
        log.Add(6, new StanceCommand(0, new[] { 4, 5 }, UnitStance.Hold));
        log.Add(7, new StanceCommand(0, new[] { 6 }, UnitStance.Retreat));
        log.Add(8, new StanceCommand(1, new[] { 9 }, UnitStance.Aggressive));
        var text = log.ToString();
        Assert.Contains("1 automate 0 on 1,2\n", text);
        Assert.Contains("3 automate-all 1 off\n", text);
        Assert.Contains("4 repeat 0 7 on\n", text);
        Assert.Contains("6 stance 0 hold 4,5\n", text);
        var copy = CommandLog.Parse(text);
        Assert.Equal(log.Commands, copy.Commands);
        Assert.Equal(text, copy.ToString());
        Assert.Throws<FormatException>(() => CommandLog.Parse(text.Replace("automate 0 on", "automate 0 yes")));
        Assert.Throws<FormatException>(() => CommandLog.Parse(text.Replace("hold", "sleep")));
        Assert.Throws<NotSupportedException>(() =>
        {
            var bad = new CommandLog { Seed = 1, SizeX = 8, SizeY = 8, SizeZ = 8 };
            bad.Add(0, new StanceCommand(0, new[] { 1 }, (UnitStance)7));
            return bad.ToString();
        });
    }

    [Fact]
    public void ScriptedMatchesUseAutomation()
    {
        var log = ScriptedMatch.Generate(2, 256, 64, 256, ticks: 3000, startUnits: 12); // a seed whose 3000 ticks pick every kind of setting
        Assert.Contains(log.Commands, c => c.Command is AutomateCommand);
        Assert.Contains(log.Commands, c => c.Command is AutomateAllCommand);
        Assert.Contains(log.Commands, c => c.Command is RepeatCommand);
        Assert.Contains(log.Commands, c => c.Command is StanceCommand);
        var a = MatchReplay.Run(log, hashEvery: 500);
        var b = MatchReplay.Run(CommandLog.Parse(log.ToString()), hashEvery: 500);
        Assert.Equal(a, b);
    }
}
