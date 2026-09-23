using EliteRemake.Data.Ships;
using Xunit;

using EliteRemake.Core.Sim;

namespace EliteRemake.Core.Tests;

/// <summary>
/// Tests for the ship blueprints extracted from the original BBC Micro disc Elite sources by
/// tools/EliteDataExtractor and embedded in EliteRemake.Data.
/// </summary>
public class ShipDataTests
{
    [Fact]
    public void LoadsAtLeast31Ships()
    {
        Assert.True(
            ShipData.All.Count >= 31,
            $"expected at least 31 ships, found {ShipData.All.Count}");

        Assert.Equal(ShipData.All.Count, ShipData.Counts.Ships);
        Assert.All(ShipData.All, ship =>
        {
            Assert.False(string.IsNullOrWhiteSpace(ship.Id));
            Assert.False(string.IsNullOrWhiteSpace(ship.Name));
            Assert.False(string.IsNullOrWhiteSpace(ship.Label));
            Assert.NotEmpty(ship.Vertices);
            Assert.NotEmpty(ship.Edges);
            Assert.NotEmpty(ship.Faces);
        });
    }

    [Fact]
    public void ShipIdsAreUnique()
    {
        HashSet<string> ids = [];
        foreach (ShipBlueprint ship in ShipData.All)
        {
            Assert.True(ids.Add(ship.Id), $"duplicate ship id '{ship.Id}'");
        }
    }

    [Fact]
    public void CobraMk3HasExpectedGeometryAndStats()
    {
        ShipBlueprint cobra = ShipData.ById("cobra-mk-3")
            ?? throw new InvalidOperationException("the Cobra Mk III blueprint was not extracted");

        Assert.Equal("Cobra Mk III", cobra.Name);
        Assert.Equal(28, cobra.Vertices.Count);
        Assert.Equal(38, cobra.Edges.Count);
        Assert.Equal(13, cobra.Faces.Count);
        Assert.Equal(28, cobra.Header.MaxSpeed);
        Assert.Equal(150, cobra.Header.MaxEnergy);

        // The header counts and the decoded arrays must agree.
        Assert.Equal(cobra.Header.VertexCount, cobra.Vertices.Count);
        Assert.Equal(cobra.Header.EdgeCount, cobra.Edges.Count);
        Assert.Equal(cobra.Header.FaceCount, cobra.Faces.Count);
    }

    [Fact]
    public void CoriolisIsRegisteredAtType2AndViperAtType16()
    {
        Assert.True(ShipData.TryGetByType(2, out ShipBlueprint? type2));
        Assert.NotNull(type2);
        Assert.Equal("coriolis", type2!.Id);
        Assert.Equal("Coriolis space station", type2.Name);
        Assert.True(type2.IsRegisteredAs(2));

        ShipBlueprint viper = ShipData.ByType(16);
        Assert.Equal("viper", viper.Id);
        Assert.Contains("COPS", viper.Symbols);

        // The 16 flight ship files plus the docked table all register these two types.
        Assert.True(ShipData.AllForType(2).Count >= 2, "type 2 is shared by the Coriolis and the Dodo");
        Assert.Contains(ShipData.AllForType(2), ship => ship.Id == "dodo");
    }

    [Fact]
    public void EveryEdgeReferencesValidVerticesAndEveryEdgeFaceIsValid()
    {
        foreach (ShipBlueprint ship in ShipData.All)
        {
            Assert.All(ship.Edges, edge =>
            {
                Assert.InRange(edge.Vertex1, 0, ship.Vertices.Count - 1);
                Assert.InRange(edge.Vertex2, 0, ship.Vertices.Count - 1);

                // The alloy plate is the one documented exception: every one of its edges (and
                // vertices) is associated with face 15, the original's "always visible" pseudo-face,
                // even though the plate has a single real face. It is pinned by its own test below.
                if (ship.Id == "plate")
                {
                    return;
                }

                Assert.Equal(2, edge.Faces.Count);
                Assert.All(edge.Faces, face => Assert.InRange(face, 0, ship.Faces.Count - 1));
            });
        }
    }

    [Fact]
    public void AlloyPlateUsesTheAlwaysVisiblePseudoFace()
    {
        ShipBlueprint plate = ShipData.ById("plate")
            ?? throw new InvalidOperationException("the alloy plate blueprint was not extracted");

        Assert.Single(plate.Faces);
        Assert.Equal(4, plate.Edges.Count);
        Assert.All(plate.Edges, edge => Assert.All(edge.Faces, face => Assert.Equal(15, face)));
    }

    [Fact]
    public void ShipSetsCoverEveryShipTypeExcept15()
    {
        Assert.Equal(17, ShipData.ShipSets.Count);
        Assert.Equal("D.MOA", ShipData.ShipSets[0].Id);
        Assert.Equal("docked", ShipData.ShipSets[^1].Id);

        for (int type = 1; type <= 31; type++)
        {
            if (type == 15)
            {
                // No XX21 table in the disc version registers ship type 15.
                Assert.False(ShipData.TryGetByType(type, out _));
                continue;
            }

            Assert.True(ShipData.TryGetByType(type, out ShipBlueprint? ship), $"no ship registered at type {type}");
            Assert.NotNull(ship);
            Assert.Contains(type, ship!.Types);
        }

        Assert.Throws<KeyNotFoundException>(() => ShipData.ByType(15));
    }

    [Fact]
    public void HangarVariantsArePresentForTheShipsThatNeedThem()
    {
        // The docked code has its own copies of the hangar blueprints for these five ships; the
        // Canister, Cobra Mk III and Viper are identical in both places.
        string[] expected = ["shuttle", "transporter", "python", "krait", "constrictor"];
        foreach (string id in expected)
        {
            ShipBlueprint ship = ShipData.ById(id)
                ?? throw new InvalidOperationException($"missing ship '{id}'");
            Assert.NotNull(ship.DockedVariant);
            Assert.Equal(ship.Vertices.Count, ship.DockedVariant!.Vertices.Count);
        }

        Assert.Null(ShipData.ById("viper")!.DockedVariant);
        Assert.Null(ShipData.ById("canister")!.DockedVariant);
    }

    [Fact]
    public void SplinterCarriesItsSourceDeclaredFaces()
    {
        ShipBlueprint splinter = ShipData.ById("splinter")
            ?? throw new InvalidOperationException("the splinter blueprint was not extracted");

        // The original disc binary points the splinter's face offset 24 bytes past its own face
        // data, so the extracted faces are not the ones in the source; the source-declared faces are
        // carried separately.
        Assert.NotNull(splinter.DeclaredFaces);
        Assert.Equal(splinter.Faces.Count, splinter.DeclaredFaces!.Count);
        Assert.NotEqual(splinter.Faces[0].X, splinter.DeclaredFaces[0].X);
    }
}

/// <summary>
/// Checks the disc's default NEWB flags — the <c>E%</c> byte each ship type carries — which decide
/// whether a ship fights, whether it is an innocent, and whether killing it makes us a fugitive.
/// </summary>
/// <remarks>
/// These flags come from a table that is easy to read from the wrong place, and the first version of
/// the extractor did exactly that: it produced a table with zeroes for every pirate in the game, so
/// nothing hostile could spawn and the sky was almost entirely peaceful. These tests pin the ships
/// that are supposed to be dangerous.
/// </remarks>
public class NewbFlagTests
{
    /// <summary>Bit 2: the ship is hostile, which is what lets it attack at all.</summary>
    private const byte Hostile = 0x04;

    /// <summary>Bit 1: a bounty hunter, which only turns on us once we are nearly a fugitive.</summary>
    private const byte BountyHunter = 0x02;

    /// <summary>Bit 0: a trader, which may turn out to be a pirate instead.</summary>
    private const byte Trader = 0x01;

    /// <summary>Bit 6: a cop, whose destruction makes us a fugitive at once.</summary>
    private const byte Cop = 0x40;

    private static byte Flags(int type) => ShipData.NewbFlagsFor(type);

    [Theory]
    [InlineData(17)]   // Sidewinder
    [InlineData(18)]   // Mamba
    [InlineData(19)]   // Krait
    [InlineData(20)]   // Adder
    [InlineData(21)]   // Gecko
    [InlineData(22)]   // Cobra Mk I
    [InlineData(23)]   // Worm
    [InlineData(24)]   // Cobra Mk III (pirate)
    [InlineData(25)]   // Asp Mk II
    [InlineData(26)]   // Python (pirate)
    [InlineData(28)]   // Moray
    [InlineData(29)]   // Thargoid
    [InlineData(30)]   // Thargon
    public void ThePirateHullsAreHostile(int type)
    {
        Assert.True((Flags(type) & Hostile) != 0,
            $"ship type {type} should be hostile but its flags are 0x{Flags(type):X2}");
    }

    [Fact]
    public void TheFerDeLanceIsABountyHunterAndNotSimplyHostile()
    {
        // 0x82: a bounty hunter that leaves a clean commander alone, which is why forcing every
        // spawn to be hostile was wrong
        Assert.Equal(BountyHunter, Flags(27) & BountyHunter);
        Assert.Equal(0, Flags(27) & Hostile);
    }

    [Fact]
    public void TheTradersAreTradersAndTheCopsAreCops()
    {
        Assert.Equal(Trader, Flags(9) & Trader);    // Shuttle
        Assert.Equal(Trader, Flags(10) & Trader);   // Transporter

        // The two cops the flight loop recognises, which the shipped data also names
        Assert.NotEqual(0, Flags(10) & Cop);
        Assert.NotEqual(0, Flags(16) & Cop);

        // And the pirates are not cops, or every kill would make us a fugitive
        Assert.Equal(0, Flags(17) & Cop);
        Assert.Equal(0, Flags(24) & Cop);
    }

    [Fact]
    public void TheTraderHullsAreNotHostileWhenTheyArrive()
    {
        // The Cobra, Python, Boa and Anaconda the spawner sends out as traders arrive peaceful, and
        // the Anaconda is a trader as well
        Assert.Equal(0, Flags(11) & Hostile);   // Cobra Mk III
        Assert.Equal(0, Flags(12) & Hostile);   // Python
        Assert.Equal(0, Flags(13) & Hostile);   // Boa
        Assert.Equal(Trader, Flags(14) & Trader);   // Anaconda
    }

    [Fact]
    public void TheSpawnerStampsTheFlagItsOwnTableCarries()
    {
        // The simulation's own defaults table has to agree with the extracted data, or a spawn made
        // without the game layer's blueprint lookup arrives with the wrong personality
        foreach (int type in new[] { 17, 18, 19, 24, 27, 29 })
        {
            Assert.Equal(Flags(type), BlueprintDefaults.For(type).NewbFlags);
        }
    }
}
