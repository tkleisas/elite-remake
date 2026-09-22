using EliteRemake.Core.Sim;
using EliteRemake.Core.Universe;
using Xunit;

namespace EliteRemake.Core.Tests;

/// <summary>
/// Checks the market and the commander against the original's own numbers. The price and
/// availability formulas are the original's GVL and TT151 routines, and the random byte they use is
/// the original's QQ26, so a known random byte must give known prices.
/// </summary>
public class MarketTests
{
    private static StarSystem Lave() => Galaxy.GenerateGalaxy(0).First(s => s.Name == "LAVE");

    [Fact]
    public void DefaultCommanderMatchesTheOriginal()
    {
        Commander commander = Commander.CreateDefault();

        Assert.Equal("JAMESON", commander.Name);
        Assert.Equal(1000, commander.Cash);       // 100.0 credits, in tenths as the original stores it
        Assert.Equal(70, commander.Fuel);         // a full tank
        Assert.Equal(3, commander.Missiles);
        Assert.Equal(22, commander.CargoCapacity);
        Assert.Equal(0, commander.GalaxyNumber);
        Assert.Equal(LaserType.Pulse, commander.GetLaser(LaserMount.Front));
        Assert.Equal(LaserType.None, commander.GetLaser(LaserMount.Rear));
        Assert.False(commander.Ecm);
        Assert.False(commander.FuelScoops);
        Assert.False(commander.DockingComputer);
        Assert.Equal("LAVE", commander.CurrentSystem.Name);
        Assert.Equal("Harmless", commander.Rating);
    }

    [Fact]
    public void MarketPricesFollowTheOriginalsFormula()
    {
        // Food has a base price of 19 and an economic factor of -2. Lave is a rich agricultural
        // world with economy 5, so the economic effect is 5 * 2 = 10, and with the low bit of the
        // random byte set the price is (19 + 1 - 10) * 4 = 40, which the original prints as 4.0
        StarSystem lave = Lave();
        MarketItem food = Market.Items[0];

        Assert.Equal(40, Market.Price(food, lave.Economy, randomByte: 1));
        Assert.Equal(36, Market.Price(food, lave.Economy, randomByte: 0));
        Assert.Equal("4.0", Market.FormatPrice(Market.Price(food, lave.Economy, randomByte: 1)));
    }

    [Fact]
    public void PricesAreAlwaysAWholeNumberOfTenths()
    {
        // The original multiplies by four at the end, so prices end in .0, .2, .4, .6 or .8
        foreach (StarSystem system in Galaxy.GenerateGalaxy(0).Take(40))
        {
            for (int randomByte = 0; randomByte < 256; randomByte += 37)
            {
                foreach (MarketItem item in Market.Items)
                {
                    int price = Market.Price(item, system.Economy, randomByte);
                    Assert.True(price > 0, $"{item.Name} in {system.Name} priced at {price}");
                    Assert.Equal(0, price % 2);
                }
            }
        }
    }

    [Fact]
    public void AvailabilityIsCappedAtSixtyThree()
    {
        foreach (StarSystem system in Galaxy.GenerateGalaxy(0).Take(40))
        {
            for (int randomByte = 0; randomByte < 256; randomByte += 53)
            {
                foreach (MarketItem item in Market.Items)
                {
                    int availability = Market.Availability(item, system.Economy, randomByte);
                    Assert.InRange(availability, 0, 63);
                }
            }
        }
    }

    [Fact]
    public void NarcoticsMaskGivesTheTopOfTheRange()
    {
        // Narcotics has a mask of %01111000, so the random byte's middle bits decide both its
        // price and its availability
        MarketItem narcotics = Market.Items[6];
        Assert.Equal(0b01111000, narcotics.Mask);

        int low = Market.Price(narcotics, economy: 0, randomByte: 0);
        int high = Market.Price(narcotics, economy: 0, randomByte: 0b01111000);
        Assert.Equal(narcotics.BasePrice * 4, low);
        Assert.Equal((narcotics.BasePrice + 0b01111000) * 4, high);
    }

    [Fact]
    public void IndustrialSystemsPayMoreForAgriculturalGoods()
    {
        // Food's economic factor is negative, so a rich industrial system (economy 0) pays less
        // than a poor agricultural one (economy 7)
        MarketItem food = Market.Items[0];
        int richIndustrial = Market.Price(food, economy: 0, randomByte: 0);
        int poorAgricultural = Market.Price(food, economy: 7, randomByte: 0);

        Assert.True(poorAgricultural < richIndustrial,
            $"food should be cheaper in an agricultural system: {poorAgricultural} vs {richIndustrial}");

        // Computers have a positive factor, so they cost more in the poor agricultural systems
        // (economy 7) where there is no industry to make them
        MarketItem computers = Market.Items[7];
        Assert.True(Market.Price(computers, economy: 7, randomByte: 0) >
                    Market.Price(computers, economy: 0, randomByte: 0));
    }

    [Fact]
    public void MarketIsDeterministicForAGivenSystemAndRandomByte()
    {
        StarSystem lave = Lave();
        MarketEntry[] first = Market.Build(lave, randomByte: 42);
        MarketEntry[] second = Market.Build(lave, randomByte: 42);

        Assert.Equal(first, second);
        Assert.NotEqual(first, Market.Build(lave, randomByte: 43));
        Assert.Equal(17, first.Length);
        Assert.Equal("Alien Items", first[^1].Item.Name);
    }

    [Fact]
    public void BuyingAndSellingMovesCashAndCargo()
    {
        Commander commander = Commander.CreateDefault();
        StarSystem lave = Lave();
        MarketEntry food = Market.Build(lave, randomByte: 1)[0];

        // Food at 4.0 credits a tonne: 100 credits buys 25 tonnes, but the Cobra only holds 22
        int bought = commander.Buy(food.Item.Index, food.Price, 25);
        Assert.Equal(22, bought);
        Assert.Equal(22, commander.CargoUsed);
        Assert.Equal(0, commander.CargoFree);
        Assert.Equal(1000 - (22 * 40), commander.Cash);

        // And selling it back at the same price returns the cash
        commander.Sell(food.Item.Index, food.Price, 22);
        Assert.Equal(0, commander.CargoUsed);
        Assert.Equal(1000, commander.Cash);
    }

    [Fact]
    public void FuelAndRatingsFollowTheOriginal()
    {
        Commander commander = Commander.CreateDefault();

        Assert.True(commander.UseFuel(7));
        Assert.Equal(63, commander.Fuel);
        Assert.False(commander.UseFuel(70));

        Assert.Equal("Harmless", commander.Rating);
        commander.RegisterKill(8);
        Assert.Equal("Mostly Harmless", commander.Rating);
        commander.RegisterKill(6392);
        Assert.Equal("Elite", commander.Rating);
    }

    [Fact]
    public void TheRandomGeneratorMatchesTheOriginalRecurrence()
    {
        // DORND keeps two interleaved Fibonacci sequences: the feeder in RAND+0 and RAND+2, and
        // the main sequence in RAND+1 and RAND+3, with the feeder's carry feeding the main one
        var random = new EliteRandom(0x12345678);
        byte[] state = random.State.ToArray();

        random.Next(out byte a, out byte x);

        byte f1 = state[0];
        bool carry = (f1 & 0x80) != 0;
        byte shifted = (byte)(f1 << 1);
        int f2 = shifted + state[2] + (carry ? 1 : 0);
        bool feederCarry = f2 > 0xFF;

        byte expectedX = state[1];
        int m2 = state[1] + state[3] + (feederCarry ? 1 : 0);

        Assert.Equal((byte)m2, a);
        Assert.Equal(expectedX, x);
        Assert.Equal((byte)f2, random.State[0]);
        Assert.Equal(shifted, random.State[2]);
        Assert.Equal((byte)m2, random.State[1]);
        Assert.Equal(expectedX, random.State[3]);
    }

    [Fact]
    public void TheRandomGeneratorDoesNotGetStuck()
    {
        var random = new EliteRandom(0);
        var seen = new HashSet<byte>();
        for (int i = 0; i < 10000; i++)
        {
            seen.Add(random.Next());
        }

        // A functioning generator should produce a good spread of values
        Assert.True(seen.Count > 200, $"only {seen.Count} distinct bytes in 10000 draws");
    }
}

/// <summary>
/// Checks the trade loop: docking, buying and selling, and the limits the hold and the bank
/// balance put on it.
/// </summary>
public class GameSessionTests
{
    private static GameSession CreateSession()
    {
        var player = new Ship(11, "cobra-mk-3", "Cobra Mk III");
        return new GameSession(Commander.CreateDefault(), new FlightSim(player));
    }

    [Fact]
    public void StartsInFlightAtLaveWithAFreshMarket()
    {
        GameSession session = CreateSession();

        Assert.Equal(GameMode.Flying, session.Mode);
        Assert.Equal("LAVE", session.System.Name);
        Assert.Equal(17, session.Market.Length);
    }

    [Fact]
    public void DockingChangesTheMarketAndLaunchingKeepsIt()
    {
        GameSession session = CreateSession();
        MarketEntry[] inFlight = session.Market;

        session.Dock();
        Assert.Equal(GameMode.Docked, session.Mode);
        Assert.NotEqual(inFlight, session.Market);

        MarketEntry[] docked = session.Market;
        session.Launch();
        Assert.Equal(GameMode.Flying, session.Mode);

        session.Dock();
        Assert.NotEqual(docked, session.Market);
    }

    [Fact]
    public void BuyingIsLimitedByTheHoldAndTheBankBalance()
    {
        GameSession session = CreateSession();
        session.Dock();

        // A purchase is limited by what the system has for sale
        int food = 0;
        int available = session.Market[food].Availability;
        Assert.True(available > 0, "Lave should have food for sale");
        Assert.Equal(available, session.Buy(food, 100));
        Assert.Equal(0, session.Market[food].Availability);

        // Now fill the rest of the hold, which is the other limit
        int textiles = 1;
        int space = session.Commander.CargoFree;
        int filled = session.Buy(textiles, 100);
        Assert.Equal(space, filled);
        Assert.Equal(0, session.Commander.CargoFree);

        // With a full hold nothing more can be bought
        int cash = session.Commander.Cash;
        Assert.Equal(0, session.Buy(2, 1));
        Assert.Contains("full", session.Message);
        Assert.Equal(cash, session.Commander.Cash);
    }

    [Fact]
    public void SellingReturnsCreditsAndRestocksTheMarket()
    {
        GameSession session = CreateSession();
        session.Dock();

        MarketEntry food = session.Market[0];
        session.Buy(0, 5);
        int cashAfterBuying = session.Commander.Cash;
        int availableAfterBuying = session.Market[0].Availability;

        int sold = session.Sell(0, 5);
        Assert.Equal(5, sold);
        Assert.Equal(0, session.Commander.GetCargo(0));
        Assert.Equal(cashAfterBuying + (5 * food.Price), session.Commander.Cash);
        Assert.Equal(availableAfterBuying + 5, session.Market[0].Availability);
    }

    [Fact]
    public void CannotBuyWhatIsNotForSale()
    {
        GameSession session = CreateSession();
        session.Dock();

        // Find something with no availability and check it cannot be bought
        int index = Array.FindIndex(session.Market, e => e.Availability == 0);
        if (index >= 0)
        {
            int cash = session.Commander.Cash;
            Assert.Equal(0, session.Buy(index, 1));
            Assert.Equal(cash, session.Commander.Cash);
            Assert.Contains("No ", session.Message);
        }
    }

    [Fact]
    public void CannotSellWhatIsNotInTheHold()
    {
        GameSession session = CreateSession();
        session.Dock();

        Assert.Equal(0, session.Sell(0, 1));
        Assert.Contains("No ", session.Message);
    }

    [Fact]
    public void PricesAreInTenthsAndFormatLikeTheOriginals()
    {
        GameSession session = CreateSession();
        session.Dock();

        foreach (MarketEntry entry in session.Market)
        {
            string text = Market.FormatPrice(entry.Price);
            Assert.Contains('.', text);
            string[] parts = text.Split('.');
            Assert.Equal(1, parts[1].Length); // one decimal place, as the original prints
        }
    }
}
