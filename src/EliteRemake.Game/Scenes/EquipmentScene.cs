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

    /// <summary>The column the prices start in, past the longest item name the shop has.</summary>
    private const int PriceColumn = 24;

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

    /// <summary>
    /// Opens the shop with the view question already asked, which is how the command line's
    /// <c>--buy</c> can show the prompt without a keyboard to answer it with.
    /// </summary>
    public void AskWhichView(int item)
    {
        Selected = item;
        AwaitingViewFor = item;
    }

    /// <summary>The item the player has selected, or -1 for none.</summary>
    public int Selected { get; private set; } = -1;

    /// <summary>
    /// The laser item waiting for a view to be chosen, or -1. The original asks this question with
    /// its qv routine — "Print a menu listing the four space views, with a View ? prompt" — as soon
    /// as a laser is bought, and fits it to the view whose number is answered.
    /// </summary>
    public int AwaitingViewFor { get; private set; } = -1;

    public string StatusLine =>
        $"Equipment: {_session.System.Name} (tech level {_session.System.TechLevel}) stocks " +
        $"{Outfitting.ItemsStocked(_session.System)} items, {Outfitting.Format(_session.Commander.Cash)} credits";

    public void Update(float elapsedSeconds)
    {
        _ = elapsedSeconds;

        KeyboardState keys = Keyboard.GetState();

        // A laser has been bought and we are waiting for the view to fit it to, so this is the qv
        // prompt: 0 to 3 answer it, and nothing else on the screen responds until it is answered.
        if (AwaitingViewFor >= 0)
        {
            for (int view = 0; view < 4; view++)
            {
                if (IsNewPress(keys, Keys.D0 + view))
                {
                    _session.BuyEquipment(AwaitingViewFor, 0, (LaserMount)view);
                    AwaitingViewFor = -1;
                    _previousKeys = keys;
                    return;
                }
            }

            // The original's gnum waits for a valid digit and has no way out of the prompt. Escape
            // abandons the purchase rather than trapping the player in it, which is an addition.
            if (IsNewPress(keys, Keys.Escape))
            {
                AwaitingViewFor = -1;
                _session.Message = "Purchase abandoned.";
            }

            _previousKeys = keys;
            return;
        }

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
            // A laser needs a view to fit it to before it can be bought
            if (Outfitting.IsLaserItem(Selected))
            {
                AwaitingViewFor = Selected;
                _session.Message = "Which view?";
            }
            else
            {
                _session.BuyEquipment(Selected);
            }
        }

        if (IsNewPress(keys, Keys.M))
        {
            _session.Screen = DockedScreen.Market;
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

        // The market, on the original's f7
        if (IsNewPress(keys, Keys.F7))
        {
            _session.Screen = DockedScreen.Market;
        }

        // The inventory, on the original's f9. The original keeps the inventory and the status
        // screen apart; ours shows the equipment and the hold on one screen, so f9 comes here too.
        if (IsNewPress(keys, Keys.F9))
        {
            _session.Screen = DockedScreen.Status;
        }

        if (IsNewPress(keys, Keys.F8))
        {
            _session.Screen = DockedScreen.Status;
        }

        // The original saves the commander from the docked screens
        if (IsNewPress(keys, Keys.S) && (keys.IsKeyDown(Keys.LeftControl) || keys.IsKeyDown(Keys.RightControl)))
        {
            _session.Save();
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

        spriteBatch.Begin(samplerState: SamplerState.PointClamp);

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
        // The price column starts past the longest name in the table, which is "Extra Military
        // Lasers": at column 18 the two ran into each other, and the dash that marks an item we
        // already own had nowhere to go at all.
        _text.Draw(spriteBatch, "ITEM", left + cellWidth, y, scale, normal);
        _text.Draw(spriteBatch, "PRICE", left + (cellWidth * PriceColumn), y, scale, normal);
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
            _text.Draw(spriteBatch, Pad(price, 8), left + (cellWidth * PriceColumn), y, scale, colour);
            _text.Draw(spriteBatch, owned, left + (cellWidth * (PriceColumn + 8)), y, scale, colour);
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
        _text.DrawHintLine(spriteBatch, "1-9 SELECT  B BUY  F FUEL  M MARKET  F8 STATUS  ESC LAUNCH", left, y, scale, (int)Camera.ViewportWidth - left - 8, dim);

        // The view menu, which the original prints at row 16, column 12 — under the list on its
        // twenty-four row screen. Ours has room below the hints, so it goes there rather than over
        // the prices.
        if (AwaitingViewFor >= 0)
        {
            int menuY = y + (cellHeight * 2);
            int menuX = left + (cellWidth * 12);
            for (int view = 0; view < 4; view++)
            {
                _text.Draw(
                    spriteBatch,
                    $"{view} {Outfitting.ViewName((LaserMount)view)}",
                    menuX,
                    menuY,
                    scale,
                    view == 0 ? highlight : normal);
                menuY += cellHeight;
            }

            _text.Draw(spriteBatch, "VIEW ?", menuX, menuY + cellHeight, scale, Palette.Cyan);
        }

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
