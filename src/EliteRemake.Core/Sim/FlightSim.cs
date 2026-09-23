using EliteRemake.Core.Maths;

using EliteRemake.Core.Universe;

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

    /// <summary>
    /// Set for one frame when a shot destroys a ship, so the caller can pay the bounty. The game
    /// clears it once it has done so.
    /// </summary>
    public Ship? DestroyedThisFrame { get; set; }

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

        // Everything in the bubble belongs on the scanner unless it is the planet or the sun
        if (!IsCelestial(ship.Type))
        {
            ship.ShowOnScanner = true;
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

        // Flying into another ship hurts us badly and annoys it
        UpdateCollisions();

        // Flying into a planet or a sun is the end of us
        HitABody = false;
        UpdateAltitudeChecks();

        // The energy bomb, if it is going off, kills everything in reach
        BombKillsThisFrame = 0;
        UpdateEnergyBomb();

        // Missiles home in, and the E.C.M. swats them down
        UpdateMissiles();

        // Scoop anything we are flying at, if we have the equipment for it
        UpdateScooping();

        // Ships that have drifted out of range leave the bubble, as they do in the original
        RemoveDistantShips();

        // Wrecked ships leave the bubble, as the original's KILLSHP does
        RemoveKilledShips();

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
    /// How far away a ship can get before it leaves the local bubble. The original's FAROF sets
    /// A = 224 and compares it with x_hi, y_hi and z_hi, dropping the ship if any of them is
    /// bigger — so a ship leaves once it is more than 224 in the high byte, which is 57344 units.
    /// Note that this is well beyond the scanner's range, so the original also keeps ships that
    /// the scanner does not show: they have flown past and are on their way out.
    /// </summary>
    public int BubbleRange { get; set; } = 224 << 8;

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

    /// <summary>The missions, which decide whether the Constrictor or extra Thargoids appear.</summary>
    public Missions? Missions { get; set; }

    /// <summary>How many Thargoids the original puts in witchspace: four.</summary>
    public const int WitchspaceThargoids = 4;

    /// <summary>The Thargoid mothership, which the original's XX21 numbers 29.</summary>
    public const int ThargoidType = 29;

    /// <summary>The Thargon, the Thargoid's small companion, which XX21 numbers 30.</summary>
    public const int ThargonType = 30;

    /// <summary>
    /// Sets up the witchspace ambush, as the original's MJP does: the bubble is emptied and four
    /// Thargoids appear, each with a Thargon in attendance, and there is no planet or sun.
    /// </summary>
    /// <remarks>
    /// The disc's loop is <c>LDA #3 / CMP MANY+THG / BCS MJP1</c>, so it keeps going until there are
    /// four Thargoids, where the Master version settles for three. The counter it tests is the one
    /// GTHG increments, so this counts Thargoids rather than assuming all eight ships fit.
    /// </remarks>
    public void ArriveInWitchspace()
    {
        foreach (Ship ship in _bubble.ToArray())
        {
            Remove(ship);
        }

        for (int i = 0; i < WitchspaceThargoids; i++)
        {
            if (SpawnAhead(ThargoidType, "thargoid", "Thargoid") is null)
            {
                break;   // no room left in the bubble
            }

            SpawnAhead(ThargonType, "thargon", "Thargon");
        }
    }

    /// <summary>
    /// Spawns a ship of a given type ahead of us, as the original's GTHG does for the Thargoids that
    /// wait in witchspace.
    /// </summary>
    /// <param name="type">The ship type, as the original's XX21 table numbers it.</param>
    /// <param name="blueprintId">The blueprint id to draw it with.</param>
    /// <param name="name">Its name, for the status line.</param>
    /// <returns>The ship, or null if there was no room for it.</returns>
    public Ship? SpawnAhead(int type, string blueprintId, string name)
    {
        var ship = Ship.Create(type, blueprintId, name, 0, 0, Spawner.SpawnDistance, 0, 0);
        ship.AiFlag = 0xF8;   // as aggressive as the original's Thargoids

        if (!Spawn(ship))
        {
            return null;
        }

        LastSpawn = SpawnKind.Pirates;
        return ship;
    }

    /// <summary>The galaxy we are in, which the missions need to find their systems.</summary>
    public int GalaxyNumber { get; set; }

    /// <summary>The seeds of the galaxy we are in, so the missions can find their systems.</summary>
    public SystemSeeds GalaxySeeds { get; set; }

    /// <summary>The ship our missiles are locked onto, or null.</summary>
    public Ship? MissileLock { get; set; }

    /// <summary>Set when a missile goes off on us, for the game to report.</summary>
    public bool HitByMissile { get; private set; }

    /// <summary>Set when the E.C.M. is active, which destroys missiles.</summary>
    public bool EcmActive { get; private set; }

    /// <summary>Frames left on the energy bomb's effect, or 0 when it is not going off.</summary>
    public int EnergyBombFrames { get; private set; }

    /// <summary>True while an energy bomb is going off.</summary>
    public bool EnergyBombActive => EnergyBombFrames > 0;

    /// <summary>
    /// Sets off the commander's energy bomb, which destroys every ship in the local bubble except
    /// the space station. The original's BOMB flag then stays set for a few frames while the effect
    /// is drawn, killing each ship as the main loop reaches it.
    /// </summary>
    public bool FireEnergyBomb()
    {
        if (Commander is not { EnergyBomb: true } || EnergyBombActive)
        {
            return false;
        }

        Commander.EnergyBomb = false; // it is a one-shot
        EnergyBombFrames = Combat.EnergyBombFrames;
        return true;
    }

    /// <summary>Kills everything the energy bomb can reach, as the original's main loop does.</summary>
    private void UpdateEnergyBomb()
    {
        if (EnergyBombFrames <= 0)
        {
            return;
        }

        EnergyBombFrames--;

        foreach (Ship ship in _bubble)
        {
            // Energy bombs are useless against space stations
            if (ship.Type == Combat.SpaceStationType || ship.IsKilled || IsCelestial(ship.Type))
            {
                continue;
            }

            ship.IsKilled = true;
            ship.IsExploding = true;
            ship.Flags |= 0x40;
            DestroyedThisFrame = ship;
            BombKillsThisFrame++;
        }
    }

    /// <summary>How many ships the energy bomb destroyed this frame.</summary>
    public int BombKillsThisFrame { get; private set; }

    /// <summary>
    /// The planet's radius in the units the altitude check works in. The original's planet radius
    /// is 96 in its 8-bit unit vectors, and the check squares the high bytes of our position, which
    /// divides by 256, so the figure to test against is 96 * 96 / 256 = 36.
    /// </summary>
    public const int PlanetRadiusSquared = 36;

    /// <summary>Set when we fly into a planet or a sun, which is fatal.</summary>
    public bool HitABody { get; private set; }

    /// <summary>
    /// The altitude check the original runs every 32 iterations of its main loop: if we are close
    /// enough to a planet or sun for the top byte of its position to be zero, the squares of the
    /// high bytes of the position say how far above its surface we are, and if they come to no more
    /// than the planet's radius then we have flown into it.
    /// </summary>
    private void UpdateAltitudeChecks()
    {
        // The original runs this on iteration 10 of every 32
        if ((MainLoopCounter & 31) != 10)
        {
            return;
        }

        foreach (Ship body in _bubble)
        {
            if (!IsCelestial(body.Type))
            {
                continue;
            }

            (int x, int y, int z) = body.GetPosition();

            // The original ORs the top bytes of the three coordinates together: if any of them is
            // non-zero we are more than 65535 away and there is nothing to check. Note this is the
            // *top* byte, which carries the high bits of the coordinate rather than a sign.
            int topByte = ((x >> 16) | (y >> 16) | (z >> 16)) & 0x7F;
            if (topByte != 0)
            {
                continue;
            }

            // We are close, so the high bytes are the significant ones. The planet's radius in
            // these units is 36, and the original subtracts 37 so that the surface itself counts.
            int xHi = Math.Abs(x) >> 8 & 0xFF;
            int yHi = Math.Abs(y) >> 8 & 0xFF;
            int zHi = Math.Abs(z) >> 8 & 0xFF;
            // The original divides the sum of the squares by 256 so it fits in a byte, which is
            // what makes the planet's radius come out as 36 rather than 9216
            int altitude = ((xHi * xHi) + (yHi * yHi) + (zHi * zHi)) / 256;

            if (altitude <= PlanetRadiusSquared + 1)
            {
                HitABody = true;
                PlayerDied = true;
                Player.Energy = 0;
                return;
            }
        }
    }

    /// <summary>
    /// The damage we take when we fly into another ship, and the damage we do to it. The original's
    /// main flight loop applies 128 to us with OOPS and 64 to the ship we hit, and makes it angry.
    /// </summary>
    public const int CollisionDamageToUs = 128;

    /// <summary>The damage a collision does to the ship we flew into.</summary>
    public const int CollisionDamageToThem = 64;

    /// <summary>Set when we collide with a ship this frame, for the game to report.</summary>
    public Ship? CollidedWith { get; private set; }

    /// <summary>
    /// Checks for collisions with other ships. The original treats any ship we touch as a collision,
    /// except the space station, which has its own docking checks: we take 128 damage, it takes 64,
    /// and it becomes thoroughly annoyed with us.
    /// </summary>
    private void UpdateCollisions()
    {
        CollidedWith = null;

        foreach (Ship ship in _bubble)
        {
            // The station has its own docking checks, and a missile reaching us is a detonation
            // rather than a collision, which the missile code handles
            if (ship.IsKilled ||
                IsCelestial(ship.Type) ||
                ship.Type == Combat.SpaceStationType ||
                Missiles.IsMissile(ship.Type))
            {
                continue;
            }

            // The original only considers ships within 256 units on every axis, and further than
            // 127 on none of them
            (int x, int y, int z) = ship.GetPosition();
            if ((x >> 8) != 0 || (y >> 8) != 0 || (z >> 8) != 0)
            {
                continue;
            }

            if (Math.Abs(x) > 127 || Math.Abs(y) > 127 || Math.Abs(z) > 127)
            {
                continue;
            }

            // A collision: we are hurt far more than the ship we ran into
            CollidedWith = ship;
            ship.AiFlag = 0xFF;

            if (Combat.ApplyHit(ship, CollisionDamageToThem))
            {
                ship.IsExploding = true;
                ship.Flags |= 0x40;
                DestroyedThisFrame = ship;
                DropsThisFrame = Debris.DestructionDrops(ship.Type, 0, Random);
            }

            if (Combat.TakeDamage(Player, CollisionDamageToUs, fromBehind: false))
            {
                PlayerDied = true;
            }

            break;
        }
    }

    /// <summary>
    /// Hitting the space station anywhere but its slot, which the original treats as fatal: the
    /// station's surface is not something a Cobra can survive.
    /// </summary>
    public void ApplyStationCollision()
    {
        Player.Energy = 0;
        Player.ForeShield = 0;
        Player.AftShield = 0;
        PlayerDied = true;
    }

    /// <summary>How long the E.C.M. stays on for once fired.</summary>
    public int EcmFrames { get; private set; }

    /// <summary>True if the commander can fire a missile right now.</summary>
    public bool CanFireMissile => Commander is { Missiles: > 0 } && MissileLock is not null;

    /// <summary>
    /// Fires a missile at the locked target, which the original's FRMIS does after making the
    /// target angry.
    /// </summary>
    public bool FireMissile()
    {
        if (!CanFireMissile)
        {
            return false;
        }

        Ship missile = Missiles.CreateMissile(MissileLock);
        missile.SetPosition(0, 0, 64);

        // It leaves our ship heading the way we are facing
        Orientation.FromHeadingPitch(0, 0).AsSpan().CopyTo(missile.Data[ShipDataBlock.Orientation..]);

        if (!Spawn(missile))
        {
            return false;
        }

        // The target is now thoroughly annoyed
        MissileLock!.AiFlag = 0xFF;
        Commander!.Missiles--;
        MissileLock = null;
        return true;
    }

    /// <summary>Fires the E.C.M., which destroys every missile in the bubble.</summary>
    public bool FireEcm()
    {
        if (Commander is not { Ecm: true } || EcmFrames > 0)
        {
            return false;
        }

        EcmFrames = 60; // the original keeps the E.C.M. running for a while
        EcmActive = true;

        if (Player.Energy > Missiles.EcmEnergyCost)
        {
            Player.Energy -= Missiles.EcmEnergyCost;
        }

        return true;
    }

    /// <summary>
    /// Runs the missiles: each one chases its target, and anything the E.C.M. can reach is
    /// destroyed. A missile that catches us does the original's 250 damage.
    /// </summary>
    private void UpdateMissiles()
    {
        HitByMissile = false;

        if (EcmFrames > 0)
        {
            EcmFrames--;
            if (EcmFrames == 0)
            {
                EcmActive = false;
            }
        }

        for (int i = _bubble.Count - 1; i >= 0; i--)
        {
            Ship missile = _bubble[i];
            if (!Missiles.IsMissile(missile.Type) || missile.IsKilled)
            {
                continue;
            }

            // The E.C.M. destroys missiles within range
            if (EcmActive)
            {
                (int ex, int ey, int ez) = missile.GetPosition();
                double ecmDistance = Math.Sqrt(((double)ex * ex) + ((double)ey * ey) + ((double)ez * ez));
                if (ecmDistance < Missiles.EcmRange)
                {
                    missile.IsKilled = true;
                    continue;
                }
            }

            // Chase the target: an enemy ship for our missiles, us for theirs
            (int X, int Y, int Z) targetPosition;
            if (missile.Target is { } target)
            {
                targetPosition = target.GetPosition();
            }
            else
            {
                targetPosition = (0, 0, 0); // us
            }

            if (Missiles.Steer(missile, targetPosition))
            {
                // It went off
                if (missile.Target is { } hit)
                {
                    if (Combat.ApplyHit(hit, Missiles.DirectHitDamage))
                    {
                        hit.IsExploding = true;
                        hit.Flags |= 0x40;
                        DestroyedThisFrame = hit;
                        DropsThisFrame = Debris.DestructionDrops(hit.Type, 0, Random);
                    }
                }
                else
                {
                    // The missile has gone off on us: record the hit, and note whether it was fatal
                    HitByMissile = true;
                    if (Combat.TakeDamage(Player, Missiles.DirectHitDamage, fromBehind: false))
                    {
                        PlayerDied = true;
                    }
                }

                missile.IsKilled = true;
            }
        }
    }

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

    /// <summary>
    /// Spawns a mission ship when one is due: the Constrictor while mission 1 is in progress and we
    /// are in its system, or an extra Thargoid while we are carrying the plans. This is the
    /// original's own order of business, and it takes precedence over the ordinary traffic.
    /// </summary>
    private bool SpawnMissionShip()
    {
        if (Missions is null || System is null)
        {
            return false;
        }

        int constrictors = _bubble.Count(s => s.Type == Missions_ConstrictorType);

        bool here = GalaxyNumber == Missions.ConstrictorGalaxy
            && Missions.IsConstrictorSystem(System.Value, GalaxySeeds);

        if (here && Missions.Mission1Active && !Missions.Mission1Complete && constrictors == 0)
        {
            var constrictor = new Ship(
                Missions_ConstrictorType,
                "constrictor",
                "Constrictor")
            {
                AiFlag = Missions.ConstrictorAiFlag,
                Energy = 252,
            };

            constrictor.SetPosition(0, 0, Spawner.SpawnDistance);
            Orientation.FromHeadingPitch(0, 0).AsSpan().CopyTo(constrictor.Data[ShipDataBlock.Orientation..]);

            if (Spawn(constrictor))
            {
                Missions.ConstrictorIsHere = true;
                LastSpawn = SpawnKind.Pirates;
                return true;
            }

            return false;
        }

        // The Thargoids try to stop us while we carry the plans
        if (Missions.ThargoidSpawnChance > 0 && Random.Next() < Missions.ThargoidSpawnChance)
        {
            Ship thargoid = Ship.Create(
                Missions_ThargoidType,
                "thargoid",
                "Thargoid",
                0, 0, Spawner.SpawnDistance, 0, 200);

            thargoid.AiFlag = 0xF8;
            if (Spawn(thargoid))
            {
                LastSpawn = SpawnKind.Pirates;
                return true;
            }
        }

        return false;
    }

    private const int Missions_ConstrictorType = 31;
    private const int Missions_ThargoidType = 29;

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

        // A mission ship comes before the ordinary traffic
        if (SpawnMissionShip())
        {
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

        if (input.SlowDown && Speed > 0)
        {
            // The original brakes with DEC DELTA and bumps the speed back to 1 with INC when it
            // reaches zero, so the test is on the value *after* the decrement. Testing the value
            // before it lets a ship already at rest brake from zero, which wraps a byte to 255 and
            // sends it off at full speed instead of leaving it stationary.
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
    /// <summary>
    /// Rotation counters set directly, as the original's docking computer does when it writes to
    /// INWK+29 and INWK+30. A counter is how far to turn this frame rather than a rate to hold, so
    /// it is converted into the rate the rest of the simulation uses: the centre is 128, and a
    /// larger counter is a faster turn, which means a smaller rate.
    /// </summary>
    /// <param name="rollCounter">The roll counter, with bit 7 as the sign.</param>
    /// <param name="pitchCounter">The pitch counter, with bit 7 as the sign.</param>
    public void SetRotationCounters(byte rollCounter, byte pitchCounter)
    {
        _rotationOverride = (rollCounter, pitchCounter);
    }

    /// <summary>Clears any counter override, returning the ship to its own controls.</summary>
    public void ClearRotationCounters() => _rotationOverride = null;

    private (byte Roll, byte Pitch)? _rotationOverride;

    private void UpdateRotation(FlightInput input)
    {
        // The docking computer writes rotation counters rather than touching the keys, and the
        // original's MVEIT applies those counters through MVS5: one fixed 1/16 radian turn of the
        // ship about its own axes for every frame the counter is non-zero. That is what the counter
        // *is* - a number of frames of turning - so its magnitude is an angle, not a key rate.
        // Handing it to the keyboard path instead rounds a counter of 4 or less away to no turn at
        // all, which is why the autopilot could not make small corrections.
        if (_rotationOverride is { } counters)
        {
            SetCounters(counters.Roll, counters.Pitch);
            return;
        }

        // The original applies the key presses first, then damps the rates towards the centre and
        // stores the damped values back into JSTX and JSTY
        RollRate = FlightControls.ApplyRollKeys(RollRate, input.RollLeft, input.RollRight, AutoRecentre);
        PitchRate = FlightControls.ApplyPitchKeys(PitchRate, input.PullUp, input.PitchDown, AutoRecentre);
        RollRate = FlightControls.DampRollRate(RollRate, DampingDisabled);
        PitchRate = FlightControls.DampPitchRate(PitchRate, DampingDisabled);

        UpdateAngles();
    }

    /// <summary>
    /// Turns the ship by the amount MVEIT part 8 would turn it for a pair of rotation counters.
    /// </summary>
    /// <remarks>
    /// Because our ship never moves and the universe turns around it, the turn is applied as the
    /// inverse rotation of the world, exactly as MVS4 does it for the keyboard. The counter's
    /// magnitude is the angle directly: a counter of <c>m</c> is <c>m</c> frames of turning at the
    /// original's fixed 1/16 radian a frame, so the turn it asks for is <c>m</c> steps. The rate is
    /// derived only for the dashboard's RL and DC dials - and derived to preserve the step count
    /// exactly, which is why it is scaled by <see cref="CounterToAngleStep"/> rather than by four:
    /// the flight model's own rate-to-angle conversion divides small values by eight, and would
    /// otherwise round a small counter away to no turn at all.
    /// </remarks>
    public void SetCounters(byte rollCounter, byte pitchCounter)
    {
        (RollAngle, RollSign) = CounterToAngle(rollCounter, pitchSignInverted: false);
        (PitchAngleValue, PitchSign) = CounterToAngle(pitchCounter, pitchSignInverted: true);

        RollRate = CounterToRate(rollCounter, pitchSignInverted: false);
        PitchRate = CounterToRate(pitchCounter, pitchSignInverted: true);
    }

    /// <summary>
    /// What the flight model multiplies a counter by to turn it back into the angle MVS5 gives.
    /// </summary>
    /// <remarks>
    /// <see cref="FlightControls.RollAngle"/> and <see cref="FlightControls.PitchAngle"/> halve
    /// their input again whenever the quartered value would be under eight, so the step size is
    /// eight in that range and four above it. Seven is the largest angle in the halved range.
    /// </remarks>
    private const int CounterToAngleStep = 8;

    private const int HalvedAngleLimit = 7;

    /// <summary>Angle units one frame of MVS5's turn is worth in the world rotation.</summary>
    private const int StepsPerCounter = 16;

    /// <summary>The largest angle the world rotation can be given in one go.</summary>
    private const int MaxAngle = 31;

    /// <summary>
    /// The angle the original would turn a ship through for a rotation counter, and the sign.
    /// </summary>
    /// <remarks>
    /// A counter is a number of frames of turning at a fixed 1/16 radian a frame, so its magnitude
    /// is the angle directly.
    ///
    /// The sign is the counter's sign bit read the same way round as a key rate: a counter with
    /// bit 7 set is a <em>negative</em> rotation, which is a roll to the right or a pull up, and
    /// that is exactly how the flight model already reads a rate below the centre. So the sign bit
    /// carries straight over, and a counter and the matching key give the same turn.
    /// </remarks>
    private static (byte Angle, byte Sign) CounterToAngle(byte counter, bool pitchSignInverted)
    {
        int magnitude = counter & 0x7F;
        bool negative = (counter & 0x80) != 0;

        // The roll counter's sign reads the same way round as a key rate. The pitch counter's does
        // not: the flight model takes a rate below the centre as a pull up, so a pitch counter with
        // bit 7 set has to come out as a rate below the centre. This is measured, twice over: a
        // pitch counter of 0x02 moves a station that is above the centre line down towards it, and
        // 0x82 moves it up.
        bool rateBelowCentre = pitchSignInverted ? negative : !negative;

        // A counter of m is m frames of MVS5's fixed 1/16 radian step, so the turn it asks for is m
        // steps. One step is sixteen of the world rotation's angle units, and the rotation cannot
        // be given more than 31 of them at once, so a larger counter is clamped here and the rest
        // of its steps are turned in the ship loop.
        int angle = Math.Min(magnitude * StepsPerCounter, MaxAngle);
        return ((byte)angle, (byte)(rateBelowCentre ? 0x00 : 0x80));
    }

    /// <summary>
    /// The key rate that would print the counter's turn on the dashboard's roll and pitch dials.
    /// </summary>
    /// <remarks>
    /// A zero magnitude is no turn whatever its sign bit says, and both encodings of it appear in
    /// DOCKIT: it writes 0 to stop pitching, and 128 — sign bit set, magnitude zero — as its
    /// "no turn" roll counter. Reading 128 as a turn would give a rate below the centre and leave
    /// the autopilot forever rolling gently to one side, which is exactly what it did.
    /// </remarks>
    private static byte CounterToRate(byte counter, bool pitchSignInverted)
    {
        int magnitude = Math.Min(counter & 0x7F, 31);
        if (magnitude == 0)
        {
            return FlightControls.Centre;
        }

        int deviation = magnitude <= HalvedAngleLimit
            ? magnitude * CounterToAngleStep
            : magnitude * 4;

        bool negative = (counter & 0x80) != 0;
        bool rateBelowCentre = pitchSignInverted ? negative : !negative;

        int rate = rateBelowCentre
            ? FlightControls.Centre - deviation
            : FlightControls.Centre + deviation;

        return (byte)Math.Clamp(rate, 0, 255);
    }

    /// <summary>Derives the roll and pitch angles from the current rates.</summary>
    private void UpdateAngles()
    {

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

    /// <summary>The targetable area of a ship, for callers that need to test their own aim.</summary>
    public int TargetableAreaOf(Ship ship) => TargetableAreaProvider(ship);

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
            // The space station keeps its roll. The original gives it a random clockwise roll when
            // it is created — a random value with bit 7 cleared, which is a roll with a 1 in 127
            // chance of having no damping — and the station visibly turns for as long as it is
            // there. MVEIT spends a counter as it uses it, so the station's roll is renewed from
            // the value it was created with.
            if (ship.Type == Combat.SpaceStationType)
            {
                ship.Data[ShipDataBlock.RollCounter] = ship.SpinRoll;
            }

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
