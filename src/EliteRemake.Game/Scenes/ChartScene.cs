using EliteRemake.Core.Graphics;
using EliteRemake.Core.Sim;
using EliteRemake.Core.Universe;
using EliteRemake.Game.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace EliteRemake.Game.Scenes;

/// <summary>Which chart is on screen.</summary>
public enum ChartRange
{
    /// <summary>The short-range chart: the systems around us, with our fuel circle.</summary>
    Short,

    /// <summary>The long-range chart: the whole galaxy.</summary>
    Long,
}

/// <summary>
/// The charts: the short-range chart with its fuel circle, and the long-range chart of the whole
/// galaxy.
/// </summary>
/// <remarks>
/// The original draws both charts in the same grid it uses everywhere else. The short-range chart
/// is centred on (104, 90) with four pixels to a galactic coordinate, so it covers about 26
/// coordinates in each direction, and its circle has a radius in pixels equal to the fuel in tenths
/// of a light year — a full tank of 70 draws a circle 70 pixels across the radius, which is exactly
/// where you can reach. The long-range chart plots all 256 systems of the galaxy, again at four
/// pixels per coordinate.
///
/// The crosshairs are moved with the cursor keys, and whatever system is nearest to them is the one
/// the next hyperspace jump will take us to.
/// </remarks>
public sealed class ChartScene : IScene
{
    private const int Columns = 32;

    /// <summary>The x coordinate of the centre of the chart, as the original has it.</summary>
    public const int ChartCentreX = 104;

    /// <summary>The y coordinate of the centre of the chart, as the original has it.</summary>
    public const int ChartCentreY = 90;

    private readonly GameSession _session;
    private readonly TextRenderer _text;
    private readonly StarSystem[] _galaxy;
    private KeyboardState _previousKeys;

    public ChartScene(ViewCamera camera, GameSession session, TextRenderer text, ChartRange range)
    {
        Camera = camera;
        _session = session;
        _text = text;
        Range = range;
        _galaxy = session.SystemsInGalaxy;
    }

    public ViewCamera Camera { get; }

    /// <summary>Which chart is showing.</summary>
    public ChartRange Range { get; set; }

    /// <summary>The crosshair position in galactic coordinates.</summary>
    public int CursorX { get; private set; }

    /// <summary>The crosshair position in galactic coordinates.</summary>
    public int CursorY { get; private set; }

    public string StatusLine =>
        $"Chart ({Range}): {_session.System.Name} at ({_session.System.X},{_session.System.Y}), " +
        $"selected {_session.SelectedSystem.Name} at {GameSession.FormatTenths(_session.SelectedDistance)} light years";

    /// <summary>Sets the crosshairs from a system, which is what opening the chart does.</summary>
    public void SelectSystem(StarSystem system)
    {
        CursorX = system.X;
        CursorY = system.Y;
        _session.SelectedSystem = system;
    }

    public void Update(float elapsedSeconds)
    {
        _ = elapsedSeconds;

        KeyboardState keys = Keyboard.GetState();
        int step = keys.IsKeyDown(Keys.LeftShift) || keys.IsKeyDown(Keys.RightShift) ? 8 : 2;

        bool moved = false;
        if (IsHeld(keys, Keys.Left))
        {
            CursorX = Math.Max(0, CursorX - step);
            moved = true;
        }

        if (IsHeld(keys, Keys.Right))
        {
            CursorX = Math.Min(255, CursorX + step);
            moved = true;
        }

        if (IsHeld(keys, Keys.Up))
        {
            CursorY = Math.Min(255, CursorY + step);
            moved = true;
        }

        if (IsHeld(keys, Keys.Down))
        {
            CursorY = Math.Max(0, CursorY - step);
            moved = true;
        }

        if (moved)
        {
            _session.SelectedSystem = NearestTo(CursorX, CursorY);
        }

        if (IsNewPress(keys, Keys.S))
        {
            Range = ChartRange.Short;
            SelectSystem(_session.System);
        }

        if (IsNewPress(keys, Keys.L))
        {
            Range = ChartRange.Long;
            SelectSystem(_session.System);
        }

        // The original jumps from the chart with H
        if (IsNewPress(keys, Keys.H))
        {
            _session.StartHyperspace();
        }

        if (IsNewPress(keys, Keys.F3))
        {
            _session.Screen = DockedScreen.DataOnSystem;
        }

        if (IsNewPress(keys, Keys.F8))
        {
            _session.Screen = DockedScreen.Status;
        }

        if (IsNewPress(keys, Keys.Escape))
        {
            _session.Screen = DockedScreen.Market;
        }

        _previousKeys = keys;
    }

    /// <summary>The system nearest the crosshairs, which the original finds with TT111.</summary>
    private StarSystem NearestTo(int x, int y)
    {
        StarSystem best = _session.System;
        int bestDistance = int.MaxValue;

        foreach (StarSystem system in _galaxy)
        {
            int distance = (Math.Abs(system.X - x) / 2) + (Math.Abs(system.Y - y) / 2);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = system;
            }
        }

        return best;
    }

    private bool IsNewPress(KeyboardState keys, Keys key) =>
        keys.IsKeyDown(key) && !_previousKeys.IsKeyDown(key);

    private static bool IsHeld(KeyboardState keys, Keys key) => keys.IsKeyDown(key);

    public void Draw(SpriteBatch spriteBatch, Texture2D pixel, GraphicsDevice device)
    {
        device.Clear(Palette.Space);

        int scale = Math.Max(2, (int)(device.Viewport.Height / 340f));
        int cellWidth = TextRenderer.CellWidth(scale);
        int cellHeight = TextRenderer.CellHeight(scale);
        int width = Columns * cellWidth;
        int left = Math.Max(cellWidth / 2, (device.Viewport.Width - width) / 2);
        int top = cellHeight;

        spriteBatch.Begin(samplerState: SamplerState.PointClamp);

        Color normal = new(200, 206, 216);
        Color dim = new(120, 128, 140);

        string title = Range == ChartRange.Short
            ? $"SHORT RANGE CHART - {_session.System.Name}"
            : $"LONG RANGE CHART - GALAXY {_session.Commander.GalaxyNumber + 1}";
        _text.DrawCentred(spriteBatch, title, left + (width / 2), top, scale, Palette.White);

        // The chart box, sized to the original's proportions
        var box = new Rectangle(
            left + cellWidth,
            top + (cellHeight * 2),
            Math.Min(width - (cellWidth * 2), cellWidth * 30),
            cellHeight * 20);
        DrawBox(spriteBatch, pixel, box, dim);

        // Four pixels per galactic coordinate at the original's scale, so the box maps a span of
        // coordinates across its width: the short-range chart covers about 26 each way, the long
        // range covers the whole galaxy
        int span = Range == ChartRange.Short ? 26 : 128;
        float pixelsPerUnit = box.Width / (float)(span * 2);

        // The long-range chart shrinks everything to fit the galaxy
        if (Range == ChartRange.Long)
        {
            pixelsPerUnit = box.Width / 256f;
        }

        StarSystem current = _session.System;

        // The fuel circle. A coordinate unit is four tenths of a light year (the original's
        // distance is four times its square root of the coordinate difference), so the reach of a
        // tank in coordinate units is the fuel divided by four — which the original draws as a
        // circle whose radius in its own chart pixels equals the fuel in tenths.
        if (Range == ChartRange.Short)
        {
            float radius = _session.Commander.Fuel / 4f * pixelsPerUnit;
            DrawCircle(spriteBatch, pixel, box.Center.X, box.Center.Y, radius, new Color(70, 76, 88), 64);
        }

        // Plot the systems
        foreach (StarSystem system in _galaxy)
        {
            int dx = system.X - current.X;
            int dy = system.Y - current.Y;

            if (Range == ChartRange.Short && (Math.Abs(dx) > span || Math.Abs(dy) > span))
            {
                continue;
            }

            float x = box.Center.X + (dx * pixelsPerUnit);
            float y = box.Center.Y - (dy * pixelsPerUnit);
            if (!box.Contains((int)x, (int)y))
            {
                continue;
            }

            bool selected = system.Seeds == _session.SelectedSystem.Seeds;
            bool here = system.Seeds == current.Seeds;
            Color colour = selected ? Palette.Yellow : here ? Palette.Cyan : normal;

            int size = selected || here ? 3 : 2;
            spriteBatch.Draw(pixel, new Rectangle((int)x, (int)y, size, size), colour);
        }

        // The crosshairs sit on the selected system
        int cursorDx = _session.SelectedSystem.X - current.X;
        int cursorDy = _session.SelectedSystem.Y - current.Y;
        float crossX = box.Center.X + (cursorDx * pixelsPerUnit);
        float crossY = box.Center.Y - (cursorDy * pixelsPerUnit);
        DrawCrosshairs(spriteBatch, pixel, crossX, crossY, Math.Max(3f, scale * 2f), Palette.White);

        // The selected system's data, which the original shows on its own screen
        StarSystem target = _session.SelectedSystem;
        int distance = _session.SelectedDistance;
        int line = box.Bottom + cellHeight;

        _text.Draw(spriteBatch, $"{target.Name} ({target.X},{target.Y})", left + cellWidth, line, scale, Palette.White);
        line += cellHeight;
        _text.Draw(
            spriteBatch,
            $"DISTANCE {GameSession.FormatTenths(distance)} LY   FUEL {GameSession.FormatTenths(_session.Commander.Fuel)} LY",
            left + cellWidth,
            line,
            scale,
            distance <= _session.Commander.Fuel ? Palette.Green : Palette.Red);
        line += cellHeight;
        _text.Draw(
            spriteBatch,
            $"ECONOMY {Galaxy.EconomyNames[target.Economy]}",
            left + cellWidth,
            line,
            scale,
            normal);
        line += cellHeight;
        _text.Draw(
            spriteBatch,
            $"GOVERNMENT {Galaxy.GovernmentNames[target.Government]}   TECH {target.TechLevel}   " +
            $"POP {target.Population / 10}.{target.Population % 10}B   RADIUS {target.Radius}KM",
            left + cellWidth,
            line,
            scale,
            normal);

        line += cellHeight * 2;
        _text.Draw(spriteBatch, _session.Message, left + cellWidth, line, scale, Palette.Cyan);
        line += cellHeight;
        _text.Draw(
            spriteBatch,
            "CURSORS MOVE   S SHORT   L LONG   F3 DATA   F8 STATUS   H HYPERSPACE   ESC BACK",
            left + cellWidth,
            line,
            scale,
            dim);

        spriteBatch.End();
    }

    private static void DrawBox(SpriteBatch spriteBatch, Texture2D pixel, Rectangle box, Color colour)
    {
        spriteBatch.Draw(pixel, new Rectangle(box.Left, box.Top, box.Width, 1), colour);
        spriteBatch.Draw(pixel, new Rectangle(box.Left, box.Bottom, box.Width, 1), colour);
        spriteBatch.Draw(pixel, new Rectangle(box.Left, box.Top, 1, box.Height), colour);
        spriteBatch.Draw(pixel, new Rectangle(box.Right, box.Top, 1, box.Height), colour);
    }

    private static void DrawCrosshairs(SpriteBatch spriteBatch, Texture2D pixel, float x, float y, float size, Color colour)
    {
        spriteBatch.Draw(pixel, new Rectangle((int)(x - size), (int)y, (int)(size * 2), 1), colour);
        spriteBatch.Draw(pixel, new Rectangle((int)x, (int)(y - size), 1, (int)(size * 2)), colour);
    }

    private static void DrawCircle(SpriteBatch spriteBatch, Texture2D pixel, float cx, float cy, float radius, Color colour, int segments)
    {
        if (radius < 2)
        {
            return;
        }

        for (int i = 0; i < segments; i++)
        {
            double a0 = i * Math.Tau / segments;
            double a1 = (i + 1) * Math.Tau / segments;
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
