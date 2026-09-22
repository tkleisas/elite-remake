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
            // Names are three or four token pairs, though a zero token prints nothing, so a name
            // can be as short as four letters
            Assert.True(system.Name.Length is >= 4 and <= 8, $"unexpected name length for {system.Name}");
            Assert.Equal(0, system.Name.Length % 2);
            for (int i = 0; i < system.Name.Length; i += 2)
            {
                string pair = system.Name.Substring(i, 2);
                Assert.Contains(pair, Galaxy.TwoLetterTokens);
            }
        }
    }
}
