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
/// This screen shows the same data in the same order, and generates the description the same way:
/// the phrases come from the original's extended token table and are assembled by its extended text
/// system, seeded from the system's own s1 and s2 so the same system always reads the same.
///
/// The disc version only shows the extended description when docked. Its PDESC routine says so
/// directly — the routine is not in the flight code at all, "as the PDESC routine isn't present in
/// the flight code due to memory restrictions" — and this screen is only reachable while docked,
/// so that falls out for free.
/// </remarks>
public sealed class DataScene : IScene
{
    private const int Columns = 32;

    private readonly GameSession _session;
    private readonly TextRenderer _text;
    private KeyboardState _previousKeys;

    private readonly List<string> _description = [];
    private StarSystem _described;
    private int _visits;

    public DataScene(ViewCamera camera, GameSession session, TextRenderer text)
    {
        Camera = camera;
        _session = session;
        _text = text;
        Regenerate();
    }

    /// <summary>
    /// Builds a description of the selected system, wrapped to the 32-column grid. A fresh
    /// description is generated whenever the selection or the visit changes, as the original's
    /// phrase choice is random.
    /// </summary>
    private void Regenerate()
    {
        StarSystem system = _session.SelectedSystem;
        if (system.Seeds == _described.Seeds && _visits == _session.Visit)
        {
            return;
        }

        _described = system;
        _visits = _session.Visit;

        // A system on a mission's trail replaces its description with a hint, as the original's
        // PDESC does — and only for the system we are docked at, not the one the charts point at
        int hint = EliteRemake.Data.DescriptionData.Hints.TokenFor(
            system,
            _session.System,
            _session.Commander.GalaxyNumber,
            _session.Missions.Mission1Active);

        // PDESC seeds the generator from the system's own s1 and s2 seeds before printing, so the
        // same system always gets the same description however many times it is read. Sharing one
        // running generator instead makes every system read alike, because each screen advances it
        // to a different point and the phrases are chosen from wherever it happens to be.
        _session.DescriptionRandom.Reseed(system.Seeds.ToBytes().AsSpan(2));

        string text = hint != 0
            ? EliteRemake.Data.DescriptionData.Hint(hint, system.Name, _session.DescriptionRandom)
            : EliteRemake.Data.DescriptionData.Describe(system.Name, _session.DescriptionRandom);
        _description.Clear();
        _description.AddRange(Wrap(text, Columns - 2));
    }

    /// <summary>Wraps text to a column limit, breaking on spaces.</summary>
    private static IEnumerable<string> Wrap(string text, int width)
    {
        foreach (string paragraph in text.Split('\n'))
        {
            string remaining = paragraph.Trim();
            while (remaining.Length > width)
            {
                int split = remaining.LastIndexOf(' ', width);
                if (split <= 0)
                {
                    split = width;
                }

                yield return remaining[..split];
                remaining = remaining[(split + 1)..].TrimStart();
            }

            if (remaining.Length > 0)
            {
                yield return remaining;
            }
        }
    }

    public ViewCamera Camera { get; }

    public string StatusLine =>
        $"Data on system: {_session.SelectedSystem.Name}, economy " +
        $"{Galaxy.EconomyNames[_session.SelectedSystem.Economy]}, tech level {_session.SelectedSystem.TechLevel}";

    public void Update(float elapsedSeconds)
    {
        _ = elapsedSeconds;

        Regenerate();
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

        // The description is assembled from the original's extended tokens, with the phrases picked
        // at random, so it changes between visits as it does in the original
        foreach (string line in _description)
        {
            _text.Draw(spriteBatch, line, left + cellWidth, y, scale, normal);
            y += cellHeight;
        }

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
