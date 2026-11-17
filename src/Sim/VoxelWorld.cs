using System;

namespace Rts.Sim;

/// <summary>
/// Chunked voxel grid. X/Z are horizontal, Y is up. Chunks are 16^3 and allocated lazily,
/// so empty sky costs no memory.
/// </summary>
public sealed class VoxelWorld
{
    public const int ChunkSize = 16;

    public int SizeX { get; }
    public int SizeY { get; }
    public int SizeZ { get; }
    public int ChunksX { get; }
    public int ChunksY { get; }
    public int ChunksZ { get; }

    private readonly BlockType[]?[] _chunks;

    /// <summary>Raised with chunk coordinates whenever a block in that chunk changes.</summary>
    public event Action<int, int, int>? ChunkChanged;

    /// <summary>Raised with block coordinates whenever a single block changes.</summary>
    public event Action<int, int, int>? BlockChanged;

    public VoxelWorld(int sizeX, int sizeY, int sizeZ)
    {
        if (sizeX <= 0 || sizeY <= 0 || sizeZ <= 0) throw new ArgumentOutOfRangeException(nameof(sizeX));
        SizeX = sizeX; SizeY = sizeY; SizeZ = sizeZ;
        ChunksX = (sizeX + ChunkSize - 1) / ChunkSize;
        ChunksY = (sizeY + ChunkSize - 1) / ChunkSize;
        ChunksZ = (sizeZ + ChunkSize - 1) / ChunkSize;
        _chunks = new BlockType[]?[ChunksX * ChunksY * ChunksZ];
    }

    public bool InBounds(int x, int y, int z) =>
        x >= 0 && y >= 0 && z >= 0 && x < SizeX && y < SizeY && z < SizeZ;

    public BlockType Get(int x, int y, int z)
    {
        if (!InBounds(x, y, z)) return BlockType.Air;
        var chunk = _chunks[ChunkIndex(x / ChunkSize, y / ChunkSize, z / ChunkSize)];
        return chunk is null ? BlockType.Air : chunk[LocalIndex(x % ChunkSize, y % ChunkSize, z % ChunkSize)];
    }

    public void Set(int x, int y, int z, BlockType type)
    {
        if (!InBounds(x, y, z)) throw new ArgumentOutOfRangeException(nameof(x), $"({x},{y},{z}) is outside the world");
        int cx = x / ChunkSize, cy = y / ChunkSize, cz = z / ChunkSize;
        int ci = ChunkIndex(cx, cy, cz);
        var chunk = _chunks[ci];
        if (chunk is null)
        {
            if (type == BlockType.Air) return;
            chunk = _chunks[ci] = new BlockType[ChunkSize * ChunkSize * ChunkSize];
        }
        int li = LocalIndex(x % ChunkSize, y % ChunkSize, z % ChunkSize);
        if (chunk[li] == type) return;
        chunk[li] = type;
        ChunkChanged?.Invoke(cx, cy, cz);
        BlockChanged?.Invoke(x, y, z);
    }

    /// <summary>A copy of the world as it is now (without anyone listening to its changes).</summary>
    public VoxelWorld Clone()
    {
        var copy = new VoxelWorld(SizeX, SizeY, SizeZ);
        for (int i = 0; i < _chunks.Length; i++)
            if (_chunks[i] is { } chunk) copy._chunks[i] = (BlockType[])chunk.Clone();
        return copy;
    }

    /// <summary>True if the chunk has storage. Unallocated chunks are all air.</summary>
    public bool IsChunkAllocated(int cx, int cy, int cz) =>
        cx >= 0 && cy >= 0 && cz >= 0 && cx < ChunksX && cy < ChunksY && cz < ChunksZ
        && _chunks[ChunkIndex(cx, cy, cz)] is not null;

    /// <summary>Side length of the array filled by <see cref="CopyChunkPadded"/>.</summary>
    public const int PaddedSize = ChunkSize + 2;

    /// <summary>
    /// Copies one chunk plus a one-block border from its neighbours into <paramref name="dest"/>,
    /// indexed <c>(y * PaddedSize + z) * PaddedSize + x</c> with local coordinates shifted by one.
    /// The copy lets a mesher run on another thread while the simulation keeps changing the world.
    /// </summary>
    public void CopyChunkPadded(int cx, int cy, int cz, BlockType[] dest)
    {
        const int p = PaddedSize;
        if (dest.Length < p * p * p) throw new ArgumentException("Destination is too small", nameof(dest));
        int ox = cx * ChunkSize - 1, oy = cy * ChunkSize - 1, oz = cz * ChunkSize - 1;
        var centre = _chunks[ChunkIndex(cx, cy, cz)];
        for (int y = 0; y < p; y++)
        for (int z = 0; z < p; z++)
        {
            int row = (y * p + z) * p;
            bool interiorRow = y > 0 && y < p - 1 && z > 0 && z < p - 1;
            if (interiorRow)
            {
                dest[row] = Get(ox, oy + y, oz + z);
                int src = LocalIndex(0, y - 1, z - 1);
                for (int x = 1; x < p - 1; x++)
                    dest[row + x] = centre is null ? BlockType.Air : centre[src + x - 1];
                dest[row + p - 1] = Get(ox + p - 1, oy + y, oz + z);
            }
            else
            {
                for (int x = 0; x < p; x++) dest[row + x] = Get(ox + x, oy + y, oz + z);
            }
        }
    }

    /// <summary>
    /// Copies the cube of <paramref name="size"/> blocks a side whose lowest corner is block (x0, y0, z0), plus a
    /// one-block border all round, into <paramref name="dest"/>, indexed
    /// <c>((y + 1) * (size + 2) + z + 1) * (size + 2) + x + 1</c> for coordinates local to the cube. Outside the
    /// world is air. Like <see cref="CopyChunkPadded"/>, for cubes that are not one chunk.
    /// </summary>
    public void CopyBoxPadded(int x0, int y0, int z0, int size, BlockType[] dest)
    {
        int p = size + 2;
        if (dest.Length < p * p * p) throw new ArgumentException("Destination is too small", nameof(dest));
        for (int y = 0; y < p; y++)
        for (int z = 0; z < p; z++)
        {
            int row = (y * p + z) * p;
            int wy = y0 - 1 + y, wz = z0 - 1 + z;
            if (wy < 0 || wz < 0 || wy >= SizeY || wz >= SizeZ)
            {
                Array.Fill(dest, BlockType.Air, row, p);
                continue;
            }
            int cy = wy / ChunkSize, cz = wz / ChunkSize;
            int local = ((wy % ChunkSize) * ChunkSize + wz % ChunkSize) * ChunkSize;
            for (int x = 0; x < p;)
            {
                int wx = x0 - 1 + x;
                if (wx < 0 || wx >= SizeX) { dest[row + x++] = BlockType.Air; continue; }
                // The rest of this row that lies in the same chunk, in one go.
                int lx = wx % ChunkSize;
                int run = Math.Min(Math.Min(ChunkSize - lx, p - x), SizeX - wx);
                var chunk = _chunks[ChunkIndex(wx / ChunkSize, cy, cz)];
                if (chunk is null) Array.Fill(dest, BlockType.Air, row + x, run);
                else Array.Copy(chunk, local + lx, dest, row + x, run);
                x += run;
            }
        }
    }

    /// <summary>
    /// A world twice as fine: every block of this one as eight blocks half its size (see
    /// <see cref="World.FineTerrain"/>).
    /// </summary>
    public VoxelWorld Upscaled()
    {
        const int half = ChunkSize / 2;
        var fine = new VoxelWorld(SizeX * 2, SizeY * 2, SizeZ * 2);
        for (int cy = 0; cy < ChunksY; cy++)
        for (int cz = 0; cz < ChunksZ; cz++)
        for (int cx = 0; cx < ChunksX; cx++)
        {
            if (_chunks[ChunkIndex(cx, cy, cz)] is not { } chunk) continue;
            for (int o = 0; o < 8; o++)
            {
                int ox = o & 1, oy = o >> 1 & 1, oz = o >> 2;
                int fx = cx * 2 + ox, fy = cy * 2 + oy, fz = cz * 2 + oz;
                if (fx >= fine.ChunksX || fy >= fine.ChunksY || fz >= fine.ChunksZ) continue;
                BlockType[]? to = null;
                for (int y = 0; y < half; y++)
                for (int z = 0; z < half; z++)
                for (int x = 0; x < half; x++)
                {
                    var b = chunk[LocalIndex(ox * half + x, oy * half + y, oz * half + z)];
                    if (b == BlockType.Air) continue;
                    to ??= new BlockType[ChunkSize * ChunkSize * ChunkSize];
                    int at = LocalIndex(x * 2, y * 2, z * 2);
                    to[at] = to[at + 1] = b;
                    to[at + ChunkSize] = to[at + ChunkSize + 1] = b;
                    at += ChunkSize * ChunkSize;
                    to[at] = to[at + 1] = b;
                    to[at + ChunkSize] = to[at + ChunkSize + 1] = b;
                }
                fine._chunks[fine.ChunkIndex(fx, fy, fz)] = to;
            }
        }
        return fine;
    }

    /// <summary>Bytes of block storage in use: a byte a block for every chunk that holds anything.</summary>
    public long StorageBytes
    {
        get
        {
            long n = 0;
            foreach (var c in _chunks) if (c is not null) n += c.Length;
            return n;
        }
    }

    /// <summary>Highest solid block in a column, or -1 if the column is empty.</summary>
    public int SurfaceHeight(int x, int z)
    {
        for (int y = SizeY - 1; y >= 0; y--)
            if (BlockInfo.IsSolid(Get(x, y, z))) return y;
        return -1;
    }

    /// <summary>FNV-1a hash over every block, used to check two simulations are identical.</summary>
    public ulong ComputeHash()
    {
        ulong h = 14695981039346656037UL;
        for (int i = 0; i < _chunks.Length; i++)
        {
            var chunk = _chunks[i];
            if (chunk is null) { h = (h ^ 0xFF) * 1099511628211UL; continue; }
            foreach (var b in chunk) h = (h ^ (byte)b) * 1099511628211UL;
        }
        return h;
    }

    private int ChunkIndex(int cx, int cy, int cz) => (cy * ChunksZ + cz) * ChunksX + cx;
    private static int LocalIndex(int lx, int ly, int lz) => (ly * ChunkSize + lz) * ChunkSize + lx;
}
