using System;
using System.Collections.Generic;
using Rts.Sim.Units;
using Rts.Sim.World;

namespace Rts.Sim.Economy;

/// <summary>
/// Rules for gathering from the world: what a block gives, how long it takes, where a worker stands to
/// work on it, and which block of the same kind it moves on to. A tree is worked as one target, its
/// trunk from base to top: it topples over (see <see cref="PhysicsSystem.FellTree"/>) and lands as tree
/// rubble, which workers then clear for <see cref="WoodPerTreeRubble"/> wood a block, so a tree gives twice
/// the wood its trunk would.
/// </summary>
public static class Harvest
{
    /// <summary>How far (blocks) a worker looks for more of the block it is digging.</summary>
    public const int SearchRadius = 6;
    /// <summary>How far (columns) a worker looks for another tree or bush; they grow further apart than ore.</summary>
    public const int PlantSearchRadius = 12;
    /// <summary>How many levels up or down a worker looks for another tree or bush.</summary>
    public const int PlantSearchHeight = 4;
    /// <summary>Food from picking one berry bush.</summary>
    public const int FoodPerBush = 5;
    /// <summary>
    /// Wood from clearing one block of tree rubble. A felled tree lands as one block of rubble for each
    /// block of its trunk, so a tree gives twice what its trunk alone would (one wood a block).
    /// </summary>
    public const int WoodPerTreeRubble = 2;

    /// <summary>True if a worker can dig, chop or pick this block. Buildings and roads are not dug this way.</summary>
    public static bool CanGather(BlockType b) => b is BlockType.Grass or BlockType.Gloom or BlockType.Soil or BlockType.Sand
        or BlockType.Mud or BlockType.Bog or BlockType.Crust or BlockType.Quicksand or BlockType.Scree or BlockType.LooseRock or BlockType.Stone or BlockType.IronOre or BlockType.Coal or BlockType.Crystal
        or BlockType.GoldOre or BlockType.BerryBush || BlockInfo.IsTrunk(b) || BlockInfo.IsLeaves(b) || BlockInfo.IsRubble(b);

    /// <summary>The good a block gives, if any. Plain ground is only dug away.</summary>
    public static bool Yields(BlockType b, out Good good)
    {
        if (BlockInfo.IsTrunk(b)) { good = Good.Wood; return true; }
        switch (b)
        {
            case BlockType.Stone: good = Good.Stone; return true;
            case BlockType.LooseRock: good = Good.Stone; return true;
            case BlockType.IronOre: good = Good.IronOre; return true;
            case BlockType.Coal: good = Good.Coal; return true;
            case BlockType.Crystal: good = Good.Crystal; return true;
            case BlockType.GoldOre: good = Good.Gold; return true;
            case BlockType.BerryBush: good = Good.Food; return true;
            // Rubble gives back what it was made of.
            case BlockType.StoneRubble: good = Good.Stone; return true;
            case BlockType.OreRubble: good = Good.IronOre; return true;
            case BlockType.CoalRubble: good = Good.Coal; return true;
            case BlockType.WoodRubble: good = Good.Wood; return true;
            case BlockType.TreeRubble: good = Good.Wood; return true;
            default: good = Good.Food; return false;
        }
    }

    /// <summary>How much of its good (see <see cref="Yields"/>) taking one block gives.</summary>
    public static int Amount(BlockType b) => b switch
    {
        BlockType.BerryBush => FoodPerBush,
        BlockType.TreeRubble => WoodPerTreeRubble,
        _ => 1,
    };

    /// <summary>
    /// True if a worker gathering <paramref name="kind"/> works on block <paramref name="b"/>: the same
    /// block, or, for wood cutters, any tree's trunk and the rubble of the trees they fell.
    /// </summary>
    public static bool Matches(BlockType kind, BlockType b) =>
        b == kind || kind == BlockType.Wood && (BlockInfo.IsTrunk(b) || b == BlockType.TreeRubble);

    /// <summary>
    /// The kind of work a block is gathered as: every tree's trunk, and tree rubble, are part of cutting wood
    /// (<see cref="BlockType.Wood"/>).
    /// </summary>
    public static BlockType JobKind(BlockType b) => BlockInfo.IsTrunk(b) || b == BlockType.TreeRubble ? BlockType.Wood : b;

    /// <summary>
    /// Ticks of work to take one block (for a tree, per trunk block). A fifth quicker than before Better tools gave
    /// tools in place of 25% faster gathering (issue #146), so the economy keeps its pace.
    /// </summary>
    public static int WorkTicks(BlockType b) => b switch
    {
        BlockType.Stone or BlockType.LooseRock => 25,
        BlockType.IronOre => 33,
        BlockType.Coal => 25,
        BlockType.Crystal => 50,
        BlockType.GoldOre => 50,
        BlockType.BerryBush => 33,
        _ when BlockInfo.IsTrunk(b) => 17,
        _ when BlockInfo.IsRubble(b) => 8,
        _ => 12,
    };

    /// <summary>
    /// Ticks of work for a unit tunnelling through a block (see <see cref="Units.CombatSystem"/>): the pace digging had
    /// before gathering was made quicker (#146), as Better tools never sped tunnels up.
    /// </summary>
    public static int TunnelTicks(BlockType b) => b switch
    {
        BlockType.Stone or BlockType.LooseRock => 30,
        BlockType.IronOre => 40,
        BlockType.Coal => 30,
        BlockType.Crystal => 60,
        BlockType.GoldOre => 60,
        BlockType.BerryBush => 40,
        _ when BlockInfo.IsTrunk(b) => 20,
        _ when BlockInfo.IsRubble(b) => 10,
        _ => 15,
    };

    /// <summary>
    /// Resources: workers move on to more of the same nearby. So does clearing rubble (for what it was
    /// made of), until the heap is gone.
    /// Digging plain ground is a one-off.
    /// </summary>
    public static bool Repeats(BlockType b) => b is BlockType.Stone or BlockType.IronOre or BlockType.Coal or BlockType.Crystal or BlockType.GoldOre
        or BlockType.BerryBush || BlockInfo.IsTrunk(b) || BlockInfo.IsRubble(b);

    /// <summary>Taken whole and carried away in one load (a bush), rather than a block at a time.</summary>
    public static bool Bulk(BlockType b) => b is BlockType.BerryBush;

    /// <summary>
    /// The levels a target spans in its column: a tree trunk from base to top, otherwise just the block.
    /// </summary>
    public static (int Base, int Top) Span(VoxelWorld w, int x, int y, int z)
    {
        if (!BlockInfo.IsTrunk(w.Get(x, y, z))) return (y, y);
        int b = y, t = y;
        while (b > 0 && BlockInfo.IsTrunk(w.Get(x, b - 1, z))) b--;
        while (t < w.SizeY - 1 && BlockInfo.IsTrunk(w.Get(x, t + 1, z))) t++;
        return (b, t);
    }

    /// <summary>
    /// Takes the tree whose trunk spans levels [baseY, topY] of column (x, z) out of the world: the trunk goes,
    /// and so do the leaves of its crown that no other trunk holds up. Returns the trunk blocks taken. With
    /// <paramref name="taken"/>, every block taken is added to it (for drawing the tree as it falls).
    /// </summary>
    public static int FellTree(VoxelWorld w, int x, int baseY, int topY, int z, List<SectionBlock>? taken = null)
    {
        int wood = 0;
        // A bigger tree (see VoxelWorld.TreeScale) has a trunk several columns square: all of it comes down,
        // its columns' feet reaching a little lower on uneven ground.
        var (x0, z0, s) = Trees.Footprint(w, x, z);
        if (s == 1) (x0, z0) = (x, z);
        int low = s == 1 ? baseY : Math.Max(0, baseY - 4 * s);
        for (int cz = z0; cz < z0 + s; cz++)
        for (int cx = x0; cx < x0 + s; cx++)
        for (int y = low; y <= topY; y++)
        {
            var trunk = w.Get(cx, y, cz);
            if (!BlockInfo.IsTrunk(trunk)) continue;
            w.Set(cx, y, cz, BlockType.Air);
            taken?.Add(new SectionBlock(cx, y, cz, trunk));
            wood++;
        }
        int reach = CrownRadius * s, above = CrownAbove * s + s - 1;
        for (int ly = low - 1; ly <= topY + above; ly++)
        for (int lz = z0 - reach; lz <= z0 + s - 1 + reach; lz++)
        for (int lx = x0 - reach; lx <= x0 + s - 1 + reach; lx++)
        {
            var leaf = w.Get(lx, ly, lz);
            if (!BlockInfo.IsLeaves(leaf) || NearTrunk(w, lx, ly, lz, s)) continue;
            w.Set(lx, ly, lz, BlockType.Air);
            taken?.Add(new SectionBlock(lx, ly, lz, leaf));
        }
        return wood;
    }

    /// <summary>Most columns a tree's crown reaches out from its trunk (see <see cref="World.RegionGenerator"/>).</summary>
    public const int CrownRadius = 2;
    /// <summary>Most levels a tree's crown rises over the top of its trunk.</summary>
    public const int CrownAbove = 3;
    /// <summary>Most levels a log drops as a felled tree lies down over falling ground; a deeper drop stops it.</summary>
    public const int MaxLogDrop = 3;

    /// <summary>
    /// Lays a felled tree's <paramref name="logs"/> blocks of rubble on the ground: a line of logs from the
    /// stump at (x, baseY, z) along (dirX, dirZ) (or the first way round, turning right, that has room),
    /// following the ground up a step of one or down a drop of up to <see cref="MaxLogDrop"/>, stopping at
    /// anything taller, water or the edge of the world; logs that do not fit heap up over the stump. Only air
    /// is filled. Every block placed is added to <paramref name="placed"/>.
    /// </summary>
    public static void LayFelled(VoxelWorld w, int x, int baseY, int z, int logs, int dirX, int dirZ,
        List<(int X, int Y, int Z)> placed)
    {
        // The first way, turning right, the first log fits.
        for (int turn = 0; turn < 4; turn++)
        {
            if (LogSpot(w, x + dirX, baseY, z + dirZ, out _)) break;
            (dirX, dirZ) = (-dirZ, dirX);
        }
        int cx = x, cy = baseY, cz = z, laid = 0;
        while (laid < logs && LogSpot(w, cx + dirX, cy, cz + dirZ, out int ny))
        {
            cx += dirX; cz += dirZ; cy = ny;
            w.Set(cx, cy, cz, BlockType.TreeRubble);
            placed.Add((cx, cy, cz));
            laid++;
        }
        // The rest heap up over the stump.
        int y = baseY;
        while (y > 1 && w.Get(x, y - 1, z) == BlockType.Air) y--;
        for (; laid < logs && y < w.SizeY; y++)
        {
            if (w.Get(x, y, z) != BlockType.Air) continue;
            w.Set(x, y, z, BlockType.TreeRubble);
            placed.Add((x, y, z));
            laid++;
        }
    }

    /// <summary>Widest gap or stream (columns) the trunk of an aimed tree lies across as a log bridge (see <see cref="LayAimed"/>).</summary>
    public const int MaxLogSpan = 2;

    /// <summary>
    /// A tree felled the way a player chose (see <see cref="World.PhysicsSystem.FellTreeAimed"/>) lies down: like
    /// <see cref="LayFelled"/>, but straight the way it fell, never turning. Where open ground (a gap, or a stream)
    /// of up to <see cref="MaxLogSpan"/> columns lies in front of it and the far side is level with where it stands,
    /// the trunk lies across at that level as a log bridge (resting on both sides), with enough logs left to reach.
    /// Logs that find no place heap up over the stump.
    /// </summary>
    public static void LayAimed(VoxelWorld w, int x, int baseY, int z, int logs, int dirX, int dirZ,
        List<(int X, int Y, int Z)> placed)
    {
        int cx = x, cy = baseY, cz = z, laid = 0;
        while (laid < logs)
        {
            int span = 0;
            while (span <= MaxLogSpan && Gap(w, cx + dirX * (span + 1), cy, cz + dirZ * (span + 1))) span++;
            if (span > 0)
            {
                int bx = cx + dirX * (span + 1), bz = cz + dirZ * (span + 1);
                if (span > MaxLogSpan || !Bank(w, bx, cy, bz) || logs - laid < span + 1)
                {
                    // Too wide, or nothing level to rest on over there: it drops in as any tree would, or stops.
                    if (!LogSpot(w, cx + dirX, cy, cz + dirZ, out int dy)) break;
                    cx += dirX; cz += dirZ; cy = dy;
                    w.Set(cx, cy, cz, BlockType.TreeRubble);
                    placed.Add((cx, cy, cz));
                    laid++;
                    continue;
                }
                for (int k = 1; k <= span + 1; k++)
                {
                    int lx = cx + dirX * k, lz = cz + dirZ * k;
                    w.Set(lx, cy, lz, BlockType.TreeRubble);
                    placed.Add((lx, cy, lz));
                    laid++;
                }
                cx = bx; cz = bz;
                continue;
            }
            if (!LogSpot(w, cx + dirX, cy, cz + dirZ, out int ny)) break;
            cx += dirX; cz += dirZ; cy = ny;
            w.Set(cx, cy, cz, BlockType.TreeRubble);
            placed.Add((cx, cy, cz));
            laid++;
        }
        int y = baseY;
        while (y > 1 && w.Get(x, y - 1, z) == BlockType.Air) y--;
        for (; laid < logs && y < w.SizeY; y++)
        {
            if (w.Get(x, y, z) != BlockType.Air) continue;
            w.Set(x, y, z, BlockType.TreeRubble);
            placed.Add((x, y, z));
            laid++;
        }
    }

    /// <summary>True if column (x, z) is open at level y and under it (air or water): a log there would lie over a gap.</summary>
    private static bool Gap(VoxelWorld w, int x, int y, int z) =>
        y >= 1 && w.InBounds(x, y, z) && w.Get(x, y, z) == BlockType.Air && w.Get(x, y - 1, z) is BlockType.Air or BlockType.Water;

    /// <summary>True if a log can rest at level y of column (x, z): open there, on ground it can lie on.</summary>
    private static bool Bank(VoxelWorld w, int x, int y, int z)
    {
        if (y < 1 || !w.InBounds(x, y, z) || w.Get(x, y, z) != BlockType.Air) return false;
        var below = w.Get(x, y - 1, z);
        return BlockInfo.CanStandOn(below) && !BlockInfo.IsLeaves(below);
    }

    /// <summary>
    /// Where a log lands in column (x, z), coming from level y next door: at y on open ground, a level up
    /// onto a step, or down a short drop. False if it cannot lie there.
    /// </summary>
    private static bool LogSpot(VoxelWorld w, int x, int y, int z, out int landY)
    {
        landY = y;
        if (!w.InBounds(x, y, z) || !w.InBounds(x, y + 1, z) || y < 1) return false;
        if (w.Get(x, y, z) != BlockType.Air)
        {
            if (!BlockInfo.CanStandOn(w.Get(x, y, z)) || BlockInfo.IsLeaves(w.Get(x, y, z))
                || w.Get(x, y + 1, z) != BlockType.Air) return false;
            landY = y + 1;
            return true;
        }
        for (int drop = 0; drop <= MaxLogDrop && landY >= 1; drop++, landY--)
        {
            var below = w.Get(x, landY - 1, z);
            if (below == BlockType.Air) continue;
            return BlockInfo.CanStandOn(below) && !BlockInfo.IsLeaves(below);
        }
        return false;
    }

    /// <summary>True if another trunk is close enough to this leaf block to hold it up.</summary>
    private static bool NearTrunk(VoxelWorld w, int x, int y, int z, int s = 1)
    {
        int r = 3 * s - 1;
        for (int dy = -3 * s; dy <= s; dy++)
        for (int dz = -r; dz <= r; dz++)
        for (int dx = -r; dx <= r; dx++)
            if (BlockInfo.IsTrunk(w.Get(x + dx, y + dy, z + dz))) return true;
        return false;
    }

    /// <summary>
    /// True if a unit standing at level <paramref name="uy"/> in column (ux, uz) can work on levels
    /// [baseY, topY] of column (bx, bz): it stands in that column or next to it (diagonals too) and the
    /// target is between the block under its feet and the block above its head.
    /// </summary>
    public static bool InReach(int ux, int uy, int uz, int bx, int baseY, int topY, int bz)
    {
        if (Math.Abs(ux - bx) > 1 || Math.Abs(uz - bz) > 1) return false;
        if (ux == bx && uz == bz) return uy - 1 == topY || uy + Walk.Clearance == baseY;
        return topY >= uy - 1 && baseY <= uy + Walk.Clearance;
    }

    /// <summary>
    /// The standing spot a unit at (fromX, fromY, fromZ) should walk to in order to work on levels
    /// [baseY, topY] of column (bx, bz): the nearest one in reach, preferring a neighbouring column to
    /// standing on top of the target. False if there is nowhere to stand. With <paramref name="allowOnTop"/>
    /// false, only neighbouring columns count. With <paramref name="wayOut"/>, only spots a unit can walk out
    /// of again once the block at <paramref name="baseY"/> is dug away count (see <see cref="LeavesWayOut"/>):
    /// a worker never drops into a hole it cannot climb out of to get at a block.
    /// </summary>
    public static bool WorkSpot(VoxelWorld w, int bx, int baseY, int topY, int bz, int fromX, int fromY, int fromZ,
        out int sx, out int sy, out int sz, bool allowOnTop = true, bool wayOut = false)
    {
        sx = sy = sz = -1;
        long best = long.MaxValue;
        for (int oz = -1; oz <= 1; oz++)
        for (int ox = -1; ox <= 1; ox++)
        {
            if (!allowOnTop && ox == 0 && oz == 0) continue;
            int x = bx + ox, z = bz + oz;
            for (int y = baseY - Walk.Clearance; y <= topY + 1; y++)
            {
                if (!InReach(x, y, z, bx, baseY, topY, bz) || !Walk.CanStand(w, x, y, z)) continue;
                long dx = x - fromX, dz = z - fromZ, dy = y - fromY;
                long score = dx * dx + dz * dz + 2 * dy * dy + (ox == 0 && oz == 0 ? 64 : 0);
                if (score >= best) continue;
                if (wayOut && !LeavesWayOut(w, bx, baseY, bz, x, y, z)) continue;
                best = score;
                (sx, sy, sz) = (x, y, z);
            }
        }
        return best != long.MaxValue;
    }

    /// <summary>
    /// The nearest block of <paramref name="kind"/> around (x, y, z) that a worker can stand next to and that
    /// is not in <paramref name="claimed"/> (keys from <see cref="Key"/>): within <see cref="SearchRadius"/>
    /// for dug blocks, or <see cref="PlantSearchRadius"/> columns and <see cref="PlantSearchHeight"/> levels
    /// for trees and bushes. For trees it is the base of a trunk. Ties go to the higher block, so mounds are
    /// dug from the top. Dug resources (stone, ore, coal) open to the sky come before ones under a roof. They
    /// are only taken from beside, never from on top, and only when taking them knocks nothing loose and
    /// leaves a way out (see <see cref="LeavesWayOut"/>), so quarries do not become pits.
    /// </summary>
    public static bool FindNext(VoxelWorld w, BlockType kind, int x, int y, int z, HashSet<long> claimed,
        out int nx, out int ny, out int nz)
    {
        // Wood cutters clear the trees they felled before felling more.
        if (kind == BlockType.Wood && FindNext(w, BlockType.TreeRubble, x, y, z, claimed, openOnly: false, out nx, out ny, out nz))
            return true;
        // Quarries are worked open to the sky, a layer at a time, before workers tunnel under a roof.
        if (Quarried(kind) && FindNext(w, kind, x, y, z, claimed, openOnly: true, out nx, out ny, out nz)) return true;
        return FindNext(w, kind, x, y, z, claimed, openOnly: false, out nx, out ny, out nz);
    }

    private static bool FindNext(VoxelWorld w, BlockType kind, int x, int y, int z, HashSet<long> claimed,
        bool openOnly, out int nx, out int ny, out int nz)
    {
        bool plant = kind is BlockType.Wood or BlockType.BerryBush;
        int radius = plant ? PlantSearchRadius : SearchRadius, height = plant ? PlantSearchHeight : SearchRadius;
        var shell = new List<(int D2, int X, int Y, int Z)>();
        for (int r = 0; r <= radius; r++)
        {
            shell.Clear();
            int h = Math.Min(r, height);
            for (int dy = -h; dy <= h; dy++)
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Max(Math.Abs(dy), Math.Abs(dz))) != r) continue;
                int bx = x + dx, by = y + dy, bz = z + dz;
                if (kind == BlockType.Wood ? !BlockInfo.IsTrunk(w.Get(bx, by, bz)) : w.Get(bx, by, bz) != kind) continue;
                if (kind == BlockType.Wood && BlockInfo.IsTrunk(w.Get(bx, by - 1, bz))) continue; // not a trunk's base
                if (claimed.Contains(Key(w, bx, by, bz)) || !Exposed(w, bx, by, bz, kind)) continue;
                if (openOnly && BlockInfo.IsSolid(w.Get(bx, by + 1, bz))) continue;
                shell.Add((dx * dx + dy * dy + dz * dz, bx, by, bz));
            }
            shell.Sort((a, b) => a.D2 != b.D2 ? a.D2.CompareTo(b.D2) : a.Y != b.Y ? b.Y.CompareTo(a.Y)
                : a.Z != b.Z ? a.Z.CompareTo(b.Z) : a.X.CompareTo(b.X));
            foreach (var c in shell)
            {
                var (baseY, topY) = Span(w, c.X, c.Y, c.Z);
                bool quarry = Quarried(kind);
                if (!WorkSpot(w, c.X, baseY, topY, c.Z, x, y, z, out int sx, out int sy, out int sz, allowOnTop: !quarry))
                    continue;
                if (quarry && (!PhysicsSystem.HoldsWithout(w, c.X, c.Y, c.Z) || !LeavesWayOut(w, c.X, c.Y, c.Z, sx, sy, sz)))
                    continue;
                (nx, ny, nz) = (c.X, c.Y, c.Z);
                return true;
            }
        }
        nx = ny = nz = -1;
        return false;
    }

    /// <summary>
    /// Dug resources that workers move on through a block at a time and so can dig themselves into a pit.
    /// Trees, bushes and rubble (which only ever lowers a heap) are not checked.
    /// </summary>
    public static bool Quarried(BlockType b) => b is BlockType.Stone or BlockType.IronOre or BlockType.Coal;

    /// <summary>How far (columns) <see cref="LeavesWayOut"/> searches for open ground around a dug block.</summary>
    public const int WayOutRadius = 12;

    /// <summary>
    /// True if, with block (bx, by, bz) dug away, a unit standing at the work spot (sx, sy, sz), and one
    /// standing on the floor of the hole the block leaves, can still walk out to open ground: a spot
    /// <see cref="WayOutRadius"/> columns away, reached by steps of at most one level up or down (so the way
    /// works both in and out). Does not change the world.
    /// </summary>
    public static bool LeavesWayOut(VoxelWorld w, int bx, int by, int bz, int sx, int sy, int sz)
    {
        if (!Escapes(w, bx, by, bz, sx, sy, sz)) return false;
        // The hole: the floor of the dug column, however far down, must stay in reach too, so the steps
        // into a quarry are kept and the stone left at its bottom can still be got at.
        for (int y = by; y >= 1; y--)
        {
            if (StandsWithout(w, bx, by, bz, bx, y, bz)) return Escapes(w, bx, by, bz, bx, y, bz);
            if (y < by && !Walk.Passable(w.Get(bx, y, bz))) break;
        }
        return true; // nowhere to stand in the hole
    }

    /// <summary>
    /// True if a unit standing at (x, y, z) can walk out to open ground now (see <see cref="LeavesWayOut"/>) and
    /// could not with block (bx, by, bz) dug away. Does not change the world.
    /// </summary>
    public static bool ShutsIn(VoxelWorld w, int bx, int by, int bz, int x, int y, int z) =>
        !Escapes(w, bx, by, bz, x, y, z) && Escapes(w, bx, -1, bz, x, y, z); // level -1: nothing taken away

    /// <summary>Walk search from (x, y, z) with block (hx, hy, hz) taken away, out to <see cref="WayOutRadius"/>.</summary>
    private static bool Escapes(VoxelWorld w, int hx, int hy, int hz, int x, int y, int z)
    {
        if (!StandsWithout(w, hx, hy, hz, x, y, z)) return false;
        var seen = new HashSet<long> { Key(w, x, y, z) };
        var open = new Queue<(int X, int Y, int Z)>();
        open.Enqueue((x, y, z));
        while (open.Count > 0)
        {
            var (cx, cy, cz) = open.Dequeue();
            if (Math.Max(Math.Abs(cx - hx), Math.Abs(cz - hz)) >= WayOutRadius) return true;
            for (int d = 0; d < 4; d++)
            {
                int nx = cx + (d == 0 ? 1 : d == 1 ? -1 : 0), nz = cz + (d == 2 ? 1 : d == 3 ? -1 : 0);
                for (int dy = -1; dy <= 1; dy++)
                {
                    int ny = cy + dy;
                    if (!StandsWithout(w, hx, hy, hz, nx, ny, nz)) continue;
                    // A step between two levels needs headroom over the lower spot (see Walk.TryStep).
                    if (dy == 1 && GetWithout(w, hx, hy, hz, cx, cy + Walk.Clearance, cz) != BlockType.Air) continue;
                    if (dy == -1 && GetWithout(w, hx, hy, hz, nx, ny + Walk.Clearance, nz) != BlockType.Air) continue;
                    if (seen.Add(Key(w, nx, ny, nz))) open.Enqueue((nx, ny, nz));
                }
            }
        }
        return false;
    }

    private static BlockType GetWithout(VoxelWorld w, int hx, int hy, int hz, int x, int y, int z) =>
        x == hx && y == hy && z == hz ? BlockType.Air : w.Get(x, y, z);

    /// <summary><see cref="Walk.CanStand"/> with block (hx, hy, hz) taken away.</summary>
    private static bool StandsWithout(VoxelWorld w, int hx, int hy, int hz, int x, int y, int z)
    {
        if (y < 1 || !w.InBounds(x, y, z)) return false;
        return BlockInfo.IsSolid(GetWithout(w, hx, hy, hz, x, y - 1, z))
            && Walk.Passable(GetWithout(w, hx, hy, hz, x, y, z))
            && GetWithout(w, hx, hy, hz, x, y + 1, z) == BlockType.Air;
    }

    /// <summary>A block with open air (or water) beside it; one buried on all sides cannot be reached.</summary>
    internal static bool Exposed(VoxelWorld w, int x, int y, int z, BlockType kind)
    {
        if (kind == BlockType.Wood) return true; // trunks stand in the open
        return !BlockInfo.IsSolid(w.Get(x + 1, y, z)) || !BlockInfo.IsSolid(w.Get(x - 1, y, z))
            || !BlockInfo.IsSolid(w.Get(x, y + 1, z)) || !BlockInfo.IsSolid(w.Get(x, y - 1, z))
            || !BlockInfo.IsSolid(w.Get(x, y, z + 1)) || !BlockInfo.IsSolid(w.Get(x, y, z - 1));
    }

    /// <summary>A block's position as one number, for sets of claimed targets.</summary>
    public static long Key(VoxelWorld w, int x, int y, int z) => ((long)y * w.SizeZ + z) * w.SizeX + x;
}
