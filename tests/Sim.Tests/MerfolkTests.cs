using System;
using System.Linq;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// The merfolk's cove: a sixth city-state on the sea shore, whose holder's fishing huts bring in half as much food again
/// (the merfolk drive the fish into their nets), and who hires merfolk tridents and harpooners.
/// </summary>
public class MerfolkTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Tps = Simulation.TicksPerSecond;
    private const int Settlers = 0, Orcs = 1;
    private const int TownX = 96, TownZ = 96;

    // A flat world with a pond (columns 30..39 by 20..29, two blocks deep) for a fishing hut, and room for the cove.
    private static Simulation WithPond()
    {
        var w = TestWorlds.Flat(192, 24, 192, Ground);
        for (int z = 20; z < 30; z++)
        for (int x = 30; x < 40; x++)
        for (int y = Ground - 1; y <= Ground; y++)
            w.Set(x, y, z, BlockType.Water);
        var sim = TestWorlds.Sim(w);
        sim.Fog = false;
        return sim;
    }

    private static CityState PlaceCove(Simulation sim) =>
        sim.Cities.Place(sim.Content.CityState("merfolk"), TownX, TownZ) ?? throw new InvalidOperationException("no ground for the cove");

    private static void Do(Simulation sim, Command c)
    {
        sim.Schedule(sim.Tick, c);
        sim.Step();
    }

    private static int RunUntil(Simulation sim, Func<bool> done, int maxTicks, string what)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return t;
            sim.Step();
        }
        Assert.Fail($"Not done after {maxTicks} ticks: {what}");
        return maxTicks;
    }

    /// <summary>The player's spearmen break the conch, and it stands again in their hands; the spearmen left are removed.</summary>
    private static void Conquer(Simulation sim, CityState town, int player, int spearmen = 30)
    {
        var squad = Enumerable.Range(0, spearmen).Select(k => sim.SpawnUnit(player, "spearman", town.X - 4 + k % 8, town.Z + 3 + k / 8)).ToArray();
        Do(sim, new AttackBuildingCommand(player, squad, sim.Cities.Totem(town)!.Id));
        RunUntil(sim, () => town.Holder == player && town.TotemId != 0, 120 * Tps, "the squad breaks the conch and it stands again");
        foreach (int id in squad)
            if (sim.Units.Exists(id)) sim.Units.Remove(id);
        sim.Step();
    }

    [Fact]
    public void TheMerfolkAreData()
    {
        var db = ContentDb.Default;
        var merfolk = db.CityState("merfolk");
        Assert.Equal(5, merfolk.Index); // placed last, after the centaurs
        Assert.Equal("Merfolk cove", merfolk.Name);
        Assert.True(merfolk.Coast);
        Assert.False(merfolk.Deep);
        Assert.False(merfolk.Crossing);
        Assert.Null(merfolk.Biome);
        Assert.Equal(0, merfolk.WatchRange);
        Assert.Equal(50, merfolk.FishingBonus);
        Assert.All(db.CityStates.Where(c => c != merfolk), c => Assert.False(c.Coast));
        Assert.All(db.CityStates.Where(c => c != merfolk), c => Assert.Equal(0, c.FishingBonus));
        Assert.Same(db.Building("merfolk_conch"), merfolk.Totem);
        Assert.Equal(BuildingKind.Totem, merfolk.Totem!.Kind);
        Assert.Equal(new[] { "merfolk_trident", "merfolk_trident", "merfolk_trident", "merfolk_harpooner", "merfolk_harpooner" }, merfolk.Guards.Select(u => u.Id));
        Assert.Equal((Good.Gold, 15), (merfolk.TributeGood, merfolk.TributeAmount));
        Assert.Equal((Good.Iron, 10, Good.Food, 15), (merfolk.Buys, merfolk.Load, merfolk.Sells, merfolk.Price));
        Assert.Equal(new[] { "merfolk_trident", "merfolk_harpooner" }, merfolk.Mercenaries.Select(u => u.Id));
        Assert.Equal((4, 45 * Tps), (merfolk.Pool, merfolk.RefillTicks));
        Assert.Null(merfolk.Lair);
        Assert.Equal((BlockType.StoneBrick, BlockType.Thatch, BlockType.Sand), (merfolk.Wall, merfolk.Roof, merfolk.Ground));
        var trident = db.Unit("merfolk_trident");
        var harpooner = db.Unit("merfolk_harpooner");
        Assert.Equal(UnitRole.LineInfantry, trident.Role);
        Assert.Equal(UnitRole.Ranged, harpooner.Role);
        Assert.Same(merfolk, trident.HiredFrom);
        Assert.Same(merfolk, harpooner.HiredFrom);
        // The harpooner throws further than a goblin's sling.
        Assert.True(harpooner.Range > db.Unit("goblin_slinger").Range);
        Assert.True(trident.Range <= Fix64.FromInt(2));
    }

    [Fact]
    public void ACoastTownCannotAlsoBeDeepACrossingOrKeepToABiome()
    {
        foreach (var extra in new[] { "\"deep\": true", "\"crossing\": true", "\"biome\": \"grassland\"" })
        {
            var files = ContentLoader.Files.ToDictionary(f => f, f => ContentLoader.EmbeddedText(f)!);
            const string old = "{ \"id\": \"merfolk\", \"name\": \"Merfolk cove\", \"coast\": true,";
            Assert.Contains(old, files["citystates.json"]);
            files["citystates.json"] = files["citystates.json"].Replace(old, old + " " + extra + ",");
            var ex = Assert.Throws<ContentException>(() => ContentLoader.Load(files));
            Assert.Contains("merfolk", ex.Message);
        }
    }

    /// <summary>
    /// Two fishers work a hut by the pond; while their player holds the cove each round brings in 6 food instead of 4.
    /// A rival holding it does nothing for them.
    /// </summary>
    [Theory]
    [InlineData(-1, 4)]
    [InlineData(Settlers, 6)]
    [InlineData(Orcs, 4)]
    public void TheHoldersFishingHutsCatchMore(int holder, int perRound)
    {
        var sim = WithPond();
        var cove = PlaceCove(sim);
        foreach (var g in cove.Guards.ToArray()) sim.Units.Remove(g);
        if (holder >= 0) Conquer(sim, cove, holder);
        Assert.Equal(holder, cove.Holder);
        Assert.Equal(holder == Settlers ? 50 : 0, sim.Cities.FishingBonusOf(Settlers));
        var hall = sim.Economy.PlaceFinished(Settlers, sim.Content.Building("town_hall"), 10, 10)!;
        var hut = sim.Economy.PlaceFinished(Settlers, sim.Content.Building("fishing_hut"), 26, 22)!;
        int fisher = sim.SpawnUnit(Settlers, "worker", 20, 12);
        Do(sim, new WorkCommand(Settlers, new[] { fisher }, hut.Id));
        int i = sim.Units.IndexOf(fisher);
        RunUntil(sim, () => sim.Units.CarryAmount[sim.Units.IndexOf(fisher)] > 0, 60 * Tps, "the fisher brings in a catch");
        Assert.Equal(Good.Food, sim.Units.Carry[sim.Units.IndexOf(fisher)]);
        Assert.Equal(perRound, sim.Units.CarryAmount[sim.Units.IndexOf(fisher)]);
        Assert.True(i >= 0);
        Assert.NotNull(hall);
    }

    /// <summary>Only fishing huts catch more: a quarry's stone is the same for the cove's holder.</summary>
    [Fact]
    public void OtherWorksAreNotChanged()
    {
        var sim = WithPond();
        var cove = PlaceCove(sim);
        foreach (var g in cove.Guards.ToArray()) sim.Units.Remove(g);
        Conquer(sim, cove, Settlers);
        sim.Economy.PlaceFinished(Settlers, sim.Content.Building("town_hall"), 10, 10);
        var quarryDef = sim.Content.Building("quarry");
        var quarry = sim.Economy.PlaceFinished(Settlers, quarryDef, 10, 40)!;
        int worker = sim.SpawnUnit(Settlers, "worker", 20, 40);
        Do(sim, new WorkCommand(Settlers, new[] { worker }, quarry.Id));
        RunUntil(sim, () => sim.Units.CarryAmount[sim.Units.IndexOf(worker)] > 0, 60 * Tps, "the worker brings in stone");
        int i = sim.Units.IndexOf(worker);
        Assert.Contains(quarryDef.Works, w => w.Good == sim.Units.Carry[i] && w.Amount == sim.Units.CarryAmount[i]);
    }

    /// <summary>
    /// The cove is placed in a match by the open sea, the usual distance away from the other towns in the open, with
    /// its conch, five guards and a square paved with sand.
    /// </summary>
    [Theory]
    [InlineData(1234UL)]
    [InlineData(7UL)]
    public void EveryMatchHasAMerfolkCoveOnTheCoast(ulong seed)
    {
        var sim = CavernTests.DefaultMatch(seed);
        var towns = sim.Cities.Towns;
        var cove = Assert.Single(towns, t => t.Def.Id == "merfolk");
        Assert.True(sim.Cities.OnCoast(cove.X, cove.Z), $"the cove at {cove.X},{cove.Z} is not by the sea");
        Assert.Equal("merfolk_conch", sim.Cities.Totem(cove)!.Def.Id);
        Assert.Equal(5, cove.Guards.Count());
        int want = sim.World.SizeX * 3 / 16;
        foreach (var other in towns.Where(t => t != cove && !t.Deep))
            Assert.True(IntMath.Distance(other.X, other.Z, cove.X, cove.Z) >= want, $"the cove is close to the {other.Def.Id}");
    }

    /// <summary>The cove is placed on a small map too, away from the other towns.</summary>
    [Fact]
    public void EveryMatchHasAMerfolkCove()
    {
        var sim = new Simulation(1234, 512, 64, 512);
        sim.PlaceStartingBuildings();
        sim.SpawnStartingUnits(24);
        sim.SpawnLairs();
        var towns = sim.SpawnCities();
        var cove = Assert.Single(towns, t => t.Def.Id == "merfolk");
        Assert.Equal(5, cove.Guards.Count());
        foreach (var other in towns.Where(t => t != cove))
        {
            long dx = other.X - cove.X, dz = other.Z - cove.Z;
            Assert.True(dx * dx + dz * dz > 48L * 48, $"the cove is close to the {other.Def.Id}");
        }
    }
}
