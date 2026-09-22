using EliteRemake.Core.Maths;

namespace EliteRemake.Core.Sim;

/// <summary>
/// The ship AI: what the other ships in the bubble decide to do about us.
/// </summary>
/// <remarks>
/// The original's TACTICS routine runs for every ship in the bubble. It decides whether the ship
/// wants to fight (a random byte with bit 7 set, compared against the ship's AI flag: if the random
/// value wins, the ship is peaceful and heads away), then steers towards or away from us by setting
/// the ship's roll and pitch counters, and finally fires if we are in its line of fire.
///
/// The steering uses the same trick the player's controls do: setting the counters makes MVEIT's
/// rotation routine turn the ship, so ships manoeuvre with exactly the same maths we do. The turn
/// rate is the original's RAT of 3, with RAT2 of 4 as the threshold below which no turn is applied.
///
/// The aim test is expressed as an angle rather than the original's dot-product byte. The original
/// compares the low byte of the dot product against 160, which is not a comparison that survives
/// being read out of context; what it means in practice is "the target is within a few degrees of
/// the nose", which is what this implementation tests directly.
/// </remarks>
public static class Tactics
{
    /// <summary>The magnitude the pitch and roll counters are set to when turning (the original's RAT).</summary>
    public const int TurnRate = 3;

    /// <summary>The aim error below which no turn is applied (the original's RAT2).</summary>
    public const int TurnThreshold = 4;

    /// <summary>The distance within which a ship will open fire: the original requires x_hi, y_hi
    /// and z_hi all to be below 32.</summary>
    public const int FireRange = 32 * 256;

    /// <summary>The cosine of the angle within which a ship considers its aim true.</summary>
    public const double AimCosine = 0.95;

    /// <summary>
    /// TA5: decides whether a ship wants to attack us. The original takes a random byte, sets bit 7
    /// so the comparison always has the high bit set, and compares it against the ship's AI flag:
    /// if the random value is greater than or equal to the flag, the ship is peaceful.
    /// </summary>
    public static bool WantsToAttack(Ship ship, EliteRandom random)
    {
        int roll = random.Next() | 0x80;
        return roll < ship.AiFlag;
    }

    /// <summary>
    /// Runs the AI for one ship. Returns true if the ship hit us with its laser this frame.
    /// </summary>
    /// <param name="ship">The ship whose turn it is.</param>
    /// <param name="random">The random number generator.</param>
    /// <param name="player">Our ship, so the AI can damage it.</param>
    /// <param name="laserPower">The ship's laser power, from its blueprint; zero means no laser.</param>
    /// <param name="damage">
    /// The damage the ship inflicts when it hits, which the original takes from byte #19 of its
    /// blueprint halved: that byte holds the laser power and missile count together, and halving it
    /// is what the original actually does.
    /// </param>
    public static bool Apply(Ship ship, EliteRandom random, Ship player, int laserPower, int damage)
    {
        if (ship.IsExploding || ship.IsKilled || !IsUnderPilotControl(ship.Type))
        {
            return false;
        }

        bool attack = WantsToAttack(ship, random);

        // Aim at us if we are the target, otherwise turn away
        (int x, int y, int z) = ship.GetPosition();
        double distance = Math.Sqrt(((double)x * x) + ((double)y * y) + ((double)z * z));
        if (distance < 1)
        {
            return false;
        }

        // The direction from the ship to us, expressed in the ship's own frame
        var toUs = new System.Numerics.Vector3((float)-x, (float)-y, (float)-z) / (float)distance;
        System.Numerics.Vector3 nose = Unit(ship.Orientation, Orientation.Nosev);
        System.Numerics.Vector3 roof = Unit(ship.Orientation, Orientation.Roofv);
        System.Numerics.Vector3 side = Unit(ship.Orientation, Orientation.Sidev);

        if (!attack)
        {
            // Peaceful ships turn away rather than towards us
            toUs = -toUs;
        }

        float aimX = System.Numerics.Vector3.Dot(toUs, side);
        float aimY = System.Numerics.Vector3.Dot(toUs, roof);
        float aimZ = System.Numerics.Vector3.Dot(toUs, nose);

        // Roll towards the target if it is off to one side, pitch if it is above or below. The
        // counters are sign-magnitude bytes, and the original gives them the opposite sign to the
        // dot product: as TACTICS puts it, "set the ship's pitch counter to 3, with the opposite
        // sign to the dot product result".
        byte roll = (byte)((aimX > 0 ? 0x80 : 0x00) | (Math.Abs(aimX) > 0.02 ? TurnRate : 0));
        byte pitch = (byte)((aimY > 0 ? 0x80 : 0x00) | (Math.Abs(aimY) > 0.02 ? TurnRate : 0));
        ship.Data[ShipDataBlock.RollCounter] = roll;
        ship.Data[ShipDataBlock.PitchCounter] = pitch;

        // Fire if we are close, roughly ahead, and the ship has a laser
        if (laserPower > 0 && aimZ > AimCosine && distance < FireRange)
        {
            // The original reads the attacker's z_sign to decide which of our shields was hit
            bool fromBehind = z < 0;
            return Combat.TakeDamage(player, damage, fromBehind);
        }

        return false;
    }

    /// <summary>
    /// Whether a ship has a pilot who can decide to manoeuvre. Missiles, cargo, asteroids, escape
    /// pods and the space station itself do not fly themselves, so the AI leaves them alone; the
    /// station in particular must stay exactly where it is.
    /// </summary>
    public static bool IsUnderPilotControl(int shipType) => shipType switch
    {
        1 => false,  // missile
        2 => false,  // Coriolis space station
        3 => false,  // escape pod
        4 => false,  // alloy plate
        5 => false,  // cargo canister
        6 => false,  // boulder
        7 => false,  // asteroid
        8 => false,  // splinter
        15 => false, // unused slot
        >= 128 => false, // planet and sun
        _ => true,
    };

    /// <summary>Reads one of a ship's orientation vectors as a unit vector.</summary>
    private static System.Numerics.Vector3 Unit(Orientation orientation, int vector)
    {
        var value = new System.Numerics.Vector3(
            (float)orientation.GetUnity(vector, Orientation.X),
            (float)orientation.GetUnity(vector, Orientation.Y),
            (float)orientation.GetUnity(vector, Orientation.Z));

        return value.LengthSquared() > 0 ? System.Numerics.Vector3.Normalize(value) : new System.Numerics.Vector3(0, 0, 1);
    }

}
