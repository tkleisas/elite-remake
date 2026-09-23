using EliteRemake.Core.Sim;
using Xunit;

namespace EliteRemake.Core.Tests;

/// <summary>
/// The four space views: the axis flipping the original applies to a ship before it decides whether
/// it is in the crosshairs or draws it.
/// </summary>
/// <remarks>
/// The rules come from PLUT, and they are short enough to write out: the rear view negates x and z,
/// and the side views swap x and z and then negate one of them. The values matter more than they look
/// — the laser mount and the view are the same number, so looking out of the back is what fires the
/// rear laser — which is why they are pinned here rather than left to the renderer to demonstrate.
/// </remarks>
public class SpaceViewTests
{
    [Theory]
    [InlineData(SpaceView.Front, 1, 2, 3, 1, 2, 3)]
    [InlineData(SpaceView.Rear, 1, 2, 3, -1, 2, -3)]
    [InlineData(SpaceView.Left, 1, 2, 3, 3, 2, -1)]
    [InlineData(SpaceView.Right, 1, 2, 3, -3, 2, 1)]
    public void AViewFlipsAPositionAsPlutDoes(
        SpaceView view, int x, int y, int z, int ex, int ey, int ez)
    {
        Assert.Equal((ex, ey, ez), Plut.Position(view, x, y, z));
    }

    /// <summary>
    /// The four transforms are rotations, not reflections, so a ship is never seen mirrored: each
    /// preserves lengths, and the left and right views undo each other.
    /// </summary>
    [Fact]
    public void TheViewsAreRotationsAndNotMirrors()
    {
        foreach (SpaceView view in Enum.GetValues<SpaceView>())
        {
            (int x, int y, int z) = Plut.Position(view, 300, -400, 1200);
            Assert.Equal((300 * 300) + (400 * 400) + (1200 * 1200), (x * x) + (y * y) + (z * z));
        }

        // Looking right and then left brings the universe back where it was, and so does looking
        // behind us twice
        foreach (SpaceView view in new[] { SpaceView.Rear, SpaceView.Left, SpaceView.Right })
        {
            SpaceView inverse = view switch
            {
                SpaceView.Rear => SpaceView.Rear,
                SpaceView.Left => SpaceView.Right,
                _ => SpaceView.Left,
            };

            (int x, int y, int z) = Plut.Position(view, 7, -11, 13);
            (x, y, z) = Plut.Position(inverse, x, y, z);
            Assert.Equal((7, -11, 13), (x, y, z));
        }
    }

    /// <summary>Looking left, what is to our left is dead ahead, and what is ahead of us is right.</summary>
    [Fact]
    public void LookingLeftPutsOurLeftInFrontAndOurFrontToTheRight()
    {
        (int x, int y, int z) = Plut.Position(SpaceView.Left, -5000, 0, 0);
        Assert.True(z > 0, "a ship off our left wing should be in front of the left view");
        Assert.Equal(0, x);

        (x, y, z) = Plut.Position(SpaceView.Left, 0, 0, 5000);
        Assert.Equal(0, z);
        Assert.True(x > 0, "a ship ahead of us should be off to the right of the left view");
        Assert.Equal(0, y);
    }

    /// <summary>A laser mount and a view are the same number in the original's LASER array.</summary>
    [Theory]
    [InlineData(SpaceView.Front, LaserMount.Front)]
    [InlineData(SpaceView.Rear, LaserMount.Rear)]
    [InlineData(SpaceView.Left, LaserMount.Left)]
    [InlineData(SpaceView.Right, LaserMount.Right)]
    public void EachViewFiresItsOwnLaser(SpaceView view, LaserMount mount)
    {
        Assert.Equal(mount, Plut.Mount(view));
    }

    /// <summary>
    /// The rear laser hits what is behind us, and the front laser does not: the hit test is done on
    /// the flipped position, as the original's HITCH is.
    /// </summary>
    [Fact]
    public void TheRearLaserHitsWhatTheFrontLaserCannotReach()
    {
        // A target 700 units behind us, and a rear laser to shoot it with
        Ship behind = Ship.Create(17, "sidewinder", "Sidewinder", 0, 0, 0, 0, -700);
        var sim = new FlightSim(new Ship(11, "cobra-mk-3", "Cobra Mk III"));
        sim.Commander = Commander.CreateDefault();
        sim.Spawn(behind);

        Assert.False(
            Combat.IsInCrosshairs(behind, sim.TargetableAreaOf(behind), SpaceView.Front),
            "a ship behind us is not in the front view's crosshairs");

        Assert.True(
            Combat.IsInCrosshairs(behind, sim.TargetableAreaOf(behind), SpaceView.Rear),
            "a ship behind us is in the rear view's crosshairs");

        // And the laser that fires is the one fitted to the view we are looking through
        sim.Commander.SetLaser(LaserMount.Rear, LaserType.Pulse);
        sim.Commander.SetLaser(LaserMount.Front, LaserType.None);

        sim.View = SpaceView.Front;
        sim.Step(new FlightInput(Fire: true));
        Assert.Equal(0, sim.FiringLaserPower);

        sim.View = SpaceView.Rear;
        sim.Step(new FlightInput(Fire: true));
        Assert.True(sim.FiringLaserPower > 0, "the rear laser should fire from the rear view");
        Assert.Same(behind, sim.LaserTarget);
    }

    /// <summary>Looking to the side puts what is off that wing in the crosshairs.</summary>
    [Theory]
    [InlineData(SpaceView.Left, -900)]
    [InlineData(SpaceView.Right, 900)]
    public void ASideViewAimsAtWhatIsOffThatWing(SpaceView view, int x)
    {
        Ship target = Ship.Create(17, "sidewinder", "Sidewinder", 0, 0, x, 0, 0);
        var sim = new FlightSim(new Ship(11, "cobra-mk-3", "Cobra Mk III"));

        Assert.False(Combat.IsInCrosshairs(target, sim.TargetableAreaOf(target), SpaceView.Front));
        Assert.True(Combat.IsInCrosshairs(target, sim.TargetableAreaOf(target), view));
    }

    /// <summary>The dust streams the other way through the rear window, and sideways through the sides.</summary>
    [Theory]
    [InlineData(SpaceView.Front, 0, 0, -1)]
    [InlineData(SpaceView.Rear, 0, 0, 1)]
    [InlineData(SpaceView.Left, -1, 0, 0)]
    [InlineData(SpaceView.Right, 1, 0, 0)]
    public void TheStardustStreamsTheOtherWayInEachView(
        SpaceView view, int x, int y, int z)
    {
        Assert.Equal((x, y, z), Plut.StreamDirection(view));
    }
}
