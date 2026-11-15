using System;
using System.Linq;
using Rts.Sim.Content;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Armour and where hits land (issue #71): body parts, attack kinds, wounds, bleeding and shields.</summary>
public class ArmourTests
{
    private const int Ground = 3;
    private const int Tps = Simulation.TicksPerSecond;
    private const int Settlers = 0;

    private static Simulation Flat(ulong seed = 1) => TestWorlds.Sim(TestWorlds.Flat(64, 16, 64, Ground), seed);
    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);
    private static readonly BodyPart[] Parts = Enum.GetValues<BodyPart>();

    /// <summary>The damage unit a's hits on unit t do on average, over where they land from level ground, in hundredths.</summary>
    private static long Expected(Simulation sim, int a, int t)
    {
        int ai = Index(sim, a), ti = Index(sim, t);
        var def = sim.Content.Units[sim.Units.Def[ai]];
        var plan = sim.Content.Units[sim.Units.Def[ti]].Body;
        return Parts.Sum(p => (long)Body.Odds(plan, p) * sim.Combat.HitDamage(ai, def, ti, p));
    }

    [Fact]
    public void AHelmetAndMailTakeMostOffASwordAndTheHeadHurtsMostAndTheArmsLeast()
    {
        var sim = Flat();
        int knight = sim.SpawnUnit(Settlers, "knight", 20, 30); // a longsword: slash
        int bare = sim.SpawnUnit(1, "spearman", 21, 30);
        int armoured = sim.SpawnUnit(1, "spearman", 21, 32);
        var u = sim.Units;
        // A helmet and a mail shirt found for the one spearman: on top of its own leather.
        int at = Index(sim, armoured) * Body.Parts;
        u.ExtraArmour[at + (int)BodyPart.Head] = 4;
        u.ExtraArmour[at + (int)BodyPart.Torso] = 6;
        u.ExtraArmour[at + (int)BodyPart.Arms] = 2;
        u.ExtraArmour[at + (int)BodyPart.Legs] = 2;
        Assert.Equal(5, sim.Combat.Armour(Index(sim, armoured), BodyPart.Head));
        Assert.Equal(7, sim.Combat.Armour(Index(sim, armoured), BodyPart.Torso));

        long plain = Expected(sim, knight, bare), mailed = Expected(sim, knight, armoured);
        Assert.True(mailed * 4 < plain * 3, $"a sword does {plain / 100.0} a hit on a spearman and {mailed / 100.0} on one in mail");

        // At the same armour, a head hit does more than a torso hit, and that more than an arm hit.
        int k = Index(sim, knight), t = Index(sim, bare);
        var def = sim.Content.Unit("knight");
        int head = sim.Combat.HitDamage(k, def, t, BodyPart.Head), torso = sim.Combat.HitDamage(k, def, t, BodyPart.Torso);
        int arms = sim.Combat.HitDamage(k, def, t, BodyPart.Arms), legs = sim.Combat.HitDamage(k, def, t, BodyPart.Legs);
        Assert.True(head > torso && torso > legs && legs > arms, $"head {head}, torso {torso}, legs {legs}, arms {arms}");

        // However thick the armour, a hit always does something.
        for (int p = 0; p < Body.Parts; p++) u.ExtraArmour[at + p] = 100;
        foreach (var p in Parts)
            Assert.True(sim.Combat.HitDamage(k, def, Index(sim, armoured), p) >= 1);
        Assert.Equal(Body.Damage(24, BodyPlan.Humanoid, BodyPart.Torso, AttackKind.Slash, 0) / 4,
            sim.Combat.HitDamage(k, def, Index(sim, armoured), BodyPart.Torso));
    }

    [Fact]
    public void SlashPierceAndCrushDifferAgainstArmourAndOnlySlashAndPierceBleed()
    {
        // Against the same armour (a knight's mail, 5 on the torso), the same 20-point blow.
        int slash = Body.Damage(20, BodyPlan.Humanoid, BodyPart.Torso, AttackKind.Slash, 5);
        int pierce = Body.Damage(20, BodyPlan.Humanoid, BodyPart.Torso, AttackKind.Pierce, 5);
        int crush = Body.Damage(20, BodyPlan.Humanoid, BodyPart.Torso, AttackKind.Crush, 5);
        Assert.True(slash < pierce && pierce < crush, $"slash {slash}, pierce {pierce}, crush {crush}");
        // With no armour they are all the same.
        Assert.Equal(20, Body.Damage(20, BodyPlan.Humanoid, BodyPart.Torso, AttackKind.Slash, 0));
        Assert.Equal(20, Body.Damage(20, BodyPlan.Humanoid, BodyPart.Torso, AttackKind.Crush, 0));

        Assert.Equal(AttackKind.Slash, ContentDb.Default.Unit("knight").Attack);
        Assert.Equal(AttackKind.Pierce, ContentDb.Default.Unit("spearman").Attack);
        Assert.Equal(AttackKind.Crush, ContentDb.Default.Unit("worker").Attack);

        // A spear (pierce) leaves a bleeding wound; a worker's hammer (crush) leaves a wound that does not bleed.
        foreach (var (attacker, bleeds) in new[] { ("spearman", true), ("knight", true), ("worker", false) })
        {
            var sim = Flat();
            int a = sim.SpawnUnit(Settlers, attacker, 20, 30);
            int t = sim.SpawnUnit(1, "spearman", 21, 30);
            sim.Units.Hp[Index(sim, t)] = 1000;
            sim.Schedule(0, new AttackCommand(Settlers, new[] { a }, t));
            sim.Step();
            int ti = Index(sim, t);
            var shot = sim.Combat.Shots.Single(s => s.Attacker == a);
            var part = shot.Part!.Value;
            Assert.Equal(1000 - shot.Damage, sim.Units.Hp[ti]);
            Assert.Equal(shot.Damage, sim.Units.Wound(ti, part));
            Assert.Equal(shot.Damage, Parts.Sum(p => sim.Units.Wound(ti, p)));
            Assert.Equal(bleeds, sim.Units.IsBleeding(ti, part));
            Assert.Equal(bleeds ? 1 << (int)part : 0, sim.Units.Bleeding[ti]);
        }
    }

    /// <summary>Head hits and all hits landed by each side's archers, one side on a plateau four levels up.</summary>
    private static (int HighHeads, int HighHits, int LowHeads, int LowHits) HillHeads(ulong seed)
    {
        var w = TestWorlds.Flat(128, 24, 64, Ground);
        TestWorlds.Box(w, 0, Ground + 1, 0, 50, Ground + 4, 63, BlockType.Stone);
        var sim = TestWorlds.Sim(w, seed);
        var high = Enumerable.Range(0, 12).Select(k => sim.SpawnUnit(0, "archer", 50 - k / 6, 26 + k % 6 * 2)).ToHashSet();
        var low = Enumerable.Range(0, 12).Select(k => sim.SpawnUnit(1, "archer", 58 + k / 6, 26 + k % 6 * 2)).ToArray();
        foreach (int id in high.Concat(low)) sim.Units.Hp[Index(sim, id)] = 10000; // nobody falls: count a long exchange
        var r = (HighHeads: 0, HighHits: 0, LowHeads: 0, LowHits: 0);
        for (int t = 0; t < 30 * Tps; t++)
        {
            long tick = sim.Tick;
            sim.Step();
            foreach (var s in sim.Combat.Shots)
            {
                if (s.Tick != tick || s.Part is not { } part) continue;
                bool head = part == BodyPart.Head;
                if (high.Contains(s.Attacker)) { r.HighHits++; if (head) r.HighHeads++; }
                else { r.LowHits++; if (head) r.LowHeads++; }
            }
        }
        return r;
    }

    [Fact]
    public void ArchersOnAHillLandMoreHeadHitsThanTheSameArchersBelow()
    {
        var r = HillHeads(1);
        Assert.True(r.HighHits > 100 && r.LowHits > 100, $"{r}");
        // From the high ground a fifth of the hits find the head, against a tenth from level ground.
        Assert.True(r.HighHeads * r.LowHits > 3 * r.LowHeads * r.HighHits / 2, $"{r}");
        Assert.Equal(r, HillHeads(1)); // the same seed lands the same hits
    }

    [Fact]
    public void BlowsStruckUphillFindTheLegsAndBlowsFromAboveTheHead()
    {
        int Count(BodyPlan plan, Body.Angle angle, BodyPart part) => Enumerable.Range(0, 100).Count(roll => Body.Pick(plan, angle, roll) == part);
        foreach (var plan in new[] { BodyPlan.Humanoid, BodyPlan.Beast })
        {
            foreach (var part in Parts) Assert.Equal(Body.Odds(plan, part), Count(plan, Body.Angle.Level, part));
            Assert.True(Count(plan, Body.Angle.Uphill, BodyPart.Legs) > Count(plan, Body.Angle.Level, BodyPart.Legs));
            Assert.Equal(0, Count(plan, Body.Angle.Uphill, BodyPart.Head));
            Assert.True(Count(plan, Body.Angle.FromAbove, BodyPart.Head) > Count(plan, Body.Angle.Level, BodyPart.Head));
        }
        Assert.Equal(0, Count(BodyPlan.Beast, Body.Angle.Level, BodyPart.Arms)); // beasts have no arms

        // So a melee blow uphill does less on average than the same blow on level ground.
        var w = TestWorlds.Flat(64, 16, 64, Ground);
        TestWorlds.Box(w, 21, Ground + 1, 0, 63, Ground + 1, 63, BlockType.Stone); // a step up, one level
        var sim = TestWorlds.Sim(w);
        int spear = sim.SpawnUnit(0, "spearman", 20, 30);
        int up = sim.SpawnUnit(1, "rider", 21, 30);
        int level = sim.SpawnUnit(1, "rider", 20, 31);
        var c = sim.Combat;
        int s = Index(sim, spear);
        Assert.Equal(Body.Angle.Uphill, c.AngleOf(s, Index(sim, up)));
        Assert.Equal(Body.Angle.Level, c.AngleOf(s, Index(sim, level)));
        Assert.Equal(Body.Angle.FromAbove, c.AngleOf(Index(sim, up), s));
        long Average(int t, Body.Angle angle) =>
            Parts.Sum(p => (long)Count(BodyPlan.Humanoid, angle, p) * c.HitDamage(s, sim.Content.Unit("spearman"), Index(sim, t), p));
        Assert.True(Average(up, Body.Angle.Uphill) < Average(level, Body.Angle.Level));
    }

    [Fact]
    public void AShieldStopsSomeHitsFromTheFrontButNoneFromBehind()
    {
        var sim = Flat();
        int knight = sim.SpawnUnit(Settlers, "knight", 20, 30);
        int front = sim.SpawnUnit(1, "spearman", 21, 30);
        int behind = sim.SpawnUnit(1, "spearman", 19, 30);
        foreach (int id in new[] { knight, front, behind }) sim.Units.Hp[Index(sim, id)] = 100000;
        // The knight faces the spearman it fights; the other comes at its back.
        sim.Schedule(0, new AttackCommand(Settlers, new[] { knight }, front));
        sim.Schedule(0, new AttackCommand(1, new[] { front, behind }, knight));
        int frontHits = 0, frontBlocked = 0, behindHits = 0, behindBlocked = 0;
        for (int t = 0; t < 40 * Tps; t++)
        {
            long tick = sim.Tick;
            sim.Step();
            foreach (var s in sim.Combat.Shots.Where(s => s.Tick == tick))
            {
                if (s.Attacker == front) { frontHits++; if (s.Blocked) frontBlocked++; }
                if (s.Attacker == behind) { behindHits++; if (s.Blocked) behindBlocked++; }
                if (s.Blocked) Assert.Equal(0, s.Damage);
            }
        }
        Assert.True(sim.Combat.FromFront(Index(sim, front), Index(sim, knight)));
        Assert.False(sim.Combat.FromFront(Index(sim, behind), Index(sim, knight)));
        Assert.Equal(40, frontHits);
        Assert.Equal(40, behindHits);
        Assert.InRange(frontBlocked, 5, 20); // three in ten, give or take
        Assert.Equal(0, behindBlocked);
        Assert.Equal(frontBlocked, sim.Combat.Blocks);

        // A unit standing idle, with nothing to face, turns to whoever hits it.
        int idle = sim.SpawnUnit(Settlers, "knight", 40, 40);
        int other = sim.SpawnUnit(1, "spearman", 38, 40);
        Assert.True(sim.Combat.FromFront(Index(sim, other), Index(sim, idle)));
    }

    [Fact]
    public void HealingMendsTheWorstWoundFirst()
    {
        var sim = Flat();
        int priest = sim.SpawnUnit(Settlers, "battle_priest", 20, 30);
        int hurt = sim.SpawnUnit(Settlers, "spearman", 21, 30);
        var u = sim.Units;
        int i = Index(sim, hurt), at = i * Body.Parts;
        u.Hp[i] = 40;
        u.Wounds[at + (int)BodyPart.Torso] = 30;
        u.Wounds[at + (int)BodyPart.Legs] = 3;
        Assert.Equal(4, sim.Content.Unit("battle_priest").Heal);
        // Only the priest heals here: the spearman is kept "in the fight" so it does not mend on its own too.
        void Heal(int times)
        {
            for (int k = 0; k < times; k++)
                for (int t = 0; t < 2 * Tps; t++) { u.LastFight[Index(sim, hurt)] = sim.Tick; sim.Step(); }
        }
        Heal(1); // one heal of 4
        i = Index(sim, hurt);
        Assert.Equal(44, u.Hp[i]);
        Assert.Equal(26, u.Wound(i, BodyPart.Torso));
        Assert.Equal(3, u.Wound(i, BodyPart.Legs));
        // 28 more, each heal on the worst wound: the torso down to 2, then the last heal mends the legs
        // (now the worse, at 3) and 1 more off the torso.
        Heal(7);
        i = Index(sim, hurt);
        Assert.Equal(72, u.Hp[i]);
        Assert.Equal(1, u.Wound(i, BodyPart.Torso));
        Assert.Equal(0, u.Wound(i, BodyPart.Legs));
        Heal(2); // back to full health: nothing left
        i = Index(sim, hurt);
        Assert.Equal(80, u.Hp[i]);
        Assert.Equal(0, Parts.Sum(p => u.Wound(i, p)));
        Assert.True(sim.Units.Exists(priest));
    }

    [Fact]
    public void ArmourIsData()
    {
        var db = ContentDb.Default;
        var knight = db.Unit("knight");
        Assert.Equal(new[] { 4, 5, 3, 3 }, knight.Armour);
        Assert.Equal(30, knight.Shield);
        Assert.Equal(BodyPlan.Beast, db.Unit("wolf").Body);
        Assert.Equal(1, db.Unit("wolf").ArmourOn(BodyPart.Torso));
        Assert.Equal(BodyPlan.Humanoid, db.Unit("spearman").Body);
        // Forms wear more than what they come from: a knight more than a spearman.
        Assert.True(knight.Armour.Sum() > db.Unit("spearman").Armour.Sum());

        var files = ContentLoader.Files.ToDictionary(f => f, f => ContentLoader.EmbeddedText(f)!);
        string units = files["units.json"];
        units = units.Replace("\"attack\": \"slash\", \"armor\": { \"head\": 4,", "\"attack\": \"chop\", \"armor\": { \"neck\": 1, \"head\": 4,");
        units = units.Replace("\"body\": \"beast\", \"armor\": { \"body\": 1 }", "\"body\": \"beast\", \"armor\": { \"arms\": 1 }");
        files["units.json"] = units;
        var e = Assert.Throws<ContentException>(() => ContentLoader.Load(files));
        Assert.Contains(e.Errors, m => m.StartsWith("units.json: unit 'knight': \"attack\" is \"chop\", which is not one of: slash, pierce, crush"));
        Assert.Contains(e.Errors, m => m.StartsWith("units.json: unit 'knight', armor: unknown field \"neck\""));
        Assert.Contains(e.Errors, m => m.StartsWith("units.json: unit 'wolf', armor: unknown field \"arms\""));
    }

    [Fact]
    public void TheSameArmouredBattleGivesTheSameHash()
    {
        ulong Run()
        {
            var sim = Flat(7);
            var a = Enumerable.Range(0, 6).Select(k => sim.SpawnUnit(Settlers, k % 2 == 0 ? "knight" : "archer", 20, 26 + k * 2)).ToArray();
            var b = Enumerable.Range(0, 6).Select(k => sim.SpawnUnit(1, k % 2 == 0 ? "spearman" : "wolf", 30, 26 + k * 2)).ToArray();
            sim.Schedule(0, new AttackMoveCommand(Settlers, a, 40, 31));
            sim.Schedule(0, new AttackMoveCommand(1, b, 10, 31));
            sim.Run(20 * Tps);
            return sim.StateHash();
        }
        Assert.Equal(Run(), Run());
    }
}
