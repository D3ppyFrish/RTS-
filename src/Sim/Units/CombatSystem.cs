using System;
using System.Collections.Generic;
using Rts.Sim.Content;
using Rts.Sim.Economy;

namespace Rts.Sim.Units;

/// <summary>
/// One hit, kept for a few ticks so the game can draw it: from the attacker's feet to the target's (or the
/// building's nearest face), and whether it was shot from range or blasts what is around it. On a unit it
/// also says the part it landed on (null for a miss or a building), the hit points it took off after armour,
/// and whether a shield stopped it. A stone dropped through a gate's murder holes falls straight down from it, by
/// no unit (<see cref="Dropped"/>). Not part of the game state.
/// </summary>
public readonly record struct Shot(long Tick, int Attacker, Fix64 FromX, int FromY, Fix64 FromZ, Fix64 ToX, int ToY, Fix64 ToZ, bool Ranged, bool Splash,
    BodyPart? Part = null, int Damage = 0, bool Blocked = false, bool Dropped = false, bool Web = false);

/// <summary>
/// Fighting, once per tick after the economy and before pathfinding and movement.
///
/// A soldier that stands still fights the nearest enemy within its sight, or one that hits it; an attack
/// order names a unit or building to fight wherever it goes; an attack-move walks somewhere and fights what
/// it meets on the way. A unit out of reach of its target chases it (<see cref="UnitOrder.Chase"/>); in
/// reach it stands and hits once per attack time. Units on a plain move or a job do not fight, and workers
/// fight only when ordered to. A soldier's stance (<see cref="UnitStance"/>) changes what it does without
/// an order: one holding its ground fights only what it can reach from where it stands and never chases,
/// and so does one on the retreat stance once it is hurt (automation walks it home when it is hit).
///
/// A hit deals the unit's attack times the role table's multiplier (<see cref="CombatRules"/>), then lands on a
/// part of the body (<see cref="Body"/>): its odds follow the part's size and the angle (a shot from the high
/// ground or a blow from above finds more heads, a blow struck uphill more legs), the part multiplies the
/// damage (the head most), and the target's armour there takes points off, more or less by the attack's kind
/// (slash, pierce, crush), never below a quarter of the hit. A shield (<see cref="UnitDef.Shield"/>) can stop a
/// whole hit from in front of its bearer, so flanking a shield wall pays. Each hit leaves a wound on its part
/// (<see cref="UnitStore.Wounds"/>), a bleeding one for slash and pierce; healing mends wounds with hit points. Ranged units (reach over <see cref="MeleeReach"/>) need a
/// clear line to the target, and with <see cref="HighGround"/> levels or more over it they reach and see a
/// quarter further. With day and night on, units see less far in the dark, bad weather and mist, and a
/// ranged shot at a unit can miss (see <see cref="Conditions"/>). All the hits of a tick land together, and so do the morale changes of its deaths, so
/// neither side strikes first.
///
/// Morale: when a unit dies, every ally near it loses a share of <see cref="Shock"/> (a bigger share the
/// fewer they are) and every enemy near it gains a share of <see cref="Cheer"/>. A unit whose morale falls
/// to <see cref="BreakMorale"/> routs: it drops what it was doing and runs home, or away from the enemy, for
/// <see cref="RoutTicks"/>, ignoring orders, then rallies. Morale comes back a point a second, up to
/// <see cref="MaxMorale"/>. Monsters never break.
///
/// Healing over time: a unit that has gone a while without hitting anything or being hit gets back a share of
/// its most hit points every second, a bigger share at home, near a town hall or house of its own (the role
/// table's recovery numbers, <see cref="CombatRules"/>). Healers heal on top of this.
///
/// A wild monster that a unit of a taming faction would kill is beaten instead (see <see cref="MonsterSystem"/>):
/// it is not removed but changes sides and lies low. Beaten monsters do not fight, and nobody picks one as a
/// target on their own.
///
/// Buildings and their damage are in CombatSystem.Structures.cs, sapper tunnels in CombatSystem.Tunnels.cs, gates'
/// murder holes in CombatSystem.MurderHoles.cs.
/// All numbers are starting guesses to be tuned in playtests.
/// </summary>
public sealed partial class CombatSystem
{
    private const int Tps = Simulation.TicksPerSecond;

    /// <summary>A unit whose range is over this (blocks) is a ranged unit.</summary>
    public static readonly Fix64 MeleeReach = Fix64.FromInt(2);
    /// <summary>Levels a ranged unit must stand above its target to have the high ground.</summary>
    public const int HighGround = 2;
    /// <summary>Range and sight on the high ground, as a multiple of the usual.</summary>
    public static readonly Fix64 HighGroundBonus = Fix64.FromRatio(5, 4);
    /// <summary>Most levels between a melee attacker and the unit it hits.</summary>
    public const int MeleeHeight = 1;
    /// <summary>Most levels between a melee unit and an enemy it goes after on its own.</summary>
    public const int MeleeSearchHeight = 3;
    /// <summary>A target a unit found itself is let go beyond this many times its sight.</summary>
    public static readonly Fix64 Leash = Fix64.FromRatio(3, 2);
    /// <summary>A unit answers a hit from an enemy up to this many times its sight away.</summary>
    public const int AnswerRange = 2;
    /// <summary>Ticks between a unit's looks for an enemy to fight (spread over units by id).</summary>
    public const int AcquireInterval = 4;
    /// <summary>Ticks between a ranged unit's checks for a clear line while it has none.</summary>
    public const int LineCheckInterval = 5;
    /// <summary>Ticks between a chaser's re-aims at a target that moved.</summary>
    public const int ReaimInterval = 5;
    /// <summary>Chases in a row that may end out of reach before the unit gives up on its target.</summary>
    public const int MaxChaseRetries = 3;
    /// <summary>Ticks a unit waits after giving up on a target before it looks for another.</summary>
    public const int GiveUpTicks = 3 * Tps;
    /// <summary>A unit on the retreat stance is hurt once its hit points are this percentage of its most, or less.</summary>
    public const int RetreatPercent = 33;

    public const int MaxMorale = 100;
    /// <summary>A unit whose morale falls to this routs.</summary>
    public const int BreakMorale = 25;
    /// <summary>Morale a unit has at least when it rallies after routing.</summary>
    public const int RallyMorale = 50;
    /// <summary>Morale lost, shared among the allies near a unit that dies (itself included).</summary>
    public const int Shock = 150;
    /// <summary>Morale gained, shared among the enemies near a unit that dies.</summary>
    public const int Cheer = 60;
    /// <summary>How near (blocks) a death has to be to touch a unit's morale.</summary>
    public static readonly Fix64 MoraleRadius = Fix64.FromInt(8);
    /// <summary>Most levels between a death and a unit whose morale it touches.</summary>
    public const int MoraleHeight = 4;
    /// <summary>Ticks a broken unit runs before it rallies.</summary>
    public const int RoutTicks = 8 * Tps;
    /// <summary>A routing unit further than this (columns) from its town hall runs to it; nearer, it runs from the enemy.</summary>
    public const int HomeRadius = 12;
    /// <summary>How far (blocks) a routing unit runs from the nearest enemy when it does not run home.</summary>
    public const int FleeDistance = 16;
    /// <summary>Ticks a hit stays in <see cref="Shots"/>.</summary>
    public const int ShotTicks = 10;

    // Units are bucketed by player in cells of this many columns, so a unit looking for enemies skips its own side.
    private const int CellSize = 8;

    private readonly Simulation _sim;
    private readonly int _cellsX, _cellsZ;
    private int _players = 1;
    private int[] _head = Array.Empty<int>();
    private int[] _next = Array.Empty<int>();
    // This tick's damage to each unit (by index), the nearest unit (id) to hit it and how far off that was
    // (squared), and the change to its morale from this tick's deaths. They all land together.
    private int[] _pending = Array.Empty<int>();
    // Units hurt by falling ground since the last tick (see World.PhysicsSystem), by id: taken with this tick's hits.
    private readonly List<(int Id, int Damage)> _crushed = new();
    private int[] _hitBy = Array.Empty<int>();
    private Fix64[] _hitFrom = Array.Empty<Fix64>();
    private int[] _cheer = Array.Empty<int>();
    // This tick's steadying by heroes (see UnitDef.Rally): the percent less morale each unit loses to deaths,
    // the morale it gets back a second and, for a ranged unit, the percent more damage it deals, the best of
    // the heroes near it.
    private int[] _steady = Array.Empty<int>();
    private int[] _inspire = Array.Empty<int>();
    private int[] _volley = Array.Empty<int>();
    private bool _anyRally;
    // This tick's heroes that lead the charge (see UnitDef.Spur), with where they stood when the tick began:
    // the movement after combat speeds up their own side's cavalry near them.
    private readonly List<(int Owner, Fix64 X, int Y, Fix64 Z, Fix64 Range, int Percent)> _spurs = new();
    // This tick's heroes that work the breach (see UnitDef.Undermine), likewise: their own side's sappers near
    // them hit buildings harder this tick and dig faster (tunnels this tick, dig areas the next).
    private readonly List<(int Owner, Fix64 X, int Y, Fix64 Z, Fix64 Range, int Percent)> _undermines = new();
    // This tick's heroes that quicken the spells (see UnitDef.Quicken), likewise: a spell their own side's
    // casters near them cast is ready again sooner.
    private readonly List<(int Owner, Fix64 X, int Y, Fix64 Z, Fix64 Range, int Percent)> _quickens = new();
    // This tick's heroes that drive the work (see UnitDef.Hasten), likewise: their own side's workers near them
    // work faster (read by the economy the next tick, which steps before combat).
    private readonly List<(int Owner, Fix64 X, int Y, Fix64 Z, Fix64 Range, int Percent)> _hastens = new();
    // This tick's bleeding (see StepBleeding): hit points lost to wounds, landing with the tick's hits.
    private int[] _bled = Array.Empty<int>();
    private readonly List<int> _dead = new();
    // This tick's beaten monsters: (unit id, the player whose unit beat it).
    private readonly List<(int Id, int Player)> _tamed = new();
    private readonly List<int> _near = new();
    // This tick's homes, where units heal faster: town halls and houses (found when first needed).
    private readonly List<Building> _homes = new();
    private bool _homesFound;
    private readonly List<Shot> _shots = new();
    // Units each player has lost in fights, and enemy units each player's units have killed.
    private readonly List<int> _losses = new();
    private readonly List<int> _kills = new();

    public CombatSystem(Simulation sim)
    {
        _sim = sim;
        _cellsX = (sim.World.SizeX + CellSize - 1) / CellSize;
        _cellsZ = (sim.World.SizeZ + CellSize - 1) / CellSize;
        sim.World.BlockChanged += OnBlockChanged;
    }

    private UnitStore U => _sim.Units;
    private VoxelWorld World => _sim.World;
    private BuildingStore Buildings => _sim.Buildings;
    private CombatRules Rules => _sim.Content.Combat;
    private UnitDef Def(int i) => _sim.Content.Units[U.Def[i]];

    /// <summary>The hits of the last <see cref="ShotTicks"/> ticks, oldest first, for drawing.</summary>
    public IReadOnlyList<Shot> Shots => _shots;

    /// <summary>Units the player has lost in fights.</summary>
    public int Losses(int player) => player >= 0 && player < _losses.Count ? _losses[player] : 0;

    /// <summary>Enemy units the player's units have killed (a death counts for the nearest unit that hit it).</summary>
    public int Kills(int player) => player >= 0 && player < _kills.Count ? _kills[player] : 0;

    /// <summary>
    /// The player's combat units, in id order: everything they own that is not a worker or a caravan. A beaten
    /// monster that is not yet ridden or recruited is left out. The HUD's Army button (and F2) selects these.
    /// </summary>
    public List<int> ArmyUnits(int player)
    {
        var u = U;
        var ids = new List<int>();
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Owner[i] != player || u.Subdued[i]) continue;
            var role = _sim.Content.Units[u.Def[i]].Role;
            if (role is UnitRole.Worker or UnitRole.Trader or UnitRole.Hauler or UnitRole.Boat) continue;
            ids.Add(u.Id[i]);
        }
        ids.Sort();
        return ids;
    }

    /// <summary>Hash of the fighting's own state (units and buildings hash theirs).</summary>
    public ulong ComputeHash()
    {
        ulong h = 14695981039346656037UL;
        void Mix(long v) { h = (h ^ (ulong)v) * 1099511628211UL; }
        Mix(_losses.Count);
        foreach (int n in _losses) Mix(n);
        Mix(_kills.Count);
        foreach (int n in _kills) Mix(n);
        HashStampedes(Mix);
        Mix(_lurkers.Count);
        foreach (int id in _lurkers) Mix(id);
        return h;
    }

    private static void Count(List<int> counts, int player)
    {
        while (counts.Count <= player) counts.Add(0);
        counts[player]++;
    }

    // --- The tick ----------------------------------------------------------------------------------

    public void Step()
    {
        var u = U;
        long tick = _sim.Tick;
        if (_pending.Length < u.Capacity)
        {
            _pending = new int[u.Capacity];
            _hitBy = new int[u.Capacity];
            _hitFrom = new Fix64[u.Capacity];
            _cheer = new int[u.Capacity];
            _steady = new int[u.Capacity];
            _inspire = new int[u.Capacity];
            _volley = new int[u.Capacity];
            _bled = new int[u.Capacity];
        }
        Array.Clear(_pending, 0, u.Count);
        Array.Clear(_bled, 0, u.Count);
        Array.Clear(_hitBy, 0, u.Count);
        foreach (var (id, damage) in _crushed)
        {
            int c = u.IndexOf(id);
            if (c >= 0) _pending[c] += damage;
        }
        _crushed.Clear();
        foreach (var (id, by, damage) in _hunted)
        {
            int c = u.IndexOf(id);
            if (c < 0) continue;
            _pending[c] += damage;
            _hitBy[c] = by;
        }
        _hunted.Clear();
        int old = 0;
        while (old < _shots.Count && _shots[old].Tick <= tick - ShotTicks) old++;
        _shots.RemoveRange(0, old);

        StepBleeding();
        StepPoison();
        StepBurst();
        StepWalls();
        StepMolts();
        StepPens();
        StepWebs();
        StepLurkers();
        BuildGrid();
        Rally();
        StepDrums();
        StepStampede();
        if (tick % Tps == 0) RecoverMorale();
        for (int i = 0; i < u.Count; i++) StepUnit(i);
        for (int i = 0; i < u.Count; i++)
            if (u.Job[i] == UnitJob.Tunnel && u.Rout[i] == 0) StepTunnel(i);
        StepMurderHoles();
        if (Resolve()) BuildGrid();
        _smitten.Clear();
        StepHeals();
        StepRecovery();
        DrainBlocks();
        for (int i = 0; i < u.Count; i++)
            if (u.Rout[i] == 0 && u.Molting[i] == 0 && u.Morale[i] <= BreakMorale && Def(i).Role != UnitRole.Monster && !Def(i).Hero && !Def(i).Machine) StartRout(i);
    }

    /// <summary>
    /// Unit i is hit by falling ground: the damage lands with this tick's blows, by no one (so no one gets
    /// the kill). Called before <see cref="Step"/>.
    /// </summary>
    internal void Crush(int i, int damage)
    {
        if (damage > 0) _crushed.Add((U.Id[i], damage));
    }

    // Wild game a hunter shot this tick (see Shoot): the game, the hunter and the damage.
    private readonly List<(int Id, int By, int Damage)> _hunted = new();

    /// <summary>
    /// Unit i, wild game, is shot dead by hunter <paramref name="by"/> (see <see cref="Economy.EconomySystem"/>'s
    /// hunting): the arrow flies now, and the kill lands with this tick's blows, the hunter's. Called before <see cref="Step"/>.
    /// </summary>
    internal void Shoot(int i, int by)
    {
        var u = U;
        _hunted.Add((u.Id[i], u.Id[by], Math.Max(1, u.Hp[i])));
        _shots.Add(new Shot(_sim.Tick, u.Id[by], u.X[by], u.Y[by], u.Z[by], u.X[i], u.Y[i], u.Z[i], true, false));
    }

    // Units struck dead by test mode this tick: even the invulnerable die.
    private readonly List<int> _smitten = new();

    /// <summary>Test mode: unit i dies this tick, as if crushed (see <see cref="Sandbox.SandboxSystem"/>). Called before <see cref="Step"/>.</summary>
    internal void Smite(int i)
    {
        _crushed.Add((U.Id[i], Math.Max(1, U.Hp[i])));
        _smitten.Add(U.Id[i]);
    }

    private void StepUnit(int i)
    {
        var u = U;
        if (u.Subdued[i]) return; // beaten: it waits for its owner's choice
        if (u.Penned[i] != 0 || u.RunStage[i] != 0) return; // kept in a pen, or running in a stampede
        if (u.Molting[i] > 0) return; // shedding its shell: it neither moves nor fights (see CombatSystem.Molt.cs)
        if (u.Rout[i] > 0)
        {
            // Running: it keeps going wherever it was running to, and rallies when the time is up.
            if (--u.Rout[i] == 0) u.Morale[i] = Math.Max(u.Morale[i], RallyMorale);
            return;
        }
        if (u.Cooldown[i] > 0) u.Cooldown[i]--;
        var def = Def(i);
        if (!def.CanAttack) return;
        if (u.Job[i] != UnitJob.None)
        {
            // Units at work do not fight, but a soldier on a raid answers an attack: it drops the raid.
            if (u.HitBy[i] == 0 || u.Job[i] is not (UnitJob.Capture or UnitJob.Burn) || def.Role == UnitRole.Worker) return;
            u.ClearJob(i);
            Halt(i);
        }
        if (u.Order[i] == UnitOrder.Move && !u.FightsOnTheWay(i))
        {
            // Walking on a plain move: it does not stop to fight.
            u.Target[i] = u.TargetBuilding[i] = 0;
            return;
        }
        if (!FindTarget(i, def, out int t, out var b))
        {
            if (u.Order[i] == UnitOrder.Chase) Halt(i);
            if (u.Combat[i] == CombatOrder.Attack) u.Combat[i] = CombatOrder.Auto;
            else if (u.Combat[i] == CombatOrder.AttackMove && u.Order[i] == UnitOrder.Idle) Resume(i);
            else if (u.Combat[i] == CombatOrder.Patrol && u.Order[i] == UnitOrder.Idle && Turn(i)) Resume(i);
            else if (u.Combat[i] == CombatOrder.Guard) Follow(i);
            return;
        }
        if (InReach(i, def, t, b))
        {
            if (u.Order[i] != UnitOrder.Idle) Halt(i);
            u.ChaseRetry[i] = 0;
            if (u.Cooldown[i] == 0 && Hit(i, def, t, b)) u.Cooldown[i] = def.AttackTicks;
        }
        else if (u.Combat[i] == CombatOrder.Auto && Holding(i))
        {
            // Holding its ground: it waits for the enemy to come within reach (or a clear line) and never chases.
            if (u.Order[i] == UnitOrder.Chase) Halt(i);
        }
        else
        {
            Chase(i, t, b);
        }
    }

    // --- Targets -----------------------------------------------------------------------------------

    /// <summary>
    /// The unit's target if it still has a good one, otherwise a new one: the enemy that last hit it, or
    /// (a few times a second) the nearest enemy in sight. False if it has nothing to fight.
    /// </summary>
    private bool FindTarget(int i, UnitDef def, out int t, out Building? b)
    {
        var u = U;
        var order = u.Combat[i];
        t = -1;
        b = null;
        // A city-state's guard fights only soldiers in its town (see CityStateSystem.Repels).
        bool guard = u.Town[i] != 0;
        if (u.TargetBuilding[i] != 0)
        {
            b = Buildings.Get(u.TargetBuilding[i]);
            if (b != null && !b.Planned && (Hostile(u.Owner[i], b.Owner) || b.Def.IsBridge && b.Owner == u.Owner[i]) && !guard) return true;
            // The wall it was breaking through is down: back to the building it was sent at (see Breach).
            b = u.BreachFor[i] != 0 ? Buildings.Get(u.BreachFor[i]) : null;
            u.TargetBuilding[i] = u.BreachFor[i] = 0;
            if (b != null && !b.Planned && Hostile(u.Owner[i], b.Owner) && !guard)
            {
                u.TargetBuilding[i] = b.Id;
                u.ChaseRetry[i] = 0;
                return true;
            }
            b = null;
        }
        // A ram goes only at buildings, and only when sent at one (or at the wall in its way, see Breach).
        if (def.Ram)
        {
            u.Target[i] = 0;
            return false;
        }
        // Holding its ground, it only fights what it can reach from where it stands.
        bool hold = order == CombatOrder.Auto && Holding(i);
        if (u.Target[i] != 0)
        {
            t = u.IndexOf(u.Target[i]);
            if (t >= 0 && Hostile(u.Owner[i], u.Owner[t]) && (!guard || _sim.Cities.Repels(i, t)) && !TooClose(i, def, t)
                && (order == CombatOrder.Attack || (!u.Subdued[t] && (Within(i, t, hold ? Reach(i, def, t) : Sight(i, def, t) * Leash)
                    || order == CombatOrder.Guard && !hold && TownGuard(i) && _sim.Vision.SeesUnit(u.Owner[i], t))))
                && (order != CombatOrder.Guard || NearCharge(i, t, LeashOf(i))))
                return true;
            t = -1;
            u.Target[i] = 0;
            u.LosBlocked[i] = false;
        }
        if (order == CombatOrder.Attack) return false;
        if (u.CombatWait[i] > 0)
        {
            u.CombatWait[i]--;
            return false;
        }
        int by = u.HitBy[i] != 0 ? u.IndexOf(u.HitBy[i]) : -1;
        // Workers do not go looking for fights.
        if (def.Role == UnitRole.Worker && order == CombatOrder.Auto) return false;
        // A guard first goes for whoever hits its charge, and fights only near its charge.
        bool guarding = order == CombatOrder.Guard;
        if (guarding && by >= 0 && !NearCharge(i, by, LeashOf(i))) by = -1;
        if (by >= 0 && Hostile(u.Owner[i], u.Owner[by]) && !u.Subdued[by] && !TooClose(i, def, by) && (!guard || _sim.Cities.Repels(i, by)) && Within(i, by, hold ? Reach(i, def, by) : Sight(i, def, by) * AnswerRange)) t = by;
        else if (guarding && ChargeAttacker(i) is var at && at >= 0) t = at;
        else if ((_sim.Tick + u.Id[i]) % AcquireInterval == 0)
        {
            t = Nearest(i, def);
            if (guarding && t >= 0 && !NearCharge(i, t, RangeOf(i))) t = -1;
            // The governor sends its town guard after enemies anywhere in the town, out of their own sight too.
            if (t < 0 && guarding && !hold && TownHallGuarded(i) is { } hall) t = Intruder(i, hall);
        }
        if (guarding && t >= 0 && t != by && !NearCharge(i, t, RangeOf(i)) && t != ChargeAttacker(i)) t = -1;
        if (t >= 0 && hold && !Within(i, t, Reach(i, def, t))) t = -1;
        if (t < 0) return false;
        u.Target[i] = u.Id[t];
        u.ChaseRetry[i] = 0;
        u.LosBlocked[i] = false;
        return true;
    }

    /// <summary>
    /// The nearest enemy unit the unit can see (ties to the lowest id), not a beaten monster, or -1. Nobody
    /// picks a neutral city-state's guard on their own (they answer one that hits them), and a guard picks
    /// only soldiers in its town.
    /// </summary>
    private static readonly Fix64 Spot2 = Fix64.FromInt(VisionSystem.SpotSubmerged * VisionSystem.SpotSubmerged);

    private int Nearest(int i, UnitDef def)
    {
        var u = U;
        Fix64 x = u.X[i], z = u.Z[i];
        bool guard = u.Town[i] != 0;
        bool monster = !guard && u.Owner[i] == Simulation.WildPlayer;
        // A Reptilian lying in the water lies in wait: it springs only at enemies that come close (CombatSystem.Swamp.cs).
        bool lying = Lurking(i);
        // In the dark it sees less far, but a target in the light as far as by day (see Conditions).
        var cond = _sim.Conditions;
        int ox = x.FloorToInt(), oz = z.FloorToInt();
        int darkPct = cond.SightPercent(ox, oz, false);
        int litPct = cond.Lights.Any ? cond.SightPercent(ox, oz, true) : darkPct;
        Fix64 darkSight = def.Sight * darkPct / 100, litSight = def.Sight * litPct / 100;
        Fix64 widest = def.IsRanged ? litSight * HighGroundBonus : litSight;
        int best = -1;
        Fix64 bestD = Fix64.MaxValue, closest = def.MinRange * def.MinRange;
        Cells(x, z, widest, out int x0, out int z0, out int x1, out int z1);
        for (int cz = z0; cz <= z1; cz++)
        for (int cx = x0; cx <= x1; cx++)
        {
            int c = (cz * _cellsX + cx) * _players;
            for (int p = 0; p < _players; p++)
            {
                if (!Hostile(u.Owner[i], p)) continue;
                // A trade partner's caravans are let be (see TradeDealSystem); an explicit order still attacks one.
                bool spareCaravans = _sim.Deals.Partners(u.Owner[i], p);
                for (int j = _head[c + p]; j >= 0; j = _next[j])
                {
                    if (u.Subdued[j]) continue;
                    if (spareCaravans && Def(j).IsTrader) continue;
                    // Nobody goes after wild game on their own: hunters shoot it, and an explicit order still attacks it.
                    if (Def(j).IsGame) continue;
                    if (guard ? !_sim.Cities.Repels(i, j) : u.Town[j] != 0 && u.Owner[j] == Simulation.WildPlayer) continue;
                    if (monster && _sim.Monsters.Spares(i, j)) continue;
                    int dy = u.Y[i] - u.Y[j];
                    if (!def.IsRanged && Math.Abs(dy) > MeleeSearchHeight) continue;
                    Fix64 s = litPct > darkPct && cond.Lights.Lit(u.X[j].FloorToInt(), u.Z[j].FloorToInt()) ? litSight : darkSight;
                    if (def.IsRanged && dy >= HighGround) s *= HighGroundBonus;
                    Fix64 dx = u.X[j] - x, dz = u.Z[j] - z;
                    if (Fix64.Abs(dx) > s || Fix64.Abs(dz) > s) continue;
                    Fix64 d = dx * dx + dz * dz;
                    if (d > s * s || d < closest) continue;
                    // A monster lying in the water goes unnoticed until it is close (see MonsterSystem.Submerged).
                    if (d > Spot2 && (lying || _sim.Monsters.Submerged(j) || Lurking(j))) continue;
                    if (d < bestD || (d == bestD && u.Id[j] < u.Id[best]))
                    {
                        best = j;
                        bestD = d;
                    }
                }
            }
        }
        return best;
    }

    /// <summary>How far unit i hits target t from: its range, a quarter more for a ranged unit on the high ground.</summary>
    private Fix64 Reach(int i, UnitDef def, int t) =>
        def.IsRanged && U.Y[i] - U.Y[t] >= HighGround ? def.Range * HighGroundBonus : def.Range;

    /// <summary>
    /// True if unit t stands nearer unit i than i's <see cref="UnitDef.MinRange"/> (a catapult's dead ground), so i
    /// cannot hit it: it neither picks it nor keeps it as a target, even on an order.
    /// </summary>
    public bool TooClose(int i, UnitDef def, int t)
    {
        if (def.MinRange <= Fix64.Zero) return false;
        Fix64 dx = U.X[t] - U.X[i], dz = U.Z[t] - U.Z[i];
        if (Fix64.Abs(dx) >= def.MinRange || Fix64.Abs(dz) >= def.MinRange) return false;
        return dx * dx + dz * dz < def.MinRange * def.MinRange;
    }

    /// <summary>
    /// True if the unit holds its ground when it has no order: in a shield wall, on the hold stance, on the retreat stance and
    /// hurt, or standing on top of a wall or gate (it does not jump down after an enemy).
    /// </summary>
    public bool Holding(int i) => U.ShieldWall[i] || U.Stance[i] switch
    {
        UnitStance.Hold => true,
        UnitStance.Retreat => Hurt(i) || _sim.Economy.WallUnder(i) != null,
        _ => _sim.Economy.WallUnder(i) != null,
    };

    /// <summary>True if the unit is down to <see cref="RetreatPercent"/> of its hit points or less.</summary>
    public bool Hurt(int i) => U.Hp[i] * 100 <= _sim.MaxHpOf(i) * RetreatPercent;

    /// <summary>
    /// How far unit i sees target t: its sight, a quarter more for a ranged unit on the high ground, less in
    /// the dark, bad weather or mist unless the target stands in the light (see <see cref="Conditions"/>).
    /// </summary>
    private Fix64 Sight(int i, UnitDef def, int t)
    {
        var c = _sim.Conditions;
        bool lit = c.Lights.Lit(U.X[t].FloorToInt(), U.Z[t].FloorToInt());
        var s = def.Sight * c.SightPercent(U.X[i].FloorToInt(), U.Z[i].FloorToInt(), lit) / 100;
        return def.IsRanged && U.Y[i] - U.Y[t] >= HighGround ? s * HighGroundBonus : s;
    }

    /// <summary>
    /// How much better unit i aims for shooting from a tower (see <see cref="BuildingDef.Aim"/>): 0 anywhere
    /// else, and for a unit that does not shoot.
    /// </summary>
    public int Aim(int i) => Def(i).IsRanged && _sim.Economy.WallUnder(i) is { Active: true } b ? b.Def.Aim : 0;

    /// <summary>How far (blocks) from the edge of its column a unit on a wall leans out to shoot (see <see cref="ClearShot"/>).</summary>
    public static readonly Fix64 LeanEdge = Fix64.FromRatio(1, 16);

    /// <summary>
    /// True if unit i has a clear line to unit t (see <see cref="ClearLine"/>). A unit on top of a wall, gate or
    /// tower leans out over its edge toward the target, so it shoots what stands at the foot of the wall under it.
    /// </summary>
    public bool ClearShot(int i, int t)
    {
        var u = U;
        Fix64 x = u.X[i], z = u.Z[i];
        if (_sim.Economy.WallUnder(i) != null)
        {
            int cx = x.FloorToInt(), cz = z.FloorToInt();
            x = Fix64.Clamp(u.X[t], Fix64.FromInt(cx) + LeanEdge, Fix64.FromInt(cx + 1) - LeanEdge);
            z = Fix64.Clamp(u.Z[t], Fix64.FromInt(cz) + LeanEdge, Fix64.FromInt(cz + 1) - LeanEdge);
        }
        return ClearLine(x, u.Y[i], z, u.X[t], u.Y[t], u.Z[t]);
    }

    private bool Within(int i, int t, Fix64 radius)
    {
        var u = U;
        Fix64 dx = u.X[t] - u.X[i], dz = u.Z[t] - u.Z[i];
        if (Fix64.Abs(dx) > radius || Fix64.Abs(dz) > radius) return false;
        return dx * dx + dz * dz <= radius * radius;
    }

    /// <summary>True if unit i can hit its target from where it stands.</summary>
    private bool InReach(int i, UnitDef def, int t, Building? b)
    {
        var u = U;
        if (b != null) return InReach(i, def, b);
        int dy = u.Y[i] - u.Y[t];
        if (!def.IsRanged && Math.Abs(dy) > MeleeHeight) return false;
        Fix64 reach = def.IsRanged && dy >= HighGround ? def.Range * HighGroundBonus : def.Range;
        if (!Within(i, t, reach) || TooClose(i, def, t)) return false;
        // A lobbed shot goes over walls and hills: it needs no clear line.
        if (!def.IsRanged || def.Lobs) return true;
        // A ranged unit needs a clear line. It looks when it is about to shoot, and every few ticks while it
        // has none (walking closer meanwhile).
        if (u.LosBlocked[i])
        {
            if ((_sim.Tick + u.Id[i]) % LineCheckInterval != 0) return false;
        }
        else if (u.Cooldown[i] > 0)
        {
            return true;
        }
        u.LosBlocked[i] = !ClearShot(i, t);
        return !u.LosBlocked[i];
    }

    /// <summary>
    /// True if unit i can hit the building: within reach of its footprint, and if melee at about its level, or
    /// beside it higher up (on the rubble heaped against a tall wall), no higher than its top.
    /// </summary>
    private bool InReach(int i, UnitDef def, Building b)
    {
        var u = U;
        int dy = u.Y[i] - b.Y;
        if (!def.IsRanged && (dy < -1 || dy > Math.Max(3, b.Shape.Height))) return false;
        Fix64 reach = def.IsRanged && dy >= HighGround ? def.Range * HighGroundBonus : def.Range;
        Fix64 dx = Gap(u.X[i], b.X, b.MaxX + 1), dz = Gap(u.Z[i], b.Z, b.MaxZ + 1);
        if (dx > reach || dz > reach) return false;
        return dx * dx + dz * dz <= reach * reach;
    }

    /// <summary>Distance (columns) from v to the span [lo, hi] (0 inside it).</summary>
    private static int Gap(int v, int lo, int hi) => v < lo ? lo - v : v > hi ? v - hi : 0;

    /// <summary>Distance from v to the span [lo, hi] (0 inside it).</summary>
    private static Fix64 Gap(Fix64 v, int lo, int hi) =>
        v < Fix64.FromInt(lo) ? Fix64.FromInt(lo) - v : v > Fix64.FromInt(hi) ? v - Fix64.FromInt(hi) : Fix64.Zero;

    /// <summary>
    /// True if nothing solid (leaves aside) and no orchard tree lies between a unit's eyes at (x0, y0, z0) and the middle of a unit
    /// standing at (x1, y1, z1). Walks the blocks along the line (Amanatides and Woo) in fixed point.
    /// </summary>
    public bool ClearLine(Fix64 x0, int y0, Fix64 z0, Fix64 x1, int y1, Fix64 z1)
    {
        Fix64 ay = Fix64.FromInt(y0) + Fix64.FromRatio(3, 2), by = Fix64.FromInt(y1) + Fix64.One;
        int cx = x0.FloorToInt(), cy = ay.FloorToInt(), cz = z0.FloorToInt();
        int ex = x1.FloorToInt(), ey = by.FloorToInt(), ez = z1.FloorToInt();
        Axis(x0, x1, cx, out int sx, out Fix64 tx, out Fix64 dtx);
        Axis(ay, by, cy, out int sy, out Fix64 ty, out Fix64 dty);
        Axis(z0, z1, cz, out int sz, out Fix64 tz, out Fix64 dtz);
        int steps = Math.Abs(ex - cx) + Math.Abs(ey - cy) + Math.Abs(ez - cz);
        for (int n = 0; n < steps; n++)
        {
            if (tx <= ty && tx <= tz) { cx += sx; tx += dtx; }
            else if (tz <= ty) { cz += sz; tz += dtz; }
            else { cy += sy; ty += dty; }
            if (cx == ex && cy == ey && cz == ez) return true;
            var block = World.Get(cx, cy, cz);
            if (BlockInfo.IsSolid(block) && !BlockInfo.IsLeaves(block) || block == BlockType.Orchard) return false;
        }
        return true;
    }

    /// <summary>
    /// One axis of a line walk from a to b starting in cell c: the step direction, the fraction of the line at
    /// which it first crosses into the next cell, and the fraction per cell. An axis it barely moves along is
    /// never stepped.
    /// </summary>
    private static void Axis(Fix64 a, Fix64 b, int c, out int step, out Fix64 next, out Fix64 delta)
    {
        Fix64 d = b - a;
        if (Fix64.Abs(d) < Fix64.FromRatio(1, 1024))
        {
            step = 0;
            next = delta = Fix64.MaxValue;
            return;
        }
        Fix64 len = Fix64.Abs(d);
        step = d > Fix64.Zero ? 1 : -1;
        Fix64 toEdge = step > 0 ? Fix64.FromInt(c + 1) - a : a - Fix64.FromInt(c);
        next = toEdge / len;
        delta = Fix64.One / len;
    }

    // --- Chasing -----------------------------------------------------------------------------------

    /// <summary>
    /// Sends unit i after its target, or keeps it going: a unit target's column, re-aimed a few times a
    /// second when it moves (less often the further off it is), or the spot beside a building nearest to i.
    /// Gives up after <see cref="MaxChaseRetries"/> chases in a row that end out of reach.
    /// </summary>
    private void Chase(int i, int t, Building? b)
    {
        var u = U;
        int x = u.X[i].FloorToInt(), z = u.Z[i].FloorToInt();
        if (u.Order[i] == UnitOrder.Chase)
        {
            if (b != null)
            {
                // There, but not in reach: count it as a chase that fell short, and look for another spot.
                if (x != u.OrderX[i] || z != u.OrderZ[i]) return;
                if (u.ChaseRetry[i]++ >= MaxChaseRetries)
                {
                    if (!Breach(i, b)) GiveUp(i);
                    return;
                }
            }
            else
            {
                int tx = u.X[t].FloorToInt(), tz = u.Z[t].FloorToInt();
                int moved = Math.Max(Math.Abs(tx - u.OrderX[i]), Math.Abs(tz - u.OrderZ[i]));
                int off = Math.Max(Math.Abs(tx - x), Math.Abs(tz - z));
                if (moved == 0 || moved * 4 < off || (_sim.Tick + u.Id[i]) % ReaimInterval != 0) return;
            }
        }
        else if (u.Order[i] == UnitOrder.Idle && u.ChaseRetry[i]++ >= MaxChaseRetries)
        {
            if (b == null ? !BringDown(i, t) : !Breach(i, b)) GiveUp(i);
            return;
        }
        int gx, gz, gy;
        if (Def(i).IsBoat)
        {
            // A ship sails within range of it by water (see BoatSystem.Chase).
            if (b != null) _sim.Boats.Chase(i, (b.X + b.MaxX) / 2, (b.Z + b.MaxZ) / 2);
            else _sim.Boats.Chase(i, u.X[t].FloorToInt(), u.Z[t].FloorToInt());
            return;
        }
        if (b != null)
        {
            // Beside it, as high up as a blow still reaches it from: the rubble heaped against a tall wall counts.
            if (!_sim.Economy.SpotBeside(b, x, z, out gx, out gz, out gy, Math.Max(3, b.Shape.Height)))
            {
                GiveUp(i);
                return;
            }
        }
        else
        {
            (gx, gz, gy) = (u.X[t].FloorToInt(), u.Z[t].FloorToInt(), u.Y[t]);
        }
        u.Order[i] = UnitOrder.Chase;
        u.OrderX[i] = gx;
        u.OrderZ[i] = gz;
        u.Blocked[i] = 0;
        u.Field[i] = _sim.Paths.Request(gx, gz, gy, u.Owner[i]).Id;
    }

    /// <summary>How far (columns) from a unit that cannot get to a building it looks for a wall to break through.</summary>
    public const int BreachRange = 16;

    /// <summary>
    /// Unit i cannot get to building b (it is walled in): it goes for a wall or gate of b's owner instead, to
    /// break through, and when that is down goes on with b (through the next wall in the way, where a wall is
    /// thick). The wall is one it can get at from its side (see <see cref="Exposed"/>) with the shortest way by
    /// it, from the unit to it and on to b (ties to the one nearer b, then the one straighter ahead, then the
    /// lowest id), so a thick wall is broken straight through; with none it can get at, the nearest. A wall it
    /// went for like this and could not get to is given up for another. False if there is no wall within
    /// <see cref="BreachRange"/> of the unit, or b is a wall it was sent at itself.
    /// </summary>
    private bool Breach(int i, Building b)
    {
        var u = U;
        Building? not = null;
        if (b.Def.IsWall)
        {
            // A wall in the way of the building it is after: another one, then.
            if (u.BreachFor[i] == 0 || Buildings.Get(u.BreachFor[i]) is not { Def.IsWall: false } goal || !Hostile(u.Owner[i], goal.Owner)) return false;
            (not, b) = (b, goal);
        }
        int x = u.X[i].FloorToInt(), z = u.Z[i].FloorToInt();
        Building? best = null, nearest = null;
        int bestWay = int.MaxValue, bestOn = int.MaxValue, bestAhead = int.MaxValue, nearestD = BreachRange + 1;
        foreach (var w in Buildings.All)
        {
            if (w == not || w.Owner != b.Owner || !w.Def.IsWall || w.Planned) continue;
            int d = w.ColumnDistance(x, z);
            if (d > BreachRange) continue;
            if (d < nearestD) (nearest, nearestD) = (w, d);
            int on = Math.Max(Gap(w.X, b.X, b.MaxX), Gap(w.Z, b.Z, b.MaxZ));
            int ahead = (w.X - x) * (w.X - x) + (w.Z - z) * (w.Z - z);
            if (d + on != bestWay ? d + on > bestWay : on != bestOn ? on > bestOn : ahead >= bestAhead) continue;
            if (!Exposed(w, x, z, d)) continue;
            (best, bestWay, bestOn, bestAhead) = (w, d + on, on, ahead);
        }
        best ??= nearest;
        if (best == null) return false;
        u.TargetBuilding[i] = best.Id;
        u.BreachFor[i] = b.Id;
        u.ChaseRetry[i] = 0;
        if (u.Order[i] == UnitOrder.Chase) Halt(i);
        return true;
    }

    /// <summary>
    /// True if wall w, d columns from column (x, z), can be got at from that side: beside it, nearer to (x, z)
    /// than it is, there is a column with no building on it to stand on (the ground, or the rubble of a wall
    /// that stood in front of it), no higher than the wall's top.
    /// </summary>
    private bool Exposed(Building w, int x, int z, int d)
    {
        foreach (var (dx, dz) in WalkGraph.Dirs)
        {
            int cx = w.X + dx, cz = w.Z + dz;
            if (Math.Max(Math.Abs(cx - x), Math.Abs(cz - z)) >= d || !World.InBounds(cx, 0, cz)) continue;
            if (Walk.NearestStandLevel(World, cx, cz, w.Y, Math.Max(3, w.Shape.Height)) >= 0 && Buildings.AtColumn(cx, cz) == null) return true;
        }
        return false;
    }

    /// <summary>
    /// Unit i cannot get to unit t, which stands on top of a wall or gate that is not i's own: it goes for
    /// that wall instead, to bring it down under it. False if t stands on no such wall.
    /// </summary>
    private bool BringDown(int i, int t)
    {
        var u = U;
        if (_sim.Economy.WallUnder(t) is not { } wall || !Hostile(u.Owner[i], wall.Owner)) return false;
        u.Target[i] = 0;
        u.TargetBuilding[i] = wall.Id;
        u.ChaseRetry[i] = 0;
        u.LosBlocked[i] = false;
        return true;
    }

    /// <summary>Lets go of the target and waits a while before looking for another.</summary>
    private void GiveUp(int i)
    {
        var u = U;
        u.Target[i] = u.TargetBuilding[i] = u.BreachFor[i] = 0;
        u.ChaseRetry[i] = 0;
        u.LosBlocked[i] = false;
        if (u.Combat[i] == CombatOrder.Attack) u.Combat[i] = CombatOrder.Auto;
        u.CombatWait[i] = GiveUpTicks;
        if (u.Order[i] == UnitOrder.Chase) Halt(i);
    }

    /// <summary>A patrol order to a goal nearer than this (columns, either way) is only an attack-move there.</summary>
    public const int PatrolShortest = 3;

    /// <summary>A patrol ends after this many walks in a row end short of where they were going (there is no way there).</summary>
    public const int PatrolMisses = 2;

    /// <summary>
    /// A patrolling unit with nothing to fight and nowhere to walk: if it only stopped to fight, it carries on
    /// to the same end. If its walk is over, it turns round and heads for the other end. A walk that ended
    /// short of its end (more than <see cref="PatrolSlack"/> columns past the arrival radius) counts as a
    /// miss, and <see cref="PatrolMisses"/> in a row end the patrol: there is no way between the ends.
    /// False if the patrol is over.
    /// </summary>
    private bool Turn(int i)
    {
        var u = U;
        int state = u.PatrolState[i];
        if ((state & 1) == 0) return true;
        Fix64 dx = u.X[i] - (Fix64.FromInt(u.DestX[i]) + Fix64.Half), dz = u.Z[i] - (Fix64.FromInt(u.DestZ[i]) + Fix64.Half);
        Fix64 near = u.ArriveRadius[i] + Fix64.FromInt(PatrolSlack);
        int misses = dx * dx + dz * dz <= near * near ? 0 : (state >> 1) + 1;
        if (misses >= PatrolMisses)
        {
            u.ClearCombat(i);
            return false;
        }
        (u.DestX[i], u.PatrolX[i]) = (u.PatrolX[i], u.DestX[i]);
        (u.DestZ[i], u.PatrolZ[i]) = (u.PatrolZ[i], u.DestZ[i]);
        (u.DestY[i], u.PatrolY[i]) = (u.PatrolY[i], u.DestY[i]);
        u.PatrolState[i] = misses << 1;
        return true;
    }

    /// <summary>How far (columns) past its arrival radius a patrolling unit may stop and still count as there.</summary>
    public const int PatrolSlack = 2;

    /// <summary>An attack-move with nothing left to fight here carries on to where it was going.</summary>
    private void Resume(int i)
    {
        var u = U;
        u.Order[i] = UnitOrder.Move;
        u.OrderX[i] = u.DestX[i];
        u.OrderZ[i] = u.DestZ[i];
        u.Blocked[i] = 0;
        u.Field[i] = _sim.Paths.Request(u.DestX[i], u.DestZ[i], u.DestY[i], u.Owner[i]).Id;
    }

    private void Halt(int i)
    {
        var u = U;
        u.Order[i] = UnitOrder.Idle;
        u.Blocked[i] = 0;
        u.Field[i] = 0;
    }

    // --- Hits --------------------------------------------------------------------------------------

    /// <summary>Ranged shots that missed since the match began (see <see cref="Conditions.HitChance"/>).</summary>
    public long Misses { get; private set; }

    /// <summary>
    /// Unit i hits its target. A unit already doomed by this tick's other hits is left for another enemy in
    /// reach; with none, i holds its blow (false). A building takes damage at once (see HitBuilding).
    /// </summary>
    private bool Hit(int i, UnitDef def, int t, Building? b)
    {
        var u = U;
        if (b != null)
        {
            Fought(i);
            int damage = BuildingDamage(def, b.Def, AttackOf(i, def));
            int undermine = UndermineOf(i); // a sapper near a hero that works the breach
            if (undermine > 0) damage = damage * (100 + undermine) / 100;
            AddShot(i, def, Clamp(u.X[i], b.X, b.MaxX + 1), b.Y + 1, Clamp(u.Z[i], b.Z, b.MaxZ + 1));
            _sim.Evolution.Damaged(i, damage);
            HitBuilding(b, damage, i, def.Role == UnitRole.Sapper || def.Ram, def.Blast);
            Thorns(i, def, b);
            if (def.Crater > 0) CraterBeside(i, def, b);
            if (def.Ignites) _sim.Fire.IgniteAround(Clamp(u.X[i], b.X, b.MaxX + 1).FloorToInt(), b.Y + 1, Clamp(u.Z[i], b.Z, b.MaxZ + 1).FloorToInt(), u.Owner[i]);
            u.ChargeLeft[i] = 0; // the charge's first blow is struck
            return true;
        }
        if (_pending[t] >= u.Hp[t])
        {
            int other = Spare(i, def);
            if (other < 0) return false;
            t = other;
            u.Target[i] = u.Id[t];
        }
        Fought(i);
        // A ranged shot can miss in the dark or in bad weather (see Conditions): it lands beside the target.
        if (def.IsRanged && !_sim.Conditions.ShotLands(u.Id[i], u.X[t].FloorToInt(), u.Z[t].FloorToInt(), Aim(i)))
        {
            Fix64 side = (u.Id[i] & 1) == 0 ? Fix64.One : -Fix64.One;
            AddShot(i, def, u.X[t] + side, u.Y[t], u.Z[t] + side / 2);
            Crater(u.X[t] + side, u.Y[t], u.Z[t] + side / 2, def.Crater);
            if (def.Ignites) _sim.Fire.IgniteAround((u.X[t] + side).FloorToInt(), u.Y[t], (u.Z[t] + side / 2).FloorToInt(), u.Owner[i]);
            Misses++;
            u.ChargeLeft[i] = 0;
            return true;
        }
        var (part, dealt, blocked) = Wound(i, def, t, splash: false);
        if (def.Splash > Fix64.Zero)
        {
            Near(u.X[t], u.Y[t], u.Z[t], def.Splash, 2, u.Owner[i], allies: false, _near);
            foreach (int j in _near)
                if (j != t) Wound(i, def, j, splash: true);
        }
        _shots.Add(new Shot(_sim.Tick, u.Id[i], u.X[i], u.Y[i], u.Z[i], u.X[t], u.Y[t], u.Z[t], def.IsRanged, def.Splash > Fix64.Zero,
            part, dealt, blocked));
        Crater(u.X[t], u.Y[t], u.Z[t], def.Crater);
        if (def.Ignites) _sim.Fire.IgniteAround(u.X[t].FloorToInt(), u.Y[t], u.Z[t].FloorToInt(), u.Owner[i]);
        u.ChargeLeft[i] = 0;
        return true;
    }

    /// <summary>Hits stopped by shields since the match began.</summary>
    public long Blocks { get; private set; }

    private static Fix64 Clamp(Fix64 v, int lo, int hi) => Fix64.Clamp(v, Fix64.FromInt(lo), Fix64.FromInt(hi));

    /// <summary>
    /// Unit i's hit on unit t: it lands on a part (see <see cref="Location"/>) unless t's shield stops it (never
    /// a blast's splash), and wounds that part. The part, the damage after armour (0 if stopped) and whether a
    /// shield stopped it. Wounds and bleeding are kept at once; the hit points come off with the tick's other hits.
    /// </summary>
    private (BodyPart Part, int Damage, bool Blocked) Wound(int i, UnitDef def, int t, bool splash)
    {
        var u = U;
        var part = Location(i, t);
        int damage = 0;
        // A lurking monster's first blow coming up out of the water is an ambush (see MonsterSystem.Spring).
        int ambush = splash ? 0 : Math.Max(_sim.Monsters.Spring(i), SpringLurker(i));
        bool blocked = !splash && Shielded(i, t);
        if (blocked) Blocks++;
        else
        {
            damage = HitDamage(i, def, t, part);
            if (ambush > 0) damage = damage * (100 + ambush) / 100;
            // A shield wall takes much less from the front (see CombatSystem.ShieldWall.cs).
            var (through, repelled) = ThroughWall(i, def, t, damage);
            if (through < damage) WallHits++;
            if (repelled) Repel(i, t, damage);
            damage = through;
            // Experience for the hit points it takes off, not for blows on a unit already doomed.
            _sim.Evolution.Damaged(i, Math.Min(damage, Math.Max(0, u.Hp[t] - _pending[t])));
            _pending[t] += damage;
            int w = t * Body.Parts + (int)part;
            u.Wounds[w] = (byte)Math.Min(255, u.Wounds[w] + damage);
            if (Body.Bleeds(def.Attack) && Def(t).Bleeds)
            {
                Cut(t, (int)part, damage);
                u.Cutter[t] = u.Id[i];
            }
            if (!splash)
            {
                Ensnare(i, def, t); // a relic of spider silk (CombatSystem.Webs.cs)
                Envenom(i, def, t); // a giant spider's bite
                Frighten(i, def, t); // a giant hornet's sting (CombatSystem.Fear.cs)
            }
        }
        // The unit answers the nearest of the tick's attackers (ties to the lowest id), whoever struck first.
        Fix64 dx = u.X[i] - u.X[t], dz = u.Z[i] - u.Z[t];
        Fix64 d = dx * dx + dz * dz;
        if (_hitBy[t] == 0 || d < _hitFrom[t] || (d == _hitFrom[t] && u.Id[i] < _hitBy[t]))
        {
            _hitBy[t] = u.Id[i];
            _hitFrom[t] = d;
        }
        return (part, damage, blocked);
    }

    /// <summary>
    /// The damage of unit i's hit on unit t before where it lands and armour: its attack times the role
    /// table's multiplier and t's weakness to i's role, less t's cover against a ranged piercing shot.
    /// </summary>
    public int Damage(int i, UnitDef def, int t)
    {
        var target = Def(t);
        Fix64 m = Rules.Multiplier(def.Role, target.Role);
        int weakness = target.WeaknessTo(def.Role);
        if (weakness > 0) m = m * (100 + weakness) / 100;
        // Under a roof (the ram's hides), arrows and bolts mostly glance off.
        if (target.Cover > 0 && def.IsRanged && def.Attack == AttackKind.Pierce) m = m * (100 - target.Cover) / 100;
        int grudge = GrudgeAgainst(i, t); // the Dwarves' Book of Grudges (CombatSystem.Grudges.cs)
        if (grudge > 0) m = m * (100 + grudge) / 100;
        if (Chilled(i)) m = m * ColdPercent / 100; // cold-blooded in the cold (CombatSystem.Swamp.cs)
        return Math.Max(1, (Fix64.FromInt(AttackOf(i, def)) * m).RoundToInt());
    }

    /// <summary>
    /// Unit i's attack: its type's damage and its relic's if it bears one, and for a ranged unit near a hero
    /// that calls the volleys this tick, that many percent more (see <see cref="Rally"/>); and for a unit that has
    /// just burst out of the ground (see <see cref="StepBurst"/>), its <see cref="UnitDef.Burst"/> percent more; and for
    /// its first blow in a charge a war drummer beat (see <see cref="Charged"/>), that charge's blow percent more.
    /// </summary>
    public int AttackOf(int i, UnitDef def)
    {
        int attack = def.Damage + (_sim.RelicOf(i)?.Damage ?? 0);
        if (_anyRally && i < _volley.Length && _volley[i] > 0) attack = attack * (100 + _volley[i]) / 100;
        if (U.ChargeLeft[i] > 0) attack = attack * (100 + U.ChargeBlow[i]) / 100;
        return U.BurstLeft[i] > 0 ? attack * (100 + def.Burst) / 100 : attack;
    }

    /// <summary>How long (seconds) a unit's blows deal more (<see cref="UnitDef.Burst"/>) after it comes up out of the ground.</summary>
    public const int BurstSeconds = 5;
    /// <summary>Every how many ticks a unit that bursts out of the ground is looked at to see whether it is underground.</summary>
    public const int BurstLook = 4;

    /// <summary>
    /// Units that burst out of the ground (<see cref="UnitDef.Burst"/>, the Deepfolk's): every <see cref="BurstLook"/>
    /// ticks each is looked at, and one with natural ground over its head (<see cref="VisionSystem.Covered"/>: in a
    /// tunnel or a cave) is burrowed; one that was burrowed and is not any more has come up, and its blows deal more
    /// for <see cref="BurstSeconds"/>. Underground, the time left is lost.
    /// </summary>
    private void StepBurst()
    {
        var u = U;
        bool look = _sim.Tick % BurstLook == 0;
        for (int i = 0; i < u.Count; i++)
        {
            if (Def(i).Burst == 0) continue;
            if (u.BurstLeft[i] > 0) u.BurstLeft[i]--;
            if (!look) continue;
            bool under = u.Aboard[i] == 0 && VisionSystem.Covered(World, u.X[i].FloorToInt(), u.Y[i], u.Z[i].FloorToInt());
            if (under) u.BurstLeft[i] = 0;
            else if (u.Burrowed[i]) u.BurstLeft[i] = BurstSeconds * Tps;
            u.Burrowed[i] = under;
        }
    }

    /// <summary>True if unit i has just burst out of the ground and its blows deal more (see <see cref="StepBurst"/>).</summary>
    public bool Bursting(int i) => (uint)i < (uint)U.Count && U.BurstLeft[i] > 0;

    /// <summary>The damage of unit i's hit on unit t landing on the part, after the part's multiplier and t's armour there.</summary>
    public int HitDamage(int i, UnitDef def, int t, BodyPart part) =>
        Tender(t, Body.Damage(Damage(i, def, t), Def(t).Body, part, def.Attack, Armour(t, part)));

    /// <summary>Unit i's armour on the part: its type's, any it has of its own, and its relic's; none while it molts or is soft.</summary>
    public int Armour(int i, BodyPart part) => ShellArmour(i, part);

    /// <summary>How unit i stands against unit t for a hit: see <see cref="Body.Angle"/>.</summary>
    public Body.Angle AngleOf(int i, int t)
    {
        var u = U;
        int dy = u.Y[i] - u.Y[t];
        if (Def(i).IsRanged) return dy >= HighGround ? Body.Angle.FromAbove : Body.Angle.Level;
        return dy > 0 ? Body.Angle.FromAbove : dy < 0 ? Body.Angle.Uphill : Body.Angle.Level;
    }

    /// <summary>
    /// The part of unit t that unit i's hit this tick lands on: a roll from the match seed, the tick and the
    /// two units, so it does not depend on the order hits are worked out in.
    /// </summary>
    public BodyPart Location(int i, int t)
    {
        var u = U;
        int roll = (int)(DeterministicRandom.Hash((int)(_sim.Tick & 0x7FFFFFFF), u.Id[i] * 65599 ^ u.Id[t], _sim.Seed ^ 0xB0D7UL) % 100);
        return Body.Pick(Def(t).Body, AngleOf(i, t), roll, Aim(i) / Body.AimShare);
    }

    /// <summary>
    /// Whether unit t's shield stops unit i's hit: only from in front of t (see <see cref="Facing"/>), with the
    /// shield's chance, rolled like <see cref="Location"/>.
    /// </summary>
    private bool Shielded(int i, int t)
    {
        int shield = Def(t).Shield;
        if (shield == 0 || !FromFront(i, t)) return false;
        var u = U;
        return DeterministicRandom.Hash((int)(_sim.Tick & 0x7FFFFFFF), u.Id[i] * 65599 ^ u.Id[t], _sim.Seed ^ 0x5D1EUL) % 100 < (uint)shield;
    }

    /// <summary>
    /// Whether unit i stands in front of unit t (within a quarter turn either side of the way t faces). A unit
    /// with nothing to face (<see cref="Facing"/> is zero) turns to whoever hits it.
    /// </summary>
    public bool FromFront(int i, int t)
    {
        var u = U;
        var (fx, fz) = Facing(t);
        if (fx == Fix64.Zero && fz == Fix64.Zero) return true;
        return fx * (u.X[i] - u.X[t]) + fz * (u.Z[i] - u.Z[t]) >= Fix64.Zero;
    }

    /// <summary>
    /// The way unit t faces (not a unit vector), or zero: the way its shield wall faces if it stands in one, else towards its target if it has one, else the way its
    /// formation faces, else towards where it walks; zero if it stands idle with none of these.
    /// </summary>
    public (Fix64 X, Fix64 Z) Facing(int t)
    {
        var u = U;
        if (u.ShieldWall[t]) return (u.WallX[t], u.WallZ[t]); // a shield wall does not turn
        int target = u.Target[t] != 0 ? u.IndexOf(u.Target[t]) : -1;
        if (target >= 0) return (u.X[target] - u.X[t], u.Z[target] - u.Z[t]);
        if (u.Formation[t] && (u.FaceX[t] != Fix64.Zero || u.FaceZ[t] != Fix64.Zero)) return (u.FaceX[t], u.FaceZ[t]);
        if (u.Order[t] != UnitOrder.Idle)
        {
            Fix64 tx = u.Formation[t] ? u.SlotX[t] : Fix64.FromInt(u.OrderX[t]) + Fix64.Half;
            Fix64 tz = u.Formation[t] ? u.SlotZ[t] : Fix64.FromInt(u.OrderZ[t]) + Fix64.Half;
            return (tx - u.X[t], tz - u.Z[t]);
        }
        return (Fix64.Zero, Fix64.Zero);
    }

    /// <summary>The nearest enemy in reach of unit i that this tick's hits do not already kill, or -1.</summary>
    private int Spare(int i, UnitDef def)
    {
        var u = U;
        Fix64 far = def.IsRanged ? def.Range * HighGroundBonus : def.Range;
        Near(u.X[i], u.Y[i], u.Z[i], far, def.IsRanged ? int.MaxValue / 2 : MeleeHeight, u.Owner[i], allies: false, _near);
        int best = -1;
        Fix64 bestD = Fix64.MaxValue;
        foreach (int j in _near)
        {
            if (_pending[j] >= u.Hp[j] || u.Subdued[j]) continue;
            int dy = u.Y[i] - u.Y[j];
            Fix64 reach = def.IsRanged && dy >= HighGround ? far : def.Range;
            Fix64 dx = u.X[j] - u.X[i], dz = u.Z[j] - u.Z[i];
            Fix64 d = dx * dx + dz * dz;
            if (d > reach * reach || d < def.MinRange * def.MinRange) continue;
            if (d < bestD || (d == bestD && u.Id[j] < u.Id[best]))
            {
                best = j;
                bestD = d;
            }
        }
        if (best >= 0 && def.IsRanged && !def.Lobs && !ClearShot(i, best)) return -1;
        return best;
    }

    private void AddShot(int i, UnitDef def, Fix64 x, int y, Fix64 z)
    {
        var u = U;
        _shots.Add(new Shot(_sim.Tick, u.Id[i], u.X[i], u.Y[i], u.Z[i], x, y, z, def.IsRanged, def.Splash > Fix64.Zero));
    }

    /// <summary>
    /// This tick's hits land; the dead shake their neighbours' morale, are counted, and are removed, except wild
    /// monsters a taming faction beats (they change sides). True if any died. <see cref="UnitStore.HitBy"/> is
    /// left naming the unit's nearest attacker this tick, or 0.
    /// </summary>
    private bool Resolve()
    {
        var u = U;
        _dead.Clear();
        for (int i = 0; i < u.Count; i++)
        {
            u.HitBy[i] = _hitBy[i];
            if (_pending[i] == 0 && _bled[i] == 0) continue;
            // Test mode's invulnerable: nothing hurts them (a test mode kill still does, see SandboxAction.Kill).
            if (_sim.Sandbox.Invulnerable(u.Owner[i]) && !_smitten.Contains(u.Id[i]))
            {
                _pending[i] = _bled[i] = 0;
                continue;
            }
            if (_pending[i] > 0)
            {
                Fought(i);
                if (_hitBy[i] != 0 && u.IndexOf(_hitBy[i]) is int by and >= 0)
                    _sim.Events.UnitAttacked(u.Owner[i], u.Owner[by], u.X[i].FloorToInt(), u.Y[i], u.Z[i].FloorToInt(), u.Id[i], u.Def[i]);
            }
            int bled = _bled[i];
            // Without bleeding deaths, the wounds stop at 1 hit point (blows can still kill).
            if (!Rules.BleedKills) bled = Math.Max(0, Math.Min(bled, u.Hp[i] - _pending[i] - 1));
            // The blood it loses is the cutter's doing: experience for it, as for a blow.
            if (bled > 0 && u.IndexOf(u.Cutter[i]) is int cutter and >= 0)
                _sim.Evolution.Damaged(cutter, Math.Min(bled, Math.Max(0, u.Hp[i] - _pending[i])));
            u.Hp[i] -= _pending[i] + bled;
            if (u.Hp[i] <= 0)
            {
                _dead.Add(i);
                if (_pending[i] == 0) BledOut++;
            }
        }
        LandFear();
        if (_dead.Count == 0) return false;
        Array.Clear(_cheer, 0, u.Count);
        _tamed.Clear();
        var monsters = _sim.Monsters;
        foreach (int d in _dead)
        {
            Mourn(d);
            // A death leaves a pool where the unit fell. (Corpses, #74, will keep bleeding until hauled off.)
            if (Def(d).Bleeds) _sim.Blood.Spill(u.X[d], u.Y[d] - 1, u.Z[d], Rts.Sim.World.BloodSystem.Pool, u.Id[d]);
            Count(_losses, u.Owner[d]);
            // The nearest blow of the tick, or for a unit that bled to death, whoever cut it. Still there: the dead are removed below.
            int killer = u.IndexOf(_hitBy[d] != 0 ? _hitBy[d] : u.Cutter[d]);
            if (killer < 0) continue;
            Count(_kills, u.Owner[killer]);
            _sim.Evolution.Killed(killer);
            BearGrudge(d, killer);
            if (!monsters.IsWild(d)) continue;
            monsters.Beaten(d, u.Owner[killer]);
            if (monsters.Tames(d, killer)) _tamed.Add((u.Id[d], u.Owner[killer]));
        }
        for (int j = 0; j < u.Count; j++)
        {
            int change = _cheer[j];
            if (change == 0 || u.Hp[j] <= 0) continue;
            int m = u.Morale[j];
            // A gain lifts morale no higher than the cap, and never lowers morale that is over it.
            // A hero near it takes some of the sting out of a loss (see Rally).
            if (change < 0 && _anyRally) change = change * (100 - _steady[j]) / 100;
            u.Morale[j] = change < 0 ? Math.Max(0, m + change) : Math.Max(m, Math.Min(MaxMorale, m + change));
        }
        // The dead drop the relics and the staff they bore where they fell; a tamed monster keeps its own.
        for (int k = _dead.Count - 1; k >= 0; k--)
            if (!Tamed(u.Id[_dead[k]]))
            {
                _sim.Items.DropFrom(_dead[k]);
                u.RemoveAt(_dead[k]);
            }
        foreach (var (id, player) in _tamed) monsters.Subdue(u.IndexOf(id), player);
        return true;
    }

    private bool Tamed(int id)
    {
        foreach (var t in _tamed)
            if (t.Id == id) return true;
        return false;
    }

    // --- Healing -----------------------------------------------------------------------------------

    /// <summary>Most levels between a healer and an ally it heals.</summary>
    public const int HealHeight = 4;

    /// <summary>
    /// Healers (<see cref="UnitDef.Heal"/>), each every heal time of its own, give hit points back to the
    /// most hurt ally in reach: the one with the smallest share of its hit points left (ties to the lowest
    /// id), never themselves. A healer that is running away heals nobody.
    /// </summary>
    private void StepHeals()
    {
        var u = U;
        long tick = _sim.Tick;
        for (int i = 0; i < u.Count; i++)
        {
            var def = Def(i);
            if (def.Heal == 0 || (tick + u.Id[i]) % def.HealTicks != 0 || u.Rout[i] > 0) continue;
            Near(u.X[i], u.Y[i], u.Z[i], def.HealRange, HealHeight, u.Owner[i], allies: true, _near);
            int best = -1, bestMax = 0;
            long bestShare = long.MaxValue;
            foreach (int j in _near)
            {
                int max = _sim.MaxHpOf(j);
                if (j == i || u.Hp[j] >= max) continue;
                // A bleeding ally comes before any that is not: it is worse off than its hit points say.
                long share = (long)u.Hp[j] * 1000 / max + (u.Bleeding[j] != 0 ? 0 : 1000);
                if (share > bestShare || (share == bestShare && u.Id[j] > u.Id[best])) continue;
                best = j;
                bestMax = max;
                bestShare = share;
            }
            if (best < 0) continue;
            int was = u.Hp[best];
            if (u.Bleeding[best] != 0) Stemmed++;
            u.StopBleeding(best); // a healer stems every bleeding wound at once
            u.Poisoned[best] = u.PoisonRate[best] = 0; // and draws out the poison
            u.Hp[best] = Math.Min(bestMax, u.Hp[best] + _sim.DuskTree.HealOf(i));
            Mend(best, u.Hp[best] - was, u.Hp[best] >= bestMax);
        }
    }

    // --- Bleeding ----------------------------------------------------------------------------------

    /// <summary>Units that bled to death (no blow landed on them in the tick they died) since the match began.</summary>
    public long BledOut { get; private set; }
    /// <summary>Times a healer stemmed a unit's bleeding since the match began.</summary>
    public long Stemmed { get; private set; }

    /// <summary>A bleeding wound of <paramref name="damage"/> on part p of unit t: it will bleed the role table's share of that away.</summary>
    private void Cut(int t, int p, int damage)
    {
        var u = U;
        int at = t * Body.Parts + p;
        int more = Rules.BleedShare == 0 ? 0 : Math.Max(1, damage * Rules.BleedShare / 100);
        if (more == 0) return;
        u.BleedLeft[at] = (byte)Math.Min(255, u.BleedLeft[at] + more);
        u.Bleeding[t] |= (byte)(1 << p);
        _sim.Blood.Spill(u.X[t], u.Y[t] - 1, u.Z[t], damage, u.Id[t]);
    }

    /// <summary>
    /// Hit points a bleeding wound with <paramref name="left"/> still to lose bleeds this second: the role
    /// table's rate, and a point more for every 20 left, so deep wounds bleed fast. Never more than is left.
    /// (Lost legs, #73, will make a crawling unit bleed faster here.)
    /// </summary>
    public int BleedLoss(int left) => Math.Min(left, Rules.BleedRate + left / 20);

    /// <summary>Hit points a grievous wound (<see cref="UnitStore.Grievous"/>) bleeds a second: enough to kill most units within half a minute untreated.</summary>
    public const int GrievousRate = 4;

    /// <summary>
    /// Opens a grievous wound on part p of unit i: a lost limb or the like (amputations, #73, call this). It
    /// bleeds <see cref="GrievousRate"/> a second and never clots on its own, so the unit dies of it unless a
    /// healer stems it or it gets home to heal. <paramref name="by"/> is the unit (id) that dealt it, or 0.
    /// </summary>
    public void OpenGrievousWound(int i, BodyPart part, int by = 0)
    {
        var u = U;
        if (!Def(i).Bleeds) return;
        int p = (int)part;
        u.Bleeding[i] |= (byte)(1 << p);
        u.Grievous[i] |= (byte)(1 << p);
        u.BleedLeft[i * Body.Parts + p] = 255;
        if (by != 0) u.Cutter[i] = by;
    }

    /// <summary>
    /// Bleeding, before the tick's fighting: every <see cref="Rts.Sim.World.BloodSystem.DripTicks"/> a bleeding unit drips
    /// on the ground it stands on, and once a second each (spread over units by id) each bleeding wound loses
    /// <see cref="BleedLoss"/> hit points, which land with the tick's hits (they do not count as being in the
    /// fight). A wound that has bled all it will clots; a grievous one (<see cref="OpenGrievousWound"/>) never does. A unit healing at home (<see cref="AtHome"/>, out of the
    /// fight for the recovery delay) has its wounds bound: they stop at once. So do a unit's whose owner knows Field
    /// dressings (<see cref="EconomySystem.BindTicks"/>) once it has been out of the fight that long, wherever it is.
    /// </summary>
    private void StepBleeding()
    {
        var u = U;
        long tick = _sim.Tick;
        var blood = _sim.Blood;
        var rules = Rules;
        _homesFound = false;
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Bleeding[i] == 0) continue;
            int x = u.X[i].FloorToInt(), z = u.Z[i].FloorToInt();
            if ((tick + u.Id[i]) % Rts.Sim.World.BloodSystem.DripTicks == 0)
                blood.Spill(u.X[i], u.Y[i] - 1, u.Z[i], Rts.Sim.World.BloodSystem.Drip, u.Id[i]);
            if ((tick + u.Id[i]) % Tps != 0) continue;
            long quiet = tick - u.LastFight[i];
            int bind = _sim.Economy.BindTicks(u.Owner[i]);
            if ((bind > 0 && quiet >= bind) || (quiet >= rules.RecoveryDelayTicks && AtHome(i)))
            {
                u.StopBleeding(i);
                continue;
            }
            int lost = 0;
            for (int p = 0; p < Body.Parts; p++)
            {
                int at = i * Body.Parts + p;
                if ((u.Bleeding[i] & (1 << p)) == 0) continue;
                if ((u.Grievous[i] & (1 << p)) != 0)
                {
                    lost += GrievousRate; // it does not clot
                    continue;
                }
                int loss = BleedLoss(u.BleedLeft[at]);
                lost += loss;
                u.BleedLeft[at] -= (byte)loss;
                if (u.BleedLeft[at] == 0) u.StopBleeding(i, p);
            }
            if (lost == 0) continue;
            _bled[i] += lost;
            blood.Spill(u.X[i], u.Y[i] - 1, u.Z[i], lost * Rts.Sim.World.BloodSystem.PerHp, u.Id[i]);
        }
    }

    /// <summary>
    /// Healing of <paramref name="healed"/> hit points mends as much of unit i's wounds, the worst part first
    /// (ties to the head end); a part mended whole stops bleeding. A unit back to full health has no wounds left.
    /// </summary>
    internal void Mend(int i, int healed, bool whole)
    {
        var u = U;
        int at = i * Body.Parts;
        if (whole)
        {
            Array.Clear(u.Wounds, at, Body.Parts);
            u.StopBleeding(i);
            return;
        }
        while (healed > 0)
        {
            int worst = -1;
            for (int p = 0; p < Body.Parts; p++)
                if (u.Wounds[at + p] > 0 && (worst < 0 || u.Wounds[at + p] > u.Wounds[at + worst])) worst = p;
            if (worst < 0) return;
            int take = Math.Min(healed, u.Wounds[at + worst]);
            u.Wounds[at + worst] -= (byte)take;
            healed -= take;
            if (u.Wounds[at + worst] == 0) u.StopBleeding(i, worst);
        }
    }

    // --- Healing over time -------------------------------------------------------------------------

    /// <summary>Unit i hit something or was hit: it heals on its own only once it has been out of the fight a while again.</summary>
    private void Fought(int i)
    {
        U.LastFight[i] = _sim.Tick;
        U.Mending[i] = Fix64.Zero;
    }

    /// <summary>
    /// Once a second each (spread over units by id), a hurt unit that has gone the role table's recovery delay
    /// without hitting anything or being hit gets back its recovery percentage of its most hit points, or
    /// the home percentage at home (<see cref="AtHome"/>). It gains whole hit points; the rest is kept toward
    /// the next. Beaten monsters waiting for their owner's choice do not heal.
    /// </summary>
    private void StepRecovery()
    {
        var u = U;
        var rules = Rules;
        long tick = _sim.Tick;
        _homesFound = false;
        for (int i = 0; i < u.Count; i++)
        {
            if ((tick + u.Id[i]) % Tps != 0 || u.Subdued[i]) continue;
            var def = Def(i);
            int regen = def.Regen;
            // Trolls' wounds close only out of the daylight.
            if (regen > 0 && def.NightRegen && _sim.Conditions.InDaylight(u.X[i].FloorToInt(), u.Y[i], u.Z[i].FloorToInt())) regen = 0;
            // The Reptilians' wounds close twice as fast in the water, and not at all in the cold (CombatSystem.Swamp.cs).
            if (regen > 0 && (def.Amphibious || def.ColdBlooded)) regen = SwampRegen(i, def, regen);
            bool resting = tick - u.LastFight[i] >= rules.RecoveryDelayTicks;
            if (!resting && regen == 0) continue;
            int max = _sim.MaxHpOf(i);
            if (u.Hp[i] >= max) continue;
            // Regenerating units (ogres) heal even in a fight, at their own rate or the resting one if faster.
            var percent = Fix64.FromInt(regen);
            if (resting) percent = Fix64.Max(percent, AtHome(i) ? rules.HomeRecoveryPercent : rules.RecoveryPercent);
            // In hundredths of a hit point, so whole percentages of whole hit points add up exactly.
            var mending = u.Mending[i] + Fix64.FromInt(max) * percent;
            int whole = (mending / 100).FloorToInt();
            int was = u.Hp[i];
            u.Hp[i] = Math.Min(max, u.Hp[i] + whole);
            Mend(i, u.Hp[i] - was, u.Hp[i] >= max);
            u.Mending[i] = u.Hp[i] < max ? mending - Fix64.FromInt(whole * 100) : Fix64.Zero;
        }
    }

    /// <summary>
    /// True if unit i is healing on its own: it is hurt and has been out of the fight for the recovery delay;
    /// <paramref name="atHome"/> if it heals at the home rate. For display.
    /// </summary>
    public bool Healing(int i, out bool atHome)
    {
        var u = U;
        atHome = false;
        if (u.Subdued[i] || _sim.Tick - u.LastFight[i] < Rules.RecoveryDelayTicks || u.Hp[i] >= _sim.MaxHpOf(i))
            return false;
        _homesFound = false; // buildings may have changed since the last tick's look
        atHome = AtHome(i);
        return true;
    }

    /// <summary>
    /// True if unit i is at home: within the role table's home range (columns) of a finished town hall, house or
    /// field hospital (<see cref="BuildingKind.Infirmary"/>) of its owner's, or within the healing reach of a city-state town its owner holds (<see cref="CityStateSystem.HealingTown"/>).
    /// </summary>
    private bool AtHome(int i)
    {
        if (!_homesFound)
        {
            _homes.Clear();
            foreach (var b in Buildings.All)
                if (b.Active && b.Def.Kind is BuildingKind.TownHall or BuildingKind.House or BuildingKind.Infirmary) _homes.Add(b);
            _homesFound = true;
        }
        var u = U;
        int owner = u.Owner[i], x = u.X[i].FloorToInt(), z = u.Z[i].FloorToInt(), range = Rules.HomeRange;
        foreach (var b in _homes)
            if (b.Owner == owner && b.ColumnDistance(x, z) <= range) return true;
        return _sim.Cities.HealingTown(owner, x, u.Y[i], z) != null;
    }

    // --- Morale ------------------------------------------------------------------------------------

    /// <summary>
    /// Unit d dies: allies near it are to lose morale, enemies near it to gain some. The changes from all of
    /// a tick's deaths are added up and made together, so the order the dead are counted in decides nothing.
    /// </summary>
    private void Mourn(int d)
    {
        var u = U;
        int owner = u.Owner[d];
        Near(u.X[d], u.Y[d], u.Z[d], MoraleRadius, MoraleHeight, owner, allies: true, _near);
        int loss = Shock / Math.Max(1, _near.Count);
        foreach (int j in _near) _cheer[j] -= loss;
        Near(u.X[d], u.Y[d], u.Z[d], MoraleRadius, MoraleHeight, owner, allies: false, _near);
        if (_near.Count == 0) return;
        int gain = Cheer / _near.Count;
        foreach (int j in _near) _cheer[j] += gain;
    }

    /// <summary>
    /// Works out which units this tick's heroes steady (<see cref="UnitDef.Rally"/>): each living hero that is
    /// not beaten steadies its own side's units within its rally range (and <see cref="MoraleHeight"/> levels),
    /// itself included, calls the volleys of the ranged ones among them, spurs on the cavalry (see
    /// <see cref="SpurOf"/>), works the breach with the sappers (see <see cref="UndermineOf"/>), quickens the
    /// casters' spells (see <see cref="QuickenOf"/>) and drives the workers' work (see <see cref="HasteOf"/>). A unit near several takes the best of each.
    /// </summary>
    private void Rally()
    {
        var u = U;
        if (_anyRally)
        {
            Array.Clear(_steady);
            Array.Clear(_volley);
        }
        _anyRally = false;
        _spurs.Clear();
        _undermines.Clear();
        _quickens.Clear();
        _hastens.Clear();
        for (int h = 0; h < u.Count; h++)
        {
            var def = Def(h);
            if (def.Rally == Fix64.Zero || u.Hp[h] <= 0 || u.Subdued[h]) continue;
            if (!_anyRally) Array.Fill(_inspire, 1, 0, u.Count);
            _anyRally = true;
            if (def.Spur > 0) _spurs.Add((u.Owner[h], u.X[h], u.Y[h], u.Z[h], def.Rally, def.Spur));
            if (def.Undermine > 0) _undermines.Add((u.Owner[h], u.X[h], u.Y[h], u.Z[h], def.Rally, def.Undermine));
            if (def.Quicken > 0) _quickens.Add((u.Owner[h], u.X[h], u.Y[h], u.Z[h], def.Rally, def.Quicken));
            if (def.Hasten > 0) _hastens.Add((u.Owner[h], u.X[h], u.Y[h], u.Z[h], def.Rally, def.Hasten));
            Near(u.X[h], u.Y[h], u.Z[h], def.Rally, MoraleHeight, u.Owner[h], allies: true, _near);
            foreach (int j in _near)
            {
                _steady[j] = Math.Max(_steady[j], def.Steady);
                _inspire[j] = Math.Max(_inspire[j], def.Inspire);
                if (Def(j).Role == UnitRole.Ranged) _volley[j] = Math.Max(_volley[j], def.Volley);
            }
        }
    }

    /// <summary>
    /// Percent faster unit i moves this tick because a hero of its own side that leads the charge
    /// (<see cref="UnitDef.Spur"/>) stood within its rally range (and <see cref="MoraleHeight"/> levels) when
    /// the tick began: cavalry only, the best of the heroes near it, else 0. Read by the movement after combat.
    /// </summary>
    public int SpurOf(int i) => _spurs.Count == 0 ? 0 : Best(_spurs, i, UnitRole.Cavalry);

    /// <summary>
    /// Percent faster unit i works this tick because a hero of its own side that works the breach
    /// (<see cref="UnitDef.Undermine"/>) stood within its rally range (and <see cref="MoraleHeight"/> levels)
    /// when the tick began: sappers only, the best of the heroes near it, else 0. Its blows on buildings land
    /// that much harder and it digs that much faster.
    /// </summary>
    public int UndermineOf(int i) => _undermines.Count == 0 ? 0 : Best(_undermines, i, UnitRole.Sapper);

    /// <summary>
    /// Percent sooner unit i is ready to cast again after a spell it casts this tick, because a hero of its own
    /// side that quickens the spells (<see cref="UnitDef.Quicken"/>) stood within its rally range (and
    /// <see cref="MoraleHeight"/> levels) when the tick began: casters only, the best of the heroes near it, else 0.
    /// </summary>
    public int QuickenOf(int i) => _quickens.Count == 0 ? 0 : Best(_quickens, i, UnitRole.Caster);

    /// <summary>
    /// Percent faster unit i works because a hero of its own side that drives the work (<see cref="UnitDef.Hasten"/>)
    /// stood within its rally range (and <see cref="MoraleHeight"/> levels) when the last combat step began:
    /// workers only, the best of the heroes near it, else 0. The economy steps before combat, so it reads last tick's.
    /// </summary>
    public int HasteOf(int i) => _hastens.Count == 0 ? 0 : Best(_hastens, i, UnitRole.Worker);

    /// <summary>The best percent of the heroes in the list that stood near unit i, if i is of the role; else 0.</summary>
    private int Best(List<(int Owner, Fix64 X, int Y, Fix64 Z, Fix64 Range, int Percent)> heroes, int i, UnitRole role)
    {
        var u = U;
        if ((uint)i >= (uint)u.Count || Def(i).Role != role) return 0;
        int best = 0;
        foreach (var s in heroes)
        {
            if (s.Owner != u.Owner[i] || s.Percent <= best || Math.Abs(u.Y[i] - s.Y) > MoraleHeight) continue;
            Fix64 dx = u.X[i] - s.X, dz = u.Z[i] - s.Z;
            if (Fix64.Abs(dx) > s.Range || Fix64.Abs(dz) > s.Range) continue;
            if (dx * dx + dz * dz <= s.Range * s.Range) best = s.Percent;
        }
        return best;
    }

    /// <summary>
    /// Once a second: morale comes back a point (more near a hero, see <see cref="Rally"/>), up to
    /// <see cref="MaxMorale"/>.
    /// </summary>
    private void RecoverMorale()
    {
        var u = U;
        for (int i = 0; i < u.Count; i++)
        {
            const int cap = MaxMorale;
            if (u.Morale[i] < cap) u.Morale[i] = Math.Min(cap, u.Morale[i] + (_anyRally ? _inspire[i] : 1));
            else if (u.Morale[i] > cap) u.Morale[i]--;
        }
    }

    /// <summary>The unit breaks: it drops its job and fight and runs for <see cref="RoutTicks"/>.</summary>
    private void StartRout(int i)
    {
        var u = U;
        u.ClearJob(i);
        u.ClearCombat(i);
        u.Rout[i] = RoutTicks;
        if (Refuge(i, out int x, out int z, out int y)) _sim.SendUnit(i, x, z, y);
        else Halt(i);
    }

    /// <summary>
    /// Where a routing unit runs: to its nearest town hall if that is well away, otherwise away from the
    /// nearest enemy. False if it has neither (it just stops).
    /// </summary>
    private bool Refuge(int i, out int x, out int z, out int y)
    {
        var u = U;
        int ux = u.X[i].FloorToInt(), uz = u.Z[i].FloorToInt();
        Building? home = null;
        long bestD = long.MaxValue;
        foreach (var b in Buildings.All)
        {
            if (b.Owner != u.Owner[i] || b.Def.Kind != BuildingKind.TownHall || !b.Active) continue;
            long d = b.CentreDistance2(ux, uz);
            if (d < bestD)
            {
                bestD = d;
                home = b;
            }
        }
        if (home != null && home.ColumnDistance(ux, uz) > HomeRadius && _sim.Economy.AccessSpot(home, out x, out z, out y))
            return true;

        x = z = y = -1;
        Near(u.X[i], u.Y[i], u.Z[i], MoraleRadius * 2, int.MaxValue / 2, u.Owner[i], allies: false, _near);
        int enemy = -1;
        Fix64 nearest = Fix64.MaxValue;
        foreach (int j in _near)
        {
            Fix64 ex = u.X[j] - u.X[i], ez = u.Z[j] - u.Z[i];
            Fix64 d = ex * ex + ez * ez;
            if (d < nearest || (d == nearest && u.Id[j] < u.Id[enemy]))
            {
                nearest = d;
                enemy = j;
            }
        }
        if (enemy < 0) return false;
        Fix64 dx = u.X[i] - u.X[enemy], dz = u.Z[i] - u.Z[enemy];
        Fix64 len = Fix64.Sqrt(dx * dx + dz * dz);
        if (len == Fix64.Zero) (dx, dz, len) = (Fix64.One, Fix64.Zero, Fix64.One);
        x = Math.Clamp((u.X[i] + dx * FleeDistance / len).FloorToInt(), 1, World.SizeX - 2);
        z = Math.Clamp((u.Z[i] + dz * FleeDistance / len).FloorToInt(), 1, World.SizeZ - 2);
        y = Walk.GroundLevel(World, x, z);
        return y >= 0;
    }

    // --- Commands ----------------------------------------------------------------------------------

    internal void Apply(AttackCommand c)
    {
        int t = U.IndexOf(c.Target);
        if (t < 0 || !Hostile(c.PlayerId, U.Owner[t])) return;
        foreach (int i in _sim.OwnedUnits(c))
        {
            if (!Def(i).CanAttack || Def(i).Ram) continue; // a ram never goes at units
            StartAttack(i);
            U.Target[i] = c.Target;
        }
    }

    internal void Apply(AttackBuildingCommand c)
    {
        var b = Buildings.Get(c.Building);
        // A player may knock out their own bridge, to cut the way across behind them.
        if (b == null || b.Planned || (b.Owner == c.PlayerId ? !b.Def.IsBridge : !Hostile(c.PlayerId, b.Owner))) return;
        foreach (int i in _sim.OwnedUnits(c))
        {
            if (!Def(i).CanAttack) continue;
            StartAttack(i);
            U.TargetBuilding[i] = b.Id;
        }
    }

    /// <summary>Sets the stance of the units that fight (workers fight only when ordered to, so they keep theirs).</summary>
    internal void Apply(StanceCommand c)
    {
        if (!Enum.IsDefined(c.Stance)) return;
        foreach (int i in _sim.OwnedUnits(c))
        {
            var def = Def(i);
            if (def.CanAttack && def.Role != UnitRole.Worker) U.Stance[i] = c.Stance;
        }
    }

    private void StartAttack(int i)
    {
        U.ClearJob(i);
        U.ClearCombat(i);
        U.Combat[i] = CombatOrder.Attack;
        Halt(i);
    }

    // --- Neighbours --------------------------------------------------------------------------------

    /// <summary>Buckets every unit by cell and player; each bucket lists its units in ascending index order.</summary>
    private void BuildGrid()
    {
        var u = U;
        int players = 1;
        for (int i = 0; i < u.Count; i++) players = Math.Max(players, u.Owner[i] + 1);
        _players = players;
        int size = _cellsX * _cellsZ * players;
        if (_head.Length < size) _head = new int[size];
        Array.Fill(_head, -1, 0, size);
        if (_next.Length < u.Capacity) _next = new int[u.Capacity];
        for (int i = u.Count - 1; i >= 0; i--)
        {
            int cx = Math.Clamp(u.X[i].FloorToInt() / CellSize, 0, _cellsX - 1);
            int cz = Math.Clamp(u.Z[i].FloorToInt() / CellSize, 0, _cellsZ - 1);
            int c = (cz * _cellsX + cx) * players + u.Owner[i];
            _next[i] = _head[c];
            _head[c] = i;
        }
    }

    /// <summary>The cells a circle of radius r around (x, z) touches.</summary>
    private void Cells(Fix64 x, Fix64 z, Fix64 r, out int x0, out int z0, out int x1, out int z1)
    {
        x0 = Math.Clamp((x - r).FloorToInt() / CellSize, 0, _cellsX - 1);
        x1 = Math.Clamp((x + r).FloorToInt() / CellSize, 0, _cellsX - 1);
        z0 = Math.Clamp((z - r).FloorToInt() / CellSize, 0, _cellsZ - 1);
        z1 = Math.Clamp((z + r).FloorToInt() / CellSize, 0, _cellsZ - 1);
    }

    /// <summary>
    /// The units within <paramref name="radius"/> blocks and <paramref name="height"/> levels of a point:
    /// those of <paramref name="owner"/> if <paramref name="allies"/>, otherwise those of every other player.
    /// In a fixed order (cell, then player, then index).
    /// </summary>
    /// <summary>The owner's units (indexes) within <paramref name="radius"/> across and <paramref name="height"/> levels of a point.</summary>
    internal void Allies(Fix64 x, int y, Fix64 z, Fix64 radius, int height, int owner, List<int> result) =>
        Near(x, y, z, radius, height, owner, allies: true, result);

    /// <summary>True if units of the two sides fight each other (different sides, not at peace: see <see cref="TruceSystem"/>).</summary>
    private bool Hostile(int a, int b) => _sim.Truces.Hostile(a, b);

    private void Near(Fix64 x, int y, Fix64 z, Fix64 radius, int height, int owner, bool allies, List<int> result)
    {
        result.Clear();
        var u = U;
        Cells(x, z, radius, out int x0, out int z0, out int x1, out int z1);
        Fix64 r2 = radius * radius;
        for (int cz = z0; cz <= z1; cz++)
        for (int cx = x0; cx <= x1; cx++)
        {
            int c = (cz * _cellsX + cx) * _players;
            for (int p = 0; p < _players; p++)
            {
                if (allies ? p != owner : !Hostile(owner, p)) continue;
                for (int j = _head[c + p]; j >= 0; j = _next[j])
                {
                    if (Math.Abs(u.Y[j] - y) > height) continue;
                    Fix64 dx = u.X[j] - x, dz = u.Z[j] - z;
                    if (Fix64.Abs(dx) > radius || Fix64.Abs(dz) > radius) continue;
                    if (dx * dx + dz * dz <= r2) result.Add(j);
                }
            }
        }
    }
}
