using System;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Computer players and the dusk tree: the Settlers send a healer to heal it once its warden is gone and bloom with
/// the Dusk Staff; the Orcs and the Deepfolk hack it down while a rival could heal it; hunting parties take on the
/// warden only for a player that wants the tree.
/// </summary>
public class AiDuskTreeTests
{
    private const int Ground = 3;
    private const int Tps = Simulation.TicksPerSecond;
    private const int TreeX = 128, TreeZ = 128;

    /// <summary>
    /// A flat map with the dusk tree and its warden in the middle, player 0's town in the west playing
    /// <paramref name="faction"/> and player 1's in the east playing <paramref name="rival"/>, each with its 24 starting units;
    /// the tree (at <paramref name="treeZ"/>) seen by player 0 unless <paramref name="seen"/> is false.
    /// </summary>
    private static Simulation Map(string faction, string rival, bool seen = true, int treeZ = TreeZ)
    {
        var w = TestWorlds.Flat(256, 32, 256, Ground);
        RegionGenerator.GrowDuskTree(w, TreeX, Ground, treeZ, 5);
        var sim = TestWorlds.Sim(w);
        sim.Fog = true;
        sim.SetFaction(0, faction);
        sim.SetFaction(1, rival);
        sim.Map.Starts.Add(new StartArea(0, 60, Ground + 1, 128));
        sim.Map.Starts.Add(new StartArea(1, 230, Ground + 1, 128));
        sim.PlaceStartingBuildings();
        sim.SpawnStartingUnits(24);
        sim.DuskTree.Place();
        if (seen) See(sim);
        return sim;
    }

    /// <summary>Player 0 sees the tree and its warden.</summary>
    private static void See(Simulation sim)
    {
        int scout = sim.SpawnUnit(0, "worker", sim.DuskTree.X - 4, sim.DuskTree.Z);
        sim.Run(2);
        sim.Units.Remove(scout);
    }

    private static void KillWarden(Simulation sim)
    {
        var lair = sim.Monsters.GetLair(sim.DuskTree.Lair)!;
        foreach (int id in lair.Pack.ToArray()) sim.Units.Remove(id);
        sim.Run(1);
        Assert.True(sim.DuskTree.Unguarded);
    }

    private static void Play(Simulation sim, AiPlayer ai, int ticks, Func<bool>? done = null)
    {
        for (int t = 0; t < ticks && (done == null || !done()); t++)
        {
            ai.Think(sim, c => sim.Schedule(sim.Tick, c));
            sim.Step();
        }
    }

    [Theory]
    [InlineData(AiLevel.Easy)]
    [InlineData(AiLevel.Normal)]
    [InlineData(AiLevel.Hard)]
    public void TheSettlersHealTheTreeOnceItsWardenIsGoneAndBloomWithTheStaff(AiLevel level)
    {
        var sim = Map("settlers", "orcs");
        sim.SpawnUnit(0, "healer", 66, 140);
        var ai = new AiPlayer(0, 1, level);
        Assert.True(ai.WantsDuskTree(sim));
        // While the warden stands, nobody is sent to the tree.
        Play(sim, ai, 10 * Tps);
        Assert.Empty(ai.TreeParty);

        KillWarden(sim);
        Play(sim, ai, 30 * Tps, () => ai.TreeParty.Count > 0);
        Assert.Single(ai.TreeParty);
        int sent = ai.TreeParty[0];
        Assert.True(sim.Content.Units[sim.Units.Def[sim.Units.IndexOf(sent)]].Heal > 0, "a healer sent");
        Play(sim, ai, 90 * Tps, () => sim.DuskTree.Healed);
        Assert.True(sim.DuskTree.Healed, "the tree healed");
        Assert.Equal(0, sim.DuskTree.HealedBy);
        Assert.Equal(sent, sim.DuskTree.StaffBearer);
        Assert.False(ai.WantsDuskTree(sim));
        Play(sim, ai, 2 * Tps);
        Assert.Empty(ai.TreeParty);

        // With nobody hurt round the bearer it keeps the bloom; with allies badly hurt there it blooms.
        var u = sim.Units;
        int[] Allies(int hp)
        {
            int b = u.IndexOf(sent);
            var ids = Enumerable.Range(0, 4)
                .Select(k => sim.SpawnUnit(0, "spearman", u.X[b].FloorToInt() + 1, u.Z[b].FloorToInt() + k - 1, u.Y[b])).ToArray();
            foreach (int id in ids) u.Hp[u.IndexOf(id)] = Math.Min(u.Hp[u.IndexOf(id)], hp);
            return ids;
        }
        Allies(int.MaxValue);
        Play(sim, ai, 3 * Tps);
        Assert.Equal(0, ai.Blooms);
        var allies = Allies(20);
        Play(sim, ai, 2 * Tps, () => ai.Blooms > 0);
        Assert.Equal(1, ai.Blooms);
        Assert.All(allies, id => Assert.True(u.Hp[u.IndexOf(id)] >= 20 + DuskTreeSystem.BloomHeal, "healed by the bloom"));
    }

    [Theory]
    [InlineData("orcs", AiLevel.Normal)]
    [InlineData("orcs", AiLevel.Hard)]
    [InlineData("deepfolk", AiLevel.Normal)]
    public void TheyHackTheTreeDownWhileARivalCouldHealIt(string faction, AiLevel level)
    {
        var sim = Map(faction, "settlers");
        var ai = new AiPlayer(0, 1, level);
        Assert.True(ai.WantsDuskTree(sim));
        KillWarden(sim);
        Play(sim, ai, 30 * Tps, () => ai.TreeParty.Count > 0);
        Assert.InRange(ai.TreeParty.Count, 1, AiPlayer.RazeParty);
        Assert.All(ai.TreeParty, id => Assert.True(sim.DuskTree.Razing(id) || sim.Units.Order[sim.Units.IndexOf(id)] == UnitOrder.Move));
        Play(sim, ai, 2 * 60 * Tps, () => sim.DuskTree.Razed);
        Assert.True(sim.DuskTree.Razed, "the tree hacked down");
        Assert.Equal(0, sim.DuskTree.RazedBy);
        Play(sim, ai, 2 * Tps);
        Assert.Empty(ai.TreeParty);
    }

    [Fact]
    public void NoRivalThatHealsItMeansTheTreeIsLeftAlone()
    {
        var sim = Map("orcs", "deepfolk");
        var ai = new AiPlayer(0, 1, AiLevel.Normal);
        Assert.False(ai.WantsDuskTree(sim));
        KillWarden(sim);
        Play(sim, ai, 30 * Tps);
        Assert.Empty(ai.TreeParty);
        Assert.False(sim.DuskTree.Razed);
    }

    [Fact]
    public void TheClassicLevelLeavesTheTreeAlone()
    {
        var sim = Map("settlers", "orcs");
        sim.SpawnUnit(0, "healer", 66, 140);
        var ai = new AiPlayer(0, 1, AiLevel.Classic);
        Assert.False(ai.WantsDuskTree(sim));
        KillWarden(sim);
        Play(sim, ai, 30 * Tps);
        Assert.Empty(ai.TreeParty);
    }

    [Fact]
    public void TheSettlersWantTheTreeOnlyWithAHealerOrOneToTrain()
    {
        var sim = Map("settlers", "orcs");
        var ai = new AiPlayer(0, 1, AiLevel.Normal);
        var healer = sim.Content.Unit("healer");
        // No healer, and no barracks with the magic to train one: it leaves the tree (and its warden) alone.
        Assert.False(ai.WantsDuskTree(sim));

        Assert.NotNull(sim.Economy.PlaceFinished(0, sim.Content.Building("barracks"), 70, 110));
        foreach (var tech in healer.Requires) sim.Economy.Grant(0, tech);
        Assert.True(sim.Economy.Unlocked(0, healer));
        Assert.True(ai.WantsDuskTree(sim));
    }

    [Theory]
    [InlineData("settlers", true)]
    [InlineData("orcs", false)]
    public void HuntingPartiesTakeOnTheWardenOnlyForAPlayerThatWantsTheTree(string rival, bool wanted)
    {
        // The tree off to the north, found after the opening raid has set out (a known warden can be the opening
        // raid's lair, and the raid looks for lairs towards the middle of the map).
        var sim = Map("orcs", rival, seen: false, treeZ: 40);
        var ai = new AiPlayer(0, 1, AiLevel.Normal);
        Play(sim, ai, 5 * Tps);
        See(sim);
        // Enough soldiers at home to outweigh the warden twice over with the guard left behind.
        for (int k = 0; k < 30; k++) sim.SpawnUnit(0, "spearman", 50 + k % 10, 140 + k / 10);
        int warden = sim.DuskTree.Lair;
        Assert.Equal(wanted, ai.WantsDuskTree(sim));
        bool hunted = false;
        Play(sim, ai, 7 * 60 * Tps, () =>
        {
            hunted |= ai.HuntLair == warden;
            return hunted;
        });
        Assert.Equal(wanted, hunted);
    }
}
