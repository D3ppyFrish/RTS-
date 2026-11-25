using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.AI;

// Terrain magic. From Normal up, in the fighting (see Micro), its ready mages open a chasm under enemy soldiers bunched
// together near them: the line, across or along, that catches the most of them and none of its own, if it catches at
// least ChasmLeast, with as many mages as are ready and its mana pays for (up to a group of three). At the smart levels
// it lays out one shrine by its town hall once it has crystal in storage to channel, so it has mana for more spells
// than the 50 every player starts with; its automatic workers keep it supplied, as they do a player's.
// Earth walls: from Normal up, in the fighting, when no chasm went off, its ready mages raise an earth wall across the
// way of enemy melee soldiers on the move at its archers and mages (at least WallLeast of them, between ChargeClose
// and ChargeReach columns from the nearest; none closer, where a wall would only wall in the fight): halfway between its shooters and the chargers, square to the way they come,
// if earth rises on at least WallRoomLeast of its columns.
public sealed partial class AiPlayer
{
    /// <summary>Fewest enemy melee soldiers charging its archers and mages for its mages to wall them off.</summary>
    public const int WallLeast = 2;
    /// <summary>A charger counts between these distances (columns) from the nearest of its archers and mages: closer is already on them.</summary>
    public const int ChargeClose = 4, ChargeReach = 16;
    /// <summary>Fewest columns of the wall that earth must rise on for it to be worth casting.</summary>
    public const int WallRoomLeast = 5;
    /// <summary>Earth walls it raised against a charge.</summary>
    public int WallsRaised { get; private set; }

    /// <summary>Fewest enemy soldiers a chasm must catch for its mages to open it.</summary>
    public const int ChasmLeast = 3;
    /// <summary>Crystal in storage before it lays out a shrine: two loads for the keeper.</summary>
    public const int ShrineCrystal = 10;
    /// <summary>Columns round its town hall's middle in which it looks for room for its shrine.</summary>
    public const int ShrineRoom = 12;

    private readonly List<(int X, int Z, int Y, bool Room)> _chasmPlan = new();

    /// <summary>
    /// Its ready mages among <paramref name="ids"/> open a chasm under the bunched <paramref name="enemies"/> (unit
    /// indices of the rival soldiers it sees round them) if one would catch at least <see cref="ChasmLeast"/> of them
    /// and none of anyone else. The mages it sent are added to <paramref name="casting"/>.
    /// </summary>
    private void Chasms(Simulation sim, List<int> ids, List<int> enemies, List<int> casting, Action<Command> issue)
    {
        if (enemies.Count < ChasmLeast) return;
        int cost = SpellSystem.ManaCost(Spell.Chasm);
        int mana = sim.Economy.Player(Player).Mana;
        if (mana < cost) return;
        var u = sim.Units;
        var spells = sim.Spells;
        var mages = new List<int>();
        foreach (int id in ids)
        {
            int i = u.IndexOf(id);
            if (i < 0 || u.Aboard[i] != 0 || !spells.CanCast(i, Spell.Chasm) || spells.Casting(id)) continue;
            mages.Add(i);
        }
        if (mages.Count == 0) return;
        int casters = Math.Min(Math.Min(mages.Count, SpellSystem.MaxGroup), mana / cost);
        int half = SpellSystem.WallLength(casters) / 2; // the longest line a group of them opens
        long range2 = (long)(SpellSystem.CastRange - 1) * (SpellSystem.CastRange - 1);

        // Everyone who could be standing on a line near the mages: the enemies it counts, and anyone it must spare.
        var (cx, cz) = Centroid(sim, ids);
        long near2 = (long)(SpellSystem.CastRange + half + 2) * (SpellSystem.CastRange + half + 2) + 24L * 24;
        var others = new List<int>();
        for (int i = 0; i < u.Count; i++)
        {
            if (enemies.Contains(i) || u.Aboard[i] != 0) continue;
            if (Dist2(u.X[i].FloorToInt(), u.Z[i].FloorToInt(), cx, cz) <= near2) others.Add(i);
        }

        int best = 0, bx1 = 0, bz1 = 0, bx2 = 0, bz2 = 0, bk = 0;
        var columns = new SortedSet<long>();
        foreach (int e in enemies)
        {
            int ex = u.X[e].FloorToInt(), ez = u.Z[e].FloorToInt();
            for (int across = 0; across < 2; across++)
            {
                int x1 = across == 0 ? ex - half : ex, z1 = across == 0 ? ez : ez - half;
                int x2 = across == 0 ? ex + half : ex, z2 = across == 0 ? ez : ez + half;
                int mx = (x1 + x2) / 2, mz = (z1 + z2) / 2;
                // Only the mages in range now cast: none walks into the fight to cast.
                int k = 0;
                foreach (int m in mages)
                    if (Dist2(u.X[m].FloorToInt(), u.Z[m].FloorToInt(), mx, mz) <= range2) k++;
                k = Math.Min(k, casters);
                if (k == 0) continue;
                spells.PlanChasm(x1, z1, x2, z2, _chasmPlan, k);
                columns.Clear();
                foreach (var c in _chasmPlan)
                    if (c.Room) columns.Add(Key(c.X, c.Z));
                if (columns.Count == 0) continue;
                int caught = 0;
                foreach (int o in enemies)
                    if (On(sim, o, columns)) caught++;
                if (caught < ChasmLeast || caught <= best) continue;
                bool spare = false;
                foreach (int o in others)
                    if (On(sim, o, columns)) { spare = true; break; }
                if (spare) continue;
                (best, bx1, bz1, bx2, bz2, bk) = (caught, x1, z1, x2, z2, k);
            }
        }
        if (best == 0) return;

        // The mages nearest the line's middle cast it.
        int bmx = (bx1 + bx2) / 2, bmz = (bz1 + bz2) / 2;
        mages.Sort((a, b) =>
        {
            long da = Dist2(u.X[a].FloorToInt(), u.Z[a].FloorToInt(), bmx, bmz);
            long db = Dist2(u.X[b].FloorToInt(), u.Z[b].FloorToInt(), bmx, bmz);
            return da != db ? da.CompareTo(db) : u.Id[a].CompareTo(u.Id[b]);
        });
        casters = bk;
        var group = new int[casters];
        for (int n = 0; n < casters; n++) group[n] = u.Id[mages[n]];
        issue(new ChasmCommand(Player, group, bx1, bz1, bx2, bz2));
        casting.AddRange(group);
        Note(sim, $"opens a chasm under {best} enemy soldiers with {casters} mage{(casters == 1 ? "" : "s")}");
    }

    /// <summary>
    /// Its ready mages among <paramref name="ids"/> raise an earth wall across the way of the enemy melee soldiers
    /// among <paramref name="enemies"/> charging its archers and mages (see the top of this file). The mages it sent are
    /// added to <paramref name="casting"/>.
    /// </summary>
    private void EarthWalls(Simulation sim, List<int> ids, List<int> enemies, List<int> casting, Action<Command> issue)
    {
        int cost = SpellSystem.ManaCost(Spell.EarthWall);
        int mana = sim.Economy.Player(Player).Mana;
        if (mana < cost || enemies.Count < WallLeast) return;
        var u = sim.Units;
        var spells = sim.Spells;
        var mages = new List<int>();
        var shooters = new List<int>();
        foreach (int id in ids)
        {
            int i = u.IndexOf(id);
            if (i < 0 || u.Aboard[i] != 0 || u.Rout[i] > 0) continue;
            var def = sim.Content.Units[u.Def[i]];
            if (def.Role == UnitRole.Siege || !(def.IsRanged || def.Role == UnitRole.Caster)) continue;
            shooters.Add(i);
            if (!casting.Contains(id) && spells.CanCast(i, Spell.EarthWall) && !spells.Casting(id)) mages.Add(i);
        }
        if (mages.Count == 0) return;

        // The chargers: enemy melee soldiers on the move near its shooters but not on them yet.
        long sx = 0, sz = 0, ex = 0, ez = 0;
        int chargers = 0;
        foreach (int e in enemies)
        {
            var def = sim.Content.Units[u.Def[e]];
            if (def.IsRanged || def.Role == UnitRole.Siege) continue;
            if (u.Order[e] == UnitOrder.Idle) continue; // standing: no charge
            int x = u.X[e].FloorToInt(), z = u.Z[e].FloorToInt();
            long nearest = long.MaxValue;
            foreach (int s in shooters) nearest = Math.Min(nearest, Dist2(x, z, u.X[s].FloorToInt(), u.Z[s].FloorToInt()));
            if (nearest < (long)ChargeClose * ChargeClose) return; // already on them: a wall now walls in the fight
            if (nearest > (long)ChargeReach * ChargeReach) continue;
            ex += x;
            ez += z;
            chargers++;
        }
        if (chargers < WallLeast) return;
        foreach (int s in shooters)
        {
            sx += u.X[s].FloorToInt();
            sz += u.Z[s].FloorToInt();
        }
        int rx = (int)(sx / shooters.Count), rz = (int)(sz / shooters.Count);
        int cx = (int)(ex / chargers), cz = (int)(ez / chargers);
        int px = (rx + cx) / 2, pz = (rz + cz) / 2;

        // The mages in range of the wall's middle cast it, as many as are ready and the mana pays for.
        long range2 = (long)(SpellSystem.CastRange - 1) * (SpellSystem.CastRange - 1);
        mages.RemoveAll(m => Dist2(u.X[m].FloorToInt(), u.Z[m].FloorToInt(), px, pz) > range2);
        if (mages.Count == 0) return;
        mages.Sort((a, b) =>
        {
            long da = Dist2(u.X[a].FloorToInt(), u.Z[a].FloorToInt(), px, pz);
            long db = Dist2(u.X[b].FloorToInt(), u.Z[b].FloorToInt(), px, pz);
            return da != db ? da.CompareTo(db) : u.Id[a].CompareTo(u.Id[b]);
        });
        int casters = Math.Min(Math.Min(mages.Count, SpellSystem.MaxGroup), mana / cost);
        int half = SpellSystem.WallLength(casters) / 2;
        // Square to the way they come: along z when they come along x, and the other way round.
        bool alongZ = Math.Abs(cx - rx) >= Math.Abs(cz - rz);
        int x1 = alongZ ? px : px - half, z1 = alongZ ? pz - half : pz;
        int x2 = alongZ ? px : px + half - 1, z2 = alongZ ? pz + half - 1 : pz;
        spells.Plan(x1, z1, x2, z2, _chasmPlan, casters);
        int room = 0;
        foreach (var c in _chasmPlan)
            if (c.Room) room++;
        if (room < WallRoomLeast) return;

        var group = new int[casters];
        for (int n = 0; n < casters; n++) group[n] = u.Id[mages[n]];
        issue(new EarthWallCommand(Player, group, x1, z1, x2, z2));
        casting.AddRange(group);
        WallsRaised++;
        Note(sim, $"raises an earth wall before {chargers} charging enemies with {casters} mage{(casters == 1 ? "" : "s")}");
    }

    private static long Key(int x, int z) => ((long)x << 32) | (uint)z;

    /// <summary>Unit i stands on one of the columns.</summary>
    private static bool On(Simulation sim, int i, SortedSet<long> columns)
    {
        var u = sim.Units;
        return columns.Contains(Key(u.X[i].FloorToInt(), u.Z[i].FloorToInt()));
    }

    /// <summary>
    /// The smart levels: one shrine by the town hall once it can build one and has <see cref="ShrineCrystal"/>
    /// crystal in storage to channel into mana, as it can pay for it.
    /// </summary>
    private void Shrine(Simulation sim, Building hall, Action<Command> issue)
    {
        if (!Smart || !hall.Active) return;
        if (sim.Economy.Stock(Player, Good.Crystal) < ShrineCrystal) return;
        var def = FirstBuilding(sim, BuildingKind.Shrine);
        if (def == null) return;
        foreach (var b in sim.Buildings.All)
            if (b.Owner == Player && b.Def.Kind == BuildingKind.Shrine) return; // one is enough (or on its way)
        if (!Affordable(sim, def.Cost)) return;
        var home = Middle(hall);
        if (Hard)
        {
            if (!Room(sim, def, home, ShrineRoom, out int x, out int z)) return;
            issue(new BuildCommand(Player, Array.Empty<int>(), def.Id, x, z));
            Spend(def.Cost);
        }
        else PlaceNear(sim, def, home, ShrineRoom, issue);
        Note(sim, "lays out a shrine to channel its crystal into mana");
    }
}
