using System;
using System.Collections.Generic;

namespace Rts.Sim.Units;

/// <summary>A wall of earth raised by <see cref="SpellSystem"/>: its blocks, and when it crumbles away.</summary>
public sealed class EarthWall
{
    /// <summary>Who raised it.</summary>
    public int Owner { get; internal set; }
    /// <summary>The tick it was raised.</summary>
    public long Raised { get; internal set; }
    /// <summary>The tick it crumbles back into the ground.</summary>
    public long Crumbles { get; internal set; }
    /// <summary>Its blocks, column by column along the line, each column bottom to top.</summary>
    public List<(int X, int Y, int Z)> Blocks { get; } = new();
}

/// <summary>
/// Terrain magic (design doc: "Shape with magic"): spells that change the ground, made with the same blocks
/// digging and building use. The first spell is <b>Earth Wall</b> (<see cref="EarthWallCommand"/>): a unit
/// whose type has it (<see cref="UnitDef.EarthWall"/>, the mage) raises a wall of earth along a line of up to
/// <see cref="MaxLength"/> columns, <see cref="Height"/> blocks high, so nobody walks through or over it.
/// The line's middle must be within <see cref="CastRange"/> blocks of the caster, which walks into range
/// first if it is not. A column is skipped where a building, a city-state's town, water or a unit stands.
/// The wall lasts <see cref="LastsTicks"/>, then crumbles back into the ground; until then workers can dig
/// through it and sappers tunnel it like any earth. A caster can raise another wall
/// <see cref="CooldownTicks"/> after its last. All numbers are starting guesses to be tuned in playtests.
/// </summary>
public sealed class SpellSystem
{
    /// <summary>Most columns one earth wall covers.</summary>
    public const int MaxLength = 8;
    /// <summary>Blocks high an earth wall stands from the ground: more than a unit climbs.</summary>
    public const int Height = 3;
    /// <summary>How far (blocks, across) from the caster the middle of the line may be.</summary>
    public const int CastRange = 12;
    /// <summary>Ticks a caster waits after raising a wall before it can raise another: 40 seconds.</summary>
    public const int CooldownTicks = 40 * Simulation.TicksPerSecond;
    /// <summary>Ticks an earth wall stands before it crumbles: 45 seconds.</summary>
    public const int LastsTicks = 45 * Simulation.TicksPerSecond;

    private readonly Simulation _sim;
    // Casters (unit id) and the tick each can cast again; sorted, so the hash is the same everywhere.
    private readonly SortedDictionary<int, long> _ready = new();
    // Casters walking into range of a line to raise a wall on it, in the order they were told.
    private readonly List<Pending> _pending = new();
    private readonly List<EarthWall> _walls = new();

    private sealed class Pending
    {
        public int Id;
        public int X1, Z1, X2, Z2;
        // Where it walks to: the line's middle.
        public int GoX, GoY, GoZ;
    }

    public SpellSystem(Simulation sim)
    {
        _sim = sim;
    }

    private UnitStore U => _sim.Units;
    private VoxelWorld World => _sim.World;

    /// <summary>Earth walls standing now, oldest first.</summary>
    public IReadOnlyList<EarthWall> Walls => _walls;

    /// <summary>The tick of the last earth wall raised, or -1 (for drawing it going up).</summary>
    public long LastCast { get; private set; } = -1;

    /// <summary>Unit i's type can raise earth walls.</summary>
    public bool HasEarthWall(int i) => i >= 0 && _sim.Content.Units[U.Def[i]].EarthWall;

    /// <summary>The tick unit i can next raise an earth wall (now or earlier if it is ready).</summary>
    public long ReadyAt(int i) => i >= 0 && _ready.TryGetValue(U.Id[i], out long t) ? t : 0;

    /// <summary>Unit i can raise an earth wall now: it has the spell, is not on its cooldown, and is not running or beaten.</summary>
    public bool CanCast(int i) =>
        HasEarthWall(i) && _sim.Tick >= ReadyAt(i) && U.Rout[i] == 0 && !U.Subdued[i] && U.Owner[i] != Simulation.WildPlayer;

    /// <summary>Unit (id) is walking into range to raise a wall.</summary>
    public bool Casting(int id)
    {
        foreach (var p in _pending)
            if (p.Id == id) return true;
        return false;
    }

    /// <summary>
    /// The columns an earth wall from (x1, z1) to (x2, z2) covers (the line a wall is laid along, at most
    /// <see cref="MaxLength"/>), each with its ground level and whether earth can rise there. The game previews
    /// the same plan.
    /// </summary>
    public void Plan(int x1, int z1, int x2, int z2, List<(int X, int Z, int Y, bool Room)> columns)
    {
        columns.Clear();
        foreach (var (x, z) in Economy.EconomySystem.WallLine(x1, z1, x2, z2))
        {
            if (columns.Count == MaxLength) break;
            int g = World.InBounds(x, 1, z) ? Walk.GroundLevel(World, x, z) : -1;
            columns.Add((x, z, g, Room(x, z, g)));
        }
    }

    private bool Room(int x, int z, int g)
    {
        var w = World;
        if (g < 1 || x < 1 || z < 1 || x > w.SizeX - 2 || z > w.SizeZ - 2 || g + Height >= w.SizeY) return false;
        if (w.Get(x, g - 1, z) == BlockType.Water || w.Get(x, g, z) == BlockType.Water) return false;
        if (_sim.Buildings.AtColumn(x, z) != null || _sim.Cities.Crowds(x, z, x, z)) return false;
        if (!Fits(w.Get(x, g, z))) return false;
        // Nobody standing in (or right at the edge of) the column, from the ground to the wall's top.
        var u = U;
        Fix64 lo = Fix64.FromInt(x) - Fix64.FromRatio(1, 4), hi = Fix64.FromInt(x + 1) + Fix64.FromRatio(1, 4);
        Fix64 loZ = Fix64.FromInt(z) - Fix64.FromRatio(1, 4), hiZ = Fix64.FromInt(z + 1) + Fix64.FromRatio(1, 4);
        for (int i = 0; i < u.Count; i++)
            if (u.X[i] > lo && u.X[i] < hi && u.Z[i] > loZ && u.Z[i] < hiZ && u.Y[i] >= g - 1 && u.Y[i] <= g + Height)
                return false;
        return true;
    }

    private static bool Fits(BlockType b) => b == BlockType.Air || BlockInfo.IsLeaves(b);

    /// <summary>
    /// The first of the player's units that can raise an earth wall now (one already in range first) raises
    /// it on the command's line, or walks into range and then raises it.
    /// </summary>
    internal void Apply(EarthWallCommand c)
    {
        int mx = (c.X1 + c.X2) / 2, mz = (c.Z1 + c.Z2) / 2;
        int far = -1;
        foreach (int i in _sim.OwnedUnits(c))
        {
            if (!CanCast(i) || Casting(U.Id[i])) continue;
            if (InRange(i, mx, mz))
            {
                U.Order[i] = UnitOrder.Idle;
                Cast(i, c.X1, c.Z1, c.X2, c.Z2);
                return;
            }
            if (far < 0) far = i;
        }
        if (far < 0 || !World.InBounds(mx, 1, mz)) return;
        int gy = Walk.GroundLevel(World, mx, mz);
        if (gy < 0) return;
        U.ClearJob(far);
        U.ClearCombat(far);
        U.ClearFormation(far);
        _pending.Add(new Pending { Id = U.Id[far], X1 = c.X1, Z1 = c.Z1, X2 = c.X2, Z2 = c.Z2, GoX = mx, GoY = gy, GoZ = mz });
        _sim.SendUnit(far, mx, mz, gy);
    }

    /// <summary>The player's units among the command's stop walking to raise a wall (they were given another order).</summary>
    internal void Release(UnitsCommand c)
    {
        if (_pending.Count == 0) return;
        foreach (int i in _sim.OwnedUnits(c))
            _pending.RemoveAll(p => p.Id == U.Id[i]);
    }

    private bool InRange(int i, int x, int z)
    {
        var u = U;
        Fix64 dx = u.X[i] - (Fix64.FromInt(x) + Fix64.Half), dz = u.Z[i] - (Fix64.FromInt(z) + Fix64.Half);
        Fix64 r = Fix64.FromInt(CastRange);
        return dx * dx + dz * dz <= r * r;
    }

    private void Cast(int i, int x1, int z1, int x2, int z2)
    {
        var w = World;
        var wall = new EarthWall { Owner = U.Owner[i], Raised = _sim.Tick, Crumbles = _sim.Tick + LastsTicks };
        var plan = new List<(int X, int Z, int Y, bool Room)>();
        Plan(x1, z1, x2, z2, plan);
        foreach (var (x, z, g, room) in plan)
        {
            if (!room) continue;
            for (int y = g; y < g + Height && Fits(w.Get(x, y, z)); y++)
            {
                w.Set(x, y, z, y == g + Height - 1 ? BlockType.Grass : BlockType.Soil);
                wall.Blocks.Add((x, y, z));
            }
        }
        _ready[U.Id[i]] = _sim.Tick + CooldownTicks;
        LastCast = _sim.Tick;
        if (wall.Blocks.Count > 0) _walls.Add(wall);
    }

    /// <summary>
    /// After the dusk tree: casters walking into range raise their wall once in range (one that was given
    /// another order, stopped short or can no longer cast gives up), and walls whose time is up crumble.
    /// </summary>
    public void Step()
    {
        var u = U;
        for (int k = 0; k < _pending.Count; k++)
        {
            var p = _pending[k];
            int i = u.IndexOf(p.Id);
            bool walking = i >= 0 && u.Order[i] == UnitOrder.Move && u.OrderX[i] == p.GoX && u.OrderZ[i] == p.GoZ;
            if (i < 0 || !CanCast(i) || u.Target[i] != 0 || u.Job[i] != UnitJob.None)
            {
                _pending.RemoveAt(k--);
                continue;
            }
            int mx = (p.X1 + p.X2) / 2, mz = (p.Z1 + p.Z2) / 2;
            if (InRange(i, mx, mz))
            {
                _pending.RemoveAt(k--);
                u.Order[i] = UnitOrder.Idle;
                Cast(i, p.X1, p.Z1, p.X2, p.Z2);
            }
            else if (!walking) _pending.RemoveAt(k--);
        }
        for (int k = 0; k < _walls.Count; k++)
        {
            if (_sim.Tick < _walls[k].Crumbles) continue;
            Crumble(_walls[k]);
            _walls.RemoveAt(k--);
        }
        if (_sim.Tick % (60 * Simulation.TicksPerSecond) == 0) Forget();
    }

    /// <summary>The wall's earth that is still there sinks back into the ground, top down.</summary>
    private void Crumble(EarthWall wall)
    {
        var w = World;
        for (int k = wall.Blocks.Count - 1; k >= 0; k--)
        {
            var (x, y, z) = wall.Blocks[k];
            if (w.Get(x, y, z) is BlockType.Soil or BlockType.Grass) w.Set(x, y, z, BlockType.Air);
        }
    }

    /// <summary>Cooldowns of casters that are gone.</summary>
    private void Forget()
    {
        List<int>? gone = null;
        foreach (var id in _ready.Keys)
            if (U.IndexOf(id) < 0) (gone ??= new()).Add(id);
        if (gone != null)
            foreach (int id in gone) _ready.Remove(id);
    }

    public ulong ComputeHash()
    {
        ulong h = 14695981039346656037UL;
        void Mix(long v) { h = (h ^ (ulong)v) * 1099511628211UL; }
        Mix(LastCast);
        Mix(_ready.Count);
        foreach (var (id, t) in _ready)
        {
            Mix(id); Mix(t);
        }
        Mix(_pending.Count);
        foreach (var p in _pending)
        {
            Mix(p.Id); Mix(p.X1); Mix(p.Z1); Mix(p.X2); Mix(p.Z2); Mix(p.GoX); Mix(p.GoY); Mix(p.GoZ);
        }
        Mix(_walls.Count);
        foreach (var wall in _walls)
        {
            Mix(wall.Owner); Mix(wall.Raised); Mix(wall.Crumbles); Mix(wall.Blocks.Count);
            foreach (var (x, y, z) in wall.Blocks)
            {
                Mix(x); Mix(y); Mix(z);
            }
        }
        return h;
    }
}
