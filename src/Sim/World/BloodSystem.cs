using System;
using System.Collections.Generic;

namespace Rts.Sim.World;

/// <summary>
/// Blood on the ground. Bleeding units drip on the top block of the column they stand on, hits that draw blood
/// splash it, and deaths leave a pool (see <see cref="Units.CombatSystem"/>), so a wounded unit leaves a trail and a
/// battle leaves a stained field. It is game state, not a look: wolves follow fresh trails (see
/// <see cref="Units.MonsterSystem"/>), and later rules read it too (corruption, the dead rising).
///
/// A column has at most one stain, on one block (the last level blood fell on). A stain has an amount (up to
/// <see cref="MaxAmount"/>), an age and the unit whose blood it last was. It ages a second a second on a clear
/// day, <see cref="RainAge"/> in rain and <see cref="StormAge"/> in a storm, and is gone at the role table's
/// blood time (<see cref="Content.CombatRules.BloodDays"/>, a day by default); new blood makes it younger
/// in step with how much more there is. Digging out or changing the stained block, or anything (water most of
/// all) filling the space over it, takes the stain away; blood never falls on water.
///
/// Stains are kept in a list, in the order they came, with a lookup by column; at most <see cref="MaxStains"/>.
/// When the list is full, the faintest eighth (amount by time left) goes, so the newest blood always shows.
/// </summary>
public sealed class BloodSystem
{
    /// <summary>The most stains on the map at once.</summary>
    public const int MaxStains = 8192;
    /// <summary>The most blood one column holds: a pool.</summary>
    public const int MaxAmount = 255;
    /// <summary>Blood a unit's death leaves where it falls.</summary>
    public const int Pool = 160;
    /// <summary>Blood each hit point bled away leaves.</summary>
    public const int PerHp = 4;
    /// <summary>Blood a bleeding unit drips every <see cref="DripTicks"/>.</summary>
    public const int Drip = 3;
    public const int DripTicks = 5;
    /// <summary>Seconds a stain ages each second in rain, and in a storm.</summary>
    public const int RainAge = 3, StormAge = 6;
    /// <summary>A stain fresher than this (seconds since blood last fell on it) is a trail a tracker can follow.</summary>
    public const int FreshSeconds = 60;

    private const int Tps = Simulation.TicksPerSecond;

    private readonly Simulation _sim;
    private readonly int _sizeZ;
    // Stains, by slot: column (x * SizeZ + z), the stained block's level, amount, age (seconds), the unit
    // whose blood it last was (id), and the tick blood last fell on it.
    private int[] _column = new int[64];
    private int[] _y = new int[64];
    private int[] _amount = new int[64];
    private int[] _age = new int[64];
    private int[] _source = new int[64];
    private long[] _fresh = new long[64];
    // Where in the column the last blood fell, in sixteenths of a block from its corner (x, z).
    private byte[] _fx = new byte[64];
    private byte[] _fz = new byte[64];
    private int _count;
    // Lookup only, never walked: the order of the list above is what counts.
    private readonly Dictionary<int, int> _slotOf = new();

    public BloodSystem(Simulation sim)
    {
        _sim = sim;
        _sizeZ = sim.World.SizeZ;
        sim.World.BlockChanged += OnBlockChanged;
    }

    /// <summary>Stains on the map.</summary>
    public int Count => _count;

    /// <summary>Changes each time any stain changes, for drawing.</summary>
    public int Version { get; private set; }

    /// <summary>Seconds a stain lasts on a clear day.</summary>
    public int LifeSeconds => Math.Max(1, (_sim.Content.Combat.BloodDays * Conditions.DayTicks / Tps).RoundToInt());

    /// <summary>The stain in a slot (0 to <see cref="Count"/> - 1), for drawing and tests.</summary>
    public Stain this[int slot] => new(_column[slot] / _sizeZ, _y[slot], _column[slot] % _sizeZ, _amount[slot], _age[slot], _source[slot], _fresh[slot],
        _fx[slot], _fz[slot]);

    /// <summary>The blood on column (x, z), or 0.</summary>
    public int At(int x, int z) =>
        x >= 0 && z >= 0 && x < _sim.World.SizeX && z < _sizeZ && _slotOf.TryGetValue(x * _sizeZ + z, out int s) ? _amount[s] : 0;

    /// <summary>The stain on column (x, z), if any.</summary>
    public Stain? StainAt(int x, int z) => _slotOf.TryGetValue(x * _sizeZ + z, out int s) ? this[s] : null;

    /// <summary>Total blood on the map.</summary>
    public long Total
    {
        get
        {
            long t = 0;
            for (int s = 0; s < _count; s++) t += _amount[s];
            return t;
        }
    }

    public ulong ComputeHash()
    {
        ulong h = 14695981039346656037UL;
        void Mix(long v) { h = (h ^ (ulong)v) * 1099511628211UL; }
        Mix(_count);
        for (int s = 0; s < _count; s++)
        {
            Mix(_column[s]); Mix(_y[s]); Mix(_amount[s]); Mix(_age[s]); Mix(_source[s]); Mix(_fresh[s]); Mix(_fx[s] | _fz[s] << 8);
        }
        return h;
    }

    /// <summary>
    /// Blood falls on block (x, y, z), the top of the column a unit stands on (its feet are at y + 1): the
    /// unit <paramref name="source"/>'s (id, or 0). Nothing if the block is not solid or water covers it.
    /// </summary>
    public void Spill(int x, int y, int z, int amount, int source) => Spill(x, y, z, amount, source, 8, 8);

    /// <summary>
    /// Blood falls at (x, z) in block coordinates (a unit's position), on block level y under it: as
    /// <see cref="Spill(int, int, int, int, int)"/>, and the stain remembers where in the column it fell.
    /// </summary>
    public void Spill(Fix64 x, int y, Fix64 z, int amount, int source)
    {
        int bx = x.FloorToInt(), bz = z.FloorToInt();
        int fx = Math.Clamp(((x - Fix64.FromInt(bx)) * 16).FloorToInt(), 0, 15);
        int fz = Math.Clamp(((z - Fix64.FromInt(bz)) * 16).FloorToInt(), 0, 15);
        Spill(bx, y, bz, amount, source, fx, fz);
    }

    private void Spill(int x, int y, int z, int amount, int source, int fx, int fz)
    {
        var world = _sim.World;
        if (amount <= 0 || !world.InBounds(x, y, z) || !BlockInfo.IsSolid(world.Get(x, y, z))) return;
        if (world.Get(x, y + 1, z) == BlockType.Water) return;
        int column = x * _sizeZ + z;
        if (!_slotOf.TryGetValue(column, out int s))
        {
            if (_count == MaxStains) Squeeze();
            if (_count == _column.Length) Grow(_count * 2);
            s = _count++;
            _slotOf[column] = s;
            _column[s] = column;
            _amount[s] = 0;
            _age[s] = 0;
        }
        int was = _amount[s];
        int now = Math.Min(MaxAmount, was + amount);
        // New blood makes the stain younger in step with how much of it is new.
        _age[s] = now == 0 ? 0 : (int)((long)_age[s] * was / now);
        _amount[s] = now;
        _y[s] = y;
        _source[s] = source;
        _fresh[s] = _sim.Tick;
        _fx[s] = (byte)fx;
        _fz[s] = (byte)fz;
        Version++;
    }

    /// <summary>Once a second, every stain ages by the weather, and those at the end of their time are gone.</summary>
    public void Step()
    {
        if (_count == 0 || _sim.Tick % Tps != 0) return;
        int step = _sim.Conditions.Weather switch
        {
            Weather.Rain => RainAge,
            Weather.Storm => StormAge,
            _ => 1,
        };
        int life = LifeSeconds;
        for (int s = _count - 1; s >= 0; s--)
        {
            _age[s] += step;
            if (_age[s] >= life) RemoveSlot(s);
        }
        Version++;
    }

    /// <summary>
    /// The freshest stain within <paramref name="radius"/> columns of (x, z) that blood fell on in the last
    /// <see cref="FreshSeconds"/>, whose unit is still on the map and passes <paramref name="follow"/>: that unit's
    /// id, or 0. Ties go to the stain nearer, then to the lower column.
    /// </summary>
    public int FreshTrail(int x, int z, int radius, Func<int, bool> follow)
    {
        if (_count == 0) return 0;
        long since = _sim.Tick - FreshSeconds * Tps;
        int best = 0, bestD = 0, bestColumn = 0;
        long bestFresh = long.MinValue;
        var world = _sim.World;
        int r2 = radius * radius;
        for (int dx = -radius; dx <= radius; dx++)
        for (int dz = -radius; dz <= radius; dz++)
        {
            int cx = x + dx, cz = z + dz, d = dx * dx + dz * dz;
            if (d > r2 || cx < 0 || cz < 0 || cx >= world.SizeX || cz >= world.SizeZ) continue;
            int column = cx * _sizeZ + cz;
            if (!_slotOf.TryGetValue(column, out int s) || _fresh[s] < since || _source[s] == 0) continue;
            bool better = _fresh[s] > bestFresh || (_fresh[s] == bestFresh && (d < bestD || (d == bestD && column < bestColumn)));
            if (!better || !follow(_source[s])) continue;
            best = _source[s];
            bestFresh = _fresh[s];
            bestD = d;
            bestColumn = column;
        }
        return best;
    }

    /// <summary>The stain on column (x, z) is scrubbed away (see <see cref="Economy.EconomySystem"/>'s cleaners). False if there was none.</summary>
    public bool Clean(int x, int z)
    {
        if (!_slotOf.TryGetValue(x * _sizeZ + z, out int s)) return false;
        RemoveSlot(s);
        Version++;
        return true;
    }

    /// <summary>
    /// The stained column within <paramref name="radius"/> columns of (cx, cz) nearest (fromX, fromZ) that
    /// <paramref name="skip"/> does not rule out (ties to the lower column), or false.
    /// </summary>
    public bool Nearest(int cx, int cz, int radius, int fromX, int fromZ, Func<int, int, bool> skip, out int x, out int y, out int z)
    {
        x = y = z = 0;
        if (_count == 0) return false;
        var world = _sim.World;
        long best = long.MaxValue;
        int r2 = radius * radius;
        for (int dx = -radius; dx <= radius; dx++)
        for (int dz = -radius; dz <= radius; dz++)
        {
            int px = cx + dx, pz = cz + dz;
            if (dx * dx + dz * dz > r2 || px < 0 || pz < 0 || px >= world.SizeX || pz >= world.SizeZ) continue;
            if (!_slotOf.TryGetValue(px * _sizeZ + pz, out int s) || skip(px, pz)) continue;
            long d = (long)(px - fromX) * (px - fromX) + (long)(pz - fromZ) * (pz - fromZ);
            long key = d * (world.SizeX * (long)_sizeZ) + px * (long)_sizeZ + pz;
            if (key >= best) continue;
            best = key;
            x = px; y = _y[s]; z = pz;
        }
        return best != long.MaxValue;
    }

    private void OnBlockChanged(int x, int y, int z)
    {
        if (_count == 0 || !_slotOf.TryGetValue(x * _sizeZ + z, out int s)) return;
        // The stained block dug out, fallen or changed, or something (water) filling the space over it.
        if (y != _y[s] && y != _y[s] + 1) return;
        if (y == _y[s] + 1 && _sim.World.Get(x, y, z) == BlockType.Air) return;
        RemoveSlot(s);
        Version++;
    }

    /// <summary>The list is full: the faintest eighth of the stains (amount by time left) goes.</summary>
    private void Squeeze()
    {
        int life = LifeSeconds;
        Span<int> counts = stackalloc int[MaxAmount + 1];
        for (int s = 0; s < _count; s++) counts[Weight(s, life)]++;
        int target = MaxStains / 8, freed = 0, threshold = 0;
        while (threshold <= MaxAmount && freed + counts[threshold] < target) freed += counts[threshold++];
        // Every stain lighter than the threshold goes, and then those at it, first slots first, until enough are gone.
        int atThreshold = target - freed;
        for (int s = _count - 1; s >= 0; s--)
        {
            int w = Weight(s, life);
            if (w < threshold) RemoveSlot(s);
        }
        for (int s = 0; s < _count && atThreshold > 0; )
        {
            if (Weight(s, life) == threshold)
            {
                RemoveSlot(s);
                atThreshold--;
            }
            else s++;
        }
    }

    private int Weight(int s, int life) => (int)((long)_amount[s] * Math.Max(0, life - _age[s]) / life);

    private void RemoveSlot(int s)
    {
        _slotOf.Remove(_column[s]);
        int last = --_count;
        if (s == last) return;
        _column[s] = _column[last]; _y[s] = _y[last]; _amount[s] = _amount[last]; _age[s] = _age[last];
        _source[s] = _source[last]; _fresh[s] = _fresh[last]; _fx[s] = _fx[last]; _fz[s] = _fz[last];
        _slotOf[_column[s]] = s;
    }

    private void Grow(int capacity)
    {
        Array.Resize(ref _column, capacity); Array.Resize(ref _y, capacity); Array.Resize(ref _amount, capacity);
        Array.Resize(ref _age, capacity); Array.Resize(ref _source, capacity); Array.Resize(ref _fresh, capacity);
        Array.Resize(ref _fx, capacity); Array.Resize(ref _fz, capacity);
    }
}

/// <summary>
/// One column's blood (see <see cref="BloodSystem"/>): on block (X, Y, Z), with its amount, age in seconds, the
/// unit whose blood it last was, when blood last fell, and where in the column it fell (FX, FZ, in sixteenths of
/// a block from the column's corner), so a trail can be drawn along the way the unit went.
/// </summary>
public readonly record struct Stain(int X, int Y, int Z, int Amount, int Age, int Source, long Fresh, int FX = 8, int FZ = 8);
