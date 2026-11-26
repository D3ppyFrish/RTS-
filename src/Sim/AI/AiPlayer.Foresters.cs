using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;

namespace Rts.Sim.AI;

/// <summary>
/// Foresters (see <see cref="EconomySystem"/>'s forester's lodge, whose foresters plant saplings that grow into trees).
/// The smart levels (Easy and up), once their town is going, put up one forester's lodge when the woods by their
/// town hall are cut down to a few trees and wood is short (Normal, once its wall is up): on open ground a little way out from the hall where
/// saplings have room (Hard: inside its town, the planting ground clear of its wall line and quarry pits). The rounds
/// give no goods, so automatic workers leave a lodge alone: it sends two foresters there itself while wood is short,
/// and lets them go back to other work once wood piles up. Its wood cutters fell the grown trees like any others.
/// The classic level never plants. The numbers are first guesses for playtesting.
/// </summary>
public sealed partial class AiPlayer
{
    /// <summary>No forester's lodge before this tick: 4 minutes in.</summary>
    public const int ForestFromTicks = 4 * 60 * Tps;

    /// <summary>No forester's lodge before it has this many workers.</summary>
    public const int ForestWorkers = 8;

    /// <summary>Columns from the town hall's middle within which it counts the trees left.</summary>
    public const int ForestNear = 28;

    /// <summary>Trees within <see cref="ForestNear"/> of the town hall below which the woods by its town are cut down.</summary>
    public const int ForestTreesFew = 6;

    /// <summary>Wood in hand at or above which it lays out no lodge and sends no foresters.</summary>
    public const int ForestWoodShort = 400;

    /// <summary>Wood in hand at or above which it lets its foresters go.</summary>
    public const int ForestWoodPlenty = 800;

    /// <summary>Columns from the town hall's middle (nearest and farthest) where the lodge goes: out of the way of the town's buildings.</summary>
    public const int ForestFrom = 16, ForestRadius = 36;

    /// <summary>Fewest saplings the lodge's ground must have room for.</summary>
    public const int ForestRoomLeast = 6;

    /// <summary>Ticks between its looks for room for a lodge (or at whether it wants one): 20 seconds, a minute after finding none.</summary>
    public const int ForestCheckTicks = 20 * Tps, ForestLookTicks = 60 * Tps;

    private readonly List<int> _foresters = new();
    private long _forestLook;

    /// <summary>The workers it keeps at its forester's lodge, by id.</summary>
    public IReadOnlyList<int> Foresters => _foresters;

    /// <summary>
    /// The smart levels' foresters: a lodge when the woods by the town hall are cut down (see
    /// <see cref="ForestTreesFew"/>, <see cref="ForestWoodShort"/>), and foresters for it while wood is short.
    /// </summary>
    private void Foresting(Simulation sim, Building hall, Action<Command> issue)
    {
        var def = ForesterLodge(sim);
        if (def == null) return;
        Building? lodge = null;
        bool site = false;
        foreach (var b in sim.Buildings.All)
        {
            if (b.Owner != Player || b.Def != def) continue;
            if (b.Active) lodge ??= b;
            else site = true;
        }

        var u = sim.Units;
        _foresters.RemoveAll(id => u.IndexOf(id) is var i && (i < 0 || u.Job[i] != UnitJob.Quarry || lodge == null || u.JobTarget[i] != lodge.Id));
        int wood = sim.Economy.Stock(Player, Good.Wood);
        if (lodge == null)
        {
            if (site || sim.Tick < ForestFromTicks || sim.Tick < _forestLook) return;
            if (wood >= ForestWoodShort || Workers(sim) < ForestWorkers) return;
            // Normal walls its town (a faction it plays balanced): the wall comes first, and once it stands the
            // saplings keep off it.
            if (Skilled && !Hard && !_walled && Style(sim) == Content.AiStyle.Balanced) return;
            // A help, not a need: built only when the goods are there, never saved up for.
            if (!Affordable(sim, def.Cost)) return;
            _forestLook = sim.Tick + ForestCheckTicks;
            var (hx, hz) = Middle(hall);
            if (TreesWithin(sim.World, hx, hz, ForestNear, ForestTreesFew) >= ForestTreesFew) return;
            if (!sim.Automation.FindRoom(Player, def, hx, hz, Hard ? HardTownRadius : ForestRadius, out int x, out int z,
                    (bx, bz) => ForestGround(sim, def, hx, hz, bx, bz)))
            {
                _forestLook = sim.Tick + ForestLookTicks;
                return;
            }
            issue(new BuildCommand(Player, Array.Empty<int>(), def.Id, x, z));
            if (Hard) Spend(def.Cost);
            Note(sim, $"lays out a forester's lodge at {x},{z}: the woods by its town are cut down");
            return;
        }

        // Workers of its own at the lodge are its foresters, whoever sent them.
        for (int i = 0; i < u.Count; i++)
            if (u.Owner[i] == Player && u.Job[i] == UnitJob.Quarry && u.JobTarget[i] == lodge.Id && !_foresters.Contains(u.Id[i]))
                _foresters.Add(u.Id[i]);

        if (wood >= ForestWoodPlenty)
        {
            if (_foresters.Count > 0)
            {
                Note(sim, "lets its foresters go, wood is plenty");
                issue(new StopCommand(Player, _foresters.ToArray()));
                issue(new AutomateCommand(Player, _foresters.ToArray(), true));
                _foresters.Clear();
            }
        }
        else if (_foresters.Count < lodge.Def.WorkerSlots && (_foresters.Count > 0 || wood < ForestWoodShort))
        {
            var more = SpareWorkers(sim, lodge.Def.WorkerSlots - _foresters.Count);
            if (more.Count > 0)
            {
                issue(new WorkCommand(Player, more.ToArray(), lodge.Id));
                _foresters.AddRange(more);
                Note(sim, $"sends {more.Count} to plant trees at its forester's lodge");
            }
        }
    }

    /// <summary>The forester's lodge its faction can build (the first building in the data that plants), or null.</summary>
    private BuildingDef? ForesterLodge(Simulation sim)
    {
        foreach (var b in sim.Content.Buildings)
            if (b.Plants > 0 && sim.Economy.Unlocked(Player, b)) return b;
        return null;
    }

    /// <summary>
    /// Ground for a forester's lodge with its corner at (x, z): at least <see cref="ForestFrom"/> columns from the
    /// town hall's middle (hx, hz), safe, not by a lair, on Hard with its planting ground clear of its wall line and
    /// quarry pits, and room there for <see cref="ForestRoomLeast"/> saplings.
    /// </summary>
    private bool ForestGround(Simulation sim, BuildingDef def, int hx, int hz, int x, int z)
    {
        int cx = x + def.SizeX / 2, cz = z + def.SizeZ / 2;
        if (Dist2(cx, cz, hx, hz) < (long)ForestFrom * ForestFrom) return false;
        if (!SafeGround(sim, def, x, z) || NearLair(sim, cx, cz)) return false;
        int r = def.Plants;
        int ax = x - r, az = z - r, bx = x + def.SizeX - 1 + r, bz = z + def.SizeZ - 1 + r;
        if (KeepsOff(sim, ax, az, bx, bz)) return false;
        if (Hard)
        {
            if (!ClearOfWall(ax, az, bx, bz, 2)) return false;
            foreach (var (px1, pz1, px2, pz2) in _pits)
                if (ax <= px2 + 2 && bx >= px1 - 2 && az <= pz2 + 2 && bz >= pz1 - 2) return false;
        }
        if (sim.Economy.CanPlace(Player, def, x, z, out _) != PlaceResult.Ok) return false;
        return sim.Economy.PlantingRoom(def, x, z, ForestRoomLeast) >= ForestRoomLeast;
    }

    /// <summary>The trees standing within <paramref name="reach"/> columns of (x, z), counting no further than <paramref name="most"/>.</summary>
    private static int TreesWithin(VoxelWorld w, int x, int z, int reach, int most)
    {
        int s = Math.Clamp(w.TreeScale, 1, Trees.MaxScale);
        long r2 = (long)reach * reach;
        int columns = 0;
        for (int dz = -reach; dz <= reach; dz++)
        for (int dx = -reach; dx <= reach; dx++)
        {
            int cx = x + dx, cz = z + dz;
            if (cx < 0 || cz < 0 || cx >= w.SizeX || cz >= w.SizeZ || (long)dx * dx + (long)dz * dz > r2) continue;
            if (!TrunkBase(w, cx, cz, out _)) continue;
            // A big tree's trunk is s by s columns.
            if (++columns >= most * s * s) return most;
        }
        return columns / (s * s);
    }
}
