using System;
using Rts.Sim.Economy;
using Rts.Sim.Replay;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Fog of war: what each player sees (issue #39).</summary>
public class VisionTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Tps = Simulation.TicksPerSecond;

    private static Simulation Flat(bool fog = true)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(96, 16, 96, Ground));
        sim.Fog = fog;
        return sim;
    }

    private static Building Place(Simulation sim, int owner, string def, int x, int z) =>
        sim.Economy.PlaceFinished(owner, sim.Content.Building(def), x, z) ?? throw new InvalidOperationException($"{def} does not fit");

    [Fact]
    public void WithoutFogEveryoneSeesEverything()
    {
        var sim = Flat(fog: false);
        sim.SpawnUnit(0, "spearman", 20, 20);
        sim.Run(VisionSystem.UpdateTicks);
        Assert.Equal(Sight.Visible, sim.Vision.At(0, 90, 90));
        Assert.Equal(Sight.Visible, sim.Vision.At(1, 20, 20));
        Assert.True(sim.Vision.Grid(0).IsEmpty);
    }

    [Fact]
    public void WhatAPlayerSeesFollowsTheirUnits()
    {
        var sim = Flat();
        var vision = sim.Vision;
        int scout = sim.SpawnUnit(0, "spearman", 20, 20);
        Assert.Equal(8, sim.Content.Unit("spearman").Sight.FloorToInt());
        Assert.Equal(Sight.Unseen, vision.At(0, 20, 20)); // nothing worked out before the first tick
        sim.Step();

        // A disc of eight columns around the column it stands on.
        Assert.Equal(Sight.Visible, vision.At(0, 20, 20));
        Assert.Equal(Sight.Visible, vision.At(0, 28, 20));
        Assert.Equal(Sight.Unseen, vision.At(0, 29, 20));
        Assert.Equal(Sight.Visible, vision.At(0, 25, 25)); // 5 and 5: 50 is within 64
        Assert.Equal(Sight.Unseen, vision.At(0, 26, 26)); // 72 is not
        Assert.Equal(Sight.Unseen, vision.At(1, 20, 20)); // player 1 has nothing there
        long first = vision.SeenColumns(0);
        Assert.InRange(first, 190, 220); // about pi times 64

        // It walks east: what it leaves behind stays seen, but is no longer in sight.
        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { scout }, 60, 20));
        for (int t = 0; t < 30 * Tps && vision.At(0, 60, 20) != Sight.Visible; t++) sim.Step();
        Assert.True(sim.Units.X[sim.Units.IndexOf(scout)].FloorToInt() >= 52);
        Assert.Equal(Sight.Seen, vision.At(0, 20, 20));
        Assert.Equal(Sight.Visible, vision.At(0, 60, 20));
        Assert.Equal(Sight.Unseen, vision.At(0, 60, 40));
        Assert.True(vision.SeenColumns(0) > 2 * first);
    }

    [Fact]
    public void BuildingsSeeAroundThemAndEnemiesAreSeenOnlyInSight()
    {
        var sim = Flat();
        var vision = sim.Vision;
        var hall = Place(sim, 1, "town_hall", 40, 40); // columns 40..46, sight 10
        int near = sim.SpawnUnit(0, "spearman", 32, 43);
        int far = sim.SpawnUnit(0, "spearman", 20, 80);
        int theirs = sim.SpawnUnit(1, "worker", 80, 80);
        sim.Step();

        Assert.Equal(10, hall.Def.Sight);
        Assert.Equal(Sight.Visible, vision.At(1, 30, 43));
        Assert.Equal(Sight.Unseen, vision.At(1, 29, 43));
        Assert.Equal(Sight.Visible, vision.At(1, 43, 56));
        Assert.Equal(Sight.Unseen, vision.At(1, 43, 57));
        Assert.Equal(Sight.Visible, vision.At(1, 33, 33)); // the corners are rounded: 7 and 7 is within 10...
        Assert.Equal(Sight.Unseen, vision.At(1, 32, 32)); // ...8 and 8 is not

        // Each player sees their own units anywhere, and the other's only within sight.
        Assert.True(vision.SeesUnit(1, sim.Units.IndexOf(near)));
        Assert.False(vision.SeesUnit(1, sim.Units.IndexOf(far)));
        Assert.True(vision.SeesUnit(1, sim.Units.IndexOf(theirs)));
        Assert.True(vision.SeesUnit(0, sim.Units.IndexOf(far)));
        Assert.False(vision.SeesUnit(0, sim.Units.IndexOf(theirs)));
    }

    [Fact]
    public void TheWorldAPlayerKnowsChangesOnlyWhereTheyLook()
    {
        var sim = Flat();
        var world = sim.World;
        int scout = sim.SpawnUnit(0, "spearman", 20, 20);
        var memory = new WorldMemory(sim, 0);
        sim.Step();

        // In sight, a change is known at once; out of sight, not until the column is seen again.
        world.Set(22, 1, 20, BlockType.IronOre);
        world.Set(60, 1, 20, BlockType.IronOre);
        world.Set(60, 2, 20, BlockType.Coal);
        world.Set(61, 1, 21, BlockType.Coal);
        Assert.Equal(BlockType.IronOre, memory.World.Get(22, 1, 20));
        Assert.Equal(BlockType.Stone, memory.World.Get(60, 1, 20));
        Assert.Equal(BlockType.Stone, memory.World.Get(61, 1, 21));
        Assert.Equal(2, memory.StaleColumns);

        // The scout walks there, and the columns catch up as they come into its sight.
        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { scout }, 60, 20));
        for (int t = 0; t < 30 * Tps && memory.StaleColumns > 0; t++)
        {
            sim.Step();
            memory.Update();
        }
        Assert.Equal(0, memory.StaleColumns);
        Assert.True(sim.Vision.Sees(0, 60, 20) && sim.Vision.Sees(0, 61, 21));
        Assert.Equal(BlockType.IronOre, memory.World.Get(60, 1, 20));
        Assert.Equal(BlockType.Coal, memory.World.Get(60, 2, 20));
        Assert.Equal(BlockType.Coal, memory.World.Get(61, 1, 21));

        // Where it has been but no longer sees, the world is known as it was when it left.
        Assert.Equal(Sight.Seen, sim.Vision.At(0, 20, 20));
        world.Set(20, 1, 20, BlockType.Crystal);
        Assert.False(memory.Update()); // nothing new seen since the last look
        Assert.Equal(BlockType.Stone, memory.World.Get(20, 1, 20));
        Assert.Equal(1, memory.StaleColumns);

        // Lifted (the match is over), it catches up with the real world and follows it, until detached.
        memory.Lift();
        Assert.Equal(0, memory.StaleColumns);
        world.Set(90, 1, 90, BlockType.Crystal);
        Assert.Equal(world.ComputeHash(), memory.World.ComputeHash());
        memory.Detach();
        world.Set(91, 1, 91, BlockType.Crystal);
        Assert.Equal(BlockType.Stone, memory.World.Get(91, 1, 91));
    }

    [Fact]
    public void FogOfWarIsSetBeforeTheFirstTickAndRecordedInTheLog()
    {
        var sim = Flat(fog: false);
        sim.Step();
        Assert.Throws<InvalidOperationException>(() => sim.Fog = true);

        var log = CommandLog.UsualStart(3, 128, 32, 128, fog: true);
        log.Ticks = 200;
        var text = log.ToString();
        Assert.Contains("fog 1", text);
        var copy = CommandLog.Parse(text);
        Assert.True(copy.Fog);
        Assert.False(CommandLog.Parse(CommandLog.UsualStart(3, 128, 32, 128).ToString()).Fog);

        // A match with fog of war replays the same, and what the players have seen is part of its state.
        var first = MatchReplay.Run(copy, 50, out var a);
        var second = MatchReplay.Run(copy, 50, out _);
        Assert.Equal(-1, MatchReplay.FirstMismatch(first, second));
        Assert.True(a.Fog);
        Assert.True(a.Vision.SeenColumns(0) > 0 && a.Vision.SeenColumns(1) > 0);
        var withoutFog = MatchReplay.Run(new CommandLog
        {
            Seed = copy.Seed, SizeX = copy.SizeX, SizeY = copy.SizeY, SizeZ = copy.SizeZ, Ticks = copy.Ticks,
            StartUnits = copy.StartUnits, StartBuildings = true, StartLairs = true, StartCities = true,
        }, 50);
        Assert.NotEqual(withoutFog[^1].Hash, first[^1].Hash);
    }
}
