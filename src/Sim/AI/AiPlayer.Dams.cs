using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.AI;

// Dams at the smart levels (Easy and up). A dam holds water back above lower ground, so letting it through is a
// trap for whoever stands in that ground: sappers in their tunnels, an army in a dug pit. Once a second it weighs
// what the water would do (see Floods): it opens the sluice of a finished dam of its own when the water through it
// would rise over the heads of rival units it sees or hears, and none of its own; it shuts it again
// SluiceOpenTicks later, so the dam holds again (the water through stays). A rival's finished dam within
// BreachReach of its town hall that it can see is breached the same way: when digging out one of the BreachTries
// earth columns two or more deep nearest its sluice would flood rival units and none of its own, it sends up to
// BreachParty workers to dig that column out down to the bed, and leaves them to it for at most BreachGiveUpTicks. It does not build dams itself: on natural ground
// there is no dry low ground beside water for one to hold back.
public sealed partial class AiPlayer
{
    /// <summary>How often (ticks) it weighs its dams and its rivals'.</summary>
    public const int DamTicks = Tps;
    /// <summary>It shuts a sluice it opened this many ticks later.</summary>
    public const int SluiceOpenTicks = 30 * Tps;
    /// <summary>It breaches rivals' dams no further than this (columns) from its town hall's middle.</summary>
    public const int BreachReach = 48;
    /// <summary>Workers it sends to breach a dam, and how long (ticks) it leaves them to it.</summary>
    public const int BreachParty = 2, BreachGiveUpTicks = 90 * Tps;
    /// <summary>The deep earth columns of a rival's dam it weighs breaching: the ones nearest the sluice.</summary>
    public const int BreachTries = 2;
    /// <summary>How far (columns, either way) from the dam it follows the water; and at most this many blocks.</summary>
    public const int FloodReach = 24, FloodMostBlocks = 8192;

    // The sluice it opened, and when; the breach it is digging: the dam, its column, the dig area's party, since when.
    private int _openedDam;
    private long _openedAt;
    private int _breachDam;
    private (int X, int Z) _breachAt;
    private readonly List<int> _breachers = new();
    private long _breachSince;

    /// <summary>The rival dam it is breaching (0 if none).</summary>
    public int Breaching => _breachDam;
    /// <summary>Dams it flooded rivals with: sluices it opened and breaches it started.</summary>
    public int SluicesOpened { get; private set; }
    public int BreachesStarted { get; private set; }

    /// <summary>The smart levels: its sluices, and breaching rivals' dams (see the top of this file).</summary>
    private void Dams(Simulation sim, Building hall, Action<Command> issue)
    {
        if (!Smart || (sim.Tick + Player * 13) % DamTicks != 0) return;
        var dams = sim.Economy.Dams;
        if (dams.Count == 0) return;

        // Its own sluice, opened lately: shut once it has let enough through.
        if (_openedDam != 0)
        {
            var open = sim.Economy.DamById(_openedDam);
            if (open == null || !open.Open) _openedDam = 0;
            else if (sim.Tick - _openedAt >= SluiceOpenTicks) issue(new SluiceCommand(Player, open.Id, false)); // refused while anyone is in the gap
        }
        foreach (var d in dams)
        {
            if (d.Owner != Player || !d.Done || d.Open || !d.SluiceColumn(out var s)) continue;
            if (!Floods(sim, d, s, out int rivals)) continue;
            issue(new SluiceCommand(Player, d.Id, true));
            _openedDam = d.Id;
            _openedAt = sim.Tick;
            SluicesOpened++;
            Note(sim, $"opens its sluice at {s.X},{s.Z} on {rivals} enemies below");
            return;
        }
        Breach(sim, hall, issue);
    }

    /// <summary>A breach of a rival's dam: keeps one going, or starts one (see the top of this file).</summary>
    private void Breach(Simulation sim, Building hall, Action<Command> issue)
    {
        var u = sim.Units;
        if (_breachDam != 0)
        {
            _breachers.RemoveAll(id => !u.Exists(id));
            var d = sim.Economy.DamById(_breachDam);
            if (d == null || _breachers.Count == 0 || sim.Tick - _breachSince >= BreachGiveUpTicks || Breached(sim, d))
            {
                if (_breachers.Count > 0 && d != null && !Breached(sim, d)) Free(_breachers.ToArray(), issue);
                _breachDam = 0;
                _breachers.Clear();
            }
            return;
        }
        var (hx, hz) = Middle(hall);
        foreach (var d in sim.Economy.Dams)
        {
            if (!Rival(d.Owner) || !d.Done) continue;
            int tried = 0;
            for (int step = 0; step < d.Columns.Count && tried < BreachTries; step++)
            {
                // From the sluice outwards (the boards are not dug), the deep columns only.
                int k = d.Sluice >= 0 ? d.Sluice + ((step & 1) == 0 ? step / 2 + 1 : -(step / 2 + 1)) : step;
                if ((uint)k >= (uint)d.Columns.Count || k == d.Sluice) continue;
                var c = d.Columns[k];
                if (c.Blocks < 2 || Breached(sim.World, c)) continue;
                long dx = c.X - hx, dz = c.Z - hz;
                if (dx * dx + dz * dz > (long)BreachReach * BreachReach || !sim.Vision.Sees(Player, c.X, c.Z)) continue;
                tried++;
                if (!Floods(sim, d, c, out int rivals)) continue;
                if (!PickBreachers(sim, c.X, c.Z)) return;
                // A 2 by 2 area with the column in the row dug to the floor (the other row is the stair).
                var dig = new DigAreaCommand(Player, _breachers.ToArray(), c.X, c.Z - 1, c.X + 1, c.Z, c.Bed);
                issue(dig);
                _breachDam = d.Id;
                _breachAt = (c.X, c.Z);
                _breachSince = sim.Tick;
                BreachesStarted++;
                Note(sim, $"breaches the enemy dam at {c.X},{c.Z} on {rivals} enemies below");
                return;
            }
        }
    }

    /// <summary>
    /// Picks the <see cref="BreachParty"/> workers nearest column (x, z) into the breach party (not Hard's
    /// masons). False if it has none.
    /// </summary>
    private bool PickBreachers(Simulation sim, int x, int z)
    {
        var u = sim.Units;
        _breachers.Clear();
        Span<long> best = stackalloc long[BreachParty];
        Span<int> ids = stackalloc int[BreachParty];
        int n = 0;
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Owner[i] != Player || sim.Content.Units[u.Def[i]].Role != UnitRole.Worker) continue;
            if (_masons.Contains(u.Id[i])) continue;
            long dx = u.X[i].FloorToInt() - x, dz = u.Z[i].FloorToInt() - z, d2 = dx * dx + dz * dz;
            int at = n < BreachParty ? n++ : BreachParty;
            if (at == BreachParty && d2 >= best[BreachParty - 1]) continue;
            if (at == BreachParty) at = BreachParty - 1;
            while (at > 0 && best[at - 1] > d2)
            {
                best[at] = best[at - 1];
                ids[at] = ids[at - 1];
                at--;
            }
            best[at] = d2;
            ids[at] = u.Id[i];
        }
        for (int k = 0; k < n; k++) _breachers.Add(ids[k]);
        return n > 0;
    }

    /// <summary>True once the column it is breaching no longer holds the water: dug below the water beside it.</summary>
    private bool Breached(Simulation sim, Dam d)
    {
        foreach (var c in d.Columns)
            if (c.X == _breachAt.X && c.Z == _breachAt.Z) return Breached(sim.World, c);
        return true;
    }

    /// <summary>True if the column's lowest block is open (dug out, or water through it).</summary>
    private static bool Breached(VoxelWorld w, DamSpan c) => Ramp.Open(w, c.X, c.Bed, c.Z);

    /// <summary>
    /// True if letting the water through column <paramref name="c"/> of the dam (its blocks from the bed up to
    /// under its top taken out) would rise over the heads of rival units it sees or hears, and of none of its own:
    /// the water beside the column (its level the highest of the water next to it) fills the open ground it reaches
    /// from the gap, below that level, within <see cref="FloodReach"/> of the column.
    /// </summary>
    private bool Floods(Simulation sim, Dam dam, DamSpan c, out int rivals)
    {
        rivals = 0;
        var w = sim.World;
        int level = -1; // the water's top: blocks below it fill
        foreach (var (sx, sz) in SideSteps)
        {
            int nx = c.X + sx, nz = c.Z + sz;
            if (!w.InBounds(nx, 1, nz) || dam.Covers(nx, nz)) continue;
            for (int y = c.Top - 1; y >= c.Bed; y--)
                if (w.Get(nx, y, nz) == BlockType.Water)
                {
                    level = Math.Max(level, y + 1);
                    break;
                }
        }
        if (level <= c.Bed + 1) return false; // nothing to hold, or only wading water

        // The ground the water reaches from the gap, block by block, below its level.
        _floodSeen.Clear();
        _floodQueue.Clear();
        for (int y = c.Bed; y < Math.Min(level, c.Top - 1); y++) Reach(c.X, y, c.Z);
        while (_floodQueue.Count > 0 && _floodSeen.Count < FloodMostBlocks)
        {
            var (x, y, z) = _floodQueue.Dequeue();
            foreach (var (sx, sz) in SideSteps) Fill(w, c, dam, x + sx, y, z + sz, level);
            Fill(w, c, dam, x, y - 1, z, level);
            Fill(w, c, dam, x, y + 1, z, level);
        }

        // Who stands in it with the water over their heads.
        var u = sim.Units;
        bool own = false;
        for (int i = 0; i < u.Count; i++)
        {
            int feet = u.Y[i];
            if (feet + 1 >= level) continue;
            if (!_floodSeen.Contains(Key(u.X[i].FloorToInt(), feet, u.Z[i].FloorToInt()))) continue;
            if (u.Owner[i] == Player) own = true;
            else if (Rival(u.Owner[i]) && !u.Subdued[i] && sim.Vision.SeesUnit(Player, i)) rivals++;
        }
        return rivals > 0 && !own;
    }

    private void Fill(VoxelWorld w, DamSpan c, Dam dam, int x, int y, int z, int level)
    {
        if (y < 1 || y >= level || !w.InBounds(x, y, z)) return;
        if (Math.Abs(x - c.X) > FloodReach || Math.Abs(z - c.Z) > FloodReach) return;
        if (w.Get(x, y, z) != BlockType.Air || dam.Covers(x, z) && (x != c.X || z != c.Z)) return;
        Reach(x, y, z);
    }

    private void Reach(int x, int y, int z)
    {
        if (_floodSeen.Add(Key(x, y, z))) _floodQueue.Enqueue((x, y, z));
    }

    private static long Key(int x, int y, int z) => ((long)x << 40) | ((long)y << 20) | (uint)z;

    private static readonly (int X, int Z)[] SideSteps = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    // Used only to ask whether a block was reached (never iterated), so its order does not matter.
    private readonly HashSet<long> _floodSeen = new();
    private readonly Queue<(int X, int Y, int Z)> _floodQueue = new();
}
