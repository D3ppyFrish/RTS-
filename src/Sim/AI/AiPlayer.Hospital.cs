using System;
using Rts.Sim.Content;
using Rts.Sim.Economy;

namespace Rts.Sim.AI;

// Field hospitals at the smart levels (Easy and up). Once it knows Field hospital and has a finished barracks, it keeps
// one field hospital on the side of its town the enemy is on (where it last saw an enemy town hall or building),
// HospitalReach columns out from its town hall: past the hall's own healing reach, where raids come in and waves come
// home, so soldiers that fought there mend as at home without walking back to the hall. One at a time, as it can pay
// for it; Hard lays it out like the rest of its town. It researches Field hospital only once it knows where the enemy
// is, and Classic never does (see Wanted).
public sealed partial class AiPlayer
{
    /// <summary>How far (columns) from its town hall's middle, towards the enemy, its field hospital goes.</summary>
    public const int HospitalReach = 24;
    /// <summary>Columns round that spot in which it looks for room for the hospital.</summary>
    public const int HospitalRoom = 8;

    /// <summary>The smart levels: a field hospital toward the enemy (see the top of this file).</summary>
    private void Hospitals(Simulation sim, Building hall, Action<Command> issue)
    {
        if (!Smart || !hall.Active) return;
        var def = FirstBuilding(sim, BuildingKind.Infirmary);
        if (def == null) return;
        bool barracks = false;
        foreach (var b in sim.Buildings.All)
        {
            if (b.Owner != Player) continue;
            if (b.Def.Kind == BuildingKind.Infirmary) return; // it has one, or one is on its way
            if (IsBarracks(sim, b.Def) && b.Active) barracks = true;
        }
        if (!barracks) return;
        var home = Middle(hall);
        var toward = KnownHall(home.X, home.Z) is { } h ? (h.X, h.Z) : _lastFoe;
        if (toward is not var (tx, tz)) return; // no front to put it by yet
        if (!Affordable(sim, def.Cost)) return;
        var spot = Toward(home.X, home.Z, tx, tz, HospitalReach, sim.World);
        if (Hard)
        {
            if (!Room(sim, def, spot, HospitalRoom, out int x, out int z)) return;
            issue(new BuildCommand(Player, Array.Empty<int>(), def.Id, x, z));
            Spend(def.Cost);
        }
        else PlaceNear(sim, def, spot, HospitalRoom, issue);
        Note(sim, $"lays out a field hospital toward the enemy at {spot.X},{spot.Z}");
    }

    /// <summary>True once it knows where to put a field hospital: an enemy town hall, or the last enemy building it saw.</summary>
    private bool KnowsFront(Simulation sim)
    {
        if (_lastFoe != null) return true;
        var hall = sim.Economy.TownHall(Player);
        if (hall == null) return false;
        var home = Middle(hall);
        return KnownHall(home.X, home.Z) != null;
    }

    /// <summary>True for a tech whose only use is a field hospital, which only the smart levels put up.</summary>
    private static bool OpensHospital(Simulation sim, TechDef tech)
    {
        foreach (var b in sim.Content.Buildings)
            if (b.Kind == BuildingKind.Infirmary)
                foreach (var r in b.Requires)
                    if (r == tech) return true;
        return false;
    }
}
