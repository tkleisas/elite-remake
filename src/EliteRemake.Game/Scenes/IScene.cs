using EliteRemake.Core.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EliteRemake.Game.Scenes;

/// <summary>A scene that can be updated and drawn by the game shell.</summary>
public interface IScene
{
    /// <summary>The camera used for the 3D view.</summary>
    ViewCamera Camera { get; }

    /// <summary>Advances the scene.</summary>
    void Update(float elapsedSeconds);

    /// <summary>Draws the scene.</summary>
    void Draw(SpriteBatch spriteBatch, Texture2D pixel, GraphicsDevice device);

    /// <summary>A one-line description of the scene, printed to the console.</summary>
    string StatusLine { get; }
}
