using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Rts.Sim.AI;
using Rts.Sim.Content;

namespace Rts.Game;

/// <summary>
/// A match as the start screen sets it up: the world seed, the player you play (and so your faction and
/// start), whether a computer plays the other side and how well (see <see cref="AiLevel"/>), whether there
/// is fog of war (see <see cref="FogOfWar"/>) and whether there are day, night and weather (see
/// <see cref="Rts.Sim.Conditions"/>), and whether it is a test mode match (see <see cref="TestPanel"/>).
/// </summary>
public sealed record MatchSetup(ulong Seed, int Player, bool Computer, bool Fog, AiLevel Level = AiLevel.Normal, bool DayNight = false,
    bool TestMode = false, bool Dominion = false, bool Awakening = false);

/// <summary>
/// The start screen: pick your faction, the world seed, whether a computer player plays the other side and
/// whether there is fog of war, then start the match. Generated maps have two start areas, player 0's in the west and player 1's in the
/// east, and each player has their faction (see <see cref="ContentDb.Factions"/>), so picking a faction
/// picks the player and the start too. Shown when the game starts without command-line options (or with
/// --menu), and again from the end of a match. Its controls are named for scripted input: start_ plus the
/// faction id (start_settlers, start_orcs), start_seed, start_new_seed, start_computer, start_level, start_fog, start_daynight, start_dominion, start_test,
/// start_button and start_quit.
/// </summary>
public partial class StartScreen : CanvasLayer
{
    /// <summary>Start areas on a generated map (see <see cref="Rts.Sim.World.RegionGenerator"/>).</summary>
    private const int Starts = 2;

    /// <summary>Raised when Start match is pressed, with the choices made.</summary>
    public event Action<MatchSetup>? StartPressed;

    /// <summary>A faction with another look was picked (see <see cref="UiStyle"/>): the screen is to be made again with these choices.</summary>
    public event Action<MatchSetup>? LookChanged;

    private readonly List<Button> _factionButtons = new();
    private Control _panel = null!;
    private LineEdit _seed = null!;
    private CheckButton _computer = null!;
    private CheckButton _fog = null!;
    private CheckButton _dayNight = null!;
    private CheckButton _test = null!;
    private CheckButton _dominion = null!;
    private CheckButton _awakening = null!;
    private OptionButton _level = null!;

    /// <summary>The computer player's levels on the start screen, in the order shown.</summary>
    private static readonly (AiLevel Level, string Name, string Tip)[] Levels =
    {
        (AiLevel.Easy, "Easy", "Scouts, plays by the fog of war, trains counters to what it sees and pulls its wounded back to heal."),
        (AiLevel.Normal, "Normal", "As Easy, and focuses its fire, builds more, settles new ground, and as the Settlers walls its town hall."),
        (AiLevel.Hard, "Hard", "The best it can play with no more than you have: it knows only what it has seen and spends only what it gathers. A big town and an army kept ahead of yours. As the Orcs it attacks in big waves, as the Settlers from a walled town."),
        (AiLevel.Classic, "Classic", "The first computer player: it sees through the fog of war and fights simply."),
    };
    private Label _loading = null!;
    private int _player;
    private ContentDb _content = null!;

    private FactionDef FactionOf(int player) => _content.Factions[player % _content.Factions.Count];

    public void Init(ContentDb content, MatchSetup setup)
    {
        Layer = 10;
        _player = Math.Clamp(setup.Player, 0, Starts - 1);
        _content = content;
        UiStyle.UseFor(FactionOf(_player));

        var look = UiStyle.Current;
        var backdrop = new Panel();
        backdrop.AddThemeStyleboxOverride("panel", UiStyle.Box(look.Fill.Darkened(0.45f), look.Fill.Darkened(0.45f)));
        backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        backdrop.Theme = UiStyle.Theme;
        AddChild(backdrop);
        var center = new CenterContainer { Theme = UiStyle.Theme };
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(center);

        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", UiStyle.Panel(1f, 28));
        center.AddChild(panel);
        _panel = panel;
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 16);
        panel.AddChild(column);

        var title = Hud.MakeLabel("RTS", 56);
        title.ThemeTypeVariation = UiStyle.Heading;
        title.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(title);

        column.AddChild(Hud.MakeLabel("Play as", 16));
        var factions = new HBoxContainer();
        factions.AddThemeConstantOverride("separation", 14);
        column.AddChild(factions);
        for (int p = 0; p < Starts; p++)
        {
            var faction = content.Factions[p % content.Factions.Count];
            var b = FactionButton(faction, p);
            factions.AddChild(b);
            _factionButtons.Add(b);
        }
        ShowChoice();

        var seedRow = new HBoxContainer();
        seedRow.AddThemeConstantOverride("separation", 10);
        column.AddChild(seedRow);
        var seedLabel = Hud.MakeLabel("World seed", 16);
        seedLabel.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        seedRow.AddChild(seedLabel);
        _seed = new LineEdit
        {
            Name = "start_seed",
            Text = setup.Seed.ToString(CultureInfo.InvariantCulture),
            MaxLength = 18,
            CustomMinimumSize = new Vector2(180, 0),
            TooltipText = "The same seed makes the same world",
        };
        _seed.AddThemeFontSizeOverride("font_size", 16);
        _seed.TextChanged += KeepDigits;
        _seed.TextSubmitted += _ => PressStart();
        seedRow.AddChild(_seed);
        var newSeed = Hud.MakeButton("New seed");
        newSeed.Name = "start_new_seed";
        newSeed.ToggleMode = false;
        Hud.Frame(newSeed, Hud.PlainColour);
        newSeed.Pressed += () => _seed.Text = RandomSeed().ToString(CultureInfo.InvariantCulture);
        seedRow.AddChild(newSeed);

        _computer = new CheckButton
        {
            Name = "start_computer",
            Text = "Computer opponent",
            ButtonPressed = setup.Computer,
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            TooltipText = "A computer player plays the other side. Off: the other side stays where it starts and does nothing.",
        };
        _computer.AddThemeFontSizeOverride("font_size", 16);
        column.AddChild(_computer);

        var levelRow = new HBoxContainer();
        levelRow.AddThemeConstantOverride("separation", 10);
        levelRow.AddChild(Hud.MakeLabel("Computer level", 16));
        _level = new OptionButton { Name = "start_level", FocusMode = Control.FocusModeEnum.None };
        _level.AddThemeFontSizeOverride("font_size", 16);
        for (int k = 0; k < Levels.Length; k++)
        {
            _level.AddItem(Levels[k].Name, k);
            _level.SetItemTooltip(k, Levels[k].Tip);
            if (Levels[k].Level == setup.Level) _level.Select(k);
        }
        levelRow.AddChild(_level);
        column.AddChild(levelRow);
        _computer.Toggled += on => _level.Disabled = !on;
        _level.Disabled = !setup.Computer;

        _fog = new CheckButton
        {
            Name = "start_fog",
            Text = "Fog of war",
            ButtonPressed = setup.Fog,
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            TooltipText = "You see only what your units and buildings can see: ground you have not explored is dark, " +
                          "and ground you have seen shows as it was when you last saw it. Off: you see the whole map.",
        };
        _fog.AddThemeFontSizeOverride("font_size", 16);
        column.AddChild(_fog);

        _dayNight = new CheckButton
        {
            Name = "start_daynight",
            Text = "Day, night and weather",
            ButtonPressed = setup.DayNight,
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            TooltipText = "Night, rain, storms and fog shorten how far units see, and archers and mages miss more. " +
                          "Lanterns, torches and campfires light the ground so it is seen and shot at as by day. " +
                          "Off: it is always a clear day.",
        };
        _dayNight.AddThemeFontSizeOverride("font_size", 16);
        column.AddChild(_dayNight);

        _dominion = new CheckButton
        {
            Name = "start_dominion",
            Text = "Win by holding the ancient sites",
            ButtonPressed = setup.Dominion,
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            TooltipText = "The old ruins are ancient sites. Soldiers standing alone in one for 15 seconds take it, and it stays yours " +
                          "until a rival takes it. Hold more than half of them for 3 minutes and you win. " +
                          "Off: only knocking down every rival town hall wins.",
        };
        _dominion.AddThemeFontSizeOverride("font_size", 16);
        column.AddChild(_dominion);

        _awakening = new CheckButton
        {
            Name = "start_awakening",
            Text = "The dark power wakes",
            ButtonPressed = setup.Awakening,
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            TooltipText = "Something sleeps under the Heartscar, at the heart of the land. It wakes 45 minutes in, sooner if you dig " +
                          "near it, and grows twice more: corrupted ground spreads from it, and broods of corrupted beasts claw up " +
                          "and come for every town hall. Off: it sleeps all match.",
        };
        _awakening.AddThemeFontSizeOverride("font_size", 16);
        column.AddChild(_awakening);

        _test = new CheckButton
        {
            Name = "start_test",
            Text = "Test mode",
            ButtonPressed = setup.TestMode,
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            TooltipText = "For trying things out: a test panel to put any unit, building, tree, block, lair or blood where you click, " +
                          "cheats, weather and time of day, single ticks, and saving and loading scenarios. F1 hides the panel.",
        };
        _test.AddThemeFontSizeOverride("font_size", 16);
        column.AddChild(_test);

        var start = Hud.MakeButton("Start match");
        start.Name = "start_button";
        start.ToggleMode = false;
        start.AddThemeFontSizeOverride("font_size", 22);
        start.CustomMinimumSize = new Vector2(260, 52);
        start.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        Hud.Frame(start, Hud.AccentColour);
        start.Pressed += PressStart;
        column.AddChild(start);

        var hint = Hud.MakeLabel("Win by destroying the other side's town hall.", 14);
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.ThemeTypeVariation = UiStyle.Dim;
        column.AddChild(hint);

        var quit = Hud.MakeButton("Quit");
        quit.Name = "start_quit";
        quit.ToggleMode = false;
        quit.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        quit.CustomMinimumSize = new Vector2(120, 0);
        Hud.Frame(quit, Hud.PlainColour);
        quit.Pressed += () => GetTree().Quit();
        column.AddChild(quit);

        _loading = Hud.MakeLabel("", 26);
        _loading.Visible = false;
        center.AddChild(_loading);
    }

    /// <summary>Hides the choices and says the world is being made (shown for a frame or two before the match starts).</summary>
    public void ShowLoading(ulong seed)
    {
        _panel.Visible = false;
        _loading.Text = $"Making world {seed}…";
        _loading.Visible = true;
    }

    /// <summary>The choices as they stand, for scripted tests.</summary>
    public string Describe() =>
        $"player={_player} seed={(_seed.Text.Length > 0 ? _seed.Text : "random")} computer={(_computer.ButtonPressed ? 1 : 0)} " +
        $"level={SelectedLevel.ToString().ToLowerInvariant()} fog={(_fog.ButtonPressed ? 1 : 0)} test={(_test.ButtonPressed ? 1 : 0)} dominion={(_dominion.ButtonPressed ? 1 : 0)} awakening={(_awakening.ButtonPressed ? 1 : 0)}";

    private AiLevel SelectedLevel => _level.Selected >= 0 ? Levels[_level.Selected].Level : AiLevel.Normal;

    /// <summary>A seed for a new match: short enough to read out and type in.</summary>
    public static ulong RandomSeed() => (ulong)Random.Shared.Next(1, 100000);

    private void PressStart() => StartPressed?.Invoke(CurrentSetup());

    /// <summary>The match the choices set up (a random seed if the seed box is empty).</summary>
    private MatchSetup CurrentSetup()
    {
        ulong seed = ulong.TryParse(_seed.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var s) ? s : RandomSeed();
        return new MatchSetup(seed, _player, _computer.ButtonPressed, _fog.ButtonPressed, SelectedLevel, _dayNight.ButtonPressed, _test.ButtonPressed,
            _dominion.ButtonPressed, _awakening.ButtonPressed);
    }

    private Button FactionButton(FactionDef faction, int player)
    {
        var team = UnitRenderer.TeamColors[player];
        var b = new Button
        {
            Name = "start_" + faction.Id,
            ToggleMode = true,
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(300, 124),
            TooltipText = $"Play the {faction.Name}, player {player}",
        };
        b.AddThemeStyleboxOverride("normal", FactionStyle(team, chosen: false, hover: false));
        b.AddThemeStyleboxOverride("hover", FactionStyle(team, chosen: false, hover: true));
        b.AddThemeStyleboxOverride("pressed", FactionStyle(team, chosen: true, hover: false));
        b.AddThemeStyleboxOverride("hover_pressed", FactionStyle(team, chosen: true, hover: true));
        b.Pressed += () =>
        {
            _player = player;
            ShowChoice();
            if (UiStyle.For(FactionOf(player)) != UiStyle.Current) LookChanged?.Invoke(CurrentSetup());
        };

        var inner = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        inner.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, Control.LayoutPresetMode.Minsize, 12);
        inner.AddThemeConstantOverride("separation", 4);
        b.AddChild(inner);
        var name = Hud.MakeLabel(faction.Name, 24);
        name.ThemeTypeVariation = UiStyle.Heading;
        name.AddThemeColorOverride("font_color", team.Lightened(0.45f));
        inner.AddChild(name);
        var description = Hud.MakeLabel(faction.Description, 14);
        description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        inner.AddChild(description);
        var where = Hud.MakeLabel(player == 0 ? "Starts in the west" : "Starts in the east", 13);
        where.ThemeTypeVariation = UiStyle.Dim;
        inner.AddChild(where);
        return b;
    }

    /// <summary>Shows which faction is picked (one at a time).</summary>
    private void ShowChoice()
    {
        for (int p = 0; p < _factionButtons.Count; p++) _factionButtons[p].SetPressedNoSignal(p == _player);
    }

    private static StyleBox FactionStyle(Color team, bool chosen, bool hover)
    {
        var look = UiStyle.Current;
        var fill = chosen ? look.ButtonFill.Lerp(team, 0.3f) : hover ? look.ButtonFill.Lightened(0.1f) : look.ButtonFill;
        var frame = chosen ? team.Lightened(0.25f) : look.Frame;
        return UiStyle.Box(fill, frame, sunken: chosen, margin: 0);
    }

    /// <summary>Only digits in the seed box.</summary>
    private void KeepDigits(string text)
    {
        var digits = new System.Text.StringBuilder(text.Length);
        foreach (char c in text)
            if (c is >= '0' and <= '9') digits.Append(c);
        if (digits.Length == text.Length) return;
        _seed.Text = digits.ToString();
        _seed.CaretColumn = digits.Length;
    }
}
