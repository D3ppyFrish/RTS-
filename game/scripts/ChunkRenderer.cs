using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.World;

namespace Rts.Game;

/// <summary>
/// Owns one MeshInstance3D per non-empty chunk. Each chunk is drawn as finely as its distance from the
/// camera calls for (<see cref="ChunkLod"/>): with <see cref="UseFineTerrain"/> the nearest ground in half-size
/// blocks, then whole blocks, then trees and buildings in bigger voxels, and the horizon in plain blocks.
/// A chunk whose blocks changed, or that the camera has come nearer to or gone further from, is copied (with
/// a one-block border) and meshed on the thread pool, and uploaded back on the main thread within a
/// per-frame time budget; the mesh it had stays on screen until then. Godot frustum-culls each
/// chunk by its bounding box, so only chunks in the camera view are drawn.
/// </summary>
public partial class ChunkRenderer : Node3D
{
    /// <summary>Draw far chunks more simply. Off: every chunk as finely as the nearest.</summary>
    public static bool Lod = true;

    /// <summary>Draw the nearest ground in half-size blocks (a prototype; see <see cref="FineTerrain"/>).</summary>
    public static bool UseFineTerrain;

    /// <summary>
    /// How far from the camera (metres, to the nearest point of the chunk) each level of detail reaches:
    /// <see cref="ChunkLod.Fine"/>, <see cref="ChunkLod.Near"/> and <see cref="ChunkLod.Far"/>; beyond is
    /// <see cref="ChunkLod.Horizon"/>.
    /// </summary>
    public static readonly float[] Reach = { 90f, 100f, 250f };

    /// <summary>A chunk keeps the level it has until it is this much past the line, so it does not flicker on it.</summary>
    private const float Slack = 1.08f;

    private const int Levels = 4;

    /// <summary>Main-thread milliseconds per frame spent uploading finished meshes.</summary>
    public double UploadBudgetMs { get; set; } = 3.0;

    /// <summary>Most chunks copied and sent to workers per frame.</summary>
    public int MaxDispatchPerFrame { get; set; } = 64;

    public int ChunkNodeCount { get; private set; }
    public int PendingCount => _stale + _inFlight;
    public long RebuildsCompleted { get; private set; }

    /// <summary>Main-thread milliseconds the last <see cref="_Process"/> took.</summary>
    public double LastProcessMs { get; private set; }

    /// <summary>Milliseconds making the half-size blocks took (0 without them), and the bytes they are stored in.</summary>
    public double FineBuildMs { get; private set; }
    public long FineBytes => _fine?.StorageBytes ?? 0;

    /// <summary>The world in half-size blocks, if it is drawn (see <see cref="UseFineTerrain"/>).</summary>
    public VoxelWorld? Fine => _fine;

    private const int PaddedLength = VoxelWorld.PaddedSize * VoxelWorld.PaddedSize * VoxelWorld.PaddedSize;

    private VoxelWorld _world = null!;
    private VoxelWorld? _fine;
    private TerrainMaterials _materials = null!;
    private Func<Vector3> _eye = () => Vector3.Zero;
    private readonly Dictionary<(int, int, int), Chunk> _chunks = new();
    private readonly List<(float Distance, (int, int, int) Key, ChunkLod Lod)> _wanted = new();
    private readonly ConcurrentQueue<Result> _results = new();
    private readonly ConcurrentBag<BlockType[]> _bufferPool = new();
    private readonly ConcurrentBag<BlockType[]> _halfPool = new();
    private int _inFlight, _stale;

    private static ChunkRenderer? _current;

    /// <summary>One chunk: the mesh on screen, and the meshes kept for the levels of detail next to it.</summary>
    private sealed class Chunk
    {
        public MeshInstance3D? Node;
        public Vector3 Centre;
        /// <summary>Goes up whenever a block of the chunk (or one beside it) changes.</summary>
        public int Content = 1;
        /// <summary>The level on screen, or -1 if nothing has been built yet.</summary>
        public int Shown = -1;
        public readonly ArrayMesh?[] Meshes = new ArrayMesh?[Levels];
        /// <summary>The <see cref="Content"/> each level's mesh was built from (0: not built), and is being built from.</summary>
        public readonly int[] Built = new int[Levels];
        public readonly int[] Sent = new int[Levels];
        public readonly int[] Triangles = new int[Levels];
        public readonly long[] Bytes = new long[Levels];
    }

    private readonly record struct Result((int, int, int) Key, int Content, ChunkLod Lod, ChunkMeshData Data);

    /// <param name="eye">Where the camera is (world metres): what each chunk's level of detail is measured from.</param>
    public void Init(VoxelWorld world, TerrainMaterials materials, Func<Vector3> eye)
    {
        _world = world;
        _materials = materials;
        _eye = eye;
        if (UseFineTerrain)
        {
            var sw = Stopwatch.StartNew();
            _fine = FineTerrain.Build(world);
            FineBuildMs = sw.Elapsed.TotalMilliseconds;
        }
        _world.BlockChanged += OnBlockChanged;
        _current = this;
    }

    /// <summary>Meshes every chunk in parallel and blocks until all are on screen. Returns milliseconds taken.</summary>
    public double LoadAll()
    {
        var sw = Stopwatch.StartNew();
        var eye = _eye();
        var jobs = new List<((int, int, int) Key, Chunk Chunk, ChunkLod Lod)>();
        for (int cy = 0; cy < _world.ChunksY; cy++)
        for (int cz = 0; cz < _world.ChunksZ; cz++)
        for (int cx = 0; cx < _world.ChunksX; cx++)
        {
            if (!_world.IsChunkAllocated(cx, cy, cz)) continue;
            var chunk = ChunkAt((cx, cy, cz));
            jobs.Add(((cx, cy, cz), chunk, Want(chunk, eye)));
        }
        var data = new ChunkMeshData[jobs.Count];
        Parallel.For(0, jobs.Count, i => data[i] = Build(jobs[i].Key, jobs[i].Lod));
        for (int i = 0; i < jobs.Count; i++) Store(jobs[i].Chunk, jobs[i].Key, jobs[i].Lod, data[i]);
        return sw.Elapsed.TotalMilliseconds;
    }

    private void OnBlockChanged(int x, int y, int z)
    {
        if (_fine != null) FineTerrain.Mirror(_world, _fine, x, y, z);
        MarkBlockDirty(x, y, z);
        // A block set down fills in the step under it, which may be in the chunk below.
        if (_fine != null && y > 0) MarkBlockDirty(x, y - 1, z);
    }

    /// <summary>Marks the chunk holding a block dirty, plus any neighbour whose border faces it affects.</summary>
    public void MarkBlockDirty(int x, int y, int z)
    {
        const int s = VoxelWorld.ChunkSize;
        int cx = x / s, cy = y / s, cz = z / s;
        int lx = x % s, ly = y % s, lz = z % s;
        MarkDirty(cx, cy, cz);
        if (lx == 0) MarkDirty(cx - 1, cy, cz);
        if (lx == s - 1) MarkDirty(cx + 1, cy, cz);
        if (ly == 0) MarkDirty(cx, cy - 1, cz);
        if (ly == s - 1) MarkDirty(cx, cy + 1, cz);
        if (lz == 0) MarkDirty(cx, cy, cz - 1);
        if (lz == s - 1) MarkDirty(cx, cy, cz + 1);
    }

    public void MarkDirty(int cx, int cy, int cz)
    {
        if (cx < 0 || cy < 0 || cz < 0 || cx >= _world.ChunksX || cy >= _world.ChunksY || cz >= _world.ChunksZ) return;
        ChunkAt((cx, cy, cz)).Content++;
    }

    private Chunk ChunkAt((int, int, int) key)
    {
        if (_chunks.TryGetValue(key, out var chunk)) return chunk;
        float size = VoxelWorld.ChunkSize * ChunkMesher.BlockSize;
        chunk = new Chunk { Centre = (new Vector3(key.Item1, key.Item2, key.Item3) + Vector3.One * 0.5f) * size };
        _chunks[key] = chunk;
        return chunk;
    }

    /// <summary>The level of detail a chunk should have with the camera at <paramref name="eye"/>.</summary>
    private ChunkLod Want(Chunk chunk, Vector3 eye)
    {
        var finest = _fine != null ? ChunkLod.Fine : ChunkLod.Near;
        if (!Lod) return finest;
        float half = VoxelWorld.ChunkSize * ChunkMesher.BlockSize * 0.5f;
        var d = (eye - chunk.Centre).Abs() - Vector3.One * half;
        float distance = new Vector3(Mathf.Max(d.X, 0), Mathf.Max(d.Y, 0), Mathf.Max(d.Z, 0)).Length();
        int strict = Level(distance, 1f), slack = Level(distance, Slack);
        int want = chunk.Shown < 0 || chunk.Shown > strict ? strict : chunk.Shown < slack ? slack : chunk.Shown;
        return (ChunkLod)Math.Max(want, (int)finest);
    }

    private static int Level(float distance, float slack)
    {
        for (int i = 0; i < Reach.Length; i++)
            if (distance < Reach[i] * slack) return i;
        return Reach.Length;
    }

    public override void _Process(double delta)
    {
        long start = Stopwatch.GetTimestamp();
        Dispatch();
        Upload();
        LastProcessMs = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    }

    private void Dispatch()
    {
        var eye = _eye();
        _wanted.Clear();
        int stale = 0;
        foreach (var (key, chunk) in _chunks)
        {
            var want = Want(chunk, eye);
            int w = (int)want;
            bool shownStale = chunk.Shown < 0 || chunk.Built[chunk.Shown] != chunk.Content;
            if (shownStale) stale++;
            if (chunk.Built[w] == chunk.Content)
            {
                if (chunk.Shown != w) Show(chunk, key, w);
                continue;
            }
            if (chunk.Sent[w] == chunk.Content) continue;
            // Chunks whose blocks changed go first, then the nearest.
            _wanted.Add((shownStale ? -1f : eye.DistanceSquaredTo(chunk.Centre), key, want));
        }
        _stale = stale;
        if (_wanted.Count == 0) return;
        if (_wanted.Count > MaxDispatchPerFrame) _wanted.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        int count = Math.Min(_wanted.Count, MaxDispatchPerFrame);
        for (int i = 0; i < count; i++)
        {
            var (_, key, lod) = _wanted[i];
            var chunk = _chunks[key];
            int content = chunk.Content;
            chunk.Sent[(int)lod] = content;
            System.Threading.Interlocked.Increment(ref _inFlight);
            // The worker copies the blocks itself, while the simulation may be changing them: any change to
            // this chunk raises its Content, so a mesh made from a torn copy is thrown away when it comes back.
            Task.Run(() =>
            {
                var data = Build(key, lod);
                _results.Enqueue(new Result(key, content, lod, data));
                System.Threading.Interlocked.Decrement(ref _inFlight);
            });
        }
    }

    /// <summary>Copies what a chunk is meshed from at a level of detail.</summary>
    private (BlockType[] Padded, BlockType[]? Half) Copy((int, int, int) key, ChunkLod lod)
    {
        var (cx, cy, cz) = key;
        var padded = RentBuffer();
        _world.CopyChunkPadded(cx, cy, cz, padded);
        if (_fine == null) return (padded, null);
        // Where the ground beside (or, for the finest level, in) the chunk is drawn half a block lower, the
        // whole blocks give way: sides against it are drawn and buildings beside it are not hidden by it.
        var open = RentBuffer();
        FineTerrain.Open(_fine, padded, cx, cy, cz, open, borderOnly: lod != ChunkLod.Fine);
        _bufferPool.Add(padded);
        if (lod != ChunkLod.Fine) return (open, null);
        var half = _halfPool.TryTake(out var b) ? b : new BlockType[ChunkMesher.HalfPaddedLength];
        _fine.CopyBoxPadded(cx * ChunkMesher.HalfChunk, cy * ChunkMesher.HalfChunk, cz * ChunkMesher.HalfChunk, ChunkMesher.HalfChunk, half);
        return (open, half);
    }

    private ChunkMeshData Build((int, int, int) key, ChunkLod lod)
    {
        var (padded, half) = Copy(key, lod);
        var data = ChunkMesher.BuildData(padded, key.Item1, key.Item2, key.Item3, lod, half);
        _bufferPool.Add(padded);
        if (half != null) _halfPool.Add(half);
        return data;
    }

    private void Upload()
    {
        long start = Stopwatch.GetTimestamp();
        double budgetTicks = UploadBudgetMs * Stopwatch.Frequency / 1000.0;
        var eye = _eye();
        while (Stopwatch.GetTimestamp() - start < budgetTicks && _results.TryDequeue(out var r))
        {
            var chunk = _chunks[r.Key];
            int l = (int)r.Lod;
            if (chunk.Sent[l] == r.Content) chunk.Sent[l] = 0;
            // The chunk has changed since this was sent off; a newer copy is on its way.
            if (chunk.Content != r.Content) continue;
            Store(chunk, r.Key, r.Lod, r.Data);
            RebuildsCompleted++;
            // On screen if it is the level wanted now, or if what is on screen is out of date.
            if ((int)Want(chunk, eye) == l || chunk.Shown < 0 || chunk.Built[chunk.Shown] != chunk.Content) Show(chunk, r.Key, l);
        }
    }

    /// <summary>
    /// Keeps a level's mesh for a chunk, shows it if the chunk shows nothing yet, and drops the meshes of
    /// other levels made before the chunk last changed (but for the one on screen).
    /// </summary>
    private void Store(Chunk chunk, (int, int, int) key, ChunkLod lod, ChunkMeshData data)
    {
        int l = (int)lod;
        chunk.Meshes[l] = ChunkMesher.ToMesh(data, _materials.Opaque, _materials.Water);
        chunk.Built[l] = chunk.Content;
        chunk.Triangles[l] = data.Triangles;
        chunk.Bytes[l] = data.Bytes;
        for (int o = 0; o < Levels; o++)
        {
            if (o == chunk.Shown || chunk.Built[o] == chunk.Content) continue;
            chunk.Meshes[o] = null;
            chunk.Built[o] = 0;
        }
        if (chunk.Shown < 0) Show(chunk, key, l);
    }

    /// <summary>Puts a chunk's mesh for a level on screen, and drops the meshes of levels not next to it.</summary>
    private void Show(Chunk chunk, (int, int, int) key, int level)
    {
        var mesh = chunk.Meshes[level];
        if (mesh == null)
        {
            if (chunk.Node != null) { chunk.Node.QueueFree(); chunk.Node = null; ChunkNodeCount--; }
        }
        else
        {
            if (chunk.Node == null)
            {
                chunk.Node = new MeshInstance3D
                {
                    Position = new Vector3(key.Item1, key.Item2, key.Item3) * VoxelWorld.ChunkSize * ChunkMesher.BlockSize,
                };
                AddChild(chunk.Node);
                ChunkNodeCount++;
            }
            chunk.Node.Mesh = mesh;
        }
        chunk.Shown = level;
        for (int l = 0; l < Levels; l++)
        {
            if (Math.Abs(l - level) <= 1) continue;
            chunk.Meshes[l] = null;
            chunk.Built[l] = 0;
        }
    }

    /// <summary>What is on screen and kept, for the benchmark: chunks and triangles at each level, and mesh memory.</summary>
    public string Describe()
    {
        var chunks = new int[Levels];
        var triangles = new long[Levels];
        long shownBytes = 0, keptBytes = 0;
        foreach (var chunk in _chunks.Values)
        {
            for (int l = 0; l < Levels; l++)
                if (chunk.Built[l] != 0 && chunk.Meshes[l] != null) keptBytes += chunk.Bytes[l];
            if (chunk.Shown < 0 || chunk.Meshes[chunk.Shown] == null) continue;
            chunks[chunk.Shown]++;
            triangles[chunk.Shown] += chunk.Triangles[chunk.Shown];
            shownBytes += chunk.Bytes[chunk.Shown];
        }
        return $"lod={(Lod ? "on" : "off")} terrain={(_fine != null ? "fine" : "blocks")} lod_chunks={string.Join("/", chunks)} " +
               $"lod_tris={string.Join("/", triangles)} mesh_mb={shownBytes / 1e6:F0} mesh_kept_mb={keptBytes / 1e6:F0} " +
               $"fine_world_mb={FineBytes / 1e6:F0} fine_build_ms={FineBuildMs:F0}";
    }

    /// <summary>
    /// How far (blocks, 0 or -0.5) the ground drawn under whole-block position (x, y, z) lies below where the
    /// rules have it: half a block where the nearest ground is drawn in half-size blocks and the step there
    /// has lost its top half. What stands on the ground is drawn that much lower.
    /// </summary>
    public static float GroundDrop(float x, int y, float z)
    {
        if (_current is not { _fine: { } fine } r || y <= 0) return 0f;
        const int s = VoxelWorld.ChunkSize;
        int bx = Mathf.FloorToInt(x), bz = Mathf.FloorToInt(z);
        if (!r._chunks.TryGetValue((bx / s, (y - 1) / s, bz / s), out var chunk) || chunk.Shown != (int)ChunkLod.Fine) return 0f;
        return FineTerrain.IsLowered(fine, Mathf.FloorToInt(x * 2), y - 1, Mathf.FloorToInt(z * 2)) ? -0.5f : 0f;
    }

    private BlockType[] RentBuffer() => _bufferPool.TryTake(out var b) ? b : new BlockType[PaddedLength];

    public override void _ExitTree()
    {
        if (_world != null) _world.BlockChanged -= OnBlockChanged;
        if (_current == this) _current = null;
    }
}
