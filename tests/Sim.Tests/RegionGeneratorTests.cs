using System;
using System.IO;
using System.Linq;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

public class RegionGeneratorTests
{
    // Full-size regions are shared across tests; generating one takes a fraction of a second.
    private static readonly ulong[] Seeds = { 1, 7, 1234 };
    private static readonly Lazy<RegionMap>[] Maps =
        Seeds.Select(s => new Lazy<RegionMap>(() => RegionGenerator.Generate(s, 512, 64, 512))).ToArray();
    /// <summary>The same regions with the first, smaller trees (--trees=1), whose shapes the tree tests check.</summary>
    private static readonly Lazy<RegionMap>[] FirstTreeMaps =
        Seeds.Select(s => new Lazy<RegionMap>(() => RegionGenerator.Generate(s, 512, 64, 512, treeScale: 1))).ToArray();

    public static TheoryData<int> MapIndexes => new() { 0, 1, 2 };

    [Theory, MemberData(nameof(MapIndexes))]
    public void EveryBiomeIsPresent(int m)
    {
        var map = Maps[m].Value;
        var counts = new int[Enum.GetValues<Biome>().Length];
        for (int z = 0; z < 512; z++)
        for (int x = 0; x < 512; x++)
            counts[(int)map.BiomeAt(x, z)]++;
        foreach (var b in Enum.GetValues<Biome>())
            Assert.True(counts[(int)b] > 1000, $"seed {map.Seed}: only {counts[(int)b]} {b} columns");
    }

    [Theory, MemberData(nameof(MapIndexes))]
    public void CavernHasOreAndCrystalOnItsWalls(int m)
    {
        var map = Maps[m].Value;
        Assert.NotNull(map.Cavern);
        var (cx, cy, cz) = map.Cavern!.Value;
        int iron = 0, crystal = 0, caveAir = 0;
        var w = map.World;
        for (int y = 1; y < cy + 12; y++)
        for (int z = cz - 60; z <= cz + 60; z++)
        for (int x = cx - 60; x <= cx + 60; x++)
        {
            if (!w.InBounds(x, y, z) || y >= w.SurfaceHeight(x, z) - 2) continue;
            var b = w.Get(x, y, z);
            if (b == BlockType.Air) { caveAir++; continue; }
            if (!Exposed(w, x, y, z)) continue;
            if (b == BlockType.IronOre) iron++;
            if (b == BlockType.Crystal) crystal++;
        }
        Assert.True(caveAir > 500, $"cavern is only {caveAir} blocks");
        Assert.True(iron > 20, $"only {iron} iron on cave walls");
        Assert.True(crystal > 10, $"only {crystal} crystal on cave walls");
    }

    [Theory, MemberData(nameof(MapIndexes))]
    public void BothStartsHaveFoodWoodStoneAndIronNearby(int m)
    {
        var map = Maps[m].Value;
        Assert.Equal(2, map.Starts.Count);
        Assert.True(map.Starts[0].X < 256 && map.Starts[1].X > 256, "starts should be on opposite (west and east) coasts");
        foreach (var start in map.Starts)
        {
            Assert.Equal(BlockType.Grass, map.World.Get(start.X, start.Y, start.Z));
            Assert.Equal(BlockType.Air, map.World.Get(start.X, start.Y + 1, start.Z));
            foreach (var need in new[] { BlockType.BerryBush, BlockType.Wood, BlockType.Stone, BlockType.IronOre })
            {
                int n = CountExposed(map.World, start.X, start.Z, RegionGenerator.StartResourceRadius, need);
                Assert.True(n > 0, $"seed {map.Seed}, player {start.Player}: no reachable {need} within {RegionGenerator.StartResourceRadius} blocks");
            }
        }
    }

    [Theory, MemberData(nameof(MapIndexes))]
    public void StartsAreRoughlyBalanced(int m)
    {
        var map = Maps[m].Value;
        foreach (var need in new[] { BlockType.BerryBush, BlockType.Wood, BlockType.Stone, BlockType.IronOre })
        {
            int a = CountExposed(map.World, map.Starts[0].X, map.Starts[0].Z, RegionGenerator.StartResourceRadius, need);
            int b = CountExposed(map.World, map.Starts[1].X, map.Starts[1].Z, RegionGenerator.StartResourceRadius, need);
            Assert.True(Math.Min(a, b) * 3 >= Math.Max(a, b), $"seed {map.Seed}: {need} is {a} vs {b}");
        }
    }

    [Theory, MemberData(nameof(MapIndexes))]
    public void StartsAreConnectedOverLandThroughTheFords(int m)
    {
        // Fresh copy: the simulation takes ownership of its map.
        var sim = new Simulation(Seeds[m], 512, 64, 512);
        var s0 = sim.Map.Starts[0];
        var s1 = sim.Map.Starts[1];
        int id = sim.SpawnStartingUnits(1)[0];
        int i = sim.Units.IndexOf(id);
        Assert.Equal(s0.Player, sim.Units.Owner[i]);
        sim.Schedule(0, new MoveCommand(s0.Player, new[] { id }, s1.X, s1.Z));
        var field = default(Rts.Sim.Units.FlowField);
        for (int t = 0; t < 100 && field is not { Ready: true }; t++)
        {
            sim.Step();
            field = sim.Paths.Get(sim.Units.Field[i]);
        }
        Assert.NotNull(field);
        int node = sim.Walkable.Node(sim.Units.X[i].FloorToInt(), sim.Units.Z[i].FloorToInt(), sim.Units.Y[i]);
        int dist = field!.Distance(node);
        Assert.True(dist != Rts.Sim.Units.FlowField.Unknown, $"seed {Seeds[m]}: no way from start 0 to start 1");
        // A real route: at least the straight line, and not an absurd detour.
        int straight = (s1.X - s0.X) * 10;
        Assert.InRange(dist, straight * 9 / 10, straight * 2);
    }

    [Theory, MemberData(nameof(MapIndexes))]
    public void TreesVaryALittleButStayAboutTheSameSize(int m)
    {
        var map = FirstTreeMaps[m].Value;
        var w = map.World;
        var heights = new int[12];
        var kinds = new int[6];
        var smallest = new int[6];
        var largest = new int[6];
        Array.Fill(smallest, int.MaxValue);
        int trees = 0;
        for (int z = 3; z < 509; z++)
        for (int x = 3; x < 509; x++)
        {
            // A trunk's base: a trunk standing on the ground.
            int y = 1;
            while (y < 63 && !(BlockInfo.IsTrunk(w.Get(x, y, z)) && !BlockInfo.IsTrunk(w.Get(x, y - 1, z)))) y++;
            if (y >= 63) continue;
            int top = y;
            while (BlockInfo.IsTrunk(w.Get(x, top + 1, z))) top++;
            int trunk = top - y + 1;
            // Its crown: the leaves round its top, within two columns of the trunk.
            var leafCount = new int[(int)BlockInfo.Last + 1];
            int leaves = 0;
            for (int ly = top - 3; ly <= top + 3; ly++)
            for (int lz = z - 2; lz <= z + 2; lz++)
            for (int lx = x - 2; lx <= x + 2; lx++)
                if (BlockInfo.IsLeaves(w.Get(lx, ly, lz))) { leafCount[(int)w.Get(lx, ly, lz)]++; leaves++; }
            var leaf = (BlockType)Array.IndexOf(leafCount, leafCount.Max());
            var kind = KindOf(w.Get(x, y, z), leaf);
            int k = (int)kind;
            Assert.InRange(trunk, MinTrunk(kind), MinTrunk(kind) + 2);
            heights[trunk]++;
            trees++;
            // Crowns are compared only for trees standing on their own (a neighbour's crown reaches in).
            if (++kinds[k] <= 150 && Alone(w, x, y, z))
            {
                smallest[k] = Math.Min(smallest[k], leaves);
                largest[k] = Math.Max(largest[k], leaves);
            }
        }
        string Report() => $"seed {map.Seed}: trunks {string.Join(",", heights)}, kinds {string.Join(",", kinds)}, " +
            $"crowns {string.Join(",", smallest)} to {string.Join(",", largest)}";
        Assert.True(trees > 500, Report());
        // Every kind grows somewhere, broad-leaved trees most.
        foreach (int n in kinds) Assert.True(n > 0, Report());
        Assert.True(kinds[(int)TreeKind.Broadleaf] == kinds.Max(), Report());
        // Mostly 4 to 6 blocks of trunk, as before: pines stand taller, and only a few.
        Assert.True(heights[4] + heights[5] + heights[6] > trees * 2 / 3, Report());
        // Crowns differ from tree to tree, but none of a kind is far bigger than another.
        for (int k = 0; k < kinds.Length; k++)
        {
            if (kinds[k] < 5) continue;
            Assert.True(largest[k] > smallest[k], Report());
            Assert.True(largest[k] <= 2 * smallest[k], Report());
        }
    }

    /// <summary>True if no other trunk stands within four columns of the one at (x, y, z).</summary>
    private static bool Alone(VoxelWorld w, int x, int y, int z)
    {
        for (int dz = -4; dz <= 4; dz++)
        for (int dx = -4; dx <= 4; dx++)
        for (int dy = -3; dy <= 3; dy++)
            if ((dx != 0 || dz != 0) && BlockInfo.IsTrunk(w.Get(x + dx, y + dy, z + dz))) return false;
        return true;
    }

    /// <summary>The kind of tree with this trunk and (mostly) these leaves.</summary>
    private static TreeKind KindOf(BlockType trunk, BlockType leaves) => trunk switch
    {
        BlockType.PineWood => leaves == BlockType.GloomLeaves ? TreeKind.Gloom : TreeKind.Pine,
        BlockType.BirchWood => TreeKind.Birch,
        BlockType.MushroomStem => TreeKind.Mushroom,
        _ => leaves switch
        {
            BlockType.WillowLeaves => TreeKind.Willow,
            _ => TreeKind.Broadleaf,
        },
    };

    private static int MinTrunk(TreeKind kind) => kind switch
    {
        TreeKind.Pine => 6,
        TreeKind.Gloom => 8,
        TreeKind.Willow => 5,
        TreeKind.Mushroom => 3,
        _ => 4,
    };

    [Theory]
    [InlineData(Biome.Forest)]
    [InlineData(Biome.Grassland)]
    [InlineData(Biome.Hills)]
    [InlineData(Biome.Swamp)]
    public void EachBiomeGrowsItsOwnTrees(Biome biome)
    {
        var count = new int[5];
        for (int roll = 0; roll < 100; roll++) count[(int)RegionGenerator.KindFor(biome, roll)]++;
        switch (biome)
        {
            case Biome.Forest:
                Assert.True(count[(int)TreeKind.Broadleaf] > 0 && count[(int)TreeKind.Pine] > 0 && count[(int)TreeKind.Birch] > 0);
                Assert.Equal(0, count[(int)TreeKind.Willow] + count[(int)TreeKind.Mushroom]);
                break;
            case Biome.Grassland:
                Assert.Equal(100, count[(int)TreeKind.Broadleaf] + count[(int)TreeKind.Birch]);
                break;
            case Biome.Hills:
                // Pines (the one dusk tree is placed on its own).
                Assert.Equal(100, count[(int)TreeKind.Pine]);
                break;
            case Biome.Swamp:
                Assert.True(count[(int)TreeKind.Willow] > 0 && count[(int)TreeKind.Mushroom] > 0);
                Assert.Equal(0, count[(int)TreeKind.Pine] + count[(int)TreeKind.Birch]);
                break;
        }
    }

    // --- Bigger maps get more features (issue #67) ----------------------------------------------

    private static readonly Lazy<RegionMap> Default768 = new(() =>
        RegionGenerator.Generate(1234, RegionGenerator.DefaultSize, RegionGenerator.DefaultHeight, RegionGenerator.DefaultSize));

    /// <summary>
    /// Separate patches of a biome at least minSize columns big (4-connected; the river, which cuts through
    /// everything, joins what it splits, though its own columns are not counted).
    /// </summary>
    private static int Patches(RegionMap map, Biome biome, int minSize)
    {
        int sx = map.World.SizeX, sz = map.World.SizeZ, patches = 0;
        var seen = new bool[sx * sz];
        var stack = new System.Collections.Generic.Stack<(int, int)>();
        for (int z0 = 0; z0 < sz; z0++)
        for (int x0 = 0; x0 < sx; x0++)
        {
            if (seen[z0 * sx + x0] || map.BiomeAt(x0, z0) != biome) continue;
            bool Part(int x, int z) => map.BiomeAt(x, z) == biome || map.BiomeAt(x, z) == Biome.River;
            int size = 0;
            seen[z0 * sx + x0] = true;
            stack.Push((x0, z0));
            while (stack.Count > 0)
            {
                var (x, z) = stack.Pop();
                if (map.BiomeAt(x, z) == biome) size++;
                foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int nx = x + dx, nz = z + dz;
                    if (nx < 0 || nz < 0 || nx >= sx || nz >= sz || seen[nz * sx + nx] || !Part(nx, nz)) continue;
                    seen[nz * sx + nx] = true;
                    stack.Push((nx, nz));
                }
            }
            if (size >= minSize) patches++;
        }
        return patches;
    }

    /// <summary>Separate pools of lava on the surface.</summary>
    private static int LavaPools(RegionMap map)
    {
        var w = map.World;
        var tops = new System.Collections.Generic.List<(int X, int Z)>();
        for (int z = 0; z < w.SizeZ; z++)
        for (int x = 0; x < w.SizeX; x++)
            if (w.Get(x, w.SurfaceHeight(x, z), z) == BlockType.Lava) tops.Add((x, z));
        int pools = 0;
        var left = new System.Collections.Generic.List<(int X, int Z)>(tops);
        while (left.Count > 0)
        {
            var (px, pz) = left[0];
            left.RemoveAll(t => Math.Abs(t.X - px) <= 12 && Math.Abs(t.Z - pz) <= 12);
            pools++;
        }
        return pools;
    }

    /// <summary>
    /// The default 768 map has more distinct places than the 512 one, not just bigger ones: two hill ranges, two
    /// swamps, a cavern under each hill range, more lava pools and more ruins. The 512 map keeps one of each.
    /// </summary>
    [Fact]
    public void BiggerMapsGetMoreFeatures()
    {
        Assert.Equal(1, RegionGenerator.FeatureSets(512, 512));
        Assert.Equal(2, RegionGenerator.FeatureSets(768, 768));
        Assert.Equal(RegionGenerator.MaxFeatureSets, RegionGenerator.FeatureSets(4096, 4096));
        var small = Maps[2].Value; // seed 1234 on 512
        var big = Default768.Value;
        Assert.Equal(1, Patches(small, Biome.Hills, 2000));
        Assert.Equal(1, Patches(small, Biome.Swamp, 2000));
        Assert.Equal(2, Patches(big, Biome.Hills, 2000));
        Assert.Equal(2, Patches(big, Biome.Swamp, 2000));
        Assert.Single(small.Caverns);
        Assert.Equal(2, big.Caverns.Count);
        Assert.Equal(big.Cavern, big.Caverns[0]);
        foreach (var (cx, cy, cz) in big.Caverns)
        {
            Assert.Equal(Biome.Hills, big.BiomeAt(cx, cz));
            Assert.True(big.World.SurfaceHeight(cx, cz) > cy + 3, $"cavern at {cx},{cz} is not underground");
        }
        Assert.True(LavaPools(big) > LavaPools(small), $"lava pools: {LavaPools(big)} on 768, {LavaPools(small)} on 512");
        Assert.Equal(RegionGenerator.RuinsPer512, small.Ruins.Count);
        Assert.True(big.Ruins.Count > small.Ruins.Count, $"ruins: {big.Ruins.Count} on 768, {small.Ruins.Count} on 512");
        // Neither start is crowded by the new features.
        foreach (var s in big.Starts)
        {
            Assert.Equal(Biome.Grassland, big.BiomeAt(s.X, s.Z));
            foreach (var (rx, _, rz) in big.Ruins)
                Assert.True(Math.Abs(rx - s.X) + Math.Abs(rz - s.Z) > RegionGenerator.StartResourceRadius, $"ruin at {rx},{rz} by a start");
        }
    }

    /// <summary>
    /// A ruin is a broken square of stone brick walls, with a doorway and a corner standing four high, on a cracked
    /// floor, with fallen stone (rubble cleared for stone) lying in and around it.
    /// </summary>
    [Theory, MemberData(nameof(MapIndexes))]
    public void RuinsAreBrokenStoneWallsWithFallenStone(int m)
    {
        var map = Maps[m].Value;
        Assert.NotEmpty(map.Ruins);
        var w = map.World;
        int r = RegionGenerator.RuinHalf;
        foreach (var (cx, g, cz) in map.Ruins)
        {
            int wall = 0, gaps = 0, tallest = 0, rubble = 0, floor = 0;
            for (int dz = -r - 1; dz <= r + 1; dz++)
            for (int dx = -r - 1; dx <= r + 1; dx++)
            {
                int x = cx + dx, z = cz + dz, ring = Math.Max(Math.Abs(dx), Math.Abs(dz));
                if (w.Get(x, g + 1, z) == BlockType.StoneRubble) rubble++;
                if (ring < r && w.Get(x, g, z) == BlockType.StoneBrick) floor++;
                if (ring != r) continue;
                int high = 0;
                while (w.Get(x, g + high + 1, z) == BlockType.StoneBrick) high++;
                if (high == 0) gaps++; else wall++;
                tallest = Math.Max(tallest, high);
            }
            Assert.True(wall > 20, $"ruin at {cx},{cz}: only {wall} wall columns");
            Assert.True(gaps >= 3, $"ruin at {cx},{cz}: no way in");
            Assert.Equal(4, tallest);
            Assert.True(rubble > 0 && floor > 10, $"ruin at {cx},{cz}: {rubble} rubble, {floor} floor");
        }
    }

    [Fact]
    public void SameSeedGivesSameMapAndDifferentSeedsDiffer()
    {
        var a = RegionGenerator.Generate(42, 128, 48, 128);
        var b = RegionGenerator.Generate(42, 128, 48, 128);
        var c = RegionGenerator.Generate(43, 128, 48, 128);
        Assert.Equal(a.ComputeHash(), b.ComputeHash());
        Assert.NotEqual(a.ComputeHash(), c.ComputeHash());
    }

    [Fact]
    public void SaveAndLoadRoundTrips()
    {
        var map = RegionGenerator.Generate(9, 96, 32, 80);
        var ms = new MemoryStream();
        map.Save(ms);
        ms.Position = 0;
        var loaded = RegionMap.Load(ms);
        Assert.Equal(map.ComputeHash(), loaded.ComputeHash());
        Assert.Equal(map.Starts, loaded.Starts);
        Assert.Equal(map.Cavern, loaded.Cavern);
        Assert.Equal(map.Caverns, loaded.Caverns);
        Assert.Equal(map.Ruins, loaded.Ruins);
        Assert.Equal(map.Seed, loaded.Seed);
        Assert.Equal(new Simulation(map).StateHash(), new Simulation(loaded).StateHash());
    }

    [Fact]
    public void LoadRejectsGarbage()
    {
        Assert.Throws<InvalidDataException>(() => RegionMap.Load(new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 })));
    }

    [Fact]
    public void SmallWorldsStillGenerate()
    {
        var map = RegionGenerator.Generate(3, 32, 32, 32);
        Assert.Equal(2, map.Starts.Count);
        Assert.NotEqual(-1, map.World.SurfaceHeight(16, 16));
    }

    [Fact]
    public void IntegerSqrtIsExact()
    {
        for (long v = 0; v < 5000; v++)
        {
            long r = Noise.Sqrt(v);
            Assert.True(r * r <= v && (r + 1) * (r + 1) > v, $"sqrt({v}) = {r}");
        }
        Assert.Equal(1 << 20, Noise.Sqrt(1L << 40));
    }

    private static bool Exposed(VoxelWorld w, int x, int y, int z) =>
        w.Get(x + 1, y, z) == BlockType.Air || w.Get(x - 1, y, z) == BlockType.Air
        || w.Get(x, y + 1, z) == BlockType.Air || w.Get(x, y - 1, z) == BlockType.Air
        || w.Get(x, y, z + 1) == BlockType.Air || w.Get(x, y, z - 1) == BlockType.Air;

    /// <summary>Blocks of a type that touch air (so a worker can reach them) within a horizontal radius.</summary>
    private static int CountExposed(VoxelWorld w, int cx, int cz, int radius, BlockType type)
    {
        int n = 0;
        for (int z = cz - radius; z <= cz + radius; z++)
        for (int x = cx - radius; x <= cx + radius; x++)
        {
            if ((x - cx) * (x - cx) + (z - cz) * (z - cz) > radius * radius) continue;
            for (int y = 1; y < w.SizeY; y++)
                if (w.Get(x, y, z) == type && Exposed(w, x, y, z)) n++;
        }
        return n;
    }
}
