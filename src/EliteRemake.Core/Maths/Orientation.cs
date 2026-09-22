using System.Runtime.CompilerServices;

namespace EliteRemake.Core.Maths;

/// <summary>
/// The nine orientation vectors of a ship, stored exactly as the original does in INWK+9 to
/// INWK+26: three vectors (nosev, roofv, sidev) of three components each, with every component
/// held as two bytes, low byte first.
/// </summary>
/// <remarks>
/// The value of a component is <c>(hi &amp; 0x7F) * 256 + lo</c>, with the sign in bit 7 of the high
/// byte, and the value of unity is 96 in the high byte (24576 as a component value) — see
/// <c>title.asm</c>: "Set nosev_z hi = 96 (96 is the value of unity in the orientation vectors)".
/// A unit vector's high bytes therefore have a length of 96, and its low bytes are zero.
/// </remarks>
public sealed class Orientation
{
    /// <summary>Byte offset of nosev.</summary>
    public const int Nosev = 0;

    /// <summary>Byte offset of roofv.</summary>
    public const int Roofv = 6;

    /// <summary>Byte offset of sidev.</summary>
    public const int Sidev = 12;

    private readonly byte[] _bytes = new byte[18];

    /// <summary>Raw byte access, matching the original's byte-addressed workspace.</summary>
    public ref byte this[int index] => ref _bytes[index];

    /// <summary>A span over all 18 bytes, for the routines that take a vector window.</summary>
    public Span<byte> AsSpan() => _bytes.AsSpan();

    /// <summary>Reads a component as a sign-magnitude 16-bit value.</summary>
    public ushort GetComponent(int vector, int axis) =>
        EliteMath.Pack(this[vector + axis + 1], this[vector + axis]);

    /// <summary>Writes a component from a sign-magnitude 16-bit value.</summary>
    public void SetComponent(int vector, int axis, ushort value)
    {
        this[vector + axis] = EliteMath.Lo(value);
        this[vector + axis + 1] = EliteMath.Hi(value);
    }

    /// <summary>
    /// The component value that represents unity (a direction cosine of 1.0). Unity is 96 in the
    /// high byte, so the value is 96 * 256 = 24576: see the original's planet.asm, which sets
    /// "(A P+1 P) = (0 96 0) = 24576", and title.asm's "96 is the value of unity in the
    /// orientation vectors".
    /// </summary>
    public const int UnityValue = 96 * 256;

    /// <summary>Sets a component from a direction cosine, where 1.0 is unity.</summary>
    public void SetUnity(int vector, int axis, double directionCosine) =>
        SetValue(vector, axis, (int)Math.Round(directionCosine * UnityValue));

    /// <summary>Reads a component as a direction cosine, where 1.0 is unity.</summary>
    public double GetUnity(int vector, int axis) => (double)GetValue(vector, axis) / UnityValue;

    /// <summary>Reads a component as a signed integer value.</summary>
    public int GetValue(int vector, int axis) => EliteMath.ToSigned(GetComponent(vector, axis));

    /// <summary>Sets a component from a signed integer value.</summary>
    public void SetValue(int vector, int axis, int value) => SetComponent(vector, axis, EliteMath.FromSigned(value));

    /// <summary>The three high bytes of a vector, as the original's XX15 workspace.</summary>
    public void GetHighBytes(int vector, Span<byte> destination)
    {
        destination[0] = this[vector + 1];
        destination[1] = this[vector + 3];
        destination[2] = this[vector + 5];
    }

    /// <summary>Copies three high bytes back into a vector.</summary>
    public void SetHighBytes(int vector, ReadOnlySpan<byte> source)
    {
        this[vector + 1] = source[0];
        this[vector + 3] = source[1];
        this[vector + 5] = source[2];
    }

    /// <summary>
    /// Builds an orthonormal orientation from a heading and pitch angle in radians, using the
    /// original's convention (nosev forward along +z, roofv up along +y, sidev = nosev x roofv)
    /// and its scale, where unity is 96 in each high byte.
    /// </summary>
    public static Orientation FromHeadingPitch(double heading, double pitch)
    {
        double sinH = Math.Sin(heading);
        double cosH = Math.Cos(heading);
        double sinP = Math.Sin(pitch);
        double cosP = Math.Cos(pitch);

        var orientation = new Orientation();

        orientation.SetUnity(Nosev, X, sinH * cosP);
        orientation.SetUnity(Nosev, Y, sinP);
        orientation.SetUnity(Nosev, Z, cosH * cosP);

        orientation.SetUnity(Roofv, X, -sinH * sinP);
        orientation.SetUnity(Roofv, Y, cosP);
        orientation.SetUnity(Roofv, Z, -cosH * sinP);

        orientation.SetUnity(Sidev, X, -cosH);
        orientation.SetUnity(Sidev, Y, 0);
        orientation.SetUnity(Sidev, Z, sinH);

        return orientation;
    }

    /// <summary>Byte offset of a component's x axis within its vector.</summary>
    public const int X = 0;

    /// <summary>Byte offset of a component's y axis within its vector.</summary>
    public const int Y = 2;

    /// <summary>Byte offset of a component's z axis within its vector.</summary>
    public const int Z = 4;
}
