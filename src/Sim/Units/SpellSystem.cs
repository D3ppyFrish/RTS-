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
/// Water frozen by <see cref="SpellSystem"/>'s Ice Bridge: its blocks (each the top of a column of water, now
/// <see cref="BlockType.Ice"/>), and when it thaws.
/// </summary>
public sealed class IceBridge
{
    /// <summary>Who froze it.</summary>
    public int Owner { get; internal set; }
    /// <summary>The tick it was frozen.</summary>
    public long Frozen { get; internal set; }
    /// <summary>The tick it thaws back into water.</summary>
    public long Thaws { get; internal set; }
    /// <summary>Its blocks, in the order they froze.</summary>
    public List<(int X, int Y, int Z)> Blocks { get; } = new();
    /// <summary>For each block, whether it was water before it froze (it thaws back into water, else into air).</summary>
    public List<bool> Water { get; } = new();
}

/// <summary>
/// A chasm opened by <see cref="SpellSystem"/>'s Chasm: the ground it took out, and when it closes again.
/// </summary>
public sealed class Chasm
{
    /// <summary>Who opened it.</summary>
    public int Owner { get; internal set; }
    /// <summary>The tick it opened.</summary>
    public long Opened { get; internal set; }
    /// <summary>The tick the ground closes over it again.</summary>
    public long Closes { get; internal set; }
    /// <summary>The blocks it took out, column by column along the line, each column top down.</summary>
    public List<(int X, int Y, int Z)> Blocks { get; } = new();
    /// <summary>For each block, what it was: the ground closes back as it was.</summary>
    public List<BlockType> Was { get; } = new();
}

/// <summary>A terrain spell (see <see cref="SpellSystem"/>).</summary>
public enum Spell
{
    EarthWall,
    IceBridge,
    Chasm,
}

/// <summary>
/// Terrain magic (design doc: "Shape with magic"): spells that change the ground, made with the same blocks
/// digging and building use. The first spell is <b>Earth Wall</b> (<see cref="EarthWallCommand"/>): a unit
/// whose type has it (<see cref="UnitDef.EarthWall"/>, the mage) raises a wall of earth along a line of up to
/// <see cref="MaxLength"/> columns, <see cref="Height"/> blocks high, so nobody walks through or over it.
/// The line's middle must be within <see cref="CastRange"/> blocks of the caster, which walks into range
/// first if it is not. A column is skipped where a building, a city-state's town, water or a unit stands.
/// The wall lasts <see cref="LastsTicks"/>, then crumbles back into the ground; until then workers can dig
/// through it and sappers tunnel it like any earth.
/// <para>The second is <b>Ice Bridge</b> (<see cref="IceBridgeCommand"/>, <see cref="UnitDef.IceBridge"/>):
/// the caster freezes the water into a level deck of ice from bank to bank, like a bridge, up to
/// <see cref="IceLength"/> columns long and <see cref="IceWidth"/> wide, that anyone can walk on. It melts after
/// <see cref="IceLastsTicks"/>; a unit still on it then falls, and drowns where only deep water is under it.</para>
/// <para>The third is <b>Chasm</b> (<see cref="ChasmCommand"/>, <see cref="UnitDef.Chasm"/>): the ground along
/// a line as long and thick as an earth wall's opens <see cref="ChasmDepth"/> blocks deep, deeper than a unit
/// climbs out of. Units standing on it fall in and take <see cref="FallDamage"/>, as from falling ground; they
/// are stuck in it until it closes after <see cref="ChasmLastsTicks"/>, when the ground comes back as it was
/// and lifts them out. Not under a building or a city-state's town, nor where water or ice is.</para>
/// A caster that cast any spell waits <see cref="CooldownTicks"/> before it casts again.
/// <para><b>Group spells</b>: every ready caster among the command's units (up to <see cref="MaxGroup"/>) casts
/// together. The ones out of range walk up while the others wait, and the spell goes off once all of them are in
/// range. Each helper beyond the first makes an earth wall one column thicker and <see cref="WallPerHelper"/>
/// columns longer (<see cref="WallLength"/>, <see cref="WallThickness"/>), and an ice bridge
/// <see cref="IceWidthPerHelper"/> columns wider (<see cref="IceWidthFor"/>). Every caster in the group then
/// waits. All numbers are starting guesses to be tuned in playtests.</para>
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
    /// <summary>Most columns along its line that one ice bridge covers: enough to span the river and its banks.</summary>
    public const int IceLength = 16;
    /// <summary>Columns across an ice bridge: its line and one on each side, so an army crosses three abreast.</summary>
    public const int IceWidth = 3;
    /// <summary>Ticks an ice bridge holds before it thaws: 30 seconds.</summary>
    public const int IceLastsTicks = 30 * Simulation.TicksPerSecond;
    /// <summary>Ticks before it thaws that an ice bridge shows cracks: the last 5 seconds.</summary>
    public const int CrackTicks = 5 * Simulation.TicksPerSecond;
    /// <summary>Blocks deep a chasm opens: more than a unit climbs, so whoever falls in is stuck.</summary>
    public const int ChasmDepth = 3;
    /// <summary>Damage to a unit that falls into a chasm as it opens.</summary>
    public const int FallDamage = 15;
    /// <summary>Ticks a chasm stays open before the ground closes over it: 30 seconds.</summary>
    public const int ChasmLastsTicks = 30 * Simulation.TicksPerSecond;

    /// <summary>Most casters that join one group spell.</summary>
    public const int MaxGroup = 3;
    /// <summary>Columns each caster beyond the first adds to an earth wall's length.</summary>
    public const int WallPerHelper = 4;
    /// <summary>Columns each caster beyond the first adds to an ice bridge's width (one on each side).</summary>
    public const int IceWidthPerHelper = 2;

    /// <summary>Casters that count for a group spell of <paramref name="casters"/>: 1 to <see cref="MaxGroup"/>.</summary>
    public static int GroupSize(int casters) => Math.Clamp(casters, 1, MaxGroup);

    /// <summary>Most columns along its line an earth wall raised by <paramref name="casters"/> together covers.</summary>
    public static int WallLength(int casters) => MaxLength + WallPerHelper * (GroupSize(casters) - 1);

    /// <summary>Columns thick an earth wall raised by <paramref name="casters"/> together is: one per caster.</summary>
    public static int WallThickness(int casters) => GroupSize(casters);

    /// <summary>Columns across an ice bridge frozen by <paramref name="casters"/> together.</summary>
    public static int IceWidthFor(int casters) => IceWidth + IceWidthPerHelper * (GroupSize(casters) - 1);

    private readonly Simulation _sim;
    // Casters (unit id) and the tick each can cast again; sorted, so the hash is the same everywhere.
    private readonly SortedDictionary<int, long> _ready = new();
    // Casters walking into range of a line to raise a wall on it, in the order they were told.
    private readonly List<Pending> _pending = new();
    private readonly List<EarthWall> _walls = new();
    private readonly List<IceBridge> _ice = new();
    private readonly List<Chasm> _chasms = new();

    private sealed class Pending
    {
        public int Id;
        // The group it casts with: the id of the group's first caster.
        public int Group;
        // In range, waiting for the rest of its group.
        public bool Waiting;
        public Spell Spell;
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

    /// <summary>Ice bridges holding now, oldest first.</summary>
    public IReadOnlyList<IceBridge> IceBridges => _ice;

    /// <summary>Chasms open now, oldest first.</summary>
    public IReadOnlyList<Chasm> Chasms => _chasms;

    /// <summary>The tick of the last chasm opened, or -1.</summary>
    public long LastChasm { get; private set; } = -1;

    /// <summary>The tick of the last earth wall raised, or -1 (for drawing it going up).</summary>
    public long LastCast { get; private set; } = -1;

    /// <summary>The tick of the last ice bridge frozen, or -1.</summary>
    public long LastFreeze { get; private set; } = -1;

    /// <summary>Unit i's type can raise earth walls.</summary>
    public bool HasEarthWall(int i) => i >= 0 && _sim.Content.Units[U.Def[i]].EarthWall;

    /// <summary>Unit i's type can freeze ice bridges.</summary>
    public bool HasIceBridge(int i) => i >= 0 && _sim.Content.Units[U.Def[i]].IceBridge;

    /// <summary>Unit i's type can open chasms.</summary>
    public bool HasChasm(int i) => i >= 0 && _sim.Content.Units[U.Def[i]].Chasm;

    /// <summary>Unit i's type has the spell.</summary>
    public bool Has(int i, Spell spell) => spell switch
    {
        Spell.IceBridge => HasIceBridge(i),
        Spell.Chasm => HasChasm(i),
        _ => HasEarthWall(i),
    };

    /// <summary>The tick unit i can next cast a spell (now or earlier if it is ready).</summary>
    public long ReadyAt(int i) => i >= 0 && _ready.TryGetValue(U.Id[i], out long t) ? t : 0;

    /// <summary>Unit i can raise an earth wall now: it has the spell, is not on its cooldown, and is not running or beaten.</summary>
    public bool CanCast(int i) => CanCast(i, Spell.EarthWall);

    /// <summary>Unit i can cast the spell now: it has it, is not waiting after its last spell, and is not running or beaten.</summary>
    public bool CanCast(int i, Spell spell) =>
        Has(i, spell) && _sim.Tick >= ReadyAt(i) && U.Rout[i] == 0 && !U.Subdued[i] && U.Owner[i] != Simulation.WildPlayer;

    /// <summary>The ice bridge is in its last <see cref="CrackTicks"/>: it shows cracks before it thaws.</summary>
    public bool Cracking(IceBridge ice) => _sim.Tick >= ice.Thaws - CrackTicks;

    /// <summary>Unit (id) is walking into range to raise a wall.</summary>
    public bool Casting(int id)
    {
        foreach (var p in _pending)
            if (p.Id == id) return true;
        return false;
    }

    /// <summary>
    /// The columns an earth wall from (x1, z1) to (x2, z2) covers, raised by <paramref name="casters"/> together:
    /// the line a wall is laid along, at most <see cref="WallLength"/> columns, and as many columns thick as
    /// <see cref="WallThickness"/> (the line, then one on each side in turn, across the line's longer axis). Each
    /// column has its ground level and whether earth can rise there. The game previews the same plan.
    /// </summary>
    public void Plan(int x1, int z1, int x2, int z2, List<(int X, int Z, int Y, bool Room)> columns, int casters = 1) =>
        Line(x1, z1, x2, z2, columns, casters, Room);

    /// <summary>
    /// The columns a chasm from (x1, z1) to (x2, z2) opens, by <paramref name="casters"/> together: the same
    /// columns as an earth wall (<see cref="Plan"/>). Each has its ground level and whether the ground there
    /// opens: natural ground (not a building's, not a tree's trunk) down to <see cref="ChasmDepth"/> under it,
    /// with no water or ice on or beside it, no building or city-state's town in the column. Units may stand on
    /// it: they fall in. The game previews the same plan.
    /// </summary>
    public void PlanChasm(int x1, int z1, int x2, int z2, List<(int X, int Z, int Y, bool Room)> columns, int casters = 1) =>
        Line(x1, z1, x2, z2, columns, casters, ChasmRoom);

    private void Line(int x1, int z1, int x2, int z2, List<(int X, int Z, int Y, bool Room)> columns, int casters,
        Func<int, int, int, bool> room)
    {
        columns.Clear();
        int length = WallLength(casters), thick = WallThickness(casters), n = 0;
        bool alongX = Math.Abs(x2 - x1) >= Math.Abs(z2 - z1);
        var seen = new HashSet<long>();
        foreach (var (lx, lz) in Economy.EconomySystem.WallLine(x1, z1, x2, z2))
        {
            if (n++ == length) break;
            for (int s = 0; s < thick; s++)
            {
                int side = Side(s);
                int x = alongX ? lx : lx + side, z = alongX ? lz + side : lz;
                if (!seen.Add(((long)x << 32) | (uint)z)) continue;
                int g = World.InBounds(x, 1, z) ? Walk.GroundLevel(World, x, z) : -1;
                columns.Add((x, z, g, room(x, z, g)));
            }
        }
    }

    /// <summary>The s-th row of a spell across its line: the line itself, then one side, then the other, and so on out.</summary>
    private static int Side(int s) => s == 0 ? 0 : s % 2 == 1 ? -(s + 1) / 2 : s / 2;

    private bool Room(int x, int z, int g)
    {
        var w = World;
        if (g < 1 || x < 1 || z < 1 || x > w.SizeX - 2 || z > w.SizeZ - 2 || g + Height >= w.SizeY) return false;
        if (w.Get(x, g - 1, z) is BlockType.Water or BlockType.Ice || w.Get(x, g, z) == BlockType.Water) return false;
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

    private bool ChasmRoom(int x, int z, int g)
    {
        var w = World;
        if (g - ChasmDepth < 1 || x < 1 || z < 1 || x > w.SizeX - 2 || z > w.SizeZ - 2) return false;
        if (_sim.Buildings.AtColumn(x, z) != null || _sim.Cities.Crowds(x, z, x, z)) return false;
        for (int y = g - ChasmDepth; y < g; y++)
            if (!Opens(w.Get(x, y, z))) return false;
        // No water or ice on the ground or around the pit: a chasm is not a channel from the river.
        for (int y = g - ChasmDepth; y <= g; y++)
        {
            if (w.Get(x, y, z) is BlockType.Water or BlockType.Ice) return false;
            foreach (var (dx, dz) in Around)
                if (w.Get(x + dx, y, z + dz) is BlockType.Water or BlockType.Ice) return false;
        }
        return true;
    }

    private static readonly (int X, int Z)[] Around = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    /// <summary>Natural ground a chasm opens through: earth, sand, stone, ore and rubble, but no trunk or building.</summary>
    private static bool Opens(BlockType b) => BlockInfo.CanFall(b) && !BlockInfo.IsBuilt(b);

    /// <summary>
    /// Plans an ice bridge from column (x1, z1) to (x2, z2), frozen by <paramref name="casters"/> together, into
    /// <paramref name="columns"/>, as the bridge tool plans a deck: both ends must be ground to stand on, at most
    /// a climb apart, and the ice runs level at the higher end's standing level, one block under it. It covers the
    /// line (at most <see cref="IceLength"/> columns that need ice) and columns on each side (across the line's
    /// longer axis), <see cref="IceWidthFor"/> in all: a column gets ice
    /// where its ground is lower than the deck. Each column has the level units would stand at on the ice and
    /// whether ice forms there (open air or water where the ice goes and above it, no building or city-state's
    /// town). Returns that level, or -1 (and no columns) if the ends are not ground, too far apart in height, or
    /// no column of the line has water under it: the ice is frozen out of water. The game previews the same plan.
    /// </summary>
    public int PlanIce(int x1, int z1, int x2, int z2, List<(int X, int Z, int Y, bool Room)> columns, int casters = 1)
    {
        columns.Clear();
        var w = World;
        if (!w.InBounds(x1, 1, z1) || !w.InBounds(x2, 1, z2)) return -1;
        int a = Walk.GroundLevel(w, x1, z1), b = Walk.GroundLevel(w, x2, z2);
        if (a < 0 || b < 0 || Math.Abs(a - b) > Walk.MaxClimb) return -1;
        int stand = Math.Max(a, b), deckY = stand - 1;
        if (stand + Walk.Clearance > w.SizeY - 1) return -1;
        bool alongX = Math.Abs(x2 - x1) >= Math.Abs(z2 - z1);
        var seen = new HashSet<long>();
        bool water = false;
        int n = 0;
        foreach (var (lx, lz) in Economy.EconomySystem.WallLine(x1, z1, x2, z2))
        {
            if (!Needs(lx, lz, stand)) continue;
            if (n++ == IceLength) break;
            if (OverWater(lx, lz, deckY)) water = true;
            for (int s = 0, width = IceWidthFor(casters); s < width; s++)
            {
                int side = Side(s);
                int x = alongX ? lx : lx + side, z = alongX ? lz + side : lz;
                if (!seen.Add(((long)x << 32) | (uint)z) || !Needs(x, z, stand)) continue;
                columns.Add((x, z, stand, IceRoom(x, z, deckY)));
            }
        }
        if (!water)
        {
            columns.Clear();
            return -1;
        }
        return stand;
    }

    /// <summary>Column (x, z) needs ice to walk across at <paramref name="stand"/>: its ground is lower.</summary>
    private bool Needs(int x, int z, int stand) => World.InBounds(x, 1, z) && Walk.GroundLevel(World, x, z) < stand;

    /// <summary>Column (x, z) has water at or below <paramref name="deckY"/> with open air or water above it.</summary>
    private bool OverWater(int x, int z, int deckY)
    {
        var w = World;
        for (int y = deckY; y >= 1; y--)
        {
            var b = w.Get(x, y, z);
            if (b == BlockType.Water) return true;
            if (b != BlockType.Air) return false;
        }
        return false;
    }

    private bool IceRoom(int x, int z, int deckY)
    {
        var w = World;
        if (x < 1 || z < 1 || x > w.SizeX - 2 || z > w.SizeZ - 2) return false;
        if (_sim.Buildings.AtColumn(x, z) != null || _sim.Cities.Crowds(x, z, x, z)) return false;
        if (w.Get(x, deckY, z) is not (BlockType.Air or BlockType.Water) && !BlockInfo.IsLeaves(w.Get(x, deckY, z))) return false;
        for (int y = deckY + 1; y <= deckY + Walk.Clearance; y++)
            if (w.Get(x, y, z) is var r && r != BlockType.Air && !BlockInfo.IsLeaves(r)) return false;
        return true;
    }

    /// <summary>
    /// The player's units that can raise an earth wall now (up to <see cref="MaxGroup"/>, ones already in range
    /// first) raise it together on the command's line, walking into range first where they are not.
    /// </summary>
    internal void Apply(EarthWallCommand c) => Order(c, Spell.EarthWall, c.X1, c.Z1, c.X2, c.Z2);

    /// <summary>
    /// The player's units that can freeze an ice bridge now (up to <see cref="MaxGroup"/>, ones already in range
    /// first) freeze the water along the command's line together, walking into range first where they are not.
    /// </summary>
    internal void Apply(IceBridgeCommand c) => Order(c, Spell.IceBridge, c.X1, c.Z1, c.X2, c.Z2);

    /// <summary>
    /// The player's units that can open a chasm now (up to <see cref="MaxGroup"/>, ones already in range first)
    /// open it together along the command's line, walking into range first where they are not.
    /// </summary>
    internal void Apply(ChasmCommand c) => Order(c, Spell.Chasm, c.X1, c.Z1, c.X2, c.Z2);

    private void Order(UnitsCommand c, Spell spell, int x1, int z1, int x2, int z2)
    {
        int mx = (x1 + x2) / 2, mz = (z1 + z2) / 2;
        var near = new List<int>();
        var far = new List<int>();
        foreach (int i in _sim.OwnedUnits(c))
        {
            if (!CanCast(i, spell) || Casting(U.Id[i])) continue;
            (InRange(i, mx, mz) ? near : far).Add(i);
        }
        var group = new List<int>(near);
        group.AddRange(far);
        if (group.Count > MaxGroup) group.RemoveRange(MaxGroup, group.Count - MaxGroup);
        if (group.Count == 0) return;
        if (far.Count == 0 || group.Count == near.Count)
        {
            foreach (int i in group) U.Order[i] = UnitOrder.Idle;
            Cast(group, spell, x1, z1, x2, z2);
            return;
        }
        if (!World.InBounds(mx, 1, mz)) return;
        // Walk to the line's middle, or (over water) to the nearest ground beside it.
        int gy = Walk.GroundLevel(World, mx, mz), gx = mx, gz = mz;
        if ((gy < 0 || World.Get(mx, gy, mz) == BlockType.Water) && !Shore(mx, mz, out gx, out gy, out gz)) return;
        int lead = U.Id[group[0]];
        foreach (int i in group)
        {
            bool waiting = InRange(i, mx, mz);
            U.ClearJob(i);
            U.ClearCombat(i);
            U.ClearFormation(i);
            if (waiting) U.Order[i] = UnitOrder.Idle;
            _pending.Add(new Pending
            {
                Id = U.Id[i], Group = lead, Waiting = waiting, Spell = spell, X1 = x1, Z1 = z1, X2 = x2, Z2 = z2,
                GoX = gx, GoY = gy, GoZ = gz,
            });
            if (!waiting) _sim.SendUnit(i, gx, gz, gy);
        }
    }

    /// <summary>The nearest column to (x, z), within cast range, with ground to stand on out of the water.</summary>
    private bool Shore(int x, int z, out int sx, out int sy, out int sz)
    {
        var w = World;
        for (int r = 1; r < CastRange; r++)
        for (int dz = -r; dz <= r; dz++)
        for (int dx = -r; dx <= r; dx++)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r || !w.InBounds(x + dx, 1, z + dz)) continue;
            int g = Walk.GroundLevel(w, x + dx, z + dz);
            if (g < 1 || w.Get(x + dx, g, z + dz) == BlockType.Water) continue;
            (sx, sy, sz) = (x + dx, g, z + dz);
            return true;
        }
        (sx, sy, sz) = (x, -1, z);
        return false;
    }

    /// <summary>The player's units among the command's stop walking to (or waiting to) cast (they were given another order).</summary>
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

    /// <summary>The casters (unit indices, the first leading) cast the spell together; each then waits.</summary>
    private void Cast(List<int> casters, Spell spell, int x1, int z1, int x2, int z2)
    {
        int i = casters[0], n = casters.Count;
        // A plan that spans nothing (no ground at its ends, no water between) freezes nothing and costs nothing.
        if (spell == Spell.IceBridge && !Freeze(i, n, x1, z1, x2, z2)) return;
        if (spell == Spell.Chasm && !Open(i, n, x1, z1, x2, z2)) return;
        if (spell == Spell.EarthWall) Raise(i, n, x1, z1, x2, z2);
        foreach (int k in casters) _ready[U.Id[k]] = _sim.Tick + CooldownTicks;
    }

    private void Raise(int i, int casters, int x1, int z1, int x2, int z2)
    {
        var w = World;
        var wall = new EarthWall { Owner = U.Owner[i], Raised = _sim.Tick, Crumbles = _sim.Tick + LastsTicks };
        var plan = new List<(int X, int Z, int Y, bool Room)>();
        Plan(x1, z1, x2, z2, plan, casters);
        foreach (var (x, z, g, room) in plan)
        {
            if (!room) continue;
            for (int y = g; y < g + Height && Fits(w.Get(x, y, z)); y++)
            {
                w.Set(x, y, z, y == g + Height - 1 ? BlockType.Grass : BlockType.Soil);
                wall.Blocks.Add((x, y, z));
            }
        }
        LastCast = _sim.Tick;
        if (wall.Blocks.Count > 0) _walls.Add(wall);
    }

    private readonly List<(int X, int Z, int Y, bool Room)> _icePlan = new();

    /// <summary>
    /// The ground of each column of the plan with room opens <see cref="ChasmDepth"/> deep, top down. Units
    /// standing on it fall in and take <see cref="FallDamage"/>. False (nothing opens, nothing is spent) if no
    /// column has room.
    /// </summary>
    private bool Open(int i, int casters, int x1, int z1, int x2, int z2)
    {
        var w = World;
        PlanChasm(x1, z1, x2, z2, _icePlan, casters);
        var chasm = new Chasm { Owner = U.Owner[i], Opened = _sim.Tick, Closes = _sim.Tick + ChasmLastsTicks };
        var opened = new HashSet<long>();
        foreach (var (x, z, g, room) in _icePlan)
        {
            if (!room) continue;
            for (int y = g - 1; y >= g - ChasmDepth; y--)
            {
                chasm.Blocks.Add((x, y, z));
                chasm.Was.Add(w.Get(x, y, z));
                w.Set(x, y, z, BlockType.Air);
            }
            opened.Add(((long)z << 20) | (long)x);
        }
        if (chasm.Blocks.Count == 0) return false;
        LastChasm = _sim.Tick;
        _chasms.Add(chasm);
        // Whoever stood on the ground that opened falls in (they drop with the movement's falling, tick by tick).
        var u = U;
        for (int k = 0; k < u.Count; k++)
        {
            int x = u.X[k].FloorToInt(), z = u.Z[k].FloorToInt();
            if (opened.Contains(((long)z << 20) | (long)x) && !Walk.CanStand(w, x, u.Y[k], z)) _sim.Combat.Crush(k, FallDamage);
        }
        return true;
    }

    /// <summary>
    /// The ground closes over the chasm as it was: each block it took out that is open again (air, or water that
    /// ran in) comes back, bottom up; ground that fell in stays. Units in it are lifted out onto the ground.
    /// </summary>
    private void Close(Chasm chasm)
    {
        var w = World;
        var closed = new HashSet<long>();
        for (int k = chasm.Blocks.Count - 1; k >= 0; k--)
        {
            var (x, y, z) = chasm.Blocks[k];
            if (w.Get(x, y, z) is not (BlockType.Air or BlockType.Water)) continue;
            if (_sim.Buildings.At(x, y, z) != null) continue;
            w.Set(x, y, z, chasm.Was[k]);
            closed.Add(((long)z << 20) | (long)x);
        }
        if (closed.Count == 0) return;
        var u = U;
        for (int i = 0; i < u.Count; i++)
        {
            int x = u.X[i].FloorToInt(), z = u.Z[i].FloorToInt(), y = u.Y[i];
            if (!closed.Contains(((long)z << 20) | (long)x) || Walk.CanStand(w, x, y, z)) continue;
            for (int up = y + 1; up <= y + ChasmDepth + Walk.Clearance && up < w.SizeY - 1; up++)
                if (Walk.CanStand(w, x, up, z))
                {
                    u.Y[i] = up;
                    break;
                }
        }
    }

    /// <summary>Ice forms in each column of the plan with room, level with the deck; leaves in the way are pushed aside.</summary>
    private bool Freeze(int i, int casters, int x1, int z1, int x2, int z2)
    {
        var w = World;
        int stand = PlanIce(x1, z1, x2, z2, _icePlan, casters);
        if (stand < 0) return false;
        LastFreeze = _sim.Tick;
        var ice = new IceBridge { Owner = U.Owner[i], Frozen = _sim.Tick, Thaws = _sim.Tick + IceLastsTicks };
        foreach (var (x, z, _, room) in _icePlan)
        {
            if (!room) continue;
            for (int y = stand; y < stand + Walk.Clearance; y++)
                if (BlockInfo.IsLeaves(w.Get(x, y, z))) w.Set(x, y, z, BlockType.Air);
            bool wet = w.Get(x, stand - 1, z) == BlockType.Water;
            w.Set(x, stand - 1, z, BlockType.Ice);
            ice.Blocks.Add((x, stand - 1, z));
            ice.Water.Add(wet);
        }
        if (ice.Blocks.Count > 0) _ice.Add(ice);
        return true;
    }

    /// <summary>
    /// After the dusk tree: casters walking into range stop and wait there (one that was given another order,
    /// stopped short or can no longer cast drops out of its group), a group whose casters are all in range casts,
    /// and walls and ice whose time is up crumble and thaw.
    /// </summary>
    public void Step()
    {
        var u = U;
        for (int k = 0; k < _pending.Count; k++)
        {
            var p = _pending[k];
            int i = u.IndexOf(p.Id);
            if (i < 0 || !CanCast(i, p.Spell) || u.Target[i] != 0 || u.Job[i] != UnitJob.None)
            {
                _pending.RemoveAt(k--);
                continue;
            }
            if (p.Waiting) continue;
            bool walking = u.Order[i] == UnitOrder.Move && u.OrderX[i] == p.GoX && u.OrderZ[i] == p.GoZ;
            if (InRange(i, (p.X1 + p.X2) / 2, (p.Z1 + p.Z2) / 2))
            {
                p.Waiting = true;
                u.Order[i] = UnitOrder.Idle;
            }
            else if (!walking) _pending.RemoveAt(k--);
        }
        // Groups whose casters are all in range cast, in the order they were told.
        for (int k = 0; k < _pending.Count; k++)
        {
            int group = _pending[k].Group;
            bool first = true, ready = true;
            for (int j = 0; j < _pending.Count; j++)
            {
                if (_pending[j].Group != group) continue;
                if (j < k) first = false;
                if (!_pending[j].Waiting) ready = false;
            }
            if (!first || !ready) continue;
            var p = _pending[k];
            var casters = new List<int>();
            foreach (var q in _pending)
                if (q.Group == group) casters.Add(u.IndexOf(q.Id));
            _pending.RemoveAll(q => q.Group == group);
            Cast(casters, p.Spell, p.X1, p.Z1, p.X2, p.Z2);
            k = -1;
        }
        for (int k = 0; k < _walls.Count; k++)
        {
            if (_sim.Tick < _walls[k].Crumbles) continue;
            Crumble(_walls[k]);
            _walls.RemoveAt(k--);
        }
        for (int k = 0; k < _ice.Count; k++)
        {
            if (_sim.Tick < _ice[k].Thaws) continue;
            Thaw(_ice[k]);
            _ice.RemoveAt(k--);
        }
        for (int k = 0; k < _chasms.Count; k++)
        {
            if (_sim.Tick < _chasms[k].Closes) continue;
            Close(_chasms[k]);
            _chasms.RemoveAt(k--);
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

    /// <summary>
    /// The ice still there melts: back into water where it was frozen out of water, into air above it. A unit
    /// standing on it falls to whatever is under it; one with only deep water under it drowns (nobody gets the
    /// kill), one over shallow water wades on.
    /// </summary>
    private void Thaw(IceBridge ice)
    {
        var w = World;
        var thawed = new HashSet<long>();
        for (int k = 0; k < ice.Blocks.Count; k++)
        {
            var (x, y, z) = ice.Blocks[k];
            if (w.Get(x, y, z) != BlockType.Ice) continue;
            w.Set(x, y, z, ice.Water[k] ? BlockType.Water : BlockType.Air);
            if (ice.Water[k]) _sim.Physics.WakeWater(x, y, z);
            thawed.Add(((long)y << 40) | ((long)z << 20) | (long)x);
        }
        if (thawed.Count == 0) return;
        var u = U;
        for (int i = 0; i < u.Count; i++)
        {
            int x = u.X[i].FloorToInt(), z = u.Z[i].FloorToInt(), y = u.Y[i] - 1;
            if (y < 0 || !thawed.Contains(((long)y << 40) | ((long)z << 20) | (long)x)) continue;
            // It sinks through air and water to the first solid block under it.
            int feet = y;
            while (feet > 1 && Walk.Passable(w.Get(x, feet - 1, z))) feet--;
            if (Walk.CanStand(w, x, feet, z)) u.Y[i] = feet;
            else _sim.Combat.Crush(i, Math.Max(1, u.Hp[i]));
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
        Mix(LastFreeze);
        Mix(_pending.Count);
        foreach (var p in _pending)
        {
            Mix(p.Id); Mix(p.Group); Mix(p.Waiting ? 1 : 0); Mix((int)p.Spell); Mix(p.X1); Mix(p.Z1); Mix(p.X2); Mix(p.Z2); Mix(p.GoX); Mix(p.GoY); Mix(p.GoZ);
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
        Mix(LastChasm);
        Mix(_chasms.Count);
        foreach (var chasm in _chasms)
        {
            Mix(chasm.Owner); Mix(chasm.Opened); Mix(chasm.Closes); Mix(chasm.Blocks.Count);
            for (int k = 0; k < chasm.Blocks.Count; k++)
            {
                var (x, y, z) = chasm.Blocks[k];
                Mix(x); Mix(y); Mix(z); Mix((int)chasm.Was[k]);
            }
        }
        Mix(_ice.Count);
        foreach (var ice in _ice)
        {
            Mix(ice.Owner); Mix(ice.Frozen); Mix(ice.Thaws); Mix(ice.Blocks.Count);
            for (int k = 0; k < ice.Blocks.Count; k++)
            {
                var (x, y, z) = ice.Blocks[k];
                Mix(x); Mix(y); Mix(z); Mix(ice.Water[k] ? 1 : 0);
            }
        }
        return h;
    }
}
