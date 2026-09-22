using EliteRemake.Core.Graphics;
using EliteRemake.Core.Sim;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EliteRemake.Game.Rendering;

/// <summary>
/// The dashboard, drawn in the style of the original's instrument panel.
/// </summary>
/// <remarks>
/// The original's dashboard is built from line drawings and bars in the bottom 64 rows of a 256-wide
/// screen: shields and fuel on the left, energy banks, speed and the roll and pitch indicators on
/// the right, the compass in the middle, and the missile indicators along the top left. This
/// renderer lays the same instruments out in the same order, using the layout's scale factor so it
/// works at any window size. Text labels arrive with the font renderer.
/// </remarks>
public sealed class HudRenderer
{
    private readonly List<Rectangle> _missiles = [];
    private readonly Rectangle _view;
    private readonly Rectangle _dashboard;
    private readonly float _scale;
    private readonly TextRenderer _text;

    public HudRenderer(ScreenLayout layout, TextRenderer text)
    {
        _view = layout.View;
        _dashboard = layout.Dashboard;
        _scale = MathF.Max(layout.Scale, 0.5f);
        _text = text;
    }

    /// <summary>The text scale that gives eight-by-eight characters of a readable size.</summary>
    private int TextScale => Math.Max(1, (int)MathF.Round(_scale));
    private int LineHeight => TextRenderer.CellHeight(TextScale);

    /// <summary>The colour of an indicator that is switched on.</summary>
    public Color On { get; set; } = Palette.White;

    /// <summary>The colour of an indicator that is switched off.</summary>
    public Color Off { get; set; } = new(40, 44, 52);

    /// <summary>The colour of the dashboard frame.</summary>
    public Color Frame { get; set; } = new(150, 156, 168);

    /// <summary>The colour of the crosshair in the middle of the space view.</summary>
    public Color Crosshair { get; set; } = new(210, 220, 235);

    /// <summary>How many missile indicators are lit, which the commander's loadout will drive.</summary>
    public int MissilesArmed { get; set; } = 3;

    /// <summary>True when a missile is locked onto a target, which lights the leftmost indicator.</summary>
    public bool Locked { get; set; }

    /// <summary>Draws the whole dashboard and the crosshair.</summary>
    public void Draw(SpriteBatch spriteBatch, Texture2D pixel, ViewCamera camera, FlightSim sim)
    {
        spriteBatch.Begin();

        DrawCrosshair(spriteBatch, pixel);
        DrawDashboardBackground(spriteBatch, pixel);
        DrawLeftPanel(spriteBatch, pixel, sim);
        DrawRightPanel(spriteBatch, pixel, sim);
        DrawCompass(spriteBatch, pixel);

        // The original flashes "ENERGY LOW" when the banks drop below 50
        if (sim.Player.Energy < 50)
        {
            _text.DrawCentred(
                spriteBatch,
                "ENERGY LOW",
                _view.Width / 2,
                (int)(_view.Height * 0.62f),
                Math.Max(2, TextScale),
                Palette.Red);
        }

        spriteBatch.End();
    }

    /// <summary>Draws the fixed gunsight at the centre of the space view.</summary>
    private void DrawCrosshair(SpriteBatch spriteBatch, Texture2D pixel)
    {
        float cx = _view.Width / 2f;
        float cy = _view.Height / 2f;
        float arm = 9 * _scale;
        float gap = 3 * _scale;
        float thickness = MathF.Max(1f, _scale);

        // A broken square, as the original's sight is drawn from four corner brackets
        for (int corner = 0; corner < 4; corner++)
        {
            float sx = (corner is 0 or 3) ? -1 : 1;
            float sy = (corner is 0 or 1) ? -1 : 1;
            float x = cx + (sx * gap);
            float y = cy + (sy * gap);

            DrawRect(spriteBatch, pixel, x, y, sx * arm, thickness, Crosshair);
            DrawRect(spriteBatch, pixel, x, y, thickness, sy * arm, Crosshair);
        }
    }

    private void DrawDashboardBackground(SpriteBatch spriteBatch, Texture2D pixel)
    {
        // A dark panel with a lit top edge, so the view above stays the focus
        spriteBatch.Draw(pixel, _dashboard, new Color(8, 10, 14));
        DrawRect(spriteBatch, pixel, _dashboard.Left, _dashboard.Top, _dashboard.Width, MathF.Max(1f, _scale), Frame);

        // The left and right instrument banks are separated by a vertical divider, as the original
        // separates the two halves of the panel
        float centreX = _dashboard.Center.X;
        float half = 3 * _scale;
        DrawRect(spriteBatch, pixel, centreX - (half / 2), _dashboard.Top + (4 * _scale), half, _dashboard.Height - (8 * _scale), new Color(22, 26, 34));
    }

    /// <summary>Shields, fuel and the missile indicators.</summary>
    private void DrawLeftPanel(SpriteBatch spriteBatch, Texture2D pixel, FlightSim sim)
    {
        // Leave room on the left for the two-letter instrument labels
        float left = _dashboard.Left + (_dashboard.Width * 0.075f);
        float top = _dashboard.Top + (_dashboard.Height * 0.12f);
        float width = MathF.Min(_dashboard.Width * 0.32f, 200 * _scale);
        float height = MathF.Max(4f, _dashboard.Height * 0.075f);
        float spacing = _dashboard.Height * 0.135f;

        // Fore and aft shields, then fuel and the two temperatures, as on the original panel
        DrawLabelledBar(spriteBatch, pixel, left, top, width, height, sim.Player.ForeShield / 255f, Palette.Cyan, "FS");
        DrawLabelledBar(spriteBatch, pixel, left, top + spacing, width, height, sim.Player.AftShield / 255f, Palette.Cyan, "AS");
        DrawLabelledBar(spriteBatch, pixel, left, top + (spacing * 2), width, height, 0.7f, Palette.Yellow, "FU");
        DrawLabelledBar(spriteBatch, pixel, left, top + (spacing * 3), width, height, 0.25f, Palette.Red, "CT");
        DrawLabelledBar(spriteBatch, pixel, left, top + (spacing * 4), width, height, sim.LaserTemperature / 255f, Palette.Red, "LT");

        // Missile indicators: four boxes that fill in as missiles are armed
        _missiles.Clear();
        float missileSize = MathF.Max(6f, _dashboard.Height * 0.13f);
        float missileY = _dashboard.Bottom - (missileSize * 1.5f);
        for (int i = 0; i < 4; i++)
        {
            var box = new Rectangle(
                (int)(left + (i * (missileSize + (missileSize * 0.25f)))),
                (int)missileY,
                (int)missileSize,
                (int)missileSize);
            _missiles.Add(box);
        }

        for (int i = 0; i < _missiles.Count; i++)
        {
            bool armed = i < MissilesArmed;

            // The leftmost indicator shows missile lock, as the original's does
            bool locked = i == 0 && Locked;
            spriteBatch.Draw(pixel, _missiles[i], locked ? Palette.Red : armed ? Palette.Yellow : Off);
            DrawOutline(spriteBatch, pixel, _missiles[i], Frame);
        }

        _text.Draw(
            spriteBatch,
            Locked ? "LOCKED" : "MISSILES",
            _missiles[^1].Right + (int)(6 * _scale),
            _missiles[0].Top,
            TextScale,
            Frame);
    }

    /// <summary>Energy banks, speed and the roll and pitch indicators.</summary>
    private void DrawRightPanel(SpriteBatch spriteBatch, Texture2D pixel, FlightSim sim)
    {
        float right = _dashboard.Right - (_dashboard.Width * 0.05f);
        float top = _dashboard.Top + (_dashboard.Height * 0.12f);
        float width = MathF.Min(_dashboard.Width * 0.32f, 200 * _scale);
        float height = MathF.Max(4f, _dashboard.Height * 0.07f);
        float spacing = _dashboard.Height * 0.105f;
        float left = right - width;

        // Four energy banks, drawn from the right, filled to the commander's current energy
        for (int i = 0; i < 4; i++)
        {
            float level = Math.Clamp((sim.Player.Energy / 4f - i), 0f, 1f);
            DrawLabelledBar(spriteBatch, pixel, left, top + (i * spacing), width, height, level, Palette.Green, "EN");
        }

        // Speed: a bar that fills as we accelerate, plus the roll and pitch indicators below it,
        // which is how the original's RL and DC dials read
        float speedY = top + (spacing * 5.2f);
        DrawLabelledBar(spriteBatch, pixel, left, speedY, width, height, sim.Speed / (float)FlightSim.MaxSpeed, Palette.White, "SP");
        DrawCentreBar(spriteBatch, pixel, left, speedY + spacing, width, height, sim.RollRate, "RL");
        DrawCentreBar(spriteBatch, pixel, left, speedY + (spacing * 2), width, height, sim.PitchRate, "DC");

        // The speed figure itself, as the original prints it beside the dial
        string speed = sim.Speed.ToString();
        _text.Draw(spriteBatch, speed, (int)(right + (6 * _scale)), (int)speedY, TextScale, Palette.White);
    }

    /// <summary>The compass: a circle with a dot showing where the space station is.</summary>
    private void DrawCompass(SpriteBatch spriteBatch, Texture2D pixel)
    {
        float radius = MathF.Min(_dashboard.Height * 0.28f, 26 * _scale);
        float cx = _dashboard.Center.X;
        float cy = _dashboard.Center.Y;

        DrawCircle(spriteBatch, pixel, cx, cy, radius, Frame, 32);
        DrawCircle(spriteBatch, pixel, cx, cy, radius * 0.55f, new Color(60, 66, 78), 24);
    }

    /// <summary>Draws a labelled bar, with the label to its left as the original does.</summary>
    private void DrawLabelledBar(
        SpriteBatch spriteBatch,
        Texture2D pixel,
        float x,
        float y,
        float width,
        float height,
        float fraction,
        Color colour,
        string label)
    {
        int labelWidth = TextRenderer.Measure(label, TextScale).X;
        _text.Draw(spriteBatch, label, (int)(x - labelWidth - (4 * _scale)), (int)y, TextScale, Frame);
        DrawBar(spriteBatch, pixel, x, y, width, height, fraction, colour);
    }

    /// <summary>Draws a horizontal bar that fills from the left.</summary>
    private void DrawBar(
        SpriteBatch spriteBatch,
        Texture2D pixel,
        float x,
        float y,
        float width,
        float height,
        float fraction,
        Color colour)
    {
        DrawOutline(spriteBatch, pixel, new Rectangle((int)x, (int)y, (int)width, (int)height), Frame);
        float filled = Math.Clamp(fraction, 0f, 1f) * (width - 2);
        if (filled > 0)
        {
            spriteBatch.Draw(pixel, new Rectangle((int)(x + 1), (int)(y + 1), (int)filled, Math.Max(1, (int)(height - 2))), colour);
        }
    }

    /// <summary>Draws a bar with a centre mark and a pointer, as the roll and pitch dials use.</summary>
    private void DrawCentreBar(
        SpriteBatch spriteBatch,
        Texture2D pixel,
        float x,
        float y,
        float width,
        float height,
        byte rate,
        string label)
    {
        int labelWidth = TextRenderer.Measure(label, TextScale).X;
        _text.Draw(spriteBatch, label, (int)(x - labelWidth - (4 * _scale)), (int)y, TextScale, Frame);
        DrawOutline(spriteBatch, pixel, new Rectangle((int)x, (int)y, (int)width, (int)height), Frame);
        Color colour = Palette.Cyan;

        float centre = x + (width / 2);
        DrawRect(spriteBatch, pixel, centre, y, MathF.Max(1, _scale), height, new Color(80, 86, 98));

        // The rate runs from 0 to 255 with 128 in the middle
        float offset = (rate - 128) / 128f;
        float pointerX = centre + (offset * (width / 2) * 0.9f);
        DrawRect(spriteBatch, pixel, pointerX - (_scale), y, MathF.Max(1, 2 * _scale), height, colour);
    }

    private void DrawOutline(SpriteBatch spriteBatch, Texture2D pixel, Rectangle rectangle, Color colour)
    {
        float thickness = MathF.Max(1f, _scale * 0.75f);
        DrawRect(spriteBatch, pixel, rectangle.Left, rectangle.Top, rectangle.Width, thickness, colour);
        DrawRect(spriteBatch, pixel, rectangle.Left, rectangle.Bottom - thickness, rectangle.Width, thickness, colour);
        DrawRect(spriteBatch, pixel, rectangle.Left, rectangle.Top, thickness, rectangle.Height, colour);
        DrawRect(spriteBatch, pixel, rectangle.Right - thickness, rectangle.Top, thickness, rectangle.Height, colour);
    }

    private static void DrawRect(SpriteBatch spriteBatch, Texture2D pixel, float x, float y, float width, float height, Color colour)
    {
        if (width < 0)
        {
            x += width;
            width = -width;
        }

        if (height < 0)
        {
            y += height;
            height = -height;
        }

        spriteBatch.Draw(
            pixel,
            new Rectangle((int)MathF.Round(x), (int)MathF.Round(y), Math.Max(1, (int)MathF.Round(width)), Math.Max(1, (int)MathF.Round(height))),
            colour);
    }

    private static void DrawCircle(SpriteBatch spriteBatch, Texture2D pixel, float cx, float cy, float radius, Color colour, int segments)
    {
        for (int i = 0; i < segments; i++)
        {
            double a0 = i * 2 * Math.PI / segments;
            double a1 = (i + 1) * 2 * Math.PI / segments;
            float x0 = cx + (radius * (float)Math.Cos(a0));
            float y0 = cy + (radius * (float)Math.Sin(a0));
            float x1 = cx + (radius * (float)Math.Cos(a1));
            float y1 = cy + (radius * (float)Math.Sin(a1));

            float dx = x1 - x0;
            float dy = y1 - y0;
            float length = MathF.Sqrt((dx * dx) + (dy * dy));
            if (length < 0.5f)
            {
                continue;
            }

            spriteBatch.Draw(
                pixel,
                new Vector2(x0, y0),
                null,
                colour,
                MathF.Atan2(dy, dx),
                Vector2.Zero,
                new Vector2(length, 1f),
                SpriteEffects.None,
                0);
        }
    }
}
