using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Uses for cleared monster dens: the Orcs' wolf lodge and the Settlers' Wolfheart (issue #3).</summary>
public class DenTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Tps = Simulation.TicksPerSecond;
    private const int Settlers = 0, Orcs = 1;
    private const int DenX = 60, DenZ = 48;

    private static Simulation Flat(ContentDb? content = null) =>
        new(new RegionMap(1, TestWorlds.Flat(96, 16, 96, Ground)), content);

    private static Lair PlaceLair(Simulation sim) =>
        sim.Monsters.Place(sim.Content.Unit("wolf"), DenX, DenZ) ?? throw new InvalidOperationException("no ground for the den");

    private static Building Place(Simulation sim, int owner, string def, int x, int z) =>
        sim.Economy.PlaceFinished(owner, sim.Content.Building(def), x, z) ?? throw new InvalidOperationException($"{def} does not fit");

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

    private static int[] Squad(Simulation sim, int owner, string unit, int x, int z, int count) =>
        Enumerable.Range(0, count).Select(k => sim.SpawnUnit(owner, unit, x + k % 4, z + k / 4 * 2)).ToArray();

    private static void RunUntil(Simulation sim, Func<bool> done, int maxTicks, string what)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return;
            sim.Step();
        }
        Assert.Fail($"Not done after {maxTicks} ticks: {what}");
    }

    /// <summary>Takes the lair's pack out of the game, so the lair is cleared on the next tick.</summary>
    private static void Clear(Simulation sim, Lair lair)
    {
        foreach (int id in lair.Pack.ToArray()) sim.Units.RemoveAt(Index(sim, id));
        sim.Step();
        Assert.True(lair.Cleared);
    }

    // --- Issue #70's checks ------------------------------------------------------------------------

    /// <summary>
    /// Orcs clear a wolf lair, then turn its den into a wolf lodge: the den is gone, the workers build the
    /// lodge, which houses five more people and brings in food from its wolves hunting.
    /// </summary>
    [Fact]
    public void OrcsClearALairAndTurnTheDenIntoAWolfLodge()
    {
        var sim = Flat();
        var hall = Place(sim, Orcs, "town_hall", 30, 40);
        hall.Stock[(int)Good.Wood] = 100;
        var lair = PlaceLair(sim);
        var wolves = lair.Pack.ToArray();

        var spearmen = Squad(sim, Orcs, "spearman", DenX - 24, DenZ - 2, 8);
        var workers = Squad(sim, Orcs, "worker", DenX - 4, DenZ - 16, 2);
        sim.Schedule(0, new AttackMoveCommand(Orcs, spearmen, DenX, DenZ));
        RunUntil(sim, () => lair.Cleared, 60 * Tps, "the orcs clear the lair");
        sim.Schedule(sim.Tick, new TameCommand(Orcs, wolves.Where(sim.Units.Exists).ToArray(), TameChoice.Recruit));
        sim.Step();

        // The workers are too far from the den to use it on their own.
        Assert.False(sim.Monsters.CanUseDen(Orcs, lair, workers.Select(id => Index(sim, id))));
        sim.Schedule(sim.Tick, new DenCommand(Orcs, workers, lair.Id));
        sim.Step();
        Assert.False(lair.Used);

        // A spearman at the den lets the player use it; the workers are sent to build the lodge.
        int housing = sim.Economy.Housing(Orcs);
        var users = spearmen.Concat(workers).ToArray();
        Assert.True(sim.Monsters.CanUseDen(Orcs, lair, users.Select(id => Index(sim, id))));
        sim.Schedule(sim.Tick, new DenCommand(Orcs, users, lair.Id));
        sim.Step();
        Assert.True(lair.Used);
        Assert.Equal(Orcs, lair.UsedBy);
        Assert.False(sim.Monsters.CanUseDen(Orcs, lair, users.Select(id => Index(sim, id))), "a den is used once");
        var lodge = sim.Buildings.Get(lair.Building)!;
        Assert.Equal("wolf_lodge", lodge.Def.Id);
        Assert.Equal(BuildingState.Site, lodge.State);
        Assert.True(lodge.ColumnDistance(DenX, DenZ) <= EconomySystem.DenSearch, "the lodge stands on the den");
        Assert.Equal(70, hall.Stock[(int)Good.Wood]);
        Assert.All(workers, id => Assert.Equal(UnitJob.Build, sim.Units.Job[Index(sim, id)]));

        RunUntil(sim, () => lodge.Active, 60 * Tps, "the workers build the lodge");
        sim.Step();
        Assert.Equal(housing + 5, sim.Economy.Housing(Orcs));

        // Its wolves bring in five food every fifteen seconds.
        int food = hall.Stock[(int)Good.Food];
        sim.Run(60 * Tps - 1);
        Assert.Equal(20, hall.Stock[(int)Good.Food] - food);

        // Using it again does nothing.
        sim.Schedule(sim.Tick, new DenCommand(Orcs, users, lair.Id));
        sim.Step();
        Assert.Single(sim.Buildings.All, b => b.Def.Id == "wolf_lodge");
    }

    /// <summary>A den can be used only once it is cleared, and only by a player whose faction has a use for it.</summary>
    [Fact]
    public void ADenIsUsedOnlyOnceItIsClearedAndOnlyByAFactionWithAUse()
    {
        var sim = Flat();
        var orcHall = Place(sim, Orcs, "town_hall", 10, 10);
        orcHall.Stock[(int)Good.Wood] = 100;
        var lair = PlaceLair(sim);
        int orc = sim.SpawnUnit(Orcs, "worker", DenX + 1, DenZ);
        int settler = sim.SpawnUnit(Settlers, "worker", DenX - 1, DenZ);

        sim.Schedule(sim.Tick, new DenCommand(Orcs, new[] { orc }, lair.Id));
        sim.Step();
        Assert.False(lair.Used, "not while the pack is there");

        Clear(sim, lair);
        // A Settlers worker cannot take the Settlers' den form (a spearman's), so it cannot use the den.
        Assert.False(sim.Monsters.CanUseDen(Settlers, lair, new[] { Index(sim, settler) }));
        sim.Schedule(sim.Tick, new DenCommand(Settlers, new[] { settler }, lair.Id));
        // Someone else's unit does not count.
        sim.Schedule(sim.Tick, new DenCommand(Orcs, new[] { settler }, lair.Id));
        sim.Step();
        Assert.False(lair.Used);

        // Without the wood for the lodge, nothing happens.
        orcHall.Stock[(int)Good.Wood] = 10;
        sim.Schedule(sim.Tick, new DenCommand(Orcs, new[] { orc }, lair.Id));
        sim.Step();
        Assert.False(lair.Used);
        Assert.Equal(10, orcHall.Stock[(int)Good.Wood]);

        orcHall.Stock[(int)Good.Wood] = 100;
        sim.Schedule(sim.Tick, new DenCommand(Orcs, new[] { orc }, lair.Id));
        sim.Step();
        Assert.True(lair.Used);
    }

    /// <summary>The wolf lodge is never in the build menu: a build order for it does nothing.</summary>
    [Fact]
    public void TheWolfLodgeIsBuiltOnlyOnADen()
    {
        var sim = Flat();
        var hall = Place(sim, Orcs, "town_hall", 10, 10);
        hall.Stock[(int)Good.Wood] = 100;
        int worker = sim.SpawnUnit(Orcs, "worker", 20, 20);
        var lodge = sim.Content.Building("wolf_lodge");
        Assert.False(sim.Economy.Unlocked(Orcs, lodge));
        sim.Schedule(sim.Tick, new BuildCommand(Orcs, new[] { worker }, "wolf_lodge", 30, 30));
        sim.Step();
        Assert.DoesNotContain(sim.Buildings.All, b => b.Def == lodge);
        Assert.Equal(100, hall.Stock[(int)Good.Wood]);
    }

    /// <summary>
    /// Settlers clear a wolf lair, then use its den on one of their spearmen: that one becomes a Wolfheart, a
    /// form it can get no other way, and the den is gone. They still get the lair's reward.
    /// </summary>
    [Fact]
    public void SettlersClearALairAndUseTheDenToMakeAWolfheart()
    {
        var sim = Flat();
        var hall = Place(sim, Settlers, "town_hall", 10, 10);
        var lair = PlaceLair(sim);
        var spearmen = Squad(sim, Settlers, "spearman", DenX - 24, DenZ - 2, 8);
        int worker = sim.SpawnUnit(Settlers, "worker", DenX - 4, DenZ - 16);
        sim.Schedule(0, new AttackMoveCommand(Settlers, spearmen, DenX, DenZ));
        int food = hall.Stock[(int)Good.Food];
        RunUntil(sim, () => lair.Cleared, 60 * Tps, "the settlers clear the lair");
        sim.Step();
        Assert.True(hall.Stock[(int)Good.Food] > food, "the lair's reward still comes");

        var alive = spearmen.Where(sim.Units.Exists).ToArray();
        Assert.NotEmpty(alive);
        var users = alive.Append(worker).ToArray();
        Assert.True(sim.Monsters.CanUseDen(Settlers, lair, users.Select(id => Index(sim, id))));
        int nearest = sim.Monsters.DenUser(lair, users.Select(id => Index(sim, id)), sim.Content.Unit("wolf").Lair!.PrizeFor(sim.Content.Faction("settlers"))!);
        int chosen = sim.Units.Id[nearest];
        int hpBefore = sim.Units.Hp[nearest];
        sim.Schedule(sim.Tick, new DenCommand(Settlers, users, lair.Id));
        sim.Step();

        Assert.True(lair.Used);
        Assert.Equal(Settlers, lair.UsedBy);
        Assert.Equal(0, lair.Building);
        var db = sim.Content;
        Assert.Equal("wolfheart", db.Units[sim.Units.Def[Index(sim, chosen)]].Id);
        Assert.True(sim.Units.Hp[Index(sim, chosen)] >= hpBefore, "it keeps its share of hit points, or more");
        Assert.Single(users, id => db.Units[sim.Units.Def[Index(sim, id)]].Id == "wolfheart");
        Assert.Equal("worker", db.Units[sim.Units.Def[Index(sim, worker)]].Id);

        // A den is used once.
        Assert.False(sim.Monsters.CanUseDen(Settlers, lair, users.Select(id => Index(sim, id))));
        sim.Schedule(sim.Tick, new DenCommand(Settlers, users, lair.Id));
        sim.Step();
        Assert.Single(users, id => db.Units[sim.Units.Def[Index(sim, id)]].Id == "wolfheart");
    }

    /// <summary>
    /// The den form goes to the selected unit nearest the den that can take it, only there and only once the
    /// lair is cleared; experience never gives it.
    /// </summary>
    [Fact]
    public void ADenFormIsTakenByTheNearestUnitThatCanAndOnlyAtADen()
    {
        var db = ContentDb.Default;
        Assert.Same(db.Unit("wolfheart"), db.Unit("wolf").Lair!.PrizeFor(db.Faction("settlers"))!.Form);

        var sim = Flat();
        var lair = PlaceLair(sim);
        foreach (int id in lair.Pack.ToArray()) sim.Units.RemoveAt(Index(sim, id)); // cleared on the next tick
        int near = sim.SpawnUnit(Settlers, "spearman", DenX + 2, DenZ);
        int nearer = sim.SpawnUnit(Settlers, "spearman", DenX + 1, DenZ);
        int worker = sim.SpawnUnit(Settlers, "worker", DenX, DenZ + 1);
        int far = sim.SpawnUnit(Settlers, "spearman", DenX - 20, DenZ);
        sim.Units.Xp[Index(sim, near)] = sim.Units.Xp[Index(sim, nearer)] = 100000;

        // Experience never gives it, and it is not among the forms a spearman picks from.
        Assert.DoesNotContain(db.Unit("wolfheart"), db.Unit("spearman").Forms);
        Assert.False(sim.Evolution.CanEvolve(Index(sim, nearer), db.Unit("wolfheart")));
        sim.Schedule(sim.Tick, new EvolveCommand(Settlers, new[] { nearer }, "wolfheart"));
        sim.Schedule(sim.Tick, new AutoEvolveCommand(Settlers, "spearman", "wolfheart"));
        sim.Step();
        Assert.True(lair.Cleared);
        Assert.Equal("spearman", db.Units[sim.Units.Def[Index(sim, nearer)]].Id);
        Assert.Null(sim.Evolution.AutoForm(Settlers, db.Unit("spearman")));

        // Too far from the den, or no spearman among them: nothing happens.
        sim.Schedule(sim.Tick, new DenCommand(Settlers, new[] { far }, lair.Id));
        sim.Schedule(sim.Tick, new DenCommand(Settlers, new[] { worker }, lair.Id));
        sim.Step();
        Assert.False(lair.Used);

        sim.Schedule(sim.Tick, new DenCommand(Settlers, new[] { worker, near, nearer, far }, lair.Id));
        sim.Step();
        Assert.True(lair.Used);
        Assert.Equal(0, lair.Building);
        Assert.Equal("wolfheart", db.Units[sim.Units.Def[Index(sim, nearer)]].Id);
        Assert.Equal("spearman", db.Units[sim.Units.Def[Index(sim, near)]].Id);
        Assert.Equal("spearman", db.Units[sim.Units.Def[Index(sim, far)]].Id);
        Assert.Equal("worker", db.Units[sim.Units.Def[Index(sim, worker)]].Id);
    }

    /// <summary>The Wolfheart is data: what it gives over a spearman.</summary>
    [Fact]
    public void TheWolfheartIsData()
    {
        var db = ContentDb.Default;
        var wolfheart = db.Unit("wolfheart");
        var spearman = db.Unit("spearman");
        Assert.True(wolfheart.FromDen);
        Assert.Same(spearman, wolfheart.EvolvesFrom);
        Assert.Same(db.Faction("settlers"), wolfheart.Faction);
        Assert.True(wolfheart.MaxHp > spearman.MaxHp && wolfheart.Damage > spearman.Damage && wolfheart.Speed > spearman.Speed && wolfheart.Sight > spearman.Sight);
        Assert.Null(wolfheart.TrainedAt);
    }

    [Fact]
    public void DenCommandsRoundTripThroughTheLogAndReplayTheSame()
    {
        var log = new CommandLog { Seed = 1, SizeX = 64, SizeY = 16, SizeZ = 64, Ticks = 10, StartLairs = true };
        log.Add(3, new DenCommand(Orcs, new[] { 5, 6 }, 2));
        string text = log.ToString();
        Assert.Contains("\n3 den 1 2 5,6\n", text);
        var copy = CommandLog.Parse(text);
        Assert.Equal(log.Commands, copy.Commands);
        Assert.Equal(text, copy.ToString());

        // The same den use twice gives the same state.
        var hashes = new ulong[2];
        for (int run = 0; run < 2; run++)
        {
            var sim = Flat();
            Place(sim, Orcs, "town_hall", 30, 40).Stock[(int)Good.Wood] = 100;
            var lair = PlaceLair(sim);
            int worker = sim.SpawnUnit(Orcs, "worker", DenX + 1, DenZ);
            Clear(sim, lair);
            sim.Schedule(sim.Tick, new DenCommand(Orcs, new[] { worker }, lair.Id));
            sim.Run(40 * Tps);
            Assert.True(sim.Buildings.Get(lair.Building)!.Active);
            hashes[run] = sim.StateHash();
        }
        Assert.Equal(hashes[0], hashes[1]);
    }

    // --- The data ------------------------------------------------------------------------------------

    [Fact]
    public void TheWolfLodgeIsData()
    {
        var db = ContentDb.Default;
        var lodge = db.Building("wolf_lodge");
        Assert.Same(lodge, db.Unit("wolf").Lair!.PrizeFor(db.Faction("orcs"))!.Building);
        Assert.True(lodge.FromDen);
        Assert.Same(db.Faction("orcs"), lodge.Faction);
        Assert.Equal(BuildingKind.House, lodge.Kind);
        Assert.Equal(5, lodge.Housing);
        Assert.Equal(new[] { new GoodAmount(Good.Food, 5) }, lodge.Yield);
        Assert.Equal(15 * Tps, lodge.YieldTicks);
    }

    [Fact]
    public void DenDataIsChecked()
    {
        const string Wolf = "\"prizes\": { \"orcs\": { \"building\": \"wolf_lodge\" }, \"settlers\": { \"form\": \"wolfheart\" } }";
        var files = Files();
        Edit(files, "units.json", Wolf, "\"prizes\": { \"orcs\": { \"building\": \"house\", \"form\": \"knight\" }, \"settlers\": { \"form\": \"knight\" } }");
        Edit(files, "buildings.json", "\"yieldTime\": 15, ", "");
        var e = Assert.Throws<ContentException>(() => ContentLoader.Load(files));
        Assert.Contains("units.json: unit 'wolf', lair, prizes, orcs: needs one of \"building\" (put up on the den), \"form\" (a unit near the den takes it) or \"relic\" (a unit near the den takes it up)", e.Errors);
        Assert.Contains("units.json: unit 'wolf', lair, prizes, settlers: \"form\" is \"knight\", which needs \"fromDen\": true in units.json", e.Errors);
        Assert.Contains("buildings.json: building 'wolf_lodge': \"yield\" and \"yieldTime\" go together: the goods it brings in, and the seconds between", e.Errors);
        Assert.Equal(3, e.Errors.Count);

        files = Files();
        Edit(files, "units.json", Wolf, "\"prizes\": { \"orcs\": { \"building\": \"house\" }, \"settlers\": { \"building\": \"wolf_lodge\" }, \"elves\": { \"relic\": \"crown\" } }");
        Edit(files, "units.json", "\"id\": \"knight\", \"name\": \"Knight\", \"evolvesFrom\": \"spearman\",", "\"id\": \"knight\", \"name\": \"Knight\",");
        Edit(files, "units.json", "\"id\": \"knight\", \"name\": \"Knight\",", "\"id\": \"knight\", \"name\": \"Knight\", \"fromDen\": true,");
        e = Assert.Throws<ContentException>(() => ContentLoader.Load(files));
        Assert.Contains("units.json: unit 'wolf', lair, prizes, orcs: \"building\" is \"house\", which needs \"fromDen\": true in buildings.json", e.Errors);
        Assert.Contains("units.json: unit 'wolf', lair, prizes, settlers: \"building\" is \"wolf_lodge\", which only orcs can build", e.Errors);
        Assert.Contains("units.json: unit 'wolf', lair, prizes, elves: \"elves\" is not a faction", e.Errors);
        Assert.Contains("units.json: unit 'knight': is taken at a den (\"fromDen\"), so it is a form: it needs \"evolvesFrom\"", e.Errors);
    }

    [Fact]
    public void TheContentHashCoversDens()
    {
        ulong Hash(string file, string old, string replacement)
        {
            var files = Files();
            Edit(files, file, old, replacement);
            return ContentLoader.Load(files).Hash;
        }
        ulong original = ContentDb.Default.Hash;
        Assert.NotEqual(original, Hash("units.json", "\"orcs\": { \"building\": \"wolf_lodge\" }, ", ""));
        Assert.NotEqual(original, Hash("units.json", ", \"settlers\": { \"form\": \"wolfheart\" }", ""));
        Assert.NotEqual(original, Hash("buildings.json", "\"yield\": { \"food\": 5 }", "\"yield\": { \"food\": 6 }"));
        Assert.NotEqual(original, Hash("buildings.json", "\"yieldTime\": 15", "\"yieldTime\": 16"));
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
