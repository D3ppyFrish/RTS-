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
    /// Plays only by what it has seen: it scouts, remembers what it has seen, trains counters to the
    /// soldiers it has seen, sizes its waves to the enemy army it knows of, marches in formation (ranged
    /// troops behind), pulls wounded soldiers home to heal, falls back from a fight it is losing, and its
    /// sappers go for buildings.
    /// </summary>
    Easy,
    /// <summary>
    /// As <see cref="Easy"/>, and also: focus fire and counter-targeting in fights, more workers, a third
    /// barracks when rich, a second town hall on new ground, and a stone wall with gates round its town hall.
    /// This was the best level before <see cref="Hard"/> (issue #2), kept as it was to measure Hard against.
    /// </summary>
    Normal,
    /// <summary>
    /// The best it can play, still only by what it has seen and with only the goods it gathers (see
    /// <c>AiPlayer.Hard.cs</c>): an economy that keeps growing, an army sized and mixed against the one it
    /// has seen, fights it picks, a quarry and storehouses by its work, and a town hall on new ground.
    /// </summary>
    Hard,
}

// The smart levels (Easy and up; issue #47). The economy is the classic one with a few additions
// (SmartEconomy); the army is run by SmartArmy instead of Army.
public sealed partial class AiPlayer
{
    /// <summary>Most workers at <see cref="AiLevel.Normal"/>.</summary>
    public const int SkilledMostWorkers = 36;
    /// <summary>Second barracks at the smart levels from this tick; a third (Normal, when rich) from this one.</summary>
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
    /// <summary>Hard's scout comes home below this share (percent) of its hit points: a rider is worth more than a longer look.</summary>
    public const int HardScoutHurtPercent = 85;
    /// <summary>Scouting goals off the start areas are on a grid this many columns apart.</summary>
    public const int ScoutGrid = 16;
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
    /// <summary>Normal: a second town hall from this tick on.</summary>
    public const int ExpandTicks = 10 * 60 * Tps;
    /// <summary>Normal: a wall round the town hall from this tick on, this many columns out from its walls.</summary>
    public const int WallTicks = 9 * 60 * Tps, WallGap = 4;
    /// <summary>
    /// Normal: ticks between looks at whether it can upgrade its wall, and what it wants left over of each
    /// good the upgrade takes after paying for all of it.
    /// </summary>
    public const int WallUpgradeTicks = 60 * Tps, WallUpgradeSpare = 100;
    /// <summary>
    /// The smart levels send a party of this many soldiers to break the totem of the nearest city-state town
    /// they do not hold, from this tick on, while no wave is out and enough soldiers are home; a party that
    /// fails is tried again this long after (ticks), and one that has not won in that long comes home.
    /// </summary>
    public const int TotemParty = 6, TotemTicks = 6 * 60 * Tps, TotemRetryTicks = 3 * 60 * Tps;

    /// <summary>The soldiers sent to break a city-state's totem, by id.</summary>
    public IReadOnlyList<int> TotemPartyIds => _totemParty;

    // What it knows of other players' buildings (by id): seen, and not seen gone since.
    private readonly SortedDictionary<int, Foe> _known = new();
    // Other players' soldiers it has seen (by id), where and when.
    private readonly SortedDictionary<int, Sighting> _spotted = new();
    // When it last looked (see Look).
    private long _looked = long.MinValue;
    // Where the starting soldiers look for a lair they have not seen (see FindLair).
    private (int X, int Z) _lairSearch;
    private readonly List<int> _forget = new();
    // The scout (unit id, 0 if none), where it is going, since when, where it has been, and when it may go again.
    private int _scout;
    private (int X, int Z)? _scoutGoal;
    // True while the scout is looking for their town (its goal is done once seen), false on a look at a town it knows of.
    private bool _scoutSearch;
    private long _scoutSent;
    private int _scoutStops, _scoutLost;
    private long _scoutAt;
    // Where a wave with nothing it knows of to go for is looking for the enemy (see Seeking), and since when
    // it has been looking, and on its way to this place.
    private (int X, int Z)? _waveGuess;
    private long _seekStart, _seekSent;
    // How many times most of that wave has been found standing about short of the place (see LookFrom).
    private int _seekStops;
    // Where it last saw an enemy building: the search starts from there.
    private (int X, int Z)? _lastFoe;
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
    // Normal: the wall round the town hall has been laid out.
    private bool _walled;
    private long _wallUpgradeAt;
    // The party sent to break a city-state's totem (unit ids), the town, when it set out, and when the next may.
    private readonly List<int> _totemParty = new();
    private int _totemTown;
    private long _totemSent, _totemAt = TotemTicks;

    private readonly record struct Sighting(int Owner, UnitRole Role, int X, int Z, long Tick, long Power);

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

    /// <summary>Other players' buildings it knows of, by id: seen, and not seen gone since.</summary>
    public IEnumerable<int> KnownBuildings => _known.Keys;

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

    /// <summary>
    /// Updates what it knows of other players' buildings and soldiers from what it sees now. A building is
    /// remembered as it last saw it until it sees the place without it; a soldier where it last saw it, for
    /// <see cref="SpotMemoryTicks"/> or until it sees it fall. Nothing out of its sight changes what it knows.
    /// </summary>
    private void Look(Simulation sim)
    {
        var vision = sim.Vision;
        long tick = sim.Tick, before = _looked;
        _looked = tick;
        bool inMatch = sim.Match.Players.Count > 0;
        foreach (var b in sim.Buildings.All)
        {
            if (b.Owner == Player || b.Owner == Simulation.WildPlayer || b.Planned) continue; // others' plans are not seen
            var (mx, mz) = Middle(b);
            if (!vision.Sees(Player, mx, mz) && !vision.Sees(Player, b.X, b.Z) && !vision.Sees(Player, b.MaxX, b.Z)
                && !vision.Sees(Player, b.X, b.MaxZ) && !vision.Sees(Player, b.MaxX, b.MaxZ)) continue;
            if (!_known.ContainsKey(b.Id))
                Note(sim, b.Def.Kind == BuildingKind.TownHall ? $"finds an enemy town hall at {mx},{mz}" : $"sees an enemy {b.Def.Name} at {mx},{mz}");
            _known[b.Id] = AsFoe(sim, b);
            _lastFoe = (mx, mz);
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
            _spotted[u.Id[i]] = new Sighting(owner, def.Role, u.X[i].FloorToInt(), u.Z[i].FloorToInt(), tick, Power(def, u.Hp[i]));
        }
        _forget.Clear();
        foreach (var (id, s) in _spotted)
        {
            // Forgotten after a while, or at once when it saw it fall: in sight at the last look, gone now.
            if (tick - s.Tick > SpotMemoryTicks || (s.Tick == before && u.IndexOf(id) < 0)) _forget.Add(id);
        }
        foreach (int id in _forget) _spotted.Remove(id);
    }

    /// <summary>The nearest enemy town hall it knows of (finished when it last saw it), or null.</summary>
    private Foe? KnownHall(int x, int z)
    {
        Foe? best = null;
        long bestD = long.MaxValue;
        foreach (var k in _known.Values)
        {
            if (k.Kind != BuildingKind.TownHall || !k.Active) continue;
            long d = Dist2(k.X, k.Z, x, z);
            if (d < bestD) (bestD, best) = (d, k);
        }
        return best;
    }

    /// <summary>The nearest enemy building it knows of, or null.</summary>
    private Foe? KnownBuilding(int x, int z)
    {
        Foe? best = null;
        long bestD = long.MaxValue;
        foreach (var k in _known.Values)
        {
            long d = Dist2(k.X, k.Z, x, z);
            if (d < bestD) (bestD, best) = (d, k);
        }
        return best;
    }

    /// <summary>True if it has seen the lair's den (the column it is on).</summary>
    private bool LairKnown(Simulation sim, Lair lair) => sim.Vision.At(Player, lair.X, lair.Z) != Sight.Unseen;

    /// <summary>The wave is the starting soldiers, out looking for a lair they have not seen yet (see <see cref="FindLair"/>).</summary>
    private const int LairSearch = -1;

    /// <summary>A search for a lair is given up after this many ticks.</summary>
    public const int LairSearchTicks = 75 * Tps;

    /// <summary>
    /// The starting soldiers looking for a lair: once they see one they go for it (true: the wave has a lair
    /// to clear); if they get where they were looking, or run out of time, without seeing one they come home.
    /// </summary>
    private bool FindLair(Simulation sim, Building hall, Action<Command> issue)
    {
        var (hx, hz) = Middle(hall);
        if (NearestLair(sim, hx, hz, PowerOf(sim, _wave)) is { } lair)
        {
            _waveLair = lair.Id;
            issue(new AttackMoveCommand(Player, _wave.ToArray(), lair.X, lair.Z));
            return true;
        }
        var (cx, cz) = Centroid(sim, _wave);
        var (gx, gz) = _lairSearch;
        if (Dist2(cx, cz, gx, gz) > 36 && sim.Tick - _waveSent <= LairSearchTicks)
        {
            if (sim.Tick % (10 * ArmyTicks) < ArmyTicks)
            {
                var idle = Unordered(sim, _wave, gx, gz);
                if (idle.Count > 0) issue(new AttackMoveCommand(Player, idle.ToArray(), gx, gz));
            }
            return false;
        }
        var (rx, rz) = RallyAt(sim, hall, null);
        issue(new MoveCommand(Player, _wave.ToArray(), rx, rz));
        EndWave();
        return false;
    }

    /// <summary>The worth (see <see cref="Power"/>) of the soldiers of a player (-1: of everyone else) it remembers seeing.</summary>
    private long EnemyPower(int owner)
    {
        long sum = 0;
        foreach (var s in _spotted.Values)
            if (owner < 0 || s.Owner == owner) sum += s.Power;
        return sum;
    }

    /// <summary>Guesses at where an enemy town hall stands are this far (columns) from its own town hall and from each other.</summary>
    public const int GuessApart = 64;

    /// <summary>
    /// Where an enemy town hall may stand, going only by the shape of the map: players start far apart, so
    /// the places across the map from its own town hall, through the middle first, then straight across
    /// each way. (It is not told where anyone starts.)
    /// </summary>
    private List<(int X, int Z)> HallGuesses(Simulation sim, int hx, int hz)
    {
        var w = sim.World;
        int mx = w.SizeX - 1 - hx, mz = w.SizeZ - 1 - hz;
        var list = new List<(int X, int Z)>(3);
        foreach (var g in new[] { (mx, mz), (mx, hz), (hx, mz) })
        {
            bool apart = Dist2(g.Item1, g.Item2, hx, hz) >= (long)GuessApart * GuessApart;
            foreach (var o in list) apart &= Dist2(g.Item1, g.Item2, o.X, o.Z) >= (long)GuessApart * GuessApart;
            if (apart) list.Add(g);
        }
        return list;
    }

    /// <summary>
    /// A place it looks for the enemy at may be one nobody can walk to (a move order to a column with a
    /// building on it ends on its roof, which there is no way up to), so its soldiers are sent to a point
    /// <see cref="LookShort"/> columns short of it, from where they see it, and each time they stop without
    /// getting there, <see cref="LookBack"/> columns further back.
    /// </summary>
    public const int LookShort = 5, LookBack = 4;

    /// <summary>
    /// Where soldiers at (x, z) are sent to look at the place (gx, gz): short of it (see <see cref="LookShort"/>)
    /// if it is enough to see it, and further back for each time they have stopped on the way.
    /// </summary>
    private static (int X, int Z) LookFrom(Simulation sim, int gx, int gz, int x, int z, bool seeing = true, int stops = 0) =>
        Toward(gx, gz, x, z, (seeing ? LookShort : 0) + LookBack * stops, sim.World);

    /// <summary>
    /// Where to look for the enemy while it knows of none of their buildings, coming from (x, z): the first
    /// of its guesses at where an enemy town hall stands (see <see cref="HallGuesses"/>) that it has not
    /// seen or given up on; then, a start area being wider than a soldier sees, the ground round where it
    /// last saw an enemy building (or round its first guess): the point of a <see cref="ScoutGrid"/> grid
    /// it has never seen that is nearest that and nearest (x, z). Null if it has seen the whole map.
    /// </summary>
    private (int X, int Z)? SearchGoal(Simulation sim, int hx, int hz, int x, int z)
    {
        var w = sim.World;
        var guesses = HallGuesses(sim, hx, hz);
        foreach (var g in guesses)
            if (!_scouted.Contains(g) && sim.Vision.At(Player, g.X, g.Z) == Sight.Unseen) return g;
        var near = _lastFoe ?? (guesses.Count > 0 ? guesses[0] : (hx, hz));
        (int X, int Z)? best = null;
        long bestD = long.MaxValue;
        for (int gz = ScoutGrid / 2; gz < w.SizeZ; gz += ScoutGrid)
        for (int gx = ScoutGrid / 2; gx < w.SizeX; gx += ScoutGrid)
        {
            if (_scouted.Contains((gx, gz)) || sim.Vision.At(Player, gx, gz) != Sight.Unseen) continue;
            long d = 2 * Dist2(near.X, near.Z, gx, gz) + Dist2(x, z, gx, gz);
            if (d < bestD) (bestD, best) = (d, (gx, gz));
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
    /// home; one lost puts off the next. The scout is taken out of <paramref name="home"/>. (Hard scouts
    /// when it means to attack, and while a wave of its own is looking for the enemy: see below.)
    /// </summary>
    private void ScoutStep(Simulation sim, Building hall, List<int> home, Action<Command> issue)
    {
        var u = sim.Units;
        long tick = sim.Tick;
        if (_scout != 0 && !u.Exists(_scout))
        {
            Note(sim, "loses its scout");
            _lookDone = tick;
            _scout = 0;
            _scoutGoal = null;
            _scoutLost++;
            _scoutAt = tick + ScoutWait;
        }
        if (_scoutGoal != null && ShutIn(sim, u.IndexOf(_scout)))
        {
            // Shut in where it stands (see ShutIn): it is no scout, and another may go.
            _scout = 0;
            _scoutGoal = null;
        }
        if (_scoutGoal is var (gx, gz))
        {
            int i = u.IndexOf(_scout);
            home.Remove(_scout);
            int ux = u.X[i].FloorToInt(), uz = u.Z[i].FloorToInt();
            // Looking for their town, a place is done once it has been seen (or the town found); looking at it again, once the scout is there.
            bool there = Dist2(ux, uz, gx, gz) <= 16 || (_scoutSearch && (KnowsEnemyHall || sim.Vision.At(Player, gx, gz) != Sight.Unseen));
            bool hurt = Hurt(sim, i, Hard ? HardScoutHurtPercent : ScoutHurtPercent);
            // Given up when it takes too long, or the scout keeps stopping short (there is no way there).
            if (u.Order[i] == UnitOrder.Idle && u.Rout[i] == 0) _scoutStops++;
            if (!there && !hurt && tick - _scoutSent <= ScoutGiveUpTicks && _scoutStops <= 3)
            {
                if (u.Order[i] == UnitOrder.Idle && u.Rout[i] == 0)
                {
                    var (mx, mz) = LookFrom(sim, gx, gz, ux, uz, _scoutSearch, _scoutStops);
                    issue(new MoveCommand(Player, new[] { _scout }, mx, mz));
                }
                return;
            }
            if (!there && !hurt) Note(sim, $"its scout cannot get to {gx},{gz} and gives the place up");
            if (!hurt) _scouted.Add((gx, gz));
            if (hurt || !_scoutSearch) _lookDone = tick; // a look at a town it knows of, or chased off on the way
            _scoutGoal = null;
            if (hurt)
            {
                Note(sim, "its scout comes home hurt");
                SendHome(sim, hall, _scout, issue);
                if (!_mending.Contains(_scout)) _mending.Add(_scout);
                _scout = 0;
                _scoutLost++;
                _scoutAt = tick + ScoutWait;
                return;
            }
        }

        bool hallKnown = KnowsEnemyHall;
        // Hard looks only when it is thinking of attacking (see HardArmy): every look risks a rider, and its
        // guess at where their town is will do until then. And while a wave of its own is looking for the
        // enemy (see Seeking), a rider looks too, one of the wave's if it has one: it covers the ground
        // several times as fast as the wave does.
        bool hunting = Hard && sim.Fog && !hallKnown && _wave.Count > 0 && _waveLair == 0 && _waveGuess != null;
        bool look = tick >= _scoutAt && (Hard ? sim.Fog && (_wave.Count == 0 ? _wantLook : hunting) : !hallKnown || (sim.Fog && _wave.Count == 0));
        var goal = look ? ScoutGoal(sim, hall, hallKnown) : null;
        if (goal != null && _scout == 0 && hunting) _scout = PickScout(sim, _wave);
        if (goal != null && (_scout != 0 || (_scout = PickScout(sim, home)) != 0))
        {
            home.Remove(_scout);
            _wave.Remove(_scout);
            _scoutGoal = goal;
            _scoutSearch = !hallKnown;
            _scoutSent = tick;
            _scoutStops = 0;
            if (hallKnown) _scoutAt = tick + RescoutTicks;
            Note(sim, hallKnown ? "sends a scout for another look at their town" : $"sends a scout to look at {goal.Value.X},{goal.Value.Z}");
            int at = u.IndexOf(_scout);
            var (mx, mz) = LookFrom(sim, goal.Value.X, goal.Value.Z, u.X[at].FloorToInt(), u.Z[at].FloorToInt(), !hallKnown, 0);
            issue(new MoveCommand(Player, new[] { _scout }, mx, mz));
            return;
        }
        // Hard waits for a look before a wave sets out: with nobody fast enough to send, or nowhere to look,
        // the wave goes without one.
        if (look && _scout == 0) _lookDone = tick;
        if (_scout != 0)
        {
            // Done: back home, and a soldier like the others again; or, with a wave of Hard's out, after the wave.
            if (Hard && _wave.Count > 0 && _waveLair == 0 && !_joining.Contains(_scout)) _joining.Add(_scout);
            else SendHome(sim, hall, _scout, issue);
            _scout = 0;
        }
        if (hallKnown) _scouted.Clear();
    }

    /// <summary>
    /// How long (ticks) until the next scout after one was lost or came home hurt: longer each time (up to
    /// the fourth), but never long while it does not know where the enemy's town hall is.
    /// </summary>
    private long ScoutWait => KnowsEnemyHall ? Math.Min(_scoutLost, Hard ? 2 : 4) * RescoutTicks / 2 : Math.Min(_scoutLost, 4) * 30L * Tps;

    private void SendHome(Simulation sim, Building hall, int id, Action<Command> issue)
    {
        if (!sim.Units.Exists(id)) return;
        var (rx, rz) = RallyAt(sim, hall, Spot(EnemyHall(sim, Middle(hall).X, Middle(hall).Z)));
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
            if (i < 0 || _mending.Contains(id) || Hurt(sim, i, HealedPercent) || ShutIn(sim, i)) continue;
            var speed = sim.Content.Units[u.Def[i]].Speed;
            if (speed < ScoutSpeed) continue;
            if (best == 0 || speed > bestSpeed) (best, bestSpeed) = (id, speed);
        }
        return best;
    }

    /// <summary>Looking again at a known town hall, the scout stops this far (columns) short of it.</summary>
    public const int ScoutStandOff = 14;

    /// <summary>
    /// Where the scout goes next. Looking again at a known town hall: a point short of it on the way from
    /// home. Otherwise where <see cref="SearchGoal"/> says to look, from where the scout stands. Null if
    /// there is nowhere left to look.
    /// </summary>
    private (int X, int Z)? ScoutGoal(Simulation sim, Building hall, bool hallKnown)
    {
        var (hx, hz) = Middle(hall);
        if (hallKnown)
        {
            if (KnownHall(hx, hz) is not { } enemy) return null;
            return Toward(enemy.X, enemy.Z, hx, hz, ScoutStandOff, sim.World);
        }
        int at = _scout != 0 ? sim.Units.IndexOf(_scout) : -1;
        var (sx, sz) = at >= 0 ? (sim.Units.X[at].FloorToInt(), sim.Units.Z[at].FloorToInt()) : (hx, hz);
        return SearchGoal(sim, hx, hz, sx, sz);
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
        RuinStep(sim, hall, home, GuardPercent, issue);
        if (_waveLair == 0) ScoutStep(sim, hall, home, issue);
        else home.Remove(_scout);
        HuntStep(sim, hall, home, GuardPercent, issue);
        SiteStep(sim, hall, home, GuardPercent, issue);
        GreatWorkStep(sim, hall, home, GuardPercent, issue);

        // Defence: as the classic player, with the fighting done well from Normal up.
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
            if (Skilled) Micro(sim, home, issue);
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
                    var (rx, rz) = RallyAt(sim, hall, Spot(EnemyHall(sim, hx, hz)));
                    issue(new MoveCommand(Player, _wave.ToArray(), rx, rz));
                    foreach (int id in _wave)
                        if (Hurt(sim, u.IndexOf(id), HealedPercent) && !_mending.Contains(id)) _mending.Add(id);
                    EndWave();
                    return;
                }
                if (Skilled) Micro(sim, _wave, issue);
                if (Marching(sim) || Seeking(sim, hall, issue)) return;
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
        var goal = enemyHall is { } eh ? (eh.AX, eh.AZ) : SearchGoal(sim, hx, hz, hx, hz);
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
    /// none out, no wave out and enough soldiers home, a new one sets out for the nearest town it has seen
    /// that we do not hold.
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
            if (sim.Vision.At(Player, town.X, town.Z) == Sight.Unseen) continue; // only a town it has seen
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
            if (i >= 0 && sim.Units.Owner[i] != Player && sim.Vision.SeesUnit(Player, i)) return true;
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
        _seekStart = _seekSent = sim.Tick;
        _seekStops = 0;
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
            if (building == 0) (ex, ez) = LookFrom(sim, ex, ez, cx, cz);
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

    /// <summary>A wave looking for the enemy gives a place up after this long (ticks) on its way there, and comes home after this long looking.</summary>
    public const int SeekGiveUpTicks = 90 * Tps, SeekTicks = 8 * 60 * Tps;

    /// <summary>
    /// A wave with no building it knows of to go for looks for the enemy: it walks to where
    /// <see cref="SearchGoal"/> says to look, and from each place it has seen to the next, until it sees an
    /// enemy building (then the classic wave takes over from there) or has looked for
    /// <see cref="SeekTicks"/> (then it comes home). False once it has a building to go for.
    /// </summary>
    private bool Seeking(Simulation sim, Building hall, Action<Command> issue)
    {
        if (_waveGuess is not var (gx, gz)) return false;
        var (cx, cz) = Centroid(sim, _wave);
        if (NextTarget(sim, cx, cz) is { } found)
        {
            _waveBuilding = found.Id;
            _waveGuess = null;
            _waveBest = int.MaxValue;
            return false;
        }
        long tick = sim.Tick;
        bool seen = sim.Vision.At(Player, gx, gz) != Sight.Unseen;
        if (seen || tick - _seekSent > SeekGiveUpTicks)
        {
            if (!seen) _scouted.Add((gx, gz)); // it cannot get there
            var (hx, hz) = Middle(hall);
            var next = tick - _seekStart <= SeekTicks ? SearchGoal(sim, hx, hz, cx, cz) : null;
            if (next is not var (nx, nz))
            {
                Note(sim, "its wave has looked for the enemy long enough and comes home");
                var (rx, rz) = RallyAt(sim, hall, null);
                issue(new MoveCommand(Player, _wave.ToArray(), rx, rz));
                EndWave();
                return true;
            }
            _waveGuess = next;
            _seekSent = tick;
            _seekStops = 0;
            var (lx, lz) = LookFrom(sim, nx, nz, cx, cz);
            issue(new AttackMoveCommand(Player, _wave.ToArray(), lx, lz));
            return true;
        }
        if (tick % (5 * ArmyTicks) < ArmyTicks)
        {
            // Most of the wave standing about short of the place: there may be no way to where it was sent.
            var idle = Unordered(sim, _wave, cx, cz);
            idle.RemoveAll(id => sim.Units.Order[sim.Units.IndexOf(id)] != UnitOrder.Idle);
            if (2 * idle.Count > _wave.Count) _seekStops = Math.Min(_seekStops + 1, 3);
            var (lx, lz) = LookFrom(sim, gx, gz, cx, cz, true, _seekStops);
            var again = Unordered(sim, _wave, lx, lz);
            if (again.Count > 0) issue(new AttackMoveCommand(Player, again.ToArray(), lx, lz));
        }
        return true;
    }

    /// <summary>
    /// A wave whose last target is gone, with no other enemy building it knows of, starts looking for the
    /// rest (see <see cref="Seeking"/>) where there is fog of war; false if there is nowhere to look.
    /// </summary>
    private bool Seek(Simulation sim, Building hall, Action<Command> issue)
    {
        if (!Smart || !sim.Fog) return false;
        var (hx, hz) = Middle(hall);
        var (cx, cz) = Centroid(sim, _wave);
        if (SearchGoal(sim, hx, hz, cx, cz) is not var (gx, gz)) return false;
        _waveGuess = (gx, gz);
        _waveBuilding = 0;
        _seekStart = _seekSent = sim.Tick;
        Note(sim, "has razed every enemy building it knew of; its wave looks for the rest");
        _seekStops = 0;
        var (lx, lz) = LookFrom(sim, gx, gz, cx, cz);
        issue(new AttackMoveCommand(Player, _wave.ToArray(), lx, lz));
        return true;
    }

    /// <summary>Soldiers at home not at the rally point go there (as the classic player); the mending go by the town hall.</summary>
    private void RallyHome(Simulation sim, Building hall, Foe? enemyHall, List<int> home, Action<Command> issue)
    {
        var u = sim.Units;
        var rally = RallyAt(sim, hall, Spot(enemyHall));
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
    /// From Normal up: the fighting. Ranged troops and casters shoot the enemy soldier in reach they are strong against,
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
    /// (a few for each wave) and siege (two) are trained only up to a number. With
    /// <paramref name="anyAffordable"/> it picks among the units it can pay for now, not among all of them.
    /// True if it queued anything.
    /// </summary>
    private bool SmartTrain(Simulation sim, Action<Command> issue, bool anyAffordable = false)
    {
        if (!Hard) Keep(sim); // Hard's economy has worked out what to keep in hand
        var (seen, mine) = TrainCounts(sim);
        bool trained = false;
        foreach (var b in sim.Buildings.All)
        {
            if (b.Owner != Player || !b.Active || b.Def.Kind != BuildingKind.Barracks || b.Queue.Count >= 2) continue;
            long total = 0;
            foreach (string id in b.Def.Trains)
                if (sim.Content.TryGetUnit(id, out var d) && sim.Economy.Unlocked(Player, d) && (!anyAffordable || Affordable(sim, d.Cost)))
                    total += Weight(sim, d, seen, mine);
            if (total == 0) continue;
            long roll = _rng.Range(0, (int)Math.Min(total, int.MaxValue));
            foreach (string id in b.Def.Trains)
            {
                if (!sim.Content.TryGetUnit(id, out var d) || !sim.Economy.Unlocked(Player, d) || (anyAffordable && !Affordable(sim, d.Cost))) continue;
                roll -= Weight(sim, d, seen, mine);
                if (roll >= 0) continue;
                if (Affordable(sim, d.Cost))
                {
                    issue(new TrainCommand(Player, b.Id, d.Id));
                    mine[(int)d.Role]++;
                    if (Hard) Spend(d.Cost);
                    trained = true;
                }
                break;
            }
        }
        return trained;
    }

    /// <summary>
    /// How likely (a weight; see <see cref="SmartTrain"/>) a barracks of a smart level is to train a unit of
    /// this type next, from the enemy soldiers it remembers and its own army as they are now.
    /// </summary>
    public long TrainWeight(Simulation sim, UnitDef def)
    {
        var (seen, mine) = TrainCounts(sim);
        return Weight(sim, def, seen, mine);
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

    /// <summary>Hard weighs a unit's worth in a fight for its price against a spearman's, between these shares (percent).</summary>
    public const int LeastWorthPercent = 50, MostWorthPercent = 200;

    /// <summary>
    /// The weight of a unit type: its role's (see below), and at Hard times what it is worth in a fight
    /// (see <see cref="Power"/>) for its price, measured against the cheapest unit of the line: it gets the
    /// most army out of what it gathers.
    /// </summary>
    private long Weight(Simulation sim, UnitDef def, int[] seen, int[] mine)
    {
        long w = Weight(sim, def.Role, seen, mine);
        if (!Hard || w == 0 || def.Role is UnitRole.Sapper or UnitRole.Siege) return w;
        var line = CheapSoldier(sim);
        if (line == null) return w;
        long worth = Power(def, def.MaxHp) * Price(line) * 100 / Math.Max(1, Power(line, line.MaxHp) * Price(def));
        return Math.Max(1, w * Math.Clamp(worth, LeastWorthPercent, MostWorthPercent) / 100);
    }

    /// <summary>What a unit costs, all goods together (stone double: it is dug, not gathered).</summary>
    private static int Price(UnitDef def)
    {
        int cost = 0;
        foreach (var g in def.Cost) cost += g.Good == Good.Stone ? 2 * g.Amount : g.Amount;
        return Math.Max(1, cost);
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

    /// <summary>Normal: a second town hall on new ground, and for a faction it plays balanced (see <see cref="Content.AiStyle"/>) a wall round the first.</summary>
    private void SmartEconomy(Simulation sim, Building hall, Action<Command> issue)
    {
        if (!Skilled) return;
        long tick = sim.Tick;
        if (tick >= ExpandTicks) Expand(sim, hall, issue);
        if (!_walled && tick >= WallTicks && Style(sim) == Content.AiStyle.Balanced) Wall(sim, hall, issue);
        else if (_walled && tick >= _wallUpgradeAt)
        {
            _wallUpgradeAt = tick + WallUpgradeTicks;
            UpgradeWall(sim, issue);
        }
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
        if (EnemyHall(sim, hx, hz) is { } enemy && Dist2(enemy.X, enemy.Z, at.X, at.Z) <= Dist2(hx, hz, at.X, at.Z)) return;
        PlaceNear(sim, def, at, 16, issue);
    }

    private static int Price(BuildingDef def, Good good)
    {
        foreach (var c in def.Cost)
            if (c.Good == good) return c.Amount;
        return 0;
    }

    /// <summary>
    /// A wall round the town hall, <see cref="WallGap"/> columns out, with gates in the middle of the door
    /// side and the far side, once it has the goods: barriers, the first tier (see <see cref="UpgradeWall"/>).
    /// Its own units walk through the gates; enemies have to break through while its soldiers fight them.
    /// </summary>
    private void Wall(Simulation sim, Building hall, Action<Command> issue)
    {
        if (FirstBuilding(sim, BuildingKind.Wall) is not { } wall || FirstBuilding(sim, BuildingKind.Gate) is not { } gate) return;
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

    /// <summary>
    /// Normal: when it has the goods to upgrade the whole of its wall to the next tier (and the tech, for
    /// stone) with <see cref="WallUpgradeSpare"/> of each left over, it does, all of it at once: barriers to
    /// wooden walls, then those to stone.
    /// </summary>
    private void UpgradeWall(Simulation sim, Action<Command> issue)
    {
        Building? first = null;
        foreach (var b in sim.Buildings.All)
            if (b.Owner == Player && b.Def.IsWall && b.Def.Upgrade != null && (first == null || b.Def.Tier < first.Def.Tier)
                && sim.Economy.CanUpgrade(b) == PlaceResult.Ok) first = b;
        if (first == null) return;
        var total = new int[Goods.Count];
        foreach (var b in sim.Economy.Stretch(first))
            foreach (var g in b.Def.Upgrade!.Cost) total[(int)g.Good] += g.Amount;
        var stock = sim.Economy.Stockpile(Player);
        foreach (var good in Goods.All)
            if (total[(int)good] > 0 && stock[(int)good] < total[(int)good] + _keep[(int)good] + WallUpgradeSpare) return;
        issue(new UpgradeCommand(Player, Array.Empty<int>(), first.Id));
        Note(sim, $"upgrades its wall to {first.Def.Upgrade!.Name.ToLowerInvariant()}");
    }
}
