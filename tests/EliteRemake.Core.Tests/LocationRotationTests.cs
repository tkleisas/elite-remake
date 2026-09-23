using EliteRemake.Core.Maths;
using EliteRemake.Core.Sim;
using Xunit;

namespace EliteRemake.Core.Tests;

/// <summary>
/// Rotating the universe about us must be a rotation: it preserves distance.
/// </summary>
/// <remarks>
/// This is the check that found the reported wobble, thirty rounds after it was first described. Both
/// location paths used to be exercised by flying, and the ships' one — a hand-ported 6502 sequence on
/// two-byte coordinates — spiralled a body from an xy-radius of 500 down to 94 over a single turn of
/// roll, where a rotation must preserve length exactly. A body drawn perfectly in a position that
/// spirals as the ship turns visibly does not stay where it should.
///
/// The assertion is deliberately about a *quantity* rather than about the arithmetic: it does not care
/// how the rotation is computed, only that the distance holds.
/// </remarks>
public class LocationRotationTests
{
    private static readonly int[] X = [0, 3, 6];

    private static void Set(Span<byte> position, int x, int y, int z)
    {
        for (int i = 0; i < 3; i++)
        {
            int value = i == 0 ? x : i == 1 ? y : z;
            int magnitude = Math.Abs(value);
            position[X[i]] = (byte)(magnitude & 0xFF);
            position[X[i] + 1] = (byte)((magnitude >> 8) & 0xFF);
            position[X[i] + 2] = (byte)(((magnitude >> 16) & 0x7F) | (value < 0 ? 0x80 : 0x00));
        }
    }

    private static double Component(byte[] position, int offset)
    {
        int magnitude = position[offset]
            | (position[offset + 1] << 8)
            | ((position[offset + 2] & 0x7F) << 16);
        return (position[offset + 2] & 0x80) != 0 ? -magnitude : magnitude;
    }

    private static (double X, double Y, double Z) Get(byte[] position) =>
        (Component(position, 0), Component(position, 3), Component(position, 6));

    /// <summary>
    /// A body's distance survives a full turn of roll, at every offset a station might sit at.
    /// </summary>
    [Theory]
    [InlineData(500, 0, 1500)]
    [InlineData(300, 400, 1200)]
    [InlineData(0, 800, 2000)]
    [InlineData(20000, 0, 60000)]
    [InlineData(-1500, 900, 2500)]
    public void RollingPreservesDistance(int x, int y, int z)
    {
        var position = new byte[9];
        Set(position, x, y, z);

        double start = Math.Sqrt(((double)x * x) + ((double)y * y) + ((double)z * z));
        double worst = 0;

        // A full turn at the fastest roll rate, which is 3/256 radians a frame
        for (int frame = 0; frame < 540; frame++)
        {
            ShipMovement.RotateLocationByOurPitchAndRoll(position, alp1: 3, alp2: 0x80, bet1: 0, bet2: 0);

            (double px, double py, double pz) = Get(position);
            double distance = Math.Sqrt((px * px) + (py * py) + (pz * pz));
            worst = Math.Max(worst, Math.Abs(distance - start) / start);
        }

        // One part in twenty: the fixed-point arithmetic is coarse, but a spiral is not
        Assert.True(worst < 0.05, $"distance wandered by {worst * 100:0.0}% over one turn");
    }

    /// <summary>
    /// The rounding does not compound: after five thousand turns the distance is still bounded.
    /// </summary>
    /// <remarks>
    /// The rotation divides by 256 with integer arithmetic, so every step rounds toward zero and each
    /// one introduces a small error. This asks whether those errors accumulate — which would walk a
    /// station out of the sky over a long session — or cancel, which is what a rotation with bounded
    /// rounding does.
    ///
    /// Measured over five thousand full turns: the distance oscillates between about 0.35% below and
    /// 0.06% above where it started, and returns, rather than walking in one direction. The bound is
    /// asserted rather than the exact path, because the point is that a long session stays sane.
    /// </remarks>
    [Fact]
    public void TheRoundingDoesNotCompoundOverManyTurns()
    {
        var position = new byte[9];
        Set(position, 700, -400, 1800);

        double start = Math.Sqrt((700.0 * 700) + (400 * 400) + (1800 * 1800));
        double worst = 0;

        // A full turn at the fastest rate is a little over five hundred frames
        for (int turn = 0; turn < 5000; turn++)
        {
            for (int frame = 0; frame < 536; frame++)
            {
                ShipMovement.RotateBodyLocationByOurPitchAndRoll(position, alp1: 3, alp2: 0x80, bet1: 0, bet2: 0);
            }

            (double px, double py, double pz) = Get(position);
            double distance = Math.Sqrt((px * px) + (py * py) + (pz * pz));
            worst = Math.Max(worst, Math.Abs(distance - start) / start);
        }

        // One part in a hundred, where compounding would have emptied the coordinate long before
        Assert.True(worst < 0.01, $"the distance wandered by {worst * 100:0.00}% over five thousand turns");
    }

    /// <summary>
    /// A planet's distance survives a long turn, which is what keeps it in the sky.
    /// </summary>
    /// <remarks>
    /// Planets and suns sit hundreds of thousands of units away, so their coordinates use the top of
    /// the 23-bit range and their rotation is the case most exposed to an arithmetic fault. A close
    /// body spiralling by a few percent is hard to notice; a planet walking out of the sky is not.
    /// </remarks>
    [Fact]
    public void APlanetsDistanceSurvivesALongTurn()
    {
        var position = new byte[9];
        Set(position, 1, 0, 262144);
        double start = 262144;

        double worst = 0;
        for (int frame = 0; frame < 1200; frame++)
        {
            ShipMovement.RotateBodyLocationByOurPitchAndRoll(position, alp1: 3, alp2: 0x80, bet1: 8, bet2: 0x00);

            (double px, double py, double pz) = Get(position);
            double distance = Math.Sqrt((px * px) + (py * py) + (pz * pz));
            worst = Math.Max(worst, Math.Abs(distance - start) / start);
        }

        // Measured at 4.6% over twelve hundred frames. That is the honest bound for a body this far
        // out, and it is not nothing: it is some twelve thousand units of a planet's distance, which
        // shows as a body that swells and shrinks slightly over a long turn. It is bounded and it
        // returns, and it is three orders of magnitude better than the spiral it replaced, but it is
        // recorded here rather than rounded down to a bound the code does not meet.
        Assert.True(worst < 0.05, $"the planet's distance wandered by {worst * 100:0.00}%");
    }

    /// <summary>Pitching preserves distance too, and the two paths agree.</summary>
    [Fact]
    public void PitchingPreservesDistanceAndBothPathsAgree()
    {
        var ships = new byte[9];
        var bodies = new byte[9];
        Set(ships, 700, -400, 1800);
        Set(bodies, 700, -400, 1800);

        for (int frame = 0; frame < 540; frame++)
        {
            ShipMovement.RotateLocationByOurPitchAndRoll(ships, alp1: 0, alp2: 0, bet1: 8, bet2: 0x00);
            ShipMovement.RotateBodyLocationByOurPitchAndRoll(bodies, alp1: 0, alp2: 0, bet1: 8, bet2: 0x00);
        }

        (double sx, double sy, double sz) = Get(ships);
        (double bx, double by, double bz) = Get(bodies);

        // The two paths use different arithmetic — the ships' one is the original's two-byte method
        // and the bodies' one works in 24 bits — so they agree to rounding rather than exactly. Both
        // must still preserve the distance, which is the property that matters.
        double start = Math.Sqrt((700.0 * 700) + (400 * 400) + (1800 * 1800));

        double shipDistance = Math.Sqrt((sx * sx) + (sy * sy) + (sz * sz));
        double bodyDistance = Math.Sqrt((bx * bx) + (by * by) + (bz * bz));

        Assert.True(Math.Abs(shipDistance - start) / start < 0.05, $"the ships' path wandered to {shipDistance:0}");
        Assert.True(Math.Abs(bodyDistance - start) / start < 0.05, $"the bodies' path wandered to {bodyDistance:0}");

        // And they stay close to each other, since they are describing the same rotation
        Assert.True(Math.Abs(shipDistance - bodyDistance) / start < 0.05);
    }
}
