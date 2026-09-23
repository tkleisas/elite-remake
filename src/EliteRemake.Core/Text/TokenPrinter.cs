using System.Text;
using EliteRemake.Core.Sim;

namespace EliteRemake.Core.Text;

/// <summary>One element of an extended token.</summary>
/// <param name="Kind">The element's kind: ECHR, ETWO, EJMP, ETOK, EREC, ERND, STOK or EBYT.</param>
/// <param name="Value">The token, jump, standard token or byte number, for the kinds that have one.</param>
/// <param name="Text">The characters, for the kinds that carry text.</param>
public readonly record struct TokenElement(string Kind, int Value, string? Text);

/// <summary>Something the original does part way through printing a token.</summary>
/// <remarks>
/// The briefings are not just text: they clear the screen, show the ship, wait for a key press and
/// print an INCOMING MESSAGE banner. Those are things the display does rather than words, so the
/// printer reports them and leaves the caller to act on them — the text itself is unaffected, and a
/// caller that only wants the words can ignore them.
/// </remarks>
public enum TokenAction
{
    /// <summary>Jump token 7: sound the beeper.</summary>
    Beep,

    /// <summary>Jump token 22: show the ship and wait for a key press.</summary>
    ShowShip,

    /// <summary>Jump token 24: wait for a key press.</summary>
    WaitForKey,

    /// <summary>Jump token 25: clear the screen and show the incoming message banner.</summary>
    IncomingMessage,
}

/// <summary>An action the tokens ask for, and where in the printed text it falls.</summary>
/// <param name="Action">What to do.</param>
/// <param name="Position">How many characters had been printed when the token asked for it.</param>
public readonly record struct TokenEvent(TokenAction Action, int Position);

/// <summary>
/// Prints the original's extended text tokens, which is how a system's description and the mission
/// briefings are assembled.
/// </summary>
/// <remarks>
/// A system's description is extended token 5, which the original expands as:
///
/// <code>
///     {lower case} {random 18} {the system name} IS {random 19}{left align}
/// </code>
///
/// Random tokens choose one of five tokens from a run given by the MTIN table, and the choice is
/// made with the game's random number generator — so a system's description is not fixed. Look at
/// the same system twice and you may be told something different, which is exactly what the
/// original does. The phrases themselves are the original's, extracted from its token table.
///
/// The jump tokens are the original's JMTB table, so the numbers mean what the disc version's jump
/// table says they mean: case changes, the system and commander names, the printer's tables being
/// switched, the mission captain's name and the mission hint.
/// </remarks>
public sealed class TokenPrinter
{
    private readonly IReadOnlyDictionary<int, TokenElement[]> _tokens;
    private readonly int[] _mtin;
    private readonly EliteRandom _random;
    private readonly StringBuilder _output = new();
    private readonly List<TokenEvent> _events = [];
    private readonly HashSet<int> _expanding = [];

    private bool _lowerCase;
    private bool _singleCap;
    private bool _sentenceCase = true;
    private bool _startOfWord;
    private bool _standardTokens;
    private bool _standardSentenceCase;

    public TokenPrinter(
        IReadOnlyDictionary<int, TokenElement[]> tokens,
        int[] mtin,
        EliteRandom random)
    {
        _tokens = tokens;
        _mtin = mtin;
        _random = random;
    }

    /// <summary>The recursion limit, so a malformed token table cannot loop forever.</summary>
    public int MaxDepth { get; init; } = 32;

    /// <summary>
    /// A second table to consult for tokens the first does not hold. The mission hints live in their
    /// own table and refer to tokens in the main one, so printing a hint needs both.
    /// </summary>
    public IReadOnlyDictionary<int, TokenElement[]>? FallbackTokens { get; init; }

    /// <summary>
    /// The standard token table, QQ18, which jump token 6 switches to and jump token 5 switches
    /// back. Its tokens are stored as the bytes TT27 dispatches on, so printing one is a second,
    /// simpler expansion.
    /// </summary>
    public IReadOnlyDictionary<int, TokenElement[]>? StandardTokens { get; init; }

    /// <summary>
    /// The two-letter tokens that jump token 18 prints one to four of, in the order the original's
    /// combined table gives them.
    /// </summary>
    public IReadOnlyList<string> TwoLetterTokens { get; init; } = [];

    /// <summary>The standard table's two-letter tokens, which QQ16 holds for tokens 128 to 159.</summary>
    public IReadOnlyList<string> StandardTwoLetterTokens { get; init; } = [];

    /// <summary>The commander's name, which jump token 4 prints.</summary>
    public string CommanderName { get; init; } = "JAMESON";

    /// <summary>The galaxy number, which selects the mission captain's name and the mission hint.</summary>
    public int Galaxy { get; init; }

    /// <summary>The actions the tokens asked for while printing, in the order they occurred.</summary>
    public IReadOnlyList<TokenEvent> Events => _events;

    /// <summary>
    /// Expands a token into text. The name is what {the system name} expands to, so the caller
    /// decides whose description this is.
    /// </summary>
    public string Print(int token, string systemName)
    {
        _output.Clear();
        _events.Clear();
        // The tokens themselves decide the case: the description opens with {lower case}, while the
        // mission hints do not, and so keep the capitals the tables store them in
        _lowerCase = false;
        _singleCap = false;
        _sentenceCase = false;
        _startOfWord = true;
        _standardTokens = false;
        _standardSentenceCase = false;
        _expanding.Clear();

        Expand(token, systemName, 0);
        string text = Tidy(_output.ToString());

        // The events were recorded as the text was printed, but the tidy that follows collapses
        // runs of spaces and trims the ends, so a position can fall past the returned text's end —
        // the briefing's last {show ship and wait} token does exactly that. The events are clamped
        // into the text, in order, so what they report is where the tidied text reaches.
        for (int i = 0; i < _events.Count; i++)
        {
            if (_events[i].Position > text.Length)
            {
                _events[i] = _events[i] with { Position = text.Length };
            }
        }

        return text;
    }

    private void Expand(int token, string systemName, int depth)
    {
        if (depth > MaxDepth)
        {
            return;
        }

        if (!_tokens.TryGetValue(token, out TokenElement[]? elements))
        {
            if (FallbackTokens is null || !FallbackTokens.TryGetValue(token, out elements))
            {
                return;
            }
        }

        if (!_expanding.Add(token))
        {
            return; // a token that refers to itself
        }

        foreach (TokenElement element in elements)
        {
            switch (element.Kind)
            {
                case "ECHR":
                case "ETWO":
                    Emit(element.Text ?? string.Empty);
                    break;

                case "EJMP":
                    Jump(element.Value, systemName, depth);
                    break;

                case "ETOK":
                case "EREC":
                    // While the printer is pointed at the standard tokens (jump token 6, until
                    // jump token 5), a recursive token number is a standard token number
                    if (_standardTokens && StandardTokens is not null)
                    {
                        ExpandStandard(element.Value, systemName, depth + 1);
                    }
                    else
                    {
                        Expand(element.Value, systemName, depth + 1);
                    }

                    break;

                case "STOK":
                    // A token borrowed from the standard table, which is what jump token 6 leaves
                    // the printer pointing at
                    ExpandStandard(element.Value, systemName, depth + 1);
                    break;

                case "ERND":
                {
                    // A random token picks one of five from the run the MTIN table gives
                    byte roll = _random.Next();
                    int chosen = _mtin[element.Value] + (roll % 5);
                    Expand(chosen, systemName, depth + 1);
                    break;
                }
            }
        }

        _expanding.Remove(token);
    }

    /// <summary>
    /// Applies a jump token, which is the original's JMTB table: the disc version's jump tokens 1 to
    /// 32, which change the case, print a name, switch tables, ask for a key press, and so on.
    /// </summary>
    private void Jump(int number, string systemName, int depth)
    {
        switch (number)
        {
            case 1:
                // {all caps}
                _lowerCase = false;
                _sentenceCase = false;
                _singleCap = false;
                break;

            case 2:
                // {sentence case}: a capital to start, lower case afterwards
                _sentenceCase = true;
                _lowerCase = false;
                break;

            case 3:
                // The selected system's name, which is a proper noun: it keeps its capital
                // wherever it appears in the sentence
                EmitProperNoun(systemName);
                break;

            case 4:
                // The commander's name, which the original prints with TT27's control code 4:
                // its name routine hands each character straight to the printer without any case
                // conversion, so the name comes out exactly as it was saved
                EmitVerbatim(CommanderName);
                break;

            case 5:
                // {extended tokens}
                _standardTokens = false;
                break;

            case 6:
                // {standard tokens, sentence case}: the standard tokens have their own case
                // setting (QQ17 in the original), so this must not disturb the extended tokens'
                // one — the briefing keeps its lower case when jump token 5 switches back
                _standardTokens = true;
                _standardSentenceCase = true;
                break;

            case 7:
                _events.Add(new TokenEvent(TokenAction.Beep, _output.Length));
                break;

            case 8:
            case 9:
            case 11:
            case 14:
            case 15:
            case 16:
            case 20:
            case 21:
            case 23:
            case 29:
                // Layout, colour and the screen: {tab 6}, {clear screen}, the title box,
                // {justify}, {left align}, the drive number, {move to row 10}, {column 6}. The
                // words are unaffected. Jump token 23 and 29 also set lower case, which the
                // description relies on
                if (number is 23 or 29)
                {
                    _lowerCase = true;
                    _sentenceCase = false;
                }

                break;

            case 10:
            case 12:
                // Line feed and carriage return
                _output.Append('\n');
                break;

            case 13:
                // All lower case
                _lowerCase = true;
                _sentenceCase = false;
                break;

            case 17:
                // The system's name as an adjective: the original removes the name's last letter if
                // it is a vowel and prints token 153 ("IAN") after it, so LAVE gives LAVIAN
                EmitAdjective(systemName, depth);
                break;

            case 18:
                // One to four random two-letter tokens, with the first capitalised
                PrintRandomWord(depth);
                break;

            case 19:
                // A single capital letter, then back to lower case
                _singleCap = true;
                break;

            case 22:
                _events.Add(new TokenEvent(TokenAction.ShowShip, _output.Length));
                break;

            case 24:
                _events.Add(new TokenEvent(TokenAction.WaitForKey, _output.Length));
                break;

            case 25:
                _events.Add(new TokenEvent(TokenAction.IncomingMessage, _output.Length));
                break;

            case 27:
                // The mission captain's name: token 217 plus the galaxy number
                Expand(217 + Galaxy, systemName, depth + 1);
                break;

            case 28:
                // The mission 1 location hint: token 220 plus the galaxy number
                Expand(220 + Galaxy, systemName, depth + 1);
                break;
        }
    }

    /// <summary>
    /// Prints a standard token, which is a run of the bytes TT27 dispatches on. It is a much older
    /// and simpler scheme than the extended tokens: control codes, characters, two-letter tokens and
    /// recursive tokens, with no jumps and no random phrases.
    /// </summary>
    private void ExpandStandard(int token, string systemName, int depth)
    {
        if (depth > MaxDepth || StandardTokens is null || !_expanding.Add(-token - 1))
        {
            return;
        }

        // While a standard token is being printed its case setting applies instead of the extended
        // one, and the extended one is put back afterwards
        bool lowerCase = _lowerCase;
        bool sentenceCase = _sentenceCase;
        if (_standardSentenceCase)
        {
            _lowerCase = false;
            _sentenceCase = true;
        }

        if (StandardTokens.TryGetValue(token, out TokenElement[]? bytes))
        {
            foreach (TokenElement element in bytes)
            {
                if (element.Kind != "EBYT")
                {
                    continue;
                }

                int value = element.Value;
                switch (value)
                {
                    case < 14:
                        // Control codes: the two the briefings can reach are the cases below, and
                        // the others print the cash, fuel and galaxy number
                        StandardControlCode(value, systemName);
                        break;

                    case < 32:
                        ExpandStandard(value + 114, systemName, depth + 1);
                        break;

                    case < 96:
                        Emit(((char)value).ToString());
                        break;

                    case < 128:
                        ExpandStandard(value, systemName, depth + 1);
                        break;

                    case < 160:
                        // A two-letter token from the standard table's QQ16. Nothing the briefings
                        // reach uses one
                        EmitTwoLetter(value - 128);
                        break;

                    default:
                        ExpandStandard(value - 160, systemName, depth + 1);
                        break;
                }
            }
        }

        _lowerCase = lowerCase;
        _sentenceCase = sentenceCase;
        _expanding.Remove(-token - 1);
    }

    /// <summary>The standard tokens' control codes, which TT27 prints before any text.</summary>
    private void StandardControlCode(int code, string systemName)
    {
        switch (code)
        {
            case 2:
            case 3:
                EmitProperNoun(systemName);
                break;

            case 4:
                EmitVerbatim(CommanderName);
                break;

            case 6:
                _sentenceCase = true;
                _lowerCase = false;
                break;

            case 7:
                _events.Add(new TokenEvent(TokenAction.Beep, _output.Length));
                break;

            case 8:
                _lowerCase = false;
                _sentenceCase = false;
                break;

            case 10:
            case 12:
                _output.Append('\n');
                break;
        }
    }

    /// <summary>
    /// Prints a two-letter token from the standard table's QQ16, which stores them as pairs of
    /// letters with a question mark standing for a single-letter token.
    /// </summary>
    private void EmitTwoLetter(int index)
    {
        if (index < StandardTwoLetterTokens.Count)
        {
            string pair = StandardTwoLetterTokens[index];
            Emit(pair[..1]);
            if (pair.Length > 1 && pair[1] != '?')
            {
                Emit(pair[1..]);
            }
        }
    }

    /// <summary>Prints one to four random two-letter tokens, as jump token 18 does.</summary>
    private void PrintRandomWord(int depth)
    {
        _ = depth;
        if (TwoLetterTokens.Count == 0)
        {
            return;
        }

        // The original sets sentence case for the word, picks a count with one random number and
        // then a pair with another for each pair it prints
        bool allCaps = !_sentenceCase && !_lowerCase;
        _singleCap = !allCaps;

        int count = (_random.Next() & 3) + 1;
        for (int i = 0; i < count; i++)
        {
            // The original masks the random byte with 62 to give an even offset into the table of
            // two-byte pairs, so the offset is twice the index
            string pair = TwoLetterTokens[(_random.Next() & 62) >> 1];
            Emit(pair);
        }
    }

    /// <summary>
    /// Prints a system's name as an adjective: the original drops the name's last letter if it is a
    /// vowel, then prints token 153 ("IAN").
    /// </summary>
    private void EmitAdjective(string systemName, int depth)
    {
        string stem = systemName;
        if (stem.Length > 0 && "aeiou".Contains(char.ToLowerInvariant(stem[^1])))
        {
            stem = stem[..^1];
        }

        EmitProperNoun(stem);
        Expand(153, systemName, depth + 1);
    }

    /// <summary>
    /// Adds a proper noun, capitalising its first letter and leaving the rest as the tables give it,
    /// so a system's name reads correctly in the middle of a lower case sentence.
    /// </summary>
    /// <remarks>
    /// The name is printed through the standard token system, which has its own case setting rather
    /// than the extended tokens' one. Sentence case gives "Lave" and all caps gives "LAVE", which is
    /// what a system's name looks like in a mission hint.
    /// </remarks>
    private void EmitProperNoun(string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (!_lowerCase && !_sentenceCase)
        {
            _output.Append(text.ToUpperInvariant());
        }
        else
        {
            _output.Append(char.ToUpperInvariant(text[0]));
            _output.Append(text[1..].ToLowerInvariant());
        }

        _singleCap = false;
        _startOfWord = text[^1] is '.' or ':';
    }

    /// <summary>Adds text exactly as it is stored, without any case conversion.</summary>
    private void EmitVerbatim(string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        _output.Append(text);
        _singleCap = false;
        _startOfWord = text[^1] is '.' or ':';
    }

    /// <summary>Adds text, applying whatever case the tokens have asked for.</summary>
    /// <remarks>
    /// Sentence case is the original's: the first letter of each word keeps the capital the table
    /// stores it with and the rest of the word is lower cased, so CAPTAIN prints as Captain. A word
    /// starts after a space, a full stop, a colon or a line break, which TT26 is the authority for —
    /// a comma is not one, so an "I" after a comma is lower cased to "i" and the briefing really
    /// does read "Greetings Commander Jameson, i am Captain ...". That is the original's text, not a
    /// mistake in the tables.
    /// </remarks>
    private void Emit(string text)
    {
        foreach (char character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                _output.Append(character);
                _startOfWord = true;
                continue;
            }

            // A single capital takes precedence over lower case: {single cap} capitalises the next
            // letter and then lower case resumes, which is how the system name keeps its capital
            // inside an otherwise lower case description
            if (_singleCap)
            {
                _output.Append(char.ToUpperInvariant(character));
                _singleCap = false;
            }
            else if (_lowerCase)
            {
                _output.Append(char.ToLowerInvariant(character));
            }
            else if (_sentenceCase && _startOfWord)
            {
                _output.Append(char.ToUpperInvariant(character));
            }
            else
            {
                _output.Append(_sentenceCase ? char.ToLowerInvariant(character) : character);
            }

            _startOfWord = character is '.' or ':';
        }
    }

    /// <summary>Tidies the assembled text: single spaces, no space before punctuation.</summary>
    private static string Tidy(string text)
    {
        string collapsed = System.Text.RegularExpressions.Regex.Replace(text, "[ \t]+", " ");
        collapsed = collapsed.Replace(" .", ".").Replace(" ,", ",").Replace(" '", "'");
        return collapsed.Trim();
    }
}
