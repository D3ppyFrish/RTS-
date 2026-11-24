using System;

namespace Rts.Sim.Economy;

/// <summary>
/// Orchards (Crop rotation, issue #148): a field planted with fruit trees (<see cref="BuildingKind.Orchard"/>). When it
/// is finished its saplings are planted; <see cref="BuildingDef.GrowTicks"/> later they have grown into a block of fruit
/// trees (<see cref="BlockType.Orchard"/>). A grown orchard bears a basket of its <see cref="BuildingDef.Yield"/> every
/// <see cref="BuildingDef.YieldTicks"/> with nobody working it, up to <see cref="OrchardBaskets"/> baskets waiting, which
/// automatic workers and carts carry to storage. Its trees are too thick to see or shoot through, so an orchard screens a
/// flank, and units push through them at half pace. Raiders who burn or take it cut it down: the baskets are lost and it
/// grows again from saplings.
/// </summary>
public sealed partial class EconomySystem
{
    /// <summary>Most baskets of fruit that wait at an orchard to be carried away; the trees bear no more until one is.</summary>
    public const int OrchardBaskets = 3;

    /// <summary>Columns between an orchard's trees: each tree's crown is three columns across.</summary>
    public const int OrchardSpacing = 3;

    /// <summary>The food waiting at orchard b in baskets.</summary>
    public static int Baskets(Building b) => b.Def.Kind == BuildingKind.Orchard && b.Def.Yield.Length > 0
        ? b.Stock[(int)b.Def.Yield[0].Good] / Math.Max(1, b.Def.Yield[0].Amount) : 0;

    /// <summary>How far through growing orchard b's trees are, in percent (100 once grown, 0 for anything but an orchard).</summary>
    public int GrowthPercent(Building b)
    {
        if (b.Def.Kind != BuildingKind.Orchard || !b.Active) return 0;
        if (b.Grown) return 100;
        return (int)Math.Clamp((_sim.Tick - b.Planted) * 100 / Math.Max(1, b.Def.GrowTicks), 0, 99);
    }

    /// <summary>
    /// The columns of orchard b's tree trunks (relative to its corner): a tree every <see cref="OrchardSpacing"/>
    /// columns, one in from the edge.
    /// </summary>
    private static bool Trunk(BuildingDef def, int x, int z) =>
        x % OrchardSpacing == 1 && z % OrchardSpacing == 1 && x <= def.SizeX - 2 && z <= def.SizeZ - 2;

    /// <summary>The trunk column nearest to column c along an axis <paramref name="size"/> columns long.</summary>
    private static int NearestTrunk(int c, int size) =>
        Math.Clamp(c / OrchardSpacing * OrchardSpacing + 1, 1, Math.Max(1, (size - 3) / OrchardSpacing * OrchardSpacing + 1));

    /// <summary>
    /// Whether orchard b's grown trees fill level <paramref name="up"/> over its ground at column (x, z) (relative to its
    /// corner): the trunks at the foot, the crowns three columns across over them, narrowing at the top.
    /// </summary>
    private static bool Crown(BuildingDef def, int x, int z, int up)
    {
        // The trunk this column's crown belongs to: the nearest along each axis.
        int tx = NearestTrunk(x, def.SizeX), tz = NearestTrunk(z, def.SizeZ);
        int dx = Math.Abs(x - tx), dz = Math.Abs(z - tz);
        return up switch
        {
            0 => dx == 0 && dz == 0,
            1 => true,
            2 => dx + dz <= 1 || (dx + dz == 2 && (x + z) % 2 == 0),
            3 => dx == 0 && dz == 0,
            _ => false,
        };
    }

    /// <summary>Orchard b is planted (again): its trees, if any, are gone, and saplings stand at the trunks.</summary>
    internal void Replant(Building b)
    {
        ClearTrees(b);
        b.Planted = _sim.Tick;
        b.Grown = false;
        var w = World;
        for (int z = 0; z < b.Def.SizeZ; z++)
        for (int x = 0; x < b.Def.SizeX; x++)
            if (Trunk(b.Def, x, z) && w.Get(b.X + x, b.Y, b.Z + z) == BlockType.Air) w.Set(b.X + x, b.Y, b.Z + z, BlockType.Orchard);
    }

    /// <summary>Takes orchard b's trees and saplings off the map (cut down, or the orchard is gone).</summary>
    internal void ClearTrees(Building b)
    {
        if (b.Def.Kind != BuildingKind.Orchard) return;
        var w = World;
        for (int up = 0; up <= 3; up++)
        for (int z = b.Z; z <= b.MaxZ; z++)
        for (int x = b.X; x <= b.MaxX; x++)
            if (w.InBounds(x, b.Y + up, z) && w.Get(x, b.Y + up, z) == BlockType.Orchard) w.Set(x, b.Y + up, z, BlockType.Air);
    }

    /// <summary>Orchard b's saplings have grown: its trees fill the air over it (nothing already there is replaced).</summary>
    private void GrowOrchard(Building b)
    {
        b.Grown = true;
        var w = World;
        for (int up = 0; up <= 3; up++)
        for (int z = 0; z < b.Def.SizeZ; z++)
        for (int x = 0; x < b.Def.SizeX; x++)
        {
            int wx = b.X + x, wy = b.Y + up, wz = b.Z + z;
            if (Crown(b.Def, x, z, up) && w.InBounds(wx, wy, wz) && w.Get(wx, wy, wz) == BlockType.Air) w.Set(wx, wy, wz, BlockType.Orchard);
        }
    }

    /// <summary>Once a second: orchards whose saplings are old enough grow.</summary>
    private void StepOrchards()
    {
        if (_sim.Tick % Tps != 0) return;
        foreach (var b in Buildings.All)
            if (b.Def.Kind == BuildingKind.Orchard && b.Active && !b.Grown && _sim.Tick - b.Planted >= b.Def.GrowTicks) GrowOrchard(b);
    }

    /// <summary>A grown orchard bears a basket (its <see cref="BuildingDef.Yield"/>), while fewer than <see cref="OrchardBaskets"/> wait.</summary>
    private void Bear(Building b)
    {
        if (!b.Grown || Baskets(b) >= OrchardBaskets) return;
        var p = Player(b.Owner);
        foreach (var g in b.Def.Yield)
        {
            b.Stock[(int)g.Good] += g.Amount;
            p.Gathered[(int)g.Good] += g.Amount;
        }
    }

    /// <summary>Raiders have burned or taken orchard b: they cut it down, its baskets are lost, and it grows again from saplings.</summary>
    private void CutDown(Building b)
    {
        Array.Clear(b.Stock);
        Replant(b);
    }

    /// <summary>
    /// The grown orchards that screen sight (see <see cref="VisionSystem"/>): their footprints and the top level of
    /// their trees, in building order.
    /// </summary>
    public void Screens(System.Collections.Generic.List<(int X0, int Z0, int X1, int Z1, int Top)> into)
    {
        into.Clear();
        foreach (var b in Buildings.All)
            if (b.Def.Kind == BuildingKind.Orchard && b.Active && b.Grown) into.Add((b.X, b.Z, b.MaxX, b.MaxZ, b.Y + 3));
    }
}
