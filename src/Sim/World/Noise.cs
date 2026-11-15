namespace Rts.Sim.World;

/// <summary>Integer-only noise helpers. Same results on every machine.</summary>
public static class Noise
{
    /// <summary>Bilinear value noise on a grid of <paramref name="cell"/> blocks. Returns 0..255.</summary>
    public static int Value(int x, int z, int cell, ulong seed)
    {
        if (cell < 1) cell = 1;
        int cx = FloorDiv(x, cell), cz = FloorDiv(z, cell);
        int fx = x - cx * cell, fz = z - cz * cell;
        int a = (int)(DeterministicRandom.Hash(cx, cz, seed) & 255);
        int b = (int)(DeterministicRandom.Hash(cx + 1, cz, seed) & 255);
        int c = (int)(DeterministicRandom.Hash(cx, cz + 1, seed) & 255);
        int d = (int)(DeterministicRandom.Hash(cx + 1, cz + 1, seed) & 255);
        long top = (long)a * (cell - fx) + (long)b * fx;
        long bottom = (long)c * (cell - fx) + (long)d * fx;
        return (int)((top * (cell - fz) + bottom * fz) / ((long)cell * cell));
    }

    /// <summary>Hash of a 3D point, 0..999. For scattering ore and features.</summary>
    public static int Permille(int x, int y, int z, ulong seed) =>
        (int)(DeterministicRandom.Hash(x * 31 + y, z * 17 - y * 7, seed) % 1000);

    public static int FloorDiv(int a, int b) => a >= 0 ? a / b : (a - b + 1) / b;

    /// <summary>Integer square root (floor).</summary>
    public static int Sqrt(long v)
    {
        if (v <= 0) return 0;
        long x = v, y = (x + 1) / 2;
        while (y < x) { x = y; y = (x + v / x) / 2; }
        return (int)x;
    }
}
