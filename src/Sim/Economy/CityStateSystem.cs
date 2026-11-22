using System;
using System.Collections.Generic;
using Rts.Sim.Content;
using Rts.Sim.Units;
using Rts.Sim.World;

namespace Rts.Sim.Economy;

/// <summary>A city-state's town on the map, and who holds it (see <see cref="CityStateSystem"/>).</summary>
public sealed class CityState
{
    internal CityState(int id, CityStateDef def, int x, int y, int z)
    {
        Id = id;
        Def = def;
        X = x;
        Y = y;
        Z = z;
        Ready = def.Pool;
        GuardIds = new int[def.Guards.Count];
    }

    public int Id { get; }
    public CityStateDef Def { get; }
    /// <summary>The market's column, in the middle of the town square: the totem stands on it.</summary>
    public int X { get; }
    public int Z { get; }
    /// <summary>The level of the town square: units in it stand with their feet at Y.</summary>
    public int Y { get; }
    /// <summary>The player who holds the town (its last totem's destroyer), or -1 while it is neutral.</summary>
    public int Holder { get; internal set; } = -1;
    /// <summary>The standing totem (a building id), or 0 while it is down.</summary>
    public int TotemId { get; internal set; }
    /// <summary>The player whose unit last hit the standing totem, or -1.</summary>
    public int LastHitBy { get; internal set; } = -1;
    /// <summary>Ticks left until a fallen totem stands again.</summary>
    public int RespawnTimer { get; internal set; }
    /// <summary>The guard (unit id) at each of the town's guard posts, by <see cref="CityStateDef.Guards"/> entry; 0 for a fallen one.</summary>
    internal readonly int[] GuardIds;
    /// <summary>Ticks spent toward replacing a fallen guard.</summary>
    public int GuardTimer { get; internal set; }
    /// <summary>Ticks spent toward the next tribute caravan, while the town has a holder.</summary>
    public int TributeTimer { get; internal set; }
    /// <summary>The town's tribute caravans on the road (unit ids, oldest first).</summary>
    internal readonly List<int> CaravanIds = new();
    /// <summary>Mercenaries ready to hire (at most <see cref="CityStateDef.Pool"/>).</summary>
    public int Ready { get; internal set; }
    /// <summary>Ticks spent toward the next mercenary, while fewer than the pool are ready.</summary>
    public int RefillTimer { get; internal set; }
    /// <summary>The lair placed near the town with it (see <see cref="MonsterSystem"/>), or 0 for none.</summary>
    public int LairId { get; internal set; }

    /// <summary>
    /// The town's bridge (a crossing's, see <see cref="CityStateDef.Crossing"/>): the columns from (BridgeX0, BridgeZ0)
    /// to (BridgeX1, BridgeZ1), its deck one under the square's level. Empty (X1 below X0) for a town without one.
    /// </summary>
    public int BridgeX0 { get; internal set; }
    public int BridgeZ0 { get; internal set; }
    public int BridgeX1 { get; internal set; } = -1;
    public int BridgeZ1 { get; internal set; } = -1;
    /// <summary>True if the town has a bridge.</summary>
    public bool HasBridge => BridgeX1 >= BridgeX0;

    /// <summary>Units on the toll ground whose owner paid the toll this time on it (unit ids).</summary>
    internal readonly SortedSet<int> Paid = new();
    /// <summary>Units on the toll ground whose owner could not pay (unit ids): the guards fight them.</summary>
    internal readonly SortedSet<int> Unpaid = new();
    /// <summary>Ticks left of each player's feud with the town (see <see cref="CityStateDef.FeudTicks"/>), by player.</summary>
    internal readonly int[] Feud = new int[Simulation.WildPlayer];
    /// <summary>Gold the toll has brought in since the town last changed hands (its holder's, or the town's own while it has none).</summary>
    public int Tolls { get; internal set; }

    /// <summary>True if the town takes a toll (see <see cref="CityStateDef.TollGold"/>).</summary>
    public bool Toll => Def.TollGold > 0;
    /// <summary>True if the unit (an id) is on the toll ground and its owner paid.</summary>
    public bool PaidToll(int id) => Paid.Contains(id);
    /// <summary>True if the unit (an id) is on the toll ground and its owner could not pay.</summary>
    public bool OwesToll(int id) => Unpaid.Contains(id);
    /// <summary>Ticks left of the player's feud with the town, or 0.</summary>
    public int FeudWith(int player) => player >= 0 && player < Feud.Length ? Feud[player] : 0;

    /// <summary>Columns from (x, z) to the town's bridge, counting diagonal steps as one (0 on it); int.MaxValue with no bridge.</summary>
    public int BridgeDistance(int x, int z) =>
        !HasBridge ? int.MaxValue : Math.Max(0, Math.Max(Math.Max(BridgeX0 - x, x - BridgeX1), Math.Max(BridgeZ0 - z, z - BridgeZ1)));

    /// <summary>
    /// True if a unit with its feet at (x, y, z) is on the town's toll ground: within <see cref="CityStateDef.GuardReach"/>
    /// columns of its square or its bridge, at the town's level. False for a town without a toll.
    /// </summary>
    public bool OnTollGround(int x, int y, int z) =>
        Toll && AtLevel(y) && Math.Min(ColumnDistance(x, z), BridgeDistance(x, z)) <= Def.GuardReach;

    /// <summary>The owner of the town's guards, totem and tribute: its holder, or the wild side while it has none.</summary>
    public int Side => Holder >= 0 ? Holder : Simulation.WildPlayer;

    /// <summary>The guards standing (unit ids, by post).</summary>
    public IEnumerable<int> Guards
    {
        get
        {
            foreach (int id in GuardIds)
                if (id != 0) yield return id;
        }
    }

    /// <summary>The tribute caravans on the road (unit ids, oldest first).</summary>
    public IReadOnlyList<int> Caravans => CaravanIds;

    /// <summary>True for a town dug out of a chamber of the deep caves (see <see cref="CityStateDef.Deep"/>).</summary>
    public bool Deep => Def.Deep;

    /// <summary>
    /// True if a unit with its feet at level y is at the town's level: anywhere for a town in the open, within
    /// <see cref="CityStateSystem.DeepLevels"/> levels of its square for one in the deep caves, so its guards and
    /// healing do not reach the ground far over it.
    /// </summary>
    public bool AtLevel(int y) => !Def.Deep || Math.Abs(y - Y) <= CityStateSystem.DeepLevels;

    /// <summary>True if column (x, z) is in the town's square.</summary>
    public bool Covers(int x, int z) => ColumnDistance(x, z) == 0;

    /// <summary>Columns from (x, z) to the town's square, counting diagonal steps as one (0 inside).</summary>
    public int ColumnDistance(int x, int z) =>
        Math.Max(0, Math.Max(Math.Abs(x - X), Math.Abs(z - Z)) - CityStateSystem.TownRadius);
}

/// <summary>
/// City-states (<see cref="ContentDb.CityStates"/>): towns of other races that no player builds. Every match has
/// a town of each (<see cref="PlaceAll"/>), a square of huts round a market with a totem on it, placed about as
/// far from every start area as from the others, with a lair of the city-state's monster just beyond it.
///
/// The town is won by force: whoever destroys its totem (a building of kind totem, attacked like any other)
/// holds the town, and the totem stands again <see cref="CityStateDef.TotemRespawnTicks"/> later, in the
/// holder's colour. Destroying a held town's totem takes the town over. The town's guards
/// (<see cref="CityStateDef.Guards"/>) stand round the market and belong to whoever holds it (the wild side
/// while nobody does): they fight soldiers that come within <see cref="CityStateDef.GuardReach"/> columns of
/// the square, never workers or caravans, so anyone can trade there, and they take no orders. A fallen guard
/// is replaced every <see cref="CityStateDef.GuardRefillTicks"/>. While the town has a holder it sends them a
/// tribute caravan every <see cref="CityStateDef.TributeTicks"/>, which walks the goods to the holder's first
/// town hall and goes home; and the holder alone can hire its mercenaries (<see cref="HireCommand"/>), which
/// step out by the market, and earns <see cref="CityStateDef.HolderBonus"/> percent more on their caravans' trade.
///
/// A caravan (a unit with the trader role) fetches a load of the good the town buys from its owner's
/// storage nearest the town, carries it to the market and brings back the good the town sells, to the
/// storage nearest the market (see EconomySystem.Trade.cs). What a load fetches grows with the route: the
/// straight-line distance between the storage it was loaded at and the market.
/// All numbers are starting guesses to be tuned in playtests.
/// </summary>
public sealed class CityStateSystem
{
    /// <summary>The town's square reaches this many columns from the market each way (15 by 15).</summary>
    public const int TownRadius = 7;
    /// <summary>Most levels the ground in a town's square may differ from the market's before it is levelled.</summary>
    public const int TownSlope = 2;
    /// <summary>Fewest columns between a town's square and a building or another town.</summary>
    public const int TownGap = 12;
    /// <summary>Fewest columns between a town's square and the edge of the map.</summary>
    public const int TownMargin = 8;
    /// <summary>How near the market a caravan has to be to trade (columns each way, and levels).</summary>
    public const int TradeReach = 3;
    /// <summary>The nearest and furthest a town's own lair is from its market (blocks); no other den may be nearer.</summary>
    public const int LairNear = 24;
    public const int LairFar = 36;
    /// <summary>Where caravans trade, beside the totem: this many columns south of the market.</summary>
    public const int TradeSpotOffset = 2;
    /// <summary>How many levels above or below a deep town's square its guards and healing reach (its hall is 9 high).</summary>
    public const int DeepLevels = 8;
    /// <summary>Fewest layers of ground left between a deep town's hall roof and the open above it.</summary>
    public const int DeepRoof = 4;
    /// <summary>Most cells walked looking for a way from a deep chamber up to the open (see <see cref="Surfaces"/>).</summary>
    public const int DeepWalk = 400_000;

    // Huts: one at each corner of the square, 4 by 4, walls 3 high, a roof and a ridge on top.
    private const int HutSize = 4;
    private const int HutWalls = 3;
    // Levels cleared above the square, and filled below it where the ground is lower.
    private const int ClearAbove = 8;
    private const int FillBelow = 4;
    // Where the guards stand, round the market (by guard entry, wrapping round).
    private static readonly (int X, int Z)[] GuardPosts =
    {
        (0, -5), (0, 5), (-5, 0), (5, 0), (-3, -3), (3, 3), (-3, 3), (3, -3),
    };
    // Where mercenaries step out, around the market.
    private static readonly (int X, int Z)[] MarketSpots =
    {
        (0, 2), (2, 0), (0, -2), (-2, 0), (2, 2), (-2, 2), (2, -2), (-2, -2),
    };

    private readonly Simulation _sim;
    private readonly List<CityState> _towns = new();
    private int _nextId = 1;

    public CityStateSystem(Simulation sim)
    {
        _sim = sim;
    }

    private VoxelWorld World => _sim.World;
    private UnitStore U => _sim.Units;
    private EconomySystem Economy => _sim.Economy;

    /// <summary>Every town, in the order they were placed.</summary>
    public IReadOnlyList<CityState> Towns => _towns;

    public CityState? Get(int id)
    {
        foreach (var town in _towns)
            if (town.Id == id) return town;
        return null;
    }

    /// <summary>The town whose square covers column (x, z), or null.</summary>
    public CityState? At(int x, int z)
    {
        foreach (var town in _towns)
            if (town.Covers(x, z)) return town;
        return null;
    }

    /// <summary>True if the columns from (x0, z0) to (x1, z1) are in, or right next to, a town's square (no building goes there).</summary>
    internal bool Crowds(int x0, int z0, int x1, int z1)
    {
        foreach (var town in _towns)
        {
            if (town.Deep) continue; // far under the ground: building over it is fine
            int r = TownRadius + 1;
            if (x0 <= town.X + r && x1 >= town.X - r && z0 <= town.Z + r && z1 >= town.Z - r) return true;
            if (town.HasBridge && x0 <= town.BridgeX1 + 1 && x1 >= town.BridgeX0 - 1 && z0 <= town.BridgeZ1 + 1 && z1 >= town.BridgeZ0 - 1) return true;
        }
        return false;
    }

    /// <summary>Hash of the towns (their blocks hash with the world).</summary>
    public ulong ComputeHash()
    {
        ulong h = 14695981039346656037UL;
        void Mix(long v) { h = (h ^ (ulong)v) * 1099511628211UL; }
        Mix(_nextId);
        Mix(_towns.Count);
        foreach (var town in _towns)
        {
            Mix(town.Id); Mix(town.Def.Index); Mix(town.X); Mix(town.Y); Mix(town.Z);
            Mix(town.Holder); Mix(town.TotemId); Mix(town.LastHitBy); Mix(town.RespawnTimer);
            foreach (int g in town.GuardIds) Mix(g);
            Mix(town.GuardTimer); Mix(town.TributeTimer);
            Mix(town.CaravanIds.Count);
            foreach (int c in town.CaravanIds) Mix(c);
            Mix(town.Ready); Mix(town.RefillTimer); Mix(town.LairId);
            Mix(town.BridgeX0); Mix(town.BridgeZ0); Mix(town.BridgeX1); Mix(town.BridgeZ1);
            Mix(town.Paid.Count);
            foreach (int id in town.Paid) Mix(id);
            Mix(town.Unpaid.Count);
            foreach (int id in town.Unpaid) Mix(id);
            foreach (int f in town.Feud) Mix(f);
            Mix(town.Tolls);
        }
        return h;
    }

    // --- Placing towns -----------------------------------------------------------------------------

    /// <summary>
    /// Match setup: a town for each city-state, in data order, each with a lair of its monster (if it has
    /// one) just beyond it. The town goes where the ground allows (see <see cref="SiteFits"/>), as near as
    /// it can to being equally far from every start area, and about 3/16 of the map's width from the
    /// middle of the starts, so it lies off the straight way between them. Call after placing the starting
    /// buildings, units and lairs. Returns the towns placed.
    /// </summary>
    public List<CityState> PlaceAll()
    {
        var placed = new List<CityState>();
        var defs = _sim.Content.CityStates;
        if (defs.Count == 0) return placed;
        var starts = _sim.Map.Starts;
        int cx = World.SizeX / 2, cz = World.SizeZ / 2;
        if (starts.Count > 0)
        {
            long sx = 0, sz = 0;
            foreach (var s in starts)
            {
                sx += s.X;
                sz += s.Z;
            }
            cx = (int)(sx / starts.Count);
            cz = (int)(sz / starts.Count);
        }
        var scored = Scored(cx, cz, out int want);
        foreach (var def in defs)
        {
            if (def.Deep)
            {
                if (PlaceDeep(def, cx, cz, want) is { } deep) placed.Add(deep);
                continue;
            }
            if (def.Crossing && PlaceCrossing(def, scored, want, cx, cz) is { } crossing)
            {
                placed.Add(crossing);
                PlaceLair(crossing, cx, cz);
                continue;
            }
            var sites = Spread(scored, want, cx, cz);
            // A town that keeps to a biome (the centaurs' open grassland) takes the best site in it; with none, the
            // best site anywhere.
            // A town on the coast (the merfolk's cove) likewise takes the best site by the open sea.
            for (int pass = def.Biome is null && !def.Coast ? 1 : 0; pass < 2; pass++)
            {
                CityState? town = null;
                foreach (var (x, z) in sites)
                {
                    if (pass == 0 && (def.Biome is { } biome ? !InBiome(x, z, biome) : !OnCoast(x, z))) continue;
                    if (pass == 0 && NearestTown(x, z) < want) continue;
                    if (!SiteFits(x, z)) continue;
                    town = Place(def, x, z)!;
                    placed.Add(town);
                    PlaceLair(town, cx, cz);
                    break;
                }
                if (town != null) break;
            }
        }
        return placed;
    }

    /// <summary>
    /// Columns a town could go at, every fourth column out from (cx, cz), each scored (lower is better) by how
    /// much further it is from the furthest start than from the nearest, counted four times, plus how far off
    /// the wanted distance from (cx, cz) it is (<paramref name="want"/>, about 3/16 of the map's width). Sites
    /// from half to two and a half times that distance out are scored; each also carries how far it is from
    /// the nearest start.
    /// </summary>
    private List<(long Score, int X, int Z, int Near)> Scored(int cx, int cz, out int want)
    {
        var w = World;
        var starts = _sim.Map.Starts;
        want = Math.Max(TownRadius + 1, Math.Min(w.SizeX, w.SizeZ) * 3 / 16);
        int edge = TownMargin + TownRadius;
        var scored = new List<(long Score, int X, int Z, int Near)>();
        for (int z = cz - (cz - edge) / 4 * 4; z < w.SizeZ - edge; z += 4)
        for (int x = cx - (cx - edge) / 4 * 4; x < w.SizeX - edge; x += 4)
        {
            if (x < edge || z < edge) continue;
            int r = IntMath.Distance(cx, cz, x, z);
            if (r < want / 2 || r > want * 5 / 2) continue;
            int near = int.MaxValue, far = 0;
            foreach (var s in starts)
            {
                int d = IntMath.Distance(s.X, s.Z, x, z);
                near = Math.Min(near, d);
                far = Math.Max(far, d);
            }
            int unfair = starts.Count > 1 ? far - near : 0;
            scored.Add((4L * unfair + Math.Abs(r - want), x, z, near));
        }
        return scored;
    }

    /// <summary>
    /// The scored sites best first for the next town: each also counts how much nearer it is than one and a
    /// half times <paramref name="want"/> to the nearest town already placed, so the towns spread round the middle
    /// rather than bunch. The first town is looked for at most one and a half times <paramref name="want"/>
    /// from (cx, cz) (the distance in <see cref="Scored"/>); later ones further out too, as the fair ground
    /// nearer the middle may be taken, but never nearer any start than one and a half times
    /// <paramref name="want"/>. Ties to the lower z, then x.
    /// </summary>
    private List<(int X, int Z)> Spread(List<(long Score, int X, int Z, int Near)> scored, int want, int cx, int cz)
    {
        var ranked = new List<(long Score, int X, int Z)>(scored.Count);
        foreach (var (score, x, z, near) in scored)
        {
            if (_towns.Count == 0 ? IntMath.Distance(cx, cz, x, z) > want * 3 / 2 : near < want * 3 / 2) continue;
            int nearest = int.MaxValue;
            foreach (var town in _towns) nearest = Math.Min(nearest, IntMath.Distance(town.X, town.Z, x, z));
            long crowd = nearest == int.MaxValue ? 0 : Math.Max(0, want * 3 / 2 - nearest);
            ranked.Add((score + crowd, x, z));
        }
        ranked.Sort((a, b) =>
        {
            int c = a.Score.CompareTo(b.Score);
            if (c != 0) return c;
            c = a.Z.CompareTo(b.Z);
            return c != 0 ? c : a.X.CompareTo(b.X);
        });
        var sites = new List<(int X, int Z)>(ranked.Count);
        foreach (var s in ranked) sites.Add((s.X, s.Z));
        return sites;
    }

    /// <summary>Columns from (x, z) to the nearest town's market in the open (deep towns left out), or int.MaxValue.</summary>
    private int NearestTown(int x, int z)
    {
        int nearest = int.MaxValue;
        foreach (var town in _towns)
            if (!town.Deep) nearest = Math.Min(nearest, IntMath.Distance(town.X, town.Z, x, z));
        return nearest;
    }

    /// <summary>How far round a market (columns) <see cref="InBiome"/> looks at the land.</summary>
    public const int BiomeReach = 24;

    /// <summary>
    /// True if the market at column (x, z) is in the biome and so is at least half the land within
    /// <see cref="BiomeReach"/> columns of it (every fourth column looked at), so the town stands well inside it.
    /// </summary>
    public bool InBiome(int x, int z, World.Biome biome)
    {
        var map = _sim.Map;
        if (map.BiomeAt(x, z) != biome) return false;
        int inside = 0, all = 0;
        for (int dz = -BiomeReach; dz <= BiomeReach; dz += 4)
        for (int dx = -BiomeReach; dx <= BiomeReach; dx += 4)
        {
            if (dx * dx + dz * dz > BiomeReach * BiomeReach) continue;
            int sx = x + dx, sz = z + dz;
            if ((uint)sx >= (uint)World.SizeX || (uint)sz >= (uint)World.SizeZ) continue;
            all++;
            if (map.BiomeAt(sx, sz) == biome) inside++;
        }
        return inside * 2 >= all;
    }

    /// <summary>How far (columns) from a coast town's market the open sea may be (<see cref="OnCoast"/>).</summary>
    public const int CoastReach = 28;

    /// <summary>
    /// True if the open sea (<see cref="Biome.Ocean"/>) lies within <see cref="CoastReach"/> columns of the market at
    /// column (x, z), straight out east, west, south or north (every second column looked at), and the market itself is
    /// on dry land: a site for a town on the shore.
    /// </summary>
    public bool OnCoast(int x, int z)
    {
        var map = _sim.Map;
        if (map.BiomeAt(x, z) == Biome.Ocean) return false;
        foreach (var (dx, dz) in Steps)
            for (int k = 2; k <= CoastReach; k += 2)
            {
                int sx = x + dx * k, sz = z + dz * k;
                if ((uint)sx >= (uint)World.SizeX || (uint)sz >= (uint)World.SizeZ) break;
                if (map.BiomeAt(sx, sz) == Biome.Ocean) return true;
            }
        return false;
    }

    /// <summary>
    /// Percent more food <paramref name="player"/>'s fishing huts bring in: the best <see cref="CityStateDef.FishingBonus"/>
    /// of the towns they hold (the merfolk's cove), or 0.
    /// </summary>
    public int FishingBonusOf(int player)
    {
        int best = 0;
        foreach (var town in _towns)
            if (town.Holder == player) best = Math.Max(best, town.Def.FishingBonus);
        return best;
    }

    /// <summary>
    /// True if a town can have its market at column (x, z): the square is well inside the map, its ground is
    /// within <see cref="TownSlope"/> levels of the market's and not under water, no tree stands in it or
    /// within two columns of it, it is <see cref="TownGap"/> columns from buildings and other towns, and no
    /// den is within <see cref="LairNear"/> blocks of the market (the dusk tree's warden half as far again).
    /// </summary>
    public bool SiteFits(int x, int z)
    {
        var w = World;
        int r = TownRadius;
        if (x - r < TownMargin || z - r < TownMargin || x + r >= w.SizeX - TownMargin || z + r >= w.SizeZ - TownMargin) return false;
        int y = Walk.GroundLevel(w, x, z);
        if (y < 2 || y + ClearAbove >= w.SizeY) return false;
        foreach (var b in _sim.Buildings.All)
        {
            int dx = Math.Max(0, Math.Max(b.X - (x + r), (x - r) - b.MaxX));
            int dz = Math.Max(0, Math.Max(b.Z - (z + r), (z - r) - b.MaxZ));
            if (Math.Max(dx, dz) < TownGap) return false;
        }
        foreach (var town in _towns)
            if (!town.Deep && Math.Max(Math.Abs(town.X - x), Math.Abs(town.Z - z)) - 2 * r < TownGap) return false;
        foreach (var lair in _sim.Monsters.Lairs)
        {
            // The dusk tree's warden is kept half as far again, so the town's guards and the tree's fights stay apart.
            int keep = lair.Monster.Lair?.DuskTree == true ? LairNear * 3 / 2 : LairNear;
            if (IntMath.Distance(x, z, lair.X, lair.Z) < keep) return false;
        }
        for (int cz = z - r - 2; cz <= z + r + 2; cz++)
        for (int cx = x - r - 2; cx <= x + r + 2; cx++)
        {
            for (int cy = y - TownSlope; cy <= y + ClearAbove; cy++)
                if (BlockInfo.IsTrunk(w.Get(cx, cy, cz))) return false;
            if (Math.Abs(cx - x) > r || Math.Abs(cz - z) > r) continue;
            int g = Walk.GroundLevel(w, cx, cz);
            if (g < 0 || Math.Abs(g - y) > TownSlope || w.Get(cx, g, cz) == BlockType.Water) return false;
        }
        return true;
    }

    /// <summary>
    /// Builds a town of the city-state with its market at column (x, z), on the ground there: the square is
    /// levelled (grass at the market's level, soil under it, air above), with a hut at each corner whose door
    /// faces the market, the totem on the market and the guards at their posts, all wild. Does not check the site (see <see cref="SiteFits"/>). Null if there is no ground at
    /// (x, z) or the square does not fit in the world. The square is at the ground's level there, or at level
    /// <paramref name="level"/> when given (a deep town's, in a cave chamber), and topped with <see cref="CityStateDef.Ground"/>.
    /// </summary>
    public CityState? Place(CityStateDef def, int x, int z, int level = -1)
    {
        var w = World;
        int r = TownRadius;
        if (!w.InBounds(x - r, 0, z - r) || !w.InBounds(x + r, 0, z + r)) return null;
        int y = level >= 0 ? level : Walk.GroundLevel(w, x, z);
        if (y < 2 || y + ClearAbove >= w.SizeY) return null;
        for (int cz = z - r; cz <= z + r; cz++)
        for (int cx = x - r; cx <= x + r; cx++)
        {
            for (int cy = Math.Max(1, y - FillBelow); cy < y - 1; cy++)
                if (!BlockInfo.IsSolid(w.Get(cx, cy, cz))) w.Set(cx, cy, cz, BlockType.Soil);
            if (w.Get(cx, y - 1, cz) != def.Ground) w.Set(cx, y - 1, cz, def.Ground);
            for (int cy = y; cy <= y + ClearAbove; cy++)
                if (w.Get(cx, cy, cz) != BlockType.Air) w.Set(cx, cy, cz, BlockType.Air);
        }
        for (int hz = 0; hz < 2; hz++)
        for (int hx = 0; hx < 2; hx++)
            Hut(def, hx == 0 ? x - r : x + r - HutSize + 1, y, hz == 0 ? z - r : z + r - HutSize + 1, hx == 0, hz == 0);
        var town = new CityState(_nextId++, def, x, y, z);
        _towns.Add(town);
        RaiseTotem(town);
        for (int k = 0; k < town.GuardIds.Length; k++) SpawnGuard(town, k);
        return town;
    }

    /// <summary>Columns a crossing's square may stand back from the river (the bank it levels between them is bridged too).</summary>
    public const int BankReach = 6;
    /// <summary>Longest bridge a crossing lays, in columns.</summary>
    public const int MaxCrossing = 40;
    /// <summary>Columns between a bridge's piers.</summary>
    private const int PierEvery = 4;
    /// <summary>Headroom cleared over a bridge's deck.</summary>
    private const int BridgeHeadroom = 4;

    /// <summary>
    /// Places a crossing (<see cref="CityStateDef.Crossing"/>): at the best scored site (<see cref="Spread"/>) that fits
    /// (<see cref="SiteFits"/>) and has the river within <see cref="BankReach"/> columns of its square's edge, straight out
    /// from the market, with a bridge to lay across it (<see cref="PlanCrossing"/>). The town is built and the bridge laid
    /// over the river from the square's edge. Null if no site has the river beside it (a map without one).
    /// </summary>
    private CityState? PlaceCrossing(CityStateDef def, List<(long Score, int X, int Z, int Near)> scored, int want, int cx, int cz)
    {
        foreach (var (x, z) in Spread(scored, want, cx, cz))
        {
            if (!SiteFits(x, z) || !PlanCrossing(x, z, out int dx, out int dz, out int length)) continue;
            var town = Place(def, x, z)!;
            LayBridge(town, dx, dz, length);
            return town;
        }
        return null;
    }

    /// <summary>
    /// True if a bridge can go out from the square of a town with its market at (x, z) (on the ground there), three
    /// columns wide along the row or column through the market, in direction (dx, dz): one of the four straight ways,
    /// tried east, west, south, north. The river (<see cref="RegionMap.BiomeAt"/>) must come within
    /// <see cref="BankReach"/> columns of the square's edge, and past it the ground must rise to within a climb of the
    /// square's level before <see cref="MaxCrossing"/> columns; nothing on the way may stand higher than a climb over
    /// that level, and no tree, building or other town may be in it. <paramref name="length"/> is how many columns
    /// out from the square the bridge runs, up to and with the first one on the far bank.
    /// </summary>
    public bool PlanCrossing(int x, int z, out int dx, out int dz, out int length)
    {
        var w = World;
        int y = Walk.GroundLevel(w, x, z);
        int r = TownRadius;
        foreach (var (sx, sz) in Steps)
        {
            (dx, dz) = (sx, sz);
            length = 0;
            if (y < 2 || y + BridgeHeadroom >= w.SizeY) return false;
            bool river = false, ok = true;
            for (int k = 1; k <= MaxCrossing && ok; k++)
            {
                bool wet = false, bank = true;
                for (int side = -1; side <= 1 && ok; side++)
                {
                    int bx = x + sx * (r + k) + sz * side, bz = z + sz * (r + k) + sx * side;
                    if (bx < 1 || bz < 1 || bx >= w.SizeX - 1 || bz >= w.SizeZ - 1) { ok = false; break; }
                    if (_sim.Map.BiomeAt(bx, bz) == Biome.River) wet = true;
                    int g = Walk.GroundLevel(w, bx, bz);
                    if (g > y + Walk.MaxClimb) ok = false; // a bank or hill in the way
                    if (g < y - Walk.MaxClimb || w.Get(bx, g - 1, bz) == BlockType.Water) bank = false;
                    for (int cy = Math.Max(0, y - 1); cy <= y + BridgeHeadroom; cy++)
                        if (BlockInfo.IsTrunk(w.Get(bx, cy, bz))) ok = false;
                    if (_sim.Buildings.AtColumn(bx, bz) != null) ok = false;
                    foreach (var town in _towns)
                        if (!town.Deep && town.ColumnDistance(bx, bz) <= TownGap / 2) ok = false;
                }
                if (!ok) break;
                if (wet) river = true;
                else if (!river && k > BankReach) break; // the river is not near enough this way
                else if (river && bank)
                {
                    length = k;
                    return true;
                }
            }
        }
        dx = dz = length = 0;
        return false;
    }

    /// <summary>
    /// Lays the town's bridge (see <see cref="PlanCrossing"/>): a deck of stone brick one under the square's level,
    /// three columns wide, from the square's edge <paramref name="length"/> columns out in direction (dx, dz), with the
    /// headroom over it cleared, and piers of stone brick under its edges every <see cref="PierEvery"/> columns, down to
    /// the river bed. It is ground, not a building: anyone walks over it, and it can be dug like any block.
    /// </summary>
    private void LayBridge(CityState town, int dx, int dz, int length)
    {
        var w = World;
        int r = TownRadius, y = town.Y;
        int ax = town.X + dx * (r + 1) - Math.Abs(dz), az = town.Z + dz * (r + 1) - Math.Abs(dx);
        int bx = town.X + dx * (r + length) + Math.Abs(dz), bz = town.Z + dz * (r + length) + Math.Abs(dx);
        town.BridgeX0 = Math.Min(ax, bx);
        town.BridgeX1 = Math.Max(ax, bx);
        town.BridgeZ0 = Math.Min(az, bz);
        town.BridgeZ1 = Math.Max(az, bz);
        for (int k = 1; k <= length; k++)
        for (int side = -1; side <= 1; side++)
        {
            int cx = town.X + dx * (r + k) + dz * side, cz = town.Z + dz * (r + k) + dx * side;
            w.Set(cx, y - 1, cz, BlockType.StoneBrick);
            for (int cy = y; cy <= y + BridgeHeadroom; cy++)
                if (w.Get(cx, cy, cz) != BlockType.Air) w.Set(cx, cy, cz, BlockType.Air);
            if (side == 0 || k % PierEvery != 0 || k == length) continue;
            for (int cy = y - 2; cy >= 1 && !BlockInfo.IsSolid(w.Get(cx, cy, cz)); cy--) w.Set(cx, cy, cz, BlockType.StoneBrick);
        }
    }

    /// <summary>
    /// Places a deep town (<see cref="CityStateDef.Deep"/>) in a chamber of the upper level of the deep caves
    /// (<see cref="RegionMap.Deeps"/>), not a monsters' nest's, at least one and a half times
    /// <paramref name="want"/> from every start, from which a unit can walk up to the open. The chambers are
    /// scored as surface towns' sites are (<see cref="Scored"/>, <see cref="Spread"/>): how much further from the
    /// furthest start than from the nearest, four times, how far off <paramref name="want"/> from (cx, cz), and how much
    /// nearer than one and a half times <paramref name="want"/> to a town already placed; ties to the earlier chamber.
    /// The square is dug out at the chamber's floor (see <see cref="DeepSiteFits"/>). Null if no chamber fits (a map
    /// without deep caves).
    /// </summary>
    private CityState? PlaceDeep(CityStateDef def, int cx, int cz, int want)
    {
        var deeps = _sim.Map.Deeps;
        int deep = Rts.Sim.World.RegionGenerator.DeepLayers(World.SizeY);
        var starts = _sim.Map.Starts;
        var ranked = new List<(long Score, int K)>();
        for (int k = 0; k < deeps.Count; k++)
        {
            if (k % Rts.Sim.World.RegionGenerator.DeepNestEvery == 0) continue; // a nest's chamber
            var (x, y, z) = deeps[k];
            if (y < deep / 2) continue; // the lower level
            int near = int.MaxValue, far = 0;
            foreach (var s in starts)
            {
                int d = IntMath.Distance(s.X, s.Z, x, z);
                near = Math.Min(near, d);
                far = Math.Max(far, d);
            }
            if (starts.Count > 0 && near < want * 3 / 2) continue;
            int unfair = starts.Count > 1 ? far - near : 0;
            int nearest = int.MaxValue;
            foreach (var town in _towns) nearest = Math.Min(nearest, IntMath.Distance(town.X, town.Z, x, z));
            long crowd = nearest == int.MaxValue ? 0 : Math.Max(0, want * 3 / 2 - nearest);
            ranked.Add((4L * unfair + Math.Abs(IntMath.Distance(cx, cz, x, z) - want) + crowd, k));
        }
        ranked.Sort((a, b) => a.Score != b.Score ? a.Score.CompareTo(b.Score) : a.K.CompareTo(b.K));
        foreach (var (_, k) in ranked)
        {
            var (x, y, z) = deeps[k];
            int floor = Walk.NearestStandLevel(World, x, z, y - 2, 3);
            if (floor < 0 || !DeepSiteFits(x, floor, z) || !Surfaces(x, floor, z)) continue;
            var town = Place(def, x, z, floor);
            // What is left of the giant mushrooms over the hall goes too, up to the cave's roof.
            int r = TownRadius;
            for (int mz = z - r; mz <= z + r; mz++)
            for (int mx = x - r; mx <= x + r; mx++)
            for (int my = floor + ClearAbove + 1; my < World.SizeY - 1; my++)
            {
                var b = World.Get(mx, my, mz);
                if (b is BlockType.MushroomStem or BlockType.MushroomCap) World.Set(mx, my, mz, BlockType.Air);
                else if (BlockInfo.IsSolid(b)) break; // the cave's roof
            }
            return town;
        }
        return null;
    }

    /// <summary>
    /// True if a deep town's square can be dug out with its market at (x, y, z): well inside the map, the open ground
    /// more than <see cref="DeepRoof"/> layers over the hall it digs, no water or lava beside the square or over it
    /// that its digging would let in (a pool in the middle is filled), no tree or giant mushroom, and no den within
    /// <see cref="LairNear"/> blocks.
    /// </summary>
    public bool DeepSiteFits(int x, int y, int z)
    {
        var w = World;
        int r = TownRadius;
        if (x - r < TownMargin || z - r < TownMargin || x + r >= w.SizeX - TownMargin || z + r >= w.SizeZ - TownMargin) return false;
        if (y < FillBelow + 2) return false;
        foreach (var lair in _sim.Monsters.Lairs)
            if (IntMath.Distance(x, z, lair.X, lair.Z) < LairNear) return false;
        for (int cz = z - r - 1; cz <= z + r + 1; cz++)
        for (int cx = x - r - 1; cx <= x + r + 1; cx++)
        {
            bool inside = Math.Abs(cx - x) <= r && Math.Abs(cz - z) <= r;
            for (int cy = y - FillBelow; cy <= y + ClearAbove + DeepRoof; cy++)
            {
                var b = w.Get(cx, cy, cz);
                if (BlockInfo.IsTrunk(b) && b != BlockType.MushroomStem) return false; // giant mushrooms are cleared
                bool liquid = b is BlockType.Water or BlockType.Lava;
                if (liquid && (!inside || cy >= y)) return false;
            }
            if (inside && Top(cx, cz) <= y + ClearAbove + DeepRoof) return false; // nearly open to the sky
        }
        return true;
    }

    /// <summary>The highest block in column (x, z) that is not air (the land, or water on it), or -1.</summary>
    private int Top(int x, int z)
    {
        for (int y = World.SizeY - 1; y >= 0; y--)
            if (World.Get(x, y, z) != BlockType.Air) return y;
        return -1;
    }

    /// <summary>
    /// True if a unit at (x, y, z) underground can walk up to the open (a column whose ground is no higher than
    /// where it stands), looking at up to <see cref="DeepWalk"/> cells. The walk only asks whether a cell was
    /// reached, so the order sets are kept in does not matter.
    /// </summary>
    private bool Surfaces(int x, int y, int z)
    {
        var w = World;
        long Key(int kx, int ky, int kz) => ((long)kx * w.SizeY + ky) * w.SizeZ + kz;
        var seen = new HashSet<long> { Key(x, y, z) };
        var open = new Queue<(int X, int Y, int Z)>();
        open.Enqueue((x, y, z));
        var ground = new Dictionary<int, int>(); // each column's ground level, worked out once
        while (open.Count > 0 && seen.Count < DeepWalk)
        {
            var (cx, cy, cz) = open.Dequeue();
            int column = cz * w.SizeX + cx;
            if (!ground.TryGetValue(column, out int top)) ground[column] = top = Walk.GroundLevel(w, cx, cz);
            if (top <= cy) return true;
            foreach (var (dx, dz) in Steps)
            {
                int nx = cx + dx, nz = cz + dz;
                if (!w.InBounds(nx, 0, nz) || !Walk.TryStep(w, cx, cz, cy, nx, nz, out int ny)) continue;
                if (seen.Add(Key(nx, ny, nz))) open.Enqueue((nx, ny, nz));
            }
        }
        return false;
    }

    private static readonly (int X, int Z)[] Steps = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    /// <summary>
    /// A hut with its corner at column (x, z), standing at level y: walls of the city-state's wall block, a
    /// door in the wall toward the market (on the <paramref name="west"/> or east side of it, in the
    /// <paramref name="north"/> or south wall), and a roof with a ridge.
    /// </summary>
    private void Hut(CityStateDef def, int x, int y, int z, bool west, bool north)
    {
        var w = World;
        int last = HutSize - 1;
        for (int j = 0; j < HutSize; j++)
        for (int i = 0; i < HutSize; i++)
        {
            if (i == 0 || j == 0 || i == last || j == last)
                for (int k = 0; k < HutWalls; k++) w.Set(x + i, y + k, z + j, def.Wall);
            w.Set(x + i, y + HutWalls, z + j, def.Roof);
            if (j > 0 && j < last) w.Set(x + i, y + HutWalls + 1, z + j, def.Roof);
        }
        int doorX = x + (west ? last - 1 : 1), doorZ = z + (north ? last : 0);
        w.Set(doorX, y, doorZ, BlockType.Door);
        w.Set(doorX, y + 1, doorZ, BlockType.Door);
    }

    /// <summary>
    /// Puts a lair of the city-state's monster (if it has one) between <see cref="LairNear"/> and
    /// <see cref="LairFar"/> blocks from the market, as near as the ground allows to straight out from
    /// (cx, cz) past the town, so the starts face the town before its lair.
    /// </summary>
    private void PlaceLair(CityState town, int cx, int cz)
    {
        if (town.Def.Lair is not { } monster) return;
        long dx = town.X - cx, dz = town.Z - cz;
        long len = IntMath.ISqrt(dx * dx + dz * dz);
        if (len == 0) (dx, dz, len) = (0, -1, 1);
        int mid = (LairNear + LairFar) / 2;
        int wantX = town.X + (int)(dx * mid / len), wantZ = town.Z + (int)(dz * mid / len);
        var spots = new List<(long Score, int X, int Z)>();
        for (int z = town.Z - LairFar; z <= town.Z + LairFar; z += 2)
        for (int x = town.X - LairFar; x <= town.X + LairFar; x += 2)
        {
            int d = IntMath.Distance(town.X, town.Z, x, z);
            if (d < LairNear || d > LairFar) continue;
            long ex = x - wantX, ez = z - wantZ;
            spots.Add((ex * ex + ez * ez, x, z));
        }
        spots.Sort((a, b) =>
        {
            int c = a.Score.CompareTo(b.Score);
            if (c != 0) return c;
            c = a.Z.CompareTo(b.Z);
            return c != 0 ? c : a.X.CompareTo(b.X);
        });
        foreach (var (_, x, z) in spots)
        {
            if (!_sim.Monsters.SiteFits(x, z)) continue;
            if (_sim.Monsters.Place(monster, x, z) is { } lair) town.LairId = lair.Id;
            return;
        }
    }

    // --- The tick ----------------------------------------------------------------------------------

    /// <summary>
    /// Each town: a fallen totem hands the town to whoever hit it last and stands again in time; fallen
    /// guards are replaced and idle ones walk back to their posts; the holder gets a tribute caravan in
    /// time, and caravans that have delivered go home; another mercenary is readied every
    /// <see cref="CityStateDef.RefillTicks"/> while it has fewer than its pool.
    /// </summary>
    public void Step()
    {
        foreach (var town in _towns)
        {
            StepTotem(town);
            StepGuards(town);
            StepToll(town);
            StepTribute(town);
            if (town.Ready >= town.Def.Pool) continue;
            if (++town.RefillTimer < town.Def.RefillTicks) continue;
            town.RefillTimer = 0;
            town.Ready++;
        }
    }

    // --- The totem ---------------------------------------------------------------------------------

    /// <summary>The town's standing totem, or null while it is down.</summary>
    public Building? Totem(CityState town) => town.TotemId != 0 ? _sim.Buildings.Get(town.TotemId) : null;

    /// <summary>The town whose totem the building is, or null.</summary>
    public CityState? TownOfTotem(Building b)
    {
        foreach (var town in _towns)
            if (town.TotemId == b.Id) return town;
        return null;
    }

    /// <summary>A unit of the player's hit the totem (in this tick's fighting).</summary>
    internal void TotemHit(Building b, int player)
    {
        if (player < 0 || player >= Simulation.WildPlayer || player == b.Owner) return;
        if (TownOfTotem(b) is not { } town) return;
        town.LastHitBy = player;
        StartFeud(town, player);
    }

    /// <summary>
    /// When the totem has fallen, the player who hit it last holds the town (if nobody did, it stays with the
    /// holder it had), and the totem stands again after <see cref="CityStateDef.TotemRespawnTicks"/>.
    /// </summary>
    private void StepTotem(CityState town)
    {
        if (town.Def.Totem == null) return;
        if (town.TotemId != 0)
        {
            if (_sim.Buildings.Get(town.TotemId) != null) return;
            town.TotemId = 0;
            town.RespawnTimer = town.Def.TotemRespawnTicks;
            if (town.LastHitBy >= 0 && town.LastHitBy != town.Holder) Conquer(town, town.LastHitBy);
            town.LastHitBy = -1;
        }
        if (town.RespawnTimer > 0)
        {
            town.RespawnTimer--;
            return;
        }
        RaiseTotem(town);
    }

    /// <summary>
    /// The player holds the town now: its guards are theirs, and it starts counting toward their first
    /// tribute. Tribute already on the road still goes to the old holder.
    /// </summary>
    private void Conquer(CityState town, int player)
    {
        int old = town.Holder;
        town.Holder = player;
        town.TributeTimer = 0;
        // The toll starts over: the new holder's units cross free, everyone else's pay them.
        town.Paid.Clear();
        town.Unpaid.Clear();
        if (player < town.Feud.Length) town.Feud[player] = 0;
        town.Tolls = 0;
        var u = U;
        foreach (int id in town.Guards)
        {
            int i = u.IndexOf(id);
            if (i < 0) continue;
            u.Owner[i] = player;
            u.ClearCombat(i);
            u.HitBy[i] = 0;
        }
        _sim.Economy.Discover(player, Rts.Sim.Content.Discovery.Town);
        _sim.Events.TownConquered(town, player, old);
    }

    /// <summary>
    /// Puts the town's totem up on the market, in its holder's hands: the footprint is cleared of rubble and
    /// anything else. False (and idle units there are sent off it) while a unit stands in the way.
    /// </summary>
    private bool RaiseTotem(CityState town)
    {
        var def = town.Def.Totem;
        if (def == null) return false;
        var w = World;
        int x0 = town.X - def.SizeX / 2, z0 = town.Z - def.SizeZ / 2;
        int x1 = x0 + def.SizeX - 1, z1 = z0 + def.SizeZ - 1;
        if (!w.InBounds(x0, 0, z0) || !w.InBounds(x1, 0, z1) || town.Y + def.Height >= w.SizeY) return false;
        var u = U;
        bool blocked = false;
        for (int i = 0; i < u.Count; i++)
        {
            int ux = u.X[i].FloorToInt(), uz = u.Z[i].FloorToInt();
            if (ux < x0 || ux > x1 || uz < z0 || uz > z1 || u.Y[i] < town.Y - 1 || u.Y[i] > town.Y + def.Height) continue;
            blocked = true;
            if (u.Order[i] == UnitOrder.Idle && u.Job[i] == UnitJob.None && TradeSpot(town, out int sx, out int sy, out int sz))
                _sim.SendUnit(i, sx, sz, sy);
        }
        if (blocked) return false;
        for (int z = z0; z <= z1; z++)
        for (int x = x0; x <= x1; x++)
        {
            if (!BlockInfo.IsSolid(w.Get(x, town.Y - 1, z))) w.Set(x, town.Y - 1, z, BlockType.Soil);
            for (int y = town.Y; y <= town.Y + def.Height; y++)
                if (w.Get(x, y, z) != BlockType.Air) w.Set(x, y, z, BlockType.Air);
        }
        var b = _sim.Buildings.Add(town.Side, def, x0, town.Y, z0);
        foreach (var t in def.Blocks) w.Set(b.X + t.X, b.Y + t.Y, b.Z + t.Z, t.Block);
        b.Placed = def.Blocks.Count;
        b.Work = def.BuildTicks;
        b.State = BuildingState.Active;
        town.TotemId = b.Id;
        town.LastHitBy = -1;
        return true;
    }

    // --- Guards ------------------------------------------------------------------------------------

    /// <summary>
    /// True if guard <paramref name="guard"/> fights unit <paramref name="t"/>: an enemy soldier (not a worker,
    /// a caravan or a unit that cannot fight) within <see cref="CityStateDef.GuardReach"/> columns of its
    /// town's square.
    /// </summary>
    internal bool Repels(int guard, int t)
    {
        var u = U;
        if (!_sim.Truces.Hostile(u.Owner[guard], u.Owner[t]) || Get(u.Town[guard]) is not { } town) return false;
        int owner = u.Owner[t];
        // A toll town fights a player's units on its toll ground that did not pay, or whose owner is at feud with it.
        if (town.Toll && owner >= 0 && owner < Simulation.WildPlayer && u.Town[t] == 0)
            return town.OnTollGround(u.X[t].FloorToInt(), u.Y[t], u.Z[t].FloorToInt())
                   && (town.Unpaid.Contains(u.Id[t]) || town.FeudWith(owner) > 0);
        var def = _sim.Content.Units[u.Def[t]];
        if (!def.CanAttack || def.Role is UnitRole.Worker or UnitRole.Trader) return false;
        return town.ColumnDistance(u.X[t].FloorToInt(), u.Z[t].FloorToInt()) <= town.Def.GuardReach && town.AtLevel(u.Y[t]);
    }

    /// <summary>Prunes fallen guards, replaces one in time, and walks idle guards back to their posts.</summary>
    private void StepGuards(CityState town)
    {
        var u = U;
        bool missing = false;
        for (int k = 0; k < town.GuardIds.Length; k++)
        {
            int id = town.GuardIds[k];
            int i = id != 0 ? u.IndexOf(id) : -1;
            if (i < 0)
            {
                town.GuardIds[k] = 0;
                missing = true;
                continue;
            }
            if (u.Order[i] != UnitOrder.Idle || u.Target[i] != 0 || u.Rout[i] != 0 || u.Subdued[i]) continue;
            var (px, pz) = Post(town, k);
            if (Math.Max(Math.Abs(u.X[i].FloorToInt() - px), Math.Abs(u.Z[i].FloorToInt() - pz)) <= 1) continue;
            int py = Walk.NearestStandLevel(World, px, pz, town.Y, 2);
            if (py < 0) continue;
            // Back to its post, fighting what it meets on the way.
            _sim.SendUnit(i, px, pz, py);
            u.Combat[i] = CombatOrder.AttackMove;
            (u.DestX[i], u.DestZ[i], u.DestY[i]) = (px, pz, py);
        }
        if (!missing)
        {
            town.GuardTimer = 0;
            return;
        }
        if (++town.GuardTimer < town.Def.GuardRefillTicks) return;
        town.GuardTimer = 0;
        for (int k = 0; k < town.GuardIds.Length; k++)
        {
            if (town.GuardIds[k] != 0) continue;
            SpawnGuard(town, k);
            return;
        }
    }

    private readonly HashSet<int> _onToll = new();
    private readonly int[] _gold = new int[Simulation.WildPlayer];

    /// <summary>
    /// A toll town's toll (<see cref="CityStateDef.TollGold"/>): feuds run down, and a player whose unit struck a guard
    /// this tick is at feud again. Each player's unit that comes onto the toll ground (not the holder's, not a town's
    /// guard or tribute caravan, not a beaten monster) is paid for once while it stays on it: its owner pays the toll from
    /// their storage nearest the market, and it goes to the holder's storage nearest the market (while the town has a
    /// holder). Units whose owner is at feud, or cannot pay, owe it until they leave or it is paid. The sets only ever
    /// change by membership, so their order does not matter.
    /// </summary>
    private void StepToll(CityState town)
    {
        if (!town.Toll) return;
        var u = U;
        for (int p = 0; p < town.Feud.Length; p++)
            if (town.Feud[p] > 0) town.Feud[p]--;
        foreach (int id in town.Guards)
        {
            int g = u.IndexOf(id);
            int by = g >= 0 && u.HitBy[g] != 0 ? u.IndexOf(u.HitBy[g]) : -1;
            if (by >= 0) StartFeud(town, u.Owner[by]);
        }
        _onToll.Clear();
        var cost = town.Def.TollCost;
        // Each player's gold, worked out once a tick (when first needed), as units that owe the toll try again every tick.
        Array.Fill(_gold, -1);
        for (int i = 0; i < u.Count; i++)
        {
            int p = u.Owner[i];
            if (p < 0 || p >= Simulation.WildPlayer || p == town.Holder || u.Town[i] != 0 || u.Subdued[i]) continue;
            if (!town.OnTollGround(u.X[i].FloorToInt(), u.Y[i], u.Z[i].FloorToInt())) continue;
            int id = u.Id[i];
            _onToll.Add(id);
            if (town.Paid.Contains(id)) continue;
            if (town.Feud[p] == 0 && _gold[p] < 0) _gold[p] = Economy.Stock(p, Good.Gold);
            if (town.Feud[p] == 0 && _gold[p] >= town.Def.TollGold && Economy.TrySpend(p, cost, town.X, town.Z))
            {
                _gold[p] -= town.Def.TollGold;
                town.Unpaid.Remove(id);
                town.Paid.Add(id);
                town.Tolls += town.Def.TollGold;
                if (town.Holder >= 0 && Economy.NearestStorage(town.Holder, town.X, town.Z) is { } store)
                    store.Stock[(int)Good.Gold] += town.Def.TollGold;
                continue;
            }
            if (town.Unpaid.Contains(id)) continue;
            bool owed = false;
            foreach (int o in town.Unpaid)
                if (u.IndexOf(o) is int k && k >= 0 && u.Owner[k] == p) owed = true;
            town.Unpaid.Add(id);
            if (!owed) _sim.Events.TollUnpaid(town, p, id);
        }
        town.Paid.RemoveWhere(id => !_onToll.Contains(id));
        town.Unpaid.RemoveWhere(id => !_onToll.Contains(id));
    }

    /// <summary>The player (if a player, and not the town's holder) is at feud with a toll town from now.</summary>
    private static void StartFeud(CityState town, int player)
    {
        if (!town.Toll || player < 0 || player >= town.Feud.Length || player == town.Holder) return;
        town.Feud[player] = town.Def.FeudTicks;
    }

    /// <summary>Guard post k's column.</summary>
    private static (int X, int Z) Post(CityState town, int k)
    {
        var (ox, oz) = GuardPosts[k % GuardPosts.Length];
        int ring = k / GuardPosts.Length; // more guards than posts stand a step further out
        return (town.X + ox + Math.Sign(ox) * ring, town.Z + oz + Math.Sign(oz) * ring);
    }

    /// <summary>A guard of entry k steps out at its post, for the town's holder (or the wild side).</summary>
    private void SpawnGuard(CityState town, int k)
    {
        var (x, z) = Post(town, k);
        int y = Walk.NearestStandLevel(World, x, z, town.Y, 2);
        if (y < 0) return;
        var def = town.Def.Guards[k];
        int owner = town.Side;
        int id = U.Spawn(owner, def.Index, Fix64.FromInt(x) + Fix64.Half, y, Fix64.FromInt(z) + Fix64.Half, Economy.MaxHp(owner, def));
        U.Town[U.IndexOf(id)] = town.Id;
        town.GuardIds[k] = id;
    }

    // --- Tribute -----------------------------------------------------------------------------------

    /// <summary>
    /// While the town has a holder, a tribute caravan sets out every <see cref="CityStateDef.TributeTicks"/>
    /// (if the holder has a town hall to take it to). Caravans that have delivered walk back to the market
    /// and are gone; one that lost its way with its load looks for the hall again, or goes home.
    /// </summary>
    private void StepTribute(CityState town)
    {
        var u = U;
        for (int k = town.CaravanIds.Count - 1; k >= 0; k--)
        {
            int i = u.IndexOf(town.CaravanIds[k]);
            if (i < 0)
            {
                town.CaravanIds.RemoveAt(k);
                continue;
            }
            if (u.Job[i] != UnitJob.None || u.Rout[i] != 0) continue;
            if (u.CarryAmount[i] > 0 && Capital(u.Owner[i]) is { } hall)
            {
                Economy.Deliver(i, hall);
                continue;
            }
            if (Economy.AtTown(i, town))
            {
                u.RemoveAt(i);
                town.CaravanIds.RemoveAt(k);
                continue;
            }
            if (u.Order[i] == UnitOrder.Idle && TradeSpot(town, out int x, out int y, out int z)) _sim.SendUnit(i, x, z, y);
        }
        var def = town.Def;
        if (town.Holder < 0 || def.TributeCaravan == null || def.TributeAmount <= 0)
        {
            town.TributeTimer = 0;
            return;
        }
        if (++town.TributeTimer < def.TributeTicks) return;
        town.TributeTimer = 0;
        if (Capital(town.Holder) is not { } capital || !TradeSpot(town, out int sx, out int sy, out int sz)) return;
        int id = U.Spawn(town.Holder, def.TributeCaravan.Index, Fix64.FromInt(sx) + Fix64.Half, sy, Fix64.FromInt(sz) + Fix64.Half,
            Economy.MaxHp(town.Holder, def.TributeCaravan));
        int c = u.IndexOf(id);
        u.Town[c] = town.Id;
        u.Carry[c] = def.TributeGood;
        u.CarryAmount[c] = def.TributeAmount;
        Economy.Deliver(c, capital);
        town.CaravanIds.Add(id);
        _sim.Events.TributeSent(town, town.Holder);
    }

    /// <summary>Where tribute goes: the player's first finished town hall (the lowest id), or null if they have none.</summary>
    /// <summary>
    /// The town held by <paramref name="player"/> whose healing reach (<see cref="CityStateDef.HealRange"/> columns
    /// beyond its square) takes in column (x, z) at level y, or null if none does. Its holder's units there heal as at home.
    /// </summary>
    public CityState? HealingTown(int player, int x, int y, int z)
    {
        foreach (var town in _towns)
            if (town.Holder == player && town.Def.HealRange >= 0 && town.ColumnDistance(x, z) <= town.Def.HealRange && town.AtLevel(y)) return town;
        return null;
    }

    public Building? Capital(int player)
    {
        foreach (var b in _sim.Buildings.All)
            if (b.Owner == player && b.Active && b.Def.Kind == BuildingKind.TownHall) return b;
        return null;
    }

    /// <summary>Where caravans trade and tribute sets out: a spot to stand <see cref="TradeSpotOffset"/> columns south of the market.</summary>
    public bool TradeSpot(CityState town, out int x, out int y, out int z)
    {
        x = town.X;
        z = town.Z + TradeSpotOffset;
        y = Walk.NearestStandLevel(World, x, z, town.Y, 2);
        return y >= 0;
    }

    // --- Trade -------------------------------------------------------------------------------------

    /// <summary>What a load sold at the town by a caravan of the player's fetches, carried along a route of that length (more for its holder).</summary>
    public int Earnings(CityState town, int player, int route)
    {
        long gain = (long)town.Def.Price * Math.Max(0, route) / 100;
        if (town.Holder == player) gain = gain * (100 + town.Def.HolderBonus) / 100;
        return (int)Math.Min(gain, int.MaxValue);
    }

    /// <summary>The length of a caravan's route from the storage building to the town's market: the straight-line distance from its middle.</summary>
    public static int Route(Building store, CityState town) =>
        (int)(IntMath.ISqrt(store.CentreDistance2(town.X, town.Z)) / 2);

    // --- Commands ----------------------------------------------------------------------------------

    /// <summary>
    /// The town's holder hires one of its mercenaries, if one is ready and they have room for it, food and the
    /// unit's cost (paid from their storage nearest the town). It steps out beside the market.
    /// </summary>
    internal void Apply(HireCommand c)
    {
        if (Get(c.City) is not { } town || town.Holder != c.PlayerId || town.Ready <= 0) return;
        if (!_sim.Content.TryGetUnit(c.Unit, out var def) || def.HiredFrom != town.Def) return;
        if (!CanHire(c.PlayerId)) return;
        if (!MarketSpot(town, out int x, out int y, out int z)) return;
        if (!Economy.TrySpend(c.PlayerId, def.Cost, town.X, town.Z)) return;
        U.Spawn(c.PlayerId, def.Index, Fix64.FromInt(x) + Fix64.Half, y, Fix64.FromInt(z) + Fix64.Half, Economy.MaxHp(c.PlayerId, def));
        town.Ready--;
    }

    /// <summary>True if the player has room for another unit (a hire needs it, as training does).</summary>
    public bool CanHire(int player) => Economy.Population(player) < Economy.Housing(player);

    /// <summary>Where a hired mercenary steps out: the first free spot to stand two columns from the market, starting with a different one each hire.</summary>
    private bool MarketSpot(CityState town, out int x, out int y, out int z)
    {
        for (int k = 0; k < MarketSpots.Length; k++)
        {
            var (ox, oz) = MarketSpots[(town.Ready + k) % MarketSpots.Length];
            x = town.X + ox;
            z = town.Z + oz;
            y = Walk.NearestStandLevel(World, x, z, town.Y, 2);
            if (y >= 0) return true;
        }
        x = y = z = -1;
        return false;
    }
}
