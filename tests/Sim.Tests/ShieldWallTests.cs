using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Content;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Issue #145: Drilled ranks lets line infantry take up a shield wall.</summary>
public class ShieldWallTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Tps = Simulation.TicksPerSecond;

    private static Simulation Flat(int size = 64) => TestWorlds.Sim(TestWorlds.Flat(size, 16, size, Ground));

    private static void Drill(Simulation sim, int player) => sim.Economy.Grant(player, sim.Content.Tech("settler_drill"));

    private static int Hp(Simulation sim, int id) => sim.Units.Exists(id) ? sim.Units.Hp[sim.Units.IndexOf(id)] : 0;

    private static bool Walled(Simulation sim, int id) => sim.Units.ShieldWall[sim.Units.IndexOf(id)];

    [Fact]
    public void DrilledRanksGivesTheShieldWallNotHitPoints()
    {
        var db = ContentLoader.LoadEmbedded();
        var effect = Assert.Single(db.Tech("settler_drill").Effects);
        Assert.Equal((TechEffectType.ShieldWall, UnitRole.LineInfantry), (effect.Type, effect.Role!.Value));
        var wall = db.Combat.Wall;
        Assert.Equal((25, 25, 100, 2 * Tps, Tps), (wall.Front, wall.Charge, wall.Repel, wall.StaggerTicks, wall.DropTicks));

        var sim = Flat();
        int spear = sim.SpawnUnit(0, "spearman", 20, 20);
        int hp = Hp(sim, spear);
        Drill(sim, 0);
        Assert.Equal(hp, Hp(sim, spear));
        Assert.Equal(sim.Content.Unit("spearman").MaxHp, sim.MaxHpOf(sim.Units.IndexOf(spear)));
    }

    [Fact]
    public void OnlyLineInfantryWithTheTechCanFormTheWall()
    {
        var sim = Flat();
        int spear = sim.SpawnUnit(0, "spearman", 20, 20);
        int archer = sim.SpawnUnit(0, "archer", 22, 20);
        sim.Schedule(0, new ShieldWallCommand(0, new[] { spear, archer }, true));
        sim.Run(2);
        Assert.False(Walled(sim, spear)); // no tech yet
        Drill(sim, 0);
        sim.Schedule(sim.Tick, new ShieldWallCommand(0, new[] { spear, archer }, true));
        sim.Run(2);
        Assert.True(Walled(sim, spear));
        Assert.False(Walled(sim, archer));
        int other = sim.SpawnUnit(1, "spearman", 30, 30);
        sim.Schedule(sim.Tick, new ShieldWallCommand(1, new[] { other }, true));
        sim.Run(2);
        Assert.False(Walled(sim, other)); // player 1 has not drilled
    }

    [Fact]
    public void AMoveOrderDropsTheWallAndTheUnitWaitsASecondBeforeWalking()
    {
        var sim = Flat();
        Drill(sim, 0);
        int spear = sim.SpawnUnit(0, "spearman", 20, 20);
        sim.Schedule(0, new ShieldWallCommand(0, new[] { spear }, true));
        sim.Run(5);
        int i = sim.Units.IndexOf(spear);
        var (x, z) = (sim.Units.X[i], sim.Units.Z[i]);
        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { spear }, 40, 20));
        sim.Step();
        Assert.False(Walled(sim, spear));
        sim.Run(sim.Content.Combat.Wall.DropTicks - 2);
        i = sim.Units.IndexOf(spear);
        Assert.Equal((x, z), (sim.Units.X[i], sim.Units.Z[i]));
        sim.Run(Tps);
        i = sim.Units.IndexOf(spear);
        Assert.True(sim.Units.X[i] > x, "it walks once the wall is down");
    }

    [Fact]
    public void AUnitInTheWallIsNotShovedAndDoesNotChase()
    {
        var sim = Flat();
        Drill(sim, 0);
        int spear = sim.SpawnUnit(0, "spearman", 20, 20);
        sim.Schedule(0, new ShieldWallCommand(0, new[] { spear }, true));
        sim.Run(2);
        int i = sim.Units.IndexOf(spear);
        var (x, z) = (sim.Units.X[i], sim.Units.Z[i]);
        // An enemy in sight but out of reach, and friends walking right through it.
        sim.SpawnUnit(1, "worker", 20, 26);
        var friends = Enumerable.Range(0, 4).Select(k => sim.SpawnUnit(0, "worker", 14, 19 + k)).ToArray();
        sim.Schedule(sim.Tick, new MoveCommand(0, friends, 26, 20));
        sim.Run(5 * Tps);
        i = sim.Units.IndexOf(spear);
        Assert.Equal((x, z), (sim.Units.X[i], sim.Units.Z[i]));
        Assert.True(Walled(sim, spear));
    }

    /// <summary>
    /// Six spearmen in a line along x at z = 20, under fire from six archers holding their ground eight columns
    /// to +z. The spearmen alive and their hit points left after the time.
    /// </summary>
    private static (int Alive, int Hp) UnderFire(bool wall, int seconds)
    {
        var sim = Flat();
        Drill(sim, 0);
        var spears = Enumerable.Range(0, 6).Select(k => sim.SpawnUnit(0, "spearman", 20 + 2 * k, 20)).ToArray();
        var archers = Enumerable.Range(0, 6).Select(k => sim.SpawnUnit(1, "archer", 20 + 2 * k, 28)).ToArray();
        sim.Schedule(0, new StanceCommand(0, spears, UnitStance.Hold));
        sim.Schedule(0, new StanceCommand(1, archers, UnitStance.Hold));
        if (wall) sim.Schedule(0, new ShieldWallCommand(0, spears, true));
        sim.Run(seconds * Tps);
        return (spears.Count(sim.Units.Exists), spears.Sum(id => Hp(sim, id)));
    }

    [Fact]
    public void ALineInAShieldWallSurvivesArcherFireThatKillsTheSameSpearmenStandingLoose()
    {
        // Loose, they are all dead in under 8 seconds; walled, they stand twice that long and more.
        var loose = UnderFire(wall: false, seconds: 12);
        var walled = UnderFire(wall: true, seconds: 12);
        Assert.Equal(0, loose.Alive);
        Assert.Equal(6, walled.Alive);
        Assert.True(walled.Hp > 6 * 80 / 3, $"walled spearmen keep {walled.Hp} hit points");
    }

    /// <summary>
    /// A rider attacks a lone spearman (holding its ground) from +z or -z. With a wall, it was raised before the
    /// rider came, with no enemy in sight, so it faces +z. Hit points the spearman lost, and the rider, after the time.
    /// </summary>
    private static (int SpearLost, int RiderLost) Charge(bool wall, bool fromFront, int seconds)
    {
        var sim = Flat();
        Drill(sim, 0);
        int spear = sim.SpawnUnit(0, "spearman", 30, 30);
        sim.Schedule(0, new StanceCommand(0, new[] { spear }, UnitStance.Hold));
        if (wall) sim.Schedule(0, new ShieldWallCommand(0, new[] { spear }, true));
        sim.Run(2);
        if (wall) Assert.Equal(Fix64.One, sim.Units.WallZ[sim.Units.IndexOf(spear)]);
        int rider = sim.SpawnUnit(1, "rider", 30, fromFront ? 40 : 20);
        int spearHp = Hp(sim, spear), riderHp = Hp(sim, rider);
        sim.Schedule(sim.Tick, new AttackCommand(1, new[] { rider }, spear));
        sim.Run(seconds * Tps);
        return (spearHp - Hp(sim, spear), riderHp - Hp(sim, rider));
    }

    [Fact]
    public void ACavalryChargeIntoTheWallsFrontDoesLessAndTheRiderTakesTheHit()
    {
        var loose = Charge(wall: false, fromFront: true, seconds: 6);
        var front = Charge(wall: true, fromFront: true, seconds: 6);
        Assert.True(front.SpearLost * 2 < loose.SpearLost, $"front {front.SpearLost}, loose {loose.SpearLost}");
        Assert.True(front.RiderLost > loose.RiderLost, $"rider loses {front.RiderLost} on the wall, {loose.RiderLost} on a loose spearman");
    }

    [Fact]
    public void ACavalryChargeIntoTheWallsBackIsNotStopped()
    {
        var loose = Charge(wall: false, fromFront: false, seconds: 6);
        var back = Charge(wall: true, fromFront: false, seconds: 6);
        Assert.True(back.SpearLost >= loose.SpearLost, $"back {back.SpearLost}, loose {loose.SpearLost}");
    }

    [Fact]
    public void NeighboursInTheWallCoverEachOthersSides()
    {
        var sim = Flat();
        Drill(sim, 0);
        int lone = sim.SpawnUnit(0, "spearman", 10, 30);
        int left = sim.SpawnUnit(0, "spearman", 40, 30), mid = sim.SpawnUnit(0, "spearman", 41, 30), right = sim.SpawnUnit(0, "spearman", 42, 30);
        sim.Schedule(0, new ShieldWallCommand(0, new[] { lone, left, mid, right }, true));
        sim.Run(2);
        // Archers placed off the side (+x), the front (+z) and the back (-z) of the lone one and the middle one.
        int Shooter(int x, int z) => sim.SpawnUnit(1, "archer", x, z);
        var c = sim.Combat;
        int I(int id) => sim.Units.IndexOf(id);
        int sideOfLone = Shooter(18, 30), frontOfLone = Shooter(10, 38), backOfLone = Shooter(10, 22);
        int sideOfMid = Shooter(49, 30), backOfMid = Shooter(41, 22);
        Assert.True(c.WallCovers(I(frontOfLone), I(lone)));
        Assert.False(c.WallCovers(I(sideOfLone), I(lone)));
        Assert.False(c.WallCovers(I(backOfLone), I(lone)));
        Assert.True(c.WallCovers(I(sideOfMid), I(mid)), "a neighbour covers that side");
        Assert.False(c.WallCovers(I(backOfMid), I(mid)), "nobody covers the back");
        Assert.False(c.WallCovers(I(sideOfMid), I(right)), "the end of the line is open");
    }

    [Fact]
    public void ARoutingUnitDropsTheWall()
    {
        var sim = Flat();
        Drill(sim, 0);
        int spear = sim.SpawnUnit(0, "spearman", 20, 20);
        sim.Schedule(0, new ShieldWallCommand(0, new[] { spear }, true));
        sim.Run(2);
        sim.Units.Morale[sim.Units.IndexOf(spear)] = 0;
        sim.Run(2);
        Assert.False(Walled(sim, spear));
    }

    [Fact]
    public void TheShieldWallCommandRoundTripsThroughTheLog()
    {
        var log = new CommandLog();
        log.Add(3, new ShieldWallCommand(0, new[] { 4, 5 }, true));
        log.Add(9, new ShieldWallCommand(1, new[] { 6 }, false));
        var back = CommandLog.Parse(log.ToString());
        Assert.Equal(log.Commands.Select(c => c.Command), back.Commands.Select(c => c.Command));
    }

    [Fact]
    public void NormalAndHardSettlersRaiseTheWallUnderArcherFireAndLowerItAfter()
    {
        foreach (var level in new[] { AiLevel.Normal, AiLevel.Hard })
        {
            var sim = AiTests.WithStarts(96, (30, 48));
            Drill(sim, 0);
            var hall = sim.Economy.TownHall(0)!;
            var spears = Enumerable.Range(0, 4).Select(k => sim.SpawnUnit(0, "spearman", hall.X + 2 * k, hall.MaxZ + 4)).ToArray();
            var archers = Enumerable.Range(0, 3).Select(k => sim.SpawnUnit(1, "archer", hall.X + 2 * k, hall.MaxZ + 12)).ToArray();
            sim.Schedule(0, new StanceCommand(1, archers, UnitStance.Hold));
            var ai = new AiPlayer(0, 1, level);
            var log = new List<Command>();
            AiTests.PlayLogged(sim, new[] { ai }, log, () => log.Any(c => c is ShieldWallCommand { Raise: true }), 10 * Tps);
            var raised = log.OfType<ShieldWallCommand>().FirstOrDefault(c => c.Raise);
            Assert.True(raised != null, $"{level}: a shield wall under fire");
            Assert.Contains(raised!.Units, spears.Contains);
            // The archers go: the wall comes down again.
            foreach (int a in archers) if (sim.Units.Exists(a)) sim.Units.Remove(a);
            AiTests.PlayLogged(sim, new[] { ai }, log, () => spears.All(id => !sim.Units.Exists(id) || !Walled(sim, id)), 10 * Tps);
            Assert.All(spears.Where(sim.Units.Exists), id => Assert.False(Walled(sim, id), $"{level}: wall lowered"));
        }
    }
}
