namespace EliteRemake.Core.Sim;

/// <summary>
/// Lasers: what they cost, what they damage, and whether they are on target.
/// </summary>
/// <remarks>
/// The original's laser power values are the bytes it stores in the commander's LASER array: a
/// pulse laser is 15, a beam laser is 15 with bit 7 set, a military laser is 23 with bit 7 set and
/// a mining laser is 50. Only the low seven bits are the damage, so a pulse laser and a beam laser
/// hurt equally per shot — what differs is the rate. Pulse lasers can only fire again after their
/// power (masked with %11111010) frames have passed, while beam and military lasers fire every
/// frame, which is why they melt through a target but overheat so quickly.
/// </remarks>
public static class Combat
{
    /// <summary>The space station's ship type, which an energy bomb cannot touch.</summary>
    public const int SpaceStationType = 2;

    /// <summary>How many frames the energy bomb's effect lasts.</summary>
    public const int EnergyBombFrames = 8;


    /// <summary>The damage of a pulse laser (the original's POW).</summary>
    public const int PulseLaserPower = 15;

    /// <summary>The damage of a beam laser, which is a pulse laser with bit 7 set.</summary>
    public const int BeamLaserPower = 15;

    /// <summary>The damage of a military laser (the original's Armlas = 128 + 23).</summary>
    public const int MilitaryLaserPower = 23;

    /// <summary>The damage of a mining laser (the original's Mlas), good against asteroids.</summary>
    public const int MiningLaserPower = 50;

    /// <summary>The temperature at which a laser overheats and stops firing.</summary>
    public const int OverheatTemperature = 242;

    /// <summary>How much a shot raises the laser temperature.</summary>
    public const int HeatPerShot = 8;

    /// <summary>How much the laser cools each frame.</summary>
    public const int CoolingPerFrame = 1;

    /// <summary>How much energy a shot costs.</summary>
    public const int EnergyPerShot = 1;

    /// <summary>The damage a laser type does per shot.</summary>
    public static int Power(LaserType type) => type switch
    {
        LaserType.Pulse => PulseLaserPower,
        LaserType.Beam => BeamLaserPower,
        LaserType.Military => MilitaryLaserPower,
        LaserType.Mining => MiningLaserPower,
        _ => 0,
    };

    /// <summary>
    /// How long a laser must wait between shots, in ticks of the original's fifty-hertz clock.
    /// </summary>
    /// <remarks>
    /// The original stores the laser power masked with %11111010 in LASCT and refuses to fire while
    /// it is non-zero, which gives a pulse laser a ten-tick gap and lets beam and military lasers
    /// fire continuously. **LASCT is not a main loop counter**: it is decremented in LINSCN, which
    /// runs on the vertical sync, "50 times a second" — so a pulse laser fires every 10/50 of a
    /// second, five times a second, however fast or slow the flight loop is running. The Electron
    /// version makes the same adaptation in its own source, decrementing it "by 4 on each iteration
    /// around the main game loop" because its loop is a quarter of the sync rate.
    /// </remarks>
    public static int FireInterval(LaserType type) => type switch
    {
        // A pulse laser's power is 15, which masks to 10 ticks — five shots a second. A mining
        // laser's 50 masks to 50, so it fires once a second, which is the slow, deliberate tool the
        // original makes it.
        LaserType.Pulse => LaserRawByte(LaserType.Pulse) & 0b11111010,
        LaserType.Mining => LaserRawByte(LaserType.Mining) & 0b11111010,
        _ => 0,
    };

    /// <summary>
    /// How many ticks of the original's fifty-hertz clock pass in one iteration of the main loop.
    /// </summary>
    /// <remarks>
    /// Fifty hertz divided by the loop rate. LASCT counts these ticks, so a counter that is spent one
    /// per iteration — as ours was — makes a pulse laser fire four times too slowly.
    /// </remarks>
    public const int LaserTicksPerIteration = 4;

    /// <summary>The raw byte the original stores in the commander's LASER array for a laser type.</summary>
    public static int LaserRawByte(LaserType type) => type switch
    {
        LaserType.Pulse => PulseLaserPower,
        LaserType.Beam => 128 + BeamLaserPower,
        LaserType.Military => 128 + MilitaryLaserPower,
        LaserType.Mining => MiningLaserPower,
        _ => 0,
    };

    /// <summary>
    /// HITCH: whether a ship is in our crosshairs. The original requires the ship to be in front of
    /// us, to be a ship rather than the planet or sun, not to be exploding, to be within 256 units
    /// of the centre line, and to fall inside the targetable area from its blueprint.
    /// </summary>
    /// <param name="targetableArea">The area from the ship's blueprint.</param>
    /// <param name="view">
    /// The view we are looking through. The original flips each ship to the current view before it
    /// calls HITCH, so "in front of us" means in front of the window we are looking through: the
    /// rear laser hits what is behind the ship.
    /// </param>
    public static bool IsInCrosshairs(Ship ship, int targetableArea, SpaceView view = SpaceView.Front)
    {
        (int x, int y, int z) = ship.GetPosition();
        (x, y, z) = Plut.Position(view, x, y, z);

        if (z <= 0)
        {
            return false; // behind us
        }

        if (ship.Type < 0 || FlightSim.IsCelestial(ship.Type))
        {
            return false; // the planet or the sun
        }

        if (ship.IsExploding || ship.IsKilled)
        {
            return false;
        }

        if (Math.Abs(x) >= 256 || Math.Abs(y) >= 256)
        {
            return false; // too far off the centre line
        }

        return ((x * x) + (y * y)) <= targetableArea;
    }

    /// <summary>
    /// OOPS: takes damage on our ship, with the shields absorbing it before the energy banks.
    /// </summary>
    /// <remarks>
    /// The original decides which shield was hit from the attacker's z_sign: an attacker behind us
    /// has a negative z, so its fire lands on the aft shield. If the shield cannot absorb the whole
    /// hit, it drops to zero and the remainder comes off the energy banks — and if the energy banks
    /// cannot absorb that, we die.
    /// </remarks>
    /// <param name="ship">Our ship.</param>
    /// <param name="damage">The damage to take.</param>
    /// <param name="fromBehind">True if the attacker is behind us.</param>
    /// <returns>True if the damage destroyed us.</returns>
    public static bool TakeDamage(Ship ship, int damage, bool fromBehind)
    {
        if (damage <= 0)
        {
            return false;
        }

        int shield = fromBehind ? ship.AftShield : ship.ForeShield;
        if (shield >= damage)
        {
            if (fromBehind)
            {
                ship.AftShield = (byte)(shield - damage);
            }
            else
            {
                ship.ForeShield = (byte)(shield - damage);
            }

            return false;
        }

        // The shields are overwhelmed, so they drop to zero and the rest hits the energy banks
        int overflow = damage - shield;
        if (fromBehind)
        {
            ship.AftShield = 0;
        }
        else
        {
            ship.ForeShield = 0;
        }

        if (ship.Energy > overflow)
        {
            ship.Energy = (byte)(ship.Energy - overflow);
            return false;
        }

        // Our energy levels are either zero or negative, so we have died
        ship.Energy = 0;
        return true;
    }

    /// <summary>
    /// Recharges the shields from the energy banks, as the original's SHD routine does: each shield
    /// gains a point and the energy banks pay for it, but only once the banks are above half full.
    /// </summary>
    public static void RechargeShields(Ship ship)
    {
        if ((ship.Energy & 0x80) == 0)
        {
            return; // the original only charges the shields above 50% energy
        }

        if (ship.AftShield < 255)
        {
            ship.AftShield++;
            if (ship.Energy > 0)
            {
                ship.Energy--;
            }
        }

        if (ship.ForeShield < 255)
        {
            ship.ForeShield++;
            if (ship.Energy > 0)
            {
                ship.Energy--;
            }
        }
    }

    /// <summary>
    /// Recharges the energy banks. The original adds ENGY + 1 a frame, so an energy unit doubles
    /// the rate.
    /// </summary>
    public static void RechargeEnergy(Ship ship)
    {
        int rate = ship.HasEnergyUnit ? 2 : 1;
        ship.Energy = (byte)Math.Min(255, ship.Energy + rate);
    }

    /// <summary>
    /// The ship type of the Constrictor, which the disc protects from everything but a military
    /// laser.
    /// </summary>
    public const int ConstrictorType = 31;

    /// <summary>
    /// The damage a laser shot does to a particular ship.
    /// </summary>
    /// <remarks>
    /// This is the laser's power for every ship but one. On the disc, the Constrictor can only be
    /// harmed by a military laser, and then for a quarter of the damage:
    ///
    /// <code>
    /// CPY #CON / BNE BURN           \ only the Constrictor is special
    /// LDA LAS / CMP #(Armlas AND 127) / BNE MA8   \ only a military laser
    /// LSR LAS / LSR LAS             \ a quarter of the damage
    /// </code>
    ///
    /// The other versions except the disc take the same branch, so this is not disc-only; what is
    /// disc-only is the <c>CPY #CON</c> above it, which is why the rule is written here per target.
    /// A pulse or beam laser therefore does nothing at all to the Constrictor, which makes the
    /// mission's target genuinely harder rather than merely tough.
    /// </remarks>
    public static int DamageAgainst(LaserType laser, int power, Ship target)
    {
        if (target.Type != ConstrictorType)
        {
            return power;
        }

        return laser == LaserType.Military ? power / 4 : 0;
    }

    /// <summary>
    /// Applies a laser hit to a ship, returning true if the hit destroyed it.
    /// </summary>
    public static bool ApplyHit(Ship target, int power)
    {
        int energy = target.Energy - power;
        if (energy > 0)
        {
            target.Energy = (byte)energy;
            return false;
        }

        target.Energy = 0;
        return true;
    }
}
