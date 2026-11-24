using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rts.Sim;
using Rts.Sim.Automation;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;

namespace Rts.Game;

/// <summary>
/// The local player's mouse and keyboard: selecting units and buildings, giving orders, placing
/// buildings and roads, control groups, the time controls, the height slice and the terrain tool.
/// Selection lives here, outside the simulation; orders go to the simulation as commands, the same way
/// a network peer's would.
///
///   Left click / drag     select a unit or building / box-select your units (Shift adds; double-click
///                         picks every unit of that type on screen)
///   Right click           depends on what is under the cursor: units attack an enemy unit or building,
///                         except an enemy storehouse, which they capture (Ctrl+right click burns it);
///                         workers gather a tree, bush, stone or ore, build a site or work a farm or
///                         smelter; caravans trade with a city-state's town; otherwise move there,
///                         keeping their shape (Ctrl+right click: attack-move, fighting whatever they meet
///                         on the way; Alt+right click on the ground: patrol, walking there and back
///                         again and again, fighting whatever they meet, until given another order; Alt+right
///                         click on one of your units or buildings: guard it). With only your barracks or town
///                         hall selected: set its rally point (on the building itself: clear it)
///   Right drag            draw a line: the selected units spread out along it facing forward from it,
///                         melee in front and ranged behind (with Ctrl, they attack-move there)
///   H G C B M N           place a house, storehouse, farm, barracks, smelter or town hall (the selected
///                         workers build it; Shift keeps placing)
///   L                     lay a road: click where it starts, then where it ends
///   Y                     wall tool: drag a line for the selected workers to wall off (Shift keeps the tool);
///                         O places a gate, which only your units walk through (draw a wall through it)
///   '                     bridge tool: drag from bank to bank for the selected workers to bridge, a level
///                         deck across deep water or a drop (Shift keeps the tool); Ctrl+right click a
///                         bridge to knock that column out
///   U                     train at the selected building (a worker at a town hall, a spearman at a barracks)
///   Shift+U               the selected town hall's governor: off, growth, army, defence, then off again
///   K                     open or close the tech tree
///   Ctrl+1..9, 1..9       assign / recall a control group (press twice to centre the camera on it)
///   Space, -, +           pause, slower, faster (0.5x to 4x)
///   PgDn, PgUp, Home      lower / raise / turn off the height slice
///   T                     terrain tool: left click digs, right click places stone (Esc to leave); not in
///                         matches from the start screen (see TerrainTool)
///   X                     tunnel tool: the selected sappers tunnel to the block clicked, or under the
///                         part of a building clicked so that it falls (Shift keeps the tool, Esc to leave)
///   J, Shift+J            the selected workers find their own jobs, or stop doing so / all your workers
///                         and those you train later
///   P                     the selected building repeats its training, or stops
///   Shift+P               rebuild orders: your destroyed buildings are put back where they stood, or not
///   V                     the selected soldiers' stance: aggressive, hold their ground, retreat when hurt
///   .                     select your next idle worker and centre the camera on it (as the HUD's Idle
///                         workers button does)
///   F2                    select all your combat units, leaving workers and caravans out (as the HUD's
///                         Army button does)
/// Evolving has no key: the selection panel has a button for each form the selected units can take, and
/// one that picks the form your units of that type take on their own (see <see cref="EvolutionSystem"/>).
/// Neither has taming: the selection panel has Ride and Recruit buttons for the beaten monsters selected,
/// and the top bar's Beaten button selects your next one (see <see cref="MonsterSystem"/>). A city-state's
/// town is selected by clicking it, or with its button in the top bar; its panel has buttons for gifts
/// and, for its ally, for hiring its mercenaries (see <see cref="CityStateSystem"/>).
/// </summary>
public partial class PlayerControl : Node
{
    /// <summary>The player you play (0 unless the game was started with --player).</summary>
    public static int LocalPlayer { get; internal set; }
    /// <summary>
    /// Whether the terrain tool (T: dig any block, place stone) can be used. It is a sandbox tool for trying
    /// things out, so matches started from the start screen go without it.
    /// </summary>
    public static bool TerrainTool { get; internal set; } = true;
    private const float DragThreshold = 6f;
    private const ulong DoubleTapMs = 350;

    public enum ToolMode { Select, Terrain, Build, Road, Tunnel, Dig, Wall, Bridge, Ramp, Ladder, Dam, EarthWall, IceBridge, Chasm, GrowForest, Fell, Prospect, Stampede, Gaze, Test }

    /// <summary>Building hotkeys, in the order the HUD lists them; Key.None for a building placed from its button only.</summary>
    public static readonly (Key Key, string Building)[] BuildKeys =
    {
        (Key.H, "house"), (Key.G, "storehouse"), (Key.C, "farm"), (Key.B, "barracks"), (Key.M, "smelter"), (Key.N, "town_hall"),
        (Key.None, "orchard"), (Key.None, "quarry"), (Key.None, "mine"), (Key.None, "fishing_hut"), (Key.None, "forester_lodge"), (Key.None, "hunting_lodge"), (Key.None, "sawmill"), (Key.None, "mason_yard"), (Key.None, "siege_workshop"), (Key.None, "dock"), (Key.None, "shrine"),
        (Key.O, "barrier_gate"), (Key.Slash, "watchtower"), (Key.Backslash, "stone_tower"), (Key.None, "listening_post"), (Key.I, "torch"), (Key.Semicolon, "campfire"),
    };

    /// <summary>The building the wall tool lays, a column at a time.</summary>
    public const string WallBuilding = "barrier";
    /// <summary>The building the bridge tool lays, a deck column at a time.</summary>
    public const string BridgeBuilding = "bridge";

    /// <summary>What the build tool would place under the cursor, and whether it fits.</summary>
    public readonly record struct Placement(bool Visible, int X, int Y, int Z, PlaceResult Result);

    private Simulation _sim = null!;
    private CameraRig _rig = null!;
    private GameClock _clock = null!;
    private HeightSlice _slice = null!;
    private SelectionRenderer _rings = null!;

    private readonly List<int> _selected = new();
    private readonly List<int> _preview = new();
    private readonly HashSet<int> _selectedSet = new();
    private readonly List<int>[] _groups = new List<int>[10];
    private bool _leftDown, _dragging;
    private Vector2 _pressPos, _mousePos;
    private int _lastGroup = -1;
    private ulong _lastGroupMs;
    private bool _roadStarted;
    private int _roadX, _roadZ;
    private readonly List<(int X, int Y, int Z)> _roadPreview = new();
    private bool _digDragging;
    private int _digX, _digZ, _digLevel;
    private readonly List<(int X, int Y, int Z)> _digPreview = new();
    private bool _wallDragging;
    private int _wallX, _wallZ;
    private readonly List<(int X, int Y, int Z, bool Ok)> _wallPreview = new();
    private bool _rightDown, _lineDragging, _rightCtrl, _rightAlt;
    private Vector2 _rightPos;
    private readonly List<Vector3> _formationPreview = new();

    public IReadOnlyList<int> Selected => _selected;
    public IReadOnlyList<int> Preview => _preview;
    public ToolMode Tool { get; private set; }
    /// <summary>The selected building's id, or 0. Selecting units clears it, and the other way round.</summary>
    public int SelectedBuilding { get; private set; }
    /// <summary>The selected city-state town's id, or 0. Selecting units or a building clears it, and the other way round.</summary>
    public int SelectedCity { get; private set; }
    /// <summary>The building the build tool places.</summary>
    public BuildingDef? BuildDef { get; private set; }
    public Placement Ghost { get; private set; }
    /// <summary>
    /// Planner mode (Tab, or the Plan button): what the build tool places is a plan, marked out but not paid for
    /// or built until you start it (right click it). Without it, a building you cannot afford is planned too.
    /// </summary>
    public bool PlanMode { get; private set; }
    /// <summary>The build tool's next click makes a plan rather than a site.</summary>
    public bool Planning => PlanMode || Ghost.Result == PlaceResult.CannotAfford;
    /// <summary>The road the road tool would lay, column by column at ground level (empty until it has a start).</summary>
    public IReadOnlyList<(int X, int Y, int Z)> RoadPreview => _roadPreview;
    public bool RoadStarted => _roadStarted;
    /// <summary>How many levels the dig tool digs down, below the ground where the drag started.</summary>
    public int DigDepth { get; private set; } = 2;
    /// <summary>The columns the dig tool would mark, at ground level (empty until a drag starts).</summary>
    public IReadOnlyList<(int X, int Y, int Z)> DigPreview => _digPreview;
    public bool DigStarted => _digDragging;
    /// <summary>
    /// The columns the wall tool would wall off along the line being dragged, at ground level, each with
    /// whether a wall goes there (room, and goods enough by then); empty until a drag starts.
    /// </summary>
    public IReadOnlyList<(int X, int Y, int Z, bool Ok)> WallPreview => _wallPreview;
    public bool WallStarted => _wallDragging;
    /// <summary>Where each selected unit would stand in the line being dragged out with the right button (block coordinates; empty when none is).</summary>
    public IReadOnlyList<Vector3> FormationPreview => _formationPreview;
    /// <summary>The tech tree screen is showing.</summary>
    public bool TechTreeOpen { get; private set; }
    public bool Dragging => _dragging;
    public Rect2 DragRect => new Rect2(_pressPos, _mousePos - _pressPos).Abs();

    /// <summary>
    /// Test mode's place tool (see <see cref="TestPanel"/>): with <see cref="ToolMode.Test"/>, a left click on
    /// the map calls this with the column clicked and its ground level (the highest solid block under the
    /// cursor, or the ground under a tree there). It stays on until right click or Esc.
    /// </summary>
    public Action<int, int, int>? TestPlace { get; set; }
    /// <summary>What test mode's place tool puts down, for the tool hint.</summary>
    public string TestPlacing { get; private set; } = "";

    /// <summary>Starts test mode's place tool, placing what <paramref name="what"/> names (see <see cref="TestPlace"/>).</summary>
    public void StartTestPlace(string what)
    {
        SetTool(ToolMode.Test);
        TestPlacing = what;
        Changed?.Invoke();
    }

    /// <summary>Leaves whatever tool is on for plain selecting.</summary>
    public void StopTool() => SetTool(ToolMode.Select);

    /// <summary>Test mode: you play <paramref name="player"/> from now on (the selection is cleared).</summary>
    public void SwitchPlayer(int player)
    {
        LocalPlayer = player;
        SetTool(ToolMode.Select);
        SetSelection(Array.Empty<int>());
    }

    /// <summary>Raised whenever the selection or tool changes, so the HUD can refresh.</summary>
    public event Action? Changed;

    public void Init(Simulation sim, CameraRig rig, GameClock clock, HeightSlice slice, SelectionRenderer rings, FogOfWar fog)
    {
        _sim = sim;
        _rig = rig;
        _clock = clock;
        _slice = slice;
        _rings = rings;
        Fog = fog;
    }

    /// <summary>Fog of war as you see it: what shows, and the world as you know it.</summary>
    public FogOfWar Fog { get; private set; } = null!;

    /// <summary>The world as you know it (under fog of war, as you last saw it): what clicks hit.</summary>
    private VoxelWorld World => Fog.World;

    /// <summary>
    /// Drops units and buildings that no longer exist, refreshes the units inside a box being dragged, and
    /// works out what the build or road tool would place under the cursor.
    /// </summary>
    public void UpdateFrame()
    {
        int before = _selected.Count;
        // Units that are gone, and other players' units that have gone out of sight.
        _selected.RemoveAll(id => _sim.Units.IndexOf(id) is var i && (i < 0 || !Fog.Shows(i)));
        if (_selected.Count != before)
        {
            _selectedSet.Clear();
            _selectedSet.UnionWith(_selected);
            Changed?.Invoke();
        }
        if (SelectedBuilding != 0 && _sim.Buildings.Get(SelectedBuilding) == null)
        {
            SelectedBuilding = 0;
            Changed?.Invoke();
        }
        _preview.Clear();
        if (_dragging) UnitsInRect(DragRect, _preview);
        UpdateGhost();
        UpdateRoadPreview();
        UpdateDigPreview();
        UpdateWallPreview();
        UpdateFormationPreview();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                if (mb.Pressed)
                {
                    _mousePos = mb.Position;
                    switch (Tool)
                    {
                        case ToolMode.Terrain:
                            Dig(mb.Position);
                            return;
                        case ToolMode.Build:
                            PlaceBuilding(mb.ShiftPressed);
                            return;
                        case ToolMode.Road:
                            RoadClick(mb.Position, mb.ShiftPressed);
                            return;
                        case ToolMode.Tunnel:
                            Tunnel(mb.Position, mb.ShiftPressed);
                            return;
                        case ToolMode.Dig:
                            DigStart(mb.Position);
                            return;
                        case ToolMode.Wall or ToolMode.Bridge or ToolMode.Ramp or ToolMode.Ladder or ToolMode.Dam or ToolMode.EarthWall or ToolMode.IceBridge or ToolMode.Chasm or ToolMode.GrowForest or ToolMode.Fell or ToolMode.Prospect or ToolMode.Stampede:
                            WallStart(mb.Position);
                            return;
                        case ToolMode.Gaze:
                            GazeAt(mb.Position);
                            GetViewport().SetInputAsHandled();
                            return;
                        case ToolMode.Test:
                            if (GroundUnder(mb.Position, out int tx, out int ty, out int tz)) TestPlace?.Invoke(tx, ty - 1, tz);
                            GetViewport().SetInputAsHandled();
                            return;
                    }
                    _leftDown = true;
                    _dragging = false;
                    _pressPos = _mousePos = mb.Position;
                    if (mb.DoubleClick) SelectSameTypeOnScreen(mb.Position);
                }
                else if (Tool == ToolMode.Dig && _digDragging)
                {
                    _mousePos = mb.Position;
                    DigEnd(mb.ShiftPressed);
                }
                else if (Tool is ToolMode.Wall or ToolMode.Bridge or ToolMode.Ramp or ToolMode.Ladder or ToolMode.Dam or ToolMode.EarthWall or ToolMode.IceBridge or ToolMode.Chasm or ToolMode.GrowForest or ToolMode.Fell or ToolMode.Prospect or ToolMode.Stampede && _wallDragging)
                {
                    _mousePos = mb.Position;
                    WallEnd(mb.ShiftPressed);
                }
                else if (_leftDown)
                {
                    _leftDown = false;
                    _mousePos = mb.Position;
                    if (_dragging) BoxSelect(DragRect, mb.ShiftPressed);
                    else ClickSelect(mb.Position, mb.ShiftPressed);
                    _dragging = false;
                    _preview.Clear();
                }
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } mb:
                _mousePos = mb.Position;
                if (Tool == ToolMode.Terrain) Place(mb.Position);
                else if (Tool != ToolMode.Select) SetTool(ToolMode.Select);
                else
                {
                    // The order waits for the release: a drag draws a formation line instead.
                    _rightDown = true;
                    _lineDragging = false;
                    _rightPos = mb.Position;
                    _rightCtrl = mb.CtrlPressed;
                    _rightAlt = mb.AltPressed;
                }
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: false } mb:
                _mousePos = mb.Position;
                if (!_rightDown) break;
                _rightDown = false;
                if (_lineDragging) OrderLine(_rightPos, mb.Position, _rightCtrl || mb.CtrlPressed);
                else if (_rightAlt || mb.AltPressed) Patrol(_rightPos);
                else Order(_rightPos, _rightCtrl || mb.CtrlPressed);
                _lineDragging = false;
                _formationPreview.Clear();
                break;
            case InputEventMouseMotion mm:
                _mousePos = mm.Position;
                if (_leftDown && !_dragging && _mousePos.DistanceTo(_pressPos) > DragThreshold) _dragging = true;
                if (_rightDown && !_lineDragging && _mousePos.DistanceTo(_rightPos) > DragThreshold && OwnSelected().Count > 0)
                    _lineDragging = true;
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                HandleKey(key);
                break;
        }
    }

    private void HandleKey(InputEventKey key)
    {
        var code = key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode;
        if (code >= Key.Key0 && code <= Key.Key9)
        {
            int g = (int)(code - Key.Key0);
            if (key.CtrlPressed) AssignGroup(g);
            else RecallGroup(g);
            return;
        }
        if (code == Key.O && key.ShiftPressed && !key.CtrlPressed)
        {
            ToggleDam(); // Shift+O, before O places a gate
            GetViewport().SetInputAsHandled();
            return;
        }
        foreach (var (buildKey, building) in BuildKeys)
        {
            if (buildKey == Key.None || code != buildKey || key.CtrlPressed) continue;
            StartBuild(building);
            GetViewport().SetInputAsHandled();
            return;
        }
        switch (key.Keycode)
        {
            case Key.L when key.ShiftPressed: ToggleGrowForest(); break;
            case Key.L: StartRoad(); break;
            case Key.Comma: Bloom(); break;
            case Key.Y when key.ShiftPressed: ToggleEarthWall(); break;
            case Key.Y: ToggleWall(); break;
            case Key.Apostrophe when key.ShiftPressed: ToggleIceBridge(); break;
            case Key.Apostrophe: ToggleBridge(); break;
            case Key.U:
                if (_sim.Buildings.Get(SelectedBuilding) is { Def.IsWall: true }) UpgradeSelected(stretch: !key.ShiftPressed);
                // Any other building with a bigger one to become: Ctrl+U, or U alone when it trains nothing.
                else if (_sim.Buildings.Get(SelectedBuilding) is { Def.Upgrade: not null } grows && !key.ShiftPressed
                         && (key.CtrlPressed || grows.Def.Trains.Length == 0))
                    UpgradeSelected(stretch: false);
                else if (key.ShiftPressed && _sim.Buildings.Get(SelectedBuilding) is { Def.Kind: BuildingKind.TownHall } hall)
                    SetGovernor((GovernorFocus)(((int)hall.Governor + 1) % Enum.GetValues<GovernorFocus>().Length));
                else TrainAtSelected(null);
                break;
            case Key.J:
                if (key.CtrlPressed) TogglePriorities();
                else if (key.ShiftPressed) ToggleAutoAll();
                else ToggleAutoSelected();
                break;
            case Key.P:
                if (key.ShiftPressed) ToggleRebuild();
                else ToggleRepeat();
                break;
            case Key.Tab: TogglePlanMode(); break;
            case Key.Delete or Key.Backspace: CancelSelectedPlan(); break;
            case Key.V when key.CtrlPressed: ToggleShieldWall(); break;
            case Key.C when key.CtrlPressed: BeatCharge(); break;
            case Key.M when key.CtrlPressed: Molt(); break;
            case Key.S when key.CtrlPressed: ToggleStampede(); break;
            case Key.G when key.CtrlPressed: ToggleGaze(); break;
            case Key.V when key.ShiftPressed: DefendTown(); break;
            case Key.V: CycleStance(); break;
            case Key.K when key.ShiftPressed: ToggleLadder(); break;
            case Key.K: ToggleTechTree(); break;
            case Key.Period: SelectNextIdleWorker(); break;
            case Key.F2: SelectArmy(); break;
            case Key.Space: _clock.TogglePause(); break;
            case Key.Minus or Key.KpSubtract: _clock.Slower(); break;
            case Key.Equal or Key.Plus or Key.KpAdd: _clock.Faster(); break;
            case Key.Pagedown: _slice.Lower(SliceStartLevel()); break;
            case Key.Pageup: _slice.Raise(); break;
            case Key.Home: _slice.Off(); break;
            case Key.T when TerrainTool:
                SetTool(Tool == ToolMode.Terrain ? ToolMode.Select : ToolMode.Terrain);
                break;
            case Key.X when key.CtrlPressed: ToggleFell(); break;
            case Key.X when key.ShiftPressed: ToggleRamp(); break;
            case Key.X:
                SetTool(Tool == ToolMode.Tunnel ? ToolMode.Select : ToolMode.Tunnel);
                break;
            case Key.Z when key.CtrlPressed: ToggleProspect(); break;
            case Key.Z when key.ShiftPressed: ToggleChasm(); break;
            case Key.Z:
                ToggleDig();
                break;
            case Key.Bracketleft when Tool == ToolMode.Dig:
                ChangeDigDepth(-1);
                break;
            case Key.Bracketright when Tool == ToolMode.Dig:
                ChangeDigDepth(+1);
                break;
            case Key.Escape:
                if (TechTreeOpen) ToggleTechTree();
                else if (PrioritiesOpen) TogglePriorities();
                else if (Tool != ToolMode.Select) SetTool(ToolMode.Select);
                else SetSelection(Array.Empty<int>());
                break;
            default: return;
        }
        GetViewport().SetInputAsHandled();
    }

    /// <summary>Where the slice starts when first lowered: just above head height at the camera focus.</summary>
    private int SliceStartLevel()
    {
        var focus = _rig.Position / ChunkMesher.BlockSize;
        int x = Math.Clamp(Mathf.FloorToInt(focus.X), 0, _sim.World.SizeX - 1);
        int z = Math.Clamp(Mathf.FloorToInt(focus.Z), 0, _sim.World.SizeZ - 1);
        int ground = Walk.GroundLevel(_sim.World, x, z);
        return ground >= 0 ? ground + Walk.Clearance : _sim.World.SizeY / 2;
    }

    // ---- Selection ----

    private void ClickSelect(Vector2 screen, bool add)
    {
        int id = PickUnit(screen);
        if (id == 0)
        {
            var building = BuildingUnder(screen);
            if (building != null) SelectBuilding(building.Id);
            else if (CityUnder(screen) is { } city) SelectCity(city.Id);
            else if (!add) SetSelection(Array.Empty<int>());
            return;
        }
        bool own = OwnerOf(id) == LocalPlayer;
        if (add && own && _selected.Count > 0 && OwnerOf(_selected[0]) == LocalPlayer)
        {
            // Shift-click toggles one of your units in or out of the selection.
            var next = new List<int>(_selected);
            if (!next.Remove(id)) next.Add(id);
            SetSelection(next);
        }
        else SetSelection(new[] { id });
    }

    private void BoxSelect(Rect2 rect, bool add)
    {
        var inside = new List<int>();
        UnitsInRect(rect, inside);
        var next = new List<int>();
        if (add && _selected.Count > 0 && OwnerOf(_selected[0]) == LocalPlayer) next.AddRange(_selected);
        foreach (int id in inside)
            if (!next.Contains(id)) next.Add(id);
        if (next.Count > 0 || !add) SetSelection(next);
    }

    private void SelectSameTypeOnScreen(Vector2 screen)
    {
        int id = PickUnit(screen);
        if (id == 0 || OwnerOf(id) != LocalPlayer) return;
        int def = _sim.Units.Def[_sim.Units.IndexOf(id)];
        var all = new List<int>();
        UnitsInRect(GetViewport().GetVisibleRect(), all);
        all.RemoveAll(u => _sim.Units.Def[_sim.Units.IndexOf(u)] != def);
        SetSelection(all);
        _leftDown = false; // the release that follows is part of the double click, not a new selection
    }

    public void SetSelection(IReadOnlyList<int> ids)
    {
        _selected.Clear();
        _selectedSet.Clear();
        foreach (int id in ids)
            if (_sim.Units.Exists(id) && _selectedSet.Add(id)) _selected.Add(id);
        SelectedBuilding = 0;
        SelectedCity = 0;
        Changed?.Invoke();
    }

    public void SelectBuilding(int id)
    {
        _selected.Clear();
        _selectedSet.Clear();
        SelectedBuilding = _sim.Buildings.Get(id) != null ? id : 0;
        SelectedCity = 0;
        Changed?.Invoke();
    }

    public void SelectCity(int id)
    {
        _selected.Clear();
        _selectedSet.Clear();
        SelectedBuilding = 0;
        SelectedCity = _sim.Cities.Get(id) != null ? id : 0;
        Changed?.Invoke();
    }

    /// <summary>The unit drawn nearest to a screen point (within its size on screen), or 0.</summary>
    public int PickUnit(Vector2 screen)
    {
        var cam = _rig.Camera;
        var u = _sim.Units;
        float pixelsPerMetre = GetViewport().GetVisibleRect().Size.Y / (2f * Mathf.Tan(Mathf.DegToRad(cam.Fov) / 2f));
        float best = float.MaxValue;
        int bestId = 0;
        for (int i = 0; i < u.Count; i++)
        {
            // Those aboard a ship are picked as the ship.
            if (u.Aboard[i] != 0 || !_slice.Shows(u.Y[i]) || !Fog.Shows(i)) continue;
            var p = Centre(i);
            if (cam.IsPositionBehind(p)) continue;
            float depth = cam.GlobalPosition.DistanceTo(p);
            float size = _sim.Content.Units[u.Def[i]].IsBoat ? 1.5f : 0.5f;
            float radius = Mathf.Max(8f, size * pixelsPerMetre / depth);
            float d2 = cam.UnprojectPosition(p).DistanceSquaredTo(screen);
            if (d2 <= radius * radius && d2 < best)
            {
                best = d2;
                bestId = u.Id[i];
            }
        }
        return bestId;
    }

    /// <summary>Your units whose middle is drawn inside a screen rectangle, in id order (a city-state's guards and tribute left out).</summary>
    private void UnitsInRect(Rect2 rect, List<int> result)
    {
        var cam = _rig.Camera;
        var u = _sim.Units;
        for (int i = 0; i < u.Count; i++)
        {
            // A city-state's guards and tribute caravans go about their own business: they are not boxed.
            // Nor are those aboard a ship: the ship is.
            if (u.Owner[i] != LocalPlayer || u.Town[i] != 0 || u.Aboard[i] != 0 || !_slice.Shows(u.Y[i])) continue;
            var p = Centre(i);
            if (cam.IsPositionBehind(p)) continue;
            if (rect.HasPoint(cam.UnprojectPosition(p))) result.Add(u.Id[i]);
        }
        result.Sort();
    }

    private Vector3 Centre(int i)
    {
        var u = _sim.Units;
        return new Vector3(u.X[i].ToFloat(), u.Y[i] + 1f, u.Z[i].ToFloat()) * ChunkMesher.BlockSize;
    }

    private int OwnerOf(int id) => _sim.Units.Owner[_sim.Units.IndexOf(id)];

    // ---- Orders ----

    private List<int> OwnSelected()
    {
        var own = new List<int>(_selected.Count);
        foreach (int id in _selected)
            if (_sim.Units.Exists(id) && OwnerOf(id) == LocalPlayer) own.Add(id);
        return own;
    }

    /// <summary>The selected units of yours that are workers, in id order.</summary>
    private List<int> OwnWorkers(List<int> own) => OwnWithRole(own, UnitRole.Worker);

    /// <summary>Those of the units with the role, in the same order.</summary>
    private List<int> OwnWithRole(List<int> own, UnitRole role)
    {
        var list = new List<int>(own.Count);
        foreach (int id in own)
            if (_sim.Content.Units[_sim.Units.Def[_sim.Units.IndexOf(id)]].Role == role) list.Add(id);
        return list;
    }

    /// <summary>The selected units of yours that are sappers, in id order.</summary>
    public List<int> OwnSappers() => OwnWithRole(OwnSelected(), UnitRole.Sapper);

    /// <summary>The selected workers of yours.</summary>
    public List<int> OwnWorkers() => OwnWorkers(OwnSelected());

    /// <summary>The selected units of yours that can cast the spell (mages), in id order.</summary>
    public List<int> OwnCasters(Spell spell = Spell.EarthWall)
    {
        var list = new List<int>();
        foreach (int id in OwnSelected())
            if (_sim.Spells.Has(_sim.Units.IndexOf(id), spell)) list.Add(id);
        return list;
    }

    /// <summary>
    /// A right click: on an enemy unit, attack it (units that cannot fight go to it); on a building of
    /// yours, workers build it, work it or unload there, and ranged units take posts on top of a wall they
    /// can stand on (the rest go to its door); on an enemy storehouse,
    /// capture it (burn it with Ctrl); on another enemy building, attack it; on a tree, bush, stone or ore,
    /// workers gather it (the rest go next to it); anywhere else, move there, or with Ctrl attack-move there.
    /// </summary>
    private void Order(Vector2 screen, bool ctrl)
    {
        var own = OwnSelected();
        if (own.Count == 0)
        {
            // Right click on a plan of yours starts it, with nobody selected too (automatic workers take it on).
            if (BuildingUnder(screen) is { Planned: true } plan && plan.Owner == LocalPlayer) StartPlan(plan, own);
            else Rally(screen);
            return;
        }
        float bs = ChunkMesher.BlockSize;
        int target = PickUnit(screen);
        // A click on someone aboard a ship is a click on the ship.
        if (target > 0 && _sim.Units.Aboard[_sim.Units.IndexOf(target)] is int ship and > 0 && _sim.Units.Exists(ship)) target = ship;
        if (target > 0 && OwnerOf(target) == LocalPlayer && IsBoat(target) && _sim.Content.Units[_sim.Units.Def[_sim.Units.IndexOf(target)]].Seats > 0)
        {
            // Your own ship: the rest of the selection walks aboard.
            var riders = own.FindAll(id => id != target && !IsBoat(id));
            if (riders.Count > 0)
            {
                var u = _sim.Units;
                int t = u.IndexOf(target);
                _sim.Schedule(_sim.Tick, new BoardCommand(LocalPlayer, riders.ToArray(), target));
                _rings.ShowMarker(new Vector3(u.X[t].ToFloat(), u.Y[t], u.Z[t].ToFloat()) * bs, SelectionRenderer.Work);
                return;
            }
        }
        if (target > 0 && OwnerOf(target) != LocalPlayer)
        {
            var u = _sim.Units;
            int t = u.IndexOf(target);
            // Units that cannot fight go to where the enemy stands instead.
            var fighters = own.FindAll(id => _sim.Content.Units[u.Def[u.IndexOf(id)]].CanAttack);
            if (fighters.Count > 0) _sim.Schedule(_sim.Tick, new AttackCommand(LocalPlayer, fighters.ToArray(), target));
            own.RemoveAll(fighters.Contains);
            if (own.Count > 0)
                _sim.Schedule(_sim.Tick, new MoveCommand(LocalPlayer, own.ToArray(), u.X[t].FloorToInt(), u.Z[t].FloorToInt(), u.Y[t]));
            _rings.ShowMarker(new Vector3(u.X[t].ToFloat(), u.Y[t], u.Z[t].ToFloat()) * bs, SelectionRenderer.Other);
            return;
        }
        var building = BuildingUnder(screen);
        if (building is { Def.IsBridge: true, State: BuildingState.Active } && ctrl)
        {
            // Ctrl+right click on a bridge (yours too): knock that column out, cutting the way across.
            var fighters = own.FindAll(id => _sim.Content.Units[_sim.Units.Def[_sim.Units.IndexOf(id)]].CanAttack);
            if (fighters.Count > 0) _sim.Schedule(_sim.Tick, new AttackBuildingCommand(LocalPlayer, fighters.ToArray(), building.Id));
            _rings.ShowMarker(new Vector3(building.X + 0.5f, building.Y + 1, building.Z + 0.5f) * bs, SelectionRenderer.Other);
            return;
        }
        // A finished bridge is walked onto like ground, whoever built it.
        if (building != null && !(building.Def.IsBridge && building.State == BuildingState.Active))
        {
            OrderAtBuilding(own, building, ctrl);
            return;
        }
        if (CityUnder(screen) is { } city)
        {
            // Caravans trade there; the rest walk to where the click was.
            var caravans = OwnWithRole(own, UnitRole.Trader);
            if (caravans.Count > 0)
            {
                _sim.Schedule(_sim.Tick, new TradeCommand(LocalPlayer, caravans.ToArray(), city.Id));
                _rings.ShowMarker(new Vector3(city.X + 0.5f, city.Y, city.Z + 0.5f) * bs, SelectionRenderer.Work);
                own.RemoveAll(caravans.Contains);
                if (own.Count == 0) return;
            }
        }
        if (DuskTreeUnder(screen))
        {
            // Battle priests heal the dusk tree (the Orcs' fighters hack it down) once its warden is gone.
            var tree = _sim.DuskTree;
            var u = _sim.Units;
            var tending = own.FindAll(id => tree.CanWork(u.IndexOf(id)));
            if (tending.Count > 0 && tree.CanUse(LocalPlayer, tending.ConvertAll(u.IndexOf)))
            {
                _sim.Schedule(_sim.Tick, new DuskTreeCommand(LocalPlayer, tending.ToArray()));
                _rings.ShowMarker(new Vector3(tree.X + 0.5f, tree.Y, tree.Z + 0.5f) * bs, SelectionRenderer.Work);
                own.RemoveAll(tending.Contains);
                if (own.Count == 0) return;
            }
        }
        if (RuinUnder(screen) is { } ruin)
        {
            // An old ruin nobody has searched yet: the selected units walk in and search it.
            var u = _sim.Units;
            var searching = own.FindAll(id => _sim.Ruins.CanSearch(u.IndexOf(id)));
            if (searching.Count > 0)
            {
                _sim.Schedule(_sim.Tick, new SearchRuinCommand(LocalPlayer, searching.ToArray(), ruin.Index));
                _rings.ShowMarker(new Vector3(ruin.X + 0.5f, ruin.Y + 1, ruin.Z + 0.5f) * bs, SelectionRenderer.Work);
                own.RemoveAll(searching.Contains);
                if (own.Count == 0) return;
            }
        }
        if (ResourceUnder(screen, out int rx, out int ry, out int rz))
        {
            var workers = OwnWorkers(own);
            if (workers.Count > 0)
            {
                _sim.Schedule(_sim.Tick, new GatherCommand(LocalPlayer, workers.ToArray(), rx, ry, rz));
                own.RemoveAll(workers.Contains);
                _rings.ShowMarker(new Vector3(rx + 0.5f, ry, rz + 0.5f) * bs, SelectionRenderer.Work);
                if (own.Count == 0) return;
            }
        }
        if (!GroundUnder(screen, out int x, out int y, out int z)) return;
        // Blood on the ground there: workers clean it up, and the blood around it.
        if (_sim.Blood.At(x, z) > 0 || BloodNear(x, z))
        {
            var cleaners = OwnWorkers(own);
            if (cleaners.Count > 0)
            {
                _sim.Schedule(_sim.Tick, new CleanCommand(LocalPlayer, cleaners.ToArray(), x, z));
                own.RemoveAll(cleaners.Contains);
                _rings.ShowMarker(new Vector3(x + 0.5f, y, z + 0.5f) * bs, SelectionRenderer.Work);
                if (own.Count == 0) return;
            }
        }
        // Both ends the same: they move there keeping their shape.
        _sim.Schedule(_sim.Tick, new FormationMoveCommand(LocalPlayer, own.ToArray(), x, z, x, z, ctrl, y));
        _rings.ShowMarker(new Vector3(x + 0.5f, y, z + 0.5f) * bs, ctrl ? SelectionRenderer.Other : SelectionRenderer.Own);
    }

    /// <summary>
    /// An Alt+right click: the selected soldiers patrol between where they stand and the ground clicked
    /// (see <see cref="CombatSystem"/>); the rest of the selection, which cannot fight, just walks there.
    /// </summary>
    private void Patrol(Vector2 screen)
    {
        var own = OwnSelected();
        if (own.Count == 0) return;
        var u = _sim.Units;
        float bs = ChunkMesher.BlockSize;
        var fighters = own.FindAll(id => _sim.Content.Units[u.Def[u.IndexOf(id)]].CanAttack);
        // On one of your units or buildings: guard it.
        int ward = PickUnit(screen);
        if (ward > 0 && OwnerOf(ward) == LocalPlayer)
        {
            fighters.Remove(ward);
            if (fighters.Count == 0) return;
            _sim.Schedule(_sim.Tick, new GuardCommand(LocalPlayer, fighters.ToArray(), ward));
            int w = u.IndexOf(ward);
            _rings.ShowMarker(new Vector3(u.X[w].ToFloat(), u.Y[w], u.Z[w].ToFloat()) * bs, SelectionRenderer.Own);
            return;
        }
        if (BuildingUnder(screen) is { Planned: false } charge && charge.Owner == LocalPlayer)
        {
            if (fighters.Count == 0) return;
            _sim.Schedule(_sim.Tick, new GuardCommand(LocalPlayer, fighters.ToArray(), 0, charge.Id));
            _rings.ShowMarker(new Vector3(charge.X + charge.Def.SizeX / 2f, charge.Y, charge.Z + charge.Def.SizeZ / 2f) * bs, SelectionRenderer.Own);
            return;
        }
        if (!GroundUnder(screen, out int x, out int y, out int z)) return;
        if (fighters.Count > 0) _sim.Schedule(_sim.Tick, new PatrolCommand(LocalPlayer, fighters.ToArray(), x, z, y));
        own.RemoveAll(fighters.Contains);
        if (own.Count > 0) _sim.Schedule(_sim.Tick, new MoveCommand(LocalPlayer, own.ToArray(), x, z, y));
        _rings.ShowMarker(new Vector3(x + 0.5f, y, z + 0.5f) * ChunkMesher.BlockSize, SelectionRenderer.Other);
    }

    /// <summary>
    /// A right click with your building that trains units selected (and no units): its rally point goes where the
    /// click was, and the units it trains walk there; a right click on the building itself clears it.
    /// </summary>
    private void Rally(Vector2 screen)
    {
        var b = _sim.Buildings.Get(SelectedBuilding);
        if (b == null || b.Owner != LocalPlayer || b.Def.Trains.Length == 0 || b.Planned) return;
        if (BuildingUnder(screen) == b)
        {
            _sim.Schedule(_sim.Tick, new RallyCommand(LocalPlayer, b.Id, -1, -1));
            return;
        }
        if (!GroundUnder(screen, out int x, out int y, out int z)) return;
        _sim.Schedule(_sim.Tick, new RallyCommand(LocalPlayer, b.Id, x, z, y));
        _rings.ShowMarker(new Vector3(x + 0.5f, y, z + 0.5f) * ChunkMesher.BlockSize, SelectionRenderer.Own);
    }

    /// <summary>
    /// A right drag on the ground: the selected units line up along the line from where it started to where
    /// it ended (with Ctrl, attack-moving there). A drag that ends where it started is a plain right click.
    /// </summary>
    private void OrderLine(Vector2 from, Vector2 to, bool ctrl)
    {
        var own = OwnSelected();
        if (own.Count == 0) return;
        if (!GroundUnder(from, out int x1, out _, out int z1) || !GroundUnder(to, out int x2, out _, out int z2) || (x1 == x2 && z1 == z2))
        {
            Order(from, ctrl);
            return;
        }
        _sim.Schedule(_sim.Tick, new FormationMoveCommand(LocalPlayer, own.ToArray(), x1, z1, x2, z2, ctrl));
        int mx = (x1 + x2) / 2, mz = (z1 + z2) / 2;
        _rings.ShowMarker(new Vector3(mx + 0.5f, Walk.GroundLevel(World, mx, mz), mz + 0.5f) * ChunkMesher.BlockSize,
            ctrl ? SelectionRenderer.Other : SelectionRenderer.Own);
    }

    /// <summary>While a formation line is being dragged out: where each selected unit would stand on it.</summary>
    private void UpdateFormationPreview()
    {
        _formationPreview.Clear();
        if (!_lineDragging || Tool != ToolMode.Select) return;
        if (!GroundUnder(_rightPos, out int x1, out _, out int z1) || !GroundUnder(_mousePos, out int x2, out _, out int z2)) return;
        if (x1 == x2 && z1 == z2) return;
        var u = _sim.Units;
        var members = new List<Formations.Member>();
        foreach (int id in OwnSelected())
        {
            int i = u.IndexOf(id);
            var def = _sim.Content.Units[u.Def[i]];
            var rank = !def.CanAttack ? FormationRank.Support : def.IsRanged ? FormationRank.Ranged : FormationRank.Melee;
            members.Add(new Formations.Member(id, u.X[i], u.Z[i], rank));
        }
        if (members.Count == 0) return;
        var half = Fix64.Half;
        var slots = Formations.Line(members, Fix64.FromInt(x1) + half, Fix64.FromInt(z1) + half, Fix64.FromInt(x2) + half,
            Fix64.FromInt(z2) + half, out _, out _);
        foreach (var s in slots)
        {
            float sx = s.X.ToFloat(), sz = s.Z.ToFloat();
            _formationPreview.Add(new Vector3(sx, Walk.GroundLevel(World, (int)MathF.Floor(sx), (int)MathF.Floor(sz)), sz));
        }
    }

    private void OrderAtBuilding(List<int> own, Building b, bool burn)
    {
        float bs = ChunkMesher.BlockSize;
        var centre = new Vector3(b.X + b.Def.SizeX / 2f, b.Y, b.Z + b.Def.SizeZ / 2f) * bs;
        if (b.Def.Kind == BuildingKind.Totem && _sim.Cities.TownOfTotem(b) is { } city)
        {
            // A city-state's totem: caravans trade at the town, and soldiers attack it unless it is yours.
            var caravans = OwnWithRole(own, UnitRole.Trader);
            if (caravans.Count > 0) _sim.Schedule(_sim.Tick, new TradeCommand(LocalPlayer, caravans.ToArray(), city.Id));
            own.RemoveAll(caravans.Contains);
            if (own.Count == 0 || b.Owner == LocalPlayer)
            {
                _rings.ShowMarker(centre, SelectionRenderer.Work);
                return;
            }
        }
        if (b.Def.Kind == BuildingKind.TownHall && _sim.Economy.PartnerHall(LocalPlayer, b.Id) != null)
        {
            // A trade partner's town hall (see TradeDealSystem): caravans trade there; soldiers still attack it.
            var caravans = OwnWithRole(own, UnitRole.Trader);
            if (caravans.Count > 0) _sim.Schedule(_sim.Tick, new MarketCommand(LocalPlayer, caravans.ToArray(), b.Id));
            own.RemoveAll(caravans.Contains);
            if (own.Count == 0)
            {
                _rings.ShowMarker(centre, SelectionRenderer.Work);
                return;
            }
        }
        if (b.Owner != LocalPlayer)
        {
            var units = own.ToArray();
            _sim.Schedule(_sim.Tick, !b.Def.Raidable
                ? new AttackBuildingCommand(LocalPlayer, units, b.Id)
                : burn
                    ? new BurnCommand(LocalPlayer, units, b.Id)
                    : new CaptureCommand(LocalPlayer, units, b.Id));
            _rings.ShowMarker(centre, SelectionRenderer.Other);
            return;
        }
        if (b.Planned)
        {
            StartPlan(b, own);
            return;
        }
        var u = _sim.Units;
        // Tamed beasts sent to your finished beast pen go into it and are kept there (see CombatSystem.Stampede.cs).
        if (b.Owner == LocalPlayer && b.Active && b.Def.Pen != null)
        {
            var beasts = own.FindAll(id => _sim.Content.Units[u.Def[u.IndexOf(id)]].Pennable);
            if (beasts.Count > 0)
            {
                _sim.Schedule(_sim.Tick, new PenCommand(LocalPlayer, beasts.ToArray(), b.Id));
                own.RemoveAll(beasts.Contains);
                if (own.Count == 0)
                {
                    _rings.ShowMarker(centre, SelectionRenderer.Work);
                    return;
                }
            }
        }
        // Workers sent to a building of yours marked to take down take it down (Craft guilds).
        if (b.Owner == LocalPlayer && b.Salvaging && b.Active)
        {
            var takers = OwnWorkers(own);
            if (takers.Count > 0)
            {
                _sim.Schedule(_sim.Tick, new SalvageCommand(LocalPlayer, takers.ToArray(), b.Id));
                own.RemoveAll(takers.Contains);
            }
        }
        // Workers sent to a damaged building of yours repair it (every faction, no tech needed).
        var menders = new List<int>();
        if (b.Owner == LocalPlayer && !b.WantsBuilders && _sim.Economy.Repairable(b))
        {
            menders = OwnWorkers(own).FindAll(id => u.CarryAmount[u.IndexOf(id)] == 0 || !b.Def.Storage);
            if (menders.Count > 0)
            {
                _sim.Schedule(_sim.Tick, new RepairCommand(LocalPlayer, menders.ToArray(), b.Id));
                own.RemoveAll(menders.Contains);
            }
        }
        var working = new List<int>();
        // Workers with something to do there: build the site, work the farm or smelter, unload at storage, or
        // go on supply duty at storage with supply rules (carts too).
        if (b.Owner == LocalPlayer)
            foreach (int id in OwnWorkers(own))
                if (b.WantsBuilders || b.Def.WorkerSlots > 0 || (b.Def.Storage && (u.CarryAmount[u.IndexOf(id)] > 0 || b.HasSupply && b.Active)))
                    working.Add(id);
        // Pack ponies run goods out to your sites, and to and from your workshops, smelters, quarries, mines and orchards.
        if (b.Owner == LocalPlayer && (b.State == BuildingState.Site
                || b.Active && (b.Def.Kind is BuildingKind.Workshop or BuildingKind.Smelter or BuildingKind.Orchard || b.Def.IsQuarry)))
            foreach (int id in OwnWithRole(own, UnitRole.Hauler))
                if (_sim.Content.Units[u.Def[u.IndexOf(id)]].Runs)
                    working.Add(id);
        // Carts unload at your storage, or take supply duty at storage with supply rules.
        if (b.Owner == LocalPlayer && b.Def.Storage && b.Active)
            foreach (int id in OwnWithRole(own, UnitRole.Hauler))
                if (u.CarryAmount[u.IndexOf(id)] > 0 || b.HasSupply)
                    working.Add(id);
        if (working.Count > 0) _sim.Schedule(_sim.Tick, new WorkCommand(LocalPlayer, working.ToArray(), b.Id));
        own.RemoveAll(working.Contains);
        // Archers and other ranged units sent at a wall of yours they can stand on take posts on top of it.
        if (b.Def.Walkway && b.Active)
        {
            var posted = own.FindAll(id => _sim.Economy.CanPost(u.IndexOf(id)));
            if (posted.Count > 0)
            {
                _sim.Schedule(_sim.Tick, new PostCommand(LocalPlayer, posted.ToArray(), b.Id));
                own.RemoveAll(posted.Contains);
                working.AddRange(posted);
            }
        }
        if (own.Count > 0 && _sim.Economy.AccessSpot(b, out int ax, out int az, out int ay))
            _sim.Schedule(_sim.Tick, new MoveCommand(LocalPlayer, own.ToArray(), ax, az, ay));
        _rings.ShowMarker(centre, working.Count + menders.Count > 0 ? SelectionRenderer.Work : SelectionRenderer.Own);
    }

    /// <summary>
    /// Starts one of your plans: the workers among <paramref name="own"/> build it (with none, automatic workers
    /// may). If you cannot pay for it yet (or it no longer fits) nothing happens but a red marker.
    /// </summary>
    public void StartPlan(Building b, List<int>? own = null)
    {
        float bs = ChunkMesher.BlockSize;
        var centre = new Vector3(b.X + b.Def.SizeX / 2f, b.Y, b.Z + b.Def.SizeZ / 2f) * bs;
        if (!b.Planned || b.Owner != LocalPlayer || _sim.Economy.CanStart(b) != PlaceResult.Ok)
        {
            _rings.ShowMarker(centre, SelectionRenderer.Other);
            return;
        }
        var workers = own != null ? OwnWorkers(own) : new List<int>();
        _sim.Schedule(_sim.Tick, new WorkCommand(LocalPlayer, workers.ToArray(), b.Id));
        _rings.ShowMarker(centre, SelectionRenderer.Work);
        Changed?.Invoke();
    }

    /// <summary>Starts the selected plan with the selected workers (the selected building's Start button).</summary>
    public void StartSelectedPlan()
    {
        if (_sim.Buildings.Get(SelectedBuilding) is { Planned: true } b) StartPlan(b, OwnSelected());
    }

    /// <summary>Removes the selected plan, if it is one of yours.</summary>
    public void CancelSelectedPlan()
    {
        if (_sim.Buildings.Get(SelectedBuilding) is not { Planned: true } b || b.Owner != LocalPlayer) return;
        _sim.Schedule(_sim.Tick, new CancelPlanCommand(LocalPlayer, b.Id));
        Changed?.Invoke();
    }

    public void TogglePlanMode()
    {
        PlanMode = !PlanMode;
        Changed?.Invoke();
    }

    /// <summary>The building whose blocks (or levelled site, or field, or plan) are under the cursor, or null.</summary>
    public Building? BuildingUnder(Vector2 screen)
    {
        if (!VoxelRay.FromScreen(_rig.Camera, screen, World, HiddenFrom, out var hit, out _) || !Fog.HasSeen(hit.X, hit.Z)) return null;
        var b = _sim.Buildings.At(hit.X, hit.Y, hit.Z);
        if (b != null && !(b.Planned && b.Owner != LocalPlayer)) return b;
        // A site with nothing built yet, or a field: the ground under its footprint.
        b = _sim.Buildings.AtColumn(hit.X, hit.Z);
        if (b != null && b.Planned && b.Owner != LocalPlayer) return null; // others' plans are not seen
        return b != null && hit.Y >= b.Y - 2 && hit.Y < b.Y + Math.Max(b.Shape.Height, 1) ? b : null;
    }

    /// <summary>The city-state town whose square (ground or huts) is under the cursor, or null.</summary>
    public CityState? CityUnder(Vector2 screen)
    {
        if (!VoxelRay.FromScreen(_rig.Camera, screen, World, HiddenFrom, out var hit, out _) || !Fog.HasSeen(hit.X, hit.Z)) return null;
        var town = _sim.Cities.At(hit.X, hit.Z);
        return town != null && hit.Y >= town.Y - 2 && hit.Y <= town.Y + 5 ? town : null;
    }

    /// <summary>The old ruin under the cursor (its walls, floor or the stone fallen in it), if you have seen it and nobody has searched it yet.</summary>
    public AncientRuin? RuinUnder(Vector2 screen)
    {
        if (!VoxelRay.FromScreen(_rig.Camera, screen, World, HiddenFrom, out var hit, out _) || !Fog.HasSeen(hit.X, hit.Z)) return null;
        var ruin = _sim.Ruins.At(hit.X, hit.Z);
        return ruin is { Searched: false } && hit.Y >= ruin.Y && hit.Y <= ruin.Y + 5 ? ruin : null;
    }

    /// <summary>True if the dusk tree (its wood, or its leaves once healed) is under the cursor.</summary>
    public bool DuskTreeUnder(Vector2 screen)
    {
        var tree = _sim.DuskTree;
        if (!tree.Exists || !VoxelRay.FromScreen(_rig.Camera, screen, World, HiddenFrom, out var hit, out _)) return false;
        var block = World.Get(hit.X, hit.Y, hit.Z);
        return (BlockInfo.IsDuskTree(block) || block == BlockType.DuskLeaves)
            && Math.Abs(hit.X - tree.X) <= 7 && Math.Abs(hit.Z - tree.Z) <= 7 && Fog.HasSeen(hit.X, hit.Z);
    }

    /// <summary>
    /// The block to gather under the cursor: a tree's trunk (also when the click hits its leaves), a berry
    /// bush, or stone, ore or coal.
    /// </summary>
    private bool ResourceUnder(Vector2 screen, out int x, out int y, out int z)
    {
        x = y = z = 0;
        var w = World;
        if (!VoxelRay.FromScreen(_rig.Camera, screen, w, HiddenFrom, out var hit, out _)) return false;
        var block = w.Get(hit.X, hit.Y, hit.Z);
        if (Harvest.Repeats(block))
        {
            (x, y, z) = (hit.X, hit.Y, hit.Z);
            return true;
        }
        if (!BlockInfo.IsLeaves(block)) return false;
        // The trunk holding up these leaves: the nearest wood below or beside them.
        long best = long.MaxValue;
        for (int dy = -5; dy <= 0; dy++)
        for (int dz = -2; dz <= 2; dz++)
        for (int dx = -2; dx <= 2; dx++)
        {
            if (!BlockInfo.IsTrunk(w.Get(hit.X + dx, hit.Y + dy, hit.Z + dz))) continue;
            long d = dx * dx + dz * dz + dy * dy;
            if (d >= best) continue;
            best = d;
            (x, y, z) = (hit.X + dx, hit.Y + dy, hit.Z + dz);
        }
        return best != long.MaxValue;
    }

    /// <summary>
    /// The standing spot under the cursor: on top of the block clicked (which can be a tunnel floor when
    /// the slice is on). A click on a tree means the ground beside it, not the canopy.
    /// </summary>
    private bool GroundUnder(Vector2 screen, out int x, out int y, out int z)
    {
        x = y = z = 0;
        var w = World;
        if (!VoxelRay.FromScreen(_rig.Camera, screen, w, HiddenFrom, out var hit, out _)) return false;
        (x, y, z) = (hit.X, hit.Y + 1, hit.Z);
        var block = w.Get(hit.X, hit.Y, hit.Z);
        if (!BlockInfo.IsLeaves(block) && !BlockInfo.IsTrunk(block) && !BlockInfo.IsDuskTree(block)) return true;
        for (int r = 0; r <= 3; r++)
        for (int dz = -r; dz <= r; dz++)
        for (int dx = -r; dx <= r; dx++)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
            int gy = Walk.GroundLevel(w, hit.X + dx, hit.Z + dz);
            if (gy < 0 || !_slice.Shows(gy)) continue;
            (x, y, z) = (hit.X + dx, gy, hit.Z + dz);
            return true;
        }
        return true;
    }

    private int HiddenFrom => _slice.On ? _slice.Level : int.MaxValue;

    // ---- Control groups ----

    private void AssignGroup(int g)
    {
        _groups[g] = OwnSelected();
        _lastGroup = -1;
    }

    private void RecallGroup(int g)
    {
        if (_groups[g] is not { } group) return;
        group.RemoveAll(id => !_sim.Units.Exists(id));
        ulong now = Time.GetTicksMsec();
        if (g == _lastGroup && now - _lastGroupMs <= DoubleTapMs && group.Count > 0) CentreOn(group);
        _lastGroup = g;
        _lastGroupMs = now;
        SetSelection(group);
    }

    private void CentreOn(List<int> ids)
    {
        var sum = Vector3.Zero;
        foreach (int id in ids) sum += Centre(_sim.Units.IndexOf(id));
        var c = sum / ids.Count;
        _rig.Position = new Vector3(c.X, c.Y - ChunkMesher.BlockSize, c.Z);
    }

    // ---- Building, roads and training ----

    private void SetTool(ToolMode tool)
    {
        if (tool != Tool || tool is not (ToolMode.Wall or ToolMode.Bridge or ToolMode.Ramp or ToolMode.Ladder or ToolMode.Dam or ToolMode.EarthWall or ToolMode.IceBridge or ToolMode.Chasm or ToolMode.GrowForest or ToolMode.Fell or ToolMode.Prospect or ToolMode.Stampede)) _wallDragging = false;
        Tool = tool;
        if (tool != ToolMode.Build) BuildDef = null;
        if (tool != ToolMode.Road) _roadStarted = false;
        if (tool != ToolMode.Dig) _digDragging = false;
        Ghost = default;
        _roadPreview.Clear();
        _digPreview.Clear();
        _wallPreview.Clear();
        Changed?.Invoke();
    }

    /// <summary>Starts placing a building (by definition id); the selected workers will build it.</summary>
    public void StartBuild(string building)
    {
        if (!_sim.Content.TryGetBuilding(building, out var def)) return;
        def = _sim.FactionOf(LocalPlayer).Own(def); // your faction's own in place of everyone's (B: the Deepfolk's brood chamber)
        SetTool(ToolMode.Build);
        BuildDef = def;
        Changed?.Invoke();
    }

    public void StartRoad() => SetTool(ToolMode.Road);

    /// <summary>
    /// Queues a unit at the selected building, if it is yours and trains that unit; with no unit given, the
    /// first one it trains.
    /// </summary>
    public void TrainAtSelected(string? unit)
    {
        var b = _sim.Buildings.Get(SelectedBuilding);
        if (b == null || b.Owner != LocalPlayer || b.Def.Trains.Length == 0) return;
        unit ??= b.Def.Trains[0];
        if (Array.IndexOf(b.Def.Trains, unit) < 0) return;
        _sim.Schedule(_sim.Tick, new TrainCommand(LocalPlayer, b.Id, unit));
    }

    /// <summary>
    /// Upgrades the selected wall or gate of yours to its next tier: the whole stretch of that tier it stands
    /// in, as far as the goods go, or with <paramref name="stretch"/> off that column alone.
    /// </summary>
    public void UpgradeSelected(bool stretch)
    {
        var b = _sim.Buildings.Get(SelectedBuilding);
        if (b == null || b.Owner != LocalPlayer || _sim.Economy.CanUpgrade(b) != PlaceResult.Ok) return;
        _sim.Schedule(_sim.Tick, new UpgradeCommand(LocalPlayer, System.Array.Empty<int>(), b.Id, stretch));
        _rings.ShowMarker(new Vector3(b.X + 0.5f, b.Y, b.Z + 0.5f) * ChunkMesher.BlockSize, SelectionRenderer.Work);
        Changed?.Invoke();
    }

    // ---- Automation ----

    /// <summary>The selected workers of yours find their own jobs; if they all do already, they stop.</summary>
    public void ToggleAutoSelected()
    {
        var workers = OwnWorkers(OwnSelected());
        if (workers.Count == 0) return;
        bool on = false;
        foreach (int id in workers)
            if (!_sim.Units.Auto[_sim.Units.IndexOf(id)]) on = true;
        _sim.Schedule(_sim.Tick, new AutomateCommand(LocalPlayer, workers.ToArray(), on));
        Changed?.Invoke();
    }

    /// <summary>All your workers, and those you train from now on, find their own jobs; or none of them.</summary>
    public void ToggleAutoAll()
    {
        _sim.Schedule(_sim.Tick, new AutomateAllCommand(LocalPlayer, !_sim.Economy.AutoWorkers(LocalPlayer)));
        Changed?.Invoke();
    }

    /// <summary>Your destroyed buildings are put back where they stood, or no longer (see <see cref="RebuildCommand"/>).</summary>
    public void ToggleRebuild()
    {
        _sim.Schedule(_sim.Tick, new RebuildCommand(LocalPlayer, !_sim.Economy.Rebuilds(LocalPlayer)));
        Changed?.Invoke();
    }

    /// <summary>
    /// The rival's truce button (see <see cref="TruceSystem"/>): at peace, breaks the truce off (it ends after a
    /// warning); with your offer standing, takes it back; otherwise offers one, or accepts the rival's offer.
    /// </summary>
    public void Truce(int rival)
    {
        var truces = _sim.Truces;
        if (truces.AtPeace(LocalPlayer, rival) || truces.Offered(LocalPlayer, rival))
            _sim.Schedule(_sim.Tick, new EndTruceCommand(LocalPlayer, rival));
        else if (truces.CanOffer(LocalPlayer, rival))
            _sim.Schedule(_sim.Tick, new TruceCommand(LocalPlayer, rival));
        Changed?.Invoke();
    }

    /// <summary>
    /// The rival's trade deal button (see <see cref="TradeDealSystem"/>): with a deal, ends it; with your offer
    /// standing, takes it back; otherwise offers one, or accepts the rival's offer.
    /// </summary>
    public void Deal(int rival)
    {
        var deals = _sim.Deals;
        if (deals.Partners(LocalPlayer, rival) || deals.Offered(LocalPlayer, rival))
            _sim.Schedule(_sim.Tick, new EndTradeDealCommand(LocalPlayer, rival));
        else if (deals.CanOffer(LocalPlayer, rival))
            _sim.Schedule(_sim.Tick, new TradeDealCommand(LocalPlayer, rival));
        Changed?.Invoke();
    }

    /// <summary>You give up the match (see <see cref="SurrenderCommand"/>). The game runs on so that it ends at once.</summary>
    public void Surrender()
    {
        _sim.Schedule(_sim.Tick, new SurrenderCommand(LocalPlayer));
        _clock.SetSpeed(_clock.SpeedIndex, resume: true);
        Changed?.Invoke();
    }

    /// <summary>Puts a governor with that focus in charge of the selected town hall of yours, or takes it away (see <see cref="GovernCommand"/>).</summary>
    public void SetGovernor(GovernorFocus focus)
    {
        var b = _sim.Buildings.Get(SelectedBuilding);
        if (b == null || b.Owner != LocalPlayer || b.Def.Kind != BuildingKind.TownHall) return;
        _sim.Schedule(_sim.Tick, new GovernCommand(LocalPlayer, b.Id, focus));
        Changed?.Invoke();
    }

    /// <summary>The selected building of yours repeats its training, or stops.</summary>
    public void ToggleRepeat()
    {
        var b = _sim.Buildings.Get(SelectedBuilding);
        if (b == null || b.Owner != LocalPlayer || b.Def.Trains.Length == 0) return;
        _sim.Schedule(_sim.Tick, new RepeatCommand(LocalPlayer, b.Id, !b.Repeat));
        Changed?.Invoke();
    }

    /// <summary>Supply rule amounts, in the order a supply button steps through them.</summary>
    public static readonly int[] SupplySteps = { 0, 25, 50, 100, 200 };

    /// <summary>
    /// The selected storage building of yours keeps the next amount of a good (by <see cref="SupplySteps"/>,
    /// back to none after the last), or the one before with <paramref name="back"/>.
    /// </summary>
    public void CycleSupply(Good good, bool back = false)
    {
        var b = _sim.Buildings.Get(SelectedBuilding);
        if (b == null || b.Owner != LocalPlayer || !b.Def.Storage || b.Planned) return;
        int now = b.Supply[(int)good], n = SupplySteps.Length;
        int next = back ? SupplySteps[n - 1] : SupplySteps[0]; // wraps round past either end
        if (back)
        {
            for (int k = n - 1; k >= 0; k--)
                if (SupplySteps[k] < now) { next = SupplySteps[k]; break; }
        }
        else
        {
            foreach (int step in SupplySteps)
                if (step > now) { next = step; break; }
        }
        _sim.Schedule(_sim.Tick, new SupplyCommand(LocalPlayer, b.Id, good, next));
        Changed?.Invoke();
    }

    /// <summary>The selected soldiers of yours take the stance after the first one's: aggressive, hold, retreat.</summary>
    public void CycleStance()
    {
        var soldiers = OwnSoldiers();
        if (soldiers.Count == 0) return;
        var next = (UnitStance)(((int)_sim.Units.Stance[_sim.Units.IndexOf(soldiers[0])] + 1) % 3);
        _sim.Schedule(_sim.Tick, new StanceCommand(LocalPlayer, soldiers.ToArray(), next));
        Changed?.Invoke();
    }

    /// <summary>The selected units of yours that can take up a shield wall (see <see cref="CombatSystem"/>), or stand in one.</summary>
    public List<int> OwnShieldWallers()
    {
        var list = new List<int>();
        foreach (int id in OwnSoldiers())
        {
            int i = _sim.Units.IndexOf(id);
            if (_sim.Units.ShieldWall[i] || _sim.Combat.CanShieldWall(i)) list.Add(id);
        }
        return list;
    }

    /// <summary>The selected building if it is a finished beast pen of yours (see <see cref="BuildingKind.Pen"/>), else null.</summary>
    public Building? SelectedPen() =>
        _sim.Buildings.Get(SelectedBuilding) is { Active: true, Def.Pen: not null } b && b.Owner == LocalPlayer ? b : null;

    /// <summary>The Stampede button and key (Ctrl+S): with your beast pen selected, drag the line its beasts run along.</summary>
    public void ToggleStampede()
    {
        if (Tool == ToolMode.Stampede) SetTool(ToolMode.Select);
        else if (SelectedPen() is { } pen && _sim.Combat.PenCount(pen.Id) > 0) SetTool(ToolMode.Stampede);
    }

    /// <summary>The selected building if it is a finished crystal lens of yours (see <see cref="BuildingKind.Lens"/>), else null.</summary>
    public Building? SelectedLens() =>
        _sim.Buildings.Get(SelectedBuilding) is { Active: true, Def.Lens: not null } b && b.Owner == LocalPlayer ? b : null;

    /// <summary>The Gaze button and key (Ctrl+G): with your crystal lens selected and ready, click where it looks.</summary>
    public void ToggleGaze()
    {
        if (Tool == ToolMode.Gaze) SetTool(ToolMode.Select);
        else if (SelectedLens() is { } lens && _sim.Economy.CanGaze(LocalPlayer, lens) == GazeResult.Ok) SetTool(ToolMode.Gaze);
    }

    /// <summary>The selected crystal lens gazes at the ground under the mouse; then the tool is put away.</summary>
    private void GazeAt(Vector2 screen)
    {
        if (SelectedLens() is { } lens && GroundUnder(screen, out int x, out _, out int z))
        {
            _sim.Schedule(_sim.Tick, new GazeCommand(LocalPlayer, lens.Id, x, z));
            Changed?.Invoke();
        }
        SetTool(ToolMode.Select);
    }

    /// <summary>The Let out button: the selected pen's beasts are let out, free to take orders.</summary>
    public void LetOut()
    {
        if (SelectedPen() is not { } pen || _sim.Combat.PenCount(pen.Id) == 0) return;
        _sim.Schedule(_sim.Tick, new UnpenCommand(LocalPlayer, pen.Id));
        Changed?.Invoke();
    }

    /// <summary>The columns along the stampede's line being dragged (the beasts run from the pen to its start, then along it).</summary>
    private void UpdateStampedePreview()
    {
        if (!_wallDragging || !GroundUnder(_mousePos, out int x2, out _, out int z2)) return;
        int n = Math.Max(Math.Abs(x2 - _wallX), Math.Abs(z2 - _wallZ));
        for (int k = 0; k <= n; k++)
        {
            int x = n == 0 ? _wallX : _wallX + (int)Math.Round((x2 - _wallX) * k / (double)n);
            int z = n == 0 ? _wallZ : _wallZ + (int)Math.Round((z2 - _wallZ) * k / (double)n);
            int y = Walk.GroundLevel(_sim.World, x, z);
            if (y >= 1) _wallPreview.Add((x, y, z, true));
        }
    }

    /// <summary>The selected war drummers of yours (see <see cref="UnitDef.Drums"/>).</summary>
    public List<int> OwnDrummers()
    {
        var list = OwnSelected();
        list.RemoveAll(id => _sim.Content.Units[_sim.Units.Def[_sim.Units.IndexOf(id)]].Drums == null);
        return list;
    }

    /// <summary>The selected war drummers that are ready beat the charge.</summary>
    public void BeatCharge()
    {
        var ready = OwnDrummers();
        ready.RemoveAll(id => !_sim.Combat.CanCharge(_sim.Units.IndexOf(id)));
        if (ready.Count == 0) return;
        _sim.Schedule(_sim.Tick, new ChargeCommand(LocalPlayer, ready.ToArray()));
        Changed?.Invoke();
    }

    /// <summary>The selected units of yours that can molt (see <see cref="Rts.Sim.Content.TechEffectType.Molt"/>), hurt or not.</summary>
    public List<int> OwnMolters()
    {
        var list = OwnSelected();
        list.RemoveAll(id => !_sim.Combat.CanMolt(_sim.Units.IndexOf(id)));
        return list;
    }

    /// <summary>The selected units that are hurt badly enough molt.</summary>
    public void Molt()
    {
        var ready = OwnMolters();
        ready.RemoveAll(id => !_sim.Combat.ReadyToMolt(_sim.Units.IndexOf(id)));
        if (ready.Count == 0) return;
        _sim.Schedule(_sim.Tick, new MoltCommand(LocalPlayer, ready.ToArray()));
        Changed?.Invoke();
    }

    /// <summary>The selected line infantry raise their shield wall, or lower it if they all stand in one already.</summary>
    public void ToggleShieldWall()
    {
        var units = OwnShieldWallers();
        if (units.Count == 0) return;
        SetShieldWall(!units.TrueForAll(id => _sim.Units.ShieldWall[_sim.Units.IndexOf(id)]));
    }

    /// <summary>The selected line infantry raise (or lower) their shield wall.</summary>
    public void SetShieldWall(bool raise)
    {
        var units = OwnShieldWallers();
        if (units.Count == 0) return;
        _sim.Schedule(_sim.Tick, new ShieldWallCommand(LocalPlayer, units.ToArray(), raise));
        Changed?.Invoke();
    }

    /// <summary>
    /// The governed town hall of yours nearest the selected soldiers (ties to the lowest id), or null: the one
    /// <see cref="DefendTown"/> hands them to.
    /// </summary>
    public Building? TownToDefend()
    {
        var soldiers = OwnSoldiers();
        if (soldiers.Count == 0) return null;
        int i = _sim.Units.IndexOf(soldiers[0]);
        int x = _sim.Units.X[i].FloorToInt(), z = _sim.Units.Z[i].FloorToInt();
        Building? best = null;
        int bestD = int.MaxValue;
        foreach (var b in _sim.Buildings.All)
        {
            if (b.Owner != LocalPlayer || b.Planned || b.Def.Kind != BuildingKind.TownHall || b.Governor == GovernorFocus.None) continue;
            int d = b.ColumnDistance(x, z);
            if (d < bestD) (best, bestD) = (b, d);
        }
        return best;
    }

    /// <summary>
    /// The selected soldiers join the town guard of your nearest governed town hall: they guard it, and the
    /// governor sends them after enemies anywhere in its town (the same as Alt+right click on the hall).
    /// </summary>
    public void DefendTown()
    {
        var hall = TownToDefend();
        if (hall == null) return;
        _sim.Schedule(_sim.Tick, new GuardCommand(LocalPlayer, OwnSoldiers().ToArray(), 0, hall.Id));
        float bs = ChunkMesher.BlockSize;
        _rings.ShowMarker(new Vector3(hall.X + hall.Def.SizeX / 2f, hall.Y, hall.Z + hall.Def.SizeZ / 2f) * bs, SelectionRenderer.Own);
        Changed?.Invoke();
    }

    /// <summary>The selected units of yours that fight and are not workers (the ones with a stance), in id order.</summary>
    public List<int> OwnSoldiers()
    {
        var result = OwnSelected();
        result.RemoveAll(id =>
        {
            var def = _sim.Content.Units[_sim.Units.Def[_sim.Units.IndexOf(id)]];
            return !def.CanAttack || def.Role == UnitRole.Worker;
        });
        return result;
    }

    // ---- Evolution ----

    /// <summary>The selected units of yours that can take the form (they have the experience) do.</summary>
    public void EvolveSelected(string form)
    {
        if (!_sim.Content.TryGetUnit(form, out var def)) return;
        var ready = OwnSelected().FindAll(id => _sim.Evolution.CanEvolve(_sim.Units.IndexOf(id), def));
        if (ready.Count == 0) return;
        _sim.Schedule(_sim.Tick, new EvolveCommand(LocalPlayer, ready.ToArray(), def.Id));
        Changed?.Invoke();
    }

    /// <summary>
    /// The next choice of form for your units of a type to take on their own once they can, now and later:
    /// none, then each of its forms in turn, then none again.
    /// </summary>
    public void CycleAutoEvolve(string unit)
    {
        if (!_sim.Content.TryGetUnit(unit, out var def) || def.Forms.Count == 0) return;
        int next = 0;
        if (_sim.Evolution.AutoForm(LocalPlayer, def) is { } current)
            for (int k = 0; k < def.Forms.Count; k++)
                if (def.Forms[k] == current) next = k + 1;
        string? form = next < def.Forms.Count ? def.Forms[next].Id : null;
        _sim.Schedule(_sim.Tick, new AutoEvolveCommand(LocalPlayer, def.Id, form));
        Changed?.Invoke();
    }

    // ---- Taming ----

    /// <summary>The selected units of yours that are beaten monsters waiting for your choice, in selection order.</summary>
    public List<int> OwnBeaten()
    {
        var result = OwnSelected();
        result.RemoveAll(id => !_sim.Units.Subdued[_sim.Units.IndexOf(id)]);
        return result;
    }

    /// <summary>The selected beaten monsters of yours are recruited, or ridden by the nearest unit that can.</summary>
    public void TameSelected(TameChoice choice)
    {
        var beaten = OwnBeaten();
        if (beaten.Count == 0) return;
        _sim.Schedule(_sim.Tick, new TameCommand(LocalPlayer, beaten.ToArray(), choice));
        Changed?.Invoke();
    }

    /// <summary>
    /// The cleared, unused den nearest your selected units that your faction can use with them (one of them is
    /// near it), or null. See <see cref="MonsterSystem"/>.
    /// </summary>
    public Lair? UsableDen()
    {
        var own = OwnSelected();
        if (own.Count == 0 || !_sim.Monsters.HasPrizes(LocalPlayer)) return null;
        var indexes = new List<int>(own.Count);
        foreach (int id in own) indexes.Add(_sim.Units.IndexOf(id));
        foreach (var lair in _sim.Monsters.Lairs)
            if (_sim.Monsters.CanUseDen(LocalPlayer, lair, indexes)) return lair;
        return null;
    }

    /// <summary>Your selected units use the den near them for your faction's prize there (the Orcs put up a wolf lodge on a wolf den).</summary>
    public void UseDen()
    {
        if (UsableDen() is not { } lair) return;
        _sim.Schedule(_sim.Tick, new DenCommand(LocalPlayer, OwnSelected().ToArray(), lair.Id));
        Changed?.Invoke();
    }

    /// <summary>Columns from an old ruin within which a selected unit of yours offers to search it (the Search button).</summary>
    public const int RuinOfferRange = 24;

    /// <summary>
    /// The unsearched old ruin you have seen nearest your selected units, with one of them that can search it within
    /// <see cref="RuinOfferRange"/> columns, or null (see <see cref="RuinSystem"/>).
    /// </summary>
    public AncientRuin? NearRuin()
    {
        var own = OwnSelected();
        if (own.Count == 0) return null;
        var u = _sim.Units;
        AncientRuin? best = null;
        long bestD = long.MaxValue;
        foreach (var ruin in _sim.Ruins.Ruins)
        {
            if (ruin.Searched || !Fog.HasSeen(ruin.X, ruin.Z)) continue;
            foreach (int id in own)
            {
                int i = u.IndexOf(id);
                if (!_sim.Ruins.CanSearch(i)) continue;
                long dx = u.X[i].FloorToInt() - ruin.X, dz = u.Z[i].FloorToInt() - ruin.Z;
                if (Math.Abs(dx) > RuinOfferRange || Math.Abs(dz) > RuinOfferRange || dx * dx + dz * dz >= bestD) continue;
                best = ruin;
                bestD = dx * dx + dz * dz;
            }
        }
        return best;
    }

    /// <summary>Your selected units search the old ruin near them (see <see cref="NearRuin"/>).</summary>
    public void SearchRuin()
    {
        if (NearRuin() is not { } ruin) return;
        var u = _sim.Units;
        var searching = OwnSelected().FindAll(id => _sim.Ruins.CanSearch(u.IndexOf(id)));
        if (searching.Count == 0) return;
        _sim.Schedule(_sim.Tick, new SearchRuinCommand(LocalPlayer, searching.ToArray(), ruin.Index));
        Changed?.Invoke();
    }

    /// <summary>The selected unit of yours that bears the Dusk Staff, or 0.</summary>
    public int StaffSelected()
    {
        foreach (int id in OwnSelected())
            if (_sim.DuskTree.HasStaff(_sim.Units.IndexOf(id))) return id;
        return 0;
    }

    /// <summary>The Dusk Staff's bearer, if selected, uses Dusk Bloom (key ,): every ally near it heals.</summary>
    public void Bloom()
    {
        int id = StaffSelected();
        if (id == 0 || !_sim.DuskTree.CanBloom(_sim.Units.IndexOf(id))) return;
        _sim.Schedule(_sim.Tick, new BloomCommand(LocalPlayer, new[] { id }));
        Changed?.Invoke();
    }

    /// <summary>Your beaten monsters, in id order.</summary>
    public List<int> AllBeaten()
    {
        var u = _sim.Units;
        var ids = new List<int>();
        for (int i = 0; i < u.Count; i++)
            if (u.Owner[i] == LocalPlayer && u.Subdued[i]) ids.Add(u.Id[i]);
        ids.Sort();
        return ids;
    }

    /// <summary>
    /// Selects your next beaten monster and centres the camera on it, so that you choose for each in turn:
    /// the one after the beaten monster selected now, in id order, or the first. (A box around several
    /// selects them all, to choose for all of them at once.)
    /// </summary>
    public void SelectNextBeaten()
    {
        var ids = AllBeaten();
        if (ids.Count == 0) return;
        int next = _selected.Count == 1 ? ids.IndexOf(_selected[0]) + 1 : 0;
        var one = new List<int> { ids[next % ids.Count] };
        SetSelection(one);
        CentreOn(one);
    }

    // ---- Idle workers and alerts ----

    /// <summary>
    /// Selects your next worker with nothing to do and centres the camera on it: the one after the idle worker
    /// selected now, in id order, or the first (see <see cref="EconomySystem.IdleWorkers"/>).
    /// </summary>
    public void SelectNextIdleWorker()
    {
        var ids = _sim.Economy.IdleWorkers(LocalPlayer);
        if (ids.Count == 0) return;
        int next = 0;
        if (_selected.Count == 1)
        {
            next = ids.Count;
            for (int k = 0; k < ids.Count && next == ids.Count; k++)
                if (ids[k] > _selected[0]) next = k;
        }
        var one = new List<int> { ids[next % ids.Count] };
        SetSelection(one);
        CentreOn(one);
    }

    /// <summary>
    /// Selects every combat unit you own, leaving workers and caravans out (see <see cref="CombatSystem.ArmyUnits"/>).
    /// Does nothing if you have none, so the selection you had stays.
    /// </summary>
    public void SelectArmy()
    {
        var ids = _sim.Combat.ArmyUnits(LocalPlayer);
        if (ids.Count > 0) SetSelection(ids);
    }

    /// <summary>Moves the camera focus to block (x, y, z), keeping its angle and distance.</summary>
    public void LookAt(int x, int y, int z)
    {
        if (x < 0 || z < 0) return;
        _rig.Position = new Vector3(x + 0.5f, y, z + 0.5f) * ChunkMesher.BlockSize;
    }

    /// <summary>The block column the camera looks at, for scripted tests.</summary>
    public (int X, int Z) CameraColumn
    {
        get
        {
            var p = _rig.Position / ChunkMesher.BlockSize;
            return (Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Z));
        }
    }

    // ---- City-states ----

    /// <summary>Hires one of the selected town's mercenaries (only whoever holds the town can).</summary>
    public void HireAtSelectedCity(string unit)
    {
        if (_sim.Cities.Get(SelectedCity) is not { } town) return;
        _sim.Schedule(_sim.Tick, new HireCommand(LocalPlayer, town.Id, unit));
        Changed?.Invoke();
    }

    /// <summary>Selects the town and centres the camera on its market.</summary>
    public void FocusCity(int id)
    {
        if (_sim.Cities.Get(id) is not { } town) return;
        SelectCity(town.Id);
        _rig.Position = new Vector3(town.X + 0.5f, town.Y, town.Z + 0.5f) * ChunkMesher.BlockSize;
    }

    /// <summary>Moves the camera to the ancient site (ruin index).</summary>
    public void FocusSite(int index)
    {
        if (index < 0 || index >= _sim.Ruins.Ruins.Count) return;
        var ruin = _sim.Ruins.Ruins[index];
        _rig.Position = new Vector3(ruin.X + 0.5f, ruin.Y + 1, ruin.Z + 0.5f) * ChunkMesher.BlockSize;
    }

    /// <summary>
    /// Moves the camera to (x, y, z), ground in the deep caves maybe: when the land is over it, the height slice is
    /// set to cut it away a little above (a great work, say).
    /// </summary>
    public void FocusAt(int x, int y, int z)
    {
        _rig.Position = new Vector3(x + 0.5f, y + 1, z + 0.5f) * ChunkMesher.BlockSize;
        // The top of the column, land or water.
        int cx = Math.Clamp(x, 0, _sim.World.SizeX - 1), cz = Math.Clamp(z, 0, _sim.World.SizeZ - 1), top = _sim.World.SizeY - 1;
        while (top > 0 && _sim.World.Get(cx, top, cz) == BlockType.Air) top--;
        if (top > y + 12) _slice.Set(y + 10);
    }

    /// <summary>Starts researching a tech at your town hall.</summary>
    public void Research(TechDef tech) => _sim.Schedule(_sim.Tick, new ResearchCommand(LocalPlayer, tech.Id));

    public void ToggleTechTree()
    {
        TechTreeOpen = !TechTreeOpen;
        if (TechTreeOpen) PrioritiesOpen = false;
        Changed?.Invoke();
    }

    // ---- Work priorities ----

    /// <summary>The work priorities menu (Ctrl+J) is open.</summary>
    public bool PrioritiesOpen { get; private set; }

    public void TogglePriorities()
    {
        PrioritiesOpen = !PrioritiesOpen;
        if (PrioritiesOpen) TechTreeOpen = false;
        Changed?.Invoke();
    }

    /// <summary>Gives the workers (yours, by id) a priority list; <see cref="WorkPriorities.UseDefault"/> puts them back on your default.</summary>
    public void SetPriorities(int[] workers, ulong list)
    {
        if (workers.Length == 0) return;
        _sim.Schedule(_sim.Tick, new PriorityCommand(LocalPlayer, workers, list));
        Changed?.Invoke();
    }

    /// <summary>Sets your default priority list, for workers without one of their own and those you train later.</summary>
    public void SetDefaultPriorities(ulong list)
    {
        _sim.Schedule(_sim.Tick, new DefaultPriorityCommand(LocalPlayer, list));
        Changed?.Invoke();
    }

    /// <summary>All your workers, by id, in the order they came.</summary>
    public List<int> AllOwnWorkers()
    {
        var u = _sim.Units;
        var ids = new List<int>();
        for (int i = 0; i < u.Count; i++)
            if (u.Owner[i] == LocalPlayer && _sim.Content.Units[u.Def[i]].Role == UnitRole.Worker && !u.Subdued[i]) ids.Add(u.Id[i]);
        ids.Sort();
        return ids;
    }

    /// <summary>Where the building would go with its middle under the cursor.</summary>
    private void UpdateGhost()
    {
        if (Tool != ToolMode.Build || BuildDef is not { } def || !GroundUnder(_mousePos, out int x, out int y, out int z))
        {
            Ghost = default;
            return;
        }
        if (def.IsTower) SnapToWall(ref x, ref z);
        int cx = x - def.SizeX / 2, cz = z - def.SizeZ / 2;
        // A great work stands in the deep caves, under other ground: at the level the cursor is on (the height slice
        // cuts the land over it away), not on top of the ground.
        var result = _sim.Economy.CanPlace(LocalPlayer, def, cx, cz, out int baseY, level: def.Kind == BuildingKind.GreatWork ? y : -1);
        Ghost = new Placement(true, cx, baseY >= 0 ? baseY : y, cz, result);
    }

    /// <summary>
    /// A tower snaps onto a wall: with the cursor on a finished wall of yours, or right beside one, its middle
    /// goes on that wall's column (the nearest, straight across before cornerwise), so it stands in the line.
    /// Two columns or more from a wall it goes where the cursor is, alone or up against the wall.
    /// </summary>
    private void SnapToWall(ref int x, ref int z)
    {
        int best = int.MaxValue, bx = x, bz = z;
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
        {
            var b = _sim.Buildings.AtColumn(x + dx, z + dz);
            if (b == null || b.Owner != LocalPlayer || b.Def.Kind != BuildingKind.Wall || !b.Active) continue;
            // The wall of the line itself before its facing, then the nearest.
            int score = (b.Def.FacingOf != null ? 4 : 0) + dx * dx + dz * dz;
            if (score < best) (best, bx, bz) = (score, x + dx, z + dz);
        }
        (x, z) = (bx, bz);
    }

    private void PlaceBuilding(bool keepPlacing)
    {
        if (BuildDef is not { } def || !Ghost.Visible || Ghost.Result is not (PlaceResult.Ok or PlaceResult.CannotAfford)) return;
        bool plan = Planning;
        int level = def.Kind == BuildingKind.GreatWork ? Ghost.Y : -1;
        _sim.Schedule(_sim.Tick, plan
            ? new PlanCommand(LocalPlayer, def.Id, Ghost.X, Ghost.Z, level)
            : new BuildCommand(LocalPlayer, OwnWorkers(OwnSelected()).ToArray(), def.Id, Ghost.X, Ghost.Z, level));
        float bs = ChunkMesher.BlockSize;
        _rings.ShowMarker(new Vector3(Ghost.X + def.SizeX / 2f, Ghost.Y, Ghost.Z + def.SizeZ / 2f) * bs,
            plan ? GhostRenderer.PlannedColor : SelectionRenderer.Work);
        if (!keepPlacing) SetTool(ToolMode.Select);
    }

    /// <summary>The first click sets where the road starts; the second lays it (Shift: carry on from its end).</summary>
    private void RoadClick(Vector2 screen, bool keepLaying)
    {
        if (!GroundUnder(screen, out int x, out _, out int z)) return;
        if (!_roadStarted)
        {
            (_roadStarted, _roadX, _roadZ) = (true, x, z);
            return;
        }
        _sim.Schedule(_sim.Tick, new RoadCommand(LocalPlayer, _roadX, _roadZ, x, z));
        if (keepLaying) (_roadX, _roadZ) = (x, z);
        else SetTool(ToolMode.Select);
    }

    /// <summary>The columns the road would cover, along the same line the simulation lays it.</summary>
    private void UpdateRoadPreview()
    {
        _roadPreview.Clear();
        if (Tool != ToolMode.Road || !_roadStarted || !GroundUnder(_mousePos, out int x2, out _, out int z2)) return;
        var w = World;
        int x = _roadX, z = _roadZ;
        int dx = Math.Abs(x2 - x), dz = -Math.Abs(z2 - z);
        int sx = x < x2 ? 1 : -1, sz = z < z2 ? 1 : -1;
        int err = dx + dz;
        for (int n = 0; n < EconomySystem.MaxRoad; n++)
        {
            int y = Walk.GroundLevel(w, x, z);
            if (y >= 1 && _sim.Buildings.AtColumn(x, z) == null && BlockInfo.IsGround(w.Get(x, y - 1, z)))
                _roadPreview.Add((x, y, z));
            if (x == x2 && z == z2) return;
            int e2 = 2 * err;
            if (e2 >= dz) { err += dz; x += sx; }
            if (e2 <= dx) { err += dx; z += sz; }
        }
    }

    // ---- Terrain tool ----

    private void Dig(Vector2 screen)
    {
        if (VoxelRay.FromScreen(_rig.Camera, screen, World, HiddenFrom, out var hit, out _))
            _sim.Schedule(_sim.Tick, new DigCommand(LocalPlayer, hit.X, hit.Y, hit.Z));
    }

    private void Place(Vector2 screen)
    {
        if (VoxelRay.FromScreen(_rig.Camera, screen, World, HiddenFrom, out _, out var before))
            _sim.Schedule(_sim.Tick, new PlaceCommand(LocalPlayer, before.X, before.Y, before.Z, BlockType.Stone));
    }

    // ---- Tunnel tool ----

    /// <summary>
    /// The selected sappers tunnel to the block clicked, so that they end up standing where it was; on a
    /// building, under the column clicked (two levels below its base, so the wall above falls; three under a
    /// field). Shift keeps the tool for another tunnel.
    /// </summary>
    private void Tunnel(Vector2 screen, bool keep)
    {
        var sappers = OwnSappers();
        if (sappers.Count == 0 || !VoxelRay.FromScreen(_rig.Camera, screen, World, HiddenFrom, out var hit, out _)) return;
        int x = hit.X, y = hit.Y, z = hit.Z;
        var b = _sim.Buildings.At(x, y, z);
        if (b != null) y = b.Y - (b.Def.Walkable ? 3 : 2);
        _sim.Schedule(_sim.Tick, new TunnelCommand(LocalPlayer, sappers.ToArray(), x, y, z));
        _rings.ShowMarker(new Vector3(x + 0.5f, y, z + 0.5f) * ChunkMesher.BlockSize, SelectionRenderer.Work);
        if (!keep) SetTool(ToolMode.Select);
    }

    // ---- Dig tool ----

    /// <summary>The selected workers and sappers: those the dig tool sends.</summary>
    public List<int> OwnDiggers()
    {
        var list = new List<int>();
        foreach (int id in OwnSelected())
            if (_sim.Content.Units[_sim.Units.Def[_sim.Units.IndexOf(id)]].Role is UnitRole.Worker or UnitRole.Sapper) list.Add(id);
        return list;
    }

    /// <summary>The Dig button and key: starts (or stops) marking out an area for the selected workers and sappers.</summary>
    public void ToggleDig() => SetTool(Tool == ToolMode.Dig ? ToolMode.Select : ToolMode.Dig);

    /// <summary>Digs one level deeper or shallower, between 1 and <see cref="DigArea.MaxDepth"/>.</summary>
    public void ChangeDigDepth(int by)
    {
        DigDepth = Math.Clamp(DigDepth + by, 1, DigArea.MaxDepth);
        Changed?.Invoke();
    }

    /// <summary>Pressing the button down on the ground starts the area there.</summary>
    private void DigStart(Vector2 screen)
    {
        if (!GroundUnder(screen, out int x, out int y, out int z)) return;
        (_digDragging, _digX, _digZ, _digLevel) = (true, x, z, y);
        Changed?.Invoke();
    }

    /// <summary>
    /// Letting go marks the area out to the column under the cursor, dug <see cref="DigDepth"/> levels below
    /// the ground where the drag started (Shift keeps the tool for another area).
    /// </summary>
    private void DigEnd(bool keep)
    {
        _digDragging = false;
        if (DigCorner(out int x2, out int z2))
        {
            var diggers = OwnDiggers();
            if (diggers.Count > 0)
            {
                _sim.Schedule(_sim.Tick, new DigAreaCommand(LocalPlayer, diggers.ToArray(), _digX, _digZ, x2, z2, _digLevel - DigDepth));
                _rings.ShowMarker(new Vector3((_digX + x2) / 2f + 0.5f, _digLevel, (_digZ + z2) / 2f + 0.5f) * ChunkMesher.BlockSize,
                    SelectionRenderer.Work);
            }
        }
        _digPreview.Clear();
        if (!keep) SetTool(ToolMode.Select);
        else Changed?.Invoke();
    }

    /// <summary>The column under the cursor, kept within <see cref="DigArea.MaxSide"/> of where the drag started.</summary>
    private bool DigCorner(out int x2, out int z2)
    {
        x2 = z2 = 0;
        if (!GroundUnder(_mousePos, out int x, out _, out int z)) return false;
        int m = DigArea.MaxSide - 1;
        x2 = Math.Clamp(x, _digX - m, _digX + m);
        z2 = Math.Clamp(z, _digZ - m, _digZ + m);
        return true;
    }

    private void UpdateDigPreview()
    {
        _digPreview.Clear();
        if (Tool != ToolMode.Dig || !_digDragging || !DigCorner(out int x2, out int z2)) return;
        var w = World;
        for (int z = Math.Min(_digZ, z2); z <= Math.Max(_digZ, z2); z++)
        for (int x = Math.Min(_digX, x2); x <= Math.Max(_digX, x2); x++)
        {
            int y = Walk.GroundLevel(w, x, z);
            if (y >= 1) _digPreview.Add((x, y, z));
        }
    }

    // ---- Wall tool ----

    /// <summary>The Wall button and key: starts (or stops) drawing walls for the selected workers to build.</summary>
    public void ToggleWall() => SetTool(Tool == ToolMode.Wall ? ToolMode.Select : ToolMode.Wall);

    /// <summary>The Bridge button and key: starts (or stops) drawing bridges for the selected workers to build.</summary>
    public void ToggleBridge() => SetTool(Tool == ToolMode.Bridge ? ToolMode.Select : ToolMode.Bridge);

    /// <summary>The Ramp button and key (Shift+X): starts (or stops) drawing ramps for the selected workers and sappers to heap up.</summary>
    public void ToggleRamp() => SetTool(Tool == ToolMode.Ramp ? ToolMode.Select : ToolMode.Ramp);

    /// <summary>The Fell tree button and key (Ctrl+X): pick a tree, then drag the way it should fall, for the selected militia to fell.</summary>
    public void ToggleFell() => SetTool(Tool == ToolMode.Fell ? ToolMode.Select : ToolMode.Fell);

    /// <summary>The Sound for ore button and key (Ctrl+Z): click a spot for the selected foremen to sound the ground there.</summary>
    public void ToggleProspect() => SetTool(Tool == ToolMode.Prospect ? ToolMode.Select : ToolMode.Prospect);

    /// <summary>The selected units of yours that can fell a tree the way you choose (see <see cref="FellCommand"/>).</summary>
    public List<int> OwnFellers()
    {
        var list = new List<int>();
        foreach (int id in OwnSelected())
            if (_sim.Economy.FellTicksOf(_sim.Units.IndexOf(id)) > 0) list.Add(id);
        return list;
    }

    /// <summary>The selected units of yours that can sound the ground (see <see cref="ProspectCommand"/>).</summary>
    public List<int> OwnProspectors()
    {
        var list = new List<int>();
        foreach (int id in OwnSelected())
            if (_sim.Economy.ProspectTicksOf(_sim.Units.IndexOf(id)) > 0) list.Add(id);
        return list;
    }

    /// <summary>Your units doing this job (for the status line).</summary>
    /// <summary>Your finished buildings that have lost hit points.</summary>
    private int OwnDamaged() => _sim.Buildings.All.Count(b => b.Owner == LocalPlayer && b.Damaged);

    private int Jobs(UnitJob job)
    {
        int n = 0;
        var u = _sim.Units;
        for (int i = 0; i < u.Count; i++)
            if (u.Owner[i] == LocalPlayer && u.Job[i] == job) n++;
        return n;
    }

    /// <summary>The tree the fell tool picked (its trunk's column), or (-1, -1) while none is picked.</summary>
    public (int X, int Z) FellTree { get; private set; } = (-1, -1);

    /// <summary>The columns the picked tree comes down on, the way it is being dragged (see <see cref="PhysicsSystem.FallColumns"/>).</summary>
    private void UpdateFellPreview()
    {
        FellTree = (-1, -1);
        if (!_wallDragging || !_sim.Economy.FindTree(_wallX, _wallZ, out int tx, out int baseY, out int tz)) return;
        FellTree = (tx, tz);
        if (!GroundUnder(_mousePos, out int x2, out _, out int z2) || (x2 == _wallX && z2 == _wallZ)) return;
        int dx = x2 - _wallX, dz = z2 - _wallZ;
        (dx, dz) = Math.Abs(dx) >= Math.Abs(dz) ? (Math.Sign(dx), 0) : (0, Math.Sign(dz));
        var (_, top) = Harvest.Span(World, tx, baseY, tz);
        int reach = top - baseY + 3;
        for (int k = 1; k <= reach; k++)
        {
            int x = tx + dx * k, z = tz + dz * k;
            if (!World.InBounds(x, 0, z)) break;
            int y = Walk.GroundLevel(World, x, z);
            if (y >= 1) _wallPreview.Add((x, y, z, true));
        }
    }

    /// <summary>The ring of ground the selected prospectors would sound, round the column under the cursor.</summary>
    private void UpdateProspectPreview()
    {
        int r = _sim.Economy.ProspectRadius(LocalPlayer);
        if (r <= 0 || !GroundUnder(_mousePos, out int cx, out _, out int cz)) return;
        for (int dz = -r; dz <= r; dz++)
        for (int dx = -r; dx <= r; dx++)
        {
            int d2 = dx * dx + dz * dz;
            if (d2 > r * r || d2 <= (r - 1) * (r - 1) && (dx != 0 || dz != 0)) continue;
            int x = cx + dx, z = cz + dz;
            if (!World.InBounds(x, 0, z)) continue;
            int y = Walk.GroundLevel(World, x, z);
            if (y >= 1) _wallPreview.Add((x, y, z, true));
        }
    }

    /// <summary>The Ladder button and key (Shift+K): starts (or stops) placing siege ladders for the selected workers and sappers to raise.</summary>
    public void ToggleLadder() => SetTool(Tool == ToolMode.Ladder ? ToolMode.Select : ToolMode.Ladder);

    /// <summary>The Dam button and key (Shift+O): starts (or stops) drawing dams for the selected workers and sappers to build.</summary>
    public void ToggleDam() => SetTool(Tool == ToolMode.Dam ? ToolMode.Select : ToolMode.Dam);

    /// <summary>The Earth Wall button and key (Shift+Y): starts (or stops) drawing an earth wall for a selected mage to raise.</summary>
    public void ToggleEarthWall() => SetTool(Tool == ToolMode.EarthWall ? ToolMode.Select : ToolMode.EarthWall);

    /// <summary>The Ice Bridge button and key (Shift+'): starts (or stops) drawing an ice bridge for a selected mage to freeze.</summary>
    public void ToggleIceBridge() => SetTool(Tool == ToolMode.IceBridge ? ToolMode.Select : ToolMode.IceBridge);

    /// <summary>The Chasm button and key (Shift+Z): starts (or stops) drawing a chasm for a selected mage to open.</summary>
    public void ToggleChasm() => SetTool(Tool == ToolMode.Chasm ? ToolMode.Select : ToolMode.Chasm);

    /// <summary>The Grow Forest button and key (Shift+L): starts (or stops) drawing a grove for a selected mage to grow.</summary>
    public void ToggleGrowForest() => SetTool(Tool == ToolMode.GrowForest ? ToolMode.Select : ToolMode.GrowForest);

    /// <summary>
    /// The selected caster that would raise the next earth wall: the first ready one, or 0. With
    /// <paramref name="soonest"/>, the one ready soonest when none is ready.
    /// </summary>
    public int ReadyCaster(bool soonest = false, Spell spell = Spell.EarthWall)
    {
        int best = 0;
        long at = long.MaxValue;
        foreach (int id in OwnCasters(spell))
        {
            int i = _sim.Units.IndexOf(id);
            if (_sim.Spells.CanCast(i, spell)) return id;
            if (soonest && _sim.Spells.ReadyAt(i) < at) (best, at) = (id, _sim.Spells.ReadyAt(i));
        }
        return best;
    }

    /// <summary>
    /// How many of the selected casters would cast the spell together now (see <see cref="SpellSystem.GroupSize"/>):
    /// the ready ones, at most <see cref="SpellSystem.MaxGroup"/>, or 1 while none is ready (for the preview).
    /// </summary>
    public int GroupCasters(Spell spell = Spell.EarthWall)
    {
        int n = 0;
        foreach (int id in OwnCasters(spell))
            if (_sim.Spells.CanCast(_sim.Units.IndexOf(id), spell)) n++;
        return SpellSystem.GroupSize(n);
    }

    /// <summary>Pressing the button down on the ground starts the wall (or bridge) there.</summary>
    private void WallStart(Vector2 screen)
    {
        if (!GroundUnder(screen, out int x, out _, out int z)) return;
        (_wallDragging, _wallX, _wallZ) = (true, x, z);
        Changed?.Invoke();
    }

    /// <summary>Letting go lays the wall to the column under the cursor (Shift keeps the tool for another stretch).</summary>
    private void WallEnd(bool keep)
    {
        _wallDragging = false;
        if (GroundUnder(_mousePos, out int x2, out int y2, out int z2))
        {
            var workers = OwnWorkers(OwnSelected()).ToArray();
            if (Tool == ToolMode.EarthWall)
                _sim.Schedule(_sim.Tick, new EarthWallCommand(LocalPlayer, OwnCasters().ToArray(), _wallX, _wallZ, x2, z2));
            else if (Tool == ToolMode.IceBridge)
                _sim.Schedule(_sim.Tick, new IceBridgeCommand(LocalPlayer, OwnCasters(Spell.IceBridge).ToArray(), _wallX, _wallZ, x2, z2));
            else if (Tool == ToolMode.Chasm)
                _sim.Schedule(_sim.Tick, new ChasmCommand(LocalPlayer, OwnCasters(Spell.Chasm).ToArray(), _wallX, _wallZ, x2, z2));
            else if (Tool == ToolMode.GrowForest)
                _sim.Schedule(_sim.Tick, new GrowForestCommand(LocalPlayer, OwnCasters(Spell.GrowForest).ToArray(), _wallX, _wallZ, x2, z2));
            else if (Tool == ToolMode.Ramp)
                _sim.Schedule(_sim.Tick, new RampCommand(LocalPlayer, OwnDiggers().ToArray(), _wallX, _wallZ, x2, z2));
            else if (Tool == ToolMode.Ladder)
                _sim.Schedule(_sim.Tick, new LadderCommand(LocalPlayer, OwnDiggers().ToArray(), _wallX, _wallZ, x2, z2));
            else if (Tool == ToolMode.Dam)
                _sim.Schedule(_sim.Tick, new DamCommand(LocalPlayer, OwnDiggers().ToArray(), _wallX, _wallZ, x2, z2));
            else if (Tool == ToolMode.Fell)
            {
                if (x2 != _wallX || z2 != _wallZ)
                    _sim.Schedule(_sim.Tick, new FellCommand(LocalPlayer, OwnFellers().ToArray(), _wallX, _wallZ, x2 - _wallX, z2 - _wallZ));
            }
            else if (Tool == ToolMode.Prospect)
                _sim.Schedule(_sim.Tick, new ProspectCommand(LocalPlayer, OwnProspectors().ToArray(), x2, z2));
            else if (Tool == ToolMode.Stampede)
            {
                if (SelectedPen() is { } pen) _sim.Schedule(_sim.Tick, new StampedeCommand(LocalPlayer, pen.Id, _wallX, _wallZ, x2, z2));
            }
            else _sim.Schedule(_sim.Tick, Tool == ToolMode.Bridge
                ? new BridgeCommand(LocalPlayer, workers, BridgeBuilding, _wallX, _wallZ, x2, z2)
                : new WallCommand(LocalPlayer, workers, WallBuilding, _wallX, _wallZ, x2, z2));
            _rings.ShowMarker(new Vector3((_wallX + x2) / 2f + 0.5f, y2, (_wallZ + z2) / 2f + 0.5f) * ChunkMesher.BlockSize,
                SelectionRenderer.Work);
        }
        _wallPreview.Clear();
        if (!keep) SetTool(ToolMode.Select);
        else Changed?.Invoke();
    }

    /// <summary>The columns of the line being dragged, as the simulation lays them: a wall where there is room, paid in turn.</summary>
    private void UpdateWallPreview()
    {
        _wallPreview.Clear();
        if (Tool == ToolMode.Bridge)
        {
            UpdateBridgePreview();
            return;
        }
        if (Tool == ToolMode.Ramp)
        {
            UpdateRampPreview();
            return;
        }
        if (Tool == ToolMode.Dam)
        {
            UpdateDamPreview();
            return;
        }
        if (Tool == ToolMode.Ladder)
        {
            UpdateLadderPreview();
            return;
        }
        if (Tool == ToolMode.Fell)
        {
            UpdateFellPreview();
            return;
        }
        if (Tool == ToolMode.Prospect)
        {
            UpdateProspectPreview();
            return;
        }
        if (Tool == ToolMode.Stampede)
        {
            UpdateStampedePreview();
            return;
        }
        if (Tool is ToolMode.EarthWall or ToolMode.IceBridge or ToolMode.Chasm or ToolMode.GrowForest)
        {
            UpdateEarthWallPreview();
            return;
        }
        if (Tool != ToolMode.Wall || !_wallDragging || !GroundUnder(_mousePos, out int x2, out _, out int z2)) return;
        if (!_sim.Content.TryGetBuilding(WallBuilding, out var def)) return;
        var stock = (int[])_sim.Economy.Stockpile(LocalPlayer).Clone();
        bool broke = false;
        foreach (var (x, z) in EconomySystem.WallLine(_wallX, _wallZ, x2, z2))
        {
            int y = Walk.GroundLevel(World, x, z);
            if (y < 1 || _sim.Buildings.AtColumn(x, z) != null) continue; // a gate or wall there stays
            bool ok = !broke && _sim.Economy.CanPlace(LocalPlayer, def, x, z, out _, checkPlayer: false) == PlaceResult.Ok;
            if (ok)
            {
                foreach (var c in def.Cost) stock[(int)c.Good] -= c.Amount;
                foreach (var c in def.Cost)
                    if (stock[(int)c.Good] < 0) broke = true; // the simulation stops laying here
                ok = !broke;
            }
            _wallPreview.Add((x, y, z, ok));
        }
    }

    private readonly List<BridgeSpan> _bridgePlan = new();
    private readonly List<RampSpan> _rampPlan = new();

    /// <summary>Your ramps still being heaped up.</summary>
    private int OwnRamps()
    {
        int n = 0;
        foreach (var r in _sim.Economy.Ramps)
            if (r.Owner == LocalPlayer) n++;
        return n;
    }

    /// <summary>Your units at work on a ramp.</summary>
    private int RampBuilders()
    {
        int n = 0;
        for (int i = 0; i < _sim.Units.Count; i++)
            if (_sim.Units.Owner[i] == LocalPlayer && _sim.Units.Job[i] == UnitJob.Ramp) n++;
        return n;
    }

    /// <summary>The top the ramp being dragged reaches, or -1 while it would not get there (see <see cref="EconomySystem.PlanRamp"/>).</summary>
    public int RampTop { get; private set; } = -1;
    /// <summary>Blocks of earth (each a stone) the ramp being dragged takes.</summary>
    public int RampBlocks { get; private set; }

    /// <summary>
    /// The columns of the ramp being dragged, as the simulation plans them (at the level units will stand on each),
    /// each with whether it goes up as planned: room, a top it gets to, and stone enough by then.
    /// </summary>
    private void UpdateRampPreview()
    {
        RampTop = -1;
        RampBlocks = 0;
        if (!_wallDragging || !GroundUnder(_mousePos, out int x2, out _, out int z2)) return;
        RampTop = _sim.Economy.PlanRamp(_wallX, _wallZ, x2, z2, _rampPlan);
        int stone = _sim.Economy.Stock(LocalPlayer, Good.Stone);
        foreach (var span in _rampPlan)
        {
            RampBlocks += span.Blocks;
            bool ok = RampTop >= 0 && span.Room && RampBlocks * EconomySystem.StonePerRamp <= stone;
            _wallPreview.Add((span.X, span.Top, span.Z, ok));
        }
    }
    /// <summary>The top the ladder being dragged reaches, or -1 while it cannot go up there (see <see cref="EconomySystem.PlanLadder"/>).</summary>
    public int LadderTop { get; private set; } = -1;
    /// <summary>Rungs (each <see cref="EconomySystem.WoodPerRung"/> wood) the ladder being dragged takes.</summary>
    public int LadderRungs { get; private set; }

    /// <summary>
    /// The ladder being dragged: its foot, and the top of what it leans on, each with whether it goes up as planned
    /// (somewhere to lean 2 to 10 levels up, room in the foot column, and wood enough).
    /// </summary>
    private void UpdateLadderPreview()
    {
        LadderTop = -1;
        LadderRungs = 0;
        if (!_wallDragging || !GroundUnder(_mousePos, out int x2, out _, out int z2)) return;
        LadderTop = _sim.Economy.PlanLadder(_wallX, _wallZ, x2, z2, out int foot);
        var (fx, fz) = EconomySystem.LadderFace(_wallX, _wallZ, x2, z2);
        bool ok = LadderTop >= 0;
        if (ok) LadderRungs = LadderTop - foot;
        ok &= LadderRungs * EconomySystem.WoodPerRung <= _sim.Economy.Stock(LocalPlayer, Good.Wood);
        if (foot >= 1) _wallPreview.Add((_wallX, foot, _wallZ, ok));
        int top = Walk.GroundLevel(World, fx, fz);
        if ((fx != _wallX || fz != _wallZ) && top >= 1) _wallPreview.Add((fx, top, fz, ok));
    }

    /// <summary>Your ladders being raised, and those up.</summary>
    private (int Raising, int Up) OwnLadders()
    {
        int raising = 0, up = 0;
        foreach (var l in _sim.Economy.Ladders)
            if (l.Owner == LocalPlayer)
            {
                if (l.Done) up++;
                else raising++;
            }
        return (raising, up);
    }

    private readonly List<DamSpan> _damPlan = new();

    /// <summary>The top the dam being dragged runs at, or -1 while it cannot go in (see <see cref="EconomySystem.PlanDam"/>).</summary>
    public int DamTop { get; private set; } = -1;
    /// <summary>Blocks (each a stone) the dam being dragged takes.</summary>
    public int DamBlocks { get; private set; }

    /// <summary>
    /// The columns of the dam being dragged, as the simulation plans them (at the level units will stand on its top),
    /// each with whether it goes in as planned: room, a dam that can go in at all, and stone enough by then.
    /// </summary>
    private void UpdateDamPreview()
    {
        DamTop = -1;
        DamBlocks = 0;
        if (!_wallDragging || !GroundUnder(_mousePos, out int x2, out _, out int z2)) return;
        DamTop = _sim.Economy.PlanDam(_wallX, _wallZ, x2, z2, _damPlan, out _);
        int stone = _sim.Economy.Stock(LocalPlayer, Good.Stone);
        foreach (var span in _damPlan)
        {
            DamBlocks += span.Blocks;
            bool ok = DamTop >= 0 && span.Room && DamBlocks * EconomySystem.StonePerRamp <= stone;
            _wallPreview.Add((span.X, span.Top, span.Z, ok));
        }
    }

    /// <summary>Your dams still being built, and your finished ones.</summary>
    private (int Building, int Done) OwnDams()
    {
        int building = 0, done = 0;
        foreach (var d in _sim.Economy.Dams)
            if (d.Owner == LocalPlayer)
            {
                if (d.Done) done++;
                else building++;
            }
        return (building, done);
    }

    /// <summary>Your units at work on a dam.</summary>
    private int DamBuilders()
    {
        int n = 0;
        for (int i = 0; i < _sim.Units.Count; i++)
            if (_sim.Units.Owner[i] == LocalPlayer && _sim.Units.Job[i] == UnitJob.Dam) n++;
        return n;
    }

    /// <summary>Columns from a dam's sluice within which a selected unit of yours offers to open or shut it.</summary>
    public const int SluiceOfferRange = 24;

    /// <summary>
    /// Your finished dam with a sluice nearest your selected units, with one of them within
    /// <see cref="SluiceOfferRange"/> columns of its sluice, or null (see <see cref="Dam"/>).
    /// </summary>
    public Dam? NearDam()
    {
        var own = OwnSelected();
        if (own.Count == 0) return null;
        var u = _sim.Units;
        Dam? best = null;
        long bestD = long.MaxValue;
        foreach (var dam in _sim.Economy.Dams)
        {
            if (dam.Owner != LocalPlayer || !dam.Done || !dam.SluiceColumn(out var s)) continue;
            foreach (int id in own)
            {
                int i = u.IndexOf(id);
                long dx = u.X[i].FloorToInt() - s.X, dz = u.Z[i].FloorToInt() - s.Z;
                if (Math.Abs(dx) > SluiceOfferRange || Math.Abs(dz) > SluiceOfferRange || dx * dx + dz * dz >= bestD) continue;
                best = dam;
                bestD = dx * dx + dz * dz;
            }
        }
        return best;
    }

    /// <summary>Water blocks within 8 columns of the sluice of the dam near your selected units, below its top (for scripted checks).</summary>
    private int SluiceWater()
    {
        if (NearDam() is not { } dam || !dam.SluiceColumn(out var s)) return 0;
        int n = 0;
        for (int x = s.X - 8; x <= s.X + 8; x++)
        for (int z = s.Z - 8; z <= s.Z + 8; z++)
        for (int y = Math.Max(1, s.Top - EconomySystem.MaxDamFill); y < s.Top; y++)
            if (World.InBounds(x, y, z) && World.Get(x, y, z) == BlockType.Water) n++;
        return n;
    }

    private bool IsBoat(int id) => _sim.Units.IndexOf(id) is int i and >= 0 && _sim.Content.Units[_sim.Units.Def[i]].IsBoat;

    /// <summary>Your selected ships, how many units are aboard them and how many seats they have in all.</summary>
    public (int Ships, int Aboard, int Seats) SelectedShips()
    {
        int ships = 0, aboard = 0, seats = 0;
        foreach (int id in OwnSelected())
        {
            if (!IsBoat(id)) continue;
            int i = _sim.Units.IndexOf(id);
            ships++;
            aboard += _sim.Boats.Load(i);
            seats += _sim.Content.Units[_sim.Units.Def[i]].Seats;
        }
        return (ships, aboard, seats);
    }

    /// <summary>Your selected ships put whoever is aboard ashore on the nearest land (see <see cref="UnloadCommand"/>).</summary>
    public void Unload()
    {
        var ships = OwnSelected().FindAll(IsBoat);
        if (ships.Count == 0) return;
        _sim.Schedule(_sim.Tick, new UnloadCommand(LocalPlayer, ships.ToArray()));
        Changed?.Invoke();
    }

    /// <summary>Opens the sluice of the dam near your selected units, or shuts it if it is open (see <see cref="NearDam"/>).</summary>
    /// <summary>Marks the selected building of yours to be taken down (Craft guilds' salvage), or takes the mark off.</summary>
    public void ToggleSalvage()
    {
        if (_sim.Buildings.Get(SelectedBuilding) is not { } b || b.Owner != LocalPlayer) return;
        _sim.Schedule(_sim.Tick, new SalvageCommand(LocalPlayer, System.Array.Empty<int>(), b.Id, Stop: b.Salvaging));
        Changed?.Invoke();
    }

    public void ToggleSluice()
    {
        if (NearDam() is not { } dam) return;
        _sim.Schedule(_sim.Tick, new SluiceCommand(LocalPlayer, dam.Id, !dam.Open));
        Changed?.Invoke();
    }

    private readonly List<(int X, int Z, int Y, bool Room)> _earthPlan = new();

    /// <summary>The columns the earth wall being dragged covers, as the simulation plans them, each with whether earth rises there.</summary>
    private void UpdateEarthWallPreview()
    {
        if (!_wallDragging || !GroundUnder(_mousePos, out int x2, out _, out int z2)) return;
        if (Tool == ToolMode.IceBridge) _sim.Spells.PlanIce(_wallX, _wallZ, x2, z2, _earthPlan, GroupCasters(Spell.IceBridge)); // empty if it cannot span
        else if (Tool == ToolMode.Chasm) _sim.Spells.PlanChasm(_wallX, _wallZ, x2, z2, _earthPlan, GroupCasters(Spell.Chasm));
        else if (Tool == ToolMode.GrowForest) _sim.Spells.PlanGrove(_wallX, _wallZ, x2, z2, _earthPlan, GroupCasters(Spell.GrowForest));
        else _sim.Spells.Plan(_wallX, _wallZ, x2, z2, _earthPlan, GroupCasters());
        foreach (var (x, z, y, room) in _earthPlan)
            if (y >= 1) _wallPreview.Add((x, y, z, room));
    }

    /// <summary>
    /// The deck columns of the bridge being dragged, as the simulation plans them (at the level units would stand
    /// on the deck), each with whether it goes there: room, and goods enough by then. Empty while the ends are
    /// not ground about level with each other.
    /// </summary>
    private void UpdateBridgePreview()
    {
        if (!_wallDragging || !GroundUnder(_mousePos, out int x2, out _, out int z2)) return;
        if (!_sim.Content.TryGetBuilding(BridgeBuilding, out var def)) return;
        if (_sim.Economy.PlanBridge(_wallX, _wallZ, x2, z2, _bridgePlan) < 0) return;
        var stock = (int[])_sim.Economy.Stockpile(LocalPlayer).Clone();
        bool broke = !_sim.Economy.Unlocked(LocalPlayer, def);
        foreach (var span in _bridgePlan)
        {
            bool ok = !broke && span.Room;
            if (ok)
            {
                foreach (var c in def.Cost) stock[(int)c.Good] -= c.Amount;
                foreach (var c in def.Cost)
                    if (stock[(int)c.Good] < 0) broke = true; // the simulation stops laying here
                ok = !broke;
            }
            _wallPreview.Add((span.X, span.DeckY + 1, span.Z, ok));
        }
    }

    /// <summary>Buildings of other players, leaving out city-state totems.</summary>
    /// <summary>Blood on a column next to (x, z): a click a little off a drop still counts as on the blood.</summary>
    private bool BloodNear(int x, int z)
    {
        for (int dx = -1; dx <= 1; dx++)
        for (int dz = -1; dz <= 1; dz++)
            if (_sim.Blood.At(x + dx, z + dz) > 0) return true;
        return false;
    }

    private int Bleeding()
    {
        int n = 0;
        for (int i = 0; i < _sim.Units.Count; i++)
            if (_sim.Units.Bleeding[i] != 0) n++;
        return n;
    }

    private int EnemyBuildings()
    {
        int n = 0;
        foreach (var b in _sim.Buildings.All)
            if (b.Owner != LocalPlayer && b.Def.Kind != BuildingKind.Totem) n++;
        return n;
    }

    /// <summary>One line describing the UI state, for scripted screenshot tests.</summary>
    /// <summary>For scripted tests: how many of your units of each type that hatches in batches you have, as id:count.</summary>
    private string Batches()
    {
        var u = _sim.Units;
        var counts = new List<string>();
        foreach (var def in _sim.Content.Units)
        {
            if (def.Batch <= 1 || def.Faction != _sim.FactionOf(LocalPlayer)) continue;
            int n = 0;
            for (int i = 0; i < u.Count; i++)
                if (u.Owner[i] == LocalPlayer && u.Def[i] == def.Index) n++;
            counts.Add($"{def.Id}:{n}");
        }
        return counts.Count > 0 ? string.Join(',', counts) : "none";
    }

    /// <summary>For scripted tests: how many of your units have just burst out of the ground and strike harder.</summary>
    private int Bursting()
    {
        int n = 0;
        for (int i = 0; i < _sim.Units.Count; i++)
            if (_sim.Units.Owner[i] == LocalPlayer && _sim.Combat.Bursting(i)) n++;
        return n;
    }

    public string Describe()
    {
        var own = OwnSelected();
        int working = 0;
        foreach (int id in own)
            if (_sim.Units.Job[_sim.Units.IndexOf(id)] != UnitJob.None) working++;
        int buildings = 0, sites = 0, plans = 0, ready = 0;
        foreach (var b in _sim.Buildings.All)
        {
            if (b.Owner != LocalPlayer) continue;
            if (b.Planned)
            {
                plans++;
                if (_sim.Economy.CanStart(b) == PlaceResult.Ok) ready++;
                continue;
            }
            buildings++;
            if (b.State == BuildingState.Site) sites++;
        }
        var sel = _sim.Buildings.Get(SelectedBuilding);
        string building = sel == null ? "none" : $"{sel.Def.Id}";
        var u = _sim.Units;
        int units = 0, enemies = 0, enemiesShown = 0, drumsHeard = 0, drumming = 0, fighting = 0, routing = 0, tunnelling = 0, digging = 0, cleaning = 0, patrolling = 0, guarding = 0;
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Owner[i] != LocalPlayer)
            {
                if (u.Owner[i] == Simulation.WildPlayer) continue; // the monsters are counted as wild
                enemies++;
                if (Fog.Shows(i)) enemiesShown++;
                if (_sim.Vision.HearsDrum(LocalPlayer, u.Id[i])) drumsHeard++;
                continue;
            }
            units++;
            if (_sim.Combat.IsDrumming(i)) drumming++;
            if (u.Rout[i] > 0) routing++;
            else if (u.Target[i] != 0 || u.TargetBuilding[i] != 0) fighting++;
            if (u.Job[i] == UnitJob.Tunnel) tunnelling++;
            if (u.Job[i] == UnitJob.Dig) digging++;
            if (u.Job[i] == UnitJob.Clean) cleaning++;
            if (u.Combat[i] == CombatOrder.Patrol) patrolling++;
            if (u.Combat[i] == CombatOrder.Guard) guarding++;
        }
        int inFormation = 0;
        foreach (int id in own)
            if (u.Formation[u.IndexOf(id)]) inFormation++;
        int auto = 0;
        foreach (int id in OwnWorkers(own))
            if (u.Auto[u.IndexOf(id)]) auto++;
        var soldiers = OwnSoldiers();
        string stance = soldiers.Count > 0 ? u.Stance[u.IndexOf(soldiers[0])].ToString() : "none";
        int walled = 0;
        foreach (int id in own)
            if (u.ShieldWall[u.IndexOf(id)]) walled++;
        var evolution = _sim.Evolution;
        int canEvolve = 0, evolved = 0, heroes = 0, denForms = 0, relics = 0;
        foreach (int id in own)
            if (evolution.CanEvolve(u.IndexOf(id))) canEvolve++;
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Owner[i] != LocalPlayer) continue;
            var unitDef = _sim.Content.Units[u.Def[i]];
            if (unitDef.IsForm) evolved++;
            if (unitDef.Hero) heroes++;
            if (unitDef.FromDen) denForms++;
            if (u.Relic[i] != 0) relics++;
        }
        var autoForms = new List<string>();
        foreach (var def in _sim.Content.Units)
            if (evolution.AutoForm(LocalPlayer, def) is { } form) autoForms.Add($"{def.Id}:{form.Id}");
        int beaten = 0, monsters = 0, riders = 0, wild = 0, lairs = 0;
        for (int i = 0; i < u.Count; i++)
        {
            var def = _sim.Content.Units[u.Def[i]];
            // Monsters; a town's guards are counted as guards and the dusk tree's warden as the warden.
            if (u.Owner[i] == Simulation.WildPlayer && u.Town[i] == 0 && def != _sim.Content.DuskWarden) wild++;
            if (u.Owner[i] != LocalPlayer) continue;
            if (u.Subdued[i]) beaten++;
            else if (def.Role == UnitRole.Monster) monsters++;
            foreach (var m in _sim.Content.LairMonsters)
                if (m.Ride?.Becomes == def) riders++;
        }
        int densUsed = 0, lodges = 0;
        foreach (var lair in _sim.Monsters.Lairs)
        {
            if (!lair.Cleared && lair.Id != _sim.DuskTree.Lair) lairs++;
            if (lair.UsedBy == LocalPlayer) densUsed++;
        }
        foreach (var b in _sim.Buildings.All)
            if (b.Owner == LocalPlayer && b.Def.FromDen && b.Active) lodges++;
        int caravans = 0, trading = 0, markets = 0, mercs = 0;
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Owner[i] != LocalPlayer || u.Town[i] != 0) continue; // a city-state's own are counted apart
            var def = _sim.Content.Units[u.Def[i]];
            if (def.IsTrader)
            {
                caravans++;
                if (u.Job[i] == UnitJob.Trade) trading++;
                if (u.Job[i] == UnitJob.Market) markets++;
            }
            if (def.HiredFrom != null) mercs++;
        }
        // The selected town, else the first (the goblins'), for the city-state fields.
        var towns = _sim.Cities.Towns;
        var town = _sim.Cities.Get(SelectedCity) ?? (towns.Count > 0 ? towns[0] : null);
        int first = _selected.Count > 0 ? u.IndexOf(_selected[0]) : -1;
        var stock = _sim.Economy.Stockpile(LocalPlayer);
        int techs = 0;
        foreach (var t in _sim.Content.Techs)
            if (_sim.Economy.HasTech(LocalPlayer, t)) techs++;
        var cam = CameraColumn;
        var dusk = _sim.DuskTree;
        string duskState = !dusk.Exists ? "none" : dusk.Razed ? "razed" : dusk.Healed ? "healed" : dusk.Unguarded ? "unguarded" : "guarded";
        string warden = dusk.Lair != 0 ? "dead" : "none";
        for (int w = 0; w < u.Count; w++)
            if (_sim.Content.Units[u.Def[w]] == _sim.Content.DuskWarden)
            {
                warden = u.Owner[w] == Simulation.WildPlayer ? "wild" : u.Subdued[w] ? "beaten" : u.Owner[w] == LocalPlayer ? "own" : "other";
                break;
            }
        int bearer = dusk.StaffBearer != 0 ? u.IndexOf(dusk.StaffBearer) : -1;
        string staff = bearer < 0 ? "none" : u.Owner[bearer] == LocalPlayer ? "own" : "other";
        string staffAt = bearer < 0 ? "none" : $"{u.X[bearer].FloorToInt()},{u.Y[bearer]},{u.Z[bearer].FloorToInt()}";
        int onSupply = 0, carts = 0, cartsOnSupply = 0, cartLoad = 0, ponies = 0, poniesBringing = 0, ponyLoad = 0, due = 0;
        foreach (var b in _sim.Buildings.All)
            if (b.Owner == LocalPlayer && b.State == BuildingState.Site)
                foreach (int d in b.Due) due += d;
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Owner[i] != LocalPlayer) continue;
            if (u.Job[i] == UnitJob.Supply) onSupply++;
            if (_sim.Content.Units[u.Def[i]].Runs)
            {
                ponies++;
                if (u.Job[i] == UnitJob.Bring) poniesBringing++;
                ponyLoad = Math.Max(ponyLoad, u.CarryAmount[i]);
                continue;
            }
            if (!_sim.Content.Units[u.Def[i]].IsHauler) continue;
            carts++;
            if (u.Job[i] == UnitJob.Supply) cartsOnSupply++;
            cartLoad = Math.Max(cartLoad, u.CarryAmount[i]);
        }
        // The selected building's supply rules and what it holds of those goods, as good:held/kept.
        var supply = new List<string>();
        if (sel != null)
            foreach (var g in Goods.All)
                if (sel.Supply[(int)g] > 0) supply.Add($"{g}:{sel.Stock[(int)g]}/{sel.Supply[(int)g]}");
        int tending = 0;
        var ownSet = new HashSet<int>(own);
        foreach (int id in dusk.Tending)
            if (ownSet.Contains(id)) tending++;
        return $"selected={_selected.Count} first_id={(_selected.Count > 0 ? _selected[0] : 0)} own={own.Count} preview={_preview.Count} dragging={(_dragging ? 1 : 0)} " +
               $"paused={(_clock.Paused ? 1 : 0)} speed={_clock.Speed} slice={_slice.Level} tool={Tool} " +
               $"working={working} buildings={buildings} sites={sites} building={building} look={(sel != null ? sel.Look.ToString().ToLowerInvariant() : "none")} queue={sel?.Queue.Count ?? 0} rally={(sel is { HasRally: true } ? $"{sel.RallyX},{sel.RallyZ}" : "none")} " +
               $"ghost={(Ghost.Visible ? Ghost.Result.ToString() : "none")} road={_roadPreview.Count} " +
               $"plans={plans} plans_ready={ready} plan_mode={(PlanMode ? 1 : 0)} " +
               $"supply={(supply.Count > 0 ? string.Join(',', supply) : "none")} on_supply={onSupply} carts={carts} carts_on_supply={cartsOnSupply} cart_load={cartLoad} " +
               $"ponies={ponies} ponies_bringing={poniesBringing} pony_load={ponyLoad} due={due} " +
               $"food={stock[(int)Good.Food]} food_rate={_sim.Economy.FoodPerMinute(LocalPlayer)} wood={stock[(int)Good.Wood]} stone={stock[(int)Good.Stone]} iron={stock[(int)Good.Iron]} " +
               $"techtree={(TechTreeOpen ? 1 : 0)} priorities_menu={(PrioritiesOpen ? 1 : 0)} " +
               $"default_priorities={WorkPriorities.ToLog(_sim.Automation.DefaultPriorities(LocalPlayer))} " +
               $"priorities={(first >= 0 && _sim.Content.Units[u.Def[first]].Role == UnitRole.Worker ? WorkPriorities.ToLog(u.Priorities[first]) : "none")} " +
               $"research={_sim.Economy.Researching(LocalPlayer)?.Id ?? "none"} techs={techs} " +
               $"units={units} enemies={enemies} enemies_shown={enemiesShown} fog={(Fog.Enabled ? "on" : "off")} gazes={_sim.Economy.Gazes.Count(g => g.Player == LocalPlayer)} lens_selected={(SelectedLens() != null ? 1 : 0)} " +
               $"faction={_sim.FactionOf(LocalPlayer).Id} batches={Batches()} bursting={Bursting()} " +
               $"seen={_sim.Vision.SeenColumns(LocalPlayer)} fighting={fighting} patrolling={patrolling} guarding={guarding} routing={routing} drumming={drumming} drums_heard={drumsHeard} molting={Enumerable.Range(0, u.Count).Count(i => u.Owner[i] == LocalPlayer && u.Molting[i] > 0)} soft={Enumerable.Range(0, u.Count).Count(i => u.Owner[i] == LocalPlayer && u.Soft[i] > 0)} penned={Enumerable.Range(0, u.Count).Count(i => u.Owner[i] == LocalPlayer && u.Penned[i] != 0)} stampeding={Enumerable.Range(0, u.Count).Count(i => u.Owner[i] == LocalPlayer && u.RunStage[i] != 0)} trampled={_sim.Combat.Trampled} pens={OwnKind(BuildingKind.Pen)} tunnelling={tunnelling} " +
               $"digging={digging} dig_areas={_sim.Economy.DigAreas.Count} dig_preview={_digPreview.Count} dig_depth={DigDepth} " +
               $"casters={OwnCasters().Count} caster_ready={(ReadyCaster() != 0 ? 1 : 0)} casting={OwnCasting()} earth_walls={OwnEarthWalls()} earth_blocks={OwnEarthBlocks()} " +
               $"ice_casters={OwnCasters(Spell.IceBridge).Count} ice_bridges={OwnIce(out int iceBlocks, out int cracking)} ice_blocks={iceBlocks} ice_cracking={cracking} " +
               $"chasm_casters={OwnCasters(Spell.Chasm).Count} chasms={OwnChasms(out int chasmOpen)} chasm_open={chasmOpen} in_chasm={InChasm()} " +
               $"grove_casters={OwnCasters(Spell.GrowForest).Count} grove_trees={_sim.Spells.GroveTrees} " +
               $"walls={OwnWalls(BuildingKind.Wall)} gates={OwnWalls(BuildingKind.Gate)} bridges={OwnWalls(BuildingKind.Bridge)} wall_preview={_wallPreview.Count} " +
               $"ramps={OwnRamps()} ramp_top={RampTop} ramp_blocks={RampBlocks} ramp_builders={RampBuilders()} " +
               $"ladders={OwnLadders().Raising} ladders_up={OwnLadders().Up} ladder_top={LadderTop} ladder_rungs={LadderRungs} drag_from={_wallX},{_wallZ} " +
               $"dams={OwnDams().Building} dams_done={OwnDams().Done} dam_top={DamTop} dam_blocks={DamBlocks} dam_builders={DamBuilders()} " +
               $"near_dam={NearDam()?.Id ?? 0} sluice_open={(NearDam()?.Open == true ? 1 : 0)} sluice_water={SluiceWater()} " +
               $"tier1={OwnTier(1)} tier2={OwnTier(2)} upgrading={OwnUpgrading()} posted={OwnPosted()} " +
               $"facings={OwnFacings()} towers={OwnWalls(BuildingKind.Tower)} posted_on_towers={OwnPosted(towers: true)} " +
               $"health={sel?.Health ?? 0} max_health={sel?.MaxHealth ?? 0} damaged={OwnDamaged()} repairing={Jobs(UnitJob.Repair)} salvaging={Jobs(UnitJob.Salvage)} salvage_marked={_sim.Buildings.All.Count(b => b.Owner == LocalPlayer && b.Salvaging)} enemy_buildings={EnemyBuildings()} stones_dropped={_sim.Combat.StonesDropped} " +
               $"blood={_sim.Blood.Count} bleeding={Bleeding()} bled_out={_sim.Combat.BledOut} cleaning={cleaning} " +
               $"fellers={OwnFellers().Count} prospectors={OwnProspectors().Count} soundings={_sim.Economy.Soundings(LocalPlayer)} " +
               $"prospected={_sim.Economy.ProspectedOre(LocalPlayer).Count} felling={Jobs(UnitJob.Fell)} prospecting={Jobs(UnitJob.Prospect)} " +
               $"falling_trees={_sim.Physics.FallingTrees.Count} aimed_felled={_sim.Physics.AimedLanded} tree_crushed={_sim.Physics.TreeCrushed} " +
               $"auto={auto} auto_all={(_sim.Economy.AutoWorkers(LocalPlayer) ? 1 : 0)} repeat={(sel?.Repeat == true ? 1 : 0)} stance={stance} shield_wall={walled} " +
               $"rebuild={(_sim.Economy.Rebuilds(LocalPlayer) ? 1 : 0)} ruins={OwnRuins()} " +
               $"governor={(sel?.Governor ?? GovernorFocus.None).ToString().ToLowerInvariant()} town_guard={(sel != null ? _sim.Combat.TownGuardCount(sel) : 0)} " +
               $"houses={OwnKind(BuildingKind.House)} quarries={OwnKind(BuildingKind.Quarry)} " +
               $"quarrying={Enumerable.Range(0, u.Count).Count(i => u.Owner[i] == LocalPlayer && u.Job[i] == UnitJob.Quarry)} " +
               $"fishing={Enumerable.Range(0, u.Count).Count(i => u.Owner[i] == LocalPlayer && u.Job[i] == UnitJob.Quarry && _sim.Buildings.Get(u.JobTarget[i])?.Def.Shore == true)} " +
               $"saplings={_sim.Economy.Saplings.Count} trees_grown={_sim.Economy.TreesGrown} " +
               $"orchards={OwnKind(BuildingKind.Orchard)} orchards_grown={_sim.Buildings.All.Count(b => b.Owner == LocalPlayer && b.Active && b.Grown)} " +
               $"baskets={_sim.Buildings.All.Where(b => b.Owner == LocalPlayer).Sum(b => EconomySystem.Baskets(b))} " +
               $"ruin_near={NearRuin()?.Index ?? -1} ruin_searching={Enumerable.Range(0, u.Count).Count(i => u.Owner[i] == LocalPlayer && _sim.Ruins.Searching(u.Id[i]) >= 0)} " +
               $"ruins_found={_sim.Ruins.Ruins.Count(r => r.SearchedBy == LocalPlayer)} " +
               $"sites_active={(_sim.Sites.Active ? 1 : 0)} sites_held={_sim.Sites.Held(LocalPlayer)} sites_leader={_sim.Sites.Leader} " +
               $"great_works={_sim.Ascendancy.Works.Count} great_work_left={(_sim.Ascendancy.Leading is { } gw ? _sim.Ascendancy.TicksLeft(gw) : -1)} " +
               $"great_work_owner={_sim.Ascendancy.Leading?.Owner ?? -1} by_ascendancy={(_sim.Match.ByAscendancy ? 1 : 0)} " +
               $"herds={_sim.Monsters.Herds.Count(h => !h.Cleared)} deer={_sim.Monsters.Herds.Sum(h => h.Pack.Count)} hunted={_sim.Economy.GameHunted} " +
               $"wilds={(_sim.Map.HasWilds ? 1 : 0)} stalker_dens={_sim.Monsters.Lairs.Count(l => l.Monster.Id == "shade_stalker" && !l.Cleared)} " +
               $"stalkers={_sim.Monsters.Lairs.Where(l => l.Monster.Id == "shade_stalker").Sum(l => l.Pack.Count)} " +
               $"dark_stage={_sim.DarkPower.Stage} dark_radius={_sim.DarkPower.Radius} dark_next={_sim.DarkPower.TicksToNextStage} " +
               $"broods={_sim.DarkPower.Broods.Count} brood_beasts={_sim.DarkPower.Broods.Sum(b => b.Beasts.Count)} " +
               $"sites_left={_sim.Sites.TicksLeft} sites_claim={_sim.Ruins.Ruins.Max(r => (int?)r.Claim) ?? 0} " +
               $"ore={stock[(int)Good.IronOre]} coal={stock[(int)Good.Coal]} crystal={stock[(int)Good.Crystal]} gold={stock[(int)Good.Gold]} mining_gold={Enumerable.Range(0, u.Count).Count(i => u.Owner[i] == LocalPlayer && u.Job[i] == UnitJob.Gather && u.JobBlock[i] == BlockType.GoldOre)} mana={_sim.Economy.Player(LocalPlayer).Mana} " +
               $"planks={stock[(int)Good.Planks]} sawmills={OwnWorkshops(Good.Planks)} sawyers={WorkshopWorkers(Good.Planks)} " +
               $"cut_stone={stock[(int)Good.CutStone]} mason_yards={OwnWorkshops(Good.CutStone)} masons={WorkshopWorkers(Good.CutStone)} " +
               $"shrines={OwnKind(BuildingKind.Shrine)} keepers={Enumerable.Range(0, u.Count).Count(i => u.Owner[i] == LocalPlayer && u.Job[i] == UnitJob.Haul && _sim.Buildings.Get(u.JobTarget[i])?.Def.Kind == BuildingKind.Shrine)} " +
               $"farms={OwnKind(BuildingKind.Farm)} barracks={OwnKind(BuildingKind.Barracks)} storehouses={OwnKind(BuildingKind.Storehouse)} " +
               $"catapults={Enumerable.Range(0, u.Count).Count(i => u.Owner[i] == LocalPlayer && _sim.Content.Units[u.Def[i]].Id == "catapult")} " +
               $"rams={Enumerable.Range(0, u.Count).Count(i => u.Owner[i] == LocalPlayer && _sim.Content.Units[u.Def[i]].Id == "battering_ram")} " +
               $"craters={_sim.Combat.Craters} " +
               $"siege_workshops={_sim.Buildings.All.Count(b => b.Owner == LocalPlayer && b.Active && b.Def.Id == "siege_workshop")} " +
               $"ships={Enumerable.Range(0, u.Count).Count(i => u.Owner[i] == LocalPlayer && _sim.Content.Units[u.Def[i]].IsBoat)} " +
               $"aboard={Enumerable.Range(0, u.Count).Count(i => u.Owner[i] == LocalPlayer && u.Aboard[i] != 0)} " +
               $"ship_at={Enumerable.Range(0, u.Count).Where(i => u.Owner[i] == LocalPlayer && _sim.Content.Units[u.Def[i]].IsBoat).Select(i => $"{u.X[i].FloorToInt()},{u.Z[i].FloorToInt()}").FirstOrDefault() ?? "none"} " +
               $"docks={_sim.Buildings.All.Count(b => b.Owner == LocalPlayer && b.Active && b.Def.Id == "dock")} " +
               $"unit={(first >= 0 ? _sim.Content.Units[u.Def[first]].Id : "none")} xp={(first >= 0 ? u.Xp[first] : 0)} " +
               $"can_evolve={canEvolve} evolved={evolved} heroes={heroes} auto_evolve={(autoForms.Count > 0 ? string.Join(',', autoForms) : "none")} " +
               $"beaten={beaten} monsters={monsters} riders={riders} wild={wild} lairs={lairs} " +
               $"den_usable={(UsableDen() != null ? 1 : 0)} dens_used={densUsed} den_buildings={lodges} den_forms={denForms} relics={relics} dropped_items={_sim.Items.Items.Count} item0_at={(_sim.Items.Items.Count > 0 ? $"{_sim.Items.Items[0].X},{_sim.Items.Items[0].Y},{_sim.Items.Items[0].Z}" : "none")} " +
               $"city={(_sim.Cities.Get(SelectedCity)?.Def.Id ?? "none")} holder={town?.Holder ?? -1} " +
               $"totem_owner={(town != null ? _sim.Cities.Totem(town)?.Owner ?? -1 : -1)} guards={town?.Guards.Count() ?? 0} " +
               $"guards_owner={(town != null && town.Guards.Any() ? u.Owner[u.IndexOf(town.Guards.First())] : -1)} tribute={town?.Caravans.Count ?? 0} " +
               $"pool={town?.Ready ?? 0} caravans={caravans} trading={trading} markets={markets} mercs={mercs} " +
               $"bridge={(town?.HasBridge == true ? 1 : 0)} tolls={town?.Tolls ?? 0} feud={town?.FeudWith(LocalPlayer) ?? 0} " +
               $"idle={_sim.Economy.IdleWorkers(LocalPlayer).Count} army={_sim.Combat.ArmyUnits(LocalPlayer).Count} " +
               $"selected_workers={_selected.Count(id => u.IndexOf(id) is int k && k >= 0 && _sim.Content.Units[u.Def[k]].Role == UnitRole.Worker)} camera={cam.X},{cam.Z} " +
               $"dusk_tree={duskState} warden={warden} staff={staff} staff_at={staffAt} tending={tending} bloom_ready={(StaffSelected() is int sb && sb != 0 && dusk.CanBloom(u.IndexOf(sb)) ? 1 : 0)} " +
               $"formation={inFormation} line_preview={_formationPreview.Count} spread={Spread(own)}";
    }

    /// <summary>Your units walking into range to raise an earth wall.</summary>
    private int OwnCasting()
    {
        int n = 0;
        for (int i = 0; i < _sim.Units.Count; i++)
            if (_sim.Units.Owner[i] == LocalPlayer && _sim.Spells.Casting(_sim.Units.Id[i])) n++;
        return n;
    }

    /// <summary>Earth walls of yours standing now.</summary>
    private int OwnEarthWalls()
    {
        int n = 0;
        foreach (var wall in _sim.Spells.Walls)
            if (wall.Owner == LocalPlayer) n++;
        return n;
    }

    /// <summary>Chasms of yours open now, and how many of their blocks are still open (air).</summary>
    private int OwnChasms(out int open)
    {
        int n = 0;
        open = 0;
        foreach (var chasm in _sim.Spells.Chasms)
        {
            if (chasm.Owner != LocalPlayer) continue;
            n++;
            foreach (var (x, y, z) in chasm.Blocks)
                if (_sim.World.Get(x, y, z) == BlockType.Air) open++;
        }
        return n;
    }

    /// <summary>Units (anyone's) standing in an open chasm, below the ground it was cut from.</summary>
    private int InChasm()
    {
        var u = _sim.Units;
        int n = 0;
        foreach (var chasm in _sim.Spells.Chasms)
        {
            var open = new HashSet<(int, int, int)>(chasm.Blocks);
            for (int i = 0; i < u.Count; i++)
                if (open.Contains((u.X[i].FloorToInt(), u.Y[i], u.Z[i].FloorToInt()))) n++;
        }
        return n;
    }

    /// <summary>Ice bridges of yours holding now, their ice blocks, and how many of them are cracking.</summary>
    private int OwnIce(out int blocks, out int cracking)
    {
        int n = 0;
        blocks = cracking = 0;
        foreach (var ice in _sim.Spells.IceBridges)
        {
            if (ice.Owner != LocalPlayer) continue;
            n++;
            if (_sim.Spells.Cracking(ice)) cracking++;
            foreach (var (x, y, z) in ice.Blocks)
                if (_sim.World.Get(x, y, z) == BlockType.Ice) blocks++;
        }
        return n;
    }

    /// <summary>Blocks of your standing earth walls still there (not dug away).</summary>
    private int OwnEarthBlocks()
    {
        int n = 0;
        foreach (var wall in _sim.Spells.Walls)
            if (wall.Owner == LocalPlayer)
                foreach (var (x, y, z) in wall.Blocks)
                    if (_sim.World.Get(x, y, z) is BlockType.Soil or BlockType.Grass) n++;
        return n;
    }

    /// <summary>Your walls and gates of a tier (0 the barrier, 1 the wooden wall, 2 the stone wall).</summary>
    private int OwnTier(int tier)
    {
        int n = 0;
        foreach (var b in _sim.Buildings.All)
            if (b.Owner == LocalPlayer && b.Def.IsWall && b.Def.FacingOf == null && b.Def.Tier == tier) n++;
        return n;
    }

    /// <summary>Your buildings of a kind, built or going up (not plans).</summary>
    private int OwnWorkshops(Good makes)
    {
        int n = 0;
        foreach (var b in _sim.Buildings.All)
            if (b.Owner == LocalPlayer && b.Def.Kind == BuildingKind.Workshop && b.Def.Makes == makes && !b.Planned) n++;
        return n;
    }

    private int WorkshopWorkers(Good makes)
    {
        var u = _sim.Units;
        int n = 0;
        for (int i = 0; i < u.Count; i++)
            if (u.Owner[i] == LocalPlayer && u.Job[i] == UnitJob.Haul && _sim.Buildings.Get(u.JobTarget[i]) is { } b
                && b.Def.Kind == BuildingKind.Workshop && b.Def.Makes == makes) n++;
        return n;
    }

    private int OwnKind(BuildingKind kind)
    {
        int n = 0;
        foreach (var b in _sim.Buildings.All)
            if (b.Owner == LocalPlayer && b.Def.Kind == kind && !b.Planned) n++;
        return n;
    }

    /// <summary>Your ruins that rebuild orders will put back.</summary>
    private int OwnRuins()
    {
        int n = 0;
        foreach (var r in _sim.Automation.Ruins)
            if (r.Owner == LocalPlayer) n++;
        return n;
    }

    /// <summary>The columns of facing on your stone walls and gates (sites too).</summary>
    private int OwnFacings()
    {
        int n = 0;
        foreach (var b in _sim.Buildings.All)
            if (b.Owner == LocalPlayer && b.Def.FacingOf != null) n++;
        return n;
    }

    /// <summary>Your walls and gates being upgraded.</summary>
    private int OwnUpgrading()
    {
        int n = 0;
        foreach (var b in _sim.Buildings.All)
            if (b.Owner == LocalPlayer && b.Upgrade != null) n++;
        return n;
    }

    /// <summary>Your units standing on top of a wall, gate or tower; or on a tower only.</summary>
    private int OwnPosted(bool towers = false)
    {
        int n = 0;
        var u = _sim.Units;
        for (int i = 0; i < u.Count; i++)
            if (u.Owner[i] == LocalPlayer && _sim.Economy.WallUnder(i) is { } b && (!towers || b.Def.IsTower)) n++;
        return n;
    }

    /// <summary>Your buildings of a kind; of walls and gates, those of the line itself, not their facings.</summary>
    private int OwnWalls(BuildingKind kind)
    {
        int n = 0;
        foreach (var b in _sim.Buildings.All)
            if (b.Owner == LocalPlayer && b.Def.Kind == kind && b.Def.FacingOf == null) n++;
        return n;
    }

    /// <summary>The columns the units stand in, as min_x,min_z,max_x,max_z ("none" for no units).</summary>
    private string Spread(List<int> ids)
    {
        if (ids.Count == 0) return "none";
        var u = _sim.Units;
        int x0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, z1 = int.MinValue;
        foreach (int id in ids)
        {
            int i = u.IndexOf(id);
            int x = u.X[i].FloorToInt(), z = u.Z[i].FloorToInt();
            (x0, z0, x1, z1) = (Math.Min(x0, x), Math.Min(z0, z), Math.Max(x1, x), Math.Max(z1, z));
        }
        return $"{x0},{z0},{x1},{z1}";
    }
}
