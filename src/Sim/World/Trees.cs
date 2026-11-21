using System;

namespace Rts.Sim.World;

/// <summary>
/// How each kind of tree is grown out of blocks: the map generator plants them with this (see
/// <see cref="RegionGenerator"/>), and test mode puts them where you click (see <see cref="SandboxSystem"/>).
/// </summary>
public static class Trees
{
    /// <summary>Kinds of tree crown (see <see cref="Grow"/>).</summary>
    public const int CrownShapes = 4;

    /// <summary>The trunk the map's trees usually have: 4 or 5 blocks (now and then 6) before the kind's own change.</summary>
    public const int UsualTrunk = 5;

    private static readonly (int Dx, int Dz)[] Sides = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    /// <summary>
    /// A tree standing on ground block (x, g, z), with a trunk <paramref name="trunk"/> blocks tall and a
    /// crown of leaves round its top, five across with rounded corners. Crowns come in
    /// <see cref="CrownShapes"/> kinds, so trees of one height still differ: 0 the plain one (a square of
    /// three and a tuft on top), 1 rounder (a cross on top, no tuft), 2 narrower underneath (three across at
    /// the lowest layer), 3 fuller (the tuft a cross). Each kind has its own trunk wood and crown; the shape's
    /// small variations are a hash of the column and <paramref name="seed"/>. Leaves only fill air. False
    /// (and nothing grown) if the tree would not fit under the top of the world.
    /// </summary>
    public static bool Grow(VoxelWorld w, int x, int g, int z, int trunk, int crown, TreeKind kind, ulong seed)
    {
        int s = w.TreeScale;
        return s <= 1 ? GrowUsual(w, x, g, z, trunk, crown, kind, seed, x, z) : GrowScaled(w, x, g, z, trunk, crown, kind, seed, s);
    }

    /// <summary>The biggest <see cref="VoxelWorld.TreeScale"/> trees grow at.</summary>
    public const int MaxScale = 3;

    /// <summary>
    /// The columns a scaled tree's trunk stands in, for a trunk block in column (x, z): trunks of scale s are
    /// s blocks square, lined up on a grid of s, so the square holding (x, z) is the whole trunk. Its west and
    /// north edge, and its width.
    /// </summary>
    public static (int X0, int Z0, int Size) Footprint(VoxelWorld w, int x, int z)
    {
        int s = Math.Clamp(w.TreeScale, 1, MaxScale);
        return (Math.DivRem(x, s, out _) * s, Math.DivRem(z, s, out _) * s, s);
    }

    /// <summary>
    /// A usual tree grown in a small world of its own, then copied into <paramref name="w"/> with each block
    /// made a cube <paramref name="s"/> across, its trunk standing on the s-aligned square holding (x, z). Each
    /// trunk column reaches down through open air to the ground under it. Leaves only fill air.
    /// </summary>
    private static bool GrowScaled(VoxelWorld w, int x, int g, int z, int trunk, int crown, TreeKind kind, ulong seed, int s)
    {
        s = Math.Min(s, MaxScale);
        const int half = 4, tall = 16; // a usual tree: crown within 2 (willow curtains, sprigs) and under 14 tall
        var small = new VoxelWorld(half * 2 + 1, tall, half * 2 + 1);
        if (!GrowUsual(small, half, 0, half, trunk, crown, kind, seed, x, z)) return false;
        int top = 0;
        for (int ly = tall - 1; ly > 0 && top == 0; ly--)
            for (int lz = 0; lz < small.SizeZ && top == 0; lz++)
            for (int lx = 0; lx < small.SizeX; lx++)
                if (small.Get(lx, ly, lz) != BlockType.Air) { top = ly; break; }
        if (!w.InBounds(x, g, z) || g + top * s >= w.SizeY) return false;
        var (x0, z0, _) = Footprint(w, x, z);
        for (int ly = 1; ly <= top; ly++)
        for (int lz = 0; lz < small.SizeZ; lz++)
        for (int lx = 0; lx < small.SizeX; lx++)
        {
            var b = small.Get(lx, ly, lz);
            if (b == BlockType.Air) continue;
            bool wood = BlockInfo.IsTrunk(b);
            for (int j = 0; j < s; j++)
            for (int k = 0; k < s; k++)
            for (int i = 0; i < s; i++)
            {
                int bx = x0 + (lx - half) * s + i, by = g + 1 + (ly - 1) * s + j, bz = z0 + (lz - half) * s + k;
                if (!w.InBounds(bx, by, bz)) continue;
                if (wood) w.Set(bx, by, bz, b);
                else if (w.Get(bx, by, bz) == BlockType.Air) w.Set(bx, by, bz, b);
            }
        }
        // The trunk's foot: down to the ground in every column of it.
        var foot = small.Get(half, 1, half);
        for (int k = 0; k < s; k++)
        for (int i = 0; i < s; i++)
            for (int by = g; by > 0 && w.Get(x0 + i, by, z0 + k) is BlockType.Air or BlockType.Water; by--)
                w.Set(x0 + i, by, z0 + k, foot);
        return true;
    }

    /// <summary>
    /// The usual tree (see <see cref="Grow"/>), its shape's small variations a hash of column (hx, hz).
    /// </summary>
    private static bool GrowUsual(VoxelWorld w, int x, int g, int z, int trunk, int crown, TreeKind kind, ulong seed, int hx, int hz)
    {
        // Pines stand two blocks taller (their lowest boughs clear the ground like any crown), willows one,
        // and mushrooms are a block shorter than a tree, with the cap over the top of the stalk.
        trunk += kind switch { TreeKind.Pine => 2, TreeKind.Gloom => 4, TreeKind.Willow => 1, TreeKind.Mushroom => -1, _ => 0 };
        if (!w.InBounds(x, g, z) || g + trunk + 3 >= w.SizeY) return false;
        var wood = kind switch
        {
            TreeKind.Pine or TreeKind.Gloom => BlockType.PineWood,
            TreeKind.Birch => BlockType.BirchWood,
            TreeKind.Mushroom => BlockType.MushroomStem,
            _ => BlockType.Wood,
        };
        for (int y = 1; y <= trunk; y++) w.Set(x, g + y, z, wood);
        int top = g + trunk;
        uint h = DeterministicRandom.Hash(hx, hz, seed + 32);
        switch (kind)
        {
            case TreeKind.Pine: Pine(w, x, top, z, crown); return true;
            case TreeKind.Gloom: Pine(w, x, top, z, crown, BlockType.GloomLeaves); return true;
            case TreeKind.Birch: Birch(w, x, top, z, h); return true;
            case TreeKind.Willow: Willow(w, x, top, z, h); return true;
            case TreeKind.Mushroom: Mushroom(w, x, top, z, crown); return true;
        }
        for (int dy = -1; dy <= 1; dy++)
        {
            int r = dy == 1 || dy == -1 && crown == 2 ? 1 : 2;
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (Math.Abs(dx) == r && Math.Abs(dz) == r && (r == 2 || dy == 1 && crown == 1)) continue; // round the corners
                Leaf(w, x + dx, top + dy, z + dz);
            }
        }
        if (crown == 1) return true;
        Leaf(w, x, top + 2, z);
        if (crown != 3) return true;
        Leaf(w, x + 1, top + 2, z); Leaf(w, x - 1, top + 2, z); Leaf(w, x, top + 2, z + 1); Leaf(w, x, top + 2, z - 1);
        return true;
    }

    private static void Leaf(VoxelWorld w, int x, int y, int z, BlockType leaf = BlockType.Leaves)
    {
        if (w.InBounds(x, y, z) && w.Get(x, y, z) == BlockType.Air) w.Set(x, y, z, leaf);
    }

    /// <summary>A layer of crown round (x, z): a square of radius r, less its corners if <paramref name="round"/>.</summary>
    private static void Layer(VoxelWorld w, int x, int y, int z, int r, BlockType leaf, bool round = false)
    {
        for (int dz = -r; dz <= r; dz++)
        for (int dx = -r; dx <= r; dx++)
            if (!round || Math.Abs(dx) != r || Math.Abs(dz) != r) Leaf(w, x + dx, y, z + dz, leaf);
    }

    /// <summary>A diamond of leaves round (x, z): every block within two steps (a cross two long and the square inside).</summary>
    private static void Diamond(VoxelWorld w, int x, int y, int z, BlockType leaf)
    {
        for (int dz = -2; dz <= 2; dz++)
        for (int dx = -2; dx <= 2; dx++)
            if (Math.Abs(dx) + Math.Abs(dz) <= 2) Leaf(w, x + dx, y, z + dz, leaf);
    }

    /// <summary>A pine: tiers of needles narrowing to a point over the top of the trunk (a gloom pine's are <see cref="BlockType.GloomLeaves"/>).</summary>
    private static void Pine(VoxelWorld w, int x, int top, int z, int crown, BlockType n = BlockType.PineNeedles)
    {
        Layer(w, x, top - 3, z, 2, n, round: true);
        Layer(w, x, top - 2, z, 1, n);
        if (crown % 2 == 0) Diamond(w, x, top - 1, z, n); else Layer(w, x, top - 1, z, 2, n, round: true);
        Layer(w, x, top, z, 1, n);
        Leaf(w, x, top + 1, z, n);
        Leaf(w, x + 1, top + 1, z, n); Leaf(w, x - 1, top + 1, z, n); Leaf(w, x, top + 1, z + 1, n); Leaf(w, x, top + 1, z - 1, n);
        Leaf(w, x, top + 2, z, n);
        if (crown >= 2) Leaf(w, x, top + 3, z, n);
    }

    /// <summary>A birch: a small, open crown, with a sprig or two reaching out.</summary>
    private static void Birch(VoxelWorld w, int x, int top, int z, uint h)
    {
        var b = BlockType.BirchLeaves;
        Layer(w, x, top - 1, z, 1, b);
        Layer(w, x, top, z, 1, b);
        for (int k = 0; k < 4; k++)
        {
            if ((h >> k & 1) == 0) continue;
            var (dx, dz) = Sides[k];
            Leaf(w, x + 2 * dx, top - (int)(h >> 4 + k & 1), z + 2 * dz, b);
        }
        Leaf(w, x, top + 1, z, b); Leaf(w, x + 1, top + 1, z, b); Leaf(w, x - 1, top + 1, z, b);
        Leaf(w, x, top + 1, z + 1, b); Leaf(w, x, top + 1, z - 1, b);
        if ((h >> 8 & 1) == 0) Leaf(w, x, top + 2, z, b);
    }

    /// <summary>A willow: a wide, low dome with curtains of leaves hanging from its rim.</summary>
    private static void Willow(VoxelWorld w, int x, int top, int z, uint h)
    {
        var l = BlockType.WillowLeaves;
        Layer(w, x, top, z, 2, l, round: true);
        Layer(w, x, top + 1, z, 1, l);
        Leaf(w, x, top + 2, z, l);
        // Curtains: down one or two blocks from the rim (never lower than a unit's head, two blocks up).
        for (int dz = -2; dz <= 2; dz++)
        for (int dx = -2; dx <= 2; dx++)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != 2 || Math.Abs(dx) == 2 && Math.Abs(dz) == 2) continue;
            int hang = 1 + (int)(DeterministicRandom.Hash(x + dx, z + dz, (ulong)h) & 1);
            for (int k = 1; k <= hang; k++)
            {
                int y = top - k;
                if (!w.InBounds(x + dx, y - 2, z + dz) || w.Get(x + dx, y - 1, z + dz) != BlockType.Air
                    || w.Get(x + dx, y - 2, z + dz) != BlockType.Air) break;
                Leaf(w, x + dx, y, z + dz, l);
            }
        }
    }

    /// <summary>A giant mushroom: a wide cap over a pale stalk.</summary>
    private static void Mushroom(VoxelWorld w, int x, int top, int z, int crown)
    {
        var c = BlockType.MushroomCap;
        Layer(w, x, top + 1, z, 2, c, round: true);
        if (crown % 2 == 0) Layer(w, x, top + 2, z, 1, c); else Diamond(w, x, top + 2, z, c);
        if (crown >= 2) Leaf(w, x, top + 3, z, c);
    }
}
