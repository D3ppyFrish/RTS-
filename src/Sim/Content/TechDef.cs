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
