using System;
using System.Linq;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>The fishing hut: a quarry for food that must stand at the water's edge.</summary>
public class FishingTests
{
    private const int Tps = Simulation.TicksPerSecond;
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4

    // A flat world with a pond, columns 30..39 by 20..29, two blocks deep.
    private static Simulation WithPond()
    {
        var w = TestWorlds.Flat(64, 16, 64, Ground);
        for (int z = 20; z < 30; z++)
        for (int x = 30; x < 40; x++)
        for (int y = Ground - 1; y <= Ground; y++)
            w.Set(x, y, z, BlockType.Water);
        return TestWorlds.Sim(w);
    }

    private static int[] Workers(Simulation sim, int n) => Enumerable.Range(0, n).Select(k => sim.SpawnUnit(0, "worker", 20 + k, 12)).ToArray();

    [Fact]
    public void TheDataHasAFishingHutForFood()
    {
        var hut = ContentDb.Default.Building("fishing_hut");
        Assert.Equal(BuildingKind.Quarry, hut.Kind);
        Assert.True(hut.Shore);
        Assert.Equal(new[] { new GoodAmount(Good.Food, 4) }, hut.Works);
        Assert.Empty(hut.Requires);
        Assert.False(ContentDb.Default.Building("quarry").Shore);
    }

    [Fact]
    public void AFishingHutStandsOnlyAtTheWatersEdge()
    {
        var sim = WithPond();
        var hut = sim.Content.Building("fishing_hut");
        // Right beside the pond (its east side on the column next to the water), and touching it only at a corner.
        Assert.Equal(PlaceResult.Ok, sim.Economy.CanPlace(0, hut, 26, 22, out _, checkPlayer: false));
        Assert.Equal(PlaceResult.Ok, sim.Economy.CanPlace(0, hut, 26, 16, out _, checkPlayer: false));
        // A column of dry ground between it and the water is too far; so is open grass.
        Assert.Equal(PlaceResult.NoWater, sim.Economy.CanPlace(0, hut, 25, 22, out _, checkPlayer: false));
        Assert.Equal(PlaceResult.NoWater, sim.Economy.CanPlace(0, hut, 5, 45, out _, checkPlayer: false));
        // Out on the water it does not fit at all, as any building: there is no ground there.
        Assert.Equal(PlaceResult.Uneven, sim.Economy.CanPlace(0, hut, 32, 22, out _, checkPlayer: false));
        // Other buildings do not care about water.
        Assert.Equal(PlaceResult.Ok, sim.Economy.CanPlace(0, sim.Content.Building("quarry"), 5, 45, out _, checkPlayer: false));
    }

    [Fact]
    public void WaterUnderTheGroundOrFarDownACliffDoesNotCount()
    {
        var w = TestWorlds.Flat(64, 16, 64, Ground);
        // Water under a layer of grass beside the hut: not open water.
        for (int z = 20; z < 30; z++) w.Set(29, Ground - 1, z, BlockType.Water);
        var sim = TestWorlds.Sim(w);
        var hut = sim.Content.Building("fishing_hut");
        Assert.Equal(PlaceResult.NoWater, sim.Economy.CanPlace(0, hut, 25, 22, out _, checkPlayer: false));
        Assert.True(EconomySystem.BesideWater(sim.World, 25, 22, 28, 25, Ground + 1) == false);
        // The same water a little lower than the ground beside it, open to the sky: it counts.
        for (int z = 20; z < 30; z++) sim.World.Set(29, Ground, z, BlockType.Air);
        Assert.Equal(PlaceResult.Ok, sim.Economy.CanPlace(0, hut, 25, 22, out _, checkPlayer: false));
        // Too far down: water more than ShoreDrop below where it stands does not.
        Assert.False(EconomySystem.BesideWater(sim.World, 25, 22, 28, 25, Ground + 2 + EconomySystem.ShoreDrop));
    }

    [Fact]
    public void WorkersBuildItBesideThePondAndBringFish()
    {
        var sim = WithPond();
        var hall = sim.Economy.PlaceFinished(0, sim.Content.Building("town_hall"), 10, 10)!;
        hall.Stock[(int)Good.Wood] = 200;
        var w = Workers(sim, 2);
        // Away from water the order does nothing.
        sim.Schedule(0, new BuildCommand(0, w, "fishing_hut", 5, 45));
        sim.Run(2);
        Assert.DoesNotContain(sim.Buildings.All, b => b.Def.Id == "fishing_hut");

        sim.Schedule(sim.Tick, new BuildCommand(0, w, "fishing_hut", 26, 22));
        sim.Run(2);
        var hut = Assert.Single(sim.Buildings.All, b => b.Def.Id == "fishing_hut");
        for (int t = 0; t < 120 * Tps && !hut.Active; t++) sim.Step();
        Assert.True(hut.Active, "the workers built the fishing hut");

        int food = hall.Stock[(int)Good.Food];
        sim.Schedule(sim.Tick, new WorkCommand(0, w, hut.Id));
        sim.Run(2);
        Assert.Equal(2, Enumerable.Range(0, sim.Units.Count).Count(i => sim.Units.Job[i] == UnitJob.Quarry && sim.Units.JobTarget[i] == hut.Id));
        sim.Run(2 * 60 * Tps);
        // Two fishers, 4 food each round of 15 seconds plus the walk to the hall, less what the two of them eat.
        int got = hall.Stock[(int)Good.Food] - food;
        Assert.True(got >= 16, $"food: {got}");
        Assert.True(hall.Stock[(int)Good.Wood] < 200, "the hut was paid for");
    }

    [Fact]
    public void AutomaticWorkersFishWhenFoodIsNeeded()
    {
        var sim = WithPond();
        var store = sim.Economy.PlaceFinished(0, sim.Content.Building("storehouse"), 10, 20)!;
        foreach (var g in new[] { Good.Wood, Good.Stone, Good.IronOre, Good.Coal }) store.Stock[(int)g] = 600;
        var hut = sim.Economy.PlaceFinished(0, sim.Content.Building("fishing_hut"), 26, 22)!;
        var w = Workers(sim, 2);
        sim.Schedule(0, new AutomateCommand(0, w, true));
        sim.Run(2 * Tps);
        Assert.Equal(2, Enumerable.Range(0, sim.Units.Count).Count(i => sim.Units.Job[i] == UnitJob.Quarry && sim.Units.JobTarget[i] == hut.Id));
    }

    [Fact]
    public void OnlyAPlainBuildingCanNeedTheShore()
    {
        var files = ContentLoader.Files.ToDictionary(f => f, f => ContentLoader.EmbeddedText(f)!);
        const string old = "{ \"id\": \"barrier\",";
        int at = files["buildings.json"].IndexOf(old, StringComparison.Ordinal);
        Assert.True(at >= 0);
        files["buildings.json"] = files["buildings.json"][..at] + "{ \"id\": \"barrier\", \"shore\": true," + files["buildings.json"][(at + old.Length)..];
        var e = Assert.Throws<ContentException>(() => ContentLoader.Load(files));
        Assert.Contains(e.Errors, m => m.StartsWith("buildings.json: building 'barrier': a wall cannot need the water's edge"));
    }
}
