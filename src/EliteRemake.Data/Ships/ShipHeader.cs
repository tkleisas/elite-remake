using System.Text.Json.Serialization;

namespace EliteRemake.Data.Ships;

/// <summary>
/// The 20-byte header of an original Elite ship blueprint, with the packed bytes already decoded.
/// </summary>
public sealed class ShipHeader
{
    /// <summary>Low nibble of blueprint byte #0: the maximum number of canisters dropped on demise.</summary>
    [JsonPropertyName("maxCanisters")]
    public int MaxCanisters { get; init; }

    /// <summary>
    /// High nibble of blueprint byte #0 plus one: the market item number awarded when the ship is
    /// scooped, or 0 when the ship is not scooped as a market item. (Enhanced versions only.)
    /// </summary>
    [JsonPropertyName("scoopMarketItem")]
    public int ScoopMarketItem { get; init; }

    /// <summary>Raw blueprint byte #0.</summary>
    [JsonPropertyName("canisterByte")]
    public int CanisterByte { get; init; }

    /// <summary>Blueprint bytes #1-2: the targetable area (a square, e.g. 160 * 160 for the Coriolis).</summary>
    [JsonPropertyName("targetableArea")]
    public int TargetableArea { get; init; }

    /// <summary>
    /// Signed 16-bit offset from the blueprint start to the edge data, exactly as stored. This is a
    /// property of the file layout, not of the ship: negative values mean the edges belong to an
    /// earlier blueprint (the Thargon reuses the cargo canister's edges).
    /// </summary>
    [JsonPropertyName("edgesOffset")]
    public int EdgesOffset { get; init; }

    /// <summary>Signed 16-bit offset from the blueprint start to the face data, exactly as stored.</summary>
    [JsonPropertyName("facesOffset")]
    public int FacesOffset { get; init; }

    /// <summary>Blueprint byte #5: 1 + 4 * the maximum number of visible edges.</summary>
    [JsonPropertyName("maxEdges")]
    public int MaxEdges { get; init; }

    /// <summary>Blueprint byte #6: the gun vertex * 4.</summary>
    [JsonPropertyName("gunVertex")]
    public int GunVertex { get; init; }

    /// <summary>Blueprint byte #7: 6 + 4 * the number of explosion nodes.</summary>
    [JsonPropertyName("explosionCount")]
    public int ExplosionCount { get; init; }

    /// <summary>Number of vertices (blueprint byte #8 / 6).</summary>
    [JsonPropertyName("vertexCount")]
    public int VertexCount { get; init; }

    /// <summary>Number of edges (blueprint byte #9).</summary>
    [JsonPropertyName("edgeCount")]
    public int EdgeCount { get; init; }

    /// <summary>Number of faces (blueprint byte #12 / 4).</summary>
    [JsonPropertyName("faceCount")]
    public int FaceCount { get; init; }

    /// <summary>Bounty in tenths of a credit (blueprint bytes #10-11).</summary>
    [JsonPropertyName("bounty")]
    public int Bounty { get; init; }

    /// <summary>Blueprint byte #13: the distance at which the ship stops being drawn.</summary>
    [JsonPropertyName("visibilityDistance")]
    public int VisibilityDistance { get; init; }

    /// <summary>Blueprint byte #14: maximum energy.</summary>
    [JsonPropertyName("maxEnergy")]
    public int MaxEnergy { get; init; }

    /// <summary>Blueprint byte #15: maximum speed.</summary>
    [JsonPropertyName("maxSpeed")]
    public int MaxSpeed { get; init; }

    /// <summary>Blueprint byte #18: the face normals are scaled by 2^NormalScale.</summary>
    [JsonPropertyName("normalScale")]
    public int NormalScale { get; init; }

    /// <summary>Bits 3-7 of blueprint byte #19.</summary>
    [JsonPropertyName("laserPower")]
    public int LaserPower { get; init; }

    /// <summary>Bits 0-2 of blueprint byte #19.</summary>
    [JsonPropertyName("missiles")]
    public int Missiles { get; init; }

    /// <summary>Raw blueprint byte #19: (LaserPower &lt;&lt; 3) | Missiles.</summary>
    [JsonPropertyName("laserMissileByte")]
    public int LaserMissileByte { get; init; }

    /// <summary>The gun vertex index (blueprint byte #6 / 4), or -1 when the ship has no gun vertex.</summary>
    [JsonIgnore]
    public int GunVertexIndex => GunVertex / 4;

    /// <summary>The number of explosion nodes ((byte #7 - 6) / 4).</summary>
    [JsonIgnore]
    public int ExplosionNodes => (ExplosionCount - 6) / 4;

    /// <summary>The maximum number of visible edges ((byte #5 - 1) / 4).</summary>
    [JsonIgnore]
    public int MaxVisibleEdges => (MaxEdges - 1) / 4;

    /// <summary>The face normal scale as a multiplier (2^NormalScale).</summary>
    [JsonIgnore]
    public int NormalScaleFactor => 1 << NormalScale;
}
