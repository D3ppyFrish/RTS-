using System.Collections.Generic;
using Godot;
using Rts.Sim;
using Rts.Sim.Economy;

namespace Rts.Game;

/// <summary>
/// How the world is lit: the sky, the sun or moon (the one light that casts shadows), the ambient light,
/// the haze that softens the far distance, glow on bright things and soft shading in corners, all following
/// the time of day (<see cref="DayCycle"/>). From dusk to dawn a lantern glows at the door of each building
/// you can see, and the nearest of them light the ground around them. A pixel sun (by night a pixel moon)
/// hangs in the sky where the light comes from; it is low in the sky only around sunrise and sunset and at
/// night, so it shows when the camera is tilted down toward the horizon.
///
/// Screen-space ambient occlusion only draws with the Forward+ renderer (the game on Windows); the
/// compatibility renderer the screenshot scripts use skips it and draws everything else.
/// </summary>
public partial class WorldLighting : Node3D
{
    /// <summary>The most lantern lights that shine at once, on the buildings nearest the camera.</summary>
    private const int MaxLanternLights = 16;
    /// <summary>Lantern lights further than this (metres) from the camera's focus stay off.</summary>
    private const float LanternLightRange = 90f;

    private readonly DayCycle _cycle;
    private Simulation _sim = null!;
    private FogOfWar _fog = null!;
    private CameraRig _rig = null!;
    private Godot.Environment _env = null!;
    private ProceduralSkyMaterial _sky = null!;
    private DirectionalLight3D _sun = null!;
    private readonly List<OmniLight3D> _lights = new();
    private MultiMeshInstance3D _lanterns = null!;
    private StandardMaterial3D _lanternMaterial = null!;
    private float _lastSkyHour = -100f;
    private MeshInstance3D _disc = null!;
    private StandardMaterial3D _discMaterial = null!;
    private ImageTexture _sunTexture = null!, _moonTexture = null!;
    /// <summary>How far away (metres) the sun and moon are drawn: past the world, inside the camera's far plane.</summary>
    private const float DiscDistance = 700f;
    /// <summary>The sun's and moon's size in the sky (metres at <see cref="DiscDistance"/>), about 5 degrees across.</summary>
    private const float DiscSize = 60f;
    private readonly List<(float Distance, Vector3 At, float Range)> _doors = new();

    public WorldLighting(DayCycle cycle) => _cycle = cycle;

    public DayCycle Cycle => _cycle;
    /// <summary>The hour the world is lit for this frame.</summary>
    public float Hour { get; private set; }
    /// <summary>How the weather looks this frame (see <see cref="DayCycle.SkyAt"/>).</summary>
    public DayCycle.WeatherLook Look { get; private set; }
    /// <summary>A flash of lightning, 0 to 1, set by <see cref="Ambience"/> in a storm.</summary>
    public float Flash { get; set; }
    /// <summary>The light this frame, after the weather.</summary>
    public DayCycle.Light Light { get; private set; }

    public void Init(Simulation sim, FogOfWar fog, CameraRig rig)
    {
        (_sim, _fog, _rig) = (sim, fog, rig);
        _sky = new ProceduralSkyMaterial { SunAngleMax = 20f, SunCurve = 0.1f, GroundCurve = 0.04f };
        _env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = _sky, RadianceSize = Sky.RadianceSizeEnum.Size64 },
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
            TonemapMode = Godot.Environment.ToneMapper.Aces,
            TonemapExposure = 1.1f,
            TonemapWhite = 6f,
            // Glow: lanterns, crystals and sunlit water bloom a little.
            GlowEnabled = true,
            GlowIntensity = 0.6f,
            GlowStrength = 1.0f,
            GlowBloom = 0.02f,
            GlowHdrThreshold = 1.1f,
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Screen,
            // Haze: far land fades into the sky's colour, which gives depth when zoomed out.
            FogEnabled = true,
            FogMode = Godot.Environment.FogModeEnum.Exponential,
            FogDensity = 0.0011f,
            FogAerialPerspective = 0.2f,
            FogSkyAffect = 0f,
            FogSunScatter = 0.15f,
            // Soft shading where blocks meet (Forward+ only).
            SsaoEnabled = true,
            SsaoRadius = 1.2f,
            SsaoIntensity = 1.6f,
            SsaoDetail = 0.6f,
            AdjustmentEnabled = true,
            AdjustmentSaturation = 1f,
            AdjustmentContrast = 1.04f,
        };
        AddChild(new WorldEnvironment { Environment = _env });

        _sun = new DirectionalLight3D
        {
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 150f,
            ShadowBlur = 1.5f,
            ShadowBias = 0.05f,
            LightAngularDistance = 0.8f,
            // The sky's own round sun is not drawn; the pixel one is.
            SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly,
        };
        AddChild(_sun);

        _sunTexture = Pixels(SunGlyph);
        _moonTexture = Pixels(MoonGlyph);
        _discMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
            AlphaScissorThreshold = 0.5f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            DisableFog = true,
            DisableReceiveShadows = true,
            AlbedoTexture = _sunTexture,
        };
        _disc = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = Vector2.One * DiscSize, Material = _discMaterial },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            // Bigger than the world's bounds would otherwise allow, so it is never culled as off screen.
            ExtraCullMargin = DiscDistance,
        };
        AddChild(_disc);

        for (int i = 0; i < MaxLanternLights; i++)
        {
            var light = new OmniLight3D
            {
                LightColor = new Color(1f, 0.62f, 0.3f),
                OmniRange = 7f,
                OmniAttenuation = 1.4f,
                ShadowEnabled = false,
                Visible = false,
            };
            AddChild(light);
            _lights.Add(light);
        }
        // The lanterns themselves: small glowing blocks, brighter than white so they bloom at night.
        _lanternMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(1f, 0.75f, 0.4f),
        };
        var cube = new BoxMesh { Size = Vector3.One * ChunkMesher.BlockSize * 0.35f, Material = _lanternMaterial };
        _lanterns = new MultiMeshInstance3D
        {
            Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = cube },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_lanterns);
        _rig.Camera.AddChild(new Outlines { Position = new Vector3(0, 0, -1f) });
        UpdateFrame(0f);
    }

    /// <summary>Lights the world for the time of day at the last tick plus <paramref name="alpha"/> of the next.</summary>
    public void UpdateFrame(float alpha)
    {
        Hour = _cycle.Hour(_sim.Tick, alpha);
        Look = _cycle.SkyAt(_sim.Tick, alpha);
        var light = Overcast(DayCycle.LightAt(Hour), Look);
        Light = light;
        var (pitch, yaw) = DayCycle.Direction(Hour);
        _sun.RotationDegrees = new Vector3(pitch, yaw, 0);
        _sun.LightColor = light.Sun;
        _sun.LightEnergy = light.SunEnergy;
        _sun.ShadowOpacity = Mathf.Clamp(light.SunEnergy * 1.2f, 0.4f, 1f);
        PlaceDisc(light);

        _env.AmbientLightColor = light.Ambient;
        _env.AmbientLightEnergy = light.AmbientEnergy + Flash * 2.5f;
        _env.FogLightColor = light.Haze;
        // Rain and fog close the distance in.
        _env.FogDensity = 0.0011f + 0.004f * Look.Mist + 0.0025f * Look.Rain;
        // Colours fade by moonlight, as they do to the eye at night.
        _env.AdjustmentSaturation = Mathf.Lerp(1f, 0.45f, light.Lanterns) * (1f - 0.3f * Look.Cloud);
        _env.FogSunScatter = DayCycle.IsNight(Hour) ? 0f : 0.15f;
        // The sky only shows at a low camera; changing it re-renders it, so it follows in small steps.
        float skyKey = Hour + Look.Cloud * 100f + Look.Mist * 1000f;
        if (Mathf.Abs(skyKey - _lastSkyHour) > 0.1f)
        {
            _lastSkyHour = skyKey;
            _sky.SkyTopColor = light.SkyTop;
            _sky.SkyHorizonColor = light.SkyHorizon;
            _sky.GroundHorizonColor = light.SkyHorizon.Darkened(0.2f);
            _sky.GroundBottomColor = light.SkyTop.Darkened(0.5f);
            _sky.SunAngleMax = DayCycle.IsNight(Hour) ? 4f : 20f;
        }
        UpdateLanterns(light.Lanterns);
    }

    /// <summary>
    /// The light under cloud: the sun dims and loses its colour, shadows soften, the sky and the haze turn
    /// grey, and lanterns come on a little earlier under a dark sky.
    /// </summary>
    private static DayCycle.Light Overcast(DayCycle.Light l, DayCycle.WeatherLook sky)
    {
        float c = sky.Cloud;
        if (c <= 0.001f && sky.Mist <= 0.001f) return l;
        Color Grey(Color col, float amount)
        {
            float v = (col.R + col.G + col.B) / 3f;
            return col.Lerp(new Color(v, v, v * 1.05f), amount);
        }
        float dim = 1f - 0.65f * c;
        return l with
        {
            Sun = Grey(l.Sun, 0.6f * c),
            SunEnergy = l.SunEnergy * dim,
            Ambient = Grey(l.Ambient, 0.5f * c),
            AmbientEnergy = l.AmbientEnergy * (1f + 0.25f * c),
            SkyTop = Grey(l.SkyTop, 0.8f * c) * (1f - 0.35f * c),
            SkyHorizon = Grey(l.SkyHorizon, 0.7f * Mathf.Max(c, sky.Mist)),
            Haze = Grey(l.Haze, 0.6f * Mathf.Max(c, sky.Mist)),
            Lanterns = Mathf.Max(l.Lanterns, Mathf.Clamp((c - 0.7f) * 1.2f, 0f, 0.35f)),
        };
    }

    /// <summary>The pixel sun or moon, where the light comes from, as seen from the camera.</summary>
    private void PlaceDisc(DayCycle.Light light)
    {
        bool night = DayCycle.IsNight(Hour);
        var towardLight = _sun.GlobalTransform.Basis.Z.Normalized();
        _disc.GlobalPosition = _rig.Camera.GlobalPosition + towardLight * DiscDistance;
        _discMaterial.AlbedoTexture = night ? _moonTexture : _sunTexture;
        // Brighter than white so it glows; tinted by the light, so the sun is redder as it sets.
        _discMaterial.AlbedoColor = night ? new Color(1.15f, 1.2f, 1.35f) : light.Sun.Lightened(0.35f) * 1.5f;
        // Hidden in the moment the light swaps between the sun and the moon at the horizon, and behind thick cloud.
        _disc.Visible = light.SunEnergy > 0.12f && Look.Cloud < 0.7f;
    }

    // The sun and moon, one character per pixel: Y the bright middle, y the face, o the rim and rays, . clear.
    private static readonly string[] SunGlyph =
    {
        ".......oo.......", "..o....oo....o..", "...o........o...", ".......yy.......",
        ".....yyyyyy.....", "....yyyYYyyy....", "....yyYYYYyy....", "oo.yyYYYYYYyy.oo",
        "oo.yyYYYYYYyy.oo", "....yyYYYYyy....", "....yyyYYyyy....", ".....yyyyyy.....",
        ".......yy.......", "...o........o...", "..o....oo....o..", ".......oo.......",
    };
    private static readonly string[] MoonGlyph =
    {
        "................", ".....yyyy.......", "...yyYYy........", "..yyYYy.........",
        "..yYYy..........", ".yYYy...........", ".yYYy...........", ".yYYy...........",
        ".yYYy...........", ".yYYYy..........", "..yYYYy.........", "..yyYYYyy...yy..",
        "...yyYYYYyyyy...", ".....yyyyyy.....", "................", "................",
    };

    private static ImageTexture Pixels(string[] rows)
    {
        var image = Image.CreateEmpty(rows[0].Length, rows.Length, false, Image.Format.Rgba8);
        for (int y = 0; y < rows.Length; y++)
        for (int x = 0; x < rows[y].Length; x++)
            image.SetPixel(x, y, rows[y][x] switch
            {
                'Y' => new Color(1f, 1f, 0.92f),
                'y' => new Color(1f, 0.88f, 0.55f),
                'o' => new Color(1f, 0.72f, 0.3f),
                _ => new Color(0, 0, 0, 0),
            });
        return ImageTexture.CreateFromImage(image);
    }

    private void UpdateLanterns(float glow)
    {
        var mm = _lanterns.Multimesh;
        if (glow <= 0.01f)
        {
            if (mm.InstanceCount != 0) mm.InstanceCount = 0;
            foreach (var l in _lights) l.Visible = false;
            return;
        }
        float bs = ChunkMesher.BlockSize;
        var focus = _rig.GlobalPosition;
        _doors.Clear();
        foreach (var b in _sim.Buildings.All)
        {
            if (b.State != BuildingState.Active || !Lit(b.Def) || !_fog.Shows(b)) continue;
            // By the door: the middle of the front (+Z) wall, a little above head height.
            var at = new Vector3(b.X + b.Def.SizeX / 2f, b.Y + Mathf.Min(2.3f, b.Def.WallHeight - 0.3f), b.MaxZ + 1.25f) * bs;
            // The light reaches about as far as the ground it lights in the game (see Conditions).
            float reach = (b.Def.Light + Mathf.Max(b.Def.SizeX, b.Def.SizeZ) / 2 + 1) * bs * 1.2f;
            _doors.Add((at.DistanceSquaredTo(focus), at, reach));
        }
        _doors.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        if (mm.InstanceCount != _doors.Count) mm.InstanceCount = _doors.Count;
        for (int i = 0; i < _doors.Count; i++) mm.SetInstanceTransform(i, new Transform3D(Basis.Identity, _doors[i].At));
        // Brighter than white at night, so the glow picks the lanterns out; dull by day.
        _lanternMaterial.AlbedoColor = new Color(1f, 0.75f, 0.4f) * (0.5f + 3.5f * glow);

        float range = LanternLightRange * LanternLightRange;
        for (int i = 0; i < _lights.Count; i++)
        {
            var l = _lights[i];
            l.Visible = i < _doors.Count && _doors[i].Distance < range;
            if (!l.Visible) continue;
            l.Position = _doors[i].At + new Vector3(0, 0, 0.3f);
            l.LightEnergy = 1.6f * glow;
            l.OmniRange = _doors[i].Range;
        }
    }

    /// <summary>
    /// Buildings with a light in the data have a lantern at the door (torches and campfires have their own
    /// flames, see <see cref="Ambience"/>).
    /// </summary>
    private static bool Lit(BuildingDef def) => def.Light > 0 && def.Kind != BuildingKind.Light;
}
