using EliteRemake.Core.Graphics;
using EliteRemake.Core.Maths;
using EliteRemake.Core.Ships;
using EliteRemake.Core.Sim;
using EliteRemake.Core.Text;
using EliteRemake.Game.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace EliteRemake.Game.Scenes;

/// <summary>
/// A mission briefing, staged the way the disc's docked code stages it: the INCOMING MESSAGE
/// banner, then whatever the briefing has to show — mission 1's rotating Constrictor — and then the
/// token's own text, waiting for a key at each of the points the printer reports.
/// </summary>
/// <remarks>
/// <para>
/// The disc's BRIEF runs this order of business: BRIS prints token 216 — "{clear screen} INCOMING
/// MESSAGE" — and waits two seconds; then the Constrictor is set up at 256 units and rotated for 64
/// iterations of the main loop with roll and pitch counters of &amp;7F, which never damp; then BRL2
/// moves it up the screen and away until it is near the top; and then the briefing text prints over
/// it, with jump token 22 showing the ship and waiting for a key press, and 24 just waiting. Every
/// briefing ends by showing the Status Mode screen, which is BRP's own exit.
/// </para>
/// <para>
/// The printer's <see cref="TokenPrinter.Events"/> report where those actions fall in the printed
/// text, so the staging is done by segmenting the text at each event and turning the page on a key
/// press.
/// </para>
/// </remarks>
public sealed class BriefingScene : IScene
{
    private readonly GameSession _session;
    private readonly TextRenderer _text;
    private readonly MeshRenderer _renderer;
    private readonly string[] _pages;
    private readonly List<TokenEvent> _events;
    private readonly int _shipType;

    private enum Stage
    {
        Banner,
        Rotating,
        Drift,
        Text,
    }

    private Stage _stage = Stage.Banner;
    private float _bannerLeft;
    private float _iterationClock;
    private int _rotations;
    private int _drifts;
    private int _page;
    private string _shownText = string.Empty;
    private KeyboardState _previousKeys;

    /// <summary>How long the banner stays up: the disc's DELAY of 100 at 50 vertical syncs a second.</summary>
    public const float BannerSeconds = 2f;

    /// <summary>How many iterations the briefing ship rotates for: the disc's MCNT of 64.</summary>
    public const int RotationIterations = 64;

    /// <summary>
    /// How many iterations the ship drifts for, which is the disc's BRL2 loop: z_lo takes 128
    /// steps of two to overflow past 255, and y_lo reaches its cap of 112 first.
    /// </summary>
    public const int DriftIterations = 128;

    /// <summary>How many times a second the briefing's own loop runs its iterations.</summary>
    public const float IterationsPerSecond = 30f;

    public BriefingScene(ViewCamera camera, GameSession session, TextRenderer text, GraphicsDevice device)
    {
        Camera = camera;
        _session = session;
        _text = text;
        _renderer = new MeshRenderer(device);

        // The disc's BRIEF prints the banner from BRIS, before the token; the other four texts open
        // with the banner themselves, their first event being jump token 25. Both end at the same
        // place: the banner is up for two seconds, then whatever the briefing has to show
        Briefing pending = session.PendingBriefing!;
        (string printed, IReadOnlyList<TokenEvent> events) = EliteRemake.Data.DescriptionData.MissionTextWithEvents(
            pending.Token,
            session.System.Name,
            session.Commander.Name,
            session.Commander.GalaxyNumber,
            session.DescriptionRandom);

        // The text is cut into pages at the printer's events, and the first IncomingMessage event —
        // the banner — is the staging that happens before the text, not a page of it
        (_pages, _events) = Paginate(printed, events);
        _shownText = _pages[0];

        if (pending.ShipType != 0 && ShipCatalog.ByType(pending.ShipType) is { } missionShip)
        {
            _missionMesh = missionShip.Mesh;

            // The disc's own setup: ZINF resets the workspace, the ship sits at z_hi 1 — 256 units
            // away — and it rotates with roll and pitch counters of &7F, "which doesn't dampen"
            _ship = Ship.Create(pending.ShipType, missionShip.Id, missionShip.Name, 0f, 0f, 0, 0, 256);
            _ship.Data[ShipDataBlock.RollCounter] = 0x7F;
            _ship.Data[ShipDataBlock.PitchCounter] = 0x7F;
        }

        _bannerLeft = BannerSeconds;
        Built = pending;
    }

    private readonly ShipMesh? _missionMesh;
    private Ship? _ship;

    /// <summary>The briefing this scene was built to stage, so a new briefing gets a new scene.</summary>
    public Briefing Built { get; }

    public ViewCamera Camera { get; }

    public string StatusLine =>
        $"Briefing: {_session.System.Name}, token {_session.PendingBriefing?.Token}, " +
        $"stage {_stage}, page {_page + 1} of {_pages.Length}, ship {_ship?.BlueprintId ?? "none"}";

    /// <summary>
    /// Cuts the printed text into pages at the printer's events. An IncomingMessage event at the
    /// very start is the banner itself, which the scene stages before any text; every other event
    /// ends a page, and the text that follows it starts the next one.
    /// </summary>
    private static (string[] Pages, List<TokenEvent> Events) Paginate(
        string text,
        IReadOnlyList<TokenEvent> events)
    {
        var pages = new List<string>();
        var staged = new List<TokenEvent>();
        int start = 0;

        if (events.Count > 0 && events[0].Action == TokenAction.IncomingMessage && events[0].Position <= 1)
        {
            staged.Add(events[0]);
            start = events[0].Position;
            events = events.Skip(1).ToList();
        }

        foreach (TokenEvent stage in events)
        {
            pages.Add(text[start..stage.Position].TrimEnd('\n'));
            staged.Add(stage);
            start = stage.Position;
        }

        pages.Add(text[start..].Trim());

        if (pages[^1].Length == 0 && pages.Count > 1)
        {
            pages.RemoveAt(pages.Count - 1);
        }

        return ([.. pages], staged);
    }

    public void Update(float elapsedSeconds)
    {
        KeyboardState keys = Keyboard.GetState();

        switch (_stage)
        {
            case Stage.Banner:
                // The disc's DELAY: two seconds of the banner, with no key to hurry it
                _bannerLeft -= elapsedSeconds;
                if (_bannerLeft <= 0)
                {
                    _stage = _missionMesh is not null ? Stage.Rotating : Stage.Text;
                }

                break;

            case Stage.Rotating:
                // The disc's BRL1: 64 iterations, each an LL9 and an MVEIT, whose counters never
                // damp — a clockwise roll and a diving pitch
                _iterationClock += elapsedSeconds;
                while (_iterationClock >= 1f / IterationsPerSecond && _rotations < RotationIterations)
                {
                    _iterationClock -= 1f / IterationsPerSecond;
                    _rotations++;
                    if (_ship is { } ship)
                    {
                        ShipMovement.RotateShipAboutItself(ship.Orientation, ship.Data);
                    }
                }

                if (_rotations >= RotationIterations)
                {
                    _stage = Stage.Drift;
                }

                break;

            case Stage.Drift:
                // The disc's BRL2: "INC z_lo ... INC z_lo" moves the ship two low bytes away per
                // iteration and "STX INWK+3" climbs y_lo by one, capped at 112, until the z_lo
                // overflows past 255 and the ship is near the top of the screen at some 500 units.
                // Then the text is shown over it
                _iterationClock += elapsedSeconds;
                while (_iterationClock >= 1f / IterationsPerSecond && _drifts < DriftIterations)
                {
                    _iterationClock -= 1f / IterationsPerSecond;
                    _drifts++;
                    (int x, int y, int z) = _ship!.GetPosition();
                    _ship.SetPosition(x, Math.Min(y + 1, 112), z + 2);
                }

                if (_drifts >= DriftIterations)
                {
                    _stage = Stage.Text;
                    _shownText = _pages[0];
                }

                break;

            case Stage.Text:
                // A key press turns the page, which is the disc's PAUSE: the text waits until a key
                // is pressed. The last page's key shows the Status Mode screen, as BRP does
                bool anyKey = keys.GetPressedKeyCount() > 0 && !_previousKeys.GetPressedKeys().Any();
                if (anyKey)
                {
                    _page++;
                    if (_page >= _pages.Length)
                    {
                        _session.CompleteBriefing();
                        return;
                    }

                    _shownText = _pages[_page];
                }

                break;
        }

        _previousKeys = keys;
    }

    public void Draw(SpriteBatch spriteBatch, Texture2D pixel, GraphicsDevice device)
    {
        device.Clear(Palette.Space);

        int scale = Math.Max(2, (int)(device.Viewport.Height / 320f));
        int cellWidth = TextRenderer.CellWidth(scale);
        int cellHeight = TextRenderer.CellHeight(scale);

        spriteBatch.Begin(samplerState: SamplerState.PointClamp);

        if (_stage is Stage.Banner or Stage.Rotating or Stage.Drift)
        {
            if (_stage == Stage.Banner)
            {
                // The disc's own banner: "{tab 6}{move to row 10, white}{all caps}INCOMING MESSAGE"
                _text.DrawCentred(
                    spriteBatch,
                    "INCOMING MESSAGE",
                    device.Viewport.Width / 2,
                    device.Viewport.Height / 3,
                    scale * 2,
                    Palette.White);
            }
            else if (_missionMesh is { } mesh)
            {
                _renderer.Begin();
                _renderer.DrawShip(
                    mesh,
                    DrawnPosition(),
                    ViewedShip(),
                    Camera,
                    Palette.Hull,
                    visibilityDistance: 31);
                _renderer.End();
            }

            spriteBatch.End();
            return;
        }

        // The text pages, wrapped to the window's width the way the disc lays its justified text
        // out across the 32 columns of the text grid, keeping the printer's own paragraph breaks.
        // The ship, when the briefing has one, sits above the text: the disc's PAS1 shows the
        // rotating ship at (0, 112, 256) while the briefing prints at the foot of the screen
        bool shipShown = _missionMesh is not null;
        int columns = 32;
        int top = shipShown ? (int)(device.Viewport.Height * 0.5f) : cellHeight * 3;

        foreach ((string line, int index) in Wrap(_shownText, columns).Select((l, i) => (l, i)))
        {
            _text.Draw(spriteBatch, line, cellWidth, top + (index * cellHeight), scale, Palette.White);
        }

        if (_page < _pages.Length - 1)
        {
            _text.DrawHintLine(
                spriteBatch,
                "PRESS A KEY",
                cellWidth,
                device.Viewport.Height - (cellHeight * 2),
                scale,
                (int)Camera.ViewportWidth - (cellWidth * 2),
                new Color(120, 128, 140));
        }

        spriteBatch.End();

        if (shipShown && _missionMesh is { } shown)
        {
            _renderer.Begin();
            _renderer.DrawShip(
                _missionMesh,
                DrawnPosition(),
                ViewedShip(),
                Camera,
                Palette.Hull,
                visibilityDistance: 31);
            _renderer.End();
        }
    }

    /// <summary>
    /// Wraps a page into lines of a given character width, on words: the disc's own printer lays
    /// the briefing out across the 32-column text grid, and ours does the same job for the window.
    /// </summary>
    private static IEnumerable<string> Wrap(string text, int columns)
    {
        var lines = new List<string>();

        // The printer's own newlines are the briefing's paragraph breaks, so each paragraph is
        // wrapped separately rather than flowed together
        foreach (string paragraph in text.Split('\n'))
        {
            var current = new System.Text.StringBuilder();

            foreach (string word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (current.Length > 0 && current.Length + 1 + word.Length > columns)
                {
                    lines.Add(current.ToString());
                    current.Clear().Append(word);
                }
                else if (current.Length > 0)
                {
                    current.Append(' ').Append(word);
                }
                else
                {
                    current.Append(word);
                }
            }

            lines.Add(current.ToString());
        }

        return lines;
    }

    /// <summary>Where the briefing ship is drawn, in the original's units, through the camera.</summary>
    private System.Numerics.Vector3 DrawnPosition()
    {
        (int x, int y, int z) = _ship!.GetPosition();
        return new System.Numerics.Vector3(x, y, z);
    }

    /// <summary>The briefing ship's orientation as the mesh renderer wants it.</summary>
    private ShipOrientation ViewedShip() => ShipOrientation.FromEliteOrientation(_ship!.Orientation);
}
