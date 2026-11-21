using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Computer players and the discovery techs: every level but Classic researches the ones it has opened, after
/// its first two techs, and with Beast lore it sends workers to the trees beside a lair.
/// </summary>
public class AiDiscoveryTests
{
    private const int Tps = Simulation.TicksPerSecond;
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4

    private static readonly AiLevel[] FairLevels = { AiLevel.Easy, AiLevel.Normal, AiLevel.Hard };

    /// <summary>A flat world with player 0's start (its town hall, rich in goods) at (40, 40).</summary>
    private static Simulation Town(Action<VoxelWorld>? shape = null)
    {
        var world = TestWorlds.Flat(128, 16, 128, Ground);
        shape?.Invoke(world);
        var sim = TestWorlds.Sim(world);
        sim.Map.Starts.Add(new StartArea(0, 40, Ground + 1, 40));
        sim.PlaceStartingBuildings();
        var hall = sim.Economy.TownHall(0)!;
        foreach (var g in new[] { Good.Food, Good.Wood, Good.Stone, Good.Iron }) hall.Stock[(int)g] = 800;
        return sim;
    }

    private static TechDef Tech(Simulation sim, string id) => sim.Content.Techs.First(t => t.Id == id);

    /// <summary>The first two techs of both orders, as if already researched.</summary>
    private static void FirstTwo(Simulation sim)
    {
        Assert.Equal(AiPlayer.TechOrder.Take(AiPlayer.DiscoveriesAfter), AiPlayer.HardTechOrder.Take(AiPlayer.DiscoveriesAfter));
        foreach (var id in AiPlayer.TechOrder.Take(AiPlayer.DiscoveriesAfter)) sim.Economy.Grant(0, Tech(sim, id));
    }

    private static List<Command> Play(Simulation sim, AiPlayer ai, int ticks)
    {
        var log = new List<Command>();
        for (int t = 0; t < ticks; t++)
        {
            ai.Think(sim, c =>
            {
                log.Add(c);
                sim.Schedule(sim.Tick, c);
            });
            sim.Step();
        }
        return log;
    }

    private static string[] Researched(List<Command> log) => log.OfType<ResearchCommand>().Select(c => c.Tech).ToArray();

    [Fact]
    public void EveryLevelButClassicResearchesADiscoveryTechOnceItHasOpenedIt()
    {
        foreach (var level in FairLevels)
        {
            // Nothing discovered: it goes on down its usual order, and no discovery tech is in it.
            var plain = Town();
            FirstTwo(plain);
            var log = Play(plain, new AiPlayer(0, 1, level), 20 * Tps);
            Assert.NotEmpty(Researched(log));
            Assert.DoesNotContain(Researched(log), id => Tech(plain, id).Discovery != Discovery.None);

            // A ruin searched: Wardens' charts comes next, ahead of the rest of its order.
            var sim = Town();
            FirstTwo(sim);
            sim.Economy.Discover(0, Discovery.Ruin);
            log = Play(sim, new AiPlayer(0, 1, level), 20 * Tps);
            Assert.Equal("wardens_charts", Researched(log).FirstOrDefault());
            var charts = Tech(sim, "wardens_charts");
            Play(sim, new AiPlayer(0, 1, level), charts.ResearchTicks + 2 * Tps);
            Assert.True(sim.Economy.HasTech(0, charts), $"{level} to have Wardens' charts");
        }
    }

    [Fact]
    public void ItResearchesTheDiscoveryTechsOnlyAfterItsFirstTwo()
    {
        foreach (var level in FairLevels)
        {
            var sim = Town();
            sim.Economy.Discover(0, Discovery.Lair);
            var log = Play(sim, new AiPlayer(0, 1, level), 5 * 60 * Tps);
            var order = Researched(log);
            int lore = Array.IndexOf(order, "beast_lore");
            Assert.True(lore >= AiPlayer.DiscoveriesAfter, $"{level}: {string.Join(", ", order)}");
            Assert.Equal(AiPlayer.TechOrder.Take(AiPlayer.DiscoveriesAfter), order.Take(AiPlayer.DiscoveriesAfter));
        }
    }

    [Fact]
    public void TheClassicLevelNeverResearchesThem()
    {
        var sim = Town();
        FirstTwo(sim);
        sim.Economy.Discover(0, Discovery.Ruin);
        sim.Economy.Discover(0, Discovery.Lair);
        var log = Play(sim, new AiPlayer(0, 1, AiLevel.Classic), 60 * Tps);
        Assert.NotEmpty(Researched(log));
        Assert.DoesNotContain(Researched(log), id => Tech(sim, id).Discovery != Discovery.None);
    }

    [Fact]
    public void WithBeastLoreItSendsWorkersToTheTreesBesideALair()
    {
        // The only trees are a few columns from a wolf den: it keeps its workers off them, until it has Beast lore.
        foreach (bool lore in new[] { false, true })
        {
            var sim = Town(w =>
            {
                for (int k = 0; k < 4; k++) TestWorlds.Tree(w, 62 + 3 * k, 40, 5);
            });
            Assert.NotNull(sim.Monsters.Place(sim.Content.Unit("wolf"), 70, 50));
            if (lore) sim.Economy.Grant(0, Tech(sim, "beast_lore"));
            var log = Play(sim, new AiPlayer(0, 1, AiLevel.Easy), 30 * Tps);
            bool chops = log.OfType<GatherCommand>().Any(g => g.X >= 60 && g.X <= 74 && g.Z >= 38 && g.Z <= 42);
            Assert.Equal(lore, chops);
        }
    }
}
