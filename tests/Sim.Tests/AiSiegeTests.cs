using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Computer players and siege engines: a smelter, a sawmill and a siege workshop once they have Smelting, catapults
/// trained there, and catapults in a wave shooting towers and walls with soldiers screening them.
/// </summary>
public class AiSiegeTests
{
    private const int Ground = 3;
    private const int Tps = Simulation.TicksPerSecond;

    private static Simulation WithStarts(int size, params (int X, int Z)[] starts)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(size, 16, size, Ground));
        for (int p = 0; p < starts.Length; p++) sim.Map.Starts.Add(new StartArea(p, starts[p].X, Ground + 1, starts[p].Z));
        sim.PlaceStartingBuildings();
        return sim;
    }

    private static bool Play(Simulation sim, IReadOnlyList<AiPlayer> players, Func<bool> done, int maxTicks, List<Command>? log = null)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return true;
            foreach (var ai in players) ai.Think(sim, c =>
            {
                log?.Add(c);
                sim.Schedule(sim.Tick, c);
            });
            sim.Step();
        }
        return done();
    }

    /// <summary>
    /// One town in the west of a flat map with Smelting, a finished barracks and enough workers, kept in
    /// plenty of every good (planks and iron too: there is no ore on a flat map).
    /// </summary>
    private static (Simulation Sim, Building Hall) Town()
    {
        var sim = WithStarts(256, (60, 128));
        var hall = sim.Economy.TownHall(0)!;
        sim.Economy.Grant(0, sim.Content.Tech("smelting"));
        Assert.NotNull(sim.Economy.PlaceFinished(0, sim.Content.Building("barracks"), 40, 110));
        for (int k = 0; k < AiPlayer.SiegeWorkers; k++) sim.SpawnUnit(0, "worker", 50 + k, 150);
        return (sim, hall);
    }

    private static void Stock(Building hall)
    {
        foreach (var (good, n) in new[] { (Good.Food, 1500), (Good.Wood, 1500), (Good.Stone, 800), (Good.Planks, 400), (Good.Iron, 200) })
            hall.Stock[(int)good] = Math.Max(hall.Stock[(int)good], n);
    }

    private static IEnumerable<Building> Mine(Simulation sim) => sim.Buildings.All.Where(b => b.Owner == 0);

    private static bool HasCatapult(Simulation sim) =>
        Enumerable.Range(0, sim.Units.Count).Any(i => sim.Units.Owner[i] == 0 && sim.Content.Units[sim.Units.Def[i]].Id == "catapult");

    [Theory]
    [InlineData(AiLevel.Easy)]
    [InlineData(AiLevel.Normal)]
    [InlineData(AiLevel.Hard)]
    public void WithSmeltingASmartComputerBuildsASiegeWorkshopAndTrainsCatapults(AiLevel level)
    {
        var (sim, hall) = Town();
        var ai = new AiPlayer(0, 1, level);
        var players = new[] { ai };
        bool done = false;
        for (int t = 0; t < 8 * 60 && !done; t++)
        {
            Stock(hall);
            done = Play(sim, players, () => HasCatapult(sim), Tps);
        }
        Assert.True(done, "a catapult to be trained");
        Assert.Contains(Mine(sim), b => b.Def.Kind == BuildingKind.Smelter);
        Assert.Contains(Mine(sim), b => b.Def.Kind == BuildingKind.Workshop && b.Def.Makes == Good.Planks);
        var shop = Assert.Single(Mine(sim), b => AiPlayer.SiegeWorks(sim, b.Def));
        Assert.Equal("siege_workshop", shop.Def.Id);
        Assert.True(AiPlayer.SiegeWorks(sim, shop.Def));
        Assert.False(AiPlayer.SiegeWorks(sim, sim.Content.Building("barracks")));

        // Two catapults at most, however rich it is.
        Play(sim, players, () => false, 3 * 60 * Tps);
        int catapults = Enumerable.Range(0, sim.Units.Count).Count(i => sim.Units.Owner[i] == 0 && sim.Content.Units[sim.Units.Def[i]].Id == "catapult");
        Assert.InRange(catapults, 1, 2);
    }

    [Fact]
    public void NoSiegeWorkshopWithoutSmeltingOrForTheClassicLevel()
    {
        foreach (var (level, smelting) in new[] { (AiLevel.Classic, true), (AiLevel.Normal, false) })
        {
            var (sim, hall) = Town();
            if (!smelting)
            {
                sim = WithStarts(256, (60, 128));
                hall = sim.Economy.TownHall(0)!;
                Assert.NotNull(sim.Economy.PlaceFinished(0, sim.Content.Building("barracks"), 40, 110));
                for (int k = 0; k < AiPlayer.SiegeWorkers; k++) sim.SpawnUnit(0, "worker", 50 + k, 150);
            }
            var players = new[] { new AiPlayer(0, 1, level) };
            for (int t = 0; t < 3 * 60; t++)
            {
                Stock(hall);
                Play(sim, players, () => false, Tps);
            }
            Assert.DoesNotContain(Mine(sim), b => AiPlayer.SiegeWorks(sim, b.Def) || b.Def.Kind == BuildingKind.Smelter);
        }
    }

    /// <summary>
    /// Player 0, an Easy computer player, with eight spearmen and two catapults, against player 1, who gives no
    /// orders, with a watchtower in front of its town hall.
    /// </summary>
    private static (Simulation Sim, AiPlayer Ai, Building Tower, int[] Catapults) Siege()
    {
        var sim = WithStarts(128, (24, 64), (100, 64));
        for (int k = 0; k < 8; k++) sim.SpawnUnit(0, "spearman", 18 + k, 72);
        var catapults = new[] { sim.SpawnUnit(0, "catapult", 20, 76), sim.SpawnUnit(0, "catapult", 24, 76) };
        var hall = sim.Economy.TownHall(1)!;
        var tower = sim.Economy.PlaceFinished(1, sim.Content.Building("watchtower"), hall.X - 8, hall.Z + 2)!;
        Assert.NotNull(tower);
        return (sim, new AiPlayer(0, 1, AiLevel.Easy), tower, catapults);
    }

    [Fact]
    public void CatapultsInAWaveShootTheTowerInFrontOfTheTownHall()
    {
        var (sim, ai, tower, catapults) = Siege();
        var players = new[] { ai };
        var u = sim.Units;
        Assert.True(Play(sim, players, () => ai.Waves > 0, AiPlayer.SmartFirstWaveTicks + 3 * 60 * Tps), "a wave to set out");
        Assert.All(catapults, id => Assert.Contains(id, ai.Wave));
        bool shooting = false;
        Assert.True(Play(sim, players, () =>
        {
            shooting |= catapults.Any(id => u.IndexOf(id) is var i && i >= 0 && u.TargetBuilding[i] == tower.Id);
            return sim.Buildings.Get(tower.Id) == null;
        }, 4 * 60 * Tps), "the tower to fall");
        Assert.True(shooting, "the catapults to be sent at the tower");
    }

    [Fact]
    public void SoldiersOfTheWaveGoForAnEnemyThatComesAtItsCatapults()
    {
        var (sim, ai, tower, catapults) = Siege();
        var players = new[] { ai };
        var u = sim.Units;
        Assert.True(Play(sim, players, () => ai.Waves > 0, AiPlayer.SmartFirstWaveTicks + 3 * 60 * Tps), "a wave to set out");
        // The catapults on the tower, and the rest of the wave busy at the enemy's buildings.
        Assert.True(Play(sim, players, () => catapults.Any(id => u.IndexOf(id) is var i && i >= 0 && u.TargetBuilding[i] == tower.Id)
            && ai.Wave.All(id => catapults.Contains(id) || u.IndexOf(id) is var i && i >= 0 && u.TargetBuilding[i] != 0),
            3 * 60 * Tps), "the catapults to start on the tower and the soldiers on a building");
        // A rider of player 1 comes at the first catapult, into its dead ground.
        int c = u.IndexOf(catapults[0]);
        int cx = u.X[c].FloorToInt(), cz = u.Z[c].FloorToInt();
        int rider = sim.SpawnUnit(1, "rider", cx - 3, cz);
        Assert.True(rider > 0);
        sim.Schedule(sim.Tick, new AttackCommand(1, new[] { rider }, catapults[0]));
        var log = new List<Command>();
        Play(sim, players, () => !u.Exists(rider), 40 * Tps, log);
        Assert.False(u.Exists(rider), "the rider to be cut down");
        // A few of the wave's soldiers are sent at it, and the catapult backs off out of the rider's reach.
        string why = string.Join("; ", log.Select(c => c.GetType().Name + ":" + string.Join(",", ((UnitsCommand)c).Units)).Take(30));
        var sent = log.OfType<AttackCommand>().Where(a => a.Target == rider).SelectMany(a => a.Units).Distinct().ToList();
        Assert.True(sent.Count >= 1 && sent.Count <= AiPlayer.ScreenSoldiers, why + $" rider {rider} wave {string.Join(",", ai.Wave)} died at {sim.Tick}");
        Assert.All(sent, id => Assert.DoesNotContain(id, catapults));
        Assert.Contains(log, c => c is MoveCommand m && m.Units.Contains(catapults[0]));
    }
    /// <summary>
    /// Player 0, a computer player with Smelting and a finished siege workshop, kept in plenty, against player 1, who
    /// gives no orders, with a stone gate (and wall either side) in front of its town hall if walled.
    /// </summary>
    private static (Simulation Sim, AiPlayer Ai, Building? Gate) WalledFoe(AiLevel level, bool walled)
    {
        var sim = WithStarts(128, (24, 64), (100, 64));
        sim.Economy.Grant(0, sim.Content.Tech("smelting"));
        Assert.NotNull(sim.Economy.PlaceFinished(0, sim.Content.Building("siege_workshop"), 14, 44));
        var hall = sim.Economy.TownHall(1)!;
        Building? gate = null;
        if (walled)
        {
            gate = sim.Economy.PlaceFinished(1, sim.Content.Building("stone_gate"), hall.X - 10, hall.Z);
            Assert.NotNull(gate);
            for (int dz = 1; dz <= 4; dz++)
            {
                sim.Economy.PlaceFinished(1, sim.Content.Building("stone_wall"), hall.X - 10, gate!.Z - dz);
                sim.Economy.PlaceFinished(1, sim.Content.Building("stone_wall"), hall.X - 10, gate.Z + gate.Def.SizeZ - 1 + dz);
            }
        }
        return (sim, new AiPlayer(0, 1, level), gate);
    }

    private static bool HasRam(Simulation sim) =>
        Enumerable.Range(0, sim.Units.Count).Any(i => sim.Units.Owner[i] == 0 && sim.Content.Units[sim.Units.Def[i]].Ram)
        || sim.Buildings.All.Any(b => b.Owner == 0 && b.Queue.Any(q => sim.Content.Units[q].Ram));

    [Theory]
    [InlineData(AiLevel.Easy)]
    [InlineData(AiLevel.Normal)]
    [InlineData(AiLevel.Hard)]
    public void ASmartComputerTrainsRamsOnlyOnceItKnowsOfAnEnemyWallOrGate(AiLevel level)
    {
        var ram = Rts.Sim.Content.ContentDb.Default.Unit("battering_ram");
        foreach (bool walled in new[] { false, true })
        {
            var (sim, ai, gate) = WalledFoe(level, walled);
            Assert.Equal(0, ai.TrainWeight(sim, ram));
            if (gate == null) continue;
            // It sees the gate: a spearman of its stands by it.
            int look = sim.SpawnUnit(0, "spearman", gate.X - 4, gate.Z);
            Assert.True(look > 0);
            Play(sim, new[] { ai }, () => ai.KnowsWalls(sim), 10 * Tps);
            Assert.True(ai.KnowsWalls(sim), "it to see the gate");
            Assert.True(ai.TrainWeight(sim, ram) > 0);
        }
    }

    [Fact]
    public void WithoutWallsTheSiegeWorkshopTrainsNoRams()
    {
        var (sim, ai, _) = WalledFoe(AiLevel.Normal, walled: false);
        var hall = sim.Economy.TownHall(0)!;
        var players = new[] { ai };
        for (int t = 0; t < 90; t++)
        {
            Stock(hall);
            Play(sim, players, () => false, Tps);
        }
        Assert.True(HasCatapult(sim) || sim.Buildings.All.Any(b => b.Owner == 0 && b.Queue.Count > 0), "the workshop to train");
        Assert.False(HasRam(sim));
    }

    [Fact]
    public void RamsInAWaveGoForTheGateWithSoldiersScreeningThem()
    {
        var (sim, ai, gate) = WalledFoe(AiLevel.Easy, walled: true);
        var u = sim.Units;
        for (int k = 0; k < 8; k++) sim.SpawnUnit(0, "spearman", 18 + k, 72);
        var rams = new[] { sim.SpawnUnit(0, "battering_ram", 20, 76), sim.SpawnUnit(0, "battering_ram", 24, 76) };
        Assert.All(rams, id => Assert.True(id > 0));
        var players = new[] { ai };
        Assert.True(Play(sim, players, () => ai.Waves > 0, AiPlayer.SmartFirstWaveTicks + 3 * 60 * Tps), "a wave to set out");
        Assert.All(rams, id => Assert.Contains(id, ai.Wave));
        bool atGate = false;
        Assert.True(Play(sim, players, () =>
        {
            atGate |= rams.Any(id => u.IndexOf(id) is var i && i >= 0 && u.TargetBuilding[i] == gate!.Id);
            return sim.Buildings.Get(gate!.Id) == null;
        }, 5 * 60 * Tps), "the gate to fall");
        Assert.True(atGate, "the rams to be sent at the gate");
    }
}
