using System.Collections.Generic;
using Godot;
using Rts.Sim;

namespace Rts.Game;

/// <summary>
/// The alerts column at the top left, under the top bar: an Idle workers button with how many of your
/// workers have nothing to do (each click selects the next one; the . key does the same), and below it
/// your latest alerts, newest first, from the simulation's event log (see <see cref="EventLog"/>): your
/// units or buildings under attack, a building finished, research done, a unit trained, a town hall lost,
/// and an enemy town hall destroyed or taken. Clicking an alert moves the camera there. An alert goes after
/// <see cref="ShowSeconds"/> seconds on screen, not counting time paused (so it does not flash by at 4x
/// speed); the same alert again soon after is counted on the one showing instead of added.
/// </summary>
public partial class Alerts : VBoxContainer
{
    /// <summary>Seconds an alert stays on screen while the game runs.</summary>
    public const double ShowSeconds = 20;
    /// <summary>Alerts shown at once; older ones drop off.</summary>
    public const int MaxShown = 5;
    /// <summary>Game seconds within which the same alert is counted on the last one rather than added.</summary>
    private const int MergeSeconds = 10;

    private static readonly Color AttackColour = new(0.62f, 0.16f, 0.14f);
    private static readonly Color GoodColour = new(0.18f, 0.45f, 0.24f);
    private static readonly Color ResearchColour = new(0.22f, 0.36f, 0.62f);

    private sealed class Alert
    {
        public required GameEvent Event;
        public required string Text;
        public required Color Colour;
        public int Count = 1;
        public long LastTick;
        public double Age;
    }

    private Simulation _sim = null!;
    private PlayerControl _control = null!;
    private Button _idleButton = null!;
    private Button _armyButton = null!;
    private readonly List<Alert> _alerts = new(); // newest first
    private readonly List<Button> _buttons = new();
    private long _lastSeq;
    private bool _dirty = true;

    public void Init(Simulation sim, PlayerControl control)
    {
        _sim = sim;
        _control = control;
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeConstantOverride("separation", 4);
        // Whatever happened before the HUD came up (a replay jumped forward) is not news.
        _lastSeq = sim.Events.NextSeq - 1;

        _idleButton = Hud.MakeButton("");
        _idleButton.Name = "idle_button";
        _idleButton.ToggleMode = false;
        _idleButton.AddThemeFontSizeOverride("font_size", 14);
        _idleButton.Alignment = HorizontalAlignment.Left;
        _idleButton.TooltipText = "Your workers with nothing to do: click (or press .) to select the next one and look at it";
        _idleButton.Pressed += () => _control.SelectNextIdleWorker();
        AddChild(_idleButton);

        _armyButton = Hud.MakeButton("");
        _armyButton.Name = "army_button";
        _armyButton.ToggleMode = false;
        _armyButton.AddThemeFontSizeOverride("font_size", 14);
        _armyButton.Alignment = HorizontalAlignment.Left;
        _armyButton.TooltipText = "Your combat units: click (or press F2) to select them all, leaving workers out";
        _armyButton.Pressed += () => _control.SelectArmy();
        AddChild(_armyButton);

        for (int k = 0; k < MaxShown; k++)
        {
            var b = Hud.MakeButton("");
            b.Name = $"alert_{k}";
            b.ToggleMode = false;
            b.Visible = false;
            b.AddThemeFontSizeOverride("font_size", 14);
            b.Alignment = HorizontalAlignment.Left;
            b.TooltipText = "Click to look there";
            int index = k;
            b.Pressed += () => Clicked(index);
            AddChild(b);
            _buttons.Add(b);
        }
    }

    private void Clicked(int index)
    {
        if (index >= _alerts.Count) return;
        var e = _alerts[index].Event;
        _control.LookAt(e.X, e.Y, e.Z);
    }

    /// <summary>Takes in new events, drops old alerts and updates the buttons. Call every frame.</summary>
    public void Refresh(double delta, bool paused)
    {
        int idle = _sim.Economy.IdleWorkers(PlayerControl.LocalPlayer).Count;
        _idleButton.Text = $"Idle workers: {idle}  [.]";
        _idleButton.Disabled = idle == 0;
        int army = _sim.Combat.ArmyUnits(PlayerControl.LocalPlayer).Count;
        _armyButton.Text = $"Army: {army}  [F2]";
        _armyButton.Disabled = army == 0;

        foreach (var e in _sim.Events.After(_lastSeq, PlayerControl.LocalPlayer)) Add(e);
        _lastSeq = _sim.Events.NextSeq - 1;
        int before = _alerts.Count;
        if (!paused)
            foreach (var a in _alerts) a.Age += delta;
        _alerts.RemoveAll(a => a.Age > ShowSeconds);
        if (_alerts.Count != before) _dirty = true;
        if (!_dirty) return;
        _dirty = false;
        for (int k = 0; k < _buttons.Count; k++)
        {
            var b = _buttons[k];
            b.Visible = k < _alerts.Count;
            if (!b.Visible) continue;
            var a = _alerts[k];
            b.Text = a.Count > 1 ? $"{a.Text}  x{a.Count}" : a.Text;
            Hud.Frame(b, a.Colour);
            b.Disabled = a.Event.X < 0;
        }
    }

    private void Add(GameEvent e)
    {
        var (text, colour) = Describe(e);
        if (text == null) return;
        _dirty = true;
        long merge = MergeSeconds * Simulation.TicksPerSecond;
        foreach (var a in _alerts)
        {
            if (a.Text != text || e.Tick - a.LastTick > merge) continue;
            a.Count++;
            a.LastTick = e.Tick;
            a.Age = 0;
            a.Event = e;
            _alerts.Remove(a);
            _alerts.Insert(0, a);
            return;
        }
        _alerts.Insert(0, new Alert { Event = e, Text = text, Colour = colour, LastTick = e.Tick });
        if (_alerts.Count > MaxShown) _alerts.RemoveRange(MaxShown, _alerts.Count - MaxShown);
    }

    private (string? Text, Color Colour) Describe(GameEvent e)
    {
        var content = _sim.Content;
        return e.Kind switch
        {
            GameEventKind.UnitsAttacked => ("Your units are under attack", AttackColour),
            GameEventKind.BuildingAttacked => ($"Your {Lower(content.Buildings[e.Def].Name)} is under attack", AttackColour),
            GameEventKind.BuildingFinished => ($"{content.Buildings[e.Def].Name} finished", GoodColour),
            GameEventKind.ResearchDone => ($"Research done: {content.Techs[e.Def].Name}", ResearchColour),
            GameEventKind.UnitTrained => ($"{content.Units[e.Def].Name} trained", Hud.PlainColour),
            GameEventKind.TownHallDestroyed => ("Your town hall was destroyed", AttackColour),
            GameEventKind.TownHallCaptured => ("Your town hall was captured", AttackColour),
            GameEventKind.EnemyTownHallDestroyed => ("You destroyed an enemy town hall", GoodColour),
            GameEventKind.EnemyTownHallTaken => ("You captured an enemy town hall", GoodColour),
            GameEventKind.TownConquered => ($"The {Lower(content.CityStates[e.Def].Name)} is yours", GoodColour),
            GameEventKind.TownLost => ($"Your {Lower(content.CityStates[e.Def].Name)} was taken", AttackColour),
            GameEventKind.TributeSent => ($"Tribute is on its way from the {Lower(content.CityStates[e.Def].Name)}", Hud.PlainColour),
            _ => (null, Hud.PlainColour),
        };
    }

    private static string Lower(string name) => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];

    /// <summary>For scripted tests: how many alerts show, and the newest one's kind.</summary>
    public string Describe() =>
        $"alerts={_alerts.Count} alert0={(_alerts.Count > 0 ? _alerts[0].Event.Kind.ToString() : "none")} " +
        $"alert_kinds={(_alerts.Count > 0 ? string.Join(',', _alerts.ConvertAll(a => a.Event.Kind.ToString())) : "none")}";
}
