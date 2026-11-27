using System;
using System.Collections.Generic;

namespace Rts.Sim.Units;

/// <summary>
/// Boats (role <see cref="UnitRole.Boat"/>, the harbor concept art's ships): built at a dock on the shore, they sail on
/// open water and carry units across it.
///
/// <list type="bullet">
/// <item><b>Water</b>: a boat sails on a column whose top block is water (<see cref="Surface"/>), floating with its
/// keel on the water (its <see cref="UnitStore.Y"/> is the level over the water's top block). It goes from one such
/// column to the next only at the same water level, never cutting a corner past land, so a bridge, ice, a dam or a
/// sandbar stops it. It never sets foot on land, and <see cref="UnitMovement"/> leaves it alone: this system moves it.</item>
/// <item><b>Orders</b>: a move order (any kind) on water sails there along the shortest way by water
/// (<see cref="Sail"/>); on land it sails to the nearest water within <see cref="UnloadReach"/> columns from which
/// whoever is aboard can be put ashore, and puts them ashore there (<see cref="UnitJob.Land"/>). An
/// <see cref="UnloadCommand"/> does that where the boat is.</item>
/// <item><b>Boarding</b> (<see cref="BoardCommand"/>): each unit walks to the shore beside the boat and steps aboard
/// once it is within <see cref="BoardReach"/> of it (or on the spot beside it it walked to), while it has a seat free (<see cref="UnitDef.Seats"/>). Aboard
/// (<see cref="UnitStore.Aboard"/>) it rides where the boat goes, takes no orders and is not moved by anything else;
/// it can still be shot, and archers aboard shoot back from the deck.</item>
/// <item><b>Landing</b>: the boat puts them ashore on the standing ground within <see cref="LandReach"/> columns of
/// it, nearest first.</item>
/// <item><b>Sinking</b>: whoever is aboard a boat that is gone goes down with it.</item>
/// </list>
/// </summary>
public sealed class BoatSystem
{
    /// <summary>How far (blocks, across) from a boat a unit steps aboard it, and how many levels above or below it.</summary>
    public static readonly Fix64 BoardReach = Fix64.FromRatio(17, 10);
    public const int BoardRise = 3;
    /// <summary>Columns from a boat the ground its passengers are put ashore on may be.</summary>
    public const int LandReach = 2;
    /// <summary>Columns from where a landing is ordered that the water the boat lands them from may be.</summary>
    public const int UnloadReach = 8;
    /// <summary>Ticks between a boarding unit's checks that the boat is still where it was going to.</summary>
    public const int BoardRecheck = 20;
    /// <summary>Times a boarding unit tries again to walk to the boat before it gives up.</summary>
    public const int BoardTries = 4;
    /// <summary>Ticks a way over water is kept after the water changed before it is worked out again.</summary>
    public const int FieldRefresh = 20;

    private const short Unknown = -2, Dry = -1;
    private readonly Simulation _sim;
    // Each column's top block (its level, or Unknown until it is first asked about) and whether it is water.
    private readonly short[] _top;
    private readonly bool[] _wet;
    private int _version;
    // Ways over water, by goal: (kind, column). Looked up only, never iterated in a way that matters.
    private readonly Dictionary<long, Field> _fields = new();
    private int[] _load = Array.Empty<int>();
    private readonly PriorityQueue<int, int> _queue = new();
    private readonly List<int> _sources = new();

    private sealed class Field
    {
        public int[] Dist = Array.Empty<int>();
        public int Version;
        public long Built;
        public long Used;
    }

    public BoatSystem(Simulation sim)
    {
        _sim = sim;
        var w = sim.World;
        _top = new short[w.SizeX * w.SizeZ];
        _wet = new bool[w.SizeX * w.SizeZ];
        Array.Fill(_top, Unknown);
        w.BlockChanged += OnBlockChanged;
    }

    private UnitStore U => _sim.Units;
    private int SizeX => _sim.World.SizeX;

    /// <summary>The level of the water on top of column (x, z), or -1 if its top block is not water (or it is off the map).</summary>
    public int Surface(int x, int z)
    {
        var w = _sim.World;
        if ((uint)x >= (uint)w.SizeX || (uint)z >= (uint)w.SizeZ) return Dry;
        int c = z * w.SizeX + x;
        if (_top[c] == Unknown) Scan(x, z, c);
        return _wet[c] ? _top[c] : Dry;
    }

    /// <summary>True if a boat can float on column (x, z).</summary>
    public bool Navigable(int x, int z) => Surface(x, z) >= 0;

    // Finds column c's top block: its level, and whether it is water.
    private void Scan(int x, int z, int c)
    {
        var w = _sim.World;
        for (int y = w.SizeY - 1; y >= 0; y--)
        {
            var b = w.Get(x, y, z);
            if (b == BlockType.Air) continue;
            _top[c] = (short)y;
            _wet[c] = b == BlockType.Water;
            return;
        }
        _top[c] = Dry;
        _wet[c] = false;
    }

    private void OnBlockChanged(int x, int y, int z)
    {
        int c = z * SizeX + x;
        // A change under the top block changes nothing on top.
        if (_top[c] == Unknown || y < _top[c]) return;
        int old = _wet[c] ? _top[c] : Dry;
        Scan(x, z, c);
        if ((_wet[c] ? _top[c] : Dry) != old) _version++;
    }

    /// <summary>The level a boat on column (x, z) floats at (its feet's level), or -1 off the water.</summary>
    public int FloatLevel(int x, int z)
    {
        int s = Surface(x, z);
        return s >= 0 ? s + 1 : -1;
    }

    // --- Orders ------------------------------------------------------------------------------------

    /// <summary>
    /// A move order for boat i to column (x, z): on water, it sails there; on land, it sails to the water nearest
    /// it whoever is aboard can be put ashore from, and lands them (see <see cref="Land"/>).
    /// </summary>
    internal void Sail(int i, int x, int z)
    {
        var u = U;
        u.ClearJob(i);
        u.ClearCombat(i);
        u.ClearFormation(i);
        if (!_sim.World.InBounds(x, 0, z)) return;
        if (Navigable(x, z))
        {
            u.Order[i] = UnitOrder.Move;
            u.OrderX[i] = x;
            u.OrderZ[i] = z;
            u.Blocked[i] = 0;
            u.Field[i] = 0;
            return;
        }
        Land(i, x, z);
    }

    /// <summary>
    /// Boat i sails to the water within <see cref="UnloadReach"/> columns of column (x, z) nearest to it by water from
    /// which whoever is aboard can be put ashore, and puts them ashore. Nothing happens if there is no such water.
    /// </summary>
    internal void Land(int i, int x, int z)
    {
        var u = U;
        u.ClearJob(i);
        u.ClearCombat(i);
        u.ClearFormation(i);
        if (!_sim.World.InBounds(x, 0, z)) return;
        var field = LandField(x, z);
        if (field == null) { u.Order[i] = UnitOrder.Idle; return; }
        u.Job[i] = UnitJob.Land;
        u.JobX[i] = x;
        u.JobZ[i] = z;
        u.Order[i] = UnitOrder.Move;
        u.OrderX[i] = x;
        u.OrderZ[i] = z;
        u.Blocked[i] = 0;
        u.Field[i] = 0;
    }

    /// <summary>
    /// Boat i sails to water column (x, z) for its job (a fishing boat to its fishing ground or its dock), keeping the
    /// job; it stops where it gets to, or at once if there is no way there by water.
    /// </summary>
    internal void Steer(int i, int x, int z)
    {
        var u = U;
        u.Order[i] = UnitOrder.Move;
        u.OrderX[i] = x;
        u.OrderZ[i] = z;
        u.Blocked[i] = 0;
        u.Field[i] = 0;
    }

    /// <summary>
    /// How far every column is from water column (x, z) by water (10 a straight step, 14 a diagonal; -1 for none),
    /// indexed z * SizeX + x, or null if (x, z) is not water. Read it at once: it is shared and may be worked out again.
    /// </summary>
    public int[]? WaysTo(int x, int z) => SailField(x, z)?.Dist;

    internal void Apply(UnloadCommand c)
    {
        var u = U;
        foreach (int i in _sim.OwnedUnits(c))
        {
            if (!Def(i).IsBoat) continue;
            Land(i, u.X[i].FloorToInt(), u.Z[i].FloorToInt());
        }
    }

    internal void Apply(BoardCommand c)
    {
        var u = U;
        int b = u.IndexOf(c.Boat);
        if (b < 0 || u.Owner[b] != c.PlayerId || !Def(b).IsBoat || Def(b).Seats == 0) return;
        foreach (int i in _sim.OwnedUnits(c))
        {
            if (Def(i).IsBoat) continue;
            u.ClearJob(i);
            u.ClearCombat(i);
            u.ClearFormation(i);
            u.Order[i] = UnitOrder.Idle;
            u.Job[i] = UnitJob.Board;
            u.JobTarget[i] = c.Boat;
            u.JobY[i] = -1; // no spot chosen yet
        }
    }

    /// <summary>
    /// Where a boat the building (a dock) has built is launched: the water column within two columns of its footprint
    /// nearest its middle (then in a fixed order), at the level it floats at. False if there is none.
    /// </summary>
    public bool Launch(Economy.Building b, out int x, out int z, out int y)
    {
        x = z = y = -1;
        int mx2 = b.X * 2 + b.Def.SizeX, mz2 = b.Z * 2 + b.Def.SizeZ, best = int.MaxValue;
        for (int cz = b.Z - 2; cz <= b.MaxZ + 2; cz++)
        for (int cx = b.X - 2; cx <= b.MaxX + 2; cx++)
        {
            if (cx >= b.X && cx <= b.MaxX && cz >= b.Z && cz <= b.MaxZ) continue;
            int fy = FloatLevel(cx, cz);
            if (fy < 0 || Math.Abs(fy - b.Y) > Economy.EconomySystem.ShoreDrop + 1) continue;
            int dx = cx * 2 + 1 - mx2, dz = cz * 2 + 1 - mz2, d = dx * dx + dz * dz;
            if (d >= best) continue;
            (best, x, z, y) = (d, cx, cz, fy);
        }
        return x >= 0;
    }

    // --- Each tick -----------------------------------------------------------------------------------

    /// <summary>Boarding, sailing, landing, and passengers riding (or going down with their boat). Before the fighting.</summary>
    public void Step()
    {
        var u = U;
        if (u.Count == 0) return;
        if (_load.Length < u.Capacity) _load = new int[u.Capacity];
        Array.Clear(_load, 0, u.Count);
        bool any = false;
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Aboard[i] == 0) continue;
            int b = u.IndexOf(u.Aboard[i]);
            if (b < 0)
            {
                // Its boat is gone: down with it.
                u.Aboard[i] = 0;
                _sim.Combat.Crush(i, Math.Max(1, u.Hp[i]));
                continue;
            }
            _load[b]++;
        }
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Job[i] == UnitJob.Board) { StepBoarding(i); continue; }
            if (!Def(i).IsBoat) continue;
            any = true;
            Float(i);
            if (u.Order[i] != UnitOrder.Idle) StepSailing(i);
        }
        if (!any) return;
        // Passengers ride: each in its seat, along the boat from its bow.
        Array.Clear(_load, 0, u.Count);
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Aboard[i] == 0) continue;
            int b = u.IndexOf(u.Aboard[i]);
            if (b < 0) continue;
            int seat = _load[b]++;
            var (ox, oz) = SeatOffset(b, seat);
            u.X[i] = u.X[b] + ox;
            u.Z[i] = u.Z[b] + oz;
            u.Y[i] = u.Y[b];
        }
        if ((_sim.Tick & 255) == 0) Forget();
    }

    /// <summary>Where seat n of boat b is from its middle: two abreast, from the bow back, on the way the boat faces.</summary>
    public (Fix64 X, Fix64 Z) SeatOffset(int b, int seat)
    {
        var u = U;
        Fix64 fx = u.FaceX[b], fz = u.FaceZ[b];
        if (fx == Fix64.Zero && fz == Fix64.Zero) fx = Fix64.One;
        int rows = Math.Max(1, (Def(b).Seats + 1) / 2);
        int row = seat / 2;
        // Along: from 1.25 forward to 1.25 back; across: 3/8 to either side (clear of the masts).
        Fix64 along = rows == 1 ? Fix64.Zero : Fix64.FromRatio(5, 4) - Fix64.FromRatio(5, 2) * row / (rows - 1);
        Fix64 across = seat % 2 == 0 ? Fix64.FromRatio(3, 8) : -Fix64.FromRatio(3, 8);
        return (fx * along - fz * across, fz * along + fx * across);
    }

    /// <summary>How many units are aboard boat i (counted this tick).</summary>
    public int Load(int i)
    {
        var u = U;
        int n = 0, id = u.Id[i];
        for (int j = 0; j < u.Count; j++)
            if (u.Aboard[j] == id) n++;
        return n;
    }

    // A boat floats at the water's level; one left on dry ground (the water drained away) sits on it.
    private void Float(int i)
    {
        var u = U;
        int x = u.X[i].FloorToInt(), z = u.Z[i].FloorToInt();
        int y = FloatLevel(x, z);
        if (y < 0)
        {
            int top = _sim.World.SurfaceHeight(x, z);
            y = top >= 0 ? top + 1 : u.Y[i];
        }
        u.Y[i] = y;
    }

    private void StepSailing(int i)
    {
        var u = U;
        bool landing = u.Job[i] == UnitJob.Land;
        var field = landing ? LandField(u.JobX[i], u.JobZ[i])
            : u.Order[i] == UnitOrder.Chase ? ReachField(u.OrderX[i], u.OrderZ[i], ReachOf(Def(i)))
            : SailField(u.OrderX[i], u.OrderZ[i]);
        int x = u.X[i].FloorToInt(), z = u.Z[i].FloorToInt();
        int c = z * SizeX + x;
        int d = field == null || !Navigable(x, z) ? -1 : field.Dist[c];
        if (d < 0)
        {
            // No way there by water.
            Stop(i);
            return;
        }
        if (d == 0)
        {
            if (landing) PutAshore(i, u.JobX[i], u.JobZ[i]);
            Stop(i);
            return;
        }
        // The neighbouring column nearest the goal.
        int best = -1, bestD = d;
        for (int k = 0; k < WalkGraph.Dirs.Length; k++)
        {
            var (dx, dz) = WalkGraph.Dirs[k];
            int nx = x + dx, nz = z + dz;
            if (!Passes(x, z, nx, nz)) continue;
            int nd = field!.Dist[nz * SizeX + nx];
            if (nd < 0 || nd >= bestD) continue;
            best = k;
            bestD = nd;
        }
        if (best < 0) { Stop(i); return; }
        var (bx, bz) = WalkGraph.Dirs[best];
        Fix64 tx = Fix64.FromInt(x + bx) + Fix64.Half - u.X[i];
        Fix64 tz = Fix64.FromInt(z + bz) + Fix64.Half - u.Z[i];
        Fix64 len = Fix64.Sqrt(tx * tx + tz * tz);
        if (len == Fix64.Zero) return;
        var speed = _sim.Economy.SpeedPerTick(u.Owner[i], Def(i));
        Fix64 step = Fix64.Min(speed, len);
        u.X[i] += tx * step / len;
        u.Z[i] += tz * step / len;
        u.FaceX[i] = tx / len;
        u.FaceZ[i] = tz / len;
        u.Blocked[i] = 0;
    }

    // Whether a boat goes from column (x, z) to the one beside it at (nx, nz): both water at the same level, and on a
    // diagonal both columns between as well.
    private bool Passes(int x, int z, int nx, int nz)
    {
        int s = Surface(nx, nz);
        if (s < 0 || s != Surface(x, z)) return false;
        if (nx != x && nz != z) return Surface(nx, z) == s && Surface(x, nz) == s;
        return true;
    }

    private void Stop(int i)
    {
        var u = U;
        u.Order[i] = UnitOrder.Idle;
        // A fishing boat's job goes on: it sees for itself whether it got where it was going.
        if (u.Job[i] != UnitJob.Fish) u.ClearJob(i);
        u.Blocked[i] = 0;
    }

    // --- Ways over water -------------------------------------------------------------------------------

    private Field? SailField(int x, int z)
    {
        if (!Navigable(x, z)) return null;
        long key = (long)z * SizeX + x;
        return Get(key, () => { _sources.Clear(); _sources.Add(z * SizeX + x); });
    }

    /// <summary>
    /// How near (columns, from the middle of what it is after) a ship that fights sails to shoot: a block inside its
    /// range, so it is in reach when it stops (see <see cref="CombatSystem"/>).
    /// </summary>
    public static int ReachOf(UnitDef def) => Math.Max(1, def.Range.FloorToInt() - 1);

    /// <summary>
    /// Ship i, which fights, goes after something at column (x, z) (on land or water): it sails to the nearest water
    /// within <see cref="ReachOf"/> columns of it. The combat system calls this in place of a walk; with no such water
    /// it stops, and the combat system gives up the chase.
    /// </summary>
    internal void Chase(int i, int x, int z)
    {
        var u = U;
        u.Order[i] = UnitOrder.Chase;
        u.OrderX[i] = x;
        u.OrderZ[i] = z;
        u.Blocked[i] = 0;
        u.Field[i] = 0;
    }

    // The water within r columns of (x, z), as the sources of a way over the water.
    private Field? ReachField(int x, int z, int r)
    {
        long key = (2L << 40) | ((long)r << 32) | ((long)z * SizeX + x);
        if (_fields.TryGetValue(key, out var f) && Fresh(f)) { f.Used = _sim.Tick; return f; }
        FindReach(x, z, r);
        if (_sources.Count == 0) return null;
        return Get(key, () => FindReach(x, z, r));
    }

    private void FindReach(int x, int z, int r)
    {
        _sources.Clear();
        for (int dz = -r; dz <= r; dz++)
        for (int dx = -r; dx <= r; dx++)
            if (dx * dx + dz * dz <= r * r && Navigable(x + dx, z + dz)) _sources.Add((z + dz) * SizeX + x + dx);
    }

    private Field? LandField(int x, int z)
    {
        long key = (1L << 40) | ((long)z * SizeX + x);
        if (_fields.TryGetValue(key, out var f) && Fresh(f)) { f.Used = _sim.Tick; return f; }
        FindLandings(x, z);
        if (_sources.Count == 0) return null;
        return Get(key, () => FindLandings(x, z));
    }

    // The water within UnloadReach of (x, z) that passengers can be put ashore from, onto ground about as near (x, z)
    // as any: not on a spit or an islet on the way when the far bank was asked for.
    private void FindLandings(int x, int z)
    {
        _sources.Clear();
        int best = int.MaxValue;
        for (int pass = 0; pass < 2; pass++)
        {
            int limit = 0;
            if (pass == 1)
            {
                if (best == int.MaxValue) return;
                int r = 0;
                while ((r + 1) * (r + 1) <= best) r++;
                limit = (r + 2) * (r + 2);
            }
            for (int dz = -UnloadReach; dz <= UnloadReach; dz++)
            for (int dx = -UnloadReach; dx <= UnloadReach; dx++)
            {
                int cx = x + dx, cz = z + dz;
                if (!Navigable(cx, cz) || !Ashore(cx, cz, x, z, out int ax, out int az, out _)) continue;
                int d = (ax - x) * (ax - x) + (az - z) * (az - z);
                if (pass == 0) best = Math.Min(best, d);
                else if (d <= limit) _sources.Add(cz * SizeX + cx);
            }
        }
    }

    private bool Fresh(Field f) => f.Version == _version || _sim.Tick - f.Built < FieldRefresh;

    private Field Get(long key, Action sources)
    {
        if (_fields.TryGetValue(key, out var f) && Fresh(f))
        {
            f.Used = _sim.Tick;
            return f;
        }
        f ??= new Field();
        sources();
        Build(f);
        _fields[key] = f;
        return f;
    }

    // Shortest ways by water to the sources: 10 a straight step, 14 a diagonal one.
    private void Build(Field f)
    {
        var w = _sim.World;
        int n = w.SizeX * w.SizeZ;
        if (f.Dist.Length != n) f.Dist = new int[n];
        Array.Fill(f.Dist, -1);
        _queue.Clear();
        foreach (int s in _sources)
        {
            if (f.Dist[s] == 0) continue;
            f.Dist[s] = 0;
            _queue.Enqueue(s, 0);
        }
        while (_queue.TryDequeue(out int c, out int d))
        {
            if (d != f.Dist[c]) continue;
            int x = c % w.SizeX, z = c / w.SizeX;
            for (int k = 0; k < WalkGraph.Dirs.Length; k++)
            {
                var (dx, dz) = WalkGraph.Dirs[k];
                int nx = x + dx, nz = z + dz;
                if (!Passes(x, z, nx, nz)) continue;
                int nc = nz * w.SizeX + nx, nd = d + WalkGraph.DirCost[k];
                if (f.Dist[nc] >= 0 && f.Dist[nc] <= nd) continue;
                f.Dist[nc] = nd;
                _queue.Enqueue(nc, nd);
            }
        }
        f.Version = _version;
        f.Built = f.Used = _sim.Tick;
    }

    // Drops the ways nobody has used for a while (which are dropped does not change any result).
    private void Forget()
    {
        List<long>? old = null;
        foreach (var (key, f) in _fields)
            if (_sim.Tick - f.Used > 600) (old ??= new()).Add(key);
        if (old != null) foreach (long k in old) _fields.Remove(k);
    }

    // --- Ashore and aboard -------------------------------------------------------------------------------

    /// <summary>
    /// True if a unit aboard a boat on water column (x, z) can be put ashore beside it: the nearest standing spot
    /// within <see cref="LandReach"/> columns at about the water's level (fewest columns away, then in a fixed order).
    /// </summary>
    public bool Ashore(int x, int z, out int ax, out int az, out int ay) => Ashore(x, z, x, z, out ax, out az, out ay);

    /// <summary>
    /// The standing spot within <see cref="LandReach"/> columns of water column (x, z), at about the water's level,
    /// nearest to column (tx, tz), then nearest the water, then in a fixed order. False if there is none.
    /// </summary>
    public bool Ashore(int x, int z, int tx, int tz, out int ax, out int az, out int ay)
    {
        ax = az = ay = -1;
        int y = FloatLevel(x, z);
        if (y < 0) return false;
        long bestD = long.MaxValue;
        for (int dz = -LandReach; dz <= LandReach; dz++)
        for (int dx = -LandReach; dx <= LandReach; dx++)
        {
            if (dx == 0 && dz == 0) continue;
            int sx = x + dx, sz = z + dz;
            long d = ((long)(sx - tx) * (sx - tx) + (long)(sz - tz) * (sz - tz)) * 64 + dx * dx + dz * dz;
            if (d >= bestD) continue;
            int sy = Walk.NearestStandLevel(_sim.World, sx, sz, y, BoardRise);
            if (sy < 0) continue;
            (ax, az, ay, bestD) = (sx, sz, sy, d);
        }
        return ax >= 0;
    }

    // Puts whoever is aboard boat b ashore on the standing ground round it nearest column (tx, tz) (then nearest the
    // boat), a few to a column.
    private void PutAshore(int b, int tx, int tz)
    {
        var u = U;
        int id = u.Id[b], x = u.X[b].FloorToInt(), z = u.Z[b].FloorToInt(), y = u.Y[b];
        var spots = new List<(int D, int X, int Z, int Y)>();
        for (int dz = -LandReach; dz <= LandReach; dz++)
        for (int dx = -LandReach; dx <= LandReach; dx++)
        {
            if (dx == 0 && dz == 0) continue;
            int sy = Walk.NearestStandLevel(_sim.World, x + dx, z + dz, y, BoardRise);
            int sx = x + dx, sz = z + dz;
            if (sy >= 0) spots.Add((((sx - tx) * (sx - tx) + (sz - tz) * (sz - tz)) * 64 + dx * dx + dz * dz, sx, sz, sy));
        }
        if (spots.Count == 0) return;
        spots.Sort();
        int k = 0;
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Aboard[i] != id) continue;
            var s = spots[k++ % spots.Count];
            u.Aboard[i] = 0;
            u.X[i] = Fix64.FromInt(s.X) + Fix64.Half;
            u.Z[i] = Fix64.FromInt(s.Z) + Fix64.Half;
            u.Y[i] = s.Y;
            u.Order[i] = UnitOrder.Idle;
            u.Field[i] = 0;
            u.Blocked[i] = 0;
            u.ClearCombat(i);
        }
    }

    private void StepBoarding(int i)
    {
        var u = U;
        int b = u.IndexOf(u.JobTarget[i]);
        if (b < 0 || u.Owner[b] != u.Owner[i] || _load[b] >= Def(b).Seats)
        {
            GiveUp(i);
            return;
        }
        int bx = u.X[b].FloorToInt(), bz = u.Z[b].FloorToInt(), at = bz * SizeX + bx;
        // Aboard once beside it, or on the spot beside it it was sent to (a boat put in two columns off the shore).
        bool there = u.JobY[i] == at && u.X[i].FloorToInt() == u.JobX[i] && u.Z[i].FloorToInt() == u.JobZ[i];
        if ((there || Fix64.Max(Fix64.Abs(u.X[i] - u.X[b]), Fix64.Abs(u.Z[i] - u.Z[b])) <= BoardReach) && Math.Abs(u.Y[i] - u.Y[b]) <= BoardRise)
        {
            u.ClearJob(i);
            u.Order[i] = UnitOrder.Idle;
            u.Field[i] = 0;
            u.Blocked[i] = 0;
            u.Aboard[i] = u.Id[b];
            _load[b]++;
            return;
        }
        bool idle = u.Order[i] == UnitOrder.Idle;
        // On its way: it looks again now and then in case the boat moved.
        if (!idle && (u.JobY[i] == at || (_sim.Tick + u.Id[i]) % BoardRecheck != 0)) return;
        // Got where it was going (or could not) and still not aboard: try again, a few times.
        if (idle && u.JobY[i] >= 0 && ++u.JobRetry[i] > BoardTries) { GiveUp(i); return; }
        // A boat out on the water has nowhere to walk aboard from.
        if (!Ashore(bx, bz, out int ax, out int az, out int ay)) { GiveUp(i); return; }
        u.JobX[i] = ax;
        u.JobZ[i] = az;
        u.JobY[i] = at;
        _sim.SendUnit(i, ax, az, ay);
    }

    private void GiveUp(int i)
    {
        var u = U;
        u.ClearJob(i);
        if (u.Order[i] == UnitOrder.Move) u.Order[i] = UnitOrder.Idle;
    }

    private UnitDef Def(int i) => _sim.Content.Units[U.Def[i]];
}
