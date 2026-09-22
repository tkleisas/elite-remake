using EliteRemake.Core.Graphics;
using EliteRemake.Core.Sim;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EliteRemake.Game.Rendering;

/// <summary>
/// Draws the planet and the sun: the two bodies the original draws as circles rather than as ship
/// models.
/// </summary>
/// <remarks>
/// The original projects a circle with a radius of 24576 units divided by the body's distance, so a
/// body's apparent size depends only on how far away it is — which is why a sun you fly towards
/// fills the screen. This renderer keeps that rule exactly, and gives the discs a softly shaded edge
/// rather than the original's hard circle, since we are drawing solids rather than wireframes.
/// </remarks>
public sealed class CelestialRenderer : IDisposable
{
    private const int DiscTextureSize = 256;

    private readonly Texture2D _disc;
    private readonly Texture2D _ringedDisc;

    public CelestialRenderer(GraphicsDevice device)
    {
        _disc = CreateDisc(device, ringed: false, ringColour: Color.White);
        _ringedDisc = CreateDisc(device, ringed: true, ringColour: new Color(255, 255, 255, 90));
    }

    /// <summary>The colour of the planet, which varies from system to system.</summary>
    public Color PlanetColour { get; set; } = new(150, 140, 120);

    /// <summary>The colour of the sun's disc.</summary>
    public Color SunColour { get; set; } = new(255, 240, 190);

    /// <summary>
    /// Draws a planet or sun at its position in view space, returning false if it is behind us or
    /// too far away to be seen.
    /// </summary>
    public bool Draw(
        SpriteBatch spriteBatch,
        ViewCamera camera,
        System.Numerics.Vector3 position,
        bool isSun,
        float focus)
    {
        // The original hides the body once z_sign reaches 48 — that is the *sign* byte, the top
        // byte of the 24-bit coordinate, so the test is on z >> 16 and not on z >> 8. Dividing by
        // 256 here instead culled every planet and sun in the game, since their distances are in
        // the hundreds of thousands.
        if (position.Z <= 0 || (int)(position.Z / 65536f) >= 48)
        {
            return false;
        }

        if (!camera.Project(position, out System.Numerics.Vector2 centre))
        {
            return false;
        }

        // Screen radius = focal length * radius / distance, which is the original's projection
        float screenRadius = camera.FocalLength * SystemArrival.BodyRadius / position.Z;
        if (screenRadius < 1.5f)
        {
            return false;
        }

        float diameter = screenRadius * 2;
        var destination = new Rectangle(
            (int)(centre.X - screenRadius),
            (int)(centre.Y - screenRadius),
            (int)MathF.Max(2, diameter),
            (int)MathF.Max(2, diameter));

        Texture2D texture = isSun ? _disc : (focus > 0.5f ? _ringedDisc : _disc);
        Color colour = isSun ? SunColour : PlanetColour;
        spriteBatch.Draw(texture, destination, colour);
        return true;
    }

    /// <summary>
    /// Builds a disc texture: opaque in the middle, fading out at the rim. The "ringed" variant has
    /// a faint extra ring, which is how the original's second planet type is drawn.
    /// </summary>
    private static Texture2D CreateDisc(GraphicsDevice device, bool ringed, Color ringColour)
    {
        var pixels = new Color[DiscTextureSize * DiscTextureSize];
        float radius = DiscTextureSize / 2f;
        float centre = radius - 0.5f;

        for (int y = 0; y < DiscTextureSize; y++)
        {
            for (int x = 0; x < DiscTextureSize; x++)
            {
                float dx = x - centre;
                float dy = y - centre;
                float distance = MathF.Sqrt((dx * dx) + (dy * dy));
                float coverage = Math.Clamp(radius - distance, 0f, 1f);
                byte alpha = (byte)(coverage * 255);

                if (ringed)
                {
                    // A ring at about four fifths of the radius, as the original's ringed planet
                    float ringDistance = MathF.Abs(distance - (radius * 0.78f));
                    if (ringDistance < 1.5f)
                    {
                        alpha = (byte)Math.Max(alpha, ringColour.A);
                    }
                }

                // Premultiplied: the colour is stored multiplied by the alpha, which is what the
                // sprite batch's default blend state expects. Storing plain white with an alpha
                // leaves the corners opaque, which draws the planet as a square.
                pixels[(y * DiscTextureSize) + x] = new Color(alpha, alpha, alpha, alpha);
            }
        }

        var texture = new Texture2D(device, DiscTextureSize, DiscTextureSize);
        texture.SetData(pixels);
        return texture;
    }

    public void Dispose()
    {
        _disc.Dispose();
        _ringedDisc.Dispose();
    }
}
