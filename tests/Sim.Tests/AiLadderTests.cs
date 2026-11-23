using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Computer players and siege ladders: a wave held up by an enemy wall, or a cliff, has its sappers raise a ladder,
/// and its soldiers climb it, one holding the top while the rest go on over.
/// </summary>
public class AiLadderTests
{
    private const int Ground = 3;
    private const int Tps = Simulation.TicksPerSecond;
    private const int WallX = 80;

    private static bool Play(Simulation sim, AiPlayer ai, Func<bool> done, int maxTicks)
    {
        var hall = sim.Economy.TownHall(ai.Player)!;
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return true;
            hall.Stock[(int)Good.Wood] = Math.Max(hall.Stock[(int)Good.Wood], 200);
            hall.Stock[(int)Good.Food] = Math.Max(hall.Stock[(int)Good.Food], 200);
            ai.Think(sim, c => sim.Schedule(sim.Tick, c));
            sim.Step();
        }
        return done();
    }

    /// <summary>
    /// Player 0 (a computer player of the level) in the west with 8 spearmen and 2 sappers, against player 1 in the
    /// east, who gives no orders, behind a stone wall right across the map (or, with <paramref name="cliff"/>, up on a
    /// cliff 5 levels high) at x = 80, with no way round it.
    /// </summary>
    private static (Simulation Sim, AiPlayer Ai, List<int> Soldiers, int[] Sappers) Setup(AiLevel level, bool cliff)
    {
        const int size = 128;
        var w = TestWorlds.Flat(size, 32, size, Ground);
        // Bedrock, so its own workers do not quarry their way into the cliff for stone.
        if (cliff) TestWorlds.Box(w, WallX, Ground + 1, 0, size - 1, Ground + 5, size - 1, BlockType.Bedrock);
        var sim = TestWorlds.Sim(w);
        sim.Map.Starts.Add(new StartArea(0, 24, Ground + 1, 64));
        sim.Map.Starts.Add(new StartArea(1, 104, cliff ? Ground + 6 : Ground + 1, 64));
        sim.PlaceStartingBuildings();
        if (!cliff)
        {
            for (int z = 1; z < size - 1; z++) Assert.NotNull(sim.Economy.PlaceFinished(1, sim.Content.Building("stone_wall"), WallX, z));
            foreach (int z in new[] { 0, size - 1 }) TestWorlds.Box(w, WallX, Ground + 1, z, WallX, 14, z, BlockType.Bedrock);
        }
        var soldiers = Enumerable.Range(0, 8).Select(k => sim.SpawnUnit(0, "spearman", 40 + k, 60)).ToList();
        var sappers = new[] { sim.SpawnUnit(0, "sapper", 40, 66), sim.SpawnUnit(0, "sapper", 42, 66) };
        Assert.All(soldiers.Concat(sappers), id => Assert.True(id > 0));
        return (sim, new AiPlayer(0, 1, level), soldiers, sappers);
    }

    private static int Past(Simulation sim, IEnumerable<int> ids) =>
        ids.Count(id => sim.Units.IndexOf(id) is var i && i >= 0 && sim.Units.X[i].FloorToInt() > WallX);

    [Theory]
    [InlineData(AiLevel.Easy)]
    [InlineData(AiLevel.Normal)]
    [InlineData(AiLevel.Hard)]
    public void AWaveHeldUpByAWallRaisesALadderAndGoesOverIt(AiLevel level)
    {
        var (sim, ai, soldiers, sappers) = Setup(level, cliff: false);
        Assert.True(Play(sim, ai, () => ai.Waves > 0, AiPlayer.SmartFirstWaveTicks + 3 * 60 * Tps), "a wave to set out");
        Assert.True(Play(sim, ai, () => ai.WaveLadder(sim) is { Done: true }, 3 * 60 * Tps), "the sappers to raise a ladder");
        var ladder = ai.WaveLadder(sim)!;
        Assert.Equal(0, ladder.Owner);
        Assert.Equal(WallX, ladder.FaceX);
        Assert.True(ladder.X < WallX, "raised from the wave's side");
        Assert.True(Play(sim, ai, () => ai.LadderHolder != 0 && Past(sim, soldiers) >= 2, 2 * 60 * Tps),
            $"a soldier to hold the top and others to go on over: holder {ai.LadderHolder}, past {Past(sim, soldiers)}");
        int h = sim.Units.IndexOf(ai.LadderHolder);
        Assert.Equal(ladder.Top, sim.Units.Y[h]);
        Assert.Equal(0, sim.Units.Owner[h]);
        Assert.DoesNotContain(ai.LadderHolder, sappers);
        Assert.DoesNotContain(sim.Content.Units[sim.Units.Def[h]].Role, new[] { UnitRole.Worker, UnitRole.Sapper });
    }

    [Fact]
    public void AWaveHeldUpByACliffRaisesALadderUpIt()
    {
        var (sim, ai, soldiers, _) = Setup(AiLevel.Normal, cliff: true);
        Assert.NotNull(sim.Economy.TownHall(1));
        Assert.True(Play(sim, ai, () => ai.Waves > 0, AiPlayer.SmartFirstWaveTicks + 3 * 60 * Tps), "a wave to set out");
        Assert.True(Play(sim, ai, () => ai.WaveLadder(sim) is { Done: true }, 3 * 60 * Tps), "the sappers to raise a ladder up the cliff");
        var ladder = ai.WaveLadder(sim)!;
        Assert.Equal(WallX - 1, ladder.X);
        Assert.Equal(Ground + 6, ladder.Top);
        Assert.True(Play(sim, ai, () => soldiers.Any(id => sim.Units.IndexOf(id) is var i && i >= 0 && sim.Units.Y[i] == Ground + 6),
            2 * 60 * Tps), "soldiers to climb onto the cliff");
    }

    [Fact]
    public void WithNoWallOrCliffInTheWayNoLadderGoesUp()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(128, 16, 128, Ground));
        sim.Map.Starts.Add(new StartArea(0, 24, Ground + 1, 64));
        sim.Map.Starts.Add(new StartArea(1, 104, Ground + 1, 64));
        sim.PlaceStartingBuildings();
        for (int k = 0; k < 8; k++) sim.SpawnUnit(0, "spearman", 40 + k, 60);
        sim.SpawnUnit(0, "sapper", 40, 66);
        var ai = new AiPlayer(0, 1, AiLevel.Normal);
        Assert.True(Play(sim, ai, () => ai.Waves > 0, AiPlayer.SmartFirstWaveTicks + 3 * 60 * Tps), "a wave to set out");
        Play(sim, ai, () => false, 90 * Tps);
        Assert.Empty(sim.Economy.Ladders);
    }

    [Fact]
    public void TheClassicLevelRaisesNoLadders()
    {
        var (sim, ai, _, _) = Setup(AiLevel.Classic, cliff: false);
        Play(sim, ai, () => false, AiPlayer.SmartFirstWaveTicks + 4 * 60 * Tps);
        Assert.Empty(sim.Economy.Ladders);
    }
}
