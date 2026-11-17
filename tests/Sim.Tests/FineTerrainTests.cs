using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.Meshing;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Half-size world blocks (see <see cref="FineTerrain"/>), and what draws them and the far-away world: the
/// mesher on cubes of any size, and fine voxels merged for distance.
/// </summary>
public class FineTerrainTests
{
    /// <summary>Grass ground whose height in each column is <paramref name="height"/> of (x, z).</summary>
    private static VoxelWorld Ground(int sx, int sz, Func<int, int, int> height, int sy = 16)
    {
        var w = new VoxelWorld(sx, sy, sz);
        for (int z = 0; z < sz; z++)
        for (int x = 0; x < sx; x++)
        {
            int h = height(x, z);
            for (int y = 0; y <= h; y++) w.Set(x, y, z, y == h ? BlockType.Grass : BlockType.Soil);
        }
        return w;
    }

    /// <summary>The highest solid half-size block of fine column (fx, fz).</summary>
    private static int Top(VoxelWorld fine, int fx, int fz) => fine.SurfaceHeight(fx, fz);

    [Fact]
    public void EveryBlockBecomesEightHalfSizeBlocks()
    {
        var w = new VoxelWorld(40, 20, 24);
        var rng = new DeterministicRandom(7);
        for (int i = 0; i < 4000; i++)
            w.Set(rng.Range(0, 40), rng.Range(0, 20), rng.Range(0, 24), (BlockType)rng.Range(1, 12));
        var fine = w.Upscaled();
        Assert.Equal((80, 40, 48), (fine.SizeX, fine.SizeY, fine.SizeZ));
        for (int y = 0; y < fine.SizeY; y++)
        for (int z = 0; z < fine.SizeZ; z++)
        for (int x = 0; x < fine.SizeX; x++)
            Assert.Equal(w.Get(x / 2, y / 2, z / 2), fine.Get(x, y, z));
    }

    [Fact]
    public void FlatGroundStaysWhole()
    {
        var w = Ground(32, 32, (_, _) => 4);
        Assert.Equal(w.Upscaled().ComputeHash(), FineTerrain.Build(w).ComputeHash());
    }

    [Fact]
    public void TheEdgeOfAPlateauGetsAShoulder()
    {
        // A step up along x = 16 with no higher ground beyond: the first two blocks of the upper level lose
        // their top half, and nothing else changes.
        var w = Ground(32, 32, (x, _) => x < 16 ? 4 : 5);
        var fine = FineTerrain.Build(w);
        for (int fz = 0; fz < 64; fz++)
        for (int fx = 0; fx < 64; fx++)
        {
            int want = fx < 32 ? 9 : fx < 32 + 2 * FineTerrain.Shoulder ? 10 : 11;
            Assert.Equal(want, Top(fine, fx, fz));
        }
        Assert.Equal(BlockType.Grass, fine.Get(33, 10, 5)); // the lowered step is still grass on top
    }

    [Fact]
    public void ASlopeGetsAStepHalfwayAlongEachTerrace()
    {
        // Three terraces eight blocks wide: the middle one is cut halfway between the lower and the higher.
        var w = Ground(24, 24, (x, _) => 3 + x / 8);
        var fine = FineTerrain.Build(w);
        for (int fx = 16; fx < 32; fx++) Assert.Equal(fx < 24 ? 8 : 9, Top(fine, fx, 20));
        // The lowest has nothing lower to step down to.
        for (int fx = 0; fx < 16; fx++) Assert.Equal(7, Top(fine, fx, 20));
    }

    [Fact]
    public void ContoursCutAcrossBlocks()
    {
        // In a round hollow the half steps are rings, finer than the blocks: about half the ground is
        // lowered, and the rings cut many blocks in part.
        var w = Ground(48, 48, (x, z) => 3 + (int)(Math.Sqrt((x - 24) * (x - 24) + (z - 24) * (z - 24)) / 5));
        var lowered = FineTerrain.Lowered(w, FineTerrain.Heights(w));
        int count = 0, split = 0;
        for (int z = 15; z < 30; z++)
        for (int x = 15; x < 30; x++)
        {
            int part = 0;
            for (int k = 0; k < 4; k++)
                if (lowered[(2 * z + (k >> 1)) * 96 + 2 * x + (k & 1)]) part++;
            count += part;
            if (part is > 0 and < 4) split++;
        }
        Assert.InRange(count, 300, 600); // about half of the 900 columns
        Assert.True(split > 10, $"{split} blocks are lowered in part"); // the contour cuts across blocks
    }

    [Fact]
    public void NothingIsLoweredUnderWhatStandsOnIt()
    {
        var w = Ground(32, 32, (x, _) => x < 16 ? 4 : 5);
        w.Set(16, 6, 10, BlockType.Wood);    // a trunk on the edge
        w.Set(17, 6, 12, BlockType.Planks);  // a wall
        w.Set(16, 6, 14, BlockType.BerryBush);
        var fine = FineTerrain.Build(w);
        foreach (var (x, z) in new[] { (16, 10), (17, 12), (16, 14) })
        for (int k = 0; k < 4; k++)
            Assert.Equal(BlockType.Grass, fine.Get(2 * x + (k & 1), 11, 2 * z + (k >> 1)));
        Assert.Equal(BlockType.Air, fine.Get(32, 11, 16)); // open ground on the same edge is lowered
    }

    [Fact]
    public void TreesDoNotCountAsHigherGround()
    {
        // A crown of leaves over the edge of a plateau leaves the shoulder under it as it is.
        var w = Ground(32, 32, (x, _) => x < 16 ? 4 : 5);
        var plain = FineTerrain.Build(w).ComputeHash();
        for (int dz = -2; dz <= 2; dz++)
        for (int dx = -2; dx <= 2; dx++)
            w.Set(20 + dx, 10, 16 + dz, BlockType.Leaves);
        var fine = FineTerrain.Build(w);
        for (int dz = -2; dz <= 2; dz++)
        for (int dx = -2; dx <= 2; dx++)
            w.Set(20 + dx, 10, 16 + dz, BlockType.Air);
        // The same ground with or without the crown: only the leaves differ.
        for (int fz = 0; fz < 64; fz++)
        for (int fx = 0; fx < 64; fx++)
            Assert.Equal(fx < 32 ? 9 : fx < 36 ? 10 : 11, Enumerable.Range(0, 20).Last(y => FineTerrain.IsTerrain(fine.Get(fx, y, fz))));
        Assert.NotEqual(plain, fine.ComputeHash());
    }

    [Fact]
    public void GroundLevelWithWaterStaysWhole()
    {
        // A pool one block deep in flat ground with a step up behind it: the bank level with the water keeps
        // its whole blocks (a lowered step would lie under the water), the step behind is still split.
        var w = Ground(32, 32, (x, _) => x < 8 ? 3 : x < 20 ? 4 : 5);
        for (int z = 0; z < 32; z++)
        for (int x = 0; x < 8; x++)
            w.Set(x, 4, z, BlockType.Water);
        var fine = FineTerrain.Build(w);
        for (int fx = 16; fx < 40; fx++) Assert.Equal(9, Top(fine, fx, 30));
        Assert.Equal(10, Top(fine, 40, 30));
        // The water is as it was, and its bed is not stepped.
        for (int fx = 0; fx < 16; fx++)
        {
            Assert.Equal(BlockType.Water, fine.Get(fx, 9, 30));
            Assert.Equal(BlockType.Water, fine.Get(fx, 8, 30));
            Assert.Equal(BlockType.Grass, fine.Get(fx, 7, 30));
        }
    }

    [Fact]
    public void TheFinerWorldIsNeverHigherAndAtMostHalfABlockLower()
    {
        var map = RegionGenerator.Generate(1234, 128, 32, 128);
        var w = map.World;
        var fine = FineTerrain.Build(w);
        int lowered = 0;
        for (int y = 0; y < w.SizeY; y++)
        for (int z = 0; z < w.SizeZ; z++)
        for (int x = 0; x < w.SizeX; x++)
        {
            var b = w.Get(x, y, z);
            for (int k = 0; k < 4; k++)
            {
                int fx = 2 * x + (k & 1), fz = 2 * z + (k >> 1);
                Assert.Equal(b, fine.Get(fx, 2 * y, fz));
                var top = fine.Get(fx, 2 * y + 1, fz);
                if (top == b) continue;
                Assert.Equal(BlockType.Air, top);
                Assert.True(BlockInfo.IsGround(b));
                Assert.Equal(BlockType.Air, w.Get(x, y + 1, z));
                Assert.True(FineTerrain.IsLowered(fine, fx, y, fz));
                lowered++;
            }
        }
        // A good part of a rolling region's open ground is stepped.
        Assert.InRange(lowered, 128 * 128 * 4 / 20, 128 * 128 * 4 / 2);
    }

    [Fact]
    public void TheSameWorldGivesTheSameFinerWorld()
    {
        ulong Hash() => FineTerrain.Build(RegionGenerator.Generate(77, 96, 32, 96).World).ComputeHash();
        Assert.Equal(Hash(), Hash());
    }

    [Theory]
    [InlineData(BlockType.Ice)]
    [InlineData(BlockType.Lava)]
    public void GroundLevelWithIceOrLavaStaysWholeToo(BlockType pool)
    {
        // As with water: a frozen pool's bank does not drop when the water turns to ice.
        var w = Ground(32, 32, (x, _) => x < 8 ? 3 : x < 20 ? 4 : 5);
        for (int z = 0; z < 32; z++)
        for (int x = 0; x < 8; x++)
            w.Set(x, 4, z, pool);
        var fine = FineTerrain.Build(w);
        for (int fx = 16; fx < 40; fx++) Assert.Equal(9, Top(fine, fx, 30));
        Assert.Equal(10, Top(fine, 40, 30));
    }

    [Fact]
    public void ChangesToTheWorldCarryOver()
    {
        var w = Ground(32, 32, (x, _) => x < 16 ? 4 : 5);
        var fine = new FineWorld(w);
        w.BlockChanged += fine.Changed;
        Assert.Equal(FineTerrain.Build(w).ComputeHash(), fine.Blocks.ComputeHash());
        // Dug: all eight half-size blocks go and what is under it shows whole, and the ground round the new
        // pit gets a shoulder two blocks wide.
        Assert.Equal(9, Top(fine.Blocks, 14, 16));
        w.Set(8, 4, 8, BlockType.Air);
        Assert.Equal(7, Top(fine.Blocks, 16, 16));
        Assert.True(fine.Stale > 0);
        Assert.True(fine.Refresh() > 0);
        Assert.Equal(0, fine.Stale);
        Assert.Equal(7, Top(fine.Blocks, 16, 16));
        Assert.Equal(7, Top(fine.Blocks, 17, 17));
        Assert.Equal(8, Top(fine.Blocks, 15, 16));
        Assert.Equal(8, Top(fine.Blocks, 12, 16));
        Assert.Equal(9, Top(fine.Blocks, 11, 16));
        // Built on a lowered step: the step under it is filled in, so the wall does not stand on a gap.
        Assert.Equal(BlockType.Air, fine.Blocks.Get(32, 11, 20));
        w.Set(16, 6, 10, BlockType.Planks);
        fine.Refresh();
        for (int k = 0; k < 4; k++)
        {
            Assert.Equal(BlockType.Grass, fine.Blocks.Get(32 + (k & 1), 11, 20 + (k >> 1)));
            Assert.Equal(BlockType.Planks, fine.Blocks.Get(32 + (k & 1), 12, 20 + (k >> 1)));
            Assert.Equal(BlockType.Planks, fine.Blocks.Get(32 + (k & 1), 13, 20 + (k >> 1)));
        }
        // Its neighbour on the edge keeps its step.
        Assert.Equal(BlockType.Air, fine.Blocks.Get(32, 11, 24));
        // The wall comes down: the step is back.
        w.Set(16, 6, 10, BlockType.Air);
        fine.Refresh();
        Assert.Equal(BlockType.Air, fine.Blocks.Get(32, 11, 20));
        Assert.Equal(BlockType.Air, fine.Blocks.Get(32, 12, 20));
    }

    [Fact]
    public void CatchingUpALittleAtATimeEndsTheSame()
    {
        // A long trench dug at once, worked out a hundred columns a call: it takes several calls, and ends as
        // the finer world made afresh.
        var w = Ground(48, 48, (x, z) => 6 + (x + z) / 12);
        var fine = new FineWorld(w);
        w.BlockChanged += fine.Changed;
        for (int x = 4; x < 44; x++)
        for (int y = 5; y < 12; y++)
            w.Set(x, y, 24, BlockType.Air);
        int waiting = fine.Stale, calls = 0;
        Assert.True(waiting > 300);
        while (fine.Stale > 0)
        {
            fine.Refresh(most: 100);
            Assert.Equal(Math.Max(0, waiting - 100), fine.Stale);
            waiting = fine.Stale;
            calls++;
        }
        Assert.True(calls > 3);
        Assert.Equal(FineTerrain.Build(w).ComputeHash(), fine.Blocks.ComputeHash());
    }

    [Fact]
    public void AChangedWorldGivesTheFinerWorldItWouldGiveAfresh()
    {
        // Dig, pile up, build, flood, freeze and pave at random, catching up now and then: the finer world is
        // always the one the world as it stands would be given from scratch, whatever happened on the way.
        var w = RegionGenerator.Generate(1234, 96, 32, 96).World;
        var fine = new FineWorld(w);
        w.BlockChanged += fine.Changed;
        var rng = new DeterministicRandom(5);
        var steps = new List<(int X, int Y, int Z)>();
        int moved = 0;
        for (int round = 0; round < 8; round++)
        {
            for (int i = 0; i < 80; i++)
            {
                // Mostly near one spot, as a pit or a wall is: changes whose reach overlaps.
                int x = round % 2 == 0 ? rng.Range(0, 96) : 40 + rng.Range(0, 12);
                int z = round % 2 == 0 ? rng.Range(0, 96) : 40 + rng.Range(0, 12);
                int top = 31;
                while (top > 0 && w.Get(x, top, z) == BlockType.Air) top--;
                switch (rng.Range(0, 8))
                {
                    case 0: case 1: if (top > 1) w.Set(x, top, z, BlockType.Air); break;
                    case 2: if (top < 30) w.Set(x, top + 1, z, BlockType.Stone); break;
                    case 3: if (top < 30) w.Set(x, top + 1, z, BlockType.Planks); break;
                    case 4: if (top < 30) w.Set(x, top + 1, z, BlockType.Water); break;
                    case 5: w.Set(x, top, z, w.Get(x, top, z) == BlockType.Water ? BlockType.Ice : BlockType.Road); break;
                    case 6: if (top < 28) w.Set(x, top + 3, z, BlockType.Soil); break;
                    case 7: w.Set(x, rng.Range(1, 31), z, rng.Range(0, 2) == 0 ? BlockType.Air : BlockType.Grass); break;
                }
            }
            steps.Clear();
            int changed = fine.Refresh((x, y, z) => steps.Add((x, y, z)));
            Assert.Equal(changed, steps.Count);
            moved += changed;
            var fresh = FineTerrain.Build(w);
            for (int y = 0; y < fresh.SizeY; y++)
            for (int z = 0; z < fresh.SizeZ; z++)
            for (int x = 0; x < fresh.SizeX; x++)
                Assert.True(fresh.Get(x, y, z) == fine.Blocks.Get(x, y, z), $"round {round}: half-size block ({x}, {y}, {z})");
            // Every block it said was stepped is ground on top.
            foreach (var (x, y, z) in steps) Assert.True(BlockInfo.IsGround(w.Get(x, y, z)));
        }
        // The changes moved a good many steps. Nothing waits, and catching up again changes nothing.
        Assert.True(moved > 200, $"{moved} columns changed their steps");
        Assert.Equal(0, fine.Refresh());
    }

    [Fact]
    public void WholeBlocksGiveWayWhereTheGroundBesideThemIsLowered()
    {
        // The step runs along x = 16, the border between chunks 0 and 1: chunk 0, drawn in whole blocks, must
        // see the lowered edge of chunk 1 as open so that it draws nothing short against it.
        var w = Ground(32, 32, (x, _) => x < 16 ? 4 : 5);
        var fine = FineTerrain.Build(w);
        const int p = VoxelWorld.PaddedSize;
        var padded = new BlockType[p * p * p];
        var open = new BlockType[p * p * p];
        w.CopyChunkPadded(0, 0, 0, padded);
        int edge = GreedyMesher.Index(16, 5, 8); // the lowered block of chunk 1, in chunk 0's border
        Assert.Equal(BlockType.Grass, padded[edge]);
        FineTerrain.Open(fine, padded, 0, 0, 0, open, borderOnly: true);
        Assert.Equal(BlockType.Air, open[edge]);
        // Only that row of blocks (those of it in the world) changed.
        Assert.Equal(17, Enumerable.Range(0, padded.Length).Count(i => padded[i] != open[i]));

        // For chunk 1 itself only the border changes with borderOnly, and every lowered block without it.
        w.CopyChunkPadded(1, 0, 0, padded);
        FineTerrain.Open(fine, padded, 1, 0, 0, open, borderOnly: true);
        Assert.Equal(BlockType.Grass, open[GreedyMesher.Index(0, 5, 8)]);
        FineTerrain.Open(fine, padded, 1, 0, 0, open, borderOnly: false);
        Assert.Equal(BlockType.Air, open[GreedyMesher.Index(0, 5, 8)]);
        Assert.Equal(BlockType.Air, open[GreedyMesher.Index(1, 5, 8)]);
        Assert.Equal(BlockType.Grass, open[GreedyMesher.Index(2, 5, 8)]);
    }

    [Fact]
    public void ABoxIsCopiedAsBlockByBlock()
    {
        var w = new VoxelWorld(50, 40, 50);
        var rng = new DeterministicRandom(3);
        for (int i = 0; i < 20000; i++)
            w.Set(rng.Range(0, 50), rng.Range(0, 40), rng.Range(0, 50), (BlockType)rng.Range(1, 12));
        const int size = 32, p = size + 2;
        var box = new BlockType[p * p * p];
        foreach (var (x0, y0, z0) in new[] { (0, 0, 0), (32, 32, 32), (16, 0, 32) })
        {
            Array.Fill(box, BlockType.Bedrock);
            w.CopyBoxPadded(x0, y0, z0, size, box);
            for (int y = -1; y <= size; y++)
            for (int z = -1; z <= size; z++)
            for (int x = -1; x <= size; x++)
                Assert.Equal(w.Get(x0 + x, y0 + y, z0 + z), box[GreedyMesher.Index(x, y, z, size)]);
        }
    }

    [Fact]
    public void TheMesherWorksOnCubesOfAnySize()
    {
        // A chunk of half-size blocks is 32 a side: flat ground in it is still one quad.
        const int size = 32, p = size + 2;
        var w = new VoxelWorld(64, 64, 64);
        for (int z = 0; z < 64; z++)
        for (int x = 0; x < 64; x++)
            w.Set(x, 5, z, BlockType.Grass);
        var box = new BlockType[p * p * p];
        w.CopyBoxPadded(16, 0, 16, size, box);
        var opaque = new List<Quad>();
        var water = new List<Quad>();
        GreedyMesher.Build(box, size, opaque, water, ao: true);
        var top = Assert.Single(opaque, q => q.Face == 2);
        Assert.Equal((0, 6, 0, 32, 32), (top.X, top.Y, top.Z, top.W, top.H));
        Assert.Equal(2, opaque.Count); // and its underside; the sides are hidden by the ground beyond

        // At a chunk's own size it is the mesher as before.
        var padded = new BlockType[VoxelWorld.PaddedSize * VoxelWorld.PaddedSize * VoxelWorld.PaddedSize];
        var map = RegionGenerator.Generate(5, 48, 32, 48);
        map.World.CopyChunkPadded(1, 0, 1, padded);
        var a = new List<Quad>();
        var b = new List<Quad>();
        GreedyMesher.Build(padded, a, water, ao: true);
        GreedyMesher.Build(padded, VoxelWorld.ChunkSize, b, water, ao: true);
        Assert.NotEmpty(a);
        Assert.Equal(a, b);
    }

    [Fact]
    public void HalfSizeBlocksMeshTheSameGroundWithHalfSteps()
    {
        // The area of ground seen from above is the same in whole blocks and in half-size ones (four to one).
        var w = Ground(32, 32, (x, z) => 4 + (x + z) / 10);
        var fine = FineTerrain.Build(w);
        var padded = new BlockType[VoxelWorld.PaddedSize * VoxelWorld.PaddedSize * VoxelWorld.PaddedSize];
        var box = new BlockType[34 * 34 * 34];
        w.CopyChunkPadded(0, 0, 0, padded);
        fine.CopyBoxPadded(0, 0, 0, 32, box);
        var whole = new List<Quad>();
        var half = new List<Quad>();
        var water = new List<Quad>();
        GreedyMesher.Build(padded, whole, water);
        GreedyMesher.Build(box, 32, half, water);
        long Tops(List<Quad> quads) => quads.Where(q => q.Face == 2).Sum(q => (long)q.W * q.H);
        Assert.Equal(16 * 16, Tops(whole));
        Assert.Equal(32 * 32, Tops(half));
        // Twice as many levels of ground.
        Assert.True(half.Where(q => q.Face == 2).Select(q => q.Y).Distinct().Count()
                    > whole.Where(q => q.Face == 2).Select(q => q.Y).Distinct().Count());
    }

    [Fact]
    public void FineVoxelsMergeIntoBiggerOnes()
    {
        // A 16-voxel cube of stone (plus the one-voxel border): at step 4 it is a cube of 4 voxels a side.
        const int n = 18;
        var grid = new byte[n * n * n];
        for (int y = 1; y <= 16; y++)
        for (int z = 1; z <= 16; z++)
        for (int x = 1; x <= 16; x++)
            grid[(y * n + z) * n + x] = (x + y + z) % 5 == 0 ? StructurePalette.StoneB : StructurePalette.StoneA;
        var small = new byte[6 * 6 * 6];
        StructureMesher.Shrink(grid, n, n, n, small, 4);
        for (int y = 0; y < 6; y++)
        for (int z = 0; z < 6; z++)
        for (int x = 0; x < 6; x++)
        {
            bool inside = x is >= 1 and <= 4 && y is >= 1 and <= 4 && z is >= 1 and <= 4;
            // The commonest colour wins; the border is empty because the border of the grid is.
            Assert.Equal(inside ? StructurePalette.StoneA : StructurePalette.Empty, small[(y * 6 + z) * 6 + x]);
        }
    }

    [Fact]
    public void AMergedVoxelIsDrawnOnlyIfEnoughOfItIs()
    {
        // One block (8 voxels a side, plus border) at step 4: a post 4 voxels thick through the middle fills
        // a quarter of each merged voxel and stays (a far tree keeps its trunk); a post 2 thick does not.
        const int n = 10;
        byte[] Post(int from, int to)
        {
            var grid = new byte[n * n * n];
            for (int y = 1; y <= 8; y++)
            for (int z = from; z <= to; z++)
            for (int x = from; x <= to; x++)
                grid[(y * n + z) * n + x] = StructurePalette.BarkA;
            var small = new byte[4 * 4 * 4];
            StructureMesher.Shrink(grid, n, n, n, small, 4);
            return small;
        }
        Assert.Equal(8, Post(3, 6).Count(c => c == StructurePalette.BarkA));
        Assert.Equal(0, Post(4, 5).Count(c => c != StructurePalette.Empty));
    }

    [Fact]
    public void TheBorderOfMergedVoxelsHidesOnlyWhereAllOfItDoes()
    {
        const int n = 10;
        var grid = new byte[n * n * n];
        // Ground under the whole block, and a wall along half of one side.
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
            grid[z * n + x] = StructurePalette.Occluder;
        for (int y = 1; y <= 8; y++)
        for (int z = 1; z <= 4; z++)
            grid[(y * n + z) * n] = StructurePalette.Occluder;
        var small = new byte[4 * 4 * 4];
        StructureMesher.Shrink(grid, n, n, n, small, 4);
        Assert.Equal(StructurePalette.Occluder, small[(0 * 4 + 1) * 4 + 1]);
        Assert.Equal(StructurePalette.Occluder, small[(1 * 4 + 1) * 4 + 0]);
        Assert.Equal(StructurePalette.Empty, small[(1 * 4 + 2) * 4 + 0]);
    }

    [Fact]
    public void AFarTreeIsTheSameTreeInFewerBiggerVoxels()
    {
        var w = Ground(16, 16, (_, _) => 2);
        Assert.True(Trees.Grow(w, 8, 2, 8, 5, 0, TreeKind.Broadleaf, 1));
        var padded = new BlockType[VoxelWorld.PaddedSize * VoxelWorld.PaddedSize * VoxelWorld.PaddedSize];
        w.CopyChunkPadded(0, 0, 0, padded);
        var near = new List<FineQuad>();
        var far = new List<FineQuad>();
        StructureMesher.Build(padded, 0, 0, 0, near, ao: true, step: 1);
        StructureMesher.Build(padded, 0, 0, 0, far, ao: true, step: 4);
        Assert.NotEmpty(far);
        Assert.True(far.Count * 2 < near.Count, $"{far.Count} far quads against {near.Count} near");
        // On the bigger voxels' grid, within the tree's blocks, and the trunk still stands on the ground.
        const int f = StructureDetail.Size;
        foreach (var q in far)
        for (int k = 0; k < 4; k++)
        {
            var (x, y, z) = q.Corner(k);
            Assert.True(x % 4 == 0 && y % 4 == 0 && z % 4 == 0);
            Assert.InRange(x, 6 * f, 11 * f);
            Assert.InRange(z, 6 * f, 11 * f);
            Assert.InRange(y, 3 * f, 11 * f);
        }
        Assert.Contains(far, q => q.Face is 0 or 1 && q.Y == 3 * f && StructurePalette.IsLeaf(q.Colour) == false);
        // Step 1 is the mesher as it was.
        var plain = new List<FineQuad>();
        StructureMesher.Build(padded, 0, 0, 0, plain, ao: true);
        Assert.Equal(plain, near);
    }
}
