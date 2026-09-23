using EliteRemake.Core.Universe;

namespace EliteRemake.Core.Sim;

/// <summary>
/// The two missions, as the original tracks them in the bits of its TP byte.
/// </summary>
/// <remarks>
/// The original keeps both missions in one byte of the commander's block:
///
/// <code>
///     bit 0: mission 1 is in progress
///     bit 1: mission 1 is complete
///     bit 2: mission 2 is in progress (we are on our way to pick up the plans)
///     bit 3: we are carrying the plans
/// </code>
///
/// Mission 1 sends us after a Constrictor in galaxy 2 at coordinates (144, 33) — the target is
/// hard-coded in the original's THERE routine, which simply checks whether we are in that system.
/// The Constrictor only appears there, and only while the mission is in progress, carrying an AI
/// flag of %11111001: E.C.M., the highest aggression, hostile and under AI control. Killing it
/// completes the mission and pays 5,000 credits.
///
/// Mission 2 has us carry documents, and while we are carrying them there is an extra 22% chance
/// of a Thargoid appearing in any system — the Thargoids are trying to stop us. Delivering the
/// plans earns a naval energy unit.
/// </remarks>
public sealed class Missions
{
    /// <summary>The galaxy the Constrictor is hiding in, as the original's THERE has it.</summary>
    public const int ConstrictorGalaxy = 1;

    /// <summary>The x coordinate of the Constrictor's system.</summary>
    public const int ConstrictorX = 144;

    /// <summary>The y coordinate of the Constrictor's system.</summary>
    public const int ConstrictorY = 33;

    /// <summary>The Constrictor's ship type.</summary>
    public const int ConstrictorType = 31;

    /// <summary>
    /// The Constrictor's system number within its galaxy, from the original's RUPLA table: Orarra,
    /// system 193, which sits at (144, 33).
    /// </summary>
    public const int ConstrictorIndex = 193;

    /// <summary>The Thargoid ship type.</summary>
    public const int ThargoidType = 29;

    /// <summary>The AI flag the Constrictor carries: E.C.M., top aggression, hostile, AI on.</summary>
    public const byte ConstrictorAiFlag = 0b1111_1001;

    /// <summary>The extra chance of a Thargoid while we are carrying the plans.</summary>
    public const int PlansThargoidChance = 56; // 22% of 256

    /// <summary>The reward for completing mission 1, in tenths of a credit.</summary>
    public const int ConstrictorReward = 50000;

    /// <summary>Mission 1 is in progress.</summary>
    public bool Mission1Active { get; set; }

    /// <summary>Mission 1 has been completed.</summary>
    public bool Mission1Complete { get; set; }

    /// <summary>Mission 2 is in progress: we are on our way to collect the plans.</summary>
    public bool Mission2Active { get; set; }

    /// <summary>We are carrying the plans, which the Thargoids want.</summary>
    public bool CarryingPlans { get; set; }

    /// <summary>True while the Constrictor should be in our system.</summary>
    public bool ConstrictorIsHere { get; set; }

    /// <summary>The mission status byte, as the original's TP stores it.</summary>
    public int StatusByte =>
        (Mission1Active ? 1 : 0) |
        (Mission1Complete ? 2 : 0) |
        (Mission2Active ? 4 : 0) |
        (CarryingPlans ? 8 : 0);

    /// <summary>Rebuilds the missions from the original's status byte.</summary>
    public static Missions FromStatusByte(int status) => new()
    {
        Mission1Active = (status & 1) != 0,
        Mission1Complete = (status & 2) != 0,
        Mission2Active = (status & 4) != 0,
        CarryingPlans = (status & 8) != 0,
    };

    /// <summary>
    /// True when the system we are in is the Constrictor's, which the original's THERE works out
    /// from the galaxy number and the coordinates.
    /// </summary>
    /// <remarks>
    /// The original hard-codes galaxy 2 at (144, 33), and with the seeds twisted four times per
    /// system — as its TT20 does — the system at those coordinates is Orarra, system 193, exactly
    /// where the original's mission tables put it. The nearest-system fallback is kept only as a
    /// safety net; ConstrictorTargetExists reports whether the original's coordinates are there.
    /// </remarks>
    public static bool IsConstrictorSystem(StarSystem system, int galaxyNumber) =>
        galaxyNumber == ConstrictorGalaxy && system.Seeds == ConstrictorTarget(Galaxy.GalaxySeeds(galaxyNumber)).Seeds;

    /// <summary>The system the Constrictor hides in, resolved the way the original intends.</summary>
    public static StarSystem ConstrictorTarget(SystemSeeds galaxySeeds)
    {
        StarSystem[] galaxy = Universe.Galaxy.GenerateGalaxy(galaxySeeds);
        foreach (StarSystem system in galaxy)
        {
            if (system.X == ConstrictorX && system.Y == ConstrictorY)
            {
                return system;
            }
        }

        // The original's coordinates have no system here: take the nearest, which is the closest
        // thing to the intended target until the galaxy generation is corrected
        return galaxy
            .OrderBy(s => Math.Abs(s.X - ConstrictorX) + Math.Abs(s.Y - ConstrictorY))
            .First();
    }

    /// <summary>True when the original's hard-coded target system exists in the galaxy.</summary>
    public static bool ConstrictorTargetExists(SystemSeeds galaxySeeds) =>
        Universe.Galaxy.GenerateGalaxy(galaxySeeds)
            .Any(s => s.X == ConstrictorX && s.Y == ConstrictorY);

    /// <summary>
    /// Whether the Constrictor should appear here: we are in its system, mission 1 is in progress
    /// and not yet complete, and there is not one in the bubble already.
    /// </summary>
    public bool ShouldSpawnConstrictor(StarSystem system, int galaxyNumber, int constrictorsInBubble) =>
        Mission1Active &&
        !Mission1Complete &&
        constrictorsInBubble == 0 &&
        IsConstrictorSystem(system, galaxyNumber);

    /// <summary>
    /// Whether this is the system the Constrictor hides in, looked up from a galaxy's seeds rather
    /// than a system number, for callers that already hold the seeds.
    /// </summary>
    public static bool IsConstrictorSystem(StarSystem system, SystemSeeds galaxySeeds) =>
        system.Seeds == ConstrictorTarget(galaxySeeds).Seeds;

    /// <summary>
    /// Records that we destroyed the Constrictor: the original's KILLSHP sets bit 1, and the debrief
    /// that follows clears bit 0, so a completed mission leaves just bit 1 set.
    /// </summary>
    /// <returns>The reward in tenths of a credit, or 0 if this was not the mission target.</returns>
    public int RegisterConstrictorKill(int shipType)
    {
        if (shipType != ConstrictorType || !Mission1Active || Mission1Complete)
        {
            return 0;
        }

        Mission1Active = false;
        Mission1Complete = true;
        return ConstrictorReward;
    }

    /// <summary>
    /// True once we have been offered and accepted the mission, which the original gates on being
    /// in one of the first two galaxies.
    /// </summary>
    public static bool IsMissionGalaxy(int galaxyNumber) => galaxyNumber <= 1;

    /// <summary>
    /// The chance in 256 of an extra Thargoid appearing: the original's 22% while we carry the
    /// plans, and nothing otherwise.
    /// </summary>
    public int ThargoidSpawnChance => CarryingPlans ? PlansThargoidChance : 0;

    /// <summary>
    /// Offers mission 1 if the commander is eligible. The original's DOENTRY checks two things: that
    /// the high byte of the kill tally is non-zero, which means a combat rank of Competent and so
    /// 256 kills or more, and that we are in one of the first two galaxies.
    /// </summary>
    public bool OfferMission1(Commander commander) =>
        !Mission1Active &&
        !Mission1Complete &&
        commander.Kills >= CompetentKills &&
        commander.GalaxyNumber <= 1;

    /// <summary>The kill tally that makes a commander Competent, which is what mission 1 needs.</summary>
    public const int CompetentKills = 256;

    /// <summary>Accepts mission 1.</summary>
    public void AcceptMission1() => Mission1Active = true;

    /// <summary>Offers mission 2, which the original gives once mission 1 is done.</summary>
    public bool OfferMission2() => Mission1Complete && !Mission2Active && !CarryingPlans;

    /// <summary>Accepts mission 2, which sets bit 2: on our way to collect the plans.</summary>
    public void AcceptMission2() => Mission2Active = true;

    /// <summary>
    /// Picks up the plans, which the original does at a specific system: bit 3 goes on and bit 2
    /// comes off, so we are now the one the Thargoids are looking for.
    /// </summary>
    public bool PickUpPlans(StarSystem system, int galaxyNumber)
    {
        if (!Mission2Active || CarryingPlans || !IsPlansSystem(system, galaxyNumber))
        {
            return false;
        }

        Mission2Active = false;
        CarryingPlans = true;
        return true;
    }

    /// <summary>Delivers the plans, completing mission 2.</summary>
    public bool DeliverPlans(StarSystem system, int galaxyNumber)
    {
        if (!CarryingPlans || !IsDeliverySystem(system, galaxyNumber))
        {
            return false;
        }

        CarryingPlans = false;
        return true;
    }

    /// <summary>Where the plans are collected: the original uses the Constrictor's galaxy.</summary>
    public static bool IsPlansSystem(StarSystem system, int galaxyNumber) =>
        IsConstrictorSystem(system, galaxyNumber);

    /// <summary>Where the plans are delivered, back in the first galaxy.</summary>
    public static bool IsDeliverySystem(StarSystem system, int galaxyNumber) =>
        galaxyNumber == 0 && system.X == 20 && system.Y == 173; // Lave
}
