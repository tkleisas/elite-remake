using EliteRemake.Core.Graphics;
using EliteRemake.Core.Sim;
using EliteRemake.Game.Rendering;
using EliteRemake.Game.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace EliteRemake.Game;

/// <summary>
/// The game shell: window, frame loop, screenshot support and the current scene.
/// </summary>
public sealed class EliteGame : Microsoft.Xna.Framework.Game
{
    private readonly GameOptions _options;
    private readonly GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch = null!;
    private Texture2D _pixel = null!;
    private TextRenderer _text = null!;
    private Audio.SoundBank _sound = null!;
    private IScene _scene = null!;
    private TitleScene? _titleScene;
    private FlightScene? _flightScene;
    private MarketScene? _marketScene;
    private EquipmentScene? _equipmentScene;
    private ChartScene? _shortChartScene;
    private ChartScene? _longChartScene;
    private DataScene? _dataScene;
    private StatusScene? _statusScene;
    private GameSession _session = null!;
    private int _frame;
    private bool _screenshotWritten;

    public EliteGame(GameOptions options)
    {
        _options = options;

        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = options.Width,
            PreferredBackBufferHeight = options.Height,
            SynchronizeWithVerticalRetrace = true,
            PreferredDepthStencilFormat = DepthFormat.Depth24,
        };

        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
        Window.Title = "Elite";
    }

    /// <summary>The view camera, sized to the current space view.</summary>
    public ViewCamera Camera { get; private set; } = null!;

    /// <summary>Where the space view and the dashboard sit in the window.</summary>
    public ScreenLayout Layout { get; private set; }

    protected override void LoadContent()
    {
        // MonoGame calls LoadContent from Initialize, so the graphics device is ready here but
        // nothing else should be assumed about ordering
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData([Color.White]);

        Layout = ScreenLayout.ForWindow(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
        Camera = CreateCamera();
        _text = new TextRenderer(GraphicsDevice, Data.FontData.Font);
        _sound = new Audio.SoundBank();
        _sound.Load();
        _sound.LoadMusic();
        Console.WriteLine($"Loaded {_sound.SoundCount} sounds and the music");
        _session = SceneFactory.CreateSession(_options);
        _scene = CreateSceneForMode();

        if (_scene is Scenes.FlightScene sounds)
        {
            sounds.Sounds = _sound;
        }

        if (_options.SimWarmupFrames > 0 && _scene is Scenes.FlightScene flight)
        {
            flight.Warmup(_options.SimWarmupFrames, _options.WarmupInput);
        }

        // --in-system-jump: the "J" key's own mechanic, from the command line. It runs after the
        // warmup, because a jump needs an empty sky: the station is in the bubble until we have
        // flown far enough away from it, which is exactly the original's rule.
        if (_options.InSystemJumps > 0 && _scene is Scenes.FlightScene jumping)
        {
            for (int i = 0; i < _options.InSystemJumps; i++)
            {
                Console.WriteLine(jumping.InSystemJump()
                    ? $"In-system jump {i + 1} made."
                    : $"In-system jump {i + 1} refused.");
            }
        }
    }

    /// <summary>Builds (once) and returns the scene for the session's current mode.</summary>
    private IScene CreateSceneForMode()
    {
        // The ship viewer replaces the game entirely, so it takes priority over the session's mode
        if (_options.ViewerShip is not null)
        {
            return _viewerScene ??= SceneFactory.CreateViewer(_options, GraphicsDevice, Camera);
        }

        // --buy with a laser and no view wanted to see the shop's "Which view?" prompt rather than
        // answer it, so the equipment screen opens with the question already on it
        if (_options.BuyLaserItem >= 0 &&
            _session.Mode == GameMode.Docked &&
            _session.Screen == DockedScreen.Equipment)
        {
            _equipmentScene ??= new EquipmentScene(Camera, _session, _text);
            _equipmentScene.AskWhichView(_options.BuyLaserItem);
            _options.BuyLaserItem = -1;
            return _equipmentScene;
        }

        if (_session.Mode == GameMode.Title)
        {
            if (_titleScene is null)
            {
                _titleScene = (TitleScene)SceneFactory.CreateTitle(
                    _session, GraphicsDevice, Camera, _text, _settings, _sound, _options.TitleSettings);

                // The waltz plays on the start screen and nowhere else: the flight loop owns the
                // sound chip, and the original's own sounds would be fighting it
                if (_settings.Music)
                {
                    _sound.PlayMusic();
                }
            }

            return _titleScene;
        }

        if (_session.Mode == GameMode.Docked)
        {
            return _session.Screen switch
            {
                DockedScreen.Equipment => _equipmentScene ??= new EquipmentScene(Camera, _session, _text),
                DockedScreen.ShortRangeChart => _shortChartScene ??= OpenChart(ChartRange.Short),
                DockedScreen.LongRangeChart => _longChartScene ??= OpenChart(ChartRange.Long),
                DockedScreen.DataOnSystem => _dataScene ??= new DataScene(Camera, _session, _text),
                DockedScreen.Status => _statusScene ??= new StatusScene(Camera, _session, _text, _flightScene),
                DockedScreen.Settings => _settingsScene ??= new SettingsScene(Camera, _session, _text, _settings),
                _ => _marketScene ??= new MarketScene(Camera, _session, _text),
            };
        }

        return _flightScene ??= SceneFactory.CreateFlightScene(
            _options, GraphicsDevice, Camera, Layout, _text, _session, _settings);
    }

    /// <summary>
    /// Tells the dashboard where the window has put the space view and the dashboard band. It is
    /// built with the layout that was current when the scene was created, so it has to be told
    /// whenever the window changes size or it carries on drawing at the old size and in the old
    /// place - which leaves it in the top-left corner when the window is maximised.
    /// </summary>
    private void SyncDashboardLayout()
    {
        if (_flightScene is { } flight)
        {
            flight.Hud.Layout = Layout;
        }
    }

    private IScene? _viewerScene;
    private IScene? _settingsScene;

    /// <summary>The player's bindings, loaded once so the screens and the flight scene share them.</summary>
    private readonly Settings _settings = Settings.Load();

    /// <summary>Builds a chart scene, with the crosshairs starting on the current system.</summary>
    private ChartScene OpenChart(ChartRange range)
    {
        var scene = new ChartScene(Camera, _session, _text, range);
        scene.SelectSystem(_session.System);
        return scene;
    }

    /// <summary>Keeps the projection and the dashboard in step with the window size.</summary>
    private void UpdateCameraToViewport()
    {
        var viewport = GraphicsDevice.Viewport;
        ScreenLayout layout = ScreenLayout.ForWindow(viewport.Width, viewport.Height);
        if (layout.View != Layout.View || layout.Dashboard != Layout.Dashboard)
        {
            Layout = layout;
        }

        SyncDashboardLayout();

        Rectangle view = Layout.View;
        if (Math.Abs(Camera.ViewportWidth - view.Width) > 0.5f ||
            Math.Abs(Camera.ViewportHeight - view.Height) > 0.5f)
        {
            Camera.Resize(view.Width, view.Height, view.Width / 2f, view.Height / 2f);
        }
    }

    protected override void UnloadContent()
    {
        _sound?.Dispose();
        _text?.Dispose();
        _pixel?.Dispose();
        _spriteBatch?.Dispose();
        base.UnloadContent();
    }

    private ViewCamera CreateCamera()
    {
        Rectangle view = Layout.View;
        return new ViewCamera(
            view.Width,
            view.Height,
            view.Width / 2f,
            view.Height / 2f);
    }

    protected override void Update(GameTime gameTime)
    {
        // ESCAPE leaves the game except in flight, where the original launches an escape pod with it
        // and a faithful remake has to let it; F10 leaves from anywhere, and the game-over screen
        // still answers to ESCAPE because there is no ship left to leave.
        KeyboardState pressed = Microsoft.Xna.Framework.Input.Keyboard.GetState();
        bool inFlight = _session.Mode == GameMode.Flying && !_session.GameOver;
        if (pressed.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.F10) ||
            (pressed.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Escape) && !inFlight))
        {
            Exit();
        }

        UpdateCameraToViewport();

        // Being destroyed resolves to the station (with an escape pod) or to game over
        if (_session.Flight.PlayerDied && _session.Mode == GameMode.Flying)
        {
            _session.Flight.PlayerDied = false;
            _session.Flight.Player.Energy = 150;
            _session.HandlePlayerDeath();
        }

        // The start screen's quit option leaves the game
        if (_titleScene is { QuitRequested: true })
        {
            Exit();
            return;
        }

        // The session decides whether we are flying or docked; follow it
        IScene wanted = CreateSceneForMode();
        if (!ReferenceEquals(wanted, _scene))
        {
            if (_scene is TitleScene)
            {
                _sound.StopMusic();
            }

            _scene = wanted;

            if (_scene is Scenes.FlightScene flightSounds)
            {
                flightSounds.Sounds = _sound;
            }

            Console.WriteLine(_scene.StatusLine);
        }

        // The game is over: the wreck is left where it fell and nothing else happens until the
        // player asks for another go. The original's DEATH routine stops the flight loop and shows
        // the GAME OVER screen, and without this the simulation carried on around a dead commander —
        // the pirates that killed us kept firing, and our energy was quietly put back to a full bank
        // so the wreck could be shot at indefinitely.
        if (_session.GameOver && _session.Mode == GameMode.Flying)
        {
            HandleGameOverKeys();
        }

        // The original's own docked keys work from every docked screen, so the three that our merged
        // screens answer to are handled here rather than once in each of them: f1 buys cargo, f2
        // sells cargo and f3 shows the equipment shop, which is where the original puts them. The
        // rest — f4 and f5 for the charts, f6 for the data, f7 for the market prices, f8 for the
        // status screen and f9 for the inventory — are each screen's own business.
        if (_session.Mode == GameMode.Docked && _scene is not TitleScene)
        {
            HandleDockedFunctionKeys();
        }

        var updateClock = System.Diagnostics.Stopwatch.StartNew();
        if (!_options.Paused && !(_session.GameOver && _session.Mode == GameMode.Flying))
        {
            _scene.Update((float)gameTime.ElapsedGameTime.TotalSeconds);
        }

        _updateTicks += updateClock.ElapsedTicks;

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _scene.Draw(_spriteBatch, _pixel, GraphicsDevice);
        _drawTicks += sw.ElapsedTicks;

        base.Draw(gameTime);

        _frame++;
        if (_framesDrawn == 0)
        {
            _clock.Restart();
        }

        if (_frame == 2)
        {
            Console.WriteLine(_scene.StatusLine);
        }

        _framesDrawn++;

        if (_options.PrintFontSheet && _frame == 3)
        {
            WriteFontSheet();

            if (_options.ScreenshotPath is { } sheetPath)
            {
                SaveScreenshot(sheetPath);
            }

            Exit();
        }

        if (_options.ScreenshotPath is { } path && !_screenshotWritten && _frame >= _options.ScreenshotFrame)
        {
            SaveScreenshot(path);
            _screenshotWritten = true;
            ReportFinalState();
            Exit();
        }

        if (_options.ExitAfterFrames is { } exitAfter && _frame >= exitAfter)
        {
            ReportFrameRate();
            ReportFinalState();
            Exit();
        }
    }

    /// <summary>
    /// Waits for the player to say what happens after they die: the original's GAME OVER screen
    /// offers a new commander or the way out.
    /// </summary>
    /// <summary>
    /// The original's docked keys that every docked screen answers to: f1 and f2 for the market,
    /// which the original splits into a buying screen and a selling one, and f3 for the equipment
    /// shop.
    /// </summary>
    private void HandleDockedFunctionKeys()
    {
        KeyboardState keys = Microsoft.Xna.Framework.Input.Keyboard.GetState();

        if (IsNewPress(keys, Keys.F1) || IsNewPress(keys, Keys.F2))
        {
            _session.Screen = DockedScreen.Market;
        }

        if (IsNewPress(keys, Keys.F3))
        {
            _session.Screen = DockedScreen.Equipment;
        }

        _dockedKeys = keys;
    }

    /// <summary>True the first frame a key goes down, so a held key does not repeat.</summary>
    private bool IsNewPress(KeyboardState keys, Keys key) =>
        keys.IsKeyDown(key) && !_dockedKeys.IsKeyDown(key);

    private KeyboardState _dockedKeys;

    private void HandleGameOverKeys()
    {
        Microsoft.Xna.Framework.Input.KeyboardState keys = Microsoft.Xna.Framework.Input.Keyboard.GetState();

        if (keys.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.N) && !_gameOverKeys.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.N))
        {
            _session.Restart();
            _flightScene = null;
        }

        _gameOverKeys = keys;
    }

    /// <summary>The keys as they were on the previous frame, for the game-over screen.</summary>
    private Microsoft.Xna.Framework.Input.KeyboardState _gameOverKeys;

    /// <summary>
    /// Prints the scene's state as the run ends. The scene also prints its status line when it
    /// changes, which is early on, so without this a headless run says nothing about how it finished:
    /// a screenshot of frame 60 comes with a status line from frame 2, and reading that as the final
    /// state has been wrong more than once.
    /// </summary>
    private void ReportFinalState() => Console.WriteLine($"Final: {_scene.StatusLine}");

    /// <summary>When the first frame was drawn, and how many have been, for the frame-rate report.</summary>
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    private int _framesDrawn;
    private long _updateTicks;
    private long _drawTicks;

    /// <summary>
    /// Prints how long the frames actually took. The display's vertical sync caps this, so the
    /// figure is a floor on the achievable rate rather than a measure of raw throughput: if it
    /// matches the refresh rate then the game is keeping up with the display, and if it does not
    /// then something is too slow to.
    /// </summary>
    private void ReportFrameRate()
    {
        double seconds = _clock.Elapsed.TotalSeconds;
        if (seconds <= 0 || _framesDrawn <= 0)
        {
            return;
        }

        double perFrame = seconds * 1000 / _framesDrawn;
        double fps = _framesDrawn / seconds;
        double updateMs = _updateTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / _framesDrawn;
        double drawMs = _drawTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / _framesDrawn;
        Console.WriteLine(
            $"Drew {_framesDrawn} frames in {seconds:0.00}s: {perFrame:0.00} ms a frame, {fps:0.0} fps " +
            $"(simulation steps run at {Scenes.FlightScene.FrameRate:0} Hz, up to 10 a frame)");
        Console.WriteLine(
            $"  of which our own work: {updateMs:0.00} ms update + {drawMs:0.00} ms draw = " +
            $"{updateMs + drawMs:0.00} ms; the rest is the display");
    }

    /// <summary>
    /// Draws every character the font defines, so the glyphs can be inspected at a glance.
    /// </summary>
    public void WriteFontSheet()
    {
        GraphicsDevice.Clear(new Color(0, 0, 0));
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);

        const string rows =
            " !\"#$%&'()*+,-./0123456789:;<=>?@" +
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_" +
            "`abcdefghijklmnopqrstuvwxyz{|}~";

        int scale = 3;
        int cell = TextRenderer.CellWidth(scale) + 2;
        int columns = 32;
        for (int i = 0; i < rows.Length; i++)
        {
            int x = (i % columns) * cell;
            int y = (i / columns) * (TextRenderer.CellHeight(scale) + 4);
            _text.Draw(_spriteBatch, rows[i].ToString(), x, y, scale, Color.White);
        }

        _spriteBatch.End();
    }

    /// <summary>Writes the current back buffer to a PNG file.</summary>
    private void SaveScreenshot(string path)
    {
        int width = GraphicsDevice.PresentationParameters.BackBufferWidth;
        int height = GraphicsDevice.PresentationParameters.BackBufferHeight;
        var data = new Color[width * height];
        GraphicsDevice.GetBackBufferData(data);

        using var texture = new Texture2D(GraphicsDevice, width, height);
        texture.SetData(data);

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = File.Create(path);
        texture.SaveAsPng(stream, width, height);
        Console.WriteLine($"Wrote {path} ({width}x{height}, frame {_frame})");
    }
}
