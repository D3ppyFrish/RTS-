using System;
using System.Collections.Generic;

namespace Rts.Sim.Meshing;

/// <summary>
/// One rectangle of merged block faces. Coordinates are chunk-local block units.
/// Face numbering: 0 +X, 1 -X, 2 +Y, 3 -Y, 4 +Z, 5 -Z.
/// </summary>
public readonly record struct Quad(byte Face, BlockType Block, int X, int Y, int Z, int W, int H)
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
}

/// <summary>
/// Greedy mesher: merges coplanar visible faces of the same block type into rectangles.
/// Works on a padded copy of a chunk (see <see cref="VoxelWorld.CopyChunkPadded"/>) so it can
/// run off the main thread. Integer-only and allocation-free apart from the output lists.
/// </summary>
public static class GreedyMesher
{
    private const int S = VoxelWorld.ChunkSize;
    private const int P = VoxelWorld.PaddedSize;

    /// <summary>Whether a face of <paramref name="block"/> touching <paramref name="neighbour"/> is drawn.</summary>
    public static bool FaceVisible(BlockType block, BlockType neighbour)
    {
        if (block == BlockType.Air) return false;
        return block == BlockType.Water ? neighbour == BlockType.Air : !BlockInfo.IsSolid(neighbour);
    }

    /// <summary>Appends quads for one chunk. Water goes to <paramref name="water"/>, everything else to <paramref name="opaque"/>.</summary>
    public static void Build(ReadOnlySpan<BlockType> padded, List<Quad> opaque, List<Quad> water)
    {
        if (padded.Length < P * P * P) throw new ArgumentException("Padded chunk is too small", nameof(padded));
        Span<BlockType> mask = stackalloc BlockType[S * S];
        Span<int> pos = stackalloc int[3];
        Span<int> step = stackalloc int[3];

        for (int face = 0; face < 6; face++)
        {
            int a = face >> 1, u = (a + 1) % 3, v = (a + 2) % 3;
            bool positive = (face & 1) == 0;
            step.Clear();
            step[a] = positive ? 1 : -1;
            int neighbourOffset = Index(step[0], step[1], step[2]) - Index(0, 0, 0);

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
                        int idx = Index(pos[0], pos[1], pos[2]);
                        var b = padded[idx];
                        bool visible = FaceVisible(b, padded[idx + neighbourOffset]);
                        mask[j * S + i] = visible ? b : BlockType.Air;
                        any |= visible;
                    }
                }
                if (!any) continue;

                for (int j = 0; j < S; j++)
                for (int i = 0; i < S;)
                {
                    var m = mask[j * S + i];
                    if (m == BlockType.Air) { i++; continue; }
                    int w = 1;
                    while (i + w < S && mask[j * S + i + w] == m) w++;
                    int h = 1;
                    while (j + h < S && RowMatches(mask, (j + h) * S + i, w, m)) h++;
                    for (int y = 0; y < h; y++) mask.Slice((j + y) * S + i, w).Fill(BlockType.Air);

                    pos[a] = slice + (positive ? 1 : 0);
                    pos[u] = i;
                    pos[v] = j;
                    var quad = new Quad((byte)face, m, pos[0], pos[1], pos[2], w, h);
                    (m == BlockType.Water ? water : opaque).Add(quad);
                    pos[a] = slice;
                    i += w;
                }
            }
        }
    }

    private static bool RowMatches(ReadOnlySpan<BlockType> mask, int start, int w, BlockType m)
    {
        for (int k = 0; k < w; k++) if (mask[start + k] != m) return false;
        return true;
    }

    /// <summary>Index into the padded array for chunk-local coordinates (-1..16).</summary>
    public static int Index(int x, int y, int z) => ((y + 1) * P + (z + 1)) * P + (x + 1);
}
