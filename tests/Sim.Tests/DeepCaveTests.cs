using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// The deeper map and its deep caves: 64 layers of rock under the land, with two levels of chambers and tunnels
/// running under most of the map, joined to the hill caverns and to the surface, and furnished.
/// </summary>
public class DeepCaveTests
{
    private readonly ITestOutputHelper _out;
    public DeepCaveTests(ITestOutputHelper output) => _out = output;

    private static readonly Lazy<RegionMap> DefaultMap = new(() =>
        RegionGenerator.Generate(1234, RegionGenerator.DefaultSize, RegionGenerator.DefaultHeight, RegionGenerator.DefaultSize));

    [Fact]
    public void TheDefaultMapHasDeepRockUnderTheLand()
    {
        Assert.Equal(128, RegionGenerator.DefaultHeight);
        Assert.Equal(64, RegionGenerator.DeepLayers(RegionGenerator.DefaultHeight));
        Assert.Equal(64 + 14, RegionGenerator.WaterLevel(RegionGenerator.DefaultHeight));
        // A 64-high map is laid out as it always was.
        Assert.Equal(0, RegionGenerator.DeepLayers(64));
        Assert.Equal(14, RegionGenerator.WaterLevel(64));
        Assert.Empty(RegionGenerator.Generate(7, 256, 64, 256).Deeps);

        var map = DefaultMap.Value;
        var w = map.World;
        // The starts stand at the land's height above the deep rock, as high over the water as before.
        foreach (var s in map.Starts) Assert.InRange(s.Y, 64 + 16, 64 + 20);
        // Under the land, mostly rock.
        int solid = 0, all = 0;
        for (int z = 16; z < w.SizeZ; z += 32)
        for (int x = 16; x < w.SizeX; x += 32)
        for (int y = 2; y < 64; y++, all++)
            if (w.Get(x, y, z) != BlockType.Air) solid++;
        Assert.InRange(solid * 100 / all, 80, 99);
    }

    /// <summary>The deep chambers lie on a grid across most of the map, on two levels, under a roof of rock.</summary>
    [Fact]
    public void TheDeepCavesRunUnderMostOfTheMap()
    {
        var map = DefaultMap.Value;
        var w = map.World;
        int deep = RegionGenerator.DeepLayers(w.SizeY);
        Assert.InRange(map.Deeps.Count, 40, 100);
        Assert.True(map.Deeps.Max(d => d.X) - map.Deeps.Min(d => d.X) >= w.SizeX * 3 / 4);
        Assert.True(map.Deeps.Max(d => d.Z) - map.Deeps.Min(d => d.Z) >= w.SizeZ * 3 / 4);
        Assert.All(map.Deeps, d => Assert.InRange(d.Y, 4, deep));
        Assert.Contains(map.Deeps, d => d.Y < deep / 2);
        Assert.Contains(map.Deeps, d => d.Y > deep / 2);
        // Open cave in most 64 by 64 squares of land.
        int squares = 0, withCave = 0;
        for (int sz = 0; sz < w.SizeZ; sz += 64)
        for (int sx = 0; sx < w.SizeX; sx += 64)
        {
            if (map.BiomeAt(sx + 32, sz + 32) == Biome.Ocean) continue;
            squares++;
            bool cave = false;
            for (int z = sz; z < sz + 64 && !cave; z += 2)
            for (int x = sx; x < sx + 64 && !cave; x += 2)
            for (int y = 4; y < deep && !cave; y++)
                cave = w.Get(x, y, z) == BlockType.Air;
            if (cave) withCave++;
        }
        _out.WriteLine($"{withCave} of {squares} squares of land have deep cave under them");
        Assert.True(withCave * 10 >= squares * 7, $"{withCave} of {squares}");
    }

    /// <summary>
    /// On foot: from the main hill cavern's first chamber, nearly every deep chamber can be walked to, on both
    /// levels; and the deep caves have ways out to the surface of their own.
    /// </summary>
    [Fact]
    public void TheDeepCavesJoinUpWithTheCavernsAndTheSurface()
    {
        var map = DefaultMap.Value;
        var w = map.World;
        long Key(int x, int y, int z) => ((long)x * w.SizeY + y) * w.SizeZ + z;
        var cave = map.Cavern!.Value;
        var (seen, surfaced) = WalkFrom(w, cave.X, cave.Y, cave.Z);
        _out.WriteLine($"{seen.Count} cells walked, {surfaced} out in the open");
        int reached = 0;
        foreach (var d in map.Deeps)
        {
            bool got = false;
            for (int y = d.Y - 12; y <= d.Y + 2 && !got; y++)
            for (int dz = -9; dz <= 9 && !got; dz++)
            for (int dx = -9; dx <= 9 && !got; dx++)
                got = seen.Contains(Key(d.X + dx, y, d.Z + dz));
            if (got) reached++;
            else _out.WriteLine($"not reached: {d}");
        }
        _out.WriteLine($"{reached} of {map.Deeps.Count} deep chambers reached from the cavern");
        Assert.True(reached * 10 >= map.Deeps.Count * 9, $"{reached} of {map.Deeps.Count}");
        Assert.True(surfaced > 0);
        // And back: from the lower level's last nest chamber (one with no pool in its middle), out into the open.
        var last = map.Deeps[(map.Deeps.Count - 1) / RegionGenerator.DeepNestEvery * RegionGenerator.DeepNestEvery];
        Assert.True(last.Y < RegionGenerator.DeepLayers(w.SizeY) / 2);
        var (_, back) = WalkFrom(w, last.X, last.Y, last.Z);
        Assert.True(back > 0, "no way up from the lower level");
    }

    /// <summary>Every cell walked to from (x, y, z) underground, and how many of them are out in the open (walking stops there).</summary>
    private static (HashSet<long> Seen, int Surfaced) WalkFrom(VoxelWorld w, int sx, int y0, int sz)
    {
        int deep = RegionGenerator.DeepLayers(w.SizeY);
        long Key(int x, int y, int z) => ((long)x * w.SizeY + y) * w.SizeZ + z;
        int sy = Walk.NearestStandLevel(w, sx, sz, y0, 8);
        Assert.True(sy >= 0);
        var seen = new HashSet<long> { Key(sx, sy, sz) };
        var open = new Queue<(int X, int Y, int Z)>();
        open.Enqueue((sx, sy, sz));
        int surfaced = 0;
        while (open.Count > 0)
        {
            var (x, y, z) = open.Dequeue();
            if (y >= deep + 6 && Walk.GroundLevel(w, x, z) <= y) { surfaced++; continue; } // out in the open: stop here
            foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = x + dx, nz = z + dz;
                if (!w.InBounds(nx, 0, nz) || !Walk.TryStep(w, x, z, y, nx, nz, out int ny)) continue;
                if (seen.Add(Key(nx, ny, nz))) open.Enqueue((nx, ny, nz));
            }
        }
        return (seen, surfaced);
    }

    /// <summary>What is down there: ore and crystal in the walls (more crystal deeper), giant mushrooms, pools of water and lava.</summary>
    [Fact]
    public void TheDeepCavesHaveThingsInThem()
    {
        var w = DefaultMap.Value.World;
        int deep = RegionGenerator.DeepLayers(w.SizeY);
        var count = new Dictionary<BlockType, int>();
        int crystalUp = 0, crystalDown = 0, stoneUp = 0, stoneDown = 0;
        for (int z = 0; z < w.SizeZ; z++)
        for (int x = 0; x < w.SizeX; x++)
        for (int y = 1; y < deep; y++)
        {
            var b = w.Get(x, y, z);
            count[b] = count.GetValueOrDefault(b) + 1;
            bool low = y < deep / 2;
            if (b == BlockType.Crystal) { if (low) crystalDown++; else crystalUp++; }
            if (b == BlockType.Stone) { if (low) stoneDown++; else stoneUp++; }
        }
        _out.WriteLine(string.Join(", ", count.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} {kv.Value}")));
        Assert.True(count.GetValueOrDefault(BlockType.MushroomStem) >= 40);
        Assert.True(count.GetValueOrDefault(BlockType.MushroomCap) >= 200);
        Assert.True(count.GetValueOrDefault(BlockType.Water) >= 50);
        Assert.True(count.GetValueOrDefault(BlockType.Lava) >= 30);
        Assert.True(count.GetValueOrDefault(BlockType.IronOre) >= 10_000);
        Assert.True(crystalDown * (long)stoneUp > crystalUp * (long)stoneDown, "crystal is richer deeper");
    }

    /// <summary>The pools hold: no water or lava down there with open air beside it at its level.</summary>
    [Fact]
    public void TheDeepPoolsDoNotSpill()
    {
        var w = DefaultMap.Value.World;
        int deep = RegionGenerator.DeepLayers(w.SizeY);
        for (int z = 1; z < w.SizeZ - 1; z++)
        for (int x = 1; x < w.SizeX - 1; x++)
        for (int y = 2; y < deep; y++)
        {
            if (w.Get(x, y, z) is not (BlockType.Water or BlockType.Lava)) continue;
            Assert.NotEqual(BlockType.Air, w.Get(x, y - 1, z));
            foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                Assert.NotEqual(BlockType.Air, w.Get(x + dx, y, z + dz));
        }
    }

    /// <summary>Monster nests in the deep chambers, none under a start; and the hill caverns' lairs stay in their cavern.</summary>
    [Fact]
    public void MonstersNestInTheDeepCaves()
    {
        var sim = CavernTests.DefaultMatch();
        int deep = RegionGenerator.DeepLayers(sim.World.SizeY);
        var nests = sim.Monsters.Lairs.Where(l => l.Y < deep).ToList();
        Assert.InRange(nests.Count, 8, 30);
        Assert.All(nests, l => Assert.Equal(CavernSpot.Along, l.Monster.Lair!.Cavern));
        foreach (var l in nests)
        foreach (var s in sim.Map.Starts)
            Assert.True(IntMath.Distance(l.X, l.Z, s.X, s.Z) >= MonsterSystem.WildStartGap);
        var cave = sim.Map.Cavern!.Value;
        var wyrm = sim.Monsters.Lairs.Single(l => l.Monster.Id == "cave_wyrm");
        Assert.True(wyrm.Y >= cave.Y - MonsterSystem.CavernDrop);
    }

    [Fact]
    public void TheDeepChambersAreSavedWithTheMap()
    {
        var map = DefaultMap.Value;
        using var ms = new MemoryStream();
        map.Save(ms);
        ms.Position = 0;
        var loaded = RegionMap.Load(ms);
        Assert.Equal(map.Deeps, loaded.Deeps);
        Assert.Equal(map.ComputeHash(), loaded.ComputeHash());
    }
}
