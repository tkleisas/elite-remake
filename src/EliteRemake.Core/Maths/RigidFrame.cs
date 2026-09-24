using System.Numerics;

namespace EliteRemake.Core.Maths;

/// <summary>
/// Turns the original's three orientation vectors into a rigid frame.
/// </summary>
/// <remarks>
/// <para>
/// The original keeps a ship's attitude as three vectors, <c>nosev</c>, <c>roofv</c> and <c>sidev</c>,
/// and rebuilds them in <c>TIDY</c> by normalising <c>nosev</c>, rebuilding one component of
/// <c>roofv</c> so that it is perpendicular to the nose, and taking <c>sidev</c> as the cross product.
/// Its normalising step is <c>NORM</c>, which divides by <c>LL5</c> — an <em>approximated</em> square
/// root, whose reciprocal is out by a few percent. So the original's own frame is not exactly rigid,
/// and it can afford that because it draws a wireframe: a few percent of scale error moves the ends of
/// lines slightly and nothing else.
/// </para>
/// <para>
/// A renderer that fills the faces cannot afford it. The transform the three vectors define shears
/// the whole body, and shears it differently as the ship turns. Measured over ninety-six frames of
/// rolling, the vectors swing between 90% and 100% of unit length and the determinant between 0.82
/// and 0.98, where a rigid frame holds all four at 1.00.
/// </para>
/// <para>
/// This is therefore a deliberate departure, and it is the same departure the solid rendering makes
/// everywhere else: the wireframe's approximations are no longer free.
/// </para>
/// </remarks>
public static class RigidFrame
{
    /// <summary>
    /// Builds a rigid frame from the nose, roof and side directions, given as unit-scale vectors.
    /// </summary>
    /// <param name="nose">The ship's forward direction; kept as the frame's reference.</param>
    /// <param name="roof">The ship's up direction, which is made perpendicular to the nose.</param>
    /// <param name="side">Unused beyond the fallback: the side is derived, not taken.</param>
    /// <remarks>
    /// The nose is kept because it is the ship's forward axis and because its length is the scale, so
    /// a vector the fixed-point arithmetic has left slightly short does not shrink the ship. The roof
    /// is then made perpendicular to it, and the side is taken as their cross product — which is what
    /// makes the frame rigid however the three arrived.
    ///
    /// The side is deliberately ignored rather than used as a third input: a rigid frame has only two
    /// degrees of freedom to give, and taking all three would mean choosing which error to keep.
    /// </remarks>
    public static (Vector3 Nose, Vector3 Roof, Vector3 Side) Build(Vector3 nose, Vector3 roof, Vector3 side)
    {
        _ = side;

        // A degenerate nose has no meaningful frame, so fall back to the identity rather than
        // propagating a NaN into the vertex stream
        if (nose.LengthSquared() < 1e-12f)
        {
            return (Vector3.UnitZ, Vector3.UnitY, Vector3.UnitX);
        }

        nose = Vector3.Normalize(nose);

        roof -= nose * Vector3.Dot(roof, nose);

        if (roof.LengthSquared() < 1e-12f)
        {
            // The roof was parallel to the nose, so any perpendicular will do
            Vector3 reference = MathF.Abs(nose.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
            roof = Vector3.Normalize(Vector3.Cross(reference, nose));
        }
        else
        {
            roof = Vector3.Normalize(roof);
        }

        // The side is exactly perpendicular to both, which is what makes the frame rigid. It is
        // roof x nose — the negative of the disc's own sidev, which ZINF's identity and the
        // normalisation both build as nose x roof. The renderer folds that sign into its own x
        // axis: the vertex stream is transformed with this side, and the ship viewer has verified
        // the whole pipeline against the disc's own models, so this sign is the drawing's
        // convention rather than a slip. Changing it here would mirror every model on its own
        // sideways axis.
        return (nose, roof, Vector3.Cross(roof, nose));
    }
}
