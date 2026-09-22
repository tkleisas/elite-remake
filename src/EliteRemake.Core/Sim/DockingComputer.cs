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

    /// <summary>Which of DOCKIT's phases the last manoeuvre came from.</summary>
    public enum Phase
    {
        /// <summary>PH1: head for the ideal docking position out along the slot.</summary>
        IdealPosition,

        /// <summary>PH2: too close and badly placed, so turn away.</summary>
        TurnAway,

        /// <summary>PH3: refine the approach.</summary>
        Refine,
    }

    /// <summary>The phase the last manoeuvre came from, for tracing what the autopilot is doing.</summary>
    public static Phase LastPhase { get; private set; }

    /// <summary>The manoeuvre the docking computer wants this frame.</summary>
    /// <param name="RollCounter">The roll counter to set, as DOCKIT writes to INWK+29.</param>
    /// <param name="PitchCounter">The pitch counter to set, as DOCKIT writes to INWK+30.</param>
    /// <param name="SpeedUp">Whether to thrust.</param>
    /// <param name="SlowDown">Whether to brake.</param>
    /// <param name="LinedUp">Whether we are lined up well enough for the slot's cone.</param>
    public readonly record struct Manoeuvre(
        byte RollCounter,
        byte PitchCounter,
        bool SpeedUp,
        bool SlowDown,
        bool LinedUp);

    /// <summary>
    /// Works out the docking computer's manoeuvre for this frame, as counters and a speed rather
    /// than as key presses — which is what DOCKIT actually produces.
    /// </summary>
    /// <param name="station">The space station.</param>
    /// <param name="speed">Our current speed.</param>
    /// <param name="rollRate">Our roll rate, which the original centres on 128.</param>
    /// <param name="pitchRate">Our pitch rate, which the original centres on 128.</param>
    public static Manoeuvre Fly(Ship station, int speed)
    {
        (int sx, int sy, int sz) = station.GetPosition();
        var stationPosition = new Vector3(sx, sy, sz);
        float distance = stationPosition.Length();

        if (distance < 1)
        {
            return default;
        }

        Vector3 toStation = stationPosition / distance;

        // The station's nose is its slot, and the slot turns as the station rolls
        Vector3 slot = Unit(station.Orientation, Orientation.Nosev);

        // How squarely we are looking at the slot, which is the original's dot product test
        float approach = Vector3.Dot(toStation, slot);

        // The station's nose points out of its slot, so when the slot faces us the nose points
        // from the station towards us and the vector to the station points the other way: a
        // squarely lined-up approach gives a dot product of -1.
        bool linedUp = toStation.Z > 0.99f && approach < -0.99f;

        // PH1: fly for the ideal docking position, out along the slot, and match the station's roll.
        // The original tests its dot product against 35 of 96 and goes to the ideal position when
        // the slot is not facing us squarely enough. Its dot product runs the other way to this
        // one — the original's is the slot against the line to us, this is the line to the station
        // against the slot — so the comparison is reversed here. Getting this backwards made PH1
        // the default branch, which left the ship rolling to match the station for ever.
        if (approach * UnitVector > -IdealApproachDot)
        {
            LastPhase = Phase.IdealPosition;
            Vector3 ideal = stationPosition + (slot * IdealDockingSteps * UnitVector);
            Vector3 aim = ideal.LengthSquared() > 0 ? Vector3.Normalize(ideal) : toStation;
            return Steer(aim, speed, distance, matchStationRoll: true, station.Data[ShipDataBlock.RollCounter], linedUp);
        }

        // PH2: too close and badly placed, so turn away rather than press on into the hull
        if (distance < TooCloseDistance && Math.Abs(approach) < 0.5f)
        {
            LastPhase = Phase.TurnAway;
            return Steer(-toStation, speed, distance, false, 0, linedUp);
        }

        // PH3: refine the approach, rolling and pitching towards the station
        LastPhase = Phase.Refine;
        return Steer(toStation, speed, distance, false, 0, linedUp);
    }

    /// <summary>
    /// Turns us towards a direction the way DOCKIT does: by setting the roll and pitch counters,
    /// which the flight model then flies the ship with. This is the part that cannot be expressed as
    /// key presses — the original's docking computer does not hold the controls down, it sets the
    /// counters and lets MVEIT do the rest.
    /// </summary>
    private static Manoeuvre Steer(
        Vector3 aim,
        int speed,
        float distance,
        bool matchStationRoll,
        int stationRollCounter,
        bool linedUp)
    {
        // We face along +z in the world's terms, because the universe turns around us, so the aim's
        // world components are the aim relative to our own axes
        int rollAngle = (int)(aim.X * UnitVector);
        int pitchAngle = (int)(aim.Y * UnitVector);

        byte rollCounter = 128;  // centred: no roll
        byte pitchCounter = 128; // centred: no pitch

        if (matchStationRoll)
        {
            // PH1 rolls to match the space station's own roll, so the slot stays lined up as the
            // station turns. The station's roll counter says how fast it is turning and which way,
            // with bit 7 as the sign, so we roll with it rather than rolling blindly. The original
            // uses no damping for this.
            int magnitude = stationRollCounter & 0x7F;
            bool clockwise = (stationRollCounter & 0x80) != 0;
            if (magnitude > TurnThreshold)
            {
                rollCounter = (byte)(magnitude | (clockwise ? 0x00 : 0x80));
            }
        }
        else
        {
            // RefineApproach, which is the disc version's own PH3 code: the roll counter is two,
            // with its sign taken from -x * y of the target in our frame — a quadrant test rather
            // than the x component alone, which is how the ship knows which way to roll to bring the
            // target round to the centre. The pitch counter is zeroed here and set by the caller.
            float sign = -(aim.X * aim.Y);
            rollCounter = (byte)(TurnCounter | (sign < 0 ? 0x80 : 0x00));
            pitchCounter = 128;

            if (Math.Abs(pitchAngle) > TurnThreshold)
            {
                pitchCounter = (byte)(TurnCounter | (pitchAngle > 0 ? 0x00 : 0x80));
            }

            // If the target is more than six units off the centre line the original stops
            // manoeuvring altogether and slows right down, because it is out of our sights: this is
            // a fine adjustment, and getting roughly lined up is PH1's job.
            if (Math.Abs(aim.X) * 6 >= 0.375f)
            {
                rollCounter = 128;
                pitchCounter = 128;
            }
        }

        // The original caps the docking speed, easing in rather than charging at the station
        int allowed = (int)Math.Clamp(distance / 160f, 4, DockingSpeed);

        return new Manoeuvre(rollCounter, pitchCounter, speed < allowed, speed > allowed, linedUp);
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
