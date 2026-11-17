using Godot;

namespace Rts.Game;

/// <summary>
/// Thin dark outlines where one thing stands in front of another: the edge of a leaf clump against the
/// ground behind it, a roof against the sky, a step down the hill. Drawn by a quad over the whole screen
/// that reads the depth of what is already drawn and darkens a pixel that is nearer than the pixels on
/// either side of it (so a flat surface, however steeply it is seen, gets no lines). The look of the
/// concept art: shapes read apart from each other without any extra detail in the models.
/// </summary>
public partial class Outlines : MeshInstance3D
{
    private const string Code = @"
shader_type spatial;
render_mode unshaded, blend_mix, depth_test_disabled, depth_draw_never, cull_disabled, fog_disabled, shadows_disabled;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest, repeat_disable;
uniform float strength = 0.5;
void vertex() {
    POSITION = vec4(VERTEX.xy * 2.0, 0.0, 1.0);
}
float view_depth(vec2 uv, mat4 inv_projection) {
    float d = textureLod(depth_tex, uv, 0.0).r;
#if CURRENT_RENDERER == RENDERER_COMPATIBILITY
    d = d * 2.0 - 1.0;
#endif
    vec4 v = inv_projection * vec4(uv * 2.0 - 1.0, d, 1.0);
    return -v.z / v.w;
}
void fragment() {
    vec2 px = 1.0 / VIEWPORT_SIZE;
    float c = view_depth(SCREEN_UV, INV_PROJECTION_MATRIX);
    float l = view_depth(SCREEN_UV - vec2(px.x, 0.0), INV_PROJECTION_MATRIX);
    float r = view_depth(SCREEN_UV + vec2(px.x, 0.0), INV_PROJECTION_MATRIX);
    float d = view_depth(SCREEN_UV - vec2(0.0, px.y), INV_PROJECTION_MATRIX);
    float u = view_depth(SCREEN_UV + vec2(0.0, px.y), INV_PROJECTION_MATRIX);
    // How far this pixel stands out in front of its neighbours, relative to its distance.
    float bulge = max(l + r - 2.0 * c, d + u - 2.0 * c) / c;
    float edge = smoothstep(0.03, 0.09, bulge);
    ALBEDO = vec3(0.0);
    ALPHA = strength * edge;
}
";

    public Outlines()
    {
        var material = new ShaderMaterial { Shader = new Shader { Code = Code }, RenderPriority = 100 };
        Mesh = new QuadMesh { Size = Vector2.One, Material = material };
        CastShadow = ShadowCastingSetting.Off;
        // Never culled: the vertex shader puts it over the whole screen wherever the node is.
        ExtraCullMargin = 16384f;
    }
}
