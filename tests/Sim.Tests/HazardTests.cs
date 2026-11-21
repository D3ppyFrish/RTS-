using System;
using System.Linq;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Issue #66: sinkholes, units on a move order that keep off hazards, and an alert when the ground kills.</summary>
public class HazardTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Feet = Ground + 1;
    private const int Tps = Simulation.TicksPerSecond;
    private const int Settlers = 0, Wild = Simulation.WildPlayer;

    private static Simulation Flat(int size = 64) => TestWorlds.Sim(TestWorlds.Flat(size, 16, size, Ground));

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);
    private static int Hp(Simulation sim, int id) => sim.Units.Exists(id) ? sim.Units.Hp[Index(sim, id)] : 0;
    private static (int X, int Y, int Z) At(Simulation sim, int id)
    {
        int i = Index(sim, id);
        return (sim.Units.X[i].FloorToInt(), sim.Units.Y[i], sim.Units.Z[i].FloorToInt());
    }

    /// <summary>Steps until the unit stands in column (x, z), checking <paramref name="each"/> every tick on the way.</summary>
    private static void WalkTo(Simulation sim, int id, int x, int z, int maxTicks, Action? each = null)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            var p = At(sim, id);
            if (p.X == x && p.Z == z) return;
            each?.Invoke();
            sim.Step();
        }
        Assert.Fail($"unit {id} did not get to {x},{z} in {maxTicks} ticks (it is at {At(sim, id)})");
    }

    // --- Move orders around hazards -------------------------------------------------------------------

    /// <summary>
    /// A band of poison bog lies across the straight way, with open ground round its end: a unit sent across walks
    /// round it. At most it brushes the band's corner as it steps diagonally past it, and comes to no harm.
    /// </summary>
    [Fact]
    public void UnitsWalkAroundPoisonBogWhenThereIsAnotherWay()
    {
        var sim = Flat();
        for (int z = 10; z <= 30; z++)
        for (int x = 29; x <= 31; x++)
            sim.World.Set(x, Ground, z, BlockType.Bog);
        int id = sim.SpawnUnit(Settlers, "spearman", 20, 20);
        int full = Hp(sim, id), onBog = 0;
        sim.Schedule(0, new MoveCommand(Settlers, new[] { id }, 40, 20));
        WalkTo(sim, id, 40, 20, 60 * Tps, () =>
        {
            var p = At(sim, id);
            if (sim.World.Get(p.X, p.Y - 1, p.Z) == BlockType.Bog) onBog++;
            Assert.True(p.X < 29 || p.X > 31 || p.Z < 10 || p.Z > 29, $"the unit went across the bog at {p}");
        });
        Assert.InRange(onBog, 0, Tps / 4);
        Assert.InRange(Hp(sim, id), full - HazardSystem.BogDamage, full);
    }

    /// <summary>When the bog runs right across the map there is no other way: the unit wades across it.</summary>
    [Fact]
    public void UnitsCrossABogWhenThereIsNoOtherWay()
    {
        var sim = Flat();
        for (int z = 0; z < 64; z++) sim.World.Set(30, Ground, z, BlockType.Bog);
        int id = sim.SpawnUnit(Settlers, "spearman", 20, 20);
        sim.Schedule(0, new MoveCommand(Settlers, new[] { id }, 40, 20));
        WalkTo(sim, id, 40, 20, 60 * Tps);
    }

    /// <summary>
    /// The walk graph marks the ground beside lava (its heat), bog and sinkhole crust as hazards, and keeps up as
    /// lava appears in the next column; a unit sent past a pool of lava gives the heat a wide berth.
    /// </summary>
    [Fact]
    public void UnitsKeepOutOfLavasHeat()
    {
        var sim = Flat();
        var g = sim.Walkable;
        Assert.False(g.IsHazard(g.Node(31, 20, Feet)));
        for (int z = 15; z <= 25; z++) sim.World.Set(30, Ground, z, BlockType.Lava);
        g.Update();
        Assert.True(g.IsHazard(g.Node(31, 20, Feet)), "beside the lava is hot");
        Assert.True(g.IsHazard(g.Node(29, 25, Feet)));
        Assert.False(g.IsHazard(g.Node(32, 20, Feet)), "two columns off is safe");
        sim.World.Set(10, Ground, 10, BlockType.Bog);
        HazardSystem.MakeSinkhole(sim.World, 50, Ground, 50);
        g.Update();
        Assert.True(g.IsHazard(g.Node(10, 10, Feet)));
        Assert.True(g.IsHazard(g.Node(52, 50, Feet)));

        int id = sim.SpawnUnit(Settlers, "spearman", 20, 20);
        int full = Hp(sim, id);
        sim.Schedule(0, new MoveCommand(Settlers, new[] { id }, 40, 20));
        WalkTo(sim, id, 40, 20, 60 * Tps, () =>
        {
            var p = At(sim, id);
            Assert.Equal(0, sim.Hazards.DamageAt(p.X, p.Y, p.Z));
        });
        Assert.Equal(full, Hp(sim, id));
    }

    // --- Sinkholes --------------------------------------------------------------------------------------

    /// <summary>
    /// A sinkhole's crust holds nothing up: the moment a unit stands on it the whole lid gives way, the units on it
    /// drop in and are hurt for <see cref="HazardSystem.SinkholeDamage"/>, and those beside it are not. The hole is
    /// a funnel, so even a soldier (who cannot dig) climbs back out.
    /// </summary>
    [Fact]
    public void ASinkholeGivesWayUnderAUnitAndItClimbsOut()
    {
        var sim = Flat();
        HazardSystem.MakeSinkhole(sim.World, 30, Ground, 30);
        Assert.Equal(BlockType.Crust, sim.World.Get(32, Ground, 30));
        Assert.Equal(BlockType.Air, sim.World.Get(30, Ground - 1, 30));
        Assert.True(BlockInfo.IsSolid(sim.World.Get(32, Ground - 1, 30)), "nothing hollow under its edge");

        int middle = sim.SpawnUnit(Settlers, "spearman", 30, 30);
        int edge = sim.SpawnUnit(Settlers, "spearman", 32, 30);
        int beside = sim.SpawnUnit(Settlers, "spearman", 34, 30);
        int full = Hp(sim, middle);
        sim.Run(Tps);

        for (int dz = -2; dz <= 2; dz++)
        for (int dx = -2; dx <= 2; dx++)
            Assert.NotEqual(BlockType.Crust, sim.World.Get(30 + dx, Ground, 30 + dz));
        Assert.Equal(full - HazardSystem.SinkholeDamage, Hp(sim, middle));
        Assert.Equal(full - HazardSystem.SinkholeDamage, Hp(sim, edge));
        Assert.Equal(full, Hp(sim, beside));
        Assert.Equal(Feet - 2, At(sim, middle).Y);
        Assert.Equal(Feet - 1, At(sim, edge).Y);
        Assert.Equal(Feet, At(sim, beside).Y);

        sim.Schedule(sim.Tick, new MoveCommand(Settlers, new[] { middle }, 40, 30));
        WalkTo(sim, middle, 40, 30, 30 * Tps);
        Assert.Equal(Feet, At(sim, middle).Y);
    }

    /// <summary>A unit on a move order walks around a sinkhole rather than over it, so the crust holds.</summary>
    [Fact]
    public void UnitsOnAMoveOrderWalkAroundASinkhole()
    {
        var sim = Flat();
        HazardSystem.MakeSinkhole(sim.World, 30, Ground, 20);
        int id = sim.SpawnUnit(Settlers, "spearman", 20, 20);
        sim.Schedule(0, new MoveCommand(Settlers, new[] { id }, 40, 20));
        WalkTo(sim, id, 40, 20, 60 * Tps);
        Assert.Equal(BlockType.Crust, sim.World.Get(30, Ground, 20));
        Assert.Equal(Feet, At(sim, id).Y);
    }

    /// <summary>
    /// The default map has its sinkholes: lids of crust over a hollow on grassland, forest and hill ground, none
    /// near a start, about <see cref="RegionGenerator.SinkholesPer512"/> per 512 by 512 blocks.
    /// </summary>
    [Fact]
    public void TheDefaultMapHasSinkholesAwayFromTheStarts()
    {
        int size = RegionGenerator.DefaultSize;
        var map = RegionGenerator.Generate(1234, size, RegionGenerator.DefaultHeight, size);
        var w = map.World;
        int hollow = 0;
        for (int z = 0; z < w.SizeZ; z++)
        for (int x = 0; x < w.SizeX; x++)
        {
            // The crust is the ground's top block, though a tree's crown may overhang it.
            int top = w.SurfaceHeight(x, z);
            while (top > 1 && w.Get(x, top, z) != BlockType.Crust && !BlockInfo.IsGround(w.Get(x, top, z))) top--;
            if (w.Get(x, top, z) != BlockType.Crust) continue;
            Assert.Contains(map.BiomeAt(x, z), new[] { Biome.Grassland, Biome.Forest, Biome.Hills, Biome.Wilds });
            foreach (var s in map.Starts)
                Assert.True(IntMath.Distance(x, z, s.X, s.Z) > RegionGenerator.StartResourceRadius, $"sinkhole at {x},{z} is near a start");
            if (w.Get(x, top - 1, z) == BlockType.Air) hollow++;
        }
        int sinkholes = hollow / 9; // each has a three by three hollow under its middle
        int want = RegionGenerator.SinkholesPer512 * size * size / (512 * 512);
        Assert.Equal(0, hollow % 9);
        Assert.InRange(sinkholes, want / 2, want);
        // The same for the same seed.
        var again = RegionGenerator.Generate(1234, size, RegionGenerator.DefaultHeight, size).World;
        for (int z = 0; z < w.SizeZ; z += 3)
        for (int x = 0; x < w.SizeX; x += 3)
            Assert.Equal(w.Get(x, w.SurfaceHeight(x, z), z), again.Get(x, again.SurfaceHeight(x, z), z));
    }

    // --- Alerts -------------------------------------------------------------------------------------------

    /// <summary>
    /// A player's unit killed by the ground alone tells its player (by unit and place); a wild monster's death
    /// tells no one.
    /// </summary>
    [Fact]
    public void AUnitKilledByTheGroundRaisesAnAlert()
    {
        var sim = Flat();
        sim.World.Set(30, Ground, 30, BlockType.Lava);
        sim.World.Set(55, Ground, 8, BlockType.Lava);
        int worker = sim.SpawnUnit(Settlers, "worker", 31, 30);
        int wolf = sim.SpawnUnit(Wild, "wolf", 54, 8);
        int def = sim.Units.Def[Index(sim, worker)];
        long seq = sim.Events.NextSeq - 1;
        for (int t = 0; t < 120 * Tps && (sim.Units.Exists(worker) || sim.Units.Exists(wolf)); t++) sim.Step();
        Assert.False(sim.Units.Exists(worker));
        Assert.False(sim.Units.Exists(wolf));
        var lost = sim.Events.After(seq).Where(e => e.Kind == GameEventKind.UnitLostToHazard).ToList();
        var e = Assert.Single(lost);
        Assert.Equal(Settlers, e.Player);
        Assert.Equal(worker, e.Subject);
        Assert.Equal(def, e.Def);
        Assert.Equal((31, Feet, 30), (e.X, e.Y, e.Z));
    }

    /// <summary>A unit killed by a sinkhole's fall is reported too.</summary>
    [Fact]
    public void AUnitKilledByASinkholeRaisesAnAlert()
    {
        var sim = Flat();
        HazardSystem.MakeSinkhole(sim.World, 30, Ground, 30);
        int id = sim.SpawnUnit(Settlers, "worker", 30, 30);
        sim.Units.Hp[Index(sim, id)] = HazardSystem.SinkholeDamage;
        long seq = sim.Events.NextSeq - 1;
        sim.Run(Tps);
        Assert.False(sim.Units.Exists(id));
        Assert.Contains(sim.Events.After(seq), e => e.Kind == GameEventKind.UnitLostToHazard && e.Subject == id);
    }
}
