using System.Reflection;
using System.Text.Json;

namespace EliteRemake.Data.Ships;

/// <summary>
/// Loads the ship blueprint data extracted from the original BBC Micro disc Elite sources.
///
/// The data is embedded in this assembly (data/ships.json in the repository), so the game needs no
/// external data directory. Loading is lazy and thread-safe: the JSON is parsed the first time any
/// member is used.
/// </summary>
public static class ShipData
{
    /// <summary>The logical name of the embedded resource holding the ship data.</summary>
    public const string ResourceName = "EliteRemake.Data.data.ships.json";

    /// <summary>The schema version this loader understands.</summary>
    public const int SupportedSchemaVersion = 1;

    private static readonly Lazy<ShipDataDocument> LazyDocument =
        new(LoadDocument, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<Lookup> LazyLookup = new(() => new Lookup(Document));

    /// <summary>The complete extracted document.</summary>
    public static ShipDataDocument Document => LazyDocument.Value;

    /// <summary>Every extracted ship blueprint, ordered by lowest registered ship type.</summary>
    public static IReadOnlyList<ShipBlueprint> All => Document.Ships;

    /// <summary>The XX21 lookup tables: D.MOA ... D.MOP and the docked (ship hangar) table.</summary>
    public static IReadOnlyList<ShipSet> ShipSets => Document.ShipSets;

    /// <summary>Extraction notes.</summary>
    public static IReadOnlyList<string> Notes => Document.Notes;

    /// <summary>Summary counts for the extracted data.</summary>
    public static ShipDataCounts Counts => Document.Counts;

    /// <summary>
    /// The ship blueprint registered at the given ship type.
    ///
    /// Type numbers are not unique across all ship sets (type 2 is the Coriolis in some files and the
    /// Dodo in others), so this returns the registration from the first ship set that uses the type,
    /// in D.MOA ... D.MOP, docked order. Use <see cref="AllForType"/> for every registration.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No ship set registers the given ship type.</exception>
    public static ShipBlueprint ByType(int shipType)
    {
        ShipBlueprint? blueprint = LazyLookup.Value.PrimaryByType.GetValueOrDefault(shipType);
        return blueprint ?? throw new KeyNotFoundException($"no ship blueprint registered for ship type {shipType}");
    }

    /// <summary>Non-throwing variant of <see cref="ByType"/>.</summary>
    public static bool TryGetByType(int shipType, out ShipBlueprint? blueprint) =>
        LazyLookup.Value.PrimaryByType.TryGetValue(shipType, out blueprint);

    /// <summary>Every ship registered at the given ship type, in ship set order.</summary>
    public static IReadOnlyList<ShipBlueprint> AllForType(int shipType) =>
        LazyLookup.Value.AllByType.TryGetValue(shipType, out List<ShipBlueprint>? ships) ? ships : [];

    /// <summary>The ship with the given id (e.g. "cobra-mk-3"), or null.</summary>
    public static ShipBlueprint? ById(string id) => LazyLookup.Value.ById.GetValueOrDefault(id);

    /// <summary>
    /// The default NEWB flags for a ship type, from the disc's <c>E%</c> table, or 0 for a type the
    /// table does not cover.
    /// </summary>
    /// <remarks>
    /// A type with no entry gets no flags rather than a guess: the table leaves most types at zero,
    /// and inventing flags for the others would be exactly the kind of inference that has been wrong
    /// everywhere else in this port.
    /// </remarks>
    public static byte NewbFlagsFor(int type)
    {
        foreach (NewbFlagEntry entry in LazyDocument.Value.NewbFlags)
        {
            if (entry.Type == type)
            {
                return (byte)entry.Flags;
            }
        }

        return 0;
    }

    /// <summary>The ship set with the given id ("D.MOA" ... "D.MOP" or "docked"), or null.</summary>
    public static ShipSet? ShipSetById(string id)
    {
        foreach (ShipSet set in Document.ShipSets)
        {
            if (string.Equals(set.Id, id, StringComparison.Ordinal))
            {
                return set;
            }
        }

        return null;
    }

    private static ShipDataDocument LoadDocument()
    {
        Assembly assembly = typeof(ShipData).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"embedded resource '{ResourceName}' not found in {assembly.GetName().Name}; "
                + $"available resources: {string.Join(", ", assembly.GetManifestResourceNames())}");

        ShipDataDocument? document = JsonSerializer.Deserialize<ShipDataDocument>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (document is null)
        {
            throw new InvalidDataException($"embedded resource '{ResourceName}' is empty or not valid JSON");
        }

        if (document.SchemaVersion != SupportedSchemaVersion)
        {
            throw new InvalidDataException(
                $"embedded resource '{ResourceName}' has schema version {document.SchemaVersion}, "
                + $"but this build understands version {SupportedSchemaVersion}");
        }

        return document;
    }

    private sealed class Lookup
    {
        public Lookup(ShipDataDocument document)
        {
            foreach (ShipBlueprint ship in document.Ships)
            {
                ById[ship.Id] = ship;
            }

            // Walk the ship sets in document order (D.MOA ... D.MOP, docked) so that the first
            // registration of a contested ship type wins.
            foreach (ShipSet set in document.ShipSets)
            {
                foreach (ShipSetEntry entry in set.Entries)
                {
                    if (entry.Ship is null || !ById.TryGetValue(entry.Ship, out ShipBlueprint? ship))
                    {
                        continue;
                    }

                    if (!PrimaryByType.ContainsKey(entry.Type))
                    {
                        PrimaryByType[entry.Type] = ship;
                    }

                    if (!AllByType.TryGetValue(entry.Type, out List<ShipBlueprint>? ships))
                    {
                        ships = [];
                        AllByType[entry.Type] = ships;
                    }

                    if (!ships.Contains(ship))
                    {
                        ships.Add(ship);
                    }
                }
            }
        }

        public Dictionary<string, ShipBlueprint> ById { get; } = new(StringComparer.Ordinal);

        public Dictionary<int, ShipBlueprint> PrimaryByType { get; } = [];

        public Dictionary<int, List<ShipBlueprint>> AllByType { get; } = [];
    }
}
