using System;

namespace Rts.Sim.Units;

/// <summary>
/// Moves units each tick: following their order's flow field (or straight at the target until it is
/// ready), separation so crowds spread out, stepping across terrain (up one block, down up to three),
/// falling when the ground is dug away, and settling when a group reaches its destination. Units on a
/// road walk twice as fast. A unit posted on a wall too high to step off climbs down when an order takes it
/// somewhere it cannot walk to from up there.
/// </summary>
public sealed class UnitMovement
{
    /// <summary>Distance (blocks) below which two units push each other apart.</summary>
    public static readonly Fix64 SeparationRadius = Fix64.FromRatio(7, 10);
    /// <summary>Largest separation push per tick.</summary>
    public static readonly Fix64 MaxPush = Fix64.FromRatio(1, 20);
    /// <summary>Ticks a unit may be stuck before it gives up its order.</summary>
    public const int GiveUpTicks = 60;
    /// <summary>
    /// Most neighbours a unit reacts to per tick. In a pile (a pit, a jammed ford) the push is at its cap
    /// long before this, and it keeps the cost of a pile linear in its size.
    /// </summary>
    public const int MaxNeighbours = 10;

    private readonly Simulation _sim;
    private readonly SpatialGrid _grid;
    private Fix64[] _pushX = Array.Empty<Fix64>();
    private Fix64[] _pushZ = Array.Empty<Fix64>();
    private bool[] _settledNeighbour = Array.Empty<bool>();
    private byte[] _neighbours = Array.Empty<byte>();
    // The column (z * SizeX + x) of the flow-field node a unit is heading for this tick, or -1.
    private int[] _waypoint = Array.Empty<int>();
    // The unit steered this tick by keeping its place in its formation (see SteerFormation).
    private bool[] _keepingPlace = Array.Empty<bool>();
    // The field of the last unit steered; units ordered together sit next to each other in the arrays.
    private int _cachedFieldId = -1;
    private FlowField? _cachedField;

    public UnitMovement(Simulation sim)
    {
        _sim = sim;
        _grid = new SpatialGrid(sim.World.SizeX, sim.World.SizeZ);
    }

    public SpatialGrid Grid => _grid;

    public void Step()
    {
        var u = _sim.Units;
        var w = _sim.World;
        if (u.Count == 0) return;
        if (_pushX.Length < u.Capacity)
        {
            _pushX = new Fix64[u.Capacity];
            _pushZ = new Fix64[u.Capacity];
            _settledNeighbour = new bool[u.Capacity];
            _neighbours = new byte[u.Capacity];
            _waypoint = new int[u.Capacity];
            _keepingPlace = new bool[u.Capacity];
        }

        // 1. Gravity and being buried: keep every unit on valid ground.
        for (int i = 0; i < u.Count; i++) Settle(w, u, i);

        // 2. Separation forces from current positions (computed for all units before anyone moves).
        _grid.Build(u);
        ComputeSeparation(u);

        // 3. Steering and movement, in index order.
        _cachedFieldId = -1;
        for (int i = 0; i < u.Count; i++)
        {
            Fix64 vx = _pushX[i], vz = _pushZ[i];
            // A unit with no order is only ever nudged (by the crowd, or sliding on ice): that never drops it
            // more than a level, so a worker beside a hole is not shoved down into it.
            _waypoint[i] = u.Order[i] == UnitOrder.Idle ? NoDrop : -1;
            _keepingPlace[i] = false;
            if (u.Loose[i] > 0) u.Loose[i]--;
            if (u.Order[i] != UnitOrder.Idle) Steer(u, i, ref vx, ref vz);
            bool icy = OnIce(w, u, i);
            if (icy)
            {
                // Slippery: most of last tick's slide carries on, and only a little of where it wants to go.
                vx = u.SlideX[i] * IceKeep + vx * (Fix64.One - IceKeep);
                vz = u.SlideZ[i] * IceKeep + vz * (Fix64.One - IceKeep);
                if (Fix64.Abs(vx) + Fix64.Abs(vz) < SlideStop) vx = vz = Fix64.Zero;
            }
            u.SlideX[i] = u.SlideZ[i] = Fix64.Zero;
            if (vx == Fix64.Zero && vz == Fix64.Zero) continue;
            int wp = _waypoint[i];
            Fix64 fromX = u.X[i], fromZ = u.Z[i];
            bool moved = TryMove(w, u, i, vx, vz, wp) || TryMove(w, u, i, vx, Fix64.Zero, wp) || TryMove(w, u, i, Fix64.Zero, vz, wp);
            if (icy && moved)
            {
                // What it slid keeps going next tick (bumping into something stops that way).
                u.SlideX[i] = u.X[i] - fromX;
                u.SlideZ[i] = u.Z[i] - fromZ;
            }
            if (u.Order[i] != UnitOrder.Idle)
            {
                u.Blocked[i] = moved ? 0 : u.Blocked[i] + 1;
                if (_keepingPlace[i] && u.Blocked[i] >= LooseAfter)
                {
                    // Keeping its place in the formation walked it into something the formation's middle
                    // goes around: it finds its own way for a while.
                    u.Loose[i] = LooseTicks;
                    u.Blocked[i] = 0;
                }
                if (u.Blocked[i] >= GiveUpTicks) EndOrder(u, i);
            }
        }
    }

    /// <summary>
    /// Adds a moving unit's own velocity: along its flow field toward the next node's centre, or straight
    /// at the goal when it is in the goal column or the field is not ready or does not reach it yet.
    /// Ends the order on arrival, or when the field shows there is no way to the goal. A chase does not end
    /// on arrival (the combat system stops it once the target is in reach), and close to the unit it chases
    /// it heads straight for it.
    /// </summary>
    private void Steer(UnitStore u, int i, ref Fix64 vx, ref Fix64 vz)
    {
        var full = _sim.Economy.SpeedPerTick(u.Owner[i], _sim.Content.Units[u.Def[i]]);
        int spur = _sim.Combat.SpurOf(i); // a hero leading the charge near it
        if (spur > 0) full = full * (100 + spur) / 100;
        var speed = full;
        bool formation = u.Formation[i] && u.Order[i] == UnitOrder.Move;
        // A spurred formation keeps the slowest member's pace, a spurred one's faster (riders round the hero
        // stay together).
        if (formation && u.SpeedCap[i] > Fix64.Zero)
            speed = Fix64.Min(speed, spur > 0 ? u.SpeedCap[i] * (100 + spur) / 100 : u.SpeedCap[i]);
        var underfoot = _sim.World.Get(u.X[i].FloorToInt(), u.Y[i] - 1, u.Z[i].FloorToInt());
        if (underfoot == BlockType.Road) { speed *= 2; full *= 2; }
        else if (BlockInfo.IsRubble(underfoot)) { speed /= 2; full /= 2; } // picking a way over a heap of rubble
        else if (underfoot == BlockType.Quicksand) { speed /= Rts.Sim.World.HazardSystem.QuicksandSlow; full /= Rts.Sim.World.HazardSystem.QuicksandSlow; } // the sand grips
        if (formation)
        {
            SteerFormation(u, i, speed, full, ref vx, ref vz);
            return;
        }
        bool chase = u.Order[i] == UnitOrder.Chase;
        if (chase && CloseTarget(u, i, out var px, out var pz))
        {
            Fix64 tx = px - u.X[i], tz = pz - u.Z[i];
            Fix64 d = Fix64.Sqrt(tx * tx + tz * tz);
            if (d > ChaseStop)
            {
                vx += tx * Fix64.Min(speed, d - ChaseStop) / d;
                vz += tz * Fix64.Min(speed, d - ChaseStop) / d;
            }
            return;
        }
        if (u.Field[i] != _cachedFieldId)
        {
            _cachedFieldId = u.Field[i];
            _cachedField = _sim.Paths.Get(_cachedFieldId);
        }
        if (_cachedField is { Ready: true } field)
        {
            var g = _sim.Walkable;
            int x = u.X[i].FloorToInt(), z = u.Z[i].FloorToInt();
            int node = g.Node(x, z, u.Y[i]);
            int pathDist = node < 0 ? FlowField.Unknown : field.Distance(x, z, node & (WalkGraph.MaxLevels - 1));
            if (pathDist >= FlowField.Unreachable && node >= 0)
            {
                // The unit is stuck where it is (say it fell into a pit), or the field covers everything
                // reachable from the goal and not this unit: there is no way there, so give up. Otherwise the
                // field should grow to reach it. A complete field whose terrain changed since may have missed
                // a new way, so then wait for its update.
                if (pathDist == FlowField.Unreachable || (field.Complete && !field.Dirty))
                {
                    GiveUp(u, i);
                    return;
                }
                field.WantsMore = true;
            }
            else if (pathDist > 0 && pathDist < FlowField.Unreachable)
            {
                int d = field.NextDirection(node, x, z, out int via);
                if (via != pathDist && field.Dirty) field.Broken = true;
                if (d >= 0)
                {
                    Fix64 remaining = Fix64.FromInt(pathDist) / 10;
                    if (!chase && (remaining <= u.ArriveRadius[i] || (_settledNeighbour[i] && remaining <= u.ArriveRadius[i] * 3 / 2)))
                    {
                        EndOrder(u, i);
                        return;
                    }
                    // Head for the centre of the next node's column at full speed: it is only a waypoint.
                    _waypoint[i] = (z + WalkGraph.Dirs[d].Z) * g.SizeX + x + WalkGraph.Dirs[d].X;
                    Fix64 nx = Fix64.FromInt(x + WalkGraph.Dirs[d].X) + Fix64.Half - u.X[i];
                    Fix64 nz = Fix64.FromInt(z + WalkGraph.Dirs[d].Z) + Fix64.Half - u.Z[i];
                    Fix64 len = Fix64.Sqrt(nx * nx + nz * nz);
                    if (len > Fix64.Zero)
                    {
                        vx += nx * speed / len;
                        vz += nz * speed / len;
                    }
                    return;
                }
            }
        }

        // Straight at the goal: in the goal column, or no usable field here.
        Fix64 dx = Fix64.FromInt(u.OrderX[i]) + Fix64.Half - u.X[i];
        Fix64 dz = Fix64.FromInt(u.OrderZ[i]) + Fix64.Half - u.Z[i];
        Fix64 dist = Fix64.Sqrt(dx * dx + dz * dz);
        if (chase)
        {
            if (dist <= ChaseStop) return; // there: the combat system decides what next
        }
        else if (dist <= u.ArriveRadius[i] || (_settledNeighbour[i] && dist <= u.ArriveRadius[i] * 3 / 2))
        {
            EndOrder(u, i);
            return;
        }
        Fix64 step = Fix64.Min(speed, dist);
        vx += dx * step / dist;
        vz += dz * step / dist;
    }

    /// <summary>
    /// The share of last tick's slide a unit on ice keeps (see <see cref="BlockType.Ice"/>): the rest of its
    /// step is where it wants to go. So it takes about a second to reach full speed, swings wide on a turn,
    /// and slides on about a block and a half after it stops.
    /// </summary>
    public static readonly Fix64 IceKeep = Fix64.FromRatio(7, 8);
    /// <summary>A slide slower than this (blocks per tick, |x| + |z|) stops.</summary>
    public static readonly Fix64 SlideStop = Fix64.FromRatio(1, 64);

    /// <summary>Unit i stands on ice.</summary>
    public static bool OnIce(VoxelWorld w, UnitStore u, int i) =>
        u.Y[i] >= 1 && w.Get(u.X[i].FloorToInt(), u.Y[i] - 1, u.Z[i].FloorToInt()) == BlockType.Ice;

    /// <summary>A formation unit this close (blocks) to its place walks straight to it.</summary>
    public static readonly Fix64 FormationClose = Fix64.FromRatio(5, 2);
    /// <summary>Ticks in a row a unit keeping its formation place may be stuck before it finds its own way.</summary>
    public const int LooseAfter = 8;
    /// <summary>Ticks a formation unit that got stuck finds its own way before it tries keeping its place again.</summary>
    public const int LooseTicks = 40;
    /// <summary>A waypoint no column matches: the unit does not drop more than a level anywhere.</summary>
    private const int NoDrop = int.MaxValue;

    /// <summary>
    /// A unit on a formation move. All the formation's units share a flow field to the formation's middle
    /// (the order's column); each keeps its place relative to the middle by stepping the way the field leads
    /// from the point that place is offset from: it moves as if it were the middle, so the formation keeps
    /// its shape across open ground, at the pace of its slowest unit. It only does so while that step also
    /// takes it nearer the middle along its own way (#37): where keeping its place would lead it into a wall,
    /// round the wrong side of a gap or back the way it came, it follows the field from where it stands
    /// instead, at its own full speed so it catches up, and takes its place again once it can. Near the end
    /// (that point at the middle, or the unit about as near the middle as its place is) it walks straight to
    /// its place if the way there is clear; if not, it walks on to the middle first, and stops there if its
    /// place still cannot be reached straight from it.
    /// </summary>
    private void SteerFormation(UnitStore u, int i, Fix64 speed, Fix64 fullSpeed, ref Fix64 vx, ref Fix64 vz)
    {
        Fix64 sx = u.SlotX[i] - u.X[i], sz = u.SlotZ[i] - u.Z[i];
        Fix64 toSlot = Fix64.Sqrt(sx * sx + sz * sz);
        if (toSlot <= u.ArriveRadius[i] || (_settledNeighbour[i] && toSlot <= u.ArriveRadius[i] * 3 / 2))
        {
            EndOrder(u, i);
            return;
        }
        if (u.Field[i] != _cachedFieldId)
        {
            _cachedFieldId = u.Field[i];
            _cachedField = _sim.Paths.Get(_cachedFieldId);
        }
        if (toSlot <= FormationClose && ClearTo(u, i, u.SlotX[i], u.SlotZ[i])) goto straight;
        if (_cachedField is not { Ready: true } field) goto straight; // no way known yet: head for its place
        {
            var g = _sim.Walkable;
            int x = u.X[i].FloorToInt(), z = u.Z[i].FloorToInt();
            int node = g.Node(x, z, u.Y[i]);
            int pathDist = node < 0 ? FlowField.Unknown : field.Distance(node);
            if (pathDist >= FlowField.Unreachable)
            {
                if (node < 0) goto straight; // between levels for a moment (falling): carry on
                if (pathDist == FlowField.Unreachable || (field.Complete && !field.Dirty))
                {
                    GiveUp(u, i);
                    return;
                }
                field.WantsMore = true;
                goto straight;
            }
            Fix64 ox = u.SlotX[i] - Fix64.FromInt(u.OrderX[i]) - Fix64.Half;
            Fix64 oz = u.SlotZ[i] - Fix64.FromInt(u.OrderZ[i]) - Fix64.Half;
            bool nearEnd = Fix64.FromInt(pathDist) / 10 <= Fix64.Sqrt(ox * ox + oz * oz) + Fix64.FromInt(2);
            // Its own way: the next column on its path to the middle.
            int dir = field.NextDirection(node, x, z, out int via);
            if (via != pathDist && field.Dirty) field.Broken = true;
            // Keep its place: step the way the field leads from where the middle would be, if that step is
            // also on its own way.
            if (u.Loose[i] == 0 && toSlot > FormationClose && !nearEnd)
            {
                Fix64 px = u.X[i] - ox, pz = u.Z[i] - oz;
                int mx = px.FloorToInt(), mz = pz.FloorToInt();
                int m = g.NearestNode(mx, mz, u.Y[i]);
                if (m >= 0 && Math.Abs(g.Level(m) - u.Y[i]) <= 1)
                {
                    int md = field.Distance(m);
                    if (md <= 10)
                    {
                        nearEnd = true; // the middle is there: time to take its place
                    }
                    else if (md < FlowField.Unreachable)
                    {
                        int d = field.NextDirection(m, mx, mz, out _);
                        int own = d < 0 ? -1 : g.Step(node, d);
                        Fix64 nx = Fix64.Zero, nz = Fix64.Zero;
                        if (d >= 0)
                        {
                            nx = Fix64.FromInt(mx + WalkGraph.Dirs[d].X) + Fix64.Half - px;
                            nz = Fix64.FromInt(mz + WalkGraph.Dirs[d].Z) + Fix64.Half - pz;
                        }
                        Fix64 len = Fix64.Sqrt(nx * nx + nz * nz);
                        // On its own way too: a step nearer the middle along its own path, no more than a
                        // right angle off the way its path goes from here.
                        if (own >= 0 && field.Distance(own) < pathDist && len > Fix64.Zero && dir >= 0
                            && WalkGraph.Dirs[d].X * WalkGraph.Dirs[dir].X + WalkGraph.Dirs[d].Z * WalkGraph.Dirs[dir].Z >= 0)
                        {
                            vx += nx * speed / len;
                            vz += nz * speed / len;
                            _waypoint[i] = g.Column(own); // a drop on its way down is fine; nowhere else
                            _keepingPlace[i] = true;
                            return;
                        }
                        // Keeping its place leads it off its way here: it goes its own way for a while rather than
                        // swinging between the two.
                        u.Loose[i] = LooseTicks;
                    }
                }
            }
            if (nearEnd && ClearTo(u, i, u.SlotX[i], u.SlotZ[i])) goto straight;
            if (pathDist == 0)
            {
                // At the middle, and no clear way from here to its place: it stops here.
                EndOrder(u, i);
                return;
            }
            // Its own way to the middle, at its own speed.
            if (dir < 0) goto straight;
            _waypoint[i] = (z + WalkGraph.Dirs[dir].Z) * g.SizeX + x + WalkGraph.Dirs[dir].X;
            Fix64 wx = Fix64.FromInt(x + WalkGraph.Dirs[dir].X) + Fix64.Half - u.X[i];
            Fix64 wz = Fix64.FromInt(z + WalkGraph.Dirs[dir].Z) + Fix64.Half - u.Z[i];
            Fix64 wl = Fix64.Sqrt(wx * wx + wz * wz);
            if (wl > Fix64.Zero)
            {
                vx += wx * fullSpeed / wl;
                vz += wz * fullSpeed / wl;
            }
            return;
        }
        straight:
        // Straight to its place.
        Fix64 step = Fix64.Min(speed, toSlot);
        vx += sx * step / toSlot;
        vz += sz * step / toSlot;
    }

    /// <summary>Longest straight walk (blocks) <see cref="ClearTo"/> checks.</summary>
    public const int ClearReach = 24;

    /// <summary>
    /// True if unit i can walk in a straight line from where it stands to (tx, tz): every column the line
    /// crosses is a step it can take (up one, or down one; corners as <see cref="TryMove"/> takes them), and
    /// none is shut to it. False past <see cref="ClearReach"/> blocks.
    /// </summary>
    private bool ClearTo(UnitStore u, int i, Fix64 tx, Fix64 tz)
    {
        var w = _sim.World;
        Fix64 dx = tx - u.X[i], dz = tz - u.Z[i];
        int steps = (int)((Fix64.Abs(dx) + Fix64.Abs(dz)) * 4).FloorToInt() + 1;
        if (steps > ClearReach * 8) return false;
        int cx = u.X[i].FloorToInt(), cz = u.Z[i].FloorToInt(), cy = u.Y[i];
        for (int k = 1; k <= steps; k++)
        {
            int nx = (u.X[i] + dx * k / steps).FloorToInt(), nz = (u.Z[i] + dz * k / steps).FloorToInt();
            if (nx == cx && nz == cz) continue;
            if (nx != cx && nz != cz
                && (!Walk.TryStep(w, cx, cz, cy, nx, cz, out _) || !Walk.TryStep(w, cx, cz, cy, cx, nz, out _)))
                return false;
            if (!Walk.TryStep(w, cx, cz, cy, nx, nz, out int ny) || ny < cy - 1 || Shut(w, u.Owner[i], nx, ny, nz)) return false;
            (cx, cz, cy) = (nx, nz, ny);
        }
        return true;
    }

    /// <summary>How close (blocks) a chaser comes to the middle of its goal, or to the unit it chases.</summary>
    public static readonly Fix64 ChaseStop = Fix64.FromRatio(3, 10);
    /// <summary>A chaser this close (blocks, and a level) to the unit it chases heads straight for it.</summary>
    public static readonly Fix64 ChaseClose = Fix64.FromInt(2);

    /// <summary>Where the unit a chaser is after stands, if it is close enough to head straight for.</summary>
    private static bool CloseTarget(UnitStore u, int i, out Fix64 x, out Fix64 z)
    {
        x = z = Fix64.Zero;
        int t = u.Target[i] != 0 ? u.IndexOf(u.Target[i]) : -1;
        if (t < 0 || Math.Abs(u.Y[t] - u.Y[i]) > 1) return false;
        x = u.X[t];
        z = u.Z[t];
        return Fix64.Abs(x - u.X[i]) <= ChaseClose && Fix64.Abs(z - u.Z[i]) <= ChaseClose;
    }

    /// <summary>
    /// A unit on top of a wall, gate or tower, with an order and no way on from up there (it is too high to
    /// step off), climbs down (and off any other building's roof, see <see cref="OffRoof"/>): it is set on the ground at the foot of the wall (see
    /// <see cref="Economy.EconomySystem.Foot"/>), on the side nearest to where it is going, and walks on from
    /// there. False if it stands on no wall, or the wall has no ground at its foot.
    /// </summary>
    private bool ClimbDown(UnitStore u, int i)
    {
        int x, z, y;
        if (_sim.Economy.WallUnder(i) is { } wall)
        {
            if (!_sim.Economy.Foot(wall, u.X[i].FloorToInt(), u.Z[i].FloorToInt(), u.OrderX[i], u.OrderZ[i], out x, out z, out y))
                return false;
        }
        else if (!OffRoof(u, i, out x, out z, out y)) return false;
        u.X[i] = Fix64.FromInt(x) + Fix64.Half;
        u.Z[i] = Fix64.FromInt(z) + Fix64.Half;
        u.Y[i] = y;
        u.Blocked[i] = 0;
        return true;
    }

    /// <summary>
    /// There is no way from where unit i stands to where it is going. Up on a wall it climbs down; in a pit or
    /// hole it cannot walk out of, a worker or sapper digs its way out (see <see cref="CombatSystem.DigOut"/>);
    /// otherwise it stops.
    /// </summary>
    private void GiveUp(UnitStore u, int i)
    {
        if (ClimbDown(u, i)) return;
        var field = _sim.Paths.Get(u.Field[i]);
        bool chase = u.Order[i] == UnitOrder.Chase;
        var combat = u.Combat[i];
        int patrol = u.PatrolState[i];
        EndOrder(u, i);
        if (_sim.Combat.DigOut(i) || chase || field == null || field.Fallback) return;
        // Somewhere it cannot get to (a cliff top, across deep water, up a tree): it goes as near as it can.
        if (field.FallbackNode < 0) field.FallbackNode = NearestReachable(u, i, field.GoalX, field.GoalY, field.GoalZ);
        int n = field.FallbackNode;
        if (n < 0) return;
        var g = _sim.Walkable;
        int x = g.ColumnX(n), z = g.ColumnZ(n);
        Fix64 dx = Fix64.FromInt(x) + Fix64.Half - u.X[i], dz = Fix64.FromInt(z) + Fix64.Half - u.Z[i];
        if (Fix64.Sqrt(dx * dx + dz * dz) <= u.ArriveRadius[i] + Fix64.One) return; // already about as near as it gets
        var near = _sim.Paths.Request(x, z, g.Level(n), u.Owner[i]);
        near.Fallback = true;
        // A formation that cannot get to its places gathers there as a crowd does (see Simulation.ApplyMove).
        if (u.Formation[i])
        {
            u.ClearFormation(i);
            var crowd = Fix64.FromRatio(3, 10) + (Fix64.Sqrt(Fix64.FromInt(Math.Max(1, field.Users))) - Fix64.One) * Fix64.FromRatio(55, 100);
            u.ArriveRadius[i] = Fix64.Max(u.ArriveRadius[i], crowd);
        }
        u.Order[i] = UnitOrder.Move;
        u.OrderX[i] = x;
        u.OrderZ[i] = z;
        u.Field[i] = near.Id;
        u.Blocked[i] = 0;
        // An attack-move or patrol carries on, to the new goal.
        u.Combat[i] = combat;
        u.PatrolState[i] = patrol;
    }

    /// <summary>Most nodes searched for the nearest reachable place to a goal a unit cannot get to.</summary>
    public const int NearestSearchLimit = 40_000;
    private int[] _seen = Array.Empty<int>();
    private int _seenStamp;
    private readonly System.Collections.Generic.Queue<int> _open = new();

    /// <summary>
    /// The node unit i can walk to that is nearest to the goal (across first, then in height; ties to the
    /// lowest node number), searching at most <see cref="NearestSearchLimit"/> nodes; -1 if it stands nowhere.
    /// </summary>
    private int NearestReachable(UnitStore u, int i, int gx, int gy, int gz)
    {
        var g = _sim.Walkable;
        int start = g.Node(u.X[i].FloorToInt(), u.Z[i].FloorToInt(), u.Y[i]);
        if (start < 0) return -1;
        if (_seen.Length < g.NodeCount) _seen = new int[g.NodeCount];
        _seenStamp++;
        _open.Clear();
        _open.Enqueue(start);
        _seen[start] = _seenStamp;
        int best = -1, visited = 0;
        long bestScore = long.MaxValue;
        while (_open.Count > 0 && visited < NearestSearchLimit)
        {
            int a = _open.Dequeue();
            visited++;
            long dx = g.ColumnX(a) - gx, dz = g.ColumnZ(a) - gz, dy = g.Level(a) - gy;
            long score = (dx * dx + dz * dz) * 1024 + Math.Abs(dy);
            if (score < bestScore || (score == bestScore && a < best))
            {
                bestScore = score;
                best = a;
            }
            for (int d = 0; d < 8; d++)
            {
                int b = g.Step(a, d);
                if (b < 0 || _seen[b] == _seenStamp) continue;
                _seen[b] = _seenStamp;
                _open.Enqueue(b);
            }
        }
        return best;
    }

    /// <summary>Nodes found by the last pocket check (see <see cref="OffRoof"/>).</summary>
    private readonly System.Collections.Generic.HashSet<int> _reach = new();

    /// <summary>
    /// A unit stuck on the roof of some other building (a house, a hall: it got shoved up there, or was there
    /// when it was built) climbs down too: to the ground beside the building nearest to where it is going,
    /// somewhere it can walk on from. False if it is not on a building, or there is no such ground.
    /// </summary>
    private bool OffRoof(UnitStore u, int i, out int sx, out int sz, out int sy)
    {
        sx = sz = sy = -1;
        int ux = u.X[i].FloorToInt(), uz = u.Z[i].FloorToInt();
        if (_sim.Buildings.At(ux, u.Y[i] - 1, uz) is not { } b) return false;
        var g = _sim.Walkable;
        long best = long.MaxValue;
        for (int cz = b.Z - 1; cz <= b.MaxZ + 1; cz++)
        for (int cx = b.X - 1; cx <= b.MaxX + 1; cx++)
        {
            if (b.Covers(cx, cz)) continue;
            long dx = cx - u.OrderX[i], dz = cz - u.OrderZ[i], score = dx * dx + dz * dz;
            if (score >= best) continue;
            int y = Walk.NearestStandLevel(_sim.World, cx, cz, b.Y, 2);
            int n = y < 0 ? -1 : g.Node(cx, cz, y);
            if (n < 0 || !g.ReachesMoreThan(n, PathService.PocketLimit, _reach)) continue;
            best = score;
            (sx, sz, sy) = (cx, cz, y);
        }
        return best != long.MaxValue;
    }

    private static void EndOrder(UnitStore u, int i)
    {
        // The end of an attack-move's walk (arrived, or no way on) is the end of the attack-move.
        if (u.Order[i] == UnitOrder.Move && u.Combat[i] == CombatOrder.AttackMove) u.Combat[i] = CombatOrder.Auto;
        // On a patrol it turns round (see CombatSystem).
        if (u.Order[i] == UnitOrder.Move && u.Combat[i] == CombatOrder.Patrol) u.PatrolState[i] |= 1;
        u.Order[i] = UnitOrder.Idle;
        u.Blocked[i] = 0;
        u.Field[i] = 0;
    }

    /// <summary>Neighbour columns visited from each unit, so every pair of nearby units is handled once.</summary>
    private static readonly (int X, int Z)[] ForwardColumns = { (1, 0), (-1, 1), (0, 1), (1, 1) };

    /// <summary>
    /// Separation pushes for every unit from current positions. Each pair of nearby units is visited once
    /// and both get their (opposite) push, which halves the square roots compared with a per-unit scan.
    /// Pushes are sums of exact fixed-point values, so the visiting order cannot change the result.
    /// </summary>
    private void ComputeSeparation(UnitStore u)
    {
        Array.Clear(_pushX, 0, u.Count);
        Array.Clear(_pushZ, 0, u.Count);
        Array.Clear(_settledNeighbour, 0, u.Count);
        Array.Clear(_neighbours, 0, u.Count);
        for (int i = 0; i < u.Count; i++)
        {
            // Units later in the same column, then every unit in the four forward columns. A unit that
            // already has its fill of neighbours stops looking, so a pile costs time linear in its size.
            for (int j = _grid.Next(i); j >= 0 && _neighbours[i] < MaxNeighbours; j = _grid.Next(j)) Separate(u, i, j);
            int cx = u.X[i].FloorToInt(), cz = u.Z[i].FloorToInt();
            foreach (var (ox, oz) in ForwardColumns)
                for (int j = _grid.First(cx + ox, cz + oz); j >= 0 && _neighbours[i] < MaxNeighbours; j = _grid.Next(j))
                    Separate(u, i, j);
        }
        for (int i = 0; i < u.Count; i++)
        {
            _pushX[i] = Fix64.Clamp(_pushX[i], -MaxPush, MaxPush);
            _pushZ[i] = Fix64.Clamp(_pushZ[i], -MaxPush, MaxPush);
        }
    }

    private void Separate(UnitStore u, int i, int j)
    {
        if (Math.Abs(u.Y[j] - u.Y[i]) > 1) return;
        Fix64 r = SeparationRadius;
        Fix64 dx = u.X[i] - u.X[j], dz = u.Z[i] - u.Z[j];
        if (Fix64.Abs(dx) >= r || Fix64.Abs(dz) >= r) return;
        Fix64 d2 = dx * dx + dz * dz;
        if (d2 >= r * r) return;
        _neighbours[i]++;
        if (_neighbours[j] < MaxNeighbours) _neighbours[j]++;
        if (u.Group[i] != 0 && u.Group[j] == u.Group[i])
        {
            if (u.Order[j] == UnitOrder.Idle) _settledNeighbour[i] = true;
            if (u.Order[i] == UnitOrder.Idle) _settledNeighbour[j] = true;
        }
        Fix64 d = Fix64.Sqrt(d2);
        if (d == Fix64.Zero)
        {
            // Exactly on top of each other: split them by id so the result does not depend on order.
            Fix64 split = u.Id[i] < u.Id[j] ? -MaxPush : MaxPush;
            _pushX[i] += split;
            _pushX[j] -= split;
            return;
        }
        // Push proportional to overlap, (r - d) / 2 along the line between them. A moving unit pushing
        // into an idle one is only pushed back half as hard, so marching units shove idle ones aside.
        Fix64 k = (r - d) / (d * 2);
        Fix64 px = dx * k, pz = dz * k;
        bool iMoving = u.Order[i] != UnitOrder.Idle, jMoving = u.Order[j] != UnitOrder.Idle;
        if (iMoving && !jMoving) { _pushX[i] += px / 2; _pushZ[i] += pz / 2; }
        else { _pushX[i] += px; _pushZ[i] += pz; }
        if (jMoving && !iMoving) { _pushX[j] -= px / 2; _pushZ[j] -= pz / 2; }
        else { _pushX[j] -= px; _pushZ[j] -= pz; }
    }

    /// <summary>
    /// Moves a unit by (vx, vz) if the terrain allows it. Returns false if it could not move. A unit following
    /// its path (<paramref name="waypoint"/>, the column it heads for, or -1) does not drop more than a level
    /// into any other column, so cutting a corner along the rim of a pit does not land it in the pit. Another
    /// player's gate is shut to it, as is cutting a corner past one.
    /// </summary>
    private bool TryMove(VoxelWorld w, UnitStore u, int i, Fix64 vx, Fix64 vz, int waypoint)
    {
        if (vx == Fix64.Zero && vz == Fix64.Zero) return false;
        Fix64 nx = u.X[i] + vx, nz = u.Z[i] + vz;
        // Stay inside the world.
        if (nx < Fix64.Zero || nz < Fix64.Zero || nx >= Fix64.FromInt(w.SizeX) || nz >= Fix64.FromInt(w.SizeZ)) return false;
        int cx = u.X[i].FloorToInt(), cz = u.Z[i].FloorToInt();
        int tx = nx.FloorToInt(), tz = nz.FloorToInt();
        int ny = u.Y[i];
        if (tx != cx || tz != cz)
        {
            if (tx != cx && tz != cz)
            {
                // Diagonal: both side cells must be enterable too, so units cannot squeeze between corners.
                if (!Walk.TryStep(w, cx, cz, u.Y[i], tx, cz, out int sy) || !Walk.TryStep(w, cx, cz, u.Y[i], cx, tz, out int sy2))
                    return false;
                if (Shut(w, u.Owner[i], tx, sy, cz) || Shut(w, u.Owner[i], cx, sy2, tz)) return false;
            }
            if (!Walk.TryStep(w, cx, cz, u.Y[i], tx, tz, out ny)) return false;
            if (Shut(w, u.Owner[i], tx, ny, tz)) return false;
            if (waypoint >= 0 && ny < u.Y[i] - 1 && tz * w.SizeX + tx != waypoint) return false;
        }
        u.X[i] = nx;
        u.Z[i] = nz;
        u.Y[i] = ny;
        return true;
    }

    /// <summary>True if standing at level y of column (x, z) puts a unit of the player in someone else's gate.</summary>
    private bool Shut(VoxelWorld w, int player, int x, int y, int z)
    {
        if (w.Get(x, y, z) != BlockType.Gate && w.Get(x, y + 1, z) != BlockType.Gate) return false;
        int owner = _sim.Buildings.GateOwner(x, z);
        return owner >= 0 && owner != player;
    }

    /// <summary>Falls if the ground was removed; climbs out if a block was placed into the unit.</summary>
    private static void Settle(VoxelWorld w, UnitStore u, int i)
    {
        int x = u.X[i].FloorToInt(), z = u.Z[i].FloorToInt(), y = u.Y[i];
        if (Walk.CanStand(w, x, y, z)) return;
        if (Walk.Passable(w.Get(x, y, z)) && Walk.Headroom(w.Get(x, y + 1, z)))
        {
            // Ground gone: fall to the next floor (one block per tick looks like falling).
            if (y > 1 && Walk.Passable(w.Get(x, y - 1, z))) u.Y[i] = y - 1;
            return;
        }
        // Something is in the unit's space: move up to the nearest free level above.
        int up = -1;
        for (int d = 1; d <= 4 && up < 0; d++)
            if (Walk.CanStand(w, x, y + d, z)) up = y + d;
        if (up >= 0) u.Y[i] = up;
    }
}
