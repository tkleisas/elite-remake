using EliteRemake.Core.Maths;
using EliteRemake.Core.Sim;
using EliteRemake.Core.Universe;
using Xunit;

namespace EliteRemake.Core.Tests;

/// <summary>
/// Checks the flight simulation: that the universe rotates around us, that our speed moves
/// everything backwards, and that a ship's own motion and tidying follow the original's order.
/// </summary>
public class FlightSimTests
{
    private static (FlightSim Sim, Ship Ship) CreateSim()
    {
        var player = new Ship(11, "cobra-mk-3", "Cobra Mk III");
        var sim = new FlightSim(player);
        var target = Ship.Create(ShipTypes.Coriolis, "coriolis", "Coriolis space station", 0, 0, 0, 0, 2000);
        sim.Spawn(target);
        return (sim, target);
    }

    [Fact]
    public void SpeedKeys_ChangeSpeedByOneAndRespectTheLimits()
    {
        var (sim, _) = CreateSim();
        Assert.Equal(0, sim.Speed);

        sim.Step(new FlightInput(SpeedUp: true));
        Assert.Equal(1, sim.Speed);

        // The original never lets the speed drop below 1
        sim.Step(new FlightInput(SlowDown: true));
        Assert.Equal(1, sim.Speed);

        for (int i = 0; i < 100; i++)
        {
            sim.Step(new FlightInput(SpeedUp: true));
        }

        Assert.Equal(FlightSim.MaxSpeed, sim.Speed);
    }

    /// <summary>
    /// Braking while already at rest must leave the ship at rest. The original brakes with a DEC and
    /// bumps the speed back to 1 with INC only when it has actually reached zero, so the test is on
    /// the value after the decrement. Testing the value before it lets a stopped ship brake from
    /// zero, which wraps the byte to 255 and fires it off at full speed instead of holding station.
    /// </summary>
    [Fact]
    public void BrakingWhileStoppedLeavesTheShipStopped()
    {
        var (sim, _) = CreateSim();
        Assert.Equal(0, sim.Speed);

        for (int i = 0; i < 10; i++)
        {
            sim.Step(new FlightInput(SlowDown: true));
        }

        Assert.Equal(0, sim.Speed);
    }

    [Fact]
    public void OurSpeed_MovesTheUniverseBackwards()
    {
        var (sim, station) = CreateSim();
        sim.Speed = 20;

        // One frame at speed 20 moves the station 20 units closer to us
        sim.Step();
        Assert.Equal(1980, station.GetCoordinate(ShipDataBlock.Z));
    }

    [Fact]
    public void Rolling_MovesObjectsAboveUsSideways()
    {
        var (sim, station) = CreateSim();
        station.SetPosition(0, 10000, 0);

        // Roll for a while (two frames of damping per frame, so hold the key down)
        for (int i = 0; i < 20; i++)
        {
            sim.Step(new FlightInput(RollLeft: true));
        }

        // Rolling turns the world: the station above us swings out to one side
        Assert.True(Math.Abs(station.GetCoordinate(ShipDataBlock.X)) > 100,
            $"expected the station to swing sideways, x = {station.GetCoordinate(ShipDataBlock.X)}");

        // The distance is preserved, give or take the small-angle approximation
        double distance = Math.Sqrt(
            Math.Pow(station.GetCoordinate(ShipDataBlock.X), 2) +
            Math.Pow(station.GetCoordinate(ShipDataBlock.Y), 2) +
            Math.Pow(station.GetCoordinate(ShipDataBlock.Z), 2));
        Assert.InRange(distance, 8500, 11500);
    }

    [Fact]
    public void PullingUp_MovesObjectsAheadDownTheScreen()
    {
        var (sim, station) = CreateSim();
        station.SetPosition(0, 0, 10000);

        for (int i = 0; i < 20; i++)
        {
            sim.Step(new FlightInput(PullUp: true));
        }

        // Pulling up sends everything ahead of us downwards, which in the original's frame (where
        // y points up) means a negative y
        Assert.True(station.GetCoordinate(ShipDataBlock.Y) < -500,
            $"expected the station to drop, y = {station.GetCoordinate(ShipDataBlock.Y)}");

        // And pitching down brings it back up again
        for (int i = 0; i < 40; i++)
        {
            sim.Step(new FlightInput(PitchDown: true));
        }

        Assert.True(station.GetCoordinate(ShipDataBlock.Y) > 500,
            $"expected the station to rise, y = {station.GetCoordinate(ShipDataBlock.Y)}");
    }

    [Fact]
    public void RatesRecentreWhenTheKeysAreReleased()
    {
        var (sim, _) = CreateSim();

        for (int i = 0; i < 10; i++)
        {
            sim.Step(new FlightInput(RollLeft: true));
        }

        byte held = sim.RollRate;
        Assert.NotEqual(FlightControls.Centre, held);

        // With the key released, the damping creeps the rate back towards 128, two units a frame
        for (int i = 0; i < 200; i++)
        {
            sim.Step();
        }

        Assert.Equal(FlightControls.Centre, sim.RollRate);
    }

    [Fact]
    public void AShipsOwnSpeed_MovesItAlongItsNose()
    {
        var (sim, station) = CreateSim();
        station.Speed = 28;
        station.SetPosition(0, 0, 2000);

        // The station's nose starts along +z, so it flies away from us
        sim.Step();
        Assert.Equal(2042, station.GetCoordinate(ShipDataBlock.Z));
    }

    [Fact]
    public void TidyRunsOnTheOriginalSchedule()
    {
        var (sim, _) = CreateSim();

        // Skew the station's orientation and run enough frames for slot 0 to be tidied: the
        // original tidies slot 0 when the main loop counter is 0, 16, 32 and so on
        Ship station = sim.Bubble[0];
        station.Orientation.SetValue(Orientation.Nosev, Orientation.Y, 30000);

        for (int i = 0; i < 16; i++)
        {
            sim.Step();
        }

        double length = Math.Sqrt(
            Math.Pow(station.Orientation.GetValue(Orientation.Nosev, Orientation.X), 2) +
            Math.Pow(station.Orientation.GetValue(Orientation.Nosev, Orientation.Y), 2) +
            Math.Pow(station.Orientation.GetValue(Orientation.Nosev, Orientation.Z), 2));

        // A tidied vector has a length of 96 in its high bytes, scaled to 16 bits
        Assert.InRange(length / 256.0, 90, 102);
    }

    [Fact]
    public void CoordinatesUseTheFullTwentyThreeBits()
    {
        // The planet and sun sit millions of units away, so coordinates must keep the high bits in
        // the third byte rather than truncating at 16 bits
        var ship = new Ship(2, "coriolis", "Test");

        ship.SetCoordinate(ShipDataBlock.Z, 4 << 16);
        Assert.Equal(4 << 16, ship.GetCoordinate(ShipDataBlock.Z));

        ship.SetCoordinate(ShipDataBlock.Z, -(7 << 16));
        Assert.Equal(-(7 << 16), ship.GetCoordinate(ShipDataBlock.Z));

        ship.SetCoordinate(ShipDataBlock.X, 1234567);
        Assert.Equal(1234567, ship.GetCoordinate(ShipDataBlock.X));

        ship.SetCoordinate(ShipDataBlock.Y, -7654321);
        Assert.Equal(-7654321, ship.GetCoordinate(ShipDataBlock.Y));
    }

    [Fact]
    public void ArrivingInASystemPlacesThePlanetAndSun()
    {
        var sim = new FlightSim(new Ship(11, "cobra-mk-3", "Cobra Mk III"));
        EliteRemake.Core.Universe.StarSystem lave = EliteRemake.Core.Universe.Galaxy
            .GenerateGalaxy(0)
            .First(s => s.Name == "LAVE");

        SystemArrival.AddSystemBodies(sim, lave);

        Ship sun = sim.Bubble.Single(s => s.Type == ShipTypes.Sun);
        Ship planet = sim.Bubble.Single(s => s.Type is SystemArrival.PlanetTypeA or SystemArrival.PlanetTypeB);

        // The sun is behind us, the planet ahead to the upper right, both millions of units away
        Assert.True(sun.GetCoordinate(ShipDataBlock.Z) < 0, "the sun should be behind us");
        Assert.True(planet.GetCoordinate(ShipDataBlock.Z) > 0, "the planet should be ahead of us");
        Assert.True(planet.GetCoordinate(ShipDataBlock.Z) >= 3 << 16, "the planet should be distant");

        double sunDistance = sun.GetPosition().Z / 65536.0;
        Assert.InRange(sunDistance, -7, -1);
    }

    [Fact]
    public void ThePlanetKeepsItsDistanceWhileWeManoeuvre()
    {
        // The planet sits millions of units away, so its coordinates need all 23 bits of their
        // magnitude. The original moves it with MV40 for exactly this reason.
        var sim = new FlightSim(new Ship(11, "cobra-mk-3", "Cobra Mk III"));
        EliteRemake.Core.Universe.StarSystem lave = EliteRemake.Core.Universe.Galaxy
            .GenerateGalaxy(0)
            .First(s => s.Name == "LAVE");

        SystemArrival.AddSystemBodies(sim, lave);
        Ship planet = sim.Bubble.Single(s => s.Type is SystemArrival.PlanetTypeA or SystemArrival.PlanetTypeB);
        int before = planet.GetCoordinate(ShipDataBlock.Z);

        Assert.True(before > 100000, $"the planet should start far away, z = {before}");

        // Roll, pitch and accelerate for a couple of seconds
        for (int i = 0; i < 100; i++)
        {
            sim.Step(new FlightInput(RollLeft: true, PullUp: true, SpeedUp: true));
        }

        int after = planet.GetCoordinate(ShipDataBlock.Z);
        Assert.True(after > 100000, $"the planet's distance collapsed to z = {after}");

        double distance = Math.Sqrt(
            Math.Pow(planet.GetCoordinate(ShipDataBlock.X), 2) +
            Math.Pow(planet.GetCoordinate(ShipDataBlock.Y), 2) +
            Math.Pow(planet.GetCoordinate(ShipDataBlock.Z), 2));
        Assert.InRange(distance, 100000, 500000);
    }

    [Fact]
    public void KilledShipsAreRemoved()
    {
        var (sim, station) = CreateSim();
        Assert.Single(sim.Bubble);

        station.IsKilled = true;
        Assert.Equal(1, sim.RemoveKilledShips());
        Assert.Empty(sim.Bubble);
    }

    [Fact]
    public void BubbleIsCappedAtTheOriginalsSlotCount()
    {
        var player = new Ship(11, "cobra-mk-3", "Cobra Mk III");
        var sim = new FlightSim(player);

        for (int i = 0; i < FlightSim.MaxShipsInBubble; i++)
        {
            Assert.True(sim.Spawn(new Ship(17, "sidewinder", "Sidewinder")));
        }

        Assert.False(sim.Spawn(new Ship(17, "sidewinder", "Sidewinder")));
        Assert.Equal(FlightSim.MaxShipsInBubble, sim.Bubble.Count);
    }
    /// <summary>
    /// The in-system jump, WARP: the planet and the sun move one step of 65536 away along our own z
    /// axis, and nothing else in the sky moves at all.
    /// </summary>
    [Fact]
    public void AnInSystemJumpMovesThePlanetAndTheSunBackwards()
    {
        var (sim, planet, sun) = CreateSystemSim();
        (int px, int py, int pz) = planet.GetPosition();
        (_, _, int sz) = sun.GetPosition();

        Assert.True(sim.TryInSystemJump());

        Assert.Equal((px, py, pz - FlightSim.InSystemJumpDistance), planet.GetPosition());
        Assert.Equal(sz - FlightSim.InSystemJumpDistance, sun.GetPosition().Z);

        // "Set the main loop counter to 1, so the next iteration through the main loop will
        // potentially spawn ships"
        Assert.Equal(1, sim.MainLoopCounter);
    }

    /// <summary>Junk rides along with us: WARP's ship check starts past the junk in the FRIN table.</summary>
    [Fact]
    public void JunkDoesNotStopAnInSystemJump()
    {
        var (sim, planet, _) = CreateSystemSim();
        sim.Spawn(Debris.CreateJunk(Debris.Canister, sim.Random));

        // The disc's junk is every type from the escape pod to the transporter, so the shuttle and
        // the transporter the station launches count too — a list that stopped at the splinter let a
        // shuttle block a jump
        sim.Spawn(new Ship(Debris.Shuttle, "shuttle", "Shuttle"));
        sim.Spawn(new Ship(Debris.Transporter, "transporter", "Transporter"));

        int before = planet.GetCoordinate(ShipDataBlock.Z);
        Assert.True(sim.TryInSystemJump());
        Assert.Equal(before - FlightSim.InSystemJumpDistance, planet.GetCoordinate(ShipDataBlock.Z));
    }

    [Fact]
    public void AShipInTheBubbleStopsAnInSystemJump()
    {
        var (sim, planet, sun) = CreateSystemSim();
        sim.Spawn(new Ship(17, "sidewinder", "Sidewinder"));

        Assert.False(sim.TryInSystemJump());
        Assert.Equal(5 << 16, planet.GetCoordinate(ShipDataBlock.Z));
        Assert.Equal(-(3 << 16), sun.GetCoordinate(ShipDataBlock.Z));
    }

    [Fact]
    public void ASpaceStationStopsAnInSystemJump()
    {
        var (sim, planet, _) = CreateSystemSim();
        sim.Spawn(SystemArrival.CreateStation(3000, SystemArrival.StationRollCounter));

        Assert.False(sim.TryInSystemJump());
        Assert.Equal(5 << 16, planet.GetCoordinate(ShipDataBlock.Z));
    }

    [Fact]
    public void ThereIsNoInSystemJumpInWitchspace()
    {
        var (sim, planet, _) = CreateSystemSim();
        sim.InWitchspace = true;

        Assert.False(sim.TryInSystemJump());
        Assert.Equal(5 << 16, planet.GetCoordinate(ShipDataBlock.Z));
    }

    /// <summary>
    /// A body we are facing and already too close to refuses the jump: WARP ORs the magnitudes of the
    /// three top bytes and refuses when halving that leaves zero, so anything under two steps of 65536
    /// in every axis is too close to jump past.
    /// </summary>
    [Theory]
    [InlineData(1 << 16)]
    [InlineData(0)]
    public void APlanetWeAreTooCloseToStopsAnInSystemJump(int distance)
    {
        var (sim, planet, _) = CreateSystemSim();
        planet.SetPosition(0, 0, distance);

        Assert.False(sim.TryInSystemJump());
        Assert.Equal(distance, planet.GetCoordinate(ShipDataBlock.Z));
    }

    /// <summary>A body behind us is not checked at all, however close it is.</summary>
    [Fact]
    public void ABodyBehindUsIsNotTooCloseToJumpPast()
    {
        var (sim, planet, _) = CreateSystemSim();
        planet.SetPosition(0, 0, -1);

        Assert.True(sim.TryInSystemJump());
        Assert.Equal(-1 - FlightSim.InSystemJumpDistance, planet.GetCoordinate(ShipDataBlock.Z));
    }

    /// <summary>A ship that is not under pilot control — a missile, or a rock — is still a ship.</summary>
    [Fact]
    public void EvenAMissileStopsAnInSystemJump()
    {
        var (sim, _, _) = CreateSystemSim();
        sim.Spawn(new Ship(1, "missile", "Missile"));

        Assert.False(sim.TryInSystemJump());
    }

    /// <summary>
    /// Unarming the missile releases the lock and asks for the indicator to be redrawn, which is the
    /// original's ABORT reached from the "U" key.
    /// </summary>
    [Fact]
    public void UnarmingTheMissileReleasesTheLock()
    {
        var (sim, _) = CreateSim();
        sim.Commander = Commander.CreateDefault();
        sim.MissileLock = sim.Bubble[0];

        Assert.True(sim.UnarmMissile());
        Assert.Null(sim.MissileLock);
        Assert.True(sim.MissileUnarmedThisFrame);
    }

    [Fact]
    public void AMissileWeDoNotHaveCannotBeUnarmed()
    {
        var (sim, _) = CreateSim();
        sim.Commander = Commander.CreateDefault();
        sim.Commander.Missiles = 0;
        sim.MissileLock = sim.Bubble[0];

        Assert.False(sim.UnarmMissile());
        Assert.NotNull(sim.MissileLock);
    }

    /// <summary>
    /// A simulation holding the planet and the sun where the original places them: the planet ahead
    /// at 5 steps of 65536 and slightly up and to the right, the sun behind at 3.
    /// </summary>
    private static (FlightSim Sim, Ship Planet, Ship Sun) CreateSystemSim()
    {
        var sim = new FlightSim(new Ship(11, "cobra-mk-3", "Cobra Mk III"))
        {
            SpawningEnabled = false,
        };

        StarSystem lave = Galaxy.GenerateGalaxy(Galaxy.GalaxySeeds(0))[7];
        SystemArrival.AddSystemBodies(sim, lave);

        Ship planet = sim.Bubble.Single(s => s.Type == SystemArrival.PlanetTypeA);
        Ship sun = sim.Bubble.Single(s => s.Type == ShipTypes.Sun);

        // Pin both to known distances, so the jump is measured rather than read off the seeds
        planet.SetPosition(2 << 16, 2 << 16, 5 << 16);
        sun.SetPosition(1 << 16, 0, -(3 << 16));
        return (sim, planet, sun);
    }
}
