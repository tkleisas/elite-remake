using System.Buffers.Binary;

namespace EliteDataExtractor.Extraction;

/// <summary>
/// Reads the <c>E%</c> table — the default NEWB flags for each ship type — out of the disc's ship
/// blueprint files.
/// </summary>
/// <remarks>
/// <para>
/// The disc keeps a ship's default NEWB flags in the blueprint file it is loaded from, immediately
/// after the XX21 lookup table, and the loader's own definitions say so:
/// </para>
/// <code>
/// XX21 = &amp;5600    \ The address of the ship blueprints lookup table, where the chosen ship blueprints file is loaded
/// E%   = &amp;563E    \ The address of the default NEWB ship bytes within the loaded ship blueprints file
/// </code>
/// <para>
/// So <c>E%</c> is at offset <c>&amp;563E - &amp;5600 = 62</c> in the file, which is exactly where the
/// 31 two-byte XX21 entries end, and <c>NWSHP</c> reads it as <c>LDA E%-1,Y</c> with Y the ship type —
/// that is, offset <c>62 + type - 1</c>. There are sixteen blueprint files and each holds a different
/// handful of ships, so the flags for a type are taken from whichever files actually define it; every
/// file that defines a type agrees on its flags, which is a fact this reader checks rather than
/// assumes.
/// </para>
/// <para>
/// <b>This used to read somewhere else entirely.</b> The first version of this reader hunted for a
/// distinctive byte pattern in the docked code, found one, and read from there — but the pattern had
/// been taken from the bytes it found, so it could never have failed, and the table it produced had
/// zeroes for every pirate in the game. Nothing hostile could spawn, so the sky was almost entirely
/// peaceful. The table is now read from the file the game reads it from, and the number of files
/// consulted, and any disagreement between them, is reported.
/// </para>
/// </remarks>
internal static class NewbFlagExtractor
{
    /// <summary>The address the ship blueprints file is loaded at, from the loader's own source.</summary>
    private const int ShipFileBase = 0x5600;

    /// <summary>Where <c>E%</c> sits in the file: the base subtracted from <c>E%</c>'s address.</summary>
    private const int TableOffset = 0x563E - ShipFileBase;

    /// <summary>How many ship types the disc registers; XX21 holds one two-byte entry each.</summary>
    public const int EntryCount = 34;

    /// <summary>The highest type the disc's XX21 table covers.</summary>
    private const int LastType = 31;

    /// <summary>The bit that marks a ship as a cop, which the flight loop tests before it decides
    /// how much a kill raises our legal status.</summary>
    public const byte CopBit = 0x40;

    /// <summary>
    /// One ship type's flags, and how many blueprint files agreed on them.
    /// </summary>
    /// <param name="Type">The ship type number, from 1 to 31.</param>
    /// <param name="Flags">The default NEWB flags for that type.</param>
    /// <param name="Files">How many blueprint files define this type and carry these flags.</param>
    public readonly record struct Entry(int Type, byte Flags, int Files);

    /// <summary>
    /// Reads the flags for every ship type out of the disc's blueprint files.
    /// </summary>
    /// <param name="shipFileDirectory">The directory holding the assembled <c>D.MO?</c> files.</param>
    /// <param name="notes">Collects anything worth telling the caller about.</param>
    /// <returns>
    /// One entry per type from 0 to 33, or null if no blueprint file could be read. Types the disc
    /// does not define come back with zero flags and no files.
    /// </returns>
    public static Entry[]? Read(string shipFileDirectory, List<string> notes)
    {
        if (!Directory.Exists(shipFileDirectory))
        {
            notes.Add($"The ship blueprint files are not at {shipFileDirectory}, so the default NEWB "
                + "flags are absent. Ship type numbers are unaffected.");
            return null;
        }

        string[] files = Directory.GetFiles(shipFileDirectory, "D.MO?.bin");
        Array.Sort(files, StringComparer.Ordinal);
        if (files.Length == 0)
        {
            notes.Add($"No D.MO? blueprint files in {shipFileDirectory}, so the default NEWB flags are "
                + "absent. Ship type numbers are unaffected.");
            return null;
        }

        var flags = new byte[LastType + 1];
        var filesPerType = new int[LastType + 1];
        var conflicting = new List<string>();

        foreach (string path in files)
        {
            byte[] data = File.ReadAllBytes(path);
            string name = Path.GetFileName(path);

            for (int type = 1; type <= LastType; type++)
            {
                if ((type * 2) > data.Length)
                {
                    break;
                }

                int address = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan((type - 1) * 2, 2));

                // An XX21 entry that points nowhere is how a file says "this ship is not one of mine"
                int offset = address - ShipFileBase;
                if (address == 0 || offset < 0 || offset > data.Length - 4)
                {
                    continue;
                }

                int position = TableOffset + type - 1;
                if (position >= data.Length)
                {
                    continue;
                }

                byte value = data[position];
                if (filesPerType[type] == 0)
                {
                    flags[type] = value;
                }
                else if (flags[type] != value)
                {
                    conflicting.Add($"type {type}: {flags[type]:X2} against {value:X2} in {name}");
                }

                filesPerType[type]++;
            }
        }

        if (conflicting.Count > 0)
        {
            notes.Add("The blueprint files disagree about the default NEWB flags for "
                + string.Join("; ", conflicting)
                + ". The first file read wins; the table needs looking at.");
        }

        int defined = 0;
        for (int type = 1; type <= LastType; type++)
        {
            if (filesPerType[type] > 0)
            {
                defined++;
            }
        }

        notes.Add($"Default NEWB flags read from {files.Length} blueprint files at offset {TableOffset}, "
            + $"covering {defined} of {LastType} ship types");

        var entries = new Entry[EntryCount];
        for (int type = 0; type < EntryCount; type++)
        {
            entries[type] = type <= LastType
                ? new Entry(type, flags[type], filesPerType[type])
                : new Entry(type, 0, 0);
        }

        return entries;
    }
}
