namespace EliteRemake.Core.Sim;

/// <summary>A ship's position on the 3D scanner, in the original's dashboard coordinates.</summary>
/// <param name="X">The blip's x, where 123 is the centre of the scanner.</param>
/// <param name="Y">The blip's y, where 220 is the centre row and smaller is further ahead.</param>
/// <param name="StickX">Where the ship's stick meets the centre line.</param>
/// <param name="StickY">The centre line's y for this row.</param>
/// <param name="Missile">True for a missile, which the original shows in a different colour.</param>
public readonly record struct ScannerBlip(int X, int Y, int StickX, int StickY, bool Missile);

/// <summary>
/// The 3D scanner: the elliptical radar on the dashboard, which shows the ships around us.
/// </summary>
/// <remarks>
/// The scanner is a view from behind and above. The horizontal axis is left and right, the vertical
/// axis of the ellipse is distance ahead — so ships in front appear near the top — and a ship's
/// height is shown by the "stick": the blip sits above or below the centre line of its row, and a
/// line joins it to that line, so you can read at a glance whether a ship is above you, below you,
/// or level with you.
///
/// The original's SCAN routine works in dashboard coordinates, and these are its numbers exactly:
///
/// <code>
///     x = 123 + x_hi (signed)
///     y = 220 - z_hi/4 (signed) - y_hi/2 (signed), clamped to 194..247
/// </code>
///
/// so the scanner is 128 pixels across and a little over 50 deep, centred on (123, 220). A ship
/// more than 16383 units away on any axis is off the scanner entirely, which is the original's test
/// on bits 6 and 7 of the high bytes.
/// </remarks>
public static class Scanner
{
    /// <summary>The x coordinate of the centre of the scanner, as the original has it.</summary>
    public const int CentreX = 123;

    /// <summary>The y coordinate of the centre row of the scanner.</summary>
    public const int CentreY = 220;

    /// <summary>The top of the scanner.</summary>
    public const int Top = 194;

    /// <summary>The bottom of the scanner.</summary>
    public const int Bottom = 247;

    /// <summary>The half-width of the scanner's ellipse.</summary>
    public const int HalfWidth = 64;

    /// <summary>The half-height of the scanner's ellipse.</summary>
    public const int HalfHeight = 26;

    /// <summary>
    /// The x coordinate of the blip's stick on the centre line, in the original's coordinates.
    /// </summary>
    public static int StickX => CentreX;

    /// <summary>
    /// Projects a ship onto the scanner. Returns null when the ship is too far away to show or is
    /// not flagged for the scanner, which is what the original's SCAN checks before it draws.
    /// </summary>
    public static ScannerBlip? Project(Ship ship)
    {
        // The original only shows ships flagged for the scanner, and never the planet or the sun
        if (!ship.ShowOnScanner || FlightSim.IsCelestial(ship.Type))
        {
            return null;
        }

        int xHi = SignedHighByte(ship, ShipDataBlock.X);
        int yHi = SignedHighByte(ship, ShipDataBlock.Y);
        int zHi = SignedHighByte(ship, ShipDataBlock.Z);

        // Too far away: the original tests bits 6 and 7 of each high byte
        if (Math.Abs(xHi) >= 64 || Math.Abs(yHi) >= 64 || Math.Abs(zHi) >= 64)
        {
            return null;
        }

        int x = CentreX + xHi;

        // Forward is up the scanner, and a ship's height lifts its blip off the centre line
        int row = CentreY - (zHi / 4);
        int y = Math.Clamp(row - (yHi / 2), Top, Bottom);

        // The stick joins the blip to the centre line of its row, from where the ellipse is widest
        int stickX = CentreX;

        return new ScannerBlip(x, y, stickX, row, ship.Type == Missiles.MissileType);
    }

    /// <summary>
    /// True if a scanner coordinate is inside the ellipse, which is where the original only draws
    /// blips that fit on the dashboard.
    /// </summary>
    public static bool IsInside(int x, int y)
    {
        double dx = (x - CentreX) / (double)HalfWidth;
        double dy = (y - CentreY) / (double)HalfHeight;
        return (dx * dx) + (dy * dy) <= 1.0;
    }

    /// <summary>
    /// The half-width of the ellipse at a given row, so a stick can be drawn only as far as the
    /// scanner's edge.
    /// </summary>
    public static int HalfWidthAt(int y)
    {
        double dy = (y - CentreY) / (double)HalfHeight;
        double remaining = 1 - (dy * dy);
        return remaining <= 0 ? 0 : (int)(HalfWidth * Math.Sqrt(remaining));
    }

    /// <summary>
    /// The high byte of one of a ship's coordinates, made negative when the coordinate is. Elite
    /// keeps a coordinate's sign in its own byte rather than in bit 7 of the high byte, so the
    /// magnitude and the sign are read separately — which is exactly what the original's SCAN does.
    /// </summary>
    private static int SignedHighByte(Ship ship, int coordinate)
    {
        int magnitude = ship.Data[coordinate + 1];
        bool negative = (ship.Data[coordinate + 2] & 0x80) != 0;
        return negative ? -magnitude : magnitude;
    }
}
