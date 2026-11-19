using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Computer players weigh monster lairs before attacking them (issue #78): hunting parties for the lairs spread
/// across the map, sent only at a pack they outweigh and called back from one they are losing to, and an
/// opening raid that leaves a lair too strong for the starting soldiers alone.
/// </summary>
public class HuntTests
{
    private const int Tps = Simulation.TicksPerSecond;
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int HallX = 24, HallZ = 128;

    /// <summary>A flat 256 by 256 world with one start (a town hall at (24, 128)) and that many spearmen by it.</summary>
    private static Simulation WithSpearmen(int count)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(256, 16, 256, Ground));
        sim.Map.Starts.Add(new StartArea(0, HallX, Ground + 1, HallZ));
        sim.PlaceStartingBuildings();
        var hall = sim.Economy.TownHall(0)!;
        for (int k = 0; k < count; k++) sim.SpawnUnit(0, "spearman", HallX - 4 + k % 8, hall.MaxZ + 2 + k / 8);
        return sim;
    }

    private static Lair PlaceLair(Simulation sim, string monster, int x, int z) =>
        sim.Monsters.Place(sim.Content.Unit(monster), x, z) ?? throw new InvalidOperationException("no ground for the den");

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

    /// <summary>
    /// With eight spearmen it can spare six: enough for a herd of boar, not for a stone giant. It passes the
    /// giant's lair, though it is nearer, sends just enough at the boar, and clears them.
    /// </summary>
    [Theory]
    [InlineData(AiLevel.Easy)]
    [InlineData(AiLevel.Hard)]
    public void AHuntingPartyGoesForAPackItCanBeatAndPassesOneItCannot(AiLevel level)
    {
        var sim = WithSpearmen(8);
        var giant = PlaceLair(sim, "stone_giant", HallX, HallZ + 100);
        var boar = PlaceLair(sim, "boar", HallX + 126, HallZ - 88);
        var ai = new AiPlayer(0, 1, level);

        Assert.False(Play(sim, ai, () => ai.HuntPartyIds.Count > 0, AiPlayer.HuntTicks - 5 * Tps), "no hunt before its time");
        Assert.True(Play(sim, ai, () => ai.HuntPartyIds.Count > 0, 20 * Tps), "a hunting party to set out");
        Assert.Equal(boar.Id, ai.HuntLair);
        Assert.InRange(ai.HuntPartyIds.Count, AiPlayer.LeastHunt, 8 - AiPlayer.LeastGuard);

        Assert.True(Play(sim, ai, () => boar.Cleared, 3 * 60 * Tps), "the party to clear the boar");
        Assert.Equal(0, boar.ClearedBy);
        Play(sim, ai, () => false, AiPlayer.HuntRetryTicks + 30 * Tps);
        Assert.Equal(1, ai.Hunts);
        Assert.False(giant.Cleared);
        Assert.NotEqual(giant.Id, ai.HuntLair);
    }

    /// <summary>A party that is losing breaks off, comes home, and leaves that lair alone for a while.</summary>
    [Fact]
    public void AHuntingPartyThatIsLosingBreaksOffAndLeavesTheLairAlone()
    {
        var sim = WithSpearmen(8);
        var boar = PlaceLair(sim, "boar", HallX + 126, HallZ - 88);
        var ai = new AiPlayer(0, 1, AiLevel.Normal);
        Assert.True(Play(sim, ai, () => ai.HuntPartyIds.Count > 0, AiPlayer.HuntTicks + 20 * Tps), "a hunting party to set out");
        var party = ai.HuntPartyIds.ToArray();

        // The party takes a beating on the way: what is left of it is worth less than the boar.
        var u = sim.Units;
        foreach (int id in party) u.Hp[u.IndexOf(id)] = 5;
        Assert.True(Play(sim, ai, () => ai.HuntPartyIds.Count == 0, 5 * Tps), "the party to break off");
        Assert.Equal(1, ai.HuntsBrokenOff);
        Assert.False(boar.Cleared);

        // Healed again, they still leave that lair alone for a while.
        foreach (int id in party)
            if (u.IndexOf(id) is >= 0 and var i) u.Hp[i] = sim.Content.Units[u.Def[i]].MaxHp;
        Assert.False(Play(sim, ai, () => ai.HuntPartyIds.Count > 0, AiPlayer.HuntRetryTicks + 60 * Tps), "no party back to the lair it broke off from");
    }

    /// <summary>The opening raid on the nearest lair is only sent at a pack the starting soldiers outweigh.</summary>
    [Theory]
    [InlineData("wolf", true)]
    [InlineData("stone_giant", false)]
    public void TheOpeningRaidWeighsTheLairFirst(string monster, bool raids)
    {
        var sim = WithSpearmen(8);
        PlaceLair(sim, monster, HallX + 40, HallZ);
        var ai = new AiPlayer(0, 1, AiLevel.Classic);
        Play(sim, ai, () => false, 2);
        Assert.Equal(raids, ai.Wave.Count > 0);
    }
}
