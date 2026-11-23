using System;
using System.Collections.Generic;
using Rts.Sim.Content;
using Rts.Sim.Units;

namespace Rts.Sim.Economy;

/// <summary>
/// The tools of Better tools (issue #146): aimed felling and prospecting.
///
/// <para><b>Aimed felling</b> (<see cref="FellCommand"/>). A unit whose player has the tech for it (see
/// <see cref="TechEffectType.Felling"/>: the Settlers' militia, other factions' workers more slowly) walks to the
/// tree, to the side away from where it is to fall, works at it a while and brings it down that way (see
/// <see cref="World.PhysicsSystem.FellTreeAimed"/>): it crushes the units and hits the buildings it lands on, and a
/// trunk landing across a narrow gap or stream lies there as a log bridge. Its wood lies where it fell.</para>
///
/// <para><b>Prospecting</b> (<see cref="ProspectCommand"/>). A unit whose player has the tech for it (see
/// <see cref="TechEffectType.Prospecting"/>: the Settlers' foreman, other factions' workers more slowly) walks to
/// the spot and sounds the ground there a while; then every ore, coal, gold and crystal block within the radius,
/// at any depth, shows to its player through the ground (<see cref="ProspectedOre"/>), and the ground round the
/// spot comes out of the fog as seen.</para>
/// </summary>
public sealed partial class EconomySystem
{
    /// <summary>Furthest (columns) from the spot ordered a tree to fell is looked for: a click on its crown finds it.</summary>
    public const int FellSearch = 2;

    /// <summary>Ticks of work for unit i to fell a tree the way it chooses; 0 if it cannot (no tech, or not a feller).</summary>
    public int FellTicksOf(int i) => Find(U.Owner[i])?.FellTicks[U.Def[i]] ?? 0;

    /// <summary>Ticks of work for unit i to sound the ground; 0 if it cannot (no tech, or not a prospector).</summary>
    public int ProspectTicksOf(int i) => Find(U.Owner[i])?.ProspectTicks[U.Def[i]] ?? 0;

    /// <summary>True if the player's units of this type can fell a tree the way they choose.</summary>
    public bool CanFell(int player, UnitDef def) => (Find(player)?.FellTicks[def.Index] ?? 0) > 0;

    /// <summary>True if the player's units of this type can sound the ground.</summary>
    public bool CanProspect(int player, UnitDef def) => (Find(player)?.ProspectTicks[def.Index] ?? 0) > 0;

    /// <summary>How far (columns) the player's prospectors' sounding shows ore; 0 without the tech.</summary>
    public int ProspectRadius(int player) => Find(player)?.ProspectRadius ?? 0;

    /// <summary>Damage the player's aimed trees do to each unit and building they come down on.</summary>
    public int FellCrush(int player) => Find(player)?.FellCrush ?? 0;

    /// <summary>True if the block is one prospecting finds: iron ore, coal, gold ore or crystal.</summary>
    public static bool IsProspected(BlockType b) => b is BlockType.IronOre or BlockType.Coal or BlockType.GoldOre or BlockType.Crystal;

    /// <summary>The key a found block is kept under (see <see cref="Harvest.Key"/>).</summary>
    public long OreKey(int x, int y, int z) => Harvest.Key(World, x, y, z);

    /// <summary>
    /// The blocks the player's prospectors have found that are still there (dug-out ones drop away), in key order:
    /// what the game shows through the ground.
    /// </summary>
    public List<(int X, int Y, int Z, BlockType Block)> ProspectedOre(int player)
    {
        var list = new List<(int, int, int, BlockType)>();
        var p = Find(player);
        if (p == null) return list;
        int sx = World.SizeX, sz = World.SizeZ;
        foreach (long k in p.Prospected)
        {
            int x = (int)(k % sx), z = (int)(k / sx % sz), y = (int)(k / ((long)sx * sz));
            var b = World.Get(x, y, z);
            if (IsProspected(b)) list.Add((x, y, z, b));
        }
        return list;
    }

    /// <summary>True if the player has found the block at (x, y, z) by prospecting (whether or not it is still there).</summary>
    public bool Prospected(int player, int x, int y, int z) => Find(player)?.Prospected.Contains(OreKey(x, y, z)) ?? false;

    /// <summary>Times the player has sounded the ground (for the computer players and the screenshot checks).</summary>
    public int Soundings(int player) => Find(player)?.Soundings ?? 0;

    // --- Felling -----------------------------------------------------------------------------------

    internal void Apply(FellCommand c)
    {
        if (c.DirX == 0 && c.DirZ == 0) return;
        if (!FindTree(c.X, c.Z, out int tx, out int baseY, out int tz)) return;
        int dir = Math.Abs(c.DirX) >= Math.Abs(c.DirZ) ? (c.DirX > 0 ? 0 : 1) : (c.DirZ > 0 ? 2 : 3);
        foreach (int i in OwnedUnits(c))
        {
            if (FellTicksOf(i) <= 0) continue;
            StartJob(i, UnitJob.Fell, dir);
            U.JobX[i] = tx;
            U.JobY[i] = baseY;
            U.JobZ[i] = tz;
        }
    }

    /// <summary>The way a fell job's tree falls (see <see cref="UnitJob.Fell"/>).</summary>
    public static (int DirX, int DirZ) FellDirection(int code) => code switch
    {
        0 => (1, 0),
        1 => (-1, 0),
        2 => (0, 1),
        _ => (0, -1),
    };

    /// <summary>
    /// The tree nearest column (x, z), within <see cref="FellSearch"/> columns (ties to the lowest z, then x): a trunk
    /// block's column and the level its trunk starts at. False if none.
    /// </summary>
    public bool FindTree(int x, int z, out int tx, out int baseY, out int tz)
    {
        tx = tz = baseY = -1;
        int best = int.MaxValue;
        for (int dz = -FellSearch; dz <= FellSearch; dz++)
        for (int dx = -FellSearch; dx <= FellSearch; dx++)
        {
            int cx = x + dx, cz = z + dz, d = dx * dx + dz * dz;
            if (d >= best || !World.InBounds(cx, 0, cz)) continue;
            for (int y = World.SizeY - 1; y >= 1; y--)
            {
                if (!BlockInfo.IsTrunk(World.Get(cx, y, cz))) continue;
                var (b, _) = Harvest.Span(World, cx, y, cz);
                if (b < 1 || BlockInfo.IsTrunk(World.Get(cx, b - 1, cz))) break;
                (best, tx, baseY, tz) = (d, cx, b, cz);
                break;
            }
        }
        return best != int.MaxValue;
    }

    private void StepFell(int i)
    {
        var u = U;
        int x = u.JobX[i], y = u.JobY[i], z = u.JobZ[i];
        if (!BlockInfo.IsTrunk(World.Get(x, y, z)))
        {
            EndJob(i); // felled (by another of the party) or gone
            return;
        }
        var (baseY, topY) = Harvest.Span(World, x, y, z);
        var (dirX, dirZ) = FellDirection(u.JobTarget[i]);
        int ux = u.X[i].FloorToInt(), uz = u.Z[i].FloorToInt();
        // In reach, and not on the side it falls to.
        bool there = Harvest.InReach(ux, u.Y[i], uz, x, baseY, topY, z) && (ux - x) * dirX + (uz - z) * dirZ <= 0;
        if (u.Phase[i] != JobPhase.Working)
        {
            switch (Approach(i, there))
            {
                case Travel.Arrived:
                    SetPhase(i, JobPhase.Working);
                    return;
                case Travel.NeedSpot:
                    // The nearest spot to the back of the tree, away from where it falls.
                    int bx = x - 3 * dirX, bz = z - 3 * dirZ;
                    if (Harvest.WorkSpot(World, x, baseY, topY, z, bx, baseY, bz, out int sx, out int sy, out int sz, allowOnTop: false)
                        && (sx - x) * dirX + (sz - z) * dirZ <= 0)
                    {
                        _sim.SendUnit(i, sx, sz, sy);
                        return;
                    }
                    break;
                case Travel.Walking:
                    return;
            }
            EndJob(i); // no way to it from behind
            return;
        }
        if (!there)
        {
            SetPhase(i, JobPhase.ToWork);
            return;
        }
        var (_, _, s) = Rts.Sim.World.Trees.Footprint(World, x, z);
        if (++u.JobTimer[i] < FellTicksOf(i) * s) return;
        _sim.Physics.FellTreeAimed(x, baseY, topY, z, dirX, dirZ, FellCrush(u.Owner[i]), u.Owner[i]);
        EndJob(i);
    }

    // --- Prospecting -------------------------------------------------------------------------------

    internal void Apply(ProspectCommand c)
    {
        if (!World.InBounds(c.X, 0, c.Z)) return;
        int y = Walk.GroundLevel(World, c.X, c.Z);
        foreach (int i in OwnedUnits(c))
        {
            if (ProspectTicksOf(i) <= 0) continue;
            StartJob(i, UnitJob.Prospect, 0);
            U.JobX[i] = c.X;
            U.JobY[i] = y;
            U.JobZ[i] = c.Z;
        }
    }

    private void StepProspect(int i)
    {
        var u = U;
        int x = u.JobX[i], z = u.JobZ[i];
        int ux = u.X[i].FloorToInt(), uz = u.Z[i].FloorToInt();
        bool there = Math.Abs(ux - x) <= 1 && Math.Abs(uz - z) <= 1;
        if (u.Phase[i] != JobPhase.Working)
        {
            switch (Approach(i, there))
            {
                case Travel.Arrived:
                    SetPhase(i, JobPhase.Working);
                    return;
                case Travel.NeedSpot:
                    _sim.SendUnit(i, x, z, u.JobY[i] > 0 ? u.JobY[i] : u.Y[i]);
                    return;
                case Travel.Walking:
                    return;
            }
            EndJob(i);
            return;
        }
        if (!there)
        {
            SetPhase(i, JobPhase.ToWork);
            return;
        }
        if (++u.JobTimer[i] < ProspectTicksOf(i)) return;
        int owner = u.Owner[i];
        Sound(owner, x, z);
        // The others sounding the same spot for the same player are done too.
        for (int j = 0; j < u.Count; j++)
            if (j != i && u.Owner[j] == owner && u.Job[j] == UnitJob.Prospect && u.JobX[j] == x && u.JobZ[j] == z) EndJob(j);
        EndJob(i);
    }

    /// <summary>
    /// The player sounds the ground at column (x, z): every ore, coal, gold and crystal block within their
    /// <see cref="ProspectRadius"/> columns, at any depth, is found, and the ground there comes out of the fog as seen.
    /// Returns the blocks newly found.
    /// </summary>
    internal int Sound(int player, int x, int z)
    {
        var p = Find(player);
        int r = p?.ProspectRadius ?? 0;
        if (p == null || r <= 0) return 0;
        p.Soundings++;
        int found = 0;
        for (int dz = -r; dz <= r; dz++)
        for (int dx = -r; dx <= r; dx++)
        {
            if (dx * dx + dz * dz > r * r) continue;
            int cx = x + dx, cz = z + dz;
            if (!World.InBounds(cx, 0, cz)) continue;
            for (int y = 0; y < World.SizeY; y++)
                if (IsProspected(World.Get(cx, y, cz)) && p.Prospected.Add(OreKey(cx, y, cz))) found++;
        }
        _sim.Vision.Reveal(player, x, z, r);
        _sim.Events.Prospected(player, x, Walk.GroundLevel(World, x, z), z, found);
        return found;
    }

    private void MixTools(ref ulong h)
    {
        foreach (var p in _players)
        {
            h = (h ^ (ulong)p.Soundings) * 1099511628211UL;
            h = (h ^ (ulong)p.Prospected.Count) * 1099511628211UL;
            foreach (long k in p.Prospected) h = (h ^ (ulong)k) * 1099511628211UL;
        }
    }
}
