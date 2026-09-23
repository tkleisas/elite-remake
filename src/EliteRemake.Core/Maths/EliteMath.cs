using System.Runtime.CompilerServices;

namespace EliteRemake.Core.Maths;

/// <summary>
/// Faithful ports of the BBC Micro disc Elite maths routines.
/// </summary>
/// <remarks>
/// Elite works with three number formats, all of which are reproduced here exactly:
///
/// <list type="bullet">
/// <item><b>Sign-magnitude 8-bit</b> ("SM8"): bit 7 is the sign, bits 0-6 the magnitude.
/// Angles, speeds, ratios and orientation-vector high bytes all use this format.</item>
/// <item><b>Sign-magnitude 16-bit</b> ("SM16"): bit 15 is the sign, bits 0-14 the magnitude,
/// stored little-endian as (lo, hi). This is what MULT1/ADD return and what the orientation
/// vectors are made of (a unit vector's components sum in quadrature to 256).</item>
/// <item><b>Sign-magnitude 24-bit</b>: a sign byte with the sign in bit 7, followed by hi and lo
/// magnitude bytes. All positions in the local bubble use this.</item>
/// </list>
///
/// The ports below transcribe the 6502 instruction sequences instruction by instruction, including
/// their carries, truncations and approximations, because those quirks are part of the game's feel.
/// Each method names the original routine it ports so it can be audited against the source.
/// </remarks>
public static class EliteMath
{
    /// <summary>The value that represents 1.0 in Elite's 8-bit ratio units (TIS2's "96").</summary>
    public const int Unit = 256;

    private static readonly byte[] SineTable = BuildSineTable();

    private static byte[] BuildSineTable()
    {
        // SNE: 32 entries, sin(segment / 64 * 2pi) * 256, rounded with BeebAsm's INT(x + 0.5).
        var table = new byte[32];
        for (int i = 0; i < 32; i++)
        {
            double n = Math.Abs(Math.Sin(i / 64.0 * 2 * Math.PI));
            table[i] = n >= 1 ? (byte)255 : (byte)(int)(256 * n + 0.5);
        }

        return table;
    }

    // ---------------------------------------------------------------------------------------------
    // Sign-magnitude helpers
    // ---------------------------------------------------------------------------------------------

    /// <summary>Low byte of a sign-magnitude 16-bit value.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte Lo(ushort value) => (byte)value;

    /// <summary>High byte of a sign-magnitude 16-bit value (bit 7 is the sign).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte Hi(ushort value) => (byte)(value >> 8);

    /// <summary>Packs (hi, lo) into a sign-magnitude 16-bit value.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort Pack(byte hi, byte lo) => (ushort)((hi << 8) | lo);

    /// <summary>Magnitude of a sign-magnitude 16-bit value (0..32767).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Magnitude(ushort value) => ((value >> 8) & 0x7F) << 8 | (value & 0xFF);

    /// <summary>True if the sign bit of a sign-magnitude 16-bit value is set.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNegative(ushort value) => (value & 0x8000) != 0;

    /// <summary>Converts a sign-magnitude 16-bit value to a signed integer.</summary>
    public static int ToSigned(ushort value) => IsNegative(value) ? -Magnitude(value) : Magnitude(value);

    /// <summary>Converts a signed integer to sign-magnitude 16-bit, clamping to the 15-bit range.</summary>
    public static ushort FromSigned(int value)
    {
        int magnitude = Math.Abs(value);
        if (magnitude > 0x7FFF)
        {
            magnitude = 0x7FFF;
        }

        return (ushort)(magnitude | (value < 0 ? 0x8000 : 0));
    }

    /// <summary>Magnitude of a sign-magnitude 8-bit value (0..127).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Magnitude8(byte value) => value & 0x7F;

    /// <summary>Negates a sign-magnitude 8-bit value (the 6502's <c>EOR #%10000000</c>).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte Negate8(byte value) => (byte)(value ^ 0x80);

    // ---------------------------------------------------------------------------------------------
    // SNE sine table
    // ---------------------------------------------------------------------------------------------

    /// <summary>SNE: sin of a segment, scaled by 256. There are 64 segments in a circle.</summary>
    public static byte SinSegment(int segment) => SineTable[segment & 31];

    /// <summary>SNE read as a cosine: sin(segment + 16).</summary>
    public static byte CosSegment(int segment) => SineTable[(segment + 16) & 31];

    // ---------------------------------------------------------------------------------------------
    // MULT1 / ADD / MAD
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// MULT1: (A P) = Q * A for two sign-magnitude 8-bit arguments. The 6502 routine is an exact
    /// shift-and-add multiply, so the result is the exact product of the magnitudes carrying the
    /// sign of Q EOR A, in sign-magnitude 16-bit form.
    /// </summary>
    public static ushort Mult1(byte q, byte a)
    {
        byte p = (byte)((a & 0x7F) >> 1);
        bool carry = (a & 0x01) != 0;
        byte t = (byte)((q ^ a) & 0x80);
        byte absQ = (byte)(q & 0x7F);
        if (absQ == 0)
        {
            return 0; // mu10: (A P) = (0 0)
        }

        byte t1 = (byte)(absQ - 1); // |Q| - 1, as the carry is always set for the ADC
        byte acc = 0;

        for (int i = 0; i < 7; i++)
        {
            if (carry)
            {
                int sum = acc + t1 + 1; // ADC T1 with the carry set
                acc = (byte)sum;
                carry = sum > 0xFF;
            }

            // ROR A
            bool nextCarry = (acc & 0x01) != 0;
            acc = (byte)((acc >> 1) | (carry ? 0x80 : 0x00));
            carry = nextCarry;

            // ROR P
            nextCarry = (p & 0x01) != 0;
            p = (byte)((p >> 1) | (carry ? 0x80 : 0x00));
            carry = nextCarry;
        }

        // LSR A / ROR P, as we only pushed 7 bits through the loop
        carry = (acc & 0x01) != 0;
        acc = (byte)(acc >> 1);
        p = (byte)((p >> 1) | (carry ? 0x80 : 0x00));

        return Pack((byte)(acc | t), p);
    }

    /// <summary>
    /// ADD: (A X) = (A P) + (S R), a 16-bit sign-magnitude addition.
    /// </summary>
    public static ushort Add(byte a, byte p, byte s, byte r)
    {
        byte t1 = a;
        byte t = (byte)(a & 0x80);

        if (((a ^ s) & 0x80) == 0)
        {
            // Same signs, so add the magnitudes
            int lowSum = p + r;
            byte x = (byte)lowSum;
            int highSum = s + t1 + (lowSum > 0xFF ? 1 : 0);
            return Pack((byte)((byte)highSum | t), x);
        }

        // Different signs, so subtract the smaller magnitude from the larger
        byte u = (byte)(s & 0x7F);
        int lowDiff = p - r;
        byte xOut = (byte)lowDiff;
        int borrowLow = lowDiff < 0 ? 1 : 0;
        int highDiff = (t1 & 0x7F) - u - borrowLow;

        if (highDiff >= 0)
        {
            // |A| >= |S|, so the subtraction was the right way round and the sign is A's
            return Pack((byte)((byte)highDiff ^ t), xOut);
        }

        // |A| < |S|, so we need to negate the result using two's complement
        byte stored = (byte)highDiff;             // STA U
        int negLow = (~xOut & 0xFF) + 1;          // EOR #&FF / ADC #1 (with C clear)
        byte negLowByte = (byte)negLow;
        bool carryOut = negLow > 0xFF;
        int negHigh = 0 - stored - (carryOut ? 0 : 1); // LDA #0 / SBC U
        byte negHighByte = (byte)((byte)negHigh | 0x80);
        return Pack((byte)(negHighByte ^ t), negLowByte);
    }

    /// <summary>
    /// MAD: (A X) = Q * A + (S R).
    /// </summary>
    public static ushort Mad(byte q, byte a, byte s, byte r)
    {
        ushort product = Mult1(q, a);
        return Add(Hi(product), Lo(product), s, r);
    }

    // ---------------------------------------------------------------------------------------------
    // MU11 / SQUA / SQUA2
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// MU11: (A P) = P * X for two unsigned bytes, returning the 16-bit product.
    /// </summary>
    public static ushort Mu11(byte p, byte x)
    {
        byte t = (byte)(x - 1);
        byte acc = 0;
        bool carry = (p & 0x01) != 0;
        p = (byte)(p >> 1);

        for (int i = 0; i < 8; i++)
        {
            if (carry)
            {
                int sum = acc + t + 1; // ADC T, with the carry set
                acc = (byte)sum;
                carry = sum > 0xFF;
            }

            // ROR A / ROR P
            bool nextCarry = (acc & 0x01) != 0;
            acc = (byte)((acc >> 1) | (carry ? 0x80 : 0x00));
            carry = nextCarry;

            nextCarry = (p & 0x01) != 0;
            p = (byte)((p >> 1) | (carry ? 0x80 : 0x00));
            carry = nextCarry;
        }

        return Pack(acc, p);
    }

    /// <summary>
    /// SQUA2: (A P) = A * A for an unsigned byte A.
    /// </summary>
    public static ushort Squa2(byte a) => Mu11(a, a);

    /// <summary>
    /// SQUA: (A P) = |A| * |A|, ignoring the sign of A.
    /// </summary>
    public static ushort Squa(byte a) => Squa2((byte)(a & 0x7F));

    // ---------------------------------------------------------------------------------------------
    // FMLTU
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// FMLTU: A = A * Q / 256 for two unsigned bytes, i.e. the high byte of the product.
    /// </summary>
    public static byte Fmltu(byte a, byte q)
    {
        a = (byte)~a;
        byte p = (byte)((a >> 1) | 0x80);
        bool carry = (a & 0x01) != 0; // ROR shifts out bit 0 of the complemented A
        byte acc = 0;

        while (true)
        {
            if (carry)
            {
                // MU7: just shift, pushing a 0 into bit 7
                acc = (byte)(acc >> 1);
            }
            else
            {
                int sum = acc + q; // ADC Q with the carry clear
                bool sumCarry = sum > 0xFF;
                acc = (byte)sum;

                // ROR A
                bool nextCarry = (acc & 0x01) != 0;
                acc = (byte)((acc >> 1) | (sumCarry ? 0x80 : 0x00));
                _ = nextCarry;
            }

            carry = (p & 0x01) != 0; // LSR P
            p = (byte)(p >> 1);
            if (p == 0)
            {
                return acc;
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // TIS2 / TIS3 / DIVDT
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// TIS2: A = A / Q * 96, i.e. a division expressed with 96 (0x60) representing 1.0.
    /// Returns 96 with the sign of the argument if |A| >= Q.
    /// </summary>
    public static byte Tis2(byte a, byte q)
    {
        byte y = a;
        byte absA = (byte)(a & 0x7F);
        if (absA >= q)
        {
            return (byte)((y & 0x80) | 96); // TI4
        }

        byte t = 0xFE;
        bool carry;
        do
        {
            absA = (byte)(absA << 1); // ASL A (the carry out is discarded by CMP)
            if (absA >= q)
            {
                absA = (byte)(absA - q);
                carry = true;
            }
            else
            {
                carry = false;
            }

            bool nextCarry = (t & 0x80) != 0; // ROL T
            t = (byte)((t << 1) | (carry ? 0x01 : 0x00));
            carry = nextCarry;
        }
        while (carry);

        byte shifted = (byte)(t >> 2); // LSR A / LSR A / STA T
        carry = (shifted & 0x01) != 0; // LSR A
        int result = (shifted >> 1) + shifted + (carry ? 1 : 0); // ADC T
        return (byte)((y & 0x80) | (byte)result);
    }

    /// <summary>
    /// TIS3: A = -(nosev_1 * roofv_1 + nosev_2 * roofv_2) / nosev_3, where the three arguments
    /// select which component of each vector to use (0 = x, 2 = y, 4 = z, as byte offsets).
    /// The vectors and the scratch bytes are supplied by the caller so this stays a pure function.
    /// </summary>
    /// <param name="nosev">The ship's nosev, as 6 bytes starting at x_lo.</param>
    /// <param name="roofv">The ship's roofv, as 6 bytes starting at x_lo.</param>
    /// <param name="x">Index into the vectors for the first component (0, 2 or 4).</param>
    /// <param name="y">Index into the vectors for the second component (0, 2 or 4).</param>
    /// <param name="a">Index into the vectors for the divisor component (0, 2 or 4).</param>
    public static byte Tis3(ReadOnlySpan<byte> nosev, ReadOnlySpan<byte> roofv, int x, int y, int a)
    {
        byte q = nosev[x + 1];              // nosev_hi + X
        byte arg = roofv[x + 1];            // roofv_hi + X
        ushort product = Mult1(q, arg);     // MULT12: (S R) = Q * A
        byte s = Hi(product);
        byte r = Lo(product);

        q = nosev[y + 1];
        arg = roofv[y + 1];
        ushort sum = Mad(q, arg, s, r);     // (A X) = Q * A + (S R)
        byte pHi = Hi(sum);                 // EOR #%10000000, then fall into DIVDT
        byte pLo = Lo(sum);

        q = nosev[a + 1];
        pHi = (byte)(pHi ^ 0x80);
        return Divdt(pHi, pLo, q);
    }

    /// <summary>
    /// DIVDT: (P+1 A) = (A P) / Q, a 16-bit by 8-bit division. Only the low byte of the quotient
    /// (which ends up in P) is returned, matching the original routine.
    /// </summary>
    public static byte Divdt(byte a, byte p, byte q)
    {
        byte pHi = a;
        byte pLo = p;
        byte t = (byte)((a ^ q) & 0x80);

        // ASL P(1 0) / ROL P+1
        bool carry = (pLo & 0x80) != 0;
        pLo = (byte)(pLo << 1);
        pHi = (byte)((pHi << 1) | (carry ? 0x01 : 0x00));

        // ASL Q / LSR Q is just a way of clearing the carry flag; Q is unchanged
        carry = false;
        byte acc = 0;

        for (int i = 0; i < 16; i++)
        {
            // ROL A
            bool nextCarry = (acc & 0x80) != 0;
            acc = (byte)((acc << 1) | (carry ? 0x01 : 0x00));
            carry = nextCarry;

            // CMP Q / SBC Q
            if (acc >= q)
            {
                acc = (byte)(acc - q);
                carry = true;
            }
            else
            {
                carry = false;
            }

            // ROL P
            nextCarry = (pLo & 0x80) != 0;
            pLo = (byte)((pLo << 1) | (carry ? 0x01 : 0x00));
            carry = nextCarry;

            // ROL P+1
            nextCarry = (pHi & 0x80) != 0;
            pHi = (byte)((pHi << 1) | (carry ? 0x01 : 0x00));
            carry = nextCarry;
        }

        return (byte)(pLo | t);
    }

    /// <summary>
    /// DVID96: A = |A| / 96, using the high byte only, with the sign of the argument preserved.
    /// Used by TIS1 to divide the product of two orientation-vector high bytes back down.
    /// </summary>
    public static byte Dvid96(byte a)
    {
        byte t = (byte)(a & 0x80);
        byte absA = (byte)(a & 0x7F);
        byte t1 = 0xFE;
        bool carry;
        do
        {
            absA = (byte)(absA << 1); // ASL A (the carry out is discarded by CMP)
            if (absA >= 96)
            {
                absA = (byte)(absA - 96);
                carry = true;
            }
            else
            {
                carry = false;
            }

            bool nextCarry = (t1 & 0x80) != 0; // ROL T1
            t1 = (byte)((t1 << 1) | (carry ? 0x01 : 0x00));
            carry = nextCarry;
        }
        while (carry);

        return (byte)(t1 | t);
    }

    /// <summary>
    /// TIS1: A = (-X * A + (S R)) / 96, where X is an 8-bit sign-magnitude value.
    /// The result is the high byte of the multiply-accumulate divided by 96.
    /// </summary>
    public static byte Tis1(byte a, byte x, byte s, byte r)
    {
        byte q = x;
        ushort product = Mad(q, (byte)(a ^ 0x80), s, r);
        return Dvid96(Hi(product));
    }

    // ---------------------------------------------------------------------------------------------
    // MLTU2 / MVT6: 24-bit multiply and add, used to rotate positions
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// MLTU2: (A P+1 P) = (P+1 P) * Q, a 16-bit by 8-bit multiply with a 24-bit result.
    /// </summary>
    /// <param name="hi">
    /// The high byte of the 16-bit multiplicand, passed with its bits complemented for the low
    /// byte because the routine relies on the complement trick.
    /// </param>
    /// <param name="lo">The low byte of the multiplicand, with its bits complemented.</param>
    /// <param name="q">The multiplier.</param>
    /// <returns>
    /// The 24-bit result as (A, P+1, P), plus the carry flag the routine leaves behind. The
    /// original's callers rely on that carry being whatever the final rotate produced, so it is
    /// returned here rather than being treated as undefined.
    /// </returns>
    public static (byte A, byte PHi, byte PLo, bool Carry) Mltu2(byte hi, byte lo, byte q)
    {
        byte a = (byte)~hi;
        byte p = lo;

        // LSR A, with the carry holding the bit that falls off
        bool carry = (a & 0x01) != 0;
        a = (byte)(a >> 1);
        byte pHi = a;

        // ROR P, bringing in the carry from above
        bool nextCarry = (p & 0x01) != 0;
        p = (byte)((p >> 1) | (carry ? 0x80 : 0x00));
        carry = nextCarry;

        a = 0;
        for (int i = 0; i < 16; i++)
        {
            if (!carry)
            {
                int sum = a + q; // ADC Q with the carry clear
                bool sumCarry = sum > 0xFF;
                a = (byte)sum;

                // ROR A
                nextCarry = (a & 0x01) != 0;
                a = (byte)((a >> 1) | (sumCarry ? 0x80 : 0x00));
            }
            else
            {
                // MU21: LSR A
                nextCarry = (a & 0x01) != 0;
                a = (byte)(a >> 1);
            }

            // ROR P+1
            bool pHiCarry = (pHi & 0x01) != 0;
            pHi = (byte)((pHi >> 1) | (nextCarry ? 0x80 : 0x00));

            // ROR P
            nextCarry = (p & 0x01) != 0;
            p = (byte)((p >> 1) | (pHiCarry ? 0x80 : 0x00));
            carry = nextCarry;
        }

        return (a, pHi, p, carry);
    }

    /// <summary>
    /// MVT6: add the 24-bit value (A, P+2, P+1) to the coordinate at <paramref name="offset"/>,
    /// where A is the sign byte and (P+2, P+1) the magnitude.
    /// </summary>
    /// <param name="coordinate">A 24-bit coordinate as three bytes: lo, hi, sign.</param>
    /// <param name="offset">Offset of the coordinate's low byte.</param>
    /// <param name="a">The sign byte of the value to add.</param>
    /// <param name="p1">The low byte of the value to add; updated with the result.</param>
    /// <param name="p2">The high byte of the value to add; updated with the result.</param>
    /// <returns>The sign byte of the result.</returns>
    public static byte Mvt6(Span<byte> coordinate, int offset, byte a, ref byte p1, ref byte p2)
    {
        // Bit 7 of A is the sign of the value in (P+2, P+1); bits 0-6 are preserved in the result.
        bool coordinateNegative = (coordinate[offset + 2] & 0x80) != 0;
        bool deltaNegative = (a & 0x80) != 0;

        byte sign = (byte)((a & 0x7F) | (coordinateNegative ? 0x80 : 0x00));

        // The coordinate is 23-bit sign-magnitude and the delta is 16-bit, so the result needs all 23
        // bits. This used to take the coordinate's low 16 bits as one value and add the delta to it,
        // which is right up to 65535 and wrong from there: adding 1000 to 70000 gave 5464, because the
        // carry out of bit 16 had nowhere to go and the sign byte was left alone.
        //
        // That was the reported wobble. A coordinate above 65535 absorbs the addition incorrectly, so a
        // body 500 units off to one side spiralled from an xy-radius of 500 down to 94 over a single
        // turn, where a rotation must preserve length. Measured on this primitive directly: every case
        // below 65536 is exact and every case at or above it was wrong.
        long magnitude = coordinate[offset]
            | ((long)coordinate[offset + 1] << 8)
            | ((long)(coordinate[offset + 2] & 0x7F) << 16);
        long delta = p1 | ((long)p2 << 8);

        // Magnitudes add when the signs agree and subtract when they differ, which is what sign
        // magnitude means; the smaller is taken from the larger and keeps its sign.
        long result = coordinateNegative == deltaNegative
            ? magnitude + delta
            : magnitude - delta;

        if (result < 0)
        {
            result = -result;
            sign ^= 0x80;
        }

        coordinate[offset] = (byte)(result & 0xFF);
        coordinate[offset + 1] = (byte)((result >> 8) & 0xFF);
        sign = (byte)((sign & 0x80) | ((result >> 16) & 0x7F));

        p1 = coordinate[offset];
        p2 = coordinate[offset + 1];
        return sign;
    }

    // ---------------------------------------------------------------------------------------------
    // LL5: square root
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// LL5: Q = SQRT(R Q), an 8-bit restoring square root of the 16-bit value (R Q).
    /// </summary>
    public static byte Sqrt(byte r, byte q)
    {
        byte y = r;
        byte s = q;
        byte x = 0;
        byte result = 0;
        int t = 8;

        do
        {
            bool carry;
            if (x < result)
            {
                carry = false;
            }
            else if (x > result)
            {
                carry = true;
            }
            else
            {
                carry = y >= 64; // CPY #64
            }

            if (carry)
            {
                // LL8: subtract 64 from the dividend and Q from the remainder, carrying any
                // borrow from the first subtraction into the second
                int yDiff = y - 64;
                bool subtractCarry = yDiff >= 0;
                y = (byte)yDiff;
                int xDiff = x - result - (subtractCarry ? 0 : 1);
                carry = xDiff >= 0;
                x = (byte)xDiff;
            }

            // ROL Q, with the carry set by the comparison above
            bool carryIn = carry;
            carry = (result & 0x80) != 0;
            result = (byte)((result << 1) | (carryIn ? 0x01 : 0x00));

            // ASL S / TYA / ROL A / TAY / TXA / ROL A / TAX (twice over)
            carry = (s & 0x80) != 0;
            s = (byte)(s << 1);
            bool yCarry = (y & 0x80) != 0;
            y = (byte)((y << 1) | (carry ? 0x01 : 0x00));
            carry = yCarry;
            x = (byte)((x << 1) | (carry ? 0x01 : 0x00));

            carry = (s & 0x80) != 0;
            s = (byte)(s << 1);
            yCarry = (y & 0x80) != 0;
            y = (byte)((y << 1) | (carry ? 0x01 : 0x00));
            carry = yCarry;
            x = (byte)((x << 1) | (carry ? 0x01 : 0x00));

            t--;
        }
        while (t != 0);

        return result;
    }
}
