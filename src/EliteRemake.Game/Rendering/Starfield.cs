using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EliteRemake.Game.Rendering;

/// <summary>
/// The stardust of the space view: a pool of particles around us that streams past as we fly and
/// swings round as we turn.
/// </summary>
/// <remarks>
/// <para>
/// The original keeps a pool of dust particles in the K% workspace, moves them towards us every
/// iteration and applies our current pitch and roll to each one — "so the stardust moves correctly
/// when we steer our ship" — recycling any that rushes past into a new particle ahead. This is the
/// same idea in floating point, because the dust has no effect on the simulation.
/// </para>
/// <para>
/// <b>The rotation is not decoration.</b> It was missing: the dust only moved in z, so rolling the
/// ship swung the ships and the planets round the view while the dust hung still, which is the one
/// thing the eye uses to tell that it is turning. The angles are the simulation's own, put through
/// the same arithmetic it applies to a ship's location, so the dust and the ships turn together.
/// </para>
/// <para>
/// The count is a presentation choice: the original shows eighteen particles in normal space and
/// three in witchspace, where ours shows many more. The density is what a modern widescreen view
/// wants, and it is recorded as a departure rather than passed off as a port.
/// </para>
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
    /// <param name="distanceTravelled">How far we have moved this frame.</param>
    /// <param name="rollAngle">Our roll angle, as the original's ALPHA: a signed value.</param>
    /// <param name="pitchAngle">Our pitch angle, as the original's BETA: a signed value.</param>
    public void Update(float distanceTravelled, int rollAngle = 0, int pitchAngle = 0)
    {
        for (int i = 0; i < StarCount; i++)
        {
            _stars[i].Z -= distanceTravelled;
            if (_stars[i].Z < 1f)
            {
                _stars[i] = NewStar(anyDepth: false);
            }
        }

        if (rollAngle == 0 && pitchAngle == 0)
        {
            return;
        }

        for (int i = 0; i < StarCount; i++)
        {
            // Exactly the arithmetic the simulation applies to a ship's location, so the dust and the
            // ships turn together. It is the original's own sequence, and the order of it matters:
            // each step works from the value the step before it left.
            float x = _stars[i].X;
            float y = _stars[i].Y;
            float z = _stars[i].Z;

            float k2 = y - ((x * rollAngle) / 256f);
            z += (pitchAngle * k2) / 256f;
            y = k2 - ((pitchAngle * z) / 256f);
            x += (rollAngle * y) / 256f;

            _stars[i].X = x;
            _stars[i].Y = y;
            _stars[i].Z = z;
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
