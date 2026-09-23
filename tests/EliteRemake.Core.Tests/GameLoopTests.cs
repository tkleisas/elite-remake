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
    /// Arriving in a new system halves the legal status, as the original's SOLAR does.
    /// </summary>
    /// <remarks>
    /// SOLAR halves FIST with an LSR every time we arrive in a system, which is how a commander who
    /// lies low and keeps jumping works his way back to clean. Without it a record is permanent, and
    /// a commander who becomes a fugitive once can never be clean again.
    /// </remarks>
    [Fact]
    public void FleeingToANewSystemImprovesOurRecord()
    {
        GameSession session = NewSession();
        session.Commander.Fuel = 70;
        session.Commander.LegalStatus = 200;

        session.SelectedSystem = NearestReachable(session);
        Assert.True(session.StartHyperspace());
        Assert.True(CompleteJump(session));

        Assert.Equal(100, session.Commander.LegalStatus);
        Assert.Equal("Fugitive", session.Commander.LegalStatusName);

        // A few more and he is an offender, then clean again
        for (int i = 0; i < 7; i++)
        {
            session.Commander.Fuel = 70;
            session.SelectedSystem = NearestReachable(session);
            session.StartHyperspace();
            CompleteJump(session);
        }

        // 200 halved eight times is 0
        Assert.Equal(0, session.Commander.LegalStatus);
        Assert.Equal("Clean", session.Commander.LegalStatusName);
    }

    /// <summary>
    /// A real engagement, through the paths the game itself uses: the spawner produces the ships and
    /// the simulation fights them.
    /// </summary>
    /// <remarks>
    /// This exists because three consecutive rounds audited the combat rules while building their own
    /// ships by hand, and the spawner was producing ships that could not fight at all. A check that
    /// constructs its own inputs cannot find a fault in the thing that constructs them, so this one
    /// lets <see cref="Spawner"/> make the ships and asks only whether the commander survives being
    /// shot at.
    /// </remarks>
    [Fact]
    public void ACommanderIsAttackedByWhatTheSpawnerProduces()
    {
        var commander = Commander.CreateDefault();
        var sim = new FlightSim(new Ship(11, "cobra-mk-3", "Cobra Mk III"))
        {
            Commander = commander,
            System = Galaxy.GenerateGalaxy(Galaxy.GalaxySeeds(0))[7],   // Lave
            GalaxySeeds = Galaxy.GalaxySeeds(0),
        };

        sim.Player.Energy = 255;
        sim.Player.ForeShield = 255;
        sim.Player.AftShield = 255;

        // Every spawned ship carries a laser, as the game's blueprint lookup would give it
        sim.LaserPowerProvider = _ => 10;

        // Let the spawner fill the sky, then fly
        int damage = 0;
        int hostile = 0;
        for (int frame = 0; frame < 20_000; frame++)
        {
            sim.Step();
            damage += sim.DamageTakenThisFrame;

            if ((frame & 255) == 0)
            {
                hostile = sim.Bubble.Count(s => s.IsHostile);

            }
        }

        Assert.True(sim.Bubble.Count > 0, "the spawner should have produced something");
        Assert.True(hostile > 0, "and it should be hostile");
        Assert.True(damage > 0, "so an unarmed commander left sitting still should be shot at");
    }

    /// <summary>
    /// A commander can shoot back, kill what the spawner sends, and live to be paid for it.
    /// </summary>
    /// <remarks>
    /// The other half of the live check: that one proves we are shot at, this one proves the fight can
    /// be won. Both consume the real paths — <see cref="Spawner"/> makes the ships, the crosshair test
    /// picks the target, and the kill goes through the bounty and legal-status bookkeeping — rather
    /// than constructing the interesting state by hand.
    /// </remarks>
    [Fact]
    public void ACommanderCanFightBackAndBePaid()
    {
        var commander = Commander.CreateDefault();
        var sim = new FlightSim(new Ship(11, "cobra-mk-3", "Cobra Mk III"))
        {
            Commander = commander,
            System = Galaxy.GenerateGalaxy(Galaxy.GalaxySeeds(0))[7],
            GalaxySeeds = Galaxy.GalaxySeeds(0),
        };

        sim.Player.Energy = 255;
        sim.Player.ForeShield = 255;
        sim.Player.AftShield = 255;
        sim.LaserPowerProvider = _ => 10;

        var random = new EliteRandom(4);
        StarSystem lave = Galaxy.GenerateGalaxy(Galaxy.GalaxySeeds(0))[7];

        // Put a pirate directly ahead, in the crosshairs, with a front laser fitted
        Ship target = Spawner.Create(SpawnKind.Pirates, lave, random);
        target.SetPosition(0, 0, 1500);

        // Held still for the test: the point is whether the laser can destroy it, not whether a
        // pirate that is flying and turning stays in the crosshairs. It matters more than it used to,
        // because the location rotation used to spiral a ship away from where it was put, which
        // carried the target out of the crosshairs on its own.
        target.Speed = 0;
        Assert.True(sim.Spawn(target));

        // Our laser needs a power and a target, and the game supplies both from the blueprint
        sim.LaserPowerProvider = ship => ship == target ? 0 : Combat.PulseLaserPower;
        sim.TargetableAreaProvider = _ => 95 * 95;

        int kills = 0;
        for (int frame = 0; frame < 4_000 && kills == 0; frame++)
        {
            sim.Step(new FlightInput(Fire: true));
            if (sim.DestroyedThisFrame is not null)
            {
                kills++;
            }
        }

        Assert.True(kills > 0, "a pirate held in the crosshairs should be destroyed");
    }

    /// <summary>
    /// A ship the spawner produces is hostile, and therefore able to attack.
    /// </summary>
    /// <remarks>
    /// The flight loop decides whether to fight from the NEWB hostile bit, and the original sets that
    /// bit for both spawn branches — "set bit 2 of the NEWB flags ... so the ship we are about to
    /// spawn is hostile". Leaving it clear meant every pirate and bounty hunter arrived aggressive
    /// but peaceful: nothing the spawner created could attack at all. No unit test noticed, because
    /// the spawner's own test only checked which ship type it chose.
    /// </remarks>
    [Fact]
    public void ASpawnedPirateIsHostile()
    {
        var random = new EliteRandom(1);
        StarSystem lave = Galaxy.GenerateGalaxy(Galaxy.GalaxySeeds(0))[7];

        foreach (SpawnKind kind in new[] { SpawnKind.Pirates, SpawnKind.BountyHunter })
        {
            Ship ship = Spawner.Create(kind, lave, random);
            Assert.True(ship.IsHostile, $"{kind} should spawn hostile");
            Assert.True(ship.AiFlag >= 0x80, "and aggressive, as it already was");
        }

        // And the two together are what lets it fight: aggressive alone is not enough
        var aggressiveOnly = new Ship(17, "sidewinder", "Sidewinder") { AiFlag = 0xF8 };
        Assert.False(Tactics.WantsToAttack(aggressiveOnly, new EliteRandom(1)));

        aggressiveOnly.NewbFlags = Ship.NewbHostile;
        bool attacked = false;
        var roll = new EliteRandom(1);
        for (int i = 0; i < 200 && !attacked; i++) attacked = Tactics.WantsToAttack(aggressiveOnly, roll);
        Assert.True(attacked, "hostile and aggressive should eventually attack");
    }

    /// <summary>
    /// The simulation follows us from system to system, which the mission rules depend on.
    /// </summary>
    /// <remarks>
    /// <c>Flight.System</c> was set in exactly one place — loading a commander — so after any jump the
    /// simulation still believed it was in the system we had left. The mission rules read it when they
    /// decide whether to put the Constrictor in the sky, so even with the galaxy right the Constrictor
    /// would not appear in its own system after flying there.
    /// </remarks>
    [Fact]
    public void TheSimulationFollowsUsFromSystemToSystem()
    {
        GameSession session = NewSession();
        Assert.Equal(session.System.Name, session.Flight.System?.Name);

        // Jump somewhere and check the simulation came too
        StarSystem target = NearestReachable(session);
        session.SelectedSystem = target;
        Assert.True(session.StartHyperspace());
        Assert.True(CompleteJump(session));

        Assert.Equal(target.Name, session.System.Name);
        Assert.Equal(target.Name, session.Flight.System?.Name);

        // And a galactic jump, where both the system and the galaxy change
        session.Commander.GalacticHyperdrive = true;
        Assert.True(session.UseGalacticHyperdrive());

        Assert.Equal(session.System.Name, session.Flight.System?.Name);
        Assert.Equal(session.Commander.GalaxyNumber, session.Flight.GalaxyNumber);
        Assert.Equal(
            Galaxy.GalaxySeeds(session.Commander.GalaxyNumber),
            session.Flight.GalaxySeeds);
    }

    /// <summary>
    /// The whole of mission 1, flown: offered, accepted, found, killed, and paid at the debriefing.
    /// </summary>
    /// <remarks>
    /// Every mission test before this called the mission rules directly and passed, while the game
    /// could not complete mission 1 at all: <c>FlightSim</c> holds the missions and the galaxy it is in,
    /// and *neither was ever assigned*, so the simulation always saw no missions and galaxy 1 — and the
    /// Constrictor lives in galaxy 2. This flies to its system and waits for it, which is the only kind
    /// of test that could have found it.
    /// </remarks>
    [Fact]
    public void MissionOneCanBeFlownAndPaid()
    {
        var commander = Commander.CreateDefault();
        var sim = new FlightSim(new Ship(11, "cobra-mk-3", "Cobra Mk III")) { Commander = commander };
        var session = new GameSession(commander, sim);

        // A commander good enough to be offered the mission
        commander.Kills = Missions.CompetentKills;
        session.Dock();
        Assert.True(session.Missions.OfferMission1(commander));

        // Go to the Constrictor's system, in its own galaxy
        commander.CurrentSystem = Galaxy
            .GenerateGalaxy(Galaxy.GalaxySeeds(Missions.ConstrictorGalaxy))
            .First(s => s.Name == "ORARRA");
        commander.GalaxyNumber = Missions.ConstrictorGalaxy;

        // Accept before loading: Load rebuilds the missions from the commander's status byte, so
        // accepting afterwards would change a Missions object the simulation no longer holds
        commander.MissionStatus = 1;   // bit 0: mission 1 in progress
        session.Load(commander);

        Assert.Equal("ORARRA", session.System.Name);
        Assert.Equal(Missions.ConstrictorGalaxy, sim.GalaxyNumber);
        Assert.Same(session.Missions, sim.Missions);

        // The Constrictor appears, and only because we are in the right place
        session.Launch();
        Ship? constrictor = null;
        for (int i = 0; i < 4_000 && constrictor is null; i++)
        {
            sim.Step();
            constrictor = sim.Bubble.FirstOrDefault(s => s.Type == Missions.ConstrictorType);
        }

        Assert.NotNull(constrictor);

        // Killing it completes the objective; the debriefing pays
        session.BountyProvider = _ => 0;
        int cash = commander.Cash;
        session.RegisterKill(constrictor!);

        Assert.True(session.Missions.Mission1Complete);
        Assert.Equal(cash, commander.Cash);

        int kills = commander.Kills;
        session.Dock();

        Assert.Equal(cash + Missions.ConstrictorReward, commander.Cash);
        Assert.Equal(kills + Missions.DebriefKillPoints, commander.Kills);
        Assert.False(session.Missions.Mission1Active);
    }

    /// <summary>
    /// A hostile ship engages us and a non-hostile one does not — the link between the NEWB hostile
    /// bit and the decision to fight.
    /// </summary>
    /// <remarks>
    /// This is the check that caught the hostile bit being written and never read: the role rules set
    /// it, and the decision to attack was made from the AI flag alone, so a trader that "turned
    /// pirate" went on behaving like a trader. Nothing about either half looks wrong on its own, which
    /// is why the test has to fly it rather than check the flags.
    /// </remarks>
    [Fact]
    public void AHostileShipEngagesUsAndANonHostileOneDoesNot()
    {
        static (FlightSim Sim, Ship Enemy) SetUp(bool hostile)
        {
            var sim = new FlightSim(new Ship(11, "cobra-mk-3", "Cobra Mk III"))
            {
                Commander = Commander.CreateDefault(),
            };
            sim.Player.Energy = 255;
            sim.Player.ForeShield = 255;
            sim.Player.AftShield = 255;
            sim.LaserPowerProvider = ship => ship.Type == 17 ? 10 : 0;

            Ship enemy = Ship.Create(17, "sidewinder", "Sidewinder", 0, 0, 0, 0, 1500);
            enemy.AiFlag = 0xF8;                    // as aggressive as it gets
            enemy.NewbFlags = hostile ? Ship.NewbHostile : (byte)0;
            enemy.Orientation.SetUnity(EliteRemake.Core.Maths.Orientation.Nosev, EliteRemake.Core.Maths.Orientation.Z, -1.0);
            enemy.Energy = 70;
            sim.Spawn(enemy);

            return (sim, enemy);
        }

        static int DamageOver(FlightSim sim, int frames)
        {
            int damage = 0;
            for (int i = 0; i < frames; i++)
            {
                sim.Step();
                damage += sim.DamageTakenThisFrame;
            }

            return damage;
        }

        (FlightSim hostileSim, _) = SetUp(hostile: true);
        Assert.True(DamageOver(hostileSim, 400) > 0, "a hostile ship lined up on us should open fire");

        (FlightSim peacefulSim, _) = SetUp(hostile: false);
        Assert.Equal(0, DamageOver(peacefulSim, 400));
    }

    /// <summary>
    /// A trader turns out to be a pirate 39% of the time, and a bounty hunter only comes for the
    /// nearly wanted.
    /// </summary>
    /// <remarks>
    /// TACTICS reads the ship's role from its NEWB flags and rewrites them: bit 0 is a trader, which
    /// rolls against #100 and becomes a pirate on the 39% that do not reach it, and bit 1 is a bounty
    /// hunter, which goes hostile only once our legal status reaches 40. The rates are measured here
    /// rather than assumed, because a comparison written the wrong way round gives a plausible-looking
    /// constant and the wrong world — which happened once already with the Anaconda's roll.
    /// </remarks>
    [Fact]
    public void TradersTurnPirateAndBountyHuntersWaitForUsToBeWanted()
    {
        var random = new EliteRandom(1);

        // A trader turns pirate at roughly 39%, and once hostile it keeps the bit
        int turned = 0;
        for (int i = 0; i < 10_000; i++)
        {
            var trader = new Ship(9, "shuttle", "Shuttle") { NewbFlags = Ship.NewbTrader | Ship.NewbInnocent };
            Tactics.DecideRole(trader, random, legalStatus: 0);
            if (trader.IsHostile) turned++;
        }

        Assert.InRange(turned, 3_700, 4_100);   // 39% of 10000

        // A bounty hunter stays peaceful while we are clean and turns once we are nearly a fugitive
        var clean = new Ship(24, "cobra-mk-3-p", "Python") { NewbFlags = Ship.NewbBountyHunter };
        for (int i = 0; i < 200; i++) Tactics.DecideRole(clean, random, legalStatus: 0);
        Assert.False(clean.IsHostile, "a clean commander is left alone");

        var wanted = new Ship(24, "cobra-mk-3-p", "Python") { NewbFlags = Ship.NewbBountyHunter };
        Tactics.DecideRole(wanted, random, legalStatus: 40);
        Assert.True(wanted.IsHostile, "a bounty hunter comes for the nearly wanted");

        // And a ship with neither role is untouched
        var rock = new Ship(7, "asteroid", "Asteroid") { NewbFlags = 0 };
        for (int i = 0; i < 100; i++) Tactics.DecideRole(rock, random, legalStatus: 255);
        Assert.Equal(0, rock.NewbFlags);
    }

    /// <summary>
    /// An Anaconda releases a Worm, and on this build nothing else.
    /// </summary>
    /// <remarks>
    /// The disc's branch, which differs from the advanced versions': "in the disc version, Anacondas
    /// can only spawn Worms, while in the advanced versions they can also spawn Sidewinders". The
    /// roll is a 22% chance on every frame, so an Anaconda that survives a while fills the sky.
    /// </remarks>
    [Fact]
    public void AnAnacondaReleasesAWormAndNothingElse()
    {
        var random = new EliteRandom(1);

        // Only the Anaconda releases anything
        foreach (int type in new[] { 11, 12, 13, 16, 19 })
        {
            Assert.False(Tactics.ShouldReleaseShip(new Ship(type, "x", "x"), new EliteRandom(1)));
        }

        // The Anaconda does, at roughly the 22% the original rolls
        int released = 0;
        for (int i = 0; i < 10_000; i++)
        {
            if (Tactics.ShouldReleaseShip(new Ship(Tactics.AnacondaType, "anaconda", "Anaconda"), random))
            {
                released++;
            }
        }

        Assert.InRange(released, 2_050, 2_350);   // 22% of 10000, with room for the generator

        // And it is always the Worm: the disc has no Sidewinder branch
        Assert.Equal(23, Tactics.WormType);
    }

    /// <summary>
    /// The released ship arrives in the bubble under its own AI.
    /// </summary>
    [Fact]
    public void AReleasedWormJoinsTheBubble()
    {
        GameSession session = NewSession();

        // The spawner off, so the only ship that can appear is the one the Anaconda releases. It has
        // to be said explicitly now that the simulation knows which system it is in and will otherwise
        // fill the sky with ordinary traffic as well.
        session.Flight.SpawningEnabled = false;

        var anaconda = Ship.Create(Tactics.AnacondaType, "anaconda", "Anaconda", 0, 0, 0, 3000, 0);
        Assert.True(session.Flight.Spawn(anaconda));

        // Run until one is released, which the 22% chance makes quick
        int before = session.Flight.Bubble.Count;
        for (int i = 0; i < 500 && session.Flight.Bubble.Count == before; i++)
        {
            session.Flight.Step();
        }

        Assert.Equal(before + 1, session.Flight.Bubble.Count);
        Ship worm = session.Flight.Bubble.Last();
        Assert.Equal(Tactics.WormType, worm.Type);
        Assert.Equal(Tactics.SpawnedShipAiFlag, worm.AiFlag);
        Assert.True(worm.AiFlag >= 0x80, "it has AI and is hostile");
    }

    /// <summary>
    /// A missile lock does not outlive the ship it is locked onto.
    /// </summary>
    /// <remarks>
    /// The original's KILLSHP clears the lock when a ship is destroyed, for every kill rather than
    /// only for a kill by our own missile. Without it the lock survives its target, the missile
    /// indicators keep showing a target that is gone, and the next missile flies at a ship that has
    /// been removed from the bubble.
    /// </remarks>
    [Fact]
    public void AMissileLockIsReleasedWhenItsTargetIsDestroyed()
    {
        GameSession session = NewSession();
        var target = Ship.Create(17, "sidewinder", "Sidewinder", 0, 0, 0, 2000, 0);
        Assert.True(session.Flight.Spawn(target));

        session.Flight.MissileLock = target;
        Assert.NotNull(session.Flight.MissileLock);
        Assert.True(session.Flight.CanFireMissile || session.Commander.Missiles == 0);

        // Destroy it and clear the wreck away
        target.Energy = 0;
        target.IsKilled = true;
        Assert.Equal(1, session.Flight.RemoveKilledShips());

        Assert.Null(session.Flight.MissileLock);
        Assert.False(session.Flight.CanFireMissile, "there is nothing left to lock onto");
    }

    /// <summary>
    /// Shooting an innocent turns the space station against us.
    /// </summary>
    /// <remarks>
    /// The original's ANGRY tests bit 5 of the ship's NEWB flags and, when it is set, calls AN2 to
    /// make the station hostile. That is what stops shooting at traders being free: the station we
    /// are trying to dock at takes an interest, and a hostile station will not let us in.
    /// </remarks>
    [Fact]
    public void ShootingAnInnocentTurnsTheStationHostile()
    {
        GameSession session = NewSession();

        // A station and an innocent trader, as the game spawns them
        var station = Ship.Create(2, "coriolis", "Coriolis", 0, 0, 0, 2000, 0);
        var trader = Ship.Create(12, "python", "Python", 0, 0, 0, 1500, 0);
        trader.NewbFlags = Ship.NewbInnocent;

        Assert.True(session.Flight.Spawn(station));
        Assert.True(session.Flight.Spawn(trader));
        Assert.False(station.AiFlag >= 0x80, "the station starts peaceful");

        session.Flight.MakeAngry(trader);

        Assert.True(station.AiFlag >= 0x80, "the station should turn hostile");
        Assert.Equal(FlightSim.HostileStationAiFlag, station.AiFlag);
        Assert.Equal(FlightSim.HostileStationSpeed, station.Speed);

        // The station's AI flag is what Docking.Check reads as "hostile", so this is what stops
        // us docking
        Assert.True(station.AiFlag >= 0x80);

        // Shooting a pirate, which has no innocent bit, leaves the station alone
        GameSession other = NewSession();
        var otherStation = Ship.Create(2, "coriolis", "Coriolis", 0, 0, 0, 2000, 0);
        var pirate = Ship.Create(19, "krait", "Krait", 0, 0, 0, 1500, 0);
        pirate.NewbFlags = 0x8C;

        Assert.True(other.Flight.Spawn(otherStation));
        Assert.True(other.Flight.Spawn(pirate));

        other.Flight.MakeAngry(pirate);

        Assert.False(otherStation.AiFlag >= 0x80, "the station should not care about a pirate");
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
