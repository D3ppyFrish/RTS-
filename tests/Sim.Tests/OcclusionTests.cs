using System.Collections.Generic;
using System.Linq;
using Rts.Sim.Meshing;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Ambient occlusion baked into the block and fine-voxel meshes (see <see cref="Occlusion"/>).</summary>
public class OcclusionTests
{
    private static List<Quad> Mesh(VoxelWorld w, bool ao)
    {
        var padded = new BlockType[VoxelWorld.PaddedSize * VoxelWorld.PaddedSize * VoxelWorld.PaddedSize];
        w.CopyChunkPadded(0, 0, 0, padded);
        var opaque = new List<Quad>();
        GreedyMesher.Build(padded, opaque, new List<Quad>(), ao);
        return opaque;
    }

    [Fact]
    public void LevelCountsTheSolidsAroundACorner()
    {
        Assert.Equal(3, Occlusion.Level(false, false, false));
        Assert.Equal(2, Occlusion.Level(true, false, false));
        Assert.Equal(2, Occlusion.Level(false, false, true));
        Assert.Equal(1, Occlusion.Level(true, false, true));
        // Two sides close the corner off whatever the diagonal holds.
        Assert.Equal(0, Occlusion.Level(true, true, false));
    }

    [Fact]
    public void OpenGroundStillMergesIntoOneQuad()
    {
        var w = new VoxelWorld(16, 16, 16);
        for (int x = 0; x < 16; x++)
        for (int z = 0; z < 16; z++)
            w.Set(x, 2, z, BlockType.Grass);
        var top = Assert.Single(Mesh(w, ao: true), q => q.Face == 2);
        Assert.Equal(256, top.W * top.H);
        Assert.Equal(Occlusion.Open, top.Ao);
    }

    [Fact]
    public void GroundDarkensBesideABlockStandingOnIt()
    {
        var w = new VoxelWorld(16, 16, 16);
        for (int x = 0; x < 16; x++)
        for (int z = 0; z < 16; z++)
            w.Set(x, 2, z, BlockType.Grass);
        w.Set(8, 3, 8, BlockType.Stone);
        var tops = Mesh(w, ao: true).Where(q => q.Face == 2 && q.Y == 3).ToList();
        // The ground right beside the block is shaded on the corners that touch it, and nowhere else.
        var beside = Assert.Single(tops, q => q.X == 9 && q.Z == 8);
        Assert.Equal((1, 1), (beside.W, beside.H));
        var levels = Enumerable.Range(0, 4).Select(beside.AoAt).ToList();
        Assert.Equal(2, levels.Count(l => l == 2));
        Assert.Equal(2, levels.Count(l => l == 3));
        // Further off the ground is open and still merges.
        Assert.Contains(tops, q => q.Ao == Occlusion.Open && q.W * q.H > 1);
        // The block's own top is open.
        Assert.Equal(Occlusion.Open, Assert.Single(Mesh(w, ao: true), q => q.Face == 2 && q.Y == 4).Ao);
    }

    [Fact]
    public void WithoutOcclusionTheMeshIsUnchanged()
    {
        var w = new VoxelWorld(16, 16, 16);
        for (int x = 0; x < 16; x++)
        for (int z = 0; z < 16; z++)
            w.Set(x, 2, z, BlockType.Grass);
        w.Set(8, 3, 8, BlockType.Stone);
        var plain = Mesh(w, ao: false);
        Assert.All(plain, q => Assert.Equal(Occlusion.Open, q.Ao));
        Assert.True(plain.Count < Mesh(w, ao: true).Count);
    }

    [Fact]
    public void FineVoxelsShadeTheInsideOfACorner()
    {
        // A floor of fine voxels with walls along two edges: the floor in the corner between them is shaded
        // as a whole face, and the open floor is not.
        const int n = 6;
        var grid = new byte[n * n * n];
        int At(int x, int y, int z) => (y * n + z) * n + x;
        for (int x = 1; x < 5; x++)
        for (int z = 1; z < 5; z++)
            grid[At(x, 1, z)] = StructurePalette.StoneA;
        for (int k = 1; k < 5; k++)
        {
            grid[At(1, 2, k)] = StructurePalette.StoneA;
            grid[At(k, 2, 1)] = StructurePalette.StoneA;
        }
        var quads = new List<FineQuad>();
        StructureMesher.Mesh(grid, n, n, n, 0, 0, 0, quads, ao: true);
        var floor = quads.Where(q => q.Face == 2 && q.Y == 2).ToList();
        var corner = Assert.Single(floor, q => q.X == 2 && q.Z == 2);
        Assert.True(Occlusion.Uniform(corner.Ao));
        Assert.True(corner.AoAt(0) < 3);
        Assert.All(floor.Where(q => q != corner), q => Assert.Equal(Occlusion.Open, q.Ao));
    }
}
