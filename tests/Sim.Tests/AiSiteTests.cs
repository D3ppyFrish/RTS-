using System;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Computer players and the ancient sites, with dominion on (see <c>AiPlayer.Sites.cs</c>): the smart levels send
/// garrisons to hold sites near home, and everyone they can spare to take back a site from a rival who holds most.
/// </summary>
public class AiSiteTests
{
    private const int Tps = Simulation.TicksPerSecond;
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int HallX = 24, HallZ = 128;
    private static readonly (int X, int Z)[] Sites = { (HallX + 60, HallZ - 40), (HallX + 150, HallZ + 80), (HallX + 160, HallZ - 90) };

    /// <summary>
    /// A flat 256 by 256 world with the computer player's start (a town hall at (24, 128)) and that many spearmen by
    /// it, a rival's start in the far corner, and three ruins, with dominion on.
    /// </summary>
    private static Simulation WithSites(int spearmen)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(256, 16, 256, Ground));
        sim.Dominion = true;
        sim.Map.Starts.Add(new StartArea(0, HallX, Ground + 1, HallZ));
        sim.Map.Starts.Add(new StartArea(1, 232, Ground + 1, 232));
        foreach (var (x, z) in Sites) sim.Map.Ruins.Add((x, Ground, z));
        sim.Ruins.Place();
        sim.PlaceStartingBuildings();
        var hall = sim.Economy.TownHall(0)!;
        for (int k = 0; k < spearmen; k++) sim.SpawnUnit(0, "spearman", HallX - 4 + k % 8, hall.MaxZ + 2 + k / 8);
        return sim;
    }

    private static bool Play(Simulation sim, AiPlayer ai, Func<bool> done, int maxTicks)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return true;
            ai.Think(sim, c => sim.Schedule(sim.Tick, c));
            sim.Step();
        }
        return done();
    }

    [Theory]
    [InlineData(AiLevel.Easy)]
    [InlineData(AiLevel.Normal)]
    public void AGarrisonHoldsTheNearestSite(AiLevel level)
    {
        var sim = WithSites(10);
        // A strong rival army at home, so no attack wave sets out and takes the soldiers away.
        for (int k = 0; k < 30; k++)
        {
            int id = sim.SpawnUnit(1, "spearman", 220 + k % 8, 220 + k / 8, Ground + 1);
            sim.Units.Stance[sim.Units.IndexOf(id)] = UnitStance.Hold;
        }
        var ai = new AiPlayer(0, 1, level);
        Assert.False(Play(sim, ai, () => ai.GarrisonSoldiers > 0, AiPlayer.SiteTicks - 5 * Tps), "no garrison before its time");
        Assert.True(Play(sim, ai, () => ai.GarrisonSoldiers > 0, 20 * Tps), "a garrison to set out");
        Assert.Equal(0, ai.GarrisonSites.First());
        Assert.Equal(AiPlayer.SiteParty, ai.GarrisonSoldiers);
        Assert.True(Play(sim, ai, () => sim.Ruins.Ruins[0].Holder == 0, 60 * Tps), "the garrison to take the site");
        // It stays there, and the next garrison goes to the next site (Normal keeps two, Easy one).
        Play(sim, ai, () => false, 30 * Tps);
        Assert.Equal(0, sim.Ruins.Ruins[0].Holder);
        Assert.Equal(ai.Garrisons, ai.GarrisonSites.Count());
        Assert.Equal(level == AiLevel.Easy ? 1 : 2, ai.Garrisons);
    }

    [Fact]
    public void ItTakesBackASiteFromARivalWhoHoldsMost()
    {
        var sim = WithSites(16);
        var ai = new AiPlayer(0, 1, AiLevel.Normal);
        // The rival holds the two far sites, with a lone spearman in each.
        foreach (int k in new[] { 1, 2 })
        {
            int id = sim.SpawnUnit(1, "spearman", Sites[k].X, Sites[k].Z, Ground + 1);
            sim.Units.Stance[sim.Units.IndexOf(id)] = UnitStance.Hold;
        }
        Assert.True(Play(sim, ai, () => sim.Sites.Leader == 1, DominionSystem.ClaimTicks + 5 * Tps), "the rival to hold most");
        Assert.True(Play(sim, ai, () => ai.GarrisonSoldiers > 4, 5 * Tps), "it sends soldiers to take one back");
        Assert.True(Play(sim, ai, () => sim.Sites.Leader != 1, 2 * 60 * Tps), "the rival's clock to stop");
        Assert.False(sim.Match.Over);
        Assert.Contains(new[] { 1, 2 }, k => sim.Ruins.Ruins[k].Holder == 0);
    }
}
