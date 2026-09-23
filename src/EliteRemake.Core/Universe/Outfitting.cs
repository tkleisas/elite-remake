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

    /// <summary>
    /// What a large cargo bay makes the hold, which the original's EQSHP sets to 37.
    /// </summary>
    public const int LargeCargoCapacity = 37;

    /// <summary>
    /// The equipment table, in the original's order, with the disc version's own prices.
    /// </summary>
    /// <remarks>
    /// These are the disc's PRXS table. Its entries are tenths of a credit in the comment's own
    /// terms — the missile's 300 is "30.0 Cr" — and fuel is the exception: PRXS holds a 1 there
    /// because its price is worked out in EQSHP as twice the light years, and that doubling is what
    /// turns it into the table's tenths.
    ///
    /// Seven of these used to hold the Elite-A table's prices instead, which differ substantially:
    /// that build halves the escape pod, the docking computer and the galactic hyperdrive, cuts the
    /// energy unit and the energy bomb by more than half, and prices the military lasers at less
    /// than a third.
    /// </remarks>
    public static readonly EquipmentItem[] Items =
    [
        new(0, "Fuel", 0),                  // priced per light year, at 2 Cr each
        new(1, "Missile", 300),
        new(2, "Large Cargo Bay", 4000),
        new(3, "E.C.M. System", 6000),
        new(4, "Extra Pulse Lasers", 4000),
        new(5, "Extra Beam Lasers", 10000),
        new(6, "Fuel Scoops", 5250),
        new(7, "Escape Pod", 10000),
        new(8, "Energy Bomb", 9000),
        new(9, "Energy Unit", 15000),
        new(10, "Docking Computer", 10000),
        new(11, "Galactic Hyperdrive", 50000),
        new(12, "Extra Military Lasers", 60000),
        new(13, "Extra Mining Lasers", 8000),
    ];

    /// <summary>
    /// How many items a system stocks.
    /// </summary>
    /// <remarks>
    /// The original adds three to the tech level and then compares against <c>#12</c>, setting the
    /// count to fourteen if it is at least that — a jump, not a cap:
    ///
    /// <code>
    /// LDA tek / CLC / ADC #3   \ A is now 3 to 17
    /// CMP #12                  \ If A >= 12 then set A = 14
    /// BCC P%+4
    /// LDA #14
    /// </code>
    ///
    /// So a tech level of 9 stocks fourteen items, not twelve, and 10 stocks fourteen rather than
    /// thirteen — the last two tech levels before the cap stock the whole list, which is a sharper
    /// step than a smooth clamp gives. Tech levels 0 to 8 are unaffected, which is nine of the
    /// fifteen levels and the reason a clamp looked right.
    /// </remarks>
    public static int ItemsStocked(StarSystem system)
    {
        int count = system.TechLevel + 3;
        return count >= 12 ? MaxItems : Math.Max(3, count);
    }

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
        2 when commander.CargoCapacity >= LargeCargoCapacity => "A large cargo bay is already fitted.",
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
                // The original's EQSHP sets CRGO to 37 for a large cargo bay: "we just scored
                // ourselves a large cargo bay, so update our current cargo capacity in CRGO to 37".
                // A Cobra starts at 22, so the bay is worth fifteen tonnes, not thirteen.
                commander.CargoCapacity = LargeCargoCapacity;
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
