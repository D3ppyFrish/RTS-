using System;
using System.Linq;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Cut stone: stone dressed at a mason's yard, carried to storage, and spent on stone towers and great works.</summary>
public class MasonTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Tps = Simulation.TicksPerSecond;
    private const int P = 0;

    private static Simulation Flat() => TestWorlds.Sim(TestWorlds.Flat(64, 16, 64, Ground));

    private static Building Place(Simulation sim, int owner, string def, int x, int z) =>
        sim.Economy.PlaceFinished(owner, sim.Content.Building(def), x, z) ?? throw new InvalidOperationException($"{def} does not fit");

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

    private static int RunUntil(Simulation sim, Func<bool> done, int maxTicks, string what)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return t;
            sim.Step();
        }
        Assert.Fail($"Not done after {maxTicks} ticks: {what}");
        return maxTicks;
    }

    [Fact]
    public void CutStoneIsAGoodAndTheMasonsYardIsAWorkshopInTheData()
    {
        Assert.Contains(Good.CutStone, Goods.All);
        Assert.Equal(Goods.All.Length, Goods.Count);
        Assert.Equal("Cut stone", Goods.Name(Good.CutStone));

        var sim = Flat();
        var def = sim.Content.Building("mason_yard");
        Assert.Equal(BuildingKind.Workshop, def.Kind);
        Assert.Equal((Good.Stone, Good.CutStone), (def.Takes, def.Makes));
        Assert.Equal(Good.CutStone, EconomySystem.Makes(def));
        Assert.True(def.MakeTicks > 0);
        Assert.True(def.WorkerSlots >= 1);
        Assert.Equal(new[] { "masonry" }, def.Requires.Select(t => t.Id));
        // Enough kept in storage for a great work's cut stone, so a mason can stock up for one.
        int greatWork = sim.Content.Building("hall_of_legends").Cost.Single(c => c.Good == Good.CutStone).Amount;
        Assert.True(def.Keep >= greatWork);

        // Stone towers and both great works are built of cut stone; stone walls and gates still of plain stone.
        foreach (var id in new[] { "stone_tower", "hall_of_legends", "beast_throne" })
            Assert.Contains(sim.Content.Building(id).Cost, c => c.Good == Good.CutStone);
        foreach (var id in new[] { "stone_wall", "stone_facing", "stone_gate", "stone_gate_facing" })
            Assert.DoesNotContain(sim.Content.Building(id).Cost, c => c.Good == Good.CutStone);
    }

    [Fact]
    public void TheMasonsYardNeedsMasonry()
    {
        var sim = Flat();
        Place(sim, P, "storehouse", 10, 10).Stock[(int)Good.Wood] = 100;
        sim.Buildings.All.First().Stock[(int)Good.Stone] = 100;
        var def = sim.Content.Building("mason_yard");
        Assert.Equal(PlaceResult.Locked, sim.Economy.CanPlace(P, def, 30, 30, out _));
        sim.Economy.Grant(P, sim.Content.Tech("masonry"));
        Assert.Equal(PlaceResult.Ok, sim.Economy.CanPlace(P, def, 30, 30, out _));
    }

    [Fact]
    public void AMasonCutsStoneFromStorageAndCarriesItBack()
    {
        var sim = Flat();
        var store = Place(sim, P, "storehouse", 10, 10);
        store.Stock[(int)Good.Stone] = 12;
        var yard = Place(sim, P, "mason_yard", 24, 10);
        int w = sim.SpawnUnit(P, "worker", 18, 18);
        sim.Schedule(0, new WorkCommand(P, new[] { w }, yard.Id));
        sim.Step();
        Assert.Equal(UnitJob.Haul, sim.Units.Job[Index(sim, w)]);

        RunUntil(sim, () => store.Stock[(int)Good.CutStone] == 12, 8000, "twelve cut stone stored");
        Assert.Equal(0, store.Stock[(int)Good.Stone]);
        Assert.Equal(0, yard.Stock[(int)Good.Stone]);
        Assert.Equal(0, yard.Stock[(int)Good.CutStone]);
        // Only stone goes in: the mason never fetches wood for it.
        Assert.Equal(0, yard.Stock[(int)Good.Wood]);
    }

    [Fact]
    public void CuttingOneStoneTakesTheMakeTime()
    {
        var sim = Flat();
        var yard = Place(sim, P, "mason_yard", 24, 10);
        yard.Stock[(int)Good.Stone] = 2;
        sim.Step();
        Assert.Equal(1, yard.Stock[(int)Good.Stone]);
        sim.Run(yard.Def.MakeTicks - 1);
        Assert.Equal(0, yard.Stock[(int)Good.CutStone]);
        sim.Step();
        Assert.Equal(1, yard.Stock[(int)Good.CutStone]);
        sim.Run(yard.Def.MakeTicks);
        Assert.Equal(2, yard.Stock[(int)Good.CutStone]);
        // Wood left in a mason's yard is not its work.
        yard.Stock[(int)Good.Wood] = 3;
        sim.Run(3 * yard.Def.MakeTicks);
        Assert.Equal(3, yard.Stock[(int)Good.Wood]);
        Assert.Equal(2, yard.Stock[(int)Good.CutStone]);
    }

    [Fact]
    public void TheMasonStopsFetchingStoneOnceThereIsCutStoneEnough()
    {
        var sim = Flat();
        var store = Place(sim, P, "storehouse", 10, 10);
        var yard = Place(sim, P, "mason_yard", 24, 10);
        store.Stock[(int)Good.Stone] = 100;
        store.Stock[(int)Good.CutStone] = yard.Def.Keep;
        int w = sim.SpawnUnit(P, "worker", 18, 18);
        sim.Schedule(0, new WorkCommand(P, new[] { w }, yard.Id));
        sim.Run(30 * Tps);
        Assert.Equal(100, store.Stock[(int)Good.Stone]);
        Assert.False(sim.Economy.WorkshopWants(yard));

        store.Stock[(int)Good.CutStone] = 0;
        Assert.True(sim.Economy.WorkshopWants(yard));
        RunUntil(sim, () => store.Stock[(int)Good.CutStone] >= 5, 60 * Tps, "stone cut once it ran short");
        Assert.True(store.Stock[(int)Good.Stone] < 100);
    }

    [Fact]
    public void AnAutomaticWorkerKeepsAMasonsYardWhileThereIsStone()
    {
        var sim = Flat();
        var store = Place(sim, P, "storehouse", 10, 10);
        var yard = Place(sim, P, "mason_yard", 20, 10);
        int a = sim.SpawnUnit(P, "worker", 12, 20);
        sim.Schedule(sim.Tick, new AutomateCommand(P, new[] { a }, true));
        sim.Run(2 * Tps);
        Assert.NotEqual(UnitJob.Haul, sim.Units.Job[Index(sim, a)]); // no stone: nothing to cut

        store.Stock[(int)Good.Stone] = 20;
        RunUntil(sim, () => sim.Units.Job[Index(sim, a)] == UnitJob.Haul, 10 * Tps, "the automatic worker to take the yard");
        Assert.Equal(yard.Id, sim.Units.JobTarget[Index(sim, a)]);
        RunUntil(sim, () => store.Stock[(int)Good.CutStone] >= 10, 120 * Tps, "stone cut by the automatic worker");
    }

    [Fact]
    public void AStoneTowerIsBuiltOfCutStone()
    {
        var sim = Flat();
        sim.Economy.Grant(P, sim.Content.Tech("masonry"));
        var store = Place(sim, P, "storehouse", 10, 10);
        var tower = sim.Content.Building("stone_tower");
        int cut = tower.Cost.Single(c => c.Good == Good.CutStone).Amount;
        store.Stock[(int)Good.Wood] = 100;
        store.Stock[(int)Good.Stone] = 500; // plain stone alone does not raise it
        Assert.False(sim.Economy.CanAfford(P, tower.Cost));
        store.Stock[(int)Good.CutStone] = cut;
        Assert.True(sim.Economy.CanAfford(P, tower.Cost));

        int w = sim.SpawnUnit(P, "worker", 30, 24);
        sim.Schedule(0, new BuildCommand(P, new[] { w }, "stone_tower", 30, 30));
        sim.Step();
        var site = Assert.Single(sim.Buildings.All, b => b.Def == tower);
        RunUntil(sim, () => site.Active, 200 * Tps, "the stone tower to be built");
        Assert.Equal(0, store.Stock[(int)Good.CutStone]);
        Assert.Equal(500, store.Stock[(int)Good.Stone]);
    }

    [Fact]
    public void CutStoneInAYardCountsInTheStateHash()
    {
        var a = Flat();
        var b = Flat();
        Place(a, P, "mason_yard", 24, 10);
        var yb = Place(b, P, "mason_yard", 24, 10);
        Assert.Equal(a.StateHash(), b.StateHash());
        yb.Stock[(int)Good.CutStone] = 1;
        Assert.NotEqual(a.StateHash(), b.StateHash());
    }
}
