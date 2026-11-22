using System.Collections.Generic;

namespace Rts.Sim.Economy;

/// <summary>
/// A column of a planned dam: its bed (the level units would stand at there now, under any water) and its top (the
/// level units stand at on the finished dam), so the blocks from Bed to Top - 1 are filled. Room is false where
/// something is in the way of the fill or of the headroom above it (a building, a city-state's town, solid ground
/// or a trunk), or the column takes more than <see cref="EconomySystem.MaxDamFill"/> blocks.
/// </summary>
public readonly record struct DamSpan(int X, int Z, int Bed, int Top, bool Room)
{
    /// <summary>Blocks this column takes.</summary>
    public int Blocks => Top - Bed;
}

/// <summary>
/// A dam a player's workers and sappers build across water or a dug channel (<see cref="DamCommand"/>): a wall of
/// rammed earth from the bed up to the level of its higher bank, a column wide, that holds the water back and that
/// units walk across on top. Its deepest column is a <b>sluice</b> of timber boards: once the dam is finished its
/// owner opens it (<see cref="SluiceCommand"/>), and the boards under the top one come out, so the water pours
/// through into lower ground on the far side; shutting it puts them back. The top board stays, so the way across
/// stays open. The earth is plain ground: anyone digs it away, which breaches the dam. The boards are not dug.
/// </summary>
public sealed class Dam
{
    public int Id { get; }
    public int Owner { get; }
    /// <summary>The columns to fill, from the end the dam was drawn from (columns with nothing to fill left out).</summary>
    public IReadOnlyList<DamSpan> Columns => _columns;
    /// <summary>Which of <see cref="Columns"/> is the sluice, or -1 if the dam has none (no column two or more deep).</summary>
    public int Sluice { get; }
    /// <summary>True once every block is in: the sluice can be opened and shut from then on.</summary>
    public bool Done { get; internal set; }
    /// <summary>True while the sluice is open (its boards under the top one taken out).</summary>
    public bool Open { get; internal set; }

    private readonly List<DamSpan> _columns;

    internal Dam(int id, int owner, List<DamSpan> columns, int sluice)
    {
        Id = id;
        Owner = owner;
        _columns = columns;
        Sluice = sluice;
    }

    /// <summary>True if column (x, z) is one of the dam's.</summary>
    public bool Covers(int x, int z)
    {
        foreach (var c in _columns)
            if (c.X == x && c.Z == z) return true;
        return false;
    }

    /// <summary>The sluice's column, if the dam has one.</summary>
    public bool SluiceColumn(out DamSpan span)
    {
        span = Sluice >= 0 ? _columns[Sluice] : default;
        return Sluice >= 0;
    }

    /// <summary>What block goes into column k: timber boards in the sluice, rammed earth elsewhere.</summary>
    public BlockType BlockOf(int k) => k == Sluice ? BlockType.Planks : BlockType.Soil;

    /// <summary>
    /// The next block to fill: the lowest open one of the first column (from the end it was drawn from) that has
    /// any, and which column that is. False when the dam is done. Blocks something else filled meanwhile (rubble,
    /// say) count as filled.
    /// </summary>
    public bool Next(VoxelWorld w, out int x, out int y, out int z, out int column)
    {
        for (int k = 0; k < _columns.Count; k++)
        {
            var c = _columns[k];
            for (int yy = c.Bed; yy < c.Top; yy++)
                if (Ramp.Open(w, c.X, yy, c.Z))
                {
                    (x, y, z, column) = (c.X, yy, c.Z, k);
                    return true;
                }
        }
        x = y = z = column = -1;
        return false;
    }

    /// <summary>Blocks still to fill.</summary>
    public int Left(VoxelWorld w)
    {
        int n = 0;
        foreach (var c in _columns)
            for (int y = c.Bed; y < c.Top; y++)
                if (Ramp.Open(w, c.X, y, c.Z)) n++;
        return n;
    }

    internal void Mix(ref ulong h)
    {
        void M(ref ulong hh, long v) => hh = (hh ^ (ulong)v) * 1099511628211UL;
        M(ref h, Id);
        M(ref h, Owner);
        M(ref h, Sluice);
        M(ref h, Done ? 1 : 0);
        M(ref h, Open ? 1 : 0);
        foreach (var c in _columns)
        {
            M(ref h, c.X);
            M(ref h, c.Z);
            M(ref h, c.Bed);
            M(ref h, c.Top);
        }
    }
}
