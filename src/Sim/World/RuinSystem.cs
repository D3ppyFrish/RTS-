using System;
using System.Collections.Generic;
using Rts.Sim.Content;
using Rts.Sim.Units;

namespace Rts.Sim.World;

/// <summary>One of the map's old ruins (see <see cref="RegionMap.Ruins"/>) and what was left in it.</summary>
public sealed class AncientRuin
{
    /// <summary>Position in <see cref="RuinSystem.Ruins"/> (and in the map's list).</summary>
    public int Index { get; init; }
    /// <summary>The middle column, and the level of its floor (units stand on the block above).</summary>
    public int X { get; init; }
    public int Y { get; init; }
    public int Z { get; init; }
    /// <summary>What it holds, or null if the data has nothing for it.</summary>
    public RuinFindDef? Find { get; internal set; }
    /// <summary>The player whose units found what it held, or -1 while nobody has.</summary>
    public int SearchedBy { get; internal set; } = -1;
    /// <summary>The tick it was searched, or -1.</summary>
    public long SearchedAt { get; internal set; } = -1;
    /// <summary>The unit (id) that found it, or 0.</summary>
    public int Finder { get; internal set; }
    /// <summary>Ticks of searching done on it by each player's units (see <see cref="RuinSystem.SearchTicks"/>).</summary>
    internal readonly int[] Work = new int[Simulation.WildPlayer + 1];

    public bool Searched => SearchedBy >= 0;

    /// <summary>As an ancient site (see <see cref="DominionSystem"/>): the player who holds it, or -1.</summary>
    public int Holder { get; internal set; } = -1;
    /// <summary>The tick the holder took it, or -1.</summary>
    public long HeldSince { get; internal set; } = -1;
    /// <summary>The player whose fighting units are taking it, or -1.</summary>
    public int Claimant { get; internal set; } = -1;
    /// <summary>Ticks the claimant has had it to themselves, of <see cref="DominionSystem.ClaimTicks"/>.</summary>
    public int Claim { get; internal set; }
}

/// <summary>
/// Searching the map's old ruins (the design doc's discoveries: the story is told by what the land holds). The map
/// generator puts up the broken walls of old stone halls in the open country (<see cref="RegionGenerator.RuinsPer512"/>
/// for every 512 by 512 blocks). At match start each is given one find from <c>data/ruins.json</c>
/// (<see cref="RuinFindDef"/>), shuffled by the map's seed: goods, a relic, or both, with a line of writing that tells
/// a little of who built them. A find with a relic goes to one ruin at most.
///
/// Any player's units can search a ruin (<see cref="SearchRuinCommand"/>): they walk into it and search, and every
/// tick each one inside (up to <see cref="MaxHands"/> of a player's) adds a tick of work to their player's search of
/// it. The first player to reach <see cref="SearchTicks"/> gets the find: the goods go into their storage nearest the
/// ruin, and the relic to the unit that found it if it can bear one (a fighting unit with none yet), or it is left
/// lying at its feet for one that can (see <see cref="DroppedItemSystem"/>). A ruin is searched once a match; rivals'
/// searches of it are over. An order of any other kind, a fight, running away or being beaten stops a unit
/// searching; work done stays with its player. All numbers are starting guesses to be tuned in playtests.
/// </summary>
public sealed class RuinSystem
{
    /// <summary>Ticks of one unit's searching to find what a ruin holds: 12 seconds.</summary>
    public const int SearchTicks = 12 * Simulation.TicksPerSecond;
    /// <summary>Most units of one player whose searching counts each tick: more get in each other's way.</summary>
    public const int MaxHands = 3;
    /// <summary>Columns from a ruin's middle (across) within which a unit searches it: inside its walls.</summary>
    public const int Reach = RegionGenerator.RuinHalf - 1;
    /// <summary>Levels above or below the ruin's floor a searching unit may stand.</summary>
    public const int ReachHeight = 2;

    private readonly Simulation _sim;
    private readonly List<AncientRuin> _ruins = new();
    // Units searching (in the order they started) and the ruin each searches.
    private readonly List<Searcher> _searchers = new();

    private sealed class Searcher
    {
        public int Id;
        public int Ruin;
        public int Player;
        public int X, Y, Z;
    }

    public RuinSystem(Simulation sim)
    {
        _sim = sim;
    }

    private UnitStore U => _sim.Units;

    /// <summary>The map's ruins, in the map's order.</summary>
    public IReadOnlyList<AncientRuin> Ruins => _ruins;

    /// <summary>The ruin (index) the unit (id) is searching, or -1.</summary>
    public int Searching(int id)
    {
        foreach (var s in _searchers)
            if (s.Id == id) return s.Ruin;
        return -1;
    }

    /// <summary>Ticks of searching the player's units have done on the ruin (index), of <see cref="SearchTicks"/>.</summary>
    public int Progress(int player, int ruin) =>
        ruin >= 0 && ruin < _ruins.Count && player >= 0 && player <= Simulation.WildPlayer ? _ruins[ruin].Work[player] : 0;

    /// <summary>The ruin whose walls stand round column (x, z) (within a block of them), or null.</summary>
    public AncientRuin? At(int x, int z)
    {
        int r = RegionGenerator.RuinHalf + 1;
        foreach (var ruin in _ruins)
            if (Math.Abs(x - ruin.X) <= r && Math.Abs(z - ruin.Z) <= r) return ruin;
        return null;
    }

    public ulong ComputeHash()
    {
        ulong h = 14695981039346656037UL;
        void Mix(long v) { h = (h ^ (ulong)v) * 1099511628211UL; }
        Mix(_ruins.Count);
        foreach (var r in _ruins)
        {
            Mix(r.X); Mix(r.Y); Mix(r.Z); Mix(r.Find?.Index ?? -1); Mix(r.SearchedBy); Mix(r.SearchedAt); Mix(r.Finder);
            foreach (int w in r.Work) Mix(w);
        }
        Mix(_searchers.Count);
        foreach (var s in _searchers)
        {
            Mix(s.Id); Mix(s.Ruin); Mix(s.Player); Mix(s.X); Mix(s.Y); Mix(s.Z);
        }
        return h;
    }

    // --- Setup -------------------------------------------------------------------------------------

    /// <summary>
    /// Match setup (the simulation calls it when it starts; call it again after adding ruins to the map by hand): a
    /// ruin for each of the map's (<see cref="RegionMap.Ruins"/>), each given a find. The finds are
    /// shuffled by the map's seed and dealt out in turn; one with a relic is dealt once, and when there are more
    /// ruins than finds, the rest go round the finds without one.
    /// </summary>
    public void Place()
    {
        _ruins.Clear();
        _searchers.Clear();
        var finds = new List<RuinFindDef>(_sim.Content.RuinFinds);
        var rng = new DeterministicRandom(_sim.Map.Seed ^ 0x5E7A1C0FFEEUL);
        for (int k = finds.Count - 1; k > 0; k--)
        {
            int j = rng.Range(0, k + 1);
            (finds[k], finds[j]) = (finds[j], finds[k]);
        }
        var again = finds.FindAll(f => f.Relic == null);
        int n = 0;
        foreach (var (x, y, z) in _sim.Map.Ruins)
        {
            RuinFindDef? find = n < finds.Count ? finds[n] : again.Count > 0 ? again[(n - finds.Count) % again.Count] : null;
            _ruins.Add(new AncientRuin { Index = n, X = x, Y = y, Z = z, Find = find });
            n++;
        }
    }

    // --- Searching ---------------------------------------------------------------------------------

    /// <summary>
    /// True if unit i can search a ruin: a player's unit (not a wild monster, nor a city-state's), not beaten and
    /// not running away.
    /// </summary>
    public bool CanSearch(int i)
    {
        var u = U;
        return i >= 0 && i < u.Count && u.Hp[i] > 0 && u.Owner[i] >= 0 && u.Owner[i] < Simulation.WildPlayer
            && !u.Subdued[i] && u.Rout[i] == 0 && u.Town[i] == 0;
    }

    /// <summary>True if the player can send any of these units (indexes) to search the ruin (index) now.</summary>
    public bool CanUse(int player, int ruin, IEnumerable<int> units)
    {
        if (ruin < 0 || ruin >= _ruins.Count || _ruins[ruin].Searched) return false;
        foreach (int i in units)
            if (i >= 0 && i < U.Count && U.Owner[i] == player && CanSearch(i)) return true;
        return false;
    }

    /// <summary>The player's units that can search walk into the ruin and search it.</summary>
    internal void Apply(SearchRuinCommand c)
    {
        if (c.Ruin < 0 || c.Ruin >= _ruins.Count || _ruins[c.Ruin].Searched) return;
        var ruin = _ruins[c.Ruin];
        var u = U;
        foreach (int i in _sim.OwnedUnits(c))
        {
            if (!CanSearch(i)) continue;
            _searchers.RemoveAll(s => s.Id == u.Id[i]);
            if (!Spot(ruin, i, out int sx, out int sy, out int sz)) continue;
            u.ClearJob(i);
            u.ClearCombat(i);
            u.ClearFormation(i);
            _searchers.Add(new Searcher { Id = u.Id[i], Ruin = ruin.Index, Player = c.PlayerId, X = sx, Y = sy, Z = sz });
            if (!InReach(ruin, i)) _sim.SendUnit(i, sx, sz, sy);
            else u.Order[i] = UnitOrder.Idle;
        }
    }

    /// <summary>The player's units among the command's stop searching (they were given another order).</summary>
    internal void Release(UnitsCommand c)
    {
        if (_searchers.Count == 0) return;
        foreach (int i in _sim.OwnedUnits(c))
            _searchers.RemoveAll(s => s.Id == U.Id[i]);
    }

    /// <summary>
    /// Where unit i walks to search the ruin: the open column inside its walls, within a column of its middle,
    /// nearest the unit (so a group spreads over the floor a little).
    /// </summary>
    private bool Spot(AncientRuin ruin, int i, out int x, out int y, out int z)
    {
        var u = U;
        int ux = u.X[i].FloorToInt(), uz = u.Z[i].FloorToInt();
        long best = long.MaxValue;
        x = y = z = -1;
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int cx = ruin.X + dx, cz = ruin.Z + dz;
            int cy = Walk.NearestStandLevel(_sim.World, cx, cz, ruin.Y + 1, ReachHeight);
            if (cy < 0) continue;
            long d = (long)(cx - ux) * (cx - ux) + (long)(cz - uz) * (cz - uz);
            if (d >= best) continue;
            best = d;
            (x, y, z) = (cx, cy, cz);
        }
        return best != long.MaxValue;
    }

    /// <summary>Unit i stands inside the ruin's walls.</summary>
    public bool InReach(AncientRuin ruin, int i)
    {
        var u = U;
        int x = u.X[i].FloorToInt(), z = u.Z[i].FloorToInt();
        return Math.Abs(x - ruin.X) <= Reach && Math.Abs(z - ruin.Z) <= Reach && Math.Abs(u.Y[i] - (ruin.Y + 1)) <= ReachHeight;
    }

    /// <summary>
    /// After the dusk tree: searchers walk into their ruin and search it; one that has another order, is fighting,
    /// can no longer search, or stopped short of the ruin gives up. Each player's searchers inside a ruin (up to
    /// <see cref="MaxHands"/>) add their ticks of work; the first player to reach <see cref="SearchTicks"/> finds
    /// what it holds (in ruin order, then player order, if two get there on the same tick).
    /// </summary>
    public void Step()
    {
        if (_searchers.Count == 0) return;
        var u = U;
        Span<int> hands = stackalloc int[Simulation.WildPlayer + 1];
        for (int r = 0; r < _ruins.Count; r++)
        {
            hands.Clear();
            int first = -1;
            for (int k = 0; k < _searchers.Count; k++)
            {
                var s = _searchers[k];
                if (s.Ruin != r) continue;
                var ruin = _ruins[r];
                int i = u.IndexOf(s.Id);
                bool walking = i >= 0 && u.Order[i] == UnitOrder.Move && u.OrderX[i] == s.X && u.OrderZ[i] == s.Z;
                bool stays = i >= 0 && u.Owner[i] == s.Player && CanSearch(i) && !ruin.Searched && u.Target[i] == 0
                    && u.Job[i] == UnitJob.None && (walking || (u.Order[i] == UnitOrder.Idle && InReach(ruin, i)));
                if (!stays)
                {
                    _searchers.RemoveAt(k--);
                    continue;
                }
                if (walking || hands[s.Player] >= MaxHands) continue;
                hands[s.Player]++;
                if (++ruin.Work[s.Player] >= SearchTicks && first < 0) first = k;
            }
            if (first >= 0) Found(_ruins[r], _searchers[first]);
        }
    }

    /// <summary>The searcher's player finds what the ruin holds; every search of it is over.</summary>
    private void Found(AncientRuin ruin, Searcher s)
    {
        var u = U;
        int i = u.IndexOf(s.Id);
        int p = s.Player;
        ruin.SearchedBy = p;
        ruin.SearchedAt = _sim.Tick;
        ruin.Finder = s.Id;
        _searchers.RemoveAll(o => o.Ruin == ruin.Index);
        _sim.Economy.Discover(p, Rts.Sim.Content.Discovery.Ruin);
        if (ruin.Find is not { } find) return;
        if (find.Goods.Length > 0 && _sim.Economy.NearestStorage(p, ruin.X, ruin.Z) is { } store)
            foreach (var g in find.Goods) store.Stock[(int)g.Good] += g.Amount;
        if (find.Relic is { } relic)
        {
            var def = _sim.Content.Units[u.Def[i]];
            if (u.Relic[i] == 0 && def.Damage > 0 && def.Role != UnitRole.Worker)
            {
                u.Relic[i] = (short)(relic.Index + 1);
                u.Hp[i] = Math.Min(_sim.MaxHpOf(i), u.Hp[i] + relic.Hp);
            }
            else _sim.Items.Drop(relic, u.X[i].FloorToInt(), u.Y[i], u.Z[i].FloorToInt(), p);
        }
        _sim.Events.RuinSearched(p, ruin, s.Id);
    }
}
