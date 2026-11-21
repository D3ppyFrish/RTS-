using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;

namespace Rts.Sim.Content;

/// <summary>Game data that failed its checks: every problem found, one line each.</summary>
public sealed class ContentException : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public ContentException(IReadOnlyList<string> errors) : base(Format(errors))
    {
        Errors = errors;
    }

    private static string Format(IReadOnlyList<string> errors) =>
        $"The game data has {errors.Count} problem{(errors.Count == 1 ? "" : "s")}:\n  " + string.Join("\n  ", errors);
}

/// <summary>
/// Loads content from six JSON files, <c>factions.json</c>, <c>techs.json</c>, <c>buildings.json</c>,
/// <c>units.json</c>, the combat role table <c>roles.json</c> and <c>citystates.json</c> (each describes its
/// fields in a comment at the top), and checks it before the game uses it: unknown or misspelt fields, missing or out-of-range
/// values, duplicate ids, references to ids that do not exist, prerequisite cycles, techs or units that
/// would need another faction's techs, a role table with a role missing, listed twice, or both strong
/// and weak against another, and evolution forms that loop or that no unit could ever reach.
/// Every problem is reported at once in a <see cref="ContentException"/>. The game's own files are built
/// into the core; <see cref="LoadDirectory"/> reads replacements from disk.
///
/// Ids are lower case letters, digits and underscores, starting with a letter; names of goods, roles,
/// kinds, blocks and effects are written the same way (<c>iron_ore</c>, <c>line_infantry</c>). Times are
/// whole seconds except attack times; those, speeds, ranges and multipliers may have decimals (read
/// without floating point, so every machine gets the same numbers).
/// </summary>
public static class ContentLoader
{
    /// <summary>The data files, in the order they are read.</summary>
    public static readonly IReadOnlyList<string> Files =
        new[] { "factions.json", "techs.json", "buildings.json", "units.json", "roles.json", "citystates.json", "relics.json", "ruins.json" };

    private const int Tps = Simulation.TicksPerSecond;
    private const int MaxSeconds = 3600;

    private static readonly JsonDocumentOptions Options = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        MaxDepth = 16,
    };

    /// <summary>Building materials a building's walls, footing and roof may use.</summary>
    private static readonly BlockType[] Materials = { BlockType.Planks, BlockType.Thatch, BlockType.RoofTile, BlockType.StoneBrick, BlockType.Palisade, BlockType.Rampart, BlockType.Trestle };

    /// <summary>Loads the game's own data, built into the core.</summary>
    public static ContentDb LoadEmbedded() => Load(EmbeddedText);

    /// <summary>The text of one of the game's own data files, or null if there is no such file.</summary>
    public static string? EmbeddedText(string file)
    {
        using var stream = typeof(ContentLoader).Assembly.GetManifestResourceStream("data/" + file);
        if (stream == null) return null;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>Loads data files from a directory (all four must be there).</summary>
    public static ContentDb LoadDirectory(string directory) => Load(file =>
    {
        string path = Path.Combine(directory, file);
        return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
    });

    /// <summary>Loads data from texts by file name (as in <see cref="Files"/>).</summary>
    public static ContentDb Load(IReadOnlyDictionary<string, string> files) =>
        Load(file => files.TryGetValue(file, out var text) ? text : null);

    /// <summary>Loads data, getting each file's text (null if the file is missing) from <paramref name="read"/>.</summary>
    public static ContentDb Load(Func<string, string?> read) => new Loader(read).Run();

    /// <summary>
    /// Snake-case name of an enum value, as data files write it: <c>LineInfantry</c> is <c>line_infantry</c>.
    /// </summary>
    public static string SnakeName<T>(T value) where T : struct, Enum
    {
        string name = value.ToString();
        var sb = new StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i])) sb.Append('_');
            sb.Append(char.ToLowerInvariant(name[i]));
        }
        return sb.ToString();
    }

    /// <summary>True if the text is a valid id: a lower case letter, then lower case letters, digits or underscores.</summary>
    public static bool IsId(string text)
    {
        if (text.Length is 0 or > 40 || text[0] < 'a' || text[0] > 'z') return false;
        foreach (char c in text)
            if (!(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_')) return false;
        return true;
    }

    private static bool TryEnum<T>(string text, out T value) where T : struct, Enum
    {
        value = default;
        if (!IsId(text)) return false;
        var sb = new StringBuilder(text.Length);
        bool upper = true;
        foreach (char c in text)
        {
            if (c == '_')
            {
                upper = true;
                continue;
            }
            sb.Append(upper ? char.ToUpperInvariant(c) : c);
            upper = false;
        }
        return Enum.TryParse(sb.ToString(), ignoreCase: false, out value) && Enum.IsDefined(value) && SnakeName(value) == text;
    }

    private static string Names<T>() where T : struct, Enum
    {
        var names = new List<string>();
        foreach (var v in Enum.GetValues<T>()) names.Add(SnakeName(v));
        return string.Join(", ", names);
    }

    /// <summary>One load: reads every file, then resolves references between them, then checks the whole.</summary>
    private sealed class Loader
    {
        private readonly Func<string, string?> _read;
        private readonly List<string> _errors = new();
        private readonly ContentDb _db = new();
        private readonly List<JsonDocument> _docs = new();
        // References between definitions, resolved once every file has been read.
        private readonly List<Action> _resolve = new();

        public Loader(Func<string, string?> read) => _read = read;

        public ContentDb Run()
        {
            try
            {
                ReadFile("factions.json", "faction", ReadFaction);
                ReadFile("techs.json", "tech", ReadTech);
                ReadFile("buildings.json", "building", ReadBuilding);
                ReadFile("units.json", "unit", ReadUnit);
                ReadRoles("roles.json");
                ReadFile("citystates.json", "city-state", ReadCityState);
                ReadFile("relics.json", "relic", ReadRelic);
                ReadFile("ruins.json", "find", ReadRuinFind);
                foreach (var resolve in _resolve) resolve();
                CheckTree();
                CheckFactions();
                CheckForms();
                CheckUpgrades();
                CheckRequired();
                if (_errors.Count > 0) throw new ContentException(_errors);
                Finish();
                return _db;
            }
            finally
            {
                foreach (var doc in _docs) doc.Dispose();
            }
        }

        /// <summary>The file's JSON, or null (and reported) if it is missing or is not valid JSON.</summary>
        private JsonDocument? Parse(string file)
        {
            var text = _read(file);
            if (text == null)
            {
                _errors.Add($"{file} is missing");
                return null;
            }
            try
            {
                var doc = JsonDocument.Parse(text, Options);
                _docs.Add(doc);
                return doc;
            }
            catch (JsonException e)
            {
                string message = e.Message;
                int cut = message.IndexOf(" LineNumber:", StringComparison.Ordinal);
                if (cut > 0) message = message[..cut];
                _errors.Add($"{file} line {(e.LineNumber ?? 0) + 1}: {message}");
                return null;
            }
        }

        private void ReadFile(string file, string what, Action<Obj> read)
        {
            if (Parse(file) is not { } doc) return;
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                _errors.Add($"{file}: should be a list of {what}s: [ {{ ... }}, {{ ... }} ]");
                return;
            }
            int n = 0;
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                n++;
                if (e.ValueKind != JsonValueKind.Object) _errors.Add($"{file}: {what} {n} should be an object: {{ ... }}");
                else read(new Obj(_errors, file, what, $"{what} {n}", e));
            }
        }

        // --- Files -------------------------------------------------------------------------------

        private void ReadFaction(Obj o)
        {
            var id = o.Id();
            o.Keys("id", "name", "description", "uiLook", "experience", "tamesMonsters", "duskTree", "aiStyle");
            if (id == null) return;
            if (_db.TryGetFaction(id, out _) || id == _db.Wild.Id)
            {
                o.Error(id == _db.Wild.Id ? "\"wild\" is the monsters' side, which the game provides" : "another faction already has this id");
                return;
            }
            var faction = new FactionDef
            {
                Id = id,
                Name = o.Str("name", true) ?? id,
                Description = o.Str("description", false) ?? "",
                UiLook = o.Str("uiLook", false) ?? "",
                Experience = Experience(o),
                TamesMonsters = o.Bool("tamesMonsters"),
                DuskTree = o.Enum("duskTree", DuskTreeUse.None, required: false),
                AiStyle = o.Enum("aiStyle", AiStyle.Waves, required: false),
            };
            _db.Add(faction);
        }

        /// <summary>
        /// A lair's prizes, <c>"prizes": { "orcs": { "building": id }, "settlers": { "form": id } }</c>: for each
        /// faction one of a building put up on the den, a form, or a relic, resolved once every file is read.
        /// </summary>
        private void Prizes(Obj lair, LairDef def)
        {
            if (!lair.E.TryGetProperty("prizes", out var e)) return;
            var where = new Obj(_errors, lair.File, "prizes", $"{lair.Where}, prizes", e);
            if (e.ValueKind != JsonValueKind.Object)
            {
                where.Error("should be an object: { \"orcs\": { \"building\": \"wolf_lodge\" }, \"settlers\": { \"relic\": \"...\" } }");
                return;
            }
            var prizes = new List<LairPrize>();
            def.Prizes = prizes;
            foreach (var p in e.EnumerateObject())
            {
                var o = new Obj(_errors, lair.File, "prize", $"{lair.Where}, prizes, {p.Name}", p.Value);
                if (!_db.TryGetFaction(p.Name, out var faction))
                {
                    o.Error($"\"{p.Name}\" is not a faction");
                    continue;
                }
                if (p.Value.ValueKind != JsonValueKind.Object)
                {
                    o.Error("should be an object: { \"building\": \"...\" }, { \"form\": \"...\" } or { \"relic\": \"...\" }");
                    continue;
                }
                o.Keys("building", "form", "relic");
                var building = o.Str("building", false);
                var form = o.Str("form", false);
                var relic = o.Str("relic", false);
                if ((building != null ? 1 : 0) + (form != null ? 1 : 0) + (relic != null ? 1 : 0) != 1)
                {
                    o.Error("needs one of \"building\" (put up on the den), \"form\" (a unit near the den takes it) or \"relic\" (a unit near the den takes it up)");
                    continue;
                }
                _resolve.Add(() =>
                {
                    if (building != null)
                    {
                        if (!_db.TryGetBuilding(building, out var b)) o.Error($"\"building\" is \"{building}\", which is not a building");
                        else if (!b.FromDen) o.Error($"\"building\" is \"{building}\", which needs \"fromDen\": true in buildings.json");
                        else if (b.Faction != null && b.Faction != faction) o.Error($"\"building\" is \"{building}\", which only {b.Faction.Id} can build");
                        else prizes.Add(new LairPrize(faction, new DenUse { Building = b }));
                    }
                    else if (form != null)
                    {
                        if (!_db.TryGetUnit(form, out var u)) o.Error($"\"form\" is \"{form}\", which is not a unit");
                        else if (!u.FromDen) o.Error($"\"form\" is \"{form}\", which needs \"fromDen\": true in units.json");
                        else if (u.Faction != null && u.Faction != faction) o.Error($"\"form\" is \"{form}\", which only {u.Faction.Id} have");
                        else prizes.Add(new LairPrize(faction, new DenUse { Form = u }));
                    }
                    else
                    {
                        if (!_db.TryGetRelic(relic!, out var r)) o.Error($"\"relic\" is \"{relic}\", which is not in relics.json");
                        else prizes.Add(new LairPrize(faction, new DenUse { Relic = r }));
                    }
                });
            }
        }

        /// <summary>A faction's experience rules, or null if its units earn none.</summary>
        private ExperienceRules? Experience(Obj faction)
        {
            if (!faction.E.TryGetProperty("experience", out var e)) return null;
            var o = new Obj(_errors, faction.File, "experience", $"{faction.Where}, experience", e);
            if (e.ValueKind != JsonValueKind.Object)
            {
                o.Error("should be an object: { \"levels\": [ 150, 400 ], \"perDamage\": 1, ... }");
                return null;
            }
            o.Keys("levels", "perDamage", "perKill", "perBuildSecond", "perMinute");
            var levels = o.IntList("levels", 1, 1000000);
            if (levels.Count == 0) o.Error("needs \"levels\": the experience for each level, such as [ 150, 400 ]");
            for (int k = 1; k < levels.Count; k++)
            {
                if (levels[k] > levels[k - 1]) continue;
                o.Error("\"levels\" should rise: each level needs more experience than the one before");
                break;
            }
            return new ExperienceRules
            {
                Levels = levels.ToArray(),
                PerDamage = o.Int("perDamage", 0, 1000, 0),
                PerKill = o.Int("perKill", 0, 100000, 0),
                PerBuildSecond = o.Int("perBuildSecond", 0, 1000, 0),
                PerMinute = o.Int("perMinute", 0, 100000, 0),
            };
        }

        /// <summary>A monster's lairs, or null if it keeps none.</summary>
        /// <summary>
        /// A unit's armour on each part, from <c>"armor": { "head": 2, "torso": 4, "arms": 1, "legs": 1 }</c>
        /// (parts left out have none). A beast names its torso "body" and has no arms.
        /// </summary>
        private int[] Armour(Obj unit, BodyPlan plan)
        {
            var armour = new int[Body.Parts];
            bool beast = plan == BodyPlan.Beast;
            if (Part(unit, "armor", beast ? "{ \"head\": 1, \"body\": 2, \"legs\": 1 }" : "{ \"head\": 2, \"torso\": 4, \"arms\": 1, \"legs\": 1 }") is not { } o)
                return armour;
            if (beast) o.Keys("head", "body", "legs");
            else o.Keys("head", "torso", "arms", "legs");
            armour[(int)BodyPart.Head] = o.Int("head", 0, 100, 0);
            armour[(int)BodyPart.Torso] = o.Int(beast ? "body" : "torso", 0, 100, 0);
            if (!beast) armour[(int)BodyPart.Arms] = o.Int("arms", 0, 100, 0);
            armour[(int)BodyPart.Legs] = o.Int("legs", 0, 100, 0);
            return armour;
        }

        private LairDef? Lair(Obj unit)
        {
            if (!unit.E.TryGetProperty("lair", out var e)) return null;
            var o = new Obj(_errors, unit.File, "lair", $"{unit.Where}, lair", e);
            if (e.ValueKind != JsonValueKind.Object)
            {
                o.Error("should be an object: { \"pack\": 4, \"guard\": 12, \"leash\": 20, \"reward\": { \"food\": 150 } }");
                return null;
            }
            o.Keys("pack", "guard", "leash", "reward", "duskTree", "biome", "count", "atStarts", "prizes", "cavern", "roam", "grow", "growTo");
            int guard = o.Int("guard", 1, 64, 12);
            int leash = o.Int("leash", 1, 128, guard + 8);
            if (leash < guard) o.Error($"\"leash\" is {leash}, but the pack guards {guard} blocks around the den, so it should be at least that");
            bool dusk = o.Bool("duskTree");
            Biome? biome = null;
            if (o.Str("biome", false) is { } name)
            {
                if (Enum.TryParse<Biome>(name, ignoreCase: true, out var b) && b is not (Biome.Ocean or Biome.River)) biome = b;
                else o.Error($"\"biome\" is \"{name}\": it should be grassland, forest, hills, swamp, beach or wilds");
            }
            var cavern = CavernSpot.None;
            if (o.Str("cavern", false) is { } spot)
            {
                if (spot == "along") cavern = CavernSpot.Along;
                else if (spot == "deepest") cavern = CavernSpot.Deepest;
                else o.Error($"\"cavern\" is \"{spot}\": it should be along (spread along the cavern) or deepest (its far end)");
            }
            if (cavern != CavernSpot.None && biome != null) o.Error("a lair is in the cavern or in a \"biome\", not both");
            int count = o.Int("count", 0, 64, biome != null || cavern != CavernSpot.None ? 1 : 0);
            if (count > 0 && biome == null && cavern == CavernSpot.None) o.Error("\"count\" needs a \"biome\" or \"cavern\" to spread the lairs over");
            int roam = o.Int("roam", 0, 128, 0);
            if (roam > 0 && biome == null) o.Error("\"roam\" needs a \"biome\": only the lairs spread across the map roam");
            int grow = o.Int("grow", 0, 3600, 0);
            int pack = o.Int("pack", 1, 50, 4, required: true);
            int growTo = o.Int("growTo", 1, 50, grow > 0 ? pack * 2 : pack);
            if (e.TryGetProperty("growTo", out _) && grow == 0) o.Error("\"growTo\" needs a \"grow\" (seconds between births)");
            if (grow > 0 && growTo <= pack) o.Error($"\"growTo\" is {growTo}, but the pack starts {pack} strong, so it should be more");
            if (grow > 0 && dusk) o.Error("the dusk tree's warden does not \"grow\"");
            bool atStarts = !dusk && (e.TryGetProperty("atStarts", out _) ? o.Bool("atStarts") : biome == null && cavern == CavernSpot.None);
            if (dusk && (biome != null || cavern != CavernSpot.None || o.Bool("atStarts") || e.TryGetProperty("prizes", out _)))
                o.Error("a lair with \"duskTree\" guards the dusk tree only: no \"biome\", \"cavern\", \"atStarts\" or \"prizes\"");
            var def = new LairDef
            {
                Pack = pack,
                Guard = guard,
                Leash = Math.Max(leash, guard),
                Reward = o.Cost("reward"),
                DuskTree = dusk,
                Biome = biome,
                Count = biome != null || cavern != CavernSpot.None ? count : 0,
                AtStarts = atStarts,
                Cavern = cavern,
                Roam = biome != null ? roam : 0,
                GrowTicks = dusk ? 0 : grow * Simulation.TicksPerSecond,
                GrowTo = grow > 0 && !dusk ? growTo : pack,
            };
            if (!dusk) Prizes(o, def);
            return def;
        }

        /// <summary>Who rides a beaten monster and what they become (unit ids, resolved later), or null if nobody does.</summary>
        private (string Rider, string Becomes)? Ride(Obj unit)
        {
            if (!unit.E.TryGetProperty("ride", out var e)) return null;
            var o = new Obj(_errors, unit.File, "ride", $"{unit.Where}, ride", e);
            if (e.ValueKind != JsonValueKind.Object)
            {
                o.Error("should be an object: { \"rider\": \"spearman\", \"becomes\": \"wolf_rider\" }");
                return null;
            }
            o.Keys("rider", "becomes");
            var rider = o.Str("rider", true);
            var becomes = o.Str("becomes", true);
            return rider != null && becomes != null ? (rider, becomes) : null;
        }

        private void ReadTech(Obj o)
        {
            var id = o.Id();
            o.Keys("id", "name", "description", "icon", "faction", "cost", "researchTime", "requires", "effects", "discovery");
            if (id == null) return;
            if (_db.TryGetTech(id, out _))
            {
                o.Error("another tech already has this id");
                return;
            }
            var def = new TechDef
            {
                Id = id,
                Name = o.Str("name", true) ?? id,
                Description = o.Str("description", false) ?? "",
                Icon = o.Str("icon", false) ?? id,
                Cost = o.Cost("cost"),
                ResearchTicks = o.Int("researchTime", 1, MaxSeconds, 20, required: true) * Tps,
                Discovery = o.Enum("discovery", Discovery.None, required: false),
            };
            _db.Add(def);
            var faction = o.Str("faction", false);
            var requires = o.Ids("requires");
            _resolve.Add(() =>
            {
                def.Faction = Faction(o, faction);
                def.Requires = Techs(o, "requires", requires);
                def.Effects = Effects(o);
            });
        }

        private void ReadBuilding(Obj o)
        {
            var id = o.Id();
            o.Keys("id", "name", "kind", "size", "wallHeight", "wall", "footing", "roof", "chimney", "cost", "buildTime",
                "housing", "storage", "raidable", "workerSlots", "sight", "light", "requires", "faction", "icon", "fromDen", "yield", "yieldTime",
                "sturdy", "thick", "walkway", "upgrade", "facing", "aim", "speed", "works", "workTime", "shore", "plants", "mana", "channelTime");
            if (id == null) return;
            if (_db.TryGetBuilding(id, out _))
            {
                o.Error("another building already has this id");
                return;
            }
            var kind = o.Enum("kind", BuildingKind.House, required: true);
            var (sizeX, sizeZ) = o.Size("size");
            int slots = o.Int("workerSlots", 0, 20, 0);
            if (kind is BuildingKind.Farm or BuildingKind.Smelter or BuildingKind.Quarry or BuildingKind.Shrine && slots == 0)
                o.Error($"a {SnakeName(kind)} needs \"workerSlots\" of 1 or more, or nobody can work it");
            int wallHeight = o.Int("wallHeight", 1, 12, 3);
            if (kind is BuildingKind.Wall or BuildingKind.Tower && wallHeight <= Walk.MaxClimb)
                o.Error($"a {SnakeName(kind)} needs \"wallHeight\" of {Walk.MaxClimb + 1} or more, or units step over it");
            if (kind == BuildingKind.Gate && wallHeight <= Walk.Clearance)
                o.Error($"a gate needs \"wallHeight\" of {Walk.Clearance + 1} or more: its leaves are {Walk.Clearance} high, with wall over them");
            if (kind == BuildingKind.Bridge && (sizeX != 1 || sizeZ != 1 || wallHeight != 1))
                o.Error("a bridge is laid a column at a time, one deck block each: \"size\" [1, 1] and \"wallHeight\" 1");
            var def = new BuildingDef
            {
                Id = id,
                Name = o.Str("name", true) ?? id,
                Icon = o.Str("icon", false) ?? id,
                Kind = kind,
                SizeX = sizeX,
                SizeZ = sizeZ,
                WallHeight = wallHeight,
                Wall = o.Material("wall", BlockType.Planks),
                Footing = o.Material("footing", BlockType.Air),
                Roof = o.Material("roof", BlockType.Thatch),
                Chimney = o.Bool("chimney"),
                Cost = o.Cost("cost"),
                BuildTicks = o.Int("buildTime", 1, MaxSeconds, 20, required: true) * Tps,
                Housing = o.Int("housing", 0, 1000, 0),
                Storage = o.Bool("storage"),
                Raidable = o.Bool("raidable"),
                WorkerSlots = slots,
                Sight = o.Int("sight", 0, 64, 6),
                Light = o.Int("light", 0, 32, 0),
                FromDen = o.Bool("fromDen"),
                Yield = o.Cost("yield"),
                YieldTicks = o.Int("yieldTime", 1, MaxSeconds, 0) * Tps,
                Sturdy = o.Int("sturdy", 1, 1000, 1),
                Thick = o.Bool("thick"),
                Walkway = o.Bool("walkway"),
                Aim = o.Int("aim", 0, 100, 0),
                Speed = o.Int("speed", 100, 1000, 100),
                Works = o.Cost("works"),
                WorkTicks = o.Int("workTime", 1, MaxSeconds, 0) * Tps,
                Shore = o.Bool("shore"),
                Plants = o.Int("plants", 0, 32, 0),
                Mana = o.Int("mana", 0, 1000, 0),
                ChannelTicks = o.Int("channelTime", 1, MaxSeconds, 0) * Tps,
            };
            if (kind == BuildingKind.Shrine && (def.Mana == 0 || def.ChannelTicks == 0))
                o.Error("a shrine needs \"mana\" (what one crystal gives) and \"channelTime\" (the seconds it takes)");
            if (kind != BuildingKind.Shrine && (def.Mana > 0 || def.ChannelTicks > 0))
                o.Error("only a shrine has \"mana\" and \"channelTime\"");
            if (def.Shore && kind is BuildingKind.Wall or BuildingKind.Gate or BuildingKind.Bridge or BuildingKind.Tower or BuildingKind.Totem)
                o.Error($"a {SnakeName(kind)} cannot need the water's edge (\"shore\")");
            if (def.Plants > 0 && kind != BuildingKind.Quarry) o.Error("only a quarry \"plants\" trees");
            if (def.Plants > 0 && def.Works.Length > 0) o.Error("a quarry that \"plants\" trees gives no \"works\"");
            if (kind == BuildingKind.Quarry && def.WorkTicks == 0 || kind == BuildingKind.Quarry && def.Works.Length == 0 && def.Plants == 0)
                o.Error("a quarry needs \"works\" (the goods a round of work gives) or \"plants\", and \"workTime\" (the seconds a round takes)");
            if (kind != BuildingKind.Quarry && (def.Works.Length > 0 || def.WorkTicks > 0))
                o.Error("only a quarry has \"works\" and \"workTime\"");
            bool wall = kind is BuildingKind.Wall or BuildingKind.Gate, tower = kind == BuildingKind.Tower;
            if (def.Walkway && !wall && !tower) o.Error("only a wall, gate or tower has a \"walkway\"");
            if (def.Thick && !wall && !tower) o.Error("only a wall, gate or tower is \"thick\"");
            if (def.Aim > 0 && !tower) o.Error("only a tower has \"aim\"");
            if (def.Aim > 0 && !def.Walkway) o.Error("\"aim\" is for the units posted on it: it needs a \"walkway\"");
            if (def.Walkway && wallHeight <= CombatSystem.MeleeHeight + 1)
                o.Error($"a \"walkway\" needs \"wallHeight\" of {CombatSystem.MeleeHeight + 2} or more, or blows from the ground reach the units on it");
            if ((def.Yield.Length > 0) != (def.YieldTicks > 0))
                o.Error("\"yield\" and \"yieldTime\" go together: the goods it brings in, and the seconds between");
            if (def.FromDen && kind is BuildingKind.TownHall or BuildingKind.Wall or BuildingKind.Gate or BuildingKind.Totem or BuildingKind.Bridge or BuildingKind.Tower)
                o.Error($"a {SnakeName(kind)} cannot be built on a den (\"fromDen\")");
            _db.Add(def);
            var faction = o.Str("faction", false);
            var requires = o.Ids("requires");
            var upgrade = o.Str("upgrade", false);
            // What is laid a column at a time, a city-state's totem and what is built on a den have no upgrade.
            bool upgradable = kind is not (BuildingKind.Light or BuildingKind.Bridge or BuildingKind.Totem);
            if (upgrade != null && !upgradable) o.Error($"a {SnakeName(kind)} has no \"upgrade\"");
            var facing = o.Str("facing", false);
            if (facing != null && (!wall || sizeX != 1 || sizeZ != 1)) o.Error("only a wall or gate one column across has a \"facing\"");
            _resolve.Add(() =>
            {
                def.Faction = Faction(o, faction);
                def.Requires = Techs(o, "requires", requires);
                if (facing != null && wall)
                {
                    // A facing stands beside what it faces, level with it: the same kind and height, one column across.
                    if (!_db.TryGetBuilding(facing, out var face)) o.Error($"\"facing\": there is no building \"{facing}\"");
                    else if (face == def) o.Error("\"facing\": a building is not its own facing");
                    else if (face.Kind != def.Kind || face.SizeX != 1 || face.SizeZ != 1 || face.WallHeight != def.WallHeight)
                        o.Error($"\"facing\": \"{facing}\" must be of the same kind and height, one column across");
                    else if (face.FacingOf != null) o.Error($"\"facing\": \"{facing}\" is already the facing of \"{face.FacingOf.Id}\"");
                    else
                    {
                        def.Facing = face;
                        face.FacingOf = def;
                    }
                }
                if (upgrade == null || !upgradable) return;
                if (!_db.TryGetBuilding(upgrade, out var to))
                {
                    o.Error($"\"upgrade\": there is no building \"{upgrade}\"");
                    return;
                }
                // An upgrade takes the place of what stands there: the same kind, at least as tall, and each building
                // is the upgrade of one other at most, so every building has one way up. A wall, gate or tower keeps
                // its footprint; anything else may grow, but never shrink.
                if (to == def) o.Error("\"upgrade\": a building is not its own upgrade");
                else if (to.Kind != def.Kind || ((wall || tower) && (to.SizeX != def.SizeX || to.SizeZ != def.SizeZ)))
                    o.Error($"\"upgrade\": \"{upgrade}\" must be of the same kind and size");
                else if (to.SizeX < def.SizeX || to.SizeZ < def.SizeZ)
                    o.Error($"\"upgrade\": \"{upgrade}\" must be of the same kind and at least as big");
                else if (to.WallHeight < def.WallHeight) o.Error($"\"upgrade\": \"{upgrade}\" must be at least as tall");
                else if (to.UpgradeOf != null) o.Error($"\"upgrade\": \"{upgrade}\" is already the upgrade of \"{to.UpgradeOf.Id}\"");
                else if (to.FromDen || def.FromDen) o.Error("\"upgrade\": not for a building put up on a den");
                else
                {
                    def.Upgrade = to;
                    to.UpgradeOf = def;
                }
            });
        }

        private void ReadUnit(Obj o)
        {
            var id = o.Id();
            o.Keys("id", "name", "description", "role", "speed", "hp", "model", "cost", "trainTime", "trainedAt", "requires",
                "faction", "damage", "attackTime", "range", "sight", "splash", "weakTo", "heal", "healTime", "healRange",
                "regen", "work", "evolvesFrom", "lair", "ride", "hiredFrom", "fromDen", "attack", "body", "armor", "shield",
                "bleeds", "tracksBlood", "blast", "earthWall", "iceBridge", "chasm", "growForest", "start", "hero", "rally", "steady", "inspire", "volley", "spur", "undermine", "quicken", "hasten", "awakening", "brood", "bounty");
            if (id == null) return;
            if (_db.TryGetUnit(id, out _))
            {
                o.Error("another unit already has this id");
                return;
            }
            var range = o.Decimal("range", Fix64.Half, Fix64.FromInt(32), Fix64.FromRatio(5, 4), required: false);
            var attackTime = o.Decimal("attackTime", Fix64.FromRatio(1, Tps), Fix64.FromInt(60), Fix64.One, required: false);
            var healTime = o.Decimal("healTime", Fix64.FromRatio(1, Tps), Fix64.FromInt(60), Fix64.FromInt(2), required: false);
            var body = o.Enum("body", BodyPlan.Humanoid, required: false);
            var def = new UnitDef
            {
                Id = id,
                Name = o.Str("name", true) ?? id,
                Description = o.Str("description", false) ?? "",
                Role = o.Enum("role", UnitRole.Worker, required: true),
                Speed = o.Decimal("speed", Fix64.FromRatio(1, 10), Fix64.FromInt(20), Fix64.FromInt(4)),
                MaxHp = o.Int("hp", 1, 100000, 50, required: true),
                Model = o.Str("model", false) ?? id,
                Cost = o.Cost("cost"),
                TrainTicks = o.Int("trainTime", 1, MaxSeconds, 10) * Tps,
                Damage = o.Int("damage", 0, 100000, 0),
                AttackTicks = Math.Max(1, (attackTime * Tps).RoundToInt()),
                Range = range,
                Sight = o.Decimal("sight", Fix64.One, Fix64.FromInt(64), Fix64.Max(Fix64.FromInt(8), range), required: false),
                Splash = o.Decimal("splash", Fix64.Zero, Fix64.FromInt(8), Fix64.Zero, required: false),
                WeakTo = o.RolePercents("weakTo", 1, 1000),
                Attack = o.Enum("attack", AttackKind.Slash, required: false),
                Body = body,
                Armour = Armour(o, body),
                Shield = o.Int("shield", 0, 90, 0),
                Bleeds = !o.Has("bleeds") || o.Bool("bleeds"),
                TracksBlood = o.Bool("tracksBlood"),
                Blast = o.Bool("blast"),
                EarthWall = o.Bool("earthWall"),
                IceBridge = o.Bool("iceBridge"),
                Chasm = o.Bool("chasm"),
                GrowForest = o.Bool("growForest"),
                Heal = o.Int("heal", 0, 100000, 0),
                HealTicks = Math.Max(1, (healTime * Tps).RoundToInt()),
                HealRange = o.Decimal("healRange", Fix64.One, Fix64.FromInt(32), Fix64.FromInt(5), required: false),
                Regen = o.Int("regen", 0, 100, 0),
                Hero = o.Bool("hero"),
                Rally = o.Decimal("rally", Fix64.Zero, Fix64.FromInt(32), Fix64.Zero, required: false),
                Steady = o.Int("steady", 0, 100, 0),
                Inspire = o.Int("inspire", 1, 100, 1),
                Volley = o.Int("volley", 0, 200, 0),
                Spur = o.Int("spur", 0, 200, 0),
                Undermine = o.Int("undermine", 0, 400, 0),
                Quicken = o.Int("quicken", 0, 400, 0),
                Hasten = o.Int("hasten", 0, 400, 0),
                WorkPercent = o.Int("work", 10, 1000, 100),
                Lair = Lair(o),
                Awakening = o.Int("awakening", 1, Rts.Sim.World.AwakeningSystem.Stages, 0),
                Brood = o.Int("brood", 1, 50, 0),
                Bounty = o.Cost("bounty"),
                FromDen = o.Bool("fromDen"),
                InStart = !o.Has("start") || o.Bool("start"),
            };
            if (def.Heal == 0 && (o.Has("healTime") || o.Has("healRange")))
                o.Error("has \"healTime\" or \"healRange\" but no \"heal\"");
            if (def.Rally == Fix64.Zero && (o.Has("steady") || o.Has("inspire") || o.Has("volley") || o.Has("spur") || o.Has("undermine") || o.Has("quicken") || o.Has("hasten")))
                o.Error("has \"steady\", \"inspire\", \"volley\", \"spur\", \"undermine\", \"quicken\" or \"hasten\" but no \"rally\"");
            _db.Add(def);
            var trainedAt = o.Str("trainedAt", false);
            var faction = o.Str("faction", false);
            var requires = o.Ids("requires");
            var evolvesFrom = o.Str("evolvesFrom", false);
            if (def.FromDen && evolvesFrom == null)
                o.Error("is taken at a den (\"fromDen\"), so it is a form: it needs \"evolvesFrom\"");
            if (evolvesFrom != null && (trainedAt != null || o.Has("cost") || o.Has("trainTime")))
                o.Error("is a form units evolve into, so it is not trained: it cannot have \"trainedAt\", \"cost\" or \"trainTime\"");
            if (def.Lair != null && (trainedAt != null || evolvesFrom != null || o.Has("cost") || o.Has("trainTime")))
                o.Error("lives in lairs, so it is not trained or evolved into: it cannot have \"trainedAt\", \"evolvesFrom\", \"cost\" or \"trainTime\"");
            if (def.Lair != null && def.Role != UnitRole.Monster)
                o.Error("lives in lairs, so its \"role\" should be monster");
            if (def.Awakening > 0 && (def.Role != UnitRole.Monster || def.Lair != null || trainedAt != null || evolvesFrom != null || o.Has("cost")))
                o.Error("rises with the dark power (\"awakening\"), so it is a monster with no \"lair\", \"trainedAt\", \"evolvesFrom\" or \"cost\"");
            if ((def.Awakening > 0) != (def.Brood > 0))
                o.Error("\"awakening\" and \"brood\" go together: the stage a corrupted beast rises from, and how many rise at a time");
            if (def.Bounty.Length > 0 && def.Role != UnitRole.Monster)
                o.Error("only a monster pays a \"bounty\"");
            var hiredFrom = o.Str("hiredFrom", false);
            if (hiredFrom != null && (trainedAt != null || evolvesFrom != null || def.Lair != null || o.Has("trainTime")))
                o.Error("is a mercenary, hired at a city-state's town: it cannot have \"trainedAt\", \"evolvesFrom\", \"lair\" or \"trainTime\"");
            if (hiredFrom != null && def.Cost.Length == 0)
                o.Error("is a mercenary, so it needs a \"cost\" to hire it for");
            if (def.Role == UnitRole.Trader && (def.Damage > 0 || evolvesFrom != null || def.Lair != null || hiredFrom != null))
                o.Error("is a caravan (role trader): it cannot have \"damage\", \"evolvesFrom\", \"lair\" or \"hiredFrom\"");
            var ride = Ride(o);
            _resolve.Add(() =>
            {
                if (ride is { } r)
                {
                    if (!_db.TryGetUnit(r.Rider, out var rider)) o.Error($"\"ride\": \"rider\" is \"{r.Rider}\", which is not a unit");
                    if (!_db.TryGetUnit(r.Becomes, out var becomes)) o.Error($"\"ride\": \"becomes\" is \"{r.Becomes}\", which is not a unit");
                    if (rider == def || becomes == def) o.Error("\"ride\": a monster cannot ride itself or be what its rider becomes");
                    else if (rider != null && rider == becomes) o.Error("\"ride\": the rider has to become another unit");
                    else if (rider != null && becomes != null) def.Ride = new RideDef { Rider = rider, Becomes = becomes };
                }
                if (trainedAt != null)
                {
                    if (_db.TryGetBuilding(trainedAt, out var b)) def.TrainedAt = b;
                    else o.Error($"\"trainedAt\" is \"{trainedAt}\", which is not a building");
                }
                def.Faction = Faction(o, faction);
                def.Requires = Techs(o, "requires", requires);
                if (hiredFrom != null)
                {
                    if (_db.TryGetCityState(hiredFrom, out var city)) def.HiredFrom = city;
                    else o.Error($"\"hiredFrom\" is \"{hiredFrom}\", which is not a city-state");
                }
                if (evolvesFrom == null) return;
                if (!_db.TryGetUnit(evolvesFrom, out var from)) o.Error($"\"evolvesFrom\" is \"{evolvesFrom}\", which is not a unit");
                else if (from == def) o.Error("cannot evolve from itself");
                else def.EvolvesFrom = from;
            });
        }

        private void ReadRelic(Obj o)
        {
            var id = o.Id();
            o.Keys("id", "name", "description", "icon", "hp", "damage", "armor");
            if (id == null) return;
            if (_db.TryGetRelic(id, out _))
            {
                o.Error("another relic already has this id");
                return;
            }
            _db.Add(new RelicDef
            {
                Id = id,
                Name = o.Str("name", true) ?? id,
                Description = o.Str("description", false) ?? "",
                Icon = o.Str("icon", false) ?? id,
                Hp = o.Int("hp", 0, 10000, 0),
                Damage = o.Int("damage", 0, 1000, 0),
                Armour = o.Int("armor", 0, 100, 0),
            });
        }

        private void ReadRuinFind(Obj o)
        {
            var id = o.Id();
            o.Keys("id", "name", "lore", "goods", "relic");
            if (id == null) return;
            foreach (var f in _db.RuinFinds)
                if (f.Id == id)
                {
                    o.Error("another find already has this id");
                    return;
                }
            var goods = o.Cost("goods");
            var relicId = o.Str("relic", false);
            RelicDef? relic = null;
            if (relicId != null && !_db.TryGetRelic(relicId, out relic)) o.Error($"\"relic\" is \"{relicId}\", which is not in relics.json");
            if (goods.Length == 0 && relicId == null) o.Error("needs \"goods\", a \"relic\" or both: what the ruin holds");
            _db.Add(new RuinFindDef
            {
                Id = id,
                Name = o.Str("name", true) ?? id,
                Lore = o.Str("lore", false) ?? "",
                Goods = goods,
                Relic = relic,
            });
        }

        private void ReadCityState(Obj o)
        {
            var id = o.Id();
            o.Keys("id", "name", "description", "totem", "guards", "tribute", "trade", "mercenaries", "heal", "lair", "wall", "roof", "ground", "deep");
            if (id == null) return;
            if (_db.TryGetCityState(id, out _))
            {
                o.Error("another city-state already has this id");
                return;
            }
            var totem = Part(o, "totem", "{ \"building\": \"goblin_totem\", \"respawn\": 5 }");
            totem?.Keys("building", "respawn");
            var guards = Part(o, "guards", "{ \"units\": [\"goblin_cutthroat\"], \"reach\": 4, \"refill\": 45 }");
            guards?.Keys("units", "reach", "refill");
            var tribute = Part(o, "tribute", "{ \"caravan\": \"caravan\", \"good\": \"iron\", \"amount\": 15, \"every\": 60 }");
            tribute?.Keys("caravan", "good", "amount", "every");
            var trade = Part(o, "trade", "{ \"buys\": \"food\", \"load\": 20, \"sells\": \"iron\", \"price\": 5, \"holderBonus\": 50 }");
            trade?.Keys("buys", "load", "sells", "price", "holderBonus");
            var mercenaries = Part(o, "mercenaries", "{ \"pool\": 6, \"refill\": 30 }");
            mercenaries?.Keys("pool", "refill");
            var heal = Part(o, "heal", "{ \"range\": 6 }");
            heal?.Keys("range");
            var buys = trade?.Enum("buys", Good.Food, required: false) ?? Good.Food;
            var sells = trade?.Enum("sells", Good.Iron, required: false) ?? Good.Iron;
            if (buys == sells) trade!.Error($"buys and sells the same good ({SnakeName(buys)})");
            var def = new CityStateDef
            {
                Id = id,
                Name = o.Str("name", true) ?? id,
                Description = o.Str("description", false) ?? "",
                TotemRespawnTicks = (totem?.Int("respawn", 0, MaxSeconds, 5) ?? 5) * Tps,
                GuardReach = guards?.Int("reach", 0, 64, 4) ?? 4,
                GuardRefillTicks = (guards?.Int("refill", 1, MaxSeconds, 45) ?? 45) * Tps,
                TributeGood = tribute?.Enum("good", Good.Iron, required: false) ?? Good.Iron,
                TributeAmount = tribute?.Int("amount", 0, 10000, 15) ?? 0,
                TributeTicks = (tribute?.Int("every", 1, MaxSeconds, 60) ?? 60) * Tps,
                Buys = buys,
                Load = trade?.Int("load", 1, 1000, 20) ?? 20,
                Sells = sells,
                Price = trade?.Int("price", 0, 10000, 5) ?? 5,
                HolderBonus = trade?.Int("holderBonus", 0, 1000, 50) ?? 50,
                Pool = mercenaries?.Int("pool", 0, 100, 6) ?? 6,
                RefillTicks = (mercenaries?.Int("refill", 1, MaxSeconds, 30) ?? 30) * Tps,
                HealRange = heal?.Int("range", 0, 64, 6) ?? -1,
                Wall = o.Material("wall", BlockType.Planks),
                Roof = o.Material("roof", BlockType.Thatch),
                Ground = o.Ground("ground"),
                Deep = o.Bool("deep"),
            };
            var totemId = totem?.Str("building", true);
            var guardIds = guards?.Ids("units") ?? new List<string>();
            var caravanId = tribute?.Str("caravan", true);
            _resolve.Add(() =>
            {
                if (totemId != null)
                {
                    if (!_db.TryGetBuilding(totemId, out var t)) totem!.Error($"\"building\" is \"{totemId}\", which is not a building");
                    else if (t.Kind != BuildingKind.Totem) totem!.Error($"\"building\" is \"{totemId}\", which is not of kind totem");
                    else def.Totem = t;
                }
                var list = new List<UnitDef>();
                foreach (var g in guardIds)
                {
                    if (!_db.TryGetUnit(g, out var u)) guards!.Error($"\"units\" names \"{g}\", which is not a unit");
                    else if (!u.CanAttack || u.IsTrader) guards!.Error($"\"units\" names \"{g}\", which cannot fight");
                    else list.Add(u);
                }
                def.Guards = list.ToArray();
                if (caravanId != null)
                {
                    if (!_db.TryGetUnit(caravanId, out var c)) tribute!.Error($"\"caravan\" is \"{caravanId}\", which is not a unit");
                    else if (!c.IsTrader) tribute!.Error($"\"caravan\" is \"{caravanId}\", which is not a trader");
                    else def.TributeCaravan = c;
                }
            });
            _db.Add(def);
            var lair = o.Str("lair", false);
            if (lair == null) return;
            _resolve.Add(() =>
            {
                if (!_db.TryGetUnit(lair, out var monster)) o.Error($"\"lair\" is \"{lair}\", which is not a unit");
                else if (monster.Lair == null) o.Error($"\"lair\" is \"{lair}\", which keeps no lair (see \"lair\" in units.json)");
                else def.Lair = monster;
            });
        }

        /// <summary>An optional object inside <paramref name="o"/>, or null if it is not given (or is not an object, reported).</summary>
        private Obj? Part(Obj o, string key, string example)
        {
            if (!o.E.TryGetProperty(key, out var e)) return null;
            var part = new Obj(_errors, o.File, key, $"{o.Where}, {key}", e);
            if (e.ValueKind == JsonValueKind.Object) return part;
            part.Error($"should be an object: {example}");
            return null;
        }

        /// <summary>
        /// The role table: the multiplier for attacks a role is strong against, the one for attacks on
        /// buildings, how fast units out of the fight heal, and a row per role saying what it is strong and
        /// weak against.
        /// </summary>
        private void ReadRoles(string file)
        {
            if (Parse(file) is not { } doc) return;
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                _errors.Add($"{file}: should be an object: {{ \"strong\": 2, \"structures\": 0.25, \"roles\": [ ... ] }}");
                return;
            }
            var table = new Obj(_errors, file, "table", "the table", doc.RootElement);
            table.Keys("strong", "structures", "thick", "recovery", "bleeding", "roles");
            var combat = _db.Combat;
            combat.Strong = table.Decimal("strong", Fix64.One, Fix64.FromInt(10), Fix64.FromInt(2));
            combat.Structures = table.Decimal("structures", Fix64.Zero, Fix64.One, Fix64.FromRatio(1, 4));
            combat.Thick = table.Decimal("thick", Fix64.Zero, Fix64.One, Fix64.One, required: false);
            if (Part(table, "recovery", "{ \"after\": 10, \"percent\": 0.25, \"homePercent\": 2, \"homeRange\": 12 }") is { } recovery)
            {
                recovery.Keys("after", "percent", "homePercent", "homeRange");
                combat.RecoveryDelayTicks = recovery.Int("after", 0, MaxSeconds, 10) * Tps;
                combat.RecoveryPercent = recovery.Decimal("percent", Fix64.Zero, Fix64.FromInt(100), Fix64.FromRatio(1, 4), required: false);
                combat.HomeRecoveryPercent = recovery.Decimal("homePercent", Fix64.Zero, Fix64.FromInt(100), Fix64.FromInt(2), required: false);
                combat.HomeRange = recovery.Int("homeRange", 0, 512, 12);
            }
            if (Part(table, "bleeding", "{ \"share\": 40, \"rate\": 1, \"kills\": true, \"bloodDays\": 1 }") is { } bleeding)
            {
                bleeding.Keys("share", "rate", "kills", "bloodDays");
                combat.BleedShare = bleeding.Int("share", 0, 1000, 40);
                combat.BleedRate = bleeding.Int("rate", 1, 100, 1);
                combat.BleedKills = !bleeding.Has("kills") || bleeding.Bool("kills");
                combat.BloodDays = bleeding.Decimal("bloodDays", Fix64.FromRatio(1, 20), Fix64.FromInt(30), Fix64.One, required: false);
            }
            if (!doc.RootElement.TryGetProperty("roles", out var list) || list.ValueKind != JsonValueKind.Array)
            {
                table.Error("needs \"roles\": a list of rows: [ { \"role\": ... }, ... ]");
                return;
            }
            var seen = new bool[CombatRules.RoleCount];
            int n = 0;
            foreach (var e in list.EnumerateArray())
            {
                n++;
                var row = new Obj(_errors, file, "role", $"role {n}", e);
                if (e.ValueKind != JsonValueKind.Object)
                {
                    row.Error("should be an object: { \"role\": ... }");
                    continue;
                }
                var name = row.Str("role", true);
                if (name == null) continue;
                if (!TryEnum<UnitRole>(name, out var role))
                {
                    row.Error($"\"role\" is \"{name}\", which is not one of: {Names<UnitRole>()}");
                    continue;
                }
                var o = new Obj(_errors, file, "role", $"role '{name}'", e);
                o.Keys("role", "job", "strongAgainst", "weakAgainst");
                if (seen[(int)role])
                {
                    o.Error("is listed twice");
                    continue;
                }
                seen[(int)role] = true;
                combat.SetJob(role, o.Str("job", false) ?? "");
                var strong = o.Ids("strongAgainst");
                var weak = o.Ids("weakAgainst");
                foreach (var other in Roles(o, "strongAgainst", strong, allowStructures: true))
                {
                    if (other is { } r) combat.SetStrong(role, r);
                    else combat.SetStrongAgainstStructures(role);
                }
                foreach (var other in Roles(o, "weakAgainst", weak, allowStructures: false))
                {
                    if (other is not { } r) continue;
                    if (strong.Contains(SnakeName(r))) o.Error($"is both strong and weak against {SnakeName(r)}");
                    combat.SetStrong(r, role);
                }
            }
            foreach (var role in Enum.GetValues<UnitRole>())
                if (!seen[(int)role])
                    _errors.Add($"{file}: role \"{SnakeName(role)}\" is missing; every role needs a row, even with empty lists");
        }

        /// <summary>The roles a list names (null for "structures" where allowed), reporting unknown names and repeats.</summary>
        private static List<UnitRole?> Roles(Obj o, string key, List<string> names, bool allowStructures)
        {
            var roles = new List<UnitRole?>();
            var seen = new List<string>();
            foreach (var name in names)
            {
                if (seen.Contains(name))
                {
                    o.Error($"\"{key}\" names \"{name}\" twice");
                    continue;
                }
                seen.Add(name);
                if (allowStructures && name == "structures") roles.Add(null);
                else if (TryEnum<UnitRole>(name, out var r)) roles.Add(r);
                else o.Error($"\"{key}\" names \"{name}\", which is not a role (roles: {Names<UnitRole>()}{(allowStructures ? ", or structures" : "")})");
            }
            return roles;
        }

        // --- References --------------------------------------------------------------------------

        private FactionDef? Faction(Obj o, string? id)
        {
            if (id == null) return null;
            if (_db.TryGetFaction(id, out var f)) return f;
            o.Error($"\"faction\" is \"{id}\", which is not a faction (factions: {FactionNames()})");
            return null;
        }

        private string FactionNames()
        {
            var ids = new List<string>();
            foreach (var f in _db.Factions) ids.Add(f.Id);
            return string.Join(", ", ids);
        }

        private TechDef[] Techs(Obj o, string key, List<string> ids)
        {
            var list = new List<TechDef>();
            foreach (var id in ids)
            {
                if (!_db.TryGetTech(id, out var t)) o.Error($"\"{key}\" names \"{id}\", which is not a tech");
                else if (list.Contains(t)) o.Error($"\"{key}\" names \"{id}\" twice");
                else list.Add(t);
            }
            return list.ToArray();
        }

        private TechEffect[] Effects(Obj tech)
        {
            var effects = new List<TechEffect>();
            if (!tech.E.TryGetProperty("effects", out var list)) return effects.ToArray();
            if (list.ValueKind != JsonValueKind.Array)
            {
                tech.Error("\"effects\" should be a list: [ { \"type\": ... } ]");
                return effects.ToArray();
            }
            int n = 0;
            foreach (var item in list.EnumerateArray())
            {
                n++;
                var o = new Obj(_errors, tech.File, "effect", $"{tech.Where}, effect {n}", item);
                if (item.ValueKind != JsonValueKind.Object)
                {
                    o.Error("should be an object: { \"type\": ... }");
                    continue;
                }
                var typeName = o.Str("type", true);
                if (typeName == null) continue;
                if (!TryEnum<TechEffectType>(typeName, out var type))
                {
                    o.Error($"\"type\" is \"{typeName}\", which is not an effect (effects: {Names<TechEffectType>()})");
                    continue;
                }
                switch (type)
                {
                    case TechEffectType.GatherSpeed:
                        o.Keys("type", "percent");
                        effects.Add(new TechEffect { Type = type, Amount = o.Int("percent", 1, 1000, 0, required: true) });
                        break;
                    case TechEffectType.Carry:
                    case TechEffectType.FarmYield:
                        o.Keys("type", "amount");
                        effects.Add(new TechEffect { Type = type, Amount = o.Int("amount", 1, 100, 0, required: true) });
                        break;
                    case TechEffectType.Housing:
                    {
                        o.Keys("type", "building", "amount");
                        var id = o.Str("building", true);
                        BuildingDef? building = null;
                        if (id != null && !_db.TryGetBuilding(id, out building))
                            o.Error($"\"building\" is \"{id}\", which is not a building");
                        effects.Add(new TechEffect { Type = type, Building = building, Amount = o.Int("amount", 1, 1000, 0, required: true) });
                        break;
                    }
                    case TechEffectType.UnitHp:
                    case TechEffectType.UnitSpeed:
                    {
                        string amountKey = type == TechEffectType.UnitHp ? "amount" : "percent";
                        o.Keys("type", "role", "unit", amountKey);
                        UnitRole? role = o.Has("role") ? o.Enum("role", UnitRole.Worker, required: true) : null;
                        var unitId = o.Str("unit", false);
                        UnitDef? unit = null;
                        if (unitId != null && !_db.TryGetUnit(unitId, out unit))
                            o.Error($"\"unit\" is \"{unitId}\", which is not a unit");
                        if (role != null && unitId != null) o.Error("give a \"role\" or a \"unit\", not both");
                        int amount = type == TechEffectType.UnitHp ? o.Int("amount", 1, 100000, 0, required: true)
                            : o.Int("percent", 1, 1000, 0, required: true);
                        effects.Add(new TechEffect { Type = type, Role = role, Unit = unit, Amount = amount });
                        break;
                    }
                    case TechEffectType.Roads:
                    case TechEffectType.SpareWorkers:
                        o.Keys("type");
                        effects.Add(new TechEffect { Type = type });
                        break;
                    case TechEffectType.Reveal:
                        o.Keys("type", "what");
                        effects.Add(new TechEffect { Type = type, Reveal = o.Enum("what", MapFeature.Ruins, required: true) });
                        break;
                }
            }
            return effects.ToArray();
        }

        // --- Whole-data checks -------------------------------------------------------------------

        /// <summary>Reports prerequisite cycles (each once, as a path); if there are none, sets each tech's depth.</summary>
        private void CheckTree()
        {
            var techs = _db.Techs;
            var state = new byte[techs.Count]; // 0 not seen, 1 on the current path, 2 done
            var path = new List<TechDef>();
            bool cycles = false;

            void Visit(TechDef t)
            {
                state[t.Index] = 1;
                path.Add(t);
                foreach (var r in t.Requires)
                {
                    if (state[r.Index] == 1)
                    {
                        var ids = new List<string>();
                        for (int k = path.IndexOf(r); k < path.Count; k++) ids.Add(path[k].Id);
                        ids.Add(r.Id);
                        _errors.Add($"techs.json: prerequisite cycle, each needs the next: {string.Join(" -> ", ids)}");
                        cycles = true;
                    }
                    else if (state[r.Index] == 0)
                    {
                        Visit(r);
                    }
                }
                path.RemoveAt(path.Count - 1);
                state[t.Index] = 2;
            }

            foreach (var t in techs)
                if (state[t.Index] == 0) Visit(t);
            if (cycles) return;

            var depth = new int[techs.Count];
            Array.Fill(depth, -1);
            int Depth(TechDef t)
            {
                if (depth[t.Index] >= 0) return depth[t.Index];
                int d = 0;
                foreach (var r in t.Requires) d = Math.Max(d, Depth(r) + 1);
                return depth[t.Index] = d;
            }
            foreach (var t in techs) t.Depth = Depth(t);
        }

        /// <summary>Reports techs, units and buildings that need a tech only another faction can research.</summary>
        private void CheckFactions()
        {
            foreach (var t in _db.Techs)
                CheckNeeds("techs.json", $"tech '{t.Id}'", t.Faction, t.Requires);
            foreach (var b in _db.Buildings)
                CheckNeeds("buildings.json", $"building '{b.Id}'", b.Faction, b.Requires);
            foreach (var u in _db.Units)
            {
                CheckNeeds("units.json", $"unit '{u.Id}'", u.Faction, u.Requires);
                if (u.TrainedAt?.Faction is { } f && u.Faction != f)
                    _errors.Add($"units.json: unit '{u.Id}' is trained at a {u.TrainedAt.Id}, which only {f.Id} can build");
            }
        }

        private void CheckNeeds(string file, string what, FactionDef? faction, IReadOnlyList<TechDef> requires)
        {
            foreach (var r in requires)
            {
                if (r.Faction == null || r.Faction == faction) continue;
                string who = faction == null ? "every faction" : $"only {faction.Id}";
                _errors.Add($"{file}: {what} is for {who} but needs \"{r.Id}\", which only {r.Faction.Id} can research");
            }
        }

        /// <summary>
        /// Evolution forms: no loops (each reported once, as a path); a form of one faction's unit is that
        /// faction's too; and some faction whose units earn experience has a level for each step of the way.
        /// Sets each unit's tier if there are no loops.
        /// </summary>
        private void CheckForms()
        {
            var units = _db.Units;
            bool loops = false;
            foreach (var u in units)
            {
                var path = new List<UnitDef>();
                UnitDef? f = u;
                while (f != null && !path.Contains(f))
                {
                    path.Add(f);
                    f = f.EvolvesFrom;
                }
                // Report a loop from its first unit in data order, which is on it.
                if (f != u || path.Exists(p => p.Index < u.Index)) continue;
                var ids = path.ConvertAll(p => p.Id);
                ids.Add(u.Id);
                _errors.Add($"units.json: evolution loop, each evolves from the next: {string.Join(" -> ", ids)}");
                loops = true;
            }
            if (loops) return;

            int mostLevels = -1;
            foreach (var faction in _db.Factions)
                if (faction.Experience is { } x) mostLevels = Math.Max(mostLevels, x.Levels.Count);
            foreach (var u in units)
            {
                int tier = 0;
                for (var f = u.EvolvesFrom; f != null; f = f.EvolvesFrom) tier++;
                u.Tier = tier;
                if (u.EvolvesFrom is not { } from) continue;
                string what = $"units.json: unit '{u.Id}'";
                if (from.Faction != null && u.Faction != null && u.Faction != from.Faction)
                    _errors.Add($"{what} is for {u.Faction.Id} but evolves from '{from.Id}', which only {from.Faction.Id} have");
                var faction = u.Faction ?? from.Faction;
                if (faction != null && faction.Experience == null)
                    _errors.Add($"{what} evolves from '{from.Id}', but {faction.Id} units do not earn experience (no \"experience\" in factions.json)");
                else if (faction == null && mostLevels < 0)
                    _errors.Add($"{what} evolves from '{from.Id}', but no faction's units earn experience (no \"experience\" in factions.json)");
                else if ((faction?.Experience?.Levels.Count ?? mostLevels) is var levels && levels < tier)
                    _errors.Add($"{what} is {tier} evolutions from a trained unit, so it needs level {tier}, but "
                                + (faction != null ? $"{faction.Id} units have only {levels}" : $"no faction's units have more than {levels}"));
            }
        }

        /// <summary>
        /// Sets each building's tier (how many upgrades it is from one that is placed), if no building is an
        /// upgrade of itself by way of others.
        /// </summary>
        private void CheckUpgrades()
        {
            foreach (var b in _db.Buildings)
            {
                int tier = 0;
                var from = b.UpgradeOf;
                while (from != null && from != b && tier <= _db.Buildings.Count)
                {
                    tier++;
                    from = from.UpgradeOf;
                }
                if (from == null) b.Tier = tier;
                else if (from == b)
                    _errors.Add($"buildings.json: building '{b.Id}' is its own upgrade, by way of '{b.UpgradeOf!.Id}'");
                // A facing goes up beside a wall when that wall's upgrade is finished, and is only ever that.
                if (b.Facing != null && b.UpgradeOf == null)
                    _errors.Add($"buildings.json: building '{b.Id}' has a \"facing\", so it must be the upgrade of another (it is faced when the upgrade is finished)");
                if (b.FacingOf != null && (b.Upgrade != null || b.UpgradeOf != null || b.Facing != null))
                    _errors.Add($"buildings.json: building '{b.Id}' is the facing of '{b.FacingOf.Id}', so it can have no \"upgrade\" or \"facing\" and be no upgrade itself");
            }
            // A facing counts as of the tier of the wall it faces.
            foreach (var b in _db.Buildings)
                if (b.FacingOf != null) b.Tier = b.FacingOf.Tier;
        }

        /// <summary>The content the game itself relies on.</summary>
        private void CheckRequired()
        {
            if (_db.Factions.Count == 0) _errors.Add("factions.json: there must be at least one faction");
            if (!_db.TryGetBuilding("town_hall", out var hall) || hall.Kind != BuildingKind.TownHall)
                _errors.Add("buildings.json: there must be a building \"town_hall\" of kind town_hall (every player starts with one)");
            else if (hall.Requires.Count > 0 || hall.Faction != null)
                _errors.Add("buildings.json: every faction starts with a town_hall, so it cannot need techs or belong to one faction");
            if (!_db.TryGetUnit("worker", out var worker) || worker.Role != UnitRole.Worker)
                _errors.Add("units.json: there must be a unit \"worker\" with role worker");
        }

        /// <summary>Derived facts: what each building trains, the units every match starts with, each city-state's mercenaries, whether roads need a tech, and the hash.</summary>
        private void Finish()
        {
            foreach (var b in _db.Buildings)
            {
                var trains = new List<string>();
                // An upgrade trains what the building it was upgraded from trains.
                var root = b;
                for (int k = 0; root.UpgradeOf != null && k < _db.Buildings.Count; k++) root = root.UpgradeOf;
                foreach (var u in _db.Units)
                    if (u.TrainedAt == root) trains.Add(u.Id);
                b.Trains = trains.ToArray();
            }
            var bases = new List<UnitDef>();
            var monsters = new List<UnitDef>();
            var wild = new List<UnitDef>();
            var cavern = new List<UnitDef>();
            foreach (var u in _db.Units)
            {
                var forms = new List<UnitDef>();
                foreach (var f in _db.Units)
                    if (f.EvolvesFrom == u && !f.FromDen) forms.Add(f);
                u.Forms = forms.ToArray();
                if (u.TrainedAt != null && !u.IsTrader) bases.Add(u);
                if (u.Lair is { DuskTree: false, AtStarts: true }) monsters.Add(u);
                if (u.Lair is { DuskTree: false, Count: > 0, Cavern: CavernSpot.None }) wild.Add(u);
                if (u.Lair is { DuskTree: false, Count: > 0, Cavern: not CavernSpot.None }) cavern.Add(u);
                if (u.Lair is { DuskTree: true }) _db.DuskWarden = u;
            }
            _db.BaseUnits = bases.ToArray();
            _db.StartUnits = bases.FindAll(u => u.InStart).ToArray();
            _db.LairMonsters = monsters.ToArray();
            _db.WildMonsters = wild.ToArray();
            _db.CavernMonsters = cavern.ToArray();
            foreach (var c in _db.CityStates)
            {
                var hired = new List<UnitDef>();
                foreach (var u in _db.Units)
                    if (u.HiredFrom == c) hired.Add(u);
                c.Mercenaries = hired.ToArray();
            }
            foreach (var t in _db.Techs)
            foreach (var e in t.Effects)
                if (e.Type == TechEffectType.Roads) _db.RoadsNeedTech = true;
            _db.Hash = Hash(_db);
        }

        private static ulong Hash(ContentDb db)
        {
            var h = new Hasher();
            h.Add(db.Factions.Count);
            foreach (var f in db.Factions)
            {
                h.Add(f.Id);
                h.Add(f.TamesMonsters ? 1 : 0);
                h.Add((int)f.DuskTree);
                h.Add((int)f.AiStyle);
                if (f.Experience is not { } x)
                {
                    h.Add(-1);
                    continue;
                }
                h.Add(x.Levels.Count);
                foreach (int level in x.Levels) h.Add(level);
                h.Add(x.PerDamage);
                h.Add(x.PerKill);
                h.Add(x.PerBuildSecond);
                h.Add(x.PerMinute);
            }
            h.Add(db.Techs.Count);
            foreach (var t in db.Techs)
            {
                h.Add(t.Id);
                h.Add(t.Faction?.Index ?? -1);
                h.Add(t.Cost);
                h.Add(t.ResearchTicks);
                h.Add((int)t.Discovery);
                h.Add(t.Requires);
                h.Add(t.Effects.Count);
                foreach (var e in t.Effects)
                {
                    h.Add((int)e.Type);
                    h.Add(e.Amount);
                    h.Add(e.Building?.Index ?? -1);
                    h.Add(e.Role is { } role ? (int)role : -1);
                    h.Add(e.Unit?.Index ?? -1);
                    h.Add((int)e.Reveal);
                }
            }
            h.Add(db.Buildings.Count);
            foreach (var b in db.Buildings)
            {
                h.Add(b.Id);
                h.Add((int)b.Kind);
                h.Add(b.SizeX);
                h.Add(b.SizeZ);
                h.Add(b.WallHeight);
                h.Add((int)b.Wall);
                h.Add((int)b.Footing);
                h.Add((int)b.Roof);
                h.Add(b.Chimney ? 1 : 0);
                h.Add(b.Cost);
                h.Add(b.BuildTicks);
                h.Add(b.Housing);
                h.Add(b.Storage ? 1 : 0);
                h.Add(b.Raidable ? 1 : 0);
                h.Add(b.WorkerSlots);
                h.Add(b.Sight);
                h.Add(b.Light);
                h.Add(b.FromDen ? 1 : 0);
                h.Add(b.Yield);
                h.Add(b.YieldTicks);
                h.Add(b.Requires);
                h.Add(b.Faction?.Index ?? -1);
                h.Add(b.Sturdy);
                h.Add(b.Thick ? 1 : 0);
                h.Add(b.Walkway ? 1 : 0);
                h.Add(b.Speed);
                h.Add(b.Upgrade?.Index ?? -1);
                h.Add(b.Facing?.Index ?? -1);
                h.Add(b.Aim);
                h.Add(b.Works);
                h.Add(b.WorkTicks);
                h.Add(b.Shore ? 1 : 0);
                h.Add(b.Plants);
                h.Add(b.Mana);
                h.Add(b.ChannelTicks);
            }
            h.Add(db.Units.Count);
            foreach (var u in db.Units)
            {
                h.Add(u.Id);
                h.Add((int)u.Role);
                h.Add(u.Speed.Raw);
                h.Add(u.MaxHp);
                h.Add(u.Cost);
                h.Add(u.TrainTicks);
                h.Add(u.TrainedAt?.Index ?? -1);
                h.Add(u.Requires);
                h.Add(u.Faction?.Index ?? -1);
                h.Add(u.Damage);
                h.Add(u.AttackTicks);
                h.Add(u.Range.Raw);
                h.Add(u.Sight.Raw);
                h.Add(u.Splash.Raw);
                foreach (var r in Enum.GetValues<UnitRole>()) h.Add(u.WeaknessTo(r));
                h.Add((int)u.Attack);
                h.Add((int)u.Body);
                foreach (int a in u.Armour) h.Add(a);
                h.Add(u.Shield);
                h.Add(u.Bleeds ? 1 : 0);
                h.Add(u.TracksBlood ? 1 : 0);
                h.Add(u.Blast ? 1 : 0);
                h.Add(u.EarthWall ? 1 : 0);
                h.Add(u.IceBridge ? 1 : 0);
                h.Add(u.Chasm ? 1 : 0);
                h.Add(u.GrowForest ? 1 : 0);
                h.Add(u.Heal);
                h.Add(u.HealTicks);
                h.Add(u.HealRange.Raw);
                h.Add(u.Regen);
                h.Add(u.Hero ? 1 : 0);
                h.Add(u.Rally.Raw);
                h.Add(u.Steady);
                h.Add(u.Inspire);
                h.Add(u.Volley);
                h.Add(u.Spur);
                h.Add(u.Undermine);
                h.Add(u.Quicken);
                h.Add(u.Hasten);
                h.Add(u.WorkPercent);
                h.Add(u.EvolvesFrom?.Index ?? -1);
                h.Add(u.FromDen ? 1 : 0);
                h.Add(u.InStart ? 1 : 0);
                if (u.Awakening > 0 || u.Bounty.Length > 0)
                {
                    h.Add(u.Awakening);
                    h.Add(u.Brood);
                    h.Add(u.Bounty);
                }
                if (u.Lair is { } lair)
                {
                    h.Add(lair.Pack);
                    h.Add(lair.Guard);
                    h.Add(lair.Leash);
                    h.Add(lair.Reward);
                    h.Add(lair.DuskTree ? 1 : 0);
                    h.Add(lair.Biome is { } b ? (int)b : -1);
                    h.Add(lair.Count);
                    h.Add(lair.AtStarts ? 1 : 0);
                    h.Add((int)lair.Cavern);
                    h.Add(lair.Roam);
                    h.Add(lair.Prizes.Count);
                    foreach (var p in lair.Prizes)
                    {
                        h.Add(p.Faction.Index);
                        h.Add(p.Use.Building?.Index ?? -1);
                        h.Add(p.Use.Form?.Index ?? -1);
                        h.Add(p.Use.Relic?.Index ?? -1);
                    }
                }
                else
                {
                    h.Add(-1);
                }
                h.Add(u.Ride?.Rider.Index ?? -1);
                h.Add(u.Ride?.Becomes.Index ?? -1);
                h.Add(u.HiredFrom?.Index ?? -1);
            }
            h.Add(db.CityStates.Count);
            foreach (var c in db.CityStates)
            {
                h.Add(c.Id);
                h.Add(c.Totem?.Index ?? -1);
                h.Add(c.TotemRespawnTicks);
                h.Add(c.Guards.Count);
                foreach (var g in c.Guards) h.Add(g.Index);
                h.Add(c.GuardReach);
                h.Add(c.GuardRefillTicks);
                h.Add((int)c.TributeGood);
                h.Add(c.TributeAmount);
                h.Add(c.TributeTicks);
                h.Add(c.TributeCaravan?.Index ?? -1);
                h.Add((int)c.Buys);
                h.Add(c.Load);
                h.Add((int)c.Sells);
                h.Add(c.Price);
                h.Add(c.HolderBonus);
                h.Add(c.Pool);
                h.Add(c.RefillTicks);
                h.Add(c.HealRange);
                h.Add(c.Lair?.Index ?? -1);
                h.Add((int)c.Ground);
                h.Add(c.Deep ? 1 : 0);
                h.Add((int)c.Wall);
                h.Add((int)c.Roof);
            }
            var combat = db.Combat;
            h.Add(combat.Strong.Raw);
            h.Add(combat.Structures.Raw);
            h.Add(combat.Thick.Raw);
            h.Add(combat.RecoveryDelayTicks);
            h.Add(combat.RecoveryPercent.Raw);
            h.Add(combat.HomeRecoveryPercent.Raw);
            h.Add(combat.HomeRange);
            h.Add(combat.BleedShare);
            h.Add(combat.BleedRate);
            h.Add(combat.BleedKills ? 1 : 0);
            h.Add(combat.BloodDays.Raw);
            foreach (var a in Enum.GetValues<UnitRole>())
            {
                h.Add(combat.StrongAgainstStructures(a) ? 1 : 0);
                foreach (var t in Enum.GetValues<UnitRole>()) h.Add(combat.StrongAgainst(a, t) ? 1 : 0);
            }
            h.Add(db.Relics.Count);
            foreach (var r in db.Relics)
            {
                h.Add(r.Id);
                h.Add(r.Hp);
                h.Add(r.Damage);
                h.Add(r.Armour);
            }
            h.Add(db.RuinFinds.Count);
            foreach (var f in db.RuinFinds)
            {
                h.Add(f.Id);
                h.Add(f.Relic?.Index ?? -1);
                h.Add(f.Goods);
            }
            return h.Value;
        }
    }

    /// <summary>FNV-1a over a sequence of numbers and strings.</summary>
    private sealed class Hasher
    {
        public ulong Value { get; private set; } = 14695981039346656037UL;

        public void Add(long v) => Value = (Value ^ (ulong)v) * 1099511628211UL;

        public void Add(string s)
        {
            Add(s.Length);
            foreach (char c in s) Add(c);
        }

        public void Add(GoodAmount[] cost)
        {
            Add(cost.Length);
            foreach (var c in cost)
            {
                Add((int)c.Good);
                Add(c.Amount);
            }
        }

        public void Add(IReadOnlyList<TechDef> techs)
        {
            Add(techs.Count);
            foreach (var t in techs) Add(t.Index);
        }
    }

    /// <summary>One JSON object being read: typed getters that report problems instead of throwing.</summary>
    private sealed class Obj
    {
        private readonly List<string> _errors;
        private readonly string _what;

        public Obj(List<string> errors, string file, string what, string where, JsonElement e)
        {
            _errors = errors;
            File = file;
            _what = what;
            Where = where;
            E = e;
        }

        public string File { get; }
        /// <summary>What the object is, for messages: "unit 3" until its id is known, then "unit 'archer'".</summary>
        public string Where { get; private set; }
        public JsonElement E { get; }

        public void Error(string message) => _errors.Add($"{File}: {Where}: {message}");

        public bool Has(string key) => E.TryGetProperty(key, out _);

        /// <summary>Reports fields other than these, and fields given twice.</summary>
        public void Keys(params string[] allowed)
        {
            var seen = new List<string>();
            foreach (var p in E.EnumerateObject())
            {
                if (Array.IndexOf(allowed, p.Name) < 0) Error($"unknown field \"{p.Name}\" (fields: {string.Join(", ", allowed)})");
                else if (seen.Contains(p.Name)) Error($"\"{p.Name}\" is given twice");
                else seen.Add(p.Name);
            }
        }

        /// <summary>The object's id, checked; null (and reported) if it is missing or malformed.</summary>
        public string? Id()
        {
            var id = Str("id", true);
            if (id == null) return null;
            if (!IsId(id))
            {
                Error($"id \"{id}\" should be lower case letters, digits and underscores, starting with a letter");
                return null;
            }
            Where = $"{_what} '{id}'";
            return id;
        }

        public string? Str(string key, bool required)
        {
            if (!E.TryGetProperty(key, out var v))
            {
                if (required) Error($"needs \"{key}\"");
                return null;
            }
            if (v.ValueKind != JsonValueKind.String)
            {
                Error($"\"{key}\" should be text in quotes");
                return null;
            }
            return v.GetString();
        }

        public int Int(string key, int min, int max, int fallback, bool required = false)
        {
            if (!E.TryGetProperty(key, out var v))
            {
                if (required) Error($"needs \"{key}\"");
                return fallback;
            }
            if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out int n))
            {
                Error($"\"{key}\" should be a whole number");
                return fallback;
            }
            if (n < min || n > max)
            {
                Error($"\"{key}\" is {n}; it should be from {min} to {max}");
                return fallback;
            }
            return n;
        }

        /// <summary>A number that may have decimals, read exactly as written.</summary>
        public Fix64 Decimal(string key, Fix64 min, Fix64 max, Fix64 fallback, bool required = true)
        {
            if (!E.TryGetProperty(key, out var v))
            {
                if (required) Error($"needs \"{key}\"");
                return fallback;
            }
            if (v.ValueKind != JsonValueKind.Number || !Fix64.TryParse(v.GetRawText(), out var f))
            {
                Error($"\"{key}\" should be a number such as 4 or 4.5");
                return fallback;
            }
            if (f < min || f > max)
            {
                Error($"\"{key}\" is {v.GetRawText()}; it should be from {min} to {max}");
                return fallback;
            }
            return f;
        }

        public bool Bool(string key)
        {
            if (!E.TryGetProperty(key, out var v)) return false;
            if (v.ValueKind is JsonValueKind.True or JsonValueKind.False) return v.GetBoolean();
            Error($"\"{key}\" should be true or false");
            return false;
        }

        public T Enum<T>(string key, T fallback, bool required) where T : struct, System.Enum
        {
            var text = Str(key, required);
            if (text == null) return fallback;
            if (TryEnum<T>(text, out var value)) return value;
            Error($"\"{key}\" is \"{text}\", which is not one of: {Names<T>()}");
            return fallback;
        }

        /// <summary>A building material; <paramref name="fallback"/> if the field is not given.</summary>
        public BlockType Material(string key, BlockType fallback)
        {
            var text = Str(key, false);
            if (text == null) return fallback;
            if (TryEnum<BlockType>(text, out var block) && Array.IndexOf(Materials, block) >= 0) return block;
            var names = new List<string>();
            foreach (var m in Materials) names.Add(SnakeName(m));
            Error($"\"{key}\" is \"{text}\", which is not a building material ({string.Join(", ", names)})");
            return fallback;
        }

        /// <summary>What a town square is paved with: grass (the fallback), stone or stone_brick.</summary>
        public BlockType Ground(string key)
        {
            var text = Str(key, false);
            if (text == null) return BlockType.Grass;
            if (TryEnum<BlockType>(text, out var block) && block is BlockType.Grass or BlockType.Stone or BlockType.StoneBrick) return block;
            Error($"\"{key}\" is \"{text}\"; it should be grass, stone or stone_brick");
            return BlockType.Grass;
        }

        /// <summary>A footprint, [x, z] in blocks.</summary>
        public (int X, int Z) Size(string key)
        {
            const int max = 15;
            if (!E.TryGetProperty(key, out var v))
            {
                Error($"needs \"{key}\": [x, z]");
                return (4, 4);
            }
            if (v.ValueKind == JsonValueKind.Array && v.GetArrayLength() == 2
                && v[0].TryGetInt32Safe(out int x) && v[1].TryGetInt32Safe(out int z) && x is >= 1 and <= max && z is >= 1 and <= max)
                return (x, z);
            Error($"\"{key}\" should be [x, z] with each a whole number from 1 to {max}");
            return (4, 4);
        }

        /// <summary>Goods and amounts, as in <c>{ "wood": 30, "stone": 10 }</c>, in the order written.</summary>
        public GoodAmount[] Cost(string key)
        {
            if (!E.TryGetProperty(key, out var v)) return Array.Empty<GoodAmount>();
            if (v.ValueKind != JsonValueKind.Object)
            {
                Error($"\"{key}\" should be goods and amounts, such as {{ \"wood\": 30 }}");
                return Array.Empty<GoodAmount>();
            }
            var list = new List<GoodAmount>();
            foreach (var p in v.EnumerateObject())
            {
                if (!TryEnum<Good>(p.Name, out var good))
                {
                    Error($"\"{key}\" has \"{p.Name}\", which is not a good (goods: {Names<Good>()})");
                    continue;
                }
                if (!p.Value.TryGetInt32Safe(out int n) || n < 1 || n > 100000)
                {
                    Error($"\"{key}\": {p.Name} should be a whole number from 1 to 100000");
                    continue;
                }
                if (list.Exists(g => g.Good == good))
                {
                    Error($"\"{key}\" has {p.Name} twice");
                    continue;
                }
                list.Add(new GoodAmount(good, n));
            }
            return list.ToArray();
        }

        /// <summary>A list of whole numbers, as in <c>[ 150, 400 ]</c>; empty (and reported) if it is malformed.</summary>
        public List<int> IntList(string key, int min, int max)
        {
            var list = new List<int>();
            if (!E.TryGetProperty(key, out var v)) return list;
            if (v.ValueKind != JsonValueKind.Array)
            {
                Error($"\"{key}\" should be a list of whole numbers, such as [ 150, 400 ]");
                return list;
            }
            foreach (var item in v.EnumerateArray())
            {
                if (item.TryGetInt32Safe(out int n) && n >= min && n <= max)
                {
                    list.Add(n);
                    continue;
                }
                Error($"\"{key}\" should list whole numbers from {min} to {max}");
                return new List<int>();
            }
            return list;
        }

        /// <summary>
        /// Roles and percentages, as in <c>{ "caster": 50 }</c>, indexed by <see cref="UnitRole"/>; null if the
        /// field is missing or names no role.
        /// </summary>
        public int[]? RolePercents(string key, int min, int max)
        {
            if (!E.TryGetProperty(key, out var v)) return null;
            if (v.ValueKind != JsonValueKind.Object)
            {
                Error($"\"{key}\" should be roles and percentages, such as {{ \"caster\": 50 }}");
                return null;
            }
            var percents = new int[CombatRules.RoleCount];
            bool any = false;
            foreach (var p in v.EnumerateObject())
            {
                if (!TryEnum<UnitRole>(p.Name, out var role))
                {
                    Error($"\"{key}\" has \"{p.Name}\", which is not a role (roles: {Names<UnitRole>()})");
                    continue;
                }
                if (!p.Value.TryGetInt32Safe(out int n) || n < min || n > max)
                {
                    Error($"\"{key}\": {p.Name} should be a whole number from {min} to {max}");
                    continue;
                }
                if (percents[(int)role] != 0)
                {
                    Error($"\"{key}\" has {p.Name} twice");
                    continue;
                }
                percents[(int)role] = n;
                any = true;
            }
            return any ? percents : null;
        }

        /// <summary>One id or a list of ids.</summary>
        public List<string> Ids(string key)
        {
            var list = new List<string>();
            if (!E.TryGetProperty(key, out var v)) return list;
            if (v.ValueKind == JsonValueKind.String)
            {
                list.Add(v.GetString()!);
            }
            else if (v.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in v.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String) list.Add(item.GetString()!);
                    else Error($"\"{key}\" should list ids in quotes");
                }
            }
            else
            {
                Error($"\"{key}\" should be an id, or a list of ids: [ \"a\", \"b\" ]");
            }
            return list;
        }
    }
}

internal static class JsonElementExtensions
{
    /// <summary>TryGetInt32 that is false (rather than throwing) for values that are not numbers.</summary>
    public static bool TryGetInt32Safe(this JsonElement e, out int value)
    {
        value = 0;
        return e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out value);
    }
}
