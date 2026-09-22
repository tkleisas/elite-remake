using EliteRemake.Core.Text;
using Xunit;

namespace EliteRemake.Core.Tests;

/// <summary>
/// Checks the bitmap font that the whole game's text depends on: that it loads, covers the
/// character set the original uses, and that malformed font files are rejected rather than
/// rendering garbage.
/// </summary>
public class BitmapFontTests
{
    [Fact]
    public void TheGameFontCoversPrintableAscii()
    {
        BitmapFont font = Data.FontData.Font;

        Assert.Equal(95, font.GlyphCount);
        Assert.Equal(32, font.FirstCharacter);
        Assert.Equal(126, font.LastCharacter);

        for (char character = ' '; character <= '~'; character++)
        {
            Assert.True(font.HasGlyph(character), $"the font has no glyph for '{character}' ({(int)character})");
            Assert.Equal(BitmapFont.CellHeight, font.GetGlyph(character).Length);
        }
    }

    [Fact]
    public void GlyphsHaveTheExpectedShape()
    {
        BitmapFont font = Data.FontData.Font;

        // 'A' is a triangle on two legs, as drawn in font8x8.txt
        byte[] a = font.GetGlyph('A').ToArray();
        Assert.Equal([0x20, 0x50, 0x88, 0x88, 0xF8, 0x88, 0x88, 0x00], a);

        // The digits are drawn in the same eight-by-eight cell
        byte[] zero = font.GetGlyph('0').ToArray();
        Assert.Equal(0x70, zero[0]);
        Assert.Equal(0x70, zero[6]);

        // Descenders drop into the bottom row: 'g' uses row 7
        byte[] g = font.GetGlyph('g').ToArray();
        Assert.NotEqual(0, g[7]);
    }

    [Fact]
    public void UndefinedCharactersFallBackToAQuestionMark()
    {
        BitmapFont font = Data.FontData.Font;
        Assert.Equal(font.GetGlyph('?').ToArray(), font.GetGlyph('\u00ff').ToArray());
    }

    [Fact]
    public void ParseRejectsARowOfTheWrongWidth()
    {
        var text = "65 A\n" + string.Join('\n', Enumerable.Repeat("..#.....", 7)) + "\n...#...\n";
        FormatException error = Assert.Throws<FormatException>(() => BitmapFont.Parse(text));
        Assert.Contains("pixels wide", error.Message);
    }

    [Fact]
    public void ParseRejectsATruncatedGlyph()
    {
        var text = "65 A\n..#.....\n..#.....\n";
        Assert.Throws<FormatException>(() => BitmapFont.Parse(text));
    }

    [Fact]
    public void ParseRejectsDuplicateCharacters()
    {
        string glyph = string.Join('\n', Enumerable.Repeat("..#.....", 8));
        var text = $"65 A\n{glyph}\n65 A\n{glyph}\n";
        FormatException error = Assert.Throws<FormatException>(() => BitmapFont.Parse(text));
        Assert.Contains("more than once", error.Message);
    }

    [Fact]
    public void ParseIgnoresCommentsAndBlankLines()
    {
        string glyph = string.Join('\n', Enumerable.Repeat("..#.....", 8));
        var text = $"# a comment\n\n65 A\n{glyph}\n\n# another comment\n";
        BitmapFont font = BitmapFont.Parse(text);

        Assert.Equal(1, font.GlyphCount);
        Assert.True(font.HasGlyph('A'));
        Assert.False(font.HasGlyph('B'));
    }

    [Fact]
    public void ParseRejectsAnEmptyFont()
    {
        Assert.Throws<FormatException>(() => BitmapFont.Parse("# nothing here\n"));
    }
}
