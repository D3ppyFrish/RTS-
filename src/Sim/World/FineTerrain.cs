using System;
using Rts.Sim.Meshing;

namespace Rts.Sim.World;

/// <summary>
/// The world at twice the resolution: every block as eight blocks half its size (0.25 m), with the open
/// ground's steps split in two. A prototype of half-size world blocks: the game's rules still run on the
/// whole blocks (<see cref="VoxelWorld"/> as before), and this finer world is what is drawn near the camera.
///
/// Every block becomes eight (<see cref="VoxelWorld.Upscaled"/>), and then the top half of a ground block is
/// taken off wherever the ground there is nearer to lower ground than to higher ground: a hillside that
/// climbed in half-metre steps climbs in quarter-metre ones, each contour halfway between the two it lies
/// between, and the edge of a plateau gets a shoulder. Only open natural ground changes (grass, soil, sand,
/// mud and stone with air above, and no water, ice or lava beside it at its level); what stands on the
/// ground, and what is built, keeps its whole blocks. So the finer world is never higher than the whole
/// blocks, and never more than half a block lower. Integer-only: the same world gives the same finer world,
/// however it came to be (<see cref="FineWorld"/> keeps one up to date as the world changes).
/// </summary>
public static class FineTerrain
{
    /// <summary>How far (whole blocks) a column looks for lower and higher ground.</summary>
    public const int Reach = 8;

    /// <summary>How wide (whole blocks) the lowered shoulder is where no higher ground is in reach.</summary>
    public const int Shoulder = 2;

    /// <summary>Blocks that make up the ground's height: solid, and neither a plant nor drawn as fine voxels.</summary>
    public static bool IsTerrain(BlockType t) =>
        BlockInfo.IsSolid(t) && t != BlockType.BerryBush && !StructureDetail.IsDetailed(t);

    /// <summary>The finer world of <paramref name="coarse"/>, as it is now.</summary>
    public static VoxelWorld Build(VoxelWorld coarse)
    {
        var fine = coarse.Upscaled();
        Refine(coarse, fine);
        return fine;
    }

    /// <summary>
    /// The height of each column of <paramref name="w"/>, as lower or higher ground is told by: its highest
    /// terrain block or, if it lies under water, the water's top block; -1 if it has neither.
    /// </summary>
    public static int[] Heights(VoxelWorld w)
    {
        var h = new int[w.SizeX * w.SizeZ];
        for (int z = 0; z < w.SizeZ; z++)
        for (int x = 0; x < w.SizeX; x++)
        {
            int y = w.SizeY - 1;
            while (y >= 0 && !IsLevel(w.Get(x, y, z))) y--;
            h[z * w.SizeX + x] = y;
        }
        return h;
    }

    /// <summary>
    /// Which fine columns (two a block each way, indexed <c>fz * 2 * SizeX + fx</c>) lose the top half of
    /// their ground block, from the ground's heights (see <see cref="Heights"/>).
    /// </summary>
    public static bool[] Lowered(VoxelWorld coarse, int[] heights)
    {
        int sx = coarse.SizeX, sz = coarse.SizeZ;
        var lowered = new bool[4 * sx * sz];
        for (int z = 0; z < sz; z++)
        for (int x = 0; x < sx; x++)
        {
            int bits = LoweredAt(coarse, heights, x, z);
            for (int s = 0; s < 4; s++)
                if ((bits & (1 << s)) != 0) lowered[(z * 2 + (s >> 1)) * 2 * sx + x * 2 + (s & 1)] = true;
        }
        return lowered;
    }

    /// <summary>
    /// Which of the four fine columns of block column (x, z) lose the top half of their ground block: bit
    /// <c>s</c> is the fine column <c>s &amp; 1</c> along x and <c>s &gt;&gt; 1</c> along z.
    /// </summary>
    public static int LoweredAt(VoxelWorld coarse, int[] heights, int x, int z)
    {
        int sx = coarse.SizeX, sz = coarse.SizeZ;
        int g = heights[z * sx + x];
        if (g < 0 || !BlockInfo.IsGround(coarse.Get(x, g, z))) return 0;
        if (g + 1 < coarse.SizeY && coarse.Get(x, g + 1, z) != BlockType.Air) return 0;
        // Ground level with water, ice or lava beside it stays whole: a lowered step there would lie under it.
        if (LiquidBeside(coarse, x, g, z)) return 0;
        // Distances are doubled fine blocks (so the middle of a fine column is a whole number), squared.
        const int reach2 = 4 * Reach * 4 * Reach, shoulder2 = 4 * Shoulder * 4 * Shoulder;
        Span<int> down = stackalloc int[4];
        Span<int> up = stackalloc int[4];
        down.Fill(int.MaxValue);
        up.Fill(int.MaxValue);
        int x0 = Math.Max(x - Reach, 0), x1 = Math.Min(x + Reach, sx - 1);
        for (int nz = Math.Max(z - Reach, 0); nz <= Math.Min(z + Reach, sz - 1); nz++)
        {
            int z0 = GapSquared[2 * (nz - z + Reach)], z1 = GapSquared[2 * (nz - z + Reach) + 1];
            int row = nz * sx;
            for (int nx = x0; nx <= x1; nx++)
            {
                int hn = heights[row + nx];
                if (hn == g) continue;
                var to = hn < g ? down : up;
                int e0 = GapSquared[2 * (nx - x + Reach)], e1 = GapSquared[2 * (nx - x + Reach) + 1];
                if (e0 + z0 < to[0]) to[0] = e0 + z0;
                if (e1 + z0 < to[1]) to[1] = e1 + z0;
                if (e0 + z1 < to[2]) to[2] = e0 + z1;
                if (e1 + z1 < to[3]) to[3] = e1 + z1;
            }
        }
        int bits = 0;
        for (int s = 0; s < 4; s++)
        {
            if (down[s] > reach2) continue;
            int limit = up[s] > reach2 ? shoulder2 : up[s];
            if (down[s] < limit) bits |= 1 << s;
        }
        return bits;
    }

    /// <summary>
    /// Twice the distance along one axis, in fine blocks, from the middle of a block's fine column
    /// <paramref name="sub"/> (0 or 1) to the block <paramref name="d"/> blocks along.
    /// </summary>
    private static int Gap(int d, int sub) => d > 0 ? 4 * d - 2 * sub - 1 : d < 0 ? -4 * d + 2 * sub - 3 : 0;

    /// <summary><see cref="Gap"/> squared, for each block from -<see cref="Reach"/> to <see cref="Reach"/> along and each fine column: at <c>2 * (d + Reach) + sub</c>.</summary>
    private static readonly int[] GapSquared = MakeGapSquared();

    private static int[] MakeGapSquared()
    {
        var squared = new int[2 * (2 * Reach + 1)];
        for (int d = -Reach; d <= Reach; d++)
        for (int sub = 0; sub < 2; sub++)
            squared[2 * (d + Reach) + sub] = Gap(d, sub) * Gap(d, sub);
        return squared;
    }

    /// <summary>Takes the lowered half blocks (see <see cref="Lowered"/>) off <paramref name="fine"/>, a plain upscale of <paramref name="coarse"/>.</summary>
    private static void Refine(VoxelWorld coarse, VoxelWorld fine)
    {
        int sx = coarse.SizeX, sz = coarse.SizeZ, fx2 = 2 * sx;
        var heights = Heights(coarse);
        var lowered = Lowered(coarse, heights);
        for (int fz = 0; fz < 2 * sz; fz++)
        for (int fx = 0; fx < fx2; fx++)
            if (lowered[fz * fx2 + fx]) fine.Set(fx, 2 * heights[(fz / 2) * sx + fx / 2] + 1, fz, BlockType.Air);
    }

    /// <summary>True if water, ice or lava lies beside block (x, y, z) at its level.</summary>
    private static bool LiquidBeside(VoxelWorld w, int x, int y, int z)
    {
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
            if (w.Get(x + dx, y, z + dz) is BlockType.Water or BlockType.Ice or BlockType.Lava) return true;
        return false;
    }

    /// <summary>Makes the eight half-size blocks of whole block (x, y, z) of <paramref name="fine"/> <paramref name="b"/>.</summary>
    internal static void Fill(VoxelWorld fine, int x, int y, int z, BlockType b)
    {
        for (int dy = 0; dy < 2; dy++)
        for (int dz = 0; dz < 2; dz++)
        for (int dx = 0; dx < 2; dx++)
            fine.Set(2 * x + dx, 2 * y + dy, 2 * z + dz, b);
    }

    /// <summary>True if <paramref name="t"/> makes up the ground's height as lower or higher ground is told by (see <see cref="Heights"/>).</summary>
    internal static bool IsLevel(BlockType t) => t == BlockType.Water || IsTerrain(t);

    /// <summary>
    /// True if the top half of whole block (x, y, z) is missing above fine column (fx, fz) of
    /// <paramref name="fine"/>: the ground there is a quarter of a metre lower than the rules have it.
    /// </summary>
    public static bool IsLowered(VoxelWorld fine, int fx, int y, int fz) =>
        BlockInfo.IsSolid(fine.Get(fx, 2 * y, fz)) && !BlockInfo.IsSolid(fine.Get(fx, 2 * y + 1, fz));

    /// <summary>
    /// Copies the padded chunk <paramref name="padded"/> of <paramref name="coarse"/> chunk (cx, cy, cz) (see
    /// <see cref="VoxelWorld.CopyChunkPadded"/>) into <paramref name="dest"/> with every ground block that has
    /// lost any of its top half in <paramref name="fine"/> made air. With
    /// <paramref name="borderOnly"/> only the border changes: a chunk drawn in whole blocks then draws its sides
    /// against a neighbour drawn finer, so no gap shows between the two.
    /// </summary>
    public static void Open(VoxelWorld fine, ReadOnlySpan<BlockType> padded, int cx, int cy, int cz, Span<BlockType> dest, bool borderOnly)
    {
        const int s = VoxelWorld.ChunkSize, p = VoxelWorld.PaddedSize;
        padded.Slice(0, p * p * p).CopyTo(dest);
        for (int y = 0; y < p; y++)
        for (int z = 0; z < p; z++)
        for (int x = 0; x < p; x++)
        {
            bool border = x == 0 || x == p - 1 || y == 0 || y == p - 1 || z == 0 || z == p - 1;
            if (borderOnly && !border) continue;
            int at = (y * p + z) * p + x;
            if (!BlockInfo.IsGround(padded[at])) continue;
            int wx = cx * s + x - 1, wy = cy * s + y - 1, wz = cz * s + z - 1;
            for (int k = 0; k < 4; k++)
            {
                var top = fine.Get(2 * wx + (k & 1), 2 * wy + 1, 2 * wz + (k >> 1));
                if (BlockInfo.IsSolid(top)) continue;
                dest[at] = top;
                break;
            }
        }
    }
}
