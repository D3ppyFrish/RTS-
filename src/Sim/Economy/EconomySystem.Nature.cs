using System;
using System.Collections.Generic;
using Rts.Sim.World;

namespace Rts.Sim.Economy;

/// <summary>
/// Mana from nature (the Fey; see "The Fey" in docs/rules.md): a shrine with <see cref="BuildingDef.Nature"/> (the
/// fairy ring) needs no keeper and no crystal. Every <see cref="BuildingDef.ChannelTicks"/> it gives its owner
/// <see cref="BuildingDef.Mana"/> for each living tree standing within <see cref="NatureDef.Reach"/> columns of its
/// footprint, up to <see cref="NatureDef.Trees"/>. So the woods round a ring are its mana: felled, burned or never
/// there, it channels less or nothing. Its owner's automatic workers and computer players leave those trees standing
/// (<see cref="Spared"/>).
/// </summary>
public sealed partial class EconomySystem
{
    /// <summary>
    /// A fairy ring channels from the trees round it: counts down to the next round (<see cref="Building.SmeltTicks"/>,
    /// at its <see cref="BuildingDef.Speed"/>), then gives its owner its mana for the trees it counts then.
    /// </summary>
    private void StepNature(Building b)
    {
        if (b.SmeltTicks > 0 && (b.SmeltTicks = Math.Max(0, b.SmeltTicks - Steps(b))) == 0)
        {
            int mana = b.Def.Mana * NatureTrees(b);
            var p = Player(b.Owner);
            p.Mana += mana;
            p.ManaChannelled += mana;
        }
        if (b.SmeltTicks == 0) b.SmeltTicks = b.Def.ChannelTicks;
    }

    /// <summary>
    /// The living trees a fairy ring channels from: those whose trunk stands within its reach of its footprint, up to
    /// its most. A big tree counts for as many as its trunk is columns thick (one of the map's trees, two columns
    /// square, for four), so the measure is the wood standing round it. 0 for any other building.
    /// </summary>
    public int NatureTrees(Building b) => TreesAround(b.Def, b.X, b.Z, b.Owner);

    /// <summary>The most trees the player's fairy ring of this kind counts: its own most, and more with Wild woods.</summary>
    public int MostTrees(BuildingDef def, int player) =>
        def.Nature is { } nature ? nature.Trees * (100 + (Find(player)?.WildWoodsPercent ?? 0)) / 100 : 0;

    /// <summary>
    /// The living trees a fairy ring of this kind would channel from with the corner of its footprint at (bx, bz): those
    /// whose trunk stands within its reach of the footprint, up to the most the player's rings count (see <see cref="MostTrees"/>). 0 for a building that is not one.
    /// </summary>
    public int TreesAround(BuildingDef def, int bx, int bz, int player)
    {
        if (def.Nature is not { } nature) return 0;
        var w = World;
        int s = Math.Clamp(w.TreeScale, 1, Trees.MaxScale);
        int most = MostTrees(def, player), columns = 0;
        int x0 = Math.Max(0, bx - nature.Reach), x1 = Math.Min(w.SizeX - 1, bx + def.SizeX - 1 + nature.Reach);
        int z0 = Math.Max(0, bz - nature.Reach), z1 = Math.Min(w.SizeZ - 1, bz + def.SizeZ - 1 + nature.Reach);
        for (int z = z0; z <= z1; z++)
        for (int x = x0; x <= x1; x++)
        {
            if (!TrunkBase(w, x, z, 16 * s)) continue;
            if (++columns >= most) return most;
        }
        return columns;
    }

    /// <summary>The column has the foot of a tree trunk in it, within <paramref name="depth"/> levels of the surface (a tree's height).</summary>
    private static bool TrunkBase(VoxelWorld w, int x, int z, int depth)
    {
        int top = w.SurfaceHeight(x, z);
        for (int y = top; y >= Math.Max(1, top - depth); y--)
            if (BlockInfo.IsTrunk(w.Get(x, y, z)) && !BlockInfo.IsTrunk(w.Get(x, y - 1, z))) return true;
        return false;
    }

    /// <summary>The player's fairy rings (finished, being built or planned), in id order: none for most players.</summary>
    public List<Building> NatureRings(int player)
    {
        var rings = new List<Building>();
        foreach (var b in Buildings.All)
            if (b.Owner == player && b.Def.Nature != null) rings.Add(b);
        return rings;
    }

    /// <summary>
    /// True if a tree in column (x, z) is one of the player's fairy rings' (see <see cref="NatureRings"/>): their
    /// automatic workers and computer players do not fell it.
    /// </summary>
    public static bool Spared(List<Building> rings, int x, int z)
    {
        foreach (var b in rings)
        {
            int reach = b.Def.Nature!.Reach;
            if (x >= b.X - reach && x < b.X + b.Def.SizeX + reach && z >= b.Z - reach && z < b.Z + b.Def.SizeZ + reach) return true;
        }
        return false;
    }
}
