using System;
using System.Collections.Generic;

namespace Rts.Sim.Meshing;

/// <summary>
/// One rectangle of merged fine-voxel faces (see <see cref="StructureDetail"/>), in fine voxels relative to
/// the chunk's corner. Face numbering and corners as in <see cref="Quad"/>.
/// </summary>
public readonly record struct FineQuad(byte Face, byte Colour, int X, int Y, int Z, int W, int H, byte Ao = Occlusion.Open)
{
    /// <summary>How open corner <paramref name="i"/> is (see <see cref="Occlusion"/>).</summary>
    public int AoAt(int i) => Occlusion.At(Ao, (Face & 1) == 0, i);

    /// <summary>Corner <paramref name="i"/> (0..3), counter-clockwise when seen from outside the solid.</summary>
    public (int X, int Y, int Z) Corner(int i) => new Quad(Face, BlockType.Air, X, Y, Z, W, H).Corner(i);
}

/// <summary>
/// Meshes the building blocks (and rubble) of a chunk at character scale: every such block of the chunk (see
/// <see cref="StructureDetail.IsDetailed"/>) becomes its fine voxels, and the visible faces of those are
/// merged greedily by colour. Terrain around them hides faces that touch it. The terrain mesher is given the
/// chunk with its building blocks taken out (<see cref="StripStructures"/>), so the ground under and beside a
/// building is drawn where the building's finer shape leaves it showing. Like <see cref="GreedyMesher"/> it
/// works on a padded copy of a chunk, so it can run off the main thread.
/// </summary>
public static class StructureMesher
{
    private const int S = VoxelWorld.ChunkSize;
    private const int P = VoxelWorld.PaddedSize;
    private const int F = StructureDetail.Size;

    [ThreadStatic] private static byte[]? _grid;
    [ThreadStatic] private static BlockType[]? _near;
    // The fine voxels of each block of the chunk being built, worked out once (see Shape).
    [ThreadStatic] private static byte[]? _shapes;
    [ThreadStatic] private static bool[]? _shaped;

    /// <summary>True if the padded chunk has any building blocks of its own (not counting its border).</summary>
    public static bool HasStructures(ReadOnlySpan<BlockType> padded)
    {
        for (int y = 0; y < S; y++)
        for (int z = 0; z < S; z++)
        for (int x = 0; x < S; x++)
            if (StructureDetail.IsDetailed(padded[GreedyMesher.Index(x, y, z)])) return true;
        return false;
    }

    /// <summary>Copies a padded chunk into <paramref name="dest"/> with every building block made air.</summary>
    public static void StripStructures(ReadOnlySpan<BlockType> padded, Span<BlockType> dest)
    {
        for (int i = 0; i < P * P * P; i++)
        {
            var b = padded[i];
            dest[i] = StructureDetail.IsDetailed(b) ? BlockType.Air : b;
        }
    }

    /// <summary>Appends the fine quads of the chunk's building blocks to <paramref name="output"/>.</summary>
    public static void Build(ReadOnlySpan<BlockType> padded, int chunkX, int chunkY, int chunkZ, List<FineQuad> output)
    {
        if (padded.Length < P * P * P) throw new ArgumentException("Padded chunk is too small", nameof(padded));
        _shapes ??= new byte[S * S * S * StructureDetail.Volume];
        Array.Clear(_shaped ??= new bool[S * S * S]);
        Region(padded, chunkX, chunkY, chunkZ, 0, 0, 0, S, output);
    }

    /// <summary>Whether the chunk's fine quads carry ambient occlusion (see <see cref="Occlusion"/>).</summary>
    [ThreadStatic] private static bool _ao;

    /// <summary>As <see cref="Build(ReadOnlySpan{BlockType}, int, int, int, List{FineQuad})"/>, with ambient occlusion if <paramref name="ao"/>.</summary>
    public static void Build(ReadOnlySpan<BlockType> padded, int chunkX, int chunkY, int chunkZ, List<FineQuad> output, bool ao)
    {
        _ao = ao;
        try { Build(padded, chunkX, chunkY, chunkZ, output); }
        finally { _ao = false; }
    }

    /// <summary>How many fine voxels a side the voxels of the chunk being built are drawn as (see <see cref="Build(ReadOnlySpan{BlockType}, int, int, int, List{FineQuad}, bool, int)"/>).</summary>
    [ThreadStatic] private static int _step;
    [ThreadStatic] private static byte[]? _small;

    /// <summary>
    /// As <see cref="Build(ReadOnlySpan{BlockType}, int, int, int, List{FineQuad}, bool)"/>, drawn more simply
    /// for far away: with <paramref name="step"/> 2 or 4 the fine voxels are merged into voxels that many a
    /// side before they are meshed (a block is then 4 or 2 voxels a side instead of 8), each as solid as most
    /// of what it stands for and its commonest colour. The quads are still in fine voxels.
    /// </summary>
    public static void Build(ReadOnlySpan<BlockType> padded, int chunkX, int chunkY, int chunkZ, List<FineQuad> output, bool ao, int step)
    {
        if (step != 1 && step != 2 && step != 4) throw new ArgumentOutOfRangeException(nameof(step));
        _step = step;
        try { Build(padded, chunkX, chunkY, chunkZ, output, ao); }
        finally { _step = 1; }
    }

    /// <summary>
    /// Merges a grid of fine voxels with a border one voxel thick (as <see cref="MeshBox"/> fills it) into
    /// voxels <paramref name="step"/> a side, with the border still one voxel thick. A merged voxel is drawn
    /// if at least <see cref="KeepSixteenths"/> sixteenths of it are, in the commonest colour there; the border
    /// hides faces only where all of it does, so nothing drawn simply is left open against its neighbour.
    /// </summary>
    public static void Shrink(ReadOnlySpan<byte> grid, int nx, int ny, int nz, Span<byte> small, int step)
    {
        int mx = (nx - 2) / step + 2, my = (ny - 2) / step + 2, mz = (nz - 2) / step + 2;
        Span<byte> counts = stackalloc byte[StructurePalette.Count];
        int need = (step * step * step * KeepSixteenths + 15) / 16;
        for (int j = 0; j < my; j++)
        for (int k = 0; k < mz; k++)
        for (int i = 0; i < mx; i++)
        {
            int xa = i == 0 ? 0 : 1 + (i - 1) * step, xb = i == 0 ? 1 : i == mx - 1 ? nx : xa + step;
            int ya = j == 0 ? 0 : 1 + (j - 1) * step, yb = j == 0 ? 1 : j == my - 1 ? ny : ya + step;
            int za = k == 0 ? 0 : 1 + (k - 1) * step, zb = k == 0 ? 1 : k == mz - 1 ? nz : za + step;
            bool border = i == 0 || j == 0 || k == 0 || i == mx - 1 || j == my - 1 || k == mz - 1;
            byte result;
            if (border)
            {
                result = StructurePalette.Occluder;
                for (int y = ya; y < yb && result != StructurePalette.Empty; y++)
                for (int z = za; z < zb && result != StructurePalette.Empty; z++)
                for (int x = xa; x < xb; x++)
                    if (grid[(y * nz + z) * nx + x] == StructurePalette.Empty) { result = StructurePalette.Empty; break; }
            }
            else
            {
                counts.Clear();
                int solid = 0;
                for (int y = ya; y < yb; y++)
                for (int z = za; z < zb; z++)
                for (int x = xa; x < xb; x++)
                {
                    byte c = grid[(y * nz + z) * nx + x];
                    if (c == StructurePalette.Empty || c >= StructurePalette.Count) continue;
                    counts[c]++;
                    solid++;
                }
                result = StructurePalette.Empty;
                if (solid >= need)
                {
                    int best = 0;
                    for (int c = 1; c < counts.Length; c++)
                        if (counts[c] > best) { best = counts[c]; result = (byte)c; }
                }
            }
            small[(j * mz + k) * mx + i] = result;
        }
    }

    /// <summary>How much of a merged voxel must be drawn for it to be (see <see cref="Shrink"/>), in sixteenths.</summary>
    public const int KeepSixteenths = 4;

    /// <summary>Smallest region (blocks on a side) <see cref="Region"/> splits a chunk into.</summary>
    private const int MinRegion = 4;

    /// <summary>
    /// Meshes the detailed blocks in the cube of <paramref name="size"/> blocks at (rx, ry, rz) of the chunk:
    /// as one box around them, or, where they are few and spread out (trees dotted over a chunk), as the
    /// eight halves of the cube in turn, so the fine grid stays close to the blocks it draws.
    /// </summary>
    private static void Region(ReadOnlySpan<BlockType> padded, int chunkX, int chunkY, int chunkZ,
        int rx, int ry, int rz, int size, List<FineQuad> output)
    {
        int x0 = S, y0 = S, z0 = S, x1 = -1, y1 = -1, z1 = -1, count = 0;
        for (int y = ry; y < ry + size; y++)
        for (int z = rz; z < rz + size; z++)
        for (int x = rx; x < rx + size; x++)
        {
            if (!StructureDetail.IsDetailed(padded[GreedyMesher.Index(x, y, z)])) continue;
            count++;
            x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); z0 = Math.Min(z0, z);
            x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); z1 = Math.Max(z1, z);
        }
        if (count == 0) return;
        long box = (long)(x1 - x0 + 3) * (y1 - y0 + 3) * (z1 - z0 + 3);
        if (size > MinRegion && box > 4L * count + 64)
        {
            int half = size / 2;
            for (int oy = 0; oy < 2; oy++)
            for (int oz = 0; oz < 2; oz++)
            for (int ox = 0; ox < 2; ox++)
                Region(padded, chunkX, chunkY, chunkZ, rx + ox * half, ry + oy * half, rz + oz * half, half, output);
            return;
        }
        MeshBox(padded, chunkX, chunkY, chunkZ, x0, y0, z0, x1, y1, z1, output);
    }

    /// <summary>
    /// Meshes the detailed blocks in blocks [x0, x1] x [y0, y1] x [z0, z1] of the chunk, on a grid one fine
    /// voxel bigger all round, so faces against what is next to them are checked too. Detailed blocks of the
    /// chunk around the box (meshed with another part of it) hide the faces against them without being
    /// drawn here.
    /// </summary>
    private static void MeshBox(ReadOnlySpan<BlockType> padded, int chunkX, int chunkY, int chunkZ,
        int x0, int y0, int z0, int x1, int y1, int z1, List<FineQuad> output)
    {
        // The grid starts a voxel before block x0 (and y0, z0) and ends a voxel after block x1.
        int nx = (x1 - x0 + 1) * F + 2, ny = (y1 - y0 + 1) * F + 2, nz = (z1 - z0 + 1) * F + 2;
        int volume = nx * ny * nz;
        var grid = _grid;
        if (grid == null || grid.Length < volume) grid = _grid = new byte[Math.Max(volume, 64 * 64 * 64)];
        for (int by = y0 - 1; by <= y1 + 1; by++)
        for (int bz = z0 - 1; bz <= z1 + 1; bz++)
        for (int bx = x0 - 1; bx <= x1 + 1; bx++)
        {
            var b = padded[GreedyMesher.Index(bx, by, bz)];
            bool inChunk = bx >= 0 && by >= 0 && bz >= 0 && bx < S && by < S && bz < S;
            bool inBox = bx >= x0 && bx <= x1 && by >= y0 && by <= y1 && bz >= z0 && bz <= z1;
            // Where the block's voxels fall in the grid, clipped to it.
            int ox = (bx - x0) * F + 1, oy = (by - y0) * F + 1, oz = (bz - z0) * F + 1;
            int xs = Math.Max(0, -ox), xe = Math.Min(F, nx - ox);
            int ys = Math.Max(0, -oy), ye = Math.Min(F, ny - oy);
            int zs = Math.Max(0, -oz), ze = Math.Min(F, nz - oz);
            if (inChunk && StructureDetail.IsDetailed(b))
            {
                var shape = Shape(padded, chunkX, chunkY, chunkZ, bx, by, bz);
                for (int y = ys; y < ye; y++)
                for (int z = zs; z < ze; z++)
                {
                    var from = shape.Slice(StructureDetail.VoxelIndex(xs, y, z), xe - xs);
                    var to = grid.AsSpan(((oy + y) * nz + oz + z) * nx + ox + xs, xe - xs);
                    if (inBox)
                    {
                        from.CopyTo(to);
                        continue;
                    }
                    for (int k = 0; k < from.Length; k++)
                        to[k] = from[k] == StructurePalette.Empty ? StructurePalette.Empty : StructurePalette.Occluder;
                }
            }
            else
            {
                // Solid terrain hides the faces that touch it; air, water and the next chunk's buildings do not.
                byte fill = BlockInfo.IsSolid(b) && !StructureDetail.IsDetailed(b) ? StructurePalette.Occluder : StructurePalette.Empty;
                for (int y = ys; y < ye; y++)
                for (int z = zs; z < ze; z++)
                    grid.AsSpan(((oy + y) * nz + oz + z) * nx + ox + xs, xe - xs).Fill(fill);
            }
        }

        int step = _step;
        if (step <= 1)
        {
            Mesh(grid.AsSpan(0, volume), nx, ny, nz, x0 * F - 1, y0 * F - 1, z0 * F - 1, output, _ao);
            return;
        }
        // Drawn simply: merged voxels, meshed the same way, and the quads put back in fine voxels.
        int mx = (nx - 2) / step + 2, my = (ny - 2) / step + 2, mz = (nz - 2) / step + 2;
        var small = _small;
        if (small == null || small.Length < mx * my * mz) small = _small = new byte[Math.Max(mx * my * mz, 34 * 34 * 34)];
        Shrink(grid.AsSpan(0, volume), nx, ny, nz, small, step);
        int first = output.Count;
        Mesh(small.AsSpan(0, mx * my * mz), mx, my, mz, x0 * F / step - 1, y0 * F / step - 1, z0 * F / step - 1, output, _ao);
        for (int q = first; q < output.Count; q++)
        {
            var quad = output[q];
            output[q] = quad with { X = quad.X * step, Y = quad.Y * step, Z = quad.Z * step, W = quad.W * step, H = quad.H * step };
        }
    }

    /// <summary>The fine voxels of detailed block (bx, by, bz) of the chunk being built, worked out the first time asked.</summary>
    private static ReadOnlySpan<byte> Shape(ReadOnlySpan<BlockType> padded, int chunkX, int chunkY, int chunkZ, int bx, int by, int bz)
    {
        int at = (by * S + bz) * S + bx;
        var shape = _shapes.AsSpan(at * StructureDetail.Volume, StructureDetail.Volume);
        if (_shaped![at]) return shape;
        var near = _near ??= new BlockType[27];
        for (int dy = -1; dy <= 1; dy++)
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
            near[StructureDetail.NeighbourIndex(dx, dy, dz)] = padded[GreedyMesher.Index(bx + dx, by + dy, bz + dz)];
        StructureDetail.Fill(near, chunkX * S + bx, chunkY * S + by, chunkZ * S + bz, shape);
        _shaped[at] = true;
        return shape;
    }

    /// <summary>
    /// Greedy-meshes a grid of fine voxels (indexed <c>(y * nz + z) * nx + x</c>): faces of coloured voxels
    /// that touch empty space (or the edge of the grid) are merged into rectangles of one colour. Quads are
    /// offset by (ox, oy, oz). With <paramref name="ao"/> they carry ambient occlusion (one level per face) from the
    /// voxels around them (see <see cref="Occlusion"/>), and only faces shaded alike merge.
    /// </summary>
    public static void Mesh(ReadOnlySpan<byte> grid, int nx, int ny, int nz, int ox, int oy, int oz, List<FineQuad> output,
        bool ao = false)
    {
        Span<int> dims = stackalloc int[] { nx, ny, nz };
        Span<int> strides = stackalloc int[] { 1, nx * nz, nx };
        Span<int> pos = stackalloc int[3];
        int maxArea = Math.Max(nx * ny, Math.Max(ny * nz, nx * nz));
        byte[] mask = new byte[maxArea];
        byte[] aoMask = new byte[maxArea];
        // Slices along each axis holding anything to draw: the rest are skipped (trees leave a lot of air).
        var drawn = new bool[nx + ny + nz];
        for (int y = 0; y < ny; y++)
        for (int z = 0; z < nz; z++)
        {
            var row = grid.Slice((y * nz + z) * nx, nx);
            for (int x = 0; x < nx; x++)
            {
                byte c = row[x];
                if (c == StructurePalette.Empty || c == StructurePalette.Occluder) continue;
                drawn[x] = true;
                drawn[nx + y] = true;
                drawn[nx + ny + z] = true;
            }
        }
        for (int face = 0; face < 6; face++)
        {
            int a = face >> 1, u = (a + 1) % 3, v = (a + 2) % 3;
            bool positive = (face & 1) == 0;
            int du = dims[u], dv = dims[v], sa = strides[a], su = strides[u], sv = strides[v];
            int first = a == 0 ? 0 : a == 1 ? nx : nx + ny;
            for (int slice = 0; slice < dims[a]; slice++)
            {
                if (!drawn[first + slice]) continue;
                int next = slice + (positive ? 1 : -1);
                bool hasNext = next >= 0 && next < dims[a];
                int nextOffset = (positive ? 1 : -1) * sa;
                bool any = false;
                for (int j = 0; j < dv; j++)
                {
                    int at = slice * sa + j * sv;
                    for (int i = 0; i < du; i++, at += su)
                    {
                        byte c = grid[at];
                        byte m = 0;
                        if (c != StructurePalette.Empty && c != StructurePalette.Occluder
                            && (!hasNext || grid[at + nextOffset] == StructurePalette.Empty))
                        {
                            m = c;
                            any = true;
                        }
                        mask[j * du + i] = m;
                        aoMask[j * du + i] = m != 0 && ao && hasNext
                            ? FaceAo(grid, at + nextOffset, i, j, du, dv, su, sv) : Occlusion.Open;
                    }
                }
                if (!any) continue;

                for (int j = 0; j < dv; j++)
                for (int i = 0; i < du;)
                {
                    byte m = mask[j * du + i];
                    if (m == 0) { i++; continue; }
                    byte o = aoMask[j * du + i];
                    bool merge = Occlusion.Uniform(o);
                    int w = 1;
                    while (merge && i + w < du && mask[j * du + i + w] == m && aoMask[j * du + i + w] == o) w++;
                    int h = 1;
                    while (merge && j + h < dv && RowMatches(mask, aoMask, (j + h) * du + i, w, m, o)) h++;
                    for (int y = 0; y < h; y++) mask.AsSpan((j + y) * du + i, w).Clear();

                    pos[a] = slice + (positive ? 1 : 0);
                    pos[u] = i;
                    pos[v] = j;
                    output.Add(new FineQuad((byte)face, m, pos[0] + ox, pos[1] + oy, pos[2] + oz, w, h, o));
                    i += w;
                }
            }
        }
    }

    private static bool RowMatches(ReadOnlySpan<byte> mask, ReadOnlySpan<byte> aoMask, int start, int w, byte m, byte o)
    {
        for (int k = 0; k < w; k++) if (mask[start + k] != m || aoMask[start + k] != o) return false;
        return true;
    }

    /// <summary>
    /// The openness of the face at (i, j) of its slice whose front voxel is grid index <paramref name="front"/>,
    /// one level for the whole face: shaded where it lies in a nook, with solid voxels on two sides of its front
    /// or on one side and both corners there (beyond the grid's edge counts as open). Shading a fine face as a
    /// whole, and only in nooks, keeps most faces of a flat run alike so they still merge: corner by corner
    /// shading tripled the triangles of a forest, this adds about half.
    /// </summary>
    private static byte FaceAo(ReadOnlySpan<byte> grid, int front, int i, int j, int du, int dv, int su, int sv)
    {
        const byte E = StructurePalette.Empty;
        bool hasU0 = i > 0, hasU1 = i + 1 < du, hasV0 = j > 0, hasV1 = j + 1 < dv;
        int sides = (hasU0 && grid[front - su] != E ? 1 : 0) + (hasU1 && grid[front + su] != E ? 1 : 0)
            + (hasV0 && grid[front - sv] != E ? 1 : 0) + (hasV1 && grid[front + sv] != E ? 1 : 0);
        int corners = (hasU0 && hasV0 && grid[front - su - sv] != E ? 1 : 0) + (hasU1 && hasV0 && grid[front + su - sv] != E ? 1 : 0)
            + (hasU0 && hasV1 && grid[front - su + sv] != E ? 1 : 0) + (hasU1 && hasV1 && grid[front + su + sv] != E ? 1 : 0);
        int shade = 2 * sides + corners; // 0 open .. 12 at the bottom of a pit
        int level = shade < 5 ? 3 : 1;
        return (byte)(level * 0x55);
    }
}
