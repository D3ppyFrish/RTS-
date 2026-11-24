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
    /// column (see <see cref="WallCommand"/>), and set next to other walls and gates without a gap. Walls come
    /// in tiers: the one that is laid, and those it is upgraded to in place (see <see cref="BuildingDef.Upgrade"/>).
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
    /// <summary>
    /// A tower: a solid block of its wall material, taller than a wall, with a deck on top that ranged units
    /// are posted on (see <see cref="PostCommand"/>). Placed like any building, on open ground or onto a wall
    /// of its owner's, whose columns under it become part of it; it joins walls and gates without a gap.
    /// It sees far (<see cref="BuildingDef.Sight"/>), and those posted on it aim better (<see cref="BuildingDef.Aim"/>).
    /// </summary>
    Tower,
    /// <summary>
    /// A quarry, mine or fishing hut: workers work it, as they work a farm, for the goods in its <see cref="BuildingDef.Works"/>
    /// list, one round of work (<see cref="BuildingDef.WorkTicks"/>) at a time, taking each good of the list in turn,
    /// and carry them to storage. It never runs out. A fishing hut must stand at the water's edge (<see cref="BuildingDef.Shore"/>).
    /// </summary>
    Quarry,
    /// <summary>
    /// A shrine: its worker brings it crystal from storage, and it channels each crystal into
    /// <see cref="BuildingDef.Mana"/> mana for its owner over <see cref="BuildingDef.ChannelTicks"/>. Mana is what
    /// spells cost (see <see cref="Units.SpellSystem.ManaCost"/>).
    /// </summary>
    Shrine,
    /// <summary>
    /// A faction's great work (see <see cref="Rts.Sim.World.AscendancySystem"/>): it stands only in the deep caves, on ground
    /// under the land (<see cref="EconomySystem.DeepGround"/>), and a player has one at a time. Once finished it
    /// wins the match for its owner if it is still standing <see cref="Rts.Sim.World.AscendancySystem.AscendancyTicks"/> later.
    /// </summary>
    GreatWork,
    /// <summary>
    /// A workshop (the sawmill, the mason's yard): its worker brings it <see cref="BuildingDef.Takes"/> from storage and
    /// carries what it <see cref="BuildingDef.Makes"/> back, as a smelter's does ore and iron. It works one good into one
    /// every <see cref="BuildingDef.MakeTicks"/> (at its <see cref="BuildingDef.Speed"/>), and its worker stops fetching
    /// while its player has <see cref="BuildingDef.Keep"/> of what it makes in storage.
    /// </summary>
    Workshop,
    /// <summary>
    /// A listening post: a small hut whose watchman listens at the ground for digging and footsteps under it, so its
    /// owner sees units underground (in tunnels and caves) within <see cref="BuildingDef.Hearing"/> columns of it,
    /// which nobody on the surface sees otherwise (see <see cref="VisionSystem"/>).
    /// </summary>
    ListeningPost,
    /// <summary>
    /// An orchard (see <see cref="EconomySystem"/>'s orchards): a field planted with fruit trees that grow for
    /// <see cref="BuildingDef.GrowTicks"/> and then bear a basket of its <see cref="BuildingDef.Yield"/> every
    /// <see cref="BuildingDef.YieldTicks"/> with nobody working it, which workers and carts carry to storage. Its grown
    /// trees screen sight and shots like a hedge, and units push through them slowly. Raiders cut it down, and it
    /// grows again from saplings.
    /// </summary>
    Orchard,
    /// <summary>
    /// A beast pen (see <see cref="Units.CombatSystem"/>'s stampede): a yard of trodden earth that units walk onto, ringed
    /// by a low stake fence (a look only). Its owner's tamed beasts (<see cref="Units.UnitDef.Pennable"/>) are sent into
    /// it and kept there, up to <see cref="PenDef.Keeps"/>, and the player looses them from it as a stampede.
    /// </summary>
    Pen,
    /// <summary>
    /// A crystal lens (Crystal lens, a discovery tech; see <see cref="EconomySystem.Gazes"/>): a tower with a great crystal
    /// on top that its owner aims anywhere on the map (see <see cref="GazeCommand"/>), once every <see cref="LensDef.EveryTicks"/>,
    /// for <see cref="LensDef.Mana"/> mana: a circle of <see cref="LensDef.Radius"/> columns there is in their sight,
    /// through the fog of war, for <see cref="LensDef.Ticks"/>.
    /// </summary>
    Lens,
}

/// <summary>
/// The shape a faction's buildings take (<c>"look"</c> in <c>data/factions.json</c>, <see cref="Content.FactionDef.Look"/>;
/// see <see cref="BuildingDef.Shaped"/>).
/// </summary>
public enum BuildingLook : byte
{
    /// <summary>As the building's data says: houses with walls and a roof, walls and towers of their materials.</summary>
    Standard,
    /// <summary>
    /// The Deepfolk's: every hollow building a mound of hive earth like an ant hill (see <see cref="BuildingDef.Mound"/>),
    /// and walls, gates and towers ramparts of hive earth.
    /// </summary>
    Mound,
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
    /// A mound, like an ant hill, instead of a house with walls and a roof: the Deepfolk's buildings. Round at
    /// the foot (the footprint's corners left open), its sides rise straight for <see cref="WallHeight"/> and then
    /// narrow, like a heap of earth, to a rounded top, all of <see cref="Wall"/> (the roof material is not used). Hollow like a house,
    /// with its door in the middle of the front.
    /// </summary>
    public bool Mound { get; init; }
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
    /// <summary>Most workers that can work here at once (farm, smelter, quarry, mine).</summary>
    public int WorkerSlots { get; init; }
    /// <summary>How far (columns) beyond its footprint its player sees under fog of war (see <see cref="VisionSystem"/>).</summary>
    public int Sight { get; init; } = 6;
    /// <summary>
    /// What a quarry or mine gives its workers (see <see cref="BuildingKind.Quarry"/>): each round of work gives one
    /// entry of the list, a worker taking the entries in turn, so the list sets both which goods and how much of each.
    /// </summary>
    public GoodAmount[] Works { get; init; } = Array.Empty<GoodAmount>();
    /// <summary>Worker-ticks of one round of work at a quarry or mine; 0 for other buildings.</summary>
    public int WorkTicks { get; init; }
    /// <summary>
    /// It must stand at the water's edge: open water in a column right beside its footprint (a fishing hut; see
    /// <see cref="Economy.EconomySystem.BesideWater"/>).
    /// </summary>
    public bool Shore { get; init; }
    /// <summary>
    /// A forester's lodge: a quarry whose rounds give no goods but plant a sapling within this many columns of it
    /// (see <see cref="Economy.EconomySystem.Plant"/>); 0 for every other building.
    /// </summary>
    public int Plants { get; init; }
    /// <summary>
    /// A hunter's lodge: a quarry whose workers hunt the wild game (see <see cref="Units.UnitDef.Game"/>) within this
    /// many columns of it, each round of work an aimed shot, and carry home what they shoot; 0 for every other building.
    /// </summary>
    public int Hunts { get; init; }
    /// <summary>
    /// A yard that mends the machines it builds (a dock its ships, a siege workshop its catapults and rams): percent of
    /// such a machine's hit points given back each second it waits out of the fight within
    /// <see cref="Economy.EconomySystem.YardReach"/> of it, with no worker and no tech; 0 for every other building.
    /// </summary>
    public int Mends { get; init; }
    /// <summary>
    /// How far (columns beyond its footprint) a listening post hears units underground, so its owner sees them under
    /// fog of war (see <see cref="BuildingKind.ListeningPost"/>, <see cref="VisionSystem"/>); 0 for other buildings.
    /// </summary>
    public int Hearing { get; init; }
    /// <summary>A shrine: mana each crystal it channels gives its owner; 0 for every other building.</summary>
    public int Mana { get; init; }
    /// <summary>A shrine: ticks it takes to channel one crystal (before its <see cref="Speed"/>); 0 for every other building.</summary>
    public int ChannelTicks { get; init; }
    /// <summary>A workshop's ticks to work one good into one (see <see cref="BuildingKind.Workshop"/>); 0 for anything else.</summary>
    public int MakeTicks { get; init; }
    /// <summary>The good a workshop works (wood at a sawmill, stone at a mason's yard); unused for anything else.</summary>
    public Good Takes { get; init; }
    /// <summary>The good a workshop makes (planks, cut stone); unused for anything else.</summary>
    public Good Makes { get; init; }
    /// <summary>
    /// A workshop: what it makes in its player's storage at which its worker stops fetching, so a workshop left running
    /// does not work up all the town's wood or stone; it goes on once that is spent below this. 0 for anything else.
    /// </summary>
    public int Keep { get; init; }
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
    /// <summary>An orchard: ticks its saplings take to grow into fruit trees, once planted; 0 for anything else.</summary>
    public int GrowTicks { get; init; }
    /// <summary>
    /// How many times the damage of the same block elsewhere each of its blocks takes before it is knocked out
    /// (1 unless given): what makes one tier of wall sturdier than the last (see <see cref="BlockToughness"/>).
    /// </summary>
    public int Sturdy { get; init; } = 1;
    /// <summary>
    /// A thick, solid wall, gate or tower (<c>"thick"</c> in the data): blows struck at it close up do less to it
    /// (see <see cref="Content.CombatRules.Thick"/>), and a blast that knocks a block out of it brings down what
    /// stands above and beside it, toppling away from the blast (see <see cref="Units.CombatSystem"/>).
    /// </summary>
    public bool Thick { get; init; }
    /// <summary>
    /// A wall or gate with a walk along its top: ranged units can be posted on it (see <see cref="PostCommand"/>).
    /// </summary>
    public bool Walkway { get; init; }
    /// <summary>
    /// What it can be turned into where it stands (see <see cref="UpgradeCommand"/>), for that building's cost and
    /// building time; null if nothing. A wall, gate or tower keeps its footprint; any other building may grow,
    /// outward from its middle on every side, onto free ground round it.
    /// </summary>
    public BuildingDef? Upgrade { get; internal set; }
    /// <summary>
    /// The building this one is an upgrade of; null if it is none. An upgrade is never placed as it is: the
    /// building it comes from is, and then upgraded.
    /// </summary>
    public BuildingDef? UpgradeOf { get; internal set; }
    /// <summary>How many upgrades it is from a building that is placed: 0 for one that is, 1 for its upgrade, and so on.</summary>
    public int Tier { get; internal set; }
    /// <summary>
    /// A wall or gate that is faced: when it is finished (it is an upgrade), a column of this building goes up
    /// on each side of it, so the wall is three columns thick (see <see cref="Economy.EconomySystem.FacingCells"/>).
    /// Null for a wall one column thick.
    /// </summary>
    public BuildingDef? Facing { get; internal set; }
    /// <summary>The wall or gate this one is the facing of; null if it is none. A facing is never placed as it is.</summary>
    public BuildingDef? FacingOf { get; internal set; }
    /// <summary>
    /// How much better units posted on it aim: the share (percent) of their misses in the dark and bad weather
    /// that land after all, and a fifth as many points of their odds moved to the head; a tower's.
    /// </summary>
    public int Aim { get; init; }
    /// <summary>
    /// The holes in a gate's roof over its gateway that drop stones on enemies below (see
    /// <see cref="Units.CombatSystem"/>'s murder holes), for an owner with a <see cref="Content.TechEffectType.MurderHoles"/>
    /// tech; null for a gate without them.
    /// </summary>
    public MurderHoles? MurderHoles { get; init; }
    /// <summary>A beast pen's numbers (<c>pen</c> in the data; only for <see cref="BuildingKind.Pen"/>), else null.</summary>
    public PenDef? Pen { get; init; }
    /// <summary>A crystal lens's numbers (<c>lens</c> in the data; only for <see cref="BuildingKind.Lens"/>), else null.</summary>
    public LensDef? Lens { get; init; }
    /// <summary>
    /// How fast it trains units and smelts, in percent of the usual pace (100 unless given): what makes a great
    /// hall or a foundry better at its work than the building it was upgraded from.
    /// </summary>
    public int Speed { get; init; } = 100;

    /// <summary>A quarry or mine (see <see cref="BuildingKind.Quarry"/>).</summary>
    public bool IsQuarry => Kind == BuildingKind.Quarry;

    /// <summary>True if a round of work here can give <paramref name="good"/>.</summary>
    public bool Gives(Good good)
    {
        foreach (var w in Works)
            if (w.Good == good) return true;
        return Hunts > 0 && good == Good.Food;
    }

    /// <summary>Units stand on it rather than next to it.</summary>
    public bool Walkable => Kind is BuildingKind.Farm or BuildingKind.Orchard or BuildingKind.Pen;

    /// <summary>A wall or gate: laid in lines, and put right next to other walls and gates.</summary>
    public bool IsWall => Kind is BuildingKind.Wall or BuildingKind.Gate;
    /// <summary>A tower (see <see cref="BuildingKind.Tower"/>).</summary>
    public bool IsTower => Kind == BuildingKind.Tower;
    /// <summary>A wall, gate or tower: they join up without a gap, and units are posted on top of them.</summary>
    public bool IsRampart => IsWall || IsTower;

    /// <summary>A column of bridge deck (see <see cref="BuildingKind.Bridge"/>).</summary>
    public bool IsBridge => Kind == BuildingKind.Bridge;

    // The shape of a building of this kind for a faction with BuildingLook.Mound (see Shaped): this definition again,
    // built of hive earth. _base is the definition it is the shape of (null for a definition from the data).
    private BuildingDef? _mound;
    private BuildingDef? _base;

    /// <summary>True for a definition from the data; false for one made by <see cref="Shaped"/>.</summary>
    public bool IsBase => _base == null;

    /// <summary>
    /// What a building of this kind is made of when a player whose faction builds with <paramref name="look"/> puts it
    /// up (see <see cref="Building.Shape"/>): this definition for <see cref="BuildingLook.Standard"/>; for
    /// <see cref="BuildingLook.Mound"/>, one that is the same building in every way but its blocks. A hollow building
    /// becomes a mound of hive earth (see <see cref="Mound"/>) on the same footprint, as tall as it was with its roof
    /// and with its door in the same place; a wall, gate or tower is the same column of hive earth (a gate keeping its
    /// leaves). Either way its blocks add up to the same toughness as before, so it is as hard to bring down. Fields,
    /// bridges, lights and totems, and buildings that are mounds already, are as they are.
    /// </summary>
    public BuildingDef Shaped(BuildingLook look)
    {
        if (look == BuildingLook.Standard || _base != null || Mound
            || Kind is BuildingKind.Farm or BuildingKind.Orchard or BuildingKind.Pen or BuildingKind.Totem or BuildingKind.Light or BuildingKind.Bridge) return this;
        var mound = _mound;
        if (mound == null)
        {
            // Built in a local first: definitions are shared, and another thread may be reading them.
            mound = (BuildingDef)MemberwiseClone();
            mound._base = this;
            mound._mound = null;
            mound._blocks = null;
            mound._toughness = null;
            mound._indexAt = null;
            mound._height = -1;
            mound._solid = mound._hollow = 1;
            _ = mound.Blocks;
            _mound = mound;
        }
        return mound;
    }

    private TemplateBlock[]? _blocks;
    private int[]? _toughness;
    // A hollow building's blocks are made tougher by solid / hollow (the toughness of the solid building it would
    // be over that of its blocks), so hollowing it out leaves it as hard to bring down as before. 1 / 1 otherwise.
    private int _solid = 1, _hollow = 1;
    private int[]? _indexAt;

    /// <summary>The building's blocks in the order they are built: bottom layer first.</summary>
    public IReadOnlyList<TemplateBlock> Blocks => _blocks ??= BuildTemplate();

    /// <summary>The toughness of all its blocks together (see <see cref="BlockInfo.Toughness"/>).</summary>
    public int Toughness => ToughnessOf(Blocks.Count);

    /// <summary>
    /// The damage block k of <see cref="Blocks"/> takes before it is knocked out: its material's toughness
    /// (see <see cref="BlockInfo.Toughness"/>) times <see cref="Sturdy"/>. A hollow building's blocks are tougher
    /// in proportion to what it lacks inside, so all of it together is as tough as it would be solid.
    /// </summary>
    public int BlockToughness(int k)
    {
        var block = Blocks[k].Block; // builds the template, and with it the hollow building's scale
        return BlockInfo.Toughness(block) * Sturdy * _solid / _hollow;
    }

    /// <summary>The toughness of its first <paramref name="placed"/> blocks in build order.</summary>
    public int ToughnessOf(int placed)
    {
        var sums = _toughness;
        if (sums == null)
        {
            // Built in a local first: definitions are shared, and another thread may be reading them.
            var blocks = Blocks;
            sums = new int[blocks.Count + 1];
            for (int k = 0; k < blocks.Count; k++) sums[k + 1] = sums[k] + BlockToughness(k);
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
        if (_base != null) return HiveTemplate(_base);
        var list = new List<TemplateBlock>();
        if (Kind is BuildingKind.Farm or BuildingKind.Orchard or BuildingKind.Pen)
        {
            // The field (a pen's yard of trodden earth) replaces the ground surface, one level below where units stand.
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
        if (IsRampart || Kind is BuildingKind.Light or BuildingKind.Bridge)
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

        if (Mound) return MoundTemplate();

        // Hollow: a floor, walls round the edge and a roof over a room inside, so it comes apart like a
        // building, not a solid lump. The floor inside is BlockType.Floor, which nobody stands on, so units
        // never end up shut in the room (or step into it through a gap knocked in a wall).
        int doorX = SizeX / 2;
        int solid = 0; // the toughness it would have solid
        for (int y = 0; y < WallHeight; y++)
        for (int z = 0; z < SizeZ; z++)
        for (int x = 0; x < SizeX; x++)
        {
            bool edge = x == 0 || z == 0 || x == SizeX - 1 || z == SizeZ - 1;
            var block = y == 0 && Footing != BlockType.Air ? Footing : Wall;
            if (z == SizeZ - 1 && x == doorX && y < 2) block = BlockType.Door;
            solid += BlockInfo.Toughness(block);
            if (!edge && y > 0) continue;
            list.Add(new TemplateBlock(x, y, z, edge ? block : BlockType.Floor));
        }
        // A stepped roof: each layer one block in from the last. Each layer is only the ring the next one
        // does not cover, and the top layer is whole, so the room goes up under the roof.
        int top = WallHeight;
        for (int k = 0; k <= SizeX - 1 - k && k <= SizeZ - 1 - k; k++, top++)
        {
            bool last = k + 1 > SizeX - 2 - k || k + 1 > SizeZ - 2 - k;
            for (int z = k; z <= SizeZ - 1 - k; z++)
            for (int x = k; x <= SizeX - 1 - k; x++)
            {
                bool chimney = Chimney && x == SizeX - 1 && z == 0;
                var block = chimney ? BlockType.StoneBrick : Roof;
                solid += BlockInfo.Toughness(block);
                if (!last && x > k && x < SizeX - 1 - k && z > k && z < SizeZ - 1 - k) continue;
                list.Add(new TemplateBlock(x, top, z, block));
            }
        }
        int hollow = 0;
        foreach (var t in list) hollow += BlockInfo.Toughness(t.Block);
        if (hollow > 0 && solid > hollow) (_solid, _hollow) = (solid, hollow);
        if (Chimney)
            for (int y = WallHeight + 1; y < top + 1; y++)
                list.Add(new TemplateBlock(SizeX - 1, y, 0, BlockType.StoneBrick));
        // Build order: bottom up, then back to front, then left to right.
        list.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.Z != b.Z ? a.Z.CompareTo(b.Z) : a.X.CompareTo(b.X));
        return list.ToArray();
    }

    /// <summary>
    /// Whether column (x, z) of the footprint is inside a mound's shape at level y (see <see cref="Mound"/>): an
    /// ellipse a little bigger than will fit in the footprint, so its corners are cut, as wide up to the top of the
    /// walls and then narrowing like a dome, to nothing at <paramref name="peak"/> levels over the walls.
    /// </summary>
    private bool InMound(int x, int z, int y, int peak) => InMound(x, z, y, WallHeight, peak);

    private bool InMound(int x, int z, int y, int walls, int peak)
    {
        // The footprint's corners are always open, so even a small mound is round at the foot.
        if (SizeX >= 3 && SizeZ >= 3 && (x == 0 || x == SizeX - 1) && (z == 0 || z == SizeZ - 1)) return false;
        // In half blocks from the middle; inside if (cx/SizeX)^2 + (cz/SizeZ)^2 <= 13/10 * (1 - up/peak)^2 * (1 + up/peak):
        // a cone with a little bulge, like a heap of earth.
        long cx = 2 * x + 1 - SizeX, cz = 2 * z + 1 - SizeZ, sx = SizeX, sz = SizeZ;
        long up = Math.Max(0, y - (walls - 1)), p = peak;
        return (cx * cx * sz * sz + cz * cz * sx * sx) * 10 * p * p * p <= 13 * sx * sx * sz * sz * (p - up) * (p - up) * (p + up);
    }

    /// <summary>A mound's peak: how many levels over its walls it narrows to nothing.</summary>
    private int MoundPeak => Math.Max(SizeX, SizeZ) / 2 + 2;

    private TemplateBlock[] MoundTemplate() => MoundTemplate(WallHeight, Wall, Footing);

    private TemplateBlock[] MoundTemplate(int walls, BlockType wall, BlockType footing)
    {
        var list = new List<TemplateBlock>();
        int doorX = SizeX / 2, peak = MoundPeak;
        int solid = 0;
        for (int y = 0; y < walls + peak; y++)
        for (int z = 0; z < SizeZ; z++)
        for (int x = 0; x < SizeX; x++)
        {
            if (!InMound(x, z, y, walls, peak)) continue;
            var block = y == 0 && footing != BlockType.Air ? footing : wall;
            bool door = z == SizeZ - 1 && x == doorX && y < 2;
            if (door) block = BlockType.Door;
            solid += BlockInfo.Toughness(block);
            // The shell: blocks with open air (outside the mound) beside them or over them. The rest is the room.
            bool shell = door || !InMound(x, z, y + 1, walls, peak);
            for (int d = 0; d < 4 && !shell; d++)
            {
                int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0), nz = z + (d == 2 ? 1 : d == 3 ? -1 : 0);
                shell = nx < 0 || nz < 0 || nx >= SizeX || nz >= SizeZ || !InMound(nx, nz, y, walls, peak);
            }
            if (!shell && y > 0) continue;
            list.Add(new TemplateBlock(x, y, z, shell ? block : BlockType.Floor));
        }
        int hollow = 0;
        foreach (var t in list) hollow += BlockInfo.Toughness(t.Block);
        if (hollow > 0 && solid > hollow) (_solid, _hollow) = (solid, hollow);
        list.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.Z != b.Z ? a.Z.CompareTo(b.Z) : a.X.CompareTo(b.X));
        return list.ToArray();
    }

    /// <summary>
    /// The blocks of <paramref name="from"/> in hive earth (see <see cref="Shaped"/>), with each block's toughness
    /// scaled so that all of them add up to the toughness of <paramref name="from"/>.
    /// </summary>
    private TemplateBlock[] HiveTemplate(BuildingDef from)
    {
        TemplateBlock[] blocks;
        if (IsRampart)
        {
            var list = new List<TemplateBlock>();
            foreach (var t in from.Blocks)
                list.Add(t with { Block = t.Block == BlockType.Gate ? BlockType.Gate : BlockType.Hive });
            blocks = list.ToArray();
        }
        else
        {
            // As tall as the house with its roof: a mound is (walls + peak - 1) levels high. Its walls are at least
            // two high, so the door fits.
            int walls = Math.Max(2, from.Height - MoundPeak + 1);
            blocks = MoundTemplate(walls, BlockType.Hive, BlockType.Air);
        }
        long sum = 0;
        foreach (var t in blocks) sum += BlockInfo.Toughness(t.Block) * Sturdy;
        if (sum <= 0) return blocks;
        int target = from.Toughness;
        (_solid, _hollow) = (target, (int)sum);
        // Each block's toughness is rounded down: scale up once more for what the rounding lost.
        long total = 0;
        foreach (var t in blocks) total += BlockInfo.Toughness(t.Block) * Sturdy * _solid / _hollow;
        if (total > 0 && total < target) _solid = (int)((long)target * target / total);
        return blocks;
    }
}

/// <summary>
/// A gate's murder holes (<c>murderHoles</c> in <c>data/buildings.json</c>): while the gate is held, a stone of
/// <see cref="Damage"/> (crushing, on the head) drops every <see cref="EverySeconds"/> on an enemy in the gateway
/// or up to <see cref="Reach"/> columns out in front of it. The gate is held while a unit of its owner stands on
/// a wall, gate or tower within <see cref="Held"/> columns of it.
/// </summary>
public sealed class MurderHoles
{
    public int Damage { get; init; }
    public int EverySeconds { get; init; }
    public int Reach { get; init; }
    public int Held { get; init; }
}

/// <summary>
/// A beast pen's numbers (<c>pen</c> in <c>data/buildings.json</c>; see <c>Units/CombatSystem.Stampede.cs</c>): it keeps up
/// to <see cref="Keeps"/> tamed beasts. Loosed as a stampede, they run at <see cref="Speed"/> columns a second for at most
/// <see cref="Run"/> columns; each beast that runs into an enemy deals it <see cref="Trample"/> (crushing) and knocks it
/// down for <see cref="DownTicks"/>; a beast running into an enemy's wooden building strikes it once for
/// <see cref="Smash"/>. After the run the beasts walk <see cref="Slow"/> percent of their pace for <see cref="TiredTicks"/>.
/// </summary>
public sealed class LensDef
{
    /// <summary>Ticks from one gaze to the next (<c>every</c>, in seconds, in the data).</summary>
    public int EveryTicks { get; init; }
    /// <summary>Columns round the spot aimed at that the gaze shows.</summary>
    public int Radius { get; init; }
    /// <summary>Ticks the gaze lasts (<c>time</c>, in seconds, in the data).</summary>
    public int Ticks { get; init; }
    /// <summary>Mana each gaze costs.</summary>
    public int Mana { get; init; }
}

/// <summary>
/// A beast pen's numbers (see <see cref="BuildingKind.Pen"/>).
/// </summary>
public sealed class PenDef
{
    public int Keeps { get; init; }
    public int Speed { get; init; }
    public int Trample { get; init; }
    public int DownTicks { get; init; }
    public int Smash { get; init; }
    public int Run { get; init; }
    public int TiredTicks { get; init; }
    public int Slow { get; init; }
}
