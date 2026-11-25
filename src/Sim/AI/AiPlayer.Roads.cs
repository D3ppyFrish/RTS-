using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.AI;

/// <summary>
/// Roads (see <see cref="RoadCommand"/>: a straight line of road, 1 stone a block, twice as fast to walk on). Once the
/// smart levels (Easy and up) have the Roads tech, they lay a road from their town hall to each of their finished
/// storehouses out by their work, the way their carts and ponies run goods home and their workers walk out, one road at
/// a time with stone to spare. A road runs from just outside the hall to just outside the storehouse; it is not laid
/// when it would be short, longer than one road can be, cross one of its walls or towers, or run over a hazard, ground
/// it keeps off, or (on Hard) its pits. Hard lays them only while its town is calm. The classic level never lays roads.
/// The numbers are first guesses for playtesting.
/// </summary>
public sealed partial class AiPlayer
{
    /// <summary>Stone it keeps in hand on top of a road's price (and what it keeps for other things) before it lays the road.</summary>
    public const int RoadStoneSpare = 100;

    /// <summary>Fewer new road blocks than this along the way and it does not bother: the storehouse is close by.</summary>
    public const int RoadLeast = 6;

    /// <summary>Ticks between its roads: 20 seconds.</summary>
    public const int RoadTicks = 20 * Tps;

    // The storehouses it has laid a road to or given up on, by id; and the tick before which it lays no other.
    private readonly List<int> _roaded = new();
    private long _roadAt;

    /// <summary>The storehouses it has laid a road to, or found no road worth laying to, by id.</summary>
    public IReadOnlyList<int> Roaded => _roaded;

    /// <summary>The smart levels' roads from the town hall to its storehouses (see the top of this file).</summary>
    private void Roads(Simulation sim, Building hall, Action<Command> issue)
    {
        if (!Smart || !hall.Active || sim.Tick < _roadAt || !sim.Economy.CanLayRoads(Player)) return;
        foreach (var b in sim.Buildings.All)
        {
            if (b.Owner != Player || !b.Active || !b.Def.Storage || b == hall || _roaded.Contains(b.Id)) continue;
            var (x1, z1, x2, z2) = RoadEnds(hall, b);
            int blocks = RoadBlocks(sim, x1, z1, x2, z2);
            if (blocks < RoadLeast)
            {
                _roaded.Add(b.Id); // close by, too far for one road, or in the way of its wall or a hazard
                continue;
            }
            var stock = sim.Economy.Stockpile(Player);
            if (stock[(int)Good.Stone] < blocks * EconomySystem.StonePerRoad + RoadStoneSpare + _keep[(int)Good.Stone]) return;
            issue(new RoadCommand(Player, x1, z1, x2, z2));
            if (Hard) Spend(new[] { new GoodAmount(Good.Stone, blocks * EconomySystem.StonePerRoad) });
            _roaded.Add(b.Id);
            _roadAt = sim.Tick + RoadTicks;
            Note(sim, $"lays a road of {blocks} blocks from its town hall to its storehouse at {b.X},{b.Z}");
            return;
        }
    }

    /// <summary>A road's ends: the column just outside the hall nearest the storehouse, and the one just outside the storehouse nearest the hall.</summary>
    public static (int X1, int Z1, int X2, int Z2) RoadEnds(Building hall, Building store)
    {
        var (hx, hz) = Middle(hall);
        var (sx, sz) = Middle(store);
        int x1 = Math.Clamp(sx, hall.X - 1, hall.X + hall.Def.SizeX), z1 = Math.Clamp(sz, hall.Z - 1, hall.Z + hall.Def.SizeZ);
        int x2 = Math.Clamp(hx, store.X - 1, store.X + store.Def.SizeX), z2 = Math.Clamp(hz, store.Z - 1, store.Z + store.Def.SizeZ);
        return (x1, z1, x2, z2);
    }

    /// <summary>
    /// How many blocks of road a line from (x1, z1) to (x2, z2) would lay, or 0 if it should not be laid: longer than one
    /// road reaches, or over one of its walls or towers (a gate is fine), a hazard, bog or quicksand, ground it keeps off,
    /// or (on Hard) one of its pits.
    /// </summary>
    private int RoadBlocks(Simulation sim, int x1, int z1, int x2, int z2)
    {
        if (Math.Max(Math.Abs(x2 - x1), Math.Abs(z2 - z1)) >= EconomySystem.MaxRoad) return 0;
        var econ = sim.Economy;
        var w = sim.World;
        int blocks = 0;
        foreach (var (x, z) in EconomySystem.RoadLine(x1, z1, x2, z2))
        {
            if (sim.Buildings.AtColumn(x, z) is { } on && (on.Def.Kind is BuildingKind.Wall or BuildingKind.Tower)) return 0;
            if (KeepsOff(sim, x, z) || (Hard && InPit(x, z))) return 0;
            if (!econ.RoadGoesOn(x, z, out int y)) continue;
            if (World.HazardSystem.IsHazard(w, x, y, z) || w.Get(x, y - 1, z) is BlockType.Bog or BlockType.Quicksand) return 0;
            blocks++;
        }
        return blocks;
    }

    /// <summary>True if column (x, z) is in or by one of Hard's pits.</summary>
    private bool InPit(int x, int z)
    {
        foreach (var (px1, pz1, px2, pz2) in _pits)
            if (x >= px1 - 1 && x <= px2 + 1 && z >= pz1 - 1 && z <= pz2 + 1) return true;
        return false;
    }
}
