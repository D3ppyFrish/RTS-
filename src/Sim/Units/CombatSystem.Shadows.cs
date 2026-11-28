using Rts.Sim.World;

namespace Rts.Sim.Units;

/// <summary>
/// Clan Kage, the ratfolk ninja clan (see "Clan Kage" in docs/rules.md). Their shinobi and kunoichi have
/// <b>camouflage</b> (<see cref="UnitDef.Camouflage"/>): standing still with nothing to fight they are hidden, as a
/// Reptilian lying in the water is (<see cref="Lurking"/>, <see cref="Hiding"/>), seen by their enemies only from close
/// by and lying in wait; with Strike from the shadows (an <see cref="Content.TechEffectType.Ambush"/> tech) their first
/// blow out of hiding deals more. All their units are <b>shadowborn</b> (<see cref="UnitDef.Shadowborn"/>): softer out
/// in the daylight, harder in the shadowed wilds (<see cref="ShadowPercent"/>). The kunoichi is an <b>assassin</b>
/// (<see cref="UnitDef.Assassin"/>): her blows deal more to workers, casters and heroes (<see cref="Marked"/>).
/// </summary>
public sealed partial class CombatSystem
{
    /// <summary>Percent of its damage a shadowborn unit deals out in the daylight.</summary>
    public const int LightPercent = 80;
    /// <summary>Percent of its damage a shadowborn unit deals in the shadowed wilds.</summary>
    public const int WildsPercent = 125;

    /// <summary>
    /// True if unit i stands still: no order to go anywhere, no job, no combat target (a unit or a building) and no order
    /// to attack, move-attack, patrol or guard. A unit with camouflage standing still is hidden (<see cref="Lurking"/>).
    /// </summary>
    private bool StandsStill(int i)
    {
        var u = U;
        return u.Order[i] == UnitOrder.Idle && u.Job[i] == UnitJob.None && u.TargetBuilding[i] == 0
            && u.Combat[i] == CombatOrder.Auto;
    }

    /// <summary>True if unit i is a player's unit with camouflage hiding as it stands still (Clan Kage's shinobi and kunoichi).</summary>
    public bool Hiding(int i) => (uint)i < (uint)U.Count && Def(i).Camouflage && Lurking(i);

    /// <summary>
    /// Percent of its damage shadowborn unit i deals where it stands: <see cref="LightPercent"/> out in the daylight
    /// (<see cref="Conditions.InDaylight"/>: by day under the open sky), <see cref="WildsPercent"/> in the shadowed
    /// wilds at any hour, otherwise 100 (at night, at dawn and dusk, underground, and with day and night off).
    /// </summary>
    public int ShadowPercent(int i)
    {
        var u = U;
        int x = u.X[i].FloorToInt(), z = u.Z[i].FloorToInt();
        if (_sim.Map.BiomeAt(x, z) == Biome.Wilds) return WildsPercent;
        return _sim.Conditions.InDaylight(x, u.Y[i], z) ? LightPercent : 100;
    }

    /// <summary>True if a unit of this kind is an assassin's mark (<see cref="UnitDef.Assassin"/>): a worker, a caster or a hero.</summary>
    public static bool Marked(UnitDef target) => target.Role is UnitRole.Worker or UnitRole.Caster || target.Hero;
}
