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
    private readonly TextRenderer _text;

    public HudRenderer(ScreenLayout layout, TextRenderer text)
    {
        Layout = layout;
        _text = text;
    }

    /// <summary>
    /// Where the space view and the dashboard sit in the window. This is updated whenever the window
    /// changes size, so the dashboard follows the bottom of the screen rather than staying where it
    /// was first drawn.
    /// </summary>
    public ScreenLayout Layout { get; set; }

    /// <summary>The area the 3D view occupies, which the crosshair and the labels are centred on.</summary>
    private Rectangle View => Layout.View;

    /// <summary>The area the dashboard occupies, across the bottom of the window.</summary>
    private Rectangle Dashboard => Layout.Dashboard;

    /// <summary>
    /// How many screen pixels one original pixel occupies. The floor keeps the instruments from
    /// collapsing into an unreadable smear in a very small window.
    /// </summary>
    private float Scale => MathF.Max(Layout.Scale, 0.5f);

    /// <summary>The text scale that gives eight-by-eight characters of a readable size.</summary>
    private int TextScale => Math.Max(1, (int)MathF.Round(Scale));
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

    /// <summary>
    /// The commander's fuel in tenths of a light year, for the fuel bar. It is pushed in by the
    /// scene because only the commander knows it, and it is not the simulation's to hold.
    /// </summary>
    public int Fuel { get; set; } = EliteRemake.Core.Universe.Outfitting.MaxFuel;

    /// <summary>True when a missile is locked onto a target, which lights the leftmost indicator.</summary>
    public bool Locked { get; set; }

    /// <summary>
    /// The in-flight message, or empty for none. The original displays these in capitals at the
    /// bottom of the space view, erasing whatever was there before.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Draws the whole dashboard and the crosshair.</summary>
    public void Draw(SpriteBatch spriteBatch, Texture2D pixel, ViewCamera camera, FlightSim sim)
    {
        spriteBatch.Begin(samplerState: SamplerState.PointClamp);

        DrawCrosshair(spriteBatch, pixel);
        DrawDashboardBackground(spriteBatch, pixel);
        DrawLeftPanel(spriteBatch, pixel, sim);
        DrawRightPanel(spriteBatch, pixel, sim);
        DrawScanner(spriteBatch, pixel, sim);
        DrawCompass(spriteBatch, pixel, sim);

        // In-flight messages go across the foot of the space view, which is where the original's
        // MESS puts them: "display an in-flight message in capitals at the bottom of the space view,
        // erasing any existing in-flight message first"
        if (Message.Length > 0)
        {
            string line = Message.ToUpperInvariant();
            int messageScale = Math.Max(2, TextScale);
            _text.DrawCentred(
                spriteBatch,
                line,
                View.Width / 2,
                (int)(View.Height * 0.86f),
                EliteRemake.Core.Text.HintLine.Fit(
                    line.Length,
                    messageScale,
                    TextRenderer.CellWidth(messageScale),
                    (int)(View.Width * 0.95f)),
                Palette.Yellow);
        }

        // The original flashes "ENERGY LOW" when the banks are at 50 or below: LDA #50, CMP ENERGY,
        // BCC - so it is an inclusive test, not a strict one
        if (sim.Player.Energy <= 50)
        {
            _text.DrawCentred(
                spriteBatch,
                "ENERGY LOW",
                View.Width / 2,
                (int)(View.Height * 0.62f),
                Math.Max(2, TextScale),
                Palette.Red);
        }

        spriteBatch.End();
    }

    /// <summary>
    /// Draws the 3D scanner: the ellipse, the centre line, and a blip and stick for every ship the
    /// original's SCAN would show. The projection is done in the original's dashboard coordinates,
    /// so this only has to scale them into place.
    /// </summary>
    private void DrawScanner(SpriteBatch spriteBatch, Texture2D pixel, FlightSim sim)
    {
        // The scanner occupies a 128 by 54 patch of the original's 256-wide dashboard, but it has
        // to fit the gap between the two instrument panels here, so the scale is capped to whatever
        // room is left in the middle
        // The scanner has to fit between the two instrument panels without touching either, and it
        // keeps the original's shape: its ellipse is 128 by 52 in a 256-wide dashboard, a little
        // over twice as wide as it is tall
        float gap = Dashboard.Width * 0.30f;
        float scannerScale = MathF.Min(Scale, gap / (Scanner.HalfWidth * 2f));

        float width = Scanner.HalfWidth * 2 * scannerScale;
        float height = Scanner.HalfHeight * 2 * scannerScale;
        float cx = View.Width / 2f;
        // Sit the scanner in the dashboard, just above the bottom edge
        float cy = Dashboard.Top + (height * 0.5f);

        // The ellipse, drawn as a ring of points
        DrawEllipse(spriteBatch, pixel, cx, cy, width / 2, height / 2, Frame);

        // The centre line, which runs across the middle of the scanner
        spriteBatch.Draw(
            pixel,
            new Rectangle((int)(cx - (width / 2)), (int)cy, (int)width, (int)MathF.Max(1f, Scale)),
            Frame);

        // The forward view wedge. It opens upwards because forward is up the scanner, and it starts
        // at the centre of the ellipse, which is where the ship is. Each arm runs to the point where
        // it meets the ellipse rather than past it, so the wedge stays inside the scanner.
        const float wedgeDegrees = 35f;
        float wedgeRadians = wedgeDegrees * MathF.PI / 180f;
        var wedgeColour = new Color(90, 190, 200);
        float wedgeThickness = MathF.Max(1f, scannerScale * 0.6f);

        float rx = width / 2f;
        float ry = height / 2f;
        foreach (int direction in new[] { -1, 1 })
        {
            // The direction of the arm, then the distance at which it meets the ellipse
            float dx = MathF.Sin(wedgeRadians) * direction;
            float dy = -MathF.Cos(wedgeRadians);
            float reach = 1f / MathF.Sqrt(((dx / rx) * (dx / rx)) + ((dy / ry) * (dy / ry)));

            DrawLine(spriteBatch, pixel, cx, cy, cx + (dx * reach), cy + (dy * reach), wedgeThickness, wedgeColour);
        }

        foreach (Ship ship in sim.Bubble)
        {
            if (Scanner.Project(ship) is not { } blip)
            {
                continue;
            }

            // The original scales its dashboard coordinates into the scanner's patch
            float bx = cx + ((blip.X - Scanner.CentreX) * scannerScale);
            float by = cy + ((blip.Y - Scanner.CentreY) * scannerScale);
            float rowY = cy + ((blip.StickY - Scanner.CentreY) * scannerScale);
            float stickX = cx + ((blip.StickX - Scanner.CentreX) * scannerScale);

            // Skip anything that would fall outside the ellipse
            if (!Scanner.IsInside(blip.X, blip.Y))
            {
                continue;
            }

            Color colour = blip.Missile ? Palette.Yellow : new Color(120, 255, 120);

            // The stick joins the blip to the centre line of its row, showing its height
            if (Math.Abs(by - rowY) >= scannerScale)
            {
                spriteBatch.Draw(
                    pixel,
                    new Rectangle((int)stickX, (int)MathF.Min(by, rowY), (int)MathF.Max(1f, scannerScale), (int)MathF.Abs(by - rowY)),
                    colour);
            }

            int size = Math.Max(1, (int)MathF.Round(scannerScale));
            spriteBatch.Draw(pixel, new Rectangle((int)bx - (size / 2), (int)by - (size / 2), size, size), colour);
        }
    }

    /// <summary>Draws an ellipse outline, by walking round it and joining the points.</summary>
    private static void DrawEllipse(
        SpriteBatch spriteBatch,
        Texture2D pixel,
        float cx,
        float cy,
        float rx,
        float ry,
        Color colour)
    {
        // A dotted outline: evenly spaced dots around the ellipse rather than a continuous line,
        // which is the scanner style the dashboard is being drawn to match. The spacing is set so
        // the dots read as a ring without merging into one.
        float dot = MathF.Max(1f, MathF.Round(ry / 22f));
        float circumference = MathF.PI * (3 * (rx + ry) - MathF.Sqrt(((3 * rx) + ry) * (rx + (3 * ry))));
        int dots = Math.Max(24, (int)(circumference / (dot * 3.2f)));

        for (int i = 0; i < dots; i++)
        {
            double a = i * Math.Tau / dots;
            float x = cx + (rx * (float)Math.Cos(a));
            float y = cy + (ry * (float)Math.Sin(a));
            spriteBatch.Draw(
                pixel,
                new Rectangle((int)x, (int)y, (int)dot, (int)dot),
                colour);
        }
    }

    /// <summary>Draws a line of the given thickness, by filling the rectangle between its ends.</summary>
    private static void DrawLine(
        SpriteBatch spriteBatch,
        Texture2D pixel,
        float x0,
        float y0,
        float x1,
        float y1,
        float thickness,
        Color colour)
    {
        float dx = x1 - x0;
        float dy = y1 - y0;
        float length = MathF.Sqrt((dx * dx) + (dy * dy));
        if (length < 0.01f)
        {
            return;
        }

        spriteBatch.Draw(
            pixel,
            new Vector2(x0, y0),
            null,
            colour,
            MathF.Atan2(dy, dx),
            Vector2.Zero,
            new Vector2(length, thickness),
            SpriteEffects.None,
            0f);
    }

    /// <summary>Draws the fixed gunsight at the centre of the space view.</summary>
    private void DrawCrosshair(SpriteBatch spriteBatch, Texture2D pixel)
    {
        float cx = View.Width / 2f;
        float cy = View.Height / 2f;
        float arm = 9 * Scale;
        float gap = 3 * Scale;
        float thickness = MathF.Max(1f, Scale);

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
        spriteBatch.Draw(pixel, Dashboard, new Color(8, 10, 14));
        DrawRect(spriteBatch, pixel, Dashboard.Left, Dashboard.Top, Dashboard.Width, MathF.Max(1f, Scale), Frame);

        // The left and right instrument banks are separated by a vertical divider, as the original
        // separates the two halves of the panel
        float centreX = Dashboard.Center.X;
        float half = 3 * Scale;
        DrawRect(spriteBatch, pixel, centreX - (half / 2), Dashboard.Top + (4 * Scale), half, Dashboard.Height - (8 * Scale), new Color(22, 26, 34));
    }

    /// <summary>Shields, fuel and the missile indicators.</summary>
    private void DrawLeftPanel(SpriteBatch spriteBatch, Texture2D pixel, FlightSim sim)
    {
        // Leave room on the left for the two-letter instrument labels
        float left = Dashboard.Left + (Dashboard.Width * 0.075f);
        float top = Dashboard.Top + (Dashboard.Height * 0.12f);
        // Narrower than the panel could be, so the scanner has room between the two panels
        float width = MathF.Min(Dashboard.Width * 0.24f, 150 * Scale);
        float height = MathF.Max(4f, Dashboard.Height * 0.075f);
        float spacing = Dashboard.Height * 0.135f;

        // Fore and aft shields, then fuel and the two temperatures, as on the original panel
        DrawLabelledBar(spriteBatch, pixel, left, top, width, height, sim.Player.ForeShield / 255f, Palette.Cyan, "FS");
        DrawLabelledBar(spriteBatch, pixel, left, top + spacing, width, height, sim.Player.AftShield / 255f, Palette.Cyan, "AS");
        DrawLabelledBar(spriteBatch, pixel, left, top + (spacing * 2), width, height, Fuel / (float)EliteRemake.Core.Universe.Outfitting.MaxFuel, Palette.Yellow, "FU");
        DrawLabelledBar(spriteBatch, pixel, left, top + (spacing * 3), width, height, sim.CabinTemperature / 255f, Palette.Red, "CT");
        DrawLabelledBar(spriteBatch, pixel, left, top + (spacing * 4), width, height, sim.LaserTemperature / 255f, Palette.Red, "LT");

        // Missile indicators: four boxes that fill in as missiles are armed
        _missiles.Clear();
        float missileSize = MathF.Max(6f, Dashboard.Height * 0.13f);
        float missileY = Dashboard.Bottom - (missileSize * 1.5f);
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
            _missiles[^1].Right + (int)(6 * Scale),
            _missiles[0].Top,
            TextScale,
            Frame);
    }

    /// <summary>
    /// The half-width of the scanner's patch, which the instrument panels keep clear of. The
    /// scanner has to fit between the two panels, so the panels are sized from the scanner rather
    /// than from fixed fractions of the dashboard, which is what used to let them crowd it.
    /// </summary>
    private float ScannerHalfWidth => Scanner.HalfWidth * MathF.Min(Scale, Dashboard.Width * 0.30f / (Scanner.HalfWidth * 2f));

    /// <summary>The gap between a panel and the scanner's edge.</summary>
    private float PanelGap => MathF.Max(6f, 22 * Scale);

    /// <summary>Energy banks, speed and the roll and pitch indicators.</summary>
    private void DrawRightPanel(SpriteBatch spriteBatch, Texture2D pixel, FlightSim sim)
    {
        // The instrument labels sit on the right of these bars, so the panel stops short of the
        // edge of the dashboard by however wide a label is. The width comes from the font rather
        // than a fraction of the dashboard so the labels cannot be clipped at the screen edge, at
        // any window size.
        int labelWidth = TextRenderer.Measure("EN", TextScale).X;
        float right = Dashboard.Right - labelWidth - (6 * Scale);
        float top = Dashboard.Top + (Dashboard.Height * 0.12f);
        // The energy banks are drawn from the right, so the panel starts where the scanner ends
        float width = MathF.Max(40f, right - ((Dashboard.Center.X + ScannerHalfWidth) + PanelGap));
        float height = MathF.Max(4f, Dashboard.Height * 0.07f);
        float spacing = Dashboard.Height * 0.105f;
        float left = right - width;

        // Four energy banks, drawn from the right, filled to the commander's current energy
        for (int i = 0; i < 4; i++)
        {
            float level = Math.Clamp((sim.Player.Energy / 4f - i), 0f, 1f);
            DrawLabelledBar(spriteBatch, pixel, left, top + (i * spacing), width, height, level, Palette.Green, "EN", labelOnRight: true);
        }

        // Speed: a bar that fills as we accelerate, plus the roll and pitch indicators below it,
        // which is how the original's RL and DC dials read
        float speedY = top + (spacing * 5.2f);
        DrawLabelledBar(spriteBatch, pixel, left, speedY, width, height, sim.Speed / (float)FlightSim.MaxSpeed, Palette.White, "SP");
        DrawCentreBar(spriteBatch, pixel, left, speedY + spacing, width, height, sim.RollRate, "RL");
        DrawCentreBar(spriteBatch, pixel, left, speedY + (spacing * 2), width, height, sim.PitchRate, "DC");
    }

    /// <summary>The compass: a circle with a dot showing where the space station is.</summary>
    /// <summary>
    /// Draws the compass: a fixed circle with a dot inside it showing where the planet or the space
    /// station is. The original's SP2 works the dot's position out by dividing the body's
    /// x and y coordinates by ten, giving a value from -9 to +9, and placing the dot at
    /// (195 + x, 204 - y) on its dashboard — so the dot moves inside a circle a little under twenty
    /// pixels across, and there is no large ring at all.
    /// </summary>
    private void DrawCompass(SpriteBatch spriteBatch, Texture2D pixel, FlightSim sim)
    {
        // The original's compass sits to the right of the scanner, just inside its edge
        float scale = MathF.Min(Scale, Dashboard.Width * 0.02f);
        // Inside the scanner's right edge, so the compass never reaches the panel beside it
        float scannerHalfWidth = Scanner.HalfWidth * MathF.Min(Scale, Dashboard.Width * 0.30f / (Scanner.HalfWidth * 2f));
        float cx = Dashboard.Center.X + (scannerHalfWidth * 0.58f);
        float cy = Dashboard.Top + (Dashboard.Height * 0.62f);
        float radius = 10 * scale;

        DrawCircle(spriteBatch, pixel, cx, cy, radius, Frame, 24);

        // The compass shows the station while we are inside its safe zone — the original's SSPR is
        // the count of stations in our bubble, and it is what COMPAS branches on: "If we are inside
        // the space station safe zone, jump to SP1 to draw the space station on the compass" — and
        // the planet otherwise. Preferring the planet whenever it was in the bubble meant the dot
        // never guided us to the station, which is the one thing a pilot needs it for.
        Ship? body = null;
        if (sim.StationIsPresent)
        {
            foreach (Ship ship in sim.Bubble)
            {
                if (ship.Type == Combat.SpaceStationType)
                {
                    body = ship;
                    break;
                }
            }
        }

        if (body is null)
        {
            foreach (Ship ship in sim.Bubble)
            {
                if (ship.Type is 128 or 130)
                {
                    body = ship;
                    break;
                }
            }
        }

        if (body is null)
        {
            return;
        }

        (int bx, int by, _) = body.GetPosition();

        // The original divides each coordinate by ten to get a value from -9 to +9, so the dot
        // stays inside the circle whatever the distance
        int dotX = Math.Clamp(bx / 10, -9, 9);
        int dotY = Math.Clamp(by / 10, -9, 9);

        float x = cx + (dotX * scale);
        float y = cy - (dotY * scale); // the y-axis is flipped, as the original notes
        float size = MathF.Max(2f, 2 * scale);
        spriteBatch.Draw(pixel, new Rectangle((int)(x - (size / 2)), (int)(y - (size / 2)), (int)size, (int)size), Palette.White);
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
        string label,
        bool labelOnRight = false)
    {
        // The right-hand panel's labels go on the outer side of its bars, because its inner side is
        // the scanner; the left-hand panel's go on the inner side, as the original has them
        int labelWidth = TextRenderer.Measure(label, TextScale).X;
        float labelX = labelOnRight
            ? x + width + (4 * Scale)
            : x - labelWidth - (4 * Scale);

        _text.Draw(spriteBatch, label, (int)labelX, (int)y, TextScale, Frame);
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
        _text.Draw(spriteBatch, label, (int)(x - labelWidth - (4 * Scale)), (int)y, TextScale, Frame);
        DrawOutline(spriteBatch, pixel, new Rectangle((int)x, (int)y, (int)width, (int)height), Frame);
        Color colour = Palette.Cyan;

        float centre = x + (width / 2);
        DrawRect(spriteBatch, pixel, centre, y, MathF.Max(1, Scale), height, new Color(80, 86, 98));

        // The rate runs from 0 to 255 with 128 in the middle
        float offset = (rate - 128) / 128f;
        float pointerX = centre + (offset * (width / 2) * 0.9f);
        DrawRect(spriteBatch, pixel, pointerX - (Scale), y, MathF.Max(1, 2 * Scale), height, colour);
    }

    private void DrawOutline(SpriteBatch spriteBatch, Texture2D pixel, Rectangle rectangle, Color colour)
    {
        float thickness = MathF.Max(1f, Scale * 0.75f);
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
