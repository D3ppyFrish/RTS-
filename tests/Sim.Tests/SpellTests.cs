using System;
using System.Collections.Generic;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Terrain magic: the mage's Earth Wall.</summary>
public class SpellTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Stand = Ground + 1;
    private const int Tps = Simulation.TicksPerSecond;
    private const int P = 0, Enemy = 1;

    private static Simulation Flat() => TestWorlds.Sim(TestWorlds.Flat(64, 16, 64, Ground));

    private static int Spawn(Simulation sim, int owner, string unit, int x, int z) => sim.SpawnUnit(owner, unit, x, z, Stand);

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

    /// <summary>Earth blocks standing in column (x, z) above the ground.</summary>
    private static int Earth(Simulation sim, int x, int z)
    {
        int n = 0;
        for (int y = Stand; y < Stand + SpellSystem.Height + 2; y++)
            if (sim.World.Get(x, y, z) is BlockType.Soil or BlockType.Grass) n++;
        return n;
    }

    private static void Cast(Simulation sim, int[] units, int x1, int z1, int x2, int z2)
    {
        sim.Schedule(sim.Tick, new EarthWallCommand(P, units, x1, z1, x2, z2));
        sim.Step();
    }

    [Fact]
    public void AMageRaisesAWallOfEarthTooHighToClimb()
    {
        var sim = Flat();
        int mage = Spawn(sim, P, "mage", 20, 30);
        Cast(sim, new[] { mage }, 26, 27, 26, 33);
        for (int z = 27; z <= 33; z++)
        {
            Assert.Equal(SpellSystem.Height, Earth(sim, 26, z));
            Assert.Equal(Stand + SpellSystem.Height, Walk.GroundLevel(sim.World, 26, z));
        }
        Assert.Equal(0, Earth(sim, 26, 26));
        Assert.Equal(0, Earth(sim, 26, 34));
        Assert.True(SpellSystem.Height > Walk.MaxClimb, "nobody climbs over it");
        Assert.Single(sim.Spells.Walls);
        Assert.Equal(7 * SpellSystem.Height, sim.Spells.Walls[0].Blocks.Count);
    }

    [Fact]
    public void AWallIsAtMostEightColumnsLong()
    {
        var sim = Flat();
        int mage = Spawn(sim, P, "mage", 20, 30);
        Cast(sim, new[] { mage }, 26, 20, 26, 40);
        int columns = 0;
        for (int z = 15; z <= 45; z++)
            if (Earth(sim, 26, z) > 0) columns++;
        Assert.Equal(SpellSystem.MaxLength, columns);
        Assert.Equal(SpellSystem.Height, Earth(sim, 26, 20));
    }

    [Fact]
    public void OnlyUnitsWithTheSpellCastIt()
    {
        var sim = Flat();
        int spear = Spawn(sim, P, "spearman", 20, 30);
        Cast(sim, new[] { spear }, 26, 28, 26, 32);
        Assert.Empty(sim.Spells.Walls);
        Assert.Equal(0, Earth(sim, 26, 30));

        // Nor someone else's mage.
        int theirs = Spawn(sim, Enemy, "mage", 20, 32);
        Cast(sim, new[] { theirs }, 26, 28, 26, 32);
        Assert.Empty(sim.Spells.Walls);
    }

    [Fact]
    public void ACasterWaitsBeforeItsNextWallButAnotherCanCast()
    {
        var sim = Flat();
        int a = Spawn(sim, P, "mage", 20, 30), b = Spawn(sim, P, "mage", 20, 31);
        Cast(sim, new[] { a }, 26, 20, 26, 24);
        Assert.False(sim.Spells.CanCast(Index(sim, a)));
        Assert.True(sim.Spells.CanCast(Index(sim, b)));
        // With both selected only b is ready, so it casts alone: a plain wall.
        Cast(sim, new[] { a, b }, 26, 30, 26, 34);
        Assert.Equal(2, sim.Spells.Walls.Count);
        Assert.Equal(5 * SpellSystem.Height, sim.Spells.Walls[1].Blocks.Count);
        Assert.False(sim.Spells.CanCast(Index(sim, b)));
        // Neither can cast a third until its wait is over.
        Cast(sim, new[] { a, b }, 30, 30, 30, 34);
        Assert.Equal(2, sim.Spells.Walls.Count);
        Assert.Equal(0, Earth(sim, 30, 32));
        sim.Run(SpellSystem.CooldownTicks);
        Assert.True(sim.Spells.CanCast(Index(sim, a)));
        Cast(sim, new[] { a }, 30, 30, 30, 34);
        Assert.Equal(SpellSystem.Height, Earth(sim, 30, 32));
    }

    /// <summary>Columns (along x, at row z) with earth standing in them, from x 0 to 63.</summary>
    private static int Row(Simulation sim, int z)
    {
        int n = 0;
        for (int x = 0; x < 64; x++)
            if (Earth(sim, x, z) > 0) n++;
        return n;
    }

    [Fact]
    public void MagesCastingTogetherRaiseAThickerLongerWall()
    {
        // Two mages: two rows thick (the line and the row before it), twelve columns long.
        var sim = Flat();
        int a = Spawn(sim, P, "mage", 30, 22), b = Spawn(sim, P, "mage", 31, 22);
        Cast(sim, new[] { a, b }, 20, 30, 45, 30);
        Assert.Single(sim.Spells.Walls);
        Assert.Equal(SpellSystem.MaxLength + SpellSystem.WallPerHelper, Row(sim, 30));
        Assert.Equal(SpellSystem.MaxLength + SpellSystem.WallPerHelper, Row(sim, 29));
        Assert.Equal(0, Row(sim, 31));
        Assert.Equal(2 * 12 * SpellSystem.Height, sim.Spells.Walls[0].Blocks.Count);
        Assert.False(sim.Spells.CanCast(Index(sim, a)));
        Assert.False(sim.Spells.CanCast(Index(sim, b)), "both cast, so both wait");

        // Three: three rows, sixteen long.
        sim = Flat();
        int[] three = { Spawn(sim, P, "mage", 30, 22), Spawn(sim, P, "mage", 31, 22), Spawn(sim, P, "mage", 32, 22) };
        Cast(sim, three, 20, 30, 45, 30);
        for (int z = 29; z <= 31; z++) Assert.Equal(16, Row(sim, z));
        Assert.Equal(0, Row(sim, 28));
        Assert.Equal(0, Row(sim, 32));
    }

    [Fact]
    public void AtMostThreeMagesJoinOneSpell()
    {
        var sim = Flat();
        int[] four = new int[4];
        for (int k = 0; k < 4; k++) four[k] = Spawn(sim, P, "mage", 28 + k, 22);
        Cast(sim, four, 20, 30, 45, 30);
        Assert.Single(sim.Spells.Walls);
        Assert.Equal(SpellSystem.WallLength(SpellSystem.MaxGroup), Row(sim, 30));
        Assert.Equal(0, Row(sim, 32));
        int ready = 0;
        foreach (int id in four)
            if (sim.Spells.CanCast(Index(sim, id))) ready++;
        Assert.Equal(1, ready);
        Assert.Equal(SpellSystem.WallLength(4), SpellSystem.WallLength(3));
    }

    [Fact]
    public void AMageInRangeWaitsForTheOthersToWalkUp()
    {
        var sim = Flat();
        int near = Spawn(sim, P, "mage", 30, 22), far = Spawn(sim, P, "mage", 2, 30);
        Cast(sim, new[] { far, near }, 26, 26, 26, 34);
        Assert.Empty(sim.Spells.Walls);
        Assert.True(sim.Spells.Casting(near));
        Assert.True(sim.Spells.Casting(far));
        Assert.True(sim.Spells.CanCast(Index(sim, near)), "nothing is spent while it waits");
        for (int t = 0; t < 30 * Tps && sim.Spells.Walls.Count == 0; t++) sim.Step();
        Assert.Single(sim.Spells.Walls);
        Assert.False(sim.Spells.Casting(near));
        Assert.False(sim.Spells.Casting(far));
        // A wall two thick: the line at x 26 and the row before it.
        Assert.Equal(SpellSystem.Height, Earth(sim, 26, 30));
        Assert.Equal(SpellSystem.Height, Earth(sim, 25, 30));
        Assert.Equal(0, Earth(sim, 27, 30));
        Assert.False(sim.Spells.CanCast(Index(sim, near)));
        Assert.False(sim.Spells.CanCast(Index(sim, far)));
    }

    [Fact]
    public void IfTheWalkerIsCalledOffTheOneWaitingCastsAlone()
    {
        var sim = Flat();
        int near = Spawn(sim, P, "mage", 30, 22), far = Spawn(sim, P, "mage", 2, 30);
        Cast(sim, new[] { far, near }, 26, 26, 26, 34);
        sim.Run(Tps);
        sim.Schedule(sim.Tick, new MoveCommand(P, new[] { far }, 2, 10));
        sim.Run(2);
        Assert.Single(sim.Spells.Walls);
        Assert.Equal(SpellSystem.Height, Earth(sim, 26, 30));
        Assert.Equal(0, Earth(sim, 25, 30));
        Assert.True(sim.Spells.CanCast(Index(sim, far)), "the one that left spends nothing");
        Assert.False(sim.Spells.CanCast(Index(sim, near)));
    }

    [Fact]
    public void ThePlanGrowsWithTheCasters()
    {
        var sim = Flat();
        var plan = new List<(int X, int Z, int Y, bool Room)>();
        sim.Spells.Plan(20, 30, 45, 30, plan);
        Assert.Equal(SpellSystem.MaxLength, plan.Count);
        sim.Spells.Plan(20, 30, 45, 30, plan, 2);
        Assert.Equal(2 * 12, plan.Count);
        sim.Spells.Plan(20, 30, 45, 30, plan, 3);
        Assert.Equal(3 * 16, plan.Count);
        foreach (var (_, z, y, room) in plan)
        {
            Assert.InRange(z, 29, 31);
            Assert.Equal(Stand, y);
            Assert.True(room);
        }
    }

    [Fact]
    public void AMageOutOfRangeWalksUpAndThenCasts()
    {
        var sim = Flat();
        int mage = Spawn(sim, P, "mage", 5, 30);
        Cast(sim, new[] { mage }, 40, 28, 40, 32);
        Assert.Empty(sim.Spells.Walls);
        Assert.True(sim.Spells.Casting(mage));
        for (int t = 0; t < 30 * Tps && sim.Spells.Walls.Count == 0; t++) sim.Step();
        Assert.Single(sim.Spells.Walls);
        Assert.False(sim.Spells.Casting(mage));
        Assert.Equal(SpellSystem.Height, Earth(sim, 40, 30));
        int i = Index(sim, mage);
        Fix64 dx = sim.Units.X[i] - Fix64.FromInt(40), dz = sim.Units.Z[i] - Fix64.FromInt(30);
        Assert.True(dx * dx + dz * dz <= Fix64.FromInt(SpellSystem.CastRange + 1) * Fix64.FromInt(SpellSystem.CastRange + 1),
            "it stops once in range");
        Assert.Equal(UnitOrder.Idle, sim.Units.Order[i]);
    }

    [Fact]
    public void AnotherOrderStopsAMageOnItsWayToCast()
    {
        var sim = Flat();
        int mage = Spawn(sim, P, "mage", 5, 30);
        Cast(sim, new[] { mage }, 40, 28, 40, 32);
        sim.Run(Tps);
        sim.Schedule(sim.Tick, new MoveCommand(P, new[] { mage }, 5, 10));
        sim.Run(30 * Tps);
        Assert.False(sim.Spells.Casting(mage));
        Assert.Empty(sim.Spells.Walls);
        Assert.True(sim.Spells.CanCast(Index(sim, mage)), "an order given up costs nothing");
    }

    [Fact]
    public void EarthDoesNotRiseUnderUnitsOrBuildings()
    {
        var sim = Flat();
        int mage = Spawn(sim, P, "mage", 20, 30);
        Spawn(sim, Enemy, "spearman", 26, 30);
        var b = sim.Economy.PlaceFinished(P, sim.Content.Building("house"), 25, 33)!;
        Cast(sim, new[] { mage }, 26, 27, 26, 34);
        Assert.Equal(SpellSystem.Height, Earth(sim, 26, 27));
        Assert.Equal(0, Earth(sim, 26, 30));
        Assert.Same(b, sim.Buildings.AtColumn(26, 33));
        Assert.Equal(SpellSystem.Height, Earth(sim, 26, 28));
    }

    [Fact]
    public void TheWallCrumblesWhenItsTimeIsUp()
    {
        var sim = Flat();
        int mage = Spawn(sim, P, "mage", 20, 30);
        Cast(sim, new[] { mage }, 26, 28, 26, 32);
        // A worker digs a block out before then: it stays dug, and the rest of the wall still crumbles.
        sim.Schedule(sim.Tick, new DigCommand(P, 26, Stand + SpellSystem.Height - 1, 30));
        sim.Run(SpellSystem.LastsTicks - 2);
        Assert.Equal(SpellSystem.Height - 1, Earth(sim, 26, 30));
        Assert.Equal(SpellSystem.Height, Earth(sim, 26, 28));
        sim.Run(2);
        Assert.Empty(sim.Spells.Walls);
        for (int z = 28; z <= 32; z++)
        {
            Assert.Equal(0, Earth(sim, 26, z));
            Assert.Equal(Stand, Walk.GroundLevel(sim.World, 26, z));
        }
        Assert.Equal(BlockType.Grass, sim.World.Get(26, Ground, 30));
    }

    [Fact]
    public void TheSameCastsGiveTheSameState()
    {
        ulong Play()
        {
            var sim = Flat();
            int a = Spawn(sim, P, "mage", 5, 30), b = Spawn(sim, P, "mage", 20, 20);
            int d = Spawn(sim, P, "mage", 36, 30);
            sim.Schedule(0, new EarthWallCommand(P, new[] { a, d }, 40, 28, 40, 32));
            int c = Spawn(sim, P, "mage", 4, 34);
            sim.Schedule(10, new EarthWallCommand(P, new[] { b }, 22, 25, 28, 25));
            sim.Schedule(20, new EarthWallCommand(P, new[] { b, c }, 10, 20, 10, 26)); // b is waiting: c walks up alone
            sim.Run(SpellSystem.LastsTicks + 10 * Tps);
            return sim.StateHash();
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void TheCommandRoundTripsThroughTheLog()
    {
        var log = CommandLog.Parse("rts-log 1\nseed 1\nworld 32 16 32\nticks 10\n5 earthwall 0 3 4 9 4 7,8\n");
        Assert.Equal(new EarthWallCommand(0, new[] { 7, 8 }, 3, 4, 9, 4), log.Commands[0].Command);
        Assert.Equal(log.ToString(), CommandLog.Parse(log.ToString()).ToString());
    }

    [Fact]
    public void TheMageHasTheSpellInTheData()
    {
        Assert.True(Content.ContentDb.Default.Unit("mage").EarthWall);
        Assert.False(Content.ContentDb.Default.Unit("spearman").EarthWall);
    }
}
