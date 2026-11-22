using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.AI;

/// <summary>
/// Hunting deer (see <see cref="EconomySystem"/>'s hunter's lodge). The smart levels (Easy and up) put up one
/// hunter's lodge by the den of the nearest herd of wild game they have seen, once their town is going and while
/// food is not piling up; they keep the lodge's places filled with hunters while its herd has game to spare, and
/// put a storehouse by it when the walk home with the meat is long. The classic level never hunts. The numbers
/// are first guesses for playtesting.
/// </summary>
public sealed partial class AiPlayer
{
    /// <summary>No lodge before this tick (the first farms and barracks come first): 3 minutes in.</summary>
    public const int LodgeFromTicks = 3 * 60 * Tps;

    /// <summary>No lodge before it has this many workers.</summary>
    public const int LodgeWorkers = 8;

    /// <summary>Food in hand at or above which a lodge is not worth it yet (food is piling up as it is).</summary>
    public const int LodgeFoodPlenty = 300;

    /// <summary>It hunts herds whose den is at most this far (columns) from its town hall.</summary>
    public const int LodgeReach = 100;

    /// <summary>How far (columns) from a herd's den it looks for room for the lodge.</summary>
    public const int LodgeRadius = 12;

    /// <summary>
    /// Deer in the lodge's reach before it sends hunters out again after letting them go (above the
    /// <see cref="EconomySystem.HerdKeep"/> they leave, so the hunters are not sent and let go every time one deer is born).
    /// </summary>
    public const int LodgeHuntFrom = EconomySystem.HerdKeep + 2;

    /// <summary>A lodge this far (columns) or more from the nearest storage gets a storehouse by it.</summary>
    public const int LodgeStoreGap = 24;

    // Its hunters, by id.
    private readonly List<int> _hunters = new();

    /// <summary>The workers it keeps at its hunter's lodge, by id.</summary>
    public IReadOnlyList<int> Hunters => _hunters;

    /// <summary>
    /// The smart levels' hunting: a lodge by a herd it has seen when it helps (see <see cref="LodgeFromTicks"/>,
    /// <see cref="LodgeWorkers"/>, <see cref="LodgeFoodPlenty"/>), hunters for it, and a storehouse by it when
    /// storage is far.
    /// </summary>
    private void Hunting(Simulation sim, Building hall, Action<Command> issue)
    {
        var def = Lodge(sim);
        if (def == null) return;
        Building? lodge = null;
        bool site = false;
        foreach (var b in sim.Buildings.All)
        {
            if (b.Owner != Player || b.Def.Hunts <= 0) continue;
            if (b.Active)
            {
                lodge ??= b;
            }
            else site = true;
        }

        var u = sim.Units;
        _hunters.RemoveAll(id => u.IndexOf(id) is var i && (i < 0 || u.Job[i] != UnitJob.Quarry));
        if (lodge == null)
        {
            if (site || sim.Tick < LodgeFromTicks) return;
            if (sim.Economy.Stock(Player, Good.Food) >= LodgeFoodPlenty || Workers(sim) < LodgeWorkers) return;
            if (HerdToHunt(sim, hall) is not { } herd) return;
            if (!Affordable(sim, def.Cost))
            {
                if (Hard) Save(def.Cost);
                return;
            }
            if (sim.Automation.FindRoom(Player, def, herd.X, herd.Z, LodgeRadius, out int x, out int z, (bx, bz) => SafeGround(sim, def, bx, bz)))
            {
                issue(new BuildCommand(Player, Array.Empty<int>(), def.Id, x, z));
                if (Hard) Spend(def.Cost);
                Note(sim, $"lays out a hunter's lodge at {x},{z} for the deer there");
            }
            return;
        }

        // Hunters while there is game to spare in reach; none (back to other work) while there is not, until the herd
        // has grown back to LodgeHuntFrom.
        int game = sim.Economy.GameInReach(lodge);
        if (game <= EconomySystem.HerdKeep)
        {
            if (_hunters.Count > 0)
            {
                Note(sim, "lets its hunters go, no deer to spare");
                issue(new StopCommand(Player, _hunters.ToArray()));
                issue(new AutomateCommand(Player, _hunters.ToArray(), true));
                _hunters.Clear();
            }
        }
        else if (_hunters.Count < lodge.Def.WorkerSlots && (_hunters.Count > 0 || game >= LodgeHuntFrom))
        {
            var more = SpareWorkers(sim, lodge.Def.WorkerSlots - _hunters.Count);
            if (more.Count > 0)
            {
                issue(new WorkCommand(Player, more.ToArray(), lodge.Id));
                _hunters.AddRange(more);
            }
        }

        // A storehouse by the lodge when the meat has far to go.
        var store = FirstBuilding(sim, BuildingKind.Storehouse);
        if (store == null || _hunters.Count == 0) return;
        var (lx, lz) = Middle(lodge);
        if (sim.Economy.NearestStorage(Player, lx, lz) is { } near && near.ColumnDistance(lx, lz) < LodgeStoreGap) return;
        foreach (var b in sim.Buildings.All)
            if (b.Owner == Player && !b.Active && b.Def.Storage && b.ColumnDistance(lx, lz) < LodgeStoreGap) return; // one on its way
        if (!Affordable(sim, store.Cost)) return;
        if (sim.Automation.FindRoom(Player, store, lx, lz, StoreRadius, out int sx, out int sz, (bx, bz) => SafeGround(sim, store, bx, bz)))
        {
            issue(new BuildCommand(Player, Array.Empty<int>(), store.Id, sx, sz));
            if (Hard) Spend(store.Cost);
            Note(sim, $"lays out a storehouse by its hunter's lodge at {sx},{sz}");
        }
    }

    /// <summary>The hunter's lodge its faction can build (the first building in the data that hunts), or null.</summary>
    private BuildingDef? Lodge(Simulation sim)
    {
        foreach (var b in sim.Content.Buildings)
            if (b.Hunts > 0 && sim.Economy.Unlocked(Player, b)) return b;
        return null;
    }

    /// <summary>How many workers it has.</summary>
    private int Workers(Simulation sim)
    {
        var u = sim.Units;
        int n = 0;
        for (int i = 0; i < u.Count; i++)
            if (u.Owner[i] == Player && sim.Content.Units[u.Def[i]].Role == UnitRole.Worker) n++;
        return n;
    }

    /// <summary>
    /// The herd to put a lodge by: the one whose den is nearest its town hall, within <see cref="LodgeReach"/>,
    /// that it has seen (the den's ground, or one of the herd), with more game than hunters leave, nearer this town
    /// hall than any enemy's and not by a monster lair; or null.
    /// </summary>
    private Lair? HerdToHunt(Simulation sim, Building hall)
    {
        var (hx, hz) = Middle(hall);
        var enemy = EnemyHall(sim, hx, hz);
        var u = sim.Units;
        Lair? best = null;
        long bestD = (long)LodgeReach * LodgeReach + 1;
        foreach (var herd in sim.Monsters.Herds)
        {
            if (herd.Cleared || herd.PackIds.Count <= EconomySystem.HerdKeep) continue;
            long d = Dist2(hx, hz, herd.X, herd.Z);
            if (d >= bestD) continue;
            if (enemy is { } e && Dist2(herd.X, herd.Z, e.X, e.Z) <= d) continue;
            if (NearLair(sim, herd.X, herd.Z) || KeepsOff(sim, herd.X, herd.Z)) continue;
            bool seen = sim.Vision.At(Player, herd.X, herd.Z) != Sight.Unseen;
            foreach (int id in herd.PackIds)
            {
                if (seen) break;
                int i = u.IndexOf(id);
                seen = i >= 0 && sim.Vision.At(Player, u.X[i].FloorToInt(), u.Z[i].FloorToInt()) != Sight.Unseen;
            }
            if (!seen) continue;
            best = herd;
            bestD = d;
        }
        return best;
    }

    /// <summary>
    /// Up to n workers to send hunting: idle ones first, then wood cutters, then those gathering anything else but
    /// food, none carrying a load, none it keeps at its quarry.
    /// </summary>
    private List<int> SpareWorkers(Simulation sim, int n)
    {
        var u = sim.Units;
        var picked = new List<int>();
        for (int pass = 0; pass < 3 && picked.Count < n; pass++)
        for (int i = 0; i < u.Count && picked.Count < n; i++)
        {
            if (u.Owner[i] != Player || sim.Content.Units[u.Def[i]].Role != UnitRole.Worker) continue;
            int id = u.Id[i];
            if (picked.Contains(id) || _masons.Contains(id) || u.Held[i]) continue;
            bool ok = pass switch
            {
                0 => u.Job[i] == UnitJob.None && u.Order[i] == UnitOrder.Idle,
                1 => u.Job[i] == UnitJob.Gather && u.JobBlock[i] == BlockType.Wood && u.CarryAmount[i] == 0,
                _ => u.Job[i] == UnitJob.Gather && !OnFood(u, i) && u.CarryAmount[i] == 0,
            };
            if (ok) picked.Add(id);
        }
        return picked;
    }
}
