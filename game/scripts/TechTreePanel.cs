using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using Rts.Sim;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Game;

/// <summary>
/// The tech tree screen (K): the local player's techs (shared ones and their faction's) in columns by
/// how many techs lead up to them, with a line from each tech to the techs it opens up. Researched techs
/// are green, the one being researched shows its progress, techs that can be started now are bright, and
/// the rest are dim. Clicking one researches it at the town hall. Each tech's button is named
/// <c>tech_ID</c> so scripted input can click it.
/// </summary>
public partial class TechTreePanel : PanelContainer
{
    public const float ButtonWidth = 206, ButtonHeight = 54, ColumnGap = 54, RowGap = 12;

    private enum State { Researched, Researching, Available, Unaffordable, Locked }

    private Simulation _sim = null!;
    private PlayerControl _control = null!;
    private TechLines _lines = null!;
    private Label _status = null!;
    private readonly List<(TechDef Tech, Button Button)> _buttons = new();
    private readonly Dictionary<State, StyleBox> _styles = new();
    private readonly Dictionary<State, StyleBox> _hover = new();

    public void Init(Simulation sim, PlayerControl control)
    {
        _sim = sim;
        _control = control;
        AddThemeStyleboxOverride("panel", UiStyle.Panel(0.97f, 18));
        MakeStyles();

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        AddChild(column);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 14);
        column.AddChild(header);
        var faction = _sim.Economy.Faction(PlayerControl.LocalPlayer);
        var title = MakeLabel($"Techs: {faction.Name}", 24);
        title.ThemeTypeVariation = UiStyle.Heading;
        header.AddChild(title);
        header.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        _status = MakeLabel("", 15);
        _status.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        header.AddChild(_status);
        var close = new Button { Text = "Close  [K]", FocusMode = FocusModeEnum.None, Name = "techs_close" };
        close.Pressed += _control.ToggleTechTree;
        header.AddChild(close);

        _lines = new TechLines { MouseFilter = MouseFilterEnum.Pass };
        column.AddChild(_lines);
        LayOut(faction);

        var hint = MakeLabel("Click a tech to research it at your town hall, one at a time. Lines lead from each tech to what it opens up.", 13);
        hint.ThemeTypeVariation = UiStyle.Dim;
        column.AddChild(hint);
        Refresh();
    }

    /// <summary>Places a button per tech: a column per depth, each tech in the row nearest the average of its prerequisites' rows.</summary>
    private void LayOut(FactionDef faction)
    {
        var techs = new List<TechDef>();
        int maxDepth = 0;
        foreach (var t in _sim.Content.Techs)
        {
            if (t.Faction != null && t.Faction != faction) continue;
            techs.Add(t);
            maxDepth = Math.Max(maxDepth, t.Depth);
        }
        var rowOf = new Dictionary<TechDef, int>();
        int rows = 0;
        for (int d = 0; d <= maxDepth; d++)
        {
            var col = techs.FindAll(t => t.Depth == d);
            var wanted = new Dictionary<TechDef, float>();
            foreach (var t in col)
            {
                float sum = 0;
                int n = 0;
                foreach (var r in t.Requires)
                    if (rowOf.TryGetValue(r, out int row)) { sum += row; n++; }
                wanted[t] = n > 0 ? sum / n : 0;
            }
            if (d > 0) col.Sort((a, b) => wanted[a] != wanted[b] ? wanted[a].CompareTo(wanted[b]) : a.Index.CompareTo(b.Index));
            var taken = new HashSet<int>();
            int next = 0;
            foreach (var t in col)
            {
                int row = d == 0 ? next++ : NearestFree(taken, (int)Math.Round(wanted[t]));
                taken.Add(row);
                rowOf[t] = row;
                rows = Math.Max(rows, row + 1);
            }
        }
        foreach (var t in techs)
        {
            var tech = t;
            var b = new Button
            {
                Name = "tech_" + t.Id,
                FocusMode = FocusModeEnum.None,
                Alignment = HorizontalAlignment.Left,
                Position = new Vector2(t.Depth * (ButtonWidth + ColumnGap), rowOf[t] * (ButtonHeight + RowGap)),
                Size = new Vector2(ButtonWidth, ButtonHeight),
                ClipText = true,
            };
            b.AddThemeFontSizeOverride("font_size", 14);
            b.Icon = UiIcons.Get(t.Icon.Length > 0 ? t.Icon : t.Id);
            b.Pressed += () => _control.Research(tech);
            _lines.AddChild(b);
            _buttons.Add((t, b));
        }
        _lines.Sim = _sim;
        _lines.Buttons = _buttons;
        _lines.CustomMinimumSize = new Vector2((maxDepth + 1) * (ButtonWidth + ColumnGap) - ColumnGap, rows * (ButtonHeight + RowGap) - RowGap);
    }

    private static int NearestFree(HashSet<int> taken, int row)
    {
        for (int k = 0; ; k++)
        {
            if (!taken.Contains(row + k)) return row + k;
            if (row - k >= 0 && !taken.Contains(row - k)) return row - k;
        }
    }

    /// <summary>Updates every button's text, colour and whether it can be clicked.</summary>
    public void Refresh()
    {
        var econ = _sim.Economy;
        int player = PlayerControl.LocalPlayer;
        var current = econ.Researching(player);
        _status.Text = current != null
            ? $"Researching {current.Name}: {100L * econ.ResearchTicks(player) / current.ResearchTicks}%"
            : econ.TownHall(player) == null ? "You need a town hall to research" : "Nothing being researched";
        foreach (var (tech, button) in _buttons)
        {
            var can = econ.CanResearch(player, tech);
            var state = econ.HasTech(player, tech) ? State.Researched
                : tech == current ? State.Researching
                : can == ResearchResult.Ok ? State.Available
                : can is ResearchResult.CannotAfford or ResearchResult.Busy or ResearchResult.NoTownHall ? State.Unaffordable
                : State.Locked;
            string second = state switch
            {
                State.Researched => "Researched",
                State.Researching => $"Researching {100L * econ.ResearchTicks(player) / tech.ResearchTicks}%",
                _ when can == ResearchResult.NotDiscovered => DiscoveryText(tech.Discovery),
                _ => $"{Hud.CostText(tech.Cost)}   {tech.ResearchTicks / Simulation.TicksPerSecond} s",
            };
            button.Text = $"{tech.Name}\n{second}";
            button.Disabled = state != State.Available;
            foreach (var slot in new[] { "normal", "disabled", "pressed", "focus" }) button.AddThemeStyleboxOverride(slot, _styles[state]);
            button.AddThemeStyleboxOverride("hover", _hover[state]);
            var text = state == State.Locked ? UiStyle.Current.DimText with { A = 0.75f } : UiStyle.Current.Text;
            button.AddThemeColorOverride("font_color", text);
            button.AddThemeColorOverride("font_disabled_color", text);
            button.AddThemeColorOverride("font_hover_color", Colors.White);
            button.AddThemeColorOverride("font_pressed_color", Colors.White);
            button.SelfModulate = state == State.Locked ? new Color(1, 1, 1, 0.8f) : Colors.White;
            button.TooltipText = Tooltip(tech, can);
        }
        _lines.QueueRedraw();
    }

    private string Tooltip(TechDef tech, ResearchResult can)
    {
        var text = new StringBuilder(tech.Description);
        foreach (var e in tech.Effects) text.Append('\n').Append(EffectText(e));
        var unlocks = _sim.Content.UnlockedBy(tech);
        foreach (var t in _sim.Content.LeadsTo(tech))
            if (t.Faction == null || t.Faction == _sim.Economy.Faction(PlayerControl.LocalPlayer)) unlocks.Add(t.Name);
        if (unlocks.Count > 0) text.Append("\nOpens up: ").Append(string.Join(", ", unlocks));
        if (tech.Requires.Count > 0)
        {
            var needs = new List<string>();
            foreach (var r in tech.Requires) needs.Add(r.Name);
            text.Append("\nNeeds: ").Append(string.Join(", ", needs));
        }
        string? why = can switch
        {
            ResearchResult.Busy => "Another tech is being researched.",
            ResearchResult.NoTownHall => "You need a finished town hall.",
            ResearchResult.CannotAfford => "Not enough goods.",
            ResearchResult.NotDiscovered => $"A discovery: {DiscoveryText(tech.Discovery)} to research it.",
            _ => null,
        };
        if (why != null) text.Append('\n').Append(why);
        return text.ToString();
    }

    /// <summary>One line saying what an effect does.</summary>
    public static string EffectText(TechEffect e) => e.Type switch
    {
        TechEffectType.Housing => e.Percent > 0 ? $"Each {e.Building?.Name.ToLowerInvariant()} houses {e.Percent}% more" : $"Each {e.Building?.Name.ToLowerInvariant()} houses {e.Amount} more",
        TechEffectType.UnitHp => e.Percent > 0 ? $"{Who(e)} have {e.Percent}% more hit points" : $"{Who(e)} have {e.Amount} more hit points",
        TechEffectType.Roads => "Roads can be laid (L)",
        TechEffectType.Salvage => "Workers take down your own buildings for half their cost back",
        TechEffectType.Reveal => e.Reveal switch
        {
            MapFeature.Ruins => "Shows every old ruin on the map",
            MapFeature.Lairs => "Shows every monster lair on the map",
            _ => "Shows every city-state town on the map",
        },
        TechEffectType.SpareWorkers => "Wild monsters leave your workers alone",
        _ => e.Type.ToString(),
    };

    /// <summary>What the player has to do to make the discovery: "Search an old ruin".</summary>
    public static string DiscoveryText(Discovery d) => d switch
    {
        Discovery.Ruin => "Search an old ruin",
        Discovery.Lair => "Clear a monster lair",
        Discovery.Town => "Take a city-state town",
        Discovery.Giant => "Beat a stone giant in a mountain pass",
        Discovery.Deeps => "Set foot in the deep caves",
        Discovery.Crystal => "Dig a block of crystal",
        _ => "",
    };

    private static string Who(TechEffect e)
    {
        if (e.Unit != null) return e.Unit.Name + "s";
        if (e.Role is not { } role) return e.Fighters ? "All fighting units" : "All units";
        string name = ContentLoader.SnakeName(role).Replace('_', ' ');
        return char.ToUpperInvariant(name[0]) + name[1..];
    }

    /// <summary>A box per state in the current look: green when researched, gold while researching, the highlight when it can be.</summary>
    private void MakeStyles()
    {
        var look = UiStyle.Current;
        void Add(State s, Color fill, Color frame, bool sunken = false)
        {
            _styles[s] = UiStyle.Box(fill, frame, sunken, 1f, 10, 6);
            _hover[s] = UiStyle.Box(fill.Lightened(0.12f), frame.Lightened(0.25f), sunken, 1f, 10, 6);
        }
        Add(State.Researched, look.ButtonFill.Lerp(new Color(0.2f, 0.5f, 0.24f), 0.65f), new Color(0.42f, 0.78f, 0.45f), sunken: true);
        Add(State.Researching, look.ButtonFill.Lerp(new Color(0.55f, 0.42f, 0.1f), 0.65f), new Color(0.95f, 0.75f, 0.3f));
        Add(State.Available, look.ButtonFill, look.Highlight);
        Add(State.Unaffordable, look.ButtonFill.Darkened(0.15f), look.Frame);
        Add(State.Locked, look.Fill.Darkened(0.3f), look.Frame.Darkened(0.35f));
    }

    private static Label MakeLabel(string text, int size)
    {
        var label = new Label { Text = text, MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }
}

/// <summary>Holds the tech tree's buttons and draws the lines between them, under the buttons.</summary>
public partial class TechLines : Control
{
    public Simulation Sim = null!;
    public List<(TechDef Tech, Button Button)> Buttons = new();

    public override void _Draw()
    {
        var at = new Dictionary<TechDef, Button>();
        foreach (var (t, b) in Buttons) at[t] = b;
        var done = new Color(0.42f, 0.78f, 0.45f, 0.95f);
        var open = UiStyle.Current.DimText with { A = 0.6f };
        foreach (var (t, b) in Buttons)
        foreach (var r in t.Requires)
        {
            if (!at.TryGetValue(r, out var from)) continue;
            var a = from.Position + new Vector2(TechTreePanel.ButtonWidth, TechTreePanel.ButtonHeight / 2);
            var z = b.Position + new Vector2(0, TechTreePanel.ButtonHeight / 2);
            // Each tech's lines turn at their own distance into the gap, so lines from different techs never share a stretch.
            int row = Mathf.RoundToInt(from.Position.Y / (TechTreePanel.ButtonHeight + TechTreePanel.RowGap));
            float mid = z.X - TechTreePanel.ColumnGap + 12 + row % 3 * 15;
            var color = Sim.Economy.HasTech(PlayerControl.LocalPlayer, r) ? done : open;
            DrawPolyline(new[] { a, new Vector2(mid, a.Y), new Vector2(mid, z.Y), z }, color, 2f, antialiased: true);
        }
    }
}
