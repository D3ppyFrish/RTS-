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
/// The giant hornets: a swarm round a papery hive on a stump in the forests. Their stings take morale (6 a sting), so a
/// swarm breaks a squad before it kills it; heroes, monsters and machines are not frightened, and a hero steadies the
/// stung near it. The hive hatches a new hornet every 20 seconds while it stands, even once the swarm is dead, so the
/// nest is cleared by knocking the hive down; whoever does takes up the queen's sting, whose bearer frightens too.
/// </summary>
public class HornetTests
{
    private const int Tps = Simulation.TicksPerSecond;
    private const int Settlers = 0, Orcs = 1;

    private static Simulation Flat() => TestWorlds.Sim(TestWorlds.Flat(96, 16, 96, 3));
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

    [Fact]
    public void HornetsSwarmFromAHiveInTheForests()
    {
        var db = ContentDb.Default;
        var hornet = db.Unit("giant_hornet");
        Assert.Equal(UnitRole.Monster, hornet.Role);
        Assert.Equal(6, hornet.Fear);
        Assert.Equal(Biome.Forest, hornet.Lair!.Biome);
        Assert.Same(db.Building("hornet_hive"), hornet.Lair.Totem);
        Assert.Equal(BuildingKind.Totem, hornet.Lair.Totem!.Kind);
        Assert.Equal((5, 20 * Tps, 5), (hornet.Lair.Pack, hornet.Lair.GrowTicks, hornet.Lair.GrowTo));
        Assert.Contains(hornet, db.WildMonsters);
        var sting = db.Relic("queens_sting");
        Assert.Equal((2, 8), (sting.Damage, sting.Fear));
        Assert.Same(sting, hornet.Lair.PrizeFor(db.Faction("settlers"))!.Relic);
        Assert.Same(sting, hornet.Lair.PrizeFor(db.Faction("orcs"))!.Relic);
        Assert.Equal(0, db.Unit("wolf").Fear);
        Assert.Equal(0, db.Relic("spider_silk").Fear);
    }

    /// <summary>
    /// Each sting that lands takes 6 morale from the stung, with nobody near it dying, until it breaks and runs.
    /// </summary>
    [Fact]
    public void StingsBreakASquadBeforeTheyKillIt()
    {
        var sim = Flat();
        var lair = sim.Monsters.Place(sim.Content.Unit("giant_hornet"), 60, 48)!;
        Assert.Equal(5, lair.Pack.Count);
        int spearman = sim.SpawnUnit(Settlers, "spearman", 56, 48);
        sim.Units.Hp[Index(sim, spearman)] = 100000; // the stings do not kill it
        RunUntil(sim, () => sim.Combat.Frights > 0, 10 * Tps, "a hornet stings the spearman");
        int i = Index(sim, spearman);
        Assert.Equal(CombatSystem.MaxMorale - 6 * (int)sim.Combat.Frights, sim.Units.Morale[i]);
        RunUntil(sim, () => sim.Units.Rout[Index(sim, spearman)] > 0, 20 * Tps, "the stung spearman breaks");
        Assert.Equal(5, lair.Pack.Count);
        Assert.Equal(0, sim.Combat.Losses(Settlers));
    }

    /// <summary>A hero is never frightened, and the units it steadies lose half as much to a sting (the Marshal's 50).</summary>
    [Fact]
    public void AHeroIsNotFrightenedAndSteadiesTheStung()
    {
        int Lost(bool marshal, bool stingHero)
        {
            var sim = Flat();
            int orc = sim.SpawnUnit(Orcs, "spearman", 40, 40);
            Bear(sim, orc, "queens_sting");
            int foe = sim.SpawnUnit(Settlers, stingHero ? "marshal" : "spearman", 42, 40);
            sim.Units.Hp[Index(sim, foe)] = 100000;
            if (marshal && !stingHero)
            {
                int m = sim.SpawnUnit(Settlers, "marshal", 42, 44);
                sim.Units.Hp[Index(sim, m)] = 100000;
            }
            sim.Schedule(0, new AttackCommand(Orcs, new[] { orc }, foe));
            RunUntil(sim, () => sim.Combat.Frights > 0 || (stingHero && sim.Units.Hp[Index(sim, foe)] < 100000), 10 * Tps, "the bearer strikes");
            sim.Step();
            return CombatSystem.MaxMorale - sim.Units.Morale[Index(sim, foe)];
        }
        Assert.Equal(8, Lost(marshal: false, stingHero: false));
        Assert.Equal(4, Lost(marshal: true, stingHero: false));
        Assert.Equal(0, Lost(marshal: false, stingHero: true));
    }

    /// <summary>
    /// Swatting the swarm does not clear the nest: the hive hatches a new hornet every 20 seconds even with none left.
    /// Knocking it down clears the nest, pays whoever did it, and its den gives the queen's sting.
    /// </summary>
    [Fact]
    public void TheHiveHatchesAgainUntilItIsKnockedDown()
    {
        var sim = Flat();
        var hall = sim.Economy.PlaceFinished(Settlers, sim.Content.Building("town_hall"), 10, 40)!;
        var lair = sim.Monsters.Place(sim.Content.Unit("giant_hornet"), 60, 48)!;
        var hive = sim.Monsters.TotemOf(lair)!;
        Assert.Equal((Simulation.WildPlayer, "hornet_hive"), (hive.Owner, hive.Def.Id));
        Assert.True(hive.Covers(60, 48));
        // A full swarm hatches no more: the hive only brings it back to five.
        sim.Run(45 * Tps);
        Assert.Equal(5, lair.Pack.Count);
        foreach (int id in lair.Pack.ToArray()) sim.Units.RemoveAt(Index(sim, id));
        sim.Step();
        Assert.Empty(lair.Pack);
        Assert.False(lair.Cleared);
        RunUntil(sim, () => lair.Pack.Count > 0, 21 * Tps, "the hive hatches a hornet");
        Assert.Single(lair.Pack);
        RunUntil(sim, () => lair.Pack.Count > 1, 21 * Tps, "the hive hatches another");
        Assert.False(lair.Cleared);

        // Spearmen who shrug off the stings, and keep their nerve, knock the hive down.
        var ids = new List<int>();
        for (int k = 0; k < 6; k++)
        {
            int id = sim.SpawnUnit(Settlers, "spearman", 56, 46 + k);
            sim.Units.Hp[Index(sim, id)] = 100000;
            ids.Add(id);
        }
        sim.Schedule(sim.Tick, new AttackBuildingCommand(Settlers, ids.ToArray(), hive.Id));
        RunUntil(sim, () =>
        {
            foreach (int id in ids) sim.Units.Morale[Index(sim, id)] = CombatSystem.MaxMorale;
            foreach (int h in lair.Pack) sim.Units.Hp[Index(sim, h)] = 100000; // only the hive is hit
            return lair.Cleared;
        }, 120 * Tps, "the spearmen knock the hive down");
        Assert.Equal(Settlers, lair.ClearedBy);
        Assert.True(sim.Monsters.TotemDown(lair));
        sim.Step();
        Assert.Equal((120, 30), (hall.Stock[(int)Good.Food], hall.Stock[(int)Good.Wood]));

        // No more hatch once it is down.
        int left = lair.Pack.Count;
        sim.Run(25 * Tps);
        Assert.True(lair.Pack.Count <= left);

        foreach (int h in lair.Pack.ToArray()) sim.Units.RemoveAt(Index(sim, h));
        int taker = sim.SpawnUnit(Settlers, "spearman", lair.X + 3, lair.Z);
        sim.Schedule(sim.Tick, new DenCommand(Settlers, new[] { taker }, lair.Id));
        sim.Step();
        Assert.Same(sim.Content.Relic("queens_sting"), sim.RelicOf(Index(sim, taker)));
    }

    /// <summary>The default map has hornet nests in the forests, each round its hive, the same for the same seed.</summary>
    [Fact]
    public void TheDefaultMapHasHornetNestsInTheForests()
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
        var nests = sim.Monsters.Lairs.Where(l => l.Monster.Id == "giant_hornet").ToList();
        Assert.Equal(MonsterSystem.WildCount(2, (long)sim.World.SizeX * sim.World.SizeZ), nests.Count);
        foreach (var n in nests)
        {
            Assert.Equal(Biome.Forest, sim.Map.BiomeAt(n.X, n.Z));
            var hive = sim.Monsters.TotemOf(n)!;
            Assert.True(hive.Covers(n.X, n.Z));
            Assert.Equal(5, n.Pack.Count);
            foreach (var s in sim.Map.Starts) Assert.True(IntMath.Distance(n.X, n.Z, s.X, s.Z) >= MonsterSystem.WildStartGap);
        }
        Assert.Equal(nests.Select(n => (n.X, n.Z)), Match().Monsters.Lairs.Where(l => l.Monster.Id == "giant_hornet").Select(n => (n.X, n.Z)));
    }

    [Fact]
    public void FearDataIsChecked()
    {
        var files = ContentTests.Files();
        files["units.json"] = files["units.json"].Replace("\"fear\": 6", "\"fear\": 500");
        files["relics.json"] = files["relics.json"].Replace("\"fear\": 8", "\"fear\": -1");
        var e = Assert.Throws<ContentException>(() => ContentLoader.Load(files));
        Assert.Contains(e.Errors, m => m.Contains("giant_hornet") && m.Contains("fear"));
        Assert.Contains(e.Errors, m => m.Contains("queens_sting") && m.Contains("fear"));
    }
}
