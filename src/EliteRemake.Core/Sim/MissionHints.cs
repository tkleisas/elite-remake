using EliteRemake.Core.Universe;

namespace EliteRemake.Core.Sim;

/// <summary>One of the original's description overrides: a system, and when its hint is shown.</summary>
/// <param name="System">The system's number within its galaxy.</param>
/// <param name="Criteria">The galaxy number, or a value with bit 7 set for a hint always shown.</param>
public readonly record struct MissionHint(int System, int Criteria)
{
    /// <summary>Bit 7 marks a hint that is always shown, whatever the mission status.</summary>
    public const int AlwaysBit = 0x80;

    /// <summary>True if this hint does not depend on a mission being in progress.</summary>
    public bool Always => (Criteria & AlwaysBit) != 0;

    /// <summary>The galaxy the hint belongs to.</summary>
    public int Galaxy => Criteria & 0x7F;
}

/// <summary>
/// The original's mission hints: the descriptions that replace a system's usual one while the
/// Constrictor mission is in progress.
/// </summary>
/// <remarks>
/// The original keeps three tables for this. RUPLA names the system by its number within the galaxy,
/// RUGAL says which galaxy the hint belongs to — with bit 7 set for the one hint that is always
/// shown — and RUTOK holds the text. Its PDESC routine walks the tables when you dock, and if the
/// system you are docked at has an entry, the hint is printed instead of the generated description.
///
/// The hints are what turn the Constrictor mission into a trail: each system tells you where the
/// stolen ship was seen next, until the last one points you at Orarra.
/// </remarks>
public sealed class MissionHints
{
    private readonly IReadOnlyList<MissionHint> _hints;

    public MissionHints(IReadOnlyList<MissionHint> hints) => _hints = hints;

    /// <summary>How many hints the original's tables hold.</summary>
    public int Count => _hints.Count;

    /// <summary>
    /// The hint to show for a system, or 0 when the system has none.
    /// </summary>
    /// <remarks>
    /// The criteria are the original's, and the order of the tests matters. PDESC first matches the
    /// system number in RUPLA, then requires bits 0-6 of RUGAL to equal the current galaxy, and only
    /// then looks at bit 7:
    ///
    /// <code>
    /// LDA RUGAL-1,Y / AND #%01111111   \ bits 0-6 are the galaxy
    /// CMP GCNT / BNE PD2               \ and it must be the galaxy we are in
    /// LDA RUGAL-1,Y / BMI PD3          \ bit 7 set means print it without more ado
    /// ...                              \ otherwise mission 1 must be in progress
    /// </code>
    ///
    /// So bit 7 means "no mission needed yet", not "any galaxy". Reading it as the latter — which
    /// this used to do — makes the Teorge hint appear in all eight galaxies rather than the first,
    /// and the Arredi and Anreer hints in all eight rather than the third.
    /// </remarks>
    /// <param name="systemIndex">The system's number within its galaxy.</param>
    /// <param name="galaxyNumber">The galaxy we are in.</param>
    /// <param name="mission1Active">Whether mission 1 is in progress.</param>
    /// <returns>The hint's token number, or 0.</returns>
    public int TokenFor(int systemIndex, int galaxyNumber, bool mission1Active)
    {
        for (int i = 0; i < _hints.Count; i++)
        {
            MissionHint hint = _hints[i];
            if (hint.System != systemIndex || hint.Galaxy != galaxyNumber)
            {
                continue;
            }

            // The token number is the entry's position in the table
            if (hint.Always || mission1Active)
            {
                return i + 1;
            }
        }

        return 0;
    }

    /// <summary>
    /// The hint for a system, if it has one and we are docked there — the original only shows a hint
    /// for the system we are actually in, not the one the charts are pointing at.
    /// </summary>
    public int TokenFor(StarSystem system, StarSystem current, int galaxyNumber, bool mission1Active) =>
        system.Seeds == current.Seeds ? TokenFor(system.Index, galaxyNumber, mission1Active) : 0;
}
