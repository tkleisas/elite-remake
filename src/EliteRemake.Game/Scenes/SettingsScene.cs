using EliteRemake.Core.Graphics;
using EliteRemake.Core.Sim;
using EliteRemake.Game.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace EliteRemake.Game.Scenes;

/// <summary>
/// The control settings: which key does what.
/// </summary>
/// <remarks>
/// This screen is a modern addition. The original's keys are fixed — IBM and Acornsoft chose them
/// and there was no way to change them — so nothing here is a port. It follows the original's screens
/// in how it looks and how it is driven, and it is reached from the status screen rather than taking
/// a red function key of its own, so the original's own screens keep the keys they had.
/// </remarks>
public sealed class SettingsScene : IScene
{
    private const int Columns = 32;

    private readonly GameSession _session;
    private readonly TextRenderer _text;
    private readonly Settings _settings;
    private KeyboardState _previousKeys;
    private int _selected;
    private bool _waitingForKey;
    private string _message = string.Empty;

    /// <summary>An on/off option shown above the bindings, which is not a key.</summary>
    /// <param name="Name">The label, as the panel shows it.</param>
    /// <param name="Value">Reads the current value, for the right-hand column.</param>
    /// <param name="Toggle">Flips the value when the row is chosen.</param>
    public readonly record struct Extra(string Name, Func<string> Value, Action Toggle);

    /// <param name="camera">The camera the panel is laid out from.</param>
    /// <param name="session">The session, which the docked screen returns to when the panel closes.</param>
    /// <param name="text">The bitmap font renderer.</param>
    /// <param name="settings">The bindings to edit.</param>
    /// <param name="extras">On/off options to show above the bindings, if any.</param>
    /// <param name="onExit">
    /// What to do when the panel is closed. The docked screen returns to the status screen, which is
    /// where the original keeps it; the title screen has its own place to go back to.
    /// </param>
    public SettingsScene(
        ViewCamera camera,
        GameSession session,
        TextRenderer text,
        Settings settings,
        IReadOnlyList<Extra>? extras = null,
        Action? onExit = null)
    {
        Camera = camera;
        _session = session;
        _text = text;
        _settings = settings;
        _extras = extras ?? [];
        _onExit = onExit;
    }

    private readonly IReadOnlyList<Extra> _extras;
    private readonly Action? _onExit;

    /// <summary>How many rows the panel has in total.</summary>
    private int RowCount => _extras.Count + _settings.Bindings.Count;

    public ViewCamera Camera { get; }

    public string Name => "Control Settings";

    public string StatusLine =>
        $"Control settings: {_settings.Bindings.Count} bindings, selected {SelectedName}";

    /// <summary>The name of the row the cursor is on.</summary>
    private string SelectedName =>
        _selected < _extras.Count ? _extras[_selected].Name : _settings.Bindings[_selected - _extras.Count].Name;

    private bool IsNewPress(KeyboardState keys, Keys key) =>
        keys.IsKeyDown(key) && !_previousKeys.IsKeyDown(key);

    public void Update(float elapsedSeconds)
    {
        KeyboardState keys = Keyboard.GetState();

        if (_waitingForKey)
        {
            // The next key pressed becomes the binding. Escape abandons the rebind rather than
            // binding Escape, which a player would otherwise do by accident and then be stuck with.
            foreach (Keys key in keys.GetPressedKeys())
            {
                if (!IsNewPress(keys, key))
                {
                    continue;
                }

                if (key == Keys.Escape)
                {
                    _message = "Rebind cancelled.";
                }
                else
                {
                    (string name, _, Action<string> set) = _settings.Bindings[_selected - _extras.Count];
                    set(key.ToString());
                    _message = $"{name} is now {Settings.DisplayName(key.ToString())}.";
                }

                _waitingForKey = false;
                break;
            }

            _previousKeys = keys;
            return;
        }

        int rows = Math.Max(1, RowCount);

        if (IsNewPress(keys, Keys.Up) || IsNewPress(keys, Keys.W))
        {
            _selected = (_selected + rows - 1) % rows;
        }
        else if (IsNewPress(keys, Keys.Down) || IsNewPress(keys, Keys.S))
        {
            _selected = (_selected + 1) % rows;
        }
        else if (_selected < _extras.Count &&
                 (IsNewPress(keys, Keys.Enter) || IsNewPress(keys, Keys.Space) ||
                  IsNewPress(keys, Keys.Left) || IsNewPress(keys, Keys.Right)))
        {
            // An on/off option flips on either direction key or on Enter, since there is no key to
            // wait for
            Extra extra = _extras[_selected];
            extra.Toggle();
            _message = $"{extra.Name} is {extra.Value().ToLowerInvariant()}.";
        }
        else if (_selected >= _extras.Count &&
                 (IsNewPress(keys, Keys.Enter) || IsNewPress(keys, Keys.Space)))
        {
            _waitingForKey = true;
            _message = "Press the key to bind, or ESCAPE to cancel.";
        }
        else if (IsNewPress(keys, Keys.S) && (keys.IsKeyDown(Keys.LeftControl) || keys.IsKeyDown(Keys.RightControl)))
        {
            _message = _settings.Save();
        }
        else if (IsNewPress(keys, Keys.R))
        {
            var defaults = new Settings();
            for (int i = 0; i < _settings.Bindings.Count; i++)
            {
                _settings.Bindings[i].Set(defaults.Bindings[i].Get());
            }

            _message = "Bindings reset to the originals.";
        }
        else if (IsNewPress(keys, Keys.Escape))
        {
            if (_onExit is { } exit)
            {
                exit();
            }
            else
            {
                _session.Screen = DockedScreen.Status;
            }
        }

        _previousKeys = keys;
    }

    public void Draw(SpriteBatch spriteBatch, Texture2D pixel, GraphicsDevice device)
    {
        _ = pixel;
        device.Clear(Palette.Space);

        // The whole panel is laid out from the view and scaled to fit it, so the list, the title and
        // the hints stay on screen at any window size rather than running off the top on a small one
        int rows = RowCount + 5;   // title, the rows, a gap, the message, two hints

        // The glyphs are eight pixels tall plus their spacing, so the line height follows from the
        // scale rather than the other way round: choosing a line height and then a scale that does
        // not fit inside it is what made the rows overlap
        int scale = Math.Max(1, (int)(Camera.ViewportHeight * 0.9f / rows) / 10);
        int textHeight = TextRenderer.CellHeight(scale) + (2 * scale);
        int left = (int)(Camera.CentreX - (10 * 8 * scale));
        int nameColumn = left + (17 * 8 * scale);

        int blockHeight = rows * textHeight;
        int line = (int)((Camera.ViewportHeight - blockHeight) / 2);

        spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        _text.DrawCentred(spriteBatch, "CONTROL SETTINGS", (int)Camera.CentreX, line, scale, Palette.White);
        line += textHeight * 2;

        for (int i = 0; i < RowCount; i++)
        {
            string name;
            string value;

            if (i < _extras.Count)
            {
                name = _extras[i].Name;
                value = _extras[i].Value();
            }
            else
            {
                (string bindingName, Func<string> get, _) = _settings.Bindings[i - _extras.Count];
                name = bindingName;
                value = Settings.DisplayName(get());
            }

            Color colour = i == _selected ? Palette.Yellow : Palette.White;
            string marker = i == _selected ? ">" : " ";

            _text.Draw(spriteBatch, $"{marker}{name}", left, line, scale, colour);
            _text.Draw(spriteBatch, value, nameColumn, line, scale, colour);
            line += textHeight;
        }

        line += textHeight;
        _text.Draw(spriteBatch, _message, left, line, scale, Palette.Cyan);
        line += textHeight;
        _text.Draw(spriteBatch, "UP/DOWN SELECT  ENTER CHANGE", left, line, scale, Palette.Cyan);
        line += textHeight;
        _text.Draw(spriteBatch, "R RESET  CTRL-S SAVE  ESC BACK", left, line, scale, Palette.Cyan);
        spriteBatch.End();
    }
}
