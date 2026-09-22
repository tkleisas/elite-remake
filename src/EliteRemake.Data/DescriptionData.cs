using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using EliteRemake.Core.Sim;
using EliteRemake.Core.Text;

namespace EliteRemake.Data;

/// <summary>
/// The extended text tokens that make up a system's description, extracted from the original's
/// token table.
/// </summary>
public static class DescriptionData
{
    /// <summary>The logical name of the embedded token resource.</summary>
    public const string ResourceName = "EliteRemake.Data.data.descriptions.json";

    private static readonly Lazy<Document> LazyDocument = new(Load);

    /// <summary>The description token's number in the original's table.</summary>
    public static int DescriptionToken => LazyDocument.Value.DescriptionToken;

    /// <summary>
    /// Generates a description of a system, using the given generator for the random choices so the
    /// caller decides how stable the description is.
    /// </summary>
    public static string Describe(string systemName, EliteRandom random)
    {
        Document document = LazyDocument.Value;
        var printer = new TokenPrinter(_tokens.Value, document.Mtin, random);
        return printer.Print(document.DescriptionToken, systemName);
    }

    private static readonly Lazy<Dictionary<int, TokenElement[]>> _tokens = new(() => LazyDocument.Value.Tokens);

    private static Document Load()
    {
        Assembly assembly = typeof(DescriptionData).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"The token resource {ResourceName} is missing. Run: dotnet run --project tools/EliteDataExtractor -- tokens");
        }

        using var reader = new StreamReader(stream);
        Document? document = JsonSerializer.Deserialize<Document>(
            reader.ReadToEnd(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return document ?? throw new InvalidDataException("The token resource is empty.");
    }

    private sealed class Document
    {
        [JsonPropertyName("descriptionToken")]
        public int DescriptionToken { get; init; }

        [JsonPropertyName("mtin")]
        public int[] Mtin { get; init; } = [];

        [JsonPropertyName("tokens")]
        public Dictionary<string, TokenElement[]> RawTokens { get; init; } = [];

        /// <summary>The tokens, keyed by number rather than by string.</summary>
        [JsonIgnore]
        public Dictionary<int, TokenElement[]> Tokens =>
            RawTokens.ToDictionary(pair => int.Parse(pair.Key), pair => pair.Value);
    }
}
