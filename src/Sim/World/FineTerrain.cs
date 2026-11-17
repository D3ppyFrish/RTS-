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
/// mud and stone with air above, and no water beside it at its level); what stands on the ground, and what has been dug or built, keeps
/// its whole blocks. So the finer world is never higher than the whole blocks, and never more than half a
/// block lower. Integer-only: the same world gives the same finer world.
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

    private static bool IsLevel(BlockType t) => t == BlockType.Water || IsTerrain(t);

    /// <summary>
    /// Which fine columns (two a block each way, indexed <c>fz * 2 * SizeX + fx</c>) lose the top half of
    /// their ground block, from the ground's heights (see <see cref="Heights"/>).
    /// </summary>
    public static bool[] Lowered(VoxelWorld coarse, int[] heights)
    {
        int sx = coarse.SizeX, sz = coarse.SizeZ;
        var lowered = new bool[4 * sx * sz];
        // Distances are doubled fine blocks (so the middle of a fine column is a whole number), squared.
        int reach2 = 4 * Reach * 4 * Reach, shoulder2 = 4 * Shoulder * 4 * Shoulder;
        Span<int> down = stackalloc int[4];
        Span<int> up = stackalloc int[4];
        for (int z = 0; z < sz; z++)
        for (int x = 0; x < sx; x++)
        {
            int g = heights[z * sx + x];
            if (g < 0 || !BlockInfo.IsGround(coarse.Get(x, g, z))) continue;
            if (g + 1 < coarse.SizeY && coarse.Get(x, g + 1, z) != BlockType.Air) continue;
            // Ground level with water beside it stays whole: a lowered step there would lie under the water.
            if (WaterBeside(coarse, x, g, z)) continue;
            down.Fill(int.MaxValue);
            up.Fill(int.MaxValue);
            for (int dz = -Reach; dz <= Reach; dz++)
            {
                int nz = z + dz;
                if (nz < 0 || nz >= sz) continue;
                for (int dx = -Reach; dx <= Reach; dx++)
                {
                    int nx = x + dx;
                    if (nx < 0 || nx >= sx) continue;
                    int hn = heights[nz * sx + nx];
                    if (hn == g) continue;
                    var to = hn < g ? down : up;
                    for (int s = 0; s < 4; s++)
                    {
                        int ex = Gap(dx, s & 1), ez = Gap(dz, s >> 1);
                        int d2 = ex * ex + ez * ez;
                        if (d2 < to[s]) to[s] = d2;
                    }
                }
            }
            for (int s = 0; s < 4; s++)
            {
                if (down[s] > reach2) continue;
                int limit = up[s] > reach2 ? shoulder2 : up[s];
                if (down[s] < limit) lowered[(z * 2 + (s >> 1)) * 2 * sx + x * 2 + (s & 1)] = true;
            }
        }
        return lowered;
    }

    /// <summary>
    /// Twice the distance along one axis, in fine blocks, from the middle of a block's fine column
    /// <paramref name="sub"/> (0 or 1) to the block <paramref name="d"/> blocks along.
    /// </summary>
    private static int Gap(int d, int sub) => d > 0 ? 4 * d - 2 * sub - 1 : d < 0 ? -4 * d + 2 * sub - 3 : 0;

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

    private static bool WaterBeside(VoxelWorld w, int x, int y, int z)
    {
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
            if (w.Get(x + dx, y, z + dz) == BlockType.Water) return true;
        return false;
    }

    /// <summary>
    /// Brings <paramref name="fine"/> up to date after block (x, y, z) of <paramref name="coarse"/> changed:
    /// its eight half-size blocks become what it is now, and a block set down on a lowered step fills the step
    /// under it in, so nothing stands on a gap.
    /// </summary>
    public static void Mirror(VoxelWorld coarse, VoxelWorld fine, int x, int y, int z)
    {
        var b = coarse.Get(x, y, z);
        Fill(fine, x, y, z, b);
        if (BlockInfo.IsSolid(b) && y > 0)
        {
            var below = coarse.Get(x, y - 1, z);
            if (BlockInfo.IsSolid(below)) Fill(fine, x, y - 1, z, below);
        }
    }

    private static void Fill(VoxelWorld fine, int x, int y, int z, BlockType b)
    {
        for (int dy = 0; dy < 2; dy++)
        for (int dz = 0; dz < 2; dz++)
        for (int dx = 0; dx < 2; dx++)
            fine.Set(2 * x + dx, 2 * y + dy, 2 * z + dz, b);
    }

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
