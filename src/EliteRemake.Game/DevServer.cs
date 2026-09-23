using System.Net;
using System.Text;

namespace EliteRemake.Game;

/// <summary>
/// The development harness: a small HTTP server that drives the live game from outside, so a
/// command line can advance the simulation, act on it and look at it at its own pace — instead of
/// restarting the whole game and replaying to a frame number for every observation.
/// </summary>
/// <remarks>
/// <para>
/// It is started only by the <c>--dev-server</c> command-line option, listens on the loopback
/// interface alone, and does nothing a player would meet: the game is driven by the same update and
/// draw loop it always had, and the server's requests are work for that loop to do, not a second way
/// in.
/// </para>
/// <para>
/// The commands are:
///
/// <list type="bullet">
/// <item><c>GET /status</c> — the scene's own status line, the mode we are in, and the simulation's
/// main loop counter.</item>
/// <item><c>POST /step?frames=n</c> — advances the game by n drawn frames, running a full frame each
/// time against a fixed sixtieth of a second, so the simulation moves on without waiting for real
/// time to. The reply comes when the frames have been run.</item>
/// <item><c>POST /screenshot?path=file</c> — writes the next drawn frame to a PNG.</item>
/// <item><c>POST /launch</c>, <c>POST /dock</c>, <c>POST /autopilot?on=true|false</c>,
/// <c>POST /pause?on=true|false</c> — the actions the game's own keys take, for a harness that
/// wants to fly without a keyboard.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class DevServer : IDisposable
{
    private readonly EliteGame _game;
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly ManualResetEventSlim _done = new(initialState: false);
    private readonly object _gate = new();
    private readonly int _port;

    /// <summary>How many drawn frames the shell still owes the last step request.</summary>
    private int _pendingFrames;

    /// <summary>Which action the shell still owes, and whether it asked for the state on.</summary>
    private DevAction _pendingAction;

    private bool _pendingActionOn;

    /// <summary>Where the next screenshot goes, until Draw has written it.</summary>
    private string? _screenshotPath;

    private enum DevAction
    {
        None,
        Launch,
        Dock,
        Autopilot,
        Pause,
    }

    public DevServer(EliteGame game, int port)
    {
        _game = game;
        _port = port;
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
    }

    public void Start()
    {
        _listener.Start();
        var worker = new Thread(() => Listen(_stopping.Token))
        {
            IsBackground = true,
            Name = "elite-dev-server",
        };
        worker.Start();
        Console.WriteLine($"Dev server on http://127.0.0.1:{_port}/");
    }

    private async void Listen(CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().WaitAsync(stopping);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                continue; // a broken request is not a broken server
            }

            try
            {
                Handle(context);
            }
            catch (Exception error)
            {
                Reply(context, 500, $"dev server error: {error.Message}\n");
            }
        }
    }

    private void Handle(HttpListenerContext context)
    {
        string path = context.Request.Url?.AbsolutePath ?? "/";
        Dictionary<string, string> query = ParseQuery(context.Request.Url?.Query);

        switch (path)
        {
            case "/status":
                Reply(context, 200, _game.DevStatus());
                return;

            case "/step":
            {
                int frames = Math.Clamp(
                    int.Parse(Value(query, "frames", "1")), 0, 5000);
                string? shot = Value(query, "screenshot", string.Empty) is { Length: > 0 } target ? target : null;

                // The shell runs the frames from its own update; this waits for it, so a client's
                // step-then-ask sequence is synchronous. A screenshot named with the step is taken
                // at the end of the run, so a mid-sequence frame can be caught atomically.
                lock (_gate)
                {
                    _pendingFrames = frames;
                    if (shot is not null)
                    {
                        _screenshotPath = shot;
                    }

                    _done.Reset();
                }

                _done.Wait(TimeSpan.FromSeconds(120));
                Reply(context, 200, _game.DevStatus());
                return;
            }

            case "/screenshot":
            {
                string target = Value(query, "path", "screenshot.png");

                // The back buffer holds the last drawn frame; Draw writes it and signals
                lock (_gate)
                {
                    _screenshotPath = target;
                    _done.Reset();
                }

                _done.Wait(TimeSpan.FromSeconds(120));
                Reply(context, 200, $"wrote {target}\n");
                return;
            }

            case "/launch":
                Post(0, DevAction.Launch, on: true);
                _done.Wait(TimeSpan.FromSeconds(120));
                Reply(context, 200, _game.DevStatus());
                return;

            case "/dock":
                Post(0, DevAction.Dock, on: true);
                _done.Wait(TimeSpan.FromSeconds(120));
                Reply(context, 200, _game.DevStatus());
                return;

            case "/autopilot":
                Post(0, DevAction.Autopilot, Flag(query, "on", true));
                _done.Wait(TimeSpan.FromSeconds(120));
                Reply(context, 200, _game.DevStatus());
                return;

            case "/pause":
                Post(0, DevAction.Pause, Flag(query, "on", true));
                Reply(context, 200, _game.DevStatus());
                return;

            default:
                Reply(context, 404, "no such command\n");
                return;
        }
    }

    /// <summary>Hands a request to the shell, which runs it on its own thread with its next frame.</summary>
    private void Post(int frames, DevAction action, bool on)
    {
        lock (_gate)
        {
            _pendingFrames = frames;
            _pendingAction = action;
            _pendingActionOn = on;
            _done.Reset();
        }
    }

    /// <summary>
    /// Runs the work the shell owes the dev server. Called from the game's own update, on the game's
    /// own thread, so everything the harness asks for runs with the game's own frame logic.
    /// </summary>
    public void RunPendingWork()
    {
        int frames;
        DevAction action;
        bool on;
        lock (_gate)
        {
            frames = _pendingFrames;
            _pendingFrames = 0;
            action = _pendingAction;
            _pendingAction = DevAction.None;
            on = _pendingActionOn;
        }

        if (frames == 0 && action == DevAction.None)
        {
            return;
        }

        switch (action)
        {
            case DevAction.Launch:
                _game.DevLaunch();
                break;
            case DevAction.Dock:
                _game.DevDock();
                break;
            case DevAction.Autopilot:
                _game.DevAutopilot(on);
                break;
            case DevAction.Pause:
                _game.DevPause(on);
                break;
        }

        if (frames > 0)
        {
            _game.Advance(frames);
        }

        // A step that named a screenshot is not done until Draw has written it: the wait must
        // cover the screenshot, or the reply would come before the picture did
        lock (_gate)
        {
            if (_screenshotPath is null)
            {
                _done.Set();
            }
        }
    }

    /// <summary>
    /// Writes the requested screenshot once the frame has been drawn, which is where the back
    /// buffer holds what a screenshot is of. Called from the end of Draw.
    /// </summary>
    public void TakeScreenshotIfRequested()
    {
        string? path;
        lock (_gate)
        {
            path = _screenshotPath;
            _screenshotPath = null;
        }

        if (path is not null)
        {
            _game.SaveScreenshotNow(path);
            _done.Set();
        }
    }

    private static Dictionary<string, string> ParseQuery(string? query)
    {
        var parsed = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(query))
        {
            return parsed;
        }

        foreach (string pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = pair.IndexOf('=');
            string key = equals < 0 ? pair : pair[..equals];
            string value = equals < 0 ? string.Empty : Uri.UnescapeDataString(pair[(equals + 1)..]);
            parsed[key] = value;
        }

        return parsed;
    }

    private static string Value(Dictionary<string, string> query, string key, string fallback) =>
        query.TryGetValue(key, out string? value) ? value : fallback;

    private static bool Flag(Dictionary<string, string> query, string key, bool fallback) =>
        query.TryGetValue(key, out string? value)
            ? value is "true" or "1" or "on"
            : fallback;

    private static void Reply(HttpListenerContext context, int status, string body)
    {
        // A harness client that has gone away must not take the game with it: a failed reply is
        // written to nothing, and the server lives on
        try
        {
            context.Response.StatusCode = status;
            byte[] payload = Encoding.UTF8.GetBytes(body);
            context.Response.ContentLength64 = payload.Length;
            context.Response.OutputStream.Write(payload);
            context.Response.OutputStream.Close();
        }
        catch (Exception)
        {
        }
    }

    public void Dispose()
    {
        _stopping.Cancel();
        try
        {
            _listener.Stop();
            _listener.Close();
        }
        catch (Exception)
        {
        }

        _done.Dispose();
        _stopping.Dispose();
    }
}
