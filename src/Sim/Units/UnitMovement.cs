using System;

namespace Rts.Sim.Units;

/// <summary>
/// Moves units each tick: following their order's flow field (or straight at the target until it is
/// ready), separation so crowds spread out, stepping across terrain (up one block, down up to three),
/// falling when the ground is dug away, and settling when a group reaches its destination. Units on a
/// road walk twice as fast.
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
            _waypoint[i] = -1;
            _keepingPlace[i] = false;
            if (u.Loose[i] > 0) u.Loose[i]--;
            if (u.Order[i] != UnitOrder.Idle) Steer(u, i, ref vx, ref vz);
            if (vx == Fix64.Zero && vz == Fix64.Zero) continue;
            int wp = _waypoint[i];
            bool moved = TryMove(w, u, i, vx, vz, wp) || TryMove(w, u, i, vx, Fix64.Zero, wp) || TryMove(w, u, i, Fix64.Zero, vz, wp);
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
        var speed = _sim.Economy.SpeedPerTick(u.Owner[i], _sim.Content.Units[u.Def[i]]);
        bool formation = u.Formation[i] && u.Order[i] == UnitOrder.Move;
        if (formation && u.SpeedCap[i] > Fix64.Zero) speed = Fix64.Min(speed, u.SpeedCap[i]);
        var underfoot = _sim.World.Get(u.X[i].FloorToInt(), u.Y[i] - 1, u.Z[i].FloorToInt());
        if (underfoot == BlockType.Road) speed *= 2;
        else if (BlockInfo.IsRubble(underfoot)) speed /= 2; // picking a way over a heap of rubble
        if (formation)
        {
            SteerFormation(u, i, speed, ref vx, ref vz);
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
                    EndOrder(u, i);
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
    /// its shape across open ground. Once that point is (nearly) at the middle, or the unit is close to its
    /// place, it walks straight to its place. Where keeping its place would walk it into a wall or off a
    /// ledge the middle's way avoids (that point is off the ground, on another level, or the unit got
    /// stuck), it follows the field from where it stands instead, until it is about as close to the middle
    /// as its place is.
    /// </summary>
    private void SteerFormation(UnitStore u, int i, Fix64 speed, ref Fix64 vx, ref Fix64 vz)
    {
        Fix64 sx = u.SlotX[i] - u.X[i], sz = u.SlotZ[i] - u.Z[i];
        Fix64 toSlot = Fix64.Sqrt(sx * sx + sz * sz);
        if (toSlot <= u.ArriveRadius[i] || (_settledNeighbour[i] && toSlot <= u.ArriveRadius[i] * 3 / 2))
        {
            EndOrder(u, i);
            return;
        }
        if (toSlot > FormationClose)
        {
            if (u.Field[i] != _cachedFieldId)
            {
                _cachedFieldId = u.Field[i];
                _cachedField = _sim.Paths.Get(_cachedFieldId);
            }
            if (_cachedField is { Ready: true } field)
            {
                var g = _sim.Walkable;
                Fix64 ox = u.SlotX[i] - Fix64.FromInt(u.OrderX[i]) - Fix64.Half;
                Fix64 oz = u.SlotZ[i] - Fix64.FromInt(u.OrderZ[i]) - Fix64.Half;
                // Keep its place: step the way the field leads from where the middle would be.
                if (u.Loose[i] == 0)
                {
                    Fix64 px = u.X[i] - ox, pz = u.Z[i] - oz;
                    int mx = px.FloorToInt(), mz = pz.FloorToInt();
                    int m = g.NearestNode(mx, mz, u.Y[i]);
                    if (m >= 0 && Math.Abs(g.Level(m) - u.Y[i]) <= 1)
                    {
                        int md = field.Distance(m);
                        if (md < FlowField.Unreachable)
                        {
                            if (md <= 10) goto straight; // the middle is there: take its place
                            int d = field.NextDirection(m, mx, mz, out _);
                            if (d >= 0)
                            {
                                Fix64 nx = Fix64.FromInt(mx + WalkGraph.Dirs[d].X) + Fix64.Half - px;
                                Fix64 nz = Fix64.FromInt(mz + WalkGraph.Dirs[d].Z) + Fix64.Half - pz;
                                Fix64 len = Fix64.Sqrt(nx * nx + nz * nz);
                                if (len > Fix64.Zero)
                                {
                                    vx += nx * speed / len;
                                    vz += nz * speed / len;
                                    _waypoint[i] = NoDrop;
                                    _keepingPlace[i] = true;
                                    return;
                                }
                            }
                        }
                    }
                }
                // Its own way to the middle, until it is about as near the middle as its place is.
                int x = u.X[i].FloorToInt(), z = u.Z[i].FloorToInt();
                int node = g.Node(x, z, u.Y[i]);
                int pathDist = node < 0 ? FlowField.Unknown : field.Distance(node);
                if (pathDist >= FlowField.Unreachable && node >= 0)
                {
                    if (pathDist == FlowField.Unreachable || (field.Complete && !field.Dirty))
                    {
                        EndOrder(u, i);
                        return;
                    }
                    field.WantsMore = true;
                }
                else if (pathDist < FlowField.Unreachable && Fix64.FromInt(pathDist) / 10 > Fix64.Sqrt(ox * ox + oz * oz) + Fix64.FromInt(2))
                {
                    int d = field.NextDirection(node, x, z, out int via);
                    if (via != pathDist && field.Dirty) field.Broken = true;
                    if (d >= 0)
                    {
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
        }
        straight:
        // Straight to its place: close to it, or no usable field here.
        Fix64 step = Fix64.Min(speed, toSlot);
        vx += sx * step / toSlot;
        vz += sz * step / toSlot;
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

    private static void EndOrder(UnitStore u, int i)
    {
        // The end of an attack-move's walk (arrived, or no way on) is the end of the attack-move.
        if (u.Order[i] == UnitOrder.Move && u.Combat[i] == CombatOrder.AttackMove) u.Combat[i] = CombatOrder.Auto;
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
