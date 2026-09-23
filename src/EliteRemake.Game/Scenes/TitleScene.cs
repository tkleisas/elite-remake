using EliteRemake.Core.Graphics;
using EliteRemake.Core.Maths;
using EliteRemake.Core.Ships;
using EliteRemake.Core.Sim;
using EliteRemake.Game.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace EliteRemake.Game.Scenes;

/// <summary>
/// The start screen: the game's name, our ship turning in front of us, and the things you can do
/// before flying.
/// </summary>
/// <remarks>
/// <para>
/// The original has a title screen — it shows the game's name over a starfield — but it has no menu:
/// the disc version asks whether to load a commander and then gets on with it, because a 1984 game
/// had a keyboard and nothing else to offer. The frame here is the original's, and the options are
/// what a modern player expects to find before a game starts: start, a commander from the save file,
/// a new commander, the controls, and the music.
/// </para>
/// <para>
/// The music is the one deliberate piece of it that is not a port at all. The BBC disc has no music,
/// so the waltz is a departure rather than a restoration — see <see cref="Core.Audio.BlueDanube"/>.
/// </para>
/// </remarks>
public sealed class TitleScene : IScene
{
    private readonly GameSession _session;
    private readonly TextRenderer _text;
    private readonly Settings _settings;
    private readonly Audio.SoundBank? _sound;
    private readonly MeshRenderer _renderer;
    private readonly Starfield _starfield;
    private readonly ShipMesh _ship;
    private readonly SettingsScene _settingsScene;

    private Orientation _orientation;
    private float _time;
    private int _selected;
    private string _message = string.Empty;
    private KeyboardState _previousKeys;

    public TitleScene(
        ViewCamera camera,
        GraphicsDevice device,
        GameSession session,
        TextRenderer text,
        Settings settings,
        Audio.SoundBank? sound,
        ShipMesh ship)
    {
        Camera = camera;
        _session = session;
        _text = text;
        _settings = settings;
        _sound = sound;
        _ship = ship;
        _renderer = new MeshRenderer(device);
        _starfield = new Starfield();
        _orientation = Orientation.FromHeadingPitch(0, 0);

        // The settings panel is the docked one, which is the same list of bindings wherever it is
        // opened from. The music option is the only thing the title screen adds to it.
        _settingsScene = new SettingsScene(
            camera,
            session,
            text,
            settings,
            extras:
            [
                new SettingsScene.Extra(
                    "MUSIC",
                    () => settings.Music ? "ON" : "OFF",
                    ToggleMusic),
            ],
            onExit: () => ShowSettings = false);
    }

    /// <summary>True while the control panel is open over the title.</summary>
    public bool ShowSettings { get; set; }

    public ViewCamera Camera { get; }

    /// <summary>What the cursor can land on.</summary>
    private IReadOnlyList<string> Options =>
    [
        "START",
        GameSession.HasSave ? "LOAD COMMANDER" : "NEW COMMANDER",
        GameSession.HasSave ? "NEW COMMANDER" : "SETTINGS",
        GameSession.HasSave ? "SETTINGS" : "QUIT",
        GameSession.HasSave ? "QUIT" : string.Empty,
    ];

    /// <summary>The option the cursor is on.</summary>
    private string SelectedOption
    {
        get
        {
            IReadOnlyList<string> options = Options;
            int index = Math.Clamp(_selected, 0, OptionCount - 1);
            return options[index];
        }
    }

    /// <summary>How many options there are, leaving out the empty slot the shorter menu has.</summary>
    private int OptionCount => GameSession.HasSave ? 5 : 4;

    private void ToggleMusic()
    {
        _settings.Music = !_settings.Music;
        if (_settings.Music)
        {
            _sound?.PlayMusic();
        }
        else
        {
            _sound?.StopMusic();
        }

        _settings.Save();
    }

    private bool IsNewPress(KeyboardState keys, Keys key) =>
        keys.IsKeyDown(key) && !_previousKeys.IsKeyDown(key);

    public void Update(float elapsedSeconds)
    {
        if (ShowSettings)
        {
            _settingsScene.Update(elapsedSeconds);
            return;
        }

        _time += elapsedSeconds;
        _orientation = Orientation.FromHeadingPitch(_time * 0.45, Math.Sin(_time * 0.3) * 0.35);
        _starfield.Update(40f * elapsedSeconds);

        KeyboardState keys = Keyboard.GetState();

        if (IsNewPress(keys, Keys.Up) || IsNewPress(keys, Keys.W))
        {
            _selected = (_selected + OptionCount - 1) % OptionCount;
        }
        else if (IsNewPress(keys, Keys.Down) || IsNewPress(keys, Keys.S))
        {
            _selected = (_selected + 1) % OptionCount;
        }
        else if (IsNewPress(keys, Keys.Enter) || IsNewPress(keys, Keys.Space))
        {
            Choose(SelectedOption);
        }
        else if (IsNewPress(keys, Keys.M))
        {
            ToggleMusic();
            _message = $"Music {(_settings.Music ? "on" : "off")}.";
        }
        else if (IsNewPress(keys, Keys.Escape))
        {
            Choose("QUIT");
        }

        _previousKeys = keys;
    }

    private void Choose(string option)
    {
        switch (option)
        {
            case "START":
                StartGame();
                break;

            case "LOAD COMMANDER":
                _message = _session.TryLoad() ?? "Commander loaded. Press START.";
                break;

            case "NEW COMMANDER":
                _session.NewCommander();
                _message = "A new commander, with a Cobra Mk III and a hundred credits.";
                break;

            case "SETTINGS":
                ShowSettings = true;
                break;

            case "QUIT":
                QuitRequested = true;
                break;

            default:
                // The empty slot in the shorter menu
                break;
        }
    }

    /// <summary>Starts the game: the shell moves on to the flight scene.</summary>
    private void StartGame()
    {
        _sound?.StopMusic();
        _session.Launch();
    }

    /// <summary>Set when the player asks to leave, which the shell acts on.</summary>
    public bool QuitRequested { get; private set; }

    public void Draw(SpriteBatch spriteBatch, Texture2D pixel, GraphicsDevice device)
    {
        if (ShowSettings)
        {
            _settingsScene.Draw(spriteBatch, pixel, device);
            return;
        }

        device.Clear(Palette.Space);

        spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        _starfield.Draw(spriteBatch, pixel, Camera);
        spriteBatch.End();

        // Our ship, turning on the spot in front of the title
        _renderer.Begin();
        _renderer.DrawShip(
            _ship,
            new System.Numerics.Vector3(0, 0, ShipCatalog.ViewerDistance(_ship)),
            ShipOrientation.FromEliteOrientation(_orientation),
            Camera,
            Palette.Hull,
            visibilityDistance: float.MaxValue,
            Palette.StationSlot);
        _renderer.End();

        DrawMenu(spriteBatch);
    }

    private void DrawMenu(SpriteBatch spriteBatch)
    {
        // Everything is laid out from the view so the title fills the screen at any window size
        int titleScale = Math.Max(2, (int)(Camera.ViewportHeight / 90f));
        int scale = Math.Max(1, titleScale / 3);
        int line = (int)(Camera.ViewportHeight * 0.07f);

        spriteBatch.Begin(samplerState: SamplerState.PointClamp);

        // The wordmark, with a wide gap between the letters as the original's logo has
        _text.DrawCentred(
            spriteBatch,
            "E L I T E",
            (int)Camera.CentreX,
            line,
            titleScale,
            Palette.White);

        line += TextRenderer.CellHeight(titleScale) * 2;

        _text.DrawCentred(
            spriteBatch,
            "A REMAKE OF THE BBC DISC VERSION",
            (int)Camera.CentreX,
            line,
            scale,
            Palette.Cyan);

        // The menu sits low in the view, under the ship
        int optionLine = (int)(Camera.ViewportHeight * 0.66f);
        int optionHeight = TextRenderer.CellHeight(scale) + (scale * 3);

        IReadOnlyList<string> options = Options;
        for (int i = 0; i < OptionCount; i++)
        {
            bool chosen = i == Math.Clamp(_selected, 0, OptionCount - 1);
            string label = chosen ? $"> {options[i]} <" : options[i];
            _text.DrawCentred(
                spriteBatch,
                label,
                (int)Camera.CentreX,
                optionLine,
                scale,
                chosen ? Palette.Yellow : Palette.White);
            optionLine += optionHeight;
        }

        int hintLine = (int)Camera.ViewportHeight - (TextRenderer.CellHeight(scale) * 3);
        if (_message.Length > 0)
        {
            _text.DrawCentred(spriteBatch, _message, (int)Camera.CentreX, hintLine, scale, Palette.Yellow);
            hintLine += TextRenderer.CellHeight(scale) + (scale * 2);
        }

        _text.DrawCentred(
            spriteBatch,
            "UP/DOWN SELECT   ENTER CHOOSE   M MUSIC",
            (int)Camera.CentreX,
            hintLine,
            scale,
            Palette.Cyan);

        // The version, in the corner where a bug report can find it. It is read off the assembly
        // rather than written here, so it cannot fall behind the build
        _text.Draw(
            spriteBatch,
            Core.GameVersion.Display,
            scale * 4,
            (int)Camera.ViewportHeight - TextRenderer.CellHeight(scale) - (scale * 2),
            scale,
            Palette.Cyan);

        spriteBatch.End();
    }

    public string Name => "Title";

    public string StatusLine =>
        $"Title: version {Core.GameVersion.Number}, selected {SelectedOption}, " +
        $"music {(_settings.Music ? "on" : "off")} " +
        $"({(_sound is { MusicPlaying: true } ? "playing" : "silent")}), " +
        $"save {(GameSession.HasSave ? "present" : "absent")}";
}
