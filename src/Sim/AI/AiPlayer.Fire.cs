using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;

namespace Rts.Sim.AI;

// Setting woods alight (see FireSystem), from Normal up. In dry weather (a clear or cloudy day, the ground no wetter
// than FireWetMost) it looks at the woods by the rivals it sees: a grove is the trees whose trunks stand within
// GroveLink columns of each other, so that a fire in one runs through them all. A grove is worth burning when the
// rival wooden buildings beside it (FireBuildingWorth each) and rival units standing in it come to at least
// FireAtLeast, and nothing of its own or of anyone it spares is near it: no building of its own within FireHome columns
// of its trees (the woods by its town are its wood), none with timber or thatch of a side it spares within FireSafe,
// and no unit of its own within FireClear columns of it but the one sent; and not while enemy soldiers are within
// DefendRadius of its town hall, when its defenders would run into the fire. It sends its nearest soldier that can
// light a fire (within FireLook columns, not in a fight, and with no rival soldier within FireDanger) to the tree of
// the grove nearest to it, and its army leaves that soldier to it until the job is done. One fire at a time, and one
// every FireEveryTicks at most. The classic and Easy players leave fire alone.
public sealed partial class AiPlayer
{
    /// <summary>Trees whose trunks stand this close (columns, each way) are one grove: a fire in one reaches the next.</summary>
    public const int GroveLink = 6;
    /// <summary>Most trees of a grove it looks at; a forest bigger than that is too big to know where it would burn.</summary>
    public const int GroveMost = 400;
    /// <summary>A spared side's wooden buildings must be further than this (columns) from every tree of a grove it lights.</summary>
    public const int FireSafe = 6;
    /// <summary>
    /// It lights no grove with a tree this close (columns) to any building of its own: the woods by its town are its
    /// wood, and a fire there is its workers' and defenders' to walk into.
    /// </summary>
    public const int FireHome = RecallRadius;
    /// <summary>None of its units but the one sent may stand this close (columns, each way) to a tree of a grove it lights.</summary>
    public const int FireClear = 6;
    /// <summary>A rival wooden building this close (columns) to a tree of a grove is scorched when the grove burns.</summary>
    public const int FireReach = 3;
    /// <summary>What a rival wooden building by the grove counts for (a rival unit in it counts one).</summary>
    public const int FireBuildingWorth = 3;
    /// <summary>The least a grove must be worth to set alight: a wooden building, or three rival units in it.</summary>
    public const int FireAtLeast = 3;
    /// <summary>A soldier with a rival soldier it sees this close (columns) is not sent to light a fire: it has a fight on its hands.</summary>
    public const int FireDanger = 12;
    /// <summary>Furthest (columns) a soldier is sent to light a fire.</summary>
    public const int FireLook = 24;
    /// <summary>Wettest the ground may be (percent) for it to light a fire: wetter, the woods are slow to catch.</summary>
    public const int FireWetMost = 40;
    /// <summary>Least time between two fires it lights: half a minute.</summary>
    public const int FireEveryTicks = 30 * Tps;

    // The soldier sent to set a grove alight (by id), left out of its army's orders meanwhile; 0 if none.
    private int _kindler;
    private long _kindledAt = long.MinValue / 2;

    /// <summary>The soldier sent to light a fire for it now (by id), or 0.</summary>
    public int Kindler => _kindler;

    /// <summary>Fires it has ordered lit (for the tests and the match runner's trace).</summary>
    public int Kindlings { get; private set; }

    /// <summary>True if the weather and the ground are dry enough for it to light a fire: clear or cloudy, and not wet.</summary>
    public static bool FireWeather(Simulation sim) =>
        sim.Fire.CanKindle && sim.Conditions.Weather is Weather.Clear or Weather.Cloudy && sim.Conditions.Wetness <= FireWetMost;

    /// <summary>Picks a grove by the rivals it sees to set alight, and sends its nearest soldier to light it.</summary>
    private void Kindle(Simulation sim, Building hall, Action<Command> issue)
    {
        var u = sim.Units;
        if (_kindler != 0)
        {
            if (u.Exists(_kindler) && sim.Tick - _kindledAt < 2 * Tps) return; // the order lands
            if (u.Exists(_kindler) && u.Job[u.IndexOf(_kindler)] == UnitJob.Kindle) return; // still at it
            _kindler = 0;
        }
        if (sim.Tick - _kindledAt < FireEveryTicks || !FireWeather(sim)) return;
        // Not while it fights off an attack at home: its defenders would run into the fire.
        var (hx, hz) = Middle(hall);
        if (NearestEnemy(sim, hx, hz, DefendRadius, soldiersOnly: true) >= 0) return;

        // What it sees of its rivals: their wooden buildings, and their units by column.
        var w = sim.World;
        int sx = w.SizeX;
        var targets = new List<Building>();
        foreach (var b in sim.Buildings.All)
        {
            if (b.Planned || !Rival(b.Owner) || b.Owner >= Simulation.WildPlayer || !Wooden(w, b)) continue;
            if (sim.Vision.Sees(Player, (b.X + b.MaxX) / 2, (b.Z + b.MaxZ) / 2) && !sim.Vision.HiddenFrom(Player, b)) targets.Add(b);
        }
        var rivals = new SortedDictionary<long, int>();
        var fighters = new List<(int X, int Z)>();
        for (int i = 0; i < u.Count; i++)
        {
            int o = u.Owner[i];
            if (o >= Simulation.WildPlayer || !Rival(o) || u.Hp[i] <= 0 || u.Aboard[i] != 0 || !sim.Vision.SeesUnit(Player, i)) continue;
            int rx = u.X[i].FloorToInt(), rz = u.Z[i].FloorToInt();
            long k = (long)rz * sx + rx;
            rivals[k] = rivals.TryGetValue(k, out int n) ? n + 1 : 1;
            var def = sim.Content.Units[u.Def[i]];
            if (def.CanAttack && def.Role is not (UnitRole.Worker or UnitRole.Trader)) fighters.Add((rx, rz));
        }
        if (targets.Count == 0 && rivals.Count < FireAtLeast) return;

        // Its soldiers that could go and light it: not in a fight, and with no rival soldier near.
        var kindlers = new List<int>();
        foreach (int id in NotFighting(sim, Soldiers(sim)))
        {
            int i = u.IndexOf(id);
            if (!sim.Economy.CanKindle(i) || u.Aboard[i] != 0 || u.Hp[i] <= 0) continue;
            int ux = u.X[i].FloorToInt(), uz = u.Z[i].FloorToInt();
            if (fighters.Exists(f => Math.Abs(f.X - ux) <= FireDanger && Math.Abs(f.Z - uz) <= FireDanger)) continue;
            kindlers.Add(i);
        }
        if (kindlers.Count == 0) return;

        // The trees beside what it would burn, each grove looked at once.
        var trees = new Dictionary<long, bool>();
        var looked = new SortedSet<long>();
        var seeds = new List<(int X, int Z)>();
        foreach (var b in targets)
            for (int z = b.Z - FireReach; z <= b.MaxZ + FireReach; z++)
            for (int x = b.X - FireReach; x <= b.MaxX + FireReach; x++)
                seeds.Add((x, z));
        foreach (long k in rivals.Keys)
        {
            int rx = (int)(k % sx), rz = (int)(k / sx);
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
                seeds.Add((rx + dx, rz + dz));
        }

        int bestI = -1, bestX = 0, bestZ = 0, bestWorth = 0;
        long bestD = long.MaxValue;
        foreach (var (x, z) in seeds)
        {
            if (!w.InBounds(x, 0, z) || looked.Contains((long)z * sx + x) || !TreeAt(w, trees, x, z)) continue;
            var grove = Grove(w, trees, x, z);
            foreach (var (gx, gz) in grove) looked.Add((long)gz * sx + gx);
            if (grove.Count == 0 || grove.Count >= GroveMost) continue;
            int worth = GroveWorth(sim, grove, targets, rivals);
            if (worth < FireAtLeast || worth < bestWorth) continue;
            // The grove's tree nearest to one of its soldiers, and that soldier.
            int gi = -1, tx = 0, tz = 0;
            long d = long.MaxValue;
            foreach (int i in kindlers)
            {
                int ux = u.X[i].FloorToInt(), uz = u.Z[i].FloorToInt();
                foreach (var (gx, gz) in grove)
                {
                    long dd = Dist2(ux, uz, gx, gz);
                    if (dd < d) (d, gi, tx, tz) = (dd, i, gx, gz);
                }
            }
            if (gi < 0 || d > (long)FireLook * FireLook) continue;
            if (!SafeToBurn(sim, grove, u.Id[gi])) continue;
            if (worth == bestWorth && d >= bestD) continue;
            (bestWorth, bestD, bestI, bestX, bestZ) = (worth, d, gi, tx, tz);
        }
        if (bestI < 0) return;
        _kindler = u.Id[bestI];
        _kindledAt = sim.Tick;
        Kindlings++;
        issue(new KindleCommand(Player, new[] { _kindler }, bestX, bestZ));
        Note(sim, $"sets the wood at {bestX}, {bestZ} alight by its rivals");
    }

    /// <summary>True if the building has a block of timber, thatch or the like in it that fire scorches.</summary>
    private static bool Wooden(VoxelWorld w, Building b)
    {
        int top = b.Y + Math.Max(1, b.Shape.Height);
        for (int y = b.Y; y <= top; y++)
        for (int z = b.Z; z <= b.MaxZ; z++)
        for (int x = b.X; x <= b.MaxX; x++)
            if (FireSystem.Flammable(w.Get(x, y, z))) return true;
        return false;
    }

    /// <summary>True if column (x, z) has a tree's trunk in it that can burn (remembered in <paramref name="trees"/>).</summary>
    private static bool TreeAt(VoxelWorld w, Dictionary<long, bool> trees, int x, int z)
    {
        if (!w.InBounds(x, 0, z)) return false;
        long k = (long)z * w.SizeX + x;
        if (trees.TryGetValue(k, out bool t)) return t;
        t = false;
        for (int y = w.SizeY - 1; y >= 1; y--)
        {
            var b = w.Get(x, y, z);
            if (b == BlockType.Air || BlockInfo.IsLeaves(b)) continue;
            t = BlockInfo.IsTrunk(b) && FireSystem.CanBurn(b);
            break;
        }
        trees[k] = t;
        return t;
    }

    /// <summary>
    /// The trees of the grove that the tree at (x, z) stands in: every tree within <see cref="GroveLink"/> columns of
    /// another of it, in the order found; stops at <see cref="GroveMost"/>.
    /// </summary>
    private static List<(int X, int Z)> Grove(VoxelWorld w, Dictionary<long, bool> trees, int x, int z)
    {
        var grove = new List<(int X, int Z)> { (x, z) };
        var seen = new SortedSet<long> { (long)z * w.SizeX + x };
        for (int n = 0; n < grove.Count && grove.Count < GroveMost; n++)
        {
            var (cx, cz) = grove[n];
            for (int dz = -GroveLink; dz <= GroveLink; dz++)
            for (int dx = -GroveLink; dx <= GroveLink; dx++)
            {
                int nx = cx + dx, nz = cz + dz;
                if (!w.InBounds(nx, 0, nz) || !seen.Add((long)nz * w.SizeX + nx) || !TreeAt(w, trees, nx, nz)) continue;
                grove.Add((nx, nz));
            }
        }
        return grove;
    }

    /// <summary>What burning the grove is worth: <see cref="FireBuildingWorth"/> for each rival wooden building by it, one for each rival unit in it.</summary>
    private static int GroveWorth(Simulation sim, List<(int X, int Z)> grove, List<Building> targets, SortedDictionary<long, int> rivals)
    {
        int worth = 0;
        foreach (var b in targets)
            foreach (var (gx, gz) in grove)
                if (b.ColumnDistance(gx, gz) <= FireReach)
                {
                    worth += FireBuildingWorth;
                    break;
                }
        int sx = sim.World.SizeX;
        var counted = new SortedSet<long>();
        foreach (var (gx, gz) in grove)
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                long k = (long)(gz + dz) * sx + gx + dx;
                if (rivals.TryGetValue(k, out int n) && counted.Add(k)) worth += n;
            }
        return worth;
    }

    /// <summary>
    /// True if setting the grove alight spares its own: no building of its own within <see cref="FireHome"/> columns of a
    /// tree of it, no wooden building of a side it is not fighting within <see cref="FireSafe"/>, and none of its units but <paramref name="kindler"/> within <see cref="FireClear"/> columns of it.
    /// </summary>
    private bool SafeToBurn(Simulation sim, List<(int X, int Z)> grove, int kindler)
    {
        var w = sim.World;
        foreach (var b in sim.Buildings.All)
        {
            if (b.Owner != Player && b.Owner < Simulation.WildPlayer && Rival(b.Owner)) continue;
            int reach = b.Owner == Player ? FireHome : FireSafe;
            bool near = false;
            foreach (var (gx, gz) in grove)
                if (b.ColumnDistance(gx, gz) <= reach)
                {
                    near = true;
                    break;
                }
            if (near && (b.Owner == Player || Wooden(w, b))) return false;
        }
        var u = sim.Units;
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Owner[i] != Player || u.Id[i] == kindler || u.Hp[i] <= 0 || u.Aboard[i] != 0) continue;
            int ux = u.X[i].FloorToInt(), uz = u.Z[i].FloorToInt();
            foreach (var (gx, gz) in grove)
                if (Math.Abs(gx - ux) <= FireClear && Math.Abs(gz - uz) <= FireClear) return false;
        }
        return true;
    }
}
