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
        _sim = sim;
        _hud = hud;
        Camera = camera;
    }

    /// <summary>Draws the planet and the sun, as well as the ships.</summary>
    public CelestialRenderer Celestial => _celestial;

    /// <summary>The simulation this scene is driving.</summary>
    public FlightSim Sim => _sim;

    public ViewCamera Camera { get; }

    /// <summary>The last input read, exposed for diagnostics.</summary>
    public FlightInput LastInput { get; private set; }

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
        LastInput = ReadInput();

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

        return new FlightInput(left, right, pullUp, pitchDown, speedUp, slowDown);
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

        _hud.Draw(spriteBatch, pixel, Camera, _sim);
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
