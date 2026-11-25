using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.AI;

// Hard's watchtowers (a faction it plays balanced, so a town with a wall: see AiPlayer.HardWall.cs). Once the line of
// its wall is laid all round, it puts watchtowers onto it, out of the wood it can spare: first one on each side of the
// gate nearest the enemy, then one on each corner, the corner nearest the enemy first, KeepTowers in all. Each goes
// over the finished wall columns under it (see EconomySystem.TakesPlaceOf) and is built by its automatic workers like
// any site, one at a time. A tower sees far through the fog, and its archers by that gate are posted on it first (see
// PostGuard), where they aim better. A tower knocked down is put up again the next time it looks at its wall.
public sealed partial class AiPlayer
{
    /// <summary>Hard, balanced: watchtowers it keeps on its wall: two by the gate toward the enemy and one on each corner.</summary>
    public const int KeepTowers = 6;
    /// <summary>
    /// Hard, balanced: how far (columns) along its wall a tower may slide from where it wants it, when something
    /// stands in the way there (a building too near the line inside it, say).
    /// </summary>
    public const int TowerSlide = 2;

    /// <summary>Hard: watchtower sites it has laid out on its wall (counted once each).</summary>
    public int TowersLaid { get; private set; }

    /// <summary>
    /// Where Hard wants its towers, in order, each as the column the tower's middle stands on and the way along
    /// the wall it may slide (dx, dz), or (0, 0) for a corner, which may slide along either side: the two beside
    /// the gate nearest the enemy (sliding away from the gate), then the corners, the one nearest the enemy first.
    /// </summary>
    private List<(int X, int Z, int Dx, int Dz)> TowerSpots((int X, int Z) enemy)
    {
        var spots = new List<(int X, int Z, int Dx, int Dz)>();
        if (_ring is not var (x0, z0, x1, z1) || _gates.Count == 0) return spots;
        var gate = _gates[0];
        foreach (var g in _gates)
            if (Dist2(g.X, g.Z, enemy.X, enemy.Z) < Dist2(gate.X, gate.Z, enemy.X, enemy.Z)) gate = g;
        // Just past the gate's columns: the tower's own columns run right up to the gate's.
        int off = KeepGate / 2 + 2;
        foreach (int s in new[] { -1, 1 })
            spots.Add(gate.AlongX ? (gate.X + s * off, gate.Z, s, 0) : (gate.X, gate.Z + s * off, 0, s));
        var corners = new List<(int X, int Z)> { (x0, z0), (x1, z0), (x0, z1), (x1, z1) };
        corners.Sort((a, b) =>
        {
            long da = Dist2(a.X, a.Z, enemy.X, enemy.Z), db = Dist2(b.X, b.Z, enemy.X, enemy.Z);
            return da != db ? da.CompareTo(db) : a.X != b.X ? a.X.CompareTo(b.X) : a.Z.CompareTo(b.Z);
        });
        foreach (var (x, z) in corners) spots.Add((x, z, 0, 0));
        return spots;
    }

    /// <summary>
    /// Hard, balanced: puts its next watchtower onto its wall (see the top of this file). Called from
    /// <see cref="HardWall"/> once no column of the line is missing. It waits while a tower of its own is still
    /// going up, and it pays out of what it can spare (see <see cref="SpareForWall"/>): soldiers, houses and
    /// research come first.
    /// </summary>
    private void Towers(Simulation sim, (int X, int Z) enemy, Action<Command> issue)
    {
        var def = FirstBuilding(sim, BuildingKind.Tower);
        if (def == null) return;
        foreach (var b in sim.Buildings.All)
            if (b.Owner == Player && b.Def.IsTower && !b.Active) return; // one at a time
        var stock = sim.Economy.Stockpile(Player);
        int k = 0;
        foreach (var (sx, sz, dx, dz) in TowerSpots(enemy))
        {
            if (k++ >= KeepTowers) break;
            if (TowerOn(sim, sx, sz) != null) continue;
            if (!SpareForWall(stock, def.Cost)) return;
            // Where it wants it, else slid along the wall a column or two (either way along a side, at a corner).
            var tries = new List<(int X, int Z)> { (sx, sz) };
            for (int s = 1; s <= TowerSlide; s++)
            {
                if (dx != 0 || dz != 0) tries.Add((sx + s * dx, sz + s * dz));
                else
                {
                    int ix = _ring!.Value.X0 == sx ? 1 : -1, iz = _ring!.Value.Z0 == sz ? 1 : -1;
                    tries.Add((sx + s * ix, sz));
                    tries.Add((sx, sz + s * iz));
                }
            }
            foreach (var (tx, tz) in tries)
            {
                int cx = tx - def.SizeX / 2, cz = tz - def.SizeZ / 2;
                if (!WallReadyUnder(sim, def, cx, cz)) continue;
                if (sim.Economy.CanPlace(Player, def, cx, cz, out _) != PlaceResult.Ok) continue;
                issue(new BuildCommand(Player, Array.Empty<int>(), def.Id, cx, cz));
                Spend(def.Cost);
                TowersLaid++;
                Note(sim, $"puts a {def.Name.ToLowerInvariant()} onto its wall at {tx},{tz}");
                return;
            }
        }
    }

    /// <summary>A tower of its own (finished or not) covering column (x, z), or null.</summary>
    private Building? TowerOn(Simulation sim, int x, int z)
    {
        foreach (var b in sim.Buildings.All)
            if (b.Owner == Player && b.Def.IsTower && b.Covers(x, z)) return b;
        return null;
    }

    /// <summary>
    /// True if every column of the line of its wall under a tower with its corner at (x, z) holds a finished wall
    /// of its own that is not being upgraded: the tower is put onto the wall whole, not onto a gap or a site.
    /// </summary>
    private bool WallReadyUnder(Simulation sim, BuildingDef def, int x, int z)
    {
        if (_ring is not var (x0, z0, x1, z1)) return false;
        bool any = false;
        for (int cz = z; cz < z + def.SizeZ; cz++)
        for (int cx = x; cx < x + def.SizeX; cx++)
        {
            bool onLine = ((cx == x0 || cx == x1) && cz >= z0 && cz <= z1) || ((cz == z0 || cz == z1) && cx >= x0 && cx <= x1);
            if (!onLine) continue;
            var b = sim.Buildings.AtColumn(cx, cz);
            if (b == null || b.Owner != Player || b.Def.Kind != BuildingKind.Wall || !b.Active || b.Upgrade != null) return false;
            any = true;
        }
        return any;
    }

    /// <summary>Its finished towers that can be stood on beside the gate at (gx, gz) (see <see cref="TowerSpots"/>), lowest id first.</summary>
    private List<Building> GateTowers(Simulation sim, int gx, int gz)
    {
        var list = new List<Building>();
        foreach (var b in sim.Buildings.All)
            if (b.Owner == Player && b.Def.IsTower && b.Active && b.Def.Walkway && b.ColumnDistance(gx, gz) <= KeepGate + TowerSlide) list.Add(b);
        list.Sort((a, b) => a.Id.CompareTo(b.Id));
        return list;
    }

    /// <summary>
    /// Its ranged soldiers standing on its wall (not a tower) within reach of a tower by the gate go up that tower
    /// while it has free posts, the nearest first: they aim better from up there (see <see cref="PostGuard"/>).
    /// </summary>
    private void UpTheTowers(Simulation sim, List<Building> towers, Action<Command> issue)
    {
        var u = sim.Units;
        var econ = sim.Economy;
        var sent = new List<int>();
        foreach (var t in towers)
        {
            int free = econ.FreePosts(t);
            for (int i = 0; i < u.Count; i++)
                if (u.Owner[i] == Player && u.Job[i] == UnitJob.Post && u.JobTarget[i] == t.Id) free--;
            if (free <= 0) continue;
            var up = new List<(long D, int Id)>();
            for (int i = 0; i < u.Count; i++)
            {
                if (u.Owner[i] != Player || u.Job[i] != UnitJob.None || u.Rout[i] != 0 || u.Target[i] != 0) continue;
                if (econ.WallUnder(i) is not { } on || on.Def.IsTower) continue;
                int x = u.X[i].FloorToInt(), z = u.Z[i].FloorToInt();
                if (t.ColumnDistance(x, z) > EconomySystem.PostReach || sent.Contains(u.Id[i]) || HoldingGate(sim, x, z)) continue;
                up.Add((Dist2(x, z, t.X + t.Def.SizeX / 2, t.Z + t.Def.SizeZ / 2), u.Id[i]));
            }
            if (up.Count == 0) continue;
            up.Sort();
            var ids = new List<int>();
            for (int k = 0; k < up.Count && k < free; k++) ids.Add(up[k].Id);
            sent.AddRange(ids);
            issue(new PostCommand(Player, ids.ToArray(), t.Id));
        }
    }

    /// <summary>True if column (x, z) is near enough a gate of its own with murder holes to hold it (see <see cref="HoldGates"/>).</summary>
    private bool HoldingGate(Simulation sim, int x, int z)
    {
        if (!sim.Economy.HasMurderHoles(Player)) return false;
        foreach (var g in _gates)
            if (sim.Buildings.AtColumn(g.X, g.Z) is { } at && at.Owner == Player && at.Def.MurderHoles is { } holes
                && at.ColumnDistance(x, z) <= holes.Held) return true;
        return false;
    }
}
