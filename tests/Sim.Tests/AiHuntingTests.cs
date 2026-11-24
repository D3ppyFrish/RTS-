using System;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Computer players hunting deer: a hunter's lodge by a herd, hunters for it, and a storehouse by it.</summary>
public class AiHuntingTests
{
    private const int Ground = 3;
    private const int Tps = Simulation.TicksPerSecond;
    private const int HerdX = 110, HerdZ = 128;

    /// <summary>One town in the west of a flat map, and a herd of deer 70 blocks east of it.</summary>
    private static (Simulation Sim, Building Hall, Lair Herd) Town(bool seen = true)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(256, 16, 256, Ground));
        sim.Map.Starts.Add(new StartArea(0, 40, Ground + 1, 128));
        sim.PlaceStartingBuildings();
        var herd = sim.Monsters.Place(sim.Content.Unit("deer"), HerdX, HerdZ)!;
        if (seen)
        {
            // A look at the herd's ground, as a scout would have.
            int scout = sim.SpawnUnit(0, "worker", HerdX + 2, HerdZ + 2);
            sim.Run(2);
            sim.Units.Remove(scout);
        }
        return (sim, sim.Economy.TownHall(0)!, herd);
    }

    private static void Play(Simulation sim, AiPlayer ai, int ticks, Func<bool>? done = null)
    {
        for (int t = 0; t < ticks && (done == null || !done()); t++)
        {
            ai.Think(sim, c => sim.Schedule(sim.Tick, c));
            sim.Step();
        }
    }

    /// <summary>Plays to when it may lay out a lodge, then keeps food low and wood plenty while it plays on.</summary>
    private static void PlayOn(Simulation sim, Building hall, AiPlayer ai, int ticks, Func<bool>? done = null, int food = 100)
    {
        Play(sim, ai, AiPlayer.LodgeFromTicks - (int)sim.Tick);
        for (int t = 0; t < ticks && (done == null || !done()); t += Tps)
        {
            hall.Stock[(int)Good.Food] = Math.Min(hall.Stock[(int)Good.Food], food);
            hall.Stock[(int)Good.Wood] = Math.Max(hall.Stock[(int)Good.Wood], 400);
            hall.Stock[(int)Good.Stone] = Math.Max(hall.Stock[(int)Good.Stone], 200);
            Play(sim, ai, Tps, done);
        }
    }

    private static Building? Lodge(Simulation sim) => sim.Buildings.All.FirstOrDefault(b => b.Owner == 0 && b.Def.Hunts > 0);

    [Theory]
    [InlineData(AiLevel.Easy)]
    [InlineData(AiLevel.Normal)]
    [InlineData(AiLevel.Hard)]
    public void ASmartComputerPutsUpALodgeByTheDeerAndHuntsThem(AiLevel level)
    {
        var (sim, hall, herd) = Town();
        var ai = new AiPlayer(0, 1, level);
        Play(sim, ai, AiPlayer.LodgeFromTicks - 10 * Tps);
        Assert.Null(Lodge(sim)); // not before its town is going

        PlayOn(sim, hall, ai, 8 * 60 * Tps, () => sim.Economy.GameHunted >= 2);
        var lodge = Lodge(sim);
        Assert.NotNull(lodge);
        // By the herd's den, so the whole herd is in its reach.
        var (lx, lz) = (lodge!.X + lodge.Def.SizeX / 2, lodge.Z + lodge.Def.SizeZ / 2);
        Assert.True(Math.Abs(lx - herd.X) + Math.Abs(lz - herd.Z) <= 2 * AiPlayer.LodgeRadius + 4, $"lodge at {lx},{lz}");
        Assert.True(lodge.Active);
        Assert.Equal(lodge.Def.WorkerSlots, ai.Hunters.Count);
        Assert.All(ai.Hunters, id => Assert.Equal((UnitJob.Quarry, lodge.Id), (sim.Units.Job[sim.Units.IndexOf(id)], sim.Units.JobTarget[sim.Units.IndexOf(id)])));
        Assert.True(sim.Economy.GameHunted >= 2, $"deer brought home: {sim.Economy.GameHunted}");
        // Only the one lodge.
        Assert.Single(sim.Buildings.All, b => b.Owner == 0 && b.Def.Hunts > 0);
    }

    [Fact]
    public void ItPutsAStorehouseByALodgeFarFromStorage()
    {
        var (sim, hall, _) = Town();
        var ai = new AiPlayer(0, 1, AiLevel.Normal);
        PlayOn(sim, hall, ai, 8 * 60 * Tps, () => Lodge(sim) is { Active: true } l && ai.Hunters.Count > 0
            && sim.Buildings.All.Any(b => b.Owner == 0 && b.Def.Kind == BuildingKind.Storehouse && b.ColumnDistance(l.X, l.Z) < AiPlayer.LodgeStoreGap));
        var lodge = Lodge(sim)!;
        Assert.Contains(sim.Buildings.All, b => b.Owner == 0 && b.Def.Kind == BuildingKind.Storehouse && b.ColumnDistance(lodge.X, lodge.Z) < AiPlayer.LodgeStoreGap);
    }

    [Fact]
    public void ItLetsItsHuntersGoWhenThereAreNoDeerToSpare()
    {
        var (sim, hall, herd) = Town();
        var ai = new AiPlayer(0, 1, AiLevel.Easy);
        PlayOn(sim, hall, ai, 8 * 60 * Tps, () => ai.Hunters.Count > 0);
        Assert.NotEmpty(ai.Hunters);
        var hunters = ai.Hunters.ToArray();
        // The herd is shot out.
        foreach (int id in herd.Pack.ToArray()) sim.Units.Remove(id);
        PlayOn(sim, hall, ai, 10 * Tps);
        Assert.Empty(ai.Hunters);
        Assert.All(hunters, id => Assert.NotEqual(UnitJob.Quarry, sim.Units.Job[sim.Units.IndexOf(id)]));
    }

    [Fact]
    public void NoLodgeForAHerdItHasNotSeenOrWithFoodPilingUpOrForTheClassicLevel()
    {
        // A herd it has not seen (until its scouts come by).
        var (sim, hall, herd) = Town(seen: false);
        var ai = new AiPlayer(0, 1, AiLevel.Normal);
        bool early = false;
        PlayOn(sim, hall, ai, 90 * Tps, () =>
        {
            early |= Lodge(sim) != null && sim.Vision.At(0, herd.X, herd.Z) == Sight.Unseen
                && herd.Pack.All(id => sim.Units.IndexOf(id) is var i && sim.Vision.At(0, sim.Units.X[i].FloorToInt(), sim.Units.Z[i].FloorToInt()) == Sight.Unseen);
            return false;
        });
        Assert.False(early);

        // Food piling up.
        (sim, hall, _) = Town();
        ai = new AiPlayer(0, 1, AiLevel.Normal);
        PlayOn(sim, hall, ai, 90 * Tps, food: 5000, done: () => { hall.Stock[(int)Good.Food] = 5000; return false; });
        Assert.Null(Lodge(sim));

        // The classic level never hunts.
        (sim, hall, _) = Town();
        ai = new AiPlayer(0, 1, AiLevel.Classic);
        PlayOn(sim, hall, ai, 90 * Tps);
        Assert.Null(Lodge(sim));
    }

    [Fact]
    public void AComputerHuntingIsTheSameEveryRun()
    {
        ulong Run()
        {
            var (sim, hall, _) = Town();
            var ai = new AiPlayer(0, 7, AiLevel.Normal);
            PlayOn(sim, hall, ai, 90 * Tps);
            return sim.StateHash();
        }
        Assert.Equal(Run(), Run());
    }
}
