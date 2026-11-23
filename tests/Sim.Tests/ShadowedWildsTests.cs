using System;
using System.Linq;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// The shadowed wilds (the design doc's far interior): a dark forest round the middle of the map, where units see
/// less far and shoot worse, shadow crystal grows out of the ground and corrupted beasts den in packs that grow.
/// </summary>
public class ShadowedWildsTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Tps = Simulation.TicksPerSecond;
    private const int Settlers = 0, Wild = Simulation.WildPlayer;

    private static readonly Lazy<RegionMap> DefaultMap = new(() =>
        RegionGenerator.Generate(1234, RegionGenerator.DefaultSize, RegionGenerator.DefaultHeight, RegionGenerator.DefaultSize));

    /// <summary>A flat world whose east half (x of 48 and more) is shadowed wilds.</summary>
    private static Simulation HalfWilds(bool fog = true)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(96, 16, 96, Ground));
        for (int z = 0; z < 96; z++)
        for (int x = 48; x < 96; x++)
            sim.Map.SetBiome(x, z, Biome.Wilds);
        sim.Fog = fog;
        return sim;
    }

    private static Building Place(Simulation sim, int owner, string def, int x, int z) =>
        sim.Economy.PlaceFinished(owner, sim.Content.Building(def), x, z) ?? throw new InvalidOperationException($"{def} does not fit");

    // --- The map ------------------------------------------------------------------------------------

    /// <summary>
    /// The default map has its wilds round the middle, well away from both starts: gloom moss for grass, gloom
    /// pines thick on it, and outcrops of shadow crystal standing out of it.
    /// </summary>
    [Fact]
    public void TheDefaultMapHasShadowedWildsRoundItsMiddle()
    {
        var map = DefaultMap.Value;
        var w = map.World;
        Assert.True(map.HasWilds);
        int wilds = 0, gloom = 0, grass = 0, crystal = 0, needles = 0;
        long sx = 0, sz = 0;
        int keepOff = RegionGenerator.WildsKeepOff * w.SizeX / 512;
        for (int z = 0; z < w.SizeZ; z++)
        for (int x = 0; x < w.SizeX; x++)
        {
            if (map.BiomeAt(x, z) != Biome.Wilds) continue;
            wilds++;
            sx += x; sz += z;
            foreach (var s in map.Starts)
                Assert.True(IntMath.Distance(x, z, s.X, s.Z) >= keepOff, $"the wilds at {x},{z} are near a start");
            int top = w.SurfaceHeight(x, z), under = top;
            while (under > 0 && (BlockInfo.IsLeaves(w.Get(x, under, z)) || w.Get(x, under, z) == BlockType.Air)) under--; // under a crown
            for (int y = under; y > 0 && w.Get(x, y, z) == BlockType.Crystal; y--) crystal++;
            for (int y = top; y > 0; y--)
            {
                var b = w.Get(x, y, z);
                if (b == BlockType.GloomLeaves) needles++;
                else if (b == BlockType.Gloom) { gloom++; break; }
                else if (b == BlockType.Grass) { grass++; break; }
            }
        }
        Assert.InRange(wilds, 6_000, 40_000);
        // Its middle is near the middle of the map.
        Assert.True(Math.Abs(sx / wilds - w.SizeX / 2) <= 48, $"the wilds' middle is at x {sx / wilds}");
        Assert.True(Math.Abs(sz / wilds - w.SizeZ / 2) <= 48, $"the wilds' middle is at z {sz / wilds}");
        Assert.Equal(0, grass);
        Assert.True(gloom > wilds * 2 / 3, $"only {gloom} of {wilds} columns are gloom moss");
        Assert.True(needles > wilds / 2, $"only {needles} blocks of gloom pine crowns");
        // Each outcrop is 34 blocks of crystal: 16 over the ground and two layers of nine in it.
        int want = RegionGenerator.WildsCrystals * RegionGenerator.FeatureSets(w.SizeX, w.SizeZ);
        Assert.Equal(0, crystal % 34);
        Assert.InRange(crystal / 34, want / 2, want);
        // Gloom moss grows nowhere else.
        for (int z = 0; z < w.SizeZ; z += 3)
        for (int x = 0; x < w.SizeX; x += 3)
            if (map.BiomeAt(x, z) != Biome.Wilds)
                Assert.NotEqual(BlockType.Gloom, w.Get(x, w.SurfaceHeight(x, z), z));
    }

    /// <summary>A small map (a test or scripted one) has no wilds.</summary>
    [Fact]
    public void ASmallMapHasNoWilds()
    {
        var map = RegionGenerator.Generate(1234, 256, 64, 256);
        Assert.False(map.HasWilds);
    }

    /// <summary>The same seed lays out the same wilds.</summary>
    [Fact]
    public void TheSameSeedGivesTheSameWilds()
    {
        var again = RegionGenerator.Generate(1234, RegionGenerator.DefaultSize, RegionGenerator.DefaultHeight, RegionGenerator.DefaultSize);
        Assert.Equal(DefaultMap.Value.ComputeHash(), again.ComputeHash());
    }

    /// <summary>Gloom moss is ground like grass: workers dig it, buildings level it, it falls when undermined.</summary>
    [Fact]
    public void GloomMossIsGroundLikeGrass()
    {
        Assert.True(Harvest.CanGather(BlockType.Gloom));
        Assert.False(Harvest.Yields(BlockType.Gloom, out _));
        Assert.True(BlockInfo.IsGround(BlockType.Gloom));
        Assert.True(BlockInfo.CanFall(BlockType.Gloom));
        Assert.True(BlockInfo.IsLeaves(BlockType.GloomLeaves));
        Assert.False(BlockInfo.IsLeaves(BlockType.Gloom));
        Assert.True(BlockInfo.Last >= BlockType.GloomLeaves); // saved maps may hold both
    }

    // --- The gloom ----------------------------------------------------------------------------------

    /// <summary>
    /// A scout in the wilds sees <see cref="Conditions.GloomSight"/> percent as far as one on open ground, by day
    /// and with day and night off; a torch lights the gloom and it sees as far as ever there.
    /// </summary>
    [Fact]
    public void InTheGloomUnitsSeeLessFarUnlessThereIsLight()
    {
        foreach (bool dayNight in new[] { false, true })
        {
            var sim = HalfWilds();
            sim.DayNight = dayNight;
            Assert.Equal(100, sim.Conditions.SightPercent(20, 20, false));
            Assert.Equal(Conditions.GloomSight, sim.Conditions.SightPercent(70, 20, false));
            Assert.Equal(100, sim.Conditions.SightPercent(70, 20, true));

            sim.SpawnUnit(Settlers, "spearman", 20, 20); // sees 8 on open ground
            sim.SpawnUnit(Settlers, "spearman", 70, 60); // sees 8 * 60% = 4.8 in the gloom
            sim.Run(VisionSystem.UpdateTicks);
            Assert.Equal(Sight.Visible, sim.Vision.At(Settlers, 28, 20));
            Assert.Equal(Sight.Visible, sim.Vision.At(Settlers, 74, 60));
            Assert.NotEqual(Sight.Visible, sim.Vision.At(Settlers, 76, 60));

            // A torch by the second scout lights the gloom round it.
            Place(sim, Settlers, "torch", 72, 64);
            sim.Run(Lights.UpdateTicks);
            Assert.True(sim.Conditions.Lights.Lit(70, 60), $"day and night {(dayNight ? "on" : "off")}: no light by day in the wilds");
            Assert.Equal(Sight.Visible, sim.Vision.At(Settlers, 77, 60));
        }
    }

    /// <summary>
    /// Shots at a target in the gloom miss <see cref="Conditions.GloomMiss"/> times in a hundred, unless it stands
    /// in the light; a target on open ground by day is hit every time.
    /// </summary>
    [Fact]
    public void ShotsIntoTheGloomMissUnlessItIsLit()
    {
        var sim = HalfWilds();
        Assert.Equal(100, sim.Conditions.HitChance(20, 20));
        Assert.Equal(100 - Conditions.GloomMiss, sim.Conditions.HitChance(70, 20));
        Place(sim, Settlers, "torch", 72, 22);
        sim.Run(Lights.UpdateTicks);
        Assert.Equal(100, sim.Conditions.HitChance(70, 20));
    }

    // --- The corrupted beasts -----------------------------------------------------------------------

    /// <summary>On the default map the shade stalkers' dens are all in the wilds.</summary>
    [Fact]
    public void ShadeStalkersDenInTheWilds()
    {
        var sim = new Simulation(1234, RegionGenerator.DefaultSize, RegionGenerator.DefaultHeight, RegionGenerator.DefaultSize);
        sim.PlaceStartingBuildings();
        sim.SpawnLairs();
        sim.SpawnCities();
        var stalker = sim.Content.Unit("shade_stalker");
        var dens = sim.Monsters.Lairs.Where(l => l.Monster == stalker).ToList();
        int want = (int)(stalker.Lair!.Count * (long)sim.World.SizeX * sim.World.SizeZ / (512L * 512L));
        Assert.Equal(want, dens.Count);
        foreach (var den in dens)
        {
            Assert.Equal(Biome.Wilds, sim.Map.BiomeAt(den.X, den.Z));
            Assert.Equal(stalker.Lair.Pack, den.Pack.Count);
        }
    }

    /// <summary>
    /// A shade stalker pack left alone grows: one more is born at the den every <see cref="LairDef.GrowTicks"/>,
    /// until it is <see cref="LairDef.GrowTo"/> strong, and no more after that.
    /// </summary>
    [Fact]
    public void AStalkerPackLeftAloneGrows()
    {
        var sim = HalfWilds(fog: false);
        var stalker = sim.Content.Unit("shade_stalker");
        var def = stalker.Lair!;
        Assert.True(def.GrowTicks > 0 && def.GrowTo > def.Pack);
        var lair = sim.Monsters.Place(stalker, 72, 48)!;
        Assert.Equal(def.Pack, lair.Pack.Count);
        sim.Run(def.GrowTicks - 1);
        Assert.Equal(def.Pack, lair.Pack.Count);
        sim.Run(2);
        Assert.Equal(def.Pack + 1, lair.Pack.Count);
        int newborn = sim.Units.IndexOf(lair.Pack[^1]);
        Assert.Equal(Wild, sim.Units.Owner[newborn]);
        Assert.True(IntMath.Distance(sim.Units.X[newborn].FloorToInt(), sim.Units.Z[newborn].FloorToInt(), 72, 48) <= 12);
        sim.Run(def.GrowTicks * (def.GrowTo - def.Pack + 2));
        Assert.Equal(def.GrowTo, lair.Pack.Count);
    }

    /// <summary>
    /// A grown pack is a harder fight: a Settler squad that would clear a fresh den is beaten by the same den left
    /// to grow, and whoever clears one gets its shadow crystal and gold.
    /// </summary>
    [Fact]
    public void ADenLeftToGrowIsAHarderFightAndClearingOnePays()
    {
        var (sim, lair) = Fight(late: false);
        Assert.True(lair.Cleared, "six spearmen did not clear a fresh den");
        var stock = new int[Goods.Count];
        foreach (var b in sim.Buildings.All.Where(b => b.Owner == Settlers))
            for (int g = 0; g < stock.Length; g++) stock[g] += b.Stock[g];
        Assert.True(stock[(int)Good.Crystal] >= 40, $"only {stock[(int)Good.Crystal]} crystal for clearing it");
        Assert.True(stock[(int)Good.Gold] >= 20, $"only {stock[(int)Good.Gold]} gold for clearing it");

        var (_, grown) = Fight(late: true);
        Assert.False(grown.Cleared, "six spearmen cleared a den left to grow as easily");
    }

    /// <summary>Six Settler spearmen attack a shade stalker den, at once or after it has grown for a while.</summary>
    private static (Simulation Sim, Lair Lair) Fight(bool late)
    {
        var sim = HalfWilds(fog: false);
        sim.Map.Starts.Add(new StartArea(Settlers, 12, Ground + 1, 48));
        sim.PlaceStartingBuildings();
        var lair = sim.Monsters.Place(sim.Content.Unit("shade_stalker"), 72, 48)!;
        if (late) sim.Run(lair.Monster.Lair!.GrowTicks * 6);
        var squad = Enumerable.Range(0, 6).Select(k => sim.SpawnUnit(Settlers, "spearman", 52 + k % 3, 46 + k / 3 * 2)).ToArray();
        sim.Schedule(sim.Tick, new AttackMoveCommand(Settlers, squad, 72, 48));
        for (int t = 0; t < 120 * Tps && !lair.Cleared && squad.Any(sim.Units.Exists); t++) sim.Step();
        return (sim, lair);
    }

    // --- The data -----------------------------------------------------------------------------------

    [Fact]
    public void GrowToNeedsGrowAndMustBeMoreThanThePack()
    {
        var files = new System.Collections.Generic.Dictionary<string, string>();
        foreach (var f in ContentLoader.Files) files[f] = ContentLoader.EmbeddedText(f)!;
        files["units.json"] = files["units.json"].Replace("\"grow\": 150, \"growTo\": 9", "\"growTo\": 9");
        var e = Assert.Throws<ContentException>(() => ContentLoader.Load(files));
        Assert.Contains(e.Errors, m => m.Contains("unit 'shade_stalker'") && m.Contains("\"growTo\" needs a \"grow\""));

        foreach (var f in ContentLoader.Files) files[f] = ContentLoader.EmbeddedText(f)!;
        files["units.json"] = files["units.json"].Replace("\"grow\": 150, \"growTo\": 9", "\"grow\": 150, \"growTo\": 2");
        e = Assert.Throws<ContentException>(() => ContentLoader.Load(files));
        Assert.Contains(e.Errors, m => m.Contains("\"growTo\" is 2, but the pack starts 3 strong"));
    }
}
