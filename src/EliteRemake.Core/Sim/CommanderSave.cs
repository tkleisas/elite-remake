using System.Text.Json;
using System.Text.Json.Serialization;
using EliteRemake.Core.Universe;

namespace EliteRemake.Core.Sim;

/// <summary>
/// The commander's saved state, as a modern equivalent of the original's commander block.
/// </summary>
/// <remarks>
/// The original saves a 76-byte block to disc, ending in two checksum bytes, and the block's layout
/// is tied to BBC filing systems and to the game's own workspace. This remake saves the same
/// information — everything the commander carries and everything about where they are — as a small
/// JSON document instead, with a version number so an old save can be recognised, and it validates
/// what it reads rather than trusting it.
///
/// The fields and their meanings are the original's: cash in tenths of a credit, fuel in tenths of
/// a light year, the galaxy number, the system's seeds, the laser loadout, the equipment flags, the
/// hold contents and the kill tally.
/// </remarks>
public sealed class CommanderSave
{
    /// <summary>The version of the save format this code writes.</summary>
    public const int CurrentVersion = 1;

    /// <summary>The format version the file was written with.</summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>The commander's name.</summary>
    public string Name { get; set; } = "JAMESON";

    /// <summary>Cash in tenths of a credit.</summary>
    public int Cash { get; set; } = 1000;

    /// <summary>Fuel in tenths of a light year.</summary>
    public int Fuel { get; set; } = 70;

    /// <summary>The galaxy number, 0 to 7.</summary>
    public int Galaxy { get; set; }

    /// <summary>The system's index within its galaxy.</summary>
    public int SystemIndex { get; set; }

    /// <summary>The three system seeds, packed as the original stores them.</summary>
    public int Seed0 { get; set; }

    /// <summary>The second seed.</summary>
    public int Seed1 { get; set; }

    /// <summary>The third seed.</summary>
    public int Seed2 { get; set; }

    /// <summary>The legal status.</summary>
    public int LegalStatus { get; set; }

    /// <summary>The kill tally.</summary>
    public int Kills { get; set; }

    /// <summary>Missiles carried.</summary>
    public int Missiles { get; set; } = 3;

    /// <summary>Cargo capacity in tonnes.</summary>
    public int CargoCapacity { get; set; } = 22;

    /// <summary>Equipment flags, as the original's separate bytes.</summary>
    public bool Ecm { get; set; }

    /// <summary>Whether fuel scoops are fitted.</summary>
    public bool FuelScoops { get; set; }

    /// <summary>Whether an energy bomb is carried.</summary>
    public bool EnergyBomb { get; set; }

    /// <summary>
    /// The energy unit fitted: none, a standard unit from the shop, or the navy unit mission 2
    /// awards. Saved under the original's own name for the field, and read as a number — an older
    /// save carries a plain true/false for the standard unit, which is read as 1 or 0.
    /// </summary>
    [JsonPropertyName("EnergyUnit")]
    [JsonConverter(typeof(EnergyUnitLevelConverter))]
    public int EnergyUnitLevel { get; set; }

    /// <summary>Whether a docking computer is fitted.</summary>
    public bool DockingComputer { get; set; }

    /// <summary>Whether a galactic hyperdrive is fitted.</summary>
    public bool GalacticHyperdrive { get; set; }

    /// <summary>Whether an escape pod is fitted.</summary>
    public bool EscapePod { get; set; }

    /// <summary>Mission status.</summary>
    public int MissionStatus { get; set; }

    /// <summary>The four laser mounts, front, rear, left and right.</summary>
    public int[] Lasers { get; set; } = [1, 0, 0, 0];

    /// <summary>How much of each commodity is in the hold.</summary>
    public int[] Cargo { get; set; } = new int[17];

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>
    /// Reads the energy unit as either a number (this version) or a boolean (an older save, whose
    /// true is the standard unit), and writes a number.
    /// </summary>
    private sealed class EnergyUnitLevelConverter : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType switch
            {
                JsonTokenType.True => Commander.StandardEnergyUnit,
                JsonTokenType.False => Commander.NoEnergyUnit,
                JsonTokenType.Number => reader.GetInt32(),
                _ => throw new JsonException("The energy unit entry is neither a number nor a boolean."),
            };

        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(value);
    }

    /// <summary>Captures a commander's state.</summary>
    public static CommanderSave FromCommander(Commander commander)
    {
        var save = new CommanderSave
        {
            Name = commander.Name,
            Cash = commander.Cash,
            Fuel = commander.Fuel,
            Galaxy = commander.GalaxyNumber,
            SystemIndex = commander.CurrentSystem.Index,
            Seed0 = commander.CurrentSystem.Seeds.S0,
            Seed1 = commander.CurrentSystem.Seeds.S1,
            Seed2 = commander.CurrentSystem.Seeds.S2,
            LegalStatus = commander.LegalStatus,
            Kills = commander.Kills,
            Missiles = commander.Missiles,
            CargoCapacity = commander.CargoCapacity,
            Ecm = commander.Ecm,
            FuelScoops = commander.FuelScoops,
            EnergyBomb = commander.EnergyBomb,
            EnergyUnitLevel = commander.EnergyUnitLevel,
            DockingComputer = commander.DockingComputer,
            GalacticHyperdrive = commander.GalacticHyperdrive,
            EscapePod = commander.EscapePod,
            MissionStatus = commander.MissionStatus,
            Lasers = new int[4],
            Cargo = new int[17],
        };

        foreach (LaserMount mount in Enum.GetValues<LaserMount>())
        {
            save.Lasers[(int)mount] = (int)commander.GetLaser(mount);
        }

        for (int item = 0; item < 17; item++)
        {
            save.Cargo[item] = commander.GetCargo(item);
        }

        return save;
    }

    /// <summary>Rebuilds a commander from a saved state.</summary>
    public Commander ToCommander()
    {
        SystemSeeds seeds = new((ushort)Seed0, (ushort)Seed1, (ushort)Seed2);

        var commander = new Commander
        {
            Name = Name,
            Cash = Cash,
            Fuel = Math.Clamp(Fuel, 0, 70),
            GalaxyNumber = Math.Clamp(Galaxy, 0, Universe.Galaxy.GalaxyCount - 1),
            LegalStatus = Math.Clamp(LegalStatus, 0, 255),
            Kills = Math.Max(0, Kills),
            Missiles = Math.Clamp(Missiles, 0, 4),
            CargoCapacity = Math.Clamp(CargoCapacity, 1, 255),
            Ecm = Ecm,
            FuelScoops = FuelScoops,
            EnergyBomb = EnergyBomb,
            EnergyUnitLevel = Math.Clamp(EnergyUnitLevel, Commander.NoEnergyUnit, Commander.NavalEnergyUnit),
            DockingComputer = DockingComputer,
            GalacticHyperdrive = GalacticHyperdrive,
            EscapePod = EscapePod,
            MissionStatus = MissionStatus,
            CurrentSystem = Universe.Galaxy.Describe(seeds, SystemIndex),
        };

        for (int mount = 0; mount < 4 && mount < Lasers.Length; mount++)
        {
            // A saved laser is validated rather than clamped, so a mining laser survives the round
            // trip: clamping into 0-3 silently turned one into a military laser, which is a more
            // expensive weapon than the one the commander saved
            int saved = Lasers[mount];
            LaserType type = Enum.IsDefined(typeof(LaserType), saved) ? (LaserType)saved : LaserType.None;
            commander.SetLaser((LaserMount)mount, type);
        }

        for (int item = 0; item < 17 && item < Cargo.Length; item++)
        {
            commander.AddCargo(item, Math.Max(0, Cargo[item]));
        }

        return commander;
    }

    /// <summary>Serialises the save to JSON.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>
    /// Parses a save from JSON, throwing if it is not a save file this version understands.
    /// </summary>
    /// <exception cref="InvalidDataException">The file is not a valid save.</exception>
    public static CommanderSave FromJson(string json)
    {
        CommanderSave? save;
        try
        {
            save = JsonSerializer.Deserialize<CommanderSave>(json, Options);
        }
        catch (JsonException error)
        {
            throw new InvalidDataException($"The save file could not be read: {error.Message}", error);
        }

        if (save is null)
        {
            throw new InvalidDataException("The save file is empty.");
        }

        if (save.Version != CurrentVersion)
        {
            throw new InvalidDataException(
                $"The save file is version {save.Version}, but this build reads version {CurrentVersion}.");
        }

        if (save.Cargo.Length != 17 || save.Lasers.Length != 4)
        {
            throw new InvalidDataException("The save file is missing the hold or the laser mounts.");
        }

        return save;
    }
}
