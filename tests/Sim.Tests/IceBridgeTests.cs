using System;
using System.Collections.Generic;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Terrain magic: the mage's Ice Bridge, frozen over water and thawing again.</summary>
public class IceBridgeTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Stand = Ground + 1;
    private const int Tps = Simulation.TicksPerSecond;
    private const int P = 0, Enemy = 1;
    // The river: water from z 28 to 33 across the whole map, deep (levels 1 to 3) unless shallow.
    private const int RiverZ1 = 28, RiverZ2 = 33;

    private static Simulation River(bool shallow = false)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(64, 16, 64, Ground));
        for (int x = 0; x < 64; x++)
        for (int z = RiverZ1; z <= RiverZ2; z++)
        for (int y = shallow ? Ground : 1; y <= Ground; y++)
            sim.World.Set(x, y, z, BlockType.Water);
        return sim;
    }

    private static int Spawn(Simulation sim, int owner, string unit, int x, int z) => sim.SpawnUnit(owner, unit, x, z, Stand);

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

    private static void Freeze(Simulation sim, int[] units, int x1, int z1, int x2, int z2)
    {
        sim.Schedule(sim.Tick, new IceBridgeCommand(P, units, x1, z1, x2, z2));
        sim.Step();
    }

    private static int IceIn(Simulation sim, int x1, int x2, int z1, int z2)
    {
        int n = 0;
        for (int x = x1; x <= x2; x++)
        for (int z = z1; z <= z2; z++)
            if (sim.World.Get(x, Ground, z) == BlockType.Ice) n++;
        return n;
    }

    [Fact]
    public void AMageFreezesTheRiverIntoABridgeThreeColumnsWide()
    {
        var sim = River();
        int mage = Spawn(sim, P, "mage", 30, 24);
        // From bank to bank: the bank columns stay grass, the six of water between freeze.
        Freeze(sim, new[] { mage }, 30, 26, 30, 35);
        Assert.Single(sim.Spells.IceBridges);
        Assert.Equal(6 * SpellSystem.IceWidth, IceIn(sim, 29, 31, RiverZ1, RiverZ2));
        Assert.Equal(6 * SpellSystem.IceWidth, sim.Spells.IceBridges[0].Blocks.Count);
        Assert.Equal(0, IceIn(sim, 28, 28, RiverZ1, RiverZ2));
        Assert.Equal(0, IceIn(sim, 32, 32, RiverZ1, RiverZ2));
        Assert.Equal(BlockType.Grass, sim.World.Get(30, Ground, 26));
        Assert.Equal(BlockType.Water, sim.World.Get(30, Ground - 1, 30)); // only the top freezes
        for (int z = RiverZ1; z <= RiverZ2; z++) Assert.True(Walk.CanStand(sim.World, 30, Stand, z));
        Assert.False(sim.Spells.CanCast(Index(sim, mage), Spell.IceBridge));
    }

    [Fact]
    public void AnArmyWalksAcrossTheIceToTheFarBank()
    {
        var sim = River();
        int mage = Spawn(sim, P, "mage", 30, 24);
        int spear = Spawn(sim, P, "spearman", 31, 24);
        // Without the ice there is no way over.
        Assert.Equal(-1, Walk.GroundLevel(sim.World, 30, 30));
        Freeze(sim, new[] { mage }, 30, 26, 30, 35);
        sim.Schedule(sim.Tick, new MoveCommand(P, new[] { spear }, 30, 40));
        sim.Run(15 * Tps);
        int i = Index(sim, spear);
        Assert.True(sim.Units.Z[i] > Fix64.FromInt(RiverZ2 + 1), $"it crossed (z {sim.Units.Z[i]})");
        Assert.Equal(Stand, sim.Units.Y[i]);
    }

    [Fact]
    public void MagesFreezingTogetherMakeAWiderBridge()
    {
        var sim = River();
        sim.Economy.Player(P).Mana = 1000; // spells cost mana: plenty for every cast here
        int a = Spawn(sim, P, "mage", 30, 24), b = Spawn(sim, P, "mage", 31, 24);
        Freeze(sim, new[] { a, b }, 30, 26, 30, 35);
        Assert.Equal(5, SpellSystem.IceWidthFor(2));
        Assert.Equal(6 * 5, IceIn(sim, 28, 32, RiverZ1, RiverZ2));
        Assert.Equal(0, IceIn(sim, 27, 27, RiverZ1, RiverZ2));
        Assert.Equal(0, IceIn(sim, 33, 33, RiverZ1, RiverZ2));
        Assert.False(sim.Spells.CanCast(Index(sim, a), Spell.IceBridge));
        Assert.False(sim.Spells.CanCast(Index(sim, b), Spell.IceBridge));

        sim = River();
        sim.Economy.Player(P).Mana = 1000;
        int[] three = { Spawn(sim, P, "mage", 30, 24), Spawn(sim, P, "mage", 31, 24), Spawn(sim, P, "mage", 29, 24) };
        Freeze(sim, three, 30, 26, 30, 35);
        Assert.Equal(6 * 7, IceIn(sim, 27, 33, RiverZ1, RiverZ2));
        Assert.Equal(6 * 7, IceIn(sim, 0, 63, RiverZ1, RiverZ2));
        var plan = new List<(int X, int Z, int Y, bool Room)>();
        Assert.Equal(Stand, sim.Spells.PlanIce(40, 26, 40, 35, plan, 3));
        Assert.Equal(6 * 7, plan.Count);
    }

    [Fact]
    public void ABridgeIsAtMostSixteenColumnsLong()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(64, 16, 64, Ground));
        for (int x = 10; x < 50; x++)
        for (int z = 20; z <= 40; z++)
            sim.World.Set(x, Ground, z, BlockType.Water);
        int mage = Spawn(sim, P, "mage", 30, 18);
        Freeze(sim, new[] { mage }, 8, 21, 52, 21);
        Assert.Equal(SpellSystem.IceLength * SpellSystem.IceWidth, IceIn(sim, 10, 49, 20, 40));
        Assert.Equal(SpellSystem.IceLength, IceIn(sim, 10, 49, 21, 21));
        Assert.Equal(SpellSystem.IceLength, IceIn(sim, 10, 49, 20, 20));
        Assert.Equal(SpellSystem.IceLength, IceIn(sim, 10, 49, 22, 22));
    }

    [Fact]
    public void ThePlanShowsWhereWaterFreezes()
    {
        var sim = River();
        var plan = new List<(int X, int Z, int Y, bool Room)>();
        Assert.Equal(Stand, sim.Spells.PlanIce(30, 26, 30, 35, plan));
        Assert.Equal(6 * SpellSystem.IceWidth, plan.Count);
        foreach (var (x, z, y, room) in plan)
        {
            Assert.InRange(z, RiverZ1, RiverZ2);
            Assert.InRange(x, 29, 31);
            Assert.True(room);
            Assert.Equal(Stand, y);
        }
        // Ends that are not ground, or far apart in height, plan nothing.
        Assert.Equal(-1, sim.Spells.PlanIce(30, 29, 30, 35, plan));
        Assert.Empty(plan);
    }

    [Fact]
    public void WithoutWaterThereIsNoIce()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(64, 16, 64, Ground));
        for (int x = 0; x < 64; x++)
        for (int z = 28; z <= 31; z++)
        for (int y = 1; y <= Ground; y++)
            sim.World.Set(x, y, z, BlockType.Air); // a dry trench
        int mage = Spawn(sim, P, "mage", 30, 24);
        Freeze(sim, new[] { mage }, 30, 26, 30, 34);
        Assert.Empty(sim.Spells.IceBridges);
        Assert.True(sim.Spells.CanCast(Index(sim, mage), Spell.IceBridge), "nothing frozen costs nothing");
    }

    /// <summary>A river in a gorge: deep water (levels 1 to 3) under banks two blocks higher (units stand at 6).</summary>
    private static Simulation Gorge()
    {
        var sim = River();
        for (int x = 0; x < 64; x++)
        for (int z = 0; z < 64; z++)
        {
            if (z >= RiverZ1 && z <= RiverZ2) continue;
            sim.World.Set(x, Ground, z, BlockType.Soil);
            sim.World.Set(x, Ground + 1, z, BlockType.Soil);
            sim.World.Set(x, Ground + 2, z, BlockType.Grass);
        }
        return sim;
    }

    [Fact]
    public void OverAGorgeTheIceSpansFromBankToBankAtTheirHeight()
    {
        var sim = Gorge();
        const int High = Stand + 2;
        int mage = sim.SpawnUnit(P, "mage", 30, 24, High);
        int spear = sim.SpawnUnit(P, "spearman", 31, 24, High);
        Freeze(sim, new[] { mage }, 30, 26, 30, 35);
        Assert.Single(sim.Spells.IceBridges);
        for (int z = RiverZ1; z <= RiverZ2; z++)
        {
            Assert.Equal(BlockType.Ice, sim.World.Get(30, High - 1, z));
            Assert.Equal(BlockType.Air, sim.World.Get(30, Ground + 1, z));
            Assert.Equal(BlockType.Water, sim.World.Get(30, Ground, z));
        }
        sim.Schedule(sim.Tick, new MoveCommand(P, new[] { spear }, 30, 40));
        sim.Run(15 * Tps);
        int i = Index(sim, spear);
        Assert.True(sim.Units.Z[i] > Fix64.FromInt(RiverZ2 + 1), $"it crossed (z {sim.Units.Z[i]})");

        // Whoever is on it when it melts falls into the deep water and drowns; the ice melts into air.
        int caught = sim.SpawnUnit(P, "spearman", 30, 31, High);
        sim.Schedule(sim.Tick, new StanceCommand(P, new[] { caught }, UnitStance.Hold));
        sim.Run(SpellSystem.IceLastsTicks);
        Assert.Empty(sim.Spells.IceBridges);
        Assert.Equal(BlockType.Air, sim.World.Get(30, High - 1, 30));
        Assert.Equal(BlockType.Water, sim.World.Get(30, Ground, 30));
        sim.Run(2);
        Assert.Equal(-1, Index(sim, caught));
        Assert.NotEqual(-1, Index(sim, spear));
    }

    [Fact]
    public void TheIceCracksThenThawsBackIntoWater()
    {
        var sim = River();
        int mage = Spawn(sim, P, "mage", 30, 24);
        Freeze(sim, new[] { mage }, 30, 26, 30, 35);
        var ice = sim.Spells.IceBridges[0];
        Assert.False(sim.Spells.Cracking(ice));
        sim.Run(SpellSystem.IceLastsTicks - SpellSystem.CrackTicks);
        Assert.True(sim.Spells.Cracking(ice));
        Assert.Equal(18, IceIn(sim, 0, 63, 0, 63));
        sim.Run(SpellSystem.CrackTicks);
        Assert.Empty(sim.Spells.IceBridges);
        Assert.Equal(0, IceIn(sim, 0, 63, 0, 63));
        Assert.Equal(BlockType.Water, sim.World.Get(30, Ground, 30));
    }

    [Fact]
    public void UnitsOnTheIceWhenItThawsDrownInDeepWater()
    {
        var sim = River();
        int mage = Spawn(sim, P, "mage", 30, 24);
        Freeze(sim, new[] { mage }, 30, 26, 30, 35);
        int caught = sim.SpawnUnit(P, "spearman", 30, 30, Stand);
        int safe = Spawn(sim, P, "spearman", 34, 24);
        sim.Schedule(sim.Tick, new StanceCommand(P, new[] { caught }, UnitStance.Hold));
        sim.Run(SpellSystem.IceLastsTicks + 2);
        Assert.Equal(-1, Index(sim, caught));
        Assert.NotEqual(-1, Index(sim, safe));
        Assert.NotEqual(-1, Index(sim, mage));
    }

    [Fact]
    public void UnitsOnTheIceOverShallowWaterWadeOn()
    {
        var sim = River(shallow: true);
        int mage = Spawn(sim, P, "mage", 30, 24);
        Freeze(sim, new[] { mage }, 30, 26, 30, 35);
        Assert.Equal(18, IceIn(sim, 0, 63, 0, 63));
        int wader = sim.SpawnUnit(P, "spearman", 30, 30, Stand);
        sim.Schedule(sim.Tick, new StanceCommand(P, new[] { wader }, UnitStance.Hold));
        sim.Run(SpellSystem.IceLastsTicks + 2);
        int i = Index(sim, wader);
        Assert.NotEqual(-1, i);
        Assert.Equal(Ground, sim.Units.Y[i]);
        Assert.Equal(BlockType.Water, sim.World.Get(30, Ground, 30));
    }

    [Fact]
    public void IceCannotBeDugNorHaveEarthRaisedOnIt()
    {
        var sim = River();
        int a = Spawn(sim, P, "mage", 30, 24), b = Spawn(sim, P, "mage", 31, 24);
        Freeze(sim, new[] { a }, 30, 26, 30, 35);
        sim.Schedule(sim.Tick, new DigCommand(P, 30, Ground, 30));
        sim.Step();
        Assert.Equal(BlockType.Ice, sim.World.Get(30, Ground, 30));
        Assert.False(BlockInfo.IsDiggable(BlockType.Ice));
        sim.Schedule(sim.Tick, new EarthWallCommand(P, new[] { b }, 26, 30, 34, 30));
        sim.Step();
        for (int x = 28; x <= 32; x++) Assert.Equal(BlockType.Air, sim.World.Get(x, Stand, 30));
    }

    [Fact]
    public void OneSpellMakesTheCasterWaitForEither()
    {
        var sim = River();
        int mage = Spawn(sim, P, "mage", 30, 24);
        sim.Schedule(sim.Tick, new EarthWallCommand(P, new[] { mage }, 26, 22, 34, 22));
        sim.Step();
        Assert.Single(sim.Spells.Walls);
        Freeze(sim, new[] { mage }, 30, 26, 30, 35);
        Assert.Empty(sim.Spells.IceBridges);
        sim.Run(SpellSystem.CooldownTicks);
        Freeze(sim, new[] { mage }, 30, 26, 30, 35);
        Assert.Single(sim.Spells.IceBridges);
    }

    [Fact]
    public void OnlyUnitsWithTheSpellFreezeWater()
    {
        var sim = River();
        int spear = Spawn(sim, P, "spearman", 30, 24);
        Freeze(sim, new[] { spear }, 30, 26, 30, 35);
        Assert.Empty(sim.Spells.IceBridges);
        int theirs = Spawn(sim, Enemy, "mage", 31, 24);
        Freeze(sim, new[] { theirs }, 30, 26, 30, 35);
        Assert.Empty(sim.Spells.IceBridges);
    }

    [Fact]
    public void AMageFarAwayWalksToTheBankAndThenFreezes()
    {
        var sim = River();
        int mage = Spawn(sim, P, "mage", 30, 5);
        Freeze(sim, new[] { mage }, 50, 26, 50, 35);
        Assert.Empty(sim.Spells.IceBridges);
        Assert.True(sim.Spells.Casting(mage));
        for (int t = 0; t < 30 * Tps && sim.Spells.IceBridges.Count == 0; t++) sim.Step();
        Assert.Single(sim.Spells.IceBridges);
        Assert.Equal(6 * SpellSystem.IceWidth, IceIn(sim, 49, 51, RiverZ1, RiverZ2));
    }

    [Fact]
    public void TheSameCastsGiveTheSameState()
    {
        ulong Play()
        {
            var sim = River();
            int a = Spawn(sim, P, "mage", 30, 5), b = Spawn(sim, P, "mage", 20, 24);
            int s = Spawn(sim, P, "spearman", 20, 25);
            sim.Schedule(0, new IceBridgeCommand(P, new[] { a }, 50, 26, 50, 35));
            sim.Schedule(10, new IceBridgeCommand(P, new[] { b }, 20, 26, 20, 35));
            sim.Schedule(20, new MoveCommand(P, new[] { s }, 20, 45));
            sim.Run(SpellSystem.IceLastsTicks + 30 * Tps);
            return sim.StateHash();
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void TheCommandRoundTripsThroughTheLog()
    {
        var log = CommandLog.Parse("rts-log 1\nseed 1\nworld 32 16 32\nticks 10\n5 icebridge 0 3 4 9 4 7,8\n");
        Assert.Equal(new IceBridgeCommand(0, new[] { 7, 8 }, 3, 4, 9, 4), log.Commands[0].Command);
        Assert.Equal(log.ToString(), CommandLog.Parse(log.ToString()).ToString());
    }

    /// <summary>A flat world with a sheet of ice for ground from z 20 to 29 (the rest is grass).</summary>
    private static Simulation IceSheet()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(64, 16, 64, Ground));
        for (int x = 0; x < 64; x++)
        for (int z = 20; z < 30; z++)
            sim.World.Set(x, Ground, z, BlockType.Ice);
        return sim;
    }

    [Fact]
    public void OnIceAUnitIsSlowToGetGoing()
    {
        var sim = IceSheet();
        int onIce = Spawn(sim, P, "spearman", 5, 25), onGrass = Spawn(sim, P, "spearman", 5, 40);
        sim.Schedule(sim.Tick, new MoveCommand(P, new[] { onIce }, 50, 25));
        sim.Schedule(sim.Tick, new MoveCommand(P, new[] { onGrass }, 50, 40));
        sim.Run(Tps / 2);
        Fix64 ice = sim.Units.X[Index(sim, onIce)] - Fix64.FromInt(5), grass = sim.Units.X[Index(sim, onGrass)] - Fix64.FromInt(5);
        Assert.True(ice > Fix64.Zero, "it gets going");
        Assert.True(ice * 4 < grass * 3, $"slower off the mark on ice ({ice} against {grass})");
        // Once going it keeps up, and it still gets there.
        sim.Run(15 * Tps);
        Assert.True(sim.Units.X[Index(sim, onIce)] > Fix64.FromInt(49));
    }

    [Fact]
    public void OnIceAUnitSlidesOnAfterItStops()
    {
        var sim = IceSheet();
        int onIce = Spawn(sim, P, "spearman", 5, 25), onGrass = Spawn(sim, P, "spearman", 5, 40);
        sim.Schedule(sim.Tick, new MoveCommand(P, new[] { onIce }, 60, 25));
        sim.Schedule(sim.Tick, new MoveCommand(P, new[] { onGrass }, 60, 40));
        sim.Run(3 * Tps);
        int a = Index(sim, onIce), b = Index(sim, onGrass);
        Assert.NotEqual(Fix64.Zero, sim.Units.SlideX[a]);
        Assert.Equal(Fix64.Zero, sim.Units.SlideX[b]); // only on ice
        Fix64 iceAt = sim.Units.X[a], grassAt = sim.Units.X[b];
        sim.Schedule(sim.Tick, new StopCommand(P, new[] { onIce, onGrass }));
        sim.Run(2 * Tps);
        a = Index(sim, onIce);
        b = Index(sim, onGrass);
        Assert.True(sim.Units.X[a] - iceAt > Fix64.One, $"it slid on ({sim.Units.X[a] - iceAt})");
        Assert.True(sim.Units.X[b] - grassAt < Fix64.Half, "on grass it stops");
        Assert.Equal(Fix64.Zero, sim.Units.SlideX[a]); // and the slide dies away
    }

    [Fact]
    public void TheMageHasTheSpellInTheData()
    {
        Assert.True(Content.ContentDb.Default.Unit("mage").IceBridge);
        Assert.False(Content.ContentDb.Default.Unit("spearman").IceBridge);
    }
}
