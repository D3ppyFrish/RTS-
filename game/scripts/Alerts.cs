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
        /// <summary>More to read on hovering it (what was written in a ruin), or null.</summary>
        public string? More;
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
        // A great work stands in the deep caves: the height slice cuts the land over it away.
        if (e.Kind is GameEventKind.GreatWorkBegun or GameEventKind.GreatWorkFinished or GameEventKind.GreatWorkLost) _control.FocusAt(e.X, e.Y, e.Z);
        else _control.LookAt(e.X, e.Y, e.Z);
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
            b.TooltipText = a.More != null ? $"{a.More}\nClick to look there" : "Click to look there";
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
        _alerts.Insert(0, new Alert { Event = e, Text = text, Colour = colour, LastTick = e.Tick, More = MoreOf(e) });
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
            GameEventKind.TechDiscovered => ($"Discovery: {content.Techs[e.Def].Name} can be researched", ResearchColour),
            GameEventKind.UnitTrained => ($"{content.Units[e.Def].Name} trained", Hud.PlainColour),
            GameEventKind.TownHallDestroyed => ("Your town hall was destroyed", AttackColour),
            GameEventKind.TownHallCaptured => ("Your town hall was captured", AttackColour),
            GameEventKind.EnemyTownHallDestroyed => ("You destroyed an enemy town hall", GoodColour),
            GameEventKind.EnemyTownHallTaken => ("You captured an enemy town hall", GoodColour),
            GameEventKind.TownConquered => ($"The {Lower(content.CityStates[e.Def].Name)} is yours", GoodColour),
            GameEventKind.TownLost => ($"Your {Lower(content.CityStates[e.Def].Name)} was taken", AttackColour),
            GameEventKind.TributeSent => ($"Tribute is on its way from the {Lower(content.CityStates[e.Def].Name)}", Hud.PlainColour),
            GameEventKind.UnitLostToHazard => ($"Your {Lower(content.Units[e.Def].Name)} {HazardDeath(e)}", AttackColour),
            GameEventKind.RuinSearched => (RuinFound(e), GoodColour),
            GameEventKind.SiteTaken => ($"You took an ancient site: you hold {_sim.Sites.Held(e.Player)} of {_sim.Ruins.Ruins.Count}", GoodColour),
            GameEventKind.SiteLost => ($"The {Faction(e.Subject)} took one of your ancient sites", AttackColour),
            GameEventKind.DominionStarted => e.Subject == e.Player
                ? ($"You hold most of the ancient sites: hold them {Minutes(e.Def)} minutes to win", GoodColour)
                : ($"The {Faction(e.Subject)} hold most of the ancient sites: they win in {Minutes(e.Def)} minutes", AttackColour),
            GameEventKind.GreatWorkBegun => e.Subject == e.Player
                ? ($"You began the {GreatWork(e)} in the deep caves", GoodColour)
                : ($"The {Faction(e.Subject)} began their {GreatWork(e)} in the deep caves", AttackColour),
            GameEventKind.GreatWorkFinished => e.Subject == e.Player
                ? ($"Your {GreatWork(e)} is finished: keep it standing {Minutes(Rts.Sim.World.AscendancySystem.AscendancyTicks)} minutes to win", GoodColour)
                : ($"The {Faction(e.Subject)} finished their {GreatWork(e)}: bring it down within {Minutes(Rts.Sim.World.AscendancySystem.AscendancyTicks)} minutes or they win", AttackColour),
            GameEventKind.GreatWorkLost => e.Subject == e.Player
                ? ($"Your {GreatWork(e)} was brought down", AttackColour)
                : ($"The {Faction(e.Subject)}' {GreatWork(e)} was brought down", GoodColour),
            _ => (null, Hud.PlainColour),
        };
    }

    private string GreatWork(GameEvent e) => e.Def >= 0 ? _sim.Content.Buildings[e.Def].Name : "great work";

    private string Faction(int player) => _sim.Economy.Player(player).Faction.Name;

    private static int Minutes(int ticks) => ticks / (60 * Simulation.TicksPerSecond);

    /// <summary>What your units found in a ruin: "Found a sealed granary in the ruin: 200 food, 60 wood".</summary>
    private string? RuinFound(GameEvent e)
    {
        if (e.Def < 0 || e.Def >= _sim.Ruins.Ruins.Count || _sim.Ruins.Ruins[e.Def].Find is not { } find) return "Your units searched the ruin";
        var parts = new List<string>();
        if (find.Goods.Length > 0) parts.Add(Hud.CostText(find.Goods));
        if (find.Relic is { } relic) parts.Add($"the {relic.Name}");
        return $"Found {find.Name} in the ruin: {string.Join(" and ", parts)}";
    }

    /// <summary>What to read on hovering the event's alert: the words found in a ruin.</summary>
    private string? MoreOf(GameEvent e) =>
        e.Kind == GameEventKind.RuinSearched && e.Def >= 0 && e.Def < _sim.Ruins.Ruins.Count
        && _sim.Ruins.Ruins[e.Def].Find is { Lore.Length: > 0 } find ? find.Lore : null;

    /// <summary>What the ground did, read from where the unit died: water over its head means a flood, stone rubble on it falling rocks, the crust gone a sinkhole.</summary>
    private string HazardDeath(GameEvent e)
    {
        var below = _sim.World.Get(e.X, e.Y - 1, e.Z);
        if (_sim.World.Get(e.X, e.Y + 1, e.Z) == BlockType.Water) return "drowned in a flood";
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
            if (_sim.World.Get(e.X + dx, e.Y + dy, e.Z + dz) == BlockType.StoneRubble) return "was crushed by falling rocks";
        if (below == BlockType.Bog) return "died in the poison bog";
        if (below == BlockType.Quicksand) return "was dragged under by quicksand";
        if (Rts.Sim.World.HazardSystem.DamageAt(_sim.World, e.X, e.Y, e.Z) >= Rts.Sim.World.HazardSystem.LavaDamage) return "burned beside the lava";
        return "fell into a sinkhole";
    }

    private static string Lower(string name) => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];

    /// <summary>For scripted tests: how many alerts show, and the newest one's kind.</summary>
    public string Describe() =>
        $"alerts={_alerts.Count} alert0={(_alerts.Count > 0 ? _alerts[0].Event.Kind.ToString() : "none")} " +
        $"alert_kinds={(_alerts.Count > 0 ? string.Join(',', _alerts.ConvertAll(a => a.Event.Kind.ToString())) : "none")}";
}
