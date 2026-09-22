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
    public static Ship CreatePlanet(StarSystem system)
    {
        // The planet's type comes from bit 1 of the system's tech level
        int type = (system.TechLevel & 0x02) != 0 ? PlanetTypeB : PlanetTypeA;

        // The original works the distance out from s0_hi and stores it in the planet's z_sign:
        // bits 0-2, plus 6, halved, then bits 0-1 plus 3, gives 3 to 7 in the top byte. The same
        // value goes into the x and y sign bytes, so the planet sits ahead of us and only slightly
        // off to one side — the sign bytes carry the top bits of the coordinate, so a small value
        // there is a small offset, not a large one. Putting the planet half its distance to the
        // side instead, as this did, throws it off the corner of the screen and it is never seen.
        int zSign = (((system.Seeds.S0Hi & 0x07) + 6) >> 1 & 0x03) + 3;
        int offset = system.Seeds.S1Lo >= 128 ? 1 : 0;
        int vertical = system.Seeds.S1Hi >= 128 ? 1 : 0;

        var planet = new Ship(type, string.Empty, $"{system.Name} (planet)");
        planet.SetCoordinate(ShipDataBlock.Z, zSign << 16);
        planet.SetCoordinate(ShipDataBlock.X, offset);
        planet.SetCoordinate(ShipDataBlock.Y, vertical);

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
    public static Ship CreateSun(StarSystem system)
    {
        // z_sign = (s1_hi AND %111) OR %10000001, so the sun is behind us at 1 to 7
        int zSign = (system.Seeds.S1Hi & 0x07) | 0x81;

        // x_sign and y_sign come from the low bits of s2_hi, so the sun is off to one side
        int offset = system.Seeds.S2Hi & 0x03;

        var sun = new Ship(ShipTypes.Sun, string.Empty, $"{system.Name} (sun)");
        // The sign bytes carry the top bits of the coordinate, so a small value there is a small
        // offset: the sun is behind us, either dead centre in the rear view or off to one side
        sun.SetCoordinate(ShipDataBlock.Z, -((system.Seeds.S1Hi & 0x07) | 0x01) << 16);
        sun.SetCoordinate(ShipDataBlock.X, offset);
        sun.SetCoordinate(ShipDataBlock.Y, offset);
        sun.Energy = 255;

        return sun;
    }

    /// <summary>Adds the planet and the sun to the simulation, as arriving in a system does.</summary>
    public static void AddSystemBodies(FlightSim sim, StarSystem system)
    {
        sim.Spawn(CreateSun(system));
        sim.Spawn(CreatePlanet(system));
    }

    /// <summary>
    /// Creates the space station for a system, placed ahead of us as the original does.
    /// </summary>
    /// <param name="distance">How far ahead of us to put the station, in the original's units.</param>
    /// <param name="spinRoll">The station's roll, which it keeps as it turns.</param>
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
        // appears rather than waiting for the first frame to renew it from SpinRoll
        station.Data[ShipDataBlock.RollCounter] = spinRoll;
        FaceTowardsUs(station);
        return station;
    }

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
