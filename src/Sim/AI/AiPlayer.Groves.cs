using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;

namespace Rts.Sim.AI;

// Growing the woods back. From Normal up, while no enemy is near its town (for Hard, none for AlarmTicks), when its workers have cut down every tree
// within GroveNear columns of its town hall and wood in storage is below GroveWoodPlenty, a free mage at home (one on
// no errand and in no wave, within GroveHome of the hall) grows a grove: one mage's line of trees, between GroveFrom
// and GroveTo columns from the hall's middle, on the side away from the enemy town it knows of, clear of Hard's wall
// line, its quarry, lairs and the Heartscar, where at least GroveTreesLeast trees have room. It only spends mana it
// can spare: it keeps enough for a chasm after paying for the grove. Its wood cutters then fell the new trees like
// any others; once they are gone it grows another, no sooner than GroveEveryTicks after the last.
public sealed partial class AiPlayer
{
    /// <summary>Columns from the town hall's middle within which any tree means it needs no grove yet.</summary>
    public const int GroveNear = 28;
    /// <summary>Columns from the town hall's middle (nearest and farthest) where it grows a grove.</summary>
    public const int GroveFrom = 14, GroveTo = 24;
    /// <summary>Columns from the town hall's middle within which a mage counts as at home.</summary>
    public const int GroveHome = 32;
    /// <summary>Fewest trees the grove must have room for to be worth the mana.</summary>
    public const int GroveTreesLeast = 3;
    /// <summary>Wood in storage at or above which a grove is not worth the mana.</summary>
    public const int GroveWoodPlenty = 400;
    /// <summary>Ticks between groves: 2 minutes.</summary>
    public const int GroveEveryTicks = 2 * 60 * Tps;
    /// <summary>Ticks between looks at whether it needs a grove: 5 seconds.</summary>
    private const int GroveLookTicks = 5 * Tps;

    /// <summary>Groves its mages grew by its town.</summary>
    public int GrovesGrown { get; private set; }

    private long _groveAt;
    private int _groveMage;
    private readonly List<(int X, int Z, int Y, bool Room)> _grovePlan = new();

    /// <summary>
    /// From Normal up: a free mage among those at home grows a grove by the town hall once the trees near it are gone
    /// (see the top of this file). A mage on its way to grow one is taken out of <paramref name="home"/> until it has.
    /// </summary>
    private void Groves(Simulation sim, Building hall, List<int> home, Action<Command> issue)
    {
        if (_groveMage != 0)
        {
            if (sim.Units.Exists(_groveMage) && sim.Spells.Casting(_groveMage)) home.Remove(_groveMage);
            else _groveMage = 0;
        }
        if (!Skilled || !hall.Active || sim.Tick < _groveAt || sim.Tick % GroveLookTicks >= ArmyTicks) return;
        if (Hard && sim.Tick - _alarm <= AlarmTicks) return;
        int cost = SpellSystem.ManaCost(Spell.GrowForest);
        if (sim.Economy.Player(Player).Mana < cost + SpellSystem.ManaCost(Spell.Chasm)) return;
        if (sim.Economy.Stock(Player, Good.Wood) >= GroveWoodPlenty) return;
        var (hx, hz) = Middle(hall);
        if (NearestEnemy(sim, hx, hz, DefendRadius, soldiersOnly: false) >= 0) return;

        // A free mage at home.
        var u = sim.Units;
        var mages = new List<int>();
        foreach (int id in home)
        {
            int i = u.IndexOf(id);
            if (i < 0 || u.Aboard[i] != 0 || u.Job[i] != UnitJob.None || u.Rout[i] > 0) continue;
            if (!sim.Spells.CanCast(i, Spell.GrowForest) || sim.Spells.Casting(id) || _mending.Contains(id)) continue;
            if (Dist2(u.X[i].FloorToInt(), u.Z[i].FloorToInt(), hx, hz) > (long)GroveHome * GroveHome) continue;
            mages.Add(i);
        }
        if (mages.Count == 0) return;
        if (TreesNear(sim.World, hx, hz, GroveNear)) return;
        if (GroveSpot(sim, hx, hz) is not var (x1, z1, x2, z2, trees))
        {
            _groveAt = sim.Tick + GroveEveryTicks; // no room by the town: look again later
            return;
        }
        int mx = (x1 + x2) / 2, mz = (z1 + z2) / 2;
        mages.Sort((a, b) =>
        {
            long da = Dist2(u.X[a].FloorToInt(), u.Z[a].FloorToInt(), mx, mz);
            long db = Dist2(u.X[b].FloorToInt(), u.Z[b].FloorToInt(), mx, mz);
            return da != db ? da.CompareTo(db) : u.Id[a].CompareTo(u.Id[b]);
        });
        _groveMage = u.Id[mages[0]];
        home.Remove(_groveMage);
        issue(new GrowForestCommand(Player, new[] { _groveMage }, x1, z1, x2, z2));
        _groveAt = sim.Tick + GroveEveryTicks;
        GrovesGrown++;
        Note(sim, $"has a mage grow a grove of {trees} trees at {mx},{mz}: the woods by its town are cut down");
    }

    /// <summary>True if a tree trunk stands within <paramref name="reach"/> columns of (x, z).</summary>
    private static bool TreesNear(VoxelWorld w, int x, int z, int reach)
    {
        long r2 = (long)reach * reach;
        for (int dz = -reach; dz <= reach; dz++)
        for (int dx = -reach; dx <= reach; dx++)
        {
            int cx = x + dx, cz = z + dz;
            if (cx < 0 || cz < 0 || cx >= w.SizeX || cz >= w.SizeZ || (long)dx * dx + (long)dz * dz > r2) continue;
            if (TrunkBase(w, cx, cz, out _)) return true;
        }
        return false;
    }

    /// <summary>
    /// The line for one mage's grove nearest the town hall's middle (hx, hz) between <see cref="GroveFrom"/> and
    /// <see cref="GroveTo"/> columns out, farthest from the enemy town it knows of among the nearest, and the trees
    /// it has room for; null if none has room for <see cref="GroveTreesLeast"/>.
    /// </summary>
    private (int X1, int Z1, int X2, int Z2, int Trees)? GroveSpot(Simulation sim, int hx, int hz)
    {
        var w = sim.World;
        var enemy = EnemyHall(sim, hx, hz);
        int s = Math.Clamp(w.TreeScale, 1, Trees.MaxScale);
        int length = SpellSystem.WallLength(1);
        for (int r = GroveFrom; r <= GroveTo; r += 2)
        {
            (int, int, int, int, int)? best = null;
            long bestScore = -1;
            for (int dz = -r; dz <= r; dz += 2)
            for (int dx = -r; dx <= r; dx += 2)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                int cx = hx + dx, cz = hz + dz;
                for (int along = 0; along < 2; along++)
                {
                    // A line square to the way out from the hall, so the grove does not reach back towards it.
                    bool alongX = Math.Abs(dz) >= Math.Abs(dx);
                    if (along == 1) alongX = !alongX;
                    int x1 = alongX ? cx - length / 2 : cx, z1 = alongX ? cz : cz - length / 2;
                    int x2 = alongX ? x1 + length - 1 : cx, z2 = alongX ? cz : z1 + length - 1;
                    if (x1 < 2 || z1 < 2 || x2 > w.SizeX - 3 || z2 > w.SizeZ - 3) continue;
                    if (!ClearOfWall(x1 - s, z1 - s, x2 + 2 * s, z2 + 2 * s, 2)) continue;
                    if (_quarry is var (qx1, qz1, qx2, qz2, _) && x1 <= qx2 + 3 && x2 >= qx1 - 3 && z1 <= qz2 + 3 && z2 >= qz1 - 3) continue;
                    if (NearLair(sim, cx, cz) || KeepsOff(sim, x1, z1, x2, z2)) continue;
                    sim.Spells.PlanGrove(x1, z1, x2, z2, _grovePlan, 1);
                    int trees = 0;
                    for (int k = 0; k < _grovePlan.Count; k += s * s)
                        if (_grovePlan[k].Room) trees++;
                    if (trees < GroveTreesLeast) continue;
                    long score = enemy is { } e ? Dist2(cx, cz, e.X, e.Z) : 0;
                    score = score * 16 + trees;
                    if (score > bestScore) (bestScore, best) = (score, (x1, z1, x2, z2, trees));
                }
            }
            if (best != null) return best;
        }
        return null;
    }
}
