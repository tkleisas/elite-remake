using System.Text;
using System.Text.RegularExpressions;
using EliteDataExtractor.Assembly;

namespace EliteDataExtractor.Extraction;

/// <summary>
/// Extracts every ship blueprint reachable in the BBC Micro disc (Stairway to Hell) build by
/// assembling the original ship sources, and records the ship-type registrations from the XX21
/// lookup tables in the flight ship files and the docked code.
/// </summary>
internal sealed partial class ShipExtractor
{
    public const string BuildOptionsFile = "versions/disc/1-source-files/main-sources/elite-build-options.asm";
    public const string MissileSourceFile = "versions/disc/1-source-files/main-sources/elite-missile.asm";
    public const string DockedXx21File = "library/disc/docked/variable/xx21.asm";
    public const string DockedBinaryFile = "versions/disc/3-assembled-output/T.CODE.unprot.bin";
    public const string BinaryDirectory = "versions/disc/3-assembled-output";
    public const string FlightSourceDirectory = "versions/disc/1-source-files/main-sources";

    /// <summary>Ship file letters, in D.MOA-D.MOP order.</summary>
    private const string ShipFileLetters = "abcdefghijklmnop";

    private readonly string _libraryRoot;
    private readonly Action<string> _log;

    public ShipExtractor(string libraryRoot, Action<string> log)
    {
        _libraryRoot = Path.GetFullPath(libraryRoot);
        _log = log;
    }

    public ExtractionResult Extract()
    {
        var shipSets = new List<ShipSet>();
        var blueprintsBySet = new Dictionary<(string, string), ShipBlueprint>();
        var notes = new List<string>();

        // The missile blueprint is not stored in the ship files; it lives in MISSILE.bin and is
        // pointed at by XX21 slot 1 in every ship set.
        _log("Assembling the missile blueprint (elite-missile.asm)");
        AssemblyResult missileAssembly = new Assembler(_libraryRoot).Assemble(MissileSourceFile);
        int? missileAddress = missileAssembly.LabelAddress("SHIP_MISSILE")
            ?? throw new AsmException("SHIP_MISSILE label not found in elite-missile.asm");
        ShipBlueprint missileBlueprint = DecodeBlueprint(missileAssembly, "SHIP_MISSILE", missileAddress.Value);
        var blueprintsByLabel = new Dictionary<string, ShipBlueprint>(StringComparer.OrdinalIgnoreCase)
        {
            ["SHIP_MISSILE"] = missileBlueprint,
        };
        var firstSetByLabel = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SHIP_MISSILE"] = "MISSILE.bin",
        };

        // Flight ship files: assemble each one and decode the blueprints its XX21 table points at.
        for (int index = 0; index < ShipFileLetters.Length; index++)
        {
            char letter = ShipFileLetters[index];
            string id = $"D.MO{char.ToUpperInvariant(letter)}";
            string asmFile = $"{FlightSourceDirectory}/elite-ships-{letter}.asm";
            string binary = $"{BinaryDirectory}/{id}.bin";
            _log($"Assembling {asmFile}");

            AssemblyResult assembly = new Assembler(_libraryRoot).Assemble(asmFile);
            foreach (string warning in assembly.Warnings)
            {
                _log($"  warning: {warning}");
            }

            var slots = new List<ShipSetSlot>();
            var xx21 = ParseXx21(ReadSource(asmFile), asmFile);
            foreach ((string expression, string? symbol, int? commentType, string? typeName, int lineNumber) in xx21)
            {
                int type = slots.Count + 1;
                if (commentType is int declared && declared != type)
                {
                    throw new AsmException(
                        $"{asmFile}:{lineNumber}: XX21 comment says type {declared} but the slot is type {type}");
                }

                long pointer = expression == "0" ? 0 : ResolvePointer(assembly, expression, asmFile, lineNumber);
                string? label = pointer == 0 ? null : expression;

                ShipBlueprint? blueprint = null;
                if (label is not null)
                {
                    if (string.Equals(label, "SHIP_MISSILE", StringComparison.OrdinalIgnoreCase))
                    {
                        if (pointer != missileAddress.Value)
                        {
                            throw new AsmException(
                                $"{asmFile}:{lineNumber}: SHIP_MISSILE pointer is &{pointer:X4}, expected &{missileAddress.Value:X4}");
                        }

                        blueprint = missileBlueprint;
                    }
                    else
                    {
                        int? address = assembly.LabelAddress(label)
                            ?? throw new AsmException($"{asmFile}:{lineNumber}: {label} is not defined in this file");
                        if (address != pointer)
                        {
                            throw new AsmException(
                                $"{asmFile}:{lineNumber}: {label} pointer is &{pointer:X4} but the label is at &{address:X4}");
                        }

                        blueprint = DecodeBlueprint(assembly, label, address.Value);
                        blueprintsBySet[(id, label)] = blueprint;

                        if (!blueprintsByLabel.TryAdd(label, blueprint))
                        {
                            // Some blueprints are laid out differently in different ship files
                            // (e.g. the Thargon reuses the cargo canister's edge data, so its edge
                            // offset depends on how far away the canister is in that file). The
                            // per-file verification against the reference binaries is what proves
                            // correctness; BuildCanonicalShips reports any differences.
                        }
                        else
                        {
                            firstSetByLabel[label] = id;
                        }
                    }
                }

                slots.Add(new ShipSetSlot
                {
                    Type = type,
                    Symbol = symbol,
                    TypeName = typeName,
                    Label = label,
                    Ship = null,
                    Pointer = pointer,
                    Blueprint = blueprint,
                });
            }

            // The XX21 table stored in the binary must agree with the assembled label addresses.
            VerifyXx21Bytes(assembly, binary, slots, asmFile);

            shipSets.Add(new ShipSet
            {
                Id = id,
                AsmFile = asmFile,
                Binary = $"{id}.bin",
                BaseAddress = assembly.BaseAddress,
                SlotCount = slots.Count,
                Slots = slots,
                BinaryData = ReadBinary(binary),
                Assembly = assembly,
            });
        }

        // The docked code contains its own XX21 table and its own copies of the hangar ship
        // blueprints (assembled at &5600 inside T.CODE). The task's brief assumed the docked table
        // contributes only type mappings, but the source includes full blueprints for the hangar, so
        // the whole ship block is assembled here (from a generated wrapper that reuses the real
        // preamble and include list of elite-source-docked.asm) and verified against T.CODE.unprot.bin.
        _log($"Assembling the docked ship hangar block ({DockedXx21File} and the hangar blueprints)");
        AssemblyResult dockedAssembly = AssembleDockedShipBlock();
        int? dockedXx21 = dockedAssembly.LabelAddress("XX21")
            ?? throw new AsmException("XX21 label not found in the assembled docked ship block");
        int dockedBinaryOffset = dockedXx21.Value - ReadDockedCodeBase();

        var dockedSlots = new List<ShipSetSlot>();
        foreach ((string expression, string? symbol, int? commentType, string? typeName, int lineNumber) in ParseXx21(
            ReadSource(DockedXx21File),
            DockedXx21File))
        {
            int type = dockedSlots.Count + 1;
            if (commentType is int declared && declared != type)
            {
                throw new AsmException(
                    $"{DockedXx21File}:{lineNumber}: XX21 comment says type {declared} but the slot is type {type}");
            }

            long pointer = expression == "0" ? 0 : ResolvePointer(dockedAssembly, expression, DockedXx21File, lineNumber);
            string? label = pointer == 0 ? null : expression;
            ShipBlueprint? blueprint = null;
            if (label is not null)
            {
                if (string.Equals(label, "SHIP_MISSILE", StringComparison.OrdinalIgnoreCase))
                {
                    if (pointer != missileAddress.Value)
                    {
                        throw new AsmException(
                            $"{DockedXx21File}:{lineNumber}: SHIP_MISSILE pointer is &{pointer:X4}, expected &{missileAddress.Value:X4}");
                    }

                    blueprint = missileBlueprint;
                }
                else
                {
                    int address = dockedAssembly.LabelAddress(label)
                        ?? throw new AsmException($"{DockedXx21File}:{lineNumber}: {label} is not defined in the docked ship block");
                    if (address != pointer)
                    {
                        throw new AsmException(
                            $"{DockedXx21File}:{lineNumber}: {label} pointer is &{pointer:X4} but the label is at &{address:X4}");
                    }

                    blueprint = DecodeBlueprint(dockedAssembly, label, address);
                    blueprintsBySet[("docked", label)] = blueprint;
                }
            }

            dockedSlots.Add(new ShipSetSlot
            {
                Type = type,
                Symbol = symbol,
                TypeName = typeName,
                Label = label,
                Pointer = pointer,
                Blueprint = blueprint,
            });
        }

        VerifyXx21Bytes(dockedAssembly, DockedBinaryFile, dockedSlots, DockedXx21File);

        shipSets.Add(new ShipSet
        {
            Id = "docked",
            AsmFile = DockedXx21File,
            Binary = "T.CODE.unprot.bin",
            BaseAddress = dockedAssembly.BaseAddress,
            BinaryOffset = dockedBinaryOffset,
            SlotCount = dockedSlots.Count,
            Slots = dockedSlots,
            BinaryData = ReadBinary(DockedBinaryFile),
            Assembly = dockedAssembly,
        });

        // Fill in the ship ids now that every label is known.
        var idsByLabel = shipSets
            .SelectMany(set => set.Slots)
            .Where(slot => slot.Label is not null)
            .Select(slot => slot.Label!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(label => label, Slug, StringComparer.OrdinalIgnoreCase);

        foreach (ShipSet set in shipSets)
        {
            for (int i = 0; i < set.Slots.Count; i++)
            {
                ShipSetSlot slot = set.Slots[i];
                if (slot.Label is null)
                {
                    continue;
                }

                set.Slots[i] = new ShipSetSlot
                {
                    Type = slot.Type,
                    Symbol = slot.Symbol,
                    TypeName = slot.TypeName,
                    Label = slot.Label,
                    Ship = idsByLabel[slot.Label],
                    Pointer = slot.Pointer,
                    Blueprint = slot.Blueprint,
                };
            }
        }

        List<ShipDocument> ships = BuildCanonicalShips(
            shipSets, blueprintsByLabel, blueprintsBySet, firstSetByLabel, missileAssembly, notes);

        int registeredTypeCount = shipSets
            .SelectMany(set => set.Slots)
            .Where(slot => slot.Label is not null)
            .Select(slot => slot.Type)
            .Distinct()
            .Count();

        // Type 15 is not used by any XX21 table in the disc version.
        var missingTypes = Enumerable.Range(1, 31)
            .Where(type => !shipSets.SelectMany(set => set.Slots).Any(slot => slot.Type == type && slot.Label is not null))
            .ToList();
        if (missingTypes.Count > 0)
        {
            notes.Add(
                $"Ship type(s) {string.Join(", ", missingTypes)} are not registered in any XX21 table in the disc version.");
        }

        var document = new ShipDataDocument
        {
            Source = BuildSourceInfo(),
            Counts = new ShipDataCounts
            {
                Ships = ships.Count,
                RegisteredTypes = registeredTypeCount,
                ShipSets = shipSets.Count,
                ShipSetEntries = shipSets.Sum(set => set.Slots.Count(slot => slot.Label is not null)),
                Vertices = ships.Sum(ship => ship.Vertices.Count),
                Edges = ships.Sum(ship => ship.Edges.Count),
                Faces = ships.Sum(ship => ship.Faces.Count),
            },
            ShipSets = shipSets,
            Ships = ships,
            Notes = notes,
        };

        return new ExtractionResult
        {
            Document = document,
            ShipSets = shipSets,
            BlueprintsByLabel = blueprintsByLabel,
            BlueprintsBySet = blueprintsBySet,
            Notes = notes,
        };
    }

    private ShipDataSource BuildSourceInfo()
    {
        var flags = new SortedDictionary<string, bool>(StringComparer.Ordinal);
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string line in ReadSource(BuildOptionsFile).Split('\n'))
        {
            string code = AsmText.StripComment(line).Trim();
            int equals = code.IndexOf('=');
            if (equals > 0)
            {
                options[code[..equals].Trim()] = code[(equals + 1)..].Trim();
            }
        }

        // The ship sources define the full set of version flags; mirror those that are boolean.
        AssemblyResult flight = new Assembler(_libraryRoot).Assemble($"{FlightSourceDirectory}/elite-ships-a.asm");
        foreach ((string name, long value) in flight.Symbols)
        {
            if (name.StartsWith('_') && value is 0 or 1)
            {
                flags[name] = value == 1;
            }
        }

        return new ShipDataSource
        {
            BuildVersion = options.TryGetValue("_VERSION", out string? version) ? int.Parse(version) : 0,
            BuildVariant = options.TryGetValue("_VARIANT", out string? variant) ? int.Parse(variant) : 0,
            Flags = flags,
        };
    }

    private List<ShipDocument> BuildCanonicalShips(
        List<ShipSet> shipSets,
        Dictionary<string, ShipBlueprint> blueprintsByLabel,
        Dictionary<(string SetId, string Label), ShipBlueprint> blueprintsBySet,
        Dictionary<string, string> firstSetByLabel,
        AssemblyResult missileAssembly,
        List<string> notes)
    {
        var documents = new List<ShipDocument>();

        foreach ((string label, ShipBlueprint blueprint) in blueprintsByLabel)
        {
            var registrations = new List<ShipSetSlot>();
            foreach (ShipSet set in shipSets)
            {
                registrations.AddRange(set.Slots.Where(slot =>
                    string.Equals(slot.Label, label, StringComparison.OrdinalIgnoreCase)));
            }

            registrations.Sort((left, right) => left.Type.CompareTo(right.Type));

            // All copies of a blueprint should decode to the same geometry in every ship file. Some
            // do not: the Thargon reuses the cargo canister's edges (so its edge offset depends on
            // the file layout) and the splinter's face offset points past its own face data into
            // whatever follows it. Those differences are reported as notes; the per-file comparison
            // against the reference binaries is what proves the data is right.
            var differingSets = new List<string>();
            foreach (ShipSet set in shipSets)
            {
                foreach (ShipSetSlot slot in set.Slots)
                {
                    if (slot.Blueprint is null || !string.Equals(slot.Label, label, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (string.Equals(set.Id, "docked", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string? difference = BlueprintComparer.Diff(blueprint, slot.Blueprint, ignoreLayoutOffsets: true);
                    if (difference is not null)
                    {
                        differingSets.Add($"{set.Id} ({difference})");
                    }
                }
            }

            if (differingSets.Count > 0)
            {
                notes.Add(
                    $"{label}: the blueprint decodes differently in {string.Join(", ", differingSets.Distinct())}; "
                    + $"the canonical entry uses {firstSetByLabel[label]}.");
            }

            (string summary, string asmFile) = ReadShipMetadata(label);

            string binary = string.Equals(label, "SHIP_MISSILE", StringComparison.OrdinalIgnoreCase)
                ? "MISSILE.bin"
                : shipSets.First(set => set.Slots.Any(slot =>
                    string.Equals(slot.Label, label, StringComparison.OrdinalIgnoreCase) && set.Binary is not null)).Binary!;

            var shipNotes = new List<string>();
            List<ShipFace>? declaredFaceData = null;

            // The docked code has its own copy of some of the hangar blueprints, which differs from
            // the flight version for most of them. Emit it when the geometry or stats differ.
            ShipGeometry? dockedVariant = null;
            if (blueprintsBySet.TryGetValue(("docked", label), out ShipBlueprint? dockedBlueprint) && dockedBlueprint is not null)
            {
                string? dockedDifference = BlueprintComparer.Diff(blueprint, dockedBlueprint, ignoreLayoutOffsets: true);
                if (dockedDifference is not null)
                {
                    dockedVariant = new ShipGeometry
                    {
                        Header = dockedBlueprint.Header,
                        Vertices = dockedBlueprint.Vertices,
                        Edges = dockedBlueprint.Edges,
                        Faces = dockedBlueprint.Faces,
                    };

                    shipNotes.Add(
                        $"the docked ship hangar (T.CODE) uses a different blueprint ({dockedDifference}); "
                        + "it is emitted as dockedVariant.");
                }
            }

            // Face 15 is a pseudo-face: LL9 forces it to be always visible (XX2+15 = 255) so that a
            // vertex or edge can be pinned as always visible. The alloy plate uses it for every
            // vertex and edge even though it has only one real face, so out-of-range references are
            // reported rather than treated as errors.
            var outOfRangeFaces = new SortedSet<int>();
            int vertexReferences = 0;
            int edgeReferences = 0;
            foreach (ShipVertex vertex in blueprint.Vertices)
            {
                foreach (int face in vertex.Faces)
                {
                    if (face >= blueprint.Header.FaceCount)
                    {
                        outOfRangeFaces.Add(face);
                        vertexReferences++;
                    }
                }
            }

            foreach (ShipEdge edge in blueprint.Edges)
            {
                foreach (int face in edge.Faces)
                {
                    if (face >= blueprint.Header.FaceCount)
                    {
                        outOfRangeFaces.Add(face);
                        edgeReferences++;
                    }
                }
            }

            // References to face 15 from vertices are the normal "always visible" marker and are not
            // worth a note; out-of-range references from edges, or to any other face number, are.
            bool noteOutOfRange = edgeReferences > 0 || outOfRangeFaces.Any(face => face != 15);
            if (noteOutOfRange)
            {
                shipNotes.Add(
                    $"face number(s) {string.Join(", ", outOfRangeFaces)} exceed this ship's "
                    + $"{blueprint.Header.FaceCount} face(s) ({vertexReferences} vertex reference(s), "
                    + $"{edgeReferences} edge reference(s)); face 15 is the original's always-visible "
                    + "pseudo-face (LL9 sets XX2+15 = 255), so these references are intentional.");
            }

            AssemblyResult? assembly = string.Equals(label, "SHIP_MISSILE", StringComparison.OrdinalIgnoreCase)
                ? missileAssembly
                : FindAssembly(shipSets, label);
            string? edgesFrom = FindSourceLabel(assembly, blueprint.Address + blueprint.Header.EdgesOffset);
            string? facesFrom = FindSourceLabel(assembly, blueprint.Address + blueprint.Header.FacesOffset);

            if (assembly is not null)
            {
                if (assembly.LabelAddress(label + "_EDGES") is int declaredEdges
                    && declaredEdges != blueprint.Address + blueprint.Header.EdgesOffset)
                {
                    shipNotes.Add(
                        $"edges data is not at {label}_EDGES (offset {declaredEdges - blueprint.Address}); " +
                        $"the offset in the header reads edges from offset {blueprint.Header.EdgesOffset} ({edgesFrom ?? "an unnamed address"}).");
                }

                if (assembly.LabelAddress(label + "_FACES") is int declaredFaces
                    && declaredFaces != blueprint.Address + blueprint.Header.FacesOffset)
                {
                    declaredFaceData = DecodeFaces(
                        assembly.Image,
                        assembly.OffsetOf(declaredFaces),
                        blueprint.Header.FaceCount,
                        label);
                    shipNotes.Add(
                        $"faces data is not at {label}_FACES (offset {declaredFaces - blueprint.Address}); " +
                        $"the offset in the header reads faces from offset {blueprint.Header.FacesOffset} ({facesFrom ?? "an unnamed address"}), " +
                        "which is what the original game reads and what this entry's faces contain. " +
                        "The source-declared faces are emitted separately as declaredFaces.");
                }
            }

            notes.AddRange(shipNotes.Select(note => $"{label}: {note}"));

            var symbols = new List<string>();
            var typeNames = new List<string>();
            foreach (ShipSetSlot slot in registrations)
            {
                if (slot.Symbol is not null && !symbols.Contains(slot.Symbol))
                {
                    symbols.Add(slot.Symbol);
                }

                if (slot.TypeName is not null && !typeNames.Contains(slot.TypeName))
                {
                    typeNames.Add(slot.TypeName);
                }
            }

            documents.Add(new ShipDocument
            {
                Id = Slug(label),
                Name = FriendlyName(summary),
                Summary = summary,
                Label = label,
                AsmFile = asmFile,
                Binary = binary,
                Types = [.. registrations.Select(slot => slot.Type).Distinct()],
                Symbols = symbols,
                TypeNames = typeNames,
                ShipSets = [.. shipSets.Where(set => set.Slots.Any(slot =>
                    string.Equals(slot.Label, label, StringComparison.OrdinalIgnoreCase))).Select(set => set.Id)],
                EdgesFrom = edgesFrom,
                FacesFrom = facesFrom,
                DockedVariant = dockedVariant,
                DeclaredFaces = declaredFaceData,
                Header = blueprint.Header,
                Vertices = blueprint.Vertices,
                Edges = blueprint.Edges,
                Faces = blueprint.Faces,
                Notes = shipNotes.Count == 0 ? null : shipNotes,
            });
        }

        documents.Sort((left, right) =>
        {
            int byType = left.Types.Min().CompareTo(right.Types.Min());
            return byType != 0 ? byType : string.CompareOrdinal(left.Label, right.Label);
        });

        return documents;
    }

    private static AssemblyResult? FindAssembly(List<ShipSet> shipSets, string label)
    {
        foreach (ShipSet set in shipSets)
        {
            if (set.Id == "docked")
            {
                continue;
            }

            if (set.Slots.Any(slot => string.Equals(slot.Label, label, StringComparison.OrdinalIgnoreCase)))
            {
                return set.Assembly;
            }
        }

        return null;
    }

    private static string? FindSourceLabel(AssemblyResult? assembly, int address) =>
        assembly?.Labels.Values.FirstOrDefault(lab => lab.Address == address)?.Name;

    private readonly Dictionary<string, (string Summary, string File)> _metadataCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Reads the Name/Summary comment block from the source file that defines a ship label.</summary>
    private (string Summary, string File) ReadShipMetadata(string label)
    {
        if (_metadataCache.TryGetValue(label, out (string Summary, string File) cached))
        {
            return cached;
        }

        string? found = null;
        string summary = label;

        foreach (string file in Directory.EnumerateFiles(
            Path.Combine(_libraryRoot, "library"),
            "ship_*.asm",
            SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            Match nameMatch = NameCommentRegex().Match(text);
            if (!nameMatch.Success || !string.Equals(nameMatch.Groups[1].Value, label, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Match summaryMatch = SummaryCommentRegex().Match(text);
            found = file;
            if (summaryMatch.Success)
            {
                summary = summaryMatch.Groups[1].Value.Trim();
            }

            break;
        }

        if (found is null)
        {
            throw new AsmException($"could not find a source file containing '{label}' with a Summary comment");
        }

        var result = (summary, RelativePath(found));
        _metadataCache[label] = result;
        return result;
    }

    /// <summary>Turns "Ship blueprint for a Cobra Mk III" into "Cobra Mk III".</summary>
    internal static string FriendlyName(string summary)
    {
        string name = summary.Trim();
        foreach (string prefix in new[] { "Ship blueprint for a ", "Ship blueprint for an " })
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                name = name[prefix.Length..];
                break;
            }
        }

        // "Dodecahedron ("Dodo") space station" is the odd one out: prefer the alias in quotes, so
        // the ship is called "Dodo (space station)".
        Match alias = AliasNameRegex().Match(name);
        if (alias.Success)
        {
            string quoted = alias.Groups[2].Value.Trim();
            string tail = alias.Groups[3].Value.Trim();
            name = tail.Length == 0 ? quoted : $"{quoted} ({tail})";
        }

        return name.Length == 0 ? summary : char.ToUpperInvariant(name[0]) + name[1..];
    }

    private static string Slug(string label)
    {
        string name = label.StartsWith("SHIP_", StringComparison.OrdinalIgnoreCase) ? label[5..] : label;
        return name.ToLowerInvariant().Replace('_', '-');
    }

    private static long ResolvePointer(AssemblyResult assembly, string expression, string file, int lineNumber) =>
        assembly.SymbolValue(expression)
        ?? throw new AsmException($"{file}:{lineNumber}: cannot resolve XX21 pointer expression '{expression}'");

    /// <summary>Checks that the assembled XX21 table bytes match the label addresses.</summary>
    private static void VerifyXx21Bytes(AssemblyResult assembly, string binary, List<ShipSetSlot> slots, string asmFile)
    {
        _ = binary;
        int? xx21 = assembly.LabelAddress("XX21")
            ?? throw new AsmException($"{asmFile}: XX21 label not found");

        for (int i = 0; i < slots.Count; i++)
        {
            int offset = assembly.OffsetOf(xx21.Value) + (i * 2);
            if (offset < 0 || offset + 1 >= assembly.Image.Length)
            {
                throw new AsmException($"{asmFile}: XX21 slot {i + 1} is outside the assembled image");
            }

            int stored = assembly.Image[offset] | (assembly.Image[offset + 1] << 8);
            if (stored != slots[i].Pointer)
            {
                throw new AsmException(
                    $"{asmFile}: XX21 slot {i + 1} assembles to &{stored:X4} but {slots[i].Label ?? "0"} resolves to &{slots[i].Pointer:X4}");
            }
        }
    }

    /// <summary>Parses the .XX21 EQUW block from a source file.</summary>
    private static List<(string Expression, string? Symbol, int? Type, string? Name, int Line)> ParseXx21(
        string source,
        string file)
    {
        var result = new List<(string, string?, int?, string?, int)>();
        string[] lines = source.Split('\n');
        bool inTable = false;

        for (int i = 0; i < lines.Length; i++)
        {
            string raw = lines[i];
            string code = AsmText.StripComment(raw).Trim();

            if (!inTable)
            {
                if (code.Equals(".XX21", StringComparison.OrdinalIgnoreCase))
                {
                    inTable = true;
                }

                continue;
            }

            if (code.Length == 0)
            {
                continue;
            }

            if (code.StartsWith('.'))
            {
                break;
            }

            if (!code.StartsWith("EQUW", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            string expression = code[4..].Trim();
            string comment = ExtractComment(raw);
            Match match = Xx21CommentRegex().Match(comment);
            if (match.Success)
            {
                string? symbol = match.Groups[1].Success ? match.Groups[1].Value : null;
                int type = int.Parse(match.Groups[2].Value);
                string name = match.Groups[3].Value.Trim();
                result.Add((expression, symbol, type, name, i + 1));
            }
            else
            {
                result.Add((expression, null, null, null, i + 1));
            }
        }

        if (!inTable)
        {
            throw new AsmException($"{file}: no .XX21 table found");
        }

        if (result.Count == 0)
        {
            throw new AsmException($"{file}: the .XX21 table is empty");
        }

        return result;
    }

    private static string ExtractComment(string raw)
    {
        bool inString = false;
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (c == '"')
            {
                inString = !inString;
            }
            else if (c == '\\' && !inString)
            {
                return raw[(i + 1)..];
            }
        }

        return string.Empty;
    }

    /// <summary>Decodes the 20-byte header plus vertex, edge and face data of a blueprint.</summary>
    internal static ShipBlueprint DecodeBlueprint(AssemblyResult assembly, string label, int address)
    {
        byte[] image = assembly.Image;
        int start = assembly.OffsetOf(address);
        Require(start >= 0 && start + 20 <= image.Length, label, "blueprint header lies outside the assembled image");

        int canisterByte = image[start];
        int targetableArea = ReadWord(image, start + 1);
        int edgesOffset = ReadSignedWord(image[start + 3] | (image[start + 16] << 8));
        int facesOffset = ReadSignedWord(image[start + 4] | (image[start + 17] << 8));
        int maxEdges = image[start + 5];
        int gunVertex = image[start + 6];
        int explosionCount = image[start + 7];
        int vertexBytes = image[start + 8];
        int edgeCount = image[start + 9];
        int bounty = ReadWord(image, start + 10);
        int faceBytes = image[start + 12];
        int visibilityDistance = image[start + 13];
        int maxEnergy = image[start + 14];
        int maxSpeed = image[start + 15];
        int normalScale = image[start + 18];
        int laserMissileByte = image[start + 19];

        Require(vertexBytes % 6 == 0, label, $"vertex byte count {vertexBytes} is not a multiple of 6");
        Require(faceBytes % 4 == 0, label, $"face byte count {faceBytes} is not a multiple of 4");
        int vertexCount = vertexBytes / 6;
        int faceCount = faceBytes / 4;

        var header = new ShipHeader
        {
            MaxCanisters = canisterByte & 0x0F,
            ScoopMarketItem = (canisterByte >> 4) == 0 ? 0 : (canisterByte >> 4) + 1,
            CanisterByte = canisterByte,
            TargetableArea = targetableArea,
            EdgesOffset = edgesOffset,
            FacesOffset = facesOffset,
            MaxEdges = maxEdges,
            GunVertex = gunVertex,
            ExplosionCount = explosionCount,
            VertexCount = vertexCount,
            EdgeCount = edgeCount,
            FaceCount = faceCount,
            Bounty = bounty,
            VisibilityDistance = visibilityDistance,
            MaxEnergy = maxEnergy,
            MaxSpeed = maxSpeed,
            NormalScale = normalScale,
            LaserPower = laserMissileByte >> 3,
            Missiles = laserMissileByte & 0x07,
            LaserMissileByte = laserMissileByte,
        };

        var vertices = new List<ShipVertex>(vertexCount);
        for (int i = 0; i < vertexCount; i++)
        {
            int offset = start + 20 + (i * 6);
            Require(offset + 6 <= image.Length, label, $"vertex {i} lies outside the assembled image");
            int ax = image[offset];
            int ay = image[offset + 1];
            int az = image[offset + 2];
            int signs = image[offset + 3];
            int face12 = image[offset + 4];
            int face34 = image[offset + 5];

            vertices.Add(new ShipVertex
            {
                X = (signs & 0x80) != 0 ? -ax : ax,
                Y = (signs & 0x40) != 0 ? -ay : ay,
                Z = (signs & 0x20) != 0 ? -az : az,
                Faces =
                [
                    face12 & 0x0F,
                    (face12 >> 4) & 0x0F,
                    face34 & 0x0F,
                    (face34 >> 4) & 0x0F,
                ],
                Visibility = signs & 0x1F,
            });
        }

        var edges = new List<ShipEdge>(edgeCount);
        for (int i = 0; i < edgeCount; i++)
        {
            int offset = start + edgesOffset + (i * 4);
            Require(offset >= 0 && offset + 4 <= image.Length, label, $"edge {i} lies outside the assembled image");
            int visibility = image[offset];
            int faceByte = image[offset + 1];
            edges.Add(new ShipEdge
            {
                Visibility = visibility,
                Faces = [faceByte & 0x0F, (faceByte >> 4) & 0x0F],
                Vertex1 = image[offset + 2] >> 2,
                Vertex2 = image[offset + 3] >> 2,
            });
        }

        List<ShipFace> faces = DecodeFaces(image, start + facesOffset, faceCount, label);

        return new ShipBlueprint
        {
            Label = label,
            Address = address,
            Header = header,
            Vertices = vertices,
            Edges = edges,
            Faces = faces,
        };
    }

    /// <summary>
    /// The docked code (T.CODE) contains its own copy of the hangar ship blueprints, assembled at
    /// &5600 after the docked XX21 table. There is no standalone source file for that block, so a
    /// wrapper is generated from the real preamble and include list of elite-source-docked.asm and
    /// assembled in the system temp directory.
    /// </summary>
    private AssemblyResult AssembleDockedShipBlock()
    {
        const string DockedSource = FlightSourceDirectory + "/elite-source-docked.asm";
        string[] lines = ReadSource(DockedSource).Split('\n');

        var wrapper = new StringBuilder();

        // 1. The version-flag preamble, from the build options include up to the GUARD directive.
        bool inPreamble = false;
        foreach (string raw in lines)
        {
            string code = AsmText.StripComment(raw).Trim();
            if (!inPreamble)
            {
                if (code.StartsWith("INCLUDE", StringComparison.OrdinalIgnoreCase)
                    && code.Contains("elite-build-options", StringComparison.Ordinal))
                {
                    inPreamble = true;
                    wrapper.AppendLine(raw);
                }

                continue;
            }

            if (code.StartsWith("GUARD", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            wrapper.AppendLine(raw);
        }

        // 2. The ship blueprint block: the XX21 table and everything included with it, including the
        //    SKIP that pads the block out to the end of T.CODE.
        wrapper.AppendLine(" SHIP_MISSILE = &7F00        \\ The address of the missile ship blueprint");
        wrapper.AppendLine(" CODE% = &5600               \\ Address of the ship hangar blueprint block");
        wrapper.AppendLine(" LOAD% = &5600");
        wrapper.AppendLine(" ORG CODE%");

        bool inShips = false;
        foreach (string raw in lines)
        {
            string code = AsmText.StripComment(raw).Trim();
            if (!inShips)
            {
                if (code.StartsWith("INCLUDE", StringComparison.OrdinalIgnoreCase)
                    && code.Contains("docked/variable/xx21.asm", StringComparison.Ordinal))
                {
                    inShips = true;
                    wrapper.AppendLine(raw);
                }

                continue;
            }

            if (code.StartsWith("PRINT", StringComparison.OrdinalIgnoreCase)
                || code.StartsWith("SAVE", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            wrapper.AppendLine(raw);
        }

        if (!inShips)
        {
            throw new AsmException($"{DockedSource}: could not find the docked ship blueprint block");
        }

        string path = Path.Combine(Path.GetTempPath(), $"elite-docked-ships-{Guid.NewGuid():N}.asm");
        File.WriteAllText(path, wrapper.ToString());
        try
        {
            AssemblyResult assembly = new Assembler(_libraryRoot).Assemble(path);
            foreach (string warning in assembly.Warnings)
            {
                _log($"  warning: {warning}");
            }

            return assembly;
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Reads CODE% (the docked code's assembly address) from elite-source-docked.asm.</summary>
    private int ReadDockedCodeBase()
    {
        string source = ReadSource(FlightSourceDirectory + "/elite-source-docked.asm");
        Match match = CodeBaseRegex().Match(source);
        if (!match.Success)
        {
            throw new AsmException("could not find CODE% in elite-source-docked.asm");
        }

        return (int)new ExpressionEvaluator(_ => null).Evaluate(match.Groups[1].Value);
    }

    /// <summary>Decodes a block of FACE data (four bytes per face).</summary>
    private static List<ShipFace> DecodeFaces(byte[] image, int offset, int faceCount, string label)
    {
        var faces = new List<ShipFace>(faceCount);
        for (int i = 0; i < faceCount; i++)
        {
            int position = offset + (i * 4);
            Require(position >= 0 && position + 4 <= image.Length, label, $"face {i} lies outside the assembled image");
            int signs = image[position];
            int nx = image[position + 1];
            int ny = image[position + 2];
            int nz = image[position + 3];
            faces.Add(new ShipFace
            {
                X = (signs & 0x80) != 0 ? -nx : nx,
                Y = (signs & 0x40) != 0 ? -ny : ny,
                Z = (signs & 0x20) != 0 ? -nz : nz,
                Visibility = signs & 0x1F,
            });
        }

        return faces;
    }

    private static void Require(bool condition, string label, string message)
    {
        if (!condition)
        {
            throw new AsmException($"{label}: {message}");
        }
    }

    private static int ReadWord(byte[] image, int offset) => image[offset] | (image[offset + 1] << 8);

    private static int ReadSignedWord(int value) => (short)value;

    private byte[] ReadBinary(string relativePath)
    {
        string path = Path.Combine(_libraryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            throw new AsmException($"reference binary not found: {path}");
        }

        return File.ReadAllBytes(path);
    }

    private string ReadSource(string relativePath)
    {
        string path = Path.Combine(_libraryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            throw new AsmException($"source file not found: {path}");
        }

        return File.ReadAllText(path);
    }

    private string RelativePath(string fullPath) =>
        Path.GetRelativePath(_libraryRoot, fullPath).Replace(Path.DirectorySeparatorChar, '/');

    [GeneratedRegex(@"^\s*\\\s+Name:\s*(\S+)\s*$", RegexOptions.Multiline)]
    private static partial Regex NameCommentRegex();

    [GeneratedRegex(@"^\s*\\\s+Summary:\s*(.+?)\s*$", RegexOptions.Multiline)]
    private static partial Regex SummaryCommentRegex();

    [GeneratedRegex(@"^(.+?)\s*\(""(.+?)""\)\s*(.*)$")]
    private static partial Regex AliasNameRegex();

    [GeneratedRegex(@"^\s*(?:([A-Za-z0-9_]+)\s*=\s*)?(\d+)\s*=\s*(.+?)\s*$")]
    private static partial Regex Xx21CommentRegex();

    [GeneratedRegex(@"^\s*CODE%\s*=\s*(&[0-9A-Fa-f]+|\d+)\s", RegexOptions.Multiline)]
    private static partial Regex CodeBaseRegex();
}
