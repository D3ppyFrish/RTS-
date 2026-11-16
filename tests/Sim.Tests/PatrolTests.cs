using System;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Patrol orders: walk to a point and back again and again, fighting what is met on the way.</summary>
public class PatrolTests
{
    private const int Tps = Simulation.TicksPerSecond;

    private static Simulation Flat(int size = 64) => TestWorlds.Sim(TestWorlds.Flat(size, 16, size, 3));

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

    private static int X(Simulation sim, int id) => sim.Units.X[Index(sim, id)].FloorToInt();

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
    public void APatrolWalksToTheGoalAndBackAgainAndAgain()
    {
        var sim = Flat();
        int spear = sim.SpawnUnit(0, "spearman", 10, 30);
        sim.Schedule(0, new PatrolCommand(0, new[] { spear }, 40, 30));
        for (int lap = 0; lap < 2; lap++)
        {
            RunUntil(sim, () => X(sim, spear) >= 39, 60 * Tps, $"lap {lap}: the spearman to reach the far end");
            RunUntil(sim, () => X(sim, spear) <= 11, 60 * Tps, $"lap {lap}: the spearman to walk back to where it started");
        }
        int i = Index(sim, spear);
        Assert.Equal(CombatOrder.Patrol, sim.Units.Combat[i]);
        Assert.Equal(UnitOrder.Move, sim.Units.Order[i]);
    }

    [Fact]
    public void APatrolFightsWhatItMeetsAndCarriesOn()
    {
        var sim = Flat();
        int spear = sim.SpawnUnit(0, "spearman", 10, 30);
        int worker = sim.SpawnUnit(1, "worker", 25, 31); // on the way; workers do not start fights
        sim.Schedule(0, new PatrolCommand(0, new[] { spear }, 40, 30));
        RunUntil(sim, () => !sim.Units.Exists(worker), 60 * Tps, "the spearman to cut down the worker on its way");
        // It does not turn back after the fight: it carries on to the far end, then comes back.
        RunUntil(sim, () => X(sim, spear) >= 39, 60 * Tps, "the spearman to carry on to the far end");
        RunUntil(sim, () => X(sim, spear) <= 11, 60 * Tps, "the spearman to walk back");
        Assert.Equal(CombatOrder.Patrol, sim.Units.Combat[Index(sim, spear)]);
    }

    [Fact]
    public void AnotherOrderEndsThePatrol()
    {
        var sim = Flat();
        int spear = sim.SpawnUnit(0, "spearman", 10, 30);
        sim.Schedule(0, new PatrolCommand(0, new[] { spear }, 40, 30));
        sim.Schedule(2 * Tps, new MoveCommand(0, new[] { spear }, 20, 20));
        RunUntil(sim, () => sim.Units.Order[Index(sim, spear)] == UnitOrder.Idle && sim.Tick > 2 * Tps, 60 * Tps, "the spearman to get there");
        sim.Run(5 * Tps);
        int i = Index(sim, spear);
        Assert.Equal(CombatOrder.Auto, sim.Units.Combat[i]);
        Assert.Equal(UnitOrder.Idle, sim.Units.Order[i]);
        Assert.Equal((20, 20), (X(sim, spear), sim.Units.Z[i].FloorToInt()));
    }

    [Fact]
    public void APatrolToAGoalRightBesideIsOnlyAnAttackMove()
    {
        var sim = Flat();
        int spear = sim.SpawnUnit(0, "spearman", 10, 30);
        sim.Schedule(0, new PatrolCommand(0, new[] { spear }, 11, 30));
        sim.Run(5 * Tps);
        int i = Index(sim, spear);
        Assert.Equal(CombatOrder.Auto, sim.Units.Combat[i]);
        Assert.Equal(UnitOrder.Idle, sim.Units.Order[i]);
    }

    [Fact]
    public void APatrolWithNoWayToItsGoalEnds()
    {
        // A wall of stone right across the map, three blocks high: there is no way to the other side.
        var w = TestWorlds.Flat(64, 16, 64, 3);
        for (int z = 0; z < 64; z++)
            for (int y = 4; y <= 6; y++)
                w.Set(30, y, z, BlockType.Stone);
        var sim = TestWorlds.Sim(w);
        int spear = sim.SpawnUnit(0, "spearman", 10, 30);
        sim.Schedule(0, new PatrolCommand(0, new[] { spear }, 50, 30));
        RunUntil(sim, () => sim.Units.Combat[Index(sim, spear)] == CombatOrder.Auto, 120 * Tps, "the patrol to give up");
        Assert.True(X(sim, spear) < 30);
    }

    [Fact]
    public void PatrolsAreLoggedAndReplayTheSame()
    {
        var log = CommandLog.Parse("rts-log 1\nseed 1\nworld 32 16 32\nticks 10\n5 patrol 1 20 21 101,102\n6 patrol 0 3 4 7 5\n");
        Assert.Equal(new PatrolCommand(1, new[] { 101, 102 }, 20, 21), log.Commands[0].Command);
        Assert.Equal(new PatrolCommand(0, new[] { 7 }, 3, 4, 5), log.Commands[1].Command);
        Assert.Contains("5 patrol 1 20 21 101,102", log.ToString());
        Assert.Contains("6 patrol 0 3 4 7 5", log.ToString());

        // The same patrol played twice ends in the same state.
        ulong Play()
        {
            var sim = Flat();
            int a = sim.SpawnUnit(0, "spearman", 10, 30);
            int b = sim.SpawnUnit(0, "archer", 10, 32);
            sim.SpawnUnit(1, "spearman", 30, 31);
            sim.Schedule(0, new PatrolCommand(0, new[] { a, b }, 45, 31));
            sim.Run(40 * Tps);
            return sim.StateHash();
        }
        Assert.Equal(Play(), Play());
    }
}
