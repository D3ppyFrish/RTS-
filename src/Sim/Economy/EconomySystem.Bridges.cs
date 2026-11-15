using System;
using System.Collections.Generic;
using Rts.Sim.Units;

namespace Rts.Sim.Economy;

/// <summary>A column of a planned bridge: where its deck block goes, and whether there is room for it.</summary>
public readonly record struct BridgeSpan(int X, int Z, int DeckY, bool Room);

/// <summary>
/// Bridges (<see cref="BridgeCommand"/>): a level deck of one-column sites laid along a line, across deep water,
/// a chasm or a trench, at the height of the higher end. Each column is its own building of one deck block, so
/// a blow knocks it out and it falls, and the way across is cut. Workers build a deck column from ground or
/// finished deck beside it, so a bridge goes out from its ends, a column at a time. Anyone can walk over it.
/// </summary>
public sealed partial class EconomySystem
{
    /// <summary>Most deck columns one bridge command lays.</summary>
    public const int MaxBridge = 32;

    // The neighbours a builder stands on, straight ones first.
    private static readonly (int X, int Z)[] Around =
        { (0, -1), (-1, 0), (1, 0), (0, 1), (-1, -1), (1, -1), (-1, 1), (1, 1) };
    // The levels (from the deck block's) a builder stands at, best first: on a level with the deck's top.
    private static readonly int[] BuilderLevels = { 1, 2, 0, -1 };

    private readonly List<BridgeSpan> _bridgePlan = new();

    /// <summary>
    /// Plans a bridge from column (x1, z1) to (x2, z2) (the line <see cref="WallLine"/> gives) into
    /// <paramref name="deck"/>: both ends must be ground to stand on, at most a climb apart, and the deck runs
    /// level at the higher end's standing level. A column of the line gets a deck block (one under that level)
    /// where nobody could walk across at about that height: deep water, a drop of two or more, a trench.
    /// Ground at about the deck's height, or higher, is left as it is. At most <see cref="MaxBridge"/> deck
    /// columns. Room is false where the deck block or the headroom above it is taken (by ground, a building,
    /// a tree trunk) or a city-state's town is there. Returns the standing level on the deck, or -1 (and no
    /// columns) if the ends are not ground or too far apart in height. The game previews the same plan.
    /// </summary>
    public int PlanBridge(int x1, int z1, int x2, int z2, List<BridgeSpan> deck)
    {
        deck.Clear();
        var w = World;
        if (!w.InBounds(x1, 1, z1) || !w.InBounds(x2, 1, z2)) return -1;
        int a = Walk.GroundLevel(w, x1, z1), b = Walk.GroundLevel(w, x2, z2);
        if (a < 0 || b < 0 || Math.Abs(a - b) > Walk.MaxClimb) return -1;
        int stand = Math.Max(a, b), deckY = stand - 1;
        if (stand + Walk.Clearance > w.SizeY - 1) return -1;
        foreach (var (x, z) in WallLine(x1, z1, x2, z2))
        {
            if (deck.Count == MaxBridge) break;
            int g = Walk.GroundLevel(w, x, z);
            if (g >= stand - Walk.MaxClimb) continue; // ground to walk on at about the deck's height
            bool room = x >= 1 && z >= 1 && x <= w.SizeX - 2 && z <= w.SizeZ - 2
                        && Buildings.AtColumn(x, z) == null && !_sim.Cities.Crowds(x, z, x, z)
                        && w.Get(x, deckY, z) is var d && (d is BlockType.Air or BlockType.Water || BlockInfo.IsLeaves(d));
            for (int y = stand; room && y < stand + Walk.Clearance; y++)
                room = w.Get(x, y, z) is var r && (r == BlockType.Air || BlockInfo.IsLeaves(r));
            deck.Add(new BridgeSpan(x, z, deckY, room));
        }
        return stand;
    }

    internal void Apply(BridgeCommand c)
    {
        if (!_sim.Content.TryGetBuilding(c.Building, out var def) || !def.IsBridge || !Unlocked(c.PlayerId, def)) return;
        if (PlanBridge(c.X1, c.Z1, c.X2, c.Z2, _bridgePlan) < 0) return;
        var w = World;
        var sites = new List<Building>();
        foreach (var span in _bridgePlan)
        {
            if (!span.Room) continue;
            if (!TrySpend(c.PlayerId, def.Cost, span.X, span.Z)) break;
            for (int y = span.DeckY; y <= span.DeckY + Walk.Clearance; y++)
                if (BlockInfo.IsLeaves(w.Get(span.X, y, span.Z))) w.Set(span.X, y, span.Z, BlockType.Air);
            sites.Add(Buildings.Add(c.PlayerId, def, span.X, span.DeckY, span.Z));
        }
        // Each worker starts on the column nearest to it that can be built now: at the end on its side.
        sites.RemoveAll(b => !BridgeAccess(b, out _, out _, out _));
        if (sites.Count == 0) return;
        foreach (int i in Workers(c))
        {
            int x = U.X[i].FloorToInt(), z = U.Z[i].FloorToInt();
            Building? best = null;
            foreach (var b in sites)
                if (best == null || b.ColumnDistance(x, z) < best.ColumnDistance(x, z)) best = b;
            StartJob(i, UnitJob.Build, best!.Id);
        }
    }

    /// <summary>
    /// Where a worker stands to build bridge column b: a column beside it (straight before diagonal) with ground
    /// or finished deck to stand on about level with the deck. False while there is none, as for a column out
    /// over the water before the deck next to it is built.
    /// </summary>
    public bool BridgeAccess(Building b, out int x, out int z, out int y)
    {
        var w = World;
        foreach (var (dx, dz) in Around)
        {
            int cx = b.X + dx, cz = b.Z + dz;
            if (!w.InBounds(cx, 1, cz)) continue;
            var other = Buildings.AtColumn(cx, cz);
            if (other != null && !(other.Def.IsBridge && other.Active)) continue;
            foreach (int dy in BuilderLevels)
            {
                if (!Walk.CanStand(w, cx, b.Y + dy, cz)) continue;
                (x, z, y) = (cx, cz, b.Y + dy);
                return true;
            }
        }
        x = z = y = -1;
        return false;
    }

    /// <summary>
    /// The player's bridge site nearest to column (x, z), within <see cref="WallReach"/> of it, that can be built
    /// now (see <see cref="BridgeAccess"/>); of those as near, the one with the fewest workers on it, then the
    /// lowest id. Nearest first, since the other end of a bridge may be across the water. Null if there is none.
    /// </summary>
    private Building? NextBridgeSite(int player, int x, int z)
    {
        Building? best = null;
        int bestWorkers = 0, bestD = 0;
        foreach (var b in Buildings.All)
        {
            if (b.Owner != player || b.State != BuildingState.Site || !b.Def.IsBridge) continue;
            int d = b.ColumnDistance(x, z);
            if (d > WallReach || !BridgeAccess(b, out _, out _, out _)) continue;
            int workers = WorkersAt(b, UnitJob.Build);
            if (best != null && (d > bestD || (d == bestD && workers >= bestWorkers))) continue;
            (best, bestWorkers, bestD) = (b, workers, d);
        }
        return best;
    }
}
