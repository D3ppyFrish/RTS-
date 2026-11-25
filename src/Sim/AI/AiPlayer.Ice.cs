using System;
using System.Collections.Generic;
using Rts.Sim.Units;

namespace Rts.Sim.AI;

// Ice bridges for its waves, from Normal up. A wave out whose way by land to the enemy (the town hall it knows, or
// where it guesses one stands) is cut off, or longer than the way over the ice by at least IceSaves steps, and that has
// a mage with Ice Bridge ready and the mana for it, crosses the water on ice rather than going round or by ship: it
// looks along the banks it can walk to (within IceSearch steps) for a straight line, across x or along z, over the
// water to ground it can walk on to the enemy from, short enough to freeze (SpellSystem.IceLength) and with room for
// ice on every column, and takes the one that makes the whole way shortest. The wave gathers on the near bank, its
// mage freezes the water (one mage, two for a wave of more than IceBigWave), and the wave walks over to the far bank
// before the ice thaws; then it goes on for the enemy from there. It looks again a few times on the way (IceChecks),
// as for ships; a wave that cannot cross on ice may still be ferried.
public sealed partial class AiPlayer
{
    /// <summary>Steps along the near bank (from the wave) within which it looks for a place to freeze a crossing.</summary>
    public const int IceSearch = 48;
    /// <summary>Steps the way over the ice must save on the way by land (when there is one) to be worth the mana.</summary>
    public const int IceSaves = 24;
    /// <summary>A wave bigger than this has two mages freeze the crossing, for a wider bridge.</summary>
    public const int IceBigWave = 12;
    /// <summary>Times it looks for an ice crossing in one wave, and the ticks between looks.</summary>
    public const int IceChecks = 3, IceRecheckTicks = 30 * Tps;
    /// <summary>Ticks it waits for the wave to gather on the near bank before the mage freezes it anyway.</summary>
    public const int IceGatherTicks = 40 * Tps;
    /// <summary>Columns from the bank within which a soldier counts as gathered there, or across on the far bank.</summary>
    public const int IceGathered = 6;
    /// <summary>Ticks it waits for the ice after sending the mage, before it gives the crossing up.</summary>
    public const int IceCastTicks = 20 * Tps;

    private sealed class IceCrossing
    {
        public int AX, AZ, BX, BZ;
        public int DX, DZ; // the way over, one step
        public int GX, GZ, FX, FZ; // where the wave gathers on the near bank, and where it walks to on the far one
        public int Stage; // 0 gathering, 1 casting, 2 crossing
        public long Since, CastAt;
        public int Casters;
    }

    private IceCrossing? _ice;
    private int _iceChecks;
    private long _iceNext;
    private int[]? _iceFromWave, _iceFromGoal;
    private readonly List<int> _iceQueue = new();
    private readonly List<(int X, int Z, int Y, bool Room)> _icePlan = new();

    /// <summary>True while a wave of its own is crossing the water on ice (gathering, freezing or walking over).</summary>
    public bool IceCrossingWave => _ice != null;
    /// <summary>The near and far banks of the ice crossing under way, if any.</summary>
    public (int AX, int AZ, int BX, int BZ)? IceBanks => _ice is { } c ? (c.AX, c.AZ, c.BX, c.BZ) : null;
    /// <summary>Ice bridges its mages froze for its waves.</summary>
    public int IceBridgesFrozen { get; private set; }

    private void ResetIce()
    {
        _ice = null;
        _iceChecks = 0;
        _iceNext = 0;
    }

    /// <summary>
    /// Normal and up, a wave out: true while it is crossing on ice (the wave's other orders wait), having planned a
    /// crossing if the way by land is cut off or long (see the top of this file).
    /// </summary>
    private bool IceCross(Simulation sim, Action<Command> issue)
    {
        if (!Skilled || _waveLair != 0) return false;
        if (_ice == null && !StartIce(sim, issue)) return false;
        var c = _ice!;
        var u = sim.Units;
        var spells = sim.Spells;
        switch (c.Stage)
        {
            case 0:
            {
                // Gathering on the near bank: most of the wave there, or long enough.
                if (Gathered(sim, c.GX, c.GZ) * 2 <= _wave.Count && sim.Tick - c.Since < IceGatherTicks) return true;
                var mages = IceMages(sim);
                int cost = SpellSystem.ManaCost(Spell.IceBridge);
                int casters = Math.Min(Math.Min(c.Casters, mages.Count), sim.Economy.Player(Player).Mana / cost);
                if (casters == 0)
                {
                    if (sim.Tick - c.Since < IceGatherTicks + IceCastTicks) return true; // its mage may be ready soon
                    return GiveUpIce(sim, "no mage ready to freeze the water");
                }
                mages.Sort((a, b) =>
                {
                    long da = Dist2(u.X[a].FloorToInt(), u.Z[a].FloorToInt(), c.AX, c.AZ);
                    long db = Dist2(u.X[b].FloorToInt(), u.Z[b].FloorToInt(), c.AX, c.AZ);
                    return da != db ? da.CompareTo(db) : u.Id[a].CompareTo(u.Id[b]);
                });
                var group = new int[casters];
                for (int n = 0; n < casters; n++) group[n] = u.Id[mages[n]];
                issue(new IceBridgeCommand(Player, group, c.AX, c.AZ, c.BX, c.BZ));
                c.Stage = 1;
                c.Since = c.CastAt = sim.Tick;
                Note(sim, $"has {casters} mage{(casters == 1 ? "" : "s")} freeze the water from {c.AX},{c.AZ} to {c.BX},{c.BZ}");
                return true;
            }
            case 1:
            {
                if (OurIce(sim, c) is not { })
                {
                    if (sim.Tick - c.Since < IceCastTicks) return true;
                    return GiveUpIce(sim, "the water did not freeze");
                }
                IceBridgesFrozen++;
                issue(new MoveCommand(Player, _wave.ToArray(), c.FX, c.FZ));
                c.Stage = 2;
                c.Since = sim.Tick;
                Note(sim, $"walks its wave of {_wave.Count} over the ice");
                return true;
            }
            default:
            {
                // Crossing: done once most are over, or the ice is gone.
                var ice = OurIce(sim, c);
                bool over = Over(sim, c) * 2 > _wave.Count;
                if (!over && ice != null && !spells.Cracking(ice))
                {
                    // Stragglers that stopped on the near side set out again.
                    if (sim.Tick % (5 * ArmyTicks) < ArmyTicks)
                    {
                        var again = Unordered(sim, _wave, c.FX, c.FZ);
                        if (again.Count > 0) issue(new MoveCommand(Player, again.ToArray(), c.FX, c.FZ));
                    }
                    return true;
                }
                if (ice != null && spells.Cracking(ice))
                {
                    // Nobody steps onto cracking ice: those not yet on it wait on the near bank.
                    var wait = new List<int>();
                    foreach (int id in _wave)
                    {
                        int i = u.IndexOf(id);
                        if (i < 0) continue;
                        int x = u.X[i].FloorToInt(), z = u.Z[i].FloorToInt();
                        if ((x - c.AX) * c.DX + (z - c.AZ) * c.DZ <= 0 && !OnIce(sim, x, z)) wait.Add(id);
                    }
                    if (wait.Count > 0) issue(new MoveCommand(Player, wait.ToArray(), c.GX, c.GZ));
                }
                Note(sim, $"has its wave across the water on the ice: {Over(sim, c)} over");
                _ice = null;
                // The wave sets out afresh from where it is.
                _waveBest = int.MaxValue;
                _waveCheck = sim.Tick;
                return false;
            }
        }
    }

    private bool GiveUpIce(Simulation sim, string why)
    {
        Note(sim, $"gives up crossing on ice: {why}");
        _ice = null;
        _waveBest = int.MaxValue;
        _waveCheck = sim.Tick;
        return false;
    }

    /// <summary>The ice bridge of its own frozen for the crossing, if it stands.</summary>
    private IceBridge? OurIce(Simulation sim, IceCrossing c)
    {
        foreach (var ice in sim.Spells.IceBridges)
            if (ice.Owner == Player && ice.Frozen >= c.CastAt) return ice;
        return null;
    }

    private static bool OnIce(Simulation sim, int x, int z)
    {
        int g = Walk.GroundLevel(sim.World, x, z);
        return g > 0 && sim.World.Get(x, g - 1, z) == BlockType.Ice;
    }

    /// <summary>Soldiers of the wave on the far bank: past its edge, off the ice.</summary>
    private int Over(Simulation sim, IceCrossing c)
    {
        var u = sim.Units;
        int n = 0;
        foreach (int id in _wave)
        {
            int i = u.IndexOf(id);
            if (i < 0) continue;
            int x = u.X[i].FloorToInt(), z = u.Z[i].FloorToInt();
            if ((x - c.BX) * c.DX + (z - c.BZ) * c.DZ >= 0 && !OnIce(sim, x, z)) n++;
        }
        return n;
    }

    /// <summary>Soldiers of the wave within <see cref="IceGathered"/> columns of (x, z).</summary>
    private int Gathered(Simulation sim, int x, int z)
    {
        var u = sim.Units;
        int n = 0;
        foreach (int id in _wave)
        {
            int i = u.IndexOf(id);
            if (i < 0) continue;
            if (Dist2(u.X[i].FloorToInt(), u.Z[i].FloorToInt(), x, z) <= (long)IceGathered * IceGathered) n++;
        }
        return n;
    }

    /// <summary>Unit indices of the wave's mages that can freeze an ice bridge now.</summary>
    private List<int> IceMages(Simulation sim)
    {
        var u = sim.Units;
        var mages = new List<int>();
        foreach (int id in _wave)
        {
            int i = u.IndexOf(id);
            if (i < 0 || u.Aboard[i] != 0 || sim.Spells.Casting(id) || !sim.Spells.CanCast(i, Spell.IceBridge)) continue;
            mages.Add(i);
        }
        return mages;
    }

    private bool StartIce(Simulation sim, Action<Command> issue)
    {
        if (_iceChecks >= IceChecks || sim.Tick < _iceNext) return false;
        if (sim.Economy.Player(Player).Mana < SpellSystem.ManaCost(Spell.IceBridge)) return false;
        var mages = IceMages(sim);
        if (mages.Count == 0) return false;
        var goal = Target(sim, _waveBuilding) is { } foe ? (foe.AX, foe.AZ) : _waveGuess;
        if (goal is not var (gx, gz)) return false;
        var (cx, cz) = Centroid(sim, _wave);
        if (Dist2(cx, cz, gx, gz) < (long)FerryLeast * FerryLeast) return false;
        _iceNext = sim.Tick + IceRecheckTicks;
        _iceChecks++;
        int casters = Math.Min(_wave.Count > IceBigWave ? 2 : 1, mages.Count);
        if (PlanIceCrossing(sim, cx, cz, gx, gz, casters) is not var (ax, az, bx, bz)) return false;
        int dx = Math.Sign(bx - ax), dz = Math.Sign(bz - az);
        var (fx, fz) = Back(sim, bx, bz, dx, dz, IceGathered);
        var (sx, sz) = Back(sim, ax, az, -dx, -dz, IceGathered / 2);
        _ice = new IceCrossing
        {
            AX = ax, AZ = az, BX = bx, BZ = bz, DX = dx, DZ = dz, GX = sx, GZ = sz, FX = fx, FZ = fz,
            Since = sim.Tick, Casters = casters,
        };
        issue(new MoveCommand(Player, _wave.ToArray(), sx, sz));
        Note(sim, $"gathers its wave of {_wave.Count} at {ax},{az} to cross the water on ice to {bx},{bz}");
        return true;
    }

    /// <summary>
    /// The best straight crossing to freeze for a wave round (cx, cz) going to (gx, gz): from a bank it can walk to,
    /// over the water to ground from which it can walk to the goal (see the top of this file); null if there is
    /// none, or the way by land is short enough without one.
    /// </summary>
    private (int AX, int AZ, int BX, int BZ)? PlanIceCrossing(Simulation sim, int cx, int cz, int gx, int gz, int casters)
    {
        var g = sim.Walkable;
        var u = sim.Units;
        // The wave's walk-graph node: the soldier standing nearest its middle.
        int start = -1;
        long nearest = long.MaxValue;
        foreach (int id in _wave)
        {
            int i = u.IndexOf(id);
            if (i < 0 || u.Aboard[i] != 0) continue;
            int x = u.X[i].FloorToInt(), z = u.Z[i].FloorToInt();
            int node = g.Node(x, z, u.Y[i]);
            if (node < 0) continue;
            long d = Dist2(x, z, cx, cz);
            if (d < nearest) (nearest, start) = (d, node);
        }
        if (start < 0) return null;
        // The goal's: every node within two columns of it (one may be on a roof).
        var ends = new List<int>();
        for (int dz = -2; dz <= 2; dz++)
        for (int dx = -2; dx <= 2; dx++)
        {
            int x = gx + dx, z = gz + dz;
            if ((uint)x >= (uint)g.SizeX || (uint)z >= (uint)g.SizeZ) continue;
            for (int s = 0; s < WalkGraph.MaxLevels; s++)
            {
                int n = ((z * g.SizeX + x) << WalkGraph.LevelBits) + s;
                if (g.Level(n) < 0) break;
                ends.Add(n);
            }
        }
        if (ends.Count == 0) return null;

        _iceFromWave = Steps(sim, new List<int> { start }, _iceFromWave);
        _iceFromGoal = Steps(sim, ends, _iceFromGoal);
        var fromWave = _iceFromWave;
        var fromGoal = _iceFromGoal;
        int land = int.MaxValue;
        for (int dz = -2; dz <= 2; dz++)
        for (int dx = -2; dx <= 2; dx++)
        {
            int x = gx + dx, z = gz + dz;
            if ((uint)x >= (uint)g.SizeX || (uint)z >= (uint)g.SizeZ) continue;
            for (int s = 0; s < WalkGraph.MaxLevels; s++)
            {
                int n = ((z * g.SizeX + x) << WalkGraph.LevelBits) + s;
                if (g.Level(n) < 0) break;
                if (fromWave[n] >= 0) land = Math.Min(land, fromWave[n]);
            }
        }

        // Shortest whole way first; between ways as short (diagonal steps count as one), the straightest.
        int best = land == int.MaxValue ? int.MaxValue : land - IceSaves;
        long bestStraight = long.MaxValue;
        (int, int, int, int)? plan = null;
        for (int a = 0; a < fromWave.Length; a++)
        {
            int da = fromWave[a];
            if (da < 0 || da > IceSearch || da > best) continue;
            int ax = g.ColumnX(a), az = g.ColumnZ(a), ay = g.Level(a);
            for (int dir = 0; dir < 4; dir++)
            {
                int sx = dir == 0 ? 1 : dir == 1 ? -1 : 0, sz = dir == 2 ? 1 : dir == 3 ? -1 : 0;
                for (int k = 1; k <= SpellSystem.IceLength + 1; k++)
                {
                    int x = ax + sx * k, z = az + sz * k;
                    if ((uint)x >= (uint)g.SizeX || (uint)z >= (uint)g.SizeZ) break;
                    int b = g.NearestNode(x, z, ay);
                    if (b < 0 || Math.Abs(g.Level(b) - ay) > Walk.MaxClimb) continue; // water or a drop: on over it
                    if (k == 1) break; // still on the bank: the column next to the water looks from there
                    int db = fromGoal[b];
                    long straight = Dist2(ax, az, cx, cz) + Dist2(x, z, gx, gz);
                    if (db < 0 || da + k + db > best || (da + k + db == best && straight >= bestStraight)) break;
                    if (sim.Spells.PlanIce(ax, az, x, z, _icePlan, casters) < 0) break;
                    bool room = _icePlan.Count > 0;
                    foreach (var col in _icePlan)
                        if (!col.Room) { room = false; break; }
                    if (!room) break;
                    (best, bestStraight) = (da + k + db, straight);
                    plan = (ax, az, x, z);
                    break;
                }
            }
        }
        return plan;
    }

    /// <summary>
    /// The column up to <paramref name="far"/> steps on from (x, z) the way (dx, dz) over ground it can walk at about
    /// the same level, so a wave sent there stands back from the water rather than crowding the bank.
    /// </summary>
    private static (int X, int Z) Back(Simulation sim, int x, int z, int dx, int dz, int far)
    {
        var w = sim.World;
        int level = Walk.GroundLevel(w, x, z);
        for (int k = 1; k <= far; k++)
        {
            int nx = x + dx, nz = z + dz;
            if (!w.InBounds(nx, 1, nz)) break;
            int l = Walk.GroundLevel(w, nx, nz);
            if (l < 0 || Math.Abs(l - level) > Walk.MaxClimb) break;
            (x, z, level) = (nx, nz, l);
        }
        return (x, z);
    }

    /// <summary>Steps on the walk graph from the nearest of the nodes to every node it can walk to (-1 where it cannot).</summary>
    private int[] Steps(Simulation sim, List<int> from, int[]? into)
    {
        var g = sim.Walkable;
        if (into == null || into.Length != g.NodeCount) into = new int[g.NodeCount];
        Array.Fill(into, -1);
        _iceQueue.Clear();
        foreach (int n in from)
            if (into[n] < 0)
            {
                into[n] = 0;
                _iceQueue.Add(n);
            }
        for (int head = 0; head < _iceQueue.Count; head++)
        {
            int n = _iceQueue[head];
            for (int d = 0; d < WalkGraph.Dirs.Length; d++)
            {
                int m = g.Step(n, d);
                if (m < 0 || into[m] >= 0) continue;
                into[m] = into[n] + 1;
                _iceQueue.Add(m);
            }
        }
        return into;
    }
}
