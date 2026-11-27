using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;

namespace Rts.Sim.AI;

// Charcoal from burnt woods (see FireSystem), from Normal up. A forest fire leaves a charred stump at the foot of each
// tree it burns, and a stump gives charcoal (coal). While it has a finished smelter and less than CharcoalWanted coal
// in hand, it keeps up to CharcoalDiggers workers digging the stumps it has seen within CharcoalReach columns of its
// storage: the nearest such stump to its storage with no fire within a column of it and no rival soldier it sees within
// CharcoalDanger columns. It sends idle workers first, then ones digging stone, nearest the stump first; they move on
// to the stumps round it by themselves, and back to other work once the stumps or the need run out. Its automatic
// workers dig stumps within their reach on their own too (see AutomationSystem); this sends them to the burnt woods
// further out, the ones it burnt by its rivals included. The classic and Easy players leave stumps to their automatic
// workers. The numbers are first guesses for playtesting.
public sealed partial class AiPlayer
{
    /// <summary>It digs charcoal while it has less coal in hand than this (and a smelter to burn it in).</summary>
    public const int CharcoalWanted = 40;
    /// <summary>Most workers it keeps digging charred stumps.</summary>
    public const int CharcoalDiggers = 2;
    /// <summary>Furthest (columns) from its nearest storage a stump it sends workers to may be.</summary>
    public const int CharcoalReach = 48;
    /// <summary>It sends no workers to a stump with a rival soldier it sees this close (columns).</summary>
    public const int CharcoalDanger = 12;

    // Workers it sent to dig stumps (by id), and when it last sent any.
    private readonly List<int> _charcoalers = new();
    private long _charcoalAt = long.MinValue / 2;

    /// <summary>Workers it has sent to dig charred stumps for charcoal (by id; for the tests and the match runner).</summary>
    public IReadOnlyList<int> CharcoalerIds => _charcoalers;

    /// <summary>Times it has sent workers to dig charred stumps.</summary>
    public int CharcoalSends { get; private set; }

    /// <summary>Keeps workers on the charred stumps while it needs coal for its smelter.</summary>
    private void Charcoal(Simulation sim, Action<Command> issue)
    {
        var u = sim.Units;
        // Those that left off digging stumps are let go (after the order has had time to land).
        if (sim.Tick - _charcoalAt >= 2 * Tps)
            _charcoalers.RemoveAll(id => !u.Exists(id) || !OnCharcoal(u, u.IndexOf(id)));
        if (sim.Economy.Stock(Player, Good.Coal) >= CharcoalWanted) return;
        bool smelter = false;
        foreach (var b in sim.Buildings.All)
            smelter |= b.Owner == Player && b.Active && b.Def.Kind == BuildingKind.Smelter;
        if (!smelter) return;

        int onCharcoal = 0;
        for (int i = 0; i < u.Count; i++)
            if (u.Owner[i] == Player && OnCharcoal(u, i)) onCharcoal++;
        int n = CharcoalDiggers - onCharcoal;
        if (n <= 0 || Stump(sim) is not var (sx, sy, sz)) return;

        // Idle workers first, then those digging stone; nearest the stump first.
        _scratch.Clear();
        var near = new List<(int Rank, long D, int Id)>();
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Owner[i] != Player || sim.Content.Units[u.Def[i]].Role != UnitRole.Worker || u.Rout[i] > 0) continue;
            if (_passMiners.Contains(u.Id[i]) || _woodSent.Contains(u.Id[i])) continue;
            bool idle = u.Job[i] == UnitJob.None && u.Order[i] == UnitOrder.Idle && !u.Held[i];
            bool stone = u.Job[i] == UnitJob.Gather && u.JobBlock[i] == BlockType.Stone && u.CarryAmount[i] == 0;
            if (!idle && !stone) continue;
            near.Add((idle ? 0 : 1, Dist2(u.X[i].FloorToInt(), u.Z[i].FloorToInt(), sx, sz), u.Id[i]));
        }
        if (near.Count == 0) return;
        near.Sort((a, b) => a.Rank != b.Rank ? a.Rank.CompareTo(b.Rank) : a.D != b.D ? a.D.CompareTo(b.D) : a.Id.CompareTo(b.Id));
        for (int k = 0; k < Math.Min(n, near.Count); k++) _scratch.Add(near[k].Id);
        var ids = _scratch.ToArray();
        issue(new GatherCommand(Player, ids, sx, sy, sz));
        _charcoalers.AddRange(ids);
        _charcoalAt = sim.Tick;
        CharcoalSends++;
        Note(sim, $"sends {ids.Length} worker{(ids.Length == 1 ? "" : "s")} to dig charcoal from burnt stumps");
    }

    /// <summary>True if unit i is digging charred stumps (or carrying their charcoal in).</summary>
    private static bool OnCharcoal(UnitStore u, int i) => u.Job[i] == UnitJob.Gather && u.JobBlock[i] == BlockType.CharredWood;

    /// <summary>
    /// The stump to send workers to: of the charred stumps it has seen within <see cref="CharcoalReach"/> columns of its
    /// storage, the one nearest it, with no fire beside it and no rival soldier it sees within
    /// <see cref="CharcoalDanger"/>, off the corrupted ground; or null.
    /// </summary>
    public (int X, int Y, int Z)? Stump(Simulation sim)
    {
        (int X, int Y, int Z)? best = null;
        long bestD = (long)CharcoalReach * CharcoalReach + 1;
        foreach (var (x, y, z) in sim.Fire.Stumps())
        {
            if (sim.Vision.At(Player, x, z) == Sight.Unseen) continue;
            var store = sim.Economy.NearestStorage(Player, x, z);
            if (store == null) return null;
            var (mx, mz) = Middle(store);
            long d = Dist2(x, z, mx, mz);
            if (d >= bestD || sim.Economy.Claimed(x, y, z) || sim.Fire.HeatAt(x, y, z) > 0 || KeepsOff(sim, x, z)) continue;
            if (NearestEnemy(sim, x, z, CharcoalDanger, soldiersOnly: true) >= 0) continue;
            (bestD, best) = (d, (x, y, z));
        }
        return best;
    }
}
