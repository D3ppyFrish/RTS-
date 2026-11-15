using System;
using System.Collections.Generic;
using Godot;
using Rts.Sim;
using Rts.Sim.Economy;
using Rts.Sim.World;

namespace Rts.Game;

/// <summary>
/// The life of the world, for the look only (the rules are in <see cref="Conditions"/>): flames on torches
/// and campfires (out in a storm, and smoking), smoke from chimneys and, morning and evening, from the roofs
/// of houses, the glow of lava, fireflies and mist over the swamp, stars at night, and rain that falls around
/// the camera. Trees sway and the ground gets wet through the terrain shader
/// (<see cref="TerrainMaterials.SetWeather"/>). Everything moves on the graphics card from the time and a
/// few numbers per instance, so it costs the main thread little: the lists of flames and smoke are rebuilt a
/// few times a second, and the lights are moved to the nearest sources each frame.
///
/// Flames, smoke and lava lights only show where the player can see (see <see cref="FogOfWar"/>), and the
/// fireflies and the mist only on ground the player has seen.
/// </summary>
public partial class Ambience : Node3D
{
    private const float Bs = ChunkMesher.BlockSize;
    /// <summary>The most flame and lava lights that shine at once, at the sources nearest the camera.</summary>
    private const int MaxFireLights = 12;
    /// <summary>Puffs of smoke rising from each chimney at once.</summary>
    private const int PuffsPerSource = 10;
    private const int RainDrops = 5000;
    private const int Stars = 700;
    private const float StarDistance = 640f;
    /// <summary>Where the wind blows to (x, z).</summary>
    private static readonly Vector2 WindTo = new Vector2(0.8f, 0.45f).Normalized();

    private Simulation _sim = null!;
    private FogOfWar _fog = null!;
    private CameraRig _rig = null!;
    private WorldLighting _lighting = null!;
    private TerrainMaterials _terrain = null!;

    private MultiMeshInstance3D _flames = null!, _smoke = null!, _rain = null!, _stars = null!, _flies = null!;
    private ShaderMaterial _flameMat = null!, _smokeMat = null!, _rainMat = null!, _starMat = null!, _flyMat = null!, _mistMat = null!;
    private readonly List<MeshInstance3D> _mist = new();
    private readonly List<OmniLight3D> _fireLights = new();
    private readonly List<(Vector3 At, float Size, float Seed)> _fires = new();
    private readonly List<(Vector3 At, float Amount, float Seed)> _chimneys = new();
    private readonly List<(float Distance, Vector3 At, float Range, float Seed, bool Lava)> _nearFires = new();
    private readonly List<Vector3> _lava = new();
    private double _rebuild;
    private float _wet;
    private float _flash, _nextFlash = 4f;
    private readonly Random _random = new(1);

    public void Init(Simulation sim, FogOfWar fog, CameraRig rig, WorldLighting lighting, TerrainMaterials terrain)
    {
        (_sim, _fog, _rig, _lighting, _terrain) = (sim, fog, rig, lighting, terrain);

        _flameMat = new ShaderMaterial { Shader = new Shader { Code = FlameCode } };
        _flames = Instances(new QuadMesh { Size = new Vector2(1f, 1f), CenterOffset = new Vector3(0, 0.5f, 0) }, _flameMat);
        _smokeMat = new ShaderMaterial { Shader = new Shader { Code = SmokeCode } };
        _smoke = Instances(new BoxMesh { Size = Vector3.One * 0.3f }, _smokeMat);
        _rainMat = new ShaderMaterial { Shader = new Shader { Code = RainCode } };
        _rain = Instances(new BoxMesh { Size = new Vector3(0.012f, 0.35f, 0.012f) }, _rainMat);
        _starMat = new ShaderMaterial { Shader = new Shader { Code = StarCode } };
        _stars = Instances(new BoxMesh { Size = Vector3.One * 2.2f }, _starMat);
        _flyMat = new ShaderMaterial { Shader = new Shader { Code = FireflyCode } };
        _flies = Instances(new BoxMesh { Size = Vector3.One * 0.08f }, _flyMat);
        _terrain.Track(_flyMat);

        for (int i = 0; i < MaxFireLights; i++)
        {
            var l = new OmniLight3D { LightColor = new Color(1f, 0.55f, 0.22f), OmniAttenuation = 1.3f, ShadowEnabled = false, Visible = false };
            AddChild(l);
            _fireLights.Add(l);
        }
        PlaceRain();
        PlaceStars();
        FindLava();
        PlaceSwamp();
        PlaceDuskMist();
        Rebuild();
    }

    private MultiMeshInstance3D Instances(Mesh mesh, Material material)
    {
        if (mesh is PrimitiveMesh primitive) primitive.Material = material;
        else mesh.SurfaceSetMaterial(0, material);
        var node = new MultiMeshInstance3D
        {
            Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseCustomData = true,
                Mesh = mesh,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            // Everything here moves in the shader, away from where the instances are, so never cull it.
            CustomAabb = new Aabb(new Vector3(-1e4f, -1e4f, -1e4f), new Vector3(2e4f, 2e4f, 2e4f)),
        };
        AddChild(node);
        return node;
    }

    /// <summary>Once a frame, after <see cref="WorldLighting.UpdateFrame"/>.</summary>
    public void UpdateFrame(float delta)
    {
        var look = _lighting.Look;
        var light = _lighting.Light;
        float hour = _lighting.Hour;
        float dark = light.Lanterns; // 0 by day, 1 at night
        _rebuild -= delta;
        if (_rebuild <= 0)
        {
            _rebuild = 0.25;
            Rebuild();
        }

        // Wet ground: soaks up in a few seconds of rain, dries over a minute.
        float wetTarget = look.Rain > 0.15f ? Mathf.Min(1f, look.Rain * 1.4f) : 0f;
        _wet = Mathf.MoveToward(_wet, wetTarget, delta * (wetTarget > _wet ? 0.15f : 0.02f));
        _terrain.SetWeather(look.Wind, _wet);

        var wind = new Vector3(WindTo.X, 0, WindTo.Y);
        foreach (var m in new[] { _smokeMat, _rainMat, _flameMat })
        {
            m.SetShaderParameter("wind_dir", WindTo);
            m.SetShaderParameter("wind", look.Wind);
        }
        _flameMat.SetShaderParameter("glow", 1.1f + 0.9f * dark);
        float bright = Mathf.Clamp(light.SunEnergy * 0.5f + light.AmbientEnergy * 0.6f, 0.15f, 1.2f);
        _smokeMat.SetShaderParameter("tint", new Color(0.78f, 0.78f, 0.8f) * bright);
        _rainMat.SetShaderParameter("amount", look.Rain);
        _rainMat.SetShaderParameter("tint", new Color(0.7f, 0.78f, 0.9f) * Mathf.Clamp(bright + 0.25f, 0.35f, 1.1f));
        _rain.Visible = look.Rain > 0.01f;
        if (_rain.Visible)
        {
            // A box of rain around the camera's focus, scaled up as the camera pulls back.
            float scale = Mathf.Max(1f, _rig.Distance / 45f);
            var at = _rig.GlobalPosition;
            _rain.GlobalPosition = new Vector3(Mathf.Snapped(at.X, 1f), at.Y - 6f * scale, Mathf.Snapped(at.Z, 1f));
            _rain.Scale = Vector3.One * scale;
        }

        // Stars: bright on a clear night, gone behind cloud and by day.
        float starAmount = Mathf.Clamp((dark - 0.3f) / 0.7f, 0f, 1f) * (1f - look.Cloud);
        _stars.Visible = starAmount > 0.01f;
        _starMat.SetShaderParameter("amount", starAmount);
        if (_stars.Visible) _stars.GlobalPosition = _rig.Camera.GlobalPosition;

        // The swamp: fireflies on clear nights, mist at night and in the morning (as in the rules).
        float flies = dark * (1f - look.Rain) * (1f - 0.6f * look.Wind);
        _flyMat.SetShaderParameter("amount", flies);
        _flies.Visible = flies > 0.01f && _flies.Multimesh.InstanceCount > 0;
        float mist = MistAt(_lighting.Cycle.On ? hour : -1f);
        _mistMat?.SetShaderParameter("amount", Mathf.Max(mist, look.Mist * 0.7f));
        _mistMat?.SetShaderParameter("tint", light.Haze.Lightened(0.25f));
        foreach (var m in _mist) m.Visible = mist > 0.01f || look.Mist > 0.01f;

        // The dusk tree: purple mist about its roots and a cold cyan glow, deeper at night, gone once it falls.
        if (_duskMistMat != null)
        {
            bool stands = !_sim.DuskTree.Razed;
            _duskMistMat.SetShaderParameter("amount", 0.75f + 0.5f * dark);
            foreach (var m in _duskMist) m.Visible = stands;
            if (_duskLight != null)
            {
                _duskLight.Visible = stands && dark > 0.05f;
                _duskLight.LightEnergy = 1.6f * dark;
            }
        }

        // Lightning in a storm: a white flash now and then.
        if (look.Rain > 0.85f && look.Cloud > 0.9f)
        {
            _nextFlash -= delta;
            if (_nextFlash <= 0)
            {
                _flash = 1f;
                _nextFlash = 5f + (float)_random.NextDouble() * 12f;
            }
        }
        _flash = Mathf.MoveToward(_flash, 0f, delta * 6f);
        _lighting.Flash = _flash > 0.5f || (_flash > 0.2f && _flash < 0.3f) ? _flash : 0f;

        UpdateFireLights(dark);
    }

    /// <summary>How thick the swamp's mist is at <paramref name="hour"/> (0 to 1): it lies all night and lifts by 9:00.</summary>
    private static float MistAt(float hour)
    {
        if (hour < 0) return 0f;
        float lift = Conditions.MistEnd / 60f;
        float dusk = Conditions.DuskEnd / 60f;
        if (hour < lift - 0.5f || hour >= dusk) return 1f;
        if (hour < lift + 0.5f) return 1f - (hour - (lift - 0.5f));
        if (hour >= dusk - 1f) return hour - (dusk - 1f);
        return 0f;
    }

    // --- Flames and smoke --------------------------------------------------------------------------

    private void Rebuild()
    {
        _fires.Clear();
        _chimneys.Clear();
        bool storm = _sim.Conditions.Weather == Weather.Storm;
        float hour = _lighting.Cycle.TrueHour(_sim.Tick);
        // Cooking fires: smoke from roofs in the morning and the evening.
        bool cooking = hour is >= 6f and < 9.5f or >= 17.5f and < 22f;
        foreach (var b in _sim.Buildings.All)
        {
            if (b.State != BuildingState.Active || !_fog.Shows(b)) continue;
            float seed = (b.Id * 0.618034f) % 1f;
            var def = b.Def;
            if (def.Kind == BuildingKind.Light)
            {
                var top = new Vector3(b.X + 0.5f, b.Y + def.WallHeight, b.Z + 0.5f) * Bs;
                if (storm) _chimneys.Add((top, 0.35f, seed)); // put out: a wisp of smoke
                else _fires.Add((top, def.WallHeight <= 1 ? 0.75f : 0.42f, seed));
            }
            else if (def.Chimney)
                _chimneys.Add((new Vector3(b.X + def.SizeX - 0.5f, b.Y + def.Height, b.Z + 0.5f) * Bs, 1f, seed));
            else if (cooking && def.Kind is BuildingKind.House or BuildingKind.TownHall)
                _chimneys.Add((new Vector3(b.X + def.SizeX / 2f, b.Y + def.Height, b.Z + def.SizeZ / 2f) * Bs, 0.7f, seed));
        }
        var mm = _flames.Multimesh;
        if (mm.InstanceCount != _fires.Count) mm.InstanceCount = _fires.Count;
        for (int i = 0; i < _fires.Count; i++)
        {
            var (at, size, seed) = _fires[i];
            mm.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(Vector3.One * size), at));
            mm.SetInstanceCustomData(i, new Color(seed, size, 0, 0));
        }
        mm = _smoke.Multimesh;
        int puffs = _chimneys.Count * PuffsPerSource;
        if (mm.InstanceCount != puffs) mm.InstanceCount = puffs;
        for (int i = 0; i < puffs; i++)
        {
            var (at, amount, seed) = _chimneys[i / PuffsPerSource];
            int k = i % PuffsPerSource;
            mm.SetInstanceTransform(i, new Transform3D(Basis.Identity, at));
            // Phase, speed, how thick, and a scatter value for this puff.
            mm.SetInstanceCustomData(i, new Color((seed + k / (float)PuffsPerSource) % 1f, 0.12f + 0.03f * seed, amount, (k * 0.37f + seed) % 1f));
        }
    }

    private void UpdateFireLights(float dark)
    {
        var focus = _rig.GlobalPosition;
        _nearFires.Clear();
        if (dark > 0.02f)
        {
            foreach (var (at, size, seed) in _fires)
                _nearFires.Add((at.DistanceSquaredTo(focus), at + new Vector3(0, 0.25f, 0), size > 0.6f ? 6.5f : 5.2f, seed, false));
            foreach (var at in _lava)
            {
                float d = at.DistanceSquaredTo(focus);
                if (d < 120f * 120f && _fog.Sees((int)(at.X / Bs), (int)(at.Z / Bs)))
                    _nearFires.Add((d, at, (Lights.LavaLight + 2) * Bs * 2f, at.X * 0.37f % 1f, true));
            }
            _nearFires.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        }
        float t = (float)Time.GetTicksMsec() / 1000f;
        for (int i = 0; i < _fireLights.Count; i++)
        {
            var l = _fireLights[i];
            l.Visible = i < _nearFires.Count && _nearFires[i].Distance < 100f * 100f;
            if (!l.Visible) continue;
            var f = _nearFires[i];
            l.Position = f.At;
            l.OmniRange = f.Range;
            float flicker = f.Lava
                ? 0.9f + 0.1f * Mathf.Sin(t * 1.3f + f.Seed * 20f)
                : 0.8f + 0.12f * Mathf.Sin(t * 13f + f.Seed * 40f) + 0.08f * Mathf.Sin(t * 7.3f + f.Seed * 9f);
            l.LightColor = f.Lava ? new Color(1f, 0.38f, 0.12f) : new Color(1f, 0.58f, 0.24f);
            l.LightEnergy = (f.Lava ? 1.3f : 1.8f) * dark * flicker;
        }
    }

    /// <summary>Groups the lava on the surface into a light every few columns.</summary>
    private void FindLava()
    {
        var w = _sim.World;
        var cells = new SortedDictionary<(int, int), (Vector3 Sum, int N)>();
        for (int z = 0; z < w.SizeZ; z++)
        for (int x = 0; x < w.SizeX; x++)
            for (int y = w.SizeY - 1; y >= 1; y--)
            {
                var t = w.Get(x, y, z);
                if (t == BlockType.Air) continue;
                if (t == BlockType.Lava)
                {
                    var key = (x / 6, z / 6);
                    var (sum, n) = cells.TryGetValue(key, out var v) ? v : (Vector3.Zero, 0);
                    cells[key] = (sum + new Vector3(x + 0.5f, y + 1.6f, z + 0.5f) * Bs, n + 1);
                }
                break;
            }
        foreach (var (sum, n) in cells.Values) _lava.Add(sum / n);
    }

    // --- Rain and stars ----------------------------------------------------------------------------

    private void PlaceRain()
    {
        var mm = _rain.Multimesh;
        mm.InstanceCount = RainDrops;
        var r = new Random(7);
        for (int i = 0; i < RainDrops; i++)
        {
            var at = new Vector3((float)r.NextDouble() * 70f - 35f, 0, (float)r.NextDouble() * 70f - 35f);
            mm.SetInstanceTransform(i, new Transform3D(Basis.Identity, at));
            mm.SetInstanceCustomData(i, new Color((float)r.NextDouble(), (float)r.NextDouble(), (float)r.NextDouble(), 0));
        }
    }

    private void PlaceStars()
    {
        var mm = _stars.Multimesh;
        mm.InstanceCount = Stars;
        var r = new Random(11);
        for (int i = 0; i < Stars; i++)
        {
            // Spread evenly over the sky above the horizon.
            float y = 0.05f + (float)r.NextDouble() * 0.95f;
            float a = (float)r.NextDouble() * Mathf.Tau;
            float s = Mathf.Sqrt(1f - y * y);
            var dir = new Vector3(Mathf.Cos(a) * s, y, Mathf.Sin(a) * s);
            float size = 0.5f + 0.9f * (float)Math.Pow(r.NextDouble(), 4);
            mm.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(Vector3.One * size), dir * StarDistance));
            mm.SetInstanceCustomData(i, new Color((float)r.NextDouble(), (float)r.NextDouble(), 0, 0));
        }
    }

    // --- The swamp ---------------------------------------------------------------------------------

    private void PlaceSwamp()
    {
        var map = _sim.Map;
        var w = _sim.World;
        int minX = int.MaxValue, minZ = int.MaxValue, maxX = -1, maxZ = -1;
        long ySum = 0, n = 0;
        var columns = new List<(int X, int Z)>();
        for (int z = 0; z < w.SizeZ; z++)
        for (int x = 0; x < w.SizeX; x++)
        {
            if (map.BiomeAt(x, z) != Biome.Swamp) continue;
            columns.Add((x, z));
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minZ = Math.Min(minZ, z); maxZ = Math.Max(maxZ, z);
        }
        if (columns.Count == 0) return;
        var r = new Random(3);
        int count = Math.Min(500, columns.Count / 4);
        var mm = _flies.Multimesh;
        mm.InstanceCount = count;
        for (int i = 0; i < count; i++)
        {
            var (x, z) = columns[r.Next(columns.Count)];
            int g = Rts.Sim.Units.Walk.GroundLevel(w, x, z);
            if (g < 0) g = RegionGenerator.WaterLevel(w.SizeY) + 1;
            ySum += g;
            n++;
            var at = new Vector3(x + (float)r.NextDouble(), g + 0.6f + 2.2f * (float)r.NextDouble(), z + (float)r.NextDouble()) * Bs;
            mm.SetInstanceTransform(i, new Transform3D(Basis.Identity, at));
            mm.SetInstanceCustomData(i, new Color((float)r.NextDouble(), (float)r.NextDouble(), (float)r.NextDouble(), 0));
        }

        // The mist: two sheets over the swamp, shaped by a picture of where the swamp is.
        int sx = maxX - minX + 9, sz = maxZ - minZ + 9;
        var image = Image.CreateEmpty(sx, sz, false, Image.Format.R8);
        foreach (var (x, z) in columns) image.SetPixel(x - minX + 4, z - minZ + 4, new Color(1, 1, 1));
        var mask = ImageTexture.CreateFromImage(image);
        _mistMat = new ShaderMaterial { Shader = new Shader { Code = MistCode } };
        _mistMat.SetShaderParameter("mask", mask);
        _mistMat.SetShaderParameter("mask_origin", new Vector2(minX - 4, minZ - 4) * Bs);
        _mistMat.SetShaderParameter("mask_size", new Vector2(sx, sz) * Bs);
        _terrain.Track(_mistMat);
        float ground = n > 0 ? (float)ySum / n : RegionGenerator.WaterLevel(w.SizeY) + 1;
        for (int k = 0; k < 3; k++)
        {
            var plane = new MeshInstance3D
            {
                Mesh = new PlaneMesh { Size = new Vector2(sx, sz) * Bs, Material = _mistMat },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Position = new Vector3((minX - 4 + sx / 2f) * Bs, (ground + 0.4f + 0.9f * k) * Bs, (minZ - 4 + sz / 2f) * Bs),
                Visible = false,
            };
            AddChild(plane);
            _mist.Add(plane);
        }
    }

    private ShaderMaterial? _duskMistMat;
    private readonly List<MeshInstance3D> _duskMist = new();
    private OmniLight3D? _duskLight;

    private void PlaceDuskMist()
    {
        var tree = _sim.DuskTree;
        if (!tree.Exists) return;
        const int r = 11, size = 2 * r + 1;
        var image = Image.CreateEmpty(size, size, false, Image.Format.R8);
        for (int z = 0; z < size; z++)
        for (int x = 0; x < size; x++)
        {
            float d = Mathf.Sqrt((x - r) * (x - r) + (z - r) * (z - r)) / r;
            image.SetPixel(x, z, new Color(Mathf.Clamp(1.15f - d * 1.1f, 0f, 1f), 0, 0));
        }
        var origin = new Vector2(tree.X - r + 0.5f, tree.Z - r + 0.5f) * Bs;
        _duskMistMat = new ShaderMaterial { Shader = new Shader { Code = MistCode } };
        _duskMistMat.SetShaderParameter("mask", ImageTexture.CreateFromImage(image));
        _duskMistMat.SetShaderParameter("mask_origin", origin);
        _duskMistMat.SetShaderParameter("mask_size", new Vector2(size, size) * Bs);
        _duskMistMat.SetShaderParameter("tint", new Color(0.55f, 0.32f, 0.78f));
        _terrain.Track(_duskMistMat);
        for (int k = 0; k < 3; k++)
        {
            var plane = new MeshInstance3D
            {
                Mesh = new PlaneMesh { Size = new Vector2(size, size) * Bs, Material = _duskMistMat },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Position = new Vector3(tree.X + 0.5f, tree.Y + 0.3f + 0.7f * k, tree.Z + 0.5f) * Bs,
            };
            AddChild(plane);
            _duskMist.Add(plane);
        }
        _duskLight = new OmniLight3D
        {
            LightColor = new Color(0.35f, 0.95f, 1f), OmniRange = 9f * Bs, OmniAttenuation = 1.5f, ShadowEnabled = false,
            Position = new Vector3(tree.X + 0.5f, tree.Y + 4f, tree.Z + 0.5f) * Bs, Visible = false,
        };
        AddChild(_duskLight);
    }

    // --- Shaders -----------------------------------------------------------------------------------

    // A pixel flame on a quad that turns to face the camera about the upright: a flickering tongue, white
    // and yellow at the heart, orange, red at the tips, brighter than white at night so it glows.
    private const string FlameCode = @"
shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled;
uniform float glow = 1.0;
uniform vec2 wind_dir = vec2(1.0, 0.0);
uniform float wind = 0.2;
varying float v_seed;
void vertex() {
    v_seed = INSTANCE_CUSTOM.x;
    vec3 origin = MODEL_MATRIX[3].xyz;
    vec3 cam = INV_VIEW_MATRIX[3].xyz;
    vec2 d = normalize(cam.xz - origin.xz + vec2(0.0001));
    vec3 right = vec3(d.y, 0.0, -d.x);
    // Leans with the wind toward the top.
    vec3 lean = vec3(wind_dir.x, 0.0, wind_dir.y) * wind * 0.35 * VERTEX.y;
    VERTEX = right * VERTEX.x + vec3(0.0, VERTEX.y, 0.0) + lean;
}
void fragment() {
    vec2 grid = vec2(7.0, 11.0);
    vec2 uv = (floor(UV * grid) + 0.5) / grid;
    float h = 1.0 - uv.y;
    float t = TIME * 9.0 + v_seed * 40.0;
    float flick = 0.82 + 0.12 * sin(t) + 0.08 * sin(t * 1.9 + 2.0);
    h /= flick;
    float sway = 0.09 * sin(t * 0.7 + h * 5.0) * h;
    float x = abs(uv.x - 0.5 - sway);
    float w = 0.46 * sqrt(max(0.0, 1.0 - h)) * min(1.0, 0.55 + h * 3.0);
    if (h > 1.0 || x > w) discard;
    vec3 c = x < w * 0.45 && h < 0.5 ? vec3(1.0, 0.95, 0.6) : h < 0.72 ? vec3(1.0, 0.62, 0.15) : vec3(0.95, 0.28, 0.08);
    ALBEDO = c * glow;
}
";

    // Smoke: grey puffs (small cubes, like the blocks of the world) that rise, grow, drift with the wind and
    // fade out, each a little behind the last.
    private const string SmokeCode = @"
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_back, shadows_disabled;
uniform vec3 tint : source_color = vec3(0.6);
uniform vec2 wind_dir = vec2(1.0, 0.0);
uniform float wind = 0.2;
varying float v_alpha;
void vertex() {
    vec4 c = INSTANCE_CUSTOM;
    float ph = fract(TIME * c.y + c.x);
    float rise = ph * (3.6 - 1.8 * wind);
    vec2 drift = wind_dir * (0.25 + 2.8 * wind) * ph * ph
        + vec2(sin(ph * 6.0 + c.w * 30.0), cos(ph * 5.0 + c.w * 11.0)) * 0.12 * ph;
    float s = mix(0.6, 2.6, ph) * mix(0.7, 1.0, c.z);
    VERTEX = VERTEX * s + vec3(drift.x, rise, drift.y);
    v_alpha = smoothstep(0.0, 0.08, ph) * (1.0 - ph) * c.z;
}
void fragment() {
    ALBEDO = tint;
    ALPHA = v_alpha * 0.85;
}
";

    // Rain: thin streaks that fall through a box around the camera and start again at the top, slanting with
    // the wind. How hard it rains (amount) is how many of them fall.
    private const string RainCode = @"
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, shadows_disabled;
uniform float amount = 0.0;
uniform vec3 tint : source_color = vec3(0.7, 0.78, 0.9);
uniform vec2 wind_dir = vec2(1.0, 0.0);
uniform float wind = 0.2;
const float HEIGHT = 26.0;
varying float v_alpha;
void vertex() {
    vec4 c = INSTANCE_CUSTOM;
    if (c.y > amount) {
        VERTEX = vec3(0.0);
    } else {
        float fall = 13.0 + 5.0 * c.z;
        float y = HEIGHT - mod(TIME * fall + c.x * HEIGHT, HEIGHT);
        vec2 slant = wind_dir * wind * 0.45;
        VERTEX.xz += slant * VERTEX.y * 2.0 + slant * y;
        VERTEX.y += y;
    }
    v_alpha = 0.16 + 0.14 * c.z;
}
void fragment() {
    ALBEDO = tint;
    ALPHA = v_alpha;
}
";

    // Stars: small squares on the sky far past the world, twinkling; not hazed by the fog.
    private const string StarCode = @"
shader_type spatial;
render_mode unshaded, fog_disabled, shadows_disabled;
uniform float amount = 0.0;
varying float v_twinkle;
void vertex() {
    vec4 c = INSTANCE_CUSTOM;
    v_twinkle = 0.65 + 0.35 * sin(TIME * (1.0 + 2.5 * c.y) + c.x * 50.0);
}
void fragment() {
    float b = amount * v_twinkle;
    if (b < 0.03) discard;
    ALBEDO = vec3(0.85, 0.9, 1.0) * b * 1.6;
}
";

    // Fireflies: specks of green-gold light that wander in loops and blink, only in sight.
    private const string FireflyCode = @"
shader_type spatial;
render_mode unshaded, shadows_disabled;
uniform float amount = 0.0;
" + TerrainMaterials.FogCode + @"
varying float v_blink;
void vertex() {
    vec4 c = INSTANCE_CUSTOM;
    float t = TIME * 0.5;
    VERTEX += vec3(sin(t * (0.7 + c.z) + c.x * 20.0) * 0.5, sin(t * 1.3 + c.y * 13.0) * 0.25, cos(t * (0.6 + c.y) + c.x * 17.0) * 0.5);
    v_blink = smoothstep(0.55, 1.0, sin(TIME * (1.2 + 1.5 * c.y) + c.x * 40.0) * 0.5 + 0.5);
    v_fog_xz = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xz;
}
void fragment() {
    float b = amount * v_blink * fog_at(v_fog_xz).y;
    if (b < 0.04) discard;
    ALBEDO = vec3(0.7, 0.95, 0.12) * b * 0.9;
}
";

    // Mist: soft drifting sheets over the swamp, shaped by the swamp's mask and gone over ground never seen.
    private const string MistCode = @"
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, shadows_disabled;
uniform float amount = 0.0;
uniform vec3 tint : source_color = vec3(0.8);
uniform sampler2D mask : filter_linear, repeat_disable;
uniform vec2 mask_origin;
uniform vec2 mask_size = vec2(1.0);
" + TerrainMaterials.FogCode + @"
varying vec3 v_world;
float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}
void vertex() {
    v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}
void fragment() {
    vec2 p = v_world.xz;
    float m = texture(mask, (p - mask_origin) / mask_size).r;
    vec2 drift = vec2(TIME * 0.05, TIME * 0.02);
    float n = noise(p * 0.12 + drift + v_world.y) * 0.65 + noise(p * 0.35 - drift * 1.7) * 0.35;
    float a = smoothstep(0.1, 0.9, m) * smoothstep(0.3, 0.8, n) * amount * fog_at(p).x;
    if (a < 0.01) discard;
    ALBEDO = tint;
    ALPHA = a * 0.3;
}
";
}
