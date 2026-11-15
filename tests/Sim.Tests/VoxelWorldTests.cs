using Xunit;

namespace Rts.Sim.Tests;

public class VoxelWorldTests
{
    [Fact]
    public void SetAndGetAcrossChunks()
    {
        var w = new VoxelWorld(40, 20, 40);
        w.Set(0, 0, 0, BlockType.Stone);
        w.Set(39, 19, 39, BlockType.Wood);
        w.Set(17, 3, 16, BlockType.IronOre);
        Assert.Equal(BlockType.Stone, w.Get(0, 0, 0));
        Assert.Equal(BlockType.Wood, w.Get(39, 19, 39));
        Assert.Equal(BlockType.IronOre, w.Get(17, 3, 16));
        Assert.Equal(BlockType.Air, w.Get(5, 5, 5));
    }

    [Fact]
    public void ACloneIsTheSameWorldButSeparate()
    {
        var w = new VoxelWorld(40, 20, 40);
        w.Set(17, 3, 16, BlockType.IronOre);
        w.Set(39, 19, 39, BlockType.Wood);
        var copy = w.Clone();
        Assert.Equal(w.ComputeHash(), copy.ComputeHash());
        int changes = 0;
        w.BlockChanged += (_, _, _) => changes++;
        copy.Set(17, 3, 16, BlockType.Air);
        copy.Set(1, 1, 1, BlockType.Stone);
        Assert.Equal(BlockType.IronOre, w.Get(17, 3, 16));
        Assert.Equal(BlockType.Air, w.Get(1, 1, 1));
        Assert.Equal(0, changes);
        Assert.NotEqual(w.ComputeHash(), copy.ComputeHash());
    }

    [Fact]
    public void OutOfBoundsReadsAsAir()
    {
        var w = new VoxelWorld(16, 16, 16);
        Assert.Equal(BlockType.Air, w.Get(-1, 0, 0));
        Assert.Equal(BlockType.Air, w.Get(0, 16, 0));
    }

    [Fact]
    public void ChunkChangedFiresOnlyOnRealChange()
    {
        var w = new VoxelWorld(32, 16, 32);
        int count = 0;
        w.ChunkChanged += (_, _, _) => count++;
        w.Set(20, 1, 1, BlockType.Stone);
        w.Set(20, 1, 1, BlockType.Stone);
        Assert.Equal(1, count);
    }

    [Fact]
    public void SurfaceHeightFindsTopSolidBlock()
    {
        var w = new VoxelWorld(16, 16, 16);
        w.Set(2, 0, 2, BlockType.Bedrock);
        w.Set(2, 4, 2, BlockType.Grass);
        w.Set(2, 5, 2, BlockType.Water);
        Assert.Equal(4, w.SurfaceHeight(2, 2));
        Assert.Equal(-1, w.SurfaceHeight(3, 3));
    }

    [Fact]
    public void BlockChangedReportsBlockCoordinates()
    {
        var w = new VoxelWorld(32, 16, 32);
        var seen = new System.Collections.Generic.List<(int, int, int)>();
        w.BlockChanged += (x, y, z) => seen.Add((x, y, z));
        w.Set(20, 1, 3, BlockType.Stone);
        w.Set(20, 1, 3, BlockType.Stone);
        Assert.Equal(new[] { (20, 1, 3) }, seen);
    }

    [Fact]
    public void UnallocatedChunksReportEmpty()
    {
        var w = new VoxelWorld(32, 32, 32);
        w.Set(1, 1, 1, BlockType.Stone);
        Assert.True(w.IsChunkAllocated(0, 0, 0));
        Assert.False(w.IsChunkAllocated(1, 1, 1));
        Assert.False(w.IsChunkAllocated(-1, 0, 0));
    }
}
