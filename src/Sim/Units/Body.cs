using System;

namespace Rts.Sim.Units;

/// <summary>
/// Where a hit lands. A beast (<see cref="BodyPlan.Beast"/>) has no arms: its <see cref="Torso"/> is its body.
/// The values index per-part arrays (<see cref="UnitDef.Armour"/>, <see cref="UnitStore.Wounds"/>).
/// </summary>
public enum BodyPart : byte
{
    Head,
    Torso,
    Arms,
    Legs,
}

/// <summary>The shape of a unit's body, which decides the parts a hit can land on and how likely each is.</summary>
public enum BodyPlan : byte
{
    /// <summary>Head, torso, arms and legs.</summary>
    Humanoid,
    /// <summary>A four-legged monster or mount: head, body and legs.</summary>
    Beast,
}

/// <summary>
/// How a weapon hurts. Armour stops some kinds better than others (<see cref="Body.ArmourPercent"/>), and the
/// kind decides whether the wound bleeds (<see cref="Body.Bleeds"/>).
/// </summary>
public enum AttackKind : byte
{
    /// <summary>Swords, axes, claws: bleeds, but armour stops it best.</summary>
    Slash,
    /// <summary>Spears, arrows, lances: bleeds, and armour counts in full.</summary>
    Pierce,
    /// <summary>Hammers, clubs, stones, blasts: does not bleed, but goes through half of the armour.</summary>
    Crush,
}

/// <summary>
/// The body rules: the odds a hit lands on each part, what each part multiplies damage by, and how armour
/// works against each kind of attack (see <see cref="CombatSystem.HitDamage"/>). All numbers are starting
/// guesses to be tuned in playtests.
/// </summary>
public static class Body
{
    public const int Parts = 4;

    /// <summary>A hit always does at least this percentage of what it would do with no armour (and at least 1).</summary>
    public const int ArmourFloorPercent = 25;

    // Odds (percent) of each part, by plan, for a hit from level ground: [head, torso, arms, legs].
    private static readonly int[] HumanoidOdds = { 10, 50, 20, 20 };
    private static readonly int[] BeastOdds = { 15, 60, 0, 25 };

    // Damage multiplier (percent) of each part, by plan.
    private static readonly int[] HumanoidHurt = { 150, 100, 70, 85 };
    private static readonly int[] BeastHurt = { 150, 100, 0, 70 };

    /// <summary>Percentage points of the odds that move to the head from the legs for a shot from the high ground, or a blow from above.</summary>
    public const int HighShift = 10;
    /// <summary>Percentage points that move to the legs for a blow struck uphill (from the head first, then the torso).</summary>
    public const int UphillShift = 20;
    /// <summary>A tower's aim (see <see cref="Economy.BuildingDef.Aim"/>) moves a point of the odds to the head for each this many.</summary>
    public const int AimShare = 5;

    /// <summary>How hard a hit on the part hurts, in percent.</summary>
    public static int HurtPercent(BodyPlan plan, BodyPart part) => (plan == BodyPlan.Beast ? BeastHurt : HumanoidHurt)[(int)part];

    /// <summary>The part's share of hits from level ground, in percent (0 for a part the plan does not have).</summary>
    public static int Odds(BodyPlan plan, BodyPart part) => (plan == BodyPlan.Beast ? BeastOdds : HumanoidOdds)[(int)part];

    /// <summary>Whether the plan has the part (beasts have no arms).</summary>
    public static bool Has(BodyPlan plan, BodyPart part) => Odds(plan, part) > 0;

    /// <summary>The share of a part's armour that counts against the kind of attack, in percent.</summary>
    public static int ArmourPercent(AttackKind kind) => kind switch
    {
        AttackKind.Slash => 150,
        AttackKind.Pierce => 100,
        _ => 50,
    };

    /// <summary>Whether a wound of this kind bleeds.</summary>
    public static bool Bleeds(AttackKind kind) => kind != AttackKind.Crush;

    /// <summary>How the attacker stands against the target, which moves the odds.</summary>
    public enum Angle : byte
    {
        Level,
        /// <summary>A ranged shot from <see cref="CombatSystem.HighGround"/> levels or more above, or a melee blow from above.</summary>
        FromAbove,
        /// <summary>A melee blow on a target standing higher.</summary>
        Uphill,
    }

    /// <summary>
    /// The part a roll (0 to 99) lands on, for a body of the plan hit from the angle. From above, the head
    /// takes <see cref="HighShift"/> points off the legs; uphill, the legs take <see cref="UphillShift"/>
    /// points off the head and then the torso. An aimed shot (from a tower) moves <paramref name="aimed"/>
    /// points more to the head, off the legs and then the arms.
    /// </summary>
    public static BodyPart Pick(BodyPlan plan, Angle angle, int roll, int aimed = 0)
    {
        var o = plan == BodyPlan.Beast ? BeastOdds : HumanoidOdds;
        int head = o[0], torso = o[1], arms = o[2], legs = o[3];
        if (angle == Angle.FromAbove)
        {
            int move = Math.Min(HighShift, legs);
            head += move;
            legs -= move;
        }
        else if (angle == Angle.Uphill)
        {
            int fromHead = Math.Min(UphillShift, head);
            int fromTorso = Math.Min(UphillShift - fromHead, torso);
            head -= fromHead;
            torso -= fromTorso;
            legs += fromHead + fromTorso;
        }
        if (aimed > 0)
        {
            int fromLegs = Math.Min(aimed, legs);
            int fromArms = Math.Min(aimed - fromLegs, arms);
            legs -= fromLegs;
            arms -= fromArms;
            head += fromLegs + fromArms;
        }
        if (roll < head) return BodyPart.Head;
        roll -= head;
        if (roll < torso) return BodyPart.Torso;
        roll -= torso;
        if (roll < arms) return BodyPart.Arms;
        return BodyPart.Legs;
    }

    /// <summary>
    /// What a hit of <paramref name="raw"/> (after the role table) does to a part: times the part's
    /// multiplier, less the share of the part's armour that counts against the kind, but never under
    /// <see cref="ArmourFloorPercent"/> of the hit and never under 1.
    /// </summary>
    public static int Damage(int raw, BodyPlan plan, BodyPart part, AttackKind kind, int armour)
    {
        long hit = (long)raw * HurtPercent(plan, part); // in hundredths
        long stopped = (long)armour * ArmourPercent(kind);
        long floor = hit * ArmourFloorPercent / 100;
        long left = Math.Max(hit - stopped, floor);
        return (int)Math.Max(1, (left + 50) / 100);
    }
}
