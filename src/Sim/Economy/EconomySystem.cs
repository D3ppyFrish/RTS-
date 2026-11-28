using System;
using System.Collections.Generic;
using Rts.Sim.Content;
using Rts.Sim.Units;

namespace Rts.Sim.Economy;

/// <summary>Why a building cannot go where the player wants it.</summary>
public enum PlaceResult : byte
{
    Ok,
    /// <summary>Too close to the edge of the world.</summary>
    OutOfBounds,
    /// <summary>Another building, or a city-state's town, is on or right next to the footprint.</summary>
    Crowded,
    /// <summary>The ground varies by more than one block, or there is no ground.</summary>
    Uneven,
    /// <summary>Water, trees, rocks or other blocks are in the way.</summary>
    Blocked,
    CannotAfford,
    /// <summary>The player lacks a tech it needs, or it is another faction's.</summary>
    Locked,
    /// <summary>A building that must stand at the water's edge (<see cref="BuildingDef.Shore"/>) has no water beside it.</summary>
    NoWater,
    /// <summary>A great work (<see cref="BuildingKind.GreatWork"/>) must stand in the deep caves, under the land.</summary>
    NotDeep,
    /// <summary>The player already has a great work, built, being built or planned: one at a time.</summary>
    OnlyOne,
}

/// <summary>Per-player economy state and running totals.</summary>
public sealed class PlayerEconomy
{
    internal PlayerEconomy(ContentDb content, int player, FactionDef faction)
    {
        Faction = faction;
        Researched = new bool[content.Techs.Count];
        HousingBonus = new int[content.Buildings.Count];
        HpBonus = new int[content.Units.Count];
        ShieldWall = new bool[content.Units.Count];
        Molt = new bool[content.Units.Count];
        Smoke = new bool[content.Units.Count];
        ShadowStep = new bool[content.Units.Count];
        Sabotage = new bool[content.Units.Count];
        Climb = new bool[content.Units.Count];
        FellTicks = new int[content.Units.Count];
        ProspectTicks = new int[content.Units.Count];
        AutoForm = new int[content.Units.Count];
        Array.Fill(AutoForm, -1);
    }

    /// <summary>The player's faction (see <see cref="Simulation.FactionOf"/>).</summary>
    public FactionDef Faction { get; internal set; }
    /// <summary>Workers it trains take jobs on their own (see <see cref="AutomateAllCommand"/>).</summary>
    public bool AutoWorkers { get; internal set; }
    /// <summary>The priority list its workers follow unless they have one of their own (see <see cref="Automation.WorkPriorities"/>).</summary>
    public ulong DefaultPriorities { get; internal set; } = Automation.WorkPriorities.Standard;
    /// <summary>Its destroyed buildings are put back (see <see cref="RebuildCommand"/>).</summary>
    public bool Rebuild { get; internal set; }
    /// <summary>Goods gathered from the world (blocks, farms), by <see cref="Good"/>.</summary>
    public int[] Gathered { get; } = new int[Goods.Count];
    /// <summary>Goods paid for buildings, roads and training.</summary>
    public int[] Spent { get; } = new int[Goods.Count];
    /// <summary>
    /// Mana, channelled from crystal at the player's shrines and spent on spells (see
    /// <see cref="Units.SpellSystem.ManaCost"/>). Not a good: nobody carries it, and it cannot be raided. Like a
    /// building's <see cref="Building.Stock"/>, tests and match setup may set it directly; play changes it only
    /// through shrines and spells.
    /// </summary>
    public int Mana { get; set; } = EconomySystem.StartingMana;
    /// <summary>
    /// Clan honour (Clan Kage's; see <see cref="Content.FactionDef.Honour"/>), earned by contracts carried out, lost by
    /// dishonourable defeats and spent on what costs honour (see <see cref="EconomySystem.Honour"/>). Not a good: nobody
    /// carries it, and it cannot be raided. Tests and match setup may set it directly.
    /// </summary>
    public int Honour { get; set; }
    /// <summary>Clan honour earned this match, before any was lost or spent.</summary>
    public int HonourEarned { get; internal set; }
    /// <summary>Clan honour lost to dishonour this match.</summary>
    public int HonourLost { get; internal set; }
    /// <summary>Mana channelled at the player's shrines this match.</summary>
    public int ManaChannelled { get; internal set; }
    /// <summary>Food gathered in the last minute (see <see cref="EconomySystem.FoodPerMinute"/>).</summary>
    public int FoodGathered { get; internal set; }

    // Gathered food as it stood at each of the last 60 whole seconds, the oldest overwritten each second.
    internal readonly int[] FoodSamples = new int[60];

    // Research: techs had (by index), the one being researched (-1 for none) and the ticks spent on it.
    internal readonly bool[] Researched;
    internal int Researching = -1;
    internal int ResearchTicks;
    // Bonuses from the techs had, recomputed when one is added (see EconomySystem.UpdateBonuses).
    internal bool Roads, SpareWorkers, MurderHoles, Salvage, FieldRepairs, CutWebs, Thorns;
    /// <summary>Ticks out of the fight after which the player's units bind their own wounds, 0 if they cannot (<see cref="TechEffectType.BindWounds"/>).</summary>
    internal int BindTicks;
    /// <summary>How much more (percent) the player's units deal units they bear a grudge against, 0 for no grudges (<see cref="TechEffectType.Grudges"/>).</summary>
    internal int GrudgePercent;
    /// <summary>How many more trees (percent) the player's fairy rings count at most, 0 for none (<see cref="TechEffectType.WildWoods"/>).</summary>
    internal int WildWoodsPercent;
    /// <summary>How much more (percent) the first blow of the player's units lying in wait in the water deals, 0 for none (<see cref="TechEffectType.Ambush"/>).</summary>
    internal int AmbushPercent;
    // The discoveries made (bit 1 << Discovery), which open discovery techs.
    internal byte Discoveries;
    internal readonly int[] HousingBonus, HpBonus;
    /// <summary>Which unit types (by index) can take up a shield wall (see <see cref="TechEffectType.ShieldWall"/>).</summary>
    internal readonly bool[] ShieldWall;
    /// <summary>Which unit types (by index) can molt (see <see cref="TechEffectType.Molt"/>).</summary>
    internal readonly bool[] Molt;
    /// <summary>Which unit types (by index) can throw smoke bombs (see <see cref="TechEffectType.SmokeBombs"/>).</summary>
    internal readonly bool[] Smoke;
    /// <summary>Which unit types (by index) can step through the shadows (see <see cref="TechEffectType.ShadowStep"/>).</summary>
    internal readonly bool[] ShadowStep;
    /// <summary>Which unit types (by index) can sabotage enemy buildings (see <see cref="TechEffectType.Sabotage"/>).</summary>
    internal readonly bool[] Sabotage;
    /// <summary>Which unit types (by index) can climb walls and cliffs (see <see cref="TechEffectType.WallClimbing"/>).</summary>
    internal readonly bool[] Climb;
    /// <summary>
    /// Ticks of work for each unit type (by index) to fell a tree the way it chooses, or to sound the ground; 0 if it
    /// cannot (see <see cref="TechEffectType.Felling"/>, <see cref="TechEffectType.Prospecting"/>).
    /// </summary>
    internal readonly int[] FellTicks, ProspectTicks;
    /// <summary>Damage an aimed tree does to each unit it comes down on, and how far (columns) sounding the ground shows ore.</summary>
    internal int FellCrush, ProspectRadius;
    /// <summary>Times the player has sounded the ground.</summary>
    internal int Soundings;
    /// <summary>The ore, coal, gold and crystal blocks the player's prospectors have found (keys x, y, z: see <see cref="EconomySystem.OreKey"/>).</summary>
    internal readonly SortedSet<long> Prospected = new();
    // The form (unit index) each unit type (by index) takes on its own once it can, or -1 (see EvolutionSystem).
    internal readonly int[] AutoForm;
}

/// <summary>
/// The logistics economy: buildings made of blocks, goods that are carried, and the jobs of the units
/// that carry them. Runs once per tick after commands and before pathfinding and movement.
///
/// Goods only exist in buildings or on units. Workers gather from blocks and farms and carry what they
/// get (up to <see cref="CarryCapacity"/>) to the nearest storehouse; a player's stock is what their
/// storehouses hold, and costs are paid from it. Roads double walking speed. Houses raise the population
/// limit, every unit eats food each minute, and storehouses can be captured or burned by an enemy
/// standing at one with nobody defending it. Techs (see EconomySystem.Research.cs) unlock buildings and
/// units and change some of these numbers. All numbers are starting guesses to be tuned in playtests.
/// </summary>
public sealed partial class EconomySystem
{
    private const int Tps = Simulation.TicksPerSecond;

    /// <summary>Most goods one worker carries, before techs (see <see cref="Capacity"/>).</summary>
    public const int CarryCapacity = 5;
    /// <summary>An owner's unit within this many columns of a building stops raids on it.</summary>
    public const int GuardRadius = 8;
    public const int CaptureTicks = 10 * Tps;
    public const int BurnTicks = 5 * Tps;
    public const int SmeltTicks = 10 * Tps;
    public const int OrePerIngot = 2;
    public const int CoalPerIngot = 1;
    /// <summary>Ticks of farm work for one harvest of <see cref="FarmYield"/> food.</summary>
    public const int FarmTicks = 15 * Tps;
    /// <remarks>6 since Crop rotation gives orchards instead of 2 more food a harvest (issue #148): the base pace makes up some of it.</remarks>
    public const int FarmYield = 6;
    /// <summary>Longest road one command lays, and the stone each road block costs.</summary>
    public const int MaxRoad = 64;
    public const int StonePerRoad = 1;
    /// <summary>Most units waiting in one building's training queue.</summary>
    public const int MaxQueue = 5;
    /// <summary>Walks in a row that may end short of the work before the unit gives up on it.</summary>
    public const int MaxRetries = 3;
    /// <summary>
    /// Blocks in a row a gatherer may fail to get to (it tries the next one nearby each time) before it gives
    /// the job up: without a limit, a worker by a pit it cannot get down into tries the blocks in it forever.
    /// </summary>
    public const int MaxMisses = 3;
    /// <summary>Ticks a unit with nothing to do waits before looking again.</summary>
    public const int WaitTicks = Tps;

    /// <summary>Mana every player has at the start of a match: enough for a spell or two before the first shrine.</summary>
    public const int StartingMana = 50;

    /// <summary>What a player's town hall holds at the start of a match.</summary>
    public static readonly GoodAmount[] StartingGoods =
    {
        new(Good.Food, 200), new(Good.Wood, 150), new(Good.Stone, 50),
    };

    private readonly Simulation _sim;
    private readonly List<PlayerEconomy> _players = new();
    private int[] _population = Array.Empty<int>();
    // Raiders standing at their target this tick: (building id, unit index), in unit order.
    private readonly List<(int Building, int Unit)> _raiders = new();
    // Gather targets claimed by some unit, so workers spread over a resource; rebuilt once a tick when used.
    private readonly HashSet<long> _claimed = new();
    private long _claimedTick = -1;

    public EconomySystem(Simulation sim)
    {
        _sim = sim;
    }

    private VoxelWorld World => _sim.World;
    private UnitStore U => _sim.Units;
    public BuildingStore Buildings => _sim.Buildings;

    public PlayerEconomy Player(int player)
    {
        if (player < 0) throw new ArgumentOutOfRangeException(nameof(player));
        while (_players.Count <= player) _players.Add(new PlayerEconomy(_sim.Content, _players.Count, _sim.FactionOf(_players.Count)));
        return _players[player];
    }

    // --- Queries -----------------------------------------------------------------------------------

    /// <summary>Goods in the player's finished storage buildings, by <see cref="Good"/>.</summary>
    public int[] Stockpile(int player)
    {
        var total = new int[Goods.Count];
        foreach (var b in Buildings.All)
        {
            if (b.Owner != player || !b.Active || !b.Def.Storage) continue;
            for (int g = 0; g < Goods.Count; g++) total[g] += b.Stock[g];
        }
        return total;
    }

    public int Stock(int player, Good good) => Stockpile(player)[(int)good];

    /// <summary>Units the player has (a city-state's guards and tribute caravans, which need no housing, left out).</summary>
    public int Population(int player)
    {
        int n = 0;
        for (int i = 0; i < U.Count; i++)
            if (U.Owner[i] == player && U.Town[i] == 0) n++;
        return n;
    }

    /// <summary>The player's population limit: the housing of their finished buildings.</summary>
    public int Housing(int player)
    {
        int n = 0;
        foreach (var b in Buildings.All)
            if (b.Owner == player && b.Active) n += HousingOf(b);
        return n;
    }

    /// <summary>How much food the player gathered in the last minute (units eat nothing once trained).</summary>
    public int FoodPerMinute(int player) =>
        player >= 0 && player < _players.Count ? _players[player].FoodGathered : 0;

    /// <summary>True if the player's workers are all automatic, those they train included (see <see cref="AutomateAllCommand"/>).</summary>
    public bool AutoWorkers(int player) => player >= 0 && player < _players.Count && _players[player].AutoWorkers;

    /// <summary>True if the player has rebuild orders on (see <see cref="RebuildCommand"/>).</summary>
    public bool Rebuilds(int player) => player >= 0 && player < _players.Count && _players[player].Rebuild;

    /// <summary>
    /// The player's workers with nothing to do, by id: standing with no order and no job (automatic ones too,
    /// while they find no work), not running away and not beaten monsters.
    /// </summary>
    public List<int> IdleWorkers(int player)
    {
        var u = U;
        var ids = new List<int>();
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Owner[i] != player || u.Order[i] != UnitOrder.Idle || u.Job[i] != UnitJob.None) continue;
            if (u.Rout[i] > 0 || u.Subdued[i] || _sim.Content.Units[u.Def[i]].Role != UnitRole.Worker) continue;
            ids.Add(u.Id[i]);
        }
        ids.Sort();
        return ids;
    }

    /// <summary>Players with economy state: every player who owns or has owned something, or has given an economy setting.</summary>
    internal int PlayerCount => _players.Count;

    /// <summary>True if the player is in the match: they have economy state already, or own a unit or a building.</summary>
    internal bool HasPlayer(int player)
    {
        if (player < 0) return false;
        if (player < _players.Count) return true;
        for (int i = 0; i < U.Count; i++)
            if (U.Owner[i] == player) return true;
        foreach (var b in Buildings.All)
            if (b.Owner == player) return true;
        return false;
    }

    /// <summary>True if some worker is gathering block (x, y, z) (for a tree, the base of its trunk) or on its way to.</summary>
    internal bool Claimed(int x, int y, int z)
    {
        EnsureClaims();
        return _claimed.Contains(Harvest.Key(World, x, y, z));
    }

    /// <summary>Hash of the per-player state that affects the game (buildings and units hash themselves).</summary>
    public ulong ComputeHash()
    {
        ulong h = 14695981039346656037UL;
        void Mix(long v) { h = (h ^ (ulong)v) * 1099511628211UL; }
        foreach (var p in _players)
        {
            Mix(p.Faction.Index);
            Mix(p.AutoWorkers ? 1 : 0);
            Mix(p.Rebuild ? 1 : 0);
            Mix((long)p.DefaultPriorities);
            Mix(p.Researching);
            Mix(p.ResearchTicks);
            Mix(p.Mana);
            Mix(p.Honour);
            Mix(p.Discoveries);
            for (int t = 0; t < p.Researched.Length; t++)
                if (p.Researched[t]) Mix(t);
            foreach (int form in p.AutoForm) Mix(form);
        }
        MixDigAreas(ref h);
        MixRamps(ref h);
        MixDams(ref h);
        MixLadders(ref h);
        MixTools(ref h);
        MixSaplings(ref h);
        MixLenses(ref h);
        h = (h ^ (ulong)Sabotages) * 1099511628211UL;
        h = (h ^ (ulong)GoodsSpoiled) * 1099511628211UL;
        h = (h ^ (ulong)Climbs) * 1099511628211UL;
        MixHonour(ref h);
        return h;
    }

    // --- The tick ----------------------------------------------------------------------------------

    public void Step()
    {
        CountPopulation();
        _raiders.Clear();
        _saboteurs.Clear();
        for (int i = 0; i < U.Count; i++)
            if (U.Job[i] != UnitJob.None) StepJob(i);
        PruneDigAreas();
        PruneRamps();
        PruneDams();
        StepLadders();
        StepSaplings();
        StepOrchards();
        StepYards();

        var all = Buildings.All;
        for (int k = 0; k < all.Count; k++)
        {
            var b = all[k];
            if (b.State == BuildingState.Site)
            {
                StepDue(b);
                StepSite(b);
            }
            else if (b.Active && Stalled(b)) { } // sabotaged: it does no work (EconomySystem.Sabotage.cs)
            else if (b.Active)
            {
                if (b.Upgrade != null) StepUpgrade(b);
                if (b.Def.RegrowTicks > 0 && (_sim.Tick + b.Id) % b.Def.RegrowTicks == 0) Regrow(b);
                if (b.Def.Kind == BuildingKind.Smelter) StepSmelter(b);
                else if (b.Def.Kind == BuildingKind.Shrine) { if (b.Def.Nature != null) StepNature(b); else StepShrine(b); }
                else if (b.Def.Kind == BuildingKind.Workshop) StepWorkshop(b);
                if (b.Queue.Count > 0) StepTraining(b);
                if (b.Def.YieldTicks > 0 && (_sim.Tick + b.Id) % b.Def.YieldTicks == 0)
                {
                    if (b.Def.Kind == BuildingKind.Orchard) Bear(b);
                    else BringYield(b);
                }
            }
            if (b.Def.Raidable && !b.Planned && StepRaid(b))
            {
                k--; // burned down
                continue;
            }
            StepSabotage(b);
        }

        StepResearch();
        StepSaboteurWaits();
        StepHonour();
        StepLenses();
        StepDeepFinds();
        if (_sim.Tick % Tps == 0) SampleFood();
    }

    private void CountPopulation()
    {
        int players = _players.Count;
        for (int i = 0; i < U.Count; i++) players = Math.Max(players, U.Owner[i] + 1);
        foreach (var b in Buildings.All) players = Math.Max(players, b.Owner + 1);
        if (players > _players.Count) Player(players - 1);
        if (_population.Length < players) _population = new int[players];
        Array.Clear(_population);
        for (int i = 0; i < U.Count; i++)
            if (U.Town[i] == 0) _population[U.Owner[i]]++; // a city-state's own take no housing
    }

    /// <summary>Once a second: what each player gathered in food since the same second a minute ago.</summary>
    private void SampleFood()
    {
        int slot = (int)(_sim.Tick / Tps % 60);
        foreach (var p in _players)
        {
            int now = p.Gathered[(int)Good.Food];
            p.FoodGathered = now - p.FoodSamples[slot];
            p.FoodSamples[slot] = now;
        }
    }

    // --- Buildings ---------------------------------------------------------------------------------

    /// <summary>
    /// Checks whether the player can put a building with its footprint's corner at column (x, z), and the
    /// level it would stand at. The ground may vary by one block (the site is levelled); trees, water and
    /// other buildings (or the gap around them; walls, gates and towers need none between each other) must
    /// not be in the way. A tower may stand on the player's own walls (see <see cref="TakesPlaceOf"/>). With
    /// <paramref name="checkPlayer"/> false only the ground counts, not the player's techs and goods (match setup).
    /// A plan is checked with <paramref name="ignore"/>: the building that may stand on its own footprint. With a
    /// <paramref name="level"/> (0 or more) the ground is the standing level nearest it in each column rather than the
    /// top of the ground, for ground under other ground (a great work in the deep caves).
    /// </summary>
    public PlaceResult CanPlace(int player, BuildingDef def, int x, int z, out int baseY, bool checkPlayer = true, Building? ignore = null,
        int level = -1)
    {
        baseY = -1;
        if (def.IsBridge) return PlaceResult.Blocked; // laid along a line instead (see BridgeCommand)
        // An upgrade is not placed: the building it comes from is, and then upgraded (see UpgradeCommand). Nor is
        // a facing: it goes up beside the wall it faces.
        if (checkPlayer && (def.UpgradeOf != null || def.FacingOf != null || !Unlocked(player, def))) return PlaceResult.Locked;
        var w = World;
        int x1 = x + def.SizeX - 1, z1 = z + def.SizeZ - 1;
        if (x < 1 || z < 1 || x1 > w.SizeX - 2 || z1 > w.SizeZ - 2) return PlaceResult.OutOfBounds;
        List<Building>? taken = null;
        foreach (var b in Buildings.All)
        {
            if (b == ignore) continue;
            // Walls, gates and towers join up with each other; everything else keeps a gap of a column (from the
            // ground a building being upgraded grows onto, too).
            int gap = def.IsRampart && b.Def.IsRampart ? 0 : 1;
            var (bx0, bz0, bx1, bz1) = b.Claim;
            if (x > bx1 + gap || x1 < bx0 - gap || z > bz1 + gap || z1 < bz0 - gap) continue;
            if (!TakesPlaceOf(player, def, x, z, b)) return PlaceResult.Crowded;
            (taken ??= new List<Building>()).Add(b);
        }
        if (_sim.Cities.Crowds(x, z, x1, z1)) return PlaceResult.Crowded;

        // Under a wall the tower takes the place of, the ground is where the wall stands, and the wall's own
        // blocks are not in the way.
        Building? Under(int cx, int cz)
        {
            if (taken != null)
                foreach (var t in taken)
                    if (t.Covers(cx, cz)) return t;
            return null;
        }
        baseY = Under(x + def.SizeX / 2, z + def.SizeZ / 2)?.Y
                ?? (level >= 0 ? Walk.NearestStandLevel(w, x + def.SizeX / 2, z + def.SizeZ / 2, level, 2) : Walk.GroundLevel(w, x + def.SizeX / 2, z + def.SizeZ / 2));
        int clear = Math.Max(def.Shaped(LookOf(player)).Height, Walk.Clearance);
        if (baseY < 2 || baseY + clear >= w.SizeY) return PlaceResult.Uneven;
        for (int cz = z; cz <= z1; cz++)
        for (int cx = x; cx <= x1; cx++)
        {
            var under = Under(cx, cz);
            int g = under?.Y ?? (level >= 0 ? Walk.NearestStandLevel(w, cx, cz, baseY, 1) : Walk.GroundLevel(w, cx, cz));
            if (g < 0 || g < baseY - 1 || g > baseY + 1) return PlaceResult.Uneven;
            // A high column is cut down: its top block must be plain ground with solid ground under it.
            if (g == baseY + 1 && (!BlockInfo.IsGround(w.Get(cx, baseY, cz)) || !BlockInfo.IsSolid(w.Get(cx, baseY - 1, cz))))
                return PlaceResult.Blocked;
            int from = under == null ? Math.Max(g, baseY) : Math.Max(under.Y + Math.Max(under.Shape.Height, 1), baseY);
            for (int y = from; y < baseY + clear; y++)
                if (w.Get(cx, y, cz) is var c && c != BlockType.Air && !BlockInfo.IsLeaves(c)) return PlaceResult.Blocked;
        }
        if (def.Shore && !BesideWater(w, x, z, x1, z1, baseY)) return PlaceResult.NoWater;
        if (def.Kind == BuildingKind.GreatWork)
        {
            if (!DeepGround(baseY)) return PlaceResult.NotDeep;
            if (checkPlayer && HasGreatWork(player, ignore)) return PlaceResult.OnlyOne;
        }
        if (checkPlayer && !CanAfford(player, def.Cost)) return PlaceResult.CannotAfford;
        return PlaceResult.Ok;
    }

    /// <summary>
    /// True if a building standing at level <paramref name="y"/> stands in the deep caves: in the deep rock under the
    /// land (<see cref="Rts.Sim.World.RegionGenerator.DeepLayers"/>), where a great work must stand. A map with no deep rock
    /// has none.
    /// </summary>
    public bool DeepGround(int y) => y < Rts.Sim.World.RegionGenerator.DeepLayers(World.SizeY);

    /// <summary>True if the player has a great work (finished, a site or a plan) other than <paramref name="ignore"/>.</summary>
    public bool HasGreatWork(int player, Building? ignore = null)
    {
        foreach (var b in Buildings.All)
            if (b != ignore && b.Owner == player && b.Def.Kind == BuildingKind.GreatWork) return true;
        return false;
    }

    /// <summary>How far below the level a building stands at the water beside it may lie and still count (see <see cref="BesideWater"/>).</summary>
    public const int ShoreDrop = 4;

    /// <summary>
    /// True if a column just outside the footprint (x0..x1, z0..z1), in the ring one column round it, has water on
    /// top at most <see cref="ShoreDrop"/> blocks below <paramref name="baseY"/>, or up to its level: open water,
    /// not water under ground, nor far down a cliff.
    /// </summary>
    public static bool BesideWater(VoxelWorld w, int x0, int z0, int x1, int z1, int baseY)
    {
        for (int cz = z0 - 1; cz <= z1 + 1; cz++)
        for (int cx = x0 - 1; cx <= x1 + 1; cx++)
        {
            if (cx >= x0 && cx <= x1 && cz >= z0 && cz <= z1) continue;
            if (cx < 0 || cz < 0 || cx >= w.SizeX || cz >= w.SizeZ) continue;
            for (int y = baseY + 1; y >= Math.Max(0, baseY - ShoreDrop); y--)
            {
                var b = w.Get(cx, y, cz);
                if (b == BlockType.Air) continue;
                if (b == BlockType.Water) return true;
                break;
            }
        }
        return false;
    }

    /// <summary>
    /// True if a tower with its footprint's corner at column (x, z) takes the place of building b: a finished
    /// wall of the player's own (a column of any tier, or of a facing; not a gate, since nobody walks through a
    /// tower) or a smaller finished tower of theirs, wholly inside its footprint. That is how a tower is put
    /// onto a wall: the wall stands as it is while the tower goes up around and over it, and is part of the
    /// tower once that is finished (see <see cref="Absorb"/>).
    /// </summary>
    public static bool TakesPlaceOf(int player, BuildingDef def, int x, int z, Building b)
    {
        if (!def.IsTower || b.Owner != player || !b.Active) return false;
        if (b.Def.Kind != BuildingKind.Wall && !(b.Def.IsTower && b.Def.SizeX * b.Def.SizeZ < def.SizeX * def.SizeZ)) return false;
        return b.X >= x && b.Z >= z && b.MaxX <= x + def.SizeX - 1 && b.MaxZ <= z + def.SizeZ - 1;
    }

    /// <summary>
    /// A finished tower takes in what it was put onto (see <see cref="TakesPlaceOf"/>): those walls are gone as
    /// buildings, and their columns are the tower's, whole.
    /// </summary>
    private void Absorb(Building tower)
    {
        if (!tower.Def.IsTower) return;
        var all = Buildings.All;
        int top = tower.Y + tower.Shape.Height;
        List<Building>? taken = null;
        foreach (var b in all)
            if (b != tower && TakesPlaceOf(tower.Owner, tower.Def, tower.X, tower.Z, b)) (taken ??= new List<Building>()).Add(b);
        if (taken == null) return;
        foreach (var b in taken)
        {
            // What it has above or below the tower goes; the rest becomes the tower's own blocks below.
            for (int k = 0; k < b.Placed; k++)
            {
                var t = b.Shape.Blocks[k];
                int x = b.X + t.X, y = b.Y + t.Y, z = b.Z + t.Z;
                if ((y < tower.Y || y >= top) && World.Get(x, y, z) == t.Block) World.Set(x, y, z, y < tower.Y ? BlockType.Soil : BlockType.Air);
            }
            Buildings.Remove(b);
        }
        var blocks = tower.Shape.Blocks;
        for (int k = 0; k < blocks.Count; k++)
        {
            var t = blocks[k];
            int x = tower.X + t.X, y = tower.Y + t.Y, z = tower.Z + t.Z;
            bool inside = false;
            foreach (var b in taken) inside |= b.Covers(x, z);
            if (!inside) continue;
            if (World.Get(x, y, z) != t.Block) World.Set(x, y, z, t.Block);
            if (tower.Gone != null && tower.Gone[k])
            {
                tower.Gone[k] = false;
                tower.Lost -= tower.Shape.BlockToughness(k);
            }
        }
    }

    /// <summary>
    /// Puts a building down without cost or work, finished (match setup, tests). Null if it does not fit.
    /// </summary>
    public Building? PlaceFinished(int owner, BuildingDef def, int x, int z, int level = -1)
    {
        if (CanPlace(owner, def, x, z, out int baseY, checkPlayer: false, level: level) != PlaceResult.Ok) return null;
        var b = Prepare(owner, def, x, z, baseY);
        foreach (var t in b.Shape.Blocks) World.Set(b.X + t.X, b.Y + t.Y, b.Z + t.Z, t.Block);
        b.Placed = b.Shape.Blocks.Count;
        b.Work = def.BuildTicks;
        b.State = BuildingState.Active;
        Absorb(b);
        if (def.Kind == BuildingKind.Orchard) Replant(b);
        return b;
    }

    /// <summary>
    /// Test mode: the building is gone at once, with its blocks (turned to air) and goods; nothing falls as
    /// rubble (see <see cref="Sandbox.SandboxSystem"/>).
    /// </summary>
    internal void Demolish(Building b)
    {
        var blocks = b.Shape.Blocks;
        for (int k = 0; k < b.Placed && k < blocks.Count; k++)
        {
            var t = blocks[k];
            int x = b.X + t.X, y = b.Y + t.Y, z = b.Z + t.Z;
            if (World.InBounds(x, y, z) && World.Get(x, y, z) == t.Block) World.Set(x, y, z, t.Y < 0 ? BlockType.Soil : BlockType.Air);
        }
        ClearTrees(b);
        Buildings.Remove(b);
    }

    /// <summary>
    /// Creates the building and levels its site: high columns cut down, low ones filled, leaves cleared. The
    /// walls a tower is put onto are left standing (see <see cref="TakesPlaceOf"/>).
    /// </summary>
    private Building Prepare(int owner, BuildingDef def, int x, int z, int baseY)
    {
        var look = LookOf(owner);
        Level(owner, def.Shaped(look), x, z, baseY);
        return Buildings.Add(owner, def, x, baseY, z, look);
    }

    /// <summary>The shape <paramref name="owner"/>'s buildings are put up in: their faction's (see <see cref="FactionDef.Look"/>).</summary>
    public BuildingLook LookOf(int owner) => owner < 0 ? BuildingLook.Standard : _sim.FactionOf(owner).Look;

    private void Level(int owner, BuildingDef def, int x, int z, int baseY)
    {
        var w = World;
        int clear = Math.Max(def.Height, Walk.Clearance);
        for (int cz = z; cz < z + def.SizeZ; cz++)
        for (int cx = x; cx < x + def.SizeX; cx++)
        {
            if (def.IsTower && OnTaken(owner, def, x, z, cx, cz)) continue;
            if (!BlockInfo.IsSolid(w.Get(cx, baseY - 1, cz))) w.Set(cx, baseY - 1, cz, BlockType.Soil);
            for (int y = baseY; y < baseY + clear; y++)
                if (w.Get(cx, y, cz) != BlockType.Air) w.Set(cx, y, cz, BlockType.Air);
        }
    }

    /// <summary>True if column (cx, cz) holds a wall that a tower of the owner's at (x, z) takes the place of.</summary>
    private bool OnTaken(int owner, BuildingDef def, int x, int z, int cx, int cz)
    {
        foreach (var b in Buildings.All)
            if (b.Covers(cx, cz) && TakesPlaceOf(owner, def, x, z, b)) return true;
        return false;
    }

    /// <summary>
    /// A site goes up as it is worked: its blocks appear bottom layer first, in step with the work done.
    /// A block waits while a unit stands where it goes; idle units on the site are sent off it, and so are
    /// builders at work on another site (see <see cref="ClearSite"/>).
    /// </summary>
    private void StepSite(Building b)
    {
        var blocks = b.Shape.Blocks;
        int target = (int)((long)Math.Min(b.Work, b.Def.BuildTicks) * blocks.Count / b.Def.BuildTicks);
        if (b.Placed < target && !b.Def.Walkable && _sim.Tick % 10 == 0) ClearSite(b);
        while (b.Placed < target)
        {
            var t = blocks[b.Placed];
            int x = b.X + t.X, y = b.Y + t.Y, z = b.Z + t.Z;
            if (t.Y >= 0 && Occupied(x, y, z)) break;
            var current = World.Get(x, y, z);
            // Something built into the site meanwhile stays; the field replaces ground, walls fill air (a bridge's deck
            // water too). Rubble lying there (a felled tree's logs come down on a site) is cleared away, as a repair
            // clears it: left in, it would stand in the wall as a gap that counts as whole until someone gathers it.
            if (t.Y < 0 ? current != BlockType.Air && current != BlockType.Water
                : current == BlockType.Air || BlockInfo.IsLeaves(current) || BlockInfo.IsRubble(current)
                  || (b.Def.IsBridge && current == BlockType.Water))
                World.Set(x, y, z, t.Block);
            b.Placed++;
        }
        if (b.Placed == blocks.Count && b.Work >= b.Def.BuildTicks)
        {
            b.State = BuildingState.Active;
            Absorb(b);
            if (b.Def.Kind == BuildingKind.Orchard) Replant(b);
            _sim.Events.BuildingFinished(b);
        }
    }

    /// <summary>True if a unit's body is in block (x, y, z).</summary>
    private bool Occupied(int x, int y, int z)
    {
        var u = U;
        for (int i = 0; i < u.Count; i++)
            if ((u.Y[i] == y || u.Y[i] + 1 == y) && u.X[i].FloorToInt() == x && u.Z[i].FloorToInt() == z) return true;
        return false;
    }

    /// <summary>
    /// Sends units standing on a building site off it: idle ones to the nearest free spot beside it, and a
    /// builder at work on another site to the nearest free spot beside the site it is building (it works on
    /// from there). Without the second, two columns of a wall side by side, each with its builder standing
    /// where the other's blocks go, would wait on each other for good.
    /// </summary>
    private void ClearSite(Building b)
    {
        var u = U;
        for (int i = 0; i < u.Count; i++)
        {
            var from = b;
            if (u.Job[i] == UnitJob.Build)
            {
                if (u.JobTarget[i] == b.Id || u.Phase[i] != JobPhase.Working || u.Order[i] != UnitOrder.Idle) continue;
                var own = Buildings.Get(u.JobTarget[i]);
                if (own == null) continue;
                from = own;
            }
            else if (u.Order[i] != UnitOrder.Idle || u.Job[i] != UnitJob.None) continue;
            int x = u.X[i].FloorToInt(), z = u.Z[i].FloorToInt();
            if (!b.Covers(x, z) || u.Y[i] < b.Y - 1 || u.Y[i] > b.Y + b.Shape.Height) continue;
            if (SpotBeside(from, x, z, out int sx, out int sz, out int sy)) _sim.SendUnit(i, sx, sz, sy);
        }
    }

    /// <summary>
    /// The standable column next to the building (within 3 columns) nearest to (x, z), within
    /// <paramref name="levels"/> of the level the building stands at.
    /// </summary>
    internal bool SpotBeside(Building b, int x, int z, out int sx, out int sz, out int sy, int levels = 2) =>
        SpotBeside(b.X, b.Z, b.MaxX, b.MaxZ, b.Y, x, z, out sx, out sz, out sy, levels);

    /// <summary>
    /// The standable column next to the footprint [x0, x1] by [z0, z1] (within 3 columns) nearest to (x, z),
    /// within <paramref name="levels"/> of level <paramref name="baseY"/>, not under a building.
    /// </summary>
    private bool SpotBeside(int x0, int z0, int x1, int z1, int baseY, int x, int z, out int sx, out int sz, out int sy, int levels = 2)
    {
        sx = sz = sy = -1;
        long best = long.MaxValue;
        for (int d = 1; d <= 3 && best == long.MaxValue; d++)
        for (int cz = z0 - d; cz <= z1 + d; cz++)
        for (int cx = x0 - d; cx <= x1 + d; cx++)
        {
            int ddx = cx < x0 ? x0 - cx : cx > x1 ? cx - x1 : 0, ddz = cz < z0 ? z0 - cz : cz > z1 ? cz - z1 : 0;
            if (Math.Max(ddx, ddz) != d) continue;
            int y = Walk.NearestStandLevel(World, cx, cz, baseY, levels);
            if (y < 0 || Buildings.AtColumn(cx, cz) != null) continue;
            long dx = cx - x, dz = cz - z, score = dx * dx + dz * dz;
            if (score >= best) continue;
            best = score;
            (sx, sz, sy) = (cx, cz, y);
        }
        return best != long.MaxValue;
    }

    /// <summary>
    /// Where units go to use a building: in front of the door (or the nearest open side), or the middle
    /// of a field. False if there is nowhere to stand.
    /// </summary>
    public bool AccessSpot(Building b, out int x, out int z, out int y)
    {
        if (b.Def.IsBridge) return BridgeAccess(b, out x, out z, out y);
        if (b.Def.Walkable)
        {
            x = b.X + b.Def.SizeX / 2;
            z = b.Z + b.Def.SizeZ / 2;
            y = Walk.NearestStandLevel(World, x, z, b.Y, 2);
            return y >= 0;
        }
        return SpotBeside(b, b.X + b.Def.SizeX / 2, b.MaxZ + 1, out x, out z, out y);
    }

    /// <summary>True if unit i is where it can use the building: beside it (on it, for a field), at about its level.</summary>
    public bool AtBuilding(int i, Building b)
    {
        int d = b.ColumnDistance(U.X[i].FloorToInt(), U.Z[i].FloorToInt());
        if (b.Def.Walkable ? d > 1 : d != 1) return false;
        return Math.Abs(U.Y[i] - b.Y) <= 2;
    }

    /// <summary>
    /// Ticks of training or smelting the building gets through this tick: its <see cref="BuildingDef.Speed"/>
    /// in hundredths, spread evenly over the ticks (1 every tick at 100, 1 and 2 by turns at 150).
    /// </summary>
    private int Steps(Building b)
    {
        long t = _sim.Tick, s = b.Def.Speed;
        return (int)((t + 1) * s / 100 - t * s / 100);
    }

    private void StepSmelter(Building b)
    {
        if (b.SmeltTicks > 0 && (b.SmeltTicks = Math.Max(0, b.SmeltTicks - Steps(b))) == 0) b.Stock[(int)Good.Iron]++;
        if (b.SmeltTicks == 0 && b.Stock[(int)Good.IronOre] >= OrePerIngot && b.Stock[(int)Good.Coal] >= CoalPerIngot)
        {
            b.Stock[(int)Good.IronOre] -= OrePerIngot;
            b.Stock[(int)Good.Coal] -= CoalPerIngot;
            b.SmeltTicks = SmeltTicks;
        }
    }

    /// <summary>
    /// A workshop works what its worker brought into what it makes (wood into planks at a sawmill, stone into cut
    /// stone at a mason's yard), one into one every <see cref="BuildingDef.MakeTicks"/> (at its <see cref="BuildingDef.Speed"/>);
    /// the one being worked is counted in <see cref="Building.SmeltTicks"/>, and what it makes waits in its stock for
    /// the worker to carry to storage.
    /// </summary>
    private void StepWorkshop(Building b)
    {
        if (b.SmeltTicks > 0 && (b.SmeltTicks = Math.Max(0, b.SmeltTicks - Steps(b))) == 0) b.Stock[(int)b.Def.Makes]++;
        if (b.SmeltTicks == 0 && b.Stock[(int)b.Def.Takes] > 0)
        {
            b.Stock[(int)b.Def.Takes]--;
            b.SmeltTicks = b.Def.MakeTicks;
        }
    }

    /// <summary>
    /// A shrine channels the crystal its worker brought, one at a time (<see cref="BuildingDef.ChannelTicks"/>
    /// each, at its <see cref="BuildingDef.Speed"/>), and each one gives its owner <see cref="BuildingDef.Mana"/>.
    /// The crystal being channelled is counted in <see cref="Building.SmeltTicks"/>.
    /// </summary>
    private void StepShrine(Building b)
    {
        if (b.SmeltTicks > 0 && (b.SmeltTicks = Math.Max(0, b.SmeltTicks - Steps(b))) == 0)
        {
            var p = Player(b.Owner);
            p.Mana += b.Def.Mana;
            p.ManaChannelled += b.Def.Mana;
        }
        if (b.SmeltTicks == 0 && b.Stock[(int)Good.Crystal] > 0)
        {
            b.Stock[(int)Good.Crystal]--;
            b.SmeltTicks = b.Def.ChannelTicks;
        }
    }

    /// <summary>The player has at least <paramref name="mana"/> mana.</summary>
    public bool HasMana(int player, int mana) => player >= 0 && Player(player).Mana >= mana;

    /// <summary>Takes <paramref name="mana"/> mana from the player (they must have it).</summary>
    internal void SpendMana(int player, int mana) => Player(player).Mana -= mana;

    /// <summary>
    /// Trains the first unit in the queue while the owner has room; it steps out at the door. A unit that
    /// hatches in a batch (<see cref="UnitDef.Batch"/>) waits for room for the whole batch, which steps out
    /// side by side along the door's wall. A worker comes out automatic if its player has said so; on repeat,
    /// the unit is owed to the queue again.
    /// </summary>
    private void StepTraining(Building b)
    {
        int owner = b.Owner;
        var def = _sim.Content.Units[b.Queue[0]];
        if (_population[owner] + def.Batch > Housing(owner)) return;
        if (b.TrainTicks < def.TrainTicks) b.TrainTicks = Math.Min(b.TrainTicks + Steps(b), def.TrainTicks);
        if (b.TrainTicks < def.TrainTicks) return;
        int x, z, y;
        if (def.IsBoat ? !_sim.Boats.Launch(b, out x, out z, out y) : !AccessSpot(b, out x, out z, out y)) return;
        for (int k = 0; k < def.Batch; k++)
        {
            int ux = x, uz = z, uy = y;
            // The rest of a batch beside the first: one column either way along the wall in turn, where there is room.
            int along = (k + 1) / 2 * (k % 2 == 1 ? -1 : 1);
            if (k > 0 && !def.IsBoat && !b.Def.Walkable && !b.Def.IsBridge
                && !SpotBeside(b, b.X + b.Def.SizeX / 2 + along, b.MaxZ + 1, out ux, out uz, out uy))
                (ux, uz, uy) = (x, z, y);
            int id = U.Spawn(owner, def.Index, Fix64.FromInt(ux) + Fix64.Half, uy, Fix64.FromInt(uz) + Fix64.Half, MaxHp(owner, def));
            if (def.Role == UnitRole.Worker) U.Auto[U.IndexOf(id)] = Player(owner).AutoWorkers;
            else if (def.IsHauler) U.Auto[U.IndexOf(id)] = true; // a cart looks for supply work on its own
            if (b.HasRally)
            {
                int i = U.IndexOf(id);
                _sim.SendUnit(i, b.RallyX, b.RallyZ, b.RallyY);
                U.ArriveRadius[i] = RallyArrive;
            }
            else if (def.IsFisher && FishesFrom(b, def)) StartFishing(U.IndexOf(id), b); // out fishing at once
            _population[owner]++;
            _sim.Events.UnitTrained(owner, id, def.Index, ux, uy, uz);
        }
        b.Queue.RemoveAt(0);
        b.TrainTicks = 0;
        if (b.Repeat && !def.Hero) b.Owed.Add(def.Index); // a hero is one of a kind: not again
    }

    /// <summary>
    /// Capture or burn progress: the raider standing at the building with the lowest unit id decides who
    /// raids and how; any unit of the owner within <see cref="GuardRadius"/> holds it off. Returns true if
    /// the building burned down (and is gone).
    /// </summary>
    private bool StepRaid(Building b)
    {
        int raider = -1;
        foreach (var (id, i) in _raiders)
            if (id == b.Id && U.Owner[i] != b.Owner && (raider < 0 || U.Id[i] < U.Id[raider])) raider = i;
        if (raider < 0)
        {
            b.RaidPlayer = -1;
            b.Raid = RaidKind.None;
            b.RaidTicks = 0;
            return false;
        }
        int player = U.Owner[raider];
        var kind = U.Job[raider] == UnitJob.Burn ? RaidKind.Burn : RaidKind.Capture;
        if (player != b.RaidPlayer || kind != b.Raid)
        {
            b.RaidPlayer = player;
            b.Raid = kind;
            b.RaidTicks = 0;
            _sim.Events.BuildingAttacked(b, player);
        }
        if (Defended(b))
        {
            b.RaidTicks = 0;
            return false;
        }
        if (++b.RaidTicks < (kind == RaidKind.Burn ? BurnTicks : CaptureTicks)) return false;

        foreach (var (id, i) in _raiders)
            if (id == b.Id && U.Owner[i] == player) U.ClearJob(i);
        b.RaidPlayer = -1;
        b.Raid = RaidKind.None;
        b.RaidTicks = 0;
        if (b.Def.Kind == BuildingKind.Orchard)
        {
            // An orchard is cut down, whether its raiders came to burn it or to take it.
            CutDown(b);
            return false;
        }
        if (kind == RaidKind.Capture)
        {
            BuildingLost(b);
            b.Owner = player;
            b.Queue.Clear();
            b.RallyX = -1;
            b.TrainTicks = 0;
            b.Repeat = false;
            b.Owed.Clear();
            Array.Clear(b.Supply); // the old owner's supply rules go with it
            b.Governor = GovernorFocus.None; // and so does their governor
            b.Salvaging = false; // and their mark to take it down
            b.Sabotaged = b.SabotageWork = 0; // a sabotage is the old owner's trouble
            b.SabotagePlayer = -1;
            b.Salvaged = 0;
            return false;
        }
        // Burned: the goods are lost and what is left of it crumbles.
        BuildingLost(b);
        _sim.Automation.Destroyed(b);
        _sim.Physics.Crumble(b);
        Buildings.Remove(b);
        return true;
    }

    /// <summary>True if any of the owner's units is within <see cref="GuardRadius"/> columns of the building.</summary>
    private bool Defended(Building b)
    {
        for (int i = 0; i < U.Count; i++)
        {
            if (U.Owner[i] != b.Owner) continue;
            if (b.ColumnDistance(U.X[i].FloorToInt(), U.Z[i].FloorToInt()) <= GuardRadius && Math.Abs(U.Y[i] - b.Y) <= GuardRadius)
                return true;
        }
        return false;
    }

    // --- Stock -------------------------------------------------------------------------------------

    public bool CanAfford(int player, GoodAmount[] cost)
    {
        if (cost.Length == 0) return true;
        var stock = Stockpile(player);
        foreach (var c in cost)
            if (stock[(int)c.Good] < c.Amount) return false;
        return true;
    }

    /// <summary>Pays a cost from the player's storage, nearest to column (x, z) first. False (and nothing paid) if short.</summary>
    internal bool TrySpend(int player, GoodAmount[] cost, int x, int z)
    {
        if (!CanAfford(player, cost)) return false;
        foreach (var c in cost)
        {
            Take(player, c.Good, c.Amount, (x, z));
            Player(player).Spent[(int)c.Good] += c.Amount;
        }
        return true;
    }

    /// <summary>
    /// Takes up to <paramref name="amount"/> of a good from the player's finished storage buildings: nearest
    /// to <paramref name="near"/> first, or in building order. Returns the amount taken.
    /// </summary>
    private int Take(int player, Good good, int amount, (int X, int Z)? near)
    {
        var stores = new List<Building>();
        foreach (var b in Buildings.All)
            if (b.Owner == player && b.Active && b.Def.Storage && b.Stock[(int)good] > 0) stores.Add(b);
        if (near is { } n)
            stores.Sort((a, b) =>
            {
                int c = a.CentreDistance2(n.X, n.Z).CompareTo(b.CentreDistance2(n.X, n.Z));
                return c != 0 ? c : a.Id.CompareTo(b.Id);
            });
        int taken = 0;
        foreach (var b in stores)
        {
            int t = Math.Min(amount - taken, b.Stock[(int)good]);
            b.Stock[(int)good] -= t;
            taken += t;
            if (taken == amount) break;
        }
        return taken;
    }

    /// <summary>
    /// The player's finished storage building nearest to column (x, z) (by straight line, then id), holding
    /// at least one of <paramref name="holding"/> if given; null if there is none.
    /// </summary>
    public Building? NearestStorage(int player, int x, int z, Good? holding = null)
    {
        Building? best = null;
        long bestD = long.MaxValue;
        foreach (var b in Buildings.All)
        {
            if (b.Owner != player || !b.Active || !b.Def.Storage) continue;
            if (holding is { } g && b.Stock[(int)g] == 0) continue;
            long d = b.CentreDistance2(x, z);
            if (d >= bestD) continue;
            bestD = d;
            best = b;
        }
        return best;
    }

    // --- Commands ----------------------------------------------------------------------------------

    /// <summary>Places a site for the player if it fits and they can pay, and sends the command's workers to build it.</summary>
    internal void Apply(BuildCommand c)
    {
        if (!_sim.Content.TryGetBuilding(c.Building, out var def)) return;
        if (CanPlace(c.PlayerId, def, c.X, c.Z, out int baseY, level: c.Y) != PlaceResult.Ok) return;
        if (!TrySpend(c.PlayerId, def.Cost, c.X + def.SizeX / 2, c.Z + def.SizeZ / 2)) return;
        var b = Prepare(c.PlayerId, def, c.X, c.Z, baseY);
        SetAside(b);
        foreach (int i in Workers(c))
            StartJob(i, UnitJob.Build, b.Id);
    }

    /// <summary>
    /// Marks out a plan for the player where the building fits, whether or not they can pay for it yet; the
    /// ground is left as it is until the plan is started.
    /// </summary>
    internal void Apply(PlanCommand c)
    {
        if (!_sim.Content.TryGetBuilding(c.Building, out var def)) return;
        var fit = CanPlace(c.PlayerId, def, c.X, c.Z, out int baseY, level: c.Y);
        if (fit != PlaceResult.Ok && fit != PlaceResult.CannotAfford) return;
        var b = Buildings.Add(c.PlayerId, def, c.X, baseY, c.Z, LookOf(c.PlayerId));
        b.State = BuildingState.Planned;
    }

    internal void Apply(CancelPlanCommand c)
    {
        var b = Buildings.Get(c.Building);
        if (b != null && b.Owner == c.PlayerId && b.Planned) Buildings.Remove(b);
    }

    /// <summary>
    /// Why the player's plan cannot be started now: <see cref="PlaceResult.Ok"/> if it can (the ground still fits,
    /// the techs are there and the goods are in store).
    /// </summary>
    public PlaceResult CanStart(Building plan) =>
        !plan.Planned ? PlaceResult.Blocked : CanPlace(plan.Owner, plan.Def, plan.X, plan.Z, out _, ignore: plan, level: LevelOf(plan));

    /// <summary>
    /// The level a plan or ruin is checked at again: its own for a great work, which stands under other ground;
    /// -1 (the top of the ground) for anything else, which is placed there however the ground has changed.
    /// </summary>
    public static int LevelOf(Building b) => b.Def.Kind == BuildingKind.GreatWork ? b.Y : -1;

    /// <summary>
    /// Starts a plan: pays for it, levels its ground and makes it a site. False, with nothing changed, if it
    /// cannot start yet (see <see cref="CanStart"/>).
    /// </summary>
    private bool StartPlan(Building b)
    {
        if (CanPlace(b.Owner, b.Def, b.X, b.Z, out int baseY, ignore: b, level: LevelOf(b)) != PlaceResult.Ok) return false;
        if (!TrySpend(b.Owner, b.Def.Cost, b.X + b.Def.SizeX / 2, b.Z + b.Def.SizeZ / 2)) return false;
        Level(b.Owner, b.Shape, b.X, b.Z, baseY);
        b.Y = baseY;
        b.State = BuildingState.Site;
        SetAside(b);
        return true;
    }

    /// <summary>
    /// Puts a site of a den building (<see cref="BuildingDef.FromDen"/>) as near column (x, z), the den, as it fits
    /// (its middle within <see cref="DenSearch"/> columns), pays for it, and sends the workers among the command's
    /// units to build it. Null, with nothing paid, if the player's faction or techs do not allow it, they cannot
    /// pay, or it fits nowhere.
    /// </summary>
    internal Building? BuildOnDen(UnitsCommand c, BuildingDef def, int x, int z)
    {
        int player = c.PlayerId;
        if (def.Faction != null && def.Faction != Faction(player)) return null;
        if (MissingTech(player, def.Requires) != null || !CanAfford(player, def.Cost)) return null;
        int cx = x - def.SizeX / 2, cz = z - def.SizeZ / 2;
        for (int r = 0; r <= DenSearch; r++)
        for (int dz = -r; dz <= r; dz++)
        for (int dx = -r; dx <= r; dx++)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue; // the ring r out, nearest rings first
            if (CanPlace(player, def, cx + dx, cz + dz, out int baseY, checkPlayer: false) != PlaceResult.Ok) continue;
            if (!TrySpend(player, def.Cost, x, z)) return null;
            var b = Prepare(player, def, cx + dx, cz + dz, baseY);
            SetAside(b);
            foreach (int i in Workers(c))
                StartJob(i, UnitJob.Build, b.Id);
            return b;
        }
        return null;
    }

    /// <summary>How far (columns) from a den the middle of a building put up on it may be, where the ground at the den is uneven.</summary>
    public const int DenSearch = 4;

    /// <summary>A building's own yield (<see cref="BuildingDef.Yield"/>) goes into its owner's storage nearest it.</summary>
    private void BringYield(Building b)
    {
        int mx = b.X + b.Def.SizeX / 2, mz = b.Z + b.Def.SizeZ / 2;
        var p = Player(b.Owner);
        foreach (var g in b.Def.Yield)
        {
            if (NearestStorage(b.Owner, mx, mz) is not { } store) return;
            store.Stock[(int)g.Good] += g.Amount;
            p.Gathered[(int)g.Good] += g.Amount;
        }
    }

    internal void Apply(GatherCommand c)
    {
        var block = World.Get(c.X, c.Y, c.Z);
        if (!Harvest.CanGather(block)) return;
        var kind = Harvest.JobKind(block); // a felled tree's rubble is cleared by wood cutters
        int y = Harvest.Span(World, c.X, c.Y, c.Z).Base; // a tree is gathered from its trunk's base
        EnsureClaims();
        foreach (int i in Workers(c))
        {
            int tx = c.X, ty = y, tz = c.Z;
            // Spread a group over the resource: each worker takes the nearest block nobody else has.
            if (_claimed.Contains(Harvest.Key(World, tx, ty, tz)) && Harvest.Repeats(kind)
                && Harvest.FindNext(World, block == BlockType.TreeRubble ? block : kind, c.X, y, c.Z, _claimed, out int nx, out int ny, out int nz))
                (tx, ty, tz) = (nx, ny, nz);
            StartJob(i, UnitJob.Gather, 0);
            U.JobBlock[i] = kind;
            Claim(i, tx, ty, tz);
            if (MustUnloadFirst(i, kind)) GoToStorage(i);
        }
    }

    internal void Apply(WorkCommand c)
    {
        var b = Buildings.Get(c.Building);
        if (b == null || b.Owner != c.PlayerId) return;
        if (b.Planned && !StartPlan(b)) return;
        // Carts only haul: they unload at storage, or take supply duty at storage with supply rules.
        foreach (int i in OwnedUnits(c))
        {
            if (!_sim.Content.Units[U.Def[i]].IsHauler) continue;
            if (IsPony(i) && PonyTo(i, b)) continue;
            if (!b.Active) continue;
            if (b.Def.Kind == BuildingKind.Orchard)
            {
                // A cart takes an orchard's baskets to storage (once it has unloaded what it carries).
                if (U.Job[i] == UnitJob.Haul && U.JobTarget[i] == b.Id) continue;
                StartJob(i, UnitJob.Haul, b.Id);
                if (U.CarryAmount[i] > 0) GoToStorage(i);
                continue;
            }
            if (!b.Def.Storage) continue;
            if (U.CarryAmount[i] > 0)
            {
                StartJob(i, UnitJob.Deliver, 0);
                U.JobStore[i] = b.Id;
                SetPhase(i, JobPhase.ToStore);
            }
            else if (b.HasSupply && !IsPony(i) && !(U.Job[i] == UnitJob.Supply && U.JobTarget[i] == b.Id))
            {
                StartJob(i, UnitJob.Supply, b.Id);
                DecideSupply(i, b);
            }
        }
        foreach (int i in Workers(c))
        {
            if (b.WantsBuilders)
            {
                StartJob(i, UnitJob.Build, b.Id);
            }
            else if (b.Def.Kind is BuildingKind.Farm or BuildingKind.Smelter or BuildingKind.Quarry or BuildingKind.Shrine or BuildingKind.Workshop or BuildingKind.Orchard)
            {
                var job = JobAt(b.Def);
                if (U.Job[i] == job && U.JobTarget[i] == b.Id) continue;
                if (WorkersAt(b, job) >= b.Def.WorkerSlots) continue;
                StartJob(i, job, b.Id);
                if (job == UnitJob.Farm && U.CarryAmount[i] > 0 && U.Carry[i] != Good.Food) GoToStorage(i);
                // A quarry or mine worker unloads what it carries first, so the first round's goods are not mixed in.
                if (job == UnitJob.Quarry && U.CarryAmount[i] > 0) GoToStorage(i);
            }
            else if (b.Def.Storage && b.Active && U.CarryAmount[i] > 0)
            {
                StartJob(i, UnitJob.Deliver, 0);
                U.JobStore[i] = b.Id;
                SetPhase(i, JobPhase.ToStore);
            }
            else if (b.Def.Storage && b.Active && b.HasSupply)
            {
                // Empty-handed at storage with supply rules: supply duty there.
                if (U.Job[i] == UnitJob.Supply && U.JobTarget[i] == b.Id) continue;
                StartJob(i, UnitJob.Supply, b.Id);
                DecideSupply(i, b);
            }
        }
    }

    internal void Apply(CaptureCommand c) => StartRaid(c, c.Building, UnitJob.Capture);

    internal void Apply(BurnCommand c) => StartRaid(c, c.Building, UnitJob.Burn);

    private void StartRaid(UnitsCommand c, int building, UnitJob job)
    {
        var b = Buildings.Get(building);
        if (b == null || !_sim.Truces.Hostile(c.PlayerId, b.Owner) || !b.Def.Raidable || b.Planned) return;
        foreach (int i in OwnedUnits(c))
            StartJob(i, job, b.Id);
    }

    /// <summary>Lays road block by block along the line until the player runs out of stone.</summary>
    internal void Apply(RoadCommand c)
    {
        if (!CanLayRoads(c.PlayerId)) return;
        var w = World;
        foreach (var (x, z) in RoadLine(c.X1, c.Z1, c.X2, c.Z2))
        {
            if (!RoadGoesOn(x, z, out int y)) continue;
            if (Take(c.PlayerId, Good.Stone, StonePerRoad, (c.X1, c.Z1)) < StonePerRoad) return;
            Player(c.PlayerId).Spent[(int)Good.Stone] += StonePerRoad;
            w.Set(x, y - 1, z, BlockType.Road);
        }
    }

    /// <summary>The columns a road from (x1, z1) towards (x2, z2) runs over, in order: at most <see cref="MaxRoad"/>.</summary>
    public static IEnumerable<(int X, int Z)> RoadLine(int x1, int z1, int x2, int z2)
    {
        int x = x1, z = z1;
        int dx = Math.Abs(x2 - x1), dz = -Math.Abs(z2 - z1);
        int sx = x1 < x2 ? 1 : -1, sz = z1 < z2 ? 1 : -1;
        int err = dx + dz;
        for (int n = 0; n < MaxRoad; n++)
        {
            yield return (x, z);
            if (x == x2 && z == z2) yield break;
            int e2 = 2 * err;
            if (e2 >= dz) { err += dz; x += sx; }
            if (e2 <= dx) { err += dx; z += sz; }
        }
    }

    /// <summary>
    /// True if a road laid over column (x, z) turns a block into road there (open natural ground, no building on it;
    /// a column already road is not), with <paramref name="y"/> the level a unit stands at on it.
    /// </summary>
    public bool RoadGoesOn(int x, int z, out int y)
    {
        y = -1;
        var w = World;
        if (!w.InBounds(x, 0, z) || Buildings.AtColumn(x, z) != null) return false;
        y = Walk.GroundLevel(w, x, z);
        return y >= 1 && BlockInfo.IsGround(w.Get(x, y - 1, z));
    }

    internal void Apply(TrainCommand c)
    {
        var b = Buildings.Get(c.Building);
        if (b == null || b.Owner != c.PlayerId || !b.Active || b.Queue.Count >= MaxQueue) return;
        if (Array.IndexOf(b.Def.Trains, c.Unit) < 0 || !_sim.Content.TryGetUnit(c.Unit, out var def)) return;
        if (!Unlocked(c.PlayerId, def) || Faction(c.PlayerId).Replaces(b.Def)) return;
        if (Player(c.PlayerId).Honour < def.Honour || (def.Hero && HeroTaken(c.PlayerId, def))) return;
        if (Player(c.PlayerId).Mana < def.Mana) return; // the Fey's wisps and enchanters are paid in mana too
        if (!TrySpend(c.PlayerId, def.Cost, b.X + b.Def.SizeX / 2, b.Z + b.Def.SizeZ / 2)) return;
        Player(c.PlayerId).Honour -= def.Honour;
        Player(c.PlayerId).Mana -= def.Mana;
        b.Queue.Add(def.Index);
        b.Owed.Remove(def.Index); // a unit owed to a repeating queue is paid for now
    }

    /// <summary>Turns repeat on or off at one of the player's buildings that trains units; off forgets what was owed.</summary>
    internal void Apply(RepeatCommand c)
    {
        var b = Buildings.Get(c.Building);
        if (b == null || b.Owner != c.PlayerId || b.Def.Trains.Length == 0) return;
        b.Repeat = c.On;
        if (!c.On) b.Owed.Clear();
    }

    /// <summary>How close (blocks) a unit walking to its building's rally point comes before it stops: room for a crowd.</summary>
    public static readonly Fix64 RallyArrive = Fix64.FromRatio(3, 2);

    /// <summary>
    /// Sets or clears where the building's trained units walk when they step out (see <see cref="RallyCommand"/>):
    /// only on the player's own building that trains units. The spot snaps to ground a unit can stand on.
    /// </summary>
    internal void Apply(RallyCommand c)
    {
        var b = Buildings.Get(c.Building);
        if (b == null || b.Owner != c.PlayerId || b.Def.Trains.Length == 0) return;
        if (c.X < 0 || c.Z < 0 || c.X >= _sim.World.SizeX || c.Z >= _sim.World.SizeZ || b.Covers(c.X, c.Z))
        {
            b.RallyX = -1;
            b.RallyZ = b.RallyY = 0;
            return;
        }
        var (x, z, y) = _sim.StandingGoal(c.X, c.Z, c.Y);
        (b.RallyX, b.RallyZ, b.RallyY) = (x, z, y);
    }

    /// <summary>The command's units that exist, belong to the player and are not routing, without duplicates, in order.</summary>
    private List<int> OwnedUnits(UnitsCommand c) => _sim.OwnedUnits(c);

    /// <summary>The command's units that are workers (the only ones that gather, build and haul).</summary>
    private List<int> Workers(UnitsCommand c)
    {
        var result = OwnedUnits(c);
        result.RemoveAll(i => _sim.Content.Units[U.Def[i]].Role != UnitRole.Worker);
        return result;
    }

    /// <summary>
    /// Pack pony i sent to building b (by the player or automation): out to a site with goods due, or to a workshop,
    /// smelter, quarry, mine or orchard to run its goods. False if b is neither (storage is handled as for a cart).
    /// </summary>
    private bool PonyTo(int i, Building b)
    {
        if (b.State == BuildingState.Site)
        {
            if (U.Job[i] == UnitJob.Bring && U.JobTarget[i] == b.Id) return true;
            if (U.CarryAmount[i] > 0) { ToStorageOrEnd(i); return true; }
            StartJob(i, UnitJob.Bring, b.Id);
            DecideBring(i, b);
            return true;
        }
        if (!b.Active || b.Def.Storage) return false;
        if (b.Def.Kind is not (BuildingKind.Workshop or BuildingKind.Smelter or BuildingKind.Orchard) && !(b.Def.IsQuarry && b.Def.Hunts == 0 && b.Def.Plants == 0))
            return true;
        if (U.Job[i] == UnitJob.Haul && U.JobTarget[i] == b.Id) return true;
        StartJob(i, UnitJob.Haul, b.Id);
        DecideHaul(i, b);
        return true;
    }

    /// <summary>The job of a worker working a building of this kind: farming, hauling for a smelter, shrine or workshop, or quarrying.</summary>
    public static UnitJob JobAt(BuildingDef def) => def.Kind switch
    {
        BuildingKind.Farm => UnitJob.Farm,
        BuildingKind.Quarry => UnitJob.Quarry,
        _ => UnitJob.Haul,
    };

    private int WorkersAt(Building b, UnitJob job)
    {
        int n = 0;
        for (int i = 0; i < U.Count; i++)
            if (U.Job[i] == job && U.JobTarget[i] == b.Id) n++;
        return n;
    }
}
