using EliteRemake.Data.Ships;
using Xunit;

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
