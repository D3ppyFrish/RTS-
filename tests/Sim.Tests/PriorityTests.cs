using System;
using System.Linq;
using Rts.Sim.Automation;
using Rts.Sim.Economy;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Builder priority lists: the kinds of work each automatic worker takes on its own, in order.</summary>
public class PriorityTests
{
    private const int Tps = Simulation.TicksPerSecond;
    private const int Ground = 3;

    private static Simulation Flat(int size = 64) => TestWorlds.Sim(TestWorlds.Flat(size, 16, size, Ground));

    private static Building Place(Simulation sim, int owner, string def, int x, int z) =>
        sim.Economy.PlaceFinished(owner, sim.Content.Building(def), x, z) ?? throw new InvalidOperationException($"{def} does not fit");

    private static int Worker(Simulation sim, int owner, int x, int z) => sim.SpawnUnit(owner, "worker", x, z);

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

    private static UnitJob Job(Simulation sim, int id) => sim.Units.Job[Index(sim, id)];

    // --- Lists -------------------------------------------------------------------------------------

    [Fact]
    public void ListsPackInOrderAndChange()
    {
        var list = WorkPriorities.Pack(WorkKind.Wood, WorkKind.Build, WorkKind.Wood, WorkKind.Stone);
        Assert.Equal(new[] { WorkKind.Wood, WorkKind.Build, WorkKind.Stone }, WorkPriorities.Unpack(list));
        Assert.Equal(new[] { WorkKind.Build, WorkKind.Wood, WorkKind.Stone },
            WorkPriorities.Unpack(WorkPriorities.Move(list, WorkKind.Build, -1)));
        Assert.Equal(list, WorkPriorities.Move(list, WorkKind.Wood, -1)); // already at the top
        Assert.Equal(list, WorkPriorities.Move(list, WorkKind.Stone, +1)); // already at the bottom
        Assert.Equal(new[] { WorkKind.Wood, WorkKind.Build, WorkKind.Stone, WorkKind.Coal },
            WorkPriorities.Unpack(WorkPriorities.Toggle(list, WorkKind.Coal)));
        Assert.Equal(new[] { WorkKind.Wood, WorkKind.Stone }, WorkPriorities.Unpack(WorkPriorities.Toggle(list, WorkKind.Build)));

        // Every kind fits; an empty list is not the same as following the default.
        var full = WorkPriorities.Pack(WorkPriorities.All.Reverse().ToArray());
        Assert.Equal(WorkPriorities.All.Reverse(), WorkPriorities.Unpack(full));
        Assert.Equal(WorkPriorities.Nothing, WorkPriorities.Pack());
        Assert.NotEqual(WorkPriorities.UseDefault, WorkPriorities.Pack());
        Assert.Empty(WorkPriorities.Unpack(WorkPriorities.Nothing));
        Assert.Equal(new[] { WorkKind.Build, WorkKind.Farm, WorkKind.Smelt, WorkKind.Needed },
            WorkPriorities.Unpack(WorkPriorities.Standard));
    }

    // --- Following the list ------------------------------------------------------------------------

    [Fact]
    public void AWorkerTakesTheFirstKindOfWorkOnItsList()
    {
        var sim = Flat();
        var store = Place(sim, 0, "storehouse", 10, 10);
        store.Stock[(int)Good.Wood] = 100;
        TestWorlds.Tree(sim.World, 30, 30, 4);
        sim.World.Set(24, Ground + 1, 34, BlockType.BerryBush);
        sim.Schedule(0, new BuildCommand(0, Array.Empty<int>(), "house", 20, 30)); // a site with nobody on it
        int usual = Worker(sim, 0, 20, 20), lumberjack = Worker(sim, 0, 21, 20), forager = Worker(sim, 0, 22, 20);
        sim.Schedule(0, new AutomateCommand(0, new[] { usual, lumberjack, forager }, true));
        sim.Schedule(0, new PriorityCommand(0, new[] { lumberjack }, WorkPriorities.Pack(WorkKind.Wood, WorkKind.Build)));
        sim.Schedule(0, new PriorityCommand(0, new[] { forager }, WorkPriorities.Pack(WorkKind.Food)));
        sim.Run(2 * Tps);

        Assert.Equal(UnitJob.Build, Job(sim, usual)); // the standard list builds first
        Assert.Equal(UnitJob.Gather, Job(sim, lumberjack));
        Assert.Equal(BlockType.Wood, sim.Units.JobBlock[Index(sim, lumberjack)]);
        Assert.Equal(UnitJob.Gather, Job(sim, forager));
        Assert.Equal(BlockType.BerryBush, sim.Units.JobBlock[Index(sim, forager)]);
    }

    [Fact]
    public void AKindOfWorkThereIsNoneOfIsPassedOver()
    {
        var sim = Flat();
        var store = Place(sim, 0, "storehouse", 10, 10);
        store.Stock[(int)Good.Wood] = 100;
        TestWorlds.Tree(sim.World, 30, 30, 4);
        sim.Schedule(0, new BuildCommand(0, Array.Empty<int>(), "house", 20, 30));
        int w = Worker(sim, 0, 20, 20);
        sim.Schedule(0, new AutomateCommand(0, new[] { w }, true));
        // No stone anywhere on a grass world: on to building.
        sim.Schedule(0, new PriorityCommand(0, new[] { w }, WorkPriorities.Pack(WorkKind.Stone, WorkKind.Build, WorkKind.Wood)));
        sim.Run(2 * Tps);
        Assert.Equal(UnitJob.Build, Job(sim, w));
    }

    [Fact]
    public void WorkLeftOffTheListIsNeverTaken()
    {
        var sim = Flat();
        var store = Place(sim, 0, "storehouse", 10, 10);
        store.Stock[(int)Good.Wood] = 100;
        TestWorlds.Tree(sim.World, 30, 30, 4);
        sim.Schedule(0, new BuildCommand(0, Array.Empty<int>(), "house", 20, 30));
        int gatherer = Worker(sim, 0, 20, 20), idle = Worker(sim, 0, 22, 20);
        sim.Schedule(0, new AutomateCommand(0, new[] { gatherer, idle }, true));
        sim.Schedule(0, new PriorityCommand(0, new[] { gatherer }, WorkPriorities.Pack(WorkKind.Needed)));
        sim.Schedule(0, new PriorityCommand(0, new[] { idle }, WorkPriorities.Nothing));
        sim.Run(5 * Tps);
        Assert.Equal(UnitJob.Gather, Job(sim, gatherer));
        Assert.Equal(UnitJob.None, Job(sim, idle));
        var site = Assert.Single(sim.Buildings.All, b => b.Def.Id == "house");
        Assert.Equal(BuildingState.Site, site.State);
    }

    [Fact]
    public void AnAutomaticWorkerStillUnloadsWhateverItsList()
    {
        var sim = Flat();
        var store = Place(sim, 0, "storehouse", 10, 10);
        int w = Worker(sim, 0, 30, 20);
        int i = Index(sim, w);
        sim.Units.Carry[i] = Good.Stone;
        sim.Units.CarryAmount[i] = 3;
        sim.Schedule(0, new AutomateCommand(0, new[] { w }, true));
        sim.Schedule(0, new PriorityCommand(0, new[] { w }, WorkPriorities.Nothing));
        RunUntil(sim, () => store.Stock[(int)Good.Stone] == 3, 30 * Tps, "the stone to be dropped off");
    }

    // --- The default list --------------------------------------------------------------------------

    [Fact]
    public void TheDefaultListIsForWorkersWithoutOneOfTheirOwnAndThoseTrainedLater()
    {
        var sim = Flat();
        var hall = Place(sim, 0, "town_hall", 10, 10);
        hall.Stock[(int)Good.Food] = 500;
        hall.Stock[(int)Good.Wood] = 100;
        TestWorlds.Tree(sim.World, 40, 40, 4);
        int follower = Worker(sim, 0, 20, 30), own = Worker(sim, 0, 22, 30);
        var woodFirst = WorkPriorities.Pack(WorkKind.Wood);
        var buildFirst = WorkPriorities.Pack(WorkKind.Build, WorkKind.Needed);
        sim.Schedule(0, new AutomateAllCommand(0, true));
        sim.Schedule(0, new PriorityCommand(0, new[] { own }, buildFirst));
        sim.Schedule(0, new DefaultPriorityCommand(0, woodFirst));
        sim.Schedule(0, new TrainCommand(0, hall.Id, "worker"));
        sim.Step();
        Assert.Equal(woodFirst, sim.Economy.Player(0).DefaultPriorities);
        Assert.Equal(woodFirst, sim.Automation.PrioritiesOf(Index(sim, follower)));
        Assert.Equal(buildFirst, sim.Automation.PrioritiesOf(Index(sim, own)));

        RunUntil(sim, () => sim.Units.Count == 3, 60 * Tps, "a worker to be trained");
        int trained = sim.Units.Count - 1;
        Assert.Equal(WorkPriorities.UseDefault, sim.Units.Priorities[trained]);
        Assert.Equal(woodFirst, sim.Automation.PrioritiesOf(trained));

        // Back to the standard default, and the worker with its own list back on the default.
        sim.Schedule(sim.Tick, new DefaultPriorityCommand(0, WorkPriorities.UseDefault));
        sim.Schedule(sim.Tick, new PriorityCommand(0, new[] { own }, WorkPriorities.UseDefault));
        sim.Step();
        Assert.Equal(WorkPriorities.Standard, sim.Economy.Player(0).DefaultPriorities);
        Assert.Equal(WorkPriorities.Standard, sim.Automation.PrioritiesOf(Index(sim, own)));
    }

    [Fact]
    public void OnlyThePlayerSetsTheirWorkersLists()
    {
        var sim = Flat();
        int mine = Worker(sim, 0, 20, 20), theirs = Worker(sim, 1, 24, 20);
        int soldier = sim.SpawnUnit(0, "spearman", 26, 20);
        var list = WorkPriorities.Pack(WorkKind.Coal);
        sim.Schedule(0, new PriorityCommand(1, new[] { mine, theirs }, list));
        sim.Schedule(0, new PriorityCommand(0, new[] { soldier }, list));
        sim.Schedule(0, new DefaultPriorityCommand(7, list)); // a player who is not in the match
        sim.Step();
        Assert.Equal(WorkPriorities.UseDefault, sim.Units.Priorities[Index(sim, mine)]);
        Assert.Equal(list, sim.Units.Priorities[Index(sim, theirs)]);
        Assert.Equal(WorkPriorities.UseDefault, sim.Units.Priorities[Index(sim, soldier)]);
    }

    // --- State and the log -------------------------------------------------------------------------

    [Fact]
    public void ListsArePartOfTheState()
    {
        Simulation Make(bool setList)
        {
            var sim = Flat();
            int w = Worker(sim, 0, 20, 20);
            if (setList) sim.Schedule(0, new PriorityCommand(0, new[] { w }, WorkPriorities.Pack(WorkKind.Wood)));
            sim.Step();
            return sim;
        }
        Assert.Equal(Make(true).StateHash(), Make(true).StateHash());
        Assert.NotEqual(Make(true).StateHash(), Make(false).StateHash());

        var a = Flat();
        var b = Flat();
        Worker(a, 0, 20, 20);
        Worker(b, 0, 20, 20);
        a.Schedule(0, new DefaultPriorityCommand(0, WorkPriorities.Pack(WorkKind.Stone)));
        a.Step();
        b.Step();
        Assert.NotEqual(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void PriorityCommandsRoundTripThroughTheLog()
    {
        var log = new CommandLog { Seed = 5, SizeX = 64, SizeY = 32, SizeZ = 64, Ticks = 100 };
        log.Add(1, new PriorityCommand(0, new[] { 1, 2 }, WorkPriorities.Pack(WorkKind.Wood, WorkKind.IronOre, WorkKind.Build)));
        log.Add(2, new PriorityCommand(1, new[] { 3 }, WorkPriorities.UseDefault));
        log.Add(2, new PriorityCommand(1, new[] { 4 }, WorkPriorities.Nothing));
        log.Add(3, new DefaultPriorityCommand(0, WorkPriorities.Standard));
        log.Add(3, new DefaultPriorityCommand(1, WorkPriorities.UseDefault));
        var text = log.ToString();
        Assert.Contains("1 priority 0 wood,iron-ore,build 1,2\n", text);
        Assert.Contains("2 priority 1 default 3\n", text);
        Assert.Contains("2 priority 1 none 4\n", text);
        Assert.Contains("3 default-priority 0 build,farm,smelt,needed\n", text);
        var copy = CommandLog.Parse(text);
        Assert.Equal(log.Commands, copy.Commands);
        Assert.Equal(text, copy.ToString());
        Assert.Throws<FormatException>(() => CommandLog.Parse(text.Replace("wood,iron-ore", "wood,gold")));
    }

    [Fact]
    public void AnEconomyWithListsReplaysTheSameWithAutomationOff()
    {
        (ulong Hash, System.Collections.Generic.List<(long, Command)> Given) Run(bool automation,
            System.Collections.Generic.List<(long Tick, Command Command)>? replay)
        {
            var sim = Flat();
            sim.Automation.Enabled = automation;
            var recorded = new System.Collections.Generic.List<(long, Command)>();
            if (automation) sim.Automation.Record = recorded;
            var hall = Place(sim, 0, "town_hall", 10, 10);
            hall.Stock[(int)Good.Wood] = 200;
            for (int k = 0; k < 4; k++) sim.World.Set(12 + 2 * k, Ground + 1, 30, BlockType.BerryBush);
            TestWorlds.Tree(sim.World, 40, 40, 4);
            TestWorlds.Tree(sim.World, 44, 36, 4);
            var w = Enumerable.Range(0, 4).Select(k => Worker(sim, 0, 20 + k, 30)).ToArray();
            sim.Schedule(0, new AutomateAllCommand(0, true));
            sim.Schedule(0, new DefaultPriorityCommand(0, WorkPriorities.Pack(WorkKind.Build, WorkKind.Wood, WorkKind.Food)));
            sim.Schedule(0, new PriorityCommand(0, new[] { w[0] }, WorkPriorities.Pack(WorkKind.Food)));
            sim.Schedule(0, new BuildCommand(0, Array.Empty<int>(), "house", 24, 20));
            if (replay != null)
                foreach (var (t, c) in replay) sim.Schedule(t, c);
            sim.Run(90 * Tps);
            return (sim.StateHash(), recorded);
        }
        var first = Run(true, null);
        Assert.NotEmpty(first.Given);
        var again = Run(false, first.Given.Select(g => (g.Item1, g.Item2)).ToList());
        Assert.Equal(first.Hash, again.Hash);
    }

    private static void RunUntil(Simulation sim, Func<bool> done, int maxTicks, string what)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return;
            sim.Step();
        }
        Assert.Fail($"Not done after {maxTicks} ticks: {what}");
    }
}
