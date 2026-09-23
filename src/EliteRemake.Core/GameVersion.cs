using System.Reflection;

namespace EliteRemake.Core;

/// <summary>
/// The remake's version number.
/// </summary>
/// <remarks>
/// The number itself lives in <c>Directory.Build.props</c> at the repository root and is read back
/// off the assembly rather than written a second time here: a version that has to be kept in step by
/// hand is a version that drifts, and the one place it has to be right is the place the build reads.
/// The scheme is semantic versioning, described in <c>docs/VERSIONING.md</c> — MAJOR when the port is
/// complete against the disc version, MINOR for a mechanic or a screen the original has and we do
/// not, PATCH for a fix to something already claimed.
/// </remarks>
public static class GameVersion
{
    /// <summary>The version number on its own, such as "1.0.0".</summary>
    public static string Number { get; } = ReadNumber();

    /// <summary>The version as the title screen shows it, such as "v1.0.0".</summary>
    public static string Display => "v" + Number;

    private static string ReadNumber()
    {
        string? informational = typeof(GameVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
        {
            return "0.0.0";
        }

        // The SDK appends the commit's hash to the informational version ("1.0.0+1a2b3c4"), which is
        // useful in a bug report and clutter on the title screen
        int plus = informational.IndexOf('+');
        return plus < 0 ? informational : informational[..plus];
    }
}
