using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// The centaur camp: a fifth city-state on the open land, whose holder sees the land all round it (its scouts ride
/// it) and hires centaur archers and lancers, cavalry as quick as a rider; the archers shoot from the saddle.
/// </summary>
public class CentaurTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Tps = Simulation.TicksPerSecond;
    private const int Settlers = 0, Orcs = 1;
    private const int TownX = 96, TownZ = 96;

    private static Simulation Flat(int size = 192, bool fog = false)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(size, 24, size, Ground));
        sim.Fog = fog;
        return sim;
    }

    private static CityState PlaceCamp(Simulation sim) =>
        sim.Cities.Place(sim.Content.CityState("centaurs"), TownX, TownZ) ?? throw new InvalidOperationException("no ground for the camp");

    private static Building Hall(Simulation sim, int owner, int x, int z, int food = 0, int wood = 0, int gold = 0)
    {
        var b = sim.Economy.PlaceFinished(owner, sim.Content.Building("town_hall"), x, z) ?? throw new InvalidOperationException("the hall does not fit");
        b.Stock[(int)Good.Food] = food;
        b.Stock[(int)Good.Wood] = wood;
        b.Stock[(int)Good.Gold] = gold;
        return b;
    }

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

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

    /// <summary>The player's spearmen break the standard, and it stands again in their hands; the spearmen left are removed.</summary>
    private static void Conquer(Simulation sim, CityState town, int player, int spearmen = 30)
    {
        var squad = Enumerable.Range(0, spearmen).Select(k => sim.SpawnUnit(player, "spearman", town.X - 4 + k % 8, town.Z + 3 + k / 8)).ToArray();
        Do(sim, new AttackBuildingCommand(player, squad, sim.Cities.Totem(town)!.Id));
        RunUntil(sim, () => town.Holder == player && town.TotemId != 0, 120 * Tps, "the squad breaks the standard and it stands again");
        foreach (int id in squad)
            if (sim.Units.Exists(id)) sim.Units.Remove(id);
        sim.Step();
    }

    [Fact]
    public void TheCentaursAreData()
    {
        var db = ContentDb.Default;
        var centaurs = db.CityState("centaurs");
        Assert.Equal(4, centaurs.Index); // placed last, after the trolls
        Assert.Equal("Centaur camp", centaurs.Name);
        Assert.False(centaurs.Deep);
        Assert.False(centaurs.Crossing);
        Assert.Equal(0, centaurs.TollGold);
        Assert.Equal(40, centaurs.WatchRange);
        Assert.Equal(Rts.Sim.World.Biome.Grassland, centaurs.Biome);
        Assert.All(db.CityStates.Where(c => c != centaurs), c => Assert.Null(c.Biome));
        Assert.Same(db.Building("centaur_standard"), centaurs.Totem);
        Assert.Equal(BuildingKind.Totem, centaurs.Totem!.Kind);
        Assert.Equal(new[] { "centaur_archer", "centaur_archer", "centaur_archer", "centaur_lancer", "centaur_lancer" }, centaurs.Guards.Select(u => u.Id));
        Assert.Equal(6, centaurs.GuardReach); // mounted: they ride out further than the others' guards
        Assert.Equal((Good.Food, 40), (centaurs.TributeGood, centaurs.TributeAmount));
        Assert.Equal((Good.Wood, Good.Food, 12), (centaurs.Buys, centaurs.Sells, centaurs.Price));
        Assert.Equal(new[] { "centaur_archer", "centaur_lancer" }, centaurs.Mercenaries.Select(u => u.Id));
        Assert.Equal((4, 45 * Tps), (centaurs.Pool, centaurs.RefillTicks));
        Assert.Null(centaurs.Lair);
        Assert.Equal((BlockType.Planks, BlockType.Thatch, BlockType.Grass), (centaurs.Wall, centaurs.Roof, centaurs.Ground));
        // Both are cavalry, nearly as quick as a rider; the archer shoots from range, the lancer fights close.
        var archer = db.Unit("centaur_archer");
        var lancer = db.Unit("centaur_lancer");
        var rider = db.Unit("rider");
        foreach (var u in new[] { archer, lancer })
        {
            Assert.Equal(UnitRole.Cavalry, u.Role);
            Assert.Same(centaurs, u.HiredFrom);
            Assert.True(u.Speed > db.Unit("spearman").Speed * 3 / 2, $"{u.Id} is not quick");
            Assert.True(u.Speed <= rider.Speed);
        }
        Assert.True(archer.Range > Fix64.FromInt(2), "the archer does not shoot");
        Assert.True(lancer.Range <= Fix64.FromInt(2));
        Assert.True(lancer.MaxHp > archer.MaxHp);
        // The other towns' scouts show nothing.
        Assert.All(db.CityStates.Where(c => c != centaurs), c => Assert.Equal(0, c.WatchRange));
    }

    [Fact]
    public void WatchDataIsChecked()
    {
        var files = ContentLoader.Files.ToDictionary(f => f, f => ContentLoader.EmbeddedText(f)!);
        string Errors(string old, string replacement)
        {
            var edited = new Dictionary<string, string>(files) { ["citystates.json"] = files["citystates.json"].Replace(old, replacement) };
            Assert.NotEqual(files["citystates.json"], edited["citystates.json"]);
            var ex = Assert.Throws<ContentException>(() => ContentLoader.Load(edited));
            return string.Join("\n", ex.Errors);
        }
        Assert.Contains("range", Errors("\"watch\": { \"range\": 40 }", "\"watch\": { \"range\": 0 }"));
        Assert.Contains("reach", Errors("\"watch\": { \"range\": 40 }", "\"watch\": { \"reach\": 40 }"));
        Assert.Contains("biome", Errors("\"biome\": \"grassland\"", "\"biome\": \"meadow\""));
        Assert.Contains("biome", Errors("\"id\": \"centaurs\", \"name\": \"Centaur camp\",", "\"id\": \"centaurs\", \"name\": \"Centaur camp\", \"deep\": true,"));
    }

    /// <summary>
    /// With fog of war on, nobody sees the open land round the camp until they hold it: then its holder sees 40 columns
    /// round its square, though no unit or building of theirs is near, and the rival does not. When the rival takes the
    /// camp over, the land goes out of the first holder's sight (seen before, no longer in sight) and into the new one's.
    /// </summary>
    [Fact]
    public void TheHolderSeesTheLandRoundTheCamp()
    {
        var sim = Flat(fog: true);
        Hall(sim, Settlers, 12, 12);
        Hall(sim, Orcs, 12, 170);
        var town = PlaceCamp(sim);
        int r = CityStateSystem.TownRadius;
        int near = TownX + r + 36, far = TownX + r + 46; // east of the camp: within its scouts' 40 columns, and beyond them
        sim.Run(VisionSystem.UpdateTicks * 2);
        Assert.False(sim.Vision.Sees(Settlers, near, TownZ));
        Assert.False(sim.Vision.Sees(Orcs, near, TownZ));

        Conquer(sim, town, Settlers);
        sim.Run(VisionSystem.UpdateTicks * 2);
        Assert.True(sim.Vision.Sees(Settlers, near, TownZ), "the holder does not see the land round the camp");
        Assert.True(sim.Vision.Sees(Settlers, TownX, TownZ - r - 36));
        Assert.False(sim.Vision.Sees(Settlers, far, TownZ));
        Assert.False(sim.Vision.Sees(Orcs, near, TownZ));

        Conquer(sim, town, Orcs);
        sim.Run(VisionSystem.UpdateTicks * 2);
        Assert.True(sim.Vision.Sees(Orcs, near, TownZ));
        Assert.Equal(Sight.Seen, sim.Vision.At(Settlers, near, TownZ));
    }

    /// <summary>At night the scouts see less far, as everyone does.</summary>
    [Fact]
    public void TheScoutsSeeLessAtNight()
    {
        var sim = Flat(fog: true);
        sim.DayNight = true;
        sim.DayStart = 1 * 60; // 1 in the night
        Hall(sim, Settlers, 12, 12);
        var town = PlaceCamp(sim);
        int r = CityStateSystem.TownRadius;
        Conquer(sim, town, Settlers);
        sim.Run(VisionSystem.UpdateTicks * 2);
        Assert.True(sim.Conditions.SightPercent(TownX, TownZ, false) < 100);
        Assert.False(sim.Vision.Sees(Settlers, TownX + r + 36, TownZ));
        Assert.True(sim.Vision.Sees(Settlers, TownX + r + 10, TownZ));
    }

    /// <summary>
    /// The holder hires a centaur archer for food, wood and gold; it steps out at the camp and, told to attack an Orc
    /// worker, shoots it from the saddle without riding up to it.
    /// </summary>
    [Fact]
    public void CentaurArchersShootFromTheSaddle()
    {
        var sim = Flat();
        var hall = Hall(sim, Settlers, 12, 12, food: 1000, wood: 200, gold: 100);
        Hall(sim, Orcs, 12, 170);
        var town = PlaceCamp(sim);
        Conquer(sim, town, Settlers);
        int before = sim.Units.Count;
        var mine = new HashSet<int>(Enumerable.Range(0, sim.Units.Count).Select(i => sim.Units.Id[i]));
        Do(sim, new HireCommand(Settlers, town.Id, "centaur_archer"));
        var archerDef = sim.Content.Unit("centaur_archer");
        int archer = Enumerable.Range(0, sim.Units.Count).Select(i => sim.Units.Id[i])
            .Single(id => !mine.Contains(id) && sim.Units.Def[Index(sim, id)] == archerDef.Index);
        Assert.Equal(Settlers, sim.Units.Owner[Index(sim, archer)]);
        Assert.Equal((1000 - 70, 200 - 20, 100 - 15), (hall.Stock[(int)Good.Food], hall.Stock[(int)Good.Wood], hall.Stock[(int)Good.Gold]));

        // Out on the open land, away from the camp's guards.
        int ax = TownX - 40, az = TownZ + 30;
        sim.Units.X[Index(sim, archer)] = Fix64.FromInt(ax) + Fix64.Half;
        sim.Units.Z[Index(sim, archer)] = Fix64.FromInt(az) + Fix64.Half;
        int worker = sim.SpawnUnit(Orcs, "worker", ax + 7, az);
        Do(sim, new AttackCommand(Settlers, new[] { archer }, worker));
        int maxHp = sim.Content.Units[sim.Units.Def[Index(sim, worker)]].MaxHp;
        RunUntil(sim, () => sim.Units.Hp[Index(sim, worker)] < maxHp, 10 * Tps, "the archer hits the worker");
        var dx = sim.Units.X[Index(sim, archer)] - sim.Units.X[Index(sim, worker)];
        var dz = sim.Units.Z[Index(sim, archer)] - sim.Units.Z[Index(sim, worker)];
        Assert.True(dx * dx + dz * dz > Fix64.FromInt(16), "the archer rode up to the worker to hit it");
    }

    /// <summary>
    /// The camp is placed in a match on the open grassland, the usual distance away from the other towns, with its
    /// standard and five guards.
    /// </summary>
    [Theory]
    [InlineData(1234UL)]
    [InlineData(7UL)]
    public void EveryMatchHasACentaurCampOnTheGrassland(ulong seed)
    {
        var sim = CavernTests.DefaultMatch(seed);
        var towns = sim.Cities.Towns;
        var camp = Assert.Single(towns, t => t.Def.Id == "centaurs");
        Assert.True(sim.Cities.InBiome(camp.X, camp.Z, Rts.Sim.World.Biome.Grassland), $"the camp at {camp.X},{camp.Z} is not on the grassland");
        int want = sim.World.SizeX * 3 / 16;
        foreach (var other in towns.Where(t => t != camp && !t.Deep))
            Assert.True(IntMath.Distance(other.X, other.Z, camp.X, camp.Z) >= want, $"the camp is close to the {other.Def.Id}");
    }

    /// <summary>The camp is placed on a small map too, with its standard and five guards, away from the other towns.</summary>
    [Fact]
    public void EveryMatchHasACentaurCamp()
    {
        var sim = new Simulation(1234, 512, 64, 512);
        sim.PlaceStartingBuildings();
        sim.SpawnStartingUnits(24);
        sim.SpawnLairs();
        var towns = sim.SpawnCities();
        var camp = Assert.Single(towns, t => t.Def.Id == "centaurs");
        Assert.Equal("centaur_standard", sim.Cities.Totem(camp)!.Def.Id);
        Assert.Equal(5, camp.Guards.Count());
        foreach (var other in towns.Where(t => t != camp))
        {
            long dx = other.X - camp.X, dz = other.Z - camp.Z;
            Assert.True(dx * dx + dz * dz > 48L * 48, $"the camp is close to the {other.Def.Id}");
        }
    }
}
