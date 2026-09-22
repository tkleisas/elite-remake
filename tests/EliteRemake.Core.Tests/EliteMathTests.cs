using EliteRemake.Core.Maths;
using Xunit;

namespace EliteRemake.Core.Tests;

/// <summary>
/// Checks the ported maths primitives against independently calculated expectations. The point is
/// not just that the port runs, but that it computes what the original routines are documented to
/// compute, so any transcription slip shows up as a wrong number.
/// </summary>
public class EliteMathTests
{
    [Fact]
    public void Mult1_IsAnExactSignedProduct()
    {
        // MULT1's shift-and-add loop multiplies the 7-bit magnitudes exactly
        for (int q = 0; q <= 127; q++)
        {
            for (int a = 0; a <= 127; a++)
            {
                Check(q, a, 0x00);
                Check(q, a, 0x80);
            }
        }

        static void Check(int q, int a, int signBits)
        {
            byte qb = (byte)(q | signBits);
            byte ab = (byte)(a | (signBits ^ 0x80)); // opposite signs when signBits is set
            ushort result = EliteMath.Mult1(qb, ab);

            int expectedMagnitude = q * a;
            int expectedSign = ((qb ^ ab) & 0x80) != 0 ? -1 : 1;

            Assert.Equal(expectedMagnitude, EliteMath.Magnitude(result));
            if (expectedMagnitude != 0)
            {
                Assert.Equal(expectedSign < 0, EliteMath.IsNegative(result));
                Assert.Equal(expectedSign * expectedMagnitude, EliteMath.ToSigned(result));
            }
        }
    }

    [Fact]
    public void Mult1_ZeroOperandGivesZero()
    {
        Assert.Equal(0, EliteMath.Mult1(0, 0x60));
        Assert.Equal(0, EliteMath.Mult1(0x60, 0));
        Assert.Equal(0, EliteMath.Mult1(0x80, 0x00));
    }

    [Fact]
    public void Add_AddsSignMagnitudeValues()
    {
        // (A P) + (S R) for values that cannot overflow the 15-bit magnitude
        int[] samples = [0, 1, -1, 96, -96, 1000, -1000, 12345, -12345, 32767, -32767];

        foreach (int left in samples)
        {
            foreach (int right in samples)
            {
                long sum = (long)left + right;
                if (Math.Abs(sum) > 32767)
                {
                    continue; // the original wraps here, so it is not a meaningful check
                }

                ushort a = EliteMath.FromSigned(left);
                ushort s = EliteMath.FromSigned(right);
                ushort result = EliteMath.Add(EliteMath.Hi(a), EliteMath.Lo(a), EliteMath.Hi(s), EliteMath.Lo(s));

                Assert.Equal((int)sum, EliteMath.ToSigned(result));
            }
        }
    }

    [Fact]
    public void Add_SubtractsWhenSignsDiffer()
    {
        // -5 + 3 = -2 (exercises the two's complement negation branch)
        ushort minusFive = EliteMath.FromSigned(-5);
        ushort plusThree = EliteMath.FromSigned(3);
        ushort result = EliteMath.Add(
            EliteMath.Hi(minusFive), EliteMath.Lo(minusFive),
            EliteMath.Hi(plusThree), EliteMath.Lo(plusThree));
        Assert.Equal(-2, EliteMath.ToSigned(result));

        // 3 + -5 = -2 (exercises the |A| < |S| branch)
        minusFive = EliteMath.FromSigned(-5);
        plusThree = EliteMath.FromSigned(3);
        result = EliteMath.Add(
            EliteMath.Hi(plusThree), EliteMath.Lo(plusThree),
            EliteMath.Hi(minusFive), EliteMath.Lo(minusFive));
        Assert.Equal(-2, EliteMath.ToSigned(result));

        // -300 + 300 = 0
        ushort minus300 = EliteMath.FromSigned(-300);
        ushort plus300 = EliteMath.FromSigned(300);
        result = EliteMath.Add(
            EliteMath.Hi(minus300), EliteMath.Lo(minus300),
            EliteMath.Hi(plus300), EliteMath.Lo(plus300));
        Assert.Equal(0, EliteMath.ToSigned(result));
    }

    [Fact]
    public void Mad_MultipliesAndAccumulates()
    {
        byte q = 96;        // 1.0 in vector units
        byte a = 0x80 | 32; // -0.333
        int expected = -(96 * 32) + 1000;
        ushort s = EliteMath.FromSigned(1000);
        ushort result = EliteMath.Mad(q, a, EliteMath.Hi(s), EliteMath.Lo(s));
        Assert.Equal(expected, EliteMath.ToSigned(result));
    }

    [Fact]
    public void Squa2_SquaresAnUnsignedByte()
    {
        for (int i = 0; i <= 255; i++)
        {
            ushort result = EliteMath.Squa2((byte)i);
            Assert.Equal(i * i, (EliteMath.Hi(result) << 8) | EliteMath.Lo(result));
        }
    }

    [Fact]
    public void Squa_IgnoresTheSignBit()
    {
        Assert.Equal(EliteMath.Squa2(96), EliteMath.Squa(0x80 | 96));
    }

    [Fact]
    public void Fmltu_ReturnsTheHighByteOfTheProduct()
    {
        for (int a = 0; a <= 255; a += 7)
        {
            for (int q = 0; q <= 255; q += 11)
            {
                byte result = EliteMath.Fmltu((byte)a, (byte)q);
                Assert.Equal((a * q) >> 8, result);
            }
        }
    }

    [Fact]
    public void Fmltu_WithUnity()
    {
        // 0.5 * 0.5 = 0.25 in the 8-bit fixed point used by the flight model
        byte result = EliteMath.Fmltu(128, 128);
        Assert.Equal(64, result);
    }

    [Fact]
    public void Tis2_DividesWith96AsUnity()
    {
        // TIS2 returns 96 when |A| >= Q, and the correct sign
        Assert.Equal(96, EliteMath.Tis2(100, 100));
        Assert.Equal(96, EliteMath.Tis2(127, 1));
        Assert.Equal(96 | 0x80, EliteMath.Tis2(0x80 | 100, 100));

        // Otherwise it approximates A / Q * 96
        for (int q = 1; q <= 127; q++)
        {
            for (int a = 0; a < q; a++)
            {
                double expected = 96.0 * a / q;
                byte result = EliteMath.Tis2((byte)a, (byte)q);

                // TIS2 is a 7-iteration restoring division that converts to 96-units with two
                // floor operations, so it is coarse by design. The original relies on exactly
                // this coarseness (it is what shapes the short-range chart's fuel circle), so we
                // check the approximation is within a couple of units rather than exact.
                const double tolerance = 2.0;
                Assert.True(Math.Abs(result - expected) <= tolerance, $"TIS2({a}, {q}) = {result}, expected about {expected}");
            }
        }
    }

    [Fact]
    public void Sqrt_MatchesTheSquareRoot()
    {
        for (int value = 0; value < 65536; value += 257)
        {
            byte r = (byte)(value >> 8);
            byte q = (byte)(value & 0xFF);
            byte result = EliteMath.Sqrt(r, q);
            int expected = (int)Math.Sqrt(value);
            // LL5 is an 8-bit restoring square root, so allow the usual rounding drift
            Assert.True(Math.Abs(result - expected) <= 1, $"SQRT({value}) = {result}, expected about {expected}");
        }
    }

    [Fact]
    public void Sqrt_OfPerfectSquaresIsExact()
    {
        for (int i = 1; i <= 255; i++)
        {
            int square = i * i;
            byte result = EliteMath.Sqrt((byte)(square >> 8), (byte)(square & 0xFF));
            Assert.Equal(i, result);
        }
    }

    [Fact]
    public void SineTable_MatchesTheOriginal()
    {
        // SNE, as generated by the original source: ABS(SIN((I / 64) * 2 * PI)) * 256
        Assert.Equal(0, EliteMath.SinSegment(0));
        Assert.Equal(25, EliteMath.SinSegment(1));
        Assert.Equal(50, EliteMath.SinSegment(2));
        Assert.Equal(74, EliteMath.SinSegment(3));
        Assert.Equal(98, EliteMath.SinSegment(4));
        Assert.Equal(181, EliteMath.SinSegment(8));  // sin 45 degrees
        Assert.Equal(255, EliteMath.SinSegment(16)); // sin 90 degrees
        Assert.Equal(255, EliteMath.CosSegment(0));  // cos(0) = 1
        Assert.Equal(181, EliteMath.CosSegment(8));  // cos 45 degrees
    }
}
