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
              --screenshot <path>   write a PNG of frame --frame and exit
              --frame <n>           frame to capture (default 60)
            """);
    }
}
