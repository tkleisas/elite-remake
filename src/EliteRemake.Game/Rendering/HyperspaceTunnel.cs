using EliteRemake.Core.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EliteRemake.Game.Rendering;

/// <summary>
/// The hyperspace tunnel: rings drawn one inside the next, as the original's HFS2 draws them.
/// </summary>
/// <remarks>
/// LL164 sets the ring step to 4 — "so there are more sections in the rings and they are quite
/// round", against the launch rings' 8 — and calls HFS2. HFS2 then draws **eight sets** of rings,
/// and within each set every ring is twice the radius of the one before, starting from a radius of
/// 8 to 15 depending on the set, until doubling would take the ring off-screen or its radius past
/// 160:
///
/// <code>
/// .HFL1  LDA XX4 / AND #7 / CLC / ADC #8 / STA K   \ the starting radius, 8 to 15
/// .HFL2  ... draw a circle of radius K
///        ASL K                                     \ double it
///        BCS HF8                                   \ off-screen, so stop
///        LDA K / CMP #160 / BCC HFL2               \ past 160, so stop
/// </code>
///
/// The rings are all on screen together — this is a tunnel seen down its length rather than an
/// animation of one ring — which is why they are drawn every frame for as long as the countdown
/// lasts rather than moving.
/// </remarks>
public static class HyperspaceTunnel
{
    /// <summary>The ring step size LL164 uses for hyperspace, against 8 for the launch rings.</summary>
    public const int StepSize = 4;

    /// <summary>How many sets of rings HFS2 draws in the flight bank, for hyperspace and docking.</summary>
    public const int RingSets = 8;

    /// <summary>
    /// How many sets the launch tunnel draws: the docked bank's HFS2 calls HFS1 and then falls
    /// through into it again, which is the sixteen rings the original versions draw.
    /// </summary>
    public const int LaunchRingSets = 16;

    /// <summary>The radius past which HFS2 stops drawing a set.</summary>
    public const int MaximumRadius = 160;

    /// <summary>How much of the view is covered before the rings stop widening.</summary>
    private const int ScreenWidth = 256;
    private const int ScreenHeight = 192;

    /// <summary>
    /// Draws the tunnel over the whole space view.
    /// </summary>
    /// <remarks>
    /// The original's radius is in 256x192 screen pixels, so it is scaled by the view's height —
    /// the same basis the projection uses — to fill a widescreen window without changing the shape.
    /// </remarks>
    /// <param name="spriteBatch">The batch to draw with.</param>
    /// <param name="pixel">A one-pixel texture.</param>
    /// <param name="camera">The camera the space view is drawn with.</param>
    /// <param name="colour">The colour of the rings.</param>
    /// <param name="ringSets">
    /// How many sets of rings to draw. Hyperspace and the docking tunnel use the flight bank's
    /// HFS2, which draws eight; the launch tunnel is drawn from the docked bank, where HFS2 calls
    /// HFS1 and then falls through into it, so it draws sixteen.
    /// </param>
    public static void Draw(
        SpriteBatch spriteBatch,
        Texture2D pixel,
        ViewCamera camera,
        Color colour,
        int ringSets = RingSets)
    {
        float scale = camera.ViewportHeight / ScreenHeight;
        float cx = camera.CentreX;
        float cy = camera.CentreY;

        // HFS2 clears the screen and draws a border box before the rings; the caller has already
        // cleared, and the border is drawn here so the tunnel reads as the original's screen
        DrawBorder(spriteBatch, pixel, cx, cy, scale, colour);


        for (int set = 0; set < ringSets; set++)
        {
            // The original's terminating test, in its own units: a set stops once a ring's radius
            // reaches 160, which is what its own CMP #160 does. Bounding by the window instead would
            // let the rings grow past the shape the original draws.
            for (int radius = 8 + (set & 7); radius < MaximumRadius; radius *= 2)
            {
                float r = radius * scale;
                DrawRing(spriteBatch, pixel, cx, cy, r, r, scale, colour);
            }
        }
    }

    /// <summary>Draws one ring the way CIRCLE2 draws a circle: a stepped polygon outline.</summary>
    private static void DrawRing(
        SpriteBatch spriteBatch,
        Texture2D pixel,
        float cx,
        float cy,
        float rx,
        float ry,
        float scale,
        Color colour)
    {
        // The original draws these as continuous lines. Drawn as dots they have to be spaced far
        // enough apart not to merge into a solid blob at the centre, where the rings crowd
        // together, so the spacing is a fixed number of pixels rather than a fixed count: at a
        // couple of pixels the inner rings read as rings, and the outer ones stay smooth.
        float circumference = MathF.PI * (rx + ry);
        int dot = Math.Max(1, (int)MathF.Round(scale));
        int thickness = Math.Max(1, (int)MathF.Round(scale));

        int segments = Math.Max(12, (int)(circumference / (dot * 2.2f)));

        for (int i = 0; i < segments; i++)
        {
            double a = i * Math.Tau / segments;
            float x = cx + (rx * (float)Math.Cos(a));
            float y = cy + (ry * (float)Math.Sin(a));
            spriteBatch.Draw(pixel, new Rectangle((int)x, (int)y, thickness, thickness), colour);
        }
    }

    /// <summary>The border box HFS2 draws around the screen before the rings.</summary>
    private static void DrawBorder(
        SpriteBatch spriteBatch,
        Texture2D pixel,
        float cx,
        float cy,
        float scale,
        Color colour)
    {
        _ = (cy, scale);
        int thickness = Math.Max(1, (int)MathF.Round(scale));
        int top = 0;
        int bottom = (int)(cy * 2) - thickness;

        DrawBar(spriteBatch, pixel, 0, top, (int)(cx * 2), thickness, colour);
        DrawBar(spriteBatch, pixel, 0, bottom, (int)(cx * 2), thickness, colour);
    }

    private static void DrawBar(SpriteBatch spriteBatch, Texture2D pixel, int x, int y, int width, int height, Color colour) =>
        spriteBatch.Draw(pixel, new Rectangle(x, y, width, height), colour);
}
