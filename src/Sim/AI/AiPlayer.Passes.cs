using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;

namespace Rts.Sim.AI;

/// <summary>
/// Mining the mountain passes (the design doc's stone giants that guard passes for "gold, iron vein access"). The
/// smart levels (Easy and up) work the iron and gold seamed into a pass's walls once no monster guards the pass: a
/// pass they have seen, near enough to their town hall and nearer it than any enemy's, while they need the ore (a
/// smelter to feed and little iron ore in hand, or gold for mercenaries and little in hand). They keep a few miners
/// digging the seams and put a storehouse in the pass when storage is far; the miners go back to other work when the
/// need is met or the seams near the pass are dug out. The classic level never mines a pass. The numbers are first
/// guesses for playtesting.
/// </summary>
public sealed partial class AiPlayer
{
    /// <summary>It mines passes whose middle is at most this far (columns) from its town hall.</summary>
    public const int PassMineReach = 160;

    /// <summary>How many miners it keeps in the pass.</summary>
    public const int PassMiners = 3;

    /// <summary>No miners are sent before it has this many workers.</summary>
    public const int PassMineWorkers = 10;

    /// <summary>It wants iron ore from the pass (with a smelter) while it has less than this in hand.</summary>
    public const int PassOreWanted = 150;

    /// <summary>It wants gold from the pass (holding a town whose mercenaries cost gold) while it has less than this in hand.</summary>
    public const int PassGoldWanted = 80;

    /// <summary>A pass is guarded while a lair of monsters still about lies within this many columns of its middle.</summary>
    public const int PassGuardGap = 24;

    /// <summary>
    /// The seams it digs: ore within this many columns of the pass's middle along the ridge (the
    /// <see cref="RegionGenerator"/>'s 15 each side and a little to spare), <see cref="PassSeamAcross"/> across it,
    /// and from the floor up <see cref="PassSeamHigh"/> levels.
    /// </summary>
    public const int PassSeamAlong = 18, PassSeamAcross = 8, PassSeamHigh = 10;

    /// <summary>A pass mine this far (columns) or more from the nearest storage gets a storehouse in the pass.</summary>
    public const int PassStoreGap = 24;

    // Its miners in the pass, by id; the pass they work ((-1, -1) if none).
    private readonly List<int> _passMiners = new();
    private (int X, int Y, int Z) _minedPass = (-1, -1, -1);

    /// <summary>The workers it keeps digging ore in a mountain pass, by id.</summary>
    public IReadOnlyList<int> PassMinerIds => _passMiners;

    /// <summary>The middle of the pass it mines, or null.</summary>
    public (int X, int Z)? MinedPass => _passMiners.Count > 0 ? (_minedPass.X, _minedPass.Z) : null;

    /// <summary>
    /// The smart levels' pass mines: miners on the seams of the nearest open pass while it needs the ore (see
    /// <see cref="PassOreWanted"/>, <see cref="PassGoldWanted"/>), let go when it does not, and a storehouse in the
    /// pass when storage is far.
    /// </summary>
    private void PassMines(Simulation sim, Building hall, Action<Command> issue)
    {
        var u = sim.Units;
        _passMiners.RemoveAll(id => u.IndexOf(id) is var i && (i < 0 || !OnOre(u, i)));
        var want = PassGood(sim);
        var pass = want == null ? null : PassToMine(sim, hall);
        if (pass is not { } p)
        {
            LetMinersGo(sim, "it has the ore it needs, or no open pass to dig", issue);
            return;
        }
        if (_passMiners.Count > 0 && p != _minedPass) LetMinersGo(sim, "the pass it mined is no longer the one to dig", issue);
        _minedPass = p;
        var kind = want == Good.Gold ? BlockType.GoldOre : BlockType.IronOre;

        // Miners sent this time: the miners already there find the next block of the seam on their own.
        if (_passMiners.Count < PassMiners && Workers(sim) >= PassMineWorkers)
        {
            var other = kind == BlockType.GoldOre ? BlockType.IronOre : BlockType.GoldOre;
            if ((Seam(sim, p, kind) ?? Seam(sim, p, other)) is not { } at)
            {
                LetMinersGo(sim, "the seams by the pass are dug out", issue);
                return;
            }
            var (bx, by, bz) = at;
            var more = SpareWorkers(sim, PassMiners - _passMiners.Count);
            if (more.Count > 0)
            {
                issue(new GatherCommand(Player, more.ToArray(), bx, by, bz));
                _passMiners.AddRange(more);
                Note(sim, $"sends {more.Count} to dig {(kind == BlockType.GoldOre ? "gold" : "iron ore")} in the pass at {p.X},{p.Z}");
            }
        }

        // A storehouse in the pass when the ore has far to go.
        if (_passMiners.Count == 0) return;
        var store = FirstBuilding(sim, BuildingKind.Storehouse);
        if (store == null) return;
        if (sim.Economy.NearestStorage(Player, p.X, p.Z) is { } near && near.ColumnDistance(p.X, p.Z) < PassStoreGap) return;
        foreach (var b in sim.Buildings.All)
            if (b.Owner == Player && !b.Active && b.Def.Storage && b.ColumnDistance(p.X, p.Z) < PassStoreGap) return; // one on its way
        if (!Affordable(sim, store.Cost)) return;
        if (sim.Automation.FindRoom(Player, store, p.X, p.Z, StoreRadius, out int sx, out int sz, (x, z) => SafeGround(sim, store, x, z)))
        {
            issue(new BuildCommand(Player, Array.Empty<int>(), store.Id, sx, sz));
            if (Hard) Spend(store.Cost);
            Note(sim, $"lays out a storehouse by its pass mine at {sx},{sz}");
        }
    }

    /// <summary>True if the unit is digging iron ore or gold.</summary>
    private static bool OnOre(UnitStore u, int i) =>
        u.Job[i] == UnitJob.Gather && u.JobBlock[i] is BlockType.IronOre or BlockType.GoldOre;

    /// <summary>Its miners go back to other work.</summary>
    private void LetMinersGo(Simulation sim, string why, Action<Command> issue)
    {
        if (_passMiners.Count == 0) return;
        Note(sim, $"lets its pass miners go: {why}");
        issue(new StopCommand(Player, _passMiners.ToArray()));
        issue(new AutomateCommand(Player, _passMiners.ToArray(), true));
        _passMiners.Clear();
    }

    /// <summary>
    /// What it wants from a pass, or null: gold while it holds a town whose mercenaries cost gold and has less than
    /// <see cref="PassGoldWanted"/>, else iron ore while it has a finished smelter and less than <see cref="PassOreWanted"/>.
    /// </summary>
    private Good? PassGood(Simulation sim)
    {
        bool gold = false;
        foreach (var town in sim.Cities.Towns)
        {
            if (town.Holder != Player) continue;
            foreach (var m in town.Def.Mercenaries)
                foreach (var c in m.Cost)
                    gold |= c.Good == Good.Gold;
        }
        if (gold && sim.Economy.Stock(Player, Good.Gold) < PassGoldWanted) return Good.Gold;
        bool smelter = false;
        foreach (var b in sim.Buildings.All)
            smelter |= b.Owner == Player && b.Active && b.Def.Kind == BuildingKind.Smelter;
        if (smelter && sim.Economy.Stock(Player, Good.IronOre) < PassOreWanted) return Good.IronOre;
        return null;
    }

    /// <summary>
    /// The pass to mine: the one nearest its town hall, within <see cref="PassMineReach"/>, that it has seen, nearer this
    /// town hall than any enemy's, off the corrupted ground, and that no lair it knows of with monsters still about
    /// guards (within <see cref="PassGuardGap"/>); or null.
    /// </summary>
    public (int X, int Y, int Z)? PassToMine(Simulation sim, Building hall)
    {
        var (hx, hz) = Middle(hall);
        var enemy = EnemyHall(sim, hx, hz);
        (int X, int Y, int Z)? best = null;
        long bestD = (long)PassMineReach * PassMineReach + 1;
        foreach (var pass in sim.Map.Passes)
        {
            long d = Dist2(hx, hz, pass.X, pass.Z);
            if (d >= bestD) continue;
            if (enemy is { } e && Dist2(pass.X, pass.Z, e.X, e.Z) <= d) continue;
            if (sim.Vision.At(Player, pass.X, pass.Z) == Sight.Unseen || KeepsOff(sim, pass.X, pass.Z)) continue;
            bool guarded = false;
            foreach (var lair in sim.Monsters.Lairs)
                guarded |= !lair.Cleared && lair.Pack.Count > 0 && Dist2(pass.X, pass.Z, lair.X, lair.Z) <= (long)PassGuardGap * PassGuardGap
                    && LairKnown(sim, lair);
            if (guarded) continue;
            (bestD, best) = (d, pass);
        }
        return best;
    }

    /// <summary>
    /// The block of ore of <paramref name="kind"/> in the pass's walls nearest its middle that a worker can dig from
    /// beside without knocking anything loose or shutting itself in (see <see cref="Harvest.WorkSpot"/>); or null.
    /// </summary>
    private static (int X, int Y, int Z)? Seam(Simulation sim, (int X, int Y, int Z) pass, BlockType kind)
    {
        var w = sim.World;
        (int X, int Y, int Z)? best = null;
        long bestD = long.MaxValue;
        for (int y = Math.Max(1, pass.Y); y <= Math.Min(w.SizeY - 2, pass.Y + PassSeamHigh); y++)
        for (int z = pass.Z - PassSeamAcross; z <= pass.Z + PassSeamAcross; z++)
        for (int x = pass.X - PassSeamAlong; x <= pass.X + PassSeamAlong; x++)
        {
            if (!w.InBounds(x, y, z) || w.Get(x, y, z) != kind) continue;
            long d = Dist2(x, z, pass.X, pass.Z) + (long)(y - pass.Y) * (y - pass.Y);
            if (d >= bestD) continue;
            if (!Harvest.WorkSpot(w, x, y, y, z, pass.X, pass.Y + 1, pass.Z, out _, out _, out _, allowOnTop: false, wayOut: true)) continue;
            if (!PhysicsSystem.HoldsWithout(w, x, y, z)) continue;
            (bestD, best) = (d, (x, y, z));
        }
        return best;
    }
}
