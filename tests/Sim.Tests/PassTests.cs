using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Rts.Sim.Content;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// The design doc's stone giants that "guard passes" (Mountains: "Centre ridges"; the reward "gold, iron vein access"):
/// a ridge of sheer stone runs across each hill range with one pass through it, the pass's walls are seamed with iron
/// and gold, and a stone giant stands in every pass.
/// </summary>
public class PassTests
{
    private static readonly Lazy<Simulation> Match = new(() => DefaultMatch(1234));

    private static Simulation DefaultMatch(ulong seed)
    {
        var sim = new Simulation(seed, RegionGenerator.DefaultSize, RegionGenerator.DefaultHeight, RegionGenerator.DefaultSize);
        sim.PlaceStartingBuildings();
        sim.SpawnStartingUnits(24);
        sim.SpawnLairs();
        sim.SpawnCities();
        return sim;
    }

    // Default-map lengths (768 wide, so 3/2 of the 512 numbers, rounded down).
    private static int Scale(int v) => Math.Max(1, v * RegionGenerator.DefaultSize / 512);
    private static readonly int Half = Scale(RegionGenerator.RidgeHalf), PassHalf = Scale(RegionGenerator.PassHalf),
        Ramp = Scale(RegionGenerator.PassRamp);

    /// <summary>
    /// Walks (the way a unit steps: one block up, a few down) from (x0, z0) and says whether it reaches (x1, z1),
    /// keeping within the box and off any column <paramref name="blocked"/> says.
    /// </summary>
    private static bool Reaches(VoxelWorld w, int x0, int z0, int x1, int z1, (int X0, int Z0, int X1, int Z1) box,
        Func<int, int, bool>? blocked = null)
    {
        int y0 = Walk.GroundLevel(w, x0, z0);
        Assert.True(y0 > 0);
        var seen = new HashSet<(int, int, int)> { (x0, z0, y0) };
        var queue = new Queue<(int X, int Z, int Y)>();
        queue.Enqueue((x0, z0, y0));
        while (queue.Count > 0)
        {
            var (x, z, y) = queue.Dequeue();
            if (x == x1 && z == z1) return true;
            foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = x + dx, nz = z + dz;
                if (nx < box.X0 || nz < box.Z0 || nx > box.X1 || nz > box.Z1 || blocked?.Invoke(nx, nz) == true) continue;
                if (!Walk.TryStep(w, x, z, y, nx, nz, out int ny) || !seen.Add((nx, nz, ny))) continue;
                queue.Enqueue((nx, nz, ny));
            }
        }
        return false;
    }

    [Fact]
    public void EveryHillRangeHasARidgeWithAPassThroughIt()
    {
        foreach (ulong seed in new ulong[] { 1234, 7 })
        {
            var map = RegionGenerator.Generate(seed, RegionGenerator.DefaultSize, RegionGenerator.DefaultHeight, RegionGenerator.DefaultSize);
            // The default map has two hill ranges (issue #67), and a pass across each.
            Assert.Equal(RegionGenerator.FeatureSets(RegionGenerator.DefaultSize, RegionGenerator.DefaultSize), map.Passes.Count);
            foreach (var (px, py, pz) in map.Passes)
            {
                Assert.Equal(Biome.Hills, map.BiomeAt(px, pz));
                // A level floor between the walls.
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -PassHalf + 1; dx <= PassHalf - 1; dx++)
                    Assert.Equal(py + 1, Walk.GroundLevel(map.World, px + dx, pz + dz));
                // Walls of stone on both sides of it, along the ridge, too high to climb.
                foreach (int side in new[] { -1, 1 })
                {
                    int x = px + side * (PassHalf + 3);
                    int top = Enumerable.Range(pz - 3, 7).Max(z => Walk.GroundLevel(map.World, x, z));
                    Assert.True(top >= py + 1 + RegionGenerator.RidgeLift - 1, $"seed {seed}: the wall {side} of the pass at ({px}, {pz}) stands at {top}, floor {py}");
                }
            }
        }
    }

    [Fact]
    public void ThePassIsTheWayThroughTheRidge()
    {
        var w = Match.Value.World;
        foreach (var (px, _, pz) in Match.Value.Map.Passes)
        {
            int across = Half + Ramp;
            var box = (px - 24, pz - across - 12, px + 24, pz + across + 12);
            // Through the pass from one side of the ridge to the other.
            Assert.True(Reaches(w, px, pz - across, px, pz + across, box), $"no way through the pass at ({px}, {pz})");
            // With the pass shut, the ridge's sheer sides keep the two apart.
            Assert.False(Reaches(w, px, pz - across, px, pz + across, box, (x, z) => Math.Abs(x - px) <= PassHalf + 3 && Math.Abs(z - pz) <= Half + 2),
                $"the ridge at ({px}, {pz}) can be climbed");
        }
    }

    [Fact]
    public void ThePassWallsAreSeamedWithIronAndGold()
    {
        var map = Match.Value.Map;
        int reach = PassHalf + Scale(RegionGenerator.VeinReach);
        foreach (var (px, py, pz) in map.Passes)
        {
            int iron = 0, gold = 0, stone = 0;
            for (int z = pz - reach; z <= pz + reach; z++)
            for (int x = px - reach; x <= px + reach; x++)
            for (int y = py + 1; y <= py + RegionGenerator.RidgeLift + 4; y++)
            {
                var b = map.World.Get(x, y, z);
                if (b == BlockType.IronOre) iron++;
                else if (b == BlockType.GoldOre) gold++;
                else if (b == BlockType.Stone) stone++;
            }
            Assert.True(iron >= 40, $"{iron} iron ore in the walls of the pass at ({px}, {pz})");
            Assert.True(gold >= 5, $"{gold} gold ore in the walls of the pass at ({px}, {pz})");
            // Iron about one block in seven of the rock, gold about one in thirty.
            Assert.InRange(iron * 1000 / (iron + gold + stone), 80, 220);
        }
    }

    [Fact]
    public void AStoneGiantStandsInEveryPass()
    {
        var sim = Match.Value;
        var giants = sim.Monsters.Lairs.Where(l => l.Monster.Id == "stone_giant").ToList();
        var cityLairs = sim.Cities.Towns.Select(t => t.LairId).ToHashSet();
        foreach (var (px, py, pz) in sim.Map.Passes)
        {
            var lair = Assert.Single(giants, l => IntMath.Distance(l.X, l.Z, px, pz) <= MonsterSystem.PassHeld);
            Assert.Equal(py + 1, lair.Y);
            var giant = Assert.Single(lair.Pack);
            int i = sim.Units.IndexOf(giant);
            Assert.True(IntMath.Distance(sim.Units.X[i].FloorToInt(), sim.Units.Z[i].FloorToInt(), px, pz) <= MonsterSystem.PassHeld + 2);
            // A city-state's own giant may hold one already; any other is the pass's own, in its middle.
            if (!cityLairs.Contains(lair.Id)) Assert.Equal((px, pz), (lair.X, lair.Z));
        }
        // None spread over the hills besides: the passes have them all (the city-states' own lairs aside).
        Assert.All(giants.Where(l => !cityLairs.Contains(l.Id)),
            l => Assert.Contains(sim.Map.Passes, p => (p.X, p.Z) == (l.X, l.Z)));
    }

    [Fact]
    public void AStoneGiantTakesAPassWhateverIsNear()
    {
        // Seed 7: neither pass is held by a city-state's giant, and each gets its own.
        var sim = DefaultMatch(7);
        foreach (var (px, _, pz) in sim.Map.Passes)
            Assert.Contains(sim.Monsters.Lairs, l => l.Monster.Id == "stone_giant" && (l.X, l.Z) == (px, pz));
    }

    [Fact]
    public void WithNoPassTheGiantsKeepToTheHills()
    {
        // A map with no pass (a made one): the giant spreads over its biome as before.
        var w = TestWorlds.Flat(1024, 16, 1024, 3);
        var map = new RegionMap(5, w);
        for (int z = 0; z < 1024; z++)
        for (int x = 0; x < 1024; x++)
            map.SetBiome(x, z, x > 600 ? Biome.Hills : Biome.Grassland);
        map.Starts.Add(new StartArea(0, 100, 4, 512));
        var sim = new Simulation(map);
        var placed = sim.Monsters.PlaceWild();
        Assert.Contains(placed, l => l.Monster.Id == "stone_giant" && l.X > 600);
    }

    [Fact]
    public void ThePassesAreSavedWithTheMap()
    {
        var map = Match.Value.Map;
        using var ms = new MemoryStream();
        map.Save(ms);
        ms.Position = 0;
        var loaded = RegionMap.Load(ms);
        Assert.NotEmpty(loaded.Passes);
        Assert.Equal(map.Passes, loaded.Passes);
        Assert.Equal(map.ComputeHash(), loaded.ComputeHash());
    }

    [Fact]
    public void APassLairNeedsABiomeAndNeitherRoamsNorLurks()
    {
        var files = Files();
        Edit(files, "units.json", "\"biome\": \"hills\", \"count\": 1, \"pass\": true", "\"pass\": true, \"roam\": 8");
        var e = Assert.Throws<ContentException>(() => ContentLoader.Load(files));
        Assert.Contains(e.Errors, m => m.Contains("unit 'stone_giant', lair") && m.Contains("\"pass\") needs a \"biome\""));
        Assert.Contains(e.Errors, m => m.Contains("unit 'stone_giant', lair") && m.Contains("does not \"roam\" or \"lurk\""));
    }

    private static Dictionary<string, string> Files()
    {
        var files = new Dictionary<string, string>();
        foreach (var f in ContentLoader.Files) files[f] = ContentLoader.EmbeddedText(f)!;
        return files;
    }

    private static void Edit(Dictionary<string, string> files, string file, string old, string replacement)
    {
        string text = files[file];
        int at = text.IndexOf(old, StringComparison.Ordinal);
        Assert.True(at >= 0, $"{file} does not contain {old}");
        files[file] = text[..at] + replacement + text[(at + old.Length)..];
    }
}
