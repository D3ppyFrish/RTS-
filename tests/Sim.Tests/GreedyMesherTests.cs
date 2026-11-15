using System.Collections.Generic;
using System.Linq;
using Rts.Sim.Meshing;
using Xunit;

namespace Rts.Sim.Tests;

public class GreedyMesherTests
{
    private static readonly (int X, int Y, int Z)[] Normals =
        { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) };

    private static (List<Quad> Opaque, List<Quad> Water) Mesh(VoxelWorld w, int cx = 0, int cy = 0, int cz = 0)
    {
        var padded = new BlockType[VoxelWorld.PaddedSize * VoxelWorld.PaddedSize * VoxelWorld.PaddedSize];
        w.CopyChunkPadded(cx, cy, cz, padded);
        var opaque = new List<Quad>();
        var water = new List<Quad>();
        GreedyMesher.Build(padded, opaque, water);
        return (opaque, water);
    }

    [Fact]
    public void SingleBlockGivesSixUnitQuads()
    {
        var w = new VoxelWorld(16, 16, 16);
        w.Set(3, 4, 5, BlockType.Stone);
        var (opaque, water) = Mesh(w);
        Assert.Empty(water);
        Assert.Equal(6, opaque.Count);
        Assert.All(opaque, q => Assert.Equal((1, 1), (q.W, q.H)));
        Assert.Equal(Enumerable.Range(0, 6), opaque.Select(q => (int)q.Face).OrderBy(f => f));
    }

    [Fact]
    public void FlatSlabMergesIntoSixQuads()
    {
        var w = new VoxelWorld(16, 16, 16);
        for (int x = 0; x < 16; x++)
        for (int z = 0; z < 16; z++)
            w.Set(x, 2, z, BlockType.Grass);
        var (opaque, _) = Mesh(w);
        Assert.Equal(6, opaque.Count);
        var top = Assert.Single(opaque, q => q.Face == 2);
        Assert.Equal(256, top.W * top.H);
        Assert.Equal(3, top.Y);
    }

    [Fact]
    public void DifferentBlockTypesAreNotMerged()
    {
        var w = new VoxelWorld(16, 16, 16);
        for (int x = 0; x < 16; x++)
        for (int z = 0; z < 16; z++)
            w.Set(x, 0, z, x < 8 ? BlockType.Stone : BlockType.Sand);
        var (opaque, _) = Mesh(w);
        var tops = opaque.Where(q => q.Face == 2).ToList();
        Assert.Equal(2, tops.Count);
        Assert.All(tops, q => Assert.Equal(128, q.W * q.H));
    }

    [Fact]
    public void WaterGoesToItsOwnListAndHidesSharedFaces()
    {
        var w = new VoxelWorld(16, 16, 16);
        w.Set(1, 1, 1, BlockType.Stone);
        w.Set(1, 2, 1, BlockType.Water);
        w.Set(2, 2, 1, BlockType.Water);
        var (opaque, water) = Mesh(w);
        // Stone top under water is still drawn (water is see-through).
        Assert.Contains(opaque, q => q.Face == 2 && q.Y == 2);
        // Water touching water has no face between them; water on stone has no bottom face there.
        Assert.DoesNotContain(water, q => q.Face == 0 && q.X == 2);
        var top = Assert.Single(water, q => q.Face == 2);
        Assert.Equal(2, top.W * top.H);
    }

    [Fact]
    public void NeighbourChunkHidesBorderFaces()
    {
        var w = new VoxelWorld(32, 16, 16);
        w.Set(15, 0, 0, BlockType.Stone);
        w.Set(16, 0, 0, BlockType.Stone);
        var (opaque, _) = Mesh(w);
        Assert.DoesNotContain(opaque, q => q.Face == 0);
        Assert.Equal(5, opaque.Count);
    }

    [Fact]
    public void CornersWindCounterClockwiseFromOutside()
    {
        var w = new VoxelWorld(16, 16, 16);
        w.Set(3, 4, 5, BlockType.Stone);
        w.Set(4, 4, 5, BlockType.Stone);
        w.Set(3, 5, 5, BlockType.Stone);
        foreach (var q in Mesh(w).Opaque)
        {
            var c0 = q.Corner(0); var c1 = q.Corner(1); var c2 = q.Corner(2);
            var e1 = (c1.X - c0.X, c1.Y - c0.Y, c1.Z - c0.Z);
            var e2 = (c2.X - c0.X, c2.Y - c0.Y, c2.Z - c0.Z);
            var cross = (e1.Item2 * e2.Item3 - e1.Item3 * e2.Item2,
                         e1.Item3 * e2.Item1 - e1.Item1 * e2.Item3,
                         e1.Item1 * e2.Item2 - e1.Item2 * e2.Item1);
            var n = Normals[q.Face];
            Assert.True(cross.Item1 * n.X + cross.Item2 * n.Y + cross.Item3 * n.Z > 0, $"face {q.Face} winds the wrong way");
        }
    }

    [Fact]
    public void MergedAreaMatchesNaiveFaceCountOnGeneratedTerrain()
    {
        var w = new VoxelWorld(48, 32, 48);
        Rts.Sim.World.RegionGenerator.Generate(w, 99);
        for (int cx = 0; cx < w.ChunksX; cx++)
        for (int cy = 0; cy < w.ChunksY; cy++)
        for (int cz = 0; cz < w.ChunksZ; cz++)
        {
            var (opaque, water) = Mesh(w, cx, cy, cz);
            var naive = new int[6, (int)BlockInfo.Last + 1];
            for (int lx = 0; lx < 16; lx++)
            for (int ly = 0; ly < 16; ly++)
            for (int lz = 0; lz < 16; lz++)
            {
                int x = cx * 16 + lx, y = cy * 16 + ly, z = cz * 16 + lz;
                var b = w.Get(x, y, z);
                for (int f = 0; f < 6; f++)
                {
                    var n = Normals[f];
                    if (GreedyMesher.FaceVisible(b, w.Get(x + n.X, y + n.Y, z + n.Z))) naive[f, (int)b]++;
                }
            }
            var merged = new int[6, (int)BlockInfo.Last + 1];
            foreach (var q in opaque.Concat(water)) merged[q.Face, (int)q.Block] += q.W * q.H;
            Assert.Equal(naive, merged);
        }
    }

    [Fact]
    public void PaddedCopyMatchesWorld()
    {
        var w = new VoxelWorld(40, 20, 40);
        Rts.Sim.World.RegionGenerator.Generate(w, 5);
        var padded = new BlockType[VoxelWorld.PaddedSize * VoxelWorld.PaddedSize * VoxelWorld.PaddedSize];
        w.CopyChunkPadded(1, 0, 1, padded);
        for (int y = -1; y <= 16; y++)
        for (int z = -1; z <= 16; z++)
        for (int x = -1; x <= 16; x++)
            Assert.Equal(w.Get(16 + x, y, 16 + z), padded[GreedyMesher.Index(x, y, z)]);
    }
}
