using EliteRemake.Core.Graphics;
using EliteRemake.Core.Sim;
using EliteRemake.Core.Universe;
using EliteRemake.Game.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace EliteRemake.Game.Scenes;

/// <summary>
/// The station's equipment shop: what this system stocks, what it costs, and what we already have.
/// </summary>
/// <remarks>
/// The original lists the items on sale with their prices and marks the ones we already own with a
/// dash. Which items appear depends on the system's tech level, so a backwater sells fuel and
/// missiles while a corporate state will sell you a galactic hyperdrive. Number keys pick an item;
/// fuel is bought in ten light year blocks, everything else in one go.
/// </remarks>
public sealed class EquipmentScene : IScene
{
    private const int Columns = 32;
    private const int FuelBlock = 10;

    private readonly GameSession _session;
    private readonly TextRenderer _text;
    private KeyboardState _previousKeys;

    public EquipmentScene(ViewCamera camera, GameSession session, TextRenderer text)
    {
        Camera = camera;
        _session = session;
        _text = text;
    }

    public ViewCamera Camera { get; }

    /// <summary>The item the player has selected, or -1 for none.</summary>
    public int Selected { get; private set; } = -1;

    public string StatusLine =>
        $"Equipment: {_session.System.Name} (tech level {_session.System.TechLevel}) stocks " +
        $"{Outfitting.ItemsStocked(_session.System)} items, {Outfitting.Format(_session.Commander.Cash)} credits";

    public void Update(float elapsedSeconds)
    {
        _ = elapsedSeconds;

        KeyboardState keys = Keyboard.GetState();

        for (int i = 0; i < 10; i++)
        {
            if (IsNewPress(keys, Keys.D1 + i))
            {
                int index = i == 9 ? 9 : i;
                if (index < Outfitting.ItemsStocked(_session.System))
                {
                    Selected = index;
                }
            }
        }

        // F buys fuel in ten light year blocks, B buys the selected item
        if (IsNewPress(keys, Keys.F))
        {
            _session.BuyEquipment(0, FuelBlock);
        }

        if (Selected >= 0 && IsNewPress(keys, Keys.B))
        {
            _session.BuyEquipment(Selected);
        }

        if (IsNewPress(keys, Keys.M))
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
            _session.Launch();
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
        int top = cellHeight;

        spriteBatch.Begin();

        Color normal = new(200, 206, 216);
        Color highlight = Palette.Yellow;
        Color dim = new(120, 128, 140);

        _text.DrawCentred(
            spriteBatch,
            $"EQUIPMENT - {_session.System.Name}",
            left + (width / 2),
            top,
            scale,
            Palette.White);

        int y = top + (cellHeight * 2);
        _text.Draw(spriteBatch, "ITEM", left + cellWidth, y, scale, normal);
        _text.Draw(spriteBatch, "PRICE", left + (cellWidth * 20), y, scale, normal);
        y += cellHeight;

        int stocked = Outfitting.ItemsStocked(_session.System);
        for (int i = 0; i < stocked; i++)
        {
            EquipmentItem item = Outfitting.Items[i];
            bool selected = i == Selected;
            Color colour = selected ? highlight : normal;

            string marker = i < 10 ? ((i + 1) % 10).ToString() : " ";
            string price = item.Number == 0
                ? $"{Outfitting.Format(Outfitting.FuelPricePerLightYear)}/ly"
                : Outfitting.Format(item.Price);
            string owned = Owned(item.Number) ? "-" : string.Empty;

            _text.Draw(spriteBatch, marker, left, y, scale, colour);
            _text.Draw(spriteBatch, item.Name, left + (cellWidth * 2), y, scale, colour);
            _text.Draw(spriteBatch, Pad(price, 8), left + (cellWidth * 18), y, scale, colour);
            _text.Draw(spriteBatch, owned, left + (cellWidth * 27), y, scale, colour);
            y += cellHeight;
        }

        y += cellHeight;
        _text.Draw(
            spriteBatch,
            $"CASH {Outfitting.Format(_session.Commander.Cash)}   FUEL {_session.Commander.Fuel}.0   " +
            $"MISSILES {_session.Commander.Missiles}   HOLD {_session.Commander.CargoUsed}/{_session.Commander.CargoCapacity}t",
            left,
            y,
            scale,
            normal);

        y += cellHeight;
        _text.Draw(spriteBatch, _session.Message, left, y, scale, Palette.Cyan);
        y += cellHeight;
        _text.Draw(spriteBatch, "1-9 SELECT  B BUY  F FUEL  M MARKET  F1/F2 CHARTS  F3 DATA  F8 STATUS  ESC LAUNCH", left, y, scale, dim);

        spriteBatch.End();
    }

    /// <summary>True if the commander already owns this item, so the shop shows a dash.</summary>
    private bool Owned(int item)
    {
        Commander commander = _session.Commander;
        return item switch
        {
            2 => commander.CargoCapacity >= 35,
            3 => commander.Ecm,
            6 => commander.FuelScoops,
            7 => commander.EscapePod,
            8 => commander.EnergyBomb,
            9 => commander.EnergyUnit,
            10 => commander.DockingComputer,
            11 => commander.GalacticHyperdrive,
            _ => false,
        };
    }

    private static string Pad(string value, int width) =>
        value.Length >= width ? value : value.PadLeft(width);
}
