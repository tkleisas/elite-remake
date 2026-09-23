using EliteRemake.Core.Maths;
using Xunit;

namespace EliteRemake.Core.Tests;

/// <summary>
/// MVT6 adds a 16-bit value to a 23-bit sign-magnitude coordinate and must use all 23 bits.
/// </summary>
/// <remarks>
/// This is the primitive the reported wobble came from. It used to take the coordinate's low 16 bits
/// as a single value and add the delta to it, which is exact up to 65535 and wrong from there: the
/// carry out of bit 16 had nowhere to go and the sign byte was left alone, so a coordinate of 70000
/// plus 1000 gave 5464. A body whose coordinates exceeded 16 bits therefore absorbed every rotation
/// step incorrectly and spiralled — measured as an xy-radius falling from 500 to 94 over one turn of
/// roll, where a rotation must preserve length.
///
/// It had no test at all before this, which is why the fault survived a round that claimed to have
/// fixed it and a round that read the listing twice.
/// </remarks>
public class Mvt6Tests
{
    private static byte[] Coordinate(long value)
    {
        long m = Math.Abs(value);
        return [(byte)(m & 0xFF), (byte)((m >> 8) & 0xFF), (byte)(((m >> 16) & 0x7F) | (value < 0 ? 0x80 : 0))];
    }

    /// <summary>Every combination of sign and magnitude up to and past the 16-bit boundary.</summary>
    [Theory]
    [InlineData(500, 300)]
    [InlineData(500, -300)]
    [InlineData(-500, 300)]
    [InlineData(-500, -300)]
    [InlineData(60000, 40000)]
    [InlineData(65535, 1)]
    [InlineData(65536, 1)]
    [InlineData(70000, 1000)]
    [InlineData(70000, -1000)]
    [InlineData(100000, 40000)]
    [InlineData(200000, 5000)]
    [InlineData(-200000, 5000)]
    public void AddsToACoordinate(long coordinate, int delta)
    {
        byte[] c = Coordinate(coordinate);
        byte p1 = (byte)(Math.Abs(delta) & 0xFF);
        byte p2 = (byte)((Math.Abs(delta) >> 8) & 0xFF);
        byte a = (byte)(delta < 0 ? 0x80 : 0x00);

        byte sign = EliteMath.Mvt6(c, 0, a, ref p1, ref p2);

        long magnitude = p1 | ((long)p2 << 8) | ((long)(sign & 0x7F) << 16);
        long got = (sign & 0x80) != 0 ? -magnitude : magnitude;

        Assert.Equal(coordinate + delta, got);
    }
}
