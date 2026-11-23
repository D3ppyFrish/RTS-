using System;
using System.Collections.Generic;
using Rts.Sim.Automation;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;

namespace Rts.Sim;

/// <summary>
/// The authoritative game state, advanced in fixed ticks. Contains no engine code, so it can be
/// tested headless and run identically on every client.
/// </summary>
public sealed class Simulation
{
    public const int TicksPerSecond = 20;
    /// <summary>
    /// The owner of wild monsters (see <see cref="MonsterSystem"/>): no player plays it, and it fights every
    /// player. Players are numbered below it.
    /// </summary>
    public const int WildPlayer = 7;

    public VoxelWorld World { get; }
    public RegionMap Map { get; }
    public long Tick { get; private set; }
    public ulong Seed { get; }
    public ContentDb Content { get; }
    public UnitStore Units { get; } = new();
    /// <summary>Where units can stand and step, kept in step with the world.</summary>
    public WalkGraph Walkable { get; }
    /// <summary>Flow fields for move orders.</summary>
    public PathService Paths { get; }
    public BuildingStore Buildings { get; } = new();
    /// <summary>Goods, buildings, worker jobs and raids.</summary>
    public EconomySystem Economy { get; }
    /// <summary>Fighting, morale, damage to buildings and sapper tunnels.</summary>
    public CombatSystem Combat { get; }
    /// <summary>Optional automation: workers that find their own jobs, repeating training, retreating soldiers.</summary>
    public AutomationSystem Automation { get; }
    /// <summary>Experience and evolution into new forms (the Settlers').</summary>
    public EvolutionSystem Evolution { get; }
    /// <summary>Wild monsters and their lairs, and taming them (the Orcs').</summary>
    public MonsterSystem Monsters { get; }
    /// <summary>The map's dusk tree, its warden, healing it or hacking it down, and the Dusk Staff.</summary>
    public DuskTreeSystem DuskTree { get; }
    /// <summary>The map's old ruins and what players find searching them.</summary>
    public RuinSystem Ruins { get; }
    /// <summary>Dominion: holding most of the ancient sites (the ruins) to win, with <see cref="Dominion"/> on.</summary>
    public DominionSystem Sites { get; }
    /// <summary>Ascendancy: a faction's great work in the deep caves, standing long enough, wins.</summary>
    public AscendancySystem Ascendancy { get; }
    /// <summary>The dark power under the Heartscar, waking late in the match, with <see cref="Awakening"/> on.</summary>
    public AwakeningSystem DarkPower { get; }
    /// <summary>Relics and the Dusk Staff lying where their bearers fell, until a unit picks them up.</summary>
    public DroppedItemSystem Items { get; }
    /// <summary>Terrain magic: earth walls (see <see cref="SpellSystem"/>).</summary>
    public SpellSystem Spells { get; }
    /// <summary>Boats: sailing on water, units boarding them and being put ashore (see <see cref="BoatSystem"/>).</summary>
    public BoatSystem Boats { get; }
    /// <summary>City-states' towns: their totems and holders, guards, tribute, caravan trade and mercenaries.</summary>
    public CityStateSystem Cities { get; }
    /// <summary>Short truces between rival players: offers, peace, warnings before it ends.</summary>
    public TruceSystem Truces { get; }
    public TradeDealSystem Deals { get; }
    /// <summary>Who is still in the match and who won: a player whose town halls are all gone is out.</summary>
    public MatchSystem Match { get; }
    /// <summary>What each player sees, with fog of war on (<see cref="Fog"/>).</summary>
    public VisionSystem Vision { get; }
    /// <summary>Ground that falls when nothing holds it up, the rubble it leaves, and water that flows.</summary>
    public PhysicsSystem Physics { get; }
    /// <summary>Blood on the ground: trails, stained fields and pools (see <see cref="BloodSystem"/>).</summary>
    public BloodSystem Blood { get; }
    /// <summary>Natural hazards: poison bog and the heat by lava (see <see cref="HazardSystem"/>).</summary>
    public HazardSystem Hazards { get; }
    /// <summary>The time of day, the weather and the light, with <see cref="DayNight"/> on.</summary>
    public Conditions Conditions { get; }
    /// <summary>Test mode's tools and cheats, with <see cref="TestMode"/> on (see <see cref="Sandbox.SandboxSystem"/>).</summary>
    public Sandbox.SandboxSystem Sandbox { get; }
    /// <summary>What players should hear about: attacks, buildings finished, research, training, town halls lost.</summary>
    public EventLog Events { get; }

    private readonly SortedDictionary<long, List<Command>> _scheduled = new();
    private readonly UnitMovement _movement;
    private int _nextGroup = 1;
    private Replay.CommandLog? _recording;
    private bool _fog;
    private bool _dayNight;
    private int _dayStart = Conditions.StartMinute;
    private bool _testMode;
    private bool _dominion;
    private bool _awakening;
    private readonly List<FactionDef?> _factions = new();

    /// <summary>Starts a match on a freshly generated region, with the game's own data unless given other content.</summary>
    public Simulation(ulong seed, int sizeX, int sizeY, int sizeZ, ContentDb? content = null, int treeScale = RegionGenerator.DefaultTreeScale)
        : this(RegionGenerator.Generate(seed, sizeX, sizeY, sizeZ, treeScale), content) { }

    /// <summary>Starts a match on a generated or loaded map. The simulation takes ownership of it.</summary>
    public Simulation(RegionMap map, ContentDb? content = null)
    {
        Map = map;
        Seed = map.Seed;
        World = map.World;
        Content = content ?? ContentDb.Default;
        Walkable = new WalkGraph(World);
        Paths = new PathService(Walkable);
        Paths.GateOwner = Buildings.GateOwner;
        Economy = new EconomySystem(this);
        Combat = new CombatSystem(this);
        Evolution = new EvolutionSystem(this);
        Monsters = new MonsterSystem(this);
        DuskTree = new DuskTreeSystem(this);
        Ruins = new RuinSystem(this);
        Ruins.Place();
        Sites = new DominionSystem(this);
        Ascendancy = new AscendancySystem(this);
        DarkPower = new AwakeningSystem(this);
        Items = new DroppedItemSystem(this);
        Spells = new SpellSystem(this);
        Boats = new BoatSystem(this);
        Cities = new CityStateSystem(this);
        Match = new MatchSystem(this);
        Truces = new TruceSystem(this);
        Deals = new TradeDealSystem(this);
        Automation = new AutomationSystem(this);
        Vision = new VisionSystem(this);
        Physics = new PhysicsSystem(this);
        Blood = new BloodSystem(this);
        Hazards = new HazardSystem(this);
        Events = new EventLog(this);
        Conditions = new Conditions(this);
        Sandbox = new Sandbox.SandboxSystem(this);
        _movement = new UnitMovement(this);
    }

    /// <summary>
    /// Fog of war: each player sees only what their units and buildings can see (see <see cref="VisionSystem"/>).
    /// Off unless set, before the first tick.
    /// </summary>
    public bool Fog
    {
        get => _fog;
        set
        {
            if (value != _fog && Tick != 0) throw new InvalidOperationException("Fog of war is set before the first tick");
            _fog = value;
        }
    }

    /// <summary>
    /// Day, night and weather: units see less far in the dark and in bad weather, ranged shots can miss, and
    /// lanterns and fires light the ground (see <see cref="Sim.Conditions"/>). Off unless set, before the
    /// first tick; off, it is always a clear day.
    /// </summary>
    public bool DayNight
    {
        get => _dayNight;
        set
        {
            if (value != _dayNight && Tick != 0) throw new InvalidOperationException("Day and night are set before the first tick");
            _dayNight = value;
        }
    }

    /// <summary>
    /// Test mode (see <see cref="Sandbox.SandboxSystem"/>): the Sandbox* commands, which place anything
    /// anywhere and turn on cheats, are carried out. Off unless set, before the first tick; off, they are
    /// ignored, so they cannot change a normal match.
    /// </summary>
    public bool TestMode
    {
        get => _testMode;
        set
        {
            if (value != _testMode && Tick != 0) throw new InvalidOperationException("Test mode is set before the first tick");
            _testMode = value;
        }
    }

    /// <summary>
    /// Dominion (see <see cref="DominionSystem"/>): a player also wins by holding more than half the map's ancient
    /// sites (its old ruins) for three minutes. Off unless set, before the first tick; off, the ruins are only
    /// searched for what they hold.
    /// </summary>
    public bool Dominion
    {
        get => _dominion;
        set
        {
            if (value != _dominion && Tick != 0) throw new InvalidOperationException("Dominion is set before the first tick");
            _dominion = value;
        }
    }

    /// <summary>
    /// The Awakening (see <see cref="AwakeningSystem"/>): the dark power under the Heartscar wakes late in the match,
    /// spreading corrupted ground and sending broods of corrupted beasts at every player. Off unless set, before the
    /// first tick; off, it sleeps all match.
    /// </summary>
    public bool Awakening
    {
        get => _awakening;
        set
        {
            if (value != _awakening && Tick != 0) throw new InvalidOperationException("The Awakening is set before the first tick");
            _awakening = value;
        }
    }

    /// <summary>
    /// The faction <paramref name="player"/> plays: the one set with <see cref="SetFaction"/>, else the factions in data
    /// order (player 0 the first, player 1 the second, and so on round). Wild monsters are the wild side
    /// (<see cref="ContentDb.Wild"/>).
    /// </summary>
    public FactionDef FactionOf(int player)
    {
        if (player == WildPlayer) return Content.Wild;
        if (player >= 0 && player < _factions.Count && _factions[player] is { } chosen) return chosen;
        return Content.Factions[player % Content.Factions.Count];
    }

    /// <summary>
    /// Match setup: <paramref name="player"/> plays <paramref name="faction"/> (any faction, so two players can play
    /// the same one). Before the first tick.
    /// </summary>
    public void SetFaction(int player, FactionDef faction)
    {
        if (player < 0 || player >= WildPlayer) throw new ArgumentOutOfRangeException(nameof(player));
        if (faction.Index < 0 || faction.Index >= Content.Factions.Count || Content.Factions[faction.Index] != faction) throw new ArgumentException($"{faction.Id} is not a faction of this game's data", nameof(faction));
        if (Tick != 0 && FactionOf(player) != faction) throw new InvalidOperationException("Factions are set before the first tick");
        while (_factions.Count <= player) _factions.Add(null);
        _factions[player] = faction;
        Economy.Player(player).Faction = faction;
    }

    /// <summary>Match setup: <paramref name="player"/> plays the faction with that id (see <see cref="SetFaction(int, FactionDef)"/>).</summary>
    public void SetFaction(int player, string factionId) => SetFaction(player, Content.Faction(factionId));

    /// <summary>
    /// Test mode: the clock jumps to <paramref name="minute"/> minutes into the day, and the day goes on from
    /// there (it moves the minute the match is taken to have started at).
    /// </summary>
    internal void SetClock(int minute)
    {
        int now = Conditions.MinuteAt(Tick, 0);
        _dayStart = (((minute - now) % 1440) + 1440) % 1440;
    }

    /// <summary>
    /// The minute of the day the match starts at, with <see cref="DayNight"/> on (8:00 unless set, before the
    /// first tick).
    /// </summary>
    public int DayStart
    {
        get => _dayStart;
        set
        {
            if (value != _dayStart && Tick != 0) throw new InvalidOperationException("The time of day is set before the first tick");
            _dayStart = ((value % 1440) + 1440) % 1440;
        }
    }

    /// <summary>
    /// Creates a unit standing on the ground at (x, z) (under any tree canopy). Used by match setup and tests;
    /// during play units come from buildings. Returns the unit id, or -1 if there is no ground there.
    /// </summary>
    public int SpawnUnit(int owner, string unitId, int x, int z, int y = -1)
    {
        var def = Content.Unit(unitId);
        if (y < 0) y = def.IsBoat ? Boats.FloatLevel(x, z) : Walk.GroundLevel(World, x, z);
        if (y < 0) return -1;
        return Units.Spawn(owner, def.Index, Fix64.FromInt(x) + Fix64.Half, y, Fix64.FromInt(z) + Fix64.Half, Economy.MaxHp(owner, def));
    }

    /// <summary>
    /// Match setup: <paramref name="perPlayer"/> units at every start area, cycling through the unit types
    /// matches start with for that player's faction (see <see cref="FactionDef.StartUnits"/>), in rows of ten just south of the
    /// start point (skipping spots with nowhere to stand, like tree trunks). Returns the new ids in spawn order.
    /// </summary>
    public List<int> SpawnStartingUnits(int perPlayer)
    {
        var ids = new List<int>();
        foreach (var start in Map.Starts)
        {
            var defs = FactionOf(start.Player).StartUnits;
            int placed = 0;
            for (int k = 0; placed < perPlayer && k < perPlayer * 4; k++)
            {
                int x = Math.Clamp(start.X - 5 + k % 10, 0, World.SizeX - 1);
                int z = Math.Clamp(start.Z + 4 + k / 10, 0, World.SizeZ - 1);
                int id = SpawnUnit(start.Player, defs[placed % defs.Count].Id, x, z);
                if (id < 0) continue;
                ids.Add(id);
                placed++;
            }
        }
        return ids;
    }

    /// <summary>
    /// Match setup: a finished town hall for every player, centred on their start point, holding the
    /// starting goods (<see cref="EconomySystem.StartingGoods"/>). Each player given one is in the match (see
    /// <see cref="MatchSystem"/>). Call before spawning units there.
    /// </summary>
    public void PlaceStartingBuildings()
    {
        var hall = Content.Building("town_hall");
        foreach (var start in Map.Starts)
        {
            var b = Economy.PlaceFinished(start.Player, hall, start.X - hall.SizeX / 2, start.Z - hall.SizeZ / 2);
            if (b == null) continue;
            foreach (var g in EconomySystem.StartingGoods) b.Stock[(int)g.Good] += g.Amount;
            Match.Join(start.Player);
        }
    }

    /// <summary>Units each player has at the usual start: four of every unit type matches start with.</summary>
    public static int UsualStartUnits(ContentDb content) => 4 * content.StartUnits.Count;

    /// <summary>
    /// Match setup for the usual start: each player's town hall with the starting goods,
    /// <see cref="UsualStartUnits"/> units at every start area, a monster lair on the way from each start to
    /// the map centre and a town for each city-state. <see cref="Replay.CommandLog.UsualStart"/> is the log
    /// header that sets a match up the same way. Returns the starting units' ids.
    /// </summary>
    public List<int> SetUpUsualStart()
    {
        PlaceStartingBuildings();
        var ids = SpawnStartingUnits(UsualStartUnits(Content));
        SpawnLairs();
        SpawnCities();
        return ids;
    }

    /// <summary>
    /// Match setup: a monster lair on the way from every start area to the map centre, with its pack (see
    /// <see cref="MonsterSystem.PlaceAtStarts"/>), and the Dusk Warden by the map's dusk tree (see
    /// <see cref="DuskTreeSystem.Place"/>). Returns the start areas' lairs. Call after placing the starting buildings and units.
    /// </summary>
    public List<Lair> SpawnLairs()
    {
        var lairs = Monsters.PlaceAtStarts();
        DuskTree.Place();
        _lairsSpawned = true;
        return lairs;
    }

    /// <summary>
    /// Match setup: a town for each city-state, about as far from every start area as from the others, each
    /// with a lair of its monster nearby (see <see cref="CityStateSystem.PlaceAll"/>). Call after spawning the
    /// lairs. When the lairs were spawned, the lairs spread across the map follow, away from the towns (see
    /// <see cref="MonsterSystem.PlaceWild"/>), and then those in the cavern (<see cref="MonsterSystem.PlaceCavern"/>).
    /// </summary>
    public List<CityState> SpawnCities()
    {
        var towns = Cities.PlaceAll();
        if (_lairsSpawned)
        {
            Monsters.PlaceWild();
            Monsters.PlaceCavern();
            Monsters.PlaceHerds();
        }
        return towns;
    }

    // SpawnLairs was called: SpawnCities then spreads the wild lairs (they keep away from the towns).
    private bool _lairsSpawned;

    /// <summary>
    /// The match as it is played, when set: each scheduled command is added to this log on the tick it is
    /// carried out, and the log's length keeps up with the clock, so replaying the log plays the match so
    /// far. Automation's own orders are left out, since a replay gives them again. Set it before the first
    /// tick, on a simulation set up as the log's header says (see <see cref="Replay.CommandLog.CreateSimulation"/>).
    /// </summary>
    public Replay.CommandLog? Recording
    {
        get => _recording;
        set
        {
            if (value != null && Tick != 0) throw new InvalidOperationException("Start recording before the first tick");
            _recording = value;
        }
    }

    /// <summary>Queue a command to run at the start of a future tick.</summary>
    public void Schedule(long tick, Command command)
    {
        if (tick < Tick) throw new ArgumentOutOfRangeException(nameof(tick), "Cannot schedule a command in the past");
        if (!_scheduled.TryGetValue(tick, out var list)) _scheduled[tick] = list = new List<Command>();
        list.Add(command);
    }

    public void Step()
    {
        Units.SavePrevious();
        if (_scheduled.Remove(Tick, out var commands))
            foreach (var c in commands)
            {
                _recording?.Add(Tick, c);
                Apply(c);
            }
        Sandbox.Step();
        Truces.Step();
        Deals.Step();
        Conditions.Step();
        Automation.Step();
        Economy.Step();
        Physics.Step();
        Hazards.Step();
        DarkPower.Step();
        Boats.Step();
        Combat.Step();
        Blood.Step();
        Evolution.Step();
        Monsters.Step();
        DuskTree.Step();
        Ruins.Step();
        Items.Step();
        Spells.Step();
        Cities.Step();
        Paths.Step(Units, Tick);
        _movement.Step();
        Sites.Step();
        Ascendancy.Step();
        Match.Step();
        Vision.Step();
        Events.Step();
        Tick++;
        if (_recording != null) _recording.Ticks = Tick;
    }

    public void Run(int ticks)
    {
        for (int i = 0; i < ticks; i++) Step();
    }

    /// <summary>Hash of everything that must match across machines.</summary>
    public ulong StateHash() =>
        World.ComputeHash() ^ Units.ComputeHash() * 0xC2B2AE3D27D4EB4FUL ^ (ulong)Tick * 0x9E3779B97F4A7C15UL
        ^ Buildings.ComputeHash() * 0x165667B19E3779F9UL ^ Economy.ComputeHash() * 0x27D4EB2F165667C5UL
        ^ Combat.ComputeHash() * 0x94D049BB133111EBUL ^ Monsters.ComputeHash() * 0xD6E8FEB86659FD93UL
        ^ Cities.ComputeHash() * 0xA24BAED4963EE407UL ^ Match.ComputeHash() * 0x9FB21C651E98DF25UL
        ^ Truces.ComputeHash() * 0x8CB92BA72F3D8DD7UL ^ Deals.ComputeHash() * 0xE7037ED1A0B428DBUL
        ^ Physics.ComputeHash() * 0x61C8864680B583EBUL
        ^ Blood.ComputeHash() * 0x9E6C63D0676A9A99UL ^ DuskTree.ComputeHash() * 0x3C6EF372FE94F82BUL
        ^ Ruins.ComputeHash() * 0xC6BC279692B5C323UL
        ^ (_dominion ? 0x2B992DDFA23249D6UL ^ Sites.ComputeHash() * 0x9E3779B185EBCA87UL : 0)
        ^ Ascendancy.ComputeHash() * 0xF1357AEA2E62A9C5UL
        ^ (_awakening ? 0x3F84D5B5B5470917UL ^ DarkPower.ComputeHash() * 0x9E3779B97F4A7C55UL : 0)
        ^ Spells.ComputeHash() * 0x2127599BF4325C37UL ^ Items.ComputeHash() * 0x5851F42D4C957F2DUL
        ^ (_fog ? Vision.ComputeHash() * 0xBF58476D1CE4E5B9UL : 0)
        ^ (_testMode ? 0x7A3E1C5B9D2F4E61UL ^ Sandbox.ComputeHash() * 0xE7037ED1A0B428DBUL : 0)
        ^ (_dayNight ? 0x5851F42D4C957F2DUL ^ (ulong)_dayStart * 0x2545F4914F6CDD1DUL ^ Conditions.ComputeHash() * 0xD1B54A32D192ED03UL : 0);

    private void Apply(Command command)
    {
        if (Sandbox.Apply(command)) return;
        // Any other order stops units working on the dusk tree.
        if (command is UnitsCommand released and not (DuskTreeCommand or BloomCommand or StanceCommand or ShieldWallCommand or AutomateCommand or PriorityCommand))
            DuskTree.Release(this, released);
        if (command is UnitsCommand left and not (SearchRuinCommand or BloomCommand or StanceCommand or ShieldWallCommand or AutomateCommand or PriorityCommand))
            Ruins.Release(left);
        if (command is UnitsCommand stopped and not (EarthWallCommand or IceBridgeCommand or ChasmCommand or GrowForestCommand or StanceCommand or ShieldWallCommand or AutomateCommand or PriorityCommand))
            Spells.Release(stopped);
        // An order to go somewhere or fight lowers a shield wall first (see CombatSystem.ShieldWall.cs).
        if (command is UnitsCommand lowered and not (ShieldWallCommand or StanceCommand or AutomateCommand or PriorityCommand or StopCommand or EvolveCommand))
            Combat.LowerWalls(lowered);
        switch (command)
        {
            case DigCommand d when BlockInfo.IsDiggable(World.Get(d.X, d.Y, d.Z)):
                World.Set(d.X, d.Y, d.Z, BlockType.Air);
                break;
            case PlaceCommand p when World.InBounds(p.X, p.Y, p.Z) && World.Get(p.X, p.Y, p.Z) == BlockType.Air:
                World.Set(p.X, p.Y, p.Z, p.Block);
                break;
            case MoveCommand m:
                ApplyMove(m, m.X, m.Z, m.Y, attack: false);
                break;
            case AttackMoveCommand am:
                ApplyMove(am, am.X, am.Z, am.Y, attack: true);
                break;
            case PatrolCommand pa:
                ApplyMove(pa, pa.X, pa.Z, pa.Y, attack: true, patrol: true);
                break;
            case GuardCommand gd: Combat.Apply(gd); break;
            case RallyCommand ra: Economy.Apply(ra); break;
            case FormationMoveCommand fm:
                ApplyFormation(fm);
                break;
            case StopCommand s:
                foreach (int i in OwnedUnits(s))
                {
                    Units.ClearFormation(i);
                    Units.Order[i] = UnitOrder.Idle;
                    Units.Blocked[i] = 0;
                    Units.Field[i] = 0;
                    Units.ClearJob(i);
                    Units.ClearCombat(i);
                }
                break;
            case AttackCommand a: Combat.Apply(a); break;
            case AttackBuildingCommand ab: Combat.Apply(ab); break;
            case TunnelCommand tc: Combat.Apply(tc); break;
            case GatherCommand g: Economy.Apply(g); break;
            case CleanCommand cl: Economy.Apply(cl); break;
            case DigAreaCommand da: Economy.Apply(da); break;
            case RampCommand rc: Economy.Apply(rc); break;
            case LadderCommand lc: Economy.Apply(lc); break;
            case DamCommand dc: Economy.Apply(dc); break;
            case SluiceCommand sl: Economy.Apply(sl); break;
            case WorkCommand w: Economy.Apply(w); break;
            case BuildCommand b: Economy.Apply(b); break;
            case PlanCommand pc: Economy.Apply(pc); break;
            case CancelPlanCommand cp: Economy.Apply(cp); break;
            case CaptureCommand c: Economy.Apply(c); break;
            case BurnCommand b: Economy.Apply(b); break;
            case RoadCommand r: Economy.Apply(r); break;
            case WallCommand wc: Economy.Apply(wc); break;
            case UpgradeCommand up: Economy.Apply(up); break;
            case PostCommand po: Economy.Apply(po); break;
            case BridgeCommand bc: Economy.Apply(bc); break;
            case TrainCommand t: Economy.Apply(t); break;
            case ResearchCommand r: Economy.Apply(r); break;
            case RepeatCommand r: Economy.Apply(r); break;
            case SupplyCommand sp: Economy.Apply(sp); break;
            case StanceCommand st: Combat.Apply(st); break;
            case ShieldWallCommand sw: Combat.Apply(sw); break;
            case FellCommand fl: Economy.Apply(fl); break;
            case ProspectCommand pr: Economy.Apply(pr); break;
            case AutomateCommand a: Automation.Apply(a); break;
            case AutomateAllCommand a: Automation.Apply(a); break;
            case RebuildCommand rb: Automation.Apply(rb); break;
            case GovernCommand gv: Automation.Apply(gv); break;
            case PriorityCommand pr: Automation.Apply(pr); break;
            case DefaultPriorityCommand dp: Automation.Apply(dp); break;
            case EvolveCommand e: Evolution.Apply(e); break;
            case AutoEvolveCommand e: Evolution.Apply(e); break;
            case TameCommand t: Monsters.Apply(t); break;
            case DenCommand d: Monsters.Apply(d); break;
            case DuskTreeCommand dt: DuskTree.Apply(dt); break;
            case BloomCommand bl: DuskTree.Apply(bl); break;
            case SearchRuinCommand sr: Ruins.Apply(sr); break;
            case EarthWallCommand ew: Spells.Apply(ew); break;
            case IceBridgeCommand ib: Spells.Apply(ib); break;
            case ChasmCommand ch: Spells.Apply(ch); break;
            case GrowForestCommand gf: Spells.Apply(gf); break;
            case TradeCommand t: Economy.Apply(t); break;
            case HireCommand h: Cities.Apply(h); break;
            case SurrenderCommand s: Match.Apply(s); break;
            case TruceCommand tr: Truces.Apply(tr); break;
            case EndTruceCommand et: Truces.Apply(et); break;
            case TradeDealCommand td: Deals.Apply(td); break;
            case EndTradeDealCommand ed: Deals.Apply(ed); break;
            case MarketCommand mk: Economy.Apply(mk); break;
            case BoardCommand bo: Boats.Apply(bo); break;
            case UnloadCommand ul: Boats.Apply(ul); break;
        }
        if (command is UnitsCommand uc) Automation.Note(uc);
    }

    /// <summary>Applies a command at once, as if it had been scheduled for this tick (automation gives its orders this way).</summary>
    internal void Issue(Command command) => Apply(command);

    /// <summary>Starts unit i walking alone to level y of column (x, z). Jobs move their units with this.</summary>
    internal void SendUnit(int i, int x, int z, int y)
    {
        if (Content.Units[Units.Def[i]].IsBoat)
        {
            Boats.Sail(i, x, z);
            return;
        }
        var field = Paths.Request(x, z, y, Units.Owner[i]);
        Units.Order[i] = UnitOrder.Move;
        Units.OrderX[i] = x;
        Units.OrderZ[i] = z;
        Units.ClearFormation(i);
        Units.Group[i] = _nextGroup++;
        Units.ArriveRadius[i] = Fix64.FromRatio(3, 10);
        Units.Blocked[i] = 0;
        Units.Field[i] = field.Id;
    }

    /// <summary>
    /// A move order, or with <paramref name="attack"/> an attack-move: the units fight what they meet on the
    /// way (see <see cref="CombatSystem"/>), and with <paramref name="patrol"/> as well a patrol: each unit
    /// walks back to where it stood when it gets there, and so on (a unit already within
    /// <see cref="CombatSystem.PatrolShortest"/> columns of the goal just attack-moves there).
    /// </summary>
    private void ApplyMove(UnitsCommand m, int x, int z, int y, bool attack, bool patrol = false)
    {
        if (x < 0 || z < 0 || x >= World.SizeX || z >= World.SizeZ) return;
        var indexes = OwnedUnits(m);
        SailBoats(indexes, x, z);
        if (indexes.Count == 0) return;
        // Aim for the nearest spot a unit can stand on (a click on water or a wall snaps to the shore or its foot).
        var (gx, gz, gy) = StandingGoal(x, z, y);
        var field = Paths.Request(gx, gz, gy, m.PlayerId);
        int group = _nextGroup++;
        // A crowd needs room: the arrival radius grows with the square root of the group size.
        var radius = Fix64.FromRatio(3, 10) + (Fix64.Sqrt(Fix64.FromInt(indexes.Count)) - Fix64.One) * Fix64.FromRatio(55, 100);
        foreach (int i in indexes)
        {
            Units.ClearJob(i);
            Units.ClearCombat(i);
            Units.ClearFormation(i);
            if (attack)
            {
                Units.Combat[i] = CombatOrder.AttackMove;
                (Units.DestX[i], Units.DestZ[i], Units.DestY[i]) = (gx, gz, gy);
                int sx = Units.X[i].FloorToInt(), sz = Units.Z[i].FloorToInt();
                if (patrol && Math.Max(Math.Abs(sx - gx), Math.Abs(sz - gz)) >= CombatSystem.PatrolShortest)
                {
                    Units.Combat[i] = CombatOrder.Patrol;
                    (Units.PatrolX[i], Units.PatrolZ[i], Units.PatrolY[i]) = (sx, sz, Units.Y[i]);
                }
            }
            Units.Order[i] = UnitOrder.Move;
            Units.OrderX[i] = gx;
            Units.OrderZ[i] = gz;
            Units.Group[i] = group;
            Units.ArriveRadius[i] = radius;
            Units.Blocked[i] = 0;
            Units.Field[i] = field.Id;
        }
    }

    /// <summary>
    /// The boats among a move order's units sail to column (x, z) (or, on land, put whoever is aboard ashore by it; see
    /// <see cref="BoatSystem.Sail"/>), and are taken out of the list: the rest walk.
    /// </summary>
    private void SailBoats(List<int> indexes, int x, int z)
    {
        for (int k = indexes.Count - 1; k >= 0; k--)
        {
            int i = indexes[k];
            if (!Content.Units[Units.Def[i]].IsBoat) continue;
            Boats.Sail(i, x, z);
            indexes.RemoveAt(k);
        }
    }

    /// <summary>How close (blocks) a unit on a formation move comes to its place before it stops.</summary>
    public static readonly Fix64 FormationArrive = Fix64.FromRatio(1, 4);

    /// <summary>
    /// A formation move (see <see cref="Formations"/>): every unit gets its own place, snapped to ground it
    /// can stand on near it, and they all follow one flow field to the formation's middle, each keeping its
    /// place relative to it on the way (see <see cref="UnitMovement"/>), at the slowest unit's speed.
    /// </summary>
    private void ApplyFormation(FormationMoveCommand f)
    {
        int last = World.SizeX - 1, lastZ = World.SizeZ - 1;
        if (f.X1 < 0 || f.Z1 < 0 || f.X1 > last || f.Z1 > lastZ || f.X2 < 0 || f.Z2 < 0 || f.X2 > last || f.Z2 > lastZ) return;
        var indexes = OwnedUnits(f);
        SailBoats(indexes, (f.X1 + f.X2) / 2, (f.Z1 + f.Z2) / 2);
        if (indexes.Count == 0) return;
        var members = new Formations.Member[indexes.Count];
        Fix64 cap = Fix64.Zero;
        for (int k = 0; k < indexes.Count; k++)
        {
            int i = indexes[k];
            var def = Content.Units[Units.Def[i]];
            var rank = !def.CanAttack ? FormationRank.Support : def.IsRanged ? FormationRank.Ranged : FormationRank.Melee;
            members[k] = new Formations.Member(Units.Id[i], Units.X[i], Units.Z[i], rank);
            var speed = Economy.SpeedPerTick(Units.Owner[i], def);
            if (k == 0 || speed < cap) cap = speed;
        }
        Fix64 half = Fix64.Half;
        Fix64 x1 = Fix64.FromInt(f.X1) + half, z1 = Fix64.FromInt(f.Z1) + half;
        Fix64 x2 = Fix64.FromInt(f.X2) + half, z2 = Fix64.FromInt(f.Z2) + half;
        bool line = f.X1 != f.X2 || f.Z1 != f.Z2;
        var slots = line
            ? Formations.Line(members, x1, z1, x2, z2, out var faceX, out var faceZ)
            : Formations.KeepShape(members, x1, z1, out faceX, out faceZ);

        // The formation's middle, where its flow field leads.
        var (gx, gz, gy) = StandingGoal((f.X1 + f.X2) / 2, (f.Z1 + f.Z2) / 2, f.Y);
        var field = Paths.Request(gx, gz, gy, f.PlayerId);
        int group = _nextGroup++;
        for (int k = 0; k < indexes.Count; k++)
        {
            int i = indexes[k];
            var (sx, sz) = StandingPlace(slots[k].X, slots[k].Z, gy, gx, gz);
            Units.ClearJob(i);
            Units.ClearCombat(i);
            Units.ClearFormation(i);
            if (f.Attack)
            {
                Units.Combat[i] = CombatOrder.AttackMove;
                (Units.DestX[i], Units.DestZ[i], Units.DestY[i]) = (gx, gz, gy);
            }
            Units.Order[i] = UnitOrder.Move;
            Units.OrderX[i] = gx;
            Units.OrderZ[i] = gz;
            Units.Group[i] = group;
            Units.ArriveRadius[i] = FormationArrive;
            Units.Blocked[i] = 0;
            Units.Field[i] = field.Id;
            Units.Formation[i] = true;
            Units.SlotX[i] = sx;
            Units.SlotZ[i] = sz;
            Units.FaceX[i] = faceX;
            Units.FaceZ[i] = faceZ;
            Units.SpeedCap[i] = cap;
        }
    }

    /// <summary>
    /// A formation place a unit can stand on: (x, z) itself if its column has ground within a few levels of
    /// <paramref name="y"/> (the formation's middle), otherwise the middle of the nearest such column within
    /// 3 blocks, otherwise the formation's middle (gx, gz).
    /// </summary>
    private (Fix64 X, Fix64 Z) StandingPlace(Fix64 x, Fix64 z, int y, int gx, int gz)
    {
        const int maxClimb = 3, range = 3;
        int cx = x.FloorToInt(), cz = z.FloorToInt();
        bool Fits(int px, int pz)
        {
            int node = Walkable.NearestNode(px, pz, y);
            return node >= 0 && Math.Abs(Walkable.Level(node) - y) <= maxClimb;
        }
        if (Fits(cx, cz)) return (x, z);
        int bestD2 = range * range + 1, bx = gx, bz = gz;
        for (int dz = -range; dz <= range; dz++)
        for (int dx = -range; dx <= range; dx++)
        {
            int d2 = dx * dx + dz * dz;
            if (d2 >= bestD2 || !Fits(cx + dx, cz + dz)) continue;
            (bestD2, bx, bz) = (d2, cx + dx, cz + dz);
        }
        return (Fix64.FromInt(bx) + Fix64.Half, Fix64.FromInt(bz) + Fix64.Half);
    }

    /// <summary>
    /// The standing spot for a move target: level y (or the top) of column (x, z) if a unit can stand
    /// there, otherwise the closest column within 8 blocks that has ground.
    /// </summary>
    internal (int X, int Z, int Y) StandingGoal(int x, int z, int y)
    {
        int best = Walkable.NearestNode(x, z, y);
        if (best >= 0) return (x, z, Walkable.Level(best));
        const int range = 8;
        int bestD2 = range * range + 1;
        for (int dz = -range; dz <= range; dz++)
        for (int dx = -range; dx <= range; dx++)
        {
            int d2 = dx * dx + dz * dz;
            if (d2 >= bestD2) continue;
            int node = Walkable.NearestNode(x + dx, z + dz, y);
            if (node < 0) continue;
            best = node;
            bestD2 = d2;
        }
        return best >= 0 ? (Walkable.ColumnX(best), Walkable.ColumnZ(best), Walkable.Level(best)) : (x, z, y);
    }

    /// <summary>
    /// Indexes of the command's units that exist and belong to the commanding player, without duplicates, in
    /// the command's order. Routing units are left out: they do not take orders until they rally. So are
    /// beaten monsters, until the player has them ridden or recruited (see <see cref="MonsterSystem"/>), and a
    /// city-state town's guards and tribute caravans, which go about their own business (see <see cref="CityStateSystem"/>),
    /// and units aboard a boat, until it puts them ashore (see <see cref="BoatSystem"/>).
    /// </summary>
    /// <summary>Unit i's most hit points: its type's for its owner, and the Dusk Staff's and its relic's if it bears them.</summary>
    public int MaxHpOf(int i) =>
        Economy.MaxHp(Units.Owner[i], Content.Units[Units.Def[i]]) + (DuskTree.HasStaff(i) ? DuskTreeSystem.StaffHp : 0)
        + (RelicOf(i)?.Hp ?? 0);

    /// <summary>The relic unit i bears (a lair's prize, see <see cref="MonsterSystem"/>), or null.</summary>
    public Content.RelicDef? RelicOf(int i) => Units.Relic[i] > 0 ? Content.Relics[Units.Relic[i] - 1] : null;

    internal List<int> OwnedUnits(UnitsCommand c)
    {
        var result = new List<int>(c.Units.Length);
        var seen = new HashSet<int>();
        foreach (int id in c.Units)
        {
            int i = Units.IndexOf(id);
            if (i >= 0 && Units.Owner[i] == c.PlayerId && Units.Rout[i] == 0 && !Units.Subdued[i] && Units.Town[i] == 0 && Units.Aboard[i] == 0 && seen.Add(id)) result.Add(i);
        }
        return result;
    }
}
