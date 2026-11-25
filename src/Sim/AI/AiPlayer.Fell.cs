using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;

namespace Rts.Sim.AI;

// Aimed felling (issue #146), at the smart levels (Easy and up). Once it has Better tools, a unit of its that can fell
// (a Settler militia, an Orc or Deepfolk worker) and stands near a tree brings it down onto rivals it sees: the tree
// and the way it falls are picked so that at least FellAtLeast rival units (a rival building counting as one) stand
// where it lands, and nothing of its own nor of anyone it is not fighting (a city-state, a player it has a truce
// with). One feller at a time; its army leaves the feller to it until the tree is down or the job is given up.
public sealed partial class AiPlayer
{
    /// <summary>Furthest (columns, each way) from a feller that a tree to fell onto rivals is looked for.</summary>
    public const int FellLook = 8;

    /// <summary>Fewest rival units (a rival building counting as one) a tree must come down on to be worth felling.</summary>
    public const int FellAtLeast = 2;

    // The unit felling a tree for it (by id), left out of its army's orders meanwhile; 0 if none.
    private int _feller;

    /// <summary>The unit felling a tree onto rivals for it now (by id), or 0.</summary>
    public int Feller => _feller;

    /// <summary>Trees it has ordered felled onto rivals (for the tests and the match runner's trace).</summary>
    public int Fellings { get; private set; }

    /// <summary>Picks a tree to bring down onto rivals it sees, and sends the nearest unit that can fell it.</summary>
    private void Fell(Simulation sim, Action<Command> issue)
    {
        var econ = sim.Economy;
        var u = sim.Units;
        if (_feller != 0)
        {
            if (u.Exists(_feller) && u.Job[u.IndexOf(_feller)] == UnitJob.Fell) return; // still at it
            _feller = 0;
        }
        if (econ.FellCrush(Player) <= 0) return;

        // The rival units it sees, by column (several may stand in one).
        var rivals = new SortedDictionary<long, int>();
        int sx = sim.World.SizeX;
        for (int i = 0; i < u.Count; i++)
        {
            int o = u.Owner[i];
            if (o == Player || o >= Simulation.WildPlayer || !Rival(o) || u.Hp[i] <= 0 || u.Aboard[i] != 0) continue;
            if (!sim.Vision.SeesUnit(Player, i)) continue;
            long k = (long)u.Z[i].FloorToInt() * sx + u.X[i].FloorToInt();
            rivals[k] = rivals.TryGetValue(k, out int n) ? n + 1 : 1;
        }
        if (rivals.Count == 0) return;

        int bestFeller = -1, bestX = 0, bestZ = 0, bestDir = 0;
        long bestScore = long.MinValue;
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Owner[i] != Player || u.Rout[i] > 0 || u.Aboard[i] != 0 || u.Job[i] == UnitJob.Build) continue;
            if (econ.FellTicksOf(i) <= 0) continue;
            int ux = u.X[i].FloorToInt(), uz = u.Z[i].FloorToInt();
            if (!RivalsNear(rivals, sx, ux, uz, FellLook + 12)) continue;
            for (int tz = uz - FellLook; tz <= uz + FellLook; tz++)
            for (int tx = ux - FellLook; tx <= ux + FellLook; tx++)
            {
                if (!StumpAt(sim.World, tx, tz, out int baseY, out int topY)) continue;
                for (int dir = 0; dir < 4; dir++)
                {
                    var (dx, dz) = EconomySystem.FellDirection(dir);
                    int hits = FellHits(sim, rivals, tx, baseY, topY, tz, dx, dz);
                    if (hits < FellAtLeast) continue;
                    // Most hits; then the feller with the least way round to the back of the tree.
                    long bx = tx - 2 * dx - ux, bz = tz - 2 * dz - uz;
                    long score = hits * 100_000L - (bx * bx + bz * bz);
                    if (score <= bestScore) continue;
                    (bestScore, bestFeller, bestX, bestZ, bestDir) = (score, i, tx, tz, dir);
                }
            }
        }
        if (bestFeller < 0) return;
        var (fx, fz) = EconomySystem.FellDirection(bestDir);
        _feller = u.Id[bestFeller];
        Fellings++;
        issue(new FellCommand(Player, new[] { _feller }, bestX, bestZ, fx, fz));
        Note(sim, $"fells a tree onto rivals at {bestX}, {bestZ}");
    }

    /// <summary>True if any rival unit stands within r columns (each way) of (x, z).</summary>
    private static bool RivalsNear(SortedDictionary<long, int> rivals, int sx, int x, int z, int r)
    {
        foreach (long k in rivals.Keys)
        {
            int rx = (int)(k % sx), rz = (int)(k / sx);
            if (Math.Abs(rx - x) <= r && Math.Abs(rz - z) <= r) return true;
        }
        return false;
    }

    /// <summary>
    /// True if (x, z) is a tree's stump column (a big tree's first column only), with the level its trunk starts at
    /// and its top. Not the dusk tree, nor anything else that is not a trunk.
    /// </summary>
    private static bool StumpAt(VoxelWorld w, int x, int z, out int baseY, out int topY)
    {
        baseY = topY = -1;
        if (!w.InBounds(x, 0, z)) return false;
        var (x0, z0, s) = Trees.Footprint(w, x, z);
        if (s > 1 && (x0 != x || z0 != z)) return false;
        for (int y = w.SizeY - 1; y >= 1; y--)
        {
            if (!BlockInfo.IsTrunk(w.Get(x, y, z))) continue;
            var (b, t) = Harvest.Span(w, x, y, z);
            if (b < 1 || BlockInfo.IsTrunk(w.Get(x, b - 1, z))) return false;
            (baseY, topY) = (b, t);
            return true;
        }
        return false;
    }

    /// <summary>
    /// The rival units (a rival building counting as one) the tree would come down on felled that way; -1 if it would
    /// come down on anything of its own, or of anyone it is not fighting.
    /// </summary>
    private int FellHits(Simulation sim, SortedDictionary<long, int> rivals, int x, int baseY, int topY, int z, int dx, int dz)
    {
        var cols = sim.Physics.FallColumnsOf(x, baseY, topY, z, dx, dz);
        int sx = sim.World.SizeX, hits = 0;
        foreach (var (cx, cz) in cols)
            if (rivals.TryGetValue((long)cz * sx + cx, out int n)) hits += n;
        if (hits == 0) return 0;
        // Nothing of its own or of a side it spares, units or buildings, under it.
        var u = sim.Units;
        for (int i = 0; i < u.Count; i++)
        {
            int o = u.Owner[i];
            if (o == Simulation.WildPlayer || u.Hp[i] <= 0 || u.Aboard[i] != 0) continue;
            if (o != Player && o < Simulation.WildPlayer && Rival(o)) continue;
            if (Math.Abs(u.Y[i] - baseY) > PhysicsSystem.FallHeight) continue;
            if (cols.Contains((u.X[i].FloorToInt(), u.Z[i].FloorToInt()))) return -1;
        }
        var seen = new List<int>();
        foreach (var (cx, cz) in cols)
        {
            var b = sim.Buildings.AtColumn(cx, cz);
            if (b == null || seen.Contains(b.Id) || b.Y > baseY + PhysicsSystem.FallHeight) continue;
            seen.Add(b.Id);
            if (b.Owner == Player || b.Owner >= Simulation.WildPlayer || !Rival(b.Owner)) return -1;
            if (sim.Vision.Sees(Player, cx, cz)) hits++;
        }
        return hits;
    }
}
