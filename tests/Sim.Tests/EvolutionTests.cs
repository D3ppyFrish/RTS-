using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>The Settlers' experience and evolution (issue #12).</summary>
public class EvolutionTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Tps = Simulation.TicksPerSecond;
    // Players are given the factions in data order: the Settlers earn experience, the Orcs do not.
    private const int Settlers = 0, Orcs = 1;

    private static Simulation Flat(int size = 64) => TestWorlds.Sim(TestWorlds.Flat(size, 16, size, Ground));

    private static Building Place(Simulation sim, int owner, string def, int x, int z) =>
        sim.Economy.PlaceFinished(owner, sim.Content.Building(def), x, z) ?? throw new InvalidOperationException($"{def} does not fit");

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

    private static UnitDef DefOf(Simulation sim, int id) => sim.Content.Units[sim.Units.Def[Index(sim, id)]];

    private static int Xp(Simulation sim, int id) => sim.Units.Xp[Index(sim, id)];

    private static int Hp(Simulation sim, int id) => sim.Units.Exists(id) ? sim.Units.Hp[Index(sim, id)] : 0;

    private static Fix64 X(Simulation sim, int id) => sim.Units.X[Index(sim, id)];

    /// <summary>Steps until the condition holds; fails the test if it takes longer than maxTicks. Returns the ticks taken.</summary>
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

    // --- Issue #12's check -------------------------------------------------------------------------

    /// <summary>
    /// A spearman earns its first level by killing three enemy workers, then evolves into a knight: it hits
    /// harder and has more hit points (the bonus), and it walks slower (the drawback).
    /// </summary>
    [Fact]
    public void ASpearmanLevelsUpAndBecomesAKnightThatHitsHarderButWalksSlower()
    {
        var sim = Flat();
        int spear = sim.SpawnUnit(Settlers, "spearman", 20, 30);
        // Far enough apart that one's death does not shake the others' morale.
        var prey = new[] { sim.SpawnUnit(Orcs, "worker", 21, 30), sim.SpawnUnit(Orcs, "worker", 21, 42), sim.SpawnUnit(Orcs, "worker", 21, 54) };

        sim.Schedule(0, new EvolveCommand(Settlers, new[] { spear }, "knight"));
        sim.Step();
        Assert.Equal("spearman", DefOf(sim, spear).Id); // no level yet

        foreach (int w in prey)
        {
            sim.Schedule(sim.Tick, new AttackCommand(Settlers, new[] { spear }, w));
            RunUntil(sim, () => !sim.Units.Exists(w), 20 * Tps, "the worker falls");
        }
        // Each worker: 40 hit points of damage and 10 for the kill.
        Assert.Equal(150, Xp(sim, spear));
        Assert.Equal(1, sim.Evolution.Level(Index(sim, spear)));
        Assert.True(sim.Evolution.CanEvolve(Index(sim, spear)));
        Assert.Equal(80, Hp(sim, spear)); // workers do not fight back

        sim.Schedule(sim.Tick, new EvolveCommand(Settlers, new[] { spear }, "knight"));
        sim.Step();
        var knight = DefOf(sim, spear);
        Assert.Equal("knight", knight.Id);
        Assert.Equal(130, Hp(sim, spear)); // unhurt, so still at full health
        Assert.Equal(150, Xp(sim, spear)); // experience is kept
        Assert.False(sim.Evolution.CanEvolve(Index(sim, spear))); // there is no second tier yet

        // The bonus: a knight is heavy infantry, strong against spearmen, and hits for 12 to the spearman's 8.
        int enemy = sim.SpawnUnit(Orcs, "spearman", 20, 46);
        int recruit = sim.SpawnUnit(Settlers, "spearman", 22, 46);
        int Damage(int a, int t) => sim.Combat.Damage(Index(sim, a), DefOf(sim, a), Index(sim, t));
        Assert.Equal(24, Damage(spear, enemy));
        Assert.Equal(8, Damage(recruit, enemy));
        sim.Units.Remove(enemy);
        sim.Units.Remove(recruit);

        // The drawback: it walks at 3 blocks a second to a spearman's 4.
        int z = sim.Units.Z[Index(sim, spear)].FloorToInt();
        int walker = sim.SpawnUnit(Settlers, "spearman", X(sim, spear).FloorToInt(), z + 4);
        var from = (Knight: X(sim, spear), Spearman: X(sim, walker));
        sim.Schedule(sim.Tick, new MoveCommand(Settlers, new[] { spear }, 60, z));
        sim.Schedule(sim.Tick, new MoveCommand(Settlers, new[] { walker }, 60, z + 4));
        sim.Run(5 * Tps);
        int knightWalked = (X(sim, spear) - from.Knight).RoundToInt(), spearmanWalked = (X(sim, walker) - from.Spearman).RoundToInt();
        Assert.InRange(knightWalked, 14, 16);
        Assert.InRange(spearmanWalked, 19, 21);
    }

    // --- The forms -----------------------------------------------------------------------------------

    [Fact]
    public void EveryFormGivesSomething()
    {
        var db = ContentDb.Default;
        Assert.Equal(new[] { "knight", "pathfinder", "battle_priest", "siege_engineer" }, db.Unit("spearman").Forms.Select(f => f.Id));
        // The healer (issue #42) has no forms yet: it is a later addition, and its forms are a follow-up.
        // Machines (the catapult) are built, not raised, so they have no forms either.
        // A faction's own units (the Deepfolk's hive) evolve only if that faction earns experience, which they do not.
        foreach (var b in db.BaseUnits.Where(b => b.Id != "healer" && !b.Machine && b.Faction == null)) Assert.True(b.Forms.Count >= 2, $"{b.Id} has {b.Forms.Count} forms");
        Assert.Empty(db.Unit("healer").Forms);
        Assert.Empty(db.Unit("catapult").Forms);
        Assert.Empty(db.Unit("battering_ram").Forms);
        Assert.Empty(db.Unit("skitterling").Forms);
        foreach (var f in db.Units.Where(u => u.IsForm))
        {
            var from = f.EvolvesFrom!;
            // Heroes (HeroTests) are the last forms, a level further on; the rest are first forms.
            Assert.Equal(f.Hero ? 2 : 1, f.Tier);
            Assert.Same(db.Faction("settlers"), f.Faction);
            Assert.Null(f.TrainedAt);
            bool gain = f.MaxHp > from.MaxHp || f.Damage > from.Damage || f.Range > from.Range || f.Sight > from.Sight
                        || f.Speed > from.Speed || f.Splash > from.Splash || f.Heal > 0 || f.WorkPercent > from.WorkPercent
                        || f.Role != from.Role;
            Assert.True(gain, $"{f.Id} gains nothing");
        }
        Assert.Equal(new[] { 150, 500 }, db.Faction("settlers").Experience!.Levels);
        Assert.Null(db.Faction("orcs").Experience);
    }

    [Fact]
    public void APathfinderTakesHalfAsMuchAgainFromMagic()
    {
        var sim = Flat();
        int pathfinder = sim.SpawnUnit(Settlers, "pathfinder", 20, 30);
        int spear = sim.SpawnUnit(Settlers, "spearman", 20, 34);
        int mage = sim.SpawnUnit(Orcs, "mage", 26, 32);
        int archer = sim.SpawnUnit(Orcs, "archer", 26, 36);
        int Damage(int a, int t) => sim.Combat.Damage(Index(sim, a), DefOf(sim, a), Index(sim, t));
        Assert.Equal(15, Damage(mage, pathfinder));
        Assert.Equal(10, Damage(mage, spear));
        Assert.Equal(12, Damage(archer, pathfinder)); // arrows hurt both the same: twice, against line infantry
        Assert.Equal(12, Damage(archer, spear));

        sim.Schedule(0, new AttackCommand(Orcs, new[] { mage }, pathfinder));
        RunUntil(sim, () => Hp(sim, pathfinder) < 80, 5 * Tps, "the blast lands");
        var shot = sim.Combat.Shots.Last(s => s.Attacker == mage);
        Assert.Equal(80 - shot.Damage, Hp(sim, pathfinder));
        // Where it landed, it hurt more than the same blast on a spearman.
        var part = shot.Part!.Value;
        Assert.True(shot.Damage > sim.Combat.HitDamage(Index(sim, mage), DefOf(sim, mage), Index(sim, spear), part));
    }

    [Fact]
    public void ABattlePriestHealsTheMostHurtAllyNearItAndEatsTwiceAsMuch()
    {
        var sim = Flat();
        var store = Place(sim, Settlers, "storehouse", 40, 40);
        store.Stock[(int)Good.Food] = 100;
        int priest = sim.SpawnUnit(Settlers, "battle_priest", 20, 30);
        int scratched = sim.SpawnUnit(Settlers, "spearman", 21, 30);
        int wounded = sim.SpawnUnit(Settlers, "spearman", 22, 31);
        int far = sim.SpawnUnit(Settlers, "spearman", 30, 30); // ten blocks off: out of reach
        sim.Units.Hp[Index(sim, priest)] = 30;
        sim.Units.Hp[Index(sim, scratched)] = 60;
        sim.Units.Hp[Index(sim, wounded)] = 40;
        sim.Units.Hp[Index(sim, far)] = 10;

        sim.Run(2 * Tps); // one heal
        Assert.Equal(44, Hp(sim, wounded));
        Assert.Equal(60, Hp(sim, scratched));
        Assert.Equal(10, Hp(sim, far));
        Assert.Equal(30, Hp(sim, priest)); // not itself
        sim.Run(2 * Tps);
        Assert.Equal(48, Hp(sim, wounded)); // still the most hurt, as a share of its hit points
    }

    [Fact]
    public void AForemanBuildsHalfAsFastAgain()
    {
        var sim = Flat();
        var store = Place(sim, Settlers, "storehouse", 24, 16);
        store.Stock[(int)Good.Wood] = 100;
        int worker = sim.SpawnUnit(Settlers, "worker", 10, 10);
        int foreman = sim.SpawnUnit(Settlers, "foreman", 10, 30);
        sim.Schedule(0, new BuildCommand(Settlers, new[] { worker }, "house", 12, 8));
        sim.Schedule(0, new BuildCommand(Settlers, new[] { foreman }, "house", 12, 28));
        sim.Step();
        var houses = sim.Buildings.All.Where(b => b.Def.Id == "house").OrderBy(b => b.Z).ToArray();
        Assert.Equal(2, houses.Length);
        int foremanTicks = RunUntil(sim, () => houses[1].Active, 40 * Tps, "the foreman's house is up");
        int workerTicks = foremanTicks + RunUntil(sim, () => houses[0].Active, 40 * Tps, "the worker's house is up");
        // 15 seconds of work for a worker, two thirds of that for a foreman; the walk there is the same.
        Assert.InRange(workerTicks - foremanTicks, 95, 105);
    }

    [Fact]
    public void AWorkerWhoTakesUpArmsStopsWorkingAndUnitsKeepTheirShareOfHitPoints()
    {
        var w = TestWorlds.Flat(64, 16, 64, Ground);
        w.Set(24, Ground + 1, 30, BlockType.Stone);
        var sim = TestWorlds.Sim(w);
        Place(sim, Settlers, "storehouse", 40, 40);
        int worker = sim.SpawnUnit(Settlers, "worker", 20, 30);
        int spear = sim.SpawnUnit(Settlers, "spearman", 20, 34);
        sim.Units.Xp[Index(sim, worker)] = sim.Units.Xp[Index(sim, spear)] = 150;
        sim.Units.Hp[Index(sim, worker)] = 20; // half of 40
        sim.Units.Hp[Index(sim, spear)] = 40;  // half of 80
        sim.Schedule(0, new AutomateCommand(Settlers, new[] { worker }, true));
        sim.Schedule(0, new GatherCommand(Settlers, new[] { worker }, 24, Ground + 1, 30));
        RunUntil(sim, () => sim.Units.CarryAmount[Index(sim, worker)] > 0, 20 * Tps, "the worker has dug the stone");

        sim.Schedule(sim.Tick, new EvolveCommand(Settlers, new[] { worker }, "militia"));
        sim.Schedule(sim.Tick, new EvolveCommand(Settlers, new[] { spear }, "knight"));
        sim.Step();
        int i = Index(sim, worker);
        Assert.Equal(UnitRole.LineInfantry, DefOf(sim, worker).Role);
        Assert.Equal((UnitJob.None, 0, false), (sim.Units.Job[i], sim.Units.CarryAmount[i], sim.Units.Auto[i]));
        Assert.Equal(35, Hp(sim, worker)); // half of the militia's 70
        Assert.Equal(65, Hp(sim, spear));  // half of the knight's 130
    }

    // --- Experience ----------------------------------------------------------------------------------

    [Fact]
    public void SettlersEarnExperienceForDamageAndKillsAndOrcsDoNot()
    {
        var sim = Flat();
        int spear = sim.SpawnUnit(Settlers, "spearman", 20, 30);
        int victim = sim.SpawnUnit(Orcs, "worker", 21, 30);
        int orc = sim.SpawnUnit(Orcs, "spearman", 40, 30);
        int settler = sim.SpawnUnit(Settlers, "worker", 41, 30);
        sim.Schedule(0, new AttackCommand(Settlers, new[] { spear }, victim));
        sim.Schedule(0, new AttackCommand(Orcs, new[] { orc }, settler));
        RunUntil(sim, () => !sim.Units.Exists(victim) && !sim.Units.Exists(settler), 10 * Tps, "both workers fall");
        // Spearmen hit workers for 16: 16, 16, then the last 8 of its 40 hit points; and 10 for the kill.
        Assert.Equal(40 + 10, Xp(sim, spear));
        Assert.Equal(0, Xp(sim, orc));

        // Damage to buildings counts too: a spearman deals a quarter of its 8 to them.
        var house = Place(sim, Orcs, "house", 30, 10);
        sim.Schedule(sim.Tick, new AttackBuildingCommand(Settlers, new[] { spear }, house.Id));
        int hits = 0;
        for (int t = 0; t < 30 * Tps && hits < 5; t++)
        {
            sim.Step();
            hits += sim.Combat.Shots.Count(s => s.Tick == sim.Tick - 1 && s.Attacker == spear);
        }
        Assert.Equal(5, hits);
        Assert.Equal(50 + 5 * 2, Xp(sim, spear));
    }

    [Fact]
    public void WorkersEarnExperienceBuildingAndEveryoneForStayingAlive()
    {
        var sim = Flat();
        foreach (int p in new[] { Settlers, Orcs })
            Place(sim, p, "storehouse", 10 + 30 * p, 22).Stock[(int)Good.Wood] = 100;
        int settler = sim.SpawnUnit(Settlers, "worker", 10, 10);
        int orc = sim.SpawnUnit(Orcs, "worker", 40, 10);
        sim.Schedule(0, new BuildCommand(Settlers, new[] { settler }, "house", 12, 8));
        sim.Schedule(0, new BuildCommand(Orcs, new[] { orc }, "house", 42, 8));
        sim.Step();
        RunUntil(sim, () => sim.Buildings.All.Count(b => b.Def.Id == "house" && b.Active) == 2, 40 * Tps, "both houses are up");
        Assert.Equal(15, Xp(sim, settler)); // a house is 15 seconds of building
        Assert.Equal(0, Xp(sim, orc));

        int soldier = sim.SpawnUnit(Settlers, "spearman", 20, 30);
        sim.Run((int)(EvolutionSystem.Minute - sim.Tick % EvolutionSystem.Minute) + 1); // past the next minute
        Assert.Equal(15 + 5, Xp(sim, settler));
        Assert.Equal(5, Xp(sim, soldier));
        Assert.Equal(0, Xp(sim, orc));
    }

    // --- Commands and automation ---------------------------------------------------------------------

    [Fact]
    public void OnlyTheOwnerEvolvesTheirUnitsAndOnlyIntoTheirOwnForms()
    {
        var sim = Flat();
        int spear = sim.SpawnUnit(Settlers, "spearman", 20, 30);
        int archer = sim.SpawnUnit(Settlers, "archer", 20, 32);
        int orc = sim.SpawnUnit(Orcs, "spearman", 40, 40);
        foreach (int id in new[] { spear, archer, orc }) sim.Units.Xp[Index(sim, id)] = 500;
        sim.Schedule(0, new EvolveCommand(Orcs, new[] { spear }, "knight"));      // not their unit
        sim.Schedule(0, new EvolveCommand(Settlers, new[] { archer }, "knight")); // not an archer's form
        sim.Schedule(0, new EvolveCommand(Orcs, new[] { orc }, "knight"));        // orcs do not evolve
        sim.Schedule(0, new EvolveCommand(Settlers, new[] { spear }, "dragon"));  // no such unit
        sim.Step();
        Assert.Equal(new[] { "spearman", "archer", "spearman" }, new[] { spear, archer, orc }.Select(id => DefOf(sim, id).Id));
        Assert.Equal(0, sim.Evolution.Level(Index(sim, orc)));
        Assert.False(sim.Evolution.CanEvolve(Index(sim, orc)));

        sim.Schedule(sim.Tick, new EvolveCommand(Settlers, new[] { spear, archer }, "pathfinder"));
        sim.Step();
        Assert.Equal("pathfinder", DefOf(sim, spear).Id);
        Assert.Equal("archer", DefOf(sim, archer).Id);
    }

    [Fact]
    public void AutomationEvolvesUnitsIntoTheFormThePlayerChoseOnceTheyCan()
    {
        var sim = Flat();
        int spear = sim.SpawnUnit(Settlers, "spearman", 20, 30);
        int other = sim.SpawnUnit(Settlers, "spearman", 20, 32);
        int orc = sim.SpawnUnit(Orcs, "spearman", 40, 40);
        var given = new List<(long Tick, Command Command)>();
        sim.Automation.Record = given;
        sim.Schedule(0, new AutoEvolveCommand(Settlers, "spearman", "knight"));
        sim.Schedule(0, new AutoEvolveCommand(Settlers, "archer", "knight"));    // not an archer's form: ignored
        sim.Schedule(0, new AutoEvolveCommand(Orcs, "spearman", "knight"));      // orcs do not evolve: ignored
        sim.Run(2 * Tps);
        Assert.Equal("spearman", DefOf(sim, spear).Id); // no level yet
        var spearman = sim.Content.Unit("spearman");
        Assert.Same(sim.Content.Unit("knight"), sim.Evolution.AutoForm(Settlers, spearman));
        Assert.Null(sim.Evolution.AutoForm(Settlers, sim.Content.Unit("archer")));
        Assert.Null(sim.Evolution.AutoForm(Orcs, spearman));

        sim.Units.Xp[Index(sim, spear)] = 150;
        sim.Units.Xp[Index(sim, orc)] = 1000;
        sim.Run(Tps);
        Assert.Equal("knight", DefOf(sim, spear).Id);
        Assert.Equal("spearman", DefOf(sim, other).Id);
        Assert.Equal("spearman", DefOf(sim, orc).Id);
        var evolve = Assert.IsType<EvolveCommand>(Assert.Single(given).Command);
        Assert.Equal((Settlers, "knight"), (evolve.PlayerId, evolve.Form));
        Assert.Equal(new[] { spear }, evolve.Units);

        // Switched off, the next spearman to level up waits for the player to pick.
        sim.Schedule(sim.Tick, new AutoEvolveCommand(Settlers, "spearman", null));
        sim.Units.Xp[Index(sim, other)] = 150;
        sim.Run(2 * Tps);
        Assert.Equal("spearman", DefOf(sim, other).Id);
        Assert.Null(sim.Evolution.AutoForm(Settlers, spearman));
    }

    [Fact]
    public void EvolutionCommandsRoundTripThroughTheLog()
    {
        var log = new CommandLog { Seed = 1, SizeX = 64, SizeY = 16, SizeZ = 64, Ticks = 10 };
        log.Add(1, new EvolveCommand(0, new[] { 3, 4 }, "knight"));
        log.Add(2, new AutoEvolveCommand(0, "spearman", "knight"));
        log.Add(3, new AutoEvolveCommand(1, "archer", null));
        string text = log.ToString();
        Assert.Contains("\n1 evolve 0 knight 3,4\n", text);
        Assert.Contains("\n2 auto-evolve 0 spearman knight\n", text);
        Assert.Contains("\n3 auto-evolve 1 archer off\n", text);
        var copy = CommandLog.Parse(text);
        Assert.Equal(log.Commands, copy.Commands);
        Assert.Equal(text, copy.ToString());
    }

    [Fact]
    public void ScriptedMatchesEvolveUnitsAndReplayTheSame()
    {
        var log = ScriptedMatch.Generate(3, 256, 64, 256, ticks: 4000, startUnits: 40);
        Assert.Contains(log.Commands, c => c.Command is EvolveCommand);
        Assert.Contains(log.Commands, c => c.Command is AutoEvolveCommand);
        var hashes = new ulong[2];
        int mostForms = 0;
        for (int run = 0; run < 2; run++)
        {
            var sim = log.CreateSimulation();
            foreach (var c in log.Commands) sim.Schedule(c.Tick, c.Command);
            for (long t = 0; t < log.Ticks; t += 100)
            {
                sim.Run(100);
                mostForms = Math.Max(mostForms, Enumerable.Range(0, sim.Units.Count).Count(i => sim.Content.Units[sim.Units.Def[i]].IsForm));
            }
            hashes[run] = sim.StateHash();
        }
        Assert.Equal(hashes[0], hashes[1]);
        Assert.True(mostForms > 0, "no unit evolved");
    }

    // --- The data checks -----------------------------------------------------------------------------

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

    [Fact]
    public void EvolutionDataIsChecked()
    {
        var files = Files();
        Edit(files, "units.json", "\"id\": \"foreman\", \"name\": \"Foreman\", \"evolvesFrom\": \"worker\"", "\"id\": \"foreman\", \"name\": \"Foreman\", \"evolvesFrom\": \"peasant\"");
        Edit(files, "units.json", "\"id\": \"militia\", \"name\": \"Militia\", \"evolvesFrom\": \"worker\"", "\"id\": \"militia\", \"name\": \"Militia\", \"evolvesFrom\": \"marshal\"");
        Edit(files, "units.json", "\"arms\": 2, \"legs\": 2 } },", "\"arms\": 2, \"legs\": 2 }, \"cost\": { \"food\": 10 } },");
        Edit(files, "units.json", "\"id\": \"outrider\", \"name\": \"Outrider\", \"evolvesFrom\": \"rider\", \"faction\": \"settlers\"",
            "\"id\": \"outrider\", \"name\": \"Outrider\", \"evolvesFrom\": \"rider\", \"faction\": \"orcs\"");
        Edit(files, "units.json", "\"weakTo\": { \"line_infantry\": 50 }", "\"weakTo\": { \"spears\": 50 }");
        Edit(files, "units.json", "\"sight\": 15, \"splash\": 1.5,", "\"sight\": 15, \"splash\": 1.5, \"healTime\": 3,");
        var e = Assert.Throws<ContentException>(() => ContentLoader.Load(files));
        Assert.Contains("units.json: unit 'foreman': \"evolvesFrom\" is \"peasant\", which is not a unit", e.Errors);
        Assert.Contains("units.json: unit 'militia' is 3 evolutions from a trained unit, so it needs level 3, but settlers units have only 2", e.Errors);
        Assert.Contains("units.json: unit 'lancer': is a form units evolve into, so it is not trained: it cannot have \"trainedAt\", \"cost\" or \"trainTime\"", e.Errors);
        Assert.Contains("units.json: unit 'outrider' evolves from 'rider', but orcs units do not earn experience (no \"experience\" in factions.json)", e.Errors);
        Assert.Contains(e.Errors, m => m.StartsWith("units.json: unit 'ranger': \"weakTo\" has \"spears\", which is not a role (roles: worker, "));
        Assert.Contains("units.json: unit 'seer': has \"healTime\" or \"healRange\" but no \"heal\"", e.Errors);
        Assert.Equal(6, e.Errors.Count);

        files = Files();
        Edit(files, "units.json", "\"id\": \"knight\", \"name\": \"Knight\", \"evolvesFrom\": \"spearman\"", "\"id\": \"knight\", \"name\": \"Knight\", \"evolvesFrom\": \"pathfinder\"");
        Edit(files, "units.json", "\"id\": \"pathfinder\", \"name\": \"Pathfinder\", \"evolvesFrom\": \"spearman\"", "\"id\": \"pathfinder\", \"name\": \"Pathfinder\", \"evolvesFrom\": \"knight\"");
        Edit(files, "units.json", "\"id\": \"seer\", \"name\": \"Seer\", \"evolvesFrom\": \"mage\"", "\"id\": \"seer\", \"name\": \"Seer\", \"evolvesFrom\": \"seer\"");
        Edit(files, "factions.json", "\"levels\": [150, 500]", "\"levels\": [150, 100]");
        e = Assert.Throws<ContentException>(() => ContentLoader.Load(files));
        Assert.Contains("units.json: evolution loop, each evolves from the next: knight -> pathfinder -> knight", e.Errors);
        Assert.Contains("units.json: unit 'seer': cannot evolve from itself", e.Errors);
        Assert.Contains("factions.json: faction 'settlers', experience: \"levels\" should rise: each level needs more experience than the one before", e.Errors);
        Assert.Equal(3, e.Errors.Count);
    }

    [Fact]
    public void EvolutionDataIsPartOfTheContentHash()
    {
        var files = Files();
        Edit(files, "units.json", "\"work\": 150", "\"work\": 160");
        Assert.NotEqual(ContentDb.Default.Hash, ContentLoader.Load(files).Hash);
        files = Files();
        Edit(files, "factions.json", "\"perKill\": 10", "\"perKill\": 11");
        Assert.NotEqual(ContentDb.Default.Hash, ContentLoader.Load(files).Hash);
        Assert.Equal(ContentDb.Default.Hash, ContentLoader.Load(Files()).Hash);
    }
}
