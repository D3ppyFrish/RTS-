using System;
using Godot;
using Rts.Sim;
using Rts.Sim.Units;

namespace Rts.Game;

/// <summary>
/// When the camera is far out, a small badge floats over every unit you can see showing what kind of
/// unit it is: a pick for workers, a spear for line infantry, a shield for heavy infantry, a bow for
/// ranged units, a horse for cavalry, a shovel for sappers, a star for casters, a bomb for siege, a paw
/// for monsters, a purse for caravans and a wheel for carts. The badge is in its owner's colour with a dark rim, or a gold
/// rim for the forms units evolve into (see <see cref="EvolutionSystem"/>). Badges keep the same size on
/// screen, face the camera, and fade in as the camera pulls back past <see cref="ShowFrom"/>.
/// </summary>
public partial class UnitIcons : Node3D
{
    // 12 transform + 4 colour (team colour and fade) + 4 custom data (glyph, gold rim).
    private const int FloatsPerInstance = 20;

    /// <summary>Camera distances (metres) over which the badges fade in.</summary>
    public const float ShowFrom = 70f, FullAt = 85f;
    /// <summary>Badge size as a share of its distance from the camera, so it stays about 20 pixels across.</summary>
    private const float SizePerMetre = 0.018f;
    private const int Cell = 16;

    /// <summary>One 12 by 12 glyph per <see cref="UnitRole"/>, in its order; drawn in a 16 pixel cell.</summary>
    internal static readonly string[][] Glyphs =
    {
        new[] // worker: a pick
        {
            ".XXXXXXXXX..", "XX...XX...XX", "X....XX....X", ".....XX.....", ".....XX.....", ".....XX.....",
            ".....XX.....", ".....XX.....", ".....XX.....", ".....XX.....", ".....XX.....", "....XXXX....",
        },
        new[] // line infantry: a spear
        {
            "........XXXX", ".........XXX", "........XXXX", ".......XXX.X", "......XXX...", ".....XXX....",
            "....XXX.....", "...XXX......", "..XXX.......", ".XXX........", "XXX.........", "XX..........",
        },
        new[] // heavy infantry: a shield
        {
            "XXXXXXXXXXXX", "XXXXXXXXXXXX", "XX...XX...XX", "XX...XX...XX", "XXXXXXXXXXXX", "XXXXXXXXXXXX",
            "XX...XX...XX", ".XX..XX..XX.", ".XX..XX..XX.", "..XX.XX.XX..", "...XXXXXX...", ".....XX.....",
        },
        new[] // ranged: a bow and arrow
        {
            "XXXX........", "X..XXX......", "X....XX.....", "X.....XX..X.", "X.....XX...X", "XXXXXXXXXXXX",
            "X.....XX...X", "X.....XX..X.", "X....XX.....", "X..XXX......", "XXXX........", "............",
        },
        new[] // cavalry: a horse's head
        {
            "......XX....", ".....XXXX...", "....XXXXXX..", "...XXXXXXXX.", "..XXXX.XXXXX", ".XXXXXXXXXXX",
            "XXXXX..XXXXX", "XXX....XXXX.", ".......XXXX.", "......XXXXX.", ".....XXXXXX.", "....XXXXXXX.",
        },
        new[] // sapper: a shovel
        {
            "....XXXX....", "....X..X....", ".....XX.....", ".....XX.....", ".....XX.....", ".....XX.....",
            "...XXXXXX...", "...XXXXXX...", "...XXXXXX...", "...XXXXXX...", "....XXXX....", ".....XX.....",
        },
        new[] // caster: a star
        {
            ".....XX.....", ".....XX.....", "....XXXX....", "XXXXXXXXXXXX", ".XXXXXXXXXX.", "...XXXXXX...",
            "...XXXXXX...", "..XXXXXXXX..", "..XXX..XXX..", ".XXX....XXX.", ".XX......XX.", "............",
        },
        new[] // siege: a bomb with a lit fuse
        {
            ".........X..", "........X.X.", ".......X....", "....XXXX....", "...XXXXXX...", "..XXXXXXXX..",
            ".XXXXXXXXXX.", ".XXXXXXXXXX.", ".XXXXXXXXXX.", "..XXXXXXXX..", "...XXXXXX...", "....XXXX....",
        },
        new[] // monster: a paw
        {
            "..XX....XX..", "..XX....XX..", "............", "XX..XXXX..XX", "XX..XXXX..XX", "...XXXXXX...",
            "..XXXXXXXX..", "..XXXXXXXX..", "..XXXXXXXX..", "...XXXXXX...", "............", "............",
        },
        new[] // trader: a purse
        {
            "....XXXX....", ".....XX.....", "....XXXX....", "...XXXXXX...", "..XXXXXXXX..", ".XXXX..XXXX.",
            ".XXX.XXXXXX.", ".XXXX..XXXX.", ".XXXXXX.XXX.", ".XXXX..XXXX.", "..XXXXXXXX..", "............",
        },
        new[] // hauler: a cart's spoked wheel
        {
            "...XXXXXX...", "..XX.XX.XX..", ".XXX.XX.XXX.", "XX.XXXXXX.XX", "X...XXXX...X", "XXXXX..XXXXX",
            "XXXXX..XXXXX", "X...XXXX...X", "XX.XXXXXX.XX", ".XXX.XX.XXX.", "..XX.XX.XX..", "...XXXXXX...",
        },
    };

    private const string ShaderCode = @"
shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled;
uniform sampler2D atlas : filter_nearest;
uniform float glyphs = 10.0;
uniform float slice_y = 100000.0;
varying vec4 v_team;
varying vec2 v_glyph;
varying float v_world_y;
void vertex() {
    v_team = COLOR;
    v_glyph = INSTANCE_CUSTOM.xy;
    v_world_y = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).y;
}
void fragment() {
    if (v_world_y > slice_y) discard;
    float edge = min(min(UV.x, 1.0 - UV.x), min(UV.y, 1.0 - UV.y));
    vec3 c;
    if (edge < 0.1) {
        c = v_glyph.y > 0.5 ? vec3(1.0, 0.78, 0.2) : vec3(0.05, 0.05, 0.06);
    } else {
        float m = texture(atlas, vec2((v_glyph.x + UV.x) / glyphs, UV.y)).r;
        float light = dot(v_team.rgb, vec3(0.3, 0.55, 0.15));
        vec3 ink = light > 0.62 ? vec3(0.08) : vec3(1.0);
        c = m > 0.5 ? ink : v_team.rgb;
    }
    ALBEDO = c;
    ALPHA = v_team.a;
}
";

    private Simulation _sim = null!;
    private FogOfWar _fog = null!;
    private Camera3D _camera = null!;
    private CameraRig _rig = null!;
    private MultiMesh _mesh = null!;
    private MultiMeshInstance3D _node = null!;
    private float[] _buffer = Array.Empty<float>();
    private ShaderMaterial _material = null!;

    public ShaderMaterial Material => _material;
    /// <summary>Badges drawn this frame (for scripted tests).</summary>
    public int Shown { get; private set; }

    public void Init(Simulation sim, CameraRig rig, FogOfWar fog)
    {
        _sim = sim;
        _rig = rig;
        _camera = rig.Camera;
        _fog = fog;
        _material = new ShaderMaterial { Shader = new Shader { Code = ShaderCode } };
        _material.SetShaderParameter("atlas", ImageTexture.CreateFromImage(Atlas()));
        _material.SetShaderParameter("glyphs", (float)Glyphs.Length);
        _material.RenderPriority = 1; // over health bars and rings
        _mesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            UseCustomData = true,
            Mesh = new QuadMesh { Size = Vector2.One },
        };
        var worldBox = new Aabb(new Vector3(-8, -8, -8),
            new Vector3(sim.World.SizeX + 16, sim.World.SizeY + 64, sim.World.SizeZ + 16) * ChunkMesher.BlockSize);
        _node = new MultiMeshInstance3D
        {
            Multimesh = _mesh, MaterialOverride = _material, CustomAabb = worldBox,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_node);
    }

    /// <summary>The glyph atlas: one 16 pixel cell per glyph in a row, white where the glyph is.</summary>
    public static Image Atlas()
    {
        var image = Image.CreateEmpty(Cell * Glyphs.Length, Cell, false, Image.Format.L8);
        for (int g = 0; g < Glyphs.Length; g++)
        {
            var rows = Glyphs[g];
            if (rows.Length != 12) throw new InvalidOperationException($"glyph {g} has {rows.Length} rows");
            for (int y = 0; y < 12; y++)
            {
                if (rows[y].Length != 12) throw new InvalidOperationException($"glyph {g} row {y} is {rows[y].Length} wide");
                for (int x = 0; x < 12; x++)
                    if (rows[y][x] == 'X') image.SetPixel(g * Cell + 2 + x, 2 + y, Colors.White);
            }
        }
        return image;
    }

    /// <summary>Places the badges for this frame; <paramref name="alpha"/> is how far we are between ticks.</summary>
    public void UpdateFrame(float alpha)
    {
        Shown = 0;
        float fade = Mathf.Clamp((_rig.Distance - ShowFrom) / (FullAt - ShowFrom), 0, 1);
        _node.Visible = fade > 0;
        if (fade <= 0) return;

        var u = _sim.Units;
        var defs = _sim.Content.Units;
        if (_mesh.InstanceCount < Math.Max(16, u.Count))
        {
            int cap = 1 << (int)Math.Ceiling(Math.Log2(Math.Max(16, u.Count)));
            _mesh.InstanceCount = cap;
            _buffer = new float[cap * FloatsPerInstance];
        }
        var basis = _camera.GlobalTransform.Basis;
        Vector3 right = basis.X.Normalized(), up = basis.Y.Normalized(), back = basis.Z.Normalized();
        var eye = _camera.GlobalPosition;
        float bs = ChunkMesher.BlockSize;
        int n = 0;
        for (int i = 0; i < u.Count; i++)
        {
            if (!_fog.Shows(i)) continue;
            var def = defs[u.Def[i]];
            float x = Mathf.Lerp(u.PrevX[i].ToFloat(), u.X[i].ToFloat(), alpha);
            float z = Mathf.Lerp(u.PrevZ[i].ToFloat(), u.Z[i].ToFloat(), alpha);
            float y = Mathf.Lerp(u.PrevY[i], u.Y[i], alpha) + ChunkRenderer.GroundDrop(x, u.Y[i], z);
            float size = SizePerMetre * eye.DistanceTo(new Vector3(x, y, z) * bs);
            // Above the unit's head, clear of the model however far out the camera is.
            var p = new Vector3(x * bs, y * bs + 1.6f + size * 0.6f, z * bs);
            var team = UnitRenderer.TeamColors[u.Owner[i] & 7];
            int o = n++ * FloatsPerInstance;
            var b = _buffer;
            Vector3 bx = right * size, by = up * size, bz = back;
            b[o + 0] = bx.X; b[o + 1] = by.X; b[o + 2] = bz.X; b[o + 3] = p.X;
            b[o + 4] = bx.Y; b[o + 5] = by.Y; b[o + 6] = bz.Y; b[o + 7] = p.Y;
            b[o + 8] = bx.Z; b[o + 9] = by.Z; b[o + 10] = bz.Z; b[o + 11] = p.Z;
            b[o + 12] = team.R; b[o + 13] = team.G; b[o + 14] = team.B; b[o + 15] = fade;
            b[o + 16] = (int)def.Role; b[o + 17] = def.IsForm ? 1 : 0; b[o + 18] = 0; b[o + 19] = 0;
        }
        Shown = n;
        RenderingServer.MultimeshSetBuffer(_mesh.GetRid(), _buffer);
        _mesh.VisibleInstanceCount = n;
    }
}
