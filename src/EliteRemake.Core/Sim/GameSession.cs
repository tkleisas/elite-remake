using EliteRemake.Core.Universe;

namespace EliteRemake.Core.Sim;

/// <summary>The station's screens.</summary>
public enum DockedScreen
{
    /// <summary>The market, where cargo is bought and sold.</summary>
    Market,

    /// <summary>The equipment shop.</summary>
    Equipment,

    /// <summary>The short-range chart.</summary>
    ShortRangeChart,

    /// <summary>The long-range chart.</summary>
    LongRangeChart,

    /// <summary>Data on System.</summary>
    DataOnSystem,

    /// <summary>The commander's status and inventory.</summary>
    Status,

    /// <summary>
    /// The control settings.
    /// </summary>
    /// <remarks>
    /// Not a screen the original has — its keys were fixed and there was no way to change them —
    /// so this is a modern addition, kept out of the way of the original's own screens.
    /// </remarks>
    Settings,
}

/// <summary>Where the player is: flying, or docked at the station.</summary>
public enum GameMode
{
    /// <summary>On the start screen, before the game has begun.</summary>
    Title,

    /// <summary>In flight.</summary>
    Flying,

    /// <summary>Docked, looking at the station's screens.</summary>
    Docked,
}

/// <summary>
/// A game in progress: the commander, the system they are in, the flight simulation and the
/// station's market.
/// </summary>
/// <remarks>
/// Docking regenerates the market, as the original does when you arrive: GVL picks a fresh random
/// byte for the visit, and that one byte decides both the prices and the quantities available. The
/// byte is drawn from a generator seeded from the system's own seeds, so a given system always
/// behaves consistently, and it is re-drawn every time you dock.
/// </remarks>
public sealed class GameSession
{
    private readonly EliteRandom _random;

    public GameSession(Commander commander, FlightSim flight)
    {
        Commander = commander;
        Flight = flight;
        Flight.Commander = commander;
        System = commander.CurrentSystem;

        // Seed the generator from the system's seeds so the same system always gets the same
        // sequence of markets
        uint seed = (uint)((System.Seeds.S0 << 16) | System.Seeds.S1);
        seed ^= (uint)System.Seeds.S2 << 8;
        _random = new EliteRandom(seed);

        Market = Universe.Market.Build(System, _random.Next());

        // The charts start with the crosshairs on the system we are in
        SelectedSystem = System;

        Missions = Missions.FromStatusByte(commander.MissionStatus);

        // The simulation needs the missions: the Constrictor's appearance, the Thargoid intercepts and
        // the plans are all decided inside it, from the mission rules. This was never set, so the
        // simulation always saw no missions at all and the Constrictor could never appear — mission 1
        // was uncompletable in the game, though every mission test passed because they call the mission
        // rules directly rather than flying to the system.
        Flight.Missions = Missions;
        SyncSimulationToUniverse();
    }

    /// <summary>
    /// Tells the simulation which system and galaxy we are in.
    /// </summary>
    /// <remarks>
    /// The mission rules are stated in terms of both — the Constrictor is in its own galaxy, the plans
    /// and their delivery in another — and the simulation reads them when it decides whether to spawn a
    /// mission ship. Keeping the two in step was being done by hand, in one place out of four: the
    /// simulation kept the system it was loaded with, so after a jump the Constrictor would not appear
    /// in its own system even once the galaxy was right.
    /// </remarks>
    private void SyncSimulationToUniverse()
    {
        Flight.System = System;
        Flight.GalaxyNumber = Commander.GalaxyNumber;
        Flight.GalaxySeeds = Universe.Galaxy.GalaxySeeds(Commander.GalaxyNumber);
    }

    /// <summary>The commander.</summary>
    public Commander Commander { get; private set; }

    /// <summary>The flight simulation, which is paused while we are docked.</summary>
    public FlightSim Flight { get; }

    /// <summary>The system we are in.</summary>
    public StarSystem System { get; private set; }

    /// <summary>The station's market for this visit.</summary>
    public MarketEntry[] Market { get; private set; }

    /// <summary>Whether we are flying or docked.</summary>
    public GameMode Mode { get; set; } = GameMode.Flying;

    /// <summary>Which station screen we are looking at, when docked.</summary>
    public DockedScreen Screen { get; set; } = DockedScreen.Market;

    /// <summary>An optional message to show the player, such as the result of a trade.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>The system the charts have selected, which a hyperspace jump will take us to.</summary>
    public StarSystem SelectedSystem { get; set; }

    /// <summary>
    /// How many times we have arrived somewhere, so a screen can tell a new visit from a redraw.
    /// </summary>
    public int Visit { get; private set; }

    /// <summary>
    /// The generator the system descriptions draw their phrases from. It advances as descriptions
    /// are generated, so the same system can be described differently on different visits — which
    /// is what the original's random phrase choice does.
    /// </summary>
    public EliteRandom DescriptionRandom { get; } = new();

    /// <summary>Frames left on the hyperspace countdown, or 0 when we are not jumping.</summary>
    public int HyperspaceCountdown { get; private set; }

    /// <summary>The distance to the selected system, in tenths of a light year.</summary>
    public int SelectedDistance => Galaxy.DistanceTenths(System, SelectedSystem);

    /// <summary>
    /// Starts a hyperspace jump to the selected system, as the original's hyp routine does: the
    /// jump needs enough fuel for the distance, and then runs a countdown before we arrive.
    /// </summary>
    public bool StartHyperspace()
    {
        if (HyperspaceCountdown > 0)
        {
            return false;
        }

        if (SelectedSystem.Seeds == System.Seeds)
        {
            Message = "You are already here.";
            return false;
        }

        int distance = SelectedDistance;
        if (distance > Commander.Fuel)
        {
            Message = $"Not enough fuel: {FormatTenths(distance)} light years needed, " +
                      $"{FormatTenths(Commander.Fuel)} in the tank.";
            return false;
        }

        InWitchspace = false;     // a jump out of witchspace is an ordinary one
        HyperspaceCountdown = 20; // the original counts down before the jump
        Message = $"Hyperspace to {SelectedSystem.Name} ({FormatTenths(distance)} light years).";
        return true;
    }

    /// <summary>
    /// Completes the jump: the fuel is spent, we arrive in the new system, and its station, planet
    /// and sun are set up.
    /// </summary>
    public bool CompleteHyperspace()
    {
        if (HyperspaceCountdown > 0)
        {
            return false;
        }

        int distance = SelectedDistance;

        // The commander spends the fuel through the same method the rest of the game uses, so the
        // check and the deduction cannot come apart. This was a hand-written check and subtraction
        // beside a tested UseFuel that nothing called.
        if (!Commander.UseFuel(distance))
        {
            return false;
        }

        // The jump can go wrong. The fuel is spent either way, but a mis-jump leaves us in
        // witchspace rather than at the destination, which is what the caller's arrival then has to
        // deal with.
        InWitchspace = ForceMisjump || IsMisjump(_random.Next());
        ForceMisjump = false;
        if (InWitchspace)
        {
            Message = "Hyperspace malfunction! You have been thrown into witchspace.";
            return true;
        }

        Commander.CurrentSystem = SelectedSystem;
        System = SelectedSystem;
        Visit++;
        Market = Universe.Market.Build(System, _random.Next());
        SyncSimulationToUniverse();

        // Arriving somewhere new improves our record: SOLAR halves our legal status with an LSR every
        // time we arrive in a system, so a commander who lies low and keeps jumping works his way
        // back to clean. Bit 0 is lost, and it is not lost silently: the carry from that same shift
        // is what SOLAR's planet distance adds to its 3 to 7, so it decides whether the planet sits
        // one step of 65536 further away. The bit is kept here for the sky that is built next.
        ArrivalStatusCarry = Commander.LegalStatus & 1;
        if (Commander.LegalStatus > 0)
        {
            Commander.LegalStatus /= 2;
        }

        Message = $"Arrived in the {System.Name} system.";
        return true;
    }

    /// <summary>
    /// The bit our legal status loses when the arrival halves it, which the original's planet
    /// distance is built from. See <see cref="SystemArrival.CreatePlanet"/>.
    /// </summary>
    public int ArrivalStatusCarry { get; private set; }

    /// <summary>
    /// True when the last jump went wrong and we are in witchspace.
    /// </summary>
    /// <remarks>
    /// The disc version rolls a random byte as it jumps and mis-jumps on 253 or more, which its own
    /// comment gives as a 0.78% chance. A mis-jump leaves us where we were: MJP loads the Thargoid
    /// blueprints, shows the tunnel again, resets the flight variables, sets its MJ flag and spawns
    /// four Thargoids, each with a Thargon in attendance, and there is no planet or sun to be seen.
    ///
    /// The flag lives on the flight simulation, which is what reads it: WARP refuses an in-system
    /// jump while it is set.
    /// </remarks>
    public bool InWitchspace
    {
        get => Flight.InWitchspace;
        private set => Flight.InWitchspace = value;
    }

    /// <summary>The random byte at or above which a jump mis-jumps: the original's <c>CMP #253</c>.</summary>
    public const int MisjumpThreshold = 253;

    /// <summary>
    /// Forces the next jump to mis-jump instead of arriving, which is what holding CTRL down as the
    /// countdown ends does.
    /// </summary>
    /// <remarks>
    /// The original reaches MJP by two routes from TT18: this one, when CTRL is held, and a random
    /// byte of 253 or more. It gates the CTRL route behind the author-names flag, so that the
    /// competition code would record whether the feature had been used — an anti-cheat measure for
    /// a 1984 contest rather than a game rule. There is no competition to protect here, so the
    /// key simply works.
    /// </remarks>
    public bool ForceMisjump { get; set; }

    /// <summary>
    /// Decides whether a jump mis-jumps, as the disc version does with a random byte compared
    /// against 253.
    /// </summary>
    /// <param name="randomByte">The random byte, which the original gets from DORND.</param>
    public static bool IsMisjump(int randomByte) => (randomByte & 0xFF) >= MisjumpThreshold;

    /// <summary>
    /// Starts a jump for display purposes, without a destination or fuel to pay for it.
    /// </summary>
    /// <remarks>
    /// This exists so the hyperspace tunnel can be looked at from the command line. Nothing else
    /// should use it: a real jump goes through <see cref="StartHyperspace"/>, which checks the fuel.
    /// </remarks>
    public void ForceHyperspaceForDisplay()
    {
        InWitchspace = false;
        HyperspaceCountdown = 20;
        Message = "Hyperspace drive engaged.";
    }

    /// <summary>
    /// The galaxy we are in, from its own seeds.
    /// </summary>
    /// <remarks>
    /// This is the only correct way to ask for the galaxy: a *system's* seeds are not the galaxy's
    /// except by coincidence for system 0, whose seeds are the galaxy's own. Generating from another
    /// system's seeds gives an entirely different galaxy — all 256 systems differ — which is what the
    /// charts used to show once the commander had left Lave, and what the nearest-reachable-system
    /// searches were picking destinations from.
    /// </remarks>
    public StarSystem[] SystemsInGalaxy => Universe.Galaxy.GenerateGalaxy(
        Universe.Galaxy.GalaxySeeds(Commander.GalaxyNumber));

    /// <summary>Advances the hyperspace countdown, returning true when the jump completes.</summary>
    public bool TickHyperspace()
    {
        if (HyperspaceCountdown <= 0)
        {
            return false;
        }

        HyperspaceCountdown--;
        if (HyperspaceCountdown > 0)
        {
            return false;
        }

        return CompleteHyperspace();
    }

    /// <summary>Formats tenths of a light year the way the original prints them.</summary>
    public static string FormatTenths(int tenths) => $"{tenths / 10}.{Math.Abs(tenths % 10)}";

    /// <summary>
    /// Uses the galactic hyperdrive, which the original fires with CTRL-H: the drive is consumed
    /// and the galaxy seeds are rotated one bit to the left, so we arrive in the next galaxy with
    /// everything except the drive intact.
    /// </summary>
    public bool UseGalacticHyperdrive()
    {
        if (!Commander.GalacticHyperdrive)
        {
            Message = "No galactic hyperdrive fitted.";
            return false;
        }

        if (HyperspaceCountdown > 0)
        {
            return false;
        }

        Commander.GalacticHyperdrive = false;
        Commander.GalaxyNumber = (Commander.GalaxyNumber + 1) % Galaxy.GalaxyCount;

        // The original's GHY routine rotates the *galaxy's* seed table one bit to the left, raises
        // the galaxy number, and then finds the nearest system to (96, 96) — the point it always
        // arrives at. It is not the same system number: rotating a system's own seeds is not the
        // same operation at all, and the nearest system to (96, 96) would very rarely be the one
        // that index names.
        StarSystem arrived = Galaxy.FindClosest(
            Galaxy.GalaxySeeds(Commander.GalaxyNumber),
            96,
            96).System;

        System = arrived;
        Commander.CurrentSystem = System;
        SelectedSystem = System;

        // The simulation has to follow, or the mission rules would still be looking in the old galaxy
        SyncSimulationToUniverse();

        Market = Universe.Market.Build(System, _random.Next());
        Message = $"Galactic hyperspace: arrived in galaxy {Commander.GalaxyNumber + 1}, {System.Name} system.";
        return true;
    }

    /// <summary>True when the commander is wanted, which the status screen shows.</summary>
    public bool IsWanted => Commander.LegalStatus > 0;

    /// <summary>The two missions the original offers, and the state they are in.</summary>
    public Missions Missions { get; private set; }

    /// <summary>
    /// Docks at the station: the market is regenerated, and any mission business is settled —
    /// picking up the plans, delivering them, or being offered the next mission.
    /// </summary>
    public void HandleMissionArrival()
    {
        if (Missions.PickUpPlans(System, Commander.GalaxyNumber))
        {
            Commander.MissionStatus = Missions.StatusByte;
            Message = "You have collected the plans. The Thargoids will be looking for you.";
            return;
        }

        if (Missions.DeliverPlans(System, Commander.GalaxyNumber))
        {
            Commander.MissionStatus = Missions.StatusByte;
            Commander.Cash += 10000;
            Message = "The plans are delivered. The Navy pays 1,000 credits.";
            return;
        }

        if (Missions.OfferMission1(Commander))
        {
            Message = "A Navy officer offers you a mission: hunt down a Constrictor in galaxy 2.";
        }
        else if (Missions.OfferMission2())
        {
            Message = "A Navy officer asks you to carry documents for them.";
        }
    }

    /// <summary>Accepts whichever mission has been offered.</summary>
    public bool AcceptMission()
    {
        if (Missions.OfferMission1(Commander))
        {
            Missions.AcceptMission1();
            Commander.MissionStatus = Missions.StatusByte;
            Message = "Mission accepted: find and destroy the Constrictor.";
            return true;
        }

        if (Missions.OfferMission2())
        {
            Missions.AcceptMission2();
            Commander.MissionStatus = Missions.StatusByte;
            Message = "Mission accepted: collect the plans.";
            return true;
        }

        return false;
    }

    /// <summary>The default path of the commander's save file.</summary>
    public static string DefaultSavePath =>
        Path.Combine(AppContext.BaseDirectory, "commander.json");

    /// <summary>True when there is a saved commander available to load.</summary>
    /// <remarks>
    /// The start screen offers "load commander" only when there is something to load, and this is
    /// asked of the same path <see cref="TryLoad"/> reads, so the menu cannot promise a load that
    /// then fails for want of a file.
    /// </remarks>
    public static bool HasSave => File.Exists(DefaultSavePath);

    /// <summary>
    /// Replaces the commander in play with a brand new one, which is what the start screen's "new
    /// commander" does and what the original asks for by name on the disc.
    /// </summary>
    /// <remarks>
    /// Everything that hangs off the commander has to be rebuilt with it, exactly as
    /// <see cref="Load"/> does and for the same reasons: the simulation holds the commander, the
    /// missions live in the status byte, and the ship's equipment is the commander's.
    /// </remarks>
    public void NewCommander()
    {
        Commander fresh = Commander.CreateDefault();
        Load(fresh);
        GameOver = false;

        // A new commander comes with a new ship, so the wreck's empty banks and shields go with it.
        // Load replaces everything that hangs off the commander but not the ship the commander is
        // sitting in, and restarting after a death used to hand back a commander in a wreck.
        Flight.Player.Energy = NewShipEnergy;
        Flight.Player.ForeShield = 255;
        Flight.Player.AftShield = 255;
        Flight.Player.IsExploding = false;
        Flight.Player.IsKilled = false;

        Message = $"A new commander: {fresh.Name}.";
    }

    /// <summary>
    /// Saves the commander, as the original does when docked. Returns a message describing what
    /// happened, so the docked screens can show it.
    /// </summary>
    public string Save(string? path = null)
    {
        if (Mode != GameMode.Docked)
        {
            Message = "You can only save while docked.";
            return Message;
        }

        // The missions live in the commander's status byte, so saving keeps them
        Commander.MissionStatus = Missions.StatusByte;

        string target = path ?? DefaultSavePath;
        try
        {
            File.WriteAllText(target, CommanderSave.FromCommander(Commander).ToJson());
            Message = $"Commander saved to {Path.GetFileName(target)}.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Message = $"Could not save: {error.Message}";
        }

        return Message;
    }

    /// <summary>
    /// Loads a saved commander, replacing the one in play. Returns null on success, or a message
    /// explaining why the load failed.
    /// </summary>
    public string? TryLoad(string? path = null)
    {
        string target = path ?? DefaultSavePath;
        if (!File.Exists(target))
        {
            return $"No save file at {target}.";
        }

        try
        {
            CommanderSave save = CommanderSave.FromJson(File.ReadAllText(target));
            Load(save.ToCommander());
            Message = $"Loaded commander {Commander.Name}.";
            return null;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return error.Message;
        }
    }

    /// <summary>
    /// Replaces the commander in play with a loaded one, which is what starting the game does when
    /// a save file is present.
    /// </summary>
    /// <remarks>
    /// The missions live in the commander's status byte and are rebuilt from it here, as the
    /// constructor does. Loading without this leaves the missions empty, so a commander who saved
    /// while carrying the plans or hunting the Constrictor loads with no mission at all — and the
    /// four bits of the byte that a save carries would have been written and never read back.
    /// </remarks>
    public void Load(Commander commander)
    {
        Commander = commander;
        Flight.Commander = commander;
        Flight.Player.HasEnergyUnit = commander.EnergyUnit;

        System = commander.CurrentSystem;
        SelectedSystem = System;
        Market = Universe.Market.Build(System, _random.Next());
        Missions = Missions.FromStatusByte(commander.MissionStatus);

        // As in the constructor: the simulation holds the missions, and they are rebuilt here, so it
        // has to be given the new object rather than keeping the one it had
        Flight.Missions = Missions;

        // The simulation needs the galaxy as well as the system, because the mission rules are stated
        // in terms of both: the Constrictor is only in its own galaxy, and the plans and their delivery
        // are in another. This was never set anywhere, so the simulation always believed it was in
        // galaxy 1 and the Constrictor could never appear — mission 1 was uncompletable, which no test
        // covered because they set the mission state by hand rather than flying to the system.
        SyncSimulationToUniverse();
    }

    /// <summary>Docks at the station.</summary>
    /// <remarks>
    /// The market is deliberately not rebuilt here. The original's GVL has exactly one caller in the
    /// whole source — the arrival after a hyperspace jump — and the market screen prices its items
    /// from what GVL left behind rather than recalculating. Rebuilding on docking instead means
    /// launching and redocking rerolls the prices, which is a way to shop for a good deal that the
    /// original does not offer.
    /// </remarks>
    public void Dock()
    {
        Mode = GameMode.Docked;
        Message = $"Docked at {System.Name} station.";

        // Mission business is settled on arrival, as the original's docked code does
        HandleMissionArrival();

        // A debriefing due from the Constrictor's destruction is attended now, which is where the
        // reward and the kill points are paid. The commander's status byte is what a save carries,
        // so it has to follow the missions here as well as after a kill.
        if (Missions.AttendDebrief(Commander) is { } debrief)
        {
            Message = debrief;
            Commander.MissionStatus = Missions.StatusByte;
        }
    }

    /// <summary>
    /// The Viper's ship type, which the disc's <c>E%</c> table marks as a cop along with the
    /// Transporter. Kept for callers that want a single type; the flight loop tests each ship's own
    /// <see cref="Ship.NewbFlags"/> instead, because that is what the original does.
    /// </summary>
    public const int CopType = 16;

    /// <summary>True once the commander has been killed.</summary>
    public bool GameOver { get; private set; }

    /// <summary>
    /// How much bounty a ship is worth, in tenths of a credit.
    /// </summary>
    /// <remarks>
    /// The default answers from the blueprints, which the core now carries, so a kill is worth
    /// something without the game wiring anything up. The game may still override it.
    ///
    /// The original's bounty byte is in whole credits and the commander's cash is in tenths, so the
    /// value is multiplied by ten — which is why a Sidewinder's blueprint 50 is 5.0 credits. The
    /// game layer had this multiplication and the core default returned zero, so a session without
    /// the game was paid nothing for anything it destroyed.
    /// </remarks>
    public Func<Ship, int> BountyProvider { get; set; } =
        ship => BlueprintDefaults.For(ship.Type).Bounty * 10;

    /// <summary>
    /// Awards the bounty for a destroyed ship and counts the kill, as the original's KILLSHP and
    /// kill tally do. Destroying an innocent ship costs us our legal status.
    /// </summary>
    /// <param name="destroyed">The ship we destroyed.</param>
    /// <returns>The bounty paid, in tenths of a credit.</returns>
    public int RegisterKill(Ship destroyed)
    {
        int bounty = BountyProvider(destroyed);

        // Destroying the Constrictor completes the first mission's objective, but the reward and
        // the kill points wait for the debriefing at a station, as the original's DEBRIEF does
        if (Missions.RegisterConstrictorKill(destroyed.Type))
        {
            Message = "The Constrictor is destroyed. Return to a station for the debriefing.";
        }

        Commander.Cash += bounty;

        // "LDA #0 / JSR MESS" prints control code 0 — the current cash, right-aligned, then " CR" —
        // as an in-flight message, so a kill that pays anything says so. A ship with no bounty says
        // nothing at all: the original skips the whole block when the blueprint's bounty is zero.
        if (bounty > 0)
        {
            Message = $"{Outfitting.Format(Commander.Cash)} CR";
        }

        // And every 256 kills gets a pat on the back: EXNO2's "INC TALLY / BNE ... / INC TALLY+1 /
        // LDA #101 / JSR MESS", where token 101 is "RIGHT ON COMMANDER!". Laser kills count here
        // too — part 11 calls EXNO2 when a shot kills the ship in the crosshairs, and the energy
        // bomb and missiles have their own calls — so this is the one place the tally is kept.
        int before = Commander.Kills;
        Commander.RegisterKill();
        if (before / KillMilestone != Commander.Kills / KillMilestone)
        {
            Message = "RIGHT ON COMMANDER!";
        }

        // Shooting up the innocent makes us wanted, and the original decides both halves of this
        // from the ship's own NEWB flags rather than from its type or its AI: bit 6 marks a cop,
        // whose destruction raises our legal status to at least 64 with an ORA and makes us a
        // fugitive at once, and bit 5 marks an innocent, which adds exactly 1. Fifty innocents are
        // therefore what it takes to become a fugitive by degrees.
        // The flags come from the disc's E% table, which the data extractor reads out of the
        // assembled docked code and the game stamps onto each ship as it spawns.
        if ((destroyed.NewbFlags & Ship.NewbCop) != 0)
        {
            Commander.LegalStatus = Math.Min(255, Commander.LegalStatus | Commander.CopKillStatus);
        }
        else if ((destroyed.NewbFlags & Ship.NewbInnocent) != 0)
        {
            Commander.LegalStatus = Math.Min(255, Commander.LegalStatus + 1);
        }

        // Keep the commander's status byte in step with the missions
        Commander.MissionStatus = Missions.StatusByte;
        return bounty;
    }

    /// <summary>
    /// Launches our own escape pod, which is what the original's ESCAPE key does: the pod is spent,
    /// the cargo is lost with the ship, and we are picked up at the station.
    /// </summary>
    /// <returns>True if the pod was launched; false if we have none fitted, or are not flying.</returns>
    /// <remarks>
    /// The original refuses the key when ESCP is clear and does nothing else at all — there is no
    /// message and no beep — which is what this returns false for. It shows our abandoned Cobra
    /// drifting away first, through ninety-seven passes of its inner loop; the remake goes straight
    /// to the station, which is recorded as a departure.
    /// </remarks>
    public bool LaunchEscapePod()
    {
        if (Mode != GameMode.Flying || !Commander.EscapePod)
        {
            return false;
        }

        HandlePlayerDeath();
        return true;
    }

    /// <summary>
    /// Handles the destruction of our ship. With an escape pod fitted the commander survives, loses
    /// the cargo and wakes up in the station, as the original does; without one it is game over,
    /// and the commander is rebuilt from scratch (until save and load arrive, this stands in for
    /// reloading the last saved commander).
    /// </summary>
    public void HandlePlayerDeath()
    {
        if (Commander.EscapePod)
        {
            // The original's ESCAPE routine does four things before it hands us to the station: it
            // empties all seventeen cargo slots, clears our criminal record, spends the pod, and
            // delivers a replacement ship with a full tank. The fuel matters — being rescued with an
            // empty tank and no cargo to sell would strand a commander with no way to earn.
            for (int item = 0; item < 17; item++)
            {
                Commander.RemoveCargo(item, Commander.GetCargo(item));
            }

            Commander.LegalStatus = 0;
            Commander.EscapePod = false;
            Commander.Fuel = Outfitting.MaxFuel;

            Dock();
            Message = "Escape pod launched: cargo lost, but you were picked up at the station.";
            return;
        }

        GameOver = true;
        Message = "GAME OVER - press ESC or F10 to leave, or N for a new commander";
    }

    /// <summary>Starts again with a fresh commander, as reloading a save does.</summary>
    public void Restart()
    {
        GameOver = false;
        NewCommander();
        Message = "New commander: 100 credits, full tank, pulse laser, three missiles.";
    }

    /// <summary>The energy a freshly delivered Cobra Mk III comes with.</summary>
    public const int NewShipEnergy = 150;

    /// <summary>Launches from the station.</summary>
    public void Launch()
    {
        Mode = GameMode.Flying;
        Message = string.Empty;
        Launches++;

        // We launch looking out of the front window, which is TT110: it reaches LOOK1 with X = 0
        // whether it is launching us or bringing us out of a hyperspace jump.
        Flight.View = SpaceView.Front;

        // TT110 resets the flight variables with RES2 and then builds the sky it wants us to see:
        // the planet dead ahead at one step of 65536 — "INC INWK+8 ... so the planet appears at a
        // z_sign of 1 in front of us when we launch" — and the station just behind us, at 256 units,
        // so that we come out of its slot. The sun does not come back: the station took its slot.
        //
        // Our launch speed is DELTA = 12, which is what shoots us clear of the station.
        Flight.Speed = LaunchSpeed;
        Flight.ClearBubble();
        Flight.Spawn(SystemArrival.CreatePlanet(System, ArrivalStatusCarry).MovedTo(0, 0, PlanetAheadOnLaunch));
        Flight.SpawnStationAt(0, 0, -StationBehindOnLaunch);
    }

    /// <summary>
    /// The speed TT110 gives us as we leave the station: "LDA #12 / STA DELTA".
    /// </summary>
    public const byte LaunchSpeed = 12;

    /// <summary>
    /// How far ahead of us TT110 puts the planet when we launch: "INC INWK+8 ... a z_sign of 1 in
    /// front of us", which is one step of 65536 units.
    /// </summary>
    public const int PlanetAheadOnLaunch = 65536;

    /// <summary>
    /// How far behind us TT110 puts the station when we launch: "LDA #128 / STA INWK+8" for the sign
    /// and "INC INWK+7" for the high byte, so it is 256 units behind us, "only just behind us".
    /// </summary>
    public const int StationBehindOnLaunch = 256;

    /// <summary>
    /// Buys an equipment item from the station's shop, applying its effect to the commander.
    /// </summary>
    public string? BuyEquipment(int item, int lightYears = 0, LaserMount? mount = null)
    {
        string? result = Universe.Outfitting.Buy(Commander, System, item, lightYears, mount);
        Message = result ?? string.Empty;

        // A few items change how our ship behaves in flight
        Flight.Player.HasEnergyUnit = Commander.EnergyUnit;
        return result;
    }

    /// <summary>
    /// How many times we have launched from a station. The flight scene watches this so that it can
    /// draw the launch tunnel: the original's LAUN draws it as we leave, and it counts rather than
    /// flags so that a launch is never missed and never replayed.
    /// </summary>
    public int Launches { get; private set; }

    /// <summary>
    /// The kill tally at which the original offers a pat on the back: token 101, "RIGHT ON
    /// COMMANDER!", is printed every time the tally's high byte goes up, which is every 256 kills.
    /// </summary>
    public const int KillMilestone = 256;

    /// <summary>Buys as much of an item as the credits and the hold allow.</summary>
    public int Buy(int item, int amount)
    {
        MarketEntry entry = Market[item];
        if (entry.Availability <= 0)
        {
            Message = $"No {entry.Item.Name.ToLowerInvariant()} available.";
            return 0;
        }

        int affordable = entry.Price > 0 ? Commander.Cash / entry.Price : 0;
        int wanted = Math.Min(amount, Math.Min(entry.Availability, Commander.CargoFree));
        int bought = Commander.Buy(item, entry.Price, wanted);

        if (bought == 0)
        {
            Message = Commander.CargoFree == 0
                ? "Cargo hold full."
                : $"Not enough credits for {entry.Item.Name.ToLowerInvariant()}.";
            return 0;
        }

        Market[item] = entry with { Availability = entry.Availability - bought };
        Message = $"Bought {bought} {entry.Item.Unit} of {entry.Item.Name.ToLowerInvariant()} " +
                  $"for {MarketFormat(bought * entry.Price)}.";
        return bought;
    }

    /// <summary>Sells from the hold, as much as we are carrying.</summary>
    public int Sell(int item, int amount)
    {
        MarketEntry entry = Market[item];
        int held = Commander.GetCargo(item);
        if (held == 0)
        {
            Message = $"No {entry.Item.Name.ToLowerInvariant()} in the hold.";
            return 0;
        }

        // Selling does not add to the market's availability. The disc version's only write to the
        // availability table is the subtraction when buying — the only code anywhere in the library
        // that adds to it is the NES version, which has a sell path of its own — so what you sell
        // does not come back on the market for you or anyone else to buy. The cash is the whole of
        // what you get for it.
        int sold = Commander.Sell(item, entry.Price, Math.Min(amount, held));
        Message = $"Sold {sold} {entry.Item.Unit} of {entry.Item.Name.ToLowerInvariant()} " +
                  $"for {MarketFormat(sold * entry.Price)}.";
        return sold;
    }

    /// <summary>Formats an amount in tenths of a credit the way the original does.</summary>
    public static string MarketFormat(int tenths) =>
        $"{tenths / 10}.{Math.Abs(tenths % 10)}";
}
