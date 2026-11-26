using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Issue #45: a bigger map, with natural hazards and more monsters spread across it to find.</summary>
public class WildsTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Tps = Simulation.TicksPerSecond;
    private const int Settlers = 0, Orcs = 1, Wild = Simulation.WildPlayer;

    private static Simulation Flat(int size = 64) => TestWorlds.Sim(TestWorlds.Flat(size, 16, size, Ground));

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);
    private static int Hp(Simulation sim, int id) => sim.Units.Exists(id) ? sim.Units.Hp[Index(sim, id)] : 0;

    private static void RunUntil(Simulation sim, Func<bool> done, int maxTicks, string what)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return;
            sim.Step();
        }
        Assert.Fail($"Not done after {maxTicks} ticks: {what}");
    }

    /// <summary>A match on the default map, set up as the game starts one.</summary>
    private static Simulation DefaultMatch(ulong seed = 1234, int size = RegionGenerator.DefaultSize)
    {
        var sim = new Simulation(seed, size, RegionGenerator.DefaultHeight, size);
        sim.PlaceStartingBuildings();
        sim.SpawnStartingUnits(24);
        sim.SpawnLairs();
        sim.SpawnCities();
        return sim;
    }

    // --- The map ------------------------------------------------------------------------------------

    /// <summary>
    /// The default map is bigger than the first slice's 512 by 512, and its swamp has patches of poison bog on
    /// dry mud, none of them near a start; the hills still have their pools of lava.
    /// </summary>
    [Fact]
    public void TheDefaultMapIsBiggerAndItsSwampHasPoisonBog()
    {
        Assert.True(RegionGenerator.DefaultSize > 512);
        var map = RegionGenerator.Generate(1234, RegionGenerator.DefaultSize, RegionGenerator.DefaultHeight, RegionGenerator.DefaultSize);
        var w = map.World;
        int bog = 0, lava = 0, swamp = 0;
        for (int z = 0; z < w.SizeZ; z++)
        for (int x = 0; x < w.SizeX; x++)
        {
            if (map.BiomeAt(x, z) == Biome.Swamp) swamp++;
            int top = w.SurfaceHeight(x, z);
            var b = w.Get(x, top, z);
            if (b == BlockType.Lava) lava++;
            if (b != BlockType.Bog) continue;
            bog++;
            Assert.Equal(Biome.Swamp, map.BiomeAt(x, z));
            foreach (var s in map.Starts)
                Assert.True(IntMath.Distance(x, z, s.X, s.Z) > RegionGenerator.StartResourceRadius, $"bog at {x},{z} is near a start");
        }
        Assert.InRange(bog, swamp / 20, swamp / 2);
        Assert.True(lava > 0, "no lava in the hills");
    }

    // --- Hazards ------------------------------------------------------------------------------------

    /// <summary>
    /// A unit standing on poison bog loses <see cref="HazardSystem.BogDamage"/> hit points a second, one beside it
    /// on plain grass nothing; with the bog dug away (it is only a crust on the mud) the ground is safe.
    /// </summary>
    [Fact]
    public void PoisonBogHurtsUnitsStandingOnItEverySecond()
    {
        var sim = Flat();
        sim.World.Set(20, Ground, 20, BlockType.Bog);
        int onBog = sim.SpawnUnit(Settlers, "spearman", 20, 20);
        int beside = sim.SpawnUnit(Settlers, "spearman", 23, 20);
        int full = Hp(sim, onBog);
        Assert.Equal(HazardSystem.BogDamage, sim.Hazards.DamageAt(20, Ground + 1, 20));
        Assert.Equal(0, sim.Hazards.DamageAt(23, Ground + 1, 20));

        sim.Run(3 * Tps);
        Assert.Equal(full - 3 * HazardSystem.BogDamage, Hp(sim, onBog));
        Assert.Equal(full, Hp(sim, beside));

        Assert.True(Harvest.CanGather(BlockType.Bog), "workers dig bog away");
        sim.World.Set(20, Ground, 20, BlockType.Mud);
        int left = Hp(sim, onBog);
        sim.Run(3 * Tps);
        Assert.True(Hp(sim, onBog) >= left, "dug away, the bog hurts no more");
    }

    /// <summary>
    /// Units next to lava, within a column of it, are scorched for <see cref="HazardSystem.LavaDamage"/> a
    /// second; two columns off they are safe. Wild monsters burn too, and a unit left in the heat dies of it with
    /// no one getting the kill.
    /// </summary>
    [Fact]
    public void LavaScorchesUnitsBesideItUntilTheyDie()
    {
        var sim = Flat();
        sim.World.Set(30, Ground, 30, BlockType.Lava);
        sim.World.Set(55, Ground, 8, BlockType.Lava); // a pool of its own for the wolf, out of sight of the workers
        int scorched = sim.SpawnUnit(Settlers, "worker", 31, 31);
        int safe = sim.SpawnUnit(Settlers, "worker", 32, 30);
        int wolf = sim.SpawnUnit(Wild, "wolf", 54, 8);
        int full = Hp(sim, scorched), wolfFull = Hp(sim, wolf);
        Assert.Equal(HazardSystem.LavaDamage, sim.Hazards.DamageAt(31, Ground + 1, 31));
        Assert.Equal(0, sim.Hazards.DamageAt(32, Ground + 1, 30));

        sim.Run(2 * Tps);
        Assert.Equal(full - 2 * HazardSystem.LavaDamage, Hp(sim, scorched));
        Assert.Equal(full, Hp(sim, safe));
        Assert.Equal(wolfFull - 2 * HazardSystem.LavaDamage, Hp(sim, wolf));

        RunUntil(sim, () => !sim.Units.Exists(scorched), (full / HazardSystem.LavaDamage + 2) * Tps, "the worker dies in the heat");
        Assert.True(sim.Units.Exists(safe));
        Assert.Equal(0, sim.Combat.Kills(Wild));
    }

    // --- Monsters across the map --------------------------------------------------------------------

    /// <summary>
    /// Besides the starts' wolf lairs, the dusk tree's warden and the goblins' wolves, the default map has lairs
    /// spread across it: herds of boar in the forests, stone giants in the hills, more wolf packs in the
    /// grassland, bog horrors in the swamp and giant crabs on the beaches, as many as the data's count for each 512 by 512 blocks of map, each well away from the starts,
    /// the towns and every other lair, and the same for the same seed.
    /// </summary>
    [Fact]
    public void LairsAreSpreadAcrossTheMapByBiome()
    {
        var sim = DefaultMatch();
        var fixedLairs = 2 + 1 + sim.Cities.Towns.Count(t => t.LairId > 0);
        // Then those spread across the map; the cavern's come last, underground (see CavernTests).
        var wild = sim.Monsters.Lairs.Skip(fixedLairs).Where(l => l.Monster.Lair!.Cavern == CavernSpot.None).ToList();
        long area = (long)sim.World.SizeX * sim.World.SizeZ;
        foreach (var id in new[] { "boar", "stone_giant", "wolf", "bog_horror", "giant_crab", "giant_spider" })
        {
            var def = sim.Content.Unit(id).Lair!;
            int want = MonsterSystem.WildCount(def.Count, area);
            Assert.Equal((int)(def.Count * area / (512 * 512)), want); // the default map: as many as ever
            int found = wild.Count(l => l.Monster.Id == id);
            Assert.True(found >= 1 && found <= want, $"{found} wild {id} lairs, want 1 to {want}");
        }
        foreach (var lair in wild)
        {
            var def = lair.Monster.Lair!;
            Assert.Equal(def.Biome, sim.Map.BiomeAt(lair.X, lair.Z));
            Assert.Equal(def.Pack, lair.Pack.Count);
            foreach (var s in sim.Map.Starts)
                Assert.True(IntMath.Distance(lair.X, lair.Z, s.X, s.Z) >= MonsterSystem.WildStartGap, $"{lair.Monster.Id} lair near a start");
            foreach (var other in sim.Monsters.Lairs)
                if (other != lair && other.Monster.Lair!.Cavern == CavernSpot.None) Assert.True(IntMath.Distance(lair.X, lair.Z, other.X, other.Z) >= MonsterSystem.WildGap, $"{lair.Monster.Id} lair crowds another");
            foreach (var town in sim.Cities.Towns)
                Assert.True(IntMath.Distance(lair.X, lair.Z, town.X, town.Z) >= MonsterSystem.WildGap);
        }

        var again = DefaultMatch();
        Assert.Equal(sim.Monsters.Lairs.Select(l => (l.Monster.Id, l.X, l.Z)), again.Monsters.Lairs.Select(l => (l.Monster.Id, l.X, l.Z)));
        Assert.Equal(sim.StateHash(), again.StateHash());
    }

    /// <summary>
    /// The land beyond the default map's area is twice as wild: a map up to the default size gets the data's count
    /// for each 512 by 512 blocks, and the large map that many again for every 512 by 512 blocks beyond it.
    /// </summary>
    [Fact]
    public void BigMapsAreWilderBeyondTheDefaultMapsArea()
    {
        long Area(int n) => (long)n * n;
        Assert.Equal(1, MonsterSystem.WildCount(1, Area(512)));
        Assert.Equal(4, MonsterSystem.WildCount(2, Area(RegionGenerator.DefaultSize))); // 2 x 2.25, rounded down
        // The large map: 2.25 blocks of 512 by 512 as before, and its 1.75 more twice over: 5.75 for each one in the data.
        Assert.Equal(5, MonsterSystem.WildCount(1, Area(RegionGenerator.LargeSize)));
        Assert.Equal(11, MonsterSystem.WildCount(2, Area(RegionGenerator.LargeSize)));
        Assert.Equal(17, MonsterSystem.WildCount(3, Area(RegionGenerator.LargeSize)));
    }

    /// <summary>
    /// The large map (see <see cref="RegionGenerator.LargeSize"/>) is made, and has more lairs spread across it than
    /// the default map, every one still well away from the starts.
    /// </summary>
    [Fact]
    public void TheLargeMapHasMoreLairsAwayFromTheStarts()
    {
        var usual = DefaultMatch();
        var large = DefaultMatch(size: RegionGenerator.LargeSize);
        Assert.Equal(RegionGenerator.LargeSize, large.World.SizeX);
        static int Wild(Simulation sim) => sim.Monsters.Lairs.Count(l => l.Monster.Lair!.Biome != null && l.Monster.Lair.Cavern == CavernSpot.None);
        Assert.True(Wild(large) > Wild(usual) * 3 / 2, $"{Wild(large)} lairs on the large map, {Wild(usual)} on the default one");
        foreach (var lair in large.Monsters.Lairs.Where(l => !l.Monster.Lair!.AtStarts && l.Monster.Lair.Cavern == CavernSpot.None && l.Monster.Lair.Biome != null))
            foreach (var s in large.Map.Starts)
                Assert.True(IntMath.Distance(lair.X, lair.Z, s.X, s.Z) >= MonsterSystem.WildStartGap, $"{lair.Monster.Id} lair near a start");
    }

    /// <summary>
    /// The Orcs ride a wild boar they beat to war: a spearman near it climbs on and becomes a rider (the Orcs'
    /// own rider sits on a war boar). Settlers who clear a boar herd get its food.
    /// </summary>
    [Fact]
    public void OrcsRideBeatenBoarsAndSettlersEatThem()
    {
        var sim = Flat(96);
        var lair = sim.Monsters.Place(sim.Content.Unit("boar"), 60, 48)!;
        Assert.Equal(3, lair.Pack.Count);
        int boar = lair.Pack[0];
        int spearman = sim.SpawnUnit(Orcs, "spearman", 58, 48);
        sim.Units.Hp[Index(sim, boar)] = 1;
        sim.Schedule(sim.Tick, new AttackCommand(Orcs, new[] { spearman }, boar));
        RunUntil(sim, () => sim.Units.Owner[Index(sim, boar)] == Orcs, 3 * Tps, "the spearman beats the boar");
        sim.Schedule(sim.Tick, new TameCommand(Orcs, new[] { boar }, TameChoice.Ride));
        sim.Step();
        Assert.False(sim.Units.Exists(boar));
        Assert.Equal("rider", sim.Content.Units[sim.Units.Def[Index(sim, spearman)]].Id);

        var settlers = Flat(96);
        var hall = settlers.Economy.PlaceFinished(Settlers, settlers.Content.Building("town_hall"), 10, 40)!;
        var herd = settlers.Monsters.Place(settlers.Content.Unit("boar"), 60, 48)!;
        foreach (int b in herd.Pack) settlers.Units.Hp[Index(settlers, b)] = 1;
        int archer = settlers.SpawnUnit(Settlers, "archer", 60, 40);
        settlers.Schedule(0, new AttackMoveCommand(Settlers, new[] { archer }, 60, 48));
        RunUntil(settlers, () => herd.Cleared, 30 * Tps, "the archer clears the herd");
        Assert.Equal(200, hall.Stock[(int)Good.Food]);
    }

    /// <summary>
    /// A stone giant guards its ground alone, hits hard and is hard to hurt; beaten, it pays stone and iron.
    /// Every wild monster's lair leaves a den with a prize (see PrizeTests).
    /// </summary>
    [Fact]
    public void StoneGiantsPayStoneAndIron()
    {
        var sim = Flat(96);
        var db = sim.Content;
        Assert.True(db.Unit("stone_giant").MaxHp > 4 * db.Unit("wolf").MaxHp);
        Assert.True(db.Unit("stone_giant").Lair!.Den);
        Assert.True(db.Unit("boar").Lair!.Den);
        Assert.True(db.Unit("wolf").Lair!.Den);
        Assert.False(db.Unit("dusk_warden").Lair!.Den);
        Assert.Equal(new[] { "wolf" }, db.LairMonsters.Select(u => u.Id));
        Assert.Equal(new[] { "wolf", "boar", "stone_giant", "bog_horror", "giant_crab", "shade_stalker", "giant_spider" }, db.WildMonsters.Select(u => u.Id));

        var hall = sim.Economy.PlaceFinished(Settlers, db.Building("town_hall"), 10, 40)!;
        var lair = sim.Monsters.Place(db.Unit("stone_giant"), 60, 48)!;
        int giant = Assert.Single(lair.Pack);
        sim.Units.Hp[Index(sim, giant)] = 1;
        int archer = sim.SpawnUnit(Settlers, "archer", 60, 40);
        sim.Schedule(0, new AttackCommand(Settlers, new[] { archer }, giant));
        RunUntil(sim, () => lair.Cleared, 10 * Tps, "the archer fells the giant");
        sim.Step();
        Assert.Equal((200, 60), (hall.Stock[(int)Good.Stone], hall.Stock[(int)Good.Iron]));
    }

    // --- Data ---------------------------------------------------------------------------------------

    /// <summary>A lair's biome must be one with dry land, and a count needs a biome to spread over.</summary>
    [Fact]
    public void ALairsBiomeAndCountAreChecked()
    {
        var files = Files();
        Edit(files, "units.json", "\"biome\": \"forest\"", "\"biome\": \"lava\"");
        Edit(files, "units.json", "\"biome\": \"hills\", \"count\": 1", "\"count\": 1");
        var e = Assert.Throws<ContentException>(() => ContentLoader.Load(files));
        Assert.Contains(e.Errors, m => m.Contains("unit 'boar', lair") && m.Contains("\"biome\" is \"lava\""));
        Assert.Contains(e.Errors, m => m.Contains("unit 'stone_giant', lair") && m.Contains("\"count\" needs a \"biome\""));
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
