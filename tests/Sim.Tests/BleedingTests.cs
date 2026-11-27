using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Bleeding wounds and blood on the ground (issue #72).</summary>
public class BleedingTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Tps = Simulation.TicksPerSecond;
    private const int Settlers = 0, Orcs = 1, Wild = Simulation.WildPlayer;

    private static Simulation Flat(int size = 96, ContentDb? content = null) =>
        new(new RegionMap(1, TestWorlds.Flat(size, 16, size, Ground)), content);

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);
    private static int Hp(Simulation sim, int id) => sim.Units.Exists(id) ? sim.Units.Hp[Index(sim, id)] : 0;

    private static Building Place(Simulation sim, int owner, string def, int x, int z) =>
        sim.Economy.PlaceFinished(owner, sim.Content.Building(def), x, z) ?? throw new InvalidOperationException($"{def} does not fit");

    private static void RunUntil(Simulation sim, Func<bool> done, int maxTicks, string what)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return;
            sim.Step();
        }
        Assert.Fail($"Not done after {maxTicks} ticks: {what}");
    }

    /// <summary>Gives unit id a bleeding wound on the part, of <paramref name="wound"/> hit points with <paramref name="bleed"/> still to bleed.</summary>
    private static void Cut(Simulation sim, int id, BodyPart part, int wound, int bleed)
    {
        var u = sim.Units;
        int i = Index(sim, id), at = i * Body.Parts + (int)part;
        u.Wounds[at] = (byte)wound;
        u.BleedLeft[at] = (byte)bleed;
        u.Bleeding[i] |= (byte)(1 << (int)part);
    }

    /// <summary>Keeps unit id "in the fight" so it does not heal on its own while a test watches it bleed.</summary>
    private static void Run(Simulation sim, int ticks, int keepFighting)
    {
        for (int t = 0; t < ticks; t++)
        {
            if (sim.Units.Exists(keepFighting)) sim.Units.LastFight[Index(sim, keepFighting)] = sim.Tick;
            sim.Step();
        }
    }

    [Fact]
    public void ABleedingWoundLosesHitPointsEachSecondUntilItClots()
    {
        var sim = Flat();
        int spear = sim.SpawnUnit(Settlers, "spearman", 20, 30);
        sim.Units.Hp[Index(sim, spear)] = 60;
        Cut(sim, spear, BodyPart.Torso, 20, 30);
        // 30 left to bleed: 1 a second and a point more for every 20 left, so 2, 2, 2, 2, 2, 2 (to 18), then 1s.
        Assert.Equal(2, sim.Combat.BleedLoss(30));
        Assert.Equal(1, sim.Combat.BleedLoss(19));
        Assert.Equal(3, sim.Combat.BleedLoss(40));
        Run(sim, Tps, spear);
        Assert.Equal(58, Hp(sim, spear));
        Run(sim, 40 * Tps, spear);
        int i = Index(sim, spear);
        Assert.Equal(30, Hp(sim, spear)); // all 30 bled away, no more
        Assert.Equal(0, sim.Units.Bleeding[i]);
        Assert.Equal(0, sim.Units.Bleed(i, BodyPart.Torso));
        Assert.Equal(20, sim.Units.Wound(i, BodyPart.Torso)); // the wound itself is still there to mend
    }

    [Fact]
    public void ASlashOrPierceWoundBleedsMoreOfItsDamageAndACrushWoundDoesNot()
    {
        foreach (var (attacker, bleeds) in new[] { ("spearman", true), ("knight", true), ("siege_engineer", false) })
        {
            var sim = Flat();
            int a = sim.SpawnUnit(Settlers, attacker, 20, 30);
            int t = sim.SpawnUnit(Orcs, "spearman", 21, 30);
            sim.Units.Hp[Index(sim, t)] = 1000;
            sim.Schedule(0, new AttackCommand(Settlers, new[] { a }, t));
            sim.Step();
            var shot = sim.Combat.Shots.Single(s => s.Attacker == a);
            int ti = Index(sim, t);
            Assert.Equal(bleeds ? Math.Max(1, shot.Damage * 40 / 100) : 0, sim.Units.Bleed(ti, shot.Part!.Value));
            Assert.Equal(bleeds, sim.Units.Bleeding[ti] != 0);
            Assert.Equal(bleeds ? a : 0, sim.Units.Cutter[ti]);
            // A blow that draws blood splashes it on the ground.
            Assert.Equal(bleeds, sim.Blood.At(21, 30) > 0);
        }
    }

    [Fact]
    public void BleedingCanKillAndTheCutterGetsTheKill()
    {
        var sim = Flat();
        int spear = sim.SpawnUnit(Settlers, "spearman", 20, 30);
        int victim = sim.SpawnUnit(Orcs, "worker", 40, 30);
        sim.Units.Hp[Index(sim, victim)] = 3;
        Cut(sim, victim, BodyPart.Legs, 30, 20);
        sim.Units.Cutter[Index(sim, victim)] = spear;
        RunUntil(sim, () => !sim.Units.Exists(victim), 5 * Tps, "the worker bleeds to death");
        Assert.Equal(1, sim.Combat.BledOut);
        Assert.Equal(1, sim.Combat.Losses(Orcs));
        Assert.Equal(1, sim.Combat.Kills(Settlers));
        // Its death leaves a pool where it fell.
        Assert.True(sim.Blood.At(40, 30) >= BloodSystem.Pool);
    }

    [Fact]
    public void WithoutBleedingDeathsBleedingStopsAtOneHitPoint()
    {
        var files = ContentLoader.Files.ToDictionary(f => f, f => ContentLoader.EmbeddedText(f)!);
        files["roles.json"] = files["roles.json"].Replace("\"kills\": true", "\"kills\": false");
        var sim = Flat(content: ContentLoader.Load(files));
        Assert.False(sim.Content.Combat.BleedKills);
        int victim = sim.SpawnUnit(Orcs, "worker", 40, 30);
        sim.Units.Hp[Index(sim, victim)] = 3;
        Cut(sim, victim, BodyPart.Legs, 30, 20);
        Run(sim, 30 * Tps, victim);
        Assert.Equal(1, Hp(sim, victim));
        Assert.Equal(0, sim.Combat.BledOut);
    }

    [Fact]
    public void AGrievousWoundNeverClotsAndKillsUnlessTreated()
    {
        var sim = Flat();
        int untreated = sim.SpawnUnit(Settlers, "spearman", 20, 30);
        int treated = sim.SpawnUnit(Settlers, "spearman", 60, 30);
        int priest = sim.SpawnUnit(Settlers, "battle_priest", 61, 30);
        foreach (int id in new[] { untreated, treated }) sim.Combat.OpenGrievousWound(Index(sim, id), BodyPart.Arms);
        Assert.Equal(255, sim.Units.Bleed(Index(sim, untreated), BodyPart.Arms));
        // 80 hit points at 4 a second: dead in 20 seconds, though no blow lands and nothing clots.
        RunUntil(sim, () => !sim.Units.Exists(untreated), 21 * Tps + 1, "the untreated spearman bleeds out");
        Assert.Equal(1, sim.Combat.BledOut);
        // The one beside a priest is stemmed and lives.
        int i = Index(sim, treated);
        Assert.Equal(0, sim.Units.Bleeding[i]);
        Assert.Equal(0, sim.Units.Grievous[i]);
        Assert.True(Hp(sim, treated) > 60);
        Assert.True(sim.Units.Exists(priest));
    }

    [Fact]
    public void ABattlePriestStemsBleedingAtOnceAndSeesToTheBleedingFirst()
    {
        var sim = Flat();
        int priest = sim.SpawnUnit(Settlers, "battle_priest", 20, 30);
        int bleeding = sim.SpawnUnit(Settlers, "spearman", 21, 30);
        int worse = sim.SpawnUnit(Settlers, "spearman", 20, 31); // more hurt, but not bleeding
        sim.Units.Hp[Index(sim, bleeding)] = 60;
        sim.Units.Hp[Index(sim, worse)] = 20;
        Cut(sim, bleeding, BodyPart.Arms, 20, 40);
        for (int t = 0; t < 3 * Tps && sim.Combat.Stemmed == 0; t++)
        {
            sim.Units.LastFight[Index(sim, bleeding)] = sim.Units.LastFight[Index(sim, worse)] = sim.Tick;
            sim.Step();
        }
        Assert.Equal(1, sim.Combat.Stemmed);
        int i = Index(sim, bleeding);
        Assert.Equal(0, sim.Units.Bleeding[i]);
        Assert.Equal(0, sim.Units.Bleed(i, BodyPart.Arms));
        Assert.Equal(20, Hp(sim, worse)); // it waits for the next heal
        Assert.True(sim.Units.Exists(priest));
    }

    [Fact]
    public void AUnitHealingAtHomeHasItsWoundsBound()
    {
        var sim = Flat();
        var hall = Place(sim, Settlers, "town_hall", 30, 30);
        int spear = sim.SpawnUnit(Settlers, "spearman", hall.X - 2, hall.Z);
        int away = sim.SpawnUnit(Settlers, "spearman", 80, 80);
        foreach (int id in new[] { spear, away })
        {
            sim.Units.Hp[Index(sim, id)] = 70;
            Cut(sim, id, BodyPart.Torso, 40, 30);
        }
        // Out of the fight for the recovery delay: the one at home stops bleeding, the one away does not.
        sim.Run(sim.Content.Combat.RecoveryDelayTicks + Tps);
        Assert.Equal(0, sim.Units.Bleeding[Index(sim, spear)]);
        Assert.NotEqual(0, sim.Units.Bleeding[Index(sim, away)]);
        Assert.True(Hp(sim, spear) > Hp(sim, away));
    }

    [Fact]
    public void ABleedingUnitLeavesATrailThatFadesAndRainFadesItFaster()
    {
        var sim = Flat();
        int spear = sim.SpawnUnit(Settlers, "spearman", 10, 30);
        sim.Units.Hp[Index(sim, spear)] = 80;
        Cut(sim, spear, BodyPart.Legs, 20, 60);
        sim.Schedule(0, new MoveCommand(Settlers, new[] { spear }, 60, 30, Ground + 1));
        RunUntil(sim, () => sim.Units.X[Index(sim, spear)].FloorToInt() >= 59, 30 * Tps, "the spearman walks east");
        // Blood on many columns along the way, on the grass under its feet.
        var trail = Enumerable.Range(10, 50).Where(x => sim.Blood.At(x, 30) > 0).ToList();
        Assert.True(trail.Count >= 15, $"only {trail.Count} stained columns");
        Assert.All(trail, x => Assert.Equal(Ground, sim.Blood.StainAt(x, 30)!.Value.Y));
        Assert.All(trail, x => Assert.Equal(spear, sim.Blood.StainAt(x, 30)!.Value.Source));

        // Gone after the blood time (a day by default), with the oldest of it first.
        int life = sim.Blood.LifeSeconds;
        Assert.Equal(Conditions.DayTicks / Tps, life);
        sim.Units.StopBleeding(Index(sim, spear));
        sim.Run(life * Tps / 2);
        Assert.True(sim.Blood.Count > 0);
        sim.Run(life * Tps / 2 + Tps);
        Assert.Equal(0, sim.Blood.Count);

        // In rain it is gone in a third of the time.
        var wet = Flat();
        wet.DayNight = true;
        int w = wet.SpawnUnit(Settlers, "spearman", 10, 30);
        Cut(wet, w, BodyPart.Legs, 20, 60);
        wet.Run(Tps);
        wet.Units.StopBleeding(Index(wet, w));
        Assert.True(wet.Blood.Count > 0);
        wet.Conditions.SetWeather(Weather.Rain, 10L * life * Tps, Weather.Rain);
        wet.Run(life * Tps / BloodSystem.RainAge + 2 * Tps);
        Assert.Equal(0, wet.Blood.Count);
    }

    [Fact]
    public void AStainRemembersWhereInTheColumnTheBloodFell()
    {
        var sim = Flat();
        sim.Blood.Spill(Fix64.FromRatio(41, 4), Ground, Fix64.FromRatio(83, 4), 10, 0); // (10.25, 20.75)
        var st = sim.Blood.StainAt(10, 20)!.Value;
        Assert.Equal((4, 12), (st.FX, st.FZ));
        Assert.Equal((10, Ground, 20), (st.X, st.Y, st.Z));
    }

    [Fact]
    public void DiggingOrFloodingAStainedBlockTakesItsBloodAway()
    {
        var sim = Flat();
        sim.Blood.Spill(20, Ground, 20, 50, 0);
        sim.Blood.Spill(21, Ground, 20, 50, 0);
        sim.Blood.Spill(22, Ground, 20, 50, 0);
        Assert.Equal(3, sim.Blood.Count);
        sim.World.Set(20, Ground, 20, BlockType.Air);       // dug out
        sim.World.Set(21, Ground + 1, 20, BlockType.Water); // water over it
        Assert.Equal(0, sim.Blood.At(20, 20));
        Assert.Equal(0, sim.Blood.At(21, 20));
        Assert.Equal(50, sim.Blood.At(22, 20));
        // Blood never falls on water, or on air.
        sim.Blood.Spill(21, Ground, 20, 50, 0);
        sim.Blood.Spill(20, Ground, 20, 50, 0);
        Assert.Equal(1, sim.Blood.Count);
    }

    [Fact]
    public void WorkersCleanTheBloodAroundWhereTheyAreSent()
    {
        var sim = Flat();
        // A field of stains around (40, 40), and one far off.
        for (int k = 0; k < 6; k++) sim.Blood.Spill(38 + k, Ground, 40 + k % 3, 60, 0);
        sim.Blood.Spill(70, Ground, 70, 60, 0);
        var workers = new[] { sim.SpawnUnit(Settlers, "worker", 30, 30), sim.SpawnUnit(Settlers, "worker", 31, 30) };
        int spear = sim.SpawnUnit(Settlers, "spearman", 32, 30);
        sim.Schedule(0, new CleanCommand(Settlers, workers.Append(spear).ToArray(), 40, 40));
        sim.Step();
        Assert.All(workers, w => Assert.Equal(UnitJob.Clean, sim.Units.Job[Index(sim, w)]));
        Assert.Equal(UnitJob.None, sim.Units.Job[Index(sim, spear)]); // soldiers do not clean
        // Two cleaners on different stains.
        Assert.NotEqual(sim.Units.JobX[Index(sim, workers[0])] * 1000 + sim.Units.JobZ[Index(sim, workers[0])],
            sim.Units.JobX[Index(sim, workers[1])] * 1000 + sim.Units.JobZ[Index(sim, workers[1])]);
        RunUntil(sim, () => workers.All(w => sim.Units.Job[Index(sim, w)] == UnitJob.None), 60 * Tps, "the field is cleaned");
        Assert.Equal(1, sim.Blood.Count); // only the far stain is left
        Assert.True(sim.Blood.At(70, 70) > 0);
        // It goes in the command log and replays.
        var log = new Rts.Sim.Replay.CommandLog();
        var cmd = new CleanCommand(0, new[] { 3, 4 }, 40, 41);
        log.Add(5, cmd);
        Assert.Equal(cmd, Rts.Sim.Replay.CommandLog.Parse(log.ToString()).Commands.Single().Command);
    }

    [Fact]
    public void BloodStaysUnderTheCapAndTheNewestShows()
    {
        var sim = Flat(160);
        for (int x = 0; x < 160; x++)
        for (int z = 0; z < 80; z++)
            sim.Blood.Spill(x, Ground, z, 1 + (x + z) % 10, 0);
        Assert.True(sim.Blood.Count <= BloodSystem.MaxStains);
        Assert.True(sim.Blood.At(159, 79) > 0);
    }

    [Fact]
    public void ABigBattleKeepsBloodUnderTheCap()
    {
        var sim = Flat(160);
        for (int k = 0; k < 500; k++)
        {
            sim.SpawnUnit(Settlers, "spearman", 40 + k % 25, 30 + k / 25 * 4 % 100);
            sim.SpawnUnit(Orcs, "spearman", 100 + k % 25, 30 + k / 25 * 4 % 100);
        }
        var all = Enumerable.Range(0, sim.Units.Count).Select(i => sim.Units.Id[i]).ToArray();
        sim.Schedule(0, new AttackMoveCommand(Settlers, all.Where(id => sim.Units.Owner[Index(sim, id)] == Settlers).ToArray(), 110, 80, Ground + 1));
        sim.Schedule(0, new AttackMoveCommand(Orcs, all.Where(id => sim.Units.Owner[Index(sim, id)] == Orcs).ToArray(), 50, 80, Ground + 1));
        int most = 0;
        for (int t = 0; t < 60 * Tps; t++)
        {
            sim.Step();
            most = Math.Max(most, sim.Blood.Count);
        }
        Assert.True(sim.Combat.Losses(Settlers) + sim.Combat.Losses(Orcs) > 100, "the armies to meet");
        Assert.True(most > 200, $"only {most} stains");
        Assert.True(most <= BloodSystem.MaxStains);
    }

    // --- Wolves on the trail ------------------------------------------------------------------------

    private const int DenX = 60, DenZ = 48;

    private static Lair PlaceLair(Simulation sim) =>
        sim.Monsters.Place(sim.Content.Unit("wolf"), DenX, DenZ) ?? throw new InvalidOperationException("no ground for the den");

    [Fact]
    public void AWolfThatFindsAFreshTrailFollowsItToTheBleedingUnit()
    {
        var sim = Flat();
        var lair = PlaceLair(sim);
        sim.Run(Tps); // the pack settles at its den
        // A bleeding worker passed by the den and went on, well past the pack's guard radius and leash.
        int worker = sim.SpawnUnit(Settlers, "worker", DenX, DenZ - 30);
        Assert.True(30 > sim.Content.Unit("wolf").Lair!.Leash);
        for (int k = 0; k < 6; k++) sim.Blood.Spill(DenX + 2 + k, Ground, DenZ, 20, worker);
        RunUntil(sim, () => lair.Trackers.Count > 0, 2 * Tps, "a wolf picks up the trail");
        int tracker = lair.Trackers[0];
        Assert.Equal(worker, sim.Units.Target[Index(sim, tracker)]);
        RunUntil(sim, () => !sim.Units.Exists(worker), 30 * Tps, "the wolf runs the worker down");
        Assert.True(sim.Units.Exists(tracker));
        // With its quarry dead it is a plain guard again, and goes home.
        sim.Run(MonsterSystem.GuardInterval + 1);
        Assert.Empty(lair.Trackers);
    }

    [Fact]
    public void WolvesIgnoreOldTrailsAndTrailsOfTheirOwn()
    {
        var sim = Flat();
        var lair = PlaceLair(sim);
        sim.Run(Tps);
        int worker = sim.SpawnUnit(Settlers, "worker", DenX, DenZ - 30);
        int wolf = sim.SpawnUnit(Wild, "wolf", DenX, DenZ + 30);
        for (int k = 0; k < 6; k++) sim.Blood.Spill(DenX + 2 + k, Ground, DenZ, 20, wolf);
        sim.Run(4 * Tps);
        Assert.Empty(lair.Trackers);
        // A trail is followed only while it is fresh.
        int other = sim.SpawnUnit(Settlers, "worker", 10, 10);
        sim.Blood.Spill(12, Ground, 12, 20, other);
        Assert.Equal(other, sim.Blood.FreshTrail(10, 10, 4, _ => true));
        sim.Run(BloodSystem.FreshSeconds * Tps + 1);
        Assert.Equal(0, sim.Blood.FreshTrail(10, 10, 4, _ => true));
        Assert.True(sim.Units.Exists(worker));
    }

    [Fact]
    public void AFreshTrailIsTheSameWhetherFewOrManyStainsLie()
    {
        var sim = Flat();
        int near = sim.SpawnUnit(Settlers, "worker", 10, 10);
        int far = sim.SpawnUnit(Settlers, "worker", 12, 12);
        int skipped = sim.SpawnUnit(Settlers, "worker", 14, 14);
        sim.Blood.Spill(11, Ground, 10, 20, near);
        sim.Run(Tps);
        sim.Blood.Spill(12, Ground, 12, 20, far); // fresher, so followed before the nearer one
        sim.Blood.Spill(10, Ground, 13, 20, skipped); // freshest, but ruled out
        bool Follow(int id) => id != skipped;
        // Few stains: the look goes over the stains themselves.
        Assert.Equal(far, sim.Blood.FreshTrail(10, 10, 4, Follow));
        // Many stains, all out of reach: the look goes over the columns round the spot, with the same answer.
        for (int k = 0; k < 100; k++) sim.Blood.Spill(40 + k % 10, Ground, 40 + k / 10, 20, near);
        Assert.True(sim.Blood.Count > 81);
        Assert.Equal(far, sim.Blood.FreshTrail(10, 10, 4, Follow));
        Assert.Equal(near, sim.Blood.FreshTrail(10, 10, 4, id => id == near));
    }
}
