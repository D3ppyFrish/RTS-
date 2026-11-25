using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.AI;

// Dropped relics and the Dusk Staff (issue #73), at the smart levels (Easy and up). A relic or the staff lies where its
// bearer fell until a unit that can take it walks over it (see DroppedItemSystem). Once it has seen one lying within
// HuntReach of its town hall, off the corrupted ground, with no rival soldier it sees within ItemDanger columns of it,
// it sends one unit that can take it up (DroppedItemSystem.CanTake: a fighting unit without a relic for a relic, a unit
// that heals for the staff) to walk onto it: its nearest one at home that is not hurt, or one of its wave within
// ItemWaveReach columns of it that is not fighting (the bearer fell in the wave's fight). One unit is out on an item at
// a time, and is left out of the army and the wave while it is (see ItemStep); one from the wave goes back to it after.
// One that does not get there within ItemGiveUpTicks (or dies) gives up, and that item is left alone for ShunTicks.
public sealed partial class AiPlayer
{
    /// <summary>A unit sent to a dropped item gives up after this many ticks.</summary>
    public const int ItemGiveUpTicks = 2 * 60 * Tps;
    /// <summary>It leaves an item alone while a rival soldier it sees is within this many columns of it.</summary>
    public const int ItemDanger = 12;
    /// <summary>A unit of its wave within this many columns of an item may be sent to take it up.</summary>
    public const int ItemWaveReach = 24;

    // The unit out on an item (0 if none), the item, whether the unit came from the wave, and when it was sent.
    private int _itemRunner, _item;
    private bool _itemFromWave;
    private long _itemSent;
    // Items left alone after a unit gave up on one, until when; in the order they were left.
    private readonly List<(int Item, long Until)> _itemShunned = new();
    // The dropped items it has seen lying on the ground, by id (it goes by where it saw them, as with lairs).
    private readonly SortedSet<int> _itemsSeen = new();

    /// <summary>The unit it sent to take up a dropped item (0 if none is out).</summary>
    public int ItemRunner => _itemRunner;
    /// <summary>The dropped item its unit is on its way to (an id in <see cref="DroppedItemSystem.Items"/>; 0 if none).</summary>
    public int ItemSought => _itemRunner != 0 ? _item : 0;
    /// <summary>Dropped items its units have taken up when sent for them.</summary>
    public int ItemsTaken { get; private set; }

    /// <summary>
    /// The unit on a dropped item, after the dens: while it is on its way it is taken out of <paramref name="home"/>
    /// and the wave; with none out, it sends one to the item it wants (see the notes at the top).
    /// </summary>
    private void ItemStep(Simulation sim, Building hall, List<int> home, Action<Command> issue)
    {
        if (!Smart) return;
        var u = sim.Units;
        _itemShunned.RemoveAll(s => sim.Tick >= s.Until);
        if (_itemRunner != 0)
        {
            int i = u.IndexOf(_itemRunner);
            var item = FindItem(sim, _item);
            if (item == null)
            {
                if (i >= 0 && Bears(sim, i)) { ItemsTaken++; Note(sim, $"its {UnitName(sim, i)} takes up the {_itemName}"); }
                else Note(sim, $"the {_itemName} was taken up by someone else");
                EndItem(sim, shun: false);
                return;
            }
            if (i < 0 || !sim.Items.CanTake(i, item))
            {
                Note(sim, $"its unit on the {item.Name} stops short");
                EndItem(sim, shun: i < 0); // killed on the way: leave it alone a while
                return;
            }
            home.Remove(_itemRunner);
            _wave.Remove(_itemRunner);
            if (sim.Tick - _itemSent > ItemGiveUpTicks)
            {
                Note(sim, $"its unit gives up on the {item.Name}");
                EndItem(sim, shun: true);
                return;
            }
            // Stopped, or taken up with something else on the way: on to the item again.
            if (u.Order[i] == UnitOrder.Idle || u.Job[i] != UnitJob.None)
                issue(new MoveCommand(Player, [_itemRunner], item.X, item.Z, item.Y));
            return;
        }
        if (sim.Items.Items.Count == 0) return;

        // The item and unit nearest each other, of the items it wants and the units that could take each up.
        var (hx, hz) = Middle(hall);
        DroppedItem? best = null;
        int runner = 0;
        bool fromWave = false;
        long bestD = long.MaxValue;
        foreach (var item in sim.Items.Items)
        {
            if (!WantsItem(sim, item, hx, hz)) continue;
            foreach (int id in home) Weigh(id, item, false);
            foreach (int id in _wave) Weigh(id, item, true);
        }
        if (best == null) return;
        _itemRunner = runner;
        _item = best.Id;
        _itemName = best.Name;
        _itemFromWave = fromWave;
        _itemSent = sim.Tick;
        home.Remove(runner);
        _wave.Remove(runner);
        int r = u.IndexOf(runner);
        issue(new MoveCommand(Player, [runner], best.X, best.Z, best.Y));
        Note(sim, $"sends a {UnitName(sim, r)}{(fromWave ? " from its wave" : "")} to take up the {best.Name} at {best.X},{best.Z}");

        void Weigh(int id, DroppedItem item, bool wave)
        {
            int i = u.IndexOf(id);
            if (i < 0 || u.Aboard[i] != 0 || _mending.Contains(id) || !sim.Items.CanTake(i, item)) return;
            if (Hurt(sim, i, HealedPercent)) return;
            long d = Dist2(u.X[i].FloorToInt(), u.Z[i].FloorToInt(), item.X, item.Z);
            if (wave && (d > (long)ItemWaveReach * ItemWaveReach || u.Target[i] != 0 || u.TargetBuilding[i] != 0)) return;
            if (d < bestD || (d == bestD && id < runner)) (bestD, best, runner, fromWave) = (d, item, id, wave);
        }
    }

    // The name of the item its unit is out on (kept for the note once the item is gone).
    private string _itemName = "";

    /// <summary>
    /// True if it wants the dropped item now: seen lying there (now or before), within <see cref="HuntReach"/> of its town hall at
    /// (hx, hz), off the corrupted ground, with no rival soldier it sees within <see cref="ItemDanger"/>, and not shunned.
    /// </summary>
    private bool WantsItem(Simulation sim, DroppedItem item, int hx, int hz)
    {
        foreach (var s in _itemShunned)
            if (s.Item == item.Id) return false;
        if (!_itemsSeen.Contains(item.Id)) return false;
        if (Dist2(hx, hz, item.X, item.Z) > (long)HuntReach * HuntReach || KeepsOff(sim, item.X, item.Z)) return false;
        return NearestEnemy(sim, item.X, item.Z, ItemDanger, soldiersOnly: true) < 0;
    }

    /// <summary>Notes the dropped items it sees lying on the ground now, so that it knows of them once they are out of sight.</summary>
    private void SeeItems(Simulation sim)
    {
        var items = sim.Items.Items;
        if (items.Count == 0)
        {
            _itemsSeen.Clear();
            return;
        }
        foreach (var item in items)
            if (!_itemsSeen.Contains(item.Id) && sim.Vision.Sees(Player, item.X, item.Z)) _itemsSeen.Add(item.Id);
    }

    /// <summary>The dropped item with that id, if it still lies on the ground.</summary>
    private static DroppedItem? FindItem(Simulation sim, int id)
    {
        foreach (var item in sim.Items.Items)
            if (item.Id == id) return item;
        return null;
    }

    /// <summary>True if unit i bears the item its unit was sent for: the staff, or a relic of that name.</summary>
    private bool Bears(Simulation sim, int i) =>
        _itemName == "Dusk Staff" ? sim.DuskTree.HasStaff(i) : sim.RelicOf(i) is { } r && r.Name == _itemName;

    private static string UnitName(Simulation sim, int i) => i >= 0 ? sim.Content.Units[sim.Units.Def[i]].Name : "unit";

    /// <summary>The unit on an item is done (one from the wave goes back to it); with <paramref name="shun"/>, that item is left alone for <see cref="ShunTicks"/>.</summary>
    private void EndItem(Simulation sim, bool shun)
    {
        if (shun) _itemShunned.Add((_item, sim.Tick + ShunTicks));
        if (_itemFromWave && _wave.Count > 0 && sim.Units.Exists(_itemRunner) && !_wave.Contains(_itemRunner)) _wave.Add(_itemRunner);
        _itemRunner = 0;
        _item = 0;
        _itemFromWave = false;
    }
}
