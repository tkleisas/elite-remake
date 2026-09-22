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

        return CreateFlightScene(options, device, camera, layout, text);
    }

    /// <summary>
    /// Sets up a flight scene: our Cobra, a space station ahead of us and a few other ships to fly
    /// around, which is enough to exercise the flight model and the renderer.
    /// </summary>
    private static IScene CreateFlightScene(GameOptions options, GraphicsDevice device, ViewCamera camera, ScreenLayout layout, TextRenderer text)
    {
        var player = new Ship(11, "cobra-mk-3", "Cobra Mk III")
        {
            MaxEnergy = 150,
        };

        player.Energy = 150;

        var sim = new FlightSim(player);
        var scene = new FlightScene(device, camera, sim, new HudRenderer(layout, text));

        foreach (ShipCatalog.Entry entry in ShipCatalog.All)
        {
            scene.RegisterMesh(entry.Id, entry.Mesh);
        }

        // Start in Lave, the system the default commander is docked at
        var lave = EliteRemake.Core.Universe.Galaxy.GenerateGalaxy(0)
            .First(s => s.Name == "LAVE");
        scene.ArriveInSystem(lave);
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
