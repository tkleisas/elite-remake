using EliteRemake.Core.Graphics;
using EliteRemake.Core.Sim;
using EliteRemake.Game.Rendering;
using EliteRemake.Game.Scenes;
using Microsoft.Xna.Framework.Graphics;

namespace EliteRemake.Game;

/// <summary>Builds the scene the game should start in.</summary>
public static class SceneFactory
{
    /// <summary>
    /// Builds the development ship viewer, which shows one blueprint turning on the spot so the
    /// extracted geometry can be checked against the original's own models.
    /// </summary>
    /// <summary>
    /// Builds the start screen, with our own ship turning in front of the name.
    /// </summary>
    public static IScene CreateTitle(
        GameSession session,
        GraphicsDevice device,
        ViewCamera camera,
        TextRenderer text,
        Settings settings,
        Audio.SoundBank? sound,
        bool showSettings = false) =>
        new TitleScene(camera, device, session, text, settings, sound, PlayerShipMesh())
        {
            ShowSettings = showSettings,
        };

    /// <summary>The Cobra Mk III, which is the ship the player flies and the one on the title screen.</summary>
    private static EliteRemake.Core.Ships.ShipMesh PlayerShipMesh()
    {
        ShipCatalog.Entry entry = ShipCatalog.Find("cobra-mk-3");
        return entry.Mesh;
    }

    public static IScene CreateViewer(GameOptions options, GraphicsDevice device, ViewCamera camera)
    {
        ShipCatalog.Entry requested = ShipCatalog.Find(options.ViewerShip!);
        return new ShipViewerScene(
            requested.Mesh,
            requested.Name,
            device,
            camera,
            distance: options.ViewerDistance ?? ShipCatalog.ViewerDistance(requested.Mesh),
            fixedHeading: options.ViewerHeading,
            fixedPitch: options.ViewerPitch);
    }

    /// <summary>Creates the game session: the commander and their flight simulation.</summary>
    public static GameSession CreateSession(GameOptions options)
    {
        var player = new Ship(11, "cobra-mk-3", "Cobra Mk III")
        {
            MaxEnergy = 150,
        };

        player.Energy = 150;

        var commander = Commander.CreateDefault();
        commander.SetLaser(LaserMount.Front, options.FrontLaser);

        if (options.FullyEquipped)
        {
            commander.Ecm = true;
            commander.FuelScoops = true;
            commander.EnergyBomb = true;
            commander.DockingComputer = true;
            commander.GalacticHyperdrive = true;
            commander.EscapePod = true;
            commander.SetLaser(LaserMount.Rear, LaserType.Pulse);
            commander.Missiles = 4;
        }

        var session = new GameSession(commander, new FlightSim(player));

        // Start from the saved commander if there is one, as the original does
        if (!options.NewCommander)
        {
            string? failure = session.TryLoad();
            if (failure is null)
            {
                Console.WriteLine($"Loaded commander {session.Commander.Name} at {session.System.Name}");
            }
        }

        // The disc version opens by asking whether to load a commander and then starts; the start
        // screen is where that question lives in this port, so unless a development option has asked
        // for a particular screen the game begins there
        if (options.ShowTitle)
        {
            session.Mode = GameMode.Title;
        }

        if (options.StartDocked)
        {
            session.Dock();
            session.Screen = options.StartScreen;
        }

        // --launch: the real path out of the station, so the flight scene sees a launch and draws
        // the tunnel the original's LAUN draws
        if (options.StartByLaunching)
        {
            session.Dock();
            session.Launch();
        }

        // --buy: the equipment shop's own rules, from the command line. A laser needs a view, which
        // is the original's "View ?" prompt answered in advance.
        if (options.BuyItem is { } purchase)
        {
            string[] parts = purchase.Split(',', 2);
            int item = int.Parse(parts[0]);
            LaserMount? mount = parts.Length > 1
                ? parts[1].ToLowerInvariant() switch
                {
                    "rear" => LaserMount.Rear,
                    "left" => LaserMount.Left,
                    "right" => LaserMount.Right,
                    _ => LaserMount.Front,
                }
                : null;

            if (mount is null && EliteRemake.Core.Universe.Outfitting.IsLaserItem(item))
            {
                // Leave the question open for the equipment screen to show
                session.Screen = DockedScreen.Equipment;
                options.BuyLaserItem = item;
            }
            else
            {
                string? result = session.BuyEquipment(item, 0, mount);
                Console.WriteLine($"Buy {purchase}: {result ?? "no effect"} " +
                                  $"({EliteRemake.Core.Universe.Outfitting.Format(session.Commander.Cash)} credits)");
            }
        }

        return session;
    }

    /// <summary>
    /// Sets up a flight scene: our Cobra, a space station ahead of us and a few other ships to fly
    /// around, which is enough to exercise the flight model and the renderer.
    /// </summary>
    /// <param name="settings">
    /// The player's bindings. The caller owns them so that the settings screen and the flight scene
    /// share one set: loading a second copy here means a rebind changes the screen's copy and leaves
    /// the flight scene reading the old one, which looks exactly like rebinding not working.
    /// </param>
    public static FlightScene CreateFlightScene(
        GameOptions options,
        GraphicsDevice device,
        ViewCamera camera,
        ScreenLayout layout,
        TextRenderer text,
        GameSession session,
        Settings settings)
    {
        FlightSim sim = session.Flight;
        var scene = new FlightScene(device, camera, sim, new HudRenderer(layout, text))
        {
            Session = session,
            Settings = settings,
        };

        if (options.SimRate is { } rate && rate > 0)
        {
            scene.Rate = rate;
        }

        if (options.Autopilot)
        {
            scene.DockingComputerEngaged = true;
        }

        foreach (ShipCatalog.Entry entry in ShipCatalog.All)
        {
            scene.RegisterMesh(entry.Id, entry.Mesh);
        }

        // Ships fire with the laser power from their blueprint, and their aggression is the
        // original's AI flag
        sim.LaserPowerProvider = ship =>
            ShipCatalog.ByType(ship.Type) is { } lasered &&
            EliteRemake.Data.Ships.ShipData.ById(lasered.Id) is { } laserBlueprint
                ? laserBlueprint.Header.LaserPower
                : 0;

        // The original halves blueprint byte #19 to get the damage an enemy inflicts on us
        sim.DamageProvider = ship =>
            ShipCatalog.ByType(ship.Type) is { } attacker &&
            EliteRemake.Data.Ships.ShipData.ById(attacker.Id) is { } attackerBlueprint
                ? Math.Max(0, attackerBlueprint.Header.LaserMissileByte / 2)
                : 0;

        sim.TargetableAreaProvider = ship =>
            ShipCatalog.ByType(ship.Type) is { } entry &&
            EliteRemake.Data.Ships.ShipData.ById(entry.Id) is { } blueprint
                ? blueprint.Header.TargetableArea
                : 55 * 55;

        sim.System = session.System;
        sim.Missions = session.Missions;
        sim.GalaxyNumber = session.Commander.GalaxyNumber;
        // The galaxy's own seeds, which is what resolves the Constrictor's system: the commander's
        // current system has its own seeds and passing those through resolves to a different
        // system number in a shorter galaxy. It happens to land on Orarra either way, which is
        // exactly the sort of accident worth removing.
        sim.GalaxySeeds = EliteRemake.Core.Universe.Galaxy.GalaxySeeds(session.Commander.GalaxyNumber);

        // What scooping a ship yields. The blueprint's high nibble plus one is the market item, and
        // the original uses that number directly to index its zero-based QQ20 hold slots — so
        // "market item 1" is hold slot 1, not slot 0. Checking the three scoopable ships against the
        // source's own comments fixes the convention: the escape pod's nibble 2 gives 3 and the
        // comment says slaves, which is item 3; the thargon's nibble 15 gives 16 and the comment says
        // alien items, which is item 16.
        sim.ScoopItemProvider = ship =>
            ShipCatalog.ByType(ship.Type) is { } scooped &&
            EliteRemake.Data.Ships.ShipData.ById(scooped.Id) is { } scoopBlueprint &&
            scoopBlueprint.Header.ScoopMarketItem > 0
                ? scoopBlueprint.Header.ScoopMarketItem
                : -1;

        // Give any ship that spawns a name and a blueprint from the catalogue, so it can be drawn
        sim.ShipSpawned = ship =>
        {
            if (ShipCatalog.ByType(ship.Type) is { } spawned)
            {
                ship.BlueprintId = spawned.Id;
                ship.Name = spawned.Name;
                ApplyBlueprint(ship);
            }
        };
        // No station unless the command line asked for one: arriving in a system leaves the planet
        // and the sun in the sky, and the station appears when we reach the planet's orbit.
        //
        // An empty bubble is what says we have not arrived anywhere yet. Starting by launching has
        // already built the sky TT110 builds — the planet dead ahead and the station just behind us
        // — and arriving over the top of it threw that away and left us looking at an arrival sky
        // with no station in it.
        if (session.Flight.Bubble.Count == 0)
        {
            scene.ArriveInSystem(session.System, options.StationDistance);
        }

        scene.FollowPlanet = options.FlyToPlanet;

        // --view: start looking through one of the other three windows
        scene.SetView(options.StartView switch
        {
            "rear" => EliteRemake.Core.Sim.SpaceView.Rear,
            "left" => EliteRemake.Core.Sim.SpaceView.Left,
            "right" => EliteRemake.Core.Sim.SpaceView.Right,
            _ => EliteRemake.Core.Sim.SpaceView.Front,
        });
        session.BountyProvider = ship =>
            ShipCatalog.ByType(ship.Type) is { } bountyShip &&
            EliteRemake.Data.Ships.ShipData.ById(bountyShip.Id) is { } bountyBlueprint
                ? bountyBlueprint.Header.Bounty * 10
                : 0;


        if (!options.EmptySystem)
        {
            // A couple of ships to fly around, placed ahead and to one side. They are given an AI
            // flag: without one they fly straight for ever, which is what the original's own
            // spawner avoids by setting "ORA #%11000000" on every ship it makes. A Sidewinder with
            // AI is hostile, so the development flight has something that will come at us.
            Ship sidewinder = ApplyBlueprint(Ship.Create(17, "sidewinder", "Sidewinder", 0.4f, 0.1f, 1200, -300, 4200));
            sidewinder.AiFlag = EliteRemake.Core.Sim.Spawner.AiFlag(sim.Random);
            sim.Spawn(sidewinder);

            Ship python = ApplyBlueprint(Ship.Create(12, "python", "Python", -0.3f, -0.05f, -2500, 400, 7000));
            python.AiFlag = EliteRemake.Core.Sim.Spawner.AiFlag(sim.Random);
            sim.Spawn(python);
        }

        // --stress fills the bubble with ships, so the frame cost can be measured at its worst
        // rather than on a quiet system
        if (options.StressShips > 0)
        {
            var random = new EliteRemake.Core.Sim.EliteRandom(1);
            for (int i = 0; i < options.StressShips; i++)
            {
                int type = 16 + (i % 8);
                if (ShipCatalog.ByType(type) is { } entry)
                {
                    var ship = EliteRemake.Core.Sim.Ship.Create(
                        type, entry.Id, entry.Name,
                        (i - (options.StressShips / 2f)) * 0.05f,
                        ((i % 5) - 2) * 0.04f,
                        -1200 + ((i % 7) * 400),
                        ((i % 5) - 2) * 300,
                        2000 + ((i % 9) * 900));

                    sim.Spawn(ApplyBlueprint(ship));
                }
            }
        }

        // --jump begins a jump at once, so the hyperspace tunnel can be seen without flying to the
        // charts and picking a destination first
        if (options.StartHyperspace)
        {
            scene.ShowTunnelFrames = 40;
        }

        if (options.StartHyperspace)
        {
            session.SelectedSystem = session.System.Seeds == session.Commander.CurrentSystem.Seeds
                ? EliteRemake.Core.Universe.Galaxy.NearestReachable(
                    session.System, session.SystemsInGalaxy, session.Commander.Fuel)
                : session.SelectedSystem;

            if (!session.StartHyperspace())
            {
                // Nothing was in range, so jump to nowhere in particular to show the effect anyway
                session.ForceHyperspaceForDisplay();
            }
        }

        return scene;
    }

    /// <summary>Copies a blueprint's stats onto a spawned ship, as the original's NWSHP does.</summary>
    public static Ship ApplyBlueprint(Ship ship)
    {
        if (ShipCatalog.ByType(ship.Type) is { } entry)
        {
            EliteRemake.Data.Ships.ShipBlueprint blueprint = EliteRemake.Data.Ships.ShipData.ById(entry.Id)!;
            ship.MaxEnergy = (byte)Math.Min(blueprint.Header.MaxEnergy, 255);
            ship.Energy = ship.MaxEnergy;
            ship.VisibilityDistance = blueprint.Header.VisibilityDistance;
            ship.MaxSpeed = blueprint.Header.MaxSpeed;

            // The default NEWB flags for this ship type, out of the disc's E% table. The flight loop
            // tests them to decide how much killing this ship raises our legal status, so they have
            // to be stamped on as the ship arrives rather than derived from its AI.
            //
            // NWSHP *or's* the table into the flags the ship already carries, having first cleared
            // bits 4 and 7 — those two describe the ship's own state (it is docking, or it has been
            // scooped) and are never taken from the table. Assigning instead of or-ing throws away
            // the hostility a spawner set, so a ship sent out to pick a fight arrives peaceful.
            byte defaults = EliteRemake.Data.Ships.ShipData.NewbFlagsFor(ship.Type);
            ship.NewbFlags |= (byte)(defaults & 0b0110_1111);
            if (ship.Speed == 0)
            {
                ship.Speed = (byte)Math.Min(blueprint.Header.MaxSpeed, 255);
            }
        }

        return ship;
    }
}
