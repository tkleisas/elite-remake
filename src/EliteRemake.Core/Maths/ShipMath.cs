using System.Runtime.InteropServices;

namespace EliteRemake.Core.Maths;

/// <summary>
/// Faithful ports of the routines that move, rotate and tidy a ship's orientation vectors and
/// position: MVS4, MVS5, NORM, TIDY, MVT1 and the keyboard damping routine <c>cntr</c>.
/// </summary>
public static class ShipMath
{
    /// <summary>
    /// MVS4: apply pitch and roll angles alpha (roll) and beta (pitch) to one of a ship's
    /// orientation vectors, using the small-angle approximation and the Minsky circle algorithm.
    /// </summary>
    /// <param name="orientation">The ship's orientation vectors.</param>
    /// <param name="vector">Byte offset of the vector to rotate (0 = nosev, 6 = roofv, 12 = sidev).</param>
    /// <param name="alpha">The roll angle.</param>
    /// <param name="beta">The pitch angle.</param>
    public static void Mvs4(Orientation orientation, int vector, byte alpha, byte beta)
    {
        int y = vector;

        // nosev_y = nosev_y - alpha * nosev_x_hi
        byte s = orientation[y + 3];
        byte r = orientation[y + 2];
        byte a = (byte)(orientation[y + 1] ^ 0x80);
        ushort result = EliteMath.Mad(alpha, a, s, r);
        orientation[y + 3] = EliteMath.Hi(result);
        orientation[y + 2] = EliteMath.Lo(result);

        // nosev_x = nosev_x + alpha * nosev_y_hi
        s = orientation[y + 1];
        r = orientation[y];
        a = orientation[y + 3];
        result = EliteMath.Mad(alpha, a, s, r);
        orientation[y + 1] = EliteMath.Hi(result);
        orientation[y] = EliteMath.Lo(result);

        // nosev_y = nosev_y - beta * nosev_z_hi
        s = orientation[y + 3];
        r = orientation[y + 2];
        a = (byte)(orientation[y + 5] ^ 0x80);
        result = EliteMath.Mad(beta, a, s, r);
        orientation[y + 3] = EliteMath.Hi(result);
        orientation[y + 2] = EliteMath.Lo(result);

        // nosev_z = nosev_z + beta * nosev_y_hi
        s = orientation[y + 5];
        r = orientation[y + 4];
        a = orientation[y + 3];
        result = EliteMath.Mad(beta, a, s, r);
        orientation[y + 5] = EliteMath.Hi(result);
        orientation[y + 4] = EliteMath.Lo(result);
    }

    /// <summary>
    /// MVS5: rotate two orientation vectors against each other by 3.6 degrees, the routine a ship
    /// uses to rotate about its own axes. The carry flag equivalent of the original is the sign of
    /// <paramref name="rat2"/>, which gives the direction of the rotation.
    /// </summary>
    /// <param name="orientation">The ship's orientation vectors.</param>
    /// <param name="x">Byte offset of the first component (0, 2, 4, 6, 8, 10, 12, 14 or 16).</param>
    /// <param name="y">Byte offset of the second component.</param>
    /// <param name="rat2">The direction of the rotation: 0 for positive, 0x80 for negative.</param>
    public static void Mvs5(Orientation orientation, int x, int y, byte rat2)
    {
        // T = |v[x]| / 512, i.e. |v[x]_hi| / 2
        byte t = (byte)((orientation[x + 1] & 0x7F) >> 1);

        // (S R) = (1 - 1/512) * v[x], by subtracting T from the 16-bit value
        int lowDiff = orientation[x] - t;
        byte r = (byte)lowDiff;
        int borrow = lowDiff < 0 ? 1 : 0;
        int highDiff = orientation[x + 1] - borrow;
        byte s = (byte)highDiff;

        // (A P) = |v[y]| / 16, with the sign of v[y]
        byte p = orientation[y];
        byte sign = (byte)(orientation[y + 1] & 0x80);
        int absHigh = orientation[y + 1] & 0x7F;
        int shifted = absHigh >> 4;
        int shiftedLow = ((absHigh & 0x0F) << 4) | (p >> 4);
        byte a = (byte)(shifted | sign);
        p = (byte)shiftedLow;
        a ^= rat2;

        // K(1 0) = (A P) + (S R) = (1 - 1/512) * v[x] +/- v[y] / 16
        ushort k = EliteMath.Add(a, p, s, r);

        // Now do the same the other way round: (S R) = (1 - 1/512) * v[y]
        t = (byte)((orientation[y + 1] & 0x7F) >> 1);
        lowDiff = orientation[y] - t;
        r = (byte)lowDiff;
        borrow = lowDiff < 0 ? 1 : 0;
        highDiff = orientation[y + 1] - borrow;
        s = (byte)highDiff;

        // (A P) = |v[x]| / 16, with the opposite sign
        p = orientation[x];
        sign = (byte)(orientation[x + 1] & 0x80);
        absHigh = orientation[x + 1] & 0x7F;
        shifted = absHigh >> 4;
        shiftedLow = ((absHigh & 0x0F) << 4) | (p >> 4);
        a = (byte)(shifted | sign);
        p = (byte)shiftedLow;
        a ^= 0x80;      // the sign of -v[x]
        a ^= rat2;

        ushort newY = EliteMath.Add(a, p, s, r);

        // v[y] = (1 - 1/512) * v[y] -/+ v[x] / 16, then v[x] = K(1 0)
        orientation[y + 1] = EliteMath.Hi(newY);
        orientation[y] = EliteMath.Lo(newY);
        orientation[x + 1] = EliteMath.Hi(k);
        orientation[x] = EliteMath.Lo(k);
    }

    /// <summary>
    /// NORM: normalise the 3-byte vector in <paramref name="xyz"/>, returning its original length.
    /// The vector is scaled so that unity is 96, matching TIS2's convention.
    /// </summary>
    public static byte Norm(Span<byte> xyz)
    {
        ushort square = EliteMath.Squa(xyz[0]);
        byte r = EliteMath.Hi(square);
        byte q = EliteMath.Lo(square);

        square = EliteMath.Squa(xyz[1]);
        byte t = EliteMath.Hi(square);
        byte p = EliteMath.Lo(square);

        int low = q + p;
        q = (byte)low;
        int high = r + t + (low > 0xFF ? 1 : 0);
        r = (byte)high;

        square = EliteMath.Squa(xyz[2]);
        t = EliteMath.Hi(square);
        p = EliteMath.Lo(square);

        low = q + p;
        q = (byte)low;
        high = r + t + (low > 0xFF ? 1 : 0);
        r = (byte)high;

        byte length = EliteMath.Sqrt(r, q); // LL5
        xyz[0] = EliteMath.Tis2(xyz[0], length);
        xyz[1] = EliteMath.Tis2(xyz[1], length);
        xyz[2] = EliteMath.Tis2(xyz[2], length);
        return length;
    }

    /// <summary>
    /// TIDY: tidy up a ship's orientation vectors to prevent them from becoming elongated and out
    /// of shape over time. It normalises nosev and roofv, rebuilds the component of roofv most
    /// orthogonal to nosev, then sets sidev to the cross product of the other two.
    /// </summary>
    public static void Tidy(Orientation orientation)
    {
        Span<byte> xx15 = stackalloc byte[3];
        Span<byte> all = orientation.AsSpan();
        ReadOnlySpan<byte> nosev = all.Slice(0, 6);
        ReadOnlySpan<byte> roofv = all.Slice(6, 6);

        // Normalise nosev
        orientation.GetHighBytes(Orientation.Nosev, xx15);
        Norm(xx15);
        orientation.SetHighBytes(Orientation.Nosev, xx15);

        // Rebuild the roofv component that lines up with the largest nosev component
        if ((xx15[0] & 0x60) != 0)
        {
            // nosev_x is big, so roofv_x = -(nosev_y * roofv_y + nosev_z * roofv_z) / nosev_x
            orientation[Orientation.Roofv + 1] = EliteMath.Tis3(nosev, roofv, x: 2, y: 4, a: 0);
        }
        else if ((xx15[1] & 0x60) != 0)
        {
            // nosev_y is big, so roofv_y = -(nosev_x * roofv_x + nosev_z * roofv_z) / nosev_y
            orientation[Orientation.Roofv + 3] = EliteMath.Tis3(nosev, roofv, x: 0, y: 4, a: 2);
        }
        else
        {
            // nosev_z is big, so roofv_z = -(nosev_x * roofv_x + nosev_y * roofv_y) / nosev_z
            orientation[Orientation.Roofv + 5] = EliteMath.Tis3(nosev, roofv, x: 0, y: 2, a: 4);
        }

        // Normalise roofv
        orientation.GetHighBytes(Orientation.Roofv, xx15);
        Norm(xx15);
        orientation.SetHighBytes(Orientation.Roofv, xx15);

        // sidev = nosev x roofv, calculated from the high bytes
        // Q starts as nosev_y; TIS1 leaves Q set to its X argument for the next call
        byte q = orientation[Orientation.Nosev + 3];
        ushort product = EliteMath.Mult1(q, orientation[Orientation.Roofv + 5]);
        byte s = EliteMath.Hi(product);
        byte r = EliteMath.Lo(product);
        byte a = EliteMath.Tis1(orientation[Orientation.Roofv + 3], orientation[Orientation.Nosev + 5], s, r);
        orientation[Orientation.Sidev + 1] = (byte)(a ^ 0x80);

        q = orientation[Orientation.Nosev + 5]; // Q was set to nosev_z by the last TIS1
        product = EliteMath.Mult1(q, orientation[Orientation.Roofv + 1]);
        s = EliteMath.Hi(product);
        r = EliteMath.Lo(product);
        a = EliteMath.Tis1(orientation[Orientation.Roofv + 5], orientation[Orientation.Nosev + 1], s, r);
        orientation[Orientation.Sidev + 3] = (byte)(a ^ 0x80);

        q = orientation[Orientation.Nosev + 1]; // Q was set to nosev_x by the last TIS1
        product = EliteMath.Mult1(q, orientation[Orientation.Roofv + 3]);
        s = EliteMath.Hi(product);
        r = EliteMath.Lo(product);
        a = EliteMath.Tis1(orientation[Orientation.Roofv + 1], orientation[Orientation.Nosev + 3], s, r);
        orientation[Orientation.Sidev + 5] = (byte)(a ^ 0x80);

        // Zero the low bytes of all the components, as they are not needed for a unit vector.
        // Note that the original's TIL1 loop runs from INWK+23 down to INWK+9, so it clears
        // sidev_y, sidev_x, roofv_z, roofv_y, roofv_x, nosev_z, nosev_y and nosev_x, but not
        // sidev_z_lo. That quirk is reproduced here.
        orientation[Orientation.Nosev] = 0;
        orientation[Orientation.Nosev + 2] = 0;
        orientation[Orientation.Nosev + 4] = 0;
        orientation[Orientation.Roofv] = 0;
        orientation[Orientation.Roofv + 2] = 0;
        orientation[Orientation.Roofv + 4] = 0;
        orientation[Orientation.Sidev] = 0;
        orientation[Orientation.Sidev + 2] = 0;
    }

    /// <summary>
    /// cntr: apply keyboard damping, creeping the rate in <paramref name="x"/> towards the centre
    /// by 1. Set <paramref name="dampingDisabled"/> for the DAMP flag.
    /// </summary>
    public static byte Cntr(byte x, bool dampingDisabled = false)
    {
        if (dampingDisabled)
        {
            return x;
        }

        if (x < 128)
        {
            // BPL BUMP
            x = (byte)(x + 1);
            if (x != 0)
            {
                return x;
            }

            // .REDU
            x = (byte)(x - 1);
            if (x == 0)
            {
                return (byte)(x + 1);
            }

            return x;
        }

        // DEX / BMI RE1
        x = (byte)(x - 1);
        if (x >= 128)
        {
            return x;
        }

        // .BUMP
        x = (byte)(x + 1);
        if (x != 0)
        {
            return x;
        }

        // .REDU
        x = (byte)(x - 1);
        if (x == 0)
        {
            return (byte)(x + 1);
        }

        return x;
    }

    /// <summary>
    /// MVT1: add the 16-bit delta (A R) to the coordinate at <paramref name="offset"/> in
    /// <paramref name="coordinate"/>, which is stored as lo, hi, sign.
    /// </summary>
    /// <param name="coordinate">A 24-bit coordinate as three bytes: lo, hi, sign.</param>
    /// <param name="offset">Offset of the coordinate's low byte.</param>
    /// <param name="a">The delta's high byte, whose bit 7 is the sign.</param>
    /// <param name="r">The delta's low byte.</param>
    /// <param name="signOnly">
    /// When true, only the sign of <paramref name="a"/> is used, reproducing calls to the
    /// original's MVT1-2 entry point.
    /// </param>
    public static void Mvt1(Span<byte> coordinate, int offset, byte a, byte r, bool signOnly = false)
    {
        if (signOnly)
        {
            a = (byte)(a & 0x80);
        }

        // ASL A / STA S / LDA #0 / ROR A / STA T / LSR S
        bool carry = (a & 0x80) != 0;
        byte s = (byte)(a << 1);
        byte t = (byte)(carry ? 0x80 : 0x00);
        s = (byte)(s >> 1);
        carry = false; // LSR S leaves the carry clear

        if (((t ^ coordinate[offset + 2]) & 0x80) != 0)
        {
            // MV10: different signs, so subtract the magnitudes (carry flag = false means borrow)
            bool carryFlag;
            int lowDiff = coordinate[offset] - r;
            coordinate[offset] = (byte)lowDiff;
            carryFlag = lowDiff >= 0;

            int highDiff = coordinate[offset + 1] - s - (carryFlag ? 0 : 1);
            coordinate[offset + 1] = (byte)highDiff;
            carryFlag = highDiff >= 0;

            int signDiff = (coordinate[offset + 2] & 0x7F) - (carryFlag ? 0 : 1);
            coordinate[offset + 2] = (byte)(((byte)signDiff | 0x80) ^ t);

            if (carryFlag)
            {
                return; // MV11: no borrow, so the coordinate has the sign we just stored
            }

            // The subtraction borrowed, so negate the whole 24-bit result
            carryFlag = false; // C is clear on entry to the negation
            int negLow = 1 - coordinate[offset] - (carryFlag ? 0 : 1);
            coordinate[offset] = (byte)negLow;
            carryFlag = negLow >= 0;

            int negHigh = 0 - coordinate[offset + 1] - (carryFlag ? 0 : 1);
            coordinate[offset + 1] = (byte)negHigh;
            carryFlag = negHigh >= 0;

            int negSign = 0 - coordinate[offset + 2] - (carryFlag ? 0 : 1);
            coordinate[offset + 2] = (byte)(((byte)negSign & 0x7F) | t);
            return;
        }

        // Same signs, so add the magnitudes
        int lowSum = coordinate[offset] + r;
        coordinate[offset] = (byte)lowSum;
        int highSum = coordinate[offset + 1] + s + (lowSum > 0xFF ? 1 : 0);
        coordinate[offset + 1] = (byte)highSum;
        int signSum = coordinate[offset + 2] + (highSum > 0xFF ? 1 : 0);
        coordinate[offset + 2] = (byte)(((byte)signSum) | t);
    }
}
