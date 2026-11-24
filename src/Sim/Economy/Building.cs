using System;
using System.Collections.Generic;

namespace Rts.Sim.Economy;

public enum BuildingState : byte
{
    /// <summary>Placed and paid for; workers are building it, and its blocks go up as they work.</summary>
    Site,
    /// <summary>Finished and working.</summary>
    Active,
    /// <summary>
    /// A plan: the player's mark for a building to come, neither paid for nor worked on, its ground untouched.
    /// It keeps its footprint free of other buildings; a <see cref="WorkCommand"/> on it pays and turns it into a
    /// <see cref="Site"/> (see <see cref="PlanCommand"/>).
    /// </summary>
    Planned,
}

public enum RaidKind : byte
{
    None,
    Capture,
    Burn,
}

/// <summary>
/// A placed building. Its footprint is columns [X, X + SizeX) by [Z, Z + SizeZ); Y is the level units
/// stand at around it, so its first layer of blocks is at Y (a farm's field is at Y - 1).
/// </summary>
/// <summary>What a town hall's governor builds and trains for, beyond keeping the settlement housed and fed (see <see cref="GovernCommand"/>).</summary>
public enum GovernorFocus : byte
{
    /// <summary>No governor: the player runs the settlement.</summary>
    None,
    /// <summary>More workers, a storehouse and a smelter: a bigger economy.</summary>
    Growth,
    /// <summary>A barracks and a steady stream of mixed soldiers.</summary>
    Army,
    /// <summary>A barracks, watchtowers around the hall, and archers.</summary>
    Defence,
}

public sealed class Building
{
    public int Id { get; }
    public int Owner { get; internal set; }
    /// <summary>What it is. Changes only when an upgrade is finished (see <see cref="Upgrade"/>).</summary>
    public BuildingDef Def { get; internal set; }
    /// <summary>
    /// The shape it was put up in: its owner's faction's (see <see cref="Content.FactionDef.Look"/>) when it was placed.
    /// It keeps it when it is captured, and grows in it when it is upgraded.
    /// </summary>
    public BuildingLook Look { get; }
    /// <summary>
    /// What its blocks are (<see cref="BuildingDef.Blocks"/>), with their toughness and its height: <see cref="Def"/> in
    /// its <see cref="Look"/> (see <see cref="BuildingDef.Shaped"/>). Everything else about it is <see cref="Def"/>'s.
    /// </summary>
    public BuildingDef Shape => Def.Shaped(Look);
    /// <summary>The footprint's corner. Moves only when an upgrade that grows it is finished (see <see cref="CornerOf"/>).</summary>
    public int X { get; internal set; }
    public int Y { get; internal set; }
    public int Z { get; internal set; }
    public BuildingState State { get; internal set; }
    /// <summary>Worker-ticks of building work done (a site is finished at <see cref="BuildingDef.BuildTicks"/>).</summary>
    public int Work { get; internal set; }
    /// <summary>Template blocks placed so far.</summary>
    public int Placed { get; internal set; }
    /// <summary>Goods inside, by <see cref="Good"/>: a store's stock, or a smelter's inputs and output.</summary>
    public int[] Stock { get; } = new int[Goods.Count];
    /// <summary>
    /// Supply rules, by <see cref="Good"/>: how much of each good a storage building should keep (0 for no rule).
    /// Workers on supply duty fill it from the owner's other storage (see <see cref="SupplyCommand"/>).
    /// </summary>
    public int[] Supply { get; } = new int[Goods.Count];
    /// <summary>
    /// A site far from storage: goods of its cost, by <see cref="Good"/>, paid and set aside at storage but not yet carried
    /// out to it. Its work goes only as far as the goods carried out so far allow (see <see cref="Economy.EconomySystem.MaxWork"/>).
    /// </summary>
    public int[] Due { get; } = new int[Goods.Count];
    /// <summary>True if goods of its cost still have to be carried out to it.</summary>
    public bool HasDue => Array.Exists(Due, a => a > 0);
    /// <summary>True if it has a supply rule for any good.</summary>
    public bool HasSupply => Array.Exists(Supply, a => a > 0);

    /// <summary>The player capturing or burning it, or -1.</summary>
    public int RaidPlayer { get; internal set; } = -1;
    public RaidKind Raid { get; internal set; }
    public int RaidTicks { get; internal set; }

    /// <summary>Ticks left on the ingot being smelted (at a shrine, the crystal being channelled), or 0.</summary>
    public int SmeltTicks { get; internal set; }

    /// <summary>Units waiting to be trained (unit definition indexes), first in front.</summary>
    public List<int> Queue { get; } = new();
    public int TrainTicks { get; internal set; }
    /// <summary>It repeats its training: each unit it finishes is owed to the queue again (see <see cref="RepeatCommand"/>).</summary>
    public bool Repeat { get; internal set; }
    /// <summary>
    /// Units (definition indexes) finished while on repeat and not yet queued again, oldest first; automation
    /// queues them as the player can pay.
    /// </summary>
    public List<int> Owed { get; } = new();

    /// <summary>
    /// Where its trained units walk when they step out (see <see cref="RallyCommand"/>): column RallyX, RallyZ at
    /// level RallyY. RallyX is -1 while it has none.
    /// </summary>
    public int RallyX { get; internal set; } = -1;
    public int RallyZ { get; internal set; }
    public int RallyY { get; internal set; }
    /// <summary>True if it has a rally point.</summary>
    public bool HasRally => RallyX >= 0;

    /// <summary>An orchard: the tick its saplings were (last) planted, when it was finished or cut down.</summary>
    public long Planted { get; internal set; }
    /// <summary>An orchard whose trees have grown: they screen sight and shots, and bear fruit.</summary>
    public bool Grown { get; internal set; }

    /// <summary>A town hall's governor and its focus, or <see cref="GovernorFocus.None"/> (see <see cref="GovernCommand"/>).</summary>
    public GovernorFocus Governor { get; internal set; }

    /// <summary>
    /// The toughness of its placed blocks that are gone: knocked out, dug away or fallen. It collapses once
    /// that is half of what was built (see <see cref="Units.CombatSystem"/>).
    /// </summary>
    public int Lost { get; internal set; }
    /// <summary>Damage dealt toward knocking out its next block.</summary>
    public int Damage { get; internal set; }
    /// <summary>Which of its blocks (by index in the template) are counted in <see cref="Lost"/>; null while none are.</summary>
    internal bool[]? Gone;

    /// <summary>
    /// What it is being turned into, paid for and waiting for its builders, or null (see <see cref="UpgradeCommand"/>).
    /// Until the work is done it stands as it is.
    /// </summary>
    public BuildingDef? Upgrade { get; internal set; }
    /// <summary>Worker-ticks of work done on its upgrade (finished at the upgrade's <see cref="BuildingDef.BuildTicks"/>).</summary>
    public int UpgradeWork { get; internal set; }
    /// <summary>
    /// Ticks its finished upgrade has waited for units to step off the ground it grows onto (see
    /// <see cref="Economy.EconomySystem.UpgradeWaitTicks"/>).
    /// </summary>
    public int UpgradeWait { get; internal set; }
    /// <summary>True if builders have work to do on it: a site going up, or a building being upgraded whose work is not done.</summary>
    public bool WantsBuilders => State == BuildingState.Site || (Upgrade != null && UpgradeWork < Upgrade.BuildTicks);

    /// <summary>
    /// The footprint corner it has once it is turned into <paramref name="to"/>: a bigger building grows from
    /// its middle, half the extra on each side (the odd column on the far side).
    /// </summary>
    public (int X, int Z) CornerOf(BuildingDef to) => (X - (to.SizeX - Def.SizeX) / 2, Z - (to.SizeZ - Def.SizeZ) / 2);

    /// <summary>
    /// The columns it takes up: its footprint, and while it is being upgraded to a bigger building, the ground
    /// that building grows onto too, which nothing else may be put on meanwhile.
    /// </summary>
    public (int X0, int Z0, int X1, int Z1) Claim
    {
        get
        {
            if (Upgrade == null) return (X, Z, MaxX, MaxZ);
            var (x, z) = CornerOf(Upgrade);
            return (Math.Min(X, x), Math.Min(Z, z), Math.Max(MaxX, x + Upgrade.SizeX - 1), Math.Max(MaxZ, z + Upgrade.SizeZ - 1));
        }
    }

    /// <summary>The toughness of the blocks placed so far.</summary>
    public int Built => Shape.ToughnessOf(Placed);
    /// <summary>What it can take before it collapses, from <see cref="MaxHealth"/> down to 0.</summary>
    public int Health => Math.Max(0, Built / 2 - Lost - Damage);
    /// <summary>Half the toughness of what is built: a building collapses when it has lost that much.</summary>
    public int MaxHealth => Built / 2;

    internal Building(int id, int owner, BuildingDef def, int x, int y, int z, BuildingLook look)
    {
        Id = id;
        Owner = owner;
        Def = def;
        Look = look;
        X = x;
        Y = y;
        Z = z;
    }

    public int MaxX => X + Def.SizeX - 1;
    public int MaxZ => Z + Def.SizeZ - 1;
    public bool Active => State == BuildingState.Active;
    public bool Planned => State == BuildingState.Planned;

    /// <summary>True if column (x, z) is inside the footprint.</summary>
    public bool Covers(int x, int z) => x >= X && x <= MaxX && z >= Z && z <= MaxZ;

    /// <summary>Columns from (x, z) to the footprint, counting diagonal steps as one (0 inside).</summary>
    public int ColumnDistance(int x, int z)
    {
        int dx = x < X ? X - x : x > MaxX ? x - MaxX : 0;
        int dz = z < Z ? Z - z : z > MaxZ ? z - MaxZ : 0;
        return Math.Max(dx, dz);
    }

    /// <summary>True if block (x, y, z) is inside the building's box (footprint by its full height).</summary>
    public bool Contains(int x, int y, int z) =>
        Covers(x, z) && y >= Y + (Def.Walkable ? -1 : 0) && y < Y + Math.Max(Shape.Height, 1);

    /// <summary>Squared horizontal distance from (x, z) to the footprint's centre, in half blocks.</summary>
    public long CentreDistance2(int x, int z)
    {
        long dx = 2L * x - (X + MaxX), dz = 2L * z - (Z + MaxZ);
        return dx * dx + dz * dz;
    }
}

/// <summary>All buildings, in ascending id order (the order every system visits them in).</summary>
public sealed class BuildingStore
{
    private readonly List<Building> _list = new();
    private readonly Dictionary<int, Building> _byId = new();
    private int _nextId = 1;

    public IReadOnlyList<Building> All => _list;
    public int Count => _list.Count;

    public Building? Get(int id) => _byId.TryGetValue(id, out var b) ? b : null;

    internal Building Add(int owner, BuildingDef def, int x, int y, int z, BuildingLook look = BuildingLook.Standard)
    {
        var b = new Building(_nextId++, owner, def, x, y, z, look);
        _list.Add(b);
        _byId.Add(b.Id, b);
        return b;
    }

    internal void Remove(Building b)
    {
        _list.Remove(b);
        _byId.Remove(b.Id);
    }

    /// <summary>The building whose box contains block (x, y, z), or null.</summary>
    public Building? At(int x, int y, int z)
    {
        foreach (var b in _list)
            if (b.Contains(x, y, z)) return b;
        return null;
    }

    /// <summary>
    /// The owner of the gate on column (x, z), or -1 if there is none: their units walk through it, everyone
    /// else's find it shut (see <see cref="BlockType.Gate"/>).
    /// </summary>
    public int GateOwner(int x, int z)
    {
        foreach (var b in _list)
            if (b.Def.Kind == BuildingKind.Gate && b.Covers(x, z)) return b.Owner;
        return -1;
    }

    /// <summary>The building whose footprint covers column (x, z), or null.</summary>
    public Building? AtColumn(int x, int z)
    {
        foreach (var b in _list)
            if (b.Covers(x, z)) return b;
        return null;
    }

    /// <summary>FNV-1a over every field that affects the game.</summary>
    public ulong ComputeHash()
    {
        ulong h = 14695981039346656037UL;
        void Mix(long v) { h = (h ^ (ulong)v) * 1099511628211UL; }
        Mix(_list.Count);
        Mix(_nextId);
        foreach (var b in _list)
        {
            Mix(b.Id); Mix(b.Owner); Mix(b.Def.Index); Mix((int)b.Look); Mix(b.X); Mix(b.Y); Mix(b.Z);
            Mix((int)b.State); Mix(b.Work); Mix(b.Placed);
            foreach (int s in b.Stock) Mix(s);
            foreach (int s in b.Supply) Mix(s);
            foreach (int s in b.Due) Mix(s);
            Mix(b.RaidPlayer); Mix((int)b.Raid); Mix(b.RaidTicks); Mix(b.SmeltTicks);
            Mix(b.Queue.Count);
            foreach (int q in b.Queue) Mix(q);
            Mix(b.TrainTicks);
            Mix(b.Repeat ? 1 : 0);
            Mix(b.Owed.Count);
            foreach (int o in b.Owed) Mix(o);
            Mix(b.Lost); Mix(b.Damage);
            Mix(b.Upgrade?.Index ?? -1); Mix(b.UpgradeWork); Mix(b.UpgradeWait);
            Mix(b.RallyX); Mix(b.RallyZ); Mix(b.RallyY);
            Mix((int)b.Governor);
            Mix(b.Planted); Mix(b.Grown ? 1 : 0);
        }
        return h;
    }
}
