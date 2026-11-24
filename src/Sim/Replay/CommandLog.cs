using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Linq;
using Rts.Sim.Automation;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.Replay;

/// <summary>One command scheduled on one tick.</summary>
public readonly record struct LoggedCommand(long Tick, Command Command);

/// <summary>
/// Everything needed to reproduce a match: seed, world size, length and every command in order.
/// Because the simulation is deterministic, a command log is also a replay.
///
/// Text format, one entry per line (lines starting with # are comments):
/// <code>
/// rts-log 1
/// seed 1234
/// world 512 64 512
/// content 3f2a9c0d81e4b7a6    (optional: hash of the game data it was played with)
/// ticks 12000
/// start-units 100
/// 40 dig 0 10 12 30
/// 41 place 1 10 13 30 Stone
/// 42 move 0 200 260 1,2,3
/// 42 move 0 200 260 4,5 17      (optional last field: the level to stand at, e.g. inside a tunnel)
/// 43 stop 1 101,102
/// 44 gather 0 30 12 41 1,2          (dig, chop or pick block (30, 12, 41))
/// 45 build 0 house 60 70 3,4        (place a house with its corner at column (60, 70); units 3 and 4 build it)
/// 46 work 0 7 5,6                   (work at building 7: build, farm, supply a smelter, unload, or start a plan)
/// 46 plan 0 house 60 70             (plan a house with its corner at column (60, 70), not yet paid for)
/// 46 unplan 0 7                     (remove plan 7)
/// 47 capture 1 2 101,102            (or burn: raid building 2)
/// 48 road 0 10 10 30 10
/// 49 train 0 1 worker               (queue a worker at building 1)
/// 50 research 0 farming
/// 51 attack 0 57 1,2,3               (attack unit 57)
/// 52 attack-building 0 4 1,2,3      (attack building 4)
/// 53 attack-move 0 200 260 1,2,3    (optional last field: the level, as for move)
/// 54 tunnel 0 30 9 41 8,9           (sappers dig a tunnel to column (30, 41), feet at level 9)
/// 55 automate 0 on 5,6              (workers 5 and 6 find their own jobs; off: they wait for orders)
/// 56 automate-all 0 on              (all of player 0's workers, and those trained later)
/// 57 repeat 0 1 on                  (building 1 repeats its training)
/// 57 governor 0 1 army             (a governor runs town hall 1 for an army; none, growth, army or defence)
/// 58 stance 0 hold 1,2,3            (hold, aggressive or retreat)
/// 59 shieldwall 0 on 1,2,3          (a shield wall raised; off: lowered)
/// 59 charge 1 7                     (player 1's war drummer 7 beats the charge)
/// 59 pen 1 12 7,8                   (player 1's beasts 7 and 8 go into beast pen 12)
/// 59 stampede 1 12 40 50 60 50      (player 1 looses beast pen 12's beasts along the line from 40,50 to 60,50)
/// 59 unpen 1 12                     (player 1 lets beast pen 12's beasts out)
/// 59 fell 0 40 52 1 0 7             (unit 7 fells the tree at 40,52 to fall towards +x)
/// 59 prospect 0 40 52 8             (unit 8 sounds the ground at 40,52)
/// 59 evolve 0 knight 1,2            (units 1 and 2 take the form "knight", if they can)
/// 60 auto-evolve 0 spearman knight  (player 0's spearmen become knights on their own; off: the player picks)
/// 61 tame 1 ride 90,91              (player 1's beaten monsters 90 and 91 are ridden; recruit: they join the army)
/// 62 den 1 2 5,6                   (player 1 uses cleared den 2, with units 5 and 6 near it)
/// 63 trade 0 1 7,8                  (caravans 7 and 8 trade with town 1)
/// 64 market 0 12 7,8                 (caravans 7 and 8 trade at a deal partner's town hall, building 12)
/// 64 hire 0 1 goblin_slinger        (player 0, who holds town 1, hires a goblin slinger there)
/// 65 surrender 1                    (player 1 gives up the match)
/// </code>
/// Test mode's commands (carried out only with "test-mode 1", see <see cref="Simulation.TestMode"/>):
/// <code>
/// 70 sandbox-unit 0 7 wolf 120 80 3       (player 0 puts 3 wolves of the wild player at column (120, 80))
/// 71 sandbox-building 0 1 barracks 90 60  (a finished barracks of player 1's with its corner at (90, 60))
/// 72 sandbox-tree 0 Pine 100 70
/// 73 sandbox-blocks 0 Water 10 20 10 14 22 14   (fill a box; Air clears it)
/// 74 sandbox-lair 0 wolf 150 150
/// 75 sandbox-blood 0 100 100 3 120        (radius 3, 120 on each column)
/// 76 sandbox-weather 0 Storm 2            (a storm for two hours)
/// 77 sandbox-time 0 1260                  (the clock jumps to 21:00)
/// 78 sandbox-goods 0 0 500                (500 more of every good for player 0)
/// 79 sandbox-tech 0 0 all                 (every tech, or one by id)
/// 80 sandbox-cheat 0 0 Invulnerable on    (InstantBuild, InstantTrain, InstantResearch or Invulnerable)
/// 81 sandbox-units 0 Heal 5,6             (Heal, Kill, Remove, Bleed or Veteran)
/// 82 sandbox-clear 0 100 100 4            (remove the units and buildings within 4 columns)
/// </code>
/// "start-units" (optional, default 0) spawns that many units per start area before the first tick,
/// "start-buildings 1" (optional) first places each player's town hall with the starting goods,
/// "start-lairs 1" (optional) then puts a monster lair on the way from each start to the map centre, and
/// "start-cities 1" (optional) then places a town for each city-state, and "fog 1" (optional) plays it with
/// fog of war (see <see cref="Simulation.Fog"/>), and "daynight 1" (optional) with day, night and weather
/// (see <see cref="Simulation.DayNight"/>), starting at minute M of the day with "daystart M" (optional, 480 if not given), and
/// "test-mode 1" (optional) in test mode, "dominion 1" (optional) with dominion, the ancient sites' win (see
/// <see cref="Simulation.Dominion"/>), and "trees N" for trees N times the first size (optional, 1 if not
/// given, see <see cref="VoxelWorld.TreeScale"/>), and "faction P id" (optional, one line a player) has player P play the
/// faction with that id (see <see cref="Simulation.SetFaction(int, string)"/>; players without one play the factions in data
/// order). A log with a "content" line only replays with the same data
/// (see <see cref="ContentDb.Hash"/>).
/// </summary>
public sealed class CommandLog
{
    public const int FormatVersion = 1;

    public ulong Seed { get; init; }
    public int SizeX { get; init; }
    public int SizeY { get; init; }
    public int SizeZ { get; init; }
    public long Ticks { get; set; }
    /// <summary>Units each player starts with (see <see cref="Simulation.SpawnStartingUnits"/>).</summary>
    public int StartUnits { get; init; }
    /// <summary>Each player starts with a town hall (see <see cref="Simulation.PlaceStartingBuildings"/>).</summary>
    public bool StartBuildings { get; init; }
    /// <summary>The match has a monster lair for each start area (see <see cref="Simulation.SpawnLairs"/>).</summary>
    public bool StartLairs { get; init; }
    /// <summary>The match has a town for each city-state (see <see cref="Simulation.SpawnCities"/>).</summary>
    public bool StartCities { get; init; }
    /// <summary>The match is played with fog of war (see <see cref="Simulation.Fog"/>).</summary>
    public bool Fog { get; init; }
    /// <summary>The match is played with day, night and weather (see <see cref="Simulation.DayNight"/>).</summary>
    public bool DayNight { get; init; }
    /// <summary>The minute of the day the match starts at (see <see cref="Simulation.DayStart"/>).</summary>
    public int DayStart { get; init; } = Conditions.StartMinute;
    /// <summary>The match is played in test mode (see <see cref="Simulation.TestMode"/>).</summary>
    public bool TestMode { get; init; }
    /// <summary>The match is played with dominion, the ancient sites' win (see <see cref="Simulation.Dominion"/>).</summary>
    public bool Dominion { get; init; }
    /// <summary>The match is played with the Awakening, the dark power waking late on (see <see cref="Simulation.Awakening"/>).</summary>
    public bool Awakening { get; init; }
    /// <summary>
    /// How big the map's trees grow (see <see cref="VoxelWorld.TreeScale"/>): "trees N" in the log, and 1, the
    /// first trees, for logs recorded before trees grew bigger.
    /// </summary>
    public int TreeScale { get; init; } = 1;
    /// <summary>
    /// The faction each player plays, by player ("faction P id" in the log): null, or a null entry, for the faction
    /// in data order (see <see cref="Simulation.FactionOf"/>).
    /// </summary>
    public IReadOnlyList<string?>? Factions { get; init; }
    /// <summary>Hash of the game data the match was played with, if recorded.</summary>
    public ulong? ContentHash { get; init; }
    public List<LoggedCommand> Commands { get; } = new();

    /// <summary>
    /// The header for a match from the usual start (see <see cref="Simulation.SetUpUsualStart"/>) on a
    /// generated map, with the game's own data unless given other content. Add the commands as the match is
    /// played (see <see cref="Simulation.Recording"/>).
    /// </summary>
    public static CommandLog UsualStart(ulong seed, int sizeX, int sizeY, int sizeZ, ContentDb? content = null, bool fog = false, bool dayNight = false,
        int dayStart = Conditions.StartMinute, bool testMode = false, int treeScale = Rts.Sim.World.RegionGenerator.DefaultTreeScale,
        bool dominion = false, bool awakening = false, IReadOnlyList<string?>? factions = null)
    {
        content ??= ContentDb.Default;
        return new CommandLog
        {
            Seed = seed, SizeX = sizeX, SizeY = sizeY, SizeZ = sizeZ, StartUnits = Simulation.UsualStartUnits(content),
            StartBuildings = true, StartLairs = true, StartCities = true, ContentHash = content.Hash, Fog = fog, DayNight = dayNight, DayStart = dayStart,
            TestMode = testMode, TreeScale = treeScale, Dominion = dominion, Awakening = awakening,
            Factions = factions is { Count: > 0 } ? factions.ToArray() : null,
        };
    }

    /// <summary>
    /// A fresh simulation set up for this match, with the game's own data unless given other content.
    /// Throws if the log was recorded with different data, which would not replay the same.
    /// </summary>
    public Simulation CreateSimulation(ContentDb? content = null)
    {
        content ??= ContentDb.Default;
        if (ContentHash is { } hash && hash != content.Hash)
            throw new InvalidOperationException(
                $"This match was played with different game data (content {hash:x16}, this game has {content.Hash:x16})");
        var sim = new Simulation(Seed, SizeX, SizeY, SizeZ, content, TreeScale) { Fog = Fog, DayNight = DayNight, DayStart = DayStart, TestMode = TestMode,
            Dominion = Dominion, Awakening = Awakening };
        if (Factions != null)
            for (int p = 0; p < Factions.Count; p++)
                if (Factions[p] is { } id)
                {
                    if (!content.TryGetFaction(id, out var faction))
                        throw new InvalidOperationException($"This match was played with a faction this game does not have: {id}");
                    sim.SetFaction(p, faction);
                }
        if (StartBuildings) sim.PlaceStartingBuildings();
        if (StartUnits > 0) sim.SpawnStartingUnits(StartUnits);
        if (StartLairs) sim.SpawnLairs();
        if (StartCities) sim.SpawnCities();
        return sim;
    }

    public void Add(long tick, Command command)
    {
        if (Commands.Count > 0 && tick < Commands[^1].Tick)
            throw new ArgumentException("Commands must be added in tick order", nameof(tick));
        Commands.Add(new LoggedCommand(tick, command));
    }

    public void Write(TextWriter w)
    {
        var ci = CultureInfo.InvariantCulture;
        w.WriteLine($"rts-log {FormatVersion}");
        w.WriteLine(string.Create(ci, $"seed {Seed}"));
        w.WriteLine(string.Create(ci, $"world {SizeX} {SizeY} {SizeZ}"));
        if (ContentHash is { } hash) w.WriteLine(string.Create(ci, $"content {hash:x16}"));
        w.WriteLine(string.Create(ci, $"ticks {Ticks}"));
        if (StartUnits > 0) w.WriteLine(string.Create(ci, $"start-units {StartUnits}"));
        if (StartBuildings) w.WriteLine("start-buildings 1");
        if (StartLairs) w.WriteLine("start-lairs 1");
        if (StartCities) w.WriteLine("start-cities 1");
        if (Fog) w.WriteLine("fog 1");
        if (DayNight) w.WriteLine("daynight 1");
        if (DayStart != Conditions.StartMinute) w.WriteLine($"daystart {DayStart}");
        if (TestMode) w.WriteLine("test-mode 1");
        if (Dominion) w.WriteLine("dominion 1");
        if (Awakening) w.WriteLine("awakening 1");
        if (TreeScale != 1) w.WriteLine(string.Create(ci, $"trees {TreeScale}"));
        if (Factions != null)
            for (int p = 0; p < Factions.Count; p++)
                if (Factions[p] is { } id) w.WriteLine(string.Create(ci, $"faction {p} {id}"));
        foreach (var (tick, cmd) in Commands)
        {
            w.WriteLine(cmd switch
            {
                DigCommand d => string.Create(ci, $"{tick} dig {d.PlayerId} {d.X} {d.Y} {d.Z}"),
                PlaceCommand p => string.Create(ci, $"{tick} place {p.PlayerId} {p.X} {p.Y} {p.Z} {p.Block}"),
                MoveCommand m => m.Y < 0
                    ? string.Create(ci, $"{tick} move {m.PlayerId} {m.X} {m.Z} {Ids(m.Units)}")
                    : string.Create(ci, $"{tick} move {m.PlayerId} {m.X} {m.Z} {Ids(m.Units)} {m.Y}"),
                FormationMoveCommand fm => string.Create(ci,
                    $"{tick} {(fm.Attack ? "attack-formation" : "formation")} {fm.PlayerId} {fm.X1} {fm.Z1} {fm.X2} {fm.Z2} {Ids(fm.Units)}{(fm.Y < 0 ? "" : $" {fm.Y}")}"),
                StopCommand s => string.Create(ci, $"{tick} stop {s.PlayerId} {Ids(s.Units)}"),
                GatherCommand g => string.Create(ci, $"{tick} gather {g.PlayerId} {g.X} {g.Y} {g.Z} {Ids(g.Units)}"),
                CleanCommand cl => string.Create(ci, $"{tick} clean {cl.PlayerId} {cl.X} {cl.Z} {Ids(cl.Units)}"),
                BuildCommand b => string.Create(ci, $"{tick} build {b.PlayerId} {Name(b.Building)} {b.X} {b.Z} {Ids(b.Units)}{(b.Y >= 0 ? $" {b.Y}" : "")}"),
                PlanCommand pl => string.Create(ci, $"{tick} plan {pl.PlayerId} {Name(pl.Building)} {pl.X} {pl.Z}{(pl.Y >= 0 ? $" {pl.Y}" : "")}"),
                CancelPlanCommand cp => string.Create(ci, $"{tick} unplan {cp.PlayerId} {cp.Building}"),
                WorkCommand wk => string.Create(ci, $"{tick} work {wk.PlayerId} {wk.Building} {Ids(wk.Units)}"),
                CaptureCommand c => string.Create(ci, $"{tick} capture {c.PlayerId} {c.Building} {Ids(c.Units)}"),
                BurnCommand bn => string.Create(ci, $"{tick} burn {bn.PlayerId} {bn.Building} {Ids(bn.Units)}"),
                WallCommand wc => string.Create(ci, $"{tick} wall {wc.PlayerId} {Name(wc.Building)} {wc.X1} {wc.Z1} {wc.X2} {wc.Z2} {Ids(wc.Units)}"),
                BridgeCommand bc => string.Create(ci, $"{tick} bridge {bc.PlayerId} {Name(bc.Building)} {bc.X1} {bc.Z1} {bc.X2} {bc.Z2} {Ids(bc.Units)}"),
                UpgradeCommand up => string.Create(ci, $"{tick} upgrade {up.PlayerId} {up.Building} {(up.Stretch ? "stretch" : "one")} {Ids(up.Units)}"),
                PostCommand po => string.Create(ci, $"{tick} post {po.PlayerId} {po.Building} {Ids(po.Units)}"),
                BoardCommand bo => string.Create(ci, $"{tick} board {bo.PlayerId} {bo.Boat} {Ids(bo.Units)}"),
                UnloadCommand ul => string.Create(ci, $"{tick} unload {ul.PlayerId} {Ids(ul.Units)}"),
                RoadCommand r => string.Create(ci, $"{tick} road {r.PlayerId} {r.X1} {r.Z1} {r.X2} {r.Z2}"),
                TrainCommand t => string.Create(ci, $"{tick} train {t.PlayerId} {t.Building} {Name(t.Unit)}"),
                ResearchCommand rs => string.Create(ci, $"{tick} research {rs.PlayerId} {Name(rs.Tech)}"),
                AttackCommand a => string.Create(ci, $"{tick} attack {a.PlayerId} {a.Target} {Ids(a.Units)}"),
                AttackBuildingCommand ab => string.Create(ci, $"{tick} attack-building {ab.PlayerId} {ab.Building} {Ids(ab.Units)}"),
                AttackMoveCommand am => am.Y < 0
                    ? string.Create(ci, $"{tick} attack-move {am.PlayerId} {am.X} {am.Z} {Ids(am.Units)}")
                    : string.Create(ci, $"{tick} attack-move {am.PlayerId} {am.X} {am.Z} {Ids(am.Units)} {am.Y}"),
                PatrolCommand pa => pa.Y < 0
                    ? string.Create(ci, $"{tick} patrol {pa.PlayerId} {pa.X} {pa.Z} {Ids(pa.Units)}")
                    : string.Create(ci, $"{tick} patrol {pa.PlayerId} {pa.X} {pa.Z} {Ids(pa.Units)} {pa.Y}"),
                GuardCommand gd => string.Create(ci, $"{tick} guard {gd.PlayerId} {gd.Unit} {gd.Building} {Ids(gd.Units)}"),
                RallyCommand ra => string.Create(ci, $"{tick} rally {ra.PlayerId} {ra.Building} {ra.X} {ra.Z} {ra.Y}"),
                RampCommand rc => string.Create(ci, $"{tick} ramp {rc.PlayerId} {rc.X1} {rc.Z1} {rc.X2} {rc.Z2} {Ids(rc.Units)}"),
                LadderCommand lc => string.Create(ci, $"{tick} ladder {lc.PlayerId} {lc.X1} {lc.Z1} {lc.X2} {lc.Z2} {Ids(lc.Units)}"),
                DamCommand dc => string.Create(ci, $"{tick} dam {dc.PlayerId} {dc.X1} {dc.Z1} {dc.X2} {dc.Z2} {Ids(dc.Units)}"),
                SluiceCommand sl => string.Create(ci, $"{tick} sluice {sl.PlayerId} {sl.Dam} {OnOff(sl.Open)}"),
                DigAreaCommand da => string.Create(ci, $"{tick} dig-area {da.PlayerId} {da.X1} {da.Z1} {da.X2} {da.Z2} {da.Y} {Ids(da.Units)}"),
                TunnelCommand tn => string.Create(ci, $"{tick} tunnel {tn.PlayerId} {tn.X} {tn.Y} {tn.Z} {Ids(tn.Units)}"),
                ShieldWallCommand sw => string.Create(ci, $"{tick} shieldwall {sw.PlayerId} {OnOff(sw.Raise)} {Ids(sw.Units)}"),
                ChargeCommand ch => string.Create(ci, $"{tick} charge {ch.PlayerId} {Ids(ch.Units)}"),
                FellCommand fl => string.Create(ci, $"{tick} fell {fl.PlayerId} {fl.X} {fl.Z} {fl.DirX} {fl.DirZ} {Ids(fl.Units)}"),
                ProspectCommand pr => string.Create(ci, $"{tick} prospect {pr.PlayerId} {pr.X} {pr.Z} {Ids(pr.Units)}"),
                RepairCommand rp => string.Create(ci, $"{tick} repair {rp.PlayerId} {rp.Building} {Ids(rp.Units)}"),
                PenCommand pn => string.Create(ci, $"{tick} pen {pn.PlayerId} {pn.Building} {Ids(pn.Units)}"),
                StampedeCommand sd => string.Create(ci, $"{tick} stampede {sd.PlayerId} {sd.Building} {sd.X1} {sd.Z1} {sd.X2} {sd.Z2}"),
                UnpenCommand up => string.Create(ci, $"{tick} unpen {up.PlayerId} {up.Building}"),
                AutomateCommand au => string.Create(ci, $"{tick} automate {au.PlayerId} {OnOff(au.On)} {Ids(au.Units)}"),
                AutomateAllCommand aa => string.Create(ci, $"{tick} automate-all {aa.PlayerId} {OnOff(aa.On)}"),
                GovernCommand gv => string.Create(ci, $"{tick} governor {gv.PlayerId} {gv.Building} {gv.Focus.ToString().ToLowerInvariant()}"),
                RebuildCommand rb => string.Create(ci, $"{tick} rebuild {rb.PlayerId} {OnOff(rb.On)}"),
                PriorityCommand pr => string.Create(ci, $"{tick} priority {pr.PlayerId} {WorkPriorities.ToLog(pr.List)} {Ids(pr.Units)}"),
                DefaultPriorityCommand dp => string.Create(ci, $"{tick} default-priority {dp.PlayerId} {WorkPriorities.ToLog(dp.List)}"),
                RepeatCommand rp => string.Create(ci, $"{tick} repeat {rp.PlayerId} {rp.Building} {OnOff(rp.On)}"),
                SupplyCommand sp => string.Create(ci, $"{tick} supply {sp.PlayerId} {sp.Building} {sp.Good} {sp.Amount}"),
                StanceCommand st => string.Create(ci, $"{tick} stance {st.PlayerId} {StanceName(st.Stance)} {Ids(st.Units)}"),
                EvolveCommand ev => string.Create(ci, $"{tick} evolve {ev.PlayerId} {ev.Form} {Ids(ev.Units)}"),
                AutoEvolveCommand ae => string.Create(ci, $"{tick} auto-evolve {ae.PlayerId} {ae.Unit} {ae.Form ?? "off"}"),
                TameCommand tm => string.Create(ci, $"{tick} tame {tm.PlayerId} {ChoiceName(tm.Choice)} {Ids(tm.Units)}"),
                TradeCommand tr => string.Create(ci, $"{tick} trade {tr.PlayerId} {tr.City} {Ids(tr.Units)}"),
                MarketCommand mk => string.Create(ci, $"{tick} market {mk.PlayerId} {mk.Hall} {Ids(mk.Units)}"),
                DenCommand dn => string.Create(ci, $"{tick} den {dn.PlayerId} {dn.Lair} {Ids(dn.Units)}"),
                DuskTreeCommand dt => string.Create(ci, $"{tick} dusk-tree {dt.PlayerId} {Ids(dt.Units)}"),
                BloomCommand bl => string.Create(ci, $"{tick} bloom {bl.PlayerId} {Ids(bl.Units)}"),
                SearchRuinCommand sr => string.Create(ci, $"{tick} search-ruin {sr.PlayerId} {sr.Ruin} {Ids(sr.Units)}"),
                EarthWallCommand ew => string.Create(ci, $"{tick} earthwall {ew.PlayerId} {ew.X1} {ew.Z1} {ew.X2} {ew.Z2} {Ids(ew.Units)}"),
                IceBridgeCommand ib => string.Create(ci, $"{tick} icebridge {ib.PlayerId} {ib.X1} {ib.Z1} {ib.X2} {ib.Z2} {Ids(ib.Units)}"),
                ChasmCommand ch => string.Create(ci, $"{tick} chasm {ch.PlayerId} {ch.X1} {ch.Z1} {ch.X2} {ch.Z2} {Ids(ch.Units)}"),
                GrowForestCommand gf => string.Create(ci, $"{tick} growforest {gf.PlayerId} {gf.X1} {gf.Z1} {gf.X2} {gf.Z2} {Ids(gf.Units)}"),
                HireCommand hr => string.Create(ci, $"{tick} hire {hr.PlayerId} {hr.City} {Name(hr.Unit)}"),
                SurrenderCommand su => string.Create(ci, $"{tick} surrender {su.PlayerId}"),
                TruceCommand tr => string.Create(ci, $"{tick} truce {tr.PlayerId} {tr.Other}"),
                EndTruceCommand et => string.Create(ci, $"{tick} endtruce {et.PlayerId} {et.Other}"),
                TradeDealCommand td => string.Create(ci, $"{tick} deal {td.PlayerId} {td.Other}"),
                EndTradeDealCommand ed => string.Create(ci, $"{tick} enddeal {ed.PlayerId} {ed.Other}"),
                SandboxUnitCommand x => string.Create(ci, $"{tick} sandbox-unit {x.PlayerId} {x.Owner} {Name(x.Unit)} {x.X} {x.Z} {x.Count}"),
                SandboxBuildingCommand x => string.Create(ci, $"{tick} sandbox-building {x.PlayerId} {x.Owner} {Name(x.Building)} {x.X} {x.Z}"),
                SandboxTreeCommand x => string.Create(ci, $"{tick} sandbox-tree {x.PlayerId} {EnumName(x.Kind)} {x.X} {x.Z}"),
                SandboxBlocksCommand x => string.Create(ci,
                    $"{tick} sandbox-blocks {x.PlayerId} {EnumName(x.Block)} {x.X1} {x.Y1} {x.Z1} {x.X2} {x.Y2} {x.Z2}"),
                SandboxLairCommand x => string.Create(ci, $"{tick} sandbox-lair {x.PlayerId} {Name(x.Monster)} {x.X} {x.Z}"),
                SandboxBloodCommand x => string.Create(ci, $"{tick} sandbox-blood {x.PlayerId} {x.X} {x.Z} {x.Radius} {x.Amount}"),
                SandboxWeatherCommand x => string.Create(ci, $"{tick} sandbox-weather {x.PlayerId} {EnumName(x.Weather)} {x.Hours}"),
                SandboxTimeCommand x => string.Create(ci, $"{tick} sandbox-time {x.PlayerId} {x.Minute}"),
                SandboxGoodsCommand x => string.Create(ci, $"{tick} sandbox-goods {x.PlayerId} {x.Owner} {x.Amount}"),
                SandboxDamageCommand x => string.Create(ci, $"{tick} sandbox-damage {x.PlayerId} {x.X} {x.Z} {x.Damage}"),
                SandboxTechCommand x => string.Create(ci, $"{tick} sandbox-tech {x.PlayerId} {x.Owner} {Name(x.Tech)}"),
                SandboxCheatCommand x => string.Create(ci, $"{tick} sandbox-cheat {x.PlayerId} {x.Owner} {EnumName(x.Cheat)} {OnOff(x.On)}"),
                SandboxUnitsCommand x => string.Create(ci, $"{tick} sandbox-units {x.PlayerId} {EnumName(x.Action)} {Ids(x.Units)}"),
                SandboxClearCommand x => string.Create(ci, $"{tick} sandbox-clear {x.PlayerId} {x.X} {x.Z} {x.Radius}"),
                _ => throw new NotSupportedException($"No log format for {cmd.GetType().Name}"),
            });
        }
    }

    public override string ToString()
    {
        var sb = new StringWriter(new StringBuilder(), CultureInfo.InvariantCulture) { NewLine = "\n" };
        Write(sb);
        return sb.ToString();
    }

    public static CommandLog Read(TextReader r)
    {
        ulong? seed = null;
        int[]? world = null;
        long ticks = 0;
        int startUnits = 0;
        bool startBuildings = false, startLairs = false, startCities = false, fog = false, dayNight = false, testMode = false, dominion = false, awakening = false;
        int treeScale = 1;
        var factions = new List<string?>();
        int dayStart = Conditions.StartMinute;
        ulong? content = null;
        int? version = null;
        var commands = new List<LoggedCommand>();
        int lineNo = 0;
        string? line;
        while ((line = r.ReadLine()) != null)
        {
            lineNo++;
            line = line.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            try
            {
                switch (f[0])
                {
                    case "rts-log":
                        version = Int(f[1]);
                        if (version != FormatVersion) throw new FormatException($"Unsupported log version {version}");
                        break;
                    case "seed": seed = ulong.Parse(f[1], CultureInfo.InvariantCulture); break;
                    case "world": world = new[] { Int(f[1]), Int(f[2]), Int(f[3]) }; break;
                    case "content": content = ulong.Parse(f[1], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture); break;
                    case "ticks": ticks = long.Parse(f[1], CultureInfo.InvariantCulture); break;
                    case "start-units":
                        startUnits = Int(f[1]);
                        if (startUnits < 0) throw new FormatException("start-units cannot be negative");
                        break;
                    case "start-buildings": startBuildings = Flag(f); break;
                    case "start-lairs": startLairs = Flag(f); break;
                    case "start-cities": startCities = Flag(f); break;
                    case "fog": fog = Flag(f); break;
                    case "daynight": dayNight = Flag(f); break;
                    case "daystart": dayStart = Int(f[1]); break;
                    case "test-mode": testMode = Flag(f); break;
                    case "dominion": dominion = Flag(f); break;
                    case "awakening": awakening = Flag(f); break;
                    case "trees": treeScale = Int(f[1]); break;
                    case "faction":
                    {
                        int player = Int(f[1]);
                        if (player < 0 || player >= Simulation.WildPlayer) throw new FormatException($"faction: no player {player}");
                        while (factions.Count <= player) factions.Add(null);
                        factions[player] = Name(f[2]);
                        break;
                    }
                    default:
                        long tick = long.Parse(f[0], CultureInfo.InvariantCulture);
                        Command cmd = f[1] switch
                        {
                            "dig" => new DigCommand(Int(f[2]), Int(f[3]), Int(f[4]), Int(f[5])),
                            "place" => new PlaceCommand(Int(f[2]), Int(f[3]), Int(f[4]), Int(f[5]),
                                ParseBlock(f[6])),
                            "move" => new MoveCommand(Int(f[2]), ParseIds(f[5]), Int(f[3]), Int(f[4]),
                                f.Length > 6 ? Int(f[6]) : -1),
                            "formation" or "attack-formation" => new FormationMoveCommand(Int(f[2]), ParseIds(f[7]),
                                Int(f[3]), Int(f[4]), Int(f[5]), Int(f[6]), f[1] == "attack-formation", f.Length > 8 ? Int(f[8]) : -1),
                            "stop" => new StopCommand(Int(f[2]), ParseIds(f[3])),
                            "gather" => new GatherCommand(Int(f[2]), ParseIds(f[6]), Int(f[3]), Int(f[4]), Int(f[5])),
                            "clean" => new CleanCommand(Int(f[2]), ParseIds(f[5]), Int(f[3]), Int(f[4])),
                            "build" => new BuildCommand(Int(f[2]), ParseIds(f[6]), Name(f[3]), Int(f[4]), Int(f[5]), f.Length > 7 ? Int(f[7]) : -1),
                            "plan" => new PlanCommand(Int(f[2]), Name(f[3]), Int(f[4]), Int(f[5]), f.Length > 6 ? Int(f[6]) : -1),
                            "unplan" => new CancelPlanCommand(Int(f[2]), Int(f[3])),
                            "work" => new WorkCommand(Int(f[2]), ParseIds(f[4]), Int(f[3])),
                            "capture" => new CaptureCommand(Int(f[2]), ParseIds(f[4]), Int(f[3])),
                            "burn" => new BurnCommand(Int(f[2]), ParseIds(f[4]), Int(f[3])),
                            "wall" => new WallCommand(Int(f[2]), ParseIds(f[8]), Name(f[3]), Int(f[4]), Int(f[5]), Int(f[6]), Int(f[7])),
                            "bridge" => new BridgeCommand(Int(f[2]), ParseIds(f[8]), Name(f[3]), Int(f[4]), Int(f[5]), Int(f[6]), Int(f[7])),
                            "upgrade" => new UpgradeCommand(Int(f[2]), ParseIds(f[5]), Int(f[3]), ParseStretch(f[4])),
                            "post" => new PostCommand(Int(f[2]), ParseIds(f[4]), Int(f[3])),
                            "board" => new BoardCommand(Int(f[2]), ParseIds(f[4]), Int(f[3])),
                            "unload" => new UnloadCommand(Int(f[2]), ParseIds(f[3])),
                            "road" => new RoadCommand(Int(f[2]), Int(f[3]), Int(f[4]), Int(f[5]), Int(f[6])),
                            "train" => new TrainCommand(Int(f[2]), Int(f[3]), Name(f[4])),
                            "research" => new ResearchCommand(Int(f[2]), Name(f[3])),
                            "attack" => new AttackCommand(Int(f[2]), ParseIds(f[4]), Int(f[3])),
                            "attack-building" => new AttackBuildingCommand(Int(f[2]), ParseIds(f[4]), Int(f[3])),
                            "attack-move" => new AttackMoveCommand(Int(f[2]), ParseIds(f[5]), Int(f[3]), Int(f[4]),
                                f.Length > 6 ? Int(f[6]) : -1),
                            "patrol" => new PatrolCommand(Int(f[2]), ParseIds(f[5]), Int(f[3]), Int(f[4]),
                                f.Length > 6 ? Int(f[6]) : -1),
                            "ramp" => new RampCommand(Int(f[2]), ParseIds(f[7]), Int(f[3]), Int(f[4]), Int(f[5]), Int(f[6])),
                            "ladder" => new LadderCommand(Int(f[2]), ParseIds(f[7]), Int(f[3]), Int(f[4]), Int(f[5]), Int(f[6])),
                            "dam" => new DamCommand(Int(f[2]), ParseIds(f[7]), Int(f[3]), Int(f[4]), Int(f[5]), Int(f[6])),
                            "sluice" => new SluiceCommand(Int(f[2]), Int(f[3]), ParseOnOff(f[4])),
                            "dig-area" => new DigAreaCommand(Int(f[2]), ParseIds(f[8]), Int(f[3]), Int(f[4]), Int(f[5]), Int(f[6]), Int(f[7])),
                            "tunnel" => new TunnelCommand(Int(f[2]), ParseIds(f[6]), Int(f[3]), Int(f[4]), Int(f[5])),
                            "shieldwall" => new ShieldWallCommand(Int(f[2]), ParseIds(f[4]), ParseOnOff(f[3])),
                            "charge" => new ChargeCommand(Int(f[2]), ParseIds(f[3])),
                            "fell" => new FellCommand(Int(f[2]), ParseIds(f[7]), Int(f[3]), Int(f[4]), Int(f[5]), Int(f[6])),
                            "prospect" => new ProspectCommand(Int(f[2]), ParseIds(f[5]), Int(f[3]), Int(f[4])),
                            "repair" => new RepairCommand(Int(f[2]), ParseIds(f[4]), Int(f[3])),
                            "pen" => new PenCommand(Int(f[2]), ParseIds(f[4]), Int(f[3])),
                            "stampede" => new StampedeCommand(Int(f[2]), Int(f[3]), Int(f[4]), Int(f[5]), Int(f[6]), Int(f[7])),
                            "unpen" => new UnpenCommand(Int(f[2]), Int(f[3])),
                            "automate" => new AutomateCommand(Int(f[2]), ParseIds(f[4]), ParseOnOff(f[3])),
                            "automate-all" => new AutomateAllCommand(Int(f[2]), ParseOnOff(f[3])),
                            "governor" => new GovernCommand(Int(f[2]), Int(f[3]), ParseFocus(f[4])),
                            "rebuild" => new RebuildCommand(Int(f[2]), ParseOnOff(f[3])),
                            "priority" => new PriorityCommand(Int(f[2]), ParseIds(f[4]), WorkPriorities.ParseLog(f[3])),
                            "default-priority" => new DefaultPriorityCommand(Int(f[2]), WorkPriorities.ParseLog(f[3])),
                            "repeat" => new RepeatCommand(Int(f[2]), Int(f[3]), ParseOnOff(f[4])),
                            "supply" => new SupplyCommand(Int(f[2]), Int(f[3]), Goods.Parse(f[4]), Int(f[5])),
                            "guard" => new GuardCommand(Int(f[2]), ParseIds(f[5]), Int(f[3]), Int(f[4])),
                            "rally" => new RallyCommand(Int(f[2]), Int(f[3]), Int(f[4]), Int(f[5]), Int(f[6])),
                            "stance" => new StanceCommand(Int(f[2]), ParseIds(f[4]), ParseStance(f[3])),
                            "evolve" => new EvolveCommand(Int(f[2]), ParseIds(f[4]), Name(f[3])),
                            "auto-evolve" => new AutoEvolveCommand(Int(f[2]), Name(f[3]), f[4] == "off" ? null : Name(f[4])),
                            "tame" => new TameCommand(Int(f[2]), ParseIds(f[4]), ParseChoice(f[3])),
                            "trade" => new TradeCommand(Int(f[2]), ParseIds(f[4]), Int(f[3])),
                            "market" => new MarketCommand(Int(f[2]), ParseIds(f[4]), Int(f[3])),
                            "den" => new DenCommand(Int(f[2]), ParseIds(f[4]), Int(f[3])),
                            "dusk-tree" => new DuskTreeCommand(Int(f[2]), ParseIds(f[3])),
                            "bloom" => new BloomCommand(Int(f[2]), ParseIds(f[3])),
                            "search-ruin" => new SearchRuinCommand(Int(f[2]), ParseIds(f[4]), Int(f[3])),
                            "earthwall" => new EarthWallCommand(Int(f[2]), ParseIds(f[7]), Int(f[3]), Int(f[4]), Int(f[5]), Int(f[6])),
                            "icebridge" => new IceBridgeCommand(Int(f[2]), ParseIds(f[7]), Int(f[3]), Int(f[4]), Int(f[5]), Int(f[6])),
                            "chasm" => new ChasmCommand(Int(f[2]), ParseIds(f[7]), Int(f[3]), Int(f[4]), Int(f[5]), Int(f[6])),
                            "growforest" => new GrowForestCommand(Int(f[2]), ParseIds(f[7]), Int(f[3]), Int(f[4]), Int(f[5]), Int(f[6])),
                            "hire" => new HireCommand(Int(f[2]), Int(f[3]), Name(f[4])),
                            "surrender" => new SurrenderCommand(Int(f[2])),
                            "truce" => new TruceCommand(Int(f[2]), Int(f[3])),
                            "endtruce" => new EndTruceCommand(Int(f[2]), Int(f[3])),
                            "deal" => new TradeDealCommand(Int(f[2]), Int(f[3])),
                            "enddeal" => new EndTradeDealCommand(Int(f[2]), Int(f[3])),
                            "sandbox-unit" => new SandboxUnitCommand(Int(f[2]), Int(f[3]), Name(f[4]), Int(f[5]), Int(f[6]), Int(f[7])),
                            "sandbox-building" => new SandboxBuildingCommand(Int(f[2]), Int(f[3]), Name(f[4]), Int(f[5]), Int(f[6])),
                            "sandbox-tree" => new SandboxTreeCommand(Int(f[2]), ParseEnum<Rts.Sim.World.TreeKind>(f[3]), Int(f[4]), Int(f[5])),
                            "sandbox-blocks" => new SandboxBlocksCommand(Int(f[2]), ParseBlock(f[3]), Int(f[4]), Int(f[5]), Int(f[6]),
                                Int(f[7]), Int(f[8]), Int(f[9])),
                            "sandbox-lair" => new SandboxLairCommand(Int(f[2]), Name(f[3]), Int(f[4]), Int(f[5])),
                            "sandbox-blood" => new SandboxBloodCommand(Int(f[2]), Int(f[3]), Int(f[4]), Int(f[5]), Int(f[6])),
                            "sandbox-weather" => new SandboxWeatherCommand(Int(f[2]), ParseEnum<Weather>(f[3]), Int(f[4])),
                            "sandbox-time" => new SandboxTimeCommand(Int(f[2]), Int(f[3])),
                            "sandbox-goods" => new SandboxGoodsCommand(Int(f[2]), Int(f[3]), Int(f[4])),
                            "sandbox-damage" => new SandboxDamageCommand(Int(f[2]), Int(f[3]), Int(f[4]), Int(f[5])),
                            "sandbox-tech" => new SandboxTechCommand(Int(f[2]), Int(f[3]), Name(f[4])),
                            "sandbox-cheat" => new SandboxCheatCommand(Int(f[2]), Int(f[3]), ParseEnum<SandboxCheat>(f[4]), ParseOnOff(f[5])),
                            "sandbox-units" => new SandboxUnitsCommand(Int(f[2]), ParseIds(f[4]), ParseEnum<SandboxAction>(f[3])),
                            "sandbox-clear" => new SandboxClearCommand(Int(f[2]), Int(f[3]), Int(f[4]), Int(f[5])),
                            _ => throw new FormatException($"Unknown command '{f[1]}'"),
                        };
                        if (commands.Count > 0 && tick < commands[^1].Tick) throw new FormatException("Commands are out of tick order");
                        commands.Add(new LoggedCommand(tick, cmd));
                        break;
                }
            }
            catch (Exception e) when (e is FormatException or IndexOutOfRangeException or OverflowException or ArgumentException)
            {
                throw new FormatException($"Command log line {lineNo}: {e.Message}", e);
            }
        }
        if (version == null) throw new FormatException("Missing 'rts-log' header");
        if (seed == null || world == null) throw new FormatException("Command log needs 'seed' and 'world' lines");
        var log = new CommandLog
        {
            Seed = seed.Value, SizeX = world[0], SizeY = world[1], SizeZ = world[2], Ticks = ticks, StartUnits = startUnits,
            StartBuildings = startBuildings, StartLairs = startLairs, StartCities = startCities, ContentHash = content,
            Fog = fog,
            DayNight = dayNight,
            DayStart = dayStart,
            TestMode = testMode,
            Dominion = dominion,
            Awakening = awakening,
            TreeScale = treeScale,
            Factions = factions.Count > 0 ? factions.ToArray() : null,
        };
        log.Commands.AddRange(commands);
        return log;
    }

    public static CommandLog Parse(string text) => Read(new StringReader(text));

    private static int Int(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    /// <summary>A header line that switches something on (1) or leaves it off (0).</summary>
    private static bool Flag(string[] f) => Int(f[1]) switch
    {
        0 => false,
        1 => true,
        _ => throw new FormatException($"{f[0]} is 0 or 1"),
    };

    private static string ChoiceName(TameChoice choice) => choice switch
    {
        TameChoice.Recruit => "recruit",
        TameChoice.Ride => "ride",
        _ => throw new NotSupportedException($"No log name for tame choice {(int)choice}"),
    };

    private static TameChoice ParseChoice(string s) => s switch
    {
        "recruit" => TameChoice.Recruit,
        "ride" => TameChoice.Ride,
        _ => throw new FormatException($"Unknown tame choice '{s}'"),
    };

    private static string GoodName(Good good) =>
        Enum.IsDefined(good) ? good.ToString() : throw new NotSupportedException($"No log name for good {(int)good}");

    private static BlockType ParseBlock(string s) =>
        Enum.TryParse<BlockType>(s, ignoreCase: false, out var b) && Enum.IsDefined(b) && !int.TryParse(s, out _)
            ? b : throw new FormatException($"Unknown block '{s}'");

    /// <summary>An enum value by its name, as in the log (never a bare number).</summary>
    private static string EnumName<T>(T value) where T : struct, Enum =>
        Enum.IsDefined(value) ? value.ToString() : throw new NotSupportedException($"No log name for {typeof(T).Name} {value}");

    private static T ParseEnum<T>(string s) where T : struct, Enum =>
        Enum.TryParse<T>(s, ignoreCase: false, out var v) && Enum.IsDefined(v) && !int.TryParse(s, out _)
            ? v : throw new FormatException($"Unknown {typeof(T).Name} '{s}'");

    private static string Ids(int[] ids) => ids.Length == 0 ? "-" : string.Join(',', ids);

    private static string OnOff(bool on) => on ? "on" : "off";

    private static bool ParseOnOff(string s) => s switch
    {
        "on" => true,
        "off" => false,
        _ => throw new FormatException($"Expected on or off, not '{s}'"),
    };

    private static bool ParseStretch(string s) => s switch
    {
        "stretch" => true,
        "one" => false,
        _ => throw new FormatException($"Expected stretch or one, not '{s}'"),
    };

    private static string StanceName(UnitStance stance) => stance switch
    {
        UnitStance.Aggressive => "aggressive",
        UnitStance.Hold => "hold",
        UnitStance.Retreat => "retreat",
        _ => throw new NotSupportedException($"No log name for stance {(int)stance}"),
    };

    private static Economy.GovernorFocus ParseFocus(string s) =>
        Enum.TryParse<Economy.GovernorFocus>(s, ignoreCase: true, out var f) && Enum.IsDefined(f) && !char.IsDigit(s[0]) ? f : throw new FormatException($"Unknown governor focus '{s}'");

    private static UnitStance ParseStance(string s) => s switch
    {
        "aggressive" => UnitStance.Aggressive,
        "hold" => UnitStance.Hold,
        "retreat" => UnitStance.Retreat,
        _ => throw new FormatException($"Unknown stance '{s}'"),
    };

    /// <summary>A content id (building, unit or tech): letters, digits and underscores, so it stays one field.</summary>
    private static string Name(string id)
    {
        if (id.Length == 0) throw new FormatException("Empty content id");
        foreach (char ch in id)
            if (!char.IsAsciiLetterOrDigit(ch) && ch != '_') throw new FormatException($"Bad content id '{id}'");
        return id;
    }

    private static int[] ParseIds(string s)
    {
        if (s == "-") return Array.Empty<int>();
        var parts = s.Split(',');
        var ids = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++) ids[i] = Int(parts[i]);
        return ids;
    }
}
