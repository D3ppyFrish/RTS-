using System;
using System.Linq;
using Rts.Sim.Economy;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Fighting (issue #10): roles and counters, high ground, line of sight, morale, buildings and tunnels.</summary>
public class CombatTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Tps = Simulation.TicksPerSecond;

    private static Simulation Flat(int size = 64) => TestWorlds.Sim(TestWorlds.Flat(size, 16, size, Ground));

    private static Building Place(Simulation sim, int owner, string def, int x, int z) =>
        sim.Economy.PlaceFinished(owner, sim.Content.Building(def), x, z) ?? throw new InvalidOperationException($"{def} does not fit");

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

    /// <summary>The unit's hit points, or 0 once it is dead.</summary>
    private static int Hp(Simulation sim, int id) => sim.Units.Exists(id) ? sim.Units.Hp[Index(sim, id)] : 0;

    private static int Cost(Simulation sim, string unit) => sim.Content.Unit(unit).Cost.Sum(c => c.Amount);

    private static int Alive(Simulation sim, int[] ids) => ids.Count(sim.Units.Exists);

    /// <summary>Steps until the condition holds; fails the test if it takes longer than maxTicks.</summary>
    private static int RunUntil(Simulation sim, Func<bool> done, int maxTicks, string what)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return t;
            sim.Step();
        }
        Assert.Fail($"Not done after {maxTicks} ticks: {what}");
        return maxTicks;
    }

    /// <summary>Steps until one side is gone, or for at most maxTicks.</summary>
    private static void Fight(Simulation sim, int[] a, int[] b, int maxTicks)
    {
        for (int t = 0; t < maxTicks && Alive(sim, a) > 0 && Alive(sim, b) > 0; t++) sim.Step();
    }

    /// <summary>
    /// n units in rows of <paramref name="perRow"/> along z, <paramref name="spacing"/> columns apart, the
    /// first row at column x and each next one <paramref name="rowGap"/> columns behind it (in direction dx).
    /// </summary>
    private static int[] Army(Simulation sim, int owner, string unit, int n, int x, int dx, int z, int perRow = 6, int spacing = 2, int rowGap = 1)
    {
        var ids = new int[n];
        for (int k = 0; k < n; k++)
        {
            ids[k] = sim.SpawnUnit(owner, unit, x + dx * (k / perRow) * rowGap, z + k % perRow * spacing);
            Assert.True(ids[k] > 0);
        }
        return ids;
    }

    // --- Issue #10's checks ------------------------------------------------------------------------

    /// <summary>
    /// Two armies of the same cost meet on open ground, each attack-moving through where the other started.
    /// How many of each are left once one side is gone (or after a minute), and how many each side had.
    /// </summary>
    private static (int Left, int Right, int LeftCount, int RightCount) Battle(string left, string right, int budget = 720)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(128, 24, 64, Ground));
        var a = Army(sim, 0, left, budget / Cost(sim, left), 36, -1, 26);
        var b = Army(sim, 1, right, budget / Cost(sim, right), 50, 1, 26);
        sim.Schedule(0, new AttackMoveCommand(0, a, 60, 31));
        sim.Schedule(0, new AttackMoveCommand(1, b, 26, 31));
        Fight(sim, a, b, 60 * Tps);
        return (Alive(sim, a), Alive(sim, b), a.Length, b.Length);
    }

    /// <summary>Issue #10's check: each counter in the role table wins its matchup at equal cost, from either side.</summary>
    [Theory]
    [InlineData("spearman", "rider")]  // line infantry beats cavalry
    [InlineData("archer", "spearman")] // ranged beats line infantry
    [InlineData("archer", "worker")]   // and workers
    [InlineData("rider", "archer")]    // cavalry beats ranged
    [InlineData("rider", "mage")]      // and casters, who are weak against it
    [InlineData("spearman", "sapper")] // sappers are weak against every combat unit
    public void EachCounterWinsItsMatchupAtEqualCost(string winner, string loser)
    {
        foreach (bool winnerLeft in new[] { true, false })
        {
            var r = winnerLeft ? Battle(winner, loser) : Battle(loser, winner);
            int won = winnerLeft ? r.Left : r.Right, lost = winnerLeft ? r.Right : r.Left;
            int count = winnerLeft ? r.LeftCount : r.RightCount;
            Assert.True(lost == 0 && won * 2 >= count,
                $"{winner} against {loser}, {(winnerLeft ? "from the left" : "from the right")}: {won} of {count} {winner}s left, and {lost} {loser}s");
        }
    }

    /// <summary>
    /// Twelve archers a side, ten columns apart, standing their ground: one side on a plateau four levels
    /// up (raised: 0 left, 1 right) or both on the flat (-1).
    /// </summary>
    private static (int Left, int Right) HillFight(int raised, ulong seed = 1)
    {
        var w = TestWorlds.Flat(128, 24, 64, Ground);
        const int left = 50, right = 60;
        if (raised == 0) TestWorlds.Box(w, 0, Ground + 1, 0, left, Ground + 4, 63, BlockType.Stone);
        if (raised == 1) TestWorlds.Box(w, right, Ground + 1, 0, 127, Ground + 4, 63, BlockType.Stone);
        var sim = TestWorlds.Sim(w, seed);
        var a = Army(sim, 0, "archer", 12, left, -1, 26);
        var b = Army(sim, 1, "archer", 12, right, 1, 26);
        Fight(sim, a, b, 60 * Tps);
        return (Alive(sim, a), Alive(sim, b));
    }

    /// <summary>Issue #10's check: archers on a hill beat the same archers below, whichever side the hill is on.</summary>
    [Fact]
    public void ArchersOnAHillBeatTheSameArchersBelow()
    {
        // Where each arrow lands is down to chance, so on the flat one fight can go either way, but over a
        // few matches neither side comes out ahead.
        int leftTotal = 0, rightTotal = 0;
        for (ulong seed = 1; seed <= 6; seed++)
        {
            var flat = HillFight(-1, seed);
            Assert.True(flat.Left < 12 && flat.Right < 12, $"on the flat both sides lose some: {flat}");
            leftTotal += flat.Left;
            rightTotal += flat.Right;
        }
        Assert.InRange(leftTotal - rightTotal, -6, 6);

        var hillLeft = HillFight(0);
        Assert.True(hillLeft.Left >= 9 && hillLeft.Right <= 6, $"hill on the left: {hillLeft}");
        var hillRight = HillFight(1);
        Assert.True(hillRight.Right >= 9 && hillRight.Left <= 6, $"hill on the right: {hillRight}");
    }

    /// <summary>Ten mages against twelve spearmen marching at them, the spearmen <paramref name="spacing"/> columns apart.</summary>
    private static (int Mages, int Spearmen) CasterFight(int spacing)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(128, 24, 64, Ground));
        var mages = Army(sim, 0, "mage", 10, 30, -1, 27, perRow: 5);
        var spears = Army(sim, 1, "spearman", 12, 52, 1, 30, perRow: 4, spacing: spacing, rowGap: spacing);
        sim.Schedule(0, new AttackMoveCommand(0, mages, 70, 31));
        sim.Schedule(0, new AttackMoveCommand(1, spears, 10, 31));
        Fight(sim, mages, spears, 60 * Tps);
        return (Alive(sim, mages), Alive(sim, spears));
    }

    /// <summary>Casters are strong against clumped groups: their blasts break a packed block that beats them spread out.</summary>
    [Fact]
    public void CastersBreakAPackedGroupThatBeatsThemSpreadOut()
    {
        var packed = CasterFight(spacing: 1);
        Assert.True(packed.Spearmen == 0 && packed.Mages >= 5, $"packed: {packed}");
        // Spread out, the spearmen cut down most of the mages and most of them live (where blows land is down
        // to chance, so a mage or two may get away).
        var spread = CasterFight(spacing: 2);
        Assert.True(spread.Mages <= 3 && spread.Spearmen >= 6, $"spread out: {spread}");
    }

    /// <summary>Ticks n units of a kind take to bring down a house.</summary>
    private static int Demolish(string unit, int n)
    {
        var sim = Flat();
        var house = Place(sim, 0, "house", 30, 30);
        var ids = Army(sim, 1, unit, n, 20, -1, 26);
        sim.Schedule(0, new AttackBuildingCommand(1, ids, house.Id));
        return RunUntil(sim, () => sim.Buildings.Get(house.Id) == null, 120 * Tps, $"{n} {unit}s to bring down the house");
    }

    /// <summary>Sappers are strong against structures: they bring a house down far faster than spearmen of the same cost.</summary>
    [Fact]
    public void SappersBringAHouseDownFasterThanSpearmenOfTheSameCost()
    {
        int sappers = Demolish("sapper", 720 / 70);
        int spearmen = Demolish("spearman", 720 / 60);
        Assert.True(sappers * 2 < spearmen, $"sappers took {sappers} ticks, spearmen {spearmen}");
    }

    // --- Hits --------------------------------------------------------------------------------------

    [Fact]
    public void DamageFollowsTheRoleTable()
    {
        var w = TestWorlds.Flat(64, 16, 64, Ground);
        TestWorlds.Box(w, 21, Ground + 1, 0, 63, Ground + 1, 63, BlockType.Stone); // a step up, one level
        var sim = TestWorlds.Sim(w);
        int spear = sim.SpawnUnit(0, "spearman", 20, 30);
        int archer = sim.SpawnUnit(0, "archer", 20, 32);
        int rider = sim.SpawnUnit(1, "rider", 21, 30);     // a level up
        int enemy = sim.SpawnUnit(1, "spearman", 20, 31);  // level with the spearman
        int worker = sim.SpawnUnit(1, "worker", 21, 32);   // a level up
        var u = sim.Units;
        Assert.Equal(u.Y[Index(sim, spear)] + 1, u.Y[Index(sim, rider)]);
        int Damage(int a, int t) => sim.Combat.Damage(Index(sim, a), sim.Content.Units[u.Def[Index(sim, a)]], Index(sim, t));

        Assert.Equal(8, Damage(spear, enemy));
        Assert.Equal(16, Damage(spear, rider));  // 8, twice against cavalry (uphill blows find the legs instead, see ArmourTests)
        Assert.Equal(12, Damage(rider, spear));  // downhill: no change; cavalry is weak against line infantry, not the other way
        Assert.Equal(12, Damage(archer, worker)); // ranged: twice against workers, and no loss shooting uphill
        Assert.Equal(6, Damage(archer, rider));
    }

    [Fact]
    public void ArchersOnHighGroundReachAndSeeAQuarterFurther()
    {
        foreach (bool hill in new[] { true, false })
        {
            var w = TestWorlds.Flat(64, 16, 64, Ground);
            if (hill) TestWorlds.Box(w, 0, Ground + 1, 0, 20, Ground + 2, 63, BlockType.Stone); // two levels up
            var sim = TestWorlds.Sim(w);
            int archer = sim.SpawnUnit(0, "archer", 20, 30);
            // Eleven blocks off: beyond an archer's range (9) and sight (10), within both a quarter further.
            int worker = sim.SpawnUnit(1, "worker", 31, 30);
            sim.Run(2 * Tps);
            Assert.Equal(hill, Hp(sim, worker) < 40);
            Assert.Equal(20, sim.Units.X[Index(sim, archer)].FloorToInt());
        }
    }

    [Fact]
    public void ArrowsNeedAClearLineButFlyThroughLeaves()
    {
        foreach (var wall in new[] { BlockType.Stone, BlockType.Leaves, BlockType.Air })
        {
            var w = TestWorlds.Flat(64, 16, 64, Ground);
            TestWorlds.Box(w, 25, Ground + 1, 0, 25, Ground + 3, 63, wall); // wall to wall, three high
            var sim = TestWorlds.Sim(w);
            sim.SpawnUnit(0, "archer", 20, 30);
            int worker = sim.SpawnUnit(1, "worker", 28, 30);
            sim.Run(5 * Tps);
            Assert.Equal(wall != BlockType.Stone, Hp(sim, worker) < 40);
        }
    }

    [Fact]
    public void HitsLandTogetherSoNeitherSideStrikesFirst()
    {
        var sim = Flat();
        int a = sim.SpawnUnit(0, "spearman", 20, 30);
        int b = sim.SpawnUnit(1, "spearman", 21, 30);
        sim.Units.Hp[Index(sim, a)] = sim.Units.Hp[Index(sim, b)] = 1; // one hit each
        sim.Schedule(0, new AttackCommand(0, new[] { a }, b));
        sim.Schedule(0, new AttackCommand(1, new[] { b }, a));
        sim.Step();
        Assert.False(sim.Units.Exists(a));
        Assert.False(sim.Units.Exists(b));
    }

    [Fact]
    public void UnitsDoNotWasteBlowsOnTheDoomed()
    {
        var sim = Flat();
        // Two archers shoot at the same worker, which one arrow kills: the second shoots the other worker.
        var archers = new[] { sim.SpawnUnit(0, "archer", 20, 30), sim.SpawnUnit(0, "archer", 20, 31) };
        var workers = new[] { sim.SpawnUnit(1, "worker", 26, 30), sim.SpawnUnit(1, "worker", 26, 32) };
        foreach (int w in workers) sim.Units.Hp[Index(sim, w)] = 10;
        sim.Schedule(0, new AttackCommand(0, archers, workers[0]));
        sim.Step();
        Assert.Equal(0, Alive(sim, workers));
    }

    [Fact]
    public void AttacksWaitForTheCooldown()
    {
        var sim = Flat();
        int spear = sim.SpawnUnit(0, "spearman", 20, 20);
        int worker = sim.SpawnUnit(1, "worker", 21, 20);
        sim.Schedule(0, new AttackCommand(0, new[] { spear }, worker));
        var def = sim.Content.Unit("spearman");
        // Counted in wounds, not hit points: the spear's wounds bleed between blows.
        int Wounded() { int w = Index(sim, worker); return Enum.GetValues<BodyPart>().Sum(p => sim.Units.Wound(w, p)); }
        sim.Step(); // the first blow
        int afterOne = Wounded();
        Assert.True(afterOne > 0);
        sim.Run(def.AttackTicks - 1);
        Assert.Equal(afterOne, Wounded());
        sim.Run(1);
        Assert.True(Wounded() > afterOne);
    }

    [Fact]
    public void SplashHitsEnemiesAroundTheTargetButNotFriends()
    {
        var sim = Flat();
        int mage = sim.SpawnUnit(0, "mage", 20, 20);
        var clump = new[] { sim.SpawnUnit(1, "worker", 25, 20), sim.SpawnUnit(1, "worker", 26, 20), sim.SpawnUnit(1, "worker", 25, 21) };
        int apart = sim.SpawnUnit(1, "worker", 25, 24);
        int friend = sim.SpawnUnit(0, "worker", 24, 20);
        sim.Schedule(0, new AttackCommand(0, new[] { mage }, clump[0]));
        sim.Run(1);
        foreach (int id in clump) Assert.InRange(Hp(sim, id), 1, 39); // each hurt where the blast found it
        Assert.Equal(40, Hp(sim, apart));
        Assert.Equal(40, Hp(sim, friend));
    }

    // --- Orders ------------------------------------------------------------------------------------

    [Fact]
    public void AnAttackMoveFightsOnTheWayAndAPlainMoveDoesNot()
    {
        foreach (bool attack in new[] { true, false })
        {
            var sim = Flat();
            int spear = sim.SpawnUnit(0, "spearman", 10, 30);
            int worker = sim.SpawnUnit(1, "worker", 30, 31); // in the way; workers do not start fights
            sim.Schedule(0, attack ? new AttackMoveCommand(0, new[] { spear }, 50, 30) : new MoveCommand(0, new[] { spear }, 50, 30));
            RunUntil(sim, () => sim.Units.X[Index(sim, spear)].FloorToInt() == 50 && sim.Units.Order[Index(sim, spear)] == UnitOrder.Idle,
                60 * Tps, "the spearman to get there");
            Assert.Equal(attack, !sim.Units.Exists(worker));
            Assert.Equal(CombatOrder.Auto, sim.Units.Combat[Index(sim, spear)]);
        }
    }

    [Fact]
    public void AnAttackOrderChasesItsTarget()
    {
        var sim = Flat();
        int rider = sim.SpawnUnit(0, "rider", 10, 30);
        int worker = sim.SpawnUnit(1, "worker", 20, 30);
        sim.Schedule(0, new MoveCommand(1, new[] { worker }, 55, 55)); // it runs
        sim.Schedule(0, new AttackCommand(0, new[] { rider }, worker));
        RunUntil(sim, () => !sim.Units.Exists(worker), 20 * Tps, "the rider to catch the worker");
        sim.Step();
        Assert.Equal(CombatOrder.Auto, sim.Units.Combat[Index(sim, rider)]);
    }

    [Fact]
    public void ASoldierOnARaidThatIsAttackedDropsTheRaidAndFightsBack()
    {
        var sim = Flat();
        var store = Place(sim, 0, "storehouse", 10, 10);
        int raider = sim.SpawnUnit(1, "spearman", 40, 40);
        sim.Schedule(0, new CaptureCommand(1, new[] { raider }, store.Id));
        sim.Run(5 * Tps);
        Assert.Equal(UnitJob.Capture, sim.Units.Job[Index(sim, raider)]);
        int archer = sim.SpawnUnit(0, "archer", 30, 10);
        sim.Schedule(sim.Tick, new AttackCommand(0, new[] { archer }, raider));
        RunUntil(sim, () => sim.Units.Job[Index(sim, raider)] == UnitJob.None, 10 * Tps, "the raider to drop the raid");
        Assert.Equal(archer, sim.Units.Target[Index(sim, raider)]);
        Assert.Equal(0, store.Owner);
    }

    [Fact]
    public void IdleSoldiersFightEnemiesTheySeeButWorkersDoNot()
    {
        var sim = Flat();
        int spear = sim.SpawnUnit(0, "spearman", 20, 20);
        int worker = sim.SpawnUnit(0, "worker", 20, 30);
        int enemy = sim.SpawnUnit(1, "worker", 26, 20); // 6 blocks from the spearman, which sees 8
        int far = sim.SpawnUnit(1, "worker", 50, 50);
        sim.Run(CombatSystem.AcquireInterval);
        Assert.Equal(enemy, sim.Units.Target[Index(sim, spear)]);
        Assert.Equal((0, UnitOrder.Idle), (sim.Units.Target[Index(sim, worker)], sim.Units.Order[Index(sim, worker)]));
        sim.Run(10 * Tps);
        Assert.False(sim.Units.Exists(enemy));
        Assert.True(sim.Units.Exists(far));
        Assert.Equal(UnitOrder.Idle, sim.Units.Order[Index(sim, spear)]);
        Assert.Equal((1, 1, 0, 0), (sim.Combat.Losses(1), sim.Combat.Kills(0), sim.Combat.Losses(0), sim.Combat.Kills(1)));
    }

    [Fact]
    public void ASoldierGivesUpAChaseItStartedItself()
    {
        var sim = Flat();
        int spear = sim.SpawnUnit(0, "spearman", 10, 20);
        int rider = sim.SpawnUnit(1, "rider", 17, 20);
        sim.Run(CombatSystem.AcquireInterval);
        Assert.Equal(rider, sim.Units.Target[Index(sim, spear)]);
        // The rider rides off; the spearman follows a little and then stands again.
        sim.Schedule(sim.Tick, new MoveCommand(1, new[] { rider }, 60, 20));
        sim.Run(8 * Tps);
        int i = Index(sim, spear);
        Assert.Equal((UnitOrder.Idle, 0), (sim.Units.Order[i], sim.Units.Target[i]));
        Assert.InRange(sim.Units.X[i].FloorToInt(), 11, 40);
    }

    [Fact]
    public void AnAttackOrderFollowsTheTargetAroundAWall()
    {
        var w = TestWorlds.Flat(64, 16, 64, Ground);
        TestWorlds.Box(w, 30, Ground + 1, 5, 30, Ground + 5, 50, BlockType.Stone); // a wall with a gap at the north end
        var sim = TestWorlds.Sim(w);
        int spear = sim.SpawnUnit(0, "spearman", 10, 30);
        int target = sim.SpawnUnit(1, "worker", 50, 30);
        sim.Schedule(0, new AttackCommand(0, new[] { spear }, target));
        RunUntil(sim, () => !sim.Units.Exists(target), 60 * Tps, "the spearman to get round the wall to the worker");
    }

    [Fact]
    public void OnlyEnemiesCanBeAttackedAndOnlyByTheirOwner()
    {
        var sim = Flat();
        int spear = sim.SpawnUnit(0, "spearman", 20, 20);
        int friend = sim.SpawnUnit(0, "worker", 21, 20);
        int enemy = sim.SpawnUnit(1, "spearman", 40, 40);
        sim.Schedule(0, new AttackCommand(0, new[] { spear }, friend));
        sim.Schedule(0, new AttackCommand(1, new[] { spear }, friend)); // not player 1's unit
        sim.Run(5);
        Assert.Equal((CombatOrder.Auto, UnitOrder.Idle), (sim.Units.Combat[Index(sim, spear)], sim.Units.Order[Index(sim, spear)]));
        Assert.Equal(40, Hp(sim, friend));
        sim.Schedule(sim.Tick, new AttackCommand(0, new[] { spear }, enemy));
        sim.Run(1);
        Assert.Equal((CombatOrder.Attack, UnitOrder.Chase), (sim.Units.Combat[Index(sim, spear)], sim.Units.Order[Index(sim, spear)]));
        sim.Schedule(sim.Tick, new StopCommand(0, new[] { spear }));
        sim.Run(1);
        Assert.Equal((CombatOrder.Auto, UnitOrder.Idle, 0), (sim.Units.Combat[Index(sim, spear)], sim.Units.Order[Index(sim, spear)], sim.Units.Target[Index(sim, spear)]));
    }

    [Fact]
    public void ADefendingSoldierFightsOffARaider()
    {
        var sim = Flat();
        var store = Place(sim, 0, "storehouse", 10, 10);
        int raider = sim.SpawnUnit(1, "worker", 30, 12);
        int guard = sim.SpawnUnit(0, "spearman", 18, 12);
        sim.Schedule(0, new CaptureCommand(1, new[] { raider }, store.Id));
        sim.Run(20 * Tps);
        Assert.False(sim.Units.Exists(raider));
        Assert.True(sim.Units.Exists(guard));
        Assert.Equal(0, store.Owner);
    }

    // --- Morale ------------------------------------------------------------------------------------

    [Fact]
    public void LosingHalfASquadBreaksTheRestAndTheyRunFromTheEnemy()
    {
        var sim = Flat();
        var squad = Army(sim, 0, "spearman", 4, 20, -1, 30, perRow: 2);
        int archer = sim.SpawnUnit(1, "archer", 28, 31);
        var u = sim.Units;
        // Two of them have one arrow left in them.
        u.Hp[Index(sim, squad[0])] = u.Hp[Index(sim, squad[1])] = 1;

        RunUntil(sim, () => Alive(sim, squad) == 3, 5 * Tps, "the first death");
        // Each of the four near the death (the dead included) takes a quarter of the shock.
        foreach (int s in squad.Where(u.Exists))
            Assert.InRange(u.Morale[Index(sim, s)], CombatSystem.MaxMorale - CombatSystem.Shock / 4, CombatSystem.MaxMorale - CombatSystem.Shock / 4 + 2);
        Assert.Equal(CombatSystem.MaxMorale, u.Morale[Index(sim, archer)]); // no higher than the cap
        Assert.All(squad.Where(u.Exists), s => Assert.Equal(0, u.Rout[Index(sim, s)]));

        RunUntil(sim, () => Alive(sim, squad) == 2, 5 * Tps, "the second death");
        var left = squad.Where(u.Exists).ToArray();
        int ax = u.X[Index(sim, archer)].FloorToInt();
        foreach (int s in left)
        {
            int i = Index(sim, s);
            Assert.True(u.Rout[i] > 0, "a spearman left with half the squad dead routs");
            Assert.Equal(UnitOrder.Move, u.Order[i]);
            Assert.True(Math.Abs(u.OrderX[i] - ax) > Math.Abs(u.X[i].FloorToInt() - ax), "it runs away from the archer");
        }
        Assert.Equal(0, u.Rout[Index(sim, archer)]);

        // While it runs it does not take orders; then it rallies.
        int first = Index(sim, left[0]);
        int runX = u.OrderX[first], runZ = u.OrderZ[first];
        sim.Schedule(sim.Tick, new MoveCommand(0, left, 10, 50));
        sim.Step();
        first = Index(sim, left[0]);
        Assert.Equal((runX, runZ), (u.OrderX[first], u.OrderZ[first]));
        RunUntil(sim, () => !u.Exists(left[0]) || u.Rout[Index(sim, left[0])] == 0, CombatSystem.RoutTicks + 2, "the rally");
        if (u.Exists(left[0])) Assert.True(u.Morale[Index(sim, left[0])] >= CombatSystem.RallyMorale);
    }

    [Fact]
    public void ArmiesBreakUnderHeavyLossesAndTheWinnersHoldFirm()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(128, 24, 64, Ground));
        var archers = Army(sim, 0, "archer", 12, 36, -1, 26);
        var spears = Army(sim, 1, "spearman", 12, 50, 1, 26);
        sim.Schedule(0, new AttackMoveCommand(0, archers, 60, 31));
        sim.Schedule(0, new AttackMoveCommand(1, spears, 26, 31));
        bool spearsBroke = false, archersBroke = false;
        for (int t = 0; t < 60 * Tps && Alive(sim, spears) > 0; t++)
        {
            sim.Step();
            spearsBroke |= spears.Any(s => sim.Units.Exists(s) && sim.Units.Rout[Index(sim, s)] > 0);
            archersBroke |= archers.Any(a => sim.Units.Exists(a) && sim.Units.Rout[Index(sim, a)] > 0);
        }
        Assert.True(spearsBroke, "the losing side breaks before the end");
        Assert.False(archersBroke, "the winning side holds");
    }

    [Fact]
    public void HungryArmiesHaveLessMorale()
    {
        var sim = Flat();
        Place(sim, 0, "town_hall", 10, 10); // and no food
        int spear = sim.SpawnUnit(0, "spearman", 30, 30);
        Assert.Equal(CombatSystem.MaxMorale, sim.Combat.MoraleCap(0));
        sim.Run(EconomySystem.UpkeepInterval + 1);
        Assert.True(sim.Economy.Starving(0));
        Assert.Equal(CombatSystem.StarvingMorale, sim.Combat.MoraleCap(0));
        // It sinks a point a second down to the cap.
        sim.Run((CombatSystem.MaxMorale - CombatSystem.StarvingMorale + 2) * Tps);
        Assert.Equal(CombatSystem.StarvingMorale, sim.Units.Morale[Index(sim, spear)]);
    }

    // --- Buildings ---------------------------------------------------------------------------------

    [Fact]
    public void HitsWearDownTheColumnNearestTheAttackerFromTheTop()
    {
        var sim = Flat();
        var house = Place(sim, 0, "house", 20, 20);
        int y = house.Y;
        int spear = sim.SpawnUnit(1, "spearman", 19, 21); // beside the west wall, level with z = 21
        sim.Schedule(0, new AttackBuildingCommand(1, new[] { spear }, house.Id));
        RunUntil(sim, () => house.Lost > 0, 20 * Tps, "the first block to come out");
        Assert.False(BlockInfo.IsBuilt(sim.World.Get(20, y + 3, 21))); // the roof over the nearest column
        Assert.Equal(BlockType.Planks, sim.World.Get(20, y + 2, 21)); // the wall under it still stands
        Assert.Equal(house.Def.BlockToughness(house.Def.BlockAt(0, 3, 1)), house.Lost); // a block of thatch

        RunUntil(sim, () => !BlockInfo.IsBuilt(sim.World.Get(20, y, 21)), 60 * Tps, "the column to be worn down");
        for (int k = 0; k <= 3; k++) Assert.False(BlockInfo.IsBuilt(sim.World.Get(20, y + k, 21)));
        Assert.Equal(BlockType.Planks, sim.World.Get(20, y, 22));
        Assert.Same(house, sim.Buildings.Get(house.Id)); // a gap, but the house stands
        Assert.True(house.Health < house.MaxHealth);
    }

    [Fact]
    public void ASappersBlowBringsDownTheWholeColumn()
    {
        var sim = Flat();
        var house = Place(sim, 0, "house", 20, 20);
        int y = house.Y;
        int sapper = sim.SpawnUnit(1, "sapper", 19, 21);
        sim.Schedule(0, new AttackBuildingCommand(1, new[] { sapper }, house.Id));
        RunUntil(sim, () => house.Lost > 0, 20 * Tps, "the first block to come out");
        for (int k = 0; k <= 3; k++) Assert.Equal(BlockType.Air, sim.World.Get(20, y + k, 21));
        int column = 0;
        for (int k = 0; k <= 3; k++) column += house.Def.BlockToughness(house.Def.BlockAt(0, k, 1));
        Assert.Equal(column, house.Lost); // three of planks and one of thatch
        Assert.Equal(BlockType.Planks, sim.World.Get(20, y, 22));
    }

    [Fact]
    public void ABuildingThatLosesHalfOfItselfCollapses()
    {
        var sim = Flat();
        var house = Place(sim, 0, "house", 20, 20);
        var spears = Army(sim, 1, "spearman", 4, 18, -1, 20);
        sim.Schedule(0, new AttackBuildingCommand(1, spears, house.Id));
        for (int t = 0; t < 120 * Tps && sim.Buildings.Get(house.Id) != null; t++)
        {
            Assert.True(house.Lost * 2 < house.Built, "it stands only while it has more than half left");
            sim.Step();
        }
        Assert.Null(sim.Buildings.Get(house.Id));
        for (int x = 20; x < 24; x++)
        for (int z = 20; z < 24; z++)
        for (int k = 0; k <= house.Def.Height; k++)
            Assert.False(BlockInfo.IsBuilt(sim.World.Get(x, house.Y + k, z))); // gone, or rubble now
        // With nothing left to hit, they look for something else to do.
        sim.Step();
        Assert.All(spears, s => Assert.Equal(CombatOrder.Auto, sim.Units.Combat[Index(sim, s)]));
    }

    // --- Tunnels -----------------------------------------------------------------------------------

    [Fact]
    public void SappersTunnelThroughARidgeAndOthersCanFollow()
    {
        var w = TestWorlds.Flat(64, 16, 64, Ground);
        int y = Ground + 1;
        TestWorlds.Box(w, 30, y, 0, 37, y + 5, 63, BlockType.Stone); // a ridge, wall to wall, too steep to climb
        var sim = TestWorlds.Sim(w);
        int sapper = sim.SpawnUnit(0, "sapper", 26, 30);
        sim.Schedule(0, new TunnelCommand(0, new[] { sapper }, 42, y, 30));
        sim.Step();
        Assert.Equal(UnitJob.Tunnel, sim.Units.Job[Index(sim, sapper)]);
        RunUntil(sim, () => sim.Units.Job[Index(sim, sapper)] == UnitJob.None, 120 * Tps, "the tunnel");
        int i = Index(sim, sapper);
        Assert.Equal((42, 30), (sim.Units.X[i].FloorToInt(), sim.Units.Z[i].FloorToInt()));
        for (int x = 30; x <= 37; x++)
        {
            Assert.Equal(BlockType.Air, w.Get(x, y, 30));
            Assert.Equal(BlockType.Air, w.Get(x, y + 1, 30));
            Assert.Equal(BlockType.Stone, w.Get(x, y + 2, 30)); // a roof over it
            Assert.Equal(BlockType.Stone, w.Get(x, y, 31));
        }
        int spear = sim.SpawnUnit(0, "spearman", 26, 34);
        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { spear }, 45, 34));
        RunUntil(sim, () => sim.Units.X[Index(sim, spear)].FloorToInt() == 45, 60 * Tps, "the spearman to walk through");
    }

    [Fact]
    public void ATunnelJustUnderAWallBringsItDownIntoASumpAndTheSapperDigsOn()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(64, 20, 64, 8)); // deep enough ground for a sump
        var house = Place(sim, 1, "house", 30, 30);
        int y = house.Y;
        int sapper = sim.SpawnUnit(0, "sapper", 24, 31);
        int hp = sim.Units.Hp[Index(sim, sapper)];
        // Two levels down, so the tunnel's roof is the ground the wall stands on.
        sim.Schedule(0, new TunnelCommand(0, new[] { sapper }, 38, y - 2, 31));
        sim.Step();
        int lowest = int.MaxValue;
        RunUntil(sim, () =>
        {
            for (int k = 3; k <= 6; k++)
                if (sim.World.Get(30, y - k, 31) == BlockType.Air) lowest = Math.Min(lowest, y - k);
            return sim.Units.Job[Index(sim, sapper)] == UnitJob.None;
        }, 240 * Tps, "the tunnel");
        // It dug a sump under the wall before taking its footing away, so the wall fell into that, not on the
        // sapper, which then dug on through the rubble to the far side.
        Assert.True(lowest < y - 2, "no sump under the wall");
        Assert.Equal(38, sim.Units.X[Index(sim, sapper)].FloorToInt());
        Assert.Equal(hp, sim.Units.Hp[Index(sim, sapper)]);
        for (int x = 30; x <= 33; x++)
        {
            for (int k = 0; k <= house.Def.Height; k++) Assert.False(BlockInfo.IsBuilt(sim.World.Get(x, y + k, 31)));
            // The house is hollow: its side walls (x 30 and 33) fell into sumps; inside them only the floor and
            // the roof above it came down, little enough to land in the tunnel, where the sapper dug through it.
            if (x is 30 or 33) Assert.True(BlockInfo.IsRubble(sim.World.Get(x, y - 3, 31)));
            Assert.Equal(BlockType.Planks, sim.World.Get(x, y, 30));
            Assert.Equal(x is 30 or 33 ? BlockType.Planks : BlockType.Floor, sim.World.Get(x, y, 32));
        }
        Assert.Same(house, sim.Buildings.Get(house.Id));
        Assert.True(house.Lost > 0);
    }

    [Fact]
    public void TunnelsStopAtBuildingsWaterAndBedrock()
    {
        // At the house's own level the tunnel runs into its wall.
        var sim = Flat();
        var house = Place(sim, 1, "house", 30, 30);
        int sapper = sim.SpawnUnit(0, "sapper", 24, 31);
        sim.Schedule(0, new TunnelCommand(0, new[] { sapper }, 38, house.Y, 31));
        sim.Step();
        RunUntil(sim, () => sim.Units.Job[Index(sim, sapper)] == UnitJob.None, 60 * Tps, "the tunnel to stop");
        Assert.Equal(29, sim.Units.X[Index(sim, sapper)].FloorToInt());
        Assert.Equal(0, house.Lost);

        // Down toward bedrock, it stops a level above it.
        sim = Flat();
        sapper = sim.SpawnUnit(0, "sapper", 24, 31);
        sim.Schedule(0, new TunnelCommand(0, new[] { sapper }, 40, 0, 31));
        sim.Step();
        RunUntil(sim, () => sim.Units.Job[Index(sim, sapper)] == UnitJob.None, 60 * Tps, "the tunnel to stop");
        Assert.Equal(1, sim.Units.Y[Index(sim, sapper)]);
        Assert.Equal(BlockType.Bedrock, sim.World.Get(28, 0, 31));

        // Water: it does not dig into a flooded block.
        var w = TestWorlds.Flat(64, 16, 64, Ground);
        TestWorlds.Box(w, 28, Ground + 1, 0, 28, Ground + 2, 63, BlockType.Stone);
        w.Set(28, Ground + 1, 31, BlockType.Water);
        sim = TestWorlds.Sim(w);
        sapper = sim.SpawnUnit(0, "sapper", 24, 31);
        sim.Schedule(0, new TunnelCommand(0, new[] { sapper }, 40, Ground + 1, 31));
        sim.Step();
        RunUntil(sim, () => sim.Units.Job[Index(sim, sapper)] == UnitJob.None, 60 * Tps, "the tunnel to stop");
        Assert.Equal(27, sim.Units.X[Index(sim, sapper)].FloorToInt());
        Assert.Equal(BlockType.Water, sim.World.Get(28, Ground + 1, 31));
    }

    [Fact]
    public void OnlySappersTunnel()
    {
        var sim = Flat();
        int spear = sim.SpawnUnit(0, "spearman", 24, 31);
        sim.Schedule(0, new TunnelCommand(0, new[] { spear }, 40, Ground, 31));
        sim.Step();
        Assert.Equal(UnitJob.None, sim.Units.Job[Index(sim, spear)]);
    }

    // --- Determinism and replays -------------------------------------------------------------------

    private static Simulation SiegeAndBattle()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(128, 24, 64, Ground));
        var house = Place(sim, 1, "house", 70, 30);
        var a = Army(sim, 0, "spearman", 6, 36, -1, 26).Concat(Army(sim, 0, "archer", 6, 34, -1, 27)).ToArray();
        var b = Army(sim, 1, "rider", 4, 50, 1, 26).Concat(Army(sim, 1, "mage", 4, 52, 1, 27)).ToArray();
        var sappers = Army(sim, 0, "sapper", 3, 36, -1, 40);
        sim.Schedule(0, new AttackMoveCommand(0, a, 60, 31));
        sim.Schedule(0, new AttackMoveCommand(1, b, 26, 31));
        sim.Schedule(0, new AttackBuildingCommand(0, sappers.Take(2).ToArray(), house.Id));
        sim.Schedule(0, new TunnelCommand(0, sappers.Skip(2).ToArray(), 72, Ground - 1, 40));
        sim.Run(40 * Tps);
        return sim;
    }

    [Fact]
    public void TheSameBattleGivesTheSameHash()
    {
        var first = SiegeAndBattle();
        var second = SiegeAndBattle();
        Assert.Equal(first.StateHash(), second.StateHash());
        Assert.True(first.Units.Count < 6 + 6 + 4 + 4 + 3, "some fell");
    }

    [Fact]
    public void CombatCommandsRoundTripThroughTheLog()
    {
        var log = new CommandLog { Seed = 5, SizeX = 64, SizeY = 32, SizeZ = 64, Ticks = 100, StartUnits = 3 };
        log.Add(1, new AttackCommand(0, new[] { 1, 2 }, 7));
        log.Add(2, new AttackBuildingCommand(1, new[] { 8 }, 3));
        log.Add(3, new AttackMoveCommand(0, new[] { 4, 5 }, 30, 40));
        log.Add(3, new AttackMoveCommand(1, new[] { 6 }, 30, 40, 12));
        log.Add(4, new TunnelCommand(1, new[] { 9 }, 20, 5, 22));
        var copy = CommandLog.Parse(log.ToString());
        Assert.Equal(log.Commands, copy.Commands);
        Assert.Equal(log.ToString(), copy.ToString());
    }
}
