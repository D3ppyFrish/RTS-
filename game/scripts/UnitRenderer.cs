using System;
using System.Collections.Generic;
using Godot;
using Rts.Sim;
using Rts.Sim.Economy;
using Rts.Sim.Meshing;
using Rts.Sim.Units;
using Rts.Game.Detail;

namespace Rts.Game;

/// <summary>
/// Draws every unit with one MultiMesh per model, so thousands of units cost a handful of draw calls.
/// A faction with its own version of a model (see <see cref="UnitModels.ForFaction"/>) has its units drawn
/// with it, so Orc spearmen look like orcs.
/// Positions are interpolated between the previous and current simulation tick for smooth motion;
/// units turn to face the way they walk. Workers carrying goods have a load on their back in the goods'
/// colour. Monster lairs have a den at their middle (see <see cref="MonsterSystem"/>), and city-state towns a
/// banner at their market in their ally's colour (see <see cref="CityStateSystem"/>). Under fog of war
/// (<see cref="FogOfWar"/>) other players' units show only where you can see, and dens and banners once
/// you have seen where they stand.
/// </summary>
public partial class UnitRenderer : Node3D
{
    /// <summary>
    /// How much lower (blocks, negative) unit i is drawn than it stands: on the deck of a wall, gate or tower,
    /// which is drawn sunk between its battlements (see <see cref="StructureDetail.DeckSink"/>), its feet are
    /// on the deck. 0 anywhere else.
    /// </summary>
    /// <summary>How far (blocks) a lurking monster lying in the water sinks below where it stands: to its shoulders, feet already in the water.</summary>
    private const float SubmergedSink = 0.7f;
    /// <summary>How far a unit hiding as it stands still (Clan Kage) crouches.</summary>
    private const float HidingCrouch = 0.2f;
    private float[] _sinkById = new float[256];

    public static float DeckOffset(Simulation sim, int i) =>
        sim.Economy.WallUnder(i) is { Def.Walkway: true } ? -StructureDetail.DeckSink / (float)StructureDetail.Size : 0f;

    // 12 transform + 4 colour (always white) + 4 custom data (team colour). The colour slot is unused but
    // switched on: the Compatibility renderer reads custom data wrongly when instance colours are off.
    private const int FloatsPerInstance = 20;

    /// <summary>Each player's colour; the last is the wild monsters' (<see cref="Simulation.WildPlayer"/>).</summary>
    public static readonly Color[] TeamColors =
    {
        new(0.2f, 0.45f, 0.95f), new(0.9f, 0.2f, 0.15f), new(0.25f, 0.75f, 0.3f), new(0.95f, 0.8f, 0.2f),
        new(0.6f, 0.3f, 0.8f), new(0.95f, 0.55f, 0.15f), new(0.2f, 0.8f, 0.8f), new(0.5f, 0.42f, 0.3f),
    };

    private sealed class Batch
    {
        public required MultiMesh Mesh;
        public float[] Buffer = Array.Empty<float>();
        public int Count;
    }

    /// <summary>
    /// Models drawn where they stand that are not units of the rules (--golems=N, --showcase): scenery for looking
    /// at the detail and measuring what it costs. Positions in metres.
    /// </summary>
    public readonly List<(string Model, Vector3 At, float Yaw, Color Team)> Showpieces = new();

    /// <summary>Units drawn detailed last frame in their finest voxels.</summary>
    public int FineCount => _detail.DrawnFine;

    private Simulation _sim = null!;
    private FogOfWar _fog = null!;
    private readonly List<(MeshInstance3D Node, Lair Lair)> _dens = new();
    private readonly Dictionary<string, Batch> _batches = new();
    /// <summary>The batch each player's units of each type are drawn in: [owner][def].</summary>
    private Batch[][] _batchByOwnerDef = Array.Empty<Batch[]>();
    private Batch _loads = null!;
    private Batch _staff = null!;
    /// <summary>Each kind of totem's carved bands and head, by the totem building's id (models "&lt;id&gt;_band" and "&lt;id&gt;_head").</summary>
    private readonly Dictionary<string, (Batch Bands, Batch Head)> _totems = new();
    /// <summary>A totem's paint while nobody holds its town.</summary>
    private static readonly Color Neutral = new(0.86f, 0.84f, 0.78f);
    private float[] _yawById = new float[1024];
    // Detailed units near the camera (see DetailedUnits): the model each player's units of each type use, and
    // per unit id the walk cycle's phase and how much of a stride it is taking.
    private DetailedUnits _detail = null!;
    private string[][] _modelByOwnerDef = Array.Empty<string[]>();
    private float[] _phaseById = new float[1024];
    private float[] _strideById = new float[1024];
    private float _time;

    /// <summary>Average milliseconds UpdateInstances has taken (all units, plain and detailed) since the first frame.</summary>
    public double AverageUpdateMs => _updates == 0 ? 0 : _updateMs / _updates;
    private double _updateMs;
    private int _updates;

    /// <summary>Of <see cref="AverageUpdateMs"/>, the average spent animating detailed units and uploading them.</summary>
    public string DetailSplit => _detail.Frames == 0 ? "-" : $"{_detail.PlaceMs / _detail.Frames:F2}/{_detail.UploadMs / _detail.Frames:F2}";

    /// <summary>Units drawn detailed last frame.</summary>
    public int DetailedCount => _detail.Drawn;
    private ShaderMaterial _material = null!;

    /// <summary>
    /// The town's totem carved over its pole (a goblin's wooden pole, an ogre's stone idol): a band for each
    /// block of the pole still standing (from the plinth up), the top one a head, all painted in the holder's
    /// colour. Nothing while it is down.
    /// </summary>
    private void WriteTotem(CityState town, float bs)
    {
        if (_sim.Cities.Totem(town) is not { } totem) return;
        WriteTotem(totem, town.Holder >= 0 ? TeamColors[town.Holder & 7] : Neutral, bs);
    }

    /// <summary>The totem carved over the pole of totem building <paramref name="totem"/>, painted <paramref name="colour"/>.</summary>
    private void WriteTotem(Building totem, Color colour, float bs)
    {
        var def = totem.Def;
        int x = def.SizeX / 2 + totem.X, z = def.SizeZ / 2 + totem.Z;
        int top = 0;
        while (top + 1 < def.Height && _sim.World.Get(x, totem.Y + top + 1, z) == def.Wall) top++;
        if (top == 0) return;
        float px = (x + 0.5f) * bs, pz = (z + 0.5f) * bs;
        var (bands, head) = _totems[def.Id];
        for (int y = 1; y < top; y++) Write(bands, 1, 0, px, (totem.Y + y) * bs, pz, colour);
        Write(head, 1, 0, px, (totem.Y + top) * bs, pz, colour);
    }

    public static readonly string ShaderCode = @"
shader_type spatial;
render_mode cull_back;
uniform float slice_y = 100000.0;
varying vec3 v_team;
varying vec4 v_col;
varying float v_world_y;
// A hive unit molting or soft after it is drawn paler: its new shell not yet hardened (custom alpha 2 + paleness).
varying float v_pale;
// Colours are picked as on screen (sRGB) and turned linear for lighting, like the terrain's.
vec3 linear_of(vec3 c) { return mix(c, c * (c * (c * 0.305306011 + 0.682171111) + 0.012522878), 0.85); }
void vertex() {
    v_team = INSTANCE_CUSTOM.rgb;
    v_col = COLOR;
    v_pale = INSTANCE_CUSTOM.a > 1.5 ? clamp(INSTANCE_CUSTOM.a - 2.0, 0.0, 1.0) : 0.0;
    v_world_y = (MODEL_MATRIX * vec4(VERTEX, 1.0)).y;
}
void fragment() {
    if (v_world_y > slice_y) discard;
    vec3 albedo = v_col.a < 0.5 ? v_team * v_col.r : v_col.rgb;
    ALBEDO = linear_of(mix(albedo, vec3(0.93, 0.86, 0.74), v_pale));
    ROUGHNESS = 0.9;
}
";

    public ShaderMaterial Material => _material;
    /// <summary>Hides detailed units above <paramref name="y"/> metres, as the height slice does.</summary>
    public void SetDetailSlice(float y) => _detail.SetSlice(y);

    /// <summary>F7 turns detailed units on and off, to compare the looks and the frame time.</summary>
    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F7 })
        {
            DetailedUnits.Enabled = !DetailedUnits.Enabled;
            GD.Print($"Detailed units {(DetailedUnits.Enabled ? "on" : "off")}");
        }
    }

    /// <summary>The colour of a load of each good.</summary>
    public static Color LoadColor(Good g) => g switch
    {
        Good.Food => new Color(0.85f, 0.2f, 0.3f),
        Good.Wood => new Color(0.6f, 0.4f, 0.2f),
        Good.Stone => new Color(0.62f, 0.62f, 0.64f),
        Good.IronOre => new Color(0.72f, 0.42f, 0.28f),
        Good.Coal => new Color(0.13f, 0.13f, 0.15f),
        _ => new Color(0.78f, 0.82f, 0.9f),
    };

    public void Init(Simulation sim, FogOfWar fog)
    {
        _sim = sim;
        _fog = fog;
        _material = new ShaderMaterial { Shader = new Shader { Code = ShaderCode } };
        var defs = sim.Content.Units;
        var worldBox = new Aabb(new Vector3(-8, -8, -8),
            new Vector3(sim.World.SizeX + 16, sim.World.SizeY + 16, sim.World.SizeZ + 16) * ChunkMesher.BlockSize);
        _batchByOwnerDef = new Batch[Simulation.WildPlayer + 1][];
        _modelByOwnerDef = new string[Simulation.WildPlayer + 1][];
        _detail = new DetailedUnits();
        AddChild(_detail);
        _detail.Init(worldBox);
        for (int owner = 0; owner <= Simulation.WildPlayer; owner++)
        {
            string faction = sim.Economy.Faction(owner).Id;
            var row = _batchByOwnerDef[owner] = new Batch[defs.Count];
            var models = _modelByOwnerDef[owner] = new string[defs.Count];
            for (int d = 0; d < defs.Count; d++)
            {
                string model = models[d] = UnitModels.ForFaction(defs[d].Model, faction);
                if (!_batches.TryGetValue(model, out var batch))
                {
                    var mm = new MultiMesh
                    {
                        TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                        UseColors = true,
                        UseCustomData = true,
                        Mesh = UnitModels.Build(model),
                    };
                    var node = new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = _material, CustomAabb = worldBox };
                    AddChild(node);
                    batch = new Batch { Mesh = mm };
                    _batches[model] = batch;
                }
                row[d] = batch;
            }
        }
        var loads = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            UseCustomData = true,
            Mesh = UnitModels.Build("load"),
        };
        AddChild(new MultiMeshInstance3D { Multimesh = loads, MaterialOverride = _material, CustomAabb = worldBox });
        _loads = new Batch { Mesh = loads };
        _batches["load"] = _loads;
        // City-state totems: a carved band per block of the pole, and a head on the top one.
        Batch Totem(string model)
        {
            var mesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                UseCustomData = true,
                Mesh = UnitModels.Build(model),
            };
            AddChild(new MultiMeshInstance3D { Multimesh = mesh, MaterialOverride = _material, CustomAabb = worldBox });
            var batch = new Batch { Mesh = mesh };
            _batches[model] = batch;
            return batch;
        }
        _staff = Totem("dusk_staff_bearer");
        foreach (var city in sim.Content.CityStates)
            if (city.Totem is { } t && !_totems.ContainsKey(t.Id)) _totems[t.Id] = (Totem(t.Id + "_band"), Totem(t.Id + "_head"));
        // The wild lairs' totems (the giant spiders' at the bottom of their hill caves).
        foreach (var monster in sim.Content.WildMonsters)
            if (monster.Lair?.Totem is { } t && !_totems.ContainsKey(t.Id)) _totems[t.Id] = (Totem(t.Id + "_band"), Totem(t.Id + "_head"));

        // The dens of the lairs, which stay where they are until a player uses one.
        var den = UnitModels.Build("den");
        float bs = ChunkMesher.BlockSize;
        foreach (var lair in sim.Monsters.Lairs)
        {
            if (!lair.Monster.Lair!.Den) continue; // no den: the warden's lair is the dusk tree itself, and a monster no faction has a prize for leaves none
            var node = new MeshInstance3D { Mesh = den, MaterialOverride = _material, Position = new Vector3(lair.X + 0.5f, lair.Y, lair.Z + 0.5f) * bs };
            AddChild(node);
            _dens.Add((node, lair));
        }
    }

    /// <summary>Updates every instance. <paramref name="alpha"/> is how far we are between the last tick and the next (0..1).</summary>
    public void UpdateInstances(float alpha, float delta)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        var u = _sim.Units;
        foreach (var b in _batches.Values) b.Count = 0;
        for (int i = 0; i < u.Count; i++)
        {
            BatchOf(i).Count++;
            if (u.CarryAmount[i] > 0) _loads.Count++;
        }
        foreach (var piece in Showpieces)
            if (_batches.TryGetValue(piece.Model, out var plain)) plain.Count++;
        foreach (var town in _sim.Cities.Towns)
        {
            if (town.Def.Totem is not { } t) continue;
            var (bands, head) = _totems[t.Id];
            head.Count++;
            bands.Count += t.Height;
        }
        foreach (var lair in _sim.Monsters.Lairs)
        {
            if (lair.Monster.Lair!.Totem is not { } t || !_totems.TryGetValue(t.Id, out var batches)) continue;
            batches.Head.Count++;
            batches.Bands.Count += t.Height;
        }
        foreach (var b in _batches.Values)
        {
            int needed = Math.Max(16, b.Count);
            if (b.Mesh.InstanceCount < needed)
            {
                int cap = (int)Math.Min(int.MaxValue / 2, 1L << (int)Math.Ceiling(Math.Log2(needed)));
                b.Mesh.InstanceCount = cap;
                b.Buffer = new float[cap * FloatsPerInstance];
            }
            b.Count = 0;
        }

        float bs = ChunkMesher.BlockSize;
        float turn = Math.Min(1f, delta * 12f);
        _time += delta;
        _detail.BeginFrame();
        bool detailed = DetailedUnits.Enabled;
        var camera = GetViewport()?.GetCamera3D();
        var eye = camera?.GlobalPosition ?? Vector3.Zero;
        var defs = _sim.Content.Units;
        for (int i = 0; i < u.Count; i++)
        {
            if (!_fog.Shows(i)) continue;
            var b = BatchOf(i);
            float px = u.PrevX[i].ToFloat(), pz = u.PrevZ[i].ToFloat();
            float cx = u.X[i].ToFloat(), cz = u.Z[i].ToFloat();
            float x = Mathf.Lerp(px, cx, alpha), z = Mathf.Lerp(pz, cz, alpha);
            float y = Mathf.Lerp(u.PrevY[i], u.Y[i], alpha) + DeckOffset(_sim, i) + ChunkRenderer.GroundDrop(x, u.Y[i], z);

            int id = u.Id[i];
            if (id >= _yawById.Length) Array.Resize(ref _yawById, Math.Max(id + 1, _yawById.Length * 2));
            // A lurking monster lying in the water sinks to its eyes, and rises as it comes out at someone.
            if (id >= _sinkById.Length) Array.Resize(ref _sinkById, Math.Max(id + 1, _sinkById.Length * 2));
            // A Reptilian lying in the water sinks too; Clan Kage's shinobi and kunoichi hiding crouch low.
            float sinkTo = _sim.Combat.Hiding(i) ? HidingCrouch : _sim.Monsters.Submerged(i) || _sim.Combat.Lurking(i) ? SubmergedSink : 0f;
            _sinkById[id] = Mathf.MoveToward(_sinkById[id], sinkTo, delta * 3f);
            y -= _sinkById[id];
            float dx = cx - px, dz = cz - pz;
            if (u.ShieldWall[i])
            {
                // In a shield wall: it turns to face the way the wall faces, whoever it strikes at.
                _yawById[id] = Mathf.LerpAngle(_yawById[id], Mathf.Atan2(u.WallX[i].ToFloat(), u.WallZ[i].ToFloat()), turn);
            }
            else if (dx * dx + dz * dz > 1e-5f)
            {
                float target = Mathf.Atan2(dx, dz);
                _yawById[id] = Mathf.LerpAngle(_yawById[id], target, turn);
            }
            else if (u.Formation[i] && u.Order[i] == UnitOrder.Idle && u.Target[i] == 0 && (u.FaceX[i] != Fix64.Zero || u.FaceZ[i] != Fix64.Zero))
            {
                // Standing in formation: it turns to face the way the formation faces.
                _yawById[id] = Mathf.LerpAngle(_yawById[id], Mathf.Atan2(u.FaceX[i].ToFloat(), u.FaceZ[i].ToFloat()), turn * 0.5f);
            }
            float yaw = _yawById[id];
            // Molting: it shudders where it stands, its shell splitting, and is drawn pale until the new one hardens.
            float pale = 0f;
            if (u.Molting[i] > 0)
            {
                yaw += Mathf.Sin(_time * 30f + id) * 0.15f;
                y -= 0.08f;
                pale = 0.75f;
            }
            else if (u.Soft[i] > 0) pale = 0.6f;
            // An illusion of yours (the Fey's Glamour) shimmers pale, so you know it from the real thing; to the enemy it
            // looks like any other.
            else if (u.Illusion[i] > 0 && u.Owner[i] == PlayerControl.LocalPlayer)
            {
                pale = 0.45f + 0.2f * (0.5f + 0.5f * Mathf.Sin(_time * 6f + id));
                y += 0.06f * Mathf.Sin(_time * 3f + id);
            }
            float s = Mathf.Sin(yaw), c = Mathf.Cos(yaw);
            var team = TeamColors[u.Owner[i] & 7];

            if (u.CarryAmount[i] > 0) Write(_loads, c, s, x * bs, y * bs, z * bs, LoadColor(u.Carry[i]));
            var at = new Vector3(x * bs, y * bs, z * bs);
            string model = _modelByOwnerDef[u.Owner[i] & 7][u.Def[i]];
            float d2 = at.DistanceSquaredTo(eye);
            if (detailed && camera != null && pale == 0f && !_sim.DuskTree.HasStaff(i) && _detail.Covers(model, d2))
            {
                if (id >= _phaseById.Length)
                {
                    Array.Resize(ref _phaseById, _yawById.Length);
                    Array.Resize(ref _strideById, _yawById.Length);
                }
                // Walk cycle: one stride every 1.6 blocks walked; the stride eases in and out.
                float speed = Mathf.Sqrt(dx * dx + dz * dz) * Simulation.TicksPerSecond;
                _strideById[id] = Mathf.Lerp(_strideById[id], speed > 0.05f ? 1f : 0f, Math.Min(1f, delta * 8f));
                _phaseById[id] = Mathf.PosMod(_phaseById[id] + speed * delta * Mathf.Tau / _detail.StrideBlocks(model), Mathf.Tau);
                var def = defs[u.Def[i]];
                float strike = u.Target[i] != 0 && u.Cooldown[i] > 0 && def.AttackTicks > 0
                    ? 1f - Math.Clamp((u.Cooldown[i] - alpha) / def.AttackTicks, 0f, 1f) : -1f;
                if (_detail.Add(model, at, d2, yaw, team, _phaseById[id], _strideById[id], strike, _time, id))
                    continue;
            }
            Write(b, c, s, x * bs, y * bs, z * bs, team, pale);
        }
        foreach (var town in _sim.Cities.Towns)
            if (_fog.HasSeen(town.X, town.Z)) WriteTotem(town, bs);
        foreach (var lair in _sim.Monsters.Lairs)
            if (_sim.Monsters.TotemOf(lair) is { } totem && _fog.HasSeen(lair.X, lair.Z)) WriteTotem(totem, Neutral, bs);
        // Showpieces (--golems, --showcase): models that are not units of the rules, standing still and breathing;
        // plain where they would be, if they have a plain model.
        for (int k = 0; k < Showpieces.Count; k++)
        {
            var (model, at, yaw, team) = Showpieces[k];
            if (detailed && camera != null && _detail.Add(model, at, at.DistanceSquaredTo(eye), yaw, team, 0, 0, -1, _time, -1 - k)) continue;
            if (_batches.TryGetValue(model, out var plain)) Write(plain, Mathf.Cos(yaw), Mathf.Sin(yaw), at.X, at.Y, at.Z, team);
        }
        foreach (var (node, lair) in _dens) node.Visible = !lair.Used && _fog.HasSeen(lair.X, lair.Z); // a used den is gone

        _detail.EndFrame();
        foreach (var b in _batches.Values)
        {
            RenderingServer.MultimeshSetBuffer(b.Mesh.GetRid(), b.Buffer);
            b.Mesh.VisibleInstanceCount = b.Count;
        }
        _updateMs += (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        _updates++;
    }

    private Batch BatchOf(int i) =>
        _sim.DuskTree.HasStaff(i) ? _staff : _batchByOwnerDef[_sim.Units.Owner[i] & 7][_sim.Units.Def[i]];

    /// <summary>Adds an instance turned by yaw (sine s, cosine c) at (x, y, z) metres, tinted <paramref name="tint"/>.</summary>
    private static void Write(Batch b, float c, float s, float x, float y, float z, Color tint, float pale = 0f)
    {
        int o = b.Count++ * FloatsPerInstance;
        var buf = b.Buffer;
        buf[o + 0] = c; buf[o + 1] = 0; buf[o + 2] = s; buf[o + 3] = x;
        buf[o + 4] = 0; buf[o + 5] = 1; buf[o + 6] = 0; buf[o + 7] = y;
        buf[o + 8] = -s; buf[o + 9] = 0; buf[o + 10] = c; buf[o + 11] = z;
        buf[o + 12] = 1; buf[o + 13] = 1; buf[o + 14] = 1; buf[o + 15] = 1;
        buf[o + 16] = tint.R; buf[o + 17] = tint.G; buf[o + 18] = tint.B; buf[o + 19] = pale > 0f ? 2f + pale : 1f;
    }
}
