using System;
using Rts.Sim.Replay;
using Xunit;

namespace Rts.Sim.Tests;

public class ReplayTests
{
    private static CommandLog SmallMatch(ulong seed = 7) =>
        ScriptedMatch.Generate(seed, 64, 32, 64, ticks: 400, startUnits: 20);

    [Fact]
    public void LogRoundTripsThroughText()
    {
        var log = SmallMatch();
        Assert.NotEmpty(log.Commands);
        var copy = CommandLog.Parse(log.ToString());
        Assert.Equal(log.Seed, copy.Seed);
        Assert.Equal((log.SizeX, log.SizeY, log.SizeZ, log.Ticks), (copy.SizeX, copy.SizeY, copy.SizeZ, copy.Ticks));
        Assert.Equal(20, copy.StartUnits);
        Assert.Equal(log.Commands, copy.Commands);
        Assert.Equal(log.ToString(), copy.ToString());
    }

    [Fact]
    public void AttackOrdersAreLogged()
    {
        var log = CommandLog.Parse("rts-log 1\nseed 1\nworld 32 16 32\nticks 10\n5 attack 1 7 101,102\n");
        Assert.Equal(new AttackCommand(1, new[] { 101, 102 }, 7), log.Commands[0].Command);
        Assert.Contains("5 attack 1 7 101,102", log.ToString());
        Assert.Contains(SmallMatch().Commands, c => c.Command is AttackCommand);
    }

    [Fact]
    public void SurrendersAreLogged()
    {
        var log = CommandLog.Parse("rts-log 1\nseed 1\nworld 32 16 32\nticks 10\n5 surrender 1\n");
        Assert.Equal(new SurrenderCommand(1), log.Commands[0].Command);
        Assert.Contains("5 surrender 1", log.ToString());
    }

    [Fact]
    public void ARecordingListsEveryScheduledCommandOnTheTickItIsCarriedOut()
    {
        var played = SmallMatch(5);
        var log = new CommandLog
        {
            Seed = played.Seed, SizeX = played.SizeX, SizeY = played.SizeY, SizeZ = played.SizeZ, StartUnits = played.StartUnits,
        };
        var sim = log.CreateSimulation();
        sim.Recording = log;
        // Every order is given ahead of time; the recording lists each on the tick it is carried out.
        foreach (var (tick, cmd) in played.Commands) sim.Schedule(tick, cmd);
        sim.Run((int)played.Ticks);
        Assert.Equal(played.Commands, log.Commands);
        Assert.Equal(played.Ticks, log.Ticks);
        Assert.Equal(sim.StateHash(), MatchReplay.Run(CommandLog.Parse(log.ToString()), 1000)[^1].Hash);
        Assert.Throws<InvalidOperationException>(() => sim.Recording = new CommandLog()); // only from the first tick
    }

    [Fact]
    public void ARecordingLeavesOutAutomationsOwnOrders()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(64, 16, 64, 3));
        var hall = sim.Economy.PlaceFinished(0, sim.Content.Building("town_hall"), 30, 30)!;
        TestWorlds.Tree(sim.World, 40, 42, 4);
        for (int k = 0; k < 3; k++) sim.SpawnUnit(0, "worker", 30 + k, hall.MaxZ + 2);
        var log = new CommandLog();
        sim.Recording = log;
        var automation = new System.Collections.Generic.List<(long Tick, Command Command)>();
        sim.Automation.Record = automation;
        sim.Schedule(0, new AutomateAllCommand(0, true));
        sim.Run(5 * Simulation.TicksPerSecond);
        Assert.NotEmpty(automation); // automation sent the workers to work...
        Assert.Equal(new LoggedCommand(0, new AutomateAllCommand(0, true)), Assert.Single(log.Commands)); // ...but only the player's order is kept
        Assert.Equal(sim.Tick, log.Ticks);
    }

    [Fact]
    public void TheUsualStartHeaderSetsAMatchUpAsTheGameDoes()
    {
        var log = CommandLog.UsualStart(11, 256, 64, 256);
        Assert.True(log.StartBuildings && log.StartLairs && log.StartCities);
        Assert.Equal(Simulation.UsualStartUnits(log.CreateSimulation().Content), log.StartUnits);
        var game = new Simulation(11, 256, 64, 256);
        var units = game.SetUpUsualStart();
        Assert.NotEmpty(units);
        Assert.NotEmpty(game.Match.Players);
        Assert.Equal(game.StateHash(), log.CreateSimulation().StateHash());
    }

    [Fact]
    public void ReplayIsDeterministic()
    {
        var log = SmallMatch();
        var a = MatchReplay.Run(log, hashEvery: 50);
        var b = MatchReplay.Run(CommandLog.Parse(log.ToString()), hashEvery: 50);
        Assert.Equal(8, a.Count);
        Assert.Equal(-1, MatchReplay.FirstMismatch(a, b));
    }

    [Fact]
    public void ReplayReproducesTheRecordedMatch()
    {
        // The generator runs its own simulation while recording; replaying must reach the same state.
        var log = SmallMatch(3);
        var recorder = log.CreateSimulation();
        foreach (var (tick, cmd) in log.Commands) recorder.Schedule(tick, cmd);
        recorder.Run((int)log.Ticks);
        Assert.Equal(recorder.StateHash(), MatchReplay.Run(log, 1000)[^1].Hash);
    }

    [Fact]
    public void ChangedCommandIsDetected()
    {
        var log = SmallMatch();
        var tampered = CommandLog.Parse(log.ToString());
        int i = tampered.Commands.FindIndex(c => c.Command is DigCommand && c.Tick > 200);
        var d = (DigCommand)tampered.Commands[i].Command;
        tampered.Commands[i] = new LoggedCommand(tampered.Commands[i].Tick, d with { Y = d.Y - 1 });
        int mismatch = MatchReplay.FirstMismatch(MatchReplay.Run(log, 50), MatchReplay.Run(tampered, 50));
        Assert.True(mismatch >= 4, $"expected the first difference after tick 200, got sample {mismatch}");
    }

    [Fact]
    public void ScriptedPlayersOrderTheirUnits()
    {
        var log = SmallMatch();
        Assert.Contains(log.Commands, c => c.Command is MoveCommand m && m.Units.Length > 0);
        Assert.Contains(log.Commands, c => c.Command is StopCommand);
        var sim = log.CreateSimulation();
        Assert.Equal(40, sim.Units.Count);
    }

    [Fact]
    public void ChangedMoveOrderIsDetected()
    {
        var log = SmallMatch();
        var tampered = CommandLog.Parse(log.ToString());
        int i = tampered.Commands.FindIndex(c => c.Command is MoveCommand && c.Tick > 200);
        var m = (MoveCommand)tampered.Commands[i].Command;
        tampered.Commands[i] = new LoggedCommand(tampered.Commands[i].Tick, m with { X = m.X < 32 ? m.X + 20 : m.X - 20 });
        Assert.True(MatchReplay.FirstMismatch(MatchReplay.Run(log, 50), MatchReplay.Run(tampered, 50)) >= 4);
    }

    [Fact]
    public void HashSamplesIncludeFinalTick()
    {
        var log = SmallMatch();
        log.Ticks = 120;
        var samples = MatchReplay.Run(log, 50);
        Assert.Equal(new long[] { 50, 100, 120 }, samples.ConvertAll(s => s.Tick));
    }

    [Theory]
    [InlineData("seed 1\nworld 16 16 16\n")]
    [InlineData("rts-log 1\nworld 16 16 16\n")]
    [InlineData("rts-log 1\nseed 1\nworld 16 16 16\n5 jump 0 1 2 3\n")]
    [InlineData("rts-log 1\nseed 1\nworld 16 16 16\n5 dig 0 1 2 3\n4 dig 0 1 2 3\n")]
    [InlineData("rts-log 1\nseed 1\nworld 16 16 16\n5 place 0 1 2 3 Moonrock\n")]
    [InlineData("rts-log 1\nseed 1\nworld 16 16 16\nstart-units -3\n")]
    [InlineData("rts-log 1\nseed 1\nworld 16 16 16\n5 move 0 1 2 x,y\n")]
    public void BadLogsAreRejected(string text)
    {
        Assert.Throws<FormatException>(() => CommandLog.Parse(text));
    }
}
