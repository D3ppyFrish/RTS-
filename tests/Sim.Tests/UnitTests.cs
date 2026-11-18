using System;
using System.Linq;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

public class UnitTests
{
    private static int[] SpawnLine(Simulation sim, int owner, int count, int x, int z, string type = "worker") =>
        Enumerable.Range(0, count).Select(k => sim.SpawnUnit(owner, type, x + k % 8, z + k / 8)).ToArray();

    private static (Fix64 X, int Y, Fix64 Z) Pos(Simulation sim, int id)
    {
        int i = sim.Units.IndexOf(id);
        return (sim.Units.X[i], sim.Units.Y[i], sim.Units.Z[i]);
    }

    [Fact]
    public void IdsStayStableWhenOthersAreRemoved()
    {
        var store = new UnitStore();
        var ids = Enumerable.Range(0, 5).Select(k => store.Spawn(0, 0, Fix64.FromInt(k), 1, Fix64.Zero, 10)).ToArray();
        store.Remove(ids[1]);
        Assert.Equal(4, store.Count);
        Assert.False(store.Exists(ids[1]));
        foreach (var id in ids.Where(id => id != ids[1]))
        {
            int i = store.IndexOf(id);
            Assert.Equal(id, store.Id[i]);
        }
        Assert.Equal(Fix64.FromInt(4), store.X[store.IndexOf(ids[4])]);
        int fresh = store.Spawn(0, 0, Fix64.Zero, 1, Fix64.Zero, 10);
        Assert.DoesNotContain(fresh, ids);
    }

    [Fact]
    public void SpawnedUnitsStandOnTheGround()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(ground: 5));
        int id = sim.SpawnUnit(0, "worker", 10, 10);
        Assert.Equal(6, Pos(sim, id).Y);
    }

    [Fact]
    public void MoveReachesTargetOnFlatGround()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat());
        int id = sim.SpawnUnit(0, "worker", 5, 5);
        sim.Schedule(0, new MoveCommand(0, new[] { id }, 40, 30));
        sim.Run(20 * 12);
        var (x, _, z) = Pos(sim, id);
        Assert.Equal(40, x.FloorToInt());
        Assert.Equal(30, z.FloorToInt());
        Assert.Equal(UnitOrder.Idle, sim.Units.Order[sim.Units.IndexOf(id)]);
    }

    [Fact]
    public void SpeedMatchesDefinition()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat());
        int id = sim.SpawnUnit(0, "worker", 2, 10);
        sim.Schedule(0, new MoveCommand(0, new[] { id }, 60, 10));
        sim.Run(20); // one second at 4 blocks per second
        Assert.Equal(6, Pos(sim, id).X.FloorToInt());
    }

    [Fact]
    public void ClimbsOneBlockStepButNotTwo()
    {
        var w = TestWorlds.Flat(ground: 3);
        TestWorlds.Box(w, 20, 4, 0, 63, 4, 63, BlockType.Stone);   // a one-block step at x = 20
        TestWorlds.Box(w, 40, 5, 0, 41, 6, 63, BlockType.Stone);   // a two-block wall at x = 40
        var sim = TestWorlds.Sim(w);
        int id = sim.SpawnUnit(0, "worker", 5, 10);
        sim.Schedule(0, new MoveCommand(0, new[] { id }, 35, 10));
        sim.Run(20 * 20);
        var (x, y, _) = Pos(sim, id);
        Assert.Equal(35, x.FloorToInt());
        Assert.Equal(5, y); // on top of the step
        // Nothing gets it over the two-block wall.
        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { id }, 60, 10));
        sim.Run(20 * 20);
        Assert.True(Pos(sim, id).X.FloorToInt() < 40, $"unit at x = {Pos(sim, id).X}");
    }

    [Fact]
    public void DropsThreeBlocksButNotFour()
    {
        var w = TestWorlds.Flat(ground: 8);
        TestWorlds.Box(w, 20, 6, 0, 29, 8, 63, BlockType.Air); // a pit three deep at x = 20..29 (floor at y = 5)
        TestWorlds.Box(w, 30, 5, 0, 63, 8, 63, BlockType.Air); // beyond it, ground four lower than the pit floor? no: floor at 4
        var sim = TestWorlds.Sim(w);
        int id = sim.SpawnUnit(0, "worker", 5, 10);
        sim.Schedule(0, new MoveCommand(0, new[] { id }, 25, 10));
        sim.Run(20 * 10);
        Assert.Equal(6, Pos(sim, id).Y); // dropped three, from 9 to 6
        var climbBack = new MoveCommand(0, new[] { id }, 5, 10);
        sim.Schedule(sim.Tick, climbBack);
        sim.Run(20 * 10);
        Assert.Equal(6, Pos(sim, id).Y); // a three-block wall is too high to climb back up
        Assert.True(Pos(sim, id).X.FloorToInt() >= 20);
    }

    [Fact]
    public void FallsWhenTheGroundIsDugAway()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(ground: 6));
        int id = sim.SpawnUnit(0, "worker", 10, 10);
        for (int y = 3; y <= 6; y++) sim.Schedule(0, new DigCommand(0, 10, y, 10));
        sim.Run(10);
        Assert.Equal(3, Pos(sim, id).Y);
    }

    [Fact]
    public void ClimbsOutWhenABlockIsPlacedOnIt()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(ground: 3));
        int id = sim.SpawnUnit(0, "worker", 10, 10);
        sim.Schedule(0, new PlaceCommand(0, 10, 4, 10, BlockType.Stone));
        sim.Run(2);
        Assert.Equal(5, Pos(sim, id).Y);
    }

    [Fact]
    public void OtherPlayersCannotOrderYourUnits()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat());
        int id = sim.SpawnUnit(0, "worker", 5, 5);
        sim.Schedule(0, new MoveCommand(1, new[] { id }, 40, 40));
        sim.Run(40);
        Assert.Equal(5, Pos(sim, id).X.FloorToInt());
    }

    [Fact]
    public void CrowdsSpreadOutAndSettle()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(96, 16, 96));
        var ids = SpawnLine(sim, 0, 120, 10, 10);
        sim.Schedule(0, new MoveCommand(0, ids, 60, 60));
        sim.Run(20 * 40);
        var u = sim.Units;
        Assert.All(ids, id => Assert.Equal(UnitOrder.Idle, u.Order[u.IndexOf(id)]));
        // Everyone ended up near the target and nobody is stacked on anyone else.
        for (int a = 0; a < u.Count; a++)
        {
            Fix64 dx = u.X[a] - Fix64.FromInt(60), dz = u.Z[a] - Fix64.FromInt(60);
            Assert.True(Fix64.Sqrt(dx * dx + dz * dz) < Fix64.FromInt(14), $"unit {u.Id[a]} is far from the goal");
            for (int b = a + 1; b < u.Count; b++)
            {
                Fix64 ex = u.X[a] - u.X[b], ez = u.Z[a] - u.Z[b];
                Assert.True(ex * ex + ez * ez > Fix64.FromRatio(1, 25), $"units {u.Id[a]} and {u.Id[b]} overlap");
            }
        }
    }

    [Fact]
    public void StopCancelsTheOrder()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat());
        int id = sim.SpawnUnit(0, "worker", 5, 5);
        sim.Schedule(0, new MoveCommand(0, new[] { id }, 60, 5));
        sim.Schedule(20, new StopCommand(0, new[] { id }));
        sim.Run(100);
        Assert.InRange(Pos(sim, id).X.FloorToInt(), 8, 10);
    }

    private static ulong RunBusyMatch(ulong seed)
    {
        var sim = new Simulation(seed, 96, 32, 96);
        var rng = new DeterministicRandom(seed);
        var ids = Enumerable.Range(0, 300).Select(k => sim.SpawnUnit(k % 2, "worker", 20 + k % 20, 20 + k / 20)).Where(id => id > 0).ToArray();
        for (int t = 0; t < 600; t += 40)
        {
            var mine = ids.Where((_, k) => k % 2 == 0 && rng.Range(0, 3) > 0).ToArray();
            sim.Schedule(t, new MoveCommand(0, mine, rng.Range(5, 90), rng.Range(5, 90)));
            var theirs = ids.Where((_, k) => k % 2 == 1).ToArray();
            sim.Schedule(t + 7, new MoveCommand(1, theirs, rng.Range(5, 90), rng.Range(5, 90)));
            int x = rng.Range(0, 96), z = rng.Range(0, 96);
            sim.Schedule(t + 3, new DigCommand(0, x, sim.World.SurfaceHeight(x, z), z));
        }
        sim.Run(600);
        return sim.StateHash();
    }

    [Fact]
    public void SpawnsUnderTreesNotOnTheCanopy()
    {
        var w = TestWorlds.Flat();
        // A tree on grass at level 3: trunk at (10, 10), leaves canopy from three blocks above the ground.
        for (int y = 4; y <= 8; y++) w.Set(10, y, 10, BlockType.Wood);
        for (int x = 8; x <= 12; x++)
        for (int z = 8; z <= 12; z++)
        for (int y = 7; y <= 9; y++)
            if (w.Get(x, y, z) == BlockType.Air) w.Set(x, y, z, BlockType.Leaves);
        var sim = TestWorlds.Sim(w);
        int id = sim.SpawnUnit(0, "worker", 11, 10);
        Assert.Equal(4, Pos(sim, id).Y);
        Assert.Equal(-1, sim.SpawnUnit(0, "worker", 10, 10)); // inside the trunk: nowhere to stand
    }

    [Fact]
    public void EveryStartAreaGetsItsStartingUnits()
    {
        var sim = new Simulation(11, 128, 32, 128);
        var ids = sim.SpawnStartingUnits(30);
        Assert.Equal(30 * sim.Map.Starts.Count, ids.Count);
        foreach (var start in sim.Map.Starts)
        {
            var mine = ids.Where(id => sim.Units.Owner[sim.Units.IndexOf(id)] == start.Player).ToArray();
            Assert.Equal(30, mine.Length);
            // Every unit type matches start with is represented (forms are not), and everyone stands on solid ground near the start.
            Assert.Equal(sim.Content.StartUnits.Count, mine.Select(id => sim.Units.Def[sim.Units.IndexOf(id)]).Distinct().Count());
            Assert.DoesNotContain(mine, id => sim.Content.Units[sim.Units.Def[sim.Units.IndexOf(id)]].IsForm);
            foreach (int id in mine)
            {
                var (x, y, z) = Pos(sim, id);
                Assert.True(Walk.CanStand(sim.World, x.FloorToInt(), y, z.FloorToInt()));
                Assert.InRange(x.FloorToInt() - start.X, -5, 4);
                Assert.InRange(z.FloorToInt() - start.Z, 4, 12);
            }
        }
    }

    [Fact]
    public void UnitMovementIsDeterministic()
    {
        Assert.Equal(RunBusyMatch(5), RunBusyMatch(5));
        Assert.NotEqual(RunBusyMatch(5), RunBusyMatch(6));
    }

    [Fact]
    public void MoveAndStopRoundTripThroughTheLog()
    {
        var log = new CommandLog { Seed = 1, SizeX = 32, SizeY = 16, SizeZ = 32, Ticks = 10 };
        log.Add(1, new MoveCommand(0, new[] { 3, 4, 5 }, 10, 12));
        log.Add(2, new StopCommand(1, new[] { 7 }));
        log.Add(3, new MoveCommand(0, Array.Empty<int>(), 1, 1));
        var copy = CommandLog.Parse(log.ToString());
        Assert.Equal(log.Commands, copy.Commands);
    }
}
