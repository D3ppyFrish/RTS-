using System;
using System.Collections.Generic;

namespace Rts.Sim.Automation;

/// <summary>A kind of work an automatic worker can take on its own (see <see cref="WorkPriorities"/>).</summary>
public enum WorkKind : byte
{
    /// <summary>Help build a site that can use another builder.</summary>
    Build = 1,
    /// <summary>Work a farm with a free place.</summary>
    Farm,
    /// <summary>Keep a smelter going: carry ore and coal in, iron out.</summary>
    Smelt,
    /// <summary>Gather whatever good is needed most, shared out over the gatherers (the usual rule).</summary>
    Needed,
    Food,
    Wood,
    Stone,
    IronOre,
    Coal,
    /// <summary>Supply duty: carry goods to storage that is short under its supply rules (see <see cref="SupplyCommand"/>).</summary>
    Supply,
}

/// <summary>
/// A worker's priority list: the kinds of work it takes on its own, in order. An idle automatic worker
/// first takes what it carries to storage, then goes down its list and takes the first kind of work it
/// finds; kinds left off the list it never takes on its own. Packed into a <see cref="ulong"/>, four bits
/// per kind, first kind in the lowest bits, so a list is plain state that hashes and fits a command.
/// A worker whose list is <see cref="UseDefault"/> follows its player's default list.
/// </summary>
public static class WorkPriorities
{
    /// <summary>The worker follows its player's default list.</summary>
    public const ulong UseDefault = 0;
    /// <summary>A list with nothing on it: the worker takes no work on its own (it still unloads).</summary>
    public const ulong Nothing = 0xF;

    /// <summary>Every kind, in the order a list shows the kinds left off it.</summary>
    public static readonly WorkKind[] All =
    {
        WorkKind.Build, WorkKind.Farm, WorkKind.Smelt, WorkKind.Supply, WorkKind.Needed,
        WorkKind.Food, WorkKind.Wood, WorkKind.Stone, WorkKind.IronOre, WorkKind.Coal,
    };

    /// <summary>The list players start with: build, farm, smelt, supply, then gather what is needed most.</summary>
    public static readonly ulong Standard = Pack(WorkKind.Build, WorkKind.Farm, WorkKind.Smelt, WorkKind.Supply, WorkKind.Needed);

    /// <summary>Packs kinds into a list, in order; repeats and unknown kinds are left out.</summary>
    public static ulong Pack(params WorkKind[] kinds) => Pack((IReadOnlyList<WorkKind>)kinds);

    public static ulong Pack(IReadOnlyList<WorkKind> kinds)
    {
        ulong list = 0;
        int n = 0;
        uint seen = 0;
        foreach (var k in kinds)
        {
            if (!Enum.IsDefined(k) || (seen & 1u << (int)k) != 0) continue;
            seen |= 1u << (int)k;
            list |= (ulong)k << (4 * n++);
        }
        return n == 0 ? Nothing : list;
    }

    /// <summary>The kinds on a list, in order (none for <see cref="UseDefault"/> or <see cref="Nothing"/>).</summary>
    public static List<WorkKind> Unpack(ulong list)
    {
        var kinds = new List<WorkKind>();
        uint seen = 0;
        for (int n = 0; n < 16 && list != 0; n++, list >>= 4)
        {
            var k = (WorkKind)(list & 0xF);
            if (k == 0 || !Enum.IsDefined(k)) break;
            if ((seen & 1u << (int)k) != 0) continue;
            seen |= 1u << (int)k;
            kinds.Add(k);
        }
        return kinds;
    }

    /// <summary>The list with one kind moved up (-1) or down (+1) a place; unchanged if it can't move.</summary>
    public static ulong Move(ulong list, WorkKind kind, int by)
    {
        var kinds = Unpack(list);
        int at = kinds.IndexOf(kind), to = at + by;
        if (at < 0 || to < 0 || to >= kinds.Count) return list;
        (kinds[at], kinds[to]) = (kinds[to], kinds[at]);
        return Pack(kinds);
    }

    /// <summary>The list with a kind put on (at the bottom) or taken off.</summary>
    public static ulong Toggle(ulong list, WorkKind kind)
    {
        var kinds = Unpack(list);
        if (!kinds.Remove(kind)) kinds.Add(kind);
        return Pack(kinds);
    }

    /// <summary>The good a gathering kind is for, or null for the other kinds.</summary>
    public static Economy.Good? GoodOf(WorkKind kind) => kind switch
    {
        WorkKind.Food => Economy.Good.Food,
        WorkKind.Wood => Economy.Good.Wood,
        WorkKind.Stone => Economy.Good.Stone,
        WorkKind.IronOre => Economy.Good.IronOre,
        WorkKind.Coal => Economy.Good.Coal,
        _ => null,
    };

    /// <summary>The kind's name as players read it.</summary>
    public static string Label(WorkKind kind) => kind switch
    {
        WorkKind.Build => "Build",
        WorkKind.Farm => "Farm",
        WorkKind.Smelt => "Smelt",
        WorkKind.Supply => "Supply storehouses",
        WorkKind.Needed => "Gather what's needed",
        WorkKind.Food => "Gather food",
        WorkKind.Wood => "Gather wood",
        WorkKind.Stone => "Gather stone",
        WorkKind.IronOre => "Gather iron ore",
        WorkKind.Coal => "Gather coal",
        _ => kind.ToString(),
    };

    /// <summary>The kind's name in the command log.</summary>
    public static string LogName(WorkKind kind) => kind switch
    {
        WorkKind.Build => "build",
        WorkKind.Farm => "farm",
        WorkKind.Smelt => "smelt",
        WorkKind.Supply => "supply",
        WorkKind.Needed => "needed",
        WorkKind.Food => "food",
        WorkKind.Wood => "wood",
        WorkKind.Stone => "stone",
        WorkKind.IronOre => "iron-ore",
        WorkKind.Coal => "coal",
        _ => throw new NotSupportedException($"No log name for work kind {(int)kind}"),
    };

    /// <summary>A list as the log writes it: "default", "none", or its kinds joined by commas.</summary>
    public static string ToLog(ulong list) => list == UseDefault ? "default"
        : Unpack(list) is { Count: > 0 } kinds ? string.Join(',', kinds.ConvertAll(LogName)) : "none";

    public static ulong ParseLog(string s)
    {
        if (s == "default") return UseDefault;
        if (s == "none") return Nothing;
        var kinds = new List<WorkKind>();
        foreach (var part in s.Split(','))
        {
            var k = Array.Find(All, a => LogName(a) == part);
            if (k == 0) throw new FormatException($"Unknown work kind '{part}'");
            kinds.Add(k);
        }
        return Pack(kinds);
    }
}
