namespace EliteDataExtractor.Assembly;

/// <summary>
/// A single line of BeebAsm source, tagged with the file and line number it came from so that
/// diagnostics can point back at the original library.
/// </summary>
internal sealed record AsmLine(string File, int LineNumber, string Text)
{
    public override string ToString() => $"{File}:{LineNumber}";

    /// <summary>Returns the line with any trailing BeebAsm comment removed.</summary>
    public string Code => AsmText.StripComment(Text);
}

/// <summary>Small helpers for handling BeebAsm source text.</summary>
internal static class AsmText
{
    /// <summary>
    /// Removes a BeebAsm comment from a line. A backslash starts a comment, but only when it is not
    /// inside a double-quoted string.
    /// </summary>
    public static string StripComment(string text)
    {
        bool inString = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"')
            {
                inString = !inString;
            }
            else if (c == '\\' && !inString)
            {
                return text[..i];
            }
        }

        return text;
    }

    /// <summary>
    /// Splits a comma-separated argument list, honouring parentheses and double-quoted strings.
    /// </summary>
    public static List<string> SplitArguments(string text)
    {
        var result = new List<string>();
        int depth = 0;
        bool inString = false;
        int start = 0;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"')
            {
                inString = !inString;
            }
            else if (!inString)
            {
                switch (c)
                {
                    case '(':
                        depth++;
                        break;
                    case ')':
                        depth--;
                        break;
                    case ',' when depth == 0:
                        result.Add(text[start..i]);
                        start = i + 1;
                        break;
                }
            }
        }

        result.Add(text[start..]);
        return result;
    }

    /// <summary>Splits a line into whitespace-separated tokens, honouring quoted strings.</summary>
    public static List<string> SplitTokens(string text)
    {
        var tokens = new List<string>();
        int i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i]))
            {
                i++;
            }

            if (i >= text.Length)
            {
                break;
            }

            int start = i;
            bool inString = false;
            while (i < text.Length && (inString || !char.IsWhiteSpace(text[i])))
            {
                if (text[i] == '"')
                {
                    inString = !inString;
                }

                i++;
            }

            tokens.Add(text[start..i]);
        }

        return tokens;
    }

    /// <summary>True if the character can start a BeebAsm symbol name.</summary>
    public static bool IsSymbolStart(char c) => char.IsAsciiLetter(c) || c is '_' or '@';

    /// <summary>True if the character can appear in a BeebAsm symbol name.</summary>
    public static bool IsSymbolChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '@' or '%';
}
