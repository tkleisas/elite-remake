using EliteRemake.Core.Maths;
using EliteRemake.Core.Universe;

namespace EliteRemake.Core.Sim;

/// <summary>What kind of ship a spawn produced, for the game to report.</summary>
public enum SpawnKind
{
    /// <summary>Nothing spawned this time.</summary>
    None,

    /// <summary>A group of pirates, flying in formation.</summary>
    Pirates,

    /// <summary>A lone bounty hunter, after us because of our legal status.</summary>
    BountyHunter,
}

/// <summary>
/// Populates a system with other ships.
/// </summary>
/// <remarks>
/// The original's main game loop decides whether to spawn something each iteration. In the disc
/// version the rules are:
///
/// <list type="bullet">
/// <item>A random byte is compared against 120, so there is a 47% chance of continuing at all.</item>
/// <item>Anarchy systems (government 0) always continue; otherwise the low three bits of the random
/// byte are compared against the government, so the safer the system, the less likely a spawn.</item>
/// <item>Then there is a 61% chance of a pack of pirates and otherwise a lone bounty hunter.</item>
/// <item>Pirates fly one of eight pack-hunter types starting at the Sidewinder, and bounty hunters
/// one of four starting at the Cobra Mk III (pirate) — which is why the Moray, which sits after the
/// Fer-de-lance in the table, never spawns.</item>
/// </list>
///
/// Ships are placed around 9728 units ahead of us with a random offset, which is the distance the
/// original uses when it fixes a new ship's position.
/// </remarks>
public static class Spawner
{
    /// <summary>The first pack-hunter type: the Sidewinder.</summary>
    public const int PackHunterBase = 17;

    /// <summary>The number of pack-hunter types: Sidewinder to Cobra Mk III (pirate).</summary>
    public const int PackHunterCount = 8;

    /// <summary>The first bounty hunter type: the Cobra Mk III (pirate).</summary>
    public const int BountyHunterBase = 24;

    /// <summary>The number of bounty hunter types: Cobra Mk III (pirate) to Fer-de-lance.</summary>
    public const int BountyHunterCount = 4;

    /// <summary>The z distance, in units, that a newly spawned ship appears at.</summary>
    public const int SpawnDistance = 38 * 256;

    /// <summary>How many frames the extra-vessels counter delays the next spawn by.</summary>
    public const int SpawnDelay = 64;

    /// <summary>
    /// Decides whether to spawn anything this iteration and, if so, what.
    /// </summary>
    /// <param name="system">The system we are in.</param>
    /// <param name="random">The random number generator.</param>
    /// <returns>The kind of spawn, or <see cref="SpawnKind.None"/>.</returns>
    public static SpawnKind ChooseSpawn(StarSystem system, EliteRandom random)
    {
        // There is a 47% chance of spawning anything at all in the disc version
        byte roll = random.Next();
        if (roll >= 120)
        {
            return SpawnKind.None;
        }

        // Safe systems are less likely to have company: anarchy always continues, otherwise the
        // low bits of the random byte must exceed the government
        if (system.Government != 0 && (roll & 7) < system.Government)
        {
            return SpawnKind.None;
        }

        // Then it is 61% pirates and otherwise a lone bounty hunter
        byte choice = random.Next();
        return choice >= 100 ? SpawnKind.Pirates : SpawnKind.BountyHunter;
    }

    /// <summary>Picks the ship type for a spawn, using the original's tables.</summary>
    public static int ShipType(SpawnKind kind, EliteRandom random) => kind switch
    {
        SpawnKind.Pirates => PackHunterBase + (random.Next() & (PackHunterCount - 1)),
        SpawnKind.BountyHunter => BountyHunterBase + (random.Next() & (BountyHunterCount - 1)),
        _ => 0,
    };

    /// <summary>
    /// Builds the AI flag for a spawned ship. The original sets bits 6 and 7 (AI enabled and
    /// aggressive) and uses bit 0 to give the ship an E.C.M. about a fifth of the time.
    /// </summary>
    public static byte AiFlag(EliteRandom random)
    {
        bool ecm = random.Next() >= 200;
        int flag = 0xC0 | (ecm ? 1 : 0);

        // Pirates are a little less single-minded than bounty hunters
        flag |= random.Next() & 0x0F;
        return (byte)flag;
    }

    /// <summary>
    /// Creates a ship for a spawn, placed ahead of us with a random offset and pointing our way.
    /// </summary>
    /// <param name="kind">What kind of spawn this is.</param>
    /// <param name="system">The system we are in.</param>
    /// <param name="random">The random number generator.</param>
    /// <param name="index">Index within a pirate pack, used to spread the group out.</param>
    public static Ship Create(SpawnKind kind, StarSystem system, EliteRandom random, int index = 0)
    {
        int type = ShipType(kind, random);

        // Spread a pack out around the lead ship
        int offsetX = ((random.Next() & 0xFF) - 128) * (index + 1);
        int offsetY = ((random.Next() & 0xFF) - 128) * (index + 1);
        int z = SpawnDistance + (index * 512);

        var ship = new Ship(type, string.Empty, $"Type {type}")
        {
            AiFlag = AiFlag(random),
        };

        ship.SetPosition(offsetX, offsetY, z);

        // The original gives a new ship a random orientation, and its AI takes it from there —
        // without this, ships spawn pointing away from us and simply fly off
        double heading = random.Next() * Math.Tau / 256.0;
        double pitch = ((random.Next() & 0x3F) - 32) / 256.0;
        Orientation.FromHeadingPitch(heading, pitch).AsSpan().CopyTo(ship.Data[ShipDataBlock.Orientation..]);

        return ship;
    }
}
