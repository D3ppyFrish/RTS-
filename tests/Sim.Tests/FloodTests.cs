using System.Linq;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Floods: water let into a dug pit goes over the heads of the units in it. Each is swept out to the nearest
/// ground where its head is clear and hurt; one with no such ground near stays under and drowns.
/// </summary>
public class FloodTests
{
    private const int Ground = 3; // grass at 3 over stone; units stand at 4
    private const int Tps = Simulation.TicksPerSecond;
    private const int Settlers = 0;

    /// <summary>
    /// Flat ground with a pond one deep at x 10..15 (water at level 3, as high as the grass) and, east of it, a
    /// dry pit three deep (levels 1..3) from x 17 to <paramref name="pitX1"/> and z 1 to <paramref name="pitZ1"/>.
    /// Column x 16 is the bank between them.
    /// </summary>
    private static Simulation PondAndPit(int pitX1, int pitZ1, int pitZ0 = 1)
    {
        var w = TestWorlds.Flat(48, 16, 48, Ground);
        TestWorlds.Box(w, 10, Ground, 10, 15, Ground, 15, BlockType.Water);
        TestWorlds.Box(w, 17, 1, pitZ0, pitX1, Ground, pitZ1, BlockType.Air);
        return TestWorlds.Sim(w);
    }

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

    [Fact]
    public void AUnitInAFloodedPitIsSweptOutToTheEdgeAndHurt()
    {
        var sim = PondAndPit(21, 14, 10);
        int id = sim.SpawnUnit(Settlers, "worker", 19, 12);
        int i = Index(sim, id);
        Assert.Equal(1, sim.Units.Y[i]);
        int full = sim.Units.Hp[i];

        sim.Schedule(0, new DigCommand(Settlers, 16, Ground, 12)); // break the bank
        for (int t = 0; t < 30 * Tps && sim.World.Get(19, 2, 12) != BlockType.Water; t++) sim.Step();
        Assert.Equal(BlockType.Water, sim.World.Get(19, 2, 12));
        sim.Run(2);

        i = Index(sim, id);
        Assert.True(i >= 0, "the worker should have been swept out alive");
        Assert.Equal(full - PhysicsSystem.FloodDamage, sim.Units.Hp[i]);
        int x = sim.Units.X[i].FloorToInt(), z = sim.Units.Z[i].FloorToInt(), y = sim.Units.Y[i];
        Assert.True(Walk.CanStand(sim.World, x, y, z), "it stands where it was swept to");
        Assert.NotEqual(BlockType.Water, sim.World.Get(x, y + 1, z));
        Assert.InRange(System.Math.Max(System.Math.Abs(x - 19), System.Math.Abs(z - 12)), 1, PhysicsSystem.EscapeRange);

        // Out of the water (or wading in it), it is not hurt again.
        int hp = sim.Units.Hp[i];
        sim.Run(5 * Tps);
        Assert.Equal(hp, sim.Units.Hp[Index(sim, id)]);
    }

    [Fact]
    public void AUnitFarFromTheEdgeOfAFloodDrownsAndItsPlayerHears()
    {
        var sim = PondAndPit(40, 30);
        int id = sim.SpawnUnit(Settlers, "worker", 30, 16);
        long seq = sim.Events.NextSeq - 1;
        sim.Schedule(0, new DigCommand(Settlers, 16, Ground, 12));
        for (int t = 0; t < 90 * Tps && sim.Units.Exists(id); t++) sim.Step();

        Assert.False(sim.Units.Exists(id), "the worker should have drowned");
        var e = Assert.Single(sim.Events.After(seq), e => e.Kind == GameEventKind.UnitLostToHazard);
        Assert.Equal(id, e.Subject);
        Assert.Equal(BlockType.Water, sim.World.Get(e.X, e.Y + 1, e.Z));
    }

    /// <summary>A pit let into a pond fills to the brim (the pond's level), not only its floor.</summary>
    [Fact]
    public void ABreachedPitFillsToTheBrim()
    {
        var sim = PondAndPit(21, 14, 10);
        sim.Schedule(0, new DigCommand(Settlers, 16, Ground, 12));
        sim.Run(30 * Tps);
        for (int y = 1; y <= Ground; y++)
        for (int z = 10; z <= 14; z++)
        for (int x = 17; x <= 21; x++)
            Assert.Equal(BlockType.Water, sim.World.Get(x, y, z));
        Assert.Equal(BlockType.Air, sim.World.Get(19, Ground + 1, 12));
        Assert.Equal(0, sim.Physics.ActiveWater);
    }

    [Fact]
    public void AStrandedUnitUnderWaterLosesDrownDamageASecond()
    {
        var sim = PondAndPit(40, 30);
        Assert.Equal(0, sim.Hazards.DamageAt(30, 1, 16));
        TestWorlds.Box(sim.World, 17, 1, 1, 40, 2, 30, BlockType.Water);
        Assert.Equal(HazardSystem.DrownDamage, sim.Hazards.DamageAt(30, 1, 16));
        Assert.True(HazardSystem.IsHazard(sim.World, 30, 1, 16));
        // Wading (water at the feet only) is no hazard.
        Assert.Equal(0, sim.Hazards.DamageAt(30, 2, 16));
    }

    [Fact]
    public void FloodsComeOutTheSameEveryRun()
    {
        ulong Play()
        {
            var sim = PondAndPit(30, 20);
            for (int k = 0; k < 6; k++) sim.SpawnUnit(Settlers, "worker", 18 + 2 * k, 4 + 3 * k);
            sim.Schedule(3, new DigCommand(Settlers, 16, Ground, 12));
            sim.Run(40 * Tps);
            return sim.StateHash();
        }
        Assert.Equal(Play(), Play());
    }
}
