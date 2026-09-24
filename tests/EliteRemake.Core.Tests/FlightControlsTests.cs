using EliteRemake.Core.Maths;
using EliteRemake.Core.Sim;
using EliteRemake.Core.Universe;
using Xunit;

namespace EliteRemake.Core.Tests;

/// <summary>
/// Checks the flight controls' own tables and keys, which the flight model reads every frame: the
/// roll and pitch angle tables that drive the dials and the world rotation, BUMP2 and REDU2's
/// recentring, and the damping.
/// </summary>
public class FlightControlsTests
{
    [Fact]
    public void TheCentreMeansNoRollAndNoPitch()
    {
        var (roll, _, _) = FlightControls.RollAngle(FlightControls.Centre);
        var (pitch, _, _) = FlightControls.PitchAngle(FlightControls.Centre);

        Assert.Equal(0, roll);
        Assert.Equal(0, pitch);
    }

    [Fact]
    public void RollAnglesRunFullDeflectionToEight()
    {
        // ALP1 is the magnitude of the roll angle, worked out from the damped rate: a rate held at
        // the far end of its travel gives the largest angle, and the sign lands in ALP2
        var (full, sign, flipped) = FlightControls.RollAngle(FlightControls.Centre - FlightControls.RollStep * 16);

        Assert.True(full > 0, $"a hard roll should give an angle, not {full}");
        Assert.True((sign & 0x80) != 0, "a roll below the centre is one sign");
        Assert.NotEqual(sign, flipped);

        var (other, _, _) = FlightControls.RollAngle(FlightControls.Centre + FlightControls.RollStep * 16);
        Assert.True(other > 0, "the other direction is an angle too, not nothing");
    }

    [Fact]
    public void AFullPitchDeflectionGivesTheDiscsEight()
    {
        // "Add 4 before dividing by 16, so a full deflection gives a pitch angle of 8": the disc's
        // BET1 runs 0 to 8, and 8 is the very ends of the rate's range, 0 and 255
        var (full, _, _) = FlightControls.PitchAngle(0);

        Assert.Equal(8, full);

        var (other, _, _) = FlightControls.PitchAngle(255);
        Assert.Equal(8, other);

        // And a held rate mid-range gives less than the full deflection
        var (held, _, _) = FlightControls.PitchAngle(FlightControls.Centre);
        Assert.Equal(0, held);
    }

    [Fact]
    public void Bump2ClampsAtTheTopOfTheRange()
    {
        Assert.Equal(255, FlightControls.Bump2(255, FlightControls.RollStep, autoRecentre: false));
        Assert.Equal(255, FlightControls.Bump2(250, FlightControls.RollStep, autoRecentre: false));
    }

    [Fact]
    public void Redu2ClampsAtTheBottomOfTheRange()
    {
        // "DEA #A: result = x - a, and if it underflows, result = 1" — never zero
        Assert.Equal(1, FlightControls.Redu2(1, FlightControls.RollStep, autoRecentre: false));
    }

    [Fact]
    public void TappingTheOtherKeyRecentresWhenAutoRecentreIsOn()
    {
        // The disc's DJD: a rate on one side of the centre, nudged across to the other, snaps
        // straight back to the centre
        byte rolled = FlightControls.Centre;
        for (int i = 0; i < 5; i++)
        {
            rolled = FlightControls.Bump2(rolled, FlightControls.RollStep, autoRecentre: true);
        }

        byte stopped = FlightControls.Redu2(rolled, FlightControls.RollStep, autoRecentre: true);
        Assert.Equal(FlightControls.Centre, stopped);

        // And with it off, the rate just creeps back a step at a time
        byte creeping = FlightControls.Redu2(rolled, FlightControls.RollStep, autoRecentre: false);
        Assert.NotEqual(FlightControls.Centre, creeping);
    }

    [Fact]
    public void DampingCentresTheRatesTwoAtATime()
    {
        // The original calls cntr twice per frame for roll, so the rate creeps by two
        byte rolled = (byte)(FlightControls.Centre - 20);
        Assert.Equal((byte)(FlightControls.Centre - 18), FlightControls.DampRollRate(rolled));

        // And damping off leaves it alone
        Assert.Equal(rolled, FlightControls.DampRollRate(rolled, dampingDisabled: true));
    }
}

/// <summary>
/// Checks the missiles' steering, which the flight loop runs for every missile every iteration: the
/// counters it sets chase the target, and a close-enough missile goes off.
/// </summary>
public class MissileSteeringTests
{
    private static Ship Missile()
    {
        var missile = Missiles.CreateMissile(target: null);
        missile.SetPosition(0, 0, 0);
        return missile;
    }

    [Fact]
    public void AMissileCloseEnoughGoesOff()
    {
        var missile = Missile();
        Assert.True(Missiles.Steer(missile, (0, 0, Missiles.ImpactRange - 1)), "inside the impact range it detonates");
        Assert.False(Missiles.Steer(missile, (0, 0, Missiles.ImpactRange + 1000)));
    }

    [Fact]
    public void TheCountersChaseTheTarget()
    {
        var missile = Missile();

        // A target off to one side and above: the counters turn, with a magnitude of 4 while the
        // aim is more than the threshold
        Assert.False(Missiles.Steer(missile, (2000, 2000, 4000)));

        byte rollTurned = missile.Data[ShipDataBlock.RollCounter];
        byte pitchTurned = missile.Data[ShipDataBlock.PitchCounter];
        Assert.True((rollTurned & 0x7F) == 4, $"the roll counter should turn by 4, not {rollTurned:X2}");
        Assert.True((pitchTurned & 0x7F) == 4, $"the pitch counter should turn by 4, not {pitchTurned:X2}");

        // A target on the other side rolls the other way: the sign bits differ
        Assert.False(Missiles.Steer(missile, (-2000, 2000, 4000)));
        Assert.NotEqual(rollTurned, missile.Data[ShipDataBlock.RollCounter]);

        // And pitch does the same for above and below
        Assert.False(Missiles.Steer(missile, (2000, -2000, 4000)));
        Assert.NotEqual(pitchTurned, missile.Data[ShipDataBlock.PitchCounter]);
    }

    [Fact]
    public void ATargetDeadAheadIsLeftAlone()
    {
        var missile = Missile();

        Missiles.Steer(missile, (0, 0, 4000));

        Assert.Equal(0x00, missile.Data[ShipDataBlock.RollCounter]);
        Assert.Equal(0x00, missile.Data[ShipDataBlock.PitchCounter]);
    }
}
