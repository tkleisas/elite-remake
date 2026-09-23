using EliteRemake.Core.Audio;
using Xunit;

namespace EliteRemake.Core.Tests;

/// <summary>
/// The sound table is the disc's own SFX data, byte for byte.
/// </summary>
/// <remarks>
/// The ten entries and their order come from the source's <c>SFX</c> table, and the labels are the
/// source's own — "16 - We died 1 / We made a hit or kill 2" and so on. The values are the thing that
/// would drift silently: a sound with the wrong pitch still plays, and it is only wrong beside the
/// original. The E.C.M.'s switch-off entry is all zeros on purpose, which is how the original stops the
/// field's continuous tone, and is exactly the kind of entry a well-meaning correction would "fix".
/// </remarks>
public class SoundTableTests
{
    [Fact]
    public void EverySoundMatchesTheDiscsData()
    {
        (SoundEffect Effect, int Offset, byte Channel, byte Amplitude, byte Pitch, byte Duration)[] expected =
        [
            (SoundEffect.LaserFire, 0, 0x12, 0x01, 0x00, 0x10),
            (SoundEffect.LaserHit, 8, 0x12, 0x02, 0x2C, 0x08),
            (SoundEffect.Explosion, 16, 0x11, 0x03, 0xF0, 0x18),
            (SoundEffect.HitOrDeath, 24, 0x10, 0xF1, 0x07, 0x1A),
            (SoundEffect.Beep, 32, 0x03, 0xF1, 0xBC, 0x01),
            (SoundEffect.Boop, 40, 0x13, 0xF4, 0x0C, 0x08),
            (SoundEffect.Missile, 48, 0x10, 0xF1, 0x06, 0x0C),
            (SoundEffect.Hyperspace, 56, 0x10, 0x02, 0x60, 0x10),
            (SoundEffect.EcmOn, 64, 0x13, 0x04, 0xC2, 0xFF),
            (SoundEffect.EcmOff, 72, 0x13, 0x00, 0x00, 0x00),
        ];

        Assert.Equal(expected.Length, Beeps.Table.Count);

        foreach ((SoundEffect effect, int offset, byte channel, byte amplitude, byte pitch, byte duration) in expected)
        {
            // The effect's number is its offset in the disc's table, which is how the original
            // addresses it
            Assert.Equal(offset, (int)effect);

            Beeps.SoundData data = Beeps.Table[effect];
            Assert.Equal(channel, data.ChannelAndFlush);
            Assert.Equal(amplitude, data.Amplitude);
            Assert.Equal(pitch, data.Pitch);
            Assert.Equal(duration, data.Duration);
        }
    }

    /// <summary>
    /// The E.C.M.'s switch-off entry is all zeros, and that is not a mistake.
    /// </summary>
    /// <remarks>
    /// This looks like an unfinished entry and is the obvious thing to "correct". It is what the
    /// original's SFX table holds for entry 72, and ECMOF calls it on this build — zeroing the sound
    /// buffer is how the original stops the E.C.M.'s continuous tone.
    /// </remarks>
    [Fact]
    public void TheEcmOffEntryIsDeliberatelySilent()
    {
        Beeps.SoundData off = Beeps.Table[SoundEffect.EcmOff];

        Assert.Equal(0, off.Amplitude);
        Assert.Equal(0, off.Pitch);
        Assert.Equal(0, off.Duration);
    }
}
