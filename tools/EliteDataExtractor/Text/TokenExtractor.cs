using System.Text;
using System.Text.Json;

namespace EliteDataExtractor.Text;

/// <summary>
/// Extracts the extended text tokens that make up a system's description.
/// </summary>
/// <remarks>
/// The original builds a system's description from extended token 5, which is:
///
/// <code>
///     [176] {random 18} [202] {random 19} [177]
/// </code>
///
/// Token 176 applies the case, random token 18 picks one of five opening phrases, token 202 prints
/// " IS ", random token 19 picks one of five descriptions, and token 177 finishes the sentence.
/// Each random token chooses from a run of five tokens given by the original's MTIN table, so the
/// description is assembled from the real tables rather than being a fixed string per system.
///
/// This extractor walks the references from token 5, following recursive tokens and the MTIN runs,
/// and writes the whole reachable set to JSON for the game to print.
/// </remarks>
public static class TokenExtractor
{
    /// <summary>The extended token that holds the description.</summary>
    public const int DescriptionToken = 5;

    /// <summary>Extracts the description tokens from the source library.</summary>
    /// <param name="sourceRoot">The root of the source library.</param>
    /// <returns>The JSON document as a string.</returns>
    public static string Extract(string sourceRoot)
    {
        string variablePath = Path.Combine(sourceRoot, "library", "enhanced", "main", "variable");
        Dictionary<int, List<Element>> tokens = ParseTokens(Path.Combine(variablePath, "tkn1.asm"));
        int[] mtin = ParseMtin(Path.Combine(variablePath, "mtin.asm"));

        // Walk everything reachable from the description token
        var reachable = new SortedSet<int>();
        var pending = new Stack<int>();
        pending.Push(DescriptionToken);

        while (pending.Count > 0)
        {
            int token = pending.Pop();
            if (!reachable.Add(token) || !tokens.TryGetValue(token, out List<Element>? elements))
            {
                continue;
            }

            foreach (Element element in elements)
            {
                if (element.Kind is "ETOK" or "EREC")
                {
                    pending.Push(element.Value);
                }
                else if (element.Kind == "ERND")
                {
                    for (int i = 0; i < 5; i++)
                    {
                        pending.Push(mtin[element.Value] + i);
                    }
                }
            }
        }

        var document = new
        {
            schemaVersion = 1,
            generator = "EliteDataExtractor tokens",
            descriptionToken = DescriptionToken,
            mtin,
            tokens = reachable.ToDictionary(
                token => token.ToString(),
                token => tokens[token].Select(e => new { kind = e.Kind, value = e.Value, text = e.Text }).ToArray()),
        };

        return JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>One element of a token: a directive, a character or a token reference.</summary>
    private readonly record struct Element(string Kind, int Value, string? Text);

    private static Dictionary<int, List<Element>> ParseTokens(string path)
    {
        var tokens = new Dictionary<int, List<Element>>();
        List<Element>? current = null;

        foreach (string raw in File.ReadLines(path))
        {
            // The token number is given in a trailing comment on the first line of each token.
            // Only start a new token when we are not already inside one: comments inside a token
            // often mention other token numbers, and following those would split the token apart.
            int marker = current is null ? raw.IndexOf("Token ", StringComparison.Ordinal) : -1;
            if (marker >= 0)
            {
                int start = marker + "Token ".Length;
                int end = raw.IndexOf(':', start);
                if (end > start && int.TryParse(raw[start..end], out int number))
                {
                    current = [];
                    tokens[number] = current;
                }
            }

            int comment = raw.IndexOf('\\');
            string code = (comment >= 0 ? raw[..comment] : raw).Trim();
            if (code.Length == 0 || current is null)
            {
                continue;
            }

            if (code.StartsWith("EQUB VE", StringComparison.Ordinal))
            {
                current = null;
                continue;
            }

            Element? element = ParseElement(code);
            if (element is not null)
            {
                current.Add(element.Value);
            }
        }

        return tokens;
    }

    private static Element? ParseElement(string code)
    {
        string[] parts = code.Split(' ', 2, StringSplitOptions.TrimEntries);
        string kind = parts[0];
        string argument = parts.Length > 1 ? parts[1] : string.Empty;

        switch (kind)
        {
            case "ECHR":
                return new Element(kind, 0, Unquote(argument));

            case "ETWO":
            {
                string[] pair = argument.Split(',', StringSplitOptions.TrimEntries);
                return new Element(kind, 0, pair.Length == 2 ? Unquote(pair[0]) + Unquote(pair[1]) : argument);
            }

            case "EJMP":
            case "ETOK":
            case "EREC":
            case "ERND":
                return int.TryParse(argument.TrimStart('0'), out int value) || int.TryParse(argument, out value)
                    ? new Element(kind, value, null)
                    : null;

            default:
                return null;
        }
    }

    private static string Unquote(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length >= 3 && trimmed[0] == '\'')
        {
            // A backtick stands in for an apostrophe in the source
            char character = trimmed[1] == '`' ? '\'' : trimmed[1];
            return character.ToString();
        }

        return trimmed.Trim('\'');
    }

    /// <summary>Parses the MTIN table: the start of the five-token run for each random token.</summary>
    private static int[] ParseMtin(string path)
    {
        var values = new List<int>();
        foreach (string raw in File.ReadLines(path))
        {
            int comment = raw.IndexOf('\\');
            string code = (comment >= 0 ? raw[..comment] : raw).Trim();
            if (!code.StartsWith("EQUB ", StringComparison.Ordinal))
            {
                continue;
            }

            string value = code["EQUB ".Length..].Trim();
            if (int.TryParse(value, out int number))
            {
                values.Add(number);
            }
        }

        return values.ToArray();
    }
}
