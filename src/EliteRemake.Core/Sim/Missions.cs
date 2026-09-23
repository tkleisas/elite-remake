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

    /// <summary>Whether the Constrictor is destroyed and the debrief has not been attended yet.</summary>
    public bool DebriefPending { get; set; }

    /// <summary>
    /// Records that we destroyed the Constrictor: the original's KILLSHP sets bit 1 of TP to mark
    /// mission 1 as successfully completed.
    /// </summary>
    /// <remarks>
    /// Nothing is paid here. The original's KILLSHP only sets the flag; the reward, and on the disc
    /// the 256 kill points, are paid by the DEBRIEF routine the next time we attend a debriefing at a
    /// station — which is why <see cref="Mission1Active"/> stays on until then.
    /// </remarks>
    /// <returns>True if this was the mission target and the kill counted.</returns>
    public bool RegisterConstrictorKill(int shipType)
    {
        if (shipType != ConstrictorType || !Mission1Active || Mission1Complete)
        {
            return false;
        }

        Mission1Complete = true;
        DebriefPending = true;
        return true;
    }

    /// <summary>
    /// Attends the debriefing, if one is due, paying the reward and the kill points.
    /// </summary>
    /// <remarks>
    /// The original's DEBRIEF clears bit 0 of TP to take mission 1 out of progress, adds 256 to the
    /// kill tally — on the disc the points arrive here rather than at the kill — and pays 5000.0
    /// credits through MCASH. It then prints extended token 15, the thank you message.
    /// </remarks>
    /// <returns>A message describing the debrief, or null if none was due.</returns>
    public string? AttendDebrief(Commander commander)
    {
        if (!DebriefPending)
        {
            return null;
        }

        DebriefPending = false;
        Mission1Active = false;
        commander.Kills += DebriefKillPoints;
        commander.Cash += ConstrictorReward;

        return $"Mission 1 complete: the Constrictor is destroyed. {ConstrictorReward / 10} credits " +
               $"and {DebriefKillPoints} kill points awarded.";
    }

    /// <summary>
    /// The kill points the debrief awards: the original's <c>INC TALLY+1</c>, which is 256.
    /// </summary>
    public const int DebriefKillPoints = 256;

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

    /// <summary>
    /// Offers mission 2, which the disc's DOENTRY gates on four things: mission 1 is complete and
    /// no longer in progress (bits 0-3 of TP are exactly %0010), the kill tally's high byte is 5 or
    /// more — a rank partway from Dangerous to Deadly — and we are in the third galaxy.
    /// </summary>
    /// <remarks>
    /// The original's checks, in order:
    ///
    /// <code>
    /// LDA TP / AND #%00001111 / CMP #%00000010   \ mission 1 done and out of progress
    /// LDA TALLY+1 / CMP #5 / BCC EN4             \ 5 in the high byte is 1280 kills
    /// LDA GCNT / CMP #2 / BNE EN4                \ the third galaxy, 0-based
    /// </code>
    ///
    /// The rank gate sits between Dangerous and Deadly: 1280 kills is 5 * 256, the tally the
    /// original reads in its two bytes.
    /// </remarks>
    public bool OfferMission2(Commander commander) =>
        Mission1Complete &&
        !Mission1Active &&
        !Mission2Active &&
        !CarryingPlans &&
        commander.Kills >= Mission2KillRank &&
        commander.GalaxyNumber == PlansGalaxy;

    /// <summary>
    /// The kill tally mission 2 needs: the original compares the tally's high byte against 5, which
    /// is 1280 kills — three eighths of the way from Dangerous (256) to Deadly (2560).
    /// </summary>
    public const int Mission2KillRank = 5 * 256;

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

    /// <summary>The galaxy mission 2's plans and delivery both belong to: the third.</summary>
    public const int PlansGalaxy = 2;

    /// <summary>Where the plans are collected: the original's <c>CMP #215</c> and <c>CMP #84</c>.</summary>
    public const int PlansX = 215;
    public const int PlansY = 84;

    /// <summary>Where they are delivered: the original's <c>CMP #63</c> and <c>CMP #72</c>.</summary>
    public const int DeliveryX = 63;
    public const int DeliveryY = 72;

    /// <summary>
    /// True at the system where mission 2's plans are collected.
    /// </summary>
    /// <remarks>
    /// DOENTRY checks the galaxy and then the coordinates:
    ///
    /// <code>
    /// LDA GCNT / CMP #2 / BNE EN4      \ the third galaxy, and nowhere else
    /// LDA QQ0 / CMP #215 / BNE EN4     \ and the system at (215, 84)
    /// LDA QQ1 / CMP #84 / BNE EN4
    /// </code>
    ///
    /// That is Ceerdi, at index 83 of the third galaxy. This used to reuse the Constrictor's system,
    /// which is Orarra in the *second* galaxy — so the plans could never be collected at all, because
    /// the two conditions can never hold in the same place.
    /// </remarks>
    public static bool IsPlansSystem(StarSystem system, int galaxyNumber) =>
        galaxyNumber == PlansGalaxy && system.X == PlansX && system.Y == PlansY;

    /// <summary>
    /// True at the system where the plans are delivered.
    /// </summary>
    /// <remarks>
    /// DEBRIEF2's own test, with the coordinates the disc also demands: (63, 72) in the third galaxy.
    /// That is Birera, at index 36 — the system the mission text names ("the plans we need to take to
    /// Birera", and the Thargoid intercept token that names it too). This used to be Lave, which is
    /// in the first galaxy, so the delivery could never be made either.
    /// </remarks>
    public static bool IsDeliverySystem(StarSystem system, int galaxyNumber) =>
        galaxyNumber == PlansGalaxy && system.X == DeliveryX && system.Y == DeliveryY;
}
