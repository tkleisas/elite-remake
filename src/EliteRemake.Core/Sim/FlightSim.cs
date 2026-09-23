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
    /// Every ship destroyed since the game last drained the kill reports, from lasers, missiles,
    /// collisions and the energy bomb alike. The game pays each one its own bounty, counts its kill
    /// and reads its legal status, so they are collected rather than overwritten — a bomb or a busy
    /// frame destroys several, and the last would have swallowed the rest.
    /// </summary>
    public IReadOnlyList<Ship> KillReports => _killReports;

    private readonly List<Ship> _killReports = [];

    /// <summary>Reports a destroyed ship, for the game to pay when it drains the reports.</summary>
    private void ReportKill(Ship destroyed) => _killReports.Add(destroyed);

    /// <summary>Reports what a destroyed ship left behind, for the game to spawn when it drains.</summary>
    private void ReportDrops(Ship destroyed, (int Type, int Count) drops)
    {
        if (drops.Count > 0)
        {
            _dropReports.Add((destroyed, drops.Type, drops.Count));
        }
    }

    /// <summary>
    /// Returns every ship destroyed since the last drain and empties the report. Draining is the
    /// game layer's job, once per drawn frame: the simulation reports, the game pays.
    /// </summary>
    public List<Ship> DrainKillReports()
    {
        List<Ship> drained = [.. _killReports];
        _killReports.Clear();
        return drained;
    }

    /// <summary>
    /// Which space view we are looking through, which is the original's VIEW, and so which laser
    /// fires and which way round the crosshair test is done.
    /// </summary>
    public SpaceView View { get; set; } = SpaceView.Front;

    /// <summary>
    /// Which mount is firing, which follows the view: the original reads <c>LASER,X</c> with X set to
    /// VIEW, so the laser fitted to the window we are looking through is the one that fires.
    /// </summary>
    public LaserMount ActiveMount => Plut.Mount(View);

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

    /// <summary>
    /// Throws the whole bubble away, which is what the original's RES2 does to the flight variables
    /// and workspaces when we launch or arrive somewhere new.
    /// </summary>
    /// <remarks>
    /// RES2 also clears the missile target: "Reset MSTG, the missile target, to &amp;FF (no target)".
    /// Without this a lock survived the launch it was reset by and pointed at a ship that is no
    /// longer in the bubble, so the next missile flew at nothing.
    /// </remarks>
    public void ClearBubble()
    {
        _bubble.Clear();
        MissileLock = null;
    }

    /// <summary>Removes every ship whose status has marked it for removal.</summary>
    /// <summary>
    /// Removes the ships that have been destroyed, releasing the missile lock if it was on one.
    /// </summary>
    /// <remarks>
    /// The original's KILLSHP clears the missile lock when the ship it is locked onto is destroyed —
    /// "we need to remove our missile lock, so call ABORT to unarm the missile and update the missile
    /// indicators" — and it does so for every kill, not only for a kill by our own missile. Without
    /// this the lock survives its target, so the missile indicators keep showing a target that is no
    /// longer there and the next missile flies at a ship that has been removed from the bubble.
    /// </remarks>
    public int RemoveKilledShips()
    {
        int removed = _bubble.RemoveAll(ship => ship.IsKilled);

        if (MissileLock is { IsKilled: true })
        {
            MissileLock = null;
            MissileUnarmedThisFrame = true;
        }

        return removed;
    }

    /// <summary>
    /// Runs the space station's tactics: it launches a shuttle or a transporter to trade with the
    /// planet, or the police if we have annoyed it.
    /// </summary>
    /// <remarks>
    /// The original's limits are counted over the whole bubble: it refuses to launch a trader while
    /// one is already out there, and stops sending police once four are about.
    /// </remarks>
    private void LaunchFromStation(Ship station)
    {
        bool transporter = false;
        int cops = 0;

        foreach (Ship other in _bubble)
        {
            if (other.Type == Tactics.TransporterType)
            {
                transporter = true;
            }
            else if (other.Type == Tactics.CopType)
            {
                cops++;
            }
        }

        int type = Tactics.StationLaunch(station, Random, transporter, cops);
        if (type == 0)
        {
            return;
        }

        if (SpawnFromParent(type, station) is not { } launched)
        {
            return;
        }

        // Whatever the station launches gets its AI from the same three instructions in the original,
        // and its personality from the E% flags for its type. A police Viper is a bounty hunter and a
        // Shuttle is a trader; neither is hostile in itself, so it is their NEWB flags and not this
        // line that decide whether they shoot at us.
        launched.AiFlag = Tactics.StationLaunchAiFlag;

        StationLaunchedThisFrame = launched;
    }

    /// <summary>The ship a station launched this frame, for the game to report.</summary>
    public Ship? StationLaunchedThisFrame { get; private set; }

    /// <summary>
    /// A ship that has given up launches its escape pod and is left drifting, as the original's
    /// SESCP does: the pod is spawned as a child of the ship and the ship itself has its AI switched
    /// off, so it becomes a sitting duck rather than a threat.
    /// </summary>
    /// <remarks>
    /// The pod is a ship of type 3 and carries the AI flag of a free-floating pod, so it drifts
    /// rather than manoeuvres. Shooting it is worth nothing and scooping it fills the hold with
    /// slaves, which is the original's own grim joke.
    /// </remarks>
    private void LaunchEscapePod(Ship ship)
    {
        // The ship has bailed out: no more tactics from it, which is what "sitting duck" means
        ship.AiFlag = 0;

        var pod = new Ship(ShipTypes.EscapePod, "escape-pod", "Escape pod")
        {
            MaxSpeed = BlueprintDefaults.For(ShipTypes.EscapePod).MaxSpeed,
            AiFlag = 0,
            Speed = BlueprintDefaults.For(ShipTypes.EscapePod).Speed,
        };

        (int x, int y, int z) = ship.GetPosition();
        pod.SetPosition(x, y, z + 256);

        if (Spawn(pod))
        {
            EscapePodLaunchedThisFrame = ship;
        }
    }

    /// <summary>The ship that launched an escape pod this frame, for the game to report.</summary>
    public Ship? EscapePodLaunchedThisFrame { get; private set; }

    /// <summary>
    /// A ship fires a missile at us, as the original's SFRMIS does: the missile is spawned as a child
    /// of the ship, pointing at us, and the ship's own missile count goes down by one.
    /// </summary>
    /// <remarks>
    /// A Thargoid does not launch a missile: it launches one of the Thargons it carries, which is the
    /// same code path on the disc and the reason a Thargoid is dangerous to leave alone.
    /// </remarks>
    private void FireMissileFrom(Ship ship)
    {
        ship.Missiles = (byte)(ship.Missiles - 1);

        if (ship.Type == Tactics_ThargoidType)
        {
            SpawnFromParent(Debris.Thargon, ship);
            return;
        }

        // A missile aimed at us has no target ship, which is how the update loop tells ours from
        // theirs: theirs home on us
        var missile = new Ship(Missiles.MissileType, "missile", "Missile")
        {
            Speed = MissileSpeed,
            AiFlag = Tactics.AiEnabled,
        };

        (int x, int y, int z) = ship.GetPosition();
        missile.SetPosition(x, y, z);

        // It starts pointing at us, so it does not have to turn before it can chase. The original's
        // SFRMIS copies the launch ship's own orientation and lets the missile steer from there;
        // pointing it straight at us is the same idea without the turn.
        double distance = Math.Sqrt(((double)x * x) + ((double)y * y) + ((double)z * z));
        if (distance >= 1)
        {
            double heading = Math.Atan2(-x, -z);
            double pitch = Math.Asin(Math.Clamp(-y / distance, -1, 1));
            Orientation.FromHeadingPitch(heading, pitch).AsSpan()
                .CopyTo(missile.Data[ShipDataBlock.Orientation..]);
        }

        if (Spawn(missile))
        {
            MissileFiredAtUsThisFrame = ship;
        }
    }

    /// <summary>The ship that launched a missile at us this frame, for the game to report.</summary>
    public Ship? MissileFiredAtUsThisFrame { get; private set; }

    /// <summary>How fast an enemy missile flies, which is the same as one of ours.</summary>
    private const int MissileSpeed = 44;

    /// <summary>The Thargoid's ship type, which launches Thargons rather than missiles.</summary>
    private const int Tactics_ThargoidType = 29;

    /// <summary>
    /// Spawns the smaller ship an Anaconda releases, near the Anaconda and under its own AI.
    /// </summary>
    /// <remarks>
    /// The spawned ship is put just ahead of the ship that released it, which is what the original's
    /// SFS1 does when it is called from TACTICS: the ship workspace holds the parent, so the child
    /// starts from the parent's position and orientation.
    /// </remarks>
    private Ship? SpawnFromParent(int type, Ship parent)
    {
        (int x, int y, int z) = parent.GetPosition();
        BlueprintDefaults defaults = BlueprintDefaults.For(type);

        // Just ahead of the parent, which is where the original's workspace puts it. It carries the
        // blueprint's own figures, as every ship NWSHP creates does: without them a child arrives
        // with no speed and simply hangs in space, which is what a station's shuttle did before the
        // game layer's blueprint lookup happened to cover for it.
        var child = Ship.Create(type, string.Empty, $"Type {type}", x, y, z + 256, 0, 0);
        child.AiFlag = Tactics.SpawnedShipAiFlag;
        child.Speed = defaults.Speed;
        child.MaxEnergy = defaults.MaxEnergy;
        child.MaxSpeed = defaults.MaxSpeed;
        child.VisibilityDistance = defaults.VisibilityDistance;
        child.NewbFlags = defaults.NewbFlags;
        child.Energy = child.MaxEnergy;

        if (!Spawn(child))
        {
            return null;
        }

        ShipSpawned?.Invoke(child);
        return child;
    }

    /// <summary>
    /// The chance that the trader-or-junk branch produces a trader rather than a rock: the original
    /// branches on the V flag of a random byte, which is set half the time.
    /// </summary>
    public const int TraderIn256 = 128;

    /// <summary>
    /// How many iterations of the original's main loop make a second.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not 50.</b> Fifty hertz is the BBC's television refresh, and it is easy to mistake for the
    /// game's rate, but the original's main loop is not locked to the screen at all: it iterates as
    /// fast as the 6502 can get round it, and everything in the game — movement, rotation, the AI,
    /// spawning — happens once per iteration, whatever that is worth in real time.
    /// </para>
    /// <para>
    /// The figure here is measured rather than assumed. Mark Moxon, whose disassembly this port
    /// follows, timed the main loop counter on the disc version running in an emulator: 256
    /// iterations took about 22 seconds on the title screen and about 20 seconds with the station in
    /// view, which is 11.5 to 13 iterations a second; a busy scene with the sun and enemy ships
    /// drops to 4 or 5; and an empty side view reaches about 36. The typical case in flight is
    /// therefore around twelve and a half, which is what the simulation steps at.
    /// </para>
    /// <para>
    /// That the two clocks are separate is visible in the original's own source: on the BBC the
    /// laser's pulse counter is decremented "every vertical sync (in the LINSCN routine, which is
    /// called 50 times a second)", while on the Electron the same counter is decremented "by 4 on
    /// each iteration around the main game loop".
    /// </para>
    /// <para>
    /// A fixed rate cannot reproduce a rate that varied with how much was on screen, and it should
    /// not try: the original's 4 frames a second in a crowded fight is not a feature worth having.
    /// What matters is that one iteration of our simulation is one iteration of the original's, so
    /// that every ported constant is worth what it was worth, and that is what this rate gives.
    /// </para>
    /// </remarks>
    public const float IterationsPerSecond = 12.5f;

    /// <summary>Advances the simulation by one iteration of the original's main loop.</summary>
    public void Step(FlightInput input = default)
    {
        UpdateSpeed(input);
        UpdateRotation(input);
        UpdateLasers(input);

        DamageTakenThisFrame = 0;
        MissileUnarmedThisFrame = false;
        EscapePodLaunchedThisFrame = null;
        MissileFiredAtUsThisFrame = null;
        StationLaunchedThisFrame = null;

        // The original's SSPR, which is the count of stations in our bubble and so is true for as
        // long as the station is with us. It guards two things: nothing spawns while it is set, and
        // a hostile pirate inside the zone loses its aggression.
        bool stationPresent = StationIsPresent;

        for (int slot = 0; slot < _bubble.Count; slot++)
        {
            Ship ship = _bubble[slot];

            if (Tactics.RunsTacticsThisFrame(ship, slot, MainLoopCounter))
            {
                // TACTICS decides what this ship is before it decides what to do: a trader may turn
                // out to be a pirate, and a bounty hunter only comes for a commander who is nearly a
                // fugitive. Both rewrite the ship's own NEWB flags, so the decision is made once.
                Tactics.DecideRole(ship, Random, Commander?.LegalStatus ?? 0, stationPresent);

                // An Anaconda may release the ship it carries, which is part of TACTICS in the original
                if (Tactics.ShouldReleaseShip(ship, Random))
                {
                    SpawnFromParent(Tactics.WormType, ship);
                }

                // TACTICS runs before the ship is moved, as it does in the original.
                //
                // What we lost is measured from the shields and the energy banks together, and it is
                // recorded whether or not the hit was fatal. This used to be counted only when
                // TakeDamage reported a kill, which is when the banks reach zero — so every hit the
                // shields absorbed was recorded as no damage at all. That is nearly all of them: the
                // game therefore made no sound when we were being hit, and the tests that watched
                // this figure were watching nothing.
                int before = Player.Energy + Player.ForeShield + Player.AftShield;
                bool fatal = Tactics.Apply(
                    ship,
                    Random,
                    Player,
                    LaserPowerOf(ship),
                    DamageOf(ship),
                    PlanetPosition,
                    StationPosition);
                int lost = before - (Player.Energy + Player.ForeShield + Player.AftShield);
                if (lost > 0)
                {
                    DamageTakenThisFrame += lost;
                }

                if (fatal || Player.Energy == 0)
                {
                    PlayerDied = true;
                }

                // A ship that is into the last eighth of its energy may give up and take to its
                // escape pod, which is part of the same routine
                if (Tactics.ShouldLaunchEscapePod(ship, Random))
                {
                    LaunchEscapePod(ship);
                }

                // And one with less than half its energy in the banks may spend a missile on us
                if (Tactics.ShouldFireMissile(ship, Random, EcmActive))
                {
                    FireMissileFrom(ship);
                }

                // The station is the one ship whose tactics are not its own manoeuvring: it launches
                // the shuttles and transports that trade with the planet, or the police, if we have
                // annoyed it
                if (ship.Type == Combat.SpaceStationType)
                {
                    LaunchFromStation(ship);
                }
            }

            Mveit(ship, slot);
        }

        // The station appears when we reach the planet, which is part 14 of the original's flight
        // loop running once every thirty-two iterations
        UpdateStationSpawn();

        // Recharge the energy banks and, above half full, the shields, which part 13 of the
        // original's flight loop does every eighth iteration: "LDA MCNT / AND #7 / BNE MA22", whose
        // own summary calls it "every 7 iterations" because it counts the seven it skips.
        //
        // This used to run every iteration, so the banks recovered eight times as fast as the
        // original's and the shields with them. It was invisible in a quiet sky and decisive in a
        // fight: the E.C.M.'s drain of a unit an iteration, the laser's own energy cost and every
        // hit we took were all being cancelled seven times out of eight.
        if ((MainLoopCounter & 7) == 0)
        {
            Combat.RechargeShields(Player);
            Combat.RechargeEnergy(Player);
        }

        // Flying into another ship hurts us badly and annoys it
        UpdateCollisions();

        // Flying into a planet or a sun is the end of us
        HitABody = false;
        UpdateAltitudeChecks();
        UpdateSunHeatAndScooping();

        // The energy bomb, if it is going off, kills everything in reach
        UpdateEnergyBomb();

        // Missiles home in, and the E.C.M. swats them down
        UpdateMissiles();

        // Scoop anything we are flying at, if we have the equipment for it
        UpdateScooping();

        // Explosion clouds grow and then take the wreck with them, which the original does as it
        // draws each one: "ADC #4 / BCS EX2" on the cloud counter, and EX2 sets the killed bit
        UpdateExplosions();

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
    /// What destroyed ships have left behind since the game last drained the reports: the wreck
    /// they came from, the type of thing dropped and how many, so the game can spawn them beside
    /// the wreck they came from. Collected rather than overwritten, for the same reason the kills
    /// are.
    /// </summary>
    public IReadOnlyList<(Ship Destroyed, int Type, int Count)> DropReports => _dropReports;

    private readonly List<(Ship Destroyed, int Type, int Count)> _dropReports = [];

    /// <summary>Returns every drop since the last drain and empties the report.</summary>
    public List<(Ship Destroyed, int Type, int Count)> DrainDropReports()
    {
        List<(Ship Destroyed, int Type, int Count)> drained = [.. _dropReports];
        _dropReports.Clear();
        return drained;
    }

    /// <summary>Scooped cargo this frame, if any, for the game to report.</summary>
    public (int Item, int Amount)? ScoopedThisFrame { get; private set; }

    /// <summary>The commander, so scooping knows what is fitted and where cargo goes.</summary>

    /// <summary>How a canister's contents are decided, from the blueprints.</summary>
    /// <summary>
    /// What scooping a ship yields, as a commodity number for the hold.
    /// </summary>
    /// <remarks>
    /// The numbering is the original's market item numbering, which it also uses directly as the
    /// hold's slot number: item 3 is slaves, item 12 is furs, item 16 is alien items. The source's own
    /// comments for the three scoopable ships are what fix the convention — the escape pod's nibble
    /// gives 3 and it says slaves, the thargon's gives 16 and it says alien items — and 0 means the
    /// blueprint names no commodity, which for a cargo canister is what sends the game to the
    /// original's own random-contents path.
    /// </remarks>
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
    /// True while we are in witchspace, which is the original's MJ flag.
    /// </summary>
    /// <remarks>
    /// The flag is owned here rather than by the game because the flight routines read it: WARP
    /// refuses an in-system jump in witchspace, and witchspace is a state of the flight simulation
    /// rather than of the session around it.
    /// </remarks>
    public bool InWitchspace { get; set; }

    /// <summary>How far an in-system jump takes us, in the original's units.</summary>
    /// <remarks>
    /// WARP adds <c>&amp;81</c> to the planet's and the sun's z_sign — the top byte of the 24-bit
    /// coordinate — and throws the low byte of the result away, which is one step of 65536 units. The
    /// addition is sign-magnitude: against a body in front of us it takes one off the magnitude, and
    /// against one behind us it adds one, so either way the body moves away along our own z axis.
    /// That is the whole of the jump: we have travelled forward and the sky has not changed.
    /// </remarks>
    public const int InSystemJumpDistance = 65536;

    /// <summary>
    /// The original's WARP, which the "J" key calls: an in-system jump towards the planet.
    /// </summary>
    /// <returns>
    /// True if we jumped; false if WARP refused, which in the original is the WA1 branch that makes a
    /// long, low beep. The caller is what plays it.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A jump is refused when there is anything in the bubble but junk — WARP reads the slot just
    /// past the junk in the FRIN table, and the original keeps its junk in the first slots — when a
    /// space station is present, or when we are in witchspace, which has no planet to jump towards.
    /// It is refused again when we are facing a body and are already too close to it: WARP ORs the
    /// magnitudes of the three top bytes of the body's position, halves the result and refuses if it
    /// has become zero, so "too close" means less than two steps of 65536 in every axis.
    /// </para>
    /// <para>
    /// Only the planet and the sun move. Everything else — junk, which the refusal above allows to be
    /// there, and our own speed and heading — stays exactly where it was, so a rock we were flying
    /// alongside is still alongside us after the jump.
    /// </para>
    /// </remarks>
    public bool TryInSystemJump()
    {
        // "ORA MJ": there is no in-system jump in witchspace, which has neither planet nor sun
        if (InWitchspace)
        {
            return false;
        }

        // "LDX JUNK / LDA FRIN+2,X / ORA SSPR": anything but junk stops the jump. The two slots in
        // front of the junk hold the planet and the sun, which is why the original starts its count
        // at FRIN+2; here they are ordinary members of the bubble, so they are skipped by type.
        foreach (Ship ship in _bubble)
        {
            if (!IsSystemBody(ship.Type) && !Debris.IsJunk(ship.Type))
            {
                return false;
            }
        }

        // "If we are facing the planet and are too close to it, we can't jump past it"
        if (TooCloseToJump(SystemArrival.PlanetTypeA) ||
            TooCloseToJump(SystemArrival.PlanetTypeB) ||
            TooCloseToJump(ShipTypes.Sun))
        {
            return false;
        }

        bool moved = false;
        foreach (Ship body in _bubble)
        {
            if (!IsSystemBody(body.Type))
            {
                continue;
            }

            (int x, int y, int z) = body.GetPosition();
            body.SetPosition(x, y, z - InSystemJumpDistance);
            moved = true;
        }

        if (!moved)
        {
            return false;   // nowhere to jump: no planet and no sun in the bubble
        }

        // "Set the main loop counter to 1, so the next iteration through the main loop will
        // potentially spawn ships"
        MainLoopCounter = 1;

        // "Set EV, the extra vessels spawning counter, to 0"
        _spawnDelay = 0;

        // The original goes on to LOOK1 with QQ11 forced non-zero, which clears the screen, redraws
        // the crosshairs and sets up a new stardust field. Clearing the screen here means no more
        // than returning true: the ships that were drawn are gone from the bubble, and the caller
        // re-seeds the stardust.
        return true;
    }

    /// <summary>
    /// Whether one of the original's WARP proximity checks applies to a body: it refuses the jump
    /// when the body is in front of us and closer than two steps of the top byte.
    /// </summary>
    /// <remarks>
    /// WARP tests the body's z_sign first and skips the check when bit 7 is set, which for the
    /// original's sign-magnitude byte means the body is behind us. The port's coordinates are signed
    /// integers, so that is a test for a negative z — and a body exactly level with us counts as in
    /// front, as it does in the original, where z_sign is then +0 with bit 7 clear.
    /// </remarks>
    private bool TooCloseToJump(int type)
    {
        foreach (Ship body in _bubble)
        {
            if (body.Type != type)
            {
                continue;
            }

            (int x, int y, int z) = body.GetPosition();

            if (z < 0)
            {
                return false;
            }

            int topByte = (Math.Abs(x) >> 16) | (Math.Abs(y) >> 16) | (Math.Abs(z) >> 16);

            // "LSR A / BEQ WA1": the original halves the OR of the three magnitudes and refuses the
            // jump if the result is zero
            return topByte < 2;
        }

        return false;
    }

    /// <summary>True for the planet and the sun, which are the bodies the simulation places itself.</summary>
    private static bool IsSystemBody(int shipType) =>
        shipType is SystemArrival.PlanetTypeA or SystemArrival.PlanetTypeB or ShipTypes.Sun;

    /// <summary>
    /// The original's ABORT, which the "U" key calls: the missile we have locked onto a target is
    /// unarmed, so the lock is released and the indicator goes back to green.
    /// </summary>
    /// <returns>
    /// True if there was a missile to unarm, which is the original's test of NOMSL; the caller plays
    /// the long, low beep that tells us the missile is no longer aimed at anything.
    /// </returns>
    public bool UnarmMissile()
    {
        if (Commander is not { Missiles: > 0 })
        {
            return false;
        }

        MissileLock = null;
        MissileUnarmedThisFrame = true;
        return true;
    }

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
        InWitchspace = true;

        // RES2 is what MJP runs to set the ambush up, and it clears the missile target as it goes
        MissileLock = null;

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

    /// <summary>The acceleration the original's ANGRY gives a ship it has just been shot at.</summary>
    public const byte AngryAcceleration = 2;

    /// <summary>
    /// Makes a ship angry, as the original's ANGRY does when we shoot it.
    /// </summary>
    /// <remarks>
    /// Two things happen. The ship's AI is switched on if it was off, and its acceleration is raised,
    /// so that a trader we have just shot at stops being a bystander. And if the ship is an
    /// <em>innocent</em> — bit 5 of its NEWB flags — the space station is made hostile too:
    ///
    /// <code>
    /// LDY #36 / LDA (INF),Y / AND #%00100000   \ the ship's NEWB flags
    /// BEQ P%+5 / JSR AN2                       \ an innocent means the station turns on us
    /// </code>
    ///
    /// That is the enhanced versions' rule and the disc's, and it is what stops shooting at traders
    /// being free: the station you are trying to dock at takes an interest.
    /// </remarks>
    public void MakeAngry(Ship ship)
    {
        if (ship.IsInnocent)
        {
            MakeStationHostile();
        }

        if (ship.AiFlag != 0)
        {
            // Bit 7 is AI enabled, so a ship with an AI flag but bit 7 clear starts acting
            ship.AiFlag |= 0x80;
        }

        // And whatever it was doing, it accelerates now: the original sets byte #28 to 2
        ship.Acceleration = AngryAcceleration;
    }

    /// <summary>
    /// Makes the space station hostile, as the original's ANGRY and AN2 do.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On this build hostility is a NEWB flag and the AI flag's bit 7 means only that the ship has
    /// AI: AN2 is <c>LDA K%+NI%+36 / ORA #%00000100 / STA K%+NI%+36</c>, which sets bit 2 of the
    /// station's NEWB flags. That is why a hostile station sends the police after us rather than
    /// simply shooting: its tactics branch on the hostile bit.
    /// </para>
    /// <para>
    /// Setting the AI flag instead was reading the cassette version, where bit 7 of the AI flag is
    /// what makes a station hostile. It also would have broken docking outright once the station
    /// started carrying the AI flag NWSPS gives it, because the docking check asked the same
    /// question of the same byte.
    /// </para>
    /// </remarks>
    private void MakeStationHostile()
    {
        foreach (Ship ship in _bubble)
        {
            if (ship.Type != Combat.SpaceStationType || ship.IsKilled)
            {
                continue;
            }

            ship.NewbFlags |= Ship.NewbHostile;

            // ANGRY sets bit 7 of the AI flag to make sure the ship can act, and leaves a ship with
            // no AI flag at all alone — it has neither AI nor aggression to work with
            if (ship.AiFlag == 0)
            {
                continue;
            }

            ship.AiFlag |= 0x80;
            ship.Acceleration = AngryAcceleration;

            // "Set the ship's byte #30 (pitch counter) to 4, so it starts diving"
            ship.Data[ShipDataBlock.PitchCounter] = HostileStationPitchCounter;
        }
    }

    /// <summary>The pitch counter ANGRY gives a ship it has annoyed, so that it starts diving.</summary>
    public const byte HostileStationPitchCounter = 4;

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

    /// <summary>
    /// Set for a frame when the missile lock is released, for the game to sound.
    /// </summary>
    /// <remarks>
    /// The original makes its long, low beep at the two points the missile is unarmed — "to make a
    /// low, long beep to indicate the missile is now unarmed" — which are firing one and losing the
    /// lock because the target was destroyed. This flag is set at both, and the game clears it.
    /// </remarks>
    public bool MissileUnarmedThisFrame { get; set; }

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
            // Energy bombs are useless against space stations, and "ships can't explode more than
            // once": a ship that is already exploding is left alone
            if (ship.Type == Combat.SpaceStationType ||
                ship.IsKilled ||
                ship.IsExploding ||
                IsCelestial(ship.Type))
            {
                continue;
            }

            // The bomb does not remove a ship either: it starts its explosion, and the cloud's own
            // counter is what takes the wreck away about sixty iterations later. The original's
            // part 5 sets the killed bit and lets LL9 start the cloud as it draws; either way the
            // player sees the ship blow up rather than vanish.
            ship.StartExplosion();
            ReportKill(ship);
        }
    }

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
    /// enough to a planet for the top byte of its position to be zero, the squares of the high bytes
    /// of the position say how far above its surface we are, and if they come to no more than the
    /// planet's radius then we have flown into it.
    /// </summary>
    /// <remarks>
    /// This is the original's planet check, which is the first ship slot and runs on iteration 10 of
    /// every 32. The sun is deliberately not checked here: it has its own slot, its own iteration and
    /// its own death, by heat rather than by impact, in <see cref="UpdateSunHeatAndScooping"/>. Leaving
    /// the sun in this loop killed us at the impact radius instead, which is further out than the heat
    /// death, so the cabin temperature never had the chance to rise.
    /// </remarks>
    private void UpdateAltitudeChecks()
    {
        // The original runs this on iteration 10 of every 32
        if ((MainLoopCounter & 31) != 10)
        {
            return;
        }

        foreach (Ship body in _bubble)
        {
            if (!IsPlanet(body.Type))
            {
                continue;
            }

            (int x, int y, int z) = body.GetPosition();

            if (TopByteCap(x, y, z) != 0)
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

    /// <summary>The cabin temperature in deep space, one notch up the dashboard's bar.</summary>
    public const int DeepSpaceCabinTemperature = 30;

    /// <summary>The cabin temperature at which the sun starts topping up the fuel tank.</summary>
    public const int ScoopingCabinTemperature = 224;

    /// <summary>The cabin temperature of a ship that is close enough to the sun to be cooked.</summary>
    public const int FatalCabinTemperature = 255;

    /// <summary>
    /// How hot the cabin is, which the dashboard shows as the CT bar. It is 30 in deep space and
    /// climbs as we approach the sun.
    /// </summary>
    public int CabinTemperature { get; private set; } = DeepSpaceCabinTemperature;

    /// <summary>Set when the sun has cooked us this frame.</summary>
    public bool CookedByTheSun { get; private set; }

    /// <summary>
    /// The original's sun check, which runs on iteration 20 of every 32: the sun heats the cabin,
    /// and once it is hot enough the fuel scoops start topping up the tank, and if it gets hotter
    /// still the cabin temperature goes off the scale and we die.
    /// </summary>
    /// <remarks>
    /// The temperature comes from the same Pythagoras as the planet altitude check but inverted, so
    /// a large distance gives a low reading: the sum of the squares of the high bytes of the sun's
    /// position is subtracted from 285, which makes the bar climb as the sun fills the view. The
    /// original only runs this when no space station is in the bubble, because the station and the
    /// sun are never both near us, and the station occupies the same ship slot as the sun.
    /// </remarks>
    private void UpdateSunHeatAndScooping()
    {
        CookedByTheSun = false;

        // The original runs this on iteration 20 of every 32
        if ((MainLoopCounter & 31) != 20)
        {
            return;
        }

        Ship? sun = null;
        foreach (Ship body in _bubble)
        {
            if (body.Type == ShipTypes.Sun)
            {
                sun = body;
                break;
            }
        }

        if (sun is null)
        {
            return;
        }

        // The space station and the sun share a ship slot in the original, so the sun's check is
        // skipped while a station is in the bubble
        if (StationIsPresent)
        {
            CabinTemperature = DeepSpaceCabinTemperature;
            return;
        }

        (int x, int y, int z) = sun.GetPosition();

        // MAS2: the OR of the top bytes. If any of them is non-zero we are more than 65535 away
        // and are merely in deep space, which is as cool as the cabin gets
        if (TopByteCap(x, y, z) != 0)
        {
            CabinTemperature = DeepSpaceCabinTemperature;
            return;
        }

        // MAS3: the high bytes of the three coordinates, squared, added a byte at a time. The
        // original adds only the high byte of each square and gives up with &FF if the sum
        // overflows, which is how it tells "near the sun" from "not near enough"
        int xHi = Math.Abs(x) >> 8 & 0xFF;
        int yHi = Math.Abs(y) >> 8 & 0xFF;
        int zHi = Math.Abs(z) >> 8 & 0xFF;
        int sum = ((xHi * xHi) >> 8) + ((yHi * yHi) >> 8);
        if (sum > 0xFF)
        {
            // A fair way from the sun, and the original's comment for this reads "ouch, hot, hot,
            // hot" of the opposite case: an overflowing sum means a long way out
            CabinTemperature = DeepSpaceCabinTemperature + 1;
            return;
        }

        sum += (zHi * zHi) >> 8;
        if (sum > 0xFF)
        {
            CabinTemperature = DeepSpaceCabinTemperature + 1;
            return;
        }

        // The summed squares are inverted and offset to give the temperature, which is 30 far out
        // and climbs towards 255 as the sun's disc fills the view. If the inversion carries then
        // the sun is too close to survive
        int temperature = 285 - sum;
        if (temperature > 255)
        {
            CabinTemperature = temperature - 256;
            CookedByTheSun = true;
            PlayerDied = true;
            Player.Energy = 0;
            return;
        }

        CabinTemperature = temperature;

        // The cabin has to be this hot before the scoops can reach the sun's material
        if (temperature < ScoopingCabinTemperature)
        {
            return;
        }

        ScoopFuelFromTheSun();
    }

    /// <summary>
    /// Tops the tank up while we are close enough to the sun and have scoops fitted: the original
    /// takes our speed divided by eight, so the faster we skim the more we collect, and caps the
    /// tank at a full 7.0 light years.
    /// </summary>
    private void ScoopFuelFromTheSun()
    {
        if (Commander is not { FuelScoops: true } commander)
        {
            return;
        }

        int scooped = Speed >> 3;
        commander.Fuel = Math.Min(Outfitting.MaxFuel, commander.Fuel + scooped);
    }

    /// <summary>
    /// Where the planet is, which is where the original sends every peaceful ship that is not
    /// docking, or null when there is no planet in the bubble.
    /// </summary>
    private System.Numerics.Vector3? PlanetPosition => PositionOf(SystemArrival.PlanetTypeA);

    /// <summary>Where the station is, or null when there is none in the bubble.</summary>
    private System.Numerics.Vector3? StationPosition => PositionOf(Combat.SpaceStationType);

    /// <summary>The position of the first ship of a given type in the bubble.</summary>
    private System.Numerics.Vector3? PositionOf(int type)
    {
        foreach (Ship ship in _bubble)
        {
            if (ship.Type == type)
            {
                (int x, int y, int z) = ship.GetPosition();
                return new System.Numerics.Vector3(x, y, z);
            }
        }

        return null;
    }

    /// <summary>
    /// Spawns the space station when we reach the planet, which is the original's own way of
    /// putting one in the sky: part 14 of the flight loop runs every thirty-two iterations, and if
    /// there is no station in the bubble — SSPR is clear — and we are close enough to the point one
    /// planetary radius above the planet's surface along its nose vector, it puts a station there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is why arriving in a system leaves you with a planet and no station: the station is in
    /// orbit, and it appears when you get there. The remake used to put one three thousand units
    /// ahead of us the moment we arrived, which made every system a two-minute errand and meant the
    /// planet, whose orbit the station turns with, never had anything to do with docking.
    /// </para>
    /// <para>
    /// The sun goes when the station arrives, because in the original they share a ship slot: NWSPS
    /// clears the second slot of the FRIN table — the sun's — so that the new station is created
    /// into it, and the sun's own comment says the slot is "reserved for the sun (or space station)".
    /// That is also why the cabin temperature check is skipped while a station is about, which this
    /// simulation already did.
    /// </para>
    /// </remarks>
    private void UpdateStationSpawn()
    {
        if ((MainLoopCounter & 31) != 0 || StationIsPresent)
        {
            return;
        }

        foreach (Ship body in _bubble)
        {
            if (!IsPlanet(body.Type))
            {
                continue;
            }

            // The planet itself has to be within 65536 units in every axis before its orbit is in
            // reach: "JSR MAS2 ... if it's non-zero, jump to MA23S ... too far from the planet to
            // bump into a space station"
            (int px, int py, int pz) = body.GetPosition();
            if (TopByteCap(px, py, pz) != 0)
            {
                return;
            }

            ((int x, int y, int z), bool inRange) = SystemArrival.StationSpawnPoint(body);
            if (!inRange)
            {
                return;
            }

            var station = SystemArrival.CreateStation(0, SystemArrival.StationRollCounter);
            station.SetPosition(x, y, z);
            Spawn(station);

            // "The sun and the space station can't both be about": the station takes the sun's slot
            RemoveTheSun();
            return;
        }
    }

    /// <summary>
    /// Ages the explosion clouds: DOEXP adds 4 to a ship's cloud counter each time it draws the
    /// cloud, and when the addition overflows, EX2 sets the status byte's exploding and killed bits
    /// together. The killed bit is what removes the wreck, which is why a destroyed ship is a cloud
    /// for about sixty iterations and then is not there at all.
    /// </summary>
    private void UpdateExplosions()
    {
        foreach (Ship ship in _bubble)
        {
            if (!ship.IsExploding || ship.IsKilled)
            {
                continue;
            }

            int counter = ship.ExplosionCounter + ExplosionTicksPerDraw;
            if (counter > 255)
            {
                ship.IsKilled = true;   // EX2's "ORA #%10100000", the killed half
            }
            else
            {
                ship.ExplosionCounter = (byte)counter;
            }
        }
    }

    /// <summary>How much the cloud counter advances each time the cloud is drawn: DOEXP's "ADC #4".</summary>
    public const int ExplosionTicksPerDraw = 4;

    /// <summary>Takes the sun out of the bubble, as NWSPS clearing the FRIN table's second slot does.</summary>
    private void RemoveTheSun()
    {
        foreach (Ship ship in _bubble.ToArray())
        {
            if (ship.Type == ShipTypes.Sun)
            {
                Remove(ship);
            }
        }
    }

    /// <summary>The station the original spawns, for callers that need to place one themselves.</summary>
    public Ship? SpawnStationAt(int x, int y, int z)
    {
        var station = SystemArrival.CreateStation(0, SystemArrival.StationRollCounter);
        station.SetPosition(x, y, z);

        if (!Spawn(station))
        {
            return null;
        }

        RemoveTheSun();
        return station;
    }

    /// <summary>True while a space station is in the local bubble, which is the original's SSPR.</summary>
    public bool StationIsPresent
    {
        get
        {
            foreach (Ship ship in _bubble)
            {
                if (ship.Type == Combat.SpaceStationType)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>True for the two planet types, which are the bodies that can be crashed into.</summary>
    private static bool IsPlanet(int shipType) =>
        shipType is SystemArrival.PlanetTypeA or SystemArrival.PlanetTypeB;

    /// <summary>
    /// The original's MAS2: the OR of the top bytes of the three coordinates, which is a cap on how
    /// far away a body can be. Anything but zero means it is more than 65535 away in some axis, and
    /// both the planet altitude check and the sun's heat check start by skipping in that case.
    /// </summary>
    /// <remarks>
    /// The magnitudes have to be taken before the shift. A coordinate is signed, and C# shifts a
    /// negative int arithmetically, so a body a thousand units away on the negative side of an axis
    /// shifts to &minus;1 and reports itself as 127 units of top byte — that is, as far away as it is
    /// possible to be. Testing for zero then never passes, and the body we are flying straight at is
    /// never checked. The original has no such trap because it ORs the raw bytes, whose top bit is the
    /// sign and is masked off afterwards.
    /// </remarks>
    private static int TopByteCap(int x, int y, int z) =>
        ((Math.Abs(x) >> 16) | (Math.Abs(y) >> 16) | (Math.Abs(z) >> 16)) & 0x7F;

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
            // rather than a collision, which the missile code handles. The exploding-or-killed bits
            // are ORed into the distance test in the original, so a wreck is past colliding with
            // the moment it starts to blow up — "either the ship is far away, or it is already
            // exploding, or has been flagged as being killed"
            if (ship.IsKilled ||
                ship.IsExploding ||
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
                ship.StartExplosion();
                ReportKill(ship);
                ReportDrops(ship, Debris.DestructionDrops(ship.Type, 0, Random));
            }

            // Which shield takes it comes from the ship's own z_sign, as OOPS does for any attacker:
            // ramming from behind should not take the forward shield
            (_, _, int collisionZ) = ship.GetPosition();
            if (Combat.TakeDamage(Player, CollisionDamageToUs, fromBehind: collisionZ < 0))
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
        MissileUnarmedThisFrame = true;
        return true;
    }

    /// <summary>
    /// How long the E.C.M. runs for: ECBLB2 sets its countdown timer to 32, and part 16 of the
    /// flight loop decrements it once an iteration.
    /// </summary>
    public const int EcmDuration = 32;

    /// <summary>Fires the E.C.M., which destroys every missile in the bubble.</summary>
    /// <remarks>
    /// Switching it on costs nothing. The energy goes a unit at a time while it runs — "LDA ECMP /
    /// BEQ MA69 / JSR DENGY ... deplete our energy banks by 1" — and when the banks are empty the
    /// E.C.M. switches itself off, which is the original's way of making it a decision rather than a
    /// button: thirty-two units of energy, and a commander who fires it on empty banks gets one
    /// iteration of it.
    /// </remarks>
    public bool FireEcm()
    {
        if (Commander is not { Ecm: true } || EcmFrames > 0)
        {
            return false;
        }

        // "LDA ECMA / BNE MA64": an E.C.M. that is already going off blocks another, whether it is
        // ours or another ship's
        EcmFrames = EcmDuration;
        EcmActive = true;
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
            // The E.C.M. drains the energy banks a unit an iteration, and gives up when they are
            // empty: the drain comes before the countdown in the original, so the last iteration of
            // an E.C.M. fired on a nearly empty tank is the one that empties it.
            if (Player.Energy > 0)
            {
                Player.Energy--;
            }

            EcmFrames--;

            if (EcmFrames == 0 || Player.Energy == 0)
            {
                EcmFrames = 0;
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

            // The E.C.M. destroys every missile in the local bubble, which is the original's own
            // wording: "it will destroy any missiles which are currently in the local bubble". There
            // is no range on it, and the port's 20,000-unit limit was an invention of its own.
            if (EcmActive)
            {
                (int ex, int ey, int ez) = missile.GetPosition();
                missile.IsKilled = true;

                // And if it went off right beside us it hurts: "the missile just got destroyed
                // near us, so call OOPS to damage the ship by 80, which is nowhere near as bad as
                // the 250 damage from a missile slamming straight into us". The test is the
                // original's own, and it is almost always false — see Missiles.IsBesideUs.
                if (Missiles.IsBesideUs(ex, ey, ez) &&
                    Combat.TakeDamage(Player, Missiles.NearbyDamage, fromBehind: ez < 0))
                {
                    PlayerDied = true;
                }

                continue;
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
                        hit.StartExplosion();
                        ReportKill(hit);
                        ReportDrops(hit, Debris.DestructionDrops(hit.Type, 0, Random));
                    }
                }
                else
                {
                    // The missile has gone off on us: record the hit, and note whether it was fatal.
                    // Which shield it takes comes from the missile's own z_sign, as the original's
                    // OOPS does — "fetch byte #8 (z_sign) for the ship attacking us... if A is
                    // negative, then we got hit in the rear". Passing false meant every missile took
                    // the forward shield however it arrived.
                    (_, _, int missileZ) = missile.GetPosition();
                    HitByMissile = true;
                    if (Combat.TakeDamage(Player, Missiles.DirectHitDamage, fromBehind: missileZ < 0))
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
        if (Commander is null)
        {
            return;
        }

        foreach (Ship ship in _bubble)
        {
            // The same gate as the collisions: the exploding-or-killed bits are ORed into the
            // distance test in the original, so a canister that is blowing up is past scooping
            if (!Debris.IsScoopable(ship.Type) || ship.IsKilled || ship.IsExploding)
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

            (int Item, int Amount)? scooped = Debris.TryScoop(ship, Commander, ScoopItemProvider(ship));
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

        // The rule lives in Missions, where it can be read and tested in one place. It used to be
        // written out again here, with the tests exercising only the copy that the game never
        // called — two statements of one rule, either of which could have been changed alone.
        if (Missions.ShouldSpawnConstrictor(System.Value, GalaxyNumber, constrictors))
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

        // Nothing spawns inside the station's safe zone. The original funnels every spawn path
        // through MTT1, which jumps to the end of the main loop while SSPR is set, so a station in
        // the bubble means no traders, no pirates and no police until we have left it behind.
        if (StationIsPresent)
        {
            return;
        }

        // "We only get here once every 256 iterations of the main loop": the original decrements its
        // main loop counter and jumps out of the whole spawning section unless it has reached zero,
        // so this is not a per-frame decision at all. Making it one — which is what the port did,
        // gated only by the extra-vessels counter — put several times the original's traffic in the
        // sky, because a roll that said "nothing spawns" was simply retried on the next iteration
        // instead of waiting another 256 of them.
        if ((MainLoopCounter & (Spawner.MainLoopDecisionPeriod - 1)) != 0)
        {
            return;
        }

        // EV, the extra-vessels counter, counts decision boundaries rather than iterations: "DEC EV
        // and if it is still positive, jump to MLOOPS to stop spawning; INC EV, so EV is negative,
        // so bump it up again"
        _spawnDelay--;
        if (_spawnDelay >= 0)
        {
            return;
        }

        _spawnDelay++;

        // A mission ship comes before the ordinary traffic
        if (SpawnMissionShip())
        {
            return;
        }

        // The original's first roll after the main loop counter comes round decides between the
        // quiet half of the game and the dangerous half. On 13% of decisions it goes down the
        // "trader or junk" branch, and that branch is split evenly: half a trader, half a rock. The
        // other 87% is the pirates and bounty hunters below.
        int junk = 0;
        foreach (Ship existing in _bubble)
        {
            if (Debris.IsJunk(existing.Type))
            {
                junk++;
            }
        }

        if (Debris.WantsJunk(Random, junk))
        {
            // "Set A, X and V flag to random numbers ... If V flag is set (50% chance), jump up to
            // MTT4 to spawn a trader"
            if (Random.Next() >= TraderIn256)
            {
                Spawn(Spawner.Create(SpawnKind.Trader, System.Value, Random));
                LastSpawn = SpawnKind.Trader;
                LastJunkSpawned = 0;
                return;
            }

            int junkType = Debris.ChooseJunkType(Random);
            Spawn(Debris.CreateJunk(junkType, Random));
            LastSpawn = SpawnKind.None;
            LastJunkSpawned = junkType;
            return;
        }

        LastJunkSpawned = 0;

        // Before it considers pirates, the original considers the police: it works out how bad we
        // look — contraband in the hold, or our legal status if there are already police about — and
        // rolls against that. A pack of police is sent if the roll comes off, and then "if we now
        // have at least one cop in the local bubble, stop spawning", so a police pack takes the place
        // of the pirates rather than joining them.
        int cops = _bubble.Count(s => s.Type == Spawner.CopType);
        int badness = Spawner.Badness(Commander, cops);
        if (badness > 0 && Random.Next() < badness)
        {
            int officers = 1 + (Random.Next() % 4);
            for (int i = 0; i < officers; i++)
            {
                if (!Spawn(Spawner.Create(SpawnKind.Cops, System.Value, Random, i)))
                {
                    break;
                }
            }

            LastSpawn = SpawnKind.Cops;
            _spawnDelay = officers;
            return;
        }

        SpawnKind kind = Spawner.ChooseSpawn(System.Value, Random);
        if (kind == SpawnKind.None)
        {
            return;
        }

        // A pack of pirates arrives together, up to the original's four in a group. The pack size is
        // what the original stores in EV, so a bigger pack keeps the sky quiet for longer.
        int count = kind == SpawnKind.Pirates ? 1 + (Random.Next() % 4) : 1;
        for (int i = 0; i < count; i++)
        {
            if (!Spawn(Spawner.Create(kind, System.Value, Random, i)))
            {
                break; // the bubble is full
            }
        }

        LastSpawn = kind;
        _spawnDelay = kind == SpawnKind.Pirates ? count : 0;
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
        ScoopedThisFrame = null;

        LaserType laser = Commander?.GetLaser(ActiveMount) ?? LaserType.Pulse;
        int power = Combat.Power(laser);

        // The pulse counter counts the original's fifty-hertz ticks, which do not divide evenly into
        // our iterations, so what is left over is carried into the next interval rather than thrown
        // away: a ten-tick laser is one shot every two and a half iterations, and rounding that to
        // three or four would make it fire at four fifths of the original's rate.
        if (input.Fire && power > 0 && LaserCooldown <= 0 && LaserTemperature < Combat.OverheatTemperature)
        {
            FiringLaserPower = power;
            LaserTemperature = Math.Min(255, LaserTemperature + Combat.HeatPerShot);
            LaserCooldown += Combat.FireInterval(laser);



            // Deplete our energy, as firing does in the original: LASLI ends with a call to DENGY,
            // which takes a unit off the energy banks
            if (Player.Energy > 0)
            {
                Player.Energy -= Combat.EnergyPerShot;
            }

            // Find the first ship in the crosshairs, which is the one we hit. The test is on the
            // flipped position, because the original flips each ship to the current view before it
            // calls HITCH: firing the rear laser hits what is behind us, and the crosshairs mean the
            // middle of the window we are looking through.
            foreach (Ship ship in _bubble)
            {
                if (Combat.IsInCrosshairs(ship, TargetableArea(ship), View))
                {
                    LaserTarget = ship;

                    // The damage this shot does, which is the laser's power except against the
                    // Constrictor. On the disc only a military laser can harm the super-ship, and
                    // then only for a quarter of the damage: "only military lasers can harm the
                    // Constrictor in mission 1, and then they only inflict a quarter of the damage
                    // that military lasers inflict on normal ships". The test is on the laser and
                    // not on the target's shields, so a pulse or beam laser does nothing to it.
                    int damage = Combat.DamageAgainst(laser, power, ship);

                    // A ship that survives being shot at is made angry, which switches its AI on
                    // and, if it was an innocent, turns the station against us
                    if (!Combat.ApplyHit(ship, damage))
                    {
                        MakeAngry(ship);
                    }
                    else
                    {
                        // The hit destroyed it, so start its explosion and report the kill
                        ship.StartExplosion();
                        ReportKill(ship);

                        // Rocks and ships leave something behind when they are destroyed
                        ReportDrops(ship, Debris.DestructionDrops(ship.Type, power, Random));
                    }

                    break;
                }
            }
        }

        if (LaserCooldown > 0)
        {
            // The pulse counter counts the original's fifty-hertz ticks, not iterations, so it is
            // spent four at a time: a pulse laser fires five times a second whatever the flight
            // loop is doing, which is what the original's LINSCN gives it. It is allowed to go
            // negative — the overshoot is what carries the leftover ticks into the next interval,
            // and clamping it here rounds every gap up to a whole iteration.
            LaserCooldown -= Combat.LaserTicksPerIteration;
        }

        // The laser cools by one degree a frame, as the original's main game loop does
        if (LaserTemperature > 0)
        {
            LaserTemperature -= Combat.CoolingPerFrame;
        }
    }

    /// <summary>The targetable area of a ship, which the hit test uses.</summary>
    /// <summary>
    /// The area a hit test uses for a ship, defaulting to its blueprint value rather than to a
    /// constant. The original takes it from the blueprint, so a core-only session should too — a
    /// constant here would make every ship equally easy to hit and quietly ignore the field.
    /// </summary>
    public Func<Ship, int> TargetableAreaProvider { get; set; } =
        ship => BlueprintDefaults.For(ship.Type).TargetableArea;

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

        // Part 4: apply the ship's acceleration to its speed, cap it at the ship's own maximum and
        // clear the acceleration, which is a one-off change
        ShipMovement.ApplyAcceleration(ship);

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
            // The angles MVS4 rotates by are the magnitudes *with their sign bits*: the original
            // builds them with ORA ALP2 and ORA BET2, "so ALPHA has a different sign to the actual
            // roll rate". Handing MVS4 the bare magnitudes loses the direction, and a rotation
            // without a direction is not a rotation — every ship in the sky was counter-rolled the
            // same way whichever way we rolled, which is why matching the station's roll to dock was
            // impossible: rolling with it and rolling against it looked the same.
            byte alpha = (byte)(RollAngle | (RollSign & 0x80));
            byte beta = (byte)(PitchAngleValue | (PitchSign & 0x80));

            ShipMath.Mvs4(ship.Orientation, Orientation.Nosev, alpha, beta);
            ShipMath.Mvs4(ship.Orientation, Orientation.Roofv, alpha, beta);
            ShipMath.Mvs4(ship.Orientation, Orientation.Sidev, alpha, beta);

            // Part 8: rotate the ship about its own axes by its pitch and roll counters, which is
            // how ships turn under their own power (and how the AI steers).
            //
            // The station needs nothing special here. NWSPS gives it a roll counter of 255, whose
            // low seven bits are all set, and MVEIT does not damp a counter like that — so it turns
            // for as long as it exists at one MVS5 step an iteration. Renewing the counter every
            // frame, as this used to, was both unnecessary and wrong: the original's roll is
            // anti-clockwise and this made it clockwise.
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

    /// <summary>The Coriolis space station.</summary>
    public const int Coriolis = 2;

    /// <summary>The escape pod.</summary>
    public const int EscapePod = 3;
}
