using System;
using System.IO;
using System.IO.Compression;
using Godot;

namespace Rts.Game.Detail;

/// <summary>
/// Models made outside the game (Meshy AI, with textures), turned into voxels offline by
/// <c>scripts/import-model.py</c> and saved gzipped in <c>game/models/*.vox.gz</c>: "VOXC", three int32 sizes in
/// sculpt units (a thirty-second of a block), an int32 colour count and three bytes per colour, then one byte per
/// voxel (0 empty, else a colour), x varying fastest, then y, then z. The colours come from the model's own
/// texture, so an imported model is shown as made, with the same levels of detail as the sculpted ones.
/// </summary>
public static partial class Sculpted
{
    private sealed class ImportedGrid
    {
        public int X, Y, Z, Lowest;
        public FineMaterial[] Palette = Array.Empty<FineMaterial>();
        public byte[] Cells = Array.Empty<byte>();
        public byte At(int x, int y, int z) =>
            x < 0 || y < 0 || z < 0 || x >= X || y >= Y || z >= Z ? (byte)0 : Cells[x + X * (y + Y * z)];
    }

    private static ImportedGrid? ReadImported(string path)
    {
        var bytes = Godot.FileAccess.GetFileAsBytes(path);
        if (bytes.Length == 0)
        {
            GD.PrintErr($"Imported model {path} could not be read");
            return null;
        }
        using var gz = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
        using var all = new MemoryStream();
        gz.CopyTo(all);
        var data = all.ToArray();
        if (data.Length < 20 || data[0] != 'V' || data[1] != 'O' || data[2] != 'X' || data[3] != 'C')
        {
            GD.PrintErr($"Imported model {path} is not a VOXC file");
            return null;
        }
        var g = new ImportedGrid { X = BitConverter.ToInt32(data, 4), Y = BitConverter.ToInt32(data, 8), Z = BitConverter.ToInt32(data, 12) };
        int n = BitConverter.ToInt32(data, 16);
        g.Palette = new FineMaterial[n + 1];
        for (int k = 0; k < n; k++)
            g.Palette[k + 1] = new FineMaterial(Color.Color8(data[20 + 3 * k], data[21 + 3 * k], data[22 + 3 * k]));
        g.Cells = data.AsSpan(20 + 3 * n, g.X * g.Y * g.Z).ToArray();
        g.Lowest = g.Y;
        for (int i = 0; i < g.Cells.Length; i++)
            if (g.Cells[i] != 0) g.Lowest = Math.Min(g.Lowest, i / g.X % g.Y);
        return g;
    }

    /// <summary>
    /// An imported model as one part (it stands still and breathes), at a step, from <paramref name="path"/>.
    /// <paramref name="glows"/> are boxes in the file's own voxels (x0, y0, z0, x1, y1, z1, inclusive) painted
    /// with a glowing material, for eyes and the like the model's texture can only give as a colour.
    /// </summary>
    private static FineGrid[] Imported(string path, int step, (int X0, int Y0, int Z0, int X1, int Y1, int Z1, FineMaterial M)[]? glows = null)
    {
        var g = ReadImported(path);
        if (g == null) return new[] { new Sculpt(step, 0, 0, 0, 1, 1, 1).Grid };
        if (glows != null && glows.Length > 0)
        {
            int first = g.Palette.Length;
            var palette = new FineMaterial[first + glows.Length];
            Array.Copy(g.Palette, palette, first);
            for (int k = 0; k < glows.Length; k++)
            {
                var b = glows[k];
                palette[first + k] = b.M;
                for (int z = b.Z0; z <= b.Z1; z++)
                for (int y = b.Y0; y <= b.Y1; y++)
                for (int x = b.X0; x <= b.X1; x++)
                    if (x >= 0 && y >= 0 && z >= 0 && x < g.X && y < g.Y && z < g.Z)
                        g.Cells[x + g.X * (y + g.Y * z)] = (byte)(first + k);
            }
            g.Palette = palette;
        }
        float hx = g.X / 2f, hz = g.Z / 2f;
        var grid = new Sculpt(step, -hx, 0, -hz, hx, g.Y - g.Lowest, hz).Grid;
        var codes = new byte[g.Palette.Length];
        for (int k = 1; k < codes.Length; k++) codes[k] = grid.Of(g.Palette[k]);
        // Each voxel takes the colour at its centre, as a shape would fill it.
        for (int y = grid.Y0; y < grid.Y0 + grid.NY; y++)
        for (int z = grid.Z0; z < grid.Z0 + grid.NZ; z++)
        for (int x = grid.X0; x < grid.X0 + grid.NX; x++)
        {
            int lx = (int)MathF.Floor((x + 0.5f) * step + hx), ly = (int)MathF.Floor((y + 0.5f) * step) + g.Lowest;
            int lz = (int)MathF.Floor((z + 0.5f) * step + hz);
            byte c = g.At(lx, ly, lz);
            if (c != 0 && c < codes.Length) grid.Set(x, y, z, codes[c]);
        }
        return new[] { grid };
    }

    private static RigDef ImportedRig(string path, float stride = 1.2f, float bob = 1f,
        params (int, int, int, int, int, int, FineMaterial)[] glows) => new()
    {
        Roles = new[] { Role.Body }, Pivots = new[] { Vector3.Zero }, Build = step => Imported(path, step, glows),
        StrideBlocks = stride, Bob = bob, Far = true,
    };
}
