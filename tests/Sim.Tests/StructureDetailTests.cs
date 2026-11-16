using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.Economy;
using Rts.Sim.Meshing;
using Xunit;

namespace Rts.Sim.Tests;

public class StructureDetailTests
{
    private const int F = StructureDetail.Size;

    /// <summary>The fine voxels of block (x, y, z) of <paramref name="w"/>.</summary>
    private static byte[] Shape(VoxelWorld w, int x, int y, int z)
    {
        var near = new BlockType[27];
        for (int dy = -1; dy <= 1; dy++)
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
            near[StructureDetail.NeighbourIndex(dx, dy, dz)] = w.Get(x + dx, y + dy, z + dz);
        var shape = new byte[StructureDetail.Volume];
        StructureDetail.Fill(near, x, y, z, shape);
        return shape;
    }

    private static byte At(byte[] shape, int x, int y, int z) => shape[StructureDetail.VoxelIndex(x, y, z)];

    private static VoxelWorld Ground()
    {
        var w = new VoxelWorld(32, 16, 32);
        for (int x = 0; x < 32; x++)
        for (int z = 0; z < 32; z++)
            w.Set(x, 0, z, BlockType.Grass);
        return w;
    }

    [Fact]
    public void OnlyBuildingBlocksGetDetail()
    {
        var structures = Enum.GetValues<BlockType>().Where(StructureDetail.IsStructure).ToArray();
        Assert.Equal(new[] { BlockType.Planks, BlockType.Thatch, BlockType.RoofTile, BlockType.StoneBrick, BlockType.Door, BlockType.Gate, BlockType.Palisade, BlockType.Rampart },
            structures);
    }

    [Fact]
    public void BuriedBlockIsSolidThrough()
    {
        foreach (var t in new[] { BlockType.Planks, BlockType.StoneBrick, BlockType.Thatch, BlockType.RoofTile })
        {
            var w = new VoxelWorld(8, 8, 8);
            for (int x = 2; x <= 4; x++)
            for (int y = 2; y <= 4; y++)
            for (int z = 2; z <= 4; z++)
                w.Set(x, y, z, t);
            var shape = Shape(w, 3, 3, 3);
            Assert.All(shape, v => Assert.NotEqual(StructurePalette.Empty, v));
        }
    }

    [Fact]
    public void WallTopHasBattlementsAndSolidBase()
    {
        var w = Ground();
        for (int x = 4; x <= 10; x++)
        for (int y = 1; y <= 3; y++)
            w.Set(x, y, 8, BlockType.StoneBrick);
        var top = Shape(w, 7, 3, 8);
        int gaps = 0;
        for (int y = 0; y < F; y++)
        for (int z = 0; z < F; z++)
        for (int x = 0; x < F; x++)
        {
            if (z == 0 || z == F - 1) continue; // the faces, where the mortar is sunk
            if (y < F / 2) Assert.NotEqual(StructurePalette.Empty, At(top, x, y, z));
            else if (At(top, x, y, z) == StructurePalette.Empty) gaps++;
        }
        // Each wall block has one crenel three voxels wide and four deep, right through the wall.
        Assert.Equal(3 * (F - 2) * (F / 2), gaps);
        // The crenel runs along the wall: the same columns are open on both faces.
        for (int x = 0; x < F; x++)
            Assert.Equal(At(top, x, F - 1, 0) == StructurePalette.Empty, At(top, x, F - 1, F - 1) == StructurePalette.Empty);
    }

    [Fact]
    public void ABarrierIsARowOfPointedStakes()
    {
        var w = Ground();
        for (int x = 4; x <= 10; x++)
        for (int y = 1; y <= 2; y++)
            w.Set(x, y, 8, BlockType.Palisade);
        var bottom = Shape(w, 7, 1, 8);
        var top = Shape(w, 7, 2, 8);
        for (int y = 0; y < F; y++)
        for (int z = 0; z < F; z++)
        for (int x = 0; x < F; x++)
        {
            // Four stakes to a block, each four voxels across with its corners rounded off, the same all the way up...
            bool corner = (x % 4 is 0 or 3) && (z % 4 is 0 or 3);
            Assert.Equal(corner, At(bottom, x, y, z) == StructurePalette.Empty);
            // ...and cut to a point at the top of the barrier: only the middle of each stake reaches the last voxel.
            bool tip = x % 4 is 1 or 2 && z % 4 is 1 or 2;
            Assert.Equal(y == F - 1 ? !tip : corner, At(top, x, y, z) == StructurePalette.Empty);
        }
        Assert.Equal(StructurePalette.Heartwood, At(top, 1, F - 1, 1)); // the cut wood is pale
        Assert.Contains(At(bottom, 1, 0, 0), new[] { StructurePalette.StakeA, StructurePalette.StakeB, StructurePalette.BarkA, StructurePalette.TimberDark });
    }

    [Fact]
    public void AWoodenWallHasAParapetAndAHouseWallDoesNot()
    {
        var w = Ground();
        for (int x = 4; x <= 10; x++)
        for (int y = 1; y <= 3; y++)
            w.Set(x, y, 8, BlockType.Planks);
        int Gaps(byte[] shape)
        {
            int gaps = 0;
            for (int y = F / 2; y < F; y++)
            for (int z = 0; z < F; z++)
            for (int x = 0; x < F; x++)
                if (x >= 5 && At(shape, x, y, z) == StructurePalette.Empty) gaps++;
            return gaps;
        }
        // The top block of a wall standing free has a gap three voxels wide and four deep, right through it.
        Assert.Equal(3 * F * (F / 2), Gaps(Shape(w, 7, 3, 8)));
        Assert.True(Gaps(Shape(w, 7, 2, 8)) < F * (F / 2)); // the block under it is whole (but for its sunk boards)
        // A plank wall with a roof on it is a house's: no parapet.
        for (int x = 4; x <= 10; x++) w.Set(x, 4, 8, BlockType.Thatch);
        Assert.True(Gaps(Shape(w, 7, 3, 8)) < F * (F / 2));
    }

    [Fact]
    public void RoofEdgesSlopeAtFortyFiveDegrees()
    {
        var w = Ground();
        // A two-layer stepped roof: a 3 x 3 layer with one block on top.
        for (int x = 4; x <= 6; x++)
        for (int z = 4; z <= 6; z++)
            w.Set(x, 1, z, BlockType.Thatch);
        w.Set(5, 2, 5, BlockType.Thatch);
        var edge = Shape(w, 6, 1, 5); // open towards +X only
        for (int x = 0; x < F; x++)
        for (int y = 0; y < F; y++)
            Assert.Equal(y <= F - 1 - x, At(edge, x, y, 3) != StructurePalette.Empty);
        var corner = Shape(w, 6, 1, 6); // open towards +X and +Z: a hip
        Assert.NotEqual(StructurePalette.Empty, At(corner, 0, F - 1, 0));
        Assert.Equal(StructurePalette.Empty, At(corner, 0, F - 1, F - 1));
        Assert.Equal(StructurePalette.Empty, At(corner, F - 1, 1, F - 1));
        Assert.NotEqual(StructurePalette.Empty, At(corner, F - 1, 0, F - 1));
        var covered = Shape(w, 5, 1, 5); // under the top block
        Assert.All(covered, v => Assert.NotEqual(StructurePalette.Empty, v));
    }

    [Fact]
    public void PlankWallsGetAFrameAndWindows()
    {
        var w = Ground();
        // A 6 x 6 house shell three blocks high, with a roof over it.
        for (int x = 4; x <= 9; x++)
        for (int z = 4; z <= 9; z++)
        {
            for (int y = 1; y <= 3; y++) w.Set(x, y, z, BlockType.Planks);
            w.Set(x, 4, z, BlockType.RoofTile);
        }
        var corner = Shape(w, 9, 2, 9);
        Assert.Equal(StructurePalette.TimberDark, At(corner, F - 1, 4, F - 1));
        var windows = 0;
        for (int x = 5; x <= 8; x++)
        {
            var s = Shape(w, x, 2, 9);
            if (s.Contains(StructurePalette.Glass)) windows++;
            // The boards sit a voxel back from the face, so the frame stands out.
            Assert.Contains(StructurePalette.Empty, s);
        }
        Assert.Equal(2, windows); // every other block along the wall
        // No windows in the top course, under the roof, or the bottom course, on the ground.
        Assert.DoesNotContain(StructurePalette.Glass, Shape(w, 6, 3, 9));
        Assert.DoesNotContain(StructurePalette.Glass, Shape(w, 6, 1, 9));
    }

    [Fact]
    public void ShapesAreTheSameEveryTime()
    {
        var w = Ground();
        for (int y = 1; y <= 3; y++) w.Set(5, y, 5, BlockType.StoneBrick);
        Assert.Equal(Shape(w, 5, 3, 5), Shape(w, 5, 3, 5));
        Assert.Equal(Shape(w, 5, 2, 5), Shape(w, 5, 2, 5));
    }

    // --- Meshing ------------------------------------------------------------------------------------------

    private static List<FineQuad> MeshGrid(byte[] grid, int n)
    {
        var quads = new List<FineQuad>();
        StructureMesher.Mesh(grid, n, n, n, 0, 0, 0, quads);
        return quads;
    }

    [Fact]
    public void SingleVoxelGivesSixUnitQuads()
    {
        var grid = new byte[4 * 4 * 4];
        grid[(1 * 4 + 1) * 4 + 1] = StructurePalette.StoneA;
        var quads = MeshGrid(grid, 4);
        Assert.Equal(6, quads.Count);
        Assert.All(quads, q => Assert.Equal((1, 1), (q.W, q.H)));
    }

    [Fact]
    public void SameColourMergesAndOccludersHideFaces()
    {
        var grid = new byte[4 * 4 * 4];
        for (int y = 1; y < 3; y++)
        for (int z = 0; z < 2; z++)
        for (int x = 0; x < 2; x++)
            grid[(y * 4 + z) * 4 + x] = StructurePalette.PlankA;
        Assert.Equal(6, MeshGrid(grid, 4).Count);
        // Terrain under the cube hides its bottom and is not drawn itself.
        for (int z = 0; z < 4; z++)
        for (int x = 0; x < 4; x++)
            grid[(0 * 4 + z) * 4 + x] = StructurePalette.Occluder;
        var quads = MeshGrid(grid, 4);
        Assert.DoesNotContain(quads, q => q.Face == 3);
        Assert.All(quads, q => Assert.Equal(StructurePalette.PlankA, q.Colour));
    }

    [Fact]
    public void MergedQuadsCoverEveryVisibleFace()
    {
        var rng = new DeterministicRandom(7);
        const int n = 10;
        var grid = new byte[n * n * n];
        for (int i = 0; i < grid.Length; i++) grid[i] = (byte)(rng.Range(0, 3) == 0 ? 0 : 1 + rng.Range(0, 3));
        long naive = 0;
        int[][] dirs = { new[] { 1, 0, 0 }, new[] { -1, 0, 0 }, new[] { 0, 1, 0 }, new[] { 0, -1, 0 }, new[] { 0, 0, 1 }, new[] { 0, 0, -1 } };
        for (int y = 0; y < n; y++)
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            if (grid[(y * n + z) * n + x] == 0) continue;
            foreach (var d in dirs)
            {
                int ax = x + d[0], ay = y + d[1], az = z + d[2];
                bool inside = ax >= 0 && ay >= 0 && az >= 0 && ax < n && ay < n && az < n;
                if (!inside || grid[(ay * n + az) * n + ax] == 0) naive++;
            }
        }
        Assert.Equal(naive, MeshGrid(grid, n).Sum(q => (long)q.W * q.H));
    }

    [Fact]
    public void HouseMeshesWithinItsBlocksAndTheGroundStillShows()
    {
        var w = Ground();
        var def = new BuildingDef { Id = "house", Name = "House", Kind = BuildingKind.House, Roof = BlockType.Thatch };
        foreach (var t in def.Blocks) w.Set(6 + t.X, 1 + t.Y, 6 + t.Z, t.Block);
        var padded = new BlockType[VoxelWorld.PaddedSize * VoxelWorld.PaddedSize * VoxelWorld.PaddedSize];
        w.CopyChunkPadded(0, 0, 0, padded);
        Assert.True(StructureMesher.HasStructures(padded));

        var quads = new List<FineQuad>();
        StructureMesher.Build(padded, 0, 0, 0, quads);
        Assert.NotEmpty(quads);
        int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;
        foreach (var q in quads)
        for (int k = 0; k < 4; k++)
        {
            var (x, y, z) = q.Corner(k);
            minX = Math.Min(minX, x); minY = Math.Min(minY, y); minZ = Math.Min(minZ, z);
            maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); maxZ = Math.Max(maxZ, z);
        }
        Assert.Equal((6 * F, 1 * F, 6 * F), (minX, minY, minZ));
        Assert.Equal(((6 + def.SizeX) * F, (1 + def.Height) * F, (6 + def.SizeZ) * F), (maxX, maxY, maxZ));
        Assert.DoesNotContain(quads, q => q.Face == 3 && q.Y == F); // the ground hides the floor

        // The terrain mesher sees the building as air, so it draws the ground's top under the building.
        var stripped = new BlockType[padded.Length];
        StructureMesher.StripStructures(padded, stripped);
        Assert.DoesNotContain(stripped, StructureDetail.IsDetailed);
        var opaque = new List<Quad>();
        GreedyMesher.Build(stripped, opaque, new List<Quad>());
        Assert.All(opaque, q => Assert.False(StructureDetail.IsDetailed(q.Block)));
    }

    [Fact]
    public void RubbleIsAHeapOfLumpsUnlessCovered()
    {
        var w = Ground();
        for (int x = 4; x <= 6; x++)
        for (int z = 4; z <= 6; z++)
            w.Set(x, 1, z, BlockType.StoneRubble);
        w.Set(5, 2, 5, BlockType.WoodRubble);
        var covered = Shape(w, 5, 1, 5);
        Assert.All(covered, v => Assert.NotEqual(StructurePalette.Empty, v));
        var heap = Shape(w, 5, 2, 5);
        Assert.Contains(StructurePalette.Empty, heap);
        for (int z = 0; z < F; z++)
        for (int x = 0; x < F; x++)
            Assert.NotEqual(StructurePalette.Empty, At(heap, x, 0, z)); // every lump stands on the block below
        Assert.All(heap.Where(v => v != StructurePalette.Empty),
            v => Assert.Contains(v, new[] { StructurePalette.PlankA, StructurePalette.PlankB, StructurePalette.PlankC, StructurePalette.Timber, StructurePalette.TimberDark }));
        Assert.True(StructureDetail.IsDetailed(BlockType.StoneRubble));
        Assert.False(StructureDetail.IsStructure(BlockType.StoneRubble));
    }

    [Fact]
    public void TreesAreRoundTrunksUnderLeafyCrownsAndFelledTreesAreLogs()
    {
        var w = Ground();
        TestWorlds.Tree(w, 10, 10, 4, ground: 0);
        Assert.True(StructureDetail.IsDetailed(BlockType.Wood));
        Assert.True(StructureDetail.IsDetailed(BlockType.Leaves));

        // The trunk is round, narrower than its block, and flares out at the ground.
        var mid = Shape(w, 10, 2, 10);
        Assert.Equal(StructurePalette.Empty, At(mid, 0, 4, 0));
        Assert.Equal(StructurePalette.Empty, At(mid, 0, 4, 4));
        Assert.NotEqual(StructurePalette.Empty, At(mid, 4, 4, 1));
        var foot = Shape(w, 10, 1, 10);
        Assert.NotEqual(StructurePalette.Empty, At(foot, 0, 0, 4));
        Assert.All(mid.Where(v => v != StructurePalette.Empty),
            v => Assert.Contains(v, new[] { StructurePalette.BarkA, StructurePalette.BarkB, StructurePalette.BarkDark }));

        // Leaves are leaf colours, solid inside the crown and ragged where it meets the air.
        var outer = Shape(w, 12, 4, 10);
        Assert.Contains(StructurePalette.Empty, outer);
        Assert.All(outer.Where(v => v != StructurePalette.Empty), v => Assert.True(StructurePalette.IsLeaf(v)));
        var inner = Shape(w, 11, 4, 10);
        Assert.NotEqual(StructurePalette.Empty, At(inner, 2, 4, 4)); // its middle, against other leaves

        // A felled tree: logs along their line, the cut showing at the open end.
        for (int x = 20; x <= 23; x++) w.Set(x, 1, 20, BlockType.TreeRubble);
        var log = Shape(w, 21, 1, 20);
        Assert.NotEqual(StructurePalette.Empty, At(log, 0, 2, 3));
        Assert.NotEqual(StructurePalette.Empty, At(log, 7, 2, 3)); // runs the block's length along x
        Assert.Equal(StructurePalette.Empty, At(log, 3, 2, 0));    // but is narrower across it
        var end = Shape(w, 23, 1, 20);
        Assert.Equal(StructurePalette.Pith, At(end, 7, 2, 3));
        Assert.Equal(StructurePalette.Heartwood, At(end, 7, 2, 1));
    }

    [Fact]
    public void EachKindOfTreeHasItsOwnBarkAndLeaves()
    {
        var w = Ground();
        TestWorlds.Tree(w, 5, 5, 4, ground: 0);
        TestWorlds.Tree(w, 12, 5, 4, ground: 0, BlockType.BirchWood, BlockType.BirchLeaves);
        TestWorlds.Tree(w, 19, 5, 4, ground: 0, BlockType.PineWood, BlockType.PineNeedles);
        TestWorlds.Tree(w, 26, 5, 4, ground: 0, BlockType.MushroomStem, BlockType.MushroomCap);
        foreach (var t in new[] { BlockType.PineWood, BlockType.BirchWood, BlockType.MushroomStem, BlockType.PineNeedles,
                     BlockType.BirchLeaves, BlockType.WillowLeaves, BlockType.DuskLeaves, BlockType.MushroomCap })
            Assert.True(StructureDetail.IsDetailed(t));

        // A birch is slender: where the broad-leaved trunk still is, the birch's is not.
        var oak = Shape(w, 5, 2, 5);
        var birch = Shape(w, 12, 2, 5);
        Assert.NotEqual(StructurePalette.Empty, At(oak, 1, 4, 4));
        Assert.Equal(StructurePalette.Empty, At(birch, 1, 4, 4));
        Assert.NotEqual(StructurePalette.Empty, At(birch, 4, 4, 2));
        Assert.All(birch.Where(v => v != StructurePalette.Empty),
            v => Assert.Contains(v, new[] { StructurePalette.BirchA, StructurePalette.BirchB, StructurePalette.BirchMark }));
        Assert.Contains(StructurePalette.BirchMark, Enumerable.Range(1, 3).SelectMany(y => Shape(w, 12, y, 5)));
        var pine = Shape(w, 19, 2, 5);
        Assert.All(pine.Where(v => v != StructurePalette.Empty), v => Assert.Contains(v,
            new[] { StructurePalette.PineBarkA, StructurePalette.PineBarkB, StructurePalette.BarkDark }));

        // Each crown in its own colours, all swaying in the wind, except a mushroom's cap.
        var needles = Shape(w, 20, 4, 5);
        Assert.Contains(needles, v => v is StructurePalette.NeedleA or StructurePalette.NeedleB or StructurePalette.NeedleDark);
        Assert.All(needles.Where(v => v != StructurePalette.Empty), v => Assert.True(StructurePalette.IsLeaf(v)));
        var cap = Shape(w, 27, 3, 5); // the cap's rim, open underneath: gills
        Assert.Contains(StructurePalette.Gills, cap);
        Assert.All(cap.Where(v => v != StructurePalette.Empty), v => Assert.False(StructurePalette.IsLeaf(v)));

        // A willow's hanging edge frays into strands; a dusk tree's crown is violet.
        w.Set(10, 8, 20, BlockType.WillowLeaves);
        w.Set(10, 9, 20, BlockType.WillowLeaves);
        var strands = Shape(w, 10, 8, 20);
        Assert.Contains(StructurePalette.Empty, strands);
        Assert.Contains(strands, v => v is StructurePalette.WillowA or StructurePalette.WillowB);
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
            w.Set(20 + dx, 8, 20 + dz, BlockType.DuskLeaves);
        Assert.Contains(Shape(w, 20, 8, 20), v => v is StructurePalette.DuskA or StructurePalette.DuskB);
    }
}
