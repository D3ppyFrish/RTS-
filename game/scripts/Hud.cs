using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text;
using Godot;
using Rts.Sim;
using Rts.Sim.Automation;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;

namespace Rts.Game;

/// <summary>
/// The minimal HUD: stored goods, population and game clock along the top with research, the switch for
/// automatic workers and the time controls, a PAUSED banner, the build buttons on the left, the height
/// slice control on the right, what is selected along the bottom (with train and repeat buttons for a
/// building that trains units, for units that can evolve, a row of evolve buttons per type with the
/// form that type takes on its own, and for beaten monsters, Ride and Recruit buttons), a Beaten button in
/// the top bar while you have beaten monsters waiting, a button per city-state town in the top bar with
/// who holds it (its panel has hire buttons for its holder), the tech tree screen,
/// the selection box being dragged, and a line of key hints. The Surrender button just under the right
/// end of the top bar asks first (surrender_yes, surrender_no); once the match is over for you it reads
/// Results and opens the end of match panel again (see <see cref="MatchEndPanel"/>). Under the top bar on
/// the left are the Idle workers button and the latest alerts (see <see cref="Alerts"/>).
/// </summary>
public partial class Hud : CanvasLayer
{
    private Simulation _sim = null!;
    private GameClock _clock = null!;
    private HeightSlice _slice = null!;
    private PlayerControl _control = null!;

    private readonly Label[] _stockLabels = new Label[Goods.Count];
    private Label _popLabel = null!;
    private Label _manaLabel = null!;
    private Label _foodRateLabel = null!;
    private Label _clockLabel = null!;
    private WorldLighting _lighting = null!;
    private Button _dayButton = null!;
    private bool _dayIsNight;
    private Button _pauseButton = null!;
    private readonly List<Button> _speedButtons = new();
    private Control _pausedBanner = null!;
    private Label _pausedHint = null!;
    private VSlider _slider = null!;
    private Label _sliceLabel = null!;
    private PanelContainer _selectionPanel = null!;
    private Label _selectionLabel = null!;
    private Label _descriptionLabel = null!;
    private BodyChart _bodyChart = null!;
    private VBoxContainer _trainBox = null!;
    private Button _planModeButton = null!;
    private HBoxContainer _planRow = null!;
    private Button _startPlanButton = null!;
    private HBoxContainer _upgradeRow = null!;
    // Supply rules of the selected storage building of yours: a button per good.
    private VBoxContainer _supplyBox = null!;
    private VBoxContainer _governorBox = null!;
    private readonly List<(Button Button, GovernorFocus Focus)> _governorButtons = new();
    private readonly Button[] _supplyButtons = new Button[Goods.Count];
    private Button _upgradeButton = null!;
    private Button _upgradeOneButton = null!;
    private VBoxContainer _evolveBox = null!;
    private readonly List<EvolveRow> _evolveRows = new();
    private string _evolveKey = "";
    private HBoxContainer _tameRow = null!;
    private Label _tameLabel = null!;
    private Button _rideButton = null!, _recruitButton = null!, _beatenButton = null!;
    private Button _denButton = null!;
    private Button _ruinButton = null!;
    private Button _bloomButton = null!;
    private Button _earthWallButton = null!;
    private Button _chasmButton = null!;
    private Button _growForestButton = null!;
    private Button _iceBridgeButton = null!;
    private Button _digButton = null!;
    private readonly List<(Button Button, UnitDef Unit)> _trainButtons = new();
    private readonly List<(Button Button, CityState Town)> _cityButtons = new();
    // The ancient sites, with dominion on: who holds how many, and the dominion clock (click to look at the next site).
    private Button _sitesButton = null!;
    private int _nextSite;
    // The leader the sites button was last framed for (-1 nobody, -2 not yet).
    private int _sitesFramedFor = -2;
    private VBoxContainer _cityBox = null!;
    private HBoxContainer _hireRow = null!;
    private readonly List<(Button Button, UnitDef Unit)> _hireButtons = new();
    private CityStateDef? _cityFor;
    private Button? _repeatButton;
    private BuildingDef? _trainFor;
    private Button _autoButton = null!;
    private Button _rebuildButton = null!;
    private readonly List<(Button Button, BuildingDef? Def, Key Key)> _buildButtons = new();
    private Label _researchLabel = null!;
    private Button _techButton = null!;
    private TechTreePanel _techPanel = null!;
    private Button _prioritiesButton = null!, _workerPrioritiesButton = null!, _defendTownButton = null!;
    private Label _townGuardLabel = null!;
    private PrioritiesPanel _prioritiesPanel = null!;
    private Label _toolLabel = null!;
    private SelectionBox _box = null!;
    private bool _updatingSlider;
    private double _textTimer;
    private bool _textDirty = true;
    private Button _surrenderButton = null!;
    private Control _surrenderPanel = null!;
    private Alerts _alerts = null!;

    /// <summary>The alerts column and idle workers button (see <see cref="Alerts"/>).</summary>
    public Alerts AlertFeed => _alerts;

    /// <summary>The match is over for you: the Surrender button reads Results and raises <see cref="ResultsPressed"/>.</summary>
    public bool MatchOver { get; set; }

    /// <summary>The Results button was pressed (once the match is over for you).</summary>
    public event Action? ResultsPressed;

    public void Init(Simulation sim, GameClock clock, HeightSlice slice, PlayerControl control, WorldLighting lighting)
    {
        _lighting = lighting;
        _sim = sim;
        _clock = clock;
        _slice = slice;
        _control = control;

        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = UiStyle.Theme };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        _box = new SelectionBox { MouseFilter = Control.MouseFilterEnum.Ignore };
        _box.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(_box);

        BuildTopBar(root);
        BuildPausedBanner(root);
        BuildSurrenderPanel(root);
        BuildSlicePanel(root);
        BuildBuildPanel(root);
        BuildSelectionPanel(root);

        _alerts = new Alerts();
        _alerts.Init(_sim, _control);
        Place(_alerts, Control.LayoutPreset.TopLeft, 12, 54);
        root.AddChild(_alerts);

        _techPanel = new TechTreePanel { Visible = false };
        _techPanel.Init(_sim, _control);
        Place(_techPanel, Control.LayoutPreset.Center, 0, 0);
        _techPanel.GrowHorizontal = Control.GrowDirection.Both;
        _techPanel.GrowVertical = Control.GrowDirection.Both;
        root.AddChild(_techPanel);

        _prioritiesPanel = new PrioritiesPanel { Visible = false };
        _prioritiesPanel.Init(_sim, _control);
        Place(_prioritiesPanel, Control.LayoutPreset.Center, 0, 0);
        _prioritiesPanel.GrowHorizontal = Control.GrowDirection.Both;
        _prioritiesPanel.GrowVertical = Control.GrowDirection.Both;
        root.AddChild(_prioritiesPanel);

        _toolLabel = MakeLabel("", 14);
        Place(_toolLabel, Control.LayoutPreset.BottomRight, -14, -10);
        _toolLabel.GrowHorizontal = Control.GrowDirection.Begin;
        _toolLabel.GrowVertical = Control.GrowDirection.Begin;
        _toolLabel.HorizontalAlignment = HorizontalAlignment.Right;
        _toolLabel.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
        root.AddChild(_toolLabel);

        _slice.Changed += SyncSlider;
        _control.Changed += () => _textDirty = true;
        SyncSlider();
    }

    private void BuildTopBar(Control root)
    {
        var bar = new PanelContainer { ThemeTypeVariation = UiStyle.Bar };
        bar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
        root.AddChild(bar);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        bar.AddChild(row);

        foreach (var good in Goods.All)
        {
            _stockLabels[(int)good] = Stat(row, UiIcons.For(good), Goods.Name(good));
            if (good != Good.Food) continue;
            // Beside the food stock: food gained or lost a minute.
            _foodRateLabel = MakeLabel("", 16);
            _foodRateLabel.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            _foodRateLabel.TooltipText = "Food a minute: what was gathered in the last minute";
            _foodRateLabel.MouseFilter = Control.MouseFilterEnum.Pass;
            _stockLabels[(int)good].GetParent().AddChild(_foodRateLabel);
        }
        _manaLabel = Stat(row, UiIcons.Get("mana"), "Mana: channelled from crystal at your shrines, and spent on spells");
        _popLabel = Stat(row, UiIcons.Get("people"), "People over housing");
        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        _clockLabel = Stat(row, UiIcons.Get("time"), "Match time and game speed");
        // The time of day (see DayCycle): click to keep it day.
        _dayButton = MakeButton("");
        _dayButton.Name = "day_button";
        _dayButton.Icon = UiIcons.Get("sun");
        _dayButton.CustomMinimumSize = new Vector2(84, 0);
        _dayButton.Alignment = HorizontalAlignment.Left;
        _dayButton.Toggled += on => _lighting.Cycle.AlwaysDay = on;
        row.AddChild(_dayButton);
        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });

        _researchLabel = MakeLabel("", 15);
        _researchLabel.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(_researchLabel);
        _techButton = MakeButton("Techs [K]");
        _techButton.Name = "techs_button";
        _techButton.Pressed += () => _control.ToggleTechTree();
        row.AddChild(_techButton);
        _autoButton = MakeButton("Auto workers");
        _autoButton.Name = "auto_button";
        _autoButton.TooltipText = "[Shift+J] All your workers, and those you train, find their own jobs; houses and farms go up when needed";
        _autoButton.Pressed += () => _control.ToggleAutoAll();
        row.AddChild(_autoButton);
        _rebuildButton = MakeButton("Rebuild");
        _rebuildButton.Name = "rebuild_button";
        _rebuildButton.TooltipText = "[Shift+P] Rebuild orders: your destroyed buildings are cleared and put back where they stood, once no enemy is near and you can pay";
        _rebuildButton.Pressed += () => _control.ToggleRebuild();
        row.AddChild(_rebuildButton);
        _prioritiesButton = MakeButton("Priorities");
        _prioritiesButton.Name = "priorities_button";
        _prioritiesButton.TooltipText = "Work priorities [Ctrl+J]: what each automatic worker does first, and the default list for new workers";
        _prioritiesButton.Pressed += () => _control.TogglePriorities();
        row.AddChild(_prioritiesButton);
        _beatenButton = MakeButton("");
        _beatenButton.Name = "beaten_button";
        _beatenButton.ToggleMode = false;
        _beatenButton.TooltipText = "Monsters your units beat, waiting for you to ride or recruit them: click to select the next one\n" +
                                    "(or drag a box around several to choose for all of them at once)";
        _beatenButton.Pressed += () => _control.SelectNextBeaten();
        row.AddChild(_beatenButton);
        // Under the middle of the top bar, which is full: a button per city-state town, and the ancient sites.
        var under = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        under.AddThemeConstantOverride("separation", 6);
        foreach (var town in _sim.Cities.Towns)
        {
            var b = MakeButton("");
            b.Name = "city_" + town.Def.Id;
            b.ToggleMode = false;
            b.AddThemeFontSizeOverride("font_size", 14);
            b.TooltipText = $"{town.Def.Name}: {town.Def.Description}\nClick to select it and look at it. Whoever breaks the totem " +
                            "on its market holds the town.";
            b.Pressed += () => _control.FocusCity(town.Id);
            under.AddChild(b);
            _cityButtons.Add((b, town));
        }

        _sitesButton = MakeButton("");
        _sitesButton.Name = "sites_button";
        _sitesButton.ToggleMode = false;
        _sitesButton.Icon = UiIcons.Get("ruin");
        _sitesButton.Visible = false;
        _sitesButton.Pressed += () =>
        {
            int n = _sim.Ruins.Ruins.Count;
            if (n == 0) return;
            _control.FocusSite(_nextSite % n);
            _nextSite = (_nextSite + 1) % n;
        };
        under.AddChild(_sitesButton);
        Place(under, Control.LayoutPreset.CenterTop, 0, 54);
        under.GrowHorizontal = Control.GrowDirection.Both;
        bar.GetParent().AddChild(under);

        _pauseButton = MakeButton("Pause");
        _pauseButton.Pressed += () => _clock.TogglePause();
        row.AddChild(_pauseButton);
        for (int i = 0; i < GameClock.Speeds.Length; i++)
        {
            int index = i;
            var b = MakeButton(GameClock.Label(GameClock.Speeds[i]));
            b.Pressed += () => _clock.SetSpeed(index, resume: true);
            row.AddChild(b);
            _speedButtons.Add(b);
        }
    }

    /// <summary>
    /// The Surrender button, just under the right end of the top bar (which is full), and the panel that
    /// asks before you give up the match.
    /// </summary>
    private void BuildSurrenderPanel(Control root)
    {
        _surrenderButton = MakeButton("Surrender");
        _surrenderButton.Name = "surrender_button";
        _surrenderButton.ToggleMode = false;
        _surrenderButton.AddThemeFontSizeOverride("font_size", 14);
        _surrenderButton.TooltipText = "Give up the match (it asks first)";
        _surrenderButton.Pressed += () =>
        {
            if (MatchOver) ResultsPressed?.Invoke();
            else _surrenderPanel.Visible = !_surrenderPanel.Visible;
        };
        Place(_surrenderButton, Control.LayoutPreset.TopRight, -12, 54);
        _surrenderButton.GrowHorizontal = Control.GrowDirection.Begin;
        root.AddChild(_surrenderButton);

        var panel = new PanelContainer { Visible = false };
        panel.AddThemeStyleboxOverride("panel", UiStyle.Panel(0.97f, 18));
        Place(panel, Control.LayoutPreset.Center, 0, 0);
        panel.GrowHorizontal = Control.GrowDirection.Both;
        panel.GrowVertical = Control.GrowDirection.Both;
        root.AddChild(panel);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        panel.AddChild(column);
        var title = MakeLabel("Give up the match?", 24);
        title.ThemeTypeVariation = UiStyle.Heading;
        title.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(title);
        var text = MakeLabel("It counts as a defeat.", 14);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(text);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 12);
        column.AddChild(row);
        var yes = MakeButton("Surrender");
        yes.Name = "surrender_yes";
        yes.ToggleMode = false;
        Frame(yes, MatchEndPanel.DefeatColor.Darkened(0.45f));
        yes.Pressed += () =>
        {
            panel.Visible = false;
            _control.Surrender();
        };
        row.AddChild(yes);
        var no = MakeButton("Keep playing");
        no.Name = "surrender_no";
        no.ToggleMode = false;
        Frame(no, PlainColour);
        no.Pressed += () => panel.Visible = false;
        row.AddChild(no);
        _surrenderPanel = panel;
    }

    private void BuildPausedBanner(Control root)
    {
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        panel.AddThemeStyleboxOverride("panel", UiStyle.Panel(0.85f, 14));
        Place(panel, Control.LayoutPreset.CenterTop, 0, 70);
        panel.GrowHorizontal = Control.GrowDirection.Both;
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        var label = MakeLabel("PAUSED", 40);
        label.ThemeTypeVariation = UiStyle.Heading;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(label);
        _pausedHint = MakeLabel("", 14);
        _pausedHint.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(_pausedHint);
        panel.AddChild(column);
        root.AddChild(panel);
        _pausedBanner = panel;
    }

    private void BuildSlicePanel(Control root)
    {
        var panel = new PanelContainer();
        Place(panel, Control.LayoutPreset.CenterRight, -12, 0);
        panel.GrowHorizontal = Control.GrowDirection.Begin;
        panel.GrowVertical = Control.GrowDirection.Both;
        root.AddChild(panel);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        panel.AddChild(column);
        var title = MakeLabel("Height slice", 15);
        title.ThemeTypeVariation = UiStyle.Heading;
        title.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(title);
        _slider = new VSlider
        {
            MinValue = 1,
            MaxValue = _slice.MaxLevel,
            Step = 1,
            CustomMinimumSize = new Vector2(24, 260),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            FocusMode = Control.FocusModeEnum.None,
        };
        _slider.ValueChanged += v =>
        {
            if (!_updatingSlider) _slice.Set((int)v >= _slice.MaxLevel ? -1 : (int)v);
        };
        column.AddChild(_slider);
        _sliceLabel = MakeLabel("", 14);
        _sliceLabel.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(_sliceLabel);
        var keys = MakeLabel("PgUp / PgDn\nHome: off", 12);
        keys.HorizontalAlignment = HorizontalAlignment.Center;
        keys.ThemeTypeVariation = UiStyle.Dim;
        column.AddChild(keys);
    }

    /// <summary>
    /// One button per building (with its hotkey and cost, or the tech it needs) and one for roads, two abreast
    /// down the left side (one below the other they ran into the selection panel).
    /// </summary>
    private void BuildBuildPanel(Control root)
    {
        var panel = new PanelContainer();
        Place(panel, Control.LayoutPreset.CenterLeft, 12, -30);
        panel.GrowVertical = Control.GrowDirection.Both;
        root.AddChild(panel);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 4);
        panel.AddChild(column);
        var title = MakeLabel("Build", 16);
        title.ThemeTypeVariation = UiStyle.Heading;
        title.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(title);
        _planModeButton = MakeToolButton("Plan mode: off  [Tab]");
        _planModeButton.Name = "plan_mode";
        _planModeButton.TooltipText = "Place buildings as plans: marked out in see-through violet, not paid for or built. " +
                                      "A plan turns cyan once you can pay for it; right click it to start building. " +
                                      "Placing what you cannot afford makes a plan even with this off.";
        _planModeButton.Pressed += () => _control.TogglePlanMode();
        column.AddChild(_planModeButton);
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 4);
        grid.AddThemeConstantOverride("v_separation", 4);
        column.AddChild(grid);
        foreach (var (key, id) in PlayerControl.BuildKeys)
        {
            if (!_sim.Content.TryGetBuilding(id, out var def)) continue;
            var b = MakeToolButton("");
            b.Name = "build_" + def.Id;
            b.Icon = UiIcons.Get(def.Icon.Length > 0 ? def.Icon : def.Id);
            b.Pressed += () => _control.StartBuild(def.Id);
            if (def.Kind == BuildingKind.Light)
                b.TooltipText = $"Lights the ground {def.Light} columns around it from dusk to dawn: what stands there is seen from " +
                                "as far as by day and shot at as by day. A storm puts its open flame out until it passes.";
            if (def.IsQuarry)
                b.TooltipText = $"Right click it with workers: up to {def.WorkerSlots} work it, each round of " +
                                $"{def.WorkTicks / Simulation.TicksPerSecond} seconds giving {string.Join(", then ", def.Works.Select(w => $"{w.Amount} {Goods.Name(w.Good).ToLowerInvariant()}"))}, " +
                                "which they carry to storage. " +
                                (def.Shore ? "It must stand at the water's edge, with open water right beside it, and never runs out. "
                                           : "It never runs out, wherever it stands. ") +
                                "Automatic workers work it when its goods are needed.";
            if (def.Plants > 0)
                b.TooltipText = $"Right click it with workers: up to {def.WorkerSlots} foresters work it, each round of " +
                                $"{def.WorkTicks / Simulation.TicksPerSecond} seconds planting a sapling on open grass within {def.Plants} " +
                                $"columns of it, clear of buildings and other trees. A sapling grows into a tree in " +
                                $"{EconomySystem.SaplingGrowTicks / Simulation.TicksPerSecond / 60} minutes, for your wood cutters to fell. " +
                                "Automatic workers leave it to you.";
            if (def.Kind == BuildingKind.Shrine)
                b.TooltipText = $"Right click it with a worker: they bring it crystal from storage, and it channels each crystal " +
                                $"into {def.Mana} mana in {def.ChannelTicks / Simulation.TicksPerSecond} seconds. Spells cost mana. " +
                                "Crystal grows on the cavern walls: send workers to dig it. Automatic workers keep it once you have crystal.";
            if (def.IsTower)
                b.TooltipText = $"Stands alone, or on your wall: put it over a finished wall and it snaps onto it, and the wall " +
                                $"stands until the tower is done. It sees {def.Sight} columns around it. Archers and other ranged " +
                                $"units posted on top (right click it with them) aim better: {def.Aim}% of the shots they would " +
                                "miss in the dark or the rain land, and they hit the head more often.";
            grid.AddChild(b);
            _buildButtons.Add((b, def, key));
        }
        if (_sim.Content.TryGetBuilding(PlayerControl.WallBuilding, out var wallDef))
        {
            var wall = MakeToolButton("");
            wall.Name = "build_wall";
            wall.Icon = UiIcons.Get(wallDef.Icon.Length > 0 ? wallDef.Icon : wallDef.Id);
            wall.TooltipText = "Drag a line for the selected workers to wall off, a column at a time. " +
                               "Walls join up with each other and with gates; a gate lets only your units through. " +
                               "This is the cheap first tier, easy to knock down: select a column of it to upgrade the " +
                               "stretch to a wooden wall that archers stand on, and that to a stone wall three columns " +
                               "thick. Put a tower onto a wall for a strong point that sees far.";
            wall.Pressed += () => _control.ToggleWall();
            grid.AddChild(wall);
            _buildButtons.Add((wall, wallDef, Key.Y));
        }
        if (_sim.Content.TryGetBuilding(PlayerControl.BridgeBuilding, out var bridgeDef))
        {
            var bridge = MakeToolButton("");
            bridge.Name = "build_bridge";
            bridge.Icon = UiIcons.Get(bridgeDef.Icon.Length > 0 ? bridgeDef.Icon : bridgeDef.Id);
            bridge.TooltipText = "Drag from bank to bank for the selected workers to lay a level deck across deep water, a " +
                                 "chasm or a trench, a column at a time from the ends. Anyone can walk over it; Ctrl+right " +
                                 "click a bridge with soldiers selected to knock that column out.";
            bridge.Pressed += () => _control.ToggleBridge();
            grid.AddChild(bridge);
            _buildButtons.Add((bridge, bridgeDef, Key.Apostrophe));
        }
        var road = MakeToolButton("");
        road.Name = "build_road";
        road.Icon = UiIcons.Get("road");
        road.Pressed += () => _control.StartRoad();
        grid.AddChild(road);
        _buildButtons.Add((road, null, Key.L));
    }

    private void BuildSelectionPanel(Control root)
    {
        _selectionPanel = new PanelContainer();
        Place(_selectionPanel, Control.LayoutPreset.BottomLeft, 12, -12);
        _selectionPanel.GrowVertical = Control.GrowDirection.Begin;
        _selectionPanel.CustomMinimumSize = new Vector2(380, 0);
        root.AddChild(_selectionPanel);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        _selectionPanel.AddChild(column);
        _selectionLabel = MakeLabel("", 16);
        column.AddChild(_selectionLabel);
        _bodyChart = new BodyChart { Name = "body_chart", Visible = false };
        column.AddChild(_bodyChart);
        _descriptionLabel = MakeLabel("", 13);
        _descriptionLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _descriptionLabel.CustomMinimumSize = new Vector2(360, 0);
        _descriptionLabel.ThemeTypeVariation = UiStyle.Dim;
        column.AddChild(_descriptionLabel);
        _trainBox = new VBoxContainer();
        _trainBox.AddThemeConstantOverride("separation", 4);
        column.AddChild(_trainBox);
        _planRow = new HBoxContainer { Visible = false };
        _planRow.AddThemeConstantOverride("separation", 4);
        _startPlanButton = MakeToolButton("Start building");
        _startPlanButton.Name = "plan_start";
        _startPlanButton.ToggleMode = false;
        _startPlanButton.TooltipText = "Pay for it and start building, with the selected workers (or right click it).";
        _startPlanButton.Pressed += () => _control.StartSelectedPlan();
        _planRow.AddChild(_startPlanButton);
        var cancel = MakeToolButton("Remove plan  [Del]");
        cancel.Name = "plan_cancel";
        cancel.ToggleMode = false;
        cancel.Pressed += () => _control.CancelSelectedPlan();
        _planRow.AddChild(cancel);
        column.AddChild(_planRow);
        _upgradeRow = new HBoxContainer { Visible = false };
        _upgradeRow.AddThemeConstantOverride("separation", 4);
        _upgradeButton = MakeToolButton("Upgrade");
        _upgradeButton.Name = "wall_upgrade";
        _upgradeButton.ToggleMode = false;
        _upgradeButton.Pressed += () => _control.UpgradeSelected(stretch: true);
        _upgradeRow.AddChild(_upgradeButton);
        _upgradeOneButton = MakeToolButton("Only this one");
        _upgradeOneButton.Name = "wall_upgrade_one";
        _upgradeOneButton.ToggleMode = false;
        _upgradeOneButton.TooltipText = "Upgrade this column only.";
        _upgradeOneButton.Pressed += () => _control.UpgradeSelected(stretch: false);
        _upgradeRow.AddChild(_upgradeOneButton);
        column.AddChild(_upgradeRow);
        BuildSupplyRow(column);
        BuildGovernorRow(column);
        _evolveBox = new VBoxContainer();
        _evolveBox.AddThemeConstantOverride("separation", 4);
        column.AddChild(_evolveBox);

        _digButton = MakeToolButton("Dig an area  [Z]");
        _digButton.Name = "dig_button";
        _digButton.TooltipText = "Drag out an area for the selected workers and sappers to dig out, a layer at a time from the top. " +
                                 "Workers carry what they dig to storage; sappers dig twice as fast and leave it.";
        _digButton.Pressed += () => _control.ToggleDig();
        _digButton.Visible = false;
        column.AddChild(_digButton);

        _workerPrioritiesButton = MakeToolButton("Work priorities  [Ctrl+J]");
        _workerPrioritiesButton.Name = "worker_priorities";
        _workerPrioritiesButton.ToggleMode = false;
        _workerPrioritiesButton.TooltipText = "Set what the selected workers do first when they find their own work";
        _workerPrioritiesButton.Pressed += () => _control.TogglePriorities();
        _workerPrioritiesButton.Visible = false;
        column.AddChild(_workerPrioritiesButton);

        _defendTownButton = MakeToolButton("Defend the town  [Shift+V]");
        _defendTownButton.Name = "defend_town";
        _defendTownButton.ToggleMode = false;
        _defendTownButton.TooltipText = "Hand the selected soldiers to the governor of your nearest governed town hall: they guard the hall, " +
                                        $"and the governor sends them after enemies you can see anywhere within {CombatSystem.TownReach} columns of it.";
        _defendTownButton.Pressed += () => _control.DefendTown();
        _defendTownButton.Visible = false;
        column.AddChild(_defendTownButton);

        _tameRow = new HBoxContainer { Visible = false };
        _tameRow.AddThemeConstantOverride("separation", 4);
        _tameLabel = MakeLabel("", 13);
        _tameLabel.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _tameLabel.CustomMinimumSize = new Vector2(170, 0);
        _tameRow.AddChild(_tameLabel);
        _rideButton = MakeToolButton("Ride");
        _rideButton.Name = "tame_ride";
        _rideButton.ToggleMode = false;
        _rideButton.Pressed += () => _control.TameSelected(TameChoice.Ride);
        _tameRow.AddChild(_rideButton);
        _recruitButton = MakeToolButton("Recruit");
        _recruitButton.Name = "tame_recruit";
        _recruitButton.ToggleMode = false;
        _recruitButton.CustomMinimumSize = new Vector2(90, 0);
        _recruitButton.TooltipText = "It joins your army as it is, back at full health.";
        _recruitButton.Pressed += () => _control.TameSelected(TameChoice.Recruit);
        _tameRow.AddChild(_recruitButton);
        column.AddChild(_tameRow);

        _denButton = MakeToolButton("Use the den");
        _denButton.Name = "den_use";
        _denButton.ToggleMode = false;
        _denButton.Visible = false;
        _denButton.Pressed += () => _control.UseDen();
        column.AddChild(_denButton);

        _ruinButton = MakeToolButton("Search the ruin");
        _ruinButton.Name = "ruin_search";
        _ruinButton.Icon = UiIcons.Get("ruin");
        _ruinButton.ToggleMode = false;
        _ruinButton.Visible = false;
        _ruinButton.Pressed += () => _control.SearchRuin();
        column.AddChild(_ruinButton);

        _bloomButton = MakeToolButton("Dusk Bloom [,]");
        _bloomButton.Name = "dusk_bloom";
        _bloomButton.Icon = UiIcons.Get("dusk_bloom");
        _bloomButton.ToggleMode = false;
        _bloomButton.Visible = false;
        _bloomButton.Pressed += () => _control.Bloom();
        column.AddChild(_bloomButton);

        _earthWallButton = MakeToolButton("Earth Wall  [Shift+Y]");
        _earthWallButton.Name = "earth_wall";
        _earthWallButton.Icon = UiIcons.Get("earth_wall");
        _earthWallButton.Visible = false;
        _earthWallButton.Pressed += () => _control.ToggleEarthWall();
        column.AddChild(_earthWallButton);

        _iceBridgeButton = MakeToolButton("Ice Bridge  [Shift+']");
        _iceBridgeButton.Name = "ice_bridge";
        _iceBridgeButton.Icon = UiIcons.Get("ice_bridge");
        _iceBridgeButton.Visible = false;
        _iceBridgeButton.Pressed += () => _control.ToggleIceBridge();
        column.AddChild(_iceBridgeButton);

        _chasmButton = MakeToolButton("Chasm  [Shift+Z]");
        _chasmButton.Name = "chasm";
        _chasmButton.Icon = UiIcons.Get("chasm");
        _chasmButton.Visible = false;
        _chasmButton.Pressed += () => _control.ToggleChasm();
        column.AddChild(_chasmButton);

        _growForestButton = MakeToolButton("Grow Forest  [Shift+L]");
        _growForestButton.Name = "grow_forest";
        _growForestButton.Icon = UiIcons.Get("grow_forest");
        _growForestButton.Visible = false;
        _growForestButton.Pressed += () => _control.ToggleGrowForest();
        column.AddChild(_growForestButton);

        _cityBox = new VBoxContainer { Visible = false };
        _cityBox.AddThemeConstantOverride("separation", 4);
        _hireRow = new HBoxContainer();
        _hireRow.AddThemeConstantOverride("separation", 4);
        _cityBox.AddChild(_hireRow);
        column.AddChild(_cityBox);
    }

    /// <summary>The selected town's buttons: a hire button per mercenary (hire_UNIT).</summary>
    private void BuildCityButtons(CityStateDef? def)
    {
        _cityFor = def;
        foreach (var (b, _) in _hireButtons) b.QueueFree();
        _hireButtons.Clear();
        if (def == null) return;
        foreach (var unit in def.Mercenaries)
        {
            var b = MakeToolButton("");
            b.Name = "hire_" + unit.Id;
            b.ToggleMode = false;
            b.TooltipText = $"{unit.Description}\nOnly whoever holds the town hires them; one steps out at the market.";
            b.Pressed += () => _control.HireAtSelectedCity(unit.Id);
            _hireRow.AddChild(b);
            _hireButtons.Add((b, unit));
        }
    }

    /// <summary>The evolve buttons for one type of selected unit: what they are, a button per form, and the form they take on their own.</summary>
    private sealed record EvolveRow(UnitDef Unit, HBoxContainer Box, Label Label, List<(Button Button, UnitDef Form)> Forms, Button Auto);

    /// <summary>One row per type: a label, a button per form (evolve_ID) and the automatic choice (auto_evolve_ID).</summary>
    private void BuildEvolveRows(List<UnitDef> types, string key)
    {
        _evolveKey = key;
        foreach (var row in _evolveRows)
        {
            _evolveBox.RemoveChild(row.Box);
            row.Box.QueueFree();
        }
        _evolveRows.Clear();
        foreach (var unit in types)
        {
            var box = new HBoxContainer();
            box.AddThemeConstantOverride("separation", 4);
            var label = MakeLabel("", 13);
            label.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            label.CustomMinimumSize = new Vector2(170, 0); // so the rows' buttons line up
            box.AddChild(label);
            var forms = new List<(Button, UnitDef)>();
            foreach (var form in unit.Forms)
            {
                var b = MakeToolButton(form.Name);
                b.Name = "evolve_" + form.Id;
                b.ToggleMode = false;
                b.CustomMinimumSize = new Vector2(60, 0);
                b.TooltipText = FormTooltip(unit, form);
                b.Pressed += () => _control.EvolveSelected(form.Id);
                box.AddChild(b);
                forms.Add((b, form));
            }
            var auto = MakeToolButton("");
            auto.Name = "auto_evolve_" + unit.Id;
            auto.CustomMinimumSize = new Vector2(60, 0);
            auto.TooltipText = $"The form all your {Plural(unit.Name).ToLowerInvariant()}, now and later, take on their own " +
                               "once they have the experience. Click for the next choice.";
            auto.Pressed += () => _control.CycleAutoEvolve(unit.Id);
            box.AddChild(auto);
            _evolveBox.AddChild(box);
            _evolveRows.Add(new EvolveRow(unit, box, label, forms, auto));
        }
    }

    /// <summary>A form's description, then what it gains and gives up against the type it evolves from.</summary>
    private static string FormTooltip(UnitDef from, UnitDef form)
    {
        var lines = new List<string>();
        if (form.Description.Length > 0) lines.Add(form.Description);
        void Change(string what, string a, string b)
        {
            if (a != b) lines.Add($"{what}: {a} to {b}");
        }
        Change("Role", RoleName(from.Role), RoleName(form.Role));
        if (from.Role == UnitRole.Worker && form.Role != UnitRole.Worker) lines.Add("No longer gathers, builds or farms");
        Change("Hit points", $"{from.MaxHp}", $"{form.MaxHp}");
        Change("Damage", $"{from.Damage}", $"{form.Damage}");
        Change("Time between hits", Seconds(from.AttackTicks), Seconds(form.AttackTicks));
        Change("Range", Number(from.Range), Number(form.Range));
        Change("Blast", Number(from.Splash), Number(form.Splash));
        Change("Sight", Number(from.Sight), Number(form.Sight));
        Change("Speed", Number(from.Speed), Number(form.Speed));
        Change("Work speed", $"{from.WorkPercent}%", $"{form.WorkPercent}%");
        if (form.Heal > 0 && (form.Heal != from.Heal || form.HealTicks != from.HealTicks || form.HealRange != from.HealRange))
            lines.Add($"Heals the most hurt ally within {Number(form.HealRange)} blocks by {form.Heal} hit points every {Seconds(form.HealTicks)}");
        foreach (var role in Enum.GetValues<UnitRole>())
            if (form.WeaknessTo(role) != from.WeaknessTo(role))
                lines.Add($"Takes {form.WeaknessTo(role)}% more damage from {RoleName(role)} attacks");
        if (form.Hero && !from.Hero) lines.Add("A hero: you can have only one at a time, and it never breaks and runs");
        if (form.Rally > Fix64.Zero && (form.Steady > 0 || form.Inspire > 1)
            && (form.Rally != from.Rally || form.Steady != from.Steady || form.Inspire != from.Inspire))
            lines.Add($"Steadies your units within {Number(form.Rally)} blocks: they lose {form.Steady}% less morale to deaths " +
                      $"and get {form.Inspire} back a second instead of 1");
        if (form.Rally > Fix64.Zero && form.Volley > 0 && (form.Rally != from.Rally || form.Volley != from.Volley))
            lines.Add($"Calls the volleys: your ranged units within {Number(form.Rally)} blocks deal {form.Volley}% more damage");
        if (form.Rally > Fix64.Zero && form.Spur > 0 && (form.Rally != from.Rally || form.Spur != from.Spur))
            lines.Add($"Leads the charge: your riders within {Number(form.Rally)} blocks move {form.Spur}% faster");
        if (form.Rally > Fix64.Zero && form.Undermine > 0 && (form.Rally != from.Rally || form.Undermine != from.Undermine))
            lines.Add($"Works the breach: your sappers within {Number(form.Rally)} blocks hit buildings and dig {form.Undermine}% faster");
        if (form.Rally > Fix64.Zero && form.Quicken > 0 && (form.Rally != from.Rally || form.Quicken != from.Quicken))
            lines.Add($"Quickens the spells: your casters within {Number(form.Rally)} blocks are ready to cast again {form.Quicken}% sooner");
        if (form.Rally > Fix64.Zero && form.Hasten > 0 && (form.Rally != from.Rally || form.Hasten != from.Hasten))
            lines.Add($"Drives the work: your workers within {Number(form.Rally)} blocks gather, build and farm {form.Hasten}% faster");
        var spells = new List<string>();
        if (form.EarthWall && !from.EarthWall) spells.Add("Earth Wall");
        if (form.IceBridge && !from.IceBridge) spells.Add("Ice Bridge");
        if (form.Chasm && !from.Chasm) spells.Add("Chasm");
        if (form.GrowForest && !from.GrowForest) spells.Add("Grow Forest");
        if (spells.Count > 0) lines.Add("Casts " + string.Join(", ", spells));
        var lost = new List<string>();
        if (from.EarthWall && !form.EarthWall) lost.Add("Earth Wall");
        if (from.IceBridge && !form.IceBridge) lost.Add("Ice Bridge");
        if (from.Chasm && !form.Chasm) lost.Add("Chasm");
        if (from.GrowForest && !form.GrowForest) lost.Add("Grow Forest");
        if (lost.Count > 0) lines.Add("No longer casts " + string.Join(", ", lost));
        return string.Join("\n", lines);
    }

    private static string RoleName(UnitRole role) => role switch
    {
        UnitRole.LineInfantry => "line infantry",
        UnitRole.HeavyInfantry => "heavy infantry",
        _ => role.ToString().ToLowerInvariant(),
    };

    private static string Number(Fix64 value) => value.ToFloat().ToString("0.##", CultureInfo.InvariantCulture);

    private static string Seconds(int ticks) =>
        ((float)ticks / Simulation.TicksPerSecond).ToString("0.##", CultureInfo.InvariantCulture) + " s";

    /// <summary>One train button per unit the building trains (the first also on U), and a repeat switch (P).</summary>
    private void BuildTrainButtons(BuildingDef? def)
    {
        _trainFor = def;
        foreach (var (button, _) in _trainButtons) button.QueueFree();
        _trainButtons.Clear();
        _repeatButton?.QueueFree();
        _repeatButton = null;
        if (def == null) return;
        foreach (var id in def.Trains)
        {
            var unit = _sim.Content.Unit(id);
            var b = MakeToolButton("");
            b.Name = "train_" + id;
            b.Icon = UiIcons.For(unit.Role, UnitRenderer.TeamColors[PlayerControl.LocalPlayer]);
            b.ToggleMode = false;
            b.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
            b.Pressed += () => _control.TrainAtSelected(unit.Id);
            _trainBox.AddChild(b);
            _trainButtons.Add((b, unit));
        }
        _repeatButton = MakeToolButton("Repeat training: off  [P]");
        _repeatButton.Name = "repeat_button";
        _repeatButton.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        _repeatButton.TooltipText = "Each unit it finishes is queued again once you can pay for it";
        _repeatButton.Pressed += () => _control.ToggleRepeat();
        _trainBox.AddChild(_repeatButton);
    }

    /// <summary>
    /// "Keep stocked here": one button per good for the selected storage building's supply rules. A click
    /// steps the amount up (25, 50, 100, 200, then none), a right click steps it down.
    /// </summary>
    private void BuildSupplyRow(VBoxContainer column)
    {
        _supplyBox = new VBoxContainer { Name = "supply_box", Visible = false };
        _supplyBox.AddThemeConstantOverride("separation", 4);
        var title = MakeLabel("Keep stocked here (workers on supply duty fetch it from your other storage):", 13);
        title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        title.CustomMinimumSize = new Vector2(360, 0);
        title.ThemeTypeVariation = UiStyle.Dim;
        _supplyBox.AddChild(title);
        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 4);
        grid.AddThemeConstantOverride("v_separation", 4);
        _supplyBox.AddChild(grid);
        foreach (var good in Goods.All)
        {
            var g = good;
            var button = MakeToolButton("");
            button.Name = "supply_" + g.ToString().ToLowerInvariant();
            button.Icon = UiIcons.For(g);
            button.CustomMinimumSize = new Vector2(118, 0);
            button.TooltipText = $"How much {Goods.Name(g).ToLowerInvariant()} to keep here. Click for more, right click for less. " +
                                 "Automatic workers with supply on their priority list fetch it from your other storage, " +
                                 "or right click this building with workers to put them on supply duty.";
            button.Pressed += () => _control.CycleSupply(g);
            button.GuiInput += e =>
            {
                if (e is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true })
                    _control.CycleSupply(g, back: true);
            };
            grid.AddChild(button);
            _supplyButtons[(int)g] = button;
        }
        column.AddChild(_supplyBox);
    }

    /// <summary>"Governor": Off, Growth, Army and Defence buttons for the selected town hall of yours (see <see cref="GovernCommand"/>).</summary>
    private void BuildGovernorRow(VBoxContainer column)
    {
        _governorBox = new VBoxContainer { Name = "governor_box", Visible = false };
        _governorBox.AddThemeConstantOverride("separation", 4);
        var title = MakeLabel("Governor (runs this town for you; Shift+U steps through):", 13);
        title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        title.CustomMinimumSize = new Vector2(360, 0);
        title.ThemeTypeVariation = UiStyle.Dim;
        _governorBox.AddChild(title);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        _governorBox.AddChild(row);
        foreach (var (focus, label, tip) in new[]
        {
            (GovernorFocus.None, "Off", "You run this town yourself."),
            (GovernorFocus.Growth, "Growth", "Houses and farms as needed, workers up to " + AutomationSystem.WorkerTarget(GovernorFocus.Growth) +
                ", a storehouse and a smelter, and the techs for them."),
            (GovernorFocus.Army, "Army", "Houses and farms as needed, workers up to " + AutomationSystem.WorkerTarget(GovernorFocus.Army) +
                ", a barracks and a steady stream of mixed soldiers, and the techs for them."),
            (GovernorFocus.Defence, "Defence", "Houses and farms as needed, workers up to " + AutomationSystem.WorkerTarget(GovernorFocus.Defence) +
                ", a barracks, a watchtower at each corner of the town, archers, and archery."),
        })
        {
            var f = focus;
            var button = MakeToolButton(label);
            button.Name = "governor_" + f.ToString().ToLowerInvariant();
            button.TooltipText = tip + " The governor makes idle workers in the town automatic and gives only orders you could give.";
            button.Pressed += () => _control.SetGovernor(f);
            row.AddChild(button);
            _governorButtons.Add((button, f));
        }
        _townGuardLabel = MakeLabel("", 13);
        _townGuardLabel.Name = "town_guard";
        _townGuardLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _townGuardLabel.CustomMinimumSize = new Vector2(360, 0);
        _townGuardLabel.ThemeTypeVariation = UiStyle.Dim;
        _governorBox.AddChild(_townGuardLabel);
        column.AddChild(_governorBox);
    }

    /// <summary>Shows the governor buttons for a town hall of yours.</summary>
    private void RefreshGovernor(Building? b)
    {
        bool show = b != null && b.Owner == PlayerControl.LocalPlayer && b.Def.Kind == BuildingKind.TownHall && !b.Planned;
        _governorBox.Visible = show;
        if (!show) return;
        foreach (var (button, focus) in _governorButtons) button.SetPressedNoSignal(b!.Governor == focus);
        int guard = _sim.Combat.TownGuardCount(b!);
        _townGuardLabel.Visible = b.Governor != GovernorFocus.None || guard > 0;
        _townGuardLabel.Text = b.Governor == GovernorFocus.None
            ? $"{guard} soldiers guard this hall; with a governor they defend the whole town"
            : guard == 0
                ? "Town guard: none. Select soldiers and press Shift+V (or Alt+right-click this hall) to hand them to the governor"
                : $"Town guard: {guard} soldier{(guard == 1 ? "" : "s")}, sent after enemies you see within {CombatSystem.TownReach} columns";
    }

    /// <summary>Shows the supply buttons for a finished storage building of yours.</summary>
    private void RefreshSupply(Building? b)
    {
        bool show = b != null && b.Owner == PlayerControl.LocalPlayer && b.Def.Storage && b.Active;
        _supplyBox.Visible = show;
        if (!show) return;
        foreach (var g in Goods.All)
        {
            int want = b!.Supply[(int)g];
            var button = _supplyButtons[(int)g];
            button.Text = want > 0 ? $"{Goods.Name(g)}: {want}" : $"{Goods.Name(g)}: -";
            button.SetPressedNoSignal(want > 0);
        }
    }

    /// <summary>Updates the HUD: buttons, banner and selection box every frame, texts ten times a second.</summary>
    public void Refresh(double delta)
    {
        _pauseButton.SetPressedNoSignal(_clock.Paused);
        _techButton.SetPressedNoSignal(_control.TechTreeOpen);
        _autoButton.SetPressedNoSignal(_sim.Economy.AutoWorkers(PlayerControl.LocalPlayer));
        _rebuildButton.SetPressedNoSignal(_sim.Economy.Rebuilds(PlayerControl.LocalPlayer));
        _techPanel.Visible = _control.TechTreeOpen;
        _prioritiesButton.SetPressedNoSignal(_control.PrioritiesOpen);
        if (_control.PrioritiesOpen && !_prioritiesPanel.Visible)
        {
            _prioritiesPanel.Visible = true;
            _prioritiesPanel.Opened();
        }
        _prioritiesPanel.Visible = _control.PrioritiesOpen;
        for (int i = 0; i < _speedButtons.Count; i++) _speedButtons[i].SetPressedNoSignal(!_clock.Paused && i == _clock.SpeedIndex);
        _pausedBanner.Visible = _clock.Paused;
        _pausedHint.Text = MatchOver ? "The match is over. Space lets it run on." : "Orders can still be given. Space to resume.";
        // Only a player still in a match of two or more can give up; once it is over for them, the button shows the results.
        _surrenderButton.Visible = MatchOver || _sim.Match.Players.Count >= 2 && _sim.Match.InPlay(PlayerControl.LocalPlayer);
        _surrenderButton.Text = MatchOver ? "Results" : "Surrender";
        if (MatchOver) _surrenderPanel.Visible = false;
        _box.Active = _control.Dragging;
        _box.Rect = _control.DragRect;
        _box.QueueRedraw();
        _alerts.Refresh(delta, _clock.Paused);

        _textTimer -= delta;
        if (_textTimer > 0 && !_textDirty) return;
        _textTimer = 0.1;
        _textDirty = false;
        ShowStock();

        long seconds = _sim.Tick / Simulation.TicksPerSecond;
        _clockLabel.Text = $"{seconds / 60:00}:{seconds % 60:00}   {GameClock.Label(_clock.Speed)}";
        float hour = _lighting.Hour;
        var cycle = _lighting.Cycle;
        float trueHour = cycle.TrueHour(_sim.Tick);
        int minutes = (int)(trueHour * 60) / 15 * 15; // a quarter of an hour at a time, so it doesn't flicker
        var weather = cycle.Weather;
        string clock = $"{minutes / 60:00}:{minutes % 60:00}";
        _dayButton.Text = !cycle.On ? "Day" : weather == Weather.Clear ? clock : $"{clock}  {DayCycle.Name(weather)}";
        _dayButton.TooltipText = DescribeConditions(trueHour, weather);
        bool night = DayCycle.IsNight(hour);
        if (night != _dayIsNight)
        {
            _dayIsNight = night;
            _dayButton.Icon = UiIcons.Get(night ? "moon" : "sun");
        }
        _selectionLabel.Text = DescribeSelection();
        _selectionPanel.Visible = _selectionLabel.Text.Length > 0;
        bool one = _control.Selected.Count == 1 && _sim.Buildings.Get(_control.SelectedBuilding) == null && _sim.Cities.Get(_control.SelectedCity) == null;
        _bodyChart.Show(_sim, one ? _control.Selected[0] : 0);
        _descriptionLabel.Text = DescribeUnitType();
        _descriptionLabel.Visible = _descriptionLabel.Text.Length > 0;
        RefreshButtons();
        _toolLabel.Text = DescribeTool();
        _researchLabel.Text = DescribeResearch();
        if (_techPanel.Visible) _techPanel.Refresh();
        if (_prioritiesPanel.Visible) _prioritiesPanel.Refresh();
    }

    private string DescribeResearch()
    {
        int player = PlayerControl.LocalPlayer;
        var tech = _sim.Economy.Researching(player);
        return tech == null ? "" : $"Researching {tech.Name} {100L * _sim.Economy.ResearchTicks(player) / tech.ResearchTicks}%";
    }

    /// <summary>The first tech the player still needs for something, as text ("needs Farming"), or null if none.</summary>
    private string? Needs(IReadOnlyList<TechDef> requires, FactionDef? faction)
    {
        int player = PlayerControl.LocalPlayer;
        if (faction != null && faction != _sim.Economy.Faction(player)) return $"{faction.Name} only";
        return _sim.Economy.MissingTech(player, requires) is { } t ? $"needs {t.Name}" : null;
    }

    /// <summary>The tech that allows roads, if roads need one.</summary>
    private TechDef? RoadTech()
    {
        foreach (var t in _sim.Content.Techs)
        foreach (var e in t.Effects)
            if (e.Type == TechEffectType.Roads) return t;
        return null;
    }

    /// <summary>
    /// Greys out what the player cannot afford or lacks the techs for, and shows train buttons for their own
    /// finished building that trains units.
    /// </summary>
    /// <summary>
    /// The tooltip of the time of day button: the time, the weather and what is coming, and what they do to
    /// sight and aim (see <see cref="Conditions"/>).
    /// </summary>
    private string DescribeConditions(float hour, Weather weather)
    {
        var cycle = _lighting.Cycle;
        var c = _sim.Conditions;
        if (!cycle.On)
            return (cycle.AlwaysDay ? "Always day." : "Day and night are off for this match: it is always a clear day.") +
                   " Units see as far as usual and every shot lands.";
        var sb = new System.Text.StringBuilder();
        sb.Append($"Day {cycle.Day(_sim.Tick)}, {DayCycle.PartOf(hour).ToLowerInvariant()}, {DayCycle.Name(weather).ToLowerInvariant()}.");
        if (c.Next != c.Weather)
        {
            float hours = (c.ChangesAt - _sim.Tick) / (float)Conditions.HourTicks;
            sb.Append($" {DayCycle.Name(c.Next)} coming in about {Mathf.Max(1, Mathf.RoundToInt(hours))} h.");
        }
        int dark = c.SightPercent(-1, -1, false), lit = c.SightPercent(-1, -1, true);
        sb.Append($"\nUnits see {dark}% as far as on a clear day");
        if (lit > dark) sb.Append($", and {lit}% into the light of lanterns, torches, campfires and lava");
        sb.Append('.');
        if (c.Part != DayPart.Day && c.Weather == Weather.Storm) sb.Append(" The storm has put out torches and campfires.");
        int hit = c.HitChance(-1, -1);
        sb.Append(hit < 100 ? $"\nArchers and mages hit {hit}% of their shots at targets out of the light." : "\nEvery shot lands.");
        sb.Append("\nIn the swamp, mist cuts sight by a quarter at night and in the morning.");
        sb.Append(cycle.AlwaysDay ? "\nAlways day (the look only): click to show the real time of day."
            : "\nClick to keep the world lit as by day (the look only: units still see as the time and weather allow).");
        return sb.ToString();
    }

    private void RefreshButtons()
    {
        var econ = _sim.Economy;
        int player = PlayerControl.LocalPlayer;
        foreach (var (button, def, key) in _buildButtons)
        {
            string? needs;
            if (def is { Kind: BuildingKind.Wall or BuildingKind.Bridge })
            {
                needs = Needs(def.Requires, def.Faction);
                button.Text = $"{def.Name}  [{KeyText(key)}]\n{needs ?? CostText(def.Cost) + " a column"}";
                button.Disabled = needs != null || !econ.CanAfford(player, def.Cost);
                button.SetPressedNoSignal(_control.Tool == (def.IsBridge ? PlayerControl.ToolMode.Bridge : PlayerControl.ToolMode.Wall));
                continue;
            }
            if (def != null)
            {
                needs = Needs(def.Requires, def.Faction);
                button.Text = $"{def.Name}{(key == Key.None ? "" : $"  [{KeyText(key)}]")}\n{needs ?? CostText(def.Cost)}";
                // What you cannot afford yet can still be placed, as a plan.
                button.Disabled = needs != null;
            }
            else
            {
                needs = econ.CanLayRoads(player) ? null : $"needs {RoadTech()?.Name}";
                button.Text = $"Road  [{key}]\n{needs ?? $"{EconomySystem.StonePerRoad} stone a block"}";
                button.Disabled = needs != null || econ.Stock(player, Good.Stone) < EconomySystem.StonePerRoad;
            }
            button.SetPressedNoSignal(def != null ? _control.BuildDef == def : _control.Tool == PlayerControl.ToolMode.Road);
        }
        _planModeButton.SetPressedNoSignal(_control.PlanMode);
        _planModeButton.Text = _control.PlanMode ? "Plan mode: on  [Tab]" : "Plan mode: off  [Tab]";
        var b = _sim.Buildings.Get(_control.SelectedBuilding);
        bool plan = b is { Planned: true } && b.Owner == player;
        _planRow.Visible = plan;
        if (plan) _startPlanButton.Disabled = econ.CanStart(b!) != PlaceResult.Ok;
        RefreshUpgrade(b);
        RefreshSupply(b);
        RefreshGovernor(b);
        bool trains = b != null && b.Owner == player && b.Active && b.Def.Trains.Length > 0;
        var trainFor = trains ? b!.Def : null;
        if (trainFor != _trainFor) BuildTrainButtons(trainFor);
        if (_repeatButton != null)
        {
            bool on = b?.Repeat == true;
            _repeatButton.SetPressedNoSignal(on);
            _repeatButton.Text = on ? "Repeat training: on  [P]" : "Repeat training: off  [P]";
        }
        for (int k = 0; k < _trainButtons.Count; k++)
        {
            var (button, unit) = _trainButtons[k];
            string? needs = Needs(unit.Requires, unit.Faction);
            string key = k == 0 ? "  [U]" : "";
            button.Text = needs != null ? $"{unit.Name}: {needs}" : $"Train {unit.Name.ToLowerInvariant()}{key}   {CostText(unit.Cost)}";
            button.Disabled = needs != null || !econ.CanAfford(player, unit.Cost) || b!.Queue.Count >= EconomySystem.MaxQueue;
        }
        _digButton.Visible = _control.OwnDiggers().Count > 0 || _control.Tool == PlayerControl.ToolMode.Dig;
        _digButton.SetPressedNoSignal(_control.Tool == PlayerControl.ToolMode.Dig);
        _workerPrioritiesButton.Visible = _control.OwnWorkers().Count > 0;
        _defendTownButton.Visible = _control.TownToDefend() != null;
        RefreshEvolveRows();
        RefreshTaming();
        RefreshDen();
        RefreshRuin();
        RefreshBloom();
        RefreshEarthWall();
        RefreshIceBridge();
        RefreshChasm();
        RefreshGrowForest();
        RefreshCities();
        RefreshSites();
    }

    /// <summary>A build key as it is written on its button.</summary>
    private static string KeyText(Key key) => key switch
    {
        Key.Semicolon => ";",
        Key.Apostrophe => "'",
        Key.Slash => "/",
        Key.Backslash => "\\",
        _ => key.ToString(),
    };

    /// <summary>
    /// The upgrade buttons while a wall or gate of yours that has a tier above it is selected: the whole
    /// stretch it stands in (as far as the goods go), or it alone. Greyed out while you lack the tech or the
    /// goods for one column, or it is being upgraded already. A stone wall that has lost some of its facing
    /// has the button too: it puts the facing back.
    /// </summary>
    private void RefreshUpgrade(Building? b)
    {
        bool own = b != null && b.Owner == PlayerControl.LocalPlayer && b.Active;
        var to = own ? b!.Def.Upgrade : null;
        if (own && to == null && b!.Def.Facing is { } facing && b.Upgrade == null)
        {
            int missing = _sim.Economy.MissingFacings(b).Count;
            _upgradeRow.Visible = missing > 0;
            if (missing == 0) return;
            _upgradeButton.Text = $"Put the facing back  [U]   {CostText(facing.Cost)} a column";
            _upgradeButton.TooltipText =
                $"This stretch of {Plural(b.Def.Name).ToLowerInvariant()} has lost some of the facing that makes it three columns thick " +
                $"({missing} column{(missing == 1 ? "" : "s")} beside this one). Workers put it up again, as far as your goods go.";
            _upgradeButton.Disabled = _sim.Economy.CanUpgrade(b) != PlaceResult.Ok;
            _upgradeOneButton.Visible = true;
            _upgradeOneButton.Disabled = _upgradeButton.Disabled;
            return;
        }
        _upgradeRow.Visible = to != null;
        if (to == null) return;
        if (!b!.Def.IsWall)
        {
            RefreshGrow(b, to);
            return;
        }
        var result = _sim.Economy.CanUpgrade(b!);
        string? needs = Needs(to.Requires, to.Faction);
        int stretch = b!.Upgrade != null ? 0 : _sim.Economy.Stretch(b).Count;
        _upgradeButton.Text = b.Upgrade != null ? $"Becoming a {to.Name.ToLowerInvariant()}: {100L * b.UpgradeWork / Math.Max(1, to.BuildTicks)}%"
            : needs != null ? $"{to.Name}: {needs}"
            : $"Upgrade to {to.Name.ToLowerInvariant()}  [U]   {CostText(to.Cost)} a column";
        _upgradeButton.TooltipText =
            $"Turns this stretch of {Plural(b.Def.Name).ToLowerInvariant()} ({stretch} joined up here) into {Plural(to.Name).ToLowerInvariant()}, " +
            "nearest first, as far as your goods go. Workers do the work (automatic ones by themselves; or right click the wall with workers), " +
            "and the wall stands as it is until they are done." + (to.Walkway && !b.Def.Walkway ? " Archers can be posted on top of the new wall." : "") +
            (to.Facing != null ? $" A {to.Name.ToLowerInvariant()} is faced on both sides when it is done, at no further cost, so it is three columns thick." : "");
        _upgradeButton.Disabled = result != PlaceResult.Ok;
        _upgradeOneButton.Visible = b.Upgrade == null && needs == null;
        _upgradeOneButton.Disabled = result != PlaceResult.Ok;
    }

    /// <summary>
    /// The upgrade button for a house, hall or other building that has a bigger one to become: what it becomes,
    /// its price and what it does better, greyed out without the tech, the goods or room round it to grow.
    /// </summary>
    private void RefreshGrow(Building b, BuildingDef to)
    {
        var result = _sim.Economy.CanUpgrade(b);
        string? needs = Needs(to.Requires, to.Faction);
        string key = b.Def.Trains.Length > 0 ? "Ctrl+U" : "U";
        _upgradeButton.Text = b.Upgrade != null
            ? $"Becoming a {to.Name.ToLowerInvariant()}: {100L * Math.Min(b.UpgradeWork, to.BuildTicks) / Math.Max(1, to.BuildTicks)}%"
            : needs != null ? $"{to.Name}: {needs}"
            : result == PlaceResult.Blocked ? $"{to.Name}: no room round it to grow"
            : $"Upgrade to {to.Name.ToLowerInvariant()}  [{key}]   {CostText(to.Cost)}";
        var better = new List<string>();
        int housing = to.Housing - b.Def.Housing;
        if (housing > 0) better.Add($"houses {housing} more");
        if (to.Speed > b.Def.Speed) better.Add($"{(to.Kind == BuildingKind.Smelter ? "smelts" : to.Kind == BuildingKind.Shrine ? "channels" : "trains")} at {to.Speed}% of the usual pace");
        if (to.WorkerSlots > b.Def.WorkerSlots) better.Add($"takes {to.WorkerSlots} workers");
        if (to.Sight > b.Def.Sight) better.Add($"sees {to.Sight} columns round it");
        if (b.Def.Raidable && !to.Raidable) better.Add("cannot be captured or burned");
        if (to.Sturdy > b.Def.Sturdy) better.Add("is sturdier");
        _upgradeButton.TooltipText =
            $"Turns this {b.Def.Name.ToLowerInvariant()} into a {to.Name.ToLowerInvariant()}, {to.SizeX} by {to.SizeZ} columns: " +
            $"it grows from its middle onto the level, free ground round it. A {to.Name.ToLowerInvariant()} " +
            (better.Count > 0 ? string.Join(", ", better) : "is bigger") +
            ". Workers do the work (automatic ones by themselves; or right click it with workers), and it keeps working as it is until they are done.";
        _upgradeButton.Disabled = result != PlaceResult.Ok;
        _upgradeOneButton.Visible = false;
    }

    /// <summary>
    /// The town buttons in the top bar (who holds the town), and the selected town's hire buttons, greyed out
    /// when you do not hold it, lack the goods, have no room or nobody is ready.
    /// </summary>
    private void RefreshCities()
    {
        var econ = _sim.Economy;
        int player = PlayerControl.LocalPlayer;
        foreach (var (button, town) in _cityButtons)
        {
            // The race alone (the id, "Goblins"), so three towns' buttons fit in the top bar; the tooltip has the name.
            button.Text = $"{char.ToUpperInvariant(town.Def.Id[0])}{town.Def.Id[1..]}: {HolderText(town, player)}";
            button.SetPressedNoSignal(_control.SelectedCity == town.Id);
        }
        var selected = _sim.Cities.Get(_control.SelectedCity);
        if (selected?.Def != _cityFor) BuildCityButtons(selected?.Def);
        _cityBox.Visible = selected != null;
        if (selected == null) return;
        bool yours = selected.Holder == player, room = _sim.Cities.CanHire(player);
        foreach (var (button, unit) in _hireButtons)
        {
            button.Text = !yours ? $"{unit.Name}: break the totem first" : $"Hire {unit.Name.ToLowerInvariant()}   {CostText(unit.Cost)}";
            button.Disabled = !yours || selected.Ready == 0 || !room || !econ.CanAfford(player, unit.Cost);
        }
    }

    /// <summary>
    /// The ancient sites button: how many you hold of how many, or, while someone's dominion clock runs, who wins in
    /// how long. Its tooltip says what everyone holds and how dominion works.
    /// </summary>
    private void RefreshSites()
    {
        var sites = _sim.Sites;
        _sitesButton.Visible = sites.Active;
        if (!sites.Active) return;
        int me = PlayerControl.LocalPlayer, count = _sim.Ruins.Ruins.Count;
        if (sites.Leader >= 0)
        {
            int left = (sites.TicksLeft + Simulation.TicksPerSecond - 1) / Simulation.TicksPerSecond;
            string who = sites.Leader == me ? "You win" : $"{_sim.Economy.Player(sites.Leader).Faction.Name} win";
            _sitesButton.Text = $"{who} in {left / 60}:{left % 60:00}";
        }
        else _sitesButton.Text = $"Sites {sites.Held(me)}/{count}";
        if (_sitesFramedFor != sites.Leader)
        {
            _sitesFramedFor = sites.Leader;
            Frame(_sitesButton, sites.Leader < 0 ? PlainColour
                : sites.Leader == me ? MatchEndPanel.VictoryColor.Darkened(0.45f) : MatchEndPanel.DefeatColor.Darkened(0.45f));
        }
        var held = new StringBuilder();
        foreach (int p in _sim.Match.Players)
            held.Append($"\n{(p == me ? "You" : _sim.Economy.Player(p).Faction.Name)}: {sites.Held(p)}");
        _sitesButton.TooltipText =
            $"Ancient sites: the {count} old ruins. Soldiers standing alone inside one for {DominionSystem.ClaimTicks / Simulation.TicksPerSecond} s take it, " +
            $"and it stays yours until a rival does the same. Hold {sites.Needed} of them for {DominionSystem.DominionTicks / (60 * Simulation.TicksPerSecond)} minutes and you win." +
            $"\nHeld:{held}\nClick to look at the next site.";
    }

    /// <summary>Who holds the town, as the player sees it: neutral, yours, or another player's.</summary>
    private string HolderText(CityState town, int player) =>
        town.Holder == player ? "yours" : town.Holder >= 0 ? $"held by {PlayerName(town.Holder)}" : "neutral";

    /// <summary>
    /// The Beaten button while you have beaten monsters, and the Ride and Recruit buttons while some are
    /// selected. Ride is greyed out when none of them has a unit near that could climb on.
    /// </summary>
    private void RefreshTaming()
    {
        int waiting = _control.AllBeaten().Count;
        _beatenButton.Visible = waiting > 0;
        _beatenButton.Text = $"Beaten: {waiting}";
        var beaten = _control.OwnBeaten();
        _tameRow.Visible = beaten.Count > 0;
        if (beaten.Count == 0) return;
        var u = _sim.Units;
        var first = _sim.Content.Units[u.Def[u.IndexOf(beaten[0])]];
        string name = (beaten.Count == 1 ? first.Name : Plural(first.Name)).ToLowerInvariant();
        _tameLabel.Text = $"{beaten.Count} beaten {name}:";
        int canRide = 0;
        foreach (int id in beaten)
            if (_sim.Monsters.RiderFor(u.IndexOf(id)) >= 0) canRide++;
        if (first.Ride is { } ride)
        {
            string rider = ride.Rider.Name.ToLowerInvariant();
            _rideButton.Text = canRide > 0 ? $"Ride: {ride.Becomes.Name.ToLowerInvariant()}" : $"Ride: needs a {rider} near";
            _rideButton.TooltipText = $"The nearest {rider} of yours within {MonsterSystem.RideRange} blocks climbs on and becomes a " +
                                      $"{ride.Becomes.Name.ToLowerInvariant()}, keeping its share of health. The {first.Name.ToLowerInvariant()} is gone.\n" +
                                      ride.Becomes.Description;
        }
        else
        {
            _rideButton.Text = "Ride: nobody can";
            _rideButton.TooltipText = $"Nobody rides a {first.Name.ToLowerInvariant()}.";
        }
        _rideButton.Disabled = canRide == 0;
    }

    /// <summary>
    /// The den button while a cleared den your faction can use is near your selected units: for the Orcs, a
    /// wolf lodge put up on it (greyed out while you cannot pay); for a form, the unit that takes it.
    /// </summary>
    /// <summary>The Dusk Bloom button while the Dusk Staff's bearer is selected (greyed out until it can bloom again).</summary>
    private void RefreshBloom()
    {
        int id = _control.StaffSelected();
        _bloomButton.Visible = id != 0;
        if (id == 0) return;
        var tree = _sim.DuskTree;
        bool ready = tree.CanBloom(_sim.Units.IndexOf(id));
        long wait = Math.Max(0, tree.BloomReady - _sim.Tick);
        _bloomButton.Text = ready ? "Dusk Bloom [,]" : $"Dusk Bloom in {(wait + Simulation.TicksPerSecond - 1) / Simulation.TicksPerSecond} s";
        _bloomButton.TooltipText = $"The Dusk Staff's bearer heals every ally within {DuskTreeSystem.BloomRange} blocks, itself too, " +
                                   $"by {DuskTreeSystem.BloomHeal}. Then it waits {DuskTreeSystem.BloomCooldownTicks / Simulation.TicksPerSecond} s.";
        _bloomButton.Disabled = !ready;
    }

    /// <summary>The Earth Wall button while a mage of yours is selected (greyed out while none is ready).</summary>
    private void RefreshEarthWall()
    {
        bool tool = _control.Tool == PlayerControl.ToolMode.EarthWall;
        _earthWallButton.Visible = _control.OwnCasters().Count > 0 || tool;
        _earthWallButton.SetPressedNoSignal(tool);
        if (!_earthWallButton.Visible) return;
        int ready = _control.ReadyCaster();
        int next = ready != 0 ? ready : _control.ReadyCaster(soonest: true);
        long wait = next == 0 ? 0 : Math.Max(0, _sim.Spells.ReadyAt(_sim.Units.IndexOf(next)) - _sim.Tick);
        _earthWallButton.Text = ready != 0 || next == 0 ? "Earth Wall  [Shift+Y]"
            : $"Earth Wall in {(wait + Simulation.TicksPerSecond - 1) / Simulation.TicksPerSecond} s";
        _earthWallButton.TooltipText = $"A mage raises a wall of earth {SpellSystem.Height} blocks high along a line of up to " +
                                       $"{SpellSystem.MaxLength} columns, within {SpellSystem.CastRange} blocks of it. " +
                                       $"It crumbles after {SpellSystem.LastsTicks / Simulation.TicksPerSecond} s; workers can dig through it before then. " +
                                       $"Then that mage waits {SpellSystem.CooldownTicks / Simulation.TicksPerSecond} s. " +
                                       $"Up to {SpellSystem.MaxGroup} ready mages selected together cast as one: each extra mage makes the wall " +
                                       $"a column thicker and {SpellSystem.WallPerHelper} columns longer.";
        _earthWallButton.Disabled = ready == 0 && !tool;
        ManaGate(_earthWallButton, Spell.EarthWall, "Earth Wall", tool);
    }

    /// <summary>The Ice Bridge button while a mage of yours is selected (greyed out while none is ready).</summary>
    private void RefreshIceBridge()
    {
        bool tool = _control.Tool == PlayerControl.ToolMode.IceBridge;
        _iceBridgeButton.Visible = _control.OwnCasters(Spell.IceBridge).Count > 0 || tool;
        _iceBridgeButton.SetPressedNoSignal(tool);
        if (!_iceBridgeButton.Visible) return;
        int ready = _control.ReadyCaster(spell: Spell.IceBridge);
        int next = ready != 0 ? ready : _control.ReadyCaster(soonest: true, spell: Spell.IceBridge);
        long wait = next == 0 ? 0 : Math.Max(0, _sim.Spells.ReadyAt(_sim.Units.IndexOf(next)) - _sim.Tick);
        _iceBridgeButton.Text = ready != 0 || next == 0 ? "Ice Bridge  [Shift+']"
            : $"Ice Bridge in {(wait + Simulation.TicksPerSecond - 1) / Simulation.TicksPerSecond} s";
        _iceBridgeButton.TooltipText = $"A mage freezes the water into a level way of ice from bank to bank, up to {SpellSystem.IceLength} columns long " +
                                       $"and {SpellSystem.IceWidth} wide, within {SpellSystem.CastRange} blocks of it, that anyone can walk on, slipping and sliding. " +
                                       $"It cracks, then melts after {SpellSystem.IceLastsTicks / Simulation.TicksPerSecond} s: " +
                                       "whoever is still on it falls, and drowns in deep water. " +
                                       $"Then that mage waits {SpellSystem.CooldownTicks / Simulation.TicksPerSecond} s before its next spell. " +
                                       $"Up to {SpellSystem.MaxGroup} ready mages selected together cast as one: each extra mage makes the bridge " +
                                       $"{SpellSystem.IceWidthPerHelper} columns wider.";
        _iceBridgeButton.Disabled = ready == 0 && !tool;
        ManaGate(_iceBridgeButton, Spell.IceBridge, "Ice Bridge", tool);
    }

    /// <summary>The Chasm button while a mage of yours is selected (greyed out while none is ready).</summary>
    private void RefreshChasm()
    {
        bool tool = _control.Tool == PlayerControl.ToolMode.Chasm;
        _chasmButton.Visible = _control.OwnCasters(Spell.Chasm).Count > 0 || tool;
        _chasmButton.SetPressedNoSignal(tool);
        if (!_chasmButton.Visible) return;
        int ready = _control.ReadyCaster(spell: Spell.Chasm);
        int next = ready != 0 ? ready : _control.ReadyCaster(soonest: true, spell: Spell.Chasm);
        long wait = next == 0 ? 0 : Math.Max(0, _sim.Spells.ReadyAt(_sim.Units.IndexOf(next)) - _sim.Tick);
        _chasmButton.Text = ready != 0 || next == 0 ? "Chasm  [Shift+Z]"
            : $"Chasm in {(wait + Simulation.TicksPerSecond - 1) / Simulation.TicksPerSecond} s";
        _chasmButton.TooltipText = $"A mage opens the ground {SpellSystem.ChasmDepth} blocks deep along a line of up to " +
                                   $"{SpellSystem.MaxLength} columns, within {SpellSystem.CastRange} blocks of it: too deep to climb out of. " +
                                   $"Whoever stands there falls in and takes {SpellSystem.FallDamage} damage. " +
                                   $"It closes after {SpellSystem.ChasmLastsTicks / Simulation.TicksPerSecond} s and lifts them out. " +
                                   "Not under buildings, nor beside water. " +
                                   $"Then that mage waits {SpellSystem.CooldownTicks / Simulation.TicksPerSecond} s before its next spell. " +
                                   $"Up to {SpellSystem.MaxGroup} ready mages selected together cast as one: each extra mage makes the chasm " +
                                   $"a column wider and {SpellSystem.WallPerHelper} columns longer.";
        _chasmButton.Disabled = ready == 0 && !tool;
        ManaGate(_chasmButton, Spell.Chasm, "Chasm", tool);
    }

    /// <summary>The Grow Forest button while a mage of yours is selected (greyed out while none is ready).</summary>
    private void RefreshGrowForest()
    {
        bool tool = _control.Tool == PlayerControl.ToolMode.GrowForest;
        _growForestButton.Visible = _control.OwnCasters(Spell.GrowForest).Count > 0 || tool;
        _growForestButton.SetPressedNoSignal(tool);
        if (!_growForestButton.Visible) return;
        int ready = _control.ReadyCaster(spell: Spell.GrowForest);
        int next = ready != 0 ? ready : _control.ReadyCaster(soonest: true, spell: Spell.GrowForest);
        long wait = next == 0 ? 0 : Math.Max(0, _sim.Spells.ReadyAt(_sim.Units.IndexOf(next)) - _sim.Tick);
        _growForestButton.Text = ready != 0 || next == 0 ? "Grow Forest  [Shift+L]"
            : $"Grow Forest in {(wait + Simulation.TicksPerSecond - 1) / Simulation.TicksPerSecond} s";
        _growForestButton.TooltipText = $"A mage grows full-grown trees along a line of up to {SpellSystem.MaxLength} columns, " +
                                        $"within {SpellSystem.CastRange} blocks of it: a tree on every other spot, with a gap between each two. " +
                                        "Trunks stop arrows and block the way, so the grove is cover for archers and a screen an army must file through. " +
                                        "The trees stay, and woodcutters can fell them for wood. Not on buildings, water or units. " +
                                        $"Then that mage waits {SpellSystem.CooldownTicks / Simulation.TicksPerSecond} s before its next spell. " +
                                        $"Up to {SpellSystem.MaxGroup} ready mages selected together cast as one: each extra mage adds a row of trees " +
                                        "on the gaps of the first and makes it " + $"{SpellSystem.WallPerHelper} columns longer.";
        _growForestButton.Disabled = ready == 0 && !tool;
        ManaGate(_growForestButton, Spell.GrowForest, "Grow Forest", tool);
    }

    /// <summary>
    /// A spell button says what the spell costs, and is greyed out while the local player has too little mana
    /// for one mage to cast it.
    /// </summary>
    private void ManaGate(Button button, Spell spell, string name, bool tool)
    {
        int cost = SpellSystem.ManaCost(spell);
        button.TooltipText += $" Each mage that casts it pays {cost} mana; shrines channel mana from crystal dug in the caverns.";
        if (tool || _sim.Economy.Player(PlayerControl.LocalPlayer).Mana >= cost) return;
        button.Text = $"{name}: needs {cost} mana";
        button.Disabled = true;
    }

    private void RefreshRuin()
    {
        var ruin = _control.NearRuin();
        _ruinButton.Visible = ruin != null;
        if (ruin == null) return;
        int done = _sim.Ruins.Progress(PlayerControl.LocalPlayer, ruin.Index) * 100 / RuinSystem.SearchTicks;
        _ruinButton.Text = done > 0 ? $"Search the old ruin ({done}% searched)" : "Search the old ruin";
        _ruinButton.TooltipText = "Your selected units walk into the old ruin and search it for what its builders left behind: " +
                                  $"{RuinSystem.SearchTicks / Simulation.TicksPerSecond} s for one, faster with up to {RuinSystem.MaxHands} together. " +
                                  "The first side to finish gets it, once a match. Right click on the ruin does the same.";
    }

    private void RefreshDen()
    {
        int player = PlayerControl.LocalPlayer;
        var lair = _control.UsableDen();
        var use = lair != null ? _sim.Monsters.DenUse(player, lair) : null;
        _denButton.Visible = lair != null && use != null;
        if (lair == null || use == null) return;
        string den = $"{lair.Monster.Name.ToLowerInvariant()} den";
        if (use.Building is { } def)
        {
            bool afford = _sim.Economy.CanAfford(player, def.Cost);
            _denButton.Text = $"Make the {den} a {def.Name.ToLowerInvariant()}   {CostText(def.Cost)}";
            _denButton.Icon = UiIcons.Get(def.Icon.Length > 0 ? def.Icon : def.Id);
            string yield = def.YieldTicks > 0
                ? $" Brings in {CostText(def.Yield)} every {def.YieldTicks / Simulation.TicksPerSecond} s once built."
                : "";
            _denButton.TooltipText = $"Your selected workers build a {def.Name.ToLowerInvariant()} on the cleared {den}, which is gone. " +
                                     $"It houses {def.Housing}.{yield}";
            _denButton.Disabled = !afford;
        }
        else if (use.Form is { } form)
        {
            _denButton.Text = $"Make a {form.EvolvesFrom!.Name.ToLowerInvariant()} a {form.Name.ToLowerInvariant()} at the {den}";
            _denButton.Icon = null;
            _denButton.TooltipText = $"The selected {form.EvolvesFrom!.Name.ToLowerInvariant()} nearest the den becomes a " +
                                     $"{form.Name.ToLowerInvariant()}, a form it can get nowhere else. The den is gone.\n{form.Description}";
            _denButton.Disabled = false;
        }
        else if (use.Relic is { } relic)
        {
            _denButton.Text = $"Take up the {relic.Name.ToLowerInvariant()} at the {den}";
            _denButton.Icon = UiIcons.Get(relic.Icon);
            _denButton.TooltipText = $"The selected fighting unit nearest the den, without a relic already, takes up the {relic.Name.ToLowerInvariant()}" +
                                     $"{RelicText(relic)}. It keeps it until it dies, then drops it where it falls for any fighting unit to pick up. The den is gone.\n{relic.Description}";
            _denButton.Disabled = false;
        }
    }

    /// <summary>What a relic gives its bearer, as ": +60 hit points, +2 armour", or "" for nothing.</summary>
    private static string RelicText(Rts.Sim.Content.RelicDef relic)
    {
        var parts = new List<string>();
        if (relic.Hp > 0) parts.Add($"+{relic.Hp} hit points");
        if (relic.Damage > 0) parts.Add($"+{relic.Damage} damage");
        if (relic.Armour > 0) parts.Add($"+{relic.Armour} armour");
        return parts.Count > 0 ? ": " + string.Join(", ", parts) : "";
    }

    /// <summary>
    /// The evolve rows, one for each type of selected unit of yours that has forms, if one of them can
    /// evolve or the type has a form it takes on its own: how many there are and how many are ready (or
    /// the experience the most seasoned one has and needs), a button per form that is greyed out until one
    /// of them can take it, and the form the type takes on its own.
    /// </summary>
    private void RefreshEvolveRows()
    {
        int player = PlayerControl.LocalPlayer;
        var u = _sim.Units;
        var evolution = _sim.Evolution;
        var types = new List<UnitDef>();
        if (evolution.Rules(player) != null)
        {
            foreach (int id in _control.Selected)
            {
                int i = u.IndexOf(id);
                var def = _sim.Content.Units[u.Def[i]];
                if (u.Owner[i] != player || def.Forms.Count == 0 || types.Contains(def)) continue;
                if (evolution.CanEvolve(i) || evolution.AutoForm(player, def) != null || evolution.WaitsForHero(i)) types.Add(def);
            }
            types.Sort((a, b) => a.Index.CompareTo(b.Index));
        }
        var key = new StringBuilder();
        foreach (var t in types) key.Append(t.Index).Append(',');
        if (key.ToString() != _evolveKey) BuildEvolveRows(types, key.ToString());

        foreach (var row in _evolveRows)
        {
            int count = 0, ready = 0, best = -1;
            var canTake = new bool[row.Forms.Count];
            foreach (int id in _control.Selected)
            {
                int i = u.IndexOf(id);
                if (u.Owner[i] != player || u.Def[i] != row.Unit.Index) continue;
                count++;
                if (best < 0 || u.Xp[i] > u.Xp[best]) best = i;
                bool any = false;
                for (int k = 0; k < row.Forms.Count; k++)
                    if (evolution.CanEvolve(i, row.Forms[k].Form)) any = canTake[k] = true;
                if (any) ready++;
            }
            string name = (count == 1 ? row.Unit.Name : Plural(row.Unit.Name)).ToLowerInvariant();
            int next = best >= 0 ? evolution.NextLevelXp(best) : -1;
            string state = ready > 0 ? $"{ready} ready" : best < 0 ? "" : next > 0 ? $"{u.Xp[best]}/{next} xp" : $"{u.Xp[best]} xp";
            row.Label.Text = $"{count} {name}, {state}:";
            for (int k = 0; k < row.Forms.Count; k++)
            {
                var (button, form) = row.Forms[k];
                string? needs = Needs(form.Requires, form.Faction);
                // A hero is one of a kind: none rises while yours lives.
                if (needs == null && form.Hero && evolution.HasHero(player, form)) needs = "you have one";
                button.Text = needs != null ? $"{form.Name}: {needs}" : form.Name;
                button.Disabled = !canTake[k];
            }
            var auto = evolution.AutoForm(player, row.Unit);
            row.Auto.Text = auto != null ? $"Auto: {auto.Name}" : "Auto: off";
            row.Auto.SetPressedNoSignal(auto != null);
        }
    }

    private string DescribeTool()
    {
        switch (_control.Tool)
        {
            case PlayerControl.ToolMode.Terrain:
                return "Terrain tool: left click digs, right click places stone (T or Esc to leave)";
            case PlayerControl.ToolMode.Test:
                return $"Test mode: left click to place {_control.TestPlacing}\nRight click or Esc to stop";
            case PlayerControl.ToolMode.Build:
            {
                var ghost = _control.Ghost;
                string why = !ghost.Visible ? "" : ghost.Result switch
                {
                    PlaceResult.Ok => "",
                    PlaceResult.Crowded => "   (too close to another building)",
                    PlaceResult.Uneven => "   (the ground is too steep)",
                    PlaceResult.Blocked => "   (something is in the way)",
                    PlaceResult.NoWater => "   (it must stand right beside water)",
                    PlaceResult.CannotAfford => "   (not enough goods)",
                    PlaceResult.Locked when _control.BuildDef is { } def => $"   ({Needs(def.Requires, def.Faction)})",
                    _ => "   (off the map)",
                };
                if (_control.Planning && ghost.Result is PlaceResult.Ok or PlaceResult.CannotAfford)
                    return $"Planning a {_control.BuildDef?.Name.ToLowerInvariant()}: left click marks it out, to start later by right clicking it" +
                           (ghost.Result == PlaceResult.CannotAfford ? "   (not enough goods yet)" : "") +
                           "\nShift+click to plan more, Tab for plan mode, right click or Esc to stop";
                return $"Placing a {_control.BuildDef?.Name.ToLowerInvariant()}: left click builds it with the selected workers{why}\n" +
                       "Shift+click to place more, Tab to plan instead, right click or Esc to stop";
            }
            case PlayerControl.ToolMode.Tunnel:
                return "Tunnel: left click where the selected sappers dig to (on a building: under it, to bring it down)" +
                       (_control.OwnSappers().Count == 0 ? "   (select sappers first)" : "") +
                       "\nShift+click to dig another, right click or Esc to stop";
            case PlayerControl.ToolMode.Dig:
                return (_control.DigStarted
                           ? $"Dig: let go to mark out {_control.DigPreview.Count} columns, dug {_control.DigDepth} deep"
                           : $"Dig: drag out an area for the selected workers and sappers to dig, {_control.DigDepth} deep") +
                       (_control.OwnDiggers().Count == 0 ? "   (select workers or sappers first)" : "") +
                       "\n[ and ]: shallower or deeper   Shift: mark out another   Right click or Esc to stop";
            case PlayerControl.ToolMode.Wall:
            {
                var preview = _control.WallPreview;
                int ok = 0;
                foreach (var c in preview)
                    if (c.Ok) ok++;
                return (_control.WallStarted
                           ? $"Wall: let go to wall off {ok} columns" + (ok < preview.Count ? $" ({preview.Count - ok} have no room or goods)" : "")
                           : "Wall: drag a line for the selected workers to wall off") +
                       (_control.OwnWorkers().Count == 0 ? "   (select workers first)" : "") +
                       "\nShift: draw another   Right click or Esc to stop   (O: a gate, which only your units walk through)";
            }
            case PlayerControl.ToolMode.EarthWall:
            {
                var preview = _control.WallPreview;
                int ok = 0;
                foreach (var c in preview)
                    if (c.Ok) ok++;
                return (_control.WallStarted
                           ? $"Earth Wall: let go to raise earth on {ok} columns" + (ok < preview.Count ? $" ({preview.Count - ok} have a unit, a building or water on them)" : "")
                           : $"Earth Wall: drag a line of up to {SpellSystem.WallLength(_control.GroupCasters())} columns for a mage to raise earth along") +
                       (_control.GroupCasters() > 1 ? $"   ({_control.GroupCasters()} mages together: {SpellSystem.WallThickness(_control.GroupCasters())} thick)" : "") +
                       (_control.OwnCasters().Count == 0 ? "   (select a mage first)"
                           : _control.ReadyCaster() == 0 ? "   (no selected mage is ready)" : "") +
                       "\nRight click or Esc to stop";
            }
            case PlayerControl.ToolMode.IceBridge:
            {
                var preview = _control.WallPreview;
                int ok = 0;
                foreach (var c in preview)
                    if (c.Ok) ok++;
                return (!_control.WallStarted
                           ? $"Ice Bridge: drag from bank to bank, across water, for a mage to freeze a way over (up to {SpellSystem.IceLength} columns)" +
                             (_control.GroupCasters(Spell.IceBridge) > 1 ? $"   ({_control.GroupCasters(Spell.IceBridge)} mages together: {SpellSystem.IceWidthFor(_control.GroupCasters(Spell.IceBridge))} wide)" : "")
                           : preview.Count == 0
                               ? "Ice Bridge: both ends must be ground about level with each other, with water between"
                               : $"Ice Bridge: let go to freeze {ok} columns of ice" + (ok < preview.Count ? $" ({preview.Count - ok} have something in the way)" : "")) +
                       (_control.OwnCasters(Spell.IceBridge).Count == 0 ? "   (select a mage first)"
                           : _control.ReadyCaster(spell: Spell.IceBridge) == 0 ? "   (no selected mage is ready)" : "") +
                       "\nRight click or Esc to stop";
            }
            case PlayerControl.ToolMode.Chasm:
            {
                var preview = _control.WallPreview;
                int ok = 0;
                foreach (var c in preview)
                    if (c.Ok) ok++;
                int group = _control.GroupCasters(Spell.Chasm);
                return (_control.WallStarted
                           ? $"Chasm: let go to open {ok} columns" + (ok < preview.Count ? $" ({preview.Count - ok} have a building, water or no ground to open)" : "")
                           : $"Chasm: drag a line of up to {SpellSystem.WallLength(group)} columns for a mage to open the ground along") +
                       (group > 1 ? $"   ({group} mages together: {SpellSystem.WallThickness(group)} wide)" : "") +
                       (_control.OwnCasters(Spell.Chasm).Count == 0 ? "   (select a mage first)"
                           : _control.ReadyCaster(spell: Spell.Chasm) == 0 ? "   (no selected mage is ready)" : "") +
                       "\nRight click or Esc to stop";
            }
            case PlayerControl.ToolMode.GrowForest:
            {
                var preview = _control.WallPreview;
                int ok = 0;
                foreach (var c in preview)
                    if (c.Ok) ok++;
                int group = _control.GroupCasters(Spell.GrowForest);
                int square = Math.Clamp(_sim.World.TreeScale, 1, Rts.Sim.World.Trees.MaxScale);
                square *= square;
                return (_control.WallStarted
                           ? $"Grow Forest: let go to grow {ok / square} trees" + (ok < preview.Count ? $" ({(preview.Count - ok) / square} have a building, water, a unit or something in the way)" : "")
                           : $"Grow Forest: drag a line of up to {SpellSystem.WallLength(group)} columns for a mage to grow trees along") +
                       (group > 1 ? $"   ({group} mages together: {group} rows)" : "") +
                       (_control.OwnCasters(Spell.GrowForest).Count == 0 ? "   (select a mage first)"
                           : _control.ReadyCaster(spell: Spell.GrowForest) == 0 ? "   (no selected mage is ready)" : "") +
                       "\nRight click or Esc to stop";
            }
            case PlayerControl.ToolMode.Bridge:
            {
                var preview = _control.WallPreview;
                int ok = 0;
                foreach (var c in preview)
                    if (c.Ok) ok++;
                return (!_control.WallStarted ? "Bridge: drag from bank to bank for the selected workers to bridge"
                           : preview.Count == 0 ? "Bridge: both ends must be ground, about level, with deep water or a drop between"
                           : $"Bridge: let go to lay {ok} deck columns" + (ok < preview.Count ? $" ({preview.Count - ok} have no room or goods)" : "")) +
                       (_control.OwnWorkers().Count == 0 ? "   (select workers first)" : "") +
                       "\nShift: draw another   Right click or Esc to stop   (Ctrl+right click a bridge: knock that column out)";
            }
            case PlayerControl.ToolMode.Road:
                return _control.RoadStarted
                    ? $"Road: click where it ends ({_control.RoadPreview.Count} blocks, {_control.RoadPreview.Count * EconomySystem.StonePerRoad} stone; Shift carries on)\nRight click or Esc to stop"
                    : "Road: click where it starts\nRight click or Esc to stop";
            default:
                return "Left: select   Right: move, attack, gather, build, work, clean blood, capture, trade   Right-drag: line up   Ctrl+Right: attack-move, burn   Alt+Right: patrol, guard   Ctrl+1-9: groups\n" +
                       $"Space: pause   -/+: speed   PgUp/PgDn: slice   {(PlayerControl.TerrainTool ? "T: terrain   " : "")}X: tunnel   Z: dig   Shift+Z: chasm   H G C B M N O I ;: build   / \\: towers   Tab: plan   Y: wall   Shift+Y: earth wall   ': bridge   Shift+': ice bridge   L: road   U: train   K: techs\n" +
                       "J: automatic workers (Shift+J: all)   P: repeat training (Shift+P: rebuild)   V: stance   Shift+L: grow forest";
        }
    }

    public static string CostText(GoodAmount[] cost)
    {
        var text = new StringBuilder();
        foreach (var c in cost)
        {
            if (text.Length > 0) text.Append(", ");
            text.Append(c.Amount).Append(' ').Append(Goods.Name(c.Good).ToLowerInvariant());
        }
        return text.Length > 0 ? text.ToString() : "free";
    }

    private string DescribeSelection()
    {
        if (_sim.Buildings.Get(_control.SelectedBuilding) is { } building) return DescribeBuilding(building);
        if (_sim.Cities.Get(_control.SelectedCity) is { } town) return DescribeCity(town);
        var sel = _control.Selected;
        if (sel.Count == 0) return "";
        var u = _sim.Units;
        var defs = _sim.Content.Units;
        if (sel.Count == 1)
        {
            int i = u.IndexOf(sel[0]);
            var def = defs[u.Def[i]];
            string load = u.CarryAmount[i] > 0 ? $"    carrying {u.CarryAmount[i]} {Goods.Name(u.Carry[i]).ToLowerInvariant()}" : "";
            string morale = def.CanAttack && u.Rout[i] == 0 ? $"    morale {u.Morale[i]}" : "";
            string rule = def.Role == UnitRole.Worker
                ? (u.Auto[i] ? (u.Held[i] ? "    automatic, waiting for orders" : "    automatic") : "")
                : def.CanAttack ? $"    {StanceName(u.Stance[i])}" : "";
            string heals = !_sim.Combat.Healing(i, out bool home) ? "" : !home ? " (healing)"
                : _sim.Cities.HealingTown(u.Owner[i], u.X[i].FloorToInt(), u.Y[i], u.Z[i].FloorToInt()) is { } camp
                    ? $" (healing fast, at {camp.Def.Name.ToLowerInvariant()})" : " (healing fast, at home)";
            int hurt = _sim.Hazards.DamageAt(u.X[i].FloorToInt(), u.Y[i], u.Z[i].FloorToInt());
            if (hurt > 0) heals += _sim.World.Get(u.X[i].FloorToInt(), u.Y[i] + 1, u.Z[i].FloorToInt()) == BlockType.Water ? " (drowning)"
                : hurt >= Rts.Sim.World.HazardSystem.LavaDamage ? " (scorched by lava)"
                : _sim.World.Get(u.X[i].FloorToInt(), u.Y[i] - 1, u.Z[i].FloorToInt()) == BlockType.Quicksand ? " (caught in quicksand)" : " (poisoned by the bog)";
            string staff = _sim.DuskTree.HasStaff(i) ? " with the Dusk Staff" : "";
            if (_sim.RelicOf(i) is { } relic) staff += $" with the {relic.Name}";
            int tend = _sim.DuskTree.TendProgress(u.Id[i]);
            int searching = _sim.Ruins.Searching(u.Id[i]);
            if (searching >= 0)
                heals += $" (searching the ruin, {_sim.Ruins.Progress(u.Owner[i], searching) * 100 / RuinSystem.SearchTicks}%)";
            if (tend > 0)
                heals += _sim.DuskTree.Razing(u.Id[i])
                    ? $" (hacking at the dusk tree, {_sim.DuskTree.RazeWork * 100 / DuskTreeSystem.RazeTicks}%)"
                    : $" (healing the dusk tree, {tend * 100 / DuskTreeSystem.TendTicks}%)";
            return $"{def.Name}{staff}   ({PlayerName(u.Owner[i])})   {_sim.Content.Combat.Job(def.Role)}\n" +
                   $"HP {u.Hp[i]} / {_sim.MaxHpOf(i)}{heals}    {Activity(u, i)}{load}{morale}{rule}    height {u.Y[i]}" +
                   DescribeExperience(u, i);
        }
        var counts = new int[defs.Count];
        int moving = 0, working = 0, fighting = 0, routing = 0, automatic = 0, ready = 0, beaten = 0, healing = 0, patrolling = 0, guarding = 0;
        var stances = new int[3];
        foreach (int id in sel)
        {
            int i = u.IndexOf(id);
            counts[u.Def[i]]++;
            var def = defs[u.Def[i]];
            if (u.Auto[i]) automatic++;
            if (u.Owner[i] == PlayerControl.LocalPlayer && _sim.Evolution.CanEvolve(i)) ready++;
            if (def.CanAttack && def.Role != UnitRole.Worker && (int)u.Stance[i] < stances.Length) stances[(int)u.Stance[i]]++;
            if (_sim.Combat.Healing(i, out _)) healing++;
            if (u.Combat[i] == CombatOrder.Patrol) patrolling++;
            if (u.Combat[i] == CombatOrder.Guard) guarding++;
            if (u.Subdued[i]) beaten++;
            else if (u.Rout[i] > 0) routing++;
            else if (u.Job[i] != UnitJob.None) working++;
            else if (u.Target[i] != 0 || u.TargetBuilding[i] != 0) fighting++;
            else if (u.Order[i] == UnitOrder.Move) moving++;
        }
        var parts = new List<string> { $"{moving} moving" };
        if (working > 0) parts.Add($"{working} at work");
        if (fighting > 0) parts.Add($"{fighting} fighting");
        if (patrolling > 0) parts.Add($"{patrolling} patrolling");
        if (guarding > 0) parts.Add($"{guarding} guarding");
        if (routing > 0) parts.Add($"{routing} running away");
        if (healing > 0) parts.Add($"{healing} healing");
        if (automatic > 0) parts.Add($"{automatic} automatic");
        if (ready > 0) parts.Add($"{ready} can evolve");
        if (beaten > 0) parts.Add($"{beaten} beaten, waiting for you");
        if (stances[(int)UnitStance.Hold] > 0) parts.Add($"{stances[(int)UnitStance.Hold]} holding");
        if (stances[(int)UnitStance.Retreat] > 0) parts.Add($"{stances[(int)UnitStance.Retreat]} retreat when hurt");
        string busy = string.Join(", ", parts);
        var text = new StringBuilder($"{sel.Count} units selected   ({busy})\n");
        int shown = 0;
        for (int d = 0; d < defs.Count; d++)
        {
            if (counts[d] == 0) continue;
            if (shown++ > 0) text.Append("   ");
            text.Append(counts[d]).Append(' ').Append(counts[d] > 1 ? Plural(defs[d].Name) : defs[d].Name);
        }
        return text.ToString();
    }

    /// <summary>
    /// For a unit whose player's units earn experience: its level and experience (and what the next level
    /// needs), and whether it can evolve now.
    /// </summary>
    private string DescribeExperience(UnitStore u, int i)
    {
        var evolution = _sim.Evolution;
        if (evolution.Rules(u.Owner[i]) == null) return "";
        int next = evolution.NextLevelXp(i);
        var text = new StringBuilder($"\nLevel {evolution.Level(i)}    experience {u.Xp[i]}");
        if (next > 0) text.Append($" / {next}");
        if (evolution.CanEvolve(i))
            text.Append(u.Owner[i] == PlayerControl.LocalPlayer ? "    ready to evolve: pick a form below" : "    ready to evolve");
        return text.ToString();
    }

    private string DescribeBuilding(Building b)
    {
        var u = _sim.Units;
        var text = new StringBuilder($"{b.Def.Name}   ({PlayerName(b.Owner)})");
        if (b.MaxHealth > 0) text.Append($"   health {b.Health} / {b.MaxHealth}");
        text.Append('\n');
        if (b.Planned)
        {
            var result = _sim.Economy.CanStart(b);
            text.Append(result switch
            {
                PlaceResult.Ok => "Planned: ready to build. Right click it to start",
                PlaceResult.CannotAfford => $"Planned: waiting for goods ({CostText(b.Def.Cost)})",
                PlaceResult.Locked => $"Planned: {Needs(b.Def.Requires, b.Def.Faction)}",
                _ => "Planned: something is in the way now",
            });
        }
        else if (b.State == BuildingState.Site)
        {
            int builders = 0;
            for (int i = 0; i < u.Count; i++)
                if (u.Job[i] == UnitJob.Build && u.JobTarget[i] == b.Id) builders++;
            text.Append($"Being built: {100L * b.Work / b.Def.BuildTicks}%   {builders} builder{(builders == 1 ? "" : "s")}");
            if (builders == 0) text.Append(" (right click it with workers)");
        }
        else
        {
            var parts = new List<string>();
            int housing = _sim.Economy.HousingOf(b);
            if (housing > 0) parts.Add($"Houses {housing}");
            if (b.Def.WorkerSlots > 0)
            {
                var job = EconomySystem.JobAt(b.Def);
                int n = 0;
                for (int i = 0; i < u.Count; i++)
                    if (u.Job[i] == job && u.JobTarget[i] == b.Id) n++;
                parts.Add($"{n} of {b.Def.WorkerSlots} workers (right click it with workers)");
            }
            if (b.Def.Plants > 0) parts.Add($"{_sim.Economy.SaplingsOf(b)} saplings growing");
            if (b.Def.Kind == BuildingKind.Smelter && b.SmeltTicks > 0)
                parts.Add($"smelting {100 - 100L * b.SmeltTicks / EconomySystem.SmeltTicks}%");
            if (b.Def.Kind == BuildingKind.Shrine)
                parts.Add(b.SmeltTicks > 0
                    ? $"channelling a crystal {100 - 100L * b.SmeltTicks / Math.Max(1, b.Def.ChannelTicks)}% ({b.Def.Mana} mana each)"
                    : $"no crystal to channel ({b.Def.Mana} mana each; dig crystal in the caverns)");
            text.Append(string.Join("    ", parts));
            if (b.Def.Storage || b.Def.Kind is BuildingKind.Smelter or BuildingKind.Shrine)
            {
                var goods = new List<string>();
                foreach (var g in Goods.All)
                    if (b.Stock[(int)g] > 0) goods.Add($"{b.Stock[(int)g]} {Goods.Name(g).ToLowerInvariant()}");
                if (parts.Count > 0) text.Append('\n');
                text.Append(goods.Count > 0 ? "Holds " + string.Join(", ", goods) : "Empty");
                if (b.Def.Storage && b.HasSupply) text.Append(DescribeSupply(b));
            }
            if (b.Queue.Count > 0)
            {
                var first = _sim.Content.Units[b.Queue[0]];
                text.Append($"\nTraining a {first.Name.ToLowerInvariant()}: {100L * b.TrainTicks / Math.Max(1, first.TrainTicks)}%");
                if (b.Queue.Count > 1) text.Append($", {b.Queue.Count - 1} more queued");
                if (_sim.Economy.Population(b.Owner) >= _sim.Economy.Housing(b.Owner)) text.Append("   (needs more houses)");
            }
            if (b.Def.IsRampart) text.Append(DescribeWall(b));
            else text.Append(DescribeUpgrade(b));
            if (b.Def.Trains.Length > 0 && b.Owner == PlayerControl.LocalPlayer)
                text.Append(b.HasRally
                    ? "\nRally point set: new units walk to the flag (right click the building to clear it)"
                    : "\nRight click the ground to set a rally point for new units");
            if (b.Governor != GovernorFocus.None)
                text.Append($"\nGoverned for {b.Governor.ToString().ToLowerInvariant()}: the governor builds, trains and researches for this town");
            if (b.Repeat)
            {
                text.Append("\nOn repeat");
                if (b.Owed.Count > 0)
                    text.Append($": {b.Owed.Count} to queue again when there are goods (and a minute's food) for {(b.Owed.Count == 1 ? "it" : "them")}");
            }
        }
        if (b.Raid != RaidKind.None && b.RaidPlayer >= 0)
        {
            int total = b.Raid == RaidKind.Capture ? EconomySystem.CaptureTicks : EconomySystem.BurnTicks;
            text.Append($"\n{(b.Raid == RaidKind.Capture ? "Being captured" : "Burning")} by {PlayerName(b.RaidPlayer)}: {100L * b.RaidTicks / total}%");
        }
        return text.ToString();
    }

    /// <summary>The building's supply rules: what it keeps, what is still missing, and who is on supply duty.</summary>
    private string DescribeSupply(Building b)
    {
        var u = _sim.Units;
        int duty = 0;
        for (int i = 0; i < u.Count; i++)
            if (u.Job[i] == UnitJob.Supply && u.JobTarget[i] == b.Id) duty++;
        var rules = new List<string>();
        foreach (var g in Goods.All)
        {
            int want = b.Supply[(int)g];
            if (want == 0) continue;
            int missing = EconomySystem.Short(b, g);
            rules.Add($"{want} {Goods.Name(g).ToLowerInvariant()}{(missing > 0 ? $" ({missing} short)" : "")}");
        }
        return $"\nKeeps {string.Join(", ", rules)}   {duty} on supply duty";
    }

    /// <summary>
    /// A house's, hall's or other building's upgrade: how it is going, and what the building does better than the
    /// one it came from, or that it is as big as it gets.
    /// </summary>
    private string DescribeUpgrade(Building b)
    {
        var text = new StringBuilder();
        if (b.Def.Speed > 100)
            text.Append($"\n{(b.Def.Kind == BuildingKind.Smelter ? "Smelts" : b.Def.Kind == BuildingKind.Shrine ? "Channels" : "Trains")} at {b.Def.Speed}% of the usual pace");
        if (b.Def.UpgradeOf is { } from && from.Raidable && !b.Def.Raidable) text.Append("\nToo stout to be captured or burned");
        if (b.Upgrade is { } to)
        {
            var u = _sim.Units;
            int builders = 0;
            for (int i = 0; i < u.Count; i++)
                if (u.Job[i] == UnitJob.Build && u.JobTarget[i] == b.Id) builders++;
            if (b.UpgradeWork >= to.BuildTicks) text.Append($"\nBecoming a {to.Name.ToLowerInvariant()}: waiting for the ground round it to clear");
            else
            {
                text.Append($"\nBecoming a {to.Name.ToLowerInvariant()}: {100L * b.UpgradeWork / Math.Max(1, to.BuildTicks)}%   " +
                            $"{builders} builder{(builders == 1 ? "" : "s")}");
                if (builders == 0) text.Append(" (right click it with workers)");
            }
        }
        else if (b.Def.Upgrade == null && b.Def.UpgradeOf != null) text.Append("\nAs big as it gets");
        return text.ToString();
    }

    /// <summary>
    /// A wall's, gate's or tower's place among its tiers, who is posted on it, what a tower does for them, and
    /// how its upgrade is going.
    /// </summary>
    private string DescribeWall(Building b)
    {
        var u = _sim.Units;
        var econ = _sim.Economy;
        var text = new StringBuilder();
        if (b.Def.IsTower) text.Append($"Sees {b.Def.Sight} columns around it\n");
        text.Append(!b.Def.Walkway ? "Too low to post anyone on"
            : b.Def.IsTower ? "Ranged units are posted on top (right click it with them)"
            : "Archers and other ranged units can be posted on top (right click it with them)");
        if (b.Def.Walkway)
        {
            int posted = 0, first = -1;
            for (int i = 0; i < u.Count; i++)
                if (u.Y[i] == b.Y + b.Def.Height && b.Covers(u.X[i].FloorToInt(), u.Z[i].FloorToInt()) && econ.WallUnder(i) == b)
                {
                    if (first < 0) first = i;
                    posted++;
                }
            if (b.Def.IsTower)
            {
                int free = econ.FreePosts(b);
                text.Append($"\n{posted} up there, room for {free} more along its edge");
                if (b.Def.Aim > 0)
                    text.Append($"\nFrom up here {b.Def.Aim}% of the shots that would miss in the dark\nor the rain land, and more hit the head");
            }
            else if (first >= 0) text.Append($": a {_sim.Content.Units[u.Def[first]].Name.ToLowerInvariant()} stands here");
        }
        if (b.Def.FacingOf is { } faced)
            text.Append($"\nThe facing of a {faced.Name.ToLowerInvariant()}: with it the wall is three columns thick");
        else if (b.Def.Facing != null)
        {
            int missing = econ.MissingFacings(b).Count;
            text.Append(missing > 0 ? $"\nIt has lost its facing on {missing} column{(missing == 1 ? "" : "s")} beside it"
                : "\nFaced on its free sides: three columns thick");
        }
        if (b.Upgrade != null)
        {
            int builders = 0;
            for (int i = 0; i < u.Count; i++)
                if (u.Job[i] == UnitJob.Build && u.JobTarget[i] == b.Id) builders++;
            text.Append($"\nBeing made a {b.Upgrade.Name.ToLowerInvariant()}: {100L * b.UpgradeWork / Math.Max(1, b.Upgrade.BuildTicks)}%   " +
                        $"{builders} builder{(builders == 1 ? "" : "s")}");
            if (builders == 0) text.Append(" (right click it with workers)");
        }
        else if (b.Def.IsWall && b.Def.Upgrade == null && (b.Def.UpgradeOf != null || b.Def.FacingOf != null)) text.Append("\nThe strongest there is");
        return text.ToString();
    }

    /// <summary>
    /// A city-state's town: who holds it and how to take it, its totem and guards, the tribute and units its
    /// holder gets, and what its trade pays.
    /// </summary>
    private string DescribeCity(CityState town)
    {
        var def = town.Def;
        int player = PlayerControl.LocalPlayer;
        var u = _sim.Units;
        int trading = 0;
        for (int i = 0; i < u.Count; i++)
            if (u.Owner[i] == player && u.Job[i] == UnitJob.Trade && u.JobTarget[i] == town.Id) trading++;
        const int tps = Simulation.TicksPerSecond;
        string Seconds(int ticks) => $"{(ticks + tps - 1) / tps} s";
        var totem = _sim.Cities.Totem(town);
        string totemText = totem != null ? $"Totem {totem.Health}/{totem.MaxHealth}" : $"Totem down: it stands again in {Seconds(town.RespawnTimer)}";
        string guards = $"{town.Guards.Count()} of {def.Guards.Count} guards" +
                        (town.Guards.Count() < def.Guards.Count ? $", the next in {Seconds(def.GuardRefillTicks - town.GuardTimer)}" : "");
        string take = town.Holder == player
            ? "Its guards fight your enemies' soldiers in the town; anyone who breaks the totem takes it from you" +
              (def.HealRange >= 0 ? $"\nYour hurt units within {def.HealRange} blocks of the town heal fast there, as at home" : "")
            : town.Holder >= 0
                ? $"Break {PlayerName(town.Holder)}'s totem to take the town: its guards fight your soldiers there"
                : "Break the totem to make them your vassals: the guards fight soldiers that come in, never workers or caravans";
        string tribute = def.TributeCaravan != null && def.TributeAmount > 0
            ? $"Tribute for its holder: {def.TributeAmount} {Goods.Name(def.TributeGood).ToLowerInvariant()} every {Seconds(def.TributeTicks)}" +
              (town.Holder == player ? $", the next in {Seconds(def.TributeTicks - town.TributeTimer)}" : "") + "\n"
            : "";
        string refill = town.Ready < def.Pool ? $", the next in {Seconds(def.RefillTicks - town.RefillTimer)}" : "";
        return $"{def.Name}   (city-state)   {HolderText(town, player)}\n" +
               $"{totemText}   {guards}\n" +
               take + "\n" + tribute +
               $"Caravans bring {def.Load} {Goods.Name(def.Buys).ToLowerInvariant()} and get {def.Price} {Goods.Name(def.Sells).ToLowerInvariant()} " +
               $"per 100 blocks from your storage ({def.HolderBonus}% more for its holder): {trading} of yours trading here\n" +
               $"Mercenaries for its holder: {town.Ready} of {def.Pool} ready{refill}";
    }

    /// <summary>What a unit is doing: running away, its job, fighting or chasing, moving, or idle.</summary>
    private string Activity(UnitStore u, int i)
    {
        if (u.Subdued[i]) return u.Owner[i] == PlayerControl.LocalPlayer ? "beaten: ride it or recruit it (below)" : "beaten";
        if (u.Rout[i] > 0) return "running away (broken)";
        if (DescribeJob(u, i) is { } job) return job;
        int t = u.IndexOf(u.Target[i]);
        string enemy = t >= 0 ? $"a {_sim.Content.Units[u.Def[t]].Name.ToLowerInvariant()}" : "an enemy";
        if (u.Order[i] == UnitOrder.Chase) return u.TargetBuilding[i] != 0 ? "going to attack a building" : $"chasing {enemy}";
        if (u.TargetBuilding[i] != 0) return "attacking a building";
        if (u.Target[i] != 0) return $"fighting {enemy}";
        if (u.Order[i] == UnitOrder.Move) return u.Combat[i] switch
        {
            CombatOrder.AttackMove => "attack-moving",
            CombatOrder.Patrol => "patrolling",
            CombatOrder.Guard => GuardActivity(u, i),
            _ => "moving",
        };
        if (u.Combat[i] == CombatOrder.Guard) return GuardActivity(u, i);
        return "idle";
    }

    /// <summary>"guarding" and what: a unit by its kind, or a building by its name.</summary>
    private string GuardActivity(UnitStore u, int i)
    {
        int w = u.GuardUnit[i] != 0 ? u.IndexOf(u.GuardUnit[i]) : -1;
        if (w >= 0) return $"guarding a {_sim.Content.Units[u.Def[w]].Name.ToLowerInvariant()}";
        var b = _sim.Buildings.Get(u.GuardBuilding[i]);
        if (_sim.Combat.TownGuard(i)) return "defending the town";
        return b != null ? $"guarding the {b.Def.Name.ToLowerInvariant()}" : "guarding";
    }

    private static string StanceName(UnitStance stance) => stance switch
    {
        UnitStance.Hold => "holds its ground",
        UnitStance.Retreat => "retreats when hurt",
        _ => "aggressive",
    };

    /// <summary>What a unit's job has it doing, or null if it has none.</summary>
    private string? DescribeJob(UnitStore u, int i) => u.Job[i] switch
    {
        UnitJob.Gather => u.JobBlock[i] switch
        {
            BlockType.Wood => "chopping wood",
            BlockType.BerryBush => "picking berries",
            BlockType.Stone or BlockType.LooseRock => "quarrying stone",
            BlockType.IronOre => "mining iron ore",
            BlockType.Coal => "mining coal",
            BlockType.GoldOre => "mining gold",
            _ => "digging",
        },
        UnitJob.Build => "building",
        UnitJob.Farm => "farming",
        UnitJob.Quarry => _sim.Buildings.Get(u.JobTarget[i]) is { } q ? q.Def.Shore ? "fishing" : q.Def.Plants > 0 ? "planting trees" : $"working the {q.Def.Name.ToLowerInvariant()}" : "quarrying",
        UnitJob.Haul => _sim.Buildings.Get(u.JobTarget[i])?.Def.Kind == BuildingKind.Shrine ? "keeping the shrine" : "supplying the smelter",
        UnitJob.Supply => u.Phase[i] switch
        {
            JobPhase.Fetch => "on supply duty: fetching a load",
            JobPhase.Wait => "on supply duty: waiting for goods to fetch",
            _ => "on supply duty: bringing a load",
        },
        UnitJob.Deliver => "taking goods to storage",
        UnitJob.Capture => "capturing a storehouse",
        UnitJob.Burn => "burning a storehouse",
        UnitJob.Tunnel => "tunnelling",
        UnitJob.Clean => u.Phase[i] == JobPhase.Working ? "scrubbing blood" : "cleaning blood",
        UnitJob.Post => u.Phase[i] == JobPhase.Working ? "climbing a wall" : "going to its post on a wall",
        UnitJob.Dig => u.Phase[i] switch
        {
            JobPhase.ToStore => "digging an area: taking what it dug to storage",
            JobPhase.Wait => "digging an area: waiting for room",
            _ => "digging an area",
        },
        UnitJob.Trade => u.Phase[i] switch
        {
            JobPhase.Fetch => "trading: fetching a load",
            JobPhase.ToWork => "trading: taking a load to market",
            JobPhase.ToStore => "trading: bringing back its pay",
            _ => "trading: waiting for a whole load in storage",
        },
        _ => null,
    };

    /// <summary>What the game says about the selected unit's type (a form's gains and drawbacks), if one unit is selected.</summary>
    private string DescribeUnitType()
    {
        if (_sim.Cities.Get(_control.SelectedCity) is { } town) return town.Def.Description;
        if (_control.SelectedBuilding != 0 || _control.Selected.Count != 1) return "";
        int i = _sim.Units.IndexOf(_control.Selected[0]);
        return i < 0 ? "" : _sim.Content.Units[_sim.Units.Def[i]].Description;
    }

    /// <summary>The local player's stored goods, and population over housing, each beside its picture.</summary>
    private void ShowStock()
    {
        var econ = _sim.Economy;
        int player = PlayerControl.LocalPlayer;
        var stock = econ.Stockpile(player);
        foreach (var good in Goods.All) _stockLabels[(int)good].Text = stock[(int)good].ToString(CultureInfo.InvariantCulture);
        _popLabel.Text = $"{econ.Population(player)} / {econ.Housing(player)}";
        _manaLabel.Text = econ.Player(player).Mana.ToString(CultureInfo.InvariantCulture);
        int rate = econ.FoodPerMinute(player);
        _foodRateLabel.Text = (rate > 0 ? "+" : "") + rate.ToString(CultureInfo.InvariantCulture) + "/min";
        _foodRateLabel.AddThemeColorOverride("font_color", rate > 0 ? new Color(0.55f, 0.9f, 0.45f) : new Color(0.8f, 0.8f, 0.75f));
    }

    /// <summary>A picture and a number beside it in the top bar; <paramref name="tooltip"/> says what it counts.</summary>
    private static Label Stat(HBoxContainer row, Texture2D icon, string tooltip)
    {
        var box = new HBoxContainer { TooltipText = tooltip, MouseFilter = Control.MouseFilterEnum.Pass };
        box.AddThemeConstantOverride("separation", 5);
        box.AddChild(new TextureRect { Texture = icon, StretchMode = TextureRect.StretchModeEnum.KeepCentered, MouseFilter = Control.MouseFilterEnum.Ignore });
        var label = MakeLabel("", 19);
        label.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        label.CustomMinimumSize = new Vector2(30, 0);
        box.AddChild(label);
        row.AddChild(box);
        return label;
    }

    private static string Plural(string name) => name.EndsWith("man") ? name[..^3] + "men" : name + "s";

    private static string PlayerName(int owner) =>
        owner == PlayerControl.LocalPlayer ? "you" : owner == Simulation.WildPlayer ? "wild" : $"player {owner + 1}";

    private void SyncSlider()
    {
        _updatingSlider = true;
        _slider.Value = _slice.On ? _slice.Level : _slice.MaxLevel;
        _updatingSlider = false;
        _sliceLabel.Text = _slice.On ? $"cut at {_slice.Level}" : "off";
    }

    /// <summary>Anchors a control to a corner or edge midpoint, offset by (x, y) pixels, at its minimum size.</summary>
    internal static void Place(Control c, Control.LayoutPreset preset, float x, float y)
    {
        c.SetAnchorsPreset(preset);
        c.OffsetLeft = c.OffsetRight = x;
        c.OffsetTop = c.OffsetBottom = y;
    }

    internal static Label MakeLabel(string text, int size)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }

    internal static Button MakeToolButton(string text)
    {
        var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, ToggleMode = true };
        b.AddThemeFontSizeOverride("font_size", 13);
        b.Alignment = HorizontalAlignment.Left;
        b.CustomMinimumSize = new Vector2(150, 0);
        return b;
    }

    internal static Button MakeButton(string text)
    {
        var b = new Button { Text = text, ToggleMode = true, FocusMode = Control.FocusModeEnum.None };
        b.AddThemeFontSizeOverride("font_size", 16);
        b.CustomMinimumSize = new Vector2(52, 0);
        return b;
    }

    /// <summary>
    /// Gives a button the current look's button style tinted with <paramref name="colour"/> (lighter on hover,
    /// sunken when pressed), for the buttons that must stand out from the usual ones: the start screen's, the
    /// end of match panel's and the alerts.
    /// </summary>
    internal static void Frame(Button b, Color colour)
    {
        var look = UiStyle.Current;
        var frame = colour.Lightened(0.3f);
        b.AddThemeStyleboxOverride("normal", UiStyle.Box(colour, frame, false, 1f, 14, 6));
        b.AddThemeStyleboxOverride("hover", UiStyle.Box(colour.Lightened(0.14f), frame.Lightened(0.15f), false, 1f, 14, 6));
        b.AddThemeStyleboxOverride("pressed", UiStyle.Box(colour.Darkened(0.15f), look.Highlight, true, 1f, 14, 6));
        b.AddThemeStyleboxOverride("hover_pressed", UiStyle.Box(colour.Darkened(0.05f), look.Highlight, true, 1f, 14, 6));
        b.AddThemeColorOverride("font_hover_color", Colors.White);
        b.AddThemeColorOverride("font_pressed_color", Colors.White);
    }

    /// <summary>The colour of a button that starts or confirms something (see <see cref="Frame"/>).</summary>
    internal static Color AccentColour => UiStyle.Current.AccentFill;
    /// <summary>The colour of the other framed buttons (see <see cref="Frame"/>).</summary>
    internal static Color PlainColour => UiStyle.Current.ButtonFill;
}

/// <summary>The rubber-band rectangle while box-selecting.</summary>
public partial class SelectionBox : Control
{
    public bool Active;
    public Rect2 Rect;

    public override void _Draw()
    {
        if (!Active) return;
        DrawRect(Rect, new Color(0.35f, 1f, 0.4f, 0.12f));
        DrawRect(Rect, new Color(0.35f, 1f, 0.4f, 0.9f), filled: false, width: 1.5f);
    }
}
