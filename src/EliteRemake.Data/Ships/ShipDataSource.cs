using System.Text.Json.Serialization;

namespace EliteRemake.Data.Ships;

/// <summary>Where the ship data came from: the original 6502 source library and the build options used.</summary>
public sealed class ShipDataSource
{
    [JsonPropertyName("library")]
    public string Library { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("variant")]
    public string Variant { get; init; } = string.Empty;

    [JsonPropertyName("buildVersion")]
    public int BuildVersion { get; init; }

    [JsonPropertyName("buildVariant")]
    public int BuildVariant { get; init; }

    /// <summary>The BeebAsm build flags, e.g. "_DISC_FLIGHT" = true.</summary>
    [JsonPropertyName("flags")]
    public IReadOnlyDictionary<string, bool> Flags { get; init; } = new Dictionary<string, bool>();
}

/// <summary>Summary counts for the extracted data.</summary>
public sealed class ShipDataCounts
{
    [JsonPropertyName("ships")]
    public int Ships { get; init; }

    /// <summary>Distinct ship type numbers registered in at least one XX21 table (30 of the 31 slots).</summary>
    [JsonPropertyName("registeredTypes")]
    public int RegisteredTypes { get; init; }

    [JsonPropertyName("shipSets")]
    public int ShipSets { get; init; }

    /// <summary>Total number of populated XX21 slots across all ship sets.</summary>
    [JsonPropertyName("shipSetEntries")]
    public int ShipSetEntries { get; init; }

    [JsonPropertyName("vertices")]
    public int Vertices { get; init; }

    [JsonPropertyName("edges")]
    public int Edges { get; init; }

    [JsonPropertyName("faces")]
    public int Faces { get; init; }
}
