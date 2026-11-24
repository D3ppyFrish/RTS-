using System;
using Rts.Sim.Economy;

namespace Rts.Sim.Units;

/// <summary>How a unit is moving. Jobs (<see cref="UnitJob"/>) move their units with Move orders too.</summary>
public enum UnitOrder : byte
{
    Idle = 0,
    Move = 1,
    /// <summary>
    /// Walking after its combat target (see <see cref="CombatSystem"/>): like Move, but it does not end on
    /// arrival; the combat system stops it once the target is in reach.
    /// </summary>
    Chase = 2,
}

/// <summary>What a unit does about enemies (see <see cref="CombatSystem"/>). Units on a job or a plain move do not fight.</summary>
public enum CombatOrder : byte
{
    /// <summary>A soldier's default: when it stands, it fights enemies it spots or that hit it.</summary>
    Auto = 0,
    /// <summary>Attacks <see cref="UnitStore.Target"/> or <see cref="UnitStore.TargetBuilding"/>, wherever it goes, until it is gone.</summary>
    Attack,
    /// <summary>Walks to <see cref="UnitStore.DestX"/>, Y, Z, fighting the enemies it spots on the way.</summary>
    AttackMove,
    /// <summary>
    /// Patrols: walks to <see cref="UnitStore.DestX"/>, Y, Z as on an attack-move, then back to
    /// <see cref="UnitStore.PatrolX"/>, Y, Z, and so on, until it is given another order.
    /// </summary>
    Patrol,
    /// <summary>
    /// Guards a unit (<see cref="UnitStore.GuardUnit"/>) or a building (<see cref="UnitStore.GuardBuilding"/>) of
    /// its owner: it keeps near it, follows it, and fights the enemies that come near it (see
    /// <see cref="Rts.Sim.Units.CombatSystem"/>), until it is given another order or the charge is gone.
    /// </summary>
    Guard,
}

/// <summary>
/// How a soldier fights when it has no attack order: its standing rule, set by the player
/// (see <see cref="CombatSystem"/> and <see cref="Automation.AutomationSystem"/>).
/// </summary>
public enum UnitStance : byte
{
    /// <summary>Goes after enemies it sees and those that hit it (the default).</summary>
    Aggressive = 0,
    /// <summary>Holds its ground: fights only enemies it can reach from where it stands, and never chases.</summary>
    Hold,
    /// <summary>
    /// Retreat when hurt: fights like an aggressive unit until a hit leaves it with a third of its health or
    /// less; then it walks back to its town hall, and from then on holds its ground.
    /// </summary>
    Retreat,
}

/// <summary>Work a unit is doing, on top of its movement (see <see cref="Economy.EconomySystem"/>).</summary>
public enum UnitJob : byte
{
    None = 0,
    /// <summary>Dig, chop or pick a block (<see cref="UnitStore.JobX"/>, Y, Z), carry the goods to storage, repeat nearby.</summary>
    Gather,
    /// <summary>Build the site <see cref="UnitStore.JobTarget"/>.</summary>
    Build,
    /// <summary>Work the farm <see cref="UnitStore.JobTarget"/> and carry the food to storage.</summary>
    Farm,
    /// <summary>Supply the smelter <see cref="UnitStore.JobTarget"/> from storage and carry its iron back.</summary>
    Haul,
    /// <summary>Take what it carries to the storage <see cref="UnitStore.JobStore"/>, then stop.</summary>
    Deliver,
    /// <summary>Capture the enemy building <see cref="UnitStore.JobTarget"/>.</summary>
    Capture,
    /// <summary>Burn the enemy building <see cref="UnitStore.JobTarget"/>.</summary>
    Burn,
    /// <summary>A sapper digs a tunnel to level <see cref="UnitStore.JobY"/> of column (JobX, JobZ) (see <see cref="CombatSystem"/>).</summary>
    Tunnel,
    /// <summary>
    /// A caravan trades with the city-state town <see cref="UnitStore.JobTarget"/>: it fetches a load from
    /// storage, sells it at the town's market and brings back what it got (see <see cref="Economy.CityStateSystem"/>).
    /// While it carries a load, <see cref="UnitStore.JobX"/> is the length of its route.
    /// </summary>
    Trade,
    /// <summary>
    /// Dig out the dig area <see cref="UnitStore.JobTarget"/> (see <see cref="Economy.DigArea"/>), working on
    /// block (<see cref="UnitStore.JobX"/>, Y, Z) of kind <see cref="UnitStore.JobBlock"/>. Workers and sappers.
    /// </summary>
    Dig,
    /// <summary>
    /// Clean blood off the ground (see <see cref="World.BloodSystem"/>) around column <see cref="UnitStore.JobTarget"/>
    /// (x * SizeZ + z), scrubbing the stain on column (<see cref="UnitStore.JobX"/>, JobZ) at level JobY. Workers.
    /// </summary>
    Clean,
    /// <summary>
    /// Go up onto the wall or gate <see cref="UnitStore.JobTarget"/> (see <see cref="PostCommand"/>): walk to its
    /// foot, then climb. The job ends on top. Ranged units.
    /// </summary>
    Post,
    /// <summary>
    /// Supply duty at the storage building <see cref="UnitStore.JobTarget"/> (see <see cref="SupplyCommand"/>):
    /// fetch the good it is shortest of from the owner's other storage (<see cref="UnitStore.JobStore"/>, the good
    /// in <see cref="UnitStore.JobX"/>) and carry it there, again and again. Workers.
    /// </summary>
    Supply,
    /// <summary>
    /// Work the quarry or mine <see cref="UnitStore.JobTarget"/> (see <see cref="Economy.BuildingKind.Quarry"/>) and
    /// carry what each round gives to storage; <see cref="UnitStore.JobX"/> counts its rounds there, which says
    /// which of the building's goods the next round gives.
    /// </summary>
    Quarry,
    /// <summary>
    /// Heap up the ramp <see cref="UnitStore.JobTarget"/> (see <see cref="Economy.Ramp"/>): fill its next block with
    /// earth from a spot beside it, again and again. Workers and sappers.
    /// </summary>
    Ramp,
    /// <summary>
    /// A caravan trades at the town hall <see cref="UnitStore.JobTarget"/> of a trade partner (see
    /// <see cref="TradeDealSystem"/>): it fetches a load of food from storage, sells it at the hall and brings back
    /// gold. While it carries a load, <see cref="UnitStore.JobX"/> is the length of its route.
    /// </summary>
    Market,
    /// <summary>
    /// Build the dam <see cref="UnitStore.JobTarget"/> (see <see cref="Economy.Dam"/>): fill its next block with
    /// earth (or the sluice's boards) from a spot beside it, again and again. Workers and sappers.
    /// </summary>
    Dam,
    /// <summary>
    /// Raise the siege ladder <see cref="UnitStore.JobTarget"/> (see <see cref="Economy.Ladder"/>): put in its next rung
    /// from its foot, again and again until it is up. Workers and sappers.
    /// </summary>
    Ladder,
    /// <summary>
    /// Walk aboard the boat <see cref="UnitStore.JobTarget"/> (see <see cref="BoatSystem"/>): to the shore spot
    /// (<see cref="UnitStore.JobX"/>, JobZ) beside it, then step aboard. JobY is the boat's column when that spot was chosen.
    /// </summary>
    Board,
    /// <summary>
    /// A boat sails to the water by column (<see cref="UnitStore.JobX"/>, JobZ) nearest to it and puts whoever is aboard
    /// ashore there (see <see cref="BoatSystem"/>).
    /// </summary>
    Land,
    /// <summary>
    /// Fell the tree standing in column (<see cref="UnitStore.JobX"/>, JobZ), its trunk starting at level JobY, so it
    /// falls the way <see cref="UnitStore.JobTarget"/> says (0 +x, 1 -x, 2 +z, 3 -z; see <see cref="FellCommand"/>).
    /// </summary>
    Fell,
    /// <summary>Sound the ground at column (<see cref="UnitStore.JobX"/>, JobZ) (see <see cref="ProspectCommand"/>).</summary>
    Prospect,
    /// <summary>
    /// A pack pony brings goods out to the far site <see cref="UnitStore.JobTarget"/> (see <see cref="Economy.EconomySystem.StepBring"/>):
    /// fetches the good in <see cref="UnitStore.JobX"/> from storage <see cref="UnitStore.JobStore"/>, then carries it out.
    /// JobStore stays set while it carries goods for the site.
    /// </summary>
    Bring,
}

/// <summary>Where a unit is in its job's cycle.</summary>
public enum JobPhase : byte
{
    /// <summary>Walking to the work: the block, or the job's building.</summary>
    ToWork = 0,
    /// <summary>At the work, working (or holding position for a raid).</summary>
    Working,
    /// <summary>Walking to storage <see cref="UnitStore.JobStore"/> to drop off what it carries.</summary>
    ToStore,
    /// <summary>Walking to storage <see cref="UnitStore.JobStore"/> to pick up goods for the job's building.</summary>
    Fetch,
    /// <summary>Nothing to do right now; checks again shortly.</summary>
    Wait,
}

/// <summary>
/// All units, stored as parallel arrays (structure of arrays) so the per-tick systems walk memory
/// in order and thousands of units stay cheap. A unit keeps the same <see cref="Id"/> for life;
/// its array index can change when another unit is removed (the last unit moves into the gap).
///
/// Positions: <see cref="X"/>/<see cref="Z"/> are horizontal block coordinates in fixed point
/// (a block's centre is at +0.5); <see cref="Y"/> is the level the unit's feet occupy, i.e. it
/// stands on the solid block at Y - 1.
/// </summary>
public sealed class UnitStore
{
    public int Count { get; private set; }

    public int[] Id = Array.Empty<int>();
    public int[] Owner = Array.Empty<int>();
    public int[] Def = Array.Empty<int>();
    public Fix64[] X = Array.Empty<Fix64>();
    public Fix64[] Z = Array.Empty<Fix64>();
    public int[] Y = Array.Empty<int>();
    /// <summary>Position at the start of the current tick, for smooth drawing between ticks. Not part of the state hash.</summary>
    public Fix64[] PrevX = Array.Empty<Fix64>();
    public Fix64[] PrevZ = Array.Empty<Fix64>();
    public int[] PrevY = Array.Empty<int>();
    public int[] Hp = Array.Empty<int>();
    public UnitOrder[] Order = Array.Empty<UnitOrder>();
    public int[] OrderX = Array.Empty<int>();
    public int[] OrderZ = Array.Empty<int>();
    /// <summary>Units ordered together share a group; used to settle crowds at the destination.</summary>
    public int[] Group = Array.Empty<int>();
    public Fix64[] ArriveRadius = Array.Empty<Fix64>();
    /// <summary>Ticks in a row the unit could not make progress.</summary>
    public int[] Blocked = Array.Empty<int>();
    /// <summary>Flow field the unit's move order follows (see <see cref="PathService"/>), or 0.</summary>
    public int[] Field = Array.Empty<int>();

    // Formations (see Formations and UnitMovement).
    /// <summary>
    /// The unit's last move was a formation move: it walks to its own place (<see cref="SlotX"/>, <see cref="SlotZ"/>)
    /// rather than to the order's column, which is where the formation's middle is. Stays set after it gets
    /// there, for the way it faces, until it is given another order.
    /// </summary>
    public bool[] Formation = Array.Empty<bool>();
    /// <summary>Where the unit's place in its formation is (block coordinates, a block's centre at +0.5).</summary>
    public Fix64[] SlotX = Array.Empty<Fix64>();
    public Fix64[] SlotZ = Array.Empty<Fix64>();
    /// <summary>Which way the formation faces (a unit vector), or zero.</summary>
    public Fix64[] FaceX = Array.Empty<Fix64>();
    public Fix64[] FaceZ = Array.Empty<Fix64>();
    /// <summary>Fastest the unit walks on its formation move (blocks per tick, before roads): the slowest unit's speed.</summary>
    public Fix64[] SpeedCap = Array.Empty<Fix64>();
    /// <summary>
    /// How far (blocks per tick, across) the unit slid last tick on ice: on ice it keeps most of it, so it is
    /// slow to get going, to turn and to stop (see <see cref="UnitMovement"/>). Zero off ice.
    /// </summary>
    public Fix64[] SlideX = Array.Empty<Fix64>(), SlideZ = Array.Empty<Fix64>();
    /// <summary>Ticks left in which a formation unit that got stuck keeping its place follows its own way instead.</summary>
    public int[] Loose = Array.Empty<int>();

    public UnitJob[] Job = Array.Empty<UnitJob>();
    public JobPhase[] Phase = Array.Empty<JobPhase>();
    /// <summary>The job's building id (site, farm, smelter, raid target), the town a caravan trades with, or 0.</summary>
    public int[] JobTarget = Array.Empty<int>();
    /// <summary>Gather: the block being worked. Haul while fetching: X is the good to fetch. Trade: X is the route's length.</summary>
    public int[] JobX = Array.Empty<int>();
    public int[] JobY = Array.Empty<int>();
    public int[] JobZ = Array.Empty<int>();
    /// <summary>Gather: the kind of block being gathered, so the worker can find more of it nearby.</summary>
    public BlockType[] JobBlock = Array.Empty<BlockType>();
    /// <summary>Ticks of work done on the current block or harvest, or ticks left to wait.</summary>
    public int[] JobTimer = Array.Empty<int>();
    /// <summary>Storage building the unit is heading to, or 0.</summary>
    public int[] JobStore = Array.Empty<int>();
    /// <summary>Times in a row the unit's walk to its work ended short of it.</summary>
    public int[] JobRetry = Array.Empty<int>();
    /// <summary>Gather: blocks in a row the unit could not get to (see <see cref="Economy.EconomySystem.MaxMisses"/>).</summary>
    public int[] JobMisses = Array.Empty<int>();
    public Good[] Carry = Array.Empty<Good>();
    public int[] CarryAmount = Array.Empty<int>();

    // Combat (see CombatSystem).
    public CombatOrder[] Combat = Array.Empty<CombatOrder>();
    /// <summary>The unit it is fighting (id), or 0.</summary>
    public int[] Target = Array.Empty<int>();
    /// <summary>The building it is attacking (id), or 0.</summary>
    public int[] TargetBuilding = Array.Empty<int>();
    /// <summary>Where an attack-move ends: column (DestX, DestZ) at level DestY.</summary>
    public int[] DestX = Array.Empty<int>();
    public int[] DestZ = Array.Empty<int>();
    public int[] DestY = Array.Empty<int>();
    /// <summary>On a patrol, the other end of it (where it walks back to from DestX, DestZ, DestY).</summary>
    public int[] PatrolX = Array.Empty<int>();
    public int[] PatrolZ = Array.Empty<int>();
    public int[] PatrolY = Array.Empty<int>();
    /// <summary>
    /// On a patrol: 1 once its walk to the end it is heading for has ended (it turns round), plus 2 for every
    /// walk in a row that ended short of its end (see <see cref="CombatSystem"/>). 0 otherwise.
    /// </summary>
    public int[] PatrolState = Array.Empty<int>();
    /// <summary>On a guard order, the unit (id) it guards, or 0.</summary>
    public int[] GuardUnit = Array.Empty<int>();
    /// <summary>On a guard order, the building (id) it guards, or 0.</summary>
    public int[] GuardBuilding = Array.Empty<int>();
    /// <summary>Ticks until it can hit again.</summary>
    public int[] Cooldown = Array.Empty<int>();
    /// <summary>0 to <see cref="CombatSystem.MaxMorale"/>: falls when allies die nearby; the unit routs when it breaks.</summary>
    public int[] Morale = Array.Empty<int>();
    /// <summary>Ticks of routing left: it runs and ignores orders until then. 0 when it is not routing.</summary>
    public int[] Rout = Array.Empty<int>();
    /// <summary>Chases in a row that ended out of reach of the target.</summary>
    public int[] ChaseRetry = Array.Empty<int>();
    /// <summary>
    /// The building (id) it was sent to attack and could not get to, while it breaks through a wall in the way
    /// (see <see cref="CombatSystem"/>): it goes back to that building when the wall is down. 0 otherwise.
    /// </summary>
    public int[] BreachFor = Array.Empty<int>();
    /// <summary>Ticks before it looks for a fight again, after giving up on one.</summary>
    public int[] CombatWait = Array.Empty<int>();
    /// <summary>The nearest unit (id) that hit it in the last tick, or 0.</summary>
    public int[] HitBy = Array.Empty<int>();
    /// <summary>Its last look at its target found no clear line to shoot along.</summary>
    public bool[] LosBlocked = Array.Empty<bool>();
    public UnitStance[] Stance = Array.Empty<UnitStance>();
    /// <summary>The tick it last hit something or was hit, or 0 if it never has: once out of the fight for a while, it heals on its own.</summary>
    public long[] LastFight = Array.Empty<long>();
    /// <summary>Hundredths of a hit point it has healed on its own toward the next whole one.</summary>
    public Fix64[] Mending = Array.Empty<Fix64>();

    // Automation (see Automation.AutomationSystem).
    /// <summary>A worker that takes the nearest needed job on its own whenever it has nothing to do.</summary>
    public bool[] Auto = Array.Empty<bool>();
    /// <summary>
    /// The player has moved, stopped or sent it to fight since its last job: automation leaves the worker
    /// where it is until it is given a job or switched to automatic again.
    /// </summary>
    public bool[] Held = Array.Empty<bool>();
    /// <summary>
    /// Its priority list (see <see cref="Automation.WorkPriorities"/>), or
    /// <see cref="Automation.WorkPriorities.UseDefault"/> to follow its player's default list.
    /// </summary>
    public ulong[] Priorities = Array.Empty<ulong>();

    // Evolution (see EvolutionSystem).
    /// <summary>Experience earned from fighting, building and staying alive, for factions whose units earn it.</summary>
    public int[] Xp = Array.Empty<int>();

    // Monsters (see MonsterSystem).
    /// <summary>
    /// A wild monster a taming faction beat: it is its new owner's, but lies low (no orders, no fighting)
    /// until they have it ridden or recruited.
    /// </summary>
    public bool[] Subdued = Array.Empty<bool>();
    /// <summary>The relic the unit bears (its index in <see cref="Content.ContentDb.Relics"/> plus one), or 0 for none.</summary>
    public short[] Relic = Array.Empty<short>();
    /// <summary>
    /// The boat the unit rides in (its id, see <see cref="BoatSystem"/>), or 0 on foot. A unit aboard goes where the boat
    /// goes and takes no orders until it is put ashore.
    /// </summary>
    public int[] Aboard = Array.Empty<int>();

    // City-states (see Economy.CityStateSystem).
    /// <summary>
    /// The city-state town the unit serves (its id), or 0 for a player's own unit: one of the town's guards,
    /// or a caravan carrying its tribute. Such units belong to whoever holds the town (the wild side while
    /// nobody does), take no orders, need no housing and eat nothing.
    /// </summary>
    public int[] Town = Array.Empty<int>();

    /// <summary>
    /// How hurt each part of each unit is: the hit points lost there and not yet mended, at most 255, at
    /// <c>i * Body.Parts + (int)part</c> (see <see cref="Wound(int, BodyPart)"/>). Healing mends them.
    /// </summary>
    public byte[] Wounds = Array.Empty<byte>();
    /// <summary>Which of the unit's parts have a bleeding wound: bit <c>1 &lt;&lt; (int)part</c>, set by slash and pierce hits, cleared when the part mends.</summary>
    public byte[] Bleeding = Array.Empty<byte>();
    /// <summary>
    /// Hit points each part's bleeding wound has still to lose before it clots on its own, at
    /// <c>i * Body.Parts + (int)part</c>; above 0 exactly when the part's <see cref="Bleeding"/> bit is set.
    /// </summary>
    public byte[] BleedLeft = Array.Empty<byte>();
    /// <summary>
    /// Which of its bleeding wounds are grievous (bit <c>1 &lt;&lt; (int)part</c>): a lost limb or the like (#73).
    /// They bleed fast and never clot on their own; only a healer or getting home stops them.
    /// </summary>
    public byte[] Grievous = Array.Empty<byte>();
    /// <summary>The unit (id) whose blow last made it bleed, or 0: it gets the credit if the unit bleeds to death.</summary>
    public int[] Cutter = Array.Empty<int>();
    /// <summary>
    /// Armour the unit has on each part on top of its type's (<see cref="UnitDef.Armour"/>), at
    /// <c>i * Body.Parts + (int)part</c>: a piece found or given to this one unit. It keeps it when it evolves.
    /// </summary>
    public byte[] ExtraArmour = Array.Empty<byte>();
    /// <summary>
    /// For a unit that bursts out of the ground (<see cref="UnitDef.Burst"/>): whether it was underground at the last
    /// look (<see cref="CombatSystem.StepBurst"/>), and the ticks its blows still deal more since it came up.
    /// </summary>
    public bool[] Burrowed = Array.Empty<bool>();
    public int[] BurstLeft = Array.Empty<int>();
    /// <summary>
    /// The unit stands in a shield wall (see <see cref="CombatSystem"/>, <see cref="ShieldWallCommand"/>), facing
    /// (<see cref="WallX"/>, <see cref="WallZ"/>), a unit vector or near enough, fixed when it raised the wall.
    /// </summary>
    public bool[] ShieldWall = Array.Empty<bool>();
    public Fix64[] WallX = Array.Empty<Fix64>(), WallZ = Array.Empty<Fix64>();
    /// <summary>
    /// Ticks left in which the unit cannot walk: it is lowering its shield wall, or a rider thrown back by one.
    /// </summary>
    public int[] Stunned = Array.Empty<int>();

    public int Wound(int i, BodyPart part) => Wounds[i * Body.Parts + (int)part];
    public bool IsBleeding(int i, BodyPart part) => (Bleeding[i] & (1 << (int)part)) != 0;
    public int Extra(int i, BodyPart part) => ExtraArmour[i * Body.Parts + (int)part];
    public int Bleed(int i, BodyPart part) => BleedLeft[i * Body.Parts + (int)part];

    /// <summary>The part stops bleeding (it clotted, mended or was stemmed).</summary>
    public void StopBleeding(int i, int part)
    {
        Bleeding[i] &= (byte)~(1 << part);
        Grievous[i] &= (byte)~(1 << part);
        BleedLeft[i * Body.Parts + part] = 0;
    }

    /// <summary>All of the unit's wounds stop bleeding.</summary>
    public void StopBleeding(int i)
    {
        Bleeding[i] = 0;
        Grievous[i] = 0;
        Array.Clear(BleedLeft, i * Body.Parts, Body.Parts);
    }

    private int[] _indexById = new int[64];
    private int _nextId = 1;

    public int Capacity => Id.Length;

    /// <summary>Adds a unit and returns its id.</summary>
    public int Spawn(int owner, int def, Fix64 x, int y, Fix64 z, int hp)
    {
        if (Count == Capacity) Grow(Math.Max(64, Capacity * 2));
        int i = Count++;
        int id = _nextId++;
        if (id >= _indexById.Length) Array.Resize(ref _indexById, Math.Max(id + 1, _indexById.Length * 2));
        _indexById[id] = i + 1; // stored +1 so 0 means "no unit"
        Id[i] = id;
        Owner[i] = owner;
        Def[i] = def;
        X[i] = PrevX[i] = x;
        Z[i] = PrevZ[i] = z;
        Y[i] = PrevY[i] = y;
        Hp[i] = hp;
        Order[i] = UnitOrder.Idle;
        OrderX[i] = OrderZ[i] = 0;
        Group[i] = 0;
        ArriveRadius[i] = Fix64.Zero;
        Blocked[i] = 0;
        Field[i] = 0;
        ClearFormation(i);
        Job[i] = UnitJob.None;
        Phase[i] = JobPhase.ToWork;
        JobTarget[i] = JobX[i] = JobY[i] = JobZ[i] = JobTimer[i] = JobStore[i] = JobRetry[i] = JobMisses[i] = 0;
        JobBlock[i] = BlockType.Air;
        Carry[i] = Good.Food;
        CarryAmount[i] = 0;
        Combat[i] = CombatOrder.Auto;
        Target[i] = TargetBuilding[i] = DestX[i] = DestZ[i] = DestY[i] = 0;
        PatrolX[i] = PatrolZ[i] = PatrolY[i] = PatrolState[i] = 0;
        GuardUnit[i] = GuardBuilding[i] = 0;
        Cooldown[i] = Rout[i] = ChaseRetry[i] = CombatWait[i] = HitBy[i] = BreachFor[i] = 0;
        Morale[i] = CombatSystem.MaxMorale;
        LosBlocked[i] = false;
        Stance[i] = UnitStance.Aggressive;
        LastFight[i] = 0;
        Mending[i] = Fix64.Zero;
        Auto[i] = Held[i] = false;
        Priorities[i] = 0;
        Xp[i] = 0;
        Subdued[i] = false;
        Relic[i] = 0;
        Aboard[i] = 0;
        Town[i] = 0;
        Array.Clear(Wounds, i * Body.Parts, Body.Parts);
        Array.Clear(ExtraArmour, i * Body.Parts, Body.Parts);
        StopBleeding(i);
        Cutter[i] = 0;
        Burrowed[i] = false;
        BurstLeft[i] = 0;
        ShieldWall[i] = false;
        WallX[i] = WallZ[i] = Fix64.Zero;
        Stunned[i] = 0;
        SlideX[i] = SlideZ[i] = Fix64.Zero;
        return id;
    }

    /// <summary>The unit is no longer on a formation move: it walks to its order's column and faces where it walks.</summary>
    public void ClearFormation(int i)
    {
        Formation[i] = false;
        SlotX[i] = SlotZ[i] = FaceX[i] = FaceZ[i] = SpeedCap[i] = Fix64.Zero;
        Loose[i] = 0;
    }

    /// <summary>True if the unit walks somewhere fighting what it meets: an attack-move or a patrol.</summary>
    public bool FightsOnTheWay(int i) => Combat[i] is CombatOrder.AttackMove or CombatOrder.Patrol or CombatOrder.Guard;

    /// <summary>Back to the default of fighting what it meets while it stands: no target and no attack order.</summary>
    public void ClearCombat(int i)
    {
        Combat[i] = CombatOrder.Auto;
        Target[i] = TargetBuilding[i] = BreachFor[i] = 0;
        ChaseRetry[i] = CombatWait[i] = 0;
        PatrolX[i] = PatrolZ[i] = PatrolY[i] = PatrolState[i] = 0;
        GuardUnit[i] = GuardBuilding[i] = 0;
        LosBlocked[i] = false;
    }

    /// <summary>Ends the unit's job (what it carries stays with it).</summary>
    public void ClearJob(int i)
    {
        Job[i] = UnitJob.None;
        Phase[i] = JobPhase.ToWork;
        JobTarget[i] = JobX[i] = JobY[i] = JobZ[i] = JobTimer[i] = JobStore[i] = JobRetry[i] = JobMisses[i] = 0;
        JobBlock[i] = BlockType.Air;
    }

    /// <summary>Array index of a unit, or -1 if it does not exist (never did, or was removed).</summary>
    public int IndexOf(int id) => id > 0 && id < _indexById.Length ? _indexById[id] - 1 : -1;

    public bool Exists(int id) => IndexOf(id) >= 0;

    /// <summary>Removes the unit at an index; the last unit takes its place.</summary>
    public void RemoveAt(int index)
    {
        if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
        _indexById[Id[index]] = 0;
        int last = --Count;
        if (index != last)
        {
            CopySlot(last, index);
            _indexById[Id[index]] = index + 1;
        }
    }

    public void Remove(int id)
    {
        int i = IndexOf(id);
        if (i >= 0) RemoveAt(i);
    }

    /// <summary>Remembers this tick's starting positions for interpolation.</summary>
    public void SavePrevious()
    {
        Array.Copy(X, PrevX, Count);
        Array.Copy(Z, PrevZ, Count);
        Array.Copy(Y, PrevY, Count);
    }

    /// <summary>FNV-1a over every field that affects the game.</summary>
    public ulong ComputeHash()
    {
        ulong h = 14695981039346656037UL;
        void Mix(long v) { h = (h ^ (ulong)v) * 1099511628211UL; }
        Mix(Count);
        Mix(_nextId);
        for (int i = 0; i < Count; i++)
        {
            Mix(Id[i]); Mix(Owner[i]); Mix(Def[i]);
            Mix(X[i].Raw); Mix(Z[i].Raw); Mix(Y[i]);
            Mix(Hp[i]); Mix((int)Order[i]); Mix(OrderX[i]); Mix(OrderZ[i]);
            Mix(Group[i]); Mix(ArriveRadius[i].Raw); Mix(Blocked[i]); Mix(Field[i]);
            Mix(Formation[i] ? 1 : 0); Mix(SlotX[i].Raw); Mix(SlotZ[i].Raw); Mix(FaceX[i].Raw); Mix(FaceZ[i].Raw);
            Mix(SpeedCap[i].Raw); Mix(Loose[i]); Mix(SlideX[i].Raw); Mix(SlideZ[i].Raw);
            Mix((int)Job[i]); Mix((int)Phase[i]); Mix(JobTarget[i]); Mix(JobX[i]); Mix(JobY[i]); Mix(JobZ[i]);
            Mix((int)JobBlock[i]); Mix(JobTimer[i]); Mix(JobStore[i]); Mix(JobRetry[i]); Mix(JobMisses[i]);
            Mix((int)Carry[i]); Mix(CarryAmount[i]);
            Mix((int)Combat[i]); Mix(Target[i]); Mix(TargetBuilding[i]); Mix(DestX[i]); Mix(DestZ[i]); Mix(DestY[i]);
            Mix(PatrolX[i]); Mix(PatrolZ[i]); Mix(PatrolY[i]); Mix(PatrolState[i]); Mix(GuardUnit[i]); Mix(GuardBuilding[i]);
            Mix(Cooldown[i]); Mix(Morale[i]); Mix(Rout[i]); Mix(ChaseRetry[i]); Mix(CombatWait[i]); Mix(HitBy[i]); Mix(BreachFor[i]);
            Mix(LosBlocked[i] ? 1 : 0); Mix((int)Stance[i]); Mix(LastFight[i]); Mix(Mending[i].Raw);
            Mix(Auto[i] ? 1 : 0); Mix(Held[i] ? 1 : 0); Mix((long)Priorities[i]);
            Mix(Xp[i]); Mix(Subdued[i] ? 1 : 0); Mix(Town[i]); Mix(Relic[i]); Mix(Aboard[i]);
            for (int p = 0; p < Body.Parts; p++)
                Mix(Wounds[i * Body.Parts + p] | ExtraArmour[i * Body.Parts + p] << 8 | BleedLeft[i * Body.Parts + p] << 16);
            Mix(Bleeding[i]); Mix(Grievous[i]); Mix(Cutter[i]);
            Mix(Burrowed[i] ? 1 : 0); Mix(BurstLeft[i]);
            Mix(ShieldWall[i] ? 1 : 0); Mix(WallX[i].Raw); Mix(WallZ[i].Raw); Mix(Stunned[i]);
        }
        return h;
    }

    private void CopySlot(int from, int to)
    {
        Id[to] = Id[from]; Owner[to] = Owner[from]; Def[to] = Def[from];
        X[to] = X[from]; Z[to] = Z[from]; Y[to] = Y[from];
        PrevX[to] = PrevX[from]; PrevZ[to] = PrevZ[from]; PrevY[to] = PrevY[from];
        Hp[to] = Hp[from]; Order[to] = Order[from]; OrderX[to] = OrderX[from]; OrderZ[to] = OrderZ[from];
        Group[to] = Group[from]; ArriveRadius[to] = ArriveRadius[from]; Blocked[to] = Blocked[from];
        Field[to] = Field[from];
        Formation[to] = Formation[from]; SlotX[to] = SlotX[from]; SlotZ[to] = SlotZ[from];
        FaceX[to] = FaceX[from]; FaceZ[to] = FaceZ[from]; SpeedCap[to] = SpeedCap[from]; Loose[to] = Loose[from]; SlideX[to] = SlideX[from]; SlideZ[to] = SlideZ[from];
        Job[to] = Job[from]; Phase[to] = Phase[from]; JobTarget[to] = JobTarget[from];
        JobX[to] = JobX[from]; JobY[to] = JobY[from]; JobZ[to] = JobZ[from]; JobBlock[to] = JobBlock[from];
        JobTimer[to] = JobTimer[from]; JobStore[to] = JobStore[from]; JobRetry[to] = JobRetry[from]; JobMisses[to] = JobMisses[from];
        Carry[to] = Carry[from]; CarryAmount[to] = CarryAmount[from];
        Combat[to] = Combat[from]; Target[to] = Target[from]; TargetBuilding[to] = TargetBuilding[from];
        DestX[to] = DestX[from]; DestZ[to] = DestZ[from]; DestY[to] = DestY[from];
        PatrolX[to] = PatrolX[from]; PatrolZ[to] = PatrolZ[from]; PatrolY[to] = PatrolY[from]; PatrolState[to] = PatrolState[from];
        GuardUnit[to] = GuardUnit[from]; GuardBuilding[to] = GuardBuilding[from];
        Cooldown[to] = Cooldown[from]; Morale[to] = Morale[from]; Rout[to] = Rout[from];
        ChaseRetry[to] = ChaseRetry[from]; CombatWait[to] = CombatWait[from]; HitBy[to] = HitBy[from]; BreachFor[to] = BreachFor[from];
        LosBlocked[to] = LosBlocked[from]; Stance[to] = Stance[from]; LastFight[to] = LastFight[from]; Mending[to] = Mending[from];
        Auto[to] = Auto[from]; Held[to] = Held[from]; Priorities[to] = Priorities[from];
        Xp[to] = Xp[from]; Subdued[to] = Subdued[from]; Town[to] = Town[from]; Relic[to] = Relic[from]; Aboard[to] = Aboard[from];
        Array.Copy(Wounds, from * Body.Parts, Wounds, to * Body.Parts, Body.Parts);
        Array.Copy(ExtraArmour, from * Body.Parts, ExtraArmour, to * Body.Parts, Body.Parts);
        Bleeding[to] = Bleeding[from]; Grievous[to] = Grievous[from]; Cutter[to] = Cutter[from];
        Burrowed[to] = Burrowed[from]; BurstLeft[to] = BurstLeft[from];
        ShieldWall[to] = ShieldWall[from]; WallX[to] = WallX[from]; WallZ[to] = WallZ[from]; Stunned[to] = Stunned[from];
        Array.Copy(BleedLeft, from * Body.Parts, BleedLeft, to * Body.Parts, Body.Parts);
    }

    private void Grow(int capacity)
    {
        Array.Resize(ref Id, capacity); Array.Resize(ref Owner, capacity); Array.Resize(ref Def, capacity);
        Array.Resize(ref X, capacity); Array.Resize(ref Z, capacity); Array.Resize(ref Y, capacity);
        Array.Resize(ref PrevX, capacity); Array.Resize(ref PrevZ, capacity); Array.Resize(ref PrevY, capacity);
        Array.Resize(ref Hp, capacity); Array.Resize(ref Order, capacity);
        Array.Resize(ref OrderX, capacity); Array.Resize(ref OrderZ, capacity);
        Array.Resize(ref Group, capacity); Array.Resize(ref ArriveRadius, capacity); Array.Resize(ref Blocked, capacity);
        Array.Resize(ref Field, capacity);
        Array.Resize(ref Formation, capacity); Array.Resize(ref SlotX, capacity); Array.Resize(ref SlotZ, capacity);
        Array.Resize(ref FaceX, capacity); Array.Resize(ref FaceZ, capacity); Array.Resize(ref SpeedCap, capacity); Array.Resize(ref SlideX, capacity); Array.Resize(ref SlideZ, capacity);
        Array.Resize(ref Loose, capacity);
        Array.Resize(ref Job, capacity); Array.Resize(ref Phase, capacity); Array.Resize(ref JobTarget, capacity);
        Array.Resize(ref JobX, capacity); Array.Resize(ref JobY, capacity); Array.Resize(ref JobZ, capacity);
        Array.Resize(ref JobBlock, capacity); Array.Resize(ref JobTimer, capacity); Array.Resize(ref JobStore, capacity);
        Array.Resize(ref JobRetry, capacity); Array.Resize(ref JobMisses, capacity); Array.Resize(ref Carry, capacity); Array.Resize(ref CarryAmount, capacity);
        Array.Resize(ref Combat, capacity); Array.Resize(ref Target, capacity); Array.Resize(ref TargetBuilding, capacity);
        Array.Resize(ref DestX, capacity); Array.Resize(ref DestZ, capacity); Array.Resize(ref DestY, capacity);
        Array.Resize(ref PatrolX, capacity); Array.Resize(ref PatrolZ, capacity); Array.Resize(ref PatrolY, capacity);
        Array.Resize(ref PatrolState, capacity); Array.Resize(ref GuardUnit, capacity); Array.Resize(ref GuardBuilding, capacity);
        Array.Resize(ref Cooldown, capacity); Array.Resize(ref Morale, capacity); Array.Resize(ref Rout, capacity);
        Array.Resize(ref ChaseRetry, capacity); Array.Resize(ref CombatWait, capacity); Array.Resize(ref HitBy, capacity);
        Array.Resize(ref BreachFor, capacity);
        Array.Resize(ref LosBlocked, capacity); Array.Resize(ref Stance, capacity);
        Array.Resize(ref LastFight, capacity); Array.Resize(ref Mending, capacity);
        Array.Resize(ref Auto, capacity); Array.Resize(ref Held, capacity); Array.Resize(ref Priorities, capacity);
        Array.Resize(ref Xp, capacity); Array.Resize(ref Subdued, capacity); Array.Resize(ref Town, capacity);
        Array.Resize(ref Relic, capacity);
        Array.Resize(ref Aboard, capacity);
        Array.Resize(ref Wounds, capacity * Body.Parts); Array.Resize(ref ExtraArmour, capacity * Body.Parts);
        Array.Resize(ref Bleeding, capacity); Array.Resize(ref BleedLeft, capacity * Body.Parts);
        Array.Resize(ref Cutter, capacity); Array.Resize(ref Grievous, capacity);
        Array.Resize(ref Burrowed, capacity); Array.Resize(ref BurstLeft, capacity);
        Array.Resize(ref ShieldWall, capacity); Array.Resize(ref WallX, capacity); Array.Resize(ref WallZ, capacity);
        Array.Resize(ref Stunned, capacity);
    }
}
