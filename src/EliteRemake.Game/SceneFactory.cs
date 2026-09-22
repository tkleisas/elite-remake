using EliteRemake.Core.Graphics;
using EliteRemake.Game.Scenes;
using Microsoft.Xna.Framework.Graphics;

namespace EliteRemake.Game;

/// <summary>Builds the scene the game should start in.</summary>
public static class SceneFactory
{
    public static IScene Create(GameOptions options, GraphicsDevice device, ViewCamera camera)
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

        // The flight scene arrives in the next milestone; until then the viewer shows a ship so the
        // extracted geometry can be inspected
        ShipCatalog.Entry entry = ShipCatalog.First();
        return new ShipViewerScene(entry.Mesh, entry.Name, device, camera, distance: ShipCatalog.ViewerDistance(entry.Mesh));
    }
}
