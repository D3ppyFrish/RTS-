using System;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Economy;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Computer players search the old ruins (see <c>AiPlayer.Ruins.cs</c>): the smart levels send a small party to
/// the nearest ruin they have seen, keep it off ruins by a lair or held by a stronger force, and bring it home
/// once the ruin is searched or it is outweighed there.
/// </summary>
public class AiRuinTests
{
    private const int Tps = Simulation.TicksPerSecond;
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int HallX = 24, HallZ = 128;

    /// <summary>A flat 256 by 256 world with one start (a town hall at (24, 128)), that many spearmen by it, and ruins at the columns given.</summary>
    private static Simulation WithRuins(int spearmen, params (int X, int Z)[] ruins)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(256, 16, 256, Ground));
        sim.Map.Starts.Add(new StartArea(0, HallX, Ground + 1, HallZ));
        foreach (var (x, z) in ruins) sim.Map.Ruins.Add((x, Ground, z));
        sim.Ruins.Place();
        sim.PlaceStartingBuildings();
        var hall = sim.Economy.TownHall(0)!;
        for (int k = 0; k < spearmen; k++) sim.SpawnUnit(0, "spearman", HallX - 4 + k % 8, hall.MaxZ + 2 + k / 8);
        return sim;
    }

    /// <summary>Steps the match with the computer player thinking before every tick, until done or maxTicks.</summary>
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

    private static int StockOf(Simulation sim)
    {
        int sum = 0;
        foreach (int n in sim.Economy.TownHall(0)!.Stock) sum += n;
        return sum;
    }

    [Theory]
    [InlineData(AiLevel.Easy)]
    [InlineData(AiLevel.Normal)]
    [InlineData(AiLevel.Hard)]
    public void ASmallPartySearchesTheNearestRuinAndComesHome(AiLevel level)
    {
        var sim = WithRuins(8, (HallX + 150, HallZ + 60), (HallX + 90, HallZ - 30));
        var near = sim.Ruins.Ruins[1];
        var ai = new AiPlayer(0, 1, level);

        Assert.False(Play(sim, ai, () => ai.RuinPartyIds.Count > 0, AiPlayer.RuinTicks - 5 * Tps), "no ruin party before its time");
        Assert.True(Play(sim, ai, () => ai.RuinPartyIds.Count > 0, 20 * Tps), "a ruin party to set out");
        Assert.Equal(near.Index, ai.RuinTarget);
        Assert.Equal(AiPlayer.RuinParty, ai.RuinPartyIds.Count);

        int before = StockOf(sim);
        Assert.True(Play(sim, ai, () => near.Searched, 2 * 60 * Tps), "the party to search the ruin");
        Assert.Equal(0, near.SearchedBy);
        if (near.Find!.Goods.Length > 0) Assert.True(StockOf(sim) > before, "the goods in its storage");
        Play(sim, ai, () => false, 2 * Tps);
        Assert.Equal(1, ai.RuinsSearched);
        Assert.Equal(-1, ai.RuinTarget);

        // The next one is the other ruin.
        Assert.True(Play(sim, ai, () => ai.RuinPartyIds.Count > 0, AiPlayer.RuinRetryTicks + 20 * Tps), "a party for the other ruin");
        Assert.Equal(0, ai.RuinTarget);
    }

    [Fact]
    public void TheClassicLevelLeavesRuinsBe()
    {
        var sim = WithRuins(8, (HallX + 60, HallZ));
        var ai = new AiPlayer(0, 1, AiLevel.Classic);
        Play(sim, ai, () => false, AiPlayer.RuinTicks + 60 * Tps);
        Assert.Empty(ai.RuinPartyIds);
        Assert.False(sim.Ruins.Ruins[0].Searched);
    }

    [Fact]
    public void ItSendsNoPartyWithoutSoldiersToSpare()
    {
        var sim = WithRuins(AiPlayer.LeastGuard + AiPlayer.RuinParty - 1, (HallX + 60, HallZ));
        var ai = new AiPlayer(0, 1, AiLevel.Easy);
        Assert.False(Play(sim, ai, () => ai.RuinPartyIds.Count > 0, AiPlayer.RuinTicks + 30 * Tps), "no party: it must keep its guard");
    }

    [Fact]
    public void ItPassesARuinByALairForOneFurtherOff()
    {
        var sim = WithRuins(8, (HallX + 60, HallZ), (HallX + 120, HallZ + 50));
        sim.Monsters.Place(sim.Content.Unit("stone_giant"), HallX + 66, HallZ + 6);
        var ai = new AiPlayer(0, 1, AiLevel.Normal);
        Assert.True(Play(sim, ai, () => ai.RuinPartyIds.Count > 0, AiPlayer.RuinTicks + 20 * Tps), "a ruin party to set out");
        Assert.Equal(1, ai.RuinTarget);
    }

    [Fact]
    public void APartyOutweighedAtTheRuinBreaksOffAndLeavesItAlone()
    {
        var sim = WithRuins(8, (HallX + 100, HallZ));
        var ruin = sim.Ruins.Ruins[0];
        var ai = new AiPlayer(0, 1, AiLevel.Easy);
        Assert.True(Play(sim, ai, () => ai.RuinPartyIds.Count > 0, AiPlayer.RuinTicks + 20 * Tps), "a ruin party to set out");
        var party = ai.RuinPartyIds.ToArray();

        // A rival's soldiers hold the ruin: more than the party.
        for (int k = 0; k < 4; k++) sim.SpawnUnit(1, "spearman", ruin.X - 2 + k, ruin.Z + 2, Ground + 1);
        Assert.True(Play(sim, ai, () => ai.RuinPartyIds.Count == 0, 5 * Tps), "the party to break off");
        Assert.Equal(1, ai.RuinsBrokenOff);
        Assert.False(ruin.Searched);
        Assert.False(Play(sim, ai, () => ai.RuinPartyIds.Count > 0, AiPlayer.RuinRetryTicks + 20 * Tps), "no party back to the ruin it broke off from");
    }

    [Fact]
    public void ItGoesOnlyToARuinItHasSeen()
    {
        var sim = WithRuins(8, (HallX + 200, HallZ + 100));
        sim.Fog = true;
        var ruin = sim.Ruins.Ruins[0];
        var ai = new AiPlayer(0, 1, AiLevel.Normal);
        bool sent = false;
        for (int t = 0; t < AiPlayer.RuinTicks + 3 * 60 * Tps && !sent; t++)
        {
            bool seen = sim.Vision.At(0, ruin.X, ruin.Z) != Sight.Unseen;
            ai.Think(sim, c => sim.Schedule(sim.Tick, c));
            if (ai.RuinPartyIds.Count > 0)
            {
                Assert.True(seen, "a party only for a ruin it has seen");
                sent = true;
            }
            sim.Step();
        }
    }
}
