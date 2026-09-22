using System.Reflection;
using EliteRemake.Core.Text;

namespace EliteRemake.Data;

/// <summary>
/// The game's bitmap font, loaded from the embedded text description.
/// </summary>
public static class FontData
{
    /// <summary>The logical name of the embedded font resource.</summary>
    public const string ResourceName = "EliteRemake.Data.data.font8x8.txt";

    private static readonly Lazy<BitmapFont> LazyFont = new(Load);

    /// <summary>The font, parsed once on first use.</summary>
    public static BitmapFont Font => LazyFont.Value;

    private static BitmapFont Load()
    {
        Assembly assembly = typeof(FontData).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"The font resource {ResourceName} is missing. Known resources: " +
                string.Join(", ", assembly.GetManifestResourceNames()));
        }

        using var reader = new StreamReader(stream);
        return BitmapFont.Parse(reader.ReadToEnd());
    }
}
