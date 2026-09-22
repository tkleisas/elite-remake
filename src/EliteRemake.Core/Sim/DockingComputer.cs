using EliteRemake.Core.Maths;
using System.Numerics;

namespace EliteRemake.Core.Sim;

/// <summary>
/// The docking computer: a port of the original's DOCKIT routine.
/// </summary>
/// <remarks>
/// The original's docking computer is not a control loop of its own. Its key logger's manoeuvring
/// code sets up a scratch ship looking along +z at our speed, calls DOCKIT to work out the moves,
/// and writes the answer back into the key logger — so the computer flies the ship through exactly
/// the same path as the keyboard. This port keeps that shape: it decides what the manoeuvre should
/// be and returns a <see cref="FlightInput"/>, the same thing the keyboard produces.
///
/// DOCKIT works like this:
///
/// <list type="bullet">
/// <item>Outside the station's safe zone, or too far away for anything accurate, it heads towards
/// the planet (GOPL) — the station is somewhere in that direction.</item>
/// <item>Pointing the wrong way, or poorly lined up, it flies for the <b>ideal docking position</b>,
/// a point out along the slot from the station. DCS1 works that out by stepping out along the
/// station's nose vector, twice per call, and PH1 calls it twice — so the ideal position is eight
/// nose vectors out. Crucially, PH1 also <b>rolls to match the station's own roll</b>, because the
/// Coriolis turns and the slot's orientation changes as you approach. A controller that aims at a
/// point and ignores that can never line up with a rotating slot.</item>
/// <item>Too close and badly placed (within 157 units), it turns away instead (PH2).</item>
/// <item>Pointing the right way, it refines (PH3), rolling and pitching towards the station only
/// while it is within the sight limit of the crosshairs.</item>
/// </list>
///
/// The counters are the original's: a counter of 2 to turn by, a threshold of 6 below which pitch
/// and roll are not applied at all, and the speed capped at 22 while docking.
/// </remarks>
public static class DockingComputer
{
    /// <summary>The magnitude to set the pitch or roll counter to when turning (the original's RAT).</summary>
    public const int TurnCounter = 2;

    /// <summary>The angle below which pitch and roll are not applied at all (the original's RAT2).</summary>
    public const int TurnThreshold = 6;

    /// <summary>The speed the original caps docking at.</summary>
    public const int DockingSpeed = 22;

    /// <summary>The distance inside which a badly placed ship turns away instead of pressing on.</summary>
    public const int TooCloseDistance = 157;

    /// <summary>The dot product below which the approach is not good enough to refine.</summary>
    public const int IdealApproachDot = 35;

    /// <summary>
    /// How many nose vectors out the ideal docking position sits. DCS1 steps out by two nose
    /// vectors and runs twice, and PH1 calls it twice.
    /// </summary>
    public const int IdealDockingSteps = 8;

    /// <summary>One nose vector, in the original's units, where 96 is a unit vector.</summary>
    private const int UnitVector = 96;

    /// <summary>
    /// Works out the docking computer's manoeuvre for this frame. Returns the controls to apply, and
    /// whether we are lined up well enough for the slot's cone to accept us.
    /// </summary>
    /// <param name="station">The space station.</param>
    /// <param name="speed">Our current speed.</param>
    /// <param name="rollRate">Our roll rate, which the original centres on 128.</param>
    /// <param name="pitchRate">Our pitch rate, which the original centres on 128.</param>
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

        // The station's nose is its slot, and the slot turns as the station rolls
        Vector3 slot = Unit(station.Orientation, Orientation.Nosev);

        // How squarely we are looking at the slot, which is the original's dot product test
        float approach = Vector3.Dot(toStation, slot);

        // Straight ahead and down the slot's axis: the docking checks will accept us
        bool linedUp = toStation.Z > 0.99f && approach < -0.99f;

        // PH1: fly for the ideal docking position, out along the slot, and match the station's roll
        if (approach > -0.2f || approach * UnitVector < IdealApproachDot)
        {
            Vector3 ideal = stationPosition + (slot * IdealDockingSteps * UnitVector);
            Vector3 aim = ideal.LengthSquared() > 0 ? Vector3.Normalize(ideal) : toStation;
            return (
                Steer(
                    aim,
                    speed,
                    distance,
                    rollRate,
                    pitchRate,
                    matchStationRoll: true,
                    station.Data[ShipDataBlock.RollCounter]),
                linedUp);
        }

        // PH2: too close and badly placed, so turn away rather than press on into the hull
        if (distance < TooCloseDistance && Math.Abs(approach) < 0.5f)
        {
            return (Steer(-toStation, speed, distance, rollRate, pitchRate, false), linedUp);
        }

        // PH3: refine the approach, rolling and pitching towards the station
        return (Steer(toStation, speed, distance, rollRate, pitchRate, false), linedUp);
    }

    /// <summary>
    /// Turns us towards a direction using the original's counters: the roll and pitch counters are
    /// set to the turn magnitude when the aim is off by more than the threshold, and released once
    /// the ship is already turning that way, so the turn settles instead of overshooting.
    /// </summary>
    private static FlightInput Steer(
        Vector3 aim,
        int speed,
        float distance,
        int rollRate,
        int pitchRate,
        bool matchStationRoll,
        int stationRollCounter = 0)
    {
        // We face along +z in the world's terms, because the universe turns around us, so the aim's
        // world components are the aim relative to our own axes
        int rollAngle = (int)(aim.X * UnitVector);
        int pitchAngle = (int)(aim.Y * UnitVector);

        bool rollLeft = false;
        bool rollRight = false;
        bool pullUp = false;
        bool pitchDown = false;

        if (matchStationRoll)
        {
            // PH1 rolls to match the space station's own roll, so the slot stays lined up as the
            // station turns. The station's roll counter says how fast it is turning and which way,
            // with bit 7 as the sign, so we roll with it rather than rolling blindly.
            int stationRoll = stationRollCounter;
            int magnitude = stationRoll & 0x7F;
            bool clockwise = (stationRoll & 0x80) != 0;

            rollRight = magnitude > TurnThreshold && clockwise;
            rollLeft = magnitude > TurnThreshold && !clockwise;
        }
        else
        {
            // Roll towards the station when it is off to one side, and release once the rate is
            // already carrying us round
            if (Math.Abs(rollAngle) > TurnThreshold)
            {
                rollRight = rollAngle > 0 && rollRate >= 128 - TurnCounter;
                rollLeft = rollAngle < 0 && rollRate <= 128 + TurnCounter;
            }

            // Pitch towards it, releasing in the same way
            if (Math.Abs(pitchAngle) > TurnThreshold)
            {
                pullUp = pitchAngle > 0 && pitchRate >= 128 - TurnCounter;
                pitchDown = pitchAngle < 0 && pitchRate <= 128 + TurnCounter;
            }
        }

        // The original caps the docking speed, easing in rather than charging at the station
        int allowed = (int)Math.Clamp(distance / 160f, 4, DockingSpeed);

        return new FlightInput(
            RollLeft: rollLeft,
            RollRight: rollRight,
            PullUp: pullUp,
            PitchDown: pitchDown,
            SpeedUp: speed < allowed,
            SlowDown: speed > allowed,
            Fire: false);
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
