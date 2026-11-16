using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rts.Sim;
using Rts.Sim.Automation;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;

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
///                         again and again, fighting whatever they meet, until given another order)
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
///   V                     the selected soldiers' stance: aggressive, hold their ground, retreat when hurt
///   .                     select your next idle worker and centre the camera on it (as the HUD's Idle
///                         workers button does)
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

    public enum ToolMode { Select, Terrain, Build, Road, Tunnel, Dig, Wall, Bridge, Test }

    /// <summary>Building hotkeys, in the order the HUD lists them.</summary>
    public static readonly (Key Key, string Building)[] BuildKeys =
    {
        (Key.H, "house"), (Key.G, "storehouse"), (Key.C, "farm"), (Key.B, "barracks"), (Key.M, "smelter"), (Key.N, "town_hall"),
        (Key.O, "barrier_gate"), (Key.Slash, "watchtower"), (Key.Backslash, "stone_tower"), (Key.I, "torch"), (Key.Semicolon, "campfire"),
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
                        case ToolMode.Wall or ToolMode.Bridge:
                            WallStart(mb.Position);
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
                else if (Tool is ToolMode.Wall or ToolMode.Bridge && _wallDragging)
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
        foreach (var (buildKey, building) in BuildKeys)
        {
            if (code != buildKey || key.CtrlPressed) continue;
            StartBuild(building);
            GetViewport().SetInputAsHandled();
            return;
        }
        switch (key.Keycode)
        {
            case Key.L: StartRoad(); break;
            case Key.Comma: Bloom(); break;
            case Key.Y: ToggleWall(); break;
            case Key.Apostrophe: ToggleBridge(); break;
            case Key.U:
                if (_sim.Buildings.Get(SelectedBuilding) is { Def.IsWall: true }) UpgradeSelected(stretch: !key.ShiftPressed);
                else TrainAtSelected(null);
                break;
            case Key.J:
                if (key.CtrlPressed) TogglePriorities();
                else if (key.ShiftPressed) ToggleAutoAll();
                else ToggleAutoSelected();
                break;
            case Key.P: ToggleRepeat(); break;
            case Key.Tab: TogglePlanMode(); break;
            case Key.Delete or Key.Backspace: CancelSelectedPlan(); break;
            case Key.V: CycleStance(); break;
            case Key.K: ToggleTechTree(); break;
            case Key.Period: SelectNextIdleWorker(); break;
            case Key.Space: _clock.TogglePause(); break;
            case Key.Minus or Key.KpSubtract: _clock.Slower(); break;
            case Key.Equal or Key.Plus or Key.KpAdd: _clock.Faster(); break;
            case Key.Pagedown: _slice.Lower(SliceStartLevel()); break;
            case Key.Pageup: _slice.Raise(); break;
            case Key.Home: _slice.Off(); break;
            case Key.T when TerrainTool:
                SetTool(Tool == ToolMode.Terrain ? ToolMode.Select : ToolMode.Terrain);
                break;
            case Key.X:
                SetTool(Tool == ToolMode.Tunnel ? ToolMode.Select : ToolMode.Tunnel);
                break;
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
            if (!_slice.Shows(u.Y[i]) || !Fog.Shows(i)) continue;
            var p = Centre(i);
            if (cam.IsPositionBehind(p)) continue;
            float depth = cam.GlobalPosition.DistanceTo(p);
            float radius = Mathf.Max(8f, 0.5f * pixelsPerMetre / depth);
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
            if (u.Owner[i] != LocalPlayer || u.Town[i] != 0 || !_slice.Shows(u.Y[i])) continue;
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
            return;
        }
        float bs = ChunkMesher.BlockSize;
        int target = PickUnit(screen);
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
        if (own.Count == 0 || !GroundUnder(screen, out int x, out int y, out int z)) return;
        var u = _sim.Units;
        var fighters = own.FindAll(id => _sim.Content.Units[u.Def[u.IndexOf(id)]].CanAttack);
        if (fighters.Count > 0) _sim.Schedule(_sim.Tick, new PatrolCommand(LocalPlayer, fighters.ToArray(), x, z, y));
        own.RemoveAll(fighters.Contains);
        if (own.Count > 0) _sim.Schedule(_sim.Tick, new MoveCommand(LocalPlayer, own.ToArray(), x, z, y));
        _rings.ShowMarker(new Vector3(x + 0.5f, y, z + 0.5f) * ChunkMesher.BlockSize, SelectionRenderer.Other);
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
        // Workers with something to do there: build the site, work the farm or smelter, unload at storage.
        var u = _sim.Units;
        var working = new List<int>();
        if (b.Owner == LocalPlayer)
            foreach (int id in OwnWorkers(own))
                if (b.WantsBuilders || b.Def.WorkerSlots > 0 || (b.Def.Storage && u.CarryAmount[u.IndexOf(id)] > 0))
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
        _rings.ShowMarker(centre, working.Count > 0 ? SelectionRenderer.Work : SelectionRenderer.Own);
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
        return b != null && hit.Y >= b.Y - 2 && hit.Y < b.Y + Math.Max(b.Def.Height, 1) ? b : null;
    }

    /// <summary>The city-state town whose square (ground or huts) is under the cursor, or null.</summary>
    public CityState? CityUnder(Vector2 screen)
    {
        if (!VoxelRay.FromScreen(_rig.Camera, screen, World, HiddenFrom, out var hit, out _) || !Fog.HasSeen(hit.X, hit.Z)) return null;
        var town = _sim.Cities.At(hit.X, hit.Z);
        return town != null && hit.Y >= town.Y - 2 && hit.Y <= town.Y + 5 ? town : null;
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
        if (tool != Tool || tool is not (ToolMode.Wall or ToolMode.Bridge)) _wallDragging = false;
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

    /// <summary>You give up the match (see <see cref="SurrenderCommand"/>). The game runs on so that it ends at once.</summary>
    public void Surrender()
    {
        _sim.Schedule(_sim.Tick, new SurrenderCommand(LocalPlayer));
        _clock.SetSpeed(_clock.SpeedIndex, resume: true);
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

    /// <summary>The selected soldiers of yours take the stance after the first one's: aggressive, hold, retreat.</summary>
    public void CycleStance()
    {
        var soldiers = OwnSoldiers();
        if (soldiers.Count == 0) return;
        var next = (UnitStance)(((int)_sim.Units.Stance[_sim.Units.IndexOf(soldiers[0])] + 1) % 3);
        _sim.Schedule(_sim.Tick, new StanceCommand(LocalPlayer, soldiers.ToArray(), next));
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
        if (own.Count == 0 || _sim.Monsters.DenUse(LocalPlayer) == null) return null;
        var indexes = new List<int>(own.Count);
        foreach (int id in own) indexes.Add(_sim.Units.IndexOf(id));
        foreach (var lair in _sim.Monsters.Lairs)
            if (_sim.Monsters.CanUseDen(LocalPlayer, lair, indexes)) return lair;
        return null;
    }

    /// <summary>Your selected units use the den near them, as your faction does (the Orcs put up a wolf lodge on it).</summary>
    public void UseDen()
    {
        if (UsableDen() is not { } lair) return;
        _sim.Schedule(_sim.Tick, new DenCommand(LocalPlayer, OwnSelected().ToArray(), lair.Id));
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
        var result = _sim.Economy.CanPlace(LocalPlayer, def, cx, cz, out int baseY);
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
        _sim.Schedule(_sim.Tick, plan
            ? new PlanCommand(LocalPlayer, def.Id, Ghost.X, Ghost.Z)
            : new BuildCommand(LocalPlayer, OwnWorkers(OwnSelected()).ToArray(), def.Id, Ghost.X, Ghost.Z));
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
            _sim.Schedule(_sim.Tick, Tool == ToolMode.Bridge
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
        int units = 0, enemies = 0, enemiesShown = 0, fighting = 0, routing = 0, tunnelling = 0, digging = 0, cleaning = 0, patrolling = 0;
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Owner[i] != LocalPlayer)
            {
                if (u.Owner[i] == Simulation.WildPlayer) continue; // the monsters are counted as wild
                enemies++;
                if (Fog.Shows(i)) enemiesShown++;
                continue;
            }
            units++;
            if (u.Rout[i] > 0) routing++;
            else if (u.Target[i] != 0 || u.TargetBuilding[i] != 0) fighting++;
            if (u.Job[i] == UnitJob.Tunnel) tunnelling++;
            if (u.Job[i] == UnitJob.Dig) digging++;
            if (u.Job[i] == UnitJob.Clean) cleaning++;
            if (u.Combat[i] == CombatOrder.Patrol) patrolling++;
        }
        int inFormation = 0;
        foreach (int id in own)
            if (u.Formation[u.IndexOf(id)]) inFormation++;
        int auto = 0;
        foreach (int id in OwnWorkers(own))
            if (u.Auto[u.IndexOf(id)]) auto++;
        var soldiers = OwnSoldiers();
        string stance = soldiers.Count > 0 ? u.Stance[u.IndexOf(soldiers[0])].ToString() : "none";
        var evolution = _sim.Evolution;
        int canEvolve = 0, evolved = 0, denForms = 0;
        foreach (int id in own)
            if (evolution.CanEvolve(u.IndexOf(id))) canEvolve++;
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Owner[i] != LocalPlayer) continue;
            var unitDef = _sim.Content.Units[u.Def[i]];
            if (unitDef.IsForm) evolved++;
            if (unitDef.FromDen) denForms++;
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
        int caravans = 0, trading = 0, mercs = 0;
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Owner[i] != LocalPlayer || u.Town[i] != 0) continue; // a city-state's own are counted apart
            var def = _sim.Content.Units[u.Def[i]];
            if (def.IsTrader)
            {
                caravans++;
                if (u.Job[i] == UnitJob.Trade) trading++;
            }
            if (def.HiredFrom != null) mercs++;
        }
        var towns = _sim.Cities.Towns;
        var town = towns.Count > 0 ? towns[0] : null;
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
        int tending = 0;
        var ownSet = new HashSet<int>(own);
        foreach (int id in dusk.Tending)
            if (ownSet.Contains(id)) tending++;
        return $"selected={_selected.Count} first_id={(_selected.Count > 0 ? _selected[0] : 0)} own={own.Count} preview={_preview.Count} dragging={(_dragging ? 1 : 0)} " +
               $"paused={(_clock.Paused ? 1 : 0)} speed={_clock.Speed} slice={_slice.Level} tool={Tool} " +
               $"working={working} buildings={buildings} sites={sites} building={building} queue={sel?.Queue.Count ?? 0} " +
               $"ghost={(Ghost.Visible ? Ghost.Result.ToString() : "none")} road={_roadPreview.Count} " +
               $"plans={plans} plans_ready={ready} plan_mode={(PlanMode ? 1 : 0)} " +
               $"food={stock[(int)Good.Food]} wood={stock[(int)Good.Wood]} stone={stock[(int)Good.Stone]} iron={stock[(int)Good.Iron]} " +
               $"techtree={(TechTreeOpen ? 1 : 0)} priorities_menu={(PrioritiesOpen ? 1 : 0)} " +
               $"default_priorities={WorkPriorities.ToLog(_sim.Automation.DefaultPriorities(LocalPlayer))} " +
               $"priorities={(first >= 0 && _sim.Content.Units[u.Def[first]].Role == UnitRole.Worker ? WorkPriorities.ToLog(u.Priorities[first]) : "none")} " +
               $"research={_sim.Economy.Researching(LocalPlayer)?.Id ?? "none"} techs={techs} " +
               $"units={units} enemies={enemies} enemies_shown={enemiesShown} fog={(Fog.Enabled ? "on" : "off")} " +
               $"seen={_sim.Vision.SeenColumns(LocalPlayer)} fighting={fighting} patrolling={patrolling} routing={routing} tunnelling={tunnelling} " +
               $"digging={digging} dig_areas={_sim.Economy.DigAreas.Count} dig_preview={_digPreview.Count} dig_depth={DigDepth} " +
               $"walls={OwnWalls(BuildingKind.Wall)} gates={OwnWalls(BuildingKind.Gate)} bridges={OwnWalls(BuildingKind.Bridge)} wall_preview={_wallPreview.Count} " +
               $"tier1={OwnTier(1)} tier2={OwnTier(2)} upgrading={OwnUpgrading()} posted={OwnPosted()} " +
               $"facings={OwnFacings()} towers={OwnWalls(BuildingKind.Tower)} posted_on_towers={OwnPosted(towers: true)} " +
               $"health={sel?.Health ?? 0} enemy_buildings={EnemyBuildings()} " +
               $"blood={_sim.Blood.Count} bleeding={Bleeding()} bled_out={_sim.Combat.BledOut} cleaning={cleaning} " +
               $"auto={auto} auto_all={(_sim.Economy.AutoWorkers(LocalPlayer) ? 1 : 0)} repeat={(sel?.Repeat == true ? 1 : 0)} stance={stance} " +
               $"unit={(first >= 0 ? _sim.Content.Units[u.Def[first]].Id : "none")} xp={(first >= 0 ? u.Xp[first] : 0)} " +
               $"can_evolve={canEvolve} evolved={evolved} auto_evolve={(autoForms.Count > 0 ? string.Join(',', autoForms) : "none")} " +
               $"beaten={beaten} monsters={monsters} riders={riders} wild={wild} lairs={lairs} " +
               $"den_usable={(UsableDen() != null ? 1 : 0)} dens_used={densUsed} den_buildings={lodges} den_forms={denForms} " +
               $"city={(_sim.Cities.Get(SelectedCity)?.Def.Id ?? "none")} holder={town?.Holder ?? -1} " +
               $"totem_owner={(town != null ? _sim.Cities.Totem(town)?.Owner ?? -1 : -1)} guards={town?.Guards.Count() ?? 0} " +
               $"guards_owner={(town != null && town.Guards.Any() ? u.Owner[u.IndexOf(town.Guards.First())] : -1)} tribute={town?.Caravans.Count ?? 0} " +
               $"pool={town?.Ready ?? 0} caravans={caravans} trading={trading} mercs={mercs} " +
               $"idle={_sim.Economy.IdleWorkers(LocalPlayer).Count} camera={cam.X},{cam.Z} " +
               $"dusk_tree={duskState} warden={warden} staff={staff} staff_at={staffAt} tending={tending} bloom_ready={(StaffSelected() is int sb && sb != 0 && dusk.CanBloom(u.IndexOf(sb)) ? 1 : 0)} " +
               $"formation={inFormation} line_preview={_formationPreview.Count} spread={Spread(own)}";
    }

    /// <summary>Your walls and gates of a tier (0 the barrier, 1 the wooden wall, 2 the stone wall).</summary>
    private int OwnTier(int tier)
    {
        int n = 0;
        foreach (var b in _sim.Buildings.All)
            if (b.Owner == LocalPlayer && b.Def.IsWall && b.Def.FacingOf == null && b.Def.Tier == tier) n++;
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
