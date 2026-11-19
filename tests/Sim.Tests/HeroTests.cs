using System;
using System.Collections.Generic;
using Rts.Sim.Content;
using Rts.Sim.Sandbox;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Heroes: the Settlers' last forms, one of a kind, that steady the soldiers around them (the design doc's
/// "units near a hero hold longer"). The first is the Marshal, a knight's form at level 2.
/// </summary>
public class HeroTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Tps = Simulation.TicksPerSecond;
    private const int Settlers = 0, Orcs = 1;

    // In test mode, so a unit can be struck dead (see Kill).
    private static Simulation Flat(int size = 96)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(size, 16, size, Ground));
        sim.TestMode = true;
        return sim;
    }

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

    private static string DefId(Simulation sim, int id) => sim.Content.Units[sim.Units.Def[Index(sim, id)]].Id;

    private static int Morale(Simulation sim, int id) => sim.Units.Morale[Index(sim, id)];

    /// <summary>A knight with the experience for its next form.</summary>
    private static int Veteran(Simulation sim, int x, int z, int owner = Settlers)
    {
        int id = sim.SpawnUnit(owner, "knight", x, z);
        sim.Units.Xp[Index(sim, id)] = 500;
        return id;
    }

    private static void Evolve(Simulation sim, int id, string form)
    {
        sim.Schedule(sim.Tick, new EvolveCommand(Settlers, new[] { id }, form));
        sim.Step();
    }

    /// <summary>Test mode strikes the unit dead this tick, as falling ground would.</summary>
    private static void Kill(Simulation sim, int id)
    {
        sim.Schedule(sim.Tick, new SandboxUnitsCommand(Settlers, new[] { id }, SandboxAction.Kill));
        for (int t = 0; t < 2 * Tps && sim.Units.Exists(id); t++) sim.Step();
        Assert.False(sim.Units.Exists(id));
    }

    [Fact]
    public void TheMarshalIsAKnightsFormAtTheSecondLevelAndIsAHero()
    {
        var db = ContentDb.Default;
        var marshal = db.Unit("marshal");
        Assert.Same(db.Unit("knight"), marshal.EvolvesFrom);
        Assert.Equal(2, marshal.Tier);
        Assert.True(marshal.Hero);
        Assert.Equal(Fix64.FromInt(8), marshal.Rally);
        Assert.Equal(50, marshal.Steady);
        Assert.Equal(3, marshal.Inspire);
        Assert.Contains(marshal, db.Unit("knight").Forms);
        // Its gain is hit points and damage; its drawback is upkeep.
        Assert.True(marshal.MaxHp > db.Unit("knight").MaxHp && marshal.Damage > db.Unit("knight").Damage);
        Assert.True(marshal.Upkeep > db.Unit("knight").Upkeep);

        var sim = Flat();
        int fresh = sim.SpawnUnit(Settlers, "knight", 20, 30);
        sim.Units.Xp[Index(sim, fresh)] = 499;
        Assert.False(sim.Evolution.CanEvolve(Index(sim, fresh), marshal)); // level 1 only
        int vet = Veteran(sim, 20, 40);
        Assert.Equal(2, sim.Evolution.Level(Index(sim, vet)));
        Evolve(sim, vet, "marshal");
        Assert.Equal("marshal", DefId(sim, vet));
        Assert.Equal(200, sim.Units.Hp[Index(sim, vet)]); // unhurt, so at full health
    }

    [Fact]
    public void APlayerHasOneMarshalAtATimeAndAnotherCanRiseOnceItFalls()
    {
        var sim = Flat();
        int a = Veteran(sim, 20, 30), b = Veteran(sim, 20, 40);
        int rival = Veteran(sim, 60, 60, owner: Orcs); // a rival's units are no hindrance (and orcs do not evolve)
        sim.Schedule(0, new EvolveCommand(Settlers, new[] { a, b }, "marshal"));
        sim.Step();
        // Both were picked; only the first became the Marshal.
        Assert.Equal("marshal", DefId(sim, a));
        Assert.Equal("knight", DefId(sim, b));
        Assert.False(sim.Evolution.CanEvolve(Index(sim, b), sim.Content.Unit("marshal")));
        Assert.False(sim.Evolution.CanEvolve(Index(sim, b))); // the Marshal is the knight's only form
        Assert.True(sim.Evolution.HasHero(Settlers, sim.Content.Unit("marshal")));
        Assert.True(sim.Evolution.WaitsForHero(Index(sim, b)));
        Assert.False(sim.Evolution.WaitsForHero(Index(sim, a)));
        Assert.False(sim.Evolution.HasHero(Orcs, sim.Content.Unit("marshal")));
        Assert.True(sim.Units.Exists(rival));

        // Automation on its standing choice does not make a second either.
        sim.Schedule(sim.Tick, new AutoEvolveCommand(Settlers, "knight", "marshal"));
        sim.Run(3 * Tps);
        Assert.Equal("knight", DefId(sim, b));

        // The Marshal falls; the next knight with the experience takes up the role on its own.
        Kill(sim, a);
        sim.Run(3 * Tps);
        Assert.False(sim.Units.Exists(a));
        Assert.Equal("marshal", DefId(sim, b));
    }

    /// <summary>
    /// The same death shakes a squad half as much with the Marshal among them (counted in the share too), and
    /// a squad out of its reach as before.
    /// </summary>
    [Fact]
    public void SoldiersNearTheMarshalLoseHalfTheMoraleToADeath()
    {
        int Shaken(bool withMarshal)
        {
            var sim = Flat();
            int doomed = sim.SpawnUnit(Settlers, "spearman", 30, 30);
            int near = sim.SpawnUnit(Settlers, "spearman", 32, 30);
            int other = sim.SpawnUnit(Settlers, "knight", 30, 33); // the Marshal or a plain knight: the same share either way
            if (withMarshal)
            {
                sim.Units.Xp[Index(sim, other)] = 500;
                Evolve(sim, other, "marshal");
            }
            Kill(sim, doomed);
            Assert.False(sim.Units.Exists(doomed));
            return CombatSystem.MaxMorale - Morale(sim, near);
        }
        int plain = Shaken(false), steadied = Shaken(true);
        Assert.Equal(CombatSystem.Shock / 3, plain);
        Assert.Equal(CombatSystem.Shock / 3 / 2, steadied);

        // Out of the Marshal's reach (8 blocks), a death shakes as before.
        var far = Flat();
        int m = Veteran(far, 10, 10);
        Evolve(far, m, "marshal");
        int d = far.SpawnUnit(Settlers, "spearman", 60, 60), n = far.SpawnUnit(Settlers, "spearman", 62, 60);
        Kill(far, d);
        Assert.Equal(CombatSystem.MaxMorale - CombatSystem.Shock / 2, Morale(far, n));
        Assert.Equal(CombatSystem.MaxMorale, Morale(far, m));
    }

    [Fact]
    public void SoldiersNearTheMarshalGetTheirMoraleBackThreeTimesAsFast()
    {
        var sim = Flat();
        int m = Veteran(sim, 30, 30);
        Evolve(sim, m, "marshal");
        int near = sim.SpawnUnit(Settlers, "spearman", 33, 30);
        int far = sim.SpawnUnit(Settlers, "spearman", 70, 70);
        int enemyNear = sim.SpawnUnit(Orcs, "spearman", 30, 36); // the Marshal steadies its own side only
        sim.Units.Hp[Index(sim, enemyNear)] = 1;
        foreach (int id in new[] { near, far, enemyNear }) sim.Units.Morale[Index(sim, id)] = 40;
        sim.Units.Morale[Index(sim, m)] = 40;
        // Stop the two sides fighting: they hold their ground out of each other's reach.
        sim.Schedule(sim.Tick, new StanceCommand(Settlers, new[] { m, near }, UnitStance.Hold));
        sim.Schedule(sim.Tick, new StanceCommand(Orcs, new[] { enemyNear }, UnitStance.Hold));
        sim.Run(10 * Tps);
        Assert.InRange(Morale(sim, near), 40 + 3 * 9, 40 + 3 * 10);
        Assert.InRange(Morale(sim, m), 40 + 3 * 9, 40 + 3 * 10); // it steadies itself too
        Assert.InRange(Morale(sim, far), 40 + 9, 40 + 10);
        Assert.InRange(Morale(sim, enemyNear), 40 + 9, 40 + 10);
        // Never past the cap.
        sim.Run(20 * Tps);
        Assert.Equal(CombatSystem.MaxMorale, Morale(sim, near));
    }

    [Fact]
    public void TheMarshalNeverBreaks()
    {
        var sim = Flat();
        int m = Veteran(sim, 30, 30);
        Evolve(sim, m, "marshal");
        int spear = sim.SpawnUnit(Settlers, "spearman", 60, 60);
        sim.Units.Morale[Index(sim, m)] = 0;
        sim.Units.Morale[Index(sim, spear)] = 0;
        sim.Step();
        Assert.Equal(0, sim.Units.Rout[Index(sim, m)]);
        Assert.True(sim.Units.Rout[Index(sim, spear)] > 0);
    }

    [Fact]
    public void HeroDataIsCheckedAndHashed()
    {
        var files = Files();
        Edit(files, "units.json", "\"hero\": true, \"rally\": 8, ", "\"hero\": true, ");
        var e = Assert.Throws<ContentException>(() => ContentLoader.Load(files));
        Assert.Contains("units.json: unit 'marshal': has \"steady\" or \"inspire\" but no \"rally\"", e.Errors);
        Assert.Single(e.Errors);

        foreach (var (old, changed) in new[] { ("\"rally\": 8", "\"rally\": 9"), ("\"steady\": 50", "\"steady\": 40"),
                                                ("\"inspire\": 3", "\"inspire\": 2"), ("\"hero\": true", "\"hero\": false") })
        {
            files = Files();
            Edit(files, "units.json", old, changed);
            Assert.NotEqual(ContentDb.Default.Hash, ContentLoader.Load(files).Hash);
        }
    }

    private static Dictionary<string, string> Files()
    {
        var files = new Dictionary<string, string>();
        foreach (var f in ContentLoader.Files) files[f] = ContentLoader.EmbeddedText(f)!;
        return files;
    }

    private static void Edit(Dictionary<string, string> files, string file, string old, string replacement)
    {
        string text = files[file];
        int at = text.IndexOf(old, StringComparison.Ordinal);
        Assert.True(at >= 0, $"{file} does not contain {old}");
        files[file] = text[..at] + replacement + text[(at + old.Length)..];
    }
}
