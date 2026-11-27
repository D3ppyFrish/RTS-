using System;

namespace Rts.Sim.Units;

/// <summary>
/// Buckets units by the block column they stand in, rebuilt every tick. Lets each unit find its
/// neighbours without checking every other unit. Iteration order is fixed, so results are deterministic.
/// A rebuild empties only the columns the last one filled, so its cost follows the units, not the size of the
/// map (a 1024 by 1024 map has a million columns and a few hundred units).
/// </summary>
public sealed class SpatialGrid
{
    private readonly int _sizeX, _sizeZ;
    private readonly int[] _head;
    private int[] _next = Array.Empty<int>();
    // The columns the last build put a unit in, so the next one empties only those.
    private int[] _used = Array.Empty<int>();
    private int _usedCount;
    // One bit a column, set where some unit stands: a lookup of an empty column (most of them) reads this
    // eighth of a megabyte, which stays in cache, rather than a far-off part of the 4 MB column heads.
    private readonly ulong[] _occupied;

    public SpatialGrid(int sizeX, int sizeZ)
    {
        _sizeX = sizeX;
        _sizeZ = sizeZ;
        _head = new int[sizeX * sizeZ];
        Array.Fill(_head, -1);
        _occupied = new ulong[(sizeX * sizeZ + 63) >> 6];
    }

    public void Build(UnitStore u)
    {
        for (int k = 0; k < _usedCount; k++)
        {
            int c = _used[k];
            _head[c] = -1;
            _occupied[c >> 6] = 0; // whole words: every bit set in it is one of these columns
        }
        _usedCount = 0;
        if (_next.Length < u.Capacity) _next = new int[u.Capacity];
        if (_used.Length < u.Count) _used = new int[Math.Max(u.Count, u.Capacity)];
        // Insert in reverse so each bucket lists units in ascending index order.
        for (int i = u.Count - 1; i >= 0; i--)
        {
            int c = Cell(u.X[i].FloorToInt(), u.Z[i].FloorToInt());
            if (_head[c] < 0)
            {
                _used[_usedCount++] = c;
                _occupied[c >> 6] |= 1UL << c;
            }
            _next[i] = _head[c];
            _head[c] = i;
        }
    }

    public int First(int x, int z)
    {
        if ((uint)x >= (uint)_sizeX || (uint)z >= (uint)_sizeZ) return -1;
        int c = z * _sizeX + x;
        return (_occupied[c >> 6] & (1UL << c)) == 0 ? -1 : _head[c];
    }
    public int Next(int index) => _next[index];

    private int Cell(int x, int z) => Math.Clamp(z, 0, _sizeZ - 1) * _sizeX + Math.Clamp(x, 0, _sizeX - 1);
}
