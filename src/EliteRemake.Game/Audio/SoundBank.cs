using EliteRemake.Core.Audio;
using Microsoft.Xna.Framework.Audio;

namespace EliteRemake.Game.Audio;

/// <summary>
/// Plays the game's sounds, one at a time as the original's single sound chip does.
/// </summary>
/// <remarks>
/// The BBC Micro has one sound chip, so the original can only ever make one noise: a new sound
/// flushes whatever is playing. This class keeps that behaviour by cutting off the previous sound
/// when a new one starts, and renders each sound to a PCM buffer once at startup so playing one
/// costs nothing.
/// </remarks>
public sealed class SoundBank : IDisposable
{
    private readonly Dictionary<Core.Audio.SoundEffect, Microsoft.Xna.Framework.Audio.SoundEffect> _sounds = [];
    private Microsoft.Xna.Framework.Audio.SoundEffectInstance? _playing;

    /// <summary>True to silence all sound.</summary>
    public bool Muted { get; set; }

    /// <summary>The overall volume, from 0 to 1.</summary>
    public float Volume { get; set; } = 0.35f;

    /// <summary>How many sounds have been prepared, for diagnostics.</summary>
    public int SoundCount => _sounds.Count;

    /// <summary>Renders every sound up front.</summary>
    public void Load()
    {
        foreach (Core.Audio.SoundEffect effect in Enum.GetValues<Core.Audio.SoundEffect>())
        {
            float[] samples = Beeps.Render(effect);
            if (samples.Length == 0)
            {
                continue;
            }

            var buffer = new byte[samples.Length * 2];
            for (int i = 0; i < samples.Length; i++)
            {
                short value = (short)(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue);
                buffer[i * 2] = (byte)(value & 0xFF);
                buffer[(i * 2) + 1] = (byte)((value >> 8) & 0xFF);
            }

            try
            {
                _sounds[effect] = new Microsoft.Xna.Framework.Audio.SoundEffect(
                    buffer,
                    Beeps.SampleRate,
                    AudioChannels.Mono);
            }
            catch (Exception)
            {
                // No audio device available: run silently rather than failing to start
                Muted = true;
                return;
            }
        }
    }

    /// <summary>
    /// Plays a sound, cutting off whatever was playing, as the original's sounds do when their
    /// flush control is set.
    /// </summary>
    /// <summary>
    /// Plays a sound, stopping whatever was playing.
    /// </summary>
    /// <remarks>
    /// A sound with no samples is treated as "stop the current sound" rather than as nothing at all.
    /// The original's SFX entry 72 is exactly that — all four bytes zero, which silences the channel —
    /// and it is how the E.C.M.'s continuous tone is cut off when the field collapses. Our E.C.M. tone
    /// is rendered from entry 64 with a duration of 255 ticks, which is nearly thirteen seconds where
    /// the field lasts 60 frames, so without this the tone would outlive the E.C.M. by a wide margin.
    /// </remarks>
    public void Play(Core.Audio.SoundEffect effect)
    {
        if (Muted || Volume <= 0)
        {
            return;
        }

        if (!_sounds.TryGetValue(effect, out var sound))
        {
            // No samples: this is a silencing entry, so stop what is playing
            Stop();
            return;
        }

        if (_playing is { State: SoundState.Playing })
        {
            _playing.Stop();
        }

        var instance = sound.CreateInstance();
        instance.Volume = Volume;
        instance.Play();
        _playing = instance;
    }

    /// <summary>Stops the sound now playing, if any.</summary>
    public void Stop()
    {
        if (_playing is { State: SoundState.Playing })
        {
            _playing.Stop();
        }
    }

    public void Dispose()
    {
        _playing?.Dispose();
        _playing = null;

        foreach (var sound in _sounds.Values)
        {
            sound.Dispose();
        }

        _sounds.Clear();
    }
}
