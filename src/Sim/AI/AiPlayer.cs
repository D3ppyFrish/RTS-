using System;
using System.Collections.Generic;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.AI;

/// <summary>
/// A computer player. It plays only by giving the commands a person could give (see <see cref="Command"/>),
/// deciding from the game as it stands each time it looks, so a match with it in records and replays like
/// any other, and two of them make a headless match (see <see cref="AiMatch"/>). What it reads, and its own
/// seeded random numbers, are the same on every machine, so it plays the same everywhere.
///
/// Economy: all its workers are automatic, so automation has them gather what is needed most, put up a
/// house when training needs room and a farm when food runs short (see
/// <see cref="Automation.AutomationSystem"/>). On top of that it trains workers at the town hall, more as
/// the match goes on; researches techs in a set order; puts up barracks; and puts a storehouse by gathering
/// that is far from storage. It keeps a minute's food in hand for everyone before spending food. While
/// food is short it frees workers for food and saves up for a farm; while wood is short it sends workers to
/// trees farther out than automatic workers look.
///
/// Army: the starting soldiers first clear the nearest monster lair. The barracks train a mix of the
/// soldiers the player can train. Soldiers at home gather in front of the town hall, and when there are
/// enough of them they go as a wave to the nearest enemy town hall, fighting what they meet, and bring it
/// down; each wave is bigger than the last, and a smaller one goes when the enemy has few soldiers left.
/// Enemies near the town hall are met by every soldier at home, and by a wave that has not gone far. A
/// wave that has lost most of its soldiers comes home. Orcs ride or recruit the monsters they beat; the
/// Settlers' soldiers evolve on their own into the strongest form.
/// </summary>
public sealed partial class AiPlayer
{
    private const int Tps = Simulation.TicksPerSecond;
    /// <summary>Ticks between looks at the economy, and at the army.</summary>
    public const int EconomyTicks = 2 * Tps, ArmyTicks = Tps;
    /// <summary>Workers wanted at the start of the match, how many more each minute, and at most.</summary>
    public const int FirstWorkers = 10, WorkersPerMinute = 1, MostWorkers = 30;
    /// <summary>Soldiers the first wave needs, and how many more each wave after it.</summary>
    public const int FirstWave = 12, WaveGrowth = 4, BiggestWave = 60;
    /// <summary>
    /// A smaller wave sets out anyway when it has at least this many soldiers and more than twice as many
    /// as the enemy has in all: a beaten enemy is finished off rather than left to recover.
    /// </summary>
    public const int LeastWave = 4;
    /// <summary>No wave sets out before this.</summary>
    public const int FirstWaveTicks = 3 * 60 * Tps;
    /// <summary>After this, waves set out at half the size: the match should end.</summary>
    public const int LateTicks = 25 * 60 * Tps;
    /// <summary>A wave down to this share (percent) of the soldiers it set out with comes home.</summary>
    public const int RetreatPercent = 25;
    /// <summary>Enemies this close (columns) to a town hall of ours are met by the soldiers at home.</summary>
    public const int DefendRadius = 32;
    /// <summary>A wave this close to home when enemies come turns back to help.</summary>
    public const int RecallRadius = 48;
    /// <summary>A wave this close to its target building attacks it (or the defenders around it).</summary>
    public const int SiegeRadius = 14;
    /// <summary>
    /// Ticks a wave at its target waits for a defender to fall before the defenders stop holding it back (they
    /// may be out of reach, or keep out of the fight): it brings the target down instead.
    /// </summary>
    public const int SiegeStallTicks = 30 * Tps;
    /// <summary>How far in front of the town hall (toward the enemy) soldiers at home gather.</summary>
    public const int RallyDistance = 12;
    /// <summary>The starting soldiers clear a lair at most this far (columns) from the town hall.</summary>
    public const int LairReach = 80;
    /// <summary>Gathering this far (columns) from storage wants a storehouse; at most this many storehouses.</summary>
    public const int FarGather = 18, MostStorehouses = 4;
    /// <summary>
    /// Wood below this is short. While it is, and fewer than a quarter of the workers (at least
    /// <see cref="LeastWoodWorkers"/>) cut trees, it sends more to the trees nearest its town hall, out to
    /// <see cref="WoodReach"/> columns: automatic workers only look for trees near where they are.
    /// </summary>
    public const int WoodShort = 100, LeastWoodWorkers = 3, WoodReach = 128;
    /// <summary>
    /// Ticks between sending workers to trees. If none of the last ones it sent is cutting wood by then
    /// (they could not get there, say), it gives up on those trees and looks for others.
    /// </summary>
    public const int WoodTicks = 20 * Tps;
    /// <summary>Barracks: the first once it has this many workers, a second from this tick on.</summary>
    public const int BarracksWorkers = 10, SecondBarracksTicks = 12 * 60 * Tps;
    /// <summary>
    /// Orders to units already doing what is wanted are not given again: a unit gets a new order to go
    /// somewhere when it is idle or headed more than this many columns away from it.
    /// </summary>
    public const int Slack = 8;
    /// <summary>A wave leaves this share (percent) of the soldiers at home behind, and at least <see cref="LeastGuard"/>.</summary>
    public const int GuardPercent = 20, LeastGuard = 2;
    /// <summary>
    /// While food is short, workers are moved onto food until this share (percent) of them gather or farm
    /// it, a few at a time. Automation only gives an idle worker a new job, and a gatherer keeps at its
    /// kind of block, so this frees some gatherers of other goods for automation to send to food.
    /// </summary>
    public const int FoodWorkersPercent = 60, MovedAtOnce = 2;
    /// <summary>
    /// Ticks between freeing workers for food; and how long it waits instead when none of the last ones
    /// it freed went to food (there is none in reach, and no farm with room), so it does not keep pulling
    /// workers off their work for nothing.
    /// </summary>
    public const int FeedTicks = 15 * Tps, FeedBackoffTicks = 90 * Tps;

    /// <summary>Techs in the order it researches them (those its faction cannot have are skipped).</summary>
    public static readonly string[] TechOrder =
    {
        "farming", "tools", "archery", "horse_breeding", "masonry", "sapping", "basic_magic", "crop_rotation",
        "orc_war_drums", "orc_beast_pens", "deep_chitin", "settler_guilds", "deep_warrens", "roads", "carts", "smelting",
        "settler_drill",
    };

    /// <summary>The smart levels research the discovery techs they have opened after this many of their usual order.</summary>
    public const int DiscoveriesAfter = 2;

    /// <summary>
    /// Its tech order as it researches it: for the smart levels, each discovery tech (in the data's order) whose
    /// discovery it has made comes in after the first <see cref="DiscoveriesAfter"/> of the order. They are cheap and
    /// each pays off at once (ruins and lairs out of the fog for its parties, workers safe beside a lair). The
    /// classic level never researches them.
    /// </summary>
    private IEnumerable<string> InLine(Simulation sim, string[] order)
    {
        for (int i = 0; i < order.Length; i++)
        {
            if (i == DiscoveriesAfter && Smart)
                foreach (var tech in sim.Content.Techs)
                    if (tech.Discovery != Discovery.None && sim.Economy.Discovered(Player, tech.Discovery)) yield return tech.Id;
            yield return order[i];
        }
    }

    /// <summary>How often (out of the weights of what a barracks can train) each role is picked.</summary>
    private static int RoleWeight(UnitRole r) => r switch
    {
        UnitRole.LineInfantry => 4,
        UnitRole.HeavyInfantry => 3,
        UnitRole.Ranged => 3,
        UnitRole.Cavalry => 3,
        UnitRole.Caster => 2,
        UnitRole.Sapper => 2,
        UnitRole.Siege => 1,
        _ => 0,
    };

    private readonly DeterministicRandom _rng;
    private bool _started;
    // The wave out (unit ids), what it is after (a building id, or a lair id for the opening raid), and how big it set out.
    private readonly List<int> _wave = new();
    private int _waveBuilding, _waveLair, _waveSize, _waves;
    private long _waveSent, _waveCheck;
    // At the target: since when no defender has fallen, and how many there were at the last look.
    private long _siegeSince;
    private int _siegeDefenders = int.MaxValue;
    private int _waveBest = int.MaxValue;
    // Beaten monsters it has tried to ride (by id): next time it recruits them.
    private readonly List<int> _rideTried = new();
    // The workers it last freed for food, and when it may next free some.
    private readonly List<int> _fed = new();
    private long _feedAt;
    // Goods to keep in hand when spending (see Keep).
    private readonly int[] _keep = new int[Goods.Count];
    // The trees it last sent workers to (a trunk's base), if any; the workers it sent and when it may send
    // more; and trees it gave up on (no more are looked for near them).
    private (int X, int Y, int Z)? _forest;
    private readonly List<int> _woodSent = new();
    private long _woodAt;
    private readonly List<(int X, int Z)> _badForests = new();
    // Where soldiers at home gather, and when each soldier (by id) was last sent there.
    private (int X, int Z)? _rally;
    private readonly Dictionary<int, long> _rallied = new();
    private readonly List<int> _scratch = new();

    public AiPlayer(int player, ulong seed, AiLevel level = AiLevel.Classic)
    {
        Player = player;
        Level = level;
        _rng = new DeterministicRandom(seed ^ 0xA1u ^ (ulong)(player + 1) * 0x9E3779B97F4A7C15UL);
    }

    /// <summary>How well it plays (see <see cref="AiLevel"/>).</summary>
    public AiLevel Level { get; }

    /// <summary>True from <see cref="AiLevel.Easy"/> up: it plays by the fog of war, scouts, counters and pulls back its wounded.</summary>
    private bool Smart => Level != AiLevel.Classic;

    /// <summary>True from <see cref="AiLevel.Normal"/> up: it also focuses its fire, builds more, walls its town hall and settles new ground.</summary>
    private bool Skilled => Level >= AiLevel.Normal;

    /// <summary>True at <see cref="AiLevel.Hard"/>, which runs its own economy and army (see <c>AiPlayer.Hard.cs</c>).</summary>
    private bool Hard => Level == AiLevel.Hard;

    /// <summary>The player it plays.</summary>
    public int Player { get; }

    /// <summary>Waves it has sent so far (the opening lair raid does not count).</summary>
    public int Waves => _waves;

    /// <summary>The soldiers out on a wave (or the opening lair raid) now, by id.</summary>
    public IReadOnlyList<int> Wave => _wave;

    /// <summary>What its army is doing, in a word or two (for match traces; see the match runner's --trace).</summary>
    public string Doing { get; private set; } = "";

    /// <summary>The building the wave out is after (0 if none).</summary>
    public int WaveTarget => _waveBuilding;

    /// <summary>
    /// Looks at the game before tick <see cref="Simulation.Tick"/> runs and gives its commands through
    /// <paramref name="issue"/>, which should schedule them for that tick (and record them, for a replay).
    /// Call it once before every tick.
    /// </summary>
    public void Think(Simulation sim, Action<Command> issue)
    {
        if (sim.Match.Players.Count > 0 && !sim.Match.InPlay(Player)) return;
        if (sim.Match.Over) return;
        var hall = sim.Economy.TownHall(Player);
        if (hall == null) return;
        var raw = issue;
        if (Smart) issue = c => { if (SpareRaiders(c) is { } kept) raw(kept); }; // raiders left to it (AiPlayer.Raids.cs)
        for (int p = 0; p < Simulation.WildPlayer; p++) _peace[p] = sim.Truces.AtPeace(Player, p);
        if ((sim.Tick + Player * 5) % ArmyTicks == 0)
        {
            AnswerTruces(sim, issue);
            AnswerDeals(sim, issue);
        }
        if (Smart && (sim.Tick + Player * 11) % TradeTicks == 0) Trade(sim, hall, issue);
        if (!_started)
        {
            _started = true;
            Start(sim, issue);
        }
        if (Smart) SeeItems(sim); // dropped items lying where it sees (AiPlayer.Items.cs)
        long tick = sim.Tick;
        // Players look on different ticks, so that two of them do not both work on the same tick.
        if ((tick + Player * 7) % EconomyTicks == 0) Economy(sim, hall, issue);
        if (Smart && (tick + Player * 7) % EconomyTicks == 0) Prospect(sim, issue);
        if ((tick + Player * 3) % ArmyTicks == 0)
        {
            if (Smart) Fell(sim, issue);
            if (Smart) Raid(sim, raw);
            Molt(sim, hall, issue);
            Bloom(sim, issue);
            if (Hard) HardArmy(sim, hall, issue);
            else if (Smart) SmartArmy(sim, hall, issue);
            else Army(sim, hall, issue);
        }
        if (Smart) Dams(sim, hall, issue);
        if (Smart && (tick + Player * 3) % ArmyTicks == 0) LeaveScar(sim, issue);
        if (Skilled && (tick + Player * 3) % WallCheckTicks == 0) LowerIdleWalls(sim, issue);
    }

    // --- Truces ------------------------------------------------------------------------------------

    // By player: at peace with it this tick (see TruceSystem); refreshed at the start of every Think.
    private readonly bool[] _peace = new bool[Simulation.WildPlayer];

    /// <summary>True if the side is a rival it fights: another player, not the wild side, not one it is at peace with.</summary>
    private bool Rival(int owner) => owner != Player && owner != Simulation.WildPlayer && !((uint)owner < (uint)_peace.Length && _peace[owner]);

    /// <summary>
    /// A computer player takes a rival's offer of a truce unless its own soldiers are clearly the stronger (more
    /// than <see cref="TruceRefusePercent"/>% of the rival's, by the same worth it weighs fights by).
    /// </summary>
    public const int TruceRefusePercent = 150;

    /// <summary>Answers the truces it is offered: accepts each one it is not clearly too strong to want.</summary>
    private void AnswerTruces(Simulation sim, Action<Command> issue)
    {
        long mine = -1;
        for (int p = 0; p < Simulation.WildPlayer; p++)
        {
            if (!sim.Truces.Offered(p, Player) || !sim.Truces.CanOffer(Player, p)) continue;
            if (mine < 0) mine = PowerOf(sim, Soldiers(sim));
            long theirs = PowerOf(sim, Soldiers(sim, p));
            if (mine * 100 > theirs * TruceRefusePercent) continue;
            Note(sim, $"agrees a truce with player {p}");
            issue(new TruceCommand(Player, p));
        }
    }

    // --- Opening -----------------------------------------------------------------------------------

    /// <summary>Every worker automatic, the units' forms chosen, and the starting soldiers off to clear the nearest lair.</summary>
    private void Start(Simulation sim, Action<Command> issue)
    {
        if (!Hard) issue(new AutomateAllCommand(Player, true)); // Hard: see AutomateNew
        foreach (var def in sim.Content.Units)
        {
            if (def.Forms.Count == 0 || !sim.Economy.Unlocked(Player, def)) continue;
            if (BestForm(sim, def) is { } form) issue(new AutoEvolveCommand(Player, def.Id, form.Id));
        }
        var hall = sim.Economy.TownHall(Player)!;
        var (hx, hz) = Middle(hall);
        var soldiers = Soldiers(sim);
        if (Hard)
        {
            _startPower = PowerOf(sim, soldiers);
            KeepExplorer(sim, soldiers);
        }
        var nearest = NearestLair(sim, hx, hz, PowerOf(sim, soldiers));
        if (soldiers.Count == 0 || (nearest == null && (!Smart || sim.Monsters.Lairs.Count == 0))) return;
        _wave.AddRange(soldiers);
        _waveBuilding = 0;
        _waveSize = soldiers.Count;
        _waveSent = sim.Tick;
        if (nearest != null)
        {
            _waveLair = nearest.Id;
            issue(new AttackMoveCommand(Player, soldiers.ToArray(), nearest.X, nearest.Z));
            return;
        }
        // The smart levels have not seen a lair yet. In a match with lairs there is one on the way from every
        // start to the middle of the map (a rule of the game, not something seen), so the soldiers look that way.
        _waveLair = LairSearch;
        var w = sim.World;
        _lairSearch = Toward(hx, hz, w.SizeX / 2, w.SizeZ / 2, MonsterSystem.LairDistance, w);
        issue(new AttackMoveCommand(Player, soldiers.ToArray(), _lairSearch.X, _lairSearch.Z));
    }

    /// <summary>Hard: its fastest soldier stays out of the opening raid, to explore round its town.</summary>
    private void KeepExplorer(Simulation sim, List<int> soldiers)
    {
        if (soldiers.Count <= 2) return;
        var u = sim.Units;
        int best = -1;
        for (int j = 0; j < soldiers.Count; j++)
        {
            var speed = sim.Content.Units[u.Def[u.IndexOf(soldiers[j])]].Speed;
            if (speed >= ScoutSpeed && (best < 0 || speed > sim.Content.Units[u.Def[u.IndexOf(soldiers[best])]].Speed)) best = j;
        }
        if (best >= 0) soldiers.RemoveAt(best);
    }

    /// <summary>
    /// The nearest lair with monsters still about within <see cref="LairReach"/> of (x, z) (for the smart levels,
    /// one they have seen) whose pack soldiers worth <paramref name="force"/> outweigh (see <see cref="LairOddsPercent"/>), or null.
    /// </summary>
    private Lair? NearestLair(Simulation sim, int x, int z, long force)
    {
        Lair? nearest = null;
        long best = (long)LairReach * LairReach + 1;
        foreach (var lair in sim.Monsters.Lairs)
        {
            if (lair.Cleared || (Smart && !LairKnown(sim, lair)) || !Outweighs(sim, force, lair)) continue;
            long d = Dist2(x, z, lair.X, lair.Z);
            if (d < best) (best, nearest) = (d, lair);
        }
        return nearest;
    }

    /// <summary>
    /// The form units of this type should take: for workers the one that still works, for soldiers the
    /// one with the most damage times hit points (ties to the first). Null if none of its forms is ours.
    /// </summary>
    private UnitDef? BestForm(Simulation sim, UnitDef def)
    {
        UnitDef? best = null;
        long bestScore = -1;
        foreach (var f in def.Forms)
        {
            if (!sim.Economy.Unlocked(Player, f)) continue;
            if (def.Role == UnitRole.Worker && f.Role != UnitRole.Worker) continue;
            long score = (long)Math.Max(1, f.Damage) * f.MaxHp * f.WorkPercent;
            if (score > bestScore) (bestScore, best) = (score, f);
        }
        return best;
    }

    // --- Economy -----------------------------------------------------------------------------------

    private void Economy(Simulation sim, Building hall, Action<Command> issue)
    {
        if (Hard)
        {
            HardEconomy(sim, hall, issue);
            return;
        }
        var econ = sim.Economy;
        int minutes = (int)(sim.Tick / (60 * Tps));
        Keep(sim);
        Wood(sim, hall, issue);

        // A storehouse where workers gather far from storage, before anything else is paid for.
        int barracks = 0, storehouses = 0;
        bool site = false, storeSite = false;
        foreach (var b in sim.Buildings.All)
        {
            if (b.Owner != Player) continue;
            if (IsBarracks(sim, b.Def))
            {
                barracks++;
                site |= !b.Active;
            }
            if (b.Def.Kind == BuildingKind.Storehouse)
            {
                storehouses++;
                storeSite |= !b.Active;
            }
        }
        var storeDef = FirstBuilding(sim, BuildingKind.Storehouse);
        if (storeDef != null && !storeSite && storehouses < MostStorehouses && FarGathering(sim) is var (fx, fz, n) && n >= 3)
        {
            if (Affordable(sim, storeDef.Cost)) PlaceNear(sim, storeDef, (fx, fz), StoreRadius, issue);
            else
                foreach (var c in storeDef.Cost) _keep[(int)c.Good] += c.Amount;
        }
        if (Smart) SaveForBarracks(sim, barracks, site);

        // Research, one tech at a time, in order (the smart levels: with the discovery techs it has opened, see InLine).
        if (econ.Researching(Player) == null)
        {
            foreach (string id in InLine(sim, TechOrder))
            {
                if (!sim.Content.TryGetTech(id, out var tech)) continue;
                var r = econ.CanResearch(Player, tech);
                if (r is ResearchResult.Researched or ResearchResult.OtherFaction) continue;
                if (r == ResearchResult.Ok && Affordable(sim, tech.Cost)) issue(new ResearchCommand(Player, id));
                break;
            }
        }

        Feed(sim, issue);

        // Workers at the town hall, one in the queue at a time.
        int workers = 0;
        var u = sim.Units;
        for (int i = 0; i < u.Count; i++)
            if (u.Owner[i] == Player && sim.Content.Units[u.Def[i]].Role == UnitRole.Worker) workers++;
        int wanted = Math.Min(Skilled ? SkilledMostWorkers : MostWorkers, FirstWorkers + WorkersPerMinute * minutes);
        var worker = FirstTrained(sim, hall, UnitRole.Worker);
        if (worker != null && hall.Queue.Count == 0 && workers < wanted && Affordable(sim, worker.Cost))
            issue(new TrainCommand(Player, hall.Id, worker.Id));

        // Barracks: one once there are enough workers, a second later on.
        var barracksDef = FirstBuilding(sim, BuildingKind.Barracks);
        if (barracksDef != null && !site && ((barracks == 0 && workers >= BarracksWorkers) || (barracks == 1 && sim.Tick >= SecondBarracksAt)
                || (Skilled && barracks == 2 && sim.Tick >= ThirdBarracksTicks && Rich(sim, barracksDef.Cost)))
            && Affordable(sim, barracksDef.Cost))
            PlaceNear(sim, barracksDef, Middle(hall), Automation.AutomationSystem.PlanRadius, issue);

        if (!site) Orchards(sim, hall, issue);
        if (Smart) SiegeEconomy(sim, hall, workers, issue);
        if (Smart) Shipyard(sim, hall, workers, issue);
        if (Smart) Carts(sim, hall, issue);
        if (Smart) Roads(sim, hall, issue);
        if (Smart) ListeningPosts(sim, hall, issue);
        if (Smart) CrystalLens(sim, hall, issue);
        if (Smart) Shrine(sim, hall, issue);
        if (Smart) Fishing(sim, hall, issue);
        if (Smart) Hunting(sim, hall, issue);
        if (Smart) PassMines(sim, hall, issue);
        if (Smart) SmartEconomy(sim, hall, issue);
    }

    /// <summary>How far (columns) from the middle of far gathering it looks for room for a storehouse.</summary>
    private const int StoreRadius = 12;

    /// <summary>
    /// While wood is short (<see cref="WoodShort"/>) and too few workers cut trees, sends up to
    /// <see cref="MovedAtOnce"/> more (idle ones, or ones digging stone, ore or coal) to the trees nearest
    /// the town hall; a storehouse follows once enough of them work far from storage.
    /// </summary>
    private void Wood(Simulation sim, Building hall, Action<Command> issue)
    {
        // Smart levels: idle workers are sent to trees whenever there are any; and after the opening wood is
        // short below twice as much, and a third of the workers cut it (half, some taken off food, with plenty
        // of food in hand).
        bool later = Smart && sim.Tick >= SmartWoodTicks;
        bool woodShort = sim.Economy.Stock(Player, Good.Wood) < (later ? 2 * WoodShort : WoodShort);
        if ((!woodShort && !Smart) || sim.Tick < _woodAt) return;
        var u = sim.Units;
        bool foodRich = later && sim.Economy.Stock(Player, Good.Food) >= FoodRich;
        if (_woodSent.Count > 0)
        {
            bool cutting = false;
            foreach (int id in _woodSent)
            {
                int i = u.IndexOf(id);
                cutting |= i >= 0 && u.Job[i] == UnitJob.Gather && u.JobBlock[i] == BlockType.Wood;
            }
            _woodSent.Clear();
            if (!cutting && _forest is var (bx, _, bz))
            {
                if (_badForests.Count >= 16) _badForests.RemoveAt(0);
                _badForests.Add((bx, bz));
                _forest = null;
            }
        }
        int workers = 0, onWood = 0, idlers = 0;
        _scratch.Clear();
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Owner[i] != Player || sim.Content.Units[u.Def[i]].Role != UnitRole.Worker) continue;
            workers++;
            if (_passMiners.Contains(u.Id[i])) continue; // kept digging the pass's seams
            if (u.Job[i] == UnitJob.Gather && u.JobBlock[i] == BlockType.Wood)
            {
                onWood++;
                continue;
            }
            bool digging = u.Job[i] == UnitJob.Gather && u.CarryAmount[i] == 0 && (u.JobBlock[i] != BlockType.BerryBush || foodRich);
            if (foodRich && u.Job[i] == UnitJob.Farm && u.CarryAmount[i] == 0) digging = true;
            bool idle = u.Job[i] == UnitJob.None && u.Order[i] == UnitOrder.Idle && !u.Held[i];
            if (Smart && idle) _scratch.Insert(idlers++, u.Id[i]); // the idle first
            else if (digging || idle) _scratch.Add(u.Id[i]);
        }
        int share = Math.Max(LeastWoodWorkers, workers / (foodRich ? 2 : later ? 3 : 4)) - onWood;
        int n = Math.Min(Math.Min(share, MovedAtOnce), _scratch.Count);
        if (Smart) n = Math.Max(woodShort ? n : 0, idlers);
        if (n <= 0) return;
        _woodAt = sim.Tick + WoodTicks; // looking for trees far out is slow: not again for a while
        if (!Forest(sim, hall, out int x, out int y, out int z)) return;
        var ids = _scratch.GetRange(0, n).ToArray();
        issue(new GatherCommand(Player, ids, x, y, z));
        _woodSent.AddRange(ids);
    }

    /// <summary>
    /// A tree to send wood cutters to: the one it sent them to last while it stands (they find the trees
    /// around it on their own), or else the base of the trunk nearest the town hall, out to
    /// <see cref="WoodReach"/> columns, that a worker can stand by, that is nearer this town hall than any
    /// enemy's and that is not by a monster lair. The smart levels only know of trees on ground they have
    /// seen. False if there is none.
    /// </summary>
    private bool Forest(Simulation sim, Building hall, out int x, out int y, out int z)
    {
        var w = sim.World;
        if (_forest is var (fx, fy, fz) && BlockInfo.IsTrunk(w.Get(fx, fy, fz)))
        {
            (x, y, z) = (fx, fy, fz);
            return true;
        }
        _forest = null;
        var (hx, hz) = Middle(hall);
        var enemy = EnemyHall(sim, hx, hz);
        var vision = sim.Vision;
        long bestD = long.MaxValue;
        (x, y, z) = (-1, -1, -1);
        for (int r = 0; r <= WoodReach && bestD == long.MaxValue; r++)
        {
            for (int dz = -r; dz <= r; dz++)
            {
                int cz = hz + dz;
                if (cz < 0 || cz >= w.SizeZ) continue;
                int step = dz == -r || dz == r ? 1 : 2 * r;
                for (int dx = -r; dx <= r; dx += Math.Max(1, step))
                {
                    int cx = hx + dx;
                    if (cx < 0 || cx >= w.SizeX) continue;
                    long d = (long)dx * dx + (long)dz * dz;
                    if (d >= bestD) continue;
                    if (Smart && vision.At(Player, cx, cz) == Sight.Unseen) continue; // only trees it has seen
                    if (!TrunkBase(w, cx, cz, out int ty)) continue;
                    if (enemy is { } e && Dist2(cx, cz, e.X, e.Z) <= d) continue;
                    if (NearLair(sim, cx, cz) || NearBadForest(cx, cz) || KeepsOff(sim, cx, cz)) continue;
                    var (baseY, topY) = Harvest.Span(w, cx, ty, cz);
                    if (!Harvest.WorkSpot(w, cx, baseY, topY, cz, hx, hall.Y, hz, out int sx, out int sy, out int sz)) continue;
                    if (World.HazardSystem.IsHazard(w, sx, sy, sz)) continue; // not into the bog, the quicksand or the lava's heat
                    bestD = d;
                    (x, y, z) = (cx, ty, cz);
                }
            }
        }
        if (bestD == long.MaxValue) return false;
        _forest = (x, y, z);
        return true;
    }

    /// <summary>The level of the base of a tree trunk in the column, if there is a tree there.</summary>
    private static bool TrunkBase(VoxelWorld w, int x, int z, out int y)
    {
        int top = w.SurfaceHeight(x, z);
        for (y = top; y >= Math.Max(1, top - 12); y--)
            if (BlockInfo.IsTrunk(w.Get(x, y, z)) && !BlockInfo.IsTrunk(w.Get(x, y - 1, z))) return true;
        return false;
    }

    /// <summary>
    /// True if column (x, z) is near a lair whose monsters are still about (for the smart levels, one they have
    /// seen, and never once they have Beast lore).
    /// </summary>
    private bool NearLair(Simulation sim, int x, int z)
    {
        if (Smart && sim.Economy.SparesWorkers(Player)) return false; // Beast lore: the monsters leave its workers be
        foreach (var lair in sim.Monsters.Lairs)
            if (!lair.Cleared && Dist2(x, z, lair.X, lair.Z) <= (long)LairKeepOff * LairKeepOff && (!Smart || LairKnown(sim, lair))) return true;
        return false;
    }

    /// <summary>Workers are not sent to trees this close (columns) to a lair that has not been cleared.</summary>
    private const int LairKeepOff = 16;

    /// <summary>True if column (x, z) is near trees it gave up on (see <see cref="WoodTicks"/>).</summary>
    private bool NearBadForest(int x, int z)
    {
        foreach (var (bx, bz) in _badForests)
            if (Dist2(x, z, bx, bz) <= (long)Harvest.PlantSearchRadius * Harvest.PlantSearchRadius) return true;
        return false;
    }

    /// <summary>
    /// While food is short (less than <see cref="FoodShort"/> in hand), frees up to
    /// <see cref="MovedAtOnce"/> workers gathering something else (stone first, then ore and coal, then
    /// wood) for automation to send to food, every <see cref="FeedTicks"/>, until
    /// <see cref="FoodWorkersPercent"/> of the workers are on food. If none of the last ones it freed went to
    /// food (or to build, a farm say), it waits <see cref="FeedBackoffTicks"/> before trying again.
    /// </summary>
    private void Feed(Simulation sim, Action<Command> issue)
    {
        var econ = sim.Economy;
        var u = sim.Units;
        long tick = sim.Tick;
        if (econ.Stock(Player, Good.Food) >= FoodShort)
        {
            _fed.Clear();
            return;
        }
        if (tick < _feedAt) return;
        if (_fed.Count > 0)
        {
            bool helped = false;
            foreach (int id in _fed)
            {
                int i = u.IndexOf(id);
                helped |= i >= 0 && (OnFood(u, i) || u.Job[i] == UnitJob.Build);
            }
            _fed.Clear();
            if (!helped)
            {
                _feedAt = tick + FeedBackoffTicks;
                return;
            }
        }
        int workers = 0, onFood = 0;
        _scratch.Clear();
        for (int pass = 0; pass < 3; pass++)
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Owner[i] != Player || sim.Content.Units[u.Def[i]].Role != UnitRole.Worker) continue;
            bool food = OnFood(u, i);
            if (pass == 0)
            {
                workers++;
                if (food) onFood++;
                continue;
            }
            if (u.Job[i] != UnitJob.Gather || food || u.CarryAmount[i] > 0) continue;
            bool wood = u.JobBlock[i] == BlockType.Wood;
            if ((pass == 1) != wood) _scratch.Add(u.Id[i]);
        }
        int want = (workers * FoodWorkersPercent + 99) / 100 - onFood;
        int n = Math.Min(Math.Min(want, MovedAtOnce), _scratch.Count);
        if (n <= 0) return;
        var ids = _scratch.GetRange(0, n).ToArray();
        issue(new StopCommand(Player, ids));
        issue(new AutomateCommand(Player, ids, true));
        _fed.AddRange(ids);
        _feedAt = tick + FeedTicks;
    }

    /// <summary>True if unit i is farming or picking berries.</summary>
    private static bool OnFood(UnitStore u, int i) =>
        u.Job[i] == UnitJob.Farm || (u.Job[i] == UnitJob.Gather && u.JobBlock[i] == BlockType.BerryBush);

    /// <summary>
    /// Where gathering is far from storage: the middle of the blocks being gathered at least
    /// <see cref="FarGather"/> columns from the nearest storage, and how many there are (null if none).
    /// </summary>
    private (int X, int Z, int Count)? FarGathering(Simulation sim)
    {
        var u = sim.Units;
        long sx = 0, sz = 0;
        int n = 0;
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Owner[i] != Player || u.Job[i] != UnitJob.Gather) continue;
            int x = u.JobX[i], z = u.JobZ[i];
            var store = sim.Economy.NearestStorage(Player, x, z);
            if (store != null && store.ColumnDistance(x, z) < FarGather) continue;
            sx += x;
            sz += z;
            n++;
        }
        return n == 0 ? null : ((int)(sx / n), (int)(sz / n), n);
    }

    /// <summary>
    /// True if a building with its corner at (x, z) keeps its builders off hazard ground: no bog, quicksand,
    /// sinkhole or lava heat under it or in the ring round it (see <see cref="World.HazardSystem.HazardNear"/>), and
    /// (the smart levels) none of it on or near the corrupted ground (see <see cref="KeepsOff(Simulation, int, int)"/>).
    /// </summary>
    private bool SafeGround(Simulation sim, BuildingDef def, int x, int z) =>
        !World.HazardSystem.HazardNear(sim.World, x, z, def.SizeX, def.SizeZ)
        && !KeepsOff(sim, x - 1, z - 1, x + def.SizeX, z + def.SizeZ);

    /// <summary>Lays out a site for the building near the point (on safe ground), for the automatic workers to build.</summary>
    private void PlaceNear(Simulation sim, BuildingDef def, (int X, int Z) at, int radius, Action<Command> issue)
    {
        if (sim.Automation.FindRoom(Player, def, at.X, at.Z, radius, out int x, out int z, (bx, bz) => SafeGround(sim, def, bx, bz)))
            issue(new BuildCommand(Player, Array.Empty<int>(), def.Id, x, z));
    }

    /// <summary>
    /// Food below which it moves workers to food (<see cref="Feed"/>): about a soldier's price. Units eat
    /// nothing once trained, so no food is held back from spending.
    /// </summary>
    public const int FoodShort = 40;

    /// <summary>
    /// Works out what to keep in hand when spending: while food is short (as automation sees it) with no farm that has room or is being built, the price of a farm,
    /// so that automation can put one up (it does when the player can pay; see
    /// <see cref="Automation.AutomationSystem"/>).
    /// </summary>
    private void Keep(Simulation sim)
    {
        Array.Clear(_keep);
        var econ = sim.Economy;
        var farm = FirstBuilding(sim, BuildingKind.Farm);
        if (farm == null || econ.Stock(Player, Good.Food) >= 100) return;
        var u = sim.Units;
        int workers = 0, slots = 0, farmers = 0;
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Owner[i] != Player || sim.Content.Units[u.Def[i]].Role != UnitRole.Worker) continue;
            workers++;
            if (u.Job[i] == UnitJob.Farm) farmers++;
        }
        foreach (var b in sim.Buildings.All)
        {
            if (b.Owner != Player || b.Def.Kind != BuildingKind.Farm) continue;
            if (!b.Active) return; // one is on its way
            slots += b.Def.WorkerSlots;
        }
        if (farmers < slots || slots >= Math.Max(2, workers / 3)) return;
        foreach (var c in farm.Cost)
            if (c.Good != Good.Food) _keep[(int)c.Good] += c.Amount;
    }

    /// <summary>True if the player can pay the cost and still have what <see cref="Keep"/> keeps in hand.</summary>
    private bool Affordable(Simulation sim, GoodAmount[] cost)
    {
        var stock = sim.Economy.Stockpile(Player);
        foreach (var c in cost)
            if (stock[(int)c.Good] < c.Amount + _keep[(int)c.Good]) return false;
        return true;
    }

    private BuildingDef? FirstBuilding(Simulation sim, BuildingKind kind)
    {
        var faction = sim.FactionOf(Player);
        foreach (var everyone in sim.Content.Buildings)
        {
            // Its faction's own in place of everyone's (the Deepfolk's brood chamber for the barracks).
            var d = faction.Own(everyone);
            if (d.Kind == kind && d.UpgradeOf == null && sim.Economy.Unlocked(Player, d)) return d;
        }
        return null;
    }

    private UnitDef? FirstTrained(Simulation sim, Building b, UnitRole role)
    {
        foreach (string id in b.Def.Trains)
            if (sim.Content.TryGetUnit(id, out var d) && d.Role == role && sim.Economy.Unlocked(Player, d)) return d;
        return null;
    }

    // --- Army --------------------------------------------------------------------------------------

    private void Army(Simulation sim, Building hall, Action<Command> issue)
    {
        Train(sim, issue);
        Tame(sim, issue);

        var u = sim.Units;
        var (hx, hz) = Middle(hall);
        _wave.RemoveAll(id => !u.Exists(id));
        var home = Soldiers(sim);
        home.RemoveAll(_wave.Contains);

        // Defence: enemies near home are met by everyone at home, and by a wave that is still near.
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
            return;
        }

        if (_wave.Count > 0)
        {
            StepWave(sim, hall, issue);
            return;
        }

        // Soldiers at home gather at the rally point; enough of them set out as a wave.
        var enemyHall = EnemyHall(sim, hx, hz);
        int size = Math.Min(BiggestWave, FirstWave + WaveGrowth * _waves);
        if (sim.Tick >= LateTicks) size = Math.Max(FirstWave / 2, size / 2);
        int guard = Math.Max(LeastGuard, home.Count * GuardPercent / 100);
        int ready = home.Count - guard;
        if (enemyHall is { } foe && sim.Tick >= FirstWaveTicks
            && (ready >= size || (ready >= LeastWave && ready > 2 * Soldiers(sim, foe.Owner).Count)))
        {
            // The wave: all but the guard, the nearest to the enemy first.
            var (ex, ez) = (foe.AX, foe.AZ);
            home.Sort((a, b) =>
            {
                int ia = u.IndexOf(a), ib = u.IndexOf(b);
                int c = Dist2(u.X[ia].FloorToInt(), u.Z[ia].FloorToInt(), ex, ez).CompareTo(Dist2(u.X[ib].FloorToInt(), u.Z[ib].FloorToInt(), ex, ez));
                return c != 0 ? c : a.CompareTo(b);
            });
            _wave.AddRange(home.GetRange(0, home.Count - guard));
            _waveBuilding = foe.Id;
            _waveLair = 0;
            _waveSize = _wave.Count;
            _waveSent = sim.Tick;
            _waveBest = int.MaxValue;
            _waves++;
            issue(new AttackMoveCommand(Player, _wave.ToArray(), ex, ez));
            return;
        }
        if (sim.Tick % (5 * ArmyTicks) < ArmyTicks)
        {
            // Soldiers standing away from the rally point go there; one that did not get there is sent
            // again only after a while (the way may be blocked).
            var rally = Rally(sim, hall, Spot(enemyHall));
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
    }

    /// <summary>Where an enemy building stands (its middle), or null.</summary>
    private static (int X, int Z)? Spot(Foe? foe) => foe is { } f ? (f.X, f.Z) : null;

    /// <summary>A soldier sent to the rally point that is still away from it is sent again after this many ticks.</summary>
    private const int RallyAgainTicks = 60 * Tps;

    /// <summary>
    /// The wave out: the opening raid on a lair ends when the lair is cleared; a wave on a building attacks it
    /// once near (or the defenders around it, while there are any), moves on to the enemy's next building
    /// when it falls, and comes home when it is down to <see cref="RetreatPercent"/> of its soldiers.
    /// </summary>
    private void StepWave(Simulation sim, Building hall, Action<Command> issue)
    {
        var u = sim.Units;
        var (hx, hz) = Middle(hall);
        _engines.Clear();
        _shield.Clear();
        if (_waveLair == LairSearch && !FindLair(sim, hall, issue)) return;
        if (_waveLair != 0)
        {
            var lair = sim.Monsters.GetLair(_waveLair);
            if (lair == null || lair.Cleared || sim.Tick - _waveSent > 3 * 60 * Tps) EndWave();
            else if (PowerOf(sim, _wave) * 100 < PackWorth(sim, lair) * BreakOffPercent)
            {
                // Losing to the pack: the raid breaks off and comes home (issue #78).
                Note(sim, $"its opening raid breaks off from the {lair.Monster.Name} lair");
                var (rx, rz) = RallyAt(sim, hall, null);
                issue(new MoveCommand(Player, _wave.ToArray(), rx, rz));
                EndWave();
            }
            else if (sim.Tick % (10 * ArmyTicks) < ArmyTicks)
            {
                var idle = Unordered(sim, _wave, lair.X, lair.Z);
                if (idle.Count > 0) issue(new AttackMoveCommand(Player, idle.ToArray(), lair.X, lair.Z));
            }
            return;
        }
        if (_wave.Count * 100 < _waveSize * RetreatPercent)
        {
            var (rx, rz) = RallyAt(sim, hall, Spot(EnemyHall(sim, hx, hz)));
            issue(new MoveCommand(Player, _wave.ToArray(), rx, rz));
            EndWave();
            return;
        }
        // The building the wave is after, as far as it knows: the smart levels go by what they remember, so
        // one that came down out of their sight is still gone for until they see the place again.
        var (cx, cz) = Centroid(sim, _wave);
        if (Target(sim, _waveBuilding) is not { } foe)
        {
            if (NextTarget(sim, cx, cz) is not { } next)
            {
                if (!Seek(sim, hall, issue)) EndWave();
                return;
            }
            foe = next;
            _waveBuilding = foe.Id;
            _waveBest = int.MaxValue;
            _siegeDefenders = int.MaxValue;
        }
        var (tx, tz) = (foe.AX, foe.AZ);
        int far = (int)IntMath.ISqrt(Dist2(cx, cz, tx, tz));
        Drum(sim, far, tx, tz, issue); // its war drummer: along on the way, beating the charge at the target
        // Its catapults (smart levels) shoot from the last stretch on, with soldiers screening them.
        if (Smart && far <= SiegeReach)
        {
            Bombard(sim, foe, issue);
            // A wave of burrowers tunnels under a wall; a ladder goes up only where no tunnel will do.
            if (!Undermine(sim, foe, cx, cz, issue)) Escalade(sim, foe, cx, cz, far, issue);
        }
        if (far > SiegeRadius && !MostAt(sim, tx, tz))
        {
            // On the way: units that have stopped (they fought something, or gave up) set out again, every
            // few seconds; a wave that has not got any nearer for a minute is sent again from where it is.
            _siegeDefenders = int.MaxValue;
            if (far < _waveBest)
            {
                _waveBest = far;
                _waveCheck = sim.Tick;
            }
            List<int>? again = null;
            if (sim.Tick - _waveCheck > 60 * Tps)
            {
                // A soldier stranded where it cannot walk off (a pillar the ground fell away from, a pit)
                // leaves the wave, or it would hold the wave's middle back from its target for good.
                _wave.RemoveAll(id => Stranded(sim, id));
                if (_wave.Count == 0)
                {
                    EndWave();
                    return;
                }
                again = NotFighting(sim, _wave);
                _waveCheck = sim.Tick;
            }
            else if (sim.Tick % (5 * ArmyTicks) < ArmyTicks) again = Unordered(sim, _wave, tx, tz);
            again?.RemoveAll(id => _engines.Contains(id) || _shield.Contains(id) || Crewed(id));
            if (again is { Count: > 0 }) issue(new AttackMoveCommand(Player, again.ToArray(), tx, tz));
            return;
        }
        // At the target: fight its defenders while there are any, then bring it down. The wave goes for the
        // defender nearest the target, which may stand back out of sight of the door (issue #37). Defenders
        // that hold the wave up for SiegeStallTicks without one of them falling (out of reach, say) stop
        // holding it back.
        if (sim.Buildings.Get(foe.Id) is not { } target)
        {
            // Gone, though it has not seen that yet: a closer look.
            var look = Unordered(sim, _wave, foe.X, foe.Z);
            look.RemoveAll(Crewed);
            if (look.Count > 0) issue(new AttackMoveCommand(Player, look.ToArray(), foe.X, foe.Z));
            return;
        }
        int defender = NearestEnemy(sim, tx, tz, SiegeRadius + 4, soldiersOnly: true, out int defenders);
        if (defenders < _siegeDefenders) _siegeSince = sim.Tick; // just arrived, or one fell
        _siegeDefenders = defenders;
        bool defended = defender >= 0 && sim.Tick - _siegeSince < SiegeStallTicks;
        _scratch.Clear();
        foreach (int id in _wave)
        {
            int i = u.IndexOf(id);
            if (u.Rout[i] > 0 || _engines.Contains(id) || _shield.Contains(id) || Crewed(id)) continue;
            bool onBuilding = u.Combat[i] == CombatOrder.Attack && u.TargetBuilding[i] == target.Id;
            // Sappers (smart levels) go for the building whatever else is going on: they deal it full damage.
            if (Smart && sim.Content.Units[u.Def[i]].Role == UnitRole.Sapper)
            {
                if (!onBuilding) (_sappers ??= new()).Add(id);
                continue;
            }
            if (defended ? onBuilding || (u.Target[i] == 0 && u.Order[i] == UnitOrder.Idle) : !onBuilding)
                _scratch.Add(id);
        }
        if (_sappers is { Count: > 0 })
        {
            issue(new AttackBuildingCommand(Player, _sappers.ToArray(), target.Id));
            _sappers.Clear();
        }
        if (_scratch.Count == 0) return;
        if (defended) issue(new AttackMoveCommand(Player, _scratch.ToArray(), u.X[defender].FloorToInt(), u.Z[defender].FloorToInt()));
        else issue(new AttackBuildingCommand(Player, _scratch.ToArray(), target.Id));
    }

    /// <summary>
    /// True if most of the wave is within <see cref="SiegeRadius"/> of (tx, tz): it has got there, though a few
    /// stuck far behind (on the way out of its own town, say) hold its middle back.
    /// </summary>
    private bool MostAt(Simulation sim, int tx, int tz)
    {
        var u = sim.Units;
        int at = 0;
        foreach (int id in _wave)
        {
            int i = u.IndexOf(id);
            if (i >= 0 && Dist2(u.X[i].FloorToInt(), u.Z[i].FloorToInt(), tx, tz) <= (long)SiegeRadius * SiegeRadius) at++;
        }
        return 2 * at > _wave.Count;
    }

    private void EndWave()
    {
        _wave.Clear();
        _waveGuess = null;
        _stage = null;
        _waveBuilding = _waveLair = _waveSize = 0;
        _waveBest = int.MaxValue;
        _siegeDefenders = int.MaxValue;
        ResetLadders();
        ResetTunnels();
        ResetFerry();
        ResetIce();
    }

    /// <summary>Keeps each barracks training: a unit picked by <see cref="RoleWeight"/> from what it can train, when affordable.</summary>
    private void Train(Simulation sim, Action<Command> issue)
    {
        Keep(sim);
        foreach (var b in sim.Buildings.All)
        {
            if (b.Owner != Player || !b.Active || b.Def.Kind != BuildingKind.Barracks || b.Queue.Count >= 2 || Docks(sim, b.Def)) continue;
            // A war drummer first, if it can train one and has none (one goes with each wave).
            if (Drummer(sim, b) is { } drummer)
            {
                if (Affordable(sim, drummer.Cost)) issue(new TrainCommand(Player, b.Id, drummer.Id));
                continue;
            }
            int total = 0;
            foreach (string id in b.Def.Trains)
                if (sim.Content.TryGetUnit(id, out var d) && sim.Economy.Unlocked(Player, d) && !d.Ram && d.Drums == null) total += RoleWeight(d.Role);
            if (total == 0) continue;
            int roll = _rng.Range(0, total);
            foreach (string id in b.Def.Trains)
            {
                if (!sim.Content.TryGetUnit(id, out var d) || !sim.Economy.Unlocked(Player, d) || d.Ram || d.Drums != null) continue;
                roll -= RoleWeight(d.Role);
                if (roll >= 0) continue;
                if (Affordable(sim, d.Cost)) issue(new TrainCommand(Player, b.Id, d.Id));
                break;
            }
        }
    }

    /// <summary>Beaten monsters: first try to have a unit ride each, and recruit the ones nobody could ride.</summary>
    private void Tame(Simulation sim, Action<Command> issue)
    {
        var u = sim.Units;
        List<int>? ride = null, recruit = null;
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Owner[i] != Player || !u.Subdued[i]) continue;
            int id = u.Id[i];
            if (_rideTried.Contains(id)) (recruit ??= new()).Add(id);
            else
            {
                (ride ??= new()).Add(id);
                _rideTried.Add(id);
            }
        }
        if (ride != null)
            foreach (int id in ride) issue(new TameCommand(Player, new[] { id }, TameChoice.Ride));
        if (recruit != null) issue(new TameCommand(Player, recruit.ToArray(), TameChoice.Recruit));
        if (_rideTried.Count > 64) _rideTried.RemoveAll(id => !u.Exists(id));
    }

    // --- What it sees ------------------------------------------------------------------------------

    /// <summary>Its soldiers (units that fight and are not workers, caravans, ships, beaten monsters or a city-state's guards), by id in index order.</summary>
    /// <remarks>Units pulled back to molt (see <see cref="Molters"/>) are left out until they are ready again.</remarks>
    private List<int> Soldiers(Simulation sim)
    {
        var list = Soldiers(sim, Player);
        if (_molters.Count > 0) list.RemoveAll(_molters.Contains);
        if (_feller != 0) list.Remove(_feller); // felling a tree onto rivals (see AiPlayer.Fell.cs)
        if (_treeParty.Count > 0) list.RemoveAll(_treeParty.Contains); // at the dusk tree (see AiPlayer.DuskTree.cs)
        return list;
    }

    /// <summary>A player's soldiers, by id in index order.</summary>
    private static List<int> Soldiers(Simulation sim, int player)
    {
        var u = sim.Units;
        var list = new List<int>();
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Owner[i] != player || u.Subdued[i] || u.Town[i] != 0 || u.Penned[i] != 0 || u.RunStage[i] != 0) continue;
            var def = sim.Content.Units[u.Def[i]];
            if (def.CanAttack && !def.IsBoat && def.Role is not (UnitRole.Worker or UnitRole.Trader)) list.Add(u.Id[i]);
        }
        return list;
    }

    /// <summary>
    /// True if unit i stands where there is no step to take in any direction: a hole one column wide. Ground
    /// levelled for buildings on a slope can leave one between them, and a soldier standing there when they
    /// went up is shut in for good. (One posted on a lone column of wall is not: see <see cref="Units.UnitMovement.ClimbDown"/>.)
    /// </summary>
    private static bool ShutIn(Simulation sim, int i)
    {
        var g = sim.Walkable;
        var u = sim.Units;
        int node = g.Node(u.X[i].FloorToInt(), u.Z[i].FloorToInt(), u.Y[i]);
        if (node < 0) return false;
        for (int d = 0; d < WalkGraph.Dirs.Length; d++)
            if (g.Step(node, d) >= 0) return false;
        return sim.Economy.WallUnder(i) == null; // posted on a wall, it climbs down when it is sent somewhere
    }

    /// <summary>The units that are not fighting or running: no attack order or target, and not routing.</summary>
    private static List<int> NotFighting(Simulation sim, List<int> ids)
    {
        var u = sim.Units;
        var list = new List<int>();
        foreach (int id in ids)
        {
            int i = u.IndexOf(id);
            // A unit in a shield wall is holding a line under fire (see AiPlayer.ShieldWall.cs): orders would lower it.
            if (i < 0 || u.Rout[i] > 0 || u.Target[i] != 0 || u.TargetBuilding[i] != 0 || u.ShieldWall[i]) continue;
            list.Add(id);
        }
        return list;
    }

    /// <summary>
    /// The units not fighting or running that are not already on their way to column (x, z): standing
    /// about, or headed more than <see cref="Slack"/> columns from it. Orders go only to these, so that units
    /// on their way keep their path.
    /// </summary>
    private static List<int> Unordered(Simulation sim, List<int> ids, int x, int z)
    {
        var u = sim.Units;
        var list = NotFighting(sim, ids);
        list.RemoveAll(id =>
        {
            int i = u.IndexOf(id);
            return u.Order[i] != UnitOrder.Idle && Dist2(u.OrderX[i], u.OrderZ[i], x, z) <= (long)Slack * Slack;
        });
        return list;
    }

    /// <summary>Where to walk to get at a building: the spot in front of its door, or beside it.</summary>
    private static (int X, int Z) Approach(Simulation sim, Building b) =>
        sim.Economy.AccessSpot(b, out int x, out int z, out _) ? (x, z) : (b.X + b.Def.SizeX / 2, b.MaxZ + 2);

    /// <summary>
    /// The nearest unit of another player (not wild, not beaten) within the radius of column (x, z), or -1;
    /// with <paramref name="soldiersOnly"/>, only units that fight on their own (not workers or caravans).
    /// </summary>
    private int NearestEnemy(Simulation sim, int x, int z, int radius, bool soldiersOnly) =>
        NearestEnemy(sim, x, z, radius, soldiersOnly, out _);

    /// <summary>As <see cref="NearestEnemy(Simulation, int, int, int, bool)"/>, also counting the enemies within the radius.</summary>
    private int NearestEnemy(Simulation sim, int x, int z, int radius, bool soldiersOnly, out int count)
    {
        var u = sim.Units;
        int best = -1;
        count = 0;
        long bestD = (long)radius * radius + 1;
        for (int i = 0; i < u.Count; i++)
        {
            int owner = u.Owner[i];
            if (!Rival(owner) || u.Subdued[i]) continue;
            if (Smart && !sim.Vision.SeesUnit(Player, i)) continue; // it plays by the fog of war
            if (soldiersOnly)
            {
                var def = sim.Content.Units[u.Def[i]];
                if (!def.CanAttack || def.Role is UnitRole.Worker or UnitRole.Trader) continue;
            }
            long d = Dist2(u.X[i].FloorToInt(), u.Z[i].FloorToInt(), x, z);
            if (d > (long)radius * radius) continue;
            count++;
            if (d < bestD) (bestD, best) = (d, i);
        }
        return best;
    }

    /// <summary>True if any unit of the wave is within the radius of column (x, z).</summary>
    private bool WaveNear(Simulation sim, int x, int z, int radius)
    {
        var u = sim.Units;
        foreach (int id in _wave)
        {
            int i = u.IndexOf(id);
            if (i >= 0 && Dist2(u.X[i].FloorToInt(), u.Z[i].FloorToInt(), x, z) <= (long)radius * radius) return true;
        }
        return false;
    }

    /// <summary>
    /// An enemy building as the player knows it: where it stands (its middle), where to walk to get at it,
    /// and whether it is finished. The classic player reads these off the game; the smart levels remember
    /// them from when they last saw it (see <see cref="Look"/>).
    /// </summary>
    private readonly record struct Foe(int Id, int Owner, BuildingKind Kind, int X, int Z, int AX, int AZ, bool Active);

    private static Foe AsFoe(Simulation sim, Building b)
    {
        var (mx, mz) = Middle(b);
        var (ax, az) = Approach(sim, b);
        return new Foe(b.Id, b.Owner, b.Def.Kind, mx, mz, ax, az, b.Active);
    }

    /// <summary>The enemy building with this id as the player knows it, or null if there is none (any more).</summary>
    private Foe? Target(Simulation sim, int id)
    {
        if (Smart) return _known.TryGetValue(id, out var k) && Rival(k.Owner) ? k : null;
        return sim.Buildings.Get(id) is { } b && Rival(b.Owner) ? AsFoe(sim, b) : null;
    }

    /// <summary>The nearest town hall of a player still in the match (any other player in a sandbox), or null.</summary>
    private Foe? EnemyHall(Simulation sim, int x, int z)
    {
        if (Smart) return KnownHall(x, z);
        Building? best = null;
        long bestD = long.MaxValue;
        foreach (var b in sim.Buildings.All)
        {
            if (!Rival(b.Owner) || !b.Active || b.Def.Kind != BuildingKind.TownHall) continue;
            if (sim.Match.Players.Count > 0 && !sim.Match.InPlay(b.Owner)) continue;
            var (bx, bz) = Middle(b);
            long d = Dist2(bx, bz, x, z);
            if (d < bestD) (bestD, best) = (d, b);
        }
        return best != null ? AsFoe(sim, best) : null;
    }

    /// <summary>What a wave goes for next: the nearest enemy town hall, or failing that the nearest enemy building.</summary>
    private Foe? NextTarget(Simulation sim, int x, int z)
    {
        if (EnemyHall(sim, x, z) is { } hall) return hall;
        if (Smart) return KnownBuilding(x, z);
        Building? best = null;
        long bestD = long.MaxValue;
        foreach (var b in sim.Buildings.All)
        {
            if (!Rival(b.Owner) || b.Planned) continue;
            var (bx, bz) = Middle(b);
            long d = Dist2(bx, bz, x, z);
            if (d < bestD) (bestD, best) = (d, b);
        }
        return best != null ? AsFoe(sim, best) : null;
    }

    /// <summary>
    /// Where soldiers at home gather: <see cref="RallyDistance"/> columns from the town hall toward the enemy,
    /// or the nearest column to that with dry, open ground and no building on it.
    /// </summary>
    private static (int X, int Z) Rally(Simulation sim, Building hall, (int X, int Z)? enemy)
    {
        var (hx, hz) = Middle(hall);
        int x = hx, z = hall.MaxZ + RallyDistance / 2;
        if (enemy is var (ex, ez))
        {
            long dx = ex - hx, dz = ez - hz;
            long len = Math.Max(1, IntMath.ISqrt(dx * dx + dz * dz));
            (x, z) = (hx + (int)(dx * RallyDistance / len), hz + (int)(dz * RallyDistance / len));
        }
        var w = sim.World;
        for (int r = 0; r <= RallyDistance / 2; r++)
        for (int dz = -r; dz <= r; dz++)
        {
            int step = dz == -r || dz == r ? 1 : 2 * r;
            for (int dx = -r; dx <= r; dx += Math.Max(1, step))
            {
                int cx = x + dx, cz = z + dz;
                if (cx < 1 || cz < 1 || cx >= w.SizeX - 1 || cz >= w.SizeZ - 1 || sim.Buildings.AtColumn(cx, cz) != null) continue;
                int top = w.SurfaceHeight(cx, cz);
                var ground = top < 0 ? BlockType.Air : w.Get(cx, top, cz);
                if (BlockInfo.IsTrunk(ground) || BlockInfo.IsLeaves(ground) || !Walk.CanStand(w, cx, top + 1, cz)
                    || w.Get(cx, top + 1, cz) != BlockType.Air) continue;
                return (cx, cz);
            }
        }
        return (x, z);
    }

    /// <summary>A unit that can walk to fewer than this many places is stranded.</summary>
    private const int StrandedLimit = 64;

    /// <summary>
    /// True if the unit stands where it can walk to fewer than <see cref="StrandedLimit"/> places (a pit, or
    /// a pillar the ground fell away from): no order gets it anywhere.
    /// </summary>
    private static bool Stranded(Simulation sim, int id)
    {
        var u = sim.Units;
        int i = u.IndexOf(id);
        if (i < 0) return false;
        var g = sim.Walkable;
        int start = g.Node(u.X[i].FloorToInt(), u.Z[i].FloorToInt(), u.Y[i]);
        if (start < 0) return false; // falling, or in a block: it may yet land somewhere
        var seen = new HashSet<int> { start };
        var queue = new Queue<int>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            int a = queue.Dequeue();
            for (int d = 0; d < 8; d++)
            {
                int b = g.Step(a, d);
                if (b < 0 || !seen.Add(b)) continue;
                if (seen.Count >= StrandedLimit) return false;
                queue.Enqueue(b);
            }
        }
        return true;
    }

    private static (int X, int Z) Centroid(Simulation sim, List<int> ids)
    {
        var u = sim.Units;
        long sx = 0, sz = 0;
        int n = 0;
        foreach (int id in ids)
        {
            int i = u.IndexOf(id);
            if (i < 0) continue;
            sx += u.X[i].FloorToInt();
            sz += u.Z[i].FloorToInt();
            n++;
        }
        return n == 0 ? (0, 0) : ((int)(sx / n), (int)(sz / n));
    }

    private static (int X, int Z) Middle(Building b) => (b.X + b.Def.SizeX / 2, b.Z + b.Def.SizeZ / 2);

    private static long Dist2(int x0, int z0, int x1, int z1)
    {
        long dx = x1 - x0, dz = z1 - z0;
        return dx * dx + dz * dz;
    }
}
