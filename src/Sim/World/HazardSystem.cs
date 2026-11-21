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
/// gathered like mud) makes the ground safe. A unit left with its head under water by a flood, with no ground
/// near enough to be swept out to (see <see cref="PhysicsSystem"/>), loses <see cref="DrownDamage"/> a second.
///
/// Sinkholes are a thin crust (<see cref="BlockType.Crust"/>) over a hollow. The tick a unit stands on one,
/// the whole crust (every crust block joined to it, at its level) gives way: it is gone, the units on it drop
/// into the hollow and each takes <see cref="SinkholeDamage"/>. A sinkhole's hollow is a funnel, one block deep
/// at its edge and two in its middle, so what fell in can climb back out.
///
/// Rockfall: loose rock (<see cref="BlockType.LooseRock"/>) perched on the brink of a cliff in the hills, over a
/// field of scree (<see cref="BlockType.Scree"/>) at its foot. The tick a unit steps onto the scree, or onto the
/// rocks, every loose rock within <see cref="RockfallReach"/> columns of it and above its feet comes away and falls
/// towards it (see <see cref="Dislodge"/>): what the falling rock hits is crushed (<see cref="PhysicsSystem.CrushDamage"/>),
/// and it lands as stone rubble. The scree whose rocks are all gone is then plain stone.
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
    /// <summary>Hit points a second lost with the head under water, where a flood left no ground near to swim to.</summary>
    public const int DrownDamage = 10;
    /// <summary>Units on quicksand move this many times slower.</summary>
    public const int QuicksandSlow = 3;
    /// <summary>Hit points lost dropping through a sinkhole's crust.</summary>
    public const int SinkholeDamage = 20;
    /// <summary>Columns from a sinkhole's middle to the edge of its crust (see <see cref="MakeSinkhole"/>).</summary>
    public const int SinkholeRadius = 2;
    /// <summary>Most crust blocks one sinkhole gives way at once (a bound on the search; generated ones are far smaller).</summary>
    public const int MaxSinkhole = 64;

    /// <summary>Columns (either way) from a unit on scree within which the loose rocks above it come down.</summary>
    public const int RockfallReach = 4;
    /// <summary>Levels above a unit's feet that loose rock can fall on it from.</summary>
    public const int RockfallHeight = 10;
        /// <summary>Speed (64ths of a block per tick) falling rocks start off upwards at, so they tip off the brink rather than slide down its face.</summary>
    public const int RockfallHop = 24;

    private readonly Simulation _sim;
    private readonly List<(int X, int Y, int Z)> _rocks = new();
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
        int damage = w.Get(x, y + 1, z) == BlockType.Water ? DrownDamage : 0;
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
        w.Get(x, y - 1, z) is BlockType.Crust or BlockType.Scree or BlockType.LooseRock || DamageAt(w, x, y, z) > 0;

    /// <summary>Before the fighting: sinkholes give way and rocks fall on whoever stands on them, then the units whose second it is take what the ground does to them.</summary>
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
            var under = w.Get(x, y - 1, z);
            if (under == BlockType.Crust) GiveWay(x, y - 1, z);
            else if (under is BlockType.Scree or BlockType.LooseRock) Dislodge(x, y, z);
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
    /// A unit with its feet at (x, y, z) has stepped onto scree or loose rock: every loose rock within
    /// <see cref="RockfallReach"/> columns of it, from the level under its feet up to <see cref="RockfallHeight"/>
    /// above, comes away and falls (see <see cref="PhysicsSystem.Release"/>), thrown towards the unit. Then every
    /// block of scree around it that has no loose rock left above it within reach turns to plain stone, so the
    /// ground there is safe. Returns how many rocks fell.
    /// </summary>
    public int Dislodge(int x, int y, int z)
    {
        var w = _sim.World;
        int r = RockfallReach;
        _rocks.Clear();
        long sx = 0, sz = 0;
        for (int ry = y - 1; ry <= y + RockfallHeight; ry++)
        for (int rz = z - r; rz <= z + r; rz++)
        for (int rx = x - r; rx <= x + r; rx++)
            if (w.Get(rx, ry, rz) == BlockType.LooseRock) { _rocks.Add((rx, ry, rz)); sx += rx; sz += rz; }
        int fell = _rocks.Count;
        if (fell > 0)
        {
            // Thrown at the unit: pushed away from the point as far beyond the rocks' middle as the unit is in front
            // of it, at the speed that brings them down where it stands.
            int mx = (int)(sx / fell), mz = (int)(sz / fell), low = int.MaxValue;
            foreach (var rock in _rocks) low = Math.Min(low, rock.Y);
            int ticks = FlightTicks(Math.Max(0, low - y));
            int dist = Math.Max(Math.Abs(x - mx), Math.Abs(z - mz));
            int push = Math.Clamp((dist * PhysicsSystem.SubSteps + PhysicsSystem.SubSteps / 2 + ticks - 1) / ticks, MinPush, PhysicsSystem.MaxSpeed);
            _sim.Physics.Release(_rocks, 2 * mx - x, 2 * mz - z, push, hop: RockfallHop);
        }
        // The scree around whose rocks are gone is safe ground now.
        int far = 2 * r;
        for (int cz = z - far; cz <= z + far; cz++)
        for (int cx = x - far; cx <= x + far; cx++)
        for (int cy = y - 1 - RockfallHeight; cy <= y + RockfallHeight; cy++)
        {
            if (w.Get(cx, cy, cz) != BlockType.Scree || RocksAbove(w, cx, cy + 1, cz)) continue;
            w.Set(cx, cy, cz, BlockType.Stone);
        }
        return fell;
    }

    /// <summary>Least push that carries a rock a whole block out before it comes back down to where it started, clear of the brink.</summary>
    private static readonly int MinPush = PhysicsSystem.SubSteps * PhysicsSystem.Gravity / (2 * RockfallHop) + 1;

    /// <summary>Ticks a rock thrown up at <see cref="RockfallHop"/> takes to come down <paramref name="drop"/> levels below where it started.</summary>
    private static int FlightTicks(int drop)
    {
        int vel = RockfallHop, pos = 0, t = 0;
        while (pos > -drop * PhysicsSystem.SubSteps || vel > 0)
        {
            vel = Math.Max(vel - PhysicsSystem.Gravity, -PhysicsSystem.MaxSpeed);
            pos += vel;
            t++;
        }
        return Math.Max(1, t);
    }

    /// <summary>True if loose rock that a unit with its feet at (x, y, z) would shake loose is still there.</summary>
    public static bool RocksAbove(VoxelWorld w, int x, int y, int z)
    {
        int r = RockfallReach;
        for (int ry = y - 1; ry <= y + RockfallHeight; ry++)
        for (int rz = z - r; rz <= z + r; rz++)
        for (int rx = x - r; rx <= x + r; rx++)
            if (w.Get(rx, ry, rz) == BlockType.LooseRock) return true;
        return false;
    }

    /// <summary>
    /// Makes a rockfall at the brink of a cliff: loose rock on the top of column (bx, bz), whose ground is at
    /// <paramref name="top"/>, and on the columns either side of it along the brink (across direction
    /// (<paramref name="dx"/>, <paramref name="dz"/>), which points down the cliff) where the ground is as high,
    /// with one more rock on top in the middle; and scree in place of the top block of every column below the
    /// cliff, up to <paramref name="reach"/> columns out from it and two to either side, that is natural ground
    /// at least two levels lower. Returns how many columns of scree were laid.
    /// </summary>
    public static int MakeRockfall(VoxelWorld w, int bx, int top, int bz, int dx, int dz, int reach)
    {
        int px = -dz, pz = dx; // along the brink
        for (int s = -1; s <= 1; s++)
        {
            int x = bx + px * s, z = bz + pz * s;
            if (w.SurfaceHeight(x, z) != top || w.Get(x, top + 1, z) != BlockType.Air) continue;
            w.Set(x, top + 1, z, BlockType.LooseRock);
        }
        if (w.Get(bx, top + 2, bz) == BlockType.Air) w.Set(bx, top + 2, bz, BlockType.LooseRock);
        int laid = 0;
        for (int k = 1; k <= reach; k++)
        for (int s = -2; s <= 2; s++)
        {
            int x = bx + dx * k + px * s, z = bz + dz * k + pz * s;
            if (x < 0 || z < 0 || x >= w.SizeX || z >= w.SizeZ) continue;
            int h = w.SurfaceHeight(x, z);
            if (h > top - 2 || !BlockInfo.IsGround(w.Get(x, h, z)) || w.Get(x, h + 1, z) != BlockType.Air) continue;
            w.Set(x, h, z, BlockType.Scree);
            laid++;
        }
        return laid;
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
