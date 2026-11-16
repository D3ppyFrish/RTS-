using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.Automation;
using Rts.Sim.Economy;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Supply rules: storage kept stocked from the player's other storage by workers on supply duty.</summary>
public class SupplyTests
{
    private const int Tps = Simulation.TicksPerSecond;
    private const int Ground = 3;

    private static Simulation Flat(int size = 64) => TestWorlds.Sim(TestWorlds.Flat(size, 16, size, Ground));

    private static Building Place(Simulation sim, int owner, string def, int x, int z) =>
        sim.Economy.PlaceFinished(owner, sim.Content.Building(def), x, z) ?? throw new InvalidOperationException($"{def} does not fit");

    private static void RunUntil(Simulation sim, Func<bool> done, int maxTicks, string what)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return;
            sim.Step();
        }
        Assert.Fail($"Not done after {maxTicks} ticks: {what}");
    }

    /// <summary>A town hall at (6, 6) holding 100 stone and a storehouse 30 columns away at (40, 6).</summary>
    private static (Simulation Sim, Building Hall, Building Store) Town()
    {
        var sim = Flat();
        var hall = Place(sim, 0, "town_hall", 6, 6);
        Array.Clear(hall.Stock);
        hall.Stock[(int)Good.Stone] = 100;
        hall.Stock[(int)Good.Food] = 200;
        var store = Place(sim, 0, "storehouse", 40, 6);
        return (sim, hall, store);
    }

    private static int OnDuty(Simulation sim, Building b)
    {
        var u = sim.Units;
        int n = 0;
        for (int i = 0; i < u.Count; i++)
            if (u.Job[i] == UnitJob.Supply && u.JobTarget[i] == b.Id) n++;
        return n;
    }

    [Fact]
    public void AWorkerOnSupplyDutyStocksTheStorehouseFromTheTownHall()
    {
        var (sim, hall, store) = Town();
        int w = sim.SpawnUnit(0, "worker", 20, 14);
        sim.Schedule(0, new SupplyCommand(0, store.Id, Good.Stone, 40));
        sim.Schedule(1, new WorkCommand(0, new[] { w }, store.Id));

        RunUntil(sim, () => store.Stock[(int)Good.Stone] >= 40, 3 * 60 * Tps, "the storehouse to hold 40 stone");
        sim.Run(30 * Tps);
        Assert.Equal(40, store.Stock[(int)Good.Stone]);
        Assert.Equal(60, hall.Stock[(int)Good.Stone]);
        // The player put it on duty: it waits there for more to do.
        Assert.Equal(1, OnDuty(sim, store));

        // Spending some brings it back for more.
        store.Stock[(int)Good.Stone] = 25;
        RunUntil(sim, () => store.Stock[(int)Good.Stone] == 40, 2 * 60 * Tps, "the storehouse to be topped up");
        Assert.Equal(45, hall.Stock[(int)Good.Stone]);
    }

    [Fact]
    public void ItBringsNoMoreThanTheRuleAndTakesNothingTheSourceWantsToKeep()
    {
        var (sim, hall, store) = Town();
        var workers = Enumerable.Range(0, 3).Select(k => sim.SpawnUnit(0, "worker", 20 + k, 14)).ToArray();
        // Two loads and a bit: three workers at once must not overshoot.
        sim.Schedule(0, new SupplyCommand(0, store.Id, Good.Stone, 12));
        // The town hall keeps 95 of its own, so only 5 can be spared... at first.
        sim.Schedule(0, new SupplyCommand(0, hall.Id, Good.Stone, 95));
        sim.Schedule(1, new WorkCommand(0, workers, store.Id));
        sim.Run(90 * Tps);
        Assert.Equal(5, store.Stock[(int)Good.Stone]);
        Assert.Equal(95, hall.Stock[(int)Good.Stone]);

        sim.Schedule(sim.Tick, new SupplyCommand(0, hall.Id, Good.Stone, 0));
        RunUntil(sim, () => store.Stock[(int)Good.Stone] >= 12, 2 * 60 * Tps, "the storehouse to reach its rule");
        sim.Run(60 * Tps);
        Assert.Equal(12, store.Stock[(int)Good.Stone]);
        Assert.Equal(88, hall.Stock[(int)Good.Stone]);
        Assert.All(workers, id => Assert.Equal(0, sim.Units.CarryAmount[sim.Units.IndexOf(id)]));
    }

    [Fact]
    public void ClearingTheRulesEndsTheDuty()
    {
        var (sim, _, store) = Town();
        int w = sim.SpawnUnit(0, "worker", 20, 14);
        sim.Schedule(0, new SupplyCommand(0, store.Id, Good.Stone, 10));
        sim.Schedule(1, new WorkCommand(0, new[] { w }, store.Id));
        sim.Run(5 * Tps);
        Assert.Equal(1, OnDuty(sim, store));
        sim.Schedule(sim.Tick, new SupplyCommand(0, store.Id, Good.Stone, 0));
        sim.Run(60 * Tps);
        Assert.Equal(0, OnDuty(sim, store));
        Assert.False(store.HasSupply);
        // What it was carrying went to storage: nothing is lost.
        Assert.Equal(100, sim.Economy.Stock(0, Good.Stone));
    }

    [Fact]
    public void RulesAreKeptInRangeAndOnlyOnYourOwnStorage()
    {
        var (sim, hall, store) = Town();
        var house = Place(sim, 0, "house", 20, 30);
        sim.Schedule(0, new SupplyCommand(0, store.Id, Good.Wood, 9999));
        sim.Schedule(0, new SupplyCommand(0, store.Id, Good.Food, -5));
        sim.Schedule(0, new SupplyCommand(1, hall.Id, Good.Stone, 50));
        sim.Schedule(0, new SupplyCommand(0, house.Id, Good.Stone, 50));
        sim.Step();
        Assert.Equal(EconomySystem.MaxSupply, store.Supply[(int)Good.Wood]);
        Assert.Equal(0, store.Supply[(int)Good.Food]);
        Assert.Equal(0, hall.Supply[(int)Good.Stone]);
        Assert.Equal(0, house.Supply[(int)Good.Stone]);
    }

    [Fact]
    public void AutomaticWorkersTakeSupplyDutyAndGoBackToOtherWork()
    {
        var (sim, hall, store) = Town();
        var workers = Enumerable.Range(0, 4).Select(k => sim.SpawnUnit(0, "worker", 10 + k, 16)).ToArray();
        sim.Schedule(0, new AutomateAllCommand(0, true));
        sim.Schedule(0, new SupplyCommand(0, store.Id, Good.Stone, 30));
        int most = 0;
        RunUntil(sim, () =>
        {
            most = Math.Max(most, OnDuty(sim, store));
            return store.Stock[(int)Good.Stone] >= 30;
        }, 3 * 60 * Tps, "automatic workers to stock the storehouse");
        Assert.InRange(most, 1, AutomationSystem.SupplyWorkers);
        sim.Run(20 * Tps);
        Assert.Equal(0, OnDuty(sim, store));
        Assert.Equal(30, store.Stock[(int)Good.Stone]);
        Assert.Equal(70, hall.Stock[(int)Good.Stone]);
        // Free for other work again.
        Assert.All(workers, id => Assert.NotEqual(UnitJob.Supply, sim.Units.Job[sim.Units.IndexOf(id)]));
    }

    [Fact]
    public void WorkersWithoutSupplyOnTheirListLeaveItAlone()
    {
        var (sim, _, store) = Town();
        for (int k = 0; k < 3; k++) sim.SpawnUnit(0, "worker", 10 + k, 16);
        sim.Schedule(0, new AutomateAllCommand(0, true));
        sim.Schedule(0, new DefaultPriorityCommand(0, WorkPriorities.Pack(WorkKind.Build, WorkKind.Needed)));
        sim.Schedule(0, new SupplyCommand(0, store.Id, Good.Stone, 30));
        sim.Run(60 * Tps);
        Assert.Equal(0, OnDuty(sim, store));
        Assert.Equal(0, store.Stock[(int)Good.Stone]);
    }

    [Fact]
    public void ACapturedStorehouseLosesItsRules()
    {
        var (sim, _, store) = Town();
        sim.Schedule(0, new SupplyCommand(0, store.Id, Good.Stone, 30));
        var raiders = Enumerable.Range(0, 2).Select(k => sim.SpawnUnit(1, "spearman", 40 + k, 14)).ToArray();
        sim.Schedule(1, new CaptureCommand(1, raiders, store.Id));
        RunUntil(sim, () => store.Owner == 1, 60 * Tps, "the storehouse to be captured");
        Assert.False(store.HasSupply);
    }

    [Fact]
    public void SupplyCommandsRoundTripThroughTheLogAndReplayTheSame()
    {
        var log = new CommandLog { Seed = 5, SizeX = 64, SizeY = 32, SizeZ = 64, Ticks = 100 };
        log.Add(3, new SupplyCommand(0, 7, Good.IronOre, 40));
        log.Add(9, new SupplyCommand(1, 2, Good.Food, 0));
        var back = CommandLog.Parse(log.ToString());
        Assert.Equal(log.Commands, back.Commands);

        ulong Play()
        {
            var (sim, _, store) = Town();
            for (int k = 0; k < 4; k++) sim.SpawnUnit(0, "worker", 10 + k, 16);
            sim.Schedule(0, new AutomateAllCommand(0, true));
            sim.Schedule(0, new SupplyCommand(0, store.Id, Good.Stone, 30));
            sim.Run(90 * Tps);
            return sim.StateHash();
        }
        Assert.Equal(Play(), Play());
    }
}
