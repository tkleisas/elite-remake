using EliteRemake.Core.Graphics;
using EliteRemake.Core.Sim;
using EliteRemake.Game.Rendering;
using EliteRemake.Game.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

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
        Console.WriteLine($"Loaded {_sound.SoundCount} sounds");
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
    }

    /// <summary>Builds (once) and returns the scene for the session's current mode.</summary>
    private IScene CreateSceneForMode()
    {
        // The ship viewer replaces the game entirely, so it takes priority over the session's mode
        if (_options.ViewerShip is not null)
        {
            return _viewerScene ??= SceneFactory.CreateViewer(_options, GraphicsDevice, Camera);
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
        if (Microsoft.Xna.Framework.Input.Keyboard.GetState().IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Escape))
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

        // The session decides whether we are flying or docked; follow it
        IScene wanted = CreateSceneForMode();
        if (!ReferenceEquals(wanted, _scene))
        {
            _scene = wanted;

            if (_scene is Scenes.FlightScene flightSounds)
            {
                flightSounds.Sounds = _sound;
            }

            Console.WriteLine(_scene.StatusLine);
        }

        var updateClock = System.Diagnostics.Stopwatch.StartNew();
        if (!_options.Paused)
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
            Exit();
        }

        if (_options.ExitAfterFrames is { } exitAfter && _frame >= exitAfter)
        {
            ReportFrameRate();
            Exit();
        }
    }

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
        _spriteBatch.Begin();

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
