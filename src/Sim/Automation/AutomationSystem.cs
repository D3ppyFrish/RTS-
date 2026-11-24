using System;
using System.Collections.Generic;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.Automation;

/// <summary>
/// Optional automation, for players who want it: workers that find their own jobs, training that repeats,
/// soldiers that walk home when they are hurt, and town governors (see <c>AutomationSystem.Governor.cs</c>). It runs on every machine right after the tick's
/// commands, and it acts only by giving the same commands a player can give, applied as if the player had
/// given them just then. So it never does anything a player could not do by hand.
///
/// An automatic worker (<see cref="UnitStore.Auto"/>) with nothing to do takes the most useful job near
/// it. It first drops off what it carries. Then it goes down its priority list (see <see cref="WorkPriorities"/>),
/// which by default is the standard order: it helps build a site that is short of builders, works a
/// farm that has room, or supplies a smelter that has no hauler and something to smelt. Failing those, it
/// gathers the good the player needs most (see <see cref="FindGather"/>). A worker the player moves, stops
/// or sends to fight is left alone (<see cref="UnitStore.Held"/>) until it is given a job or made automatic
/// again. A player who makes all their workers automatic (<see cref="AutomateAllCommand"/>) also has
/// automation put up a house near their town hall when housing holds back training, and a farm when food
/// runs short and every farm is fully worked.
///
/// A building on repeat (<see cref="Building.Repeat"/>) queues each unit it finishes again, as soon as the
/// player can pay for it and still have a minute's food for everyone. A soldier on the retreat stance that
/// is hit while hurt walks back to its nearest town hall (see <see cref="UnitStance"/>). A unit whose
/// player has chosen a form for its type (<see cref="AutoEvolveCommand"/>) evolves into it as soon as it can.
///
/// A job that fails quickly (the worker could not get to it) is not given to anyone for a while, and the
/// worker that failed keeps away from the ground around it, since the rest of a pit or ledge it could not
/// get to is most likely out of its reach too. A worker that fails again and again is most likely stuck
/// (at the bottom of a pit, say): it waits longer after each failure instead of switching between idle and
/// working.
///
/// Which worker looks for work when, and the places it gave up on, are automation's own bookkeeping: they
/// only decide which commands it gives, so they are not part of the state hash.
/// </summary>
public sealed partial class AutomationSystem
{
    private const int Tps = Simulation.TicksPerSecond;

    /// <summary>How far (columns) an automatic worker looks for the good it needs most.</summary>
    public const int SearchRadius = 40;
    /// <summary>
    /// How far (columns) it looks when there is nothing to gather within <see cref="SearchRadius"/>: once
    /// the ground near town is cleared it walks further out rather than standing idle.
    /// </summary>
    public const int FarSearchRadius = 128;
    /// <summary>How many levels above and below its feet it looks.</summary>
    public const int SearchHeight = 12;
    /// <summary>Ticks a worker that found nothing to gather waits before it looks again.</summary>
    public const int RetryTicks = 10 * Tps;
    /// <summary>
    /// A job automation gave that ends sooner than this with its work untouched has failed (the place could
    /// not be reached, say); automation then sends nobody there for <see cref="AvoidTicks"/>.
    /// </summary>
    public const int QuickFailTicks = 15 * Tps;
    public const int AvoidTicks = 60 * Tps;
    /// <summary>
    /// Columns (each way) around a block a worker could not get to that it keeps away from for
    /// <see cref="AvoidTicks"/>.
    /// </summary>
    public const int AvoidRadius = 8;
    /// <summary>Most places one worker keeps away from at a time; a new one replaces the oldest.</summary>
    public const int MaxAvoidedAreas = 4;
    /// <summary>Longest a worker that keeps failing waits before it looks for something to gather again.</summary>
    public const int MaxBackOffTicks = 60 * Tps;
    /// <summary>A good the player has this much of is gathered only when no other good is wanted.</summary>
    public const int Plenty = 500;
    /// <summary>Food below which automatic workers favour food four to one: a worker's price.</summary>
    public const int FoodLow = 30;
    /// <summary>Food below which automation (and the governor) puts up another farm when every farm is fully worked.</summary>
    public const int FoodShort = 100;
    /// <summary>Most automatic workers on supply duty at one storage building at a time.</summary>
    public const int SupplyWorkers = 2;
    /// <summary>Ticks between a player's checks for a house or farm to build.</summary>
    public const int PlanTicks = 2 * Tps;
    /// <summary>How far (columns from the middle of the town hall) automation looks for room to build.</summary>
    public const int PlanRadius = 24;
    /// <summary>Ticks automation waits after finding no room before it looks for room again.</summary>
    public const int PlanRetryTicks = 30 * Tps;
    /// <summary>Columns kept clear in front of a door.</summary>
    public const int DoorClearance = 3;
    private const int CleanupTicks = 60 * Tps;

    /// <summary>The goods automatic workers gather, the blocks they come from, and each one's share of the gatherers.</summary>
    private static readonly (Good Good, BlockType Block, int Share)[] Gatherable =
    {
        (Good.Food, BlockType.BerryBush, 3), (Good.Wood, BlockType.Wood, 3), (Good.Stone, BlockType.Stone, 2),
        (Good.IronOre, BlockType.IronOre, 1), (Good.Coal, BlockType.Coal, 1), (Good.Gold, BlockType.GoldOre, 1),
    };

    private readonly Simulation _sim;

    // Per player, recomputed when something may have changed: goods, people and work.
    private readonly List<Stats> _stats = new();
    private bool _dirty = true;
    // Workers at each building (by id) on a build, farm, haul, supply or quarry job.
    private readonly Dictionary<int, int> _working = new();

    // Bookkeeping (not hashed): when each worker (by id) may look for work again, the last job automation
    // gave it, places nobody is sent to until a tick, and when each player may look for room again.
    private readonly Dictionary<int, long> _retry = new();
    private readonly Dictionary<int, Assignment> _assigned = new();
    private readonly Dictionary<long, long> _avoid = new();
    // Per worker (by id): its quick failures in a row, and the ground it keeps away from.
    private readonly Dictionary<int, Misses> _misses = new();
    private readonly List<long> _planRetry = new();
    private readonly List<Candidate> _ring = new();

    public AutomationSystem(Simulation sim)
    {
        _sim = sim;
    }

    /// <summary>When false automation gives no commands (tests replay what it gave with it off).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>When set, every command automation gives is added here with its tick (tests).</summary>
    public List<(long Tick, Command Command)>? Record { get; set; }

    private UnitStore U => _sim.Units;
    private BuildingStore Buildings => _sim.Buildings;
    private EconomySystem Economy => _sim.Economy;
    private VoxelWorld World => _sim.World;

    private sealed class Stats
    {
        public readonly int[] Stock = new int[Goods.Count];
        /// <summary>Workers gathering each good (farmers count for food).</summary>
        public readonly int[] Gatherers = new int[Goods.Count];
        public int Population, Workers, Housing;
        /// <summary>Units waiting to be trained: queued, or owed to a queue on repeat.</summary>
        public int Waiting;
        public int FarmSlots, FreeFarmSlots;
        public bool Smelter, HouseSite, FarmSite;
        /// <summary>The player holds a city-state town whose mercenaries cost gold: gold is worth digging.</summary>
        public bool WantsGold;

        public void Clear()
        {
            Array.Clear(Stock);
            Array.Clear(Gatherers);
            Population = Workers = Housing = Waiting = FarmSlots = FreeFarmSlots = 0;
            Smelter = HouseSite = FarmSite = WantsGold = false;
        }
    }

    /// <summary>A job automation gave a worker: when, and where its work was.</summary>
    private readonly record struct Assignment(long Tick, UnitJob Job, int Building, int X, int Y, int Z, BlockType Block);

    private readonly record struct Candidate(int Kind, long D2, int X, int Y, int Z);

    /// <summary>A worker's quick failures in a row, and the ground around them it keeps away from until a tick.</summary>
    private sealed class Misses
    {
        public int InARow;
        public readonly List<(int X, int Z, long Until)> Areas = new();
    }

    // --- The tick ----------------------------------------------------------------------------------

    /// <summary>Gives this tick's automatic commands. Called after the tick's own commands, before anything moves.</summary>
    public void Step()
    {
        if (!Enabled) return;
        long tick = _sim.Tick;
        _dirty = true;
        StepRetreats();

        // Automatic workers with nothing to do look for work once a second, spread over the ticks.
        var u = U;
        for (int i = 0; i < u.Count; i++)
        {
            if (!u.Auto[i] || u.Held[i] || u.Aboard[i] != 0 || (tick + u.Id[i]) % Tps != 0 || !Idle(i)) continue;
            int id = u.Id[i];
            CheckLastJob(id, tick);
            var job = FindJob(i, out var assignment);
            if (job == null) continue;
            Issue(job);
            if (assignment is { } a) _assigned[id] = a; // after the command, which forgets the last one
        }

        // Repeating training, once a second per building.
        var all = Buildings.All;
        for (int k = 0; k < all.Count; k++)
        {
            var b = all[k];
            if (b.Repeat && b.Owed.Count > 0 && (tick + b.Id) % Tps == 0) StepRepeat(b);
        }

        // Houses and farms, for players whose workers are all automatic.
        for (int p = 0; p < Economy.PlayerCount; p++)
            if ((tick + p * 7) % PlanTicks == 0 && Economy.AutoWorkers(p) && !Governed(p)) Plan(p);
        StepGovernors();

        StepEvolution();
        StepRebuild();

        if (tick % CleanupTicks == 0) Cleanup();
    }

    /// <summary>Applies a command now, as the player could have, and keeps it for <see cref="Record"/>.</summary>
    private void Issue(Command c)
    {
        Record?.Add((_sim.Tick, c));
        _sim.Issue(c);
        _dirty = true;
    }

    private bool IsWorker(int i) => _sim.Content.Units[U.Def[i]].Role == UnitRole.Worker;

    /// <summary>True for a cart (role <see cref="UnitRole.Hauler"/>), whose only work is supply duty.</summary>
    private bool IsCart(int i) => _sim.Content.Units[U.Def[i]].IsHauler;

    /// <summary>A cart's priority list: supply duty and nothing else.</summary>
    private static readonly ulong CartPriorities = WorkPriorities.Pack(WorkKind.Supply);

    /// <summary>True if the worker has no job, order or fight, and is not running away.</summary>
    private bool Idle(int i) =>
        U.Job[i] == UnitJob.None && U.Order[i] == UnitOrder.Idle && U.Combat[i] == CombatOrder.Auto && U.Rout[i] == 0;

    // --- Commands ----------------------------------------------------------------------------------

    /// <summary>Makes the command's workers automatic, or not; switching one on also lets go of it if the player held it.</summary>
    internal void Apply(AutomateCommand c)
    {
        foreach (int i in _sim.OwnedUnits(c))
        {
            if (!IsWorker(i) && !IsCart(i)) continue;
            U.Auto[i] = c.On;
            if (c.On) LetGo(i);
        }
    }

    /// <summary>Makes all the player's workers automatic, and those they train from now on, or none of them.</summary>
    internal void Apply(AutomateAllCommand c)
    {
        if (!Economy.HasPlayer(c.PlayerId)) return;
        Economy.Player(c.PlayerId).AutoWorkers = c.On;
        var u = U;
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Owner[i] != c.PlayerId || !IsWorker(i)) continue;
            u.Auto[i] = c.On;
            if (c.On) LetGo(i);
        }
    }

    /// <summary>Sets the priority list of the command's workers (see <see cref="WorkPriorities"/>).</summary>
    internal void Apply(PriorityCommand c)
    {
        foreach (int i in _sim.OwnedUnits(c))
        {
            if (!IsWorker(i)) continue;
            U.Priorities[i] = c.List;
            _retry.Remove(U.Id[i]); // what it may look for has changed: it looks at its next turn
        }
    }

    /// <summary>Sets the player's default priority list; <see cref="WorkPriorities.UseDefault"/> puts back the standard one.</summary>
    internal void Apply(DefaultPriorityCommand c)
    {
        if (!Economy.HasPlayer(c.PlayerId)) return;
        Economy.Player(c.PlayerId).DefaultPriorities = c.List == WorkPriorities.UseDefault ? WorkPriorities.Standard : c.List;
        var u = U;
        for (int i = 0; i < u.Count; i++)
            if (u.Owner[i] == c.PlayerId && u.Priorities[i] == WorkPriorities.UseDefault) _retry.Remove(u.Id[i]);
    }

    /// <summary>The priority list worker i goes by: its own, or else its player's default.</summary>
    public ulong PrioritiesOf(int i)
    {
        ulong own = U.Priorities[i];
        if (own != WorkPriorities.UseDefault) return own;
        return DefaultPriorities(U.Owner[i]);
    }

    /// <summary>The player's default priority list (the standard one until they set theirs). Changes nothing.</summary>
    public ulong DefaultPriorities(int player) =>
        player >= 0 && player < Economy.PlayerCount ? Economy.Player(player).DefaultPriorities : WorkPriorities.Standard;

    /// <summary>The player hands the worker (back) to automation: it looks for work at its next turn.</summary>
    private void LetGo(int i)
    {
        U.Held[i] = false;
        _retry.Remove(U.Id[i]);
        if (_misses.TryGetValue(U.Id[i], out var m)) m.InARow = 0;
    }

    /// <summary>
    /// Notes a command the units were given (by the player or by automation): a move, stop, fight, raid or
    /// tunnel holds a worker where the player wants it; a job lets automation take it over again when the
    /// job is done. Either way the job automation last gave it is over, and did not fail.
    /// </summary>
    internal void Note(UnitsCommand c)
    {
        bool held;
        switch (c)
        {
            case MoveCommand or AttackMoveCommand or FormationMoveCommand or StopCommand or AttackCommand or AttackBuildingCommand
                or CaptureCommand or BurnCommand or TunnelCommand or SearchRuinCommand:
                held = true;
                break;
            case GatherCommand or WorkCommand or BuildCommand or DigAreaCommand or WallCommand or BridgeCommand:
                held = false;
                break;
            default:
                return;
        }
        foreach (int i in _sim.OwnedUnits(c))
        {
            if (!IsWorker(i) && !IsCart(i)) continue;
            U.Held[i] = held;
            _assigned.Remove(U.Id[i]);
            // Moved out of where it was stuck, maybe: it no longer waits longer and longer.
            if (held && _misses.TryGetValue(U.Id[i], out var m)) m.InARow = 0;
        }
    }

    // --- Worker jobs -------------------------------------------------------------------------------

    /// <summary>
    /// The job for idle worker i, as a command, or null if there is nothing for it to do. Also says where
    /// the work is, to notice if the job fails.
    /// </summary>
    private Command? FindJob(int i, out Assignment? assignment)
    {
        assignment = null;
        Refresh();
        var u = U;
        int owner = u.Owner[i];
        var s = _stats[owner];
        int x = u.X[i].FloorToInt(), z = u.Z[i].FloorToInt();
        var me = new[] { u.Id[i] };
        long tick = _sim.Tick;

        // What it carries goes to storage first.
        if (u.CarryAmount[i] > 0)
        {
            var store = Economy.NearestStorage(owner, x, z);
            return store != null ? new WorkCommand(owner, me, store.Id) : null;
        }

        // The nearest building of each kind that needs a worker: a site short of builders (rank 0), a farm
        // with room (1), a smelter with no hauler (2), storage short under its supply rules (3), an orchard with
        // baskets of fruit and nobody to carry them (4).
        Span<int> nearest = stackalloc int[5];
        Span<long> nearestD = stackalloc long[5];
        nearest.Fill(-1);
        var all = Buildings.All;
        for (int k = 0; k < all.Count; k++)
        {
            var b = all[k];
            if (b.Owner != owner || Avoided(-(long)b.Id, tick)) continue;
            int rank = Wants(b, s);
            if (rank < 0) continue;
            long d = b.CentreDistance2(x, z);
            if (nearest[rank] >= 0 && d >= nearestD[rank]) continue;
            nearest[rank] = k;
            nearestD[rank] = d;
        }

        // Down the worker's priority list: the first kind of work there is some of. Looking for something
        // to gather is slow, so a worker that found nothing waits a while to look again (but takes a
        // building's work as soon as there is some).
        int id = u.Id[i];
        bool mayGather = !_retry.TryGetValue(id, out long at) || tick >= at;
        bool looked = false;
        Span<long> need = stackalloc long[Gatherable.Length];
        foreach (var kind in WorkPriorities.Unpack(IsCart(i) ? CartPriorities : PrioritiesOf(i)))
        {
            int rank = kind switch { WorkKind.Build => 0, WorkKind.Farm => 1, WorkKind.Smelt => 2, WorkKind.Supply => 3, _ => -1 };
            // Carrying an orchard's fruit in is farm work, after the farms; and a cart's, after its supply duty.
            if (rank == 1 && (IsCart(i) || nearest[1] < 0) || rank == 3 && IsCart(i) && nearest[3] < 0) rank = 4;
            if (rank >= 0)
            {
                if (nearest[rank] < 0)
                {
                    // No site wants a builder: clearing a ruin's rubble so it can be rebuilt is building work too.
                    if (rank == 0 && ClearRuin(i, out assignment) is { } clear) return clear;
                    continue;
                }
                var best = all[nearest[rank]];
                var job = rank switch { 0 => UnitJob.Build, 1 => UnitJob.Farm, 2 or 4 => UnitJob.Haul, _ => UnitJob.Supply };
                assignment = new Assignment(tick, job, best.Id, 0, 0, 0, BlockType.Air);
                return new WorkCommand(owner, me, best.Id);
            }
            int only = kind == WorkKind.Needed ? -1 : GatherIndex(WorkPriorities.GoodOf(kind)!.Value);
            // The good wanted most comes from a quarry or mine with a free place, if the player has one.
            int top = Needs(s, only, need);
            if (top >= 0 && FreeQuarry(owner, Gatherable[top].Good, x, z, tick) is { } quarry)
            {
                assignment = new Assignment(tick, UnitJob.Quarry, quarry.Id, 0, 0, 0, BlockType.Air);
                return new WorkCommand(owner, me, quarry.Id);
            }
            if (!mayGather) continue;
            looked = true;
            if (!FindGather(i, s, only, out int gx, out int gy, out int gz)) continue;
            _retry.Remove(id);
            assignment = new Assignment(tick, UnitJob.Gather, 0, gx, gy, gz, World.Get(gx, gy, gz));
            return new GatherCommand(owner, me, gx, gy, gz);
        }
        if (looked) _retry[id] = tick + RetryTicks;
        return null;
    }

    /// <summary>The index in <see cref="Gatherable"/> of a good.</summary>
    private static int GatherIndex(Good good)
    {
        for (int k = 0; k < Gatherable.Length; k++)
            if (Gatherable[k].Good == good) return k;
        return -1;
    }

    /// <summary>
    /// How much building b wants another worker: 0 for a site short of builders, 1 for a farm with room,
    /// 2 for a smelter with no hauler and something to smelt or carry away (a shrine with crystal to channel, a workshop with something to work while what it makes is short), 3 for storage short under its
    /// supply rules with fewer than <see cref="SupplyWorkers"/> on supply duty, 4 for an orchard with baskets of fruit
    /// waiting and nobody carrying them; -1 if it wants none.
    /// </summary>
    private int Wants(Building b, Stats s)
    {
        if (b.Planned) return -1; // not started: the player starts it
        int working = _working.GetValueOrDefault(b.Id);
        // A bridge's deck is built from beside it: not before there is ground or deck to stand on there.
        if (b.WantsBuilders) return working < MaxBuilders(b.Upgrade ?? b.Def) && (!b.Def.IsBridge || Economy.BridgeAccess(b, out _, out _, out _)) ? 0 : -1;
        if (b.Def.Storage) return b.HasSupply && working < SupplyWorkers && Economy.WantsSupply(b) ? 3 : -1;
        if (b.Def.Kind == BuildingKind.Farm) return working < b.Def.WorkerSlots ? 1 : -1;
        if (b.Def.Kind == BuildingKind.Orchard) return working < b.Def.WorkerSlots && EconomySystem.Baskets(b) > 0 ? 4 : -1;
        if (b.Def.Kind == BuildingKind.Shrine)
            return working < b.Def.WorkerSlots && b.Stock[(int)Good.Crystal] + s.Stock[(int)Good.Crystal] > 0 ? 2 : -1;
        if (b.Def.Kind == BuildingKind.Workshop)
            return working < b.Def.WorkerSlots && (b.Stock[(int)b.Def.Makes] > 0 || b.Stock[(int)b.Def.Takes] > 0
                || s.Stock[(int)b.Def.Takes] > 0 && s.Stock[(int)b.Def.Makes] < b.Def.Keep) ? 2 : -1;
        if (b.Def.Kind != BuildingKind.Smelter || working >= b.Def.WorkerSlots) return -1;
        bool inputs = b.Stock[(int)Good.IronOre] + s.Stock[(int)Good.IronOre] >= EconomySystem.OrePerIngot
                      && b.Stock[(int)Good.Coal] + s.Stock[(int)Good.Coal] >= EconomySystem.CoalPerIngot;
        return inputs || b.Stock[(int)Good.Iron] > 0 ? 2 : -1;
    }

    /// <summary>Most automatic workers sent to build a site: one, and one more for each half minute of work.</summary>
    public static int MaxBuilders(BuildingDef def) => 1 + def.BuildTicks / (30 * Tps);

    /// <summary>
    /// Each good's need (see <see cref="FindGather"/>), by index in <see cref="Gatherable"/>, into
    /// <paramref name="need"/> (0 for a good not wanted), and the index of the most needed; -1 if none is.
    /// </summary>
    private static int Needs(Stats s, int only, Span<long> need)
    {
        int top = -1;
        need.Clear();
        for (int k = 0; k < Gatherable.Length; k++)
        {
            var (good, _, share) = Gatherable[k];
            if (only >= 0 ? k != only : good is Good.IronOre or Good.Coal && !s.Smelter || good == Good.Gold && !s.WantsGold) continue;
            long n = share * 1000L / (s.Gatherers[(int)good] + 1);
            if (good == Good.Food && s.Stock[(int)Good.Food] < FoodLow)
                n *= 4;
            if (s.Stock[(int)good] >= Plenty) n /= 16;
            need[k] = Math.Max(1, n);
            if (top < 0 || need[k] > need[top]) top = k;
        }
        return top;
    }

    /// <summary>
    /// The nearest of the player's finished quarries and mines to (x, z) that gives <paramref name="good"/> and has a
    /// free place, or null. Gathering a good such a building gives is done there rather than block by block: it
    /// never runs out.
    /// </summary>
    private Building? FreeQuarry(int owner, Good good, int x, int z, long tick)
    {
        Building? best = null;
        long bestD = 0;
        foreach (var b in Buildings.All)
        {
            if (b.Owner != owner || !b.Def.IsQuarry || !b.Active || !b.Def.Gives(good)) continue;
            if (_working.GetValueOrDefault(b.Id) >= b.Def.WorkerSlots || Avoided(-(long)b.Id, tick)) continue;
            long d = b.CentreDistance2(x, z);
            if (best != null && d >= bestD) continue;
            (best, bestD) = (b, d);
        }
        return best;
    }

    /// <summary>
    /// The block for worker i to gather, if any: of the goods the player could use, the one with the most
    /// need, at its nearest block within <see cref="SearchRadius"/> columns and <see cref="SearchHeight"/>
    /// levels that no other worker has claimed and that a worker can stand next to (for a tree, the base
    /// of its trunk). With <paramref name="only"/> (an index in <see cref="Gatherable"/>) it looks for that
    /// good alone, smelter or not; with -1, for all of them by need. A good's need is its share of the gatherers (food and wood 3, stone 2, iron ore and
    /// coal 1, those two only with a smelter; gold 1, only while the player holds a town whose mercenaries cost gold) over its gatherers plus one; four times that for food when
    /// the player has less than <see cref="FoodLow"/>, and a sixteenth for a good they have
    /// <see cref="Plenty"/> of. The search goes out ring by ring and stops once the most needed good is found.
    /// If there is nothing to gather within <see cref="SearchRadius"/> it goes on out to
    /// <see cref="FarSearchRadius"/>, and takes the first ring with anything in it.
    /// </summary>
    private bool FindGather(int i, Stats s, int only, out int fx, out int fy, out int fz)
    {
        fx = fy = fz = -1;
        var u = U;
        int ux = u.X[i].FloorToInt(), uy = u.Y[i], uz = u.Z[i].FloorToInt();
        int kinds = Gatherable.Length;
        Span<long> need = stackalloc long[kinds];
        int top = Needs(s, only, need);
        if (top < 0) return false;

        Span<bool> have = stackalloc bool[kinds];
        Span<int> hx = stackalloc int[kinds];
        Span<int> hy = stackalloc int[kinds];
        Span<int> hz = stackalloc int[kinds];
        int wanted = 0;
        for (int k = 0; k < kinds; k++)
            if (need[k] > 0) wanted++;
        var w = World;
        long tick = _sim.Tick;
        _misses.TryGetValue(u.Id[i], out var misses);
        bool any = false;
        for (int r = 0; r <= FarSearchRadius && !have[top] && wanted > 0 && !(any && r > SearchRadius); r++)
        {
            _ring.Clear();
            for (int dz = -r; dz <= r; dz++)
            {
                int cz = uz + dz;
                if (cz < 0 || cz >= w.SizeZ) continue;
                int step = dz == -r || dz == r ? 1 : 2 * r;
                for (int dx = -r; dx <= r; dx += step)
                {
                    int cx = ux + dx;
                    if (cx < 0 || cx >= w.SizeX) continue;
                    for (int y = Math.Max(1, uy - SearchHeight); y <= Math.Min(w.SizeY - 1, uy + SearchHeight); y++)
                    {
                        int k = KindOf(w.Get(cx, y, cz));
                        if (k < 0 || need[k] == 0 || have[k]) continue;
                        _ring.Add(new Candidate(k, (long)dx * dx + (long)dz * dz + 2L * (y - uy) * (y - uy), cx, y, cz));
                    }
                }
            }
            if (_ring.Count == 0) continue;
            _ring.Sort((a, b) => a.D2 != b.D2 ? a.D2.CompareTo(b.D2) : a.Y != b.Y ? b.Y.CompareTo(a.Y)
                : a.Z != b.Z ? a.Z.CompareTo(b.Z) : a.X.CompareTo(b.X));
            foreach (var c in _ring)
            {
                if (have[c.Kind] || KeptAway(misses, c.X, c.Z, tick) || !CanSend(c, ux, uy, uz, tick)) continue;
                have[c.Kind] = true;
                (hx[c.Kind], hy[c.Kind], hz[c.Kind]) = (c.X, c.Y, c.Z);
                any = true;
                wanted--;
            }
        }

        int pick = -1;
        for (int k = 0; k < kinds; k++)
            if (have[k] && (pick < 0 || need[k] > need[pick])) pick = k;
        if (pick < 0) return false;
        (fx, fy, fz) = (hx[pick], hy[pick], hz[pick]);
        return true;
    }

    /// <summary>The index in <see cref="Gatherable"/> of the good a block gives, or -1.</summary>
    private static int KindOf(BlockType b)
    {
        if (BlockInfo.IsTrunk(b)) b = BlockType.Wood; // every tree is cut for wood
        for (int k = 0; k < Gatherable.Length; k++)
            if (Gatherable[k].Block == b) return k;
        return -1;
    }

    /// <summary>
    /// True if a worker at (ux, uy, uz) can be sent to gather candidate c: the base of a trunk if it is a
    /// tree, open to the air, not under a building, not claimed or given up on, and with somewhere to stand
    /// (for stone, ore and coal: beside it, somewhere it can walk out of again once the block is dug).
    /// </summary>
    private bool CanSend(Candidate c, int ux, int uy, int uz, long tick)
    {
        var w = World;
        var block = Gatherable[c.Kind].Block;
        if (BlockInfo.IsTrunk(block) && BlockInfo.IsTrunk(w.Get(c.X, c.Y - 1, c.Z))) return false;
        if (!Harvest.Exposed(w, c.X, c.Y, c.Z, block) || Buildings.AtColumn(c.X, c.Z) != null) return false;
        if (Economy.Claimed(c.X, c.Y, c.Z) || Avoided(Harvest.Key(w, c.X, c.Y, c.Z), tick)) return false;
        var (baseY, topY) = Harvest.Span(w, c.X, c.Y, c.Z);
        bool quarry = Harvest.Quarried(block);
        return Harvest.WorkSpot(w, c.X, baseY, topY, c.Z, ux, uy, uz, out _, out _, out _, allowOnTop: !quarry, wayOut: quarry);
    }

    /// <summary>
    /// A worker automation gave a job to is idle again: if that happened soon after, with the work still
    /// there, the job failed. Nobody is sent there for a while, the worker keeps away from the ground around
    /// a block it could not get to, and from its second failure in a row it waits before it looks for
    /// something to gather again, twice as long each time (see <see cref="MaxBackOffTicks"/>). A job that
    /// did not fail starts the count of failures again.
    /// </summary>
    private void CheckLastJob(int id, long tick)
    {
        if (!_assigned.Remove(id, out var a)) return;
        bool untouched;
        long key;
        if (a.Job == UnitJob.Gather)
        {
            untouched = World.Get(a.X, a.Y, a.Z) == a.Block;
            key = Harvest.Key(World, a.X, a.Y, a.Z);
        }
        else
        {
            var b = Buildings.Get(a.Building);
            untouched = b != null && a.Job switch
            {
                UnitJob.Build => b.WantsBuilders,
                UnitJob.Supply => Economy.WantsSupply(b), // it left while there was still supply work there
                _ => b.Active,
            };
            key = -(long)a.Building;
        }
        if (tick - a.Tick >= QuickFailTicks || !untouched)
        {
            // It got to its work: the count starts again (the ground it keeps away from stays so).
            if (_misses.TryGetValue(id, out var fine)) fine.InARow = 0;
            return;
        }
        _avoid[key] = tick + AvoidTicks;
        if (!_misses.TryGetValue(id, out var m)) _misses[id] = m = new Misses();
        m.InARow++;
        if (a.Job == UnitJob.Gather)
        {
            if (m.Areas.Count >= MaxAvoidedAreas) m.Areas.RemoveAt(0);
            m.Areas.Add((a.X, a.Z, tick + AvoidTicks));
        }
        if (m.InARow >= 2)
            _retry[id] = tick + Math.Min(MaxBackOffTicks, (long)RetryTicks << Math.Min(m.InARow - 2, 8));
    }

    /// <summary>True if (x, z) is in ground the worker keeps away from (see <see cref="CheckLastJob"/>).</summary>
    private static bool KeptAway(Misses? m, int x, int z, long tick)
    {
        if (m == null) return false;
        foreach (var (ax, az, until) in m.Areas)
            if (tick < until && Math.Abs(x - ax) <= AvoidRadius && Math.Abs(z - az) <= AvoidRadius) return true;
        return false;
    }

    private bool Avoided(long key, long tick) => _avoid.TryGetValue(key, out long until) && tick < until;

    // --- Repeat ------------------------------------------------------------------------------------

    /// <summary>Queues the oldest unit the building is owed, once there is room in the queue and the player can pay and still eat.</summary>
    private void StepRepeat(Building b)
    {
        if (!b.Active || b.Queue.Count >= EconomySystem.MaxQueue) return;
        var def = _sim.Content.Units[b.Owed[0]];
        if (!Economy.Unlocked(b.Owner, def)) return;
        Refresh();
        var s = _stats[b.Owner];
        foreach (var c in def.Cost)
        {
            int left = s.Stock[(int)c.Good] - c.Amount;
            if (left < 0) return;
        }
        Issue(new TrainCommand(b.Owner, b.Id, def.Id));
    }

    // --- Houses and farms --------------------------------------------------------------------------

    /// <summary>
    /// For a player whose workers are all automatic: a house when the units waiting to be trained will not
    /// fit, or a farm when food is short and every farm is fully worked (up to a farm place for every third
    /// worker). One site of each at a time, near the town hall; automatic workers then build it.
    /// </summary>
    private void Plan(int p)
    {
        long tick = _sim.Tick;
        while (_planRetry.Count <= p) _planRetry.Add(0);
        if (tick < _planRetry[p]) return;
        var hall = Economy.TownHall(p);
        if (hall == null) return;
        Refresh();
        if (p >= _stats.Count) return;
        var s = _stats[p];
        BuildingDef? def = null;
        if (!s.HouseSite && s.Waiting > 0 && s.Population + s.Waiting >= s.Housing) def = Unlocked(p, BuildingKind.House);
        bool foodShort = s.Stock[(int)Good.Food] < FoodShort;
        if (def == null && !s.FarmSite && foodShort && s.FreeFarmSlots == 0 && s.FarmSlots < Math.Max(2, s.Workers / 3))
            def = Unlocked(p, BuildingKind.Farm);
        if (def == null || !Economy.CanAfford(p, def.Cost)) return;
        if (Room(p, def, hall, out int x, out int z)) Issue(new BuildCommand(p, Array.Empty<int>(), def.Id, x, z));
        else _planRetry[p] = tick + PlanRetryTicks;
    }

    /// <summary>The first building of that kind (in data order) the player may build, or null.</summary>
    private BuildingDef? Unlocked(int p, BuildingKind kind)
    {
        foreach (var d in _sim.Content.Buildings)
            if (d.Kind == kind && d.UpgradeOf == null && Economy.Unlocked(p, d)) return d;
        return null;
    }

    /// <summary>
    /// The nearest spot to the middle of the town hall, ring by ring out to <see cref="PlanRadius"/>, where
    /// the building fits without standing in front of a door or having its own door blocked.
    /// </summary>
    private bool Room(int p, BuildingDef def, Building hall, out int bx, out int bz) =>
        FindRoom(p, def, hall.X + hall.Def.SizeX / 2, hall.Z + hall.Def.SizeZ / 2, PlanRadius, out bx, out bz);

    /// <summary>
    /// The corner of the building's footprint nearest to having its middle on column (cx, cz), ring by ring
    /// out to <paramref name="radius"/> columns, where the player can put it now (see
    /// <see cref="EconomySystem.CanPlace"/>) without it standing in front of a door or having its own door
    /// blocked. False if there is no such spot. Computer players place their buildings with this too, and
    /// can rule spots out with <paramref name="allowed"/> (given the footprint's corner).
    /// </summary>
    public bool FindRoom(int p, BuildingDef def, int cx, int cz, int radius, out int bx, out int bz, Func<int, int, bool>? allowed = null)
    {
        bx = bz = -1;
        for (int r = 1; r <= radius; r++)
        {
            long best = long.MaxValue;
            for (int dz = -r; dz <= r; dz++)
            {
                int step = dz == -r || dz == r ? 1 : 2 * r;
                for (int dx = -r; dx <= r; dx += step)
                {
                    long d = (long)dx * dx + (long)dz * dz;
                    if (d >= best) continue;
                    int x = cx + dx - def.SizeX / 2, z = cz + dz - def.SizeZ / 2;
                    if (allowed != null && !allowed(x, z)) continue;
                    if (Economy.CanPlace(p, def, x, z, out _) != PlaceResult.Ok || BlocksDoor(def, x, z)) continue;
                    best = d;
                    (bx, bz) = (x, z);
                }
            }
            if (best != long.MaxValue) return true;
        }
        return false;
    }

    /// <summary>True if a building with its corner at (x, z) would stand in front of a door, or have another building in front of its own.</summary>
    private bool BlocksDoor(BuildingDef def, int x, int z)
    {
        int x1 = x + def.SizeX - 1, z1 = z + def.SizeZ - 1;
        foreach (var b in Buildings.All)
        {
            if (!b.Def.Walkable && Overlap(x, z, x1, z1, b.X, b.MaxZ + 1, b.MaxX, b.MaxZ + DoorClearance)) return true;
            if (!def.Walkable && Overlap(b.X, b.Z, b.MaxX, b.MaxZ, x, z1 + 1, x1, z1 + DoorClearance)) return true;
        }
        return false;
    }

    private static bool Overlap(int ax0, int az0, int ax1, int az1, int bx0, int bz0, int bx1, int bz1) =>
        ax0 <= bx1 && ax1 >= bx0 && az0 <= bz1 && az1 >= bz0;

    // --- Retreat -----------------------------------------------------------------------------------

    /// <summary>
    /// Soldiers on the retreat stance that were hit last tick while hurt walk to their nearest town hall,
    /// unless they are near it or on their way already: one move order for each player and hall.
    /// </summary>
    private void StepRetreats()
    {
        var u = U;
        SortedDictionary<long, List<int>>? groups = null;
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Stance[i] != UnitStance.Retreat || u.Aboard[i] != 0 || u.HitBy[i] == 0 || u.Rout[i] > 0 || !_sim.Combat.Hurt(i)) continue;
            int x = u.X[i].FloorToInt(), z = u.Z[i].FloorToInt();
            var hall = NearestHall(u.Owner[i], x, z);
            if (hall == null || hall.ColumnDistance(x, z) <= CombatSystem.HomeRadius) continue;
            if (u.Order[i] == UnitOrder.Move && !u.FightsOnTheWay(i)
                && hall.ColumnDistance(u.OrderX[i], u.OrderZ[i]) <= CombatSystem.HomeRadius)
                continue;
            groups ??= new SortedDictionary<long, List<int>>();
            long key = (long)u.Owner[i] << 32 | (uint)hall.Id;
            if (!groups.TryGetValue(key, out var ids)) groups[key] = ids = new List<int>();
            ids.Add(u.Id[i]);
        }
        if (groups == null) return;
        foreach (var (key, ids) in groups)
        {
            var hall = Buildings.Get((int)(key & 0xFFFFFFFFL))!;
            if (Economy.AccessSpot(hall, out int x, out int z, out int y))
                Issue(new MoveCommand((int)(key >> 32), ids.ToArray(), x, z, y));
        }
    }

    /// <summary>The player's finished town hall nearest to column (x, z) (ties to the lower id), or null.</summary>
    private Building? NearestHall(int player, int x, int z)
    {
        Building? best = null;
        long bestD = long.MaxValue;
        foreach (var b in Buildings.All)
        {
            if (b.Owner != player || !b.Active || b.Def.Kind != BuildingKind.TownHall) continue;
            long d = b.CentreDistance2(x, z);
            if (d >= bestD) continue;
            best = b;
            bestD = d;
        }
        return best;
    }

    // --- Evolution ---------------------------------------------------------------------------------

    /// <summary>
    /// Once a second per unit: units whose player has chosen a form for their type take it as soon as they
    /// can, one evolve order for each player and form.
    /// </summary>
    private void StepEvolution()
    {
        var evolution = _sim.Evolution;
        if (!evolution.AnyAutoForms()) return;
        var u = U;
        long tick = _sim.Tick;
        SortedDictionary<long, List<int>>? groups = null;
        for (int i = 0; i < u.Count; i++)
        {
            if ((tick + u.Id[i]) % Tps != 0) continue;
            var form = evolution.AutoForm(u.Owner[i], _sim.Content.Units[u.Def[i]]);
            if (form == null || !evolution.CanEvolve(i, form)) continue;
            groups ??= new SortedDictionary<long, List<int>>();
            long key = (long)u.Owner[i] << 32 | (uint)form.Index;
            if (!groups.TryGetValue(key, out var ids)) groups[key] = ids = new List<int>();
            ids.Add(u.Id[i]);
        }
        if (groups == null) return;
        foreach (var (key, ids) in groups)
            Issue(new EvolveCommand((int)(key >> 32), ids.ToArray(), _sim.Content.Units[(int)(key & 0xFFFFFFFFL)].Id));
    }

    // --- Bookkeeping -------------------------------------------------------------------------------

    /// <summary>Recounts each player's goods, people and work, if anything may have changed since the last count.</summary>
    private void Refresh()
    {
        if (!_dirty) return;
        _dirty = false;
        var u = U;
        int players = 0;
        for (int i = 0; i < u.Count; i++) players = Math.Max(players, u.Owner[i] + 1);
        foreach (var b in Buildings.All) players = Math.Max(players, b.Owner + 1);
        while (_stats.Count < players) _stats.Add(new Stats());
        foreach (var s in _stats) s.Clear();
        _working.Clear();
        for (int i = 0; i < u.Count; i++)
        {
            var s = _stats[u.Owner[i]];
            s.Population++;
            if (IsCart(i))
            {
                // A cart on supply duty counts against the building's supply workers like a worker does.
                if (u.Job[i] is UnitJob.Supply or UnitJob.Haul) _working[u.JobTarget[i]] = _working.GetValueOrDefault(u.JobTarget[i]) + 1;
                continue;
            }
            if (!IsWorker(i)) continue;
            s.Workers++;
            if (u.Job[i] == UnitJob.Gather && Harvest.Yields(u.JobBlock[i], out var good)) s.Gatherers[(int)good]++;
            else if (u.Job[i] == UnitJob.Farm) s.Gatherers[(int)Good.Food]++;
            else if (u.Job[i] == UnitJob.Quarry && Buildings.Get(u.JobTarget[i]) is { } q)
            {
                foreach (var w in q.Def.Works) s.Gatherers[(int)w.Good]++;
                if (q.Def.Hunts > 0) s.Gatherers[(int)Good.Food]++;
            }
            if (u.Job[i] is UnitJob.Build or UnitJob.Farm or UnitJob.Haul or UnitJob.Supply or UnitJob.Quarry)
                _working[u.JobTarget[i]] = _working.GetValueOrDefault(u.JobTarget[i]) + 1;
        }
        foreach (var b in Buildings.All)
        {
            var s = _stats[b.Owner];
            s.Waiting += b.Queue.Count + b.Owed.Count;
            if (b.State == BuildingState.Site)
            {
                if (b.Def.Kind == BuildingKind.House) s.HouseSite = true;
                if (b.Def.Kind == BuildingKind.Farm) s.FarmSite = true;
                continue;
            }
            if (b.Planned) continue;
            s.Housing += Economy.HousingOf(b);
            if (b.Def.Storage)
                for (int g = 0; g < Goods.Count; g++) s.Stock[g] += b.Stock[g];
            if (b.Def.Kind == BuildingKind.Farm)
            {
                s.FarmSlots += b.Def.WorkerSlots;
                s.FreeFarmSlots += Math.Max(0, b.Def.WorkerSlots - _working.GetValueOrDefault(b.Id));
            }
            if (b.Def.Kind == BuildingKind.Smelter) s.Smelter = true;
        }
        foreach (var town in _sim.Cities.Towns)
            if (town.Holder >= 0 && town.Holder < _stats.Count && Pays(town, Good.Gold)) _stats[town.Holder].WantsGold = true;
    }

    /// <summary>True if any of the town's mercenaries costs <paramref name="good"/>.</summary>
    private static bool Pays(CityState town, Good good)
    {
        foreach (var m in town.Def.Mercenaries)
            foreach (var c in m.Cost)
                if (c.Good == good) return true;
        return false;
    }

    /// <summary>Forgets workers that are gone and places that may be tried again.</summary>
    private void Cleanup()
    {
        long tick = _sim.Tick;
        var gone = new List<int>();
        foreach (int id in _retry.Keys)
            if (!U.Exists(id)) gone.Add(id);
        foreach (int id in _assigned.Keys)
            if (!U.Exists(id)) gone.Add(id);
        foreach (int id in _misses.Keys)
            if (!U.Exists(id)) gone.Add(id);
        foreach (int id in gone)
        {
            _retry.Remove(id);
            _assigned.Remove(id);
            _misses.Remove(id);
        }
        var expired = new List<long>();
        foreach (var (key, until) in _avoid)
            if (until <= tick) expired.Add(key);
        foreach (long key in expired) _avoid.Remove(key);
    }
}
