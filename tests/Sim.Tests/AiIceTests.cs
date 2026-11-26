using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Computer players' waves crossing a river on an ice bridge their mages freeze.</summary>
public class AiIceTests
{
    private const int Ground = 3;
    private const int Tps = Simulation.TicksPerSecond;
    // The river: deep water right across the map, from x = 92 to 99, with no way round by land.
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
    /// Player 0 in the west and player 1 (who gives no orders) in the east, a river between them; player 0 has 16
    /// spearmen and, with <paramref name="mage"/>, a mage.
    /// </summary>
    private static Simulation River(bool mage, int width = RiverX1 - RiverX0 + 1)
    {
        var w = TestWorlds.Flat(192, 16, 128, Ground);
        TestWorlds.Box(w, RiverX0, 1, 0, RiverX0 + width - 1, Ground, 127, BlockType.Water);
        var sim = TestWorlds.Sim(w);
        sim.Map.Starts.Add(new StartArea(0, 40, Ground + 1, 64));
        sim.Map.Starts.Add(new StartArea(1, 150, Ground + 1, 64));
        sim.PlaceStartingBuildings();
        for (int k = 0; k < 16; k++) Assert.True(sim.SpawnUnit(0, "spearman", 40 + k % 8, 74 + k / 8) > 0);
        if (mage) Assert.True(sim.SpawnUnit(0, "mage", 46, 76) > 0);
        return sim;
    }

    private static List<int> Spearmen(Simulation sim) =>
        Enumerable.Range(0, sim.Units.Count).Where(i => sim.Units.Owner[i] == 0 && sim.Content.Units[sim.Units.Def[i]].Id == "spearman")
            .Select(i => sim.Units.Id[i]).ToList();

    private static int Across(Simulation sim, int x) =>
        Spearmen(sim).Count(id => sim.Units.X[sim.Units.IndexOf(id)].FloorToInt() > x);

    [Theory]
    [InlineData(AiLevel.Normal)]
    [InlineData(AiLevel.Hard)]
    public void AWaveCrossesTheRiverOnIceItsMageFreezes(AiLevel level)
    {
        var sim = River(mage: true);
        var first = Spearmen(sim);
        var ai = new AiPlayer(0, 1, level) { Bridging = false };
        var log = new List<(long, Command)>();
        Assert.True(Play(sim, ai, () => ai.IceCrossingWave, AiPlayer.SmartFirstWaveTicks + 3 * 60 * Tps, log), $"{level}: a wave to set out over the ice: wave " + ai.Wave.Count + " " + string.Join("; ", ai.Journal.TakeLast(15).Select(j => $"{j.Tick / Tps}s {j.Text}")));
        var (ax, _, bx, _) = ai.IceBanks!.Value;
        Assert.True(ax < RiverX0 && bx > RiverX1, $"{level}: from the near bank ({ax}) to the far one ({bx})");
        Assert.True(Play(sim, ai, () => sim.Spells.IceBridges.Count > 0, 90 * Tps, log), $"{level}: the water to freeze");
        Assert.Contains(log, e => e.Item2 is IceBridgeCommand);
        Assert.Equal(0, sim.Spells.IceBridges[0].Owner);
        Assert.True(sim.Economy.Player(0).Mana < EconomySystem.StartingMana, "it paid mana");
        Assert.True(Play(sim, ai, () => Across(sim, RiverX1) >= 8, 40 * Tps, log), $"{level}: the wave to walk over: {Across(sim, RiverX1)} across: " + string.Join("; ", ai.Journal.TakeLast(10).Select(j => $"{j.Tick / Tps}s {j.Text}")));
        Assert.Equal(1, ai.IceBridgesFrozen);
        Assert.True(Play(sim, ai, () => !ai.IceCrossingWave, 30 * Tps), $"{level}: the crossing to be done");
        // Nobody drowned, and the wave goes on for the enemy town.
        Assert.All(first, id => Assert.True(sim.Units.Exists(id), "none drowned"));
        var hall = sim.Economy.TownHall(1)!;
        Assert.True(Play(sim, ai, () => Spearmen(sim).Any(id =>
            sim.Units.IndexOf(id) is var i && Math.Abs(sim.Units.X[i].FloorToInt() - hall.X) < 16), 2 * 60 * Tps), $"{level}: the wave to march on the town");
    }

    [Fact]
    public void WithoutAMageTheWaveFreezesNothing()
    {
        var sim = River(mage: false);
        var ai = new AiPlayer(0, 1, AiLevel.Normal) { Bridging = false };
        var log = new List<(long, Command)>();
        Play(sim, ai, () => false, AiPlayer.SmartFirstWaveTicks + 2 * 60 * Tps, log);
        Assert.DoesNotContain(log, e => e.Item2 is IceBridgeCommand);
        Assert.False(ai.IceCrossingWave);
    }

    [Fact]
    public void ARiverTooWideToFreezeIsNotTried()
    {
        var sim = River(mage: true, width: SpellSystem.IceLength + 2);
        var ai = new AiPlayer(0, 1, AiLevel.Normal) { Bridging = false };
        var log = new List<(long, Command)>();
        Play(sim, ai, () => false, AiPlayer.SmartFirstWaveTicks + 2 * 60 * Tps, log);
        Assert.DoesNotContain(log, e => e.Item2 is IceBridgeCommand);
        Assert.Empty(sim.Spells.IceBridges);
    }

    [Fact]
    public void EasyLeavesTheIceAlone()
    {
        var sim = River(mage: true);
        var ai = new AiPlayer(0, 1, AiLevel.Easy) { Bridging = false };
        var log = new List<(long, Command)>();
        Play(sim, ai, () => false, AiPlayer.SmartFirstWaveTicks + 2 * 60 * Tps, log);
        Assert.DoesNotContain(log, e => e.Item2 is IceBridgeCommand);
    }

    [Fact]
    public void AnIceCrossingReplaysTheSameFromItsCommands()
    {
        var sim = River(mage: true);
        var ai = new AiPlayer(0, 1, AiLevel.Normal) { Bridging = false };
        var log = new List<(long Tick, Command C)>();
        int ticks = AiPlayer.SmartFirstWaveTicks + 3 * 60 * Tps;
        Play(sim, ai, () => false, ticks, log);
        Assert.Contains(log, e => e.C is IceBridgeCommand);

        var again = River(mage: true);
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
