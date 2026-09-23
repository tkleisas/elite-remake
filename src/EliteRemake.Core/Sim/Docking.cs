using EliteRemake.Core.Maths;
using System.Numerics;

namespace EliteRemake.Core.Sim;

/// <summary>How an approach to the space station turned out.</summary>
public enum DockingResult
{
    /// <summary>Too far away to matter.</summary>
    TooFar,

    /// <summary>We are in the slot's approach cone and may dock.</summary>
    Docking,

    /// <summary>We are close enough to hit the station, but not through the slot.</summary>
    Collision,

    /// <summary>
    /// The station has been annoyed and will not let us in.
    /// </summary>
    /// <remarks>
    /// This is its own result rather than a collision, because the two are not the same event: a
    /// collision is fatal, and a refusal is not. They shared a value until now, which made shooting an
    /// innocent at the station fatal on the approach — the check fired, and the caller read it as
    /// having flown into the hull.
    /// </remarks>
    Hostile,
}

/// <summary>
/// The docking checks, which the original's main flight loop runs whenever we are close to the
/// space station.
/// </summary>
/// <remarks>
/// The original's ISDK routine applies five tests before it lets us dock, and the disc version shows
/// the launch tunnel and resets the shields and energy banks when they all pass:
///
/// <list type="number">
/// <item>the station must not be hostile</item>
/// <item>the station's slot must be facing us, within about 26 degrees</item>
/// <item>we must be facing the station rather than away from it</item>
/// <item>we must be inside the safe cone of approach to the slot, about 22 degrees</item>
/// <item>and the slot must be roughly horizontal</item>
/// </list>
///
/// Anything else, close enough to touch, is a collision: the original's part 7 only considers ships
/// within 256 units on every axis, so that is the range used here.
/// </remarks>
public static class Docking
{
    /// <summary>
    /// How close the station must be, in units on any one axis, before docking or a collision is
    /// considered at all.
    /// </summary>
    /// <remarks>
    /// The original's part 7 skips any ship whose high byte is non-zero — "JSR MAS4 / BNE MA65", the
    /// OR of x_hi, y_hi and z_hi — and then skips it a second time if bit 7 of the OR of the three
    /// low bytes is set, which it is once any axis reaches 128. Both tests have to pass, so the
    /// docking checks only ever run for a station inside 128 units on every axis, which is to say
    /// inside its slot.
    ///
    /// This used to be a distance of 256, and that is exactly far enough out to include the station
    /// the original parks 256 units behind us when we launch: our check that the station is in front
    /// of us then read the launch itself as a crash into the station we had just left.
    /// </remarks>
    public const int ContactRange = 128;

    /// <summary>How close we must be to actually dock, which is inside the slot itself.</summary>
    public const int SlotRange = 120;

    /// <summary>The cosine of 26 degrees: the station's slot must face us this squarely.</summary>
    public const double SlotFacingCosine = 0.8988;

    /// <summary>
    /// The safe cone of approach to the slot.
    /// </summary>
    /// <remarks>
    /// The original tests the z-axis of the normalized vector to the station against a constant, and
    /// the constant is version-specific: <c>CMP #89</c> for a 22.0 degree cone in every other version
    /// and <c>CMP #86</c> for a 26.3 degree cone on this one. The disc's cone is therefore the wider
    /// of the two, and ours was the narrower — 0.9272, which is the 89. The source's own comment
    /// gives the two angles, and 86/128 is what reproduces the disc's.
    /// </remarks>
    public const double ApproachCosine = 86.0 / 128.0;

    /// <summary>
    /// How level the slot must be: the original tests <c>|roofv_x_hi| >= 80</c>, so the threshold is
    /// 80 in the same 8-bit unit the orientation vectors use, which is 128 for a unit vector.
    /// </summary>
    public const double LevelCosine = 80.0 / 128.0;

    /// <summary>
    /// Works out whether we are docking or crashing. The station's orientation is what defines the
    /// slot: its nose vector points out of the slot, and its roof vector tells us how level the slot
    /// is.
    /// </summary>
    /// <param name="station">The space station.</param>
    /// <param name="stationPosition">The station's position in our frame, so it is where the
    /// station is relative to us, not the other way round.</param>
    /// <param name="playerNose">The direction we are facing, as a unit vector.</param>
    /// <param name="stationHostile">True if the station has been annoyed, which forbids docking.</param>
    public static DockingResult Check(
        Ship station,
        (int X, int Y, int Z) stationPosition,
        Vector3 playerNose,
        bool stationHostile)
    {
        (int sx, int sy, int sz) = stationPosition;

        // Per axis, as the original tests it, and not by distance: a station 100 units away on all
        // three axes is 173 units away but is still close enough for the checks to run
        if (Math.Abs(sx) >= ContactRange || Math.Abs(sy) >= ContactRange || Math.Abs(sz) >= ContactRange)
        {
            return DockingResult.TooFar;
        }

        double distance = Math.Sqrt(((double)sx * sx) + ((double)sy * sy) + ((double)sz * sz));

        // 1. A hostile station will not let us in
        if (stationHostile)
        {
            return DockingResult.Hostile;
        }

        Vector3 slot = Unit(station.Orientation, Orientation.Nosev);
        Vector3 roof = Unit(station.Orientation, Orientation.Roofv);

        // The directions that matter: where the station is from us, and where we are from it. The
        // station's nose vector points out of its slot, so the slot faces us when that nose points
        // along the vector from the station to us.
        Vector3 toStation = new Vector3(sx, sy, sz) / (float)Math.Max(1.0, distance);
        Vector3 toUs = -toStation;

        // 2. The slot must be facing us
        if (Vector3.Dot(slot, toUs) < SlotFacingCosine)
        {
            return DockingResult.Collision;
        }

        // 3. We must be facing the station, not away from it
        if (Vector3.Dot(playerNose, toStation) < 0)
        {
            return DockingResult.Collision;
        }

        // 4. We must be inside the safe cone of approach: our position, seen from the station, has
        // to be nearly straight out along the slot's axis
        float approach = Vector3.Dot(toUs, slot);
        if (approach < ApproachCosine)
        {
            return DockingResult.Collision;
        }

        // 5. The slot must be roughly level with us
        var level = new Vector3(roof.X, 0, roof.Z);
        if (level.LengthSquared() > 0 && Math.Abs(Vector3.Normalize(level).Y) > LevelCosine)
        {
            return DockingResult.Collision;
        }

        // We are lined up with the slot and inside its cone, so this is a docking
        return DockingResult.Docking;
    }

    /// <summary>Reads one of a ship's orientation vectors as a unit vector.</summary>
    private static Vector3 Unit(Orientation orientation, int vector)
    {
        var value = new Vector3(
            (float)orientation.GetUnity(vector, Orientation.X),
            (float)orientation.GetUnity(vector, Orientation.Y),
            (float)orientation.GetUnity(vector, Orientation.Z));

        return value.LengthSquared() > 0 ? Vector3.Normalize(value) : new Vector3(0, 0, 1);
    }
}
