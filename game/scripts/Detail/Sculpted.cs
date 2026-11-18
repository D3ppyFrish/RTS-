using System;
using System.Collections.Generic;
using Godot;

namespace Rts.Game.Detail;

/// <summary>
/// Detailed models sculpted in shapes (<see cref="Sculpt"/>) rather than drawn voxel by voxel, so each can be
/// built at any voxel size: orcs (rounded green bodies, tusks, tattered kilts in the owner's colour, leather
/// straps, iron plates with rivets), wolves, a wolf rider, and a moss-covered stone golem in armour plates
/// with an orc on its back. Sizes are in sculpt units (a thirty-second of a block, 1.56 cm): an orc stands
/// about 62 units (a metre) tall, a wolf 36 at the ears, the golem 200 (3.1 m).
/// </summary>
public static partial class Sculpted
{
    private static FineMaterial M(float r, float g, float b, Surface s = Surface.Plain) => new(new Color(r, g, b), s);
    private static Vector3 V(float x, float y, float z) => new(x, y, z);

    private static readonly FineMaterial OrcSkin = M(0.5f, 0.7f, 0.3f), OrcSkinDark = M(0.39f, 0.57f, 0.23f), OrcSkinLight = M(0.6f, 0.78f, 0.37f);
    private static readonly FineMaterial OrcEye = new(new Color(1f, 0.28f, 0.1f), Surface.Glow);
    private static readonly FineMaterial Bone = M(0.9f, 0.86f, 0.72f), BoneDark = M(0.74f, 0.69f, 0.55f);
    private static readonly FineMaterial Hair = M(0.17f, 0.12f, 0.09f);
    private static readonly FineMaterial Hide = M(0.52f, 0.39f, 0.27f), HideDark = M(0.4f, 0.3f, 0.2f);
    private static readonly FineMaterial Leather = M(0.5f, 0.32f, 0.17f), LeatherDark = M(0.36f, 0.23f, 0.12f);
    private static readonly FineMaterial Rope = M(0.66f, 0.55f, 0.36f);
    // Iron is drawn plain rather than as shiny metal: without reflections to show, metal comes out nearly black.
    private static readonly FineMaterial Iron = M(0.66f, 0.67f, 0.7f), IronDark = M(0.47f, 0.48f, 0.51f),
        IronLight = M(0.82f, 0.83f, 0.86f, Surface.Metal);
    private static readonly FineMaterial Wood = M(0.5f, 0.33f, 0.17f), WoodPlanks = M(0.5f, 0.34f, 0.18f, Surface.Planks);
    private static readonly FineMaterial Fur = M(0.62f, 0.6f, 0.57f), FurDark = M(0.44f, 0.42f, 0.4f), FurPale = M(0.84f, 0.82f, 0.77f);
    private static readonly FineMaterial WolfEye = M(0.95f, 0.72f, 0.2f), Dark = M(0.1f, 0.09f, 0.09f);
    private static readonly FineMaterial Stone = M(0.74f, 0.72f, 0.67f), StoneLight = M(0.85f, 0.83f, 0.78f),
        StoneWarm = M(0.78f, 0.7f, 0.58f), StoneDark = M(0.56f, 0.55f, 0.52f);
    private static readonly FineMaterial Moss = M(0.45f, 0.58f, 0.2f), MossDark = M(0.31f, 0.44f, 0.15f);
    private static readonly FineMaterial GolemEye = new(new Color(1f, 0.7f, 0.25f), Surface.Glow);
    private static readonly FineMaterial Team = new(new Color(1f, 0, 0, 0)), TeamDark = new(new Color(0.7f, 0, 0, 0)), TeamDeep = new(new Color(0.5f, 0, 0, 0));

    private static readonly Role[] Person = { Role.LegA, Role.LegB, Role.Body, Role.Head, Role.ArmA, Role.ArmB };
    private const int LegL = 0, LegR = 1, Body = 2, Head = 3, ArmL = 4, ArmR = 5;

    /// <summary>An orc's pivots: hips, waist, neck, shoulders.</summary>
    private static readonly Vector3[] OrcPivots = { V(-6, 23, 0), V(6, 23, 0), V(0, 23, 0), V(0, 45, 3), V(-13, 42, 0), V(13, 42, 0) };

    /// <summary>The sculpted models, by the model name units use (see <see cref="UnitModels.ForFaction"/>), plus the golem.</summary>
    public static Dictionary<string, RigDef> Rigs => _rigs ??= MakeRigs();
    private static Dictionary<string, RigDef>? _rigs;

    // Built on first use: static fields are set in the order they are written, and these use ones further down.
    private static Dictionary<string, RigDef> MakeRigs() => new()
    {
        ["orcs/spearman"] = new RigDef { Roles = Person, Pivots = OrcPivots, Build = OrcSpearman },
        ["orcs/worker"] = new RigDef { Roles = Person, Pivots = OrcPivots, Build = OrcWorker },
        ["wolf"] = new RigDef
        {
            Roles = WolfRoles, Pivots = WolfPivots(1f, Vector3.Zero), Build = s => Wolf(s),
            StrideBlocks = 1.1f, LegSwing = 0.55f, Lean = 0.3f, Bob = 1f, Blow = 0.8f,
        },
        ["wolf_rider"] = new RigDef
        {
            Roles = RiderRoles, Pivots = RiderPivots(), Build = WolfRider,
            StrideBlocks = 1.4f, LegSwing = 0.55f, ArmSwing = 0.1f, Lean = 0.3f, Bob = 1.3f,
        },
        ["knight"] = ImportedRig("res://models/knight.vox.gz"), // made with Meshy AI (Sculpted.Imported)
        ["golem"] = new RigDef
        {
            Roles = new[] { Role.LegA, Role.LegB, Role.Body, Role.Head, Role.ArmA, Role.ArmB, Role.Fixed },
            Pivots = new[] { V(-26, 78, 0), V(26, 78, 0), V(0, 78, 0), V(0, 160, 24), V(-50, 152, 5), V(50, 152, 5), V(0, 78, 0) },
            Build = Golem, StrideBlocks = 4f, LegSwing = 0.35f, ArmSwing = 0.25f, Lean = 0.5f, Bob = 3f, Blow = 0.6f, Far = true,
        },
    };

    // ---- Orcs ---------------------------------------------------------------------------------------

    /// <summary>Six sculpts for an orc's parts at a step, each big enough for its part with gear held out.</summary>
    private static Sculpt[] OrcParts(int step) => new[]
    {
        new Sculpt(step, -16, -2, -12, 2, 32, 14), new Sculpt(step, -2, -2, -12, 16, 32, 14),
        new Sculpt(step, -26, 0, -22, 26, 66, 22), new Sculpt(step, -18, 38, -12, 18, 72, 20),
        new Sculpt(step, -36, -4, -16, 0, 70, 22), new Sculpt(step, 0, -4, -16, 36, 100, 22),
    };

    private static FineGrid[] Grids(Sculpt[] s)
    {
        var g = new FineGrid[s.Length];
        for (int i = 0; i < s.Length; i++) g[i] = s[i].Grid;
        return g;
    }

    /// <summary>
    /// An orc standing at <paramref name="o"/> into the sculpts given for its legs, body, head and arms (the same
    /// sculpt may take several): hunched and broad, with a heavy jaw, tusks, glowing red eyes, a topknot, bare
    /// green arms with leather bracers, hide breeches and a tattered kilt in its owner's colour under a belt,
    /// and a strap across the chest. Hands at x = ±16.5, y = 19.5, z = 4 (the right one raised to y = 64 with
    /// <paramref name="raise"/>). <paramref name="legs"/> false leaves the legs out (a rider's are drawn sitting).
    /// </summary>
    private static void Orc(Sculpt legL, Sculpt legR, Sculpt body, Sculpt head, Sculpt armL, Sculpt armR, Vector3 o,
        bool legs = true, bool raise = false, int seed = 1)
    {
        Vector3 P(float x, float y, float z) => o + V(x, y, z);
        if (legs)
            foreach (int sd in new[] { -1, 1 })
            {
                var leg = sd < 0 ? legL : legR;
                leg.Limb(P(sd * 6, 23, 0), P(sd * 7.5f, 12, 1.5f), 5, 4.2f, Hide)
                   .Limb(P(sd * 7.5f, 13.5f, 1.5f), P(sd * 7.5f, 11, 1.5f), 4.7f, 4.5f, LeatherDark)  // knee wrap
                   .Limb(P(sd * 7.5f, 11, 1.5f), P(sd * 7.5f, 4, 0.8f), 4, 3.4f, OrcSkin)             // bare shins
                   .RoundBox(P(sd * 7.5f, 2.5f, 2.8f), V(4, 2.5f, 5.6f), 1.6f, OrcSkinDark)           // big feet
                   .RoundBox(P(sd * 7.5f, 4.6f, 1.2f), V(4.3f, 1, 4.3f), 1, Leather)                  // foot wraps
                   .Box(o.X + sd * 7.5f - 3.4f, o.Y, o.Z + 7.8f, o.X + sd * 7.5f + 3.4f, o.Y + 1.2f, o.Z + 8.8f, Bone); // toenails
                leg.Mottle(Hide, HideDark, 6, 0.3f, seed + 11);
            }

        // The body: belly, chest and a hunched back, a belt with a bone buckle, a kilt in the owner's colour.
        body.RoundBox(P(0, 23, 0.5f), V(9.5f, 4, 6.5f), 3, Hide)
            .Ball(P(0, 29, 2), V(10, 7.5f, 8), OrcSkin)
            .Ball(P(0, 36, 1.5f), V(12.5f, 8, 8.5f), OrcSkin)
            .Ball(P(0, 38.5f, -3), V(11, 7, 7), OrcSkin)
            .Ball(P(-5, 37.5f, 7.5f), V(5, 3.6f, 2.6f), OrcSkin).Ball(P(5, 37.5f, 7.5f), V(5, 3.6f, 2.6f), OrcSkin) // chest
            .Ball(P(0, 43, 0.5f), V(8.5f, 4, 6), OrcSkin);                                                        // shoulders' hump
        body.Mottle(OrcSkin, OrcSkinDark, 5, 0.17f, seed).Mottle(OrcSkin, OrcSkinLight, 9, 0.18f, seed + 1);
        body.Band(P(0, 34, 0), V(1, 1.15f, 0), 1.7f, Leather)                                                    // strap across the chest
            .RoundBox(P(0, 17.5f, 0.5f), V(10.6f, 5.5f, 7.6f), 2, Team)                                          // kilt
            .RoundBox(P(0, 21.2f, 0.5f), V(10.8f, 1, 7.8f), 0.8f, TeamDeep)
            .RoundBox(P(0, 24, 0.5f), V(10.4f, 1.9f, 7.4f), 1, Leather)                                          // belt
            .RoundBox(P(0, 24, 8), V(2.4f, 2.1f, 0.9f), 0.6f, Bone)                                             // bone buckle
            .Box(o.X - 1.2f, o.Y + 23.4f, o.Z + 8.6f, o.X + 1.2f, o.Y + 24.4f, o.Z + 9.2f, Dark);
        // Tattered hem: strips of the kilt cut back by different lengths, and darker strips between.
        for (int k = 0; k < 8; k++)
        {
            float x = -10.5f + k * 2.7f;
            float cut = 1.5f + 3.5f * Sculpt.Hash(k, seed, 3, 17);
            body.Box(o.X + x, o.Y + 11, o.Z - 8.5f, o.X + x + 1.3f, o.Y + 11 + cut, o.Z + 9, default, Sculpt.Op.Carve);
            if (k % 3 == 1) body.Box(o.X + x + 1.3f, o.Y + 12, o.Z - 8.5f, o.X + x + 2.6f, o.Y + 21, o.Z + 9, TeamDark, Sculpt.Op.Paint);
        }

        // The head, thrust forward: heavy jaw, brow, red eyes, tusks, pointed ears and a topknot.
        head.Limb(P(0, 42.5f, 1), P(0, 47.5f, 4), 4.8f, OrcSkinDark)
            .Ball(P(0, 51.5f, 6), V(6.8f, 7, 6.8f), OrcSkin)
            .RoundBox(P(0, 47, 8.6f), V(6.3f, 2.9f, 4.3f), 2, OrcSkin)
            .Box(o.X - 4.2f, o.Y + 48.4f, o.Z + 12.3f, o.X + 4.2f, o.Y + 49.1f, o.Z + 13.2f, OrcSkinDark)       // mouth
            .RoundBox(P(0, 54.2f, 11.2f), V(6.2f, 1.4f, 1.7f), 0.8f, OrcSkinDark)                               // brow
            .Ball(P(0, 50.2f, 12.6f), V(1.9f, 1.6f, 1.5f), OrcSkinDark);                                         // nose
        foreach (int sd in new[] { -1, 1 })
        {
            head.Box(o.X + sd * 1.4f, o.Y + 51.2f, o.Z + 10.8f, o.X + sd * 4.4f, o.Y + 52.8f, o.Z + 12.4f, OrcEye)
                .Limb(P(sd * 3.9f, 47.6f, 11.6f), P(sd * 4.7f, 52.4f, 13.4f), 1.3f, 0.5f, Bone)                // tusks
                .Limb(P(sd * 6, 52, 6), P(sd * 12, 55, 3.5f), 2.2f, 0.6f, OrcSkin);                              // ears
        }
        head.Ball(P(0, 58, 3), V(2.7f, 2.1f, 3.8f), Hair).Limb(P(0, 58, 0.5f), P(0, 55.5f, -5.5f), 1.9f, 1, Hair)
            .Mottle(OrcSkin, OrcSkinDark, 6, 0.2f, seed + 2);

        // Arms: big bare shoulders, leather bracers, fists.
        foreach (int sd in new[] { -1, 1 })
        {
            var arm = sd < 0 ? armL : armR;
            arm.Ball(P(sd * 13, 42, 0), 5.6f, OrcSkin);
            if (raise && sd > 0)
            {
                arm.Limb(P(14, 43, 0), P(19, 52, 1.5f), 4.8f, 4, OrcSkin)
                   .Limb(P(19, 52, 1.5f), P(20, 61, 3), 4.2f, 3.6f, OrcSkin)
                   .Limb(P(19.4f, 54, 1.8f), P(19.9f, 59, 2.7f), 4.5f, 4.1f, Leather)
                   .Ball(P(20, 64, 3.5f), V(3.6f, 3.8f, 3.6f), OrcSkinDark);
            }
            else
            {
                arm.Limb(P(sd * 14, 41, 0), P(sd * 16, 31, 1.5f), 4.8f, 4, OrcSkin)
                   .Limb(P(sd * 16, 31, 1.5f), P(sd * 16.5f, 22, 3.5f), 4.2f, 3.6f, OrcSkin)
                   .Limb(P(sd * 16.2f, 29, 2), P(sd * 16.5f, 24, 3.2f), 4.5f, 4.1f, Leather)
                   .Limb(P(sd * 16.2f, 27.3f, 2.4f), P(sd * 16.3f, 26.5f, 2.6f), 4.7f, 4.7f, LeatherDark)
                   .Ball(P(sd * 16.5f, 19.5f, 4), V(3.6f, 3.6f, 3.8f), OrcSkinDark);
            }
            arm.Mottle(OrcSkin, OrcSkinDark, 7, 0.22f, seed + 3 + sd);
        }
    }

    /// <summary>A fur mantle over an orc's shoulder (side -1 left, 1 right).</summary>
    private static void FurShoulder(Sculpt arm, Vector3 o, int sd, int seed) =>
        arm.Ball(o + V(sd * 13.5f, 44.5f, -0.5f), V(6.8f, 3.6f, 6.8f), Fur).Mottle(Fur, FurDark, 3, 0.4f, seed).Erode(2.5f, 0.25f, seed, Fur, FurDark);

    /// <summary>An iron pauldron with rivets over an orc's shoulder (side -1 left, 1 right).</summary>
    private static void Pauldron(Sculpt arm, Vector3 o, int sd)
    {
        arm.Plate(o + V(sd * 14.5f, 45.5f, 0), V(6.5f, 2.3f, 6.8f), V(0, 1, 0), Iron, IronDark, IronLight, 2.2f)
           .RoundBox(o + V(sd * 17.5f, 41, 0), V(3.5f, 2.4f, 6.2f), 1.4f, Iron)
           .RoundBox(o + V(sd * 18.5f, 37.6f, 0), V(2.6f, 2, 5.4f), 1.2f, IronDark);
    }

    /// <summary>A horned iron helm on an orc's head.</summary>
    private static void HornedHelm(Sculpt head, Vector3 o)
    {
        head.RoundBox(o + V(0, 55.6f, 5.6f), V(7.6f, 3.8f, 7.6f), 3, Iron)
            .RoundBox(o + V(0, 53.2f, 5.6f), V(8, 1.1f, 8), 0.8f, IronDark);
        foreach (int sd in new[] { -1, 1 })
        {
            head.Ball(o + V(sd * 7.8f, 53.2f, 9.5f), 1.1f, IronLight).Ball(o + V(sd * 7.8f, 53.2f, 2), 1.1f, IronLight)
                .Limb(o + V(sd * 7, 56, 5), o + V(sd * 11, 59.5f, 6), 2.3f, 1.7f, Bone)
                .Limb(o + V(sd * 11, 59.5f, 6), o + V(sd * 12, 64.5f, 8.8f), 1.7f, 0.5f, Bone)
                .Box(o.X + sd * 8.2f, o.Y + 57.2f, o.Z + 4.6f, o.X + sd * 9.4f, o.Y + 59, o.Z + 6.8f, BoneDark, Sculpt.Op.Paint);
        }
    }

    /// <summary>A bone-tipped spear upright in the right hand, with a pennant in the owner's colour.</summary>
    private static void Spear(Sculpt arm, Vector3 hand, float length = 82)
    {
        float top = hand.Y + length - 20;
        arm.Rod(hand - V(0, 18, 0), hand + V(0, length - 20, 0), 1.15f, Wood)
           .Rod(hand - V(0, 2.5f, 0), hand + V(0, 3.5f, 0), 1.6f, Leather)
           .Rod(V(hand.X, top - 3, hand.Z), V(hand.X, top + 1, hand.Z), 1.7f, LeatherDark)
           .Limb(V(hand.X, top, hand.Z), V(hand.X, top + 11, hand.Z), 2.5f, 0.3f, Bone)
           .Box(hand.X - 0.3f, top - 12, hand.Z + 0.8f, hand.X + 0.3f, top - 4, hand.Z + 8, Team)
           .Box(hand.X - 0.3f, top - 9, hand.Z + 5.5f, hand.X + 0.3f, top - 7, hand.Z + 8, default, Sculpt.Op.Carve);
    }

    private static FineGrid[] OrcSpearman(int step)
    {
        var s = OrcParts(step);
        var o = Vector3.Zero;
        Orc(s[LegL], s[LegR], s[Body], s[Head], s[ArmL], s[ArmR], o, seed: 3);
        HornedHelm(s[Head], o);
        Pauldron(s[ArmR], o, 1);
        FurShoulder(s[ArmL], o, -1, 5);
        Spear(s[ArmR], V(16.5f, 19.5f, 4));
        // A round shield on the left arm, facing out: wood in the owner's colour, an iron rim with rivets, a boss.
        var c = V(-22, 25, 3);
        s[ArmL].Rod(c + V(1, 0, 0), c - V(0.6f, 0, 0), 11, Iron)
               .Rod(c + V(0.8f, 0, 0), c - V(0.2f, 0, 0), 9.6f, TeamDark)
               .Box(c.X - 0.4f, c.Y - 1.6f, c.Z - 9.6f, c.X + 1, c.Y + 1.6f, c.Z + 9.6f, WoodPlanks, Sculpt.Op.Paint)
               .Ball(c - V(0.8f, 0, 0), 3, IronLight);
        for (int k = 0; k < 8; k++)
        {
            float a = k * Mathf.Tau / 8;
            s[ArmL].Ball(c + V(-0.7f, 10.2f * MathF.Sin(a), 10.2f * MathF.Cos(a)), 0.9f, IronLight);
        }
        return Grids(s);
    }

    private static FineGrid[] OrcWorker(int step)
    {
        var s = OrcParts(step);
        var o = Vector3.Zero;
        Orc(s[LegL], s[LegR], s[Body], s[Head], s[ArmL], s[ArmR], o, seed: 7);
        FurShoulder(s[ArmL], o, -1, 9);
        FurShoulder(s[ArmR], o, 1, 10);
        s[Head].RoundBox(V(0, 54.2f, 5.8f), V(7.2f, 1, 7.2f), 0.8f, Leather);                              // headband
        // A hide sack on the back, tied with rope.
        s[Body].Ball(V(0, 34, -11.5f), V(8, 9, 5), Hide).Mottle(Hide, HideDark, 4, 0.35f, 8)
               .RoundBox(V(0, 38, -11.5f), V(7, 0.9f, 5.2f), 0.6f, Rope).Limb(V(0, 42, -14), V(0, 44, -10), 1.4f, Rope);
        // A stone pick in the right hand.
        var h = V(16.5f, 19.5f, 4);
        s[ArmR].Rod(h - V(0, 12, 0), h + V(0, 28, 0), 1.2f, Wood)
               .RoundBox(h + V(0, 28, 0), V(2.3f, 2.8f, 7.5f), 1.4f, Stone)
               .Limb(h + V(0, 28, 7), h + V(0, 25, 14), 2.2f, 0.4f, Stone)
               .Limb(h + V(0, 28, -7), h + V(0, 26, -12), 2.2f, 0.7f, StoneDark)
               .Rod(h + V(0, 25, 0), h + V(0, 31, 0), 1.9f, Rope)
               .Mottle(Stone, StoneDark, 3, 0.3f, 4);
        return Grids(s);
    }

    // ---- Wolves -------------------------------------------------------------------------------------

    private static readonly Role[] WolfRoles = { Role.LegA, Role.LegB, Role.LegB, Role.LegA, Role.Body, Role.Head, Role.Fixed };

    /// <summary>A wolf's pivots: front legs, hind legs, body, neck, tail; scaled by <paramref name="k"/> and moved by <paramref name="o"/>.</summary>
    private static Vector3[] WolfPivots(float k, Vector3 o) => new[]
    {
        o + V(-4, 18, 9) * k, o + V(4, 18, 9) * k, o + V(-4, 18, -11) * k, o + V(4, 18, -11) * k,
        o + V(0, 19, 0) * k, o + V(0, 23, 11) * k, o + V(0, 22, -16) * k,
    };

    /// <summary>The seven sculpts of a wolf (four legs, body, head, tail) scaled by <paramref name="k"/>.</summary>
    private static Sculpt[] WolfParts(int step, float k)
    {
        Sculpt S(float x0, float y0, float z0, float x1, float y1, float z1) => new(step, x0 * k, y0 * k, z0 * k, x1 * k, y1 * k, z1 * k);
        return new[]
        {
            S(-9, -1, 4, 0, 22, 16), S(0, -1, 4, 9, 22, 16), S(-9, -1, -18, 0, 24, -6), S(0, -1, -18, 9, 24, -6),
            S(-12, 8, -20, 12, 32, 18), S(-9, 17, 6, 9, 40, 31), S(-5, 6, -30, 5, 26, -12),
        };
    }

    /// <summary>
    /// A wolf into its seven sculpts (front left, front right, hind left, hind right, body, head, tail), scaled by
    /// <paramref name="k"/>: a lean grey body with a pale belly and muzzle, a darker back, a ruff, amber eyes,
    /// and a collar in its owner's colour.
    /// </summary>
    private static void WolfInto(Sculpt[] s, float k, FineMaterial coat, FineMaterial belly, FineMaterial back, int seed)
    {
        Vector3 P(float x, float y, float z) => V(x, y, z) * k;
        float R(float r) => r * k;
        var body = s[4];
        body.Ball(P(0, 19, -1), V(6, 6.2f, 12) * k, coat)
            .Ball(P(0, 19.5f, 7.5f), V(6.6f, 7.2f, 6) * k, coat)
            .Ball(P(0, 19, -10), V(6.2f, 6.5f, 6) * k, coat)
            .Limb(P(0, 21, 9), P(0, 26, 14), R(5.2f), R(4.6f), coat)
            .Ball(P(0, 22.5f, 11), V(7, 6.5f, 4.6f) * k, coat);                                                   // ruff
        body.Box(-R(9), R(10), -R(16), R(9), R(15.2f), R(10), belly, Sculpt.Op.Paint)
            .Box(-R(2.6f), R(24), -R(18), R(2.6f), R(28), R(8), back, Sculpt.Op.Paint)
            .Mottle(coat, back, 4 * k, 0.3f, seed).Mottle(coat, belly, 5 * k, 0.15f, seed + 1)
            .Erode(2 * k, 0.12f, seed + 2, coat, back)
            .Limb(P(0, 23.6f, 12.8f), P(0, 25, 13.8f), R(5.4f), R(5), Team)                                      // collar
            .Ball(P(-4.4f, 24.4f, 13.6f), R(0.9f), Iron).Ball(P(4.4f, 24.4f, 13.6f), R(0.9f), Iron);
        var head = s[5];
        head.Ball(P(0, 27, 16.5f), V(4.8f, 4.6f, 5.2f) * k, coat)
            .Ball(P(-2.8f, 25.5f, 18), V(2.4f, 2.4f, 2.6f) * k, belly).Ball(P(2.8f, 25.5f, 18), V(2.4f, 2.4f, 2.6f) * k, belly)
            .Limb(P(0, 25.8f, 19.5f), P(0, 24.8f, 26.5f), R(2.9f), R(2.1f), coat)
            .Box(-R(4), R(22), R(19), R(4), R(24.6f), R(28), belly, Sculpt.Op.Paint)
            .Box(-R(3), R(23.2f), R(20), R(3), R(23.9f), R(28), Dark, Sculpt.Op.Paint)                         // mouth
            .Ball(P(0, 25.7f, 27.2f), V(1.5f, 1.2f, 1.2f) * k, Dark);
        foreach (int sd in new[] { -1, 1 })
            head.Box(sd * R(1.5f), R(27.6f), R(20.2f), sd * R(3.3f), R(28.8f), R(21.8f), WolfEye)
                .Limb(P(sd * 2.8f, 30.5f, 15.5f), P(sd * 3.6f, 35.5f, 15), R(2.1f), R(0.5f), coat)
                .Limb(P(sd * 2.8f, 31, 16.2f), P(sd * 3.5f, 34, 16), R(1), R(0.4f), Dark);
        head.Box(-R(5), R(30), R(10), R(5), R(36), R(13.5f), back, Sculpt.Op.Paint);
        for (int f = 0; f < 2; f++)
        {
            int sd = f == 0 ? -1 : 1;
            s[f].Limb(P(sd * 4, 18, 9), P(sd * 4, 8, 10), R(2.9f), R(2.2f), coat)
                .Limb(P(sd * 4, 8, 10), P(sd * 4, 1.5f, 10.5f), R(2.1f), R(1.8f), coat)
                .Ball(P(sd * 4, 1.4f, 11.7f), V(2.3f, 1.4f, 2.8f) * k, back);
            s[f + 2].Ball(P(sd * 4, 16, -11), V(3.2f, 5, 4.5f) * k, coat)
                .Limb(P(sd * 4, 15, -11), P(sd * 4, 8, -14), R(3.4f), R(2.1f), coat)
                .Limb(P(sd * 4, 8, -14), P(sd * 4, 1.5f, -12), R(2), R(1.8f), coat)
                .Ball(P(sd * 4, 1.4f, -11), V(2.3f, 1.4f, 2.8f) * k, back);
        }
        s[6].Limb(P(0, 22, -16), P(0, 16, -24), R(2.7f), R(2.2f), coat)
            .Limb(P(0, 16, -24), P(0, 11, -27), R(2.2f), R(1.1f), back)
            .Erode(2 * k, 0.15f, seed + 3, coat);
    }

    private static FineGrid[] Wolf(int step)
    {
        var s = WolfParts(step, 1f);
        WolfInto(s, 1f, Fur, FurPale, FurDark, 21);
        return Grids(s);
    }

    // ---- Wolf rider ---------------------------------------------------------------------------------

    private const float RiderWolf = 1.3f;
    /// <summary>Where the riding orc's own origin (its feet, were it standing) lies.</summary>
    private static readonly Vector3 RiderAt = V(0, 13.2f, -1.5f);

    private static readonly Role[] RiderRoles =
        { Role.LegA, Role.LegB, Role.LegB, Role.LegA, Role.Body, Role.Head, Role.Fixed, Role.Fixed, Role.ArmA, Role.ArmB };

    private static Vector3[] RiderPivots()
    {
        var wolf = WolfPivots(RiderWolf, Vector3.Zero);
        var p = new Vector3[10];
        Array.Copy(wolf, p, 7);
        p[7] = RiderAt + OrcPivots[Head];
        p[8] = RiderAt + OrcPivots[ArmL];
        p[9] = RiderAt + OrcPivots[ArmR];
        return p;
    }

    /// <summary>
    /// A big dark wolf with a saddle and a cloth in its owner's colour down its flanks, and an orc astride it with
    /// a horned helm and a bone-tipped spear. Parts: the wolf's seven, then the orc's head and arms (its body
    /// and legs ride on the wolf's body).
    /// </summary>
    private static FineGrid[] WolfRider(int step)
    {
        var w = WolfParts(step, RiderWolf);
        var s = new Sculpt[10];
        Array.Copy(w, s, 7);
        // The wolf's body sculpt also holds the orc's body and legs, so it has to reach up to them.
        s[4] = new Sculpt(step, -26, 8, -26, 26, 82, 24);
        s[7] = new Sculpt(step, -18, 50, -12, 18, 86, 20);
        s[8] = new Sculpt(step, -36, 10, -16, 0, 84, 22);
        s[9] = new Sculpt(step, 0, 10, -16, 36, 120, 22);
        WolfInto(s, RiderWolf, M(0.52f, 0.5f, 0.48f), FurPale, FurDark, 31);
        float k = RiderWolf;
        var body = s[4];
        body.RoundBox(V(0, 22.5f, -1) * k, V(6.8f, 4.2f, 7.2f) * k, 1.5f, TeamDark)                               // cloth down the flanks
            .RoundBox(V(0, 24.5f, -1) * k, V(7, 0.8f, 7.4f) * k, 0.5f, Team)
            .RoundBox(V(0, 26.2f, -1.5f) * k, V(5.2f, 1.6f, 5.6f) * k, 1.2f, Leather)                             // saddle
            .RoundBox(V(0, 27.6f, -7) * k, V(4.6f, 1.6f, 1.2f) * k, 0.6f, LeatherDark);
        var o = RiderAt;
        Orc(body, body, body, s[7], s[8], s[9], o, legs: false, seed: 13);
        // Legs astride: thighs forward over the wolf's flanks, shins down, feet in hide wraps.
        foreach (int sd in new[] { -1, 1 })
            body.Limb(o + V(sd * 7, 23, 0), o + V(sd * 10.5f, 18, 6), 4.6f, 4.1f, Hide)
                .Limb(o + V(sd * 10.5f, 18, 6), o + V(sd * 10.8f, 9, 3.5f), 3.8f, 3.3f, OrcSkin)
                .RoundBox(o + V(sd * 10.8f, 7, 5.5f), V(3.6f, 2.4f, 5), 1.4f, HideDark);
        HornedHelm(s[7], o);
        Pauldron(s[9], o, 1);
        FurShoulder(s[8], o, -1, 15);
        Spear(s[9], o + V(16.5f, 19.5f, 4), 90);
        return Grids(s);
    }

    // ---- Golem --------------------------------------------------------------------------------------

    private static readonly FineMaterial[] Stones = { Stone, StoneLight, StoneWarm, StoneDark };

    /// <summary>Weathers stone: patches of lighter, warmer and darker stone, chipped edges, and moss on what faces up.</summary>
    private static void Weather(Sculpt s, int seed, float moss = 0.5f)
    {
        s.Mottle(Stone, StoneLight, 14, 0.3f, seed).Mottle(Stone, StoneWarm, 19, 0.25f, seed + 1)
         .Mottle(Stone, StoneDark, 8, 0.14f, seed + 2)
         .Erode(5, 0.12f, seed + 3, Stones)
         .Mottle(Stone, Moss, 7, 0.07f, seed + 4)
         .Moss(Moss, MossDark, 9, moss, seed + 5, Stones);
    }

    /// <summary>
    /// A stone golem 200 units (3.1 m) tall: short thick legs, a huge hunched chest, long arms with fists that
    /// near the ground, and a small head sunk forward between its shoulders with glowing eyes. It wears iron
    /// pauldrons and bracers with rivets, knee plates, a leather belt with an iron buckle plate, a strap across
    /// its chest and a tattered loincloth in its owner's colour; moss grows on everything that faces up. An orc
    /// rides on its back, a bone club raised. Parts: legs, body, head, arms, and the rider.
    /// </summary>
    private static FineGrid[] Golem(int step)
    {
        var legL = new Sculpt(step, -56, -2, -22, -4, 102, 38);
        var legR = new Sculpt(step, 4, -2, -22, 56, 102, 38);
        var body = new Sculpt(step, -62, 36, -44, 62, 186, 50);
        var head = new Sculpt(step, -26, 136, 12, 26, 186, 62);
        var armL = new Sculpt(step, -112, 10, -26, -24, 182, 58);
        var armR = new Sculpt(step, 24, 10, -26, 112, 182, 58);
        var rider = new Sculpt(step, -42, 168, -26, 30, 262, 24);

        foreach (int sd in new[] { -1, 1 })
        {
            var leg = sd < 0 ? legL : legR;
            float x = sd * 30;
            leg.Limb(V(sd * 26, 80, 0), V(x, 48, 2), 17, 15, Stone)
               .Ball(V(x, 46, 4), 15, Stone)
               .RoundBox(V(x, 28, 3), V(14, 18, 14), 6, Stone)
               .RoundBox(V(x, 8, 7), V(15.5f, 8, 19), 5, Stone);
            for (int t = -1; t <= 1; t++) leg.RoundBox(V(x + t * 10, 6, 25), V(4.6f, 6, 5), 2.5f, StoneDark);
            Weather(leg, 40 + sd);
            leg.Plate(V(x, 47, 18.5f), V(10, 10, 2.4f), V(0, 0, 1), Iron, IronDark, IronLight);
        }

        // Body.
        body.RoundBox(V(0, 86, 0), V(32, 13, 22), 8, Stone)
            .Ball(V(0, 108, 6), V(34, 26, 26), Stone)
            .Ball(V(0, 138, 10), V(50, 32, 34), Stone)
            .Ball(V(0, 152, -8), V(42, 28, 30), Stone)
            .RoundBox(V(-20, 140, 34), V(18, 16, 8), 6, Stone).RoundBox(V(20, 140, 34), V(18, 16, 8), 6, Stone)
            .RoundBox(V(0, 116, 28), V(16, 10, 6), 4, Stone);
        Weather(body, 50, 0.55f);
        body.Band(V(0, 128, 0), V(0.75f, 1, 0), 3.2f, Leather)                                                    // strap across the chest
            .RoundBox(V(0, 96.5f, 2), V(34, 3.8f, 25.5f), 3, Leather)                                           // belt
            .RoundBox(V(0, 99.5f, 2), V(34.3f, 0.8f, 25.8f), 0.6f, LeatherDark)
            .Plate(V(0, 97, 27), V(11, 8, 2.2f), V(0, 0, 1), Iron, IronDark, IronLight);
        // The loincloth: strips in the owner's colour, cut ragged, front and back.
        for (int k = 0; k < 7; k++)
        {
            float x = -14 + k * 4;
            float len = 30 + 12 * Sculpt.Hash(k, 1, 2, 3);
            body.Box(x, 93, 25.5f, x + 3.8f, 93 - len, 29, k % 2 == 0 ? Team : TeamDark);
            float back = 22 + 10 * Sculpt.Hash(k, 4, 5, 6);
            body.Box(x, 93, -21.5f, x + 3.8f, 93 - back, -25, k % 2 == 0 ? TeamDark : TeamDeep);
        }

        // Head.
        head.RoundBox(V(0, 164, 38), V(16, 15, 15), 7, Stone)
            .RoundBox(V(0, 153, 42), V(13, 6, 12), 4, Stone);
        Weather(head, 60, 0.6f);
        head.RoundBox(V(0, 171.5f, 51.5f), V(15.5f, 4, 4), 2, StoneDark);
        foreach (int sd in new[] { -1, 1 }) head.Box(sd * 4, 164, 51.5f, sd * 10, 167.5f, 53.6f, GolemEye);

        // Arms: shoulder, upper arm, elbow, a thick forearm and a fist with knuckles; pauldrons and bracers of iron.
        foreach (int sd in new[] { -1, 1 })
        {
            var arm = sd < 0 ? armL : armR;
            arm.Ball(V(sd * 56, 148, 5), 24, Stone)
               .Limb(V(sd * 60, 142, 6), V(sd * 70, 98, 16), 19, 16, Stone)
               .Ball(V(sd * 70, 96, 16), 17, Stone)
               .Limb(V(sd * 70, 96, 16), V(sd * 73, 52, 26), 18, 22, Stone)
               .RoundBox(V(sd * 73, 34, 28), V(19, 17, 19), 8, Stone);
            for (int t = 0; t < 4; t++) arm.Ball(V(sd * (59 + t * 9.5f), 24, 41), 6.5f, StoneDark);
            Weather(arm, 70 + sd);
            arm.Limb(V(sd * 70.6f, 86, 18), V(sd * 72.3f, 62, 24), 22.5f, 23.5f, Iron)
               .Limb(V(sd * 71, 80, 19.5f), V(sd * 71.3f, 77, 20.3f), 23.3f, 23.6f, IronDark)
               .Limb(V(sd * 71.9f, 67, 22.6f), V(sd * 72.1f, 64, 23.4f), 24, 24.3f, IronDark)
               .Limb(V(sd * 72.5f, 59, 24.6f), V(sd * 72.8f, 54, 25.4f), 23.8f, 24.2f, Leather)
               .Plate(V(sd * 95, 73, 21), V(3, 14, 14), V(sd, 0, 0), IronLight, Iron, IronLight)
               .Plate(V(sd * 60, 171, 5), V(27, 5, 27), V(0, 1, 0), Iron, IronDark, IronLight, 3)
               .Plate(V(sd * 84, 150, 5), V(4, 20, 25), V(sd, 0, 0), Iron, IronDark, IronLight, 3)
               .RoundBox(V(sd * 60, 177, 5), V(4, 2.5f, 20), 1.5f, IronDark);                                    // ridge along the top
            arm.Moss(Moss, MossDark, 8, 0.25f, 80 + sd, Iron, IronDark);
        }

        // The rider on its back, waving a bone club.
        var o = V(-8, 176, -6);
        Orc(rider, rider, rider, rider, rider, rider, o, raise: true, seed: 90);
        HornedHelm(rider, o);
        FurShoulder(rider, o, -1, 91);
        FurShoulder(rider, o, 1, 92);
        rider.Limb(o + V(20, 60, 3.5f), o + V(23, 80, 6), 1.6f, 2.2f, Bone)
             .Ball(o + V(23.3f, 81, 6.2f), V(3.2f, 2.6f, 2.8f), Bone).Ball(o + V(22.6f, 79, 5.8f), V(2.6f, 2.4f, 2.4f), BoneDark);

        return new[] { legL.Grid, legR.Grid, body.Grid, head.Grid, armL.Grid, armR.Grid, rider.Grid };
    }
}
