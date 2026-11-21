using System;
using System.Linq;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Hunting: herds of wild game roam the grassland, and hunters from a hunter's lodge shoot them for food.</summary>
public class HuntingTests
{
    private const int Tps = Simulation.TicksPerSecond;
    // Players are given the factions in data order: the Settlers first, then the Orcs, who tame monsters.
    private const int Settlers = 0, Orcs = 1;

    private static (Simulation Sim, Building Hall, Building Lodge, Lair Herd) Camp()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(96, 16, 96));
        var hall = sim.Economy.PlaceFinished(Settlers, sim.Content.Building("town_hall"), 8, 8)!;
        var lodge = sim.Economy.PlaceFinished(Settlers, sim.Content.Building("hunting_lodge"), 24, 24)!;
        var herd = sim.Monsters.Place(sim.Content.Unit("deer"), 48, 48)!;
        return (sim, hall, lodge, herd);
    }

    private static int[] Hunters(Simulation sim, Building lodge, int n = 2)
    {
        var w = Enumerable.Range(0, n).Select(k => sim.SpawnUnit(Settlers, "worker", 20 + k, 22)).ToArray();
        sim.Schedule(sim.Tick, new WorkCommand(Settlers, w, lodge.Id));
        return w;
    }

    [Fact]
    public void TheDataHasDeerAndAHuntersLodge()
    {
        var deer = ContentDb.Default.Unit("deer");
        Assert.True(deer.IsGame);
        Assert.False(deer.CanAttack);
        Assert.Equal(new[] { new GoodAmount(Good.Food, 25) }, deer.Game);
        Assert.Equal(Biome.Grassland, deer.Lair!.Biome);
        Assert.Contains(deer, ContentDb.Default.GameAnimals);
        Assert.DoesNotContain(deer, ContentDb.Default.WildMonsters);
        var lodge = ContentDb.Default.Building("hunting_lodge");
        Assert.Equal(BuildingKind.Quarry, lodge.Kind);
        Assert.Equal(32, lodge.Hunts);
        Assert.True(lodge.Gives(Good.Food));
        Assert.Empty(lodge.Requires);
    }

    [Fact]
    public void AHerdIsKeptApartFromTheMonsterLairs()
    {
        var (sim, _, _, herd) = Camp();
        Assert.Contains(herd, sim.Monsters.Herds);
        Assert.DoesNotContain(herd, sim.Monsters.Lairs);
        Assert.Equal(4, herd.Pack.Count);
        Assert.All(herd.Pack, id => Assert.Equal(Simulation.WildPlayer, sim.Units.Owner[sim.Units.IndexOf(id)]));
    }

    [Fact]
    public void HuntersShootDeerAndBringTheMeatHome()
    {
        var (sim, hall, lodge, herd) = Camp();
        int food = hall.Stock[(int)Good.Food];
        Hunters(sim, lodge);
        bool shot = false;
        for (int t = 0; t < 90 * Tps; t++)
        {
            sim.Step();
            shot |= sim.Combat.Shots.Any(s => s.Ranged && sim.Units.IndexOf(s.Attacker) is int a and >= 0 && sim.Units.Def[a] == sim.Content.Unit("worker").Index);
        }
        Assert.True(shot, "a hunter's arrow flew");
        Assert.True(sim.Economy.GameHunted >= 2, $"deer brought home: {sim.Economy.GameHunted}");
        Assert.True(hall.Stock[(int)Good.Food] - food >= 2 * 25, $"food: {hall.Stock[(int)Good.Food] - food}");
        Assert.False(herd.Cleared);
    }

    [Fact]
    public void HuntersLeaveTheLastTwoAndTheHerdGrowsBack()
    {
        var (sim, hall, lodge, herd) = Camp();
        Hunters(sim, lodge, 3);
        int least = herd.Pack.Count, last = least;
        bool grew = false;
        for (int t = 0; t < 5 * 60 * Tps; t++)
        {
            sim.Step();
            if (least == EconomySystem.HerdKeep && herd.Pack.Count > last) grew = true;
            last = herd.Pack.Count;
            least = Math.Min(least, last);
        }
        Assert.Equal(EconomySystem.HerdKeep, least);
        Assert.False(herd.Cleared);
        Assert.True(grew, "a deer was born to the herd after it was hunted down to its last two");
        // Four to start, one born a minute, shot when the herd wanders within reach.
        Assert.True(sim.Economy.GameHunted >= 3, $"deer brought home: {sim.Economy.GameHunted}");
    }

    [Fact]
    public void NothingToHuntOutOfReach()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(128, 16, 128));
        var hall = sim.Economy.PlaceFinished(Settlers, sim.Content.Building("town_hall"), 8, 8)!;
        var lodge = sim.Economy.PlaceFinished(Settlers, sim.Content.Building("hunting_lodge"), 24, 24)!;
        var herd = sim.Monsters.Place(sim.Content.Unit("deer"), 110, 110)!;
        var w = Hunters(sim, lodge);
        sim.Run(60 * Tps);
        Assert.Equal(0, sim.Economy.GameHunted);
        Assert.Equal(4, herd.Pack.Count);
        // They wait at the lodge.
        int i = sim.Units.IndexOf(w[0]);
        Assert.Equal(UnitJob.Quarry, sim.Units.Job[i]);
        Assert.True(lodge.ColumnDistance(sim.Units.X[i].FloorToInt(), sim.Units.Z[i].FloorToInt()) <= 3);
    }

    [Fact]
    public void DeerNeverFightAndNobodyGoesAfterThemOnTheirOwn()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(64, 16, 64));
        var herd = sim.Monsters.Place(sim.Content.Unit("deer"), 30, 30)!;
        var spear = sim.SpawnUnit(Settlers, "spearman", 32, 30);
        var archer = sim.SpawnUnit(Settlers, "archer", 34, 32);
        sim.Run(20 * Tps);
        Assert.Equal(4, herd.Pack.Count);
        Assert.All(new[] { spear, archer }, id => Assert.Equal(sim.Content.Unit(id == spear ? "spearman" : "archer").MaxHp, sim.Units.Hp[sim.Units.IndexOf(id)]));
        // An explicit order still kills one.
        int deer = herd.Pack[0];
        sim.Schedule(sim.Tick, new AttackCommand(Settlers, new[] { spear }, deer));
        sim.Run(30 * Tps);
        Assert.False(sim.Units.Exists(deer));
    }

    [Fact]
    public void OrcsKillDeerRatherThanBeatingThem()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(64, 16, 64));
        var herd = sim.Monsters.Place(sim.Content.Unit("deer"), 30, 30)!;
        var spear = sim.SpawnUnit(Orcs, "spearman", 32, 30);
        int deer = herd.Pack[0];
        sim.Schedule(0, new AttackCommand(Orcs, new[] { spear }, deer));
        sim.Run(30 * Tps);
        Assert.False(sim.Units.Exists(deer), "the deer is dead, not beaten");
    }

    [Fact]
    public void AHerdKilledOffIsGoneForGood()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(64, 16, 64));
        var herd = sim.Monsters.Place(sim.Content.Unit("deer"), 30, 30)!;
        var spears = Enumerable.Range(0, 4).Select(k => sim.SpawnUnit(Settlers, "spearman", 26, 26 + k)).ToArray();
        foreach (int deer in herd.Pack.ToArray())
        {
            sim.Schedule(sim.Tick, new AttackCommand(Settlers, spears, deer));
            for (int t = 0; t < 30 * Tps && sim.Units.Exists(deer); t++) sim.Step();
        }
        sim.Run(Tps);
        Assert.True(herd.Cleared);
        sim.Run(3 * 60 * Tps);
        Assert.Empty(herd.Pack);
        Assert.DoesNotContain(Enumerable.Range(0, sim.Units.Count), i => sim.Units.Def[i] == sim.Content.Unit("deer").Index);
    }

    [Fact]
    public void HerdsGrazeTheGrasslandNearTheStarts()
    {
        var sim = CavernTests.DefaultMatch();
        var herds = sim.Monsters.Herds;
        long area = (long)sim.World.SizeX * sim.World.SizeZ;
        int want = (int)(sim.Content.Unit("deer").Lair!.Count * area / (512 * 512));
        int most = want + sim.Map.Starts.Count;
        Assert.True(herds.Count >= sim.Map.Starts.Count && herds.Count <= most, $"{herds.Count} herds, want {sim.Map.Starts.Count} to {most}");
        foreach (var herd in herds)
        {
            Assert.Equal(Biome.Grassland, sim.Map.BiomeAt(herd.X, herd.Z));
            Assert.True(herd.Roam > 0);
            foreach (var s in sim.Map.Starts)
                Assert.True(IntMath.Distance(herd.X, herd.Z, s.X, s.Z) >= MonsterSystem.HerdStartGap);
            foreach (var lair in sim.Monsters.Lairs)
                if (lair.Monster.Lair!.Cavern == CavernSpot.None) Assert.True(IntMath.Distance(herd.X, herd.Z, lair.X, lair.Z) >= MonsterSystem.HerdGap);
        }
        // Every start has a herd of its own not far off.
        foreach (var s in sim.Map.Starts)
            Assert.Contains(herds, h => IntMath.Distance(h.X, h.Z, s.X, s.Z) <= MonsterSystem.HerdStartGap + MonsterSystem.HerdStartSpread);
        var again = CavernTests.DefaultMatch();
        Assert.Equal(herds.Select(h => (h.X, h.Z)), again.Monsters.Herds.Select(h => (h.X, h.Z)));
        Assert.Equal(sim.StateHash(), again.StateHash());
    }

    [Fact]
    public void HuntingIsTheSameEveryRun()
    {
        ulong Run()
        {
            var (sim, _, lodge, _) = Camp();
            Hunters(sim, lodge);
            sim.Run(90 * Tps);
            return sim.StateHash();
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void GameMustBeAHarmlessMonsterOfABiome()
    {
        var files = ContentLoader.Files.ToDictionary(f => f, f => ContentLoader.EmbeddedText(f)!);
        const string old = "\"game\": { \"food\": 25 },";
        int at = files["units.json"].IndexOf(old, StringComparison.Ordinal);
        Assert.True(at >= 0);
        files["units.json"] = files["units.json"][..at] + "\"game\": { \"food\": 25 }, \"damage\": 3," + files["units.json"][(at + old.Length)..];
        var e = Assert.Throws<ContentException>(() => ContentLoader.Load(files));
        Assert.Contains(e.Errors, m => m.Contains("is wild \"game\""));
    }
}
