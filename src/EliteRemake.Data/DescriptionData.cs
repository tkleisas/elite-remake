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
    public static string Describe(string systemName, EliteRandom random) =>
        Describe(systemName, random, Commander.DefaultName, galaxy: 0);

    /// <summary>
    /// Generates a description of a system in the given galaxy, which the tokens can refer to for the
    /// commander's name and the system's adjective.
    /// </summary>
    public static string Describe(string systemName, EliteRandom random, string commanderName, int galaxy)
    {
        Document document = LazyDocument.Value;
        return Printer(random, commanderName, galaxy).Print(document.DescriptionToken, systemName);
    }

    /// <summary>
    /// Prints one of the mission texts: the briefings that token 10, 11 and 222 hold and the
    /// debriefings that tokens 15 and 223 hold.
    /// </summary>
    /// <remarks>
    /// The mission texts need more than the description does. They name the commander and the
    /// captain delivering the briefing, and the captain's name and the location hint are chosen by
    /// the galaxy we are in — jump tokens 27 and 28 pick tokens 217-219 and 220-221 by number.
    /// </remarks>
    public static string MissionText(
        int token,
        string systemName,
        string commanderName,
        int galaxy,
        EliteRandom random) =>
        Printer(random, commanderName, galaxy).Print(token, systemName);

    /// <summary>
    /// Prints a mission text and reports the screen actions it asks for: the original's briefing
    /// routines clear the screen for the INCOMING MESSAGE banner, show the ship, and wait for a key
    /// press, and those are things the display has to stage rather than words to print.
    /// </summary>
    public static (string Text, IReadOnlyList<TokenEvent> Events) MissionTextWithEvents(
        int token,
        string systemName,
        string commanderName,
        int galaxy,
        EliteRandom random)
    {
        TokenPrinter printer = Printer(random, commanderName, galaxy);
        return (printer.Print(token, systemName), printer.Events);
    }

    /// <summary>Builds a printer with everything the extracted tables hold.</summary>
    private static TokenPrinter Printer(EliteRandom random, string commanderName, int galaxy)
    {
        Document document = LazyDocument.Value;
        return new TokenPrinter(_tokens.Value, document.Mtin, random)
        {
            StandardTokens = _standardTokens.Value,
            TwoLetterTokens = document.TwoLetterTokens,
            StandardTwoLetterTokens = document.StandardTwoLetterTokens,
            CommanderName = commanderName,
            Galaxy = galaxy,
        };
    }

    /// <summary>
    /// Prints a main-table extended token by number, which is what the original's DETOK does.
    /// </summary>
    /// <remarks>
    /// Exposed so the token tables can be checked directly rather than only through the description
    /// and hint entry points: a token that only ever appears as part of a larger phrase is otherwise
    /// hard to look at on its own, and the five that a hint's random element picks from are exactly
    /// that.
    /// </remarks>
    public static string PrintToken(int token, string systemName, EliteRandom random)
    {
        Document document = LazyDocument.Value;
        var printer = new TokenPrinter(_tokens.Value, document.Mtin, random);
        return printer.Print(token, systemName);
    }

    /// <summary>
    /// The mission texts' token numbers, as the original's briefing and debriefing routines use them.
    /// </summary>
    public static class MissionTokens
    {
        /// <summary>Mission 1's briefing, which the Navy gives us when we dock with 256 kills.</summary>
        public const int MissionOneBriefing = 10;

        /// <summary>Mission 2's first contact, when the Navy asks us to go to Ceerdi.</summary>
        public const int MissionTwoContact = 11;

        /// <summary>Mission 1's debriefing, after the Constrictor is destroyed.</summary>
        public const int MissionOneDebriefing = 15;

        /// <summary>Mission 2's briefing, which Agent Blake gives us at Ceerdi.</summary>
        public const int MissionTwoBriefing = 222;

        /// <summary>Mission 2's debriefing, after the plans are delivered.</summary>
        public const int MissionTwoDebriefing = 223;
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

    private static readonly Lazy<Dictionary<int, TokenElement[]>> _standardTokens =
        new(() => LazyDocument.Value.StandardTokens);

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

        [JsonPropertyName("standardTokens")]
        public Dictionary<string, TokenElement[]> RawStandardTokens { get; init; } = [];

        [JsonPropertyName("twoLetterTokens")]
        public string[] TwoLetterTokens { get; init; } = [];

        [JsonPropertyName("standardTwoLetterTokens")]
        public string[] StandardTwoLetterTokens { get; init; } = [];

        /// <summary>The standard token table, keyed by number rather than by string.</summary>
        [JsonIgnore]
        public Dictionary<int, TokenElement[]> StandardTokens =>
            RawStandardTokens.ToDictionary(pair => int.Parse(pair.Key), pair => pair.Value);

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
