using System;
using System.Collections.Generic;
using Rts.Sim.Units;

namespace Rts.Sim.AI;

// Flood (the Reptilians' marsh shamans). From Normal up, in the fighting (see Micro), its ready shamans flood the ground
// under the enemy soldiers near them: the line, across or along, through one of them that catches the most enemy
// soldiers who are not amphibious (they wade the pool at 70% of their pace while its own brood swims it at full pace,
// heals twice as fast in it and lies hidden in it), if it catches at least FloodLeast of them. It floods no line with
// anyone on it who is not amphibious and not its rival (its own and its allies' workers, sappers and the rest), so it
// slows none of its own. As many shamans cast as are ready, in range, and its mana pays for (up to a group of three).
public sealed partial class AiPlayer
{
    /// <summary>Fewest enemy soldiers (not amphibious) a flood must catch for its shamans to cast it.</summary>
    public const int FloodLeast = 2;
    /// <summary>Floods its shamans cast on enemy soldiers.</summary>
    public int FloodsCast { get; private set; }

    /// <summary>
    /// Its ready shamans among <paramref name="ids"/> flood the ground under the <paramref name="enemies"/> (unit
    /// indices of the rival soldiers it sees round them) if a flood would catch at least <see cref="FloodLeast"/> of
    /// them who are not amphibious and no one else but amphibious units. The shamans it sent are added to
    /// <paramref name="casting"/>.
    /// </summary>
    private void Floods(Simulation sim, List<int> ids, List<int> enemies, List<int> casting, Action<Command> issue)
    {
        if (enemies.Count < FloodLeast) return;
        int cost = SpellSystem.ManaCost(Spell.Flood);
        int mana = sim.Economy.Player(Player).Mana;
        if (mana < cost) return;
        var u = sim.Units;
        var spells = sim.Spells;
        var shamans = new List<int>();
        foreach (int id in ids)
        {
            int i = u.IndexOf(id);
            if (i < 0 || u.Aboard[i] != 0 || casting.Contains(id) || !spells.CanCast(i, Spell.Flood) || spells.Casting(id)) continue;
            shamans.Add(i);
        }
        if (shamans.Count == 0) return;

        // The enemies a pool slows: those who are not at home in the water.
        var slowed = new List<int>();
        foreach (int e in enemies)
            if (!sim.Content.Units[u.Def[e]].Amphibious) slowed.Add(e);
        if (slowed.Count < FloodLeast) return;

        int casters = Math.Min(Math.Min(shamans.Count, SpellSystem.MaxGroup), mana / cost);
        int half = SpellSystem.WallLength(casters) / 2; // the longest line a group of them floods
        long range2 = (long)(SpellSystem.CastRange - 1) * (SpellSystem.CastRange - 1);

        // Everyone near who must stay dry: not amphibious and not its rival.
        var (cx, cz) = Centroid(sim, ids);
        long near2 = (long)(SpellSystem.CastRange + half + 2) * (SpellSystem.CastRange + half + 2) + 24L * 24;
        var spare = new List<int>();
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Aboard[i] != 0 || Rival(u.Owner[i]) || sim.Content.Units[u.Def[i]].Amphibious) continue;
            if (Dist2(u.X[i].FloorToInt(), u.Z[i].FloorToInt(), cx, cz) <= near2) spare.Add(i);
        }

        int best = 0, bx1 = 0, bz1 = 0, bx2 = 0, bz2 = 0, bk = 0;
        var columns = new SortedSet<long>();
        foreach (int e in slowed)
        {
            int ex = u.X[e].FloorToInt(), ez = u.Z[e].FloorToInt();
            for (int across = 0; across < 2; across++)
            {
                int x1 = across == 0 ? ex - half : ex, z1 = across == 0 ? ez : ez - half;
                int x2 = across == 0 ? ex + half : ex, z2 = across == 0 ? ez : ez + half;
                int mx = (x1 + x2) / 2, mz = (z1 + z2) / 2;
                // Only the shamans in range now cast: none walks into the fight to cast.
                int k = 0;
                foreach (int s in shamans)
                    if (Dist2(u.X[s].FloorToInt(), u.Z[s].FloorToInt(), mx, mz) <= range2) k++;
                k = Math.Min(k, casters);
                if (k == 0) continue;
                spells.PlanFlood(x1, z1, x2, z2, _chasmPlan, k);
                columns.Clear();
                foreach (var c in _chasmPlan)
                    if (c.Room) columns.Add(Key(c.X, c.Z));
                if (columns.Count == 0) continue;
                int caught = 0;
                foreach (int o in slowed)
                    if (On(sim, o, columns)) caught++;
                if (caught < FloodLeast || caught <= best) continue;
                bool dry = false;
                foreach (int o in spare)
                    if (On(sim, o, columns)) { dry = true; break; }
                if (dry) continue;
                (best, bx1, bz1, bx2, bz2, bk) = (caught, x1, z1, x2, z2, k);
            }
        }
        if (best == 0) return;

        // The shamans nearest the line's middle cast it.
        int bmx = (bx1 + bx2) / 2, bmz = (bz1 + bz2) / 2;
        shamans.Sort((a, b) =>
        {
            long da = Dist2(u.X[a].FloorToInt(), u.Z[a].FloorToInt(), bmx, bmz);
            long db = Dist2(u.X[b].FloorToInt(), u.Z[b].FloorToInt(), bmx, bmz);
            return da != db ? da.CompareTo(db) : u.Id[a].CompareTo(u.Id[b]);
        });
        var group = new int[bk];
        for (int n = 0; n < bk; n++) group[n] = u.Id[shamans[n]];
        issue(new FloodCommand(Player, group, bx1, bz1, bx2, bz2));
        casting.AddRange(group);
        FloodsCast++;
        Note(sim, $"floods the ground under {best} enemy soldiers with {bk} shaman{(bk == 1 ? "" : "s")}");
    }
}
