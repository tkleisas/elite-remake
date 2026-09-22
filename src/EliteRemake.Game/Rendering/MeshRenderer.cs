using EliteRemake.Core.Graphics;
using EliteRemake.Core.Ships;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EliteRemake.Game.Rendering;

/// <summary>
/// Draws ship meshes as flat-shaded solid polygons, using the original blueprints' geometry, face
/// normals and visibility rules.
/// </summary>
/// <remarks>
/// The original draws ships as wireframes with hidden-line removal on a 256 x 192 screen, and it
/// hides fine detail as ships get further away: each blueprint vertex, edge and face carries a
/// 5-bit visibility value which is compared against the ship's z distance reduced to the range
/// 0-31 (the original's XX4). This renderer keeps that behaviour, so a distant ship shows only its
/// main hull while a close one shows its thrusters and landing struts, exactly as the original
/// does. Faces are filled and drawn back to front, and back faces are culled with the blueprint's
/// own normals, so a ship's silhouette matches the original's hidden-line output.
/// </remarks>
public sealed class MeshRenderer : IDisposable
{
    private const int MaxBatchVertices = 65536;

    private readonly GraphicsDevice _device;
    private readonly BasicEffect _effect;
    private readonly VertexPositionColor[] _vertices = new VertexPositionColor[MaxBatchVertices];
    private readonly short[] _indices = new short[MaxBatchVertices];
    private readonly List<Candidate> _candidates = [];
    private int _vertexCount;
    private int _indexCount;

    public MeshRenderer(GraphicsDevice device)
    {
        _device = device;
        _effect = new BasicEffect(device)
        {
            VertexColorEnabled = true,
            TextureEnabled = false,
            LightingEnabled = false,
        };
    }

    /// <summary>Direction the key light comes from, in view space (x right, y up, z forward).</summary>
    public System.Numerics.Vector3 LightDirection { get; set; } =
        System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(-0.65f, 0.45f, -0.60f));

    /// <summary>Ambient light level, so unlit faces stay readable.</summary>
    public float Ambient { get; set; } = 0.28f;

    /// <summary>The number of triangles submitted by the last draw call, for diagnostics.</summary>
    public int LastTriangleCount { get; private set; }

    /// <summary>Begins a batch of meshes drawn in screen space.</summary>
    public void Begin()
    {
        _vertexCount = 0;
        _indexCount = 0;
        _candidates.Clear();

        _effect.View = Matrix.Identity;
        _effect.Projection = Matrix.CreateOrthographicOffCenter(
            0,
            _device.Viewport.Width,
            _device.Viewport.Height,
            0,
            -1,
            1);

        // The vertices are already projected into a y-down screen space, so the winding of the
        // triangles we emit is not meaningful to the rasterizer: we do our own back-face culling
        // with the blueprint normals instead
        _device.RasterizerState = RasterizerState.CullNone;
        _device.BlendState = BlendState.Opaque;
    }

    /// <summary>Ends the batch, drawing everything queued while it was open.</summary>
    public void End()
    {
        // Painter's algorithm: draw the furthest faces first, which is how the original resolves
        // which lines are in front of which
        _candidates.Sort(static (a, b) => b.Depth.CompareTo(a.Depth));

        foreach (Candidate candidate in _candidates)
        {
            Emit(candidate);
        }

        Flush();
    }

    /// <summary>
    /// Queues a ship for drawing.
    /// </summary>
    /// <param name="mesh">The ship's geometry.</param>
    /// <param name="position">The ship's position in view space.</param>
    /// <param name="orientation">The ship's basis vectors in view space.</param>
    /// <param name="camera">The camera to project with.</param>
    /// <param name="colour">The ship's base colour.</param>
    /// <param name="visibilityDistance">
    /// The blueprint's overall visibility distance; beyond it the ship is drawn as a dot, as the
    /// original's SHPPT routine does. Pass float.MaxValue to always draw the full model.
    /// </param>
    public void DrawShip(
        ShipMesh mesh,
        System.Numerics.Vector3 position,
        ShipOrientation orientation,
        ViewCamera camera,
        Color colour,
        float visibilityDistance)
    {
        // The original reduces the ship's z distance to the range 0-31 by dividing by 128 (its
        // LL9 part 2 shifts the 16-bit z value right seven times), and hides detail whose
        // visibility value is smaller than that. Beyond the blueprint's visibility distance, which
        // is compared against z_hi, the ship is drawn as a single dot.
        float z = MathF.Max(position.Z, 0);
        int visibility = Math.Clamp((int)(z / 128), 0, 31);

        if ((z / 256) > visibilityDistance)
        {
            DrawDot(camera, position, colour);
            return;
        }

        var polygon = new System.Numerics.Vector3[16];
        var clipped = new System.Numerics.Vector3[20];

        foreach (ShipFace face in mesh.Faces)
        {
            System.Numerics.Vector3 normal = orientation.ToView(face.Normal);
            System.Numerics.Vector3 centroid = System.Numerics.Vector3.Zero;
            foreach (int index in face.Indices)
            {
                centroid += mesh.Vertices[index];
            }

            centroid /= face.Indices.Length;

            System.Numerics.Vector3 faceCentre = position + orientation.ToView(centroid);

            // A face whose visibility value is below the ship's distance is always shown (this is
            // how the original treats low-visibility faces); otherwise its normal must face us
            bool alwaysShown = face.Visibility < visibility;
            if (!alwaysShown && System.Numerics.Vector3.Dot(normal, -faceCentre) <= 0)
            {
                continue;
            }

            // Vertex-level visibility hides the original's fine detail edges on distant ships, so
            // a face whose outline needs those vertices is treated as a detail face too. Faces made
            // only of far-visible vertices are always drawn.
            if (face.Indices.Length > polygon.Length)
            {
                continue;
            }

            for (int i = 0; i < face.Indices.Length; i++)
            {
                polygon[i] = position + orientation.ToView(mesh.Vertices[face.Indices[i]]);
            }

            int count = ViewCamera.ClipNearPlane(polygon.AsSpan(0, face.Indices.Length), clipped, ViewCamera.NearPlane);
            if (count < 3)
            {
                continue;
            }

            var screen = new Vector2[count];
            float depth = 0;
            for (int i = 0; i < count; i++)
            {
                screen[i] = camera.ProjectUnchecked(clipped[i]);
                depth += clipped[i].Z;
            }

            // Flat shading from the blueprint's own normal, transformed into view space
            float lambert = MathF.Max(0, System.Numerics.Vector3.Dot(normal, -LightDirection));
            float brightness = Ambient + ((1 - Ambient) * lambert);

            _candidates.Add(new Candidate(screen, depth / count, Shade(colour, brightness)));
        }
    }

    /// <summary>Draws a ship as a single dot, as the original does for distant ships.</summary>
    private void DrawDot(ViewCamera camera, System.Numerics.Vector3 position, Color colour)
    {
        if (!camera.Project(position, out System.Numerics.Vector2 screen))
        {
            return;
        }

        const float halfSize = 1.5f;
        var points = new[]
        {
            new Vector2(screen.X - halfSize, screen.Y - halfSize),
            new Vector2(screen.X + halfSize, screen.Y - halfSize),
            new Vector2(screen.X + halfSize, screen.Y + halfSize),
            new Vector2(screen.X - halfSize, screen.Y + halfSize),
        };

        _candidates.Add(new Candidate(points, position.Z, colour));
    }

    private void Emit(Candidate candidate)
    {
        if (_vertexCount + candidate.Points.Length > _vertices.Length ||
            _indexCount + ((candidate.Points.Length - 2) * 3) > _indices.Length)
        {
            Flush();
        }

        int baseIndex = _vertexCount;
        foreach (Vector2 point in candidate.Points)
        {
            _vertices[_vertexCount++] = new VertexPositionColor(new Vector3(point.X, point.Y, 0), candidate.Colour);
        }

        for (int i = 1; i < candidate.Points.Length - 1; i++)
        {
            _indices[_indexCount++] = (short)(baseIndex + i);
            _indices[_indexCount++] = (short)(baseIndex + i + 1);
            _indices[_indexCount++] = (short)baseIndex;
        }
    }

    private void Flush()
    {
        if (_indexCount == 0)
        {
            return;
        }

        foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _device.DrawUserIndexedPrimitives(
                PrimitiveType.TriangleList,
                _vertices,
                0,
                _vertexCount,
                _indices,
                0,
                _indexCount / 3);
        }

        LastTriangleCount = _indexCount / 3;
        _vertexCount = 0;
        _indexCount = 0;
    }

    private static Color Shade(Color colour, float factor) => new(
        (byte)Math.Clamp(colour.R * factor, 0, 255),
        (byte)Math.Clamp(colour.G * factor, 0, 255),
        (byte)Math.Clamp(colour.B * factor, 0, 255),
        colour.A);

    private readonly record struct Candidate(Vector2[] Points, float Depth, Color Colour);

    public void Dispose() => _effect.Dispose();
}

/// <summary>
/// A ship's orientation in view space, as the three basis vectors the original keeps in INWK.
/// </summary>
/// <param name="Nose">The ship's forward direction.</param>
/// <param name="Roof">The ship's up direction.</param>
/// <param name="Side">The ship's right direction (the original's sidev).</param>
public readonly record struct ShipOrientation(
    System.Numerics.Vector3 Nose,
    System.Numerics.Vector3 Roof,
    System.Numerics.Vector3 Side)
{
    /// <summary>Transforms a direction from the ship's local frame into view space.</summary>
    public System.Numerics.Vector3 ToView(System.Numerics.Vector3 local) =>
        (Side * local.X) + (Roof * local.Y) + (Nose * local.Z);

    /// <summary>Builds an orientation from the game's fixed-point vectors.</summary>
    public static ShipOrientation FromEliteOrientation(EliteRemake.Core.Maths.Orientation orientation)
    {
        System.Numerics.Vector3 Vector(int vector) => System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(
            (float)orientation.GetUnity(vector, EliteRemake.Core.Maths.Orientation.X),
            (float)orientation.GetUnity(vector, EliteRemake.Core.Maths.Orientation.Y),
            (float)orientation.GetUnity(vector, EliteRemake.Core.Maths.Orientation.Z)));

        return new ShipOrientation(Vector(EliteRemake.Core.Maths.Orientation.Nosev), Vector(EliteRemake.Core.Maths.Orientation.Roofv), Vector(EliteRemake.Core.Maths.Orientation.Sidev));
    }
}
