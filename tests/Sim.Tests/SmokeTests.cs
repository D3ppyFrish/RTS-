using System.Collections.Generic;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Content;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Clan Kage's smoke bombs (see "Clan Kage" in docs/rules.md, <c>Units/CombatSystem.Smoke.cs</c>): a cloud thrown at the
/// thrower's feet hides whoever is in it through the fog of war, and nobody in it fights or is fought unit to unit.
/// </summary>
public class SmokeTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Stand = Ground + 1;
    private const int Tps = Simulation.TicksPerSecond;

    private static Simulation Flat(int size = 64, bool fog = false, bool learn = true)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(size, 16, size, Ground));
        sim.Fog = fog;
        sim.SetFaction(0, "kage");
        if (learn) Learn(sim, 0);
        return sim;
    }

    private static void Learn(Simulation sim, int player) => sim.Economy.Grant(player, sim.Content.Tech("kage_smoke_bombs"));

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

    [Fact]
    public void SmokeBombsAreClanKagesTechForTheirSkirmishers()
    {
        var content = ContentDb.Default;
        var tech = content.Tech("kage_smoke_bombs");
        Assert.Equal("kage", tech.Faction?.Id);
        Assert.Equal(new[] { "kunoichi", "shinobi", "shuriken_thrower" },
            tech.Effects.Where(e => e.Type == TechEffectType.SmokeBombs).Select(e => e.Unit!.Id));
        var rules = content.Combat.Smoke;
        Assert.Equal((3, 2, 8 * Tps, 45 * Tps), (rules.Radius, rules.Height, rules.Ticks, rules.WaitTicks));

        var sim = Flat(learn: false);
        int kunoichi = sim.SpawnUnit(0, "kunoichi", 20, 20, Stand);
        int caster = sim.SpawnUnit(0, "shadow_caster", 22, 20, Stand);
        Assert.False(sim.Combat.CanSmoke(Index(sim, kunoichi))); // not before the tech
        Learn(sim, 0);
        Assert.True(sim.Combat.ReadyToSmoke(Index(sim, kunoichi)));
        Assert.False(sim.Combat.CanSmoke(Index(sim, caster))); // casters carry none
    }

    [Fact]
    public void ABombHangsInACloudForAWhileAndTheThrowerWaitsForTheNext()
    {
        var sim = Flat();
        int kunoichi = sim.SpawnUnit(0, "kunoichi", 20, 20, Stand);
        int friend = sim.SpawnUnit(0, "kunoichi", 22, 21, Stand); // within 3 columns
        int far = sim.SpawnUnit(0, "kunoichi", 26, 20, Stand); // outside the cloud
        sim.Schedule(0, new SmokeCommand(0, new[] { kunoichi }));
        sim.Run(1);
        Assert.Single(sim.Combat.Clouds);
        Assert.Equal(1, sim.Combat.SmokeBombs);
        Assert.True(sim.Combat.InSmoke(Index(sim, kunoichi)));
        Assert.True(sim.Combat.InSmoke(Index(sim, friend)));
        Assert.False(sim.Combat.InSmoke(Index(sim, far)));
        Assert.False(sim.Combat.ReadyToSmoke(Index(sim, kunoichi)));

        // A second throw before the wait is up does nothing.
        sim.Schedule(sim.Tick, new SmokeCommand(0, new[] { kunoichi }));
        sim.Run(1);
        Assert.Equal(1, sim.Combat.SmokeBombs);

        // The cloud thins out after its time, and the thrower is ready again after its wait.
        sim.Run(sim.Content.Combat.Smoke.Ticks);
        Assert.Empty(sim.Combat.Clouds);
        Assert.False(sim.Combat.InSmoke(Index(sim, kunoichi)));
        sim.Run(sim.Content.Combat.Smoke.WaitTicks);
        Assert.True(sim.Combat.ReadyToSmoke(Index(sim, kunoichi)));
    }

    [Fact]
    public void ThrowersStandingTogetherThrowOneBombForAll()
    {
        var sim = Flat();
        var ids = new[] { sim.SpawnUnit(0, "kunoichi", 20, 20, Stand), sim.SpawnUnit(0, "kunoichi", 21, 20, Stand),
            sim.SpawnUnit(0, "shinobi", 20, 21, Stand), sim.SpawnUnit(0, "shuriken_thrower", 30, 20, Stand) };
        sim.Schedule(0, new SmokeCommand(0, ids));
        sim.Run(1);
        Assert.Equal(2, sim.Combat.Clouds.Count); // one for the three together, one for the thrower further off
        Assert.Equal(2, ids.Count(id => !sim.Combat.ReadyToSmoke(Index(sim, id))));
    }

    [Fact]
    public void WithoutTheTechNobodyThrows()
    {
        var sim = Flat(learn: false);
        int kunoichi = sim.SpawnUnit(0, "kunoichi", 20, 20, Stand);
        sim.Schedule(0, new SmokeCommand(0, new[] { kunoichi }));
        sim.Run(1);
        Assert.Empty(sim.Combat.Clouds);
    }

    [Fact]
    public void NobodyFightsInTheSmoke()
    {
        // The same fight twice, once with a smoke bomb thrown: in the smoke nobody strikes a blow (wounds still bleed).
        int[] lost = new int[2];
        for (int run = 0; run < 2; run++)
        {
            bool smoke = run == 1;
            var sim = Flat();
            int kunoichi = sim.SpawnUnit(0, "kunoichi", 20, 20, Stand);
            int spear = sim.SpawnUnit(1, "spearman", 21, 20, Stand);
            int archer = sim.SpawnUnit(1, "archer", 26, 20, Stand); // outside the cloud, within range
            sim.Run(2 * Tps);
            Assert.NotEqual(0, sim.Units.Target[Index(sim, spear)]); // fighting
            if (smoke) sim.Schedule(sim.Tick, new SmokeCommand(0, new[] { kunoichi }));
            sim.Schedule(sim.Tick, new AttackCommand(1, new[] { archer }, kunoichi)); // even ordered to
            sim.Run(2);
            int hp = sim.Units.Hp[Index(sim, kunoichi)];
            sim.Run(5 * Tps);
            int k = Index(sim, kunoichi);
            lost[run] = hp - (k >= 0 ? sim.Units.Hp[k] : 0); // without the smoke it may well be dead
            if (!smoke) continue;
            Assert.True(sim.Combat.InSmoke(k));
            Assert.Equal(0, sim.Units.Target[k]);
            Assert.Equal(0, sim.Units.Target[Index(sim, spear)]);
            Assert.Equal(0, sim.Units.Target[Index(sim, archer)]);
            Assert.Equal(CombatOrder.Auto, sim.Units.Combat[Index(sim, archer)]); // the order is lost in the smoke

            // Once the smoke clears, the fight goes on.
            sim.Run(sim.Content.Combat.Smoke.Ticks);
            Assert.False(sim.Combat.InSmoke(Index(sim, kunoichi)));
            int kk = Index(sim, kunoichi), sp = Index(sim, spear), ar = Index(sim, archer);
            Assert.True(kk < 0 || sim.Units.Target[kk] != 0 || sp >= 0 && sim.Units.Target[sp] != 0 || ar >= 0 && sim.Units.Target[ar] != 0,
                $"k {(kk >= 0 ? sim.Units.Hp[kk] : -1)} t{(kk >= 0 ? sim.Units.Target[kk] : -1)} sp {(sp >= 0 ? sim.Units.Hp[sp] : -1)} rout {(sp >= 0 ? sim.Units.Rout[sp] : -1)} ar {ar}");
        }
        Assert.True(lost[1] * 3 < lost[0], $"lost {lost[1]} in the smoke, {lost[0]} without");
    }

    [Fact]
    public void ACloudHidesWhoeverIsInItThroughTheFog()
    {
        var sim = Flat(fog: true);
        int shuriken = sim.SpawnUnit(0, "shuriken_thrower", 20, 20, Stand);
        int spear = sim.SpawnUnit(1, "spearman", 22, 20, Stand); // close enough to spot even a hidden shinobi
        sim.Schedule(0, new StanceCommand(1, new[] { spear }, UnitStance.Hold));
        sim.Run(Tps);
        Assert.True(sim.Vision.SeesUnit(1, Index(sim, shuriken)));
        sim.Schedule(sim.Tick, new SmokeCommand(0, new[] { shuriken }));
        sim.Run(2);
        int s = Index(sim, shuriken);
        Assert.False(sim.Vision.SeesUnit(1, s));
        Assert.True(sim.Vision.SeesUnit(0, s)); // its own side sees it

        // Walked out of the cloud, it shows again.
        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { shuriken }, 20, 8, Stand));
        sim.Run(4 * Tps);
        s = Index(sim, shuriken);
        Assert.False(sim.Combat.InSmoke(s));
        Assert.True(sim.Units.Z[s].FloorToInt() < 16);
    }

    [Fact]
    public void SmokeIsLoggedAndReplays()
    {
        var log = new CommandLog();
        log.Add(12, new SmokeCommand(1, new[] { 7, 9 }));
        var back = CommandLog.Parse(log.ToString());
        Assert.Equal(new SmokeCommand(1, new[] { 7, 9 }), back.Commands.Single().Command);
    }

    [Fact]
    public void ComputerPlayersThrowSmokeToPullBadlyHurtUnitsOutOfAFight()
    {
        foreach (var level in new[] { AiLevel.Classic, AiLevel.Normal, AiLevel.Hard })
        {
            var sim = AiTests.WithStarts(96, (30, 48));
            sim.SetFaction(0, "kage");
            Learn(sim, 0);
            var hall = sim.Economy.TownHall(0)!;
            int front = sim.SpawnUnit(0, "kunoichi", hall.X + 40, hall.Z);
            sim.Units.Hp[Index(sim, front)] = 10;
            int enemy = sim.SpawnUnit(1, "spearman", hall.X + 42, hall.Z);
            var ai = new AiPlayer(0, 1, level);
            var log = new List<Command>();
            AiTests.PlayLogged(sim, new[] { ai }, log, () => log.OfType<SmokeCommand>().Any(), 5 * Tps);
            var smoke = log.OfType<SmokeCommand>().FirstOrDefault();
            Assert.True(smoke != null, $"{level}: a smoke bomb");
            Assert.Contains(front, smoke!.Units);
            Assert.Contains(log.OfType<MoveCommand>(), m => m.Units.Contains(front)); // sent home
            Assert.Contains(front, ai.Slipping);
            Assert.DoesNotContain(front, ai.Wave);
            _ = enemy;
        }
    }
}
