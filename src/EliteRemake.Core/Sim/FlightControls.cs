using EliteRemake.Core.Maths;

namespace EliteRemake.Core.Sim;

/// <summary>
/// The flight controls: the roll and pitch rates, and the angles they produce.
/// </summary>
/// <remarks>
/// The original keeps the roll rate in JSTX and the pitch rate in JSTY, both as a single byte in
/// which 128 is the centre (no rate), values above 128 roll or pitch one way and values below it
/// the other. Holding a key bumps the rate by a fixed amount (7 for roll, 14 for pitch), releasing
/// it lets the rate creep back to the centre (the keyboard damping in <c>cntr</c>), and pressing
/// the opposite key recentres the rate immediately when keyboard auto-recentre is enabled. The
/// rates are then converted into the roll angle alpha and pitch angle beta that MVEIT applies to
/// the universe. This class ports that whole chain.
/// </remarks>
public static class FlightControls
{
    /// <summary>JSTX/JSTY value that means "no roll or pitch".</summary>
    public const byte Centre = 128;

    /// <summary>How much the roll rate changes per frame while a roll key is held.</summary>
    public const byte RollStep = 7;

    /// <summary>How much the pitch rate changes per frame while a pitch key is held.</summary>
    public const byte PitchStep = 14;

    /// <summary>
    /// BUMP2: increase the rate in <paramref name="x"/> by <paramref name="a"/>, clamping at the
    /// ends of the range. With auto-recentre enabled, a rate that ends up on the far side of the
    /// centre snaps to the centre, so one tap of the opposite key stops a roll.
    /// </summary>
    public static byte Bump2(byte x, byte a, bool autoRecentre)
    {
        int sum = x + a;
        byte result = sum > 0xFF ? (byte)255 : (byte)sum;

        if (result >= 128)
        {
            return result;
        }

        return autoRecentre ? Centre : result;
    }

    /// <summary>
    /// REDU2: decrease the rate in <paramref name="x"/> by <paramref name="a"/>, clamping at the
    /// ends of the range, with the same recentring behaviour as <see cref="Bump2"/>.
    /// </summary>
    public static byte Redu2(byte x, byte a, bool autoRecentre)
    {
        int difference = x - a;
        byte result = difference < 0 ? (byte)1 : (byte)difference;

        if (result < 128)
        {
            return result;
        }

        return autoRecentre ? Centre : result;
    }

    /// <summary>
    /// The roll angle alpha: the magnitude in ALP1 and the two sign flags the original keeps in
    /// ALP2 (the sign of the angle) and ALP2+1 (the sign of the rate).
    /// </summary>
    public static (byte Alp1, byte Alp2, byte Alp2Flipped) RollAngle(byte jstx, bool dampingDisabled = false)
    {
        // The original applies the keyboard damping twice for roll
        byte x = ShipMath.Cntr(ShipMath.Cntr(jstx, dampingDisabled), dampingDisabled);

        byte a = (byte)(x ^ 0x80);
        byte alp2 = (byte)(a & 0x80);           // ALP2
        byte alp2Flipped = (byte)((a ^ 0x80) & 0x80); // ALP2+1

        if ((a & 0x80) != 0)
        {
            // Change the sign with two's complement so that A is now positive
            a = (byte)((~a + 1) & 0xFF);
        }

        a = (byte)(a >> 2);
        if (a < 8)
        {
            a = (byte)(a >> 1);
        }

        return (a, alp2, alp2Flipped);
    }

    /// <summary>
    /// The pitch angle beta: the magnitude in BET1 and the sign flags BET2 and BET2+1.
    /// </summary>
    public static (byte Bet1, byte Bet2, byte Bet2Flipped) PitchAngle(byte jsty, bool dampingDisabled = false)
    {
        byte x = ShipMath.Cntr(jsty, dampingDisabled);

        byte a = (byte)(x ^ 0x80);
        byte bet2Flipped = (byte)(a & 0x80);            // BET2+1
        byte bet2 = (byte)((a ^ 0x80) & 0x80);          // BET2

        if ((a & 0x80) != 0)
        {
            a = (byte)(~a & 0xFF);
        }

        // Add 4 before dividing by 16, so a full deflection gives a pitch angle of 8
        a = (byte)(a + 4 + 1); // the original adds with the carry set
        a = (byte)(a >> 4);
        if (a < 3)
        {
            a = (byte)(a >> 1);
        }

        return (a, bet2, bet2Flipped);
    }

    /// <summary>Applies the roll keys to the roll rate.</summary>
    public static byte ApplyRollKeys(byte jstx, bool rollLeft, bool rollRight, bool autoRecentre)
    {
        if (rollLeft)
        {
            jstx = Bump2(jstx, RollStep, autoRecentre);
        }

        if (rollRight)
        {
            jstx = Redu2(jstx, RollStep, autoRecentre);
        }

        return jstx;
    }

    /// <summary>Applies the pitch keys to the pitch rate.</summary>
    public static byte ApplyPitchKeys(byte jsty, bool pitchUp, bool pitchDown, bool autoRecentre)
    {
        if (pitchDown)
        {
            jsty = Redu2(jsty, PitchStep, autoRecentre);
        }

        if (pitchUp)
        {
            jsty = Bump2(jsty, PitchStep, autoRecentre);
        }

        return jsty;
    }
}
