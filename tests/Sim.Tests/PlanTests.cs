using System;
using Rts.Sim.Automation;
using Rts.Sim.Economy;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Planner mode: plans are marked out without paying and started later (issue #92).</summary>
public class PlanTests
{
    private const int Ground = 3;

    private static Simulation Flat(int size = 64) => TestWorlds.Sim(TestWorlds.Flat(size, 16, size, Ground));

    private static Building Hall(Simulation sim) =>
        sim.Economy.PlaceFinished(0, sim.Content.Building("town_hall"), 10, 10) ?? throw new InvalidOperationException();

    private static void RunUntil(Simulation sim, Func<bool> done, int maxTicks, string what)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return;
            sim.Step();
        }
        Assert.Fail($"Not done after {maxTicks} ticks: {what}");
    }

    [Fact]
    public void APlanIsPlacedWithoutGoodsAndCostsNothing()
    {
        var sim = Flat();
        var hall = Hall(sim);
        hall.Stock[(int)Good.Wood] = 0;
        sim.Schedule(0, new PlanCommand(0, "house", 30, 30));
        sim.Step();
        var plan = Assert.Single(sim.Buildings.All, b => b.Def.Id == "house");
        Assert.Equal(BuildingState.Planned, plan.State);
        Assert.Equal(0, plan.Placed);
        Assert.Equal(0, hall.Stock[(int)Good.Wood]);
        Assert.Equal(PlaceResult.CannotAfford, sim.Economy.CanStart(plan));
        // It keeps its footprint: nothing else may go there.
        Assert.Equal(PlaceResult.Crowded, sim.Economy.CanPlace(0, sim.Content.Building("house"), 31, 31, out _, checkPlayer: false));
        // It is not worked on and gives no housing while planned.
        for (int t = 0; t < 200; t++) sim.Step();
        Assert.Equal(BuildingState.Planned, plan.State);
        Assert.Equal(25, sim.Economy.Housing(0));
    }

    [Fact]
    public void APlanThatDoesNotFitTheGroundIsRefused()
    {
        var sim = Flat();
        Hall(sim);
        sim.Schedule(0, new PlanCommand(0, "house", 12, 12)); // on the town hall
        sim.Schedule(0, new PlanCommand(0, "house", 62, 30)); // off the map
        sim.Step();
        Assert.DoesNotContain(sim.Buildings.All, b => b.Def.Id == "house");
    }

    [Fact]
    public void WorkOnAPlanWaitsForTheGoodsThenPaysAndBuildsIt()
    {
        var sim = Flat();
        var hall = Hall(sim);
        hall.Stock[(int)Good.Wood] = 10;
        int worker = sim.SpawnUnit(0, "worker", 20, 20);
        sim.Schedule(0, new PlanCommand(0, "house", 30, 30));
        sim.Step();
        var plan = Assert.Single(sim.Buildings.All, b => b.Def.Id == "house");

        // Not enough wood: the order does nothing.
        sim.Schedule(sim.Tick, new WorkCommand(0, new[] { worker }, plan.Id));
        sim.Step();
        Assert.Equal(BuildingState.Planned, plan.State);
        Assert.Equal(10, hall.Stock[(int)Good.Wood]);
        Assert.Equal(UnitJob.None, sim.Units.Job[sim.Units.IndexOf(worker)]);

        // With the wood in store it is paid for and becomes a site the worker builds.
        hall.Stock[(int)Good.Wood] = 40;
        Assert.Equal(PlaceResult.Ok, sim.Economy.CanStart(plan));
        sim.Schedule(sim.Tick, new WorkCommand(0, new[] { worker }, plan.Id));
        sim.Step();
        Assert.Equal(BuildingState.Site, plan.State);
        Assert.Equal(10, hall.Stock[(int)Good.Wood]);
        Assert.Equal(UnitJob.Build, sim.Units.Job[sim.Units.IndexOf(worker)]);
        RunUntil(sim, () => plan.Active, 2000, "the house to be built");
    }

    [Fact]
    public void APlanIsLeftAloneByAutomaticWorkersAndCanBeCancelled()
    {
        var sim = Flat();
        var hall = Hall(sim);
        hall.Stock[(int)Good.Wood] = 200;
        int worker = sim.SpawnUnit(0, "worker", 20, 20);
        sim.Schedule(0, new AutomateCommand(0, new[] { worker }, true));
        sim.Schedule(0, new PlanCommand(0, "house", 30, 30));
        for (int t = 0; t < 300; t++) sim.Step();
        var plan = Assert.Single(sim.Buildings.All, b => b.Def.Id == "house");
        Assert.Equal(BuildingState.Planned, plan.State);
        Assert.NotEqual(plan.Id, sim.Units.JobTarget[sim.Units.IndexOf(worker)]);

        sim.Schedule(sim.Tick, new CancelPlanCommand(1, plan.Id)); // not theirs
        sim.Step();
        Assert.NotNull(sim.Buildings.Get(plan.Id));
        sim.Schedule(sim.Tick, new CancelPlanCommand(0, plan.Id));
        sim.Step();
        Assert.Null(sim.Buildings.Get(plan.Id));
    }

    [Fact]
    public void APlanCannotBeAttackedOrRaided()
    {
        var sim = Flat();
        Hall(sim);
        sim.Schedule(0, new PlanCommand(0, "house", 30, 30));
        sim.Step();
        var plan = Assert.Single(sim.Buildings.All, b => b.Def.Id == "house");
        int spear = sim.SpawnUnit(1, "spearman", 40, 40);
        sim.Schedule(sim.Tick, new AttackBuildingCommand(1, new[] { spear }, plan.Id));
        sim.Step();
        Assert.Equal(0, sim.Units.TargetBuilding[sim.Units.IndexOf(spear)]);
    }

    [Fact]
    public void PlanCommandsRoundTripThroughTheLog()
    {
        var log = new CommandLog { Seed = 5, SizeX = 64, SizeY = 32, SizeZ = 64, Ticks = 100, StartUnits = 3 };
        log.Add(1, new PlanCommand(0, "house", 60, 70));
        log.Add(2, new CancelPlanCommand(0, 7));
        var copy = CommandLog.Parse(log.ToString());
        Assert.Equal(log.Commands, copy.Commands);
        Assert.Equal(log.ToString(), copy.ToString());
    }
}
