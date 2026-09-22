using System.Globalization;

namespace EliteDataExtractor.Assembly;

/// <summary>Raised for any malformed or unresolvable assembly source.</summary>
internal sealed class AsmException : Exception
{
    public AsmException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Evaluates the subset of BeebAsm expressions used by the ship blueprint sources.
///
/// Supported syntax:
///   * decimal, &amp;hex, %binary and $hex integer literals
///   * symbols (case-insensitive) resolved through a caller-supplied lookup
///   * unary - + ~ NOT, binary * / % + - &lt;&lt; &gt;&gt; comparisons, AND, OR, EOR, and
///     ^ as a power operator (BeebAsm itself has no ^ operator; the sources only mention 2^n in
///     comments, so this is provided for completeness and is not exercised by the disc ships)
///   * the functions ABS(), LO(), HI() and NOT()
///   * the predefined constants TRUE and FALSE
/// </summary>
internal sealed class ExpressionEvaluator
{
    private readonly Func<string, long?> _resolve;
    private readonly List<string> _unknown = [];

    public ExpressionEvaluator(Func<string, long?> resolve) => _resolve = resolve;

    /// <summary>Symbols that were referenced but could not be resolved.</summary>
    public IReadOnlyList<string> UnknownSymbols => _unknown;

    /// <summary>When true, an unresolved symbol throws; otherwise it evaluates to zero.</summary>
    public bool Strict { get; init; }

    public long Evaluate(string text)
    {
        var parser = new Parser(text, this);
        long value = parser.ParseExpression();
        parser.ExpectEnd();
        return value;
    }

    private long Resolve(string name)
    {
        if (name.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (name.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        long? value = _resolve(name);
        if (value.HasValue)
        {
            return value.Value;
        }

        if (Strict)
        {
            throw new AsmException($"unknown symbol '{name}' in expression");
        }

        _unknown.Add(name);
        return 0;
    }

    private enum TokenKind
    {
        Number,
        Symbol,
        Operator,
        OpenParen,
        CloseParen,
        End,
    }

    private readonly record struct Token(TokenKind Kind, long Value, string Text);

    /// <summary>Recursive-descent parser over a tokenised expression.</summary>
    private sealed class Parser
    {
        private readonly List<Token> _tokens;
        private readonly ExpressionEvaluator _owner;
        private int _position;

        public Parser(string text, ExpressionEvaluator owner)
        {
            _owner = owner;
            _tokens = Tokenize(text);
        }

        public long ParseExpression() => ParseOr();

        public void ExpectEnd()
        {
            if (Peek.Kind != TokenKind.End)
            {
                throw new AsmException($"unexpected '{Peek.Text}' at end of expression");
            }
        }

        private Token Peek => _tokens[_position];

        private Token Next() => _tokens[_position++];

        private bool TakeOperator(string op)
        {
            if (Peek.Kind == TokenKind.Operator && string.Equals(Peek.Text, op, StringComparison.OrdinalIgnoreCase))
            {
                _position++;
                return true;
            }

            return false;
        }

        private long ParseOr()
        {
            long left = ParseEor();
            while (Peek.Kind == TokenKind.Operator && Peek.Text is "OR" or "|")
            {
                _position++;
                long right = ParseEor();
                left |= right;
            }

            return left;
        }

        private long ParseEor()
        {
            long left = ParseAnd();
            while (TakeOperator("EOR"))
            {
                long right = ParseAnd();
                left ^= right;
            }

            return left;
        }

        private long ParseAnd()
        {
            long left = ParseComparison();
            while (TakeOperator("AND"))
            {
                long right = ParseComparison();
                left &= right;
            }

            return left;
        }

        private long ParseComparison()
        {
            long left = ParseShift();
            if (Peek.Kind != TokenKind.Operator)
            {
                return left;
            }

            string op = Peek.Text;
            if (op is "=" or "<>" or "<" or ">" or "<=" or ">=")
            {
                _position++;
                long right = ParseShift();
                bool result = op switch
                {
                    "=" => left == right,
                    "<>" => left != right,
                    "<" => left < right,
                    ">" => left > right,
                    "<=" => left <= right,
                    _ => left >= right,
                };
                return result ? 1 : 0;
            }

            return left;
        }

        private long ParseShift()
        {
            long left = ParseAdditive();
            while (Peek.Kind == TokenKind.Operator && Peek.Text is "<<" or ">>")
            {
                string op = Next().Text;
                long right = ParseAdditive();
                left = op == "<<" ? left << (int)right : left >> (int)right;
            }

            return left;
        }

        private long ParseAdditive()
        {
            long left = ParseMultiplicative();
            while (Peek.Kind == TokenKind.Operator && Peek.Text is "+" or "-")
            {
                string op = Next().Text;
                long right = ParseMultiplicative();
                left = op == "+" ? left + right : left - right;
            }

            return left;
        }

        private long ParseMultiplicative()
        {
            long left = ParsePower();
            while (Peek.Kind == TokenKind.Operator && Peek.Text is "*" or "/" or "%")
            {
                string op = Next().Text;
                long right = ParsePower();
                left = op switch
                {
                    "*" => left * right,
                    "/" => right == 0 ? throw new AsmException("division by zero") : left / right,
                    _ => right == 0 ? throw new AsmException("modulo by zero") : left % right,
                };
            }

            return left;
        }

        private long ParsePower()
        {
            long left = ParseUnary();
            if (Peek.Kind == TokenKind.Operator && Peek.Text == "^")
            {
                _position++;
                long right = ParsePower();
                if (right < 0)
                {
                    throw new AsmException("negative exponent");
                }

                long result = 1;
                for (long i = 0; i < right; i++)
                {
                    result *= left;
                }

                return result;
            }

            return left;
        }

        private long ParseUnary()
        {
            if (Peek.Kind == TokenKind.Operator && Peek.Text is "-" or "+" or "~" or "NOT")
            {
                string op = Next().Text;
                long operand = ParseUnary();
                return op switch
                {
                    "-" => -operand,
                    "+" => operand,
                    "~" => ~operand,
                    // NOT is a logical negation in BeebAsm: it is used as NOT(flag) in the sources,
                    // where NOT(0) must be true and NOT(1) must be false.
                    _ => operand == 0 ? 1 : 0,
                };
            }

            return ParsePrimary();
        }

        private long ParsePrimary()
        {
            Token token = Next();
            switch (token.Kind)
            {
                case TokenKind.Number:
                    return token.Value;

                case TokenKind.Symbol:
                    if (Peek.Kind == TokenKind.OpenParen)
                    {
                        _position++;
                        long argument = ParseExpression();
                        Expect(TokenKind.CloseParen);
                        return ApplyFunction(token.Text, argument);
                    }

                    return _owner.Resolve(token.Text);

                case TokenKind.OpenParen:
                {
                    long value = ParseExpression();
                    Expect(TokenKind.CloseParen);
                    return value;
                }

                default:
                    throw new AsmException($"unexpected '{token.Text}' in expression");
            }
        }

        private void Expect(TokenKind kind)
        {
            if (Peek.Kind != kind)
            {
                throw new AsmException($"unexpected '{Peek.Text}' in expression");
            }

            _position++;
        }

        private static long ApplyFunction(string name, long argument) => name.ToUpperInvariant() switch
        {
            "ABS" => Math.Abs(argument),
            "LO" => argument & 0xFF,
            "HI" => (argument >> 8) & 0xFF,
            "NOT" => argument == 0 ? 1 : 0,
            _ => throw new AsmException($"unknown function '{name}'"),
        };

        private static List<Token> Tokenize(string text)
        {
            var tokens = new List<Token>();
            int i = 0;
            bool previousWasValue = false;

            while (i < text.Length)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                if (c == '(')
                {
                    tokens.Add(new Token(TokenKind.OpenParen, 0, "("));
                    i++;
                    previousWasValue = false;
                    continue;
                }

                if (c == ')')
                {
                    tokens.Add(new Token(TokenKind.CloseParen, 0, ")"));
                    i++;
                    previousWasValue = true;
                    continue;
                }

                if (c == '&' || c == '$')
                {
                    int start = ++i;
                    while (i < text.Length && Uri.IsHexDigit(text[i]))
                    {
                        i++;
                    }

                    if (i == start)
                    {
                        throw new AsmException("malformed hex literal");
                    }

                    tokens.Add(NumberToken(text[start..i], 16, text[start..i]));
                    previousWasValue = true;
                    continue;
                }

                if (c == '%' && !previousWasValue && i + 1 < text.Length && text[i + 1] is '0' or '1')
                {
                    int start = ++i;
                    while (i < text.Length && text[i] is '0' or '1')
                    {
                        i++;
                    }

                    tokens.Add(NumberToken(text[start..i], 2, "%" + text[start..i]));
                    previousWasValue = true;
                    continue;
                }

                if (char.IsAsciiDigit(c))
                {
                    int start = i;
                    if (c == '0' && i + 1 < text.Length && (text[i + 1] is 'x' or 'X'))
                    {
                        i += 2;
                        int hexStart = i;
                        while (i < text.Length && Uri.IsHexDigit(text[i]))
                        {
                            i++;
                        }

                        tokens.Add(NumberToken(text[hexStart..i], 16, text[start..i]));
                    }
                    else
                    {
                        while (i < text.Length && char.IsAsciiDigit(text[i]))
                        {
                            i++;
                        }

                        tokens.Add(NumberToken(text[start..i], 10, text[start..i]));
                    }

                    previousWasValue = true;
                    continue;
                }

                if (AsmText.IsSymbolStart(c))
                {
                    int start = i;
                    while (i < text.Length && AsmText.IsSymbolChar(text[i]))
                    {
                        i++;
                    }

                    string name = text[start..i];
                    bool isWordOperator = name.ToUpperInvariant() is "AND" or "OR" or "EOR" or "NOT";
                    tokens.Add(isWordOperator
                        ? new Token(TokenKind.Operator, 0, name.ToUpperInvariant())
                        : new Token(TokenKind.Symbol, 0, name));
                    previousWasValue = !isWordOperator;
                    continue;
                }

                string? op = c switch
                {
                    '<' when i + 1 < text.Length && text[i + 1] == '<' => "<<",
                    '>' when i + 1 < text.Length && text[i + 1] == '>' => ">>",
                    '<' when i + 1 < text.Length && text[i + 1] == '=' => "<=",
                    '>' when i + 1 < text.Length && text[i + 1] == '=' => ">=",
                    '<' when i + 1 < text.Length && text[i + 1] == '>' => "<>",
                    '+' or '-' or '*' or '/' or '%' or '^' or '~' or '|' or '=' or '<' or '>' => c.ToString(),
                    _ => null,
                };

                if (op is null)
                {
                    throw new AsmException($"unexpected character '{c}' in expression");
                }

                i += op.Length;
                tokens.Add(new Token(TokenKind.Operator, 0, op));
                previousWasValue = false;
            }

            tokens.Add(new Token(TokenKind.End, 0, string.Empty));
            return tokens;
        }

        private static Token NumberToken(string text, int numberBase, string display)
        {
            if (numberBase == 10 && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long parsed))
            {
                return new Token(TokenKind.Number, parsed, display);
            }

            long value = 0;
            foreach (char c in text)
            {
                int digit = Convert.ToInt32(c.ToString(), numberBase);
                value = (value * numberBase) + digit;
            }

            return new Token(TokenKind.Number, value, display);
        }
    }
}
