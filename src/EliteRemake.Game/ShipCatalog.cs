using EliteRemake.Core.Ships;

namespace EliteRemake.Game;

/// <summary>
/// Provides the ship meshes the game can draw.
/// </summary>
/// <remarks>
/// TEMPORARY: until the blueprint extractor's output is wired in, this supplies a placeholder hull
/// so the renderer can be exercised. The catalog is replaced by the extracted BBC blueprints in the
/// data pipeline milestone.
/// </remarks>
public static class ShipCatalog
{
    private static readonly Lazy<(string Name, ShipMesh Mesh)[]> Ships = new(BuildPlaceholder);

    public static IReadOnlyList<(string Name, ShipMesh Mesh)> All => Ships.Value;

    public static (string Name, ShipMesh Mesh) First() => Ships.Value[0];

    public static (string Name, ShipMesh Mesh) Find(string name)
    {
        foreach ((string candidate, ShipMesh mesh) in Ships.Value)
        {
            if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase))
            {
                return (candidate, mesh);
            }
        }

        throw new ArgumentException($"No such ship: {name}. Known ships: {string.Join(", ", Ships.Value.Select(s => s.Name))}");
    }

    /// <summary>How far back the viewer should place a ship so it fills a sensible part of the view.</summary>
    public static float ViewerDistance(ShipMesh mesh) => MathF.Max(mesh.Radius * 3.2f, 60f);

    private static (string, ShipMesh)[] BuildPlaceholder()
    {
        // A simple octahedron, standing in for the extracted blueprints
        var vertices = new[]
        {
            new System.Numerics.Vector3(120, 0, 0),
            new System.Numerics.Vector3(-120, 0, 0),
            new System.Numerics.Vector3(0, 90, 0),
            new System.Numerics.Vector3(0, -90, 0),
            new System.Numerics.Vector3(0, 0, 160),
            new System.Numerics.Vector3(0, 0, -160),
        };

        var faces = new List<ShipFace>();
        void AddFace(int a, int b, int c)
        {
            System.Numerics.Vector3 normal = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]));
            faces.Add(new ShipFace
            {
                Indices = [a, b, c],
                Normal = normal,
                Visibility = 31,
                FaceNumber = faces.Count,
            });
        }

        AddFace(0, 2, 4);
        AddFace(2, 1, 4);
        AddFace(1, 3, 4);
        AddFace(3, 0, 4);
        AddFace(2, 0, 5);
        AddFace(1, 2, 5);
        AddFace(3, 1, 5);
        AddFace(0, 3, 5);

        var edges = new List<ShipEdge>();
        for (int i = 0; i < vertices.Length; i++)
        {
            for (int j = i + 1; j < vertices.Length; j++)
            {
                if (i == 0 && j == 1)
                {
                    continue;
                }

                if (i == 2 && j == 3)
                {
                    continue;
                }

                if (i == 4 && j == 5)
                {
                    continue;
                }

                edges.Add(new ShipEdge(i, j, 0, 1, 31));
            }
        }

        var mesh = new ShipMesh
        {
            Vertices = vertices,
            Faces = faces.ToArray(),
            Edges = edges.ToArray(),
            VertexVisibility = [31, 31, 31, 31, 31, 31],
            Radius = 160,
        };

        return [("Placeholder hull", mesh)];
    }
}
