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
    /// <summary>A cart: hauls goods between storage on supply duty, a big load at a time (see <see cref="UnitDef.Carry"/>), and does not fight.</summary>
    Hauler,
    /// <summary>A boat: sails on water and carries units across it (see <see cref="UnitDef.Seats"/> and <see cref="BoatSystem"/>), and does not fight.</summary>
    Boat,
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
    /// How close (blocks, middle to middle) an enemy unit can be and still be hit (<c>"minRange"</c> in the data):
    /// a catapult cannot drop a stone on what stands right beside it. 0 for no such limit. Buildings are hit at any distance.
    /// </summary>
    public Fix64 MinRange { get; init; }
    /// <summary>
    /// Its shots go up in a high arc (<c>"lobs"</c> in the data): it needs no clear line to its target, so it hits
    /// what stands behind a wall or a hill it can see over (see <see cref="CombatSystem"/>).
    /// </summary>
    public bool Lobs { get; init; }
    /// <summary>
    /// A machine (<c>"machine"</c> in the data), such as a catapult: it never breaks and runs however its morale falls,
    /// and has no blood in it (it does not bleed unless the data says so).
    /// </summary>
    public bool Machine { get; init; }
    /// <summary>
    /// A ram (<c>"ram"</c> in the data), such as the battering ram: it attacks buildings only, never units, even when
    /// ordered to, and its blows land at the foot of a wall, knocking out a column's bottom block as a sapper's do, so
    /// the rest of the column falls (see <see cref="CombatSystem"/>).
    /// </summary>
    public bool Ram { get; init; }
    /// <summary>
    /// What its blows on a gate deal, in percent of a blow on any other building (<c>"gates"</c> in the data; 100 for
    /// no difference): a ram swung at the timbers of a gate breaks it far sooner than solid wall.
    /// </summary>
    public int Gates { get; init; } = 100;
    /// <summary>
    /// How much less, in percent, arrows and other ranged piercing shots hurt it (<c>"cover"</c> in the data; 0 for
    /// none): the battering ram's roof of hides. Blows, blasts and stones land as usual.
    /// </summary>
    public int Cover { get; init; }
    /// <summary>
    /// How far (blocks) the crater its shots blow in the ground reaches round where they land (<c>"crater"</c> in the
    /// data; 0 for none): the ground there is thrown out onto the rim (see <see cref="CombatSystem.Crater"/>).
    /// </summary>
    public int Crater { get; init; }
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
    /// <summary>
    /// A tamed beast its owner can keep in a beast pen and loose from it as a stampede (<c>"pen": true</c>; a monster;
    /// see <c>CombatSystem.Stampede.cs</c>).
    /// </summary>
    public bool Pennable { get; init; }
    /// <summary>It can raise earth walls (see <see cref="SpellSystem"/>).</summary>
    public bool EarthWall { get; init; }
    /// <summary>
    /// Its attack is a blast (a siege engine's shot, a boulder, strong magic; <c>"blast"</c> in the data): it deals
    /// full damage to a thick wall, gate or tower (see <see cref="BuildingDef.Thick"/>), and a block it knocks out
    /// of one brings down what stands above and beside it, toppling away from the blast onto whoever is behind.
    /// </summary>
    public bool Blast { get; init; }
    /// <summary>It can freeze ice bridges over water (see <see cref="SpellSystem"/>).</summary>
    public bool IceBridge { get; init; }
    /// <summary>It can open chasms in the ground (see <see cref="SpellSystem"/>).</summary>
    public bool Chasm { get; init; }
    /// <summary>It can grow forests (see <see cref="SpellSystem"/>).</summary>
    public bool GrowForest { get; init; }

    /// <summary>Hit points it gives back to the most hurt ally near it (<see cref="HealRange"/>) every <see cref="HealTicks"/>; 0 if it does not heal.</summary>
    public int Heal { get; init; }
    public int HealTicks { get; init; } = 2 * Simulation.TicksPerSecond;
    public Fix64 HealRange { get; init; } = Fix64.FromInt(5);

    /// <summary>
    /// Percent of its hit points it heals each second even in a fight (ogres shrug off wounds); 0 for none.
    /// Out of the fight it heals at this rate or the usual one, whichever is faster (see <c>CombatSystem.StepRecovery</c>).
    /// </summary>
    public int Regen { get; init; }

    /// <summary>
    /// Its <see cref="Regen"/> works only out of the daylight (the trolls): by night, at dawn and dusk, or under a roof
    /// of rock or anything else, but not under the open sky by day (see <c>Conditions.InDaylight</c>).
    /// </summary>
    public bool NightRegen { get; init; }

    /// <summary>
    /// A hero (the Settlers' last forms): one of a kind, so a player has at most one living unit of this type
    /// and none evolves into it while one lives, and it never breaks and runs (see <see cref="EvolutionSystem"/>,
    /// <c>CombatSystem</c>).
    /// </summary>
    public bool Hero { get; init; }
    /// <summary>
    /// How near (blocks) its own side's units have to be for it to steady them; 0 if it steadies nobody.
    /// Steadied units lose <see cref="Steady"/> percent less morale to deaths near them and get
    /// <see cref="Inspire"/> morale back a second instead of one, and ranged ones deal <see cref="Volley"/>
    /// percent more damage, cavalry ones move <see cref="Spur"/> percent faster, sappers work
    /// <see cref="Undermine"/> percent faster, casters are ready to cast again <see cref="Quicken"/> percent
    /// sooner and workers work <see cref="Hasten"/> percent faster (see <c>CombatSystem</c>).
    /// </summary>
    public Fix64 Rally { get; init; }
    /// <summary>Percent less morale the units it steadies lose to deaths (see <see cref="Rally"/>).</summary>
    public int Steady { get; init; }
    /// <summary>Morale the units it steadies get back each second (see <see cref="Rally"/>); the usual is 1.</summary>
    public int Inspire { get; init; } = 1;
    /// <summary>
    /// Percent more damage its own side's ranged units (<see cref="UnitRole.Ranged"/>, itself included) deal
    /// while within its <see cref="Rally"/> range: the Bowmaster calls the volleys (see <c>CombatSystem</c>).
    /// </summary>
    public int Volley { get; init; }
    /// <summary>
    /// Percent faster its own side's cavalry (<see cref="UnitRole.Cavalry"/>, itself included) moves while
    /// within its <see cref="Rally"/> range: the Horselord leads the charge (see <c>CombatSystem.SpurOf</c>).
    /// </summary>
    public int Spur { get; init; }
    /// <summary>
    /// Percent faster its own side's sappers (<see cref="UnitRole.Sapper"/>, itself included) work while within
    /// its <see cref="Rally"/> range: their blows on buildings land that much harder and they dig tunnels and
    /// dig areas that much faster. The Breachmaster works the breach (see <c>CombatSystem.UndermineOf</c>).
    /// </summary>
    public int Undermine { get; init; }
    /// <summary>
    /// Percent sooner its own side's casters (<see cref="UnitRole.Caster"/>, itself included) are ready to cast
    /// again after a spell cast while within its <see cref="Rally"/> range: the wait after a spell is divided by
    /// 1 + Quicken / 100, so 100 halves it. The Archmage quickens the spells (see <c>CombatSystem.QuickenOf</c>).
    /// </summary>
    public int Quicken { get; init; }
    /// <summary>
    /// Percent faster its own side's workers (<see cref="UnitRole.Worker"/>, itself included) work while within
    /// its <see cref="Rally"/> range: gathering, building, farming, quarrying, digging and cleaning take
    /// 1 + Hasten / 100 times less long, on top of their own <see cref="WorkPercent"/>. The Master Builder drives
    /// the work (see <c>CombatSystem.HasteOf</c>).
    /// </summary>
    public int Hasten { get; init; }

    /// <summary>
    /// A war drummer (<c>"drums"</c> in the data; the Orcs'): its own side's broken units within its
    /// <see cref="Rally"/> range (its earshot) run to it instead of home and rally there, and it can beat a charge
    /// (see <see cref="ChargeCommand"/>, <see cref="DrumDef"/>, <c>CombatSystem.Drums.cs</c>). Null for every other unit.
    /// </summary>
    public DrumDef? Drums { get; init; }

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
    /// For a corrupted beast of the dark power (see <see cref="World.AwakeningSystem"/>): the stage of its waking from
    /// which broods of this kind rise out of the Heartscar (1 to <see cref="World.AwakeningSystem.Stages"/>); 0 for
    /// other units.
    /// </summary>
    public int Awakening { get; init; }
    /// <summary>How many of this kind rise in each brood sent at a player, once its <see cref="Awakening"/> stage has come.</summary>
    public int Brood { get; init; }
    /// <summary>Goods a player's storage nearest the spot gets when one of their units kills or beats this monster; none for most.</summary>
    public GoodAmount[] Bounty { get; init; } = Array.Empty<GoodAmount>();
    /// <summary>
    /// Wild game (see <see cref="MonsterSystem.Herds"/>): what a hunter brings home from one it shoots (one good, see
    /// <see cref="Economy.EconomySystem"/>'s hunting); none for every other unit. Game never fights, nobody goes after
    /// it on their own, and a faction that tames monsters kills it rather than beating it.
    /// </summary>
    public GoodAmount[] Game { get; init; } = Array.Empty<GoodAmount>();
    /// <summary>Wild game: hunters shoot it for <see cref="Game"/>.</summary>
    public bool IsGame => Game.Length > 0;
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

    /// <summary>How many hatch from one training, all for its cost and train time (<c>"batch"</c>, default 1; the Deepfolk's swarms).</summary>
    public int Batch { get; init; } = 1;

    /// <summary>
    /// For <see cref="Units.CombatSystem.BurstSeconds"/> after it comes up out of the ground (see
    /// <see cref="VisionSystem.Covered"/>), its blows deal this many percent more damage (<c>"burst"</c>; 0 for none).
    /// </summary>
    public int Burst { get; init; }

    /// <summary>A caravan, which trades with city-states (role <see cref="UnitRole.Trader"/>).</summary>
    public bool IsTrader => Role == UnitRole.Trader;

    /// <summary>A cart, which hauls goods between storage on supply duty (role <see cref="UnitRole.Hauler"/>).</summary>
    public bool IsHauler => Role == UnitRole.Hauler;

    /// <summary>
    /// How many goods one load of a cart is (<c>"carry"</c> in the data; only for role hauler); 0 for every other
    /// unit, which carries what a worker does (<see cref="Economy.EconomySystem.Capacity"/>).
    /// </summary>
    public int Carry { get; init; }

    /// <summary>
    /// A pack pony (<c>"runs"</c> in the data; only for role hauler): it runs goods to building sites and workshops and
    /// carries what workshops, quarries and mines make back to storage (see <see cref="Economy.EconomySystem.StepBring"/>),
    /// and takes no supply duty between storehouses, which is the cart's work.
    /// </summary>
    public bool Runs { get; init; }
    /// <summary>
    /// It sees in the dark (<c>"darkSight"</c> in the data; the delver): night, weather and mist do not shorten its sight,
    /// and underground (see <see cref="VisionSystem.Covered"/>) it sees twice as far (see <see cref="VisionSystem"/>).
    /// </summary>
    public bool DarkSight { get; init; }
    /// <summary>
    /// Columns round it in which it hears units underground, as a listening post does (<c>"hearing"</c> in the data;
    /// see <see cref="VisionSystem.Hears"/>); 0 for only <see cref="VisionSystem.HearClose"/>, as every unit does.
    /// </summary>
    public int Hearing { get; init; }
    /// <summary>
    /// It knows the deep caves (<c>"caveWise"</c> in the data; the delver): lava's heat does not hurt it (see
    /// <see cref="World.HazardSystem"/>), and in the deep caves (see <see cref="Economy.EconomySystem.DeepGround"/>) wild
    /// monsters do not go after it, though they still answer it if it hits them (see <see cref="MonsterSystem.Spares"/>).
    /// </summary>
    public bool CaveWise { get; init; }

    /// <summary>A cart: a hauler on supply duty between storage (role <see cref="UnitRole.Hauler"/>, not a pack pony).</summary>
    public bool IsCart => IsHauler && !Runs;

    /// <summary>
    /// For a boat (role <see cref="UnitRole.Boat"/>, <c>"seats"</c> in the data): how many units it carries. It sails
    /// only on open water and never sets foot on land (see <see cref="BoatSystem"/>); 0 for every other unit.
    /// </summary>
    public int Seats { get; init; }

    /// <summary>
    /// A ship of another role (<c>"sails"</c> in the data), such as the siege role's bombard ship: it sails on the water
    /// as a boat does (see <see cref="BoatSystem"/>) but carries nobody (it has no <see cref="Seats"/>).
    /// </summary>
    public bool Sails { get; init; }

    /// <summary>A ship, which sails on water (role <see cref="UnitRole.Boat"/>, which carries units, or <see cref="Sails"/>).</summary>
    public bool IsBoat => Role == UnitRole.Boat || Sails;

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

/// <summary>
/// A war drummer's drums (see <see cref="UnitDef.Drums"/>): the charge it beats, and how far off rivals hear it.
/// </summary>
public sealed class DrumDef
{
    /// <summary>Percent faster its own side's units within its earshot walk while it beats the charge.</summary>
    public int Speed { get; init; } = 40;
    /// <summary>Percent more damage the first blow deals that each unit in earshot strikes during the charge.</summary>
    public int Blow { get; init; } = 50;
    /// <summary>Ticks a charge lasts (<c>"time"</c> in the data, seconds).</summary>
    public int ChargeTicks { get; init; } = 6 * Simulation.TicksPerSecond;
    /// <summary>Ticks from beating a charge until it can beat the next (<c>"wait"</c> in the data, seconds).</summary>
    public int WaitTicks { get; init; } = 60 * Simulation.TicksPerSecond;
    /// <summary>
    /// Columns (<c>"heard"</c>): while drumming (beating the charge or rallying broken units), it shows through the fog
    /// of war to every rival with a unit or building this near it (see <see cref="VisionSystem.SeesUnit"/>).
    /// </summary>
    public int Heard { get; init; } = 40;
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
    /// <summary>
    /// Where lairs of this monster are spread across the map besides the starts' (see
    /// <see cref="MonsterSystem.PlaceWild"/>): the biome their dens are in, or null for none.
    /// </summary>
    public World.Biome? Biome { get; init; }
    /// <summary>Lairs of this monster spread over its <see cref="Biome"/> for each 512 by 512 blocks of map.</summary>
    public int Count { get; init; }
    /// <summary>A lair of this monster lies on the way from every start (see <see cref="MonsterSystem.PlaceAtStarts"/>).</summary>
    public bool AtStarts { get; init; } = true;
    /// <summary>
    /// Where in the map's cavern lairs of this monster lie (see <see cref="MonsterSystem.PlaceCavern"/>), <see cref="Count"/>
    /// of them (one map has one cavern): <see cref="CavernSpot.None"/> for none.
    /// </summary>
    public CavernSpot Cavern { get; init; }
    /// <summary>
    /// How far from its den (blocks) a pack spread across the map roams (see <see cref="MonsterSystem.PlaceWild"/>):
    /// it wanders the land round its den and goes after whoever comes near it, workers too; 0 for a pack that stays home.
    /// The starts' lairs and the city-states' never roam.
    /// </summary>
    public int Roam { get; init; }
    /// <summary>
    /// The pack grows the longer it is left (the shadowed wilds' corrupted beasts, see <see cref="MonsterSystem"/>):
    /// every this many ticks while the lair is not cleared, one more of the monster is born at the den, until the
    /// pack is <see cref="GrowTo"/> strong; 0 for a pack that never grows. <c>grow</c> in the data, in seconds.
    /// </summary>
    public int GrowTicks { get; init; }
    /// <summary>The most monsters a growing pack (see <see cref="GrowTicks"/>) grows to.</summary>
    public int GrowTo { get; init; }
    /// <summary>
    /// The pack waits in the shallow water by its den (the swamp's bog horrors), one monster to a pool column, and a
    /// monster lying there with nothing to fight is unseen by other players until one of their units comes within
    /// <see cref="Rts.Sim.VisionSystem.SpotSubmerged"/> columns (see <see cref="MonsterSystem.Submerged"/>).
    /// </summary>
    public bool Lurk { get; init; }
    /// <summary>
    /// The lair stands in a mountain pass (the stone giant, see <see cref="World.RegionMap.Passes"/>): one in each of
    /// the map's passes rather than <see cref="Count"/> spread over its <see cref="Biome"/>, which it falls back to on a
    /// map with no pass. <c>pass</c> in the data.
    /// </summary>
    public bool Pass { get; init; }
    /// <summary>
    /// How many percent harder the first blow is that a lurking monster strikes coming up out of the water (see
    /// <see cref="Lurk"/>); 0 for none.
    /// </summary>
    public int Ambush { get; init; }
    /// <summary>
    /// What each faction gets for beating this monster: what it makes of the den a cleared lair leaves (see
    /// <see cref="MonsterSystem"/>), in data order. Two factions can share a prize; one not listed gets none.
    /// </summary>
    public IReadOnlyList<LairPrize> Prizes { get; internal set; } = Array.Empty<LairPrize>();
    /// <summary>A cleared lair leaves a den some faction can use once: the lair has a prize. Never the dusk tree's.</summary>
    public bool Den => Prizes.Count > 0;

    /// <summary>The faction's prize for this monster, or null if it has none.</summary>
    public Content.DenUse? PrizeFor(Content.FactionDef faction)
    {
        foreach (var p in Prizes)
            if (p.Faction == faction) return p.Use;
        return null;
    }
}

/// <summary>Where in the map's cavern a monster's lairs lie (see <see cref="LairDef.Cavern"/>).</summary>
public enum CavernSpot : byte
{
    /// <summary>Not in the cavern.</summary>
    None,
    /// <summary>Spread along the cavern, from near the way in to near its far end.</summary>
    Along,
    /// <summary>In the chamber furthest from the way in (a boss).</summary>
    Deepest,
}

/// <summary>One faction's prize for beating a lair's monster (see <see cref="LairDef.Prizes"/>).</summary>
public sealed record LairPrize(Content.FactionDef Faction, Content.DenUse Use);

/// <summary>Riding a beaten monster: a unit of type <see cref="Rider"/> near it climbs on and becomes a <see cref="Becomes"/>.</summary>
public sealed class RideDef
{
    public required UnitDef Rider { get; init; }
    public required UnitDef Becomes { get; init; }
}
