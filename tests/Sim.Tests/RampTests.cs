using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.Economy;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

public class RampTests
{
    private const int Tps = Simulation.TicksPerSecond;
    private const int Ground = 3; // units stand at 4
    private const int CliffX = 40; // a cliff from here east, 5 levels up: units stand at 9 on top
    private const int CliffTop = Ground + 1 + 5;
    private const int Z = 32;

    /// <summary>Flat ground with a cliff 5 levels high across the map from x = 40 east: nobody climbs it.</summary>
    private static Simulation Cliff()
    {
        var w = TestWorlds.Flat(64, 16, 64, Ground);
        TestWorlds.Box(w, CliffX, Ground + 1, 0, 63, CliffTop - 1, 63, BlockType.Stone);
        return TestWorlds.Sim(w);
    }

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);
    private static int X(Simulation sim, int id) => sim.Units.X[Index(sim, id)].FloorToInt();
    private static int Y(Simulation sim, int id) => sim.Units.Y[Index(sim, id)];

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

    private static Building Store(Simulation sim, int owner, int stone, int x = 10, int z = 10)
    {
        var store = sim.Economy.PlaceFinished(owner, sim.Content.Building("storehouse"), x, z)!;
        store.Stock[(int)Good.Stone] = stone;
        return store;
    }

    /// <summary>The level units stand at in column (x, z).</summary>
    private static int Stand(Simulation sim, int x, int z) => Walk.GroundLevel(sim.World, x, z);

    [Fact]
    public void WorkersHeapARampUpACliffAndUnitsWalkUpIt()
    {
        var sim = Cliff();
        var store = Store(sim, 0, stone: 100);

        // Before: a spearman cannot get up the cliff.
        int spearman = sim.SpawnUnit(0, "spearman", 25, Z);
        sim.Schedule(0, new MoveCommand(0, new[] { spearman }, 50, Z));
        sim.Run(20 * Tps);
        Assert.True(X(sim, spearman) < CliffX, "nobody climbs a cliff 5 levels high");

        // The plan: from the foot at x = 32 a level a column, up to a level under the top, then level to the cliff.
        var plan = new List<RampSpan>();
        Assert.Equal(CliffTop, sim.Economy.PlanRamp(32, Z, CliffX, Z, plan));
        Assert.Equal(Enumerable.Range(33, CliffX - 33), plan.Select(s => s.X));
        Assert.Equal(new[] { 5, 6, 7, 8, 8, 8, 8 }, plan.Select(s => s.Top));
        Assert.All(plan, s => Assert.True(s.Room));
        int blocks = plan.Sum(s => s.Blocks);
        Assert.Equal(1 + 2 + 3 + 4 * 4, blocks);

        var workers = new[] { sim.SpawnUnit(0, "worker", 30, Z - 1), sim.SpawnUnit(0, "worker", 30, Z + 1) };
        sim.Schedule(sim.Tick, new RampCommand(0, workers, 32, Z, CliffX, Z));
        sim.Step();
        Assert.Single(sim.Economy.Ramps);
        Assert.All(workers, id => Assert.Equal(UnitJob.Ramp, sim.Units.Job[Index(sim, id)]));

        // It is heaped from the foot up: a column is only started once the one before it is done, and the stone
        // goes as the blocks go in.
        RunUntil(sim, () =>
        {
            bool started = false;
            for (int x = CliffX - 1; x >= 33; x--)
            {
                bool any = Stand(sim, x, Z) > Ground + 1;
                if (started) Assert.Equal(plan[x - 33].Top, Stand(sim, x, Z)); // every column nearer the foot is done
                started |= any;
            }
            int placed = Enumerable.Range(33, CliffX - 33).Sum(x => Stand(sim, x, Z) - (Ground + 1));
            Assert.Equal(100 - placed, store.Stock[(int)Good.Stone]);
            return sim.Economy.Ramps.Count == 0;
        }, 120 * Tps, "the ramp to be built");
        for (int x = 33; x < CliffX; x++) Assert.Equal(plan[x - 33].Top, Stand(sim, x, Z));
        Assert.Equal(BlockType.Soil, sim.World.Get(35, Ground + 1, Z));
        Assert.Equal(100 - blocks, store.Stock[(int)Good.Stone]);
        Assert.Equal(blocks, sim.Economy.Player(0).Spent[(int)Good.Stone]);
        sim.Run(Tps);
        Assert.All(workers, id => Assert.Equal(UnitJob.None, sim.Units.Job[Index(sim, id)]));

        // After: the spearman walks up it and onto the cliff top.
        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { spearman }, 50, Z));
        RunUntil(sim, () => X(sim, spearman) >= 50, 30 * Tps, "the spearman to walk up the ramp");
        Assert.Equal(CliffTop, Y(sim, spearman));
    }

    [Fact]
    public void ARampUpToAWallLetsTheEnemyOverIt()
    {
        var w = TestWorlds.Flat(64, 16, 64, Ground);
        var sim = TestWorlds.Sim(w);
        // Player 0's wooden wall (4 high) right across the map at x = 40.
        for (int z = 1; z < 63; z++) Assert.NotNull(sim.Economy.PlaceFinished(0, sim.Content.Building("wooden_wall"), 40, z));
        foreach (int z in new[] { 0, 63 }) TestWorlds.Box(w, 40, Ground + 1, z, 40, 12, z, BlockType.Bedrock); // no way round at the edges
        Store(sim, 1, stone: 100);
        int spearman = sim.SpawnUnit(1, "spearman", 25, Z);
        sim.Schedule(0, new MoveCommand(1, new[] { spearman }, 50, Z));
        sim.Run(20 * Tps);
        Assert.True(X(sim, spearman) < 40, "the wall keeps the enemy out");

        var workers = new[] { sim.SpawnUnit(1, "worker", 33, Z - 1), sim.SpawnUnit(1, "sapper", 33, Z + 1) };
        sim.Schedule(sim.Tick, new RampCommand(1, workers, 34, Z, 40, Z));
        sim.Step();
        RunUntil(sim, () => sim.Economy.Ramps.Count == 0, 120 * Tps, "the ramp to be built");
        Assert.Equal(Ground + 1 + 3, Stand(sim, 39, Z)); // a climb under the wall's top

        // Up the ramp onto the wall, then down the far side.
        sim.Schedule(sim.Tick, new MoveCommand(1, new[] { spearman }, 40, Z));
        RunUntil(sim, () => X(sim, spearman) == 40 && Y(sim, spearman) == Ground + 1 + 4, 30 * Tps, "the spearman to walk up onto the wall");
        sim.Schedule(sim.Tick, new MoveCommand(1, new[] { spearman }, 50, Z));
        for (int t = 0; t < 60 * Tps && !(X(sim, spearman) > 40 && Y(sim, spearman) == Ground + 1); t++) sim.Step();
        Assert.True(X(sim, spearman) > 40 && Y(sim, spearman) == Ground + 1,
            $"the spearman to get over the wall: at {X(sim, spearman)},{Y(sim, spearman)},{sim.Units.Z[Index(sim, spearman)].FloorToInt()} order {sim.Units.Order[Index(sim, spearman)]}");
    }

    [Fact]
    public void TheBuildersStopWhenTheStoneRunsOut()
    {
        var sim = Cliff();
        var store = Store(sim, 0, stone: 5);
        int worker = sim.SpawnUnit(0, "worker", 30, Z);
        sim.Schedule(0, new RampCommand(0, new[] { worker }, 32, Z, CliffX, Z));
        sim.Step();
        RunUntil(sim, () => sim.Units.Job[Index(sim, worker)] == UnitJob.None, 60 * Tps, "the worker to stop");
        Assert.Equal(0, store.Stock[(int)Good.Stone]);
        int placed = Enumerable.Range(33, CliffX - 33).Sum(x => Stand(sim, x, Z) - (Ground + 1));
        Assert.Equal((5, 5), (placed, sim.Economy.Player(0).Spent[(int)Good.Stone]));
        // Nobody builds it any more, so it is dropped.
        sim.Run(2 * Tps);
        Assert.Empty(sim.Economy.Ramps);
    }

    [Fact]
    public void ARampNeedsSomethingToLeanOnRoomAndLength()
    {
        var sim = Cliff();
        var plan = new List<RampSpan>();
        // Flat ground: nothing to climb.
        Assert.Equal(-1, sim.Economy.PlanRamp(10, Z, 20, Z, plan));
        // Too short for the height: 5 levels in 3 columns.
        Assert.Equal(-1, sim.Economy.PlanRamp(CliffX - 3, Z, CliffX, Z, plan));
        Assert.True(plan[^1].Top < CliffTop - 1, "it stops short of the top");
        // A building in the way.
        Assert.NotNull(sim.Economy.PlaceFinished(0, sim.Content.Building("house"), 34, Z - 1));
        int r = sim.Economy.PlanRamp(30, Z, CliffX, Z, plan);
        Assert.True(r == -1, string.Join(" ", plan));
        // A line that wanders is laid straight along its longer side.
        Assert.Equal(CliffTop, sim.Economy.PlanRamp(30, 20, CliffX, 22, plan));
        Assert.All(plan, s => Assert.Equal(20, s.Z));
        // A command for a plan that does not work does nothing.
        int worker = sim.SpawnUnit(0, "worker", 28, Z);
        sim.Schedule(0, new RampCommand(0, new[] { worker }, 10, Z, 20, Z));
        sim.Step();
        Assert.Empty(sim.Economy.Ramps);
        // Soldiers do not build ramps.
        int spearman = sim.SpawnUnit(0, "spearman", 28, 20);
        sim.Schedule(sim.Tick, new RampCommand(0, new[] { spearman }, 30, 20, CliffX, 20));
        sim.Step();
        Assert.Empty(sim.Economy.Ramps);
    }

    [Fact]
    public void ARampIsJustGroundTheEnemyCanDigAway()
    {
        var sim = Cliff();
        Store(sim, 0, stone: 100);
        int worker = sim.SpawnUnit(0, "worker", 30, Z);
        sim.Schedule(0, new RampCommand(0, new[] { worker }, 32, Z, CliffX, Z));
        sim.Step();
        RunUntil(sim, () => sim.Economy.Ramps.Count == 0, 120 * Tps, "the ramp to be built");
        // The defender's workers dig the top of it away, and nobody gets up any more.
        int digger = sim.SpawnUnit(1, "worker", 45, Z);
        sim.Schedule(sim.Tick, new DigAreaCommand(1, new[] { digger }, 36, Z - 1, CliffX - 1, Z, Ground + 1));
        RunUntil(sim, () => Enumerable.Range(36, CliffX - 36).All(x => Stand(sim, x, Z) == Ground + 1), 120 * Tps, "the ramp to be dug away");
    }

    [Fact]
    public void ARampCommandRoundTripsAndTheSameInputsGiveTheSameState()
    {
        var c = new RampCommand(1, new[] { 4, 9 }, 32, Z, CliffX, Z);
        var log = CommandLog.Parse("rts-log 1\nseed 1\nworld 64 16 64\nticks 10\n7 ramp 1 32 32 40 32 4,9\n");
        var back = log.Commands.Single();
        Assert.Contains("7 ramp 1 32 32 40 32 4,9", log.ToString());
        Assert.Equal(7, back.Tick);
        Assert.Equal(c, back.Command);

        ulong Run()
        {
            var sim = Cliff();
            Store(sim, 0, stone: 100);
            var workers = new[] { sim.SpawnUnit(0, "worker", 30, Z - 1), sim.SpawnUnit(0, "worker", 30, Z + 1) };
            sim.Schedule(5, new RampCommand(0, workers, 32, Z, CliffX, Z));
            sim.Run(40 * Tps);
            return sim.StateHash();
        }
        Assert.Equal(Run(), Run());
    }
}
