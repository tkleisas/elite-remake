using EliteRemake.Core.Graphics;
using EliteRemake.Core.Sim;
using EliteRemake.Core.Ships;
using EliteRemake.Game.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace EliteRemake.Game.Scenes;

/// <summary>
/// The flight scene: our ship at the centre of the universe, flying with the original's controls.
/// </summary>
/// <remarks>
/// The simulation runs at the original's frame rate of 50 Hz, with rendering free to run at
/// whatever rate the display offers. The scene owns the translation from modern input (keyboard and
/// gamepad) into the original's flight controls, and draws the local bubble with the same
/// projection the original used.
/// </remarks>
public sealed class FlightScene : IScene
{
    /// <summary>The original's frame rate: 50 frames a second.</summary>
    public const float FrameRate = 50f;

    private const float FrameTime = 1f / FrameRate;

    private readonly MeshRenderer _renderer;
    private readonly CelestialRenderer _celestial;
    private readonly Starfield _starfield = new();
    private readonly Texture2D _explosionDisc;
    private readonly FlightSim _sim;
    private readonly HudRenderer _hud;
    private readonly Dictionary<string, ShipMesh> _meshes = [];
    private float _accumulator;
    private bool _stationSpawned;
    private bool _dockingRequested;

    public FlightScene(GraphicsDevice device, ViewCamera camera, FlightSim sim, HudRenderer hud)
    {
        _renderer = new MeshRenderer(device);
        _celestial = new CelestialRenderer(device);
        _explosionDisc = CreateExplosionDisc(device);
        _sim = sim;
        _hud = hud;
        Camera = camera;
    }

    /// <summary>Draws the planet and the sun, as well as the ships.</summary>
    public CelestialRenderer Celestial => _celestial;

    /// <summary>Builds a soft disc for explosion clouds.</summary>
    private static Texture2D CreateExplosionDisc(GraphicsDevice device)
    {
        const int size = 128;
        var pixels = new Color[size * size];
        float radius = size / 2f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - (radius - 0.5f);
                float dy = y - (radius - 0.5f);
                float distance = MathF.Sqrt((dx * dx) + (dy * dy)) / radius;
                byte alpha = (byte)(Math.Clamp(1f - distance, 0f, 1f) * 220);
                pixels[(y * size) + x] = new Color((byte)255, (byte)255, (byte)255, alpha);
            }
        }

        var texture = new Texture2D(device, size, size);
        texture.SetData(pixels);
        return texture;
    }

    /// <summary>The simulation this scene is driving.</summary>
    public FlightSim Sim => _sim;

    public ViewCamera Camera { get; }

    /// <summary>The last input read, exposed for diagnostics.</summary>
    public FlightInput LastInput { get; private set; }

    /// <summary>
    /// Controls to hold down in every frame, on top of whatever the keyboard says. This is what
    /// lets a screenshot show the result of flying or firing without a human at the controls.
    /// </summary>
    public FlightInput HeldInput { get; set; }

    public string StatusLine
    {
        get
        {
            var text = new System.Text.StringBuilder();
            text.Append(
                $"Flight: speed {_sim.Speed}, roll rate {_sim.RollRate}, pitch rate {_sim.PitchRate}, " +
                $"{_sim.Bubble.Count} object(s) in the bubble, drawn triangles {_renderer.LastTriangleCount}");

            foreach (Ship ship in _sim.Bubble)
            {
                (int x, int y, int z) = ship.GetPosition();
                double distance = Math.Sqrt(((double)x * x) + ((double)y * y) + ((double)z * z));
                string kind = ship.Type switch
                {
                    ShipTypes.Sun => "sun",
                    SystemArrival.PlanetTypeA or SystemArrival.PlanetTypeB => "planet",
                    _ => "ship",
                };
                double radius = z > 0 ? Camera.FocalLength * SystemArrival.BodyRadius / z : 0;
                text.Append(
                    $"\n  {kind} type {ship.Type} '{ship.Name}' at ({x}, {y}, {z}) distance {distance:0} " +
                    $"screen radius {(IsCelestial(ship.Type) ? radius : 0):0.0}");
            }

            return text.ToString();
        }
    }

    /// <summary>Registers the meshes the scene can draw, keyed by blueprint id.</summary>
    public void RegisterMesh(string blueprintId, ShipMesh mesh) => _meshes[blueprintId] = mesh;

    /// <summary>
    /// Places the planet and the sun for a system, as arriving in it does. Their colours come from
    /// the system's seeds, so every system looks a little different.
    /// </summary>
    public void ArriveInSystem(EliteRemake.Core.Universe.StarSystem system)
    {
        _system = system;
        SystemArrival.AddSystemBodies(_sim, system);

        // Derive a muted colour for the planet from the seeds, so systems differ but stay tasteful
        int hue = (system.Seeds.S2Lo * 360) / 256;
        _celestial.PlanetColour = FromHue(hue, 0.30f, 0.80f);
    }

    private EliteRemake.Core.Universe.StarSystem? _system;

    /// <summary>Converts a hue, saturation and value into a colour.</summary>
    private static Color FromHue(float hue, float saturation, float value)
    {
        float c = value * saturation;
        float x = c * (1 - MathF.Abs(((hue / 60f) % 2) - 1));
        float m = value - c;
        (float r, float g, float b) = hue switch
        {
            < 60 => (c, x, 0f),
            < 120 => (x, c, 0f),
            < 180 => (0f, c, x),
            < 240 => (0f, x, c),
            < 300 => (x, 0f, c),
            _ => (c, 0f, x),
        };

        return new Color(r + m, g + m, b + m);
    }

    /// <summary>Places the space station ahead of us, as the original does when we arrive in a system.</summary>
    public void SpawnStationAhead(int distance = 3000)
    {
        if (_stationSpawned)
        {
            return;
        }

        var station = Ship.Create(ShipTypes.Coriolis, "coriolis", "Coriolis space station", 0, 0, 0, 0, distance);
        SceneFactory.ApplyBlueprint(station);
        _sim.Spawn(station);
        _stationSpawned = true;
    }

    /// <summary>
    /// Runs the simulation for a number of frames with a fixed input, so a screenshot can show the
    /// result of flying for a while without a human at the controls.
    /// </summary>
    public void Warmup(int frames, FlightInput input)
    {
        HeldInput = input;

        for (int i = 0; i < frames; i++)
        {
            _sim.Step(input);
        }

        LastInput = input;
        _starfield.Update(_sim.Speed * frames);
    }

    /// <summary>The session this scene is flying in, so docking can be requested.</summary>
    public GameSession? Session { get; set; }

    public void Update(float elapsedSeconds)
    {
        LastInput = ReadInput() with
        {
            RollLeft = ReadInput().RollLeft || HeldInput.RollLeft,
            RollRight = ReadInput().RollRight || HeldInput.RollRight,
            PullUp = ReadInput().PullUp || HeldInput.PullUp,
            PitchDown = ReadInput().PitchDown || HeldInput.PitchDown,
            SpeedUp = ReadInput().SpeedUp || HeldInput.SpeedUp,
            SlowDown = ReadInput().SlowDown || HeldInput.SlowDown,
            Fire = ReadInput().Fire || HeldInput.Fire,
        };

        // Docking is a debug shortcut for now: flying into the station's slot comes with the
        // docking milestone
        if (Session is not null &&
            Microsoft.Xna.Framework.Input.Keyboard.GetState().IsKeyDown(Microsoft.Xna.Framework.Input.Keys.D) &&
            !_dockingRequested)
        {
            _dockingRequested = true;
            Session.Dock();
        }
        else if (Session is not null &&
                 !Microsoft.Xna.Framework.Input.Keyboard.GetState().IsKeyDown(Microsoft.Xna.Framework.Input.Keys.D))
        {
            _dockingRequested = false;
        }

        // Run the simulation at the original's fixed rate, so the ported maths stays in its
        // original units however fast the display refreshes
        _accumulator += Math.Min(elapsedSeconds, 0.25f);
        int steps = 0;
        while (_accumulator >= FrameTime && steps < 10)
        {
            _sim.Step(LastInput);
            _accumulator -= FrameTime;
            steps++;
        }

        _starfield.Update(_sim.Speed * steps);
    }

    /// <summary>Maps the modern keyboard and gamepad onto the original's flight controls.</summary>
    private static FlightInput ReadInput()
    {
        KeyboardState keys = Keyboard.GetState();
        GamePadState pad = GamePad.GetState(PlayerIndex.One);

        bool left = keys.IsKeyDown(Keys.OemComma) || keys.IsKeyDown(Keys.Left) ||
                    pad.DPad.Left == ButtonState.Pressed || pad.ThumbSticks.Left.X < -0.4f;
        bool right = keys.IsKeyDown(Keys.OemPeriod) || keys.IsKeyDown(Keys.Right) ||
                     pad.DPad.Right == ButtonState.Pressed || pad.ThumbSticks.Left.X > 0.4f;
        bool pullUp = keys.IsKeyDown(Keys.X) || keys.IsKeyDown(Keys.Up) ||
                      pad.DPad.Up == ButtonState.Pressed || pad.ThumbSticks.Left.Y > 0.4f;
        bool pitchDown = keys.IsKeyDown(Keys.S) || keys.IsKeyDown(Keys.Down) ||
                         pad.DPad.Down == ButtonState.Pressed || pad.ThumbSticks.Left.Y < -0.4f;
        bool speedUp = keys.IsKeyDown(Keys.Space) || pad.Buttons.A == ButtonState.Pressed;
        // The original's slow-down key is "?", with "/" accepted as the unshifted equivalent
        bool slowDown = keys.IsKeyDown(Keys.OemQuestion) || keys.IsKeyDown(Keys.Divide) ||
                        pad.Buttons.B == ButtonState.Pressed;

        // "A" fires the lasers, as it does in the original
        bool fire = keys.IsKeyDown(Keys.A) || pad.Buttons.RightShoulder == ButtonState.Pressed;

        return new FlightInput(left, right, pullUp, pitchDown, speedUp, slowDown, fire);
    }

    public void Draw(SpriteBatch spriteBatch, Texture2D pixel, GraphicsDevice device)
    {
        device.Clear(Palette.Space);

        // Stars first: they are the backdrop, and the original draws them before the ships
        spriteBatch.Begin();
        _starfield.Draw(spriteBatch, pixel, Camera);
        spriteBatch.End();

        // The planet and the sun are circles rather than models, and they are so large and distant
        // that they are always behind the ships, so they are drawn first as the backdrop
        spriteBatch.Begin();
        foreach (Ship body in _sim.Bubble)
        {
            if (!IsCelestial(body.Type))
            {
                continue;
            }

            (int bx, int by, int bz) = body.GetPosition();
            _celestial.Draw(
                spriteBatch,
                Camera,
                new System.Numerics.Vector3(bx, by, bz),
                body.Type == ShipTypes.Sun,
                body.Type == SystemArrival.PlanetTypeB ? 1f : 0f);
        }

        spriteBatch.End();

        _renderer.Begin();
        foreach (Ship ship in _sim.Bubble)
        {
            if (IsCelestial(ship.Type))
            {
                continue;
            }

            if (!_meshes.TryGetValue(ship.BlueprintId, out ShipMesh? mesh))
            {
                continue;
            }

            (int x, int y, int z) = ship.GetPosition();
            if (z <= 0)
            {
                continue; // behind us
            }

            _renderer.DrawShip(
                mesh,
                new System.Numerics.Vector3(x, y, z),
                ShipOrientation.FromEliteOrientation(ship.Orientation),
                Camera,
                ColourFor(ship),
                ship.VisibilityDistance);
        }

        _renderer.End();

        spriteBatch.Begin();
        DrawLaserBeam(spriteBatch, pixel);
        DrawExplosions(spriteBatch, pixel);
        spriteBatch.End();

        _hud.Draw(spriteBatch, pixel, Camera, _sim);
    }

    /// <summary>
    /// Draws the laser beams. The original draws two lines from the bottom corners of the view to
    /// the middle of the crosshairs, with a random wobble on the x coordinate, so the beams dance
    /// as they fire.
    /// </summary>
    private void DrawLaserBeam(SpriteBatch spriteBatch, Texture2D pixel)
    {
        if (_sim.FiringLaserPower == 0)
        {
            return;
        }

        float centreX = Camera.CentreX;
        float centreY = Camera.CentreY;
        float bottom = Camera.ViewportHeight;
        float wobble = (Random.Shared.Next(0, 8) - 4) * MathF.Max(1f, Camera.ViewportHeight / 192f);

        var colour = _sim.LaserTarget is not null ? Palette.White : Palette.Laser;
        float thickness = MathF.Max(1f, Camera.ViewportHeight / 192f);

        DrawBeam(spriteBatch, pixel, centreX - (Camera.ViewportWidth * 0.25f), bottom, centreX + wobble, centreY, colour, thickness);
        DrawBeam(spriteBatch, pixel, centreX + (Camera.ViewportWidth * 0.25f), bottom, centreX + wobble, centreY, colour, thickness);
    }

    private static void DrawBeam(
        SpriteBatch spriteBatch,
        Texture2D pixel,
        float x0,
        float y0,
        float x1,
        float y1,
        Color colour,
        float thickness)
    {
        float dx = x1 - x0;
        float dy = y1 - y0;
        float length = MathF.Sqrt((dx * dx) + (dy * dy));
        if (length < 1)
        {
            return;
        }

        spriteBatch.Draw(
            pixel,
            new Vector2(x0, y0),
            null,
            colour,
            MathF.Atan2(dy, dx),
            Vector2.Zero,
            new Vector2(length, thickness),
            SpriteEffects.None,
            0);
    }

    /// <summary>
    /// Draws an expanding cloud for each exploding ship, growing as the original's explosion
    /// counter runs down.
    /// </summary>
    private void DrawExplosions(SpriteBatch spriteBatch, Texture2D pixel)
    {
        _ = pixel;
        foreach (Ship ship in _sim.Bubble)
        {
            if (!ship.IsExploding)
            {
                continue;
            }

            (int x, int y, int z) = ship.GetPosition();
            if (z <= 0)
            {
                continue;
            }

            float radius = Camera.FocalLength * (200 + (ship.Energy * 4)) / z;
            if (radius < 1)
            {
                continue;
            }

            var position = new System.Numerics.Vector3(x, y, z);
            if (!Camera.Project(position, out System.Numerics.Vector2 centre))
            {
                continue;
            }

            float size = radius * 2;
            spriteBatch.Draw(
                _explosionDisc,
                new Rectangle((int)(centre.X - radius), (int)(centre.Y - radius), (int)size, (int)size),
                Palette.Explosion);
        }
    }

    private static Color ColourFor(Ship ship) => ship.Type switch
    {
        ShipTypes.Coriolis => Palette.StationHull,
        _ => Palette.Hull,
    };

    /// <summary>True for the planet and the sun, which are drawn as discs.</summary>
    private static bool IsCelestial(int shipType) =>
        shipType is ShipTypes.Sun or SystemArrival.PlanetTypeA or SystemArrival.PlanetTypeB;
}
