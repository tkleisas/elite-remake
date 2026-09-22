using EliteRemake.Core.Graphics;
using EliteRemake.Core.Sim;
using EliteRemake.Core.Universe;
using EliteRemake.Game.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace EliteRemake.Game.Scenes;

/// <summary>
/// Data on System: everything the original tells you about the system the charts have selected.
/// </summary>
/// <remarks>
/// The original's TT25 screen prints the distance, economy, government, tech level, population,
/// productivity and average radius, and then a description of the system generated from its seeds.
/// This screen shows the same data in the same order. The description is the one part still to come:
/// the phrases live in the original's extended token table and are assembled by its extended text
/// system, which is a substantial subsystem of its own.
/// </remarks>
public sealed class DataScene : IScene
{
    private const int Columns = 32;

    private readonly GameSession _session;
    private readonly TextRenderer _text;
    private KeyboardState _previousKeys;

    public DataScene(ViewCamera camera, GameSession session, TextRenderer text)
    {
        Camera = camera;
        _session = session;
        _text = text;
    }

    public ViewCamera Camera { get; }

    public string StatusLine =>
        $"Data on system: {_session.SelectedSystem.Name}, economy " +
        $"{Galaxy.EconomyNames[_session.SelectedSystem.Economy]}, tech level {_session.SelectedSystem.TechLevel}";

    public void Update(float elapsedSeconds)
    {
        _ = elapsedSeconds;

        KeyboardState keys = Keyboard.GetState();

        if (IsNewPress(keys, Keys.Escape) || IsNewPress(keys, Keys.F3))
        {
            _session.Screen = DockedScreen.Market;
        }

        if (IsNewPress(keys, Keys.F1))
        {
            _session.Screen = DockedScreen.ShortRangeChart;
        }

        if (IsNewPress(keys, Keys.F2))
        {
            _session.Screen = DockedScreen.LongRangeChart;
        }

        if (IsNewPress(keys, Keys.H))
        {
            _session.StartHyperspace();
        }

        _previousKeys = keys;
    }

    private bool IsNewPress(KeyboardState keys, Keys key) =>
        keys.IsKeyDown(key) && !_previousKeys.IsKeyDown(key);

    public void Draw(SpriteBatch spriteBatch, Texture2D pixel, GraphicsDevice device)
    {
        device.Clear(Palette.Space);

        int scale = Math.Max(2, (int)(device.Viewport.Height / 320f));
        int cellWidth = TextRenderer.CellWidth(scale);
        int cellHeight = TextRenderer.CellHeight(scale);
        int width = Columns * cellWidth;
        int left = Math.Max(cellWidth / 2, (device.Viewport.Width - width) / 2);
        int top = cellHeight * 2;

        spriteBatch.Begin();

        Color normal = new(200, 206, 216);
        Color label = new(140, 148, 160);
        Color dim = new(120, 128, 140);

        StarSystem system = _session.SelectedSystem;
        int distance = _session.SelectedDistance;

        _text.DrawCentred(spriteBatch, $"DATA ON {system.Name}", left + (width / 2), top, scale, Palette.White);
        int y = top + (cellHeight * 2);

        // The original's order: distance, economy, government, tech level, population,
        // productivity and radius
        Line($"DISTANCE", $"{GameSession.FormatTenths(distance)} LIGHT YEARS",
            distance <= _session.Commander.Fuel ? Palette.Green : Palette.Red);

        Line("ECONOMY", Galaxy.EconomyNames[system.Economy].ToUpperInvariant(), normal);
        Line("GOVERNMENT", Galaxy.GovernmentNames[system.Government].ToUpperInvariant(), normal);
        Line("TECH LEVEL", system.TechLevel.ToString(), normal);
        Line("POPULATION", $"{system.Population / 10}.{system.Population % 10} BILLION", normal);
        Line("PRODUCTIVITY", $"{system.Productivity} MCr", normal);
        Line("AVERAGE RADIUS", $"{system.Radius} KM", normal);
        Line("GALACTIC COORDS", $"({system.X}, {system.Y})", normal);

        y += cellHeight;
        _text.Draw(spriteBatch, "DESCRIPTION", left + cellWidth, y, scale, label);
        y += cellHeight;
        _text.Draw(spriteBatch, "Not yet generated. The phrases", left + cellWidth, y, scale, dim);
        y += cellHeight;
        _text.Draw(spriteBatch, "live in the original's extended", left + cellWidth, y, scale, dim);
        y += cellHeight;
        _text.Draw(spriteBatch, "token table, still to be ported.", left + cellWidth, y, scale, dim);

        y += cellHeight * 2;
        _text.Draw(spriteBatch, _session.Message, left + cellWidth, y, scale, Palette.Cyan);
        y += cellHeight;
        _text.Draw(
            spriteBatch,
            "F1 SHORT CHART   F2 LONG CHART   H HYPERSPACE   ESC BACK",
            left + cellWidth,
            y,
            scale,
            dim);

        spriteBatch.End();

        void Line(string name, string value, Color colour)
        {
            _text.Draw(spriteBatch, name, left + cellWidth, y, scale, label);
            _text.Draw(spriteBatch, value, left + (cellWidth * 18), y, scale, colour);
            y += cellHeight;
        }
    }
}
