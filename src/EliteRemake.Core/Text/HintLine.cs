namespace EliteRemake.Core.Text;

/// <summary>
/// Works out the scale a footer line of key hints has to be drawn at to fit the space it has.
/// </summary>
/// <remarks>
/// This lives in the core rather than in the renderer so it can be tested: it is arithmetic about
/// how much room a string needs, not about drawing one. The screens found their hints running off
/// the edge at small window sizes, and the reason was that the hints had been allowed to grow longer
/// than the original's terse ones, so the rule is worth being able to check.
/// </remarks>
public static class HintLine
{
    /// <summary>
    /// The scale to draw a hint at so that it fits, never smaller than one.
    /// </summary>
    /// <param name="length">How many characters the hint has.</param>
    /// <param name="scale">The scale it would like to be drawn at.</param>
    /// <param name="cellWidth">The width of one character cell at that scale.</param>
    /// <param name="availableWidth">How much room there is, in pixels.</param>
    /// <remarks>
    /// The floor of one is deliberate: a line that cannot be read is worse than one that is cut off,
    /// because at least a cut-off line can still be recognised. Keeping the floor means the real
    /// answer to a hint that does not fit is a shorter hint, which is why the screens' hints are
    /// written tersely.
    /// </remarks>
    public static int Fit(int length, int scale, int cellWidth, int availableWidth)
    {
        if (length <= 0 || cellWidth <= 0 || availableWidth <= 0)
        {
            return Math.Max(1, scale);
        }

        int width = length * cellWidth;
        if (width <= availableWidth)
        {
            return Math.Max(1, scale);
        }

        return Math.Max(1, (scale * availableWidth) / width);
    }
}
