using EliteRemake.Core.Graphics;
using EliteRemake.Core.Maths;
using EliteRemake.Core.Ships;
using EliteRemake.Game.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EliteRemake.Game.Scenes;

/// <summary>
/// A development scene that shows one ship model turning on the spot, so the extracted geometry,
/// the projection and the shading can all be checked against the original's blueprints.
/// </summary>
public sealed class ShipViewerScene : IScene
{
    private readonly ShipMesh _mesh;
    private readonly string _name;
    private readonly MeshRenderer _renderer;
    private readonly Starfield _starfield;
    private Orientation _orientation;
    private float _distance;
    private float _time;

    public ShipViewerScene(
        ShipMesh mesh,
        string name,
        GraphicsDevice device,
        ViewCamera camera,
        float distance = 400f,
        float? fixedHeading = null,
        float fixedPitch = 0f)
    {
        _mesh = mesh;
        _name = name;
        _renderer = new MeshRenderer(device);
        _starfield = new Starfield();
        _orientation = Orientation.FromHeadingPitch(0, 0);
        _distance = distance;
        _fixedHeading = fixedHeading;
        _fixedPitch = fixedPitch;
        Animate = fixedHeading is null;
        Camera = camera;
    }

    private readonly float? _fixedHeading;
    private readonly float _fixedPitch;

    public ViewCamera Camera { get; }

    /// <summary>True to rotate the ship; set false for a still frame.</summary>
    public bool Animate { get; set; } = true;

    /// <summary>Draws the ship's edges as well as its faces, for checking the blueprint data.</summary>
    public bool Wireframe { get; set; }

    public void Update(float elapsedSeconds)
    {
        _time += elapsedSeconds;
        _orientation = _fixedHeading is { } heading
            ? Orientation.FromHeadingPitch(heading * Math.PI / 180.0, _fixedPitch * Math.PI / 180.0)
            : Orientation.FromHeadingPitch(_time * 0.6, Math.Sin(_time * 0.4) * 0.5);

        _starfield.Update(Animate ? 60f * elapsedSeconds : 0);
    }

    public void Draw(SpriteBatch spriteBatch, Texture2D pixel, GraphicsDevice device)
    {
        device.Clear(Palette.Space);
        spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        _starfield.Draw(spriteBatch, pixel, Camera);
        spriteBatch.End();

        _renderer.Begin();
        _renderer.DrawShip(
            _mesh,
            new System.Numerics.Vector3(0, 0, _distance),
            ShipOrientation.FromEliteOrientation(_orientation),
            Camera,
            Palette.Hull,

            // The viewer always shows the model in full, rather than reducing it to a dot
            visibilityDistance: float.MaxValue,
            Palette.StationSlot);
        _renderer.End();

        if (Wireframe)
        {
            DrawWireframe(spriteBatch, pixel);
        }
    }

    private void DrawWireframe(SpriteBatch spriteBatch, Texture2D pixel)
    {
        spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        foreach (ShipEdge edge in _mesh.Edges)
        {
            System.Numerics.Vector3 a = ToView(_mesh.Vertices[edge.Vertex1]);
            System.Numerics.Vector3 b = ToView(_mesh.Vertices[edge.Vertex2]);
            if (a.Z <= 0 || b.Z <= 0)
            {
                continue;
            }

            Vector2 pa = Camera.ProjectUnchecked(a);
            Vector2 pb = Camera.ProjectUnchecked(b);
            DrawLine(spriteBatch, pixel, pa, pb, new Color(0, 255, 0, 96));
        }

        spriteBatch.End();
    }

    private System.Numerics.Vector3 ToView(System.Numerics.Vector3 local)
    {
        var orientation = ShipOrientation.FromEliteOrientation(_orientation);
        return new System.Numerics.Vector3(0, 0, _distance) + orientation.ToView(local);
    }

    private static void DrawLine(SpriteBatch spriteBatch, Texture2D pixel, Vector2 from, Vector2 to, Color colour)
    {
        Vector2 delta = to - from;
        float length = delta.Length();
        if (length < 0.5f)
        {
            return;
        }

        float angle = MathF.Atan2(delta.Y, delta.X);
        spriteBatch.Draw(pixel, from, null, colour, angle, Vector2.Zero, new Vector2(length, 1), SpriteEffects.None, 0);
    }

    public string Name => _name;

    public string StatusLine =>
        $"{_name}: {_mesh.Vertices.Length} vertices, {_mesh.Faces.Length} faces, {_mesh.Edges.Length} edges, " +
        $"{_mesh.DetailEdges.Length} detail, " +
        $"radius {_mesh.Radius:0}, distance {_distance:0}, drawn triangles {_renderer.LastTriangleCount} " +
        $"(queued detail edges {_renderer.LastDetailEdgeCount})";
}
