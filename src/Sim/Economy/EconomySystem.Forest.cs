using System;
using System.Collections.Generic;
using Rts.Sim.World;

namespace Rts.Sim.Economy;

/// <summary>A young tree a forester planted (see <see cref="EconomySystem.Saplings"/>): one leaf block on the ground until it grows.</summary>
/// <param name="X">West edge of the columns its trunk will stand in (lined up on the tree grid; see <see cref="Trees.Footprint"/>).</param>
/// <param name="Y">The level the sapling's block is at, one over the ground.</param>
/// <param name="Z">North edge of its trunk's columns.</param>
/// <param name="Kind">The tree it grows into.</param>
/// <param name="Planted">The tick it was planted.</param>
/// <param name="Lodge">The forester's lodge that planted it.</param>
public readonly record struct Sapling(int X, int Y, int Z, TreeKind Kind, long Planted, int Lodge);

/// <summary>
/// Foresters (the design's living world: forests cut down can be grown back). A forester's lodge is a quarry whose
/// rounds of work give no goods (see <see cref="BuildingDef.Plants"/>): each round plants a sapling on open grass or
/// soil within reach of the lodge, kept clear of buildings, roads, hazards and other trees. A sapling is a single leaf
/// block of its kind; after <see cref="SaplingGrowTicks"/> it grows into a full tree (once nobody stands on its
/// spot), which wood cutters fell like any other. A sapling dug up, built over or knocked off by a felled tree is gone.
/// </summary>
public sealed partial class EconomySystem
{
    /// <summary>How long a sapling takes to grow into a tree.</summary>
    public const int SaplingGrowTicks = 120 * Tps;

    /// <summary>
    /// Columns kept clear between a sapling's trunk square and any other trunk or sapling, in tree-grid steps: two
    /// crowns' reach, so grown crowns meet but do not run into one another.
    /// </summary>
    public const int TreeSpacing = 2 * Harvest.CrownRadius;

    /// <summary>Columns kept clear between a sapling's trunk square and any building, in tree-grid steps: its crown's reach and one.</summary>
    public const int BuildingSpacing = Harvest.CrownRadius + 1;

    private readonly List<Sapling> _saplings = new();

    /// <summary>Saplings that have grown into trees this match.</summary>
    public int TreesGrown { get; private set; }

    /// <summary>Saplings growing, oldest first.</summary>
    public IReadOnlyList<Sapling> Saplings => _saplings;

    /// <summary>The saplings building b planted that are still growing.</summary>
    public int SaplingsOf(Building b)
    {
        int n = 0;
        foreach (var s in _saplings)
            if (s.Lodge == b.Id) n++;
        return n;
    }

    /// <summary>The block a sapling of the kind is: a leaf of that tree.</summary>
    public static BlockType SaplingBlock(TreeKind kind) => kind switch
    {
        TreeKind.Pine => BlockType.PineNeedles,
        TreeKind.Birch => BlockType.BirchLeaves,
        TreeKind.Gloom => BlockType.GloomLeaves,
        _ => BlockType.Leaves,
    };

    /// <summary>
    /// A round of work at lodge b is done: plants a sapling at the free spot within <see cref="BuildingDef.Plants"/>
    /// columns of it nearest to it (ties by a hash of the column). The tree is the kind of the nearest trunk within
    /// twice that reach (broad-leaved if none; willows and mushrooms come up broad-leaved). False if there is no room.
    /// </summary>
    internal bool Plant(Building b)
    {
        var w = World;
        int s = Math.Clamp(w.TreeScale, 1, Trees.MaxScale);
        int reach = b.Def.Plants;
        int bestX = 0, bestZ = 0, bestG = 0;
        long bestKey = long.MaxValue;
        int cx = b.X + b.Def.SizeX / 2, cz = b.Z + b.Def.SizeZ / 2;
        int x0 = Math.Max(1, b.X - reach), x1 = Math.Min(w.SizeX - 2 - s, b.MaxX + reach);
        int z0 = Math.Max(1, b.Z - reach), z1 = Math.Min(w.SizeZ - 2 - s, b.MaxZ + reach);
        for (int z = z0 - z0 % s + (z0 % s == 0 ? 0 : s); z <= z1; z += s)
        for (int x = x0 - x0 % s + (x0 % s == 0 ? 0 : s); x <= x1; x += s)
        {
            long dx = x + s / 2 - cx, dz = z + s / 2 - cz;
            long key = (dx * dx + dz * dz) << 16 | DeterministicRandom.Hash(x, z, _sim.Seed + 71) & 0xFFFF;
            if (key >= bestKey || !CanPlant(x, z, s, out int g)) continue;
            (bestX, bestZ, bestG, bestKey) = (x, z, g, key);
        }
        if (bestKey == long.MaxValue) return false;
        var kind = NearestKind(cx, cz, 2 * reach + Math.Max(b.Def.SizeX, b.Def.SizeZ));
        w.Set(bestX, bestG + 1, bestZ, SaplingBlock(kind));
        _saplings.Add(new Sapling(bestX, bestG + 1, bestZ, kind, _sim.Tick, b.Id));
        return true;
    }

    /// <summary>
    /// How many saplings a forester's lodge of <paramref name="def"/> with its corner at (x, z) would find room for
    /// now (counting no further than <paramref name="most"/>): the tree-grid spots within its reach where a sapling
    /// could go (see <see cref="CanPlant"/>), and far enough from the lodge itself. The spots counted are a tree's spacing apart, so
    /// all of them could be planted. A look ahead for whoever lays one out (computer players); it changes nothing.
    /// </summary>
    public int PlantingRoom(BuildingDef def, int x, int z, int most)
    {
        if (def.Plants <= 0) return 0;
        var w = World;
        int s = Math.Clamp(w.TreeScale, 1, Trees.MaxScale);
        int reach = def.Plants, clear = BuildingSpacing * s;
        int maxX = x + def.SizeX - 1, maxZ = z + def.SizeZ - 1;
        int x0 = Math.Max(1, x - reach), x1 = Math.Min(w.SizeX - 2 - s, maxX + reach);
        int z0 = Math.Max(1, z - reach), z1 = Math.Min(w.SizeZ - 2 - s, maxZ + reach);
        int n = 0;
        int step = s * (TreeSpacing + 1);
        for (int pz = z0 - z0 % s + (z0 % s == 0 ? 0 : s); pz <= z1 && n < most; pz += step)
        for (int px = x0 - x0 % s + (x0 % s == 0 ? 0 : s); px <= x1 && n < most; px += step)
        {
            if (px <= maxX + clear && px + s - 1 >= x - clear && pz <= maxZ + clear && pz + s - 1 >= z - clear) continue;
            if (CanPlant(px, pz, s, out _)) n++;
        }
        return n;
    }

    /// <summary>
    /// True if a tree's s-by-s trunk square with its north-west corner at (x, z) can be planted: every column grass
    /// or soil at one level g with open air over it, not a hazard, <see cref="BuildingSpacing"/> times s columns clear of
    /// every building's claim (so the grown crown does not hang over it) and clear of city-states, with nobody standing on it, and no trunk or sapling within <see cref="TreeSpacing"/> times s columns of it.
    /// </summary>
    private bool CanPlant(int x, int z, int s, out int g)
    {
        var w = World;
        g = w.SurfaceHeight(x, z);
        if (g < 2 || g + 4 * s >= w.SizeY) return false;
        for (int cz = z; cz < z + s; cz++)
        for (int cx = x; cx < x + s; cx++)
        {
            if (w.SurfaceHeight(cx, cz) != g || w.Get(cx, g, cz) is not (BlockType.Grass or BlockType.Gloom or BlockType.Soil)) return false;
            for (int y = g + 1; y <= g + 3; y++)
                if (w.Get(cx, y, cz) != BlockType.Air) return false;
            if (HazardSystem.IsHazard(w, cx, g + 1, cz)) return false;
        }
        int clear = BuildingSpacing * s;
        foreach (var b in Buildings.All)
        {
            var (bx0, bz0, bx1, bz1) = b.Claim;
            if (x <= bx1 + clear && x + s - 1 >= bx0 - clear && z <= bz1 + clear && z + s - 1 >= bz0 - clear) return false;
        }
        if (_sim.Cities.Crowds(x, z, x + s - 1, z + s - 1) || Occupied(x, z, s, g + 1)) return false;
        int gap = TreeSpacing * s;
        foreach (var p in _saplings)
            if (p.X + s - 1 >= x - gap && p.X <= x + s - 1 + gap && p.Z + s - 1 >= z - gap && p.Z <= z + s - 1 + gap) return false;
        for (int cz = Math.Max(0, z - gap); cz <= Math.Min(w.SizeZ - 1, z + s - 1 + gap); cz++)
        for (int cx = Math.Max(0, x - gap); cx <= Math.Min(w.SizeX - 1, x + s - 1 + gap); cx++)
        for (int y = Math.Max(1, g - 2); y <= g + 3; y++)
        {
            var c = w.Get(cx, y, cz);
            if (BlockInfo.IsTrunk(c) || BlockInfo.IsDuskTree(c)) return false;
        }
        return true;
    }

    /// <summary>The kind of the trunk nearest column (x, z) within <paramref name="reach"/> columns, near the ground; broad-leaved if none.</summary>
    private TreeKind NearestKind(int x, int z, int reach)
    {
        var w = World;
        long best = long.MaxValue;
        var kind = TreeKind.Broadleaf;
        for (int cz = Math.Max(0, z - reach); cz <= Math.Min(w.SizeZ - 1, z + reach); cz++)
        for (int cx = Math.Max(0, x - reach); cx <= Math.Min(w.SizeX - 1, x + reach); cx++)
        {
            long d = (long)(cx - x) * (cx - x) + (long)(cz - z) * (cz - z);
            if (d >= best) continue;
            // Under its crown: the first trunk block down the column.
            var t = BlockType.Air;
            for (int y = w.SurfaceHeight(cx, cz), low = Math.Max(1, y - 24); y >= low && !BlockInfo.IsTrunk(t); y--) t = w.Get(cx, y, cz);
            if (!BlockInfo.IsTrunk(t)) continue;
            best = d;
            kind = t switch { BlockType.PineWood when _sim.Map.BiomeAt(cx, cz) == Biome.Wilds => TreeKind.Gloom, BlockType.PineWood => TreeKind.Pine, BlockType.BirchWood => TreeKind.Birch, _ => TreeKind.Broadleaf };
        }
        return kind;
    }

    /// <summary>
    /// Once a second: saplings whose block is gone are dropped, and those old enough grow into trees, unless a unit
    /// stands on the trunk's columns (it tries again the next second) or a building has been put there (dropped).
    /// </summary>
    private void StepSaplings()
    {
        if (_saplings.Count == 0 || _sim.Tick % Tps != 0) return;
        var w = World;
        int s = Math.Clamp(w.TreeScale, 1, Trees.MaxScale);
        for (int k = 0; k < _saplings.Count; k++)
        {
            var p = _saplings[k];
            if (w.Get(p.X, p.Y, p.Z) != SaplingBlock(p.Kind) || Built(p.X, p.Z, s))
            {
                _saplings.RemoveAt(k--);
                continue;
            }
            if (_sim.Tick - p.Planted < SaplingGrowTicks || Occupied(p.X, p.Z, s, p.Y)) continue;
            _saplings.RemoveAt(k--);
            w.Set(p.X, p.Y, p.Z, BlockType.Air);
            TreesGrown++;
            uint h = DeterministicRandom.Hash(p.X, p.Z, _sim.Seed + 33);
            Trees.Grow(w, p.X, p.Y - 1, p.Z, 4 + (int)(h & 1), (int)(h >> 1 & 3), p.Kind, _sim.Seed);
        }
    }

    private bool Built(int x, int z, int s)
    {
        foreach (var b in Buildings.All)
            if (x <= b.MaxX && x + s - 1 >= b.X && z <= b.MaxZ && z + s - 1 >= b.Z) return true;
        return false;
    }

    /// <summary>True if a unit stands in the s-by-s square at (x, z), near level y.</summary>
    private bool Occupied(int x, int z, int s, int y)
    {
        var u = U;
        for (int i = 0; i < u.Count; i++)
        {
            int ux = u.X[i].FloorToInt(), uz = u.Z[i].FloorToInt();
            if (ux >= x && ux < x + s && uz >= z && uz < z + s && u.Y[i] >= y - 2 && u.Y[i] <= y + 2) return true;
        }
        return false;
    }

    private void MixSaplings(ref ulong h)
    {
        h = (h ^ (ulong)TreesGrown) * 1099511628211UL;
        h = (h ^ (ulong)GameHunted) * 1099511628211UL;
        foreach (var p in _saplings)
        {
            h = (h ^ (ulong)(p.X * 73856093L ^ p.Y * 19349663L ^ p.Z * 83492791L)) * 1099511628211UL;
            h = (h ^ (ulong)((long)p.Kind << 40 ^ p.Planted ^ (long)p.Lodge << 20)) * 1099511628211UL;
        }
    }
}
