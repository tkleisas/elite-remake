using EliteRemake.Core.Maths;

namespace EliteRemake.Core.Sim;

/// <summary>
/// The ship-moving maths: rotating a ship's location around us and moving it through space.
/// </summary>
/// <remarks>
/// Everything in the local bubble is stored relative to our ship, so when we pitch and roll it is
/// the universe that appears to rotate, in the opposite direction. MVEIT does this for every ship
/// in the bubble, rotating both its location and its orientation vectors by our pitch and roll
/// angles, and then moving it backwards by our speed. These routines are ports of the original's
/// MLTU2/MVT6-based calculations, complete with their 24-bit sign-magnitude arithmetic.
/// </remarks>
public static class ShipMovement
{
    /// <summary>Byte offsets of the x, y and z coordinates in a ship data block.</summary>
    public const int X = 0;

    /// <summary>Byte offset of the y coordinate.</summary>
    public const int Y = 3;

    /// <summary>Byte offset of the z coordinate.</summary>
    public const int Z = 6;

    /// <summary>
    /// MVEIT part 5: rotate the ship's location by our pitch and roll.
    /// </summary>
    /// <param name="position">
    /// The ship's 9-byte location block: x, y and z, each as lo, hi, sign.
    /// </param>
    /// <param name="alp1">The magnitude of our roll (the original's ALP1, 0-31).</param>
    /// <param name="alp2">The sign of our roll (ALP2).</param>
    /// <param name="bet1">The magnitude of our pitch (BET1, 0-8).</param>
    /// <param name="bet2">The sign of our pitch (BET2).</param>
    public static void RotateLocationByOurPitchAndRoll(
        Span<byte> position,
        byte alp1,
        byte alp2,
        byte bet1,
        byte bet2)
    {
        // K2 = y - x * alpha / 256
        byte p = (byte)~position[X];
        (byte a, byte pHi, byte pLo, _) = EliteMath.Mltu2(position[X + 1], p, alp1);
        _ = pLo;
        byte p1 = pHi;
        byte p2 = a;
        byte sign = (byte)((byte)(alp2 ^ 0x80) ^ position[X + 2]);
        byte k2Sign = EliteMath.Mvt6(position, Y, sign, ref p1, ref p2);
        byte k2Lo = p1;
        byte k2Hi = p2;

        // z = z + K2 * beta / 256
        p = (byte)~k2Lo;
        (a, pHi, pLo, _) = EliteMath.Mltu2(k2Hi, p, bet1);
        p1 = pHi;
        p2 = a;
        sign = (byte)(k2Sign ^ bet2);
        byte zSign = EliteMath.Mvt6(position, Z, sign, ref p1, ref p2);
        position[Z + 2] = zSign;
        position[Z] = p1;
        position[Z + 1] = p2;

        // y = K2 +/- z * beta / 256, where the sign of the combination decides which way round
        p = (byte)~position[Z];
        (a, pHi, pLo, bool carry) = EliteMath.Mltu2(position[Z + 1], p, bet1);
        p1 = pHi;
        p2 = a;
        position[Y + 2] = k2Sign;

        // The original branches to the subtract path when K2_sign EOR beta_sign EOR z_sign is
        // positive, and adds otherwise
        bool add = ((k2Sign ^ bet2 ^ zSign) & 0x80) != 0;
        if (add)
        {
            // The original does not clear the carry here: it adds using whatever MLTU2 left
            // behind, so we reproduce that
            int low = p1 + k2Lo + (carry ? 1 : 0);
            position[Y] = (byte)low;
            int high = p2 + k2Hi + (low > 0xFF ? 1 : 0);
            position[Y + 1] = (byte)high;
        }
        else
        {
            int low = k2Lo - p1 - (carry ? 0 : 1);
            position[Y] = (byte)low;
            bool borrow = low < 0;
            int high = k2Hi - p2 - (borrow ? 1 : 0);
            position[Y + 1] = (byte)high;

            if (high < 0)
            {
                // Negate (y_sign y_hi y_lo) using two's complement. The carry is clear on entry to
                // the negation, so the first subtraction borrows an extra 1
                bool carryFlag = false;
                int negLow = 1 - position[Y] - (carryFlag ? 0 : 1);
                position[Y] = (byte)negLow;
                carryFlag = negLow >= 0;
                int negHigh = 0 - position[Y + 1] - (carryFlag ? 0 : 1);
                position[Y + 1] = (byte)negHigh;
                position[Y + 2] = (byte)(position[Y + 2] ^ 0x80);
            }
        }

        // x = x + y * alpha / 256
        p = (byte)~position[Y];
        (a, pHi, pLo, _) = EliteMath.Mltu2(position[Y + 1], p, alp1);
        p1 = pHi;
        p2 = a;
        sign = (byte)(alp2 ^ position[Y + 2]);
        position[X + 2] = EliteMath.Mvt6(position, X, sign, ref p1, ref p2);
        position[X + 1] = p2;
        position[X] = p1;
    }

    /// <summary>
    /// MV40: rotate a planet or sun's location by our pitch and roll.
    /// </summary>
    /// <remarks>
    /// The original uses a separate routine for the planet and the sun, because they sit millions
    /// of units away and their coordinates need all 23 bits of their magnitude. The routine that
    /// moves ordinary ships works on two-byte coordinates and treats the third byte as a pure sign,
    /// which would destroy the planet's distance; MV40 works in wider arithmetic instead. The
    /// formulas are the same small-angle rotation, applied to the full 24-bit values.
    /// </remarks>
    public static void RotateBodyLocationByOurPitchAndRoll(
        Span<byte> position,
        byte alp1,
        byte alp2,
        byte bet1,
        byte bet2)
    {
        long x = Read24(position, X);
        long y = Read24(position, Y);
        long z = Read24(position, Z);

        // The angles are the magnitudes with the sign of ALP2 and BET2
        long alpha = (alp2 & 0x80) != 0 ? -alp1 : alp1;
        long beta = (bet2 & 0x80) != 0 ? -bet1 : bet1;

        long k2 = y - ((x * alpha) / 256);
        z += (beta * k2) / 256;
        y = k2 - ((beta * z) / 256);
        x += (alpha * y) / 256;

        Write24(position, X, x);
        Write24(position, Y, y);
        Write24(position, Z, z);
    }

    /// <summary>Reads a 24-bit sign-magnitude coordinate.</summary>
    public static int Read24(ReadOnlySpan<byte> position, int offset)
    {
        int magnitude = position[offset] | (position[offset + 1] << 8) | ((position[offset + 2] & 0x7F) << 16);
        return (position[offset + 2] & 0x80) != 0 ? -magnitude : magnitude;
    }

    /// <summary>Writes a 24-bit sign-magnitude coordinate.</summary>
    public static void Write24(Span<byte> position, int offset, long value)
    {
        long magnitude = Math.Abs(value);
        if (magnitude > 0x7FFFFF)
        {
            magnitude = 0x7FFFFF;
        }

        position[offset] = (byte)(magnitude & 0xFF);
        position[offset + 1] = (byte)((magnitude >> 8) & 0xFF);
        position[offset + 2] = (byte)(((magnitude >> 16) & 0x7F) | (value < 0 ? 0x80 : 0x00));
    }

    /// <summary>
    /// MVEIT part 3: move the ship forward along its own nose vector by its own speed.
    /// The displacement is nosev_hi * speed / 64 on each axis.
    /// </summary>
    /// <param name="position">The ship's 9-byte location block.</param>
    /// <param name="nosev">The ship's nose vector, 6 bytes starting at its low byte.</param>
    /// <param name="speed">The ship's speed (INWK+27).</param>
    public static void MoveShipForward(Span<byte> position, ReadOnlySpan<byte> nosev, byte speed)
    {
        // Q = speed * 4
        byte q = (byte)(speed << 2);

        for (int axis = 0; axis < 3; axis++)
        {
            byte component = nosev[(axis * 2) + 1]; // the high byte carries the sign
            byte magnitude = (byte)(component & 0x7F);
            byte displacement = EliteMath.Fmltu(magnitude, q);

            // The displacement is at most a byte, so the original uses the MVT1-2 entry point,
            // which takes only the sign of the delta's high byte
            ShipMath.Mvt1(position, axis * 3, component, displacement, signOnly: true);
        }
    }

    /// <summary>
    /// MVEIT part 6: move the ship backwards by our speed, as we are the ones travelling.
    /// </summary>
    /// <param name="position">The ship's 9-byte location block.</param>
    /// <param name="ourSpeed">Our speed (DELTA).</param>
    public static void MoveShipByOurSpeed(Span<byte> position, byte ourSpeed)
        => ShipMath.Mvt1(position, Z, 0x80, ourSpeed);
}
