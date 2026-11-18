using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.Content;

/// <summary>
/// All content definitions (factions, techs, buildings, units and city-states, and the combat role table), each list in
/// data file order, which gives every definition its stable index. Loaded from the JSON files in <c>data/</c> by
/// <see cref="ContentLoader"/>, which checks them first; read-only once loaded.
/// </summary>
public sealed class ContentDb
{
    private static readonly Lazy<ContentDb> s_default = new(ContentLoader.LoadEmbedded);

    private readonly List<UnitDef> _units = new();
    private readonly Dictionary<string, UnitDef> _unitsById = new(StringComparer.Ordinal);
    private readonly List<BuildingDef> _buildings = new();
    private readonly Dictionary<string, BuildingDef> _buildingsById = new(StringComparer.Ordinal);
    private readonly List<TechDef> _techs = new();
    private readonly Dictionary<string, TechDef> _techsById = new(StringComparer.Ordinal);
    private readonly List<FactionDef> _factions = new();
    private readonly Dictionary<string, FactionDef> _factionsById = new(StringComparer.Ordinal);
    private readonly List<CityStateDef> _cityStates = new();
    private readonly Dictionary<string, CityStateDef> _cityStatesById = new(StringComparer.Ordinal);

    internal ContentDb() { }

    /// <summary>The game's own data, built into the core from <c>data/*.json</c>. Loaded and checked on first use.</summary>
    public static ContentDb Default => s_default.Value;

    public IReadOnlyList<UnitDef> Units => _units;
    /// <summary>
    /// The unit types that are trained at a building, except caravans, in data order.
    /// Not forms units evolve into, monsters, mercenaries, or other units that only come about in play.
    /// </summary>
    public IReadOnlyList<UnitDef> BaseUnits { get; internal set; } = Array.Empty<UnitDef>();
    /// <summary>
    /// The base units matches start with, in data order: <see cref="BaseUnits"/> but those marked
    /// <c>"start": false</c> (the healer, issue #42), which are only trained.
    /// </summary>
    public IReadOnlyList<UnitDef> StartUnits { get; internal set; } = Array.Empty<UnitDef>();
    /// <summary>The monsters that keep a lair on the way from every start (<see cref="LairDef.AtStarts"/>), in data order.</summary>
    public IReadOnlyList<UnitDef> LairMonsters { get; internal set; } = Array.Empty<UnitDef>();
    /// <summary>The monsters whose lairs are spread across the map (a lair with a <c>biome</c> and <c>count</c>), in data order.</summary>
    public IReadOnlyList<UnitDef> WildMonsters { get; internal set; } = Array.Empty<UnitDef>();
    /// <summary>The monster that guards the map's dusk tree (a lair with <c>duskTree</c>), or null if none does.</summary>
    public UnitDef? DuskWarden { get; internal set; }
    public IReadOnlyList<BuildingDef> Buildings => _buildings;
    public IReadOnlyList<TechDef> Techs => _techs;
    public IReadOnlyList<FactionDef> Factions => _factions;
    /// <summary>The city-states every match has a town of (see <see cref="Economy.CityStateSystem"/>).</summary>
    public IReadOnlyList<CityStateDef> CityStates => _cityStates;

    /// <summary>
    /// The side of the wild monsters (<see cref="Simulation.WildPlayer"/>): not a playable faction and not in
    /// <see cref="Factions"/>. Its units earn no experience and tame nothing.
    /// </summary>
    public FactionDef Wild { get; } = new() { Id = "wild", Name = "Wild", Description = "Monsters that guard their lairs.", Index = -1 };

    /// <summary>
    /// Hash of everything in the data that affects play (not names, descriptions or models). Command logs
    /// record it, so a replay made with different data says so instead of drifting apart.
    /// </summary>
    public ulong Hash { get; internal set; }

    /// <summary>True if some tech lets players lay roads; then only players who have one can.</summary>
    public bool RoadsNeedTech { get; internal set; }

    /// <summary>The role table: which roles are strong against which (<c>data/roles.json</c>).</summary>
    public CombatRules Combat { get; } = new();

    public UnitDef Unit(string id) =>
        _unitsById.TryGetValue(id, out var d) ? d : throw new KeyNotFoundException($"Unknown unit '{id}'");

    public bool TryGetUnit(string id, out UnitDef def) => _unitsById.TryGetValue(id, out def!);

    public BuildingDef Building(string id) =>
        _buildingsById.TryGetValue(id, out var d) ? d : throw new KeyNotFoundException($"Unknown building '{id}'");

    public bool TryGetBuilding(string id, out BuildingDef def) => _buildingsById.TryGetValue(id, out def!);

    public TechDef Tech(string id) =>
        _techsById.TryGetValue(id, out var d) ? d : throw new KeyNotFoundException($"Unknown tech '{id}'");

    public bool TryGetTech(string id, out TechDef def) => _techsById.TryGetValue(id, out def!);

    public FactionDef Faction(string id) =>
        _factionsById.TryGetValue(id, out var d) ? d : throw new KeyNotFoundException($"Unknown faction '{id}'");

    public bool TryGetFaction(string id, out FactionDef def) => _factionsById.TryGetValue(id, out def!);

    public CityStateDef CityState(string id) =>
        _cityStatesById.TryGetValue(id, out var d) ? d : throw new KeyNotFoundException($"Unknown city-state '{id}'");

    public bool TryGetCityState(string id, out CityStateDef def) => _cityStatesById.TryGetValue(id, out def!);

    /// <summary>The units and buildings a tech unlocks (those that require it), units first, in data order.</summary>
    public List<string> UnlockedBy(TechDef tech)
    {
        var names = new List<string>();
        foreach (var u in _units)
            if (Contains(u.Requires, tech)) names.Add(u.Name);
        foreach (var b in _buildings)
            if (Contains(b.Requires, tech)) names.Add(b.Name);
        return names;
    }

    /// <summary>The techs that require this one, in data order.</summary>
    public List<TechDef> LeadsTo(TechDef tech)
    {
        var list = new List<TechDef>();
        foreach (var t in _techs)
            if (Contains(t.Requires, tech)) list.Add(t);
        return list;
    }

    private static bool Contains(IReadOnlyList<TechDef> list, TechDef tech)
    {
        foreach (var t in list)
            if (t == tech) return true;
        return false;
    }

    // Duplicates are reported by the loader before it adds anything, so these only guard against bugs.
    internal void Add(UnitDef def)
    {
        def.Index = _units.Count;
        _unitsById.Add(def.Id, def);
        _units.Add(def);
    }

    internal void Add(BuildingDef def)
    {
        def.Index = _buildings.Count;
        _buildingsById.Add(def.Id, def);
        _buildings.Add(def);
    }

    internal void Add(TechDef def)
    {
        def.Index = _techs.Count;
        _techsById.Add(def.Id, def);
        _techs.Add(def);
    }

    internal void Add(FactionDef def)
    {
        def.Index = _factions.Count;
        _factionsById.Add(def.Id, def);
        _factions.Add(def);
    }

    internal void Add(CityStateDef def)
    {
        def.Index = _cityStates.Count;
        _cityStatesById.Add(def.Id, def);
        _cityStates.Add(def);
    }
}
