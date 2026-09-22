using System.Text.Json.Serialization;

namespace EliteDataExtractor.Extraction;

/// <summary>The structure of the emitted data/ships.json document.</summary>
internal sealed class ShipDataDocument
{
    public int SchemaVersion { get; init; } = 1;

    public string Generator { get; init; } = "EliteDataExtractor";

    public ShipDataSource Source { get; init; } = new();

    public ShipDataCounts Counts { get; init; } = new();

    public List<ShipSet> ShipSets { get; init; } = [];

    public List<ShipDocument> Ships { get; init; } = [];

    public List<string> Notes { get; init; } = [];
}

internal sealed class ShipDataSource
{
    public string Library { get; init; } = "elite-source-code-library";

    public string Version { get; init; } = "BBC Micro disc (_VERSION=2)";

    public string Variant { get; init; } = "Stairway to Hell (_VARIANT=2)";

    public int BuildVersion { get; init; } = 2;

    public int BuildVariant { get; init; } = 2;

    public SortedDictionary<string, bool> Flags { get; init; } = [];
}

internal sealed class ShipDataCounts
{
    public int Ships { get; init; }

    /// <summary>Distinct ship type numbers that are registered in at least one XX21 table.</summary>
    public int RegisteredTypes { get; init; }

    public int ShipSets { get; init; }

    public int ShipSetEntries { get; init; }

    public int Vertices { get; init; }

    public int Edges { get; init; }

    public int Faces { get; init; }
}

/// <summary>One XX21 lookup table: a ship blueprint file (D.MOA-D.MOP) or the docked table.</summary>
internal sealed class ShipSet
{
    public string Id { get; init; } = string.Empty;

    public string AsmFile { get; init; } = string.Empty;

    /// <summary>Reference binary used for verification, or null when the set is not verified.</summary>
    public string? Binary { get; init; }

    /// <summary>Base load address of the binary (the XX21 table pointer base).</summary>
    public int BaseAddress { get; init; }

    /// <summary>Number of 16-bit entries in the XX21 table.</summary>
    public int SlotCount { get; init; }

    /// <summary>All slots, including empty ones, in type order (type 1 first).</summary>
    [JsonIgnore]
    public List<ShipSetSlot> Slots { get; init; } = [];

    /// <summary>Only the populated slots, as emitted to JSON.</summary>
    public List<ShipSetSlot> Entries => [.. Slots.Where(slot => slot.Label is not null)];

    [JsonIgnore]
    public byte[]? BinaryData { get; set; }
}

internal sealed class ShipSetSlot
{
    /// <summary>Ship type number (1-31).</summary>
    public int Type { get; init; }

    /// <summary>Four-character blueprint symbol from the XX21 comment, or null.</summary>
    public string? Symbol { get; init; }

    /// <summary>Ship name from the XX21 comment, or null.</summary>
    public string? TypeName { get; init; }

    /// <summary>Assembler label of the blueprint, or null for an empty slot.</summary>
    public string? Label { get; init; }

    /// <summary>Ship id in the top-level ships array, or null for an empty slot.</summary>
    public string? Ship { get; init; }

    [JsonIgnore]
    public long Pointer { get; init; }

    [JsonIgnore]
    public ShipBlueprint? Blueprint { get; init; }
}

internal sealed class ShipHeader
{
    /// <summary>Low nibble of blueprint byte #0: maximum number of canisters on demise.</summary>
    public int MaxCanisters { get; init; }

    /// <summary>
    /// High nibble of blueprint byte #0 plus one: the market item number awarded when the ship is
    /// scooped, or 0 if the ship is not scooped as a market item.
    /// </summary>
    public int ScoopMarketItem { get; init; }

    /// <summary>Raw blueprint byte #0 (maxCanisters | (scoopMarketItem - 1) &lt;&lt; 4).</summary>
    public int CanisterByte { get; init; }

    /// <summary>Blueprints bytes #1-2: targetable area (a square, so 160 * 160 for the Coriolis).</summary>
    public int TargetableArea { get; init; }

    /// <summary>Signed 16-bit offset from the blueprint start to the edge data.</summary>
    public int EdgesOffset { get; init; }

    /// <summary>Signed 16-bit offset from the blueprint start to the face data.</summary>
    public int FacesOffset { get; init; }

    /// <summary>Blueprint byte #5: 1 + 4 * the maximum number of visible edges.</summary>
    public int MaxEdges { get; init; }

    /// <summary>Blueprint byte #6: the gun vertex * 4.</summary>
    public int GunVertex { get; init; }

    /// <summary>Blueprint byte #7: 6 + 4 * the number of explosion nodes.</summary>
    public int ExplosionCount { get; init; }

    public int VertexCount { get; init; }

    public int EdgeCount { get; init; }

    public int FaceCount { get; init; }

    public int Bounty { get; init; }

    public int VisibilityDistance { get; init; }

    public int MaxEnergy { get; init; }

    public int MaxSpeed { get; init; }

    /// <summary>Blueprint byte #18: face normals are scaled by 2^NormalScale.</summary>
    public int NormalScale { get; init; }

    /// <summary>Bits 3-7 of blueprint byte #19.</summary>
    public int LaserPower { get; init; }

    /// <summary>Bits 0-2 of blueprint byte #19.</summary>
    public int Missiles { get; init; }

    /// <summary>Raw blueprint byte #19 ((laserPower &lt;&lt; 3) | missiles).</summary>
    public int LaserMissileByte { get; init; }
}

internal sealed class ShipVertex
{
    public int X { get; init; }

    public int Y { get; init; }

    public int Z { get; init; }

    /// <summary>
    /// The four face numbers stored for this vertex, in order. The original stores all four verbatim
    /// (duplicates and zeros included), so consumers must treat each entry as a face index.
    /// </summary>
    public List<int> Faces { get; init; } = [];

    public int Visibility { get; init; }
}

internal sealed class ShipEdge
{
    public int Vertex1 { get; init; }

    public int Vertex2 { get; init; }

    /// <summary>The two face numbers stored for this edge.</summary>
    public List<int> Faces { get; init; } = [];

    public int Visibility { get; init; }
}

internal sealed class ShipFace
{
    public int X { get; init; }

    public int Y { get; init; }

    public int Z { get; init; }

    public int Visibility { get; init; }
}

/// <summary>A decoded ship blueprint.</summary>
internal sealed class ShipBlueprint
{
    public required string Label { get; init; }

    /// <summary>Absolute address the blueprint was decoded from (assembled image address).</summary>
    [JsonIgnore]
    public int Address { get; init; }

    public required ShipHeader Header { get; init; }

    public List<ShipVertex> Vertices { get; init; } = [];

    public List<ShipEdge> Edges { get; init; } = [];

    public List<ShipFace> Faces { get; init; } = [];
}

/// <summary>A canonical ship entry in the top-level ships array.</summary>
internal sealed class ShipDocument
{
    public string Id { get; init; } = string.Empty;

    /// <summary>Friendly name, derived from the blueprint's Summary comment.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Raw Summary comment from the ship's source file.</summary>
    public string Summary { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public string AsmFile { get; init; } = string.Empty;

    /// <summary>Reference binary that the blueprint was verified against.</summary>
    public string Binary { get; init; } = string.Empty;

    /// <summary>Ship type numbers this blueprint is registered under, in ascending order.</summary>
    public List<int> Types { get; init; } = [];

    /// <summary>Four-character blueprint symbols for those registrations, in ascending type order.</summary>
    public List<string> Symbols { get; init; } = [];

    /// <summary>Ship names used by the XX21 tables for those registrations.</summary>
    public List<string> TypeNames { get; init; } = [];

    /// <summary>Ids of the ship sets (D.MOA ... D.MOP, docked) that register this ship.</summary>
    public List<string> ShipSets { get; init; } = [];

    public ShipHeader Header { get; init; } = new();

    public List<ShipVertex> Vertices { get; init; } = [];

    public List<ShipEdge> Edges { get; init; } = [];

    public List<ShipFace> Faces { get; init; } = [];

    /// <summary>Extraction notes for this ship, omitted from JSON when empty.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<string>? Notes { get; init; }
}

/// <summary>Everything the extractor produces, including the data needed by the verifier.</summary>
internal sealed class ExtractionResult
{
    public required ShipDataDocument Document { get; init; }

    /// <summary>Ship sets keyed by id, including the per-set assembled images.</summary>
    public required List<ShipSet> ShipSets { get; init; }

    /// <summary>Canonical blueprints keyed by assembler label.</summary>
    public required Dictionary<string, ShipBlueprint> BlueprintsByLabel { get; init; }

    /// <summary>Blueprints as decoded per ship set, keyed by (set id, label).</summary>
    public required Dictionary<(string SetId, string Label), ShipBlueprint> BlueprintsBySet { get; init; }

    public required List<string> Notes { get; init; }
}
