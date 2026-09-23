using EliteRemake.Core.Maths;
using EliteRemake.Core.Universe;

namespace EliteRemake.Core.Sim;

/// <summary>
/// The loose debris of a system: asteroids, boulders and cargo canisters, what they leave behind
/// when they are destroyed, and how they are scooped up.
/// </summary>
/// <remarks>
/// The original spawns junk in the main game loop with a 13% chance, and only if there are fewer
/// than three bits of junk in the local bubble already. In the disc version the type is chosen by
/// one random byte: 2% cargo canisters, 50% boulders and 48% asteroids. Junk tumbles, which the
/// original does by filling the roll counter with random bits.
///
/// Destroying junk is where mining comes in. A mining laser (power 50) breaks an asteroid into one
/// to three scoopable splinters, and a boulder into one about half the time; any other laser
/// destroys the rock outright and leaves a cargo canister instead.
/// </remarks>
public static class Debris
{
    /// <summary>The cargo canister type number.</summary>
    public const int Canister = 5;

    /// <summary>The boulder type number.</summary>
    public const int Boulder = 6;

    /// <summary>The asteroid type number.</summary>
    public const int Asteroid = 7;

    /// <summary>The splinter type number, which only mining lasers produce.</summary>
    public const int Splinter = 8;

    /// <summary>The most bits of junk the original allows in the bubble at once.</summary>
    public const int MaxJunk = 3;

    /// <summary>The distance a bit of junk appears at, in units.</summary>
    public const int SpawnDistance = 38 * 256;

    /// <summary>How close an item must be to scoop it.</summary>
    public const int ScoopRange = 200;

    /// <summary>True for the types that count as junk for the original's limit.</summary>
    public static bool IsJunk(int shipType) => shipType is Canister or Boulder or Asteroid or Splinter;

    /// <summary>The escape pod's ship type, which scoops as slaves.</summary>
    public const int EscapePod = 3;

    /// <summary>The Thargon's ship type, which scoops as alien items.</summary>
    /// <summary>
    /// The Thargon, the Thargoid's small companion. Its type is 30 — 20 is the Adder, which is not
    /// scoopable and left Thargons unscoopable for as long as it stood.
    /// </summary>
    public const int Thargon = 30;

    /// <summary>
    /// True for the types that can be scooped up. The disc version does not keep a list of these:
    /// the high nibble of the first byte of a ship's blueprint says whether it is scoopable and what
    /// it gives when scooped, which is why a canister holds whatever its blueprint says, an escape
    /// pod scoops as slaves (three tonnes of them), a splinter as minerals and a Thargon as alien
    /// items. This list is the fallback for when no blueprint is available.
    /// </summary>
    public static bool IsScoopable(int shipType) =>
        shipType is Canister or Splinter or EscapePod or Thargon;

    /// <summary>
    /// The original's junk spawn: a 13% chance of a rock or a canister, provided there are fewer
    /// than three bits of junk about already.
    /// </summary>
    /// <param name="random">The random number generator.</param>
    /// <param name="junkInBubble">How many bits of junk are already in the bubble.</param>
    /// <returns>The ship type to spawn, or 0 for nothing.</returns>
    public static bool WantsJunk(EliteRandom random, int junkInBubble)
    {
        // "If we already have 3 or more bits of junk in the local bubble, jump down to MTT1"
        if (junkInBubble >= MaxJunk)
        {
            return false;
        }

        // "If A >= 35 (87% chance), jump down to MTT1 to skip the spawning of an asteroid or cargo
        // canister" — so the branch is taken on the other 13%
        return random.Next() < JunkBranchThreshold;
    }

    /// <summary>
    /// The roll that opens the junk branch: the original's <c>CMP #35</c>, so it is taken on 35
    /// iterations in 256.
    /// </summary>
    public const int JunkBranchThreshold = 35;

    /// <summary>
    /// Picks which piece of junk appears.
    /// </summary>
    /// <remarks>
    /// <c>CMP #10</c> sets the carry on 96% of rolls, and the type is <c>#OIL + (A AND 1) + carry</c>
    /// — so a cargo canister 2% of the time, a boulder 50% and an asteroid 48%.
    /// </remarks>
    public static int ChooseJunkType(EliteRandom random)
    {
        int roll = random.Next();
        int bonus = roll >= 10 ? 1 : 0;
        return Canister + (roll & 1) + bonus;
    }

    /// <summary>Creates a bit of junk, tumbling 9728 units ahead of us.</summary>
    public static Ship CreateJunk(int shipType, EliteRandom random)
    {
        var ship = new Ship(shipType, string.Empty, $"Type {shipType}");

        // The original puts the junk far ahead with a random offset of up to 512 units
        int x = ((random.Next() & 0xFF) - 128) * 4;
        int y = ((random.Next() & 0xFF) - 128) * 4;
        ship.SetPosition(x, y, SpawnDistance);

        // Junk tumbles: the roll counter is filled with random bits
        ship.Data[ShipDataBlock.RollCounter] = (byte)(random.Next() | 0b01101111);
        ship.Data[ShipDataBlock.PitchCounter] = (byte)(random.Next() | 0b01111111);
        ship.Speed = (byte)(16 + (random.Next() & 15));

        return ship;
    }

    /// <summary>What a destroyed ship leaves behind.</summary>
    /// <param name="shipType">The type of the ship that was destroyed.</param>
    /// <param name="laserPower">The power of the laser that destroyed it.</param>
    /// <param name="random">The random number generator.</param>
    /// <returns>How many items to spawn, and their type.</returns>
    public static (int Type, int Count) DestructionDrops(int shipType, int laserPower, EliteRandom random)
    {
        bool mining = laserPower == Combat.MiningLaserPower;

        if (mining && shipType == Asteroid)
        {
            // An asteroid shatters into one to three scoopable splinters
            int count = (random.Next() | 1) & 3;
            return (Splinter, Math.Max(1, count));
        }

        if (mining && shipType == Boulder)
        {
            // A boulder gives up a splinter half the time
            return random.Next() < 128 ? (Splinter, 1) : (0, 0);
        }

        if (shipType is Asteroid or Boulder)
        {
            // Blasted with anything else, a rock leaves a cargo canister
            return (Canister, 1);
        }

        // Ships sometimes leave a canister behind
        return random.Next() < 64 ? (Canister, 1) : (0, 0);
    }

    /// <summary>
    /// Tries to scoop an item. Returns the commodity and amount collected, or null if the item
    /// cannot be scooped.
    /// </summary>
    /// <param name="item">The item we are flying at.</param>
    /// <param name="commander">The commander, who must have fuel scoops and hold space.</param>
    /// <param name="marketItem">The commodity a canister of this type carries.</param>
    public static (int Item, int Amount)? TryScoop(Ship item, Commander commander, int marketItem)
    {
        if (!commander.FuelScoops || item.IsKilled)
        {
            return null;
        }

        // The caller passes a zero-based commodity index, converted from the original's one-based
        // market item. A zero means the ship cannot be scooped at all. Where no blueprint is
        // available — the Core used on its own — fall back to the same indices for the three types
        // the original gives a commodity of their own: slaves, furs and gem-stones.
        int commodity = marketItem != 0
            ? marketItem
            : item.Type switch
            {
                // The source gives the first two explicitly: "Market item when scooped = 11 + 1 = 12
                // (minerals)" for the splinter, and the escape pod's comment says slaves. The thargon's
                // nibble of 15 gives 16, alien items, which its comment confirms.
                Splinter => 12,      // minerals
                EscapePod => 3,      // slaves
                Thargon => 16,       // alien items
                _ => 0,
            };

        if (commodity == 0 || !IsScoopable(item.Type))
        {
            return null;
        }

        if (commander.CargoFree <= 0)
        {
            return null; // nowhere to put it
        }
        int added = commander.AddCargo(commodity, 1);
        if (added == 0)
        {
            return null;
        }

        item.IsKilled = true;
        return (commodity, added);
    }
}
