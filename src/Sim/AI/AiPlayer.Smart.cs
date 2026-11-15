using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.AI;

/// <summary>How well a computer player (<see cref="AiPlayer"/>) plays.</summary>
public enum AiLevel : byte
{
    /// <summary>
    /// The first computer player (issue #15), kept as it was to measure the others against: it sees through
    /// the fog of war, trains a fixed mix and fights with its whole wave as one.
    /// </summary>
    Classic,
    /// <summary>
    /// Plays by the fog of war: it scouts, remembers what it has seen, trains counters to the soldiers it has
    /// seen, sizes its waves to the enemy army it knows of, marches in formation (ranged troops behind),
    /// pulls wounded soldiers home to heal, falls back from a fight it is losing, and its sappers go for
    /// buildings.
    /// </summary>
    Normal,
    /// <summary>
    /// As <see cref="Normal"/>, and also: focus fire and counter-targeting in fights, more workers, a third
    /// barracks when rich, a second town hall on new ground, and a stone wall with gates round its town hall.
    /// </summary>
    Hard,
}

// The smart levels (Normal and Hard; issue #47). The economy is the classic one with a few additions
// (SmartEconomy); the army is run by SmartArmy instead of Army.
public sealed partial class AiPlayer
{
    /// <summary>Most workers at <see cref="AiLevel.Hard"/>.</summary>
    public const int HardMostWorkers = 36;
    /// <summary>Second barracks at the smart levels from this tick; a third (Hard, when rich) from this one.</summary>
    public const int SmartSecondBarracksTicks = 8 * 60 * Tps, ThirdBarracksTicks = 15 * 60 * Tps;
    /// <summary>Soldiers seen are remembered this long (ticks) after they were last seen.</summary>
    public const int SpotMemoryTicks = 3 * 60 * Tps;
    /// <summary>With the enemy's town hall known, it is looked at again this often (ticks) while no wave is out.</summary>
    public const int RescoutTicks = 3 * 60 * Tps;
    /// <summary>A scout that has not reached where it was sent in this long (ticks) is sent somewhere else.</summary>
    public const int ScoutGiveUpTicks = 3 * 60 * Tps;
    /// <summary>Only soldiers at least this fast (blocks a second) scout; a scout below this share (percent) of its hit points comes home.</summary>
    public static readonly Fix64 ScoutSpeed = Fix64.FromInt(6);
    public const int ScoutHurtPercent = 70;
    /// <summary>Scouting goals off the start areas are on a grid this many columns apart.</summary>
    public const int ScoutGrid = 48;
    /// <summary>A soldier of a wave below this share (percent) of its hit points goes home to heal...</summary>
    public const int WoundedPercent = 35;
    /// <summary>...and stays home until it is back to this share (percent).</summary>
    public const int HealedPercent = 85;
    /// <summary>A wave sets out in formation to a point this many columns short of its target, then attacks.</summary>
    public const int StageDistance = 24;
    /// <summary>A wave still marching after this many ticks attacks from wherever it is.</summary>
    public const int MarchTicks = 90 * Tps;
    /// <summary>
    /// A wave falls back, before the fight starts, when the enemy soldiers it sees within
    /// <see cref="OutmatchedRadius"/> columns of its middle are stronger than it by this ratio (percent; see
    /// <see cref="Power"/>).
    /// </summary>
    public const int OutmatchedPercent = 200, OutmatchedRadius = 20;
    /// <summary>
    /// Once an enemy soldier is this close (columns) to the wave's middle the fight is on, and the wave does
    /// not fall back: turning away from a fight it is in would only get it cut down from behind.
    /// </summary>
    public const int EngagedRadius = 8;
    /// <summary>Hard: a second town hall from this tick on.</summary>
    public const int ExpandTicks = 10 * 60 * Tps;
    /// <summary>Hard: a wall round the town hall from this tick on, this many columns out from its walls.</summary>
    public const int WallTicks = 9 * 60 * Tps, WallGap = 4;
    /// <summary>
    /// The smart levels send a party of this many soldiers to break the totem of the nearest city-state town
    /// they do not hold, from this tick on, while no wave is out and enough soldiers are home; a party that
    /// fails is tried again this long after (ticks), and one that has not won in that long comes home.
    /// </summary>
    public const int TotemParty = 6, TotemTicks = 6 * 60 * Tps, TotemRetryTicks = 3 * 60 * Tps;

    /// <summary>The soldiers sent to break a city-state's totem, by id.</summary>
    public IReadOnlyList<int> TotemPartyIds => _totemParty;

    // What it knows of other players' buildings (by id): seen, and not seen gone since.
    private readonly SortedDictionary<int, Known> _known = new();
    // Other players' soldiers it has seen (by id), where and when.
    private readonly SortedDictionary<int, Spot> _spotted = new();
    private readonly List<int> _forget = new();
    // The scout (unit id, 0 if none), where it is going, since when, where it has been, and when it may go again.
    private int _scout;
    private (int X, int Z)? _scoutGoal;
    private long _scoutSent;
    private int _scoutStops, _scoutLost;
    private long _scoutAt;
    // A wave sent to where it guesses an enemy town hall is (a start area it has not seen), before it knows.
    private (int X, int Z)? _waveGuess;
    private readonly List<(int X, int Z)> _scouted = new();
    // When it last saw an enemy town hall.
    private long _hallSeen = long.MinValue;
    // Soldiers home to heal.
    private readonly List<int> _mending = new();
    // The wave's staging point while it marches there, and since when.
    private (int X, int Z)? _stage;
    private long _marchStart;
    // Sappers to send at a building (see StepWave).
    private List<int>? _sappers;
    // Hard: the wall round the town hall has been laid out.
    private bool _walled;
    // The party sent to break a city-state's totem (unit ids), the town, when it set out, and when the next may.
    private readonly List<int> _totemParty = new();
    private int _totemTown;
    private long _totemSent, _totemAt = TotemTicks;

    private readonly record struct Known(int Owner, BuildingKind Kind, int X, int Z);

    private readonly record struct Spot(int Owner, UnitRole Role, int X, int Z, long Tick, long Power);

    /// <summary>The unit scouting now (0 if none).</summary>
    public int Scout => _scoutGoal != null ? _scout : 0;

    /// <summary>Soldiers sent home to heal, by id.</summary>
    public IReadOnlyList<int> Mending => _mending;

    /// <summary>True if it knows where an enemy town hall is (it has seen it, and not seen it gone).</summary>
    public bool KnowsEnemyHall
    {
        get
        {
            foreach (var k in _known.Values)
                if (k.Kind == BuildingKind.TownHall) return true;
            return false;
        }
    }

    /// <summary>Enemy soldiers it remembers seeing, by id.</summary>
    public IEnumerable<int> Spotted => _spotted.Keys;

    private int SecondBarracksAt => Smart ? SmartSecondBarracksTicks : SecondBarracksTicks;

    /// <summary>
    /// No wave of the smart levels sets out before this: the classic player's first wave comes at three
    /// minutes, and meeting it at home, by the town hall, beats meeting it in the field.
    /// </summary>
    public const int SmartFirstWaveTicks = 5 * 60 * Tps;

    /// <summary>From this tick on the smart levels want more wood (see <see cref="Wood"/>).</summary>
    public const int SmartWoodTicks = 6 * 60 * Tps;

    /// <summary>Food in hand (on top of the reserve) above which the smart levels move workers from food to wood.</summary>
    public const int FoodRich = 150;

    /// <summary>
    /// While it wants its first or second barracks and cannot pay for it yet, keeps the price in hand so that
    /// research and the rest do not spend it first.
    /// </summary>
    private void SaveForBarracks(Simulation sim, int barracks, bool site)
    {
        var def = FirstBuilding(sim, BuildingKind.Barracks);
        if (def == null || site || barracks >= 2) return;
        if (barracks == 0)
        {
            int workers = 0;
            var u = sim.Units;
            for (int i = 0; i < u.Count; i++)
                if (u.Owner[i] == Player && sim.Content.Units[u.Def[i]].Role == UnitRole.Worker) workers++;
            if (workers < BarracksWorkers) return;
        }
        else if (sim.Tick < SecondBarracksAt) return;
        if (Affordable(sim, def.Cost)) return;
        foreach (var c in def.Cost) _keep[(int)c.Good] += c.Amount;
    }

    /// <summary>True if the player has three times the cost on top of what it keeps in hand.</summary>
    private bool Rich(Simulation sim, GoodAmount[] cost)
    {
        var stock = sim.Economy.Stockpile(Player);
        foreach (var c in cost)
            if (stock[(int)c.Good] < 3 * c.Amount + _keep[(int)c.Good]) return false;
        return true;
    }

    /// <summary>A unit's worth in a fight: hit points times damage per second.</summary>
    private static long Power(UnitDef def, int hp) => (long)Math.Max(0, hp) * def.Damage * Tps / Math.Max(1, def.AttackTicks);

    private static bool Hurt(Simulation sim, int i, int percent) =>
        sim.Units.Hp[i] * 100 < sim.Content.Units[sim.Units.Def[i]].MaxHp * percent;

    // --- What it sees ------------------------------------------------------------------------------

    /// <summary>Updates what it knows of other players' buildings and soldiers from what it sees now.</summary>
    private void Look(Simulation sim)
    {
        var vision = sim.Vision;
        long tick = sim.Tick;
        bool inMatch = sim.Match.Players.Count > 0;
        foreach (var b in sim.Buildings.All)
        {
            if (b.Owner == Player || b.Owner == Simulation.WildPlayer || b.Planned) continue; // others' plans are not seen
            var (mx, mz) = Middle(b);
            if (!vision.Sees(Player, mx, mz) && !vision.Sees(Player, b.X, b.Z) && !vision.Sees(Player, b.MaxX, b.Z)
                && !vision.Sees(Player, b.X, b.MaxZ) && !vision.Sees(Player, b.MaxX, b.MaxZ)) continue;
            _known[b.Id] = new Known(b.Owner, b.Def.Kind, mx, mz);
            if (b.Def.Kind == BuildingKind.TownHall) _hallSeen = tick;
        }
        _forget.Clear();
        foreach (var (id, k) in _known)
        {
            if (inMatch && !sim.Match.InPlay(k.Owner)) _forget.Add(id);
            else if (vision.Sees(Player, k.X, k.Z) && (sim.Buildings.Get(id) is not { } b || b.Owner != k.Owner))
                _forget.Add(id); // seen gone (or taken)
        }
        foreach (int id in _forget) _known.Remove(id);

        var u = sim.Units;
        for (int i = 0; i < u.Count; i++)
        {
            int owner = u.Owner[i];
            if (owner == Player || owner == Simulation.WildPlayer || u.Subdued[i]) continue;
            var def = sim.Content.Units[u.Def[i]];
            if (!def.CanAttack || def.Role is UnitRole.Worker or UnitRole.Trader) continue;
            if (!vision.SeesUnit(Player, i)) continue;
            _spotted[u.Id[i]] = new Spot(owner, def.Role, u.X[i].FloorToInt(), u.Z[i].FloorToInt(), tick, Power(def, u.Hp[i]));
        }
        _forget.Clear();
        foreach (var (id, s) in _spotted)
        {
            // Forgotten after a while, or at once when it is gone from where it was seen dying.
            if (tick - s.Tick > SpotMemoryTicks || (u.IndexOf(id) < 0 && vision.Sees(Player, s.X, s.Z))) _forget.Add(id);
        }
        foreach (int id in _forget) _spotted.Remove(id);
    }

    /// <summary>The nearest enemy town hall it knows of, of a player still in the match, or null.</summary>
    private Building? KnownHall(Simulation sim, int x, int z)
    {
        Building? best = null;
        long bestD = long.MaxValue;
        foreach (var (id, k) in _known)
        {
            if (k.Kind != BuildingKind.TownHall) continue;
            if (sim.Buildings.Get(id) is not { } b || b.Owner != k.Owner || !b.Active) continue;
            long d = Dist2(k.X, k.Z, x, z);
            if (d < bestD) (bestD, best) = (d, b);
        }
        return best;
    }

    /// <summary>The nearest enemy building it knows of, or null.</summary>
    private Building? KnownBuilding(Simulation sim, int x, int z)
    {
        Building? best = null;
        long bestD = long.MaxValue;
        foreach (var (id, k) in _known)
        {
            if (sim.Buildings.Get(id) is not { } b || b.Owner != k.Owner) continue;
            long d = Dist2(k.X, k.Z, x, z);
            if (d < bestD) (bestD, best) = (d, b);
        }
        return best;
    }

    /// <summary>The worth (see <see cref="Power"/>) of the soldiers of a player (-1: of everyone else) it remembers seeing.</summary>
    private long EnemyPower(int owner)
    {
        long sum = 0;
        foreach (var s in _spotted.Values)
            if (owner < 0 || s.Owner == owner) sum += s.Power;
        return sum;
    }

    /// <summary>
    /// Where it guesses an enemy town hall is while it knows of none: just short of the nearest start area of
    /// another player still in the match that it has not seen. Null if there is none.
    /// </summary>
    private (int X, int Z)? GuessHall(Simulation sim, int hx, int hz)
    {
        (int X, int Z)? best = null;
        long bestD = long.MaxValue;
        foreach (var s in sim.Map.Starts)
        {
            if (s.Player == Player || (sim.Match.Players.Count > 0 && !sim.Match.InPlay(s.Player))) continue;
            if (sim.Vision.At(Player, s.X, s.Z) != Sight.Unseen) continue;
            long d = Dist2(hx, hz, s.X, s.Z);
            if (d < bestD) (bestD, best) = (d, Toward(s.X, s.Z, hx, hz, 10, sim.World));
        }
        return best;
    }

    /// <summary>The worth of the units by id, as they are now.</summary>
    private static long PowerOf(Simulation sim, List<int> ids)
    {
        var u = sim.Units;
        long sum = 0;
        foreach (int id in ids)
        {
            int i = u.IndexOf(id);
            if (i >= 0) sum += Power(sim.Content.Units[u.Def[i]], u.Hp[i]);
        }
        return sum;
    }

    // --- Scouting ----------------------------------------------------------------------------------

    /// <summary>
    /// While it knows of no enemy town hall, a rider (its fastest soldier, if that is at least
    /// <see cref="ScoutSpeed"/> fast) scouts the other start areas, nearest first, and once they have all been
    /// seen, ground it has never seen. With the town hall known, it is looked at again every
    /// <see cref="RescoutTicks"/> while no wave is out, to see the army there. A scout that gets hurt comes
    /// home; one lost puts off the next. The scout is taken out of <paramref name="home"/>.
    /// </summary>
    private void ScoutStep(Simulation sim, Building hall, List<int> home, Action<Command> issue)
    {
        var u = sim.Units;
        long tick = sim.Tick;
        if (_scout != 0 && !u.Exists(_scout))
        {
            _scout = 0;
            _scoutGoal = null;
            _scoutLost++;
            _scoutAt = tick + _scoutLost * RescoutTicks / 2;
        }
        if (_scoutGoal is var (gx, gz))
        {
            int i = u.IndexOf(_scout);
            home.Remove(_scout);
            bool there = Dist2(u.X[i].FloorToInt(), u.Z[i].FloorToInt(), gx, gz) <= 16;
            bool hurt = Hurt(sim, i, ScoutHurtPercent);
            // Given up when it takes too long, or the scout keeps stopping short (there is no way there).
            if (u.Order[i] == UnitOrder.Idle && u.Rout[i] == 0) _scoutStops++;
            if (!there && !hurt && tick - _scoutSent <= ScoutGiveUpTicks && _scoutStops <= 3)
            {
                if (u.Order[i] == UnitOrder.Idle && u.Rout[i] == 0) issue(new MoveCommand(Player, new[] { _scout }, gx, gz));
                return;
            }
            if (!hurt) _scouted.Add((gx, gz));
            _scoutGoal = null;
            if (hurt)
            {
                SendHome(sim, hall, _scout, issue);
                if (!_mending.Contains(_scout)) _mending.Add(_scout);
                _scout = 0;
                _scoutLost++;
                _scoutAt = tick + _scoutLost * RescoutTicks / 2;
                return;
            }
        }

        bool hallKnown = KnowsEnemyHall;
        bool look = tick >= _scoutAt && (!hallKnown || (sim.Fog && _wave.Count == 0));
        var goal = look ? ScoutGoal(sim, hall, hallKnown) : null;
        if (goal != null && (_scout != 0 || (_scout = PickScout(sim, home)) != 0))
        {
            home.Remove(_scout);
            _scoutGoal = goal;
            _scoutSent = tick;
            _scoutStops = 0;
            if (hallKnown) _scoutAt = tick + RescoutTicks;
            issue(new MoveCommand(Player, new[] { _scout }, goal.Value.X, goal.Value.Z));
            return;
        }
        if (_scout != 0)
        {
            // Done: back home, and a soldier like the others again.
            SendHome(sim, hall, _scout, issue);
            _scout = 0;
        }
        if (hallKnown) _scouted.Clear();
    }

    private void SendHome(Simulation sim, Building hall, int id, Action<Command> issue)
    {
        if (!sim.Units.Exists(id)) return;
        var (rx, rz) = Rally(sim, hall, EnemyHall(sim, Middle(hall).X, Middle(hall).Z));
        issue(new MoveCommand(Player, new[] { id }, rx, rz));
    }

    /// <summary>The fastest soldier at home that is not hurt and at least <see cref="ScoutSpeed"/> fast (ties to the lowest id), or 0.</summary>
    private int PickScout(Simulation sim, List<int> home)
    {
        var u = sim.Units;
        int best = 0;
        Fix64 bestSpeed = Fix64.Zero;
        foreach (int id in home)
        {
            int i = u.IndexOf(id);
            if (i < 0 || _mending.Contains(id) || Hurt(sim, i, HealedPercent)) continue;
            var speed = sim.Content.Units[u.Def[i]].Speed;
            if (speed < ScoutSpeed) continue;
            if (best == 0 || speed > bestSpeed) (best, bestSpeed) = (id, speed);
        }
        return best;
    }

    /// <summary>
    /// Where the scout goes next. Looking again at a known town hall: a point short of it on the way from
    /// home. Otherwise the nearest other start area it has not been to, then the nearest point of a
    /// <see cref="ScoutGrid"/> grid it has never seen. Null if there is nowhere left to look.
    /// </summary>
    private (int X, int Z)? ScoutGoal(Simulation sim, Building hall, bool hallKnown)
    {
        var (hx, hz) = Middle(hall);
        var w = sim.World;
        if (hallKnown)
        {
            if (KnownHall(sim, hx, hz) is not { } enemy) return null;
            var (ex, ez) = Middle(enemy);
            return Toward(ex, ez, hx, hz, enemy.Def.Sight + 4, w);
        }
        (int X, int Z)? best = null;
        long bestD = long.MaxValue;
        foreach (var s in sim.Map.Starts)
        {
            if (s.Player == Player) continue;
            // Short of the middle, where a town hall would stand.
            var spot = Toward(s.X, s.Z, hx, hz, 6, w); // by where a town hall would stand
            if (_scouted.Contains(spot) || (sim.Fog && sim.Vision.Sees(Player, s.X, s.Z))) continue;
            long d = Dist2(hx, hz, spot.X, spot.Z);
            if (d < bestD) (bestD, best) = (d, spot);
        }
        if (best != null || GuessHall(sim, hx, hz) != null) return best;
        for (int z = ScoutGrid / 2; z < w.SizeZ; z += ScoutGrid)
        for (int x = ScoutGrid / 2; x < w.SizeX; x += ScoutGrid)
        {
            if (_scouted.Contains((x, z)) || sim.Vision.At(Player, x, z) != Sight.Unseen) continue;
            long d = Dist2(hx, hz, x, z);
            if (d < bestD) (bestD, best) = (d, (x, z));
        }
        return best;
    }

    /// <summary>The point <paramref name="dist"/> columns from (x, z) toward (tx, tz), kept on the map.</summary>
    private static (int X, int Z) Toward(int x, int z, int tx, int tz, int dist, VoxelWorld w)
    {
        long dx = tx - x, dz = tz - z;
        long len = Math.Max(1, IntMath.ISqrt(dx * dx + dz * dz));
        if (len < dist) return (tx, tz);
        int px = x + (int)(dx * dist / len), pz = z + (int)(dz * dist / len);
        return (Math.Clamp(px, 1, w.SizeX - 2), Math.Clamp(pz, 1, w.SizeZ - 2));
    }

    // --- Army --------------------------------------------------------------------------------------

    private void SmartArmy(Simulation sim, Building hall, Action<Command> issue)
    {
        Look(sim);
        SmartTrain(sim, issue);
        Tame(sim, issue);

        var u = sim.Units;
        var (hx, hz) = Middle(hall);
        _wave.RemoveAll(id => !u.Exists(id));
        var home = Soldiers(sim);
        home.RemoveAll(_wave.Contains);
        TotemStep(sim, home, issue);
        home.RemoveAll(_totemParty.Contains);
        Hire(sim, issue);
        Mend(sim, hall, issue);
        if (_waveLair == 0) ScoutStep(sim, hall, home, issue);
        else home.Remove(_scout);

        // Defence: as the classic player, with the fighting done well at Hard.
        int threat = NearestEnemy(sim, hx, hz, DefendRadius, soldiersOnly: false);
        if (threat >= 0)
        {
            int tx = u.X[threat].FloorToInt(), tz = u.Z[threat].FloorToInt();
            if (_wave.Count > 0 && _waveLair == 0 && WaveNear(sim, hx, hz, RecallRadius))
            {
                home.AddRange(_wave);
                EndWave();
            }
            var free = Unordered(sim, home, tx, tz);
            if (free.Count > 0) issue(new AttackMoveCommand(Player, free.ToArray(), tx, tz));
            if (Level == AiLevel.Hard) Micro(sim, home, issue);
            return;
        }

        if (_wave.Count > 0)
        {
            if (_waveLair == 0)
            {
                Withdraw(sim, hall, issue);
                if (_wave.Count == 0)
                {
                    EndWave();
                    return;
                }
                if (Outmatched(sim))
                {
                    var (rx, rz) = Rally(sim, hall, EnemyHall(sim, hx, hz));
                    issue(new MoveCommand(Player, _wave.ToArray(), rx, rz));
                    foreach (int id in _wave)
                        if (Hurt(sim, u.IndexOf(id), HealedPercent) && !_mending.Contains(id)) _mending.Add(id);
                    EndWave();
                    return;
                }
                if (Level == AiLevel.Hard) Micro(sim, _wave, issue);
                if (Marching(sim) || Guessing(sim, hall, issue)) return;
            }
            StepWave(sim, hall, issue);
            return;
        }

        // A wave sets out when it is big enough, or clearly stronger than the enemy army it knows of.
        home.RemoveAll(_mending.Contains);
        var enemyHall = EnemyHall(sim, hx, hz);
        int size = Math.Min(BiggestWave, FirstWave + WaveGrowth * _waves);
        if (sim.Tick >= LateTicks) size = Math.Max(FirstWave / 2, size / 2);
        int guard = Math.Max(LeastGuard, home.Count * GuardPercent / 100);
        int ready = home.Count - guard;
        var goal = enemyHall != null ? Approach(sim, enemyHall) : GuessHall(sim, hx, hz);
        if (goal is var (ex, ez) && sim.Tick >= SmartFirstWaveTicks && ready > 0)
        {
            home.Sort((a, b) =>
            {
                int ia = u.IndexOf(a), ib = u.IndexOf(b);
                int c = Dist2(u.X[ia].FloorToInt(), u.Z[ia].FloorToInt(), ex, ez).CompareTo(Dist2(u.X[ib].FloorToInt(), u.Z[ib].FloorToInt(), ex, ez));
                return c != 0 ? c : a.CompareTo(b);
            });
            var wave = home.GetRange(0, ready);
            long mine = PowerOf(sim, wave), theirs = EnemyPower(enemyHall?.Owner ?? -1);
            bool fresh = enemyHall != null && sim.Tick - _hallSeen <= SpotMemoryTicks;
            if ((ready >= size && mine * 4 >= theirs * 3) || (ready >= LeastWave && fresh && mine > 2 * theirs))
            {
                Launch(sim, wave, (ex, ez), enemyHall?.Id ?? 0, issue);
                return;
            }
        }
        if (sim.Tick % (5 * ArmyTicks) < ArmyTicks) RallyHome(sim, hall, enemyHall, home, issue);
    }

    /// <summary>
    /// The totem party: it attack-moves into the town to fight the guards, and once none stand its idle
    /// soldiers attack the totem; it ends when the town is ours, when it is wiped out, or after <see cref="TotemRetryTicks"/>. With
    /// none out, no wave out and enough soldiers home, a new one sets out for the nearest town we do not hold.
    /// </summary>
    private void TotemStep(Simulation sim, List<int> home, Action<Command> issue)
    {
        var u = sim.Units;
        _totemParty.RemoveAll(id => !u.Exists(id));
        var cities = sim.Cities;
        if (_totemParty.Count > 0)
        {
            var town = cities.Get(_totemTown);
            if (town == null || town.Holder == Player || sim.Tick - _totemSent > TotemRetryTicks)
            {
                _totemParty.Clear();
                _totemAt = sim.Tick + (town?.Holder == Player ? 0 : TotemRetryTicks);
                return;
            }
            var idle = Unordered(sim, _totemParty, town.X, town.Z);
            if (idle.Count == 0) return;
            if (GuardsStand(sim, town)) issue(new AttackMoveCommand(Player, idle.ToArray(), town.X, town.Z + CityStateSystem.TradeSpotOffset + 1));
            else if (cities.Totem(town) is { } totem) issue(new AttackBuildingCommand(Player, idle.ToArray(), totem.Id));
            return;
        }
        if (sim.Tick < _totemAt || _wave.Count > 0) return;
        var hall = sim.Economy.TownHall(Player);
        if (hall == null) return;
        var (hx, hz) = Middle(hall);
        CityState? best = null;
        long bestD = long.MaxValue;
        foreach (var town in cities.Towns)
        {
            if (town.Holder == Player || cities.Totem(town) == null) continue;
            long d = Dist2(hx, hz, town.X, town.Z);
            if (d < bestD) (bestD, best) = (d, town);
        }
        var ready = new List<int>(home);
        ready.RemoveAll(_mending.Contains);
        if (best == null || ready.Count < TotemParty + LeastGuard) return;
        ready.Sort((a, b) =>
        {
            int ia = u.IndexOf(a), ib = u.IndexOf(b);
            int c = Dist2(u.X[ia].FloorToInt(), u.Z[ia].FloorToInt(), best.X, best.Z).CompareTo(Dist2(u.X[ib].FloorToInt(), u.Z[ib].FloorToInt(), best.X, best.Z));
            return c != 0 ? c : a.CompareTo(b);
        });
        _totemParty.AddRange(ready.GetRange(0, TotemParty));
        _totemTown = best.Id;
        _totemSent = sim.Tick;
        issue(new AttackMoveCommand(Player, _totemParty.ToArray(), best.X, best.Z + CityStateSystem.TradeSpotOffset + 1));
    }

    /// <summary>True if any of the town's guards still stands against us: the party fights them before it breaks the totem.</summary>
    private bool GuardsStand(Simulation sim, CityState town)
    {
        foreach (int id in town.Guards)
        {
            int i = sim.Units.IndexOf(id);
            if (i >= 0 && sim.Units.Owner[i] != Player) return true;
        }
        return false;
    }

    /// <summary>Holding a city-state's town, it hires one of its mercenaries when one is ready and it can pay and house it.</summary>
    private void Hire(Simulation sim, Action<Command> issue)
    {
        foreach (var town in sim.Cities.Towns)
        {
            if (town.Holder != Player || town.Ready == 0 || !sim.Cities.CanHire(Player)) continue;
            foreach (var m in town.Def.Mercenaries)
            {
                if (!Affordable(sim, m.Cost)) continue;
                issue(new HireCommand(Player, town.Id, m.Id));
                return;
            }
        }
    }

    /// <summary>
    /// Sends the wave: in formation (melee in front, ranged behind) to a line across the way
    /// <see cref="StageDistance"/> columns short of the target, from where it attacks (see <see cref="Marching"/>).
    /// </summary>
    private void Launch(Simulation sim, List<int> wave, (int X, int Z) goal, int building, Action<Command> issue)
    {
        _wave.AddRange(wave);
        _waveBuilding = building;
        _waveGuess = building == 0 ? goal : null;
        _waveLair = 0;
        _waveSize = _wave.Count;
        _waveSent = sim.Tick;
        _waveBest = int.MaxValue;
        _waves++;
        var (ex, ez) = goal;
        var (cx, cz) = Centroid(sim, _wave);
        var w = sim.World;
        long dx = cx - ex, dz = cz - ez;
        long len = IntMath.ISqrt(dx * dx + dz * dz);
        if (len <= StageDistance + 8)
        {
            issue(new AttackMoveCommand(Player, _wave.ToArray(), ex, ez));
            return;
        }
        var (sx, sz) = Toward(ex, ez, cx, cz, StageDistance, w);
        long half = Math.Max(3, _wave.Count / 4);
        int x1 = Math.Clamp(sx - (int)(dz * half / len), 1, w.SizeX - 2), z1 = Math.Clamp(sz + (int)(dx * half / len), 1, w.SizeZ - 2);
        int x2 = Math.Clamp(sx + (int)(dz * half / len), 1, w.SizeX - 2), z2 = Math.Clamp(sz - (int)(dx * half / len), 1, w.SizeZ - 2);
        _stage = (sx, sz);
        _marchStart = sim.Tick;
        if (x1 == x2 && z1 == z2) issue(new AttackMoveCommand(Player, _wave.ToArray(), sx, sz));
        else issue(new FormationMoveCommand(Player, _wave.ToArray(), x1, z1, x2, z2, Attack: true));
    }

    /// <summary>
    /// True while the wave marches to its staging point: until its middle gets there, it meets enemy
    /// soldiers, or <see cref="MarchTicks"/> pass. Then the classic wave takes over and attacks.
    /// </summary>
    private bool Marching(Simulation sim)
    {
        if (_stage is not var (sx, sz)) return false;
        var (cx, cz) = Centroid(sim, _wave);
        if (Dist2(cx, cz, sx, sz) <= 64 || sim.Tick - _marchStart > MarchTicks
            || NearestEnemy(sim, cx, cz, 12, soldiersOnly: true) >= 0)
        {
            _stage = null;
            return false;
        }
        return true;
    }

    /// <summary>
    /// A wave sent where it guesses the enemy town hall is: once it knows where a town hall is, it goes for it
    /// (the classic wave from there); if the guess was wrong it tries the next start area, or comes home.
    /// False once it has a building to go for.
    /// </summary>
    private bool Guessing(Simulation sim, Building hall, Action<Command> issue)
    {
        if (_waveGuess is not var (gx, gz)) return false;
        var (cx, cz) = Centroid(sim, _wave);
        if (KnownHall(sim, cx, cz) is { } found)
        {
            _waveBuilding = found.Id;
            _waveGuess = null;
            _waveBest = int.MaxValue;
            return false;
        }
        var (hx, hz) = Middle(hall);
        var guess = GuessHall(sim, hx, hz);
        if (guess != _waveGuess)
        {
            _waveGuess = guess;
            if (guess is not var (nx, nz))
            {
                var (rx, rz) = Rally(sim, hall, null);
                issue(new MoveCommand(Player, _wave.ToArray(), rx, rz));
                EndWave();
                return true;
            }
            issue(new AttackMoveCommand(Player, _wave.ToArray(), nx, nz));
            return true;
        }
        if (sim.Tick % (5 * ArmyTicks) < ArmyTicks)
        {
            var again = Unordered(sim, _wave, gx, gz);
            if (again.Count > 0) issue(new AttackMoveCommand(Player, again.ToArray(), gx, gz));
        }
        return true;
    }

    /// <summary>Soldiers at home not at the rally point go there (as the classic player); the mending go by the town hall.</summary>
    private void RallyHome(Simulation sim, Building hall, Building? enemyHall, List<int> home, Action<Command> issue)
    {
        var u = sim.Units;
        var rally = Rally(sim, hall, enemyHall);
        if (_rally != rally || _rallied.Count > 4 * home.Count + 16)
        {
            _rally = rally;
            _rallied.Clear();
        }
        var (rx, rz) = rally;
        _scratch.Clear();
        foreach (int id in Unordered(sim, home, rx, rz))
        {
            int i = u.IndexOf(id);
            if (Dist2(u.X[i].FloorToInt(), u.Z[i].FloorToInt(), rx, rz) <= (long)Slack * Slack) continue;
            if (_rallied.TryGetValue(id, out long sent) && sim.Tick - sent < RallyAgainTicks) continue;
            _scratch.Add(id);
            _rallied[id] = sim.Tick;
        }
        if (_scratch.Count > 0) issue(new MoveCommand(Player, _scratch.ToArray(), rx, rz));
    }

    /// <summary>Wounded soldiers of the wave (below <see cref="WoundedPercent"/>) leave it and go home to heal.</summary>
    private void Withdraw(Simulation sim, Building hall, Action<Command> issue)
    {
        var u = sim.Units;
        _scratch.Clear();
        foreach (int id in _wave)
        {
            int i = u.IndexOf(id);
            if (i < 0 || u.Rout[i] > 0 || !Hurt(sim, i, WoundedPercent)) continue;
            _scratch.Add(id);
        }
        if (_scratch.Count == 0) return;
        var (dx, dz) = Approach(sim, hall);
        issue(new MoveCommand(Player, _scratch.ToArray(), dx, dz));
        foreach (int id in _scratch)
        {
            _wave.Remove(id);
            if (!_mending.Contains(id)) _mending.Add(id);
        }
    }

    /// <summary>
    /// Soldiers home to heal: the healed (and the dead) are let go; the rest who are standing about away from
    /// the town hall are sent to its door, where they heal fastest.
    /// </summary>
    private void Mend(Simulation sim, Building hall, Action<Command> issue)
    {
        var u = sim.Units;
        _mending.RemoveAll(id => u.IndexOf(id) is var i && (i < 0 || !Hurt(sim, i, HealedPercent)));
        if (_mending.Count == 0 || sim.Tick % (5 * ArmyTicks) >= ArmyTicks) return;
        var (dx, dz) = Approach(sim, hall);
        _scratch.Clear();
        foreach (int id in _mending)
        {
            int i = u.IndexOf(id);
            if (u.Order[i] != UnitOrder.Idle || u.Target[i] != 0 || u.Rout[i] > 0) continue;
            if (Dist2(u.X[i].FloorToInt(), u.Z[i].FloorToInt(), dx, dz) <= 36) continue;
            _scratch.Add(id);
        }
        if (_scratch.Count > 0) issue(new MoveCommand(Player, _scratch.ToArray(), dx, dz));
    }

    /// <summary>True if the enemy soldiers it sees around the wave are much stronger than it (see <see cref="OutmatchedPercent"/>).</summary>
    private bool Outmatched(Simulation sim)
    {
        var u = sim.Units;
        var (cx, cz) = Centroid(sim, _wave);
        long theirs = 0;
        int n = 0;
        bool engaged = false;
        long r2 = (long)OutmatchedRadius * OutmatchedRadius;
        for (int i = 0; i < u.Count; i++)
        {
            int owner = u.Owner[i];
            if (owner == Player || owner == Simulation.WildPlayer || u.Subdued[i]) continue;
            var def = sim.Content.Units[u.Def[i]];
            if (!def.CanAttack || def.Role is UnitRole.Worker or UnitRole.Trader) continue;
            if (Dist2(u.X[i].FloorToInt(), u.Z[i].FloorToInt(), cx, cz) > r2 || !sim.Vision.SeesUnit(Player, i)) continue;
            theirs += Power(def, u.Hp[i]);
            n++;
            if (Dist2(u.X[i].FloorToInt(), u.Z[i].FloorToInt(), cx, cz) <= (long)EngagedRadius * EngagedRadius) engaged = true;
        }
        return n >= 3 && !engaged && theirs * 100 > PowerOf(sim, _wave) * OutmatchedPercent;
    }

    /// <summary>
    /// Hard: the fighting. Ranged troops and casters shoot the enemy soldier in reach they are strong against,
    /// else the one with the fewest hit points, so they bring enemies down one at a time; melee troops that
    /// are not on an enemy they are strong against go for one nearby that they are (riders for archers and
    /// mages, spearmen for riders).
    /// </summary>
    private void Micro(Simulation sim, List<int> ids, Action<Command> issue)
    {
        if (sim.Tick % (2 * ArmyTicks) >= ArmyTicks) return;
        var u = sim.Units;
        var combat = sim.Content.Combat;
        var (cx, cz) = Centroid(sim, ids);
        var enemies = new List<int>();
        long r2 = 24L * 24;
        for (int i = 0; i < u.Count; i++)
        {
            int owner = u.Owner[i];
            if (owner == Player || owner == Simulation.WildPlayer || u.Subdued[i]) continue;
            var def = sim.Content.Units[u.Def[i]];
            if (!def.CanAttack || def.Role is UnitRole.Worker or UnitRole.Trader) continue;
            if (Dist2(u.X[i].FloorToInt(), u.Z[i].FloorToInt(), cx, cz) > r2 || !sim.Vision.SeesUnit(Player, i)) continue;
            enemies.Add(i);
        }
        if (enemies.Count == 0) return;
        var orders = new SortedDictionary<int, List<int>>();
        foreach (int id in ids)
        {
            int i = u.IndexOf(id);
            if (i < 0 || u.Rout[i] > 0) continue;
            var def = sim.Content.Units[u.Def[i]];
            if (def.Role is UnitRole.Sapper or UnitRole.Siege) continue;
            int ux = u.X[i].FloorToInt(), uz = u.Z[i].FloorToInt();
            long reach = def.IsRanged ? def.Range.FloorToInt() + 2 : 6;
            int best = -1;
            bool bestStrong = false;
            long bestHp = 0, bestD = 0;
            foreach (int e in enemies)
            {
                long d = Dist2(ux, uz, u.X[e].FloorToInt(), u.Z[e].FloorToInt());
                if (d > reach * reach) continue;
                var role = sim.Content.Units[u.Def[e]].Role;
                bool strong = combat.StrongAgainst(def.Role, role);
                long hp = u.Hp[e];
                if (best >= 0 && (strong != bestStrong ? !strong : hp != bestHp ? hp > bestHp : d >= bestD)) continue;
                (best, bestStrong, bestHp, bestD) = (e, strong, hp, d);
            }
            if (best < 0) continue;
            int targetId = u.Id[best];
            int current = u.Target[i];
            if (current == targetId) continue;
            int ci = current != 0 ? u.IndexOf(current) : -1;
            if (ci >= 0)
            {
                bool currentStrong = combat.StrongAgainst(def.Role, sim.Content.Units[u.Def[ci]].Role);
                if (!def.IsRanged && (currentStrong || !bestStrong)) continue; // melee: only switch onto a counter
                if (def.IsRanged && (currentStrong && !bestStrong || (currentStrong == bestStrong && u.Hp[ci] * 2 <= bestHp * 3))) continue;
            }
            else if (!def.IsRanged && !bestStrong) continue; // melee with no target fights what it meets
            if (!orders.TryGetValue(targetId, out var list)) orders[targetId] = list = new List<int>();
            list.Add(id);
        }
        foreach (var (target, list) in orders) issue(new AttackCommand(Player, list.ToArray(), target));
    }

    /// <summary>
    /// Keeps each barracks training, picked at random by weight: the classic weights, plus a lot for roles
    /// strong against the enemy soldiers it has seen and less for roles they are strong against. Sappers
    /// (a few for each wave) and siege (two) are trained only up to a number.
    /// </summary>
    private void SmartTrain(Simulation sim, Action<Command> issue)
    {
        Keep(sim);
        var (seen, mine) = TrainCounts(sim);
        foreach (var b in sim.Buildings.All)
        {
            if (b.Owner != Player || !b.Active || b.Def.Kind != BuildingKind.Barracks || b.Queue.Count >= 2) continue;
            long total = 0;
            foreach (string id in b.Def.Trains)
                if (sim.Content.TryGetUnit(id, out var d) && sim.Economy.Unlocked(Player, d)) total += Weight(sim, d.Role, seen, mine);
            if (total == 0) continue;
            long roll = _rng.Range(0, (int)Math.Min(total, int.MaxValue));
            foreach (string id in b.Def.Trains)
            {
                if (!sim.Content.TryGetUnit(id, out var d) || !sim.Economy.Unlocked(Player, d)) continue;
                roll -= Weight(sim, d.Role, seen, mine);
                if (roll >= 0) continue;
                if (Affordable(sim, d.Cost))
                {
                    issue(new TrainCommand(Player, b.Id, d.Id));
                    mine[(int)d.Role]++;
                }
                break;
            }
        }
    }

    /// <summary>
    /// How likely (a weight; see <see cref="SmartTrain"/>) a barracks of a smart level is to train a unit of
    /// this type next, from the enemy soldiers it remembers and its own army as they are now.
    /// </summary>
    public long TrainWeight(Simulation sim, UnitDef def)
    {
        var (seen, mine) = TrainCounts(sim);
        return Weight(sim, def.Role, seen, mine);
    }

    /// <summary>Enemy soldiers it remembers, and its own units (with those queued at barracks), by role.</summary>
    private (int[] Seen, int[] Mine) TrainCounts(Simulation sim)
    {
        int roles = Enum.GetValues<UnitRole>().Length;
        var seen = new int[roles];
        foreach (var s in _spotted.Values) seen[(int)s.Role]++;
        var mine = new int[roles];
        var u = sim.Units;
        for (int i = 0; i < u.Count; i++)
            if (u.Owner[i] == Player && u.Town[i] == 0) mine[(int)sim.Content.Units[u.Def[i]].Role]++;
        foreach (var b in sim.Buildings.All)
        {
            if (b.Owner != Player || b.Def.Kind != BuildingKind.Barracks) continue;
            foreach (int q in b.Queue) mine[(int)sim.Content.Units[q].Role]++;
        }
        return (seen, mine);
    }

    private long Weight(Simulation sim, UnitRole r, int[] seen, int[] mine)
    {
        int baseWeight = RoleWeight(r);
        if (baseWeight == 0) return 0;
        if (r == UnitRole.Sapper) return mine[(int)r] < Math.Min(6, 2 + _waves / 2) ? 6 : 0;
        if (r == UnitRole.Siege) return mine[(int)r] < 2 ? 2 : 0;
        var combat = sim.Content.Combat;
        long w = baseWeight * 4L;
        for (int e = 0; e < seen.Length; e++)
        {
            if (seen[e] == 0) continue;
            if (combat.StrongAgainst(r, (UnitRole)e)) w += 6L * seen[e];
            if (combat.StrongAgainst((UnitRole)e, r)) w -= 4L * seen[e];
        }
        return Math.Max(1, w);
    }

    // --- Economy -----------------------------------------------------------------------------------

    /// <summary>Hard: a second town hall on new ground, and a wall round the first.</summary>
    private void SmartEconomy(Simulation sim, Building hall, Action<Command> issue)
    {
        if (Level != AiLevel.Hard) return;
        long tick = sim.Tick;
        if (tick >= ExpandTicks) Expand(sim, hall, issue);
        if (!_walled && tick >= WallTicks) Wall(sim, hall, issue);
    }

    /// <summary>
    /// A second town hall, when it can pay for it, by where its workers gather far from storage (or by the
    /// trees it sends cutters to), nearer its own town hall than any enemy's: more storage near the work, and
    /// the match is not lost with the first one.
    /// </summary>
    private void Expand(Simulation sim, Building hall, Action<Command> issue)
    {
        int halls = 0;
        foreach (var b in sim.Buildings.All)
            if (b.Owner == Player && b.Def.Kind == BuildingKind.TownHall) halls++;
        if (halls >= 2) return;
        var def = FirstBuilding(sim, BuildingKind.TownHall);
        if (def == null || !Affordable(sim, def.Cost)) return;
        var (hx, hz) = Middle(hall);
        (int X, int Z) at;
        if (FarGathering(sim) is var (fx, fz, _)) at = (fx, fz);
        else if (_forest is var (tx, _, tz)) at = (tx, tz);
        else return;
        if (Dist2(hx, hz, at.X, at.Z) < 30L * 30) return;
        if (EnemyHall(sim, hx, hz) is { } enemy && Dist2(Middle(enemy).X, Middle(enemy).Z, at.X, at.Z) <= Dist2(hx, hz, at.X, at.Z)) return;
        PlaceNear(sim, def, at, 16, issue);
    }

    private static int Price(BuildingDef def, Good good)
    {
        foreach (var c in def.Cost)
            if (c.Good == good) return c.Amount;
        return 0;
    }

    /// <summary>
    /// A stone wall round the town hall, <see cref="WallGap"/> columns out, with gates in the middle of the
    /// door side and the far side, once it has the stone. Its own units walk through the gates; enemies have
    /// to break through while its soldiers fight them.
    /// </summary>
    private void Wall(Simulation sim, Building hall, Action<Command> issue)
    {
        if (!sim.Content.TryGetBuilding("wall", out var wall) || !sim.Content.TryGetBuilding("gate", out var gate)) return;
        if (!sim.Economy.Unlocked(Player, wall) || !sim.Economy.Unlocked(Player, gate)) return;
        int x0 = hall.X - WallGap - 1, z0 = hall.Z - WallGap - 1, x1 = hall.MaxX + WallGap + 1, z1 = hall.MaxZ + WallGap + 1;
        var w = sim.World;
        if (x0 < 1 || z0 < 1 || x1 >= w.SizeX - 1 || z1 >= w.SizeZ - 1) return;
        // Paid for all at once, gates first: a wall with no way through would shut its own units out.
        int columns = 2 * (x1 - x0 + z1 - z0);
        var stock = sim.Economy.Stockpile(Player);
        foreach (var good in new[] { Good.Stone, Good.Wood })
        {
            int need = 6 * Price(gate, good) + columns * Price(wall, good) + _keep[(int)good];
            if (stock[(int)good] < need + (good == Good.Stone ? 50 : 0)) return;
        }
        _walled = true;
        int mx = (x0 + x1) / 2;
        string wid = wall.Id, gid = gate.Id;
        var none = Array.Empty<int>();
        // Door side (+z) and far side (-z): wall, a gate three wide in the middle, wall.
        foreach (int z in new[] { z1, z0 }) issue(new WallCommand(Player, none, gid, mx - 1, z, mx + 1, z));
        foreach (int z in new[] { z1, z0 })
        {
            issue(new WallCommand(Player, none, wid, x0, z, mx - 2, z));
            issue(new WallCommand(Player, none, wid, mx + 2, z, x1, z));
        }
        issue(new WallCommand(Player, none, wid, x0, z0 + 1, x0, z1 - 1));
        issue(new WallCommand(Player, none, wid, x1, z0 + 1, x1, z1 - 1));
    }
}
