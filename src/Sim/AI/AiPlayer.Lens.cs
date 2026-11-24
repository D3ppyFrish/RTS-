using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.AI;

// The rare discovery techs at the smart levels (Easy and up). They research them as they do the other discovery
// techs (see InLine). With Crystal lens it keeps one crystal lens by its town hall, and the moment it sends a wave
// it aims the lens at where the wave is going, so the wave's target and what guards it are in sight as it marches;
// Hard, which waits for a look at their town before a wave, takes the lens's gaze as that look. With Cave guides it
// keeps a delver once a rival's great work stands in the deep caves, and sends its delvers on ahead of the strike
// force to the great work, where they hear what is underground round it.
public sealed partial class AiPlayer
{
    /// <summary>Columns round its town hall's middle in which it looks for room for its crystal lens.</summary>
    public const int LensRoom = 10;
    /// <summary>Delvers it keeps while a rival's great work stands.</summary>
    public const int Delvers = 1;

    /// <summary>The smart levels: one crystal lens by the town hall once it has the tech, as it can pay for it.</summary>
    private void CrystalLens(Simulation sim, Building hall, Action<Command> issue)
    {
        if (!Smart || !hall.Active) return;
        var def = FirstBuilding(sim, BuildingKind.Lens);
        if (def == null) return;
        foreach (var b in sim.Buildings.All)
            if (b.Owner == Player && b.Def.Kind == BuildingKind.Lens) return; // one is enough (or on its way)
        if (!Affordable(sim, def.Cost)) return;
        var home = Middle(hall);
        if (Hard)
        {
            if (!Room(sim, def, home, LensRoom, out int x, out int z)) return;
            issue(new BuildCommand(Player, Array.Empty<int>(), def.Id, x, z));
            Spend(def.Cost);
        }
        else PlaceNear(sim, def, home, LensRoom, issue);
        Note(sim, "lays out a crystal lens");
    }

    /// <summary>Its crystal lens that can gaze now, or null.</summary>
    private Building? ReadyLens(Simulation sim)
    {
        foreach (var b in sim.Buildings.All)
            if (b.Owner == Player && b.Def.Kind == BuildingKind.Lens && sim.Economy.CanGaze(Player, b) == GazeResult.Ok) return b;
        return null;
    }

    /// <summary>Aims its crystal lens at column (x, z) if it has one ready. True if it did.</summary>
    private bool GazeAt(Simulation sim, int x, int z, Action<Command> issue)
    {
        if (!Smart || !sim.Fog || ReadyLens(sim) is not { } lens) return false;
        issue(new GazeCommand(Player, lens.Id, x, z));
        Note(sim, $"aims its crystal lens at {x},{z}");
        return true;
    }

    /// <summary>True for a unit that knows the deep caves (the delver).</summary>
    private static bool IsDelver(Simulation sim, int i) => sim.Content.Units[sim.Units.Def[i]].CaveWise;

    /// <summary>
    /// While a rival's great work stands (at column (x, z)): it trains a delver at its town hall if it has none and can
    /// (see <see cref="Delvers"/>), and sends its delvers there ahead of the strike force. They are taken out of
    /// <paramref name="home"/>.
    /// </summary>
    private void SendDelvers(Simulation sim, Building hall, List<int> home, int x, int z, Action<Command> issue)
    {
        var u = sim.Units;
        var delvers = new List<int>();
        for (int i = 0; i < u.Count; i++)
            if (u.Owner[i] == Player && u.Hp[i] > 0 && IsDelver(sim, i)) delvers.Add(u.Id[i]);
        home.RemoveAll(delvers.Contains);
        if (delvers.Count < Delvers && hall.Active && hall.Queue.Count == 0)
        {
            foreach (string id in hall.Def.Trains)
            {
                if (!sim.Content.TryGetUnit(id, out var d) || !d.CaveWise || !sim.Economy.Unlocked(Player, d) || !Affordable(sim, d.Cost)) continue;
                issue(new TrainCommand(Player, hall.Id, d.Id));
                if (Hard) Spend(d.Cost);
                Note(sim, "trains a delver to go ahead of its strike force");
                break;
            }
        }
        var go = new List<int>();
        foreach (int id in delvers)
        {
            int i = u.IndexOf(id);
            if (i < 0) continue;
            long dx = u.X[i].FloorToInt() - x, dz = u.Z[i].FloorToInt() - z;
            if (dx * dx + dz * dz > 4 * 4 && (u.Order[i] == UnitOrder.Idle || sim.Tick % (5 * ArmyTicks) < ArmyTicks)) go.Add(id);
        }
        if (go.Count > 0) issue(new MoveCommand(Player, go.ToArray(), x, z));
    }
}
