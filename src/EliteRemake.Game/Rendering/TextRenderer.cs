using EliteRemake.Core.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EliteRemake.Game.Rendering;

/// <summary>
/// Draws text with the game's 8x8 bitmap font.
/// </summary>
/// <remarks>
/// The font is baked into a single-row texture atlas once, so drawing a string is a handful of
/// sprite draws with source rectangles. Text is always drawn on whole-pixel boundaries at an
/// integer scale, which is what keeps it crisp and in keeping with the original's chunky
/// eight-by-eight characters.
/// </remarks>
public sealed class TextRenderer : IDisposable
{
    private const int AtlasCharacters = 128;

    private readonly Texture2D _atlas;
    private readonly BitmapFont _font;

    public TextRenderer(GraphicsDevice device, BitmapFont font)
    {
        _font = font;

        int width = AtlasCharacters * BitmapFont.CellWidth;
        var pixels = new Color[width * BitmapFont.CellHeight];

        for (int code = 0; code < AtlasCharacters; code++)
        {
            if (!font.HasGlyph((char)code))
            {
                continue;
            }

            ReadOnlySpan<byte> glyph = font.GetGlyph((char)code);
            for (int row = 0; row < BitmapFont.CellHeight; row++)
            {
                byte bits = glyph[row];
                for (int column = 0; column < BitmapFont.CellWidth; column++)
                {
                    if ((bits & (0x80 >> column)) != 0)
                    {
                        int x = (code * BitmapFont.CellWidth) + column;
                        pixels[(row * width) + x] = Color.White;
                    }
                }
            }
        }

        _atlas = new Texture2D(device, width, BitmapFont.CellHeight);
        _atlas.SetData(pixels);
    }

    /// <summary>The width of one character cell at the given scale.</summary>
    public static int CellWidth(int scale) => BitmapFont.CellWidth * scale;

    /// <summary>The height of one character cell at the given scale.</summary>
    public static int CellHeight(int scale) => BitmapFont.CellHeight * scale;

    /// <summary>The size of a string at the given scale, in pixels.</summary>
    public static Point Measure(string text, int scale) =>
        new(text.Length * CellWidth(scale), CellHeight(scale));

    /// <summary>
    /// Draws a string with its top-left corner at the given position. Characters the font does not
    /// define are drawn as a question mark by <see cref="BitmapFont.GetGlyph"/>.
    /// </summary>
    public void Draw(SpriteBatch spriteBatch, string text, int x, int y, int scale, Color colour)
    {
        int cellWidth = CellWidth(scale);
        int cellHeight = CellHeight(scale);
        int cursor = x;

        foreach (char character in text)
        {
            if (character == ' ')
            {
                cursor += cellWidth;
                continue;
            }

            int code = character < AtlasCharacters ? character : '?';
            var source = new Rectangle(code * BitmapFont.CellWidth, 0, BitmapFont.CellWidth, BitmapFont.CellHeight);
            spriteBatch.Draw(
                _atlas,
                new Rectangle(cursor, y, cellWidth, cellHeight),
                source,
                colour);

            cursor += cellWidth;
        }
    }

    /// <summary>Draws a string centred horizontally within the given width.</summary>
    public void DrawCentred(SpriteBatch spriteBatch, string text, int centreX, int y, int scale, Color colour) =>
        Draw(spriteBatch, text, centreX - (Measure(text, scale).X / 2), y, scale, colour);

    public void Dispose() => _atlas.Dispose();
}
