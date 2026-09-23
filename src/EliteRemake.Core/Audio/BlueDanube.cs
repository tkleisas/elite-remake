namespace EliteRemake.Core.Audio;

/// <summary>
/// The Blue Danube, as the docking music the later versions of Elite played.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a deliberate departure, and the only music in the game.</b> The BBC Micro disc version
/// has no music at all: it has one sound chip, which the flight loop uses for effects, and the waltz
/// belongs to the Commodore 64 version, which had a SID to spare. The tune is here because it is the
/// sound people remember from Elite — and, in the end, from <i>2001: A Space Odyssey</i>, whose
/// docking sequence uses the same opening bars.
/// </para>
/// <para>
/// The notes are the first strain of Johann Strauss II's <i>An der schönen blauen Donau</i>, taken
/// from the tune's entry in James Kerr's <i>Merry Melodies</i> as transcribed by John Chambers for
/// the ABC notation collections, and reduced to a single line. A beeper has one voice, so the
/// accompaniment and the melody are played in the order the score has them, the way a music box or a
/// telephone ringtone renders a piece written for many instruments. The collection has the tune in G
/// rather than the original's D, and it is left there: transposing it up would put the melody beyond
/// a thousand hertz, which is shrill on a single square wave.
/// </para>
/// </remarks>
public static class BlueDanube
{
    /// <summary>Middle A, which the tuning is built from.</summary>
    private const int ConcertA = 69;

    /// <summary>The frequency of middle A, in hertz.</summary>
    private const double ConcertAFrequency = 440.0;

    /// <summary>
    /// How long a sixteenth note lasts. The source score is marked at a quarter note of half a
    /// second, which is a stately waltz and the tempo the docking music is usually played at.
    /// </summary>
    public const float SixteenthSeconds = 0.125f;

    /// <summary>
    /// The opening of the waltz, as a note number and a length in sixteenths. The note numbers are
    /// the usual MIDI ones, where 69 is middle A, and the sequence runs to the end of the first
    /// strain so that looping it does not clip a phrase in half.
    /// </summary>
    public static readonly (int Note, int Sixteenths)[] Melody =
    [
        (55, 4), (59, 4), (62, 4), (62, 8), (74, 4), (74, 8), (71, 4), (71, 8), (55, 4), (55, 4),
        (59, 4), (62, 4), (62, 8), (74, 4), (74, 8), (72, 4), (72, 8), (66, 4), (66, 4), (69, 4),
        (72, 4), (72, 8), (76, 4), (76, 8), (72, 4), (72, 8), (66, 4), (66, 4), (69, 4), (72, 4),
        (72, 8), (76, 4), (76, 8), (71, 4), (71, 8), (55, 4), (55, 4), (59, 4), (62, 4), (67, 8),
        (79, 4), (79, 8), (74, 4), (74, 8), (55, 4), (55, 4), (59, 4), (62, 4), (67, 8), (79, 4),
        (79, 8), (76, 4), (76, 8), (69, 4), (69, 4), (72, 4), (76, 4), (76, 16), (73, 4), (74, 4),
        (83, 16), (79, 4), (71, 4), (71, 8), (69, 4), (76, 8), (74, 4), (67, 6), (67, 2), (67, 4),
    ];

    /// <summary>How long the whole tune lasts, in seconds.</summary>
    public static float Duration => TotalSixteenths * SixteenthSeconds;

    /// <summary>The length of the tune in sixteenth notes.</summary>
    public static int TotalSixteenths
    {
        get
        {
            int total = 0;
            foreach ((_, int sixteenths) in Melody)
            {
                total += sixteenths;
            }

            return total;
        }
    }

    /// <summary>The frequency of a MIDI note number, in hertz.</summary>
    public static double Frequency(int note) =>
        ConcertAFrequency * Math.Pow(2, (note - ConcertA) / 12.0);

    /// <summary>
    /// Renders the waltz to mono samples, ready to be looped.
    /// </summary>
    /// <remarks>
    /// Each note is a square wave, as the original's sound chip makes, with a short attack and a
    /// decay over its length so that repeated notes are heard as separate notes rather than as one
    /// long tone. The tail of the buffer is left quiet so that the loop point falls in silence.
    /// </remarks>
    /// <param name="sampleRate">The sample rate to render at.</param>
    /// <param name="amplitude">The peak amplitude, from 0 to 1.</param>
    public static float[] Render(int sampleRate = Beeps.SampleRate, float amplitude = 0.5f)
    {
        int count = (int)(Duration * sampleRate);
        var samples = new float[count];
        int at = 0;

        for (int index = 0; index < Melody.Length; index++)
        {
            (int note, int sixteenths) = Melody[index];
            int length = (int)(sixteenths * SixteenthSeconds * sampleRate);
            double frequency = Frequency(note);

            for (int i = 0; i < length && at < count; i++, at++)
            {
                double progress = length <= 1 ? 0 : i / (double)(length - 1);

                // A quick attack so the note has an edge, then a decay to just short of silence, and
                // a gap at the end so the next note starts cleanly
                double envelope = progress < 0.02 ? progress / 0.02 : 1.0 - (progress * 0.75);
                if (progress > 0.92)
                {
                    envelope *= (1 - progress) / 0.08;
                }

                double phase = frequency * (i / (double)sampleRate);
                double value = (phase - Math.Floor(phase)) < 0.5 ? 1.0 : -1.0;
                samples[at] = (float)(value * envelope * amplitude);
            }
        }

        return samples;
    }
}
