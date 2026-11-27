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
/// and the swamp's mist, but lit columns are seen from as far as by day (see <see cref="Conditions"/>). A grown orchard's
/// trees screen sight: a unit or building sees only <see cref="ScreenDepth"/> columns into one, and nothing behind it,
/// unless it looks down over the trees (see <see cref="Economy.BuildingKind.Orchard"/>). With fog of war off (<see cref="Simulation.Fog"/>) nothing is worked out and every player sees
/// everything.
/// <para>
/// Units underground (natural ground over their heads: in a tunnel or a cave, see <see cref="Covered"/>) are not seen
/// from the surface. A player sees another's unit underground only where they hear it (<see cref="Hears"/>): within
/// <see cref="HearClose"/> columns of one of their own units (a unit's own <see cref="Units.UnitDef.Hearing"/> if
/// more: the delver), within the sight of one of their own units that is underground too, or within a listening
/// post's <see cref="Economy.BuildingDef.Hearing"/>. A crystal lens's gaze (see <see cref="Economy.EconomySystem.Gazes"/>)
/// puts a wide circle anywhere on the map in its player's sight for a while. So sappers can dig under a
/// wall unseen, unless its owner keeps a listening post near it.
/// </para>
/// </summary>
public sealed class VisionSystem
{
    /// <summary>Ticks between looks: a unit at a gallop moves two columns in that time.</summary>
    public const int UpdateTicks = 4;
    /// <summary>Columns round each of a player's units in which it hears units underground right by it.</summary>
    public const int HearClose = 2;
    /// <summary>
    /// Columns round each of a player's units within which it spots a lurking monster lying in the water (see
    /// <see cref="Units.MonsterSystem.Submerged"/>); further off, the water hides it however near it is in sight.
    /// </summary>
    public const int SpotSubmerged = 3;

    private readonly Simulation _sim;
    private readonly int _sizeX, _sizeZ;
    // Per player, a Sight per column (x + z * SizeX); null until the player has something that sees.
    private readonly byte[]?[] _grids = new byte[]?[Simulation.WildPlayer];
    // Per player, how many columns are seen or in sight: the only part of this that is history.
    private readonly long[] _seen = new long[Simulation.WildPlayer];
    // Per player, 1 for each column (x + z * SizeX) where they hear units underground now; null until they hear anything.
    private readonly byte[]?[] _heard = new byte[]?[Simulation.WildPlayer];
    // Per player, 1 for each column within SpotSubmerged of one of their units: where they spot monsters lying in the water.
    private readonly byte[]?[] _close = new byte[]?[Simulation.WildPlayer];
    // Per player, the stretches of each of those three grids written since the last look, so a look puts back only
    // those (sight in them fades to seen, hearing and spotting are cleared) instead of going over the whole map.
    private readonly Stretches?[] _gridMarks = new Stretches?[Simulation.WildPlayer];
    private readonly Stretches?[] _heardMarks = new Stretches?[Simulation.WildPlayer];
    private readonly Stretches?[] _closeMarks = new Stretches?[Simulation.WildPlayer];
    // Per player, the war drummers (unit ids) they hear drumming at the last look (see Units.DrumDef.Heard); null until any.
    private readonly List<int>?[] _drums = new List<int>?[Simulation.WildPlayer];
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
        if ((uint)player < (uint)_drums.Length && _drums[player] is { Count: > 0 } drums && drums.Contains(u.Id[i])) return true;
        if (Units.CombatSystem.Bears(u, player, i)) return true; // a grudge is never forgotten (the Dwarves' Book of Grudges)
        int x = u.X[i].FloorToInt(), z = u.Z[i].FloorToInt();
        if (Underground(i)) return Hears(player, x, z);
        if (_sim.Monsters.Submerged(i) || _sim.Combat.Lurking(i)) return Close(player, x, z); // a Reptilian lying in the water too
        return Sees(player, x, z);
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
    /// True if column (x, z) is within <see cref="SpotSubmerged"/> columns of one of the player's units now, where
    /// they spot a monster lying in the water. With fog of war off, everything is seen anyway.
    /// </summary>
    public bool Close(int player, int x, int z)
    {
        if (!_sim.Fog) return true;
        if ((uint)x >= (uint)_sizeX || (uint)z >= (uint)_sizeZ || (uint)player >= (uint)_close.Length) return false;
        return _close[player] is { } g && g[x + z * _sizeX] != 0;
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
        // Only Stamp makes a column visible and only Fill hears or spots one, and both mark what they write.
        for (int p = 0; p < _grids.Length; p++)
        {
            if (_grids[p] is { } g) _gridMarks[p]!.Fade(g);
            if (_heard[p] is { } h) _heardMarks[p]!.Clear(h);
            if (_close[p] is { } k) _closeMarks[p]!.Clear(k);
        }

        _sim.Economy.Screens(_screens);
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
            var def = defs[u.Def[i]];
            var sight = def.Sight;
            // A unit that sees in the dark (the delver) is not held back by night, weather or mist, and sees twice as
            // far underground.
            int dark = def.DarkSight ? (under ? 200 : 100) : c.SightPercent(x, z, false);
            var s = sight * dark / 100;
            _viewer = (x, z, u.Y[i]);
            Stamp(p, x, z, x, z, (s * s).FloorToInt());
            int hear = Math.Max(HearClose, def.Hearing);
            Hear(p, x, z, x, z, under ? Math.Max((s * s).FloorToInt(), hear * hear) : hear * hear);
            if (!under) Fill(_close[p] ??= new byte[_sizeX * _sizeZ], _closeMarks[p] ??= new Stretches(_sizeX, _sizeZ), x, z, x, z, SpotSubmerged * SpotSubmerged);
            int lit = lights ? c.SightPercent(x, z, true) : dark;
            if (lit > dark)
            {
                s = sight * lit / 100;
                Stamp(p, x, z, x, z, (s * s).FloorToInt(), litOnly: true);
            }
        }
        _viewer = null;
        foreach (var b in _sim.Buildings.All)
        {
            if ((uint)b.Owner >= (uint)_grids.Length || b.Planned) continue;
            if (b.Def.Hearing > 0 && b.Active) Hear(b.Owner, b.X, b.Z, b.MaxX, b.MaxZ, b.Def.Hearing * b.Def.Hearing);
            int dark = c.SightPercent(b.X, b.Z, false);
            int s = b.Def.Sight * dark / 100;
            _viewer = ((b.X + b.MaxX) / 2, (b.Z + b.MaxZ) / 2, b.Y + b.Shape.Height);
            Stamp(b.Owner, b.X, b.Z, b.MaxX, b.MaxZ, s * s);
            int lit = lights ? c.SightPercent(b.X, b.Z, true) : dark;
            if (lit > dark)
            {
                s = b.Def.Sight * lit / 100;
                Stamp(b.Owner, b.X, b.Z, b.MaxX, b.MaxZ, s * s, litOnly: true);
            }
        }
        _viewer = null;
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
        // A crystal lens's gaze shows its player a wide circle wherever it was aimed, dark or not.
        foreach (var g in _sim.Economy.Gazes)
            if ((uint)g.Player < (uint)_grids.Length) Stamp(g.Player, g.X, g.Z, g.X, g.Z, g.Radius * g.Radius);
        HearDrums();
        Version++;
    }

    /// <summary>
    /// Each player hears the war drummers drumming (see <see cref="Units.CombatSystem.Drumming"/>) with one of their
    /// units or buildings within the drummer's <see cref="Units.DrumDef.Heard"/> columns: they see the drummer through
    /// the fog of war (<see cref="SeesUnit"/>) until the next look.
    /// </summary>
    private void HearDrums()
    {
        foreach (var l in _drums) l?.Clear();
        var drumming = _sim.Combat.Drumming;
        if (drumming.Count == 0) return;
        var u = _sim.Units;
        var defs = _sim.Content.Units;
        Span<bool> hears = stackalloc bool[Simulation.WildPlayer];
        foreach (int id in drumming)
        {
            int d = u.IndexOf(id);
            if (d < 0 || defs[u.Def[d]].Drums is not { } drums) continue;
            int owner = u.Owner[d], dx = u.X[d].FloorToInt(), dz = u.Z[d].FloorToInt();
            long r2 = (long)drums.Heard * drums.Heard;
            hears.Clear();
            for (int i = 0; i < u.Count; i++)
            {
                int p = u.Owner[i];
                if ((uint)p >= (uint)hears.Length || p == owner || hears[p]) continue;
                long ex = u.X[i].FloorToInt() - dx, ez = u.Z[i].FloorToInt() - dz;
                if (ex * ex + ez * ez <= r2) hears[p] = true;
            }
            foreach (var b in _sim.Buildings.All)
            {
                int p = b.Owner;
                if ((uint)p >= (uint)hears.Length || p == owner || hears[p] || b.Planned) continue;
                long ex = Math.Clamp(dx, b.X, b.MaxX) - dx, ez = Math.Clamp(dz, b.Z, b.MaxZ) - dz;
                if (ex * ex + ez * ez <= r2) hears[p] = true;
            }
            for (int p = 0; p < hears.Length; p++)
                if (hears[p]) (_drums[p] ??= new List<int>()).Add(id);
        }
    }

    /// <summary>True if the player hears war drummer <paramref name="id"/> (a unit id) drumming at the last look.</summary>
    public bool HearsDrum(int player, int id) =>
        _sim.Fog && (uint)player < (uint)_drums.Length && _drums[player] is { } l && l.Contains(id);

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
        var marks = _gridMarks[player] ??= new Stretches(_sizeX, _sizeZ);
        var half = Disc(r2);
        int r = half.Length - 1;
        long seen = 0;
        bool screened = ScreensNear(x0 - r, z0 - r, x1 + r, z1 + r);
        for (int z = Math.Max(0, z0 - r); z <= Math.Min(_sizeZ - 1, z1 + r); z++)
        {
            int dz = z < z0 ? z0 - z : z > z1 ? z - z1 : 0;
            int h = half[dz];
            int from = Math.Max(0, x0 - h), to = Math.Min(_sizeX - 1, x1 + h);
            var row = g.AsSpan(z * _sizeX + from, to - from + 1);
            marks.Mark(z, from, to);
            for (int k = 0; k < row.Length; k++)
            {
                if (litOnly && !lights.Lit(from + k, z)) continue;
                if (screened && Screened(from + k, z)) continue;
                if (row[k] == (byte)Sight.Unseen) seen++;
                row[k] = (byte)Sight.Visible;
            }
        }
        _seen[player] += seen;
    }

    /// <summary>
    /// How many columns of an orchard's grown trees one sees into (and through): further in, and behind it, is hidden.
    /// </summary>
    public const int ScreenDepth = 2;

    // The grown orchards (see Economy.EconomySystem.Screens), and the one looking while Stamp runs: its column and level
    // (null for a town's scouts, who ride all round and see past them).
    private readonly List<(int X0, int Z0, int X1, int Z1, int Top)> _screens = new();
    private (int X, int Z, int Y)? _viewer;
    private readonly List<int> _near = new();

    /// <summary>
    /// Notes the orchards that screen the viewer's sight within the rectangle (x0, z0)-(x1, z1): those it does not stand
    /// over (its eyes above their trees). True if there are any.
    /// </summary>
    private bool ScreensNear(int x0, int z0, int x1, int z1)
    {
        _near.Clear();
        if (_viewer is not { } v) return false;
        for (int k = 0; k < _screens.Count; k++)
        {
            var o = _screens[k];
            if (o.X1 < x0 || o.X0 > x1 || o.Z1 < z0 || o.Z0 > z1 || v.Y + 1 > o.Top) continue;
            _near.Add(k);
        }
        return _near.Count > 0;
    }

    /// <summary>
    /// True if the viewer's line of sight to column (x, z) runs through more than <see cref="ScreenDepth"/> columns of
    /// one of the orchards near it (see <see cref="ScreensNear"/>), the column itself counted, the viewer's own not.
    /// Walks the line in half-column steps, in whole numbers.
    /// </summary>
    private bool Screened(int x, int z)
    {
        var v = _viewer!.Value;
        int dx = x - v.X, dz = z - v.Z;
        int n = 2 * Math.Max(Math.Abs(dx), Math.Abs(dz));
        if (n <= 2 * ScreenDepth) return false;
        foreach (int k in _near)
        {
            var o = _screens[k];
            // Quick: the line's box misses the orchard.
            if (Math.Max(x, v.X) < o.X0 || Math.Min(x, v.X) > o.X1 || Math.Max(z, v.Z) < o.Z0 || Math.Min(z, v.Z) > o.Z1) continue;
            int inside = 0, lastX = v.X, lastZ = v.Z;
            for (int s = 1; s <= n; s++)
            {
                // The column the line is in at step s, rounded to the nearest (the half rounds away from the viewer).
                int cx = v.X + (2 * dx * s + Math.Sign(dx) * n) / (2 * n);
                int cz = v.Z + (2 * dz * s + Math.Sign(dz) * n) / (2 * n);
                if (cx == lastX && cz == lastZ) continue;
                (lastX, lastZ) = (cx, cz);
                if (cx >= o.X0 && cx <= o.X1 && cz >= o.Z0 && cz <= o.Z1 && ++inside > ScreenDepth) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Lets the player hear units underground in the rectangle of columns (x0, z0)-(x1, z1) and every column within
    /// the square root of <paramref name="r2"/> of it.
    /// </summary>
    private void Hear(int player, int x0, int z0, int x1, int z1, int r2) =>
        Fill(_heard[player] ??= new byte[_sizeX * _sizeZ], _heardMarks[player] ??= new Stretches(_sizeX, _sizeZ), x0, z0, x1, z1, r2);

    /// <summary>Marks the rectangle of columns (x0, z0)-(x1, z1) in grid g, and every column within the square root of r2 of it.</summary>
    private void Fill(byte[] g, Stretches marks, int x0, int z0, int x1, int z1, int r2)
    {
        var half = Disc(r2);
        int r = half.Length - 1;
        for (int z = Math.Max(0, z0 - r); z <= Math.Min(_sizeZ - 1, z1 + r); z++)
        {
            int dz = z < z0 ? z0 - z : z > z1 ? z - z1 : 0;
            int h = half[dz];
            int from = Math.Max(0, x0 - h), to = Math.Min(_sizeX - 1, x1 + h);
            g.AsSpan(z * _sizeX + from, to - from + 1).Fill(1);
            marks.Mark(z, from, to);
        }
    }

    /// <summary>
    /// The stretches of a per-column grid written since it was last put back: for each row, which runs of
    /// <see cref="Run"/> columns. Putting back only those leaves the grid as going over all of it would.
    /// </summary>
    private sealed class Stretches
    {
        private const int Shift = 6, Run = 1 << Shift;
        private readonly int _sizeX, _words;
        private readonly ulong[] _bits;
        private readonly bool[] _rows;
        private readonly List<int> _marked = new();

        public Stretches(int sizeX, int sizeZ)
        {
            _sizeX = sizeX;
            int runs = (sizeX + Run - 1) >> Shift;
            _words = (runs + 63) >> 6;
            _bits = new ulong[_words * sizeZ];
            _rows = new bool[sizeZ];
        }

        /// <summary>Columns from..to of row z were written.</summary>
        public void Mark(int z, int from, int to)
        {
            if (from > to) return;
            if (!_rows[z])
            {
                _rows[z] = true;
                _marked.Add(z);
            }
            int baseWord = z * _words;
            for (int k = from >> Shift; k <= to >> Shift; k++) _bits[baseWord + (k >> 6)] |= 1UL << (k & 63);
        }

        /// <summary>Every visible column written fades to seen.</summary>
        public void Fade(byte[] g) => PutBack(g, fade: true);

        /// <summary>Every column written is cleared.</summary>
        public void Clear(byte[] g) => PutBack(g, fade: false);

        private void PutBack(byte[] g, bool fade)
        {
            foreach (int z in _marked)
            {
                _rows[z] = false;
                int baseWord = z * _words;
                for (int w = 0; w < _words; w++)
                {
                    ulong bits = _bits[baseWord + w];
                    if (bits == 0) continue;
                    _bits[baseWord + w] = 0;
                    while (bits != 0)
                    {
                        int k = (w << 6) + System.Numerics.BitOperations.TrailingZeroCount(bits);
                        bits &= bits - 1;
                        int from = k << Shift, len = Math.Min(Run, _sizeX - from);
                        var span = g.AsSpan(z * _sizeX + from, len);
                        if (fade) span.Replace((byte)Sight.Visible, (byte)Sight.Seen);
                        else span.Clear();
                    }
                }
            }
            _marked.Clear();
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
