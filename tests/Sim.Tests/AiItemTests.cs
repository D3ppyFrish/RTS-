using System;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Sandbox;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Computer players fetch the relics and the Dusk Staff they see lying on the ground (see AiPlayer.Items.cs).</summary>
public class AiItemTests
{
    private const int Ground = 3;
    private const int Tps = Simulation.TicksPerSecond;
    private const int ItemX = 96, ItemZ = 128;

    /// <summary>
    /// A flat map with player 0's Settlers town in the west and player 1's Orcs in the east, each with its starting units,
    /// and a giant's heart dropped by one of player 1's spearmen at (x, z), seen by player 0 unless
    /// <paramref name="seen"/> is false.
    /// </summary>
    /// <remarks>Player 0's worker that saw it is taken off the map once the computer player has looked (see <see cref="Begin"/>).</remarks>
    private static (Simulation Sim, DroppedItem Item, int Scout) Map(bool seen = true, int x = ItemX, int z = ItemZ)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(256, 32, 256, Ground));
        sim.Fog = true;
        sim.TestMode = true;
        sim.SetFaction(0, "settlers");
        sim.SetFaction(1, "orcs");
        sim.Map.Starts.Add(new StartArea(0, 60, Ground + 1, 128));
        sim.Map.Starts.Add(new StartArea(1, 230, Ground + 1, 128));
        sim.PlaceStartingBuildings();
        sim.SpawnStartingUnits(24);
        int bearer = sim.SpawnUnit(1, "spearman", x, z);
        var relic = sim.Content.Relic("giant_heart");
        sim.Units.Relic[sim.Units.IndexOf(bearer)] = (short)(relic.Index + 1);
        int scout = seen ? sim.SpawnUnit(0, "worker", x - 4, z) : 0;
        sim.Schedule(sim.Tick, new SandboxUnitsCommand(0, new[] { bearer }, SandboxAction.Kill));
        for (int t = 0; t < 2 * Tps && sim.Units.Exists(bearer); t++) sim.Step();
        sim.Run(2);
        return (sim, Assert.Single(sim.Items.Items), scout);
    }

    /// <summary>The computer player for player 0 at that level, after it has looked once; then the scout is gone.</summary>
    private static AiPlayer Begin(Simulation sim, int scout, AiLevel level)
    {
        var ai = new AiPlayer(0, 1, level);
        Play(sim, ai, 1, () => false);
        if (scout != 0) sim.Units.Remove(scout);
        return ai;
    }

    private static void Play(Simulation sim, AiPlayer ai, int ticks, Func<bool> done)
    {
        for (int t = 0; t < ticks && !done(); t++)
        {
            ai.Think(sim, c => sim.Schedule(sim.Tick, c));
            sim.Step();
        }
    }

    private static bool Lying(Simulation sim) => sim.Items.Items.Count > 0;

    [Theory]
    [InlineData(AiLevel.Easy)]
    [InlineData(AiLevel.Normal)]
    [InlineData(AiLevel.Hard)]
    public void ItSendsASoldierToTakeUpARelicItSees(AiLevel level)
    {
        var (sim, _, scout) = Map();
        var ai = Begin(sim, scout, level);
        Play(sim, ai, 2 * 60 * Tps, () => !Lying(sim));

        Assert.False(Lying(sim));
        var u = sim.Units;
        int bearer = Enumerable.Range(0, u.Count).Single(i => u.Relic[i] != 0);
        Assert.Equal(0, u.Owner[bearer]);
        Assert.Equal("giant_heart", sim.RelicOf(bearer)!.Id);
        Play(sim, ai, 2 * AiPlayer.ArmyTicks, () => false);
        Assert.Equal(1, ai.ItemsTaken);
        Assert.Equal(0, ai.ItemRunner);
    }

    [Fact]
    public void ItLeavesAnItemItHasNotSeen()
    {
        var (sim, item, scout) = Map(seen: false, x: 110, z: 24); // off the way its scout takes to the rival's town
        var ai = Begin(sim, scout, AiLevel.Normal);
        Play(sim, ai, 30 * Tps, () => false);

        Assert.Equal(0, ai.ItemRunner);
        Assert.Contains(item, sim.Items.Items);
    }

    [Fact]
    public void ItLeavesAnItemARivalSoldierStandsBy()
    {
        var (sim, _, scout) = Map();
        sim.Schedule(sim.Tick, new SandboxBuildingCommand(0, 0, "watchtower", ItemX - 8, ItemZ - 1)); // it keeps watch on the item
        sim.Step();
        int guard = sim.SpawnUnit(1, "spearman", ItemX + 6, ItemZ);
        sim.Schedule(sim.Tick, new StanceCommand(1, new[] { guard }, UnitStance.Hold));
        var ai = Begin(sim, scout, AiLevel.Normal);
        Play(sim, ai, 10 * Tps, () => ai.ItemRunner != 0);

        Assert.Equal(0, ai.ItemRunner);
        Assert.DoesNotContain(ai.Journal, j => j.Text.Contains("take up"));
    }

    [Fact]
    public void TheClassicPlayerLeavesItemsAlone()
    {
        var (sim, item, scout) = Map();
        var ai = Begin(sim, scout, AiLevel.Classic);
        Play(sim, ai, 30 * Tps, () => false);

        Assert.Equal(0, ai.ItemRunner);
        Assert.Equal(0, ai.ItemsTaken);
    }

    [Fact]
    public void TheSameMatchGivesTheSameHash()
    {
        ulong Run()
        {
            var (sim, _, scout) = Map();
            var ai = Begin(sim, scout, AiLevel.Hard);
            Play(sim, ai, 60 * Tps, () => false);
            return sim.StateHash();
        }
        Assert.Equal(Run(), Run());
    }
}
