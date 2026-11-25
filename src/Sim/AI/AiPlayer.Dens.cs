using System;
using System.Collections.Generic;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.AI;

// Cleared dens, at the smart levels (Easy and up). Once a lair it knows of within HuntReach of its town hall is cleared
// (by its hunting party or anyone else) and its den is unused, it takes its faction's prize there (see
// MonsterSystem.DenUse): it sends one unit that can use the den, its nearest one, and has it use the den once it is
// within reach (DenCommand).
// - A building (the wolf lodge, a boar pen, crab pots): a worker, once it can pay for the building; the worker builds it.
// - A form (the Wolfheart): a unit that can take the form (a spearman), from its soldiers at home.
// - A relic (the giant's heart, the bog lantern, a crab shell shield, the wyrm crystal): a fighting unit without one,
//   from its soldiers at home; its hunting party is nearest the den it has just cleared, so one of them takes it up.
// One unit is out on a den at a time, and is left out of the army while it is (see DenStep). One that does not get
// there within DenGiveUpTicks (or dies) gives up, and that den is left alone for ShunTicks.
public sealed partial class AiPlayer
{
    /// <summary>A unit sent to a den gives up after this many ticks.</summary>
    public const int DenGiveUpTicks = 3 * 60 * Tps;

    // The unit out on a den (0 if none), the den's lair, and when it was sent.
    private int _denRunner, _denLair;
    private long _denSent;
    // Dens left alone after a unit gave up on one, until when; in the order they were left.
    private readonly List<(int Lair, long Until)> _denShunned = new();

    /// <summary>The unit it sent to use a cleared den (0 if none is out).</summary>
    public int DenRunner => _denRunner;
    /// <summary>The lair whose den its unit is on its way to (0 if none).</summary>
    public int DenLair => _denRunner != 0 ? _denLair : 0;
    /// <summary>Dens it has used.</summary>
    public int DensUsed { get; private set; }

    /// <summary>
    /// The unit on a den, after the hunting party: while it is on its way it is taken out of <paramref name="home"/>, and
    /// uses the den once it can; with none out, it sends one to the nearest den it wants (see the notes at the top).
    /// </summary>
    private void DenStep(Simulation sim, Building hall, List<int> home, Action<Command> issue)
    {
        var u = sim.Units;
        var monsters = sim.Monsters;
        _denShunned.RemoveAll(s => sim.Tick >= s.Until);
        if (_denRunner != 0)
        {
            int i = u.IndexOf(_denRunner);
            var lair = monsters.GetLair(_denLair);
            if (lair != null && lair.Used)
            {
                if (lair.UsedBy == Player) DensUsed++;
                else Note(sim, $"the {lair.Monster.Name} den was used by someone else");
                EndDen(sim, shun: false);
                return;
            }
            if (i < 0 || lair == null || monsters.DenUse(Player, lair) is not { } use || !DenAffordable(sim, use))
            {
                Note(sim, "its unit on a den stops short");
                EndDen(sim, shun: i < 0 && lair != null); // killed on the way: leave that den alone a while
                return;
            }
            home.Remove(_denRunner);
            if (monsters.CanUseDen(Player, lair, [i]))
            {
                issue(new DenCommand(Player, [_denRunner], lair.Id));
                Note(sim, $"uses the {lair.Monster.Name} den at {lair.X},{lair.Z}");
                return;
            }
            if (sim.Tick - _denSent > DenGiveUpTicks)
            {
                Note(sim, $"its unit gives up on the {lair.Monster.Name} den");
                EndDen(sim, shun: true);
                return;
            }
            // Stopped, or taken up with work on the way (an automatic worker): on to the den again.
            if (u.Order[i] == UnitOrder.Idle || u.Job[i] != UnitJob.None)
                issue(new MoveCommand(Player, [_denRunner], lair.X, lair.Z));
            return;
        }

        // The nearest den it wants, and its nearest unit that can use it.
        var (hx, hz) = Middle(hall);
        Lair? best = null;
        int runner = 0;
        long bestD = (long)HuntReach * HuntReach + 1;
        foreach (var lair in monsters.Lairs)
        {
            if (!WantsDen(sim, lair, out var use)) continue;
            long d = Dist2(hx, hz, lair.X, lair.Z);
            if (d >= bestD) continue;
            int id = DenRunnerFor(sim, lair, use, home);
            if (id == 0) continue;
            (bestD, best, runner) = (d, lair, id);
        }
        if (best == null) return;
        _denRunner = runner;
        _denLair = best.Id;
        _denSent = sim.Tick;
        home.Remove(runner);
        issue(new MoveCommand(Player, [runner], best.X, best.Z));
        Note(sim, $"sends a {sim.Content.Units[u.Def[u.IndexOf(runner)]].Name} to use the {best.Monster.Name} den at {best.X},{best.Z}");
    }

    /// <summary>
    /// True if it wants the lair's den now: cleared and unused, with a prize there for its faction that it can take
    /// (a building it can pay for, a form it has the techs for), seen, off the corrupted ground and not shunned.
    /// </summary>
    private bool WantsDen(Simulation sim, Lair lair, out DenUse use)
    {
        use = null!;
        if (!lair.Cleared || lair.Used || sim.Monsters.DenUse(Player, lair) is not { } u) return false;
        foreach (var s in _denShunned)
            if (s.Lair == lair.Id) return false;
        if (!LairKnown(sim, lair) || KeepsOff(sim, lair.X, lair.Z)) return false;
        if (!DenAffordable(sim, u)) return false;
        use = u;
        return true;
    }

    /// <summary>True if it can take the prize: a building it can pay for and has the techs for, a form it has the techs for; a relic always.</summary>
    private bool DenAffordable(Simulation sim, DenUse use)
    {
        var econ = sim.Economy;
        if (use.Building is { } b)
            return (b.Faction == null || b.Faction == econ.Faction(Player)) && econ.MissingTech(Player, b.Requires) == null
                && econ.CanAfford(Player, b.Cost);
        if (use.Form is { } f)
            return (f.Faction == null || f.Faction == econ.Faction(Player)) && econ.MissingTech(Player, f.Requires) == null;
        return use.Relic != null;
    }

    /// <summary>
    /// Its unit nearest the den that can use it (by id, 0 if none): a worker for a building (any of its own, not aboard),
    /// else one of <paramref name="home"/> that can take the form, or that can bear the relic and is not hurt.
    /// </summary>
    private int DenRunnerFor(Simulation sim, Lair lair, DenUse use, List<int> home)
    {
        var u = sim.Units;
        int best = 0;
        long bestD = long.MaxValue;
        void Weigh(int i)
        {
            long d = Dist2(u.X[i].FloorToInt(), u.Z[i].FloorToInt(), lair.X, lair.Z);
            if (d < bestD || (d == bestD && u.Id[i] < best)) (bestD, best) = (d, u.Id[i]);
        }
        if (use.Building != null)
        {
            for (int i = 0; i < u.Count; i++)
            {
                if (u.Owner[i] != Player || u.Aboard[i] != 0 || u.Subdued[i] || u.Rout[i] > 0) continue;
                if (sim.Content.Units[u.Def[i]].Role != UnitRole.Worker) continue;
                Weigh(i);
            }
            return best;
        }
        foreach (int id in home)
        {
            int i = u.IndexOf(id);
            if (i < 0 || u.Aboard[i] != 0 || u.Subdued[i] || u.Rout[i] > 0 || u.Town[i] != 0 || _mending.Contains(id)) continue;
            var def = sim.Content.Units[u.Def[i]];
            if (use.Form is { } form && form.EvolvesFrom != def) continue;
            if (use.Relic != null && (u.Relic[i] != 0 || def.Damage <= 0 || def.Role == UnitRole.Worker || Hurt(sim, i, HealedPercent))) continue;
            Weigh(i);
        }
        return best;
    }

    /// <summary>The unit on a den is done; with <paramref name="shun"/>, that den is left alone for <see cref="ShunTicks"/>.</summary>
    private void EndDen(Simulation sim, bool shun)
    {
        if (shun) _denShunned.Add((_denLair, sim.Tick + ShunTicks));
        _denRunner = 0;
        _denLair = 0;
    }
}
