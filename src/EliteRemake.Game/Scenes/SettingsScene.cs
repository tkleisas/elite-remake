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

    public SettingsScene(ViewCamera camera, GameSession session, TextRenderer text, Settings settings)
    {
        Camera = camera;
        _session = session;
        _text = text;
        _settings = settings;
    }

    public ViewCamera Camera { get; }

    public string Name => "Control Settings";

    public string StatusLine =>
        $"Control settings: {_settings.Bindings.Count} bindings, selected {_settings.Bindings[_selected].Name}";

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
                    (string name, _, Action<string> set) = _settings.Bindings[_selected];
                    set(key.ToString());
                    _message = $"{name} is now {Settings.DisplayName(key.ToString())}.";
                }

                _waitingForKey = false;
                break;
            }

            _previousKeys = keys;
            return;
        }

        if (IsNewPress(keys, Keys.Up) || IsNewPress(keys, Keys.W))
        {
            _selected = (_selected + _settings.Bindings.Count - 1) % _settings.Bindings.Count;
        }
        else if (IsNewPress(keys, Keys.Down) || IsNewPress(keys, Keys.S))
        {
            _selected = (_selected + 1) % _settings.Bindings.Count;
        }
        else if (IsNewPress(keys, Keys.Enter) || IsNewPress(keys, Keys.Space))
        {
            _waitingForKey = true;
            _message = "Press the key to bind, or ESCAPE to cancel.";
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
        else if (IsNewPress(keys, Keys.S) && (keys.IsKeyDown(Keys.LeftControl) || keys.IsKeyDown(Keys.RightControl)))
        {
            _message = _settings.Save();
        }
        else if (IsNewPress(keys, Keys.Escape))
        {
            _session.Screen = DockedScreen.Status;
        }

        _previousKeys = keys;
    }

    public void Draw(SpriteBatch spriteBatch, Texture2D pixel, GraphicsDevice device)
    {
        _ = pixel;
        device.Clear(Palette.Space);

        int scale = 2;
        int line = (int)(Camera.ViewportHeight / 2) - 240;
        int left = (int)(Camera.CentreX - (Columns * 4 * scale));
        int nameColumn = left + (17 * 8 * scale);

        spriteBatch.Begin();
        _text.DrawCentred(spriteBatch, "CONTROL SETTINGS", (int)Camera.CentreX, line, scale, Palette.White);
        line += 40;

        for (int i = 0; i < _settings.Bindings.Count; i++)
        {
            (string name, Func<string> get, _) = _settings.Bindings[i];
            Color colour = i == _selected ? Palette.Yellow : Palette.White;
            string marker = i == _selected ? ">" : " ";

            _text.Draw(spriteBatch, $"{marker}{name}", left, line, scale, colour);
            _text.Draw(spriteBatch, Settings.DisplayName(get()), nameColumn, line, scale, colour);
            line += 24;
        }

        line += 16;
        _text.Draw(spriteBatch, _message, left, line, scale, Palette.Cyan);
        line += 32;
        _text.Draw(spriteBatch, "UP/DOWN SELECT   ENTER REBIND", left, line, scale, Palette.Cyan);
        line += 24;
        _text.Draw(spriteBatch, "R RESET   CTRL-S SAVE   ESCAPE BACK", left, line, scale, Palette.Cyan);
        spriteBatch.End();
    }
}
