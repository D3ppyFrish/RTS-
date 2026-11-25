using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.AI;

// Prospecting (issue #146), at the smart levels (Easy and up). Once it has Better tools, it sounds the ground once by each storage
// building it has (its town hall and storehouses), as each is finished: the nearest unit of its that can (a
// Settler foreman, an Orc or Deepfolk worker) that is not busy building goes there and sounds it. Felling trees onto
// rivals: see AiPlayer.Fell.cs.
public sealed partial class AiPlayer
{
    // Storage buildings it has sounded the ground by (or sent a prospector to), by id.
    private readonly SortedSet<int> _sounded = new();

    /// <summary>Sends a prospector to the first storage building of its not sounded yet (one at a time).</summary>
    private void Prospect(Simulation sim, Action<Command> issue)
    {
        var econ = sim.Economy;
        if (econ.ProspectRadius(Player) <= 0) return;
        var u = sim.Units;
        for (int i = 0; i < u.Count; i++)
            if (u.Owner[i] == Player && u.Job[i] == UnitJob.Prospect) return; // one is on its way already
        foreach (var b in sim.Buildings.All)
        {
            if (b.Owner != Player || !b.Active || !b.Def.Storage || _sounded.Contains(b.Id)) continue;
            // Just outside its south side, where the prospector can stand.
            int x = Middle(b).X, z = Math.Min(sim.World.SizeZ - 1, b.MaxZ + 2);
            int best = -1;
            long bestD = long.MaxValue;
            for (int i = 0; i < u.Count; i++)
            {
                if (u.Owner[i] != Player || u.Rout[i] > 0 || u.Job[i] == UnitJob.Build) continue;
                if (!econ.CanProspect(Player, sim.Content.Units[u.Def[i]])) continue;
                long dx = u.X[i].FloorToInt() - x, dz = u.Z[i].FloorToInt() - z, d = dx * dx + dz * dz;
                if (d < bestD) (best, bestD) = (i, d);
            }
            if (best < 0) return;
            _sounded.Add(b.Id);
            issue(new ProspectCommand(Player, new[] { u.Id[best] }, x, z));
            Note(sim, $"sounds the ground by its {b.Def.Name.ToLowerInvariant()} for ore");
            return;
        }
    }
}
