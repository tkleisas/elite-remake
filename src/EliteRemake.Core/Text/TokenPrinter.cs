using System.Text;
using EliteRemake.Core.Sim;

namespace EliteRemake.Core.Text;

/// <summary>One element of an extended token.</summary>
/// <param name="Kind">The element's kind: ECHR, ETWO, EJMP, ETOK, EREC or ERND.</param>
/// <param name="Value">The token or jump number, for the kinds that have one.</param>
/// <param name="Text">The characters, for the kinds that carry text.</param>
public readonly record struct TokenElement(string Kind, int Value, string? Text);

/// <summary>
/// Prints the original's extended text tokens, which is how a system's description is assembled.
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
/// </remarks>
public sealed class TokenPrinter
{
    private readonly IReadOnlyDictionary<int, TokenElement[]> _tokens;
    private readonly int[] _mtin;
    private readonly EliteRandom _random;
    private readonly StringBuilder _output = new();
    private readonly HashSet<int> _expanding = [];

    private bool _lowerCase;
    private bool _singleCap;
    private bool _sentenceCase = true;
    private bool _startOfSentence;

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
    /// Expands a token into text. The name is what {the system name} expands to, so the caller
    /// decides whose description this is.
    /// </summary>
    public string Print(int token, string systemName)
    {
        _output.Clear();
        // The tokens themselves decide the case: the description opens with {lower case}, while the
        // mission hints do not, and so keep the capitals the tables store them in
        _lowerCase = false;
        _singleCap = false;
        _sentenceCase = false;
        _startOfSentence = true;
        _expanding.Clear();

        Expand(token, systemName, 0);
        return Tidy(_output.ToString());
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
                    Emit(element.Text ?? string.Empty);
                    break;

                case "ETWO":
                    Emit(element.Text ?? string.Empty);
                    break;

                case "EJMP":
                    Jump(element.Value, systemName, depth);
                    break;

                case "ETOK":
                case "EREC":
                    Expand(element.Value, systemName, depth + 1);
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
    /// Applies a jump token, which is the original's way of changing case, inserting the system
    /// name, or starting a new line.
    /// </summary>
    private void Jump(int number, string systemName, int depth)
    {
        _ = depth;

        switch (number)
        {
            case 2:
            case 18:
                // Sentence case: a capital to start, lower case afterwards
                _sentenceCase = true;
                _lowerCase = false;
                break;

            case 13:
                // All lower case
                _lowerCase = true;
                break;

            case 19:
                // A single capital letter, then back to lower case
                _singleCap = true;
                break;

            case 3:
                // The selected system's name, which is a proper noun: it keeps its capital
                // wherever it appears in the sentence
                EmitProperNoun(systemName);
                break;

            case 12:
            case 26:
                _output.Append('\n');
                break;

            case 14:
            case 15:
            case 17:
                // Justification and alignment, which do not change the words
                break;
        }
    }

    /// <summary>
    /// Adds a proper noun, capitalising its first letter and leaving the rest as the tables give it,
    /// so a system's name reads correctly in the middle of a lower case sentence.
    /// </summary>
    private void EmitProperNoun(string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        _output.Append(char.ToUpperInvariant(text[0]));
        _output.Append(text[1..].ToLowerInvariant());
        _singleCap = false;
        _startOfSentence = false;
    }

    /// <summary>Adds text, applying whatever case the tokens have asked for.</summary>
    private void Emit(string text)
    {
        foreach (char character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                _output.Append(character);
                continue;
            }

            // A single capital takes precedence over lower case: {single cap} capitalises the next
            // letter and then lower case resumes, which is how the system name keeps its capital
            // inside an otherwise lower case description
            if (_singleCap)
            {
                _output.Append(char.ToUpperInvariant(character));
                _singleCap = false;
                _startOfSentence = false;
                continue;
            }

            if (_lowerCase)
            {
                _output.Append(char.ToLowerInvariant(character));
                _startOfSentence = false;
            }
            else if (_sentenceCase && _startOfSentence)
            {
                _output.Append(char.ToUpperInvariant(character));
                _startOfSentence = false;
            }
            else
            {
                _output.Append(_sentenceCase ? char.ToLowerInvariant(character) : character);
            }
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
