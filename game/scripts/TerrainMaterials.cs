using Godot;

namespace Rts.Game;

/// <summary>
/// Terrain and water materials, with the height slice: everything above a chosen height is cut away so
/// tunnels and caves can be seen. Where the cut opens up solid ground the inside of the terrain mesh shows,
/// and it is drawn as flat dark earth, so a sliced hill reads as a solid cross-section. With fog of war
/// (<see cref="FogOfWar"/>) ground never seen is flat black, unlit so that nothing of its shape shows, and
/// ground not in sight now is dimmed and greyed.
/// </summary>
public sealed class TerrainMaterials
{
    // Fog of war, shared by the shaders: the fog texture has a texel per column, 0 for never seen, a half
    // for seen before and 1 for in sight; filtered, so its edges are soft. fog_at gives how far a point has
    // been seen (x) and is in sight now (y); fogged greys and dims a colour out of sight, and fades it out
    // where never seen, where the shaders draw the flat UNSEEN colour instead (emitted, so unlit).
    internal const string FogCode = @"
uniform sampler2D fog_map : filter_linear, repeat_disable, hint_default_white;
uniform vec2 fog_size = vec2(256.0);
uniform float fog_on = 0.0;
const vec3 UNSEEN = vec3(0.008, 0.009, 0.012);
varying vec2 v_fog_xz;
vec2 fog_at(vec2 xz) {
    if (fog_on < 0.5) return vec2(1.0);
    float f = texture(fog_map, xz / fog_size).r;
    return vec2(smoothstep(0.15, 0.4, f), smoothstep(0.65, 0.9, f));
}
vec3 fogged(vec3 c, vec2 fog) {
    vec3 dim = mix(vec3(dot(c, vec3(0.3, 0.59, 0.11))), c, 0.4) * 0.5;
    return mix(dim, c, fog.y) * fog.x;
}
";

    // The look of blocks, shared by the shaders. Vertex colours are picked as on screen (sRGB), so they are
    // turned linear for lighting. Each block gets a slightly different shade of its colour, and a faint
    // darker line along its edges, so the land reads as blocks the way the mood board's does; the lines fade
    // out when zoomed far out, where they would shimmer. The vertex colour's alpha says what a surface is
    // (see ChunkMesher.FineMark): building detail gets neither, glowing blocks shine, leaves sway in the wind
    // (sway, from the vertex shader). In the rain the tops of blocks darken and catch the light (wet).
    private const string LookCode = @"
uniform float block_size = 0.5;
uniform float wind = 0.2;
uniform float wet = 0.0;
uniform float night_glow = 0.0;
varying vec3 v_world;
bool is_leaf(float mark) { return mark > 0.25 && mark < 0.5; }
// How far a leaf vertex moves: the same for every face meeting at a point, so the crown stays closed.
vec3 sway(vec3 world, float time) {
    float amp = block_size * (0.025 + 0.11 * wind);
    float gust = 0.7 + 0.3 * sin(time * 0.5 + world.x * 0.05);
    return vec3(sin(time * (1.3 + wind) + world.x * 0.45 + world.z * 0.3),
                0.0,
                cos(time * (1.1 + wind) + world.z * 0.4 + world.x * 0.2) * 0.6) * amp * gust;
}
varying vec3 v_world_normal;
// The size of the blocks a surface is made of, in whole blocks: a half for the half-size blocks of nearby
// ground (ChunkLod.Fine, marked in the mesh's UV), else 1. Fine voxels are an eighth of a whole block either way.
varying float v_scale;
float scale_of(vec2 uv) { return uv.x > 0.5 ? 0.5 : 1.0; }
vec3 linear_of(vec3 c) { return mix(c, c * (c * (c * 0.305306011 + 0.682171111) + 0.012522878), 0.85); }
float block_hash(vec3 p) {
    p = fract(p * 0.3183099 + vec3(0.71, 0.113, 0.419));
    p *= 17.0;
    return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}
vec3 block_look(vec3 c, float mark) {
    c = linear_of(c);
    if (mark > 0.5 && mark < 0.6) return c * 0.3; // lava: dark under its glow
    if (mark > 0.6 && mark < 0.66) return c; // crystal
    float voxels = 8.0 * v_scale;
    vec3 inside = v_world / (block_size * v_scale) - v_world_normal * 0.02;
    // Each fine voxel (an eighth of a block) a slightly different shade: the grain of the voxel style.
    // It fades out far away, where it would shimmer.
    float px = max(fwidth(inside.x), max(fwidth(inside.y), fwidth(inside.z)));
    float grain = (block_hash(floor(inside * voxels) + 0.5) - 0.5) * (1.0 - smoothstep(0.04, 0.12, px * voxels / 8.0));
    if (mark < 0.95 && (!is_leaf(mark) || mark < 0.34)) return c * (1.0 + 0.2 * grain); // fine voxels have no block grid
    c *= (0.9 + 0.17 * block_hash(floor(inside))) * (1.0 + 0.22 * grain);
    vec3 f = fract(inside);
    vec3 a = abs(v_world_normal);
    vec2 uv = a.y > 0.5 ? f.xz : a.x > 0.5 ? f.yz : f.xy;
    vec2 e = min(uv, 1.0 - uv);
    float line = 0.48 / voxels; // as wide on a half-size block as on a whole one
    float edge = (1.0 - smoothstep(0.0, max(line, px), min(e.x, e.y))) * (1.0 - smoothstep(0.05, 0.15, px));
    return c * (1.0 - 0.22 * edge);
}
// Where a fragment is lit from: the middle of the fine voxel (an eighth of a block) it lies on, lifted just
// off the face. Shadows and lamplight then fall voxel by voxel, in steps, as in the rest of the voxel look.
vec3 voxel_light_vertex(mat4 view) {
    float fine = block_size / 8.0;
    vec3 cell = (floor(v_world / fine - v_world_normal * 0.01) + 0.5) * fine;
    vec3 n = normalize(v_world_normal);
    return (view * vec4(cell + n * fine * 0.5, 1.0)).xyz;
}
vec3 block_glow(vec3 c, float mark) {
    if (mark <= 0.5 || mark >= 0.75) return vec3(0.0);
    // Lava: slow bright veins that flow across a darker crust.
    if (mark < 0.6) {
        vec2 p = floor(v_world.xz / block_size * 4.0) * block_size / 4.0 * 3.0; // in pixels of a quarter block
        float flow = sin(p.x * 2.3 + sin(p.y * 1.9 + TIME * 0.5) * 2.0 + TIME * 0.7);
        flow += sin(p.y * 2.1 - sin(p.x * 1.6 - TIME * 0.35) * 2.0 - TIME * 0.4);
        float crust = smoothstep(1.1, 1.8, abs(flow));
        return linear_of(c) * mix(0.95, 0.2, crust);
    }
    // Windows: dark glass by day, warm lamplight from inside by night.
    if (mark > 0.66) return vec3(1.0, 0.5, 0.17) * 3.2 * night_glow;
    // Crystal pulses slowly.
    float pulse = 0.85 + 0.15 * sin(TIME * 1.4 + v_world.x * 0.7 + v_world.z * 0.5);
    return linear_of(c) * 2.2 * pulse;
}
";

    // Without the slice: plain vertex colours, back faces culled, no discard (keeps early depth testing).
    private const string OpaqueCode = @"
shader_type spatial;
render_mode cull_back;
" + FogCode + LookCode + @"
void vertex() {
    v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    v_world_normal = (MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz;
    v_fog_xz = v_world.xz;
    v_scale = scale_of(UV);
    if (is_leaf(COLOR.a)) VERTEX += sway(v_world, TIME);
}
void fragment() {
    vec2 fog = fog_at(v_fog_xz);
    LIGHT_VERTEX = voxel_light_vertex(VIEW_MATRIX);
    float wet_top = wet * smoothstep(0.5, 0.9, v_world_normal.y);
    ALBEDO = fogged(block_look(COLOR.rgb, COLOR.a), fog) * (1.0 - 0.3 * wet_top);
    SPECULAR = (0.3 + 0.3 * wet_top) * fog.x;
    ROUGHNESS = 0.9 - 0.55 * wet_top;
    EMISSION = UNSEEN * (1.0 - fog.x) + block_glow(COLOR.rgb, COLOR.a) * fog.x;
}
";

    // With the slice: fragments above the cut are dropped, and so are the undersides of the first hidden
    // layer, which lie exactly on the cut. The back faces the cut exposes are drawn as the cut surface: a
    // flat earth colour (emitted, so no lighting or shadow speckle) with the block grid of the cut plane.
    private const string OpaqueSlicedCode = @"
shader_type spatial;
render_mode cull_disabled;
uniform float slice_y = 100000.0;
uniform vec3 cut_color : source_color = vec3(0.30, 0.23, 0.17);
varying float v_world_y;
varying float v_normal_y;
" + FogCode + LookCode + @"
void vertex() {
    v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    v_world_normal = (MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz;
    v_world_y = v_world.y;
    v_normal_y = v_world_normal.y;
    v_fog_xz = v_world.xz;
    v_scale = scale_of(UV);
    if (is_leaf(COLOR.a)) VERTEX += sway(v_world, TIME);
}
void fragment() {
    if (v_world_y > slice_y + 0.002 || (v_world_y > slice_y - 0.002 && v_normal_y < -0.5)) discard;
    if (FRONT_FACING) {
        vec2 fog = fog_at(v_fog_xz);
        LIGHT_VERTEX = voxel_light_vertex(VIEW_MATRIX);
        ALBEDO = fogged(block_look(COLOR.rgb, COLOR.a), fog);
        SPECULAR = 0.3 * fog.x;
        ROUGHNESS = 0.9;
        EMISSION = UNSEEN * (1.0 - fog.x) + block_glow(COLOR.rgb, COLOR.a) * fog.x;
    } else {
        // Where the view ray crosses the cut plane, for a block grid that stays put as the camera moves.
        vec3 cam = INV_VIEW_MATRIX[3].xyz;
        vec3 world = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
        vec3 dir = world - cam;
        vec2 onCut = dir.y < -0.0001 ? (cam + dir * ((slice_y - cam.y) / dir.y)).xz : world.xz;
        vec2 cell = floor(onCut / block_size);
        float checker = mod(cell.x + cell.y, 2.0);
        vec2 fog = fog_at(onCut);
        ALBEDO = vec3(0.0);
        SPECULAR = 0.0;
        EMISSION = fogged(cut_color * (0.9 + 0.1 * checker), fog) + UNSEEN * (1.0 - fog.x);
    }
}
";

    // Water: deep and clear, with slow ripples that catch the sun (and the moon) as the light moves over them,
    // and more opaque where it is seen at a low angle.
    private const string WaterCode = @"
shader_type spatial;
render_mode blend_mix, cull_back, depth_draw_opaque;
uniform float slice_y = 100000.0;
varying float v_world_y;
varying float v_normal_y;
varying vec3 v_world;
" + FogCode + @"
vec3 linear_of(vec3 c) { return c * (c * (c * 0.305306011 + 0.682171111) + 0.012522878); }
void vertex() {
    v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    v_world_y = v_world.y;
    v_normal_y = (MODEL_MATRIX * vec4(NORMAL, 0.0)).y;
    v_fog_xz = v_world.xz;
}
void fragment() {
    if (v_world_y > slice_y + 0.002 || (v_world_y > slice_y - 0.002 && v_normal_y < -0.5)) discard;
    vec2 fog = fog_at(v_fog_xz);
    vec3 c = mix(linear_of(COLOR.rgb), vec3(0.03, 0.27, 0.44), 0.65);
    if (v_normal_y > 0.5) {
        vec2 p = v_world.xz;
        float t = TIME;
        vec2 slope = vec2(cos(p.x * 1.9 + t * 1.1) * 0.6 + cos((p.x + p.y) * 4.3 + t * 2.3) * 0.3,
                          cos(p.y * 1.6 - t * 0.9) * 0.6 + cos((p.x - p.y) * 3.7 - t * 1.9) * 0.3);
        vec3 n = normalize(vec3(slope.x * 0.07, 1.0, slope.y * 0.07));
        NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);
        c *= 0.9 + 0.1 * slope.x * slope.y;
    }
    float fresnel = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 4.0);
    ALBEDO = fogged(c, fog);
    ROUGHNESS = 0.3;
    METALLIC = 0.0;
    SPECULAR = 0.4 * fog.x;
    EMISSION = UNSEEN * (1.0 - fog.x);
    ALPHA = mix(min(1.0, COLOR.a + 0.1), 1.0, fresnel * 0.5);
}
";

    private readonly Shader _opaqueShader = new() { Code = OpaqueCode };
    private readonly Shader _slicedShader = new() { Code = OpaqueSlicedCode };

    public ShaderMaterial Opaque { get; }
    public ShaderMaterial Water { get; }

    public TerrainMaterials()
    {
        Opaque = new ShaderMaterial { Shader = _opaqueShader };
        Water = new ShaderMaterial { Shader = new Shader { Code = WaterCode } };
    }

    /// <summary>Cuts away everything above world height <paramref name="sliceY"/> (metres), or nothing if null.</summary>
    /// <summary>How hard the wind blows (0 to 1, sways the trees) and how wet the ground is (0 to 1).</summary>
    public void SetWeather(float wind, float wet)
    {
        if (Mathf.IsEqualApprox(wind, _wind) && Mathf.IsEqualApprox(wet, _wet)) return;
        (_wind, _wet) = (wind, wet);
        Opaque.SetShaderParameter("wind", wind);
        Opaque.SetShaderParameter("wet", wet);
    }

    /// <summary>How much windows glow with lamplight (0 by day, 1 at night).</summary>
    public void SetNight(float glow)
    {
        if (Mathf.IsEqualApprox(glow, _night)) return;
        _night = glow;
        Opaque.SetShaderParameter("night_glow", glow);
    }

    private float _night = -1f;
    private float _wind = -1f, _wet = -1f;
    private readonly System.Collections.Generic.List<ShaderMaterial> _tracked = new();

    /// <summary>Keeps <paramref name="m"/>'s fog of war (a shader with <see cref="FogCode"/>) in step with the terrain's.</summary>
    public void Track(ShaderMaterial m)
    {
        _tracked.Add(m);
        SetFog(_fog, _fogSize);
    }

    public void SetSlice(float? sliceY)
    {
        Opaque.Shader = sliceY == null ? _opaqueShader : _slicedShader;
        if (_wind >= 0)
        {
            Opaque.SetShaderParameter("wind", _wind);
            Opaque.SetShaderParameter("wet", _wet);
        }
        if (_night >= 0) Opaque.SetShaderParameter("night_glow", _night);
        float y = sliceY ?? 100000f;
        if (sliceY != null) Opaque.SetShaderParameter("slice_y", y);
        Water.SetShaderParameter("slice_y", y);
        SetFog(_fog, _fogSize); // the other shader has its own parameters
    }

    private Texture2D? _fog;
    private Vector2 _fogSize;

    /// <summary>Draws fog of war from <paramref name="fog"/> (a texel per column of a world of <paramref name="sizeMetres"/>), or none if null.</summary>
    public void SetFog(Texture2D? fog, Vector2 sizeMetres)
    {
        _fog = fog;
        _fogSize = sizeMetres;
        foreach (var m in System.Linq.Enumerable.Concat(new[] { Opaque, Water }, _tracked))
        {
            m.SetShaderParameter("fog_on", fog != null ? 1f : 0f);
            if (fog == null) continue;
            m.SetShaderParameter("fog_map", fog);
            m.SetShaderParameter("fog_size", sizeMetres);
        }
    }
}
