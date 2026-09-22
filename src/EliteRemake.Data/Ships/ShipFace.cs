using System.Text.Json.Serialization;

namespace EliteRemake.Data.Ships;

/// <summary>A ship blueprint face, decoded from the four bytes emitted by the original FACE macro.</summary>
public sealed class ShipFace
{
    /// <summary>X component of the face normal (sign restored from the sign byte).</summary>
    [JsonPropertyName("x")]
    public int X { get; init; }

    /// <summary>Y component of the face normal.</summary>
    [JsonPropertyName("y")]
    public int Y { get; init; }

    /// <summary>Z component of the face normal.</summary>
    [JsonPropertyName("z")]
    public int Z { get; init; }

    /// <summary>Visibility distance, beyond which the face is always shown.</summary>
    [JsonPropertyName("visibility")]
    public int Visibility { get; init; }
}
