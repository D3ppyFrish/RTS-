using System;

namespace Rts.Sim.Economy;

/// <summary>
/// Goods of the slice economy. Every unit of goods exists somewhere: in a storehouse, in a processing
/// building, or carried by a worker.
/// </summary>
public enum Good : byte
{
    Food,
    Wood,
    Stone,
    IronOre,
    Coal,
    /// <summary>Iron ingots, made by the smelter.</summary>
    Iron,
    /// <summary>Crystal dug from the cavern walls; a shrine channels it into mana (see <see cref="BuildingKind.Shrine"/>).</summary>
    Crystal,
    /// <summary>
    /// Gold dug from the veins in the hills and the caves' walls; not processed. It pays the city-states'
    /// mercenaries, and the goblins pay their tribute in it.
    /// </summary>
    Gold,
}

/// <summary>An amount of one good, as in a cost.</summary>
public readonly record struct GoodAmount(Good Good, int Amount);

public static class Goods
{
    public const int Count = 8;

    public static readonly Good[] All = { Good.Food, Good.Wood, Good.Stone, Good.IronOre, Good.Coal, Good.Iron, Good.Crystal, Good.Gold };

    public static string Name(Good g) => g switch
    {
        Good.Food => "Food",
        Good.Wood => "Wood",
        Good.Stone => "Stone",
        Good.IronOre => "Iron ore",
        Good.Coal => "Coal",
        Good.Iron => "Iron",
        Good.Crystal => "Crystal",
        Good.Gold => "Gold",
        _ => throw new ArgumentOutOfRangeException(nameof(g)),
    };

    /// <summary>Parses a good by its enum name (as in data files and command logs).</summary>
    public static Good Parse(string s) =>
        Enum.TryParse<Good>(s, ignoreCase: false, out var g) && Enum.IsDefined(g) && !int.TryParse(s, out _)
            ? g : throw new FormatException($"Unknown good '{s}'");
}
