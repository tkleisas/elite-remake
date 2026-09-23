using EliteRemake.Core.Sim;
using EliteRemake.Core.Universe;
using Xunit;

namespace EliteRemake.Core.Tests;

/// <summary>
/// Checks arriving in a system. The planet and the sun are held in the same bubble as the ships,
/// which makes the order of the arrival load-bearing: clearing the bubble after adding them leaves
/// the system with a station and an empty sky, which is what both hyperspace and the galactic
/// hyperdrive used to do.
/// </summary>
public class SystemArrivalTests
{
    private static FlightSim CreateSim()
    {
        var player = new Ship(11, "cobra-mk-3", "Cobra Mk III");
        return new FlightSim(player);
    }

    /// <summary>The two systems the tests jump between, from the first galaxy's own seeds.</summary>
    private static StarSystem[] TwoSystems()
    {
        StarSystem[] galaxy = Galaxy.GenerateGalaxy(Galaxy.GalaxySeeds(0));
        return [galaxy[7], galaxy[8]];   // Lave and Diso
    }

    private static int CountOf(FlightSim sim, int type)
    {
        int count = 0;
        foreach (Ship ship in sim.Bubble)
        {
            if (ship.Type == type)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Arriving in a system leaves the planet and the sun in the sky and **no station**: the
    /// original spawns the station from the flight loop when we reach the planet's orbit, which is
    /// what makes crossing a system to find one part of the game rather than a formality.
    /// </summary>
    [Fact]
    public void Arriving_PutsTheSunAndThePlanetInTheSkyAndNoStation()
    {
        FlightSim sim = CreateSim();
        StarSystem[] systems = TwoSystems();

        SystemArrival.ArriveInSystem(sim, systems[0]);

        Assert.Equal(1, CountOf(sim, ShipTypes.Sun));
        Assert.Equal(1, CountOf(sim, SystemArrival.PlanetTypeA));
        Assert.Equal(0, CountOf(sim, ShipTypes.Coriolis));
    }

    /// <summary>A station distance is the development shortcut, and puts one where it is asked for.</summary>
    [Fact]
    public void AStationDistancePlacesOneWhereItWasAskedFor()
    {
        FlightSim sim = CreateSim();
        StarSystem[] systems = TwoSystems();

        SystemArrival.ArriveInSystem(sim, systems[0], 3000, SystemArrival.StationRollCounter);

        Ship station = Assert.Single(sim.Bubble, s => s.Type == ShipTypes.Coriolis);
        Assert.Equal(3000, station.GetPosition().Z);
    }

    /// <summary>
    /// Arriving clears the missile lock, which is RES2's own work: "Reset MSTG, the missile target,
    /// to &amp;FF (no target)". TT110 runs RES2 at every launch and arrival, so a lock never
    /// survived the journey it was reset by.
    /// </summary>
    [Fact]
    public void Arriving_ClearsTheMissileLock()
    {
        FlightSim sim = CreateSim();
        StarSystem[] systems = TwoSystems();

        sim.MissileLock = new Ship(17, "sidewinder", "Sidewinder");
        SystemArrival.ArriveInSystem(sim, systems[0]);

        Assert.Null(sim.MissileLock);
    }

    /// <summary>
    /// The station is spawned one planetary radius above the surface, along the planet's own nose
    /// vector: the planet's centre plus twice the 96-scaled nose vector, doubled into a 16-bit value.
    /// The planet's nose points back at us, so the station is 49152 units nearer to us than the
    /// planet is — two nose vectors, which is 2 * 96 * 256 units.
    /// </summary>
    [Fact]
    public void TheSpawnPointIsOneRadiusAboveTheSurfaceAlongThePlanetsNose()
    {
        StarSystem[] systems = TwoSystems();
        Ship planet = SystemArrival.CreatePlanet(systems[0]);
        planet.SetPosition(1000, -2000, 60000);

        ((int x, int y, int z), bool inRange) = SystemArrival.StationSpawnPoint(planet);

        Assert.Equal((1000, -2000, 60000 - (2 * 96 * 256)), (x, y, z));
        Assert.True(inRange, "a planet 60000 ahead puts the station's orbit within reach");

        // And a planet far enough ahead puts the spawn point out of reach: at twice the orbit's
        // distance the station would be exactly 192 * 256 units away in z, which is the limit
        planet.SetPosition(0, 0, 4 * 96 * 256);
        (_, bool tooFar) = SystemArrival.StationSpawnPoint(planet);
        Assert.False(tooFar);
    }

    [Fact]
    public void Arriving_LeavesNothingOfTheSystemWeLeft()
    {
        FlightSim sim = CreateSim();
        StarSystem[] systems = TwoSystems();

        SystemArrival.ArriveInSystem(sim, systems[0], 3000, 64);
        Ship[] first = [.. sim.Bubble];

        SystemArrival.ArriveInSystem(sim, systems[1], 3000, 64);

        // A jump replaces the sky rather than adding to it: three bodies in, three bodies out
        Assert.Equal(3, sim.Bubble.Count);
        foreach (Ship ship in first)
        {
            Assert.DoesNotContain(ship, sim.Bubble);
        }
    }

    [Fact]
    public void Arriving_ReplacesTheShipsWeLeftBehind()
    {
        FlightSim sim = CreateSim();
        StarSystem[] systems = TwoSystems();

        SystemArrival.ArriveInSystem(sim, systems[0], 3000, 64);
        Assert.True(sim.Spawn(Ship.Create(17, "sidewinder", "Sidewinder", 0, 0, 0, 0, 4000)));

        SystemArrival.ArriveInSystem(sim, systems[1], 3000, 64);

        Assert.Equal(0, CountOf(sim, 17));
        Assert.Equal(3, sim.Bubble.Count);
    }

    [Fact]
    public void ArrivingInAFullBubble_StillGetsItsSunAndPlanet()
    {
        FlightSim sim = CreateSim();
        StarSystem[] systems = TwoSystems();

        // Fill the bubble right up, so the bodies have no free slot unless the arrival empties it
        // first. The slot limit is what a ship arriving has to fit into.
        for (int i = 0; i < FlightSim.MaxShipsInBubble; i++)
        {
            Assert.True(sim.Spawn(Ship.Create(17, "sidewinder", "Sidewinder", 0, 0, 0, 0, 4000)));
        }

        SystemArrival.ArriveInSystem(sim, systems[0], 3000, 64);

        Assert.Equal(1, CountOf(sim, ShipTypes.Sun));
        Assert.Equal(1, CountOf(sim, SystemArrival.PlanetTypeA));
        Assert.Equal(1, CountOf(sim, ShipTypes.Coriolis));
    }


    /// <summary>
    /// The planet sits ahead of us and up to the right at 3 to 7 steps of 65536, as SOLAR places it:
    /// the distance comes from bits 0-1 of s0_hi plus 3 plus the carry, and the same value halved
    /// goes into both the x and y sign bytes.
    /// </summary>
    [Theory]
    [InlineData(0, 3)]
    [InlineData(1, 4)]
    [InlineData(2, 5)]
    [InlineData(3, 6)]
    public void ThePlanetIsPlacedWhereSolarPlacesIt(int seedBits, int expectedDistance)
    {
        FlightSim sim = CreateSim();
        StarSystem system = WithS0Hi(TwoSystems()[0], seedBits);

        SystemArrival.AddSystemBodies(sim, system);

        Ship planet = Assert.Single(sim.Bubble, s => s.Type == SystemArrival.PlanetTypeA);
        (int x, int y, int z) = planet.GetPosition();

        Assert.Equal(expectedDistance << 16, z);
        Assert.Equal(expectedDistance / 2 << 16, x);
        Assert.Equal(expectedDistance / 2 << 16, y);
    }

    /// <summary>
    /// The carry flag SOLAR's halving of our legal status leaves behind is added to the planet's
    /// distance, so a commander with an odd record arrives one step further out.
    /// </summary>
    [Fact]
    public void TheCarryFromHalvingOurRecordMovesThePlanetAStepFurtherOut()
    {
        StarSystem system = WithS0Hi(TwoSystems()[0], 0);   // a distance of 4

        FlightSim even = CreateSim();
        SystemArrival.AddSystemBodies(even, system, statusCarry: 0);

        FlightSim odd = CreateSim();
        SystemArrival.AddSystemBodies(odd, system, statusCarry: 1);

        int evenZ = Assert.Single(even.Bubble, s => s.Type == SystemArrival.PlanetTypeA).GetPosition().Z;
        int oddZ = Assert.Single(odd.Bubble, s => s.Type == SystemArrival.PlanetTypeA).GetPosition().Z;

        Assert.Equal(3 << 16, evenZ);
        Assert.Equal(4 << 16, oddZ);
    }

    /// <summary>
    /// The sun is behind us at an odd 1 to 7 steps of 65536, off to one side in x by up to three
    /// steps — in the sign byte and in the high byte, as SOLAR stores it — and dead centre in y.
    /// </summary>
    [Fact]
    public void TheSunIsPlacedBehindUsWhereSolarPlacesIt()
    {
        FlightSim sim = CreateSim();
        StarSystem system = TwoSystems()[0];

        SystemArrival.AddSystemBodies(sim, system);

        Ship sun = Assert.Single(sim.Bubble, s => s.Type == ShipTypes.Sun);
        (int x, int y, int z) = sun.GetPosition();

        Assert.Equal(-(((system.Seeds.S1Hi & 0x07) | 0x01) << 16), z);
        Assert.True(z < 0, "the sun is behind us");

        int offset = system.Seeds.S2Hi & 0x03;
        Assert.Equal((offset << 16) | (offset << 8), x);
        Assert.Equal(0, y);

        // Every seed gives a sun within three steps of the centre line in x, which is what the
        // original's "dead centre in our rear laser crosshairs" describes
        Assert.True(Math.Abs(x) <= 3 << 16);
    }

    /// <summary>A system whose seeds have had their s0_hi replaced, so a placement can be pinned.</summary>
    private static StarSystem WithS0Hi(StarSystem system, int s0Hi)
    {
        SystemSeeds seeds = system.Seeds with { S0 = (ushort)((system.Seeds.S0 & 0x00FF) | (s0Hi << 8)) };
        return system with { Seeds = seeds };
    }
}
