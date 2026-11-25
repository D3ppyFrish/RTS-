using System;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Computer players fishing: a fishing hut on the shore near the town hall, and fishers for it while food is short.</summary>
public class AiFishingTests
{
    private const int Ground = 3;
    private const int Tps = Simulation.TicksPerSecond;

    /// <summary>One town in the west of a flat map, with a pond (two blocks deep) starting <paramref name="pondX"/> columns in, or none.</summary>
    private static (Simulation Sim, Building Hall) Town(int pondX = 60, bool fog = false)
    {
        var w = TestWorlds.Flat(256, 16, 256, Ground);
        if (pondX > 0)
            for (int z = 116; z < 140; z++)
            for (int x = pondX; x < pondX + 12; x++)
            for (int y = Ground - 1; y <= Ground; y++)
                w.Set(x, y, z, BlockType.Water);
        var sim = TestWorlds.Sim(w);
        sim.Fog = fog;
        sim.Map.Starts.Add(new StartArea(0, 40, Ground + 1, 128));
        sim.PlaceStartingBuildings();
        return (sim, sim.Economy.TownHall(0)!);
    }

    private static void Play(Simulation sim, AiPlayer ai, int ticks, Func<bool>? done = null)
    {
        for (int t = 0; t < ticks && (done == null || !done()); t++)
        {
            ai.Think(sim, c => sim.Schedule(sim.Tick, c));
            sim.Step();
        }
    }

    /// <summary>Plays to when it may lay out a hut, then keeps food at most <paramref name="food"/> and wood plenty while it plays on.</summary>
    private static void PlayOn(Simulation sim, Building hall, AiPlayer ai, int ticks, Func<bool>? done = null, int food = 100)
    {
        Play(sim, ai, AiPlayer.FishFromTicks - (int)sim.Tick);
        for (int t = 0; t < ticks && (done == null || !done()); t += Tps)
        {
            hall.Stock[(int)Good.Food] = food;
            hall.Stock[(int)Good.Wood] = Math.Max(hall.Stock[(int)Good.Wood], 400);
            hall.Stock[(int)Good.Stone] = Math.Max(hall.Stock[(int)Good.Stone], 200);
            Play(sim, ai, Tps, done);
        }
    }

    private static Building? Hut(Simulation sim) => sim.Buildings.All.FirstOrDefault(b => b.Owner == 0 && b.Def.Id == "fishing_hut");

    [Theory]
    [InlineData(AiLevel.Easy)]
    [InlineData(AiLevel.Normal)]
    [InlineData(AiLevel.Hard)]
    public void ASmartComputerPutsUpAFishingHutOnTheShoreAndFishes(AiLevel level)
    {
        var (sim, hall) = Town();
        var ai = new AiPlayer(0, 1, level);
        Play(sim, ai, AiPlayer.FishFromTicks - 10 * Tps);
        Assert.Null(Hut(sim)); // not before its town is going

        bool caught = false;
        PlayOn(sim, hall, ai, 6 * 60 * Tps, () => caught |= ai.Fishers.Any(id => sim.Units.IndexOf(id) is var i && sim.Units.Carry[i] == Good.Food && sim.Units.CarryAmount[i] > 0));
        var hut = Hut(sim);
        Assert.NotNull(hut);
        Assert.True(hut!.Active);
        // At the pond's edge (the pond is columns 60..71 by 116..139).
        Assert.True(hut.X - 1 <= 71 && hut.MaxX + 1 >= 60 && hut.Z - 1 <= 139 && hut.MaxZ + 1 >= 116, $"hut at {hut.X},{hut.Z}");
        Assert.Equal(hut.Def.WorkerSlots, ai.Fishers.Count);
        Assert.All(ai.Fishers, id => Assert.Equal((UnitJob.Quarry, hut.Id), (sim.Units.Job[sim.Units.IndexOf(id)], sim.Units.JobTarget[sim.Units.IndexOf(id)])));
        Assert.True(caught, "no fish caught");
        // Only the one hut.
        Assert.Single(sim.Buildings.All, b => b.Owner == 0 && b.Def.Id == "fishing_hut");
    }

    [Fact]
    public void ItLetsItsFishersGoOnceFoodIsPlentyAndSendsThemBackWhenItIsShort()
    {
        var (sim, hall) = Town();
        var ai = new AiPlayer(0, 1, AiLevel.Normal);
        PlayOn(sim, hall, ai, 6 * 60 * Tps, () => ai.Fishers.Count == 2);
        Assert.Equal(2, ai.Fishers.Count);
        var fishers = ai.Fishers.ToArray();

        PlayOn(sim, hall, ai, 5 * Tps, food: AiPlayer.FishFoodPlenty + 200);
        Assert.Empty(ai.Fishers);
        int hut = Hut(sim)!.Id;
        Assert.All(fishers, id => Assert.False(sim.Units.Job[sim.Units.IndexOf(id)] == UnitJob.Quarry && sim.Units.JobTarget[sim.Units.IndexOf(id)] == hut));

        // Between plenty and short it sends none out again ...
        PlayOn(sim, hall, ai, 5 * Tps, food: (AiPlayer.FishFoodPlenty + AiPlayer.FishFoodShort) / 2);
        Assert.Empty(ai.Fishers);
        // ... and once food is short it does.
        PlayOn(sim, hall, ai, 30 * Tps, () => ai.Fishers.Count == 2, food: 50);
        Assert.Equal(2, ai.Fishers.Count);
    }

    [Fact]
    public void NoHutWithNoWaterNearOrWithFoodPilingUpOrForTheClassicLevel()
    {
        // The only water is a pond far beyond its reach.
        var (sim, hall) = Town(pondX: 40 + AiPlayer.FishRadius + 20);
        var ai = new AiPlayer(0, 1, AiLevel.Normal);
        PlayOn(sim, hall, ai, 90 * Tps);
        Assert.Null(Hut(sim));

        // Food piling up.
        (sim, hall) = Town();
        ai = new AiPlayer(0, 1, AiLevel.Normal);
        PlayOn(sim, hall, ai, 90 * Tps, food: 5000);
        Assert.Null(Hut(sim));

        // The classic level never fishes.
        (sim, hall) = Town();
        ai = new AiPlayer(0, 1, AiLevel.Classic);
        PlayOn(sim, hall, ai, 90 * Tps);
        Assert.Null(Hut(sim));
    }

    [Fact]
    public void AComputerFishingIsTheSameEveryRun()
    {
        ulong Run()
        {
            var (sim, hall) = Town();
            var ai = new AiPlayer(0, 7, AiLevel.Normal);
            PlayOn(sim, hall, ai, 90 * Tps);
            Assert.NotNull(Hut(sim));
            return sim.StateHash();
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void ItFishesOnlyFromShoreItHasSeenAndPutsAStorehouseByAFarHut()
    {
        // A pond 50 columns east of its town, out of sight of it.
        var (sim, hall) = Town(pondX: 90, fog: true);
        var ai = new AiPlayer(0, 1, AiLevel.Normal);
        PlayOn(sim, hall, ai, 30 * Tps);
        Assert.Equal(Sight.Unseen, sim.Vision.At(0, 89, 128));
        Assert.Null(Hut(sim));

        // A look at the pond, as a scout would have.
        int scout = sim.SpawnUnit(0, "worker", 86, 128);
        Play(sim, ai, 2);
        sim.Units.Remove(scout);
        PlayOn(sim, hall, ai, 8 * 60 * Tps, () => Hut(sim) is { Active: true } h && ai.Fishers.Count > 0
            && sim.Buildings.All.Any(b => b.Owner == 0 && b.Def.Kind == BuildingKind.Storehouse && b.ColumnDistance(h.X, h.Z) < AiPlayer.LodgeStoreGap));
        var hut = Hut(sim);
        Assert.NotNull(hut);
        Assert.True(hut!.MaxX + 1 >= 90, $"hut at {hut.X},{hut.Z}");
        Assert.Contains(sim.Buildings.All, b => b.Owner == 0 && b.Def.Kind == BuildingKind.Storehouse && b.ColumnDistance(hut.X, hut.Z) < AiPlayer.LodgeStoreGap);
    }
}
