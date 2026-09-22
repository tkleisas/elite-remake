using EliteRemake.Core.Graphics;
using EliteRemake.Core.Sim;
using EliteRemake.Core.Universe;
using EliteRemake.Game.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace EliteRemake.Game.Scenes;

/// <summary>
/// The station's market screen: what this system sells, at what price, and how much of it there is.
/// </summary>
/// <remarks>
/// The original lays the market out in the same 32-column text grid the whole game uses, with the
/// item name in column 1, the price in column 14 and the availability after it, one of the
/// seventeen commodities to a line. This screen keeps that layout and the same numbers, and adds
/// the commander's cash and hold so the trade can be read at a glance. Number keys pick a line,
/// B and S buy and sell.
/// </remarks>
public sealed class MarketScene : IScene
{
    private const int Columns = 32;

    private readonly GameSession _session;
    private readonly TextRenderer _text;
    private KeyboardState _previousKeys;

    public MarketScene(ViewCamera camera, GameSession session, TextRenderer text)
    {
        Camera = camera;
        _session = session;
        _text = text;
    }

    public ViewCamera Camera { get; }

    /// <summary>The item the player has selected, or -1 for none.</summary>
    public int Selected { get; private set; } = -1;

    public string StatusLine =>
        $"Market: {_session.System.Name}, {_session.Market.Length} items, " +
        $"{_session.Commander.Cash / 10}.{_session.Commander.Cash % 10} credits, " +
        $"{_session.Commander.CargoUsed}/{_session.Commander.CargoCapacity} t held";

    public void Update(float elapsedSeconds)
    {
        _ = elapsedSeconds;

        KeyboardState keys = Keyboard.GetState();

        for (int i = 0; i < 10; i++)
        {
            Keys key = Keys.D1 + i;
            if (IsNewPress(keys, key))
            {
                int index = i == 9 ? 9 : i;
                Selected = index < _session.Market.Length ? index : Selected;
            }
        }

        if (Selected >= 0)
        {
            if (IsNewPress(keys, Keys.B))
            {
                _session.Buy(Selected, 1);
            }

            if (IsNewPress(keys, Keys.S))
            {
                _session.Sell(Selected, 1);
            }
        }

        if (IsNewPress(keys, Keys.E))
        {
            _session.Screen = DockedScreen.Equipment;
        }

        if (IsNewPress(keys, Keys.Escape))
        {
            _session.Launch();
        }

        _previousKeys = keys;
    }

    /// <summary>True if the key went down since the last frame.</summary>
    private bool IsNewPress(KeyboardState keys, Keys key) =>
        keys.IsKeyDown(key) && !_previousKeys.IsKeyDown(key);

    public void Draw(SpriteBatch spriteBatch, Texture2D pixel, GraphicsDevice device)
    {
        device.Clear(Palette.Space);

        int scale = Math.Max(2, (int)(device.Viewport.Height / 320f));
        int cellWidth = TextRenderer.CellWidth(scale);
        int cellHeight = TextRenderer.CellHeight(scale);

        // Centre the 32-column grid in the window
        int width = Columns * cellWidth;
        int left = Math.Max(cellWidth / 2, (device.Viewport.Width - width) / 2);
        int top = cellHeight;

        spriteBatch.Begin();

        Color title = Palette.White;
        Color normal = new(200, 206, 216);
        Color highlight = Palette.Yellow;

        string heading = $"MARKET PRICES - {_session.System.Name}";
        _text.DrawCentred(spriteBatch, heading, left + (width / 2), top, scale, title);
        int y = top + (cellHeight * 2);

        // The original's column positions: name at 1, price at 14, availability after that
        _text.Draw(spriteBatch, "ITEM", left + cellWidth, y, scale, normal);
        _text.Draw(spriteBatch, Pad("PRICE", 6), left + (cellWidth * 14), y, scale, normal);
        _text.Draw(spriteBatch, Pad("FOR SALE", 6), left + (cellWidth * 21), y, scale, normal);
        _text.Draw(spriteBatch, "HELD", left + (cellWidth * 28), y, scale, normal);
        y += cellHeight;

        for (int i = 0; i < _session.Market.Length; i++)
        {
            MarketEntry entry = _session.Market[i];
            bool selected = i == Selected;
            Color colour = selected ? highlight : normal;

            string marker = i < 10 ? ((i + 1) % 10).ToString() : " ";
            string name = Truncate(entry.Item.Name, 12);
            string price = Market.FormatPrice(entry.Price);
            string available = entry.Availability > 0 ? entry.Availability.ToString() : "-";
            string held = _session.Commander.GetCargo(entry.Item.Index) is var held0 && held0 > 0
                ? $"({held0})"
                : string.Empty;

            _text.Draw(spriteBatch, marker, left, y, scale, colour);
            _text.Draw(spriteBatch, name, left + (cellWidth * 1), y, scale, colour);
            _text.Draw(spriteBatch, Pad(price, 6), left + (cellWidth * 14), y, scale, colour);
            _text.Draw(spriteBatch, Pad(available, 6), left + (cellWidth * 21), y, scale, colour);
            _text.Draw(spriteBatch, held, left + (cellWidth * 28), y, scale, colour);

            y += cellHeight;
        }

        y += cellHeight;
        _text.Draw(
            spriteBatch,
            $"CASH {Market.FormatPrice(_session.Commander.Cash)}   HOLD {_session.Commander.CargoUsed}/{_session.Commander.CargoCapacity}t   FUEL {_session.Commander.Fuel}.0",
            left,
            y,
            scale,
            normal);

        y += cellHeight;
        _text.Draw(spriteBatch, _session.Message, left, y, scale, Palette.Cyan);
        y += cellHeight;
        _text.Draw(spriteBatch, "1-0 SELECT   B BUY   S SELL   E EQUIPMENT   ESC LAUNCH", left, y, scale, new Color(120, 128, 140));

        spriteBatch.End();
    }

    private static string Truncate(string value, int length) =>
        value.Length <= length ? value : value[..length];

    /// <summary>Right-aligns a value in a fixed width field, as the original's number printer does.</summary>
    private static string Pad(string value, int width) =>
        value.Length >= width ? value : value.PadLeft(width);
}
