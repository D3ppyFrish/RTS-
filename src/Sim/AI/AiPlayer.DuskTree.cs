using System;
using System.Collections.Generic;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.AI;

// The dusk tree, at the smart levels (Easy and up). A player whose faction can heal the tree (the Settlers) or hack it
// down (the Orcs, the Deepfolk) wants it while it still stands unhealed; one that would hack it down wants it only while
// a rival in the match could heal it, so that nobody else gets the Dusk Staff. While it wants it:
// - Its hunting parties may go for the Dusk Warden's lair like any other (see Huntable), weighed the same way: a party
//   is sent only when it outweighs the warden.
// - Once the warden is gone, it sends units to the tree (DuskTreeCommand): its free healer nearest the tree, or up to
//   RazeParty soldiers from home beyond the guard, once it has seen where the tree stands. Those working on the tree are left out of the army (see Soldiers);
//   if they stop short (beaten off, killed) it tries again after TreeRetryTicks.
// Whoever bears the Dusk Staff for it uses Dusk Bloom once the hurt allies round the bearer are owed at least
// BloomWorth hit points (see Bloom).
public sealed partial class AiPlayer
{
    /// <summary>The most soldiers it sends to hack the dusk tree down.</summary>
    public const int RazeParty = 4;
    /// <summary>After its units stop working on the tree short of the end, it waits this many ticks before sending more.</summary>
    public const int TreeRetryTicks = 60 * Tps;
    /// <summary>The staff's bearer blooms once the allies in reach are owed at least this many hit points, all together.</summary>
    public const int BloomWorth = 3 * DuskTreeSystem.BloomHeal;

    // Its units sent to work on the dusk tree, by id in the order they were sent.
    private readonly List<int> _treeParty = new();
    private long _treeAt;

    /// <summary>Its units sent to work on the dusk tree (healing it or hacking it down), by id.</summary>
    public IReadOnlyList<int> TreeParty => _treeParty;
    /// <summary>Times it used Dusk Bloom.</summary>
    public int Blooms { get; private set; }

    /// <summary>
    /// True if it wants the dusk tree: it plays a smart level, the tree stands unhealed, and its faction heals it (and it
    /// has a healer or can train one), or hacks it down while a rival in the match could heal it.
    /// </summary>
    public bool WantsDuskTree(Simulation sim)
    {
        var tree = sim.DuskTree;
        if (!Smart || !tree.Exists || tree.Healed || tree.Razed) return false;
        var use = tree.UseFor(Player);
        if (use == DuskTreeUse.Heal) return HasOrCanTrainHealer(sim);
        if (use != DuskTreeUse.Raze) return false;
        foreach (int p in sim.Match.Players)
            if (p != Player && Rival(p) && tree.UseFor(p) == DuskTreeUse.Heal) return true;
        return false;
    }

    /// <summary>True if it has a unit that heals, or a barracks that can train one now.</summary>
    private bool HasOrCanTrainHealer(Simulation sim)
    {
        var u = sim.Units;
        for (int i = 0; i < u.Count; i++)
            if (u.Owner[i] == Player && sim.Content.Units[u.Def[i]].Heal > 0) return true;
        return TreeHealer(sim) != null;
    }

    /// <summary>A unit that heals one of its barracks can train now, if any.</summary>
    private UnitDef? TreeHealer(Simulation sim)
    {
        foreach (var b in sim.Buildings.All)
        {
            if (b.Owner != Player || !b.Active || b.Def.Kind != BuildingKind.Barracks) continue;
            foreach (string id in b.Def.Trains)
                if (sim.Content.TryGetUnit(id, out var d) && d.Heal > 0 && sim.Economy.Unlocked(Player, d)) return d;
        }
        return null;
    }

    /// <summary>
    /// The units on the tree, after the hunting party: those still working on it are taken out of
    /// <paramref name="home"/>; with none at it and the warden gone, it sends a healer or a party to hack it down.
    /// </summary>
    private void DuskTreeStep(Simulation sim, Building hall, List<int> home, int guardPercent, Action<Command> issue)
    {
        var u = sim.Units;
        var tree = sim.DuskTree;
        _treeParty.RemoveAll(id => !u.Exists(id));
        if (_treeParty.Count > 0)
        {
            bool working = false;
            foreach (int id in tree.Tending)
                if (_treeParty.Contains(id)) working = true;
            if (working && !tree.Healed && !tree.Razed)
            {
                home.RemoveAll(_treeParty.Contains);
                return;
            }
            Note(sim, tree.Healed && tree.HealedBy == Player ? "healed the dusk tree: its healer bears the Dusk Staff"
                : tree.Razed && tree.RazedBy == Player ? "hacked the dusk tree down"
                : "its units on the dusk tree stopped short");
            if (!tree.Healed && !tree.Razed) _treeAt = sim.Tick + TreeRetryTicks;
            _treeParty.Clear();
        }
        if (sim.Tick < _treeAt || !tree.Unguarded || !WantsDuskTree(sim)) return;
        if (sim.Vision.At(Player, tree.X, tree.Z) == Sight.Unseen) return; // where the tree is, it has to have seen
        var (hx, hz) = Middle(hall);
        if (Dist2(hx, hz, tree.X, tree.Z) > (long)HuntReach * HuntReach) return;

        if (tree.UseFor(Player) == DuskTreeUse.Heal)
        {
            // Its healer nearest the tree that is free: at home or on no errand, not hurt, not in a wave.
            int best = 0;
            long bestD = long.MaxValue;
            for (int i = 0; i < u.Count; i++)
            {
                if (u.Owner[i] != Player || sim.Content.Units[u.Def[i]].Heal <= 0 || !tree.CanWork(i)) continue;
                int id = u.Id[i];
                if (u.Aboard[i] != 0 || u.Job[i] != UnitJob.None || _wave.Contains(id) || _joining.Contains(id) || _huntParty.Contains(id)) continue;
                long d = Dist2(u.X[i].FloorToInt(), u.Z[i].FloorToInt(), tree.X, tree.Z);
                if (d < bestD) (bestD, best) = (d, id);
            }
            if (best == 0) return;
            _treeParty.Add(best);
        }
        else
        {
            var ready = new List<int>(home);
            ready.RemoveAll(_mending.Contains);
            ready.RemoveAll(id => Hurt(sim, u.IndexOf(id), HealedPercent) || !tree.CanWork(u.IndexOf(id)));
            int guard = Math.Max(LeastGuard, home.Count * guardPercent / 100);
            int spare = Math.Min(RazeParty, ready.Count - guard);
            if (spare < 1) return;
            SortNearest(sim, ready, tree.X, tree.Z);
            _treeParty.AddRange(ready.GetRange(0, spare));
        }
        home.RemoveAll(_treeParty.Contains);
        issue(new DuskTreeCommand(Player, _treeParty.ToArray()));
        Note(sim, tree.UseFor(Player) == DuskTreeUse.Heal ? $"sends a healer to heal the dusk tree at {tree.X},{tree.Z}"
            : $"sends {_treeParty.Count} to hack the dusk tree down at {tree.X},{tree.Z}");
    }

    // Allies near the staff's bearer (indexes), reused.
    private readonly List<int> _bloomNear = new();

    /// <summary>The hit points the allies within Dusk Bloom's reach of unit i are owed, all together (as much as a bloom gives each).</summary>
    private int Owed(Simulation sim, int i)
    {
        var u = sim.Units;
        sim.Combat.Allies(u.X[i], u.Y[i], u.Z[i], Fix64.FromInt(DuskTreeSystem.BloomRange), CombatSystem.HealHeight, Player, _bloomNear);
        int owed = 0;
        foreach (int j in _bloomNear)
            if (!u.Subdued[j]) owed += Math.Min(DuskTreeSystem.BloomHeal, Math.Max(0, sim.MaxHpOf(j) - u.Hp[j]));
        return owed;
    }

    /// <summary>Its bearer of the Dusk Staff, if it has one and it is ready, blooms once those round it are owed <see cref="BloomWorth"/>.</summary>
    private void Bloom(Simulation sim, Action<Command> issue)
    {
        var tree = sim.DuskTree;
        if (!Smart || tree.StaffBearer == 0) return;
        int i = sim.Units.IndexOf(tree.StaffBearer);
        if (i < 0 || sim.Units.Owner[i] != Player || !tree.CanBloom(i)) return;
        int owed = Owed(sim, i);
        if (owed < BloomWorth) return;
        Blooms++;
        issue(new BloomCommand(Player, new[] { tree.StaffBearer }));
        Note(sim, $"uses Dusk Bloom: {owed} hit points owed round the bearer");
    }
}
