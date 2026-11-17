using System;
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
    public static bool Cast(VoxelWorld world, Vector3 origin, Vector3 dir, int hiddenFrom, out Vector3I hit, out Vector3I before) =>
        March(origin, dir, 2000, out hit, out before,
            p => p.Y < hiddenFrom && BlockInfo.IsSolid(world.Get(p.X, p.Y, p.Z)));

    /// <summary>
    /// The same for a world drawn in half-size blocks (<paramref name="fine"/>, see
    /// <see cref="ChunkRenderer.FineOf"/>): the ray is marched through those, so a click lands on what is
    /// drawn, a lowered step and all. Where <paramref name="drawnFine"/> says a whole block is not drawn in
    /// half-size blocks (it is far from the camera) the ray meets the whole block. The block hit and the cell
    /// before it are still given in whole blocks; if the ray came to the block through the cut-away top half
    /// of a lowered step, the cell before is the empty one over that step.
    /// </summary>
    public static bool Cast(VoxelWorld world, VoxelWorld fine, Func<int, int, int, bool> drawnFine, Vector3 origin, Vector3 dir,
        int hiddenFrom, out Vector3I hit, out Vector3I before)
    {
        bool Solid(Vector3I p)
        {
            int x = p.X >> 1, y = p.Y >> 1, z = p.Z >> 1;
            if (y >= hiddenFrom) return false;
            if (BlockInfo.IsSolid(fine.Get(p.X, p.Y, p.Z))) return true;
            return BlockInfo.IsSolid(world.Get(x, y, z)) && !drawnFine(x, y, z);
        }
        if (!March(origin * 2, dir, 4000, out var fineHit, out var fineBefore, Solid))
        {
            hit = before = default;
            return false;
        }
        hit = new Vector3I(fineHit.X >> 1, fineHit.Y >> 1, fineHit.Z >> 1);
        before = new Vector3I(fineBefore.X >> 1, fineBefore.Y >> 1, fineBefore.Z >> 1);
        if (before.Y < hiddenFrom && BlockInfo.IsSolid(world.Get(before.X, before.Y, before.Z))) before.Y++;
        return true;
    }

    private static bool March(Vector3 origin, Vector3 dir, int steps, out Vector3I hit, out Vector3I before, Func<Vector3I, bool> solid)
    {
        var pos = new Vector3I(Mathf.FloorToInt(origin.X), Mathf.FloorToInt(origin.Y), Mathf.FloorToInt(origin.Z));
        before = pos;
        var step = new Vector3I(dir.X > 0 ? 1 : -1, dir.Y > 0 ? 1 : -1, dir.Z > 0 ? 1 : -1);
        var tDelta = new Vector3(Mathf.Abs(1 / dir.X), Mathf.Abs(1 / dir.Y), Mathf.Abs(1 / dir.Z));
        var tMax = new Vector3(
            (dir.X > 0 ? pos.X + 1 - origin.X : origin.X - pos.X) * tDelta.X,
            (dir.Y > 0 ? pos.Y + 1 - origin.Y : origin.Y - pos.Y) * tDelta.Y,
            (dir.Z > 0 ? pos.Z + 1 - origin.Z : origin.Z - pos.Z) * tDelta.Z);
        for (int i = 0; i < steps; i++)
        {
            if (solid(pos))
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

    /// <summary>
    /// Casts the ray under a screen position. See <see cref="Cast(VoxelWorld, Vector3, Vector3, int, out Vector3I, out Vector3I)"/>.
    /// If the world is drawn in half-size blocks the ray is marched through those where they are drawn.
    /// </summary>
    public static bool FromScreen(Camera3D camera, Vector2 screen, VoxelWorld world, int hiddenFrom,
        out Vector3I hit, out Vector3I before)
    {
        var from = camera.ProjectRayOrigin(screen) / ChunkMesher.BlockSize;
        var dir = camera.ProjectRayNormal(screen);
        if (ChunkRenderer.FineOf(world) is { } fine)
            return Cast(world, fine, ChunkRenderer.ShownFine, from, dir, hiddenFrom, out hit, out before);
        return Cast(world, from, dir, hiddenFrom, out hit, out before);
    }
}
