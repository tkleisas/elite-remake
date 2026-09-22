using EliteRemake.Core.Graphics;
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
    private IScene _scene = null!;
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
        _scene = SceneFactory.Create(_options, GraphicsDevice, Camera, Layout);

        if (_options.SimWarmupFrames > 0 && _scene is Scenes.FlightScene flight)
        {
            flight.Warmup(_options.SimWarmupFrames, _options.WarmupInput);
        }
    }

    /// <summary>Keeps the projection in step with the window size.</summary>
    private void UpdateCameraToViewport()
    {
        var viewport = GraphicsDevice.Viewport;
        ScreenLayout layout = ScreenLayout.ForWindow(viewport.Width, viewport.Height);
        if (layout.View != Layout.View)
        {
            Layout = layout;
        }

        Rectangle view = Layout.View;
        if (Math.Abs(Camera.ViewportWidth - view.Width) > 0.5f ||
            Math.Abs(Camera.ViewportHeight - view.Height) > 0.5f)
        {
            Camera.Resize(view.Width, view.Height, view.Width / 2f, view.Height / 2f);
        }
    }

    protected override void UnloadContent()
    {
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

        if (!_options.Paused)
        {
            _scene.Update((float)gameTime.ElapsedGameTime.TotalSeconds);
        }

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        _scene.Draw(_spriteBatch, _pixel, GraphicsDevice);
        base.Draw(gameTime);

        _frame++;
        if (_frame == 2)
        {
            Console.WriteLine(_scene.StatusLine);
        }

        if (_options.ScreenshotPath is { } path && !_screenshotWritten && _frame >= _options.ScreenshotFrame)
        {
            SaveScreenshot(path);
            _screenshotWritten = true;
            Exit();
        }

        if (_options.ExitAfterFrames is { } exitAfter && _frame >= exitAfter)
        {
            Exit();
        }
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
