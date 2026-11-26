using System;
using System.Collections.Generic;

namespace Rts.Sim.Units;

/// <summary>
/// Where units can stand and where they can step to, derived from the voxel world and kept up to date
/// as blocks change. Every block column has up to <see cref="MaxLevels"/> standing levels (the surface,
/// tunnels, caves, bridges), highest first; a node is one (column, level slot). Edges follow exactly the
/// rules units move by (<see cref="Walk.TryStep"/>), so any path in the graph is one a unit can walk.
///
/// Block changes only mark their column; <see cref="Update"/> then rebuilds the levels of marked columns
/// and the edges of every column next to them, and reports which 16x16 regions changed so flow fields
/// that cover them can be recomputed.
/// </summary>
public sealed class WalkGraph
{
    public const int MaxLevels = 4;
    public const int LevelBits = 2; // log2(MaxLevels)
    /// <summary>Columns per side of a region, the unit of invalidation (the same as a chunk).</summary>
    public const int RegionSize = VoxelWorld.ChunkSize;

    /// <summary>The eight step directions: four straight, then four diagonal.</summary>
    public static readonly (int X, int Z)[] Dirs =
    {
        (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, 1), (1, -1), (-1, -1),
    };

    /// <summary>Path cost of a step, in tenths of a block.</summary>
    public static readonly int[] DirCost = { 10, 10, 10, 10, 14, 14, 14, 14 };

    /// <summary>What a unit's feet are in or on at a node, which sets the cost of stepping onto it.</summary>
    internal const byte Plain = 0, Wading = 1, OnRoad = 2, Rough = 3, InGate = 4, Hazard = 5;

    /// <summary>
    /// Path cost of a step in each direction onto plain ground, into shallow water (double: wading is slow),
    /// onto a road (half: units walk twice as fast there), onto rubble or into an orchard's trees (double, like wading) and into a gate
    /// (as plain ground; only its owner's units go through, see <see cref="PathService"/>), and onto ground
    /// that hurts or traps whoever stands there (eight times plain ground, see <see cref="World.HazardSystem"/>):
    /// units walk up to seven blocks out of their way for each block of bog, lava heat or sinkhole crust they
    /// keep off, and cross it when there is no other way.
    /// </summary>
    internal static readonly int[][] StepCost =
    {
        DirCost,
        new[] { 20, 20, 20, 20, 28, 28, 28, 28 },
        new[] { 5, 5, 5, 5, 7, 7, 7, 7 },
        new[] { 20, 20, 20, 20, 28, 28, 28, 28 },
        DirCost,
        new[] { 80, 80, 80, 80, 112, 112, 112, 112 },
    };

    /// <summary>The dearest step there is (a diagonal onto a hazard), which sizes the path search's queue.</summary>
    internal const int MaxStepCost = 112;

    public int SizeX { get; }
    public int SizeZ { get; }
    public int RegionsX { get; }
    public int RegionsZ { get; }
    public int NodeCount => _level.Length;

    /// <summary>Bumped every time <see cref="Update"/> changes anything.</summary>
    public int Version { get; private set; }

    /// <summary>Nodes on a road. While there are none, path searches can use a stronger heuristic.</summary>
    public int RoadNodes { get; private set; }

    /// <summary>Regions whose nodes or edges changed in the last <see cref="Update"/>, in ascending order.</summary>
    public List<int> ChangedRegions { get; } = new();

    private readonly VoxelWorld _w;
    // Shared with PathService's inner loop, which reads them directly.
    internal readonly short[] _level;     // node -> feet level, or -1
    internal readonly uint[] _edges;      // node -> 8 x 3 bits: bit 2 = can step that way, bits 0-1 = target slot
    // The reverse of _edges, 8 x 4 bits a node: bit 4 * d + s is set if slot s of the column one step
    // against direction d steps onto the node in direction d. The path search walks edges backwards from
    // the goal, and this lets it visit only the nodes that really step onto the one it settles.
    internal readonly uint[] _into;
    internal readonly byte[] _ground;     // node -> Plain, Wading, OnRoad, Rough, InGate or Hazard: what a step onto it costs
    private readonly byte[] _base;        // node -> the same but for hazards, which depend on the columns around
    internal readonly bool[] _gated;      // column -> a unit standing there has its feet in a gate
    private readonly bool[] _columnDirty;
    private readonly List<int> _dirtyColumns = new();
    private readonly int[] _edgeStamp;
    private readonly int[] _regionStamp;
    private int _stamp;

    public WalkGraph(VoxelWorld world)
    {
        _w = world;
        SizeX = world.SizeX;
        SizeZ = world.SizeZ;
        RegionsX = (SizeX + RegionSize - 1) / RegionSize;
        RegionsZ = (SizeZ + RegionSize - 1) / RegionSize;
        int columns = SizeX * SizeZ;
        _level = new short[columns * MaxLevels];
        _edges = new uint[columns * MaxLevels];
        _into = new uint[columns * MaxLevels];
        _ground = new byte[columns * MaxLevels];
        _base = new byte[columns * MaxLevels];
        _gated = new bool[columns];
        _columnDirty = new bool[columns];
        _edgeStamp = new int[columns];
        _regionStamp = new int[RegionsX * RegionsZ];
        for (int c = 0; c < columns; c++) ComputeLevels(c);
        for (int c = 0; c < columns; c++) ComputeEdges(c);
        for (int n = 0; n < _edges.Length; n++)
            for (int d = 0; d < 8; d++)
            {
                int m = Step(n, d);
                if (m >= 0) _into[m] |= 1u << (4 * d + (n & (MaxLevels - 1)));
            }
        world.BlockChanged += OnBlockChanged;
    }

    /// <summary>The node for a unit standing at level y in column (x, z), or -1.</summary>
    public int Node(int x, int z, int y)
    {
        if ((uint)x >= (uint)SizeX || (uint)z >= (uint)SizeZ) return -1;
        int first = (z * SizeX + x) << LevelBits;
        for (int s = 0; s < MaxLevels; s++)
        {
            int l = _level[first + s];
            if (l == y) return first + s;
            if (l < 0) break;
        }
        return -1;
    }

    /// <summary>The standing node in column (x, z) closest to level y (the top one if y is negative), or -1.</summary>
    public int NearestNode(int x, int z, int y)
    {
        if ((uint)x >= (uint)SizeX || (uint)z >= (uint)SizeZ) return -1;
        int first = (z * SizeX + x) << LevelBits;
        if (_level[first] < 0) return -1;
        if (y < 0) return first;
        int best = first;
        for (int s = 1; s < MaxLevels && _level[first + s] >= 0; s++)
            if (Math.Abs(_level[first + s] - y) < Math.Abs(_level[best] - y)) best = first + s;
        return best;
    }

    public int Level(int node) => _level[node];
    public int Column(int node) => node >> LevelBits;
    public int ColumnX(int node) => (node >> LevelBits) % SizeX;
    public int ColumnZ(int node) => (node >> LevelBits) / SizeX;
    public int Region(int x, int z) => (z / RegionSize) * RegionsX + x / RegionSize;
    public bool IsWet(int node) => _base[node] == Wading;
    public bool IsRoad(int node) => _base[node] == OnRoad;
    /// <summary>True if a unit standing at the node is in a gate (its feet or its head).</summary>
    public bool InGateAt(int node) => _base[node] == InGate;
    /// <summary>True if the ground at the node hurts or traps a unit standing there (see <see cref="World.HazardSystem.IsHazard"/>).</summary>
    public bool IsHazard(int node) => _ground[node] == Hazard;
    /// <summary>True if some standing level of column (x, z) is in a gate.</summary>
    public bool Gated(int x, int z) => _gated[z * SizeX + x];

    /// <summary>Where a unit at <paramref name="node"/> ends up stepping in direction d, or -1 if it cannot.</summary>
    public int Step(int node, int d)
    {
        uint e = (_edges[node] >> (3 * d)) & 7;
        if ((e & 4) == 0) return -1;
        int col = (node >> LevelBits) + Dirs[d].Z * SizeX + Dirs[d].X;
        return (col << LevelBits) + (int)(e & 3);
    }

    /// <summary>True if stepping from <paramref name="from"/> in direction d lands on <paramref name="to"/>.</summary>
    public bool StepsTo(int from, int d, int to)
    {
        uint e = (_edges[from] >> (3 * d)) & 7;
        return (e & 4) != 0 && (int)(e & 3) == (to & (MaxLevels - 1));
    }

    /// <summary>
    /// The nodes that step onto <paramref name="node"/>: bit 4 * d + s is set if slot s of the column one
    /// step against direction d steps onto it in direction d (the reverse of <see cref="Step"/>).
    /// </summary>
    public uint Into(int node) => _into[node];

    /// <summary>Path cost of stepping in direction d onto <paramref name="to"/>: wading counts double, roads half, hazards eightfold.</summary>
    public int Cost(int d, int to) => StepCost[_ground[to]][d];

    /// <summary>
    /// True if more than <paramref name="limit"/> nodes can be walked to from <paramref name="start"/>: it is
    /// not in a pocket (a pit, a ledge) that small. The nodes found go in <paramref name="found"/> if given
    /// (all of them, when it returns false).
    /// </summary>
    public bool ReachesMoreThan(int start, int limit, HashSet<int>? found = null)
    {
        found ??= new HashSet<int>();
        found.Clear();
        found.Add(start);
        var open = new Queue<int>();
        open.Enqueue(start);
        while (open.Count > 0)
        {
            int a = open.Dequeue();
            for (int d = 0; d < 8; d++)
            {
                int b = Step(a, d);
                if (b < 0 || !found.Add(b)) continue;
                if (found.Count > limit) return true;
                open.Enqueue(b);
            }
        }
        return false;
    }

    /// <summary>Applies the block changes since the last call. Cheap when nothing changed.</summary>
    public void Update()
    {
        ChangedRegions.Clear();
        if (_dirtyColumns.Count == 0) return;
        _stamp++;
        // Levels first, so edges are built against up-to-date neighbours.
        foreach (int c in _dirtyColumns)
        {
            _columnDirty[c] = false;
            ComputeLevels(c);
        }
        foreach (int c in _dirtyColumns)
        {
            int x = c % SizeX, z = c / SizeX;
            for (int oz = -1; oz <= 1; oz++)
            for (int ox = -1; ox <= 1; ox++)
            {
                int nx = x + ox, nz = z + oz;
                if ((uint)nx >= (uint)SizeX || (uint)nz >= (uint)SizeZ) continue;
                int nc = nz * SizeX + nx;
                if (_edgeStamp[nc] == _stamp) continue;
                _edgeStamp[nc] = _stamp;
                ComputeEdges(nc);
                int r = Region(nx, nz);
                if (_regionStamp[r] != _stamp)
                {
                    _regionStamp[r] = _stamp;
                    ChangedRegions.Add(r);
                }
            }
        }
        // The reverse edges of a column come from the edges of the columns around it.
        _stamp++;
        foreach (int c in _dirtyColumns)
        {
            int x = c % SizeX, z = c / SizeX;
            for (int oz = -2; oz <= 2; oz++)
            for (int ox = -2; ox <= 2; ox++)
            {
                int nx = x + ox, nz = z + oz;
                if ((uint)nx >= (uint)SizeX || (uint)nz >= (uint)SizeZ) continue;
                int nc = nz * SizeX + nx;
                if (_edgeStamp[nc] == _stamp) continue;
                _edgeStamp[nc] = _stamp;
                ComputeInto(nx, nz);
            }
        }
        _dirtyColumns.Clear();
        ChangedRegions.Sort();
        Version++;
    }

    private void OnBlockChanged(int x, int y, int z)
    {
        int c = z * SizeX + x;
        if (_columnDirty[c]) return;
        _columnDirty[c] = true;
        _dirtyColumns.Add(c);
    }

    private void ComputeLevels(int column)
    {
        int x = column % SizeX, z = column / SizeX;
        int first = column << LevelBits;
        for (int k = 0; k < MaxLevels; k++)
            if (_base[first + k] == OnRoad) RoadNodes--;
        int s = 0;
        _gated[column] = false;
        // Scan down from the top: a level needs solid ground, passable feet and air at head height.
        BlockType above = _w.Get(x, _w.SizeY - 1, z), at = _w.Get(x, _w.SizeY - 2, z);
        for (int y = _w.SizeY - 2; y >= 1 && s < MaxLevels; y--)
        {
            BlockType below = _w.Get(x, y - 1, z);
            if (Walk.Headroom(above) && Walk.Passable(at) && BlockInfo.CanStandOn(below))
            {
                _level[first + s] = (short)y;
                byte ground = at == BlockType.Gate || above == BlockType.Gate ? InGate : at == BlockType.Water ? Wading : below == BlockType.Road ? OnRoad
                    : BlockInfo.IsRubble(below) || at == BlockType.Orchard || above == BlockType.Orchard ? Rough : Plain;
                if (ground == InGate) _gated[column] = true;
                _base[first + s] = ground;
                _ground[first + s] = ground;
                if (ground == OnRoad) RoadNodes++;
                s++;
            }
            above = at;
            at = below;
        }
        for (; s < MaxLevels; s++)
        {
            _level[first + s] = -1;
            _ground[first + s] = Plain;
            _base[first + s] = Plain;
            _edges[first + s] = 0;
        }
    }

    /// <summary>Rebuilds <see cref="_into"/> for the nodes of column (x, z) from its neighbours' edges.</summary>
    private void ComputeInto(int x, int z)
    {
        int first = (z * SizeX + x) << LevelBits;
        for (int t = 0; t < MaxLevels; t++) _into[first + t] = 0;
        for (int d = 0; d < 8; d++)
        {
            int px = x - Dirs[d].X, pz = z - Dirs[d].Z;
            if ((uint)px >= (uint)SizeX || (uint)pz >= (uint)SizeZ) continue;
            int pfirst = (pz * SizeX + px) << LevelBits;
            for (int s = 0; s < MaxLevels; s++)
            {
                uint e = (_edges[pfirst + s] >> (3 * d)) & 7;
                if ((e & 4) != 0) _into[first + (int)(e & 3)] |= 1u << (4 * d + s);
            }
        }
    }

    private void ComputeEdges(int column)
    {
        int x = column % SizeX, z = column / SizeX;
        int first = column << LevelBits;
        for (int s = 0; s < MaxLevels; s++)
        {
            int y = _level[first + s];
            if (y < 0) break;
            // Hazards depend on the columns around (lava heat), which is why they are set with the edges: those
            // are rebuilt for every column next to a changed one. A gate is never counted as a hazard.
            byte b = _base[first + s];
            _ground[first + s] = b != InGate && World.HazardSystem.IsHazard(_w, x, y, z) ? Hazard : b;
            uint edges = 0;
            for (int d = 0; d < 8; d++)
            {
                int tx = x + Dirs[d].X, tz = z + Dirs[d].Z;
                if ((uint)tx >= (uint)SizeX || (uint)tz >= (uint)SizeZ) continue;
                // A diagonal step needs both side columns to be enterable, as in UnitMovement.TryMove.
                if (d >= 4 && (!Walk.TryStep(_w, x, z, y, tx, z, out _) || !Walk.TryStep(_w, x, z, y, x, tz, out _)))
                    continue;
                if (!Walk.TryStep(_w, x, z, y, tx, tz, out int ty)) continue;
                int target = Node(tx, tz, ty);
                if (target < 0) continue; // more levels in that column than we keep
                edges |= (uint)(4 | (target & (MaxLevels - 1))) << (3 * d);
            }
            _edges[first + s] = edges;
        }
    }
}
