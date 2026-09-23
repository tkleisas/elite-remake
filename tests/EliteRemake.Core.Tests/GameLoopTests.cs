using EliteRemake.Core.Sim;
using EliteRemake.Core.Universe;
using Xunit;

namespace EliteRemake.Core.Tests;

/// <summary>
/// The game as a sequence rather than as units: a commander who docks, trades, jumps and docks
/// again, and one who changes galaxy.
/// </summary>
/// <remarks>
/// The other tests here check each part against its source, which is what makes the port faithful.
/// These check that the parts still fit together, because three of the faults this project found were
/// in the joins rather than in any part: a station whose slot faced the wrong way, charts drawn from
/// the wrong galaxy, and a galactic jump that left the old galaxy's sky in place. Every one of those
/// was found by flying the game by hand, and every one of them was invisible to a suite of over two
/// hundred passing tests. This is that flight, written down.
/// </remarks>
public class GameLoopTests
{
    private static GameSession NewSession()
    {
        var player = new Ship(11, "cobra-mk-3", "Cobra Mk III");
        return new GameSession(Commander.CreateDefault(), new FlightSim(player));
    }

    /// <summary>Runs a hyperspace countdown to its end, returning whether the jump completed.</summary>
    private static bool CompleteJump(GameSession session)
    {
        bool arrived = false;
        for (int i = 0; i < 60 && session.HyperspaceCountdown > 0; i++)
        {
            arrived = session.TickHyperspace();
        }

        return arrived;
    }

    /// <summary>The nearest system to this one that the tank can reach.</summary>
    private static StarSystem NearestReachable(GameSession session) =>
        session.SystemsInGalaxy
            .Where(s => s.Seeds != session.System.Seeds)
            .OrderBy(s => Galaxy.DistanceTenths(session.System, s))
            .First();

    /// <summary>
    /// The whole loop: dock, trade, launch, jump, arrive, dock, trade again.
    /// </summary>
    [Fact]
    public void ACommanderCanDockTradeJumpAndDockAgain()
    {
        GameSession session = NewSession();
        Commander commander = session.Commander;
        string home = session.System.Name;

        // Docked at the start of the game's own system
        session.Dock();
        Assert.Equal(GameMode.Docked, session.Mode);
        Assert.Equal(17, session.Market.Length);

        // Buy something and check the books balance
        int cashBefore = commander.Cash;
        int bought = session.Buy(0, 3);
        Assert.Equal(3, bought);
        Assert.Equal(3, commander.CargoUsed);
        Assert.True(commander.Cash < cashBefore, "buying should cost credits");

        // Launch and jump to a reachable system
        session.Launch();
        Assert.Equal(GameMode.Flying, session.Mode);

        StarSystem target = NearestReachable(session);
        session.SelectedSystem = target;
        Assert.True(session.StartHyperspace(), session.Message);
        Assert.True(CompleteJump(session), "the countdown should complete");

        Assert.False(session.InWitchspace, "this jump was not forced to fail");
        Assert.Equal(target.Name, session.System.Name);
        Assert.NotEqual(home, session.System.Name);
        Assert.Equal(1, session.Visit);

        // The fuel was spent, and the new system has its own market
        Assert.True(commander.Fuel < 70, "the jump should cost fuel");

        // Dock at the new station and sell what we bought
        session.Dock();
        int sold = session.Sell(0, bought);
        Assert.Equal(bought, sold);
        Assert.Equal(0, commander.CargoUsed);

        // The commander is where the session says he is
        Assert.Equal(session.System.Name, commander.CurrentSystem.Name);
        Assert.Equal(session.System.Seeds, commander.CurrentSystem.Seeds);
    }

    /// <summary>
    /// A saved commander's missions come back with him.
    /// </summary>
    /// <remarks>
    /// The missions live in the commander's status byte, which a save carries. Loading rebuilt the
    /// commander, the flight model and the market but not the missions, so a commander who saved
    /// while carrying the plans or hunting the Constrictor loaded with no mission at all — and the
    /// byte was written on every save and never read back.
    /// </remarks>
    [Fact]
    public void LoadingACOmmanderRestoresHisMissions()
    {
        GameSession session = NewSession();

        // A commander in the middle of mission 2: mission 1 finished and debriefed, mission 2
        // accepted, plans collected. The status byte is what a save carries.
        session.Missions.Mission1Complete = true;
        session.Missions.Mission1Active = false;
        session.Missions.AcceptMission2();

        // Collected at the system the original puts them at, through the real method, so the bit
        // that marks mission 2 in progress is cleared as picking them up does
        session.Missions.PickUpPlans(
            Galaxy.GenerateGalaxy(Galaxy.GalaxySeeds(Missions.PlansGalaxy))
                .First(x => x.X == Missions.PlansX && x.Y == Missions.PlansY),
            Missions.PlansGalaxy);

        session.Commander.MissionStatus = session.Missions.StatusByte;
        Assert.Equal(10, session.Commander.MissionStatus); // %1010: mission 1 done, carrying plans

        // Load that commander into a fresh session, as starting the game does
        var loaded = new GameSession(Commander.CreateDefault(), new FlightSim(new Ship(11, "cobra-mk-3", "Cobra Mk III")));
        loaded.Load(session.Commander);

        Assert.True(loaded.Missions.Mission1Complete);
        Assert.True(loaded.Missions.CarryingPlans);
        Assert.False(loaded.Missions.Mission1Active, "the debrief cleared that bit");
        Assert.False(loaded.Missions.Mission2Active, "picking the plans up cleared that bit");
        Assert.Equal(10, loaded.Missions.StatusByte);
    }

    /// <summary>
    /// Dying with an escape pod fitted means being picked up, not game over.
    /// </summary>
    /// <remarks>
    /// The original's ESCAPE routine empties all seventeen cargo slots, clears the criminal record,
    /// spends the pod, and delivers a replacement ship with a full tank. The fuel is the part that
    /// matters most: a commander rescued with an empty tank and nothing to sell would have no way to
    /// earn, so the rescue has to leave him able to fly.
    /// </remarks>
    [Fact]
    public void AnEscapePodRescuesTheCommanderWithAFullTank()
    {
        GameSession session = NewSession();
        Commander commander = session.Commander;

        // Something to lose, and a record to clear
        session.Dock();
        session.Buy(0, 5);
        commander.LegalStatus = 120;
        commander.Fuel = 20;
        commander.EscapePod = true;

        session.Launch();
        session.HandlePlayerDeath();

        Assert.False(session.GameOver, "an escape pod should save the commander");
        Assert.Equal(GameMode.Docked, session.Mode);
        Assert.Equal(0, commander.CargoUsed);
        Assert.Equal(0, commander.LegalStatus);
        Assert.False(commander.EscapePod, "the pod is a one-use item");
        Assert.Equal(70, commander.Fuel);
        Assert.Contains("Escape pod", session.Message);
    }

    /// <summary>
    /// Dying without one is game over, and the hold is not the thing that matters then.
    /// </summary>
    [Fact]
    public void DyingWithoutAnEscapePodIsGameOver()
    {
        GameSession session = NewSession();
        session.Commander.EscapePod = false;

        session.Launch();
        session.HandlePlayerDeath();

        Assert.True(session.GameOver);
        Assert.Equal(GameMode.Flying, session.Mode);
    }

    /// <summary>
    /// The market at a system is stable while docked there, and different at the next one.
    /// </summary>
    /// <remarks>
    /// This is the join the charts bug lived in: a system's seeds are not the galaxy's, so anything
    /// derived from "the current system" has to be checked after the current system changes.
    /// </remarks>
    [Fact]
    public void EachSystemHasItsOwnMarket()
    {
        GameSession session = NewSession();
        session.Dock();

        int[] homePrices = session.Market.Select(entry => entry.Price).ToArray();
        string home = session.System.Name;

        // Launch and dock again at the same system: the prices do not change. The original builds
        // the market once, on arrival, and the market screen prices from what that left behind, so
        // redocking is not a way to reroll a bad deal.
        session.Launch();
        session.Dock();
        Assert.Equal(homePrices, session.Market.Select(entry => entry.Price).ToArray());

        // Jump, and the market is a different one
        session.Launch();
        session.SelectedSystem = NearestReachable(session);
        session.StartHyperspace();
        CompleteJump(session);
        session.Dock();

        Assert.NotEqual(home, session.System.Name);
        Assert.Equal(17, session.Market.Length);

        // And the chart's galaxy is the one we are in, containing the system we are at
        Assert.Contains(session.SystemsInGalaxy, s => s.Seeds == session.System.Seeds);
    }

    /// <summary>
    /// A galactic jump moves galaxy, system, market and the galaxy the chart shows, all together.
    /// </summary>
    /// <remarks>
    /// The galactic hyperdrive was found to leave the old galaxy's sky in place, and to rotate a
    /// system's seeds rather than the galaxy's. Both were joins between things that were each
    /// individually right, so this checks them together: after the jump the commander's galaxy
    /// number, the session's system, its market and the galaxy it reports must all agree.
    /// </remarks>
    [Fact]
    public void AGalacticJumpMovesEverythingTogether()
    {
        GameSession session = NewSession();
        Commander commander = session.Commander;
        commander.GalacticHyperdrive = true;

        session.Dock();
        string home = session.System.Name;
        int[] homePrices = session.Market.Select(entry => entry.Price).ToArray();

        int galaxyBefore = commander.GalaxyNumber;
        Assert.True(session.UseGalacticHyperdrive());

        // The galaxy number and the galaxy the session reports agree
        Assert.Equal(galaxyBefore + 1, commander.GalaxyNumber);

        StarSystem[] galaxy = session.SystemsInGalaxy;
        Assert.Contains(galaxy, s => s.Seeds == session.System.Seeds);
        Assert.Equal(Galaxy.GalaxySeeds(commander.GalaxyNumber), galaxy[0].Seeds);

        // We arrive at the nearest system to (96, 96), as the original's GHY always does
        StarSystem expected = Galaxy.FindClosest(Galaxy.GalaxySeeds(commander.GalaxyNumber), 96, 96).System;
        Assert.Equal(expected.Name, session.System.Name);
        Assert.Equal(expected.Name, commander.CurrentSystem.Name);

        // The drive is consumed and the market is the new system's
        Assert.False(commander.GalacticHyperdrive);
        Assert.NotEqual(home, session.System.Name);
        Assert.Equal(17, session.Market.Length);
        Assert.NotEqual(homePrices, session.Market.Select(entry => entry.Price));
    }

    /// <summary>
    /// Eight galactic jumps bring the galaxy back to the first, though not the system.
    /// </summary>
    [Fact]
    public void EightGalacticJumpsReturnToTheFirstGalaxy()
    {
        GameSession session = NewSession();

        for (int i = 0; i < Galaxy.GalaxyCount; i++)
        {
            session.Commander.GalacticHyperdrive = true;
            Assert.True(session.UseGalacticHyperdrive());
            Assert.Equal((i + 1) % Galaxy.GalaxyCount, session.Commander.GalaxyNumber);

            // Every one of them arrives where its galaxy's nearest system to (96, 96) is
            StarSystem expected = Galaxy.FindClosest(
                Galaxy.GalaxySeeds(session.Commander.GalaxyNumber), 96, 96).System;
            Assert.Equal(expected.Name, session.System.Name);
        }

        Assert.Equal(0, session.Commander.GalaxyNumber);
    }

    /// <summary>
    /// The commander can reach the systems the chart shows him, and cannot reach further ones.
    /// </summary>
    /// <remarks>
    /// The charts and the jump have to agree about which galaxy they are describing, which is the
    /// join the chart bug lived in.
    /// </remarks>
    [Fact]
    public void TheJumpAndTheChartAgreeAboutWhatIsReachable()
    {
        GameSession session = NewSession();

        // Fill the tank, then find a system just inside the range and one just outside
        session.Commander.Fuel = 70;

        StarSystem[] galaxy = session.SystemsInGalaxy;
        StarSystem reachable = galaxy
            .Where(s => s.Seeds != session.System.Seeds)
            .OrderBy(s => Galaxy.DistanceTenths(session.System, s))
            .First(s => Galaxy.DistanceTenths(session.System, s) <= session.Commander.Fuel);

        session.SelectedSystem = reachable;
        Assert.True(session.StartHyperspace(), "a reachable system should be jumpable");

        // A system beyond the tank is refused
        GameSession other = NewSession();
        StarSystem tooFar = other.SystemsInGalaxy
            .Where(s => s.Seeds != other.System.Seeds)
            .OrderByDescending(s => Galaxy.DistanceTenths(other.System, s))
            .First(s => Galaxy.DistanceTenths(other.System, s) > other.Commander.Fuel);

        other.SelectedSystem = tooFar;
        Assert.False(other.StartHyperspace(), "a system beyond the tank should be refused");
        Assert.Contains("Not enough fuel", other.Message);
    }
}
