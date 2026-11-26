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
/// Computer players and ships: a dock on the shore, transport ships when the enemy is across the water, waves ferried
/// over, and bombard ships shelling walls by the shore with an escort of archers.
/// </summary>
public class AiShipTests
{
    private const int Ground = 3;
    private const int Tps = Simulation.TicksPerSecond;
    // The strait: deep water right across the map, from x = 80 to 111, with no way round by land.
    private const int SeaX0 = 80, SeaX1 = 111;

    private static void Stock(Simulation sim, int player)
    {
        var hall = sim.Economy.TownHall(player);
        if (hall == null) return;
        foreach (var (good, n) in new[] { (Good.Food, 1500), (Good.Wood, 1500), (Good.Stone, 800), (Good.Planks, 400), (Good.Iron, 200) })
            hall.Stock[(int)good] = Math.Max(hall.Stock[(int)good], n);
    }

    /// <summary>Plays until done (or the ticks run out), keeping player 0 stocked, logging its commands by tick.</summary>
    private static bool Play(Simulation sim, AiPlayer ai, Func<bool> done, int maxTicks, List<(long, Command)>? log = null)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return true;
            Stock(sim, 0);
            ai.Think(sim, c =>
            {
                log?.Add((sim.Tick, c));
                sim.Schedule(sim.Tick, c);
            });
            sim.Step();
        }
        return done();
    }

    /// <summary>
    /// Player 0 in the west and player 1 (who gives no orders) in the east, with a strait between them; with
    /// <paramref name="fleet"/>, player 0 has a dock on the west shore, two ships and 16 spearmen.
    /// </summary>
    private static Simulation Strait(bool fleet)
    {
        var w = TestWorlds.Flat(192, 16, 128, Ground);
        TestWorlds.Box(w, SeaX0, 1, 0, SeaX1, Ground, 127, BlockType.Water);
        var sim = TestWorlds.Sim(w);
        sim.Map.Starts.Add(new StartArea(0, 40, Ground + 1, 64));
        sim.Map.Starts.Add(new StartArea(1, 136, Ground + 1, 64));
        sim.PlaceStartingBuildings();
        if (!fleet) return sim;
        Assert.NotNull(sim.Economy.PlaceFinished(0, sim.Content.Building("dock"), SeaX0 - 5, 50));
        Assert.True(sim.SpawnUnit(0, "ship", SeaX0 + 2, 52) > 0);
        Assert.True(sim.SpawnUnit(0, "ship", SeaX0 + 2, 56) > 0);
        for (int k = 0; k < 16; k++) Assert.True(sim.SpawnUnit(0, "spearman", 40 + k % 8, 74 + k / 8) > 0);
        return sim;
    }

    private static List<int> Units(Simulation sim, int player, Func<UnitDef, bool> kind) =>
        Enumerable.Range(0, sim.Units.Count).Where(i => sim.Units.Owner[i] == player && kind(sim.Content.Units[sim.Units.Def[i]]))
            .Select(i => sim.Units.Id[i]).ToList();

    private static int Across(Simulation sim) =>
        Units(sim, 0, d => d.Id == "spearman").Count(id => sim.Units.X[sim.Units.IndexOf(id)].FloorToInt() > SeaX1);

    [Theory]
    [InlineData(AiLevel.Easy)]
    [InlineData(AiLevel.Hard)]
    public void AWaveWithTheEnemyAcrossTheWaterIsFerriedOverByShip(AiLevel level)
    {
        var sim = Strait(fleet: true);
        var ai = new AiPlayer(0, 1, level) { Bridging = false };
        Assert.True(Play(sim, ai, () => ai.Ferrying, AiPlayer.SmartFirstWaveTicks + 3 * 60 * Tps), "a wave to set out by sea");
        var (lx, _) = ai.FerryLanding!.Value;
        Assert.True(lx > SeaX1, "it lands on the far shore");
        Assert.True(Play(sim, ai, () => Across(sim) >= 8, 3 * 60 * Tps), $"the wave to be put ashore across the water: {Across(sim)} across");
        // Once across, the wave goes on for the enemy town.
        Assert.True(Play(sim, ai, () => !ai.Ferrying, 2 * 60 * Tps), "the ferry to be done");
        var hall = sim.Economy.TownHall(1)!;
        Assert.True(Play(sim, ai, () => Units(sim, 0, d => d.Id == "spearman").Any(id =>
            sim.Units.IndexOf(id) is var i && Math.Abs(sim.Units.X[i].FloorToInt() - hall.X) < 16), 2 * 60 * Tps), "the wave to march on the town");
    }

    [Fact]
    public void HardLiftsSoldiersJoiningItsWaveAcrossTheWater()
    {
        var sim = Strait(fleet: true);
        var ai = new AiPlayer(0, 1, AiLevel.Hard) { Bridging = false };
        Assert.True(Play(sim, ai, () => ai.Ferrying, AiPlayer.SmartFirstWaveTicks + 3 * 60 * Tps), "a wave to set out by sea");
        Assert.True(Play(sim, ai, () => !ai.Ferrying, 4 * 60 * Tps), "the wave to be across");
        Assert.False(ai.Lifting);
        // Soldiers trained at home since: they follow the wave, and the way to it is across the water.
        var fresh = new List<int>();
        for (int k = 0; k < 20; k++) fresh.Add(sim.SpawnUnit(0, "spearman", 36 + k % 10, 70 + k / 10));
        Assert.All(fresh, id => Assert.True(id > 0));
        Assert.True(Play(sim, ai, () => ai.Lifting, 2 * 60 * Tps), "the soldiers joining the wave to be lifted across by ship");
        var (lx, _) = ai.LiftLanding!.Value;
        Assert.True(lx > SeaX1, "they land on the far shore");
        int Over() => fresh.Count(id => sim.Units.IndexOf(id) is >= 0 and var i && sim.Units.Aboard[i] == 0 && sim.Units.X[i].FloorToInt() > SeaX1);
        Assert.True(Play(sim, ai, () => Over() >= AiPlayer.JoinSize, 4 * 60 * Tps), $"those joining the wave to be put ashore across the water: {Over()} across");
        Assert.True(Play(sim, ai, () => !ai.Lifting, 2 * 60 * Tps), "the lift to be done");
    }

    [Fact]
    public void AFerriedWaveReplaysTheSameFromItsCommands()
    {
        // What the computer works out about the water changes nothing in the game: its commands alone play it again.
        var sim = Strait(fleet: true);
        var ai = new AiPlayer(0, 1, AiLevel.Normal) { Bridging = false };
        var log = new List<(long Tick, Command C)>();
        int ticks = AiPlayer.SmartFirstWaveTicks + 3 * 60 * Tps;
        Play(sim, ai, () => false, ticks, log);
        Assert.Contains(log, e => e.C is BoardCommand);

        var again = Strait(fleet: true);
        int next = 0;
        for (int t = 0; t < ticks; t++)
        {
            Stock(again, 0);
            while (next < log.Count && log[next].Tick == again.Tick) again.Schedule(again.Tick, log[next++].C);
            again.Step();
        }
        Assert.Equal(sim.StateHash(), again.StateHash());
    }

    [Theory]
    [InlineData(AiLevel.Normal)]
    [InlineData(AiLevel.Hard)]
    public void WithShoreByItsTownItBuildsADockAndShipsForTheEnemyAcrossTheWater(AiLevel level)
    {
        var sim = Strait(fleet: false);
        Assert.NotNull(sim.Economy.PlaceFinished(0, sim.Content.Building("barracks"), 30, 40));
        for (int k = 0; k < AiPlayer.DockWorkers; k++) sim.SpawnUnit(0, "worker", 40 + k, 90);
        var ai = new AiPlayer(0, 1, level) { Bridging = false };
        Assert.True(Play(sim, ai, () => sim.Buildings.All.Any(b => b.Owner == 0 && AiPlayer.Docks(sim, b.Def)), 3 * 60 * Tps), "a dock to be laid out");
        var dock = sim.Buildings.All.First(b => b.Owner == 0 && AiPlayer.Docks(sim, b.Def));
        Assert.InRange(dock.MaxX, SeaX0 - 4, SeaX0 - 1);
        Assert.False(AiPlayer.Docks(sim, sim.Content.Building("barracks")));
        Assert.True(Play(sim, ai, () => Units(sim, 0, d => d.Id == "ship").Count > 0, 6 * 60 * Tps), "a ship to be built");
        Assert.True(ai.SeaRoute);
        // It is no soldier: the barracks' training and waves leave it out.
        Play(sim, ai, () => false, 2 * 60 * Tps);
        Assert.InRange(Units(sim, 0, d => d.Id == "ship").Count, 1, AiPlayer.ShipsWanted);
    }

    [Fact]
    public void WithNoWaterNearItsTownItBuildsNoDock()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(192, 16, 128, Ground));
        sim.Map.Starts.Add(new StartArea(0, 40, Ground + 1, 64));
        sim.Map.Starts.Add(new StartArea(1, 150, Ground + 1, 64));
        sim.PlaceStartingBuildings();
        Assert.NotNull(sim.Economy.PlaceFinished(0, sim.Content.Building("barracks"), 30, 40));
        for (int k = 0; k < AiPlayer.DockWorkers; k++) sim.SpawnUnit(0, "worker", 40 + k, 90);
        var ai = new AiPlayer(0, 1, AiLevel.Normal) { Bridging = false };
        Play(sim, ai, () => false, 2 * 60 * Tps);
        Assert.DoesNotContain(sim.Buildings.All, b => b.Owner == 0 && AiPlayer.Docks(sim, b.Def));
    }

    [Fact]
    public void BombardShipsShellAWallByTheShoreWithAnEscortOfArchers()
    {
        // A lake along the south of the map; the enemy town by its shore with a stone wall along the water.
        var w = TestWorlds.Flat(192, 16, 160, Ground);
        TestWorlds.Box(w, 0, 1, 0, 191, Ground, 39, BlockType.Water);
        var sim = TestWorlds.Sim(w);
        sim.Map.Starts.Add(new StartArea(0, 40, Ground + 1, 90));
        sim.Map.Starts.Add(new StartArea(1, 150, Ground + 1, 58));
        sim.PlaceStartingBuildings();
        sim.Economy.Grant(0, sim.Content.Tech("smelting"));
        var wall = new List<Building>();
        for (int x = 138; x <= 152; x++) wall.Add(sim.Economy.PlaceFinished(1, sim.Content.Building("stone_wall"), x, 46)!);
        Assert.All(wall, Assert.NotNull);
        Assert.NotNull(sim.Economy.PlaceFinished(0, sim.Content.Building("dock"), 38, 40));
        var bombards = new[] { sim.SpawnUnit(0, "bombard_ship", 40, 36), sim.SpawnUnit(0, "bombard_ship", 44, 36) };
        int escort = sim.SpawnUnit(0, "ship", 48, 37);
        Assert.All(bombards.Append(escort), id => Assert.True(id > 0));
        for (int k = 0; k < 14; k++) sim.SpawnUnit(0, "spearman", 50 + k % 7, 96 + k / 7);
        for (int k = 0; k < 4; k++) sim.SpawnUnit(0, "archer", 20 + k, 60);
        var ai = new AiPlayer(0, 1, AiLevel.Normal) { Bridging = false };

        Assert.True(Play(sim, ai, () => ai.Waves > 0, AiPlayer.SmartFirstWaveTicks + 3 * 60 * Tps), "a wave to set out");
        var ids = wall.Select(b => b.Id).ToHashSet();
        bool escorted = false;
        Assert.True(Play(sim, ai, () =>
        {
            escorted |= ai.Escort == escort && sim.Units.IndexOf(escort) is var e && e >= 0 && sim.Boats.Load(e) > 0;
            return bombards.Any(id => sim.Units.IndexOf(id) is var i && i >= 0
                && sim.Units.Combat[i] == CombatOrder.Attack && ids.Contains(sim.Units.TargetBuilding[i]));
        }, 4 * 60 * Tps), "the bombard ships to shell the wall");
        Assert.True(Play(sim, ai, () => wall.Any(b => sim.Buildings.Get(b.Id) is not { } s || s.Damage > 0), 90 * Tps), "a shell to hit the wall");
        Play(sim, ai, () => escorted |= ai.Escort == escort && sim.Units.IndexOf(escort) is var e && e >= 0 && sim.Boats.Load(e) > 0, 60 * Tps);
        Assert.True(escorted, "archers to escort them aboard a ship");
    }

    [Fact]
    public void TheClassicLevelLeavesShipsAlone()
    {
        var sim = Strait(fleet: true);
        var ai = new AiPlayer(0, 1, AiLevel.Classic) { Bridging = false };
        var log = new List<(long Tick, Command C)>();
        Play(sim, ai, () => false, AiPlayer.FirstWaveTicks + 2 * 60 * Tps, log);
        Assert.DoesNotContain(log, e => e.C is BoardCommand);
        Assert.DoesNotContain(log, e => e.C is TrainCommand t && t.Unit is "ship" or "bombard_ship");
    }
}
