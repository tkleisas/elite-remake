using EliteRemake.Core.Ships;
using EliteRemake.Data.Ships;

namespace EliteRemake.Game;

/// <summary>
/// The ships the game can draw, built from the blueprints extracted from the original BBC Micro
/// disc sources and verified against the original's own D.MOA-D.MOP binaries.
/// </summary>
public static class ShipCatalog
{
    private static readonly Lazy<Entry[]> Entries = new(Build);
    private static readonly Dictionary<int, Entry> ByTypeCache = [];

    /// <summary>Every ship, in the order the original's XX21 tables define them.</summary>
    public static IReadOnlyList<Entry> All => Entries.Value;

    /// <summary>The first ship, used as a default for the viewer.</summary>
    public static Entry First() => Entries.Value[0];

    /// <summary>Looks a ship up by name, id or blueprint symbol, case-insensitively.</summary>
    public static Entry Find(string name)
    {
        foreach (Entry entry in Entries.Value)
        {
            if (string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(entry.Id, name, StringComparison.OrdinalIgnoreCase) ||
                (entry.Symbol.Length > 0 && string.Equals(entry.Symbol, name, StringComparison.OrdinalIgnoreCase)))
            {
                return entry;
            }
        }

        throw new ArgumentException(
            $"No such ship: {name}. Known ships: {string.Join(", ", Entries.Value.Select(e => e.Name))}");
    }

    /// <summary>Finds a ship by its original ship type number, or null if that slot is empty.</summary>
    public static Entry? ByType(int shipType)
    {
        if (ByTypeCache.TryGetValue(shipType, out Entry cached))
        {
            return cached;
        }

        if (!ShipData.TryGetByType(shipType, out ShipBlueprint? blueprint) || blueprint is null)
        {
            return null;
        }

        var entry = new Entry(
            blueprint.Name,
            blueprint.Id,
            blueprint.Symbols.FirstOrDefault() ?? string.Empty,
            BuildMesh(blueprint),
            blueprint.Header.VisibilityDistance);

        ByTypeCache[shipType] = entry;
        return entry;
    }

    /// <summary>How far back the viewer should place a ship so it fills a sensible part of the view.</summary>
    public static float ViewerDistance(ShipMesh mesh) => MathF.Max(mesh.Radius * 3.2f, 60f);

    private static Entry[] Build()
    {
        var entries = new List<Entry>(ShipData.All.Count);
        foreach (ShipBlueprint blueprint in ShipData.All)
        {
            entries.Add(new Entry(
                blueprint.Name,
                blueprint.Id,
                blueprint.Symbols.FirstOrDefault() ?? string.Empty,
                BuildMesh(blueprint),
                blueprint.Header.VisibilityDistance));
        }

        return entries.ToArray();
    }

    /// <summary>Converts an extracted blueprint into the geometry the renderer draws.</summary>
    public static ShipMesh BuildMesh(ShipGeometry blueprint)
    {
        var vertices = new BlueprintVertex[blueprint.Vertices.Count];
        for (int i = 0; i < vertices.Length; i++)
        {
            ShipVertex vertex = blueprint.Vertices[i];
            vertices[i] = new BlueprintVertex(
                vertex.X,
                vertex.Y,
                vertex.Z,
                vertex.Faces.Count > 0 ? vertex.Faces[0] : ShipMesh.NoFace,
                vertex.Faces.Count > 1 ? vertex.Faces[1] : ShipMesh.NoFace,
                vertex.Faces.Count > 2 ? vertex.Faces[2] : ShipMesh.NoFace,
                vertex.Faces.Count > 3 ? vertex.Faces[3] : ShipMesh.NoFace,
                vertex.Visibility);
        }

        var edges = new BlueprintEdge[blueprint.Edges.Count];
        for (int i = 0; i < edges.Length; i++)
        {
            Data.Ships.ShipEdge edge = blueprint.Edges[i];
            edges[i] = new BlueprintEdge(
                edge.Vertex1,
                edge.Vertex2,
                edge.Faces.Count > 0 ? edge.Faces[0] : ShipMesh.NoFace,
                edge.Faces.Count > 1 ? edge.Faces[1] : ShipMesh.NoFace,
                edge.Visibility);
        }

        var faces = new BlueprintFace[blueprint.Faces.Count];
        for (int i = 0; i < faces.Length; i++)
        {
            Data.Ships.ShipFace face = blueprint.Faces[i];
            faces[i] = new BlueprintFace(face.X, face.Y, face.Z, face.Visibility);
        }

        return ShipMesh.Build(vertices, edges, faces, blueprint.Header.NormalScale);
    }

    /// <summary>One ship the game can draw.</summary>
    /// <param name="Name">The ship's friendly name.</param>
    /// <param name="Id">The ship's stable identifier.</param>
    /// <param name="Symbol">The original's four-character blueprint symbol, if it has one.</param>
    /// <param name="Mesh">The ship's solid geometry.</param>
    /// <param name="VisibilityDistance">The distance beyond which the ship is drawn as a dot.</param>
    public readonly record struct Entry(
        string Name,
        string Id,
        string Symbol,
        ShipMesh Mesh,
        int VisibilityDistance);
}
