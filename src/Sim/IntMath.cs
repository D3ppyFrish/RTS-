namespace Rts.Sim;

/// <summary>Whole-number helpers for the simulation, which uses no floating point.</summary>
public static class IntMath
{
    /// <summary>The whole square root of v, rounded down (Newton's method on integers); 0 for v of 0 or less.</summary>
    public static long ISqrt(long v)
    {
        if (v <= 0) return 0;
        long r = v, next = (r + 1) / 2;
        while (next < r)
        {
            r = next;
            next = (r + v / r) / 2;
        }
        return r;
    }

    /// <summary>The straight-line distance between two columns, rounded down.</summary>
    public static int Distance(int x0, int z0, int x1, int z1)
    {
        long dx = x1 - x0, dz = z1 - z0;
        return (int)ISqrt(dx * dx + dz * dz);
    }
}
