using System.Text.Json.Serialization;

namespace EliteRemake.Data.Ships;

/// <summary>The whole contents of data/ships.json.</summary>
/// <summary>One ship type's default NEWB flags from the disc's E% table.</summary>
public sealed class NewbFlagEntry
{
    /// <summary>The ship type number the entry is indexed by.</summary>
    [JsonPropertyName("type")]
    public int Type { get; init; }

    /// <summary>The flags byte.</summary>
    [JsonPropertyName("flags")]
    public int Flags { get; init; }

    /// <summary>The flags as eight binary digits, which is how the original's comments show them.</summary>
    [JsonPropertyName("bits")]
    public string Bits { get; init; } = string.Empty;

    /// <summary>True when this type carries the cop flag, so killing one makes us a fugitive.</summary>
    [JsonPropertyName("cop")]
    public bool Cop { get; init; }
}

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

    /// <summary>
    /// The disc's <c>E%</c> table: the default NEWB flags for each ship type, read from the assembled
    /// docked code. The flight loop tests these to decide what killing a ship does to our record.
    /// </summary>
    [JsonPropertyName("newbFlags")]
    public IReadOnlyList<NewbFlagEntry> NewbFlags { get; init; } = [];

    /// <summary>The ship blueprints, ordered by their lowest registered ship type.</summary>
    [JsonPropertyName("ships")]
    public IReadOnlyList<ShipBlueprint> Ships { get; init; } = [];

    /// <summary>Extraction notes (unusual layouts, unregistered ship types, and so on).</summary>
    [JsonPropertyName("notes")]
    public IReadOnlyList<string> Notes { get; init; } = [];
}
