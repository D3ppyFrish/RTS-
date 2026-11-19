using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Issue #78: quicksand on the beaches and river banks, and computer players that keep their workers off hazard
/// ground when they choose where to gather and where to build.
/// </summary>
public class QuicksandTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Tps = Simulation.TicksPerSecond;
    private const int Settlers = 0;

    private static Simulation Flat(int size = 64) => TestWorlds.Sim(TestWorlds.Flat(size, 16, size, Ground));

    private static int Hp(Simulation sim, int id) => sim.Units.Exists(id) ? sim.Units.Hp[sim.Units.IndexOf(id)] : 0;
    private static int Col(Simulation sim, int id) => sim.Units.X[sim.Units.IndexOf(id)].FloorToInt();

    private static void Fill(Simulation sim, int x1, int z1, int x2, int z2, BlockType b)
    {
        for (int z = z1; z <= z2; z++)
        for (int x = x1; x <= x2; x++)
            sim.World.Set(x, Ground, z, b);
    }

    /// <summary>A unit standing on quicksand loses <see cref="HazardSystem.QuicksandDamage"/> a second; one beside it loses nothing.</summary>
    [Fact]
    public void QuicksandHurtsTheUnitsStandingOnIt()
    {
        var sim = Flat();
        Fill(sim, 28, 28, 32, 32, BlockType.Quicksand);
        int on = sim.SpawnUnit(Settlers, "spearman", 30, 30);
        int beside = sim.SpawnUnit(Settlers, "spearman", 36, 30);
        int full = Hp(sim, on);
        sim.Run(10 * Tps);
        Assert.Equal(full - 10 * HazardSystem.QuicksandDamage, Hp(sim, on));
        Assert.Equal(full, Hp(sim, beside));
        Assert.True(HazardSystem.IsHazard(sim.World, 30, Ground + 1, 30));
        Assert.False(HazardSystem.IsHazard(sim.World, 36, Ground + 1, 30));
    }

    /// <summary>Quicksand grips: a unit that has to cross a band of it (there is no way round) takes far longer than over grass.</summary>
    [Fact]
    public void UnitsCrossQuicksandSlowly()
    {
        int Crossing(bool quicksand)
        {
            var sim = Flat();
            if (quicksand) Fill(sim, 26, 0, 37, 63, BlockType.Quicksand);
            int id = sim.SpawnUnit(Settlers, "spearman", 20, 30);
            sim.Schedule(0, new MoveCommand(Settlers, new[] { id }, 44, 30));
            for (int t = 0; t < 120 * Tps; t++)
            {
                if (Col(sim, id) >= 44) return t;
                sim.Step();
            }
            Assert.Fail("the unit never got across");
            return -1;
        }
        int plain = Crossing(false), slow = Crossing(true);
        // Twelve of the twenty-four columns are quicksand, crossed at a third of the speed: about twice as long.
        Assert.True(slow >= plain * 17 / 10, $"{slow} ticks over quicksand against {plain} over grass");
    }

    /// <summary>A unit on a move order walks round a patch of quicksand where there is another way.</summary>
    [Fact]
    public void UnitsOnAMoveOrderWalkAroundQuicksand()
    {
        var sim = Flat();
        Fill(sim, 29, 14, 31, 26, BlockType.Quicksand);
        int id = sim.SpawnUnit(Settlers, "spearman", 20, 20);
        sim.Schedule(0, new MoveCommand(Settlers, new[] { id }, 40, 20));
        int onSand = 0;
        for (int t = 0; t < 60 * Tps && Col(sim, id) != 40; t++)
        {
            int i = sim.Units.IndexOf(id);
            if (sim.World.Get(Col(sim, id), sim.Units.Y[i] - 1, sim.Units.Z[i].FloorToInt()) == BlockType.Quicksand) onSand++;
            sim.Step();
        }
        Assert.Equal(40, Col(sim, id));
        Assert.True(onSand <= 2 * Tps, $"on the quicksand for {onSand} ticks");
    }

    /// <summary>Workers dig quicksand away like sand; it is natural ground that falls when undercut.</summary>
    [Fact]
    public void QuicksandIsDugLikeSand()
    {
        Assert.True(Harvest.CanGather(BlockType.Quicksand));
        Assert.False(Harvest.Yields(BlockType.Quicksand, out _));
        Assert.True(BlockInfo.CanFall(BlockType.Quicksand));
        Assert.True(BlockInfo.IsGround(BlockType.Quicksand));
        Assert.True(BlockInfo.CanStandOn(BlockType.Quicksand));
    }

    /// <summary>
    /// The default map has quicksand on dry sand at the water's edge (beaches and river banks), none near a
    /// start, in several patches; the same for the same seed.
    /// </summary>
    [Fact]
    public void TheDefaultMapHasQuicksandByTheWater()
    {
        int size = RegionGenerator.DefaultSize;
        var map = RegionGenerator.Generate(1234, size, RegionGenerator.DefaultHeight, size);
        var w = map.World;
        var columns = new List<(int X, int Z)>();
        for (int z = 0; z < w.SizeZ; z++)
        for (int x = 0; x < w.SizeX; x++)
        {
            int top = w.SurfaceHeight(x, z);
            if (top < 0 || w.Get(x, top, z) != BlockType.Quicksand) continue;
            columns.Add((x, z));
            Assert.Equal(BlockType.Air, w.Get(x, top + 1, z));
            foreach (var s in map.Starts)
                Assert.True(IntMath.Distance(x, z, s.X, s.Z) > RegionGenerator.StartResourceRadius, $"quicksand at {x},{z} is near a start");
            bool water = false;
            for (int dz = -6; dz <= 6 && !water; dz++)
            for (int dx = -6; dx <= 6 && !water; dx++)
                water = w.Get(x + dx, top, z + dz) == BlockType.Water || w.Get(x + dx, top - 1, z + dz) == BlockType.Water;
            Assert.True(water, $"quicksand at {x},{z} is not by the water");
        }
        // Patches: count the columns with no quicksand column before them in the patch's search order.
        int patches = columns.Count(c => !columns.Any(o => (o.X != c.X || o.Z != c.Z)
            && Math.Abs(o.X - c.X) <= 2 * RegionGenerator.QuicksandRadius && Math.Abs(o.Z - c.Z) <= 2 * RegionGenerator.QuicksandRadius
            && (o.Z < c.Z || o.Z == c.Z && o.X < c.X)));
        int want = RegionGenerator.QuicksandPer512 * size * size / (512 * 512);
        Assert.InRange(patches, want / 2, 2 * want);
        var again = RegionGenerator.Generate(1234, size, RegionGenerator.DefaultHeight, size).World;
        foreach (var (x, z) in columns) Assert.Equal(BlockType.Quicksand, again.Get(x, again.SurfaceHeight(x, z), z));
    }

    /// <summary>A building's site and the ring round it where its builders stand count as near a hazard if any column there is hazard ground.</summary>
    [Fact]
    public void ASiteIsNearAHazardIfItsBuildersWouldStandOnOne()
    {
        var sim = Flat();
        Assert.False(HazardSystem.HazardNear(sim.World, 20, 20, 4, 4));
        sim.World.Set(24, Ground, 22, BlockType.Quicksand); // just past its east side, where a builder stands
        Assert.True(HazardSystem.HazardNear(sim.World, 20, 20, 4, 4));
        Assert.False(HazardSystem.HazardNear(sim.World, 14, 20, 4, 4));
        sim.World.Set(15, Ground, 30, BlockType.Bog);
        Assert.True(HazardSystem.HazardNear(sim.World, 14, 26, 4, 4));
        sim.World.Set(40, Ground, 40, BlockType.Lava); // the lava's heat reaches a column further
        Assert.True(HazardSystem.HazardNear(sim.World, 42, 38, 4, 4));
        Assert.False(HazardSystem.HazardNear(sim.World, 43, 38, 4, 4));
    }

    /// <summary>
    /// The computer's wood cutters go to the nearest trees it can work without standing in a hazard: a grove whose
    /// ground is quicksand is passed over for one further off on firm ground.
    /// </summary>
    [Fact]
    public void TheComputerSendsWoodCuttersPastTreesInQuicksand()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(128, 16, 128, Ground));
        sim.Map.Starts.Add(new StartArea(0, 20, Ground + 1, 64));
        sim.PlaceStartingBuildings();
        Fill(sim, 84, 52, 96, 76, BlockType.Quicksand);
        for (int k = 0; k < 6; k++)
        {
            TestWorlds.Tree(sim.World, 90, 58 + 2 * k, 4, Ground);
            TestWorlds.Tree(sim.World, 112, 58 + 2 * k, 4, Ground);
        }
        var hall = sim.Economy.TownHall(0)!;
        hall.Stock[(int)Good.Wood] = 0;
        var workers = new List<int>();
        for (int k = 0; k < 4; k++) workers.Add(sim.SpawnUnit(0, "worker", 18 + k, hall.MaxZ + 2));
        var ai = new AiPlayer(0, 1);

        int SentTo(int x) => workers.Count(id =>
        {
            int i = sim.Units.IndexOf(id);
            return sim.Units.Job[i] == UnitJob.Gather && sim.Units.JobBlock[i] == BlockType.Wood && sim.Units.JobX[i] == x;
        });
        for (int t = 0; t < 10 * Tps && SentTo(112) == 0; t++)
        {
            ai.Think(sim, c => sim.Schedule(sim.Tick, c));
            sim.Step();
        }
        Assert.True(SentTo(112) > 0, "wood cutters to the trees on firm ground");
        Assert.Equal(0, SentTo(90));
    }
}
