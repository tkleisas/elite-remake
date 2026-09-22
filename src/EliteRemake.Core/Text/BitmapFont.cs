namespace EliteRemake.Core.Text;

/// <summary>
/// An 8x8 monospace bitmap font, as used by the original for all of its text.
/// </summary>
/// <remarks>
/// The original renders text into an 8x8 character cell: the glyph occupies the top seven rows and
/// the eighth row is left for descenders, which is why a 256-pixel-wide screen holds 32 columns.
/// This class parses a font from a plain text description so the glyphs stay readable and
/// reviewable in the repository, and validates it strictly so a broken font fails loudly rather
/// than rendering rubbish.
/// </remarks>
public sealed class BitmapFont
{
    /// <summary>The width of a character cell in pixels.</summary>
    public const int CellWidth = 8;

    /// <summary>The height of a character cell in pixels.</summary>
    public const int CellHeight = 8;

    /// <summary>The character used in the font file for a pixel that is switched on.</summary>
    public const char OnPixel = '#';

    private const int MaxCharacters = 128;

    private readonly byte[]?[] _glyphs = new byte[MaxCharacters][];

    private BitmapFont()
    {
    }

    /// <summary>The number of characters defined in the font.</summary>
    public int GlyphCount { get; private set; }

    /// <summary>The lowest character code in the font.</summary>
    public int FirstCharacter { get; private set; }

    /// <summary>The highest character code in the font.</summary>
    public int LastCharacter { get; private set; }

    /// <summary>True if the font has a glyph for the given character.</summary>
    public bool HasGlyph(char character) =>
        character < MaxCharacters && _glyphs[character] is not null;

    /// <summary>
    /// Gets the glyph for a character: eight bytes, one per row, with bit 7 the leftmost pixel.
    /// Undefined characters fall back to a question mark, and then to a blank cell.
    /// </summary>
    public ReadOnlySpan<byte> GetGlyph(char character)
    {
        if (character < MaxCharacters && _glyphs[character] is { } glyph)
        {
            return glyph;
        }

        if (character != '?' && _glyphs['?'] is { } questionMark)
        {
            return questionMark;
        }

        return new byte[CellHeight];
    }

    /// <summary>Parses a font from its text description.</summary>
    /// <exception cref="FormatException">The description is malformed.</exception>
    public static BitmapFont Parse(string text)
    {
        var font = new BitmapFont();
        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        int count = 0;
        int first = int.MaxValue;
        int last = -1;
        int index = 0;

        while (index < lines.Length)
        {
            string line = lines[index].TrimEnd();
            index++;

            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            if (!char.IsAsciiDigit(line[0]))
            {
                throw new FormatException($"Expected a character code at line {index}, found \"{line}\"");
            }

            int space = line.IndexOf(' ');
            string codeText = space < 0 ? line : line[..space];
            if (!int.TryParse(codeText, out int code) || code is < 0 or >= MaxCharacters)
            {
                throw new FormatException($"Invalid character code \"{codeText}\" at line {index}");
            }

            var glyph = new byte[CellHeight];
            for (int row = 0; row < CellHeight; row++)
            {
                if (index >= lines.Length)
                {
                    throw new FormatException($"Character {code} has fewer than {CellHeight} rows");
                }

                string rowText = lines[index].TrimEnd();
                index++;

                if (rowText.Length != CellWidth)
                {
                    throw new FormatException(
                        $"Character {code} row {row} is {rowText.Length} pixels wide, expected {CellWidth}: \"{rowText}\"");
                }

                byte bits = 0;
                for (int column = 0; column < CellWidth; column++)
                {
                    char pixel = rowText[column];
                    if (pixel == OnPixel)
                    {
                        bits |= (byte)(0x80 >> column);
                    }
                    else if (pixel != '.')
                    {
                        throw new FormatException(
                            $"Character {code} row {row} has an unexpected pixel '{pixel}' (use '{OnPixel}' or '.')");
                    }
                }

                glyph[row] = bits;
            }

            if (font._glyphs[code] is not null)
            {
                throw new FormatException($"Character {code} is defined more than once");
            }

            font._glyphs[code] = glyph;
            count++;
            first = Math.Min(first, code);
            last = Math.Max(last, code);
        }

        if (count == 0)
        {
            throw new FormatException("The font contains no characters");
        }

        font.GlyphCount = count;
        font.FirstCharacter = first;
        font.LastCharacter = last;
        return font;
    }
}
