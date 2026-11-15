using System;
using System.Collections.Generic;
using Xunit;

namespace Rts.Sim.Tests;

public class Fix64Tests
{
    [Fact]
    public void BasicArithmetic()
    {
        var a = Fix64.FromInt(3);
        var b = Fix64.FromRatio(1, 2);
        Assert.Equal(Fix64.FromRatio(7, 2), a + b);
        Assert.Equal(Fix64.FromRatio(5, 2), a - b);
        Assert.Equal(Fix64.FromRatio(3, 2), a * b);
        Assert.Equal(Fix64.FromInt(6), a / b);
    }

    [Fact]
    public void NegativeFloor()
    {
        Assert.Equal(-2, Fix64.FromRatio(-3, 2).FloorToInt());
        Assert.Equal(1, Fix64.FromRatio(3, 2).FloorToInt());
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(4, 2)]
    [InlineData(144, 12)]
    [InlineData(1000000, 1000)]
    public void SqrtOfSquares(int v, int root)
    {
        Assert.Equal(Fix64.FromInt(root), Fix64.Sqrt(Fix64.FromInt(v)));
    }

    [Fact]
    public void SqrtIsCloseForNonSquares()
    {
        var r = Fix64.Sqrt(Fix64.FromInt(2));
        Assert.True(Fix64.Abs(r * r - Fix64.FromInt(2)) < Fix64.FromRatio(1, 100000));
        var q = Fix64.Sqrt(Fix64.FromRatio(1, 4));
        Assert.Equal(Fix64.Half, q);
        Assert.Equal(Fix64.Zero, Fix64.Sqrt(Fix64.FromInt(-3)));
    }

    [Theory]
    [InlineData("1.25", 5, 4)]
    [InlineData("-0.5", -1, 2)]
    [InlineData("3", 3, 1)]
    [InlineData(".75", 3, 4)]
    public void ParsesDecimals(string text, int num, int den)
    {
        Assert.Equal(Fix64.FromRatio(num, den), Fix64.Parse(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1.2.3")]
    [InlineData("-")]
    public void RejectsBadDecimals(string text)
    {
        Assert.False(Fix64.TryParse(text, out _));
    }

    [Fact]
    public void ToStringRoundTrips()
    {
        foreach (var v in new[] { Fix64.FromRatio(5, 4), Fix64.FromRatio(-1, 3), Fix64.FromInt(42), Fix64.Zero })
            Assert.True(Fix64.Abs(Fix64.Parse(v.ToString()) - v) <= Fix64.FromRaw(5));
        Assert.Equal("1.25", Fix64.FromRatio(5, 4).ToString());
        Assert.Equal("-0.5", Fix64.FromRatio(-1, 2).ToString());
    }

    /// <summary>Raw values that stress sign handling, carries and the 64-bit boundaries.</summary>
    private static IEnumerable<long> SampleRaws()
    {
        long[] edges =
        {
            0, 1, -1, 2, -2, 0xFFFFFFFFL, -0xFFFFFFFFL, 1L << 32, -(1L << 32), (1L << 32) + 1, (1L << 31) - 1,
            1L << 31, 1L << 40, -(1L << 40), 1L << 62, -(1L << 62), long.MaxValue, long.MinValue, long.MinValue + 1,
            0x7FFF_FFFF_0000_0001L, 0x1234_5678_9ABC_DEF0L, -0x1234_5678_9ABC_DEF0L,
        };
        foreach (var e in edges) yield return e;
        var rng = new DeterministicRandom(99);
        for (int i = 0; i < 20000; i++)
        {
            ulong r = rng.NextULong();
            // Mix of magnitudes: shift by 0..63 bits so small, medium and huge values all appear.
            yield return (long)(r >> (int)(rng.NextULong() % 64));
            yield return -(long)(r >> (int)(rng.NextULong() % 64));
        }
    }

    [Fact]
    public void MultiplyMatchesInt128Reference()
    {
        var raws = new List<long>(SampleRaws());
        for (int i = 0; i + 1 < raws.Count; i++)
        {
            long a = raws[i], b = raws[raws.Count - 1 - i];
            long expected = (long)(((Int128)a * b) >> Fix64.FractionBits);
            Assert.Equal(expected, (Fix64.FromRaw(a) * Fix64.FromRaw(b)).Raw);
        }
    }

    [Fact]
    public void DivideMatchesInt128Reference()
    {
        var raws = new List<long>(SampleRaws());
        for (int i = 0; i + 1 < raws.Count; i++)
        {
            long a = raws[i], b = raws[raws.Count - 1 - i];
            if (b == 0) continue;
            long expected = (long)(((Int128)a << Fix64.FractionBits) / b);
            Assert.Equal(expected, (Fix64.FromRaw(a) / Fix64.FromRaw(b)).Raw);
        }
        Assert.Throws<DivideByZeroException>(() => Fix64.One / Fix64.Zero);
    }

    [Fact]
    public void SqrtIsExactFloorOfRoot()
    {
        foreach (long raw in SampleRaws())
        {
            long root = Fix64.Sqrt(Fix64.FromRaw(raw)).Raw;
            if (raw <= 0) { Assert.Equal(0, root); continue; }
            // floor(sqrt(raw * 2^32)): root^2 <= v < (root + 1)^2
            UInt128 v = (UInt128)(ulong)raw << Fix64.FractionBits;
            UInt128 r = (ulong)root;
            Assert.True(r * r <= v, $"root too big for raw {raw}");
            Assert.True((r + 1) * (r + 1) > v, $"root too small for raw {raw}");
        }
    }
}
