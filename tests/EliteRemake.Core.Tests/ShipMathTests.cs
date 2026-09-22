using EliteRemake.Core.Maths;
using Xunit;

namespace EliteRemake.Core.Tests;

/// <summary>
/// Checks the ported ship maths: rotation, tidying and movement. These are the routines that give
/// Elite its characteristic flight feel, so they are checked against their documented behaviour
/// rather than just for internal consistency.
/// </summary>
public class ShipMathTests
{
    private static Orientation Identity()
    {
        // The original's "no rotation" orientation: nosev along +z, roofv along +y, sidev = -x,
        // with the high bytes normalised so that each vector has length 96.
        var orientation = new Orientation();
        orientation.SetUnity(Orientation.Nosev, Orientation.Z, 1.0);
        orientation.SetUnity(Orientation.Roofv, Orientation.Y, 1.0);
        orientation.SetUnity(Orientation.Sidev, Orientation.X, -1.0);
        return orientation;
    }

    private static double Length(Orientation orientation, int vector)
    {
        double x = orientation.GetValue(vector, Orientation.X);
        double y = orientation.GetValue(vector, Orientation.Y);
        double z = orientation.GetValue(vector, Orientation.Z);
        return Math.Sqrt((x * x) + (y * y) + (z * z));
    }

    [Fact]
    public void Mvs4_RollDoesNotMoveTheNoseVector()
    {
        var orientation = Identity();
        ShipMath.Mvs4(orientation, Orientation.Nosev, alpha: 8, beta: 0);

        Assert.Equal(0, orientation.GetValue(Orientation.Nosev, Orientation.X));
        Assert.Equal(0, orientation.GetValue(Orientation.Nosev, Orientation.Y));
        Assert.Equal(Orientation.UnityValue, orientation.GetValue(Orientation.Nosev, Orientation.Z));
    }

    [Fact]
    public void Mvs4_RotatesByTheSmallAngleApproximation()
    {
        // Rolling by alpha moves roofv's x component by alpha * roofv_y_hi, which is a rotation of
        // about alpha / 256 radians, as the value of unity is 96 * 256
        const byte alpha = 8;
        var orientation = Identity();
        ShipMath.Mvs4(orientation, Orientation.Roofv, alpha, beta: 0);

        double expectedAngle = alpha / 256.0;
        double actualAngle = Math.Atan2(
            orientation.GetValue(Orientation.Roofv, Orientation.X),
            orientation.GetValue(Orientation.Roofv, Orientation.Y));

        Assert.Equal(expectedAngle, actualAngle, 4);
    }

    [Fact]
    public void Mvs4_PitchRotatesTheNoseVectorUp()
    {
        const byte beta = 8;
        var orientation = Identity();
        ShipMath.Mvs4(orientation, Orientation.Nosev, alpha: 0, beta);

        // nosev_y = nosev_y - beta * nosev_z_hi, and nosev_z_hi is 96, so the nose drops
        Assert.Equal(-96 * beta, orientation.GetValue(Orientation.Nosev, Orientation.Y));
        Assert.Equal(0, orientation.GetValue(Orientation.Nosev, Orientation.X));
    }

    [Fact]
    public void Mvs4_KeepsVectorsRoughlyUnitLength()
    {
        var orientation = Identity();
        for (int i = 0; i < 20; i++)
        {
            ShipMath.Mvs4(orientation, Orientation.Nosev, alpha: 8, beta: 4);
            ShipMath.Mvs4(orientation, Orientation.Roofv, alpha: 8, beta: 4);
            ShipMath.Mvs4(orientation, Orientation.Sidev, alpha: 8, beta: 4);
        }

        // The Minsky circle algorithm deliberately allows a little drift; TIDY is what reins it in
        double nose = Length(orientation, Orientation.Nosev);
        Assert.InRange(nose / Orientation.UnityValue, 0.9, 1.1);
    }

    [Fact]
    public void Tidy_RestoresAnOrthonormalBasis()
    {
        var orientation = Identity();

        // Skew the orientation: stretch the vectors and make them non-orthogonal
        foreach (int vector in new[] { Orientation.Nosev, Orientation.Roofv, Orientation.Sidev })
        {
            orientation.SetValue(vector, Orientation.X, (orientation.GetValue(vector, Orientation.X) * 5 / 4) + 4000);
            orientation.SetValue(vector, Orientation.Y, orientation.GetValue(vector, Orientation.Y) * 5 / 4);
            orientation.SetValue(vector, Orientation.Z, (orientation.GetValue(vector, Orientation.Z) * 5 / 4) - 3000);
        }

        ShipMath.Tidy(orientation);

        double noseLength = Length(orientation, Orientation.Nosev) / 256.0;
        double roofLength = Length(orientation, Orientation.Roofv) / 256.0;
        double sideLength = Length(orientation, Orientation.Sidev) / 256.0;

        // Tidy normalises so that the high bytes have length 96
        Assert.InRange(noseLength, 94.0, 98.0);
        Assert.InRange(roofLength, 94.0, 98.0);
        Assert.InRange(sideLength, 80.0, 100.0); // sidev comes from a cross product of hi bytes

        AssertOrthogonal(orientation, Orientation.Nosev, Orientation.Roofv);
        AssertOrthogonal(orientation, Orientation.Roofv, Orientation.Sidev);
    }

    private static void AssertOrthogonal(Orientation orientation, int a, int b)
    {
        double dot = 0;
        foreach (int axis in new[] { Orientation.X, Orientation.Y, Orientation.Z })
        {
            dot += (double)orientation.GetValue(a, axis) * orientation.GetValue(b, axis);
        }

        double scale = Length(orientation, a) * Length(orientation, b);
        Assert.InRange(Math.Abs(dot) / scale, 0.0, 0.08);
    }

    [Fact]
    public void Tidy_LeavesSidevZLowByteAlone()
    {
        // Reproduces the original's TIL1 loop, which clears eight of the nine low bytes and
        // leaves sidev_z_lo untouched
        var orientation = Identity();
        orientation[Orientation.Sidev + 4] = 0x7F;
        orientation[Orientation.Nosev] = 0x7F;

        ShipMath.Tidy(orientation);

        Assert.Equal(0, orientation[Orientation.Nosev]);
        Assert.Equal(0x7F, orientation[Orientation.Sidev + 4]);
    }

    [Fact]
    public void Mvs5_RotatesTwoVectorsAgainstEachOther()
    {
        // MVS5 turns roofv and sidev against each other by 3.6 degrees, which a ship uses to
        // rotate about its own axes
        var orientation = Identity();
        orientation.SetValue(Orientation.Sidev, Orientation.Z, 0);

        double Angle() => Math.Atan2(
            orientation.GetValue(Orientation.Roofv, Orientation.X),
            orientation.GetValue(Orientation.Roofv, Orientation.Y)) * 180.0 / Math.PI;

        double before = Angle();

        // One step is a 3.6 degree turn: roofv_x gains sidev_x / 16, and sidev_x is -1.0
        ShipMath.Mvs5(orientation, Orientation.Roofv + Orientation.X, Orientation.Sidev + Orientation.X, rat2: 0);
        double afterOneStep = Angle();
        Assert.InRange(Math.Abs(afterOneStep - before), 3.0, 4.0);

        for (int i = 0; i < 9; i++)
        {
            ShipMath.Mvs5(orientation, Orientation.Roofv + Orientation.X, Orientation.Sidev + Orientation.X, rat2: 0);
        }

        // Ten steps of about 3.6 degrees, allowing for the small-angle approximation drifting
        double afterTenSteps = Angle();
        Assert.InRange(Math.Abs(afterTenSteps - before), 25.0, 45.0);
    }

    [Fact]
    public void Norm_NormalisesToUnity96()
    {
        byte[] vector = [96, 96, 96];
        byte length = ShipMath.Norm(vector);

        Assert.Equal(166, length); // sqrt(3 * 96^2) = 166
        foreach (byte component in vector)
        {
            Assert.InRange(component, 54, 57); // 96 / sqrt(3) = 55
        }
    }

    [Fact]
    public void Mvt1_AddsAndSubtractsDeltas()
    {
        // 5 + (-3) = 2
        byte[] coordinate = [5, 0, 0x00];
        ShipMath.Mvt1(coordinate, 0, 0x80, 3);
        Assert.Equal(2, coordinate[0]);
        Assert.Equal(0, coordinate[1]);
        Assert.Equal(0x00, coordinate[2]);

        // 2 + (-3) = -1 (exercises the negation branch)
        coordinate = [2, 0, 0x00];
        ShipMath.Mvt1(coordinate, 0, 0x80, 3);
        Assert.Equal(1, coordinate[0]);
        Assert.Equal(0, coordinate[1]);
        Assert.Equal(0x80, coordinate[2]);

        // -1 + 3 = 2
        coordinate = [1, 0, 0x80];
        ShipMath.Mvt1(coordinate, 0, 0x00, 3);
        Assert.Equal(2, coordinate[0]);
        Assert.Equal(0, coordinate[1]);
        Assert.Equal(0x00, coordinate[2]);
    }

    [Fact]
    public void Mvt1_HandlesCarriesAcrossBytes()
    {
        // 0x00FFFF + 1 = 0x010000
        byte[] coordinate = [0xFF, 0xFF, 0x00];
        ShipMath.Mvt1(coordinate, 0, 0x00, 1);
        Assert.Equal(0x00, coordinate[0]);
        Assert.Equal(0x00, coordinate[1]);
        Assert.Equal(0x01, coordinate[2]);
    }

    [Fact]
    public void Cntr_CreepsTowardsTheCentre()
    {
        // Values at or above 128 are the negative half of the slider, and creep downwards;
        // values below 128 creep upwards towards 128
        // Both halves of the slider converge on 0x80, one stepping down and one stepping up
        Assert.Equal(0x80, ShipMath.Cntr(0x81));
        Assert.Equal(0x80, ShipMath.Cntr(0x7F));
        Assert.Equal(0x80, ShipMath.Cntr(0x80));
        Assert.Equal(0xFD, ShipMath.Cntr(0xFE));
        Assert.Equal(0x01, ShipMath.Cntr(0x00));

        // Damping can be disabled, as the docking computer does
        Assert.Equal(0x81, ShipMath.Cntr(0x81, dampingDisabled: true));
    }
}
