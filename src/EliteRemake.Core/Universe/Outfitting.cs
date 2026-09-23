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
    /// <param name="mount">
    /// Which view to fit a laser to. The original asks which of the four when a laser is bought —
    /// "Print a menu listing the four space views, with a View ? prompt" — and refunds the laser it
    /// replaces, so this is required for the four laser items and ignored for everything else.
    /// </param>
    public static string? Buy(
        Commander commander,
        StarSystem system,
        int item,
        int lightYears = 0,
        LaserMount? mount = null)
    {
        if (!IsStocked(system, item))
        {
            return "This system does not stock that.";
        }

        if (IsLaserItem(item))
        {
            return BuyLaser(commander, item, mount);
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

    /// <summary>The four equipment items that fit a laser rather than a gadget.</summary>
    public static bool IsLaserItem(int item) => item is 4 or 5 or 12 or 13;

    /// <summary>The laser an equipment item fits: the original's POW, POW+128, Armlas and Mlas.</summary>
    public static LaserType LaserOf(int item) => item switch
    {
        4 => LaserType.Pulse,
        5 => LaserType.Beam,
        12 => LaserType.Military,
        13 => LaserType.Mining,
        _ => LaserType.None,
    };

    /// <summary>The equipment item that sells a laser, which is what a refund is priced from.</summary>
    public static int LaserItemOf(LaserType laser) => laser switch
    {
        LaserType.Pulse => 4,
        LaserType.Beam => 5,
        LaserType.Military => 12,
        LaserType.Mining => 13,
        _ => -1,
    };

    /// <summary>The names the original gives the four views, from its own "FRONT" to "RIGHT".</summary>
    public static string ViewName(LaserMount mount) => mount switch
    {
        LaserMount.Rear => "REAR",
        LaserMount.Left => "LEFT",
        LaserMount.Right => "RIGHT",
        _ => "FRONT",
    };

    /// <summary>The original's names for the lasers, as its status screen prints them.</summary>
    public static string LaserName(LaserType laser) => laser switch
    {
        LaserType.Beam => "BEAM",
        LaserType.Military => "MILITARY",
        LaserType.Mining => "MINING",
        LaserType.Pulse => "PULSE",
        _ => "NONE",
    };

    /// <summary>
    /// Fits a laser to one of the four views, as the original's laser items do.
    /// </summary>
    /// <remarks>
    /// The full price is paid and then the laser being replaced is refunded at its own list price,
    /// which is what the disc version's refund routine does: it looks the old laser's power up in
    /// PRXS and adds that price back to our cash. The first release of disc Elite got this wrong
    /// badly enough to be an infinite-money bug — it refunded a price fetched from outside the table,
    /// because it was called with a laser power where it expected an item number — and the fix is
    /// nine NOPs in the middle of the routine. This is the fixed behaviour.
    /// </remarks>
    private static string? BuyLaser(Commander commander, int item, LaserMount? mount)
    {
        if (mount is not { } view)
        {
            return "Which view?";
        }

        EquipmentItem entry = Items[item];

        if (commander.Cash < entry.Price)
        {
            return "Not enough credits.";
        }

        LaserType wanted = LaserOf(item);
        LaserType replaced = commander.GetLaser(view);

        commander.Cash -= entry.Price;

        int refund = 0;
        if (replaced != LaserType.None)
        {
            refund = Items[LaserItemOf(replaced)].Price;
            commander.Cash += refund;
        }

        commander.SetLaser(view, wanted);

        string fitted = $"Fitted a {LaserName(wanted)} laser to the {ViewName(view)} view.";
        return refund > 0
            ? $"{fitted} {Format(refund)} refunded for the {LaserName(replaced)} laser it replaced."
            : fitted;
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
        9 when commander.EnergyUnitLevel != Commander.NoEnergyUnit => "An energy unit is already fitted.",
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
                commander.EnergyUnitLevel = Commander.StandardEnergyUnit;
                break;
            case 10:
                commander.DockingComputer = true;
                break;
            case 11:
                commander.GalacticHyperdrive = true;
                break;
            // The four laser items (4, 5, 12 and 13) never reach here: Buy sends them to BuyLaser,
            // which needs to know which view to fit them to and refunds the laser they replace.
        }
    }

    /// <summary>Formats an amount in tenths of a credit.</summary>
    public static string Format(int tenths) => $"{tenths / 10}.{Math.Abs(tenths % 10)}";
}
