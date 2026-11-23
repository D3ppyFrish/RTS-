using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.Content;

/// <summary>A playable faction. Players are given them in order until a start screen lets them choose.</summary>
public sealed class FactionDef
{
    /// <summary>Position in <see cref="ContentDb.Factions"/>.</summary>
    public int Index { get; internal set; }
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    /// <summary>The look of the UI while playing it (see the game's <c>UiStyle</c>): timber, stone or crystal; empty for the default.</summary>
    public string UiLook { get; init; } = "";
    /// <summary>How the faction's units earn experience and level up to evolve; null if they do not.</summary>
    public ExperienceRules? Experience { get; init; }
    /// <summary>
    /// Its units beat wild monsters instead of killing them; the player then has each one ridden or
    /// recruited (see <see cref="Units.MonsterSystem"/>). Factions that do not tame get a lair's reward
    /// for clearing it.
    /// </summary>
    public bool TamesMonsters { get; init; }
    /// <summary>What its players can do to the dusk tree once its warden is gone (see <see cref="Units.DuskTreeSystem"/>).</summary>
    public DuskTreeUse DuskTree { get; init; }
    /// <summary>How a computer player plays it (see <see cref="AI.AiPlayer"/>).</summary>
    public AiStyle AiStyle { get; init; }
    /// <summary>
    /// The units it has its own in place of (<c>"replaces"</c> in the data), from the replaced one to its own, in data
    /// order: its players cannot train the replaced one, and its matches start with its own instead (see
    /// <see cref="StartUnits"/>).
    /// </summary>
    public IReadOnlyList<(Units.UnitDef From, Units.UnitDef To)> UnitReplacements { get; internal set; } = Array.Empty<(Units.UnitDef, Units.UnitDef)>();
    /// <summary>
    /// The buildings it has its own in place of, in data order: its players cannot build the replaced one, nor train
    /// anything in one of them (or an upgrade of one) they capture.
    /// </summary>
    public IReadOnlyList<(Economy.BuildingDef From, Economy.BuildingDef To)> BuildingReplacements { get; internal set; } = Array.Empty<(Economy.BuildingDef, Economy.BuildingDef)>();

    /// <summary>
    /// The units its matches start with, in turn (see <see cref="Simulation.SpawnStartingUnits"/>): everyone's
    /// (<see cref="ContentDb.StartUnits"/>) with its own in place of those it replaces.
    /// </summary>
    public IReadOnlyList<Units.UnitDef> StartUnits { get; internal set; } = Array.Empty<Units.UnitDef>();

    /// <summary>True if the faction has its own unit in place of this one.</summary>
    public bool Replaces(Units.UnitDef def)
    {
        foreach (var r in UnitReplacements)
            if (r.From == def) return true;
        return false;
    }

    /// <summary>True if the faction has its own building in place of this one, or of the building it is an upgrade of.</summary>
    public bool Replaces(Economy.BuildingDef def)
    {
        for (var d = def; d != null; d = d.UpgradeOf)
            foreach (var r in BuildingReplacements)
                if (r.From == d) return true;
        return false;
    }

    /// <summary>The unit the faction has in place of this one: its own if it replaces it, else the same.</summary>
    public Units.UnitDef Own(Units.UnitDef def)
    {
        foreach (var r in UnitReplacements)
            if (r.From == def) return r.To;
        return def;
    }

    /// <summary>The building the faction has in place of this one: its own if it replaces it, else the same.</summary>
    public Economy.BuildingDef Own(Economy.BuildingDef def)
    {
        foreach (var r in BuildingReplacements)
            if (r.From == def) return r.To;
        return def;
    }
}

/// <summary>
/// How a computer player plays a faction. What each level makes of it is in <see cref="AI.AiPlayer"/>: the
/// levels that can build a wall (Normal and Hard) do so only for a balanced faction.
/// </summary>
public enum AiStyle : byte
{
    /// <summary>It masses its soldiers and sends nearly all of them as one big wave, and builds no walls.</summary>
    Waves,
    /// <summary>
    /// It walls its town, and when it attacks it keeps a good part of its soldiers at home behind the wall
    /// and sends the rest: smaller attacks than a wave, from a town that is not left open.
    /// </summary>
    Balanced,
}

/// <summary>What a faction can do to the dusk tree once its warden is gone (see <see cref="Units.DuskTreeSystem"/>).</summary>
public enum DuskTreeUse : byte
{
    /// <summary>Nothing.</summary>
    None,
    /// <summary>Heal it with a unit that heals, which takes up the Dusk Staff.</summary>
    Heal,
    /// <summary>Hack it down, so nobody gets the staff.</summary>
    Raze,
}

/// <summary>
/// What a faction makes of a cleared monster den, its prize for beating that monster (a lair's
/// <see cref="Units.LairDef.Prizes"/>, see <see cref="Units.MonsterSystem"/>): the den is used up either way.
/// Exactly one of the three is set.
/// </summary>
public sealed class DenUse
{
    /// <summary>A building put up on the den as a site the player's workers build, paid for as usual (a <see cref="Economy.BuildingDef.FromDen"/> building).</summary>
    public Economy.BuildingDef? Building { get; init; }
    /// <summary>A form one of the player's units near the den takes at once, a form it can get no other way (a <see cref="Units.UnitDef.FromDen"/> form).</summary>
    public Units.UnitDef? Form { get; init; }
    /// <summary>A relic the nearest of the player's fighting units without one takes up.</summary>
    public RelicDef? Relic { get; init; }
}

/// <summary>
/// How a faction's units earn experience, and the experience each level needs. At each level a unit can
/// take its next form (see <see cref="Units.EvolutionSystem"/>).
/// </summary>
public sealed class ExperienceRules
{
    /// <summary>The experience each level needs, rising: level 1 at Levels[0], level 2 at Levels[1], and so on.</summary>
    public IReadOnlyList<int> Levels { get; init; } = Array.Empty<int>();
    /// <summary>Per hit point of damage dealt to enemy units and buildings.</summary>
    public int PerDamage { get; init; }
    /// <summary>Per enemy unit killed (a death counts for the nearest unit that hit it).</summary>
    public int PerKill { get; init; }
    /// <summary>Per second spent building.</summary>
    public int PerBuildSecond { get; init; }
    /// <summary>Per minute alive.</summary>
    public int PerMinute { get; init; }

    /// <summary>The level that much experience gives: how many of <see cref="Levels"/> it reaches.</summary>
    public int Level(int xp)
    {
        int level = 0;
        while (level < Levels.Count && xp >= Levels[level]) level++;
        return level;
    }
}

/// <summary>What a researched tech changes for its player.</summary>
public enum TechEffectType : byte
{
    /// <summary>Workers dig, chop and pick <see cref="TechEffect.Amount"/> percent faster.</summary>
    GatherSpeed,
    /// <summary>Workers carry <see cref="TechEffect.Amount"/> more goods.</summary>
    Carry,
    /// <summary>Each farm harvest gives <see cref="TechEffect.Amount"/> more food.</summary>
    FarmYield,
    /// <summary>Each of the player's <see cref="TechEffect.Building"/> houses <see cref="TechEffect.Amount"/> more people.</summary>
    Housing,
    /// <summary>Matching units have <see cref="TechEffect.Amount"/> more hit points, those already alive too.</summary>
    UnitHp,
    /// <summary>Matching units walk <see cref="TechEffect.Amount"/> percent faster.</summary>
    UnitSpeed,
    /// <summary>The player can lay roads. If no tech has this effect, roads need no tech.</summary>
    Roads,
    /// <summary>
    /// When it is researched, every <see cref="TechEffect.Reveal"/> on the map (all the old ruins, monster lairs or
    /// city-state towns) comes out of the player's fog as seen ground, as if marked on a chart.
    /// </summary>
    Reveal,
    /// <summary>Wild monsters leave the player's workers alone: they do not go after them, though they still answer one that hits them.</summary>
    SpareWorkers,
}

/// <summary>What a <see cref="TechEffectType.Reveal"/> effect shows on the map.</summary>
public enum MapFeature : byte
{
    /// <summary>The old ruins (the ancient sites).</summary>
    Ruins,
    /// <summary>The monsters' lairs, the caverns' and the deeps' too.</summary>
    Lairs,
    /// <summary>The city-states' towns.</summary>
    Towns,
}

/// <summary>
/// What a player has to do before they can research a discovery tech (the design doc's discovery techs, unlocked
/// only by exploring). Once done, it stays done for the rest of the match.
/// </summary>
public enum Discovery : byte
{
    /// <summary>Not a discovery tech: anyone can research it.</summary>
    None,
    /// <summary>The player's units finished searching an old ruin.</summary>
    Ruin,
    /// <summary>The player's unit got the last of a monster lair's pack.</summary>
    Lair,
    /// <summary>The player broke a city-state's totem and took the town.</summary>
    Town,
}

/// <summary>One effect of a tech.</summary>
public sealed class TechEffect
{
    public TechEffectType Type { get; init; }
    /// <summary>A percentage for the speed effects, otherwise a count.</summary>
    public int Amount { get; init; }
    /// <summary>The building type a housing effect applies to.</summary>
    public BuildingDef? Building { get; init; }
    /// <summary>For unit effects: only units of this role.</summary>
    public UnitRole? Role { get; init; }
    /// <summary>For unit effects: only this unit type. With neither a role nor a unit, every unit.</summary>
    public UnitDef? Unit { get; init; }
    /// <summary>For a reveal effect: what it shows.</summary>
    public MapFeature Reveal { get; init; }

    /// <summary>True if a unit effect applies to units of this type.</summary>
    public bool AppliesTo(UnitDef def) => Unit != null ? Unit == def : Role == null || Role == def.Role;
}

/// <summary>
/// Static description of a tech: researched once per player at a town hall, one at a time, after the
/// techs it requires. Units and buildings name the techs that unlock them in their own requirements.
/// </summary>
public sealed class TechDef
{
    /// <summary>Position in <see cref="ContentDb.Techs"/>; what research state stores.</summary>
    public int Index { get; internal set; }
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    /// <summary>The name of the picture the game shows for it (see the game's <c>UiIcons</c>); the id if not given.</summary>
    public string Icon { get; init; } = "";
    /// <summary>The only faction that can research it; null for every faction.</summary>
    public FactionDef? Faction { get; internal set; }
    /// <summary>What the player must have done before they can research it; <see cref="Discovery.None"/> for most techs.</summary>
    public Discovery Discovery { get; init; }
    /// <summary>What researching it costs, paid when it starts.</summary>
    public GoodAmount[] Cost { get; init; } = Array.Empty<GoodAmount>();
    /// <summary>Ticks of research at a town hall.</summary>
    public int ResearchTicks { get; init; } = 20 * Simulation.TicksPerSecond;
    /// <summary>Techs the player must have first.</summary>
    public IReadOnlyList<TechDef> Requires { get; internal set; } = Array.Empty<TechDef>();
    public IReadOnlyList<TechEffect> Effects { get; internal set; } = Array.Empty<TechEffect>();
    /// <summary>The longest chain of prerequisites under it: 0 for a tech that needs none. For laying out the tree.</summary>
    public int Depth { get; internal set; }
}
