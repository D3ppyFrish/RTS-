using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.Content;

/// <summary>
/// A city-state race's town (<c>data/citystates.json</c>): its totem and guards, the tribute and units its
/// holder gets, and what it trades (see <see cref="CityStateSystem"/>).
/// </summary>
public sealed class CityStateDef
{
    /// <summary>Position in <see cref="ContentDb.CityStates"/>.</summary>
    public int Index { get; internal set; }
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";

    /// <summary>
    /// The totem in the middle of the town (a building of kind totem, see <see cref="BuildingKind.Totem"/>), or
    /// null if the town cannot be conquered. Whoever destroys it holds the town (see <see cref="CityStateSystem"/>).
    /// </summary>
    public BuildingDef? Totem { get; internal set; }
    /// <summary>Ticks after the totem falls before it stands again, in its new holder's colour.</summary>
    public int TotemRespawnTicks { get; init; } = 5 * Simulation.TicksPerSecond;

    /// <summary>The town's guards, one per entry (in data order), standing round the market.</summary>
    public IReadOnlyList<UnitDef> Guards { get; internal set; } = Array.Empty<UnitDef>();
    /// <summary>How far beyond the town's square (columns) its guards go after soldiers.</summary>
    public int GuardReach { get; init; } = 4;
    /// <summary>Ticks until a fallen guard is replaced (one at a time).</summary>
    public int GuardRefillTicks { get; init; } = 45 * Simulation.TicksPerSecond;

    /// <summary>The good a tribute caravan brings the town's holder.</summary>
    public Good TributeGood { get; init; } = Good.Iron;
    /// <summary>How much of <see cref="TributeGood"/> each tribute caravan carries (0 for no tribute).</summary>
    public int TributeAmount { get; init; } = 15;
    /// <summary>Ticks between tribute caravans while the town has a holder.</summary>
    public int TributeTicks { get; init; } = 60 * Simulation.TicksPerSecond;
    /// <summary>The unit that carries the tribute (a trader), or null for no tribute.</summary>
    public UnitDef? TributeCaravan { get; internal set; }

    /// <summary>The good caravans bring to the town to sell.</summary>
    public Good Buys { get; init; } = Good.Food;
    /// <summary>How much of <see cref="Buys"/> a caravan carries there.</summary>
    public int Load { get; init; } = 20;
    /// <summary>The good caravans bring back.</summary>
    public Good Sells { get; init; } = Good.Iron;
    /// <summary>How much of <see cref="Sells"/> a load fetches for each 100 blocks of route.</summary>
    public int Price { get; init; } = 5;
    /// <summary>How much more the holder's caravans bring back, in percent.</summary>
    public int HolderBonus { get; init; } = 50;

    /// <summary>
    /// How far beyond the town's square (columns) its holder's hurt units heal as at home: at the home rate,
    /// with their wounds bound, once out of the fight (see <c>CombatSystem.StepRecovery</c>). Negative for no
    /// healing there.
    /// </summary>
    public int HealRange { get; init; } = 6;

    /// <summary>The units the town's holder can hire there (those whose <see cref="UnitDef.HiredFrom"/> is this), in data order.</summary>
    public IReadOnlyList<UnitDef> Mercenaries { get; internal set; } = Array.Empty<UnitDef>();
    /// <summary>Most mercenaries ready to hire at a time.</summary>
    public int Pool { get; init; } = 6;
    /// <summary>Ticks until one more mercenary is ready, while fewer than <see cref="Pool"/> are.</summary>
    public int RefillTicks { get; init; } = 30 * Simulation.TicksPerSecond;

    /// <summary>The monster whose lair lies near the town, or null for none.</summary>
    public UnitDef? Lair { get; internal set; }

    /// <summary>What the huts' walls are built of.</summary>
    public BlockType Wall { get; init; } = BlockType.Planks;
    /// <summary>What the huts' roofs are built of.</summary>
    public BlockType Roof { get; init; } = BlockType.Thatch;
    /// <summary>What the town square is paved with (its top layer): grass unless the data says otherwise.</summary>
    public BlockType Ground { get; init; } = BlockType.Grass;

    /// <summary>
    /// True for a town dug out of a chamber of the deep caves (see <c>RegionMap.Deeps</c>) instead of standing in
    /// the open; on a map with no deep caves it is left out. See <c>CityStateSystem.PlaceDeep</c>.
    /// </summary>
    public bool Deep { get; init; }

    /// <summary>
    /// True for a town on a river bank whose people built a stone bridge across the river beside it (the trolls'):
    /// it is placed where the river runs close by, and the bridge laid with it (see <c>CityStateSystem.PlaceCrossing</c>).
    /// On a map with no river beside a fair site it stands like any other town, without a bridge.
    /// </summary>
    public bool Crossing { get; init; }

    /// <summary>
    /// Gold a player pays for each unit of theirs that comes onto the town's toll ground (its square, its bridge, and
    /// <see cref="GuardReach"/> columns round them), unless they hold the town; 0 for no toll. A unit whose owner cannot
    /// pay is fought by the guards, even a worker or a caravan, and soldiers that paid are let through. The toll goes
    /// to the town's holder (see <c>CityStateSystem.StepToll</c>).
    /// </summary>
    public int TollGold { get; init; }
    /// <summary>The toll as a cost, for paying it.</summary>
    public GoodAmount[] TollCost => _tollCost ??= TollGold > 0 ? new[] { new GoodAmount(Good.Gold, TollGold) } : Array.Empty<GoodAmount>();
    private GoodAmount[]? _tollCost;
    /// <summary>
    /// Ticks a toll town takes no toll from a player whose units struck its totem or its guards, and its guards fight
    /// every unit of theirs on its toll ground instead, from the last blow.
    /// </summary>
    public int FeudTicks { get; init; } = 60 * Simulation.TicksPerSecond;

    /// <summary>
    /// How far round the town (columns from its square) its holder sees while they hold it, as their units would: the
    /// land its scouts ride (the centaurs'). 0 for nothing beyond the totem's own sight. See <c>VisionSystem.Step</c>.
    /// </summary>
    public int WatchRange { get; init; }

    /// <summary>
    /// The biome the town keeps to (the centaurs' open grassland), or null for any: it is placed at the best site whose
    /// market and most of the land round it are of that biome, and only with no such site anywhere else (see
    /// <c>CityStateSystem.InBiome</c>). Not for a deep town or a crossing.
    /// </summary>
    public World.Biome? Biome { get; init; }
}
