using System.Text.Json.Serialization;

namespace EliteRemake.Data.Ships;

/// <summary>One populated slot of an XX21 ship blueprint lookup table.</summary>
public sealed class ShipSetEntry
{
    /// <summary>Ship type number (1-31).</summary>
    [JsonPropertyName("type")]
    public int Type { get; init; }

    /// <summary>Four-character blueprint symbol from the original XX21 comment, or null.</summary>
    [JsonPropertyName("symbol")]
    public string? Symbol { get; init; }

    /// <summary>Ship name used by the original XX21 comment, or null.</summary>
    [JsonPropertyName("typeName")]
    public string? TypeName { get; init; }

    /// <summary>Original assembler label of the blueprint, or null for an empty slot.</summary>
    [JsonPropertyName("label")]
    public string? Label { get; init; }

    /// <summary>Id of the corresponding entry in <see cref="ShipData.All"/>, or null.</summary>
    [JsonPropertyName("ship")]
    public string? Ship { get; init; }
}

/// <summary>
/// One XX21 ship blueprint lookup table: one of the sixteen flight ship files (D.MOA-D.MOP) or the
/// docked table used by the ship hangar and the mission 1 briefing.
/// </summary>
public sealed class ShipSet
{
    /// <summary>Ship set id: "D.MOA" ... "D.MOP", or "docked".</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>Original source file, relative to the source library root.</summary>
    [JsonPropertyName("asmFile")]
    public string AsmFile { get; init; } = string.Empty;

    /// <summary>Reference binary the set was verified against.</summary>
    [JsonPropertyName("binary")]
    public string? Binary { get; init; }

    /// <summary>Base load address of the XX21 table (0x5600).</summary>
    [JsonPropertyName("baseAddress")]
    public int BaseAddress { get; init; }

    /// <summary>Number of 16-bit entries in the table (31 in the disc version).</summary>
    [JsonPropertyName("slotCount")]
    public int SlotCount { get; init; }

    /// <summary>The populated slots, in type order.</summary>
    [JsonPropertyName("entries")]
    public IReadOnlyList<ShipSetEntry> Entries { get; init; } = [];

    /// <summary>Finds the entry registered at the given ship type, or null.</summary>
    public ShipSetEntry? Find(int shipType)
    {
        foreach (ShipSetEntry entry in Entries)
        {
            if (entry.Type == shipType)
            {
                return entry;
            }
        }

        return null;
    }
}
