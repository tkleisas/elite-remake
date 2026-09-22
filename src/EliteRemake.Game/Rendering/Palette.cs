using Microsoft.Xna.Framework;

namespace EliteRemake.Game.Rendering;

/// <summary>
/// The colours of the remake, taken from the BBC Micro's palette.
/// </summary>
/// <remarks>
/// BBC Micro Elite runs in a four-colour mode, so the game's palette is built from the BBC's
/// logical colours: black, red, yellow and white for the space view, with green and cyan available
/// for the dashboard. The solid ships keep the original's white hulls, with shading doing the work
/// that the original's single-colour wireframes could not.
/// </remarks>
public static class Palette
{
    /// <summary>The space view background.</summary>
    public static readonly Color Space = new(0, 0, 0);

    /// <summary>BBC colour 0: black.</summary>
    public static readonly Color Black = new(0, 0, 0);

    /// <summary>BBC colour 1: red.</summary>
    public static readonly Color Red = new(255, 0, 0);

    /// <summary>BBC colour 2: green.</summary>
    public static readonly Color Green = new(0, 255, 0);

    /// <summary>BBC colour 3: yellow.</summary>
    public static readonly Color Yellow = new(255, 255, 0);

    /// <summary>BBC colour 4: blue.</summary>
    public static readonly Color Blue = new(0, 0, 255);

    /// <summary>BBC colour 5: magenta.</summary>
    public static readonly Color Magenta = new(255, 0, 255);

    /// <summary>BBC colour 6: cyan.</summary>
    public static readonly Color Cyan = new(0, 255, 255);

    /// <summary>BBC colour 7: white.</summary>
    public static readonly Color White = new(255, 255, 255);

    /// <summary>The hull colour of a standard ship.</summary>
    public static readonly Color Hull = new(228, 232, 240);

    /// <summary>The hull colour of a police ship.</summary>
    public static readonly Color PoliceHull = new(206, 220, 255);

    /// <summary>The hull colour of a pirate ship.</summary>
    public static readonly Color PirateHull = new(240, 214, 190);

    /// <summary>The hull colour of a space station.</summary>
    public static readonly Color StationHull = new(214, 218, 224);

    /// <summary>The colour of an explosion.</summary>
    public static readonly Color Explosion = new(255, 196, 96);

    /// <summary>The colour of a laser beam.</summary>
    public static readonly Color Laser = new(255, 64, 64);
}
