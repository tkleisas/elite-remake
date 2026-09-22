using EliteRemake.Core.Universe;

namespace EliteRemake.Core.Sim;

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
    }

    /// <summary>The commander.</summary>
    public Commander Commander { get; }

    /// <summary>The flight simulation, which is paused while we are docked.</summary>
    public FlightSim Flight { get; }

    /// <summary>The system we are in.</summary>
    public StarSystem System { get; }

    /// <summary>The station's market for this visit.</summary>
    public MarketEntry[] Market { get; private set; }

    /// <summary>Whether we are flying or docked.</summary>
    public GameMode Mode { get; private set; } = GameMode.Flying;

    /// <summary>An optional message to show the player, such as the result of a trade.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Docks at the station, refreshing the market as the original does.</summary>
    public void Dock()
    {
        Mode = GameMode.Docked;
        Market = Universe.Market.Build(System, _random.Next());
        Message = $"Docked at {System.Name} station.";
    }

    /// <summary>Launches from the station.</summary>
    public void Launch()
    {
        Mode = GameMode.Flying;
        Message = string.Empty;
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
