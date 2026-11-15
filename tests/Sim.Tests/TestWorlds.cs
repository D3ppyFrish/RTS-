using Rts.Sim.World;

namespace Rts.Sim.Tests;

/// <summary>Small hand-built worlds for unit and pathfinding tests.</summary>
public static class TestWorlds
{
    /// <summary>Flat stone with a grass top; units stand at level <paramref name="ground"/> + 1.</summary>
    public static VoxelWorld Flat(int sizeX = 64, int sizeY = 16, int sizeZ = 64, int ground = 3)
    {
        var w = new VoxelWorld(sizeX, sizeY, sizeZ);
        for (int x = 0; x < sizeX; x++)
        for (int z = 0; z < sizeZ; z++)
        {
            w.Set(x, 0, z, BlockType.Bedrock);
            for (int y = 1; y < ground; y++) w.Set(x, y, z, BlockType.Stone);
            w.Set(x, ground, z, BlockType.Grass);
        }
        return w;
    }

    public static Simulation Sim(VoxelWorld w, ulong seed = 1) => new(new RegionMap(seed, w));

    /// <summary>A tree like the generator's, standing on ground at level <paramref name="ground"/>: a trunk and a rounded crown of leaves.</summary>
    public static void Tree(VoxelWorld w, int x, int z, int trunk, int ground = 3, BlockType wood = BlockType.Wood, BlockType leaves = BlockType.Leaves)
    {
        for (int y = 1; y <= trunk; y++) w.Set(x, ground + y, z, wood);
        int top = ground + trunk;
        for (int dy = -1; dy <= 1; dy++)
        {
            int r = dy == 1 ? 1 : 2;
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (System.Math.Abs(dx) == r && System.Math.Abs(dz) == r && r == 2) continue;
                if (w.Get(x + dx, top + dy, z + dz) == BlockType.Air) w.Set(x + dx, top + dy, z + dz, leaves);
            }
        }
        w.Set(x, top + 2, z, leaves);
    }

    /// <summary>Fills a box of blocks (inclusive bounds).</summary>
    public static void Box(VoxelWorld w, int x0, int y0, int z0, int x1, int y1, int z1, BlockType b)
    {
        for (int x = x0; x <= x1; x++)
        for (int y = y0; y <= y1; y++)
        for (int z = z0; z <= z1; z++)
            w.Set(x, y, z, b);
    }
}
