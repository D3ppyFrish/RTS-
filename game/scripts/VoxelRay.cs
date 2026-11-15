using Godot;
using Rts.Sim;

namespace Rts.Game;

/// <summary>Voxel ray marching for mouse picking.</summary>
public static class VoxelRay
{
    /// <summary>
    /// Marches a ray (in block coordinates) through the world and returns the first solid block it hits,
    /// plus the empty cell just before it. Blocks at or above <paramref name="hiddenFrom"/> are treated as
    /// air, so clicks go through terrain the height slice has cut away.
    /// </summary>
    public static bool Cast(VoxelWorld world, Vector3 origin, Vector3 dir, int hiddenFrom, out Vector3I hit, out Vector3I before)
    {
        var pos = new Vector3I(Mathf.FloorToInt(origin.X), Mathf.FloorToInt(origin.Y), Mathf.FloorToInt(origin.Z));
        before = pos;
        var step = new Vector3I(dir.X > 0 ? 1 : -1, dir.Y > 0 ? 1 : -1, dir.Z > 0 ? 1 : -1);
        var tDelta = new Vector3(Mathf.Abs(1 / dir.X), Mathf.Abs(1 / dir.Y), Mathf.Abs(1 / dir.Z));
        var tMax = new Vector3(
            (dir.X > 0 ? pos.X + 1 - origin.X : origin.X - pos.X) * tDelta.X,
            (dir.Y > 0 ? pos.Y + 1 - origin.Y : origin.Y - pos.Y) * tDelta.Y,
            (dir.Z > 0 ? pos.Z + 1 - origin.Z : origin.Z - pos.Z) * tDelta.Z);
        for (int i = 0; i < 2000; i++)
        {
            if (pos.Y < hiddenFrom && BlockInfo.IsSolid(world.Get(pos.X, pos.Y, pos.Z)))
            {
                hit = pos;
                return true;
            }
            before = pos;
            if (tMax.X < tMax.Y && tMax.X < tMax.Z) { pos.X += step.X; tMax.X += tDelta.X; }
            else if (tMax.Y < tMax.Z) { pos.Y += step.Y; tMax.Y += tDelta.Y; }
            else { pos.Z += step.Z; tMax.Z += tDelta.Z; }
            if (pos.Y < 0 && dir.Y < 0) break;
        }
        hit = default;
        return false;
    }

    /// <summary>Casts the ray under a screen position. See <see cref="Cast"/>.</summary>
    public static bool FromScreen(Camera3D camera, Vector2 screen, VoxelWorld world, int hiddenFrom,
        out Vector3I hit, out Vector3I before)
    {
        var from = camera.ProjectRayOrigin(screen) / ChunkMesher.BlockSize;
        var dir = camera.ProjectRayNormal(screen);
        return Cast(world, from, dir, hiddenFrom, out hit, out before);
    }
}
