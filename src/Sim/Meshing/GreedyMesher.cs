using System;
using System.Collections.Generic;

namespace Rts.Sim.Meshing;

/// <summary>
/// One rectangle of merged block faces. Coordinates are chunk-local block units.
/// Face numbering: 0 +X, 1 -X, 2 +Y, 3 -Y, 4 +Z, 5 -Z.
/// </summary>
public readonly record struct Quad(byte Face, BlockType Block, int X, int Y, int Z, int W, int H, byte Ao = Occlusion.Open)
{
    /// <summary>Axis the face points along (0 X, 1 Y, 2 Z).</summary>
    public int Axis => Face >> 1;
    public bool Positive => (Face & 1) == 0;

    /// <summary>
    /// Corner <paramref name="i"/> (0..3), counter-clockwise when seen from outside the solid.
    /// W runs along axis (Axis+1)%3 and H along (Axis+2)%3.
    /// </summary>
    public (int X, int Y, int Z) Corner(int i)
    {
        // Positive faces: B, B+U, B+U+V, B+V. Negative faces reverse the winding.
        bool du, dv;
        int k = Positive ? i : (4 - i) % 4;
        du = k == 1 || k == 2;
        dv = k == 2 || k == 3;
        Span<int> p = stackalloc int[] { X, Y, Z };
        if (du) p[(Axis + 1) % 3] += W;
        if (dv) p[(Axis + 2) % 3] += H;
        return (p[0], p[1], p[2]);
    }

    /// <summary>How open corner <paramref name="i"/> is (0 tucked into a corner .. 3 open; see <see cref="Occlusion"/>).</summary>
    public int AoAt(int i) => Occlusion.At(Ao, Positive, i);
}

/// <summary>
/// Ambient occlusion baked into the meshes: how much of the sky each corner of a face sees, from the solids
/// next to it in the layer in front of the face. The four corners of a face are packed in a byte, two bits
/// each (index <c>du + 2 * dv</c> along the face's W and H axes), 3 meaning open and 0 tucked into a corner.
/// Faces only merge into one quad where the shading is the same all over them, so the shading of a merged
/// quad is the shading of each face in it.
/// </summary>
public static class Occlusion
{
    /// <summary>All four corners open.</summary>
    public const byte Open = 0xFF;

    /// <summary>The openness of one corner from its two side neighbours and the diagonal one.</summary>
    public static int Level(bool side1, bool side2, bool corner) =>
        side1 && side2 ? 0 : 3 - (side1 ? 1 : 0) - (side2 ? 1 : 0) - (corner ? 1 : 0);

    /// <summary>True if all four corners are shaded alike, so neighbouring faces may merge.</summary>
    public static bool Uniform(byte ao) => ao is 0x00 or 0x55 or 0xAA or 0xFF;

    /// <summary>Corner <paramref name="i"/>'s level, numbered as <see cref="Quad.Corner"/> numbers corners.</summary>
    public static int At(byte ao, bool positive, int i)
    {
        int k = positive ? i : (4 - i) % 4;
        int du = k == 1 || k == 2 ? 1 : 0, dv = k == 2 || k == 3 ? 1 : 0;
        return (ao >> (2 * (du + 2 * dv))) & 3;
    }
}

/// <summary>
/// Greedy mesher: merges coplanar visible faces of the same block type into rectangles.
/// Works on a padded copy of a chunk (see <see cref="VoxelWorld.CopyChunkPadded"/>) so it can
/// run off the main thread. Integer-only and allocation-free apart from the output lists.
/// </summary>
public static class GreedyMesher
{
    private const int P = VoxelWorld.PaddedSize;

    /// <summary>Whether a face of <paramref name="block"/> touching <paramref name="neighbour"/> is drawn.</summary>
    public static bool FaceVisible(BlockType block, BlockType neighbour)
    {
        if (block == BlockType.Air) return false;
        return block == BlockType.Water ? neighbour == BlockType.Air : !BlockInfo.IsSolid(neighbour);
    }

    /// <summary>
    /// Appends quads for one chunk. Water goes to <paramref name="water"/>, everything else to <paramref name="opaque"/>.
    /// With <paramref name="ao"/> the opaque quads carry their corners' ambient occlusion (see <see cref="Occlusion"/>).
    /// </summary>
    public static void Build(ReadOnlySpan<BlockType> padded, List<Quad> opaque, List<Quad> water, bool ao = false) =>
        Build(padded, VoxelWorld.ChunkSize, opaque, water, ao);

    /// <summary>
    /// As <see cref="Build(ReadOnlySpan{BlockType}, List{Quad}, List{Quad}, bool)"/> for a cube of
    /// <paramref name="size"/> blocks a side, padded by one block all round and indexed as
    /// <see cref="Index(int, int, int, int)"/>: the half-size blocks of nearby ground are meshed 32 to a chunk.
    /// </summary>
    public static void Build(ReadOnlySpan<BlockType> padded, int size, List<Quad> opaque, List<Quad> water, bool ao = false)
    {
        int S = size, P = size + 2;
        if (S <= 0 || S > 64) throw new ArgumentOutOfRangeException(nameof(size));
        if (padded.Length < P * P * P) throw new ArgumentException("Padded chunk is too small", nameof(padded));
        Span<BlockType> mask = stackalloc BlockType[S * S];
        Span<byte> aoMask = stackalloc byte[S * S];
        Span<int> strides = stackalloc int[] { 1, P * P, P };
        Span<int> pos = stackalloc int[3];
        Span<int> step = stackalloc int[3];

        for (int face = 0; face < 6; face++)
        {
            int a = face >> 1, u = (a + 1) % 3, v = (a + 2) % 3;
            bool positive = (face & 1) == 0;
            step.Clear();
            step[a] = positive ? 1 : -1;
            int neighbourOffset = (step[1] * P + step[2]) * P + step[0];
            int su = strides[u], sv = strides[v];

            for (int slice = 0; slice < S; slice++)
            {
                pos[a] = slice;
                bool any = false;
                for (int j = 0; j < S; j++)
                {
                    pos[v] = j;
                    for (int i = 0; i < S; i++)
                    {
                        pos[u] = i;
                        int idx = ((pos[1] + 1) * P + (pos[2] + 1)) * P + (pos[0] + 1);
                        var b = padded[idx];
                        bool visible = FaceVisible(b, padded[idx + neighbourOffset]);
                        mask[j * S + i] = visible ? b : BlockType.Air;
                        aoMask[j * S + i] = visible && ao && b != BlockType.Water
                            ? CornerAo(padded, idx + neighbourOffset, su, sv) : Occlusion.Open;
                        any |= visible;
                    }
                }
                if (!any) continue;

                for (int j = 0; j < S; j++)
                for (int i = 0; i < S;)
                {
                    var m = mask[j * S + i];
                    if (m == BlockType.Air) { i++; continue; }
                    byte o = aoMask[j * S + i];
                    bool merge = Occlusion.Uniform(o);
                    int w = 1;
                    while (merge && i + w < S && mask[j * S + i + w] == m && aoMask[j * S + i + w] == o) w++;
                    int h = 1;
                    while (merge && j + h < S && RowMatches(mask, aoMask, (j + h) * S + i, w, m, o)) h++;
                    for (int y = 0; y < h; y++) mask.Slice((j + y) * S + i, w).Fill(BlockType.Air);

                    pos[a] = slice + (positive ? 1 : 0);
                    pos[u] = i;
                    pos[v] = j;
                    var quad = new Quad((byte)face, m, pos[0], pos[1], pos[2], w, h, o);
                    (m == BlockType.Water ? water : opaque).Add(quad);
                    pos[a] = slice;
                    i += w;
                }
            }
        }
    }

    private static bool RowMatches(ReadOnlySpan<BlockType> mask, ReadOnlySpan<byte> aoMask, int start, int w, BlockType m, byte o)
    {
        for (int k = 0; k < w; k++) if (mask[start + k] != m || aoMask[start + k] != o) return false;
        return true;
    }

    /// <summary>The corners' openness of the face whose front cell is padded index <paramref name="front"/>.</summary>
    private static byte CornerAo(ReadOnlySpan<BlockType> padded, int front, int su, int sv)
    {
        bool u0 = BlockInfo.IsSolid(padded[front - su]), u1 = BlockInfo.IsSolid(padded[front + su]);
        bool v0 = BlockInfo.IsSolid(padded[front - sv]), v1 = BlockInfo.IsSolid(padded[front + sv]);
        int a00 = Occlusion.Level(u0, v0, BlockInfo.IsSolid(padded[front - su - sv]));
        int a10 = Occlusion.Level(u1, v0, BlockInfo.IsSolid(padded[front + su - sv]));
        int a01 = Occlusion.Level(u0, v1, BlockInfo.IsSolid(padded[front - su + sv]));
        int a11 = Occlusion.Level(u1, v1, BlockInfo.IsSolid(padded[front + su + sv]));
        return (byte)(a00 | a10 << 2 | a01 << 4 | a11 << 6);
    }

    /// <summary>Index into the padded array for chunk-local coordinates (-1..16).</summary>
    public static int Index(int x, int y, int z) => ((y + 1) * P + (z + 1)) * P + (x + 1);

    /// <summary>Index into the padded array of a cube <paramref name="size"/> blocks a side, for local coordinates (-1..size).</summary>
    public static int Index(int x, int y, int z, int size) => ((y + 1) * (size + 2) + (z + 1)) * (size + 2) + (x + 1);
}
