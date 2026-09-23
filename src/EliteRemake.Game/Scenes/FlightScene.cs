using EliteRemake.Core.Graphics;
using EliteRemake.Core.Maths;
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
    private bool _dockingRequested;
    private bool _targetPressed;
    private bool _missilePressed;
    private bool _ecmPressed;
    private bool _jumpPressed;
    private bool _bombPressed;

    /// <summary>
    /// Runs the original's docking checks: fly through the station's slot and we dock, hit the
    /// station anywhere else and we crash.
    /// </summary>
    private void UpdateDocking()
    {
        if (Session is null || Session.Mode != GameMode.Flying || _dockingRequested)
        {
            return;
        }

        foreach (Ship ship in _sim.Bubble)
        {
            if (ship.Type != Combat.SpaceStationType)
            {
                continue;
            }

            (int x, int y, int z) = ship.GetPosition();

            // The universe turns around us rather than the other way round, so we always face along
            // +z in the world's terms
            // A station we have annoyed will not let us in, which is what ANGRY and AN2 are for: the
            // flag is parsed here rather than passed as a constant, because passing `false` meant the
            // hostile-station check could never fire and shooting an innocent cost nothing.
            DockingResult result = Docking.Check(
                ship,
                (x, y, z),
                new System.Numerics.Vector3(0, 0, 1),
                stationHostile: ship.AiFlag >= 0x80);

            if (result == DockingResult.Docking)
            {
                _dockingRequested = true;
                Session.Dock();
                Sounds?.Play(Core.Audio.SoundEffect.Beep);
                return;
            }

            if (result == DockingResult.Hostile)
            {
                // The station has been annoyed and is refusing us. This is not fatal: the original
                // simply will not open the slot, so we fly on and can try again once our record has
                // improved enough for it to forget.
                Session.Message = $"{ship.Name} will not let us dock.";
                Sounds?.Play(Core.Audio.SoundEffect.Beep);
                return;
            }

            if (result == DockingResult.Collision)
            {
                // Hitting the station anywhere but the slot is fatal
                Session.Flight.ApplyStationCollision();
                Sounds?.Play(Core.Audio.SoundEffect.Explosion);
                return;
            }
        }
    }

    /// <summary>
    /// Plays the sounds for whatever happened this frame, using the original's own triggers: a zap
    /// when we fire, a hit when we are struck, an explosion when something dies, a whoosh for
    /// hyperspace and a buzz for the E.C.M.
    /// </summary>
    private void UpdateSounds()
    {
        if (Sounds is null)
        {
            return;
        }

        if (_sim.FiringLaserPower > 0)
        {
            Sounds.Play(Core.Audio.SoundEffect.LaserFire);
        }

        if (_sim.DamageTakenThisFrame > 0 || _sim.HitByMissile)
        {
            Sounds.Play(Core.Audio.SoundEffect.LaserHit);
        }

        if (_sim.DestroyedThisFrame is not null && Session is not null)
        {
            Sounds.Play(Core.Audio.SoundEffect.Explosion);
        }

        // Our own death has its own two-part sound in the original's table — entries 16 and 24, whose
        // labels are "We died 1 / We made a hit or kill 2" and "We died 2 / We made a hit or kill 1".
        // Nothing was playing either of them, so the commander died in silence.
        // The long, low beep that says the missile is no longer aimed at anything, which the original
        // makes at both points the lock is released
        if (_sim.MissileUnarmedThisFrame)
        {
            Sounds.Play(Core.Audio.SoundEffect.Boop);
        }

        if (_sim.PlayerDied && !_deathSoundPlayed)
        {
            _deathSoundPlayed = true;
            Sounds.Play(Core.Audio.SoundEffect.HitOrDeath);
        }

        // The E.C.M. has a sound for starting and another for running down, and only the first was
        // ever played: the flag went false, the effect was defined, and the two were never connected.
        // A previous-state field is what tells "it has just ended" from "it was never on" — the
        // pending flag cannot, because it is true both before the first firing and after the end.
        if (_sim.EcmActive != _ecmWasActive)
        {
            _ecmWasActive = _sim.EcmActive;
            Sounds.Play(_sim.EcmActive
                ? Core.Audio.SoundEffect.EcmOn
                : Core.Audio.SoundEffect.EcmOff);
        }

        if (Session is { HyperspaceCountdown: > 0 } && !_hyperspaceSoundPlayed)
        {
            _hyperspaceSoundPlayed = true;
            Sounds.Play(Core.Audio.SoundEffect.Hyperspace);
        }
        else if (Session is { HyperspaceCountdown: 0 })
        {
            _hyperspaceSoundPlayed = false;
        }
    }

    private bool _ecmWasActive;
    private bool _deathSoundPlayed;
    private bool _hyperspaceSoundPlayed;

    /// <summary>The nearest system we have the fuel to reach, which is where H takes us.</summary>
    private EliteRemake.Core.Universe.StarSystem NearestReachableSystem()
    {
        EliteRemake.Core.Universe.StarSystem best = Session!.System;
        int bestDistance = int.MaxValue;

        foreach (EliteRemake.Core.Universe.StarSystem candidate in
                 Session.SystemsInGalaxy)
        {
            if (candidate.Seeds == Session.System.Seeds)
            {
                continue;
            }

            int distance = EliteRemake.Core.Universe.Galaxy.DistanceTenths(Session.System, candidate);
            if (distance <= Session.Commander.Fuel && distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>
    /// Arrives in witchspace, where the ambush waits and there is no station to be found.
    /// </summary>
    public void ArriveInWitchspace()
    {
        // Witchspace has no station and no system bodies: only the ambush waiting for us
        _sim.ArriveInWitchspace();
        Sounds?.Play(Core.Audio.SoundEffect.Hyperspace);
    }

    /// <summary>The ship in our crosshairs, which a missile can lock onto.</summary>
    private Ship? FindTargetInCrosshairs()
    {
        foreach (Ship ship in _sim.Bubble)
        {
            if (Combat.IsInCrosshairs(ship, _sim.TargetableAreaOf(ship)))
            {
                return ship;
            }
        }

        return null;
    }

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

    /// <summary>The dashboard, so the game can keep its layout in step with the window.</summary>
    public HudRenderer Hud => _hud;

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
    /// Arrives in a system: the old bubble goes, and the new system's planet, sun and station
    /// arrive. Their colours come from the system's seeds, so every system looks a little
    /// different.
    /// </summary>
    /// <param name="system">The system we have arrived in.</param>
    /// <param name="stationDistance">How far ahead to place the station, in the original's units.</param>
    public void ArriveInSystem(EliteRemake.Core.Universe.StarSystem system, int stationDistance = 3000)
    {
        // The Coriolis gets a random clockwise roll, as the original's main game loop gives it: a
        // random value with bit 7 cleared. It matters for docking, because the slot's orientation
        // changes as the station turns and the docking computer's first phase matches its roll.
        SystemArrival.ArriveInSystem(
            _sim,
            system,
            stationDistance,
            (byte)(Random.Shared.Next(64, 128) & 0x7F));

        _system = system;

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

    /// <summary>The sounds, or null when the game is running silently.</summary>
    public Audio.SoundBank? Sounds { get; set; }

    /// <summary>The player's bindings, so the settings screen can change what the keys do.</summary>
    public Settings Settings { get; set; } = new();

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

        if (Session is not null)
        {
            KeyboardState keys = Microsoft.Xna.Framework.Input.Keyboard.GetState();

            // Run the hyperspace countdown, and arrive when it finishes. A jump that went wrong
            // throws us into witchspace instead, which is a Thargoid ambush and nothing else.
            if (Session.HyperspaceCountdown > 0 && Session.TickHyperspace())
            {
                if (Session.InWitchspace)
                {
                    ArriveInWitchspace();
                }
                else
                {
                    ArriveInSystem(Session.System);
                }
            }

            // T locks the missile onto whatever is in the crosshairs, as the original does
            if (keys.IsKeyDown(Settings.TargetKey) && !_targetPressed)
            {
                _targetPressed = true;
                Session.Flight.MissileLock = FindTargetInCrosshairs();
            }
            else if (!keys.IsKeyDown(Settings.TargetKey))
            {
                _targetPressed = false;
            }

            // M fires a missile, E fires the E.C.M.
            if (keys.IsKeyDown(Settings.MissileKey) && !_missilePressed)
            {
                _missilePressed = true;
                if (Session.Flight.FireMissile())
                {
                    Sounds?.Play(Core.Audio.SoundEffect.Missile);
                }
            }
            else if (!keys.IsKeyDown(Settings.MissileKey))
            {
                _missilePressed = false;
            }

            // H jumps to the nearest system we can reach, until the charts arrive. Holding CTRL
            // as well forces the jump to go wrong, as the original's own mis-jump key does.
            if (keys.IsKeyDown(Settings.HyperspaceKey) && !_jumpPressed)
            {
                _jumpPressed = true;
                if (Session.SelectedSystem.Seeds == Session.System.Seeds)
                {
                    Session.SelectedSystem = NearestReachableSystem();
                }

                Session.ForceMisjump = keys.IsKeyDown(Keys.LeftControl) || keys.IsKeyDown(Keys.RightControl);
                if (!Session.StartHyperspace())
                {
                    Session.ForceMisjump = false;
                }
            }
            else if (!keys.IsKeyDown(Settings.HyperspaceKey))
            {
                _jumpPressed = false;
            }

            // TAB sets off the energy bomb, as the original does
            if (keys.IsKeyDown(Settings.EnergyBombKey) && !_bombPressed)
            {
                _bombPressed = true;
                if (Session.Flight.FireEnergyBomb())
                {
                    Sounds?.Play(Core.Audio.SoundEffect.Explosion);
                }
            }
            else if (!keys.IsKeyDown(Settings.EnergyBombKey))
            {
                _bombPressed = false;
            }

            if (keys.IsKeyDown(Settings.EcmKey) && !_ecmPressed)
            {
                _ecmPressed = true;
                Session.Flight.FireEcm();
            }
            else if (!keys.IsKeyDown(Settings.EcmKey))
            {
                _ecmPressed = false;
            }

            // Destroying a ship pays its bounty and counts the kill, and the energy bomb's
            // victims count too
            if (_sim.DestroyedThisFrame is { } wreck)
            {
                Session.RegisterKill(wreck);
                _sim.DestroyedThisFrame = null;
            }

            for (int i = 1; i < _sim.BombKillsThisFrame; i++)
            {
                Session.Commander.RegisterKill(); // the rest of the bomb's victims
            }
        }

        UpdateSounds();
        UpdateDocking();

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
            steps++;        }

        _starfield.Update(_sim.Speed * steps);

        // Spawn whatever the last destroyed ship left behind
        if (_sim.DropsThisFrame.Count > 0 && Session is not null)
        {
            for (int i = 0; i < _sim.DropsThisFrame.Count; i++)
            {
                var drop = new Ship(_sim.DropsThisFrame.Type, string.Empty, $"Type {_sim.DropsThisFrame.Type}");
                if (_sim.DestroyedThisFrame is { } wreck)
                {
                    (int x, int y, int z) = wreck.GetPosition();
                    drop.SetPosition(x + (i * 64), y, z);
                }

                _sim.Spawn(drop);
            }
        }
    }

    /// <summary>
    /// Maps the keyboard and gamepad onto the original's flight controls.
    /// </summary>
    /// <remarks>
    /// The arrow keys and the gamepad stay alongside the bound keys rather than being rebindable:
    /// the original has two keys for each of the four directions and the arrows are one of them, so
    /// rebinding the other should not take the arrows away.
    /// </remarks>
    private FlightInput ReadInput()
    {
        KeyboardState keys = Keyboard.GetState();
        GamePadState pad = GamePad.GetState(PlayerIndex.One);

        bool left = keys.IsKeyDown(Settings.RollLeftKey) || keys.IsKeyDown(Keys.Left) ||
                    pad.DPad.Left == ButtonState.Pressed || pad.ThumbSticks.Left.X < -0.4f;
        bool right = keys.IsKeyDown(Settings.RollRightKey) || keys.IsKeyDown(Keys.Right) ||
                     pad.DPad.Right == ButtonState.Pressed || pad.ThumbSticks.Left.X > 0.4f;
        bool pullUp = keys.IsKeyDown(Settings.PullUpKey) || keys.IsKeyDown(Keys.Up) ||
                      pad.DPad.Up == ButtonState.Pressed || pad.ThumbSticks.Left.Y > 0.4f;
        bool pitchDown = keys.IsKeyDown(Settings.PitchDownKey) || keys.IsKeyDown(Keys.Down) ||
                         pad.DPad.Down == ButtonState.Pressed || pad.ThumbSticks.Left.Y < -0.4f;
        bool speedUp = keys.IsKeyDown(Settings.SpeedUpKey) || pad.Buttons.A == ButtonState.Pressed;

        // The original's slow-down key is "?", with "/" accepted as the unshifted equivalent
        bool slowDown = keys.IsKeyDown(Settings.SlowDownKey) || keys.IsKeyDown(Keys.Divide) ||
                        pad.Buttons.B == ButtonState.Pressed;

        bool fire = keys.IsKeyDown(Settings.FireKey) || pad.Buttons.RightShoulder == ButtonState.Pressed;

        return new FlightInput(left, right, pullUp, pitchDown, speedUp, slowDown, fire);
    }

    /// <summary>
    /// Draws the hyperspace tunnel for this many drawn frames as well as while a jump counts down.
    /// </summary>
    /// <remarks>
    /// The countdown runs in simulation steps, of which the shell takes more than one for each frame
    /// it draws, so a twenty-step countdown is over well before a frame-six screenshot. This holds
    /// the tunnel open by drawn frames instead, which is what the screenshot harness needs.
    /// </remarks>
    public int ShowTunnelFrames { get; set; }

    private int _drawnFrames;

    public void Draw(SpriteBatch spriteBatch, Texture2D pixel, GraphicsDevice device)
    {
        _drawnFrames++;
        device.Clear(Palette.Space);

        // Stars first: they are the backdrop, and the original draws them before the ships
        spriteBatch.Begin();
        _starfield.Draw(spriteBatch, pixel, Camera);
        spriteBatch.End();

        // The hyperspace tunnel covers everything while the drive is counting down, as the
        // original's LL164 clears the screen and draws its rings over the top
        if (Session is { HyperspaceCountdown: > 0 } || (ShowTunnelFrames > 0 && _drawnFrames < ShowTunnelFrames))
        {
            spriteBatch.Begin();
            HyperspaceTunnel.Draw(spriteBatch, pixel, Camera, Palette.White);
            spriteBatch.End();
            _hud.Draw(spriteBatch, pixel, Camera, _sim);
            return;
        }

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
                ship.VisibilityDistance,
                ship.Type == ShipTypes.Coriolis ? Palette.StationSlot : null,
                ship.Type == ShipTypes.Coriolis ? Palette.StationSlotLip : null);
        }

        _renderer.End();

        spriteBatch.Begin();
        DrawLaserBeam(spriteBatch, pixel);
        DrawExplosions(spriteBatch, pixel);
        spriteBatch.End();

        _hud.MissilesArmed = Session?.Commander.Missiles ?? 0;
        _hud.Locked = _sim.MissileLock is not null;
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
