using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.Economy;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

public class BridgeTests
{
    private const int Tps = Simulation.TicksPerSecond;
    private const int Ground = 3; // units stand at 4
    private const int RiverX0 = 30, RiverX1 = 35; // a river three blocks deep across the map: too deep to wade
    private const int Z = 32; // the line the bridge is laid along

    /// <summary>Flat ground with a deep river along z, from x = 30 to 35: nobody walks from one side to the other.</summary>
    private static Simulation River()
    {
        var w = TestWorlds.Flat(64, 16, 64, Ground);
        TestWorlds.Box(w, RiverX0, 1, 0, RiverX1, Ground, 63, BlockType.Water);
        return TestWorlds.Sim(w);
    }

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

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

    private static Building Store(Simulation sim, int owner, int wood, int x = 10)
    {
        var store = sim.Economy.PlaceFinished(owner, sim.Content.Building("storehouse"), x, 10)!;
        store.Stock[(int)Good.Wood] = wood;
        return store;
    }

    private static int X(Simulation sim, int id) => sim.Units.X[Index(sim, id)].FloorToInt();

    private static List<Building> Bridge(Simulation sim) => sim.Buildings.All.Where(b => b.Def.IsBridge).ToList();

    /// <summary>Builds the bridge across the river at z = 32 with two workers; returns its columns.</summary>
    private static List<Building> BuildBridge(Simulation sim)
    {
        var workers = new[] { sim.SpawnUnit(0, "worker", 26, Z - 1), sim.SpawnUnit(0, "worker", 26, Z + 1) };
        sim.Schedule(sim.Tick, new BridgeCommand(0, workers, "bridge", 28, Z, 37, Z));
        sim.Step();
        var bridge = Bridge(sim);
        RunUntil(sim, () => bridge.All(b => b.Active), 120 * Tps, "the bridge to be built");
        return bridge;
    }

    [Fact]
    public void WorkersBridgeARiverAColumnAtATimeAndUnitsWalkAcross()
    {
        var sim = River();
        var store = Store(sim, 0, wood: 100);

        // Before: a spearman cannot get across.
        int spearman = sim.SpawnUnit(0, "spearman", 25, Z);
        sim.Schedule(0, new MoveCommand(0, new[] { spearman }, 40, Z));
        sim.Run(20 * Tps);
        Assert.True(X(sim, spearman) < RiverX0, "nobody wades a river this deep");

        var workers = new[] { sim.SpawnUnit(0, "worker", 26, Z - 1), sim.SpawnUnit(0, "worker", 26, Z + 1) };
        sim.Schedule(sim.Tick, new BridgeCommand(0, workers, "bridge", 28, Z, 37, Z));
        sim.Step();

        // A deck column over each column of water, at the banks' height, paid for at once.
        var bridge = Bridge(sim);
        Assert.Equal(Enumerable.Range(RiverX0, RiverX1 - RiverX0 + 1), bridge.Select(b => b.X).OrderBy(x => x));
        Assert.All(bridge, b => Assert.Equal((Z, Ground, BuildingState.Site), (b.Z, b.Y, b.State)));
        Assert.Equal(100 - 3 * bridge.Count, store.Stock[(int)Good.Wood]);
        // Only the column by the bank can be built at first; the workers start there.
        Assert.All(workers, id => Assert.Equal(sim.Buildings.AtColumn(RiverX0, Z)!.Id, sim.Units.JobTarget[Index(sim, id)]));

        // It goes out over the water from the bank, a column at a time.
        int lastFinished = 0;
        RunUntil(sim, () =>
        {
            var done = bridge.Where(b => b.Active).Select(b => b.X).OrderBy(x => x).ToList();
            Assert.Equal(Enumerable.Range(RiverX0, done.Count), done);
            Assert.True(done.Count >= lastFinished);
            lastFinished = done.Count;
            return done.Count == bridge.Count;
        }, 120 * Tps, "the bridge to be built");
        for (int x = RiverX0; x <= RiverX1; x++) Assert.Equal(BlockType.Planks, sim.World.Get(x, Ground, Z));

        // After: the spearman walks over it to the far bank.
        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { spearman }, 40, Z));
        RunUntil(sim, () => X(sim, spearman) > RiverX1, 30 * Tps, "the spearman to cross the bridge");
        // The workers stop once there is no more of it to build.
        sim.Run(Tps);
        Assert.All(workers, id => Assert.Equal(UnitJob.None, sim.Units.Job[Index(sim, id)]));
    }

    [Fact]
    public void TheEnemyCrossesYourBridgeTooUntilAPlankIsKnockedOut()
    {
        var sim = River();
        Store(sim, 0, wood: 100);
        BuildBridge(sim);

        // An enemy on the far side walks over it.
        int enemy = sim.SpawnUnit(1, "spearman", 42, Z);
        sim.Schedule(sim.Tick, new MoveCommand(1, new[] { enemy }, 24, Z));
        RunUntil(sim, () => X(sim, enemy) < RiverX0, 30 * Tps, "the enemy to cross your bridge");
        sim.Schedule(sim.Tick, new MoveCommand(1, new[] { enemy }, 24, 4)); // off down the near bank, out of the way
        sim.Run(10 * Tps);

        // A spearman of yours on the near bank knocks its end column out, and it falls into the river.
        int axe = sim.SpawnUnit(0, "spearman", 28, Z + 2);
        var end = sim.Buildings.AtColumn(RiverX0, Z)!;
        sim.Schedule(sim.Tick, new AttackBuildingCommand(0, new[] { axe }, end.Id));
        RunUntil(sim, () => sim.Buildings.Get(end.Id) == null, 60 * Tps, "the end of the bridge to be knocked out");
        sim.Run(5 * Tps);
        Assert.NotEqual(BlockType.Planks, sim.World.Get(RiverX0, Ground, Z));

        // The enemy, sent back, can no longer get across: it gets as far as the bank (where the spearman may
        // well cut it down).
        sim.Schedule(sim.Tick, new MoveCommand(1, new[] { enemy }, 42, Z));
        sim.Run(20 * Tps);
        Assert.True(Index(sim, enemy) < 0 || X(sim, enemy) < RiverX0, "the way across is cut");
    }

    [Fact]
    public void ABridgeRunsLevelAtTheHigherEndAndLeavesGroundAsItIs()
    {
        var sim = River();
        var plan = new List<BridgeSpan>();
        // A bank one higher at the far end: the deck runs at its height.
        TestWorlds.Box(sim.World, 36, Ground + 1, 30, 40, Ground + 1, 34, BlockType.Soil);
        Assert.Equal(Ground + 2, sim.Economy.PlanBridge(28, Z, 37, Z, plan));
        Assert.Equal(Enumerable.Range(RiverX0, RiverX1 - RiverX0 + 1), plan.Select(s => s.X));
        Assert.All(plan, s => Assert.Equal((Ground + 1, true), (s.DeckY, s.Room)));
        // Ground a step below the deck is walked across, not bridged: only the water gets a deck.
        Assert.Equal(Ground + 2, sim.Economy.PlanBridge(20, Z, 37, Z, plan));
        Assert.Equal(RiverX1 - RiverX0 + 1, plan.Count);

        // Ends more than a climb apart, or in the water, plan nothing.
        TestWorlds.Box(sim.World, 36, Ground + 2, 30, 40, Ground + 2, 34, BlockType.Soil);
        Assert.Equal(-1, sim.Economy.PlanBridge(28, Z, 37, Z, plan));
        Assert.Empty(plan);
        Assert.Equal(-1, sim.Economy.PlanBridge(28, Z, 32, Z, plan));

        // At most MaxBridge deck columns, from the start of the line.
        var wide = River();
        TestWorlds.Box(wide.World, 10, 1, 0, 50, Ground, 63, BlockType.Water);
        Assert.Equal(Ground + 1, wide.Economy.PlanBridge(5, Z, 55, Z, plan));
        Assert.Equal(EconomySystem.MaxBridge, plan.Count);
        Assert.Equal(10, plan[0].X);

        // Bridges are laid along a line, never placed like a house.
        Assert.Equal(PlaceResult.Blocked, sim.Economy.CanPlace(0, sim.Content.Building("bridge"), 31, Z, out _, checkPlayer: false));
    }

    [Fact]
    public void ABridgeStopsWhereTheWoodRunsOut()
    {
        var sim = River();
        var store = Store(sim, 0, wood: 10);
        int worker = sim.SpawnUnit(0, "worker", 26, Z);
        sim.Schedule(0, new BridgeCommand(0, new[] { worker }, "bridge", 28, Z, 37, Z));
        sim.Step();
        // Wood for three columns, from the start of the line.
        Assert.Equal(new[] { 30, 31, 32 }, Bridge(sim).Select(b => b.X).OrderBy(x => x));
        Assert.Equal(1, store.Stock[(int)Good.Wood]);
    }

    [Fact]
    public void BridgeCommandsReplay()
    {
        var log = new CommandLog { Seed = 1, SizeX = 64, SizeY = 16, SizeZ = 64 };
        var cmd = new BridgeCommand(0, new[] { 3, 4 }, "bridge", 28, Z, 37, Z);
        log.Add(5, cmd);
        var copy = CommandLog.Parse(log.ToString());
        Assert.Equal(cmd, copy.Commands.Single().Command);
    }
}
