using System;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Computer players take their faction's prize at the cleared dens they know of (see AiPlayer.Dens.cs).</summary>
public class AiDenTests
{
    private const int Ground = 3;
    private const int Tps = Simulation.TicksPerSecond;
    private const int DenX = 100, DenZ = 128;

    /// <summary>
    /// A flat map with player 0's town in the west playing <paramref name="faction"/> and player 1's in the east, each
    /// with its starting units, and a cleared lair of <paramref name="monster"/> between them, seen by player 0 unless
    /// <paramref name="seen"/> is false.
    /// </summary>
    private static (Simulation Sim, Lair Lair) Map(string faction, string monster, bool seen = true)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(256, 32, 256, Ground));
        sim.Fog = true;
        sim.SetFaction(0, faction);
        sim.SetFaction(1, faction == "orcs" ? "settlers" : "orcs");
        sim.Map.Starts.Add(new StartArea(0, 60, Ground + 1, 128));
        sim.Map.Starts.Add(new StartArea(1, 230, Ground + 1, 128));
        sim.PlaceStartingBuildings();
        sim.SpawnStartingUnits(24);
        var lair = sim.Monsters.Place(sim.Content.Unit(monster), DenX, DenZ) ?? throw new InvalidOperationException("no ground for the den");
        foreach (int id in lair.Pack.ToArray()) sim.Units.Remove(id);
        sim.Step();
        Assert.True(lair.Cleared);
        if (seen)
        {
            int scout = sim.SpawnUnit(0, "worker", DenX - 4, DenZ);
            sim.Run(2);
            sim.Units.Remove(scout);
        }
        return (sim, lair);
    }

    private static void Play(Simulation sim, AiPlayer ai, int ticks, Func<bool> done)
    {
        for (int t = 0; t < ticks && !done(); t++)
        {
            ai.Think(sim, c => sim.Schedule(sim.Tick, c));
            sim.Step();
        }
    }

    private static string DefOf(Simulation sim, int i) => sim.Content.Units[sim.Units.Def[i]].Id;

    [Theory]
    [InlineData(AiLevel.Easy)]
    [InlineData(AiLevel.Normal)]
    [InlineData(AiLevel.Hard)]
    public void TheSettlersSendASpearmanToBecomeAWolfheart(AiLevel level)
    {
        var (sim, lair) = Map("settlers", "wolf");
        var ai = new AiPlayer(0, 1, level);
        Play(sim, ai, 3 * 60 * Tps, () => lair.Used);

        Assert.True(lair.Used);
        Assert.Equal(0, lair.UsedBy);
        var u = sim.Units;
        Assert.Contains(Enumerable.Range(0, u.Count), i => u.Owner[i] == 0 && DefOf(sim, i) == "wolfheart");
        Play(sim, ai, 2 * AiPlayer.ArmyTicks, () => false);
        Assert.Equal(1, ai.DensUsed);
        Assert.Equal(0, ai.DenRunner);
    }

    [Theory]
    [InlineData(AiLevel.Normal)]
    [InlineData(AiLevel.Hard)]
    public void TheOrcsSendAWorkerToBuildAWolfLodgeOnTheDen(AiLevel level)
    {
        var (sim, lair) = Map("orcs", "wolf");
        var ai = new AiPlayer(0, 1, level);
        Play(sim, ai, 3 * 60 * Tps, () => lair.Used);

        Assert.True(lair.Used);
        var lodge = sim.Buildings.Get(lair.Building);
        Assert.NotNull(lodge);
        Assert.Equal(0, lodge!.Owner);
        Assert.Equal("wolf_lodge", lodge.Def.Id);
        Play(sim, ai, 4 * 60 * Tps, () => lodge.Active);
        Assert.True(lodge.Active);
    }

    [Fact]
    public void ASettlerFighterTakesUpTheGiantsHeart()
    {
        var (sim, lair) = Map("settlers", "stone_giant");
        var ai = new AiPlayer(0, 1, AiLevel.Hard);
        Play(sim, ai, 3 * 60 * Tps, () => lair.Used);

        Assert.True(lair.Used);
        var u = sim.Units;
        Assert.Contains(Enumerable.Range(0, u.Count), i => u.Owner[i] == 0 && sim.RelicOf(i)?.Id == "giant_heart");
    }

    [Fact]
    public void ItLeavesADenItHasNotSeenAndOneWithNoPrizeForItsFaction()
    {
        var (unseen, lair) = Map("settlers", "wolf", seen: false);
        var ai = new AiPlayer(0, 1, AiLevel.Hard);
        Play(unseen, ai, 3 * AiPlayer.ArmyTicks, () => false);
        Assert.Equal(Sight.Unseen, unseen.Vision.At(0, lair.X, lair.Z));
        Assert.Equal(0, ai.DenRunner);
        Assert.False(lair.Used);

        var (sim, giant) = Map("orcs", "stone_giant"); // the Orcs have no use for a giant's den
        var orcs = new AiPlayer(0, 1, AiLevel.Hard);
        Play(sim, orcs, 30 * Tps, () => false);
        Assert.Equal(0, orcs.DenRunner);
        Assert.False(giant.Used);
    }

    [Fact]
    public void TheClassicPlayerLeavesDensBe()
    {
        var (sim, lair) = Map("settlers", "wolf");
        var ai = new AiPlayer(0, 1, AiLevel.Classic);
        Play(sim, ai, 60 * Tps, () => false);
        Assert.False(lair.Used);
    }
}
