using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.AI;

/// <summary>
/// Fishing (see <see cref="EconomySystem"/>'s fishing hut, a quarry for food at the water's edge). The smart levels
/// (Easy and up) put up one fishing hut on the shore they have seen nearest their town hall, once their town is going
/// and while food is short, keep its places filled with fishers while food stays short (once food piles up the fishers
/// go back to other work), and put a storehouse by it when the walk home with the fish is long. Hard fishes only
/// inside its town and leaves the fishing to its automatic workers. The classic level never fishes. The numbers are first guesses for playtesting.
/// </summary>
public sealed partial class AiPlayer
{
    /// <summary>No fishing hut before this tick (the first farms and houses come first): 2 minutes in.</summary>
    public const int FishFromTicks = 2 * 60 * Tps;

    /// <summary>No fishing hut before it has this many workers.</summary>
    public const int FishWorkers = 6;

    /// <summary>Food in hand at or above which it lays out no fishing hut and lets its fishers go.</summary>
    public const int FishFoodPlenty = 300;

    /// <summary>Food in hand below which it sends fishers out again after letting them go (so they are not sent and let go every few seconds).</summary>
    public const int FishFoodShort = 200;

    /// <summary>How far (columns) from its town hall Easy and Normal look for a shore to fish from (the usual map's starts are 40 to 50 from the water; Hard looks only within <see cref="HardTownRadius"/>).</summary>
    public const int FishRadius = 64;

    /// <summary>Ticks between its looks for a shore when the last one found none: a minute.</summary>
    public const int FishLookTicks = 60 * Tps;

    // Its fishers, by id; and the tick before which it does not look for a shore again.
    private readonly List<int> _fishers = new();
    private long _fishLook;

    /// <summary>The workers it keeps at its fishing hut, by id.</summary>
    public IReadOnlyList<int> Fishers => _fishers;

    /// <summary>
    /// The smart levels' fishing: a hut on the shore near the town hall when it helps (see <see cref="FishFromTicks"/>,
    /// <see cref="FishWorkers"/>, <see cref="FishFoodPlenty"/>), and fishers for it while food is short.
    /// </summary>
    private void Fishing(Simulation sim, Building hall, Action<Command> issue)
    {
        var def = FishingHut(sim);
        if (def == null) return;
        Building? hut = null;
        bool site = false;
        foreach (var b in sim.Buildings.All)
        {
            if (b.Owner != Player || b.Def != def) continue;
            if (b.Active) hut ??= b;
            else site = true;
        }

        var u = sim.Units;
        _fishers.RemoveAll(id => u.IndexOf(id) is var i && (i < 0 || u.Job[i] != UnitJob.Quarry || hut == null || u.JobTarget[i] != hut.Id));
        int food = sim.Economy.Stock(Player, Good.Food);
        if (hut == null)
        {
            if (site || sim.Tick < FishFromTicks || sim.Tick < _fishLook) return;
            if (food >= FishFoodPlenty || Workers(sim) < FishWorkers) return;
            // A hut is a help, not a need: it is not saved up for (on Hard that would hold back the goods its town
            // and army want), only built when the goods are there.
            if (!Affordable(sim, def.Cost)) return;
            var (hx, hz) = Middle(hall);
            // Only shore it has looked at (its corner and the far one); Hard only inside the town it walls in, not out
            // where the hut and its fishers would need a storehouse and guarding of their own.
            bool Seen(int bx, int bz) => sim.Vision.At(Player, bx, bz) != Sight.Unseen
                && sim.Vision.At(Player, bx + def.SizeX - 1, bz + def.SizeZ - 1) != Sight.Unseen;
            if (!sim.Automation.FindRoom(Player, def, hx, hz, Hard ? HardTownRadius : FishRadius, out int x, out int z, (bx, bz) => ShoreGround(sim, def, bx, bz) && Seen(bx, bz)))
            {
                _fishLook = sim.Tick + FishLookTicks;
                return;
            }
            issue(new BuildCommand(Player, Array.Empty<int>(), def.Id, x, z));
            if (Hard) Spend(def.Cost);
            Note(sim, $"lays out a fishing hut on the shore at {x},{z}");
            return;
        }

        // Workers of its own fishing at the hut are its fishers, whoever sent them (automatic workers fish there too
        // when food is the good needed most).
        for (int i = 0; i < u.Count; i++)
            if (u.Owner[i] == Player && u.Job[i] == UnitJob.Quarry && u.JobTarget[i] == hut.Id && !_fishers.Contains(u.Id[i]))
                _fishers.Add(u.Id[i]);

        // Hard sends no fishers of its own: it keeps its food spent on soldiers, so food in hand is always short, and
        // two workers kept fishing would be two fewer on wood all match. Its automatic workers fish there when food
        // is the good it needs most (they count as its fishers).
        if (!Hard && food >= FishFoodPlenty)
        {
            if (_fishers.Count > 0)
            {
                Note(sim, "lets its fishers go, food is plenty");
                issue(new StopCommand(Player, _fishers.ToArray()));
                issue(new AutomateCommand(Player, _fishers.ToArray(), true));
                _fishers.Clear();
            }
        }
        else if (!Hard && _fishers.Count < hut.Def.WorkerSlots && (_fishers.Count > 0 || food < FishFoodShort))
        {
            var more = SpareWorkers(sim, hut.Def.WorkerSlots - _fishers.Count);
            if (more.Count > 0)
            {
                issue(new WorkCommand(Player, more.ToArray(), hut.Id));
                _fishers.AddRange(more);
            }
        }

        // A storehouse by the hut when the fish have far to go.
        if (_fishers.Count > 0) StoreBy(sim, hut, "its fishing hut", issue);
    }

    /// <summary>The fishing hut its faction can build (the first shore building in the data that gives food), or null.</summary>
    private BuildingDef? FishingHut(Simulation sim)
    {
        foreach (var b in sim.Content.Buildings)
        {
            if (!b.Shore || b.Kind != BuildingKind.Quarry || !sim.Economy.Unlocked(Player, b)) continue;
            foreach (var g in b.Works)
                if (g.Good == Good.Food) return b;
        }
        return null;
    }

    /// <summary>
    /// Safe ground for a building at the water's edge with its corner at (x, z): no hazard under the building itself
    /// (the water beside it, which a unit would drown in, is what it is for), off ground it keeps off, and on Hard clear
    /// of its pits and its wall.
    /// </summary>
    private bool ShoreGround(Simulation sim, BuildingDef def, int x, int z) =>
        !World.HazardSystem.HazardNear(sim.World, x + 1, z + 1, def.SizeX - 2, def.SizeZ - 2)
        && !KeepsOff(sim, x - 1, z - 1, x + def.SizeX, z + def.SizeZ)
        && (!Hard || ClearOfPits(def, x, z) && ClearOfWall(def, x, z));
}
