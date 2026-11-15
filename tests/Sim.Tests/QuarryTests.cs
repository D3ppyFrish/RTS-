using System;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Gatherers working stone must not dig pits that they, or anyone, cannot climb out of (#33).</summary>
public class QuarryTests
{
    private const int Tps = Simulation.TicksPerSecond;

    [Fact]
    public void DiggingTheLastStepOutOfAHoleLeavesNoWayOut()
    {
        // Ground at 10 (units stand at 11). A hole two deep at (30, 30): its floor is 8, a unit in it stands at 9.
        // A step at (31, 9, 30) is the way out: from 9 up to 10, then up to 11.
        const int ground = 10;
        var w = TestWorlds.Flat(64, 24, 64, ground);
        w.Set(30, 10, 30, BlockType.Air);
        w.Set(30, 9, 30, BlockType.Air);
        w.Set(31, 10, 30, BlockType.Air);
        // With the step: a unit at the bottom gets out, so digging some other stone there is fine.
        Assert.True(Harvest.LeavesWayOut(w, 29, 9, 30, 30, 9, 30));
        // Digging the step itself would leave the hole with straight sides two high.
        Assert.False(Harvest.LeavesWayOut(w, 31, 9, 30, 30, 9, 30));
        Assert.Equal(BlockType.Stone, w.Get(31, 9, 30)); // the check does not change the world
    }

    [Fact]
    public void QuarriedStoneIsNotDugFromOnTop()
    {
        const int ground = 10;
        var w = TestWorlds.Flat(64, 24, 64, ground);
        // One exposed stone block under an open shaft, only reachable by standing on it.
        w.Set(30, 10, 30, BlockType.Air);
        Assert.True(Harvest.WorkSpot(w, 30, 9, 9, 30, 30, 11, 30, out _, out _, out _));
        Assert.False(Harvest.WorkSpot(w, 30, 9, 9, 30, 30, 11, 30, out _, out _, out _, allowOnTop: false));
        Assert.False(Harvest.FindNext(w, BlockType.Stone, 30, 9, 30, new(), out _, out _, out _));
    }

    [Fact]
    public void AWorkerQuarryingAStoneVeinLeavesStepsOut()
    {
        // A 5 by 5 vein of stone, eight deep, in soil, open at the top. The worker is told to dig it and works
        // it for ten minutes. It goes down to the bottom of the vein, but leaves a stair of stone to climb out.
        const int ground = 10;
        var sim = TestWorlds.Sim(TestWorlds.Flat(64, 24, 64, ground));
        TestWorlds.Box(sim.World, 0, 1, 0, 63, ground - 1, 63, BlockType.Soil);
        TestWorlds.Box(sim.World, 30, 2, 30, 34, ground - 1, 34, BlockType.Stone);
        TestWorlds.Box(sim.World, 30, ground, 30, 34, ground, 34, BlockType.Air);
        sim.Economy.PlaceFinished(0, sim.Content.Building("storehouse"), 20, 32);
        int w = sim.SpawnUnit(0, "worker", 25, 32);
        sim.Schedule(0, new GatherCommand(0, new[] { w }, 32, ground - 1, 32));
        for (int t = 0; t < 600 * Tps; t++) sim.Step();

        int left = 0;
        for (int x = 30; x <= 34; x++)
        for (int z = 30; z <= 34; z++)
        for (int y = 2; y < ground; y++)
            if (sim.World.Get(x, y, z) == BlockType.Stone) left++;
        Assert.True(left < 100, $"the worker quarried most of the vein ({left} of 200 blocks left)");
        // Down to the bottom: some column of the vein is open from the top to its floor.
        int fx = -1, fz = -1;
        for (int x = 30; x <= 34 && fx < 0; x++)
        for (int z = 30; z <= 34 && fx < 0; z++)
            if (Walk.GroundLevel(sim.World, x, z) == 2) (fx, fz) = (x, z);
        Assert.True(fx >= 0, "the quarry reaches the bottom of the vein");

        // The worker, and a worker put on the quarry floor, can both walk away.
        int floor = sim.SpawnUnit(0, "worker", fx, fz);
        Assert.Equal(2, sim.Units.Y[sim.Units.IndexOf(floor)]);
        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { w, floor }, 55, 55));
        for (int t = 0; t < 60 * Tps; t++) sim.Step();
        foreach (int id in new[] { w, floor })
        {
            int i = sim.Units.IndexOf(id);
            Assert.True(Math.Abs(sim.Units.X[i].FloorToInt() - 55) <= 2 && Math.Abs(sim.Units.Z[i].FloorToInt() - 55) <= 2,
                $"unit {id} walked out (at {sim.Units.X[i].FloorToInt()},{sim.Units.Y[i]},{sim.Units.Z[i].FloorToInt()})");
        }
    }
}
