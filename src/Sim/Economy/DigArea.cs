using System;
using System.Collections.Generic;
using Rts.Sim.Units;

namespace Rts.Sim.Economy;

/// <summary>
/// An area a player's workers and sappers dig out (<see cref="DigAreaCommand"/>): every natural block in
/// columns [X1, X2] × [Z1, Z2] from level <see cref="FloorY"/> up, so units end up standing at FloorY. It is
/// dug from the top down, a layer at a time, and a stair one column wide is left along one edge so the
/// diggers can always climb out: along the longer side, from the X1 (or Z1) end, each column one level
/// lower than the one before, down to the floor.
/// </summary>
public sealed class DigArea
{
    /// <summary>Most columns on a side, and most levels deep below the area's highest ground.</summary>
    public const int MaxSide = 64;
    public const int MaxDepth = 16;

    public int Id { get; }
    public int Owner { get; }
    public int X1 { get; }
    public int Z1 { get; }
    public int X2 { get; }
    public int Z2 { get; }
    /// <summary>The level units stand at once the area is dug out; blocks below it stay.</summary>
    public int FloorY { get; }
    /// <summary>The highest ground level (where a unit stands) in the area when it was marked out.</summary>
    public int TopY { get; }
    /// <summary>The stair runs along X (in row Z1), or along Z (in column X1) when the area is deeper in Z.</summary>
    public bool StairAlongX { get; }

    // Blocks nobody could get to; left alone (keys from Harvest.Key). Sorted, so the hash is stable.
    internal readonly SortedSet<long> Skipped = new();

    /// <summary>Blocks the diggers have given up on, because nobody could get to them.</summary>
    public int SkippedCount => Skipped.Count;

    internal DigArea(int id, int owner, int x1, int z1, int x2, int z2, int floorY, int topY)
    {
        Id = id;
        Owner = owner;
        X1 = x1;
        Z1 = z1;
        X2 = x2;
        Z2 = z2;
        FloorY = floorY;
        TopY = topY;
        StairAlongX = x2 - x1 >= z2 - z1;
    }

    public bool Contains(int x, int z) => x >= X1 && x <= X2 && z >= Z1 && z <= Z2;

    /// <summary>
    /// The lowest level dug in a column: the floor, or on the stair a level higher for each column nearer
    /// its top end.
    /// </summary>
    public int LowestDug(int x, int z)
    {
        bool stair = StairAlongX ? z == Z1 : x == X1;
        if (!stair) return FloorY;
        int along = StairAlongX ? x - X1 : z - Z1;
        return Math.Max(FloorY, TopY - along);
    }

    /// <summary>
    /// True if block (x, y, z) is one the area still wants dug: natural ground, ore, a tree's trunk (by its
    /// base; the tree falls whole) or a bush, in the area, at or above the column's lowest dug level, and not
    /// holding up a building or road. Leaves go with their tree.
    /// </summary>
    public bool Wants(VoxelWorld w, int x, int y, int z)
    {
        if (!Contains(x, z) || y < LowestDug(x, z)) return false;
        var b = w.Get(x, y, z);
        if (!Harvest.CanGather(b) || BlockInfo.IsLeaves(b)) return false;
        if (BlockInfo.IsTrunk(b) && BlockInfo.IsTrunk(w.Get(x, y - 1, z))) return false; // not a trunk's base
        return !BlockInfo.IsBuilt(w.Get(x, y + 1, z));
    }

    /// <summary>The highest level that could hold a block the area wants (trees stand on the ground).</summary>
    internal int ScanTop => TopY;

    internal enum Pick { Found, Busy, Done }

    /// <summary>
    /// The next block for a digger at (ux, uy, uz): in the highest layer with blocks left to dig, the nearest
    /// one nobody has claimed and nobody has given up on. Busy if every block left in that layer is claimed;
    /// Done if nothing is left.
    /// </summary>
    internal Pick Next(VoxelWorld w, HashSet<long> claimed, int ux, int uy, int uz, out int bx, out int by, out int bz)
    {
        bx = by = bz = -1;
        for (int y = ScanTop; y >= FloorY; y--)
        {
            bool any = false;
            long best = long.MaxValue;
            for (int z = Z1; z <= Z2; z++)
            for (int x = X1; x <= X2; x++)
            {
                if (!Wants(w, x, y, z)) continue;
                long key = Harvest.Key(w, x, y, z);
                if (Skipped.Contains(key)) continue;
                any = true;
                if (claimed.Contains(key)) continue;
                long dx = x - ux, dz = z - uz, dy = y - uy;
                long d2 = dx * dx + dz * dz + dy * dy;
                if (d2 >= best) continue; // ties keep the first in (z, x) order
                best = d2;
                (bx, by, bz) = (x, y, z);
            }
            if (!any) continue;
            return best == long.MaxValue ? Pick.Busy : Pick.Found;
        }
        return Pick.Done;
    }

    internal void Mix(ref ulong h)
    {
        void M(ref ulong hh, long v) => hh = (hh ^ (ulong)v) * 1099511628211UL;
        M(ref h, Id);
        M(ref h, Owner);
        M(ref h, X1);
        M(ref h, Z1);
        M(ref h, X2);
        M(ref h, Z2);
        M(ref h, FloorY);
        M(ref h, TopY);
        foreach (long k in Skipped) M(ref h, k);
    }
}
