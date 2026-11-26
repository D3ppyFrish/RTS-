using System;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Computer players growing the woods back: a forester's lodge once the trees by the town are cut down, and foresters for it while wood is short.</summary>
public class AiForesterTests
{
    private const int Ground = 3;
    private const int Tps = Simulation.TicksPerSecond;

    /// <summary>One town in the west of a flat, treeless map.</summary>
    private static (Simulation Sim, Building Hall) Town()
    {
        var w = TestWorlds.Flat(256, 16, 256, Ground);
        var sim = TestWorlds.Sim(w);
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

    /// <summary>Plays to when it may lay out a lodge, then keeps wood at <paramref name="wood"/> (food and stone plenty) while it plays on.</summary>
    private static void PlayOn(Simulation sim, Building hall, AiPlayer ai, int ticks, Func<bool>? done = null, int wood = 100)
    {
        Play(sim, ai, AiPlayer.ForestFromTicks - (int)sim.Tick);
        for (int t = 0; t < ticks && (done == null || !done()); t += Tps)
        {
            hall.Stock[(int)Good.Wood] = wood;
            hall.Stock[(int)Good.Food] = Math.Max(hall.Stock[(int)Good.Food], 400);
            hall.Stock[(int)Good.Stone] = Math.Max(hall.Stock[(int)Good.Stone], 200);
            Play(sim, ai, Tps, done);
        }
    }

    private static Building? Lodge(Simulation sim) => sim.Buildings.All.FirstOrDefault(b => b.Owner == 0 && b.Def.Id == "forester_lodge");

    [Theory]
    [InlineData(AiLevel.Easy)]
    [InlineData(AiLevel.Normal)]
    [InlineData(AiLevel.Hard)]
    public void ASmartComputerPutsUpAForestersLodgeOnceTheWoodsAreCutDownAndPlants(AiLevel level)
    {
        var (sim, hall) = Town();
        var ai = new AiPlayer(0, 1, level);
        Play(sim, ai, AiPlayer.ForestFromTicks - 10 * Tps);
        Assert.Null(Lodge(sim)); // not before its town is going

        PlayOn(sim, hall, ai, 8 * 60 * Tps, () => sim.Economy.Saplings.Count >= 2);
        var lodge = Lodge(sim);
        Assert.NotNull(lodge);
        Assert.True(lodge!.Active);
        var (hx, hz) = (hall.X + hall.Def.SizeX / 2, hall.Z + hall.Def.SizeZ / 2);
        int cx = lodge.X + lodge.Def.SizeX / 2, cz = lodge.Z + lodge.Def.SizeZ / 2;
        long d2 = (long)(cx - hx) * (cx - hx) + (long)(cz - hz) * (cz - hz);
        Assert.True(d2 >= (long)AiPlayer.ForestFrom * AiPlayer.ForestFrom, $"lodge at {lodge.X},{lodge.Z} is too near the town hall");
        Assert.True(sim.Economy.Saplings.Count >= 2, "no saplings planted");
        Assert.All(sim.Economy.Saplings, s => Assert.Equal(lodge.Id, s.Lodge));
        Assert.Equal(lodge.Def.WorkerSlots, ai.Foresters.Count);
        Assert.All(ai.Foresters, id => Assert.Equal((UnitJob.Quarry, lodge.Id), (sim.Units.Job[sim.Units.IndexOf(id)], sim.Units.JobTarget[sim.Units.IndexOf(id)])));
        Assert.Single(sim.Buildings.All, b => b.Owner == 0 && b.Def.Id == "forester_lodge");

        // The saplings grow into trees its wood cutters can fell.
        PlayOn(sim, hall, ai, EconomySystem.SaplingGrowTicks + 20 * Tps, () => sim.Economy.TreesGrown > 0);
        Assert.True(sim.Economy.TreesGrown > 0, "no tree grew");
    }

    [Fact]
    public void ItLetsItsForestersGoOnceWoodIsPlentyAndSendsThemBackWhenItIsShort()
    {
        var (sim, hall) = Town();
        var ai = new AiPlayer(0, 1, AiLevel.Easy);
        PlayOn(sim, hall, ai, 8 * 60 * Tps, () => ai.Foresters.Count == 2);
        Assert.Equal(2, ai.Foresters.Count);
        var foresters = ai.Foresters.ToArray();

        PlayOn(sim, hall, ai, 5 * Tps, wood: AiPlayer.ForestWoodPlenty + 100);
        Assert.Empty(ai.Foresters);
        int lodge = Lodge(sim)!.Id;
        Assert.All(foresters, id => Assert.False(sim.Units.Job[sim.Units.IndexOf(id)] == UnitJob.Quarry && sim.Units.JobTarget[sim.Units.IndexOf(id)] == lodge));

        // Between short and plenty it sends none out again ...
        PlayOn(sim, hall, ai, 5 * Tps, wood: (AiPlayer.ForestWoodPlenty + AiPlayer.ForestWoodShort) / 2);
        Assert.Empty(ai.Foresters);
        // ... and once wood is short it does.
        PlayOn(sim, hall, ai, 30 * Tps, () => ai.Foresters.Count == 2, wood: 50);
        Assert.Equal(2, ai.Foresters.Count);
    }

    [Fact]
    public void NoLodgeWhileTreesStandByTheTownOrWoodPilesUpOrForTheClassicLevel()
    {
        // Woods still standing east of the town when it could lay out a lodge (more than its cutters fell meanwhile).
        var (sim, hall) = Town();
        var ai = new AiPlayer(0, 1, AiLevel.Easy);
        Play(sim, ai, AiPlayer.ForestFromTicks - (int)sim.Tick);
        for (int x = 50; x <= 64; x += 3)
        for (int z = 108; z <= 152; z += 3)
            TestWorlds.Tree(sim.World, x, z, 4, Ground);
        PlayOn(sim, hall, ai, 60 * Tps);
        Assert.True(Lodge(sim) == null, string.Join("\n", ai.Journal.Select(j => j.Tick + " " + j.Text).TakeLast(15)));

        // Wood piling up.
        (sim, hall) = Town();
        ai = new AiPlayer(0, 1, AiLevel.Easy);
        PlayOn(sim, hall, ai, 90 * Tps, wood: 5000);
        Assert.Null(Lodge(sim));

        // The classic level never plants.
        (sim, hall) = Town();
        ai = new AiPlayer(0, 1, AiLevel.Classic);
        PlayOn(sim, hall, ai, 90 * Tps);
        Assert.Null(Lodge(sim));
    }

    [Fact]
    public void ALodgeGoesOnlyWhereSaplingsHaveRoom()
    {
        // The ground round the town is all stone (nothing grows on it) save a patch of grass far to the north-east.
        var (sim, hall) = Town();
        var w = sim.World;
        for (int z = 0; z < w.SizeZ; z++)
        for (int x = 0; x < w.SizeX; x++)
            if (w.Get(x, Ground, z) == BlockType.Grass && !(x >= 58 && x < 90 && z >= 90 && z < 112)) w.Set(x, Ground, z, BlockType.Stone);
        var def = sim.Content.Buildings.First(b => b.Id == "forester_lodge");
        Assert.Equal(0, sim.Economy.PlantingRoom(def, 36, 150, 10));
        Assert.True(sim.Economy.PlantingRoom(def, 70, 98, 10) > 0);

        var ai = new AiPlayer(0, 1, AiLevel.Easy);
        PlayOn(sim, hall, ai, 90 * Tps, () => Lodge(sim) != null);
        var lodge = Lodge(sim);
        Assert.NotNull(lodge);
        Assert.True(sim.Economy.PlantingRoom(lodge!.Def, lodge.X, lodge.Z, AiPlayer.ForestRoomLeast) > 0, $"lodge at {lodge.X},{lodge.Z}");
        Assert.True(lodge.MaxX + lodge.Def.Plants >= 58 && lodge.MaxZ + lodge.Def.Plants >= 90 && lodge.Z - lodge.Def.Plants < 112, $"lodge at {lodge.X},{lodge.Z}");
    }

    [Fact]
    public void AComputerPlantingIsTheSameEveryRun()
    {
        ulong Run()
        {
            var (sim, hall) = Town();
            var ai = new AiPlayer(0, 7, AiLevel.Easy);
            PlayOn(sim, hall, ai, 2 * 60 * Tps);
            Assert.NotNull(Lodge(sim));
            return sim.StateHash();
        }
        Assert.Equal(Run(), Run());
    }
}
