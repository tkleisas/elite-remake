using System.Numerics;
using EliteRemake.Core.Maths;
using Xunit;

namespace EliteRemake.Core.Tests;

/// <summary>
/// The frame the renderer builds from a ship's orientation vectors has to be rigid.
/// </summary>
/// <remarks>
/// The original's TIDY normalises with an approximated square root, so its own frame is only nearly
/// rigid — which a wireframe can afford. Filling the faces turns that same frame into a shear of the
/// whole body, and one that changes as the ship turns, so the renderer puts the vectors through
/// <see cref="RigidFrame"/> first. These tests measure the thing that was wrong rather than assuming
/// a rotation: the lengths, the angles between the vectors, and the determinant, over a long roll.
/// </remarks>
public class RigidFrameTests
{
    /// <summary>Reads an orientation's three vectors as the renderer does.</summary>
    private static (Vector3 Nose, Vector3 Roof, Vector3 Side) Vectors(Orientation o)
    {
        Vector3 V(int v) => new(
            (float)o.GetUnity(v, Orientation.X),
            (float)o.GetUnity(v, Orientation.Y),
            (float)o.GetUnity(v, Orientation.Z));

        return (V(Orientation.Nosev), V(Orientation.Roofv), V(Orientation.Sidev));
    }

    /// <summary>
    /// A frame that has been through a long roll is rigid: unit lengths, right angles, determinant 1.
    /// </summary>
    /// <remarks>
    /// This is the test that was missing. Before the frame was rebuilt, the same measurement gave
    /// lengths swinging between 0.90 and 1.00 and a determinant between 0.82 and 0.98 — a basis that
    /// is not a rotation, and therefore a transform that shears.
    /// </remarks>
    [Fact]
    public void AFrameSurvivesALongRoll()
    {
        const int Unit = 96 * 256;
        var data = new byte[20];
        var o = new Orientation(data, 0);

        // As a ship spawns: nose +z, roof +y, side +x
        o.SetValue(Orientation.Nosev, Orientation.Z, Unit);
        o.SetValue(Orientation.Roofv, Orientation.Y, Unit);
        o.SetValue(Orientation.Sidev, Orientation.X, Unit);

        for (int frame = 1; frame <= 480; frame++)
        {
            ShipMath.Mvs5(o, Orientation.Nosev, 0x00, 0x10);

            if ((frame & 15) != 0)
            {
                continue;
            }

            ShipMath.Tidy(o);
            (Vector3 rawNose, Vector3 rawRoof, Vector3 rawSide) = Vectors(o);
            (Vector3 nose, Vector3 roof, Vector3 side) = RigidFrame.Build(rawNose, rawRoof, rawSide);

            // Every vector is unit length
            Assert.Equal(1.0f, nose.Length(), 0.0001f);
            Assert.Equal(1.0f, roof.Length(), 0.0001f);
            Assert.Equal(1.0f, side.Length(), 0.0001f);

            // Every pair is at a right angle
            Assert.Equal(0.0f, Vector3.Dot(nose, roof), 0.0001f);
            Assert.Equal(0.0f, Vector3.Dot(nose, side), 0.0001f);
            Assert.Equal(0.0f, Vector3.Dot(roof, side), 0.0001f);

            // And the determinant is that of a rotation
            float det = Vector3.Dot(side, Vector3.Cross(nose, roof));
            Assert.Equal(1.0f, MathF.Abs(det), 0.0001f);
        }
    }

    /// <summary>
    /// The nose is kept, so the frame does not quietly turn the ship.
    /// </summary>
    /// <remarks>
    /// The nose is the ship's forward axis and the frame's scale, so a rebuild that moved it would
    /// point every ship somewhere slightly different from where the simulation says it is pointing.
    /// </remarks>
    [Fact]
    public void TheNoseIsKept()
    {
        var nose = Vector3.Normalize(new Vector3(0.3f, -0.5f, 0.81f));
        var roof = Vector3.Normalize(new Vector3(0.1f, 0.9f, 0.4f));
        var side = Vector3.Normalize(new Vector3(0.9f, 0.1f, 0.2f));

        (Vector3 got, _, _) = RigidFrame.Build(nose, roof, side);

        Assert.Equal(nose.X, got.X, 0.0001f);
        Assert.Equal(nose.Y, got.Y, 0.0001f);
        Assert.Equal(nose.Z, got.Z, 0.0001f);
    }

    /// <summary>
    /// A degenerate orientation gives a usable frame rather than a NaN.
    /// </summary>
    /// <remarks>
    /// A ship whose vectors have been zeroed — mid-destruction, or a slot the game has just cleared —
    /// must not put a NaN into the vertex stream, where it would poison every triangle that shares
    /// the vertex and be very hard to trace back.
    /// </remarks>
    [Fact]
    public void ADegenerateOrientationFallsBackToTheIdentity()
    {
        (Vector3 nose, Vector3 roof, Vector3 side) = RigidFrame.Build(Vector3.Zero, Vector3.Zero, Vector3.Zero);

        Assert.Equal(Vector3.UnitZ, nose);
        Assert.Equal(Vector3.UnitY, roof);
        Assert.Equal(Vector3.UnitX, side);

        // A roof parallel to the nose is likewise handled
        (Vector3 n2, Vector3 r2, Vector3 s2) = RigidFrame.Build(Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitX);
        Assert.Equal(1.0f, r2.Length(), 0.0001f);
        Assert.Equal(0.0f, Vector3.Dot(n2, r2), 0.0001f);
        Assert.Equal(1.0f, s2.Length(), 0.0001f);
        Assert.False(float.IsNaN(r2.X) || float.IsNaN(s2.X));
    }
}
