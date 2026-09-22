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
            (string name, Core.Ships.ShipMesh mesh) = ShipCatalog.Find(shipName);
            return new ShipViewerScene(mesh, name, device, camera, distance: ShipCatalog.ViewerDistance(mesh));
        }

        // The flight scene arrives in the next milestone; until then the viewer shows the whole
        // catalogue so the extracted geometry can be checked
        (string firstName, Core.Ships.ShipMesh firstMesh) = ShipCatalog.First();
        return new ShipViewerScene(firstMesh, firstName, device, camera, distance: ShipCatalog.ViewerDistance(firstMesh));
    }
}
