using System;
using Godot;
using Rts.Sim;
using Rts.Sim.Units;

namespace Rts.Game;


/// <summary>
/// The selected unit's body in the unit panel (issue #71): an outline with each part shaded by how hurt it is
/// (red as its wound grows, a drop for a bleeding wound), and beside it each part's armour and wound, its
/// shield and the kind of its attack. A beast is drawn on all fours, with no arms.
/// </summary>
public partial class BodyChart : Control
{
    private Simulation? _sim;
    private int _unit;

    public BodyChart()
    {
        CustomMinimumSize = new Vector2(360, 96);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>Shows unit id (0 hides it). Redrawn every frame it is shown, since wounds change in a fight.</summary>
    public void Show(Simulation sim, int unit)
    {
        _sim = sim;
        _unit = unit;
        Visible = unit != 0 && sim.Units.Exists(unit);
        if (Visible) QueueRedraw();
    }

    private static readonly BodyPart[] Order = { BodyPart.Head, BodyPart.Torso, BodyPart.Arms, BodyPart.Legs };

    public override void _Draw()
    {
        if (_sim == null || !_sim.Units.Exists(_unit)) return;
        var u = _sim.Units;
        int i = u.IndexOf(_unit);
        var def = _sim.Content.Units[u.Def[i]];
        int max = Math.Max(1, _sim.MaxHpOf(i));
        var look = UiStyle.Current;

        Color Shade(BodyPart p)
        {
            // Unhurt: the panel's text colour, dimmed; a wound of a third of the unit's hit points or more: full red.
            float hurt = Math.Clamp(u.Wound(i, p) * 3f / max, 0f, 1f);
            return look.DimText.Darkened(0.35f).Lerp(new Color(0.85f, 0.12f, 0.1f), hurt);
        }

        var rects = new Rect2[Body.Parts];
        if (def.Body == BodyPlan.Beast)
        {
            rects[(int)BodyPart.Head] = new Rect2(60, 22, 18, 16);
            rects[(int)BodyPart.Torso] = new Rect2(14, 32, 48, 22);
            rects[(int)BodyPart.Legs] = new Rect2(16, 54, 44, 22);
        }
        else
        {
            rects[(int)BodyPart.Head] = new Rect2(30, 4, 16, 16);
            rects[(int)BodyPart.Torso] = new Rect2(26, 22, 24, 32);
            rects[(int)BodyPart.Arms] = new Rect2(14, 22, 48, 26); // drawn as two arms either side of the torso
            rects[(int)BodyPart.Legs] = new Rect2(27, 56, 22, 34);
        }
        foreach (var p in Order)
        {
            if (!Body.Has(def.Body, p)) continue;
            var r = rects[(int)p];
            var fill = Shade(p);
            if (p == BodyPart.Arms)
            {
                DrawPart(new Rect2(r.Position, new Vector2(10, r.Size.Y)), fill, look.Frame);
                DrawPart(new Rect2(r.End.X - 10, r.Position.Y, 10, r.Size.Y), fill, look.Frame);
            }
            else if (p == BodyPart.Legs && def.Body == BodyPlan.Humanoid)
            {
                DrawPart(new Rect2(r.Position, new Vector2(10, r.Size.Y)), fill, look.Frame);
                DrawPart(new Rect2(r.End.X - 10, r.Position.Y, 10, r.Size.Y), fill, look.Frame);
            }
            else if (p == BodyPart.Legs)
            {
                for (int k = 0; k < 4; k++) DrawPart(new Rect2(r.Position.X + k * 12, r.Position.Y, 8, r.Size.Y), fill, look.Frame);
            }
            else DrawPart(r, fill, look.Frame);
            if (u.IsBleeding(i, p))
            {
                var c = r.GetCenter() + (p == BodyPart.Arms ? new Vector2(r.Size.X / 2 - 5, 4) : Vector2.Zero);
                DrawCircle(c + new Vector2(0, 2), 3.5f, new Color(0.65f, 0f, 0f));
                DrawColoredPolygon(new[] { c + new Vector2(-3, 1), c + new Vector2(3, 1), c + new Vector2(0, -5) }, new Color(0.65f, 0f, 0f));
            }
        }

        var font = GetThemeDefaultFont();
        const int size = 13;
        float x = 92, y = 16;
        foreach (var p in Order)
        {
            if (!Body.Has(def.Body, p)) continue;
            string name = p == BodyPart.Torso && def.Body == BodyPlan.Beast ? "Body" : p.ToString();
            int armour = _sim.Combat.Armour(i, p), extra = u.Extra(i, p), wound = u.Wound(i, p);
            DrawString(font, new Vector2(x, y), name, HorizontalAlignment.Left, -1, size, look.Text);
            DrawString(font, new Vector2(x + 52, y), extra > 0 ? $"armour {armour} (+{extra} found)" : $"armour {armour}",
                HorizontalAlignment.Left, -1, size, armour > 0 ? look.Text : look.DimText);
            if (wound > 0)
                DrawString(font, new Vector2(x + 172, y), u.IsBleeding(i, p) ? $"wound {wound}, bleeding" : $"wound {wound}",
                    HorizontalAlignment.Left, -1, size, new Color(1f, 0.45f, 0.4f));
            y += 18;
        }
        string attack = def.CanAttack ? $"{def.Attack.ToString().ToLowerInvariant()} attack" : "no attack";
        string shield = def.Shield > 0 ? $"shield stops {def.Shield}% from the front    " : "";
        DrawString(font, new Vector2(x, y + 2), shield + attack, HorizontalAlignment.Left, -1, size, look.DimText);
    }

    private void DrawPart(Rect2 r, Color fill, Color frame)
    {
        DrawRect(r, fill);
        DrawRect(r, frame, filled: false, width: 1.5f);
    }
}
