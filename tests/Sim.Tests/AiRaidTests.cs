using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Economy;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Computer players' waves capture or burn the rival storehouses they pass that nobody guards.</summary>
public class AiRaidTests
{
    private const int Ground = 3;
    private const int Tps = Simulation.TicksPerSecond;

    /// <summary>
    /// Player 0, a computer player, with eighteen spearmen, against player 1, who gives no orders; a storehouse of player 1's
    /// stands at (sx, 58), by the way between the two halls.
    /// </summary>
    private static (Simulation Sim, Building Store) Scene(int sx, int goods = 0)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(128, 16, 128, Ground));
        sim.Map.Starts.Add(new StartArea(0, 24, Ground + 1, 64));
        sim.Map.Starts.Add(new StartArea(1, 104, Ground + 1, 64));
        sim.PlaceStartingBuildings();
        for (int k = 0; k < 18; k++) sim.SpawnUnit(0, "spearman", 14 + k, 72 + k % 2 * 2);
        var store = sim.Economy.PlaceFinished(1, sim.Content.Building("storehouse"), sx, 58) ?? throw new InvalidOperationException("no room");
        store.Stock[(int)Good.Wood] = goods;
        return (sim, store);
    }

    private static bool Play(Simulation sim, AiPlayer ai, Func<bool> done, int maxTicks, List<Command>? log = null)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return true;
            ai.Think(sim, c =>
            {
                log?.Add(c);
                sim.Schedule(sim.Tick, c);
            });
            sim.Step();
        }
        return done();
    }

    [Fact]
    public void AWaveCapturesARivalStorehouseNearerItsOwnTown()
    {
        // Hard raids from its waves the same way; it only takes longer to send one out.
        foreach (var level in new[] { AiLevel.Easy, AiLevel.Normal })
        {
            var (sim, store) = Scene(52);
            var ai = new AiPlayer(0, 1, level);
            var log = new List<Command>();
            Assert.True(Play(sim, ai, () => ai.Raids > 0, AiPlayer.SmartFirstWaveTicks + 4 * 60 * Tps, log), $"{level}: a raid (waves {ai.Waves})");
            var capture = Assert.Single(log.OfType<CaptureCommand>());
            Assert.Equal(store.Id, capture.Building);
            Assert.Equal(AiPlayer.RaidSize, capture.Units.Length);
            Assert.Empty(log.OfType<BurnCommand>());
            Assert.True(Play(sim, ai, () => store.Owner == 0, 60 * Tps), $"{level}: the storehouse taken");
            Assert.Equal(1, ai.Captures);
            // The party is the wave's again once the storehouse is taken.
            Assert.True(Play(sim, ai, () => ai.RaidParty.Count == 0, 5 * Tps), $"{level}: the party back");
        }
    }

    [Fact]
    public void AWaveCapturesAStockedStorehouseAndBurnsAnEmptyOneNearTheRivalsTown()
    {
        // Full of goods: worth taking even near the rival's town.
        var (sim, store) = Scene(80, goods: AiPlayer.RaidGoods);
        var ai = new AiPlayer(0, 1, AiLevel.Normal);
        var log = new List<Command>();
        Assert.True(Play(sim, ai, () => ai.Raids > 0, AiPlayer.SmartFirstWaveTicks + 4 * 60 * Tps, log), "a raid");
        Assert.Equal(store.Id, Assert.Single(log.OfType<CaptureCommand>()).Building);

        // Empty: burned.
        (sim, store) = Scene(80);
        ai = new AiPlayer(0, 1, AiLevel.Normal);
        log.Clear();
        Assert.True(Play(sim, ai, () => ai.Raids > 0, AiPlayer.SmartFirstWaveTicks + 4 * 60 * Tps, log), "a raid");
        Assert.Equal(store.Id, Assert.Single(log.OfType<BurnCommand>()).Building);
        Assert.Empty(log.OfType<CaptureCommand>());
        int id = store.Id;
        Assert.True(Play(sim, ai, () => sim.Buildings.Get(id) == null, 60 * Tps), "the storehouse burned");
        Assert.Equal(0, ai.Captures);
    }

    [Fact]
    public void TheWaveLeavesItsRaidersToIt()
    {
        var (sim, store) = Scene(52);
        var ai = new AiPlayer(0, 1, AiLevel.Normal);
        var log = new List<Command>();
        Assert.True(Play(sim, ai, () => ai.Raids > 0, AiPlayer.SmartFirstWaveTicks + 4 * 60 * Tps, log), "a raid");
        var party = ai.RaidParty.ToArray();
        int from = log.Count;
        Play(sim, ai, () => store.Owner == 0, 60 * Tps, log);
        // No order but the raid itself named a raider while it was out.
        foreach (var c in log.Skip(from).OfType<UnitsCommand>())
            Assert.DoesNotContain(c.Units, party.Contains);
    }

    [Fact]
    public void NoRaidOnAGuardedStorehouseNorAtClassic()
    {
        // A spearman of player 1's holding beside it.
        var (sim, store) = Scene(52);
        int guard = sim.SpawnUnit(1, "spearman", store.X - 2, store.Z + 2);
        sim.Schedule(0, new StanceCommand(1, new[] { guard }, Units.UnitStance.Hold));
        var ai = new AiPlayer(0, 1, AiLevel.Normal);
        var log = new List<Command>();
        bool raidedGuarded = false;
        Play(sim, ai, () =>
        {
            raidedGuarded |= ai.Raids > 0 && sim.Units.Exists(guard);
            return false;
        }, AiPlayer.SmartFirstWaveTicks + 4 * 60 * Tps, log);
        Assert.True(ai.Waves > 0, "a wave set out");
        Assert.False(raidedGuarded, "no raid while its guard stood");

        // Classic leaves raiding alone.
        (sim, _) = Scene(52);
        var classic = new AiPlayer(0, 1, AiLevel.Classic);
        log.Clear();
        Play(sim, classic, () => false, AiPlayer.SmartFirstWaveTicks + 3 * 60 * Tps, log);
        Assert.Empty(log.OfType<CaptureCommand>());
        Assert.Empty(log.OfType<BurnCommand>());
    }
}
