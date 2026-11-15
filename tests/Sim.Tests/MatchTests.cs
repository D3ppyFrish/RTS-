using System;
using System.Linq;
using Rts.Sim.Economy;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Who is in the match and who wins it (see <see cref="MatchSystem"/>).</summary>
public class MatchTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4

    /// <summary>A flat world with a start area (and so a town hall) for each player given, in a row along x.</summary>
    private static Simulation WithStarts(params int[] players)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(96, 16, 96, Ground));
        for (int k = 0; k < players.Length; k++)
            sim.Map.Starts.Add(new StartArea(players[k], 16 + 30 * k, Ground + 1, 48));
        sim.PlaceStartingBuildings();
        return sim;
    }

    private static Building Hall(Simulation sim, int player) =>
        Assert.Single(sim.Buildings.All, b => b.Owner == player && b.Active && b.Def.Kind == BuildingKind.TownHall);

    /// <summary>Digs out every block of the building this tick, so it collapses.</summary>
    private static void Raze(Simulation sim, Building b)
    {
        foreach (var t in b.Def.Blocks)
            sim.Schedule(sim.Tick, new DigCommand(b.Owner, b.X + t.X, b.Y + t.Y, b.Z + t.Z));
    }

    [Fact]
    public void EveryPlayerGivenATownHallIsInTheMatch()
    {
        var sim = WithStarts(1, 0, 2);
        Assert.Equal(new[] { 0, 1, 2 }, sim.Match.Players);
        Assert.All(sim.Match.Players, p => Assert.True(sim.Match.InPlay(p)));
        Assert.False(sim.Match.InPlay(3));
        Assert.False(sim.Match.Over);
        Assert.Equal(-1, sim.Match.Winner);
        Assert.Equal(-1, sim.Match.EndTick);
    }

    [Fact]
    public void APlayerWithNoTownHallLeftIsOutAndTheLastOneInWins()
    {
        var sim = WithStarts(0, 1, 2);
        sim.Run(10);
        Raze(sim, Hall(sim, 1));
        long first = sim.Tick;
        sim.Step();
        Assert.Null(sim.Economy.TownHall(1));
        Assert.False(sim.Match.InPlay(1));
        Assert.Equal(first, sim.Match.OutAt(1));
        Assert.False(sim.Match.Over); // two are still in
        Assert.Equal(-1, sim.Match.OutAt(0));

        sim.Run(10);
        Raze(sim, Hall(sim, 0));
        long second = sim.Tick;
        sim.Step();
        Assert.True(sim.Match.Over);
        Assert.Equal(2, sim.Match.Winner);
        Assert.Equal(second, sim.Match.EndTick);
        Assert.Equal(second, sim.Match.OutAt(0));
        Assert.True(sim.Match.InPlay(2));

        // Once it is over, it stays decided.
        Raze(sim, Hall(sim, 2));
        sim.Step();
        Assert.Equal(2, sim.Match.Winner);
        Assert.Equal(second, sim.Match.EndTick);
    }

    [Fact]
    public void TheLastPlayersGoingOutOnTheSameTickIsADraw()
    {
        var sim = WithStarts(0, 1);
        Raze(sim, Hall(sim, 0));
        Raze(sim, Hall(sim, 1));
        sim.Step();
        Assert.True(sim.Match.Over);
        Assert.Equal(-1, sim.Match.Winner);
        Assert.Equal(0, sim.Match.EndTick);
    }

    [Fact]
    public void AnotherFinishedTownHallKeepsAPlayerInButASiteDoesNot()
    {
        var sim = WithStarts(0, 1);
        var first = Hall(sim, 0);
        Assert.NotNull(sim.Economy.PlaceFinished(0, sim.Content.Building("town_hall"), 12, 70));
        Raze(sim, first);
        sim.Step();
        Assert.Null(sim.Buildings.Get(first.Id));
        Assert.True(sim.Match.InPlay(0));
        Assert.False(sim.Match.Over);

        // Player 1 lays out a new town hall, but a site does not count.
        var hall1 = Hall(sim, 1);
        hall1.Stock[(int)Good.Wood] += 200;
        hall1.Stock[(int)Good.Stone] += 100;
        sim.Schedule(sim.Tick, new BuildCommand(1, Array.Empty<int>(), "town_hall", 50, 70));
        sim.Step();
        Assert.Contains(sim.Buildings.All, b => b.Owner == 1 && !b.Active && b.Def.Kind == BuildingKind.TownHall);
        Raze(sim, hall1);
        sim.Step();
        Assert.True(sim.Match.Over);
        Assert.Equal(0, sim.Match.Winner);
    }

    [Fact]
    public void APlayerWhoSurrendersIsOutAndTheOtherWins()
    {
        var sim = WithStarts(0, 1);
        sim.Run(10);
        long tick = sim.Tick;
        sim.Schedule(tick, new SurrenderCommand(1));
        sim.Step();
        Assert.True(sim.Match.Over);
        Assert.Equal(0, sim.Match.Winner);
        Assert.Equal(tick, sim.Match.EndTick);
        Assert.Equal(tick, sim.Match.OutAt(1));
        Assert.NotNull(sim.Economy.TownHall(1)); // their town stays as it is

        // The winner giving up afterwards changes nothing.
        sim.Schedule(sim.Tick, new SurrenderCommand(0));
        sim.Step();
        Assert.Equal(0, sim.Match.Winner);
        Assert.True(sim.Match.InPlay(0));
    }

    [Fact]
    public void ASurrenderCountsOnlyForAPlayerStillInTheMatch()
    {
        var sim = WithStarts(0, 1, 2);
        sim.Schedule(0, new SurrenderCommand(3)); // not in the match
        sim.Schedule(0, new SurrenderCommand(2));
        sim.Step();
        Assert.Equal(new[] { 0, 1, 2 }, sim.Match.Players);
        Assert.False(sim.Match.InPlay(2));
        Assert.False(sim.Match.Over); // two are still in

        // Giving up again does not move when they went out.
        sim.Schedule(sim.Tick, new SurrenderCommand(2));
        sim.Step();
        Assert.Equal(0, sim.Match.OutAt(2));

        // The last two give up on the same tick: a draw.
        long end = sim.Tick;
        sim.Schedule(end, new SurrenderCommand(1));
        sim.Schedule(end, new SurrenderCommand(0));
        sim.Step();
        Assert.True(sim.Match.Over);
        Assert.Equal(-1, sim.Match.Winner);
        Assert.Equal(end, sim.Match.EndTick);
    }

    [Fact]
    public void AMatchWithFewerThanTwoPlayersNeverEnds()
    {
        var sandbox = TestWorlds.Sim(TestWorlds.Flat(64, 16, 64, Ground));
        sandbox.Run(20);
        Assert.Empty(sandbox.Match.Players);
        Assert.False(sandbox.Match.Over);

        var alone = WithStarts(0);
        Raze(alone, Hall(alone, 0));
        alone.Step();
        Assert.Null(alone.Economy.TownHall(0));
        Assert.False(alone.Match.Over);
        Assert.Equal(-1, alone.Match.Winner);
    }

    [Fact]
    public void WhoIsOutIsPartOfTheStateHash()
    {
        var a = WithStarts(0, 1, 2);
        var b = WithStarts(0, 1, 2);
        ulong before = a.Match.ComputeHash();
        Assert.Equal(before, b.Match.ComputeHash());
        Raze(a, Hall(a, 2));
        Raze(b, Hall(b, 1));
        a.Step();
        b.Step();
        Assert.False(a.Match.InPlay(2));
        Assert.False(b.Match.InPlay(1));
        Assert.NotEqual(before, a.Match.ComputeHash());
        Assert.NotEqual(a.Match.ComputeHash(), b.Match.ComputeHash());
    }
}
