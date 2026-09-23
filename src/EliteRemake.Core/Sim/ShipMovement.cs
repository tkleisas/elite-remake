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
    /// <remarks>
    /// <para>
    /// This uses the same arithmetic as <see cref="RotateBodyLocationByOurPitchAndRoll"/> — the
    /// original's rotation written out in full precision — where the original works on two bytes.
    /// </para>
    /// <para>
    /// The original's own two-byte method was tried and **drifts**: with the ship's coordinates at
    /// (700, -400, 1800), a full turn of pitch left the distance at 1829 where it started at 1900, a
    /// wander of just under four percent, and one turn of roll spiralled a body from an xy-radius of
    /// 500 to 94. That was first blamed on <see cref="EliteMath.Mvt6"/> truncating the coordinate's
    /// high bits, and that fault was real and is fixed — but fixing it did not make the two-byte method
    /// rigid, so the drift is either in the method or in our port of it, and the two have not been told
    /// apart.
    /// </para>
    /// <para>
    /// A rendering that fills faces cannot show a body that drifts: a wireframe hides a few percent of
    /// positional error, and a solid body visibly does not stay where it should. The departure is
    /// therefore kept, and recorded as a departure rather than as a correction.
    /// </para>
    /// </remarks>
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
        byte bet2) =>
        RotateBodyLocationByOurPitchAndRoll(position, alp1, alp2, bet1, bet2);

    /// <summary>
    /// MVEIT part 8: rotate a ship about its own axes by its pitch and roll counters.
    /// </summary>
    /// <remarks>
    /// This is how every ship in the game turns, the player's controls included: the counters in
    /// bytes #29 and #30 give the direction of the turn, and each frame the ship is rotated by one
    /// small step for each of the three axes. The counter is decremented as it is used, so a
    /// counter of 3 turns the ship for three frames — which is why the AI refreshes it every frame
    /// it wants to keep turning.
    /// </remarks>
    /// <param name="orientation">The ship's orientation vectors.</param>
    /// <param name="data">The ship's data block, whose counters are updated.</param>
    public static void RotateShipAboutItself(Orientation orientation, Span<byte> data)
    {
        // Pitch: rotate roofv against nosev
        byte pitch = data[ShipDataBlock.PitchCounter];
        byte pitchSign = (byte)(pitch & 0x80);
        byte pitchMagnitude = (byte)(pitch & 0x7F);
        if (pitchMagnitude != 0)
        {
            // The counter counts down as it is used, but never past 127
            if (pitchMagnitude != 0x7F)
            {
                pitchMagnitude--;
            }

            data[ShipDataBlock.PitchCounter] = (byte)(pitchMagnitude | pitchSign);

            ShipMath.Mvs5(orientation, Orientation.Roofv + Orientation.X, Orientation.Nosev + Orientation.X, pitchSign);
            ShipMath.Mvs5(orientation, Orientation.Roofv + Orientation.Y, Orientation.Nosev + Orientation.Y, pitchSign);
            ShipMath.Mvs5(orientation, Orientation.Roofv + Orientation.Z, Orientation.Nosev + Orientation.Z, pitchSign);
        }

        // Roll: rotate roofv against sidev
        byte roll = data[ShipDataBlock.RollCounter];
        byte rollSign = (byte)(roll & 0x80);
        byte rollMagnitude = (byte)(roll & 0x7F);
        if (rollMagnitude != 0)
        {
            if (rollMagnitude != 0x7F)
            {
                rollMagnitude--;
            }

            data[ShipDataBlock.RollCounter] = (byte)(rollMagnitude | rollSign);

            ShipMath.Mvs5(orientation, Orientation.Roofv + Orientation.X, Orientation.Sidev + Orientation.X, rollSign);
            ShipMath.Mvs5(orientation, Orientation.Roofv + Orientation.Y, Orientation.Sidev + Orientation.Y, rollSign);
            ShipMath.Mvs5(orientation, Orientation.Roofv + Orientation.Z, Orientation.Sidev + Orientation.Z, rollSign);
        }
    }

    /// <summary>
    /// MVEIT part 4: apply a ship's acceleration to its speed, cap it at the ship's own maximum and
    /// clear the acceleration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// "LDA INWK+27 / CLC / ADC INWK+28" — the speed and the acceleration are added, the result is
    /// held at zero if it goes negative, and it is capped at byte #15 of the ship's blueprint, which
    /// is the fastest that ship may fly. The acceleration is then zeroed, because it is a one-off
    /// change: a ship that wants to keep accelerating has to ask again on its next pass through
    /// TACTICS, and TACTICS only runs for a ship every eighth iteration of the main loop.
    /// </para>
    /// <para>
    /// <b>This was missing altogether, and the acceleration byte was written and never read.</b>
    /// ANGRY sets a ship's acceleration to 2 when we shoot it — the original's way of making an
    /// angry ship come at us faster — and TACTICS sets it to 3 when a ship is lined up on its
    /// target and to -1 when it needs to turn. Every one of those writes went nowhere, so every ship
    /// in the sky flew at a constant speed for ever: nothing ever closed on us and nothing ever
    /// braked to turn, which is most of what makes the original's dogfights feel the way they do.
    /// </para>
    /// </remarks>
    public static void ApplyAcceleration(Ship ship)
    {
        // The acceleration is a signed byte: -1 is stored as &FF and the original adds it with an
        // ADC, so the arithmetic is the same as adding -1 to a speed of 1 or more
        int speed = ship.Speed + (sbyte)ship.Acceleration;

        if (speed < 0)
        {
            speed = 0;
        }

        if (ship.MaxSpeed > 0 && speed > ship.MaxSpeed)
        {
            speed = ship.MaxSpeed;
        }

        ship.Speed = (byte)Math.Min(255, speed);
        ship.Acceleration = 0;
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
