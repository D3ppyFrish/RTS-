using System;
using System.Collections.Generic;

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

    internal bool Touches(int region) =>
        (Current != null && Current[region] != null) || (Next != null && Next[region] != null);

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
    /// Ticks after which a field whose terrain changed is recomputed even if no unit has run into the change
    /// (picks up new shortcuts). Changes on a unit's way are handled sooner, see <see cref="FlowField.Broken"/>.
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

    // Larger than the most a queue key can grow in one step: the dearest step (a wading diagonal costs 28)
    // plus as much again for moving away from the units.
    private const int BucketCount = 64;
    // Bucket entries pack a node's column and slot: x | z << 10 | slot << 20.
    private const int CoordBits = 10;
    private const int CoordMask = (1 << CoordBits) - 1;

    private readonly WalkGraph _g;
    private readonly SortedDictionary<int, FlowField> _byId = new();
    private readonly SortedDictionary<long, FlowField> _byGoal = new();
    private readonly Stack<ushort[]> _pool = new();
    private readonly List<int>[] _buckets = new List<int>[BucketCount];
    private readonly int[] _wantStamp;
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
    // 1 halves the heuristic, which keeps it a lower bound where roads halve step costs.
    private int _heuristicShift;

    public PathService(WalkGraph graph)
    {
        if (graph.SizeX > CoordMask + 1 || graph.SizeZ > CoordMask + 1)
            throw new ArgumentException("Flow fields support worlds up to 1024 blocks across", nameof(graph));
        _g = graph;
        for (int i = 0; i < BucketCount; i++) _buckets[i] = new List<int>();
        _wantStamp = new int[graph.NodeCount];
        _pocketStamp = new int[graph.NodeCount];
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
        // Terrain changes mark the fields they touch for recomputation. A computation in progress keeps
        // going (restarting it on every dig nearby could starve it) and is redone afterwards.
        _g.Update();
        foreach (int r in _g.ChangedRegions)
            foreach (var f in _byId.Values)
                if (f.Touches(r)) f.Dirty = true;

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
            bool stale = (f.Dirty && (f.Broken || age >= DirtyRefreshInterval)) || (f.WantsMore && !f.Complete);
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
        f.WantsMore = false;
        f.Broken = false;
        f.StartedTick = tick;
        // With roads on the map a step can cost half as much, so the heuristic must assume roads everywhere.
        // A computation keeps the choice it starts with; roads laid meanwhile count from the next one.
        _heuristicShift = _g.RoadNodes > 0 ? 1 : 0;
        foreach (var b in _buckets) b.Clear();
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
        _buckets[_cur & (BucketCount - 1)].Add(gx | gz << CoordBits | gs << (2 * CoordBits));
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
    /// A lower bound on the path cost from column (x, z) to the nearest unit in the box. It never drops by
    /// more than a step costs, which keeps the queue order exact (a consistent heuristic). Halved while
    /// roads exist, since a road step costs half.
    /// </summary>
    private int ToBox(int x, int z)
    {
        int dx = x < _boxMinX ? _boxMinX - x : x > _boxMaxX ? x - _boxMaxX : 0;
        int dz = z < _boxMinZ ? _boxMinZ - z : z > _boxMaxZ ? z - _boxMaxZ : 0;
        return (dx > dz ? 10 * dx + 4 * dz : 10 * dz + 4 * dx) >> _heuristicShift;
    }

    /// <summary>Runs the current computation for up to <paramref name="budget"/> settled nodes; returns the work done.</summary>
    private int Continue(int budget)
    {
        var f = _computing!;
        var next = f.Next!;
        int sizeX = _g.SizeX, sizeZ = _g.SizeZ, regionsX = _g.RegionsX;
        var level = _g._level;
        var edges = _g._edges;
        var ground = _g._ground;
        var gated = _g._gated;
        var dirs = WalkGraph.Dirs;
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
            var bucket = _buckets[_cur & (BucketCount - 1)];
            if (bucket.Count == 0)
            {
                _cur++;
                continue;
            }
            int packed = bucket[^1];
            bucket.RemoveAt(bucket.Count - 1);
            _pending--;
            int x = packed & CoordMask, z = (packed >> CoordBits) & CoordMask, slot = packed >> (2 * CoordBits);
            int dist = f.Get(next, x, z, slot);
            if (dist + ToBox(x, z) != _cur) continue; // superseded by a shorter path
            used++;
            Expansions++;
            int m = ((z * sizeX + x) << WalkGraph.LevelBits) | slot;
            if (_wantStamp[m] == _wantId)
            {
                _wantStamp[m] = 0;
                if (--_wantedLeft == 0) _stopAt = _cur + Margin;
            }
            // The cost of stepping onto m. Roads count only if the heuristic allows for them (see Start).
            int kind = ground[m];
            var stepCost = WalkGraph.StepCost[kind == WalkGraph.OnRoad && _heuristicShift == 0 ? WalkGraph.Plain : kind];

            // Relax every node that steps onto m: those in the column behind each direction.
            for (int d = 0; d < 8; d++)
            {
                int px = x - dirs[d].X, pz = z - dirs[d].Z;
                if ((uint)px >= (uint)sizeX || (uint)pz >= (uint)sizeZ) continue;
                // Another player's gate is a wall: nobody gets there, and no corner is cut past one.
                if (gated[pz * sizeX + px] && !GateLets(f, px, pz)) continue;
                if (d >= 4 && ((gated[pz * sizeX + x] && !GateLets(f, x, pz)) || (gated[z * sizeX + px] && !GateLets(f, px, z))))
                    continue;
                int nd = dist + stepCost[d];
                if (nd >= FlowField.Unreachable) continue; // farther than a distance can say
                int key = -1;
                int first = (pz * sizeX + px) << WalkGraph.LevelBits;
                int shift = 3 * d;
                int r = (pz >> 4) * regionsX + (px >> 4);
                var block = next[r];
                int localBase = (((pz & 15) << 4) | (px & 15)) << WalkGraph.LevelBits;
                for (int s = 0; s < WalkGraph.MaxLevels; s++)
                {
                    int n = first + s;
                    if (level[n] < 0) break;
                    uint e = (edges[n] >> shift) & 7;
                    if ((e & 4) == 0 || (int)(e & 3) != slot) continue;
                    if (block != null && nd >= block[localBase | s]) continue;
                    if (block == null)
                    {
                        f.Set(next, px, pz, s, nd, _pool);
                        block = next[r];
                    }
                    else block[localBase | s] = (ushort)nd;
                    if (key < 0) key = nd + ToBox(px, pz);
                    _buckets[key & (BucketCount - 1)].Add(px | pz << CoordBits | s << (2 * CoordBits));
                    _pending++;
                }
            }
        }
    }

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
        foreach (var b in _buckets)
        {
            foreach (int packed in b)
            {
                int x = packed & CoordMask, z = (packed >> CoordBits) & CoordMask, slot = packed >> (2 * CoordBits);
                if (f.Get(f.Next!, x, z, slot) + ToBox(x, z) > _stopAt) f.Set(f.Next!, x, z, slot, FlowField.Unknown, _pool);
            }
            b.Clear();
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
        foreach (var b in _buckets) b.Clear();
    }
}
