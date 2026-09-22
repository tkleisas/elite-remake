using EliteRemake.Core.Sim;

namespace EliteRemake.Core.Universe;

/// <summary>One of the station's equipment items.</summary>
/// <param name="Number">The item's number in the original's table, 0 to 13.</param>
/// <param name="Name">The name the original's token table gives it.</param>
/// <param name="Price">The price in tenths of a credit.</param>
public readonly record struct EquipmentItem(int Number, string Name, int Price);

/// <summary>
/// The station's equipment shop.
/// </summary>
/// <remarks>
/// The original's EQSHP offers up to fourteen items, and which ones a system stocks depends on its
/// tech level: the original adds three to the tech level and caps the result at fourteen, so a
/// tech level 4 system sells the first seven items and a tech level 11 system sells everything.
/// Fuel comes first and is priced per light year rather than as a fixed item, at 2 credits a light
/// year, which the original stores as 20 tenths.
/// </remarks>
public static class Outfitting
{
    /// <summary>The most items any system stocks.</summary>
    public const int MaxItems = 14;

    /// <summary>The price of fuel per light year, in tenths of a credit.</summary>
    public const int FuelPricePerLightYear = 20;

    /// <summary>The largest fuel tank, in light years.</summary>
    public const int MaxFuel = 70;

    /// <summary>The equipment table, in the original's order.</summary>
    public static readonly EquipmentItem[] Items =
    [
        new(0, "Fuel", 0),                  // priced per light year
        new(1, "Missile", 300),
        new(2, "Large Cargo Bay", 4000),
        new(3, "E.C.M. System", 6000),
        new(4, "Extra Pulse Lasers", 4000),
        new(5, "Extra Beam Lasers", 10000),
        new(6, "Fuel Scoops", 5250),
        new(7, "Escape Pod", 1000),
        new(8, "Energy Bomb", 900),
        new(9, "Energy Unit", 7000),
        new(10, "Docking Computer", 7000),
        new(11, "Galactic Hyperdrive", 30000),
        new(12, "Extra Military Lasers", 19000),
        new(13, "Extra Mining Lasers", 2500),
    ];

    /// <summary>
    /// How many items a system stocks: the original adds three to the tech level and caps the
    /// result at fourteen.
    /// </summary>
    public static int ItemsStocked(StarSystem system) => Math.Clamp(system.TechLevel + 3, 3, MaxItems);

    /// <summary>True if the system stocks the given item.</summary>
    public static bool IsStocked(StarSystem system, int item) => item >= 0 && item < ItemsStocked(system);

    /// <summary>The price of an item in tenths of a credit, with fuel priced by the light year.</summary>
    public static int Price(EquipmentItem item, int lightYears = 0) =>
        item.Number == 0 ? lightYears * FuelPricePerLightYear : item.Price;

    /// <summary>
    /// Buys an item, applying its effect to the commander. Returns a message describing what
    /// happened, or null if the purchase could not be made.
    /// </summary>
    /// <param name="commander">The commander buying the item.</param>
    /// <param name="system">The system they are docked in.</param>
    /// <param name="item">The item number.</param>
    /// <param name="lightYears">How much fuel to buy, for the fuel item.</param>
    public static string? Buy(Commander commander, StarSystem system, int item, int lightYears = 0)
    {
        if (!IsStocked(system, item))
        {
            return "This system does not stock that.";
        }

        EquipmentItem entry = Items[item];

        if (item == 0)
        {
            // Fuel is bought by the light year, up to a full tank
            int space = MaxFuel - commander.Fuel;
            int wanted = Math.Clamp(lightYears, 0, space);
            int cost = wanted * FuelPricePerLightYear;

            if (wanted == 0)
            {
                return space == 0 ? "The tank is already full." : "How many light years?";
            }

            if (commander.Cash < cost)
            {
                return "Not enough credits.";
            }

            commander.Cash -= cost;
            commander.Fuel += wanted;
            return $"Bought {wanted} light years of fuel for {Format(cost)}.";
        }

        // Everything else is a one-off purchase, and some things we can only have one of
        string? already = AlreadyFitted(commander, item);
        if (already is not null)
        {
            return already;
        }

        if (commander.Cash < entry.Price)
        {
            return "Not enough credits.";
        }

        commander.Cash -= entry.Price;
        Apply(commander, item);
        return $"Bought {entry.Name} for {Format(entry.Price)}.";
    }

    /// <summary>True if the commander already has this item, where only one makes sense.</summary>
    private static string? AlreadyFitted(Commander commander, int item) => item switch
    {
        1 when commander.Missiles >= 4 => "Missile racks are already full.",
        2 when commander.CargoCapacity >= 35 => "A large cargo bay is already fitted.",
        3 when commander.Ecm => "An E.C.M. system is already fitted.",
        6 when commander.FuelScoops => "Fuel scoops are already fitted.",
        7 when commander.EscapePod => "An escape pod is already fitted.",
        8 when commander.EnergyBomb => "An energy bomb is already fitted.",
        9 when commander.EnergyUnit => "An energy unit is already fitted.",
        10 when commander.DockingComputer => "A docking computer is already fitted.",
        11 when commander.GalacticHyperdrive => "A galactic hyperdrive is already fitted.",
        _ => null,
    };

    /// <summary>Applies a purchased item to the commander.</summary>
    private static void Apply(Commander commander, int item)
    {
        switch (item)
        {
            case 1:
                commander.Missiles = Math.Min(4, commander.Missiles + 1);
                break;
            case 2:
                commander.CargoCapacity = 35; // the original's large bay
                break;
            case 3:
                commander.Ecm = true;
                break;
            case 4:
                FitLaser(commander, LaserType.Pulse);
                break;
            case 5:
                FitLaser(commander, LaserType.Beam);
                break;
            case 6:
                commander.FuelScoops = true;
                break;
            case 7:
                commander.EscapePod = true;
                break;
            case 8:
                commander.EnergyBomb = true;
                break;
            case 9:
                commander.EnergyUnit = true;
                break;
            case 10:
                commander.DockingComputer = true;
                break;
            case 11:
                commander.GalacticHyperdrive = true;
                break;
            case 12:
                FitLaser(commander, LaserType.Military);
                break;
            case 13:
                FitLaser(commander, LaserType.Pulse); // a mining laser fits the front mount
                break;
        }
    }

    /// <summary>
    /// Fits a laser to the first empty mount, which is what the original's "extra laser" items do:
    /// they upgrade the front laser or fill the rear, left and right mounts in turn.
    /// </summary>
    private static void FitLaser(Commander commander, LaserType type)
    {
        foreach (LaserMount mount in new[] { LaserMount.Front, LaserMount.Rear, LaserMount.Left, LaserMount.Right })
        {
            LaserType fitted = commander.GetLaser(mount);
            if (fitted == LaserType.None || (int)fitted < (int)type)
            {
                commander.SetLaser(mount, type);
                return;
            }
        }
    }

    /// <summary>Formats an amount in tenths of a credit.</summary>
    public static string Format(int tenths) => $"{tenths / 10}.{Math.Abs(tenths % 10)}";
}
