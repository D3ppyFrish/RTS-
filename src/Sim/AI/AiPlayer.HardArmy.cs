using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.AI;

// The Hard level's army (issue #2). It shares what it knows with the other smart levels (Look), and their
// scouting of the enemy, formation marches, focus fire, healing at home and sieges; what is its own is here:
// exploring the ground round its town, defending every building it has, picking its fights by the worth of
// the armies, falling back from a fight it is losing, and sending reinforcements after a wave.
public sealed partial class AiPlayer
{
    /// <summary>Hard: the fewest soldiers it sends as a wave.</summary>
    public const int HardWave = 12;
    /// <summary>
    /// Hard: a wave sets out only when its worth (see <see cref="Power"/>) is at least this share (percent) of
    /// the enemy army it knows of: the defender heals at home and gets its new soldiers at once, so an even
    /// fight at their door is a lost one.
    /// </summary>
    public const int HardEdgePercent = 150;
    /// <summary>
    /// Hard: a wave leaves this share (percent) of the soldiers at home behind (at least <see cref="LeastGuard"/>);
    /// more for a faction it plays balanced (see <see cref="KeepGuardPercent"/>).
    /// </summary>
    public const int HardGuardPercent = 15;
    /// <summary>Hard: soldiers trained while a wave is out follow it in groups of this many.</summary>
    public const int JoinSize = 8;
    /// <summary>
    /// Hard: a wave in a fight falls back when its worth is below this share (percent) of the enemy soldiers
    /// it sees within <see cref="OutmatchedRadius"/> columns of its middle.
    /// </summary>
    public const int LosingPercent = 60;
    /// <summary>
    /// Hard: a wave that is out comes back to defend only when the enemy soldiers at its buildings are worth
    /// more than this share (percent) of its soldiers at home, and then only if it is still within
    /// <see cref="RecallRadius"/> columns of home or the enemies are by a town hall: a raider at a far
    /// storehouse is for those at home.
    /// </summary>
    public const int RecallPercent = 80;
    /// <summary>
    /// Hard: enemies it sees this close (columns) to a town hall of its own, or to any other building of its
    /// own, are met by its soldiers at home, in formation (melee in front, ranged behind, at the pace of the
    /// slowest) so that they arrive together. Out by a far building it weighs the enemy soldiers it has seen
    /// within <see cref="ForceRadius"/> columns in the last <see cref="ForceTicks"/>: a force worth at least
    /// <see cref="HoldPercent"/> percent of its own is waited for in a line <see cref="HoldRadius"/> columns
    /// out from the town hall, where its new soldiers come out; a few raiders are met by a party worth
    /// <see cref="PartyPercent"/> percent of them, and the rest stay.
    /// </summary>
    public const int HardHallRadius = 26, HardDefendRadius = 14, HoldPercent = 50, HoldRadius = 10, ForceRadius = 40,
        ForceTicks = 10 * Tps, PartyPercent = 300;
    /// <summary>
    /// Hard: what it takes the enemy army to be worth: what it has seen of it lately, and at least what a
    /// player starts with (a scout is chased off by the fastest few and never sees the rest), and for
    /// <see cref="BeatenTicks"/> after a wave of its own was beaten, what that wave was worth when it set
    /// out. Before a wave sets out its scout has a look at their town, unless one did in the last
    /// <see cref="LookTicks"/>: the wave waits for what the scout sees.
    /// </summary>
    public const int BeatenTicks = 6 * 60 * Tps, LookTicks = 3 * 60 * Tps;
    /// <summary>
    /// Hard: one of its soldiers explores the ground round its town it has never seen, by the points of a
    /// grid this many columns apart out to this far from its town hall: that is how it finds trees, lairs
    /// and towns.
    /// </summary>
    public const int ExploreGrid = 28, ExploreReach = 112;
    /// <summary>Hard: the explorer gives a point up after this many ticks; one hurt comes home, and another sets out this long after.</summary>
    public const int ExploreGiveUpTicks = 60 * Tps, ExploreAgainTicks = 60 * Tps;

    // The explorer (unit id, 0 if none), where it is going and since when, the points it has been to or given
    // up on, and when the next may set out.
    private int _explorer;
    private (int X, int Z)? _exploreGoal;
    private long _exploreSent, _exploreAt;
    private int _exploreStops;
    private readonly List<(int X, int Z)> _explored = new();
    // Soldiers on their way to join the wave.
    private readonly List<int> _joining = new();
    // What its soldiers were worth at the start: what it takes an enemy it has not seen lately to have at least.
    private long _startPower;
    // When it last saw enemy soldiers at its buildings.
    private long _alarm = long.MinValue / 2;
    // Where it last sent its defenders in formation, and when.
    private (int X, int Z)? _defendPoint;
    private long _defendTick;
    // What the wave out was worth when it set out, and the worth of the last wave that was beaten and when.
    private long _waveWorth, _beaten, _beatenAt = long.MinValue / 2;
    // It would attack, but for a look at their town first: the scout goes (see ScoutStep). When the last look ended.
    private bool _wantLook;
    private long _lookDone = long.MinValue / 2;

    /// <summary>
    /// Hard: its soldiers at home gather beside its town hall (where <see cref="Rally"/> puts them with no
    /// enemy to face), not out in front of it toward the enemy as the other levels' do. Out in front they met
    /// a raid before the town saw it, a few at a time as each caught sight of it; beside the town hall they
    /// heal as at home, the place never moves, and raiders come up against the town and all of them at once.
    /// It builds nothing within this many columns of the place (see <see cref="Room"/>): a house going up
    /// where soldiers stand pushes them aside, into whatever hole the levelled ground leaves beside it.
    /// </summary>
    public const int RallyClear = 8;

    /// <summary>Where soldiers at home gather: Hard's beside its town hall (see <see cref="RallyClear"/>), the others' toward the enemy (see <see cref="Rally"/>).</summary>
    private (int X, int Z) RallyAt(Simulation sim, Building hall, (int X, int Z)? enemy) => Rally(sim, hall, Hard ? null : enemy);

    /// <summary>Hard: where its soldiers at home gather, the middle of the ground it keeps clear for them (see <see cref="RallyClear"/>); null without a town hall.</summary>
    public (int X, int Z)? GatheringGround(Simulation sim) =>
        Hard && sim.Economy.TownHall(Player) is { } hall ? Rally(sim, hall, null) : null;

    /// <summary>Hard: its soldiers that can go where they are sent (see <see cref="ShutIn"/>).</summary>
    private List<int> Able(Simulation sim)
    {
        var list = Soldiers(sim);
        list.RemoveAll(id => ShutIn(sim, sim.Units.IndexOf(id)));
        return list;
    }

    /// <summary>What Hard takes the enemy army to be worth (see <see cref="BeatenTicks"/>).</summary>
    private long Reckon(Simulation sim)
    {
        long theirs = Math.Max(EnemyPower(-1), _startPower);
        if (sim.Tick - _beatenAt <= BeatenTicks) theirs = Math.Max(theirs, _beaten);
        return theirs;
    }
    private readonly List<(long Tick, string Text)> _journal = new();

    /// <summary>
    /// What it decided and when, in words: a diary for match reports (see the match runner's --story). It is
    /// not part of the game and nothing reads it back.
    /// </summary>
    public IReadOnlyList<(long Tick, string Text)> Journal => _journal;

    private void Note(Simulation sim, string text) => _journal.Add((sim.Tick, text));

    /// <summary>The unit exploring round its town now (0 if none).</summary>
    public int Explorer => _exploreGoal != null ? _explorer : 0;

    /// <summary>Soldiers on their way to join the wave that is out, by id.</summary>
    public IReadOnlyList<int> Joining => _joining;

    private void HardArmy(Simulation sim, Building hall, Action<Command> issue)
    {
        Look(sim);
        Tame(sim, issue);

        var u = sim.Units;
        var (hx, hz) = Middle(hall);
        _wave.RemoveAll(id => !u.Exists(id));
        _joining.RemoveAll(id => !u.Exists(id));
        var home = Able(sim);
        home.RemoveAll(_wave.Contains);
        home.RemoveAll(_joining.Contains);
        var engines = HoldEngines(sim, home); // not sent on parties: they wait for a wave
        TotemStep(sim, home, issue);
        home.RemoveAll(_totemParty.Contains);
        Hire(sim, issue);
        Mend(sim, hall, issue);
        RuinStep(sim, hall, home, HomeGuardPercent(sim), issue);
        ScoutStep(sim, hall, home, issue);
        Explore(sim, hall, home, issue);
        HuntStep(sim, hall, home, HomeGuardPercent(sim), issue);
        SiteStep(sim, hall, home, HomeGuardPercent(sim), issue);
        GreatWorkStep(sim, hall, home, HomeGuardPercent(sim), issue);
        home.AddRange(engines);
        Fleet(sim, hall, home, issue);

        // Defence: enemies it sees by any of its buildings are met by everyone at home, and by the wave too
        // when those at home are not enough.
        if (Threat(sim, out int tx, out int tz, out long threat))
        {
            if (Doing != "defend" && threat > 0) Note(sim, $"defends its town against soldiers worth {threat / 1000}k at {tx},{tz} with {home.Count} at home worth {PowerOf(sim, home) / 1000}k");
            Doing = "defend";
            if (threat > 0) _alarm = sim.Tick;
            // The wave comes back only for a force those at home cannot see off, and from far away only for a town hall.
            if (_wave.Count > 0 && _waveLair == 0 && threat * 100 > PowerOf(sim, home) * RecallPercent
                && (WaveNear(sim, hx, hz, RecallRadius) || ByHall(sim, tx, tz)))
            {
                Note(sim, $"calls its wave of {_wave.Count} home to help");
                home.AddRange(_wave);
                home.AddRange(_joining);
                _joining.Clear();
                EndWave();
            }
            // Archers on its wall near the enemies stay up there and shoot; the rest go to meet them.
            var afoot = home.FindAll(id => !(u.IndexOf(id) is >= 0 and var p && Posted(sim, p)
                && Dist2(u.X[p].FloorToInt(), u.Z[p].FloorToInt(), tx, tz) <= (long)KeepPostHold * KeepPostHold));
            Defend(sim, hall, afoot, tx, tz, issue);
            Micro(sim, home, issue);
            if (_wave.Count == 0) return;
        }
        else _defendPoint = null;

        if (_wave.Count > 0)
        {
            if (_waveLair == 0)
            {
                Doing = _stage != null ? "march" : "attack";
                Withdraw(sim, hall, issue);
                if (_wave.Count == 0)
                {
                    Note(sim, "its wave is beaten: the last of it are dead or gone home hurt");
                    (_beaten, _beatenAt) = (_waveWorth, sim.Tick);
                    EndWave();
                    return;
                }
                if (_wave.Count * 100 < _waveSize * RetreatPercent)
                {
                    Note(sim, $"its wave is beaten: the {_wave.Count} left of {_waveSize} come home");
                    (_beaten, _beatenAt) = (_waveWorth, sim.Tick);
                    FallBack(sim, hall, issue);
                    return;
                }
                if (Outmatched(sim) || Losing(sim))
                {
                    Note(sim, $"pulls its wave of {_wave.Count} back from a fight it is losing");
                    (_beaten, _beatenAt) = (_waveWorth, sim.Tick);
                    FallBack(sim, hall, issue);
                    return;
                }
                Micro(sim, _wave, issue);
                Reinforce(sim, home, issue);
                if (Ferry(sim, issue) || Marching(sim) || Seeking(sim, hall, issue)) return;
            }
            else Doing = "lair";
            StepWave(sim, hall, issue);
            return;
        }
        if (_joining.Count > 0)
        {
            home.AddRange(_joining); // the wave is over: they are soldiers at home again
            _joining.Clear();
        }

        // A wave sets out when it is clearly stronger than the enemy army it knows of.
        Doing = "gather";
        home.RemoveAll(_mending.Contains);
        var enemyHall = EnemyHall(sim, hx, hz);
        int guard = Math.Max(LeastGuard, home.Count * HomeGuardPercent(sim) / 100);
        int ready = home.Count - guard;
        var target = enemyHall ?? KnownBuilding(hx, hz);
        var goal = target is { } t ? (t.AX, t.AZ) : SearchGoal(sim, hx, hz, hx, hz);
        _wantLook = false;
        if (goal is var (ex, ez) && ready >= HardWave)
        {
            home.Sort((a, b) =>
            {
                int ia = u.IndexOf(a), ib = u.IndexOf(b);
                // Catapults always go with the wave: they are no use at home, and the wave is what screens them.
                int c = EngineAt(sim, ib).CompareTo(EngineAt(sim, ia));
                if (c == 0) c = Dist2(u.X[ia].FloorToInt(), u.Z[ia].FloorToInt(), ex, ez).CompareTo(Dist2(u.X[ib].FloorToInt(), u.Z[ib].FloorToInt(), ex, ez));
                return c != 0 ? c : a.CompareTo(b);
            });
            var wave = home.GetRange(0, ready);
            long mine = PowerOf(sim, wave), seen = EnemyPower(-1), theirs = Reckon(sim);
            if (mine * 100 >= theirs * HardEdgePercent)
            {
                // A look at their town first, unless it has just had one: a scout that has got to where their
                // soldiers gather, or been chased off on the way, has seen more of them than one that has only
                // just caught sight of the town.
                bool looked = !sim.Fog || sim.Tick - _lookDone <= LookTicks;
                if (!looked) _wantLook = true;
                else
                {
                    Note(sim, $"sends a wave of {wave.Count} (worth {mine / 1000}k; it has seen {seen / 1000}k of theirs and reckons with {theirs / 1000}k)"
                        + (target is { } tt ? $" at their {tt.Kind} at {tt.X},{tt.Z}" : " to where it guesses their town is"));
                    Launch(sim, wave, (ex, ez), target?.Id ?? 0, issue);
                    _waveWorth = mine;
                    return;
                }
            }
        }
        if (sim.Tick % (5 * ArmyTicks) < ArmyTicks)
        {
            PostGuard(sim, home, goal ?? (sim.World.SizeX / 2, sim.World.SizeZ / 2), issue);
            RallyHome(sim, hall, enemyHall, home, issue);
        }
    }

    /// <summary>
    /// Sends its soldiers at home against the enemies at (tx, tz) (see <see cref="HardHallRadius"/>): all of
    /// them when the enemies are by a town hall of its own; by a far building, all of them to a line by the
    /// town hall if the force is a real one, or a party big enough for a few raiders.
    /// </summary>
    private void Defend(Simulation sim, Building hall, List<int> home, int tx, int tz, Action<Command> issue)
    {
        var u = sim.Units;
        var (hx, hz) = Middle(hall);
        var who = home;
        var point = (X: tx, Z: tz);
        if (!ByHall(sim, tx, tz))
        {
            long force = 0;
            foreach (var sp in _spotted.Values)
                if (sim.Tick - sp.Tick <= ForceTicks && Dist2(sp.X, sp.Z, tx, tz) <= (long)ForceRadius * ForceRadius) force += sp.Power;
            if (force * 100 >= PowerOf(sim, home) * HoldPercent) point = Toward(hx, hz, tx, tz, HoldRadius, sim.World);
            else
            {
                // A party for the raiders: the nearest soldiers, until they are worth enough (three at least).
                who = new List<int>(home);
                who.Sort((a, b) =>
                {
                    int ia = u.IndexOf(a), ib = u.IndexOf(b);
                    int c = Dist2(u.X[ia].FloorToInt(), u.Z[ia].FloorToInt(), tx, tz).CompareTo(Dist2(u.X[ib].FloorToInt(), u.Z[ib].FloorToInt(), tx, tz));
                    return c != 0 ? c : a.CompareTo(b);
                });
                long worth = 0;
                int n = 0;
                while (n < who.Count && (n < 3 || worth * 100 < force * PartyPercent))
                    worth += PowerOf(sim, who.GetRange(n++, 1));
                who.RemoveRange(n, who.Count - n);
            }
        }
        // In formation when it first sends them, or the enemy has moved on; after that the ones standing about go again.
        if (_defendPoint is not var (px, pz) || Dist2(px, pz, point.X, point.Z) > 12 * 12 || sim.Tick - _defendTick > 20 * Tps)
        {
            _defendPoint = point;
            _defendTick = sim.Tick;
            var free = NotFighting(sim, who);
            if (free.Count == 0) return;
            long dx = point.X - hx, dz = point.Z - hz;
            long len = IntMath.ISqrt(dx * dx + dz * dz);
            long half = Math.Max(3, free.Count / 4);
            var w = sim.World;
            if (len == 0 || free.Count < 3)
            {
                issue(new AttackMoveCommand(Player, free.ToArray(), point.X, point.Z));
                return;
            }
            int x1 = Math.Clamp(point.X - (int)(dz * half / len), 1, w.SizeX - 2), z1 = Math.Clamp(point.Z + (int)(dx * half / len), 1, w.SizeZ - 2);
            int x2 = Math.Clamp(point.X + (int)(dz * half / len), 1, w.SizeX - 2), z2 = Math.Clamp(point.Z - (int)(dx * half / len), 1, w.SizeZ - 2);
            if (x1 == x2 && z1 == z2) issue(new AttackMoveCommand(Player, free.ToArray(), point.X, point.Z));
            else issue(new FormationMoveCommand(Player, free.ToArray(), x1, z1, x2, z2, Attack: true));
            return;
        }
        if (sim.Tick % (3 * ArmyTicks) >= ArmyTicks) return;
        var idle = Unordered(sim, who, point.X, point.Z);
        idle.RemoveAll(id => u.Order[u.IndexOf(id)] != UnitOrder.Idle);
        if (idle.Count > 0) issue(new AttackMoveCommand(Player, idle.ToArray(), point.X, point.Z));
    }

    /// <summary>
    /// True if column (x, z) is within <see cref="HardHallRadius"/> columns of a finished town hall of its own,
    /// or of where its soldiers gather (see <see cref="GatheringGround"/>).
    /// </summary>
    private bool ByHall(Simulation sim, int x, int z)
    {
        if (GatheringGround(sim) is var (gx, gz) && Dist2(x, z, gx, gz) <= (long)HardHallRadius * HardHallRadius) return true;
        foreach (var b in sim.Buildings.All)
        {
            if (b.Owner != Player || !b.Active || b.Def.Kind != BuildingKind.TownHall) continue;
            var (mx, mz) = Middle(b);
            if (Dist2(x, z, mx, mz) <= (long)HardHallRadius * HardHallRadius) return true;
        }
        return false;
    }

    /// <summary>
    /// True if it sees enemies by its buildings: within <see cref="HardHallRadius"/> columns of a town hall of
    /// its own or of where its soldiers gather (enemies that come at them there are met by all of them
    /// together, not one by one as each catches sight of them), or <see cref="HardDefendRadius"/> of any
    /// other building. Gives where the one nearest a building is, and the worth of the soldiers among them.
    /// </summary>
    private bool Threat(Simulation sim, out int tx, out int tz, out long power)
    {
        (tx, tz, power) = (0, 0, 0);
        var u = sim.Units;
        long best = long.MaxValue;
        var mine = new List<(int X, int Z, long R2)>();
        foreach (var b in sim.Buildings.All)
        {
            if (b.Owner != Player || b.Planned || b.Def.IsWall) continue;
            var (mx, mz) = Middle(b);
            if (b.Def.Kind != BuildingKind.TownHall && Outpost(mx, mz)) continue; // a walled town does not go out that far
            long r = b.Def.Kind == BuildingKind.TownHall ? HardHallRadius : HardDefendRadius;
            mine.Add((mx, mz, r * r));
        }
        if (GatheringGround(sim) is var (gx, gz)) mine.Add((gx, gz, (long)HardHallRadius * HardHallRadius));
        for (int i = 0; i < u.Count; i++)
        {
            int owner = u.Owner[i];
            if (!Rival(owner) || u.Subdued[i] || u.Town[i] != 0) continue;
            if (!sim.Vision.SeesUnit(Player, i)) continue;
            int x = u.X[i].FloorToInt(), z = u.Z[i].FloorToInt();
            long near = long.MaxValue;
            foreach (var (mx, mz, r2) in mine)
            {
                long d = Dist2(x, z, mx, mz);
                if (d <= r2 && d < near) near = d;
            }
            if (near == long.MaxValue) continue;
            var def = sim.Content.Units[u.Def[i]];
            if (def.CanAttack && def.Role is not (UnitRole.Worker or UnitRole.Trader)) power += Power(def, u.Hp[i]);
            if (near < best) (best, tx, tz) = (near, x, z);
        }
        return best != long.MaxValue;
    }

    /// <summary>
    /// True if the wave is in a fight it is losing: the enemy soldiers it sees round it are worth much more
    /// than it is (see <see cref="LosingPercent"/>). <see cref="Outmatched"/> is the same before the fight
    /// starts, at a wider margin.
    /// </summary>
    private bool Losing(Simulation sim)
    {
        var u = sim.Units;
        var (cx, cz) = Centroid(sim, _wave);
        long theirs = 0;
        int n = 0;
        long r2 = (long)OutmatchedRadius * OutmatchedRadius;
        for (int i = 0; i < u.Count; i++)
        {
            int owner = u.Owner[i];
            if (!Rival(owner) || u.Subdued[i] || u.Town[i] != 0) continue;
            var def = sim.Content.Units[u.Def[i]];
            if (!def.CanAttack || def.Role is UnitRole.Worker or UnitRole.Trader) continue;
            if (Dist2(u.X[i].FloorToInt(), u.Z[i].FloorToInt(), cx, cz) > r2 || !sim.Vision.SeesUnit(Player, i)) continue;
            theirs += Power(def, u.Hp[i]);
            n++;
        }
        return n >= 3 && PowerOf(sim, _wave) * 100 < theirs * LosingPercent;
    }

    /// <summary>The wave comes home; the hurt among it go to heal.</summary>
    private void FallBack(Simulation sim, Building hall, Action<Command> issue)
    {
        var u = sim.Units;
        var (hx, hz) = Middle(hall);
        var (rx, rz) = RallyAt(sim, hall, Spot(EnemyHall(sim, hx, hz)));
        _wave.AddRange(_joining);
        _joining.Clear();
        issue(new MoveCommand(Player, _wave.ToArray(), rx, rz));
        foreach (int id in _wave)
            if (Hurt(sim, u.IndexOf(id), HealedPercent) && !_mending.Contains(id)) _mending.Add(id);
        EndWave();
        Doing = "fall back";
    }

    /// <summary>
    /// While a wave is out, the soldiers trained since follow it in groups of <see cref="JoinSize"/>: they
    /// walk to where the wave is, fighting what they meet, and are part of it once they get there. With the wave
    /// across the water, ships lift them over to it first (see <see cref="Lift"/>).
    /// </summary>
    private void Reinforce(Simulation sim, List<int> home, Action<Command> issue)
    {
        var u = sim.Units;
        var (cx, cz) = Centroid(sim, _wave);
        // Those that have caught up are part of the wave.
        for (int k = _joining.Count - 1; k >= 0; k--)
        {
            int i = u.IndexOf(_joining[k]);
            if (u.Aboard[i] != 0 || Dist2(u.X[i].FloorToInt(), u.Z[i].FloorToInt(), cx, cz) > 16L * 16) continue;
            _wave.Add(_joining[k]);
            _waveSize++;
            _waveWorth += Power(sim.Content.Units[u.Def[i]], u.Hp[i]);
            _joining.RemoveAt(k);
        }
        var ready = new List<int>(home);
        ready.RemoveAll(_mending.Contains);
        int guard = Math.Max(LeastGuard, ready.Count * HomeGuardPercent(sim) / 100);
        if (ready.Count - guard >= JoinSize) _joining.AddRange(ready.GetRange(0, ready.Count - guard));
        // Across the water from the wave, they go by ship (see Lift).
        Lift(sim, issue);
        if (_joining.Count == 0 || sim.Tick % (5 * ArmyTicks) >= ArmyTicks) return;
        var go = Unordered(sim, _joining, cx, cz);
        go.RemoveAll(id => Lifted(sim, id));
        if (go.Count > 0) issue(new AttackMoveCommand(Player, go.ToArray(), cx, cz));
    }

    /// <summary>
    /// One soldier (its fastest at home that is not the scout) explores round its town: it walks to the
    /// nearest point of the <see cref="ExploreGrid"/> grid within <see cref="ExploreReach"/> of its town hall
    /// that it has never seen, then the next, until there is none left. Hurt, it comes home to heal, and
    /// another sets out a while later. The explorer is taken out of <paramref name="home"/>.
    /// </summary>
    private void Explore(Simulation sim, Building hall, List<int> home, Action<Command> issue)
    {
        var u = sim.Units;
        long tick = sim.Tick;
        if (_explorer != 0 && !u.Exists(_explorer))
        {
            Note(sim, "loses the soldier exploring round its town");
            _explorer = 0;
            _exploreGoal = null;
            _exploreAt = tick + ExploreAgainTicks;
        }
        if (_explorer != 0 && (_explorer == _scout || ShutIn(sim, u.IndexOf(_explorer))))
        {
            _explorer = 0; // the scout was picked from those at home too; one shut in where it stands goes nowhere
            _exploreGoal = null;
        }
        if (_exploreGoal is var (gx, gz))
        {
            int i = u.IndexOf(_explorer);
            home.Remove(_explorer);
            bool seen = sim.Vision.At(Player, gx, gz) != Sight.Unseen;
            bool hurt = Hurt(sim, i, ScoutHurtPercent);
            if (u.Order[i] == UnitOrder.Idle && u.Rout[i] == 0) _exploreStops++;
            if (!seen && !hurt && tick - _exploreSent <= ExploreGiveUpTicks && _exploreStops <= 3)
            {
                if (u.Order[i] == UnitOrder.Idle && u.Rout[i] == 0) issue(new MoveCommand(Player, new[] { _explorer }, gx, gz));
                return;
            }
            if (!seen) _explored.Add((gx, gz));
            _exploreGoal = null;
            if (hurt)
            {
                SendHome(sim, hall, _explorer, issue);
                if (!_mending.Contains(_explorer)) _mending.Add(_explorer);
                _explorer = 0;
                _exploreAt = tick + ExploreAgainTicks;
                return;
            }
        }
        if (tick < _exploreAt) return;
        int from = _explorer != 0 ? u.IndexOf(_explorer) : -1;
        var (hx, hz) = Middle(hall);
        var goal = ExploreGoal(sim, hx, hz, from >= 0 ? u.X[from].FloorToInt() : hx, from >= 0 ? u.Z[from].FloorToInt() : hz);
        if (goal != null && (_explorer != 0 || (_explorer = PickExplorer(sim, home)) != 0))
        {
            home.Remove(_explorer);
            _exploreGoal = goal;
            _exploreSent = tick;
            _exploreStops = 0;
            issue(new MoveCommand(Player, new[] { _explorer }, goal.Value.X, goal.Value.Z));
            return;
        }
        if (_explorer != 0)
        {
            SendHome(sim, hall, _explorer, issue); // nothing left to look at: a soldier like the others again
            _explorer = 0;
        }
    }

    /// <summary>The fastest soldier at home that is not hurt, mending or the scout (ties to the lowest id), or 0.</summary>
    private int PickExplorer(Simulation sim, List<int> home)
    {
        var u = sim.Units;
        int best = 0;
        Fix64 bestSpeed = Fix64.Zero;
        foreach (int id in home)
        {
            int i = u.IndexOf(id);
            if (i < 0 || id == _scout || _mending.Contains(id) || Hurt(sim, i, HealedPercent) || ShutIn(sim, i)) continue;
            var speed = sim.Content.Units[u.Def[i]].Speed;
            if (best == 0 || speed > bestSpeed) (best, bestSpeed) = (id, speed);
        }
        return best;
    }

    /// <summary>
    /// The point of the exploring grid (see <see cref="Explore"/>) it has never seen or been sent to that is
    /// nearest (x, z), or null when it has seen them all.
    /// </summary>
    private (int X, int Z)? ExploreGoal(Simulation sim, int hx, int hz, int x, int z)
    {
        var w = sim.World;
        (int X, int Z)? best = null;
        long bestD = long.MaxValue;
        for (int dz = -ExploreReach; dz <= ExploreReach; dz += ExploreGrid)
        for (int dx = -ExploreReach; dx <= ExploreReach; dx += ExploreGrid)
        {
            int px = hx + dx, pz = hz + dz;
            if (px < 2 || pz < 2 || px >= w.SizeX - 2 || pz >= w.SizeZ - 2) continue;
            if (sim.Vision.At(Player, px, pz) != Sight.Unseen || _explored.Contains((px, pz))) continue;
            // Near home first, then what is near where it stands.
            long d = Dist2(px, pz, hx, hz) * 2 + Dist2(px, pz, x, z);
            if (d < bestD) (bestD, best) = (d, (px, pz));
        }
        return best;
    }
}
