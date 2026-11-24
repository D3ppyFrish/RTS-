using System;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Economy;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Computer players and listening posts: a smart computer player with a barracks keeps one listening post on the
/// enemy's side of its town, and more towards diggers once it has seen sappers or heard enemies underground.
/// </summary>
public class AiListeningTests
{
    private const int Ground = 10; // deep rock, so a tunnel can run under the grass
    private const int Tunnel = 4;
    private const int Tps = Simulation.TicksPerSecond;

    /// <summary>One town in the west of a flat map, with houses, workers and (if asked) a finished barracks.</summary>
    private static (Simulation Sim, Building Hall) Town(bool barracks = true, bool fog = false)
    {
        var w = TestWorlds.Flat(192, 24, 192, Ground);
        TestWorlds.Box(w, 70, Tunnel, 96, 130, Tunnel + 1, 96, BlockType.Air); // a tunnel running east from the town
        var sim = TestWorlds.Sim(w);
        sim.Fog = fog;
        sim.Map.Starts.Add(new StartArea(0, 50, Ground + 1, 96));
        sim.PlaceStartingBuildings();
        var hall = sim.Economy.TownHall(0)!;
        for (int k = 0; k < 4; k++) Assert.NotNull(sim.Economy.PlaceFinished(0, sim.Content.Building("house"), 20 + 6 * k, 70));
        if (barracks) Assert.NotNull(sim.Economy.PlaceFinished(0, sim.Content.Building("barracks"), 30, 120));
        for (int k = 0; k < 6; k++) sim.SpawnUnit(0, "worker", 40 + k, 110);
        return (sim, hall);
    }

    private static void Play(Simulation sim, AiPlayer ai, int seconds, Building hall)
    {
        for (int s = 0; s < seconds; s++)
        {
            foreach (var (good, n) in new[] { (Good.Food, 1500), (Good.Wood, 800), (Good.Stone, 600) })
                hall.Stock[(int)good] = Math.Max(hall.Stock[(int)good], n);
            for (int t = 0; t < Tps; t++)
            {
                ai.Think(sim, c => sim.Schedule(sim.Tick, c));
                sim.Step();
            }
        }
    }

    private static Building[] Posts(Simulation sim) =>
        sim.Buildings.All.Where(b => b.Owner == 0 && b.Def.Kind == BuildingKind.ListeningPost).ToArray();

    [Theory]
    [InlineData(AiLevel.Easy)]
    [InlineData(AiLevel.Normal)]
    [InlineData(AiLevel.Hard)]
    public void WithABarracksASmartComputerKeepsOneListeningPost(AiLevel level)
    {
        var (sim, hall) = Town();
        var ai = new AiPlayer(0, 1, level);
        Play(sim, ai, 90, hall);
        var posts = Posts(sim);
        Assert.Single(posts);
        Assert.True(posts[0].Active, "the post is built");
        var (hx, hz) = (hall.X + hall.Def.SizeX / 2, hall.Z + hall.Def.SizeZ / 2);
        long d2 = (long)(posts[0].X - hx) * (posts[0].X - hx) + (long)(posts[0].Z - hz) * (posts[0].Z - hz);
        Assert.True(d2 <= (long)(AiPlayer.PostReach + AiPlayer.PostRoom + 4) * (AiPlayer.PostReach + AiPlayer.PostRoom + 4),
            $"post at {posts[0].X},{posts[0].Z} is by the town hall at {hx},{hz}");

        // No more without diggers, however rich it is.
        Play(sim, ai, 60, hall);
        Assert.Single(Posts(sim));
    }

    [Fact]
    public void NoListeningPostWithoutABarracksOrForTheClassicLevel()
    {
        foreach (var (level, barracks) in new[] { (AiLevel.Classic, true), (AiLevel.Normal, false) })
        {
            var (sim, hall) = Town(barracks);
            Play(sim, new AiPlayer(0, 1, level), 60, hall);
            Assert.Empty(Posts(sim));
        }
    }

    [Theory]
    [InlineData(AiLevel.Normal)]
    [InlineData(AiLevel.Hard)]
    public void SeeingEnemySappersItPutsAListeningPostTowardsThem(AiLevel level)
    {
        var (sim, hall) = Town();
        var ai = new AiPlayer(0, 1, level);
        Play(sim, ai, 60, hall);
        var first = Assert.Single(Posts(sim));

        // Enemy sappers come out of the north, in sight of its town.
        int[] sappers = { sim.SpawnUnit(1, "sapper", 50, 150), sim.SpawnUnit(1, "sapper", 52, 150) };
        sim.Run(VisionSystem.UpdateTicks);
        Play(sim, ai, 2, hall);
        Assert.True(ai.Diggers(sim));
        foreach (int id in sappers) sim.Units.Remove(id); // gone off again

        // Out where the sappers were, far from storage: its builders carry the post's goods out (#149).
        Play(sim, ai, 150, hall);
        var posts = Posts(sim);
        Assert.Equal(2, posts.Length); // the second hears where they were; that spot needs no third
        var second = posts.Single(p => p.Id != first.Id);
        Assert.True(second.Active);
        var (hx, hz) = (hall.X + hall.Def.SizeX / 2, hall.Z + hall.Def.SizeZ / 2);
        Assert.True(second.Z > hz + AiPlayer.PostReach, $"the second post at {second.X},{second.Z} is on the sappers' side of the hall at {hx},{hz}");
    }

    [Fact]
    public void ItHearsEnemiesDiggingUnderItsTownAndThreatensNoMoreOnceItForgets()
    {
        var (sim, hall) = Town(fog: true);
        var ai = new AiPlayer(0, 1, AiLevel.Normal);
        Play(sim, ai, 60, hall);
        Assert.False(ai.Diggers(sim));
        var post = Assert.Single(Posts(sim));

        // An enemy worker digging in the tunnel east of the town, out of its post's earshot: not seen.
        int digger = sim.SpawnUnit(1, "worker", 120, 96, Tunnel);
        Play(sim, ai, 2, hall);
        Assert.False(sim.Vision.Hears(0, 120, 96));
        Assert.False(ai.Diggers(sim));
        // A unit of its own standing right over the tunnel hears it.
        sim.SpawnUnit(0, "spearman", 120, 97);
        Play(sim, ai, 2, hall);
        Assert.True(ai.Diggers(sim));
        sim.Units.Remove(digger);
        Play(sim, ai, (int)(AiPlayer.SpotMemoryTicks / Tps) + 5, hall);
        Assert.False(ai.Diggers(sim));
        Assert.Contains(Posts(sim), p => p.Id == post.Id);
    }
}
