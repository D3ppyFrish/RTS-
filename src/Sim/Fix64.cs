using System;
using System.Globalization;
using System.Numerics;

namespace Rts.Sim;

/// <summary>
/// Q32.32 fixed-point number. All simulation maths uses this instead of float/double
/// so that every machine computes bit-identical results (required for lockstep multiplayer).
/// </summary>
public readonly struct Fix64 : IEquatable<Fix64>, IComparable<Fix64>
{
    public const int FractionBits = 32;
    public readonly long Raw;

    public static readonly Fix64 Zero = new(0L);
    public static readonly Fix64 One = new(1L << FractionBits);
    public static readonly Fix64 Half = new(1L << (FractionBits - 1));
    public static readonly Fix64 MaxValue = new(long.MaxValue);

    private Fix64(long raw) => Raw = raw;

    public static Fix64 FromRaw(long raw) => new(raw);
    public static Fix64 FromInt(int value) => new((long)value << FractionBits);

    /// <summary>Builds a value from a ratio, e.g. FromRatio(1, 4) == 0.25. Deterministic.</summary>
    public static Fix64 FromRatio(int numerator, int denominator)
    {
        if (denominator == 0) throw new DivideByZeroException();
        return new((long)(((Int128)numerator << FractionBits) / denominator));
    }

    /// <summary>
    /// Parses a decimal such as "1.25" or "-0.5" with integer maths only (no floating point),
    /// so data files load identically everywhere. At most 9 fractional digits are used.
    /// </summary>
    public static Fix64 Parse(string text)
    {
        if (!TryParse(text, out var v)) throw new FormatException($"'{text}' is not a decimal number");
        return v;
    }

    public static bool TryParse(string? text, out Fix64 value)
    {
        value = Zero;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();
        bool negative = text[0] == '-';
        if (negative || text[0] == '+') text = text[1..];
        int dot = text.IndexOf('.');
        string whole = dot < 0 ? text : text[..dot];
        string frac = dot < 0 ? "" : text[(dot + 1)..];
        if (whole.Length == 0 && frac.Length == 0) return false;
        if (whole.Length == 0) whole = "0";
        if (!long.TryParse(whole, NumberStyles.None, CultureInfo.InvariantCulture, out long w) || w > int.MaxValue) return false;
        if (frac.Length > 9) frac = frac[..9];
        long f = 0, scale = 1;
        foreach (char c in frac)
        {
            if (c < '0' || c > '9') return false;
            f = f * 10 + (c - '0');
            scale *= 10;
        }
        long raw = (w << FractionBits) + (long)(((Int128)f << FractionBits) / scale);
        value = new(negative ? -raw : raw);
        return true;
    }

    public int FloorToInt() => (int)(Raw >> FractionBits);
    public int RoundToInt() => (int)((Raw + (1L << (FractionBits - 1))) >> FractionBits);
    public Fix64 Floor() => new(Raw & ~((1L << FractionBits) - 1));
    public Fix64 Fraction() => new(Raw & ((1L << FractionBits) - 1));

    /// <summary>For rendering only. Never feed the result back into the simulation.</summary>
    public float ToFloat() => Raw / (float)(1L << FractionBits);

    public static Fix64 Abs(Fix64 a) => a.Raw < 0 ? new(-a.Raw) : a;
    public static Fix64 Min(Fix64 a, Fix64 b) => a.Raw <= b.Raw ? a : b;
    public static Fix64 Max(Fix64 a, Fix64 b) => a.Raw >= b.Raw ? a : b;
    public static Fix64 Clamp(Fix64 v, Fix64 lo, Fix64 hi) => v < lo ? lo : v > hi ? hi : v;

    /// <summary>Square root with integer maths only. Negative input returns zero. Exact: floor of the true root.</summary>
    public static Fix64 Sqrt(Fix64 a)
    {
        if (a.Raw <= 0) return Zero;
        // sqrt(raw / 2^32) * 2^32 == sqrt(raw * 2^32): the integer root of the 96-bit value hi:lo.
        ulong raw = (ulong)a.Raw;
        ulong hi = raw >> FractionBits, lo = raw << FractionBits;

        // Seed from a table of the top 10 bits (shifted by an even amount), within 0.4% above the root.
        int bits = hi == 0 ? 64 - BitOperations.LeadingZeroCount(lo) : 128 - BitOperations.LeadingZeroCount(hi);
        int e = (bits - 9) & ~1;
        int top = (int)(e >= 64 ? hi >> (e - 64) : hi == 0 ? lo >> e : (hi << (64 - e)) | (lo >> e));
        ulong x = (ulong)SqrtSeed[top] << (e / 2 - 5);

        // Newton's method from above decreases strictly until it reaches floor(root), the first value whose
        // square fits. While hi != 0 the root is at least 2^32 > hi, which DivideWide needs.
        while (true)
        {
            ulong sqHi = Math.BigMul(x, x, out ulong sqLo);
            if (sqHi < hi || (sqHi == hi && sqLo <= lo)) return new((long)x);
            ulong q = hi == 0 ? lo / x : DivideWide(hi, lo, x);
            x = (x + q) >> 1;
        }
    }

    /// <summary>SqrtSeed[t] = ceil(32 * sqrt(t + 1)), so seed &lt;&lt; (e / 2 - 5) is at or above sqrt(v) when v &gt;&gt; e == t.</summary>
    private static readonly ushort[] SqrtSeed = BuildSqrtSeed();

    private static ushort[] BuildSqrtSeed()
    {
        var table = new ushort[1024];
        for (int t = 0; t < table.Length; t++)
        {
            int n = (t + 1) * 1024, s = 0;
            while (s * s < n) s++;
            table[t] = (ushort)s;
        }
        return table;
    }

    /// <summary>
    /// Divides the 128-bit value hi:lo by v. Requires hi &lt; v, so the quotient fits in 64 bits.
    /// Two 64-by-32-bit steps (Knuth's algorithm D as in Hacker's Delight "divlu"), much faster than Int128.
    /// </summary>
    private static ulong DivideWide(ulong hi, ulong lo, ulong v)
    {
        const ulong b = 1UL << 32;
        int s = BitOperations.LeadingZeroCount(v);
        v <<= s;
        ulong vn1 = v >> 32, vn0 = v & 0xFFFFFFFF;
        ulong un32 = s == 0 ? hi : (hi << s) | (lo >> (64 - s));
        ulong un10 = lo << s;
        ulong un1 = un10 >> 32, un0 = un10 & 0xFFFFFFFF;

        ulong q1 = un32 / vn1, rhat = un32 - q1 * vn1;
        while (q1 >= b || q1 * vn0 > (rhat << 32) + un1)
        {
            q1--;
            rhat += vn1;
            if (rhat >= b) break;
        }
        ulong un21 = (un32 << 32) + un1 - q1 * v; // wraps, but the true value is below v
        ulong q0 = un21 / vn1;
        rhat = un21 - q0 * vn1;
        while (q0 >= b || q0 * vn0 > (rhat << 32) + un0)
        {
            q0--;
            rhat += vn1;
            if (rhat >= b) break;
        }
        return (q1 << 32) + q0;
    }

    public static Fix64 operator +(Fix64 a, Fix64 b) => new(a.Raw + b.Raw);
    public static Fix64 operator -(Fix64 a, Fix64 b) => new(a.Raw - b.Raw);
    public static Fix64 operator -(Fix64 a) => new(-a.Raw);
    public static Fix64 operator *(Fix64 a, Fix64 b)
    {
        // Same bits as ((Int128)a * b) >> 32, using the hardware 64x64->128 multiply.
        long hi = Math.BigMul(a.Raw, b.Raw, out long lo);
        return new((hi << FractionBits) | (long)((ulong)lo >> FractionBits));
    }

    public static Fix64 operator *(Fix64 a, int b) => new(a.Raw * b);
    public static Fix64 operator /(Fix64 a, int b) => new(a.Raw / b);
    public static Fix64 operator /(Fix64 a, Fix64 b)
    {
        long n = a.Raw, d = b.Raw;
        if (d == 0) throw new DivideByZeroException();
        // Same result as ((Int128)n << 32) / d (truncating toward zero), without Int128 division.
        ulong un = n < 0 ? (ulong)-n : (ulong)n; // long.MinValue wraps to its true magnitude 2^63
        ulong ud = d < 0 ? (ulong)-d : (ulong)d;
        ulong hi = un >> FractionBits, lo = un << FractionBits;
        ulong q;
        if (hi == 0) q = lo / ud;
        else if (hi < ud) q = DivideWide(hi, lo, ud);
        else return new((long)(((Int128)n << FractionBits) / d)); // overflow: keep the old wrapped result
        return new((n ^ d) < 0 ? -(long)q : (long)q);
    }

    public static bool operator <(Fix64 a, Fix64 b) => a.Raw < b.Raw;
    public static bool operator >(Fix64 a, Fix64 b) => a.Raw > b.Raw;
    public static bool operator <=(Fix64 a, Fix64 b) => a.Raw <= b.Raw;
    public static bool operator >=(Fix64 a, Fix64 b) => a.Raw >= b.Raw;
    public static bool operator ==(Fix64 a, Fix64 b) => a.Raw == b.Raw;
    public static bool operator !=(Fix64 a, Fix64 b) => a.Raw != b.Raw;

    public bool Equals(Fix64 other) => Raw == other.Raw;
    public override bool Equals(object? obj) => obj is Fix64 f && Equals(f);
    public override int GetHashCode() => Raw.GetHashCode();
    public int CompareTo(Fix64 other) => Raw.CompareTo(other.Raw);

    /// <summary>Exact decimal text (up to 9 places), round-trips through <see cref="Parse"/> to within 1e-9.</summary>
    public override string ToString()
    {
        long raw = Raw;
        bool neg = raw < 0;
        UInt128 abs = neg ? (UInt128)(-(Int128)raw) : (UInt128)raw;
        UInt128 whole = abs >> FractionBits;
        UInt128 frac = abs & ((1UL << FractionBits) - 1);
        UInt128 dec = (frac * 1_000_000_000 + (1UL << (FractionBits - 1))) >> FractionBits;
        if (dec == 1_000_000_000) { whole++; dec = 0; }
        string s = whole.ToString(CultureInfo.InvariantCulture);
        if (dec != 0) s += "." + dec.ToString(CultureInfo.InvariantCulture).PadLeft(9, '0').TrimEnd('0');
        return neg && (whole != 0 || dec != 0) ? "-" + s : s;
    }
}
