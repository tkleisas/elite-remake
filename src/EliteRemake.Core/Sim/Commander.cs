using EliteRemake.Core.Universe;

namespace EliteRemake.Core.Sim;

/// <summary>The four laser mounts on our ship.</summary>
public enum LaserMount
{
    /// <summary>The front laser, fired with "A".</summary>
    Front = 0,

    /// <summary>The rear laser.</summary>
    Rear = 1,

    /// <summary>The left laser.</summary>
    Left = 2,

    /// <summary>The right laser.</summary>
    Right = 3,
}

/// <summary>The laser types, in the order of the original's LASER values.</summary>
public enum LaserType
{
    /// <summary>No laser fitted.</summary>
    None = 0,

    /// <summary>A pulse laser, the cheapest and weakest.</summary>
    Pulse = 1,

    /// <summary>A beam laser.</summary>
    Beam = 2,

    /// <summary>A military laser, the most powerful.</summary>
    Military = 3,
}

/// <summary>
/// The commander: everything we carry from system to system.
/// </summary>
/// <remarks>
/// This is the original's commander data block, which it keeps at TP while playing and copies to
/// NA% when saving. The default commander in the disc version starts docked at Lave with 100 credits,
/// a full fuel tank, a front pulse laser and three missiles; every field here matches that block, so
/// a new game begins exactly as the original does.
/// </remarks>
public sealed class Commander
{
    /// <summary>The commander's name, as the original's default save has it.</summary>
    public string Name { get; set; } = "JAMESON";

    /// <summary>The commander's cash in tenths of a credit, as the original stores it.</summary>
    public int Cash { get; set; } = 1000;

    /// <summary>Fuel in light years, from 0 to 70.</summary>
    public int Fuel { get; set; } = 70;

    /// <summary>The galaxy we are in, 0 to 7.</summary>
    public int GalaxyNumber { get; set; }

    /// <summary>The system we are docked at, or the last one we left.</summary>
    public StarSystem CurrentSystem { get; set; }

    /// <summary>The commander's legal status; 0 is clean, higher values are more wanted.</summary>
    public int LegalStatus { get; set; }

    /// <summary>How many kills the commander has made, which sets their rating.</summary>
    public int Kills { get; set; }

    /// <summary>Number of missiles carried, 0 to 4 in the disc version.</summary>
    public int Missiles { get; set; } = 3;

    /// <summary>Cargo capacity in tonnes.</summary>
    public int CargoCapacity { get; set; } = 22;

    /// <summary>Whether the E.C.M. system is fitted.</summary>
    public bool Ecm { get; set; }

    /// <summary>Whether fuel scoops are fitted.</summary>
    public bool FuelScoops { get; set; }

    /// <summary>Whether an energy bomb is carried.</summary>
    public bool EnergyBomb { get; set; }

    /// <summary>Whether an energy unit is fitted, which doubles the recharge rate.</summary>
    public bool EnergyUnit { get; set; }

    /// <summary>Whether the docking computer is fitted.</summary>
    public bool DockingComputer { get; set; }

    /// <summary>Whether a galactic hyperdrive is fitted.</summary>
    public bool GalacticHyperdrive { get; set; }

    /// <summary>Whether an escape pod is fitted.</summary>
    public bool EscapePod { get; set; }

    /// <summary>Mission status, which the Constrictor mission advances.</summary>
    public int MissionStatus { get; set; }

    private readonly LaserType[] _lasers = new LaserType[4];
    private readonly int[] _cargo = new int[17];

    /// <summary>
    /// Creates the default commander: 100 credits, full fuel, a front pulse laser and three
    /// missiles, docked at Lave in galaxy 0.
    /// </summary>
    public static Commander CreateDefault()
    {
        var commander = new Commander
        {
            CurrentSystem = Universe.Galaxy.GenerateGalaxy(0).First(s => s.Name == "LAVE"),
        };

        commander.SetLaser(LaserMount.Front, LaserType.Pulse);
        return commander;
    }

    /// <summary>Reads the laser fitted to a mount.</summary>
    public LaserType GetLaser(LaserMount mount) => _lasers[(int)mount];

    /// <summary>Fits a laser to a mount.</summary>
    public void SetLaser(LaserMount mount, LaserType type) => _lasers[(int)mount] = type;

    /// <summary>How much of a commodity is in the hold.</summary>
    public int GetCargo(int item) => _cargo[item];

    /// <summary>How many tonnes of cargo are in the hold.</summary>
    public int CargoUsed => _cargo.Sum();

    /// <summary>How many tonnes of cargo space are left.</summary>
    public int CargoFree => CargoCapacity - CargoUsed;

    /// <summary>Adds cargo to the hold, up to the capacity.</summary>
    public int AddCargo(int item, int amount)
    {
        int space = CargoFree;
        int added = Math.Clamp(amount, 0, space);
        _cargo[item] += added;
        return added;
    }

    /// <summary>Removes cargo from the hold.</summary>
    public int RemoveCargo(int item, int amount)
    {
        int removed = Math.Clamp(amount, 0, _cargo[item]);
        _cargo[item] -= removed;
        return removed;
    }

    /// <summary>Buys an amount of a commodity, returning how much was actually bought.</summary>
    public int Buy(int item, int pricePerUnit, int amount)
    {
        int affordable = pricePerUnit > 0 ? Cash / pricePerUnit : 0;
        int bought = AddCargo(item, Math.Min(amount, affordable));
        Cash -= bought * pricePerUnit;
        return bought;
    }

    /// <summary>Sells an amount of a commodity, returning how much was actually sold.</summary>
    public int Sell(int item, int pricePerUnit, int amount)
    {
        int sold = RemoveCargo(item, amount);
        Cash += sold * pricePerUnit;
        return sold;
    }

    /// <summary>
    /// The commander's rating, from their kill count, using the original's thresholds: Harmless,
    /// Mostly Harmless, Poor, Average, Above Average, Competent, Dangerous, Deadly and finally
    /// Elite at 6400 kills.
    /// </summary>
    public string Rating => Kills switch
    {
        < 8 => "Harmless",
        < 16 => "Mostly Harmless",
        < 32 => "Poor",
        < 64 => "Average",
        < 128 => "Above Average",
        < 512 => "Competent",
        < 2560 => "Dangerous",
        < 6400 => "Deadly",
        _ => "Elite",
    };

    /// <summary>True when the commander is anything other than clean.</summary>
    public bool IsWanted => LegalStatus > 0;

    /// <summary>The legal status as the original's word for it.</summary>
    public string LegalStatusName => LegalStatus switch
    {
        0 => "Clean",
        < 24 => "Offender",
        < 48 => "Fugitive",
        _ => "Fugitive",
    };

    /// <summary>Adds a kill and returns true if the rating improved.</summary>
    public bool RegisterKill(int count = 1)
    {
        string before = Rating;
        Kills += count;
        return Rating != before;
    }

    /// <summary>Spends fuel on a hyperspace jump, returning false if there was not enough.</summary>
    public bool UseFuel(int lightYears)
    {
        if (lightYears > Fuel)
        {
            return false;
        }

        Fuel -= lightYears;
        return true;
    }
}
