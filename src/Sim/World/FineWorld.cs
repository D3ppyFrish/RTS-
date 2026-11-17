using System;
using System.Collections.Generic;

namespace Rts.Sim.World;

/// <summary>
/// The finer world (see <see cref="FineTerrain"/>) of a world that changes. Tell it of every block that
/// changes (<see cref="Changed"/>) and let it catch up (<see cref="Refresh"/>), and its blocks are what
/// <see cref="FineTerrain.Build"/> would make of the world as it is now: the ground round a new pit gets its
/// shoulder, a step that is built on is filled in, and it comes back when what stood on it is gone.
///
/// A change to the ground's height can move the steps of the ground within <see cref="FineTerrain.Reach"/>
/// of it, so those columns are worked out again; any other change only those right beside it.
/// </summary>
public sealed class FineWorld
{
    /// <summary>The world in whole blocks.</summary>
    public VoxelWorld Coarse { get; }

    /// <summary>The world in half-size blocks.</summary>
    public VoxelWorld Blocks { get; }

    private readonly int[] _heights;
    /// <summary>Per block column, which of its four fine columns are lowered now (see <see cref="FineTerrain.LoweredAt"/>).</summary>
    private readonly byte[] _lowered;
    private readonly bool[] _stale;
    private readonly List<int> _staleColumns = new();

    public FineWorld(VoxelWorld coarse)
    {
        Coarse = coarse;
        Blocks = coarse.Upscaled();
        _heights = FineTerrain.Heights(coarse);
        _lowered = new byte[coarse.SizeX * coarse.SizeZ];
        _stale = new bool[coarse.SizeX * coarse.SizeZ];
        for (int z = 0; z < coarse.SizeZ; z++)
        for (int x = 0; x < coarse.SizeX; x++)
            Step(x, z, null);
    }

    /// <summary>How many block columns are waiting to be worked out again (see <see cref="Refresh"/>).</summary>
    public int Stale => _staleColumns.Count;

    /// <summary>
    /// Block (x, y, z) of <see cref="Coarse"/> has changed: its eight half-size blocks become what it is now,
    /// and the columns whose steps it may have moved wait for <see cref="Refresh"/>.
    /// </summary>
    public void Changed(int x, int y, int z)
    {
        int sx = Coarse.SizeX, at = z * sx + x;
        int was = _heights[at];
        var b = Coarse.Get(x, y, z);
        if (y == was) _lowered[at] = 0;
        FineTerrain.Fill(Blocks, x, y, z, b);
        int now = was;
        if (y > was && FineTerrain.IsLevel(b)) now = y;
        else if (y == was && !FineTerrain.IsLevel(b))
        {
            now = y - 1;
            while (now >= 0 && !FineTerrain.IsLevel(Coarse.Get(x, now, z))) now--;
        }
        if (now == was)
        {
            MarkStale(x, z, 1);
            return;
        }
        // The ground that was on top is whole again: it is covered now, or it is gone.
        if (was >= 0 && _lowered[at] != 0)
        {
            var under = Coarse.Get(x, was, z);
            for (int s = 0; s < 4; s++)
                if ((_lowered[at] & (1 << s)) != 0) Blocks.Set(2 * x + (s & 1), 2 * was + 1, 2 * z + (s >> 1), under);
            _lowered[at] = 0;
        }
        _heights[at] = now;
        MarkStale(x, z, 1);
        // To ground in reach this column is lower, level or higher ground, and only that: its steps can
        // move only where that has changed, which is ground no lower and no higher than this was and is.
        MarkStale(x, z, FineTerrain.Reach, Math.Min(was, now), Math.Max(was, now));
    }

    /// <summary>Marks the columns within <paramref name="reach"/> of (x, z) whose height is from <paramref name="lowest"/> to <paramref name="highest"/>.</summary>
    private void MarkStale(int x, int z, int reach, int lowest = int.MinValue, int highest = int.MaxValue)
    {
        int sx = Coarse.SizeX, sz = Coarse.SizeZ;
        for (int nz = Math.Max(0, z - reach); nz <= Math.Min(sz - 1, z + reach); nz++)
        for (int nx = Math.Max(0, x - reach); nx <= Math.Min(sx - 1, x + reach); nx++)
        {
            int at = nz * sx + nx;
            if (_stale[at] || _heights[at] < lowest || _heights[at] > highest) continue;
            _stale[at] = true;
            _staleColumns.Add(at);
        }
    }

    /// <summary>
    /// Works out again the steps of the columns a change may have moved: all of them, or the
    /// <paramref name="most"/> that have waited longest (the rest wait for the next call).
    /// <paramref name="stepped"/> is told of each whole block (x, y, z) whose half-size blocks changed.
    /// Returns how many did.
    /// </summary>
    public int Refresh(Action<int, int, int>? stepped = null, int most = int.MaxValue)
    {
        int sx = Coarse.SizeX, changed = 0, count = Math.Min(most, _staleColumns.Count);
        for (int i = 0; i < count; i++)
        {
            int at = _staleColumns[i];
            _stale[at] = false;
            if (Step(at % sx, at / sx, stepped)) changed++;
        }
        _staleColumns.RemoveRange(0, count);
        return changed;
    }

    /// <summary>Makes the steps of block column (x, z) what they should be now. True if they changed.</summary>
    private bool Step(int x, int z, Action<int, int, int>? stepped)
    {
        int at = z * Coarse.SizeX + x;
        int was = _lowered[at], now = FineTerrain.LoweredAt(Coarse, _heights, x, z);
        if (now == was) return false;
        int g = _heights[at];
        var ground = Coarse.Get(x, g, z);
        for (int s = 0; s < 4; s++)
        {
            int bit = 1 << s;
            if ((was & bit) == (now & bit)) continue;
            Blocks.Set(2 * x + (s & 1), 2 * g + 1, 2 * z + (s >> 1), (now & bit) != 0 ? BlockType.Air : ground);
        }
        _lowered[at] = (byte)now;
        stepped?.Invoke(x, g, z);
        return true;
    }

    /// <summary>
    /// True if the ground under whole-block position (x, y, z), a block standing on the ground, is a
    /// quarter of a metre lower than the rules have it at fine column (fx, fz).
    /// </summary>
    public bool IsLowered(int fx, int y, int fz) => FineTerrain.IsLowered(Blocks, fx, y, fz);
}
