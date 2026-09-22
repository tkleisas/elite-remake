using EliteRemake.Core.Ships;
using EliteRemake.Core.Sim;
using EliteRemake.Data.Ships;
using Xunit;
using ShipFace = EliteRemake.Core.Ships.ShipFace;

namespace EliteRemake.Core.Tests;

/// <summary>
/// Tests for the solid geometry built from the original blueprints, in particular the detail edges
/// that the original draws on top of a face.
/// </summary>
public class ShipMeshTests
{
    private static ShipMesh Build(string id)
    {
        ShipBlueprint blueprint = ShipData.ById(id)
            ?? throw new InvalidOperationException($"no blueprint called '{id}'");

        var vertices = new BlueprintVertex[blueprint.Vertices.Count];
        for (int i = 0; i < vertices.Length; i++)
        {
            ShipVertex vertex = blueprint.Vertices[i];
            vertices[i] = new BlueprintVertex(vertex.X, vertex.Y, vertex.Z, [.. vertex.Faces], vertex.Visibility);
        }

        var edges = new BlueprintEdge[blueprint.Edges.Count];
        for (int i = 0; i < edges.Length; i++)
        {
            Data.Ships.ShipEdge edge = blueprint.Edges[i];
            edges[i] = new BlueprintEdge(edge.Vertex1, edge.Vertex2, [.. edge.Faces], edge.Visibility);
        }

        var faces = new BlueprintFace[blueprint.Faces.Count];
        for (int i = 0; i < faces.Length; i++)
        {
            Data.Ships.ShipFace face = blueprint.Faces[i];
            faces[i] = new BlueprintFace(face.X, face.Y, face.Z, face.Visibility);
        }

        return ShipMesh.Build(vertices, edges, faces, blueprint.Header.NormalScale);
    }

    /// <summary>
    /// A Coriolis station's docking slot is the four edges 24-27 of its blueprint. They name face 0
    /// twice, so the original draws them whenever the station's front face is towards us, which is
    /// what makes the slot visible. A solid renderer has to be told to draw them explicitly.
    /// </summary>
    [Fact]
    public void CoriolisStationHasItsDockingSlotAsDetailEdges()
    {
        ShipMesh station = Build("coriolis");

        // The slot's four corners sit on the front face, at z = 160, forming a 20 x 60 rectangle
        // around the centre of the face
        var expected = new (int Vertex1, int Vertex2)[]
        {
            (12, 13),
            (13, 14),
            (14, 15),
            (15, 12),
        };

        foreach ((int vertex1, int vertex2) in expected)
        {
            DetailEdge slot = Assert.Single(
                station.DetailEdges,
                detail => detail.Edge.Vertex1 == vertex1 && detail.Edge.Vertex2 == vertex2);

            Assert.Equal(0, slot.Face);
            Assert.Equal(0, slot.Edge.Face1);
            Assert.Equal(0, slot.Edge.Face2);

            // The original's slot carries a visibility of 30, which is above the largest value the
            // z-distance test can produce, so the slot is drawn at any range the station is solid
            Assert.Equal(30, slot.Edge.Visibility);
        }

        Assert.Equal(160, station.Vertices[12].Z);
        Assert.Equal(10, station.Vertices[12].X);
        Assert.Equal(-30, station.Vertices[12].Y);
        Assert.Equal(10, station.Vertices[13].X);
        Assert.Equal(30, station.Vertices[13].Y);
        Assert.Equal(-10, station.Vertices[14].X);
        Assert.Equal(30, station.Vertices[14].Y);
        Assert.Equal(-10, station.Vertices[15].X);
        Assert.Equal(-30, station.Vertices[15].Y);
    }

    /// <summary>
    /// A Cobra Mk III carries its engine outline and other exhaust detail as edges that name face 9
    /// twice - the same trick the station uses for its slot - so they must come through as detail
    /// edges too. Their low visibility values (6, 8, 17, 19, 20) are what stops them showing until
    /// the ship is close.
    /// </summary>
    [Fact]
    public void CobraEngineOutlineIsADetailEdge()
    {
        ShipMesh cobra = Build("cobra-mk-3");

        Assert.NotEmpty(cobra.DetailEdges);
        Assert.All(cobra.DetailEdges, detail =>
        {
            Assert.Equal(9, detail.Face);
            Assert.Equal(9, detail.Edge.Face1);
            Assert.Equal(9, detail.Edge.Face2);
        });

        // The exhaust detail is drawn only up close, unlike the station's slot
        Assert.Contains(cobra.DetailEdges, detail => detail.Edge.Visibility == 6);
        Assert.Contains(cobra.DetailEdges, detail => detail.Edge.Visibility == 20);
    }

    /// <summary>
    /// A ship with no detail edges must not gain any: the alloy plate's four edges all name the
    /// original's dummy face 15, which has no normal, and the disc blueprints use edges 0-2 with
    /// face 0 on both sides. Neither is a face the renderer can fill, so neither is a detail line.
    /// </summary>
    [Fact]
    public void ShipsWithoutDetailEdgesDoNotGainAny()
    {
        ShipMesh plate = Build("plate");
        Assert.Empty(plate.DetailEdges);
    }

    /// <summary>
    /// The detail edges must not disturb the faces. A Coriolis station's front face is the rotated
    /// square whose corners are vertices 0-3, all at z = 160; the slot's corners are separate
    /// vertices that lie inside it, so they must not appear in the face's outline.
    /// </summary>
    [Fact]
    public void StationFrontFaceRemainsTheFullSquare()
    {
        ShipMesh station = Build("coriolis");

        ShipFace front = Assert.Single(station.Faces, face => face.FaceNumber == 0);
        Assert.Equal(4, front.Indices.Length);
        Assert.Equal([0, 1, 2, 3], front.Indices.Order());
        Assert.All(front.Indices, index => Assert.Equal(160, station.Vertices[index].Z));

        // The slot's corners are not part of the outline
        Assert.DoesNotContain(12, front.Indices);
        Assert.DoesNotContain(15, front.Indices);
    }

    /// <summary>
    /// A station's docking slot is in the blueprint's +z face, and the original's own docking
    /// computer (DCS1) says the nose vector points from the centre of the station out through the
    /// slot. So when a station is created ahead of us, the slot only faces us if the station's
    /// orientation has been turned around - which is what the original's NWSPS does, and what used
    /// to be missing here, leaving the slot pointing away from us where it could never be seen or
    /// flown into.
    /// </summary>
    [Fact]
    public void AStationCreatedAheadOfUsPresentsItsSlotToUs()
    {
        ShipMesh stationMesh = Build("coriolis");
        ShipFace slotFace = Assert.Single(stationMesh.Faces, face => face.FaceNumber == 0);

        // The slot's own corners confirm which way the decorated face points
        Assert.True(slotFace.Normal.Z > 0.99f, $"the front face should point along +z, not {slotFace.Normal}");

        Ship station = SystemArrival.CreateStation(3000, spinRoll: 64);

        // The station is ahead of us and the nose points back along the vector from it to us
        (int x, int y, int z) = station.GetPosition();
        Assert.Equal(3000, z);

        var toUs = new System.Numerics.Vector3(-x, -y, -z);
        toUs = System.Numerics.Vector3.Normalize(toUs);

        var nose = new System.Numerics.Vector3(
            (float)station.Orientation.GetUnity(Core.Maths.Orientation.Nosev, Core.Maths.Orientation.X),
            (float)station.Orientation.GetUnity(Core.Maths.Orientation.Nosev, Core.Maths.Orientation.Y),
            (float)station.Orientation.GetUnity(Core.Maths.Orientation.Nosev, Core.Maths.Orientation.Z));

        Assert.True(
            System.Numerics.Vector3.Dot(nose, toUs) > 0.99f,
            $"the slot should face us, but the nose vector points {nose} while we are at {toUs}");

        // And the original's own docking test agrees once we are lined up in front of the slot
        Ship close = SystemArrival.CreateStation(200, spinRoll: 64);
        (int cx, int cy, int cz) = close.GetPosition();
        Assert.Equal(
            DockingResult.Docking,
            Docking.Check(close, (cx, cy, cz), new System.Numerics.Vector3(0, 0, 1), stationHostile: false));
    }

    /// <summary>
    /// A station turns at the fixed rate the original applies, and its roll counter's magnitude
    /// does not change that: MVEIT part 8 calls MVS5 once whenever the counter is non-zero, and
    /// MVS5 turns the orientation vectors by a fixed 1/16 of a radian. NWSPS sets the counter to
    /// 255 - "maximum anti-clockwise roll with no damping" - which only means the station never
    /// stops turning.
    /// </summary>
    /// <remarks>
    /// The turn is measured frame by frame rather than accumulated, because at this rate the
    /// station comes right round every hundred frames or so and an accumulated total would wrap.
    /// </remarks>
    [Fact]
    public void AStationTurnsAtTheOriginalsFixedRate()
    {
        foreach (byte spinRoll in new byte[] { 1, 64, 127, 255 })
        {
            var player = new Ship(11, "cobra-mk-3", "Cobra Mk III");
            var sim = new FlightSim(player) { SpawningEnabled = false, Commander = Commander.CreateDefault() };
            Ship station = SystemArrival.CreateStation(3000, spinRoll);
            sim.Spawn(station);

            for (int frame = 0; frame < 12; frame++)
            {
                double before = RollAngle(station);
                sim.Step();
                double after = RollAngle(station);

                double turned = after - before;
                while (turned < -Math.PI)
                {
                    turned += Math.Tau;
                }

                while (turned > Math.PI)
                {
                    turned -= Math.Tau;
                }

                // 1/16 radian is 3.58 degrees a frame, whatever the counter's magnitude. The
                // station rolls clockwise, so the angle it turns through is negative.
                double degrees = Math.Abs(turned) * 180 / Math.PI;
                Assert.InRange(degrees, 3.4, 3.7);
            }
        }
    }

    /// <summary>The station's roll angle, read from its roof vector.</summary>
    private static double RollAngle(Ship station) => Math.Atan2(
        station.Orientation.GetUnity(Core.Maths.Orientation.Roofv, Core.Maths.Orientation.Y),
        station.Orientation.GetUnity(Core.Maths.Orientation.Roofv, Core.Maths.Orientation.X));
}
