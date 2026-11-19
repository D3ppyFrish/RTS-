using System;
using System.Collections.Generic;
using Rts.Sim.Units;

namespace Rts.Sim.World;

/// <summary>
/// Natural hazards: ground that hurts the units on or near it (issues #45, #66 and #78).
///
/// Once a second (spread over units by id) every unit standing on poison bog (<see cref="BlockType.Bog"/>)
/// loses <see cref="BogDamage"/> hit points, and every unit standing next to lava (within one column, at its
/// feet's level or the one below) loses <see cref="LavaDamage"/>, and every unit standing on quicksand
/// (<see cref="BlockType.Quicksand"/>) loses <see cref="QuicksandDamage"/> and walks at a
/// <see cref="QuicksandSlow"/>th of its speed while it is on it. Both land with the tick's hits, by no one, so
/// no one gets the kill and armour does not help. Wild monsters are hurt the same. Digging the bog away (it is
/// gathered like mud) makes the ground safe.
///
/// Sinkholes are a thin crust (<see cref="BlockType.Crust"/>) over a hollow. The tick a unit stands on one,
/// the whole crust (every crust block joined to it, at its level) gives way: it is gone, the units on it drop
/// into the hollow and each takes <see cref="SinkholeDamage"/>. A sinkhole's hollow is a funnel, one block deep
/// at its edge and two in its middle, so what fell in can climb back out.
///
/// The walk graph counts every hazard as dear ground (see <see cref="WalkGraph"/>), so units on a move order
/// walk around them where there is another way. A player's unit killed by the ground alone is reported
/// (<see cref="GameEventKind.UnitLostToHazard"/>). The numbers are first guesses for playtesting.
/// </summary>
public sealed class HazardSystem
{
    /// <summary>Hit points a second lost standing on poison bog.</summary>
    public const int BogDamage = 3;
    /// <summary>Hit points a second lost standing next to lava.</summary>
    public const int LavaDamage = 6;
    /// <summary>Hit points a second lost standing on quicksand.</summary>
    public const int QuicksandDamage = 2;
    /// <summary>Units on quicksand move this many times slower.</summary>
    public const int QuicksandSlow = 3;
    /// <summary>Hit points lost dropping through a sinkhole's crust.</summary>
    public const int SinkholeDamage = 20;
    /// <summary>Columns from a sinkhole's middle to the edge of its crust (see <see cref="MakeSinkhole"/>).</summary>
    public const int SinkholeRadius = 2;
    /// <summary>Most crust blocks one sinkhole gives way at once (a bound on the search; generated ones are far smaller).</summary>
    public const int MaxSinkhole = 64;

    private readonly Simulation _sim;
    private readonly List<(int X, int Z)> _open = new();
    private readonly List<(int X, int Z)> _fell = new();

    public HazardSystem(Simulation sim)
    {
        _sim = sim;
    }

    /// <summary>Hit points a second a unit with its feet at block (x, y, z) loses to the ground around it.</summary>
    public int DamageAt(int x, int y, int z) => DamageAt(_sim.World, x, y, z);

    /// <summary>Hit points a second a unit with its feet at block (x, y, z) of world <paramref name="w"/> loses to the ground around it.</summary>
    public static int DamageAt(VoxelWorld w, int x, int y, int z)
    {
        int damage = 0;
        var under = w.Get(x, y - 1, z);
        if (under == BlockType.Bog) damage += BogDamage;
        else if (under == BlockType.Quicksand) damage += QuicksandDamage;
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
            if (w.Get(x + dx, y, z + dz) == BlockType.Lava || w.Get(x + dx, y - 1, z + dz) == BlockType.Lava)
                return damage + LavaDamage;
        return damage;
    }

    /// <summary>
    /// True if a unit with its feet at (x, y, z) is in danger there: it is hurt every second, or it stands on a
    /// sinkhole's crust. Units on a move order keep off such ground where they can (see <see cref="WalkGraph"/>).
    /// </summary>
    public static bool IsHazard(VoxelWorld w, int x, int y, int z) =>
        w.Get(x, y - 1, z) == BlockType.Crust || DamageAt(w, x, y, z) > 0;

    /// <summary>Before the fighting: sinkholes give way under whoever stands on them, then the units whose second it is take what the ground does to them.</summary>
    public void Step()
    {
        var u = _sim.Units;
        var w = _sim.World;
        long tick = _sim.Tick;
        int tps = Simulation.TicksPerSecond;
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Hp[i] <= 0) continue;
            int x = u.X[i].FloorToInt(), z = u.Z[i].FloorToInt(), y = u.Y[i];
            if (w.Get(x, y - 1, z) == BlockType.Crust) GiveWay(x, y - 1, z);
        }
        for (int i = 0; i < u.Count; i++)
        {
            if ((tick + u.Id[i]) % tps != 0 || u.Hp[i] <= 0) continue;
            int x = u.X[i].FloorToInt(), z = u.Z[i].FloorToInt(), y = u.Y[i];
            int damage = DamageAt(w, x, y, z);
            if (damage <= 0) continue;
            _sim.Combat.Crush(i, damage);
            if (damage >= u.Hp[i]) Report(i, x, y, z);
        }
    }

    /// <summary>
    /// The sinkhole whose crust includes block (x, y, z) gives way: every crust block joined to it at that level
    /// (side by side, in a fixed order) becomes air, and the units standing on them take <see cref="SinkholeDamage"/>
    /// and drop in (they fall as units do when the ground under them is dug away).
    /// </summary>
    public void GiveWay(int x, int y, int z)
    {
        var w = _sim.World;
        _open.Clear();
        _fell.Clear();
        _open.Add((x, z));
        _fell.Add((x, z));
        while (_open.Count > 0 && _fell.Count < MaxSinkhole)
        {
            var (cx, cz) = _open[^1];
            _open.RemoveAt(_open.Count - 1);
            for (int d = 0; d < 4; d++)
            {
                int nx = cx + WalkGraph.Dirs[d].X, nz = cz + WalkGraph.Dirs[d].Z;
                if (w.Get(nx, y, nz) != BlockType.Crust || _fell.Contains((nx, nz)) || _fell.Count >= MaxSinkhole) continue;
                _fell.Add((nx, nz));
                _open.Add((nx, nz));
            }
        }
        var u = _sim.Units;
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Hp[i] <= 0 || u.Y[i] != y + 1) continue;
            int ux = u.X[i].FloorToInt(), uz = u.Z[i].FloorToInt();
            if (!_fell.Contains((ux, uz))) continue;
            _sim.Combat.Crush(i, SinkholeDamage);
            if (SinkholeDamage >= u.Hp[i]) Report(i, ux, y + 1, uz);
        }
        foreach (var (cx, cz) in _fell) w.Set(cx, y, cz, BlockType.Air);
    }

    /// <summary>
    /// Makes a sinkhole around column (cx, cz) whose ground's top block is at <paramref name="top"/>: a round lid of
    /// crust (within <see cref="SinkholeRadius"/> columns, corners cut) in place of the top block, over a funnel of a
    /// hollow: nothing under its edge, one more block of air under its middle (within a column of it). Once it
    /// gives way the hole is a block deep at the edge and two in the middle, so whatever fell in climbs back out.
    /// </summary>
    public static void MakeSinkhole(VoxelWorld w, int cx, int top, int cz)
    {
        int r = SinkholeRadius;
        for (int dz = -r; dz <= r; dz++)
        for (int dx = -r; dx <= r; dx++)
        {
            if (dx * dx + dz * dz > r * r + 1) continue;
            w.Set(cx + dx, top, cz + dz, BlockType.Crust);
            if (Math.Abs(dx) <= 1 && Math.Abs(dz) <= 1) w.Set(cx + dx, top - 1, cz + dz, BlockType.Air);
        }
    }

    /// <summary>
    /// True if a building with its corner at (x, z) and that footprint, or a worker at work around it, would be in
    /// danger (see <see cref="IsHazard"/>): any column of the footprint, or of the ring of columns round it where
    /// the builders stand, is hazard ground at its surface. Computer players keep their sites off such ground.
    /// </summary>
    public static bool HazardNear(VoxelWorld w, int x, int z, int sizeX, int sizeZ)
    {
        for (int cz = z - 1; cz <= z + sizeZ; cz++)
        for (int cx = x - 1; cx <= x + sizeX; cx++)
        {
            if (cx < 0 || cz < 0 || cx >= w.SizeX || cz >= w.SizeZ) continue;
            if (IsHazard(w, cx, w.SurfaceHeight(cx, cz) + 1, cz)) return true;
        }
        return false;
    }

    /// <summary>
    /// Makes a patch of quicksand around column (cx, cz): every column within <paramref name="radius"/> (corners
    /// cut) whose top block is sand open to the sky, at <paramref name="top"/> or a level either side of it, turns
    /// to quicksand.
    /// </summary>
    public static void MakeQuicksand(VoxelWorld w, int cx, int top, int cz, int radius)
    {
        for (int dz = -radius; dz <= radius; dz++)
        for (int dx = -radius; dx <= radius; dx++)
        {
            if (dx * dx + dz * dz > radius * radius + 1) continue;
            int x = cx + dx, z = cz + dz;
            if (x < 0 || z < 0 || x >= w.SizeX || z >= w.SizeZ) continue;
            int h = w.SurfaceHeight(x, z);
            if (Math.Abs(h - top) > 1 || w.Get(x, h, z) != BlockType.Sand || w.Get(x, h + 1, z) != BlockType.Air) continue;
            w.Set(x, h, z, BlockType.Quicksand);
        }
    }

    /// <summary>Tells the unit's owner the ground killed it (wild monsters and towns have no one to tell).</summary>
    private void Report(int i, int x, int y, int z)
    {
        var u = _sim.Units;
        _sim.Events.UnitLostToHazard(u.Owner[i], u.Id[i], u.Def[i], x, y, z);
    }
}
