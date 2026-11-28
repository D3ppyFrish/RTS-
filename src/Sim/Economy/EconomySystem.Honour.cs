using System;
using System.Collections.Generic;
using Rts.Sim.Content;
using Rts.Sim.Units;

namespace Rts.Sim.Economy;

/// <summary>
/// Clan honour, Clan Kage's (see "Clan Kage" in docs/rules.md): a faction trait (<see cref="FactionDef.Honour"/>), not a
/// tech. Its players earn honour by the contracts the design doc has them carry out (an assassination: a rival's worker,
/// caster or hero killed by one of their units; a rival's building sabotaged; a rival's town hall scouted), lose it by
/// dishonourable defeats (a finished building of theirs destroyed or captured, a hero of theirs killed), and spend it on
/// what costs honour (<see cref="UnitDef.Honour"/>, <see cref="TechDef.Honour"/>): the master ninja and the clan's top tech.
/// Honour is never below 0. It is part of the state hash.
/// </summary>
public sealed partial class EconomySystem
{
    // Rival town halls each player has scouted: player << 32 | building id (sorted, so the hash goes in order).
    private readonly SortedSet<long> _scouted = new();

    /// <summary>Contracts that earned honour since the match began (assassinations, sabotages and scoutings).</summary>
    public long Contracts { get; private set; }

    /// <summary>The player's faction's clan honour, or null if it keeps none.</summary>
    public HonourRules? HonourOf(int player) => (uint)player < Simulation.WildPlayer ? Faction(player).Honour : null;

    /// <summary>The player's clan honour now (0 for a faction that keeps none).</summary>
    public int Honour(int player) => Find(player)?.Honour ?? 0;

    /// <summary>True if the player has at least that much honour (always for 0).</summary>
    public bool HasHonour(int player, int honour) => honour <= 0 || Honour(player) >= honour;

    /// <summary>True if the player has scouted the rival town hall with this id.</summary>
    public bool Scouted(int player, int building) => _scouted.Contains(((long)player << 32) | (uint)building);

    /// <summary>
    /// True if the player has a hero of this type living, or one queued at any of their buildings: one of a kind, so
    /// they cannot train another (see <see cref="UnitDef.Hero"/>).
    /// </summary>
    public bool HeroTaken(int player, UnitDef hero)
    {
        if (_sim.Evolution.HasHero(player, hero)) return true;
        foreach (var b in Buildings.All)
            if (b.Owner == player && b.Queue.Contains(hero.Index)) return true;
        return false;
    }

    private void GainHonour(int player, int n)
    {
        if (n <= 0) return;
        var p = Player(player);
        p.Honour += n;
        p.HonourEarned += n;
        Contracts++;
    }

    private void LoseHonour(int player, int n)
    {
        if (n <= 0) return;
        var p = Player(player);
        int lost = Math.Min(n, p.Honour);
        p.Honour -= lost;
        p.HonourLost += lost;
    }

    /// <summary>
    /// Unit <paramref name="dead"/> died, killed by unit <paramref name="killer"/> (-1 for nobody): a rival's worker,
    /// caster or hero killed by a unit of an honour-keeping player's is an assassination, and an honour-keeping player's
    /// hero killed by anyone is a dishonour. Called before the dead are removed.
    /// </summary>
    internal void Slain(int dead, int killer)
    {
        var u = U;
        var def = Content.Units[u.Def[dead]];
        int owner = u.Owner[dead];
        if (def.Hero && HonourOf(owner) is { } mine) LoseHonour(owner, mine.HeroLost);
        if (killer < 0 || u.Town[dead] != 0 || (uint)owner >= Simulation.WildPlayer) return;
        int by = u.Owner[killer];
        if (by == owner || HonourOf(by) is not { } rules || !_sim.Truces.Hostile(by, owner) || !CombatSystem.Marked(def)) return;
        GainHonour(by, def.Hero ? rules.HeroSlain : rules.Assassination);
    }

    /// <summary>A sabotage of the player's was done: a contract.</summary>
    private void SabotageDone(int player)
    {
        if (HonourOf(player) is { } rules) GainHonour(player, rules.Sabotage);
    }

    /// <summary>
    /// Building b is gone from its owner, destroyed (crumbled, burned or fallen) or captured: a finished one of an
    /// honour-keeping player's, but a wall, gate, tower or bridge, is a dishonour, a town hall the greater.
    /// </summary>
    internal void BuildingLost(Building b)
    {
        if (!b.Active || b.Def.IsRampart || b.Def.IsBridge || HonourOf(b.Owner) is not { } rules) return;
        LoseHonour(b.Owner, b.Def.Kind == BuildingKind.TownHall ? rules.HallLost : rules.BuildingLost);
    }

    /// <summary>
    /// Once a second: each honour-keeping player with a unit of theirs within the scouting range of a rival's finished
    /// town hall it has not scouted yet scouts it, a contract (once a hall, for each player).
    /// </summary>
    private void StepHonour()
    {
        if (_sim.Tick % Tps != 0) return;
        var u = U;
        for (int player = 0; player < Simulation.WildPlayer; player++)
        {
            if (HonourOf(player) is not { } rules) continue;
            foreach (var b in Buildings.All)
            {
                if (b.Def.Kind != BuildingKind.TownHall || !b.Active || b.Owner == player || (uint)b.Owner >= Simulation.WildPlayer) continue;
                if (!_sim.Truces.Hostile(player, b.Owner) || Scouted(player, b.Id)) continue;
                for (int i = 0; i < u.Count; i++)
                {
                    if (u.Owner[i] != player || u.Hp[i] <= 0 || u.Town[i] != 0) continue;
                    if (b.ColumnDistance(u.X[i].FloorToInt(), u.Z[i].FloorToInt()) > rules.ScoutRange) continue;
                    _scouted.Add(((long)player << 32) | (uint)b.Id);
                    GainHonour(player, rules.Scouting);
                    break;
                }
            }
        }
    }

    private void MixHonour(ref ulong h)
    {
        h = (h ^ (ulong)Contracts) * 1099511628211UL;
        foreach (long k in _scouted) h = (h ^ (ulong)k) * 1099511628211UL;
    }
}
