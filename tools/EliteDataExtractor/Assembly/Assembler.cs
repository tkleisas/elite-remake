using System.Text;

namespace EliteDataExtractor.Assembly;

/// <summary>Information about a label defined by <c>.name</c> in the source.</summary>
internal sealed record LabelInfo(string Name, int Address, string File, int LineNumber);

/// <summary>The result of assembling one BeebAsm source file.</summary>
internal sealed class AssemblyResult
{
    public required string MainFile { get; init; }

    public required int BaseAddress { get; init; }

    public required byte[] Image { get; init; }

    public required IReadOnlyDictionary<string, LabelInfo> Labels { get; init; }

    public required IReadOnlyDictionary<string, long> Symbols { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }

    public int EndAddress => BaseAddress + Image.Length;

    public int? LabelAddress(string name) => Labels.TryGetValue(name, out LabelInfo? label) ? label.Address : null;

    public long? SymbolValue(string name) => Symbols.TryGetValue(name, out long value) ? value : null;

    /// <summary>Converts an absolute 6502 address to an offset in <see cref="Image"/>.</summary>
    public int OffsetOf(int address) => address - BaseAddress;

    /// <summary>True if the address falls inside the assembled image.</summary>
    public bool Contains(int address) => address >= BaseAddress && address < EndAddress;
}

/// <summary>
/// A small, pragmatic BeebAsm-compatible assembler, sufficient for the ship blueprint sources of
/// the BBC Micro disc version of Elite: MACRO/ENDMACRO, IF/ELIF/ELSE/ENDIF, EQUB/EQUW/EQUD/EQUS,
/// SKIP, ORG, GUARD, INCLUDE, labels and symbol assignment.
///
/// Assembly runs in two passes. The first pass records label addresses (data values that depend on
/// forward references are allowed to be wrong); the second pass evaluates every expression with the
/// complete symbol table. Conditionals must therefore be resolvable from constants alone, which is
/// true for the ship sources, where the only flags used in conditions are the build options.
/// </summary>
internal sealed class Assembler
{
    private readonly string _root;
    private readonly Dictionary<string, long> _symbols = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MacroDefinition> _macros = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LabelInfo> _labels = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _errors = [];
    private readonly List<string> _warnings = [];
    private readonly List<byte> _image = [];
    private readonly List<string> _includeStack = [];

    private int _origin = -1;
    private int _pc;
    private int _guard = -1;
    private bool _finalPass;
    private int _macroDepth;

    public Assembler(string libraryRoot) => _root = Path.GetFullPath(libraryRoot);

    /// <summary>Assembles <paramref name="mainFile"/> (relative to the library root) twice.</summary>
    public AssemblyResult Assemble(string mainFile, IReadOnlyDictionary<string, long>? initialSymbols = null)
    {
        ResetState();
        if (initialSymbols is not null)
        {
            foreach ((string name, long value) in initialSymbols)
            {
                _symbols[name] = value;
            }
        }

        _finalPass = false;
        ProcessFile(mainFile, new Scope(this, null), new Frames { Owned = true });

        var pass1Symbols = new Dictionary<string, long>(_symbols, StringComparer.OrdinalIgnoreCase);
        var pass1Labels = new Dictionary<string, LabelInfo>(_labels, StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<string> pass1Warnings = [.. _warnings];

        ResetState();
        foreach ((string name, long value) in pass1Symbols)
        {
            _symbols[name] = value;
        }

        _finalPass = true;
        ProcessFile(mainFile, new Scope(this, null), new Frames { Owned = true });

        foreach (string warning in pass1Warnings)
        {
            _warnings.Add(warning);
        }

        if (_errors.Count > 0)
        {
            var message = new StringBuilder();
            message.Append($"failed to assemble '{mainFile}':");
            foreach (string error in _errors.Take(20))
            {
                message.Append("\n  ").Append(error);
            }

            if (_errors.Count > 20)
            {
                message.Append($"\n  ... and {_errors.Count - 20} more");
            }

            throw new AsmException(message.ToString());
        }

        return new AssemblyResult
        {
            MainFile = mainFile,
            BaseAddress = _origin < 0 ? 0 : _origin,
            Image = [.. _image],
            Labels = pass1Labels,
            Symbols = new Dictionary<string, long>(_symbols, StringComparer.OrdinalIgnoreCase),
            Warnings = [.. _warnings],
        };
    }

    private void ResetState()
    {
        _symbols.Clear();
        _macros.Clear();
        _labels.Clear();
        _errors.Clear();
        _warnings.Clear();
        _image.Clear();
        _includeStack.Clear();
        _origin = -1;
        _pc = 0;
        _guard = -1;
        _macroDepth = 0;
    }

    private void ProcessFile(string path, Scope scope, Frames frames)
    {
        string full = ResolvePath(path);
        if (!File.Exists(full))
        {
            Error(null, $"included file not found: {path} (looked in {full})");
            return;
        }

        if (_includeStack.Contains(full, StringComparer.OrdinalIgnoreCase))
        {
            Error(null, $"circular INCLUDE of {path}");
            return;
        }

        _includeStack.Add(full);
        try
        {
            ProcessLines(ReadLines(full), scope, frames);
        }
        finally
        {
            _includeStack.RemoveAt(_includeStack.Count - 1);
        }
    }

    private static List<AsmLine> ReadLines(string fullPath)
    {
        var lines = new List<AsmLine>();
        string[] raw = File.ReadAllLines(fullPath);
        for (int i = 0; i < raw.Length; i++)
        {
            lines.Add(new AsmLine(fullPath, i + 1, raw[i]));
        }

        return lines;
    }

    private void ProcessLines(List<AsmLine> lines, Scope scope, Frames frames)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            AsmLine line = lines[i];
            string code = line.Code.Trim();
            if (code.Length == 0)
            {
                continue;
            }

            List<string> tokens = AsmText.SplitTokens(code);
            string keyword = tokens[0].ToUpperInvariant();

            if (keyword == "MACRO")
            {
                int end = FindEndMacro(lines, i + 1);
                if (frames.Active)
                {
                    DefineMacro(lines, i, end, code);
                }

                i = end;
                continue;
            }

            if (keyword is "IF" or "ELIF" or "ELSE" or "ENDIF")
            {
                HandleConditional(keyword, code[tokens[0].Length..], line, frames, scope);
                continue;
            }

            if (!frames.Active)
            {
                continue;
            }

            // Strip any leading .label definitions (a line may define a label and then emit data).
            while (code.StartsWith('.'))
            {
                int end = 1;
                while (end < code.Length && AsmText.IsSymbolChar(code[end]))
                {
                    end++;
                }

                string labelName = code[1..end];
                if (labelName.Length == 0)
                {
                    Error(line, "malformed label");
                    code = string.Empty;
                    break;
                }

                DefineLabel(labelName, line);
                code = code[end..].Trim();
            }

            if (code.Length == 0)
            {
                continue;
            }

            tokens = AsmText.SplitTokens(code);
            string directive = tokens[0];
            string upper = directive.ToUpperInvariant();
            string rest = code[directive.Length..].Trim();

            if (_macros.TryGetValue(directive, out MacroDefinition? macro))
            {
                ExpandMacro(macro, rest, line, scope);
                continue;
            }

            switch (upper)
            {
                case "EQUB":
                    EmitBytes(rest, line, scope, 1);
                    break;

                case "EQUW":
                    EmitBytes(rest, line, scope, 2);
                    break;

                case "EQUD":
                    EmitBytes(rest, line, scope, 4);
                    break;

                case "EQUS":
                    EmitString(rest, line, scope);
                    break;

                case "SKIP":
                {
                    long count = Eval(rest, scope, line);
                    for (long n = 0; n < count; n++)
                    {
                        EmitByte(0, line);
                    }

                    break;
                }

                case "ORG":
                    _pc = (int)Eval(rest, scope, line);
                    if (_image.Count > 0)
                    {
                        Error(line, "ORG after data has been emitted is not supported");
                    }
                    else
                    {
                        _origin = _pc;
                    }

                    break;

                case "GUARD":
                    _guard = (int)Eval(rest, scope, line);
                    break;

                case "INCLUDE":
                    ProcessFile(Unquote(rest), scope, frames);
                    break;

                case "PRINT":
                case "SAVE":
                    // Output directives are irrelevant to data extraction.
                    break;

                default:
                {
                    int equals = FindAssignment(code);
                    if (equals > 0)
                    {
                        string name = code[..equals].Trim();
                        string expression = code[(equals + 1)..].Trim();
                        scope.Set(name, Eval(expression, scope, line));
                    }
                    else
                    {
                        Error(line, $"unsupported directive or statement: {code}");
                    }

                    break;
                }
            }
        }

        if (frames.Count > 0 && frames.Owned)
        {
            Error(null, $"unterminated IF block (opened at {frames.Top.OpenedAt})");
        }
    }

    private static int FindEndMacro(List<AsmLine> lines, int start)
    {
        int depth = 1;
        for (int i = start; i < lines.Count; i++)
        {
            string code = lines[i].Code.Trim();
            if (code.Length == 0)
            {
                continue;
            }

            string keyword = AsmText.SplitTokens(code)[0].ToUpperInvariant();
            if (keyword == "MACRO")
            {
                depth++;
            }
            else if (keyword == "ENDMACRO")
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return lines.Count - 1;
    }

    private void DefineMacro(List<AsmLine> lines, int start, int end, string code)
    {
        string header = code[5..].Trim(); // strip "MACRO"
        if (header.Length == 0)
        {
            Error(lines[start], "MACRO without a name");
            return;
        }

        List<string> headerTokens = AsmText.SplitTokens(header);
        string name = headerTokens[0];
        string argumentText = header[name.Length..].Trim();
        List<string> parameters = [.. AsmText.SplitArguments(argumentText)
            .Select(argument => argument.Trim())
            .Where(argument => argument.Length > 0)];

        var body = new List<AsmLine>();
        for (int i = start + 1; i < end; i++)
        {
            body.Add(lines[i]);
        }

        _macros[name] = new MacroDefinition(name, parameters, body);
    }

    private void ExpandMacro(MacroDefinition macro, string argumentText, AsmLine line, Scope callerScope)
    {
        if (_macroDepth > 32)
        {
            Error(line, $"macro expansion too deep in {macro.Name}");
            return;
        }

        List<string> rawArguments = argumentText.Length == 0
            ? []
            : [.. AsmText.SplitArguments(argumentText)];

        if (rawArguments.Count != macro.Parameters.Count)
        {
            Error(line, $"macro {macro.Name} expects {macro.Parameters.Count} arguments but got {rawArguments.Count}");
            return;
        }

        var scope = new Scope(this, callerScope);
        for (int i = 0; i < macro.Parameters.Count; i++)
        {
            string argument = rawArguments[i].Trim();
            long value = 0;
            if (argument.Length > 0)
            {
                value = Eval(argument, callerScope, line);
            }

            scope.Set(macro.Parameters[i], value);
        }

        _macroDepth++;
        try
        {
            Frames frames = new() { Owned = true };
            ProcessLines(macro.Body, scope, frames);
        }
        finally
        {
            _macroDepth--;
        }
    }

    /// <summary>Handles one of IF/ELIF/ELSE/ENDIF.</summary>
    private void HandleConditional(string keyword, string rest, AsmLine line, Frames frames, Scope scope)
    {
        switch (keyword)
        {
            case "IF":
            {
                bool parentActive = frames.Active;
                bool active = parentActive && Eval(rest, scope, line, warnUnknown: true) != 0;
                frames.Push(new CondFrame
                {
                    ParentActive = parentActive,
                    Active = active,
                    AnyTaken = active,
                    OpenedAt = line.ToString(),
                });
                break;
            }

            case "ELIF":
            {
                if (frames.Count == 0)
                {
                    Error(line, "ELIF without IF");
                    break;
                }

                CondFrame frame = frames.Top;
                if (frame.ElseSeen)
                {
                    Error(line, "ELIF after ELSE");
                    break;
                }

                bool active = false;
                if (frame.ParentActive && !frame.AnyTaken)
                {
                    active = Eval(rest, scope, line, warnUnknown: true) != 0;
                    frame.AnyTaken |= active;
                }

                frame.Active = active;
                frames.Recompute();
                break;
            }

            case "ELSE":
            {
                if (frames.Count == 0)
                {
                    Error(line, "ELSE without IF");
                    break;
                }

                CondFrame frame = frames.Top;
                frame.Active = frame.ParentActive && !frame.AnyTaken;
                frame.AnyTaken |= frame.Active;
                frame.ElseSeen = true;
                frames.Recompute();
                break;
            }

            case "ENDIF":
                if (frames.Count == 0)
                {
                    Error(line, "ENDIF without IF");
                }
                else
                {
                    frames.Pop();
                }

                break;
        }
    }

    private void DefineLabel(string name, AsmLine line)
    {
        if (_finalPass && _symbols.TryGetValue(name, out long existing) && existing != _pc)
        {
            Error(line, $"label {name} moved between passes (&{existing:X4} -> &{_pc:X4})");
        }

        _labels[name] = new LabelInfo(name, _pc, line.File, line.LineNumber);
        _symbols[name] = _pc;
    }

    private void EmitBytes(string argumentText, AsmLine line, Scope scope, int size)
    {
        foreach (string argument in AsmText.SplitArguments(argumentText))
        {
            string expression = argument.Trim();
            if (expression.Length == 0)
            {
                Error(line, "empty data value");
                continue;
            }

            long value = Eval(expression, scope, line);
            switch (size)
            {
                case 1:
                    if (value is < -128 or > 255)
                    {
                        Error(line, $"EQUB value {value} does not fit in a byte");
                    }

                    EmitByte((int)(value & 0xFF), line);
                    break;

                case 2:
                    if (value is < -32768 or > 65535)
                    {
                        Error(line, $"EQUW value {value} does not fit in a word");
                    }

                    EmitByte((int)(value & 0xFF), line);
                    EmitByte((int)((value >> 8) & 0xFF), line);
                    break;

                default:
                    for (int i = 0; i < 4; i++)
                    {
                        EmitByte((int)((value >> (8 * i)) & 0xFF), line);
                    }

                    break;
            }
        }
    }

    private void EmitString(string argumentText, AsmLine line, Scope scope)
    {
        foreach (string argument in AsmText.SplitArguments(argumentText))
        {
            string text = argument.Trim();
            if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
            {
                foreach (char c in text[1..^1])
                {
                    if (c > 0x7F)
                    {
                        Error(line, "EQUS only supports ASCII strings");
                    }

                    EmitByte(c & 0x7F, line);
                }
            }
            else if (text.Length > 0)
            {
                EmitByte((int)(Eval(text, scope, line) & 0xFF), line);
            }
        }
    }

    private void EmitByte(int value, AsmLine line)
    {
        if (_origin < 0)
        {
            _origin = _pc;
        }

        int expected = _pc - _origin;
        if (expected < _image.Count)
        {
            Error(line, "overlapping data emission");
            return;
        }

        while (_image.Count < expected)
        {
            _image.Add(0);
        }

        _image.Add((byte)value);
        _pc++;

        if (_guard >= 0 && _pc > _guard)
        {
            Error(line, $"assembly has crossed the GUARD address &{_guard:X4}");
        }
    }

    private long Eval(string expression, Scope scope, AsmLine line, bool warnUnknown = false)
    {
        var evaluator = new ExpressionEvaluator(name =>
            scope.TryGet(name, out long value) ? value : null)
        {
            Strict = _finalPass,
        };

        try
        {
            long result = evaluator.Evaluate(expression);
            if (!_finalPass && warnUnknown)
            {
                foreach (string unknown in evaluator.UnknownSymbols)
                {
                    _warnings.Add($"{line}: pass 1 could not resolve '{unknown}' (assuming 0)");
                }
            }

            return result;
        }
        catch (AsmException exception)
        {
            Error(line, exception.Message);
            return 0;
        }
    }

    private void Error(AsmLine? line, string message)
    {
        string prefix = line is null ? string.Empty : $"{line}: ";
        string text = prefix + message;
        if (_finalPass)
        {
            _errors.Add(text);
        }
        else
        {
            _warnings.Add(text);
        }
    }

    private static int FindAssignment(string code)
    {
        int depth = 0;
        bool inString = false;
        for (int i = 0; i < code.Length; i++)
        {
            char c = code[i];
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
                    case '<' when i + 1 < code.Length && code[i + 1] is '=' or '>':
                    case '>' when i + 1 < code.Length && code[i + 1] == '=':
                        i++;
                        break;
                    case '=' when depth == 0:
                        // Only treat this as an assignment if the left-hand side is a bare symbol.
                        string left = code[..i].Trim();
                        return left.Length > 0 && left.All(AsmText.IsSymbolChar) && AsmText.IsSymbolStart(left[0])
                            ? i
                            : -1;
                }
            }
        }

        return -1;
    }

    private string ResolvePath(string path)
    {
        string unquoted = Unquote(path);
        return Path.IsPathRooted(unquoted)
            ? Path.GetFullPath(unquoted)
            : Path.GetFullPath(Path.Combine(_root, unquoted));
    }

    private static string Unquote(string text)
    {
        string trimmed = text.Trim();
        return trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"'
            ? trimmed[1..^1]
            : trimmed;
    }

    private sealed record MacroDefinition(string Name, List<string> Parameters, List<AsmLine> Body);

    private sealed class CondFrame
    {
        public bool ParentActive { get; init; }

        public bool Active { get; set; }

        public bool AnyTaken { get; set; }

        public bool ElseSeen { get; set; }

        public string OpenedAt { get; init; } = string.Empty;
    }

    private sealed class Frames
    {
        private readonly List<CondFrame> _frames = [];

        public bool Owned { get; init; }

        public bool Active { get; private set; } = true;

        public int Count => _frames.Count;

        public CondFrame Top => _frames[^1];

        public void Push(CondFrame frame)
        {
            _frames.Add(frame);
            Recompute();
        }

        public void Pop()
        {
            _frames.RemoveAt(_frames.Count - 1);
            Recompute();
        }

        public void Recompute() => Active = _frames.All(frame => frame.Active);
    }

    private sealed class Scope
    {
        private readonly Assembler _owner;
        private readonly Scope? _parent;
        private readonly Dictionary<string, long> _locals = new(StringComparer.OrdinalIgnoreCase);

        public Scope(Assembler owner, Scope? parent)
        {
            _owner = owner;
            _parent = parent;
        }

        public void Set(string name, long value)
        {
            if (_parent is null)
            {
                _owner._symbols[name] = value;
            }
            else
            {
                _locals[name] = value;
            }
        }

        public bool TryGet(string name, out long value)
        {
            if (_parent is not null)
            {
                if (_locals.TryGetValue(name, out value))
                {
                    return true;
                }

                return _parent.TryGet(name, out value);
            }

            return _owner._symbols.TryGetValue(name, out value);
        }
    }
}
