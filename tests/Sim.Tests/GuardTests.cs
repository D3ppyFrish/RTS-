using System;
using Rts.Sim.Economy;
using Rts.Sim.Replay;
using Rts.Sim.Sandbox;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Guard orders (keep near a unit or building and fight what comes near it) and rally points.</summary>
public class GuardTests
{
    private const int Tps = Simulation.TicksPerSecond;

    private static Simulation Flat(int size = 64) => TestWorlds.Sim(TestWorlds.Flat(size, 16, size, 3));

    private static Building Place(Simulation sim, int owner, string def, int x, int z) =>
        sim.Economy.PlaceFinished(owner, sim.Content.Building(def), x, z) ?? throw new InvalidOperationException($"{def} does not fit");

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

    private static (int X, int Z) At(Simulation sim, int id)
    {
        int i = Index(sim, id);
        return (sim.Units.X[i].FloorToInt(), sim.Units.Z[i].FloorToInt());
    }

    private static int Apart(Simulation sim, int a, int b)
    {
        var (ax, az) = At(sim, a);
        var (bx, bz) = At(sim, b);
        return Math.Max(Math.Abs(ax - bx), Math.Abs(az - bz));
    }

    private static void RunUntil(Simulation sim, Func<bool> done, int maxTicks, string what)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return;
            sim.Step();
        }
        Assert.Fail($"Not done after {maxTicks} ticks: {what}");
    }

    // --- Guard -------------------------------------------------------------------------------------

    [Fact]
    public void AGuardWalksToItsChargeAndFollowsItWhereItGoes()
    {
        var sim = Flat();
        int spear = sim.SpawnUnit(0, "spearman", 10, 10);
        int worker = sim.SpawnUnit(0, "worker", 30, 30);
        sim.Schedule(0, new GuardCommand(0, new[] { spear }, worker));
        RunUntil(sim, () => Apart(sim, spear, worker) <= CombatSystem.GuardFollow, 30 * Tps, "the guard to reach the worker");
        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { worker }, 50, 50));
        RunUntil(sim, () => At(sim, worker) == (50, 50) && sim.Units.Order[Index(sim, worker)] == UnitOrder.Idle, 30 * Tps, "the worker to get there");
        RunUntil(sim, () => Apart(sim, spear, worker) <= CombatSystem.GuardFollow, 10 * Tps, "the guard to follow the worker");
        Assert.Equal(CombatOrder.Guard, sim.Units.Combat[Index(sim, spear)]);
    }

    [Fact]
    public void AGuardFightsWhoeverAttacksItsCharge()
    {
        var sim = Flat();
        int worker = sim.SpawnUnit(0, "worker", 30, 30);
        int a = sim.SpawnUnit(0, "spearman", 30, 32);
        int b = sim.SpawnUnit(0, "spearman", 31, 32);
        sim.Schedule(0, new GuardCommand(0, new[] { a, b }, worker));
        // An enemy rider comes for the worker from the other side.
        int rider = sim.SpawnUnit(1, "rider", 30, 20);
        sim.Schedule(0, new AttackCommand(1, new[] { rider }, worker));
        RunUntil(sim, () => !sim.Units.Exists(rider), 30 * Tps, "the guards to cut down the rider");
        // Then they go back to guarding.
        Assert.Equal(CombatOrder.Guard, sim.Units.Combat[Index(sim, a)]);
        Assert.Equal(CombatOrder.Guard, sim.Units.Combat[Index(sim, b)]);
    }

    [Fact]
    public void AGuardLeavesAloneEnemiesFarFromItsCharge()
    {
        // An archer (sight 10, range 9) beside its charge, and an enemy worker 9 columns from the archer but
        // 12 from the charge: an archer on no order shoots it, a guard does not.
        Simulation Setup(bool guard, out int enemy)
        {
            var sim = Flat();
            int worker = sim.SpawnUnit(0, "worker", 10, 30);
            int archer = sim.SpawnUnit(0, "archer", 13, 30);
            enemy = sim.SpawnUnit(1, "worker", 22, 30);
            if (guard) sim.Schedule(0, new GuardCommand(0, new[] { archer }, worker));
            sim.Run(6 * Tps);
            return sim;
        }
        var free = Setup(false, out int e1);
        Assert.True(!free.Units.Exists(e1) || free.Units.Hp[free.Units.IndexOf(e1)] < 40);
        var guarded = Setup(true, out int e2);
        Assert.Equal(40, guarded.Units.Hp[guarded.Units.IndexOf(e2)]);
    }

    [Fact]
    public void AGuardLetsAnEnemyGoFarFromItsChargeAndComesBack()
    {
        var sim = Flat();
        int worker = sim.SpawnUnit(0, "worker", 10, 30);
        int spear = sim.SpawnUnit(0, "spearman", 12, 30);
        sim.Schedule(0, new GuardCommand(0, new[] { spear }, worker));
        // An enemy rider (twice as fast) rides in, then away beyond the leash.
        int rider = sim.SpawnUnit(1, "rider", 17, 30);
        sim.Units.Stance[Index(sim, rider)] = UnitStance.Hold;
        sim.Run(Tps);
        sim.Schedule(sim.Tick, new MoveCommand(1, new[] { rider }, 60, 30));
        sim.Run(15 * Tps);
        Assert.True(sim.Units.Exists(rider));
        Assert.True(Apart(sim, spear, worker) <= CombatSystem.GuardFollow, $"the guard is {Apart(sim, spear, worker)} from its charge");
        Assert.Equal(CombatOrder.Guard, sim.Units.Combat[Index(sim, spear)]);
    }

    [Fact]
    public void AGuardKeepsNearABuilding()
    {
        var sim = Flat();
        var store = Place(sim, 0, "storehouse", 30, 30);
        int spear = sim.SpawnUnit(0, "spearman", 5, 5);
        sim.Schedule(0, new GuardCommand(0, new[] { spear }, 0, store.Id));
        RunUntil(sim, () => store.ColumnDistance(At(sim, spear).X, At(sim, spear).Z) <= CombatSystem.GuardFollow
                            && sim.Units.Order[Index(sim, spear)] == UnitOrder.Idle, 30 * Tps, "the guard to reach the storehouse");
        sim.Run(10 * Tps);
        Assert.True(store.ColumnDistance(At(sim, spear).X, At(sim, spear).Z) <= CombatSystem.GuardFollow);
        Assert.Equal(CombatOrder.Guard, sim.Units.Combat[Index(sim, spear)]);
    }

    [Fact]
    public void TheGuardEndsWhenItsChargeDiesOrAnotherOrderComes()
    {
        var sim = Flat();
        sim.TestMode = true;
        int worker = sim.SpawnUnit(0, "worker", 30, 30);
        int a = sim.SpawnUnit(0, "spearman", 20, 30);
        int b = sim.SpawnUnit(0, "spearman", 20, 32);
        sim.Schedule(0, new GuardCommand(0, new[] { a, b }, worker));
        sim.Schedule(2 * Tps, new MoveCommand(0, new[] { b }, 10, 10));
        sim.Run(3 * Tps);
        Assert.Equal(CombatOrder.Auto, sim.Units.Combat[Index(sim, b)]);
        Assert.Equal(CombatOrder.Guard, sim.Units.Combat[Index(sim, a)]);
        sim.Schedule(sim.Tick, new SandboxUnitsCommand(0, new[] { worker }, SandboxAction.Kill));
        sim.Run(2 * Tps);
        Assert.False(sim.Units.Exists(worker));
        Assert.Equal(CombatOrder.Auto, sim.Units.Combat[Index(sim, a)]);
        Assert.Equal(0, sim.Units.GuardUnit[Index(sim, a)]);
    }

    [Fact]
    public void OnlyOwnChargesCanBeGuardedAndWorkersAndTheChargeItselfIgnoreIt()
    {
        var sim = Flat();
        int spear = sim.SpawnUnit(0, "spearman", 10, 10);
        int enemy = sim.SpawnUnit(1, "worker", 30, 30);
        var theirs = Place(sim, 1, "storehouse", 40, 40);
        sim.Schedule(0, new GuardCommand(0, new[] { spear }, enemy));
        sim.Schedule(0, new GuardCommand(0, new[] { spear }, 0, theirs.Id));
        sim.Schedule(0, new GuardCommand(0, new[] { spear }, spear));
        sim.Run(2);
        Assert.Equal(CombatOrder.Auto, sim.Units.Combat[Index(sim, spear)]);
    }

    // --- Rally points ------------------------------------------------------------------------------

    [Fact]
    public void TrainedUnitsWalkToTheRallyPoint()
    {
        var sim = Flat();
        var hall = Place(sim, 0, "town_hall", 10, 10);
        hall.Stock[(int)Good.Food] = 500;
        sim.Schedule(0, new RallyCommand(0, hall.Id, 40, 40));
        sim.Schedule(0, new TrainCommand(0, hall.Id, "worker"));
        sim.Schedule(0, new TrainCommand(0, hall.Id, "worker"));
        sim.Step();
        Assert.True(hall.HasRally);
        Assert.Equal((40, 40), (hall.RallyX, hall.RallyZ));
        int before = sim.Units.Count;
        RunUntil(sim, () => sim.Units.Count == before + 2, 60 * Tps, "two workers to be trained");
        sim.Run(20 * Tps);
        for (int i = before; i < sim.Units.Count; i++)
        {
            int x = sim.Units.X[i].FloorToInt(), z = sim.Units.Z[i].FloorToInt();
            Assert.True(Math.Max(Math.Abs(x - 40), Math.Abs(z - 40)) <= 2, $"unit {i} is at {x}, {z}");
        }
    }

    [Fact]
    public void TheRallyPointIsClearedOnTheBuildingAndOnlyOwnTrainingBuildingsTakeOne()
    {
        var sim = Flat();
        var hall = Place(sim, 0, "town_hall", 10, 10);
        var house = Place(sim, 0, "house", 30, 10);
        var theirs = Place(sim, 1, "town_hall", 40, 40);
        sim.Schedule(0, new RallyCommand(0, hall.Id, 30, 30));
        sim.Schedule(0, new RallyCommand(0, house.Id, 30, 30));
        sim.Schedule(0, new RallyCommand(0, theirs.Id, 30, 30));
        sim.Step();
        Assert.True(hall.HasRally);
        Assert.False(house.HasRally);
        Assert.False(theirs.HasRally);
        // A rally point on the building itself, or none at all, clears it: units stay at the door.
        sim.Schedule(sim.Tick, new RallyCommand(0, hall.Id, hall.X + 1, hall.Z + 1));
        sim.Step();
        Assert.False(hall.HasRally);
        sim.Schedule(sim.Tick, new RallyCommand(0, hall.Id, 30, 30));
        sim.Schedule(sim.Tick + 1, new RallyCommand(0, hall.Id, -1, -1));
        sim.Run(2);
        Assert.False(hall.HasRally);
    }

    // --- Logs and replays --------------------------------------------------------------------------

    [Fact]
    public void GuardAndRallyAreLoggedAndReplayTheSame()
    {
        var log = CommandLog.Parse("rts-log 1\nseed 1\nworld 32 16 32\nticks 10\n5 guard 1 7 0 101,102\n6 guard 0 0 4 9\n7 rally 0 3 20 21 -1\n");
        Assert.Equal(new GuardCommand(1, new[] { 101, 102 }, 7), log.Commands[0].Command);
        Assert.Equal(new GuardCommand(0, new[] { 9 }, 0, 4), log.Commands[1].Command);
        Assert.Equal(new RallyCommand(0, 3, 20, 21), log.Commands[2].Command);
        string text = log.ToString();
        Assert.Contains("5 guard 1 7 0 101,102", text);
        Assert.Contains("6 guard 0 0 4 9", text);
        Assert.Contains("7 rally 0 3 20 21 -1", text);

        ulong Play()
        {
            var sim = Flat();
            var hall = Place(sim, 0, "town_hall", 10, 10);
            hall.Stock[(int)Good.Food] = 500;
            int worker = sim.SpawnUnit(0, "worker", 30, 30);
            int a = sim.SpawnUnit(0, "spearman", 20, 30);
            int b = sim.SpawnUnit(0, "archer", 20, 32);
            int rider = sim.SpawnUnit(1, "rider", 45, 30);
            sim.Schedule(0, new GuardCommand(0, new[] { a, b }, worker));
            sim.Schedule(0, new AttackCommand(1, new[] { rider }, worker));
            sim.Schedule(0, new RallyCommand(0, hall.Id, 25, 20));
            sim.Schedule(0, new TrainCommand(0, hall.Id, "worker"));
            sim.Run(30 * Tps);
            return sim.StateHash();
        }
        Assert.Equal(Play(), Play());
    }
}
