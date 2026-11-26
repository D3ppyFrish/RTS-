using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// The giant spiders: nests of three in caves dug into the hills, a totem at the bottom of each that has to be knocked
/// down to clear the nest, whose bites web the bitten (they walk at 40% for 3 seconds), so a unit cannot run from a nest;
/// cleared, a nest gives a relic of their silk whose bearer webs too.
/// </summary>
public class SpiderTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Tps = Simulation.TicksPerSecond;
    private const int Settlers = 0, Orcs = 1;

    private static Simulation Flat() => TestWorlds.Sim(TestWorlds.Flat(96, 16, 96, Ground));
    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

    private static void RunUntil(Simulation sim, Func<bool> done, int maxTicks, string what)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return;
            sim.Step();
        }
        Assert.Fail($"Not done after {maxTicks} ticks: {what}");
    }

    private static void Bear(Simulation sim, int id, string relic)
    {
        int i = Index(sim, id);
        var def = sim.Content.Relic(relic);
        sim.Units.Relic[i] = (short)(def.Index + 1);
        sim.Units.Hp[i] += def.Hp;
    }

    /// <summary>Columns unit id walks east in the ticks, ordered to a spot far off.</summary>
    private static Fix64 Walked(Simulation sim, int id, int owner, int ticks)
    {
        var x = sim.Units.X[Index(sim, id)];
        sim.Schedule(sim.Tick, new MoveCommand(owner, new[] { id }, 94, sim.Units.Z[Index(sim, id)].FloorToInt()));
        sim.Run(ticks);
        return sim.Units.X[Index(sim, id)] - x;
    }

    [Fact]
    public void SpidersNestInTheHillCavesAndWeb()
    {
        var db = ContentDb.Default;
        var spider = db.Unit("giant_spider");
        Assert.Equal(UnitRole.Monster, spider.Role);
        Assert.Equal(Biome.Hills, spider.Lair!.Biome);
        Assert.True(spider.Lair.Cave);
        Assert.Same(db.Building("spider_totem"), spider.Lair.Totem);
        Assert.Equal(BuildingKind.Totem, spider.Lair.Totem!.Kind);
        Assert.Null(db.Unit("wolf").Lair!.Totem);
        Assert.Equal(3, spider.Lair.Pack);
        Assert.Equal((40, 3 * Tps), (spider.Web!.Pace, spider.Web.Ticks));
        Assert.Equal((Fix64.FromInt(8), 6 * Tps), (spider.Web.Range, spider.Web.EveryTicks));
        Assert.Equal((2, 6 * Tps), (spider.Poison!.PerSecond, spider.Poison.Ticks));
        Assert.Equal(Fix64.Zero, db.Relic("spider_silk").Web!.Range);
        Assert.Contains(spider, db.WildMonsters);
        var silk = db.Relic("spider_silk");
        Assert.NotNull(silk.Web);
        Assert.Same(silk, spider.Lair.PrizeFor(db.Faction("settlers"))!.Relic);
        Assert.Same(silk, spider.Lair.PrizeFor(db.Faction("orcs"))!.Relic);
        Assert.Null(db.Unit("wolf").Web);
    }

    /// <summary>
    /// A spider spits its web at a unit out of its bite, up to 8 blocks off: the unit is webbed with no blow struck, and
    /// the spider spits no more for 6 seconds.
    /// </summary>
    [Fact]
    public void ASpiderSpitsItsWebFromAfar()
    {
        var sim = Flat();
        var lair = sim.Monsters.Place(sim.Content.Unit("giant_spider"), 60, 48)!;
        int spearman = sim.SpawnUnit(Settlers, "spearman", 54, 48);
        int i = Index(sim, spearman);
        int hp = sim.Units.Hp[i];
        RunUntil(sim, () => sim.Combat.IsWebbed(Index(sim, spearman)), 5 * Tps, "a spider spits at the spearman");
        i = Index(sim, spearman);
        Assert.Equal(1, sim.Combat.Spat);
        Assert.Equal(hp, sim.Units.Hp[i]);
        Assert.False(sim.Combat.IsPoisoned(i));
        var shot = Assert.Single(sim.Combat.Shots, s => s.Web);
        int spitter = sim.Units.IndexOf(shot.Attacker);
        Assert.Contains(shot.Attacker, lair.Pack);
        var dx = sim.Units.X[spitter] - sim.Units.X[i];
        var dz = sim.Units.Z[spitter] - sim.Units.Z[i];
        Assert.True(dx * dx + dz * dz > Fix64.FromRatio(25, 16), "spat from further off than a bite");
        Assert.InRange(sim.Units.SpitWait[spitter], 6 * Tps - 2, 6 * Tps);
    }

    /// <summary>
    /// A spider's bite poisons: the bitten loses 2 hit points a second for 6 seconds after the last bite, and a healer's
    /// work draws the poison out with the bleeding.
    /// </summary>
    [Fact]
    public void ABitePoisons()
    {
        var sim = Flat();
        var lair = sim.Monsters.Place(sim.Content.Unit("giant_spider"), 60, 48)!;
        int spearman = sim.SpawnUnit(Settlers, "spearman", 57, 48);
        sim.Units.Hp[Index(sim, spearman)] = 10000;
        RunUntil(sim, () => sim.Combat.IsPoisoned(Index(sim, spearman)), 10 * Tps, "a spider bites the spearman");
        Assert.True(sim.Combat.Poisonings > 0);
        Assert.Equal(2, sim.Units.PoisonRate[Index(sim, spearman)]);

        // The nest gone, the poison works on: 2 a second while it lasts, then nothing.
        foreach (int id in lair.Pack.ToArray()) sim.Units.RemoveAt(Index(sim, id));
        int i = Index(sim, spearman);
        sim.Units.StopBleeding(i);
        sim.Units.Poisoned[i] = 6 * Tps;
        int before = sim.Units.Hp[i];
        sim.Run(6 * Tps);
        i = Index(sim, spearman);
        Assert.False(sim.Combat.IsPoisoned(i));
        Assert.InRange(before - sim.Units.Hp[i], 10, 12);
        int after = sim.Units.Hp[i];
        sim.Run(2 * Tps);
        Assert.True(sim.Units.Hp[Index(sim, spearman)] >= after);
    }

    /// <summary>
    /// A spearman that goes at a nest is webbed: it walks at 40% of its pace while the web holds, so it
    /// cannot run, and the web wears off after 3 seconds. The spider itself walks on silk.
    /// </summary>
    [Fact]
    public void ABiteWebsTheBittenAndItWearsOff()
    {
        var sim = Flat();
        var lair = sim.Monsters.Place(sim.Content.Unit("giant_spider"), 60, 48)!;
        Assert.Equal(3, lair.Pack.Count);
        int spearman = sim.SpawnUnit(Settlers, "spearman", 56, 48);
        sim.Units.Hp[Index(sim, spearman)] = 10000; // lives through the bites
        RunUntil(sim, () => sim.Combat.IsWebbed(Index(sim, spearman)), 10 * Tps, "a spider webs the spearman");
        Assert.True(sim.Combat.Webs > 0);
        Assert.Equal(40, sim.Units.WebPace[Index(sim, spearman)]);
        foreach (int s in lair.Pack) Assert.False(sim.Combat.IsWebbed(Index(sim, s)));

        // The nest is gone (out of the way); the webbed spearman walks off at less than half the pace of one free.
        foreach (int id in lair.Pack.ToArray()) sim.Units.RemoveAt(Index(sim, id));
        int free = sim.SpawnUnit(Settlers, "spearman", 40, 30);
        sim.Units.Webbed[Index(sim, spearman)] = 3 * Tps; // a full web, to measure a whole second inside it
        var slow = Walked(sim, spearman, Settlers, Tps);
        var fast = Walked(sim, free, Settlers, Tps);
        Assert.True(slow > Fix64.Zero && slow * 2 < fast, $"webbed {slow}, free {fast}");

        RunUntil(sim, () => !sim.Combat.IsWebbed(Index(sim, spearman)), 3 * Tps, "the web wears off");
        Assert.Equal(0, sim.Units.WebPace[Index(sim, spearman)]);
        // Out of the web, it walks as fast as the other.
        var after = Walked(sim, spearman, Settlers, Tps);
        Assert.True(after * 10 >= fast * 9, $"after the web {after}, free {fast}");
    }

    /// <summary>Machines are not webbed nor poisoned: a spider's bite on a catapult leaves it free and whole but for the bite.</summary>
    [Fact]
    public void MachinesAreNotWebbed()
    {
        var sim = Flat();
        var lair = sim.Monsters.Place(sim.Content.Unit("giant_spider"), 60, 48)!;
        int catapult = sim.SpawnUnit(Settlers, "catapult", 56, 48);
        sim.Units.Hp[Index(sim, catapult)] = 10000;
        int hp = sim.Units.Hp[Index(sim, catapult)];
        RunUntil(sim, () => sim.Units.Hp[Index(sim, catapult)] < hp, 10 * Tps, "a spider bites the catapult");
        sim.Step();
        Assert.False(sim.Combat.IsWebbed(Index(sim, catapult)));
        Assert.False(sim.Combat.IsPoisoned(Index(sim, catapult)));
        Assert.Equal(0, sim.Combat.Poisonings);
        Assert.Equal(0, sim.Combat.Webs);
    }

    /// <summary>
    /// A nest's totem stands on its den, the wild side's. Killing the spiders to the last does not clear the nest: it is
    /// cleared when the totem is knocked down, and the Settlers who knocked it down get the reward. At the cleared nest a
    /// fighting unit takes up the spider silk, and its own blows web what they strike (60% for 2 seconds).
    /// </summary>
    [Fact]
    public void TheSilkWebsWhatItsBearerStrikes()
    {
        var sim = Flat();
        var hall = sim.Economy.PlaceFinished(Settlers, sim.Content.Building("town_hall"), 10, 40)!;
        var lair = sim.Monsters.Place(sim.Content.Unit("giant_spider"), 60, 48)!;
        var totem = sim.Monsters.TotemOf(lair)!;
        Assert.Equal((Simulation.WildPlayer, "spider_totem"), (totem.Owner, totem.Def.Id));
        Assert.True(totem.Covers(60, 48));
        foreach (int s in lair.Pack) sim.Units.Hp[Index(sim, s)] = 1;
        int archer = sim.SpawnUnit(Settlers, "archer", 60, 38);
        sim.Schedule(0, new AttackMoveCommand(Settlers, new[] { archer }, 60, 44));
        RunUntil(sim, () => lair.Pack.Count == 0, 30 * Tps, "the archer kills the spiders");
        sim.Run(2 * Tps);
        Assert.False(lair.Cleared);
        Assert.Equal(0, hall.Stock[(int)Good.Food]);

        sim.Schedule(sim.Tick, new AttackBuildingCommand(Settlers, new[] { archer }, totem.Id));
        RunUntil(sim, () => lair.Cleared, 90 * Tps, "the archer knocks the totem down");
        Assert.Equal(Settlers, lair.ClearedBy);
        Assert.True(sim.Monsters.TotemDown(lair));
        sim.Step();
        Assert.Equal((80, 40), (hall.Stock[(int)Good.Food], hall.Stock[(int)Good.Wood]));

        int spearman = sim.SpawnUnit(Settlers, "spearman", lair.X + 2, lair.Z);
        sim.Schedule(sim.Tick, new DenCommand(Settlers, new[] { spearman }, lair.Id));
        sim.Step();
        var silk = sim.Content.Relic("spider_silk");
        Assert.Same(silk, sim.RelicOf(Index(sim, spearman)));

        var bearer = Flat();
        int orc = bearer.SpawnUnit(Orcs, "spearman", 40, 40);
        Bear(bearer, orc, "spider_silk");
        int foe = bearer.SpawnUnit(Settlers, "spearman", 42, 40);
        bearer.Units.Hp[Index(bearer, foe)] = 10000;
        bearer.Schedule(0, new AttackCommand(Orcs, new[] { orc }, foe));
        RunUntil(bearer, () => bearer.Combat.IsWebbed(Index(bearer, foe)), 10 * Tps, "the silk's bearer webs its foe");
        Assert.Equal(60, bearer.Units.WebPace[Index(bearer, foe)]);
    }

    /// <summary>
    /// Silk knives (a tech after Better tools): the player's units cut themselves out of webs. A spearman of theirs
    /// bitten at a nest takes the bite but walks on at its pace, and a spider silk's bearer cannot web it either; a
    /// rival without the tech is still webbed.
    /// </summary>
    [Fact]
    public void SilkKnivesCutTheirUnitsFree()
    {
        var knives = ContentDb.Default.Techs.First(t => t.Id == "silk_knives");
        Assert.Equal(TechEffectType.CutWebs, Assert.Single(knives.Effects).Type);
        Assert.Contains(knives.Requires, r => r.Id == "tools");
        Assert.Equal(Discovery.None, knives.Discovery);
        Assert.Null(knives.Faction);

        var sim = Flat();
        sim.Economy.Grant(Settlers, knives);
        Assert.True(sim.Economy.CutsWebs(Settlers));
        Assert.False(sim.Economy.CutsWebs(Orcs));
        sim.Monsters.Place(sim.Content.Unit("giant_spider"), 60, 48);
        int spearman = sim.SpawnUnit(Settlers, "spearman", 56, 48);
        sim.Units.Hp[Index(sim, spearman)] = 10000;
        int hp = sim.Units.Hp[Index(sim, spearman)];
        RunUntil(sim, () => sim.Units.Hp[Index(sim, spearman)] < hp - 30, 20 * Tps, "the spiders bite the spearman");
        Assert.False(sim.Combat.IsWebbed(Index(sim, spearman)));
        Assert.Equal(0, sim.Combat.Webs);

        // Against a bearer of the silk: the Settlers' spearman is not webbed, the Orcs' own foe without knives is.
        var bearer = Flat();
        bearer.Economy.Grant(Settlers, knives);
        int orc = bearer.SpawnUnit(Orcs, "spearman", 40, 40);
        Bear(bearer, orc, "spider_silk");
        int foe = bearer.SpawnUnit(Settlers, "spearman", 42, 40);
        bearer.Units.Hp[Index(bearer, foe)] = 10000;
        hp = bearer.Units.Hp[Index(bearer, foe)];
        bearer.Schedule(0, new AttackCommand(Orcs, new[] { orc }, foe));
        RunUntil(bearer, () => bearer.Units.Hp[Index(bearer, foe)] < hp - 20, 20 * Tps, "the silk's bearer strikes its foe");
        Assert.False(bearer.Combat.IsWebbed(Index(bearer, foe)));
        Assert.Equal(0, bearer.Combat.Webs);

        var rival = Flat();
        rival.Economy.Grant(Orcs, knives); // the bearer's own side has the knives: that does not shield the foe
        int orc2 = rival.SpawnUnit(Orcs, "spearman", 40, 40);
        Bear(rival, orc2, "spider_silk");
        int foe2 = rival.SpawnUnit(Settlers, "spearman", 42, 40);
        rival.Units.Hp[Index(rival, foe2)] = 10000;
        rival.Schedule(0, new AttackCommand(Orcs, new[] { orc2 }, foe2));
        RunUntil(rival, () => rival.Combat.IsWebbed(Index(rival, foe2)), 10 * Tps, "the bearer webs a foe without knives");
    }

    /// <summary>
    /// The totem knocked down while spiders are still about clears the nest all the same, paying whoever knocked it
    /// down; the spiders left go on guarding the den.
    /// </summary>
    [Fact]
    public void KnockingTheTotemDownClearsTheNestWithSpidersLeft()
    {
        var sim = Flat();
        var hall = sim.Economy.PlaceFinished(Settlers, sim.Content.Building("town_hall"), 10, 40)!;
        var lair = sim.Monsters.Place(sim.Content.Unit("giant_spider"), 60, 48)!;
        var totem = sim.Monsters.TotemOf(lair)!;
        var ids = new List<int>();
        for (int k = 0; k < 6; k++)
        {
            int id = sim.SpawnUnit(Settlers, "spearman", 56, 46 + k);
            sim.Units.Hp[Index(sim, id)] = 100000; // shrugs off the bites
            ids.Add(id);
        }
        sim.Schedule(0, new AttackBuildingCommand(Settlers, ids.ToArray(), totem.Id));
        // The spiders are put out of the fight (but kept) so that only the totem is hit.
        foreach (int s in lair.Pack) sim.Units.Hp[Index(sim, s)] = 100000;
        RunUntil(sim, () => lair.Cleared, 120 * Tps, "the spearmen knock the totem down");
        Assert.Equal(Settlers, lair.ClearedBy);
        Assert.Equal(3, lair.Pack.Count);
        sim.Step();
        Assert.Equal(80, hall.Stock[(int)Good.Food]);

        // The spiders still go after a newcomer by the den.
        int scout = sim.SpawnUnit(Settlers, "spearman", 64, 48);
        sim.Units.Hp[Index(sim, scout)] = 100000;
        RunUntil(sim, () => lair.Pack.Any(s => sim.Units.Target[Index(sim, s)] != 0), 10 * Tps, "a spider goes after the scout");
    }

    /// <summary>
    /// The default map's hill caves: in each hill range, a tunnel into the hillside that climbs down to a chamber at
    /// the bottom, under solid ground. A spider nest in each of them, its den and totem at the back of the chamber,
    /// below the mouth; a unit can walk from the mouth down to the den. The same for the same seed, and kept in a map file.
    /// </summary>
    [Fact]
    public void TheDefaultMapHasSpiderNestsInHillCaves()
    {
        Simulation Match()
        {
            var sim = new Simulation(1234, RegionGenerator.DefaultSize, RegionGenerator.DefaultHeight, RegionGenerator.DefaultSize);
            sim.PlaceStartingBuildings();
            sim.SpawnStartingUnits(24);
            sim.SpawnLairs();
            sim.SpawnCities();
            return sim;
        }
        var sim = Match();
        var map = sim.Map;
        Assert.Equal(RegionGenerator.HillCavesPerRange * RegionGenerator.FeatureSets(sim.World.SizeX, sim.World.SizeZ), map.HillCaves.Count);
        Assert.Equal(map.HillCaves.Count, map.HillCaveMouths.Count);
        var nests = sim.Monsters.Lairs.Where(l => l.Monster.Id == "giant_spider").ToList();
        Assert.NotEmpty(nests);
        foreach (var n in nests)
        {
            int k = map.HillCaves.FindIndex(c => IntMath.Distance(c.X, c.Z, n.X, n.Z) <= RegionGenerator.HillCaveChamber);
            Assert.True(k >= 0, $"the nest at {n.X},{n.Z} is in no hill cave");
            var (cx, cy, cz) = map.HillCaves[k];
            var mouth = map.HillCaveMouths[k];
            Assert.Equal(Biome.Hills, map.BiomeAt(n.X, n.Z));
            Assert.Equal(cy, n.Y);
            Assert.True(mouth.Y > n.Y + 4, $"the mouth at {mouth} is not well above the bottom at {n.Y}");
            // Under the hill: solid ground over the chamber.
            Assert.True(Walk.GroundLevel(sim.World, cx, cz) > cy + RegionGenerator.HillCaveHeadroom + RegionGenerator.HillCaveRoof);
            var totem = sim.Monsters.TotemOf(n)!;
            Assert.Equal(n.Y, totem.Y);
            Assert.True(totem.Covers(n.X, n.Z));
            Assert.Equal(3, n.Pack.Count);
            Assert.True(Reaches(sim.World, mouth, (n.X, n.Y, n.Z)), $"no way from the mouth at {mouth} down to the den at {n.X},{n.Y},{n.Z}");
        }
        Assert.Equal(nests.Count, sim.Monsters.Lairs.Count(l => l.Monster.Id == "giant_spider" && l.Totem != 0));
        var ms = new System.IO.MemoryStream();
        map.Save(ms);
        ms.Position = 0;
        var loaded = RegionMap.Load(ms);
        Assert.Equal(map.HillCaves, loaded.HillCaves);
        Assert.Equal(map.HillCaveMouths, loaded.HillCaveMouths);
        Assert.Equal(nests.Select(n => (n.X, n.Z)), Match().Monsters.Lairs.Where(l => l.Monster.Id == "giant_spider").Select(n => (n.X, n.Z)));
    }

    /// <summary>True if a unit can walk from <paramref name="from"/> to within a column of the den at <paramref name="to"/>.</summary>
    private static bool Reaches(VoxelWorld w, (int X, int Y, int Z) from, (int X, int Y, int Z) to)
    {
        var seen = new HashSet<(int, int, int)> { from };
        var open = new Queue<(int X, int Y, int Z)>();
        open.Enqueue(from);
        while (open.Count > 0)
        {
            var (x, y, z) = open.Dequeue();
            if (Math.Abs(x - to.X) <= 2 && Math.Abs(z - to.Z) <= 2 && Math.Abs(y - to.Y) <= 1) return true;
            foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = x + dx, nz = z + dz;
                if (Math.Abs(nx - from.X) > 80 || Math.Abs(nz - from.Z) > 80) continue;
                if (!Walk.TryStep(w, x, z, y, nx, nz, out int ny)) continue;
                if (seen.Add((nx, ny, nz))) open.Enqueue((nx, ny, nz));
            }
        }
        return false;
    }

    [Fact]
    public void WebDataIsCheckedAndHashed()
    {
        var files = Files();
        Edit(files, "units.json", "\"web\": { \"pace\": 40, \"time\": 3, \"range\": 8, \"every\": 6 }, \"poison\": { \"damage\": 2, \"time\": 6 }",
            "\"web\": { \"pace\": 0, \"time\": 3, \"grip\": 1, \"every\": 6 }, \"poison\": { \"damage\": 0 }");
        Edit(files, "relics.json", "\"web\": { \"pace\": 60, \"time\": 2 }", "\"web\": 5");
        var e = Assert.Throws<ContentException>(() => ContentLoader.Load(files));
        Assert.Contains(e.Errors, m => m.Contains("unit 'giant_spider', web") && m.Contains("grip"));
        Assert.Contains(e.Errors, m => m.Contains("unit 'giant_spider', web") && m.Contains("pace"));
        Assert.Contains(e.Errors, m => m.Contains("unit 'giant_spider', web") && m.Contains("\"every\" needs a \"range\""));
        Assert.Contains(e.Errors, m => m.Contains("unit 'giant_spider', poison") && m.Contains("damage"));
        Assert.Contains(e.Errors, m => m.Contains("relic 'spider_silk', web") && m.Contains("should be an object"));

        files = Files();
        Edit(files, "units.json", "\"totem\": \"spider_totem\"", "\"totem\": \"town_hall\"");
        e = Assert.Throws<ContentException>(() => ContentLoader.Load(files));
        Assert.Contains(e.Errors, m => m.Contains("giant_spider") && m.Contains("not of kind totem"));
        files = Files();
        Edit(files, "units.json", "\"biome\": \"hills\", \"count\": 2, \"cave\": true,", "\"cave\": true,");
        e = Assert.Throws<ContentException>(() => ContentLoader.Load(files));
        Assert.Contains(e.Errors, m => m.Contains("giant_spider") && m.Contains("\"cave\") needs a \"biome\""));
        Assert.Contains(e.Errors, m => m.Contains("giant_spider") && m.Contains("\"totem\" needs a \"biome\""));

        ulong original = ContentDb.Default.Hash;
        files = Files();
        Edit(files, "units.json", "\"web\": { \"pace\": 40, \"time\": 3,", "\"web\": { \"pace\": 50, \"time\": 3,");
        Assert.NotEqual(original, ContentLoader.Load(files).Hash);
        files = Files();
        Edit(files, "units.json", "\"range\": 8, \"every\": 6 }", "\"range\": 9, \"every\": 6 }");
        Assert.NotEqual(original, ContentLoader.Load(files).Hash);
        files = Files();
        Edit(files, "units.json", "\"poison\": { \"damage\": 2, \"time\": 6 }", "\"poison\": { \"damage\": 3, \"time\": 6 }");
        Assert.NotEqual(original, ContentLoader.Load(files).Hash);
        files = Files();
        Edit(files, "relics.json", "\"web\": { \"pace\": 60, \"time\": 2 }", "\"web\": { \"pace\": 60, \"time\": 4 }");
        Assert.NotEqual(original, ContentLoader.Load(files).Hash);
        files = Files();
        Edit(files, "units.json", "\"cave\": true, \"totem\": \"spider_totem\",", "");
        Assert.NotEqual(original, ContentLoader.Load(files).Hash);
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
