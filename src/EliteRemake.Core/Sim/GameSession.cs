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
    }

    /// <summary>The commander.</summary>
    public Commander Commander { get; }

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
        Commander.CurrentSystem = SelectedSystem;
        System = SelectedSystem;
        Market = Universe.Market.Build(System, _random.Next());
        Message = $"Arrived in the {System.Name} system.";
        return true;
    }

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

    /// <summary>Docks at the station, refreshing the market as the original does.</summary>
    public void Dock()
    {
        Mode = GameMode.Docked;
        Market = Universe.Market.Build(System, _random.Next());
        Message = $"Docked at {System.Name} station.";
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
        Commander.Cash += bounty;
        Commander.RegisterKill();

        // Shooting up innocent traders makes us wanted
        if (destroyed.AiFlag < 0x80)
        {
            Commander.LegalStatus = Math.Min(255, Commander.LegalStatus + 4);
        }

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
