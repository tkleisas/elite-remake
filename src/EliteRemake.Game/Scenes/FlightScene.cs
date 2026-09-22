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
    private readonly Starfield _starfield = new();
    private readonly FlightSim _sim;
    private readonly HudRenderer _hud;
    private readonly Dictionary<string, ShipMesh> _meshes = [];
    private float _accumulator;
    private bool _stationSpawned;

    public FlightScene(GraphicsDevice device, ViewCamera camera, FlightSim sim, HudRenderer hud)
    {
        _renderer = new MeshRenderer(device);
        _sim = sim;
        _hud = hud;
        Camera = camera;
    }

    /// <summary>The simulation this scene is driving.</summary>
    public FlightSim Sim => _sim;

    public ViewCamera Camera { get; }

    /// <summary>The last input read, exposed for diagnostics.</summary>
    public FlightInput LastInput { get; private set; }

    public string StatusLine =>
        $"Flight: speed {_sim.Speed}, roll rate {_sim.RollRate}, pitch rate {_sim.PitchRate}, " +
        $"{_sim.Bubble.Count} object(s) in the bubble, drawn triangles {_renderer.LastTriangleCount}";

    /// <summary>Registers the meshes the scene can draw, keyed by blueprint id.</summary>
    public void RegisterMesh(string blueprintId, ShipMesh mesh) => _meshes[blueprintId] = mesh;

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

    public void Update(float elapsedSeconds)
    {
        LastInput = ReadInput();

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

        _renderer.Begin();
        foreach (Ship ship in _sim.Bubble)
        {
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
}
