using EliteRemake.Core.Graphics;
using EliteRemake.Core.Sim;
using EliteRemake.Core.Universe;
using EliteRemake.Game.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace EliteRemake.Game.Scenes;

/// <summary>
/// The commander's status and inventory, which the original shows on its own docked screens.
/// </summary>
/// <remarks>
/// The original splits this over two screens — Status Mode, which shows the commander's name,
/// rating and legal status, and the Inventory, which lists the hold and the equipment — but they
/// answer the same question, so this screen shows both together: who you are, what you are worth,
/// what you are carrying and what is fitted. Cash is printed in credits and tenths, as every other
/// screen does.
/// </remarks>
public sealed class StatusScene : IScene
{
    private const int Columns = 32;

    /// <summary>The column the values start in, past the longest label the screen has.</summary>
    private const int ValueColumn = 21;

    private readonly GameSession _session;
    private readonly TextRenderer _text;
    private readonly FlightScene? _flight;
    private KeyboardState _previousKeys;

    /// <summary>
    /// Builds the screen.
    /// </summary>
    /// <param name="flight">
    /// The flight scene, so a galactic jump can rebuild the sky. The galactic hyperdrive is fired
    /// from this screen and it moves us to a different galaxy's system, so the planet, the sun and
    /// the station all have to be replaced — and without this the old ones stayed, leaving the sky
    /// of a galaxy we had left.
    /// </param>
    public StatusScene(ViewCamera camera, GameSession session, TextRenderer text, FlightScene? flight = null)
    {
        Camera = camera;
        _session = session;
        _text = text;
        _flight = flight;
    }

    public ViewCamera Camera { get; }

    public string StatusLine =>
        $"Status: {_session.Commander.Name}, {_session.Commander.Rating}, " +
        $"{Outfitting.Format(_session.Commander.Cash)} credits, {_session.Commander.Kills} kills";

    public void Update(float elapsedSeconds)
    {
        _ = elapsedSeconds;

        KeyboardState keys = Keyboard.GetState();

        // The control settings are a modern addition, reached from here so the original's own
        // screens keep the function keys they had
        if (IsNewPress(keys, Keys.O))
        {
            _session.Screen = DockedScreen.Settings;
        }

        // The market, on the original's f7. This screen is both the original's status screen and its
        // inventory — the equipment and the hold are here rather than on two screens — so f7 and f8
        // both lead back to the market, as f8 does in the original.
        if (IsNewPress(keys, Keys.F7) ||
            IsNewPress(keys, Keys.Escape) ||
            IsNewPress(keys, Keys.F8))
        {
            _session.Screen = DockedScreen.Market;
        }

        // CTRL-H fires the galactic hyperdrive, as it does in the original
        bool control = keys.IsKeyDown(Keys.LeftControl) || keys.IsKeyDown(Keys.RightControl);
        if (control && IsNewPress(keys, Keys.H) && _session.UseGalacticHyperdrive())
        {
            // We are in a different galaxy now, so the sky has to be rebuilt: the new system's
            // planet, sun and station, and nothing left of the old
            _flight?.ArriveInSystem(_session.System);
        }

        if (IsNewPress(keys, Keys.F5))
        {
            _session.Screen = DockedScreen.ShortRangeChart;
        }

        if (IsNewPress(keys, Keys.F4))
        {
            _session.Screen = DockedScreen.LongRangeChart;
        }

        if (IsNewPress(keys, Keys.F6))
        {
            _session.Screen = DockedScreen.DataOnSystem;
        }

        _previousKeys = keys;
    }

    private bool IsNewPress(KeyboardState keys, Keys key) =>
        keys.IsKeyDown(key) && !_previousKeys.IsKeyDown(key);

    public void Draw(SpriteBatch spriteBatch, Texture2D pixel, GraphicsDevice device)
    {
        device.Clear(Palette.Space);

        int scale = Math.Max(2, (int)(device.Viewport.Height / 340f));
        int cellWidth = TextRenderer.CellWidth(scale);
        int cellHeight = TextRenderer.CellHeight(scale);
        int width = Columns * cellWidth;
        int left = Math.Max(cellWidth / 2, (device.Viewport.Width - width) / 2);
        int y = cellHeight;

        spriteBatch.Begin(samplerState: SamplerState.PointClamp);

        Color normal = new(200, 206, 216);
        Color dim = new(120, 128, 140);
        Color highlight = Palette.Yellow;

        Commander commander = _session.Commander;

        _text.DrawCentred(spriteBatch, "COMMANDER", left + (width / 2), y, scale, Palette.White);
        y += cellHeight * 2;

        Line("NAME", commander.Name, highlight);
        Line("RATING", $"{commander.Rating} ({commander.Kills} kills)", normal);
        Line("LEGAL STATUS", commander.LegalStatusName, commander.IsWanted ? Palette.Red : normal);
        Line("CASH", $"{Outfitting.Format(commander.Cash)} CR", normal);
        Line("FUEL", $"{commander.Fuel / 10}.{commander.Fuel % 10} LIGHT YEARS", normal);
        Line("GALAXY", $"{commander.GalaxyNumber + 1} OF {Galaxy.GalaxyCount}", normal);
        Line("SYSTEM", $"{_session.System.Name} ({_session.System.X},{_session.System.Y})", normal);

        y += cellHeight;
        _text.Draw(spriteBatch, "EQUIPMENT", left + cellWidth, y, scale, Palette.White);
        y += cellHeight;
        Line("MISSILES", commander.Missiles.ToString(), normal);
        Line("CARGO BAY", $"{commander.CargoCapacity} T", normal);

        // A line for each view that has a laser fitted, which is what the original's status screen
        // does: it walks the four views and prints the view's name — tokens 96 to 99, "FRONT" to
        // "RIGHT" — followed by the laser's own name, skipping the ones with nothing fitted. Ours
        // showed the front laser alone, which was all there could be until the other three views
        // arrived.
        foreach (LaserMount mount in new[] { LaserMount.Front, LaserMount.Rear, LaserMount.Left, LaserMount.Right })
        {
            LaserType fitted = commander.GetLaser(mount);
            if (fitted != LaserType.None)
            {
                Line($"{Outfitting.ViewName(mount)} LASER", Outfitting.LaserName(fitted), normal);
            }
        }
        Line("ECM", Fitted(commander.Ecm), normal);
        Line("FUEL SCOOPS", Fitted(commander.FuelScoops), normal);
        Line("ENERGY UNIT", Fitted(commander.EnergyUnit), normal);
        Line("ESCAPE POD", Fitted(commander.EscapePod), normal);
        Line("ENERGY BOMB", Fitted(commander.EnergyBomb), normal);
        Line("DOCKING COMPUTER", Fitted(commander.DockingComputer), normal);
        Line("GALACTIC HYPERDRIVE", Fitted(commander.GalacticHyperdrive), normal);

        y += cellHeight;
        _text.Draw(spriteBatch, "CARGO", left + cellWidth, y, scale, Palette.White);
        y += cellHeight;

        bool any = false;
        for (int item = 0; item < Market.Items.Length; item++)
        {
            int held = commander.GetCargo(item);
            if (held > 0)
            {
                Line(Market.Items[item].Name.ToUpperInvariant(), $"{held} {Market.Items[item].Unit}", normal);
                any = true;
            }
        }

        if (!any)
        {
            Line("(EMPTY)", $"0/{commander.CargoCapacity} T", dim);
        }
        else
        {
            Line("TOTAL", $"{commander.CargoUsed}/{commander.CargoCapacity} T", normal);
        }

        y += cellHeight;
        _text.Draw(spriteBatch, _session.Message, left + cellWidth, y, scale, Palette.Cyan);
        y += cellHeight;
        _text.DrawHintLine(
            spriteBatch,
            "F4 CHART  F5 SHORT  F6 DATA  F7 MARKET  CTRL-H GALACTIC JUMP  O CONTROLS  ESC BACK",
            left + cellWidth,
            y,
            scale,
            (int)Camera.ViewportWidth - (left + cellWidth) - 8,
            dim);

        spriteBatch.End();

        void Line(string name, string value, Color colour)
        {
            _text.Draw(spriteBatch, name, left + cellWidth, y, scale, dim);
            // The value column leaves two clear columns past the longest label, which is what keeps
            // GALACTIC HYPERDRIVE and its "FITTED" from running together
            _text.Draw(spriteBatch, value, left + (cellWidth * ValueColumn), y, scale, colour);
            y += cellHeight;
        }

        static string Fitted(bool fitted) => fitted ? "FITTED" : "-";
    }

}
