using System;
using System.Linq;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Craters: a catapult's stones blow a bowl in the open ground where they land and throw the earth onto the rim,
/// but leave buildings' blocks, ground under water and underground alone.
/// </summary>
public class CraterTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Tps = Simulation.TicksPerSecond;

    private static void Run(Simulation sim, int seconds)
    {
        for (int t = 0; t < seconds * Tps; t++) sim.Step();
    }

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

    /// <summary>A catapult of player 0 at (20, 20) holding its ground, and a spearman of player 1 holding at (30, 20).</summary>
    private static (int Cat, int Target) Duel(Simulation sim)
    {
        int c = sim.SpawnUnit(0, "catapult", 20, 20);
        sim.Units.Stance[Index(sim, c)] = UnitStance.Hold;
        int t = sim.SpawnUnit(1, "spearman", 30, 20);
        sim.Units.Stance[Index(sim, t)] = UnitStance.Hold;
        return (c, t);
    }

    [Fact]
    public void OnlyTheCatapultAndTheBombardShipLeaveCraters()
    {
        var content = Rts.Sim.Content.ContentDb.Default;
        Assert.True(content.Unit("catapult").Crater > 0);
        // The ram strikes close up and leaves none; nobody else does either.
        Assert.True(content.Unit("bombard_ship").Crater > 0);
        Assert.All(content.Units.Where(u => u.Id is not ("catapult" or "bombard_ship")), u => Assert.Equal(0, u.Crater));
    }

    [Fact]
    public void ACatapultStoneBlowsACraterWhereItLandsAndHeapsTheEarthOnTheRim()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(64, 16, 64, Ground));
        var (_, target) = Duel(sim);
        int x = 30, z = 20;
        Run(sim, 30);
        Assert.False(sim.Units.Exists(target));
        Assert.True(sim.Combat.Craters > 0);
        var w = sim.World;
        // The middle is blown out two deep, and the ground round it at least one.
        Assert.True(w.SurfaceHeight(x, z) <= Ground - 2, $"the middle of the crater is at {w.SurfaceHeight(x, z)}");
        Assert.True(w.SurfaceHeight(x + 1, z) < Ground);
        // The earth comes down as rubble on the rim, above the ground round it.
        int rim = 0;
        for (int dz = -4; dz <= 4; dz++)
        for (int dx = -4; dx <= 4; dx++)
            if (w.Get(x + dx, Ground + 1, z + dz) == BlockType.Rubble) rim++;
        Assert.True(rim > 0, "the earth thrown out lies on the rim");
        // Far from where it landed, the ground is as it was.
        Assert.Equal(Ground, w.SurfaceHeight(40, 40));
    }

    [Fact]
    public void ACraterSparesBuildingsWaterAndTheGroundUnderARoof()
    {
        // Ground covered by a stone slab (a roof) over the target: the stone may break walls, but no crater under it.
        var w = TestWorlds.Flat(64, 16, 64, Ground);
        TestWorlds.Box(w, 26, Ground + 4, 16, 34, Ground + 4, 24, BlockType.Stone);
        var sim = TestWorlds.Sim(w);
        sim.Combat.Crater(Fix64.FromInt(30) + Fix64.Half, Ground + 1, Fix64.FromInt(20) + Fix64.Half, 2);
        Assert.Equal(0, sim.Combat.Craters);
        Assert.Equal(BlockType.Grass, sim.World.Get(30, Ground, 20));

        // Ground under water is not dug.
        w = TestWorlds.Flat(64, 16, 64, Ground);
        TestWorlds.Box(w, 26, Ground + 1, 16, 34, Ground + 1, 24, BlockType.Water);
        sim = TestWorlds.Sim(w);
        sim.Combat.Crater(Fix64.FromInt(30) + Fix64.Half, Ground + 1, Fix64.FromInt(20) + Fix64.Half, 2);
        Assert.Equal(0, sim.Combat.Craters);
        Assert.Equal(BlockType.Grass, sim.World.Get(30, Ground, 20));

        // A building's blocks are left whole; the open ground beside it is dug.
        sim = TestWorlds.Sim(TestWorlds.Flat(64, 16, 64, Ground));
        var wall = sim.Economy.PlaceFinished(1, sim.Content.Building("stone_wall"), 30, 20) ?? throw new InvalidOperationException();
        var before = Enumerable.Range(0, wall.Def.Blocks.Count)
            .Select(k => sim.World.Get(wall.X + wall.Def.Blocks[k].X, wall.Y + wall.Def.Blocks[k].Y, wall.Z + wall.Def.Blocks[k].Z)).ToArray();
        sim.Combat.Crater(Fix64.FromInt(30) + Fix64.Half, Ground + 1, Fix64.FromInt(20) + Fix64.Half, 2);
        var after = Enumerable.Range(0, wall.Def.Blocks.Count)
            .Select(k => sim.World.Get(wall.X + wall.Def.Blocks[k].X, wall.Y + wall.Def.Blocks[k].Y, wall.Z + wall.Def.Blocks[k].Z)).ToArray();
        Assert.Equal(before, after);
        Assert.Equal(1, sim.Combat.Craters);
        Assert.True(sim.World.SurfaceHeight(32, 20) < Ground);
    }

    [Fact]
    public void AUnitStandingWhereACraterIsBlownDropsIntoIt()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(64, 16, 64, Ground));
        int s = sim.SpawnUnit(1, "spearman", 31, 20);
        sim.Combat.Crater(Fix64.FromInt(31) + Fix64.Half, Ground + 1, Fix64.FromInt(20) + Fix64.Half, 2);
        Run(sim, 1);
        Assert.True(sim.Units.Y[Index(sim, s)] <= Ground - 1, "the spearman is down in the crater");
    }

    [Fact]
    public void CratersAreTheSameEveryRun()
    {
        ulong Play()
        {
            var sim = TestWorlds.Sim(TestWorlds.Flat(64, 16, 64, Ground));
            Duel(sim);
            sim.SpawnUnit(1, "spearman", 31, 23);
            Run(sim, 30);
            return sim.StateHash();
        }
        Assert.Equal(Play(), Play());
    }
}
