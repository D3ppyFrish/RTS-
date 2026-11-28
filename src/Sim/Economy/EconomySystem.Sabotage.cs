using System;
using System.Collections.Generic;
using Rts.Sim.Content;
using Rts.Sim.Units;

namespace Rts.Sim.Economy;

/// <summary>
/// Sabotage, Clan Kage's (see "Clan Kage" in docs/rules.md): a unit whose player has the tech for it
/// (<see cref="TechEffectType.Sabotage"/>, their kunoichi and shinobi) goes to an enemy building
/// (<see cref="SabotageCommand"/>) and works <see cref="SabotageRules.Ticks"/> at it while no soldier of its owner's stands
/// within <see cref="SabotageRules.Guard"/> columns. Then the building is sabotaged: for <see cref="SabotageRules.StallTicks"/>
/// it does no work (no training, research, smelting, making, upgrading or yield; see <see cref="Stalled"/>), and
/// <see cref="SabotageRules.Spoil"/> percent of each good in it is lost. The saboteurs then wait
/// <see cref="SabotageRules.WaitTicks"/> before the next.
/// </summary>
public sealed partial class EconomySystem
{
    // Saboteurs standing at their target this tick: (building id, unit index), in unit order.
    private readonly List<(int Building, int Unit)> _saboteurs = new();

    /// <summary>Buildings sabotaged since the match began.</summary>
    public long Sabotages { get; private set; }

    /// <summary>Goods (all kinds together) spoiled by sabotage since the match began.</summary>
    public long GoodsSpoiled { get; private set; }

    private SabotageRules SabotageRules => Content.Combat.Sabotage;

    /// <summary>True if building b is sabotaged and does no work.</summary>
    public bool Stalled(Building b) => b.Sabotaged > 0;

    /// <summary>True if unit i, of its type and its player's techs, can sabotage at all (ready or not).</summary>
    public bool CanSabotage(int i) =>
        (uint)i < (uint)U.Count && U.Town[i] == 0 && U.Owner[i] < Simulation.WildPlayer && CanSabotage(U.Owner[i], Content.Units[U.Def[i]]);

    /// <summary>True if unit i can set out to sabotage now: it can at all, its last sabotage is long enough ago, and it is free to.</summary>
    public bool ReadyToSabotage(int i) =>
        CanSabotage(i) && U.SabotageWait[i] == 0 && U.Hp[i] > 0 && !U.Subdued[i] && U.Aboard[i] == 0 && U.Penned[i] == 0
        && U.Rout[i] == 0 && U.RunStage[i] == 0;

    /// <summary>
    /// True if building b is one a saboteur of <paramref name="player"/>'s can wreck: a finished building of a rival's (not
    /// a wild one's), and not a wall, gate, tower, bridge or totem, that is not sabotaged already.
    /// </summary>
    public bool Sabotageable(int player, Building b) =>
        b.Active && b.Owner != player && b.Owner < Simulation.WildPlayer && _sim.Truces.Hostile(player, b.Owner)
        && !b.Def.IsRampart && !b.Def.IsBridge && b.Def.Kind != BuildingKind.Totem && b.Sabotaged == 0;

    internal void Apply(SabotageCommand c)
    {
        var b = Buildings.Get(c.Building);
        if (b == null || !Sabotageable(c.PlayerId, b)) return;
        foreach (int i in OwnedUnits(c))
            if (ReadyToSabotage(i)) StartJob(i, UnitJob.Sabotage, b.Id);
    }

    private void StepSaboteur(int i)
    {
        var b = Buildings.Get(U.JobTarget[i]);
        if (b == null || !Sabotageable(U.Owner[i], b) || !ReadyToSabotage(i))
        {
            EndJob(i);
            return;
        }
        if (WalkToBuilding(i, b, out bool there)) return;
        if (!there)
        {
            SetPhase(i, JobPhase.ToWork);
            return;
        }
        _saboteurs.Add((b.Id, i));
    }

    /// <summary>
    /// Each tick for each building: a sabotaged one's stall counts down; otherwise the saboteur standing at it with the
    /// lowest unit id says whose sabotage goes on, and the work goes on while none of the owner's soldiers is by it.
    /// </summary>
    private void StepSabotage(Building b)
    {
        if (b.Sabotaged > 0)
        {
            b.Sabotaged--;
            return;
        }
        int saboteur = -1;
        foreach (var (id, i) in _saboteurs)
            if (id == b.Id && (saboteur < 0 || U.Id[i] < U.Id[saboteur])) saboteur = i;
        if (saboteur < 0)
        {
            b.SabotagePlayer = -1;
            b.SabotageWork = 0;
            return;
        }
        int player = U.Owner[saboteur];
        if (player != b.SabotagePlayer)
        {
            b.SabotagePlayer = player;
            b.SabotageWork = 0;
            _sim.Events.BuildingAttacked(b, player);
        }
        if (Watched(b))
        {
            b.SabotageWork = 0;
            return;
        }
        var rules = SabotageRules;
        if (++b.SabotageWork < rules.Ticks) return;

        // Done: the building stalls, goods in it spoil, and the saboteurs at it slip away to wait for the next.
        b.Sabotaged = Math.Max(1, rules.StallTicks);
        b.SabotageWork = 0;
        b.SabotagePlayer = -1;
        for (int g = 0; g < b.Stock.Length; g++)
        {
            int lost = b.Stock[g] * rules.Spoil / 100;
            b.Stock[g] -= lost;
            GoodsSpoiled += lost;
        }
        foreach (var (id, i) in _saboteurs)
            if (id == b.Id && U.Owner[i] == player)
            {
                EndJob(i);
                U.SabotageWait[i] = Math.Max(1, rules.WaitTicks);
            }
        Sabotages++;
        SabotageDone(player);
    }

    /// <summary>True if a soldier of the building's owner's stands within <see cref="SabotageRules.Guard"/> columns of it.</summary>
    private bool Watched(Building b)
    {
        int r = SabotageRules.Guard;
        for (int i = 0; i < U.Count; i++)
        {
            if (U.Owner[i] != b.Owner || U.Hp[i] <= 0 || !TechEffect.Fights(Content.Units[U.Def[i]])) continue;
            if (b.ColumnDistance(U.X[i].FloorToInt(), U.Z[i].FloorToInt()) <= r && Math.Abs(U.Y[i] - b.Y) <= 2 + r) return true;
        }
        return false;
    }

    /// <summary>Each tick: the saboteurs' wait counts down.</summary>
    private void StepSaboteurWaits()
    {
        var u = U;
        for (int i = 0; i < u.Count; i++)
            if (u.SabotageWait[i] > 0) u.SabotageWait[i]--;
    }
}
