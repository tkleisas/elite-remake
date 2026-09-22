using EliteRemake.Core.Universe;

namespace EliteRemake.Core.Sim;

/// <summary>
/// Sets up the star system we arrive in: the planet and the sun.
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

        // z_sign = (s0_hi AND %11) + 3, so the planet is 3 to 7 in the top byte ahead of us
        int zSign = (system.Seeds.S0Hi & 0x03) + 3;
        int xySign = zSign >> 1;

        var planet = new Ship(type, string.Empty, $"{system.Name} (planet)");
        planet.SetCoordinate(ShipDataBlock.Z, zSign << 16);
        planet.SetCoordinate(ShipDataBlock.X, xySign << 16);
        planet.SetCoordinate(ShipDataBlock.Y, xySign << 16);

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
        sun.SetCoordinate(ShipDataBlock.Z, -(zSign & 0x07) << 16);
        sun.SetCoordinate(ShipDataBlock.X, offset << 16);
        sun.SetCoordinate(ShipDataBlock.Y, offset << 16);
        sun.Energy = 255;

        return sun;
    }

    /// <summary>Adds the planet and the sun to the simulation, as arriving in a system does.</summary>
    public static void AddSystemBodies(FlightSim sim, StarSystem system)
    {
        sim.Spawn(CreateSun(system));
        sim.Spawn(CreatePlanet(system));
    }
}
