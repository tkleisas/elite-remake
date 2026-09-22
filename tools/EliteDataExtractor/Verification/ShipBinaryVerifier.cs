using EliteDataExtractor.Extraction;

namespace EliteDataExtractor.Verification;

/// <summary>The outcome of verifying the extracted data against the original binaries.</summary>
internal sealed class VerificationReport
{
    public int FilesCompared { get; set; }

    public int SlotsCompared { get; set; }

    public int ShipsCompared { get; set; }

    public List<string> Mismatches { get; } = [];

    public List<string> Notes { get; } = [];

    public bool Passed => Mismatches.Count == 0;
}

/// <summary>
/// Independently decodes the reference binaries in versions/disc/3-assembled-output and compares them
/// with the asm-derived data. The decoder here reads the blueprint layout straight from the binary
/// (pointer table, 20-byte header, vertex/edge/face blocks) and does not share code with the
/// assembler-side decoder, so a mistake in the extraction cannot hide behind a matching mistake here.
/// </summary>
internal sealed class ShipBinaryVerifier
{
    private const int MissileAddress = 0x7F00;

    private readonly string _libraryRoot;
    private readonly Action<string> _log;

    public ShipBinaryVerifier(string libraryRoot, TextWriter log)
    {
        _libraryRoot = Path.GetFullPath(libraryRoot);
        _log = log.WriteLine;
    }

    public VerificationReport Verify(ExtractionResult extraction, string missileBinary)
    {
        var report = new VerificationReport();

        foreach (ShipSet set in extraction.ShipSets)
        {
            if (set.Id == "docked")
            {
                VerifyDockedTable(set, extraction, report);
                continue;
            }

            VerifyShipFile(set, extraction, missileBinary, report);
        }

        return report;
    }

    private void VerifyShipFile(
        ShipSet set,
        ExtractionResult extraction,
        string missileBinary,
        VerificationReport report)
    {
        byte[] binary = set.BinaryData ?? throw new InvalidOperationException($"{set.Id}: no binary data");
        Assembly.AssemblyResult assembly = set.Assembly
            ?? throw new InvalidOperationException($"{set.Id}: no assembled image");

        report.FilesCompared++;
        _log($"");
        _log($"{set.Binary} ({binary.Length} bytes, base &{set.BaseAddress:X4}, {set.SlotCount} XX21 slots)");

        // 1. The whole assembled image must match the reference binary byte for byte (the reference
        //    file is padded with zeros up to the length saved by the SAVE directive).
        int imageMismatches = 0;
        int padded = 0;
        for (int i = 0; i < binary.Length; i++)
        {
            byte expected = i < assembly.Image.Length ? assembly.Image[i] : (byte)0;
            if (i >= assembly.Image.Length)
            {
                padded++;
            }

            if (expected != binary[i])
            {
                if (imageMismatches == 0)
                {
                    report.Mismatches.Add(
                        $"{set.Id}: assembled image differs from {set.Binary} at offset &{i:X4} " +
                        $"(asm &{expected:X2}, binary &{binary[i]:X2})");
                    _log($"  image mismatch at offset &{i:X4}: asm &{expected:X2} != binary &{binary[i]:X2}");
                }

                imageMismatches++;
            }
        }

        if (imageMismatches == 0)
        {
            _log($"  assembled image: {assembly.Image.Length} bytes matched, {padded} zero-padding bytes matched");
        }
        else
        {
            report.Mismatches.Add($"{set.Id}: {imageMismatches} byte(s) differ from {set.Binary}");
        }

        // 2. The XX21 pointer table.
        int slotMismatches = 0;
        for (int i = 0; i < set.SlotCount; i++)
        {
            int offset = i * 2;
            if (offset + 1 >= binary.Length)
            {
                report.Mismatches.Add($"{set.Id}: XX21 slot {i + 1} lies outside {set.Binary}");
                slotMismatches++;
                continue;
            }

            int stored = binary[offset] | (binary[offset + 1] << 8);
            report.SlotsCompared++;
            if (stored != set.Slots[i].Pointer)
            {
                report.Mismatches.Add(
                    $"{set.Id}: XX21 slot {i + 1} is &{stored:X4} in the binary but &{set.Slots[i].Pointer:X4} in the asm");
                slotMismatches++;
            }
        }

        if (slotMismatches == 0)
        {
            _log($"  XX21 table: {set.SlotCount} slots matched");
        }

        // 3. Every populated slot: decode the blueprint from the binary and compare it with the
        //    blueprint decoded from the assembled source.
        foreach (ShipSetSlot slot in set.Slots)
        {
            if (slot.Label is null)
            {
                continue;
            }

            report.ShipsCompared++;
            string description = $"{set.Id} slot {slot.Type,2} {(slot.Symbol ?? "    "),-4} {slot.Label}";

            if (slot.Blueprint is null)
            {
                report.Mismatches.Add($"{description}: no asm-derived blueprint");
                continue;
            }

            long pointer = slot.Pointer;
            byte[] image = binary;
            int start;
            string source;
            if (pointer >= set.BaseAddress && pointer + 20 <= set.BaseAddress + binary.Length)
            {
                start = (int)(pointer - set.BaseAddress);
                source = set.Binary!;
            }
            else if (pointer == MissileAddress)
            {
                image = File.ReadAllBytes(missileBinary);
                start = 0;
                source = Path.GetFileName(missileBinary);
            }
            else
            {
                report.Mismatches.Add($"{description}: pointer &{pointer:X4} does not point into {set.Binary}");
                continue;
            }

            ShipBlueprint? decoded = DecodeBlueprint(image, start, slot.Label);
            if (decoded is null)
            {
                report.Mismatches.Add($"{description}: could not decode the blueprint at &{pointer:X4} in {source}");
                continue;
            }

            string? difference = BlueprintComparer.Diff(slot.Blueprint, decoded);
            if (difference is not null)
            {
                report.Mismatches.Add($"{description}: {difference}");
                _log($"  slot {slot.Type,2} {(slot.Symbol ?? "    "),-4} {slot.Label,-20} MISMATCH: {difference}");
                continue;
            }

            ShipHeader header = decoded.Header;
            string address = source == Path.GetFileName(missileBinary)
                ? $"&{pointer:X4} ({source})"
                : $"&{pointer:X4}";
            _log(
                $"  slot {slot.Type,2} {(slot.Symbol ?? "    "),-4} {slot.Label,-20} {address} " +
                $"ok: {header.VertexCount} vertices, {header.EdgeCount} edges, {header.FaceCount} faces, " +
                $"speed {header.MaxSpeed}, energy {header.MaxEnergy}");
        }

        _log($"  {set.Id}: {set.Slots.Count(slot => slot.Label is not null)} ships compared");
    }

    /// <summary>
    /// The docked XX21 table does not define blueprints, so only its shape can be checked: locate the
    /// table in T.CODE.unprot.bin by its empty/non-empty slot pattern and sanity-check the pointers.
    /// </summary>
    private void VerifyDockedTable(ShipSet set, ExtractionResult extraction, VerificationReport report)
    {
        byte[]? binary = set.BinaryData;
        if (binary is null)
        {
            report.Notes.Add("docked: T.CODE.unprot.bin not available, table not checked");
            return;
        }

        _log($"");
        _log($"{set.Binary} (docked XX21 table, {set.SlotCount} slots)");

        var matches = new List<int>();
        for (int offset = 0; offset + (set.SlotCount * 2) <= binary.Length; offset++)
        {
            bool match = true;
            for (int i = 0; i < set.SlotCount && match; i++)
            {
                int value = binary[offset + (i * 2)] | (binary[offset + (i * 2) + 1] << 8);
                bool expectedEmpty = set.Slots[i].Label is null;
                match = expectedEmpty ? value == 0 : value != 0;
            }

            if (match)
            {
                matches.Add(offset);
            }
        }

        if (matches.Count != 1)
        {
            report.Mismatches.Add(
                $"docked: found {matches.Count} candidate XX21 tables in {set.Binary}, expected exactly 1");
            _log($"  slot pattern: {matches.Count} matches (expected 1)");
            return;
        }

        int tableOffset = matches[0];
        int tableAddress = tableOffset + 0x11E3; // the docked code is assembled at &11E3
        _log($"  slot pattern matched at offset &{tableOffset:X4} (address &{tableAddress:X4})");

        // Sanity-check the pointers: the missile lives at &7F00 and the rest point into the ship
        // blueprint file that is loaded at &5600.
        foreach (ShipSetSlot slot in set.Slots)
        {
            if (slot.Label is null)
            {
                continue;
            }

            int value = binary[tableOffset + ((slot.Type - 1) * 2)]
                | (binary[tableOffset + ((slot.Type - 1) * 2) + 1] << 8);

            bool valid = value == MissileAddress
                || (value >= 0x5600 && value < 0x6000);
            if (!valid)
            {
                report.Mismatches.Add(
                    $"docked: slot {slot.Type} ({slot.Label}) points at &{value:X4}, which is not a ship blueprint address");
            }

            // Any flight ship file that has a blueprint for this label must place it at this address
            // for the library to be self-consistent.
            var addresses = extraction.ShipSets
                .Where(other => other.Id != "docked")
                .Select(other => other.Slots.FirstOrDefault(candidate =>
                    string.Equals(candidate.Label, slot.Label, StringComparison.OrdinalIgnoreCase)))
                .Where(candidate => candidate is not null)
                .Select(candidate => candidate!.Pointer)
                .Distinct()
                .ToList();

            if (addresses.Count > 0 && !addresses.Contains(value) && value != MissileAddress)
            {
                report.Notes.Add(
                    $"docked: slot {slot.Type} ({slot.Label}) is &{value:X4}, which does not match any flight ship file " +
                    $"(the docked hangar reads whichever ship file is currently loaded)");
            }
        }

        _log($"  {set.Slots.Count(slot => slot.Label is not null)} populated slots checked");
    }

    /// <summary>
    /// Decodes a blueprint straight from a binary image at <paramref name="start"/>, using only the
    /// layout documented for the original game.
    /// </summary>
    private static ShipBlueprint? DecodeBlueprint(byte[] image, int start, string label)
    {
        if (start < 0 || start + 20 > image.Length)
        {
            return null;
        }

        int canisterByte = image[start + 0];
        int targetableArea = image[start + 1] | (image[start + 2] << 8);
        int edgesOffset = (short)(image[start + 3] | (image[start + 16] << 8));
        int facesOffset = (short)(image[start + 4] | (image[start + 17] << 8));
        int maxEdges = image[start + 5];
        int gunVertex = image[start + 6];
        int explosionCount = image[start + 7];
        int vertexBytes = image[start + 8];
        int edgeCount = image[start + 9];
        int bounty = image[start + 10] | (image[start + 11] << 8);
        int faceBytes = image[start + 12];
        int visibilityDistance = image[start + 13];
        int maxEnergy = image[start + 14];
        int maxSpeed = image[start + 15];
        int normalScale = image[start + 18];
        int laserMissileByte = image[start + 19];

        if (vertexBytes % 6 != 0 || faceBytes % 4 != 0)
        {
            return null;
        }

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
            if (offset + 6 > image.Length)
            {
                return null;
            }

            int signs = image[offset + 3];
            int face12 = image[offset + 4];
            int face34 = image[offset + 5];
            vertices.Add(new ShipVertex
            {
                X = (signs & 0x80) != 0 ? -image[offset] : image[offset],
                Y = (signs & 0x40) != 0 ? -image[offset + 1] : image[offset + 1],
                Z = (signs & 0x20) != 0 ? -image[offset + 2] : image[offset + 2],
                Faces = [face12 & 0x0F, face12 >> 4, face34 & 0x0F, face34 >> 4],
                Visibility = signs & 0x1F,
            });
        }

        var edges = new List<ShipEdge>(edgeCount);
        for (int i = 0; i < edgeCount; i++)
        {
            int offset = start + edgesOffset + (i * 4);
            if (offset < 0 || offset + 4 > image.Length)
            {
                return null;
            }

            int faceByte = image[offset + 1];
            edges.Add(new ShipEdge
            {
                Visibility = image[offset],
                Faces = [faceByte & 0x0F, faceByte >> 4],
                Vertex1 = image[offset + 2] >> 2,
                Vertex2 = image[offset + 3] >> 2,
            });
        }

        var faces = new List<ShipFace>(faceCount);
        for (int i = 0; i < faceCount; i++)
        {
            int offset = start + facesOffset + (i * 4);
            if (offset < 0 || offset + 4 > image.Length)
            {
                return null;
            }

            int signs = image[offset];
            faces.Add(new ShipFace
            {
                X = (signs & 0x80) != 0 ? -image[offset + 1] : image[offset + 1],
                Y = (signs & 0x40) != 0 ? -image[offset + 2] : image[offset + 2],
                Z = (signs & 0x20) != 0 ? -image[offset + 3] : image[offset + 3],
                Visibility = signs & 0x1F,
            });
        }

        return new ShipBlueprint
        {
            Label = label,
            Address = start,
            Header = header,
            Vertices = vertices,
            Edges = edges,
            Faces = faces,
        };
    }
}
