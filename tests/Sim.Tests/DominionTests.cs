using System.Linq;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Dominion: taking the ancient sites (the old ruins) and winning by holding most of them (see <see cref="DominionSystem"/>).</summary>
public class DominionTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int P0 = 0, P1 = 1;
    private static readonly (int X, int Z)[] Sites = { (32, 100), (64, 100), (96, 100) };

    /// <summary>A flat world with three ruins in a row and a start (town hall) for each of two players.</summary>
    private static Simulation WithSites(bool dominion = true, int sites = 3)
    {
        var map = new RegionMap(1, TestWorlds.Flat(128, 16, 128, Ground));
        for (int k = 0; k < sites; k++) map.Ruins.Add((Sites[k].X, Ground, Sites[k].Z));
        map.Starts.Add(new StartArea(P0, 24, Ground + 1, 30));
        map.Starts.Add(new StartArea(P1, 104, Ground + 1, 30));
        var sim = new Simulation(map) { Dominion = dominion };
        sim.PlaceStartingBuildings();
        return sim;
    }

    /// <summary>A unit standing in the site, holding its ground (so it does not walk off after enemies it sees).</summary>
    private static int Put(Simulation sim, int player, int site, string unit = "spearman", int dx = 0)
    {
        int id = sim.SpawnUnit(player, unit, Sites[site].X + dx, Sites[site].Z, Ground + 1);
        sim.Units.Stance[sim.Units.IndexOf(id)] = UnitStance.Hold;
        return id;
    }

    private static AncientRuin Site(Simulation sim, int k) => sim.Ruins.Ruins[k];

    private static void Remove(Simulation sim, int id) => sim.Units.Remove(id);

    [Fact]
    public void FightingUnitsAloneInASiteTakeItAfterTheClaimTime()
    {
        var sim = WithSites();
        Assert.True(sim.Sites.Active);
        Assert.Equal(2, sim.Sites.Needed);
        Put(sim, P0, 0);
        sim.Run(DominionSystem.ClaimTicks - 1);
        Assert.Equal(-1, Site(sim, 0).Holder);
        Assert.Equal(P0, Site(sim, 0).Claimant);
        Assert.Equal(DominionSystem.ClaimTicks - 1, Site(sim, 0).Claim);
        sim.Step();
        Assert.Equal(P0, Site(sim, 0).Holder);
        Assert.Equal(1, sim.Sites.Held(P0));
        var e = Assert.Single(sim.Events.All, e => e.Kind == GameEventKind.SiteTaken);
        Assert.Equal((P0, 0), (e.Player, e.Def));
    }

    [Fact]
    public void WorkersDoNotTakeASite()
    {
        var sim = WithSites();
        Put(sim, P0, 0, "worker");
        sim.Run(DominionSystem.ClaimTicks + 10);
        Assert.Equal(-1, Site(sim, 0).Holder);
        Assert.Equal(-1, Site(sim, 0).Claimant);
    }

    [Fact]
    public void AFoughtOverSiteStandsStillAndARivalAloneTakesItFromTheHolder()
    {
        var sim = WithSites();
        Put(sim, P0, 0);
        sim.Run(DominionSystem.ClaimTicks / 2);
        int claim = Site(sim, 0).Claim;

        // A rival walks in: both inside, so it is fought over and the claim stands still.
        int theirs = Put(sim, P1, 0, dx: 1);
        sim.Run(Simulation.TicksPerSecond);
        Assert.Equal(P0, Site(sim, 0).Claimant);
        Assert.Equal(claim, Site(sim, 0).Claim);
        Assert.Equal(-1, Site(sim, 0).Holder);

        // The rival is gone: the claim goes on from where it stood.
        Remove(sim, theirs);
        sim.Run(DominionSystem.ClaimTicks - claim + 2);
        Assert.Equal(P0, Site(sim, 0).Holder);

        // With the holder's units gone, a rival alone takes it from them.
        foreach (int id in Enumerable.Range(0, sim.Units.Count).Where(i => sim.Units.Owner[i] == P0).Select(i => sim.Units.Id[i]).ToList())
            Remove(sim, id);
        sim.Run(2);
        Assert.Equal(P0, Site(sim, 0).Holder); // held with nobody in it
        Put(sim, P1, 0);
        sim.Run(DominionSystem.ClaimTicks + 5);
        Assert.Equal(P1, Site(sim, 0).Holder);
        var lost = Assert.Single(sim.Events.All, e => e.Kind == GameEventKind.SiteLost);
        Assert.Equal((P0, P1), (lost.Player, lost.Subject));
    }

    [Fact]
    public void LeavingBeforeTheClaimIsDoneStartsItAgain()
    {
        var sim = WithSites();
        int id = Put(sim, P0, 0);
        sim.Run(DominionSystem.ClaimTicks / 2);
        Remove(sim, id);
        sim.Run(2);
        Assert.Equal(0, Site(sim, 0).Claim);
        Assert.Equal(-1, Site(sim, 0).Claimant);
        Put(sim, P0, 0);
        sim.Run(DominionSystem.ClaimTicks / 2 + 5);
        Assert.Equal(-1, Site(sim, 0).Holder);
    }

    [Fact]
    public void HoldingMostSitesLongEnoughWinsTheMatch()
    {
        var sim = WithSites();
        Put(sim, P0, 0);
        Put(sim, P0, 1);
        sim.Run(DominionSystem.ClaimTicks + 1);
        Assert.Equal(2, sim.Sites.Held(P0));
        Assert.Equal(P0, sim.Sites.Leader);
        Assert.Contains(sim.Events.All, e => e.Kind == GameEventKind.DominionStarted && e.Player == P1 && e.Subject == P0);
        Assert.Contains(sim.Events.All, e => e.Kind == GameEventKind.DominionStarted && e.Player == P0 && e.Subject == P0);

        sim.Run(DominionSystem.DominionTicks - 3);
        Assert.False(sim.Match.Over);
        Assert.True(sim.Sites.TicksLeft > 0);
        sim.Run(3);
        Assert.True(sim.Match.Over);
        Assert.Equal(P0, sim.Match.Winner);
        Assert.True(sim.Match.ByDominion);
        Assert.False(sim.Match.InPlay(P1));
        Assert.NotNull(sim.Economy.TownHall(P1)); // out by dominion, with the town hall still standing
    }

    [Fact]
    public void LosingASiteStopsTheClockAndSetsItBack()
    {
        var sim = WithSites();
        int a = Put(sim, P0, 0);
        Put(sim, P0, 1);
        sim.Run(DominionSystem.ClaimTicks + DominionSystem.DominionTicks / 2);
        Assert.Equal(P0, sim.Sites.Leader);
        Assert.True(sim.Sites.Clock > DominionSystem.DominionTicks / 3);

        Remove(sim, a);
        sim.Run(2);
        Put(sim, P1, 0);
        sim.Run(DominionSystem.ClaimTicks + 1);
        Assert.Equal(P1, Site(sim, 0).Holder);
        Assert.Equal(-1, sim.Sites.Leader);
        Assert.Equal(0, sim.Sites.Clock);
        sim.Run(DominionSystem.DominionTicks);
        Assert.False(sim.Match.Over);
    }

    [Fact]
    public void OffOrWithTooFewSitesNobodyHoldsThem()
    {
        var off = WithSites(dominion: false);
        Assert.False(off.Sites.Active);
        Put(off, P0, 0);
        off.Run(DominionSystem.ClaimTicks + 5);
        Assert.Equal(-1, Site(off, 0).Holder);

        var few = WithSites(sites: 2);
        Assert.False(few.Sites.Active);
        Put(few, P0, 0);
        Put(few, P0, 1);
        few.Run(DominionSystem.ClaimTicks + 5);
        Assert.Equal(0, few.Sites.Held(P0));
    }

    [Fact]
    public void APlayerWhoIsOutHoldsNothing()
    {
        var sim = WithSites();
        Put(sim, P0, 0);
        sim.Run(DominionSystem.ClaimTicks + 1);
        Assert.Equal(P0, Site(sim, 0).Holder);
        sim.Schedule(sim.Tick, new SurrenderCommand(P0));
        sim.Run(2);
        Assert.Equal(-1, Site(sim, 0).Holder);
    }

    [Fact]
    public void DominionIsPartOfTheStateAndTheLog()
    {
        var a = WithSites();
        var b = WithSites();
        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.NotEqual(a.StateHash(), WithSites(dominion: false).StateHash());
        Put(a, P0, 0);
        Put(b, P0, 0);
        a.Run(DominionSystem.ClaimTicks + 1);
        b.Run(DominionSystem.ClaimTicks);
        Assert.NotEqual(a.StateHash(), b.StateHash());
        b.Step();
        Assert.Equal(a.StateHash(), b.StateHash());

        var log = CommandLog.Parse("rts-log 1\nseed 1\nworld 32 16 32\nticks 10\ndominion 1\n");
        Assert.True(log.Dominion);
        Assert.Contains("dominion 1", log.ToString());
        Assert.True(log.CreateSimulation().Dominion);
        Assert.False(CommandLog.Parse("rts-log 1\nseed 1\nworld 32 16 32\nticks 10\n").Dominion);
        Assert.Throws<System.InvalidOperationException>(() => a.Dominion = false);
    }
}
