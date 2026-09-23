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
    /// How far ahead a ship must be for the close-range rule not to apply: the original tests
    /// <c>z_hi &gt;= 3</c>, so anything nearer than 3 * 256 units is "pretty close".
    /// </summary>
    public const int CloseRangeZ = 3 * 256;

    /// <summary>
    /// How far to the side a ship must be for the close-range rule not to apply. The original
    /// clears bit 0 of x_hi and y_hi and tests for zero, so the tests are <c>x_hi &lt; 2</c> and
    /// <c>y_hi &lt; 2</c>: 2 * 256 units on each axis.
    /// </summary>
    public const int CloseRangeXY = 2 * 256;

    /// <summary>
    /// TA5: decides whether a ship wants to attack us. The original takes a random byte, sets bit 7
    /// so the comparison always has the high bit set, and compares it against the ship's AI flag:
    /// if the random value is greater than or equal to the flag, the ship is peaceful.
    /// </summary>
    /// <remarks>
    /// The ship also has to be hostile, which is the NEWB bit the role rules rewrite. Without this
    /// the bit had no effect at all: <see cref="DecideRole"/> set it for a trader that turned out to
    /// be a pirate, and nothing read it, so the trader went on behaving like a trader while its flags
    /// said otherwise. The original reaches the fight by the same route — TACTICS branches on the
    /// hostile bit and sends a non-hostile ship off to the planet instead.
    /// </remarks>
    public static bool WantsToAttack(Ship ship, EliteRandom random)
    {
        if (!ship.IsHostile)
        {
            return false;
        }

        int roll = random.Next() | 0x80;
        return roll < ship.AiFlag;
    }

    /// <summary>The Anaconda, the one ship that carries a smaller ship inside it.</summary>
    public const int AnacondaType = 14;

    /// <summary>The Worm, which is what an Anaconda spawns on this build.</summary>
    public const int WormType = 23;

    /// <summary>
    /// The threshold an Anaconda rolls against to release the ship it carries: the original's
    /// <c>CMP #200</c>, where a roll of 200 or more releases it. That is 56 in 256, or 22%.
    /// </summary>
    public const int AnacondaReleaseThreshold = 200;

    /// <summary>
    /// The AI flag the spawned ship is given: <c>%11110001</c>, which is E.C.M., AI enabled and
    /// hostile, with a low aggression.
    /// </summary>
    public const byte SpawnedShipAiFlag = 0b1111_0001;

    /// <summary>
    /// The threshold a trader rolls against to turn out to be a pirate: the original's
    /// <c>CPX #100</c>, where a roll of 100 or more leaves it alone. That is 156 in 256, or 61%.
    /// </summary>
    public const int TraderRemainsThreshold = 100;

    /// <summary>
    /// The legal status at which a bounty hunter comes for us: the original's <c>CPX #40</c> against
    /// <c>FIST</c>, where 50 is a fugitive.
    /// </summary>
    public const int BountyHunterLegalStatus = 40;

    /// <summary>
    /// Decides a ship's role from its NEWB flags and rewrites the flags as play goes on.
    /// </summary>
    /// <remarks>
    /// This is the rule the roles actually come from on this build, and it runs every frame the
    /// tactics do:
    ///
    /// <code>
    /// LDA NEWB / LSR A / BCC TN1        \ bit 0: is this a trader?
    /// CPX #100 / BCS TA22               \ 61% chance: leave it alone
    ///                                   \ otherwise it turns out to be a pirate
    /// .TN1
    /// LSR A / BCC TN2                   \ bit 1: is this a bounty hunter?
    /// LDX FIST / CPX #40 / BCC TN2      \ only if our legal status is 40 or more
    /// LDA NEWB / ORA #%00000100         \ a bounty hunter that hates us goes hostile
    /// </code>
    ///
    /// Two things follow from this that the spawn-table model does not give. A **trader** is a ship
    /// carrying bit 0 — on this build only the Shuttle and the Transporter — and 39% of the time it
    /// is a pirate in disguise rather than a trader, which is why the source's comment gives 39% here
    /// against 20% in the advanced versions. And a **bounty hunter** does not simply attack: it only
    /// turns on us once our legal status reaches 40, so a clean commander is left alone by the very
    /// ships that exist to hunt the wanted.
    /// </remarks>
    /// <param name="ship">The ship whose turn it is.</param>
    /// <param name="random">The random number generator.</param>
    /// <param name="legalStatus">The commander's legal status, the original's FIST.</param>
    public static void DecideRole(Ship ship, EliteRandom random, int legalStatus)
    {
        if ((ship.NewbFlags & Ship.NewbTrader) != 0)
        {
            // A trader rolls to turn out to be a pirate. Once hostile it stays hostile: the flags
            // are rewritten, so the roll is not repeated.
            if (!ship.IsHostile && random.Next() < TraderRemainsThreshold)
            {
                ship.NewbFlags |= Ship.NewbHostile;
            }
        }
        else if ((ship.NewbFlags & Ship.NewbBountyHunter) != 0 &&
                 legalStatus >= BountyHunterLegalStatus)
        {
            ship.NewbFlags |= Ship.NewbHostile;
        }
    }

    /// <summary>
    /// Considers whether an Anaconda should release the ship it carries.
    /// </summary>
    /// <remarks>
    /// The disc's branch, which differs from the advanced versions': *"in the disc version, Anacondas
    /// can only spawn Worms, while in the advanced versions they can also spawn Sidewinders"*. So the
    /// roll is a 22% chance of a Worm and nothing else — where the advanced versions then roll again
    /// and give a 61% chance of a Worm against a 39% chance of a Sidewinder.
    ///
    /// It is a chance on <em>every frame</em>, not a one-off: the original tests it each time the
    /// tactics run, so an Anaconda that survives a while will fill the sky with Worms.
    /// </remarks>
    /// <returns>True if it should release one.</returns>
    /// <remarks>
    /// The comparison is the original's <c>CMP #200 / BCC TA7</c>, which <em>skips</em> when the roll
    /// is below 200 — so it is the roll of 200 or more that releases the ship, and the chance is the
    /// 56 in 256 the source's own comment gives as 22%. Reading the branch the other way makes it
    /// release on 78% of frames instead, which fills the sky with Worms.
    /// </remarks>
    public static bool ShouldReleaseShip(Ship ship, EliteRandom random) =>
        ship.Type == AnacondaType && random.Next() >= AnacondaReleaseThreshold;

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

        (int x, int y, int z) = ship.GetPosition();

        // TA4/TA5: a ship that is right on top of us breaks off whatever its aggression says. The
        // original tests z_hi >= 3 and, failing that, x_hi OR y_hi with bit 0 cleared: a ship with
        // z under 3 * 256 and both x and y under 2 * 256 heads away rather than pressing on. This
        // is what stops a fight turning into a series of collisions.
        bool tooClose = z < CloseRangeZ && Math.Abs(x) < CloseRangeXY && Math.Abs(y) < CloseRangeXY;
        if (tooClose)
        {
            attack = false;
        }

        // Aim at us if we are the target, otherwise turn away
        double distance = Math.Sqrt(((double)x * x) + ((double)y * y) + ((double)z * z));
        if (distance < 1)
        {
            return false;
        }

        // The direction from the ship to us. This is the vector the original works its dot
        // products from, and it is kept unflipped for the laser check below.
        var towardsUs = new System.Numerics.Vector3((float)-x, (float)-y, (float)-z) / (float)distance;
        System.Numerics.Vector3 nose = Unit(ship.Orientation, Orientation.Nosev);
        System.Numerics.Vector3 roof = Unit(ship.Orientation, Orientation.Roofv);
        System.Numerics.Vector3 side = Unit(ship.Orientation, Orientation.Sidev);

        // Steering: peaceful ships turn away rather than towards us
        System.Numerics.Vector3 steer = attack ? towardsUs : -towardsUs;

        float aimX = System.Numerics.Vector3.Dot(steer, side);
        float aimY = System.Numerics.Vector3.Dot(steer, roof);
        float aimZ = System.Numerics.Vector3.Dot(towardsUs, nose);

        // Roll towards the target if it is off to one side, pitch if it is above or below. The
        // counters are sign-magnitude bytes, and the original gives them the opposite sign to the
        // dot product: as TACTICS puts it, "set the ship's pitch counter to 3, with the opposite
        // sign to the dot product result".
        //
        // The magnitude is the original's nroll, not a flat RAT. nroll doubles the dot product and
        // compares it against RAT2: below the threshold the counter is left at zero with only the
        // sign set, which stops a ship twitching at an aim it is already close to, and at or above
        // it the counter is the full RAT. The dot product is the original's, where a unit vector's
        // component runs to 96, so the comparison is against RAT2 in those units.
        byte roll = CounterFor(aimX);
        byte pitch = CounterFor(aimY);
        ship.Data[ShipDataBlock.RollCounter] = roll;
        ship.Data[ShipDataBlock.PitchCounter] = pitch;

        // Fire if the ship is attacking, close, roughly ahead and has a laser.
        //
        // The attack test is the one that matters: a peaceful ship never fires, and the original
        // never even reaches its laser checks for one, because a ship that is not hostile is sent
        // off towards the planet instead. Without it every trader that happened to be flying away
        // from us counted as aiming at us — the aim was worked out from the steering vector, which
        // is flipped for peaceful ships, so a ship pointing away from us scored a perfect hit and
        // shot us in the back as it left. This is why sitting still next to the station drained the
        // energy banks and lit the dashboard's ENERGY LOW warning with not a hostile ship in sight.
        if (attack && laserPower > 0 && aimZ > AimCosine && distance < FireRange)
        {
            // The original reads the attacker's z_sign to decide which of our shields was hit
            bool fromBehind = z < 0;
            return Combat.TakeDamage(player, damage, fromBehind);
        }

        return false;
    }

    /// <summary>
    /// nroll: the turn counter for one axis, from the original's own routine.
    /// </summary>
    /// <remarks>
    /// The original works in 8-bit signed arithmetic on the dot product of the ship's vector with
    /// the direction to the target, where a unit vector's component is 96 - so the largest dot
    /// product of two unit vectors has a magnitude of 36 after the shift the routine applies. Here
    /// the same direction comes from a normalised float, so its component is scaled back into those
    /// units before the comparison, which keeps RAT and RAT2 in the original's terms.
    /// </remarks>
    private static byte CounterFor(float aim)
    {
        int scaled = (int)(aim * UnitComponent);

        // nroll doubles the value and drops the sign bit, then compares against RAT2
        int doubled = Math.Abs(scaled) * 2;
        bool negative = scaled > 0;

        byte magnitude = doubled >= TurnThreshold ? (byte)TurnRate : (byte)0;
        return (byte)(magnitude | (negative ? 0x80 : 0x00));
    }

    /// <summary>What a unit vector's component is in the original's units.</summary>
    private const int UnitComponent = 96;

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
