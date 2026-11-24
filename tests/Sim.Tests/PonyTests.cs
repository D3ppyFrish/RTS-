using System;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Pack ponies (Handcarts, #149): a far site's goods are carried out to it before its work can go on, by its
/// builders or by a pony; ponies feed workshops and carry what workshops, quarries and mines make to storage.
/// </summary>
public class PonyTests
{
    private const int Tps = Simulation.TicksPerSecond;
    private const int P = 0;

    private static Simulation Flat(int size = 96) => TestWorlds.Sim(TestWorlds.Flat(size, 16, size, 3));

    private static Building Place(Simulation sim, string def, int x, int z) =>
        sim.Economy.PlaceFinished(P, sim.Content.Building(def), x, z) ?? throw new InvalidOperationException($"{def} does not fit");

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

    /// <summary>A pack pony as the town hall trains it: automatic.</summary>
    private static int Pony(Simulation sim, int x, int z)
    {
        int id = sim.SpawnUnit(P, "pack_pony", x, z);
        sim.Units.Auto[sim.Units.IndexOf(id)] = true;
        return id;
    }

    private static Building SiteAt(Simulation sim, int x, int z)
    {
        foreach (var b in sim.Buildings.All)
            if (b.X == x && b.Z == z) return b;
        throw new InvalidOperationException("no site there");
    }

    [Fact]
    public void ThePackPonyIsInTheData()
    {
        var db = ContentLoader.LoadEmbedded();
        var pony = db.Unit("pack_pony");
        Assert.Equal(UnitRole.Hauler, pony.Role);
        Assert.True(pony.Runs);
        Assert.False(pony.IsCart);
        Assert.True(db.Unit("cart").IsCart);
        Assert.False(pony.CanAttack);
        Assert.Equal("town_hall", pony.TrainedAt!.Id);
        Assert.Contains(pony.Requires, t => t.Id == "carts");
        // Quicker than a worker and a cart, carrying more than a worker and less than a cart.
        Assert.True(pony.Speed > db.Unit("worker").Speed && pony.Speed > db.Unit("cart").Speed);
        Assert.InRange(pony.Carry, EconomySystem.CarryCapacity + 1, db.Unit("cart").Carry - 1);
        // Handcarts no longer makes workers carry more.
        Assert.Empty(db.Tech("carts").Effects);
    }

    [Fact]
    public void ASiteNearStorageHasItsGoodsAtHand()
    {
        var sim = Flat();
        var hall = Place(sim, "town_hall", 6, 6);
        hall.Stock[(int)Good.Wood] = 100;
        int w = sim.SpawnUnit(P, "worker", 16, 16);
        sim.Schedule(0, new BuildCommand(P, new[] { w }, "house", 18, 6));
        sim.Step();
        var site = SiteAt(sim, 18, 6);
        Assert.False(site.HasDue);
        Assert.Equal(site.Def.BuildTicks, sim.Economy.MaxWork(site));
    }

    /// <summary>Ticks for one builder to put up a house 60 columns from the town hall, with a pack pony or without.</summary>
    private static int FarHouse(bool pony, out Simulation sim, out Building site)
    {
        sim = Flat();
        var hall = Place(sim, "town_hall", 6, 6);
        hall.Stock[(int)Good.Wood] = 100;
        int w = sim.SpawnUnit(P, "worker", 70, 14);
        if (pony) Pony(sim, 14, 14);
        sim.Schedule(0, new BuildCommand(P, new[] { w }, "house", 66, 6));
        sim.Step();
        site = SiteAt(sim, 66, 6);
        var s = site;
        return RunUntil(sim, () => s.Active, 30 * 60 * Tps, "the far house to be finished");
    }

    [Fact]
    public void AFarSiteWaitsForItsGoodsAndItsBuildersFetchThem()
    {
        var sim = Flat();
        var hall = Place(sim, "town_hall", 6, 6);
        hall.Stock[(int)Good.Wood] = 100;
        int w = sim.SpawnUnit(P, "worker", 70, 14);
        sim.Schedule(0, new BuildCommand(P, new[] { w }, "house", 66, 6));
        sim.Step();
        var site = SiteAt(sim, 66, 6);
        // Paid as ever (from the hall), but the wood is still to be carried out.
        Assert.Equal(70, hall.Stock[(int)Good.Wood]);
        Assert.Equal(30, site.Due[(int)Good.Wood]);
        Assert.Equal(0, sim.Economy.MaxWork(site));

        // No work goes on until some wood is there: the builder walks for it.
        sim.Run(5 * Tps);
        Assert.Equal(0, site.Work);
        Assert.Equal(JobPhase.Fetch, sim.Units.Phase[sim.Units.IndexOf(w)]);

        RunUntil(sim, () => site.Active, 30 * 60 * Tps, "the far house to be finished");
        Assert.False(site.HasDue);
        Assert.Equal(70, hall.Stock[(int)Good.Wood]); // what it fetched was set aside: nothing more paid
    }

    [Fact]
    public void AFarSiteFinishesSoonerWithAPackPony()
    {
        int without = FarHouse(pony: false, out _, out _);
        int with = FarHouse(pony: true, out var sim, out _);
        Assert.True(with < without * 3 / 4, $"with a pony {with} ticks, without {without}");
        // The pony went back to waiting for work, on its own.
        var u = sim.Units;
        for (int i = 0; i < u.Count; i++)
            if (sim.Content.Units[u.Def[i]].Runs) Assert.True(u.Auto[i]);
    }

    [Fact]
    public void APonyBringsAFarSiteItsGoodsOnItsOwn()
    {
        var sim = Flat();
        var hall = Place(sim, "town_hall", 6, 6);
        hall.Stock[(int)Good.Wood] = 100;
        int w = sim.SpawnUnit(P, "worker", 70, 14);
        int pony = Pony(sim, 14, 14);
        sim.Schedule(0, new BuildCommand(P, new[] { w }, "house", 66, 6));
        sim.Step();
        var site = SiteAt(sim, 66, 6);
        int most = 0;
        RunUntil(sim, () =>
        {
            int i = sim.Units.IndexOf(pony);
            if (sim.Units.Job[i] == UnitJob.Bring) most = Math.Max(most, sim.Units.CarryAmount[i]);
            return !site.HasDue;
        }, 10 * 60 * Tps, "the pony to bring the wood out");
        Assert.Equal(sim.Content.Unit("pack_pony").Carry, most);
    }

    [Fact]
    public void StorageBuiltNearAFarSiteBringsItsGoodsToHand()
    {
        var sim = Flat();
        var hall = Place(sim, "town_hall", 6, 6);
        hall.Stock[(int)Good.Wood] = 100;
        int w = sim.SpawnUnit(P, "worker", 70, 14);
        sim.Schedule(0, new BuildCommand(P, new[] { w }, "house", 66, 6));
        sim.Step();
        var site = SiteAt(sim, 66, 6);
        Assert.True(site.HasDue);
        Place(sim, "storehouse", 70, 20);
        sim.Run(Tps + 1);
        Assert.False(site.HasDue);
    }

    [Fact]
    public void ASawmillsPlanksReachStorageWithoutAWorkerCarryingThem()
    {
        var sim = Flat();
        var store = Place(sim, "storehouse", 10, 10);
        store.Stock[(int)Good.Wood] = 12;
        Place(sim, "sawmill", 30, 10);
        Pony(sim, 18, 18);
        // No worker at all: the pony feeds the mill its wood and runs the planks home.
        RunUntil(sim, () => store.Stock[(int)Good.Planks] == 12, 6000, "twelve planks stored");
        Assert.Equal(0, store.Stock[(int)Good.Wood]);
    }

    [Fact]
    public void QuarryWorkersPileTheirStoneForAPonyAndKeepWorking()
    {
        var sim = Flat();
        var store = Place(sim, "storehouse", 10, 10);
        var quarry = Place(sim, "quarry", 40, 10);
        int w = sim.SpawnUnit(P, "worker", 44, 18);
        sim.Schedule(0, new WorkCommand(P, new[] { w }, quarry.Id));
        int pony = Pony(sim, 18, 18);
        bool walked = false;
        RunUntil(sim, () =>
        {
            int i = sim.Units.IndexOf(w);
            if (sim.Units.Phase[i] == JobPhase.ToStore) walked = true;
            return store.Stock[(int)Good.Stone] >= 20;
        }, 10 * 60 * Tps, "twenty stone stored");
        Assert.False(walked); // the worker never left the quarry with its stone
        Assert.Equal(UnitJob.Quarry, sim.Units.Job[sim.Units.IndexOf(w)]);
        Assert.True(sim.Units.Exists(pony));
    }

    [Fact]
    public void WithoutAPonyQuarryWorkersCarryTheirOwnStone()
    {
        var sim = Flat();
        var store = Place(sim, "storehouse", 10, 10);
        var quarry = Place(sim, "quarry", 40, 10);
        int w = sim.SpawnUnit(P, "worker", 44, 18);
        sim.Schedule(0, new WorkCommand(P, new[] { w }, quarry.Id));
        RunUntil(sim, () => store.Stock[(int)Good.Stone] > 0, 10 * 60 * Tps, "some stone stored");
        Assert.Equal(0, EconomySystem.Piled(quarry) is null ? 0 : 1);
    }
}
