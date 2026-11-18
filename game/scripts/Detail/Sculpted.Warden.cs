using System;
using Godot;

namespace Rts.Game.Detail;

public static partial class Sculpted
{
    private static readonly FineMaterial WardenBark = M(0.36f, 0.35f, 0.41f), WardenBarkDark = M(0.2f, 0.19f, 0.24f),
        WardenBarkLight = M(0.47f, 0.46f, 0.52f), WardenBarkWarm = M(0.4f, 0.36f, 0.38f);
    private static readonly FineMaterial WardenVein = new(new Color(0.3f, 0.9f, 0.95f), Surface.Glow);
    private static readonly FineMaterial WardenLeaf = M(0.55f, 0.33f, 0.7f), WardenLeafDark = M(0.4f, 0.23f, 0.54f);
    private static readonly FineMaterial WardenSpeck = new(new Color(0.78f, 0.52f, 0.9f), Surface.Glow);
    private static readonly FineMaterial[] WardenBarks = { WardenBark, WardenBarkDark, WardenBarkLight, WardenBarkWarm };

    /// <summary>The warden's pivots: hips, waist, neck, shoulders.</summary>
    private static readonly Vector3[] WardenPivots = { V(-20, 72, 0), V(20, 72, 0), V(0, 72, 0), V(0, 146, 18), V(-42, 140, 0), V(42, 140, 0) };

    private static RigDef WardenRig() => new()
    {
        Roles = Person, Pivots = WardenPivots, Build = DuskWarden,
        StrideBlocks = 3.2f, LegSwing = 0.4f, ArmSwing = 0.3f, Lean = 0.5f, Bob = 2.6f, Blow = 0.8f, Far = true,
    };

    /// <summary>Bark: furrows running down it, lighter and warmer patches, knocked-off flakes.</summary>
    private static void Barked(Sculpt s, int seed)
    {
        s.Mottle(WardenBark, WardenBarkLight, 11, 0.24f, seed).Mottle(WardenBark, WardenBarkWarm, 17, 0.2f, seed + 1)
         .Mottle(WardenBark, WardenBarkDark, 6, 0.12f, seed + 2)
         .Erode(4, 0.1f, seed + 3, WardenBarks);
    }

    /// <summary>
    /// Dark furrows down a part from y0 to y1: <paramref name="count"/> thin slices out from the upright line
    /// through <paramref name="axis"/>, each a little crooked, which show as grooves where they meet the surface.
    /// </summary>
    private static void Furrows(Sculpt s, Vector3 axis, float y0, float y1, int count, int seed)
    {
        var lo = new Vector3(s.Grid.X0, s.Grid.Y0, s.Grid.Z0) * s.Step;
        var hi = new Vector3(s.Grid.X0 + s.Grid.NX, s.Grid.Y0 + s.Grid.NY, s.Grid.Z0 + s.Grid.NZ) * s.Step;
        lo.Y = MathF.Max(lo.Y, y0);
        hi.Y = MathF.Min(hi.Y, y1);
        var angles = new float[count];
        for (int k = 0; k < count; k++) angles[k] = (k + 0.6f * Sculpt.Hash(k, seed, 1, 2)) * MathF.Tau / count;
        s.Shape(lo, hi, p =>
        {
            float dx = p.X - axis.X, dz = p.Z - axis.Z;
            float r = MathF.Sqrt(dx * dx + dz * dz);
            if (r < 4) return false;
            // Each furrow wanders a little round the part as it runs up.
            float a = MathF.Atan2(dx, dz) + 0.12f * MathF.Sin(p.Y * 0.11f + seed);
            foreach (float k in angles)
            {
                float d = MathF.Abs(Mathf.Wrap(a - k, -MathF.PI, MathF.PI));
                if (d * r < 1.1f) return true;
            }
            return false;
        }, WardenBarkDark, Sculpt.Op.Paint);
    }

    /// <summary>A crack of cyan light along the points given, painted into the bark there.</summary>
    private static void Crack(Sculpt s, float r, params Vector3[] at)
    {
        for (int k = 0; k + 1 < at.Length; k++) s.Limb(at[k], at[k + 1], r, r * 0.7f, WardenVein, Sculpt.Op.Paint);
    }

    /// <summary>
    /// The Dusk Warden, about 175 units (2.7 m) tall: a hulking husk of grey bark grown round bone. Its legs are
    /// stumps that splay into roots at the ground; its body a barrel of bark, furrowed and cracked with cyan light,
    /// hunched forward, with the bark torn open over its chest to show bone ribs round a glowing heart. Its head is
    /// a stag's skull with burning eyes under a hood of bark, crowned with branching antlers that bud violet dusk
    /// leaves. Its arms hang to its knees and end in five long claws of root; violet moss and hanging vines grow
    /// over its shoulders. Parts: legs, body, head, arms.
    /// </summary>
    private static FineGrid[] DuskWarden(int step)
    {
        var legL = new Sculpt(step, -52, -2, -30, -2, 84, 40);
        var legR = new Sculpt(step, 2, -2, -30, 52, 84, 40);
        var body = new Sculpt(step, -52, 56, -44, 52, 166, 50);
        var head = new Sculpt(step, -44, 136, -6, 44, 214, 64);
        var armL = new Sculpt(step, -84, 14, -22, -22, 158, 40);
        var armR = new Sculpt(step, 22, 14, -22, 84, 158, 40);

        // Legs: a thick thigh, a gnarled knee, a shin that widens into a stump, and roots splayed out from it.
        foreach (int sd in new[] { -1, 1 })
        {
            var leg = sd < 0 ? legL : legR;
            float x = sd * 26;
            leg.Limb(V(sd * 20, 74, 0), V(x, 44, 5), 14, 11, WardenBark)
               .Ball(V(x + sd * 1.5f, 42, 7), V(12, 10, 11), WardenBark)
               .Limb(V(x, 40, 5), V(x + sd * 2, 12, 2), 10.5f, 14, WardenBark)
               .Ball(V(x + sd * 2, 9, 2), V(16, 9, 15), WardenBark);
            // Roots for toes: forward, out to the side, back, and a short one in.
            var foot = V(x + sd * 2, 8, 2);
            leg.Limb(foot, V(x + sd * 4, 2.5f, 30), 7, 2.5f, WardenBark)
               .Limb(foot, V(x - sd * 8, 2.5f, 26), 6, 2.2f, WardenBark)
               .Limb(foot, V(x + sd * 22, 2.5f, 12), 6.5f, 2.2f, WardenBarkDark)
               .Limb(foot, V(x + sd * 10, 2.5f, -24), 6, 2.5f, WardenBark)
               .Limb(V(x + sd * 4, 3, 28), V(x + sd * 6, 0.8f, 36), 2.6f, 1.2f, WardenBarkDark);
            Furrows(leg, V(x, 0, 4), 12, 70, 7, 10 + sd);
            Barked(leg, 20 + sd);
            leg.Moss(WardenLeaf, WardenLeafDark, 6, 0.12f, 30 + sd, WardenBarks);
            Crack(leg, 1.6f, V(x + sd * 6, 18, 14), V(x + sd * 4, 28, 13), V(x + sd * 8, 36, 14), V(x + sd * 5, 46, 15.5f));
        }

        // Body: hips, a barrel of a body growing up and forward into a hunched back and shoulders.
        body.Ball(V(0, 78, 0), V(27, 16, 21), WardenBark)
            .Limb(V(0, 84, 0), V(0, 118, 6), 25, 31, WardenBark)
            .Ball(V(0, 132, -4), V(37, 24, 29), WardenBark)
            .Ball(V(0, 140, -14), V(32, 20, 24), WardenBarkDark)
            .Ball(V(-30, 140, 0), V(15, 14, 15), WardenBark).Ball(V(30, 140, 0), V(15, 14, 15), WardenBark)
            .Ball(V(0, 148, -6), V(22, 13, 18), WardenBark);
        // Knots and burls on the back.
        body.Ball(V(-14, 120, -27), 6, WardenBarkDark).Ball(V(17, 108, -24), 5, WardenBarkDark).Ball(V(4, 142, -36), 5.5f, WardenBark);
        Furrows(body, V(0, 0, 0), 70, 150, 16, 40);
        Barked(body, 41);
        body.Band(V(0, 90, 0), V(0.25f, 1, 0), 2.6f, TeamDark).Band(V(0, 86, 0), V(-0.15f, 1, 0), 1.4f, TeamDeep); // a sash, once it is someone's
        // Bark torn open over the chest: bone ribs round a glowing heart, the edges of the wound ragged.
        body.Ball(V(0, 114, 30), V(15, 18, 13), default, Sculpt.Op.Carve)
            .Ball(V(0, 114, 24), V(10, 12, 7), WardenBarkDark)
            .Ball(V(0, 116, 24), V(6.5f, 7.5f, 6), WardenVein);
        for (int k = 0; k < 4; k++)
        {
            float y = 103 + k * 7;
            float w = 13 - Math.Abs(k - 1.5f) * 1.6f;
            body.Limb(V(-w - 2, y, 23), V(-3, y + 2, 31), 1.8f, Bone).Limb(V(w + 2, y, 23), V(3, y + 2, 31), 1.8f, Bone);
        }
        body.Limb(V(0, 99, 28), V(0, 131, 31), 2.2f, BoneDark);                                                  // breastbone
        for (int k = 0; k < 10; k++)
        {
            float a = k * MathF.Tau / 10;
            body.Ball(V(MathF.Sin(a) * 15.5f, 114 + MathF.Cos(a) * 18.5f, 30.5f), 2.2f + 1.5f * Sculpt.Hash(k, 5, 6, 7), WardenBarkDark);
        }
        // Cracks of light running out from it and across the back.
        Crack(body, 1.8f, V(-12, 128, 30), V(-18, 138, 27), V(-24, 142, 22));
        Crack(body, 1.7f, V(10, 98, 30), V(16, 90, 25), V(14, 82, 22));
        Crack(body, 1.5f, V(-14, 102, 29), V(-21, 94, 23));
        Crack(body, 1.6f, V(-6, 150, -22), V(4, 138, -33), V(-2, 124, -32), V(8, 112, -28));
        Crack(body, 1.4f, V(20, 140, -26), V(28, 128, -18));
        // Violet moss over the shoulders and back, and vines hanging from it.
        body.Moss(WardenLeaf, WardenLeafDark, 7, 0.22f, 50, WardenBarks);
        foreach (int sd in new[] { -1, 1 })
        {
            body.Ball(V(sd * 30, 152, -2), V(14, 6, 13), WardenLeaf).Ball(V(sd * 24, 154, -10), V(10, 5, 10), WardenLeafDark);
            for (int k = 0; k < 4; k++)
            {
                float vx = sd * (18 + k * 5), vz = 10 - k * 7;
                float len = 18 + 16 * Sculpt.Hash(k, sd, 2, 9);
                body.Limb(V(vx, 150, vz), V(vx + sd * 2, 150 - len, vz + 4), 1.5f, 1f, k % 2 == 0 ? WardenLeaf : WardenLeafDark)
                    .Ball(V(vx + sd * 2, 150 - len, vz + 4), 2, WardenSpeck);
            }
        }
        body.Mottle(WardenLeaf, WardenSpeck, 3, 0.06f, 51);

        // Head: a stag's skull with burning eyes under a hood of bark, and antlers.
        head.Limb(V(0, 140, 10), V(0, 154, 22), 12, 10, WardenBark)                                                // neck
            .Ball(V(0, 162, 22), V(17, 15, 15), WardenBark)                                                      // the hood
            .Ball(V(0, 160, 32), V(12.5f, 12.5f, 12), Bone)                                                       // the skull
            .RoundBox(V(0, 150, 45), V(7.5f, 7, 10), 4, Bone)                                                     // snout
            .RoundBox(V(0, 145, 43), V(6.5f, 3, 9), 2.5f, BoneDark);                                              // jaw
        head.Ball(V(0, 170, 28), V(15, 6, 10), WardenBark);                                                       // the hood's brow
        Barked(head, 60);
        head.Mottle(Bone, BoneDark, 4, 0.18f, 61);
        foreach (int sd in new[] { -1, 1 })
        {
            head.Ball(V(sd * 6.5f, 160, 41), V(4.5f, 4, 4), default, Sculpt.Op.Carve)                             // eye sockets
                .Ball(V(sd * 6.5f, 160, 39), V(3.5f, 3.2f, 3), Dark)
                .Ball(V(sd * 6.5f, 160, 40), V(2.2f, 2, 2), WardenVein)                                          // eyes
                .Ball(V(sd * 2.5f, 152, 55), V(1.5f, 2, 1.5f), default, Sculpt.Op.Carve);                         // nostrils
            for (int t = 0; t < 3; t++) head.Box(sd * (1.2f + t * 2.2f), 146, 53, sd * (2.4f + t * 2.2f), 149, 54.5f, BoneDark);
            // Antlers: a beam up and out, three tines off it, violet buds at the tips.
            var root = V(sd * 9, 172, 28);
            var mid = V(sd * 24, 188, 22);
            var top = V(sd * 34, 206, 14);
            head.Limb(root, mid, 4, 3, WardenBarkDark).Limb(mid, top, 3, 1.6f, WardenBarkDark)
                .Limb(V(sd * 17, 181, 25), V(sd * 18, 196, 34), 2.4f, 1.2f, WardenBarkDark)
                .Limb(mid, V(sd * 32, 194, 32), 2.4f, 1.2f, WardenBarkDark)
                .Limb(V(sd * 29, 197, 18), V(sd * 26, 210, 10), 2, 1, WardenBarkDark)
                .Limb(V(sd * 30, 198, 18), V(sd * 42, 204, 24), 2, 1, WardenBarkDark);
            foreach (var tip in new[] { top, V(sd * 18, 196, 34), V(sd * 32, 194, 32), V(sd * 26, 210, 10), V(sd * 42, 204, 24) })
                head.Ball(tip, V(3.4f, 2.6f, 3.4f), WardenLeaf).Ball(tip + V(0, 1.5f, 0), 1.3f, WardenSpeck);
            head.Ball(mid + V(0, -1, 2), V(4, 3, 4), WardenLeafDark);
        }
        Crack(head, 1.2f, V(-3, 175, 22), V(2, 168, 30), V(-1, 164, 36));

        // Arms: a knotted shoulder, a long upper arm, an elbow burl, a forearm that thickens to the wrist, and five
        // claws of root curling forward.
        foreach (int sd in new[] { -1, 1 })
        {
            var arm = sd < 0 ? armL : armR;
            arm.Ball(V(sd * 42, 140, 0), 15, WardenBark)
               .Limb(V(sd * 44, 136, 1), V(sd * 54, 98, 8), 12, 9, WardenBark)
               .Ball(V(sd * 54, 97, 7), V(10.5f, 10, 10), WardenBark)
               .Limb(V(sd * 54, 96, 8), V(sd * 58, 58, 16), 9, 12, WardenBark)
               .Ball(V(sd * 58, 55, 16), V(12, 9, 11), WardenBark);
            Furrows(arm, V(sd * 55, 0, 9), 60, 134, 6, 70 + sd);
            Barked(arm, 72 + sd);
            arm.Moss(WardenLeaf, WardenLeafDark, 6, 0.2f, 74 + sd, WardenBarks);
            // Claws: four forward and down, a thumb inside.
            for (int t = 0; t < 4; t++)
            {
                float cx = sd * (51 + t * 4.6f), cz = 22 - t * 2;
                var knuckle = V(cx, 50, cz);
                var bend = V(cx + sd * 1.5f, 34, cz + 7);
                arm.Limb(knuckle, bend, 3.6f, 2.6f, WardenBarkDark).Limb(bend, V(cx + sd * 1, 20 + t * 1.5f, cz + 13), 2.6f, 0.8f, Bone);
            }
            arm.Limb(V(sd * 50, 52, 12), V(sd * 44, 36, 22), 3.2f, 1, WardenBarkDark);
            Crack(arm, 1.4f, V(sd * 55, 126, 12), V(sd * 59, 112, 15), V(sd * 56, 102, 17));
            Crack(arm, 1.3f, V(sd * 60, 84, 19), V(sd * 62, 70, 22));
        }

        return new[] { legL.Grid, legR.Grid, body.Grid, head.Grid, armL.Grid, armR.Grid };
    }
}
