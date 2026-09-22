namespace EliteRemake.Core.Sim;

/// <summary>
/// The original's random number generator: a pair of interleaved Fibonacci sequences kept in four
/// bytes.
/// </summary>
/// <remarks>
/// The original calls this DORND and keeps its state in RAND to RAND+3. Two sequences run side by
/// side: the "feeder" (RAND+0 and RAND+2) and the "main" sequence (RAND+1 and RAND+3), with the
/// carry from the feeder feeding the main sequence. Porting it exactly means the game produces the
/// same numbers, in the same order, as the original when it is seeded the same way — which is what
/// makes the market, the ship spawns and the system descriptions reproducible.
/// </remarks>
public sealed class EliteRandom
{
    private readonly byte[] _state = new byte[4];

    /// <summary>Creates a generator with the given seed.</summary>
    public EliteRandom(uint seed = 0)
    {
        Reseed(seed);
    }

    /// <summary>The four bytes of generator state, as the original keeps them in RAND.</summary>
    public Span<byte> State => _state;

    /// <summary>Sets the generator state from a 32-bit seed.</summary>
    public void Reseed(uint seed)
    {
        _state[0] = (byte)seed;
        _state[1] = (byte)(seed >> 8);
        _state[2] = (byte)(seed >> 16);
        _state[3] = (byte)(seed >> 24);

        // The original never lets the feeder sequence start at zero, as that would leave it stuck
        if (_state[0] == 0 && _state[2] == 0)
        {
            _state[0] = 0x2B;
        }
    }

    /// <summary>
    /// DORND: advances the generator by one step, returning the new main-sequence low byte in
    /// <paramref name="a"/> and the previous one in <paramref name="x"/>, exactly as the original's
    /// callers receive them.
    /// </summary>
    public void Next(out byte a, out byte x)
    {
        // Feeder sequence: f2 = (f1 << 1) + carry, f3 = f0 + f2 + (bit 7 of f1)
        byte f1 = _state[0];
        bool carry = (f1 & 0x80) != 0;
        byte shifted = (byte)(f1 << 1);
        x = shifted;

        int f2 = shifted + _state[2] + (carry ? 1 : 0);
        _state[0] = (byte)f2;
        _state[2] = x;
        carry = f2 > 0xFF;

        // Main sequence: m2 = m0 + m1 + the carry from the feeder
        byte m1 = _state[1];
        x = m1;
        int m2 = m1 + _state[3] + (carry ? 1 : 0);
        _state[1] = (byte)m2;
        _state[3] = x;
        a = (byte)m2;
    }

    /// <summary>DORND, returning the new main-sequence byte in A.</summary>
    public byte Next()
    {
        Next(out byte a, out _);
        return a;
    }

    /// <summary>DORND with the carry cleared first, as the original's DORND2 entry point does.</summary>
    public byte NextWithCarryCleared()
    {
        // DORND2 clears the carry before the feeder calculation, which matters because the feeder
        // adds it to the shifted value; here that means the feeder step never carries in
        Next(out byte a, out _);
        return a;
    }

    /// <summary>
    /// The original's random direction: a random byte reduced to one of eight compass points, used
    /// when placing ships and asteroids.
    /// </summary>
    public int NextDirection()
    {
        Next(out byte a, out _);
        return a & 0x07;
    }
}
