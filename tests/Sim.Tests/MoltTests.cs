using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Content;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Issue #153: the Deepfolk's Hardened chitin (hit points) becomes Molting, and Brood warrens doubles each brood
/// chamber's housing.
/// </summary>
public class MoltTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Tps = Simulation.TicksPerSecond;

    private static Simulation Flat(int size = 64) => TestWorlds.Sim(TestWorlds.Flat(size, 16, size, Ground));

    private static void Learn(Simulation sim, int player) => sim.Economy.Grant(player, sim.Content.Tech("deep_chitin"));

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);
    private static int Hp(Simulation sim, int id) => sim.Units.Exists(id) ? sim.Units.Hp[Index(sim, id)] : 0;

    /// <summary>A skitterling of player 0's, hurt down to a fifth of its hit points.</summary>
    private static int Hurt(Simulation sim, int x = 20, int z = 20, string unit = "skitterling")
    {
        int id = sim.SpawnUnit(0, unit, x, z);
        int i = Index(sim, id);
        sim.Units.Hp[i] = sim.MaxHpOf(i) / 5;
        return id;
    }

    [Fact]
    public void MoltingReplacesHardenedChitinsHitPoints()
    {
        var db = ContentLoader.LoadEmbedded();
        var tech = db.Tech("deep_chitin");
        Assert.Equal("Molting", tech.Name);
        Assert.All(tech.Effects, e => Assert.Equal(TechEffectType.Molt, e.Type));
        Assert.Equal(new UnitRole?[] { UnitRole.LineInfantry, UnitRole.Ranged, UnitRole.Sapper }, tech.Effects.Select(e => e.Role));
        var m = db.Combat.Molt;
        Assert.Equal((25, 15 * Tps, 30 * Tps, 60, 150), (m.Below, m.Ticks, m.SoftTicks, m.Pace, m.Tender));

        var sim = Flat();
        int bug = sim.SpawnUnit(0, "skitterling", 20, 20);
        int hp = Hp(sim, bug);
        Learn(sim, 0);
        Assert.Equal(hp, Hp(sim, bug)); // no more hit points
        Assert.Equal(sim.Content.Unit("skitterling").MaxHp, sim.MaxHpOf(Index(sim, bug)));
    }

    [Fact]
    public void OnlyBadlyHurtUnitsWithTheTechMolt()
    {
        var sim = Flat();
        int hurt = Hurt(sim);
        int fresh = sim.SpawnUnit(0, "skitterling", 24, 20);
        sim.Schedule(0, new MoltCommand(0, new[] { hurt, fresh }));
        sim.Run(2);
        Assert.False(sim.Combat.Molting(Index(sim, hurt))); // no tech yet
        Learn(sim, 0);
        sim.Schedule(sim.Tick, new MoltCommand(0, new[] { hurt, fresh }));
        sim.Run(2);
        Assert.True(sim.Combat.Molting(Index(sim, hurt)));
        Assert.False(sim.Combat.Molting(Index(sim, fresh))); // not hurt enough
        // Another player's hurt hive unit: that player has no Molting.
        int theirs = sim.SpawnUnit(1, "skitterling", 40, 40);
        sim.Units.Hp[Index(sim, theirs)] = 5;
        sim.Schedule(sim.Tick, new MoltCommand(1, new[] { theirs }));
        sim.Run(2);
        Assert.False(sim.Combat.Molting(Index(sim, theirs)));
    }

    [Fact]
    public void AMoltingUnitStandsStillAndHealsToFullThenStaysSoftAWhile()
    {
        var sim = Flat();
        Learn(sim, 0);
        int bug = Hurt(sim);
        sim.Schedule(0, new MoltCommand(0, new[] { bug }));
        sim.Step();
        int i = Index(sim, bug);
        var (x, z) = (sim.Units.X[i], sim.Units.Z[i]);
        // An order to walk waits until it has molted.
        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { bug }, 50, 20));
        int start = Hp(sim, bug);
        sim.Run(sim.Content.Combat.Molt.Ticks / 2);
        i = Index(sim, bug);
        Assert.Equal((x, z), (sim.Units.X[i], sim.Units.Z[i]));
        Assert.InRange(Hp(sim, bug), start + 1, sim.MaxHpOf(i) - 1); // healing, not there yet
        sim.Run(sim.Content.Combat.Molt.Ticks / 2 + 2);
        i = Index(sim, bug);
        Assert.Equal(sim.MaxHpOf(i), Hp(sim, bug));
        Assert.False(sim.Combat.Molting(i));
        Assert.False(sim.Combat.Shelled(i)); // soft
        Assert.Equal(sim.Content.Combat.Molt.Pace, sim.Combat.MoltPace(i));
        Assert.True(sim.Units.X[i] > x, "it walks on once it has molted");
        sim.Run(sim.Content.Combat.Molt.SoftTicks);
        i = Index(sim, bug);
        Assert.True(sim.Combat.Shelled(i)); // hard again
        Assert.Equal(100, sim.Combat.MoltPace(i));
    }

    [Fact]
    public void ASoftUnitWalksSlower()
    {
        int Walked(bool soft)
        {
            var sim = Flat();
            Learn(sim, 0);
            int bug = sim.SpawnUnit(0, "skitterling", 10, 20);
            if (soft) sim.Units.Soft[Index(sim, bug)] = 100 * Tps;
            sim.Schedule(0, new MoveCommand(0, new[] { bug }, 60, 20));
            sim.Run(4 * Tps);
            return sim.Units.X[Index(sim, bug)].FloorToInt() - 10;
        }
        int hard = Walked(false), soft = Walked(true);
        Assert.True(soft * 100 < hard * 75, $"soft {soft} columns, hard {hard}");
    }

    [Fact]
    public void AMoltingUnitHasNoArmourAndAHitOnItTakesMore()
    {
        var sim = Flat();
        Learn(sim, 0);
        int bug = Hurt(sim);
        int spear = sim.SpawnUnit(1, "spearman", 30, 30);
        int i = Index(sim, bug), t = Index(sim, spear);
        var def = sim.Content.Unit("spearman");
        Assert.True(sim.Combat.Armour(i, BodyPart.Torso) > 0);
        int before = sim.Combat.HitDamage(t, def, i, BodyPart.Torso);
        sim.Schedule(0, new MoltCommand(0, new[] { bug }));
        sim.Run(2);
        i = Index(sim, bug);
        t = Index(sim, spear);
        Assert.True(sim.Combat.Molting(i));
        Assert.Equal(0, sim.Combat.Armour(i, BodyPart.Torso));
        int during = sim.Combat.HitDamage(t, def, i, BodyPart.Torso);
        Assert.True(during > before, $"molting {during}, not {before}");
        // A spitter has no armour to lose: it is still hit harder while soft.
        int spitter = Hurt(sim, 26, 20, "spitter");
        int s = Index(sim, spitter);
        int shelled = sim.Combat.HitDamage(Index(sim, spear), def, s, BodyPart.Torso);
        sim.Units.Soft[s] = Tps;
        Assert.True(sim.Combat.HitDamage(Index(sim, spear), def, s, BodyPart.Torso) > shelled);
    }

    [Fact]
    public void AMoltingUnitDoesNotFight()
    {
        var sim = Flat();
        Learn(sim, 0);
        int bug = Hurt(sim);
        int worker = sim.SpawnUnit(1, "worker", 21, 20);
        sim.Schedule(0, new MoltCommand(0, new[] { bug }));
        int hp = Hp(sim, worker);
        sim.Run(5 * Tps);
        Assert.True(sim.Combat.Molting(Index(sim, bug)));
        Assert.Equal(hp, Hp(sim, worker));
    }

    [Fact]
    public void AHurtUnitLeftIdleOutOfTheFightMoltsByItself()
    {
        var sim = Flat();
        Learn(sim, 0);
        int bug = Hurt(sim);
        sim.Units.LastFight[Index(sim, bug)] = 1;
        sim.Run(sim.Content.Combat.RecoveryDelayTicks + 2 * Tps);
        Assert.True(sim.Combat.Molting(Index(sim, bug)) || sim.Units.Hp[Index(sim, bug)] == sim.MaxHpOf(Index(sim, bug)));
        Assert.Equal(1, sim.Combat.Molts);
        // One still walking somewhere does not.
        int busy = Hurt(sim, 20, 30);
        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { busy }, 60, 60));
        sim.Run(3 * Tps);
        Assert.False(sim.Combat.Molting(Index(sim, busy)));
    }

    [Fact]
    public void BroodWarrensDoublesEachBroodChambersHousing()
    {
        var sim = Flat();
        var chamber = sim.Economy.PlaceFinished(0, sim.Content.Building("brood_chamber"), 20, 20)!;
        int base_ = chamber.Def.Housing;
        Assert.Equal(base_, sim.Economy.HousingOf(chamber));
        sim.Economy.Grant(0, sim.Content.Tech("farming"));
        sim.Economy.Grant(0, sim.Content.Tech("deep_warrens"));
        Assert.Equal(2 * base_, sim.Economy.HousingOf(chamber));
    }

    [Fact]
    public void AHousingEffectTakesAnAmountOrAPercentNotBoth()
    {
        var files = ContentLoader.Files.ToDictionary(f => f, f => ContentLoader.EmbeddedText(f)!);
        files["techs.json"] = files["techs.json"].Replace("\"building\": \"brood_chamber\", \"percent\": 100",
            "\"building\": \"brood_chamber\", \"percent\": 100, \"amount\": 3");
        var e = Assert.Throws<ContentException>(() => ContentLoader.Load(files));
        Assert.Contains("techs.json: tech 'deep_warrens', effect 1: give an \"amount\" or a \"percent\", not both", e.Errors);
    }

    [Fact]
    public void TheMoltCommandRoundTripsThroughTheLog()
    {
        var log = new CommandLog();
        log.Add(3, new MoltCommand(0, new[] { 4, 5 }));
        var back = CommandLog.Parse(log.ToString());
        Assert.Equal(log.Commands.Select(c => c.Command), back.Commands.Select(c => c.Command));
    }

    [Fact]
    public void ComputerPlayersPullBadlyHurtUnitsBackAndMoltThemAwayFromTheFight()
    {
        foreach (var level in new[] { AiLevel.Classic, AiLevel.Normal, AiLevel.Hard })
        {
            var sim = AiTests.WithStarts(96, (30, 48));
            Learn(sim, 0);
            var hall = sim.Economy.TownHall(0)!;
            // One hurt next to an enemy spearman, far from home; one hurt at home with no enemy near.
            int front = Hurt(sim, hall.X + 40, hall.Z);
            int home = Hurt(sim, hall.X - 3, hall.Z);
            int enemy = sim.SpawnUnit(1, "spearman", hall.X + 44, hall.Z);
            sim.Schedule(0, new StanceCommand(1, new[] { enemy }, UnitStance.Hold));
            sim.Schedule(0, new StanceCommand(0, new[] { front }, UnitStance.Hold));
            var ai = new AiPlayer(0, 1, level);
            var log = new List<Command>();
            AiTests.PlayLogged(sim, new[] { ai }, log, () => log.OfType<MoltCommand>().Any(), 5 * Tps);
            var first = log.OfType<MoltCommand>().FirstOrDefault();
            Assert.True(first != null, $"{level}: a molt");
            Assert.Contains(home, first!.Units);
            Assert.DoesNotContain(front, first.Units); // the enemy is too close
            Assert.Contains(log.OfType<MoveCommand>(), m => m.Units.Contains(front)); // it is pulled back
            Assert.Contains(front, ai.Molters);
            int fx = sim.Units.X[Index(sim, front)].FloorToInt();
            // Away from the enemy, it molts too.
            AiTests.PlayLogged(sim, new[] { ai }, log, () => sim.Units.Exists(front) && sim.Combat.Molting(Index(sim, front)), 40 * Tps);
            Assert.True(sim.Units.Exists(front) && sim.Combat.Molting(Index(sim, front)), $"{level}: the pulled-back unit molts");
            Assert.True(sim.Units.X[Index(sim, front)].FloorToInt() < fx, $"{level}: it went home first");
        }
    }
}
