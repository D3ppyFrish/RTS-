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
    /// The player's wall or gate site nearest to column (x, z), within <see cref="WallReach"/> of it, with the
    /// fewest workers on it (ties to the nearest, then the lowest id); null if there is none.
    /// </summary>
    private Building? NextWallSite(int player, int x, int z)
    {
        Building? best = null;
        int bestWorkers = 0, bestD = 0;
        foreach (var b in Buildings.All)
        {
            if (b.Owner != player || b.State != BuildingState.Site || !b.Def.IsWall) continue;
            int d = b.ColumnDistance(x, z);
            if (d > WallReach) continue;
            int workers = WorkersAt(b, UnitJob.Build);
            if (best != null && (workers > bestWorkers || (workers == bestWorkers && d >= bestD))) continue;
            (best, bestWorkers, bestD) = (b, workers, d);
        }
        return best;
    }
}
