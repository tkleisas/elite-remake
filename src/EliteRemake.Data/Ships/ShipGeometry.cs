using System.Text.Json.Serialization;

namespace EliteRemake.Data.Ships;

/// <summary>
/// The geometry and header of a ship blueprint: everything needed to size, draw and collide with a
/// ship. Used both for the main <see cref="ShipBlueprint"/> and for the docked (ship hangar) variant.
/// </summary>
public class ShipGeometry
{
    [JsonPropertyName("header")]
    public ShipHeader Header { get; init; } = new();

    [JsonPropertyName("vertices")]
    public IReadOnlyList<ShipVertex> Vertices { get; init; } = [];

    [JsonPropertyName("edges")]
    public IReadOnlyList<ShipEdge> Edges { get; init; } = [];

    [JsonPropertyName("faces")]
    public IReadOnlyList<ShipFace> Faces { get; init; } = [];
}
