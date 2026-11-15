using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Rts.Sim;
using Rts.Sim.Economy;
using Rts.Sim.World;

namespace Rts.Game;

/// <summary>
/// Test mode's panel (issue #89), shown in a match started with Test mode ticked on the start screen (or
/// --test-mode). Every change it makes is a test mode command (see <see cref="Rts.Sim.Sandbox.SandboxSystem"/>),
/// so the match records and replays as usual. F1 hides or shows it; F2 runs a single tick while paused.
///
///   Place    pick what to put down (a unit, building, tree, block, monster lair, blood, or the eraser), who
///            owns it and how many or how big, then Place on map: each left click on the map puts one down
///            until right click or Esc. Blocks are a square brush: air digs that deep and clears what stands
///            above, anything else is stacked that high on the ground.
///   Units    heal, kill, remove, wound (bleeding) or season (most experience) the selected units, and
///            switch which side you play (with fog of war off).
///   World    the weather, the time of day (with day and night on), goods and techs for either side.
///   Cheats   instant building, training and research, and invulnerability, for either side.
///   Save     single tick, and saving the scenario (the match's command log) and loading a saved one, which
///            replays it to the moment it was saved, paused.
/// Its controls are named for scripted input: test_tab_place, test_cat_unit (and _building, _tree, _block,
/// _lair, _blood, _erase), test_item, test_owner, test_size, test_height, test_place, test_heal, test_kill,
/// test_remove, test_bleed, test_veteran, test_switch, test_weather_storm (and the others), test_time_6 (and
/// 12, 20, 0), test_goods_P, test_techs_P, test_cheat_NAME_P (instantbuild, instanttrain, instantresearch,
/// invulnerable), test_step, test_save, test_scenarios, test_load.
/// </summary>
public partial class TestPanel : CanvasLayer
{
    public enum Category { Unit, Building, Tree, Block, Lair, Blood, Erase }

    /// <summary>The blocks the brush lays, in the order listed.</summary>
    public static readonly BlockType[] BrushBlocks =
    {
        BlockType.Stone, BlockType.Soil, BlockType.Grass, BlockType.Sand, BlockType.Mud, BlockType.IronOre, BlockType.Coal,
        BlockType.Crystal, BlockType.BerryBush, BlockType.Water, BlockType.Lava, BlockType.Air,
    };

    /// <summary>Raised by Save: the match is to be saved as a scenario.</summary>
    public event Action? SaveRequested;
    /// <summary>Raised by Load with the scenario file picked.</summary>
    public event Action<string>? LoadRequested;

    private Simulation _sim = null!;
    private PlayerControl _control = null!;
    private GameClock _clock = null!;
    private bool _fog;
    private string _folder = "";
    private Control _root = null!;
    private readonly List<(Button Tab, Control Page)> _pages = new();
    private readonly List<(Button Button, Category Category)> _categories = new();
    private Category _category = Category.Unit;
    private OptionButton _item = null!, _owner = null!, _scenarios = null!;
    private SpinBox _size = null!, _height = null!;
    private Label _sizeLabel = null!, _heightLabel = null!, _status = null!;
    private readonly List<(CheckButton Button, int Player, SandboxCheat Cheat)> _cheats = new();
    private readonly List<string> _scenarioPaths = new();
    private readonly List<int> _owners = new();
    private Button _switch = null!;
    private readonly List<Button> _dayButtons = new();
    private int _placed;
    // The buildings offered: all but a city-state's totem, which only stands in its town.
    private readonly List<BuildingDef> _buildings = new();

    /// <summary>The players test mode offers: those with a start on the map, then the wild monsters.</summary>
    private IReadOnlyList<int> Owners => _owners;

    public void Init(Simulation sim, PlayerControl control, GameClock clock, bool fog, string scenarioFolder)
    {
        _sim = sim;
        _control = control;
        _clock = clock;
        _fog = fog;
        _folder = scenarioFolder;
        Layer = 2;
        foreach (var s in sim.Map.Starts)
            if (!_owners.Contains(s.Player)) _owners.Add(s.Player);
        if (_owners.Count == 0) _owners.Add(0);
        _owners.Add(Simulation.WildPlayer);
        control.TestPlace = Place;
        foreach (var d in sim.Content.Buildings)
            if (d.Kind != BuildingKind.Totem) _buildings.Add(d);

        _root = new PanelContainer { Name = "test_panel", Theme = UiStyle.Theme };
        _root.AddThemeStyleboxOverride("panel", UiStyle.Panel(0.95f, 10));
        Hud.Place(_root, Control.LayoutPreset.CenterRight, -120, 0);
        _root.GrowHorizontal = Control.GrowDirection.Begin;
        _root.GrowVertical = Control.GrowDirection.Both;
        _root.CustomMinimumSize = new Vector2(330, 0);
        AddChild(_root);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        _root.AddChild(column);
        var title = Hud.MakeLabel("Test mode   (F1 hides)", 16);
        title.ThemeTypeVariation = UiStyle.Heading;
        column.AddChild(title);
        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 2);
        column.AddChild(tabs);

        AddPage(tabs, column, "Place", BuildPlace());
        AddPage(tabs, column, "Units", BuildUnits());
        AddPage(tabs, column, "World", BuildWorld());
        AddPage(tabs, column, "Cheats", BuildCheats());
        AddPage(tabs, column, "Save", BuildSave());
        ShowPage(0);

        _status = Hud.MakeLabel("", 12);
        _status.ThemeTypeVariation = UiStyle.Dim;
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _status.CustomMinimumSize = new Vector2(310, 0);
        column.AddChild(_status);
        SetCategory(Category.Unit);
    }

    /// <summary>Says something under the panel (what was saved, what could not be done).</summary>
    public void Say(string text) => _status.Text = text;

    // ---- Pages ----

    private void AddPage(HBoxContainer tabs, VBoxContainer column, string name, Control page)
    {
        var tab = Hud.MakeButton(name);
        tab.Name = "test_tab_" + name.ToLowerInvariant();
        tab.AddThemeFontSizeOverride("font_size", 13);
        int index = _pages.Count;
        tab.Pressed += () => ShowPage(index);
        tabs.AddChild(tab);
        column.AddChild(page);
        _pages.Add((tab, page));
    }

    private void ShowPage(int index)
    {
        for (int k = 0; k < _pages.Count; k++)
        {
            _pages[k].Page.Visible = k == index;
            _pages[k].Tab.SetPressedNoSignal(k == index);
        }
        if (index == 4) ListScenarios();
    }

    private static VBoxContainer Page()
    {
        var page = new VBoxContainer();
        page.AddThemeConstantOverride("separation", 5);
        return page;
    }

    private static Button Small(string text, string name, Action pressed)
    {
        var b = Hud.MakeButton(text);
        b.Name = name;
        b.ToggleMode = false;
        b.AddThemeFontSizeOverride("font_size", 13);
        b.CustomMinimumSize = new Vector2(0, 0);
        b.Pressed += pressed;
        return b;
    }

    private static GridContainer Grid(int columns)
    {
        var g = new GridContainer { Columns = columns };
        g.AddThemeConstantOverride("h_separation", 4);
        g.AddThemeConstantOverride("v_separation", 4);
        return g;
    }

    private static OptionButton Options(string name)
    {
        var o = new OptionButton { Name = name, FocusMode = Control.FocusModeEnum.None, FitToLongestItem = false };
        o.AddThemeFontSizeOverride("font_size", 13);
        o.CustomMinimumSize = new Vector2(220, 0);
        return o;
    }

    private SpinBox Spin(string name, int min, int max, int value)
    {
        var s = new SpinBox { Name = name, MinValue = min, MaxValue = max, Value = value, Step = 1, CustomMinimumSize = new Vector2(90, 0) };
        s.GetLineEdit().AddThemeFontSizeOverride("font_size", 13);
        return s;
    }

    private Control BuildPlace()
    {
        var page = Page();
        var cats = Grid(4);
        page.AddChild(cats);
        foreach (Category c in Enum.GetValues<Category>())
        {
            var b = Hud.MakeButton(c == Category.Erase ? "Eraser" : c.ToString());
            b.Name = "test_cat_" + c.ToString().ToLowerInvariant();
            b.AddThemeFontSizeOverride("font_size", 13);
            b.CustomMinimumSize = new Vector2(0, 0);
            var cat = c;
            b.Pressed += () => SetCategory(cat);
            cats.AddChild(b);
            _categories.Add((b, c));
        }
        var form = Grid(2);
        page.AddChild(form);
        form.AddChild(Hud.MakeLabel("What", 13));
        _item = Options("test_item");
        form.AddChild(_item);
        form.AddChild(Hud.MakeLabel("Owner", 13));
        _owner = Options("test_owner");
        foreach (int p in Owners) _owner.AddItem(OwnerName(p));
        form.AddChild(_owner);
        _sizeLabel = Hud.MakeLabel("How many", 13);
        form.AddChild(_sizeLabel);
        _size = Spin("test_size", 1, 50, 1);
        form.AddChild(_size);
        _heightLabel = Hud.MakeLabel("Height", 13);
        form.AddChild(_heightLabel);
        _height = Spin("test_height", 1, 16, 2);
        form.AddChild(_height);
        var place = Small("Place on map", "test_place", StartPlacing);
        Hud.Frame(place, Hud.AccentColour);
        page.AddChild(place);
        var hint = Hud.MakeLabel("Left click the map to place, as often as you like. Right click or Esc stops.", 12);
        hint.ThemeTypeVariation = UiStyle.Dim;
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        hint.CustomMinimumSize = new Vector2(310, 0);
        page.AddChild(hint);
        return page;
    }

    private string OwnerName(int p)
    {
        if (p == Simulation.WildPlayer) return "Wild (monsters)";
        string you = p == PlayerControl.LocalPlayer ? ", you" : "";
        return $"{_sim.Economy.Player(p).Faction.Name} (player {p}{you})";
    }

    private void SetCategory(Category c)
    {
        _category = c;
        foreach (var (b, cat) in _categories) b.SetPressedNoSignal(cat == c);
        _item.Clear();
        switch (c)
        {
            case Category.Unit:
                foreach (var d in _sim.Content.Units) _item.AddItem(d.Faction != null ? $"{d.Name} ({d.Faction.Name})" : d.Name);
                break;
            case Category.Building:
                foreach (var d in _buildings) _item.AddItem(d.Faction != null ? $"{d.Name} ({d.Faction.Name})" : d.Name);
                break;
            case Category.Tree:
                foreach (TreeKind k in Enum.GetValues<TreeKind>()) _item.AddItem(k == TreeKind.Broadleaf ? "Broad-leaved tree" : $"{k} tree");
                break;
            case Category.Block:
                foreach (var b in BrushBlocks) _item.AddItem(b == BlockType.Air ? "Air (dig out)" : b.ToString());
                break;
            case Category.Lair:
                foreach (var d in _sim.Content.LairMonsters) _item.AddItem($"{d.Name} lair");
                break;
            case Category.Blood:
                _item.AddItem("Fresh blood");
                break;
            case Category.Erase:
                _item.AddItem("Units and buildings");
                break;
        }
        if (_item.ItemCount > 0) _item.Select(0);
        _owner.Disabled = c is not (Category.Unit or Category.Building);
        (_sizeLabel.Text, _size.MinValue, _size.MaxValue, _size.Value) = c switch
        {
            Category.Unit => ("How many", 1, Rts.Sim.Sandbox.SandboxSystem.MaxUnits, 1),
            Category.Block => ("Brush width", 1, 15, 3),
            Category.Blood or Category.Erase => ("Radius", 0, Rts.Sim.Sandbox.SandboxSystem.MaxRadius, 3),
            _ => ("Size", 1, 1, 1),
        };
        _size.Editable = c is Category.Unit or Category.Block or Category.Blood or Category.Erase;
        _height.Editable = c == Category.Block;
        if (_control.Tool == PlayerControl.ToolMode.Test) StartPlacing();
    }

    private string Describing() => _item.Selected >= 0 ? _item.GetItemText(_item.Selected).ToLowerInvariant() : _category.ToString().ToLowerInvariant();

    private void StartPlacing() => _control.StartTestPlace(_category == Category.Erase ? "the eraser (removes units and buildings)" : Describing());

    private int Owner => _owner.Selected >= 0 && _owner.Selected < Owners.Count ? Owners[_owner.Selected] : PlayerControl.LocalPlayer;

    private void Issue(Command c) => _sim.Schedule(_sim.Tick, c);

    /// <summary>A left click on the map with the place tool on: column (x, z), ground at level y.</summary>
    private void Place(int x, int y, int z)
    {
        int me = PlayerControl.LocalPlayer, item = Math.Max(0, _item.Selected), size = (int)_size.Value, h = (int)_height.Value;
        switch (_category)
        {
            case Category.Unit:
                Issue(new SandboxUnitCommand(me, Owner, _sim.Content.Units[item].Id, x, z, size));
                break;
            case Category.Building:
                var def = _buildings[Math.Min(item, _buildings.Count - 1)];
                Issue(new SandboxBuildingCommand(me, Owner, def.Id, x - def.SizeX / 2, z - def.SizeZ / 2));
                break;
            case Category.Tree:
                Issue(new SandboxTreeCommand(me, Enum.GetValues<TreeKind>()[item], x, z));
                break;
            case Category.Block:
            {
                var block = BrushBlocks[item];
                int r0 = (size - 1) / 2, r1 = size / 2;
                // Air digs h deep and clears what stands above (trees, say); anything else is stacked h high.
                Issue(block == BlockType.Air
                    ? new SandboxBlocksCommand(me, block, x - r0, y - h + 1, z - r0, x + r1, Math.Min(_sim.World.SizeY - 1, y + 12), z + r1)
                    : new SandboxBlocksCommand(me, block, x - r0, y + 1, z - r0, x + r1, y + h, z + r1));
                break;
            }
            case Category.Lair:
                if (_sim.Content.LairMonsters.Count > 0) Issue(new SandboxLairCommand(me, _sim.Content.LairMonsters[item].Id, x, z));
                break;
            case Category.Blood:
                Issue(new SandboxBloodCommand(me, x, z, size, 120));
                break;
            case Category.Erase:
                Issue(new SandboxClearCommand(me, x, z, size));
                break;
        }
        _placed++;
    }

    private Control BuildUnits()
    {
        var page = Page();
        page.AddChild(Hud.MakeLabel("The selected units (anyone's):", 13));
        var g = Grid(3);
        page.AddChild(g);
        foreach (var (label, action) in new[]
                 {
                     ("Heal", SandboxAction.Heal), ("Kill", SandboxAction.Kill), ("Remove", SandboxAction.Remove),
                     ("Bleed", SandboxAction.Bleed), ("Veteran", SandboxAction.Veteran),
                 })
        {
            var a = action;
            g.AddChild(Small(label, "test_" + label.ToLowerInvariant(), () =>
            {
                if (_control.Selected.Count == 0)
                {
                    Say("Select some units first.");
                    return;
                }
                var ids = new int[_control.Selected.Count];
                for (int k = 0; k < ids.Length; k++) ids[k] = _control.Selected[k];
                Issue(new SandboxUnitsCommand(PlayerControl.LocalPlayer, ids, a));
            }));
        }
        _switch = Small("Play the other side", "test_switch", SwitchSide);
        _switch.Disabled = _fog;
        _switch.TooltipText = _fog ? "Only with fog of war off: what you see is the side you started as." : "You control the other side's units and buildings.";
        page.AddChild(_switch);
        return page;
    }

    private void SwitchSide()
    {
        int me = PlayerControl.LocalPlayer, next = me;
        for (int k = 0; k < Owners.Count; k++)
            if (Owners[k] == me) next = Owners[(k + 1) % (Owners.Count - 1)]; // the players, not the wild
        if (!_owners.Contains(me)) next = Owners[0];
        _control.SwitchPlayer(next);
        for (int k = 0; k < Owners.Count; k++) _owner.SetItemText(k, OwnerName(Owners[k]));
        Say($"You now play the {_sim.Economy.Player(next).Faction.Name} (player {next}).");
    }

    private Control BuildWorld()
    {
        var page = Page();
        page.AddChild(Hud.MakeLabel("Weather", 13));
        var weather = Grid(3);
        page.AddChild(weather);
        foreach (Weather w in Enum.GetValues<Weather>())
        {
            var ww = w;
            var b = Small(w.ToString(), "test_weather_" + w.ToString().ToLowerInvariant(), () => Issue(new SandboxWeatherCommand(PlayerControl.LocalPlayer, ww, 4)));
            weather.AddChild(b);
            _dayButtons.Add(b);
        }
        page.AddChild(Hud.MakeLabel("Time of day", 13));
        var time = Grid(4);
        page.AddChild(time);
        foreach (var (label, hour) in new[] { ("Dawn", 6), ("Noon", 12), ("Dusk", 20), ("Night", 0) })
        {
            int m = hour * 60;
            var b = Small(label, $"test_time_{hour}", () => Issue(new SandboxTimeCommand(PlayerControl.LocalPlayer, m)));
            time.AddChild(b);
            _dayButtons.Add(b);
        }
        if (!_sim.DayNight)
        {
            foreach (var b in _dayButtons) b.Disabled = true;
            var off = Hud.MakeLabel("Day, night and weather are off in this match (tick them on the start screen).", 12);
            off.ThemeTypeVariation = UiStyle.Dim;
            off.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            off.CustomMinimumSize = new Vector2(310, 0);
            page.AddChild(off);
        }
        page.AddChild(Hud.MakeLabel("Goods and techs", 13));
        var give = Grid(2);
        page.AddChild(give);
        foreach (int p in Owners)
        {
            if (p == Simulation.WildPlayer) continue;
            int pp = p;
            string who = _sim.Economy.Player(p).Faction.Name;
            give.AddChild(Small($"+1000 goods: {who}", $"test_goods_{p}", () => Issue(new SandboxGoodsCommand(PlayerControl.LocalPlayer, pp, 1000))));
            give.AddChild(Small($"All techs: {who}", $"test_techs_{p}", () => Issue(new SandboxTechCommand(PlayerControl.LocalPlayer, pp, "all"))));
        }
        return page;
    }

    private Control BuildCheats()
    {
        var page = Page();
        var g = Grid(Owners.Count);
        page.AddChild(g);
        g.AddChild(Hud.MakeLabel("", 13));
        foreach (int p in Owners)
            if (p != Simulation.WildPlayer) g.AddChild(Hud.MakeLabel(_sim.Economy.Player(p).Faction.Name, 13));
        foreach (SandboxCheat cheat in Enum.GetValues<SandboxCheat>())
        {
            g.AddChild(Hud.MakeLabel(CheatName(cheat), 13));
            foreach (int p in Owners)
            {
                if (p == Simulation.WildPlayer) continue;
                int pp = p;
                var c = cheat;
                var b = new CheckButton { Name = $"test_cheat_{cheat.ToString().ToLowerInvariant()}_{p}", FocusMode = Control.FocusModeEnum.None };
                b.Toggled += on => Issue(new SandboxCheatCommand(PlayerControl.LocalPlayer, pp, c, on));
                g.AddChild(b);
                _cheats.Add((b, p, cheat));
            }
        }
        return page;
    }

    private static string CheatName(SandboxCheat c) => c switch
    {
        SandboxCheat.InstantBuild => "Instant building",
        SandboxCheat.InstantTrain => "Instant training",
        SandboxCheat.InstantResearch => "Instant research",
        SandboxCheat.Invulnerable => "Invulnerable",
        _ => c.ToString(),
    };

    private Control BuildSave()
    {
        var page = Page();
        page.AddChild(Small("Step one tick  [F2]", "test_step", Step));
        var save = Small("Save scenario", "test_save", () => SaveRequested?.Invoke());
        Hud.Frame(save, Hud.AccentColour);
        page.AddChild(save);
        _scenarios = Options("test_scenarios");
        page.AddChild(_scenarios);
        page.AddChild(Small("Load scenario", "test_load", () =>
        {
            if (_scenarios.Selected < 0 || _scenarios.Selected >= _scenarioPaths.Count)
            {
                Say("No saved scenario to load.");
                return;
            }
            LoadRequested?.Invoke(_scenarioPaths[_scenarios.Selected]);
        }));
        var hint = Hud.MakeLabel($"Scenarios are saved in {ProjectSettings.GlobalizePath(_folder)}. Loading one plays it back to the moment it was saved, paused.", 12);
        hint.ThemeTypeVariation = UiStyle.Dim;
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        hint.CustomMinimumSize = new Vector2(310, 0);
        page.AddChild(hint);
        return page;
    }

    /// <summary>Fills the list of saved scenarios, newest first.</summary>
    public void ListScenarios()
    {
        _scenarios.Clear();
        _scenarioPaths.Clear();
        try
        {
            string dir = ProjectSettings.GlobalizePath(_folder);
            if (Directory.Exists(dir))
            {
                var files = new List<string>(Directory.GetFiles(dir, "*.log"));
                files.Sort(StringComparer.Ordinal);
                files.Reverse();
                foreach (var f in files)
                {
                    _scenarioPaths.Add(f);
                    _scenarios.AddItem(Path.GetFileNameWithoutExtension(f));
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Say($"Could not list the scenarios: {e.Message}");
        }
        if (_scenarios.ItemCount > 0) _scenarios.Select(0);
    }

    /// <summary>One tick, paused or not (paused is the point).</summary>
    private void Step()
    {
        _clock.Pause();
        _clock.StepPending = true;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } key) return;
        var code = key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode;
        if (code == Key.F1) _root.Visible = !_root.Visible;
        else if (code == Key.F2) Step();
        else return;
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        foreach (var (b, p, c) in _cheats) b.SetPressedNoSignal(_sim.Sandbox.Has(p, c));
    }

    /// <summary>The panel's state, for scripted tests.</summary>
    public string Describe()
    {
        var on = new List<string>();
        foreach (var (_, p, c) in _cheats)
            if (_sim.Sandbox.Has(p, c)) on.Add($"{c}:{p}");
        return $"test=1 test_panel={(_root.Visible ? 1 : 0)} test_category={_category} test_item={_item.Selected} " +
               $"test_placed={_placed} cheats={(on.Count > 0 ? string.Join(',', on) : "none")} lairs_all={_sim.Monsters.Lairs.Count} " +
               $"weather={_sim.Conditions.Weather} minute={_sim.Conditions.Minute} sim_buildings={_sim.Buildings.All.Count} sim_units={_sim.Units.Count}";
    }
}
