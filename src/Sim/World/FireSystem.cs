using System;
using System.Collections.Generic;
using Rts.Sim.Economy;

namespace Rts.Sim.World;

/// <summary>
/// Forest fires. Trees burn: a block of a tree's crown or trunk set alight (see <see cref="Ignite"/>) burns for a
/// while and then is gone, and while it burns the fire spreads to the wood and leaves around it.
///
/// Once a second every burning block:
/// <list type="bullet">
/// <item>in rain goes out with a chance of <see cref="RainDouse"/> percent, in a storm <see cref="StormDouse"/>;</item>
/// <item>otherwise counts down its burning time (<see cref="LeafBurn"/> seconds for leaves, <see cref="TrunkBurn"/>
/// for wood). Burnt out, leaves are gone and so is wood, but for the foot of a trunk (the block standing on
/// something that is not trunk), which is left a charred stump (<see cref="BlockType.CharredWood"/>): workers dig it
/// for charcoal (coal);</item>
/// <item>sets alight each block of tree within a column of it (a level below it to two above) with a chance of
/// <see cref="LeafCatch"/> percent for leaves and <see cref="TrunkCatch"/> for wood, doubled for what is above it
/// (fire climbs), the trunk over or under a burning trunk with <see cref="AlongTrunk"/>, and a crown two columns off
/// with <see cref="LeapCatch"/> (sparks jump a small gap). The chance
/// is the weather's share (<see cref="WeatherPercent"/>: a clear day all of it, cloudy three quarters, fog
/// two fifths, rain and storm none) of it, less the ground's wetness in percent, so a wet spell leaves the forests
/// hard to burn for hours;</item>
/// <item>scorches each building with a block of timber or thatch beside it (see <see cref="Flammable"/>): it takes
/// <see cref="BuildingScorch"/> damage a second, by whoever lit the fire. Stone does not burn.</item>
/// </list>
///
/// A unit with a burning block within a column of it, at its feet's level or up to two above (or the one below),
/// loses <see cref="HeatDamage"/> hit points a second (see <see cref="HazardSystem"/>), by no one, as from any hazard.
///
/// Fires are lit by units ordered to set a tree alight (<see cref="KindleCommand"/>), which they cannot do in rain or a
/// storm, and by the shots of units whose attack sets things alight (<c>"ignites"</c> in the data: the siege
/// engineer's fire pots), which light every block of tree within a block of where they land. The dusk tree and
/// giant mushrooms do not burn. Whether a block catches is a hash of the tick, the block and the seed, so every machine
/// and replay agrees; the burning blocks are part of the state hash. The numbers are first guesses for playtesting.
/// </summary>
public sealed class FireSystem
{
    /// <summary>Seconds a burning block of leaves burns before it is gone.</summary>
    public const int LeafBurn = 6;
    /// <summary>Seconds a burning block of trunk burns before it is gone (or charred, at the foot).</summary>
    public const int TrunkBurn = 10;
    /// <summary>Percent a second that a block of leaves within a column of a burning block catches.</summary>
    public const int LeafCatch = 30;
    /// <summary>Percent a second that a block of trunk within a column of a burning block catches.</summary>
    public const int TrunkCatch = 15;
    /// <summary>Percent a second that the trunk block over or under a burning block of trunk catches (fire runs along the trunk).</summary>
    public const int AlongTrunk = 50;
    /// <summary>Percent a second that a block of leaves two columns from a burning one catches (sparks).</summary>
    public const int LeapCatch = 6;
    /// <summary>Percent a second that a burning block goes out in the rain.</summary>
    public const int RainDouse = 25;
    /// <summary>Percent a second that a burning block goes out in a storm.</summary>
    public const int StormDouse = 50;
    /// <summary>Hit points a second lost by a unit beside a burning block.</summary>
    public const int HeatDamage = 6;
    /// <summary>Damage a second to a building with timber or thatch beside a burning block.</summary>
    public const int BuildingScorch = 8;
    /// <summary>Most blocks burning at once: past it, nothing more catches until some burn out.</summary>
    public const int MaxBurning = 6000;
    /// <summary>Coal a charred stump block gives (see <see cref="Harvest.Amount"/>).</summary>
    public const int CharcoalPerStump = 2;

    private readonly Simulation _sim;
    /// <summary>Burning blocks by key (see <see cref="Harvest.Key"/>): seconds left to burn, and who lit it (-1 for no one).</summary>
    private readonly SortedDictionary<long, (int Left, int By)> _burning = new();
    private readonly List<long> _keys = new();
    private readonly SortedSet<int> _scorched = new();
    private readonly Dictionary<int, int> _scorchBy = new();
    /// <summary>Charred stumps fires have left, by key (some may have been dug since: see <see cref="Stumps"/>).</summary>
    private readonly SortedSet<long> _stumps = new();
    private readonly List<long> _dug = new();

    public FireSystem(Simulation sim)
    {
        _sim = sim;
    }

    /// <summary>How many blocks are burning.</summary>
    public int Count => _burning.Count;

    /// <summary>Blocks that have burnt out since the match began.</summary>
    public int BurntOut { get; private set; }

    /// <summary>Fires that went out in the rain since the match began.</summary>
    public int Doused { get; private set; }

    /// <summary>True if the block can burn: a tree's trunk or crown, but not the dusk tree's nor a giant mushroom.</summary>
    public static bool CanBurn(BlockType b) =>
        (BlockInfo.IsTrunk(b) || BlockInfo.IsLeaves(b)) && b is not (BlockType.MushroomStem or BlockType.MushroomCap or BlockType.DuskLeaves);

    /// <summary>A building block fire scorches: timber, thatch, stakes, frames, doors, floors and living wood.</summary>
    public static bool Flammable(BlockType b) => b is BlockType.Planks or BlockType.Thatch or BlockType.Door or BlockType.Gate
        or BlockType.Palisade or BlockType.Trestle or BlockType.Floor or BlockType.Livewood;

    /// <summary>Percent of the usual chance to catch in this weather: none in rain or a storm.</summary>
    public static int WeatherPercent(Weather w) => w switch
    {
        Weather.Clear => 100,
        Weather.Cloudy => 75,
        Weather.Fog => 40,
        _ => 0,
    };

    /// <summary>True if fires can be lit now: not in rain or a storm.</summary>
    public bool CanKindle => WeatherPercent(_sim.Conditions.Weather) > 0;

    /// <summary>True if block (x, y, z) is burning.</summary>
    public bool Burning(int x, int y, int z) =>
        _burning.Count > 0 && _sim.World.InBounds(x, y, z) && _burning.ContainsKey(Harvest.Key(_sim.World, x, y, z));

    /// <summary>Every burning block, in key order (for the game's flames).</summary>
    public IEnumerable<(int X, int Y, int Z)> All()
    {
        var w = _sim.World;
        foreach (long k in _burning.Keys) yield return Unkey(w, k);
    }

    /// <summary>
    /// The charred stumps fires have left that are still there, in key order (for computer players looking for
    /// charcoal: see <c>AiPlayer.Charcoal.cs</c>).
    /// </summary>
    public IEnumerable<(int X, int Y, int Z)> Stumps()
    {
        var w = _sim.World;
        foreach (long k in _stumps)
        {
            var (x, y, z) = Unkey(w, k);
            if (w.Get(x, y, z) == BlockType.CharredWood) yield return (x, y, z);
        }
    }

    /// <summary>How many charred stumps fires have left are still there.</summary>
    public int StumpCount
    {
        get
        {
            int n = 0;
            foreach (var _ in Stumps()) n++;
            return n;
        }
    }

    /// <summary>
    /// Sets block (x, y, z) alight, by player <paramref name="by"/> (-1 for no one), if it can burn and is not burning
    /// already. True if it caught.
    /// </summary>
    public bool Ignite(int x, int y, int z, int by)
    {
        var w = _sim.World;
        if (!w.InBounds(x, y, z) || _burning.Count >= MaxBurning) return false;
        var b = w.Get(x, y, z);
        if (!CanBurn(b)) return false;
        long k = Harvest.Key(w, x, y, z);
        if (_burning.ContainsKey(k)) return false;
        _burning[k] = (BlockInfo.IsTrunk(b) ? TrunkBurn : LeafBurn, by);
        return true;
    }

    /// <summary>A shot that sets things alight lands at (x, y, z): every block of tree within a block of it catches. How many did.</summary>
    public int IgniteAround(int x, int y, int z, int by)
    {
        if (!CanKindle) return 0;
        int lit = 0;
        for (int dy = -1; dy <= 2; dy++)
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
            if (Ignite(x + dx, y + dy, z + dz, by)) lit++;
        return lit;
    }

    /// <summary>
    /// Hit points a second a unit with its feet at (x, y, z) loses to the fire: <see cref="HeatDamage"/> if a burning
    /// block is within a column of it, from the level under its feet to two above them.
    /// </summary>
    public int HeatAt(int x, int y, int z)
    {
        if (_burning.Count == 0) return 0;
        for (int dy = -1; dy <= 2; dy++)
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
            if (Burning(x + dx, y + dy, z + dz)) return HeatDamage;
        return 0;
    }

    /// <summary>Once a tick: on each second, every fire spreads, burns down or goes out, and scorches the buildings beside it.</summary>
    public void Step()
    {
        // Every half minute the stumps dug since are forgotten.
        if (_stumps.Count > 0 && _sim.Tick % (30 * Simulation.TicksPerSecond) == 0) ForgetDugStumps();
        if (_burning.Count == 0 || _sim.Tick % Simulation.TicksPerSecond != 0) return;
        var w = _sim.World;
        var weather = _sim.Conditions.Weather;
        int douse = weather == Weather.Storm ? StormDouse : weather == Weather.Rain ? RainDouse : 0;
        int share = WeatherPercent(weather) * (100 - _sim.Conditions.Wetness) / 100;
        ulong seed = _sim.Seed ^ (ulong)_sim.Tick * 0x9E3779B97F4A7C15UL;
        _keys.Clear();
        _keys.AddRange(_burning.Keys);
        _scorched.Clear();
        _scorchBy.Clear();
        foreach (long k in _keys)
        {
            var (x, y, z) = Unkey(w, k);
            var (left, by) = _burning[k];
            var block = w.Get(x, y, z);
            if (!CanBurn(block))
            {
                _burning.Remove(k); // cut down, dug away or fallen while it burned
                continue;
            }
            if (douse > 0 && Roll(k, 1, seed) < douse)
            {
                _burning.Remove(k);
                Doused++;
                continue;
            }
            if (share > 0) Spread(x, y, z, by, share, seed);
            Scorch(x, y, z, by);
            if (--left > 0)
            {
                _burning[k] = (left, by);
                continue;
            }
            _burning.Remove(k);
            BurntOut++;
            bool foot = BlockInfo.IsTrunk(block) && !BlockInfo.IsTrunk(w.Get(x, y - 1, z)) && w.Get(x, y - 1, z) != BlockType.CharredWood
                && BlockInfo.IsSolid(w.Get(x, y - 1, z));
            w.Set(x, y, z, foot ? BlockType.CharredWood : BlockType.Air);
            if (foot) _stumps.Add(k);
        }
        foreach (int id in _scorched)
            if (_sim.Buildings.Get(id) is { } b) _sim.Combat.Scorch(b, BuildingScorch, _scorchBy[id]);
    }

    /// <summary>The fire at (x, y, z) reaches out to the tree around it.</summary>
    private void Spread(int x, int y, int z, int by, int share, ulong seed)
    {
        var w = _sim.World;
        for (int dy = -1; dy <= 2; dy++)
        for (int dz = -2; dz <= 2; dz++)
        for (int dx = -2; dx <= 2; dx++)
        {
            if (dx == 0 && dy == 0 && dz == 0) continue;
            int nx = x + dx, ny = y + dy, nz = z + dz;
            if (!w.InBounds(nx, ny, nz)) continue;
            var b = w.Get(nx, ny, nz);
            if (!CanBurn(b)) continue;
            bool far = Math.Abs(dx) == 2 || Math.Abs(dz) == 2;
            if (far && (!BlockInfo.IsLeaves(b) || dy > 1)) continue;
            int chance = far ? LeapCatch : BlockInfo.IsLeaves(b) ? LeafCatch
                : dx == 0 && dz == 0 && BlockInfo.IsTrunk(w.Get(x, y, z)) ? AlongTrunk : TrunkCatch;
            if (dy > 0) chance = Math.Min(90, chance * 2);
            chance = chance * share / 100;
            long nk = Harvest.Key(w, nx, ny, nz);
            if (chance <= 0 || _burning.ContainsKey(nk)) continue;
            if (Roll(nk, 2, seed) < chance) Ignite(nx, ny, nz, by);
        }
    }

    /// <summary>Notes the buildings with timber or thatch beside the fire at (x, y, z), to be scorched once each this second.</summary>
    private void Scorch(int x, int y, int z, int by)
    {
        var w = _sim.World;
        for (int d = 0; d < 6; d++)
        {
            int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0), ny = y + (d == 2 ? 1 : d == 3 ? -1 : 0), nz = z + (d == 4 ? 1 : d == 5 ? -1 : 0);
            if (!Flammable(w.Get(nx, ny, nz))) continue;
            if (_sim.Buildings.At(nx, ny, nz) is not { } b || b.State == BuildingState.Planned) continue;
            if (_scorched.Add(b.Id)) _scorchBy[b.Id] = by;
        }
    }

    private void ForgetDugStumps()
    {
        var w = _sim.World;
        _dug.Clear();
        foreach (long k in _stumps)
        {
            var (x, y, z) = Unkey(w, k);
            if (w.Get(x, y, z) != BlockType.CharredWood) _dug.Add(k);
        }
        foreach (long k in _dug) _stumps.Remove(k);
    }

    /// <summary>A number 0..99 for block key k this second (a hash, so every machine rolls the same).</summary>
    private static int Roll(long k, int salt, ulong seed) =>
        (int)(DeterministicRandom.Hash((int)k, (int)(k >> 32) ^ salt * 0x2F1B, seed) % 100);

    private static (int X, int Y, int Z) Unkey(VoxelWorld w, long k) =>
        ((int)(k % w.SizeX), (int)(k / ((long)w.SizeX * w.SizeZ)), (int)(k / w.SizeX % w.SizeZ));

    /// <summary>Hash of the burning blocks, for the state hash.</summary>
    public ulong ComputeHash()
    {
        ulong h = 14695981039346656037UL;
        void Mix(long v) { h = (h ^ (ulong)v) * 1099511628211UL; }
        Mix(_burning.Count);
        foreach (var (k, (left, by)) in _burning)
        {
            Mix(k); Mix(left); Mix(by);
        }
        return h;
    }
}
