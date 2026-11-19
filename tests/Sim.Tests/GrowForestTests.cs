using System.Collections.Generic;
using System.Linq;
using Rts.Sim.Economy;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Terrain magic: the mage's Grow Forest.</summary>
public class GrowForestTests
{
    private const int Ground = 3; // flat test worlds: stone at 1 and 2, grass at 3, units stand at 4
    private const int Stand = Ground + 1;
    private const int Tps = Simulation.TicksPerSecond;
    private const int P = 0;

    private static Simulation Flat(int treeScale = 1, System.Action<VoxelWorld>? setup = null)
    {
        var w = TestWorlds.Flat(64, 32, 64, Ground);
        w.TreeScale = treeScale;
        setup?.Invoke(w);
        return TestWorlds.Sim(w);
    }

    private static int Spawn(Simulation sim, int owner, string unit, int x, int z) => sim.SpawnUnit(owner, unit, x, z, Stand);

    private static void Grow(Simulation sim, int[] units, int x1, int z1, int x2, int z2)
    {
        sim.Schedule(sim.Tick, new GrowForestCommand(P, units, x1, z1, x2, z2));
        sim.Step();
    }

    private static bool Trunk(Simulation sim, int x, int z) => BlockInfo.IsTrunk(sim.World.Get(x, Stand, z));

    [Fact]
    public void AMageGrowsARowOfTreesWithGapsBetween()
    {
        var sim = Flat();
        int mage = Spawn(sim, P, "mage", 20, 30);
        Grow(sim, new[] { mage }, 26, 27, 26, 33);
        // Every other column of the line takes a full-grown tree: a trunk from the ground up, a crown on top.
        for (int z = 27; z <= 33; z++)
            Assert.Equal((z - 27) % 2 == 0, Trunk(sim, 26, z));
        Assert.False(Trunk(sim, 26, 26));
        Assert.False(Trunk(sim, 26, 34));
        for (int y = Stand; y < Stand + 4; y++) Assert.Equal(BlockType.Wood, sim.World.Get(26, y, 27));
        Assert.True(Enumerable.Range(Stand + 3, 6).Any(y => BlockInfo.IsLeaves(sim.World.Get(25, y, 27))), "it has a crown");
        Assert.Equal(4, sim.Spells.GroveTrees);
        Assert.Equal(sim.Tick - 1, sim.Spells.LastGrove);
        // The gaps between trunks are open to walk through.
        Assert.True(Walk.CanStand(sim.World, 26, Stand, 28));
    }

    [Fact]
    public void TheTreesStay()
    {
        var sim = Flat();
        int mage = Spawn(sim, P, "mage", 20, 30);
        Grow(sim, new[] { mage }, 26, 27, 26, 33);
        sim.Run(5 * 60 * Tps);
        Assert.True(Trunk(sim, 26, 27));
        Assert.True(Trunk(sim, 26, 33));
    }

    [Fact]
    public void AGroveIsAsLongAsAnEarthWall()
    {
        var sim = Flat();
        int mage = Spawn(sim, P, "mage", 20, 30);
        Grow(sim, new[] { mage }, 26, 20, 26, 40);
        int trees = Enumerable.Range(10, 40).Count(z => Trunk(sim, 26, z));
        Assert.Equal(SpellSystem.MaxLength / 2, trees);
    }

    [Fact]
    public void BigTreesGrowOnTheTreeGrid()
    {
        var sim = Flat(treeScale: 2);
        int mage = Spawn(sim, P, "mage", 20, 30);
        Grow(sim, new[] { mage }, 26, 24, 26, 31);
        // The line's 8 columns cross 4 squares of the grid, 2 by 2; trees take the first and the third.
        foreach (var (x, z) in new[] { (26, 24), (27, 24), (26, 25), (27, 25), (26, 28), (27, 29) })
            Assert.True(Trunk(sim, x, z), $"trunk at {x},{z}");
        Assert.False(Trunk(sim, 26, 26));
        Assert.False(Trunk(sim, 26, 30));
        Assert.Equal(2, sim.Spells.GroveTrees);
    }

    [Fact]
    public void TrunksStopArrows()
    {
        var sim = Flat();
        int mage = Spawn(sim, P, "mage", 22, 30);
        Grow(sim, new[] { mage }, 30, 27, 30, 33);
        Assert.True(Trunk(sim, 30, 29));
        var c = sim.Combat;
        Fix64 Mid(int v) => Fix64.FromInt(v) + Fix64.Half;
        Assert.False(c.ClearLine(Mid(26), Stand, Mid(29), Mid(34), Stand, Mid(29)), "a trunk in the way");
        Assert.True(c.ClearLine(Mid(26), Stand, Mid(28), Mid(34), Stand, Mid(28)), "through the gap");
    }

    [Fact]
    public void NotOnAUnitWaterOrABuilding()
    {
        var sim = Flat(setup: w => w.Set(26, Ground, 31, BlockType.Water));
        int mage = Spawn(sim, P, "mage", 20, 30);
        Spawn(sim, P, "spearman", 26, 29);
        Grow(sim, new[] { mage }, 26, 27, 26, 33);
        Assert.True(Trunk(sim, 26, 27));
        Assert.False(Trunk(sim, 26, 29));
        Assert.False(Trunk(sim, 26, 31));
        Assert.True(Trunk(sim, 26, 33));

        var plan = new List<(int X, int Z, int Y, bool Room)>();
        var house = sim.Economy.PlaceFinished(P, sim.Content.Building("house"), 40, 40);
        Assert.NotNull(house);
        sim.Spells.PlanGrove(house!.X, house.Z, house.X, house.Z, plan);
        Assert.Single(plan);
        Assert.False(plan[0].Room);
    }

    [Fact]
    public void ALineWithNoRoomCostsNothing()
    {
        var sim = Flat(setup: w =>
        {
            for (int z = 26; z <= 34; z++) w.Set(26, Ground, z, BlockType.Water);
        });
        int mage = Spawn(sim, P, "mage", 20, 30);
        Grow(sim, new[] { mage }, 26, 28, 26, 32);
        Assert.Equal(0, sim.Spells.GroveTrees);
        Assert.True(sim.Spells.CanCast(sim.Units.IndexOf(mage), Spell.GrowForest));
    }

    [Fact]
    public void GrowingAForestMakesTheMageWaitForEverySpell()
    {
        var sim = Flat();
        int mage = Spawn(sim, P, "mage", 20, 30);
        Grow(sim, new[] { mage }, 26, 27, 26, 33);
        int i = sim.Units.IndexOf(mage);
        Assert.False(sim.Spells.CanCast(i, Spell.GrowForest));
        Assert.False(sim.Spells.CanCast(i, Spell.Chasm));
        sim.Run(SpellSystem.CooldownTicks);
        Assert.True(sim.Spells.CanCast(sim.Units.IndexOf(mage), Spell.GrowForest));
    }

    [Fact]
    public void MagesTogetherGrowAStaggeredGrove()
    {
        var sim = Flat();
        int a = Spawn(sim, P, "mage", 20, 30), b = Spawn(sim, P, "mage", 20, 31);
        Grow(sim, new[] { a, b }, 26, 27, 26, 33);
        // The line's row, and a second row two columns west with its trees on the first row's gaps.
        Assert.True(Trunk(sim, 26, 27));
        Assert.True(Trunk(sim, 24, 28));
        Assert.False(Trunk(sim, 24, 27));
        Assert.False(Trunk(sim, 25, 28));
        int trees = sim.Spells.GroveTrees;
        Assert.True(trees > SpellSystem.MaxLength / 2, $"more trees than one mage grows: {trees}");
    }

    [Fact]
    public void AMageOutOfRangeWalksUpFirst()
    {
        var sim = Flat();
        int mage = Spawn(sim, P, "mage", 5, 30);
        Grow(sim, new[] { mage }, 30, 27, 30, 33);
        Assert.Equal(0, sim.Spells.GroveTrees);
        Assert.True(sim.Spells.Casting(mage));
        sim.Run(15 * Tps);
        Assert.True(sim.Spells.GroveTrees > 0);
    }

    [Fact]
    public void AGrownTreeIsFelledForWood()
    {
        var sim = Flat();
        var hall = sim.Economy.PlaceFinished(P, sim.Content.Building("town_hall"), 4, 4)!;
        int mage = Spawn(sim, P, "mage", 20, 20);
        Grow(sim, new[] { mage }, 20, 26, 20, 26);
        Assert.True(Trunk(sim, 20, 26));
        int wood = hall.Stock[(int)Good.Wood];
        int cutter = sim.SpawnUnit(P, "worker", 16, 16);
        sim.Schedule(sim.Tick, new GatherCommand(P, new[] { cutter }, 20, Stand, 26));
        sim.Run(90 * Tps);
        Assert.True(hall.Stock[(int)Good.Wood] > wood, "the grown tree gave wood");
    }

    [Fact]
    public void TheSameGrovesGiveTheSameState()
    {
        ulong Play()
        {
            var sim = Flat(treeScale: 2);
            int a = Spawn(sim, P, "mage", 5, 30), b = Spawn(sim, P, "mage", 20, 20);
            Spawn(sim, P, "spearman", 22, 25);
            sim.Schedule(0, new GrowForestCommand(P, new[] { a }, 30, 28, 30, 36));
            sim.Schedule(10, new GrowForestCommand(P, new[] { b }, 18, 25, 26, 25));
            sim.Run(30 * Tps);
            return sim.StateHash();
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void TheCommandRoundTripsThroughTheLog()
    {
        var log = CommandLog.Parse("rts-log 1\nseed 1\nworld 32 16 32\nticks 10\n5 growforest 0 3 4 9 4 7,8\n");
        Assert.Equal(new GrowForestCommand(0, new[] { 7, 8 }, 3, 4, 9, 4), log.Commands[0].Command);
        Assert.Equal(log.ToString(), CommandLog.Parse(log.ToString()).ToString());
    }

    [Fact]
    public void TheMageHasTheSpellInTheData()
    {
        Assert.True(Content.ContentDb.Default.Unit("mage").GrowForest);
        Assert.False(Content.ContentDb.Default.Unit("spearman").GrowForest);
    }
}
