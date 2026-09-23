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

    /// <summary>A trader, minding its own business on its way to the planet or the station.</summary>
    Trader,
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

        // "a ship type from the following: Cobra Mk III, Python, Boa or Anaconda"
        SpawnKind.Trader => TraderBase + (random.Next() & 3),
        _ => 0,
    };

    /// <summary>The Cobra Mk III, the first of the four ships a trader can be flying.</summary>
    public const int TraderBase = 11;

    /// <summary>
    /// The chance a trader is on its way in to dock rather than minding its own business: the
    /// original's <c>BMI</c> on a random byte, so half of them.
    /// </summary>
    public const int DockingTradersIn256 = 128;

    /// <summary>
    /// Builds the AI flag for a spawned ship. The original sets bits 6 and 7 (AI enabled and
    /// aggressive) and uses bit 0 to give the ship an E.C.M. about a fifth of the time.
    /// </summary>
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
        BlueprintDefaults defaults = BlueprintDefaults.For(type);

        // Spread a pack out around the lead ship
        int offsetX = ((random.Next() & 0xFF) - 128) * (index + 1);
        int offsetY = ((random.Next() & 0xFF) - 128) * (index + 1);
        int z = SpawnDistance + (index * 512);

        // A trader's flags are its own: MTT4 builds them from a single random byte, and half of the
        // traders it makes are on their way in to dock. Nothing is drawn from the generator for the
        // other kinds, so that adding this did not shift the numbers they get.
        bool trader = kind == SpawnKind.Trader;
        byte traderFlags = trader ? (byte)(random.Next() >> 1) : (byte)0;
        bool docking = trader && random.Next() >= DockingTradersIn256;
        if (docking)
        {
            // "Set bits 6 and 7 of A, so the ship has AI (bit 7) and an aggression level of at
            // least 32 out of 63 (this makes the ship more likely to turn towards its target, which
            // in this case is the space station, as we are about to set the ship flags so it is
            // docking)"
            traderFlags |= 0xC0;
        }

        var ship = new Ship(type, string.Empty, $"Type {type}")
        {
            AiFlag = trader ? traderFlags : AiFlag(random),

            // The ship's personality, out of the disc's own E% byte for its type: a pirate hull is
            // hostile and a Fer-de-lance is a bounty hunter that leaves a clean commander alone.
            // NWSHP or's this into the ship's flags rather than assigning it, so anything the
            // spawner set is kept.
            //
            // This used to be a flat NewbHostile, on the reading that the original marks both
            // spawns hostile. It does — in the NES version. On the disc the hostility is the E%
            // byte's job, and forcing the bit made bounty hunters attack commanders they are
            // supposed to ignore.
            NewbFlags = (byte)(defaults.NewbFlags | (docking ? Ship.NewbDocking : 0)),

            // Everything the blueprint gives a new ship, as NWSHP copies it in: without these the
            // ship arrives inert and either cannot reach us or dies to the first hit
            Speed = defaults.Speed,
            MaxEnergy = defaults.MaxEnergy,
            MaxSpeed = defaults.MaxSpeed,
            VisibilityDistance = defaults.VisibilityDistance,
        };

        ship.Energy = ship.MaxEnergy;

        // "Store A in the ship's roll counter, giving it a clockwise roll (as bit 7 is clear), and a
        // 1 in 127 chance of having no damping" — the same byte as the AI flag, which is where the
        // trader's gentle roll comes from
        if (trader)
        {
            ship.Data[ShipDataBlock.RollCounter] = traderFlags;

            // "Set the ship speed to our random number, set to a minimum of 16 and a maximum of 31"
            ship.Speed = (byte)(16 | (random.Next() & 15));
        }

        ship.SetPosition(offsetX, offsetY, z);

        // The original gives a new ship a random orientation, and its AI takes it from there —
        // without this, ships spawn pointing away from us and simply fly off
        double heading = random.Next() * Math.Tau / 256.0;
        double pitch = ((random.Next() & 0x3F) - 32) / 256.0;
        Orientation.FromHeadingPitch(heading, pitch).AsSpan().CopyTo(ship.Data[ShipDataBlock.Orientation..]);

        return ship;
    }
}

/// <summary>
/// The blueprint values a spawned ship needs, so that it can fly and fight without help.
/// </summary>
/// <remarks>
/// <para>
/// The original's NWSHP copies a new ship's speed, energy, top speed and visibility distance out of
/// its blueprint as it adds it to the local bubble, so a ship arrives complete. We had been leaving
/// those to the game layer, which stamps blueprint values on as ships reach it — so anything spawned
/// in the core arrived inert: **no speed**, so it sat at the spawn distance and could never close to
/// within firing range, and **no energy**, so it died to a single hit. Those two faults cost two
/// rounds each to find, both by flying the game, and neither was visible to tests that built their own
/// ships.
/// </para>
/// <para>
/// The table is the blueprint bytes per ship type, written out because the core has no dependency on
/// the data files; the extractor's output is what it was checked against.
/// </para>
/// </remarks>
/// <param name="Speed">The speed it flies at.</param>
/// <param name="MaxEnergy">The energy it starts with, and its ceiling.</param>
/// <param name="MaxSpeed">The fastest it may fly.</param>
/// <param name="VisibilityDistance">How far away it is still drawn as a model.</param>
/// <param name="TargetableArea">
/// The area a hit test uses, which the original stores as a squared radius: 9025 is a Cobra's 95.
/// </param>
/// <param name="Bounty">
/// What destroying it pays, in tenths of a credit, as the original's blueprint byte gives it — so a
/// Sidewinder's 50 is 5.0 credits. Zero for anything that is not worth shooting.
/// </param>
public readonly record struct BlueprintDefaults(
    byte Speed,
    byte MaxEnergy,
    byte MaxSpeed,
    byte VisibilityDistance,
    int TargetableArea,
    int Bounty,
    byte NewbFlags)
{
    /// <summary>The defaults for a ship type, or an all-zero set for a type the table does not cover.</summary>
    public static BlueprintDefaults For(int type) => type switch
    {
        1 => new(44, 2, 44, 14, 1600, 0, 0x00),   // missile
        2 => new(0, 240, 0, 120, 25600, 0, 0x00),   // coriolis
        3 => new(8, 17, 8, 8, 256, 0, 0x01),   // escape-pod
        4 => new(16, 16, 16, 5, 100, 0, 0x00),   // plate
        5 => new(15, 17, 15, 12, 400, 0, 0x00),   // canister
        6 => new(30, 20, 30, 20, 900, 1, 0x00),   // boulder
        7 => new(30, 60, 30, 50, 6400, 5, 0x00),   // asteroid
        8 => new(10, 20, 10, 8, 256, 0, 0x00),   // splinter
        9 => new(8, 32, 8, 22, 2500, 0, 0x21),   // shuttle
        10 => new(10, 32, 10, 16, 2500, 0, 0x61),   // transporter
        11 => new(28, 150, 28, 50, 9025, 0, 0xA0),   // cobra-mk-3
        12 => new(20, 250, 20, 40, 6400, 0, 0xA0),   // python
        13 => new(24, 250, 24, 40, 4900, 0, 0xA0),   // boa
        14 => new(14, 252, 14, 50, 10000, 0, 0xA1),   // anaconda
        16 => new(32, 100, 32, 23, 5625, 0, 0xC2),   // viper
        17 => new(37, 70, 37, 20, 4225, 50, 0x0C),   // sidewinder
        18 => new(30, 90, 30, 25, 4900, 150, 0x8C),   // mamba
        19 => new(30, 80, 30, 25, 3600, 100, 0x8C),   // krait
        20 => new(24, 85, 24, 23, 2500, 40, 0x8C),   // adder
        21 => new(30, 70, 30, 18, 9801, 55, 0x0C),   // gecko
        22 => new(26, 90, 26, 19, 9801, 75, 0x8C),   // cobra-mk-1
        23 => new(23, 30, 23, 19, 9801, 0, 0x04),   // worm
        24 => new(28, 150, 28, 50, 9025, 175, 0x8C),   // cobra-mk-3-p
        25 => new(40, 150, 40, 40, 3600, 200, 0x8C),   // asp-mk-2
        26 => new(20, 250, 20, 40, 6400, 200, 0x8C),   // python-p
        27 => new(30, 160, 30, 40, 1600, 0, 0x82),   // fer-de-lance
        28 => new(25, 100, 25, 40, 900, 50, 0x0C),   // moray
        29 => new(39, 240, 39, 55, 9801, 500, 0x0C),   // thargoid
        30 => new(30, 20, 30, 20, 1600, 50, 0x04),   // thargon
        31 => new(36, 252, 36, 45, 4225, 0, 0x04),   // constrictor
        _ => default,   // a type the table does not cover
    };
}
