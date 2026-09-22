using System.Text.Json.Serialization;

namespace EliteRemake.Data.Ships;

/// <summary>
/// A ship blueprint vertex, decoded from the six bytes emitted by the original VERTEX macro.
/// </summary>
public sealed class ShipVertex
{
    /// <summary>X coordinate in the original's internal units (sign restored from the sign byte).</summary>
    [JsonPropertyName("x")]
    public int X { get; init; }

    /// <summary>Y coordinate in the original's internal units.</summary>
    [JsonPropertyName("y")]
    public int Y { get; init; }

    /// <summary>Z coordinate in the original's internal units.</summary>
    [JsonPropertyName("z")]
    public int Z { get; init; }

    /// <summary>
    /// The four face numbers stored for this vertex, in the order face1, face2, face3, face4.
    ///
    /// The original stores all four verbatim (duplicates, zeros and "unused" markers included). A
    /// value below <see cref="ShipBlueprint.Faces"/>'s count is a zero-based index into that list.
    /// The value 15 is special: LL9 forces the face visibility table entry for face 15 to 255, so a
    /// vertex associated with face 15 is always visible.
    /// </summary>
    [JsonPropertyName("faces")]
    public IReadOnlyList<int> Faces { get; init; } = [];

    /// <summary>Visibility distance, beyond which the vertex is not shown (bits 0-4 of the sign byte).</summary>
    [JsonPropertyName("visibility")]
    public int Visibility { get; init; }
}
