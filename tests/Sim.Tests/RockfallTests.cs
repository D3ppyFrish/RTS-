using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Issue #78: rockfall on the steep slopes of the hills. Loose rock perched on a cliff's brink comes down on a unit
/// that steps onto the scree at its foot.
/// </summary>
public class RockfallTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Cliff = 8;  // the cliff's top block, west of the brink
    private const int Brink = 30; // the cliff's last column; the scree is east of it
    private const int Tps = Simulation.TicksPerSecond;
    private const int Settlers = 0;

    private static int Hp(Simulation sim, int id) => sim.Units.Exists(id) ? sim.Units.Hp[sim.Units.IndexOf(id)] : 0;

    /// <summary>A flat world with a stone cliff five blocks high west of x = 31, and a rockfall on its brink at z = 30.</summary>
    private static Simulation WithCliff(out int scree)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(64, 24, 64, Ground));
        TestWorlds.Box(sim.World, 0, Ground + 1, 0, Brink, Cliff, 63, BlockType.Stone);
        scree = HazardSystem.MakeRockfall(sim.World, Brink, Cliff, 30, 1, 0, RegionGenerator.ScreeReach);
        return sim;
    }

    private static int Count(Simulation sim, BlockType b)
    {
        int n = 0;
        for (int y = 0; y < sim.World.SizeY; y++)
        for (int z = 0; z < sim.World.SizeZ; z++)
        for (int x = 0; x < sim.World.SizeX; x++)
            if (sim.World.Get(x, y, z) == b) n++;
        return n;
    }

    /// <summary>A rockfall is loose rock along the brink, with one more on top, and scree at the cliff's foot, two columns either side.</summary>
    [Fact]
    public void ARockfallIsLooseRockOverScree()
    {
        var sim = WithCliff(out int scree);
        Assert.Equal(4, Count(sim, BlockType.LooseRock));
        Assert.Equal(BlockType.LooseRock, sim.World.Get(Brink, Cliff + 2, 30));
        Assert.Equal(RegionGenerator.ScreeReach * 5, scree);
        Assert.Equal(scree, Count(sim, BlockType.Scree));
        Assert.Equal(BlockType.Scree, sim.World.Get(Brink + 1, Ground, 28));
        Assert.True(HazardSystem.IsHazard(sim.World, Brink + 2, Ground + 1, 30));
        Assert.False(HazardSystem.IsHazard(sim.World, Brink + 6, Ground + 1, 30));
    }

    /// <summary>
    /// A unit that steps onto the scree brings the rocks down on itself: they leave the brink, hit it, and land as
    /// stone rubble at the cliff's foot. The scree is plain stone after, and safe.
    /// </summary>
    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 30)]
    [InlineData(3, 29)]
    [InlineData(4, 32)]
    public void SteppingOntoTheScreeBringsTheRocksDown(int outward, int z)
    {
        var sim = WithCliff(out _);
        int id = sim.SpawnUnit(Settlers, "spearman", Brink + outward, z);
        int full = Hp(sim, id);
        sim.Run(5 * Tps);
        Assert.Equal(0, Count(sim, BlockType.LooseRock));
        Assert.Equal(0, Count(sim, BlockType.Scree));
        Assert.True(Hp(sim, id) <= full - PhysicsSystem.CrushDamage, $"hp {Hp(sim, id)} of {full}");
        Assert.True(Count(sim, BlockType.StoneRubble) >= 3, "the rocks lie as rubble");
        Assert.False(HazardSystem.IsHazard(sim.World, Brink + outward, Ground + 1, z));
        var rubble = new List<int>();
        for (int x = 0; x < 64; x++)
        for (int y = Ground + 1; y <= Ground + 3; y++)
            if (sim.World.Get(x, y, 30) == BlockType.StoneRubble || sim.World.Get(x, y, 29) == BlockType.StoneRubble || sim.World.Get(x, y, 31) == BlockType.StoneRubble) rubble.Add(x);
        Assert.Contains(rubble, x => x > Brink && x <= Brink + RegionGenerator.ScreeReach);
    }

    /// <summary>A player's unit killed by the falling rocks is reported as lost to the ground; the one that lived is not.</summary>
    [Fact]
    public void AUnitKilledByFallingRocksIsReported()
    {
        var sim = WithCliff(out _);
        int id = sim.SpawnUnit(Settlers, "spearman", Brink + 2, 30);
        sim.Units.Hp[sim.Units.IndexOf(id)] = PhysicsSystem.CrushDamage;
        long seq = sim.Events.NextSeq - 1;
        sim.Run(5 * Tps);
        Assert.False(sim.Units.Exists(id) && Hp(sim, id) > 0);
        Assert.Contains(sim.Events.After(seq), e => e.Kind == GameEventKind.UnitLostToHazard && e.Subject == id);
    }

    /// <summary>Nothing falls while no one is on the scree: a unit beside it is left alone, and the rocks stay.</summary>
    [Fact]
    public void TheRocksStayWhileNoOneIsOnTheScree()
    {
        var sim = WithCliff(out int scree);
        int id = sim.SpawnUnit(Settlers, "spearman", Brink + 7, 30);
        int full = Hp(sim, id);
        sim.Run(5 * Tps);
        Assert.Equal(4, Count(sim, BlockType.LooseRock));
        Assert.Equal(scree, Count(sim, BlockType.Scree));
        Assert.Equal(full, Hp(sim, id));
    }

    /// <summary>A unit on a move order along the cliff's foot walks round the scree rather than across it.</summary>
    [Fact]
    public void UnitsOnAMoveOrderWalkAroundTheScree()
    {
        var sim = WithCliff(out _);
        int id = sim.SpawnUnit(Settlers, "spearman", Brink + 2, 20);
        sim.Schedule(0, new MoveCommand(Settlers, new[] { id }, Brink + 2, 40));
        sim.Run(30 * Tps);
        Assert.Equal(4, Count(sim, BlockType.LooseRock));
        int i = sim.Units.IndexOf(id);
        Assert.Equal(40, sim.Units.Z[i].FloorToInt());
    }

    /// <summary>Workers quarry loose rock for stone and dig scree away (it gives nothing); both fall when undercut.</summary>
    [Fact]
    public void LooseRockIsQuarriedAndScreeDugAway()
    {
        Assert.True(Harvest.CanGather(BlockType.LooseRock));
        Assert.True(Harvest.Yields(BlockType.LooseRock, out var good));
        Assert.Equal(Good.Stone, good);
        Assert.Equal(BlockType.StoneRubble, BlockInfo.RubbleOf(BlockType.LooseRock));
        Assert.True(Harvest.CanGather(BlockType.Scree));
        Assert.False(Harvest.Yields(BlockType.Scree, out _));
        Assert.True(BlockInfo.CanFall(BlockType.Scree));
        Assert.True(BlockInfo.CanFall(BlockType.LooseRock));
        Assert.True(BlockInfo.IsGround(BlockType.Scree));
    }

    /// <summary>
    /// The default map has rockfalls in the hills: loose rock on a brink with scree below it, none near a start,
    /// several of them, the same for the same seed.
    /// </summary>
    [Fact]
    public void TheDefaultMapHasRockfallsInTheHills()
    {
        int size = RegionGenerator.DefaultSize;
        var map = RegionGenerator.Generate(1234, size, RegionGenerator.DefaultHeight, size);
        var w = map.World;
        var rocks = new List<(int X, int Y, int Z)>();
        for (int z = 0; z < w.SizeZ; z++)
        for (int x = 0; x < w.SizeX; x++)
        {
            int top = w.SurfaceHeight(x, z);
            for (int y = Math.Max(0, top - 1); y <= top; y++)
                if (w.Get(x, y, z) == BlockType.LooseRock) rocks.Add((x, y, z));
        }
        Assert.NotEmpty(rocks);
        foreach (var (x, y, z) in rocks)
        {
            Assert.Equal(Biome.Hills, map.BiomeAt(x, z));
            foreach (var s in map.Starts)
                Assert.True(IntMath.Distance(x, z, s.X, s.Z) > RegionGenerator.StartResourceRadius, $"loose rock at {x},{z} is near a start");
        }
        // Each rockfall has its top rock two over the ground: count those.
        int falls = rocks.Count(r => w.Get(r.X, r.Y - 1, r.Z) == BlockType.LooseRock);
        int want = RegionGenerator.RockfallsPer512 * size * size / (512 * 512);
        Assert.InRange(falls, want / 2, want);
        foreach (var (x, y, z) in rocks.Where(r => w.Get(r.X, r.Y - 1, r.Z) == BlockType.LooseRock))
        {
            int scree = 0;
            for (int dz = -6; dz <= 6; dz++)
            for (int dx = -6; dx <= 6; dx++)
                if (w.Get(x + dx, w.SurfaceHeight(x + dx, z + dz), z + dz) == BlockType.Scree) scree++;
            Assert.True(scree > 0, $"no scree under the rocks at {x},{z}");
        }
        var again = RegionGenerator.Generate(1234, size, RegionGenerator.DefaultHeight, size).World;
        foreach (var (x, y, z) in rocks) Assert.Equal(BlockType.LooseRock, again.Get(x, y, z));
    }
}
