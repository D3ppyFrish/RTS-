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

/// <summary>Uses for cleared monster dens: the Orcs' wolf lodge, and the hook for a den-only form (issue #70).</summary>
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
        var hall = Place(sim, Orcs, "town_hall", 10, 10);
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
        sim.Run(60 * Tps - 1); // the minute's upkeep is eaten at the next minute mark; stop short of any
        int upkeep = sim.Economy.Player(Orcs).Eaten[(int)Good.Food];
        Assert.InRange(hall.Stock[(int)Good.Food] - food + upkeep, 20, 20);

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
        // The Settlers' den form waits on the designer's choice: for now they have no use for a den.
        Assert.Null(sim.Content.Faction("settlers").Den);
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
    /// The hook the Settlers' den form will use: with a faction's den use naming a form (here a stand-in, as
    /// the designer has yet to pick the real one), the unit nearest the den that can take it does, and only there.
    /// </summary>
    [Fact]
    public void ADenFormIsTakenByTheNearestUnitThatCanAndOnlyAtADen()
    {
        var files = Files();
        Edit(files, "units.json", "  { \"id\": \"knight\",",
            "  { \"id\": \"den_test_form\", \"name\": \"Den form\", \"evolvesFrom\": \"spearman\", \"faction\": \"settlers\", \"fromDen\": true,\n" +
            "    \"role\": \"heavy_infantry\", \"speed\": 4, \"hp\": 200, \"damage\": 20 },\n  { \"id\": \"knight\",");
        Edit(files, "factions.json", "\"uiLook\": \"stone\",", "\"uiLook\": \"stone\", \"den\": { \"form\": \"den_test_form\" },");
        var db = ContentLoader.Load(files);
        Assert.Same(db.Unit("den_test_form"), db.Faction("settlers").Den!.Form);

        var sim = Flat(db);
        var lair = PlaceLair(sim);
        int near = sim.SpawnUnit(Settlers, "spearman", DenX + 2, DenZ);
        int nearer = sim.SpawnUnit(Settlers, "spearman", DenX + 1, DenZ);
        int worker = sim.SpawnUnit(Settlers, "worker", DenX, DenZ + 1);
        sim.Units.Xp[Index(sim, near)] = sim.Units.Xp[Index(sim, nearer)] = 100000;

        // Experience never gives it.
        Assert.False(sim.Evolution.CanEvolve(Index(sim, nearer), db.Unit("den_test_form")));
        sim.Schedule(sim.Tick, new EvolveCommand(Settlers, new[] { nearer }, "den_test_form"));
        sim.Step();
        Assert.Equal("spearman", db.Units[sim.Units.Def[Index(sim, nearer)]].Id);

        Clear(sim, lair);
        sim.Schedule(sim.Tick, new DenCommand(Settlers, new[] { worker, near, nearer }, lair.Id));
        sim.Step();
        Assert.True(lair.Used);
        Assert.Equal(0, lair.Building);
        Assert.Equal("den_test_form", db.Units[sim.Units.Def[Index(sim, nearer)]].Id);
        Assert.Equal("spearman", db.Units[sim.Units.Def[Index(sim, near)]].Id);
        Assert.Equal("worker", db.Units[sim.Units.Def[Index(sim, worker)]].Id);
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
            Place(sim, Orcs, "town_hall", 10, 10).Stock[(int)Good.Wood] = 100;
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
        Assert.Same(lodge, db.Faction("orcs").Den!.Building);
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
        var files = Files();
        Edit(files, "factions.json", "\"den\": { \"building\": \"wolf_lodge\" }", "\"den\": { \"building\": \"house\", \"form\": \"knight\" }");
        Edit(files, "factions.json", "\"uiLook\": \"stone\",", "\"uiLook\": \"stone\", \"den\": { \"form\": \"knight\" },");
        Edit(files, "buildings.json", "\"yieldTime\": 15, ", "");
        var e = Assert.Throws<ContentException>(() => ContentLoader.Load(files));
        Assert.Contains("factions.json: faction 'orcs', den: needs one of \"building\" (put up on the den) or \"form\" (a unit near the den takes it), not both", e.Errors);
        Assert.Contains("factions.json: faction 'settlers', den: \"form\" is \"knight\", which needs \"fromDen\": true in units.json", e.Errors);
        Assert.Contains("buildings.json: building 'wolf_lodge': \"yield\" and \"yieldTime\" go together: the goods it brings in, and the seconds between", e.Errors);
        Assert.Equal(3, e.Errors.Count);

        files = Files();
        Edit(files, "factions.json", "\"den\": { \"building\": \"wolf_lodge\" }", "\"den\": { \"building\": \"house\" }");
        Edit(files, "units.json", "\"id\": \"knight\", \"name\": \"Knight\", \"evolvesFrom\": \"spearman\",", "\"id\": \"knight\", \"name\": \"Knight\",");
        Edit(files, "units.json", "\"id\": \"knight\", \"name\": \"Knight\",", "\"id\": \"knight\", \"name\": \"Knight\", \"fromDen\": true,");
        e = Assert.Throws<ContentException>(() => ContentLoader.Load(files));
        Assert.Contains("factions.json: faction 'orcs', den: \"building\" is \"house\", which needs \"fromDen\": true in buildings.json", e.Errors);
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
        Assert.NotEqual(original, Hash("factions.json", ",\n    \"den\": { \"building\": \"wolf_lodge\" }", ""));
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
