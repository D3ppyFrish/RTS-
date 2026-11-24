using System;
using Rts.Sim.Economy;

namespace Rts.Sim.AI;

// Orchards (Crop rotation, issue #148), at every level: once it has the tech, it lays out an orchard near its town
// hall, and a second once the first has grown (MostOrchards), one at a time as it can pay. Its automatic workers carry
// the fruit in. Hard lays them out like the rest of its town (clear of its wall line and pits).
public sealed partial class AiPlayer
{
    /// <summary>Orchards it keeps by its town.</summary>
    public const int MostOrchards = 2;

    private void Orchards(Simulation sim, Building hall, Action<Command> issue)
    {
        if (!hall.Active) return;
        var def = FirstBuilding(sim, BuildingKind.Orchard);
        if (def == null) return;
        int orchards = 0;
        foreach (var b in sim.Buildings.All)
        {
            if (b.Owner != Player || b.Def.Kind != BuildingKind.Orchard) continue;
            if (!b.Active || !b.Grown) return; // one on its way, or still growing
            orchards++;
        }
        if (orchards >= MostOrchards || !Affordable(sim, def.Cost)) return;
        if (Hard) Want(sim, def, hall, HardTownRadius, false, issue);
        else PlaceNear(sim, def, Middle(hall), Automation.AutomationSystem.PlanRadius, issue);
    }
}
