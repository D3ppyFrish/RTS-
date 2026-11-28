using System;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.AI;

// Clan honour (Clan Kage's), at every level. Honour comes on its own as its waves kill rival workers and casters, its
// saboteurs sabotage and its scouts find rival town halls. It trains nothing that costs honour by the usual weights
// (see Train and Weight); instead, a barracks that can train such a hero (the master ninja) trains one as soon as the
// player has the honour and the goods for it and has none living or queued, before anything else there. The hero then
// fights in the army as any soldier does.
public sealed partial class AiPlayer
{
    /// <summary>
    /// The unit costing honour that barracks b should train now, if any: unlocked, a hero the player has none of
    /// (living or queued), with the honour for it in hand and its goods affordable.
    /// </summary>
    private UnitDef? HonourHero(Simulation sim, Building b)
    {
        var econ = sim.Economy;
        if (econ.HonourOf(Player) == null) return null;
        foreach (string id in b.Def.Trains)
        {
            if (!sim.Content.TryGetUnit(id, out var d) || d.Honour <= 0 || !econ.Unlocked(Player, d)) continue;
            if (!econ.HasHonour(Player, d.Honour) || (d.Hero && econ.HeroTaken(Player, d)) || !Affordable(sim, d.Cost)) continue;
            return d;
        }
        return null;
    }

    /// <summary>Trains the hero costing honour at barracks b if it should (see <see cref="HonourHero"/>); true if it did.</summary>
    private bool TrainHonourHero(Simulation sim, Building b, Action<Command> issue)
    {
        if (HonourHero(sim, b) is not { } hero) return false;
        issue(new TrainCommand(Player, b.Id, hero.Id));
        if (Hard) Spend(hero.Cost);
        Note(sim, $"trains its {hero.Name.ToLowerInvariant()} for honour");
        return true;
    }
}
