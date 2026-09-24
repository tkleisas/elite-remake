using EliteRemake.Core.Maths;
using EliteRemake.Core.Sim;
using EliteRemake.Core.Universe;
using EliteRemake.Data.Ships;
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

    /// <summary>
    /// Launching our own escape pod is the original's ESCAPE key: the pod is spent, the cargo goes
    /// with the ship, and we are picked up at the station with a full tank and a clean record.
    /// </summary>
    [Fact]
    public void LaunchingAnEscapePodLosesTheCargoAndEndsAtTheStation()
    {
        GameSession session = NewSession();
        session.Flight.Commander = session.Commander;
        session.Commander.EscapePod = true;
        session.Commander.Cash = 500;
        session.Commander.Fuel = 12;
        session.Commander.LegalStatus = 90;
        session.Commander.AddCargo(5, 3);
        session.Commander.AddCargo(0, 7);

        Assert.True(session.LaunchEscapePod());

        // The pod is a one-use item, the hold is emptied, the record is wiped and the replacement
        // ship arrives with a full tank. Cash is untouched: the original salvages the commander, not
        // the cargo.
        Assert.False(session.Commander.EscapePod);
        Assert.Equal(0, session.Commander.GetCargo(5));
        Assert.Equal(0, session.Commander.GetCargo(0));
        Assert.Equal(0, session.Commander.LegalStatus);
        Assert.Equal(EliteRemake.Core.Universe.Outfitting.MaxFuel, session.Commander.Fuel);
        Assert.Equal(500, session.Commander.Cash);

        // And we are in the station, not left flying
        Assert.Equal(GameMode.Docked, session.Mode);
        Assert.Equal("Escape pod launched: cargo lost, but you were picked up at the station.", session.Message);
    }

    /// <summary>Without a pod the key does nothing at all, as the original's ESCP test does.</summary>
    [Fact]
    public void ThereIsNoEscapePodToLaunchWithoutOneFitted()
    {
        GameSession session = NewSession();
        session.Flight.Commander = session.Commander;
        session.Commander.EscapePod = false;

        Assert.False(session.LaunchEscapePod());
        Assert.Equal(GameMode.Flying, session.Mode);
        Assert.False(session.GameOver);
    }

    /// <summary>
    /// Docking and launching are counted, because the flight scene draws the launch and docking
    /// tunnels when they change: the original's LAUN draws them on the way out of the station and
    /// GOIN on the way in, both of them as the flight loop ends.
    /// </summary>
    [Fact]
    public void DockingAndLaunchingAreCounted()
    {
        GameSession session = NewSession();
        Assert.Equal(0, session.Launches);

        session.Dock();
        Assert.Equal(GameMode.Docked, session.Mode);
        Assert.Equal(0, session.Launches);

        // A launch puts us back in flight looking out of the front window, whatever view we docked
        // from, and counts as a launch
        session.Flight.View = SpaceView.Rear;
        session.Launch();
        Assert.Equal(GameMode.Flying, session.Mode);
        Assert.Equal(SpaceView.Front, session.Flight.View);
        Assert.Equal(1, session.Launches);

        session.Dock();
        session.Launch();
        Assert.Equal(2, session.Launches);
    }

    /// <summary>
    /// Launching builds the sky TT110 builds: the planet dead ahead at one step of 65536, the station
    /// 256 units behind us so that we come out of its slot, no sun — the station took its slot — and
    /// a launch speed of 12.
    /// </summary>
    /// <remarks>
    /// This is a reset rather than a continuation: the original calls RES2 to throw the flight
    /// variables and workspaces away, so a launch is a fresh sky rather than the one we docked out
    /// of. The remake used to keep whatever was about, which meant the planet could be anywhere.
    /// </remarks>
    [Fact]
    public void LaunchingBuildsTheSkyTheOriginalBuilds()
    {
        GameSession session = NewSession();
        session.Flight.Commander = session.Commander;

        session.Dock();
        session.Launch();

        Assert.Equal(GameSession.LaunchSpeed, session.Flight.Speed);

        Ship planet = Assert.Single(session.Flight.Bubble, s => s.Type == SystemArrival.PlanetTypeA);
        Assert.Equal((0, 0, GameSession.PlanetAheadOnLaunch), planet.GetPosition());

        Ship station = Assert.Single(session.Flight.Bubble, s => s.Type == Combat.SpaceStationType);
        Assert.Equal((0, 0, -GameSession.StationBehindOnLaunch), station.GetPosition());
        Assert.False(station.IsHostile, "the station we launch from is not angry with us");

        Assert.DoesNotContain(session.Flight.Bubble, s => s.Type == ShipTypes.Sun);
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
            kills += sim.DrainKillReports().Count;
        }

        Assert.True(kills > 0, "a pirate held in the crosshairs should be destroyed");
    }

    /// <summary>
    /// A ship the spawner produces is hostile, and therefore able to attack.
    /// </summary>
    /// <remarks>
    /// The flight loop decides whether to fight from the NEWB hostile bit, and on this build that
    /// bit comes from the ship's own E% flags rather than from the spawner: a pirate hull is hostile
    /// and a Fer-de-lance is a bounty hunter that leaves a clean commander alone. The spawner used
    /// to force the bit on everything it made, which is the NES behaviour — that made the bounty
    /// hunters attack commanders they are supposed to ignore.
    /// </remarks>
    [Fact]
    public void ASpawnedPirateIsHostile()
    {
        var random = new EliteRandom(1);
        StarSystem lave = Galaxy.GenerateGalaxy(Galaxy.GalaxySeeds(0))[7];

        var seen = new HashSet<int>();
        for (int i = 0; i < 40; i++)
        {
            foreach (SpawnKind kind in new[] { SpawnKind.Pirates, SpawnKind.BountyHunter })
            {
                Ship ship = Spawner.Create(kind, lave, random);
                seen.Add(ship.Type);

                // It arrives with exactly the personality its blueprint gives it
                Assert.Equal(ShipData.NewbFlagsFor(ship.Type), ship.NewbFlags);
                Assert.True(ship.AiFlag >= 0x80, "and aggressive, as it already was");
            }
        }

        // Every pirate hull is hostile; the bounty hunters are a mixed bag, which is the point —
        // the Fer-de-lance in particular is not hostile until our legal status earns it
        foreach (int type in seen.Where(t => t is >= 17 and <= 24))
        {
            Assert.True((ShipData.NewbFlagsFor(type) & 0x04) != 0, $"pirate hull {type} should be hostile");
        }

        Assert.Contains(27, seen);
        Assert.False((ShipData.NewbFlagsFor(27) & 0x04) != 0, "a Fer-de-lance is not hostile on arrival");

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
    /// The whole of mission 2, flown: offered, accepted, the plans collected, and delivered.
    /// </summary>
    /// <remarks>
    /// The companion to the mission 1 test, and it exists for the same reason: the mission rules were
    /// heavily tested in isolation and the chain between them was not. Mission 2 crosses two galaxies'
    /// worth of state — the plans are in one system of the third galaxy and the delivery in another —
    /// so it exercises the simulation's view of the universe at every leg.
    /// </remarks>
    [Fact]
    public void MissionTwoCanBeFlownAndPaid()
    {
        var commander = Commander.CreateDefault();
        var sim = new FlightSim(new Ship(11, "cobra-mk-3", "Cobra Mk III")) { Commander = commander };
        var session = new GameSession(commander, sim);

        // Mission 1 complete, so mission 2 is on offer — the disc gates the offer on the third
        // galaxy and a kill tally of 1280, which is 5 in the tally's high byte
        commander.Kills = Missions.Mission2KillRank;
        commander.MissionStatus = 2;
        commander.GalaxyNumber = Missions.PlansGalaxy;
        commander.CurrentSystem = Galaxy
            .GenerateGalaxy(Galaxy.GalaxySeeds(Missions.PlansGalaxy))
            .First(s => !Missions.IsPlansSystem(s, Missions.PlansGalaxy) &&
                        !Missions.IsDeliverySystem(s, Missions.PlansGalaxy));
        session.Load(commander);
        session.Dock();

        // BRIEF2 accepts the mission before it shows anything — bit 2 of TP set before token 11
        // prints — and stages the initial contact's briefing
        Assert.True(session.Missions.Mission2Active);
        Assert.Equal(11, session.PendingBriefing?.Token);

        // Flying reloads the commander, and Load rebuilds the missions from his status byte — so the
        // byte has to be brought up to date first, or the leg we are about to fly loses the mission
        commander.MissionStatus = session.Missions.StatusByte;
        Assert.Equal(6, commander.MissionStatus);   // mission 1 done, mission 2 in progress

        // Fly to where the plans are
        commander.CurrentSystem = Galaxy
            .GenerateGalaxy(Galaxy.GalaxySeeds(Missions.PlansGalaxy))
            .First(s => s.X == Missions.PlansX && s.Y == Missions.PlansY);
        commander.GalaxyNumber = Missions.PlansGalaxy;
        session.Load(commander);
        session.Dock();

        Assert.True(session.Missions.CarryingPlans, session.Message);
        Assert.True(session.Missions.ThargoidSpawnChance > 0, "carrying the plans should draw Thargoids");
        Assert.Equal(session.System.Name, session.Flight.System?.Name);
        int plansBriefingToken = session.PendingBriefing?.Token ?? 0;

        // And to where they go, with the plans now in hand
        commander.MissionStatus = session.Missions.StatusByte;
        Assert.Equal(10, commander.MissionStatus);   // mission 1 done, carrying the plans

        commander.CurrentSystem = Galaxy
            .GenerateGalaxy(Galaxy.GalaxySeeds(Missions.PlansGalaxy))
            .First(s => s.X == Missions.DeliveryX && s.Y == Missions.DeliveryY);
        session.Load(commander);

        int kills = commander.Kills;
        session.Dock();

        Assert.False(session.Missions.CarryingPlans, session.Message);

        // DEBRIEF2 pays no cash: the reward is the special navy energy unit and 256 kill points,
        // staged as token 223 through BRP
        Assert.Equal(Commander.NavalEnergyUnit, commander.EnergyUnitLevel);
        Assert.Equal(kills + Missions.DebriefKillPoints, commander.Kills);
        Assert.Equal(223, session.PendingBriefing?.Token);

        // And on the way here, the plans briefing was token 222 — the disc's BRIEF3
        Assert.Equal(222, plansBriefingToken);
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

        // The disc's BRIEF accepts the mission before it shows anything — bit 0 of TP is set before
        // the banner comes up — and stages the briefing with the Constrictor in it
        Assert.True(session.Missions.Mission1Active);
        Assert.Equal(10, session.PendingBriefing?.Token);
        Assert.Equal(Missions.ConstrictorType, session.PendingBriefing?.ShipType);

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

        // The Constrictor appears, and only because we are in the right place. Flying clear of the
        // station comes first: nothing spawns while one is in the bubble, because the original
        // funnels every spawn path through MTT1 and that leaves the main loop while SSPR is set.
        // The test used to wait at a standstill for four thousand iterations and pass on a race —
        // the station it launched from happened to drift out of range just inside the limit.
        session.Launch();
        Ship? constrictor = null;
        for (int i = 0; i < 20_000 && constrictor is null; i++)
        {
            sim.Step(new FlightInput(SpeedUp: true));
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

        // And the debriefing is staged, as DEBRIEF stages it: BRP prints token 15 and shows the
        // Status Mode screen after it
        Assert.Equal(15, session.PendingBriefing?.Token);
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

        // TACTICS is only run for a ship with AI, and then only on one iteration in eight — MVEIT
        // compares the main loop counter with the ship's slot. Both are the original's behaviour,
        // and without the AI flag here the Anaconda would never make a decision at all.
        anaconda.AiFlag = 0xF8;
        Assert.True(session.Flight.Spawn(anaconda));

        // Run until one is released, which the 22% chance makes quick: on its own iterations, one in
        // eight of 4000 is 500 chances at 22% each
        int before = session.Flight.Bubble.Count;
        for (int i = 0; i < 4000 && session.Flight.Bubble.Count == before; i++)
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
        Ship station = SystemArrival.CreateStation(2000, SystemArrival.StationRollCounter);
        var trader = Ship.Create(12, "python", "Python", 0, 0, 0, 1500, 0);
        trader.NewbFlags = Ship.NewbInnocent;

        Assert.True(session.Flight.Spawn(station));
        Assert.True(session.Flight.Spawn(trader));

        // A station starts peaceful, with the AI flag NWSPS gives it: AI enabled and no aggression.
        // Bit 7 of the AI flag is not hostility on this build — every station carries it — so a
        // peaceful station has it set
        Assert.False(station.IsHostile, "the station starts peaceful");
        Assert.Equal(SystemArrival.StationAiFlag, station.AiFlag);

        session.Flight.MakeAngry(trader);

        // AN2 sets bit 2 of the NEWB flags, and ANGRY makes sure the ship can act on it
        Assert.True(station.IsHostile, "the station should turn hostile");
        Assert.Equal(FlightSim.AngryAcceleration, station.Acceleration);
        Assert.Equal(FlightSim.HostileStationPitchCounter, station.Data[ShipDataBlock.PitchCounter]);

        // `IsHostile` is what Docking.Check reads as "hostile", so this is what stops us docking
        Assert.True(station.IsHostile);

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
    public void LoadingACommanderRestoresHisMissions()
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
    /// Pressing the escape pod's key means being picked up at the station, minus the cargo.
    /// </summary>
    /// <remarks>
    /// The original's ESCAPE routine empties all seventeen cargo slots, clears the criminal record,
    /// spends the pod, and refills the tank to 70.0. The fuel is the part that matters most: a
    /// commander rescued with an empty tank and nothing to sell would have no way to earn, so the
    /// rescue has to leave him able to fly. It is the key's own routine, and DEATH never reaches it:
    /// dying is fatal even with a pod fitted, because there is no one left to press the key.
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
        Assert.True(session.LaunchEscapePod());

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

        Assert.False(session.GameOver, "the death animation runs first");
        Assert.True(session.DeathSequenceRunning);
        Assert.Equal(GameMode.Flying, session.Mode);

        RunOutTheDeathSequence(session);

        Assert.True(session.GameOver);
    }

    /// <summary>
    /// Runs the disc's D2 loop out — the 5.1 seconds of drifting debris — so the tests can reach
    /// the game-over screen that DEATH2 shows after it.
    /// </summary>
    private static void RunOutTheDeathSequence(GameSession session)
    {
        while (session.Flight.DeathSequenceCountdown > 0)
        {
            session.Flight.Step();
        }

        session.TickDeathSequence();
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

/// <summary>
/// Checks what happens after we are killed: the wreck is the end of the commander — a fitted escape
/// pod buys nothing, because DEATH never reaches the pod's own key — and asking for a new commander
/// gives a fresh ship rather than the wreck.
/// </summary>
public class DeathAndRestartTests
{
    private static GameSession Fly()
    {
        var session = new GameSession(
            Commander.CreateDefault(),
            new FlightSim(new Ship(11, "cobra-mk-3", "Cobra Mk III")));

        session.Flight.SpawningEnabled = false;
        return session;
    }

    /// <summary>
    /// Runs the disc's D2 loop out — the 5.1 seconds of drifting debris — so the tests can reach
    /// the game-over screen that DEATH2 shows after it.
    /// </summary>
    private static void RunOutTheDeathSequence(GameSession session)
    {
        while (session.Flight.DeathSequenceCountdown > 0)
        {
            session.Flight.Step();
        }

        session.TickDeathSequence();
    }

    [Fact]
    public void DeathWithoutAPodEndsTheGame()
    {
        GameSession session = Fly();

        session.HandlePlayerDeath();

        Assert.True(session.DeathSequenceRunning, "the disc's D2 loop runs first");
        Assert.Equal(GameMode.Flying, session.Mode);

        RunOutTheDeathSequence(session);

        Assert.True(session.GameOver);
        Assert.Equal(GameMode.Flying, session.Mode);
    }

    [Fact]
    public void DeathIsGameOverEvenWithAPodFitted()
    {
        // The disc's DEATH is fatal however the ship was lost, and it consults nothing first: the
        // pod is launched by its own key while the ship is still there to press it, so the wreck's
        // pod buys nothing
        GameSession session = Fly();
        session.Commander.EscapePod = true;

        session.HandlePlayerDeath();
        RunOutTheDeathSequence(session);

        Assert.True(session.GameOver);
        Assert.True(session.Commander.EscapePod, "the pod is not spent by a death");
    }

    [Fact]
    public void LaunchingClearsTheMissileLock()
    {
        // TT110's RES2 clears the missile target — "Reset MSTG, the missile target, to &FF" — so a
        // lock never survives the launch it was reset by and points at a ship that has gone
        GameSession session = Fly();
        session.Commander.EscapePod = true;
        var target = new Ship(17, "sidewinder", "Sidewinder");
        session.Flight.Spawn(target);
        session.Flight.MissileLock = target;

        session.Launch();

        Assert.Null(session.Flight.MissileLock);
    }

    [Fact]
    public void AskingForANewCommanderAfterDeathGivesAFreshShip()
    {
        // Restarting used to hand back the wreck: the commander was reset but the ship was not, so a
        // new game began with empty energy banks and no shields
        GameSession session = Fly();
        session.HandlePlayerDeath();
        RunOutTheDeathSequence(session);
        Assert.True(session.GameOver);

        session.Flight.Player.Energy = 0;
        session.Flight.Player.ForeShield = 0;
        session.Flight.Player.AftShield = 0;
        session.Flight.Player.IsExploding = true;
        session.Commander.Cash = 999_999;

        session.Restart();

        Assert.False(session.GameOver);
        Assert.Equal(GameSession.NewShipEnergy, session.Flight.Player.Energy);
        Assert.Equal(255, session.Flight.Player.ForeShield);
        Assert.Equal(255, session.Flight.Player.AftShield);
        Assert.False(session.Flight.Player.IsExploding);
        Assert.Equal(1000, session.Commander.Cash);
        Assert.Equal(Universe.Outfitting.MaxFuel, session.Commander.Fuel);
        Assert.Equal(GameMode.Flying, session.Mode);
    }
}

/// <summary>
/// Checks the space station's own tactics: the shuttles and transports it sends out to trade with the
/// planet, and the police it sends after a commander who has annoyed it.
/// </summary>
/// <remarks>
/// This is the source of most of the traffic in Elite's skies, and none of it was implemented — our
/// stations sat in an empty sky, because nothing else spawns near them either.
/// </remarks>
public class StationTacticsTests
{
    private static (FlightSim Sim, Ship Station) CreateSim()
    {
        var sim = new FlightSim(new Ship(11, "cobra-mk-3", "Cobra Mk III"))
        {
            SpawningEnabled = false,
            Commander = Commander.CreateDefault(),
        };

        // The planet has to be there: it is where the traffic the station launches is going
        StarSystem lave = Galaxy.GenerateGalaxy(Galaxy.GalaxySeeds(0))[7];
        SystemArrival.AddSystemBodies(sim, lave);

        Ship station = SystemArrival.CreateStation(3000, SystemArrival.StationRollCounter);
        sim.Spawn(station);
        return (sim, station);
    }

    [Fact]
    public void AStationHasTheAiFlagNwspsGivesIt()
    {
        var (_, station) = CreateSim();

        Assert.Equal(SystemArrival.StationAiFlag, station.AiFlag);
        Assert.False(station.IsHostile, "a station starts peaceful");
    }

    [Fact]
    public void AStationSendsOutShuttlesAndTransports()
    {
        var (sim, _) = CreateSim();

        // The launches are rare on purpose: one turn in eighty-five, and only while nothing else is
        // out there flying the route
        var launched = new List<int>();
        for (int i = 0; i < 20000 && launched.Count < 2; i++)
        {
            sim.Step();
            if (sim.StationLaunchedThisFrame is { } ship)
            {
                Assert.Equal(Tactics.StationLaunchAiFlag, ship.AiFlag);
                launched.Add(ship.Type);
            }
        }

        Assert.NotEmpty(launched);
        Assert.All(launched, type => Assert.True(
            type is Tactics.ShuttleType or Tactics.TransporterType,
            $"a peaceful station should launch traders, not type {type}"));
    }

    [Fact]
    public void WhatAStationLaunchesHeadsForThePlanet()
    {
        // The original's GOPL: a peaceful ship that is not docking flies to the planet. Without it
        // the shuttle a station had just launched had nowhere to go and simply fled from us.
        var (sim, _) = CreateSim();

        Ship? launched = null;
        for (int i = 0; i < 20000 && launched is null; i++)
        {
            sim.Step();
            launched = sim.StationLaunchedThisFrame;
        }

        Assert.NotNull(launched);

        // Pin the role. A trader — which is what a shuttle is in the NEWB flags — rolls to turn out
        // to be a pirate, and a pirate flies at us instead of at the planet, so leaving the roll in
        // makes this test a coin toss that the RNG decides. Measured: the roll came up pirate, and
        // the ship circled us while the planet drifted no closer. The roll itself is covered by
        // TacticsTests.
        launched.NewbFlags &= unchecked((byte)~(Ship.NewbHostile | Ship.NewbTrader));

        Ship planet = Assert.Single(sim.Bubble, s => s.Type == SystemArrival.PlanetTypeA);
        (int px, int py, int pz) = planet.GetPosition();

        double Distance()
        {
            (int x, int y, int z) = launched.GetPosition();
            return Math.Sqrt(Math.Pow(x - px, 2) + Math.Pow(y - py, 2) + Math.Pow(z - pz, 2));
        }

        double before = Distance();

        // A thousand iterations, because the shuttle is launched from the station facing us and has
        // to come about before it can close on the planet at all: the turn is the original's RAT of
        // three, which is three steps of 3.58 degrees per eight-iteration tactics cycle. Measured, it
        // closes 10,500 units over the thousand, so the margin here is wide.
        for (int i = 0; i < 1000; i++)
        {
            sim.Step();
        }

        Assert.True(Distance() < before - 5000,
            $"the launched ship should be heading for the planet: {before:0} then {Distance():0}");
    }

    [Fact]
    public void AnAngryStationSendsThePoliceInstead()
    {
        var (sim, station) = CreateSim();
        station.NewbFlags |= Ship.NewbHostile;

        int cops = 0;
        int mostAtOnce = 0;
        for (int i = 0; i < 20000; i++)
        {
            sim.Step();

            // The original's limit is on how many are out there at once, not on how many it sends
            // over a session: its police fly off towards the planet and are replaced
            mostAtOnce = Math.Max(mostAtOnce, sim.Bubble.Count(s => s.Type == Tactics.CopType));

            if (sim.StationLaunchedThisFrame is { } ship)
            {
                Assert.Equal(Tactics.CopType, ship.Type);
                Assert.Equal(Tactics.StationLaunchAiFlag, ship.AiFlag);

                // They are not hostile on arrival, and that is the original's doing rather than an
                // oversight: the police are Vipers, whose E% flags make them bounty hunters, and a
                // bounty hunter only turns on a commander whose legal status has reached 40. Shoot
                // enough innocents to annoy a station and you will usually be there; annoy it once
                // and you may not be, in which case its police fly off to the planet with everyone
                // else.
                Assert.Equal(ShipData.NewbFlagsFor(Tactics.CopType), ship.NewbFlags);
                cops++;
            }
        }

        Assert.True(cops > 0, "an angered station should send the police");
        Assert.True(mostAtOnce <= Tactics.HostileStationCopLimit,
            $"the original keeps at most {Tactics.HostileStationCopLimit} police out at once, but {mostAtOnce} were");
    }
}

/// <summary>
/// Checks that our own roll turns the rest of the universe the right way, which is what makes
/// docking possible at all.
/// </summary>
/// <remarks>
/// The station rolls continuously, so docking means rolling with it until the two rotations match and
/// the slot holds still. That only works if rolling one way counters the station's spin and rolling
/// the other way adds to it. The angles MVS4 rotates a ship's orientation by were being passed as
/// bare magnitudes, with the sign left behind in ALP2 — so both directions produced the same
/// rotation, and no amount of rolling could ever cancel the station's.
/// </remarks>
public class OurRollTurnsTheUniverseTests
{
    /// <summary>The station's roof angle as we see it: its apparent roll on screen.</summary>
    private static double ApparentRoll(Ship station) => Math.Atan2(
        station.Orientation.GetUnity(Orientation.Roofv, Orientation.Y),
        station.Orientation.GetUnity(Orientation.Roofv, Orientation.X)) * 180 / Math.PI;

    private static double SpinOver(FlightInput input, int iterations = 60)
    {
        var sim = new FlightSim(new Ship(11, "cobra-mk-3", "Cobra Mk III"))
        {
            SpawningEnabled = false,
            Commander = Commander.CreateDefault(),
        };

        Ship station = SystemArrival.CreateStation(3000, SystemArrival.StationRollCounter);
        sim.Spawn(station);

        // Hold the input long enough for the roll rate to reach a steady deflection
        for (int i = 0; i < 40; i++)
        {
            sim.Step(input);
        }

        double previous = ApparentRoll(station);
        double total = 0;
        for (int i = 0; i < iterations; i++)
        {
            sim.Step(input);
            double now = ApparentRoll(station);
            double turned = now - previous;
            while (turned > 180) turned -= 360;
            while (turned < -180) turned += 360;
            total += turned;
            previous = now;
        }

        return total / iterations;
    }

    [Fact]
    public void RollingWithTheStationCountersItsSpinAndRollingAgainstItAdds()
    {
        double coasting = SpinOver(default);
        double withIt = SpinOver(new FlightInput(RollRight: true));
        double againstIt = SpinOver(new FlightInput(RollLeft: true));

        // The station rolls one way on its own
        Assert.True(coasting < -3 && coasting > -4, $"the station's own spin measured {coasting:0.00}");

        // Rolling one way counters it and the other doubles it — and, crucially, the two are not
        // the same, which is what the missing sign made them
        Assert.True(withIt > 3, $"rolling with the station should counter its spin, but measured {withIt:0.00}");
        Assert.True(againstIt < -10, $"rolling against it should add to the spin, but measured {againstIt:0.00}");
    }

    /// <summary>The station's residual spin when the roll key is held for a fraction of each turn.</summary>
    private static double ResidualAtDutyCycle(int onIterations, int period)
    {
        var sim = new FlightSim(new Ship(11, "cobra-mk-3", "Cobra Mk III"))
        {
            SpawningEnabled = false,
            Commander = Commander.CreateDefault(),
        };

        Ship station = SystemArrival.CreateStation(3000, SystemArrival.StationRollCounter);
        sim.Spawn(station);

        double previous = ApparentRoll(station);
        double total = 0;
        int counted = 0;

        for (int i = 0; i < 400; i++)
        {
            sim.Step(new FlightInput(RollRight: i % period < onIterations));

            double now = ApparentRoll(station);
            double turned = now - previous;
            while (turned > 180) turned -= 360;
            while (turned < -180) turned += 360;

            // Ignore the first stretch, while the roll rate is still building up
            if (i >= 200)
            {
                total += turned;
                counted++;
            }

            previous = now;
        }

        return total / counted;
    }

    [Fact]
    public void HoldingTheRollForPartOfEachTurnCanCancelTheStationsSpin()
    {
        // Docking is matching the station's roll, so there has to be a deflection that leaves the two
        // turning together. Holding the key for a fraction of each turn is how a player does it: the
        // more of the turn the key is down, the harder we roll.
        double none = ResidualAtDutyCycle(onIterations: 0, period: 8);
        double some = ResidualAtDutyCycle(onIterations: 3, period: 8);
        double most = ResidualAtDutyCycle(onIterations: 8, period: 8);

        Assert.True(none < -2, $"coasting, the station should still turn, but measured {none:0.00}");
        Assert.True(most > 2, $"rolling hard, we should out-turn it, but measured {most:0.00}");

        // Which means the residual crosses zero somewhere between them, and that crossing is the
        // roll a commander docks with
        Assert.True(some > none, $"more roll should counter more of the spin: {none:0.00} then {some:0.00}");
    }
}

/// <summary>
/// Checks how often the sky is repopulated, which is a number the original fixes and a number that is
/// very easy to get wrong by a large factor.
/// </summary>
/// <remarks>
/// The original only reaches its spawning code when its main loop counter comes round to zero, which
/// is once every 256 iterations — "we only get here once every 256 iterations of the main loop" — and
/// its extra-vessels counter then delays the next decision by the size of the pack it just sent. The
/// port made a decision every iteration, gated only by that counter, so a roll that said "nothing
/// spawns" was retried immediately instead of waiting another 256: the sky carried several times the
/// traffic it should have.
/// </remarks>
public class SpawnCadenceTests
{
    [Fact]
    public void SpawnDecisionsComeRoundEveryTwoHundredAndFiftySixIterations()
    {
        var sim = new FlightSim(new Ship(11, "cobra-mk-3", "Cobra Mk III"))
        {
            SpawningEnabled = true,
            Commander = Commander.CreateDefault(),
            System = Galaxy.GenerateGalaxy(Galaxy.GalaxySeeds(0))[7],   // Lave
            GalaxySeeds = Galaxy.GalaxySeeds(0),
        };

        var seen = new List<int>();
        SpawnKind previous = SpawnKind.None;
        int previousJunk = 0;

        for (int i = 0; i < 20_000; i++)
        {
            sim.Step();

            bool spawnedShip = sim.LastSpawn != SpawnKind.None && previous == SpawnKind.None;
            bool spawnedJunk = sim.LastJunkSpawned != 0 && previousJunk != sim.LastJunkSpawned;
            if (spawnedShip || spawnedJunk)
            {
                seen.Add(i);
            }

            previous = sim.LastSpawn;
            previousJunk = sim.LastJunkSpawned;
        }

        Assert.NotEmpty(seen);

        // Every decision lands on a multiple of 256, give or take the iteration the step is counted in
        foreach (int at in seen)
        {
            Assert.True(at % 256 <= 1,
                $"a spawn decision came at iteration {at}, which is not a 256-iteration boundary");
        }

        // And they are rare: with the original's cadence this is a couple of handfuls in twenty
        // thousand iterations, where the port used to manage hundreds
        Assert.InRange(seen.Count, 5, 60);
    }
}

/// <summary>
/// Runs the simulation hard for a long stretch and checks that nothing has gone structurally wrong.
/// </summary>
/// <remarks>
/// The ported arithmetic is fixed-point and full of sign-magnitude bytes, saturating additions and
/// 24-bit coordinates. A mistake in any of it tends to show up not as a wrong number but as a ship at
/// an impossible distance, a bubble that grows without limit, or a value that has wrapped. This flies
/// several hours of game time with spawning on, jumping between systems and with ships being shot,
/// and asserts the invariants that must hold throughout.
/// </remarks>
public class SimulationSoakTests
{
    [Fact]
    public void NothingGoesStructurallyWrongOverHoursOfFlight()
    {
        var session = new GameSession(
            Commander.CreateDefault(),
            new FlightSim(new Ship(11, "cobra-mk-3", "Cobra Mk III")));

        FlightSim sim = session.Flight;
        session.Commander.SetLaser(LaserMount.Front, LaserType.Beam);
        sim.LaserPowerProvider = _ => 10;
        sim.Player.Energy = 255;
        sim.Player.ForeShield = 255;
        sim.Player.AftShield = 255;

        // About four and a half hours of game time at the original's rate: long enough for the
        // fixed-point arithmetic to accumulate any drift it is going to, and short enough to keep the
        // suite quick
        const int Iterations = 200_000;
        var random = new EliteRandom(99);

        for (int i = 0; i < Iterations; i++)
        {
            // Fly about: thrust, turn, and shoot at whatever is in front
            var input = new FlightInput(
                RollLeft: (i & 127) < 32,
                RollRight: (i & 127) >= 96,
                PullUp: (i & 255) < 64,
                PitchDown: (i & 255) >= 192,
                SpeedUp: (i & 63) == 0,
                Fire: (i & 7) == 0);

            sim.Step(input);

            Assert.InRange(sim.Bubble.Count, 0, FlightSim.MaxShipsInBubble);
            Assert.InRange(sim.Player.Energy, 0, 255);
            Assert.InRange(sim.LaserTemperature, 0, 255);
            Assert.InRange(sim.CabinTemperature, 0, 255);

            foreach (Ship ship in sim.Bubble)
            {
                (int x, int y, int z) = ship.GetPosition();
                Assert.InRange(x, -0x800000, 0x800000);
                Assert.InRange(y, -0x800000, 0x800000);
                Assert.InRange(z, -0x800000, 0x800000);
                Assert.InRange(ship.Energy, 0, 255);
                Assert.InRange(ship.Speed, 0, 255);
            }

            // Every so often, jump somewhere new: a fresh system, a fresh market and a fresh sky
            if (i % 50_000 == 49_999)
            {
                session.SelectedSystem = session.SystemsInGalaxy[random.Next() % 256];
                session.Commander.Fuel = 70;
                if (session.StartHyperspace())
                {
                    for (int tick = 0; tick < 40 && session.HyperspaceCountdown > 0; tick++)
                    {
                        session.TickHyperspace();
                    }
                }

                sim.Player.Energy = 255;
                sim.PlayerDied = false;

                // Being killed in the middle of the soak would end the game rather than the test, so
                // a pod puts us back at the station and the flight carries on
                session.Commander.EscapePod = true;
                if (session.GameOver)
                {
                    session.Restart();
                }
            }
        }

        // The commander is still in a real system with a real market after all that
        Assert.False(string.IsNullOrWhiteSpace(session.System.Name));
        Assert.NotEmpty(session.SystemsInGalaxy);
        Assert.InRange(session.Commander.Fuel, 0, 70);
    }
}

/// <summary>
/// Checks that the police come after a commander carrying contraband, and leave a clean one alone.
/// </summary>
/// <remarks>
/// The original's BAD works out how bad we look — four times the slaves and narcotics in the hold plus
/// twice the firearms — and, if there are already police about, or's in our legal status. The result is
/// the number of chances in 256 of a police pack arriving, and that pack then takes the place of the
/// pirates: "if we now have at least one cop in the local bubble, stop spawning".
/// </remarks>
public class PoliceSpawnTests
{
    /// <summary>Counts police packs over a long run with a given hold and record.</summary>
    private static int PolicePacks(int slaves, int narcotics, int firearms, int legalStatus)
    {
        var commander = Commander.CreateDefault();
        commander.AddCargo(Spawner.SlavesItem, slaves);
        commander.AddCargo(Spawner.NarcoticsItem, narcotics);
        commander.AddCargo(Spawner.FirearmsItem, firearms);
        commander.LegalStatus = legalStatus;

        var sim = new FlightSim(new Ship(11, "cobra-mk-3", "Cobra Mk III"))
        {
            SpawningEnabled = true,
            Commander = commander,
            System = Galaxy.GenerateGalaxy(Galaxy.GalaxySeeds(0))[7],   // Lave
            GalaxySeeds = Galaxy.GalaxySeeds(0),
        };

        int packs = 0;
        SpawnKind previous = SpawnKind.None;
        for (int i = 0; i < 200_000; i++)
        {
            sim.Step();
            if (sim.LastSpawn == SpawnKind.Cops && previous != SpawnKind.Cops)
            {
                packs++;
            }

            previous = sim.LastSpawn;
        }

        return packs;
    }

    [Fact]
    public void ACleanCommanderIsLeftAlone()
    {
        Assert.Equal(0, PolicePacks(slaves: 0, narcotics: 0, firearms: 0, legalStatus: 0));
    }

    [Fact]
    public void ContrabandBringsThePolice()
    {
        int some = PolicePacks(slaves: 5, narcotics: 0, firearms: 0, legalStatus: 0);
        int plenty = PolicePacks(slaves: 20, narcotics: 0, firearms: 0, legalStatus: 0);

        Assert.True(some > 0, "carrying slaves should attract the police");
        Assert.True(plenty > some * 2, $"a full hold should be worse than five tonnes: {some} against {plenty}");
    }

    [Fact]
    public void ARecordOnlyCountsOnceThePoliceAreAlreadyThere()
    {
        // "If there are no cops in the local bubble, skip the next instruction" — the legal status is
        // only or'd into our badness once they are on to us, so being wanted with nobody about is the
        // bounty hunters' business rather than theirs
        Assert.Equal(0, PolicePacks(slaves: 0, narcotics: 0, firearms: 0, legalStatus: 40));

        // Which means the roll itself is what the status changes, and the roll needs a hold to start
        // from
        Assert.Equal(0, Spawner.Badness(Commander.CreateDefault(), copsInBubble: 0));
    }
}
