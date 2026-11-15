using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using Rts.Sim;
using Rts.Sim.AI;
using Rts.Sim.Content;
using Rts.Sim.Replay;
using Rts.Sim.Units;

namespace Rts.Game;

/// <summary>
/// The game: the start screen, then a match. A match generates a voxel world from the simulation core,
/// draws it and runs the simulation; mouse and keyboard controls are in <see cref="PlayerControl"/>. When
/// the match is over for you (you won, lost or surrendered, see <see cref="MatchSystem"/>), the clock stops,
/// the match's command log is saved as a replay (see <see cref="Replays"/>) and the end of match panel
/// (<see cref="MatchEndPanel"/>) leads back to the start screen.
///
/// Started with no command-line options, the game shows the start screen (<see cref="StartScreen"/>);
/// matches started from it have no terrain tool. With options, it starts a match at once as they say
/// (seed 1234 unless given, the computer player only with --ai), which is how the tests run it.
///
/// Command-line options (after "--"):
///   --menu               show the start screen even with other options (--seed and --player fill it in)
///   --ui-look=NAME       the UI's look for this run: timber, stone or crystal (see UiStyle); otherwise each
///                        faction's own
///   --replays=DIR        save replays in DIR instead of user://replays
///   --seed=N             world seed
///   --screenshot=PATH    render a few frames, save a PNG and quit (used for automated playtests)
///   --input="STEPS"      scripted mouse and keyboard input (see InputScript); with --input-file=PATH
///                        the steps come from a file. The script's "shot" steps save screenshots.
///   --cam-yaw=DEG --cam-pitch=DEG --cam-distance=M   starting camera view
///   --benchmark[=SECONDS] dig 100 blocks per second while slowly orbiting, log frame times and quit
///   --benchmark-out=PATH  file the benchmark result line is appended to (default user://benchmark.txt)
///   --world=NxHxN         world size in blocks (default 512x64x512)
///   --map=PATH            load a saved map (RegionMap format) instead of generating one
///   --data=DIR            load the game data (factions.json, techs.json, buildings.json, units.json, roles.json,
///                         citystates.json) from DIR instead of the files built into the game; problems are
///                         printed and the game quits
///   --focus=WHERE         starting camera focus: centre, start0, start1, cavern, swamp, lava, dusk (the dusk
///                         tree) or X,Z in blocks
///   --blood=N             stain N columns around the camera focus with blood, and make every unit bleed
///                         (stress test for drawing blood)
///   --units=N             spawn N units of yours around the camera focus (stress test; all on one side,
///                         so they walk instead of fighting); otherwise a small group of every unit type
///                         that is trained (not the forms units evolve into) stands at each start area,
///                         a wolf lair lies on the way from each start area to the map centre, and each
///                         city-state has a town (with a lair of its own) about as far from every start
///   --goods=N             every town hall starts with N more of every good (for trying things out)
///   --houses=N            N finished houses of yours behind your town hall (the start has more people than
///                         room, so this makes room to try out training and hiring)
///   --battle=N            two armies of N soldiers each (every soldier type in turn) face each other at
///                         the camera focus, yours to the west, with an enemy house to the north
///   --lineup              every unit type each of the first two players can have (its faction's own, the
///                         shared ones, monsters, caravans and mercenaries) stands in a row at the camera
///                         focus, player 0's north of player 1's and too far apart to fight; no usual start
///   --dusk-party=N        N of each of your soldier types (and two of each healer, if your faction heals the
///                         dusk tree) stand fifteen columns from the dusk tree, out of its warden's reach
///   --xp=N                your units start with N experience (150 is enough for Settlers to evolve)
///   --player=N            play as player N: 0 (the default) is the Settlers at start0, 1 the Orcs at start1
///   --ai                  the other players in the match are computer players (see AiPlayer)
///   --ai-level=L          how well they play: normal, hard (the default) or classic (see AiLevel)
///   --daynight[=off]      day, night and weather (see Rts.Sim.Conditions): night, bad weather and mist shorten
///                         sight and make ranged shots miss. A match started from the command line has them only
///                         with --daynight or --time-of-day; on the start screen they are ticked unless --daynight=off
///   --lights=N            N torches around your town hall and a campfire south of it, all finished
///   --time-of-day=H       the match starts at hour H (0 to 24; 8 is morning, 13 noon, 19.5 dusk, 0 midnight),
///                         with day and night on
///   --weather=W           the weather is W (clear, cloudy, rain, storm or fog) for the first day of the match,
///                         or, with day and night off, the sky only looks it
///   --test-mode           test mode (see TestPanel): the test panel, cheats and saving scenarios; also a
///                         tickbox on the start screen
///   --scenario=PATH       start from a saved scenario (a test mode match's command log), played back to the
///                         moment it was saved, paused
///   --scenarios=DIR       save and list scenarios in DIR instead of user://scenarios
///   --fog[=off]           fog of war (see FogOfWar): a match started from the command line has it only with
///                         --fog, and on the start screen it is ticked unless --fog=off
/// A match from the usual start is recorded as it is played and saved as a replay at its end; --map,
/// --units, --battle, --lineup, --goods, --houses and --xp set a match up otherwise, so it is not.
/// </summary>
public partial class Main : Node3D
{
    private int WorldX = 512, WorldY = 64, WorldZ = 512;

    /// <summary>Most simulation ticks run in one frame; at 4x on a slow frame the game slows down instead.</summary>
    private const int MaxTicksPerFrame = 8;

    /// <summary>Options that set a match up other than the usual start, so that its log would not replay it.</summary>
    private static readonly string[] TestSetups = { "map", "units", "battle", "lineup", "goods", "houses", "xp", "weather", "lights", "blood", "dusk-party" };

    private Dictionary<string, string> _args = new();
    private ContentDb _content = null!;
    // The match being played and everything that belongs to it, or null (the start screen).
    private Node3D? _match;
    private MatchSetup? _setup;
    private StartScreen? _startScreen;
    // A match about to start, once the start screen has had a frame to say so.
    private MatchSetup? _starting;
    private int _startFrames;
    private MatchEndPanel? _endPanel;
    private bool _ended;
    private string? _replayPath;
    private string _noReplay = "";
    private Action<Command> _issue = null!;
    // A saved scenario the next match starts from, played back to where it was saved.
    private CommandLog? _scenario;
    private TestPanel? _testPanel;
    private const string ScenarioFolder = "user://scenarios";

    private Simulation _sim = null!;
    private CameraRig _rig = null!;
    private ChunkRenderer _chunks = null!;
    private TerrainMaterials _terrain = null!;
    private FogOfWar _fog = null!;
    private WorldLighting _lighting = null!;
    private Ambience _ambience = null!;
    private UnitRenderer _unitRenderer = null!;
    private SelectionRenderer _selectionRenderer = null!;
    private GhostRenderer _ghostRenderer = null!;
    private CombatRenderer _combatRenderer = null!;
    private BloodRenderer _bloodRenderer = null!;
    private UnitIcons _unitIcons = null!;
    private FallingRenderer _fallingRenderer = null!;
    private PlayerControl _control = null!;
    private Hud _hud = null!;
    private GameClock _clock = new();
    private HeightSlice _slice = null!;
    private InputScript? _script;
    private readonly List<(int Owner, int Id)> _spawned = new();
    private readonly List<AiPlayer> _computer = new();
    private string? _screenshotPath;
    private int _frames;
    private double _accumulator;
    private Benchmark? _benchmark;

    /// <summary>The match's simulation, or null while the start screen shows.</summary>
    public Simulation? Sim => _match != null ? _sim : null;
    /// <summary>The match being played is over for you (see <see cref="EndMatch"/>).</summary>
    public bool MatchEnded => _match != null && _ended;
    public string? ScreenshotPath => _screenshotPath;

    public override void _Ready()
    {
        _args = ParseArgs();
        UiStyle.Override = _args.TryGetValue("ui-look", out var look) ? look : null;
        _args.TryGetValue("screenshot", out _screenshotPath);
        if (_args.TryGetValue("world", out var size))
        {
            var parts = size.Split('x');
            if (parts.Length == 3 && int.TryParse(parts[0], out var wx) && int.TryParse(parts[1], out var wy) && int.TryParse(parts[2], out var wz))
                (WorldX, WorldY, WorldZ) = (wx, wy, wz);
        }

        try
        {
            _content = _args.TryGetValue("data", out var dataDir) ? ContentLoader.LoadDirectory(dataDir) : ContentDb.Default;
        }
        catch (ContentException e)
        {
            GD.PrintErr(e.Message);
            SetProcess(false);
            GetTree().Quit(1);
            return;
        }
        if (_args.TryGetValue("input-file", out var inputFile))
            _script = new InputScript(System.IO.File.ReadAllText(inputFile));
        else if (_args.TryGetValue("input", out var input))
            _script = new InputScript(input);

        ulong? seed = _args.TryGetValue("seed", out var s) && ulong.TryParse(s, out var v) ? v : null;
        int player = _args.TryGetValue("player", out var ps) && int.TryParse(ps, out var p) && p >= 0 && p < Simulation.WildPlayer ? p : 0;
        bool? fog = _args.TryGetValue("fog", out var fs) ? fs is not ("off" or "0" or "false") : null;
        var level = _args.TryGetValue("ai-level", out var ls) && Enum.TryParse<AiLevel>(ls, ignoreCase: true, out var l) ? l : AiLevel.Hard;
        bool? dayNight = _args.TryGetValue("daynight", out var ds) ? ds is not ("off" or "0" or "false")
            : _args.ContainsKey("time-of-day") ? true : null;
        bool test = _args.ContainsKey("test-mode");
        if (_args.TryGetValue("scenario", out var scenarioPath))
        {
            _scenario = ReadScenario(scenarioPath);
            if (_scenario == null)
            {
                SetProcess(false);
                GetTree().Quit(1);
                return;
            }
        }
        if (_scenario != null)
            StartMatch(new MatchSetup(_scenario.Seed, player, _args.ContainsKey("ai"), _scenario.Fog, level, _scenario.DayNight, TestMode: true), fromMenu: false);
        else if (_args.Count == 0 || _args.ContainsKey("menu"))
            ShowStartScreen(new MatchSetup(seed ?? StartScreen.RandomSeed(), player, Computer: true, Fog: fog ?? true, level, DayNight: dayNight ?? true, test));
        else
            StartMatch(new MatchSetup(seed ?? 1234UL, player, _args.ContainsKey("ai"), Fog: fog ?? false, level, DayNight: dayNight ?? false, test), fromMenu: false);
    }

    public override void _Process(double delta)
    {
        long workStart = Stopwatch.GetTimestamp();
        _script?.Update(this);
        if (_starting != null && --_startFrames <= 0)
        {
            var setup = _starting;
            _starting = null;
            _startScreen?.QueueFree();
            _startScreen = null;
            StartMatch(setup, fromMenu: true);
        }
        else if (_match != null)
        {
            RunMatch(delta, workStart);
        }

        if (_script != null)
        {
            if (_script.Finished) GetTree().Quit(0);
        }
        else if (_screenshotPath != null && _match != null && ++_frames == 10)
        {
            bool ok = SaveScreenshot(_screenshotPath);
            GetTree().Quit(ok ? 0 : 1);
        }
    }

    /// <summary>Shows the start screen, filled in with <paramref name="setup"/>; Start match starts the match it sets up.</summary>
    private void ShowStartScreen(MatchSetup setup)
    {
        var screen = new StartScreen { Name = "StartScreen" };
        AddChild(screen);
        screen.Init(_content, setup);
        screen.LookChanged += chosen =>
        {
            if (_starting != null) return;
            screen.QueueFree();
            ShowStartScreen(chosen);
        };
        screen.StartPressed += chosen =>
        {
            if (_starting != null) return;
            screen.ShowLoading(chosen.Seed);
            _starting = chosen;
            _startFrames = 2; // let the loading message be drawn before the world is made
        };
        _startScreen = screen;
    }

    /// <summary>
    /// Makes the world and sets the match up: from the start screen's choices, or from the command line
    /// (whose other options apply either way).
    /// </summary>
    private void StartMatch(MatchSetup setup, bool fromMenu)
    {
        _setup = setup;
        PlayerControl.LocalPlayer = setup.Player;
        PlayerControl.TerrainTool = !fromMenu || setup.TestMode;
        GameClock.Fast = setup.TestMode;
        _clock = new GameClock();
        _accumulator = 0;
        _match = new Node3D { Name = "Match" };
        AddChild(_match);

        var loadTimer = Stopwatch.StartNew();
        var scenario = _scenario;
        _scenario = null;
        if (scenario != null)
        {
            _sim = scenario.CreateSimulation(_content);
            (WorldX, WorldY, WorldZ) = (_sim.World.SizeX, _sim.World.SizeY, _sim.World.SizeZ);
        }
        else if (_args.TryGetValue("map", out var mapPath))
        {
            using var file = System.IO.File.OpenRead(mapPath);
            _sim = new Simulation(Rts.Sim.World.RegionMap.Load(file), _content);
            (WorldX, WorldY, WorldZ) = (_sim.World.SizeX, _sim.World.SizeY, _sim.World.SizeZ);
        }
        else
        {
            _sim = new Simulation(setup.Seed, WorldX, WorldY, WorldZ, _content);
        }
        _sim.Fog = setup.Fog;
        _sim.DayNight = setup.DayNight;
        _sim.TestMode = setup.TestMode;
        if (scenario == null && _args.ContainsKey("time-of-day"))
            _sim.DayStart = (int)(Mathf.PosMod(ArgFloat(_args, "time-of-day", 8f), 24f) * 60f);
        _issue = c => _sim.Schedule(_sim.Tick, c);
        double generateMs = loadTimer.Elapsed.TotalMilliseconds;

        _terrain = new TerrainMaterials();
        _chunks = new ChunkRenderer();
        _match.AddChild(_chunks);

        _rig = new CameraRig();
        _match.AddChild(_rig);
        var (fx, fz) = FocusPoint(_args.TryGetValue("focus", out var focus) ? focus : fromMenu ? $"start{setup.Player}" : "centre");
        _rig.Position = new Vector3(fx, Mathf.Max(0, _sim.World.SurfaceHeight(fx, fz)), fz) * ChunkMesher.BlockSize;
        _rig.SetView(ArgFloat(_args, "cam-yaw", 45f), ArgFloat(_args, "cam-pitch", 50f), ArgFloat(_args, "cam-distance", 60f));

        if (scenario != null) PlayScenario(scenario);
        else SetUpMatch(setup, fx, fz);
        // What is drawn is the world as you know it: with fog of war, the land as the match was set up, and
        // after that what you have seen of it.
        _fog = new FogOfWar(_sim, PlayerControl.LocalPlayer);
        _chunks.Init(_fog.World, _terrain.Opaque, _terrain.Water);
        _terrain.SetFog(_fog.Texture, new Vector2(WorldX, WorldZ) * ChunkMesher.BlockSize);
        if (setup.Computer)
            foreach (int p in _sim.Match.Players)
                if (p != PlayerControl.LocalPlayer)
                {
                    _computer.Add(new AiPlayer(p, setup.Seed, setup.Level));
                    GD.Print($"Player {p} ({_sim.Economy.Player(p).Faction.Name}) is a computer player ({setup.Level})");
                }
        var cycle = new DayCycle(_sim);
        if (_args.TryGetValue("weather", out var weather) && Enum.TryParse<Weather>(weather, ignoreCase: true, out var w))
        {
            // With day and night, the match has this weather for a day; without, the sky only looks it.
            if (_sim.DayNight) _sim.Conditions.SetWeather(w, Conditions.DayTicks);
            else cycle.WeatherOverride = w;
        }
        _lighting = new WorldLighting(cycle);
        _match.AddChild(_lighting);
        _lighting.Init(_sim, _fog, _rig);
        _ambience = new Ambience();
        _match.AddChild(_ambience);
        _ambience.Init(_sim, _fog, _rig, _lighting, _terrain);
        _unitRenderer = new UnitRenderer();
        _match.AddChild(_unitRenderer);
        _unitRenderer.Init(_sim, _fog);
        _selectionRenderer = new SelectionRenderer();
        _match.AddChild(_selectionRenderer);
        _selectionRenderer.Init(_sim);
        _ghostRenderer = new GhostRenderer();
        _match.AddChild(_ghostRenderer);
        _ghostRenderer.Init(_sim);
        _combatRenderer = new CombatRenderer();
        _match.AddChild(_combatRenderer);
        _combatRenderer.Init(_sim, _rig.Camera, _selectionRenderer.Material.Shader, _fog);
        _bloodRenderer = new BloodRenderer();
        _match.AddChild(_bloodRenderer);
        _bloodRenderer.Init(_sim, _fog);
        _unitIcons = new UnitIcons();
        _match.AddChild(_unitIcons);
        _unitIcons.Init(_sim, _rig, _fog);
        _fallingRenderer = new FallingRenderer();
        _match.AddChild(_fallingRenderer);
        _fallingRenderer.Init(_sim, _fog, _terrain.Opaque);

        _slice = new HeightSlice(WorldY);
        _slice.Changed += ApplySlice;
        _control = new PlayerControl();
        _match.AddChild(_control);
        _control.Init(_sim, _rig, _clock, _slice, _selectionRenderer, _fog);
        UiStyle.UseFor(_sim.Economy.Faction(PlayerControl.LocalPlayer));
        _hud = new Hud();
        _match.AddChild(_hud);
        _hud.Init(_sim, _clock, _slice, _control, _lighting);
        _hud.ResultsPressed += () => ShowEndPanel(true);
        if (setup.TestMode)
        {
            _testPanel = new TestPanel { Name = "TestPanel" };
            _match.AddChild(_testPanel);
            _testPanel.Init(_sim, _control, _clock, setup.Fog, ScenarioDir);
            _testPanel.SaveRequested += SaveScenario;
            _testPanel.LoadRequested += LoadScenario;
            if (scenario != null)
            {
                _clock.Pause();
                _testPanel.Say($"Loaded the scenario at {Clock(_sim.Tick)} (tick {_sim.Tick}), paused.");
            }
            else if (_sim.Recording == null) _testPanel.Say($"This match cannot be saved as a scenario: {_noReplay}.");
        }

        double meshMs = _chunks.LoadAll();
        GD.Print($"Loaded {WorldX}x{WorldY}x{WorldZ} world: generate {generateMs:F0} ms, mesh {meshMs:F0} ms, " +
                 $"total {loadTimer.Elapsed.TotalMilliseconds:F0} ms, {_chunks.ChunkNodeCount} chunk meshes");

        if (_args.TryGetValue("benchmark", out var bench))
        {
            double seconds = double.TryParse(bench, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var secs) ? secs : 10.0;
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            _benchmark = new Benchmark(seconds, setup.Seed, loadTimer.Elapsed.TotalMilliseconds,
                _args.TryGetValue("benchmark-out", out var outPath) ? outPath : "user://benchmark.txt");
        }
    }

    /// <summary>A frame of the match: the simulation ticks the clock allows, then the renderers and the HUD.</summary>
    private void RunMatch(double delta, long workStart)
    {
        // Fixed-rate simulation ticks, independent of frame rate; the clock sets pause and speed.
        _accumulator += _clock.Scale(delta);
        double step = 1.0 / Simulation.TicksPerSecond;
        if (_clock.StepPending)
        {
            // Test mode's single tick, while paused.
            _clock.StepPending = false;
            if (_clock.Paused && !_ended) _accumulator = step;
        }
        int ticks = 0;
        while (_accumulator >= step && ticks < MaxTicksPerFrame)
        {
            _benchmark?.ScheduleDigs(_sim, _rig.Position / ChunkMesher.BlockSize);
            _benchmark?.ScheduleMoves(_sim, _spawned, _rig.Position / ChunkMesher.BlockSize);
            foreach (var ai in _computer) ai.Think(_sim, _issue);
            _sim.Step();
            _fallingRenderer.AfterTick();
            _accumulator -= step;
            ticks++;
            if (!_ended && Decided())
            {
                EndMatch();
                break;
            }
        }
        if (ticks == MaxTicksPerFrame) _accumulator = Math.Min(_accumulator, step);
        _fog.Update();
        float alpha = (float)Math.Clamp(_accumulator / step, 0, 1);
        _lighting.UpdateFrame(alpha);
        _ambience.UpdateFrame((float)delta);
        _unitRenderer.UpdateInstances(alpha, (float)delta);
        _control.UpdateFrame();
        _selectionRenderer.UpdateRings(_control.Selected, _control.Preview, PlayerControl.LocalPlayer, alpha, delta);
        _selectionRenderer.UpdateTools(_control);
        _ghostRenderer.UpdateGhosts(_control);
        _combatRenderer.UpdateFrame(alpha, _control.Selected);
        _bloodRenderer.UpdateFrame(alpha);
        _unitIcons.UpdateFrame(alpha);
        _fallingRenderer.UpdateFrame(alpha, (float)delta);
        _hud.Refresh(delta);

        if (_benchmark != null)
        {
            _rig.Yaw += (float)delta * 6f;
            double workMs = (Stopwatch.GetTimestamp() - workStart) * 1000.0 / Stopwatch.Frequency;
            if (_benchmark.Record(delta, workMs, _chunks)) GetTree().Quit(0);
        }
    }

    /// <summary>The match is over, or over for you: you were in a match of two or more and are out.</summary>
    private bool Decided()
    {
        var match = _sim.Match;
        int me = PlayerControl.LocalPlayer;
        return match.Over || match.Players.Count >= 2 && match.Players.Contains(me) && !match.InPlay(me);
    }

    /// <summary>
    /// The match is over for you: the clock stops, the replay is saved (if the match was recorded) and the
    /// end of match panel says how it went.
    /// </summary>
    private void EndMatch()
    {
        _ended = true;
        _accumulator = 0;
        _clock.Pause();
        // The fog of war lifts.
        _fog.Reveal();
        _terrain.SetFog(null, default);
        var match = _sim.Match;
        int me = PlayerControl.LocalPlayer;
        bool playing = match.Players.Contains(me);
        long at = playing && !match.InPlay(me) ? match.OutAt(me) : match.EndTick;
        string time = Clock(at);
        string Name(int p) => _sim.Economy.Player(p).Faction.Name;
        // Out with a town hall still standing means they gave up.
        string Fall(int p) => p == me
            ? _sim.Economy.TownHall(p) != null ? "You surrendered." : "You have lost your last town hall."
            : _sim.Economy.TownHall(p) != null ? $"The {Name(p)} surrendered." : $"The {Name(p)} have lost their last town hall.";
        var others = new List<int>();
        foreach (int p in match.Players)
            if (p != me) others.Add(p);

        string title, summary;
        Color color;
        if (!playing)
        {
            (title, color) = ("Match over", MatchEndPanel.DrawColor);
            summary = match.Winner >= 0 ? $"The {Name(match.Winner)} (player {match.Winner}) won." : "It ended in a draw.";
        }
        else if (match.InPlay(me))
        {
            (title, color) = ("Victory", MatchEndPanel.VictoryColor);
            summary = others.Count == 1 ? Fall(others[0]) : "You are the last one left in the match.";
        }
        else if (match.Over && match.Winner < 0)
        {
            (title, color) = ("Draw", MatchEndPanel.DrawColor);
            summary = others.Count == 1 ? $"You and the {Name(others[0])} went out at the same moment."
                : "The last players in the match went out at the same moment.";
        }
        else
        {
            (title, color) = ("Defeat", MatchEndPanel.DefeatColor);
            summary = Fall(me);
        }
        string outcome = !match.Over ? $"player {me} is out" : match.Winner >= 0 ? $"player {match.Winner} won" : "a draw";

        if (_sim.Recording is { } log)
        {
            var sides = new List<string>();
            foreach (int p in match.Players)
            {
                bool computer = _computer.Exists(ai => ai.Player == p);
                sides.Add($"{Name(p)} (player {p}{(p == me ? ", you" : computer ? ", computer" : "")})");
            }
            _replayPath = Replays.Save(log, $"{string.Join(" against ", sides)}: {outcome} at {time}",
                _args.TryGetValue("replays", out var folder) ? folder : null);
            if (_replayPath == null) _noReplay = "it could not be saved";
        }
        GD.Print($"Match over at {time}: {title}, {outcome}; tick {_sim.Tick}, state hash {_sim.StateHash():X16}");
        if (_replayPath != null) GD.Print($"Replay saved to {_replayPath}");

        _endPanel = new MatchEndPanel { Name = "MatchEnd" };
        _match!.AddChild(_endPanel);
        _endPanel.Init(title, color, summary, $"Match time {time}   ·   Seed {_sim.Seed}", _replayPath, _noReplay);
        _endPanel.NewMatchPressed += NewMatch;
        _endPanel.Closed += () => ShowEndPanel(false);
        _hud.MatchOver = true;
        ShowEndPanel(true);
    }

    /// <summary>Shows or hides the end of match panel; the HUD and the mouse and keyboard controls are off while it shows.</summary>
    private void ShowEndPanel(bool show)
    {
        if (_endPanel == null) return;
        _endPanel.Visible = show;
        _hud.Visible = !show;
        _control.SetProcessUnhandledInput(!show);
    }

    /// <summary>Leaves the match for the start screen, filled in as it was but with a new seed.</summary>
    private void NewMatch()
    {
        var last = _setup!;
        LeaveMatch();
        ShowStartScreen(last with { Seed = StartScreen.RandomSeed() });
    }

    /// <summary>Takes the match down, and everything that belongs to it.</summary>
    private void LeaveMatch()
    {
        _fog.Detach();
        _match?.QueueFree();
        _match = null;
        _sim = null!;
        _computer.Clear();
        _spawned.Clear();
        _endPanel = null;
        _testPanel = null;
        _ended = false;
        _replayPath = null;
        _noReplay = "";
    }

    private string ScenarioDir => _args.TryGetValue("scenarios", out var dir) ? dir : ScenarioFolder;

    /// <summary>A saved scenario read from a file, or null (the problem is printed).</summary>
    private static CommandLog? ReadScenario(string path)
    {
        try
        {
            var log = CommandLog.Parse(System.IO.File.ReadAllText(path));
            if (!log.TestMode) GD.Print($"{path} is not a test mode match: it is loaded in test mode");
            return log;
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or FormatException)
        {
            GD.PrintErr($"Could not read the scenario {path}: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// A new match's setup, as the command-line options say (the usual start otherwise), and its recording,
    /// unless an option set it up other than the usual start, so that the log would not replay it.
    /// </summary>
    private void SetUpMatch(MatchSetup setup, int fx, int fz)
    {
        int unitCount = _args.TryGetValue("units", out var uc) && int.TryParse(uc, out var n) ? Math.Max(0, n) : -1;
        int battle = _args.TryGetValue("battle", out var bc) && int.TryParse(bc, out var nb) ? Math.Max(0, nb) : 0;
        if (_args.ContainsKey("lineup") && unitCount < 0) unitCount = 0;
        SpawnUnits(unitCount, fx, fz);
        if (battle > 0) SpawnBattle(battle, fx, fz);
        if (_args.ContainsKey("lineup")) SpawnLineup(fx, fz);
        if (_args.TryGetValue("goods", out var gs) && int.TryParse(gs, out var goods) && goods > 0) GiveGoods(goods);
        if (_args.TryGetValue("houses", out var hs) && int.TryParse(hs, out var houses) && houses > 0) GiveHouses(houses);
        if (_args.TryGetValue("lights", out var ls) && int.TryParse(ls, out var lights) && lights > 0) GiveLights(lights);
        if (_args.TryGetValue("xp", out var xs) && int.TryParse(xs, out var xp) && xp > 0) GiveExperience(xp);
        if (_args.TryGetValue("dusk-party", out var ds) && int.TryParse(ds, out var party) && party > 0) SpawnDuskParty(party);
        if (_args.TryGetValue("blood", out var bls) && int.TryParse(bls, out var stains) && stains > 0) SpillBlood(stains, fx, fz);
        // A match from the usual start is recorded as it is played, to be saved as a replay at its end.
        var odd = new List<string>();
        foreach (var option in TestSetups)
            if (_args.ContainsKey(option)) odd.Add("--" + option);
        _noReplay = odd.Count > 0 ? $"the match was set up with {string.Join(", ", odd)}" : "";
        if (odd.Count == 0) _sim.Recording = CommandLog.UsualStart(setup.Seed, WorldX, WorldY, WorldZ, _content, setup.Fog,
            setup.DayNight, _sim.DayStart, setup.TestMode);
    }

    /// <summary>
    /// Plays a saved scenario back on the new simulation, set up from its header, to the tick it was saved at,
    /// recording it again as it goes so that it can be saved on from there.
    /// </summary>
    private void PlayScenario(CommandLog scenario)
    {
        var watch = Stopwatch.StartNew();
        _sim.Recording = new CommandLog
        {
            Seed = scenario.Seed, SizeX = scenario.SizeX, SizeY = scenario.SizeY, SizeZ = scenario.SizeZ, StartUnits = scenario.StartUnits,
            StartBuildings = scenario.StartBuildings, StartLairs = scenario.StartLairs, StartCities = scenario.StartCities,
            ContentHash = scenario.ContentHash, Fog = scenario.Fog, DayNight = scenario.DayNight, DayStart = scenario.DayStart, TestMode = true,
        };
        int next = 0;
        while (_sim.Tick < scenario.Ticks)
        {
            while (next < scenario.Commands.Count && scenario.Commands[next].Tick <= _sim.Tick)
                _sim.Schedule(_sim.Tick, scenario.Commands[next++].Command);
            _sim.Step();
        }
        GD.Print($"Scenario played back to tick {_sim.Tick} in {watch.Elapsed.TotalMilliseconds:F0} ms, state hash {_sim.StateHash():X16}");
    }

    /// <summary>Test mode's Save: the match so far, as a scenario file in the scenarios folder.</summary>
    private void SaveScenario()
    {
        if (_sim.Recording is not { } log)
        {
            _testPanel?.Say($"This match cannot be saved as a scenario: {_noReplay}.");
            return;
        }
        log.Ticks = _sim.Tick;
        string? path = Replays.Save(log, $"Test mode scenario at {Clock(_sim.Tick)}", ScenarioDir, "scenario");
        GD.Print(path != null ? $"Scenario saved to {path} at tick {_sim.Tick}" : "Scenario could not be saved");
        _testPanel?.Say(path != null ? $"Saved {System.IO.Path.GetFileName(path)} at {Clock(_sim.Tick)}." : "The scenario could not be saved.");
        _testPanel?.ListScenarios();
    }

    /// <summary>Test mode's Load: the match is left for the scenario, played back to where it was saved.</summary>
    private void LoadScenario(string path)
    {
        var scenario = ReadScenario(path);
        if (scenario == null)
        {
            _testPanel?.Say($"Could not read {System.IO.Path.GetFileName(path)}.");
            return;
        }
        var last = _setup!;
        // The camera stays where it was, if the scenario's world is the same size.
        var (cx, cz) = _control.CameraColumn;
        var (yaw, pitch, distance) = (_rig.Yaw, _rig.Pitch, _rig.Distance);
        bool sameWorld = scenario.SizeX == WorldX && scenario.SizeZ == WorldZ;
        LeaveMatch();
        _scenario = scenario;
        StartMatch(last with { Seed = scenario.Seed, Fog = scenario.Fog, DayNight = scenario.DayNight, TestMode = true }, fromMenu: true);
        if (sameWorld) FocusCamera(cx, cz, yaw, pitch, distance);
    }

    private static string Clock(long ticks)
    {
        long seconds = Math.Max(0, ticks) / Simulation.TicksPerSecond;
        return $"{seconds / 60}:{seconds % 60:00}";
    }

    /// <summary>Saves the last drawn frame as a PNG.</summary>
    public bool SaveScreenshot(string path)
    {
        var image = GetViewport().GetTexture().GetImage();
        var err = image.SavePng(path);
        GD.Print(err == Error.Ok ? $"Saved screenshot to {path}" : $"Screenshot failed: {err}");
        return err == Error.Ok;
    }

    /// <summary>Moves the camera focus to block column (x, z), optionally with a new view.</summary>
    public void FocusCamera(int x, int z, float? yaw, float pitch, float distance)
    {
        x = Math.Clamp(x, 0, WorldX - 1);
        z = Math.Clamp(z, 0, WorldZ - 1);
        _rig.Position = new Vector3(x, Mathf.Max(0, _sim.World.SurfaceHeight(x, z)), z) * ChunkMesher.BlockSize;
        if (yaw != null) _rig.SetView(yaw.Value, pitch, distance);
    }

    /// <summary>Where the middle of the top of block (x, y, z) is drawn on screen, for scripted input.</summary>
    public Vector2 BlockOnScreen(int x, int y, int z) =>
        _rig.Camera.UnprojectPosition(new Vector3(x + 0.5f, y + 1f, z + 0.5f) * ChunkMesher.BlockSize);

    /// <summary>UI state for scripted tests.</summary>
    public string DescribeUi()
    {
        if (_match == null) return $"screen=start {_startScreen?.Describe()}";
        string screen = _endPanel is { Visible: true } ? "end" : "match";
        return $"tick={_sim.Tick} screen={screen} player={PlayerControl.LocalPlayer} result={Result()} " +
               $"replay={(_replayPath != null ? "saved" : "none")} terrain_tool={(PlayerControl.TerrainTool ? 1 : 0)} " +
               $"falling={_sim.Physics.Falling.Count} trees_falling={_sim.Physics.FallingTrees.Count} dust={_fallingRenderer.Effects} icons={_unitIcons.Shown} {_hud.AlertFeed.Describe()} {_control.Describe()}" +
               (_testPanel != null ? " " + _testPanel.Describe() : "");
    }

    /// <summary>How the match went for you, for scripted tests: none while it goes on.</summary>
    private string Result()
    {
        if (!_ended) return "none";
        var match = _sim.Match;
        int me = PlayerControl.LocalPlayer;
        if (!match.Players.Contains(me)) return "over";
        if (match.InPlay(me)) return "victory";
        return match.Over && match.Winner < 0 ? "draw" : "defeat";
    }

    private void ApplySlice()
    {
        var cut = _slice.CutHeight;
        _terrain.SetSlice(cut);
        float y = cut ?? 100000f;
        _unitRenderer.Material.SetShaderParameter("slice_y", y);
        _selectionRenderer.Material.SetShaderParameter("slice_y", y);
        _ghostRenderer.SetSlice(y);
        _combatRenderer.Material.SetShaderParameter("slice_y", y);
        _bloodRenderer.Material.SetShaderParameter("slice_y", y);
        _unitIcons.Material.SetShaderParameter("slice_y", y);
    }

    /// <summary>
    /// Match setup. Default: the usual start (see <see cref="Simulation.SetUpUsualStart"/>), a town hall at
    /// each start area with four of every unit type that is trained in front of it, a wolf lair with its
    /// pack on the way from each start to the map centre, and a town for each city-state. Stress mode
    /// (count >= 0): the town halls, then that many units of yours in a square around the focus (columns
    /// without ground, such as deep water, are skipped); they are all on one side so that the walking
    /// benchmark measures walking, not a battle.
    /// </summary>
    private void SpawnUnits(int count, int fx, int fz)
    {
        if (count < 0)
        {
            foreach (int id in _sim.SetUpUsualStart())
                _spawned.Add((_sim.Units.Owner[_sim.Units.IndexOf(id)], id));
            foreach (var lair in _sim.Monsters.Lairs)
                GD.Print($"Wolf lair {lair.Id}: {lair.Monster.Id} den at {lair.X},{lair.Z}, pack of {lair.Pack.Count}");
            foreach (var town in _sim.Cities.Towns)
                GD.Print($"City-state {town.Def.Id}: town {town.Id} with its market at {town.X},{town.Y},{town.Z}, lair {town.LairId}");
            return;
        }
        _sim.PlaceStartingBuildings();
        var defs = _sim.Content.BaseUnits;
        int side = (int)Math.Ceiling(Math.Sqrt(count));
        for (int k = 0; _spawned.Count < count && k < count * 4; k++)
        {
            int x = Math.Clamp(fx - side / 2 + k % side, 0, WorldX - 1);
            int z = Math.Clamp(fz - side / 2 + k / side, 0, WorldZ - 1);
            int id = _sim.SpawnUnit(PlayerControl.LocalPlayer, defs[k % defs.Count].Id, x, z);
            if (id > 0) _spawned.Add((PlayerControl.LocalPlayer, id));
        }
    }

    /// <summary>
    /// --dusk-party=N: N of each soldier type that is trained and, if your faction heals the dusk tree, two of
    /// each of its healers, yours, in ranks of eight two columns apart from fifteen columns off the dusk tree
    /// (east if the ground there is about level with it, else west, south or north), out of its warden's
    /// reach. Nobody moves until ordered.
    /// </summary>
    private void SpawnDuskParty(int n)
    {
        var tree = _sim.DuskTree;
        if (!tree.Exists) return;
        int me = PlayerControl.LocalPlayer;
        var kinds = new List<UnitDef>();
        foreach (var d in _sim.Content.BaseUnits)
            if (d.Role != UnitRole.Worker && d.CanAttack)
                for (int k = 0; k < n; k++) kinds.Add(d);
        if (tree.UseFor(me) == DuskTreeUse.Heal)
            foreach (var d in _sim.Content.Units)
                if (d.Heal > 0 && d.Faction == _sim.Economy.Faction(me)) { kinds.Add(d); kinds.Add(d); }
        // The first side (east, west, south, north) whose ground fifteen columns out is about level with the tree's.
        int dx = 1, dz = 0;
        foreach (var (sx, sz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            int g = Walk.GroundLevel(_sim.World, tree.X + 15 * sx, tree.Z + 15 * sz);
            if (g >= 0 && Math.Abs(g - tree.Y) <= 3) { (dx, dz) = (sx, sz); break; }
        }
        for (int k = 0, placed = 0; placed < kinds.Count && k < kinds.Count * 4; k++)
        {
            int along = 15 + k / 8 * 2, across = -7 + k % 8 * 2;
            int x = Math.Clamp(tree.X + dx * along - dz * across, 0, WorldX - 1);
            int z = Math.Clamp(tree.Z + dz * along + dx * across, 0, WorldZ - 1);
            int id = _sim.SpawnUnit(me, kinds[placed].Id, x, z);
            if (id > 0) { _spawned.Add((me, id)); placed++; }
        }
    }

    /// <summary>
    /// --battle: n soldiers a side, every soldier type in turn, in ranks of eight two columns apart: yours
    /// six columns west of the focus facing east, the enemy's six columns east of it, and an enemy house
    /// about fourteen columns north of the focus where the ground allows. Nobody moves until ordered (or
    /// attacked).
    /// </summary>
    private void SpawnBattle(int n, int fx, int fz)
    {
        var soldiers = new List<string>();
        foreach (var d in _sim.Content.BaseUnits)
            if (d.Role != UnitRole.Worker && d.CanAttack) soldiers.Add(d.Id);
        if (soldiers.Count == 0) return;
        for (int side = 0; side < 2; side++)
        {
            int dir = side == 0 ? -1 : 1;
            for (int k = 0, placed = 0; placed < n && k < n * 4; k++)
            {
                int x = Math.Clamp(fx + dir * (6 + k / 8), 0, WorldX - 1);
                int z = Math.Clamp(fz - 7 + k % 8 * 2, 0, WorldZ - 1);
                if (_sim.SpawnUnit(side, soldiers[placed % soldiers.Count], x, z) > 0) placed++;
            }
        }
        var house = _sim.Content.Building("house");
        for (int k = 0; k < 64; k++)
            if (_sim.Economy.PlaceFinished(1, house, fx - 4 + k % 8, fz - 14 - k / 8) != null) break;
    }

    /// <summary>
    /// --lineup: for players 0 and 1, every unit type their faction can have, in data order, one every three
    /// columns from west to east centred on the focus: player 0's nine columns north of it, player 1's nine
    /// south, 18 apart, more than any unit sees, and on the hold stance, so that nobody walks off to fight.
    /// </summary>
    private void SpawnLineup(int fx, int fz)
    {
        for (int player = 0; player < 2; player++)
        {
            var faction = _sim.Economy.Faction(player);
            var types = new List<UnitDef>();
            foreach (var d in _sim.Content.Units)
                if (d.Faction == null || d.Faction == faction) types.Add(d);
            int z = Math.Clamp(fz + (player == 0 ? -9 : 9), 0, WorldZ - 1);
            for (int k = 0; k < types.Count; k++)
            {
                int x = Math.Clamp(fx - types.Count * 3 / 2 + 3 * k, 0, WorldX - 1);
                int id = _sim.SpawnUnit(player, types[k].Id, x, z);
                if (id > 0)
                {
                    _spawned.Add((player, id));
                    _sim.Units.Stance[_sim.Units.IndexOf(id)] = UnitStance.Hold;
                }
                else GD.Print($"Lineup: no room for {faction.Id} {types[k].Id} at {x},{z}");
            }
            GD.Print($"Lineup: {types.Count} {faction.Name} unit types in a row at z={z}");
        }
    }

    /// <summary>--goods: every town hall gets this much more of every good (match setup, before the first tick).</summary>
    private void GiveGoods(int amount)
    {
        foreach (var b in _sim.Buildings.All)
            if (b.Def.Kind == Rts.Sim.Economy.BuildingKind.TownHall)
                for (int g = 0; g < b.Stock.Length; g++) b.Stock[g] += amount;
    }

    /// <summary>
    /// --houses: up to this many finished houses of yours, on the free sites nearest your start point that
    /// are not south of it, where the town hall's door and the starting units are (every fifth column,
    /// nearest first, then north to south and west to east; match setup, before the first tick).
    /// </summary>
    private void GiveHouses(int count)
    {
        int player = PlayerControl.LocalPlayer;
        var house = _sim.Content.Building("house");
        int sx = -1, sz = -1;
        foreach (var start in _sim.Map.Starts)
            if (start.Player == player) (sx, sz) = (start.X, start.Z);
        if (sx < 0) return;
        const int Reach = 30;
        var sites = new List<(int D, int X, int Z)>();
        for (int dz = -Reach; dz <= 0; dz += 5)
        for (int dx = -Reach; dx <= Reach; dx += 5)
            sites.Add((dx * dx + dz * dz, sz + dz - house.SizeZ / 2, sx + dx - house.SizeX / 2));
        sites.Sort();
        int placed = 0;
        foreach (var (_, z, x) in sites)
        {
            if (placed == count) break;
            if (_sim.Economy.PlaceFinished(player, house, x, z) != null) placed++;
        }
    }

    /// <summary>
    /// --lights: this many finished torches in a ring around your town hall, and a campfire south of it
    /// (match setup, before the first tick).
    /// </summary>
    private void GiveLights(int count)
    {
        int player = PlayerControl.LocalPlayer;
        int sx = -1, sz = -1;
        foreach (var start in _sim.Map.Starts)
            if (start.Player == player) (sx, sz) = (start.X, start.Z);
        if (sx < 0) return;
        var torch = _sim.Content.Building("torch");
        _sim.Economy.PlaceFinished(player, _sim.Content.Building("campfire"), sx + 2, sz + 9);
        int placed = 0;
        for (int k = 0; k < count * 4 && placed < count; k++)
        {
            float a = Mathf.Tau * k / (count * 2) + 0.4f;
            int r = 7 + k % 3;
            if (_sim.Economy.PlaceFinished(player, torch, sx + Mathf.RoundToInt(Mathf.Cos(a) * r), sz + Mathf.RoundToInt(Mathf.Sin(a) * r)) != null) placed++;
        }
    }

    /// <summary>--xp: every unit of yours starts with this much experience (match setup, before the first tick).</summary>
    /// <summary>--blood: N columns stained in a square around (fx, fz), more toward the middle, and every unit bleeding.</summary>
    private void SpillBlood(int stains, int fx, int fz)
    {
        int side = (int)Math.Ceiling(Math.Sqrt(stains));
        for (int k = 0; k < stains; k++)
        {
            int x = Math.Clamp(fx - side / 2 + k % side, 0, WorldX - 1), z = Math.Clamp(fz - side / 2 + k / side, 0, WorldZ - 1);
            int top = _sim.World.SurfaceHeight(x, z);
            _sim.Blood.Spill(x, top, z, 20 + (k * 37 % 230), 0);
        }
        var u = _sim.Units;
        for (int i = 0; i < u.Count; i++)
        {
            u.Bleeding[i] = 1 << (int)BodyPart.Legs;
            u.BleedLeft[i * Body.Parts + (int)BodyPart.Legs] = 12; // about 12 seconds of bleeding, never a death
        }
    }

    private void GiveExperience(int xp)
    {
        var u = _sim.Units;
        for (int i = 0; i < u.Count; i++)
            if (u.Owner[i] == PlayerControl.LocalPlayer) u.Xp[i] = Math.Min(xp, EvolutionSystem.MaxXp);
    }

    private BlockType TopBlock(int x, int z)
    {
        for (int y = WorldY - 1; y >= 0; y--)
            if (_sim.World.Get(x, y, z) is var t && t != BlockType.Air) return t;
        return BlockType.Air;
    }

    /// <summary>Camera focus from --focus: centre, start0, start1, cavern, swamp, lava, or X,Z in blocks.</summary>
    private (int X, int Z) FocusPoint(string focus)
    {
        var map = _sim.Map;
        if (focus.StartsWith("start") && int.TryParse(focus[5..], out var p) && p >= 0 && p < map.Starts.Count)
            return (map.Starts[p].X, map.Starts[p].Z);
        if (focus == "cavern" && map.Cavern is { } c) return (c.X, c.Z);
        if (focus == "dusk" && _sim.DuskTree.Exists) return (_sim.DuskTree.X, _sim.DuskTree.Z);
        if (focus is "swamp" or "lava")
        {
            // The middle of the swamp, or the first pool of lava.
            long sumX = 0, sumZ = 0, n = 0;
            for (int bz = 0; bz < WorldZ; bz++)
            for (int bx = 0; bx < WorldX; bx++)
            {
                bool hit = focus == "swamp" ? map.BiomeAt(bx, bz) == Rts.Sim.World.Biome.Swamp
                    : TopBlock(bx, bz) == BlockType.Lava;
                if (!hit) continue;
                if (focus == "lava") return (bx, bz);
                sumX += bx; sumZ += bz; n++;
            }
            if (n > 0) return ((int)(sumX / n), (int)(sumZ / n));
        }
        var parts = focus.Split(',');
        if (parts.Length == 2 && int.TryParse(parts[0], out var x) && int.TryParse(parts[1], out var z))
            return (Mathf.Clamp(x, 0, WorldX - 1), Mathf.Clamp(z, 0, WorldZ - 1));
        return (WorldX / 2, WorldZ / 2);
    }

    private static float ArgFloat(Dictionary<string, string> args, string key, float fallback) =>
        args.TryGetValue(key, out var v) && float.TryParse(v, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : fallback;

    private static Dictionary<string, string> ParseArgs()
    {
        var result = new Dictionary<string, string>();
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            var trimmed = arg.TrimStart('-');
            int eq = trimmed.IndexOf('=');
            if (eq > 0) result[trimmed[..eq]] = trimmed[(eq + 1)..];
            else result[trimmed] = "true";
        }
        return result;
    }
}
