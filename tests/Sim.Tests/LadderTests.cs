using System;
using System.Linq;
using Rts.Sim.Economy;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

public class LadderTests
{
    private const int Tps = Simulation.TicksPerSecond;
    private const int Ground = 3; // units stand at 4
    private const int Foot = Ground + 1;
    private const int CliffX = 40; // a cliff from here east, 5 levels up: units stand at 9 on top
    private const int CliffTop = Ground + 1 + 5;
    private const int Z = 32;

    /// <summary>Flat ground with a cliff 5 levels high across the map from x = 40 east: nobody climbs it.</summary>
    private static Simulation Cliff()
    {
        var w = TestWorlds.Flat(64, 16, 64, Ground);
        TestWorlds.Box(w, CliffX, Ground + 1, 0, 63, CliffTop - 1, 63, BlockType.Stone);
        return TestWorlds.Sim(w);
    }

    /// <summary>Flat ground with player 0's wooden wall (4 high) right across the map at x = 40, and no way round it.</summary>
    private static Simulation Walled()
    {
        var w = TestWorlds.Flat(64, 16, 64, Ground);
        var sim = TestWorlds.Sim(w);
        for (int z = 1; z < 63; z++) Assert.NotNull(sim.Economy.PlaceFinished(0, sim.Content.Building("wooden_wall"), 40, z));
        foreach (int z in new[] { 0, 63 }) TestWorlds.Box(w, 40, Ground + 1, z, 40, 12, z, BlockType.Bedrock);
        return sim;
    }

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);
    private static int X(Simulation sim, int id) => sim.Units.X[Index(sim, id)].FloorToInt();
    private static int Y(Simulation sim, int id) => sim.Units.Y[Index(sim, id)];

    private static void RunUntil(Simulation sim, Func<bool> done, int maxTicks, string what)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return;
            sim.Step();
        }
        Assert.Fail($"Not done after {maxTicks} ticks: {what}");
    }

    private static Building Store(Simulation sim, int owner, int wood, int x = 10, int z = 10)
    {
        var store = sim.Economy.PlaceFinished(owner, sim.Content.Building("storehouse"), x, z)!;
        store.Stock[(int)Good.Wood] = wood;
        return store;
    }

    private static int Rungs(Simulation sim, int x, int z) =>
        Enumerable.Range(0, sim.World.SizeY).Count(y => sim.World.Get(x, y, z) == BlockType.Ladder);

    /// <summary>Raises a ladder for player 1 at x = 39 against what stands at x = 40, and waits until it is up.</summary>
    private static Ladder Raise(Simulation sim, int player, params int[] builders)
    {
        sim.Schedule(sim.Tick, new LadderCommand(player, builders, 39, Z, CliffX, Z));
        sim.Step();
        var ladder = Assert.Single(sim.Economy.Ladders);
        RunUntil(sim, () => ladder.Done, 60 * Tps, "the ladder to go up");
        return ladder;
    }

    [Fact]
    public void WorkersRaiseALadderUpACliffAndUnitsClimbItBothWays()
    {
        var sim = Cliff();
        var store = Store(sim, 0, wood: 100);

        // Before: a spearman cannot get up the cliff.
        int spearman = sim.SpawnUnit(0, "spearman", 25, Z);
        sim.Schedule(0, new MoveCommand(0, new[] { spearman }, 50, Z));
        sim.Run(20 * Tps);
        Assert.True(X(sim, spearman) < CliffX, "nobody climbs a cliff 5 levels high");

        // The plan: the foot at x = 39, leaning on the cliff beside it, up to its top.
        Assert.Equal(CliffTop, sim.Economy.PlanLadder(39, Z, 50, Z + 3, out int foot));
        Assert.Equal(Foot, foot);
        Assert.Equal((CliffX, Z), EconomySystem.LadderFace(39, Z, 50, Z + 3));

        var workers = new[] { sim.SpawnUnit(0, "worker", 33, Z - 1), sim.SpawnUnit(0, "sapper", 33, Z + 1) };
        sim.Schedule(sim.Tick, new LadderCommand(0, workers, 39, Z, CliffX, Z));
        sim.Step();
        var ladder = Assert.Single(sim.Economy.Ladders);
        Assert.Equal(5, ladder.Rungs);
        Assert.All(workers, id => Assert.Equal(UnitJob.Ladder, sim.Units.Job[Index(sim, id)]));

        // It goes up a rung at a time from the bottom, the wood going as the rungs go in.
        RunUntil(sim, () =>
        {
            int rungs = Rungs(sim, 39, Z);
            Assert.Equal(ladder.Built, rungs);
            for (int y = Foot; y < Foot + rungs; y++) Assert.Equal(BlockType.Ladder, sim.World.Get(39, y, Z));
            Assert.Equal(100 - rungs * EconomySystem.WoodPerRung, store.Stock[(int)Good.Wood]);
            return ladder.Done;
        }, 60 * Tps, "the ladder to go up");
        Assert.Equal(5 * EconomySystem.WoodPerRung, sim.Economy.Player(0).Spent[(int)Good.Wood]);
        sim.Run(Tps);
        Assert.All(workers, id => Assert.Equal(UnitJob.None, sim.Units.Job[Index(sim, id)]));
        Assert.Single(sim.Economy.Ladders); // it stays up

        // A rung is a block of its own, the last there is, and not solid.
        Assert.Equal(BlockType.Ladder, BlockInfo.Last);
        Assert.False(BlockInfo.IsSolid(BlockType.Ladder));
        // Nobody stands on a rung: the foot is the only level in its column, and it is no hazard to walk past.
        Assert.Equal(Foot, Walk.GroundLevel(sim.World, 39, Z));
        Assert.True(Walk.ClimbsLadder(sim.World, 39, Z, Foot, CliffX, Z, out int up) && up == CliffTop);
        Assert.False(Walk.ClimbsLadder(sim.World, 39, Z, Foot, CliffX, Z + 1, out _), "never on a diagonal");

        // After: the spearman climbs it onto the cliff top, and back down.
        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { spearman }, 50, Z));
        RunUntil(sim, () => X(sim, spearman) >= 50, 30 * Tps, "the spearman to climb the ladder");
        Assert.Equal(CliffTop, Y(sim, spearman));
        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { spearman }, 25, Z));
        RunUntil(sim, () => X(sim, spearman) <= 25, 30 * Tps, "the spearman to climb back down");
        Assert.Equal(Foot, Y(sim, spearman));
    }

    [Fact]
    public void ALadderAgainstAWallLetsTheEnemyOverIt()
    {
        var sim = Walled();
        Store(sim, 1, wood: 100);
        int spearman = sim.SpawnUnit(1, "spearman", 25, Z);
        sim.Schedule(0, new MoveCommand(1, new[] { spearman }, 50, Z));
        sim.Run(20 * Tps);
        Assert.True(X(sim, spearman) < 40, "the wall keeps the enemy out");

        Raise(sim, 1, sim.SpawnUnit(1, "worker", 33, Z - 1));
        Assert.Equal(4, Rungs(sim, 39, Z));

        // Up the ladder onto the wall, then down the far side (see Climbing down under Moving).
        sim.Schedule(sim.Tick, new MoveCommand(1, new[] { spearman }, 40, Z));
        RunUntil(sim, () => X(sim, spearman) == 40 && Y(sim, spearman) == Foot + 4, 30 * Tps, "the spearman to climb onto the wall");
        sim.Schedule(sim.Tick, new MoveCommand(1, new[] { spearman }, 50, Z));
        for (int t = 0; t < 60 * Tps && !(X(sim, spearman) > 40 && Y(sim, spearman) == Foot); t++) sim.Step();
        Assert.True(X(sim, spearman) > 40 && Y(sim, spearman) == Foot,
            $"the spearman to get over the wall: at {X(sim, spearman)},{Y(sim, spearman)}");
    }

    [Fact]
    public void AnEnemyOnTheTopThrowsTheLadderDownUnlessItIsHeld()
    {
        var sim = Walled();
        Store(sim, 1, wood: 100);
        var ladder = Raise(sim, 1, sim.SpawnUnit(1, "worker", 33, Z - 1));

        // One of the attackers holds the top: a defender beside it does not throw it down.
        int attacker = sim.SpawnUnit(1, "worker", 40, Z + 1);
        int defender = sim.SpawnUnit(0, "worker", 40, Z - 1);
        Assert.Equal(ladder.Top, Y(sim, attacker));
        Assert.Equal(ladder.Top, Y(sim, defender));
        sim.Run(EconomySystem.LadderPushTicks + Tps);
        Assert.Single(sim.Economy.Ladders);
        Assert.Equal(0, ladder.Push);

        // With the top the defender's alone, it throws the ladder down: the rungs are gone and nobody climbs.
        sim.Units.Remove(attacker);
        sim.Run(EconomySystem.LadderPushTicks - 2);
        Assert.Single(sim.Economy.Ladders);
        sim.Run(3);
        Assert.Empty(sim.Economy.Ladders);
        Assert.Equal(0, Rungs(sim, 39, Z));
        Assert.False(Walk.ClimbsLadder(sim.World, 39, Z, Foot, CliffX, Z, out _));
    }

    [Fact]
    public void ALadderFallsWhenTheTopItLeansOnIsGone()
    {
        var sim = Cliff();
        Store(sim, 0, wood: 100);
        Raise(sim, 0, sim.SpawnUnit(0, "worker", 33, Z));
        Assert.Equal(5, Rungs(sim, 39, Z));
        // The cliff's edge above the ladder is dug out: nothing to lean on at the top.
        sim.World.Set(CliffX, CliffTop - 1, Z, BlockType.Air);
        sim.Run(2);
        Assert.Empty(sim.Economy.Ladders);
        Assert.Equal(0, Rungs(sim, 39, Z));
    }

    [Fact]
    public void ALadderNeedsAFaceRoomWoodAndBuilders()
    {
        var sim = Cliff();
        // Flat ground: nothing to lean on.
        Assert.Equal(-1, sim.Economy.PlanLadder(10, Z, 11, Z, out _));
        // A face only a climb up, or one too high.
        TestWorlds.Box(sim.World, 20, Foot, Z, 20, Foot, Z, BlockType.Stone);
        Assert.Equal(-1, sim.Economy.PlanLadder(19, Z, 20, Z, out _));
        TestWorlds.Box(sim.World, 22, Foot, Z, 22, Foot + Walk.MaxLadder, Z, BlockType.Stone);
        Assert.Equal(-1, sim.Economy.PlanLadder(21, Z, 22, Z, out _));
        // Its own column for a foot, not one that is itself the face.
        Assert.Equal(-1, sim.Economy.PlanLadder(39, Z, 39, Z, out _));
        // A trunk over the foot.
        sim.World.Set(39, CliffTop + 1, Z + 4, BlockType.Wood);
        Assert.Equal(-1, sim.Economy.PlanLadder(39, Z + 4, CliffX, Z + 4, out _));

        // Soldiers do not raise ladders.
        Store(sim, 0, wood: 3);
        int spearman = sim.SpawnUnit(0, "spearman", 33, Z);
        sim.Schedule(0, new LadderCommand(0, new[] { spearman }, 39, Z, CliffX, Z));
        sim.Step();
        Assert.Empty(sim.Economy.Ladders);

        // The wood runs out after a rung: the worker stops, and the ladder nobody raises is taken down.
        int worker = sim.SpawnUnit(0, "worker", 33, Z);
        sim.Schedule(sim.Tick, new LadderCommand(0, new[] { worker }, 39, Z, CliffX, Z));
        sim.Step();
        RunUntil(sim, () => sim.Units.Job[Index(sim, worker)] == UnitJob.None, 60 * Tps, "the worker to stop");
        Assert.Equal(EconomySystem.WoodPerRung, sim.Economy.Player(0).Spent[(int)Good.Wood]);
        sim.Run(2 * Tps);
        Assert.Empty(sim.Economy.Ladders);
        Assert.Equal(0, Rungs(sim, 39, Z));
    }

    [Fact]
    public void ALadderCommandRoundTripsAndTheSameInputsGiveTheSameState()
    {
        var c = new LadderCommand(1, new[] { 4, 9 }, 39, Z, CliffX, Z);
        var log = CommandLog.Parse("rts-log 1\nseed 1\nworld 64 16 64\nticks 10\n7 ladder 1 39 32 40 32 4,9\n");
        var back = log.Commands.Single();
        Assert.Contains("7 ladder 1 39 32 40 32 4,9", log.ToString());
        Assert.Equal(7, back.Tick);
        Assert.Equal(c, back.Command);

        ulong Run()
        {
            var sim = Cliff();
            Store(sim, 0, wood: 100);
            var workers = new[] { sim.SpawnUnit(0, "worker", 33, Z - 1), sim.SpawnUnit(0, "worker", 33, Z + 1) };
            int spearman = sim.SpawnUnit(0, "spearman", 30, Z);
            sim.Schedule(5, new LadderCommand(0, workers, 39, Z, CliffX, Z));
            sim.Schedule(15 * Tps, new MoveCommand(0, new[] { spearman }, 50, Z));
            sim.Run(30 * Tps);
            return sim.StateHash();
        }
        Assert.Equal(Run(), Run());
    }
}
