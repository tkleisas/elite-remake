using System.Numerics;
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

    /// <summary>
    /// The layer a ship's detail edges and the recesses they enclose are drawn in, on top of the
    /// faces. The original draws ships as lines on top of whatever is already on screen, so its
    /// detail is never painted over by the face it decorates; filling the faces loses that for
    /// free, so the two are separated into ordered layers instead.
    /// </summary>
    private const int DetailLayer = 1;

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

    /// <summary>How many detail edges the last ship queued, for diagnostics.</summary>
    public int LastDetailEdgeCount { get; private set; }

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
        // Painter's algorithm: draw the furthest surfaces first, which is how the original resolves
        // which lines are in front of which. A ship's detail edges and the recess they enclose are
        // decorations that belong on top of the face they mark, so they are a later layer than the
        // faces however their depths compare - sorting by depth alone cannot separate a slot from
        // the face it is cut into, because the slot is exactly as far away as the face around it
        _candidates.Sort(static (a, b) => a.Layer != b.Layer
            ? a.Layer.CompareTo(b.Layer)
            : b.Depth.CompareTo(a.Depth));

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
    /// <param name="detailColour">
    /// The colour for the recess that a ship's detail edges enclose - the hollow of a space
    /// station's docking slot, for instance. Null leaves the detail unfilled.
    /// </param>
    /// <param name="detailLipColour">
    /// The colour for the detail edges themselves. Null draws them in the ship's own colour.
    /// </param>
    public void DrawShip(
        ShipMesh mesh,
        System.Numerics.Vector3 position,
        ShipOrientation orientation,
        ViewCamera camera,
        Color colour,
        float visibilityDistance,
        Color? detailColour = null,
        Color? detailLipColour = null)
    {
        // The original reduces the ship's z-distance to the range 0-31 by dividing by 128 (its
        // LL9 part 2 shifts the 16-bit z value right seven times); a ship at z_hi >= 16 is either
        // drawn as a dot or is far enough away that hidden-line detail is not worth culling, and
        // carries a visibility value of 7. We also need the two high bytes for the line clip below
        int zInt = (int)MathF.Max(position.Z, 0);
        int zHigh = (zInt >> 8) & 0xFF;
        int visibility = Math.Clamp(zHigh / 2, 0, 7);

        if ((zInt / 256) > visibilityDistance)
        {
            DrawDot(camera, position, colour);
            return;
        }

        var polygon = new System.Numerics.Vector3[16];
        var clipped = new System.Numerics.Vector3[20];

        Color recess = detailColour ?? colour;
        Color lip = detailLipColour ?? colour;

        foreach (ShipFace face in mesh.Faces)
        {
            System.Numerics.Vector3 normal = orientation.ToView(face.Normal);

            // A face whose visibility value is below the ship's distance is always shown (this is
            // how the original treats low-visibility faces); otherwise its normal must face us
            bool alwaysShown = face.Visibility < visibility;
            if (!alwaysShown && !FaceIsVisible(mesh, face, position, orientation, normal, out _))
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

        // Draw the detail edges that decorate each visible face: the recess they enclose first, so
        // a station's slot reads as a hollow cut into its face, and then each edge on top of that.
        // The original has no recess to fill, because it draws a wireframe, but it does draw a
        // slot's outline in the same bright white as the rest of a ship's lines - and that outline
        // is what makes an opening read as an opening rather than a panel
        List<DetailEdge> details = CollectVisibleDetailEdges(mesh, position, orientation, visibility);

        LastDetailEdgeCount = details.Count;
        for (int i = 0; i < details.Count; i++)
        {
            if (i == 0 || details[i].Face != details[i - 1].Face)
            {
                int face = details[i].Face;
                QueueDetailEdgeRecess(
                    mesh,
                    details.FindAll(candidate => candidate.Face == face),
                    position,
                    orientation,
                    camera,
                    recess);
            }

            QueueDetailEdge(mesh, details[i].Edge, position, orientation, camera, lip);
        }
    }

    /// <summary>
    /// The blueprint's detail edges whose decorating face is visible and close enough to show them.
    /// </summary>
    private static List<DetailEdge> CollectVisibleDetailEdges(
        ShipMesh mesh,
        System.Numerics.Vector3 position,
        ShipOrientation orientation,
        int visibility)
    {
        var visible = new List<DetailEdge>();
        foreach (DetailEdge detail in mesh.DetailEdges)
        {
            // The original only draws a detail edge if the face it decorates is facing us, which is
            // the same test it applies to the face itself. Its visibility distance then hides the
            // edge while the ship is too far away for the detail to be worth drawing: a station's
            // slot carries 30, so it appears once we are within about 2000 units, and a Cobra's
            // exhaust detail carries 6, so it only appears up close
            if (detail.Edge.Visibility < visibility)
            {
                continue;
            }

            if (!TryFindFace(mesh, detail.Face, out ShipFace parent))
            {
                continue;
            }

            System.Numerics.Vector3 parentNormal = orientation.ToView(parent.Normal);
            if (parent.Visibility >= visibility &&
                !FaceIsVisible(mesh, parent, position, orientation, parentNormal, out _))
            {
                continue;
            }

            visible.Add(detail);
        }

        return visible;
    }

    /// <summary>
    /// Fills the shape a group of detail edges encloses, which is the recess a station's docking
    /// slot is cut into.
    /// </summary>
    private void QueueDetailEdgeRecess(
        ShipMesh mesh,
        List<DetailEdge> group,
        System.Numerics.Vector3 position,
        ShipOrientation orientation,
        ViewCamera camera,
        Color colour)
    {
        if (group.Count < 3)
        {
            return;
        }

        // Walk the group so the outline comes out as a simple polygon rather than a scatter of
        // unrelated segments
        var loop = new List<int> { group[0].Edge.Vertex1, group[0].Edge.Vertex2 };
        var remaining = new List<DetailEdge>(group);
        remaining.RemoveAt(0);

        while (remaining.Count > 0)
        {
            int last = loop[^1];
            int found = remaining.FindIndex(
                edge => edge.Edge.Vertex1 == last || edge.Edge.Vertex2 == last);

            if (found < 0)
            {
                return; // the edges do not form a single closed outline
            }

            DetailEdge edge = remaining[found];
            remaining.RemoveAt(found);
            loop.Add(edge.Edge.Vertex1 == last ? edge.Edge.Vertex2 : edge.Edge.Vertex1);
        }

        var screen = new Vector2[loop.Count];
        float depth = 0;
        for (int i = 0; i < loop.Count; i++)
        {
            System.Numerics.Vector3 point = position + orientation.ToView(mesh.Vertices[loop[i]]);
            if (point.Z < ViewCamera.NearPlane)
            {
                return; // too close to the camera to fill safely
            }

            screen[i] = camera.ProjectUnchecked(point);
            depth += point.Z;
        }

        _candidates.Add(new Candidate(screen, depth / loop.Count, colour, DetailLayer));
    }


    /// <summary>
    /// The original's back-face test: a face is visible when its outward normal points back towards
    /// us, which is the dot product of the normal with the vector from the ship to the camera.
    /// </summary>
    /// <remarks>
    /// The dot product is written out rather than called: the presentation layer aliases Vector3 to
    /// MonoGame's type, so an imported System.Numerics.Vector3 is the very same type and the two
    /// libraries' Dot methods are indistinguishable by signature. Doing the arithmetic by hand keeps
    /// the sign unambiguous.
    /// </remarks>
    private static bool FaceIsVisible(
        ShipMesh mesh,
        ShipFace face,
        System.Numerics.Vector3 position,
        ShipOrientation orientation,
        System.Numerics.Vector3 normal,
        out System.Numerics.Vector3 centre)
    {
        System.Numerics.Vector3 local = System.Numerics.Vector3.Zero;
        foreach (int index in face.Indices)
        {
            local += mesh.Vertices[index];
        }

        local /= face.Indices.Length;
        centre = position + orientation.ToView(local);

        return (normal.X * -centre.X) + (normal.Y * -centre.Y) + (normal.Z * -centre.Z) > 0;
    }

    /// <summary>Finds a face by its number in the original blueprint.</summary>
    private static bool TryFindFace(ShipMesh mesh, int faceNumber, out ShipFace face)
    {
        foreach (ShipFace candidate in mesh.Faces)
        {
            if (candidate.FaceNumber == faceNumber)
            {
                face = candidate;
                return true;
            }
        }

        face = null!;
        return false;
    }

    /// <summary>
    /// Queues one of a ship's detail lines, clipping it against the near plane and skipping it when
    /// either end is too far off screen for the original's line clipper.
    /// </summary>
    private void QueueDetailEdge(
        ShipMesh mesh,
        ShipEdge edge,
        System.Numerics.Vector3 position,
        ShipOrientation orientation,
        ViewCamera camera,
        Color colour)
    {
        System.Numerics.Vector3 a = position + orientation.ToView(mesh.Vertices[edge.Vertex1]);
        System.Numerics.Vector3 b = position + orientation.ToView(mesh.Vertices[edge.Vertex2]);

        if (!ClipToNearPlane(ref a, ref b))
        {
            return;
        }

        Vector2 pa = camera.ProjectUnchecked(a);
        Vector2 pb = camera.ProjectUnchecked(b);
        Vector2 delta = pb - pa;
        float length = delta.Length();
        if (length < 0.5f)
        {
            return;
        }

        Vector2 normal = new(-delta.Y / length, delta.X / length);
        float halfWidth = MathF.Max(0.75f, camera.FocalLength / 300f);

        _candidates.Add(new Candidate(
            [
                pa + (normal * halfWidth),
                pb + (normal * halfWidth),
                pb - (normal * halfWidth),
                pa - (normal * halfWidth),
            ],
            MathF.Min(a.Z, b.Z),
            colour,
            DetailLayer));
    }

    /// <summary>
    /// Clips a line segment to the near plane, as the original's LL118 does with its screen edges.
    /// </summary>
    private static bool ClipToNearPlane(ref System.Numerics.Vector3 a, ref System.Numerics.Vector3 b)
    {
        const float near = ViewCamera.NearPlane;
        if (a.Z >= near && b.Z >= near)
        {
            return true;
        }

        if (a.Z < near && b.Z < near)
        {
            return false;
        }

        float t = (near - a.Z) / (b.Z - a.Z);
        System.Numerics.Vector3 crossing = a + ((b - a) * t);

        if (a.Z < near)
        {
            a = crossing;
        }
        else
        {
            b = crossing;
        }

        return true;
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

    /// <summary>
    /// One surface queued for drawing. Layers are drawn in order, and within a layer the furthest
    /// surface is drawn first.
    /// </summary>
    private readonly record struct Candidate(Vector2[] Points, float Depth, Color Colour, int Layer = 0);

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
