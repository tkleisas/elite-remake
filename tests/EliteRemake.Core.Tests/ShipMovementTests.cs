using EliteRemake.Core.Maths;
using EliteRemake.Core.Sim;
using Xunit;

namespace EliteRemake.Core.Tests;

/// <summary>
/// Checks the ported ship-movement maths: the rotations that turn the universe around us, and the
/// forward motion of ships in the local bubble.
/// </summary>
public class ShipMovementTests
{
    /// <summary>Builds a 24-bit location block of lo, hi, sign bytes.</summary>
    private static byte[] Position(int x, int y, int z)
    {
        var block = new byte[9];
        Write(block, 0, x);
        Write(block, 3, y);
        Write(block, 6, z);
        return block;

        static void Write(byte[] block, int offset, int value)
        {
            int magnitude = Math.Abs(value);
            block[offset] = (byte)(magnitude & 0xFF);
            block[offset + 1] = (byte)((magnitude >> 8) & 0x7F);
            block[offset + 2] = (byte)(value < 0 ? 0x80 : 0x00);
        }
    }

    private static int Read(byte[] block, int offset)
    {
        int magnitude = block[offset] | (block[offset + 1] << 8);
        return (block[offset + 2] & 0x80) != 0 ? -magnitude : magnitude;
    }

    [Fact]
    public void MoveShipByOurSpeed_MovesEverythingBackwards()
    {
        byte[] position = Position(0, 0, 1000);
        ShipMovement.MoveShipByOurSpeed(position, 20);
        Assert.Equal(980, Read(position, 6));

        // A ship behind us moves further behind
        position = Position(0, 0, -1000);
        ShipMovement.MoveShipByOurSpeed(position, 20);
        Assert.Equal(-1020, Read(position, 6));
    }

    [Fact]
    public void MoveShipForward_UsesTheNoseVectorHighBytes()
    {
        // A ship at the origin with its nose along +z and a speed of 28 moves by
        // nosev_z_hi * speed / 64 = 96 * 28 / 64 = 42 units per frame
        byte[] position = Position(0, 0, 0);
        byte[] nosev = new byte[6];
        nosev[5] = 96; // nosev_z_hi
        ShipMovement.MoveShipForward(position, nosev, 28);
        Assert.Equal(42, Read(position, 6));
        Assert.Equal(0, Read(position, 0));
        Assert.Equal(0, Read(position, 3));

        // A ship pointing along -x moves the other way
        nosev = new byte[6];
        nosev[1] = 0x80 | 96; // nosev_x_hi, negative
        position = Position(0, 0, 0);
        ShipMovement.MoveShipForward(position, nosev, 28);
        Assert.Equal(-42, Read(position, 0));
    }

    [Fact]
    public void RotateLocation_LeavesPointsOnTheRollAxisAlone()
    {
        // Rolling does not move a ship that is straight ahead. The original's complement-based
        // multiply leaves a rounding artefact of at most a unit, so allow for that
        byte[] position = Position(0, 0, 2000);
        ShipMovement.RotateLocationByOurPitchAndRoll(position, alp1: 16, alp2: 0, bet1: 0, bet2: 0);
        Assert.InRange(Read(position, 0), -2, 2);
        Assert.InRange(Read(position, 3), -2, 2);
        Assert.Equal(2000, Read(position, 6));
    }

    [Fact]
    public void RotateLocation_MovesPointsAboveUsSidewaysWhenRolling()
    {
        // A ship directly above us moves along +x when we roll, by about alpha / 256 of its
        // distance: 8 / 256 * 10000 = 312
        byte[] position = Position(0, 10000, 0);
        ShipMovement.RotateLocationByOurPitchAndRoll(position, alp1: 8, alp2: 0, bet1: 0, bet2: 0);
        Assert.InRange(Read(position, 0), 300, 325);
    }

    [Fact]
    public void RotateLocation_PitchingMovesPointsAheadDownTheScreen()
    {
        // A ship ahead of us drops below us when we pitch, by about beta / 256 of its distance
        byte[] position = Position(0, 0, 10000);
        ShipMovement.RotateLocationByOurPitchAndRoll(position, alp1: 0, alp2: 0, bet1: 8, bet2: 0);
        Assert.InRange(Read(position, 3), -325, -300);
    }

    [Fact]
    public void RotateLocation_PreservesDistance()
    {
        var random = new Random(1234);
        for (int i = 0; i < 200; i++)
        {
            int x = random.Next(-20000, 20000);
            int y = random.Next(-20000, 20000);
            int z = random.Next(-20000, 20000);
            double before = Math.Sqrt((x * x) + (y * y) + (z * z));
            if (before < 1000)
            {
                continue;
            }

            byte[] position = Position(x, y, z);
            ShipMovement.RotateLocationByOurPitchAndRoll(
                position,
                alp1: (byte)random.Next(0, 32),
                alp2: (byte)(random.Next(2) == 0 ? 0 : 0x80),
                bet1: (byte)random.Next(0, 9),
                bet2: (byte)(random.Next(2) == 0 ? 0 : 0x80));

            double after = Math.Sqrt(
                Math.Pow(Read(position, 0), 2) +
                Math.Pow(Read(position, 3), 2) +
                Math.Pow(Read(position, 6), 2));

            // A small-angle rotation should not change the distance by more than a few percent
            Assert.InRange(after / before, 0.9, 1.1);
        }
    }

    [Fact]
    public void RotateLocation_LeavesTheOriginAlone()
    {
        byte[] position = Position(0, 0, 0);
        ShipMovement.RotateLocationByOurPitchAndRoll(position, alp1: 31, alp2: 0, bet1: 8, bet2: 0);
        Assert.InRange(Read(position, 0), -2, 2);
        Assert.InRange(Read(position, 3), -2, 2);
        Assert.InRange(Read(position, 6), -2, 2);
    }
}
