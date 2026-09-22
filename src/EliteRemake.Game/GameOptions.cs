using EliteRemake.Core.Sim;

namespace EliteRemake.Game;

/// <summary>
/// Command line options. The screenshot options exist so rendered frames can be inspected during
/// development (and in automated checks) without a human at the keyboard.
/// </summary>
public sealed class GameOptions
{
    /// <summary>Window width in pixels.</summary>
    public int Width { get; private set; } = 1600;

    /// <summary>Window height in pixels.</summary>
    public int Height { get; private set; } = 900;

    /// <summary>If set, write a PNG of the frame and exit.</summary>
    public string? ScreenshotPath { get; private set; }

    /// <summary>The frame to capture the screenshot on.</summary>
    public int ScreenshotFrame { get; private set; } = 60;

    /// <summary>If set, run the ship viewer for the named ship instead of the game.</summary>
    public string? ViewerShip { get; private set; }

    /// <summary>If set, exit after this many frames even without a screenshot.</summary>
    public int? ExitAfterFrames { get; private set; }

    /// <summary>True to start with the simulation paused.</summary>
    public bool Paused { get; private set; }

    /// <summary>Simulation steps to run before the first frame, for reproducible screenshots.</summary>
    public int WarmupSteps { get; private set; }

    /// <summary>If set, the viewer holds the ship at this heading, in degrees.</summary>
    public float? ViewerHeading { get; private set; }

    /// <summary>If set, the viewer holds the ship at this pitch, in degrees.</summary>
    public float ViewerPitch { get; private set; }

    /// <summary>Distance to place the ship from the camera in the viewer.</summary>
    public float? ViewerDistance { get; private set; }

    /// <summary>How far ahead of us to place the space station in the flight scene.</summary>
    public int StationDistance { get; private set; } = 3000;

    /// <summary>If set, the flight scene starts with an empty system.</summary>
    public bool EmptySystem { get; private set; }

    /// <summary>Number of frames to run the flight simulation before the first frame is drawn.</summary>
    public int SimWarmupFrames { get; private set; }

    /// <summary>Controls to hold down during the simulation warmup, e.g. "left,up".</summary>
    public FlightInput WarmupInput { get; private set; }

    public static GameOptions Parse(string[] args)
    {
        var options = new GameOptions();
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            string? Next() => i + 1 < args.Length ? args[++i] : null;

            switch (arg)
            {
                case "--screenshot":
                    options.ScreenshotPath = Next();
                    break;
                case "--frame":
                    options.ScreenshotFrame = int.Parse(Next() ?? "60");
                    break;
                case "--width":
                    options.Width = int.Parse(Next() ?? "1600");
                    break;
                case "--height":
                    options.Height = int.Parse(Next() ?? "900");
                    break;
                case "--viewer":
                    options.ViewerShip = Next();
                    break;
                case "--exit-after":
                    options.ExitAfterFrames = int.Parse(Next() ?? "60");
                    break;
                case "--paused":
                    options.Paused = true;
                    break;
                case "--warmup":
                    options.WarmupSteps = int.Parse(Next() ?? "0");
                    break;
                case "--viewer-heading":
                    options.ViewerHeading = float.Parse(Next() ?? "0");
                    break;
                case "--viewer-pitch":
                    options.ViewerPitch = float.Parse(Next() ?? "0");
                    break;
                case "--viewer-distance":
                    options.ViewerDistance = float.Parse(Next() ?? "0");
                    break;
                case "--station-distance":
                    options.StationDistance = int.Parse(Next() ?? "6000");
                    break;
                case "--empty":
                    options.EmptySystem = true;
                    break;
                case "--sim-warmup":
                    options.SimWarmupFrames = int.Parse(Next() ?? "0");
                    break;
                case "--hold":
                    options.WarmupInput = ParseControls(Next() ?? string.Empty);
                    break;
                case "--help":
                case "-h":
                    PrintHelp();
                    Environment.Exit(0);
                    break;
                default:
                    Console.Error.WriteLine($"Unknown option: {arg}");
                    PrintHelp();
                    Environment.Exit(2);
                    break;
            }
        }

        return options;
    }

    /// <summary>Turns a comma-separated list of control names into a flight input.</summary>
    public static FlightInput ParseControls(string value)
    {
        var input = new FlightInput();
        foreach (string name in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            input = name.ToLowerInvariant() switch
            {
                "left" => input with { RollLeft = true },
                "right" => input with { RollRight = true },
                "up" => input with { PullUp = true },
                "down" => input with { PitchDown = true },
                "faster" => input with { SpeedUp = true },
                "slower" => input with { SlowDown = true },
                _ => throw new ArgumentException($"Unknown control: {name}"),
            };
        }

        return input;
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            Elite Remake

              --width <px>          window width (default 1600)
              --height <px>         window height (default 900)
              --viewer <ship>       show the ship viewer for a named ship instead of the game
              --paused              start paused
              --warmup <steps>      run this many simulation steps before the first frame
              --exit-after <frames> quit after this many frames
              --viewer-heading <deg>  hold the viewed ship at this heading
              --viewer-pitch <deg>    hold the viewed ship at this pitch
              --viewer-distance <d>   place the viewed ship this far away
              --station-distance <d>  place the space station this far ahead (default 6000)
              --empty                 start the flight scene with an empty system
              --sim-warmup <frames>   run the flight simulation this many frames before drawing
              --hold <controls>       hold controls during the warmup: left, right, up, down,
                                      faster, slower (comma separated)
              --screenshot <path>   write a PNG of frame --frame and exit
              --frame <n>           frame to capture (default 60)
            """);
    }
}
