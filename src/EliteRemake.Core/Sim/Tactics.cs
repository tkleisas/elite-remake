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

    /// <summary>
    /// The nose dot product at or above which a ship accelerates instead of braking: the original's
    /// CNT2, "the maximum angle beyond which a ship will slow down to start turning towards its
    /// prey", which is 22 out of a possible 36.
    /// </summary>
    public const int TurnToAccelerate = 22;

    /// <summary>How much a ship speeds up when it is lined up: the original's "LDA #3 / STA INWK+28".</summary>
    public const int AccelerateRate = 3;

    /// <summary>
    /// How far off the aim has to be before a ship brakes rather than pressing on: the original's
    /// <c>CMP #18</c>, below which it "still has quite a lot of turning to do" and coasts.
    /// </summary>
    public const int BrakeFrom = 18;

    /// <summary>How hard a ship brakes: "LDA #&amp;FF ... STA INWK+28", which is -1.</summary>
    public const int BrakeRate = 1;

    /// <summary>How hard a missile brakes, which the original doubles "as missiles are more nimble".</summary>
    public const int MissileBrakeRate = 2;

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
    /// <param name="stationPresent">
    /// True while a space station is in our bubble, which is the original's SSPR and the station's
    /// no-fire zone.
    /// </param>
    public static void DecideRole(
        Ship ship,
        EliteRandom random,
        int legalStatus,
        bool stationPresent = false)
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

        // TN3: a hostile pirate inside the station's no-fire zone has its aggression cleared, as
        // the original puts it, "because even pirates aren't crazy enough to breach the station's
        // no-fire zone". Bits 1-6 of the AI flag are the aggression level, which is what the attack
        // roll reads, so clearing them leaves a ship that is still hostile and still flies at us but
        // cannot bring itself to shoot.
        if (stationPresent && ship.IsHostile && (ship.NewbFlags & Ship.NewbPirate) != 0)
        {
            ship.AiFlag &= SafeZoneAiFlag;
        }
    }

    /// <summary>
    /// The AI flag a pirate is left with inside the station's no-fire zone: bits 1 to 6 cleared, so
    /// the aggression is zero.
    /// </summary>
    public const byte SafeZoneAiFlag = 0b1000_0001;

    /// <summary>
    /// The space station's own tactics, which are what put traffic in the sky around it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The station is the one ship TACTICS treats differently. A station that has not been annoyed
    /// considers launching a Shuttle or a Transporter — the ships that "ply their trade between the
    /// station and the planet" — with a 0.8% chance on each of its turns and only ever one at a time,
    /// since it gives up while a Transporter is already out there. A station that <em>has</em> been
    /// annoyed sends the police after us instead, on a 6.2% chance, up to four of them.
    /// </para>
    /// <para>
    /// None of this was implemented, which is why our stations sat in an empty sky: the original's
    /// stations are the source of most of the traffic a commander meets, and the Shuttles and
    /// Transporters they launch are the traders whose NEWB flags let them turn out to be pirates.
    /// </para>
    /// </remarks>
    /// <param name="ship">The station.</param>
    /// <param name="random">The random number generator.</param>
    /// <param name="craftInBubble">How many ships are in the local bubble, for the limits.</param>
    /// <param name="transporterPresent">True if a Transporter is already out there.</param>
    /// <param name="copCount">How many police ships are already out there.</param>
    /// <returns>The ship type to launch, or 0 for nothing.</returns>
    public static int StationLaunch(
        Ship ship,
        EliteRandom random,
        bool transporterPresent,
        int copCount)
    {
        if (ship.IsKilled || ship.IsExploding)
        {
            return 0;
        }

        if (ship.IsHostile)
        {
            // TN5: an angry station sends the police, on 6.2%, up to four at a time
            if (random.Next() < HostileStationCopThreshold)
            {
                return 0;
            }

            return copCount >= HostileStationCopLimit ? 0 : CopType;
        }

        // "The station is not hostile, so check how many Transporters there are in the vicinity, and
        // if we already have one, return"
        if (transporterPresent)
        {
            return 0;
        }

        // 99.2% of the time nothing happens
        if (random.Next() < StationLaunchThreshold)
        {
            return 0;
        }

        // "a random number that's either 0 or 1 ... the ship type for a Shuttle or a Transporter"
        return (random.Next() & 1) == 0 ? ShuttleType : TransporterType;
    }

    /// <summary>The Shuttle, which a station launches to trade with the planet.</summary>
    public const int ShuttleType = 9;

    /// <summary>The Transporter, which a station launches to trade with the planet.</summary>
    public const int TransporterType = 10;

    /// <summary>The police ship a station sends after a commander who has annoyed it.</summary>
    public const int CopType = 16;

    /// <summary>
    /// The roll a station makes before launching a shuttle or transporter: the original's
    /// <c>CMP #253</c>, so it happens on three turns in 256 — about one in eighty-five.
    /// </summary>
    public const int StationLaunchThreshold = 253;

    /// <summary>
    /// The roll an angry station makes before sending the police: the original's <c>CMP #240</c>, so
    /// it happens on 16 turns in 256.
    /// </summary>
    public const int HostileStationCopThreshold = 240;

    /// <summary>How many police ships an angry station will have out at once.</summary>
    public const int HostileStationCopLimit = 4;

    /// <summary>
    /// The AI flag the station gives whatever it launches: <c>%11110001</c>, which is an E.C.M., AI
    /// enabled and very aggressive — 56 out of 63.
    /// </summary>
    /// <remarks>
    /// One value covers both launches, because the original's shuttle and police paths meet at the
    /// same <c>TN6</c> label and fall through the same three instructions. A launched shuttle is
    /// therefore aggressive but not hostile — its E% flags say "trader, innocent" — so what its
    /// aggression does is send it off towards the planet, which is the trade route it exists to fly.
    /// </remarks>
    public const byte StationLaunchAiFlag = 0b1111_0001;

    /// <summary>
    /// True when this ship gets to think this frame.
    /// </summary>
    /// <remarks>
    /// Two gates, both from the original's MVEIT. Bit 7 of the AI flag is "this ship has AI at all",
    /// and a ship without it has no tactics — the disc's trader spawn leaves that bit clear, which is
    /// how a trader minds its own business. And TACTICS is not run every frame for every ship: MVEIT
    /// compares the main loop counter with the ship's slot number and only calls it when
    /// <c>(counter XOR slot) AND 7</c> is zero, so each ship thinks once every eight frames, except
    /// missiles, which think every frame.
    /// </remarks>
    /// <param name="ship">The ship whose turn it might be.</param>
    /// <param name="slot">The ship's slot in the local bubble.</param>
    /// <param name="mainLoopCounter">The simulation's frame counter.</param>
    public static bool RunsTacticsThisFrame(Ship ship, int slot, int mainLoopCounter)
    {
        if ((ship.AiFlag & AiEnabled) == 0)
        {
            return false;
        }

        // Missiles chase every frame; everything else thinks on its slot's turn
        if (Missiles.IsMissile(ship.Type))
        {
            return true;
        }

        return ((mainLoopCounter ^ slot) & 7) == 0;
    }

    /// <summary>Bit 7 of the AI flag: this ship has AI at all.</summary>
    public const byte AiEnabled = 0x80;

    /// <summary>
    /// The roll a ship makes against its own missile count to decide whether to launch one: the
    /// original's <c>AND #31</c> against the number of missiles left, so a ship with one missile
    /// fires on about one frame in thirty-two and one with four on one in eight.
    /// </summary>
    public const int MissileRollMask = 31;

    /// <summary>
    /// Whether a ship that is low on energy launches a missile at us.
    /// </summary>
    /// <remarks>
    /// The original only considers missiles for a ship that is down to half its energy or less —
    /// above that it goes straight to its lasers — and it fires nothing at all while any E.C.M. is
    /// running, ours or another ship's, because the field would simply take the missile.
    /// </remarks>
    /// <param name="ship">The ship.</param>
    /// <param name="random">The random number generator.</param>
    /// <param name="ecmActive">True while any E.C.M. is running.</param>
    public static bool ShouldFireMissile(Ship ship, EliteRandom random, bool ecmActive)
    {
        if ((ship.AiFlag & AiEnabled) == 0)
        {
            return false;
        }

        int missiles = ship.Missiles & 0x07;
        if (missiles == 0)
        {
            return false;
        }

        // Above half energy the original never reaches the missile code
        if (ship.Energy > ship.MaxEnergy / 2)
        {
            return false;
        }

        if (ecmActive)
        {
            return false;
        }

        return (random.Next() & MissileRollMask) < missiles;
    }

    /// <summary>
    /// The chance a ship that has run out of luck takes to its escape pod: the original rolls against
    /// 230, so about one frame in ten, and only once the ship is into the last eighth of its energy.
    /// </summary>
    /// <param name="ship">The ship.</param>
    /// <param name="random">The random number generator.</param>
    public static bool ShouldLaunchEscapePod(Ship ship, EliteRandom random)
    {
        if ((ship.AiFlag & AiEnabled) == 0 || ship.IsExploding || ship.IsKilled)
        {
            return false;
        }

        // The last eighth of the energy banks, and the ship has to be able to carry a pod at all:
        // bit 7 of the default NEWB flags is "an escape pod is fitted"
        if (ship.Energy > ship.MaxEnergy / 8)
        {
            return false;
        }

        if ((ship.NewbFlags & Ship.NewbEscapePod) == 0)
        {
            return false;
        }

        return random.Next() >= EscapePodRoll;
    }

    /// <summary>
    /// The roll a ship makes before taking to its escape pod: the original's <c>CMP #230</c>, so a
    /// ship gives up on 26 frames in 256 — about one in ten.
    /// </summary>
    public const int EscapePodRoll = 230;

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
    /// <param name="planetPosition">
    /// Where the planet is, which is where a peaceful ship is going: the original's <c>GOPL</c> is
    /// the branch every non-hostile ship that is not docking takes.
    /// </param>
    /// <param name="stationPosition">
    /// Where the station is, which is where a ship that is docking is going — the original's
    /// <c>DOCKIT</c>.
    /// </param>
    public static bool Apply(
        Ship ship,
        EliteRandom random,
        Ship player,
        int laserPower,
        int damage,
        System.Numerics.Vector3? planetPosition = null,
        System.Numerics.Vector3? stationPosition = null)
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

        // Steering. A hostile ship manoeuvres against us; anything else is going somewhere, and the
        // original's TACTICS sends it there rather than simply away from us: a ship with the docking
        // bit set flies to the station (DOCKIT), and every other peaceful ship flies to the planet
        // (GOPL). Turning peaceful ships away from us instead is what made traders flee across the
        // sky in a straight line and never come back, and it left the shuttles a station launches
        // with nowhere to go.
        System.Numerics.Vector3 steer;
        bool steeringToThePlanet = false;
        if (attack)
        {
            steer = towardsUs;
        }
        else if (ship.IsDocking && stationPosition is { } stationAim)
        {
            steer = DirectionTo(x, y, z, stationAim, towardsUs);
        }
        else if (planetPosition is { } planetAim)
        {
            steer = DirectionTo(x, y, z, planetAim, towardsUs);
            steeringToThePlanet = true;
        }
        else
        {
            steer = -towardsUs;
        }

        float aimX = System.Numerics.Vector3.Dot(steer, side);
        float aimY = System.Numerics.Vector3.Dot(steer, roof);
        float aimZ = System.Numerics.Vector3.Dot(towardsUs, nose);

        // The three dot products the original works from, as the bytes it holds them in. A unit
        // vector's component is 96 there, so the dot product of two of them has a high byte that runs
        // to 36, and it is that byte which nroll and RAT2 are compared against. Scaling a normalised
        // float by 96 instead — which is what this did — makes the threshold 96/36 times as easy to
        // pass, so every ship turned at full rate at aim errors the original would have ignored.
        byte dotNose = DotByte(System.Numerics.Vector3.Dot(steer, nose));
        byte dotRoof = DotByte(aimY);
        byte dotSide = DotByte(aimX);

        // TA151: steering towards the planet lowers RAT2 to zero when the nose dot product is
        // negative — the planet is behind or abeam — so that roll and pitch are always applied.
        // Without it a ship whose target is behind it sets both counters to zero at the moment it is
        // broadside on, and drifts on instead of coming round.
        int threshold = steeringToThePlanet && dotNose >= 128 ? 0 : TurnThreshold;

        // Pitch first, as the original does, and then the roll from the pitch it has just asked for.
        byte pitch = Nroll(dotRoof, threshold);

        // The roll is only started when the ship is not already rolling: the original doubles the
        // current roll counter and leaves the roll alone when the result reaches 32, so a counter of
        // 16 or more plays out before a new roll is considered.
        byte roll = ship.Data[ShipDataBlock.RollCounter];
        if ((byte)(roll << 1) < 32)
        {
            // nroll takes its magnitude from the side dot product but its sign from that dot product
            // combined with the pitch counter, and then inverts it along with everything else. The
            // combination is the point: rolling the way the side dot product alone says rolls the
            // ship the wrong way whenever it is about to pitch the other way, and the nose then
            // swings round the target instead of settling on it.
            roll = Nroll((byte)(dotSide ^ pitch), threshold, dotSide);
        }

        ship.Data[ShipDataBlock.RollCounter] = roll;
        ship.Data[ShipDataBlock.PitchCounter] = pitch;

        // TA6: turning is one decision and the throttle is another. A ship that is pointing at its
        // target opens up — a nose dot product of CNT2 = 22 out of 36, which is about 52 degrees —
        // and one that is far enough off that it has turning to do brakes instead, but only once the
        // error reaches 18; between the two it coasts. A missile brakes harder, "as missiles are
        // more nimble and can brake more quickly".
        //
        // The original halves the acceleration of a ship that has just hit us with its laser, which
        // is why the brake is one off a full 3 rather than a value of its own.
        int aim = (sbyte)dotNose;
        ship.Acceleration = aim >= 0 && aim >= TurnToAccelerate
            ? (byte)AccelerateRate
            : Math.Abs(aim) >= BrakeFrom
                ? unchecked((byte)(Missiles.IsMissile(ship.Type) ? -MissileBrakeRate : -BrakeRate))
                : (byte)0;

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
            // "DEC INWK+28 / Halve the attacking ship's acceleration": a ship that has just hit us
            // with its laser throttles back a little, which is the original's way of stopping a
            // firing pass from turning into a collision
            if (ship.Acceleration > 0)
            {
                ship.Acceleration--;
            }

            // The original reads the attacker's z_sign to decide which of our shields was hit
            bool fromBehind = z < 0;
            return Combat.TakeDamage(player, damage, fromBehind);
        }

        return false;
    }

    /// <summary>
    /// The direction from a ship to somewhere else in the world, or a fallback if it is on top of it.
    /// </summary>
    private static System.Numerics.Vector3 DirectionTo(
        int x,
        int y,
        int z,
        System.Numerics.Vector3 target,
        System.Numerics.Vector3 fallback)
    {
        var delta = new System.Numerics.Vector3(target.X - x, target.Y - y, target.Z - z);
        float length = delta.Length();
        return length < 1f ? fallback : delta / length;
    }

    /// <summary>
    /// nroll: the turn counter for one axis, from the original's own routine.
    /// </summary>
    /// <remarks>
    /// The original works on the byte that holds the high byte of the dot product of two 96-scaled
    /// unit vectors, which runs to 36; <see cref="DotByte"/> produces the same value from the
    /// normalised float this code steers by, which keeps RAT and RAT2 in the original's terms.
    /// </remarks>
    private static byte Nroll(byte dot, int threshold, byte? magnitudeFrom = null)
    {
        // "EOR #%10000000 / AND #%10000000": the counter's sign is the opposite of the byte's bit 7,
        // so a dot product of exactly zero also comes out with the sign bit set
        byte sign = (byte)((dot ^ 0x80) & 0x80);

        // "TXA / ASL A / CMP RAT2 / BCC nroll2": twice the byte, compared as an unsigned value. A
        // negative dot product shifts up to something above 128 and so always passes, which is why a
        // threshold of zero means "always apply roll and pitch" and why the threshold really only
        // holds back a ship that is already nearly lined up — on the nose side of the line.
        byte doubled = (byte)((magnitudeFrom ?? dot) << 1);
        byte magnitude = doubled >= threshold ? (byte)TurnRate : (byte)0;

        return (byte)(magnitude | sign);
    }

    /// <summary>
    /// One of the original's dot products as the byte it holds it in: the high byte of the 16-bit sum
    /// of the products of two 96-scaled unit vectors, which is a signed byte running to 36.
    /// </summary>
    /// <remarks>
    /// The original's MULT12 and MAD return a 16-bit two's complement result, and the routines that
    /// steer a ship read its high byte — so the value is floored rather than truncated towards zero,
    /// which is what an arithmetic shift of a negative product does.
    /// </remarks>
    private static byte DotByte(float aim) => (byte)(int)Math.Floor(aim * DotScale);

    /// <summary>
    /// What the high byte of a dot product of two 96-scaled unit vectors is worth: 96 * 96 / 256.
    /// </summary>
    private const int DotScale = 36;

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
