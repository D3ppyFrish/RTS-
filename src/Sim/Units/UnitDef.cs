using System;
using System.Collections.Generic;
using Rts.Sim.Content;
using Rts.Sim.Economy;

namespace Rts.Sim.Units;

/// <summary>Shared combat roles from the design doc. Every faction fills the same roles.</summary>
public enum UnitRole : byte
{
    Worker,
    LineInfantry,
    HeavyInfantry,
    Ranged,
    Cavalry,
    Sapper,
    Caster,
    Siege,
    Monster,
    /// <summary>A caravan: trades with city-states (see <see cref="Economy.CityStateSystem"/>) and does not fight.</summary>
    Trader,
}

/// <summary>Static description of a unit type, loaded from <c>data/units.json</c>.</summary>
public sealed class UnitDef
{
    /// <summary>Position in <see cref="ContentDb.Units"/>; what unit arrays store.</summary>
    public int Index { get; internal set; }
    public required string Id { get; init; }
    public required string Name { get; init; }
    public UnitRole Role { get; init; }
    /// <summary>What the game says about it (for forms: what it gains and gives up).</summary>
    public string Description { get; init; } = "";
    /// <summary>Walking speed in blocks per second.</summary>
    public Fix64 Speed { get; init; } = Fix64.FromInt(4);
    public int MaxHp { get; init; } = 50;
    /// <summary>Visual model name for the renderer.</summary>
    public string Model { get; init; } = "humanoid";
    /// <summary>What training one costs, paid when it is queued.</summary>
    public GoodAmount[] Cost { get; init; } = Array.Empty<GoodAmount>();
    /// <summary>Ticks of training at a building that trains it.</summary>
    public int TrainTicks { get; init; } = 200;
    /// <summary>The building type that trains it; null if it cannot be trained.</summary>
    public BuildingDef? TrainedAt { get; internal set; }
    /// <summary>Techs the player must have before training it.</summary>
    public IReadOnlyList<TechDef> Requires { get; internal set; } = Array.Empty<TechDef>();
    /// <summary>The only faction that can train it; null for every faction.</summary>
    public FactionDef? Faction { get; internal set; }

    /// <summary>Hit points one hit takes off, before counters and terrain (see <see cref="CombatSystem"/>); 0 if it cannot attack.</summary>
    public int Damage { get; init; }
    /// <summary>Ticks between hits.</summary>
    public int AttackTicks { get; init; } = Simulation.TicksPerSecond;
    /// <summary>How far it hits, in blocks from its middle to the target's (or to a building's side).</summary>
    public Fix64 Range { get; init; } = Fix64.FromRatio(5, 4);
    /// <summary>How far it spots enemies to fight on its own, in blocks.</summary>
    public Fix64 Sight { get; init; } = Fix64.FromInt(8);
    /// <summary>Enemies this close (blocks) to its target are hit too; 0 for a single target.</summary>
    public Fix64 Splash { get; init; }
    /// <summary>
    /// Extra damage it takes from attacks by each role, in percent (indexed by <see cref="UnitRole"/>), or
    /// null if it has no weakness beyond the role table's.
    /// </summary>
    public int[]? WeakTo { get; init; }

    /// <summary>How its attack hurts (see <see cref="Body"/>): what armour stops and whether the wound bleeds.</summary>
    public AttackKind Attack { get; init; } = AttackKind.Slash;
    /// <summary>The shape of its body: the parts a hit can land on.</summary>
    public BodyPlan Body { get; init; } = BodyPlan.Humanoid;
    /// <summary>Armour on each part (indexed by <see cref="BodyPart"/>): points taken off each hit there, before the attack's kind.</summary>
    public int[] Armour { get; init; } = new int[Units.Body.Parts];
    /// <summary>
    /// Percent chance that its shield stops a whole hit from in front of it (see <see cref="CombatSystem"/>);
    /// 0 for no shield.
    /// </summary>
    public int Shield { get; init; }
    /// <summary>Whether its slash and pierce wounds bleed (see <see cref="CombatSystem"/>); off for bodies with no blood in them.</summary>
    public bool Bleeds { get; init; } = true;
    /// <summary>A wild monster that picks up a fresh blood trail follows it to the bleeding unit (see <see cref="MonsterSystem"/>).</summary>
    public bool TracksBlood { get; init; }
    /// <summary>It can raise earth walls (see <see cref="SpellSystem"/>).</summary>
    public bool EarthWall { get; init; }
    /// <summary>It can freeze ice bridges over water (see <see cref="SpellSystem"/>).</summary>
    public bool IceBridge { get; init; }

    /// <summary>Hit points it gives back to the most hurt ally near it (<see cref="HealRange"/>) every <see cref="HealTicks"/>; 0 if it does not heal.</summary>
    public int Heal { get; init; }
    public int HealTicks { get; init; } = 2 * Simulation.TicksPerSecond;
    public Fix64 HealRange { get; init; } = Fix64.FromInt(5);

    /// <summary>Food it eats each minute (see <see cref="Economy.EconomySystem.UpkeepInterval"/>).</summary>
    public int Upkeep { get; init; } = 1;
    /// <summary>How fast it gathers, builds, farms and digs tunnels, in percent of the usual.</summary>
    public int WorkPercent { get; init; } = 100;

    /// <summary>
    /// For a form (see <see cref="EvolutionSystem"/>): the unit type it evolves from. Null for a unit that is
    /// trained (or spawned) as it is.
    /// </summary>
    public UnitDef? EvolvesFrom { get; internal set; }
    /// <summary>
    /// A form taken only at a cleared monster den, by a faction whose den use names it (see
    /// <see cref="Content.DenUse"/>), never through experience.
    /// </summary>
    public bool FromDen { get; init; }
    /// <summary>The forms it can evolve into through experience, in data order (not those taken at a den, <see cref="FromDen"/>).</summary>
    public IReadOnlyList<UnitDef> Forms { get; internal set; } = Array.Empty<UnitDef>();
    /// <summary>How many evolutions it is from a trained unit: 0 for those, 1 for their forms, and so on.</summary>
    public int Tier { get; internal set; }

    /// <summary>For a monster: the lairs its kind keeps in the wild (see <see cref="MonsterSystem"/>); null for other units.</summary>
    public LairDef? Lair { get; init; }
    /// <summary>
    /// For a monster a taming faction beats (see <see cref="MonsterSystem"/>): who can ride it and what they
    /// become; null if nobody rides it (it can still be recruited).
    /// </summary>
    public RideDef? Ride { get; internal set; }

    /// <summary>For a mercenary: the city-state whose ally hires it (see <see cref="Economy.CityStateSystem"/>); null for other units.</summary>
    public CityStateDef? HiredFrom { get; internal set; }

    public bool CanAttack => Damage > 0;

    /// <summary>Whether matches start with some (<c>"start"</c> in the data, default true; see <see cref="Content.ContentDb.StartUnits"/>).</summary>
    public bool InStart { get; init; } = true;

    /// <summary>A caravan, which trades with city-states (role <see cref="UnitRole.Trader"/>).</summary>
    public bool IsTrader => Role == UnitRole.Trader;

    /// <summary>A form units evolve into rather than one that is trained.</summary>
    public bool IsForm => EvolvesFrom != null;

    /// <summary>The extra damage it takes from an attack by the role, in percent.</summary>
    public int WeaknessTo(UnitRole attacker) => WeakTo?[(int)attacker] ?? 0;

    /// <summary>Its armour on the part, from the data (a unit can have more, see <see cref="UnitStore.ExtraArmour"/>).</summary>
    public int ArmourOn(BodyPart part) => Armour[(int)part];

    /// <summary>It hits from further off than melee reach: it shoots, needs a clear line, and gains from high ground.</summary>
    public bool IsRanged => Range > CombatSystem.MeleeReach;

    /// <summary>Blocks moved per simulation tick.</summary>
    public Fix64 SpeedPerTick => Speed / Simulation.TicksPerSecond;
}

/// <summary>A monster's lairs: the pack that guards each, how far they guard and chase, and what clearing one gives.</summary>
public sealed class LairDef
{
    /// <summary>Monsters in each lair's pack.</summary>
    public int Pack { get; init; } = 4;
    /// <summary>The pack goes after other players' units this close to the den (blocks).</summary>
    public int Guard { get; init; } = 12;
    /// <summary>A monster this far from its den (blocks) breaks off and goes back.</summary>
    public int Leash { get; init; } = 20;
    /// <summary>Goods a player gets for clearing the lair, if their faction does not tame monsters.</summary>
    public GoodAmount[] Reward { get; init; } = Array.Empty<GoodAmount>();
    /// <summary>
    /// The monster guards the map's dusk tree (see <see cref="DuskTreeSystem"/>) instead of a den on the way
    /// from each start: it has no lair at the starts and leaves no den to use.
    /// </summary>
    public bool DuskTree { get; init; }
}

/// <summary>Riding a beaten monster: a unit of type <see cref="Rider"/> near it climbs on and becomes a <see cref="Becomes"/>.</summary>
public sealed class RideDef
{
    public required UnitDef Rider { get; init; }
    public required UnitDef Becomes { get; init; }
}
