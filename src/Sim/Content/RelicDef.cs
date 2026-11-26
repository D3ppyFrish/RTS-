namespace Rts.Sim.Content;

/// <summary>
/// A relic: a one-of-a-kind thing a unit takes up as the prize for beating a monster (a lair's prize, see
/// <see cref="DenUse.Relic"/> and <see cref="Units.MonsterSystem"/>). Its bearer keeps it, through evolving too,
/// and it is gone with the bearer. A unit bears at most one. Defined in <c>data/relics.json</c>.
/// </summary>
public sealed class RelicDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    /// <summary>The pixel icon's name (see the game's UiIcons).</summary>
    public string Icon { get; init; } = "";
    /// <summary>Hit points added to the bearer's most.</summary>
    public int Hp { get; init; }
    /// <summary>Damage added to each of the bearer's hits.</summary>
    public int Damage { get; init; }
    /// <summary>Armour added on every part of the bearer.</summary>
    public int Armour { get; init; }
    /// <summary>The bearer's blows web whom they strike, as a giant spider's bite does (see <see cref="Units.UnitDef.Web"/>); null for none.</summary>
    public Units.WebDef? Web { get; init; }
    /// <summary>Position in <see cref="ContentDb.Relics"/>.</summary>
    public int Index { get; internal set; }
}
