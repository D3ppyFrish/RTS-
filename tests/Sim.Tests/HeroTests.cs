using System;
using System.Collections.Generic;
using Rts.Sim.Content;
using Rts.Sim.Sandbox;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Heroes: the Settlers' last forms, one of a kind, that steady the soldiers around them (the design doc's
/// "units near a hero hold longer"). The first is the Marshal, a knight's form at level 2; then the Bowmaster
/// (longbowmen), the Horselord (lancers) and the Breachmaster (demolishers).
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
        Assert.Contains("units.json: unit 'marshal': has \"steady\", \"inspire\", \"volley\", \"spur\" or \"undermine\" but no \"rally\"", e.Errors);
        Assert.Single(e.Errors);

        foreach (var (old, changed) in new[] { ("\"rally\": 8", "\"rally\": 9"), ("\"steady\": 50", "\"steady\": 40"),
                                                ("\"inspire\": 3", "\"inspire\": 2"), ("\"hero\": true", "\"hero\": false"),
                                                ("\"undermine\": 50", "\"undermine\": 40") })
        {
            files = Files();
            Edit(files, "units.json", old, changed);
            Assert.NotEqual(ContentDb.Default.Hash, ContentLoader.Load(files).Hash);
        }
    }

    /// <summary>The Bowmaster: the longbowman's form at level 2, a hero of its own beside the Marshal.</summary>
    [Fact]
    public void TheBowmasterIsALongbowmansFormAtTheSecondLevelAndIsAHero()
    {
        var db = ContentDb.Default;
        var bowmaster = db.Unit("bowmaster");
        var longbow = db.Unit("longbowman");
        Assert.Same(longbow, bowmaster.EvolvesFrom);
        Assert.Equal(2, bowmaster.Tier);
        Assert.True(bowmaster.Hero);
        Assert.Equal(UnitRole.Ranged, bowmaster.Role);
        Assert.Equal(Fix64.FromInt(10), bowmaster.Rally);
        Assert.Equal(25, bowmaster.Volley);
        Assert.Equal(0, bowmaster.Steady);
        Assert.Equal(1, bowmaster.Inspire);
        Assert.Contains(bowmaster, longbow.Forms);
        // Its gain is hit points, damage and reach; its drawback is upkeep.
        Assert.True(bowmaster.MaxHp > longbow.MaxHp && bowmaster.Damage > longbow.Damage && bowmaster.Range > longbow.Range);
        Assert.True(bowmaster.Upkeep > longbow.Upkeep);

        var sim = Flat();
        int a = sim.SpawnUnit(Settlers, "longbowman", 20, 30), b = sim.SpawnUnit(Settlers, "longbowman", 20, 34);
        sim.Units.Xp[Index(sim, a)] = 499;
        Assert.False(sim.Evolution.CanEvolve(Index(sim, a), bowmaster)); // level 1 only
        sim.Units.Xp[Index(sim, a)] = 500;
        sim.Units.Xp[Index(sim, b)] = 500;
        // A Marshal does not stand in the Bowmaster's way: each hero is one of a kind on its own.
        int knight = Veteran(sim, 40, 40);
        Evolve(sim, knight, "marshal");
        sim.Schedule(sim.Tick, new EvolveCommand(Settlers, new[] { a, b }, "bowmaster"));
        sim.Step();
        Assert.Equal("bowmaster", DefId(sim, a));
        Assert.Equal("longbowman", DefId(sim, b));
        Assert.True(sim.Evolution.WaitsForHero(Index(sim, b)));
        Assert.Equal(80, sim.Units.Hp[Index(sim, a)]);
    }

    /// <summary>
    /// Ranged units of its own side near the Bowmaster (itself included) hit a quarter harder; foot soldiers
    /// beside it, archers out of its reach and the enemy's archers hit as before.
    /// </summary>
    [Fact]
    public void ArchersNearTheBowmasterHitAQuarterHarder()
    {
        var sim = Flat();
        int bm = sim.SpawnUnit(Settlers, "longbowman", 30, 30);
        sim.Units.Xp[Index(sim, bm)] = 500;
        Evolve(sim, bm, "bowmaster");
        int near = sim.SpawnUnit(Settlers, "archer", 33, 30);
        int spear = sim.SpawnUnit(Settlers, "spearman", 30, 33);
        int far = sim.SpawnUnit(Settlers, "archer", 70, 70);
        int enemy = sim.SpawnUnit(Orcs, "archer", 30, 36);
        sim.Step(); // the volleys are worked out with the tick
        var combat = sim.Combat;
        int Attack(int id) => combat.AttackOf(Index(sim, id), sim.Content.Units[sim.Units.Def[Index(sim, id)]]);
        var archer = sim.Content.Unit("archer");
        Assert.Equal(archer.Damage * 125 / 100, Attack(near));
        Assert.Equal(9 * 125 / 100, Attack(bm));
        Assert.Equal(archer.Damage, Attack(far));
        Assert.Equal(sim.Content.Unit("spearman").Damage, Attack(spear));
        Assert.Equal(archer.Damage, Attack(enemy));

        // Once it falls, its archers hit as before.
        Kill(sim, bm);
        sim.Step();
        Assert.Equal(archer.Damage, Attack(near));
    }

    /// <summary>A real exchange: the same archer volley takes a dummy down faster with the Bowmaster among them.</summary>
    [Fact]
    public void TheBowmastersVolleysKillFaster()
    {
        int TicksToKill(bool withBowmaster)
        {
            var sim = Flat();
            var archers = new List<int>();
            for (int k = 0; k < 4; k++) archers.Add(sim.SpawnUnit(Settlers, "archer", 30 + k, 30));
            if (withBowmaster)
            {
                // Behind the archers, holding its ground with the target beyond its own range: it only calls.
                int lb = sim.SpawnUnit(Settlers, "longbowman", 32, 22);
                sim.Units.Xp[Index(sim, lb)] = 500;
                Evolve(sim, lb, "bowmaster");
                sim.Schedule(sim.Tick, new StanceCommand(Settlers, new[] { lb }, UnitStance.Hold));
            }
            int target = sim.SpawnUnit(Orcs, "spearman", 32, 37);
            sim.Schedule(sim.Tick, new StanceCommand(Orcs, new[] { target }, UnitStance.Hold));
            long start = sim.Tick;
            while (sim.Units.Exists(target) && sim.Tick - start < 60 * Tps) sim.Step();
            Assert.False(sim.Units.Exists(target));
            return (int)(sim.Tick - start);
        }
        Assert.True(TicksToKill(true) < TicksToKill(false));
    }

    /// <summary>The Horselord: the lancer's form at level 2, a third hero of its own.</summary>
    [Fact]
    public void TheHorselordIsALancersFormAtTheSecondLevelAndIsAHero()
    {
        var db = ContentDb.Default;
        var lord = db.Unit("horselord");
        var lancer = db.Unit("lancer");
        Assert.Same(lancer, lord.EvolvesFrom);
        Assert.Equal(2, lord.Tier);
        Assert.True(lord.Hero);
        Assert.Equal(UnitRole.Cavalry, lord.Role);
        Assert.Equal(Fix64.FromInt(12), lord.Rally);
        Assert.Equal(25, lord.Spur);
        Assert.Equal(0, lord.Volley);
        Assert.Equal(0, lord.Steady);
        Assert.Contains(lord, lancer.Forms);
        // Its gain is hit points and damage; its drawback is upkeep.
        Assert.True(lord.MaxHp > lancer.MaxHp && lord.Damage > lancer.Damage);
        Assert.True(lord.Upkeep > lancer.Upkeep);

        var sim = Flat();
        int a = sim.SpawnUnit(Settlers, "lancer", 20, 30), b = sim.SpawnUnit(Settlers, "lancer", 20, 34);
        sim.Units.Xp[Index(sim, a)] = 499;
        Assert.False(sim.Evolution.CanEvolve(Index(sim, a), lord)); // level 1 only
        sim.Units.Xp[Index(sim, a)] = 500;
        sim.Units.Xp[Index(sim, b)] = 500;
        // The other heroes do not stand in its way: each is one of a kind on its own.
        Evolve(sim, Veteran(sim, 40, 40), "marshal");
        sim.Schedule(sim.Tick, new EvolveCommand(Settlers, new[] { a, b }, "horselord"));
        sim.Step();
        Assert.Equal("horselord", DefId(sim, a));
        Assert.Equal("lancer", DefId(sim, b));
        Assert.True(sim.Evolution.WaitsForHero(Index(sim, b)));
        Assert.Equal(180, sim.Units.Hp[Index(sim, a)]);
    }

    /// <summary>
    /// Cavalry of its own side near the Horselord (itself included) ride a quarter faster; foot soldiers beside
    /// it, riders out of its reach and the enemy's riders ride as before, and the spur ends when it falls.
    /// </summary>
    [Fact]
    public void RidersNearTheHorselordAreSpurredOn()
    {
        var sim = Flat();
        int hl = sim.SpawnUnit(Settlers, "lancer", 30, 30);
        sim.Units.Xp[Index(sim, hl)] = 500;
        Evolve(sim, hl, "horselord");
        int near = sim.SpawnUnit(Settlers, "rider", 36, 30);
        int spear = sim.SpawnUnit(Settlers, "spearman", 30, 33);
        int far = sim.SpawnUnit(Settlers, "rider", 70, 70);
        int enemy = sim.SpawnUnit(Orcs, "rider", 30, 36);
        sim.Step(); // the spur is worked out with the tick
        int Spur(int id) => sim.Combat.SpurOf(Index(sim, id));
        Assert.Equal(25, Spur(near));
        Assert.Equal(25, Spur(hl));
        Assert.Equal(0, Spur(far));
        Assert.Equal(0, Spur(spear));
        Assert.Equal(0, Spur(enemy));

        Kill(sim, hl);
        sim.Step();
        Assert.Equal(0, Spur(near));
    }

    /// <summary>
    /// A real ride: riders beside the Horselord cover the same ground sooner than riders without it, on a
    /// plain move and in a formation (which keeps the slowest member's pace, spurred).
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RidersWithTheHorselordArriveSooner(bool formation)
    {
        int TicksToArrive(bool withLord)
        {
            var sim = Flat();
            var riders = new List<int>();
            for (int k = 0; k < 3; k++) riders.Add(sim.SpawnUnit(Settlers, "rider", 20, 20 + 3 * k));
            var all = new List<int>(riders);
            if (withLord)
            {
                int ln = sim.SpawnUnit(Settlers, "lancer", 18, 23);
                sim.Units.Xp[Index(sim, ln)] = 500;
                Evolve(sim, ln, "horselord");
                all.Add(ln);
            }
            sim.Schedule(sim.Tick, formation
                ? new FormationMoveCommand(Settlers, all.ToArray(), 70, 18, 70, 28)
                : new MoveCommand(Settlers, all.ToArray(), 70, 23));
            long start = sim.Tick;
            while (sim.Tick - start < 60 * Tps && !riders.TrueForAll(id => sim.Units.X[Index(sim, id)] > Fix64.FromInt(60)))
                sim.Step();
            Assert.True(riders.TrueForAll(id => sim.Units.X[Index(sim, id)] > Fix64.FromInt(60)));
            return (int)(sim.Tick - start);
        }
        int with = TicksToArrive(true), without = TicksToArrive(false);
        Assert.True(with * 100 < without * 90, $"with the Horselord {with} ticks, without {without}");
    }

    /// <summary>The Breachmaster: the demolisher's form at level 2, the sappers' hero.</summary>
    [Fact]
    public void TheBreachmasterIsADemolishersFormAtTheSecondLevelAndIsAHero()
    {
        var db = ContentDb.Default;
        var master = db.Unit("breachmaster");
        var demolisher = db.Unit("demolisher");
        Assert.Same(demolisher, master.EvolvesFrom);
        Assert.Equal(2, master.Tier);
        Assert.True(master.Hero);
        Assert.Equal(UnitRole.Sapper, master.Role);
        Assert.Equal(Fix64.FromInt(10), master.Rally);
        Assert.Equal(50, master.Undermine);
        Assert.Equal(0, master.Spur);
        Assert.Equal(0, master.Volley);
        Assert.Equal(0, master.Steady);
        Assert.Contains(master, demolisher.Forms);
        // Its gain is hit points and damage; its drawback is upkeep.
        Assert.True(master.MaxHp > demolisher.MaxHp && master.Damage > demolisher.Damage);
        Assert.True(master.Upkeep > demolisher.Upkeep);

        var sim = Flat();
        int a = sim.SpawnUnit(Settlers, "demolisher", 20, 30), b = sim.SpawnUnit(Settlers, "demolisher", 20, 34);
        sim.Units.Xp[Index(sim, a)] = 499;
        Assert.False(sim.Evolution.CanEvolve(Index(sim, a), master)); // level 1 only
        sim.Units.Xp[Index(sim, a)] = 500;
        sim.Units.Xp[Index(sim, b)] = 500;
        // The other heroes do not stand in its way: each is one of a kind on its own.
        Evolve(sim, Veteran(sim, 40, 40), "marshal");
        sim.Schedule(sim.Tick, new EvolveCommand(Settlers, new[] { a, b }, "breachmaster"));
        sim.Step();
        Assert.Equal("breachmaster", DefId(sim, a));
        Assert.Equal("demolisher", DefId(sim, b));
        Assert.True(sim.Evolution.WaitsForHero(Index(sim, b)));
        Assert.Equal(120, sim.Units.Hp[Index(sim, a)]);
    }

    /// <summary>A demolisher with the experience, made the Breachmaster.</summary>
    private static int Breachmaster(Simulation sim, int x, int z)
    {
        int id = sim.SpawnUnit(Settlers, "demolisher", x, z);
        sim.Units.Xp[Index(sim, id)] = 500;
        Evolve(sim, id, "breachmaster");
        Assert.Equal("breachmaster", DefId(sim, id));
        return id;
    }

    /// <summary>
    /// Sappers of its own side near the Breachmaster (itself included) work half as fast again; foot soldiers
    /// beside it, sappers out of its reach and the enemy's sappers work as before, and it ends when it falls.
    /// </summary>
    [Fact]
    public void SappersNearTheBreachmasterWorkTheBreach()
    {
        var sim = Flat();
        int bm = Breachmaster(sim, 30, 30);
        int near = sim.SpawnUnit(Settlers, "sapper", 35, 30);
        int tunneller = sim.SpawnUnit(Settlers, "tunneller", 30, 35);
        int spear = sim.SpawnUnit(Settlers, "spearman", 30, 33);
        int far = sim.SpawnUnit(Settlers, "sapper", 70, 70);
        int enemy = sim.SpawnUnit(Orcs, "sapper", 27, 30);
        foreach (int id in new[] { bm, near, tunneller, spear, far })
            sim.Schedule(sim.Tick, new StanceCommand(Settlers, new[] { id }, UnitStance.Hold));
        sim.Schedule(sim.Tick, new StanceCommand(Orcs, new[] { enemy }, UnitStance.Hold));
        sim.Step(); // worked out with the tick
        int Undermine(int id) => sim.Combat.UndermineOf(Index(sim, id));
        Assert.Equal(50, Undermine(near));
        Assert.Equal(50, Undermine(tunneller));
        Assert.Equal(50, Undermine(bm));
        Assert.Equal(0, Undermine(far));
        Assert.Equal(0, Undermine(spear));
        Assert.Equal(0, Undermine(enemy));

        Kill(sim, bm);
        sim.Step();
        Assert.Equal(0, Undermine(near));
    }

    /// <summary>
    /// A real siege: the same sappers bring a house down sooner with the Breachmaster standing by (holding its
    /// ground out of reach, so it only works the breach).
    /// </summary>
    [Fact]
    public void TheBreachmastersSappersBringAHouseDownSooner()
    {
        int TicksToRaze(bool withMaster)
        {
            var sim = Flat();
            var house = sim.Economy.PlaceFinished(Orcs, sim.Content.Building("house"), 30, 30)!;
            Assert.NotNull(house);
            var sappers = new[] { sim.SpawnUnit(Settlers, "sapper", 29, 31), sim.SpawnUnit(Settlers, "sapper", 29, 32) };
            if (withMaster)
            {
                int bm = Breachmaster(sim, 24, 31);
                sim.Schedule(sim.Tick, new StanceCommand(Settlers, new[] { bm }, UnitStance.Hold));
            }
            sim.Schedule(sim.Tick, new AttackBuildingCommand(Settlers, sappers, house.Id));
            long start = sim.Tick;
            while (sim.Buildings.Get(house.Id) != null && sim.Tick - start < 300 * Tps) sim.Step();
            Assert.Null(sim.Buildings.Get(house.Id));
            return (int)(sim.Tick - start);
        }
        int with = TicksToRaze(true), without = TicksToRaze(false);
        Assert.True(with * 100 < without * 80, $"with the Breachmaster {with} ticks, without {without}");
    }

    /// <summary>The Breachmaster digs a tunnel through a ridge sooner than a plain demolisher (it works the breach itself).</summary>
    [Fact]
    public void TheBreachmasterTunnelsFasterThanADemolisher()
    {
        int TicksToTunnel(bool master)
        {
            var w = TestWorlds.Flat(64, 16, 64, Ground);
            int y = Ground + 1;
            TestWorlds.Box(w, 30, y, 0, 37, y + 5, 63, BlockType.Stone); // a ridge, wall to wall
            var sim = TestWorlds.Sim(w);
            sim.TestMode = true;
            int id = master ? Breachmaster(sim, 26, 30) : sim.SpawnUnit(Settlers, "demolisher", 26, 30);
            sim.Schedule(sim.Tick, new TunnelCommand(Settlers, new[] { id }, 42, y, 30));
            sim.Step();
            long start = sim.Tick;
            while (sim.Units.Job[Index(sim, id)] == UnitJob.Tunnel && sim.Tick - start < 120 * Tps) sim.Step();
            Assert.Equal(42, sim.Units.X[Index(sim, id)].FloorToInt());
            return (int)(sim.Tick - start);
        }
        int with = TicksToTunnel(true), without = TicksToTunnel(false);
        Assert.True(with * 100 < without * 80, $"the Breachmaster {with} ticks, a demolisher {without}");
    }

    /// <summary>Sappers digging out an area beside the Breachmaster finish sooner.</summary>
    [Fact]
    public void SappersDigAnAreaFasterBesideTheBreachmaster()
    {
        int TicksToDig(bool withMaster)
        {
            var sim = Flat();
            var sappers = new[] { sim.SpawnUnit(Settlers, "sapper", 22, 22), sim.SpawnUnit(Settlers, "sapper", 22, 23) };
            if (withMaster)
            {
                int bm = Breachmaster(sim, 21, 25);
                sim.Schedule(sim.Tick, new StanceCommand(Settlers, new[] { bm }, UnitStance.Hold));
            }
            sim.Schedule(sim.Tick, new DigAreaCommand(Settlers, sappers, 23, 20, 26, 26, Ground));
            sim.Step();
            long start = sim.Tick;
            bool Done() => Array.TrueForAll(sappers, id => sim.Units.Job[Index(sim, id)] == UnitJob.None);
            while (!Done() && sim.Tick - start < 300 * Tps) sim.Step();
            Assert.True(Done());
            Assert.Equal(BlockType.Air, sim.World.Get(25, Ground, 23));
            return (int)(sim.Tick - start);
        }
        int with = TicksToDig(true), without = TicksToDig(false);
        Assert.True(with * 100 < without * 90, $"with the Breachmaster {with} ticks, without {without}");
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
