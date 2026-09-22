using EliteRemake.Core.Maths;

namespace EliteRemake.Core.Sim;

/// <summary>
/// Missiles and the E.C.M. that answers them.
/// </summary>
/// <remarks>
/// A missile is just another ship — type 1 — that chases a target instead of us. Firing one takes
/// it from the commander's rack and makes the target angry, which is why shooting at a trader and
/// missing still tends to spoil its day. A hit does 250 damage, and a missile that goes off nearby
/// does 80, so a missile that misses is still worth avoiding.
///
/// The E.C.M. destroys every missile in the local bubble, at the cost of a chunk of energy. In the
/// original it also makes ships with their own E.C.M. switch them on, which is a nice touch for
/// later.
/// </remarks>
public static class Missiles
{
    /// <summary>The missile ship type, as the original's XX21 table has it.</summary>
    public const int MissileType = 1;

    /// <summary>The damage a missile does when it hits us.</summary>
    public const int DirectHitDamage = 250;

    /// <summary>The damage a missile does when it is destroyed close to us.</summary>
    public const int NearbyDamage = 80;

    /// <summary>How close a missile must get to its target to go off.</summary>
    public const int ImpactRange = 120;

    /// <summary>How much energy the E.C.M. costs to fire.</summary>
    public const int EcmEnergyCost = 8;

    /// <summary>How near a missile must be for the E.C.M. to catch it.</summary>
    public const int EcmRange = 20000;

    /// <summary>True if a ship is a missile.</summary>
    public static bool IsMissile(int shipType) => shipType == MissileType;

    /// <summary>
    /// Creates a missile launched from our ship, heading straight ahead as the original's FRS1
    /// does, with the target it will chase.
    /// </summary>
    /// <param name="target">The ship the missile is chasing, or null for us.</param>
    public static Ship CreateMissile(Ship? target) => new(MissileType, "missile", "Missile")
    {
        Speed = 44,
        Target = target,
    };

    /// <summary>
    /// Steers a missile towards its target. Returns true if it went off this frame.
    /// </summary>
    /// <param name="missile">The missile.</param>
    /// <param name="targetPosition">The target's position relative to us.</param>
    public static bool Steer(Ship missile, (int X, int Y, int Z) targetPosition)
    {
        (int x, int y, int z) = missile.GetPosition();
        int dx = targetPosition.X - x;
        int dy = targetPosition.Y - y;
        int dz = targetPosition.Z - z;

        double distance = Math.Sqrt(((double)dx * dx) + ((double)dy * dy) + ((double)dz * dz));
        if (distance < ImpactRange)
        {
            return true;
        }

        // Steer towards the target the same way ships steer towards us
        var toTarget = new System.Numerics.Vector3(dx, dy, dz) / (float)distance;
        System.Numerics.Vector3 nose = Unit(missile.Orientation, Orientation.Nosev);
        System.Numerics.Vector3 roof = Unit(missile.Orientation, Orientation.Roofv);
        System.Numerics.Vector3 side = Unit(missile.Orientation, Orientation.Sidev);

        float aimX = System.Numerics.Vector3.Dot(toTarget, side);
        float aimY = System.Numerics.Vector3.Dot(toTarget, roof);

        // The counters take the opposite sign to the aim, as TACTICS has it
        missile.Data[ShipDataBlock.RollCounter] = (byte)((aimX > 0 ? 0x80 : 0x00) | (Math.Abs(aimX) > 0.02 ? 4 : 0));
        missile.Data[ShipDataBlock.PitchCounter] = (byte)((aimY > 0 ? 0x80 : 0x00) | (Math.Abs(aimY) > 0.02 ? 4 : 0));

        return false;
    }

    /// <summary>Reads one of a ship's orientation vectors as a unit vector.</summary>
    private static System.Numerics.Vector3 Unit(Orientation orientation, int vector)
    {
        var value = new System.Numerics.Vector3(
            (float)orientation.GetUnity(vector, Orientation.X),
            (float)orientation.GetUnity(vector, Orientation.Y),
            (float)orientation.GetUnity(vector, Orientation.Z));

        return value.LengthSquared() > 0
            ? System.Numerics.Vector3.Normalize(value)
            : new System.Numerics.Vector3(0, 0, 1);
    }
}
