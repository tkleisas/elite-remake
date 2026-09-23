using EliteRemake.Core.Sim;
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

/// <summary>
/// Pins the anchors the galaxy generation is verified against, so a change that breaks the universe
/// is caught immediately.
/// </summary>
/// <remarks>
/// The first galaxy is verified against systems the original itself names — its own starting system,
/// the classic Lave to Leesti to Riedquat opening, and the systems on the Constrictor's trail — all
/// of which appear at the coordinates the original gives them.
///
/// The second galaxy is only partly verified: it contains canonical systems, but it does not match
/// the original everywhere. The original's documentation says that a galactic hyperspace jump
/// arrives at the system nearest (96, 96), and that this is Ororra in the second galaxy, and that
/// the Constrictor waits at Orarra, which the original's THERE routine places at (144, 33). Neither
/// is true of this generator, under any of the four readings of its seed rotation that have been
/// tried. These tests record the anchors that are known to hold; the discrepancy is documented in
/// the roadmap rather than papered over.
/// </remarks>
public class GalaxyAnchorTests
{
    [Fact]
    public void TheFirstGalaxyMatchesTheOriginalsOwnSystems()
    {
        StarSystem[] galaxy = Galaxy.GenerateGalaxy(0);

        // The original's first system, and the classic opening systems
        Assert.Equal("TIBEDIED", galaxy[0].Name);
        Assert.Equal((20, 173), (galaxy.First(s => s.Name == "LAVE").X, galaxy.First(s => s.Name == "LAVE").Y));

        // And the systems the original's mission tables name for the first galaxy
        Assert.Contains(galaxy, s => s.Name == "REESDICE");
        Assert.Contains(galaxy, s => s.Name == "AREXE");
    }

    [Fact]
    public void EveryGalaxyHasTwoHundredAndFiftySixSystems()
    {
        for (int galaxy = 0; galaxy < Galaxy.GalaxyCount; galaxy++)
        {
            Assert.Equal(256, Galaxy.GenerateGalaxy(galaxy).Length);
        }
    }

    [Fact]
    public void TheArrivalSystemIsTheNearestToOneHundredAndNinetySix()
    {
        // A galactic jump always arrives at galactic coordinates (96, 96), with the selected system
        // set to the nearest actual system: this is the rule the generator must follow, whatever
        // the system turns out to be called
        foreach (int galaxy in new[] { 0, 1, 2 })
        {
            StarSystem nearest = Galaxy.GenerateGalaxy(galaxy)
                .OrderBy(s => Math.Abs(s.X - 96) + Math.Abs(s.Y - 96))
                .First();

            Assert.True(Math.Abs(nearest.X - 96) + Math.Abs(nearest.Y - 96) < 40,
                $"the arrival system in galaxy {galaxy} should be near (96,96), not ({nearest.X},{nearest.Y})");
        }
    }
}

/// <summary>
/// Checks the galaxy against the original's own mission tables, which name systems by their number
/// within the galaxy.
/// </summary>
/// <remarks>
/// This is the strongest check available on the galaxy generation, because it pins both the names
/// and the ordering. It is also the check that found the real bug: the original's TT20 twists the
/// seeds four times to move from one system to the next, and twisting them once still produces a
/// plausible-looking galaxy — right names, right coordinates — but only every fourth system is one
/// the original actually has, and the rest are inventions. With four twists the tables line up
/// exactly, all 23 entries plus Lave, and Orarra lands on (144, 33) where the original's THERE
/// routine looks for it.
/// </remarks>
public class GalaxyIndexTests
{
    /// <summary>The systems the original's RUPLA table names, by galaxy and system number.</summary>
    public static TheoryData<int, int, string> TrailSystems => new()
    {
        { 0, 211, "TEORGE" },
        { 0, 150, "XEER" },
        { 0, 36, "REESDICE" },
        { 0, 28, "AREXE" },
        { 0, 7, "LAVE" },
        { 1, 253, "ERRIUS" },
        { 1, 79, "INBIBE" },
        { 1, 53, "AUSAR" },
        { 1, 118, "USLERI" },
        { 1, 32, "BEBEGE" },
        { 1, 68, "CEARSO" },
        { 1, 164, "DICELA" },
        { 1, 220, "ERINGE" },
        { 1, 106, "GEXEIN" },
        { 1, 16, "ISARIN" },
        { 1, 162, "LETIBEMA" },
        { 1, 3, "MAISSO" },
        { 1, 107, "ONEN" },
        { 1, 26, "RAMAZA" },
        { 1, 192, "SOSOLE" },
        { 1, 184, "TIVERE" },
        { 1, 5, "VERIAR" },
        { 1, 193, "ORARRA" },
        { 2, 101, "XEVEON" },
    };

    [Theory]
    [MemberData(nameof(TrailSystems))]
    public void SystemsMatchTheOriginalsNumbering(int galaxy, int index, string name)
    {
        StarSystem system = Galaxy.GenerateGalaxy(galaxy)[index];
        Assert.Equal(name, system.Name);
    }

    [Fact]
    public void TheConstrictorsSystemIsWhereTheOriginalLooksForIt()
    {
        // The original's THERE routine hard-codes galaxy 2 at (144, 33), and that is where the
        // Constrictor waits: Orarra, system 193
        StarSystem orarra = Galaxy.GenerateGalaxy(Missions.ConstrictorGalaxy)[Missions.ConstrictorIndex];
        Assert.Equal(Missions.ConstrictorX, orarra.X);
        Assert.Equal(Missions.ConstrictorY, orarra.Y);

        // And the mission's own lookup finds it, so no fallback is needed
        Assert.True(Missions.ConstrictorTargetExists(Galaxy.GalaxySeeds(Missions.ConstrictorGalaxy)));
    }

    [Fact]
    public void AGalacticJumpArrivesAtOrorra()
    {
        // A jump always arrives at the system nearest (96, 96), which the original's documentation
        // names as Ororra in the second galaxy
        StarSystem arrival = Galaxy.GenerateGalaxy(1)
            .OrderBy(s => Math.Abs(s.X - 96) + Math.Abs(s.Y - 96))
            .First();

        Assert.Equal("ORORRA", arrival.Name);
    }
}

/// <summary>
/// Checks the mission hints that replace a system's description while the Constrictor mission is on,
/// which is what turns the mission into a trail.
/// </summary>
/// <summary>
/// The galaxy a session reports is the galaxy it is in, whatever system it happens to be at.
/// </summary>
/// <remarks>
/// A system's seeds are not the galaxy's — except for system 0, whose seeds <em>are</em> the galaxy's
/// own, which is what hid this. Generating a galaxy from another system's seeds gives an entirely
/// different one, all 256 systems differing, so a chart or a nearest-reachable-system search that
/// asked the wrong way would show and pick from a galaxy that does not exist. The tests below are on
/// the game session rather than the generator because that is where the mistake was made.
/// </remarks>
public class SessionGalaxyTests
{
    private static GameSession SessionAt(StarSystem system)
    {
        Commander commander = Commander.CreateDefault();
        commander.CurrentSystem = system;

        return new GameSession(commander, new FlightSim(new Ship(11, "cobra-mk-3", "Cobra Mk III")));
    }

    [Fact]
    public void TheSessionReportsTheGalaxyItIsIn()
    {
        StarSystem[] galaxy = Galaxy.GenerateGalaxy(Galaxy.GalaxySeeds(0));

        // From any system in the galaxy, the session's galaxy is the same 256 systems
        foreach (int index in new[] { 0, 7, 100, 255 })
        {
            GameSession session = SessionAt(galaxy[index]);
            Assert.Equal(
                galaxy.Select(s => s.Name),
                session.SystemsInGalaxy.Select(s => s.Name));
        }
    }

    [Fact]
    public void ThatGalaxyHoldsTheKnownSystemsWhereTheOriginalPutsThem()
    {
        // Lave is system 7, so a session docked there must still see the real galaxy and not one
        // generated from Lave's own seeds
        StarSystem lave = Galaxy.GenerateGalaxy(Galaxy.GalaxySeeds(0)).First(s => s.Name == "LAVE");
        GameSession session = SessionAt(lave);

        StarSystem fromSession = session.SystemsInGalaxy.First(s => s.Name == "LAVE");
        Assert.Equal(20, fromSession.X);
        Assert.Equal(173, fromSession.Y);

        // And the neighbouring systems are the real ones
        Assert.Contains(session.SystemsInGalaxy, s => s.Name == "DISo" || s.Name == "DISO");
        Assert.Contains(session.SystemsInGalaxy, s => s.Name == "LEESTI");
    }
}

public class MissionHintTests
{
    [Fact]
    public void TheTrailSystemsHaveHintsAndTheRestDoNot()
    {
        var hints = Data.DescriptionData.Hints;

        // The disc version has 25 hints. The source's RUPLA and RUGAL tables hold 29 entries, but
        // four of them are behind platform guards the disc does not assemble - Lave and Riedquat
        // are overridden only in the 6502SP, Executive and Master builds - and their tokens 26 and
        // 27 live inside the same guards. Keeping them would make PDESC print past the end of the
        // table, which is what it used to do for Lave.
        Assert.Equal(25, hints.Count);

        // Lave has no hint in the disc version
        Assert.Equal(0, hints.TokenFor(7, 0, true));

        // The first galaxy's trail, by system number
        Assert.Equal(2, hints.TokenFor(150, 0, true));  // Xeer
        Assert.Equal(3, hints.TokenFor(36, 0, true));   // Reesdice
        Assert.Equal(4, hints.TokenFor(28, 0, true));   // Arexe

        // The second galaxy's, ending at the Constrictor's own system
        Assert.Equal(21, hints.TokenFor(184, 1, true));  // Tivere
        Assert.Equal(24, hints.TokenFor(193, 1, true));  // Orarra

        // A system with no hint, and a trail system with the mission not yet started
        Assert.Equal(0, hints.TokenFor(0, 0, true));
        Assert.Equal(0, hints.TokenFor(150, 0, false));
    }

    /// <summary>
    /// A hint with bit 7 set applies in its own galaxy only, not in all of them.
    /// </summary>
    /// <remarks>
    /// PDESC requires bits 0-6 of RUGAL to equal the current galaxy <em>before</em> it looks at bit
    /// 7, so bit 7 means "no mission needed yet" rather than "any galaxy". Three hints carry it:
    /// Teorge in the first galaxy, and Arredi and Anreer in the third. Reading bit 7 as a shortcut
    /// past the galaxy test — which the code used to do — puts the Teorge hint in all eight galaxies
    /// rather than the first, and the other two in all eight rather than the third.
    /// </remarks>
    [Fact]
    public void AHintWithTheAlwaysBitStillBelongsToItsGalaxy()
    {
        var hints = Data.DescriptionData.Hints;

        // Teorge, system 211 of the first galaxy, and not the second
        Assert.NotEqual(0, hints.TokenFor(211, 0, mission1Active: false));
        Assert.Equal(0, hints.TokenFor(211, 1, mission1Active: false));
        Assert.Equal(0, hints.TokenFor(211, 7, mission1Active: false));

        // Arredi and Anreer are in the third galaxy, so they need no mission but do need galaxy 2
        Assert.NotEqual(0, hints.TokenFor(100, 2, mission1Active: false));
        Assert.NotEqual(0, hints.TokenFor(41, 2, mission1Active: false));
        Assert.Equal(0, hints.TokenFor(100, 0, mission1Active: false));
        Assert.Equal(0, hints.TokenFor(41, 1, mission1Active: false));

        // And the mission's own trail still needs the mission, as before
        Assert.NotEqual(0, hints.TokenFor(150, 0, mission1Active: true));
        Assert.Equal(0, hints.TokenFor(150, 0, mission1Active: false));
    }

    [Fact]
    public void HintsBelongToTheirOwnGalaxy()
    {
        var hints = Data.DescriptionData.Hints;

        // Xeer is in the first galaxy's trail, so being in the second galaxy is not enough
        Assert.Equal(2, hints.TokenFor(150, 0, true));
        Assert.Equal(0, hints.TokenFor(150, 1, true));
    }

    [Fact]
    public void OneHintIsAlwaysShown()
    {
        var hints = Data.DescriptionData.Hints;

        // Teorge's entry has bit 7 set, so it shows whatever the mission status
        Assert.Equal(1, hints.TokenFor(211, 0, false));
    }

    [Fact]
    public void TheHintsReadAsTheOriginalHasThem()
    {
        // The trail starts at Xeer and ends at the Constrictor's own system
        string first = Data.DescriptionData.Hint(2, "Xeer", new EliteRemake.Core.Sim.EliteRandom(1));
        Assert.Contains("CONSTRICTOR", first);
        Assert.Contains("REESDICE", first);

        string last = Data.DescriptionData.Hint(24, "Orarra", new EliteRemake.Core.Sim.EliteRandom(1));
        Assert.Contains("PIRATE", last);

        // The hints are in capitals, unlike the generated descriptions
        Assert.Equal(first, first.ToUpperInvariant());
    }

    [Fact]
    public void AHintIsOnlyShownWhereWeAreDocked()
    {
        var hints = Data.DescriptionData.Hints;
        SystemSeeds seeds = Galaxy.GalaxySeeds(0);
        StarSystem[] galaxy = Galaxy.GenerateGalaxy(seeds);

        // Docked at Xeer: the hint shows
        Assert.Equal(2, hints.TokenFor(galaxy[150], galaxy[150], 0, true));

        // Looking at Xeer from somewhere else: it does not
        Assert.Equal(0, hints.TokenFor(galaxy[150], galaxy[0], 0, true));
    }
}
