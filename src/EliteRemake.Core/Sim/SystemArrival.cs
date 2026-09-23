using EliteRemake.Core.Maths;
using EliteRemake.Core.Universe;

namespace EliteRemake.Core.Sim;

/// <summary>
/// Sets up the star system we arrive in: the planet, the sun and the space station.
/// </summary>
/// <remarks>
/// The original's SOLAR routine places both bodies from the system's own seeds, so the same system
/// always looks the same. The planet sits to the upper right at a distance of 3 to 7 in the top
/// byte of the 24-bit coordinate (that is, 3-7 * 65536 units), and its type depends on the system's
/// tech level. The sun is placed behind us at 1 to 7 * 65536 units, offset slightly, so it is
/// something to turn round and look at. Both are drawn from a fixed radius of 24576 units divided
/// by their distance, which is why the sun swells so dramatically as you fly towards it.
/// </remarks>
public static class SystemArrival
{
    /// <summary>The radius the original gives the planet and the sun, in its internal units.</summary>
    public const int BodyRadius = 24576;

    /// <summary>The planet type, one of the original's two planet types.</summary>
    public const int PlanetTypeA = 128;

    /// <summary>The other planet type, chosen by the system's tech level.</summary>
    public const int PlanetTypeB = 130;

    /// <summary>
    /// Creates the planet for a system, placed as SOLAR places it.
    /// </summary>
    /// <param name="system">The system whose seeds decide where the planet sits.</param>
    /// <param name="statusCarry">
    /// The carry flag the original's own halving of our legal status leaves behind, which its planet
    /// distance picks up. See the remark below.
    /// </param>
    /// <remarks>
    /// The disc version takes the planet's distance from bits 0-1 of s0_hi and adds 3 plus the carry
    /// flag, giving 3 to 7 in the top byte of the 24-bit coordinate — that is, 3-7 * 65536 units. It
    /// then halves that value with a ROR, carry clear, and stores it in both the x and y sign bytes,
    /// so the planet is always ahead of us and up to the right, never behind or to the left.
    ///
    /// That carry is not a stray: the instruction before the planet is placed is the LSR that halves
    /// our legal status, and nothing in between touches the flag, so a commander whose status is odd
    /// arrives one step further from the planet than one whose status is even. It is a real
    /// consequence of the original's code rather than a figure invented here, which is why the
    /// parameter exists instead of the value being guessed.
    /// </remarks>
    public static Ship CreatePlanet(StarSystem system, int statusCarry = 0)
    {
        // The planet's type comes from bit 1 of the system's tech level
        int type = (system.TechLevel & 0x02) != 0 ? PlanetTypeB : PlanetTypeA;

        // z_sign = (s0_hi AND %11) + 3 + C; x_sign and y_sign are that value halved
        int zSign = (system.Seeds.S0Hi & 0x03) + 3 + (statusCarry & 1);
        int offset = zSign >> 1;

        var planet = new Ship(type, string.Empty, $"{system.Name} (planet)");
        planet.SetCoordinate(ShipDataBlock.Z, zSign << 16);
        planet.SetCoordinate(ShipDataBlock.X, offset << 16);
        planet.SetCoordinate(ShipDataBlock.Y, offset << 16);

        // The planet's nose points back at us, which is the identity orientation ZINF leaves behind:
        // "sidev = (1, 0, 0), roofv = (0, 1, 0), nosev = (0, 0, -1). The negative nosev makes the
        // ship point towards us, as the z-axis points into the screen." It is not decoration: the
        // station is spawned two nose vectors out from the planet's centre, so a planet facing away
        // from us would put the station's orbit on the far side, where the check that decides whether
        // we are close enough for it to appear can never pass.
        planet.Orientation.SetUnity(Orientation.Nosev, Orientation.Z, -1);

        // The original sets the pitch and roll counters to 127 so the planet turns slowly and
        // never damps to a stop
        planet.Data[ShipDataBlock.RollCounter] = 127;
        planet.Data[ShipDataBlock.PitchCounter] = 127;
        planet.Energy = 255;

        return planet;
    }

    /// <summary>
    /// Creates the sun for a system, placed as SOLAR places it: behind us, so the first thing a new
    /// pilot does is turn round to find it.
    /// </summary>
    /// <remarks>
    /// The disc version sets z_sign to <c>%10000001 OR (s1_hi AND 7)</c>, so the sun is behind us at
    /// an odd 1 to 7 in the top byte of the coordinate, and stores s2_hi AND 3 in the x sign byte and
    /// in the x high byte. The original's comment says y_sign; the instruction is <c>STA INWK+1</c>,
    /// which is x_hi. Taking the comment at its word would put the sun up to three times its own
    /// distance above or below us, which is not what "dead centre in our rear laser crosshairs"
    /// describes.
    /// </remarks>
    public static Ship CreateSun(StarSystem system)
    {
        int zSign = (system.Seeds.S1Hi & 0x07) | 0x01;
        int offset = system.Seeds.S2Hi & 0x03;

        var sun = new Ship(ShipTypes.Sun, string.Empty, $"{system.Name} (sun)");
        sun.SetCoordinate(ShipDataBlock.Z, -(zSign << 16));

        // "STA INWK+2 / STA INWK+1": the offset goes into x_sign *and* into x_hi — INWK+1 is the top
        // byte of x and not of y, whatever the original's own comment says — so the sun sits off to
        // one side by 1 to 3 steps of 65536 and dead centre vertically, which is the "dead centre in
        // our rear laser crosshairs" the same comment describes. Reading the second store as y_sign
        // instead throws the sun up to three times its own distance above or below us.
        sun.SetCoordinate(ShipDataBlock.X, (offset << 16) | (offset << 8));
        sun.Energy = 255;

        return sun;
    }

    /// <summary>Adds the planet and the sun to the simulation, as arriving in a system does.</summary>
    public static void AddSystemBodies(FlightSim sim, StarSystem system, int statusCarry = 0)
    {
        sim.Spawn(CreateSun(system));
        sim.Spawn(CreatePlanet(system, statusCarry));
    }

    /// <summary>
    /// Arrives in a system: the old bubble is thrown away and the new system's sun and planet take
    /// its place.
    /// </summary>
    /// <param name="sim">The simulation to arrive in.</param>
    /// <param name="system">The system we have arrived in.</param>
    /// <param name="stationDistance">
    /// A station to place this far ahead of us, or zero for the original's rule, which is that
    /// arriving in a system puts **no station in the sky at all**: the station is spawned by the
    /// flight loop when we reach the planet, which is what <see cref="FlightSim"/> does every thirty
    /// two iterations. A non-zero distance is a development shortcut for flying at a station without
    /// crossing a system to reach one; the command line's <c>--station-distance</c> is its only
    /// caller.
    /// </param>
    /// <param name="stationSpinRoll">The roll counter for a placed station.</param>
    /// <param name="statusCarry">
    /// The carry from SOLAR's halving of our legal status, which the planet's distance picks up.
    /// </param>
    /// <remarks>
    /// This exists as one method because the order is load-bearing and was got wrong twice by
    /// callers that cleared the bubble themselves. The planet and the sun live in the same bubble
    /// as the ships — they are drawn by iterating it — so adding them and clearing afterwards
    /// throws them away again: every hyperspace jump, and every galactic jump, used to arrive at a
    /// station hanging in an empty sky. Clearing first also keeps the sun and planet from being
    /// refused by the slot limit when a full bubble of ships is being replaced.
    /// </remarks>
    public static void ArriveInSystem(
        FlightSim sim,
        StarSystem system,
        int stationDistance = 0,
        byte stationSpinRoll = StationRollCounter,
        int statusCarry = 0)
    {
        foreach (Ship ship in sim.Bubble.ToArray())
        {
            sim.Remove(ship);
        }

        // Arriving anywhere but witchspace means we are no longer in it, which is the original's own
        // clearing of MJ as the hyperspace routines hand over to the arrival
        sim.InWitchspace = false;

        AddSystemBodies(sim, system, statusCarry);

        if (stationDistance > 0)
        {
            sim.Spawn(CreateStation(stationDistance, stationSpinRoll));
        }
    }

    /// <summary>
    /// The distance from the planet's surface at which the original spawns the station: the planet's
    /// centre plus twice its nose vector, which is a point one planetary radius above the surface.
    /// </summary>
    /// <remarks>
    /// The original's MAS1 forms a 16-bit value from the nose vector's high and low bytes and doubles
    /// it, so the addition is 2 * 96 * 256 units along the vector the planet is facing — the vector
    /// in the planet's own data block, which is why the station's orbit turns with the planet.
    /// </remarks>
    public const int StationOrbitMultiplier = 2;

    /// <summary>
    /// How close we must be to that point for the station to appear: the original's <c>FAROF2</c>
    /// against 192, which is 192 * 256 units in each axis.
    /// </summary>
    public const int StationSpawnRange = 192 << 8;

    /// <summary>
    /// The station's position for the moment the original spawns it: the planet's centre plus
    /// <see cref="StationOrbitMultiplier"/> times its nose vector.
    /// </summary>
    /// <returns>The position, and whether we are close enough for the station to appear.</returns>
    public static ((int X, int Y, int Z) Position, bool InRange) StationSpawnPoint(Ship planet)
    {
        (int x, int y, int z) = planet.GetPosition();

        // "MAS1: (x_sign x_hi x_lo) += (nosev_x_hi nosev_x_lo) * 2", one axis at a time
        int sx = x + (StationOrbitMultiplier * planet.Orientation.GetValue(Orientation.Nosev, Orientation.X));
        int sy = y + (StationOrbitMultiplier * planet.Orientation.GetValue(Orientation.Nosev, Orientation.Y));
        int sz = z + (StationOrbitMultiplier * planet.Orientation.GetValue(Orientation.Nosev, Orientation.Z));

        // Each axis is checked as it is worked out: "BNE MA23S ... we are too far from the planet in
        // the x-direction to bump into a space station" is a test on the sign byte, so the point has
        // to be within 65536 units in every axis...
        if (TopByte(sx) != 0 || TopByte(sy) != 0 || TopByte(sz) != 0)
        {
            return ((sx, sy, sz), false);
        }

        // ...and then the three high bytes are compared against 192, which is 49152 units
        bool inRange = Math.Abs(sx) < StationSpawnRange &&
                       Math.Abs(sy) < StationSpawnRange &&
                       Math.Abs(sz) < StationSpawnRange;

        return ((sx, sy, sz), inRange);
    }

    /// <summary>The original's MAS2: the magnitude of a coordinate's top byte.</summary>
    private static int TopByte(int value) => (Math.Abs(value) >> 16) & 0x7F;

    /// <summary>
    /// Creates the space station for a system, placed ahead of us as the original does.
    /// </summary>
    /// <param name="distance">How far ahead of us to put the station, in the original's units.</param>
    /// <param name="spinRoll">
    /// The station's roll counter. The original's NWSPS gives every station
    /// <see cref="StationRollCounter"/>, which is a full anti-clockwise roll that never damps.
    /// </param>
    /// <remarks>
    /// The original's NWSPS turns the station right around when it creates it, by flipping the sign
    /// of each of the three high bytes of its nose vector, and gives it a random clockwise roll with
    /// bit 7 cleared. Both matter for docking: the flip is what points the slot at us, and docking
    /// works out where the slot is from the station's orientation.
    /// </remarks>
    public static Ship CreateStation(int distance, byte spinRoll)
    {
        var station = new Ship(Combat.SpaceStationType, "coriolis", "Coriolis space station")
        {
            SpinRoll = spinRoll,
        };

        station.SetCoordinate(ShipDataBlock.Z, distance);

        // The roll counter is what MVEIT part 8 reads, so the station is already turning when it
        // appears
        station.Data[ShipDataBlock.RollCounter] = spinRoll;

        // NWSPS gives the station an AI flag of %10000001: AI enabled and an E.C.M., with no
        // aggression. On this build bit 7 of the AI flag means "has AI", not "is hostile" — that
        // moved into the NEWB flags — and the station needs it because its own tactics are what
        // launch the shuttles and transports that ply between it and the planet.
        station.AiFlag = StationAiFlag;
        FaceTowardsUs(station);
        return station;
    }

    /// <summary>
    /// The roll counter the original gives the space station: <c>%11111111</c>, which MVEIT reads as
    /// a full anti-clockwise roll with no damping.
    /// </summary>
    /// <remarks>
    /// <para>
    /// NWSPS sets this with a <c>DEX</c> from X = 0, so the counter is 255: bit 7 is the direction
    /// and bits 0-6 are all set, and MVEIT's damping leaves a counter whose low seven bits are all
    /// set alone. The station therefore turns for as long as it exists, at one MVS5 step per
    /// iteration of the main loop.
    /// </para>
    /// <para>
    /// This is not a random value and it is not renewed. The port used to hand the station a random
    /// roll and then put that value back every frame, on the reading that MVEIT spends the counter.
    /// It does spend it — but not when every one of the low seven bits is set, which is precisely
    /// why the original chose 255.
    /// </para>
    /// </remarks>
    public const byte StationRollCounter = 0xFF;

    /// <summary>
    /// The AI flag NWSPS gives the space station: <c>%10000001</c>, which is AI enabled and an
    /// E.C.M. fitted with no aggression.
    /// </summary>
    public const byte StationAiFlag = 0x81;

    /// <summary>
    /// Flips a newly created station's orientation vectors, as the original's NwS1 does.
    /// </summary>
    /// <remarks>
    /// The blueprint's vertices put the docking slot in its +z face, and the original's own docking
    /// computer (DCS1) explains that a station's nose vector points from its centre out through the
    /// slot. A station created with the blueprint's default orientation therefore has its slot
    /// facing away from us, and it is this flip - with us on the far side of it, because the station
    /// is created ahead of us - that turns the slot towards us. The roof and side vectors are
    /// flipped with the nose so the three stay a consistent basis; the original leaves them alone,
    /// which would leave the model mirrored.
    /// </remarks>
    private static void FaceTowardsUs(Ship station)
    {
        Orientation orientation = station.Orientation;
        for (int vector = Orientation.Nosev; vector <= Orientation.Sidev; vector += 6)
        {
            for (int axis = Orientation.X; axis <= Orientation.Z; axis += 2)
            {
                orientation.SetUnity(vector, axis, -orientation.GetUnity(vector, axis));
            }
        }
    }
}
