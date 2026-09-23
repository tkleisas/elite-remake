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

    /// <summary>The mission hints, which replace a system's description while a mission is on.</summary>
    public static Core.Sim.MissionHints Hints => _hints.Value;

    /// <summary>
    /// Prints a mission hint by its token number. The hints live in the original's second token
    /// table, so tokens in the hint range come from there and anything they refer to comes from the
    /// main table.
    /// </summary>
    public static string Hint(int token, string systemName, EliteRandom random)
    {
        Document document = LazyDocument.Value;
        var printer = new TokenPrinter(_hintTokens.Value, document.Mtin, random)
        {
            FallbackTokens = _tokens.Value,
        };

        // The original wraps a hint in token 176 and token 177, which set the case and end the line
        return printer.Print(token, systemName);
    }

    private static readonly Lazy<Core.Sim.MissionHints> _hints = new(() =>
        new Core.Sim.MissionHints(
            LazyDocument.Value.Hints.Select(h => new Core.Sim.MissionHint(h.System, h.Criteria)).ToArray()));

    private static readonly Lazy<Dictionary<int, TokenElement[]>> _hintTokens = new(() =>
        LazyDocument.Value.HintTokens.ToDictionary(pair => int.Parse(pair.Key), pair => pair.Value));

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

        [JsonPropertyName("hintTokens")]
        public Dictionary<string, TokenElement[]> RawHintTokens { get; init; } = [];

        [JsonPropertyName("hints")]
        public HintEntry[] RawHints { get; init; } = [];

        /// <summary>The hints, as the original's tables give them.</summary>
        [JsonIgnore]
        public HintEntry[] Hints => RawHints;

        /// <summary>The hint tokens, keyed by number.</summary>
        [JsonIgnore]
        public Dictionary<string, TokenElement[]> HintTokens => RawHintTokens;

        /// <summary>One entry of the RUPLA and RUGAL tables.</summary>
        public sealed class HintEntry
        {
            [JsonPropertyName("system")]
            public int System { get; init; }

            [JsonPropertyName("criteria")]
            public int Criteria { get; init; }
        }

        /// <summary>The tokens, keyed by number rather than by string.</summary>
        [JsonIgnore]
        public Dictionary<int, TokenElement[]> Tokens =>
            RawTokens.ToDictionary(pair => int.Parse(pair.Key), pair => pair.Value);
    }
}
