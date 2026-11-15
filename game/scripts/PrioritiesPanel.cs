using System;
using System.Collections.Generic;
using Godot;
using Rts.Sim;
using Rts.Sim.Automation;

namespace Rts.Game;

/// <summary>
/// The work priorities menu (Ctrl+J): the priority list of each of your workers, and your default list,
/// which new workers and every worker without a list of its own follow (see <see cref="WorkPriorities"/>).
/// On the left you pick whose list to edit: the default, the selected workers, or any one worker. On the
/// right is that list, top first: each kind of work can be moved up or down or taken off, and the kinds
/// not on it put back on. Every change is a command (<see cref="PriorityCommand"/> or
/// <see cref="DefaultPriorityCommand"/>), shown at once even while the game is paused. Buttons are named
/// <c>prio_...</c> so scripted input can click them: <c>prio_target_default</c>, <c>prio_target_selected</c>,
/// <c>prio_worker_ID</c>, <c>prio_up_KIND</c>, <c>prio_down_KIND</c>, <c>prio_toggle_KIND</c> (KIND as in the
/// log: build, farm, smelt, needed, food, wood, stone, iron-ore, coal), <c>prio_use_default</c>,
/// <c>prio_reset</c>, <c>prio_apply_all</c> and <c>prio_close</c>.
/// </summary>
public partial class PrioritiesPanel : PanelContainer
{
    /// <summary>The list being edited: the default list, the selected workers, or one worker (by id).</summary>
    private const int DefaultTarget = 0, SelectedTarget = -1;

    private Simulation _sim = null!;
    private PlayerControl _control = null!;
    private int _target;
    private Button _defaultButton = null!, _selectedButton = null!;
    private VBoxContainer _workerBox = null!, _rows = null!;
    private Label _title = null!, _note = null!;
    private Button _useDefault = null!, _reset = null!, _applyAll = null!;
    private readonly List<(int Id, Button Button)> _workerButtons = new();
    // Lists just changed, by worker id (0 for the default), until the command has been applied.
    private readonly Dictionary<int, (long Tick, ulong List)> _pending = new();
    private string _shownKey = "";

    public void Init(Simulation sim, PlayerControl control)
    {
        _sim = sim;
        _control = control;
        Name = "priorities_panel";
        AddThemeStyleboxOverride("panel", UiStyle.Panel(0.97f, 18));

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        AddChild(column);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 14);
        column.AddChild(header);
        var title = MakeLabel("Work priorities", 24);
        title.ThemeTypeVariation = UiStyle.Heading;
        header.AddChild(title);
        header.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        var close = new Button { Text = "Close  [Ctrl+J]", FocusMode = FocusModeEnum.None, Name = "prio_close" };
        close.Pressed += _control.TogglePriorities;
        header.AddChild(close);

        var body = new HBoxContainer();
        body.AddThemeConstantOverride("separation", 18);
        column.AddChild(body);

        // Whose list.
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(250, 0) };
        left.AddThemeConstantOverride("separation", 6);
        body.AddChild(left);
        var whose = MakeLabel("Whose list", 15);
        whose.ThemeTypeVariation = UiStyle.Dim;
        left.AddChild(whose);
        _defaultButton = TargetButton("Default list", "prio_target_default", DefaultTarget);
        _defaultButton.TooltipText = "The list new workers get, and every worker without a list of its own";
        left.AddChild(_defaultButton);
        _selectedButton = TargetButton("Selected workers", "prio_target_selected", SelectedTarget);
        left.AddChild(_selectedButton);
        var each = MakeLabel("Each worker", 15);
        each.ThemeTypeVariation = UiStyle.Dim;
        left.AddChild(each);
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(250, 300),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        left.AddChild(scroll);
        _workerBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _workerBox.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_workerBox);

        // The list.
        var right = new VBoxContainer { CustomMinimumSize = new Vector2(420, 0) };
        right.AddThemeConstantOverride("separation", 6);
        body.AddChild(right);
        _title = MakeLabel("", 18);
        right.AddChild(_title);
        _note = MakeLabel("", 13);
        _note.ThemeTypeVariation = UiStyle.Dim;
        _note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _note.CustomMinimumSize = new Vector2(420, 0);
        right.AddChild(_note);
        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 4);
        right.AddChild(_rows);
        right.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 6);
        right.AddChild(actions);
        _useDefault = ActionButton("Use the default list", "prio_use_default",
            "Drop this list: the workers follow your default list again");
        _useDefault.Pressed += () => Set(WorkPriorities.UseDefault);
        actions.AddChild(_useDefault);
        _reset = ActionButton("Standard list", "prio_reset", "Build, farm, smelt, then gather what's needed most");
        _reset.Pressed += () => Set(WorkPriorities.Standard);
        actions.AddChild(_reset);
        _applyAll = ActionButton("Put every worker on it", "prio_apply_all",
            "Every worker of yours drops its own list and follows the default list");
        _applyAll.Pressed += () =>
        {
            var all = _control.AllOwnWorkers();
            _control.SetPriorities(all.ToArray(), WorkPriorities.UseDefault);
            foreach (int id in all) _pending[id] = (_sim.Tick, WorkPriorities.UseDefault);
            _shownKey = "";
        };
        actions.AddChild(_applyAll);

        var hint = MakeLabel("An automatic worker with nothing to do first takes what it carries to storage, then goes down its list " +
                             "and takes the first kind of work there is some of near it. Work left off the list it leaves to others. " +
                             "A change counts from each worker's next job.", 13);
        hint.ThemeTypeVariation = UiStyle.Dim;
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        hint.CustomMinimumSize = new Vector2(690, 0);
        column.AddChild(hint);
    }

    /// <summary>Called when the menu opens: it starts on the selected workers if you have any selected, else on the default list.</summary>
    public void Opened()
    {
        _target = _control.OwnWorkers().Count > 0 ? SelectedTarget : DefaultTarget;
        _shownKey = "";
        Refresh();
    }

    /// <summary>Brings the menu up to date with the game: the workers, and the list being edited.</summary>
    public void Refresh()
    {
        var selected = _control.OwnWorkers();
        if (_target == SelectedTarget && selected.Count == 0) _target = DefaultTarget;
        if (_target > 0 && _sim.Units.IndexOf(_target) < 0) _target = DefaultTarget;
        _selectedButton.Visible = selected.Count > 0;
        _selectedButton.Text = $"Selected workers ({selected.Count})";
        _defaultButton.SetPressedNoSignal(_target == DefaultTarget);
        _selectedButton.SetPressedNoSignal(_target == SelectedTarget);
        RefreshWorkers();

        // Which workers the list is for, and the list they go by.
        int[] ids = _target == SelectedTarget ? selected.ToArray() : _target > 0 ? new[] { _target } : Array.Empty<int>();
        ulong raw = ids.Length > 0 ? Raw(ids[0]) : Raw(DefaultTarget);
        bool follows = ids.Length > 0 && raw == WorkPriorities.UseDefault;
        ulong list = follows ? Raw(DefaultTarget) : raw;
        bool mixed = false;
        foreach (int id in ids)
            if (Raw(id) != raw) mixed = true;

        _title.Text = _target == DefaultTarget ? "Default list"
            : _target == SelectedTarget ? $"{selected.Count} selected worker{(selected.Count == 1 ? "" : "s")}"
            : $"{WorkerName(_target)}";
        _note.Text = _target == DefaultTarget
            ? "New workers get this list, and so does every worker without a list of its own."
            : mixed ? "Their lists differ: this is the first one's. A change gives them all the list shown."
            : follows ? "Follows your default list. A change gives it a list of its own."
            : "Has a list of its own.";
        _useDefault.Visible = ids.Length > 0;
        _useDefault.Disabled = follows && !mixed;
        _reset.Visible = _applyAll.Visible = _target == DefaultTarget;
        _reset.Disabled = list == WorkPriorities.Standard;

        string key = $"{_target}:{list}:{ids.Length}";
        if (key == _shownKey) return;
        _shownKey = key;
        BuildRows(list);
    }

    /// <summary>A worker's own list (or the default list for id 0), counting a change not yet applied.</summary>
    private ulong Raw(int id)
    {
        if (_pending.TryGetValue(id, out var p))
        {
            if (_sim.Tick <= p.Tick) return p.List;
            _pending.Remove(id);
        }
        if (id == DefaultTarget) return _sim.Automation.DefaultPriorities(PlayerControl.LocalPlayer);
        int i = _sim.Units.IndexOf(id);
        return i >= 0 ? _sim.Units.Priorities[i] : WorkPriorities.UseDefault;
    }

    /// <summary>The list shown, as rows: the kinds on it in order with up, down and off, then the kinds not on it.</summary>
    private void BuildRows(ulong list)
    {
        foreach (var child in _rows.GetChildren())
        {
            _rows.RemoveChild(child); // now, so the new buttons get the same names
            child.QueueFree();
        }
        var on = WorkPriorities.Unpack(list);
        for (int n = 0; n < on.Count; n++)
        {
            var kind = on[n];
            var row = Row();
            var label = MakeLabel($"{n + 1}.  {WorkPriorities.Label(kind)}", 16);
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(label);
            var up = SmallButton("Up", "prio_up_" + WorkPriorities.LogName(kind), "Move up");
            up.Disabled = n == 0;
            up.Pressed += () => Edit(l => WorkPriorities.Move(l, kind, -1));
            row.AddChild(up);
            var down = SmallButton("Down", "prio_down_" + WorkPriorities.LogName(kind), "Move down");
            down.Disabled = n == on.Count - 1;
            down.Pressed += () => Edit(l => WorkPriorities.Move(l, kind, +1));
            row.AddChild(down);
            var off = SmallButton("Off", "prio_toggle_" + WorkPriorities.LogName(kind), "Take it off the list: never done on its own");
            off.CustomMinimumSize = new Vector2(56, 0);
            off.Pressed += () => Edit(l => WorkPriorities.Toggle(l, kind));
            row.AddChild(off);
            _rows.AddChild(row);
        }
        if (on.Count == 0)
        {
            var none = MakeLabel("Nothing: the workers wait for your orders.", 15);
            _rows.AddChild(none);
        }

        var offKinds = new List<WorkKind>();
        foreach (var k in WorkPriorities.All)
            if (!on.Contains(k)) offKinds.Add(k);
        if (offKinds.Count == 0) return;
        var heading = MakeLabel("Not on the list", 14);
        heading.ThemeTypeVariation = UiStyle.Dim;
        _rows.AddChild(heading);
        foreach (var k in offKinds)
        {
            var kind = k;
            var row = Row();
            var label = MakeLabel(WorkPriorities.Label(kind), 15);
            label.ThemeTypeVariation = UiStyle.Dim;
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(label);
            var put = SmallButton("Add", "prio_toggle_" + WorkPriorities.LogName(kind), "Put it on the list, at the bottom");
            put.CustomMinimumSize = new Vector2(56, 0);
            put.Pressed += () => Edit(l => WorkPriorities.Toggle(l, kind));
            row.AddChild(put);
            _rows.AddChild(row);
        }
    }

    /// <summary>Changes the list shown and gives it to whoever it is for.</summary>
    private void Edit(Func<ulong, ulong> change)
    {
        var selected = _control.OwnWorkers();
        int first = _target == SelectedTarget ? (selected.Count > 0 ? selected[0] : 0) : _target;
        ulong raw = Raw(first);
        ulong list = first != DefaultTarget && raw == WorkPriorities.UseDefault ? Raw(DefaultTarget) : raw;
        Set(change(list));
    }

    private void Set(ulong list)
    {
        long tick = _sim.Tick;
        if (_target == DefaultTarget)
        {
            _control.SetDefaultPriorities(list);
            _pending[DefaultTarget] = (tick, list == WorkPriorities.UseDefault ? WorkPriorities.Standard : list);
        }
        else
        {
            var ids = _target == SelectedTarget ? _control.OwnWorkers() : new List<int> { _target };
            _control.SetPriorities(ids.ToArray(), list);
            foreach (int id in ids) _pending[id] = (tick, list);
        }
        _shownKey = "";
        Refresh();
    }

    /// <summary>A button per worker of yours, saying whether it follows the default list.</summary>
    private void RefreshWorkers()
    {
        var ids = _control.AllOwnWorkers();
        bool same = ids.Count == _workerButtons.Count;
        for (int k = 0; same && k < ids.Count; k++) same = ids[k] == _workerButtons[k].Id;
        if (!same)
        {
            foreach (var (_, b) in _workerButtons)
            {
                _workerBox.RemoveChild(b);
                b.QueueFree();
            }
            _workerButtons.Clear();
            foreach (int id in ids)
            {
                var b = TargetButton("", "prio_worker_" + id, id);
                _workerBox.AddChild(b);
                _workerButtons.Add((id, b));
            }
        }
        foreach (var (id, b) in _workerButtons)
        {
            bool own = Raw(id) != WorkPriorities.UseDefault;
            b.Text = $"{WorkerName(id)}   {(own ? "own list" : "default")}";
            b.SetPressedNoSignal(_target == id);
        }
    }

    private string WorkerName(int id)
    {
        int i = _sim.Units.IndexOf(id);
        return i < 0 ? $"Worker {id}" : $"{_sim.Content.Units[_sim.Units.Def[i]].Name} {id}";
    }

    private Button TargetButton(string text, string name, int target)
    {
        var b = new Button { Text = text, Name = name, ToggleMode = true, FocusMode = FocusModeEnum.None, Alignment = HorizontalAlignment.Left };
        b.AddThemeFontSizeOverride("font_size", 14);
        b.Pressed += () =>
        {
            _target = target;
            _shownKey = "";
            Refresh();
        };
        return b;
    }

    private static Button ActionButton(string text, string name, string tooltip)
    {
        var b = new Button { Text = text, Name = name, FocusMode = FocusModeEnum.None, TooltipText = tooltip };
        b.AddThemeFontSizeOverride("font_size", 14);
        return b;
    }

    private static Button SmallButton(string text, string name, string tooltip)
    {
        var b = new Button { Text = text, Name = name, FocusMode = FocusModeEnum.None, TooltipText = tooltip, CustomMinimumSize = new Vector2(56, 0) };
        b.AddThemeFontSizeOverride("font_size", 14);
        return b;
    }

    private static HBoxContainer Row()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        return row;
    }

    private static Label MakeLabel(string text, int size)
    {
        var label = new Label { Text = text, MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }
}
