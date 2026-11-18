using System;
using System.Linq;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Units that trapped themselves or got stuck on the way (#37).</summary>
public class StuckTests
{
    private const int Tps = Simulation.TicksPerSecond;

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

    private static void RunUntil(Simulation sim, Func<bool> done, int maxTicks, string what)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return;
            sim.Step();
        }
        Assert.True(done(), $"waited {maxTicks} ticks for {what}");
    }

    private static double Across(Simulation sim, int id, int x, int z)
    {
        int i = Index(sim, id);
        double dx = sim.Units.X[i].ToFloat() - (x + 0.5), dz = sim.Units.Z[i].ToFloat() - (z + 0.5);
        return Math.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>
    /// Ground at 10 (units stand at 11), and a shaft two deep at (30, 30): a unit at its bottom stands at 9.
    /// The stone at (31, 9, 30) beside the bottom can only be got at from down the shaft, and with it gone
    /// the shaft is still two deep all round.
    /// </summary>
    private static Simulation Shaft()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(64, 24, 64, 10));
        sim.World.Set(30, 10, 30, BlockType.Air);
        sim.World.Set(30, 9, 30, BlockType.Air);
        return sim;
    }

    [Fact]
    public void StoneIsNotWorkedFromAHoleTheWorkerCouldNotClimbOutOf()
    {
        var sim = Shaft();
        var w = sim.World;
        Assert.True(Harvest.WorkSpot(w, 31, 9, 9, 30, 30, 11, 25, out _, out int sy, out _));
        Assert.Equal(9, sy); // the only place to stand at it is the bottom of the shaft
        Assert.False(Harvest.WorkSpot(w, 31, 9, 9, 30, 30, 11, 25, out _, out _, out _, allowOnTop: false, wayOut: true));

        // Told to dig it, a worker on the ground does not go down the shaft: it gives the job up.
        int worker = sim.SpawnUnit(0, "worker", 30, 25);
        sim.Schedule(0, new GatherCommand(0, new[] { worker }, 31, 9, 30));
        sim.Run(10 * Tps);
        Assert.Equal(UnitJob.None, sim.Units.Job[Index(sim, worker)]);
        Assert.Equal(11, sim.Units.Y[Index(sim, worker)]);
        Assert.Equal(BlockType.Stone, w.Get(31, 9, 30));
    }

    [Fact]
    public void DiggingTheStepAnotherUnitNeedsShutsItIn()
    {
        var sim = Shaft();
        var w = sim.World;
        w.Set(31, 10, 30, BlockType.Air); // a step out of the shaft: from 9 up to 10, then up to 11
        Assert.True(Harvest.ShutsIn(w, 31, 9, 30, 30, 9, 30));   // digging the step leaves the shaft two deep
        Assert.False(Harvest.ShutsIn(w, 29, 9, 30, 30, 9, 30));  // digging the other side does not
        Assert.False(Harvest.ShutsIn(w, 31, 9, 30, 20, 11, 20)); // and nobody far off is shut in by it
    }

    [Fact]
    public void ACrowdDoesNotShoveAnIdleUnitOffACliff()
    {
        // A plateau three blocks above the ground east of x = 20: three idle units bunched at its edge push
        // apart, and the one pushed east stays on top instead of dropping off.
        var w = TestWorlds.Flat();
        TestWorlds.Box(w, 0, 4, 0, 20, 6, 63, BlockType.Stone);
        var sim = TestWorlds.Sim(w);
        var ids = new[] { 20, 20, 20 }.Select(x => sim.SpawnUnit(0, "spearman", x, 30)).ToArray();
        for (int k = 0; k < ids.Length; k++)
            sim.Units.X[Index(sim, ids[k])] = Fix64.FromInt(20) + Fix64.FromRatio(7 + k, 10);
        Assert.All(ids, id => Assert.Equal(7, sim.Units.Y[Index(sim, id)]));
        sim.Run(2 * Tps);
        Assert.All(ids, id => Assert.Equal(7, sim.Units.Y[Index(sim, id)]));
        // Separated all the same.
        var xs = ids.Select(id => sim.Units.X[Index(sim, id)].ToFloat()).OrderBy(x => x).ToArray();
        Assert.True(xs[2] - xs[0] > 0.6, $"still bunched: {string.Join(", ", xs)}");
    }

    [Fact]
    public void AUnitOnARoofClimbsDownWhenSentSomewhere()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(64, 24, 64));
        var hall = sim.Economy.PlaceFinished(0, sim.Content.Building("town_hall"), 20, 20)!;
        int x = hall.X + 3, z = hall.Z + 3;
        int unit = sim.SpawnUnit(0, "spearman", x, z);
        Assert.True(sim.Units.Y[Index(sim, unit)] > 4 + Walk.MaxDrop, "it starts on the roof, too high to drop off");
        sim.Schedule(0, new MoveCommand(0, new[] { unit }, 50, 50));
        RunUntil(sim, () => sim.Units.Order[Index(sim, unit)] == UnitOrder.Idle && sim.Tick > 0, 60 * Tps, "the unit to stop");
        Assert.True(Across(sim, unit, 50, 50) < 1, "it climbed down and walked there");
    }

    [Fact]
    public void UnitsSentUpATreeStopUnderIt()
    {
        // A click on a tree's crown is a spot up in the leaves nobody can get to: they go as near as they can.
        var w = TestWorlds.Flat();
        TestWorlds.Tree(w, 40, 40, 5);
        var sim = TestWorlds.Sim(w);
        var g = sim.Walkable;
        int crown = g.NearestNode(40, 40, -1);
        Assert.True(g.Level(crown) > 9, "the top of the tree is somewhere to stand");
        var ids = Enumerable.Range(0, 4).Select(k => sim.SpawnUnit(0, "spearman", 10 + k, 10)).ToArray();
        sim.Schedule(0, new MoveCommand(0, ids, 40, 40, g.Level(crown)));
        sim.Run(2);
        RunUntil(sim, () => ids.All(id => sim.Units.Order[Index(sim, id)] == UnitOrder.Idle), 60 * Tps, "the units to stop");
        Assert.All(ids, id => Assert.True(Across(sim, id, 40, 40) < 3, $"unit {id} stopped {Across(sim, id, 40, 40):F1} from the tree"));
        Assert.All(ids, id => Assert.Equal(4, sim.Units.Y[Index(sim, id)]));
    }

    [Fact]
    public void AWorkerInAPitDigsItsWayOutWhenSentSomewhere()
    {
        // A pit three deep and two wide: a worker in it cannot climb out, a soldier neither. Sent off, the worker
        // digs steps up and walks on; the soldier stays.
        var w = TestWorlds.Flat(64, 16, 64, 8);
        TestWorlds.Box(w, 30, 6, 30, 31, 8, 30, BlockType.Air);
        var sim = TestWorlds.Sim(w);
        int worker = sim.SpawnUnit(0, "worker", 30, 30);
        int soldier = sim.SpawnUnit(0, "spearman", 31, 30);
        Assert.Equal(6, sim.Units.Y[Index(sim, worker)]);
        sim.Schedule(0, new MoveCommand(0, new[] { worker, soldier }, 30, 50));
        RunUntil(sim, () => sim.Units.Job[Index(sim, worker)] == UnitJob.Tunnel, 10 * Tps, "the worker to start digging out");
        RunUntil(sim, () => sim.Units.Job[Index(sim, worker)] == UnitJob.None, 2 * 60 * Tps, "the worker to dig out");
        Assert.True(sim.Units.Y[Index(sim, worker)] > 6, "it climbed its steps");
        Assert.Equal(6, sim.Units.Y[Index(sim, soldier)]);
        Assert.Equal(UnitJob.None, sim.Units.Job[Index(sim, soldier)]);
        // Out, it goes where it was sent, along the steps it dug.
        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { worker }, 30, 50));
        RunUntil(sim, () => Across(sim, worker, 30, 50) < 1, 30 * Tps, "the worker to get there");
    }
}
