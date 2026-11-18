using System;
using System.Collections.Generic;
using Godot;
using Rts.Sim.Meshing;

namespace Rts.Game.Detail;

/// <summary>
/// What a fine voxel is drawn as: a colour (alpha 0 means "team colour", with red holding the shade, as in
/// <see cref="UnitModels"/>) and a surface (<see cref="Surface"/>) that the shader adds a pattern to.
/// </summary>
public readonly record struct FineMaterial(Color Colour, Surface Surface = Surface.Plain);

/// <summary>Surface patterns the detail shader draws on a face, so the geometry only has to give the shape.</summary>
public enum Surface
{
    Plain = 0,
    /// <summary>Staggered stone blocks with mortar between them.</summary>
    Brick = 1,
    /// <summary>Planks with grain, running along the longer side.</summary>
    Planks = 2,
    /// <summary>Overlapping roof shingles in rows.</summary>
    Shingles = 3,
    /// <summary>Gives off light: fire, crystals, lit windows.</summary>
    Glow = 4,
    /// <summary>Polished metal: helmets, blades, studs.</summary>
    Metal = 5,
}

/// <summary>
/// A box of fine voxels being built up for one model, with its own palette. Coordinates can be negative
/// (a unit model is centred on x and z); the box grows to fit what is put in it, up to the bounds given.
/// Cell 0 is empty; every other value indexes <see cref="Palette"/>.
/// </summary>
public sealed class FineGrid
{
    public readonly int X0, Y0, Z0, NX, NY, NZ;
    public readonly byte[] Cells;
    public readonly List<FineMaterial> Palette = new() { default };
    private readonly Dictionary<FineMaterial, byte> _index = new();

    public FineGrid(int x0, int y0, int z0, int x1, int y1, int z1)
    {
        X0 = x0; Y0 = y0; Z0 = z0;
        NX = x1 - x0 + 1; NY = y1 - y0 + 1; NZ = z1 - z0 + 1;
        Cells = new byte[NX * NY * NZ];
        _index[default] = 0; // the empty material clears
    }

    /// <summary>The palette entry for a material, added the first time it is used.</summary>
    public byte Of(FineMaterial m)
    {
        if (_index.TryGetValue(m, out var b)) return b;
        if (Palette.Count >= 250) throw new InvalidOperationException("Too many materials in one fine model");
        b = (byte)Palette.Count;
        Palette.Add(m);
        _index[m] = b;
        return b;
    }

    public bool Inside(int x, int y, int z) =>
        x >= X0 && y >= Y0 && z >= Z0 && x < X0 + NX && y < Y0 + NY && z < Z0 + NZ;

    private int Index(int x, int y, int z) => ((y - Y0) * NZ + (z - Z0)) * NX + (x - X0);

    public byte Get(int x, int y, int z) => Inside(x, y, z) ? Cells[Index(x, y, z)] : (byte)0;

    public void Set(int x, int y, int z, byte c)
    {
        if (Inside(x, y, z)) Cells[Index(x, y, z)] = c;
    }

    public void Set(int x, int y, int z, FineMaterial m) => Set(x, y, z, Of(m));

    /// <summary>Fills the box [x0, x1] x [y0, y1] x [z0, z1] (inclusive, any order).</summary>
    public FineGrid Box(int x0, int y0, int z0, int x1, int y1, int z1, FineMaterial m)
    {
        byte c = Of(m);
        Fill(x0, y0, z0, x1, y1, z1, c);
        return this;
    }

    public FineGrid Clear(int x0, int y0, int z0, int x1, int y1, int z1)
    {
        Fill(x0, y0, z0, x1, y1, z1, 0);
        return this;
    }

    private void Fill(int x0, int y0, int z0, int x1, int y1, int z1, byte c)
    {
        if (x0 > x1) (x0, x1) = (x1, x0);
        if (y0 > y1) (y0, y1) = (y1, y0);
        if (z0 > z1) (z0, z1) = (z1, z0);
        x0 = Math.Max(x0, X0); y0 = Math.Max(y0, Y0); z0 = Math.Max(z0, Z0);
        x1 = Math.Min(x1, X0 + NX - 1); y1 = Math.Min(y1, Y0 + NY - 1); z1 = Math.Min(z1, Z0 + NZ - 1);
        for (int y = y0; y <= y1; y++)
        for (int z = z0; z <= z1; z++)
        {
            if (x1 < x0) continue;
            Cells.AsSpan(Index(x0, y, z), x1 - x0 + 1).Fill(c);
        }
    }

    /// <summary>An upright cylinder of radius <paramref name="r"/> (voxels) round the column centre (cx, cz).</summary>
    public FineGrid Cylinder(float cx, float cz, float r, int y0, int y1, FineMaterial m)
    {
        byte c = Of(m);
        for (int z = (int)MathF.Floor(cz - r); z <= (int)MathF.Ceiling(cz + r); z++)
        for (int x = (int)MathF.Floor(cx - r); x <= (int)MathF.Ceiling(cx + r); x++)
        {
            float dx = x + 0.5f - cx, dz = z + 0.5f - cz;
            if (dx * dx + dz * dz > r * r) continue;
            for (int y = y0; y <= y1; y++) Set(x, y, z, c);
        }
        return this;
    }

    /// <summary>
    /// A cone (pointed roof) from radius <paramref name="r"/> at <paramref name="y0"/> to a point at
    /// <paramref name="y1"/>; <paramref name="square"/> makes it a pyramid.
    /// </summary>
    public FineGrid Cone(float cx, float cz, float r, int y0, int y1, FineMaterial m, bool square = false)
    {
        byte c = Of(m);
        for (int y = y0; y <= y1; y++)
        {
            float rr = r * (1f - (y - y0) / (float)(y1 - y0 + 1));
            for (int z = (int)MathF.Floor(cz - rr); z <= (int)MathF.Ceiling(cz + rr); z++)
            for (int x = (int)MathF.Floor(cx - rr); x <= (int)MathF.Ceiling(cx + rr); x++)
            {
                float dx = MathF.Abs(x + 0.5f - cx), dz = MathF.Abs(z + 0.5f - cz);
                bool inside = square ? dx <= rr && dz <= rr : dx * dx + dz * dz <= rr * rr;
                if (inside) Set(x, y, z, c);
            }
        }
        return this;
    }

    /// <summary>Number of filled voxels.</summary>
    public int Count()
    {
        int n = 0;
        foreach (var c in Cells) if (c != 0) n++;
        return n;
    }
}

/// <summary>
/// Turns a <see cref="FineGrid"/> into a mesh: visible faces merged into rectangles of one material with the
/// same greedy mesher the buildings use (<see cref="StructureMesher.Mesh"/>). The surface pattern goes in
/// UV.x for <see cref="DetailShader"/>, which also gives each voxel a little shade of its own from its
/// position, so merged rectangles still read as voxels.
/// </summary>
public static class FineModel
{
    private static readonly Vector3[] Normals =
    {
        Vector3.Right, Vector3.Left, Vector3.Up, Vector3.Down, Vector3.Back, Vector3.Forward,
    };

    /// <summary>Baked light per face direction (+X, -X, +Y, -Y, +Z, -Z), milder than the old models'.</summary>
    private static readonly float[] Shade = { 0.9f, 0.9f, 1f, 0.6f, 0.82f, 0.78f };

    // Corners are counter-clockwise from outside; Godot treats clockwise as front-facing.
    private static readonly int[] Order = { 0, 2, 1, 0, 3, 2 };

    /// <summary>
    /// Meshes <paramref name="g"/> with voxels <paramref name="voxelSize"/> metres across; voxel (0, 0, 0)'s
    /// corner lands at the origin. Returns the mesh and how many triangles it has. With <paramref name="ao"/>,
    /// faces in nooks (between a leg and the body, under a plate's rim) are shaded darker
    /// (<see cref="StructureMesher.Mesh"/>'s ambient occlusion), which reads as depth in small voxels.
    /// </summary>
    public static (ArrayMesh Mesh, int Triangles) Build(FineGrid g, float voxelSize, bool ao = false)
    {
        // StructureMesher.Mesh draws faces against the grid's edge, so the model needs no padding.
        var quads = new List<FineQuad>();
        StructureMesher.Mesh(g.Cells, g.NX, g.NY, g.NZ, g.X0, g.Y0, g.Z0, quads, ao);
        var verts = new Vector3[quads.Count * 4];
        var normals = new Vector3[quads.Count * 4];
        var colours = new Color[quads.Count * 4];
        var uvs = new Vector2[quads.Count * 4];
        var indices = new int[quads.Count * 6];
        for (int q = 0; q < quads.Count; q++)
        {
            var quad = quads[q];
            var m = g.Palette[quad.Colour];
            float s = Shade[quad.Face] * (ao ? 0.62f + 0.38f * quad.AoAt(0) / 3f : 1f);
            var c = m.Colour.A < 0.5f ? new Color(m.Colour.R * s, 0, 0, 0) : new Color(m.Colour.R * s, m.Colour.G * s, m.Colour.B * s, 1);
            for (int k = 0; k < 4; k++)
            {
                var (x, y, z) = quad.Corner(k);
                verts[q * 4 + k] = new Vector3(x, y, z) * voxelSize;
                normals[q * 4 + k] = Normals[quad.Face];
                colours[q * 4 + k] = c;
                uvs[q * 4 + k] = new Vector2((int)m.Surface, 0);
            }
            for (int k = 0; k < 6; k++) indices[q * 6 + k] = q * 4 + Order[k];
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colours;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        if (quads.Count > 0) mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return (mesh, quads.Count * 2);
    }
}

/// <summary>
/// The shader detailed models are drawn with. Like the unit shader (team colour from the instance's custom
/// data, cut by the height slice), plus: a shade per voxel worked out from its position in the model, and the
/// surface patterns of <see cref="Surface"/> drawn per pixel (stone blocks, planks, shingles), which fade out
/// where they would be smaller than a pixel. Patterns cost no triangles, so a castle wall can be one
/// rectangle and still show every stone.
/// </summary>
public static class DetailShader
{
    public const string Code = @"
shader_type spatial;
render_mode cull_back;
uniform float slice_y = 100000.0;
uniform float voxels_per_m = 32.0;
// Models standing on the ground darken towards their foot (cheap contact shadow; SSAO is off in the
// Compatibility renderer). 0 turns it off.
uniform float foot_shadow_m = 0.0;
// A model drawn on its own (not in a MultiMesh) takes its team colour from here; alpha 0 means use the instance's.
uniform vec4 team_tint = vec4(0.0);
varying vec3 v_team;
varying vec4 v_col;
varying float v_world_y;
varying vec3 v_local;
varying vec3 v_nrm;
varying float v_mat;
vec3 linear_of(vec3 c) { return mix(c, c * (c * (c * 0.305306011 + 0.682171111) + 0.012522878), 0.85); }
float hash(vec3 p) {
    p = fract(p * vec3(0.1031, 0.1030, 0.0973));
    p += dot(p, p.yxz + 33.33);
    return fract((p.x + p.y) * p.z);
}
void vertex() {
    v_team = team_tint.a > 0.0 ? team_tint.rgb : INSTANCE_CUSTOM.rgb;
    v_col = COLOR;
    v_local = VERTEX;
    v_nrm = NORMAL;
    v_mat = UV.x;
    v_world_y = (MODEL_MATRIX * vec4(VERTEX, 1.0)).y;
}
void fragment() {
    if (v_world_y > slice_y) discard;
    vec3 base = v_col.a < 0.5 ? v_team * v_col.r : v_col.rgb;
    vec3 q = v_local * voxels_per_m;
    // The voxel this pixel is on: half a voxel inside the face.
    vec3 cell = floor(q - v_nrm * 0.5);
    float shade = 0.93 + 0.14 * hash(cell);
    int mat = int(v_mat + 0.5);
    // Coordinates across the face, in voxels.
    vec2 uv = abs(v_nrm.x) > 0.5 ? q.zy : (abs(v_nrm.z) > 0.5 ? q.xy : q.xz);
    float px = max(fwidth(uv.x), fwidth(uv.y)); // voxels per pixel
    float detail = 1.0 - smoothstep(0.6, 1.6, px); // patterns fade before they shimmer
    float rough = 0.9;
    float metal = 0.0;
    vec3 emit = vec3(0.0);
    if (mat == 1) {
        float row = floor(uv.y / 4.0);
        float u = uv.x + mod(row, 2.0) * 4.0;
        vec2 f = vec2(mod(u, 8.0), mod(uv.y, 4.0));
        float stone = hash(vec3(floor(u / 8.0), row, floor(q.x * v_nrm.x + q.y * v_nrm.y + q.z * v_nrm.z) + 7.0));
        float w = max(px, 0.45);
        float mortar = max(1.0 - smoothstep(0.0, w, f.x), 1.0 - smoothstep(0.0, w, f.y));
        float bevel = smoothstep(7.0, 8.0, f.x) * 0.08 + smoothstep(3.0, 4.0, f.y) * 0.1;
        float s = mix(0.88 + 0.2 * stone - bevel, 0.7, mortar);
        shade *= mix(1.0, s, detail);
    } else if (mat == 2) {
        float along = abs(v_nrm.y) > 0.5 ? uv.x : uv.y;
        float across = abs(v_nrm.y) > 0.5 ? uv.y : uv.x;
        float plank = floor(across / 3.0);
        float gap = 1.0 - smoothstep(0.0, max(px, 0.35), mod(across, 3.0));
        float grain = 0.5 + 0.5 * sin(along * 1.7 + hash(vec3(plank, 1.0, 3.0)) * 40.0);
        float s = (0.85 + 0.2 * hash(vec3(plank, 5.0, 9.0))) * (0.95 + 0.06 * grain);
        shade *= mix(1.0, mix(s, 0.55, gap), detail);
    } else if (mat == 3) {
        float row = floor(q.y / 2.0);
        float u = (abs(v_nrm.x) > abs(v_nrm.z) ? q.z : q.x) + mod(row, 2.0) * 1.5;
        float tile = hash(vec3(floor(u / 3.0), row, 11.0));
        float lip = smoothstep(1.2, 2.0, mod(q.y, 2.0));
        float edge = 1.0 - smoothstep(0.0, max(px, 0.3), mod(u, 3.0));
        shade *= mix(1.0, (0.8 + 0.25 * tile) * mix(1.0, 0.78, lip) * mix(1.0, 0.7, edge), detail);
    } else if (mat == 4) {
        emit = base * 2.2;
    } else if (mat == 5) {
        metal = 0.55;
        rough = 0.35;
    }
    if (foot_shadow_m > 0.0) shade *= mix(0.6, 1.0, smoothstep(0.0, foot_shadow_m, v_local.y));
    ALBEDO = linear_of(base * shade);
    ROUGHNESS = rough;
    METALLIC = metal;
    EMISSION = linear_of(emit);
}
";

    public static ShaderMaterial Make(float voxelsPerMetre, float footShadow = 0f)
    {
        var m = new ShaderMaterial { Shader = new Shader { Code = Code } };
        m.SetShaderParameter("voxels_per_m", voxelsPerMetre);
        m.SetShaderParameter("foot_shadow_m", footShadow);
        return m;
    }
}
