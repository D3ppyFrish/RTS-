using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.AI;

// Raids on storehouses, at the smart levels (Easy and up). A wave out against a rival that comes within RaidLook of a
// rival storehouse it sees, with none of that rival's units it sees within the storehouse's guard radius, sends the
// RaidSize soldiers of the wave nearest to it to take it: to capture it, goods and all, if it holds RaidGoods or more
// or stands nearer its own town hall than its owner's (somewhere it can hope to hold), and to burn it otherwise. The
// rest of the wave goes on; the party is left to it (the wave's orders pass it by) until the storehouse is taken or
// burned, a rival soldier comes at the party, or RaidGiveUpTicks pass, and then it is the wave's again. One raid at a
// time.
public sealed partial class AiPlayer
{
    /// <summary>Furthest (columns) a soldier of the wave may be from a rival storehouse to be sent to raid it.</summary>
    public const int RaidLook = 20;

    /// <summary>Soldiers sent on a raid.</summary>
    public const int RaidSize = 2;

    /// <summary>Fewest soldiers the wave keeps beside the raiders.</summary>
    public const int RaidKeeps = 2;

    /// <summary>Goods (all kinds together) in a rival storehouse that make it worth capturing wherever it stands.</summary>
    public const int RaidGoods = 30;

    /// <summary>How long (ticks) a raid may take before the party gives up and goes back to the wave.</summary>
    public const int RaidGiveUpTicks = 30 * Simulation.TicksPerSecond;

    private readonly List<int> _raidParty = new();
    private int _raidTarget;
    private long _raidSent;

    /// <summary>The soldiers out raiding a storehouse for it now (by id); empty if none.</summary>
    public IReadOnlyList<int> RaidParty => _raidParty;

    /// <summary>The rival storehouse its raiders are after (by id), or 0.</summary>
    public int RaidTarget => _raidTarget;

    /// <summary>Raids it has sent out (for the tests and the match runner's trace).</summary>
    public int Raids { get; private set; }

    /// <summary>Of those, raids sent to capture (the rest were sent to burn).</summary>
    public int Captures { get; private set; }

    /// <summary>
    /// The command with the raiders left out of it, so the wave's orders do not call them off the storehouse; null if
    /// nobody is left in it. Any command that is not about units passes as it is.
    /// </summary>
    private Command? SpareRaiders(Command c)
    {
        if (_raidParty.Count == 0 || c is not UnitsCommand uc || c is CaptureCommand or BurnCommand) return c;
        bool any = false;
        foreach (int id in uc.Units)
            if (_raidParty.Contains(id)) { any = true; break; }
        if (!any) return c;
        var kept = new List<int>(uc.Units.Length);
        foreach (int id in uc.Units)
            if (!_raidParty.Contains(id)) kept.Add(id);
        return kept.Count == 0 ? null : uc with { Units = kept.ToArray() };
    }

    /// <summary>Follows the raid out, or sends one from the wave at a rival storehouse it can take.</summary>
    private void Raid(Simulation sim, Action<Command> issue)
    {
        var u = sim.Units;
        _raidParty.RemoveAll(id => !u.Exists(id));
        if (_raidParty.Count > 0 || _raidTarget != 0)
        {
            var target = sim.Buildings.Get(_raidTarget);
            bool raiding = false;
            foreach (int id in _raidParty)
                if (u.Job[u.IndexOf(id)] is UnitJob.Capture or UnitJob.Burn) raiding = true;
            bool over = target == null || target.Owner == Player || !raiding || sim.Tick - _raidSent > RaidGiveUpTicks;
            if (!over && target != null)
            {
                var (cx, cz) = Middle(target);
                over = NearestEnemy(sim, cx, cz, EconomySystem.GuardRadius + 2, soldiersOnly: true) >= 0;
            }
            if (!over) return;
            Note(sim, target == null ? "its raiders burned the storehouse"
                : target.Owner == Player ? "its raiders took the storehouse" : "its raiders give up on the storehouse");
            _raidParty.Clear();
            _raidTarget = 0;
            return;
        }

        if (_wave.Count < RaidSize + RaidKeeps || _waveLair != 0) return;
        var free = NotFighting(sim, _wave);
        free.RemoveAll(id => EngineAt(sim, u.IndexOf(id)) || u.Aboard[u.IndexOf(id)] != 0);
        if (free.Count < RaidSize) return;

        Building? best = null;
        long bestD = long.MaxValue;
        foreach (var b in sim.Buildings.All)
        {
            if (!b.Def.Storage || !b.Def.Raidable || b.Planned || !Rival(b.Owner) || b.Owner >= Simulation.WildPlayer) continue;
            var (cx, cz) = Middle(b);
            if (!sim.Vision.Sees(Player, cx, cz)) continue;
            long d = long.MaxValue;
            foreach (int id in free)
            {
                int i = u.IndexOf(id);
                d = Math.Min(d, Dist2(u.X[i].FloorToInt(), u.Z[i].FloorToInt(), cx, cz));
            }
            if (d > (long)RaidLook * RaidLook || d >= bestD) continue;
            if (Guarded(sim, b, cx, cz)) continue;
            (bestD, best) = (d, b);
        }
        if (best == null) return;

        var (sx, sz) = Middle(best);
        free.Sort((a, b) =>
        {
            int ia = u.IndexOf(a), ib = u.IndexOf(b);
            int c = Dist2(u.X[ia].FloorToInt(), u.Z[ia].FloorToInt(), sx, sz).CompareTo(Dist2(u.X[ib].FloorToInt(), u.Z[ib].FloorToInt(), sx, sz));
            return c != 0 ? c : a.CompareTo(b);
        });
        _raidParty.AddRange(free.GetRange(0, RaidSize));
        _raidTarget = best.Id;
        _raidSent = sim.Tick;
        Raids++;
        bool capture = CaptureRather(sim, best, sx, sz);
        if (capture)
        {
            Captures++;
            issue(new CaptureCommand(Player, _raidParty.ToArray(), best.Id));
        }
        else issue(new BurnCommand(Player, _raidParty.ToArray(), best.Id));
        Note(sim, $"sends {_raidParty.Count} to {(capture ? "capture" : "burn")} a storehouse at {sx},{sz}");
    }

    /// <summary>True if it sees a unit of the storehouse's owner close enough to it to hold off a raid.</summary>
    private bool Guarded(Simulation sim, Building b, int cx, int cz)
    {
        var u = sim.Units;
        long r2 = (long)(EconomySystem.GuardRadius + 2) * (EconomySystem.GuardRadius + 2);
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Owner[i] != b.Owner || u.Hp[i] <= 0) continue;
            if (Dist2(u.X[i].FloorToInt(), u.Z[i].FloorToInt(), cx, cz) > r2) continue;
            if (sim.Vision.SeesUnit(Player, i)) return true;
        }
        return false;
    }

    /// <summary>
    /// True to capture the storehouse rather than burn it: it holds <see cref="RaidGoods"/> or more, or it stands
    /// nearer its own town hall than its owner's (or its owner has none).
    /// </summary>
    private bool CaptureRather(Simulation sim, Building b, int cx, int cz)
    {
        int goods = 0;
        foreach (int n in b.Stock) goods += n;
        if (goods >= RaidGoods) return true;
        var mine = sim.Economy.TownHall(Player);
        var theirs = sim.Economy.TownHall(b.Owner);
        if (theirs == null) return true;
        if (mine == null) return false;
        var (mx, mz) = Middle(mine);
        var (tx, tz) = Middle(theirs);
        return Dist2(mx, mz, cx, cz) < Dist2(tx, tz, cx, cz);
    }
}
