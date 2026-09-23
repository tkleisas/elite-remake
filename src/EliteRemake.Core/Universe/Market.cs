using EliteRemake.Core.Sim;

namespace EliteRemake.Core.Universe;

/// <summary>One of the seventeen tradeable commodities.</summary>
/// <param name="Index">The item's number, 0 (food) to 16 (alien items).</param>
/// <param name="Name">The item's name, as the original's token table has it.</param>
/// <param name="BasePrice">The base price, in tenths of a credit.</param>
/// <param name="EconomicFactor">
/// How strongly the system's economy affects the price: positive means an industrial system pays
/// more, negative means an agricultural system pays more.
/// </param>
/// <param name="Unit">The unit the item is sold in: tonnes, kilograms or grams.</param>
/// <param name="BaseQuantity">The base quantity available.</param>
/// <param name="Mask">The mask applied to the system's random byte.</param>
public readonly record struct MarketItem(
    int Index,
    string Name,
    int BasePrice,
    int EconomicFactor,
    string Unit,
    int BaseQuantity,
    int Mask);

/// <summary>A single line of a system's market: what is for sale and at what price.</summary>
/// <param name="Item">The commodity.</param>
/// <param name="Price">The price in tenths of a credit, so 40 is 4.0 credits.</param>
/// <param name="Availability">How many units are available.</param>
public readonly record struct MarketEntry(MarketItem Item, int Price, int Availability);

/// <summary>
/// The market: what each system sells, and at what price.
/// </summary>
/// <remarks>
/// The original builds its market in two routines. GVL picks a random byte for the visit and works
/// out how much of each item is available, and TT151 prints the name, price and availability,
/// working out each price from the item's base price, the system's economy and the same random
/// byte. Everything is deterministic given the system and that one random byte, which is why the
/// same visit always shows the same prices, and why a fresh random byte makes the market change.
/// </remarks>
public static class Market
{
    /// <summary>The seventeen commodities, in the original's order.</summary>
    public static readonly MarketItem[] Items =
    [
        new(0, "Food", 19, -2, "t", 6, 0b00000001),
        new(1, "Textiles", 20, -1, "t", 10, 0b00000011),
        new(2, "Radioactives", 65, -3, "t", 2, 0b00000111),
        new(3, "Slaves", 40, -5, "t", 226, 0b00011111),
        new(4, "Liquor/Wines", 83, -5, "t", 251, 0b00001111),
        new(5, "Luxuries", 196, 8, "t", 54, 0b00000011),
        new(6, "Narcotics", 235, 29, "t", 8, 0b01111000),
        new(7, "Computers", 154, 14, "t", 56, 0b00000011),
        new(8, "Machinery", 117, 6, "t", 40, 0b00000111),
        new(9, "Alloys", 78, 1, "t", 17, 0b00011111),
        new(10, "Firearms", 124, 13, "t", 29, 0b00000111),
        new(11, "Furs", 176, -9, "t", 220, 0b00111111),
        new(12, "Minerals", 32, -1, "t", 53, 0b00000011),
        new(13, "Gold", 97, -1, "k", 66, 0b00000111),
        new(14, "Platinum", 171, -2, "k", 55, 0b00011111),
        new(15, "Gem-Stones", 45, -1, "g", 250, 0b00001111),
        new(16, "Alien Items", 53, 15, "t", 192, 0b00000111),
    ];

    /// <summary>The alien items, the last commodity, which can never be bought.</summary>
    public const int AlienItems = 16;

    /// <summary>
    /// Builds a system's market from its economy and the random byte the original picks for the
    /// visit.
    /// </summary>
    /// <param name="system">The system whose market we are in.</param>
    /// <param name="randomByte">
    /// The original's QQ26: one random byte chosen each time we arrive in the system.
    /// </param>
    public static MarketEntry[] Build(StarSystem system, int randomByte)
    {
        var entries = new MarketEntry[Items.Length];
        for (int i = 0; i < Items.Length; i++)
        {
            entries[i] = new MarketEntry(
                Items[i],
                Price(Items[i], system.Economy, randomByte),
                Availability(Items[i], system.Economy, randomByte));
        }

        // The original's var routine zeroes AVL+16 on its way past, so alien items are never
        // available to buy in any system, whatever the economy and the random byte work out to.
        // They are what a Thargoid leaves behind, and they can only be scooped.
        entries[AlienItems] = entries[AlienItems] with { Availability = 0 };

        return entries;
    }

    /// <summary>
    /// The original's GVL, for one item: how many units are available.
    /// </summary>
    public static int Availability(MarketItem item, int economy, int randomByte)
    {
        int value = ((randomByte & 0xFF) & item.Mask) + item.BaseQuantity;
        int economicEffect = EconomicEffect(item, economy);

        value = item.EconomicFactor >= 0
            ? value - economicEffect
            : value + economicEffect;

        if (value < 0)
        {
            value = 0;
        }

        // The original keeps only six bits, so no item is ever available in more than 63 units
        return value & 0x3F;
    }

    /// <summary>
    /// The original's TT151, for one item: the price in tenths of a credit.
    /// </summary>
    public static int Price(MarketItem item, int economy, int randomByte)
    {
        int value = item.BasePrice + ((randomByte & 0xFF) & item.Mask);
        int economicEffect = EconomicEffect(item, economy);

        value = item.EconomicFactor >= 0
            ? value + economicEffect
            : value - economicEffect;

        // The original multiplies by four to give a price in tenths of a credit, which is why
        // prices always end in .0, .2, .4, .6 or .8
        return value * 4;
    }

    /// <summary>
    /// The original's <c>var</c> routine: the economy multiplied by the item's economic factor, by
    /// repeated addition in the original.
    /// </summary>
    public static int EconomicEffect(MarketItem item, int economy) => Math.Abs(item.EconomicFactor) * economy;

    /// <summary>Formats a price in tenths of a credit, as the original prints it.</summary>
    public static string FormatPrice(int tenths) => $"{tenths / 10}.{tenths % 10}";
}
