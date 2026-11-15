using System;
using System.Linq;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>The event log behind alerts (issue #42), and the idle workers list.</summary>
public class EventTests
{
    private const int Tps = Simulation.TicksPerSecond;

    private static Simulation Flat(int size = 64) => TestWorlds.Sim(TestWorlds.Flat(size, 16, size));

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

    private static GameEvent[] Of(Simulation sim, int player, GameEventKind kind) =>
        sim.Events.All.Where(e => e.Player == player && e.Kind == kind).ToArray();

    [Fact]
    public void AFinishedHouseATrainedWorkerAndResearchAreReported()
    {
        var sim = Flat();
        var hall = Place(sim, 0, "town_hall", 10, 10);
        hall.Stock[(int)Good.Wood] = 500;
        hall.Stock[(int)Good.Food] = 500;
        hall.Stock[(int)Good.Stone] = 500;
        var ids = new[] { sim.SpawnUnit(0, "worker", 20, 20), sim.SpawnUnit(0, "worker", 21, 20) };
        sim.Schedule(0, new BuildCommand(0, ids, "house", 30, 30));
        sim.Schedule(0, new TrainCommand(0, hall.Id, "worker"));
        sim.Schedule(0, new ResearchCommand(0, "farming"));
        var house = sim.Content.Building("house");
        RunUntil(sim, () => sim.Buildings.All.Any(b => b.Def == house && b.Active) && sim.Economy.Researching(0) == null
                            && sim.Units.Count == 3, 300 * Tps, "the house, the worker and the research");

        var finished = Assert.Single(Of(sim, 0, GameEventKind.BuildingFinished));
        Assert.Equal(house.Index, finished.Def);
        Assert.InRange(finished.X, 30, 33);
        Assert.InRange(finished.Z, 30, 33);
        var trained = Assert.Single(Of(sim, 0, GameEventKind.UnitTrained));
        Assert.Equal(sim.Content.Unit("worker").Index, trained.Def);
        Assert.True(sim.Units.Exists(trained.Subject));
        var research = Assert.Single(Of(sim, 0, GameEventKind.ResearchDone));
        Assert.Equal(sim.Content.Tech("farming").Index, research.Def);
        // Match setup reports nothing, and the sequence numbers only grow.
        Assert.DoesNotContain(sim.Events.All, e => e.Tick == 0 && e.Kind == GameEventKind.BuildingFinished);
        var seqs = sim.Events.All.Select(e => e.Seq).ToArray();
        Assert.Equal(seqs.OrderBy(s => s), seqs);
        Assert.Empty(sim.Events.After(seqs[^1]));
    }

    [Fact]
    public void AttacksAreReportedToTheSideHitAtMostOnceAPlaceEveryFewSeconds()
    {
        var sim = Flat();
        // A fight at the west end and, later, one at the east end, far enough off to be a new place.
        for (int k = 0; k < 4; k++)
        {
            sim.SpawnUnit(0, "spearman", 10, 10 + k);
            sim.SpawnUnit(1, "spearman", 11, 10 + k);
        }
        sim.Run(10 * Tps);
        var west = Of(sim, 0, GameEventKind.UnitsAttacked);
        Assert.NotEmpty(west);
        Assert.NotEmpty(Of(sim, 1, GameEventKind.UnitsAttacked));
        // The same fight is reported again only after the repeat time.
        for (int k = 1; k < west.Length; k++)
            Assert.True(west[k].Tick - west[k - 1].Tick >= EventLog.AttackRepeatTicks);
        Assert.True(west.Length <= 10 * Tps / EventLog.AttackRepeatTicks + 1);

        int before = west.Length;
        sim.SpawnUnit(0, "spearman", 50, 50);
        sim.SpawnUnit(1, "spearman", 51, 50);
        sim.Run(2 * Tps);
        var all = Of(sim, 0, GameEventKind.UnitsAttacked);
        Assert.Contains(all.Skip(before), e => e.X >= 40);
    }

    [Fact]
    public void ANonWildFightOnlyAndNeverYourOwnUnits()
    {
        var sim = Flat();
        sim.SpawnUnit(0, "spearman", 10, 10);
        sim.SpawnUnit(0, "worker", 30, 30);
        sim.Run(5 * Tps);
        Assert.Empty(sim.Events.All);
    }

    [Fact]
    public void ABuildingUnderAttackAndATownHallBroughtDownAreReported()
    {
        var sim = Flat();
        var hall = Place(sim, 1, "town_hall", 30, 30);
        Place(sim, 0, "town_hall", 5, 5);
        var sappers = Enumerable.Range(0, 12).Select(k => sim.SpawnUnit(0, "sapper", 26 + k % 6, 26 + k / 6)).ToArray();
        sim.Schedule(0, new AttackBuildingCommand(0, sappers, hall.Id));
        RunUntil(sim, () => sim.Buildings.Get(hall.Id) == null, 300 * Tps, "the town hall to fall");
        sim.Step();

        var hit = Of(sim, 1, GameEventKind.BuildingAttacked);
        Assert.NotEmpty(hit);
        Assert.Equal(hall.Id, hit[0].Subject);
        var lost = Assert.Single(Of(sim, 1, GameEventKind.TownHallDestroyed));
        Assert.Equal(hall.Id, lost.Subject);
        var won = Assert.Single(Of(sim, 0, GameEventKind.EnemyTownHallDestroyed));
        Assert.Equal(hall.Id, won.Subject);
        Assert.Empty(Of(sim, 0, GameEventKind.TownHallDestroyed));
    }

    [Fact]
    public void IdleWorkersAreThoseWithNothingToDo()
    {
        var sim = Flat();
        int a = sim.SpawnUnit(0, "worker", 10, 10);
        int b = sim.SpawnUnit(0, "worker", 12, 10);
        int c = sim.SpawnUnit(0, "worker", 14, 10);
        sim.SpawnUnit(0, "spearman", 16, 10);
        sim.SpawnUnit(1, "worker", 40, 40);
        Assert.Equal(new[] { a, b, c }, sim.Economy.IdleWorkers(0));
        sim.Schedule(0, new MoveCommand(0, new[] { b }, 30, 30));
        sim.Step();
        Assert.Equal(new[] { a, c }, sim.Economy.IdleWorkers(0));
        sim.Run(20 * Tps);
        Assert.Equal(new[] { a, b, c }, sim.Economy.IdleWorkers(0));
    }

    [Fact]
    public void SameInputsGiveTheSameEvents()
    {
        static string Run()
        {
            var sim = Flat();
            var hall = Place(sim, 1, "town_hall", 40, 40);
            Place(sim, 0, "town_hall", 5, 5);
            var ids = Enumerable.Range(0, 16).Select(k => sim.SpawnUnit(0, k % 2 == 0 ? "sapper" : "spearman", 20 + k % 4, 20 + k / 4)).ToArray();
            for (int k = 0; k < 6; k++) sim.SpawnUnit(1, "archer", 34 + k, 34);
            sim.Schedule(5, new AttackMoveCommand(0, ids, 42, 42));
            sim.Schedule(400, new AttackBuildingCommand(0, ids, hall.Id));
            sim.Run(120 * Tps);
            return string.Join(";", sim.Events.All) + sim.StateHash();
        }
        string first = Run();
        Assert.Contains("UnitsAttacked", first);
        Assert.Equal(first, Run());
    }
}
