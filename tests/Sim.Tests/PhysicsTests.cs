using System;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

public class PhysicsTests
{
    private const int Ground = 3; // grass at 3 over stone; units stand at 4

    private static int Count(VoxelWorld w, BlockType b, int x0, int y0, int z0, int x1, int y1, int z1)
    {
        int n = 0;
        for (int y = y0; y <= y1; y++)
        for (int z = z0; z <= z1; z++)
        for (int x = x0; x <= x1; x++)
            if (w.Get(x, y, z) == b) n++;
        return n;
    }

    /// <summary>Blocks of any kind of rubble in the box.</summary>
    private static int CountRubble(VoxelWorld w, int x0, int y0, int z0, int x1, int y1, int z1)
    {
        int n = 0;
        for (int y = y0; y <= y1; y++)
        for (int z = z0; z <= z1; z++)
        for (int x = x0; x <= x1; x++)
            if (BlockInfo.IsRubble(w.Get(x, y, z))) n++;
        return n;
    }

    private static void RunUntil(Simulation sim, Func<bool> done, int maxTicks, string what)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return;
            sim.Step();
        }
        Assert.Fail($"Not done after {maxTicks} ticks: {what}");
    }

    /// <summary>
    /// A stone pillar at x 10, z 10 from level 4 to 8 with an arm along x from 11 to 14 at level 8: the arm
    /// hangs over open ground, held up only by the pillar.
    /// </summary>
    private static Simulation Ledge()
    {
        var w = TestWorlds.Flat(32, 16, 32, Ground);
        TestWorlds.Box(w, 10, 4, 10, 10, 8, 10, BlockType.Stone);
        TestWorlds.Box(w, 11, 8, 10, 14, 8, 10, BlockType.Stone);
        return TestWorlds.Sim(w);
    }

    [Fact]
    public void AnUnderminedLedgeFallsAsOnePieceAndLandsAsRubble()
    {
        var sim = Ledge();
        sim.Schedule(0, new DigCommand(0, 10, 4, 10)); // the foot of the pillar
        sim.Step();

        var falling = Assert.Single(sim.Physics.Falling);
        Assert.Equal(8, falling.Blocks.Count); // the rest of the pillar and the arm, together
        Assert.Equal(0, Count(sim.World, BlockType.Stone, 10, 4, 10, 14, 8, 10));

        int landed = 0;
        RunUntil(sim, () =>
        {
            landed += sim.Physics.Landings.Count;
            return sim.Physics.Falling.Count == 0;
        }, 100, "the ledge to land");
        Assert.Equal(1, landed);

        // It came down as a heap of stone rubble on the ground around the pillar: the pillar's blocks stack up
        // and slide off, and the arm's blocks drop onto the ground under them.
        Assert.Equal(8, Count(sim.World, BlockType.StoneRubble, 0, 0, 0, 31, 15, 31));
        Assert.Equal(0, CountRubble(sim.World, 0, 7, 0, 31, 15, 31));
        Assert.Equal(8, CountRubble(sim.World, 8, 4, 8, 16, 6, 12));
        for (int x = 11; x <= 14; x++) Assert.Equal(BlockType.StoneRubble, sim.World.Get(x, 4, 10));
        Assert.Equal(BlockType.StoneRubble, sim.World.Get(10, 4, 10));
    }

    [Fact]
    public void AFallTakesLongerFromHigherAndSpeedsUp()
    {
        var sim = Ledge();
        sim.Schedule(0, new DigCommand(0, 10, 4, 10));
        sim.Step();
        var s = sim.Physics.Falling[0];
        int ticks = 0, lastFall = s.Fall, lastStep = 0;
        bool faster = true;
        RunUntil(sim, () =>
        {
            if (sim.Physics.Falling.Count == 0) return true;
            int step = s.Fall - lastFall;
            faster &= step >= lastStep;
            (lastFall, lastStep) = (s.Fall, step);
            ticks++;
            return false;
        }, 100, "the ledge to land");
        Assert.True(faster);
        Assert.InRange(ticks, 4, 20); // 3 blocks (1.5 m) at about 1 g: a bit over half a second
    }

    [Fact]
    public void CavesAndOverhangsTheMapWasMadeWithStandUntilSomeoneDigsAtThem()
    {
        var w = TestWorlds.Flat(32, 16, 32, Ground);
        TestWorlds.Box(w, 5, 9, 5, 8, 9, 8, BlockType.Stone); // floating: nothing holds it up
        TestWorlds.Box(w, 20, 9, 20, 23, 9, 23, BlockType.Stone);
        var sim = TestWorlds.Sim(w);
        sim.Run(40);
        Assert.Empty(sim.Physics.Falling);
        Assert.Equal(16, Count(sim.World, BlockType.Stone, 5, 9, 5, 8, 9, 8));

        // Digging a block out of one makes it fall; the other stays.
        sim.Schedule(sim.Tick, new DigCommand(0, 5, 9, 5));
        sim.Step();
        Assert.Equal(15, Assert.Single(sim.Physics.Falling).Blocks.Count);
        sim.Run(60);
        Assert.Equal(16, Count(sim.World, BlockType.Stone, 20, 9, 20, 23, 9, 23));
        Assert.Equal(15, CountRubble(sim.World, 0, 0, 0, 31, 15, 31));
    }

    [Fact]
    public void GroundStillJoinedToTheHillDoesNotFall()
    {
        var sim = Ledge();
        // An arch: the arm's far end also rests on a second pillar, so taking the first pillar out leaves it held.
        TestWorlds.Box(sim.World, 14, 4, 10, 14, 7, 10, BlockType.Stone);
        sim.Schedule(0, new DigCommand(0, 10, 4, 10));
        sim.Run(30);
        Assert.Empty(sim.Physics.Falling);
        Assert.Equal(0, CountRubble(sim.World, 0, 0, 0, 31, 15, 31));
        Assert.Equal(BlockType.Stone, sim.World.Get(10, 8, 10));
    }

    [Fact]
    public void APieceTooBigToBreakOffIsHeldUpByItsBulk()
    {
        var w = TestWorlds.Flat(48, 24, 48, Ground);
        // A slab of 20 x 3 x 20 = 1200 blocks on one stone pillar.
        TestWorlds.Box(w, 20, 4, 20, 20, 12, 20, BlockType.Stone);
        TestWorlds.Box(w, 10, 13, 10, 29, 15, 29, BlockType.Stone);
        var sim = TestWorlds.Sim(w);
        sim.Schedule(0, new DigCommand(0, 20, 4, 20));
        sim.Run(5);
        // The pillar's last few blocks fall, but not the slab.
        Assert.True(PhysicsSystem.MaxSection < 1200);
        Assert.Equal(1200, Count(sim.World, BlockType.Stone, 10, 13, 10, 29, 15, 29));
    }

    [Fact]
    public void ATreeOnAPieceThatBreaksOffGoesWithIt()
    {
        var sim = Ledge();
        TestWorlds.Tree(sim.World, 13, 10, 3, ground: 8);
        sim.Schedule(0, new DigCommand(0, 10, 4, 10));
        sim.Run(40);
        Assert.Equal(0, Count(sim.World, BlockType.Wood, 0, 0, 0, 31, 15, 31));
    }

    [Fact]
    public void FallingGroundHurtsTheUnitsItLandsOnAndTheyClimbOut()
    {
        var sim = Ledge();
        int under = sim.SpawnUnit(0, "spearman", 12, 10); // under the arm
        int beside = sim.SpawnUnit(0, "spearman", 12, 12); // two columns away
        int hp = sim.Units.Hp[sim.Units.IndexOf(under)];
        sim.Schedule(0, new DigCommand(0, 10, 4, 10));
        sim.Run(40);

        int i = sim.Units.IndexOf(under);
        Assert.True(i >= 0);
        Assert.Equal(hp - PhysicsSystem.CrushDamage, sim.Units.Hp[i]);
        // It is out of the heap and standing on open ground.
        int x = sim.Units.X[i].FloorToInt(), z = sim.Units.Z[i].FloorToInt();
        Assert.True(Walk.CanStand(sim.World, x, sim.Units.Y[i], z));
        Assert.Equal(hp, sim.Units.Hp[sim.Units.IndexOf(beside)]);
    }

    [Fact]
    public void UnitsWalkOverRubbleAtHalfSpeed()
    {
        int Crossing(BlockType ground)
        {
            var w = TestWorlds.Flat(48, 16, 16, Ground);
            // A band of rubble (or plain stone) laid as the ground across the way, 12 blocks wide.
            TestWorlds.Box(w, 16, Ground, 0, 27, Ground, 15, ground);
            var sim = TestWorlds.Sim(w);
            int unit = sim.SpawnUnit(0, "worker", 14, 8);
            sim.Schedule(0, new MoveCommand(0, new[] { unit }, 30, 8));
            int t = 0;
            RunUntil(sim, () => { t++; return sim.Units.X[sim.Units.IndexOf(unit)].FloorToInt() >= 29; }, 2000, "the crossing");
            Assert.True(Walk.CanStand(sim.World, 20, Ground + 1, 8));
            return t;
        }
        int plain = Crossing(BlockType.Stone), rough = Crossing(BlockType.StoneRubble);
        Assert.InRange(rough, plain * 3 / 2, plain * 5 / 2);
    }

    [Fact]
    public void RubbleRemembersWhatItWasMadeOf()
    {
        Assert.Equal(BlockType.StoneRubble, BlockInfo.RubbleOf(BlockType.Stone));
        Assert.Equal(BlockType.StoneRubble, BlockInfo.RubbleOf(BlockType.StoneBrick));
        Assert.Equal(BlockType.OreRubble, BlockInfo.RubbleOf(BlockType.IronOre));
        Assert.Equal(BlockType.CoalRubble, BlockInfo.RubbleOf(BlockType.Coal));
        Assert.Equal(BlockType.WoodRubble, BlockInfo.RubbleOf(BlockType.Planks));
        Assert.Equal(BlockType.Rubble, BlockInfo.RubbleOf(BlockType.Soil));
        Assert.Equal(BlockType.Rubble, BlockInfo.RubbleOf(BlockType.Thatch));

        // A ledge of iron ore comes down as ore rubble.
        var w = TestWorlds.Flat(32, 16, 32, Ground);
        TestWorlds.Box(w, 10, 4, 10, 10, 5, 10, BlockType.Stone);
        TestWorlds.Box(w, 11, 5, 10, 12, 5, 10, BlockType.IronOre);
        var sim = TestWorlds.Sim(w);
        sim.Schedule(0, new DigCommand(0, 10, 4, 10));
        sim.Run(40);
        Assert.Equal(2, Count(sim.World, BlockType.OreRubble, 0, 0, 0, 31, 15, 31));
        Assert.Equal(1, Count(sim.World, BlockType.StoneRubble, 0, 0, 0, 31, 15, 31));
    }

    [Fact]
    public void WorkersClearRubbleForWhatItWasMadeOf()
    {
        var w = TestWorlds.Flat(48, 16, 48, Ground);
        TestWorlds.Box(w, 30, Ground + 1, 30, 31, Ground + 1, 31, BlockType.StoneRubble);
        TestWorlds.Box(w, 30, Ground + 1, 34, 31, Ground + 1, 34, BlockType.WoodRubble);
        var sim = TestWorlds.Sim(w);
        var store = sim.Economy.PlaceFinished(0, sim.Content.Building("storehouse"), 10, 10)!;
        int a = sim.SpawnUnit(0, "worker", 20, 20), b = sim.SpawnUnit(0, "worker", 20, 22);
        sim.Schedule(0, new GatherCommand(0, new[] { a }, 30, Ground + 1, 30));
        sim.Schedule(0, new GatherCommand(0, new[] { b }, 30, Ground + 1, 34));
        RunUntil(sim, () => sim.Tick > 0 && sim.Units.Job[sim.Units.IndexOf(a)] == Units.UnitJob.None
            && sim.Units.Job[sim.Units.IndexOf(b)] == Units.UnitJob.None, 4000, "the workers to clear the rubble");
        Assert.Equal(0, CountRubble(sim.World, 0, 0, 0, 47, 15, 47));
        Assert.Equal(4, store.Stock[(int)Economy.Good.Stone]);
        Assert.Equal(2, store.Stock[(int)Economy.Good.Wood]);
    }

    [Fact]
    public void ABigPieceThatFallsFarBreaksApartAndScatters()
    {
        var w = TestWorlds.Flat(48, 24, 48, Ground);
        // A 4 x 4 slab, two thick, hanging eight blocks up from a pillar at its corner.
        TestWorlds.Box(w, 20, 4, 20, 20, 13, 20, BlockType.Stone);
        TestWorlds.Box(w, 21, 12, 21, 24, 13, 24, BlockType.Stone);
        TestWorlds.Box(w, 21, 13, 20, 21, 13, 20, BlockType.Stone); // joins it to the pillar
        var sim = TestWorlds.Sim(w);
        sim.Schedule(0, new DigCommand(0, 21, 13, 20));
        sim.Step();
        var slab = Assert.Single(sim.Physics.Falling);
        Assert.Equal(32, slab.Blocks.Count);
        int most = 0;
        bool broke = false;
        RunUntil(sim, () =>
        {
            foreach (var l in sim.Physics.Landings) broke |= l.Broke;
            most = Math.Max(most, sim.Physics.Falling.Count);
            return sim.Physics.Falling.Count == 0;
        }, 300, "the slab to come down");
        Assert.True(broke);
        Assert.True(most >= 4, $"it broke into {most} pieces");
        Assert.Equal(32, Count(sim.World, BlockType.StoneRubble, 0, 0, 0, 47, 23, 47));
        // The pieces flew apart: the rubble lies wider than the slab did.
        int minX = 99, maxX = -1;
        for (int x = 0; x < 48; x++)
            if (CountRubble(sim.World, x, 0, 0, x, 23, 47) > 0) { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); }
        Assert.True(maxX - minX + 1 > 4, $"rubble from x {minX} to {maxX}");
    }

    [Fact]
    public void ASmallPieceOrAShortDropDoesNotBreak()
    {
        var sim = Ledge(); // eight blocks, but it drops only one block before it lands
        sim.Schedule(0, new DigCommand(0, 10, 4, 10));
        bool broke = false;
        RunUntil(sim, () =>
        {
            foreach (var l in sim.Physics.Landings) broke |= l.Broke;
            return sim.Tick > 2 && sim.Physics.Falling.Count == 0;
        }, 100, "the ledge to land");
        Assert.False(broke);
    }

    [Fact]
    public void AHouseShedsDebrisAsItIsHitAndCrumblesWhenItFalls()
    {
        var w = TestWorlds.Flat(64, 16, 64, Ground);
        var sim = TestWorlds.Sim(w);
        var house = sim.Economy.PlaceFinished(0, sim.Content.Building("house"), 20, 20)!;
        var spears = new int[4];
        for (int k = 0; k < spears.Length; k++) spears[k] = sim.SpawnUnit(1, "spearman", 18, 20 + k);
        sim.Schedule(0, new AttackBuildingCommand(1, spears, house.Id));
        int debrisBefore = -1, most = 0;
        RunUntil(sim, () =>
        {
            if (sim.Physics.Falling.Count > 0 && debrisBefore < 0 && sim.Buildings.Get(house.Id) != null)
                debrisBefore = sim.Physics.Falling.Count; // a block knocked out flies off while it still stands
            most = Math.Max(most, sim.Physics.Falling.Count);
            return sim.Buildings.Get(house.Id) == null;
        }, 120 * Simulation.TicksPerSecond, "the house to fall");
        Assert.True(debrisBefore > 0);
        RunUntil(sim, () => sim.Physics.Falling.Count == 0, 400, "the house to come down");
        Assert.True(most >= 3, $"only {most} pieces at once");

        // Nothing of it stands, and its heaps of rubble give back its timber and stone.
        for (int x = 20; x < 20 + house.Def.SizeX; x++)
        for (int z = 20; z < 20 + house.Def.SizeZ; z++)
        for (int y = house.Y; y <= house.Y + house.Def.Height; y++)
            Assert.False(BlockInfo.IsBuilt(sim.World.Get(x, y, z)) && sim.World.Get(x, y, z) != BlockType.Farmland);
        Assert.True(Count(sim.World, BlockType.WoodRubble, 0, 0, 0, 63, 15, 63) > 0);
        Assert.True(CountRubble(sim.World, 0, 0, 0, 63, 15, 63) > 0);
        // It came down in heaps, no higher than it stood, and some of it fell outside its walls.
        Assert.Equal(0, CountRubble(sim.World, 0, house.Y + house.Def.Height, 0, 63, 15, 63));
        int inside = CountRubble(sim.World, 20, 0, 20, 19 + house.Def.SizeX, 15, 19 + house.Def.SizeZ);
        Assert.True(CountRubble(sim.World, 0, 0, 0, 63, 15, 63) > inside);
    }

    /// <summary>A pond 6 x 6 and one deep (water at level 3) in flat ground.</summary>
    private static Simulation Pond()
    {
        var w = TestWorlds.Flat(48, 16, 48, Ground);
        TestWorlds.Box(w, 10, Ground, 10, 15, Ground, 15, BlockType.Water);
        return TestWorlds.Sim(w);
    }

    [Fact]
    public void WaterFillsAChannelDugFromALakeButNoHigher()
    {
        var sim = Pond();
        // A channel 8 long out of the pond's east side, as deep as the pond, ending in a hole two deeper.
        for (int x = 16; x <= 23; x++) sim.Schedule(0, new DigCommand(0, x, Ground, 12));
        sim.Schedule(0, new DigCommand(0, 23, Ground - 1, 12));
        sim.Schedule(0, new DigCommand(0, 23, Ground - 2, 12));
        RunUntil(sim, () => sim.World.Get(23, Ground - 2, 12) == BlockType.Water, 400, "the channel to fill");

        for (int x = 16; x <= 23; x++)
        {
            Assert.Equal(BlockType.Water, sim.World.Get(x, Ground, 12));
            Assert.Equal(BlockType.Air, sim.World.Get(x, Ground + 1, 12));
        }
        Assert.Equal(BlockType.Water, sim.World.Get(23, Ground - 1, 12));
        Assert.Equal(BlockType.Water, sim.World.Get(23, Ground - 2, 12));
        sim.Run(100);
        Assert.Equal(0, sim.Physics.ActiveWater);
        Assert.Equal(0, Count(sim.World, BlockType.Water, 0, Ground + 1, 0, 47, 15, 47));
        Assert.Equal(0, Count(sim.World, BlockType.Water, 24, 0, 0, 47, 15, 47));
    }

    [Fact]
    public void WaterFlowsABlockAtATimeDownFirst()
    {
        var sim = Pond();
        // A shaft two deep right beside the pond, under a block of ground left in place at the pond's level.
        sim.Schedule(0, new DigCommand(0, 16, Ground, 12));
        sim.Schedule(0, new DigCommand(0, 16, Ground - 1, 12));
        sim.Schedule(0, new DigCommand(0, 17, Ground - 1, 12));
        sim.Step();
        int first = -1, below = -1, side = -1;
        RunUntil(sim, () =>
        {
            if (first < 0 && sim.World.Get(16, Ground, 12) == BlockType.Water) first = (int)sim.Tick;
            if (below < 0 && sim.World.Get(16, Ground - 1, 12) == BlockType.Water) below = (int)sim.Tick;
            if (side < 0 && sim.World.Get(17, Ground - 1, 12) == BlockType.Water) side = (int)sim.Tick;
            return side >= 0;
        }, 200, "the water to reach the side of the shaft");
        Assert.True(first < below && below < side);
        Assert.Equal(PhysicsSystem.WaterTicks, below - first);
    }

    [Fact]
    public void CollapsesAndWaterComeOutTheSameEveryRun()
    {
        ulong Play()
        {
            var w = TestWorlds.Flat(48, 16, 48, Ground);
            TestWorlds.Box(w, 10, Ground, 10, 15, Ground, 15, BlockType.Water);
            TestWorlds.Box(w, 30, 4, 30, 30, 8, 30, BlockType.Stone);
            TestWorlds.Box(w, 31, 8, 28, 34, 9, 33, BlockType.Stone);
            var sim = TestWorlds.Sim(w);
            sim.SpawnUnit(0, "worker", 32, 30);
            for (int x = 16; x <= 22; x++) sim.Schedule(2, new DigCommand(0, x, Ground, 13));
            sim.Schedule(5, new DigCommand(0, 30, 4, 30));
            sim.Run(200);
            Assert.True(CountRubble(sim.World, 20, 0, 20, 44, 15, 44) > 0);
            Assert.Equal(BlockType.Water, sim.World.Get(22, Ground, 13));
            return sim.StateHash();
        }
        Assert.Equal(Play(), Play());
    }
}
