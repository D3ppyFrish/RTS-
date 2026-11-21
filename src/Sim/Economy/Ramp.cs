using System.Collections.Generic;

namespace Rts.Sim.Economy;

/// <summary>
/// A column of a planned ramp: units stand at <see cref="Ground"/> there now, and at <see cref="Top"/> once the
/// ramp is built, so the blocks from Ground to Top - 1 are filled with earth (none where Top is Ground). Room is
/// false where something is in the way of the fill or of the headroom above it (a building, a city-state's town,
/// ground or a trunk overhead).
/// </summary>
public readonly record struct RampSpan(int X, int Z, int Ground, int Top, bool Room)
{
    /// <summary>Blocks of earth this column takes.</summary>
    public int Blocks => Top - Ground;
}

/// <summary>
/// A ramp a player's workers and sappers are heaping up (<see cref="RampCommand"/>): a one-column-wide slope of
/// rammed earth that rises a level a column from its foot to the height of what it leans on (a cliff, a hill, the
/// top of a wall), so units walk up where they could not climb. Its columns are filled in order from the foot,
/// each from the bottom up, so the builders always stand on finished ramp. Once it is built it is ordinary ground:
/// anyone walks it, the enemy too, and anyone can dig it away.
/// </summary>
public sealed class Ramp
{
    public int Id { get; }
    public int Owner { get; }
    /// <summary>The columns to fill, from the foot up (columns with nothing to fill left out).</summary>
    public IReadOnlyList<RampSpan> Columns => _columns;
    /// <summary>The level units stand at on the column the ramp leans on.</summary>
    public int TopY { get; }

    private readonly List<RampSpan> _columns;

    internal Ramp(int id, int owner, List<RampSpan> columns, int topY)
    {
        Id = id;
        Owner = owner;
        _columns = columns;
        TopY = topY;
    }

    /// <summary>True if column (x, z) is one of the ramp's.</summary>
    public bool Covers(int x, int z)
    {
        foreach (var c in _columns)
            if (c.X == x && c.Z == z) return true;
        return false;
    }

    /// <summary>True if block y of the span is still open (air, water or leaves) and so still to be filled.</summary>
    internal static bool Open(VoxelWorld w, int x, int y, int z) =>
        w.Get(x, y, z) is var b && (b is BlockType.Air or BlockType.Water || BlockInfo.IsLeaves(b));

    /// <summary>
    /// The next block to fill: the lowest open one of the first column (from the foot) that has any. False when
    /// the ramp is done. Blocks something else filled meanwhile (rubble, say) count as filled.
    /// </summary>
    public bool Next(VoxelWorld w, out int x, out int y, out int z)
    {
        foreach (var c in _columns)
            for (int yy = c.Ground; yy < c.Top; yy++)
                if (Open(w, c.X, yy, c.Z))
                {
                    (x, y, z) = (c.X, yy, c.Z);
                    return true;
                }
        x = y = z = -1;
        return false;
    }

    /// <summary>Blocks still to fill.</summary>
    public int Left(VoxelWorld w)
    {
        int n = 0;
        foreach (var c in _columns)
            for (int y = c.Ground; y < c.Top; y++)
                if (Open(w, c.X, y, c.Z)) n++;
        return n;
    }

    internal void Mix(ref ulong h)
    {
        void M(ref ulong hh, long v) => hh = (hh ^ (ulong)v) * 1099511628211UL;
        M(ref h, Id);
        M(ref h, Owner);
        M(ref h, TopY);
        foreach (var c in _columns)
        {
            M(ref h, c.X);
            M(ref h, c.Z);
            M(ref h, c.Ground);
            M(ref h, c.Top);
        }
    }
}
