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
    public GameMode Mode { get; private set; } = GameMode.Flying;

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
        if (distance > Commander.Fuel)
        {
            return false;
        }

        Commander.Fuel -= distance;

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
        Message = $"Arrived in the {System.Name} system.";
        return true;
    }

    /// <summary>
    /// True when the last jump went wrong and we are in witchspace.
    /// </summary>
    /// <remarks>
    /// The disc version rolls a random byte as it jumps and mis-jumps on 253 or more, which its own
    /// comment gives as a 0.78% chance. A mis-jump leaves us where we were: MJP loads the Thargoid
    /// blueprints, shows the tunnel again, resets the flight variables, sets its MJ flag and spawns
    /// four Thargoids, each with a Thargon in attendance, and there is no planet or sun to be seen.
    /// </remarks>
    public bool InWitchspace { get; private set; }

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

        Market = Universe.Market.Build(System, _random.Next());
        Message = $"Galactic hyperspace: arrived in galaxy {Commander.GalaxyNumber + 1}, {System.Name} system.";
        return true;
    }

    /// <summary>True when the commander is wanted, which the status screen shows.</summary>
    public bool IsWanted => Commander.LegalStatus > 0;

    /// <summary>The two missions the original offers, and the state they are in.</summary>
    public Missions Missions { get; }

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
    public void Load(Commander commander)
    {
        Commander = commander;
        Flight.Commander = commander;
        Flight.Player.HasEnergyUnit = commander.EnergyUnit;

        System = commander.CurrentSystem;
        SelectedSystem = System;
        Market = Universe.Market.Build(System, _random.Next());
    }

    /// <summary>Docks at the station, refreshing the market as the original does.</summary>
    public void Dock()
    {
        Mode = GameMode.Docked;
        Market = Universe.Market.Build(System, _random.Next());
        Message = $"Docked at {System.Name} station.";

        // Mission business is settled on arrival, as the original's docked code does
        HandleMissionArrival();
    }

    /// <summary>True once the commander has been killed.</summary>
    public bool GameOver { get; private set; }

    /// <summary>
    /// How much bounty a ship type is worth, from its blueprint. Set by the game, because the
    /// simulation does not know about blueprints.
    /// </summary>
    public Func<Ship, int> BountyProvider { get; set; } = _ => 0;

    /// <summary>
    /// Awards the bounty for a destroyed ship and counts the kill, as the original's KILLSHP and
    /// kill tally do. Destroying an innocent ship costs us our legal status.
    /// </summary>
    /// <param name="destroyed">The ship we destroyed.</param>
    /// <returns>The bounty paid, in tenths of a credit.</returns>
    public int RegisterKill(Ship destroyed)
    {
        int bounty = BountyProvider(destroyed);

        // Killing the Constrictor completes the first mission and pays its reward
        int mission = Missions.RegisterConstrictorKill(destroyed.Type);
        if (mission > 0)
        {
            Commander.Cash += mission;
            Message = "The Constrictor is destroyed. Mission complete: 5,000 credits.";
        }

        Commander.Cash += bounty;
        Commander.RegisterKill();

        // Shooting up innocent traders makes us wanted
        if (destroyed.AiFlag < 0x80)
        {
            Commander.LegalStatus = Math.Min(255, Commander.LegalStatus + 4);
        }

        // Keep the commander's status byte in step with the missions
        Commander.MissionStatus = Missions.StatusByte;
        return bounty;
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
            for (int item = 0; item < 17; item++)
            {
                Commander.RemoveCargo(item, Commander.GetCargo(item));
            }

            Commander.EscapePod = false;
            Console.WriteLine("Escape pod launched: cargo lost, commander recovered at the station.");
            Dock();
            return;
        }

        GameOver = true;
        Message = "GAME OVER - press ESC to leave, or N for a new commander";
    }

    /// <summary>Starts again with a fresh commander, as reloading a save does.</summary>
    public void Restart()
    {
        GameOver = false;
        Commander.Cash = 1000;
        Commander.Fuel = 70;
        Commander.Missiles = 3;
        Commander.Kills = 0;
        Commander.LegalStatus = 0;
        Commander.SetLaser(LaserMount.Front, LaserType.Pulse);
        Flight.Player.Energy = 150;
        Flight.Player.ForeShield = 255;
        Flight.Player.AftShield = 255;
        Message = "New commander: 100 credits, full tank, pulse laser, three missiles.";
        Launch();
    }

    /// <summary>Launches from the station.</summary>
    public void Launch()
    {
        Mode = GameMode.Flying;
        Message = string.Empty;
    }

    /// <summary>
    /// Buys an equipment item from the station's shop, applying its effect to the commander.
    /// </summary>
    public string? BuyEquipment(int item, int lightYears = 0)
    {
        string? result = Universe.Outfitting.Buy(Commander, System, item, lightYears);
        Message = result ?? string.Empty;

        // A few items change how our ship behaves in flight
        Flight.Player.HasEnergyUnit = Commander.EnergyUnit;
        return result;
    }

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

        int sold = Commander.Sell(item, entry.Price, Math.Min(amount, held));
        Market[item] = entry with { Availability = Math.Min(63, entry.Availability + sold) };
        Message = $"Sold {sold} {entry.Item.Unit} of {entry.Item.Name.ToLowerInvariant()} " +
                  $"for {MarketFormat(sold * entry.Price)}.";
        return sold;
    }

    /// <summary>Formats an amount in tenths of a credit the way the original does.</summary>
    public static string MarketFormat(int tenths) =>
        $"{tenths / 10}.{Math.Abs(tenths % 10)}";
}
