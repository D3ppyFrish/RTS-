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
/// it keeps off, or (on Hard) its pits. From Normal up it also lays a road from the hall toward the front, the way its
/// waves march, and one from its nearest storage to each quarry, mine and smelter out at its work; and while the ground
/// is wet or it rains it lays them twice as often with less stone in hand, since roads keep their pace in the mud. Hard
/// lays them only while its town is calm. The classic level never lays roads. The numbers are first guesses for playtesting.
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

    /// <summary>Ticks between its roads while the ground is wet or it rains, from Normal up: 10 seconds.</summary>
    public const int WetRoadTicks = 10 * Tps;

    /// <summary>Stone it keeps in hand on top of a road's price while the ground is wet or it rains, from Normal up.</summary>
    public const int WetRoadStoneSpare = 50;

    /// <summary>How far its road toward the front runs out past the town hall's wall, from Normal up.</summary>
    public const int FrontRoadLength = 40;

    // From Normal up: whether its road toward the front is laid or given up on.
    private bool _frontRoaded;

    /// <summary>True once it has laid its road toward the front, or found none worth laying.</summary>
    public bool FrontRoaded => _frontRoaded;

    /// <summary>True if the ground is damp or it rains or storms: mud is here or on its way, and roads keep their pace in it.</summary>
    public static bool WetWeather(Simulation sim) =>
        sim.Conditions.Damp || sim.Conditions.Weather is Weather.Rain or Weather.Storm;

    private enum RoadTry { Skip, Laid, Wait }

    /// <summary>The smart levels' roads from the town hall to its storehouses, and from Normal up to the front and its work sites (see the top of this file).</summary>
    private void Roads(Simulation sim, Building hall, Action<Command> issue)
    {
        if (!Smart || !hall.Active || sim.Tick < _roadAt || !sim.Economy.CanLayRoads(Player)) return;
        bool wet = Skilled && WetWeather(sim);
        foreach (var b in sim.Buildings.All)
        {
            if (b.Owner != Player || !b.Active || !b.Def.Storage || b == hall || _roaded.Contains(b.Id)) continue;
            var (x1, z1, x2, z2) = RoadEnds(hall, b);
            var r = TryRoad(sim, x1, z1, x2, z2, wet, issue, $"from its town hall to its storehouse at {b.X},{b.Z}");
            if (r == RoadTry.Wait) return;
            _roaded.Add(b.Id); // laid, or close by, too far for one road, or in the way of its wall or a hazard
            if (r == RoadTry.Laid) return;
        }
        if (!Skilled) return;
        if (!_frontRoaded)
        {
            var (hx, hz) = Middle(hall);
            if (EnemyHall(sim, hx, hz) is { } foe)
            {
                var (x1, z1, x2, z2) = FrontRoadEnds(sim, hall, foe.X, foe.Z);
                var r = TryRoad(sim, x1, z1, x2, z2, wet, issue, "from its town hall toward the front");
                if (r == RoadTry.Wait) return;
                _frontRoaded = true;
                if (r == RoadTry.Laid) return;
            }
        }
        foreach (var b in sim.Buildings.All)
        {
            if (b.Owner != Player || !b.Active || b.Def.Kind is not (BuildingKind.Quarry or BuildingKind.Smelter) || _roaded.Contains(b.Id)) continue;
            if (NearestStorage(sim, b) is not { } store) continue;
            var (x1, z1, x2, z2) = RoadEnds(store, b);
            var r = TryRoad(sim, x1, z1, x2, z2, wet, issue, $"from its storehouse at {store.X},{store.Z} to its {b.Def.Name.ToLowerInvariant()} at {b.X},{b.Z}");
            if (r == RoadTry.Wait) return;
            _roaded.Add(b.Id);
            if (r == RoadTry.Laid) return;
        }
    }

    /// <summary>Lays the road from (x1, z1) to (x2, z2) if it is worth it and there is the stone: Skip if it never will be, Wait for stone.</summary>
    private RoadTry TryRoad(Simulation sim, int x1, int z1, int x2, int z2, bool wet, Action<Command> issue, string what)
    {
        int blocks = RoadBlocks(sim, x1, z1, x2, z2);
        if (blocks < RoadLeast) return RoadTry.Skip;
        var stock = sim.Economy.Stockpile(Player);
        int spare = wet ? WetRoadStoneSpare : RoadStoneSpare;
        if (stock[(int)Good.Stone] < blocks * EconomySystem.StonePerRoad + spare + _keep[(int)Good.Stone]) return RoadTry.Wait;
        issue(new RoadCommand(Player, x1, z1, x2, z2));
        if (Hard) Spend(new[] { new GoodAmount(Good.Stone, blocks * EconomySystem.StonePerRoad) });
        _roadAt = sim.Tick + (wet ? WetRoadTicks : RoadTicks);
        Note(sim, $"lays a road of {blocks} blocks {what}{(wet ? " before the mud" : "")}");
        return RoadTry.Laid;
    }

    /// <summary>Its finished storage building (the town hall too) nearest <paramref name="site"/>, or null.</summary>
    private Building? NearestStorage(Simulation sim, Building site)
    {
        var (sx, sz) = Middle(site);
        Building? best = null;
        long bestD = long.MaxValue;
        foreach (var b in sim.Buildings.All)
        {
            if (b.Owner != Player || !b.Active || !b.Def.Storage) continue;
            var (bx, bz) = Middle(b);
            long d = Dist2(bx, bz, sx, sz);
            if (d < bestD) (bestD, best) = (d, b);
        }
        return best;
    }

    /// <summary>
    /// Its road toward the front: from the column just outside the hall nearest the enemy at (ex, ez), straight toward
    /// it for <see cref="FrontRoadLength"/> blocks, no further than halfway there and inside the map.
    /// </summary>
    public static (int X1, int Z1, int X2, int Z2) FrontRoadEnds(Simulation sim, Building hall, int ex, int ez)
    {
        var (hx, hz) = Middle(hall);
        long dx = ex - hx, dz = ez - hz;
        long len = Math.Max(1, IntMath.ISqrt(dx * dx + dz * dz));
        int x1 = Math.Clamp(hx + (int)(dx * hall.Def.SizeX / len), hall.X - 1, hall.X + hall.Def.SizeX);
        int z1 = Math.Clamp(hz + (int)(dz * hall.Def.SizeZ / len), hall.Z - 1, hall.Z + hall.Def.SizeZ);
        long reach = Math.Min(FrontRoadLength, Math.Max(0, len / 2 - Math.Max(hall.Def.SizeX, hall.Def.SizeZ) / 2));
        int x2 = Math.Clamp(x1 + (int)(dx * reach / len), 1, sim.World.SizeX - 2);
        int z2 = Math.Clamp(z1 + (int)(dz * reach / len), 1, sim.World.SizeZ - 2);
        return (x1, z1, x2, z2);
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
