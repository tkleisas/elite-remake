namespace EliteDataExtractor.Extraction;

/// <summary>Structural comparison of two decoded ship blueprints.</summary>
internal static class BlueprintComparer
{
    /// <summary>
    /// Compares two blueprints and returns a description of the first difference, or null when they
    /// are identical. <paramref name="expected"/> is the asm-derived blueprint and
    /// <paramref name="actual"/> is the copy decoded from the reference binary.
    /// </summary>
    public static string? Diff(ShipBlueprint expected, ShipBlueprint actual, bool ignoreLayoutOffsets = false)
    {
        ShipHeader left = expected.Header;
        ShipHeader right = actual.Header;

        string? header = Compare("maxCanisters", left.MaxCanisters, right.MaxCanisters)
            ?? Compare("scoopMarketItem", left.ScoopMarketItem, right.ScoopMarketItem)
            ?? Compare("canisterByte", left.CanisterByte, right.CanisterByte)
            ?? Compare("targetableArea", left.TargetableArea, right.TargetableArea)
            ?? (ignoreLayoutOffsets ? null : Compare("edgesOffset", left.EdgesOffset, right.EdgesOffset))
            ?? (ignoreLayoutOffsets ? null : Compare("facesOffset", left.FacesOffset, right.FacesOffset))
            ?? Compare("maxEdges", left.MaxEdges, right.MaxEdges)
            ?? Compare("gunVertex", left.GunVertex, right.GunVertex)
            ?? Compare("explosionCount", left.ExplosionCount, right.ExplosionCount)
            ?? Compare("vertexCount", left.VertexCount, right.VertexCount)
            ?? Compare("edgeCount", left.EdgeCount, right.EdgeCount)
            ?? Compare("faceCount", left.FaceCount, right.FaceCount)
            ?? Compare("bounty", left.Bounty, right.Bounty)
            ?? Compare("visibilityDistance", left.VisibilityDistance, right.VisibilityDistance)
            ?? Compare("maxEnergy", left.MaxEnergy, right.MaxEnergy)
            ?? Compare("maxSpeed", left.MaxSpeed, right.MaxSpeed)
            ?? Compare("normalScale", left.NormalScale, right.NormalScale)
            ?? Compare("laserPower", left.LaserPower, right.LaserPower)
            ?? Compare("missiles", left.Missiles, right.Missiles)
            ?? Compare("laserMissileByte", left.LaserMissileByte, right.LaserMissileByte);

        if (header is not null)
        {
            return header;
        }

        if (expected.Vertices.Count != actual.Vertices.Count)
        {
            return $"vertex count {expected.Vertices.Count} != {actual.Vertices.Count}";
        }

        for (int i = 0; i < expected.Vertices.Count; i++)
        {
            ShipVertex a = expected.Vertices[i];
            ShipVertex b = actual.Vertices[i];
            string? difference = Compare($"vertex[{i}].x", a.X, b.X)
                ?? Compare($"vertex[{i}].y", a.Y, b.Y)
                ?? Compare($"vertex[{i}].z", a.Z, b.Z)
                ?? Compare($"vertex[{i}].visibility", a.Visibility, b.Visibility)
                ?? CompareList($"vertex[{i}].faces", a.Faces, b.Faces);
            if (difference is not null)
            {
                return difference;
            }
        }

        if (expected.Edges.Count != actual.Edges.Count)
        {
            return $"edge count {expected.Edges.Count} != {actual.Edges.Count}";
        }

        for (int i = 0; i < expected.Edges.Count; i++)
        {
            ShipEdge a = expected.Edges[i];
            ShipEdge b = actual.Edges[i];
            string? difference = Compare($"edge[{i}].vertex1", a.Vertex1, b.Vertex1)
                ?? Compare($"edge[{i}].vertex2", a.Vertex2, b.Vertex2)
                ?? Compare($"edge[{i}].visibility", a.Visibility, b.Visibility)
                ?? CompareList($"edge[{i}].faces", a.Faces, b.Faces);
            if (difference is not null)
            {
                return difference;
            }
        }

        if (expected.Faces.Count != actual.Faces.Count)
        {
            return $"face count {expected.Faces.Count} != {actual.Faces.Count}";
        }

        for (int i = 0; i < expected.Faces.Count; i++)
        {
            ShipFace a = expected.Faces[i];
            ShipFace b = actual.Faces[i];
            string? difference = Compare($"face[{i}].x", a.X, b.X)
                ?? Compare($"face[{i}].y", a.Y, b.Y)
                ?? Compare($"face[{i}].z", a.Z, b.Z)
                ?? Compare($"face[{i}].visibility", a.Visibility, b.Visibility);
            if (difference is not null)
            {
                return difference;
            }
        }

        return null;
    }

    private static string? Compare(string field, int expected, int actual) =>
        expected == actual ? null : $"{field} {expected} != {actual}";

    private static string? CompareList(string field, List<int> expected, List<int> actual)
    {
        if (expected.Count != actual.Count)
        {
            return $"{field} count {expected.Count} != {actual.Count}";
        }

        for (int i = 0; i < expected.Count; i++)
        {
            if (expected[i] != actual[i])
            {
                return $"{field}[{i}] {expected[i]} != {actual[i]}";
            }
        }

        return null;
    }
}
