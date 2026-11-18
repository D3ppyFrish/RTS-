using Rts.Sim.Economy;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Bigger trees: <see cref="VoxelWorld.TreeScale"/> grows every tree that many times bigger (generated maps: 2).</summary>
public class BigTreeTests
{
    private const int Ground = 3;

    private static int Count(VoxelWorld w, System.Func<BlockType, bool> what)
    {
        int n = 0;
        for (int y = 0; y < w.SizeY; y++)
        for (int z = 0; z < w.SizeZ; z++)
        for (int x = 0; x < w.SizeX; x++)
            if (what(w.Get(x, y, z))) n++;
        return n;
    }

    [Fact]
    public void ATreeOfScaleTwoHasATrunkTwoSquareAndIsTwiceAsTallWithEightTimesTheLeaves()
    {
        var usual = TestWorlds.Flat(48, 40, 48, Ground);
        Assert.True(Trees.Grow(usual, 24, Ground, 24, 5, 0, TreeKind.Broadleaf, 1));
        var big = TestWorlds.Flat(48, 40, 48, Ground);
        big.TreeScale = 2;
        Assert.True(Trees.Grow(big, 25, Ground, 25, 5, 0, TreeKind.Broadleaf, 1)); // (25, 25) lies in the square from (24, 24)

        Assert.Equal(5, Count(usual, BlockInfo.IsTrunk));
        Assert.Equal(5 * 8, Count(big, BlockInfo.IsTrunk));
        Assert.Equal(Count(usual, BlockInfo.IsLeaves) * 8, Count(big, BlockInfo.IsLeaves));
        for (int y = Ground + 1; y <= Ground + 10; y++)
        for (int z = 24; z <= 25; z++)
        for (int x = 24; x <= 25; x++)
            Assert.Equal(BlockType.Wood, big.Get(x, y, z));
        Assert.Equal((24, 24, 2), Trees.Footprint(big, 25, 24));
    }

    [Fact]
    public void ABigTreeOnASlopeReachesDownToTheGroundInEveryColumn()
    {
        var w = TestWorlds.Flat(48, 40, 48, Ground);
        w.TreeScale = 2;
        w.Set(24, Ground, 24, BlockType.Air); // a dip under one column of the trunk
        Assert.True(Trees.Grow(w, 25, Ground, 25, 5, 0, TreeKind.Pine, 1));
        Assert.Equal(BlockType.PineWood, w.Get(24, Ground, 24));
    }

    [Fact]
    public void FellingABigTreeTakesItsWholeTrunkAndCrownAndLaysTwiceAUsualTreesLogs()
    {
        var w = TestWorlds.Flat(64, 40, 64, Ground);
        w.TreeScale = 2;
        Assert.True(Trees.Grow(w, 30, Ground, 30, 4, 0, TreeKind.Broadleaf, 1));
        var sim = TestWorlds.Sim(w);
        int trunk = Count(w, BlockInfo.IsTrunk);
        var (b, t) = Harvest.Span(w, 31, Ground + 2, 31);
        Assert.Equal(trunk, sim.Physics.FellTree(31, b, t, 31, 26, 31));
        Assert.Equal(0, Count(w, BlockInfo.IsTrunk));
        Assert.Equal(0, Count(w, BlockInfo.IsLeaves));
        sim.Run(PhysicsSystem.TreeFallTicks + 1);
        Assert.Equal(trunk / 4, Count(w, b => b == BlockType.TreeRubble)); // a log for every four blocks: twice a usual tree's wood
    }

    [Fact]
    public void AGeneratedMapGrowsBigTreesEachOnItsOwnSquareAndTreesOneGrowsTheFirstOnes()
    {
        var big = RegionGenerator.Generate(7, 128, 48, 128).World; // generated maps grow trees twice the size
        Assert.Equal(2, big.TreeScale);
        int trees = 0;
        for (int z = 0; z < big.SizeZ; z++)
        for (int x = 0; x < big.SizeX; x++)
        for (int y = 1; y < big.SizeY; y++)
        {
            if (!BlockInfo.IsTrunk(big.Get(x, y, z)) || BlockInfo.IsTrunk(big.Get(x, y - 1, z))) continue;
            if (x % 2 == 0 && z % 2 == 0) trees++;
            // Every column of a trunk's square holds trunk too, a few levels up.
            var (x0, z0, s) = Trees.Footprint(big, x, z);
            for (int k = 0; k < s; k++)
            for (int i = 0; i < s; i++)
                Assert.True(BlockInfo.IsTrunk(big.Get(x0 + i, y + 3, z0 + k)), $"trunk square at {x0},{z0}");
        }
        Assert.True(trees > 5, $"only {trees} big trees");
        // --trees=1 still grows the first trees, as a world left at the first size does.
        var usual = new VoxelWorld(128, 48, 128);
        RegionGenerator.Generate(usual, 7);
        Assert.Equal(usual.ComputeHash(), RegionGenerator.Generate(7, 128, 48, 128, treeScale: 1).World.ComputeHash());
    }

    [Fact]
    public void AMatchLogKeepsItsTreeSizeAndAnOlderLogReplaysWithTheFirstTrees()
    {
        var log = Replay.CommandLog.UsualStart(5, 64, 32, 64);
        Assert.Equal(RegionGenerator.DefaultTreeScale, log.TreeScale);
        string text = log.ToString();
        Assert.Contains("trees 2\n", text);
        Assert.Equal(2, Replay.CommandLog.Parse(text).TreeScale);
        Assert.Equal(2, log.CreateSimulation().World.TreeScale);
        var older = Replay.CommandLog.Parse(text.Replace("trees 2\n", ""));
        Assert.Equal(1, older.TreeScale);
        Assert.Equal(1, older.CreateSimulation().World.TreeScale);
    }
}
