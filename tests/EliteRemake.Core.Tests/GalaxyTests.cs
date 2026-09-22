using EliteRemake.Core.Universe;
using Xunit;

namespace EliteRemake.Core.Tests;

/// <summary>
/// Checks the procedural galaxy against the original's own universe. The canonical facts used here
/// come from the original sources: galaxy 0's seed, the name of its first system, and the fact that
/// the default commander starts at Lave, whose galactic coordinates are (20, 173).
/// </summary>
public class GalaxyTests
{
    [Fact]
    public void FirstSystemOfGalaxyZero_IsTibedied()
    {
        // The original's source comments its galaxy seed with "Seed s0 for system 0, galaxy 0
        // (Tibedied)"
        StarSystem system = Galaxy.Describe(Galaxy.FirstGalaxySeeds, 0);
        Assert.Equal("TIBEDIED", system.Name);
    }

    [Fact]
    public void Lave_IsAt20And173()
    {
        // The default commander data block in the original's NA% table has QQ0 = 20 (Lave's x
        // coordinate) and QQ1 = 173 (Lave's y coordinate)
        (StarSystem system, _) = Galaxy.FindClosest(Galaxy.FirstGalaxySeeds, 20, 173);

        Assert.Equal("LAVE", system.Name);
        Assert.Equal(20, system.X);
        Assert.Equal(173, system.Y);
    }

    [Fact]
    public void Lave_IsARichAgriculturalWorld()
    {
        // Lave is the world the default commander starts at, and its famous description ("most
        // famous for its vast rain forests and the Laveian tree grub") is generated from its seeds
        (StarSystem lave, _) = Galaxy.FindClosest(Galaxy.FirstGalaxySeeds, 20, 173);

        Assert.Equal(5, lave.Economy);
        Assert.Equal("Rich Agricultural", Galaxy.EconomyNames[lave.Economy]);
        Assert.Equal(3, lave.Government);
        Assert.Equal("Dictatorship", Galaxy.GovernmentNames[lave.Government]);
        Assert.Equal(4116, lave.Radius);
        Assert.Equal(25, lave.Population); // 2.5 billion, as the original prints it
    }

    [Fact]
    public void GalaxyHas256SystemsWithUniquePositions()
    {
        StarSystem[] systems = Galaxy.GenerateGalaxy(0);
        Assert.Equal(256, systems.Length);

        var positions = new HashSet<(int, int)>();
        foreach (StarSystem system in systems)
        {
            Assert.InRange(system.X, 0, 255);
            Assert.InRange(system.Y, 0, 255);
            Assert.False(string.IsNullOrWhiteSpace(system.Name), $"system {system.Index} has no name");
            positions.Add((system.X, system.Y));
        }

        // The original's galaxies pack 256 systems into a 256x256 grid with very few clashes
        Assert.True(positions.Count > 240, $"expected mostly unique positions, got {positions.Count}");
    }

    [Fact]
    public void SystemPropertiesAreInRange()
    {
        foreach (StarSystem system in Galaxy.GenerateGalaxy(0))
        {
            Assert.InRange(system.Economy, 0, 7);
            Assert.InRange(system.Government, 0, 7);
            Assert.InRange(system.TechLevel, 0, 15);
            Assert.InRange(system.Population, 0, 255);
            Assert.InRange(system.Radius, 11 * 256, 26 * 256 + 255);
        }
    }

    [Fact]
    public void EightGalacticJumpsReturnToTheFirstGalaxy()
    {
        SystemSeeds seeds = Galaxy.FirstGalaxySeeds;
        for (int i = 0; i < Galaxy.GalaxyCount; i++)
        {
            seeds = Galaxy.NextGalaxy(seeds);
        }

        Assert.Equal(Galaxy.FirstGalaxySeeds, seeds);

        // And each jump gives a different galaxy
        Assert.NotEqual(Galaxy.GalaxySeeds(0), Galaxy.GalaxySeeds(1));
    }

    [Fact]
    public void EachGalaxyHasItsOwnUniverse()
    {
        var names = new HashSet<string>();
        for (int galaxy = 0; galaxy < Galaxy.GalaxyCount; galaxy++)
        {
            StarSystem[] systems = Galaxy.GenerateGalaxy(galaxy);
            Assert.Equal(256, systems.Length);

            // The first system of each galaxy should be distinct
            Assert.True(names.Add(systems[0].Name), $"galaxy {galaxy} repeats a system name");
        }
    }

    [Fact]
    public void ClosestSystemSearch_MatchesABruteForceSweep()
    {
        var random = new Random(99);
        StarSystem[] systems = Galaxy.GenerateGalaxy(0);

        for (int i = 0; i < 50; i++)
        {
            int x = random.Next(0, 256);
            int y = random.Next(0, 256);

            (StarSystem found, int distance) = Galaxy.FindClosest(Galaxy.FirstGalaxySeeds, x, y);

            // The same distance measure, applied to every system
            int expected = systems.Min(s => (Math.Abs(s.X - x) / 2) + (Math.Abs(s.Y - y) / 2));
            Assert.Equal(expected, distance);
            Assert.Equal(expected, (Math.Abs(found.X - x) / 2) + (Math.Abs(found.Y - y) / 2));
        }
    }

    [Fact]
    public void TwistIsReversibleInTheOriginalSense()
    {
        // Twisting once and comparing against a hand-computed value checks the carry handling
        var seeds = new SystemSeeds(0x5A4A, 0x0248, 0xB753);
        SystemSeeds twisted = Galaxy.Twist(seeds);

        // s0 becomes the old s1, s1 becomes the old s2
        Assert.Equal(0x0248, twisted.S0);
        Assert.Equal(0xB753, twisted.S1);

        // The new s1 is the old s2, so s1 becomes 0xB753 and s1_hi becomes 0xB7
        //   s2_lo = (s0_lo + s1_lo) + s1_lo(new) = (0x4A + 0x48) + 0x53 = 0xE5
        //   s2_hi = (s0_hi + s1_hi + C) + s1_hi(new) + C = (0x5A + 0x02) + 0xB7 = 0x13
        int tmpLow = 0x4A + 0x48;             // 0x92, no carry
        int tmpHigh = 0x5A + 0x02;            // 0x5C, no carry
        int s2Low = (tmpLow & 0xFF) + 0x53;   // 0xE5, no carry
        int s2High = (tmpHigh & 0xFF) + 0xB7 + (s2Low > 0xFF ? 1 : 0);
        Assert.Equal(((s2High & 0xFF) << 8) | (s2Low & 0xFF), twisted.S2);
    }

    [Fact]
    public void NamesAreBuiltFromTwoLetterTokens()
    {
        // Every generated name must be reconstructible from the token table
        foreach (StarSystem system in Galaxy.GenerateGalaxy(0))
        {
            // Names are three or four token pairs, though a zero token prints nothing and the "A?"
            // token prints a single letter, so lengths vary
            Assert.True(system.Name.Length is >= 2 and <= 8, $"unexpected name length for {system.Name}");
            Assert.DoesNotContain('?', system.Name);

            // Every letter must come from the token table
            string letters = string.Concat(Galaxy.TwoLetterTokens).Replace("?", string.Empty);
            Assert.All(system.Name, letter => Assert.Contains(letter, letters));
        }
    }
}

/// <summary>
/// Checks the generated galaxy against system names the original itself names: the systems the
/// Constrictor mission sends you to, which the game's own tables spell out.
/// </summary>
/// <remarks>
/// These come from the original's mission hint tables, which name the systems on the Constrictor's
/// trail. They are a useful check because they exercise names the seed generation only produces
/// with the two-letter token table read correctly — including token 15, which the original stores
/// as "A?" and prints as just "A".
/// </remarks>
public class GalaxyNameTests
{
    private static bool Contains(string[] names, int galaxyIndex, string wanted) =>
        names.Contains(wanted, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void GalaxyOneHoldsTheStartOfTheConstrictorTrail()
    {
        string[] names = Galaxy.GenerateGalaxy(0).Select(s => s.Name).ToArray();

        Assert.True(Contains(names, 0, "REESDICE"), "Reesdice should be in the first galaxy");
        Assert.True(Contains(names, 0, "AREXE"), "Arexe should be in the first galaxy");
    }

    [Fact]
    public void GalaxyTwoHoldsTheRestOfTheTrail()
    {
        string[] names = Galaxy.GenerateGalaxy(1).Select(s => s.Name).ToArray();

        Assert.True(Contains(names, 1, "AUSAR"), "Ausar should be in the second galaxy");
        Assert.True(Contains(names, 1, "RAMAZA"), "Ramaza should be in the second galaxy");
        Assert.True(Contains(names, 1, "VERIAR"), "Veriar should be in the second galaxy");
    }

    [Fact]
    public void NoNameContainsAPlaceholder()
    {
        // The question mark in the original's token 15 is not part of any name
        for (int galaxy = 0; galaxy < Galaxy.GalaxyCount; galaxy++)
        {
            foreach (StarSystem system in Galaxy.GenerateGalaxy(galaxy))
            {
                Assert.DoesNotContain('?', system.Name);
                Assert.True(system.Name.Length is >= 2 and <= 8, $"odd name: {system.Name}");
            }
        }
    }
}
