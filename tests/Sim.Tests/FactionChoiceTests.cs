using System;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Replay;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Choosing each player's faction before the match (see <see cref="Simulation.SetFaction(int, string)"/>): any
/// faction for any player, so two players can play the same one, kept in replays.
/// </summary>
public class FactionChoiceTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Tps = Simulation.TicksPerSecond;

    private static Simulation Flat(int size = 64) => TestWorlds.Sim(TestWorlds.Flat(size, 16, size, Ground));

    [Fact]
    public void WithoutAChoicePlayersHaveTheFactionsInDataOrder()
    {
        var sim = Flat();
        Assert.Equal("settlers", sim.FactionOf(0).Id);
        Assert.Equal("orcs", sim.FactionOf(1).Id);
        Assert.Equal("deepfolk", sim.FactionOf(2).Id);
        Assert.Equal("elves", sim.FactionOf(3).Id);
        Assert.Equal("settlers", sim.FactionOf(4).Id);
        Assert.Same(sim.Content.Wild, sim.FactionOf(Simulation.WildPlayer));
        Assert.Same(sim.FactionOf(1), sim.Economy.Player(1).Faction);
        Assert.Same(sim.FactionOf(1), sim.Economy.Faction(1));
    }

    [Fact]
    public void BothPlayersCanPlayTheOrcs()
    {
        var sim = Flat();
        sim.SetFaction(0, "orcs");
        Assert.Equal("orcs", sim.FactionOf(0).Id);
        Assert.Equal("orcs", sim.FactionOf(1).Id);
        Assert.Equal("orcs", sim.Economy.Player(0).Faction.Id);
        Assert.Equal("orcs", sim.Economy.Faction(0).Id);
        Assert.True(sim.Economy.Player(0).Faction.TamesMonsters);
    }

    /// <summary>
    /// The faction rules follow the choice, not the player: player 1 playing the Settlers earns experience and
    /// evolves its spearman into a knight; player 0 playing the Orcs earns none.
    /// </summary>
    [Fact]
    public void TheFactionRulesFollowTheChoiceNotThePlayer()
    {
        var sim = Flat();
        sim.SetFaction(0, "orcs");
        sim.SetFaction(1, "settlers");
        int settlerSpear = sim.SpawnUnit(1, "spearman", 20, 30);
        int orcSpear = sim.SpawnUnit(0, "spearman", 40, 30);
        var preyForSettler = new[] { sim.SpawnUnit(0, "worker", 21, 30), sim.SpawnUnit(0, "worker", 21, 42), sim.SpawnUnit(0, "worker", 21, 54) };
        int preyForOrc = sim.SpawnUnit(1, "worker", 41, 30);

        sim.Schedule(0, new AttackCommand(0, new[] { orcSpear }, preyForOrc));
        sim.Step();
        foreach (int w in preyForSettler)
        {
            sim.Schedule(sim.Tick, new AttackCommand(1, new[] { settlerSpear }, w));
            for (int t = 0; t < 20 * Tps && sim.Units.Exists(w); t++) sim.Step();
            Assert.False(sim.Units.Exists(w));
        }
        for (int t = 0; t < 20 * Tps && sim.Units.Exists(preyForOrc); t++) sim.Step();
        Assert.False(sim.Units.Exists(preyForOrc));
        sim.Schedule(sim.Tick, new EvolveCommand(1, new[] { settlerSpear }, "knight"));
        sim.Step();

        int s = sim.Units.IndexOf(settlerSpear), o = sim.Units.IndexOf(orcSpear);
        Assert.Equal("knight", sim.Content.Units[sim.Units.Def[s]].Id);
        Assert.True(sim.Units.Xp[s] > 0);
        Assert.Equal(0, sim.Units.Xp[o]); // the Orcs earn no experience, whoever plays them
    }

    [Fact]
    public void FactionsAreChosenBeforeTheFirstTick()
    {
        var sim = Flat();
        sim.Step();
        Assert.Throws<InvalidOperationException>(() => sim.SetFaction(0, "orcs"));
        sim.SetFaction(0, "settlers"); // what it already plays: nothing changes
        Assert.Throws<ArgumentOutOfRangeException>(() => sim.SetFaction(Simulation.WildPlayer, "orcs"));
        Assert.Throws<ArgumentOutOfRangeException>(() => sim.SetFaction(-1, "orcs"));
        Assert.Throws<ArgumentException>(() => sim.SetFaction(1, sim.Content.Wild));
    }

    [Fact]
    public void TheChoiceChangesTheStateHash()
    {
        var a = Flat();
        var b = Flat();
        Assert.Equal(a.StateHash(), b.StateHash());
        b.SetFaction(0, "orcs");
        Assert.NotEqual(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void TheLogKeepsEachPlayersFaction()
    {
        var log = CommandLog.UsualStart(3, 128, 64, 128, factions: new[] { "orcs", "orcs" });
        string text = log.ToString();
        Assert.Contains("faction 0 orcs", text);
        Assert.Contains("faction 1 orcs", text);
        var copy = CommandLog.Parse(text);
        Assert.Equal(new[] { "orcs", "orcs" }, copy.Factions);
        Assert.Equal(text, copy.ToString());
        var sim = copy.CreateSimulation();
        Assert.Equal("orcs", sim.FactionOf(0).Id);
        Assert.Equal("orcs", sim.Economy.Player(0).Faction.Id);

        // A log without faction lines plays the factions in data order, as before.
        var plain = CommandLog.UsualStart(3, 128, 64, 128);
        Assert.DoesNotContain("faction", plain.ToString());
        Assert.Null(CommandLog.Parse(plain.ToString()).Factions);
        Assert.Equal("settlers", plain.CreateSimulation().FactionOf(0).Id);

        // Only player 1 chosen: player 0 keeps the default.
        var one = CommandLog.Parse("rts-log 1\nseed 1\nworld 32 16 32\nticks 10\nfaction 1 settlers\n");
        Assert.Equal(new[] { null, "settlers" }, one.Factions);
        var oneSim = one.CreateSimulation();
        Assert.Equal("settlers", oneSim.FactionOf(0).Id);
        Assert.Equal("settlers", oneSim.FactionOf(1).Id);
    }

    [Fact]
    public void ALogWithAnUnknownFactionDoesNotPlay()
    {
        var log = CommandLog.Parse("rts-log 1\nseed 1\nworld 32 16 32\nticks 10\nfaction 0 dwarves\n");
        Assert.Throws<InvalidOperationException>(() => log.CreateSimulation());
        Assert.Throws<FormatException>(() => CommandLog.Parse("rts-log 1\nseed 1\nworld 32 16 32\nticks 10\nfaction 7 orcs\n"));
        Assert.Throws<FormatException>(() => CommandLog.Parse("rts-log 1\nseed 1\nworld 32 16 32\nticks 10\nfaction 0 or-cs\n"));
    }

    /// <summary>
    /// A mirror match between computer players: Hard plays the Settlers on both sides (walls and all), builds and
    /// trains, and replaying the log plays the same match.
    /// </summary>
    [Fact]
    public void ComputerPlayersPlayAMirrorMatchThatReplaysTheSame()
    {
        var result = AiMatch.Play(3, 256, 64, 256, 4 * 60 * Tps, levels: new[] { AiLevel.Hard }, fog: true,
            factions: new[] { "settlers", "settlers" });
        Assert.Equal(new[] { "settlers", "settlers" }, result.Log.Factions);
        var samples = MatchReplay.Run(CommandLog.Parse(result.Log.ToString()), 1200, out var sim);
        var again = MatchReplay.Run(CommandLog.Parse(result.Log.ToString()), 1200, out _);
        Assert.Equal(samples.Select(h => h.Hash), again.Select(h => h.Hash));
        foreach (int p in sim.Match.Players)
        {
            Assert.Equal("settlers", sim.FactionOf(p).Id);
            int units = 0;
            for (int i = 0; i < sim.Units.Count; i++) if (sim.Units.Owner[i] == p) units++;
            Assert.True(units > Simulation.UsualStartUnits(sim.Content), $"player {p} trained units ({units})");
            Assert.True(sim.Buildings.All.Count(b => b.Owner == p) > 1, $"player {p} built");
        }
    }
}
