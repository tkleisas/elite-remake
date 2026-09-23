namespace EliteRemake.Core.Sim;

/// <summary>
/// Which of the four space views we are looking through, which is the original's VIEW.
/// </summary>
/// <remarks>
/// The values are load-bearing: the original indexes its LASER array by VIEW, so a laser's mount and
/// the view it fires from are the same number, and looking out of the back is what fires the rear
/// laser. They are also the order LOOK1 is called in — 0 for the front view, 1 for the rear, 2 for
/// the left and 3 for the right.
/// </remarks>
public enum SpaceView
{
    /// <summary>Looking forward: the frame the universe is stored in.</summary>
    Front = 0,

    /// <summary>Looking back.</summary>
    Rear = 1,

    /// <summary>Looking out of the left-hand window.</summary>
    Left = 2,

    /// <summary>Looking out of the right-hand window.</summary>
    Right = 3,
}

/// <summary>
/// PLUT: the axis flipping that turns the local universe, which is always stored as if we were
/// looking forward, into the universe as seen through one of the four space views.
/// </summary>
/// <remarks>
/// <para>
/// The original does not rotate anything to change view. It flips axes: the rear view negates x and
/// z, and the side views swap x and z and then negate one of them. PLUT works on the INWK workspace a
/// ship is copied into for its turn around the flight loop, and the flipped data is never stored —
/// only the crosshair test in HITCH, which is what decides both the laser hit and the missile lock,
/// and the drawing routines ever see it. That is why this is a transform applied at the point of use
/// rather than a change to the simulation: the stored universe is the front view, always.
/// </para>
/// <para>
/// The three orientation vectors go through the same transform as the position — one rigid rotation
/// of the whole ship — so what follows is one rule applied to four vectors. For the side views the
/// original swaps the low and high bytes of the two axes and carries the sign in the high byte,
/// which is the same arithmetic by a different route.
/// </para>
/// </remarks>
public static class Plut
{
    /// <summary>Flips a position for a view.</summary>
    /// <remarks>
    /// The rear view is the original's two sign flips, and the side views are its swap plus one, so
    /// looking left turns the world's z into the view's x and the world's x into the view's -z. The
    /// transforms are proper rotations — each is the inverse of itself for the rear view, and the
    /// left and right views are each other's inverse — so the dust and the ships stay in the same
    /// relationship to each other whichever window we look through.
    /// </remarks>
    public static (int X, int Y, int Z) Position(SpaceView view, int x, int y, int z) => view switch
    {
        SpaceView.Rear => (-x, y, -z),
        SpaceView.Left => (z, y, -x),
        SpaceView.Right => (-z, y, x),
        _ => (x, y, z),
    };

    /// <summary>Flips a direction for a view, by the same rule as a position.</summary>
    public static System.Numerics.Vector3 Direction(SpaceView view, System.Numerics.Vector3 v) => view switch
    {
        SpaceView.Rear => new System.Numerics.Vector3(-v.X, v.Y, -v.Z),
        SpaceView.Left => new System.Numerics.Vector3(v.Z, v.Y, -v.X),
        SpaceView.Right => new System.Numerics.Vector3(-v.Z, v.Y, v.X),
        _ => v,
    };

    /// <summary>
    /// The laser mount a view fires from, which is the view itself: the original's LASER array is
    /// indexed by VIEW, so the laser fitted to a view is the one that fires while we are looking
    /// through it.
    /// </summary>
    public static LaserMount Mount(SpaceView view) => (LaserMount)(int)view;

    /// <summary>
    /// The direction a stationary object appears to move in a view, as we fly forward at one unit an
    /// iteration: the reverse of our own velocity, seen through that window.
    /// </summary>
    /// <remarks>
    /// The stardust streams along this axis, and it is the reverse of our velocity because the dust
    /// is standing still and we are the ones moving: dust streams towards us in the front view,
    /// away from us in the rear view, and sideways past the side windows.
    /// </remarks>
    public static (int X, int Y, int Z) StreamDirection(SpaceView view) => view switch
    {
        SpaceView.Rear => (0, 0, 1),
        SpaceView.Left => (-1, 0, 0),
        SpaceView.Right => (1, 0, 0),
        _ => (0, 0, -1),
    };
}
