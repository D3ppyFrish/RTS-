using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Issue #65: monsters in the cavern (cave crawlers and the cave wyrm), and packs that roam.</summary>
public class CavernTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Tps = Simulation.TicksPerSecond;
    private const int Settlers = 0, Orcs = 1, Wild = Simulation.WildPlayer;

    private static Simulation Flat(int size = 64) => TestWorlds.Sim(TestWorlds.Flat(size, 16, size, Ground));

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

    /// <summary>A match on the default map, set up as the game starts one.</summary>
    internal static Simulation DefaultMatch(ulong seed = 1234)
    {
        var sim = new Simulation(seed, RegionGenerator.DefaultSize, RegionGenerator.DefaultHeight, RegionGenerator.DefaultSize);
        sim.PlaceStartingBuildings();
        sim.SpawnStartingUnits(24);
        sim.SpawnLairs();
        sim.SpawnCities();
        return sim;
    }

    // --- The cavern ---------------------------------------------------------------------------------

    /// <summary>
    /// The default map's cavern has three nests of five cave crawlers spread along it and a cave wyrm alone in its
    /// deepest chamber: every den on the cave floor, under the roof, and the wyrm's furthest in on foot from the way
    /// in. Its second cavern has crawler nests only. Same seed, same lairs.
    /// </summary>
    [Fact]
    public void TheCavernHasCrawlerNestsAndAWyrmAtItsFarEnd()
    {
        var sim = DefaultMatch();
        var db = sim.Content;
        Assert.Equal(new[] { "cave_crawler", "cave_wyrm" }, db.CavernMonsters.Select(u => u.Id));
        Assert.DoesNotContain(db.Unit("cave_crawler"), db.WildMonsters);
        Assert.DoesNotContain(db.Unit("cave_crawler"), db.LairMonsters);
        var cave = sim.Map.Cavern!.Value;
        var all = sim.Monsters.Lairs.Where(l => l.Monster.Lair!.Cavern != CavernSpot.None).ToList();
        // The main cavern's lairs come first; the default map's second cavern (issue #67, under its second hill
        // range) has three more crawler nests but no wyrm.
        var cavern = all.Take(4).ToList();
        Assert.Equal(new[] { "cave_crawler", "cave_crawler", "cave_crawler", "cave_wyrm" }, cavern.Select(l => l.Monster.Id));
        Assert.Equal(2, sim.Map.Caverns.Count);
        // (The deep caves' crawler nests come after them, see DeepCaveTests.)
        var second = all.Skip(4).Take(3).ToList();
        Assert.Equal(new[] { "cave_crawler", "cave_crawler", "cave_crawler" }, second.Select(l => l.Monster.Id));
        var (c2x, c2y, c2z) = sim.Map.Caverns[1];
        var floor2 = sim.Monsters.CavernFloor(c2x, c2y, c2z, c2y - MonsterSystem.CavernDrop);
        foreach (var lair in second)
            Assert.Contains(floor2, c => c.X == lair.X && c.Z == lair.Z && c.Y == lair.Y);

        var floor = sim.Monsters.CavernFloor(cave.X, cave.Y, cave.Z, cave.Y - MonsterSystem.CavernDrop);
        int Steps(Lair l) => floor.First(c => c.X == l.X && c.Z == l.Z && c.Y == l.Y).Steps;
        foreach (var lair in cavern)
        {
            Assert.True(Walk.GroundLevel(sim.World, lair.X, lair.Z) > lair.Y + 2, $"{lair.Monster.Id} den at {lair.X},{lair.Z} is not underground");
            Assert.InRange(lair.Pack.Count, 1, lair.Monster.Lair!.Pack);
            foreach (int id in lair.Pack)
                Assert.True(Math.Abs(sim.Units.Y[Index(sim, id)] - lair.Y) <= MonsterSystem.GuardHeight);
        }
        Assert.Equal(5, cavern.Take(3).Sum(l => l.Pack.Count) / 3);
        var steps = cavern.Select(Steps).ToList();
        Assert.True(steps[0] < steps[1] && steps[1] < steps[2] && steps[2] < steps[3], $"not spread along the cavern: {string.Join(", ", steps)}");
        Assert.Equal(floor.Max(c => c.Steps), steps[3]);

        var again = DefaultMatch();
        Assert.Equal(sim.Monsters.Lairs.Select(l => (l.Monster.Id, l.X, l.Y, l.Z)), again.Monsters.Lairs.Select(l => (l.Monster.Id, l.X, l.Y, l.Z)));
        Assert.Equal(sim.StateHash(), again.StateHash());
        // A small map's cavern (test and scripted maps) has none.
        var small = new Simulation(1234, 256, 64, 256);
        small.PlaceStartingBuildings();
        small.SpawnLairs();
        small.SpawnCities();
        Assert.NotNull(small.Map.Cavern);
        Assert.DoesNotContain(small.Monsters.Lairs, l => l.Monster.Lair!.Cavern != CavernSpot.None);
    }

    /// <summary>
    /// Cave crawlers swarm: the whole nest goes for a unit that comes near, and a lone spearman goes down under
    /// them. Settlers who clear a nest get its iron and coal.
    /// </summary>
    [Fact]
    public void CaveCrawlersSwarmAndPayIronAndCoal()
    {
        var sim = Flat(96);
        var db = sim.Content;
        var nest = sim.Monsters.Place(db.Unit("cave_crawler"), 60, 48)!;
        Assert.Equal(5, nest.Pack.Count);
        Assert.False(db.Unit("cave_crawler").Lair!.Den);
        int spearman = sim.SpawnUnit(Settlers, "spearman", 52, 48);
        RunUntil(sim, () => nest.Pack.All(id => sim.Units.Target[Index(sim, id)] == sim.Units.Id[Index(sim, spearman)]), 2 * Tps,
            "the whole nest goes for the spearman");
        RunUntil(sim, () => !sim.Units.Exists(spearman), 30 * Tps, "the swarm brings the spearman down");

        var settlers = Flat(96);
        var hall = settlers.Economy.PlaceFinished(Settlers, settlers.Content.Building("town_hall"), 10, 40)!;
        var crawlers = settlers.Monsters.Place(settlers.Content.Unit("cave_crawler"), 60, 48)!;
        foreach (int c in crawlers.Pack) settlers.Units.Hp[Index(settlers, c)] = 1;
        var archers = Enumerable.Range(0, 4).Select(k => settlers.SpawnUnit(Settlers, "archer", 57 + 2 * k, 40)).ToArray();
        settlers.Schedule(0, new AttackMoveCommand(Settlers, archers, 60, 48));
        RunUntil(settlers, () => crawlers.Cleared, 30 * Tps, "the archers clear the nest");
        Assert.Equal((30, 40), (hall.Stock[(int)Good.Iron], hall.Stock[(int)Good.Coal]));
    }

    /// <summary>
    /// The cave wyrm is the cavern's boss: far tougher than a stone giant. Settlers who kill it get stone and iron,
    /// and one of their fighters takes up the wyrm crystal at its den (more hit points, damage and armour); Orcs beat
    /// it and keep it, and take up the crystal too.
    /// </summary>
    [Fact]
    public void TheCaveWyrmLeavesItsCrystal()
    {
        var sim = Flat(96);
        var db = sim.Content;
        Assert.True(db.Unit("cave_wyrm").MaxHp > db.Unit("stone_giant").MaxHp);
        var crystal = db.Relic("wyrm_crystal");
        Assert.True(crystal.Hp > 0 && crystal.Damage > 0 && crystal.Armour > 0);

        var hall = sim.Economy.PlaceFinished(Settlers, db.Building("town_hall"), 10, 40)!;
        var lair = sim.Monsters.Place(db.Unit("cave_wyrm"), 60, 48)!;
        int wyrm = Assert.Single(lair.Pack);
        sim.Units.Hp[Index(sim, wyrm)] = 1;
        int archer = sim.SpawnUnit(Settlers, "archer", 60, 36);
        sim.Schedule(0, new AttackCommand(Settlers, new[] { archer }, wyrm));
        RunUntil(sim, () => lair.Cleared, 10 * Tps, "the archer kills the wyrm");
        sim.Step();
        Assert.Equal((150, 120), (hall.Stock[(int)Good.Stone], hall.Stock[(int)Good.Iron]));
        int full = sim.Units.Hp[Index(sim, archer)];
        sim.Schedule(sim.Tick, new MoveCommand(Settlers, new[] { archer }, 60, 46));
        RunUntil(sim, () => sim.Monsters.CanUseDen(Settlers, lair, new[] { Index(sim, archer) }), 10 * Tps, "the archer reaches the den");
        sim.Schedule(sim.Tick, new DenCommand(Settlers, new[] { archer }, lair.Id));
        sim.Step();
        Assert.Same(crystal, sim.RelicOf(Index(sim, archer)));
        Assert.Equal(full + crystal.Hp, sim.Units.Hp[Index(sim, archer)]);

        var orcs = Flat(96);
        var den = orcs.Monsters.Place(orcs.Content.Unit("cave_wyrm"), 60, 48)!;
        int beast = den.Pack[0];
        orcs.Units.Hp[Index(orcs, beast)] = 1;
        int spearman = orcs.SpawnUnit(Orcs, "spearman", 58, 48);
        orcs.Schedule(0, new AttackCommand(Orcs, new[] { spearman }, beast));
        RunUntil(orcs, () => orcs.Units.Owner[Index(orcs, beast)] == Orcs, 5 * Tps, "the spearman beats the wyrm");
        Assert.True(den.Cleared);
        Assert.Same(orcs.Content.Relic("wyrm_crystal"), orcs.Monsters.DenUse(Orcs, den)!.Relic);
    }

    // --- Roaming ------------------------------------------------------------------------------------

    /// <summary>
    /// On the default map the wolf packs and boar herds spread across it roam; the starts' wolves, the dusk tree's
    /// warden, the city-states' monsters and everything else stay home.
    /// </summary>
    [Fact]
    public void WildWolvesAndBoarsRoamAndTheStartsLairsDoNot()
    {
        var sim = DefaultMatch();
        var wild = sim.Monsters.Lairs.Where(l => l.Roam > 0).ToList();
        Assert.Contains(wild, l => l.Monster.Id == "wolf");
        Assert.Contains(wild, l => l.Monster.Id == "boar");
        Assert.All(wild, l => Assert.Equal(l.Monster.Lair!.Roam, l.Roam));
        Assert.All(wild, l => Assert.True(l.Monster.Lair!.Biome != null && sim.Map.Starts.All(s => IntMath.Distance(l.X, l.Z, s.X, s.Z) >= MonsterSystem.WildStartGap)));
        var startLairs = sim.Monsters.Lairs.Take(sim.Map.Starts.Count).ToList();
        Assert.All(startLairs, l => Assert.Equal(0, l.Roam));
        Assert.All(sim.Monsters.Lairs.Where(l => l.Monster.Id is not ("wolf" or "boar")), l => Assert.Equal(0, l.Roam));
        Assert.All(sim.Cities.Towns.Where(t => t.LairId > 0), t => Assert.Equal(0, sim.Monsters.GetLair(t.LairId)!.Roam));
    }

    /// <summary>
    /// A roaming wolf pack wanders: every <see cref="MonsterSystem.RoamSeconds"/> it moves on together to another spot
    /// within its roam of the den, and it goes after a worker near where it is now, though the worker is far
    /// outside the den's guard radius. A pack that stays home never moves on.
    /// </summary>
    [Fact]
    public void ARoamingPackWandersAndGoesAfterWorkersWhereverItIs()
    {
        var sim = Flat(160);
        var def = sim.Content.Unit("wolf").Lair!;
        var lair = sim.Monsters.Place(sim.Content.Unit("wolf"), 80, 80)!;
        var stay = sim.Monsters.Place(sim.Content.Unit("wolf"), 20, 20)!;
        sim.Monsters.StartRoaming(lair);
        Assert.Equal(def.Roam, lair.Roam);

        var spots = new HashSet<(int, int)>();
        for (int s = 0; s < 4 * MonsterSystem.RoamSeconds; s++)
        {
            sim.Run(Tps);
            spots.Add((lair.HomeX, lair.HomeZ));
            Assert.True(IntMath.Distance(lair.HomeX, lair.HomeZ, lair.X, lair.Z) <= lair.Roam);
        }
        Assert.True(spots.Count >= 3, $"the pack stayed in {spots.Count} spots");
        Assert.Equal((stay.X, stay.Z), (stay.HomeX, stay.HomeZ));
        // The pack is where it wandered to, not at the den.
        foreach (int id in lair.Pack)
        {
            int i = Index(sim, id);
            Assert.True(IntMath.Distance(sim.Units.X[i].FloorToInt(), sim.Units.Z[i].FloorToInt(), lair.HomeX, lair.HomeZ) <= def.Leash);
        }

        // Wait for it to move on to a spot well away from the den, then a worker comes by there.
        RunUntil(sim, () => IntMath.Distance(lair.HomeX, lair.HomeZ, lair.X, lair.Z) > def.Guard + 6, 20 * MonsterSystem.RoamSeconds * Tps,
            "the pack wanders well away from its den");
        sim.Run(8 * Tps);
        int hx = lair.HomeX, hz = lair.HomeZ;
        int wx = Math.Clamp(hx + (hx > lair.X ? 4 : -4), 1, 158), wz = hz;
        Assert.True(IntMath.Distance(wx, wz, lair.X, lair.Z) > def.Guard, "the worker is outside the den's guard radius");
        int worker = sim.SpawnUnit(Settlers, "worker", wx, wz);
        RunUntil(sim, () => lair.Pack.Any(id => sim.Units.Target[Index(sim, id)] == worker), 2 * Tps, "the pack goes after the worker");
    }

    // --- Data ---------------------------------------------------------------------------------------

    /// <summary>A lair's cavern spot must be along or deepest and not with a biome; only a lair with a biome roams.</summary>
    [Fact]
    public void ALairsCavernAndRoamAreChecked()
    {
        var files = Files();
        Edit(files, "units.json", "\"cavern\": \"along\"", "\"cavern\": \"middle\"");
        Edit(files, "units.json", "\"cavern\": \"deepest\"", "\"cavern\": \"deepest\", \"biome\": \"hills\"");
        Edit(files, "units.json", "\"guard\": 8, \"leash\": 12,", "\"guard\": 8, \"leash\": 12, \"roam\": 10,");
        var e = Assert.Throws<ContentException>(() => ContentLoader.Load(files));
        Assert.Contains(e.Errors, m => m.Contains("unit 'cave_crawler', lair") && m.Contains("\"cavern\" is \"middle\""));
        Assert.Contains(e.Errors, m => m.Contains("unit 'cave_wyrm', lair") && m.Contains("in the cavern or in a \"biome\""));
        Assert.DoesNotContain(e.Errors, m => m.Contains("unit 'giant_crab'"));

        files = Files();
        Edit(files, "units.json", "\"leash\": 16, \"reward\": { \"iron\": 30", "\"leash\": 16, \"roam\": 10, \"reward\": { \"iron\": 30");
        e = Assert.Throws<ContentException>(() => ContentLoader.Load(files));
        Assert.Contains(e.Errors, m => m.Contains("unit 'cave_crawler', lair") && m.Contains("\"roam\" needs a \"biome\""));
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
