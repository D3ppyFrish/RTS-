using System;
using System.Linq;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

public class FormationTests
{
    private static (Fix64 X, Fix64 Z) Pos(Simulation sim, int id)
    {
        int i = sim.Units.IndexOf(id);
        return (sim.Units.X[i], sim.Units.Z[i]);
    }

    private static bool AllIdle(Simulation sim, int[] ids) =>
        ids.All(id => sim.Units.Order[sim.Units.IndexOf(id)] == UnitOrder.Idle);

    private static void RunUntilIdle(Simulation sim, int[] ids, int maxTicks)
    {
        for (int t = 0; t < maxTicks && !(t > 5 && AllIdle(sim, ids)); t++) sim.Step();
    }

    private static Fix64 Dist(Fix64 x1, Fix64 z1, Fix64 x2, Fix64 z2) =>
        Fix64.Sqrt((x1 - x2) * (x1 - x2) + (z1 - z2) * (z1 - z2));

    [Fact]
    public void PlainRightClickKeepsTheGroupsShape()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(96, 16, 96));
        // A 4 x 2 block with gaps of two blocks, and one unit off to the side.
        var ids = Enumerable.Range(0, 8).Select(k => sim.SpawnUnit(0, "spearman", 10 + 2 * (k % 4), 10 + 2 * (k / 4))).ToList();
        ids.Add(sim.SpawnUnit(0, "spearman", 22, 12));
        var start = ids.Select(id => Pos(sim, id)).ToArray();
        Fix64 cx = start.Aggregate(Fix64.Zero, (s, p) => s + p.X) / start.Length;
        Fix64 cz = start.Aggregate(Fix64.Zero, (s, p) => s + p.Z) / start.Length;

        sim.Schedule(0, new FormationMoveCommand(0, ids.ToArray(), 60, 70, 60, 70));
        // On the way too: halfway there, every unit is where it stood relative to the first one.
        sim.Run(Simulation.TicksPerSecond * 6);
        var (fx, fz) = Pos(sim, ids[0]);
        Assert.True(Dist(fx, fz, start[0].X, start[0].Z) > Fix64.FromInt(15), $"the group did not get going: {fx},{fz} from {start[0].X},{start[0].Z}");
        for (int k = 1; k < ids.Count; k++)
        {
            var (x, z) = Pos(sim, ids[k]);
            Assert.True(Dist(x - fx, z - fz, start[k].X - start[0].X, start[k].Z - start[0].Z) < Fix64.One,
                $"unit {k} lost its place on the way");
        }
        RunUntilIdle(sim, ids.ToArray(), 20 * 40);
        Assert.True(AllIdle(sim, ids.ToArray()));

        // Each unit ends where it stood, moved by the same amount as the group's middle.
        Fix64 mx = Fix64.FromInt(60) + Fix64.Half - cx, mz = Fix64.FromInt(70) + Fix64.Half - cz;
        for (int k = 0; k < ids.Count; k++)
        {
            var (x, z) = Pos(sim, ids[k]);
            Assert.True(Dist(x, z, start[k].X + mx, start[k].Z + mz) < Fix64.FromRatio(1, 2),
                $"unit {k} at ({x}, {z}), its place is ({start[k].X + mx}, {start[k].Z + mz})");
        }
    }

    [Fact]
    public void PlainMoveStillBunchesAtThePoint()
    {
        // The old move order (the computer player's) still goes to one point, for comparison.
        var sim = TestWorlds.Sim(TestWorlds.Flat(96, 16, 96));
        var ids = Enumerable.Range(0, 8).Select(k => sim.SpawnUnit(0, "spearman", 10 + 3 * (k % 4), 10 + 3 * (k / 4))).ToArray();
        sim.Schedule(0, new MoveCommand(0, ids, 60, 70));
        RunUntilIdle(sim, ids, 20 * 40);
        foreach (var id in ids)
        {
            var (x, z) = Pos(sim, id);
            Assert.True(Dist(x, z, Fix64.FromInt(60), Fix64.FromInt(70)) < Fix64.FromInt(3));
        }
    }

    [Fact]
    public void DraggedLinePlacesTheUnitsAlongItInOrder()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(96, 16, 96));
        // Six spearmen in a clump south of the line (smaller z), in a jumbled order.
        int[] xs = { 30, 27, 33, 28, 31, 29 }, zs = { 12, 10, 11, 13, 10, 11 };
        var ids = Enumerable.Range(0, 6).Select(k => sim.SpawnUnit(0, "spearman", xs[k], zs[k])).ToArray();

        // A line from west to east at z = 40, ten blocks long.
        sim.Schedule(0, new FormationMoveCommand(0, ids, 25, 40, 35, 40));
        RunUntilIdle(sim, ids, 20 * 30);
        Assert.True(AllIdle(sim, ids));

        var ends = ids.Select(id => Pos(sim, id)).ToArray();
        // All on the line, spread along all of it, two blocks apart.
        foreach (var (x, z) in ends)
        {
            Assert.True(Fix64.Abs(z - Fix64.FromRatio(81, 2)) < Fix64.FromRatio(1, 2), $"off the line: z = {z}");
            Assert.InRange(x.FloorToInt(), 25, 35);
        }
        var byX = ends.OrderBy(p => p.X.Raw).ToArray();
        Assert.True(byX[0].X < Fix64.FromInt(26) && byX[^1].X > Fix64.FromInt(35));
        for (int k = 1; k < byX.Length; k++)
            Assert.True(byX[k].X - byX[k - 1].X > Fix64.FromRatio(3, 2), "units bunched up on the line");
        // In order: west to east as they stood (ties by id), so nobody crosses.
        var westToEast = Enumerable.Range(0, 6).OrderBy(k => xs[k]).ThenBy(k => ids[k]).ToArray();
        for (int k = 1; k < westToEast.Length; k++)
            Assert.True(ends[westToEast[k]].X > ends[westToEast[k - 1]].X);
        // Facing forward from the line, away from where they came from (north, +z).
        int i0 = sim.Units.IndexOf(ids[0]);
        Assert.Equal(Fix64.Zero, sim.Units.FaceX[i0]);
        Assert.Equal(Fix64.One, sim.Units.FaceZ[i0]);
    }

    [Fact]
    public void MeleeGoInFrontAndRangedBehind()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(96, 16, 96));
        var archers = Enumerable.Range(0, 3).Select(k => sim.SpawnUnit(0, "archer", 30 + k, 60)).ToArray();   // ahead of the others
        var spears = Enumerable.Range(0, 3).Select(k => sim.SpawnUnit(0, "spearman", 30 + k, 56)).ToArray();
        var all = archers.Concat(spears).ToArray();

        // A short line east of them, three units long, facing east.
        sim.Schedule(0, new FormationMoveCommand(0, all, 60, 57, 60, 59));
        RunUntilIdle(sim, all, 20 * 30);
        Assert.True(AllIdle(sim, all));
        foreach (int s in spears)
        {
            Assert.Equal(60, Pos(sim, s).X.FloorToInt());
            foreach (int a in archers) Assert.True(Pos(sim, a).X < Pos(sim, s).X - Fix64.FromRatio(1, 2), "an archer is not behind the spearmen");
        }
        foreach (int a in archers) Assert.Equal(59, Pos(sim, a).X.FloorToInt());
    }

    [Fact]
    public void FormationMovesAtTheSlowestUnitsSpeed()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(96, 16, 96));
        int rider = sim.SpawnUnit(0, "rider", 10, 20);       // 8 blocks a second
        int spear = sim.SpawnUnit(0, "spearman", 10, 22);    // 4 blocks a second
        sim.Schedule(0, new FormationMoveCommand(0, new[] { rider, spear }, 70, 21, 70, 21));
        sim.Run(Simulation.TicksPerSecond * 5);
        var (rx, _) = Pos(sim, rider);
        var (sx, _) = Pos(sim, spear);
        // Five seconds at 4 blocks a second: about 20 blocks, side by side.
        Assert.InRange(rx.FloorToInt(), 27, 32);
        Assert.True(Fix64.Abs(rx - sx) < Fix64.One, $"the line broke: rider at {rx}, spearman at {sx}");
    }

    [Fact]
    public void ScatteredGroupFormsUpInABlock()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(96, 16, 96));
        var ids = new[]
        {
            sim.SpawnUnit(0, "spearman", 5, 5), sim.SpawnUnit(0, "spearman", 80, 5),
            sim.SpawnUnit(0, "spearman", 5, 80), sim.SpawnUnit(0, "spearman", 80, 80),
        };
        sim.Schedule(0, new FormationMoveCommand(0, ids, 45, 45, 45, 45));
        RunUntilIdle(sim, ids, 20 * 40);
        foreach (var id in ids)
        {
            var (x, z) = Pos(sim, id);
            Assert.True(Dist(x, z, Fix64.FromRatio(91, 2), Fix64.FromRatio(91, 2)) < Fix64.FromInt(3), $"({x}, {z}) is not in the block");
        }
    }

    [Fact]
    public void FormationKeepsItsShapeAroundAWall()
    {
        var w = TestWorlds.Flat(96, 16, 96);
        // A wall across the way with a gap in the middle.
        TestWorlds.Box(w, 40, 3, 0, 41, 6, 43, BlockType.Stone);
        TestWorlds.Box(w, 40, 3, 52, 41, 6, 95, BlockType.Stone);
        var sim = TestWorlds.Sim(w);
        var ids = Enumerable.Range(0, 6).Select(k => sim.SpawnUnit(0, "spearman", 20 + k % 3, 40 + 4 * (k / 3))).ToArray();
        sim.Schedule(0, new FormationMoveCommand(0, ids, 70, 48, 70, 54));
        RunUntilIdle(sim, ids, 20 * 60);
        Assert.True(AllIdle(sim, ids));
        foreach (var id in ids)
        {
            var (x, z) = Pos(sim, id);
            Assert.InRange(x.FloorToInt(), 68, 71);
            Assert.InRange(z.FloorToInt(), 47, 55);
        }
    }

    [Fact]
    public void AttackFormationFightsOnTheWayThenTakesItsPlace()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(96, 16, 96));
        var ids = Enumerable.Range(0, 4).Select(k => sim.SpawnUnit(0, "spearman", 10 + k, 30)).ToArray();
        int enemy = sim.SpawnUnit(1, "worker", 30, 31);
        sim.Schedule(0, new FormationMoveCommand(0, ids, 50, 28, 50, 34, Attack: true));
        // Units stand still while they fight, so first the worker has to fall.
        for (int t = 0; t < 20 * 30 && sim.Units.Exists(enemy); t++) sim.Step();
        Assert.False(sim.Units.Exists(enemy));
        RunUntilIdle(sim, ids, 20 * 60);
        foreach (var id in ids) Assert.InRange(Pos(sim, id).X.FloorToInt(), 49, 51);
    }

    [Fact]
    public void AnotherOrderEndsTheFormation()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(96, 16, 96));
        var ids = Enumerable.Range(0, 3).Select(k => sim.SpawnUnit(0, "spearman", 10 + k, 10)).ToArray();
        sim.Schedule(0, new FormationMoveCommand(0, ids, 30, 30, 34, 30));
        sim.Schedule(10, new MoveCommand(0, ids, 50, 50));
        sim.Run(11);
        foreach (var id in ids)
        {
            int i = sim.Units.IndexOf(id);
            Assert.False(sim.Units.Formation[i]);
            Assert.Equal(Fix64.Zero, sim.Units.SpeedCap[i]);
        }
    }

    [Fact]
    public void FormationCommandsSurviveTheLog()
    {
        var log = new CommandLog { Seed = 1, SizeX = 64, SizeY = 16, SizeZ = 64 };
        log.Add(3, new FormationMoveCommand(0, new[] { 1, 2, 3 }, 10, 11, 20, 21));
        log.Add(4, new FormationMoveCommand(1, new[] { 4 }, 5, 6, 5, 6, Attack: true, Y: 7));
        var back = CommandLog.Parse(log.ToString());
        Assert.Equal(log.Commands, back.Commands);
    }

    [Fact]
    public void FormationMovesAreDeterministic()
    {
        ulong Run()
        {
            var sim = TestWorlds.Sim(TestWorlds.Flat(96, 16, 96));
            var ids = Enumerable.Range(0, 12).Select(k => sim.SpawnUnit(0, k % 3 == 0 ? "archer" : "spearman", 10 + k % 4, 10 + k / 4)).ToArray();
            sim.Schedule(0, new FormationMoveCommand(0, ids, 40, 60, 60, 50));
            sim.Schedule(200, new FormationMoveCommand(0, ids, 20, 20, 20, 20));
            sim.Run(500);
            return sim.StateHash();
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void LinePlanSpreadsRanksAndKeepsOrder()
    {
        // Pure geometry: eight melee and four ranged along a three-block line make two melee ranks of four
        // and a ranged rank behind them.
        var members = Enumerable.Range(0, 12).Select(k => new Formations.Member(k + 1, Fix64.FromInt(k), Fix64.Zero,
            k < 8 ? FormationRank.Melee : FormationRank.Ranged)).ToArray();
        var slots = Formations.Line(members, Fix64.FromInt(0), Fix64.FromInt(10), Fix64.FromInt(3), Fix64.FromInt(10), out var fx, out var fz);
        Assert.Equal((Fix64.Zero, Fix64.One), (fx, fz));
        var rows = slots.GroupBy(s => s.Z.Raw).OrderByDescending(g => g.Key).ToArray();
        Assert.Equal(new[] { 4, 4, 4 }, rows.Select(r => r.Count()).ToArray());
        Assert.Equal(Fix64.FromInt(10), Fix64.FromRaw(rows[0].Key));
        Assert.All(rows[2], s => Assert.True(s.Id > 8));
        foreach (var row in rows)
            Assert.Equal(new[] { 0, 1, 2, 3 }, row.Select(s => s.X.FloorToInt()).ToArray());
    }
}
