using System;
using System.Linq;
using System.Collections.Generic;
using Rts.Sim.AI;
using Rts.Sim.Content;
using Rts.Sim.World;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Clan Kage's clan honour (see "Clan Kage" in docs/rules.md, <c>Economy/EconomySystem.Honour.cs</c>): earned by
/// contracts (assassinations, sabotage, scouting), lost by dishonourable defeats, spent on the master ninja and the
/// Moonless Night.
/// </summary>
public class HonourTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Stand = Ground + 1;
    private const int Tps = Simulation.TicksPerSecond;

    private static Simulation Flat(int size = 64)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(size, 16, size, Ground));
        sim.Fog = false;
        sim.SetFaction(0, "kage");
        sim.SetFaction(1, "settlers");
        return sim;
    }

    private static Building Place(Simulation sim, int owner, string def, int x, int z) =>
        sim.Economy.PlaceFinished(owner, sim.Content.Building(def), x, z) ?? throw new InvalidOperationException($"{def} does not fit");

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

    private static void RunUntil(Simulation sim, Func<bool> done, int maxTicks, string what)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return;
            sim.Step();
        }
        Assert.True(done(), $"not done after {maxTicks} ticks: {what}");
    }

    /// <summary>Unit <paramref name="victim"/>, worn down to 1 hit point, is attacked by <paramref name="killer"/> until it dies.</summary>
    private static void Kill(Simulation sim, int killer, int victim)
    {
        sim.Units.Hp[Index(sim, victim)] = 1;
        sim.Schedule(sim.Tick, new AttackCommand(sim.Units.Owner[Index(sim, killer)], new[] { killer }, victim));
        RunUntil(sim, () => Index(sim, victim) < 0, 20 * Tps, "the victim dead");
    }

    [Fact]
    public void ClanHonourIsClanKagesTraitAndPaysForTheMasterNinjaAndTheMoonlessNight()
    {
        var content = ContentDb.Default;
        var rules = content.Faction("kage").Honour;
        Assert.NotNull(rules);
        Assert.Equal((2, 10, 10, 15, 12, 5, 20, 20),
            (rules!.Assassination, rules.HeroSlain, rules.Sabotage, rules.Scouting, rules.ScoutRange, rules.BuildingLost, rules.HallLost, rules.HeroLost));
        foreach (var f in content.Factions.Where(f => f.Id != "kage")) Assert.Null(f.Honour);

        var ninja = content.Unit("master_ninja");
        Assert.Equal("kage", ninja.Faction?.Id);
        Assert.True(ninja.Hero && ninja.Camouflage && ninja.Shadowborn && ninja.Assassin == 100);
        Assert.Equal(40, ninja.Honour);
        Assert.Equal("barracks", ninja.TrainedAt?.Id);
        Assert.Equal(60, content.Tech("kage_moonless").Honour);
        // Nothing else costs honour.
        Assert.Equal(new[] { "master_ninja" }, content.Units.Where(u => u.Honour > 0).Select(u => u.Id));
        Assert.Equal(new[] { "kage_moonless" }, content.Techs.Where(t => t.Honour > 0).Select(t => t.Id));
        // A trait, not a tech: Clan Kage stays at six techs.
        Assert.Equal(6, content.Techs.Count(t => t.Faction?.Id == "kage"));
    }

    [Fact]
    public void AnAssassinationOfARivalsWorkerOrCasterEarnsHonourButASoldierKilledDoesNot()
    {
        var sim = Flat();
        int kunoichi = sim.SpawnUnit(0, "kunoichi", 20, 20, Stand);
        int worker = sim.SpawnUnit(1, "worker", 22, 20, Stand);
        Kill(sim, kunoichi, worker);
        Assert.Equal(2, sim.Economy.Honour(0));

        int mage = sim.SpawnUnit(1, "mage", 22, 21, Stand);
        Kill(sim, kunoichi, mage);
        Assert.Equal(4, sim.Economy.Honour(0));

        int spearman = sim.SpawnUnit(1, "spearman", 22, 22, Stand);
        Kill(sim, kunoichi, spearman);
        Assert.Equal(4, sim.Economy.Honour(0));
        Assert.Equal(4, sim.Economy.Player(0).HonourEarned);

        // Another faction keeps no honour for the same kill.
        int settler = sim.SpawnUnit(1, "spearman", 30, 30, Stand);
        int kageWorker = sim.SpawnUnit(0, "worker", 32, 30, Stand);
        Kill(sim, settler, kageWorker);
        Assert.Equal(0, sim.Economy.Honour(1));
    }

    [Fact]
    public void ARivalsHeroSlainEarnsMoreAndTheClansOwnHeroLostCostsHonour()
    {
        var sim = Flat();
        int kunoichi = sim.SpawnUnit(0, "kunoichi", 20, 20, Stand);
        int marshal = sim.SpawnUnit(1, "marshal", 22, 20, Stand);
        Kill(sim, kunoichi, marshal);
        Assert.Equal(10, sim.Economy.Honour(0));

        int ninja = sim.SpawnUnit(0, "master_ninja", 30, 30, Stand);
        int knight = sim.SpawnUnit(1, "spearman", 32, 30, Stand);
        Kill(sim, knight, ninja);
        Assert.Equal(0, sim.Economy.Honour(0)); // 20 lost, but honour is never below 0
        Assert.Equal(10, sim.Economy.Player(0).HonourLost);
    }

    [Fact]
    public void ASabotageDoneEarnsHonour()
    {
        var sim = Flat();
        sim.Economy.Grant(0, sim.Content.Tech("kage_saboteurs"));
        var hall = Place(sim, 1, "town_hall", 36, 36);
        int kunoichi = sim.SpawnUnit(0, "kunoichi", 24, 24, Stand);
        sim.Schedule(0, new SabotageCommand(0, new[] { kunoichi }, hall.Id));
        int before = 0;
        RunUntil(sim, () =>
        {
            if (sim.Economy.Sabotages > 0) return true;
            before = sim.Economy.Honour(0);
            return false;
        }, 40 * Tps, "the hall sabotaged");
        Assert.Equal(before + 10, sim.Economy.Honour(0));
    }

    [Fact]
    public void ScoutingARivalsTownHallEarnsHonourOnce()
    {
        var sim = Flat();
        var hall = Place(sim, 1, "town_hall", 44, 44);
        int shinobi = sim.SpawnUnit(0, "shinobi", 4, 4, Stand);
        sim.Run(3 * Tps);
        Assert.Equal(0, sim.Economy.Honour(0)); // too far off
        Assert.False(sim.Economy.Scouted(0, hall.Id));

        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { shinobi }, 36, 36));
        RunUntil(sim, () => sim.Economy.Scouted(0, hall.Id), 30 * Tps, "the hall scouted");
        Assert.Equal(15, sim.Economy.Honour(0));
        Assert.True(hall.ColumnDistance(sim.Units.X[Index(sim, shinobi)].FloorToInt(), sim.Units.Z[Index(sim, shinobi)].FloorToInt()) <= 12);

        // Once a hall: going back and coming again earns nothing more.
        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { shinobi }, 4, 4));
        sim.Run(15 * Tps);
        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { shinobi }, 36, 36));
        sim.Run(15 * Tps);
        Assert.Equal(15, sim.Economy.Honour(0));

        // The settlers scouting the clan's hall earn nothing: they keep no honour.
        var kageHall = Place(sim, 0, "town_hall", 6, 44);
        sim.SpawnUnit(1, "rider", 10, 40, Stand);
        sim.Run(2 * Tps);
        Assert.False(sim.Economy.Scouted(1, kageHall.Id));
        Assert.Equal(0, sim.Economy.Honour(1));
    }

    [Fact]
    public void ABuildingOfTheClansBurnedByARivalCostsHonour()
    {
        var sim = Flat();
        sim.Economy.Player(0).Honour = 30;
        var store = Place(sim, 0, "storehouse", 30, 30);
        int raider = sim.SpawnUnit(1, "spearman", 27, 30, Stand);
        sim.Schedule(0, new BurnCommand(1, new[] { raider }, store.Id));
        RunUntil(sim, () => sim.Buildings.Get(store.Id) == null, 60 * Tps, "the storehouse burned");
        Assert.Equal(25, sim.Economy.Honour(0));
        Assert.Equal(5, sim.Economy.Player(0).HonourLost);
    }

    [Fact]
    public void TheMasterNinjaIsTrainedForHonourOneAtATime()
    {
        var sim = Flat();
        var hall = Place(sim, 0, "town_hall", 10, 10);
        hall.Stock[(int)Good.Food] = 1000;
        hall.Stock[(int)Good.Wood] = 1000;
        hall.Stock[(int)Good.Iron] = 100;
        var barracks = Place(sim, 0, "barracks", 24, 10);

        // Without the honour, none is trained.
        sim.Schedule(0, new TrainCommand(0, barracks.Id, "master_ninja"));
        sim.Step();
        Assert.Empty(barracks.Queue);

        sim.Economy.Player(0).Honour = 100;
        sim.Schedule(sim.Tick, new TrainCommand(0, barracks.Id, "master_ninja"));
        sim.Schedule(sim.Tick, new TrainCommand(0, barracks.Id, "master_ninja")); // one of a kind: not a second
        sim.Step();
        Assert.Single(barracks.Queue);
        Assert.Equal(60, sim.Economy.Honour(0));
        Assert.True(sim.Economy.HeroTaken(0, sim.Content.Unit("master_ninja")));

        RunUntil(sim, () => barracks.Queue.Count == 0, 40 * Tps, "the master ninja trained");
        Assert.Equal(1, Enumerable.Range(0, sim.Units.Count).Count(i => sim.Content.Units[sim.Units.Def[i]].Id == "master_ninja"));
        sim.Schedule(sim.Tick, new TrainCommand(0, barracks.Id, "master_ninja"));
        sim.Step();
        Assert.Empty(barracks.Queue); // while one lives
        Assert.Equal(60, sim.Economy.Honour(0));

        // Another faction cannot train one.
        Assert.False(sim.Economy.Unlocked(1, sim.Content.Unit("master_ninja")));
    }

    [Fact]
    public void TheMoonlessNightNeedsHonourAndSpendsIt()
    {
        var sim = Flat();
        var hall = Place(sim, 0, "town_hall", 10, 10);
        hall.Stock[(int)Good.Food] = 1000;
        hall.Stock[(int)Good.Stone] = 1000;
        hall.Stock[(int)Good.Iron] = 100;
        var tech = sim.Content.Tech("kage_moonless");
        foreach (var t in tech.Requires) sim.Economy.Grant(0, t);
        sim.Economy.Player(0).Honour = 59;
        Assert.Equal(ResearchResult.CannotAfford, sim.Economy.CanResearch(0, tech));
        Assert.Equal(ResearchResult.Ok, sim.Economy.CanResearch(0, tech, checkCost: false));
        sim.Economy.Player(0).Honour = 75;
        Assert.Equal(ResearchResult.Ok, sim.Economy.CanResearch(0, tech));
        sim.Schedule(sim.Tick, new ResearchCommand(0, "kage_moonless"));
        sim.Step();
        Assert.Equal(tech, sim.Economy.Researching(0));
        Assert.Equal(15, sim.Economy.Honour(0));
    }

    [Fact]
    public void HonourIsPartOfTheStateHash()
    {
        var a = Flat();
        var b = Flat();
        a.Step();
        b.Step();
        Assert.Equal(a.StateHash(), b.StateHash());
        b.Economy.Player(0).Honour = 5;
        Assert.NotEqual(a.StateHash(), b.StateHash());
    }

    /// <summary>A Clan Kage computer player with a barracks and the honour for it trains its master ninja, once, at every level.</summary>
    [Fact]
    public void AComputerPlayerTrainsItsMasterNinjaOnceItHasTheHonour()
    {
        foreach (var level in new[] { AiLevel.Classic, AiLevel.Easy, AiLevel.Normal, AiLevel.Hard })
        {
            var sim = TestWorlds.Sim(TestWorlds.Flat(128, 16, 128, Ground));
            sim.SetFaction(0, "kage");
            sim.Map.Starts.Add(new StartArea(0, 24, Ground + 1, 64));
            sim.Map.Starts.Add(new StartArea(1, 104, Ground + 1, 64));
            sim.PlaceStartingBuildings();
            var hall = sim.Economy.TownHall(0)!;
            foreach (var g in new[] { Good.Food, Good.Wood, Good.Stone, Good.Iron }) hall.Stock[(int)g] = 2000;
            Place(sim, 0, "barracks", 24, 80);
            var ninja = sim.Content.Unit("master_ninja");
            var ai = new AiPlayer(0, 1, level);
            var log = new List<Command>();
            void Think(int ticks)
            {
                for (int t = 0; t < ticks; t++)
                {
                    ai.Think(sim, c =>
                    {
                        log.Add(c);
                        sim.Schedule(sim.Tick, c);
                    });
                    sim.Step();
                }
            }
            Think(20 * Tps);
            Assert.DoesNotContain(log, c => c is TrainCommand { Unit: "master_ninja" }); // no honour yet

            sim.Economy.Player(0).Honour = 50;
            Think(60 * Tps);
            Assert.True(sim.Evolution.HasHero(0, ninja), $"{level}: a master ninja trained");
            Assert.Equal(10, sim.Economy.Honour(0));
            Assert.False(sim.Economy.HeroTaken(1, ninja));

            // One of a kind: with honour again, it trains no second while the first lives.
            sim.Economy.Player(0).Honour = 100;
            Think(30 * Tps);
            Assert.Equal(1, Enumerable.Range(0, sim.Units.Count).Count(i => sim.Units.Def[i] == ninja.Index));
            Assert.Equal(100, sim.Economy.Honour(0));
        }
    }

    [Fact]
    public void TestModesGoodsGiveClanHonourToAFactionThatKeepsIt()
    {
        var sim = Flat();
        sim.TestMode = true;
        Place(sim, 0, "town_hall", 10, 10);
        Place(sim, 1, "town_hall", 40, 40);
        sim.Schedule(0, new SandboxGoodsCommand(0, 0, 1000));
        sim.Schedule(0, new SandboxGoodsCommand(0, 1, 1000));
        sim.Step();
        Assert.Equal(100, sim.Economy.Honour(0));
        Assert.Equal(0, sim.Economy.Honour(1));
    }
}
