using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;

namespace Rts.Game;

/// <summary>
/// Owns one MeshInstance3D per non-empty chunk. Dirty chunks are copied (with a one-block border)
/// on the main thread, meshed on the thread pool, and uploaded back on the main thread within a
/// per-frame time budget. Godot frustum-culls each chunk by its bounding box, so only chunks in
/// the camera view are drawn.
/// </summary>
public partial class ChunkRenderer : Node3D
{
    /// <summary>Main-thread milliseconds per frame spent uploading finished meshes.</summary>
    public double UploadBudgetMs { get; set; } = 3.0;

    /// <summary>Most chunks copied and sent to workers per frame.</summary>
    public int MaxDispatchPerFrame { get; set; } = 64;

    public int ChunkNodeCount => _nodes.Count;
    public int PendingCount => _dirty.Count + _inFlight;
    public long RebuildsCompleted { get; private set; }

    /// <summary>Main-thread milliseconds the last <see cref="_Process"/> took.</summary>
    public double LastProcessMs { get; private set; }

    private const int PaddedLength = VoxelWorld.PaddedSize * VoxelWorld.PaddedSize * VoxelWorld.PaddedSize;

    private VoxelWorld _world = null!;
    private Material _opaque = null!;
    private Material _water = null!;
    private readonly Dictionary<(int, int, int), MeshInstance3D> _nodes = new();
    private readonly Dictionary<(int, int, int), int> _version = new();
    private readonly HashSet<(int, int, int)> _dirty = new();
    private readonly List<(int, int, int)> _dispatchScratch = new();
    private readonly ConcurrentQueue<Result> _results = new();
    private readonly ConcurrentBag<BlockType[]> _bufferPool = new();
    private int _inFlight;

    private readonly record struct Result((int, int, int) Key, int Version, ChunkMeshData Data);

    public void Init(VoxelWorld world, Material opaque, Material water)
    {
        _world = world;
        _opaque = opaque;
        _water = water;
        _world.BlockChanged += MarkBlockDirty;
    }

    /// <summary>Meshes every chunk in parallel and blocks until all are on screen. Returns milliseconds taken.</summary>
    public double LoadAll()
    {
        var sw = Stopwatch.StartNew();
        int cxN = _world.ChunksX, cyN = _world.ChunksY, czN = _world.ChunksZ;
        var data = new ChunkMeshData?[cxN * cyN * czN];
        Parallel.For(0, data.Length, i =>
        {
            int cx = i % cxN, cz = i / cxN % czN, cy = i / (cxN * czN);
            if (!_world.IsChunkAllocated(cx, cy, cz)) return;
            var buffer = RentBuffer();
            _world.CopyChunkPadded(cx, cy, cz, buffer);
            data[i] = ChunkMesher.BuildData(buffer, cx, cy, cz);
            _bufferPool.Add(buffer);
        });
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] is not { } d) continue;
            int cx = i % cxN, cz = i / cxN % czN, cy = i / (cxN * czN);
            Apply((cx, cy, cz), d);
        }
        return sw.Elapsed.TotalMilliseconds;
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
        _dirty.Add((cx, cy, cz));
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
        if (_dirty.Count == 0) return;
        _dispatchScratch.Clear();
        foreach (var key in _dirty)
        {
            if (_dispatchScratch.Count >= MaxDispatchPerFrame) break;
            _dispatchScratch.Add(key);
        }
        foreach (var key in _dispatchScratch)
        {
            _dirty.Remove(key);
            int version = _version.TryGetValue(key, out var v) ? v + 1 : 1;
            _version[key] = version;
            var buffer = RentBuffer();
            _world.CopyChunkPadded(key.Item1, key.Item2, key.Item3, buffer);
            System.Threading.Interlocked.Increment(ref _inFlight);
            Task.Run(() =>
            {
                var data = ChunkMesher.BuildData(buffer, key.Item1, key.Item2, key.Item3);
                _bufferPool.Add(buffer);
                _results.Enqueue(new Result(key, version, data));
                System.Threading.Interlocked.Decrement(ref _inFlight);
            });
        }
    }

    private void Upload()
    {
        long start = Stopwatch.GetTimestamp();
        double budgetTicks = UploadBudgetMs * Stopwatch.Frequency / 1000.0;
        while (Stopwatch.GetTimestamp() - start < budgetTicks && _results.TryDequeue(out var r))
        {
            // A newer copy of this chunk is already being meshed; skip the stale one.
            if (_version[r.Key] != r.Version) continue;
            Apply(r.Key, r.Data);
            RebuildsCompleted++;
        }
    }

    private void Apply((int, int, int) key, ChunkMeshData data)
    {
        var mesh = ChunkMesher.ToMesh(data, _opaque, _water);
        _nodes.TryGetValue(key, out var node);
        if (mesh == null)
        {
            if (node != null) { node.QueueFree(); _nodes.Remove(key); }
            return;
        }
        if (node == null)
        {
            node = new MeshInstance3D
            {
                Position = new Vector3(key.Item1, key.Item2, key.Item3) * VoxelWorld.ChunkSize * ChunkMesher.BlockSize,
            };
            AddChild(node);
            _nodes[key] = node;
        }
        node.Mesh = mesh;
    }

    private BlockType[] RentBuffer() => _bufferPool.TryTake(out var b) ? b : new BlockType[PaddedLength];

    public override void _ExitTree()
    {
        if (_world != null) _world.BlockChanged -= MarkBlockDirty;
    }
}
