using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Rts.Sim.World;

/// <summary>Surface biome of one column.</summary>
public enum Biome : byte
{
    Ocean = 0,
    Beach,
    Grassland,
    Forest,
    Hills,
    Swamp,
    River,
}

/// <summary>Where a player starts: the centre of a flat clearing, standing on the block at Y.</summary>
public readonly record struct StartArea(int Player, int X, int Y, int Z);

/// <summary>
/// A playable region: the voxel world plus the per-column biome map and player start areas.
/// Can be generated (<see cref="RegionGenerator"/>) or saved and loaded, so handmade maps work too.
/// </summary>
public sealed class RegionMap
{
    public const int FormatVersion = 1;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("RTSMAP");

    public ulong Seed { get; }
    public VoxelWorld World { get; }
    public List<StartArea> Starts { get; } = new();

    /// <summary>Optional: where the generator put the cavern (centre, in blocks).</summary>
    public (int X, int Y, int Z)? Cavern { get; set; }

    private readonly Biome[] _biomes;

    public RegionMap(ulong seed, VoxelWorld world)
    {
        Seed = seed;
        World = world;
        _biomes = new Biome[world.SizeX * world.SizeZ];
    }

    public Biome BiomeAt(int x, int z) =>
        x < 0 || z < 0 || x >= World.SizeX || z >= World.SizeZ ? Biome.Ocean : _biomes[z * World.SizeX + x];

    public void SetBiome(int x, int z, Biome b) => _biomes[z * World.SizeX + x] = b;

    /// <summary>Hash of blocks, biomes and starts; equal maps have equal hashes.</summary>
    public ulong ComputeHash()
    {
        ulong h = World.ComputeHash();
        foreach (var b in _biomes) h = (h ^ (byte)b) * 1099511628211UL;
        foreach (var s in Starts)
            h = (h ^ (ulong)(uint)(s.Player * 7919 + s.X * 31 + s.Y * 131 + s.Z)) * 1099511628211UL;
        return h;
    }

    /// <summary>
    /// Binary format, little-endian: "RTSMAP", u16 version, u64 seed, i32 sizeX/Y/Z, i32 start count,
    /// then (i32 player, x, y, z) per start, u8 hasCavern (+ i32 x, y, z), the biome map run-length
    /// encoded, then every block in (y, z, x) order run-length encoded. Runs are (u16 length, u8 value).
    /// </summary>
    public void Save(Stream stream)
    {
        using var w = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        w.Write(Magic);
        w.Write((ushort)FormatVersion);
        w.Write(Seed);
        w.Write(World.SizeX); w.Write(World.SizeY); w.Write(World.SizeZ);
        w.Write(Starts.Count);
        foreach (var s in Starts) { w.Write(s.Player); w.Write(s.X); w.Write(s.Y); w.Write(s.Z); }
        w.Write(Cavern.HasValue);
        if (Cavern is { } c) { w.Write(c.X); w.Write(c.Y); w.Write(c.Z); }

        var rle = new RunWriter(w);
        foreach (var b in _biomes) rle.Add((byte)b);
        rle.Flush();
        for (int y = 0; y < World.SizeY; y++)
        for (int z = 0; z < World.SizeZ; z++)
        for (int x = 0; x < World.SizeX; x++)
            rle.Add((byte)World.Get(x, y, z));
        rle.Flush();
    }

    public static RegionMap Load(Stream stream)
    {
        using var r = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        var magic = r.ReadBytes(Magic.Length);
        if (!magic.AsSpan().SequenceEqual(Magic)) throw new InvalidDataException("Not an RTS map file");
        int version = r.ReadUInt16();
        if (version != FormatVersion) throw new InvalidDataException($"Unsupported map version {version}");
        ulong seed = r.ReadUInt64();
        int sx = r.ReadInt32(), sy = r.ReadInt32(), sz = r.ReadInt32();
        if (sx <= 0 || sy <= 0 || sz <= 0 || (long)sx * sy * sz > 1L << 28) throw new InvalidDataException("Bad map size");
        var map = new RegionMap(seed, new VoxelWorld(sx, sy, sz));
        int starts = r.ReadInt32();
        if (starts < 0 || starts > 64) throw new InvalidDataException("Bad start count");
        for (int i = 0; i < starts; i++) map.Starts.Add(new StartArea(r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32()));
        if (r.ReadBoolean()) map.Cavern = (r.ReadInt32(), r.ReadInt32(), r.ReadInt32());

        var runs = new RunReader(r);
        for (int i = 0; i < map._biomes.Length; i++)
        {
            byte b = runs.Next();
            if (b > (byte)Biome.River) throw new InvalidDataException($"Bad biome {b}");
            map._biomes[i] = (Biome)b;
        }
        runs.ExpectEnd();
        byte maxBlock = (byte)BlockInfo.Last;
        for (int y = 0; y < sy; y++)
        for (int z = 0; z < sz; z++)
        for (int x = 0; x < sx; x++)
        {
            byte b = runs.Next();
            if (b > maxBlock) throw new InvalidDataException($"Bad block {b}");
            if (b != 0) map.World.Set(x, y, z, (BlockType)b);
        }
        runs.ExpectEnd();
        return map;
    }

    private sealed class RunWriter
    {
        private readonly BinaryWriter _w;
        private int _count;
        private byte _value;
        public RunWriter(BinaryWriter w) => _w = w;

        public void Add(byte v)
        {
            if (_count > 0 && (v != _value || _count == ushort.MaxValue)) Flush();
            _value = v;
            _count++;
        }

        public void Flush()
        {
            if (_count == 0) return;
            _w.Write((ushort)_count);
            _w.Write(_value);
            _count = 0;
        }
    }

    private sealed class RunReader
    {
        private readonly BinaryReader _r;
        private int _left;
        private byte _value;
        public RunReader(BinaryReader r) => _r = r;

        public byte Next()
        {
            if (_left == 0)
            {
                _left = _r.ReadUInt16();
                _value = _r.ReadByte();
                if (_left == 0) throw new InvalidDataException("Empty run");
            }
            _left--;
            return _value;
        }

        public void ExpectEnd()
        {
            if (_left != 0) throw new InvalidDataException("Run crosses a section boundary");
        }
    }
}
