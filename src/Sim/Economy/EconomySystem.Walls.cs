using System;
using System.Collections.Generic;
using Rts.Sim.Units;

namespace Rts.Sim.Economy;

/// <summary>
/// Walls and gates (<see cref="WallCommand"/>): a line of one-column sites, built by workers a column at a
/// time. Each column is its own building made of ordinary building blocks, so blows, sappers and tunnels
/// knock pieces out of a wall and what loses its support falls (see <see cref="CombatSystem"/>). Walls and
/// gates stand right next to each other, with no gap; a line steps diagonally where it must, which still
/// shuts the way, since nobody cuts a corner between two blocked columns.
///
/// Walls come in tiers (<see cref="BuildingDef.Upgrade"/>): the one that is laid, and those it is upgraded to
/// where it stands (<see cref="UpgradeCommand"/>). An upgrade is paid for when it is ordered and is builders'
/// work like a site; the wall stands as it was until the work is done, and then it is the new building, whole.
///
/// Ranged units are posted on top of a wall or gate that has a walkway (<see cref="PostCommand"/>, one to a
/// column): they walk to its foot and climb up. A unit standing on a wall holds its ground, and comes down
/// when an order takes it somewhere else (see <see cref="UnitMovement"/>).
/// </summary>
public sealed partial class EconomySystem
{
    /// <summary>Most columns one wall command lays.</summary>
    public const int MaxWall = 64;
    /// <summary>How far (columns) a worker who finished a stretch of wall looks for the next one to build.</summary>
    public const int WallReach = 8;

    /// <summary>
    /// The columns of a straight line from (x1, z1) to (x2, z2), at most <see cref="MaxWall"/> of them: the
    /// line a wall is laid along (the game previews the same one).
    /// </summary>
    public static List<(int X, int Z)> WallLine(int x1, int z1, int x2, int z2)
    {
        var list = new List<(int X, int Z)>();
        int x = x1, z = z1;
        int dx = Math.Abs(x2 - x1), dz = -Math.Abs(z2 - z1);
        int sx = x1 < x2 ? 1 : -1, sz = z1 < z2 ? 1 : -1;
        int err = dx + dz;
        while (list.Count < MaxWall)
        {
            list.Add((x, z));
            if (x == x2 && z == z2) break;
            int e2 = 2 * err;
            if (e2 >= dz) { err += dz; x += sx; }
            if (e2 <= dx) { err += dx; z += sz; }
        }
        return list;
    }

    internal void Apply(WallCommand c)
    {
        if (!_sim.Content.TryGetBuilding(c.Building, out var def) || !def.IsWall) return;
        if (def.SizeX != 1 || def.SizeZ != 1) return; // a line is laid a column at a time
        if (def.UpgradeOf != null) return; // a higher tier is not laid: the wall it comes from is, and then upgraded
        var sites = new List<Building>();
        foreach (var (x, z) in WallLine(c.X1, c.Z1, c.X2, c.Z2))
        {
            if (Buildings.AtColumn(x, z) != null) continue; // a gate (or wall) already there stays
            var result = CanPlace(c.PlayerId, def, x, z, out int baseY);
            if (result == PlaceResult.CannotAfford) break;
            if (result != PlaceResult.Ok || !TrySpend(c.PlayerId, def.Cost, x, z)) continue;
            sites.Add(Prepare(c.PlayerId, def, x, z, baseY));
        }
        if (sites.Count == 0) return;
        // Spread the workers along the line: each starts on its share of it, and moves on from there.
        var workers = Workers(c);
        for (int k = 0; k < workers.Count; k++)
            StartJob(workers[k], UnitJob.Build, sites[k * sites.Count / workers.Count].Id);
    }

    /// <summary>
    /// How far (columns) a builder who has finished a column of wall goes on to a facing that waits for its
    /// builders: further than <see cref="WallReach"/>, as nobody laid the facings out (they come with a stone
    /// wall when it is done, see <see cref="FinishUpgrade"/>), so nobody was sent to each of them.
    /// </summary>
    public const int FacingReach = 32;

    /// <summary>
    /// The player's wall or gate site (or one being upgraded) nearest to column (x, z), within
    /// <see cref="WallReach"/> of it, with the fewest workers on it (ties to the nearest, then the lowest id).
    /// With none that near, the nearest facing site within <see cref="FacingReach"/> (the lowest id of those as
    /// near); null if there is none.
    /// </summary>
    private Building? NextWallSite(int player, int x, int z)
    {
        Building? best = null, facing = null;
        int bestWorkers = 0, bestD = 0, facingD = 0;
        foreach (var b in Buildings.All)
        {
            if (b.Owner != player || !b.WantsBuilders || !b.Def.IsWall) continue;
            int d = b.ColumnDistance(x, z);
            if (d > WallReach)
            {
                if (b.Def.FacingOf != null && d <= FacingReach && (facing == null || d < facingD)) (facing, facingD) = (b, d);
                continue;
            }
            int workers = WorkersAt(b, UnitJob.Build);
            if (best != null && (workers > bestWorkers || (workers == bestWorkers && d >= bestD))) continue;
            (best, bestWorkers, bestD) = (b, workers, d);
        }
        return best ?? facing;
    }

    // --- Upgrades ----------------------------------------------------------------------------------

    /// <summary>Most walls and gates one upgrade command starts on.</summary>
    public const int MaxUpgrade = 256;

    /// <summary>
    /// Why the building cannot be upgraded now: <see cref="PlaceResult.Ok"/> if it can. Blocked: it has no
    /// upgrade, is not finished, is being upgraded already, or something is in the way above it. Locked: its
    /// owner lacks a tech the upgrade needs. A faced wall of the last tier that has lost some of its facing
    /// counts as one that can be upgraded: the order puts the facing back, for the facing's price a column.
    /// </summary>
    public PlaceResult CanUpgrade(Building b)
    {
        var to = b.Def.Upgrade;
        if (!b.Active || b.Upgrade != null) return PlaceResult.Blocked;
        if (to == null)
        {
            // There is nothing more for it to become, but a faced wall that has lost facing can have it put back.
            if (b.Def.Facing is not { } facing || MissingFacings(b).Count == 0) return PlaceResult.Blocked;
            return CanAfford(b.Owner, facing.Cost) ? PlaceResult.Ok : PlaceResult.CannotAfford;
        }
        if (!Unlocked(b.Owner, to)) return PlaceResult.Locked;
        if (!RoomFor(b, to)) return PlaceResult.Blocked;
        return CanAfford(b.Owner, to.Cost) ? PlaceResult.Ok : PlaceResult.CannotAfford;
    }

    /// <summary>True if the blocks of <paramref name="to"/> above what the building is now have room: air or leaves (units step up).</summary>
    private bool RoomFor(Building b, BuildingDef to)
    {
        int top = b.Y + Math.Max(b.Def.Height, 1);
        if (b.Y + to.Height + Walk.Clearance >= World.SizeY) return false;
        foreach (var t in to.Blocks)
        {
            int y = b.Y + t.Y;
            if (y < top) continue;
            var there = World.Get(b.X + t.X, y, b.Z + t.Z);
            if (there != BlockType.Air && !BlockInfo.IsLeaves(there)) return false;
        }
        return true;
    }

    internal void Apply(UpgradeCommand c)
    {
        var first = Buildings.Get(c.Building);
        if (first == null || first.Owner != c.PlayerId || !first.Def.IsRampart || first.Planned) return;
        var started = new List<Building>();
        foreach (var b in c.Stretch && first.Def.IsWall ? Stretch(first) : new List<Building> { first })
        {
            var result = CanUpgrade(b);
            if (result == PlaceResult.CannotAfford) break;
            if (result != PlaceResult.Ok) continue;
            if (b.Def.Upgrade == null)
            {
                // The facing it has lost goes up again, a column at a time as the goods allow.
                foreach (var (x, z) in MissingFacings(b))
                {
                    if (!TrySpend(c.PlayerId, b.Def.Facing!.Cost, x, z)) break;
                    started.Add(LayFacing(b, x, z));
                }
                continue;
            }
            if (!TrySpend(c.PlayerId, b.Def.Upgrade.Cost, b.X, b.Z)) continue;
            b.Upgrade = b.Def.Upgrade;
            b.UpgradeWork = 0;
            started.Add(b);
        }
        if (started.Count == 0) return;
        var workers = Workers(c);
        for (int k = 0; k < workers.Count; k++)
            StartJob(workers[k], UnitJob.Build, started[k * started.Count / workers.Count].Id);
    }

    /// <summary>
    /// The owner's walls and gates joined to the building (side by side or corner to corner, by way of walls
    /// and gates of any tier) that are of its tier, itself first and then nearest first along the wall, at most
    /// <see cref="MaxUpgrade"/> of them.
    /// </summary>
    public List<Building> Stretch(Building first)
    {
        var result = new List<Building>();
        if (!first.Def.IsWall) return result;
        // Looked up by column only, never walked through, so the order of the dictionary does not matter.
        var at = new Dictionary<long, Building>();
        foreach (var b in Buildings.All)
            if (b.Owner == first.Owner && b.Def.IsWall && !b.Planned) at[Column(b.X, b.Z)] = b;
        var seen = new HashSet<int> { first.Id };
        var queue = new Queue<Building>();
        queue.Enqueue(first);
        while (queue.Count > 0 && result.Count < MaxUpgrade)
        {
            var b = queue.Dequeue();
            if (b.Def.Tier == first.Def.Tier && b.Def.FacingOf == first.Def.FacingOf) result.Add(b);
            foreach (var (dx, dz) in WalkGraph.Dirs)
                if (at.TryGetValue(Column(b.X + dx, b.Z + dz), out var next) && seen.Add(next.Id)) queue.Enqueue(next);
        }
        return result;
    }

    private static long Column(int x, int z) => ((long)z << 32) | (uint)x;

    /// <summary>A building whose upgrade has had all its work becomes the new building (see <see cref="FinishUpgrade"/>).</summary>
    private void StepUpgrade(Building b)
    {
        if (b.UpgradeWork >= b.Upgrade!.BuildTicks) FinishUpgrade(b);
    }

    /// <summary>
    /// The upgrade is done: the building's blocks are the new building's, all of them (so it is whole again,
    /// whatever had been knocked out of it), and it is that building from now on. It waits while something
    /// solid is in the way above it. A unit standing where a block goes steps up onto it (see <see cref="UnitMovement"/>).
    /// </summary>
    private void FinishUpgrade(Building b)
    {
        var to = b.Upgrade!;
        if (!RoomFor(b, to)) return;
        foreach (var t in to.Blocks) World.Set(b.X + t.X, b.Y + t.Y, b.Z + t.Z, t.Block);
        b.Def = to;
        b.Placed = to.Blocks.Count;
        b.Work = to.BuildTicks;
        b.Lost = b.Damage = 0;
        b.Gone = null;
        b.Upgrade = null;
        b.UpgradeWork = 0;
        _sim.Events.BuildingFinished(b);
        // A faced wall: its facing goes up beside it, paid for with the upgrade, as sites for its builders.
        if (to.Facing != null)
            foreach (var (x, z) in MissingFacings(b)) LayFacing(b, x, z);
    }

    // --- Facings: walls three columns thick --------------------------------------------------------

    /// <summary>
    /// True if the owner has a wall or gate of a line itself (not a facing, not a plan) on column (x, z), or a
    /// tower, which a line runs into.
    /// </summary>
    private bool InLine(int owner, int x, int z) =>
        Buildings.AtColumn(x, z) is { } o && o.Owner == owner && o.Def.IsRampart && o.Def.FacingOf == null && !o.Planned;

    /// <summary>
    /// The columns beside a wall or gate of a line that lie across the way the line runs through it: the two
    /// along Z if the line has a wall next to it along X, and the other way round; both ways where the line
    /// turns or steps diagonally, with the corner outside a turn. Columns of the line itself are left out, and
    /// a wall that stands alone has none. This is where a faced wall's facing goes (see <see cref="FacingCells"/>),
    /// and, for a gate, the way through it.
    /// </summary>
    public List<(int X, int Z)> Sides(Building b)
    {
        var cells = new List<(int X, int Z)>();
        if (!b.Def.IsWall || b.Def.SizeX != 1 || b.Def.SizeZ != 1) return cells;
        int o = b.Owner, x = b.X, z = b.Z;
        bool w = InLine(o, x - 1, z), e = InLine(o, x + 1, z), n = InLine(o, x, z - 1), s = InLine(o, x, z + 1);
        bool alongX = w || e, alongZ = n || s;
        if (!alongX && !alongZ)
        {
            // A line that steps diagonally here is thick both ways.
            if (!InLine(o, x - 1, z - 1) && !InLine(o, x + 1, z - 1) && !InLine(o, x - 1, z + 1) && !InLine(o, x + 1, z + 1)) return cells;
            alongX = alongZ = true;
        }
        bool fn = alongX && !n, fs = alongX && !s, fw = alongZ && !w, fe = alongZ && !e;
        if (fn) cells.Add((x, z - 1));
        if (fw) cells.Add((x - 1, z));
        if (fe) cells.Add((x + 1, z));
        if (fs) cells.Add((x, z + 1));
        if (fn && fw && !InLine(o, x - 1, z - 1)) cells.Add((x - 1, z - 1));
        if (fn && fe && !InLine(o, x + 1, z - 1)) cells.Add((x + 1, z - 1));
        if (fs && fw && !InLine(o, x - 1, z + 1)) cells.Add((x - 1, z + 1));
        if (fs && fe && !InLine(o, x + 1, z + 1)) cells.Add((x + 1, z + 1));
        return cells;
    }

    /// <summary>How far (columns) out from a gate, straight across its line, the way through it is kept free of facings.</summary>
    public const int GateLane = 2;

    /// <summary>
    /// The columns where the facing of a wall or gate goes (see <see cref="BuildingDef.Facing"/>): its
    /// <see cref="Sides"/>, but a wall never faces into the way through a gate of the line near it: the
    /// columns straight out from the gate across its line, <see cref="GateLane"/> of them each way (the first
    /// is the gate's own to face, as a gatehouse; the next keeps the way out of it open where the wall turns
    /// right beside the gate).
    /// </summary>
    public List<(int X, int Z)> FacingCells(Building b)
    {
        var cells = Sides(b);
        if (b.Def.Kind == BuildingKind.Gate) return cells;
        cells.RemoveAll(c =>
        {
            for (int dz = -GateLane; dz <= GateLane; dz++)
            for (int dx = -GateLane; dx <= GateLane; dx++)
            {
                var g = Buildings.AtColumn(c.X + dx, c.Z + dz);
                if (g == null || g == b || g.Owner != b.Owner || g.Def.Kind != BuildingKind.Gate || g.Def.FacingOf != null || g.Planned) continue;
                foreach (var (sx, sz) in Sides(g))
                {
                    if (sx != g.X && sz != g.Z)
                    {
                        if ((sx, sz) == c) return true; // the corner outside a turn the gate stands in
                        continue;
                    }
                    for (int k = 1; k <= GateLane; k++)
                        if ((g.X + (sx - g.X) * k, g.Z + (sz - g.Z) * k) == c) return true;
                }
            }
            return false;
        });
        return cells;
    }

    /// <summary>
    /// The columns of a finished, faced wall's facing that are not there and could be put up now: free, with
    /// ground a facing can stand on at about the wall's level, and not crowding another building.
    /// </summary>
    public List<(int X, int Z)> MissingFacings(Building b)
    {
        var missing = new List<(int X, int Z)>();
        if (b.Def.Facing is not { } facing || !b.Active) return missing;
        foreach (var (x, z) in FacingCells(b))
            if (Buildings.AtColumn(x, z) == null && CanPlace(b.Owner, facing, x, z, out int y, checkPlayer: false) == PlaceResult.Ok
                && Math.Abs(y - b.Y) <= 1) missing.Add((x, z));
        return missing;
    }

    /// <summary>A site of the wall's facing on column (x, z) (one of its <see cref="MissingFacings"/>), for its builders.</summary>
    private Building LayFacing(Building b, int x, int z)
    {
        CanPlace(b.Owner, b.Def.Facing!, x, z, out int y, checkPlayer: false);
        return Prepare(b.Owner, b.Def.Facing!, x, z, y);
    }

    // --- Posts on the walls ------------------------------------------------------------------------

    /// <summary>How far (columns) from the wall named in a post order its units look for a free column of walkway.</summary>
    public const int PostReach = 12;
    /// <summary>Ticks a unit takes to climb each level of a wall.</summary>
    public const int ClimbTicks = Tps / 2;

    /// <summary>How far (columns) from a post the foot of its wall may be: a unit climbs up one column of a thick wall and crosses the deck.</summary>
    public const int FootReach = 2;

    /// <summary>
    /// The wall, gate or tower unit i stands on top of, or null: the building whose block is under its feet. A
    /// unit up there holds its ground (see <see cref="CombatSystem.Holding"/>).
    /// </summary>
    public Building? WallUnder(int i)
    {
        int x = U.X[i].FloorToInt(), y = U.Y[i] - 1, z = U.Z[i].FloorToInt();
        // The block first: almost every unit stands on plain ground, and that is settled without a look at the buildings.
        if (World.Get(x, y, z) is not (BlockType.Planks or BlockType.StoneBrick or BlockType.Palisade or BlockType.Gate or BlockType.Rampart or BlockType.Trestle)) return null;
        var b = Buildings.At(x, y, z);
        return b != null && b.Def.IsRampart ? b : null;
    }

    /// <summary>
    /// True if a unit can be posted on column (x, z) of the building now: it is finished and has a walkway, the
    /// column's top is whole with nobody on it, and it is at the edge of the deck, where the battlements are (a
    /// side with nothing as high beside it).
    /// </summary>
    public bool PostFree(Building b, int x, int z)
    {
        if (!b.Def.Walkway || !b.Active || !b.Covers(x, z)) return false;
        int top = b.Y + b.Def.Height;
        if (World.Get(x, top - 1, z) != b.Def.Blocks[^1].Block || !Walk.CanStand(World, x, top, z) || Occupied(x, top, z)) return false;
        return !BlockInfo.IsSolid(World.Get(x - 1, top - 1, z)) || !BlockInfo.IsSolid(World.Get(x + 1, top - 1, z))
            || !BlockInfo.IsSolid(World.Get(x, top - 1, z - 1)) || !BlockInfo.IsSolid(World.Get(x, top - 1, z + 1));
    }

    /// <summary>True if a unit can be posted somewhere on the building now (see <see cref="PostFree(Building, int, int)"/>).</summary>
    public bool PostFree(Building b) => FreePosts(b) > 0;

    /// <summary>How many of the building's columns a unit can be posted on now.</summary>
    public int FreePosts(Building b)
    {
        int n = 0;
        if (!b.Def.Walkway || !b.Active) return n;
        for (int z = b.Z; z <= b.MaxZ; z++)
        for (int x = b.X; x <= b.MaxX; x++)
            if (PostFree(b, x, z)) n++;
        return n;
    }

    internal void Apply(PostCommand c)
    {
        var wall = Buildings.Get(c.Building);
        if (wall == null || wall.Owner != c.PlayerId || !wall.Def.Walkway || wall.Planned) return;
        var units = OwnedUnits(c);
        units.RemoveAll(i => !CanPost(i));
        if (units.Count == 0) return;
        // The posts around the one named, nearest to its middle first: every column of walkway within reach.
        int mx = wall.X + wall.MaxX, mz = wall.Z + wall.MaxZ; // its middle, in half blocks
        long Far(int x, int z) { long dx = 2L * x - mx, dz = 2L * z - mz; return dx * dx + dz * dz; }
        var places = new List<(Building B, int X, int Z)>();
        var columns = new HashSet<long>();
        foreach (var b in Buildings.All)
        {
            if (b.Owner != c.PlayerId || !b.Def.Walkway || !b.Active) continue;
            for (int z = b.Z; z <= b.MaxZ; z++)
            for (int x = b.X; x <= b.MaxX; x++)
                if (wall.ColumnDistance(x, z) <= PostReach && columns.Add(Column(x, z))) places.Add((b, x, z));
        }
        places.Sort((p, q) => Far(p.X, p.Z) != Far(q.X, q.Z) ? Far(p.X, p.Z).CompareTo(Far(q.X, q.Z))
            : p.B.Id != q.B.Id ? p.B.Id.CompareTo(q.B.Id) : p.Z != q.Z ? p.Z.CompareTo(q.Z) : p.X.CompareTo(q.X));
        // Posts somebody else is on their way to, and those the command's own units already stand on (they stay).
        var taken = new HashSet<long>();
        for (int j = 0; j < U.Count; j++)
            if (U.Job[j] == UnitJob.Post && !units.Contains(j)) taken.Add(Column(U.JobX[j], U.JobZ[j]));
        var waiting = new List<int>();
        foreach (int i in units)
        {
            long here = Column(U.X[i].FloorToInt(), U.Z[i].FloorToInt());
            if (U.Job[i] == UnitJob.None && WallUnder(i) != null && columns.Contains(here)) taken.Add(here);
            else waiting.Add(i);
        }
        int next = 0;
        foreach (int i in waiting)
        {
            while (next < places.Count && (taken.Contains(Column(places[next].X, places[next].Z))
                || !PostFree(places[next].B, places[next].X, places[next].Z))) next++;
            if (next == places.Count) break;
            var (b, x, z) = places[next++];
            StartJob(i, UnitJob.Post, b.Id);
            U.JobX[i] = x;
            U.JobZ[i] = z;
        }
    }

    /// <summary>True if unit i can be posted on a wall: a ranged unit that fights (see <see cref="UnitDef.IsRanged"/>).</summary>
    public bool CanPost(int i)
    {
        var def = Content.Units[U.Def[i]];
        return def.CanAttack && def.IsRanged;
    }

    /// <summary>
    /// True if column (x, z) is at the foot of the rampart that post (px, pz) of the building is on: open ground
    /// right beside a wall, gate or tower of its owner's that is within <see cref="FootReach"/> columns of the
    /// post. From there a unit climbs up and crosses the deck to the post, and comes down the same way.
    /// </summary>
    private bool IsFoot(Building b, int px, int pz, int x, int z)
    {
        if (Math.Max(Math.Abs(x - px), Math.Abs(z - pz)) > FootReach + 1 || Buildings.AtColumn(x, z) != null) return false;
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int cx = x + dx, cz = z + dz;
            if ((dx == 0 && dz == 0) || Math.Max(Math.Abs(cx - px), Math.Abs(cz - pz)) > FootReach) continue;
            if (Buildings.AtColumn(cx, cz) is { } r && r.Owner == b.Owner && r.Def.IsRampart && !r.Planned) return true;
        }
        return false;
    }

    /// <summary>
    /// The place at the foot of the rampart under post (px, pz) of the building (see <see cref="IsFoot"/>) that
    /// is nearest to column (nearX, nearZ), where a unit can stand; false if there is none.
    /// </summary>
    internal bool Foot(Building b, int px, int pz, int nearX, int nearZ, out int sx, out int sz, out int sy)
    {
        sx = sz = sy = -1;
        long best = long.MaxValue;
        for (int cz = pz - FootReach - 1; cz <= pz + FootReach + 1; cz++)
        for (int cx = px - FootReach - 1; cx <= px + FootReach + 1; cx++)
        {
            long dx = cx - nearX, dz = cz - nearZ, score = dx * dx + dz * dz;
            if (score >= best || !World.InBounds(cx, 0, cz)) continue;
            int y = Walk.NearestStandLevel(World, cx, cz, b.Y, 2);
            if (y < 0 || !IsFoot(b, px, pz, cx, cz)) continue;
            best = score;
            (sx, sz, sy) = (cx, cz, y);
        }
        return best != long.MaxValue;
    }

    private void StepPost(int i)
    {
        var u = U;
        var b = Buildings.Get(u.JobTarget[i]);
        int px = u.JobX[i], pz = u.JobZ[i];
        if (b == null || b.Owner != u.Owner[i] || !PostFree(b, px, pz))
        {
            EndJob(i); // the wall is gone or broken, or somebody else stands there now
            return;
        }
        bool there = Math.Abs(u.Y[i] - b.Y) <= 2 && IsFoot(b, px, pz, u.X[i].FloorToInt(), u.Z[i].FloorToInt());
        if (u.Phase[i] == JobPhase.ToWork)
        {
            switch (Approach(i, there))
            {
                case Travel.Arrived:
                    SetPhase(i, JobPhase.Working);
                    break;
                case Travel.NeedSpot:
                    // The foot of the wall on the unit's side of it.
                    if (Foot(b, px, pz, u.X[i].FloorToInt(), u.Z[i].FloorToInt(), out int x, out int z, out int y))
                        _sim.SendUnit(i, x, z, y);
                    else EndJob(i);
                    break;
                case Travel.Failed:
                    EndJob(i);
                    break;
            }
            return;
        }
        if (!there)
        {
            SetPhase(i, JobPhase.ToWork);
            return;
        }
        if (++u.JobTimer[i] < ClimbTicks * b.Def.Height) return;
        // Up: it stands in the middle of the column, on top.
        u.X[i] = Fix64.FromInt(px) + Fix64.Half;
        u.Z[i] = Fix64.FromInt(pz) + Fix64.Half;
        u.Y[i] = b.Y + b.Def.Height;
        EndJob(i);
    }
}
