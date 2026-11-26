using System;
using System.Linq;
using Rts.Sim.Content;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Packs spread (see <see cref="LairDef.SpreadTicks"/>): a lair spread across the map that is left alone at full
/// strength has a new pack born at its den every so often, which sets off to found a lair of its own nearby, so the
/// wild fills up with dens the longer nobody clears them.
/// </summary>
public class SpreadTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Settlers = 0;

    /// <summary>A flat grassland world 192 columns across.</summary>
    private static Simulation Grassland()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(192, 16, 192, Ground));
        for (int z = 0; z < 192; z++)
        for (int x = 0; x < 192; x++)
            sim.Map.SetBiome(x, z, Biome.Grassland);
        sim.Fog = false;
        return sim;
    }

    private static (Simulation Sim, Lair Lair) WolfLair()
    {
        var sim = Grassland();
        var wolf = sim.Content.Unit("wolf");
        var lair = sim.Monsters.Place(wolf, 96, 96)!;
        sim.Monsters.StartSpreading(lair);
        return (sim, lair);
    }

    /// <summary>
    /// A wolf lair left alone founds a new one once its time comes: a new pack of four is born by its den and walks
    /// off to a den of its own in the same biome, well away from the old one but within reach of it, and the old pack
    /// stays as it was.
    /// </summary>
    [Fact]
    public void ALairLeftAloneFoundsANewOne()
    {
        var (sim, lair) = WolfLair();
        var def = lair.Monster.Lair!;
        Assert.True(def.SpreadTicks > 0);
        // A lair settles in before it first spreads: twice its time.
        sim.Run(2 * def.SpreadTicks - 1);
        Assert.Single(sim.Monsters.Lairs);
        sim.Run(2);
        Assert.Equal(2, sim.Monsters.Lairs.Count);
        var young = sim.Monsters.Lairs[1];
        Assert.Equal(lair.Id, young.Parent);
        Assert.Equal(lair.Monster, young.Monster);
        Assert.Equal(def.Pack, young.Pack.Count);
        Assert.Equal(def.Pack, lair.Pack.Count);
        Assert.True(young.NextSpread > 0, "the new lair does not spread in turn");
        int d = IntMath.Distance(young.X, young.Z, lair.X, lair.Z);
        Assert.InRange(d, MonsterSystem.WildGap, MonsterSystem.SpreadRange);
        Assert.Equal(Biome.Grassland, sim.Map.BiomeAt(young.X, young.Z));

        // The young are born by the old den and walk to their own.
        foreach (int id in young.Pack)
        {
            int i = sim.Units.IndexOf(id);
            Assert.Equal(Simulation.WildPlayer, sim.Units.Owner[i]);
        }
        sim.Run(30 * Simulation.TicksPerSecond);
        foreach (int id in young.Pack)
        {
            int i = sim.Units.IndexOf(id);
            int near = IntMath.Distance(sim.Units.X[i].FloorToInt(), sim.Units.Z[i].FloorToInt(), young.X, young.Z);
            // Wolves roam round their den: the young keep within that of their new one.
            Assert.True(near <= def.Roam + MonsterSystem.HomeRadius, $"a young wolf is {near} columns from its new den");
        }
    }

    /// <summary>However long they are left, the lairs of one monster never stand more than its cap at once.</summary>
    [Fact]
    public void SpreadingStopsAtTheCap()
    {
        var (sim, lair) = WolfLair();
        int cap = sim.Monsters.SpreadCap(lair.Monster);
        Assert.True(cap >= 2);
        sim.Run(lair.Monster.Lair!.SpreadTicks * (2 * cap + 4) + 1);
        int standing = sim.Monsters.Lairs.Count(l => !l.Cleared);
        Assert.Equal(cap, standing);
        Assert.Equal(cap, sim.Monsters.Standing(lair.Monster));
        // Every den keeps its distance from the others.
        foreach (var a in sim.Monsters.Lairs)
        foreach (var b in sim.Monsters.Lairs)
            if (a != b) Assert.True(IntMath.Distance(a.X, a.Z, b.X, b.Z) >= MonsterSystem.WildGap);
    }

    /// <summary>A pack that has lost some of its monsters does not spread until it is whole again.</summary>
    [Fact]
    public void AWeakenedPackDoesNotSpread()
    {
        var (sim, lair) = WolfLair();
        sim.Units.Remove(lair.Pack[0]);
        sim.Run(lair.Monster.Lair!.SpreadTicks * 3 + 1);
        Assert.Single(sim.Monsters.Lairs);
    }

    /// <summary>Packs spread through the wild, not into towns: no new den goes within reach of a building.</summary>
    [Fact]
    public void NoNewDenNearBuildings()
    {
        var (sim, lair) = WolfLair();
        // Houses all round the den, 50 columns out: anywhere a new den could go is within 40 of one.
        for (int a = 0; a < 360; a += 15)
        {
            int x = 96 + (int)Math.Round(50 * Math.Cos(a * Math.PI / 180)), z = 96 + (int)Math.Round(50 * Math.Sin(a * Math.PI / 180));
            sim.Economy.PlaceFinished(Settlers, sim.Content.Building("house"), x, z);
        }
        var houses = sim.Buildings.All.ToList();
        Assert.True(houses.Count >= 20, $"only {houses.Count} houses fit");
        sim.Run(lair.Monster.Lair!.SpreadTicks * 4 + 1);
        foreach (var young in sim.Monsters.Lairs.Where(l => l.Parent != 0))
            foreach (var b in houses)
                Assert.True(b.ColumnDistance(young.X, young.Z) >= MonsterSystem.SpreadBuildingGap);
    }

    /// <summary>
    /// On the default map the wolf packs, boar herds and shade stalker dens spread across it spread; the starts' own
    /// wolf lairs and the monsters that do not spread stay as they are.
    /// </summary>
    [Fact]
    public void OnTheDefaultMapTheWildLairsSpread()
    {
        var sim = new Simulation(1234, RegionGenerator.DefaultSize, RegionGenerator.DefaultHeight, RegionGenerator.DefaultSize);
        sim.PlaceStartingBuildings();
        sim.SpawnLairs();
        sim.SpawnCities();
        foreach (var id in new[] { "wolf", "boar", "shade_stalker" })
        {
            var monster = sim.Content.Unit(id);
            Assert.True(monster.Lair!.SpreadTicks > 0, $"{id} does not spread");
            Assert.Contains(sim.Monsters.Lairs, l => l.Monster == monster && l.NextSpread > 0);
            Assert.True(sim.Monsters.SpreadCap(monster) > sim.Monsters.Standing(monster));
        }
        foreach (var s in sim.Map.Starts)
            Assert.DoesNotContain(sim.Monsters.Lairs, l => l.NextSpread > 0 && IntMath.Distance(l.X, l.Z, s.X, s.Z) < MonsterSystem.WildStartGap);
        Assert.DoesNotContain(sim.Monsters.Lairs, l => l.NextSpread > 0 && l.Monster.Id is "giant_spider" or "stone_giant" or "bog_horror");
    }

    [Fact]
    public void SpreadToNeedsSpread()
    {
        var files = new System.Collections.Generic.Dictionary<string, string>();
        foreach (var f in ContentLoader.Files) files[f] = ContentLoader.EmbeddedText(f)!;
        files["units.json"] = files["units.json"].Replace("\"spread\": 300, \"spreadTo\": 2", "\"spreadTo\": 2");
        var e = Assert.Throws<ContentException>(() => ContentLoader.Load(files));
        Assert.Contains(e.Errors, m => m.Contains("unit 'wolf'") && m.Contains("\"spreadTo\" needs a \"spread\""));
    }
}
