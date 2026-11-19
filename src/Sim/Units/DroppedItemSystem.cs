using System;
using System.Collections.Generic;
using Rts.Sim.Content;

namespace Rts.Sim.Units;

/// <summary>A one-of-a-kind item lying on the ground: a relic, or the Dusk Staff.</summary>
public sealed class DroppedItem
{
    public int Id { get; init; }
    /// <summary>The relic, or null for the Dusk Staff.</summary>
    public RelicDef? Relic { get; init; }
    public bool Staff => Relic == null;
    /// <summary>The block it lies in (on the ground under it).</summary>
    public int X { get; internal set; }
    public int Y { get; internal set; }
    public int Z { get; internal set; }
    /// <summary>The tick it was dropped.</summary>
    public long Dropped { get; init; }
    /// <summary>The player whose unit dropped it.</summary>
    public int DroppedBy { get; init; }

    /// <summary>The name to show for it.</summary>
    public string Name => Relic?.Name ?? "Dusk Staff";
}

/// <summary>
/// Unique items on the ground (issue #73). A unit that dies bearing a relic (see <see cref="MonsterSystem"/>,
/// <c>data/relics.json</c>) or the Dusk Staff (see <see cref="DuskTreeSystem"/>) drops it in the block where it
/// fell. It lies there until a unit walks within <see cref="PickupRange"/> of it and can take it, whoever's
/// unit that is, enemies included (not wild monsters, nor a city-state's guards or caravans):
/// - a relic: a fighting unit (it does damage, and is no worker) without a relic, as at a den;
/// - the staff: a unit that heals, as at the tree.
/// A unit that is beaten, or running away, does not pick anything up. The nearest such unit takes it (ties to the
/// lowest id), and gets the item's hit points at once, as when it was first taken up. Dropped items never crumble;
/// one whose ground falls away falls with it, and one buried under rubble comes up on top. A beaten monster that is
/// tamed instead of killed keeps what it bears. Units cannot hand an item over on purpose; one dies and another
/// picks it up.
/// </summary>
public sealed class DroppedItemSystem
{
    /// <summary>Blocks from the item's middle within which a unit picks it up.</summary>
    public const int PickupRange = 1;
    /// <summary>Most levels between a unit and an item it picks up.</summary>
    public const int PickupHeight = 1;

    private readonly Simulation _sim;
    private readonly List<DroppedItem> _items = new(); // in id order
    private int _nextId = 1;

    public DroppedItemSystem(Simulation sim) => _sim = sim;

    private UnitStore U => _sim.Units;

    /// <summary>The items on the ground, oldest first.</summary>
    public IReadOnlyList<DroppedItem> Items => _items;

    /// <summary>The tick and item of the last pickup (for alerts and drawing), or -1 and null.</summary>
    public long LastPickup { get; private set; } = -1;

    /// <summary>Unit i is gone for good (it died): whatever unique item it bore is dropped where it stood.</summary>
    internal void DropFrom(int i)
    {
        var u = U;
        int x = u.X[i].FloorToInt(), y = u.Y[i], z = u.Z[i].FloorToInt();
        if (_sim.RelicOf(i) is { } relic)
        {
            Add(relic, x, y, z, u.Owner[i]);
            u.Relic[i] = 0;
        }
        if (_sim.DuskTree.HasStaff(i))
        {
            Add(null, x, y, z, u.Owner[i]);
            _sim.DuskTree.DropStaff(i);
        }
    }

    /// <summary>A relic is left lying in block (x, y, z), as if <paramref name="owner"/>'s unit had dropped it there (a find in a ruin nobody there could take up).</summary>
    internal void Drop(RelicDef relic, int x, int y, int z, int owner) => Add(relic, x, y, z, owner);

    private void Add(RelicDef? relic, int x, int y, int z, int owner)
    {
        var w = _sim.World;
        x = Math.Clamp(x, 0, w.SizeX - 1);
        z = Math.Clamp(z, 0, w.SizeZ - 1);
        y = Math.Clamp(y, 1, w.SizeY - 1);
        _items.Add(new DroppedItem { Id = _nextId++, Relic = relic, X = x, Y = y, Z = z, Dropped = _sim.Tick, DroppedBy = owner });
    }

    /// <summary>True if unit i could pick up the item now, wherever it stands.</summary>
    public bool CanTake(int i, DroppedItem item)
    {
        var u = U;
        if (i < 0 || i >= u.Count || u.Hp[i] <= 0 || u.Subdued[i] || u.Rout[i] > 0 || u.Town[i] != 0 || _sim.Monsters.IsWild(i)) return false;
        var def = _sim.Content.Units[u.Def[i]];
        if (item.Staff) return def.Heal > 0 && !_sim.DuskTree.HasStaff(i);
        return u.Relic[i] == 0 && def.Damage > 0 && def.Role != UnitRole.Worker;
    }

    public void Step()
    {
        if (_items.Count == 0) return;
        var u = U;
        var w = _sim.World;
        Fix64 r2 = Fix64.FromInt(PickupRange * PickupRange), half = Fix64.Half;
        for (int k = 0; k < _items.Count; k++)
        {
            var item = _items[k];
            Settle(item, w);
            Fix64 cx = Fix64.FromInt(item.X) + half, cz = Fix64.FromInt(item.Z) + half;
            int best = -1;
            Fix64 bestD = Fix64.MaxValue;
            for (int j = 0; j < u.Count; j++)
            {
                if (Math.Abs(u.Y[j] - item.Y) > PickupHeight) continue;
                Fix64 dx = u.X[j] - cx, dz = u.Z[j] - cz;
                Fix64 d = dx * dx + dz * dz;
                if (d > r2 || !CanTake(j, item)) continue;
                if (d < bestD || (d == bestD && u.Id[j] < u.Id[best]))
                {
                    best = j;
                    bestD = d;
                }
            }
            if (best < 0) continue;
            Take(best, item);
            _items.RemoveAt(k);
            k--;
        }
    }

    private void Take(int i, DroppedItem item)
    {
        var u = U;
        if (item.Relic is { } relic)
        {
            u.Relic[i] = (short)(relic.Index + 1);
            u.Hp[i] = Math.Min(_sim.MaxHpOf(i), u.Hp[i] + relic.Hp);
        }
        else _sim.DuskTree.TakeStaff(i);
        LastPickup = _sim.Tick;
    }

    /// <summary>An item with nothing under it falls; one inside ground (rubble fell on it) comes up on top.</summary>
    private static void Settle(DroppedItem item, VoxelWorld w)
    {
        while (item.Y < w.SizeY - 1 && BlockInfo.IsSolid(w.Get(item.X, item.Y, item.Z))) item.Y++;
        while (item.Y > 1 && !BlockInfo.IsSolid(w.Get(item.X, item.Y - 1, item.Z))) item.Y--;
    }

    public ulong ComputeHash()
    {
        ulong h = 14695981039346656037UL;
        void Mix(long v) { h = (h ^ (ulong)v) * 1099511628211UL; }
        Mix(_nextId);
        Mix(_items.Count);
        foreach (var item in _items)
        {
            Mix(item.Id); Mix(item.Relic is { } r ? r.Index + 1 : -1); Mix(item.X); Mix(item.Y); Mix(item.Z);
            Mix(item.Dropped); Mix(item.DroppedBy);
        }
        return h;
    }
}
