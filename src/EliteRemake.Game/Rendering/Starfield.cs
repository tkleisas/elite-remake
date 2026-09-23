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

    /// <summary>
    /// The view the field is drawn for, so that the dust streams the right way through the window we
    /// are looking through: towards us in the front view, away behind us, and sideways past the side
    /// windows.
    /// </summary>
    public EliteRemake.Core.Sim.SpaceView View { get; set; } = EliteRemake.Core.Sim.SpaceView.Front;

    public Starfield(int seed = 0x5A4A)
    {
        _random = new Random(seed);
        Reset();
    }

    /// <summary>
    /// Throws the whole field away and makes a new one, which is the original's NWSTARS: LOOK1 reaches
    /// it every time the space view is set up, and an in-system jump goes through LOOK1 with the view
    /// type forced non-zero so that the screen is cleared and the dust is new.
    /// </summary>
    public void Reset()
    {
        for (int i = 0; i < StarCount; i++)
        {
            _stars[i] = NewStar(anyDepth: true);
        }
    }

    /// <summary>
    /// Reflects the field in the screen diagonal, which is the original's FLIP: LOOK1 calls it every
    /// time we change view, and it is "a quick way of making the stardust field in the new view feel
    /// different without having to generate a whole new field" — look carefully as you change view
    /// and you can see that the new field is the old one reflected in the line from bottom left to
    /// top right.
    /// </summary>
    public void Flip()
    {
        for (int i = 0; i < StarCount; i++)
        {
            (_stars[i].X, _stars[i].Y) = (_stars[i].Y, _stars[i].X);
        }
    }

    /// <summary>Moves the stars towards us by the distance travelled this frame.</summary>
    /// <param name="distanceTravelled">How far we have moved this frame.</param>
    /// <param name="rollAngle">Our roll angle, as the original's ALPHA: a signed value.</param>
    /// <param name="pitchAngle">Our pitch angle, as the original's BETA: a signed value.</param>
    public void Update(float distanceTravelled, int rollAngle = 0, int pitchAngle = 0)
    {
        // The dust is standing still and we are the ones moving, so it streams along the reverse of
        // our own velocity seen through this window: towards us in the front view and away from us
        // in the rear, sideways past the side windows.
        (float sx, float sy, float sz) = EliteRemake.Core.Sim.Plut.StreamDirection(View);
        float travel = distanceTravelled;

        for (int i = 0; i < StarCount; i++)
        {
            _stars[i].X += sx * travel;
            _stars[i].Y += sy * travel;
            _stars[i].Z += sz * travel;

            // A particle that has reached us comes back as a new one at the far end. How far away it
            // is is measured against the stream, so the test and the new position move with the view:
            // the dust arrives from +z in the front view and from -z in the rear, where it is the
            // rear window we are watching it through.
            float depth = -((_stars[i].X * sx) + (_stars[i].Y * sy) + (_stars[i].Z * sz));
            if (depth < 1f)
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
            // reach a certain distance. "Closer" is measured along the streaming axis, which is x
            // rather than z in the side views.
            (float sx, float sy, float sz) = EliteRemake.Core.Sim.Plut.StreamDirection(View);
            float depth = -((star.X * sx) + (star.Y * sy) + (star.Z * sz));
            float brightness = MathHelper.Clamp(1f - (depth / SpawnDepth), 0.15f, 0.85f);
            var colour = new Color(brightness, brightness, brightness);
            int size = depth < 600 ? 2 : 1;
            spriteBatch.Draw(pixel, new Rectangle((int)screen.X, (int)screen.Y, size, size), colour);
        }
    }

    /// <summary>A new particle at the far end of this view's stream, spread across the window.</summary>
    /// <param name="anyDepth">True to scatter it along the stream as well, for a fresh field.</param>
    private Star NewStar(bool anyDepth)
    {
        // The far end of the stream is the opposite of the direction the dust travels
        (float sx, float sy, float sz) = EliteRemake.Core.Sim.Plut.StreamDirection(View);
        float spread = SpawnDepth * 0.5f;
        float reach = anyDepth
            ? (float)(_random.NextDouble() * SpawnDepth) + 1f
            : SpawnDepth;

        return new Star(
            (-sx * reach) + ((sx == 0 ? Spread() : 0) * spread),
            (-sy * reach) + ((sy == 0 ? Spread() : 0) * spread),
            (-sz * reach) + ((sz == 0 ? Spread() : 0) * spread));
    }

    /// <summary>A random offset from -1 to 1 across the window.</summary>
    private float Spread() => (float)((_random.NextDouble() * 2) - 1);

    private struct Star(float x, float y, float z)
    {
        public float X = x;
        public float Y = y;
        public float Z = z;
    }
}
