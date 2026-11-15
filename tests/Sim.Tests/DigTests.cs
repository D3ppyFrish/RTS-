using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

public class DigTests
{
    private const int Tps = Simulation.TicksPerSecond;
    private const int Ground = 8; // grass at 8 over stone; units stand at 9

    private static Simulation Deep() => TestWorlds.Sim(TestWorlds.Flat(64, 24, 64, Ground));

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

    private static bool AllIdle(Simulation sim, IEnumerable<int> ids)
    {
        foreach (int id in ids)
            if (sim.Units.Job[Index(sim, id)] != UnitJob.None) return false;
        return true;
    }

    /// <summary>The highest level that still holds a block the area wants, or -1.</summary>
    private static int TopLayer(Simulation sim, DigArea a)
    {
        for (int y = a.TopY; y >= a.FloorY; y--)
        for (int z = a.Z1; z <= a.Z2; z++)
        for (int x = a.X1; x <= a.X2; x++)
            if (a.Wants(sim.World, x, y, z)) return y;
        return -1;
    }

    [Fact]
    public void ATreeInADigAreaIsFelledAndTheRubbleInTheAreaDugForWood()
    {
        var sim = Deep();
        sim.Economy.PlaceFinished(0, sim.Content.Building("storehouse"), 10, 20);
        TestWorlds.Tree(sim.World, 25, 22, 4, Ground);
        int w = sim.SpawnUnit(0, "worker", 18, 22);
        sim.Schedule(0, new DigAreaCommand(0, new[] { w }, 22, 18, 31, 26, Ground));
        RunUntil(sim, () => sim.Tick > 0 && AllIdle(sim, new[] { w }), 120 * Tps, "the area to be dug");

        int wood = 0, rubble = 0, rubbleInArea = 0;
        for (int y = 0; y < 24; y++)
        for (int z = 0; z < 64; z++)
        for (int x = 0; x < 64; x++)
        {
            var b = sim.World.Get(x, y, z);
            if (b == BlockType.Wood) wood++;
            if (b != BlockType.TreeRubble) continue;
            rubble++;
            if (x is >= 22 and <= 31 && z is >= 18 and <= 26) rubbleInArea++;
        }
        Assert.Equal(0, wood);
        Assert.Equal(0, rubbleInArea);
        Assert.True(rubble < 4, "some of the tree landed in the area");
        Assert.Equal((4 - rubble) * Harvest.WoodPerTreeRubble, sim.Economy.Player(0).Gathered[(int)Good.Wood]);
    }

    [Fact]
    public void WorkersDigAnAreaOutLayerByLayerAndCarryTheStoneHome()
    {
        var sim = Deep();
        var store = sim.Economy.PlaceFinished(0, sim.Content.Building("storehouse"), 10, 20)!;
        var workers = new[]
        {
            sim.SpawnUnit(0, "worker", 18, 20), sim.SpawnUnit(0, "worker", 18, 22), sim.SpawnUnit(0, "worker", 18, 24),
        };
        const int x1 = 22, z1 = 20, x2 = 27, z2 = 23, floor = Ground + 1 - 3; // three levels: grass, stone, stone
        sim.Schedule(0, new DigAreaCommand(0, workers, x1, z1, x2, z2, floor));
        sim.Step();
        var area = Assert.Single(sim.Economy.DigAreas);
        Assert.Equal((Ground + 1, floor), (area.TopY, area.FloorY));
        Assert.True(area.StairAlongX);
        foreach (int id in workers) Assert.Equal(UnitJob.Dig, sim.Units.Job[Index(sim, id)]);

        // Each block dug is in the highest layer that still had blocks to dig.
        var before = new BlockType[x2 - x1 + 1, Ground + 1, z2 - z1 + 1];
        void Snapshot()
        {
            for (int x = x1; x <= x2; x++)
            for (int y = 0; y <= Ground; y++)
            for (int z = z1; z <= z2; z++)
                before[x - x1, y, z - z1] = sim.World.Get(x, y, z);
        }
        int layers = 0, lastTop = int.MaxValue;
        Snapshot();
        RunUntil(sim, () =>
        {
            int top = sim.Economy.DigAreas.Count > 0 ? TopLayer(sim, area) : -1;
            for (int x = x1; x <= x2; x++)
            for (int y = 0; y <= Ground; y++)
            for (int z = z1; z <= z2; z++)
                if (before[x - x1, y, z - z1] != BlockType.Air && sim.World.Get(x, y, z) == BlockType.Air)
                    Assert.True(y >= top, $"block at level {y} dug while level {top} still had blocks");
            if (top >= 0 && top < lastTop)
            {
                layers++;
                lastTop = top;
            }
            Snapshot();
            return AllIdle(sim, workers);
        }, 600 * Tps, "the area dug out");
        Assert.Equal(3, layers);
        Assert.Empty(sim.Economy.DigAreas);

        // Dug out to the floor, but for the stair along row z1: one level lower per column from the x1 end.
        int stone = 0;
        for (int x = x1; x <= x2; x++)
        for (int z = z1; z <= z2; z++)
        {
            int lowest = z == z1 ? Math.Max(floor, Ground + 1 - (x - x1)) : floor;
            for (int y = 1; y <= Ground; y++)
            {
                var expect = y < lowest ? (y == Ground ? BlockType.Grass : BlockType.Stone) : BlockType.Air;
                Assert.Equal(expect, sim.World.Get(x, y, z));
                if (y >= lowest && y < Ground) stone++;
            }
        }
        // Three rows of 6 × 2 levels, and on the stair 0, 0, 1, 2, 2, 2.
        Assert.Equal(36 + 7, stone);
        Assert.Equal(stone, store.Stock[(int)Good.Stone]);
        Assert.Equal(stone, sim.Economy.Player(0).Gathered[(int)Good.Stone]);

        // Nobody is trapped: a worker sent to the pit floor walks back out by the stair.
        int w = workers[0];
        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { w }, x2, z2));
        sim.Step();
        RunUntil(sim, () => sim.Units.Y[Index(sim, w)] == floor && sim.Units.Order[Index(sim, w)] == UnitOrder.Idle, 60 * Tps, "into the pit");
        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { w }, 18, 30));
        sim.Step();
        RunUntil(sim, () => sim.Units.Order[Index(sim, w)] == UnitOrder.Idle, 60 * Tps, "back out of the pit");
        int wi = Index(sim, w);
        Assert.Equal((18, Ground + 1, 30), (sim.Units.X[wi].FloorToInt(), sim.Units.Y[wi], sim.Units.Z[wi].FloorToInt()));
    }

    [Fact]
    public void SappersDigATrenchTwiceAsFastAndLeaveTheSpoil()
    {
        int Dig(string unit, out Simulation sim)
        {
            sim = Deep();
            // Soil gives nothing, so a worker has no trips to make either.
            TestWorlds.Box(sim.World, 20, Ground - 2, 30, 31, Ground, 30, BlockType.Soil);
            int id = sim.SpawnUnit(0, unit, 18, 30);
            sim.Schedule(0, new DigAreaCommand(0, new[] { id }, 20, 30, 31, 30, Ground - 2));
            var s = sim;
            return RunUntil(sim, () => s.Tick > 0 && AllIdle(s, new[] { id }), 600 * Tps, $"the {unit}'s trench");
        }
        int sapperTicks = Dig("sapper", out var sim);
        int workerTicks = Dig("worker", out _);
        // A one-wide trench is its own stair: one level lower per column, then the full depth.
        for (int x = 20; x <= 31; x++)
        {
            int lowest = Math.Max(Ground - 2, Ground + 1 - (x - 20));
            for (int y = Ground - 2; y <= Ground; y++)
                Assert.Equal(y < lowest ? BlockType.Soil : BlockType.Air, sim.World.Get(x, y, 30));
            Assert.Equal(BlockType.Stone, sim.World.Get(x, Ground - 3, 30));
        }
        Assert.True(sapperTicks * 10 < workerTicks * 7, $"sapper {sapperTicks} ticks, worker {workerTicks}");

        // Stone dug by a sapper is left: it carries nothing and nobody's stock grows.
        sim = Deep();
        int sapper = sim.SpawnUnit(0, "sapper", 18, 30);
        sim.Schedule(0, new DigAreaCommand(0, new[] { sapper }, 20, 30, 25, 31, Ground - 1));
        RunUntil(sim, () => sim.Tick > 0 && AllIdle(sim, new[] { sapper }), 600 * Tps, "the sapper's pit");
        Assert.Equal(BlockType.Air, sim.World.Get(25, Ground - 1, 31));
        Assert.Equal(0, sim.Units.CarryAmount[Index(sim, sapper)]);
        Assert.Equal(0, sim.Economy.Player(0).Gathered[(int)Good.Stone]);
    }

    [Fact]
    public void OnlyWorkersAndSappersDigAndAnAreaNobodyDigsIsDropped()
    {
        var sim = Deep();
        int worker = sim.SpawnUnit(0, "worker", 18, 20);
        int spear = sim.SpawnUnit(0, "spearman", 18, 22);
        int enemy = sim.SpawnUnit(1, "worker", 18, 24);
        sim.Schedule(0, new DigAreaCommand(0, new[] { worker, spear, enemy }, 22, 20, 25, 23, Ground));
        sim.Step();
        Assert.Equal(UnitJob.Dig, sim.Units.Job[Index(sim, worker)]);
        Assert.Equal(UnitJob.None, sim.Units.Job[Index(sim, spear)]);
        Assert.Equal(UnitJob.None, sim.Units.Job[Index(sim, enemy)]);
        Assert.Single(sim.Economy.DigAreas);

        sim.Schedule(sim.Tick, new StopCommand(0, new[] { worker }));
        sim.Run(2 * Tps);
        Assert.Empty(sim.Economy.DigAreas);

        // Out of what the command allows: too deep, above the ground, off the map.
        sim.Schedule(sim.Tick, new DigAreaCommand(0, new[] { worker }, 22, 20, 25, 23, Ground + 1 - DigArea.MaxDepth - 1));
        sim.Schedule(sim.Tick, new DigAreaCommand(0, new[] { worker }, 22, 20, 25, 23, Ground + 2));
        sim.Schedule(sim.Tick, new DigAreaCommand(0, new[] { worker }, 100, 20, 120, 23, Ground));
        sim.Step();
        Assert.Empty(sim.Economy.DigAreas);
        Assert.Equal(UnitJob.None, sim.Units.Job[Index(sim, worker)]);
    }

    [Fact]
    public void DigAreasAreLoggedAndReplayTheSame()
    {
        var log = new CommandLog { Seed = 1, SizeX = 64, SizeY = 24, SizeZ = 64 };
        var cmd = new DigAreaCommand(0, new[] { 3, 4 }, 22, 20, 27, 23, 6);
        log.Add(5, cmd);
        log.Ticks = 10;
        var copy = CommandLog.Parse(log.ToString());
        Assert.Equal(cmd, copy.Commands[0].Command);

        ulong Play()
        {
            var sim = Deep();
            sim.Economy.PlaceFinished(0, sim.Content.Building("storehouse"), 10, 20);
            var ids = new[] { sim.SpawnUnit(0, "worker", 18, 20), sim.SpawnUnit(0, "sapper", 18, 22) };
            sim.Schedule(0, new DigAreaCommand(0, ids, 22, 20, 27, 23, 6));
            sim.Run(20 * Tps);
            Assert.Single(sim.Economy.DigAreas); // still digging
            return sim.StateHash();
        }
        Assert.Equal(Play(), Play());
    }
}
