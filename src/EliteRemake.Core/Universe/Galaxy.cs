using System.Text;

using EliteRemake.Core.Maths;

namespace EliteRemake.Core.Universe;

/// <summary>
/// The three 16-bit seeds that define a star system, as the original keeps them in QQ15.
/// </summary>
/// <param name="S0">The first seed, whose high byte is the system's y coordinate.</param>
/// <param name="S1">The second seed, whose high byte is the system's x coordinate.</param>
/// <param name="S2">The third seed, which drives the system's name and radius.</param>
public readonly record struct SystemSeeds(ushort S0, ushort S1, ushort S2)
{
    /// <summary>Low byte of the first seed.</summary>
    public byte S0Lo => (byte)S0;

    /// <summary>High byte of the first seed: the system's y coordinate.</summary>
    public byte S0Hi => (byte)(S0 >> 8);

    /// <summary>Low byte of the second seed.</summary>
    public byte S1Lo => (byte)S1;

    /// <summary>High byte of the second seed: the system's x coordinate.</summary>
    public byte S1Hi => (byte)(S1 >> 8);

    /// <summary>Low byte of the third seed.</summary>
    public byte S2Lo => (byte)S2;

    /// <summary>High byte of the third seed.</summary>
    public byte S2Hi => (byte)(S2 >> 8);

    /// <summary>The six bytes, in the order the original stores them.</summary>
    public byte[] ToBytes() => [S0Lo, S0Hi, S1Lo, S1Hi, S2Lo, S2Hi];

    /// <summary>Builds seeds from the original's six bytes.</summary>
    public static SystemSeeds FromBytes(ReadOnlySpan<byte> bytes) => new(
        (ushort)(bytes[0] | (bytes[1] << 8)),
        (ushort)(bytes[2] | (bytes[3] << 8)),
        (ushort)(bytes[4] | (bytes[5] << 8)));
}

/// <summary>One of the 256 star systems in a galaxy.</summary>
/// <param name="Index">The system's number within its galaxy, 0-255.</param>
/// <param name="Seeds">The seeds that define it.</param>
/// <param name="Name">The generated name, e.g. "Lave".</param>
/// <param name="X">Galactic x coordinate.</param>
/// <param name="Y">Galactic y coordinate.</param>
/// <param name="Economy">Economy type, 0-7.</param>
/// <param name="Government">Government type, 0-7.</param>
/// <param name="TechLevel">Tech level, 0-15.</param>
/// <param name="Population">Population, in hundreds of millions.</param>
/// <param name="Productivity">Productivity, as the original's 16-bit value.</param>
/// <param name="Radius">Average radius in km.</param>
public readonly record struct StarSystem(
    int Index,
    SystemSeeds Seeds,
    string Name,
    int X,
    int Y,
    int Economy,
    int Government,
    int TechLevel,
    int Population,
    int Productivity,
    int Radius);

/// <summary>
/// The procedural galaxy: the same 8 galaxies of 256 systems the original generates.
/// </summary>
/// <remarks>
/// Every system is defined by three 16-bit seeds. Galaxy 0 starts from a fixed seed (&amp;5A4A,
/// &amp;0248, &amp;B753) and system <c>n</c> is the seed twisted <c>n</c> times; a galactic hyperspace
/// jump rotates each byte of the galaxy seed one bit to the left, so after eight jumps the galaxies
/// come back round to the start. Everything about a system — its name, coordinates, economy,
/// government, tech level, population, productivity and radius — is derived from its seeds, so this
/// class reproduces the original's universe exactly.
/// </remarks>
public static class Galaxy
{
    /// <summary>The number of systems in a galaxy.</summary>
    public const int SystemsPerGalaxy = 256;

    /// <summary>The number of galaxies.</summary>
    public const int GalaxyCount = 8;

    /// <summary>The seeds for system 0 of galaxy 0, which the original calls Tibedied.</summary>
    public static readonly SystemSeeds FirstGalaxySeeds = new(0x5A4A, 0x0248, 0xB753);

    /// <summary>The two-letter tokens (the original's QQ16 table), indexed by token number - 128.</summary>
    public static readonly string[] TwoLetterTokens =
    [
        "AL", "LE", "XE", "GE", "ZA", "CE", "BI", "SO",
        "US", "ES", "AR", "MA", "IN", "DI", "RE", "A?",
        "ER", "AT", "EN", "BE", "RA", "LA", "VE", "TI",
        "ED", "OR", "QU", "AN", "TE", "IS", "RI", "ON",
    ];

    /// <summary>Names of the eight economy types, as the original's token table has them.</summary>
    public static readonly string[] EconomyNames =
    [
        "Rich Industrial", "Average Industrial", "Poor Industrial", "Mainly Industrial",
        "Mainly Agricultural", "Rich Agricultural", "Average Agricultural", "Poor Agricultural",
    ];

    /// <summary>Names of the eight government types.</summary>
    public static readonly string[] GovernmentNames =
    [
        "Anarchy", "Feudal", "Multi-Government", "Dictatorship",
        "Communist", "Confederacy", "Democracy", "Corporate State",
    ];

    /// <summary>
    /// TT54: twists the seeds once, which is how the original moves from one system to the next.
    /// </summary>
    public static SystemSeeds Twist(SystemSeeds seeds)
    {
        int tmpLow = seeds.S0Lo + seeds.S1Lo;
        byte x = (byte)tmpLow;
        int carry = tmpLow > 0xFF ? 1 : 0;
        int tmpHigh = seeds.S0Hi + seeds.S1Hi + carry;
        byte y = (byte)tmpHigh;

        ushort s0 = seeds.S1;
        ushort s1 = seeds.S2;

        int s2Low = x + (byte)s1;
        byte s2Hi = (byte)(y + (byte)(s1 >> 8) + (s2Low > 0xFF ? 1 : 0));

        return new SystemSeeds(s0, s1, (ushort)(((s2Hi << 8) | (byte)s2Low)));
    }

    /// <summary>
    /// The seeds of the next galaxy: a galactic hyperspace jump rotates each byte of the galaxy
    /// seed one bit to the left (the original's GHY routine).
    /// </summary>
    public static SystemSeeds NextGalaxy(SystemSeeds seeds)
    {
        Span<byte> bytes = seeds.ToBytes();
        for (int i = 0; i < bytes.Length; i++)
        {
            int value = bytes[i];
            bytes[i] = (byte)(((value << 1) | (value >> 7)) & 0xFF);
        }

        return SystemSeeds.FromBytes(bytes);
    }

    /// <summary>The seeds of a galaxy's first system after the given number of galactic jumps.</summary>
    public static SystemSeeds GalaxySeeds(int galaxy)
    {
        SystemSeeds seeds = FirstGalaxySeeds;
        for (int i = 0; i < galaxy % GalaxyCount; i++)
        {
            seeds = NextGalaxy(seeds);
        }

        return seeds;
    }

    /// <summary>The system's galactic coordinates: x is s1_hi and y is s0_hi.</summary>
    public static (int X, int Y) Coordinates(SystemSeeds seeds) => (seeds.S1Hi, seeds.S0Hi);

    /// <summary>
    /// The system's name, built from the two-letter token table exactly as the original's <c>cpl</c>
    /// routine does: three or four token pairs taken from s2_hi as the seeds are twisted.
    /// </summary>
    public static string Name(SystemSeeds seeds)
    {
        var name = new StringBuilder(8);

        // Bit 6 of s0_lo decides whether the name has four pairs of letters or three
        int count = (seeds.S0Lo & 0x40) != 0 ? 4 : 3;

        SystemSeeds current = seeds;
        for (int i = 0; i < count; i++)
        {
            int token = current.S2Hi & 0x1F;
            if (token != 0)
            {
                // Token 15 is "A?" in the original's table, and the question mark is not part of
                // the name: it is skipped, so "A?" + "RE" + "XE" gives the system AREXE
                foreach (char letter in TwoLetterTokens[token])
                {
                    if (letter != '?')
                    {
                        name.Append(letter);
                    }
                }
            }

            current = Twist(current);
        }

        return name.ToString();
    }

    /// <summary>
    /// TT24: works out a system's economy, government, tech level, population, productivity and
    /// radius from its seeds.
    /// </summary>
    public static StarSystem Describe(SystemSeeds seeds, int index)
    {
        int economy = seeds.S0Hi & 0x07;
        int government = (seeds.S1Lo >> 3) & 0x07;

        // Anarchy and feudal systems have their economy adjusted
        if (government < 2)
        {
            economy |= 0x02;
        }

        int techLevel = (economy ^ 0x07) + (seeds.S1Hi & 0x03) + ((government >> 1) + (government & 1));
        int population = (techLevel * 4) + economy + government + 1;

        // Productivity = (flipped economy + 3) * (government + 4) * population * 8
        byte p = (byte)((economy ^ 0x07) + 3);
        byte q = (byte)(government + 4);
        (byte high, byte low) = MultiplyUnsigned(p, q);
        (high, low) = MultiplyUnsigned(low, (byte)population);
        int productivity = (low << 3) | (high >> 5);
        productivity |= (high << 3) << 8;

        int radius = (((seeds.S2Hi & 0x0F) + 11) * 256) + seeds.S1Hi;

        return new StarSystem(
            index,
            seeds,
            Name(seeds),
            seeds.S1Hi,
            seeds.S0Hi,
            economy,
            government,
            techLevel,
            population,
            productivity,
            radius);
    }

    /// <summary>MULTU: (A P) = P * Q for two unsigned bytes.</summary>
    private static (byte High, byte Low) MultiplyUnsigned(byte p, byte q)
    {
        int product = p * q;
        return ((byte)(product >> 8), (byte)product);
    }

    /// <summary>
    /// How many times the seeds are twisted to move from one system to the next. The original's TT20
    /// twists them four times, which is easy to miss: twist them once and a galaxy still looks
    /// plausible, but only every fourth system is one the original actually has.
    /// </summary>
    public const int TwistsPerSystem = 4;

    /// <summary>Generates all 256 systems of a galaxy, in order.</summary>
    public static StarSystem[] GenerateGalaxy(SystemSeeds galaxySeeds)
    {
        var systems = new StarSystem[SystemsPerGalaxy];
        SystemSeeds seeds = galaxySeeds;
        for (int i = 0; i < SystemsPerGalaxy; i++)
        {
            systems[i] = Describe(seeds, i);
            seeds = NextSystem(seeds);
        }

        return systems;
    }

    /// <summary>TT20: the seeds of the next system, four twists on from this one.</summary>
    public static SystemSeeds NextSystem(SystemSeeds seeds)
    {
        for (int i = 0; i < TwistsPerSystem; i++)
        {
            seeds = Twist(seeds);
        }

        return seeds;
    }

    /// <summary>Generates all 256 systems of the given galaxy number.</summary>
    public static StarSystem[] GenerateGalaxy(int galaxy) => GenerateGalaxy(GalaxySeeds(galaxy));

    /// <summary>
    /// TT111: finds the system closest to the given galactic coordinates, using the original's
    /// distance measure of half the x difference plus half the y difference.
    /// </summary>
    /// <returns>The closest system, and the original's distance value for it.</returns>
    public static (StarSystem System, int Distance) FindClosest(SystemSeeds galaxySeeds, int x, int y)
    {
        int best = 127;
        SystemSeeds bestSeeds = galaxySeeds;
        int bestIndex = 0;

        SystemSeeds seeds = galaxySeeds;
        for (int index = 0; index < SystemsPerGalaxy; index++)
        {
            int distance = (Math.Abs(seeds.S1Hi - x) / 2) + (Math.Abs(seeds.S0Hi - y) / 2);
            if (distance < best)
            {
                best = distance;
                bestSeeds = seeds;
                bestIndex = index;
            }

            seeds = NextSystem(seeds);
        }

        return (Describe(bestSeeds, bestIndex), best);
    }

    /// <summary>
    /// The distance between two systems in tenths of a light year, worked out exactly as the
    /// original does: the squares of the coordinate differences are added and the result is passed
    /// through the original's 8-bit square root, then multiplied by four.
    /// </summary>
    /// <remarks>
    /// This matters because the original's square root is a restoring division that is only eight
    /// bits wide, so the distances it produces are slightly coarse. Jump ranges in the original are
    /// measured in these units, which is why a full tank takes you exactly 7.0 light years.
    /// </remarks>
    public static int DistanceTenths(StarSystem from, StarSystem to)
    {
        byte dx = (byte)Math.Abs(to.X - from.X);
        byte dy = (byte)Math.Abs(to.Y - from.Y);

        ushort xSquared = EliteMath.Squa2(dx);
        byte r = EliteMath.Hi(xSquared);
        byte q = EliteMath.Lo(xSquared);

        ushort ySquared = EliteMath.Squa2(dy);
        byte t = EliteMath.Hi(ySquared);
        byte p = EliteMath.Lo(ySquared);

        int low = q + p;
        q = (byte)low;
        int high = r + t + (low > 0xFF ? 1 : 0);
        r = (byte)high;

        byte root = EliteMath.Sqrt(r, q); // LL5

        // The original shifts the square root left twice, so the distance is four times as much
        return root * 4;
    }

    /// <summary>The distance between two systems in light years, for display.</summary>
    public static double DistanceLightYears(StarSystem from, StarSystem to) =>
        DistanceTenths(from, to) / 10.0;

    /// <summary>The plain coordinate distance between two systems, which is what the charts plot.</summary>
    public static double CoordinateDistance(StarSystem from, StarSystem to)
    {
        double dx = to.X - from.X;
        double dy = to.Y - from.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
