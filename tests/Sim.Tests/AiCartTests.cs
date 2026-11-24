using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Automation;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Computer players and carts: with a sawmill and storehouses out by its work, a smart computer player trains two
/// carts and gives its town hall Keep stocked rules, and the carts bring the far storehouses' goods home.
/// </summary>
public class AiCartTests
{
    private const int Ground = 3;
    private const int Tps = Simulation.TicksPerSecond;

    /// <summary>
    /// One town in the west of a flat map with a finished sawmill, houses, and two storehouses out east, each
    /// holding wood and stone; the town hall is kept in plenty of every good but wood and stone.
    /// </summary>
    private static (Simulation Sim, Building Hall, Building[] Stores) Town(bool sawmill = true, int stores = 2)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(256, 16, 256, Ground));
        sim.Map.Starts.Add(new StartArea(0, 60, Ground + 1, 128));
        sim.PlaceStartingBuildings();
        var hall = sim.Economy.TownHall(0)!;
        if (sawmill) Assert.NotNull(sim.Economy.PlaceFinished(0, sim.Content.Building("sawmill"), 40, 140));
        for (int k = 0; k < 4; k++) Assert.NotNull(sim.Economy.PlaceFinished(0, sim.Content.Building("house"), 30 + 6 * k, 100));
        var list = new List<Building>();
        for (int k = 0; k < stores; k++)
        {
            var s = sim.Economy.PlaceFinished(0, sim.Content.Building("storehouse"), 110 + 12 * k, 110 + 20 * k)!;
            Assert.NotNull(s);
            s.Stock[(int)Good.Wood] = 200;
            s.Stock[(int)Good.Stone] = 100;
            list.Add(s);
        }
        for (int k = 0; k < 6; k++) sim.SpawnUnit(0, "worker", 50 + k, 150);
        return (sim, hall, list.ToArray());
    }

    private static void Stock(Building hall)
    {
        foreach (var (good, n) in new[] { (Good.Food, 1500), (Good.Planks, 400) })
            hall.Stock[(int)good] = Math.Max(hall.Stock[(int)good], n);
    }

    /// <summary>Plays on; true if a cart was seen bringing a load to the town hall on supply duty.</summary>
    private static bool Play(Simulation sim, AiPlayer ai, int seconds, Building hall)
    {
        bool hauled = false;
        var u = sim.Units;
        for (int s = 0; s < seconds; s++)
        {
            Stock(hall);
            for (int t = 0; t < Tps; t++)
            {
                ai.Think(sim, c => sim.Schedule(sim.Tick, c));
                sim.Step();
            }
            for (int i = 0; i < u.Count; i++)
                hauled |= u.Owner[i] == 0 && sim.Content.Units[u.Def[i]].IsCart && u.Job[i] == UnitJob.Supply
                    && u.JobTarget[i] == hall.Id && u.CarryAmount[i] > 0;
        }
        return hauled;
    }

    private static int Carts(Simulation sim) =>
        Enumerable.Range(0, sim.Units.Count).Count(i => sim.Units.Owner[i] == 0 && sim.Content.Units[sim.Units.Def[i]].IsCart);

    private static int Ponies(Simulation sim) =>
        Enumerable.Range(0, sim.Units.Count).Count(i => sim.Units.Owner[i] == 0 && sim.Content.Units[sim.Units.Def[i]].Runs);

    [Theory]
    [InlineData(AiLevel.Easy)]
    [InlineData(AiLevel.Hard)]
    public void WithHandcartsAndASawmillASmartComputerTrainsTwoPackPonies(AiLevel level)
    {
        var (sim, hall, _) = Town();
        foreach (var t in new[] { "roads", "carts" }) sim.Economy.Grant(0, sim.Content.Tech(t));
        var ai = new AiPlayer(0, 1, level);
        Play(sim, ai, 90, hall);
        Assert.Equal(AiPlayer.MostPonies, Ponies(sim));
        Play(sim, ai, 30, hall);
        Assert.Equal(AiPlayer.MostPonies, Ponies(sim));
    }

    [Fact]
    public void NoPackPoniesWithoutASawmillOrForTheClassicLevel()
    {
        foreach (var (level, sawmill) in new[] { (AiLevel.Classic, true), (AiLevel.Normal, false) })
        {
            var (sim, hall, _) = Town(sawmill);
            foreach (var t in new[] { "roads", "carts" }) sim.Economy.Grant(0, sim.Content.Tech(t));
            Play(sim, new AiPlayer(0, 1, level), 60, hall);
            Assert.Equal(0, Ponies(sim));
        }
    }

    [Theory]
    [InlineData(AiLevel.Easy)]
    [InlineData(AiLevel.Normal)]
    [InlineData(AiLevel.Hard)]
    public void WithASawmillAndStorehousesASmartComputerTrainsCartsThatBringGoodsHome(AiLevel level)
    {
        var (sim, hall, stores) = Town();
        var ai = new AiPlayer(0, 1, level);
        int far = stores.Sum(s => s.Stock[(int)Good.Wood] + s.Stock[(int)Good.Stone]);
        Assert.True(Play(sim, ai, 3 * 60, hall), "a cart to bring a load home");

        Assert.Equal(AiPlayer.MostCarts, Carts(sim));
        foreach (var g in AiPlayer.HaulGoods) Assert.Equal(AiPlayer.HallKeep, hall.Supply[(int)g]);
        foreach (var s in stores) Assert.False(s.HasSupply);
        // The far storehouses' wood and stone went home (or into the town's work) in cart loads.
        int left = stores.Sum(s => s.Stock[(int)Good.Wood] + s.Stock[(int)Good.Stone]);
        Assert.True(left <= far / 2, $"far storehouses still hold {left} of {far}");
        // Its workers leave the supply duty to the carts.
        Assert.DoesNotContain(WorkKind.Supply, WorkPriorities.Unpack(sim.Economy.Player(0).DefaultPriorities));

        // Never more than two, however rich it is.
        Play(sim, ai, 60, hall);
        Assert.Equal(AiPlayer.MostCarts, Carts(sim));
    }

    [Fact]
    public void NoCartsWithoutASawmillOrEnoughStorehousesOrForTheClassicLevel()
    {
        foreach (var (level, sawmill, stores) in new[] { (AiLevel.Classic, true, 2), (AiLevel.Normal, false, 2), (AiLevel.Normal, true, 1) })
        {
            var (sim, hall, _) = Town(sawmill, stores);
            Play(sim, new AiPlayer(0, 1, level), 90, hall);
            Assert.Equal(0, Carts(sim));
            Assert.False(hall.HasSupply);
        }
    }
}
