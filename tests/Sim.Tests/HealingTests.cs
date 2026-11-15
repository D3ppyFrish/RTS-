using System;
using Rts.Sim.Economy;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Units healing on their own once they are out of the fight (issue #38).</summary>
public class HealingTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Tps = Simulation.TicksPerSecond;

    private static Simulation Flat() => TestWorlds.Sim(TestWorlds.Flat(96, 16, 96, Ground));

    private static Building Place(Simulation sim, int owner, string def, int x, int z) =>
        sim.Economy.PlaceFinished(owner, sim.Content.Building(def), x, z) ?? throw new InvalidOperationException($"{def} does not fit");

    private static int Hp(Simulation sim, int id) => sim.Units.Exists(id) ? sim.Units.Hp[sim.Units.IndexOf(id)] : 0;

    private static int Wounded(Simulation sim, int owner, string unit, int x, int z, int hp)
    {
        int id = sim.SpawnUnit(owner, unit, x, z);
        Assert.True(id > 0);
        sim.Units.Hp[sim.Units.IndexOf(id)] = hp;
        return id;
    }

    /// <summary>Whole hit points a unit with <paramref name="max"/> at most gets back in so many seconds at a percentage a second.</summary>
    private static int Healed(int max, Fix64 percent, int seconds) => (Fix64.FromInt(max) * percent * seconds / 100).FloorToInt();

    [Fact]
    public void AWoundedUnitOutOfTheFightHealsSlowly()
    {
        var sim = Flat();
        var rules = sim.Content.Combat;
        int max = sim.Content.Unit("spearman").MaxHp;
        int spear = Wounded(sim, 0, "spearman", 20, 20, max / 2);

        sim.Run(rules.RecoveryDelayTicks); // nothing yet: it has to be out of the fight for a while first
        Assert.Equal(max / 2, Hp(sim, spear));
        sim.Run(60 * Tps);
        int healed = Healed(max, rules.RecoveryPercent, 60);
        Assert.InRange(healed, 1, max / 4); // slowly: a minute gives back less than a quarter
        Assert.Equal(max / 2 + healed, Hp(sim, spear));
    }

    [Fact]
    public void AWoundedUnitHealsFasterNearItsTownHallOrHouses()
    {
        var sim = Flat();
        var rules = sim.Content.Combat;
        Assert.True(rules.HomeRecoveryPercent > rules.RecoveryPercent * 4);
        int max = sim.Content.Unit("spearman").MaxHp;
        var hall = Place(sim, 0, "town_hall", 20, 20);
        var house = Place(sim, 0, "house", 60, 60);
        var enemyHouse = Place(sim, 1, "house", 60, 20);
        int byHall = Wounded(sim, 0, "spearman", hall.MaxX + rules.HomeRange, 23, max / 2);
        int byHouse = Wounded(sim, 0, "spearman", house.MaxX + 3, 62, max / 2);
        int away = Wounded(sim, 0, "spearman", 23, hall.MaxZ + rules.HomeRange + 1, max / 2);
        int byEnemyHouse = Wounded(sim, 0, "spearman", enemyHouse.MaxX + 3, 22, max / 2); // not its own

        sim.Run(rules.RecoveryDelayTicks + 20 * Tps);
        int atHome = max / 2 + Healed(max, rules.HomeRecoveryPercent, 20), inTheField = max / 2 + Healed(max, rules.RecoveryPercent, 20);
        Assert.True(atHome > inTheField + 10);
        Assert.Equal(atHome, Hp(sim, byHall));
        Assert.Equal(atHome, Hp(sim, byHouse));
        Assert.Equal(inTheField, Hp(sim, away));
        Assert.Equal(inTheField, Hp(sim, byEnemyHouse));

        sim.Run(60 * Tps); // and never past its most hit points
        Assert.Equal(max, Hp(sim, byHall));
        Assert.Equal(max, Hp(sim, byHouse));
        Assert.True(Hp(sim, away) < max);
    }

    [Fact]
    public void AUnitDoesNotHealWhileItFights()
    {
        var sim = Flat();
        int delay = sim.Content.Combat.RecoveryDelayTicks;
        Place(sim, 0, "town_hall", 24, 20); // all of this is at player 0's home, where units heal fastest
        var enemyHall = Place(sim, 1, "town_hall", 40, 20);

        // Hitting: a spearman knocking at the enemy's town hall for a minute does not heal...
        int spear = Wounded(sim, 0, "spearman", 34, 23, 40);
        sim.Schedule(0, new AttackBuildingCommand(0, new[] { spear }, enemyHall.Id));
        for (int t = 0; t < 60 * Tps; t++)
        {
            sim.Step();
            Assert.Equal(40, Hp(sim, spear));
        }
        Assert.True(enemyHall.Health < enemyHall.MaxHealth);
        // ...until it stops: then it heals once it has been out of the fight for a while (from its last blow,
        // a second or less before it stopped).
        sim.Schedule(sim.Tick, new StopCommand(0, new[] { spear }));
        sim.Run(delay - 2 * Tps);
        Assert.Equal(40, Hp(sim, spear));
        sim.Run(6 * Tps);
        Assert.True(Hp(sim, spear) > 40);

        // Being hit: a worker (workers do not hit back) that an enemy worker attacks does not heal either.
        int victim = Wounded(sim, 0, "worker", 22, 34, 36);
        int attacker = sim.SpawnUnit(1, "worker", 21, 34);
        sim.Schedule(sim.Tick, new AttackCommand(1, new[] { attacker }, victim));
        int hp = Hp(sim, victim);
        for (int t = 0; t < 10 * Tps; t++)
        {
            sim.Step();
            Assert.True(Hp(sim, victim) <= hp, "no healing between hits");
            hp = Hp(sim, victim);
        }
        Assert.InRange(hp, 1, 36 - 3 * sim.Content.Unit("worker").Damage);
        sim.Schedule(sim.Tick, new StopCommand(1, new[] { attacker }));
        sim.Run(delay - 2 * Tps);
        Assert.Equal(hp, Hp(sim, victim));
        sim.Run(6 * Tps);
        Assert.True(Hp(sim, victim) > hp);
    }
}
