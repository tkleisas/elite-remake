using EliteRemake.Core.Graphics;
using EliteRemake.Core.Sim;
using EliteRemake.Game.Rendering;
using EliteRemake.Game.Scenes;
using Microsoft.Xna.Framework.Graphics;

namespace EliteRemake.Game;

/// <summary>Builds the scene the game should start in.</summary>
public static class SceneFactory
{
    public static IScene Create(GameOptions options, GraphicsDevice device, ViewCamera camera, ScreenLayout layout, TextRenderer text)
    {
        if (options.ViewerShip is { } shipName)
        {
            ShipCatalog.Entry requested = ShipCatalog.Find(shipName);
            return new ShipViewerScene(
                requested.Mesh,
                requested.Name,
                device,
                camera,
                distance: options.ViewerDistance ?? ShipCatalog.ViewerDistance(requested.Mesh),
                fixedHeading: options.ViewerHeading,
                fixedPitch: options.ViewerPitch);
        }

        return CreateFlightScene(options, device, camera, layout, text, CreateSession(options));
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

        if (options.StartDocked)
        {
            session.Dock();
            session.Screen = options.StartScreen;
        }

        return session;
    }

    /// <summary>
    /// Sets up a flight scene: our Cobra, a space station ahead of us and a few other ships to fly
    /// around, which is enough to exercise the flight model and the renderer.
    /// </summary>
    public static FlightScene CreateFlightScene(
        GameOptions options,
        GraphicsDevice device,
        ViewCamera camera,
        ScreenLayout layout,
        TextRenderer text,
        GameSession session)
    {
        FlightSim sim = session.Flight;
        var scene = new FlightScene(device, camera, sim, new HudRenderer(layout, text))
        {
            Session = session,
        };

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
        sim.ScoopCommander = session.Commander;

        // What a canister holds comes from its blueprint's scoop market item
        sim.ScoopItemProvider = ship =>
            ShipCatalog.ByType(ship.Type) is { } canister &&
            EliteRemake.Data.Ships.ShipData.ById(canister.Id) is { } canisterBlueprint
                ? canisterBlueprint.Header.ScoopMarketItem
                : 0;

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
        scene.ArriveInSystem(session.System);
        session.BountyProvider = ship =>
            ShipCatalog.ByType(ship.Type) is { } bountyShip &&
            EliteRemake.Data.Ships.ShipData.ById(bountyShip.Id) is { } bountyBlueprint
                ? bountyBlueprint.Header.Bounty * 10
                : 0;

        scene.SpawnStationAhead(options.StationDistance);

        if (!options.EmptySystem)
        {
            // A couple of ships to fly around, placed ahead and to one side
            sim.Spawn(ApplyBlueprint(Ship.Create(17, "sidewinder", "Sidewinder", 0.4f, 0.1f, 1200, -300, 4200)));
            sim.Spawn(ApplyBlueprint(Ship.Create(12, "python", "Python", -0.3f, -0.05f, -2500, 400, 7000)));
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
            if (ship.Speed == 0)
            {
                ship.Speed = (byte)Math.Min(blueprint.Header.MaxSpeed, 255);
            }
        }

        return ship;
    }
}
