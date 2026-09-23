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

/// <summary>
/// Checks the system description generator: that the token table is the original's, and that the
/// printer assembles a description from it.
/// </summary>
/// <remarks>
/// The phrases are chosen at random, as they are in the original — look at the same system twice and
/// you may be told something different — so these tests check the machinery and the vocabulary
/// rather than pinning an exact sentence.
/// </remarks>
/// <summary>
/// The rule that keeps a footer line of key hints inside the view.
/// </summary>
public class HintLineTests
{
    /// <summary>A hint that already fits is left at the scale it asked for.</summary>
    [Fact]
    public void AHintThatFitsIsNotShrunk()
    {
        // 40 characters at 16 pixels a cell is 640 pixels, in 640
        Assert.Equal(2, HintLine.Fit(length: 40, scale: 2, cellWidth: 16, availableWidth: 640));
        Assert.Equal(3, HintLine.Fit(length: 20, scale: 3, cellWidth: 24, availableWidth: 640));
    }

    /// <summary>One that does not fit is scaled down to the largest scale that does.</summary>
    [Fact]
    public void AHintThatDoesNotFitIsShrunkToTheLargestScaleThatDoes()
    {
        // 84 characters is the market screen's hint, which is what prompted this: at 16 pixels a
        // cell that is 1344 pixels, more than twice the 640 it has, so it halves
        Assert.Equal(1, HintLine.Fit(length: 84, scale: 2, cellWidth: 16, availableWidth: 640));

        // A gentler overrun keeps more of the scale
        Assert.Equal(2, HintLine.Fit(length: 50, scale: 3, cellWidth: 24, availableWidth: 1000));
    }

    /// <summary>
    /// A hint that cannot fit at any scale stays at one rather than vanishing or going to zero.
    /// </summary>
    /// <remarks>
    /// The floor is deliberate. A line too small to read is worse than one that is cut off, because
    /// a cut-off line can still be recognised - and keeping the floor means the answer to a hint
    /// that does not fit is to write a shorter hint, which is what the screens now do.
    /// </remarks>
    [Fact]
    public void AHintThatCannotFitStaysAtScaleOne()
    {
        Assert.Equal(1, HintLine.Fit(length: 200, scale: 2, cellWidth: 16, availableWidth: 640));
        Assert.Equal(1, HintLine.Fit(length: 84, scale: 4, cellWidth: 32, availableWidth: 400));
    }

    /// <summary>Degenerate inputs give a usable scale rather than dividing by zero.</summary>
    [Fact]
    public void EmptyInputsDoNotDivideByZero()
    {
        Assert.Equal(2, HintLine.Fit(length: 0, scale: 2, cellWidth: 16, availableWidth: 640));
        Assert.Equal(2, HintLine.Fit(length: 40, scale: 2, cellWidth: 0, availableWidth: 640));
        Assert.Equal(2, HintLine.Fit(length: 40, scale: 2, cellWidth: 16, availableWidth: 0));
    }
}

public class DescriptionTests
{
    [Fact]
    public void TheTokenTableIsTheOriginals()
    {
        var random = new EliteRemake.Core.Sim.EliteRandom(1);
        string description = Data.DescriptionData.Describe("Lave", random);

        Assert.False(string.IsNullOrWhiteSpace(description), "a description should be generated");

        // Every description names the system and says something about it
        Assert.Contains("lave", description, StringComparison.OrdinalIgnoreCase);

        // And it uses the original's vocabulary
        string[] vocabulary =
        [
            "famous", "noted", "known", "fabled", "planet", "world", "cursed", "ravaged",
            "scourged", "beset", "deadly", "dreadful", "wicked", "unusual", "vast",
        ];

        Assert.True(
            vocabulary.Any(word => description.Contains(word, StringComparison.OrdinalIgnoreCase)),
            $"the description should use the original's words: \"{description}\"");
    }

    [Fact]
    public void TheSystemNameIsCapitalisedWhereverItAppears()
    {
        // The name is a proper noun, so it keeps its capital even in the middle of a lower case
        // sentence such as "The planet Lave is ..."
        for (uint seed = 1; seed <= 200; seed++)
        {
            string description = Data.DescriptionData.Describe(
                "LAVE",
                new EliteRemake.Core.Sim.EliteRandom(seed));

            Assert.DoesNotContain("LAVE", description);   // never shouted
            Assert.Contains("Lave", description);          // always capitalised
        }
    }

    [Fact]
    public void DescriptionsVaryWithTheRandomNumberGenerator()
    {
        // The original picks its phrases with the game's random number generator, so different
        // states give different descriptions
        var seen = new HashSet<string>();
        for (uint seed = 1; seed <= 40; seed++)
        {
            seen.Add(Data.DescriptionData.Describe("Lave", new EliteRemake.Core.Sim.EliteRandom(seed)));
        }

        Assert.True(seen.Count > 10, $"40 visits should not all say the same thing, got {seen.Count} variants");
    }

    [Fact]
    public void TheSameStateGivesTheSameDescription()
    {
        string first = Data.DescriptionData.Describe("Lave", new EliteRemake.Core.Sim.EliteRandom(12345));
        string second = Data.DescriptionData.Describe("Lave", new EliteRemake.Core.Sim.EliteRandom(12345));

        Assert.Equal(first, second);
    }

    [Fact]
    public void TheDescriptionNamesWhicheverSystemItIsGiven()
    {
        string description = Data.DescriptionData.Describe("Leesti", new EliteRemake.Core.Sim.EliteRandom(7));
        Assert.Contains("leesti", description, StringComparison.OrdinalIgnoreCase);
    }
}
