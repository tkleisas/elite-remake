using EliteRemake.Core.Audio;
using EliteRemake.Core.Maths;
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

/// <summary>
/// Checks the combat rules: what is in the crosshairs, how much a laser hurts, and how firing heats
/// the laser and drains our energy.
/// </summary>
public class CombatTests
{
    private static (FlightSim Sim, Ship Target) CreateSim()
    {
        var sim = new FlightSim(new Ship(11, "cobra-mk-3", "Cobra Mk III"));
        sim.Commander = Commander.CreateDefault();
        var target = Ship.Create(17, "sidewinder", "Sidewinder", 0, 0, 0, 0, 1000);
        target.Energy = 70;
        sim.Spawn(target);
        sim.TargetableAreaProvider = _ => 55 * 55;
        return (sim, target);
    }

    [Fact]
    public void LaserPowerMatchesTheOriginalsValues()
    {
        // The original's constants: POW = 15, a beam laser is POW with bit 7 set, and a military
        // laser is Armlas = 128 + 23
        Assert.Equal(15, Combat.LaserRawByte(LaserType.Pulse));
        Assert.Equal(143, Combat.LaserRawByte(LaserType.Beam));
        Assert.Equal(151, Combat.LaserRawByte(LaserType.Military));
        Assert.Equal(15, Combat.Power(LaserType.Pulse));
        Assert.Equal(15, Combat.Power(LaserType.Beam));
        Assert.Equal(23, Combat.Power(LaserType.Military));

        // A pulse laser has to wait ten frames between shots; beam lasers fire continuously
        Assert.Equal(10, Combat.FireInterval(LaserType.Pulse));
        Assert.Equal(0, Combat.FireInterval(LaserType.Beam));
        Assert.Equal(0, Combat.FireInterval(LaserType.Military));
    }

    [Fact]
    public void AShipInTheCrosshairsIsHit()
    {
        var (sim, target) = CreateSim();
        target.SetPosition(10, 10, 1000);

        Assert.True(Combat.IsInCrosshairs(target, 55 * 55));

        // Off to one side by more than the targetable area, it is not
        target.SetPosition(200, 0, 1000);
        Assert.False(Combat.IsInCrosshairs(target, 55 * 55));

        // Beyond 256 units off the centre line it cannot be hit at all
        target.SetPosition(10, 300, 1000);
        Assert.False(Combat.IsInCrosshairs(target, 55 * 55));

        // And a ship behind us is never hit
        target.SetPosition(0, 0, -1000);
        Assert.False(Combat.IsInCrosshairs(target, 55 * 55));
    }

    [Fact]
    public void FiringDamagesTheTargetAndCostsUsEnergy()
    {
        var (sim, target) = CreateSim();
        sim.Player.Energy = 150;

        sim.Step(new FlightInput(Fire: true));

        Assert.Equal(15, sim.FiringLaserPower);
        Assert.Same(target, sim.LaserTarget);
        Assert.Equal(70 - 15, target.Energy);

        // Firing costs a unit of energy, but the energy banks recharge a unit a frame as well, so
        // the two cancel out — which is exactly what the original does
        Assert.Equal(150, sim.Player.Energy);
        // The shot adds 8 degrees and the frame's cooling takes one back off again
        Assert.Equal(Combat.HeatPerShot - Combat.CoolingPerFrame, sim.LaserTemperature);
        Assert.True(sim.LaserCooldown > 0, "a pulse laser should have to wait between shots");
    }

    [Fact]
    public void EnoughHitsDestroyAShip()
    {
        var (sim, target) = CreateSim();
        target.Energy = 30;

        // A pulse laser does 15 a shot with a ten-frame gap, so two shots finish a Sidewinder
        int shots = 0;
        for (int i = 0; i < 40 && sim.DestroyedThisFrame is null; i++)
        {
            sim.Step(new FlightInput(Fire: true));
            if (sim.FiringLaserPower > 0)
            {
                shots++;
            }
        }

        Assert.Equal(2, shots);
        Assert.Same(target, sim.DestroyedThisFrame);
        Assert.Equal(0, target.Energy);
        Assert.True(target.IsExploding, "a destroyed ship should be exploding");
    }

    [Fact]
    public void LasersOverheatAndThenStopFiring()
    {
        var (sim, target) = CreateSim();
        sim.Commander!.SetLaser(LaserMount.Front, LaserType.Beam);
        target.Energy = 255;

        // A beam laser fires every frame and heats by 8 while cooling by 1, so it must overheat
        for (int i = 0; i < 200; i++)
        {
            sim.Step(new FlightInput(Fire: true));
        }

        Assert.True(sim.LaserTemperature >= Combat.OverheatTemperature, $"temperature {sim.LaserTemperature}");

        // Once overheated it will not fire at all
        sim.Step(new FlightInput(Fire: true));
        Assert.Equal(0, sim.FiringLaserPower);

        // And it cools down again once we stop
        for (int i = 0; i < 300; i++)
        {
            sim.Step();
        }

        Assert.Equal(0, sim.LaserTemperature);
    }
}

/// <summary>
/// Checks the ship AI: which ships want a fight, whether they steer towards us, and whether they
/// shoot when we are in their sights.
/// </summary>
public class TacticsTests
{
    private static (FlightSim Sim, Ship Enemy) CreateSim(byte aiFlag = 0xF8)
    {
        var sim = new FlightSim(new Ship(11, "cobra-mk-3", "Cobra Mk III"));
        sim.Commander = Commander.CreateDefault();
        sim.Player.Energy = 150;
        sim.LaserPowerProvider = ship => ship.Type == 17 ? 10 : 0;

        Ship enemy = Ship.Create(17, "sidewinder", "Sidewinder", 0, 0, 0, 0, 2000);
        enemy.AiFlag = aiFlag;
        enemy.Energy = 70;
        sim.Spawn(enemy);
        return (sim, enemy);
    }

    [Fact]
    public void AggressiveShipsAttackAndPeacefulOnesDoNot()
    {
        var random = new EliteRandom(1234);
        // The aggression test compares a random byte with bit 7 set against the AI flag, so a
        // flag of &F8 makes a ship attack almost every frame, while a flag below &80 means it
        // never does
        var pirate = new Ship(17, "sidewinder", "Pirate") { AiFlag = 0xF8 };
        var trader = new Ship(12, "python", "Trader") { AiFlag = 0x10 };

        int pirateAttacks = 0;
        for (int i = 0; i < 200; i++)
        {
            if (Tactics.WantsToAttack(pirate, random))
            {
                pirateAttacks++;
            }

            Assert.False(Tactics.WantsToAttack(trader, random), "a peaceful ship should never attack");
        }

        Assert.True(pirateAttacks > 150, $"a pirate should attack most frames, got {pirateAttacks}/200");
    }

    [Fact]
    public void AnEnemySteersTowardsUs()
    {
        var (sim, enemy) = CreateSim();

        // Put the enemy ahead of us and to one side, facing away
        enemy.SetPosition(600, 400, 2000);
        enemy.Orientation.SetUnity(Orientation.Nosev, Orientation.Z, 1.0);
        for (int i = 0; i < 120; i++)
        {
            sim.Step();
        }

        // The AI should have turned the ship towards us: its nose should point roughly at the
        // origin from wherever it has got to
        (int x, int y, int z) = enemy.GetPosition();
        var toUs = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(-x, -y, -z));
        System.Numerics.Vector3 nose = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(
            (float)enemy.Orientation.GetUnity(Orientation.Nosev, Orientation.X),
            (float)enemy.Orientation.GetUnity(Orientation.Nosev, Orientation.Y),
            (float)enemy.Orientation.GetUnity(Orientation.Nosev, Orientation.Z)));

        float alignment = System.Numerics.Vector3.Dot(toUs, nose);
        Assert.True(alignment > 0.8, $"the enemy should be pointing at us, alignment {alignment:0.00}");
    }

    [Fact]
    public void AnEnemyInRangeAndOnTargetHitsUs()
    {
        var (sim, enemy) = CreateSim();

        // Place the enemy close and directly ahead, pointing straight at us
        enemy.SetPosition(0, 0, 1500);
        enemy.Orientation.SetUnity(Orientation.Nosev, Orientation.Z, -1.0);

        int shieldsBefore = sim.Player.ForeShield + sim.Player.AftShield;
        bool hit = false;
        for (int i = 0; i < 50 && !hit; i++)
        {
            sim.Step();
            hit = sim.DamageTakenThisFrame > 0;
        }

        Assert.True(hit, "an enemy lined up on us at close range should open fire");
        Assert.True(
            sim.Player.ForeShield + sim.Player.AftShield < shieldsBefore,
            "and the damage should come off our shields first");
    }

    [Fact]
    public void AnEnemyOutOfRangeHoldsItsFire()
    {
        var (sim, enemy) = CreateSim();

        // Same aim, but far beyond the range at which ships open fire
        enemy.SetPosition(0, 0, Tactics.FireRange * 4);
        enemy.Orientation.SetUnity(Orientation.Nosev, Orientation.Z, -1.0);

        for (int i = 0; i < 20; i++)
        {
            sim.Step();
            Assert.Equal(0, sim.DamageTakenThisFrame);
        }
    }

    [Fact]
    public void AShipWithoutALaserCannotHitUs()
    {
        var (sim, enemy) = CreateSim();
        sim.LaserPowerProvider = _ => 0; // a ship with no laser fitted

        enemy.SetPosition(0, 0, 1500);
        enemy.Orientation.SetUnity(Orientation.Nosev, Orientation.Z, -1.0);

        for (int i = 0; i < 20; i++)
        {
            sim.Step();
            Assert.Equal(0, sim.DamageTakenThisFrame);
        }
    }
}

/// <summary>
/// Checks the damage model: shields absorbing hits before the energy banks, the recharge rules, and
/// the point at which a hit is fatal.
/// </summary>
public class DamageTests
{
    private static Ship CreateShip()
    {
        var ship = new Ship(11, "cobra-mk-3", "Cobra Mk III");
        ship.Energy = 150;
        ship.ForeShield = 255;
        ship.AftShield = 255;
        return ship;
    }

    [Fact]
    public void ShieldsAbsorbHitsBeforeTheEnergyBanks()
    {
        Ship ship = CreateShip();

        // A hit from the front comes off the fore shield and nothing else
        Assert.False(Combat.TakeDamage(ship, 20, fromBehind: false));
        Assert.Equal(235, ship.ForeShield);
        Assert.Equal(255, ship.AftShield);
        Assert.Equal(150, ship.Energy);

        // And a hit from behind comes off the aft shield
        Assert.False(Combat.TakeDamage(ship, 20, fromBehind: true));
        Assert.Equal(235, ship.ForeShield);
        Assert.Equal(235, ship.AftShield);
        Assert.Equal(150, ship.Energy);
    }

    [Fact]
    public void DamageBeyondTheShieldReachesTheEnergyBanks()
    {
        Ship ship = CreateShip();
        ship.ForeShield = 10;

        // 10 points finish the shield and the remaining 15 come off the energy
        Assert.False(Combat.TakeDamage(ship, 25, fromBehind: false));
        Assert.Equal(0, ship.ForeShield);
        Assert.Equal(135, ship.Energy);
    }

    [Fact]
    public void AHitThatEmptiesTheEnergyBanksIsFatal()
    {
        Ship ship = CreateShip();
        ship.ForeShield = 0;
        ship.AftShield = 0;
        ship.Energy = 30;

        // Exactly enough to finish us off
        Assert.True(Combat.TakeDamage(ship, 30, fromBehind: false));
        Assert.Equal(0, ship.Energy);

        // And anything beyond that as well
        Ship second = CreateShip();
        second.ForeShield = 5;
        second.Energy = 10;
        Assert.True(Combat.TakeDamage(second, 40, fromBehind: false));
    }

    [Fact]
    public void EnergyRechargesAFrameAndDoublesWithAnEnergyUnit()
    {
        Ship ship = CreateShip();
        ship.Energy = 100;

        Combat.RechargeEnergy(ship);
        Assert.Equal(101, ship.Energy);

        ship.HasEnergyUnit = true;
        Combat.RechargeEnergy(ship);
        Assert.Equal(103, ship.Energy);

        // And it stops at the maximum
        ship.Energy = 255;
        Combat.RechargeEnergy(ship);
        Assert.Equal(255, ship.Energy);
    }

    [Fact]
    public void ShieldsOnlyRechargeFromBanksAboveHalfFull()
    {
        Ship ship = CreateShip();
        ship.ForeShield = 200;
        ship.AftShield = 200;
        ship.Energy = 100; // below half, so no shield charging

        Combat.RechargeShields(ship);
        Assert.Equal(200, ship.ForeShield);
        Assert.Equal(200, ship.AftShield);
        Assert.Equal(100, ship.Energy);

        // Above half full, the banks pay a point for each shield
        ship.Energy = 200;
        Combat.RechargeShields(ship);
        Assert.Equal(201, ship.ForeShield);
        Assert.Equal(201, ship.AftShield);
        Assert.Equal(198, ship.Energy);

        // A full shield costs nothing
        ship.ForeShield = 255;
        ship.AftShield = 255;
        ship.Energy = 200;
        Combat.RechargeShields(ship);
        Assert.Equal(200, ship.Energy);
    }
}

/// <summary>
/// Checks ship spawning: that the system's government decides how busy it is, that the ship types
/// come from the original's tables, and that spawned ships appear ahead of us.
/// </summary>
public class SpawnerTests
{
    private static StarSystem SystemWithGovernment(int government)
    {
        StarSystem lave = Galaxy.GenerateGalaxy(0).First(s => s.Name == "LAVE");
        return lave with { Government = government };
    }

    [Fact]
    public void SpawnShipTypesComeFromTheOriginalsTables()
    {
        var random = new EliteRandom(7);
        var pirateTypes = new HashSet<int>();
        var hunterTypes = new HashSet<int>();

        for (int i = 0; i < 500; i++)
        {
            pirateTypes.Add(Spawner.ShipType(SpawnKind.Pirates, random));
            hunterTypes.Add(Spawner.ShipType(SpawnKind.BountyHunter, random));
        }

        // Pirates fly the eight pack hunters, Sidewinder (17) to Cobra Mk III pirate (24)
        Assert.Equal(Enumerable.Range(17, 8).ToHashSet(), pirateTypes);

        // Bounty hunters fly the four from Cobra Mk III pirate (24) to Fer-de-lance (27). The
        // Moray sits at 28 and so never spawns, exactly as in the original.
        Assert.Equal(Enumerable.Range(24, 4).ToHashSet(), hunterTypes);
        Assert.DoesNotContain(28, hunterTypes);
    }

    [Fact]
    public void AnarchySystemsAreBusierThanSafeOnes()
    {
        var random = new EliteRandom(99);
        int anarchySpawns = 0;
        int corporateSpawns = 0;

        for (int i = 0; i < 5000; i++)
        {
            if (Spawner.ChooseSpawn(SystemWithGovernment(0), random) != SpawnKind.None)
            {
                anarchySpawns++;
            }

            if (Spawner.ChooseSpawn(SystemWithGovernment(7), random) != SpawnKind.None)
            {
                corporateSpawns++;
            }
        }

        Assert.True(anarchySpawns > corporateSpawns,
            $"an anarchy should be busier: {anarchySpawns} vs {corporateSpawns}");

        // Roughly half the rolls continue in an anarchy, as the original's 47% suggests
        Assert.InRange(anarchySpawns, 2000, 2600);

        // Corporate states are much quieter
        Assert.True(corporateSpawns < anarchySpawns / 2);
    }

    [Fact]
    public void PiratesAreMoreCommonThanBountyHunters()
    {
        var random = new EliteRandom(1234);
        int pirates = 0;
        int hunters = 0;

        for (int i = 0; i < 5000; i++)
        {
            switch (Spawner.ChooseSpawn(SystemWithGovernment(0), random))
            {
                case SpawnKind.Pirates:
                    pirates++;
                    break;
                case SpawnKind.BountyHunter:
                    hunters++;
                    break;
            }
        }

        // The original's 61% pirates
        Assert.True(pirates > hunters, $"expected more pirates: {pirates} vs {hunters}");
        Assert.InRange(pirates / (double)(pirates + hunters), 0.5, 0.7);
    }

    [Fact]
    public void SpawnedShipsAppearAheadOfUsAndAreAggressive()
    {
        var random = new EliteRandom(5);
        StarSystem system = SystemWithGovernment(0);

        // A pack is at most four ships, so the lead ship and its three companions
        for (int i = 0; i < 4; i++)
        {
            Ship ship = Spawner.Create(i % 2 == 0 ? SpawnKind.Pirates : SpawnKind.BountyHunter, system, random, i);

            (int x, int y, int z) = ship.GetPosition();
            Assert.True(z > 0, "a spawned ship should be ahead of us");
            Assert.InRange(z, Spawner.SpawnDistance, Spawner.SpawnDistance + (3 * 512));
            Assert.InRange(x, -32768, 32768);
            Assert.InRange(y, -32768, 32768);

            // The AI flag has bit 7 set (AI enabled) and bit 6 (aggressive)
            Assert.True((ship.AiFlag & 0xC0) == 0xC0, $"AI flag {ship.AiFlag:X2} should be aggressive");
        }
    }

    [Fact]
    public void TheSimulationSpawnsShipsAndRespectsTheBubbleLimit()
    {
        var sim = new FlightSim(new Ship(11, "cobra-mk-3", "Cobra Mk III"))
        {
            System = SystemWithGovernment(0),
        };

        for (int i = 0; i < 20000; i++)
        {
            sim.Step();
        }

        Assert.True(sim.Bubble.Count > 0, "an anarchy system should spawn ships");
        Assert.True(sim.Bubble.Count <= FlightSim.MaxShipsInBubble);

        // Everything that spawned is either a piloted ship from the original's tables or a bit of
        // junk
        foreach (Ship ship in sim.Bubble)
        {
            Assert.True(
                Tactics.IsUnderPilotControl(ship.Type) || Debris.IsJunk(ship.Type),
                $"type {ship.Type} should be a ship or junk");
        }
    }

    [Fact]
    public void SpawningCanBeTurnedOff()
    {
        var sim = new FlightSim(new Ship(11, "cobra-mk-3", "Cobra Mk III"))
        {
            System = SystemWithGovernment(0),
            SpawningEnabled = false,
        };

        for (int i = 0; i < 5000; i++)
        {
            sim.Step();
        }

        Assert.Empty(sim.Bubble);
    }
}

/// <summary>
/// Checks the debris: what spawns, what a destroyed rock leaves behind, and scooping it up.
/// </summary>
public class DebrisTests
{
    [Fact]
    public void JunkSpawnsThirteenPercentOfTheTimeAndOnlyThreeAtOnce()
    {
        var random = new EliteRandom(42);
        int spawned = 0;

        for (int i = 0; i < 5000; i++)
        {
            if (Debris.ChooseJunk(random, junkInBubble: 0) != 0)
            {
                spawned++;
            }
        }

        // The original's 13% chance
        Assert.InRange(spawned / 5000.0, 0.10, 0.17);

        // And nothing spawns once three bits of junk are already about
        for (int i = 0; i < 1000; i++)
        {
            Assert.Equal(0, Debris.ChooseJunk(random, junkInBubble: Debris.MaxJunk));
        }
    }

    [Fact]
    public void JunkTypesFollowTheOriginalsSplit()
    {
        var random = new EliteRandom(11);
        var counts = new Dictionary<int, int>();

        for (int i = 0; i < 20000; i++)
        {
            int type = 0;
            while (type == 0)
            {
                type = Debris.ChooseJunk(random, 0);
            }

            counts[type] = counts.GetValueOrDefault(type) + 1;
        }

        // 2% cargo canisters, 50% boulders, 48% asteroids
        int total = counts.Values.Sum();
        Assert.InRange(counts[Debris.Canister] / (double)total, 0.0, 0.05);
        Assert.InRange(counts[Debris.Boulder] / (double)total, 0.42, 0.58);
        Assert.InRange(counts[Debris.Asteroid] / (double)total, 0.40, 0.56);
    }

    [Fact]
    public void MiningLasersBreakRocksIntoSplinters()
    {
        var random = new EliteRandom(3);

        // A mining laser on an asteroid gives one to three splinters
        var counts = new HashSet<int>();
        for (int i = 0; i < 200; i++)
        {
            (int type, int count) = Debris.DestructionDrops(Debris.Asteroid, Combat.MiningLaserPower, random);
            Assert.Equal(Debris.Splinter, type);
            Assert.InRange(count, 1, 3);
            counts.Add(count);
        }

        Assert.True(counts.Count > 1, "asteroids should break into a variable number of splinters");

        // Any other laser simply leaves a cargo canister
        (int otherType, int otherCount) = Debris.DestructionDrops(Debris.Asteroid, Combat.PulseLaserPower, random);
        Assert.Equal(Debris.Canister, otherType);
        Assert.Equal(1, otherCount);

        // A boulder only gives up a splinter half the time
        int boulderSplinters = 0;
        for (int i = 0; i < 400; i++)
        {
            (int type, int count) = Debris.DestructionDrops(Debris.Boulder, Combat.MiningLaserPower, random);
            if (type == Debris.Splinter && count > 0)
            {
                boulderSplinters++;
            }
        }

        Assert.InRange(boulderSplinters / 400.0, 0.4, 0.6);
    }

    [Fact]
    public void ScoopingNeedsFuelScoopsAndHoldSpace()
    {
        Commander commander = Commander.CreateDefault();
        var canister = new Ship(Debris.Canister, "canister", "Cargo canister");

        // Without fuel scoops nothing can be scooped
        Assert.Null(Debris.TryScoop(canister, commander, marketItem: 0));

        commander.FuelScoops = true;
        (int Item, int Amount)? scooped = Debris.TryScoop(canister, commander, marketItem: 0);
        Assert.NotNull(scooped);
        Assert.Equal(0, scooped!.Value.Item); // food, from the canister's blueprint
        Assert.Equal(1, commander.GetCargo(0));
        Assert.True(canister.IsKilled, "a scooped item is removed from the bubble");

        // A full hold has nowhere to put anything
        commander.AddCargo(1, commander.CargoFree);
        var second = new Ship(Debris.Canister, "canister", "Cargo canister");
        Assert.Null(Debris.TryScoop(second, commander, marketItem: 0));
    }

    [Fact]
    public void SplintersScoopAsMinerals()
    {
        Commander commander = Commander.CreateDefault();
        commander.FuelScoops = true;
        var splinter = new Ship(Debris.Splinter, "splinter", "Splinter");

        (int Item, int Amount)? scooped = Debris.TryScoop(splinter, commander, marketItem: 99);
        Assert.NotNull(scooped);
        Assert.Equal(12, scooped!.Value.Item); // minerals
        Assert.Equal(1, commander.GetCargo(12));
    }

    [Fact]
    public void FlyingAtAJunkItemScoopsIt()
    {
        var sim = new FlightSim(new Ship(11, "cobra-mk-3", "Cobra Mk III"))
        {
            SpawningEnabled = false,
        };

        Commander commander = Commander.CreateDefault();
        commander.FuelScoops = true;
        sim.ScoopCommander = commander;
        sim.ScoopItemProvider = _ => 0;

        // A canister just ahead of us, inside the scooping range
        var canister = new Ship(Debris.Canister, "canister", "Cargo canister");
        canister.SetPosition(0, 0, Debris.ScoopRange - 20);
        sim.Spawn(canister);

        sim.Step();

        Assert.NotNull(sim.ScoopedThisFrame);
        Assert.Equal(1, commander.GetCargo(0));
        Assert.True(canister.IsKilled);
    }
}

/// <summary>
/// Checks missiles and the E.C.M.: locking on, firing, homing, the damage a hit does, and the
/// countermeasure.
/// </summary>
public class MissileTests
{
    private static (FlightSim Sim, Ship Enemy) CreateSim()
    {
        var sim = new FlightSim(new Ship(11, "cobra-mk-3", "Cobra Mk III"))
        {
            SpawningEnabled = false,
            Commander = Commander.CreateDefault(),
        };

        sim.Player.Energy = 150;
        sim.Player.ForeShield = 255;
        sim.Player.AftShield = 255;

        Ship enemy = Ship.Create(17, "sidewinder", "Sidewinder", 0, 0, 0, 0, 2000);
        enemy.Energy = 70;
        sim.Spawn(enemy);
        return (sim, enemy);
    }

    [Fact]
    public void AMissileNeedsALockAndAmmunition()
    {
        var (sim, enemy) = CreateSim();

        // No lock, so nothing fires
        Assert.False(sim.FireMissile());

        sim.MissileLock = enemy;
        Assert.True(sim.CanFireMissile);
        Assert.True(sim.FireMissile());

        // Firing spends a missile and clears the lock
        Assert.Equal(2, sim.Commander!.Missiles);
        Assert.Null(sim.MissileLock);
        Assert.Contains(sim.Bubble, s => s.Type == Missiles.MissileType);

        // And an empty rack cannot fire
        sim.Commander.Missiles = 0;
        sim.MissileLock = enemy;
        Assert.False(sim.FireMissile());
    }

    [Fact]
    public void FiringAMissileMakesTheTargetHostile()
    {
        var (sim, enemy) = CreateSim();
        enemy.AiFlag = 0x10; // peaceful
        sim.MissileLock = enemy;

        sim.FireMissile();

        Assert.Equal(0xFF, enemy.AiFlag);
    }

    [Fact]
    public void AMissileHomesInAndDestroysItsTarget()
    {
        var (sim, enemy) = CreateSim();
        enemy.SetPosition(200, 200, 3000);
        enemy.Orientation.SetUnity(Orientation.Nosev, Orientation.Z, 1.0);

        sim.MissileLock = enemy;
        Assert.True(sim.FireMissile());

        // Missiles travel fast, so a few seconds should be plenty
        bool destroyed = false;
        for (int i = 0; i < 400 && !destroyed; i++)
        {
            sim.Step();
            destroyed = enemy.IsExploding || enemy.IsKilled || enemy.Energy == 0;
        }

        Assert.True(destroyed, $"the missile should have caught the Sidewinder; it is at {enemy.GetPosition()}");
    }

    [Fact]
    public void AMissileThatCatchesUsDoesTwoHundredAndFiftyDamage()
    {
        var sim = new FlightSim(new Ship(11, "cobra-mk-3", "Cobra Mk III"))
        {
            SpawningEnabled = false,
            Commander = Commander.CreateDefault(),
        };

        sim.Player.Energy = 255;
        sim.Player.ForeShield = 255;
        sim.Player.AftShield = 255;

        // An enemy missile bearing down on us: it starts close and pointing our way, as a missile
        // that has just been launched at us would be
        Ship missile = Missiles.CreateMissile(target: null);
        missile.SetPosition(0, 0, Missiles.ImpactRange - 10);
        missile.Orientation.SetUnity(Orientation.Nosev, Orientation.Z, -1.0);
        sim.Spawn(missile);

        bool hit = false;
        for (int i = 0; i < 200 && !hit; i++)
        {
            sim.Step();
            hit = sim.HitByMissile;
        }

        Assert.True(hit, "the missile should have gone off on us");

        // 250 damage: the shield takes 255 -> 5 and nothing reaches the energy
        Assert.Equal(5, sim.Player.ForeShield);
        Assert.Equal(255, sim.Player.Energy);
        Assert.True(missile.IsKilled, "the missile is spent");
    }

    [Fact]
    public void TheEcmDestroysMissilesAndCostsEnergy()
    {
        var sim = new FlightSim(new Ship(11, "cobra-mk-3", "Cobra Mk III"))
        {
            SpawningEnabled = false,
            Commander = Commander.CreateDefault(),
        };

        sim.Player.Energy = 150;
        sim.Commander!.Ecm = false;

        // Without an E.C.M. nothing happens
        Assert.False(sim.FireEcm());

        sim.Commander.Ecm = true;
        Ship missile = Missiles.CreateMissile(target: null);
        missile.SetPosition(0, 0, 5000);
        sim.Spawn(missile);

        Assert.True(sim.FireEcm());
        Assert.Equal(150 - Missiles.EcmEnergyCost, sim.Player.Energy);

        sim.Step();
        Assert.True(missile.IsKilled, "the E.C.M. should have destroyed the missile");
        Assert.True(sim.EcmActive);
    }
}

/// <summary>
/// Checks the kill tally and bounty: what a destroyed ship pays, and what shooting innocents costs.
/// </summary>
public class BountyTests
{
    private static GameSession CreateSession()
    {
        var player = new Ship(11, "cobra-mk-3", "Cobra Mk III");
        var session = new GameSession(Commander.CreateDefault(), new FlightSim(player));

        // A Sidewinder is worth 50 credits, which the original stores as 5
        session.BountyProvider = ship => ship.Type == 17 ? 500 : 0;
        return session;
    }

    [Fact]
    public void DestroyingAShipPaysItsBountyAndCountsTheKill()
    {
        GameSession session = CreateSession();
        var pirate = new Ship(17, "sidewinder", "Sidewinder") { AiFlag = 0xF8 };

        int cash = session.Commander.Cash;
        int bounty = session.RegisterKill(pirate);

        Assert.Equal(500, bounty);
        Assert.Equal(cash + 500, session.Commander.Cash);
        Assert.Equal(1, session.Commander.Kills);

        // A pirate is fair game, so our legal status is untouched
        Assert.Equal(0, session.Commander.LegalStatus);
    }

    [Fact]
    public void ShootingInnocentsMakesUsWanted()
    {
        GameSession session = CreateSession();
        var trader = new Ship(12, "python", "Python") { AiFlag = 0x10 };

        session.RegisterKill(trader);

        Assert.True(session.Commander.LegalStatus > 0, "shooting a trader should make us an offender");
        Assert.Equal("Offender", session.Commander.LegalStatusName);

        // Enough of it and we are a fugitive
        for (int i = 0; i < 10; i++)
        {
            session.RegisterKill(new Ship(12, "python", "Python") { AiFlag = 0x10 });
        }

        Assert.Equal("Fugitive", session.Commander.LegalStatusName);
    }

    [Fact]
    public void KillsMoveTheRating()
    {
        GameSession session = CreateSession();
        Assert.Equal("Harmless", session.Commander.Rating);

        for (int i = 0; i < 6400; i++)
        {
            session.RegisterKill(new Ship(17, "sidewinder", "Sidewinder") { AiFlag = 0xF8 });
        }

        Assert.Equal("Elite", session.Commander.Rating);
        Assert.Equal(6400 * 500, session.Commander.Cash - 1000);
    }
}

/// <summary>
/// Checks the equipment shop: what a system stocks, the prices, and what each purchase does.
/// </summary>
public class OutfittingTests
{
    private static StarSystem SystemWithTechLevel(int techLevel)
    {
        StarSystem lave = Galaxy.GenerateGalaxy(0).First(s => s.Name == "LAVE");
        return lave with { TechLevel = techLevel };
    }

    [Fact]
    public void TheTechLevelDecidesWhatIsStocked()
    {
        // The original adds three to the tech level and caps it at fourteen
        Assert.Equal(3, Outfitting.ItemsStocked(SystemWithTechLevel(0)));
        Assert.Equal(7, Outfitting.ItemsStocked(SystemWithTechLevel(4)));
        Assert.Equal(14, Outfitting.ItemsStocked(SystemWithTechLevel(11)));
        Assert.Equal(14, Outfitting.ItemsStocked(SystemWithTechLevel(15)));

        // So a poor system sells fuel, missiles and a cargo bay, and not much else
        StarSystem poor = SystemWithTechLevel(0);
        Assert.True(Outfitting.IsStocked(poor, 0));
        Assert.True(Outfitting.IsStocked(poor, 2));
        Assert.False(Outfitting.IsStocked(poor, 6));  // fuel scoops
        Assert.False(Outfitting.IsStocked(poor, 12)); // military lasers

        // Lave's tech level of 4 stocks the first seven
        Assert.True(Outfitting.IsStocked(SystemWithTechLevel(4), 6));
        Assert.False(Outfitting.IsStocked(SystemWithTechLevel(4), 7));
    }

    [Fact]
    public void PricesAreTheOriginals()
    {
        Assert.Equal(300, Outfitting.Items[1].Price);   // missile, 30.0 Cr
        Assert.Equal(4000, Outfitting.Items[2].Price);  // large cargo bay, 400.0 Cr
        Assert.Equal(6000, Outfitting.Items[3].Price);  // E.C.M., 600.0 Cr
        Assert.Equal(5250, Outfitting.Items[6].Price);  // fuel scoops, 525.0 Cr
        Assert.Equal(1000, Outfitting.Items[7].Price);  // escape pod, 100.0 Cr
        Assert.Equal(7000, Outfitting.Items[9].Price);  // energy unit, 700.0 Cr
        Assert.Equal(30000, Outfitting.Items[11].Price); // galactic hyperdrive, 3000.0 Cr
        Assert.Equal(19000, Outfitting.Items[12].Price); // military lasers, 1900.0 Cr
    }

    [Fact]
    public void FuelIsPricedByTheLightYear()
    {
        Commander commander = Commander.CreateDefault();
        StarSystem system = SystemWithTechLevel(4);
        commander.Fuel = 60; // 10 light years of space
        int cash = commander.Cash;

        // Buying 20 light years only fills the tank, and only costs for 10
        string? result = Outfitting.Buy(commander, system, 0, lightYears: 20);

        Assert.NotNull(result);
        Assert.Equal(70, commander.Fuel);
        Assert.Equal(cash - (10 * Outfitting.FuelPricePerLightYear), commander.Cash);
        Assert.Contains("10 light years", result);

        // And a full tank cannot be filled again
        Assert.Contains("full", Outfitting.Buy(commander, system, 0, lightYears: 5)!);
    }

    [Fact]
    public void BuyingEquipmentSpendsCashAndFitsIt()
    {
        Commander commander = Commander.CreateDefault();
        StarSystem system = SystemWithTechLevel(15);
        commander.Cash = 100000;

        Assert.NotNull(Outfitting.Buy(commander, system, 3)); // E.C.M.
        Assert.True(commander.Ecm);
        Assert.Equal(100000 - 6000, commander.Cash);

        Assert.NotNull(Outfitting.Buy(commander, system, 6)); // fuel scoops
        Assert.True(commander.FuelScoops);

        Assert.NotNull(Outfitting.Buy(commander, system, 9)); // energy unit
        Assert.True(commander.EnergyUnit);
        Assert.Contains("already", Outfitting.Buy(commander, system, 9)!);

        Assert.NotNull(Outfitting.Buy(commander, system, 2)); // large cargo bay
        Assert.Equal(35, commander.CargoCapacity);
    }

    [Fact]
    public void CannotBuyWithoutTheCredits()
    {
        Commander commander = Commander.CreateDefault(); // 100 credits
        StarSystem system = SystemWithTechLevel(15);

        string? result = Outfitting.Buy(commander, system, 11); // galactic hyperdrive, 3000 Cr
        Assert.Contains("Not enough", result!);
        Assert.False(commander.GalacticHyperdrive);
        Assert.Equal(1000, commander.Cash);
    }

    [Fact]
    public void MissilesFillTheRackAndLasersFillTheMounts()
    {
        Commander commander = Commander.CreateDefault();
        StarSystem system = SystemWithTechLevel(15);
        commander.Cash = 1000000;

        Assert.Equal(3, commander.Missiles);
        Outfitting.Buy(commander, system, 1);
        Assert.Equal(4, commander.Missiles);
        Assert.Contains("full", Outfitting.Buy(commander, system, 1)!);

        // The extra laser items upgrade the front mount or fill an empty one
        Outfitting.Buy(commander, system, 5); // beam lasers
        Assert.Equal(LaserType.Beam, commander.GetLaser(LaserMount.Front));

        Outfitting.Buy(commander, system, 12); // military lasers
        Assert.Equal(LaserType.Military, commander.GetLaser(LaserMount.Front));
    }

    [Fact]
    public void AbuyChangesTheShipAsWellAsTheCommander()
    {
        // Dock somewhere that actually stocks an energy unit
        StarSystem rich = SystemWithTechLevel(15);
        Commander commander = Commander.CreateDefault();
        commander.CurrentSystem = rich;
        commander.Cash = 100000;

        var player = new Ship(11, "cobra-mk-3", "Cobra Mk III");
        var session = new GameSession(commander, new FlightSim(player));
        session.Dock();

        Assert.False(session.Flight.Player.HasEnergyUnit);
        session.BuyEquipment(9);
        Assert.True(session.Flight.Player.HasEnergyUnit);
    }
}

/// <summary>
/// Checks hyperspace: the distance formula, the fuel it needs, and arriving in the new system.
/// </summary>
public class HyperspaceTests
{
    private static GameSession CreateSession()
    {
        var player = new Ship(11, "cobra-mk-3", "Cobra Mk III");
        return new GameSession(Commander.CreateDefault(), new FlightSim(player));
    }

    [Fact]
    public void DistancesUseTheOriginalsSquareRoot()
    {
        StarSystem[] systems = Galaxy.GenerateGalaxy(0);
        StarSystem lave = systems.First(s => s.Name == "LAVE");

        // The distance to itself is nothing
        Assert.Equal(0, Galaxy.DistanceTenths(lave, lave));

        // A full tank is 7.0 light years, and the original's fuel is in tenths, so a jump needs at
        // most 70 units of distance
        int nearest = systems.Where(s => s.Seeds != lave.Seeds)
            .Min(s => Galaxy.DistanceTenths(lave, s));
        Assert.True(nearest > 0, "there should be a nearest neighbour");

        // Distances grow with the coordinate distance
        StarSystem far = systems.OrderByDescending(s => Galaxy.CoordinateDistance(lave, s)).First();
        Assert.True(Galaxy.DistanceTenths(lave, far) > nearest);
    }

    [Fact]
    public void AHyperspaceJumpCostsFuelAndMovesUs()
    {
        GameSession session = CreateSession();
        StarSystem[] systems = Galaxy.GenerateGalaxy(0);

        // Find somewhere within range of a full tank
        StarSystem target = systems
            .Where(s => s.Seeds != session.System.Seeds)
            .Where(s => Galaxy.DistanceTenths(session.System, s) <= session.Commander.Fuel)
            .OrderBy(s => Galaxy.DistanceTenths(session.System, s))
            .First();

        session.SelectedSystem = target;
        int distance = session.SelectedDistance;
        int fuel = session.Commander.Fuel;

        Assert.True(session.StartHyperspace());
        Assert.True(session.HyperspaceCountdown > 0);

        // Tick the countdown out
        for (int i = 0; i < 100 && session.HyperspaceCountdown > 0; i++)
        {
            session.TickHyperspace();
        }

        Assert.Equal(target.Name, session.System.Name);
        Assert.Equal(fuel - distance, session.Commander.Fuel);
        Assert.Equal(session.System.Name, session.Commander.CurrentSystem.Name);
    }

    [Fact]
    public void AJumpBeyondTheFuelIsRefused()
    {
        GameSession session = CreateSession();
        StarSystem[] systems = Galaxy.GenerateGalaxy(0);

        // Somewhere far beyond a full tank
        StarSystem far = systems
            .Where(s => Galaxy.DistanceTenths(session.System, s) > session.Commander.Fuel)
            .OrderByDescending(s => Galaxy.DistanceTenths(session.System, s))
            .First();

        session.SelectedSystem = far;
        Assert.False(session.StartHyperspace());
        Assert.Contains("Not enough fuel", session.Message);

        // And jumping to where we already are is pointless
        session.SelectedSystem = session.System;
        Assert.False(session.StartHyperspace());
        Assert.Contains("already here", session.Message);
    }

    [Fact]
    public void ArrivingGivesUsAFreshMarket()
    {
        GameSession session = CreateSession();
        MarketEntry[] before = session.Market;

        StarSystem target = Galaxy.GenerateGalaxy(0)
            .Where(s => s.Seeds != session.System.Seeds)
            .Where(s => Galaxy.DistanceTenths(session.System, s) <= session.Commander.Fuel)
            .OrderBy(s => Galaxy.DistanceTenths(session.System, s))
            .First();

        session.SelectedSystem = target;
        session.StartHyperspace();
        for (int i = 0; i < 100 && session.HyperspaceCountdown > 0; i++)
        {
            session.TickHyperspace();
        }

        Assert.NotEqual(before, session.Market);
        Assert.Equal(17, session.Market.Length);
    }
}

/// <summary>
/// Checks the sound effects: that the table is the original's, and that each sound renders to
/// samples with the shape its envelope implies.
/// </summary>
public class AudioTests
{
    [Fact]
    public void TheSoundTableIsTheOriginals()
    {
        // The four bytes of each sound come straight from the original's SFX table
        Beeps.SoundData laser = Beeps.Table[SoundEffect.LaserFire];
        Assert.Equal(0x12, laser.ChannelAndFlush); // channel 2, flush on
        Assert.Equal(2, laser.Channel);
        Assert.True(laser.Flush);
        Assert.Equal(0x10, laser.Duration);        // sixteenth twentieths of a second
        Assert.Equal(0.8f, laser.Seconds, 3);

        Beeps.SoundData hyperspace = Beeps.Table[SoundEffect.Hyperspace];
        Assert.Equal(2, hyperspace.Envelope);       // the sweep
        Assert.Equal(0x60, hyperspace.Pitch);

        Beeps.SoundData explosion = Beeps.Table[SoundEffect.Explosion];
        Assert.Equal(3, explosion.Envelope);        // noise

        Beeps.SoundData ecm = Beeps.Table[SoundEffect.EcmOn];
        Assert.Equal(4, ecm.Envelope);              // tremolo
        Assert.Equal(0xFF, ecm.Duration);

        // Every sound the game can make has an entry
        foreach (SoundEffect effect in Enum.GetValues<SoundEffect>())
        {
            Assert.True(Beeps.Table.ContainsKey(effect), $"{effect} has no sound data");
        }
    }

    [Fact]
    public void PitchFollowsTheBbcDivider()
    {
        // The sound chip divides 125000 by the pitch, so a high pitch number is a low note
        Assert.Equal(125000.0 / 12, Beeps.Frequency(12));
        Assert.Equal(125000.0 / 100, Beeps.Frequency(100));
        Assert.True(Beeps.Frequency(12) > Beeps.Frequency(24));

        // A pitch of zero means the chip's largest divider, the lowest note it can make
        Assert.True(Beeps.Frequency(0) < 200);
    }

    [Fact]
    public void EverySoundRendersToSamplesOfTheRightLength()
    {
        foreach ((SoundEffect effect, Beeps.SoundData data) in Beeps.Table)
        {
            float[] samples = Beeps.Render(effect);

            // A sound with no duration, such as the E.C.M. switching off, is silent and has no
            // samples at all
            Assert.Equal((int)(data.Seconds * Beeps.SampleRate), samples.Length);
            if (samples.Length == 0)
            {
                continue;
            }

            // The beeper is a square wave, so samples are at full amplitude or none
            foreach (float sample in samples)
            {
                Assert.InRange(sample, -1f, 1f);
            }
        }
    }

    [Fact]
    public void EnvelopesShapeTheSounds()
    {
        // The E.C.M. off sound has no amplitude at all, so it is silent
        float[] silent = Beeps.Render(SoundEffect.EcmOff);
        Assert.All(silent, sample => Assert.Equal(0f, sample));

        // The explosion is noise that dies away, so it starts loud and ends quiet
        float[] explosion = Beeps.Render(SoundEffect.Explosion);
        Assert.True(Math.Abs(explosion[0]) > Math.Abs(explosion[^1]));

        // The laser is a short, loud zap
        float[] laser = Beeps.Render(SoundEffect.LaserFire);
        Assert.True(laser.Max(Math.Abs) > 0.5f);

        // The hyperspace sweep rises in frequency, so it crosses zero more often as it goes
        float[] sweep = Beeps.Render(SoundEffect.Hyperspace);
        int firstHalf = CountZeroCrossings(sweep, 0, sweep.Length / 2);
        int secondHalf = CountZeroCrossings(sweep, sweep.Length / 2, sweep.Length);
        Assert.True(secondHalf > firstHalf, $"the sweep should rise: {firstHalf} then {secondHalf}");
    }

    private static int CountZeroCrossings(float[] samples, int from, int to)
    {
        int crossings = 0;
        for (int i = from + 1; i < to; i++)
        {
            if ((samples[i - 1] < 0 && samples[i] >= 0) || (samples[i - 1] >= 0 && samples[i] < 0))
            {
                crossings++;
            }
        }

        return crossings;
    }
}

/// <summary>
/// Checks the galactic hyperdrive: it needs the drive fitted, it is consumed, and it moves us to
/// the next galaxy with the same system number.
/// </summary>
public class GalacticHyperdriveTests
{
    private static GameSession CreateSession()
    {
        var player = new Ship(11, "cobra-mk-3", "Cobra Mk III");
        return new GameSession(Commander.CreateDefault(), new FlightSim(player));
    }

    [Fact]
    public void TheDriveIsConsumedAndMovesUsToTheNextGalaxy()
    {
        GameSession session = CreateSession();
        string systemName = session.System.Name;
        int index = session.System.Index;

        // Without the drive nothing happens
        Assert.False(session.UseGalacticHyperdrive());
        Assert.Contains("No galactic hyperdrive", session.Message);
        Assert.Equal(0, session.Commander.GalaxyNumber);

        session.Commander.GalacticHyperdrive = true;
        Assert.True(session.UseGalacticHyperdrive());

        Assert.Equal(1, session.Commander.GalaxyNumber);
        Assert.False(session.Commander.GalacticHyperdrive);

        // We arrive at the same system number in the new galaxy, with a different name
        Assert.Equal(index, session.System.Index);
        Assert.Equal(session.System.Name, session.Commander.CurrentSystem.Name);
        Assert.Equal(session.System.Name, session.SelectedSystem.Name);
        Assert.NotEqual(systemName, session.System.Name);
    }

    [Fact]
    public void EightJumpsComeBackToTheFirstGalaxy()
    {
        GameSession session = CreateSession();
        string first = session.System.Name;

        for (int i = 0; i < Galaxy.GalaxyCount; i++)
        {
            session.Commander.GalacticHyperdrive = true;
            Assert.True(session.UseGalacticHyperdrive());
        }

        Assert.Equal(0, session.Commander.GalaxyNumber);
        Assert.Equal(first, session.System.Name);
    }
}

/// <summary>
/// Checks saving and loading the commander: the round trip, what is carried across, and what
/// happens when a save file is damaged.
/// </summary>
public class SaveTests
{
    private static Commander TradingCommander()
    {
        Commander commander = Commander.CreateDefault();
        commander.Name = "TESTER";
        commander.Cash = 12345;
        commander.Fuel = 42;
        commander.Kills = 100;
        commander.LegalStatus = 20;
        commander.Missiles = 2;
        commander.Ecm = true;
        commander.FuelScoops = true;
        commander.EscapePod = true;
        commander.EnergyUnit = true;
        commander.SetLaser(LaserMount.Front, LaserType.Beam);
        commander.SetLaser(LaserMount.Rear, LaserType.Pulse);
        commander.AddCargo(3, 5);
        commander.AddCargo(12, 7);
        // A system in a different galaxy, whose name is nothing like Lave's
        commander.CurrentSystem = Galaxy.GenerateGalaxy(2)[7];
        commander.GalaxyNumber = 2;
        return commander;
    }

    [Fact]
    public void ACommanderSurvivesTheRoundTrip()
    {
        Commander original = TradingCommander();
        CommanderSave save = CommanderSave.FromCommander(original);

        Commander restored = CommanderSave.FromJson(save.ToJson()).ToCommander();

        Assert.Equal(original.Name, restored.Name);
        Assert.Equal(original.Cash, restored.Cash);
        Assert.Equal(original.Fuel, restored.Fuel);
        Assert.Equal(original.GalaxyNumber, restored.GalaxyNumber);
        Assert.Equal(original.LegalStatus, restored.LegalStatus);
        Assert.Equal(original.Kills, restored.Kills);
        Assert.Equal(original.Missiles, restored.Missiles);
        Assert.Equal(original.CargoCapacity, restored.CargoCapacity);
        Assert.Equal(original.Ecm, restored.Ecm);
        Assert.Equal(original.FuelScoops, restored.FuelScoops);
        Assert.Equal(original.EscapePod, restored.EscapePod);
        Assert.Equal(original.EnergyUnit, restored.EnergyUnit);
        Assert.Equal(original.GalacticHyperdrive, restored.GalacticHyperdrive);

        // Lasers and cargo come back mount by mount, item by item
        foreach (LaserMount mount in Enum.GetValues<LaserMount>())
        {
            Assert.Equal(original.GetLaser(mount), restored.GetLaser(mount));
        }

        for (int item = 0; item < 17; item++)
        {
            Assert.Equal(original.GetCargo(item), restored.GetCargo(item));
        }

        // And so does where we are, seeds and all
        Assert.Equal(original.CurrentSystem.Name, restored.CurrentSystem.Name);
        Assert.Equal(original.CurrentSystem.X, restored.CurrentSystem.X);
        Assert.Equal(original.CurrentSystem.Y, restored.CurrentSystem.Y);
        Assert.Equal(original.CurrentSystem.Seeds, restored.CurrentSystem.Seeds);
        Assert.Equal(original.CurrentSystem.Economy, restored.CurrentSystem.Economy);
    }

    [Fact]
    public void ASavedGameCanBeWrittenAndReadBackFromDisk()
    {
        string path = Path.Combine(Path.GetTempPath(), $"elite-test-{Guid.NewGuid():N}.json");
        try
        {
            var player = new Ship(11, "cobra-mk-3", "Cobra Mk III");
            var session = new GameSession(TradingCommander(), new FlightSim(player));
            session.Dock();

            Assert.Contains("saved", session.Save(path));

            var second = new GameSession(Commander.CreateDefault(), new FlightSim(player));
            Assert.Null(second.TryLoad(path));

            Assert.Equal("TESTER", second.Commander.Name);
            Assert.Equal(12345, second.Commander.Cash);
            Assert.Equal(5, second.Commander.GetCargo(3));
            Assert.Equal(LaserType.Beam, second.Commander.GetLaser(LaserMount.Front));

            // The session's system follows the loaded commander
            Assert.Equal(second.Commander.CurrentSystem.Name, second.System.Name);
            Assert.Equal(second.System.Name, second.SelectedSystem.Name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SavingNeedsUsToBeDocked()
    {
        string path = Path.Combine(Path.GetTempPath(), $"elite-test-{Guid.NewGuid():N}.json");
        var session = new GameSession(Commander.CreateDefault(), new FlightSim(new Ship(11, "cobra-mk-3", "Cobra")));

        Assert.Contains("docked", session.Save(path));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void ADamagedSaveIsRejectedRatherThanLoaded()
    {
        // Not JSON at all
        Assert.Throws<InvalidDataException>(() => CommanderSave.FromJson("this is not a save file"));

        // A save from a different version
        Assert.Throws<InvalidDataException>(() => CommanderSave.FromJson("{\"Version\":99}"));

        // A truncated hold
        Assert.Throws<InvalidDataException>(() =>
            CommanderSave.FromJson("{\"Version\":1,\"Cargo\":[1,2,3],\"Lasers\":[1,0,0,0]}"));

        // A missing file is reported, not thrown
        var session = new GameSession(Commander.CreateDefault(), new FlightSim(new Ship(11, "cobra-mk-3", "Cobra")));
        string missing = Path.Combine(Path.GetTempPath(), $"elite-missing-{Guid.NewGuid():N}.json");
        Assert.Contains("No save file", session.TryLoad(missing)!);
    }
}
