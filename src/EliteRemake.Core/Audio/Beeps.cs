namespace EliteRemake.Core.Audio;

/// <summary>The sounds the game makes, using the original's numbering.</summary>
public enum SoundEffect
{
    /// <summary>Lasers fired by us.</summary>
    LaserFire = 0,

    /// <summary>We are being hit by lasers.</summary>
    LaserHit = 8,

    /// <summary>We died, or (first part) we made a kill.</summary>
    Explosion = 16,

    /// <summary>We died, or (second part) we made a hit.</summary>
    HitOrDeath = 24,

    /// <summary>A short, high beep, used for menu clicks and missile lock.</summary>
    Beep = 32,

    /// <summary>A long, low beep, used for missile unarming and errors.</summary>
    Boop = 40,

    /// <summary>A missile has been launched.</summary>
    Missile = 48,

    /// <summary>The hyperspace drive has engaged.</summary>
    Hyperspace = 56,

    /// <summary>The E.C.M. has been switched on.</summary>
    EcmOn = 64,

    /// <summary>The E.C.M. has been switched off.</summary>
    EcmOff = 72,
}

/// <summary>
/// The original's sound effects, and a synthesizer that turns them into samples.
/// </summary>
/// <remarks>
/// The original makes every sound with the BBC Micro's single sound chip, using the four bytes it
/// keeps in its SFX table: channel and flush control, amplitude or envelope, pitch, and duration.
/// The duration is in twentieths of a second, as BBC BASIC's SOUND takes it, so the table's
/// durations are the real ones.
///
/// The pitch byte is the BBC's pitch parameter, which drives a divider of 125000 / pitch hertz. The
/// BBC's four sound envelopes, which the game's own loader sets up, are what give the sounds their
/// character, and this synthesizer reproduces them: envelope 1 is a decaying note, 2 sweeps
/// upwards, 3 is noise, and 4 is a fast tremolo. That is how the original's laser zap, explosion
/// rumble, hyperspace whoosh and E.C.M. buzz are built.
/// </remarks>
public static class Beeps
{
    /// <summary>The sample rate the synthesizer works at.</summary>
    public const int SampleRate = 22050;

    /// <summary>One of the original's sounds: the four bytes from its SFX table.</summary>
    /// <param name="ChannelAndFlush">Channel and flush control, as the original stores it.</param>
    /// <param name="Amplitude">Amplitude, or the number of an envelope when it is 1 to 4.</param>
    /// <param name="Pitch">The BBC's pitch parameter.</param>
    /// <param name="Duration">The duration in twentieths of a second.</param>
    public readonly record struct SoundData(byte ChannelAndFlush, byte Amplitude, byte Pitch, byte Duration)
    {
        /// <summary>The channel the sound plays on.</summary>
        public int Channel => ChannelAndFlush & 0x0F;

        /// <summary>True if the sound should interrupt whatever is playing.</summary>
        public bool Flush => (ChannelAndFlush & 0xF0) != 0;

        /// <summary>The envelope number, or 0 when a plain amplitude is used.</summary>
        public int Envelope => Amplitude is >= 1 and <= 4 ? Amplitude : 0;

        /// <summary>The volume, from 0 to 1, when no envelope is used.</summary>
        public float Volume => Envelope != 0 ? 1f : (Amplitude & 0x0F) / 15f;

        /// <summary>The duration in seconds: the original counts in twentieths.</summary>
        public float Seconds => Duration / 20f;
    }

    /// <summary>The original's sound table, in the order its SFX data has it.</summary>
    public static readonly IReadOnlyDictionary<SoundEffect, SoundData> Table =
        new Dictionary<SoundEffect, SoundData>
        {
            [SoundEffect.LaserFire] = new(0x12, 0x01, 0x00, 0x10),
            [SoundEffect.LaserHit] = new(0x12, 0x02, 0x2C, 0x08),
            [SoundEffect.Explosion] = new(0x11, 0x03, 0xF0, 0x18),
            [SoundEffect.HitOrDeath] = new(0x10, 0xF1, 0x07, 0x1A),
            [SoundEffect.Beep] = new(0x03, 0xF1, 0xBC, 0x01),
            [SoundEffect.Boop] = new(0x13, 0xF4, 0x0C, 0x08),
            [SoundEffect.Missile] = new(0x10, 0xF1, 0x06, 0x0C),
            [SoundEffect.Hyperspace] = new(0x10, 0x02, 0x60, 0x10),
            [SoundEffect.EcmOn] = new(0x13, 0x04, 0xC2, 0xFF),
            [SoundEffect.EcmOff] = new(0x13, 0x00, 0x00, 0x00),
        };

    /// <summary>
    /// The frequency the BBC's pitch parameter produces: the sound chip divides 125000 by the
    /// pitch, so a high pitch number is a low note. A pitch of zero means the chip's maximum
    /// divider, which is the lowest note it can make.
    /// </summary>
    public static double Frequency(int pitch) => pitch <= 0 ? 122.0 : 125000.0 / pitch;

    /// <summary>
    /// Renders a sound to mono samples, applying its envelope.
    /// </summary>
    /// <param name="sound">The sound to render.</param>
    /// <returns>Mono samples, ready to be played or written to a file.</returns>
    public static float[] Render(SoundData sound)
    {
        int count = (int)(sound.Seconds * SampleRate);
        if (count <= 0)
        {
            return []; // a sound with no duration, such as the E.C.M. switching off
        }

        var samples = new float[count];

        double baseFrequency = Frequency(sound.Pitch);
        double phase = 0;
        var random = new Random(sound.Pitch + (sound.Duration << 8));

        for (int i = 0; i < count; i++)
        {
            double progress = count <= 1 ? 0 : i / (double)(count - 1);
            double frequency = baseFrequency;
            double amplitude = sound.Volume;

            switch (sound.Envelope)
            {
                case 1:
                    // A note that dies away
                    amplitude *= 1 - progress;
                    break;

                case 2:
                    // A sweep upwards, which is the hyperspace drive
                    frequency *= 1 + (progress * 3);
                    break;

                case 3:
                    // Noise, which is the explosion
                    amplitude *= 1 - progress;
                    phase = random.NextDouble();
                    break;

                case 4:
                    // A fast tremolo, which is the E.C.M.
                    amplitude *= 0.5 + (0.5 * Math.Sin(progress * Math.Tau * 40));
                    break;
            }

            // The beeper is a square wave: full volume or nothing
            phase += frequency / SampleRate;
            phase -= Math.Floor(phase);
            samples[i] = (phase < 0.5 ? 1f : -1f) * (float)amplitude;
        }

        return samples;
    }

    /// <summary>Renders a sound by number, for callers that only have the effect.</summary>
    public static float[] Render(SoundEffect effect) =>
        Table.TryGetValue(effect, out SoundData sound) ? Render(sound) : [];
}
