using System;
using System.Collections.Generic;

namespace Rts.Sim;

/// <summary>What a player knows of a column of the world under fog of war (see <see cref="VisionSystem"/>).</summary>
public enum Sight : byte
{
    /// <summary>Never seen.</summary>
    Unseen = 0,
    /// <summary>Seen before, but not in sight now.</summary>
    Seen = 1,
    /// <summary>In sight of one of the player's units or buildings now.</summary>
    Visible = 2,
}

/// <summary>
/// Fog of war (issue #39): what each player sees, column by column. A column is in sight while it is within
/// the sight of one of the player's units (the unit's sight in the unit data, from the column it stands on)
/// or buildings (the building's sight beyond its footprint, corners rounded), and once seen it stays seen.
/// It is worked out every <see cref="UpdateTicks"/> ticks, after the units have moved, for every player who
/// has units or buildings (not the wild monsters), the same on every machine, so a computer player can go by
/// it. With day and night on (<see cref="Simulation.DayNight"/>), sight is shortened by the dark, the weather
/// and the swamp's mist, but lit columns are seen from as far as by day (see <see cref="Conditions"/>). With fog of war off (<see cref="Simulation.Fog"/>) nothing is worked out and every player sees
/// everything.
/// <para>
/// Units underground (natural ground over their heads: in a tunnel or a cave, see <see cref="Covered"/>) are not seen
/// from the surface. A player sees another's unit underground only where they hear it (<see cref="Hears"/>): within
/// <see cref="HearClose"/> columns of one of their own units, within the sight of one of their own units that is
/// underground too, or within a listening post's <see cref="Economy.BuildingDef.Hearing"/>. So sappers can dig under a
/// wall unseen, unless its owner keeps a listening post near it.
/// </para>
/// </summary>
public sealed class VisionSystem
{
    /// <summary>Ticks between looks: a unit at a gallop moves two columns in that time.</summary>
    public const int UpdateTicks = 4;
    /// <summary>Columns round each of a player's units in which it hears units underground right by it.</summary>
    public const int HearClose = 2;

    private readonly Simulation _sim;
    private readonly int _sizeX, _sizeZ;
    // Per player, a Sight per column (x + z * SizeX); null until the player has something that sees.
    private readonly byte[]?[] _grids = new byte[]?[Simulation.WildPlayer];
    // Per player, how many columns are seen or in sight: the only part of this that is history.
    private readonly long[] _seen = new long[Simulation.WildPlayer];
    // Per player, 1 for each column (x + z * SizeX) where they hear units underground now; null until they hear anything.
    private readonly byte[]?[] _heard = new byte[]?[Simulation.WildPlayer];
    // Per unit slot, 1 if the unit was underground at the last look (only players' units, not wild monsters).
    private byte[] _under = Array.Empty<byte>();
    // Half-widths of the rows of a disc, by its squared radius.
    private readonly Dictionary<int, int[]> _discs = new();

    public VisionSystem(Simulation sim)
    {
        _sim = sim;
        _sizeX = sim.World.SizeX;
        _sizeZ = sim.World.SizeZ;
    }

    /// <summary>Counts the looks taken, so a renderer can tell when there is something new to draw.</summary>
    public int Version { get; private set; }

    /// <summary>What the player knows of column (x, z): with fog of war off, everything is in sight.</summary>
    public Sight At(int player, int x, int z)
    {
        if (!_sim.Fog) return Sight.Visible;
        if ((uint)x >= (uint)_sizeX || (uint)z >= (uint)_sizeZ || (uint)player >= (uint)_grids.Length) return Sight.Unseen;
        return _grids[player] is { } g ? (Sight)g[x + z * _sizeX] : Sight.Unseen;
    }

    /// <summary>True if column (x, z) is in the player's sight now.</summary>
    public bool Sees(int player, int x, int z) => At(player, x, z) == Sight.Visible;

    /// <summary>
    /// True if the player sees unit i now: it is theirs, or it stands where they can see, or, if it is underground,
    /// where they hear it (see <see cref="Hears"/>).
    /// </summary>
    public bool SeesUnit(int player, int i)
    {
        var u = _sim.Units;
        if (u.Owner[i] == player || !_sim.Fog) return true;
        int x = u.X[i].FloorToInt(), z = u.Z[i].FloorToInt();
        return Underground(i) ? Hears(player, x, z) : Sees(player, x, z);
    }

    /// <summary>True if unit i (a player's, not a wild monster) was underground at the last look, with fog of war on.</summary>
    public bool Underground(int i) => _sim.Fog && (uint)i < (uint)_under.Length && _under[i] != 0;

    /// <summary>
    /// True if the player hears units underground in column (x, z) now: it is within <see cref="HearClose"/> of one of
    /// their units, within the sight of one of their units underground, or within the hearing of one of their listening
    /// posts. With fog of war off, everything is seen anyway.
    /// </summary>
    public bool Hears(int player, int x, int z)
    {
        if (!_sim.Fog) return true;
        if ((uint)x >= (uint)_sizeX || (uint)z >= (uint)_sizeZ || (uint)player >= (uint)_heard.Length) return false;
        return _heard[player] is { } g && g[x + z * _sizeX] != 0;
    }

    /// <summary>
    /// True if a unit standing at (x, y, z) is underground: natural ground (not a building, a tree, ice or water) lies
    /// somewhere over its head in its column, so nobody looking from the surface sees it.
    /// </summary>
    public static bool Covered(VoxelWorld world, int x, int y, int z)
    {
        if ((uint)x >= (uint)world.SizeX || (uint)z >= (uint)world.SizeZ) return false;
        int cs = VoxelWorld.ChunkSize, cx = x / cs, cz = z / cs;
        for (int k = Math.Max(0, y + Units.Walk.Clearance); k < world.SizeY; k++)
        {
            if (!world.IsChunkAllocated(cx, k / cs, cz)) { k = (k / cs + 1) * cs - 1; continue; } // empty air
            if (Cover(world.Get(x, k, z))) return true;
        }
        return false;
    }

    private static bool Cover(BlockType t) =>
        BlockInfo.IsSolid(t) && !BlockInfo.IsBuilt(t) && !BlockInfo.IsLeaves(t) && !BlockInfo.IsTrunk(t)
        && t is not (BlockType.Ice or BlockType.Lava or BlockType.BerryBush);

    /// <summary>Columns the player has seen or sees now.</summary>
    public long SeenColumns(int player) => (uint)player < (uint)_seen.Length ? _seen[player] : 0;

    /// <summary>
    /// The player's columns as <see cref="Sight"/> values, indexed x + z * SizeX (for drawing the fog);
    /// empty while the player has seen nothing, or with fog of war off.
    /// </summary>
    public ReadOnlySpan<byte> Grid(int player) =>
        _sim.Fog && (uint)player < (uint)_grids.Length && _grids[player] is { } g ? g : ReadOnlySpan<byte>.Empty;

    /// <summary>Hash of what the players have seen (what they see now follows from where everything is).</summary>
    public ulong ComputeHash()
    {
        ulong h = 14695981039346656037UL;
        foreach (long n in _seen) h = (h ^ (ulong)n) * 1099511628211UL;
        return h;
    }

    /// <summary>Once every <see cref="UpdateTicks"/> ticks, with fog of war on: what everyone sees now.</summary>
    public void Step()
    {
        if (!_sim.Fog || _sim.Tick % UpdateTicks != 0) return;
        foreach (var g in _grids)
            if (g != null) g.AsSpan().Replace((byte)Sight.Visible, (byte)Sight.Seen);
        foreach (var g in _heard)
            if (g != null) Array.Clear(g);

        var u = _sim.Units;
        var defs = _sim.Content.Units;
        var c = _sim.Conditions;
        var world = _sim.World;
        bool lights = c.Lights.Any;
        if (_under.Length < u.Count) _under = new byte[Math.Max(u.Count, 2 * _under.Length)];
        for (int i = 0; i < u.Count; i++)
        {
            int p = u.Owner[i];
            bool owned = (uint)p < (uint)_grids.Length;
            int x = u.X[i].FloorToInt(), z = u.Z[i].FloorToInt();
            bool under = owned && Covered(world, x, u.Y[i], z);
            _under[i] = under ? (byte)1 : (byte)0;
            if (!owned) continue;
            var sight = defs[u.Def[i]].Sight;
            int dark = c.SightPercent(x, z, false);
            var s = sight * dark / 100;
            Stamp(p, x, z, x, z, (s * s).FloorToInt());
            Hear(p, x, z, x, z, under ? Math.Max((s * s).FloorToInt(), HearClose * HearClose) : HearClose * HearClose);
            int lit = lights ? c.SightPercent(x, z, true) : dark;
            if (lit > dark)
            {
                s = sight * lit / 100;
                Stamp(p, x, z, x, z, (s * s).FloorToInt(), litOnly: true);
            }
        }
        foreach (var b in _sim.Buildings.All)
        {
            if ((uint)b.Owner >= (uint)_grids.Length || b.Planned) continue;
            if (b.Def.Hearing > 0 && b.Active) Hear(b.Owner, b.X, b.Z, b.MaxX, b.MaxZ, b.Def.Hearing * b.Def.Hearing);
            int dark = c.SightPercent(b.X, b.Z, false);
            int s = b.Def.Sight * dark / 100;
            Stamp(b.Owner, b.X, b.Z, b.MaxX, b.MaxZ, s * s);
            int lit = lights ? c.SightPercent(b.X, b.Z, true) : dark;
            if (lit > dark)
            {
                s = b.Def.Sight * lit / 100;
                Stamp(b.Owner, b.X, b.Z, b.MaxX, b.MaxZ, s * s, litOnly: true);
            }
        }
        // A held town whose scouts ride the land round it (the centaurs' camp) shows its holder that land, as a
        // building's sight would: less of it in the dark, more where it is lit.
        foreach (var town in _sim.Cities.Towns)
        {
            int range = town.Def.WatchRange;
            if (range <= 0 || (uint)town.Holder >= (uint)_grids.Length) continue;
            int r0 = Economy.CityStateSystem.TownRadius;
            int dark = c.SightPercent(town.X, town.Z, false);
            int s = range * dark / 100;
            Stamp(town.Holder, town.X - r0, town.Z - r0, town.X + r0, town.Z + r0, s * s);
            int lit = lights ? c.SightPercent(town.X, town.Z, true) : dark;
            if (lit > dark)
            {
                s = range * lit / 100;
                Stamp(town.Holder, town.X - r0, town.Z - r0, town.X + r0, town.Z + r0, s * s, litOnly: true);
            }
        }
        Version++;
    }

    /// <summary>
    /// Marks every column within <paramref name="r"/> of (x, z) that the player has never seen as seen (not in sight):
    /// what a chart shows (a reveal tech, see <see cref="Content.TechEffectType.Reveal"/>). The ground there looks as
    /// it did when the match began until the player's units see it for themselves. Nothing with fog of war off.
    /// </summary>
    internal void Reveal(int player, int x, int z, int r)
    {
        if (!_sim.Fog || (uint)player >= (uint)_grids.Length) return;
        var g = _grids[player] ??= new byte[_sizeX * _sizeZ];
        var half = Disc(r * r);
        long seen = 0;
        for (int dz = -r; dz <= r; dz++)
        {
            int cz = z + dz;
            if ((uint)cz >= (uint)_sizeZ) continue;
            int h = half[Math.Abs(dz)];
            for (int cx = Math.Max(0, x - h); cx <= Math.Min(_sizeX - 1, x + h); cx++)
            {
                ref byte b = ref g[cx + cz * _sizeX];
                if (b != (byte)Sight.Unseen) continue;
                b = (byte)Sight.Seen;
                seen++;
            }
        }
        _seen[player] += seen;
        Version++;
    }

    /// <summary>
    /// Puts the rectangle of columns (x0, z0)-(x1, z1), and every column within the square root of
    /// <paramref name="r2"/> of it, in the player's sight; with <paramref name="litOnly"/>, only those in
    /// the light (see <see cref="Lights"/>).
    /// </summary>
    private void Stamp(int player, int x0, int z0, int x1, int z1, int r2, bool litOnly = false)
    {
        var lights = _sim.Conditions.Lights;
        var g = _grids[player] ??= new byte[_sizeX * _sizeZ];
        var half = Disc(r2);
        int r = half.Length - 1;
        long seen = 0;
        for (int z = Math.Max(0, z0 - r); z <= Math.Min(_sizeZ - 1, z1 + r); z++)
        {
            int dz = z < z0 ? z0 - z : z > z1 ? z - z1 : 0;
            int h = half[dz];
            int from = Math.Max(0, x0 - h), to = Math.Min(_sizeX - 1, x1 + h);
            var row = g.AsSpan(z * _sizeX + from, to - from + 1);
            for (int k = 0; k < row.Length; k++)
            {
                if (litOnly && !lights.Lit(from + k, z)) continue;
                if (row[k] == (byte)Sight.Unseen) seen++;
                row[k] = (byte)Sight.Visible;
            }
        }
        _seen[player] += seen;
    }

    /// <summary>
    /// Lets the player hear units underground in the rectangle of columns (x0, z0)-(x1, z1) and every column within
    /// the square root of <paramref name="r2"/> of it.
    /// </summary>
    private void Hear(int player, int x0, int z0, int x1, int z1, int r2)
    {
        var g = _heard[player] ??= new byte[_sizeX * _sizeZ];
        var half = Disc(r2);
        int r = half.Length - 1;
        for (int z = Math.Max(0, z0 - r); z <= Math.Min(_sizeZ - 1, z1 + r); z++)
        {
            int dz = z < z0 ? z0 - z : z > z1 ? z - z1 : 0;
            int h = half[dz];
            int from = Math.Max(0, x0 - h), to = Math.Min(_sizeX - 1, x1 + h);
            g.AsSpan(z * _sizeX + from, to - from + 1).Fill(1);
        }
    }

    /// <summary>For each row 0..r of a disc of squared radius r2, how many columns it reaches either side of the middle.</summary>
    private int[] Disc(int r2)
    {
        if (_discs.TryGetValue(r2, out var half)) return half;
        int r = (int)IntMath.ISqrt(r2);
        half = new int[r + 1];
        for (int dz = 0; dz <= r; dz++) half[dz] = (int)IntMath.ISqrt(r2 - dz * dz);
        _discs[r2] = half;
        return half;
    }
}
