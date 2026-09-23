using EliteRemake.Core.Audio;
using Xunit;

namespace EliteRemake.Core.Tests;

/// <summary>
/// Checks the Blue Danube: the one piece of music in the game, and a deliberate departure, since the
/// BBC disc has no music at all.
/// </summary>
/// <remarks>
/// The tests measure the synthesizer's output rather than trusting the note table: each note is
/// rendered, and its pitch is counted back out of the samples by zero crossings. A table with a typo
/// in it, or a synthesizer that plays the wrong octave, fails here.
/// </remarks>
public class BlueDanubeTests
{
    /// <summary>The frequency of a MIDI note number, written out independently of the class.</summary>
    private static double Expected(int note) => 440.0 * Math.Pow(2, (note - 69) / 12.0);

    [Fact]
    public void EveryNoteIsRenderedAtThePitchTheTableAsksFor()
    {
        float[] samples = BlueDanube.Render();
        Assert.NotEmpty(samples);

        int at = 0;
        for (int index = 0; index < BlueDanube.Melody.Length; index++)
        {
            (int note, int sixteenths) = BlueDanube.Melody[index];
            int length = (int)(sixteenths * BlueDanube.SixteenthSeconds * Beeps.SampleRate);
            Assert.True(length > 0, $"note {index} has no length");

            // Measure across the steady middle of the note, away from the attack and the decay
            int from = at + (length / 4);
            int to = at + (length * 4 / 5);

            int crossings = 0;
            for (int i = from + 1; i < to; i++)
            {
                if (samples[i - 1] < 0 != samples[i] < 0)
                {
                    crossings++;
                }
            }

            double seconds = (to - from) / (double)Beeps.SampleRate;
            double measured = crossings / (2 * seconds);
            double expected = Expected(note);

            Assert.True(
                Math.Abs(measured - expected) / expected < 0.03,
                $"note {index} (midi {note}) should be {expected:0.0} Hz but measured {measured:0.0} Hz");

            at += length;
        }
    }

    [Fact]
    public void TheTuneIsTheLengthItsNotesAddUpTo()
    {
        int sixteenths = 0;
        foreach ((_, int length) in BlueDanube.Melody)
        {
            sixteenths += length;
        }

        Assert.Equal(sixteenths, BlueDanube.TotalSixteenths);
        Assert.Equal(sixteenths * BlueDanube.SixteenthSeconds, BlueDanube.Duration, 3);

        float[] samples = BlueDanube.Render();
        Assert.Equal((int)(BlueDanube.Duration * Beeps.SampleRate), samples.Length);
    }

    [Fact]
    public void TheTuneOpensWithTheRisingTriadEveryoneKnows()
    {
        // The waltz begins with a rising arpeggio — G, B, D in the key the score is written in —
        // and the rest of the melody follows it. Pinned here because it is the part of the tune that
        // makes it recognisable, and because a transposed or truncated opening is the mistake worth
        // catching.
        Assert.True(BlueDanube.Melody.Length > 8);
        Assert.Equal([55, 59, 62], BlueDanube.Melody.Take(3).Select(n => n.Note));
        Assert.All(BlueDanube.Melody.Take(3), n => Assert.Equal(4, n.Sixteenths));
    }

    [Fact]
    public void TheRenderedTuneIsAudibleThroughout()
    {
        // No silent stretches of any size: every note in the table has to reach the samples, which
        // is what a mistake in the offset arithmetic would break
        float[] samples = BlueDanube.Render();
        int window = Beeps.SampleRate / 4;   // an eighth of a second

        for (int at = 0; at + window <= samples.Length; at += window)
        {
            float peak = 0;
            for (int i = at; i < at + window; i++)
            {
                peak = Math.Max(peak, Math.Abs(samples[i]));
            }

            Assert.True(peak > 0.05f, $"the music is silent at sample {at} ({at / (double)Beeps.SampleRate:0.00}s)");
        }
    }
}
