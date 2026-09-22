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
    /// <summary>How close we must be for docking or a collision to be considered.</summary>
    public const int ContactRange = 256;

    /// <summary>How close we must be to actually dock, which is inside the slot itself.</summary>
    public const int SlotRange = 120;

    /// <summary>The cosine of 26 degrees: the station's slot must face us this squarely.</summary>
    public const double SlotFacingCosine = 0.8988;

    /// <summary>The cosine of 22 degrees: the safe cone of approach to the slot.</summary>
    public const double ApproachCosine = 0.9272;

    /// <summary>The cosine of 36.6 degrees: how level the slot must be.</summary>
    public const double LevelCosine = 0.8028;

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
        double distance = Math.Sqrt(((double)sx * sx) + ((double)sy * sy) + ((double)sz * sz));
        if (distance > ContactRange)
        {
            return DockingResult.TooFar;
        }

        // 1. A hostile station will not let us in
        if (stationHostile)
        {
            return DockingResult.Collision;
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
