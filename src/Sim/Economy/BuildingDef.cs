using System;
using System.Collections.Generic;
using Rts.Sim.Content;
using Rts.Sim.Units;

namespace Rts.Sim.Economy;

public enum BuildingKind : byte
{
    /// <summary>Where a faction starts: stores goods, houses people, trains workers and researches techs.</summary>
    TownHall,
    /// <summary>Workers bring goods here. Goods inside can be captured or burned.</summary>
    Storehouse,
    /// <summary>Raises the population limit.</summary>
    House,
    /// <summary>A field that workers farm for food. Units walk across it.</summary>
    Farm,
    /// <summary>Turns iron ore and coal into iron; workers haul its inputs and output.</summary>
    Smelter,
    /// <summary>Trains soldiers.</summary>
    Barracks,
    /// <summary>
    /// A stretch of wall: solid blocks the full height of the footprint, no roof. Laid along a line, column by
    /// column (see <see cref="WallCommand"/>), and set next to other walls and gates without a gap.
    /// </summary>
    Wall,
    /// <summary>
    /// A gate in a wall: its two bottom levels are gate leaves (<see cref="BlockType.Gate"/>) its owner's units
    /// walk through and everyone else's find shut, with the wall material above.
    /// </summary>
    Gate,
    /// <summary>
    /// A light: a torch post or a campfire, a solid column of its wall material that lights the ground around
    /// it from dusk to dawn (<see cref="BuildingDef.Light"/>, see <see cref="Conditions"/>).
    /// </summary>
    Light,
    /// <summary>
    /// A city-state's totem, in the middle of its town (see <see cref="CityStateSystem"/>): a plinth of its
    /// footing material across the footprint with a pole of its wall material in the middle, wallHeight high
    /// in all. Nobody builds one; whoever destroys it holds the town.
    /// </summary>
    Totem,
    /// <summary>
    /// A column of bridge: one deck block of its wall material, laid level across water or a drop, which units
    /// walk on (see <see cref="BridgeCommand"/>). Its level (<see cref="Building.Y"/>) is the deck block's, so
    /// units stand one above it. Anyone walks over it; a blow knocks the deck out and it falls.
    /// </summary>
    Bridge,
}

/// <summary>One block of a building, relative to its footprint corner and base level.</summary>
public readonly record struct TemplateBlock(int X, int Y, int Z, BlockType Block);

/// <summary>
/// Static description of a building type. A building is made of real blocks: a box of walls with a
/// stepped roof (or, for a farm, a field of farmland), placed layer by layer as it is built. The front,
/// with the door, faces +Z. Loaded from <c>data/buildings.json</c>.
/// </summary>
public sealed class BuildingDef
{
    /// <summary>Position in <see cref="ContentDb.Buildings"/>.</summary>
    public int Index { get; internal set; }
    public required string Id { get; init; }
    public required string Name { get; init; }
    /// <summary>The name of the picture the game shows for it (see the game's <c>UiIcons</c>); the id if not given.</summary>
    public string Icon { get; init; } = "";
    public BuildingKind Kind { get; init; }
    public int SizeX { get; init; } = 4;
    public int SizeZ { get; init; } = 4;
    /// <summary>Wall height in blocks; the roof goes on top.</summary>
    public int WallHeight { get; init; } = 3;
    public BlockType Wall { get; init; } = BlockType.Planks;
    public BlockType Roof { get; init; } = BlockType.Thatch;
    /// <summary>The bottom course of the walls, if different from the wall (Air means the same).</summary>
    public BlockType Footing { get; init; } = BlockType.Air;
    /// <summary>A stone chimney at the back corner, rising above the roof.</summary>
    public bool Chimney { get; init; }
    /// <summary>
    /// How far (columns beyond its footprint) its lantern or fire lights the ground from dusk to dawn, so
    /// that what stands there is seen from the daytime distance and hit as by day (see <see cref="Conditions"/>);
    /// 0 for none.
    /// </summary>
    public int Light { get; init; }
    public GoodAmount[] Cost { get; init; } = Array.Empty<GoodAmount>();
    /// <summary>Worker-ticks of building work: one worker takes this many ticks, two take half.</summary>
    public int BuildTicks { get; init; } = 200;
    /// <summary>People this building houses (the population limit is the sum over a player's buildings).</summary>
    public int Housing { get; init; }
    /// <summary>Workers deliver goods here, and its goods count toward the player's stock.</summary>
    public bool Storage { get; init; }
    /// <summary>Enemies can capture it (with its goods) or burn it.</summary>
    public bool Raidable { get; init; }
    /// <summary>Most workers that can work here at once (farm, smelter).</summary>
    public int WorkerSlots { get; init; }
    /// <summary>How far (columns) beyond its footprint its player sees under fog of war (see <see cref="VisionSystem"/>).</summary>
    public int Sight { get; init; } = 6;
    /// <summary>Unit ids this building trains (the units whose <c>trainedAt</c> names it), in data order.</summary>
    public string[] Trains { get; internal set; } = Array.Empty<string>();
    /// <summary>Techs the player must have before placing it.</summary>
    public IReadOnlyList<TechDef> Requires { get; internal set; } = Array.Empty<TechDef>();
    /// <summary>The only faction that can build it; null for every faction.</summary>
    public FactionDef? Faction { get; internal set; }
    /// <summary>
    /// Built only on a cleared monster den, by a faction whose den use names it (see <see cref="Content.DenUse"/>),
    /// never from the build menu.
    /// </summary>
    public bool FromDen { get; init; }
    /// <summary>Goods it brings in on its own, every <see cref="YieldTicks"/>, once finished (the wolves of a wolf lodge hunting).</summary>
    public GoodAmount[] Yield { get; init; } = Array.Empty<GoodAmount>();
    /// <summary>Ticks between its yields; 0 when it has none.</summary>
    public int YieldTicks { get; init; }

    /// <summary>Units stand on it rather than next to it.</summary>
    public bool Walkable => Kind == BuildingKind.Farm;

    /// <summary>A wall or gate: laid in lines, and put right next to other walls and gates.</summary>
    public bool IsWall => Kind is BuildingKind.Wall or BuildingKind.Gate;

    /// <summary>A column of bridge deck (see <see cref="BuildingKind.Bridge"/>).</summary>
    public bool IsBridge => Kind == BuildingKind.Bridge;

    private TemplateBlock[]? _blocks;
    private int[]? _toughness;
    private int[]? _indexAt;

    /// <summary>The building's blocks in the order they are built: bottom layer first.</summary>
    public IReadOnlyList<TemplateBlock> Blocks => _blocks ??= BuildTemplate();

    /// <summary>The toughness of all its blocks together (see <see cref="BlockInfo.Toughness"/>).</summary>
    public int Toughness => ToughnessOf(Blocks.Count);

    /// <summary>The toughness of its first <paramref name="placed"/> blocks in build order.</summary>
    public int ToughnessOf(int placed)
    {
        var sums = _toughness;
        if (sums == null)
        {
            // Built in a local first: definitions are shared, and another thread may be reading them.
            var blocks = Blocks;
            sums = new int[blocks.Count + 1];
            for (int k = 0; k < blocks.Count; k++) sums[k + 1] = sums[k] + BlockInfo.Toughness(blocks[k].Block);
            _toughness = sums;
        }
        return sums[Math.Clamp(placed, 0, sums.Length - 1)];
    }

    /// <summary>
    /// The index in <see cref="Blocks"/> of the block at (x, y, z) relative to the footprint corner and base
    /// level, or -1 if the building has no block there.
    /// </summary>
    public int BlockAt(int x, int y, int z)
    {
        int levels = Height + 1; // from -1 (a field) up to the top
        if (x < 0 || z < 0 || x >= SizeX || z >= SizeZ || y < -1 || y >= levels - 1) return -1;
        var index = _indexAt;
        if (index == null)
        {
            index = new int[SizeX * SizeZ * levels];
            Array.Fill(index, -1);
            var blocks = Blocks;
            for (int k = 0; k < blocks.Count; k++)
                index[((blocks[k].Y + 1) * SizeZ + blocks[k].Z) * SizeX + blocks[k].X] = k;
            _indexAt = index;
        }
        return index[((y + 1) * SizeZ + z) * SizeX + x];
    }

    private int _height = -1;

    /// <summary>Blocks above the base level, including the roof (0 for a field).</summary>
    public int Height
    {
        get
        {
            if (_height >= 0) return _height;
            int h = 0;
            foreach (var b in Blocks) h = Math.Max(h, b.Y + 1);
            return _height = h;
        }
    }

    private TemplateBlock[] BuildTemplate()
    {
        var list = new List<TemplateBlock>();
        if (Kind == BuildingKind.Farm)
        {
            // The field replaces the ground surface, one level below where units stand.
            for (int z = 0; z < SizeZ; z++)
            for (int x = 0; x < SizeX; x++)
                list.Add(new TemplateBlock(x, -1, z, BlockType.Farmland));
            return list.ToArray();
        }
        if (Kind == BuildingKind.Totem)
        {
            var plinth = Footing != BlockType.Air ? Footing : Wall;
            for (int z = 0; z < SizeZ; z++)
            for (int x = 0; x < SizeX; x++)
                list.Add(new TemplateBlock(x, 0, z, plinth));
            for (int y = 1; y < WallHeight; y++) list.Add(new TemplateBlock(SizeX / 2, y, SizeZ / 2, Wall));
            return list.ToArray();
        }
        if (IsWall || Kind is BuildingKind.Light or BuildingKind.Bridge)
        {
            // Solid to the top (a bridge is one deck block); a gate's bottom two levels (a unit's height) are its leaves.
            for (int y = 0; y < WallHeight; y++)
            for (int z = 0; z < SizeZ; z++)
            for (int x = 0; x < SizeX; x++)
            {
                var block = Kind == BuildingKind.Gate && y < Walk.Clearance ? BlockType.Gate
                    : y == 0 && Footing != BlockType.Air ? Footing : Wall;
                list.Add(new TemplateBlock(x, y, z, block));
            }
            return list.ToArray();
        }

        int doorX = SizeX / 2;
        for (int y = 0; y < WallHeight; y++)
        for (int z = 0; z < SizeZ; z++)
        for (int x = 0; x < SizeX; x++)
        {
            var block = y == 0 && Footing != BlockType.Air ? Footing : Wall;
            if (z == SizeZ - 1 && x == doorX && y < 2) block = BlockType.Door;
            list.Add(new TemplateBlock(x, y, z, block));
        }
        // A stepped roof: each layer one block in from the last.
        int top = WallHeight;
        for (int k = 0; k <= SizeX - 1 - k && k <= SizeZ - 1 - k; k++, top++)
        for (int z = k; z <= SizeZ - 1 - k; z++)
        for (int x = k; x <= SizeX - 1 - k; x++)
        {
            bool chimney = Chimney && x == SizeX - 1 && z == 0;
            list.Add(new TemplateBlock(x, top, z, chimney ? BlockType.StoneBrick : Roof));
        }
        if (Chimney)
            for (int y = WallHeight + 1; y < top + 1; y++)
                list.Add(new TemplateBlock(SizeX - 1, y, 0, BlockType.StoneBrick));
        // Build order: bottom up, then back to front, then left to right.
        list.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.Z != b.Z ? a.Z.CompareTo(b.Z) : a.X.CompareTo(b.X));
        return list.ToArray();
    }
}
