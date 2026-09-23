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

    [Fact]
    public void Arriving_PutsTheSunThePlanetAndTheStationInTheSky()
    {
        FlightSim sim = CreateSim();
        StarSystem[] systems = TwoSystems();

        SystemArrival.ArriveInSystem(sim, systems[0], 3000, 64);

        Assert.Equal(1, CountOf(sim, ShipTypes.Sun));
        Assert.Equal(1, CountOf(sim, SystemArrival.PlanetTypeA));
        Assert.Equal(1, CountOf(sim, ShipTypes.Coriolis));
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

    [Fact]
    public void Arriving_PlacesTheStationWhereItWasAsked()
    {
        FlightSim sim = CreateSim();
        StarSystem[] systems = TwoSystems();

        SystemArrival.ArriveInSystem(sim, systems[0], 9000, 64);

        Ship station = Assert.Single(sim.Bubble, s => s.Type == ShipTypes.Coriolis);
        Assert.Equal(9000, station.GetPosition().Z);
    }
}
