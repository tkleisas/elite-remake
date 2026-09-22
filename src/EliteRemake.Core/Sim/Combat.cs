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
        _ => 0,
    };

    /// <summary>
    /// How many frames a laser must wait between shots. The original stores the laser power masked
    /// with %11111010 in LASCT and refuses to fire while it is non-zero, which gives a pulse laser
    /// a ten-frame gap and lets beam and military lasers fire continuously.
    /// </summary>
    public static int FireInterval(LaserType type) => type switch
    {
        LaserType.Pulse => LaserRawByte(LaserType.Pulse) & 0b11111010,
        _ => 0,
    };

    /// <summary>The raw byte the original stores in the commander's LASER array for a laser type.</summary>
    public static int LaserRawByte(LaserType type) => type switch
    {
        LaserType.Pulse => PulseLaserPower,
        LaserType.Beam => 128 + BeamLaserPower,
        LaserType.Military => 128 + MilitaryLaserPower,
        _ => 0,
    };

    /// <summary>
    /// HITCH: whether a ship is in our crosshairs. The original requires the ship to be in front of
    /// us, to be a ship rather than the planet or sun, not to be exploding, to be within 256 units
    /// of the centre line, and to fall inside the targetable area from its blueprint.
    /// </summary>
    public static bool IsInCrosshairs(Ship ship, int targetableArea)
    {
        if (ship.GetCoordinate(ShipDataBlock.Z) <= 0)
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

        int x = ship.GetCoordinate(ShipDataBlock.X);
        int y = ship.GetCoordinate(ShipDataBlock.Y);
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
