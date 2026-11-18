using System;
using System.Collections.Generic;
using Rts.Sim.Economy;

namespace Rts.Sim.Units;

/// <summary>
/// Sappers dig tunnels (<see cref="TunnelCommand"/>): column by column from where they stand toward the
/// goal, along the axis with further to go, a level up or down per column until they reach the goal's
/// level. They clear what a unit needs to step into the next column (its body, and the room to climb or
/// drop into it), then walk in. Spoil is left behind. They stop at bedrock, water, buildings and fields;
/// a tunnel just under a building's wall takes away what holds it up, and that part of the wall falls. The
/// sapper digs a sump under that column first, so the wall falls into it rather than onto the sapper, and
/// waits for it to land before digging on through the rubble.
/// </summary>
public sealed partial class CombatSystem
{
    /// <summary>Sappers dig this many times as fast as a worker gathering the same block.</summary>
    public const int SapperDigSpeed = 2;

    private readonly List<(int X, int Y, int Z)> _dig = new(3);

    internal void Apply(TunnelCommand c)
    {
        if (!World.InBounds(c.X, c.Y, c.Z)) return;
        foreach (int i in _sim.OwnedUnits(c))
        {
            if (Def(i).Role != UnitRole.Sapper) continue;
            U.ClearJob(i);
            U.ClearCombat(i);
            Halt(i);
            U.Job[i] = UnitJob.Tunnel;
            U.JobX[i] = c.X;
            U.JobY[i] = c.Y;
            U.JobZ[i] = c.Z;
        }
    }

    /// <summary><see cref="UnitStore.JobTarget"/> of a tunnel a trapped worker or sapper digs to get out (see <see cref="DigOut"/>).</summary>
    public const int DigOutTunnel = 1;
    /// <summary>How far (columns) a trapped digger looks for open ground to tunnel to.</summary>
    public const int DigOutReach = 12;

    private readonly HashSet<int> _pocket = new();
    private readonly HashSet<int> _beyond = new();

    /// <summary>
    /// Unit i was sent somewhere it cannot walk to from where it stands, and stands in a pocket: a pit, a hole
    /// it dug, a cave it dropped into, at most <see cref="PathService.PocketLimit"/> places to walk to. A worker
    /// or sapper digs its way out instead of giving up for good: a tunnel (see <see cref="StepTunnel"/>) to the
    /// nearest open ground outside the pocket, up or down a level per column, ending as soon as it can walk
    /// on from where it is. Its job is dropped (an automatic worker gets a new one once it is out). False if
    /// it cannot dig, is not in a pocket, or has nowhere to dig to.
    /// </summary>
    internal bool DigOut(int i)
    {
        var u = U;
        if (Def(i).Role is not (UnitRole.Worker or UnitRole.Sapper) || u.Rout[i] != 0) return false;
        var g = _sim.Walkable;
        int x = u.X[i].FloorToInt(), z = u.Z[i].FloorToInt(), y = u.Y[i];
        int start = g.Node(x, z, y);
        if (start < 0 || g.ReachesMoreThan(start, PathService.PocketLimit, _pocket)) return false;
        // The nearest ground outside the pocket, close enough in height to tunnel to (a level per column), that
        // is not a pocket of its own. Nearer first, then the smaller climb, then in a fixed order.
        for (int r = 1; r <= DigOutReach; r++)
        {
            int bestNode = -1, bestScore = int.MaxValue;
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                int cx = x + dx, cz = z + dz;
                int first = g.NearestNode(cx, cz, -1); // the column's top level; the others follow it
                if (first < 0) continue;
                for (int s = 0; s < WalkGraph.MaxLevels; s++)
                {
                    int n = first + s;
                    if (g.Level(n) < 0) break;
                    int dy = Math.Abs(g.Level(n) - y);
                    if (dy > r || _pocket.Contains(n)) continue;
                    int score = dy * 64 + dx * dx + dz * dz;
                    if (score >= bestScore || !g.ReachesMoreThan(n, PathService.PocketLimit, _beyond)) continue;
                    bestScore = score;
                    bestNode = n;
                }
            }
            if (bestNode < 0) continue;
            u.ClearJob(i);
            Halt(i);
            u.Job[i] = UnitJob.Tunnel;
            u.JobTarget[i] = DigOutTunnel;
            u.JobX[i] = g.ColumnX(bestNode);
            u.JobY[i] = g.Level(bestNode);
            u.JobZ[i] = g.ColumnZ(bestNode);
            return true;
        }
        return false;
    }

    /// <summary>Ticks a sapper takes to dig out a block.</summary>
    public static int DigTicks(BlockType b) => Math.Max(1, Harvest.WorkTicks(b) / SapperDigSpeed);

    /// <summary>True if a tunnel can be dug through the block: natural ground or a road, not bedrock or a building.</summary>
    public static bool Tunnelable(BlockType b) =>
        BlockInfo.IsDiggable(b) && (!BlockInfo.IsBuilt(b) || b == BlockType.Road);

    /// <summary>
    /// When the stretch being dug takes away the ground a building's column stands on, the sapper first digs
    /// a sump under it, deep enough that the column (which falls as rubble, see
    /// <see cref="World.PhysicsSystem"/>) drops into the sump and stays out of the tunnel the sapper stands
    /// in. <see cref="_dig"/> holds the stretch's blocks; the sump's go in front of them, top first.
    /// </summary>
    private void AddSump(int nx, int nz, int feet)
    {
        int floor = int.MaxValue, roof = int.MinValue;
        foreach (var (bx, by, bz) in _dig)
        {
            if (bx != nx || bz != nz) continue;
            floor = Math.Min(floor, by);
            roof = Math.Max(roof, by);
        }
        if (floor == int.MaxValue || roof + 1 >= World.SizeY) return;
        var b = Buildings.At(nx, roof + 1, nz);
        if (b == null || b.Def.Walkable || !BlockInfo.IsBuilt(World.Get(nx, roof + 1, nz))) return;
        int h = 0;
        // All of the building's blocks that fall with the column: up through its room to the roof (see Fall).
        int top = b.Y + b.Def.Height;
        for (int y = roof + 1; y < World.SizeY; y++)
        {
            var t = World.Get(nx, y, nz);
            if (t == BlockType.Air && y < top) continue;
            if (!BlockInfo.IsBuilt(t) || Buildings.At(nx, y, nz) != b) break;
            h++;
        }
        // The heap fills the column from the sump's floor up; it must top out low enough that nothing slides
        // off it into the sapper's column (a step of two or more), whose floor is at its feet.
        // It stops short at anything it cannot dig (bedrock, water).
        int depth = floor + h - feet - 2, dug = 0;
        while (dug < depth && floor - dug - 1 >= 0)
        {
            var below = World.Get(nx, floor - dug - 1, nz);
            if (below != BlockType.Air && (below == BlockType.Water || !Tunnelable(below))) break;
            dug++;
        }
        for (int k = dug; k >= 1; k--) _dig.Insert(0, (nx, floor - k, nz));
    }

    /// <summary>True if a falling piece is over column (x, z).</summary>
    private bool Falling(int x, int z)
    {
        foreach (var s in _sim.Physics.Falling)
            foreach (var b in s.Blocks)
                if (b.X + s.OffX == x && b.Z + s.OffZ == z) return true;
        return false;
    }

    /// <summary>True if a unit digging itself out can walk on from where it stands: it is out of the pocket.</summary>
    private bool Out(int i)
    {
        var g = _sim.Walkable;
        int n = g.Node(U.X[i].FloorToInt(), U.Z[i].FloorToInt(), U.Y[i]);
        return n >= 0 && g.ReachesMoreThan(n, PathService.PocketLimit, _beyond);
    }

    private void StepTunnel(int i)
    {
        var u = U;
        if (u.Order[i] != UnitOrder.Idle) return; // stepping into the stretch it just dug
        int x = u.X[i].FloorToInt(), z = u.Z[i].FloorToInt(), y = u.Y[i];
        int dx = u.JobX[i] - x, dz = u.JobZ[i] - z;
        if ((dx == 0 && dz == 0) || (u.JobTarget[i] == DigOutTunnel && Out(i)))
        {
            u.ClearJob(i);
            return;
        }
        int nx = x, nz = z;
        if (Math.Abs(dx) >= Math.Abs(dz)) nx += Math.Sign(dx);
        else nz += Math.Sign(dz);
        int ny = y + Math.Sign(u.JobY[i] - y);

        _dig.Clear();
        if (ny > y)
        {
            // Climbing: room over its head first, then its body one level up in the next column.
            _dig.Add((x, y + 2, z));
            _dig.Add((nx, y + 1, nz));
            _dig.Add((nx, y + 2, nz));
        }
        else if (ny < y)
        {
            // Dropping: it walks off at its own height, so the next column must be open from there down.
            _dig.Add((nx, y + 1, nz));
            _dig.Add((nx, y, nz));
            _dig.Add((nx, y - 1, nz));
        }
        else
        {
            _dig.Add((nx, y, nz));
            _dig.Add((nx, y + 1, nz));
        }
        AddSump(nx, nz, y);
        foreach (var (bx, by, bz) in _dig)
        {
            var block = World.Get(bx, by, bz);
            if (block == BlockType.Air) continue;
            if (block == BlockType.Water || !Tunnelable(block) || !World.InBounds(bx, by, bz))
            {
                u.ClearJob(i);
                return;
            }
            // A worker digging itself out digs at its gathering pace, a sapper twice as fast.
            int ticks = Def(i).Role == UnitRole.Sapper ? DigTicks(block) : Harvest.WorkTicks(block);
            if (++u.JobTimer[i] < Math.Max(1, ticks * 100 / Def(i).WorkPercent)) return;
            u.JobTimer[i] = 0;
            World.Set(bx, by, bz, BlockType.Air);
            return;
        }
        // Something it brought down is still falling into the next column: it waits for it to land.
        if (Falling(nx, nz)) return;
        if (!Walk.TryStep(World, x, z, y, nx, nz, out _))
        {
            u.ClearJob(i); // nothing to stand on there (a drop too deep, a hole)
            return;
        }
        // A short straight walk into the next column; no flow field needed.
        u.ClearFormation(i);
        u.Order[i] = UnitOrder.Move;
        u.OrderX[i] = nx;
        u.OrderZ[i] = nz;
        u.Group[i] = 0;
        u.ArriveRadius[i] = Fix64.FromRatio(3, 10);
        u.Blocked[i] = 0;
        u.Field[i] = 0;
    }
}
