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
/// Computer players and roads: with the Roads tech and stone to spare, a smart computer player lays a road from its town
/// hall to each storehouse out by its work, and none without the tech, without the stone, or at the classic level. From
/// Normal up it also paves the way toward the front and out to its work sites, and lays roads sooner while the ground is wet.
/// </summary>
public class AiRoadTests
{
    private const int Ground = 3;
    private const int Tps = Simulation.TicksPerSecond;

    /// <summary>One town in the west of a flat map with two storehouses out east; Roads granted unless told not to.</summary>
    private static (Simulation Sim, Building Hall, Building[] Stores) Town(bool roads = true, bool enemy = false)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(256, 16, 256, Ground));
        sim.Map.Starts.Add(new StartArea(0, 60, Ground + 1, 128));
        if (enemy) sim.Map.Starts.Add(new StartArea(1, 220, Ground + 1, 128));
        sim.PlaceStartingBuildings();
        var hall = sim.Economy.TownHall(0)!;
        for (int k = 0; k < 4; k++) Assert.NotNull(sim.Economy.PlaceFinished(0, sim.Content.Building("house"), 30 + 6 * k, 100));
        var stores = new[]
        {
            sim.Economy.PlaceFinished(0, sim.Content.Building("storehouse"), 100, 110)!,
            sim.Economy.PlaceFinished(0, sim.Content.Building("storehouse"), 70, 160)!,
        };
        foreach (var s in stores) Assert.NotNull(s);
        if (roads) sim.Economy.Grant(0, sim.Content.Tech("roads"));
        for (int k = 0; k < 6; k++) sim.SpawnUnit(0, "worker", 50 + k, 150);
        return (sim, hall, stores);
    }

    private static void Play(Simulation sim, AiPlayer ai, int seconds, Building hall, int stone)
    {
        for (int s = 0; s < seconds; s++)
        {
            hall.Stock[(int)Good.Food] = Math.Max(hall.Stock[(int)Good.Food], 1500);
            hall.Stock[(int)Good.Wood] = Math.Max(hall.Stock[(int)Good.Wood], 1500);
            hall.Stock[(int)Good.Stone] = Math.Max(hall.Stock[(int)Good.Stone], stone);
            for (int t = 0; t < Tps; t++)
            {
                ai.Think(sim, c => sim.Schedule(sim.Tick, c));
                sim.Step();
            }
        }
    }

    /// <summary>Road blocks along the line between the hall and the storehouse.</summary>
    private static int RoadTo(Simulation sim, Building hall, Building store)
    {
        var (x1, z1, x2, z2) = AiPlayer.RoadEnds(hall, store);
        return EconomySystem.RoadLine(x1, z1, x2, z2).Count(p => sim.World.Get(p.X, Walk.GroundLevel(sim.World, p.X, p.Z) - 1, p.Z) == BlockType.Road);
    }

    [Theory]
    [InlineData(AiLevel.Easy)]
    [InlineData(AiLevel.Normal)]
    [InlineData(AiLevel.Hard)]
    public void WithRoadsASmartComputerLaysARoadFromItsHallToEachStorehouse(AiLevel level)
    {
        var (sim, hall, stores) = Town();
        var ai = new AiPlayer(0, 1, level);
        Play(sim, ai, 60, hall, 800);
        foreach (var s in stores)
        {
            var (x1, z1, x2, z2) = AiPlayer.RoadEnds(hall, s);
            int length = Math.Max(Math.Abs(x2 - x1), Math.Abs(z2 - z1));
            Assert.True(RoadTo(sim, hall, s) >= length * 3 / 4, $"road to the storehouse at {s.X},{s.Z}: {RoadTo(sim, hall, s)} of {length}");
            Assert.Contains(s.Id, ai.Roaded);
        }
        // A unit on it walks twice as fast: the road ends just outside the hall and the storehouse.
        var (ex, ez, _, _) = AiPlayer.RoadEnds(hall, stores[0]);
        Assert.False(hall.Covers(ex, ez));
        Assert.Equal(BlockType.Road, sim.World.Get(ex, Walk.GroundLevel(sim.World, ex, ez) - 1, ez));
    }

    [Fact]
    public void NoRoadsWithoutTheTechOrStoneToSpareOrForTheClassicLevel()
    {
        foreach (var (level, roads, stone) in new[] { (AiLevel.Classic, true, 800), (AiLevel.Normal, false, 800), (AiLevel.Normal, true, 20) })
        {
            var (sim, hall, stores) = Town(roads);
            Play(sim, new AiPlayer(0, 1, level), 60, hall, stone);
            foreach (var s in stores) Assert.Equal(0, RoadTo(sim, hall, s));
        }
    }

    [Fact]
    public void ItLaysNoRoadThroughItsOwnWall()
    {
        var (sim, hall, stores) = Town();
        // A wall across the way to the first storehouse.
        var (x1, z1, x2, z2) = AiPlayer.RoadEnds(hall, stores[0]);
        var mid = EconomySystem.RoadLine(x1, z1, x2, z2).ElementAt(Math.Max(Math.Abs(x2 - x1), Math.Abs(z2 - z1)) / 2);
        var wall = sim.Content.Buildings.First(b => b.Kind == BuildingKind.Wall && b.UpgradeOf == null);
        for (int dz = -4; dz <= 4; dz++) sim.Economy.PlaceFinished(0, wall, mid.X, mid.Z + dz);
        Assert.Equal(BuildingKind.Wall, sim.Buildings.AtColumn(mid.X, mid.Z)!.Def.Kind);
        var ai = new AiPlayer(0, 1, AiLevel.Normal);
        Play(sim, ai, 60, hall, 800);
        Assert.Equal(0, RoadTo(sim, hall, stores[0]));
        Assert.True(RoadTo(sim, hall, stores[1]) > 0);
    }

    /// <summary>Road blocks along a line.</summary>
    private static int RoadOn(Simulation sim, (int X1, int Z1, int X2, int Z2) e) =>
        EconomySystem.RoadLine(e.X1, e.Z1, e.X2, e.Z2).Count(p => sim.World.Get(p.X, Walk.GroundLevel(sim.World, p.X, p.Z) - 1, p.Z) == BlockType.Road);

    [Theory]
    [InlineData(AiLevel.Normal, true)]
    [InlineData(AiLevel.Hard, true)]
    [InlineData(AiLevel.Easy, false)]
    public void FromNormalUpItPavesTheWayTowardTheEnemyItKnows(AiLevel level, bool paves)
    {
        var (sim, hall, _) = Town(enemy: true);
        var enemy = sim.Economy.TownHall(1)!;
        sim.SpawnUnit(0, "worker", 216, 136); // so it has seen the enemy's town hall
        var ai = new AiPlayer(0, 1, level);
        Play(sim, ai, 90, hall, 800);
        var ends = AiPlayer.FrontRoadEnds(sim, hall, enemy.X + enemy.Def.SizeX / 2, enemy.Z + enemy.Def.SizeZ / 2);
        Assert.True(ends.X2 > ends.X1 + 30, $"the road heads east toward the enemy: {ends}");
        int laid = RoadOn(sim, ends);
        if (paves)
        {
            Assert.True(laid >= (ends.X2 - ends.X1) * 3 / 4, $"road toward the front: {laid} blocks");
            Assert.True(ai.FrontRoaded);
        }
        else Assert.Equal(0, laid);
    }

    [Theory]
    [InlineData(AiLevel.Normal, true)]
    [InlineData(AiLevel.Easy, false)]
    public void FromNormalUpItLaysARoadFromItsNearestStorageToEachWorkSite(AiLevel level, bool paves)
    {
        var (sim, hall, stores) = Town();
        var lodge = sim.Economy.PlaceFinished(0, sim.Content.Building("hunting_lodge"), 120, 150)!;
        Assert.NotNull(lodge);
        var ai = new AiPlayer(0, 1, level);
        Play(sim, ai, 90, hall, 800);
        // The storehouse at 100,110 is nearer the lodge than the hall or the other storehouse.
        int laid = RoadTo(sim, stores[0], lodge);
        if (paves)
        {
            Assert.True(laid >= 6, $"road from the storehouse to the hunter's lodge: {laid} blocks");
            Assert.Contains(lodge.Id, ai.Roaded);
        }
        else Assert.Equal(0, laid);
    }

    [Fact]
    public void InWetWeatherItLaysRoadsWithLessStoneInHand()
    {
        int Laid(bool wet)
        {
            var (sim, hall, stores) = Town();
            sim.DayNight = true;
            sim.Conditions.SetWeather(wet ? Weather.Rain : Weather.Clear, 2L * Conditions.DayTicks, wet ? Weather.Rain : Weather.Clear);
            if (wet) sim.Conditions.SetWetness(80);
            Play(sim, new AiPlayer(0, 1, AiLevel.Normal), 30, hall, 110);
            return stores.Sum(s => RoadTo(sim, hall, s));
        }
        Assert.Equal(0, Laid(false));
        Assert.True(Laid(true) > 0);
    }

    [Fact]
    public void AComputerLayingRoadsIsTheSameEveryRun()
    {
        ulong Run()
        {
            var (sim, hall, _) = Town();
            Play(sim, new AiPlayer(0, 1, AiLevel.Normal), 45, hall, 800);
            return sim.StateHash();
        }
        Assert.Equal(Run(), Run());
    }
}
