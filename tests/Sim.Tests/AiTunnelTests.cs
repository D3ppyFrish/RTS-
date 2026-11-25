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
/// Computer players and the hive's trick: a Deepfolk wave held up by an enemy wall has a burrower tunnel under it,
/// deep enough that the wall stands, and its swarm goes through and comes up on the far side.
/// </summary>
public class AiTunnelTests
{
    private const int Ground = 8;
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
    /// Player 0 (a computer player of the level, of <paramref name="faction"/>) in the west with 8 soldiers and 2
    /// sappers of its faction, against player 1 in the east, who gives no orders, behind a stone wall right across
    /// the map at x = 80, with no way round it.
    /// </summary>
    private static (Simulation Sim, AiPlayer Ai, List<int> Soldiers, int[] Sappers) Setup(AiLevel level, string faction)
    {
        const int size = 128;
        var w = TestWorlds.Flat(size, 32, size, Ground);
        var sim = TestWorlds.Sim(w);
        sim.SetFaction(0, faction);
        sim.Map.Starts.Add(new StartArea(0, 24, Ground + 1, 64));
        sim.Map.Starts.Add(new StartArea(1, 104, Ground + 1, 64));
        sim.PlaceStartingBuildings();
        for (int z = 1; z < size - 1; z++) Assert.NotNull(sim.Economy.PlaceFinished(1, sim.Content.Building("stone_wall"), WallX, z));
        foreach (int z in new[] { 0, size - 1 }) TestWorlds.Box(w, WallX, 1, z, WallX, 20, z, BlockType.Bedrock);
        bool deep = faction == "deepfolk";
        var soldiers = Enumerable.Range(0, 8).Select(k => sim.SpawnUnit(0, deep ? "skitterling" : "spearman", 40 + k, 60)).ToList();
        string sapper = deep ? "burrower" : "sapper";
        var sappers = new[] { sim.SpawnUnit(0, sapper, 40, 66), sim.SpawnUnit(0, sapper, 42, 66) };
        Assert.All(soldiers.Concat(sappers), id => Assert.True(id > 0));
        return (sim, new AiPlayer(0, 1, level), soldiers, sappers);
    }

    private static int Past(Simulation sim, IEnumerable<int> ids) =>
        ids.Count(id => sim.Units.IndexOf(id) is var i && i >= 0 && sim.Units.X[i].FloorToInt() > WallX);

    /// <summary>Built blocks in the wall's line, above the ground.</summary>
    private static int WallBlocks(Simulation sim)
    {
        int n = 0;
        for (int z = 1; z < sim.World.SizeZ - 1; z++)
        for (int y = Ground + 1; y < sim.World.SizeY; y++)
            if (BlockInfo.IsBuilt(sim.World.Get(WallX, y, z))) n++;
        return n;
    }

    [Theory]
    [InlineData(AiLevel.Easy)]
    [InlineData(AiLevel.Normal)]
    [InlineData(AiLevel.Hard)]
    public void ADeepfolkWaveTunnelsUnderAWallAndComesUpBeyondIt(AiLevel level)
    {
        var (sim, ai, soldiers, burrowers) = Setup(level, "deepfolk");
        int walls = WallBlocks(sim);
        Assert.True(Play(sim, ai, () => ai.Waves > 0, AiPlayer.SmartFirstWaveTicks + 3 * 60 * Tps), "a wave to set out");
        Assert.True(Play(sim, ai, () => ai.TunnelDigger != 0, 2 * 60 * Tps), "a burrower to set out to dig");
        Assert.Contains(ai.TunnelDigger, burrowers);
        Assert.True(Play(sim, ai, () => ai.TunnelExit != null, 3 * 60 * Tps), "the tunnel to come up past the wall");
        var (ex, ey, ez) = ai.TunnelExit!.Value;
        Assert.True(ex > WallX, "it comes up on the far side");
        Assert.Equal(Ground + 1, ey);
        // Deep enough under the wall's foot that the wall kept its footing: none of it came down.
        Assert.Equal(walls, WallBlocks(sim));
        Assert.True(Play(sim, ai, () => Past(sim, soldiers) >= 4, 2 * 60 * Tps), $"the swarm to go through: past {Past(sim, soldiers)}");
        Assert.Equal(walls, WallBlocks(sim));
        // Underground, so nobody raised a ladder.
        Assert.Empty(sim.Economy.Ladders);
    }

    [Fact]
    public void SappersOfOtherFactionsRaiseLaddersRatherThanTunnel()
    {
        var (sim, ai, _, _) = Setup(AiLevel.Normal, "settlers");
        Assert.True(Play(sim, ai, () => ai.Waves > 0, AiPlayer.SmartFirstWaveTicks + 3 * 60 * Tps), "a wave to set out");
        Assert.True(Play(sim, ai, () => ai.WaveLadder(sim) != null, 3 * 60 * Tps), "a ladder");
        Assert.Equal(0, ai.TunnelsDug);
    }

    [Fact]
    public void TheClassicLevelDigsNoTunnels()
    {
        var (sim, ai, _, _) = Setup(AiLevel.Classic, "deepfolk");
        Play(sim, ai, () => false, AiPlayer.SmartFirstWaveTicks + 4 * 60 * Tps);
        Assert.Equal(0, ai.TunnelsDug);
    }
}
