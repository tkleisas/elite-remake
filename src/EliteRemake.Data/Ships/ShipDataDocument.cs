using System.Text.Json.Serialization;

namespace EliteRemake.Data.Ships;

/// <summary>The whole contents of data/ships.json.</summary>
public sealed class ShipDataDocument
{
    /// <summary>Schema version of the JSON; the loader rejects anything it does not understand.</summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("generator")]
    public string Generator { get; init; } = string.Empty;

    [JsonPropertyName("source")]
    public ShipDataSource Source { get; init; } = new();

    [JsonPropertyName("counts")]
    public ShipDataCounts Counts { get; init; } = new();

    /// <summary>The XX21 lookup tables: D.MOA ... D.MOP and the docked table.</summary>
    [JsonPropertyName("shipSets")]
    public IReadOnlyList<ShipSet> ShipSets { get; init; } = [];

    /// <summary>The ship blueprints, ordered by their lowest registered ship type.</summary>
    [JsonPropertyName("ships")]
    public IReadOnlyList<ShipBlueprint> Ships { get; init; } = [];

    /// <summary>Extraction notes (unusual layouts, unregistered ship types, and so on).</summary>
    [JsonPropertyName("notes")]
    public IReadOnlyList<string> Notes { get; init; } = [];
}
