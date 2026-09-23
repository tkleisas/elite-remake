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

    /// <summary>
    /// The mission texts, which are printed by the briefing and debriefing routines when we dock:
    /// token 10 is the briefing that starts mission 1, 11 is the Navy's first contact for mission 2,
    /// 222 is mission 2's briefing at Ceerdi, and 15 and 223 are the two debriefings.
    /// </summary>
    /// <remarks>
    /// They are in the same table as the descriptions, so they are extracted by the same walk — a
    /// token's references are what make it printable, and the briefing refers to a dozen others:
    /// "GREETINGS COMMANDER, I AM CAPTAIN {name} OF HER MAJESTY'S SPACE NAVY …" is assembled from
    /// token 10, its recursive tokens and the mission hint that names the system the Constrictor was
    /// last seen in.
    ///
    /// Tokens 217 to 221 are not referred to by any of them: jump token 27 prints the captain's name
    /// (token 217 plus the galaxy, so 217 to 219) and jump token 28 prints the location hint (token
    /// 220 plus the galaxy, so 220 or 221), both by number rather than by reference. Token 153 is
    /// the "IAN" that jump token 17 adds to a system's name to make its adjective, and is reached
    /// the same way.
    /// </remarks>
    public static readonly int[] MissionTokens = [10, 11, 15, 153, 217, 218, 219, 220, 221, 222, 223];

    /// <summary>
    /// The build flags the description tokens are read with: the disc version's docked code, which
    /// is the one that shows system descriptions.
    /// </summary>
    private static readonly HashSet<string> TrueFlags = new(StringComparer.Ordinal)
    {
        "_DISC_VERSION",
        "_DISC_DOCKED",
    };

    /// <summary>Extracts the description tokens from the source library.</summary>
    /// <param name="sourceRoot">The root of the source library.</param>
    /// <returns>The JSON document as a string.</returns>
    public static string Extract(string sourceRoot)
    {
        string variablePath = Path.Combine(sourceRoot, "library", "enhanced", "main", "variable");
        string commonPath = Path.Combine(sourceRoot, "library", "common", "main", "variable");
        Dictionary<int, List<Element>> tokens = ParseTokens(Path.Combine(variablePath, "tkn1.asm"));
        int[] mtin = ParseMtin(Path.Combine(variablePath, "mtin.asm"));

        // The extended table can reach into the standard one. Jump token 6 switches the printer to
        // the standard tokens and jump token 5 switches it back, which is how the briefings borrow
        // phrases like "MILITARY  LASER", "ENERGY UNIT" and "E.C.M.SYSTEM" from the docked text:
        // token 10 contains {6} MILITARY LASER {5} S, and the S finishes the word.
        Dictionary<int, List<Element>> standard = ParseStandardTokens(Path.Combine(commonPath, "qq18.asm"));

        // Jump token 18 prints one to four random two-letter tokens, and it draws them from a table
        // that runs straight on from the extended two-letter tokens into the standard ones
        string[] twoLetterTokens = ParseTwoLetterTokens(
            Path.Combine(variablePath, "tkn2.asm"),
            Path.Combine(commonPath, "qq16.asm"), skipFirst: true);
        string[] standardTwoLetterTokens = ParseTwoLetterTokens(
            Path.Combine(commonPath, "qq16.asm"),
            Path.Combine(commonPath, "qq16.asm"), skipFirst: false);

        // The mission hints live in a second token table, RUTOK, and are selected by the RUPLA and
        // RUGAL tables: RUPLA names the system by its number in the galaxy, RUGAL the galaxy and
        // whether the hint needs mission 1 to be in progress
        Dictionary<int, List<Element>> hints = ParseTokens(Path.Combine(variablePath, "rutok.asm"));
        int[] rupla = ParseMtin(Path.Combine(variablePath, "rupla.asm"));
        int[] rugal = ParseMtin(Path.Combine(variablePath, "rugal.asm"));

        // Walk everything reachable from the description token and from the mission texts
        var reachable = new SortedSet<int>();
        var pending = new Stack<int>();
        pending.Push(DescriptionToken);

        foreach (int missionToken in MissionTokens)
        {
            pending.Push(missionToken);
        }

        while (pending.Count > 0)
        {
            int token = pending.Pop();

            // A token already marked may still not have been visited, so the visit cannot be
            // conditional on the marking: only the marking is idempotent, and the references have
            // to be followed the first time the token is actually looked at
            bool firstVisit = reachable.Add(token);
            if (!tokens.TryGetValue(token, out List<Element>? elements) || !firstVisit)
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

        // The hints refer to tokens in the main table as well as their own, so walk those too
        var hintQueue = new Stack<int>(hints.Keys);
        while (hintQueue.Count > 0)
        {
            int token = hintQueue.Pop();

            // The queued token can be from either table: a hint refers to main-table tokens, and
            // those refer to more of their own. Looking only in the hint table meant the lookup
            // failed for every main-table token and the `continue` skipped it — along with
            // everything it referred to. Token 209 hangs off token 106, which the hints' random
            // element picks, so it was never reached and thirteen hints printed "APPEARED AT" with
            // no system name.
            if (!hints.TryGetValue(token, out List<Element>? hintElements) &&
                !tokens.TryGetValue(token, out hintElements))
            {
                continue;
            }

            foreach (Element element in hintElements)
            {
                // Marking a token reachable and following its references are two different things,
                // and conflating them loses tokens. `reachable.Add(x) && queue.Push(x)` skips the
                // queue whenever x was marked by an earlier hint — and a token's own references are
                // only followed when it is visited, so its children were lost with it. Token 209 is
                // reached only this way: the hints' random element picks token 106, and 106's last
                // element is ETOK 209, the system name. The token table already had 106 marked, so
                // 106 was never visited, so 209 was never found — and thirteen hints printed
                // "APPEARED AT" with no system name after it.
                if (element.Kind is "ETOK" or "EREC")
                {
                    if (tokens.ContainsKey(element.Value))
                    {
                        reachable.Add(element.Value);
                        hintQueue.Push(element.Value);
                    }
                }
                else if (element.Kind == "ERND")
                {
                    for (int i = 0; i < 5; i++)
                    {
                        int candidate = mtin[element.Value] + i;
                        if (tokens.ContainsKey(candidate))
                        {
                            reachable.Add(candidate);
                            hintQueue.Push(candidate);
                        }
                    }
                }
            }
        }

        if (tokens.TryGetValue(106, out var t106))
        {
        }

        // The standard tokens the extended text borrows: walk each one's bytes the way TT27 does,
        // following the recursive ones into the rest of the table
        var standardReachable = new SortedSet<int>();
        var standardQueue = new Stack<int>();
        foreach (int token in reachable)
        {
            if (tokens.TryGetValue(token, out List<Element>? elements))
            {
                foreach (Element element in elements.Where(e => e.Kind == "STOK"))
                {
                    standardQueue.Push(element.Value);
                }
            }
        }

        while (standardQueue.Count > 0)
        {
            int token = standardQueue.Pop();
            if (!standardReachable.Add(token) || !standard.TryGetValue(token, out List<Element>? bytes))
            {
                continue;
            }

            foreach (Element element in bytes)
            {
                if (element.Kind == "EBYT" && RecursiveToken(element.Value) is int child)
                {
                    standardQueue.Push(child);
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
            hintTokens = hints.ToDictionary(
                token => token.Key.ToString(),
                token => token.Value.Select(e => new { kind = e.Kind, value = e.Value, text = e.Text }).ToArray()),
            hints = rupla.Zip(rugal, (system, criteria) => new { system, criteria }).ToArray(),
            standardTokens = standardReachable.ToDictionary(
                token => token.ToString(),
                token => standard[token].Select(e => new { kind = e.Kind, value = e.Value, text = e.Text }).ToArray()),
            twoLetterTokens,
            standardTwoLetterTokens,
        };

        return JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Works out which recursive token a standard token's byte refers to, or null if it is not a
    /// reference to another token. This is the dispatch in TT27: 160-255 are tokens 0-95, 96-127 are
    /// themselves, 14-31 are tokens 128-145, and the rest are control codes, characters and
    /// two-letter tokens.
    /// </summary>
    private static int? RecursiveToken(int value) => value switch
    {
        >= 160 => value - 160,
        >= 128 => null,
        >= 96 => value,
        >= 32 => null,
        >= 14 => value + 114,
        _ => null,
    };

    /// <summary>One element of a token: a directive, a character or a token reference.</summary>
    private readonly record struct Element(string Kind, int Value, string? Text);

    private static Dictionary<int, List<Element>> ParseTokens(string path)
    {
        var tokens = new Dictionary<int, List<Element>>();
        List<Element>? current = null;

        // Token definitions contain IF/ELIF/ELSE blocks for the different versions of the game.
        // Reading them all in would splice two versions of a word together — reading an
        // unconditional "UNREMAR" and then the NES "UNM..." gives "unremarunremar..." — so the
        // conditionals are evaluated for the disc version's docked build.
        var branches = new Stack<(bool Taken, bool Active)>();

        foreach (string raw in File.ReadLines(path))
        {
            string? directive = Conditional(raw, branches);
            if (directive == "skip")
            {
                continue;
            }

            bool active = branches.Count == 0 || branches.Peek().Active;
            if (!active)
            {
                continue;
            }

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

    /// <summary>
    /// Handles a conditional directive, updating the branch stack. Returns "skip" when the line is
    /// a directive that should not be treated as token data.
    /// </summary>
    private static string? Conditional(string raw, Stack<(bool Taken, bool Active)> branches)
    {
        int comment = raw.IndexOf('\\');
        string code = (comment >= 0 ? raw[..comment] : raw).Trim();
        string[] parts = code.Split(' ', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        switch (parts[0])
        {
            case "IF":
            {
                bool condition = Evaluate(parts.Length > 1 ? parts[1] : string.Empty);
                branches.Push((condition, condition));
                return "skip";
            }

            case "ELIF":
            {
                if (branches.Count == 0)
                {
                    return "skip";
                }

                (bool taken, _) = branches.Pop();
                bool condition = !taken && Evaluate(parts.Length > 1 ? parts[1] : string.Empty);
                branches.Push((taken || condition, condition));
                return "skip";
            }

            case "ELSE":
            {
                if (branches.Count == 0)
                {
                    return "skip";
                }

                (bool taken, _) = branches.Pop();
                branches.Push((true, !taken));
                return "skip";
            }

            case "ENDIF":
                if (branches.Count > 0)
                {
                    branches.Pop();
                }

                return "skip";

            default:
                return null;
        }
    }

    /// <summary>Evaluates a condition such as <c>NOT(_NES_VERSION)</c> or <c>A OR B</c>.</summary>
    private static bool Evaluate(string expression)
    {
        string text = expression.Trim();

        if (text.StartsWith("NOT(", StringComparison.Ordinal) && text.EndsWith(')'))
        {
            return !Evaluate(text[4..^1]);
        }

        if (text.Contains(" OR ", StringComparison.Ordinal))
        {
            return text.Split(" OR ", StringSplitOptions.TrimEntries).Any(Evaluate);
        }

        if (text.Contains(" AND ", StringComparison.Ordinal))
        {
            return text.Split(" AND ", StringSplitOptions.TrimEntries).All(Evaluate);
        }

        if (text.Contains(" EOR ", StringComparison.Ordinal))
        {
            return text.Split(" EOR ", StringSplitOptions.TrimEntries).Count(Evaluate) % 2 == 1;
        }

        if (text is "TRUE" or "1")
        {
            return true;
        }

        if (text is "FALSE" or "0" || text.Length == 0)
        {
            return false;
        }

        return TrueFlags.Contains(text);
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
                return TryParseNumber(argument, out int value) ? new Element(kind, value, null) : null;

            case "TOKN":
                // TOKN n is a recursive token from the *standard* table, which is what jump token 6
                // switches the printer to. It carries the token's number, not a byte, so the two
                // sources of standard tokens agree on the numbering.
                return TryParseNumber(argument, out int standard) ? new Element("STOK", standard, null) : null;

            default:
                return null;
        }
    }

    /// <summary>
    /// Parses the standard recursive token table, QQ18, which the docked text and the mission
    /// briefings' borrowed phrases come from.
    /// </summary>
    /// <remarks>
    /// Its tokens are stored as bytes rather than as the macro names the extended table uses, and
    /// the bytes are kept as they are: the printer decodes them with TT27's dispatch, so a token's
    /// control codes, characters, two-letter tokens and recursive references all behave exactly as
    /// the original's do. CHAR and TWOK give their characters, RTOK and CONT give the byte their
    /// macros assemble, and EQUB 0 ends the token.
    /// </remarks>
    private static Dictionary<int, List<Element>> ParseStandardTokens(string path)
    {
        var tokens = new Dictionary<int, List<Element>>();
        List<Element>? current = null;
        var branches = new Stack<(bool Taken, bool Active)>();

        foreach (string raw in File.ReadLines(path))
        {
            if (Conditional(raw, branches) == "skip")
            {
                continue;
            }

            if (branches.Count > 0 && !branches.Peek().Active)
            {
                continue;
            }

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

            if (code.StartsWith("EQUB 0", StringComparison.Ordinal))
            {
                current = null;
                continue;
            }

            foreach (int value in ParseStandardBytes(code))
            {
                current.Add(new Element("EBYT", value, null));
            }
        }

        return tokens;
    }

    /// <summary>The bytes a line of the standard token table assembles to.</summary>
    private static IEnumerable<int> ParseStandardBytes(string code)
    {
        string[] parts = code.Split(' ', 2, StringSplitOptions.TrimEntries);
        string argument = parts.Length > 1 ? parts[1] : string.Empty;

        switch (parts[0])
        {
            case "CHAR":
                yield return Unquote(argument)[0];
                break;

            case "TWOK":
            {
                string[] pair = argument.Split(',', StringSplitOptions.TrimEntries);
                if (pair.Length == 2)
                {
                    yield return Unquote(pair[0])[0];
                    yield return Unquote(pair[1])[0];
                }

                break;
            }

            case "RTOK":
                if (TryParseNumber(argument, out int rtok))
                {
                    // RTOK stores tokens 0-95 as 160-255, tokens 128 and up as 14-31, and 96-127 as
                    // themselves, so that the byte fits in the range TT27 expects
                    yield return rtok switch
                    {
                        >= 0 and <= 95 => rtok + 160,
                        >= 128 => rtok - 114,
                        _ => rtok,
                    };
                }

                break;

            case "CONT":
                // CONT is used for the control codes, which are stored as they are
                if (TryParseNumber(argument, out int cont))
                {
                    yield return cont;
                }

                break;
        }
    }

    /// <summary>
    /// Parses the two-letter tokens that jump token 18 prints one to four of. It indexes the table
    /// at TKN2 from its second entry (the first is a newline) and runs on past the end of TKN2 into
    /// QQ16, which follows it in the original's memory.
    /// </summary>
    private static string[] ParseTwoLetterTokens(string extendedPath, string standardPath, bool skipFirst)
    {
        var pairs = new List<string>();

        // The first entry of TKN2 is {crlf}, two control codes rather than letters, and the original
        // deliberately skips it
        bool first = skipFirst;
        foreach (string path in new[] { extendedPath, standardPath })
        {
            var branches = new Stack<(bool Taken, bool Active)>();

            foreach (string raw in File.ReadLines(path))
            {
                if (Conditional(raw, branches) == "skip")
                {
                    continue;
                }

                if (branches.Count > 0 && !branches.Peek().Active)
                {
                    continue;
                }

                int comment = raw.IndexOf('\\');
                string code = (comment >= 0 ? raw[..comment] : raw).Trim();
                if (code.StartsWith("EQUS \"", StringComparison.Ordinal))
                {
                    string text = code["EQUS \"".Length..].TrimEnd('"');
                    if (first)
                    {
                        first = false;
                        continue;
                    }

                    pairs.Add(text);
                }
                else if (code.StartsWith("EQUB 12, 10", StringComparison.Ordinal))
                {
                    // TKN2's first entry, the {crlf} that is skipped
                    first = false;
                }
            }
        }

        // A random offset of 0-62 into the pairs is 32 of them. The standard table is 32 pairs long
        // as it is, and the combined one runs past the end of the extended table's 12 into it, so
        // the same cap covers both.
        return pairs.Take(32).ToArray();
    }

    /// <summary>
    /// Parses a number as the original's source writes them: decimal, or hexadecimal after an
    /// ampersand, or binary after a percent sign.
    /// </summary>
    private static bool TryParseNumber(string text, out int value)
    {
        string trimmed = text.Trim();
        value = 0;

        if (trimmed.Length == 0)
        {
            return false;
        }

        try
        {
            if (trimmed[0] == '&')
            {
                value = Convert.ToInt32(trimmed[1..], 16);
                return true;
            }

            if (trimmed[0] == '%')
            {
                value = Convert.ToInt32(trimmed[1..], 2);
                return true;
            }

            return int.TryParse(trimmed, out value);
        }
        catch (Exception error) when (error is FormatException or OverflowException or ArgumentException)
        {
            value = 0;
            return false;
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

        // These tables carry the same platform conditionals as everything else - RUPLA and RUGAL
        // wrap their Lave and Riedquat entries in the 6502SP and Executive guards, which the disc
        // does not assemble - so the branches have to be evaluated here too. Reading every EQUB
        // regardless hands back entries for tokens the disc does not have, and PDESC then prints
        // whatever lies past the end of RUTOK.
        var branches = new Stack<(bool Taken, bool Active)>();

        foreach (string raw in File.ReadLines(path))
        {
            if (Conditional(raw, branches) == "skip")
            {
                continue;
            }

            if (branches.Count > 0 && !branches.Peek().Active)
            {
                continue;
            }

            int comment = raw.IndexOf('\\');
            string code = (comment >= 0 ? raw[..comment] : raw).Trim();
            if (!code.StartsWith("EQUB ", StringComparison.Ordinal))
            {
                continue;
            }

            string value = code["EQUB ".Length..].Trim();
            if (TryParseNumber(value, out int number))
            {
                values.Add(number);
            }
        }

        return values.ToArray();
    }
}
