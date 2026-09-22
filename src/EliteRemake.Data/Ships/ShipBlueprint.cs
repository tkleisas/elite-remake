using System.Text.Json.Serialization;

namespace EliteRemake.Data.Ships;

/// <summary>
/// One extracted ship blueprint: the geometry plus the ship type numbers it is registered under in
/// the original XX21 lookup tables.
/// </summary>
public sealed class ShipBlueprint : ShipGeometry
{
    /// <summary>Stable identifier derived from the original assembler label, e.g. "cobra-mk-3".</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>Friendly name, e.g. "Cobra Mk III".</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>The Summary comment from the original source, e.g. "Ship blueprint for a Cobra Mk III".</summary>
    [JsonPropertyName("summary")]
    public string Summary { get; init; } = string.Empty;

    /// <summary>The original assembler label, e.g. "SHIP_COBRA_MK_3".</summary>
    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    /// <summary>Path of the original source file, relative to the source library root.</summary>
    [JsonPropertyName("asmFile")]
    public string AsmFile { get; init; } = string.Empty;

    /// <summary>Reference binary the blueprint was verified against.</summary>
    [JsonPropertyName("binary")]
    public string Binary { get; init; } = string.Empty;

    /// <summary>Ship type numbers this blueprint is registered under, in ascending order.</summary>
    [JsonPropertyName("types")]
    public IReadOnlyList<int> Types { get; init; } = [];

    /// <summary>Four-character blueprint symbols for those registrations (e.g. "CYL" for type 11).</summary>
    [JsonPropertyName("symbols")]
    public IReadOnlyList<string> Symbols { get; init; } = [];

    /// <summary>Ship names used by the XX21 tables for those registrations.</summary>
    [JsonPropertyName("typeNames")]
    public IReadOnlyList<string> TypeNames { get; init; } = [];

    /// <summary>Ids of the ship sets (D.MOA ... D.MOP, docked) that register this ship.</summary>
    [JsonPropertyName("shipSets")]
    public IReadOnlyList<string> ShipSets { get; init; } = [];

    /// <summary>Label the edge data was read from, or null when it matches no label.</summary>
    [JsonPropertyName("edgesFrom")]
    public string? EdgesFrom { get; init; }

    /// <summary>Label the face data was read from, or null when it matches no label.</summary>
    [JsonPropertyName("facesFrom")]
    public string? FacesFrom { get; init; }

    /// <summary>
    /// The ship's own source-declared FACE data, present only when the header's face offset does not
    /// point at it. This happens for the splinter, whose face offset in the original disc version is
    /// 24 bytes past its own face data, so <see cref="ShipGeometry.Faces"/> holds whatever the
    /// original game actually reads. A remake that wants a correctly shaded splinter should use this
    /// data instead.
    /// </summary>
    [JsonPropertyName("declaredFaces")]
    public IReadOnlyList<ShipFace>? DeclaredFaces { get; init; }

    /// <summary>
    /// The blueprint as assembled inside T.CODE for the docked ship hangar and the mission 1
    /// briefing, present only when it differs from the flight blueprint. The hangar copies of the
    /// Shuttle, Transporter, Python, Krait and Constrictor differ from their flight versions.
    /// </summary>
    [JsonPropertyName("dockedVariant")]
    public ShipGeometry? DockedVariant { get; init; }

    /// <summary>Extraction notes about anything unusual, or null.</summary>
    [JsonPropertyName("notes")]
    public IReadOnlyList<string>? Notes { get; init; }

    /// <summary>The first registered ship type, or 0 when the ship is not registered.</summary>
    [JsonIgnore]
    public int PrimaryType => Types.Count > 0 ? Types[0] : 0;

    /// <summary>True when this blueprint is registered under the given ship type.</summary>
    public bool IsRegisteredAs(int shipType) => Types.Contains(shipType);
}
