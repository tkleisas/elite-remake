using System.Numerics;

namespace EliteRemake.Core.Ships;

/// <summary>A vertex from a ship blueprint, in the ship's local coordinates.</summary>
public readonly record struct BlueprintVertex(int X, int Y, int Z, int Face1, int Face2, int Face3, int Face4, int Visibility);

/// <summary>An edge from a ship blueprint, with the two faces either side of it.</summary>
public readonly record struct BlueprintEdge(int Vertex1, int Vertex2, int Face1, int Face2, int Visibility);

/// <summary>A face from a ship blueprint, with its outward normal.</summary>
public readonly record struct BlueprintFace(int NormalX, int NormalY, int NormalZ, int Visibility);

/// <summary>A polygon reconstructed from the blueprint's edges.</summary>
public sealed class ShipFace
{
    /// <summary>Indices into <see cref="ShipMesh.Vertices"/>, in winding order.</summary>
    public required int[] Indices { get; init; }

    /// <summary>The outward unit normal in the ship's local frame.</summary>
    public required Vector3 Normal { get; init; }

    /// <summary>The blueprint's visibility distance for this face.</summary>
    public required int Visibility { get; init; }

    /// <summary>The face number in the original blueprint.</summary>
    public required int FaceNumber { get; init; }
}

/// <summary>A line segment from the blueprint, used for wireframe-style detail.</summary>
public readonly record struct ShipEdge(int Vertex1, int Vertex2, int Face1, int Face2, int Visibility);

/// <summary>
/// Solid geometry for one ship, built from the original blueprint's vertices, edges and faces.
/// </summary>
/// <remarks>
/// The original draws ships as wireframes with hidden-line removal, so the blueprints list edges
/// (each with the two faces either side of it) rather than face polygons. To shade the ships as
/// solids we reconstruct each face's vertex loop from its edges, then order the loop so that its
/// winding agrees with the blueprint's outward normal. Faces with fewer than three vertices, and
/// the dummy face 15 that the original uses for "always visible" detail edges, are ignored.
/// </remarks>
public sealed class ShipMesh
{
    /// <summary>The ship's vertices in local coordinates.</summary>
    public required Vector3[] Vertices { get; init; }

    /// <summary>The reconstructed face polygons.</summary>
    public required ShipFace[] Faces { get; init; }

    /// <summary>The blueprint's edges, for the detail lines that no face covers.</summary>
    public required ShipEdge[] Edges { get; init; }

    /// <summary>Vertex visibilities, as the blueprint's 5-bit values.</summary>
    public required int[] VertexVisibility { get; init; }

    /// <summary>The largest distance from the origin to any vertex.</summary>
    public float Radius { get; init; }

    /// <summary>The dummy face number the original reserves for "always visible".</summary>
    public const int NoFace = 15;

    /// <summary>
    /// Builds a mesh from blueprint data.
    /// </summary>
    /// <param name="vertices">The blueprint's vertices.</param>
    /// <param name="edges">The blueprint's edges.</param>
    /// <param name="faces">The blueprint's faces.</param>
    /// <param name="normalScale">
    /// The blueprint's "normals are scaled by 2^n" value; the raw normals are divided by it.
    /// </param>
    public static ShipMesh Build(
        IReadOnlyList<BlueprintVertex> vertices,
        IReadOnlyList<BlueprintEdge> edges,
        IReadOnlyList<BlueprintFace> faces,
        int normalScale)
    {
        var points = new Vector3[vertices.Count];
        var vertexVisibility = new int[vertices.Count];
        for (int i = 0; i < vertices.Count; i++)
        {
            // The original's y axis points down the screen, so flip it here and work in a
            // conventional right-handed frame from this point on
            points[i] = new Vector3(vertices[i].X, -vertices[i].Y, vertices[i].Z);
            vertexVisibility[i] = vertices[i].Visibility;
        }

        var shipEdges = new ShipEdge[edges.Count];
        for (int i = 0; i < edges.Count; i++)
        {
            shipEdges[i] = new ShipEdge(edges[i].Vertex1, edges[i].Vertex2, edges[i].Face1, edges[i].Face2, edges[i].Visibility);
        }

        float scale = MathF.Pow(2, normalScale);
        var shipFaces = new List<ShipFace>(faces.Count);
        for (int f = 0; f < faces.Count; f++)
        {
            var normal = new Vector3(faces[f].NormalX, -faces[f].NormalY, faces[f].NormalZ) / scale;
            if (normal.LengthSquared() > 0)
            {
                normal = Vector3.Normalize(normal);
            }

            int[]? loop = ReconstructLoop(f, edges, vertices.Count);
            if (loop is null || loop.Length < 3)
            {
                continue;
            }

            // Order the loop so its winding matches the outward normal
            loop = OrientLoop(loop, points, normal);

            shipFaces.Add(new ShipFace
            {
                Indices = loop,
                Normal = normal,
                Visibility = faces[f].Visibility,
                FaceNumber = f,
            });
        }

        float radius = 0;
        foreach (Vector3 point in points)
        {
            radius = MathF.Max(radius, point.Length());
        }

        return new ShipMesh
        {
            Vertices = points,
            Faces = shipFaces.ToArray(),
            Edges = shipEdges,
            VertexVisibility = vertexVisibility,
            Radius = radius,
        };
    }

    /// <summary>
    /// Walks the edges that reference a face to recover the face's vertex loop, returning null if
    /// the edges do not form a single closed loop.
    /// </summary>
    private static int[]? ReconstructLoop(int face, IReadOnlyList<BlueprintEdge> edges, int vertexCount)
    {
        // Collect the vertices and adjacency for this face
        var neighbours = new List<int>[vertexCount];
        int edgeCount = 0;
        foreach (BlueprintEdge edge in edges)
        {
            if (edge.Face1 != face && edge.Face2 != face)
            {
                continue;
            }

            edgeCount++;
            (neighbours[edge.Vertex1] ??= []).Add(edge.Vertex2);
            (neighbours[edge.Vertex2] ??= []).Add(edge.Vertex1);
        }

        if (edgeCount < 3)
        {
            return null;
        }

        var faceVertices = new List<int>();
        for (int i = 0; i < vertexCount; i++)
        {
            if (neighbours[i] is { Count: > 0 })
            {
                faceVertices.Add(i);
            }
        }

        if (faceVertices.Count != edgeCount)
        {
            // A closed loop has exactly as many edges as vertices; anything else is not a simple
            // polygon, so fall back to the raw vertex order (fan), which is still shaded correctly
            // for the convex faces these ships are made of
            return faceVertices.ToArray();
        }

        // Walk the loop
        var loop = new List<int>(faceVertices.Count);
        var visited = new HashSet<int>();
        int current = faceVertices[0];
        int previous = -1;
        while (true)
        {
            loop.Add(current);
            visited.Add(current);

            int next = -1;
            foreach (int candidate in neighbours[current]!)
            {
                if (candidate != previous && !visited.Contains(candidate))
                {
                    next = candidate;
                    break;
                }
            }

            if (next < 0)
            {
                break;
            }

            previous = current;
            current = next;
        }

        return loop.Count == faceVertices.Count ? loop.ToArray() : faceVertices.ToArray();
    }

    /// <summary>
    /// Reverses the loop if its winding disagrees with the supplied outward normal.
    /// </summary>
    private static int[] OrientLoop(int[] loop, Vector3[] points, Vector3 normal)
    {
        // Newell's method for the polygon normal
        var computed = Vector3.Zero;
        for (int i = 0; i < loop.Length; i++)
        {
            Vector3 a = points[loop[i]];
            Vector3 b = points[loop[(i + 1) % loop.Length]];
            computed.X += (a.Y - b.Y) * (a.Z + b.Z);
            computed.Y += (a.Z - b.Z) * (a.X + b.X);
            computed.Z += (a.X - b.X) * (a.Y + b.Y);
        }

        if (Vector3.Dot(computed, normal) >= 0)
        {
            return loop;
        }

        var reversed = (int[])loop.Clone();
        Array.Reverse(reversed);
        return reversed;
    }
}
