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
    /// The random byte at or above which nothing spawns: the disc version tests <c>CMP #120</c>,
    /// which is the 47% chance its own comment describes.
    /// </summary>
    public const int AnySpawnThreshold = 120;

    /// <summary>
    /// The random byte at or above which a pack of pirates rather than a lone bounty hunter
    /// appears: <c>CMP #100</c>, the 61% chance.
    /// </summary>
    public const int PackThreshold = 100;

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
        if (roll >= AnySpawnThreshold)
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
        return choice >= PackThreshold ? SpawnKind.Pirates : SpawnKind.BountyHunter;
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
    /// <summary>
    /// The speed a spawned ship of this type flies at, from its blueprint.
    /// </summary>
    /// <remarks>
    /// The original's NWSHP takes the new ship's speed from byte #15 of its blueprint, so a ship that
    /// arrives with no speed set by the caller still flies. We had been leaving speed to the game
    /// layer, which stamps blueprint values on as ships reach it — so a ship spawned in the core had
    /// speed zero, sat at the spawn distance, and could never close to within firing range. Nothing
    /// the spawner produced could reach us, which is why a commander parked in a busy system was
    /// never shot at.
    ///
    /// The table is the blueprint byte per type, written out because the core has no dependency on
    /// the data files; the extractor's output is what it was checked against.
    /// </remarks>
    public static byte SpeedFor(int type) => type switch
    {
        1 => 44,   // missile
        2 => 0,   // coriolis
        3 => 8,   // escape-pod
        4 => 16,   // plate
        5 => 15,   // canister
        6 => 30,   // boulder
        7 => 30,   // asteroid
        8 => 10,   // splinter
        9 => 8,   // shuttle
        10 => 10,   // transporter
        11 => 28,   // cobra-mk-3
        12 => 20,   // python
        13 => 24,   // boa
        14 => 14,   // anaconda
        16 => 32,   // viper
        17 => 37,   // sidewinder
        18 => 30,   // mamba
        19 => 30,   // krait
        20 => 24,   // adder
        21 => 30,   // gecko
        22 => 26,   // cobra-mk-1
        23 => 23,   // worm
        24 => 28,   // cobra-mk-3-p
        25 => 40,   // asp-mk-2
        26 => 20,   // python-p
        27 => 30,   // fer-de-lance
        28 => 25,   // moray
        29 => 39,   // thargoid
        30 => 30,   // thargon
        31 => 36,   // constrictor
        _ => 0,
    };

    /// <summary>
    /// The AI flag for a hostile ship we are about to spawn.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The disc's bounty hunters carry no E.C.M.</b> The 22% chance of one is guarded by
    /// <c>_CASSETTE_VERSION OR _DEMO_VERSION OR _ELECTRON_VERSION OR _6502SP_VERSION OR _C64_VERSION
    /// OR _APPLE_VERSION OR _MASTER_VERSION OR _NES_VERSION</c> — every version except this one, and
    /// the source says so in as many words: "Lone bounty hunters in the disc version don't have
    /// E.C.M., while in the other versions they have a 22% chance of having E.C.M." Ours had the
    /// chance, which is the cassette behaviour.
    /// </para>
    /// <para>
    /// Bit 7 is the ship having AI at all and bit 6 is aggression, as the original sets with
    /// <c>ORA #%11000000</c>; the low bits vary the aggression within that, which is what stops a
    /// pack flying as one.
    /// </para>
    /// </remarks>
    public static byte AiFlag(EliteRandom random)
    {
        // Bits 7 and 6: AI, and aggression
        int flag = 0xC0;

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

            // Both spawns are hostile — the original sets bit 2 of NEWB before either branch, with
            // the comment "set bit 2 of the NEWB flags ... so the ship we are about to spawn is
            // hostile" — and the flight loop decides whether to fight from that bit rather than from
            // the AI flag alone. Leaving it clear meant nothing the spawner produced could attack:
            // every pirate and bounty hunter arrived aggressive but peaceful.
            NewbFlags = Ship.NewbHostile,

            // A speed to fly at, as NWSHP takes from the blueprint, so the ship can actually reach us
            Speed = SpeedFor(type),
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
