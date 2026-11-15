using System;
using System.Collections.Generic;
using Godot;

namespace Rts.Game;

/// <summary>
/// The UI's look (issue #46): one theme for every panel, button and label, set on the root control of the
/// start screen, the HUD (with its tech tree) and the end of match panel, so they all share it. (A theme on
/// the window would not reach them: they sit in canvas layers, which a theme does not pass through.) Each
/// faction has its own look, named by <c>uiLook</c> in <c>data/factions.json</c>: the start screen takes the
/// look of the faction picked and a match that of the faction played; <c>--ui-look=NAME</c> uses one look
/// throughout. The looks:
/// <list type="bullet">
/// <item><b>Timber</b>: dark planks framed in lighter wood, parchment text, gold highlights and Cinzel headings, like the harbour town (the Orcs').</item>
/// <item><b>Stone</b>: grey castle masonry with bevelled edges, bronze highlights and blackletter-ish headings, like the castle siege (the Settlers').</item>
/// <item><b>Crystal</b>: dark slate with violet trim, glowing cyan highlights, pixel-font headings and angular text, like the crystal rift (no faction yet).</item>
/// </list>
/// Panels and buttons are small pixel-art pictures painted in code (plank, brick or slate patterns inside a
/// bevelled frame) at two screen pixels per picture pixel, so they match the blocky world, and tiled to any
/// size. Other code asks for boxes in the current look with <see cref="Box"/> (e.g. the tech tree's coloured
/// states) and uses the type variations <see cref="Heading"/>, <see cref="Dim"/>, <see cref="Accent"/> and
/// <see cref="Bar"/>.
/// </summary>
public static class UiStyle
{
    /// <summary>Theme type variation for titles: the heading font in the highlight colour.</summary>
    public const string Heading = "HeadingLabel";
    /// <summary>Theme type variation for hints and secondary text.</summary>
    public const string Dim = "DimLabel";
    /// <summary>Theme type variation for the button that starts or confirms something.</summary>
    public const string Accent = "AccentButton";
    /// <summary>Theme type variation for the panel along the top of the screen.</summary>
    public const string Bar = "BarPanel";

    public enum Pattern { Planks, Bricks, Slate }

    public sealed record Look(
        string Id, string Name, Pattern Pattern,
        Color Fill, Color Frame, Color ButtonFill, Color AccentFill, Color Text, Color DimText, Color Highlight,
        string BodyFont, int BodyWeight, string HeadingFont, int HeadingWeight, int FontSize);

    public static readonly Look[] Looks =
    {
        new("timber", "Timber", Pattern.Planks,
            Fill: new Color(0.22f, 0.15f, 0.1f), Frame: new Color(0.5f, 0.35f, 0.2f),
            ButtonFill: new Color(0.34f, 0.24f, 0.15f), AccentFill: new Color(0.52f, 0.17f, 0.13f),
            Text: new Color(0.97f, 0.91f, 0.77f), DimText: new Color(0.82f, 0.74f, 0.6f), Highlight: new Color(0.96f, 0.76f, 0.33f),
            BodyFont: "AlegreyaSans-Medium", BodyWeight: 400, HeadingFont: "Cinzel", HeadingWeight: 700, FontSize: 17),
        new("stone", "Stone", Pattern.Bricks,
            Fill: new Color(0.28f, 0.29f, 0.31f), Frame: new Color(0.47f, 0.48f, 0.5f),
            ButtonFill: new Color(0.36f, 0.37f, 0.39f), AccentFill: new Color(0.19f, 0.32f, 0.57f),
            Text: new Color(0.96f, 0.96f, 0.93f), DimText: new Color(0.78f, 0.79f, 0.8f), Highlight: new Color(0.88f, 0.72f, 0.42f),
            BodyFont: "AlegreyaSans-Medium", BodyWeight: 400, HeadingFont: "MedievalSharp", HeadingWeight: 400, FontSize: 17),
        new("crystal", "Crystal", Pattern.Slate,
            Fill: new Color(0.11f, 0.1f, 0.15f), Frame: new Color(0.33f, 0.28f, 0.46f),
            ButtonFill: new Color(0.19f, 0.18f, 0.26f), AccentFill: new Color(0.34f, 0.22f, 0.56f),
            Text: new Color(0.94f, 0.94f, 0.99f), DimText: new Color(0.72f, 0.72f, 0.84f), Highlight: new Color(0.47f, 0.88f, 0.97f),
            BodyFont: "ChakraPetch-Medium", BodyWeight: 400, HeadingFont: "PixelifySans", HeadingWeight: 400, FontSize: 16),
    };

    /// <summary>The look used when none has been chosen.</summary>
    public const string DefaultLook = "timber";

    /// <summary>Screen pixels per picture pixel in panels and buttons.</summary>
    private const int Px = 2;
    /// <summary>The repeating part of each pattern, in picture pixels.</summary>
    private const int Tile = 24;

    public static Look Current { get; private set; } = Looks[0];
    public static Theme Theme { get; private set; } = new();

    private static readonly Dictionary<string, Font> Fonts = new();

    public static Look Find(string id) => Array.Find(Looks, l => l.Id == id) ?? Looks[0];

    /// <summary>The look given with --ui-look, which every screen uses instead of the faction's.</summary>
    public static string? Override { get; set; }

    /// <summary>Switches to the look of the faction being played (or picked on the start screen); screens made after this use it.</summary>
    public static void UseFor(Rts.Sim.Content.FactionDef faction) =>
        Use(Find(Override ?? (faction.UiLook.Length > 0 ? faction.UiLook : DefaultLook)));

    /// <summary>The look <see cref="UseFor"/> would pick for <paramref name="faction"/>.</summary>
    public static Look For(Rts.Sim.Content.FactionDef faction) =>
        Find(Override ?? (faction.UiLook.Length > 0 ? faction.UiLook : DefaultLook));

    private static void Use(Look look)
    {
        if (look == Current && Themes.ContainsKey(look.Id)) return;
        Current = look;
        if (!Themes.TryGetValue(look.Id, out var theme)) Themes[look.Id] = theme = Build(look);
        Theme = theme;
    }

    private static readonly Dictionary<string, Theme> Themes = new();

    /// <summary>A panel or button box in the current look, filled with <paramref name="fill"/> and framed in <paramref name="frame"/>.</summary>
    /// <param name="sunken">Lit from below rather than above, as for a pressed button or a text field.</param>
    /// <param name="margin">Space between the frame and the content, in screen pixels (left and right).</param>
    /// <param name="marginY">Space above and below the content, if not <paramref name="margin"/>.</param>
    public static StyleBox Box(Color fill, Color frame, bool sunken = false, float alpha = 1f, int margin = 8, int marginY = -1) =>
        Box(Current, fill, frame, sunken, alpha, margin, marginY < 0 ? margin : marginY);

    /// <summary>The usual panel of the current look, <paramref name="alpha"/> opaque.</summary>
    public static StyleBox Panel(float alpha = 0.93f, int margin = 10) =>
        Box(Current, Current.Fill, Current.Frame, false, alpha, margin, margin);

    /// <summary>The heading font of the current look.</summary>
    public static Font HeadingFont => Font(Current.HeadingFont, Current.HeadingWeight);

    private static Theme Build(Look look)
    {
        var t = new Theme
        {
            DefaultFont = Font(look.BodyFont, look.BodyWeight),
            DefaultFontSize = look.FontSize,
        };
        var shadow = new Color(0, 0, 0, 0.6f);

        // Labels: light text with a soft shadow, so they read over the world too.
        t.SetColor("font_color", "Label", look.Text);
        t.SetColor("font_shadow_color", "Label", shadow);
        t.SetConstant("shadow_offset_x", "Label", 1);
        t.SetConstant("shadow_offset_y", "Label", 1);
        t.SetTypeVariation(Heading, "Label");
        t.SetFont("font", Heading, Font(look.HeadingFont, look.HeadingWeight));
        t.SetColor("font_color", Heading, look.Highlight);
        t.SetTypeVariation(Dim, "Label");
        t.SetColor("font_color", Dim, look.DimText);

        // Panels.
        t.SetStylebox("panel", "PanelContainer", Box(look, look.Fill, look.Frame, false, 0.93f, 10, 10));
        t.SetStylebox("panel", "Panel", Box(look, look.Fill, look.Frame, false, 0.93f, 10, 10));
        t.SetTypeVariation(Bar, "PanelContainer");
        t.SetStylebox("panel", Bar, Box(look, look.Fill, look.Frame, false, 0.9f, 12, 6));
        t.SetStylebox("panel", "TooltipPanel", Box(look, look.Fill.Darkened(0.2f), look.Frame, false, 0.97f, 10, 8));
        t.SetColor("font_color", "TooltipLabel", look.Text);
        t.SetColor("font_shadow_color", "TooltipLabel", new Color(0, 0, 0, 0));

        // Buttons: raised when up, sunken and tinted with the highlight when pressed (or switched on).
        ButtonStyles(t, "Button", look, look.ButtonFill, look.Frame);
        t.SetTypeVariation(Accent, "Button");
        ButtonStyles(t, Accent, look, look.AccentFill, look.AccentFill.Lightened(0.35f));
        foreach (var type in new[] { "Button", "CheckButton", "CheckBox" })
        {
            t.SetColor("font_color", type, look.Text);
            t.SetColor("font_hover_color", type, Colors.White);
            t.SetColor("font_pressed_color", type, look.Highlight.Lightened(0.3f));
            t.SetColor("font_hover_pressed_color", type, look.Highlight.Lightened(0.45f));
            t.SetColor("font_focus_color", type, look.Text);
            t.SetColor("font_disabled_color", type, look.DimText with { A = 0.55f });
            t.SetColor("font_outline_color", type, shadow);
        }
        t.SetColor("font_pressed_color", Accent, Colors.White);
        t.SetColor("font_hover_pressed_color", Accent, Colors.White);
        foreach (var slot in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled", "focus" })
        {
            t.SetStylebox(slot, "CheckButton", new StyleBoxEmpty());
            t.SetStylebox(slot, "CheckBox", new StyleBoxEmpty());
        }

        // Text fields and sliders: sunken wells.
        var well = Box(look, look.Fill.Darkened(0.45f), look.Frame, true, 1f, 10, 6);
        t.SetStylebox("normal", "LineEdit", well);
        t.SetStylebox("focus", "LineEdit", Box(look, look.Fill.Darkened(0.45f), look.Highlight, true, 1f, 10, 6));
        t.SetStylebox("read_only", "LineEdit", well);
        t.SetColor("font_color", "LineEdit", look.Text);
        t.SetColor("caret_color", "LineEdit", look.Highlight);
        t.SetColor("selection_color", "LineEdit", look.Highlight with { A = 0.35f });
        var groove = new StyleBoxFlat { BgColor = look.Fill.Darkened(0.55f), BorderColor = look.Frame.Darkened(0.3f) };
        groove.SetBorderWidthAll(Px);
        groove.SetContentMarginAll(3);
        t.SetStylebox("slider", "VSlider", groove);
        var fill = new StyleBoxFlat { BgColor = look.Highlight with { A = 0.55f } };
        fill.SetContentMarginAll(3);
        t.SetStylebox("grabber_area", "VSlider", fill);
        t.SetStylebox("grabber_area_highlight", "VSlider", fill);
        t.SetIcon("grabber", "VSlider", Knob(look, look.Highlight));
        t.SetIcon("grabber_highlight", "VSlider", Knob(look, look.Highlight.Lightened(0.3f)));
        return t;
    }

    private static void ButtonStyles(Theme t, string type, Look look, Color fill, Color frame)
    {
        t.SetStylebox("normal", type, Box(look, fill, frame, false, 1f, 12, 6));
        t.SetStylebox("hover", type, Box(look, fill.Lightened(0.14f), frame.Lightened(0.15f), false, 1f, 12, 6));
        var on = fill.Lerp(look.Highlight, 0.22f).Darkened(0.1f);
        t.SetStylebox("pressed", type, Box(look, on, look.Highlight.Darkened(0.15f), true, 1f, 12, 6));
        t.SetStylebox("hover_pressed", type, Box(look, on.Lightened(0.1f), look.Highlight, true, 1f, 12, 6));
        t.SetStylebox("disabled", type, Box(look, fill.Darkened(0.35f), frame.Darkened(0.35f), false, 0.85f, 12, 6));
        t.SetStylebox("focus", type, new StyleBoxEmpty());
    }

    private static Font Font(string name, int weight)
    {
        string key = $"{name}:{weight}";
        if (Fonts.TryGetValue(key, out var font)) return font;
        // Read the font file itself when it is there (running from the project), so it works before the
        // editor has imported it; an exported game has only the imported font.
        string path = $"res://fonts/{name}.ttf";
        FontFile? file = null;
        if (FileAccess.FileExists(path)) file = new FontFile { Data = FileAccess.GetFileAsBytes(path) };
        else if (ResourceLoader.Exists(path)) file = ResourceLoader.Load<FontFile>(path);
        if (file == null)
        {
            GD.PrintErr($"UI font {path} is missing; using the default font");
            return Fonts[key] = ThemeDB.FallbackFont;
        }
        if (name.StartsWith("Pixel"))
        {
            // A pixel font stays crisp only without smoothing and on whole pixels.
            file.Antialiasing = TextServer.FontAntialiasing.None;
            file.Hinting = TextServer.Hinting.None;
            file.SubpixelPositioning = TextServer.SubpixelPositioning.Disabled;
        }
        var variation = new FontVariation { BaseFont = file };
        if (weight != 400) variation.VariationOpentype = new Godot.Collections.Dictionary { { "wght", weight } };
        variation.Fallbacks = new Godot.Collections.Array<Font> { ThemeDB.FallbackFont };
        Fonts[key] = variation;
        return variation;
    }

    /// <summary>
    /// A box painted as pixel art: a dark outline with clipped corners, a two pixel frame lit from the top left
    /// (or bottom right when <paramref name="sunken"/>), and the look's pattern inside, tiled to any size.
    /// </summary>
    private static StyleBoxTexture Box(Look look, Color fill, Color frame, bool sunken, float alpha, int marginX, int marginY)
    {
        string key = $"{look.Id}:{fill.ToHtml()}:{frame.ToHtml()}:{sunken}:{alpha}:{marginX}:{marginY}";
        if (Boxes.TryGetValue(key, out var cached)) return cached;
        var made = PaintBox(look, fill, frame, sunken, alpha, marginX, marginY);
        Boxes[key] = made;
        return made;
    }

    private static readonly Dictionary<string, StyleBoxTexture> Boxes = new();

    private static StyleBoxTexture PaintBox(Look look, Color fill, Color frame, bool sunken, float alpha, int marginX, int marginY)
    {
        const int edge = 3; // outline + two pixels of frame
        int size = Tile + 2 * edge;
        var image = Image.CreateEmpty(size * Px, size * Px, false, Image.Format.Rgba8);
        var outline = frame.Darkened(0.72f);
        Color light = frame.Lightened(0.32f), dark = frame.Darkened(0.42f);
        if (sunken) (light, dark) = (dark, light);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int d = Math.Min(Math.Min(x, y), Math.Min(size - 1 - x, size - 1 - y));
            bool corner = (x == 0 || x == size - 1) && (y == 0 || y == size - 1);
            Color c;
            if (corner) continue;
            if (d == 0) c = outline;
            else if (d < edge)
            {
                bool topLeft = (y == d && x >= d && x < size - d) || (x == d && y >= d && y < size - d);
                bool bottomRight = y == size - 1 - d || x == size - 1 - d;
                c = d == 1 ? (topLeft && !bottomRight ? light : bottomRight && !topLeft ? dark : frame) : frame;
                if (d == edge - 1 && (bottomRight && !topLeft)) c = frame.Darkened(0.15f);
            }
            else
            {
                c = PatternPixel(look, fill, x - edge, y - edge);
                // An inner shadow line under the frame gives the fill some depth.
                if (!sunken && (y == edge || x == edge)) c = c.Darkened(0.18f);
                if (sunken && (y == edge || x == edge)) c = c.Darkened(0.32f);
            }
            c.A *= alpha;
            image.FillRect(new Rect2I(x * Px, y * Px, Px, Px), c);
        }
        var style = new StyleBoxTexture
        {
            Texture = ImageTexture.CreateFromImage(image),
            AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Tile,
            AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Tile,
        };
        style.SetTextureMarginAll(edge * Px);
        style.ContentMarginLeft = style.ContentMarginRight = marginX;
        style.ContentMarginTop = style.ContentMarginBottom = marginY;
        return style;
    }

    /// <summary>One pixel of the look's pattern at (u, v) in its repeating tile.</summary>
    private static Color PatternPixel(Look look, Color fill, int u, int v)
    {
        u = ((u % Tile) + Tile) % Tile;
        v = ((v % Tile) + Tile) % Tile;
        switch (look.Pattern)
        {
            case Pattern.Planks:
            {
                // Four planks a tile, each with its own shade, a seam under it, a joint somewhere along it and streaks of grain.
                // Only every other plank has a joint in the tile, so planks read as long boards rather than bricks.
                int plank = v / 6, inPlank = v % 6;
                if (inPlank == 5) return fill.Darkened(0.38f);
                if (plank % 2 == 1 && u == (plank * 7 + 3) % Tile) return fill.Darkened(0.3f);
                float shade = (Hash(plank, 7) % 9 - 4) * 0.014f;
                float grain = (Hash(plank * 31 + inPlank, (u + plank * 5) / 8) % 7 - 3) * 0.02f;
                if (inPlank == 0) shade += 0.06f;
                return Shade(fill, shade + grain);
            }
            case Pattern.Bricks:
            {
                // Courses of bricks, every other course shifted by half a brick, each brick its own shade, with speckles.
                int course = v / 6, inCourse = v % 6;
                int along = u + (course % 2) * 6;
                if (inCourse == 5 || along % 12 == 11) return fill.Darkened(0.42f);
                int brick = course * 3 + along / 12;
                float shade = (Hash(brick, 3) % 9 - 4) * 0.022f + (Hash(u, v) % 5 - 2) * 0.012f;
                if (inCourse == 0 || along % 12 == 0) shade += 0.07f;
                return Shade(fill, shade);
            }
            default:
            {
                // Dark slate with a faint dither and the odd glinting crystal fleck.
                int h = Hash(u, v);
                if (h % 89 == 0) return fill.Lerp(look.Highlight, 0.35f);
                if (h % 53 == 0) return fill.Lerp(look.Frame, 0.6f);
                return Shade(fill, (h % 5 - 2) * 0.01f + ((u + v) % 2) * 0.008f);
            }
        }
    }

    private static Color Shade(Color c, float amount) => amount >= 0 ? c.Lightened(amount) : c.Darkened(-amount);

    private static int Hash(int a, int b)
    {
        unchecked
        {
            uint h = (uint)(a * 374761393 + b * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return (int)((h ^ (h >> 16)) & 0x7fffffff);
        }
    }

    /// <summary>The slider's knob: a small bevelled block.</summary>
    private static Texture2D Knob(Look look, Color colour)
    {
        const int n = 8;
        var image = Image.CreateEmpty(n * Px, n * Px, false, Image.Format.Rgba8);
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            bool corner = (x == 0 || x == n - 1) && (y == 0 || y == n - 1);
            if (corner) continue;
            var c = x == 0 || y == 0 || x == n - 1 || y == n - 1 ? look.Fill.Darkened(0.6f)
                : x == 1 || y == 1 ? colour.Lightened(0.35f)
                : x == n - 2 || y == n - 2 ? colour.Darkened(0.35f) : colour;
            image.FillRect(new Rect2I(x * Px, y * Px, Px, Px), c);
        }
        return ImageTexture.CreateFromImage(image);
    }
}
