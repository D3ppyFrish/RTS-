using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Loading and checking the data files (issue #9).</summary>
public class ContentTests
{
    /// <summary>The game's own data files by name, to change for a test.</summary>
    private static Dictionary<string, string> Files()
    {
        var files = new Dictionary<string, string>();
        foreach (var f in ContentLoader.Files) files[f] = ContentLoader.EmbeddedText(f) ?? throw new InvalidOperationException($"{f} is not built in");
        return files;
    }

    /// <summary>Replaces the first occurrence of <paramref name="old"/> in one file.</summary>
    private static void Edit(Dictionary<string, string> files, string file, string old, string replacement)
    {
        string text = files[file];
        int at = text.IndexOf(old, StringComparison.Ordinal);
        Assert.True(at >= 0, $"{file} does not contain {old}");
        files[file] = text[..at] + replacement + text[(at + old.Length)..];
    }

    private static ContentException Fails(Dictionary<string, string> files) =>
        Assert.Throws<ContentException>(() => ContentLoader.Load(files));

    /// <summary>Issue #9's check, part one: all of the game's data loads and passes its checks.</summary>
    [Fact]
    public void TheGamesDataLoads()
    {
        var db = ContentLoader.LoadEmbedded();
        Assert.Equal(new[] { "settlers", "orcs" }, db.Factions.Select(f => f.Id));
        Assert.Equal(new[] { "town_hall", "storehouse", "house", "farm", "barracks", "smelter", "barrier", "wooden_wall", "stone_wall", "stone_facing", "barrier_gate", "wooden_gate", "stone_gate", "stone_gate_facing", "watchtower", "stone_tower", "torch", "campfire", "wolf_lodge", "goblin_totem", "ogre_idol", "bridge" }, db.Buildings.Select(b => b.Id));
        Assert.Equal(new[] { "worker", "spearman", "archer", "rider", "sapper", "mage", "healer" }, db.BaseUnits.Select(u => u.Id));
        Assert.Equal(new[] { "worker", "spearman", "archer", "rider", "sapper", "mage" }, db.StartUnits.Select(u => u.Id)); // healers are only trained
        Assert.Equal(new[] { "foreman", "militia", "knight", "pathfinder", "battle_priest", "siege_engineer", "longbowman",
            "ranger", "lancer", "outrider", "tunneller", "demolisher", "battle_mage", "seer", "wolfheart" }, db.Units.Where(u => u.IsForm).Select(u => u.Id));
        Assert.Equal(15, db.Techs.Count);

        // A branching tree: shared roots, techs that lead to more than one, long chains, a few per faction.
        Assert.True(db.Techs.Count(t => t.Requires.Count == 0 && t.Faction == null) >= 3);
        Assert.True(db.Techs.Count(t => db.LeadsTo(t).Count >= 2) >= 2);
        Assert.Equal(3, db.Techs.Max(t => t.Depth));
        foreach (var f in db.Factions) Assert.InRange(db.Techs.Count(t => t.Faction == f), 2, 4);

        // What the loader works out.
        Assert.Equal(new[] { "worker", "caravan" }, db.Building("town_hall").Trains);
        Assert.Equal(new[] { "spearman", "archer", "rider", "sapper", "mage", "healer" }, db.Building("barracks").Trains);
        Assert.True(db.RoadsNeedTech);
        Assert.Equal(2, db.Tech("smelting").Depth);
        Assert.Equal(new[] { "Farm" }, db.UnlockedBy(db.Tech("farming")));
        Assert.Equal(new[] { "crop_rotation", "horse_breeding" }, db.LeadsTo(db.Tech("farming")).Select(t => t.Id));

        // Values come through as written.
        var hall = db.Building("town_hall");
        Assert.Equal((BuildingKind.TownHall, 7, 7, 4, 25, true), (hall.Kind, hall.SizeX, hall.SizeZ, hall.WallHeight, hall.Housing, hall.Storage));
        Assert.Equal((BlockType.Planks, BlockType.StoneBrick, BlockType.RoofTile), (hall.Wall, hall.Footing, hall.Roof));
        Assert.Equal(new[] { new GoodAmount(Good.Wood, 200), new GoodAmount(Good.Stone, 100) }, hall.Cost);
        Assert.Equal(120 * Simulation.TicksPerSecond, hall.BuildTicks);
        var rider = db.Unit("rider");
        Assert.Equal((UnitRole.Cavalry, 120, "rider"), (rider.Role, rider.MaxHp, rider.Model));
        Assert.Equal((12, 20, Fix64.FromRatio(3, 2), Fix64.FromInt(10)), (rider.Damage, rider.AttackTicks, rider.Range, rider.Sight));
        var archer = db.Unit("archer");
        Assert.Equal((24, Fix64.FromInt(9), true), (archer.AttackTicks, archer.Range, archer.IsRanged));
        Assert.Equal(Fix64.FromRatio(3, 2), db.Unit("mage").Splash);
        Assert.False(db.Unit("spearman").IsRanged);
        Assert.Equal(Fix64.FromInt(8), rider.Speed);
        Assert.Same(db.Building("barracks"), rider.TrainedAt);
        Assert.Equal("horse_breeding", Assert.Single(rider.Requires).Id);
        var drill = db.Tech("settler_drill");
        Assert.Same(db.Faction("settlers"), drill.Faction);
        var effect = Assert.Single(drill.Effects);
        Assert.Equal((TechEffectType.UnitHp, 20, UnitRole.LineInfantry), (effect.Type, effect.Amount, effect.Role!.Value));
        Assert.Equal(ContentDb.Default.Hash, db.Hash);
    }

    /// <summary>Issue #10: the role table comes through, read from both sides (strongAgainst and weakAgainst).</summary>
    [Fact]
    public void TheRoleTableLoads()
    {
        var c = ContentLoader.LoadEmbedded().Combat;
        Assert.True(c.StrongAgainst(UnitRole.LineInfantry, UnitRole.Cavalry));
        Assert.False(c.StrongAgainst(UnitRole.Cavalry, UnitRole.LineInfantry));
        Assert.True(c.StrongAgainst(UnitRole.Ranged, UnitRole.LineInfantry));
        Assert.True(c.StrongAgainst(UnitRole.Cavalry, UnitRole.Ranged));
        Assert.True(c.StrongAgainst(UnitRole.Cavalry, UnitRole.Caster));   // from the caster's weakAgainst
        Assert.True(c.StrongAgainst(UnitRole.LineInfantry, UnitRole.Sapper)); // sappers are weak against every combat role
        Assert.False(c.StrongAgainst(UnitRole.Worker, UnitRole.Sapper));
        foreach (var r in Enum.GetValues<UnitRole>())
        {
            // Every role that fights is strong against workers and caravans; those two are strong against nothing.
            bool fights = r is not (UnitRole.Worker or UnitRole.Trader);
            Assert.Equal(fights, c.StrongAgainst(r, UnitRole.Worker));
            Assert.Equal(fights, c.StrongAgainst(r, UnitRole.Trader));
            Assert.False(c.StrongAgainst(UnitRole.Trader, r));
        }
        Assert.True(c.StrongAgainstStructures(UnitRole.Sapper));
        Assert.True(c.StrongAgainstStructures(UnitRole.Siege));
        Assert.False(c.StrongAgainstStructures(UnitRole.LineInfantry));
        Assert.Equal(Fix64.FromInt(2), c.Multiplier(UnitRole.Ranged, UnitRole.LineInfantry));
        Assert.Equal(Fix64.One, c.Multiplier(UnitRole.Ranged, UnitRole.Ranged));
        Assert.Equal(Fix64.FromRatio(1, 4), c.StructureMultiplier(UnitRole.Cavalry));
        Assert.Equal(Fix64.One, c.StructureMultiplier(UnitRole.Sapper));
        Assert.Equal("Tunnel, collapse walls", c.Job(UnitRole.Sapper));
    }

    [Fact]
    public void TheRoleTableIsChecked()
    {
        var files = Files();
        Edit(files, "roles.json", "\"strong\": 2,", "\"strong\": 20,");
        Edit(files, "roles.json", "\"strongAgainst\": [\"cavalry\"], \"weakAgainst\": [\"ranged\"]",
            "\"strongAgainst\": [\"cavalry\", \"horses\"], \"weakAgainst\": [\"ranged\", \"cavalry\"]");
        Edit(files, "roles.json", "\"role\": \"siege\"", "\"role\": \"caster\"");
        Edit(files, "roles.json", "\"weakAgainst\": [\"line_infantry\"] }", "\"weakAgainst\": [\"line_infantry\", \"line_infantry\"], \"strenght\": 1 }");
        var e = Fails(files);
        Assert.Contains("roles.json: the table: \"strong\" is 20; it should be from 1 to 10", e.Errors);
        Assert.Contains(e.Errors, m => m.StartsWith("roles.json: role 'line_infantry': \"strongAgainst\" names \"horses\", which is not a role (roles: worker, "));
        Assert.Contains("roles.json: role 'line_infantry': is both strong and weak against cavalry", e.Errors);
        Assert.Contains("roles.json: role 'cavalry': \"weakAgainst\" names \"line_infantry\" twice", e.Errors);
        Assert.Contains(e.Errors, m => m.StartsWith("roles.json: role 'cavalry': unknown field \"strenght\""));
        Assert.Contains("roles.json: role 'caster': is listed twice", e.Errors);
        Assert.Contains("roles.json: role \"siege\" is missing; every role needs a row, even with empty lists", e.Errors);
        Assert.Equal(7, e.Errors.Count);

        files = Files();
        files["roles.json"] = "[]";
        Assert.Contains(Fails(files).Errors, m => m.StartsWith("roles.json: should be an object"));
    }

    [Fact]
    public void CombatStatsAreChecked()
    {
        var files = Files();
        Edit(files, "units.json", "\"range\": 9", "\"range\": 40");
        Edit(files, "units.json", "\"attackTime\": 2", "\"attackTime\": 0");
        Edit(files, "units.json", "\"damage\": 12", "\"damage\": -1");
        Edit(files, "units.json", "\"splash\": 1.5", "\"splash\": 9");
        var e = Fails(files);
        Assert.Contains(e.Errors, m => m.StartsWith("units.json: unit 'archer': \"range\" is 40; it should be from 0.5 to 32"));
        Assert.Contains(e.Errors, m => m.StartsWith("units.json: unit 'mage': \"attackTime\" is 0; it should be from 0.05 to 60"));
        Assert.Contains(e.Errors, m => m.StartsWith("units.json: unit 'rider': \"damage\" is -1; it should be from 0 to 100000"));
        Assert.Contains(e.Errors, m => m.StartsWith("units.json: unit 'mage': \"splash\" is 9; it should be from 0 to 8"));
        Assert.Equal(4, e.Errors.Count);

        // Left out, a unit hits like a worker does and spots enemies eight blocks off.
        files = Files();
        Edit(files, "units.json", "\"damage\": 5, \"attackTime\": 1.5, \"range\": 1.25, \"sight\": 7,", "");
        var sapper = ContentLoader.Load(files).Unit("sapper");
        Assert.Equal((0, Simulation.TicksPerSecond, Fix64.FromRatio(5, 4), Fix64.FromInt(8)), (sapper.Damage, sapper.AttackTicks, sapper.Range, sapper.Sight));
    }

    /// <summary>Issue #46: techs and buildings name the picture the game shows for them, and factions their UI look; only for looks.</summary>
    [Fact]
    public void IconsAreReadAndDefaultToTheId()
    {
        var db = ContentLoader.LoadEmbedded();
        Assert.Equal("pick", db.Tech("tools").Icon);
        Assert.Equal("horse", db.Tech("horse_breeding").Icon);
        Assert.Equal("town_hall", db.Building("town_hall").Icon);
        Assert.Equal(("stone", "timber"), (db.Faction("settlers").UiLook, db.Faction("orcs").UiLook));
        foreach (var t in db.Techs) Assert.False(string.IsNullOrEmpty(t.Icon), t.Id);

        var files = Files();
        Edit(files, "buildings.json", "{ \"id\": \"farm\",", "{ \"id\": \"farm\", \"icon\": \"sprout\",");
        Edit(files, "techs.json", "\"icon\": \"pick\", ", "");
        Edit(files, "factions.json", "\"uiLook\": \"stone\"", "\"uiLook\": \"crystal\"");
        var changed = ContentLoader.Load(files);
        Assert.Equal("sprout", changed.Building("farm").Icon);
        Assert.Equal("tools", changed.Tech("tools").Icon);
        Assert.Equal("crystal", changed.Faction("settlers").UiLook);
        Assert.Equal(ContentDb.Default.Hash, changed.Hash);
    }

    /// <summary>How a computer player plays a faction is data: in waves, unless the file says balanced.</summary>
    [Fact]
    public void HowTheComputerPlaysAFactionIsDataAndChangesTheHash()
    {
        var db = ContentLoader.LoadEmbedded();
        Assert.Equal((AiStyle.Balanced, AiStyle.Waves), (db.Faction("settlers").AiStyle, db.Faction("orcs").AiStyle));

        var files = Files();
        Edit(files, "factions.json", ", \"aiStyle\": \"balanced\"", "");
        var changed = ContentLoader.Load(files);
        Assert.Equal(AiStyle.Waves, changed.Faction("settlers").AiStyle); // what it is when the file does not say
        Assert.NotEqual(ContentDb.Default.Hash, changed.Hash); // a computer player's commands depend on it

        files = Files();
        Edit(files, "factions.json", "\"aiStyle\": \"waves\"", "\"aiStyle\": \"turtle\"");
        var e = Fails(files);
        Assert.Contains(e.Errors, m => m.StartsWith("factions.json: faction 'orcs': \"aiStyle\" is \"turtle\", which is not one of: waves, balanced"));
    }

    [Fact]
    public void TheFilesInTheDataFolderAreTheOnesBuiltIn()
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "RTS.sln"))) dir = Path.GetDirectoryName(dir) ?? throw new InvalidOperationException("no repository root");
        var db = ContentLoader.LoadDirectory(Path.Combine(dir, "data"));
        Assert.Equal(ContentDb.Default.Hash, db.Hash);
        Assert.Throws<ContentException>(() => ContentLoader.LoadDirectory(Path.Combine(dir, "no-such-folder")));
    }

    [Fact]
    public void DecimalsAndLayoutDoNotChangeTheHashButValuesDo()
    {
        var files = Files();
        files["units.json"] = files["units.json"].Replace("\n", "\r\n").Replace("\"speed\": 8,", "\"speed\": 8.0,");
        Assert.Equal(ContentDb.Default.Hash, ContentLoader.Load(files).Hash);
        Edit(files, "units.json", "\"speed\": 8.0,", "\"speed\": 7.5,");
        var db = ContentLoader.Load(files);
        Assert.Equal(Fix64.FromRatio(15, 2), db.Unit("rider").Speed);
        Assert.NotEqual(ContentDb.Default.Hash, db.Hash);
    }

    /// <summary>Walls too low to stop anyone, and gates with no room for their leaves, fail the load.</summary>
    [Theory]
    [InlineData("\"kind\": \"wall\", \"size\": [1, 1], \"wallHeight\": 2", "\"kind\": \"wall\", \"size\": [1, 1], \"wallHeight\": 1",
        "building 'barrier': a wall needs \"wallHeight\" of 2 or more, or units step over it")]
    [InlineData("\"kind\": \"gate\", \"size\": [1, 1], \"wallHeight\": 3", "\"kind\": \"gate\", \"size\": [1, 1], \"wallHeight\": 2",
        "building 'barrier_gate': a gate needs \"wallHeight\" of 3 or more")]
    [InlineData("\"kind\": \"bridge\", \"size\": [1, 1], \"wallHeight\": 1", "\"kind\": \"bridge\", \"size\": [1, 2], \"wallHeight\": 1",
        "building 'bridge': a bridge is laid a column at a time, one deck block each")]
    public void WallsAndGatesMustBeTallEnough(string old, string replacement, string message)
    {
        var files = Files();
        Edit(files, "buildings.json", old, replacement);
        var e = Fails(files);
        Assert.Contains(e.Errors, m => m.StartsWith("buildings.json: " + message));
    }

    /// <summary>Walls and gates come in three tiers, each the upgrade of the one before.</summary>
    [Fact]
    public void WallsAndGatesComeInTiersLinkedByTheirUpgrades()
    {
        var db = ContentDb.Default;
        foreach (var ids in new[] { new[] { "barrier", "wooden_wall", "stone_wall" }, new[] { "barrier_gate", "wooden_gate", "stone_gate" } })
        {
            var tiers = ids.Select(db.Building).ToArray();
            Assert.Equal(new[] { 0, 1, 2 }, tiers.Select(t => t.Tier));
            Assert.Equal(new[] { tiers[1], tiers[2], null }, tiers.Select(t => t.Upgrade));
            Assert.Equal(new[] { null, tiers[0], tiers[1] }, tiers.Select(t => t.UpgradeOf));
            Assert.All(tiers, t => Assert.Equal(tiers[0].Kind, t.Kind));
            // Each is at least as tall and tougher, block for block and all told, than the one before.
            Assert.True(tiers[0].Height <= tiers[1].Height && tiers[1].Height < tiers[2].Height);
            Assert.True(tiers[0].Toughness * 2 <= tiers[1].Toughness && tiers[1].Toughness * 2 <= tiers[2].Toughness);
            // Only the base is wood a worker can spare; the top tier is stone and needs masonry.
            Assert.Equal(new[] { false, true, true }, tiers.Select(t => t.Walkway));
            Assert.Empty(tiers[0].Requires);
            Assert.DoesNotContain(tiers[0].Cost, c => c.Good == Good.Stone);
            Assert.DoesNotContain(tiers[1].Cost, c => c.Good == Good.Stone);
            Assert.Contains(tiers[2].Cost, c => c.Good == Good.Stone);
            Assert.Equal(new[] { "masonry" }, tiers[2].Requires.Select(t => t.Id));
        }
        Assert.Equal(BlockType.Palisade, db.Building("barrier").Blocks[0].Block);
        Assert.Equal(BlockType.Planks, db.Building("wooden_wall").Blocks[0].Block);
        Assert.Equal(BlockType.Rampart, db.Building("stone_wall").Blocks[0].Block);
        Assert.Equal(BlockInfo.Toughness(BlockType.Planks) * db.Building("wooden_wall").Sturdy, db.Building("wooden_wall").BlockToughness(0));
        Assert.Null(db.Building("house").Upgrade);
        Assert.Equal(1, db.Building("house").Sturdy);
    }

    /// <summary>An upgrade has to take the place of what stands there, and only walls have them or walkways.</summary>
    [Theory]
    [InlineData("\"upgrade\": \"wooden_wall\"", "\"upgrade\": \"wooden_wal\"", "building 'barrier': \"upgrade\": there is no building \"wooden_wal\"")]
    [InlineData("\"upgrade\": \"wooden_wall\"", "\"upgrade\": \"barrier\"", "building 'barrier': \"upgrade\": a building is not its own upgrade")]
    [InlineData("\"upgrade\": \"wooden_wall\"", "\"upgrade\": \"wooden_gate\"", "building 'barrier': \"upgrade\": \"wooden_gate\" must be of the same kind and size")]
    [InlineData("\"upgrade\": \"stone_wall\"", "\"upgrade\": \"barrier\"", "building 'wooden_wall': \"upgrade\": \"barrier\" must be at least as tall")]
    [InlineData("\"upgrade\": \"wooden_gate\"", "\"upgrade\": \"stone_gate\"", "building 'wooden_gate': \"upgrade\": \"stone_gate\" is already the upgrade of \"barrier_gate\"")]
    [InlineData("\"wallHeight\": 4, \"wall\": \"planks\",\n    \"cost\": { \"wood\": 5 }, \"buildTime\": 4, \"sight\": 2, \"sturdy\": 4, \"walkway\": true, \"icon\": \"gate\", \"upgrade\": \"stone_gate\"",
        "\"wallHeight\": 3, \"wall\": \"planks\", \"cost\": { \"wood\": 5 }, \"buildTime\": 4, \"icon\": \"gate\", \"upgrade\": \"barrier_gate\"",
        "building 'barrier_gate' is its own upgrade, by way of 'wooden_gate'")]
    [InlineData("\"light\": 7", "\"light\": 7, \"upgrade\": \"campfire\"", "building 'torch': only a wall, gate or tower has an \"upgrade\"")]
    [InlineData("\"light\": 7", "\"light\": 7, \"walkway\": true", "building 'torch': only a wall, gate or tower has a \"walkway\"")]
    [InlineData("\"sturdy\": 2, \"icon\": \"wall\"", "\"sturdy\": 2, \"walkway\": true, \"icon\": \"wall\"",
        "building 'barrier': a \"walkway\" needs \"wallHeight\" of 3 or more, or blows from the ground reach the units on it")]
    [InlineData("\"sturdy\": 2, \"icon\": \"wall\"", "\"sturdy\": 0, \"icon\": \"wall\"", "building 'barrier': \"sturdy\" is 0; it should be from 1 to 1000")]
    public void UpgradesSturdinessAndWalkwaysAreChecked(string old, string replacement, string message)
    {
        var files = Files();
        Edit(files, "buildings.json", old, replacement);
        var e = Fails(files);
        Assert.Contains(e.Errors, m => m.StartsWith("buildings.json: " + message));
    }

    /// <summary>The tiers are part of the rules, so they are part of the content's hash.</summary>
    [Theory]
    [InlineData("\"sturdy\": 4, \"walkway\": true, \"icon\": \"wall\", \"upgrade\"", "\"sturdy\": 5, \"walkway\": true, \"icon\": \"wall\", \"upgrade\"")]
    [InlineData("\"sturdy\": 4, \"walkway\": true, \"icon\": \"wall\", \"upgrade\"", "\"sturdy\": 4, \"icon\": \"wall\", \"upgrade\"")]
    [InlineData("\"aim\": 50", "\"aim\": 40")]
    [InlineData("\"facing\": \"stone_facing\" },", "\"facing\": \"stone_spare\" },\n  { \"id\": \"stone_spare\", \"name\": \"Spare\", \"kind\": \"wall\", \"size\": [1, 1], \"wallHeight\": 6, \"wall\": \"rampart\", \"buildTime\": 4 },")]
    public void ChangingATierChangesTheContentHash(string old, string replacement)
    {
        var files = Files();
        Edit(files, "buildings.json", old, replacement);
        Assert.NotEqual(ContentDb.Default.Hash, ContentLoader.Load(files).Hash);
    }

    /// <summary>
    /// The top tier of walls and gates is faced: the facing is of the same kind and height, stands for the same
    /// tier, and is never laid as it is. Towers are their own kind, with a walkway and what it gives.
    /// </summary>
    [Fact]
    public void StoneWallsAreFacedAndTowersGiveSightAndAim()
    {
        var db = ContentDb.Default;
        foreach (var (id, facing) in new[] { ("stone_wall", "stone_facing"), ("stone_gate", "stone_gate_facing") })
        {
            var wall = db.Building(id);
            var face = db.Building(facing);
            Assert.Same(face, wall.Facing);
            Assert.Same(wall, face.FacingOf);
            Assert.Equal(wall.Kind, face.Kind);
            Assert.Equal(wall.Height, face.Height);
            Assert.Equal(wall.Tier, face.Tier);
            Assert.Equal(BlockType.Rampart, face.Blocks[^1].Block);
            Assert.True(face.Walkway);
            Assert.Null(face.Upgrade);
            Assert.Null(face.UpgradeOf);
            Assert.Null(face.Facing);
        }
        Assert.Null(db.Building("wooden_wall").Facing);
        var wood = db.Building("watchtower");
        var stone = db.Building("stone_tower");
        foreach (var tower in new[] { wood, stone })
        {
            Assert.Equal(BuildingKind.Tower, tower.Kind);
            Assert.True(tower.IsTower && tower.IsRampart && !tower.IsWall);
            Assert.True(tower.Walkway);
            Assert.Equal(tower.SizeX * tower.SizeZ * tower.Height, tower.Blocks.Count); // solid to the top
            Assert.True(tower.Aim > 0);
            Assert.True(tower.Sight > db.Building("town_hall").Sight); // it sees further than anything else that is built
        }
        // Each tower stands over the walls of its time, and the stone one is the bigger and better.
        Assert.True(wood.Height > db.Building("wooden_wall").Height && stone.Height > db.Building("stone_wall").Height);
        Assert.True(stone.SizeX > wood.SizeX && stone.Height > wood.Height);
        Assert.True(stone.Aim > wood.Aim && stone.Sight > wood.Sight);
        Assert.Empty(wood.Requires);
        Assert.Equal(new[] { "masonry" }, stone.Requires.Select(t => t.Id));
        Assert.Equal(BlockType.Trestle, wood.Blocks[0].Block);
        Assert.Equal(BlockType.Rampart, stone.Blocks[0].Block);
    }

    /// <summary>A facing and what a tower gives are checked like the rest of the data.</summary>
    [Theory]
    [InlineData("\"facing\": \"stone_facing\"", "\"facing\": \"stone_facin\"", "building 'stone_wall': \"facing\": there is no building \"stone_facin\"")]
    [InlineData("\"facing\": \"stone_facing\"", "\"facing\": \"stone_wall\"", "building 'stone_wall': \"facing\": a building is not its own facing")]
    [InlineData("\"facing\": \"stone_facing\"", "\"facing\": \"stone_gate_facing\"", "building 'stone_wall': \"facing\": \"stone_gate_facing\" must be of the same kind and height, one column across")]
    [InlineData("\"facing\": \"stone_facing\"", "\"facing\": \"wooden_wall\"", "building 'stone_wall': \"facing\": \"wooden_wall\" must be of the same kind and height, one column across")]
    [InlineData("\"light\": 7", "\"light\": 7, \"facing\": \"campfire\"", "building 'torch': only a wall or gate one column across has a \"facing\"")]
    [InlineData("\"sturdy\": 2, \"icon\": \"wall\"", "\"sturdy\": 2, \"facing\": \"wooden_wall\", \"icon\": \"wall\"", "building 'barrier': \"facing\": \"wooden_wall\" must be of the same kind and height, one column across")]
    [InlineData("\"walkway\": true, \"icon\": \"wall\" },", "\"walkway\": true, \"icon\": \"wall\", \"upgrade\": \"stone_spare\" },\n  { \"id\": \"stone_spare\", \"name\": \"Spare\", \"kind\": \"wall\", \"size\": [1, 1], \"wallHeight\": 6, \"wall\": \"rampart\", \"buildTime\": 4 },",
        "building 'stone_facing' is the facing of 'stone_wall', so it can have no \"upgrade\" or \"facing\" and be no upgrade itself")]
    [InlineData("\"light\": 7", "\"light\": 7, \"aim\": 20", "building 'torch': only a tower has \"aim\"")]
    [InlineData("\"walkway\": true, \"aim\": 50", "\"aim\": 50", "building 'watchtower': \"aim\" is for the units posted on it: it needs a \"walkway\"")]
    [InlineData("\"aim\": 50", "\"aim\": 101", "building 'watchtower': \"aim\" is 101; it should be from 0 to 100")]
    [InlineData("\"kind\": \"tower\", \"size\": [3, 3], \"wallHeight\": 7", "\"kind\": \"tower\", \"size\": [3, 3], \"wallHeight\": 2", "building 'watchtower': ")]
    public void FacingsAndTowersAreChecked(string old, string replacement, string message)
    {
        var files = Files();
        Edit(files, "buildings.json", old, replacement);
        var e = Fails(files);
        Assert.Contains(e.Errors, m => m.StartsWith("buildings.json: " + message));
    }

    /// <summary>Issue #9's check, part two: a reference to an id that does not exist fails the load.</summary>
    [Theory]
    [InlineData("units.json", "\"requires\": \"archery\"", "\"requires\": \"archry\"", "unit 'archer': \"requires\" names \"archry\", which is not a tech")]
    [InlineData("units.json", "\"trainedAt\": \"town_hall\"", "\"trainedAt\": \"townhall\"", "unit 'worker': \"trainedAt\" is \"townhall\", which is not a building")]
    [InlineData("buildings.json", "\"requires\": \"farming\"", "\"requires\": \"farmin\"", "building 'farm': \"requires\" names \"farmin\", which is not a tech")]
    [InlineData("techs.json", "\"requires\": [ \"tools\" ]", "\"requires\": [ \"tool\" ]", "tech 'masonry': \"requires\" names \"tool\", which is not a tech")]
    [InlineData("techs.json", "\"faction\": \"orcs\"", "\"faction\": \"orks\"", "tech 'orc_war_drums': \"faction\" is \"orks\", which is not a faction")]
    [InlineData("techs.json", "\"building\": \"house\"", "\"building\": \"hut\"", "tech 'masonry', effect 1: \"building\" is \"hut\", which is not a building")]
    [InlineData("techs.json", "\"role\": \"cavalry\"", "\"role\": \"horse\"", "tech 'orc_beast_pens', effect 1: \"role\" is \"horse\", which is not one of: worker, line_infantry")]
    [InlineData("techs.json", "\"type\": \"carry\"", "\"type\": \"carrying\"", "tech 'carts', effect 1: \"type\" is \"carrying\", which is not an effect")]
    [InlineData("units.json", "\"cost\": { \"food\": 30 }", "\"cost\": { \"bread\": 30 }", "unit 'worker': \"cost\" has \"bread\", which is not a good")]
    [InlineData("buildings.json", "\"kind\": \"barracks\"", "\"kind\": \"fort\"", "building 'barracks': \"kind\" is \"fort\", which is not one of: town_hall")]
    [InlineData("buildings.json", "\"roof\": \"thatch\"", "\"roof\": \"straw\"", "building 'house': \"roof\" is \"straw\", which is not a building material")]
    [InlineData("roles.json", "\"role\": \"monster\"", "\"role\": \"dragon\"", "\"role\" is \"dragon\", which is not one of: worker")]
    [InlineData("roles.json", "\"weakAgainst\": [\"cavalry\"] }", "\"weakAgainst\": [\"horses\"] }", "role 'caster': \"weakAgainst\" names \"horses\", which is not a role")]
    public void UnknownIdsFail(string file, string old, string replacement, string message)
    {
        var files = Files();
        Edit(files, file, old, replacement);
        var e = Fails(files);
        Assert.Contains(e.Errors, m => m.StartsWith(file + ": ") && m.Contains(message));
        Assert.Contains(message, e.Message);
    }

    [Fact]
    public void CombatValuesMustBeNumbers()
    {
        var files = Files();
        Edit(files, "units.json", "\"damage\": 3,", "\"damage\": \"lots\",");
        Edit(files, "roles.json", "\"structures\": 0.25,", "\"structures\": \"some\",");
        var e = Fails(files);
        Assert.Contains("units.json: unit 'worker': \"damage\" should be a whole number", e.Errors);
        Assert.Contains(e.Errors, m => m.StartsWith("roles.json: the table: \"structures\" should be a number"));
        Assert.Equal(2, e.Errors.Count);
    }

    [Fact]
    public void CountersAreDataAndChangeTheHash()
    {
        var files = Files();
        Edit(files, "roles.json", "{ \"role\": \"monster\", \"job\": \"Guard their lairs\",\n      \"strongAgainst\": []",
            "{ \"role\": \"monster\", \"job\": \"Guard their lairs\",\n      \"strongAgainst\": [\"cavalry\"]");
        var db = ContentLoader.Load(files);
        Assert.Equal(Fix64.FromInt(2), db.Combat.Multiplier(UnitRole.Monster, UnitRole.Cavalry));
        Assert.Equal(Fix64.One, db.Combat.Multiplier(UnitRole.Cavalry, UnitRole.Monster));
        Assert.NotEqual(ContentDb.Default.Hash, db.Hash);
        // The multipliers are data too.
        files = Files();
        Edit(files, "roles.json", "\"strong\": 2,", "\"strong\": 2.5,");
        Edit(files, "roles.json", "\"structures\": 0.25,", "\"structures\": 0.5,");
        db = ContentLoader.Load(files);
        Assert.Equal(Fix64.FromRatio(5, 2), db.Combat.Multiplier(UnitRole.LineInfantry, UnitRole.Cavalry));
        Assert.Equal(Fix64.FromRatio(1, 2), db.Combat.StructureMultiplier(UnitRole.LineInfantry));
        Assert.NotEqual(ContentDb.Default.Hash, db.Hash);
    }

    [Fact]
    public void HealingRatesAreDataAndChangeTheHash()
    {
        var db = ContentDb.Default;
        Assert.Equal(10 * Simulation.TicksPerSecond, db.Combat.RecoveryDelayTicks);
        Assert.Equal(Fix64.FromRatio(1, 4), db.Combat.RecoveryPercent);
        Assert.Equal(Fix64.FromInt(2), db.Combat.HomeRecoveryPercent);
        Assert.Equal(12, db.Combat.HomeRange);

        var files = Files();
        Edit(files, "roles.json", "\"recovery\": { \"after\": 10, \"percent\": 0.25, \"homePercent\": 2, \"homeRange\": 12 }",
            "\"recovery\": { \"after\": 5, \"percent\": 0.5, \"homePercent\": 3, \"homeRange\": 20 }");
        db = ContentLoader.Load(files);
        Assert.Equal(5 * Simulation.TicksPerSecond, db.Combat.RecoveryDelayTicks);
        Assert.Equal(Fix64.Half, db.Combat.RecoveryPercent);
        Assert.Equal(Fix64.FromInt(3), db.Combat.HomeRecoveryPercent);
        Assert.Equal(20, db.Combat.HomeRange);
        Assert.NotEqual(ContentDb.Default.Hash, db.Hash);

        files = Files();
        Edit(files, "roles.json", "\"percent\": 0.25, \"homePercent\": 2,", "\"percent\": 150, \"homePercent\": 2, \"rate\": 1,");
        var e = Fails(files);
        Assert.Contains(e.Errors, m => m.StartsWith("roles.json: the table, recovery: \"percent\" is 150; it should be from 0 to 100"));
        Assert.Contains(e.Errors, m => m.StartsWith("roles.json: the table, recovery: unknown field \"rate\""));
        Assert.Equal(2, e.Errors.Count);
    }

    /// <summary>Issue #9's check, part three: a prerequisite cycle fails the load, and says where it is.</summary>
    [Fact]
    public void PrerequisiteCyclesFail()
    {
        var files = Files();
        // Better tools would need smelting, which needs masonry, which needs better tools.
        Edit(files, "techs.json", "\"cost\": { \"wood\": 40, \"stone\": 20 }, \"researchTime\": 30,",
            "\"cost\": { \"wood\": 40, \"stone\": 20 }, \"researchTime\": 30, \"requires\": [ \"smelting\" ],");
        var e = Fails(files);
        Assert.Equal("techs.json: prerequisite cycle, each needs the next: tools -> smelting -> masonry -> tools", Assert.Single(e.Errors));
    }

    [Fact]
    public void ATechThatNeedsItselfFails()
    {
        var files = Files();
        Edit(files, "techs.json", "\"cost\": { \"wood\": 40 }, \"researchTime\": 20 },", "\"cost\": { \"wood\": 40 }, \"researchTime\": 20, \"requires\": \"farming\" },");
        Assert.Equal("techs.json: prerequisite cycle, each needs the next: farming -> farming", Assert.Single(Fails(files).Errors));
    }

    [Fact]
    public void DuplicateIdsFail()
    {
        var files = Files();
        Edit(files, "units.json", "\"id\": \"archer\"", "\"id\": \"spearman\"");
        Edit(files, "techs.json", "\"id\": \"carts\"", "\"id\": \"roads\"");
        var e = Fails(files);
        Assert.Contains("units.json: unit 'spearman': another unit already has this id", e.Errors);
        Assert.Contains("techs.json: tech 'roads': another tech already has this id", e.Errors);
    }

    [Fact]
    public void EveryProblemIsReportedAtOnce()
    {
        var files = Files();
        Edit(files, "units.json", "\"hp\": 40", "\"hp\": 0");
        Edit(files, "units.json", "\"speed\": 8", "\"speed\": \"fast\"");
        Edit(files, "buildings.json", "\"wallHeight\": 4", "\"wallHeigth\": 4");
        Edit(files, "techs.json", "\"researchTime\": 20 },", "\"researchTime\": 2.5 },");
        Edit(files, "factions.json", "\"id\": \"orcs\"", "\"id\": \"Orcs\"");
        var e = Fails(files);
        Assert.Contains("units.json: unit 'worker': \"hp\" is 0; it should be from 1 to 100000", e.Errors);
        Assert.Contains("units.json: unit 'rider': \"speed\" should be a number such as 4 or 4.5", e.Errors);
        Assert.Contains(e.Errors, m => m.StartsWith("buildings.json: building 'town_hall': unknown field \"wallHeigth\""));
        Assert.Contains("techs.json: tech 'farming': \"researchTime\" should be a whole number", e.Errors);
        Assert.Contains(e.Errors, m => m.StartsWith("factions.json: faction 2: id \"Orcs\" should be lower case"));
        // Without the orcs, their techs name a faction that does not exist.
        Assert.Contains(e.Errors, m => m.StartsWith("techs.json: tech 'orc_war_drums': \"faction\" is \"orcs\", which is not a faction"));
        Assert.Contains(e.Errors, m => m.StartsWith("units.json: unit 'wolf_rider': \"faction\" is \"orcs\", which is not a faction"));
        Assert.Contains(e.Errors, m => m.StartsWith("buildings.json: building 'wolf_lodge': \"faction\" is \"orcs\", which is not a faction"));
        Assert.Equal(9, e.Errors.Count);
    }

    [Fact]
    public void BrokenJsonGivesTheLine()
    {
        var files = Files();
        Edit(files, "factions.json", "\"name\": \"Orcs\",", "\"name\": \"Orcs\"");
        var e = Fails(files);
        Assert.StartsWith("factions.json line 28: ", e.Errors[0]); // the line of the Orcs in the file

        files = Files();
        files["units.json"] = "{ \"id\": \"worker\" }";
        Assert.Contains("units.json: should be a list of units: [ { ... }, { ... } ]", Fails(files).Errors);

        files.Remove("techs.json");
        Assert.Contains("techs.json is missing", Fails(files).Errors);
    }

    [Fact]
    public void TheTownHallAndTheWorkerMustExist()
    {
        var files = Files();
        Edit(files, "buildings.json", "\"kind\": \"town_hall\"", "\"kind\": \"house\"");
        Edit(files, "units.json", "\"role\": \"worker\"", "\"role\": \"line_infantry\"");
        var e = Fails(files);
        Assert.Contains(e.Errors, m => m.StartsWith("buildings.json: there must be a building \"town_hall\" of kind town_hall"));
        Assert.Contains("units.json: there must be a unit \"worker\" with role worker", e.Errors);
    }

    [Fact]
    public void NothingMayNeedAnotherFactionsTech()
    {
        var files = Files();
        Edit(files, "techs.json", "\"requires\": [ \"horse_breeding\" ]", "\"requires\": [ \"settler_guilds\" ]");
        Edit(files, "units.json", "\"requires\": \"archery\"", "\"requires\": \"orc_war_drums\"");
        var e = Fails(files);
        Assert.Contains("techs.json: tech 'orc_beast_pens' is for only orcs but needs \"settler_guilds\", which only settlers can research", e.Errors);
        Assert.Contains("units.json: unit 'archer' is for every faction but needs \"orc_war_drums\", which only orcs can research", e.Errors);
        Assert.Equal(2, e.Errors.Count);
    }

    [Fact]
    public void EffectsAreChecked()
    {
        var files = Files();
        Edit(files, "techs.json", "{ \"type\": \"gather_speed\", \"percent\": 25 }", "{ \"type\": \"gather_speed\", \"amount\": 25 }");
        Edit(files, "techs.json", "\"role\": \"line_infantry\", \"amount\": 20", "\"role\": \"line_infantry\", \"unit\": \"spearman\", \"amount\": 20");
        Edit(files, "techs.json", "{ \"type\": \"carry\", \"amount\": 3 }", "{ \"type\": \"carry\", \"amount\": 0 }");
        var e = Fails(files);
        Assert.Contains(e.Errors, m => m.StartsWith("techs.json: tech 'tools', effect 1: unknown field \"amount\""));
        Assert.Contains("techs.json: tech 'tools', effect 1: needs \"percent\"", e.Errors);
        Assert.Contains("techs.json: tech 'settler_drill', effect 1: give a \"role\" or a \"unit\", not both", e.Errors);
        Assert.Contains("techs.json: tech 'carts', effect 1: \"amount\" is 0; it should be from 1 to 100", e.Errors);
        Assert.Equal(4, e.Errors.Count);
    }

    [Fact]
    public void ContentCanBeAddedWithoutCode()
    {
        var files = Files();
        Edit(files, "units.json", "\n]", ",\n  { \"id\": \"pikeman\", \"name\": \"Pikeman\", \"role\": \"heavy_infantry\", \"speed\": 3.5, \"hp\": 120,\n" +
            "    \"cost\": { \"food\": 50, \"iron\": 2 }, \"trainTime\": 20, \"trainedAt\": \"barracks\", \"requires\": [ \"smelting\", \"settler_drill\" ],\n" +
            "    \"faction\": \"settlers\" }\n]");
        var db = ContentLoader.Load(files);
        var pikeman = db.Unit("pikeman");
        Assert.Equal(db.Units.Count - 1, pikeman.Index);
        Assert.Equal(Fix64.FromRatio(7, 2), pikeman.Speed);
        Assert.Equal(new[] { "smelting", "settler_drill" }, pikeman.Requires.Select(t => t.Id));
        Assert.Contains("pikeman", db.Building("barracks").Trains);
        Assert.Same(db.Faction("settlers"), pikeman.Faction);
    }

    [Theory]
    [InlineData("worker", true)]
    [InlineData("line_infantry", true)]
    [InlineData("w2_x", true)]
    [InlineData("", false)]
    [InlineData("Worker", false)]
    [InlineData("2nd", false)]
    [InlineData("big house", false)]
    [InlineData("_x", false)]
    public void IdsAreLowerCaseWords(string id, bool valid) => Assert.Equal(valid, ContentLoader.IsId(id));

    [Fact]
    public void EnumNamesAreSnakeCase()
    {
        Assert.Equal("line_infantry", ContentLoader.SnakeName(UnitRole.LineInfantry));
        Assert.Equal("iron_ore", ContentLoader.SnakeName(Good.IronOre));
        Assert.Equal("town_hall", ContentLoader.SnakeName(BuildingKind.TownHall));
        Assert.Equal("gather_speed", ContentLoader.SnakeName(TechEffectType.GatherSpeed));
    }
}
