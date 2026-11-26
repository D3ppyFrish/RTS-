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
/// The forests' giant spiders: nests of three among the trees whose bites web the bitten (they walk at 40% for 3
/// seconds), so a unit cannot run from a nest; cleared, a nest gives a relic of their silk whose bearer webs too.
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
    public void SpidersNestInTheForestsAndWeb()
    {
        var db = ContentDb.Default;
        var spider = db.Unit("giant_spider");
        Assert.Equal(UnitRole.Monster, spider.Role);
        Assert.Equal(Biome.Forest, spider.Lair!.Biome);
        Assert.Equal(3, spider.Lair.Pack);
        Assert.Equal((40, 3 * Tps), (spider.Web!.Pace, spider.Web.Ticks));
        Assert.Contains(spider, db.WildMonsters);
        var silk = db.Relic("spider_silk");
        Assert.NotNull(silk.Web);
        Assert.Same(silk, spider.Lair.PrizeFor(db.Faction("settlers"))!.Relic);
        Assert.Same(silk, spider.Lair.PrizeFor(db.Faction("orcs"))!.Relic);
        Assert.Null(db.Unit("wolf").Web);
    }

    /// <summary>
    /// A spearman that goes at a nest is bitten and webbed: it walks at 40% of its pace while the web holds, so it
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
        RunUntil(sim, () => sim.Combat.IsWebbed(Index(sim, spearman)), 10 * Tps, "a spider bites the spearman");
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

    /// <summary>Machines are not webbed: a spider's bite on a catapult leaves it free.</summary>
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
        Assert.Equal(0, sim.Combat.Webs);
    }

    /// <summary>
    /// Clearing a nest pays the Settlers its reward; at the cleared nest a fighting unit takes up the spider silk,
    /// and its own blows web what they strike (60% for 2 seconds).
    /// </summary>
    [Fact]
    public void TheSilkWebsWhatItsBearerStrikes()
    {
        var sim = Flat();
        var hall = sim.Economy.PlaceFinished(Settlers, sim.Content.Building("town_hall"), 10, 40)!;
        var lair = sim.Monsters.Place(sim.Content.Unit("giant_spider"), 60, 48)!;
        foreach (int s in lair.Pack) sim.Units.Hp[Index(sim, s)] = 1;
        int archer = sim.SpawnUnit(Settlers, "archer", 60, 38);
        sim.Schedule(0, new AttackMoveCommand(Settlers, new[] { archer }, 60, 48));
        RunUntil(sim, () => lair.Cleared, 30 * Tps, "the archer clears the nest");
        sim.Step();
        Assert.Equal((80, 40), (hall.Stock[(int)Good.Food], hall.Stock[(int)Good.Wood]));

        int spearman = sim.SpawnUnit(Settlers, "spearman", lair.X + 1, lair.Z);
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

    /// <summary>The default map has spider nests in its forests, the same for the same seed.</summary>
    [Fact]
    public void TheDefaultMapHasSpiderNests()
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
        var nests = sim.Monsters.Lairs.Where(l => l.Monster.Id == "giant_spider").ToList();
        Assert.NotEmpty(nests);
        foreach (var n in nests) Assert.Equal(Biome.Forest, sim.Map.BiomeAt(n.X, n.Z));
        Assert.Equal(nests.Select(n => (n.X, n.Z)), Match().Monsters.Lairs.Where(l => l.Monster.Id == "giant_spider").Select(n => (n.X, n.Z)));
    }

    [Fact]
    public void WebDataIsCheckedAndHashed()
    {
        var files = Files();
        Edit(files, "units.json", "\"web\": { \"pace\": 40, \"time\": 3 }", "\"web\": { \"pace\": 0, \"time\": 3, \"grip\": 1 }");
        Edit(files, "relics.json", "\"web\": { \"pace\": 60, \"time\": 2 }", "\"web\": 5");
        var e = Assert.Throws<ContentException>(() => ContentLoader.Load(files));
        Assert.Contains(e.Errors, m => m.Contains("unit 'giant_spider', web") && m.Contains("grip"));
        Assert.Contains(e.Errors, m => m.Contains("unit 'giant_spider', web") && m.Contains("pace"));
        Assert.Contains(e.Errors, m => m.Contains("relic 'spider_silk', web") && m.Contains("should be an object"));

        ulong original = ContentDb.Default.Hash;
        files = Files();
        Edit(files, "units.json", "\"web\": { \"pace\": 40, \"time\": 3 }", "\"web\": { \"pace\": 50, \"time\": 3 }");
        Assert.NotEqual(original, ContentLoader.Load(files).Hash);
        files = Files();
        Edit(files, "relics.json", "\"web\": { \"pace\": 60, \"time\": 2 }", "\"web\": { \"pace\": 60, \"time\": 4 }");
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
