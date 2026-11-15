using System;

namespace Rts.Sim.Units;

/// <summary>
/// Buckets units by the block column they stand in, rebuilt every tick. Lets each unit find its
/// neighbours without checking every other unit. Iteration order is fixed, so results are deterministic.
/// </summary>
public sealed class SpatialGrid
{
    private readonly int _sizeX, _sizeZ;
    private readonly int[] _head;
    private int[] _next = Array.Empty<int>();

    public SpatialGrid(int sizeX, int sizeZ)
    {
        _sizeX = sizeX;
        _sizeZ = sizeZ;
        _head = new int[sizeX * sizeZ];
    }

    public void Build(UnitStore u)
    {
        Array.Fill(_head, -1);
        if (_next.Length < u.Capacity) _next = new int[u.Capacity];
        // Insert in reverse so each bucket lists units in ascending index order.
        for (int i = u.Count - 1; i >= 0; i--)
        {
            int c = Cell(u.X[i].FloorToInt(), u.Z[i].FloorToInt());
            _next[i] = _head[c];
            _head[c] = i;
        }
    }

    public int First(int x, int z) => x < 0 || z < 0 || x >= _sizeX || z >= _sizeZ ? -1 : _head[z * _sizeX + x];
    public int Next(int index) => _next[index];

    private int Cell(int x, int z) => Math.Clamp(z, 0, _sizeZ - 1) * _sizeX + Math.Clamp(x, 0, _sizeX - 1);
}
