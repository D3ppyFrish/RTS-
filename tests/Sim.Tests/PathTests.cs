using System;
using System.Linq;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Walk graph and flow-field pathfinding on terrain that changes.</summary>
public class PathTests
{
    // TestWorlds.Flat: grass at level 3, so units stand at level 4.
    private const int Feet = 4;

    private static int[] SpawnGroup(Simulation sim, int count, int x, int z) =>
        Enumerable.Range(0, count).Select(k => sim.SpawnUnit(0, "worker", x + k % 5, z + k / 5)).ToArray();

    /// <summary>Runs until every unit is idle (or the limit), recording the furthest any unit got in z.</summary>
    private static (int Ticks, int MaxZ, int MinZ) RunUntilIdle(Simulation sim, int[] ids, int limit)
    {
        int maxZ = int.MinValue, minZ = int.MaxValue;
        for (int t = 0; t < limit; t++)
        {
            sim.Step();
            bool moving = false;
            foreach (int id in ids)
            {
                int i = sim.Units.IndexOf(id);
                maxZ = Math.Max(maxZ, sim.Units.Z[i].FloorToInt());
                minZ = Math.Min(minZ, sim.Units.Z[i].FloorToInt());
                moving |= sim.Units.Order[i] == UnitOrder.Move;
            }
            if (!moving) return (t + 1, maxZ, minZ);
        }
        return (limit, maxZ, minZ);
    }

    private static void AssertAllNear(Simulation sim, int[] ids, int x, int z, double within)
    {
        foreach (int id in ids)
        {
            int i = sim.Units.IndexOf(id);
            double dx = sim.Units.X[i].ToFloat() - (x + 0.5), dz = sim.Units.Z[i].ToFloat() - (z + 0.5);
            Assert.True(Math.Sqrt(dx * dx + dz * dz) <= within,
                $"unit {id} ended at ({sim.Units.X[i]}, {sim.Units.Z[i]}), more than {within} from ({x}, {z})");
        }
    }

    [Fact]
    public void ColumnsKeepTheSurfaceAndTunnelLevels()
    {
        var w = TestWorlds.Flat();
        TestWorlds.Box(w, 20, Feet, 0, 24, Feet + 5, 63, BlockType.Stone); // a ridge 6 high
        var sim = TestWorlds.Sim(w);
        var g = sim.Walkable;
        Assert.True(g.Node(22, 10, Feet + 6) >= 0); // on top of the ridge
        Assert.Equal(-1, g.Node(22, 10, Feet));    // inside it
        sim.Schedule(0, new DigCommand(0, 22, Feet, 10));
        sim.Schedule(0, new DigCommand(0, 22, Feet + 1, 10));
        sim.Step();
        int top = g.Node(22, 10, Feet + 6), tunnel = g.Node(22, 10, Feet);
        Assert.True(top >= 0 && tunnel >= 0 && top != tunnel);
        Assert.Equal(new[] { Feet + 6, Feet }, new[] { g.Level(top), g.Level(tunnel) });
    }

    [Fact]
    public void EdgesClimbOneBlockButNotTwo()
    {
        var w = TestWorlds.Flat();
        w.Set(11, Feet, 10, BlockType.Stone);                    // one-block step east
        TestWorlds.Box(w, 10, Feet, 11, 10, Feet + 1, 11, BlockType.Stone); // two-block wall south
        var g = TestWorlds.Sim(w).Walkable;
        int from = g.Node(10, 10, Feet);
        int east = g.Step(from, 0), south = g.Step(from, 2);
        Assert.Equal(g.Node(11, 10, Feet + 1), east);
        Assert.Equal(-1, south);
        // Coming back down the step is a one-block drop.
        Assert.Equal(from, g.Step(east, 1));
    }

    [Fact]
    public void WalksAroundAWall()
    {
        var w = TestWorlds.Flat();
        TestWorlds.Box(w, 30, Feet, 0, 31, Feet + 2, 49, BlockType.Stone); // 3 high, open past z = 49
        var sim = TestWorlds.Sim(w);
        var ids = SpawnGroup(sim, 10, 10, 20);
        sim.Schedule(0, new MoveCommand(0, ids, 50, 20));
        var (ticks, maxZ, _) = RunUntilIdle(sim, ids, 1200);
        Assert.True(ticks < 1200, "units never arrived");
        Assert.True(maxZ >= 50, $"units should have gone around the end of the wall, furthest z was {maxZ}");
        AssertAllNear(sim, ids, 50, 20, 4);
    }

    [Fact]
    public void WalksThroughADugTunnel()
    {
        var w = TestWorlds.Flat();
        TestWorlds.Box(w, 28, Feet, 0, 34, Feet + 6, 63, BlockType.Stone); // ridge across the whole map
        var sim = TestWorlds.Sim(w);
        var ids = SpawnGroup(sim, 10, 10, 20);

        // No way through yet: the units see there is no path and go as near as they can, to the foot of the ridge.
        sim.Schedule(0, new MoveCommand(0, ids, 50, 20));
        var (giveUp, _, _) = RunUntilIdle(sim, ids, 600);
        Assert.True(giveUp < 600, "the units never stopped");
        foreach (int id in ids) Assert.InRange(sim.Units.X[sim.Units.IndexOf(id)].FloorToInt(), 22, 27);

        // Dig a tunnel two blocks high at z = 40 (off the straight line) and order again in the same tick.
        for (int x = 28; x <= 34; x++)
        {
            sim.Schedule(sim.Tick, new DigCommand(0, x, Feet, 40));
            sim.Schedule(sim.Tick, new DigCommand(0, x, Feet + 1, 40));
        }
        sim.Schedule(sim.Tick, new MoveCommand(0, ids, 50, 20));
        var (ticks, maxZ, _) = RunUntilIdle(sim, ids, 1200);
        Assert.True(ticks < 1200, "units never arrived");
        Assert.True(maxZ >= 40, $"units should have gone through the tunnel at z = 40, furthest z was {maxZ}");
        AssertAllNear(sim, ids, 50, 20, 4);
    }

    /// <summary>A river three blocks deep across the map, with stone bridges at the given z.</summary>
    private static VoxelWorld RiverWithBridges(params int[] bridgeZ)
    {
        var w = TestWorlds.Flat();
        TestWorlds.Box(w, 30, 1, 0, 33, 3, 63, BlockType.Water);
        foreach (int z in bridgeZ) TestWorlds.Box(w, 30, 3, z, 33, 3, z + 1, BlockType.Stone);
        return w;
    }

    [Fact]
    public void ReroutesWhenABridgeIsRemoved()
    {
        var sim = TestWorlds.Sim(RiverWithBridges(20, 55));
        var ids = SpawnGroup(sim, 10, 10, 18);
        sim.Schedule(0, new MoveCommand(0, ids, 50, 20));
        sim.Run(20);
        // The near bridge goes while the units are still on their way to it.
        foreach (int id in ids) Assert.True(sim.Units.X[sim.Units.IndexOf(id)].FloorToInt() < 25);
        for (int x = 30; x <= 33; x++)
        for (int z = 20; z <= 21; z++)
            sim.Schedule(sim.Tick, new DigCommand(1, x, 3, z));
        var (ticks, maxZ, _) = RunUntilIdle(sim, ids, 1600);
        Assert.True(ticks < 1600, "units never arrived");
        Assert.True(maxZ >= 55, $"units should have crossed at the far bridge, furthest z was {maxZ}");
        AssertAllNear(sim, ids, 50, 20, 4);
        // Nobody went into the deep water.
        foreach (int id in ids) Assert.Equal(Feet, sim.Units.Y[sim.Units.IndexOf(id)]);
    }

    [Fact]
    public void UsesTheNearBridgeWhileItStands()
    {
        var sim = TestWorlds.Sim(RiverWithBridges(20, 55));
        var ids = SpawnGroup(sim, 10, 10, 18);
        sim.Schedule(0, new MoveCommand(0, ids, 50, 20));
        var (ticks, maxZ, _) = RunUntilIdle(sim, ids, 1200);
        Assert.True(ticks < 1200, "units never arrived");
        Assert.True(maxZ < 40, $"the near bridge is shorter, but a unit went as far as z = {maxZ}");
        AssertAllNear(sim, ids, 50, 20, 4);
    }

    [Fact]
    public void UnitsWithNoWayThereGoAsNearAsTheyCan()
    {
        var w = TestWorlds.Flat();
        TestWorlds.Box(w, 30, Feet, 0, 31, Feet + 2, 63, BlockType.Stone); // wall across the whole map
        var sim = TestWorlds.Sim(w);
        var ids = SpawnGroup(sim, 5, 10, 20);
        sim.Schedule(0, new MoveCommand(0, ids, 50, 20));
        // The field covers everything reachable from the goal, so the units know at once there is no way, and
        // head for the nearest place they can get to instead: the foot of the wall, in line with the goal.
        int i = sim.Units.IndexOf(ids[0]);
        for (int t = 0; t < 10; t++) sim.Step();
        Assert.Equal(UnitOrder.Move, sim.Units.Order[i]);
        Assert.Equal((29, 20), (sim.Units.OrderX[i], sim.Units.OrderZ[i]));
        var (ticks, _, _) = RunUntilIdle(sim, ids, 600);
        Assert.True(ticks < 600, "the units never stopped");
        AssertAllNear(sim, ids, 29, 20, 3);
        // Once there they stop: they do not look again for somewhere nearer still.
        sim.Run(100);
        foreach (int id in ids) Assert.Equal(UnitOrder.Idle, sim.Units.Order[sim.Units.IndexOf(id)]);
    }

    [Fact]
    public void UnitsStuckInAPitGiveUpWithoutTheSearchCombingTheMap()
    {
        var w = TestWorlds.Flat(256, 16, 256);
        TestWorlds.Box(w, 100, 2, 100, 101, 3, 100, BlockType.Air); // two blocks deep: easy to fall in, no way out
        var sim = TestWorlds.Sim(w);
        int stuck = sim.SpawnUnit(0, "worker", 100, 100);
        Assert.Equal(2, sim.Units.Y[sim.Units.IndexOf(stuck)]);
        var group = SpawnGroup(sim, 5, 110, 100);
        sim.Schedule(0, new MoveCommand(0, group.Append(stuck).ToArray(), 200, 200));
        sim.Step();
        Assert.Equal(UnitOrder.Move, sim.Units.Order[sim.Units.IndexOf(stuck)]);
        int t = 1;
        for (; sim.Units.Order[sim.Units.IndexOf(stuck)] == UnitOrder.Move && t < 100; t++) sim.Step();
        Assert.True(t < 30, $"the unit in the pit took {t} ticks to give up");
        // Without the check, proving there is no way out means searching every node on the map.
        Assert.True(sim.Paths.Expansions < 256 * 256 / 2, $"searched {sim.Paths.Expansions} nodes");
        var (ticks, _, _) = RunUntilIdle(sim, group, 1600);
        Assert.True(ticks < 1600, "the others never arrived");
        AssertAllNear(sim, group, 200, 200, 4);
    }

    [Fact]
    public void FieldForADistantGroupCoversTheWayThereNotTheWholeMap()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(256, 16, 256));
        var ids = SpawnGroup(sim, 10, 20, 20);
        sim.Schedule(0, new MoveCommand(0, ids, 230, 230));
        var field = sim.Paths.Request(230, 230, Feet, 0);
        while (!field.Ready) sim.Step();
        Assert.True(sim.Paths.Expansions < 256 * 256 / 2, $"searched {sim.Paths.Expansions} nodes");
        Assert.Equal(FlowField.Unknown, field.Distance(sim.Walkable.Node(230, 20, Feet))); // the far corner
        var (ticks, _, _) = RunUntilIdle(sim, ids, 2000);
        Assert.True(ticks < 2000, "units never arrived");
        AssertAllNear(sim, ids, 230, 230, 4);
    }

    [Fact]
    public void OrdersToTheSameSpotShareAFieldThatIsDroppedAfterArrival()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat());
        var a = SpawnGroup(sim, 5, 10, 10);
        var b = SpawnGroup(sim, 5, 10, 40);
        sim.Schedule(0, new MoveCommand(0, a, 40, 25));
        sim.Schedule(0, new MoveCommand(0, b, 40, 25));
        sim.Step();
        Assert.Equal(1, sim.Paths.FieldCount);
        Assert.Equal(sim.Units.Field[sim.Units.IndexOf(a[0])], sim.Units.Field[sim.Units.IndexOf(b[0])]);
        RunUntilIdle(sim, a.Concat(b).ToArray(), 800);
        sim.Step();
        Assert.Equal(0, sim.Paths.FieldCount);
    }

    [Fact]
    public void TargetInWaterSnapsToTheShore()
    {
        var sim = TestWorlds.Sim(RiverWithBridges());
        var ids = SpawnGroup(sim, 1, 10, 20);
        sim.Schedule(0, new MoveCommand(0, ids, 31, 20)); // middle of the river
        RunUntilIdle(sim, ids, 600);
        int i = sim.Units.IndexOf(ids[0]);
        Assert.Equal(29, sim.Units.OrderX[i]);
        Assert.Equal(Feet, sim.Units.Y[i]);
        AssertAllNear(sim, ids, 29, 20, 1);
    }

    [Fact]
    public void TunnelLevelCanBeTheTarget()
    {
        // A tunnel under a hill: ordering to the tunnel's level (not the hilltop) leads inside.
        var w = TestWorlds.Flat();
        TestWorlds.Box(w, 28, Feet, 0, 40, Feet + 4, 63, BlockType.Stone);
        for (int x = 28; x <= 34; x++) { w.Set(x, Feet, 20, BlockType.Air); w.Set(x, Feet + 1, 20, BlockType.Air); }
        var sim = TestWorlds.Sim(w);
        var ids = SpawnGroup(sim, 1, 10, 20);
        sim.Schedule(0, new MoveCommand(0, ids, 33, 20, Feet));
        RunUntilIdle(sim, ids, 600);
        int i = sim.Units.IndexOf(ids[0]);
        Assert.Equal(Feet, sim.Units.Y[i]);
        AssertAllNear(sim, ids, 33, 20, 1);
    }
}
