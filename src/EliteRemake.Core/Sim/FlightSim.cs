using EliteRemake.Core.Maths;

namespace EliteRemake.Core.Sim;

/// <summary>The player's control inputs for one frame.</summary>
/// <param name="RollLeft">The "&lt;" key: roll left.</param>
/// <param name="RollRight">The "&gt;" key: roll right.</param>
/// <param name="PullUp">The "X" key: pull the nose up.</param>
/// <param name="PitchDown">The "S" key: pitch the nose down.</param>
/// <param name="SpeedUp">The Space key.</param>
/// <param name="SlowDown">The "?" key.</param>
public readonly record struct FlightInput(
    bool RollLeft = false,
    bool RollRight = false,
    bool PullUp = false,
    bool PitchDown = false,
    bool SpeedUp = false,
    bool SlowDown = false,
    bool Fire = false);

/// <summary>
/// The flight simulation: our ship at the centre of its own universe, with everything else moving
/// around it.
/// </summary>
/// <remarks>
/// The original never tracks our own position or orientation in flight. Instead, our ship sits at
/// the origin facing along +z, and when we pitch or roll it is the rest of the universe that moves:
/// MVEIT rotates every ship's location (part 5) and orientation vectors (part 7) by our pitch and
/// roll in the opposite direction, and moves each one backwards by our speed (part 6) because it is
/// we who are travelling. Each ship also flies forward along its own nose vector (part 3) and has
/// its vectors tidied on a rolling schedule (part 1).
///
/// This class runs that loop. It is deliberately free of any rendering or platform code so the
/// simulation can be tested on its own and replayed deterministically.
/// </remarks>
public sealed class FlightSim
{
    /// <summary>The number of ship slots in the local bubble (the original's NOSH).</summary>
    public const int MaxShipsInBubble = 12;

    /// <summary>The maximum speed the player can reach (the original caps DELTA at 40).</summary>
    public const byte MaxSpeed = 40;

    private readonly List<Ship> _bubble = [];

    /// <summary>The random number generator the AI and the rest of the simulation share.</summary>
    public EliteRandom Random { get; } = new();

    /// <summary>How much laser damage the player took this frame, so the game can react.</summary>
    public int DamageTakenThisFrame { get; private set; }

    /// <summary>Creates a flight simulation with our ship and an empty bubble.</summary>
    /// <param name="player">Our ship. Its position and orientation are not used in flight.</param>
    public FlightSim(Ship player)
    {
        Player = player;

        // By default a ship's laser does its own power in damage, unless the game supplies the
        // original's byte #19 based figure
        DamageProvider = ship => LaserPowerOf(ship);
    }

    /// <summary>Our ship.</summary>
    public Ship Player { get; }

    /// <summary>The ships in the local bubble of universe, in slot order.</summary>
    public IReadOnlyList<Ship> Bubble => _bubble;

    /// <summary>Our speed, the original's DELTA.</summary>
    public byte Speed { get; set; }

    /// <summary>The roll rate, the original's JSTX, where 128 is the centre.</summary>
    public byte RollRate { get; private set; } = FlightControls.Centre;

    /// <summary>The pitch rate, the original's JSTY, where 128 is the centre.</summary>
    public byte PitchRate { get; private set; } = FlightControls.Centre;

    /// <summary>Whether keyboard auto-recentre is enabled (the original's DJD).</summary>
    public bool AutoRecentre { get; set; } = true;

    /// <summary>Whether keyboard damping is disabled (the original's DAMP).</summary>
    public bool DampingDisabled { get; set; }

    /// <summary>The main loop counter, which the original uses to schedule work across frames.</summary>
    public int MainLoopCounter { get; private set; }

    /// <summary>The roll angle applied this frame (ALP1), for diagnostics and the dashboard.</summary>
    public byte RollAngle { get; private set; }

    /// <summary>The roll direction applied this frame (ALP2).</summary>
    public byte RollSign { get; private set; }

    /// <summary>The pitch angle applied this frame (BET1).</summary>
    public byte PitchAngleValue { get; private set; }

    /// <summary>The pitch direction applied this frame (BET2).</summary>
    public byte PitchSign { get; private set; }

    /// <summary>The laser temperature (the original's GNTMP); at 242 the laser overheats.</summary>
    public int LaserTemperature { get; private set; }

    /// <summary>Frames to wait before the laser can fire again (the original's LASCT).</summary>
    public int LaserCooldown { get; private set; }

    /// <summary>The power of the laser being fired this frame, or zero.</summary>
    public int FiringLaserPower { get; private set; }

    /// <summary>The ship the laser is hitting this frame, if any.</summary>
    public Ship? LaserTarget { get; private set; }

    /// <summary>Set for one frame when a shot destroys a ship, so the caller can pay the bounty.</summary>
    public Ship? DestroyedThisFrame { get; private set; }

    /// <summary>Which mount is firing; the front view is all the remake has so far.</summary>
    public LaserMount ActiveMount { get; set; } = LaserMount.Front;

    /// <summary>
    /// The commander whose lasers we fire. The original keeps the laser loadout in the commander
    /// data block rather than the ship, so this is how the simulation reaches it.
    /// </summary>
    public Commander? Commander { get; set; }

    /// <summary>Fits a laser, as buying one at the station does.</summary>
    public void FitLaser(LaserMount mount, LaserType type) => Commander?.SetLaser(mount, type);

    /// <summary>Called for each newly spawned ship so the game can dress it from its blueprint.</summary>
    public Action<Ship>? ShipSpawned { get; set; }

    /// <summary>Adds a ship to the local bubble, up to the original's slot limit.</summary>
    public bool Spawn(Ship ship)
    {
        if (_bubble.Count >= MaxShipsInBubble)
        {
            return false;
        }

        ShipSpawned?.Invoke(ship);
        _bubble.Add(ship);
        return true;
    }

    /// <summary>Removes a ship from the local bubble.</summary>
    public bool Remove(Ship ship) => _bubble.Remove(ship);

    /// <summary>Removes every ship whose status has marked it for removal.</summary>
    public int RemoveKilledShips() => _bubble.RemoveAll(ship => ship.IsKilled);

    /// <summary>Advances the simulation by one frame (the original runs at 50 frames a second).</summary>
    public void Step(FlightInput input = default)
    {
        UpdateSpeed(input);
        UpdateRotation(input);
        UpdateLasers(input);

        DamageTakenThisFrame = 0;

        for (int slot = 0; slot < _bubble.Count; slot++)
        {
            Ship ship = _bubble[slot];

            // TACTICS runs before the ship is moved, as it does in the original
            int before = Player.Energy + Player.ForeShield + Player.AftShield;
            if (Tactics.Apply(ship, Random, Player, LaserPowerOf(ship), DamageOf(ship)))
            {
                DamageTakenThisFrame += before - (Player.Energy + Player.ForeShield + Player.AftShield);

                if (Player.Energy == 0)
                {
                    PlayerDied = true;
                }
            }

            Mveit(ship, slot);
        }

        // Recharge the energy banks and, above half full, the shields, as the original does at
        // the end of its flight loop
        Combat.RechargeShields(Player);
        Combat.RechargeEnergy(Player);

        // Scoop anything we are flying at, if we have the equipment for it
        UpdateScooping();

        // Ships that have drifted out of range leave the bubble, as they do in the original
        RemoveDistantShips();

        // The main game loop runs the spawn decision once a frame
        UpdateSpawning();

        MainLoopCounter++;
    }

    /// <summary>True once our ship has been destroyed; the game clears it once it has reacted.</summary>
    public bool PlayerDied { get; set; }

    /// <summary>
    /// The system we are flying in, which decides what spawns around us. Set by the game when we
    /// arrive somewhere.
    /// </summary>
    public Universe.StarSystem? System { get; set; }

    /// <summary>Set to false to fly without any ships spawning.</summary>
    public bool SpawningEnabled { get; set; } = true;

    /// <summary>
    /// How far away a ship can get before it leaves the local bubble. The original drops ships out
    /// of the bubble once they are far enough behind or ahead of us, which is what stops the twelve
    /// slots filling up with ships we can no longer see.
    /// </summary>
    public int BubbleRange { get; set; } = 0x8000;

    /// <summary>The extra vessels delay: frames to wait before the next spawn (the original's EV).</summary>
    private int _spawnDelay;

    /// <summary>What spawned this frame, for the game to report.</summary>
    public SpawnKind LastSpawn { get; private set; }

    /// <summary>The type of junk spawned this frame, or 0.</summary>
    public int LastJunkSpawned { get; private set; }

    /// <summary>
    /// What a destroyed ship left behind this frame: the type and how many, for the game to spawn.
    /// </summary>
    public (int Type, int Count) DropsThisFrame { get; private set; }

    /// <summary>Scooped cargo this frame, if any, for the game to report.</summary>
    public (int Item, int Amount)? ScoopedThisFrame { get; private set; }

    /// <summary>The commander, so scooping knows what is fitted and where cargo goes.</summary>
    public Commander? ScoopCommander { get; set; }

    /// <summary>How a canister's contents are decided, from the blueprints.</summary>
    public Func<Ship, int> ScoopItemProvider { get; set; } = _ => 0;

    /// <summary>
    /// Scoops up anything scoopable that we are close enough to. The original checks each item in
    /// the bubble as part of its flight loop and collects it if we have fuel scoops fitted.
    /// </summary>
    private void UpdateScooping()
    {
        if (ScoopCommander is null)
        {
            return;
        }

        foreach (Ship ship in _bubble)
        {
            if (!Debris.IsScoopable(ship.Type) || ship.IsKilled)
            {
                continue;
            }

            (int x, int y, int z) = ship.GetPosition();
            if (z <= 0)
            {
                continue;
            }

            double distance = Math.Sqrt(((double)x * x) + ((double)y * y) + ((double)z * z));
            if (distance > Debris.ScoopRange)
            {
                continue;
            }

            (int Item, int Amount)? scooped = Debris.TryScoop(ship, ScoopCommander, ScoopItemProvider(ship));
            if (scooped is not null)
            {
                ScoopedThisFrame = scooped;
                break;
            }
        }
    }

    /// <summary>Removes ships that have drifted beyond the bubble's range.</summary>
    private void RemoveDistantShips()
    {
        _bubble.RemoveAll(ship =>
        {
            if (IsCelestial(ship.Type))
            {
                return false; // the planet and sun stay put
            }

            int z = ship.GetCoordinate(ShipDataBlock.Z);
            int x = ship.GetCoordinate(ShipDataBlock.X);
            int y = ship.GetCoordinate(ShipDataBlock.Y);
            return Math.Abs(z) > BubbleRange || Math.Abs(x) > BubbleRange || Math.Abs(y) > BubbleRange;
        });
    }

    /// <summary>
    /// The original's spawn decision, run once a frame from the main game loop. A spawn is delayed
    /// by the EV counter so ships arrive one at a time rather than in a rush.
    /// </summary>
    private void UpdateSpawning()
    {
        LastSpawn = SpawnKind.None;

        if (!SpawningEnabled || System is null)
        {
            return;
        }

        if (_spawnDelay > 0)
        {
            _spawnDelay--;
            return;
        }

        // Junk first, as the original checks for rocks before it considers ships
        int junk = 0;
        foreach (Ship existing in _bubble)
        {
            if (Debris.IsJunk(existing.Type))
            {
                junk++;
            }
        }

        int junkType = Debris.ChooseJunk(Random, junk);
        if (junkType != 0)
        {
            Spawn(Debris.CreateJunk(junkType, Random));
            LastSpawn = SpawnKind.None;
            LastJunkSpawned = junkType;
            _spawnDelay = Spawner.SpawnDelay / 2;
            return;
        }

        LastJunkSpawned = 0;

        SpawnKind kind = Spawner.ChooseSpawn(System.Value, Random);
        if (kind == SpawnKind.None)
        {
            return;
        }

        // A pack of pirates arrives together, up to the original's four in a group
        int count = kind == SpawnKind.Pirates ? 1 + (Random.Next() % 4) : 1;
        for (int i = 0; i < count; i++)
        {
            if (!Spawn(Spawner.Create(kind, System.Value, Random, i)))
            {
                break; // the bubble is full
            }
        }

        LastSpawn = kind;
        _spawnDelay = Spawner.SpawnDelay;
    }

    /// <summary>
    /// Applies the speed keys. The original changes DELTA by one per frame, caps it at 40 and never
    /// lets it drop below 1.
    /// </summary>
    private void UpdateSpeed(FlightInput input)
    {
        if (input.SpeedUp && Speed < MaxSpeed)
        {
            Speed++;
        }

        if (input.SlowDown)
        {
            Speed--;
            if (Speed == 0)
            {
                Speed = 1;
            }
        }
    }

    /// <summary>
    /// Applies the roll and pitch keys to their rates and works out the angles the universe will be
    /// rotated by.
    /// </summary>
    private void UpdateRotation(FlightInput input)
    {
        // The original applies the key presses first, then damps the rates towards the centre and
        // stores the damped values back into JSTX and JSTY
        RollRate = FlightControls.ApplyRollKeys(RollRate, input.RollLeft, input.RollRight, AutoRecentre);
        PitchRate = FlightControls.ApplyPitchKeys(PitchRate, input.PullUp, input.PitchDown, AutoRecentre);
        RollRate = FlightControls.DampRollRate(RollRate, DampingDisabled);
        PitchRate = FlightControls.DampPitchRate(PitchRate, DampingDisabled);

        (byte alp1, byte alp2, _) = FlightControls.RollAngle(RollRate);
        (byte bet1, byte bet2, _) = FlightControls.PitchAngle(PitchRate);

        RollAngle = alp1;
        RollSign = alp2;
        PitchAngleValue = bet1;
        PitchSign = bet2;
    }

    /// <summary>True for the planet and the sun, which the original moves with MV40.</summary>
    public static bool IsCelestial(int shipType) =>
        shipType is ShipTypes.Sun or SystemArrival.PlanetTypeA or SystemArrival.PlanetTypeB;

    /// <summary>
    /// Fires the laser if the trigger is held and the laser is neither cooling down nor
    /// overheated, works out what it hits, and then cools the laser down by a degree.
    /// </summary>
    private void UpdateLasers(FlightInput input)
    {
        FiringLaserPower = 0;
        LaserTarget = null;
        DestroyedThisFrame = null;
        DropsThisFrame = (0, 0);
        ScoopedThisFrame = null;

        LaserType laser = Commander?.GetLaser(ActiveMount) ?? LaserType.Pulse;
        int power = Combat.Power(laser);

        if (input.Fire && power > 0 && LaserCooldown == 0 && LaserTemperature < Combat.OverheatTemperature)
        {
            FiringLaserPower = power;
            LaserTemperature = Math.Min(255, LaserTemperature + Combat.HeatPerShot);
            LaserCooldown = Combat.FireInterval(laser);

            // Deplete our energy, as firing does in the original
            if (Player.Energy > 0)
            {
                Player.Energy--;
            }

            // Find the first ship in the crosshairs, which is the one we hit
            foreach (Ship ship in _bubble)
            {
                if (Combat.IsInCrosshairs(ship, TargetableArea(ship)))
                {
                    LaserTarget = ship;
                    if (Combat.ApplyHit(ship, power))
                    {
                        // The hit destroyed it, so start its explosion and report the kill
                        ship.IsExploding = true;
                        ship.Flags |= 0x40; // the original's bit 6: an explosion is running
                        DestroyedThisFrame = ship;

                        // Rocks and ships leave something behind when they are destroyed
                        DropsThisFrame = Debris.DestructionDrops(ship.Type, power, Random);
                    }

                    break;
                }
            }
        }

        if (LaserCooldown > 0)
        {
            LaserCooldown--;
        }

        // The laser cools by one degree a frame, as the original's main game loop does
        if (LaserTemperature > 0)
        {
            LaserTemperature -= Combat.CoolingPerFrame;
        }
    }

    /// <summary>The targetable area of a ship, which the hit test uses.</summary>
    public Func<Ship, int> TargetableAreaProvider { get; set; } = _ => 95 * 95;

    /// <summary>
    /// The laser power of a ship, from its blueprint. Enemy ships fire with the power in bits 3-7
    /// of blueprint byte #19, which is what the original reads when it decides whether a ship can
    /// shoot at us at all.
    /// </summary>
    public Func<Ship, int> LaserPowerProvider { get; set; } = _ => 0;

    /// <summary>
    /// How much damage a ship's laser does to us, which the original takes from byte #19 of the
    /// blueprint halved — that byte packs the laser power and missile count together.
    /// </summary>
    public Func<Ship, int> DamageProvider { get; set; } = _ => 0;

    private int LaserPowerOf(Ship ship) => LaserPowerProvider(ship);

    private int DamageOf(Ship ship) => DamageProvider(ship);

    private int TargetableArea(Ship ship) => TargetableAreaProvider(ship);

    /// <summary>
    /// MVEIT: moves one ship for this frame, in the original's order.
    /// </summary>
    private void Mveit(Ship ship, int slot)
    {
        // Part 1: tidy the ship's orientation vectors every 16 frames, one slot at a time. The
        // original compares the main loop counter with the slot number modulo 16.
        if (((MainLoopCounter ^ slot) & 15) == 0 && !ship.IsExploding && !ship.IsKilled)
        {
            ShipMath.Tidy(ship.Orientation);
        }

        if (ship.IsExploding || ship.IsKilled)
        {
            return;
        }

        // Part 3: move the ship forward along its own nose vector by its own speed
        if (ship.Speed != 0)
        {
            ShipMovement.MoveShipForward(ship.Data, ship.Orientation.AsSpan(Orientation.Nosev), ship.Speed);
        }

        // Part 5: rotate the ship's location by our pitch and roll, as the universe turns around
        // us. The planet and sun take the original's separate MV40 path, which keeps the full
        // 24-bit coordinates intact so their great distances survive.
        if (IsCelestial(ship.Type))
        {
            ShipMovement.RotateBodyLocationByOurPitchAndRoll(
                ship.Data,
                RollAngle,
                RollSign,
                PitchAngleValue,
                PitchSign);
        }
        else
        {
            ShipMovement.RotateLocationByOurPitchAndRoll(
                ship.Data,
                RollAngle,
                RollSign,
                PitchAngleValue,
                PitchSign);
        }

        // Part 6: move the ship backwards by our speed, as it is we who are travelling. The
        // original skips this for the sun, which returns from MVEIT before its own rotation.
        if (!IsCelestial(ship.Type))
        {
            ShipMovement.MoveShipByOurSpeed(ship.Data, Speed);
        }
        else if (Speed != 0)
        {
            // Bodies still rush past us as we fly, but in 24-bit arithmetic
            int z = ShipMovement.Read24(ship.Data, ShipDataBlock.Z) - Speed;
            ShipMovement.Write24(ship.Data, ShipDataBlock.Z, z);
        }

        // Part 7: rotate the ship's orientation vectors by our pitch and roll, so its heading stays
        // correct in our rotating frame. The planet and sun are the exception: they have no
        // meaningful heading.
        if (!IsCelestial(ship.Type))
        {
            ShipMath.Mvs4(ship.Orientation, Orientation.Nosev, RollAngle, PitchAngleValue);
            ShipMath.Mvs4(ship.Orientation, Orientation.Roofv, RollAngle, PitchAngleValue);
            ShipMath.Mvs4(ship.Orientation, Orientation.Sidev, RollAngle, PitchAngleValue);

            // Part 8: rotate the ship about its own axes by its pitch and roll counters, which is
            // how ships turn under their own power (and how the AI steers)
            ShipMovement.RotateShipAboutItself(ship.Orientation, ship.Data);
        }
    }
}

/// <summary>
/// The ship types the simulation itself cares about. The rest come from the original's XX21 table.
/// </summary>
public static class ShipTypes
{
    /// <summary>The sun, which the original gives the type number 129.</summary>
    public const int Sun = 129;

    /// <summary>The planet.</summary>
    public const int Planet = -1;

    /// <summary>The Coriolis space station.</summary>
    public const int Coriolis = 2;

    /// <summary>The escape pod.</summary>
    public const int EscapePod = 3;
}
