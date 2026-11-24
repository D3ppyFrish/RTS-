namespace Rts.Sim;

/// <summary>Terrain block types. Each block is 0.5 m on a side.</summary>
public enum BlockType : byte
{
    Air = 0,
    Grass,
    Soil,
    Sand,
    Stone,
    IronOre,
    Coal,
    Crystal,
    Wood,
    Water,
    Bedrock,
    Leaves,
    Mud,
    /// <summary>Food source: a bush that can be harvested.</summary>
    BerryBush,
    // Building blocks: buildings are made of real blocks, so they block movement and can be damaged.
    /// <summary>Sawn timber walls.</summary>
    Planks,
    /// <summary>Straw roof.</summary>
    Thatch,
    /// <summary>Fired clay roof.</summary>
    RoofTile,
    /// <summary>Dressed stone: foundations, furnaces, chimneys.</summary>
    StoneBrick,
    /// <summary>A building's door (part of the wall).</summary>
    Door,
    /// <summary>A farm's tilled field; ground units walk on.</summary>
    Farmland,
    /// <summary>Road surface: units walking on it move twice as fast.</summary>
    Road,
    // Rubble: what falling ground and wrecked buildings leave (see World.PhysicsSystem). Units walk over it
    // slowly, and workers clear it for what it was made of (see BlockInfo.RubbleOf).
    /// <summary>Broken earth (from soil, grass, sand and mud): clearing it gives nothing.</summary>
    Rubble,
    /// <summary>Broken stone, stone brick and roof tile: clearing it gives stone.</summary>
    StoneRubble,
    /// <summary>Broken iron ore: clearing it gives iron ore.</summary>
    OreRubble,
    /// <summary>Broken coal: clearing it gives coal.</summary>
    CoalRubble,
    /// <summary>Broken timber (planks, doors, stakes, a watchtower's frame): clearing it gives wood.</summary>
    WoodRubble,
    /// <summary>
    /// A gate's leaf, in a wall: solid to everyone but its owner, whose units walk through it (see
    /// <see cref="Economy.BuildingKind.Gate"/> and <see cref="Units.Walk"/>).
    /// </summary>
    Gate,
    /// <summary>
    /// Molten rock, in pools in the hills: nothing stands on it or digs it, and it glows, lighting the ground
    /// around it by night (see <see cref="Conditions"/>).
    /// </summary>
    Lava,
    /// <summary>
    /// A felled tree lying on the ground: logs and branches (see <see cref="World.PhysicsSystem.FellTree"/>).
    /// Rubble like the others (units walk over it slowly); clearing a block gives
    /// <see cref="Economy.Harvest.WoodPerTreeRubble"/> wood.
    /// </summary>
    TreeRubble,
    // Tree kinds besides the broad-leaved tree (Wood and Leaves). Every trunk is cut for wood the same way
    // (see BlockInfo.IsTrunk), and every crown is foliage units see through (see BlockInfo.IsLeaves).
    /// <summary>Pine trunk: dark, rough bark.</summary>
    PineWood,
    /// <summary>Birch trunk: white bark with dark marks.</summary>
    BirchWood,
    /// <summary>A giant mushroom's stalk (swamps, and the deep caves' upper chambers).</summary>
    MushroomStem,
    /// <summary>Pine needles: a dark, tiered cone.</summary>
    PineNeedles,
    /// <summary>Birch leaves: a small, light yellow-green crown.</summary>
    BirchLeaves,
    /// <summary>Willow leaves: curtains that hang down to the ground (swamps).</summary>
    WillowLeaves,
    /// <summary>A dusk tree's violet leaves (rare, in the hills).</summary>
    DuskLeaves,
    /// <summary>A giant mushroom's cap.</summary>
    MushroomCap,
    /// <summary>
    /// The dusk tree's gnarled trunk and roots: one tree on each map, guarded by the Dusk Warden (see
    /// <see cref="Units.DuskTreeSystem"/>). Not a resource: it cannot be cut or dug.
    /// </summary>
    DuskWood,
    /// <summary>The dusk tree's bare, twisting branches and twigs (see <see cref="DuskWood"/>).</summary>
    DuskBranch,
    /// <summary>A wooden barrier's stakes: upright logs set side by side, pointed on top (a building block).</summary>
    Palisade,
    /// <summary>
    /// Dressed stone laid as a rampart: what stone walls, gatehouses and towers are made of (a building block).
    /// As tough as <see cref="StoneBrick"/>; its open tops are drawn with battlements along their outer edges.
    /// </summary>
    Rampart,
    /// <summary>
    /// A watchtower's timber frame: posts, braces and a ladder under a deck of boards with a rail round it (a
    /// building block). As solid as planks to whoever would walk or shoot through it.
    /// </summary>
    Trestle,
    /// <summary>
    /// Water frozen by a mage's Ice Bridge (see <see cref="Units.SpellSystem"/>): units walk on it until it
    /// thaws back into water. Nothing digs it or falls with it.
    /// </summary>
    Ice,
    /// <summary>
    /// The floorboards inside a building, under its room (see <see cref="Economy.BuildingDef.Blocks"/>): a building
    /// block nobody stands on, so units are never shut inside a building or step into it through a gap.
    /// </summary>
    Floor,
    /// <summary>
    /// Poison bog: a sickly crust over the swamp's mud that units can walk on, but that hurts them while
    /// they stand on it (see <see cref="World.HazardSystem"/>). Workers dig it away like mud.
    /// </summary>
    Bog,
    /// <summary>
    /// A sinkhole's crust: a thin, dry, cracked lid of earth over a hollow (see <see cref="World.HazardSystem"/>).
    /// It looks like parched ground and holds until a unit stands on it, then gives way and the unit drops into
    /// the hole. It is held up by its rim, so it never falls on its own. Workers dig it away (it gives nothing).
    /// </summary>
    Crust,
    /// <summary>
    /// Quicksand: wet, loose sand on the beaches and river banks (see <see cref="World.HazardSystem"/>). Units can
    /// walk on it, but it grips them (they move at a third of their speed) and drags at them, hurting them while
    /// they stand on it. It looks like darker, wetter sand. Workers dig it away like sand (it gives nothing).
    /// </summary>
    Quicksand,
    /// <summary>
    /// Scree: loose, broken stones lying at the foot of a cliff in the hills, under rocks that are ready to fall
    /// (see <see cref="LooseRock"/> and <see cref="World.HazardSystem"/>). Units walk on it, but the tick one
    /// steps onto it the rocks above come down on it. Workers dig it away (it gives nothing).
    /// </summary>
    Scree,
    /// <summary>
    /// Loose rock: cracked boulders perched on the brink of a cliff in the hills, over a field of
    /// <see cref="Scree"/>. A unit stepping onto the scree below, or onto the rocks, shakes them loose and they
    /// fall (see <see cref="World.HazardSystem"/>). Workers quarry them for stone.
    /// </summary>
    LooseRock,
    /// <summary>
    /// Gold ore: glinting yellow seams in the hills' rock, a few blocks under the surface and showing where the
    /// ground is cut, and in the walls of the caverns and deep caves (see <see cref="Economy.Good.Gold"/>).
    /// Workers dig it slowly for gold; it falls like stone, and its rubble is stone rubble.
    /// </summary>
    GoldOre,
    /// <summary>
    /// Gloom moss: the dark, cold moss that carpets the shadowed wilds in the far interior instead of grass (see
    /// <see cref="World.Biome.Wilds"/>). Ground like grass in every other way: units walk on it, workers dig it away
    /// (it gives nothing), buildings level it and saplings take root in it.
    /// </summary>
    Gloom,
    /// <summary>A gloom pine's crown (the shadowed wilds): blue-black needles with pale specks that glimmer.</summary>
    GloomLeaves,
    /// <summary>
    /// A rung of a siege ladder leaning on a wall or a cliff (see <see cref="Economy.Ladder"/>): two rails and a rung
    /// against the face. Not solid: units stand in it, see and shoot through it, and nobody stands on it; a unit at
    /// its foot climbs it to the top of what it leans on, and one up there climbs down it (see <see cref="Units.Walk"/>).
    /// </summary>
    Ladder,
    /// <summary>
    /// Hive earth: soil chewed and packed hard with the Deepfolk's resin (a building block). Their buildings are
    /// mounds of it, like ant hills (see <see cref="Economy.BuildingDef.Mound"/>), lumpy and pocked with tunnel
    /// mouths. As tough as planks; it breaks into plain rubble.
    /// </summary>
    Hive,
    /// <summary>
    /// A fruit tree of an orchard (see <see cref="Economy.BuildingKind.Orchard"/>): its crown, trunk or sapling. Not
    /// solid: units push through the branches (slowly, see <see cref="Units.UnitMovement"/>), but it is too thick to see
    /// or shoot through (see <see cref="Units.CombatSystem.ClearLine"/>, <see cref="VisionSystem"/>), so a grown orchard
    /// screens what is behind it like a hedge.
    /// </summary>
    Orchard,
}

public static class BlockInfo
{
    /// <summary>The highest block type value; saved maps may contain any value up to it.</summary>
    public const BlockType Last = BlockType.Orchard;

    /// <summary>Anything but air, water, a ladder's rungs and an orchard's trees (which units stand in).</summary>
    public static bool IsSolid(BlockType t) => t != BlockType.Air && t != BlockType.Water && t != BlockType.Ladder && t != BlockType.Orchard;

    /// <summary>Bedrock, water, ice, lava and the dusk tree cannot be dug.</summary>
    public static bool IsDiggable(BlockType t) => t != BlockType.Air && t != BlockType.Water && t != BlockType.Bedrock && t != BlockType.Lava
        && t != BlockType.Ice && !IsDuskTree(t);

    /// <summary>The dusk tree's wood: its trunk, roots and branches (not its leaves).</summary>
    public static bool IsDuskTree(BlockType t) => t is BlockType.DuskWood or BlockType.DuskBranch;
    /// <summary>Solid ground something can stand on: not lava, nor a building's floor (see <see cref="BlockType.Floor"/>).</summary>
    public static bool CanStandOn(BlockType t) => IsSolid(t) && t != BlockType.Lava && t != BlockType.Floor;

    /// <summary>Blocks that buildings are made of (walls, floors, roofs, doors, gates, stakes, fields and roads).</summary>
    public static bool IsBuilt(BlockType t) => t >= BlockType.Planks && t <= BlockType.Road || t is BlockType.Gate or BlockType.Palisade or BlockType.Rampart or BlockType.Trestle or BlockType.Floor or BlockType.Hive;

    /// <summary>
    /// Damage a building's block takes before it is knocked out (see <see cref="Units.CombatSystem"/>); 0 for
    /// blocks that are not part of buildings (roads, and everything natural). A building can be sturdier than
    /// its blocks (see <see cref="Economy.BuildingDef.Sturdy"/>).
    /// </summary>
    public static int Toughness(BlockType t) => t switch
    {
        BlockType.Planks => 10,
        BlockType.Thatch => 6,
        BlockType.RoofTile => 8,
        BlockType.StoneBrick => 25,
        BlockType.Door => 8,
        BlockType.Farmland => 4,
        BlockType.Gate => 12,
        BlockType.Palisade => 10,
        BlockType.Rampart => 25,
        BlockType.Trestle => 10,
        BlockType.Floor => 6,
        BlockType.Hive => 10,
        _ => 0,
    };

    /// <summary>The least toughness of any building block: less damage than this knocks nothing out.</summary>
    public const int MinToughness = 4;

    /// <summary>
    /// Ground that falls when nothing holds it up (see <see cref="World.PhysicsSystem"/>). Plants hold
    /// themselves up, buildings look after their own blocks, and bedrock never moves.
    /// </summary>
    public static bool CanFall(BlockType t) => t is BlockType.Grass or BlockType.Gloom or BlockType.Soil or BlockType.Sand
        or BlockType.Stone or BlockType.IronOre or BlockType.Coal or BlockType.Crystal or BlockType.GoldOre or BlockType.Mud or BlockType.Bog
        or BlockType.Quicksand or BlockType.Scree or BlockType.LooseRock || IsRubble(t);

    /// <summary>Any tree's trunk: workers cut it for wood, and it topples when its foot is cut.</summary>
    public static bool IsTrunk(BlockType t) => t is BlockType.Wood or BlockType.PineWood or BlockType.BirchWood or BlockType.MushroomStem;

    /// <summary>Any tree's crown: foliage that gives nothing, goes with its tree, and that units see through.</summary>
    public static bool IsLeaves(BlockType t) => t == BlockType.Leaves || t >= BlockType.PineNeedles && t <= BlockType.MushroomCap || t == BlockType.GloomLeaves;

    /// <summary>Any kind of rubble.</summary>
    public static bool IsRubble(BlockType t) => t >= BlockType.Rubble && t <= BlockType.WoodRubble || t == BlockType.TreeRubble;

    /// <summary>
    /// The rubble a block breaks into when it comes down: it keeps what it was made of, so clearing it gives
    /// that back. Thatch and earth break into plain rubble, which gives nothing.
    /// </summary>
    public static BlockType RubbleOf(BlockType t) => t switch
    {
        BlockType.Stone or BlockType.Crystal or BlockType.GoldOre or BlockType.StoneBrick or BlockType.RoofTile or BlockType.Rampart or BlockType.LooseRock => BlockType.StoneRubble,
        BlockType.IronOre => BlockType.OreRubble,
        BlockType.Coal => BlockType.CoalRubble,
        BlockType.Planks or BlockType.Door or BlockType.Gate or BlockType.Palisade or BlockType.Trestle or BlockType.Floor => BlockType.WoodRubble,
        _ when IsTrunk(t) => BlockType.WoodRubble,
        _ when IsRubble(t) => t,
        _ => BlockType.Rubble,
    };

    /// <summary>Natural ground a building site can be levelled through or a road laid on.</summary>
    public static bool IsGround(BlockType t) =>
        t is BlockType.Grass or BlockType.Gloom or BlockType.Soil or BlockType.Sand or BlockType.Mud or BlockType.Bog or BlockType.Quicksand or BlockType.Scree or BlockType.Stone;
}
