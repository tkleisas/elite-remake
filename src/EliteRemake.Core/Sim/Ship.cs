using EliteRemake.Core.Maths;

namespace EliteRemake.Core.Sim;

/// <summary>
/// Byte offsets and flag bits of the original's 37-byte ship data block (the block that is copied
/// into INWK, one per ship in the local bubble).
/// </summary>
public static class ShipDataBlock
{
    /// <summary>The number of bytes in a ship data block (the original's NI%).</summary>
    public const int Size = 37;

    /// <summary>Offset of the x coordinate (lo, hi, sign).</summary>
    public const int X = 0;

    /// <summary>Offset of the y coordinate.</summary>
    public const int Y = 3;

    /// <summary>Offset of the z coordinate.</summary>
    public const int Z = 6;

    /// <summary>Offset of the orientation vectors (nosev, roofv, sidev).</summary>
    public const int Orientation = 9;

    /// <summary>Offset of the ship's speed.</summary>
    public const int Speed = 27;

    /// <summary>Offset of the ship's acceleration.</summary>
    public const int Acceleration = 28;

    /// <summary>Offset of the ship's roll counter, used when it manoeuvres.</summary>
    public const int RollCounter = 29;

    /// <summary>Offset of the ship's pitch counter.</summary>
    public const int PitchCounter = 30;

    /// <summary>Offset of the ship's flags.</summary>
    public const int Flags = 31;

    /// <summary>Offset of the ship's AI flag.</summary>
    public const int Ai = 32;

    /// <summary>Offset of the ship's scanner flag and colour.</summary>
    public const int Scanner = 33;

    /// <summary>Offset of the explosion cloud counter.</summary>
    public const int ExplosionCounter = 34;

    /// <summary>Offset of the ship's energy.</summary>
    public const int Energy = 35;

    /// <summary>Offset of the newly spawned ship flags.</summary>
    public const int NewbFlags = 36;

    /// <summary>
    /// Bit 4 of the flag byte: whether the original shows this ship on the dashboard's 3D scanner.
    /// The planet and the sun never are, and neither is anything already destroyed.
    /// </summary>
    public const byte FlagShowOnScanner = 0x10;

    /// <summary>Flag bit: the ship is exploding.</summary>
    public const byte FlagExploding = 0x20;

    /// <summary>Flag bit: the ship has been killed and should be removed.</summary>
    public const byte FlagKilled = 0x80;

    /// <summary>Flag bit: the ship is docking or has docked.</summary>
    public const byte FlagDocking = 0x01;
}

/// <summary>
/// A ship in the local bubble of universe, held in the original's data block layout so that the
/// ported routines can work on it directly.
/// </summary>
public sealed class Ship
{
    private readonly byte[] _data = new byte[ShipDataBlock.Size];

    /// <summary>Creates a ship of the given type, with an identity orientation.</summary>
    public Ship(int type, string blueprintId, string name)
    {
        Type = type;
        BlueprintId = blueprintId;
        Name = name;

        Orientation = new Orientation(_data, ShipDataBlock.Orientation);
        ResetOrientation();
    }

    /// <summary>The raw data block, exactly as the original lays it out.</summary>
    public Span<byte> Data => _data;

    /// <summary>
    /// Sets the identity orientation: nosev along +z, roofv along +y and sidev along -x, with
    /// unity (96) in the high bytes and the low bytes clear, as the original does.
    /// </summary>
    private void ResetOrientation()
    {
        _data.AsSpan(ShipDataBlock.Orientation, 18).Clear();
        _data[ShipDataBlock.Orientation + 5] = 96;             // nosev_z_hi
        _data[ShipDataBlock.Orientation + 6 + 3] = 96;         // roofv_y_hi
        _data[ShipDataBlock.Orientation + 12 + 1] = 0x80 | 96; // sidev_x_hi (negative)
    }

    /// <summary>The ship's type number, as used by the original's XX21 tables. Negative for the
    /// planet and sun.</summary>
    public int Type { get; set; }

    /// <summary>The blueprint this ship was built from, or an empty string for the planet and sun.</summary>
    public string BlueprintId { get; set; }

    /// <summary>A friendly name, for diagnostics.</summary>
    public string Name { get; set; }

    /// <summary>The ship's orientation vectors, as a view over the data block.</summary>
    public Orientation Orientation { get; }

    /// <summary>The ship's position, as a view over the data block (9 bytes: x, y, z).</summary>
    public Span<byte> Position => _data.AsSpan(ShipDataBlock.X, 9);

    /// <summary>The ship's speed (INWK+27).</summary>
    public byte Speed
    {
        get => _data[ShipDataBlock.Speed];
        set => _data[ShipDataBlock.Speed] = value;
    }

    /// <summary>
    /// The ship's acceleration (INWK+28), which the original's ANGRY raises when we shoot it.
    /// </summary>
    public byte Acceleration
    {
        get => _data[ShipDataBlock.Acceleration];
        set => _data[ShipDataBlock.Acceleration] = value;
    }

    /// <summary>The ship's flags (INWK+31).</summary>
    /// <summary>
    /// The roll the space station was created with. The original gives a station a random clockwise
    /// roll — a random value with bit 7 cleared, so 0 to 127 — and the station turns for as long as
    /// it is there.
    /// </summary>
    public byte SpinRoll { get; set; }

    /// <summary>True when this ship belongs on the 3D scanner.</summary>
    public bool ShowOnScanner
    {
        get => (Flags & ShipDataBlock.FlagShowOnScanner) != 0;
        set => Flags = value ? (byte)(Flags | ShipDataBlock.FlagShowOnScanner) : (byte)(Flags & ~ShipDataBlock.FlagShowOnScanner);
    }

    public byte Flags
    {
        get => _data[ShipDataBlock.Flags];
        set => _data[ShipDataBlock.Flags] = value;
    }

    /// <summary>The ship's AI flag (INWK+32).</summary>
    /// <summary>
    /// The ship's NEWB flags: whether it is a trader, innocent, cop or hostile, and so on.
    /// </summary>
    /// <remarks>
    /// The defaults per ship type are the disc's <c>E%</c> table, which the data extractor reads out
    /// of the assembled docked code. The flight loop tests bit 6 of these to decide how much killing
    /// a ship raises our legal status, and bit 5 to decide whether it counts as an innocent.
    /// </remarks>
    public byte NewbFlags { get; set; }

    /// <summary>Bit 0: a trader, which can turn out to be a pirate instead.</summary>
    public const byte NewbTrader = 0x01;

    /// <summary>Bit 1: a bounty hunter, which comes after us once we are nearly a fugitive.</summary>
    public const byte NewbBountyHunter = 0x02;

    /// <summary>Bit 2: hostile, which is what a trader becomes when it turns out to be a pirate.</summary>
    public const byte NewbHostile = 0x04;

    /// <summary>
    /// Bit 3: a pirate, which on this build is the Krait and the Constrictor. It matters beyond
    /// flavour: a pirate that finds itself inside the space station's no-fire zone has its
    /// aggression cleared, and it is the only kind of ship the original does that to.
    /// </summary>
    public const byte NewbPirate = 0x08;

    /// <summary>Bit 6: a cop, whose destruction makes us a fugitive at once.</summary>
    public const byte NewbCop = 0x40;

    /// <summary>
    /// Bit 7: an escape pod is fitted, which is what lets a ship that has run out of luck take to it.
    /// Every pirate hull has one; the Thargons, the Worm and the Constrictor do not.
    /// </summary>
    public const byte NewbEscapePod = 0x80;

    /// <summary>Bit 5: an innocent, whose destruction raises our legal status by one.</summary>
    public const byte NewbInnocent = 0x20;

    /// <summary>True when this ship is a cop.</summary>
    public bool IsCop => (NewbFlags & NewbCop) != 0;

    /// <summary>True when this ship counts as an innocent.</summary>
    public bool IsInnocent => (NewbFlags & NewbInnocent) != 0;

    /// <summary>True when this ship is hostile, which a trader becomes when it turns out to be a
    /// pirate.</summary>
    public bool IsHostile => (NewbFlags & NewbHostile) != 0;

    /// <summary>
    /// How many missiles this ship has left, which the original keeps in bits 0-2 of byte #31 and
    /// NWSHP fills in from the blueprint. An enemy spends one when it fires at us.
    /// </summary>
    public byte Missiles
    {
        get => (byte)(_data[ShipDataBlock.Flags] & 0x07);
        set => _data[ShipDataBlock.Flags] = (byte)((_data[ShipDataBlock.Flags] & 0xF8) | (value & 0x07));
    }

    public byte AiFlag
    {
        get => _data[ShipDataBlock.Ai];
        set => _data[ShipDataBlock.Ai] = value;
    }

    /// <summary>The ship's energy (INWK+35).</summary>
    public byte Energy
    {
        get => _data[ShipDataBlock.Energy];
        set => _data[ShipDataBlock.Energy] = value;
    }

    /// <summary>The ship's maximum energy, from its blueprint.</summary>
    public byte MaxEnergy { get; set; }

    /// <summary>
    /// The forward shield (the original's FSH). For our ship only: 0 is empty, 255 is full.
    /// </summary>
    public byte ForeShield { get; set; } = 255;

    /// <summary>The aft shield (the original's ASH).</summary>
    public byte AftShield { get; set; } = 255;

    /// <summary>
    /// Whether an energy unit is fitted, which doubles the rate the energy banks recharge at (the
    /// original's ENGY).
    /// </summary>
    public bool HasEnergyUnit { get; set; }

    /// <summary>
    /// The blueprint's visibility distance: beyond this many multiples of 256 units (the original
    /// compares it against z_hi) the ship is drawn as a dot rather than a model.
    /// </summary>
    public int VisibilityDistance { get; set; } = 255;

    /// <summary>The ship's maximum speed, from its blueprint.</summary>
    public int MaxSpeed { get; set; }

    /// <summary>True if the ship is exploding.</summary>
    public bool IsExploding
    {
        get => (Flags & ShipDataBlock.FlagExploding) != 0;
        set => Flags = value ? (byte)(Flags | ShipDataBlock.FlagExploding) : (byte)(Flags & ~ShipDataBlock.FlagExploding);
    }

    /// <summary>True if the ship has been killed and should be removed from the bubble.</summary>
    public bool IsKilled
    {
        get => (Flags & ShipDataBlock.FlagKilled) != 0;
        set => Flags = value ? (byte)(Flags | ShipDataBlock.FlagKilled) : (byte)(Flags & ~ShipDataBlock.FlagKilled);
    }

    /// <summary>
    /// Reads one of the ship's coordinates. A coordinate is three bytes — low, high, sign — with
    /// the sign in bit 7 of the third byte, so its magnitude has 23 bits and values run to over
    /// eight million, which is what lets the planet and sun sit millions of units away.
    /// </summary>
    public int GetCoordinate(int offset)
    {
        int magnitude = _data[offset] | (_data[offset + 1] << 8) | ((_data[offset + 2] & 0x7F) << 16);
        return (_data[offset + 2] & 0x80) != 0 ? -magnitude : magnitude;
    }

    /// <summary>Writes one of the ship's coordinates from a signed value.</summary>
    public void SetCoordinate(int offset, int value)
    {
        int magnitude = Math.Abs(value);
        _data[offset] = (byte)(magnitude & 0xFF);
        _data[offset + 1] = (byte)((magnitude >> 8) & 0xFF);
        _data[offset + 2] = (byte)(((magnitude >> 16) & 0x7F) | (value < 0 ? 0x80 : 0x00));
    }

    /// <summary>The ship's position as a signed vector, for rendering.</summary>
    public (int X, int Y, int Z) GetPosition() =>
        (GetCoordinate(ShipDataBlock.X), GetCoordinate(ShipDataBlock.Y), GetCoordinate(ShipDataBlock.Z));

    /// <summary>Sets the ship's position.</summary>
    public void SetPosition(int x, int y, int z)
    {
        SetCoordinate(ShipDataBlock.X, x);
        SetCoordinate(ShipDataBlock.Y, y);
        SetCoordinate(ShipDataBlock.Z, z);
    }

    /// <summary>Creates a ship with an orientation built from a heading and pitch in radians.</summary>
    public static Ship Create(int type, string blueprintId, string name, float heading, float pitch, int x, int y, int z)
    {
        var ship = new Ship(type, blueprintId, name);
        ship.SetPosition(x, y, z);
        Orientation orientation = Orientation.FromHeadingPitch(heading, pitch);
        orientation.AsSpan().CopyTo(ship.Data[ShipDataBlock.Orientation..]);
        return ship;
    }

    /// <summary>
    /// The ship this missile is chasing, or null when it is chasing us. The original tracks this in
    /// the missile's data block; a reference keeps it simple and obvious.
    /// </summary>
    public Ship? Target { get; set; }
}
