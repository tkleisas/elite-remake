using Microsoft.Xna.Framework;

namespace EliteRemake.Game.Rendering;

/// <summary>
/// Works out where the space view and the dashboard go in the window.
/// </summary>
/// <remarks>
/// The original's screen is 256 x 256: a 256 x 192 space view with the dashboard filling the bottom
/// 64 rows, so the dashboard takes a third of the height. The remake keeps those proportions, but
/// draws the space view across the full width of a widescreen window (showing more to the sides,
/// with the same vertical field of view) and the dashboard as a band across the bottom.
/// </remarks>
public readonly struct ScreenLayout
{
    /// <summary>Height of the original's space view.</summary>
    public const int OriginalViewHeight = 192;

    /// <summary>Height of the original's dashboard.</summary>
    public const int OriginalDashboardHeight = 64;

    private ScreenLayout(Rectangle view, Rectangle dashboard, float scale)
    {
        View = view;
        Dashboard = dashboard;
        Scale = scale;
    }

    /// <summary>The area the 3D view is drawn in.</summary>
    public Rectangle View { get; }

    /// <summary>The area the dashboard is drawn in.</summary>
    public Rectangle Dashboard { get; }

    /// <summary>
    /// How many screen pixels one original pixel occupies, based on the height of the whole screen
    /// (view plus dashboard).
    /// </summary>
    public float Scale { get; }

    /// <summary>Builds the layout for a window of the given size.</summary>
    public static ScreenLayout ForWindow(int width, int height)
    {
        float scale = height / (float)(OriginalViewHeight + OriginalDashboardHeight);
        int dashboardHeight = (int)MathF.Round(OriginalDashboardHeight * scale);
        if (dashboardHeight < 24)
        {
            dashboardHeight = Math.Min(24, height / 4);
        }

        var view = new Rectangle(0, 0, width, Math.Max(1, height - dashboardHeight));
        var dashboard = new Rectangle(0, view.Height, width, height - view.Height);
        return new ScreenLayout(view, dashboard, scale);
    }
}
