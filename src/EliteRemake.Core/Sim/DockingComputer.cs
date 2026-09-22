using EliteRemake.Core.Maths;
using System.Numerics;

namespace EliteRemake.Core.Sim;

/// <summary>
/// The docking computer: when it is switched on it flies the ship in through the station's slot.
/// </summary>
/// <remarks>
/// The original implements its docking computer through the key logger — rather than steering
/// directly, it writes the manoeuvres it wants into the same structure the keyboard fills, so the
/// ship is flown by exactly the same code whether a person or the computer is at the controls. This
/// does the same thing in the same spirit: it produces a <see cref="FlightInput"/>, the same thing
/// the keyboard produces, so the autopilot has no special path into the flight model.
///
/// The approach it flies is the one <see cref="Docking"/> demands: it aims for a point out along the
/// slot's axis, rolls and pitches onto it, and closes until the slot's cone accepts us.
/// </remarks>
public static class DockingComputer
{
    /// <summary>How far out along the slot's axis the autopilot lines up before closing.</summary>
    public const int ApproachDistance = 1200;

    /// <summary>How close we may get before it stops thrusting and coasts in.</summary>
    public const int CoastDistance = 600;

    /// <summary>
    /// The glide slope: the speed the autopilot will allow itself at a given distance. The original
    /// eases in rather than charging at the station, and without this the autopilot overshoots and
    /// leaves the station behind it, which the flight model cannot recover from since it has no yaw.
    /// </summary>
    public static int ApproachSpeed(float distance) => (int)Math.Clamp(distance / 160f, 4, 30);

    /// <summary>How small an aim error is close enough, in dot product terms.</summary>
    public const float AimDeadZone = 0.008f;

    /// <summary>
    /// The fastest the autopilot will ask the ship to turn, as an offset from the centre of the
    /// rate range. Full deflection is 128 either side, and asking for less than that is what stops
    /// the ship swinging past the station.
    /// </summary>
    public const float MaxTurnRate = 70f;

    /// <summary>
    /// How far the rate may differ from what the autopilot wants before it presses a control. A
    /// dead band keeps it from chattering between the two directions.
    /// </summary>
    public const float RateDeadBand = 8f;

    /// <summary>
    /// Works out the controls to fly us into the station's slot, and whether we are lined up.
    /// </summary>
    /// <param name="station">The space station.</param>
    /// <param name="speed">Our current speed.</param>
    /// <param name="rollRate">Our current roll rate, which the original centres on 128.</param>
    /// <param name="pitchRate">Our current pitch rate, which the original centres on 128.</param>
    public static (FlightInput Input, bool LinedUp) Fly(Ship station, int speed, int rollRate, int pitchRate)
    {
        (int sx, int sy, int sz) = station.GetPosition();
        var stationPosition = new Vector3(sx, sy, sz);
        float distance = stationPosition.Length();
        if (distance < 1)
        {
            return (default, true);
        }

        Vector3 toStation = stationPosition / distance;

        // The station's nose vector points out of its slot, so the point to line up on is out along
        // that axis, and the way in is back down it
        Vector3 slot = Unit(station.Orientation, Orientation.Nosev);
        Vector3 aimPoint = stationPosition + (slot * ApproachDistance);
        Vector3 aim = distance > ApproachDistance
            ? Vector3.Normalize(aimPoint)
            : -slot; // close in, we fly straight down the slot

        // We sit at the centre of our own universe and it turns around us, so our own axes are the
        // fixed ones: we face along +z, with +x to our right and +y above. Steering is therefore a
        // matter of reading the aim's world components directly — and because the world turns with
        // our controls, an aim to the right is corrected by rolling right.
        float aimX = aim.X;
        float aimY = aim.Y;

        // Rather than holding a control down until the station is centred — which overshoots and
        // swings past it — ask for a turn no faster than the aim error warrants, and then let the
        // controls off once the ship is already turning at that rate. Rolling right takes the rate
        // below the centre of its range and pulling up does the same for pitch, so the rates the
        // autopilot wants are negative offsets.
        float wantedRoll = -Math.Clamp(aimX, -1f, 1f) * MaxTurnRate;
        float wantedPitch = -Math.Clamp(aimY, -1f, 1f) * MaxTurnRate;

        float rollError = wantedRoll - (rollRate - 128);
        float pitchError = wantedPitch - (pitchRate - 128);

        var input = new FlightInput(
            RollLeft: rollError > RateDeadBand,
            RollRight: rollError < -RateDeadBand,
            PullUp: pitchError < -RateDeadBand,
            PitchDown: pitchError > RateDeadBand,
            SpeedUp: speed < ApproachSpeed(distance),
            SlowDown: speed > ApproachSpeed(distance),
            Fire: false);

        // Lined up when the station is straight ahead and we are looking down the slot's axis
        bool linedUp = toStation.Z > 0.99f && Vector3.Dot(toStation, slot) < -0.99f;
        return (input, linedUp);
    }

    /// <summary>Reads one of a ship's orientation vectors as a unit vector.</summary>
    private static Vector3 Unit(Orientation orientation, int vector)
    {
        var value = new Vector3(
            (float)orientation.GetUnity(vector, Orientation.X),
            (float)orientation.GetUnity(vector, Orientation.Y),
            (float)orientation.GetUnity(vector, Orientation.Z));

        return value.LengthSquared() > 0 ? Vector3.Normalize(value) : new Vector3(0, 0, 1);
    }
}
