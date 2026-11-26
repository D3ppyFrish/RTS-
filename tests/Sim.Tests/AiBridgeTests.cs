using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Computer players laying a plank bridge over the river between their town and the enemy's.</summary>
public class AiBridgeTests
{
    private const int Ground = 3;
    private const int Tps = Simulation.TicksPerSecond;
    // The river: deep water right across the map, from x = 92 to 99.
    private const int RiverX0 = 92, RiverX1 = 99;

    private static void Stock(Simulation sim, int player)
    {
        var hall = sim.Economy.TownHall(player);
        if (hall == null) return;
        foreach (var (good, n) in new[] { (Good.Food, 1500), (Good.Wood, 1500), (Good.Stone, 800), (Good.Iron, 200) })
            hall.Stock[(int)good] = Math.Max(hall.Stock[(int)good], n);
    }

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
    /// Player 0 in the west and player 1 (who gives no orders) in the east, a river between them; with
    /// <paramref name="ford"/>, the river is dry ground (a ford) from z = 56 to 72, by both towns.
    /// </summary>
    private static Simulation River(bool ford = false)
    {
        var w = TestWorlds.Flat(192, 16, 128, Ground);
        TestWorlds.Box(w, RiverX0, 1, 0, RiverX1, Ground, 127, BlockType.Water);
        if (ford) TestWorlds.Box(w, RiverX0, 1, 56, RiverX1, Ground, 72, BlockType.Grass);
        var sim = TestWorlds.Sim(w);
        sim.Map.Starts.Add(new StartArea(0, 40, Ground + 1, 64));
        sim.Map.Starts.Add(new StartArea(1, 150, Ground + 1, 64));
        sim.PlaceStartingBuildings();
        return sim;
    }

    private static List<Building> Decks(Simulation sim) => sim.Buildings.All.Where(b => b.Def.IsBridge && b.Owner == 0).ToList();

    private static string Journal(AiPlayer ai) => string.Join("; ", ai.Journal.TakeLast(12).Select(j => $"{j.Tick / Tps}s {j.Text}"));

    [Theory]
    [InlineData(AiLevel.Easy)]
    [InlineData(AiLevel.Normal)]
    [InlineData(AiLevel.Hard)]
    public void ItBridgesTheRiverBetweenTheTowns(AiLevel level)
    {
        var sim = River();
        var ai = new AiPlayer(0, 1, level);
        var log = new List<(long, Command)>();
        Assert.True(Play(sim, ai, () => ai.BridgeEnds != null, 8 * 60 * Tps, log), $"{level}: a bridge to be laid: {Journal(ai)}");
        Assert.Contains(log, e => e.Item2 is BridgeCommand);
        var (ax, _, bx, _) = ai.BridgeEnds!.Value;
        Assert.True(ax < RiverX0 && bx > RiverX1, $"{level}: from the near bank ({ax}) to the far one ({bx})");
        // A sawmill for the planks came first.
        Assert.Contains(sim.Buildings.All, b => b.Owner == 0 && b.Def.Kind == BuildingKind.Workshop && b.Def.Makes == Good.Planks);

        Assert.True(Play(sim, ai, () => Decks(sim).Count >= RiverX1 - RiverX0 + 1 && Decks(sim).All(b => b.Active), 3 * 60 * Tps, log),
            $"{level}: the bridge to be built: {Decks(sim).Count(b => b.Active)} of {Decks(sim).Count} decks; {Journal(ai)}");
        Assert.Equal(1, ai.BridgesLaid);

        // Anyone can now walk across.
        int walker = sim.SpawnUnit(0, "worker", RiverX0 - 6, 20);
        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { walker }, RiverX1 + 8, 20));
        Assert.True(Play(sim, ai, () => sim.Units.X[sim.Units.IndexOf(walker)].FloorToInt() > RiverX1, 90 * Tps), $"{level}: a walker to cross over the bridge");

        // Standing, it is not laid again.
        Play(sim, ai, () => false, 2 * AiPlayer.BridgeLookTicks, log);
        Assert.Single(log, e => e.Item2 is BridgeCommand);
    }

    [Fact]
    public void AFordByTheTownsNeedsNoBridge()
    {
        var sim = River(ford: true);
        var ai = new AiPlayer(0, 1, AiLevel.Normal);
        var log = new List<(long, Command)>();
        Play(sim, ai, () => false, 6 * 60 * Tps, log);
        Assert.DoesNotContain(log, e => e.Item2 is BridgeCommand);
        Assert.Null(ai.BridgeEnds);
    }

    [Fact]
    public void TheClassicLevelLaysNoBridge()
    {
        var sim = River();
        var ai = new AiPlayer(0, 1, AiLevel.Classic);
        var log = new List<(long, Command)>();
        Play(sim, ai, () => false, 6 * 60 * Tps, log);
        Assert.DoesNotContain(log, e => e.Item2 is BridgeCommand);
    }

    [Fact]
    public void ABridgeKnockedOutIsLaidAgain()
    {
        var sim = River();
        var ai = new AiPlayer(0, 1, AiLevel.Normal);
        var log = new List<(long, Command)>();
        Assert.True(Play(sim, ai, () => Decks(sim).Count > 0 && Decks(sim).All(b => b.Active), 10 * 60 * Tps, log), $"the bridge to be built: {Journal(ai)}");
        // Knock out its end column on the near bank: it falls into the water.
        var mid = Decks(sim).OrderBy(b => b.X).First();
        int knocker = sim.SpawnUnit(0, "spearman", mid.X - 2, mid.Z + 2);
        sim.Schedule(sim.Tick, new AttackBuildingCommand(0, new[] { knocker }, mid.Id));
        Assert.True(Play(sim, ai, () => sim.Buildings.Get(mid.Id) == null, 90 * Tps, log), "the deck column to be knocked out");
        sim.Schedule(sim.Tick, new StopCommand(0, new[] { knocker }));
        Assert.True(Play(sim, ai, () => log.Count(e => e.Item2 is BridgeCommand) >= 2, 3 * AiPlayer.BridgeLookTicks + 60 * Tps, log), $"the bridge to be laid again: {Journal(ai)}");
    }

    [Fact]
    public void ABridgeLaidReplaysTheSameFromItsCommands()
    {
        var sim = River();
        var ai = new AiPlayer(0, 1, AiLevel.Normal);
        var log = new List<(long Tick, Command C)>();
        int ticks = 6 * 60 * Tps;
        Play(sim, ai, () => false, ticks, log);
        Assert.Contains(log, e => e.C is BridgeCommand);

        var again = River();
        int next = 0;
        for (int t = 0; t < ticks; t++)
        {
            Stock(again, 0);
            while (next < log.Count && log[next].Tick == again.Tick) again.Schedule(again.Tick, log[next++].C);
            again.Step();
        }
        Assert.Equal(sim.StateHash(), again.StateHash());
    }
}
