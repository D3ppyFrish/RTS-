using System;
using Rts.Sim.Economy;

namespace Rts.Sim.AI;

// The Fey's fairy rings (mana from nature; see "The Fey" in docs/rules.md). At every level a computer player of theirs
// lays out a fairy ring in the woods near its town hall as soon as it can pay for one: of the spots it looks at, every
// RingStep columns out to RingRoom from the hall, the one whose ring would count the most trees (at least RingLeast),
// the nearest of those. A second once the first is finished. It never fells those trees (EconomySystem.Spared), and its
// automatic workers do not either.
public sealed partial class AiPlayer
{
    /// <summary>Columns round its town hall's middle in which it looks for woods for its fairy rings.</summary>
    public const int RingRoom = 48;
    /// <summary>Columns between the spots it weighs for a fairy ring.</summary>
    public const int RingStep = 4;
    /// <summary>Fewest trees a fairy ring must count where it lays one out.</summary>
    public const int RingLeast = 4;
    /// <summary>Most fairy rings it keeps.</summary>
    public const int RingsWanted = 2;
    /// <summary>Ticks between its looks for woods when it found no room for a ring.</summary>
    private const int RingLookTicks = 20 * Simulation.TicksPerSecond;

    private long _ringLook;

    /// <summary>Lays out a fairy ring in the woods by its town hall (see the file's header).</summary>
    private void FairyRing(Simulation sim, Building hall, BuildingDef def, Action<Command> issue)
    {
        if (sim.Tick < _ringLook) return;
        var rings = sim.Economy.NatureRings(Player);
        if (rings.Count >= RingsWanted) return;
        foreach (var r in rings)
            if (!r.Active) return; // one at a time
        if (!Affordable(sim, def.Cost)) return;
        var (hx, hz) = Middle(hall);
        int bestTrees = RingLeast - 1, bx = -1, bz = -1;
        long bestD = long.MaxValue;
        for (int dz = -RingRoom; dz <= RingRoom; dz += RingStep)
        for (int dx = -RingRoom; dx <= RingRoom; dx += RingStep)
        {
            long d = (long)dx * dx + (long)dz * dz;
            if (d > (long)RingRoom * RingRoom) continue;
            int x = hx + dx - def.SizeX / 2, z = hz + dz - def.SizeZ / 2;
            if (Smart && sim.Vision.At(Player, x, z) == Sight.Unseen) continue; // only woods it has seen
            if (sim.Economy.CanPlace(Player, def, x, z, out _) != PlaceResult.Ok || !SafeGround(sim, def, x, z)) continue;
            bool overlaps = false;
            foreach (var r in rings)
                if (Math.Abs(r.X - x) < 2 * def.Nature!.Reach && Math.Abs(r.Z - z) < 2 * def.Nature.Reach) overlaps = true;
            if (overlaps) continue; // a second ring in other woods: trees count for one ring only in its eyes
            int trees = sim.Economy.TreesAround(def, x, z, Player);
            if (trees < bestTrees || trees == bestTrees && d >= bestD) continue;
            (bestTrees, bestD, bx, bz) = (trees, d, x, z);
        }
        if (bx < 0)
        {
            _ringLook = sim.Tick + RingLookTicks;
            return;
        }
        issue(new BuildCommand(Player, Array.Empty<int>(), def.Id, bx, bz));
        if (Hard) Spend(def.Cost);
        _ringLook = sim.Tick + RingLookTicks; // the site shows up next look
        Note(sim, $"lays out a fairy ring at {bx},{bz} among {bestTrees} trees");
    }
}
