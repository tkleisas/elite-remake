using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Xna.Framework.Input;

namespace EliteRemake.Game;

/// <summary>
/// The player's control bindings, saved between sessions.
/// </summary>
/// <remarks>
/// The original's keys are fixed — IBM and Acornsoft chose them and there was no way to change them —
/// so this is a modern addition rather than a port. The defaults are the original's own keys wherever
/// the original has one, so a player who never opens the settings screen gets the 1984 layout.
///
/// The bindings are held as <see cref="Keys"/> names, which is what makes the file readable and what
/// lets a binding survive a key being added to MonoGame's enum.
/// </remarks>
public sealed class Settings
{
    /// <summary>Fire the lasers, which the original puts on "A".</summary>
    [JsonPropertyName("fire")]
    public string Fire { get; set; } = nameof(Keys.A);

    /// <summary>Fire a missile, on the original's "M".</summary>
    [JsonPropertyName("missile")]
    public string Missile { get; set; } = nameof(Keys.M);

    /// <summary>Lock a missile onto the target, on the original's "T".</summary>
    [JsonPropertyName("target")]
    public string Target { get; set; } = nameof(Keys.T);

    /// <summary>The E.C.M., on the original's "E".</summary>
    [JsonPropertyName("ecm")]
    public string Ecm { get; set; } = nameof(Keys.E);

    /// <summary>The energy bomb, on the original's TAB.</summary>
    [JsonPropertyName("energyBomb")]
    public string EnergyBomb { get; set; } = nameof(Keys.Tab);

    /// <summary>The docking computer, on the original's "C".</summary>
    [JsonPropertyName("dockingComputer")]
    public string DockingComputer { get; set; } = nameof(Keys.C);

    /// <summary>The in-system jump, on the original's "J".</summary>
    [JsonPropertyName("inSystemJump")]
    public string InSystemJump { get; set; } = nameof(Keys.J);

    /// <summary>Unarm the missile, on the original's "U".</summary>
    [JsonPropertyName("unarmMissile")]
    public string UnarmMissile { get; set; } = nameof(Keys.U);

    /// <summary>
    /// Launch our escape pod, on the original's ESCAPE key.
    /// </summary>
    /// <remarks>
    /// This is the one binding whose key the remake had already given to something else: ESCAPE left
    /// the game, where the original launches a pod with it. In flight the pod wins, and F10 leaves
    /// from anywhere, which is a modern addition of the same kind as the bindings themselves.
    /// </remarks>
    [JsonPropertyName("escapePod")]
    public string EscapePod { get; set; } = nameof(Keys.Escape);

    /// <summary>Hyperspace. The original uses "H" from the charts; there is no flight key for it.</summary>
    [JsonPropertyName("hyperspace")]
    public string Hyperspace { get; set; } = nameof(Keys.H);

    /// <summary>Roll left, the original's "," and the left arrow.</summary>
    [JsonPropertyName("rollLeft")]
    public string RollLeft { get; set; } = nameof(Keys.OemComma);

    /// <summary>Roll right, the original's "." and the right arrow.</summary>
    [JsonPropertyName("rollRight")]
    public string RollRight { get; set; } = nameof(Keys.OemPeriod);

    /// <summary>Pull up, the original's "X" and the up arrow.</summary>
    [JsonPropertyName("pullUp")]
    public string PullUp { get; set; } = nameof(Keys.X);

    /// <summary>Pitch down, the original's "S" and the down arrow.</summary>
    [JsonPropertyName("pitchDown")]
    public string PitchDown { get; set; } = nameof(Keys.S);

    /// <summary>Speed up, the original's SPACE.</summary>
    [JsonPropertyName("speedUp")]
    public string SpeedUp { get; set; } = nameof(Keys.Space);

    /// <summary>Slow down, the original's "?".</summary>
    [JsonPropertyName("slowDown")]
    public string SlowDown { get; set; } = nameof(Keys.OemQuestion);

    /// <summary>
    /// Whether the title screen plays the Blue Danube, which is the one piece of music in the game
    /// and a modern addition: the BBC disc has no music at all.
    /// </summary>
    [JsonPropertyName("music")]
    public bool Music { get; set; } = true;

    /// <summary>
    /// The bindings as a list a settings screen can walk, in the order it should show them.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<(string Name, Func<string> Get, Action<string> Set)> Bindings =>
    [
        ("FIRE", () => Fire, v => Fire = v),
        ("MISSILE", () => Missile, v => Missile = v),
        ("TARGET", () => Target, v => Target = v),
        ("E.C.M.", () => Ecm, v => Ecm = v),
        ("ENERGY BOMB", () => EnergyBomb, v => EnergyBomb = v),
        ("IN-SYSTEM JUMP", () => InSystemJump, v => InSystemJump = v),
        ("UNARM MISSILE", () => UnarmMissile, v => UnarmMissile = v),
        ("ESCAPE POD", () => EscapePod, v => EscapePod = v),
        ("HYPERSPACE", () => Hyperspace, v => Hyperspace = v),
        ("DOCKING COMPUTER", () => DockingComputer, v => DockingComputer = v),
        ("ROLL LEFT", () => RollLeft, v => RollLeft = v),
        ("ROLL RIGHT", () => RollRight, v => RollRight = v),
        ("PULL UP", () => PullUp, v => PullUp = v),
        ("PITCH DOWN", () => PitchDown, v => PitchDown = v),
        ("SPEED UP", () => SpeedUp, v => SpeedUp = v),
        ("SLOW DOWN", () => SlowDown, v => SlowDown = v),
    ];

    /// <summary>
    /// How a binding should be shown to the player.
    /// </summary>
    /// <remarks>
    /// MonoGame's key names are the names of the keys on a modern keyboard, so most of them read
    /// well already; the handful that are punctuation are the ones worth spelling out, because
    /// "OemQuestion" is not a key anybody has seen.
    /// </remarks>
    public static string DisplayName(string binding) => binding switch
    {
        "OemComma" => ",",
        "OemPeriod" => ".",
        "OemQuestion" => "?",
        "OemMinus" => "-",
        "OemPlus" => "+",
        "OemTilde" => "~",
        "OemOpenBrackets" => "[",
        "OemCloseBrackets" => "]",
        "OemPipe" => "|",
        "OemSemicolon" => ";",
        "OemQuotes" => "'",
        "Space" => "SPACE",
        "Tab" => "TAB",
        "Enter" => "ENTER",
        "Escape" => "ESCAPE",
        _ => binding.ToUpperInvariant(),
    };

    /// <summary>Resolves a binding name to a key, falling back to the default if it is not one.</summary>
    public static Keys Key(string name, Keys fallback) =>
        Enum.TryParse(name, ignoreCase: true, out Keys key) ? key : fallback;

    /// <summary>The key bound to an action.</summary>
    public Keys FireKey => Key(Fire, Keys.A);
    public Keys MissileKey => Key(Missile, Keys.M);
    public Keys TargetKey => Key(Target, Keys.T);
    public Keys EcmKey => Key(Ecm, Keys.E);
    public Keys EnergyBombKey => Key(EnergyBomb, Keys.Tab);
    public Keys HyperspaceKey => Key(Hyperspace, Keys.H);
    public Keys DockingComputerKey => Key(DockingComputer, Keys.C);
    public Keys InSystemJumpKey => Key(InSystemJump, Keys.J);
    public Keys UnarmMissileKey => Key(UnarmMissile, Keys.U);
    public Keys EscapePodKey => Key(EscapePod, Keys.Escape);
    public Keys RollLeftKey => Key(RollLeft, Keys.OemComma);
    public Keys RollRightKey => Key(RollRight, Keys.OemPeriod);
    public Keys PullUpKey => Key(PullUp, Keys.X);
    public Keys PitchDownKey => Key(PitchDown, Keys.S);
    public Keys SpeedUpKey => Key(SpeedUp, Keys.Space);
    public Keys SlowDownKey => Key(SlowDown, Keys.OemQuestion);

    /// <summary>Where the settings are kept.</summary>
    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "settings.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Loads the settings, or the defaults when there are none or they cannot be read.</summary>
    /// <remarks>
    /// A settings file is never worth failing over: anything unreadable gives the defaults back, and
    /// a binding that names a key this build does not have falls back per binding.
    /// </remarks>
    public static Settings Load(string? path = null)
    {
        string file = path ?? DefaultPath;
        try
        {
            if (File.Exists(file))
            {
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(file), Options) ?? new Settings();
            }
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            // Fall through to the defaults
        }

        return new Settings();
    }

    /// <summary>Saves the settings, returning a message describing what happened.</summary>
    public string Save(string? path = null)
    {
        string file = path ?? DefaultPath;
        try
        {
            File.WriteAllText(file, JsonSerializer.Serialize(this, Options));
            return $"Settings saved to {Path.GetFileName(file)}.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return $"Could not save settings: {error.Message}";
        }
    }
}
