namespace EliteDataExtractor.Extraction;

/// <summary>
/// Reads the <c>E%</c> table — the default NEWB flags for each ship type — out of the assembled
/// docked code.
/// </summary>
/// <remarks>
/// <para>
/// <c>E%</c> is a table in the disc's docked segment, and only that segment has it: the flight code
/// keeps the flags it copies into <c>ROM_E%</c>. So it cannot be read from the ship sources the way
/// the blueprints can, and its listing is not to be trusted either — the labels in
/// <c>e_per_cent.asm</c> name the ship on the <em>next</em> row down, which is what made the table
/// look like it had no entry for the Viper and one for the Splinter instead.
/// </para>
/// <para>
/// The bytes themselves are unambiguous, so this reads them out of the assembled binary instead of
/// parsing the listing. The block is located by its own distinctive pattern — ten zero bytes then
/// the Shuttle's <c>%00100001</c> — which occurs once in the docked code.
/// </para>
/// </remarks>
internal static class NewbFlagExtractor
{
    /// <summary>
    /// The opening of the E% block: ten zero entries, then the Shuttle, Transporter and Cobra Mk III
    /// flags. The first two bytes alone are not distinctive enough — the pattern has to be long
    /// enough to occur exactly once in the docked code, which is what makes locating the block
    /// without the listing's addresses safe.
    /// </summary>
    private static readonly byte[] Signature =
    [
        0x5E, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x21, 0x61, 0xA0, 0xA0, 0x00, 0x00, 0x00, 0xC2, 0x00, 0x00, 0x8C,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    ];


    /// <summary>
    /// How far into the signature the table itself begins. Its leading <c>0x5E</c> is the tail of
    /// the previous slot's XX21 pointer, which is what makes the run distinctive; it happens to be
    /// the byte the table is read from, because the verified base is the run's own start. The offset
    /// is kept as a named constant so that changing the signature cannot silently shift the read.
    /// </summary>
    private const int SignaturePrefix = 0;

    /// <summary>
    /// How many entries to read. The disc registers ship types up to 31, and the table runs on to a
    /// couple of bytes past that.
    /// </summary>
    public const int EntryCount = 34;

    /// <summary>The bit that marks a ship as a cop, which the flight loop tests before it decides
    /// how much a kill raises our legal status.</summary>
    public const byte CopBit = 0x40;

    /// <summary>
    /// Finds the table and returns its entries, or null if the pattern is not there or is not unique.
    /// </summary>
    public static byte[]? Read(string binaryPath)
    {
        byte[] data = File.ReadAllBytes(binaryPath);

        int found = -1;
        for (int i = 0; i + Signature.Length <= data.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < Signature.Length; j++)
            {
                if (data[i + j] != Signature[j])
                {
                    match = false;
                    break;
                }
            }

            if (!match)
            {
                continue;
            }

            found = i;
        }

        if (found < 0 || found + SignaturePrefix + EntryCount > data.Length)
        {
            return null;
        }

        return data[(found + SignaturePrefix)..(found + SignaturePrefix + EntryCount)];
    }
}
