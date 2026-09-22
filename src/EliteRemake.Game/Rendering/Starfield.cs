using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EliteRemake.Game.Rendering;

/// <summary>
/// The starfield of the space view: a fixed set of stars in the local bubble that drift past us as
/// we fly, reproducing the original's twinkling backdrop.
/// </summary>
/// <remarks>
/// The original keeps a pool of stars in the K% workspace and moves them towards us every frame,
/// respawning any that pass behind us. The remake does the same, in floating point, because the
/// starfield has no effect on the simulation.
/// </remarks>
public sealed class Starfield
{
    private const int StarCount = 160;
    private const float SpawnDepth = 4000f;

    private readonly Star[] _stars = new Star[StarCount];
    private readonly Random _random;

    public Starfield(int seed = 0x5A4A)
    {
        _random = new Random(seed);
        for (int i = 0; i < StarCount; i++)
        {
            _stars[i] = NewStar(anyDepth: true);
        }
    }

    /// <summary>Moves the stars towards us by the distance travelled this frame.</summary>
    public void Update(float distanceTravelled)
    {
        for (int i = 0; i < StarCount; i++)
        {
            _stars[i].Z -= distanceTravelled;
            if (_stars[i].Z < 1f)
            {
                _stars[i] = NewStar(anyDepth: false);
            }
        }
    }

    /// <summary>Draws the stars, projecting each one and shading it by distance.</summary>
    public void Draw(SpriteBatch spriteBatch, Texture2D pixel, Core.Graphics.ViewCamera camera)
    {
        foreach (Star star in _stars)
        {
            var position = new System.Numerics.Vector3(star.X, star.Y, star.Z);
            if (!camera.Project(position, out var screen))
            {
                continue;
            }

            // Stars fade in from the gloom as they get closer, as the original's do when they
            // reach a certain distance
            float brightness = MathHelper.Clamp(1f - (star.Z / SpawnDepth), 0.15f, 0.85f);
            var colour = new Color(brightness, brightness, brightness);
            int size = star.Z < 600 ? 2 : 1;
            spriteBatch.Draw(pixel, new Rectangle((int)screen.X, (int)screen.Y, size, size), colour);
        }
    }

    private Star NewStar(bool anyDepth) => new(
        (float)((_random.NextDouble() * 2) - 1) * SpawnDepth * 0.5f,
        (float)((_random.NextDouble() * 2) - 1) * SpawnDepth * 0.5f,
        anyDepth ? (float)(_random.NextDouble() * SpawnDepth) + 1f : SpawnDepth);

    private struct Star(float x, float y, float z)
    {
        public float X = x;
        public float Y = y;
        public float Z = z;
    }
}
