using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.World;

/// <summary>A block of a falling section, where it was when the section started to fall.</summary>
public readonly record struct SectionBlock(int X, int Y, int Z, BlockType Block);

/// <summary>
/// A piece of ground or of a building that is falling, or flying apart, as one rigid piece. Its blocks are
/// out of the world while it moves. Positions and speeds are in 64ths of a block (see
/// <see cref="PhysicsSystem.SubSteps"/>), measured from where the blocks were when it started.
/// </summary>
public sealed class FallingSection
{
    public int Id { get; }
    /// <summary>The blocks, where they were when it started, in ascending key order.</summary>
    public IReadOnlyList<SectionBlock> Blocks => _blocks;
    /// <summary>0 for a piece that broke off whole, one more for each time it broke apart in a hard landing.</summary>
    public int Generation { get; }
    /// <summary>The tick it started.</summary>
    public long Started { get; }
    /// <summary>
    /// True for a piece of a wall or gate: its rubble does not heap, each block sliding off any step down
    /// (see <see cref="PhysicsSystem.Release"/>), so a gap in a wall is not filled as high as the wall was.
    /// </summary>
    public bool Spreads { get; internal set; }

    /// <summary>Whole blocks moved, along x, y (up) and z.</summary>
    public int OffX { get; internal set; }
    public int OffY { get; internal set; }
    public int OffZ { get; internal set; }
    // 64ths of a block moved past the whole blocks, (-64, 64) on each axis.
    internal int SubX, SubY, SubZ;
    /// <summary>Velocity in 64ths of a block per tick (y up).</summary>
    public int VelX { get; internal set; }
    public int VelY { get; internal set; }
    public int VelZ { get; internal set; }

    /// <summary>Where it is now, in 64ths of a block from where it started.</summary>
    public int PosX => OffX * PhysicsSystem.SubSteps + SubX;
    public int PosY => OffY * PhysicsSystem.SubSteps + SubY;
    public int PosZ => OffZ * PhysicsSystem.SubSteps + SubZ;
    /// <summary>Where it was at the start of the last tick (for drawing).</summary>
    public int PrevX { get; internal set; }
    public int PrevY { get; internal set; }
    public int PrevZ { get; internal set; }
    /// <summary>How far it has dropped, in 64ths of a block (0 if it has gone up).</summary>
    public int Fall => Math.Max(0, -PosY);

    internal readonly List<SectionBlock> _blocks;
    // Ids of the units it has hit, so each is hit once.
    internal readonly List<int> Hit = new();
    internal readonly int MinX, MaxX, MinZ, MaxZ;

    internal FallingSection(int id, long started, int generation, List<SectionBlock> blocks)
    {
        Id = id;
        Started = started;
        Generation = generation;
        _blocks = blocks;
        MinX = MinZ = int.MaxValue;
        MaxX = MaxZ = int.MinValue;
        foreach (var b in blocks)
        {
            MinX = Math.Min(MinX, b.X); MaxX = Math.Max(MaxX, b.X);
            MinZ = Math.Min(MinZ, b.Z); MaxZ = Math.Max(MaxZ, b.Z);
        }
    }
}

/// <summary>
/// A tree a worker felled, toppling over (see <see cref="PhysicsSystem.FellTree"/>): it has left the world
/// and lands as tree rubble <see cref="PhysicsSystem.TreeFallTicks"/> ticks after <see cref="Started"/>.
/// </summary>
public sealed class FallingTree
{
    public int Id { get; }
    /// <summary>The tick it was felled.</summary>
    public long Started { get; }
    /// <summary>The stump: the column it stood in and the level its trunk started at.</summary>
    public int X { get; }
    public int BaseY { get; }
    public int Z { get; }
    /// <summary>The way it falls: one of (1, 0), (-1, 0), (0, 1), (0, -1).</summary>
    public int DirX { get; }
    public int DirZ { get; }
    /// <summary>Blocks of its trunk; it lands as that many blocks of tree rubble.</summary>
    public int Logs { get; }
    /// <summary>The blocks it stood as, trunk and crown, where they were (for drawing it as it falls).</summary>
    public IReadOnlyList<SectionBlock> Blocks { get; }

    internal FallingTree(int id, long started, int x, int baseY, int z, int dirX, int dirZ, int logs, List<SectionBlock> blocks)
    {
        Id = id; Started = started; X = x; BaseY = baseY; Z = z; DirX = dirX; DirZ = dirZ; Logs = logs; Blocks = blocks;
    }
}

/// <summary>
/// Where a section came down this tick (for dust, and a splash if it came down in water). With
/// <see cref="Broke"/> it hit hard and broke into pieces that fly on.
/// </summary>
public readonly record struct Landing(int Section, int X, int Y, int Z, int Blocks, bool Splash, bool Broke);

/// <summary>
/// Ground that falls, buildings that come apart and water that flows, worked out exactly (integers only),
/// so every machine agrees.
///
/// <para><b>Collapses.</b> When a block is taken out, the ground around it is checked: a piece of loose
/// ground (see <see cref="BlockInfo.CanFall"/>) no longer joined, face to face, to bedrock, the bottom of
/// the world or a building breaks off and falls as one piece, speeding up as it goes. Only blocks next to a
/// change are checked, so the caves and overhangs the map was made with stand until someone digs at them.
/// A piece bigger than <see cref="MaxSection"/> blocks is held up by its own bulk. Trees and bushes growing
/// on a piece that breaks off go with it. Buildings hand their own falling blocks over with
/// <see cref="Release"/>.</para>
///
/// <para><b>Breaking apart.</b> A piece of <see cref="BreakBlocks"/> or more blocks that comes down at
/// <see cref="BreakSpeed"/> or faster breaks into fragments (by octant around its middle) that bounce up and
/// fly outwards, and land in turn; they can break again, up to <see cref="MaxGenerations"/> times. Pieces
/// move as rigid bodies along all three axes and stop against anything solid.</para>
///
/// <para><b>Rubble.</b> Where a piece settles each block drops down its column and slides off any step of
/// two or more, so it ends up as a heap, and turns to rubble that remembers what it was made of (see
/// <see cref="BlockInfo.RubbleOf"/>): workers clear it for that. Units walk over rubble at half speed. Units
/// a piece moves into, or settles on, are hurt (<see cref="CrushDamage"/>); a unit buried climbs out to the
/// nearest open ground, and one with nowhere to go dies.</para>
///
/// <para><b>Felled trees.</b> A tree a worker fells leaves the world and topples over, away from the
/// worker, for <see cref="TreeFallTicks"/> ticks; then it lands as a line of tree rubble along the ground,
/// one block for each block of its trunk (see <see cref="Economy.Harvest.LayFelled"/>).</para>
///
/// <para><b>Water.</b> Water next to an opening flows into it a block at a time, every
/// <see cref="WaterTicks"/> ticks: down if it can, otherwise sideways, never up. Water stands at one level
/// across the map, so a channel or a hole dug from a lake fills up to the lake's level and no higher.</para>
/// </summary>
public sealed class PhysicsSystem
{
    /// <summary>Most blocks in a piece that can break off; a bigger one is held up by its own bulk.</summary>
    public const int MaxSection = 1024;
    /// <summary>Fractions of a block that positions and speeds are measured in.</summary>
    public const int SubSteps = 64;
    /// <summary>How much faster a falling piece goes each tick, in 64ths of a block per tick (about 1 g).</summary>
    public const int Gravity = 3;
    /// <summary>The fastest a piece moves on any axis, in 64ths of a block per tick (a block a tick).</summary>
    public const int MaxSpeed = SubSteps;
    /// <summary>Least blocks in a piece that breaks apart in a hard landing.</summary>
    public const int BreakBlocks = 6;
    /// <summary>Least landing speed (64ths of a block per tick) that breaks a piece: a drop of about three blocks.</summary>
    public const int BreakSpeed = 32;
    /// <summary>Most times a piece breaks apart (its fragments' fragments, and so on).</summary>
    public const int MaxGenerations = 2;
    /// <summary>Most blocks a settling block slides off steps, one column at a time.</summary>
    public const int MaxSlides = 4;
    /// <summary>Damage to a unit a piece moves into or settles on.</summary>
    public const int CrushDamage = 30;
    /// <summary>Water flows a block every this many ticks.</summary>
    public const int WaterTicks = 5;
    /// <summary>Most water blocks that flow each time; the rest flow the next time.</summary>
    public const int MaxWaterPerRound = 512;
    /// <summary>Most taken-out blocks checked for collapses in a tick; the rest are checked the next tick.</summary>
    public const int MaxChecksPerTick = 4096;
    /// <summary>How far (columns) a unit buried in rubble looks for open ground.</summary>
    public const int EscapeRange = 3;
    /// <summary>Ticks a felled tree takes to topple over and land.</summary>
    public const int TreeFallTicks = 16;

    private readonly Simulation _sim;
    private readonly VoxelWorld _w;
    // Blocks that turned to air since they were last looked at (keys, see Key).
    private readonly List<long> _opened = new();
    private readonly List<long> _batch = new();
    // Water that may flow on its next round.
    private readonly SortedSet<long> _water = new();
    private readonly List<long> _flowing = new();
    private readonly List<FallingSection> _falling = new();
    private readonly List<FallingSection> _started = new();
    private int _nextSection = 1;
    private readonly List<FallingTree> _trees = new();
    private int _nextTree = 1;
    private readonly List<(int X, int Y, int Z)> _laid = new();

    // Scratch for the support search: membership only, never iterated.
    private readonly HashSet<long> _supported = new();
    private readonly HashSet<long> _seen = new();
    private readonly List<long> _found = new();
    private readonly Stack<long> _stack = new();
    private readonly List<(int X, int Y, int Z, BlockType Block)> _placed = new();

    /// <summary>The sections moving now, oldest first.</summary>
    public IReadOnlyList<FallingSection> Falling => _falling;

    /// <summary>The felled trees toppling over now, oldest first.</summary>
    public IReadOnlyList<FallingTree> FallingTrees => _trees;

    /// <summary>Where sections (and felled trees, with section 0) came down in the last tick.</summary>
    public List<Landing> Landings { get; } = new();

    /// <summary>Water blocks waiting to flow.</summary>
    public int ActiveWater => _water.Count;

    public PhysicsSystem(Simulation sim)
    {
        _sim = sim;
        _w = sim.World;
        _w.BlockChanged += OnBlockChanged;
    }

    private void OnBlockChanged(int x, int y, int z)
    {
        if (_w.Get(x, y, z) == BlockType.Air) _opened.Add(Key(x, y, z));
    }

    internal static long Key(int x, int y, int z) => ((long)y << 40) | ((long)z << 20) | (long)x;
    private static int KX(long k) => (int)(k & 0xFFFFF);
    private static int KZ(long k) => (int)((k >> 20) & 0xFFFFF);
    private static int KY(long k) => (int)(k >> 40);

    /// <summary>The six face neighbours, down first and up last.</summary>
    private static readonly (int X, int Y, int Z)[] Faces =
    {
        (0, -1, 0), (1, 0, 0), (-1, 0, 0), (0, 0, 1), (0, 0, -1), (0, 1, 0),
    };

    /// <summary>
    /// True if taking out block (hx, hy, hz) would leave every loose block around it still held up, so nothing
    /// breaks off and falls (the same test the collapse check makes, without changing the world).
    /// </summary>
    public static bool HoldsWithout(VoxelWorld w, int hx, int hy, int hz)
    {
        var seen = new HashSet<long>();
        var stack = new Stack<long>();
        for (int f = 0; f < Faces.Length; f++)
        {
            int sx = hx + Faces[f].X, sy = hy + Faces[f].Y, sz = hz + Faces[f].Z;
            if (!w.InBounds(sx, sy, sz) || !BlockInfo.CanFall(w.Get(sx, sy, sz))) continue;
            seen.Clear();
            stack.Clear();
            long start = Key(sx, sy, sz);
            seen.Add(start);
            stack.Push(start);
            bool held = false;
            while (stack.Count > 0 && !held)
            {
                long k = stack.Pop();
                int bx = KX(k), by = KY(k), bz = KZ(k);
                if (by == 0 || seen.Count > MaxSection) { held = true; break; }
                for (int g = Faces.Length - 1; g >= 0; g--)
                {
                    int nx = bx + Faces[g].X, ny = by + Faces[g].Y, nz = bz + Faces[g].Z;
                    if (!w.InBounds(nx, ny, nz) || (nx == hx && ny == hy && nz == hz)) continue;
                    var t = w.Get(nx, ny, nz);
                    if (t == BlockType.Bedrock || BlockInfo.IsBuilt(t)) { held = true; break; }
                    if (!BlockInfo.CanFall(t)) continue;
                    long n = Key(nx, ny, nz);
                    if (seen.Add(n)) stack.Push(n);
                }
            }
            if (!held) return false;
        }
        return true;
    }

    public void Step()
    {
        Landings.Clear();
        if (_opened.Count > 0) CheckOpened();
        for (int k = 0; k < _falling.Count; k++)
            if (StepSection(_falling[k])) _falling.RemoveAt(k--);
        for (int k = 0; k < _trees.Count; k++)
        {
            if (_sim.Tick - _trees[k].Started < TreeFallTicks) continue;
            LandTree(_trees[k]);
            _trees.RemoveAt(k--);
        }
        _falling.AddRange(_started); // fragments of pieces that broke this tick move from the next
        _started.Clear();
        if (_sim.Tick % WaterTicks == 0 && _water.Count > 0) Flow();
        FreeUnits();
    }

    // --- Felled trees -----------------------------------------------------------------------------------

    /// <summary>
    /// Fells the tree whose trunk spans levels [baseY, topY] of column (x, z), cut by a unit standing in
    /// column (fromX, fromZ): it leaves the world now (see <see cref="Harvest.FellTree"/>) and falls away from
    /// the unit, landing as tree rubble <see cref="TreeFallTicks"/> ticks later. Returns its trunk blocks.
    /// </summary>
    public int FellTree(int x, int baseY, int topY, int z, int fromX, int fromZ)
    {
        var taken = new List<SectionBlock>();
        int logs = Harvest.FellTree(_w, x, baseY, topY, z, taken);
        if (logs == 0) return 0;
        int dx = x - fromX, dz = z - fromZ, dirX, dirZ;
        if (dx != 0 && Math.Abs(dx) >= Math.Abs(dz)) (dirX, dirZ) = (Math.Sign(dx), 0);
        else if (dz != 0) (dirX, dirZ) = (0, Math.Sign(dz));
        else
        {
            // Cut from on top of the stump: a way of its own.
            uint h = DeterministicRandom.Hash(x, z, (ulong)_sim.Tick);
            (dirX, dirZ) = (h & 3) switch { 0 => (1, 0), 1 => (-1, 0), 2 => (0, 1), _ => (0, -1) };
        }
        _trees.Add(new FallingTree(_nextTree++, _sim.Tick, x, baseY, z, dirX, dirZ, logs, taken));
        return logs;
    }

    /// <summary>True if a felled tree is falling from the stump at (x, baseY, z).</summary>
    public bool TreeFallingFrom(int x, int baseY, int z)
    {
        foreach (var t in _trees)
            if (t.X == x && t.BaseY == baseY && t.Z == z) return true;
        return false;
    }

    /// <summary>The tree lands: its logs lie down as tree rubble, and dust goes up where it came down.</summary>
    private void LandTree(FallingTree t)
    {
        _laid.Clear();
        Harvest.LayFelled(_w, t.X, t.BaseY, t.Z, t.Logs, t.DirX, t.DirZ, _laid);
        if (_laid.Count == 0) return;
        long sx = 0, sy = 0, sz = 0;
        foreach (var (x, y, z) in _laid) { sx += x; sy += y; sz += z; }
        int n = _laid.Count;
        Landings.Add(new Landing(0, (int)(sx / n), (int)(sy / n), (int)(sz / n), n, false, false));
    }

    // --- Starting pieces --------------------------------------------------------------------------------

    /// <summary>
    /// The blocks at these places come away from the world and fall, one piece for each group joined face to
    /// face, each pushed away from (fromX, fromZ) at <paramref name="push"/> 64ths of a block per tick (straight
    /// down from there). Buildings use this for blocks that lose their support, are knocked out, or collapse.
    /// Places that hold nothing solid are skipped. With <paramref name="spread"/> the pieces' rubble spreads
    /// out when it settles (see <see cref="FallingSection.Spreads"/>).
    /// </summary>
    public void Release(IReadOnlyList<(int X, int Y, int Z)> cells, int fromX, int fromZ, int push, bool spread = false)
    {
        _seen.Clear();
        foreach (var c in cells)
            if (_w.InBounds(c.X, c.Y, c.Z) && BlockInfo.IsSolid(_w.Get(c.X, c.Y, c.Z))) _seen.Add(Key(c.X, c.Y, c.Z));
        var order = new List<long>(_seen);
        order.Sort();
        var taken = new HashSet<long>();
        foreach (long start in order)
        {
            if (!taken.Add(start)) continue;
            _found.Clear();
            _stack.Clear();
            _stack.Push(start);
            while (_stack.Count > 0)
            {
                long k = _stack.Pop();
                _found.Add(k);
                foreach (var f in Faces)
                {
                    long n = Key(KX(k) + f.X, KY(k) + f.Y, KZ(k) + f.Z);
                    if (_seen.Contains(n) && taken.Add(n)) _stack.Push(n);
                }
            }
            var s = Start(0);
            s.Spreads = spread;
            long cx = 0, cz = 0;
            foreach (var b in s._blocks) { cx += b.X; cz += b.Z; }
            (s.VelX, s.VelZ) = Away((int)(cx * 2 / s._blocks.Count) + 1 - fromX * 2 - 1, (int)(cz * 2 / s._blocks.Count) + 1 - fromZ * 2 - 1, push);
        }
    }

    /// <summary>Speed (64ths of a block per tick) the chunks of a crumbling building are pushed outwards at.</summary>
    public const int CrumbleSpeed = 10;

    /// <summary>
    /// What is left of a building that is destroyed comes apart: its blocks break into chunks (3 x 2 x 3
    /// blocks of the building's plan, split further where they are not joined) that are pushed outwards from
    /// its middle and fall, so the roof drops in, the walls tip out and it all comes down as heaps of rubble.
    /// A field's blocks under the ground turn to soil.
    /// </summary>
    public void Crumble(Building b)
    {
        var blocks = b.Def.Blocks;
        var chunks = new SortedDictionary<int, List<(int X, int Y, int Z)>>();
        for (int k = 0; k < b.Placed; k++)
        {
            var t = blocks[k];
            int x = b.X + t.X, y = b.Y + t.Y, z = b.Z + t.Z;
            if (_w.Get(x, y, z) != t.Block) continue;
            if (t.Y < 0)
            {
                _w.Set(x, y, z, BlockType.Soil);
                continue;
            }
            int chunk = ((t.Y / 2) * 64 + t.Z / 3) * 64 + t.X / 3;
            if (!chunks.TryGetValue(chunk, out var list)) chunks[chunk] = list = new List<(int, int, int)>();
            list.Add((x, y, z));
        }
        int cx = b.X + b.Def.SizeX / 2, cz = b.Z + b.Def.SizeZ / 2;
        foreach (var list in chunks.Values) Release(list, cx, cz, CrumbleSpeed, b.Def.IsWall);
    }

    /// <summary>A velocity of about <paramref name="speed"/> in the direction (dx, dz); none if that is zero.</summary>
    private static (int X, int Z) Away(int dx, int dz, int speed)
    {
        int ax = Math.Abs(dx), az = Math.Abs(dz);
        int m = Math.Max(ax, az);
        if (m == 0 || speed == 0) return (0, 0);
        return (Math.Sign(dx) * speed * ax / m, Math.Sign(dz) * speed * az / m);
    }

    /// <summary>The blocks in <see cref="_found"/> leave the world as a new piece, which moves from the next time pieces move.</summary>
    private FallingSection Start(int generation)
    {
        _found.Sort();
        var blocks = new List<SectionBlock>(_found.Count);
        foreach (long k in _found)
        {
            int x = KX(k), y = KY(k), z = KZ(k);
            blocks.Add(new SectionBlock(x, y, z, _w.Get(x, y, z)));
        }
        foreach (var b in blocks) _w.Set(b.X, b.Y, b.Z, BlockType.Air);
        var s = new FallingSection(_nextSection++, _sim.Tick, generation, blocks);
        _falling.Add(s);
        return s;
    }

    // --- Collapses --------------------------------------------------------------------------------------

    /// <summary>
    /// Looks around the blocks that opened up since the last tick: water next to them is set flowing, and
    /// loose ground next to them that nothing holds up any more breaks off.
    /// </summary>
    private void CheckOpened()
    {
        _batch.Clear();
        _opened.Sort();
        long last = -1;
        int taken = 0;
        foreach (long key in _opened)
        {
            taken++;
            if (key == last) continue;
            last = key;
            _batch.Add(key);
            if (_batch.Count == MaxChecksPerTick) break;
        }
        _opened.RemoveRange(0, taken);
        _supported.Clear();
        foreach (long key in _batch)
        {
            int x = KX(key), y = KY(key), z = KZ(key);
            if (BlockInfo.IsSolid(_w.Get(x, y, z))) continue; // filled again since
            for (int f = 0; f < Faces.Length; f++)
            {
                int nx = x + Faces[f].X, ny = y + Faces[f].Y, nz = z + Faces[f].Z;
                if (!_w.InBounds(nx, ny, nz)) continue;
                var t = _w.Get(nx, ny, nz);
                if (t == BlockType.Water)
                {
                    if (ny >= y) _water.Add(Key(nx, ny, nz)); // water never flows up into it
                    continue;
                }
                if (!BlockInfo.CanFall(t)) continue;
                long n = Key(nx, ny, nz);
                if (_supported.Contains(n)) continue;
                if (!Unsupported(nx, ny, nz)) continue;
                BreakOff();
            }
        }
    }

    /// <summary>
    /// True if the loose block at (x, y, z) is part of a piece that nothing holds up; the piece is left in
    /// <see cref="_found"/>. Otherwise everything looked at is remembered as held up for the rest of the tick.
    /// The search goes down first, so ground over solid rock finds its footing in a few steps.
    /// </summary>
    private bool Unsupported(int x, int y, int z)
    {
        _seen.Clear();
        _found.Clear();
        _stack.Clear();
        long start = Key(x, y, z);
        _seen.Add(start);
        _stack.Push(start);
        bool held = false;
        while (_stack.Count > 0 && !held)
        {
            long k = _stack.Pop();
            _found.Add(k);
            int bx = KX(k), by = KY(k), bz = KZ(k);
            if (by == 0 || _found.Count > MaxSection || _supported.Contains(k))
            {
                held = true;
                break;
            }
            // Pushed up first and down last, so down is looked at first.
            for (int f = Faces.Length - 1; f >= 0; f--)
            {
                int nx = bx + Faces[f].X, ny = by + Faces[f].Y, nz = bz + Faces[f].Z;
                if (!_w.InBounds(nx, ny, nz)) continue;
                var t = _w.Get(nx, ny, nz);
                if (t == BlockType.Bedrock || BlockInfo.IsBuilt(t))
                {
                    held = true;
                    break;
                }
                if (!BlockInfo.CanFall(t)) continue;
                long n = Key(nx, ny, nz);
                if (_seen.Add(n)) _stack.Push(n);
            }
        }
        if (!held) return true;
        foreach (long k in _found) _supported.Add(k);
        while (_stack.Count > 0) _supported.Add(_stack.Pop());
        return false;
    }

    /// <summary>The piece in <see cref="_found"/> breaks off, with any plants growing on it, and starts to fall.</summary>
    private void BreakOff()
    {
        foreach (long k in _found)
        {
            int x = KX(k), y = KY(k) + 1, z = KZ(k);
            if (y >= _w.SizeY) continue;
            var above = _w.Get(x, y, z);
            if (BlockInfo.IsTrunk(above))
            {
                var (baseY, topY) = Harvest.Span(_w, x, y, z);
                Harvest.FellTree(_w, x, baseY, topY, z);
            }
            else if (above == BlockType.BerryBush) _w.Set(x, y, z, BlockType.Air);
        }
        Start(0);
    }

    // --- Moving pieces ----------------------------------------------------------------------------------

    /// <summary>Moves a piece on a tick: gravity, then along x, z and y. True if it came down (settled or broke).</summary>
    private bool StepSection(FallingSection s)
    {
        (s.PrevX, s.PrevY, s.PrevZ) = (s.PosX, s.PosY, s.PosZ);
        s.VelY = Math.Max(s.VelY - Gravity, -MaxSpeed);
        int impact = -s.VelY;
        MoveX(s);
        MoveZ(s);
        bool down = MoveY(s);
        if (!down && s.VelY <= 0 && !Fits(s, 0, -1, 0)) down = true; // resting on something, between whole blocks
        if (!down) return false;
        Land(s, impact);
        return true;
    }

    private void MoveX(FallingSection s)
    {
        s.SubX += s.VelX;
        while (Math.Abs(s.SubX) >= SubSteps)
        {
            int d = Math.Sign(s.SubX);
            if (!Fits(s, d, 0, 0)) { s.SubX = 0; s.VelX = 0; return; }
            s.OffX += d;
            s.SubX -= d * SubSteps;
            CrushUnits(s);
        }
    }

    private void MoveZ(FallingSection s)
    {
        s.SubZ += s.VelZ;
        while (Math.Abs(s.SubZ) >= SubSteps)
        {
            int d = Math.Sign(s.SubZ);
            if (!Fits(s, 0, 0, d)) { s.SubZ = 0; s.VelZ = 0; return; }
            s.OffZ += d;
            s.SubZ -= d * SubSteps;
            CrushUnits(s);
        }
    }

    /// <summary>Moves the piece up or down; true if it hit something on the way down.</summary>
    private bool MoveY(FallingSection s)
    {
        s.SubY += s.VelY;
        while (Math.Abs(s.SubY) >= SubSteps)
        {
            int d = Math.Sign(s.SubY);
            if (!Fits(s, 0, d, 0))
            {
                s.SubY = 0;
                if (d < 0) return true;
                s.VelY = 0;
                return false;
            }
            s.OffY += d;
            s.SubY -= d * SubSteps;
            CrushUnits(s);
        }
        return false;
    }

    /// <summary>True if every block of the piece has room, moved (dx, dy, dz) from where it is.</summary>
    private bool Fits(FallingSection s, int dx, int dy, int dz)
    {
        foreach (var b in s._blocks)
        {
            int x = b.X + s.OffX + dx, y = b.Y + s.OffY + dy, z = b.Z + s.OffZ + dz;
            if (y < 0 || y >= _w.SizeY || x < 0 || z < 0 || x >= _w.SizeX || z >= _w.SizeZ) return false;
            if (BlockInfo.IsSolid(_w.Get(x, y, z))) return false;
        }
        return true;
    }

    /// <summary>Units whose body the piece has just moved into are hurt, once per piece.</summary>
    private void CrushUnits(FallingSection s)
    {
        var u = _sim.Units;
        int minX = s.MinX + s.OffX, maxX = s.MaxX + s.OffX, minZ = s.MinZ + s.OffZ, maxZ = s.MaxZ + s.OffZ;
        for (int i = 0; i < u.Count; i++)
        {
            int x = u.X[i].FloorToInt(), z = u.Z[i].FloorToInt(), y = u.Y[i];
            if (x < minX || x > maxX || z < minZ || z > maxZ || s.Hit.Contains(u.Id[i])) continue;
            foreach (var b in s._blocks)
            {
                int by = b.Y + s.OffY;
                if (b.X + s.OffX != x || b.Z + s.OffZ != z || by < y || by > y + 1) continue;
                s.Hit.Add(u.Id[i]);
                _sim.Combat.Crush(i, CrushDamage);
                break;
            }
        }
    }

    // --- Coming down ------------------------------------------------------------------------------------

    /// <summary>
    /// The piece comes down at <paramref name="impact"/> (64ths of a block per tick): a big enough piece
    /// coming down hard enough breaks into fragments that fly on; otherwise it settles as rubble.
    /// </summary>
    private void Land(FallingSection s, int impact)
    {
        if (s._blocks.Count >= BreakBlocks && impact >= BreakSpeed && s.Generation < MaxGenerations)
        {
            Break(s, impact);
            return;
        }
        Settle(s);
    }

    /// <summary>
    /// The piece breaks by octant around its middle (or in two along its longest side, if it all lies in
    /// one): each fragment bounces up and flies outwards, a little faster for a harder landing.
    /// </summary>
    private void Break(FallingSection s, int impact)
    {
        long sx = 0, sy = 0, sz = 0;
        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue;
        foreach (var b in s._blocks)
        {
            sx += b.X; sy += b.Y; sz += b.Z;
            minX = Math.Min(minX, b.X); maxX = Math.Max(maxX, b.X);
            minY = Math.Min(minY, b.Y); maxY = Math.Max(maxY, b.Y);
            minZ = Math.Min(minZ, b.Z); maxZ = Math.Max(maxZ, b.Z);
        }
        int n = s._blocks.Count;
        // Doubled coordinates, so the middle of an even span falls between blocks.
        int mx = (int)(sx * 2 / n), my = (int)(sy * 2 / n), mz = (int)(sz * 2 / n);
        var groups = new List<SectionBlock>[8];
        int used = 0;
        foreach (var b in s._blocks)
        {
            int g = (b.X * 2 + 1 > mx ? 1 : 0) | (b.Y * 2 + 1 > my ? 2 : 0) | (b.Z * 2 + 1 > mz ? 4 : 0);
            if (groups[g] == null) { groups[g] = new List<SectionBlock>(); used++; }
            groups[g].Add(b);
        }
        if (used < 2)
        {
            // All in one octant: halve it along its longest side.
            Array.Clear(groups);
            int spanX = maxX - minX, spanY = maxY - minY, spanZ = maxZ - minZ;
            foreach (var b in s._blocks)
            {
                bool high = spanX >= spanY && spanX >= spanZ ? b.X * 2 > minX + maxX
                    : spanZ >= spanY ? b.Z * 2 > minZ + maxZ : b.Y * 2 > minY + maxY;
                int g = high ? 1 : 0;
                (groups[g] ??= new List<SectionBlock>()).Add(b);
            }
        }
        int bx = 0, by = 0, bz = 0;
        foreach (var b in s._blocks) { bx += b.X + s.OffX; by += b.Y + s.OffY; bz += b.Z + s.OffZ; }
        Landings.Add(new Landing(s.Id, bx / n, by / n, bz / n, n, false, true));
        var rng = new DeterministicRandom(_sim.Seed ^ ((ulong)s.Id * 0x9E3779B97F4A7C15UL) ^ (ulong)_sim.Tick);
        int spread = Math.Max(4, impact / 3);
        foreach (var group in groups)
        {
            if (group == null) continue;
            var blocks = new List<SectionBlock>(group.Count);
            long gx = 0, gz = 0;
            foreach (var b in group)
            {
                blocks.Add(new SectionBlock(b.X + s.OffX, b.Y + s.OffY, b.Z + s.OffZ, b.Block));
                gx += b.X; gz += b.Z;
            }
            blocks.Sort((a, c) => Key(a.X, a.Y, a.Z).CompareTo(Key(c.X, c.Y, c.Z)));
            var f = new FallingSection(_nextSection++, _sim.Tick, s.Generation + 1, blocks) { Spreads = s.Spreads };
            var (vx, vz) = Away((int)(gx * 2 / group.Count) - mx, (int)(gz * 2 / group.Count) - mz, spread);
            f.VelX = Math.Clamp(vx + rng.Range(-spread / 3, spread / 3 + 1), -MaxSpeed, MaxSpeed);
            f.VelZ = Math.Clamp(vz + rng.Range(-spread / 3, spread / 3 + 1), -MaxSpeed, MaxSpeed);
            f.VelY = impact / 3 + rng.Range(0, impact / 6 + 1);
            f.Hit.AddRange(s.Hit);
            _started.Add(f);
        }
    }

    /// <summary>
    /// The piece settles as rubble, lowest blocks first: each drops straight down its column onto whatever is
    /// there and slides off any step of two or more (up to <see cref="MaxSlides"/> columns), so it ends up as
    /// a heap. A piece that spreads (see <see cref="FallingSection.Spreads"/>) slides off any step at all, the
    /// way it was going where it has the choice, so it ends up as flat as there is room for. Units it settles
    /// on are hurt.
    /// </summary>
    private void Settle(FallingSection s)
    {
        var order = new List<SectionBlock>(s._blocks);
        order.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : Key(a.X, 0, a.Z).CompareTo(Key(b.X, 0, b.Z)));
        bool splash = false;
        long sx = 0, sy = 0, sz = 0;
        _placed.Clear();
        foreach (var b in order)
        {
            int x = b.X + s.OffX, y = b.Y + s.OffY, z = b.Z + s.OffZ;
            while (y < _w.SizeY - 1 && BlockInfo.IsSolid(_w.Get(x, y, z))) y++; // something moved in
            y = Floor(x, y, z);
            int step = s.Spreads ? 1 : 2;
            for (int slide = 0; slide < MaxSlides; slide++)
            {
                int bestX = x, bestZ = z, bestY = y;
                bool bestOnward = false;
                for (int f = 1; f <= 4; f++)
                {
                    int nx = x + Faces[f].X, nz = z + Faces[f].Z;
                    if (!_w.InBounds(nx, y, nz) || BlockInfo.IsSolid(_w.Get(nx, y, nz))) continue;
                    int ny = Floor(nx, y, nz);
                    if (ny > y - step) continue;
                    bool onward = s.Spreads && Faces[f].X * s.VelX + Faces[f].Z * s.VelZ > 0;
                    if (ny < bestY || (ny == bestY && onward && !bestOnward)) (bestX, bestY, bestZ, bestOnward) = (nx, ny, nz, onward);
                }
                if (bestY == y) break;
                (x, y, z) = (bestX, bestY, bestZ);
            }
            if (_w.Get(x, y, z) == BlockType.Water) splash = true;
            var rubble = BlockInfo.RubbleOf(b.Block);
            _w.Set(x, y, z, rubble);
            _placed.Add((x, y, z, rubble));
            sx += x; sy += y; sz += z;
        }
        int n = order.Count;
        Landings.Add(new Landing(s.Id, (int)(sx / n), (int)(sy / n), (int)(sz / n), n, splash, false));
        CrushSettled(s);
    }

    /// <summary>The lowest open level a block dropped at level y of column (x, z) comes to rest at.</summary>
    private int Floor(int x, int y, int z)
    {
        while (y > 0 && !BlockInfo.IsSolid(_w.Get(x, y - 1, z))) y--;
        return y;
    }

    /// <summary>
    /// Units whose body a piece's rubble settled in are hurt (unless the piece already hit them). Not by a
    /// wall's rubble as it spreads over the ground (see <see cref="FallingSection.Spreads"/>): that slides
    /// round their feet, and they climb out of it (see <see cref="FreeUnits"/>).
    /// </summary>
    private void CrushSettled(FallingSection s)
    {
        if (s.Spreads) return;
        var u = _sim.Units;
        for (int i = 0; i < u.Count; i++)
        {
            if (s.Hit.Contains(u.Id[i])) continue;
            int x = u.X[i].FloorToInt(), z = u.Z[i].FloorToInt(), y = u.Y[i];
            foreach (var p in _placed)
            {
                if (p.X != x || p.Z != z || p.Y < y || p.Y > y + 1) continue;
                s.Hit.Add(u.Id[i]);
                _sim.Combat.Crush(i, CrushDamage);
                break;
            }
        }
    }

    /// <summary>Units buried in rubble climb out to the nearest open ground; one with nowhere to go dies.</summary>
    private void FreeUnits()
    {
        var u = _sim.Units;
        for (int i = 0; i < u.Count; i++)
        {
            int x = u.X[i].FloorToInt(), z = u.Z[i].FloorToInt(), y = u.Y[i];
            if (!BlockInfo.IsRubble(_w.Get(x, y, z)) && !BlockInfo.IsRubble(_w.Get(x, y + 1, z))) continue;
            if (Escape(x, y, z, out int ex, out int ey, out int ez))
            {
                u.X[i] = Fix64.FromInt(ex) + Fix64.Half;
                u.Z[i] = Fix64.FromInt(ez) + Fix64.Half;
                u.Y[i] = ey;
            }
            else _sim.Combat.Crush(i, Math.Max(1, u.Hp[i]));
        }
    }

    /// <summary>The nearest open ground to (x, y, z) within <see cref="EscapeRange"/> columns, nearest level first.</summary>
    private bool Escape(int x, int y, int z, out int ex, out int ey, out int ez)
    {
        for (int r = 0; r <= EscapeRange; r++)
        {
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                int level = Walk.NearestStandLevel(_w, x + dx, z + dz, y, 3);
                if (level < 0) continue;
                (ex, ey, ez) = (x + dx, level, z + dz);
                return true;
            }
        }
        (ex, ey, ez) = (x, y, z);
        return false;
    }

    // --- Water ------------------------------------------------------------------------------------------

    /// <summary>A round of flowing: each water block waiting to flow fills the block under it if that is open, otherwise the open blocks beside it.</summary>
    private void Flow()
    {
        _flowing.Clear();
        foreach (long k in _water)
        {
            _flowing.Add(k);
            if (_flowing.Count == MaxWaterPerRound) break;
        }
        foreach (long k in _flowing) _water.Remove(k);
        foreach (long k in _flowing)
        {
            int x = KX(k), y = KY(k), z = KZ(k);
            if (_w.Get(x, y, z) != BlockType.Water) continue;
            if (y > 0 && _w.Get(x, y - 1, z) == BlockType.Air)
            {
                Fill(x, y - 1, z);
                continue;
            }
            for (int f = 1; f <= 4; f++)
            {
                int nx = x + Faces[f].X, nz = z + Faces[f].Z;
                if (_w.InBounds(nx, y, nz) && _w.Get(nx, y, nz) == BlockType.Air) Fill(nx, y, nz);
            }
        }
    }

    /// <summary>Test mode: water put down at (x, y, z) flows on its next round, as if something beside it had opened.</summary>
    internal void WakeWater(int x, int y, int z)
    {
        if (_w.Get(x, y, z) == BlockType.Water) _water.Add(Key(x, y, z));
    }

    private void Fill(int x, int y, int z)
    {
        _w.Set(x, y, z, BlockType.Water);
        _water.Add(Key(x, y, z));
    }

    /// <summary>Hash of the moving pieces and the water waiting to flow.</summary>
    public ulong ComputeHash()
    {
        ulong h = 14695981039346656037UL;
        void Mix(long v) => h = (h ^ (ulong)v) * 1099511628211UL;
        Mix(_nextSection);
        foreach (var s in _falling) MixSection(s);
        foreach (var s in _started) MixSection(s);
        foreach (long k in _water) Mix(k);
        foreach (long k in _opened) Mix(k);
        Mix(_nextTree);
        foreach (var t in _trees)
        {
            Mix(t.Id); Mix(t.Started); Mix(Key(t.X, t.BaseY, t.Z)); Mix(t.DirX * 3 + t.DirZ); Mix(t.Logs);
        }
        return h;

        void MixSection(FallingSection s)
        {
            Mix(s.Id); Mix(s.Generation); Mix(s.Spreads ? 1 : 0); Mix(s.PosX); Mix(s.PosY); Mix(s.PosZ);
            Mix(s.VelX); Mix(s.VelY); Mix(s.VelZ); Mix(s._blocks.Count);
            foreach (var b in s._blocks) Mix(Key(b.X, b.Y, b.Z) * 31 + (long)b.Block);
            foreach (int id in s.Hit) Mix(id);
        }
    }
}
