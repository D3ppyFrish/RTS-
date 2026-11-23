using System;
using System.Linq;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Siege workshops and catapults (the design doc's siege role: "break walls at range", built of planks and iron):
/// a catapult lobs stones over walls from far off and topples thick walls, but has dead ground close round it.
/// </summary>
public class SiegeTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Tps = Simulation.TicksPerSecond;

    private static Simulation Flat() => TestWorlds.Sim(TestWorlds.Flat(64, 16, 64, Ground));

    private static Building Place(Simulation sim, int owner, string def, int x, int z) =>
        sim.Economy.PlaceFinished(owner, sim.Content.Building(def), x, z) ?? throw new InvalidOperationException($"{def} does not fit");

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

    private static int Hp(Simulation sim, int id) => sim.Units.Hp[Index(sim, id)];

    private static void Run(Simulation sim, int seconds)
    {
        for (int t = 0; t < seconds * Tps; t++) sim.Step();
    }

    /// <summary>A catapult of player 0 at (20, 20) holding its ground, so it never walks.</summary>
    private static int Catapult(Simulation sim)
    {
        int c = sim.SpawnUnit(0, "catapult", 20, 20);
        sim.Units.Stance[Index(sim, c)] = UnitStance.Hold;
        return c;
    }

    /// <summary>A unit of player 1 standing still where it is put (holding its ground and doing no harm).</summary>
    private static int Target(Simulation sim, string unit, int x, int z)
    {
        int t = sim.SpawnUnit(1, unit, x, z);
        sim.Units.Stance[Index(sim, t)] = UnitStance.Hold;
        return t;
    }

    [Fact]
    public void TheCatapultIsASiegeEngineOfPlanksAndIronFromASiegeWorkshop()
    {
        var content = Rts.Sim.Content.ContentDb.Default;
        var cat = content.Unit("catapult");
        Assert.Equal(UnitRole.Siege, cat.Role);
        Assert.Null(cat.Faction); // every faction builds them
        Assert.Equal("siege_workshop", cat.TrainedAt?.Id);
        Assert.Equal(new[] { "smelting" }, cat.Requires.Select(t => t.Id));
        Assert.Contains(cat.Cost, c => c.Good == Good.Planks);
        Assert.Contains(cat.Cost, c => c.Good == Good.Iron);
        Assert.True(cat.IsRanged && cat.Blast && cat.Lobs && cat.Machine);
        Assert.False(cat.Bleeds);
        Assert.True(cat.MinRange > Fix64.Zero && cat.MinRange < cat.Range);
        Assert.False(cat.InStart);
        // It outranges an archer and a tower's archers, and hits much harder, slowly.
        Assert.True(cat.Range > content.Unit("archer").Range);
        Assert.True(cat.Damage > 3 * content.Unit("archer").Damage);
        Assert.True(cat.Speed < content.Unit("spearman").Speed);
        // The siege role deals full damage to buildings, and a blast full damage to thick walls.
        Assert.Equal(Fix64.One, Flat().Combat.StructureMultiplier(cat, content.Building("stone_wall")));

        var shop = content.Building("siege_workshop");
        Assert.Equal(BuildingKind.Barracks, shop.Kind);
        Assert.Contains("catapult", shop.Trains);
        Assert.Contains(shop.Cost, c => c.Good == Good.Planks);
        Assert.Equal(new[] { "smelting" }, shop.Requires.Select(t => t.Id));
        // A plain barracks does not build siege engines.
        Assert.DoesNotContain("catapult", content.Building("barracks").Trains);
        // Nobody else has dead ground or lobs, so nothing else changes.
        Assert.All(content.Units.Where(u => u.Id is not ("catapult" or "bombard_ship")), u => Assert.True(u.MinRange == Fix64.Zero && !u.Lobs, u.Id));
        // The only other machines are the battering ram (see RamTests) and the ships (see BoatTests, BombardShipTests).
        Assert.Equal(new[] { "catapult", "battering_ram", "ship", "bombard_ship" }, content.Units.Where(u => u.Machine).Select(u => u.Id));
    }

    [Fact]
    public void ASiegeWorkshopNeedsSmeltingAndTrainsACatapultForPlanksAndIron()
    {
        var sim = Flat();
        var hall = sim.Economy.TownHall(0) ?? Place(sim, 0, "town_hall", 6, 6);
        var store = Place(sim, 0, "storehouse", 20, 6);
        foreach (var (g, n) in new[] { (Good.Food, 200), (Good.Wood, 200), (Good.Stone, 200), (Good.Planks, 200), (Good.Iron, 50) })
            store.Stock[(int)g] = n;
        var def = sim.Content.Building("siege_workshop");
        Assert.Equal(PlaceResult.Locked, sim.Economy.CanPlace(0, def, 30, 30, out _));
        sim.Economy.Grant(0, sim.Content.Tech("smelting"));
        Assert.Equal(PlaceResult.Ok, sim.Economy.CanPlace(0, def, 30, 30, out _));

        var shop = Place(sim, 0, "siege_workshop", 30, 30);
        int planks = sim.Economy.Stock(0, Good.Planks), iron = sim.Economy.Stock(0, Good.Iron);
        int before = sim.Units.Count;
        sim.Schedule(0, new TrainCommand(0, shop.Id, "catapult"));
        Run(sim, sim.Content.Unit("catapult").TrainTicks / Tps + 2);
        Assert.Equal(before + 1, sim.Units.Count);
        int cat = sim.Units.Id[sim.Units.Count - 1];
        Assert.Equal("catapult", sim.Content.Units[sim.Units.Def[Index(sim, cat)]].Id);
        Assert.Equal(0, sim.Units.Owner[Index(sim, cat)]);
        Assert.Equal(planks - 60, sim.Economy.Stock(0, Good.Planks));
        Assert.Equal(iron - 10, sim.Economy.Stock(0, Good.Iron));
    }

    [Fact]
    public void ACatapultHitsWhatIsFarOffButCannotHitWhatStandsRightBesideIt()
    {
        // Far off (10 blocks): it shoots on its own and the spearman is soon dead.
        var sim = Flat();
        Catapult(sim);
        int far = Target(sim, "spearman", 30, 20);
        Run(sim, 30);
        Assert.False(sim.Units.Exists(far), "a spearman 10 blocks off is crushed by the catapult's stones");

        // Close (3 blocks): it never shoots, on its own or when ordered to.
        sim = Flat();
        int c = Catapult(sim);
        int near = Target(sim, "spearman", 23, 20);
        int full = Hp(sim, near);
        Run(sim, 20);
        Assert.Equal(full, Hp(sim, near));
        sim.Schedule(sim.Tick, new AttackCommand(0, new[] { c }, near));
        Run(sim, 20);
        Assert.Equal(full, Hp(sim, near));
        Assert.Equal(0, sim.Units.Target[Index(sim, c)]);

        // With both, it passes over the one beside it and shoots the one further off.
        sim = Flat();
        Catapult(sim);
        near = Target(sim, "spearman", 22, 21);
        far = Target(sim, "spearman", 29, 20);
        Run(sim, 30);
        Assert.Equal(full, Hp(sim, near));
        Assert.False(sim.Units.Exists(far));
    }

    [Fact]
    public void AnEnemyThatWalksIntoTheDeadGroundIsSafeFromTheCatapult()
    {
        // A rider charges the catapult from 10 blocks off: once it is close, no more stones land on it.
        var sim = Flat();
        int c = Catapult(sim);
        int rider = sim.SpawnUnit(1, "rider", 30, 20);
        sim.Schedule(0, new AttackCommand(1, new[] { rider }, c));
        Run(sim, 20);
        Assert.True(sim.Units.Exists(rider), "a rider at the catapult outlives it");
        Assert.False(sim.Units.Exists(c), "riders make short work of a catapult once among it");
    }

    [Fact]
    public void ACatapultLobsItsStonesOverAWallThatStopsArrows()
    {
        // A solid stone wall 4 high between the shooter at x 22 and its target at x 30.
        int Left(string shooter)
        {
            var w = TestWorlds.Flat(64, 16, 64, Ground);
            TestWorlds.Box(w, 25, Ground + 1, 10, 25, Ground + 4, 30, BlockType.Stone);
            var sim = TestWorlds.Sim(w);
            // Both hold their ground 8 blocks from the target, in reach of either.
            int s = sim.SpawnUnit(0, shooter, 22, 20);
            sim.Units.Stance[Index(sim, s)] = UnitStance.Hold;
            int t = Target(sim, "spearman", 30, 20);
            Run(sim, 30);
            return sim.Units.Exists(t) ? Hp(sim, t) : 0;
        }
        int full = Rts.Sim.Content.ContentDb.Default.Unit("spearman").MaxHp;
        Assert.Equal(full, Left("archer")); // no clear line: not one arrow gets through
        Assert.Equal(0, Left("catapult"));
    }

    [Fact]
    public void ACatapultNeverBreaksAndRunsAndDoesNotBleed()
    {
        var sim = Flat();
        int c = Catapult(sim);
        int s = sim.SpawnUnit(0, "spearman", 10, 10);
        sim.Units.Morale[Index(sim, c)] = 0;
        sim.Units.Morale[Index(sim, s)] = 0;
        Run(sim, 1);
        Assert.Equal(0, sim.Units.Rout[Index(sim, c)]);
        Assert.True(sim.Units.Rout[Index(sim, s)] > 0, "a spearman with no heart left runs");

        // Cut by a rider's sword, it loses hit points but no blood.
        sim = Flat();
        c = Catapult(sim);
        int rider = sim.SpawnUnit(1, "rider", 21, 20);
        sim.Schedule(0, new AttackCommand(1, new[] { rider }, c));
        Run(sim, 3);
        Assert.True(Hp(sim, c) < sim.Content.Unit("catapult").MaxHp);
        Assert.Equal(0, sim.Blood.Total);
    }

    [Fact]
    public void ACatapultBringsDownAStoneWallFromBeyondTheReachOfItsArchers()
    {
        // A stone wall with an archer of its owner standing behind it: the catapult 13 blocks off is out of the
        // archer's reach and knocks the wall down.
        var sim = Flat();
        var wall = Place(sim, 1, "stone_wall", 33, 20);
        int archer = Target(sim, "archer", 34, 20);
        int c = Catapult(sim);
        sim.Schedule(0, new AttackBuildingCommand(0, new[] { c }, wall.Id));
        int hp = Hp(sim, c);
        for (int t = 0; t < 300 * Tps && sim.Buildings.Get(wall.Id) != null; t++) sim.Step();
        Assert.Null(sim.Buildings.Get(wall.Id));
        Assert.Equal(hp, Hp(sim, c));
        Assert.True(sim.Units.Exists(archer));
    }
}
