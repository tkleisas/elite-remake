using System.Text.Json;
using System.Text.Json.Serialization;
using EliteDataExtractor.Extraction;

namespace EliteDataExtractor;

/// <summary>Writes data/ships.json. Output is deterministic: fixed property order, no timestamps.</summary>
internal static class ShipJsonWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NewLine = "\n",
    };

    public static string Serialize(ShipDataDocument document) => JsonSerializer.Serialize(document, Options);

    public static void Write(ShipDataDocument document, string path)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, Serialize(document) + "\n");
    }
}
