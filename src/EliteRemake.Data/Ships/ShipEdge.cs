using System.Text.Json.Serialization;

namespace EliteRemake.Data.Ships;

/// <summary>A ship blueprint edge, decoded from the four bytes emitted by the original EDGE macro.</summary>
public sealed class ShipEdge
{
    /// <summary>Index of the first vertex (the stored value divided by 4).</summary>
    [JsonPropertyName("vertex1")]
    public int Vertex1 { get; init; }

    /// <summary>Index of the second vertex (the stored value divided by 4).</summary>
    [JsonPropertyName("vertex2")]
    public int Vertex2 { get; init; }

    /// <summary>
    /// The two face numbers stored for this edge: face1 and face2. Values below
    /// <see cref="ShipBlueprint.Faces"/>'s count index that list; the value 15 is the original's
    /// always-visible pseudo-face (LL9 sets the visibility table entry for face 15 to 255), which is
    /// what the alloy plate uses for all of its edges.
    /// </summary>
    [JsonPropertyName("faces")]
    public IReadOnlyList<int> Faces { get; init; } = [];

    /// <summary>Visibility distance, beyond which the edge is not shown.</summary>
    [JsonPropertyName("visibility")]
    public int Visibility { get; init; }
}
