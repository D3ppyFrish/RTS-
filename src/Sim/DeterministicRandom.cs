namespace Rts.Sim;

/// <summary>Seeded xorshift64* generator. Same seed gives the same sequence on every machine.</summary>
public sealed class DeterministicRandom
{
    private ulong _state;

    public DeterministicRandom(ulong seed) => _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;

    /// <summary>The generator's state, for hashing.</summary>
    public ulong State => _state;

    public ulong NextULong()
    {
        _state ^= _state >> 12;
        _state ^= _state << 25;
        _state ^= _state >> 27;
        return _state * 2685821657736338717UL;
    }

    /// <summary>Uniform integer in [min, max).</summary>
    public int Range(int min, int max) => min + (int)(NextULong() % (ulong)(max - min));

    /// <summary>Stateless hash of coordinates, for noise. Pure integer maths.</summary>
    public static uint Hash(int x, int z, ulong seed)
    {
        ulong h = seed ^ ((ulong)(uint)x * 0x9E3779B1UL) ^ ((ulong)(uint)z * 0x85EBCA77UL);
        h ^= h >> 33; h *= 0xFF51AFD7ED558CCDUL;
        h ^= h >> 33; h *= 0xC4CEB9FE1A85EC53UL;
        h ^= h >> 33;
        return (uint)h;
    }
}
