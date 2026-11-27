using System;

namespace Rts.Sim.Units;

/// <summary>
/// Buckets some of the units by the square of <see cref="CellSize"/> by <see cref="CellSize"/> columns they stand in,
/// so a look for units near a spot checks a few squares instead of every unit on the map. Each square lists its units
/// in ascending index order, so a walk over the squares is deterministic. Only a speed-up: callers still test every
/// unit it hands them, and pick among them by a rule that does not depend on the order they came in.
/// </summary>
public sealed class CoarseGrid
{
    public const int Shift = 4;
    public const int CellSize = 1 << Shift;

    private readonly int _cellsX, _cellsZ;
    private readonly int[] _head;
    private int[] _next = Array.Empty<int>();

    public CoarseGrid(int sizeX, int sizeZ)
    {
        _cellsX = (sizeX + CellSize - 1) >> Shift;
        _cellsZ = (sizeZ + CellSize - 1) >> Shift;
        _head = new int[_cellsX * _cellsZ];
    }

    /// <summary>Puts every unit that <paramref name="keep"/> passes in its square, dropping what was there before.</summary>
    public void Build(UnitStore u, Func<int, bool> keep)
    {
        Array.Fill(_head, -1);
        if (_next.Length < u.Capacity) _next = new int[u.Capacity];
        // Insert in reverse so each square lists units in ascending index order.
        for (int i = u.Count - 1; i >= 0; i--)
        {
            if (!keep(i)) continue;
            int c = CellZ(u.Z[i].FloorToInt()) * _cellsX + CellX(u.X[i].FloorToInt());
            _next[i] = _head[c];
            _head[c] = i;
        }
    }

    /// <summary>The squares that hold every unit standing in columns x0..x1 by z0..z1 (cells, inclusive), clamped to the map.</summary>
    public (int X0, int Z0, int X1, int Z1) Cells(int x0, int z0, int x1, int z1) =>
        (CellX(x0), CellZ(z0), CellX(x1), CellZ(z1));

    public int First(int cx, int cz) => _head[cz * _cellsX + cx];
    public int Next(int index) => _next[index];

    private int CellX(int x) => Math.Clamp(x >> Shift, 0, _cellsX - 1);
    private int CellZ(int z) => Math.Clamp(z >> Shift, 0, _cellsZ - 1);
}
