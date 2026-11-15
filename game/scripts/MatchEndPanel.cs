using System;
using Godot;

namespace Rts.Game;

/// <summary>
/// The end of a match: Victory, Defeat or a draw, how it came about, the match time and seed, and where its
/// replay was saved (or why it was not), with a button that opens the replays folder. New match goes back to
/// the start screen, Look around closes the panel (the HUD's Results button opens it again) and Quit leaves
/// the game. Its buttons are named end_new, end_look, end_quit and end_open_folder for scripted input.
/// </summary>
public partial class MatchEndPanel : CanvasLayer
{
    public static readonly Color VictoryColor = new(1f, 0.82f, 0.3f);
    public static readonly Color DefeatColor = new(0.95f, 0.36f, 0.3f);
    public static readonly Color DrawColor = new(0.85f, 0.87f, 0.9f);

    /// <summary>New match was pressed.</summary>
    public event Action? NewMatchPressed;
    /// <summary>Look around was pressed: the panel is hidden.</summary>
    public event Action? Closed;

    /// <param name="title">Victory, Defeat, Draw or Match over.</param>
    /// <param name="summary">How it came about, e.g. "The Orcs surrendered."</param>
    /// <param name="details">The match time and seed.</param>
    /// <param name="replayPath">Where the replay was saved, or null.</param>
    /// <param name="noReplay">Why there is no replay, when there is none.</param>
    public void Init(string title, Color color, string summary, string details, string? replayPath, string noReplay)
    {
        Layer = 5;
        var backdrop = new ColorRect { Color = new Color(0, 0, 0, 0.45f) };
        backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(backdrop);
        var center = new CenterContainer { Theme = UiStyle.Theme };
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(center);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(560, 0) };
        panel.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.Current.Fill, color.Darkened(0.2f), alpha: 0.96f, margin: 26));
        center.AddChild(panel);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        panel.AddChild(column);

        var heading = Hud.MakeLabel(title, 60);
        heading.Name = "end_title";
        heading.HorizontalAlignment = HorizontalAlignment.Center;
        heading.ThemeTypeVariation = UiStyle.Heading;
        heading.AddThemeColorOverride("font_color", color);
        column.AddChild(heading);
        column.AddChild(Centered(Hud.MakeLabel(summary, 20)));
        var info = Centered(Hud.MakeLabel(details, 15));
        info.ThemeTypeVariation = UiStyle.Dim;
        column.AddChild(info);

        if (replayPath != null)
        {
            var saved = Centered(Hud.MakeLabel($"Replay saved as {System.IO.Path.GetFileName(replayPath)}", 14));
            saved.Name = "end_replay";
            saved.MouseFilter = Control.MouseFilterEnum.Pass;
            saved.TooltipText = replayPath + "\nPlay it back with the match runner: dotnet run --project tools/MatchRunner -- --log=FILE";
            column.AddChild(saved);
            var open = MakeButton("end_open_folder", "Open the replays folder", () =>
                OS.ShellOpen(System.IO.Path.GetDirectoryName(replayPath) ?? replayPath), Hud.PlainColour);
            open.AddThemeFontSizeOverride("font_size", 14);
            open.CustomMinimumSize = Vector2.Zero;
            open.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
            column.AddChild(open);
        }
        else
        {
            var none = Centered(Hud.MakeLabel($"No replay: {noReplay}.", 14));
            none.ThemeTypeVariation = UiStyle.Dim;
            column.AddChild(none);
        }

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 14);
        column.AddChild(buttons);
        buttons.AddChild(MakeButton("end_new", "New match", () => NewMatchPressed?.Invoke(), Hud.AccentColour));
        buttons.AddChild(MakeButton("end_look", "Look around", () =>
        {
            Visible = false;
            Closed?.Invoke();
        }, Hud.PlainColour));
        buttons.AddChild(MakeButton("end_quit", "Quit", () => GetTree().Quit(), Hud.PlainColour));
    }

    private static Label Centered(Label label)
    {
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.CustomMinimumSize = new Vector2(500, 0);
        return label;
    }

    private static Button MakeButton(string name, string text, Action pressed, Color colour)
    {
        var b = Hud.MakeButton(text);
        b.Name = name;
        b.ToggleMode = false;
        b.CustomMinimumSize = new Vector2(150, 42);
        b.AddThemeFontSizeOverride("font_size", 18);
        Hud.Frame(b, colour);
        b.Pressed += pressed;
        return b;
    }
}
