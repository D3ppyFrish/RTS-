using Rts.Sim.Economy;

namespace Rts.Sim.Content;

/// <summary>
/// What was left in one of the map's old ruins, for the first player whose units search it (see
/// <see cref="World.RuinSystem"/>): goods put into their storage nearest the ruin, a relic for the searcher to take
/// up, or both, and a line of carving or writing found with it that tells a little of who built the ruins.
/// Defined in <c>data/ruins.json</c>.
/// </summary>
public sealed class RuinFindDef
{
    public required string Id { get; init; }
    /// <summary>What it is, as the alert names it ("a sealed granary").</summary>
    public required string Name { get; init; }
    /// <summary>The words found with it: the story the ruins tell.</summary>
    public string Lore { get; init; } = "";
    /// <summary>Goods put into the finder's storage nearest the ruin.</summary>
    public GoodAmount[] Goods { get; init; } = System.Array.Empty<GoodAmount>();
    /// <summary>A relic the searcher takes up (or leaves lying for one who can), or null.</summary>
    public RelicDef? Relic { get; init; }
    /// <summary>Position in <see cref="ContentDb.RuinFinds"/>.</summary>
    public int Index { get; internal set; }
}
