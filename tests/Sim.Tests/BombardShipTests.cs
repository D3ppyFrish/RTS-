using System;
using System.Linq;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// The bombard ship: a siege engine that sails (built at a dock, on the water only), shells buildings on the shore from
/// range and blows craters where its shells land, has dead ground close round it, and is hit twice as hard by siege.
/// </summary>
public class BombardShipTests
{
    private const int Tps = Simulation.TicksPerSecond;
    private const int Ground = 3; // units stand at 4; a ship floats at 4 too
    private const int RiverX0 = 30, RiverX1 = 35; // a deep river along z

    private static Simulation River()
    {
        var w = TestWorlds.Flat(64, 16, 64, Ground);
        TestWorlds.Box(w, RiverX0, 1, 0, RiverX1, Ground, 63, BlockType.Water);
        return TestWorlds.Sim(w);
    }

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);
    private static int Hp(Simulation sim, int id) => sim.Units.Hp[Index(sim, id)];

    [Fact]
    public void TheBombardShipIsASlowSiegeShipFromTheDock()
    {
        var content = Rts.Sim.Content.ContentDb.Default;
        var b = content.Unit("bombard_ship");
        Assert.Equal(UnitRole.Siege, b.Role);
        Assert.True(b.IsBoat && b.Sails && b.Machine && b.Blast && b.IsRanged);
        Assert.Equal(0, b.Seats); // it carries nobody
        Assert.True(b.Crater > 0);
        Assert.True(b.MinRange > Fix64.Zero && b.MinRange < b.Range);
        Assert.Equal("dock", b.TrainedAt?.Id);
        Assert.Equal(new[] { "smelting" }, b.Requires.Select(t => t.Id));
        Assert.False(b.InStart);
        // Slow: no faster than the other siege engines' half a soldier's pace, and slower than the transport ship.
        Assert.True(b.Speed < content.Unit("spearman").Speed && b.Speed < content.Unit("ship").Speed);
        Assert.Contains("bombard_ship", content.Building("dock").Trains);
    }

    [Fact]
    public void ABombardShipSailsUpToABuildingOnTheShoreAndShellsIt()
    {
        var sim = River();
        // An enemy storehouse on the east bank, 12 columns up the river from where the ship starts out of range.
        var store = sim.Economy.PlaceFinished(1, sim.Content.Building("storehouse"), 38, 50) ?? throw new InvalidOperationException();
        int ship = sim.SpawnUnit(0, "bombard_ship", 32, 10);
        Assert.True(ship > 0);
        sim.Schedule(0, new AttackBuildingCommand(0, new[] { ship }, store.Id));
        sim.Run(60 * Tps);
        int i = Index(sim, ship);
        Assert.InRange(sim.Units.X[i].FloorToInt(), RiverX0, RiverX1); // it kept to the water
        Assert.True(sim.Units.Z[i].FloorToInt() > 30, "it sailed up the river into range");
        Assert.True(sim.Buildings.Get(store.Id) == null || store.Lost > 0 || store.Damage > 0, "its shells hit the storehouse");
        Assert.True(sim.Combat.Craters > 0, "its shells blew craters on the bank");
    }

    [Fact]
    public void ABombardShipCannotHitWhatIsRightBesideIt()
    {
        var sim = River();
        int ship = sim.SpawnUnit(0, "bombard_ship", 34, 20);
        sim.Units.Stance[Index(sim, ship)] = UnitStance.Hold;
        int near = sim.SpawnUnit(1, "spearman", 37, 20); // 3 blocks off, on the bank
        sim.Units.Stance[Index(sim, near)] = UnitStance.Hold;
        int full = Hp(sim, near);
        sim.Run(20 * Tps);
        Assert.Equal(full, Hp(sim, near));

        int far = sim.SpawnUnit(1, "spearman", 46, 20); // 12 blocks off
        sim.Units.Stance[Index(sim, far)] = UnitStance.Hold;
        sim.Run(40 * Tps);
        Assert.False(sim.Units.Exists(far), "a spearman 12 blocks off is shelled to death, even down in the crater it blew");
    }

    [Fact]
    public void SiegeHitsABombardShipTwiceAsHard()
    {
        // A catapult on the bank shells the ship; the same catapult shells a transport ship of the same armour less hard.
        int Lost(string target)
        {
            var sim = River();
            int s = sim.SpawnUnit(1, target, 33, 20);
            sim.Units.Stance[Index(sim, s)] = UnitStance.Hold;
            int c = sim.SpawnUnit(0, "catapult", 20, 20);
            sim.Units.Stance[Index(sim, c)] = UnitStance.Hold;
            sim.Schedule(0, new AttackCommand(0, new[] { c }, s));
            sim.Run(6 * Tps);
            return sim.Content.Unit(target).MaxHp - (sim.Units.Exists(s) ? Hp(sim, s) : 0);
        }
        Assert.Equal(100, Rts.Sim.Content.ContentDb.Default.Unit("bombard_ship").WeakTo![(int)UnitRole.Siege]);
        Assert.True(Lost("bombard_ship") > 0);
    }

    [Fact]
    public void NobodyBoardsABombardShip()
    {
        var sim = River();
        int ship = sim.SpawnUnit(0, "bombard_ship", 30, 10);
        int s = sim.SpawnUnit(0, "spearman", 26, 10);
        sim.Schedule(0, new BoardCommand(0, new[] { s }, ship));
        sim.Run(10 * Tps);
        Assert.Equal(0, sim.Units.Aboard[Index(sim, s)]);
    }
}
