using System.Numerics;

namespace EliteRemake.Core.Graphics;

/// <summary>
/// The camera used for the space view, reproducing the original's projection.
/// </summary>
/// <remarks>
/// The original projects a point with:
///
/// <code>
///     screen_x = 128 + 256 * x / z
///     screen_y =  96 - 256 * y / z
/// </code>
///
/// for its 256 x 192 space view, where x is to the right, y is up and z is into the screen. The
/// focal length of 256 pixels over a 192-pixel-high view gives a vertical field of view of
/// 2 * atan(96 / 256) = 41.1 degrees, which is the field of view this camera preserves: the
/// projection is scaled to the modern viewport height, so a ship at a given distance occupies the
/// same fraction of the screen height as it does on the BBC Micro, and a widescreen window simply
/// shows more to the sides.
/// </remarks>
public sealed class ViewCamera
{
    /// <summary>The width of the original space view, in pixels.</summary>
    public const int OriginalWidth = 256;

    /// <summary>The height of the original space view, in pixels.</summary>
    public const int OriginalHeight = 192;

    /// <summary>The original's focal length, in pixels.</summary>
    public const float OriginalFocalLength = 256f;

    /// <summary>The original's vertical centre, from which the field of view is derived.</summary>
    public const float OriginalHalfHeight = 96f;

    /// <summary>The distance in front of the camera at which geometry is clipped.</summary>
    public const float NearPlane = 4f;

    public ViewCamera(float viewportWidth, float viewportHeight, float centreX, float centreY)
    {
        Resize(viewportWidth, viewportHeight, centreX, centreY);
    }

    /// <summary>Width of the viewport in pixels.</summary>
    public float ViewportWidth { get; private set; }

    /// <summary>Height of the viewport in pixels.</summary>
    public float ViewportHeight { get; private set; }

    /// <summary>Screen x coordinate of the centre of projection.</summary>
    public float CentreX { get; private set; }

    /// <summary>Screen y coordinate of the centre of projection.</summary>
    public float CentreY { get; private set; }

    /// <summary>The focal length in pixels.</summary>
    public float FocalLength { get; private set; }

    /// <summary>
    /// Resizes the camera to a new viewport, preserving the original's vertical field of view.
    /// </summary>
    public void Resize(float viewportWidth, float viewportHeight, float centreX, float centreY)
    {
        ViewportWidth = viewportWidth;
        ViewportHeight = viewportHeight;
        CentreX = centreX;
        CentreY = centreY;

        // Match the original's vertical field of view: focal = half-height / tan(half-fov)
        FocalLength = viewportHeight * OriginalFocalLength / OriginalHeight;
    }

    /// <summary>
    /// Projects a point in view space (x right, y up, z forward, relative to our ship) onto the
    /// screen plane. Returns false if the point is behind the near plane or the scale is invalid.
    /// </summary>
    public bool Project(Vector3 view, out Vector2 screen)
    {
        screen = default;
        if (view.Z <= 0)
        {
            return false;
        }

        float scale = FocalLength / view.Z;
        screen = new Vector2(CentreX + (view.X * scale), CentreY - (view.Y * scale));
        return true;
    }

    /// <summary>
    /// Projects a point without any near-plane rejection, for callers that clip geometry first.
    /// </summary>
    public Vector2 ProjectUnchecked(Vector3 view)
    {
        float scale = FocalLength / MathF.Max(view.Z, 0.0001f);
        return new Vector2(CentreX + (view.X * scale), CentreY - (view.Y * scale));
    }

    /// <summary>
    /// Clips a polygon in view space against the near plane, using Sutherland-Hodgman. The result
    /// is written to <paramref name="destination"/> and the number of vertices returned.
    /// </summary>
    public static int ClipNearPlane(ReadOnlySpan<Vector3> polygon, Span<Vector3> destination, float nearPlane = NearPlane)
    {
        int count = 0;
        int n = polygon.Length;
        if (n == 0)
        {
            return 0;
        }

        for (int i = 0; i < n; i++)
        {
            Vector3 current = polygon[i];
            Vector3 next = polygon[(i + 1) % n];
            bool currentInside = current.Z >= nearPlane;
            bool nextInside = next.Z >= nearPlane;

            if (currentInside)
            {
                destination[count++] = current;
            }

            if (currentInside != nextInside)
            {
                float t = (nearPlane - current.Z) / (next.Z - current.Z);
                destination[count++] = Vector3.Lerp(current, next, t);
            }
        }

        return count;
    }
}
