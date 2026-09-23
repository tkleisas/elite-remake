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
    /// <summary>
    /// How often the simulation is stepped. This is the original's own main loop rate, which is
    /// around twelve and a half iterations a second and not the fifty of the BBC's television
    /// refresh — see <see cref="FlightSim.IterationsPerSecond"/> for where the figure comes from.
    /// </summary>
    public const float FrameRate = FlightSim.IterationsPerSecond;

    /// <summary>
    /// The rate actually in use, which is the original's unless the player has asked for another.
    /// </summary>
    /// <remarks>
    /// The rate is what decides how much of the game happens in a second, because everything in the
    /// original happens once per iteration of its main loop. Running faster makes the whole game
    /// faster — which is exactly what a fixed rate of fifty did, and why the station span four times
    /// too quickly. It is adjustable because the original's own rate was not a constant either: it
    /// fell to four or five iterations a second in a crowded fight and reached thirty-six when there
    /// was nothing to draw.
    /// </remarks>
    public float Rate { get; set; } = FrameRate;

    private float FrameTime => 1f / Rate;

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
    private bool _unarmPressed;
    private bool _inSystemJumpPressed;
    private bool _escapePodPressed;
    private bool _cancelDockingPressed;

    /// <summary>
    /// Runs the original's docking checks: fly through the station's slot and we dock, hit the
    /// station anywhere else and we crash.
    /// </summary>
    private void UpdateDocking()
    {
        if (Session is null || Session.Mode != GameMode.Flying || _dockingRequested || DockingSequenceRunning)
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
            // A station we have annoyed will not let us in, which is what ANGRY and AN2 are for.
            //
            // Hostility is the NEWB flag, not the AI flag: on this build bit 7 of the AI flag means
            // only that the ship has AI, and every station carries it — NWSPS gives the station
            // %10000001 — so asking the AI flag would refuse docking at every station in the galaxy.
            // AN2 sets bit 2 of the NEWB flags, which is what this asks.
            DockingResult result = Docking.Check(
                ship,
                (x, y, z),
                new System.Numerics.Vector3(0, 0, 1),
                stationHostile: ship.IsHostile);

            if (result == DockingResult.Docking)
            {
                _dockingRequested = true;
                _dockTunnelFrames = DockTunnelFrames;
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

    /// <summary>
    /// The original's name for a scooped commodity, which it prints from recursive tokens 48 to 64:
    /// "FOOD" for the first of the market's items and "ALIEN ITEMS" for the last.
    /// </summary>
    private static string ItemName(int item) =>
        item >= 0 && item < EliteRemake.Core.Universe.Market.Items.Length
            ? EliteRemake.Core.Universe.Market.Items[item].Name.ToUpperInvariant()
            : "SCOOPED";

    /// <summary>
    /// The ship in our crosshairs, which a missile can lock onto: the middle of the window we are
    /// looking through, so a missile can be locked onto something behind us from the rear view.
    /// </summary>
    private Ship? FindTargetInCrosshairs()
    {
        foreach (Ship ship in _sim.Bubble)
        {
            if (Combat.IsInCrosshairs(ship, _sim.TargetableAreaOf(ship), _sim.View))
            {
                return ship;
            }
        }

        return null;
    }

    /// <summary>
    /// Which space view a key press asks for, or null if none of the four is being pressed. The
    /// original's keys are red keys f0 to f3, which are F1 to F4 on a PC keyboard.
    /// </summary>
    private SpaceView? ViewKeyPressed(KeyboardState keys)
    {
        if (keys.IsKeyDown(Settings.ViewFrontKey)) return SpaceView.Front;
        if (keys.IsKeyDown(Settings.ViewRearKey)) return SpaceView.Rear;
        if (keys.IsKeyDown(Settings.ViewLeftKey)) return SpaceView.Left;
        if (keys.IsKeyDown(Settings.ViewRightKey)) return SpaceView.Right;
        return null;
    }

    /// <summary>
    /// Changes the view, as LOOK1 does: the screen is cleared, the stardust is reflected in the
    /// screen diagonal, and the crosshairs are redrawn. Ours has no screen to clear — the view is a
    /// transform applied as things are drawn — so what is left to do is the reflection and telling
    /// the dust which way to stream.
    /// </summary>
    public void SetView(SpaceView view)
    {
        if (_sim.View == view)
        {
            return;
        }

        _sim.View = view;
        _starfield.View = view;
        _starfield.Flip();
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

            // The gauges the dashboard shows, so a headless run can be checked without reading pixels
            text.Append(
                $", energy {_sim.Player.Energy}/{_sim.Player.MaxEnergy}, " +
                $"cabin {_sim.CabinTemperature}, laser {_sim.LaserTemperature}, " +
                $"autopilot {(DockingComputerEngaged ? "on" : "off")}, view {_sim.View}, " +
                $"fuel {Session?.Commander.Fuel ?? 0}, sim step {_sim.MainLoopCounter}, took {_sim.DamageTakenThisFrame} damage, died {_sim.PlayerDied}" +
                (Session is null ? string.Empty : $", game over {Session.GameOver}, \"{Session.Message}\""));

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
                    $"screen radius {(IsCelestial(ship.Type) ? radius : 0):0.0} " +
                    $"ai 0x{ship.AiFlag:X2}{(ship.IsHostile ? " hostile" : string.Empty)} " +
                    $"newb 0x{ship.NewbFlags:X2} speed {ship.Speed}");
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
        // The Coriolis turns from the moment it appears, as the original's does: NWSPS gives it a
        // roll counter of 255, a full anti-clockwise roll that never damps. It matters for docking,
        // because the slot's orientation changes as the station turns and the docking computer's
        // first phase matches its roll.
        SystemArrival.ArriveInSystem(
            _sim,
            system,
            stationDistance,
            SystemArrival.StationRollCounter,
            Session?.ArrivalStatusCarry ?? 0);

        // Arriving puts us back in the front view with a new stardust field, which is TT110: it
        // reaches LOOK1 with X = 0 for the front view, which clears the screen, reflects the dust
        // in the screen diagonal and sets up a new field. Jumping to a new system while looking out
        // of the back window otherwise leaves us there, with the dust streaming the wrong way.
        SetView(SpaceView.Front);
        _starfield.Reset();

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
            // --fly-to-planet steers during the warmup as well, which is what makes the whole
            // arrival-to-station run reachable from the command line
            _sim.Step(FollowPlanet ? SteerTowardsPlanet() ?? input : input);
        }

        LastInput = input;
        _starfield.Update(_sim.Speed * frames);
    }

    /// <summary>
    /// True to fly towards the planet, which is a development autopilot: reaching the planet is what
    /// makes the station appear, and crossing a system by hand takes minutes.
    /// </summary>
    /// <remarks>
    /// It steers with the same controls a player has — roll and pitch held one way or the other —
    /// rather than setting the orientation, so what it exercises is the real flight model. The
    /// deadband is a few degrees, so it arrives with a slight weave, as a pilot would.
    /// </remarks>
    public bool FollowPlanet { get; set; }

    /// <summary>A control input that turns us towards the planet, or null if there is no planet.</summary>
    private FlightInput? SteerTowardsPlanet()
    {
        Ship? planet = null;
        foreach (Ship ship in _sim.Bubble)
        {
            if (ship.Type is SystemArrival.PlanetTypeA or SystemArrival.PlanetTypeB)
            {
                planet = ship;
                break;
            }
        }

        if (planet is null)
        {
            return null;
        }

        (int x, int y, int z) = planet.GetPosition();
        double distance = Math.Sqrt(((double)x * x) + ((double)y * y) + ((double)z * z));
        if (distance < 1)
        {
            return new FlightInput(SlowDown: true);
        }

        // The direction to the planet in our own frame, since the universe is stored as if we were
        // looking forward
        var wanted = new System.Numerics.Vector3((float)(x / distance), (float)(y / distance), (float)(z / distance));
        System.Numerics.Vector3 nose = Unit(ship: _sim.Player, Orientation.Nosev);
        System.Numerics.Vector3 roof = Unit(ship: _sim.Player, Orientation.Roofv);
        System.Numerics.Vector3 side = Unit(ship: _sim.Player, Orientation.Sidev);

        const float deadband = 0.03f;
        float aimSide = System.Numerics.Vector3.Dot(wanted, side);
        float aimRoof = System.Numerics.Vector3.Dot(wanted, roof);
        float aimNose = System.Numerics.Vector3.Dot(wanted, nose);

        // Once we are pointing at it, slow down instead of charging through: the station appears on
        // the near side of the planet, and flying past it at speed 40 leaves it behind
        bool close = aimNose > 0.999f && distance < 200000;

        return new FlightInput(
            RollLeft: aimSide < -deadband,
            RollRight: aimSide > deadband,
            PullUp: aimRoof > deadband,
            PitchDown: aimRoof < -deadband,
            SpeedUp: !close,
            SlowDown: close && _sim.Speed > 8);
    }

    /// <summary>Reads one of a ship's orientation vectors as a unit vector.</summary>
    private static System.Numerics.Vector3 Unit(Ship ship, int vector)
    {
        var value = new System.Numerics.Vector3(
            (float)ship.Orientation.GetUnity(vector, Orientation.X),
            (float)ship.Orientation.GetUnity(vector, Orientation.Y),
            (float)ship.Orientation.GetUnity(vector, Orientation.Z));

        return value.LengthSquared() > 0 ? System.Numerics.Vector3.Normalize(value) : new System.Numerics.Vector3(0, 0, 1);
    }

    /// <summary>
    /// Makes one in-system jump, giving the sky a new stardust field if it happened. The "J" key and
    /// the command line's <c>--in-system-jump</c> both come through here, so that what the key does
    /// and what the development command does cannot drift apart.
    /// </summary>
    /// <returns>True if we jumped; false if it was refused and the caller should beep.</returns>
    public bool InSystemJump()
    {
        if (!_sim.TryInSystemJump())
        {
            return false;
        }

        _starfield.Reset();
        return true;
    }

    /// <summary>
    /// Where each ship was before the most recent iteration, so the drawing can smooth the gap.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The simulation runs at the original's own rate — about twelve and a half iterations a second,
    /// which is what the disc version managed in ordinary flight — while the display runs at whatever
    /// the monitor does. Drawing the raw positions therefore shows ships standing still and then
    /// jumping, several times a second, and at close range those jumps are large: a ship closing at
    /// thirty units an iteration moves a visible fraction of the screen each time. That is not how
    /// the original looked, because there the whole screen was redrawn at the same twelve and a half
    /// frames a second, so the motion was consistently stepped rather than stuttering against a
    /// smooth starfield.
    /// </para>
    /// <para>
    /// Interpolating between the last two iterations restores the smoothness without touching the
    /// simulation: the game still advances in the original's steps, and only the drawing is spread
    /// between them. This is the standard remedy for a fixed-step simulation on a faster display.
    /// </para>
    /// </remarks>
    private readonly Dictionary<Ship, System.Numerics.Vector3> _wasAt = [];

    /// <summary>Records where every ship is, before the simulation moves them.</summary>
    private void RememberPositions()
    {
        // Rebuilt rather than added to, so that ships which have left the bubble do not stay in the
        // table for the rest of the session
        _wasAt.Clear();

        foreach (Ship ship in _sim.Bubble)
        {
            (int x, int y, int z) = ship.GetPosition();
            _wasAt[ship] = new System.Numerics.Vector3(x, y, z);
        }
    }

    /// <summary>A ship's orientation as seen through the current view.</summary>
    private ShipOrientation Viewed(ShipOrientation orientation) => new(
        Plut.Direction(_sim.View, orientation.Nose),
        Plut.Direction(_sim.View, orientation.Roof),
        Plut.Direction(_sim.View, orientation.Side));

    /// <summary>
    /// Where to draw a ship: between where it was and where it is, by however much of an iteration
    /// has passed since the last one.
    /// </summary>
    private System.Numerics.Vector3 WhereItIsNow(Ship ship)
    {
        (int x, int y, int z) = ship.GetPosition();
        var now = new System.Numerics.Vector3(x, y, z);

        if (!_wasAt.TryGetValue(ship, out System.Numerics.Vector3 before))
        {
            return now;
        }

        float alpha = Math.Clamp(_accumulator / FrameTime, 0f, 1f);
        return System.Numerics.Vector3.Lerp(before, now, alpha);
    }

    /// <summary>
    /// True while the docking computer is flying the ship.
    /// </summary>
    /// <remarks>
    /// The original's DOKEY engages it when "C" is pressed with a docking computer fitted and a
    /// station in the safe zone, and DOCKIT then writes rotation counters rather than reading the
    /// keyboard — which is why the simulation has a path for counters that does not go through the
    /// key rate at all. Nothing engaged it before this: the computer could be bought, showed as
    /// fitted on the status screen, and did nothing whatever.
    /// </remarks>
    public bool DockingComputerEngaged { get; set; }

    /// <summary>Engages or disengages the docking computer when the key is tapped.</summary>
    private void UpdateDockingComputer(Microsoft.Xna.Framework.Input.KeyboardState keys)
    {
        // "C" hands the ship over, and only ever hands it over: the disc version's branch is a
        // straight STA auto with the key ANDed with the fitting, so pressing it twice asks twice.
        // The remake used to toggle, which left no key for the original's own way out.
        if (keys.IsKeyDown(Settings.DockingComputerKey) && !_dockingComputerPressed)
        {
            _dockingComputerPressed = true;

            if (DockingComputer.CanEngage(StationInBubble(), Session?.Commander.DockingComputer == true))
            {
                DockingComputerEngaged = true;
            }
        }
        else if (!keys.IsKeyDown(Settings.DockingComputerKey))
        {
            _dockingComputerPressed = false;
        }

        // "P" is the original's cancel-docking-computer key — "LDA KY20 / BEQ MA78 / LDA #0 /
        // STA auto" — and it is the only way to take the controls back.
        if (keys.IsKeyDown(Settings.CancelDockingKey) && !_cancelDockingPressed)
        {
            _cancelDockingPressed = true;
            DockingComputerEngaged = false;
        }
        else if (!keys.IsKeyDown(Settings.CancelDockingKey))
        {
            _cancelDockingPressed = false;
        }
    }

    /// <summary>
    /// An angle and its sign byte as one signed value, which is how the original keeps ALPHA and
    /// BETA: the magnitude with bit 7 holding the sign.
    /// </summary>
    private static int Signed(byte angle, byte sign) =>
        (sign & 0x80) != 0 ? (sbyte)(angle | 0x80) : angle;

    /// <summary>The space station in the local bubble, or null when there is none.</summary>
    private Ship? StationInBubble()
    {
        foreach (Ship ship in _sim.Bubble)
        {
            if (ship.Type == Combat.SpaceStationType)
            {
                return ship;
            }
        }

        return null;
    }

    private bool _dockingComputerPressed;

    /// <summary>
    /// Advances the simulation by one iteration, with the docking computer flying if it is engaged.
    /// </summary>
    private void StepWithDockingComputer()
    {
        if (!DockingComputerEngaged)
        {
            _sim.Step(LastInput);
            return;
        }

        Ship? station = StationInBubble();

        if (station is null || station.IsKilled)
        {
            // Nothing to fly to any more
            DockingComputerEngaged = false;
            _sim.Step(LastInput);
            return;
        }

        DockingComputer.Manoeuvre move = DockingComputer.Fly(station, _sim.Speed);
        _sim.SetRotationCounters(move.RollCounter, move.PitchCounter);
        _sim.Step(LastInput with { RollLeft = false, RollRight = false, PullUp = false, PitchDown = false,
            SpeedUp = move.SpeedUp, SlowDown = move.SlowDown });
        _sim.ClearRotationCounters();

        // Docking ends the flight, so the autopilot goes with it
        if (Session is { Mode: not GameMode.Flying })
        {
            DockingComputerEngaged = false;
        }
    }

    /// <summary>
    /// The message to show, or empty for none.
    /// </summary>
    /// <remarks>
    /// The session keeps a message as its state rather than as something to print once, and the
    /// flight loop leaves it set for ever unless something replaces it — so a jump's "hyperspace
    /// drive engaged" would sit at the bottom of the view for the rest of the game. The timer is a
    /// modern addition for that reason; the original erases the message when the next one is
    /// printed, and has no timeout of its own because most of its messages replace each other
    /// quickly. The game-over message never expires, since it is the last thing that happens.
    /// </remarks>
    private string CurrentMessage()
    {
        if (Session is null)
        {
            return string.Empty;
        }

        if (Session.GameOver)
        {
            return Session.Message;
        }

        if (Session.Message != _lastMessage)
        {
            _lastMessage = Session.Message;
            _messageLeft = MessageSeconds;
        }

        if (_messageLeft <= 0)
        {
            return string.Empty;
        }

        _messageLeft -= _lastElapsed;
        return Session.Message;
    }

    /// <summary>How long an in-flight message stays on screen.</summary>
    private const float MessageSeconds = 4f;

    private string _lastMessage = string.Empty;
    private float _messageLeft;
    private float _lastElapsed;

    /// <summary>The session this scene is flying in, so docking can be requested.</summary>
    public GameSession? Session { get; set; }

    /// <summary>The sounds, or null when the game is running silently.</summary>
    public Audio.SoundBank? Sounds { get; set; }

    /// <summary>The player's bindings, so the settings screen can change what the keys do.</summary>
    public Settings Settings { get; set; } = new();

    public void Update(float elapsedSeconds)
    {
        _lastElapsed = elapsedSeconds;

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

        if (FollowPlanet && Session is not null && SteerTowardsPlanet() is { } steering)
        {
            HeldInput = steering;
        }

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

            // C hands the ship to the docking computer, as the original's DOKEY does: it needs the
            // computer fitted and a station in range, and it refuses to fly us in to one we have
            // annoyed. Pressing it again takes the controls back.
            UpdateDockingComputer(keys);

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

            // The four space views, on the original's own keys: f0 to f3 on a BBC Micro, which are
            // F1 to F4 here because a PC keyboard has no f0
            if (ViewKeyPressed(keys) is { } wanted)
            {
                SetView(wanted);
            }

            // U unarms the missile, which the original answers with a long, low beep whether or not
            // it was aimed at anything
            if (keys.IsKeyDown(Settings.UnarmMissileKey) && !_unarmPressed)
            {
                _unarmPressed = true;
                if (Session.Flight.UnarmMissile())
                {
                    Sounds?.Play(Core.Audio.SoundEffect.Boop);
                }
            }
            else if (!keys.IsKeyDown(Settings.UnarmMissileKey))
            {
                _unarmPressed = false;
            }

            // J is the in-system jump. The original refuses one with a low beep when there is a ship
            // or a station about, or when we are already too close to the planet or the sun, and it
            // answers a jump with a new stardust field, which is the one visible part of it.
            if (keys.IsKeyDown(Settings.InSystemJumpKey) && !_inSystemJumpPressed)
            {
                _inSystemJumpPressed = true;
                if (!InSystemJump())
                {
                    Sounds?.Play(Core.Audio.SoundEffect.Boop);
                }
            }
            else if (!keys.IsKeyDown(Settings.InSystemJumpKey))
            {
                _inSystemJumpPressed = false;
            }

            // ESCAPE launches our escape pod, as it does in the original, provided we have one; the
            // game itself leaves on F10 so that the two do not fight over the key. The disc version's
            // ESCAPE makes no sound — the noise in the routine belongs to the NES version — so
            // neither does ours.
            if (keys.IsKeyDown(Settings.EscapePodKey) && !_escapePodPressed)
            {
                _escapePodPressed = true;
                Session.LaunchEscapePod();
            }
            else if (!keys.IsKeyDown(Settings.EscapePodKey))
            {
                _escapePodPressed = false;
            }

            // A missile fired at us prints the original's warning, "Print recursive token 120
            // (INCOMING MISSILE) as an in-flight message", and makes the launch sound — entry 48 of
            // the sound table, the same one our own missile uses
            if (_sim.MissileFiredAtUsThisFrame is not null)
            {
                Session.Message = "INCOMING MISSILE";
                Sounds?.Play(Core.Audio.SoundEffect.Missile);
            }

            // And scooping prints what we picked up: "Print recursive token 48 + Y as an in-flight
            // token, which will be in the range 48 (FOOD) to 64 (ALIEN ITEMS)"
            if (_sim.ScoopedThisFrame is { } scoopedItem)
            {
                Session.Message = ItemName(scoopedItem.Item);
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

        // Launching draws the tunnel as we leave the station, which is the original's LAUN: it makes
        // the launch sound — the same table entry as a missile's — and draws the rings.
        if (Session is { } launched && launched.Launches != _launchesSeen)
        {
            _launchesSeen = launched.Launches;
            _launchTunnelFrames = LaunchTunnelFrames;
            Sounds?.Play(Core.Audio.SoundEffect.Launch);
        }

        // A successful docking draws the same rings as we enter the station, which is GOIN: it calls
        // HFS2 with the launch's step size and only then shows the docking bay.
        if (_dockTunnelFrames > 0 && --_dockTunnelFrames == 0)
        {
            Session?.Dock();
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
        else if (Session is not null && !DockingSequenceRunning &&
                 !Microsoft.Xna.Framework.Input.Keyboard.GetState().IsKeyDown(Microsoft.Xna.Framework.Input.Keys.D))
        {
            _dockingRequested = false;
        }

        // The launch and docking tunnels are not part of the flight: the original draws them as it
        // leaves the flight loop (LAUN on the way out of the station, GOIN on the way in), so the
        // simulation stands still while they are up. Letting it run on through a docking tunnel flew
        // us into the station we had just been cleared to enter, which is a game over rather than a
        // docking.
        if (_launchTunnelFrames > 0 || _dockTunnelFrames > 0)
        {
            _starfield.Update(0);
            return;
        }

        // Run the simulation at the original's fixed rate, so the ported maths stays in its
        // original units however fast the display refreshes
        // A long pause (a window drag, a breakpoint) must not turn into a burst of simulation, so
        // the catch-up is capped at a quarter of a second and at ten iterations
        _accumulator += Math.Min(elapsedSeconds, 0.25f);
        int steps = 0;
        while (_accumulator >= FrameTime && steps < 10)
        {
            RememberPositions();
            StepWithDockingComputer();
            _accumulator -= FrameTime;
            steps++;
        }

        // The dust is turned by the same angles the ships are, so the sky swings when we steer
        _starfield.Update(_sim.Speed * steps, Signed(_sim.RollAngle, _sim.RollSign), Signed(_sim.PitchAngleValue, _sim.PitchSign));

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
    /// How many drawn frames the launch and docking tunnels stay up for. The original draws them
    /// once, on the frame we leave the station or enter it, and then gets on with loading the next
    /// bank of code; a handful of frames is what it takes to be seen at all.
    /// </summary>
    public const int LaunchTunnelFrames = 24;

    /// <summary>How many drawn frames the docking tunnel stays up for.</summary>
    public const int DockTunnelFrames = 24;

    private int _launchTunnelFrames;
    private int _dockTunnelFrames;
    private int _launchesSeen;

    /// <summary>
    /// True while the docking rings are up, which means the docking is decided and the station's
    /// screens are next. The check that decided it must not run again: without this it fires every
    /// frame, and each one puts the rings back up, so the ship sits at the slot for ever.
    /// </summary>
    private bool DockingSequenceRunning => _dockTunnelFrames > 0;

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

        // The dashboard's state is set here rather than at the end, because the hyperspace tunnel
        // draws the dashboard too and returns early: setting it late left the tunnel's frames with
        // whatever the previous frame had put there.
        _hud.MissilesArmed = Session?.Commander.Missiles ?? 0;
        _hud.Fuel = Session?.Commander.Fuel ?? EliteRemake.Core.Universe.Outfitting.MaxFuel;
        _hud.Message = CurrentMessage();
        _hud.Locked = _sim.MissileLock is not null;

        // Stars first: they are the backdrop, and the original draws them before the ships
        spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        _starfield.Draw(spriteBatch, pixel, Camera);
        spriteBatch.End();

        // The launch and docking tunnels cover everything for their few frames, with the launch's
        // sixteen sets of rings against the eight that hyperspace and docking draw
        if (_launchTunnelFrames > 0 || _dockTunnelFrames > 0)
        {
            int sets = _launchTunnelFrames > 0 ? HyperspaceTunnel.LaunchRingSets : HyperspaceTunnel.RingSets;
            if (_launchTunnelFrames > 0)
            {
                _launchTunnelFrames--;
            }

            spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            HyperspaceTunnel.Draw(spriteBatch, pixel, Camera, Palette.White, sets);
            spriteBatch.End();
            _hud.Draw(spriteBatch, pixel, Camera, _sim);
            return;
        }

        // The hyperspace tunnel covers everything while the drive is counting down, as the
        // original's LL164 clears the screen and draws its rings over the top
        if (Session is { HyperspaceCountdown: > 0 } || (ShowTunnelFrames > 0 && _drawnFrames < ShowTunnelFrames))
        {
            spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            HyperspaceTunnel.Draw(spriteBatch, pixel, Camera, Palette.White);
            spriteBatch.End();
            _hud.Draw(spriteBatch, pixel, Camera, _sim);
            return;
        }

        // The planet and the sun are circles rather than models, and they are so large and distant
        // that they are always behind the ships, so they are drawn first as the backdrop
        spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        foreach (Ship body in _sim.Bubble)
        {
            if (!IsCelestial(body.Type))
            {
                continue;
            }

            (int x, int y, int z) = body.GetPosition();
            (int bx, int by, int bz) = Plut.Position(_sim.View, x, y, z);
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

            // The ship is drawn through the current view, which is the axis flip the original
            // applies to a ship's INWK workspace before it draws it: the same rule turns its position
            // and its three orientation vectors, so the model is seen from the window we are looking
            // through rather than always from the front.
            System.Numerics.Vector3 where = Plut.Direction(_sim.View, WhereItIsNow(ship));
            if (where.Z <= 0)
            {
                continue; // behind us
            }

            _renderer.DrawShip(
                mesh,
                where,
                Viewed(ShipOrientation.FromEliteOrientation(ship.Orientation)),
                Camera,
                ColourFor(ship),
                ship.VisibilityDistance,
                ship.Type == ShipTypes.Coriolis ? Palette.StationSlot : null,
                ship.Type == ShipTypes.Coriolis ? Palette.StationSlotLip : null);
        }

        _renderer.End();

        spriteBatch.Begin(samplerState: SamplerState.PointClamp);
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

            (int x, int y, int z) = Plut.Position(_sim.View, ship.GetPosition().X, ship.GetPosition().Y, ship.GetPosition().Z);
            if (z <= 0)
            {
                continue;
            }

            // The cloud grows as it ages, which is DOEXP's own size: the counter it ticks up by four
            // a draw is what the cloud's radius is worked out from, "as the cloud counter ticks
            // onward, the cloud expands"
            float radius = Camera.FocalLength * (200 + (ship.ExplosionCounter * 4)) / z;
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
