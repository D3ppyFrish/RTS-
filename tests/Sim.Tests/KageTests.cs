using System.Linq;
using Rts.Sim.Content;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Clan Kage, the ratfolk ninja clan (see "Clan Kage" in docs/rules.md): their own kunoichi, shinobi, shuriken throwers
/// and shadow casters in place of everyone's spearmen, riders, archers and mages; shinobi and kunoichi hiding while they
/// stand still (<see cref="UnitDef.Camouflage"/>); all of them shadowborn, softer in the daylight and harder in the
/// shadowed wilds (<see cref="UnitDef.Shadowborn"/>); the kunoichi an assassin (<see cref="UnitDef.Assassin"/>); and
/// Strike from the shadows (an <see cref="TechEffectType.Ambush"/> tech).
/// </summary>
public class KageTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Stand = Ground + 1;
    private const int Tps = Simulation.TicksPerSecond;

    private static Simulation Flat(int size = 64, bool fog = false)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(size, 16, size, Ground));
        sim.Fog = fog;
        return sim;
    }

    private static int Count(Simulation sim, int owner, string unit)
    {
        int def = sim.Content.Unit(unit).Index, n = 0;
        for (int i = 0; i < sim.Units.Count; i++)
            if (sim.Units.Owner[i] == owner && sim.Units.Def[i] == def) n++;
        return n;
    }

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

    [Fact]
    public void ClanKageIsTheSeventhFaction()
    {
        var content = ContentDb.Default;
        var kage = content.Faction("kage");
        Assert.Equal(6, kage.Index);
        Assert.Equal("Clan Kage", kage.Name);
        Assert.Equal(DuskTreeUse.Raze, kage.DuskTree);
        Assert.Equal(AiStyle.Waves, kage.AiStyle);
        Assert.Equal(new[] { ("spearman", "kunoichi"), ("rider", "shinobi"), ("archer", "shuriken_thrower"), ("mage", "shadow_caster") },
            kage.UnitReplacements.Select(r => (r.From.Id, r.To.Id)));
        var own = new[] { "kunoichi", "shinobi", "shuriken_thrower", "shadow_caster" }.Select(content.Unit).ToArray();
        Assert.All(own, d => Assert.True(d.Shadowborn, d.Id));
        Assert.True(content.Unit("kunoichi").Camouflage && content.Unit("shinobi").Camouflage);
        Assert.False(content.Unit("shuriken_thrower").Camouflage || content.Unit("shadow_caster").Camouflage);
        Assert.Equal(100, content.Unit("kunoichi").Assassin);
        Assert.True(content.Unit("shadow_caster").Fear > 0);
        Assert.True(content.Unit("shinobi").Sight > content.Unit("rider").Sight);
        Assert.True(content.Unit("shuriken_thrower").Range < content.Unit("archer").Range);
        Assert.False(content.Unit("spearman").Camouflage || content.Unit("spearman").Shadowborn);
        Assert.Equal("kage", content.Tech("kage_shadow_strike").Faction?.Id);
        Assert.Equal("kage", content.Building("moonless_shrine").Faction?.Id);
    }

    [Fact]
    public void ClanKageStartWithTheirOwnUnits()
    {
        var sim = Flat(96);
        sim.SetFaction(1, "kage");
        sim.Map.Starts.Add(new StartArea(0, 20, Stand, 30));
        sim.Map.Starts.Add(new StartArea(1, 70, Stand, 30));
        sim.SpawnStartingUnits(12);
        Assert.Equal(0, Count(sim, 1, "spearman") + Count(sim, 1, "archer") + Count(sim, 1, "mage") + Count(sim, 1, "rider"));
        Assert.Equal(Count(sim, 0, "spearman"), Count(sim, 1, "kunoichi"));
        Assert.Equal(Count(sim, 0, "rider"), Count(sim, 1, "shinobi"));
        Assert.Equal(Count(sim, 0, "archer"), Count(sim, 1, "shuriken_thrower"));
        Assert.Equal(Count(sim, 0, "mage"), Count(sim, 1, "shadow_caster"));
        Assert.True(sim.Economy.Unlocked(1, sim.Content.Unit("kunoichi")));
        Assert.False(sim.Economy.Unlocked(1, sim.Content.Unit("spearman")));
        Assert.False(sim.Economy.Unlocked(0, sim.Content.Unit("shinobi")));
    }

    [Fact]
    public void AShinobiStandingStillIsSeenOnlyFromCloseBy()
    {
        var sim = Flat(fog: true);
        int shinobi = sim.SpawnUnit(0, "shinobi", 30, 30, Stand);
        int spear = sim.SpawnUnit(1, "spearman", 36, 30, Stand); // well within sight both ways
        sim.Run(Tps);
        int s = Index(sim, shinobi);
        Assert.True(sim.Combat.Hiding(s));
        Assert.False(sim.Vision.SeesUnit(1, s));
        Assert.Equal(0, sim.Units.Target[Index(sim, spear)]); // nobody goes after it from further off
        Assert.Equal(0, sim.Units.Target[s]); // and it lies in wait rather than going after the spearman

        // Walking, it shows.
        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { shinobi }, 30, 10, Stand));
        sim.Run(3);
        s = Index(sim, shinobi);
        Assert.False(sim.Combat.Hiding(s));
        Assert.True(sim.Vision.SeesUnit(1, s));
        // Stopped again, it hides again.
        sim.Schedule(sim.Tick, new StopCommand(0, new[] { shinobi }));
        sim.Run(3);
        Assert.True(sim.Combat.Hiding(Index(sim, shinobi)));

        // Walked up close, the spearman spots it, and it springs at him.
        var close = Flat(fog: true);
        int hidden = close.SpawnUnit(0, "kunoichi", 30, 30, Stand);
        int near = close.SpawnUnit(1, "spearman", 32, 30, Stand);
        close.Run(Tps);
        int k = Index(close, hidden);
        Assert.True(close.Vision.SeesUnit(1, k));
        Assert.NotEqual(0, close.Units.Target[k]);
        Assert.NotEqual(0, close.Units.Target[Index(close, near)]);
    }

    [Fact]
    public void ShurikenThrowersAndEveryoneElseDoNotHide()
    {
        var sim = Flat(fog: true);
        int thrower = sim.SpawnUnit(0, "shuriken_thrower", 30, 30, Stand);
        int spear = sim.SpawnUnit(0, "spearman", 30, 34, Stand);
        sim.SpawnUnit(1, "worker", 35, 30, Stand);
        sim.Run(Tps);
        Assert.False(sim.Combat.Hiding(Index(sim, thrower)));
        Assert.False(sim.Combat.Lurking(Index(sim, spear)));
        Assert.True(sim.Vision.SeesUnit(1, Index(sim, thrower)));
    }

    [Fact]
    public void ShadowbornStrikeSofterInTheDaylightAndHarderInTheWilds()
    {
        var sim = Flat();
        int kunoichi = sim.SpawnUnit(0, "kunoichi", 20, 20, Stand);
        int spear = sim.SpawnUnit(1, "spearman", 21, 20, Stand);
        int ally = sim.SpawnUnit(0, "spearman", 20, 30, Stand);
        var def = sim.Content.Unit("kunoichi");
        // With day and night off it is never day: their full strength.
        Assert.Equal(100, sim.Combat.ShadowPercent(Index(sim, kunoichi)));
        int plain = sim.Combat.Damage(Index(sim, kunoichi), def, Index(sim, spear));
        // In the shadowed wilds a quarter harder, at any hour.
        sim.Map.SetBiome(20, 20, Biome.Wilds);
        Assert.Equal(CombatSystem.WildsPercent, sim.Combat.ShadowPercent(Index(sim, kunoichi)));
        Assert.InRange(sim.Combat.Damage(Index(sim, kunoichi), def, Index(sim, spear)), plain * 125 / 100 - 1, plain * 125 / 100 + 1);
        sim.Map.SetBiome(20, 20, Biome.Grassland);

        var day = Flat();
        day.DayNight = true;
        day.DayStart = 12 * 60; // noon
        int k = day.SpawnUnit(0, "kunoichi", 20, 20, Stand);
        int target = day.SpawnUnit(1, "spearman", 21, 20, Stand);
        int other = day.SpawnUnit(0, "spearman", 20, 30, Stand);
        Assert.True(day.Conditions.InDaylight(20, Stand, 20));
        Assert.Equal(CombatSystem.LightPercent, day.Combat.ShadowPercent(Index(day, k)));
        Assert.InRange(day.Combat.Damage(Index(day, k), def, Index(day, target)), plain * 80 / 100 - 1, plain * 80 / 100 + 1);
        // Everyone else strikes the same by day and by night.
        var spearDef = sim.Content.Unit("spearman");
        Assert.Equal(sim.Combat.Damage(Index(sim, ally), spearDef, Index(sim, spear)), day.Combat.Damage(Index(day, other), spearDef, Index(day, target)));
    }

    [Fact]
    public void AKunoichiStrikesWorkersCastersAndHeroesTwiceAsHard()
    {
        var sim = Flat();
        int kunoichi = sim.SpawnUnit(0, "kunoichi", 20, 20, Stand);
        int spear = sim.SpawnUnit(1, "spearman", 21, 20, Stand);
        int worker = sim.SpawnUnit(1, "worker", 21, 21, Stand);
        int mage = sim.SpawnUnit(1, "mage", 19, 21, Stand);
        int marshal = sim.SpawnUnit(1, "marshal", 19, 20, Stand);
        var def = sim.Content.Unit("kunoichi");
        int Hit(int target) => sim.Combat.Damage(Index(sim, kunoichi), def, Index(sim, target));
        Fix64 Table(string role) => sim.Content.Combat.Multiplier(UnitRole.LineInfantry, sim.Content.Unit(role).Role);
        int attack = def.Damage;
        Assert.Equal(System.Math.Max(1, (Fix64.FromInt(attack) * Table("spearman")).RoundToInt()), Hit(spear));
        Assert.Equal(System.Math.Max(1, (Fix64.FromInt(attack) * Table("worker") * 2).RoundToInt()), Hit(worker));
        Assert.Equal(System.Math.Max(1, (Fix64.FromInt(attack) * Table("mage") * 2).RoundToInt()), Hit(mage));
        Assert.Equal(System.Math.Max(1, (Fix64.FromInt(attack) * Table("marshal") * 2).RoundToInt()), Hit(marshal));
        Assert.True(CombatSystem.Marked(sim.Content.Unit("marshal")));
        Assert.False(CombatSystem.Marked(sim.Content.Unit("spearman")));
    }

    /// <summary>The damage of a kunoichi's first blow out of hiding at a spearman, with Strike from the shadows or not.</summary>
    private static (int First, long Strikes) FirstBlowOutOfHiding(bool shadowStrike)
    {
        var sim = Flat();
        if (shadowStrike) sim.Economy.Grant(0, sim.Content.Tech("kage_shadow_strike"));
        int kunoichi = sim.SpawnUnit(0, "kunoichi", 32, 30, Stand);
        sim.Run(2);
        Assert.Equal(shadowStrike, sim.Combat.LyingInWait(Index(sim, kunoichi)));
        int spear = sim.SpawnUnit(1, "spearman", 33, 30, Stand);
        sim.Units.Hp[Index(sim, spear)] = 1000;
        sim.Units.Morale[Index(sim, spear)] = 1000;
        sim.Schedule(sim.Tick, new AttackCommand(0, new[] { kunoichi }, spear));
        sim.Schedule(sim.Tick, new StopCommand(1, new[] { spear }));
        for (int t = 0; t < 10 * Tps && sim.Units.Hp[Index(sim, spear)] == 1000; t++) sim.Step();
        return (1000 - sim.Units.Hp[Index(sim, spear)], sim.Combat.LurkerStrikes);
    }

    [Fact]
    public void WithStrikeFromTheShadowsTheFirstBlowOutOfHidingStrikesTwiceAsHard()
    {
        var (plain, none) = FirstBlowOutOfHiding(shadowStrike: false);
        var (ambush, one) = FirstBlowOutOfHiding(shadowStrike: true);
        Assert.True(plain > 0);
        Assert.Equal(0, none);
        Assert.Equal(1, one);
        Assert.InRange(ambush, 2 * plain - 2, 2 * plain + 2);
    }

    [Fact]
    public void ClanKageMatchesAreTheSameEveryTime()
    {
        ulong Run()
        {
            var sim = Flat(96);
            sim.SetFaction(1, "kage");
            sim.Map.Starts.Add(new StartArea(0, 20, Stand, 30));
            sim.Map.Starts.Add(new StartArea(1, 70, Stand, 30));
            sim.SpawnStartingUnits(12);
            sim.SpawnUnit(1, "kunoichi", 40, 30, Stand);
            sim.SpawnUnit(0, "spearman", 45, 30, Stand);
            sim.Run(20 * Tps);
            return sim.StateHash();
        }
        Assert.Equal(Run(), Run());
    }
}
