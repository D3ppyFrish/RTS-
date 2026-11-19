using System;
using System.Collections.Generic;
using Godot;
using Rts.Sim;
using Rts.Sim.Meshing;

namespace Rts.Game;

/// <summary>
/// How finely a chunk is drawn, by how far it is from the camera (see <see cref="ChunkRenderer"/>): the
/// nearest ground in half-size blocks, then as the rules have it, then with its trees and buildings in
/// voxels four times the size, and the horizon in plain whole blocks.
/// </summary>
public enum ChunkLod : byte
{
    /// <summary>Ground in half-size blocks (see <see cref="Rts.Sim.World.FineTerrain"/>); trees and buildings in full detail.</summary>
    Fine,
    /// <summary>Ground in whole blocks; trees and buildings in full detail.</summary>
    Near,
    /// <summary>Ground in whole blocks; trees and buildings in voxels four times the size (two to a block).</summary>
    Far,
    /// <summary>Everything in plain whole blocks, with no shading in the corners.</summary>
    Horizon,
}

/// <summary>Vertex arrays for one chunk, built off the main thread and uploaded later.</summary>
public sealed class ChunkMeshData
{
    public SurfaceData? Opaque;
    public SurfaceData? Water;
    public bool IsEmpty => Opaque == null && Water == null;
    public int Triangles => ((Opaque?.Indices.Length ?? 0) + (Water?.Indices.Length ?? 0)) / 3;
    /// <summary>Bytes of vertex and index data.</summary>
    public long Bytes => (Opaque?.Bytes ?? 0) + (Water?.Bytes ?? 0);
}

public sealed class SurfaceData
{
    public required Vector3[] Vertices;
    public required Vector3[] Normals;
    public required Color[] Colors;
    public required int[] Indices;
    /// <summary>
    /// Only for ground in half-size blocks (<see cref="ChunkLod.Fine"/>): x is 1 at the vertices of half-size
    /// blocks and 0 at the rest, which tells the terrain shader the size of the blocks it draws.
    /// </summary>
    public Vector2[]? Uvs;
    public long Bytes => Vertices.Length * (Uvs != null ? 48L : 40L) + Indices.Length * 4L;
}

/// <summary>
/// Turns greedy-meshed quads from <see cref="GreedyMesher"/> into Godot vertex arrays.
/// <see cref="BuildData"/> is thread-safe and touches no Godot objects; <see cref="ToMesh"/> must run
/// on the main thread. Vertices are chunk-local; the chunk node carries the offset.
/// </summary>
public static class ChunkMesher
{
    public const float BlockSize = 0.5f;

    /// <summary>Blocks a side of a chunk of half-size blocks: the same piece of the world as a chunk of whole blocks.</summary>
    public const int HalfChunk = VoxelWorld.ChunkSize * 2;

    /// <summary>Length of the padded copy a chunk of half-size blocks is meshed from (see <see cref="VoxelWorld.CopyBoxPadded"/>).</summary>
    public const int HalfPaddedLength = (HalfChunk + 2) * (HalfChunk + 2) * (HalfChunk + 2);

    private static readonly Vector3[] FaceNormals =
    {
        new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1),
    };

    /// <summary>How much light each face direction gets (quad face numbering), baked into the vertex colours.</summary>
    public static readonly float[] Shade = { 0.8f, 0.8f, 1.0f, 0.5f, 0.65f, 0.65f };

    /// <summary>
    /// Opaque surfaces tell the terrain shader (<see cref="TerrainMaterials"/>) what they are in their vertex
    /// colour's alpha: 1 for plain blocks, <see cref="FineMark"/> for the fine voxels of buildings (no block
    /// grid drawn on them), <see cref="GlowMark"/> for blocks that glow (crystal), <see cref="LavaMark"/> for lava and
    /// <see cref="LeafMark"/> for leaves, which sway in the wind, and <see cref="FineLeafMark"/> for the fine
    /// voxels of leaves (they sway too, with no block grid drawn on them).
    /// </summary>
    public const float FineMark = 0.875f, GlowMark = 0.625f, LavaMark = 0.5625f, LeafMark = 0.375f, FineLeafMark = 0.3125f,
        WindowMark = 0.6875f;

    /// <summary>
    /// How much light a corner gets by how open it is (see <see cref="Occlusion"/>): faces darken into the
    /// corners where blocks meet, so leaf clumps, steps and walls stand apart from each other.
    /// </summary>
    public static readonly float[] AoLight = { 0.48f, 0.66f, 0.83f, 1f };

    /// <summary>
    /// The two triangles of a quad, split along the diagonal that keeps a dark corner from spreading over
    /// the whole quad.
    /// </summary>
    public static void QuadIndices(int[] indices, int ii, int b, int a0, int a1, int a2, int a3)
    {
        if (a0 + a2 < a1 + a3)
        {
            indices[ii] = b; indices[ii + 1] = b + 3; indices[ii + 2] = b + 1;
            indices[ii + 3] = b + 1; indices[ii + 4] = b + 3; indices[ii + 5] = b + 2;
        }
        else
        {
            indices[ii] = b; indices[ii + 1] = b + 2; indices[ii + 2] = b + 1;
            indices[ii + 3] = b; indices[ii + 4] = b + 3; indices[ii + 5] = b + 2;
        }
    }

    /// <summary>Size of the fine voxels buildings are drawn with: the size units are built from.</summary>
    public const float FineSize = BlockSize / StructureDetail.Size;

    [ThreadStatic] private static List<Quad>? _opaque;
    [ThreadStatic] private static List<Quad>? _water;
    [ThreadStatic] private static List<FineQuad>? _fine;
    [ThreadStatic] private static BlockType[]? _stripped;

    /// <summary>
    /// Meshes chunk (cx, cy, cz) from its padded copy: terrain as whole blocks, building blocks at character
    /// scale (see <see cref="StructureMesher"/>), as simply as <paramref name="lod"/> says. For
    /// <see cref="ChunkLod.Fine"/>, <paramref name="half"/> is the padded copy of the same chunk in half-size
    /// blocks (changed here), which the ground is meshed from; the whole blocks then only give the buildings and trees.
    /// </summary>
    public static ChunkMeshData BuildData(BlockType[] padded, int cx = 0, int cy = 0, int cz = 0,
        ChunkLod lod = ChunkLod.Near, BlockType[]? half = null)
    {
        var opaque = _opaque ??= new List<Quad>(1024);
        var water = _water ??= new List<Quad>(256);
        var fine = _fine ??= new List<FineQuad>(1024);
        opaque.Clear();
        water.Clear();
        fine.Clear();
        if (lod == ChunkLod.Horizon)
        {
            GreedyMesher.Build(padded, opaque, water, ao: false);
            return new ChunkMeshData { Opaque = ToArrays(opaque, null, 1f, BlockSize, horizon: true), Water = ToArrays(water, null, 0.7f, BlockSize) };
        }
        bool structures = StructureMesher.HasStructures(padded);
        if (structures) StructureMesher.Build(padded, cx, cy, cz, fine, ao: true, step: lod == ChunkLod.Far ? 4 : 1);
        if (lod == ChunkLod.Fine && half != null)
        {
            for (int i = 0; i < HalfPaddedLength; i++)
                if (StructureDetail.IsDetailed(half[i])) half[i] = BlockType.Air;
            GreedyMesher.Build(half, HalfChunk, opaque, water, ao: true);
            return new ChunkMeshData
            {
                Opaque = ToArrays(opaque, fine, 1f, BlockSize / 2, half: true),
                Water = ToArrays(water, null, 0.7f, BlockSize / 2),
            };
        }
        if (structures)
        {
            var stripped = _stripped ??= new BlockType[padded.Length];
            StructureMesher.StripStructures(padded, stripped);
            GreedyMesher.Build(stripped, opaque, water, ao: true);
        }
        else
        {
            GreedyMesher.Build(padded, opaque, water, ao: true);
        }
        return new ChunkMeshData { Opaque = ToArrays(opaque, fine, 1f, BlockSize), Water = ToArrays(water, null, 0.7f, BlockSize) };
    }

    /// <summary>The mark (see <see cref="FineMark"/>) a whole block of this kind is drawn with.</summary>
    public static float MarkOf(BlockType block) => block switch
    {
        BlockType.Crystal => GlowMark,
        BlockType.Lava => LavaMark,
        _ when BlockInfo.IsLeaves(block) && block != BlockType.MushroomCap => LeafMark,
        _ => 1f,
    };

    /// <summary>The mark (see <see cref="FineMark"/>) a fine voxel of this colour is drawn with.</summary>
    public static float FineMarkOf(byte colour) =>
        StructurePalette.IsGlow(colour) ? GlowMark : StructurePalette.IsLeaf(colour) ? FineLeafMark
        : colour == StructurePalette.Glass ? WindowMark : FineMark;

    private static SurfaceData? ToArrays(List<Quad> quads, List<FineQuad>? fine, float alpha, float blockSize, bool horizon = false, bool half = false)
    {
        int fineCount = fine?.Count ?? 0;
        int total = quads.Count + fineCount;
        if (total == 0) return null;
        var verts = new Vector3[total * 4];
        var normals = new Vector3[verts.Length];
        var colors = new Color[verts.Length];
        var indices = new int[total * 6];
        var uvs = half ? new Vector2[verts.Length] : null;
        for (int f = 0; f < fineCount; f++)
        {
            var quad = fine![f];
            int q = quads.Count + f;
            var c = PaletteColor(quad.Colour) * Shade[quad.Face];
            c.A = alpha < 1 ? alpha : FineMarkOf(quad.Colour);
            for (int k = 0; k < 4; k++)
            {
                var (x, y, z) = quad.Corner(k);
                int vi = q * 4 + k;
                verts[vi] = new Vector3(x, y, z) * FineSize;
                normals[vi] = FaceNormals[quad.Face];
                colors[vi] = Lit(c, quad.AoAt(k));
            }
            QuadIndices(indices, q * 6, q * 4, quad.AoAt(0), quad.AoAt(1), quad.AoAt(2), quad.AoAt(3));
        }
        for (int q = 0; q < quads.Count; q++)
        {
            var quad = quads[q];
            var c = (horizon ? HorizonColor(quad.Block, quad.Face) : ColorOf(quad.Block)) * Shade[quad.Face];
            c.A = alpha < 1 ? alpha : MarkOf(quad.Block);
            for (int k = 0; k < 4; k++)
            {
                var (x, y, z) = quad.Corner(k);
                int vi = q * 4 + k;
                verts[vi] = new Vector3(x, y, z) * blockSize;
                if (uvs != null) uvs[vi] = new Vector2(1f, 0f);
                normals[vi] = FaceNormals[quad.Face];
                colors[vi] = Lit(c, quad.AoAt(k));
            }
            // Corners are counter-clockwise from outside; Godot treats clockwise as front-facing.
            QuadIndices(indices, q * 6, q * 4, quad.AoAt(0), quad.AoAt(1), quad.AoAt(2), quad.AoAt(3));
        }
        return new SurfaceData { Vertices = verts, Normals = normals, Colors = colors, Indices = indices, Uvs = uvs };
    }

    /// <summary>Colour <paramref name="c"/> at a corner of openness <paramref name="ao"/>, its mark kept.</summary>
    private static Color Lit(Color c, int ao)
    {
        float f = AoLight[ao];
        return new Color(c.R * f, c.G * f, c.B * f, c.A);
    }

    public static ArrayMesh? ToMesh(ChunkMeshData data, Material opaque, Material water)
    {
        if (data.IsEmpty) return null;
        var mesh = new ArrayMesh();
        if (data.Opaque != null) AddSurface(mesh, data.Opaque, opaque);
        if (data.Water != null) AddSurface(mesh, data.Water, water);
        return mesh;
    }

    private static void AddSurface(ArrayMesh mesh, SurfaceData s, Material material)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = s.Vertices;
        arrays[(int)Mesh.ArrayType.Normal] = s.Normals;
        arrays[(int)Mesh.ArrayType.Color] = s.Colors;
        if (s.Uvs != null) arrays[(int)Mesh.ArrayType.TexUV] = s.Uvs;
        arrays[(int)Mesh.ArrayType.Index] = s.Indices;
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);
    }

    /// <summary>The colour of a building's fine voxel (see <see cref="StructurePalette"/>).</summary>
    public static Color PaletteColor(byte c) => c switch
    {
        StructurePalette.Timber => new Color(0.36f, 0.23f, 0.13f),
        StructurePalette.TimberDark => new Color(0.27f, 0.17f, 0.1f),
        StructurePalette.PlankA => new Color(0.68f, 0.5f, 0.3f),
        StructurePalette.PlankB => new Color(0.6f, 0.43f, 0.25f),
        StructurePalette.PlankC => new Color(0.75f, 0.57f, 0.35f),
        StructurePalette.ThatchA => new Color(0.8f, 0.68f, 0.37f),
        StructurePalette.ThatchB => new Color(0.71f, 0.59f, 0.3f),
        StructurePalette.ThatchC => new Color(0.87f, 0.76f, 0.46f),
        StructurePalette.TileA => new Color(0.62f, 0.28f, 0.2f),
        StructurePalette.TileB => new Color(0.46f, 0.2f, 0.15f),
        StructurePalette.TileC => new Color(0.7f, 0.35f, 0.24f),
        StructurePalette.StoneA => new Color(0.56f, 0.55f, 0.52f),
        StructurePalette.StoneB => new Color(0.48f, 0.47f, 0.45f),
        StructurePalette.StoneC => new Color(0.63f, 0.61f, 0.57f),
        StructurePalette.Mortar => new Color(0.36f, 0.35f, 0.33f),
        StructurePalette.Glass => new Color(0.14f, 0.2f, 0.28f),
        StructurePalette.Shutter => new Color(0.27f, 0.4f, 0.34f),
        StructurePalette.Iron => new Color(0.22f, 0.22f, 0.24f),
        StructurePalette.IronStud => new Color(0.5f, 0.5f, 0.53f),
        StructurePalette.DoorA => new Color(0.42f, 0.27f, 0.14f),
        StructurePalette.DoorB => new Color(0.35f, 0.22f, 0.11f),
        StructurePalette.Soot => new Color(0.08f, 0.07f, 0.07f),
        StructurePalette.SlateA => new Color(0.42f, 0.43f, 0.46f),
        StructurePalette.SlateB => new Color(0.35f, 0.36f, 0.39f),
        StructurePalette.EarthA => new Color(0.45f, 0.32f, 0.2f),
        StructurePalette.EarthB => new Color(0.37f, 0.26f, 0.16f),
        StructurePalette.Ore => new Color(0.62f, 0.43f, 0.33f),
        StructurePalette.CoalA => new Color(0.16f, 0.16f, 0.18f),
        StructurePalette.CoalB => new Color(0.26f, 0.25f, 0.27f),
        StructurePalette.BarkA => new Color(0.4f, 0.27f, 0.16f),
        StructurePalette.BarkB => new Color(0.33f, 0.22f, 0.13f),
        StructurePalette.BarkDark => new Color(0.22f, 0.14f, 0.08f),
        StructurePalette.Heartwood => new Color(0.8f, 0.64f, 0.42f),
        StructurePalette.Pith => new Color(0.55f, 0.38f, 0.22f),
        StructurePalette.LeafA => new Color(0.25f, 0.52f, 0.2f),
        StructurePalette.LeafB => new Color(0.19f, 0.43f, 0.17f),
        StructurePalette.LeafC => new Color(0.4f, 0.64f, 0.24f),
        StructurePalette.LeafDark => new Color(0.13f, 0.3f, 0.12f),
        StructurePalette.LeafYellow => new Color(0.55f, 0.62f, 0.2f),
        StructurePalette.PineBarkA => new Color(0.42f, 0.25f, 0.16f),
        StructurePalette.PineBarkB => new Color(0.34f, 0.2f, 0.13f),
        StructurePalette.BirchA => new Color(0.9f, 0.88f, 0.82f),
        StructurePalette.BirchB => new Color(0.8f, 0.77f, 0.68f),
        StructurePalette.BirchMark => new Color(0.16f, 0.15f, 0.14f),
        StructurePalette.StemA => new Color(0.9f, 0.86f, 0.74f),
        StructurePalette.StemB => new Color(0.8f, 0.74f, 0.6f),
        StructurePalette.NeedleA => new Color(0.16f, 0.4f, 0.25f),
        StructurePalette.NeedleB => new Color(0.22f, 0.49f, 0.3f),
        StructurePalette.NeedleDark => new Color(0.1f, 0.26f, 0.18f),
        StructurePalette.BirchLeafA => new Color(0.5f, 0.68f, 0.24f),
        StructurePalette.BirchLeafB => new Color(0.42f, 0.6f, 0.2f),
        StructurePalette.WillowA => new Color(0.42f, 0.56f, 0.28f),
        StructurePalette.WillowB => new Color(0.33f, 0.46f, 0.24f),
        StructurePalette.DuskA => new Color(0.46f, 0.27f, 0.6f),
        StructurePalette.DuskB => new Color(0.58f, 0.36f, 0.7f),
        StructurePalette.DuskDark => new Color(0.27f, 0.15f, 0.38f),
        StructurePalette.DuskGlow => new Color(0.75f, 0.5f, 0.85f),
        StructurePalette.CapA => new Color(0.74f, 0.17f, 0.13f),
        StructurePalette.CapB => new Color(0.62f, 0.13f, 0.11f),
        StructurePalette.CapSpot => new Color(0.94f, 0.9f, 0.82f),
        StructurePalette.Gills => new Color(0.82f, 0.74f, 0.6f),
        StructurePalette.GillsDark => new Color(0.66f, 0.57f, 0.44f),
        StructurePalette.DuskBarkA => new Color(0.37f, 0.36f, 0.42f),
        StructurePalette.DuskBarkB => new Color(0.29f, 0.28f, 0.34f),
        StructurePalette.DuskBarkDark => new Color(0.16f, 0.15f, 0.2f),
        StructurePalette.DuskVein => new Color(0.2f, 0.75f, 0.8f),
        StructurePalette.Bone => new Color(0.9f, 0.88f, 0.82f),
        StructurePalette.BoneDark => new Color(0.12f, 0.1f, 0.12f),
        StructurePalette.Sap => new Color(0.55f, 0.04f, 0.05f),
        StructurePalette.StakeA => new Color(0.6f, 0.43f, 0.25f),
        StructurePalette.StakeB => new Color(0.52f, 0.36f, 0.2f),
        StructurePalette.RampartA => new Color(0.8f, 0.79f, 0.75f),
        StructurePalette.RampartB => new Color(0.7f, 0.69f, 0.66f),
        StructurePalette.RampartC => new Color(0.89f, 0.88f, 0.83f),
        StructurePalette.RampartMortar => new Color(0.5f, 0.49f, 0.46f),
        _ => new Color(1, 0, 1),
    };

    /// <summary>
    /// The colour of a block's face on the horizon (<see cref="ChunkLod.Horizon"/>), where trees are plain
    /// blocks: a crown gets the colours its leaves have on average up close (lighter on top), so a forest does
    /// not darken where it is drawn simply.
    /// </summary>
    public static Color HorizonColor(BlockType t, int face) => t switch
    {
        BlockType.Leaves => face == 2 ? new Color(0.36f, 0.57f, 0.21f) : new Color(0.27f, 0.52f, 0.2f),
        BlockType.PineNeedles => face == 2 ? new Color(0.2f, 0.46f, 0.28f) : new Color(0.17f, 0.41f, 0.25f),
        BlockType.BirchLeaves => face == 2 ? new Color(0.51f, 0.66f, 0.22f) : new Color(0.49f, 0.66f, 0.23f),
        BlockType.WillowLeaves => face == 2 ? new Color(0.44f, 0.59f, 0.27f) : new Color(0.4f, 0.54f, 0.27f),
        BlockType.DuskLeaves => face == 2 ? new Color(0.55f, 0.34f, 0.68f) : new Color(0.48f, 0.29f, 0.62f),
        _ => ColorOf(t),
    };

    public static Color ColorOf(BlockType t) => t switch
    {
        BlockType.Grass => new Color(0.35f, 0.53f, 0.25f),
        BlockType.Soil => new Color(0.47f, 0.33f, 0.2f),
        BlockType.Sand => new Color(0.86f, 0.8f, 0.55f),
        BlockType.Stone => new Color(0.5f, 0.5f, 0.52f),
        BlockType.IronOre => new Color(0.65f, 0.45f, 0.35f),
        BlockType.Coal => new Color(0.15f, 0.15f, 0.17f),
        BlockType.Crystal => new Color(0.55f, 0.35f, 0.9f),
        BlockType.Wood => new Color(0.45f, 0.3f, 0.15f),
        BlockType.Water => new Color(0.2f, 0.4f, 0.8f),
        BlockType.Bedrock => new Color(0.1f, 0.1f, 0.1f),
        BlockType.Leaves => new Color(0.2f, 0.45f, 0.18f),
        BlockType.Mud => new Color(0.3f, 0.27f, 0.17f),
        BlockType.Bog => new Color(0.42f, 0.27f, 0.45f), // poison bog: a sickly violet sludge, unlike mud or grass
        BlockType.Crust => new Color(0.66f, 0.57f, 0.42f), // a sinkhole's crust: parched, pale earth that stands out from grass
        BlockType.Quicksand => new Color(0.55f, 0.45f, 0.30f), // quicksand: dark, wet, glistening sand that stands out from the beach
        BlockType.BerryBush => new Color(0.75f, 0.15f, 0.3f),
        BlockType.Planks => new Color(0.72f, 0.54f, 0.32f),
        BlockType.Thatch => new Color(0.8f, 0.69f, 0.38f),
        BlockType.RoofTile => new Color(0.64f, 0.29f, 0.21f),
        BlockType.StoneBrick => new Color(0.63f, 0.61f, 0.57f),
        BlockType.Door => new Color(0.32f, 0.2f, 0.11f),
        BlockType.Gate => new Color(0.26f, 0.17f, 0.1f),
        BlockType.Palisade => new Color(0.47f, 0.33f, 0.19f),
        BlockType.Rampart => new Color(0.78f, 0.77f, 0.73f),
        BlockType.Trestle => new Color(0.5f, 0.35f, 0.2f),
        BlockType.Lava => new Color(1f, 0.3f, 0f),
        BlockType.Ice => new Color(0.74f, 0.88f, 0.96f),
        BlockType.Floor => new Color(0.55f, 0.4f, 0.24f),
        BlockType.Farmland => new Color(0.37f, 0.26f, 0.14f),
        BlockType.Road => new Color(0.6f, 0.54f, 0.43f),
        BlockType.Rubble => new Color(0.4f, 0.31f, 0.22f),
        BlockType.StoneRubble => new Color(0.4f, 0.39f, 0.38f),
        BlockType.OreRubble => new Color(0.52f, 0.36f, 0.28f),
        BlockType.CoalRubble => new Color(0.22f, 0.21f, 0.22f),
        BlockType.WoodRubble => new Color(0.5f, 0.37f, 0.22f),
        BlockType.TreeRubble => new Color(0.4f, 0.27f, 0.16f),
        BlockType.PineWood => new Color(0.33f, 0.22f, 0.14f),
        BlockType.BirchWood => new Color(0.86f, 0.84f, 0.78f),
        BlockType.MushroomStem => new Color(0.88f, 0.84f, 0.72f),
        BlockType.PineNeedles => new Color(0.12f, 0.33f, 0.2f),
        BlockType.BirchLeaves => new Color(0.5f, 0.66f, 0.26f),
        BlockType.WillowLeaves => new Color(0.4f, 0.55f, 0.25f),
        BlockType.DuskLeaves => new Color(0.5f, 0.3f, 0.62f),
        BlockType.MushroomCap => new Color(0.72f, 0.2f, 0.16f),
        BlockType.DuskWood or BlockType.DuskBranch => new Color(0.33f, 0.32f, 0.38f),
        _ => new Color(1, 0, 1),
    };
}
