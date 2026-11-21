using Rts.Sim.Replay;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Terrain magic: the mage's Chasm.</summary>
public class ChasmTests
{
    private const int Ground = 3; // flat test worlds: stone at 1 and 2, grass at 3, units stand at 4
    private const int Stand = Ground + 1;
    private const int Floor = Stand - SpellSystem.ChasmDepth;
    private const int Tps = Simulation.TicksPerSecond;
    private const int P = 0;

    private static Simulation Flat() => TestWorlds.Sim(TestWorlds.Flat(64, 16, 64, Ground));

    private static int Spawn(Simulation sim, int owner, string unit, int x, int z) => sim.SpawnUnit(owner, unit, x, z, Stand);

    private static void Open(Simulation sim, int[] units, int x1, int z1, int x2, int z2)
    {
        sim.Schedule(sim.Tick, new ChasmCommand(P, units, x1, z1, x2, z2));
        sim.Step();
    }

    [Fact]
    public void AMageOpensAChasmTooDeepToClimbOutOf()
    {
        var sim = Flat();
        int mage = Spawn(sim, P, "mage", 20, 30);
        Open(sim, new[] { mage }, 26, 27, 26, 33);
        for (int z = 27; z <= 33; z++)
        {
            for (int y = Floor; y < Stand; y++) Assert.Equal(BlockType.Air, sim.World.Get(26, y, z));
            Assert.Equal(Floor, Walk.GroundLevel(sim.World, 26, z));
        }
        Assert.Equal(BlockType.Grass, sim.World.Get(26, Ground, 26));
        Assert.Equal(BlockType.Grass, sim.World.Get(26, Ground, 34));
        Assert.True(SpellSystem.ChasmDepth > Walk.MaxClimb, "nobody climbs out of it");
        Assert.Single(sim.Spells.Chasms);
        Assert.Equal(7 * SpellSystem.ChasmDepth, sim.Spells.Chasms[0].Blocks.Count);
        Assert.Equal(sim.Tick - 1, sim.Spells.LastChasm);
    }

    [Fact]
    public void AChasmIsAsLongAsAnEarthWall()
    {
        var sim = Flat();
        int mage = Spawn(sim, P, "mage", 20, 30);
        Open(sim, new[] { mage }, 26, 20, 26, 40);
        int columns = 0;
        for (int z = 15; z <= 45; z++)
            if (sim.World.Get(26, Ground, z) == BlockType.Air) columns++;
        Assert.Equal(SpellSystem.MaxLength, columns);
    }

    [Fact]
    public void UnitsStandingOnItFallInAndAreHurt()
    {
        var sim = Flat();
        int mage = Spawn(sim, P, "mage", 20, 30);
        int spear = Spawn(sim, P, "spearman", 26, 30);
        int i = sim.Units.IndexOf(spear);
        int hp = sim.Units.Hp[i];
        Open(sim, new[] { mage }, 26, 27, 26, 33);
        sim.Run(Tps);
        i = sim.Units.IndexOf(spear);
        Assert.Equal(Floor, sim.Units.Y[i]);
        Assert.Equal(hp - SpellSystem.FallDamage, sim.Units.Hp[i]);
        // The mage, off the line, is not.
        int m = sim.Units.IndexOf(mage);
        Assert.Equal(Stand, sim.Units.Y[m]);
    }

    [Fact]
    public void TheGroundClosesAsItWasAndLiftsThemOut()
    {
        var sim = Flat();
        int mage = Spawn(sim, P, "mage", 20, 30);
        int spear = Spawn(sim, P, "spearman", 26, 30);
        Open(sim, new[] { mage }, 26, 27, 26, 33);
        sim.Run(SpellSystem.ChasmLastsTicks - 2 * Tps);
        Assert.Single(sim.Spells.Chasms);
        Assert.Equal(Floor, sim.Units.Y[sim.Units.IndexOf(spear)]);
        sim.Run(3 * Tps);
        Assert.Empty(sim.Spells.Chasms);
        for (int z = 27; z <= 33; z++)
        {
            Assert.Equal(BlockType.Grass, sim.World.Get(26, Ground, z));
            Assert.Equal(BlockType.Stone, sim.World.Get(26, Floor, z));
        }
        int i = sim.Units.IndexOf(spear);
        Assert.True(i >= 0, "nobody is buried by the ground closing");
        Assert.Equal(Stand, sim.Units.Y[i]);
    }

    [Fact]
    public void NotBesideWaterNorUnderABuilding()
    {
        var w = TestWorlds.Flat(64, 16, 64, Ground);
        w.Set(27, Ground, 30, BlockType.Water);
        var sim = TestWorlds.Sim(w);
        int mage = Spawn(sim, P, "mage", 20, 30);
        Open(sim, new[] { mage }, 26, 28, 26, 32);
        Assert.Equal(BlockType.Grass, sim.World.Get(26, Ground, 30));
        Assert.Equal(BlockType.Air, sim.World.Get(26, Ground, 29));
        Assert.Equal(BlockType.Air, sim.World.Get(26, Ground, 31));

        var plan = new System.Collections.Generic.List<(int X, int Z, int Y, bool Room)>();
        var house = sim.Economy.PlaceFinished(P, sim.Content.Building("house"), 40, 40);
        Assert.NotNull(house);
        var (hx, hz) = (house!.X, house.Z);
        sim.Spells.PlanChasm(hx, hz, hx, hz, plan);
        Assert.Single(plan);
        Assert.False(plan[0].Room);
    }

    [Fact]
    public void ALineWithNothingToOpenCostsNothing()
    {
        var w = TestWorlds.Flat(64, 16, 64, Ground);
        for (int z = 26; z <= 34; z++) w.Set(27, Ground, z, BlockType.Water);
        var sim = TestWorlds.Sim(w);
        int mage = Spawn(sim, P, "mage", 20, 30);
        Open(sim, new[] { mage }, 26, 28, 26, 32);
        Assert.Empty(sim.Spells.Chasms);
        Assert.True(sim.Spells.CanCast(sim.Units.IndexOf(mage), Spell.Chasm));
    }

    [Fact]
    public void OpeningAChasmMakesTheMageWaitForEverySpell()
    {
        var sim = Flat();
        int mage = Spawn(sim, P, "mage", 20, 30);
        Open(sim, new[] { mage }, 26, 27, 26, 33);
        int i = sim.Units.IndexOf(mage);
        Assert.False(sim.Spells.CanCast(i, Spell.Chasm));
        Assert.False(sim.Spells.CanCast(i, Spell.EarthWall));
        sim.Schedule(sim.Tick, new EarthWallCommand(P, new[] { mage }, 16, 27, 16, 33));
        sim.Step();
        Assert.Empty(sim.Spells.Walls);
    }

    [Fact]
    public void MagesTogetherOpenAWiderChasm()
    {
        var sim = Flat();
        sim.Economy.Player(P).Mana = 1000; // spells cost mana: plenty for every cast here
        int a = Spawn(sim, P, "mage", 20, 30), b = Spawn(sim, P, "mage", 20, 31);
        Open(sim, new[] { a, b }, 26, 27, 26, 33);
        for (int z = 27; z <= 33; z++)
        {
            Assert.Equal(BlockType.Air, sim.World.Get(26, Ground, z));
            Assert.Equal(BlockType.Air, sim.World.Get(25, Ground, z));
        }
        Assert.Equal(BlockType.Grass, sim.World.Get(27, Ground, 30));
    }

    [Fact]
    public void AMageOutOfRangeWalksUpFirst()
    {
        var sim = Flat();
        int mage = Spawn(sim, P, "mage", 5, 30);
        Open(sim, new[] { mage }, 30, 27, 30, 33);
        Assert.Empty(sim.Spells.Chasms);
        Assert.True(sim.Spells.Casting(mage));
        sim.Run(15 * Tps);
        Assert.Single(sim.Spells.Chasms);
    }

    [Fact]
    public void TheSameChasmsGiveTheSameState()
    {
        ulong Play()
        {
            var sim = Flat();
            int a = Spawn(sim, P, "mage", 5, 30), b = Spawn(sim, P, "mage", 20, 20);
            Spawn(sim, P, "spearman", 22, 25);
            sim.Schedule(0, new ChasmCommand(P, new[] { a }, 30, 28, 30, 32));
            sim.Schedule(10, new ChasmCommand(P, new[] { b }, 20, 25, 26, 25));
            sim.Run(SpellSystem.ChasmLastsTicks + 20 * Tps);
            return sim.StateHash();
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void TheCommandRoundTripsThroughTheLog()
    {
        var log = CommandLog.Parse("rts-log 1\nseed 1\nworld 32 16 32\nticks 10\n5 chasm 0 3 4 9 4 7,8\n");
        Assert.Equal(new ChasmCommand(0, new[] { 7, 8 }, 3, 4, 9, 4), log.Commands[0].Command);
        Assert.Equal(log.ToString(), CommandLog.Parse(log.ToString()).ToString());
    }

    [Fact]
    public void TheMageHasTheSpellInTheData()
    {
        Assert.True(Content.ContentDb.Default.Unit("mage").Chasm);
        Assert.False(Content.ContentDb.Default.Unit("spearman").Chasm);
    }
}
