using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.AI;

// Hard's stone towers (a faction it plays balanced, so a town with a wall and watchtowers on it: see
// AiPlayer.Towers.cs). Once it has Masonry and two of its watchtowers stand (StoneTowerAfter), it puts up a
// mason's yard in its town, whose automatic workers cut stone into cut stone. Then it puts a stone tower over each
// of its watchtowers in turn, the ones by the gate first and then the corners, in the order it put them up (see
// TowerSpots), one site at a time and never while a watchtower of its own is still going up. A stone tower takes the
// watchtower's place (see EconomySystem.TakesPlaceOf): it is bigger (5 by 5), sees 18 columns through the fog instead
// of 14, holds 16 archers instead of 8 and they aim better from it, and it takes only 20 wood, the good Hard runs short
// of, with 60 cut stone. One knocked down leaves a gap that is laid as barriers again, a watchtower goes back on it,
// and in time a stone tower over that.
public sealed partial class AiPlayer
{
    /// <summary>Hard: stone towers it has laid out over its watchtowers (counted once each).</summary>
    public int StoneTowersLaid { get; private set; }

    private int _stonecutter;
    /// <summary>Hard: the worker it keeps cutting stone at its mason's yard (0 if none).</summary>
    public int StonecutterId => _stonecutter;

    /// <summary>Hard: towers standing on its wall before it lays out a mason's yard and starts on stone towers.</summary>
    public const int StoneTowerAfter = 2;
    /// <summary>Hard: wood it keeps in hand on top of a stone tower's price.</summary>
    public const int StoneTowerSpareWood = 40;

    /// <summary>The tower that stands on a watchtower's ground and more (a stone tower), for its faction, or null.</summary>
    private BuildingDef? StoneTowerDef(Simulation sim, BuildingDef watch)
    {
        var faction = sim.FactionOf(Player);
        BuildingDef? best = null;
        foreach (var everyone in sim.Content.Buildings)
        {
            var d = faction.Own(everyone);
            if (!d.IsTower || d.UpgradeOf != null || !sim.Economy.Unlocked(Player, d)) continue;
            if (d.SizeX * d.SizeZ <= watch.SizeX * watch.SizeZ) continue;
            if (best == null || d.SizeX * d.SizeZ > best.SizeX * best.SizeZ) best = d;
        }
        return best;
    }

    /// <summary>
    /// Hard, balanced: a mason's yard, then stone towers over its watchtowers (see the top of this file). Called from
    /// <see cref="HardWall"/> after <see cref="Towers"/>, once no column of the line is missing.
    /// </summary>
    private void StoneTowers(Simulation sim, Building hall, (int X, int Z) enemy, Action<Command> issue)
    {
        var watch = FirstBuilding(sim, BuildingKind.Tower);
        if (watch == null || StoneTowerDef(sim, watch) is not { } stone) return;
        var mason = Making(sim, Good.CutStone);
        if (mason == null) return;
        bool yard = false, site = false, left = false;
        Building? done = null;
        foreach (var b in sim.Buildings.All)
        {
            if (b.Owner != Player) continue;
            site |= b.Def.IsTower && !b.Active;
            left |= b.Def == watch;
            if (b.Def.Kind == BuildingKind.Workshop && b.Def.Makes == Good.CutStone) { yard = true; if (b.Active) done ??= b; }
        }
        Stonecutter(sim, done, left, stone, issue);
        if (site) return; // one tower site at a time, watchtowers first
        var spots = TowerSpots(enemy);
        if (spots.Count < 2) return;
        // The towers it has, in the order it wants them (by the gate first, where it could put them there); it
        // waits for StoneTowerAfter of them to stand.
        var towers = new List<Building>();
        for (int k = 0; k < spots.Count && k < KeepTowers; k++)
            if (TowerOn(sim, spots[k].X, spots[k].Z) is { } t && t.Active && !towers.Contains(t)) towers.Add(t);
        if (towers.Count < StoneTowerAfter) return;
        var stock = sim.Economy.Stockpile(Player);
        if (!yard)
        {
            if (!SpareForStoneTower(stock, mason.Cost)) return;
            Want(sim, mason, hall, HardTownRadius, false, issue);
            Note(sim, $"lays out a {mason.Name.ToLowerInvariant()} for the cut stone of stone towers");
            return;
        }
        if (done == null || !SpareForStoneTower(stock, stone.Cost)) return;
        foreach (var t in towers)
        {
            if (t.Def != watch) continue;
            // Centred on the watchtower, else shifted a column so that it still covers the watchtower whole.
            int mx = t.X + t.Def.SizeX / 2, mz = t.Z + t.Def.SizeZ / 2;
            int reach = (stone.SizeX - t.Def.SizeX) / 2;
            var tries = new List<(int X, int Z)> { (mx, mz) };
            for (int dz = -reach; dz <= reach; dz++)
            for (int dx = -reach; dx <= reach; dx++)
                if (dx != 0 || dz != 0) tries.Add((mx + dx, mz + dz));
            foreach (var (tx, tz) in tries)
            {
                int cx = tx - stone.SizeX / 2, cz = tz - stone.SizeZ / 2;
                if (!StoneReadyUnder(sim, stone, cx, cz, t)) continue;
                if (sim.Economy.CanPlace(Player, stone, cx, cz, out _) != PlaceResult.Ok) continue;
                issue(new BuildCommand(Player, Array.Empty<int>(), stone.Id, cx, cz));
                Spend(stone.Cost);
                StoneTowersLaid++;
                Note(sim, $"puts a {stone.Name.ToLowerInvariant()} over its watchtower at {tx},{tz}");
                return;
            }
        }
    }

    /// <summary>
    /// Hard keeps one worker of its own cutting stone at its finished mason's yard while it has a watchtower left to
    /// put a stone tower over and less cut stone than one takes: its automatic workers seldom go there, as what the
    /// yard makes is not a good they want for anything else. Then the worker goes back to being automatic.
    /// </summary>
    private void Stonecutter(Simulation sim, Building? yard, bool left, BuildingDef stone, Action<Command> issue)
    {
        var u = sim.Units;
        if (_stonecutter != 0 && (u.IndexOf(_stonecutter) is var i && (i < 0 || yard == null || u.JobTarget[i] != yard.Id)))
            _stonecutter = 0; // gone, or sent off to other work
        bool want = yard != null && left && sim.Economy.Stock(Player, Good.CutStone) < Price(stone.Cost, Good.CutStone);
        if (want && _stonecutter == 0)
        {
            // A worker already at the yard (an automatic one, there for now) is kept on there.
            for (int k = 0; k < u.Count; k++)
                if (u.Owner[k] == Player && u.Job[k] == UnitJob.Haul && u.JobTarget[k] == yard!.Id)
                {
                    _stonecutter = u.Id[k];
                    issue(new AutomateCommand(Player, new[] { _stonecutter }, false));
                    Note(sim, "keeps a worker cutting stone for its stone towers");
                    return;
                }
            var more = SpareWorkers(sim, 1);
            if (more.Count == 0) return;
            issue(new AutomateCommand(Player, more.ToArray(), false)); // its own, not the automatic workers'
            issue(new WorkCommand(Player, more.ToArray(), yard!.Id));
            _stonecutter = more[0];
            Note(sim, "sets a worker to cutting stone for its stone towers");
        }
        else if (!want && _stonecutter != 0)
        {
            issue(new StopCommand(Player, new[] { _stonecutter }));
            issue(new AutomateCommand(Player, new[] { _stonecutter }, true));
            _stonecutter = 0;
        }
    }

    /// <summary>
    /// True if it can pay for a stone tower and keep <see cref="StoneTowerSpareWood"/> wood on top of what it keeps
    /// in hand: less than the wall's own spare (<see cref="KeepSpareWood"/>), as a stone tower takes little wood.
    /// </summary>
    private bool SpareForStoneTower(int[] stock, GoodAmount[] cost)
    {
        foreach (var g in cost)
            if (stock[(int)g.Good] < g.Amount + _keep[(int)g.Good]) return false;
        return stock[(int)Good.Wood] >= Price(cost, Good.Wood) + _keep[(int)Good.Wood] + StoneTowerSpareWood;
    }

    /// <summary>
    /// True if a stone tower with its corner at (x, z) covers the watchtower <paramref name="watch"/> whole and every
    /// other column of the line of its wall under it holds a finished wall of its own that is not being upgraded.
    /// </summary>
    private bool StoneReadyUnder(Simulation sim, BuildingDef def, int x, int z, Building watch)
    {
        if (_ring is not var (x0, z0, x1, z1)) return false;
        if (watch.X < x || watch.Z < z || watch.MaxX > x + def.SizeX - 1 || watch.MaxZ > z + def.SizeZ - 1) return false;
        for (int cz = z; cz < z + def.SizeZ; cz++)
        for (int cx = x; cx < x + def.SizeX; cx++)
        {
            bool onLine = ((cx == x0 || cx == x1) && cz >= z0 && cz <= z1) || ((cz == z0 || cz == z1) && cx >= x0 && cx <= x1);
            if (!onLine || watch.Covers(cx, cz)) continue;
            var b = sim.Buildings.AtColumn(cx, cz);
            if (b == null || b.Owner != Player || b.Def.Kind != BuildingKind.Wall || !b.Active || b.Upgrade != null) return false;
        }
        return true;
    }
}
