using EliteRemake.Core;
using Xunit;

namespace EliteRemake.Core.Tests;

/// <summary>
/// The version number, which the title screen shows and the release tags follow.
/// </summary>
public class VersionTests
{
    [Fact]
    public void TheVersionIsASemanticVersion()
    {
        string[] parts = GameVersion.Number.Split('.');

        Assert.Equal(3, parts.Length);
        foreach (string part in parts)
        {
            Assert.True(int.TryParse(part, out int number) && number >= 0, $"\"{part}\" is not a number");
            Assert.Equal(part, part.Trim());          // no stray spaces
            Assert.False(part.StartsWith('0') && part.Length > 1, $"\"{part}\" has a leading zero");
        }
    }

    [Fact]
    public void TheVersionComesFromTheBuildRatherThanBeingWrittenTwice()
    {
        // Directory.Build.props sets <Version>, the SDK turns it into the assembly's informational
        // version, and GameVersion reads it back. A number written here as well would be a second
        // copy to forget about, so this checks the one that ships is the one that was built
        string informational = typeof(GameVersion).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .Cast<System.Reflection.AssemblyInformationalVersionAttribute>()
            .Single()
            .InformationalVersion;

        Assert.StartsWith(GameVersion.Number, informational, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDisplayedVersionIsTheNumberWithAVee()
    {
        Assert.Equal("v" + GameVersion.Number, GameVersion.Display);
        Assert.Matches(@"^v\d+\.\d+\.\d+$", GameVersion.Display);
    }
}
