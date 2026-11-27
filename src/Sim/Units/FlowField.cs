using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rts.Sim.Units;

/// <summary>
/// Path distances (in tenths of a block) from walk-graph nodes to one destination, shared by every unit
/// ordered there. A unit steps to whichever neighbouring node is closest to the goal. Distances are kept
/// per 16x16 region and only for regions the search reached: the search heads for the units following
/// the field and stops a margin past the last of them, so a field covers a corridor, not the whole map.
/// A field is for one player's units: it goes through that player's gates and not through anyone else's.
/// </summary>
public sealed class FlowField
{
    public const ushort Unknown = ushort.MaxValue;
    /// <summary>Marks a node with no way to the goal, such as the floor of a pit a unit cannot climb out of.</summary>
    public const ushort Unreachable = ushort.MaxValue - 1;
    private const int RegionShift = 4; // log2(WalkGraph.RegionSize)
    private const int RegionNodes = WalkGraph.RegionSize * WalkGraph.RegionSize * WalkGraph.MaxLevels;

    private readonly WalkGraph _g;

    public int Id { get; }
    /// <summary>Destination column and level. The goal node is looked up again on every computation.</summary>
    public int GoalX { get; }
    public int GoalZ { get; }
    public int GoalY { get; }
    /// <summary>The player whose units follow it: their gates are open to it, other players' gates are walls.</summary>
    public int Player { get; }

    /// <summary>True once a computation has finished; until then units walk straight at the goal.</summary>
    public bool Ready => Current != null;

    /// <summary>
    /// The last computation searched everything reachable from the goal, so a unit outside the field
    /// cannot get there: no point searching again until the terrain changes.
    /// </summary>
    public bool Complete { get; internal set; }

    internal ushort[]?[]? Current;
    internal ushort[]?[]? Next;
    /// <summary>The terrain under the field changed since it was computed.</summary>
    public bool Dirty { get; internal set; }
    /// <summary>
    /// A change since it was computed may have opened a shorter way than the field knows, one its patch could
    /// not vouch for (see <see cref="PathService"/>): the field is recomputed after
    /// <see cref="PathService.DirtyRefreshInterval"/>. Changes that only lengthen or block a way are left
    /// until a unit runs into them (<see cref="Broken"/>).
    /// </summary>
    internal bool Shortcut;

    /// <summary>A unit following the field stands outside the area it covers.</summary>
    internal bool WantsMore;
    /// <summary>A unit found the field disagreeing with the terrain on its way (a step gone or a new shortcut).</summary>
    internal bool Broken;
    internal long StartedTick = PathService.LongAgo;
    internal int Users;
    /// <summary>
    /// A field to the nearest place units could reach to a goal they could not (see
    /// <see cref="UnitMovement"/>): units that cannot get here either stop instead of looking further.
    /// </summary>
    internal bool Fallback;
    /// <summary>Where units that cannot reach this goal go instead (a node), once one of them has looked; -1 if not yet.</summary>
    internal int FallbackNode = -1;

    internal FlowField(WalkGraph g, int id, int goalX, int goalZ, int goalY, int player)
    {
        _g = g;
        Id = id;
        GoalX = goalX;
        GoalZ = goalZ;
        GoalY = goalY;
        Player = player;
    }

    /// <summary>
    /// Path distance from a node to the goal in tenths of a block; <see cref="Unreachable"/> if the node is
    /// known to have no way there, or <see cref="Unknown"/> if the search did not get that far.
    /// </summary>
    public int Distance(int node)
    {
        if (Current == null || node < 0) return Unknown;
        int col = node >> WalkGraph.LevelBits;
        return Get(Current, col % _g.SizeX, col / _g.SizeX, node & (WalkGraph.MaxLevels - 1));
    }

    /// <summary>Distance of the node in column (x, z) at the given slot; see <see cref="Distance(int)"/>.</summary>
    public int Distance(int x, int z, int slot) => Current == null ? Unknown : Get(Current, x, z, slot);

    /// <summary>The neighbouring node to step to from <paramref name="node"/>, or -1 if there is none.</summary>
    public int NextStep(int node)
    {
        int col = node >> WalkGraph.LevelBits;
        int d = NextDirection(node, col % _g.SizeX, col / _g.SizeX, out _);
        return d < 0 ? -1 : _g.Step(node, d);
    }

    /// <summary>
    /// Which way to step from <paramref name="node"/> (in column x, z): the direction index into
    /// <see cref="WalkGraph.Dirs"/>, or -1. Also returns the path cost that way: on an up-to-date field it
    /// equals the node's own distance; when it does not, the terrain changed along the way.
    /// </summary>
    public int NextDirection(int node, int x, int z, out int viaCost)
    {
        viaCost = int.MaxValue;
        if (Current == null) return -1;
        int best = -1;
        for (int d = 0; d < 8; d++)
        {
            int m = _g.Step(node, d);
            if (m < 0) continue;
            int dm = Get(Current, x + WalkGraph.Dirs[d].X, z + WalkGraph.Dirs[d].Z, m & (WalkGraph.MaxLevels - 1));
            if (dm >= Unreachable) continue;
            int cost = dm + _g.Cost(d, m);
            if (cost < viaCost)
            {
                viaCost = cost;
                best = d;
            }
        }
        return best;
    }

    /// <summary>
    /// True if the field's table (its distances, or those of the computation in progress) got as far as any
    /// of these columns or a column next to one. Only a change there can alter the ways it knows: a node it
    /// reached that lost or gained a step, or a new way in from one it reached. Changes beyond are left to
    /// the search that gets to them.
    /// </summary>
    internal bool Reaches(ushort[]?[] table, List<int> columns)
    {
        foreach (int c in columns)
        {
            int x = c % _g.SizeX, z = c / _g.SizeX;
            for (int oz = -1; oz <= 1; oz++)
            for (int ox = -1; ox <= 1; ox++)
            {
                int nx = x + ox, nz = z + oz;
                if ((uint)nx >= (uint)_g.SizeX || (uint)nz >= (uint)_g.SizeZ) continue;
                if (Known(table, nx, nz)) return true;
            }
        }
        return false;
    }

    private bool Known(ushort[]?[] table, int x, int z)
    {
        var block = table[(z >> RegionShift) * _g.RegionsX + (x >> RegionShift)];
        if (block == null) return false;
        int first = Local(x, z, 0);
        for (int s = 0; s < WalkGraph.MaxLevels; s++)
            if (block[first + s] != Unknown) return true;
        return false;
    }

    internal int Get(ushort[]?[] table, int x, int z, int slot)
    {
        var block = table[(z >> RegionShift) * _g.RegionsX + (x >> RegionShift)];
        return block == null ? Unknown : block[Local(x, z, slot)];
    }

    internal void Set(ushort[]?[] table, int x, int z, int slot, int value, Stack<ushort[]> pool)
    {
        int r = (z >> RegionShift) * _g.RegionsX + (x >> RegionShift);
        var block = table[r];
        if (block == null)
        {
            block = table[r] = pool.Count > 0 ? pool.Pop() : new ushort[RegionNodes];
            Array.Fill(block, Unknown);
        }
        block[Local(x, z, slot)] = (ushort)value;
    }

    private static int Local(int x, int z, int slot) =>
        ((((z & (WalkGraph.RegionSize - 1)) << RegionShift) | (x & (WalkGraph.RegionSize - 1))) << WalkGraph.LevelBits) | slot;

    internal static void Release(ushort[]?[]? table, Stack<ushort[]> pool)
    {
        if (table == null) return;
        for (int r = 0; r < table.Length; r++)
        {
            if (table[r] is { } block) pool.Push(block);
            table[r] = null;
        }
    }
}

/// <summary>
/// Owns the flow fields: creates one per destination, computes them a bounded amount of work per tick,
/// recomputes them when the terrain they cover changes, and drops them once no unit follows them.
/// Everything happens in a fixed order with a fixed work budget, so every machine computes the same
/// fields on the same ticks.
///
/// A computation searches outward from the goal over the walk graph's edges in reverse (A* with a bucket
/// queue): nodes come off the queue in order of their distance from the goal plus a lower bound on the
/// rest of the way to the box around the units following the field. Every node it settles has its exact
/// distance, and it stops a margin past the last unit, so a field for a group far away covers the band
/// between the group and the goal rather than everything within that distance of the goal.
/// </summary>
public sealed class PathService
{
    /// <summary>Nodes settled per tick across all field computations (1 to 3 ms of work).</summary>
    public const int ExpansionsPerTick = 12_000;
    /// <summary>Fewest ticks between two recomputations of the same field.</summary>
    public const int RecomputeInterval = 10;
    /// <summary>
    /// Ticks after which a field whose terrain changed in a way its patch could not vouch for (see Repair) is
    /// recomputed even if no unit has run into the change, to pick up new shortcuts. Changes on a unit's way
    /// are handled sooner, see <see cref="FlowField.Broken"/>.
    /// </summary>
    public const int DirtyRefreshInterval = 100;
    /// <summary>How far (tenths of a block) the search continues past the farthest unit following the field.</summary>
    public const int Margin = 300;
    /// <summary>
    /// A unit the search has not reached a margin past its straight-line distance is checked for being stuck:
    /// if everything it can walk to is at most this many nodes and none of them leads to the goal, it is in a
    /// pit (or on a ledge) it cannot leave, and the search need not comb the whole map for it.
    /// </summary>
    public const int PocketLimit = 64;
    /// <summary>A start tick old enough that a recomputation is due (and subtracting it cannot overflow).</summary>
    internal const long LongAgo = -1_000_000;

    // Larger than the most a queue key can grow in one step: the dearest step (a diagonal onto a hazard)
    // plus a diagonal's worth for moving away from the units. A power of two.
    private const int BucketCount = 256;
    // Bucket entries pack a node's column and slot: x | z << 10 | slot << 20.
    private const int CoordBits = 10;
    private const int CoordMask = (1 << CoordBits) - 1;

    private readonly WalkGraph _g;
    private readonly SortedDictionary<int, FlowField> _byId = new();
    private readonly SortedDictionary<long, FlowField> _byGoal = new();
    private readonly Stack<ushort[]> _pool = new();
    // The queue: one stack of packed entries per bucket (last in, first out), kept in plain arrays
    // because pushing and popping them is most of the search's work.
    private readonly int[][] _buckets = new int[BucketCount][];
    private readonly int[] _bucketCount = new int[BucketCount];
    private readonly int[] _wantStamp;
    // A region's stamp is the computation's want id if a unit to head for stands in it: most nodes the search
    // settles are in regions with none, and checking this small table spares a look at the big one.
    private readonly int[] _wantRegion;
    // Stuck checks: a node's stamp is the id of the check that visited it, or minus the computation's
    // want id once a check from it found it is not stuck.
    private readonly int[] _pocketStamp;
    private readonly List<int> _pocket = new();
    private readonly List<long> _wanted = new(); // (check-at distance << 32) | node, ascending
    private readonly List<int> _removed = new();
    private int _wantId, _pocketId, _checkIndex, _nextCheckAt;
    private int _nextId = 1;

    // The computation in progress (at most one at a time).
    private FlowField? _computing;
    private int _cur, _pending, _stopAt, _wantedLeft;
    // The box around the units following the field, which the search heads for.
    private int _boxMinX, _boxMaxX, _boxMinZ, _boxMaxZ;
    // Per column: how far (tenths of a block, octile) it is to the nearest road, or NoRoad. The heuristic
    // counts the way off the roads at full cost and only the rest at half (see ToBox), and a step onto a road
    // costs half only where this says 0. A computation keeps the one it started with: when roads come or go
    // it is worked out again and a computation in progress starts over.
    private readonly ushort[] _roadDist;
    private int _roadVersion = -1;
    private const ushort NoRoad = ushort.MaxValue;

    public PathService(WalkGraph graph)
    {
        if (graph.SizeX > CoordMask + 1 || graph.SizeZ > CoordMask + 1)
            throw new ArgumentException("Flow fields support worlds up to 1024 blocks across", nameof(graph));
        _g = graph;
        for (int i = 0; i < BucketCount; i++) _buckets[i] = new int[64];
        _wantStamp = new int[graph.NodeCount];
        _wantRegion = new int[graph.RegionsX * graph.RegionsZ];
        _pocketStamp = new int[graph.NodeCount];
        _rStamp = new int[graph.SizeX * graph.SizeZ];
        _roadDist = new ushort[graph.SizeX * graph.SizeZ];
    }

    public int FieldCount => _byId.Count;

    /// <summary>Total nodes settled since the start, for profiling.</summary>
    public long Expansions { get; private set; }

    public FlowField? Get(int id) => id > 0 && _byId.TryGetValue(id, out var f) ? f : null;

    /// <summary>
    /// Who owns the gate a unit stands in at column (x, z), or -1 if nobody does (a wrecked gate's leaves
    /// let everyone through). Only asked about columns with a gate in them (see <see cref="WalkGraph.Gated"/>).
    /// </summary>
    public Func<int, int, int>? GateOwner { get; set; }

    /// <summary>
    /// The field leading the player's units to level y of column (x, z), shared with any existing order of
    /// theirs there.
    /// </summary>
    public FlowField Request(int x, int z, int y, int player)
    {
        long key = GoalKey(x, z, y, player);
        if (_byGoal.TryGetValue(key, out var field))
        {
            // New units are joining: if the field is out of date, refresh it without waiting.
            if (field.Dirty || !field.Complete) field.StartedTick = LongAgo;
            return field;
        }
        field = new FlowField(_g, _nextId++, x, z, y, player);
        _byId[field.Id] = field;
        _byGoal[key] = field;
        return field;
    }

    /// <summary>
    /// Once per tick, after block changes and new orders and before units move: invalidates fields over
    /// changed terrain, forgets unused ones and spends this tick's work budget on computing fields.
    /// </summary>
    public void Step(UnitStore units, long tick)
    {
        // Terrain changes where a field reaches mark it dirty and patch it (see Repair); one the patch cannot
        // vouch for, which may have a new shorter way, is recomputed. A computation in progress keeps going
        // (restarting it on every dig nearby could starve it) and is redone afterwards.
        _g.Update();
        if (_g.RoadVersion != _roadVersion)
        {
            // Roads came or went: work out the heuristic again, and start a computation in progress over
            // with it (a computation must keep the heuristic it started with).
            if (_computing is { } busy)
            {
                Abort();
                busy.StartedTick = LongAgo;
                busy.Dirty = busy.Shortcut = true;
            }
            UpdateRoads();
        }
        if (_g.ChangedColumns.Count > 0)
            foreach (var f in _byId.Values)
            {
                if (f.Shortcut) continue; // due for a recomputation already
                // A computation in progress keeps going and is redone afterwards.
                if (f.Next != null && f.Reaches(f.Next, _g.ChangedColumns))
                {
                    f.Dirty = f.Shortcut = true;
                    continue;
                }
                if (f.Current == null || !f.Reaches(f.Current, _g.ChangedColumns)) continue;
                f.Dirty = true;
                if (!Repair(f)) f.Shortcut = true;
            }

        foreach (var f in _byId.Values) f.Users = 0;
        for (int i = 0; i < units.Count; i++)
            if (units.Order[i] != UnitOrder.Idle && Get(units.Field[i]) is { } f) f.Users++;
        _removed.Clear();
        foreach (var f in _byId.Values)
            if (f.Users == 0) _removed.Add(f.Id);
        foreach (int id in _removed) Remove(_byId[id]);

        int budget = ExpansionsPerTick;
        while (budget > 0)
        {
            if (_computing == null)
            {
                var next = NextDue(tick);
                if (next == null) break;
                Start(next, units, tick);
            }
            budget -= Continue(budget);
        }
    }

    /// <summary>New fields first (lowest id), then the stale field computed longest ago.</summary>
    private FlowField? NextDue(long tick)
    {
        FlowField? best = null;
        foreach (var f in _byId.Values)
        {
            if (!f.Ready) return f;
            long age = tick - f.StartedTick;
            if (age < RecomputeInterval) continue;
            bool stale = (f.Dirty && (f.Broken || (f.Shortcut && age >= DirtyRefreshInterval))) || (f.WantsMore && !f.Complete);
            if (!stale) continue;
            if (best == null || f.StartedTick < best.StartedTick) best = f;
        }
        return best;
    }

    private long GoalKey(int x, int z, int y, int player) =>
        ((((long)y * _g.SizeZ + z) * _g.SizeX + x) << PlayerBits) | (uint)(player & ((1 << PlayerBits) - 1));

    private const int PlayerBits = 5;

    /// <summary>True if a unit of the field's player may stand in column (x, z): no gate there, or one of theirs.</summary>
    private bool GateLets(FlowField f, int x, int z)
    {
        if (!_g._gated[z * _g.SizeX + x]) return true;
        int owner = GateOwner?.Invoke(x, z) ?? -1;
        return owner < 0 || owner == f.Player;
    }

    private void Remove(FlowField f)
    {
        if (f == _computing) Abort();
        FlowField.Release(f.Current, _pool);
        f.Current = null;
        _byId.Remove(f.Id);
        _byGoal.Remove(GoalKey(f.GoalX, f.GoalZ, f.GoalY, f.Player));
    }

    private void Start(FlowField f, UnitStore units, long tick)
    {
        _computing = f;
        f.Next = new ushort[]?[_g.RegionsX * _g.RegionsZ];
        f.Dirty = false;
        f.Shortcut = false;
        f.WantsMore = false;
        f.Broken = false;
        f.StartedTick = tick;
        Array.Clear(_bucketCount);
        _cur = 0;
        _pending = 0;
        _wantId++;
        _wantedLeft = 0;
        _wanted.Clear();
        _checkIndex = 0;
        _nextCheckAt = int.MaxValue;
        _stopAt = int.MaxValue;

        int goal = _g.Node(f.GoalX, f.GoalZ, f.GoalY);
        if (goal < 0) goal = _g.NearestNode(f.GoalX, f.GoalZ, f.GoalY);
        if (goal < 0) return; // nowhere to stand at the goal any more: the field stays empty
        int gx = _g.ColumnX(goal), gz = _g.ColumnZ(goal), gs = goal & (WalkGraph.MaxLevels - 1);

        // The search heads for the units that follow this field and stops a margin past the last of them.
        _boxMinX = _boxMinZ = int.MaxValue;
        _boxMaxX = _boxMaxZ = int.MinValue;
        for (int i = 0; i < units.Count; i++)
        {
            if (units.Order[i] == UnitOrder.Idle || units.Field[i] != f.Id) continue;
            int ux = units.X[i].FloorToInt(), uz = units.Z[i].FloorToInt();
            int n = _g.Node(ux, uz, units.Y[i]);
            if (n < 0 || _wantStamp[n] == _wantId) continue;
            _wantStamp[n] = _wantId;
            _wantRegion[(uz >> 4) * _g.RegionsX + (ux >> 4)] = _wantId;
            _wantedLeft++;
            _boxMinX = Math.Min(_boxMinX, ux);
            _boxMaxX = Math.Max(_boxMaxX, ux);
            _boxMinZ = Math.Min(_boxMinZ, uz);
            _boxMaxZ = Math.Max(_boxMaxZ, uz);
            _wanted.Add(((long)(Octile(ux - gx, uz - gz) + Margin) << 32) | (uint)n);
        }
        _wanted.Sort();
        if (_wanted.Count > 0) _nextCheckAt = (int)(_wanted[0] >> 32);
        if (_wantedLeft == 0)
        {
            // Nobody to head for: a plain search a margin around the goal.
            _boxMinX = _boxMinZ = 0;
            _boxMaxX = _g.SizeX - 1;
            _boxMaxZ = _g.SizeZ - 1;
            _stopAt = Margin;
        }

        f.Set(f.Next, gx, gz, gs, 0, _pool);
        _cur = ToBox(gx, gz);
        Push(_cur & (BucketCount - 1), gx | gz << CoordBits | gs << (2 * CoordBits));
        _pending = 1;
    }

    /// <summary>Path cost of the shortest walk over open ground across dx by dz columns.</summary>
    private static int Octile(int dx, int dz)
    {
        dx = Math.Abs(dx);
        dz = Math.Abs(dz);
        return dx > dz ? 10 * dx + 4 * dz : 10 * dz + 4 * dx;
    }

    /// <summary>
    /// A lower bound on the path cost from column (x, z) to the nearest unit in the box, O being the octile
    /// distance there and R the distance to the nearest road less a diagonal step (see <see cref="_roadDist"/>):
    /// (O + min(O, R)) / 2. A way that keeps off the roads costs at least O; one that takes a road walks at
    /// least R at full cost to get onto it, and all of it at least half. Neither O nor R changes by more than
    /// a step's length a step, and a step onto a road (half cost) is from where R is 0, so the bound never
    /// drops by more than the step costs: the queue order stays exact (a consistent heuristic). With no
    /// roads anywhere it is O.
    /// </summary>
    private int ToBox(int x, int z)
    {
        int dx = x < _boxMinX ? _boxMinX - x : x > _boxMaxX ? x - _boxMaxX : 0;
        int dz = z < _boxMinZ ? _boxMinZ - z : z > _boxMaxZ ? z - _boxMaxZ : 0;
        int o = dx > dz ? 10 * dx + 4 * dz : 10 * dz + 4 * dx;
        // Less a diagonal step: the last step onto a road costs half, so it does not count at full.
        int r = _roadDist[z * _g.SizeX + x] - 14;
        return (o + (r < 0 ? 0 : r < o ? r : o)) >> 1;
    }

    /// <summary>The step costs onto node m: a road counts at half only once <see cref="_roadDist"/> knows it.</summary>
    private int[] StepCostOnto(int m)
    {
        int kind = _g._ground[m];
        return WalkGraph.StepCost[kind == WalkGraph.OnRoad && _roadDist[m >> WalkGraph.LevelBits] != 0 ? WalkGraph.Plain : kind];
    }

    /// <summary>
    /// Works out <see cref="_roadDist"/> again if roads came or went since: a two-pass distance transform
    /// over the columns, with straight steps 10 and diagonal ones 14 (the octile distance exactly).
    /// </summary>
    private void UpdateRoads()
    {
        if (_g.RoadVersion == _roadVersion) return;
        _roadVersion = _g.RoadVersion;
        int sx = _g.SizeX, sz = _g.SizeZ;
        var d = _roadDist;
        if (_g.RoadNodes == 0)
        {
            Array.Fill(d, NoRoad);
            return;
        }
        const int Far = NoRoad - 100;
        for (int c = 0; c < d.Length; c++) d[c] = _g.RoadColumn(c) ? (ushort)0 : (ushort)Far;
        for (int z = 0; z < sz; z++)
            for (int x = 0, c = z * sx; x < sx; x++, c++)
            {
                int v = d[c];
                if (v == 0) continue;
                if (x > 0) v = Math.Min(v, d[c - 1] + 10);
                if (z > 0)
                {
                    v = Math.Min(v, d[c - sx] + 10);
                    if (x > 0) v = Math.Min(v, d[c - sx - 1] + 14);
                    if (x < sx - 1) v = Math.Min(v, d[c - sx + 1] + 14);
                }
                d[c] = (ushort)Math.Min(v, Far);
            }
        for (int z = sz - 1; z >= 0; z--)
            for (int x = sx - 1, c = z * sx + x; x >= 0; x--, c--)
            {
                int v = d[c];
                if (v == 0) continue;
                if (x < sx - 1) v = Math.Min(v, d[c + 1] + 10);
                if (z < sz - 1)
                {
                    v = Math.Min(v, d[c + sx] + 10);
                    if (x < sx - 1) v = Math.Min(v, d[c + sx + 1] + 14);
                    if (x > 0) v = Math.Min(v, d[c + sx - 1] + 14);
                }
                d[c] = (ushort)Math.Min(v, Far);
            }
    }

    /// <summary>Runs the current computation for up to <paramref name="budget"/> settled nodes; returns the work done.</summary>
    private int Continue(int budget)
    {
        var f = _computing!;
        var next = f.Next!;
        int sizeX = _g.SizeX, sizeZ = _g.SizeZ, regionsX = _g.RegionsX;
        var intoEdges = _g._into;
        var gated = _g._gated;
        var wantStamp = _wantStamp;
        var wantRegion = _wantRegion;
        var buckets = _buckets;
        var counts = _bucketCount;
        int used = 0;
        while (true)
        {
            if (_pending == 0 || _cur > _stopAt)
            {
                Finish(exhausted: _pending == 0);
                return Math.Max(used, 1);
            }
            if (used >= budget) return used;
            if (_cur >= _nextCheckAt)
            {
                used += CheckNextWanted();
                continue;
            }
            int bi = _cur & (BucketCount - 1);
            int count = counts[bi];
            if (count == 0)
            {
                _cur++;
                continue;
            }
            int packed = buckets[bi][--count];
            counts[bi] = count;
            _pending--;
            int x = packed & CoordMask, z = (packed >> CoordBits) & CoordMask, slot = packed >> (2 * CoordBits);
            int region = (z >> 4) * regionsX + (x >> 4);
            var own = next[region];
            int local = (((z & 15) << 4 | (x & 15)) << WalkGraph.LevelBits) | slot;
            int dist = own == null ? FlowField.Unknown : own[local];
            if (dist + ToBox(x, z) != _cur) continue; // superseded by a shorter path
            used++;
            Expansions++;
            int m = ((z * sizeX + x) << WalkGraph.LevelBits) | slot;
            if (wantRegion[region] == _wantId && wantStamp[m] == _wantId)
            {
                wantStamp[m] = 0;
                if (--_wantedLeft == 0) _stopAt = _cur + Margin;
            }
            // The cost of stepping onto m. Roads count only once the heuristic allows for them (see ToBox).
            var stepCost = StepCostOnto(m);

            // Relax every node that steps onto m, in direction order then slot order: the reverse edges
            // list them, so columns with no step onto m are not looked at.
            uint into = intoEdges[m];
            while (into != 0)
            {
                int d = BitOperations.TrailingZeroCount(into) >> 2;
                uint ofDir = into & (0xFu << (4 * d));
                into &= ~ofDir;
                int px = x - DirX[d], pz = z - DirZ[d];
                // Another player's gate is a wall: nobody gets there, and no corner is cut past one.
                if (gated[pz * sizeX + px] && !GateLets(f, px, pz)) continue;
                if (d >= 4 && ((gated[pz * sizeX + x] && !GateLets(f, x, pz)) || (gated[z * sizeX + px] && !GateLets(f, px, z))))
                    continue;
                int nd = dist + stepCost[d];
                if (nd >= FlowField.Unreachable) continue; // farther than a distance can say
                int key = -1;
                int r = (pz >> 4) * regionsX + (px >> 4);
                var block = next[r];
                int localBase = (((pz & 15) << 4) | (px & 15)) << WalkGraph.LevelBits;
                for (uint slots = ofDir >> (4 * d); slots != 0; slots &= slots - 1)
                {
                    int s = BitOperations.TrailingZeroCount(slots);
                    if (block == null)
                    {
                        f.Set(next, px, pz, s, nd, _pool);
                        block = next[r]!;
                    }
                    else if (nd >= block[localBase | s]) continue;
                    else block[localBase | s] = (ushort)nd;
                    if (key < 0) key = (nd + ToBox(px, pz)) & (BucketCount - 1);
                    int c = counts[key];
                    var items = buckets[key];
                    if (c == items.Length) buckets[key] = items = Grow(items);
                    items[c] = px | pz << CoordBits | s << (2 * CoordBits);
                    counts[key] = c + 1;
                    _pending++;
                }
            }
        }
    }

    // WalkGraph.Dirs split into plain arrays for the search loop.
    private static readonly int[] DirX = Array.ConvertAll(WalkGraph.Dirs, d => d.X);
    private static readonly int[] DirZ = Array.ConvertAll(WalkGraph.Dirs, d => d.Z);

    /// <summary>
    /// The search has gone well past the next unit on the list without reaching it: is the unit stuck? Walks
    /// forward from it; if everything it can reach is a small area with no way to the goal, marks that area
    /// unreachable, so the units there give up instead of the search combing the whole map for them.
    /// Returns the work done.
    /// </summary>
    private int CheckNextWanted()
    {
        var f = _computing!;
        var next = f.Next!;
        int start = (int)(_wanted[_checkIndex] & 0xFFFF_FFFF);
        _checkIndex++;
        _nextCheckAt = _checkIndex < _wanted.Count ? (int)(_wanted[_checkIndex] >> 32) : int.MaxValue;
        if (_wantStamp[start] != _wantId || Known(next, start)) return 1; // reached, or already found stuck

        _pocketId++;
        _pocket.Clear();
        _pocket.Add(start);
        _pocketStamp[start] = _pocketId;
        for (int k = 0; k < _pocket.Count; k++)
        {
            int a = _pocket[k];
            for (int d = 0; d < 8; d++)
            {
                int b = _g.Step(a, d);
                if (b < 0 || _pocketStamp[b] == _pocketId) continue;
                int dist = Distance(next, b);
                if (dist == FlowField.Unreachable) continue; // a pit found earlier: no way out there either
                // Into an area that leads to the goal, or one too big to be a pit: leave it to the search.
                if (dist != FlowField.Unknown || _pocketStamp[b] == -_wantId || _pocket.Count >= PocketLimit)
                {
                    _pocketStamp[start] = -_wantId;
                    return k + 1;
                }
                _pocketStamp[b] = _pocketId;
                _pocket.Add(b);
            }
        }
        foreach (int a in _pocket)
        {
            int col = a >> WalkGraph.LevelBits;
            f.Set(next, col % _g.SizeX, col / _g.SizeX, a & (WalkGraph.MaxLevels - 1), FlowField.Unreachable, _pool);
            if (_wantStamp[a] != _wantId) continue;
            _wantStamp[a] = 0;
            if (--_wantedLeft == 0) _stopAt = _cur + Margin;
        }
        return _pocket.Count;
    }

    /// <summary>Most nodes a <see cref="Repair"/> looks at; a bigger change is left to a recomputation.</summary>
    private const int RepairNodes = 4096;
    /// <summary>Most passes a <see cref="Repair"/> makes over its nodes before leaving it to a recomputation.</summary>
    private const int RepairPasses = 32;

    // Repair's nodes: the node, what the field held for it (Unknown for a slot whose level moved), its new
    // estimate, and whether its slot is another place than it was.
    private readonly List<int> _rNode = new();
    private readonly List<int> _rOld = new();
    private readonly List<int> _rNew = new();
    private readonly List<bool> _rMoved = new();
    private readonly Dictionary<int, int> _rIndex = new(); // node -> index above (looked up, never iterated)
    private readonly int[] _rStamp;
    private int _rId;

    /// <summary>
    /// The walk graph changed where field <paramref name="f"/> reaches (<see cref="WalkGraph.ChangedColumns"/>):
    /// patches its distances instead of searching again. The nodes in and next to the changed columns get
    /// their best distance through each other and through the field's distances around them. Nodes whose
    /// levels moved (a column dug out or built on) take that distance; nodes it brings nearer the goal than
    /// the field said (a gap knocked in a wall, a pit with a way out again) take it too and pass it on to
    /// the nodes that step onto them (<see cref="Spread"/>). Any shorter way the change opened shows up as
    /// such a node: a way through the change that is shorter than the field knew must leave it, onto ground
    /// it did not change, from a node around it that the change brought nearer.
    ///
    /// A way the change lengthened or blocked is left as it was: the units that come to it find their step
    /// gone and have the field recomputed (<see cref="FlowField.Broken"/>). Ground the search never reached
    /// is left to the search. Returns false when the patch cannot vouch for the field: a field that covered
    /// everything reachable (the change may let more in), the goal itself moved, a change too big or a
    /// shortcut that reaches too far to patch. The field is then recomputed after
    /// <see cref="DirtyRefreshInterval"/>.
    /// </summary>
    private bool Repair(FlowField f)
    {
        if (f.Complete) return false; // it says what cannot get there: any change may let more in
        var table = f.Current!;
        int sizeX = _g.SizeX, sizeZ = _g.SizeZ;
        _rNode.Clear();
        _rOld.Clear();
        _rNew.Clear();
        _rMoved.Clear();
        _rIndex.Clear();
        _rId++;
        int goalColumn = f.GoalZ * sizeX + f.GoalX;
        foreach (int c in _g.ChangedColumns)
        {
            int x = c % sizeX, z = c / sizeX;
            for (int oz = -1; oz <= 1; oz++)
            for (int ox = -1; ox <= 1; ox++)
            {
                int nx = x + ox, nz = z + oz;
                if ((uint)nx >= (uint)sizeX || (uint)nz >= (uint)sizeZ) continue;
                int nc = nz * sizeX + nx;
                if (_rStamp[nc] == _rId) continue;
                _rStamp[nc] = _rId;
                bool moved = _g.MovedColumns.BinarySearch(nc) >= 0;
                if (moved && nc == goalColumn) return false; // the goal itself moved
                for (int s = 0; s < WalkGraph.MaxLevels; s++)
                {
                    int n = (nc << WalkGraph.LevelBits) | s;
                    if (_g._level[n] < 0)
                    {
                        // A level that is gone keeps no distance.
                        if (moved && f.Get(table, nx, nz, s) != FlowField.Unknown) f.Set(table, nx, nz, s, FlowField.Unknown, _pool);
                        continue;
                    }
                    int old = moved ? FlowField.Unknown : f.Get(table, nx, nz, s);
                    _rIndex[n] = _rNode.Count;
                    _rNode.Add(n);
                    _rOld.Add(old);
                    _rNew.Add(old < FlowField.Unreachable ? old : int.MaxValue);
                    _rMoved.Add(moved);
                }
            }
            if (_rNode.Count > RepairNodes) return false;
        }

        var gated = _g._gated;
        bool settled = false;
        for (int pass = 0; pass < RepairPasses && !settled; pass++)
        {
            settled = true;
            for (int k = 0; k < _rNode.Count; k++)
            {
                int n = _rNode[k];
                int col = n >> WalkGraph.LevelBits, x = col % sizeX, z = col / sizeX;
                // The same steps the search allows (see Continue): none out of another player's gate, and no
                // corner cut past one.
                if (gated[col] && !GateLets(f, x, z)) continue;
                int best = _rNew[k];
                for (int d = 0; d < 8; d++)
                {
                    int m = _g.Step(n, d);
                    if (m < 0) continue;
                    int mx = x + DirX[d], mz = z + DirZ[d];
                    if (d >= 4 && ((gated[z * sizeX + mx] && !GateLets(f, mx, z)) || (gated[mz * sizeX + x] && !GateLets(f, x, mz))))
                        continue;
                    int dm = _rIndex.TryGetValue(m, out int mi) ? _rNew[mi] : f.Get(table, mx, mz, m & (WalkGraph.MaxLevels - 1));
                    if (dm >= FlowField.Unreachable) continue;
                    int nd = dm + StepCostOnto(m)[d];
                    if (nd < best) best = nd;
                }
                if (best < _rNew[k])
                {
                    _rNew[k] = best;
                    settled = false;
                }
            }
        }
        if (!settled) return false;

        // Write what changed: moved nodes take the distance their neighbours give them, and nodes the change
        // brought nearer the goal (or connected again, out of a pit) take their new distance and pass it on.
        _rQueue.Clear();
        for (int k = 0; k < _rNode.Count; k++)
        {
            int old = _rOld[k], now = _rNew[k];
            // A node the search had not reached is left to it; others only change if they came nearer.
            if (!_rMoved[k] && (old == FlowField.Unknown || now >= old)) continue;
            int n = _rNode[k];
            int col = n >> WalkGraph.LevelBits, x = col % sizeX, z = col / sizeX, slot = n & (WalkGraph.MaxLevels - 1);
            int v = now >= FlowField.Unreachable ? FlowField.Unknown : now;
            if (v != FlowField.Unknown || f.Get(table, x, z, slot) != FlowField.Unknown) f.Set(table, x, z, slot, v, _pool);
            if (v != FlowField.Unknown) _rQueue.Enqueue(n);
        }
        return Spread(f, table);
    }

    /// <summary>Most distances a <see cref="Spread"/> lowers before leaving the rest to a recomputation.</summary>
    private const int SpreadLimit = 20_000;
    private readonly Queue<int> _rQueue = new();

    /// <summary>
    /// Passes lowered distances on from the queued nodes to every node the field reached that steps onto them
    /// and is now nearer the goal through them, as the search would have. Returns false if that came to more
    /// than <see cref="SpreadLimit"/> nodes: the field holds a mix of old and new distances, every one still a
    /// way to the goal, until it is recomputed.
    /// </summary>
    private bool Spread(FlowField f, ushort[]?[] table)
    {
        int sizeX = _g.SizeX;
        var intoEdges = _g._into;
        var gated = _g._gated;
        int work = 0;
        while (_rQueue.Count > 0)
        {
            if (++work > SpreadLimit)
            {
                _rQueue.Clear();
                return false;
            }
            int m = _rQueue.Dequeue();
            int col = m >> WalkGraph.LevelBits, x = col % sizeX, z = col / sizeX;
            int dist = f.Get(table, x, z, m & (WalkGraph.MaxLevels - 1));
            var stepCost = StepCostOnto(m);
            uint into = intoEdges[m];
            while (into != 0)
            {
                int d = BitOperations.TrailingZeroCount(into) >> 2;
                uint ofDir = into & (0xFu << (4 * d));
                into &= ~ofDir;
                int px = x - DirX[d], pz = z - DirZ[d];
                if (gated[pz * sizeX + px] && !GateLets(f, px, pz)) continue;
                if (d >= 4 && ((gated[pz * sizeX + x] && !GateLets(f, x, pz)) || (gated[z * sizeX + px] && !GateLets(f, px, z))))
                    continue;
                int nd = dist + stepCost[d];
                for (uint slots = ofDir >> (4 * d); slots != 0; slots &= slots - 1)
                {
                    int sl = BitOperations.TrailingZeroCount(slots);
                    int dp = f.Get(table, px, pz, sl);
                    if (dp >= FlowField.Unreachable || nd >= dp) continue; // not reached, or no nearer this way
                    f.Set(table, px, pz, sl, nd, _pool);
                    _rQueue.Enqueue(((pz * sizeX + px) << WalkGraph.LevelBits) | sl);
                }
            }
        }
        return true;
    }

    private int Distance(ushort[]?[] table, int node)
    {
        int col = node >> WalkGraph.LevelBits;
        return _computing!.Get(table, col % _g.SizeX, col / _g.SizeX, node & (WalkGraph.MaxLevels - 1));
    }

    private bool Known(ushort[]?[] table, int node) => Distance(table, node) != FlowField.Unknown;

    private void Finish(bool exhausted)
    {
        var f = _computing!;
        // Nodes reached but not settled before an early stop only have upper bounds: forget them.
        for (int b = 0; b < BucketCount; b++)
        {
            var items = _buckets[b];
            for (int k = 0, n = _bucketCount[b]; k < n; k++)
            {
                int packed = items[k];
                int x = packed & CoordMask, z = (packed >> CoordBits) & CoordMask, slot = packed >> (2 * CoordBits);
                if (f.Get(f.Next!, x, z, slot) + ToBox(x, z) > _stopAt) f.Set(f.Next!, x, z, slot, FlowField.Unknown, _pool);
            }
            _bucketCount[b] = 0;
        }
        FlowField.Release(f.Current, _pool);
        f.Current = f.Next;
        f.Next = null;
        f.Complete = exhausted;
        _computing = null;
    }

    private void Abort()
    {
        var f = _computing!;
        FlowField.Release(f.Next, _pool);
        f.Next = null;
        _computing = null;
        Array.Clear(_bucketCount);
    }

    private void Push(int bucket, int packed)
    {
        int n = _bucketCount[bucket];
        var items = _buckets[bucket];
        if (n == items.Length) _buckets[bucket] = items = Grow(items);
        items[n] = packed;
        _bucketCount[bucket] = n + 1;
    }

    private static int[] Grow(int[] items)
    {
        var bigger = new int[items.Length * 2];
        Array.Copy(items, bigger, items.Length);
        return bigger;
    }
}
