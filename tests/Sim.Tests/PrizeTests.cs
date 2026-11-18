using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Each monster's own prize for the factions that beat it (a lair's "prizes"): sometimes the factions get
/// different things (a wolf den), sometimes the same (a boar wallow), and some prizes are relics a unit takes up
/// (the stone giant's heart).
/// </summary>
public class PrizeTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Tps = Simulation.TicksPerSecond;
    private const int Settlers = 0, Orcs = 1;
    private const int DenX = 60, DenZ = 48;

    private static Simulation Flat() => TestWorlds.Sim(TestWorlds.Flat(96, 16, 96, Ground));

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);
    private static string DefOf(Simulation sim, int id) => sim.Content.Units[sim.Units.Def[Index(sim, id)]].Id;

    /// <summary>Places a lair of the monster at the den spot and takes its pack out, so it is cleared on the next tick.</summary>
    private static Lair ClearedLair(Simulation sim, string monster, int x = DenX, int z = DenZ)
    {
        var lair = sim.Monsters.Place(sim.Content.Unit(monster), x, z) ?? throw new InvalidOperationException("no ground for the den");
        foreach (int id in lair.Pack.ToArray()) sim.Units.RemoveAt(Index(sim, id));
        sim.Step();
        Assert.True(lair.Cleared);
        return lair;
    }

    /// <summary>Every monster with a lair of its own, but the dusk tree's warden, has a prize, chosen per monster.</summary>
    [Fact]
    public void EachMonsterHasItsOwnPrizes()
    {
        var db = ContentDb.Default;
        var settlers = db.Faction("settlers");
        var orcs = db.Faction("orcs");
        foreach (var m in db.WildMonsters) Assert.True(m.Lair!.Den, m.Id);
        Assert.Empty(db.DuskWarden!.Lair!.Prizes);

        // A wolf den: different prizes.
        var wolf = db.Unit("wolf").Lair!;
        Assert.Same(db.Building("wolf_lodge"), wolf.PrizeFor(orcs)!.Building);
        Assert.Same(db.Unit("wolfheart"), wolf.PrizeFor(settlers)!.Form);

        // A boar wallow: the same prize for both.
        var boar = db.Unit("boar").Lair!;
        Assert.Same(db.Building("boar_pen"), boar.PrizeFor(orcs)!.Building);
        Assert.Same(db.Building("boar_pen"), boar.PrizeFor(settlers)!.Building);
        Assert.Null(db.Building("boar_pen").Faction);

        // A stone giant: the Settlers take up its heart; the Orcs' prize is the giant itself, beaten and kept.
        var giant = db.Unit("stone_giant").Lair!;
        Assert.Same(db.Relic("giant_heart"), giant.PrizeFor(settlers)!.Relic);
        Assert.Null(giant.PrizeFor(orcs));
        Assert.True(orcs.TamesMonsters);
    }

    /// <summary>
    /// The giant's heart goes to the selected fighting unit nearest the den without a relic: never a worker, and
    /// a unit bears one relic at most. Its bearer has more hit points (at once, too) and more armour everywhere,
    /// keeps it when it evolves, and it is gone with the bearer.
    /// </summary>
    [Fact]
    public void SettlersTakeUpTheGiantsHeart()
    {
        var sim = Flat();
        var db = sim.Content;
        var heart = db.Relic("giant_heart");
        var lair = ClearedLair(sim, "stone_giant");
        int worker = sim.SpawnUnit(Settlers, "worker", DenX, DenZ + 1);
        int nearer = sim.SpawnUnit(Settlers, "spearman", DenX + 1, DenZ);
        int near = sim.SpawnUnit(Settlers, "spearman", DenX + 3, DenZ);
        int i = Index(sim, nearer);
        int hp = sim.Units.Hp[i], maxHp = sim.MaxHpOf(i), torso = sim.Combat.Armour(i, BodyPart.Torso);

        // A worker alone cannot take it up; the Orcs have no use for a giant's den.
        Assert.False(sim.Monsters.CanUseDen(Settlers, lair, new[] { Index(sim, worker) }));
        int orc = sim.SpawnUnit(Orcs, "spearman", DenX - 1, DenZ);
        Assert.Null(sim.Monsters.DenUse(Orcs, lair));
        Assert.False(sim.Monsters.CanUseDen(Orcs, lair, new[] { Index(sim, orc) }));
        sim.Schedule(sim.Tick, new DenCommand(Orcs, new[] { orc }, lair.Id));
        sim.Step();
        Assert.False(lair.Used);

        sim.Schedule(sim.Tick, new DenCommand(Settlers, new[] { worker, near, nearer }, lair.Id));
        sim.Step();
        Assert.True(lair.Used);
        Assert.Equal(Settlers, lair.UsedBy);
        i = Index(sim, nearer);
        Assert.Same(heart, sim.RelicOf(i));
        Assert.Null(sim.RelicOf(Index(sim, near)));
        Assert.Null(sim.RelicOf(Index(sim, worker)));
        Assert.Equal(maxHp + heart.Hp, sim.MaxHpOf(i));
        Assert.True(sim.Units.Hp[i] >= hp + heart.Hp - 2, "the bearer gets the hit points at once");
        Assert.Equal(torso + heart.Armour, sim.Combat.Armour(i, BodyPart.Torso));

        // A second giant's heart passes over the bearer to the next fighting unit.
        var second = ClearedLair(sim, "stone_giant", DenX, DenZ - 20);
        sim.Units.Z[i] = sim.Units.Z[Index(sim, near)] = Fix64.FromInt(DenZ - 20);
        sim.Schedule(sim.Tick, new DenCommand(Settlers, new[] { nearer, near }, second.Id));
        sim.Step();
        Assert.True(second.Used);
        Assert.Same(heart, sim.RelicOf(Index(sim, near)));
        Assert.Same(heart, sim.RelicOf(Index(sim, nearer)));

        // It keeps the heart when it evolves.
        sim.Units.Xp[Index(sim, nearer)] = 100000;
        sim.Schedule(sim.Tick, new EvolveCommand(Settlers, new[] { nearer }, "knight"));
        sim.Step();
        Assert.Equal("knight", DefOf(sim, nearer));
        Assert.Same(heart, sim.RelicOf(Index(sim, nearer)));
        Assert.Equal(sim.Economy.MaxHp(Settlers, db.Unit("knight")) + heart.Hp, sim.MaxHpOf(Index(sim, nearer)));
    }

    /// <summary>A boar wallow's prize is the same for both factions: a boar pen their workers build, which brings in food.</summary>
    [Fact]
    public void BothFactionsCanPenBoarsAtAClearedWallow()
    {
        foreach (int player in new[] { Settlers, Orcs })
        {
            var sim = Flat();
            var hall = sim.Economy.PlaceFinished(player, sim.Content.Building("town_hall"), 10, 10)!;
            hall.Stock[(int)Good.Wood] = 100;
            var lair = ClearedLair(sim, "boar");
            int worker = sim.SpawnUnit(player, "worker", DenX + 1, DenZ);
            Assert.True(sim.Monsters.CanUseDen(player, lair, new[] { Index(sim, worker) }));
            sim.Schedule(sim.Tick, new DenCommand(player, new[] { worker }, lair.Id));
            sim.Run(40 * Tps);
            Assert.True(lair.Used);
            var pen = sim.Buildings.Get(lair.Building)!;
            Assert.Equal("boar_pen", pen.Def.Id);
            Assert.Equal(player, pen.Owner);
            Assert.True(pen.Active, $"player {player}'s worker builds the pen");
        }
    }

    /// <summary>
    /// The swamp's bog horrors heal in the muck and track blood; both factions take up the same prize, a bog
    /// lantern, which sharpens its bearer's hits (an Orc spearman's here).
    /// </summary>
    [Fact]
    public void BogHorrorsGiveBothFactionsTheirLantern()
    {
        var sim = Flat();
        var db = sim.Content;
        var horror = db.Unit("bog_horror");
        Assert.Equal(World.Biome.Swamp, horror.Lair!.Biome);
        Assert.True(horror.Regen > 0 && horror.TracksBlood);
        var lantern = db.Relic("bog_lantern");
        Assert.Same(lantern, horror.Lair.PrizeFor(db.Faction("orcs"))!.Relic);
        Assert.Same(lantern, horror.Lair.PrizeFor(db.Faction("settlers"))!.Relic);

        var lair = ClearedLair(sim, "bog_horror");
        int orc = sim.SpawnUnit(Orcs, "spearman", DenX + 1, DenZ);
        var spearman = db.Unit("spearman");
        Assert.Equal(spearman.Damage, sim.Combat.AttackOf(Index(sim, orc), spearman));
        sim.Schedule(sim.Tick, new DenCommand(Orcs, new[] { orc }, lair.Id));
        sim.Step();
        Assert.Same(lantern, sim.RelicOf(Index(sim, orc)));
        Assert.Equal(spearman.Damage + lantern.Damage, sim.Combat.AttackOf(Index(sim, orc), spearman));
    }

    /// <summary>
    /// Giant crabs on the beaches are shelled hard all over. Their prizes differ: the Settlers' fighting unit takes
    /// up a crab shell shield (armour everywhere), the Orcs put up crab pots that bring in food.
    /// </summary>
    [Fact]
    public void GiantCrabsGiveShellsToSettlersAndPotsToOrcs()
    {
        var db = ContentDb.Default;
        var crab = db.Unit("giant_crab");
        Assert.Equal(World.Biome.Beach, crab.Lair!.Biome);
        Assert.True(crab.ArmourOn(BodyPart.Torso) > db.Unit("stone_giant").ArmourOn(BodyPart.Torso));
        var shell = db.Relic("crab_shell");
        Assert.Same(shell, crab.Lair.PrizeFor(db.Faction("settlers"))!.Relic);
        var pots = crab.Lair.PrizeFor(db.Faction("orcs"))!.Building!;
        Assert.Equal("crab_pots", pots.Id);
        Assert.Same(db.Faction("orcs"), pots.Faction);
        Assert.NotEmpty(pots.Yield);

        var sim = Flat();
        var lair = ClearedLair(sim, "giant_crab");
        int archer = sim.SpawnUnit(Settlers, "archer", DenX + 1, DenZ);
        int armour = sim.Combat.Armour(Index(sim, archer), BodyPart.Head);
        sim.Schedule(sim.Tick, new DenCommand(Settlers, new[] { archer }, lair.Id));
        sim.Step();
        Assert.Same(shell, sim.RelicOf(Index(sim, archer)));
        Assert.Equal(armour + shell.Armour, sim.Combat.Armour(Index(sim, archer), BodyPart.Head));

        sim = Flat();
        var hall = sim.Economy.PlaceFinished(Orcs, sim.Content.Building("town_hall"), 10, 10)!;
        hall.Stock[(int)Good.Wood] = 100;
        lair = ClearedLair(sim, "giant_crab");
        int worker = sim.SpawnUnit(Orcs, "worker", DenX + 1, DenZ);
        sim.Schedule(sim.Tick, new DenCommand(Orcs, new[] { worker }, lair.Id));
        sim.Run(40 * Tps);
        Assert.Equal("crab_pots", sim.Buildings.Get(lair.Building)!.Def.Id);
        Assert.True(sim.Buildings.Get(lair.Building)!.Active);
    }

    [Fact]
    public void PrizeAndRelicDataIsChecked()
    {
        var files = Files();
        Edit(files, "units.json", "\"prizes\": { \"settlers\": { \"relic\": \"giant_heart\" } }", "\"prizes\": { \"settlers\": { \"relic\": \"crown\" } }");
        Edit(files, "units.json", "\"lair\": { \"pack\": 1, \"guard\": 10, \"leash\": 16, \"duskTree\": true }",
            "\"lair\": { \"pack\": 1, \"guard\": 10, \"leash\": 16, \"duskTree\": true, \"prizes\": {} }");
        Edit(files, "relics.json", "\"hp\": 60, \"armor\": 2 }", "\"hp\": 60, \"armor\": 2, \"speed\": 1 },\n  { \"id\": \"giant_heart\", \"name\": \"Again\" }");
        var e = Assert.Throws<ContentException>(() => ContentLoader.Load(files));
        Assert.Contains("units.json: unit 'stone_giant', lair, prizes, settlers: \"relic\" is \"crown\", which is not in relics.json", e.Errors);
        Assert.Contains(e.Errors, m => m.Contains("unit 'dusk_warden', lair") && m.Contains("no \"biome\", \"atStarts\" or \"prizes\""));
        Assert.Contains(e.Errors, m => m.StartsWith("relics.json: relic 'giant_heart'") && m.Contains("speed"));
        Assert.Contains(e.Errors, m => m.StartsWith("relics.json: relic 'giant_heart'") && m.Contains("another relic already has this id"));
    }

    [Fact]
    public void TheContentHashCoversPrizesAndRelics()
    {
        ulong Hash(string file, string old, string replacement)
        {
            var files = Files();
            Edit(files, file, old, replacement);
            return ContentLoader.Load(files).Hash;
        }
        ulong original = ContentDb.Default.Hash;
        Assert.NotEqual(original, Hash("units.json", "\"settlers\": { \"building\": \"boar_pen\" }", "\"settlers\": { \"relic\": \"giant_heart\" }"));
        Assert.NotEqual(original, Hash("relics.json", "\"hp\": 60", "\"hp\": 61"));
        Assert.NotEqual(original, Hash("relics.json", "\"armor\": 2", "\"armor\": 3"));
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
