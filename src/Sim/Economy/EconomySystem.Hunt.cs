using System;
using Rts.Sim.Units;

namespace Rts.Sim.Economy;

/// <summary>
/// Hunting (the design's food from hunting): a hunter's lodge is a quarry whose workers hunt the wild game within
/// <see cref="BuildingDef.Hunts"/> columns of it (see <see cref="MonsterSystem.Herds"/>). From the lodge a hunter picks
/// the animal nearest the lodge that no other hunter is after, stalks it to within <see cref="ShotRange"/>, takes a
/// round of work to aim (the lodge's <c>workTime</c>, at its work speed and with its player's gathering techs) and
/// shoots it dead, then walks to where it fell and carries what it gives (<see cref="UnitDef.Game"/>) to the nearest
/// storage before setting out from the lodge again. Hunters leave the last <see cref="HerdKeep"/> of a herd be, so a
/// herd hunted by lodges alone always grows back; soldiers sent at it can still kill it off, and a herd shot out to
/// the last is gone for good. With nothing to hunt a hunter waits at the lodge and looks again.
///
/// A hunter's step of the hunt is kept in <see cref="UnitStore.JobMisses"/>: 0 at the lodge, 1 stalking the game
/// whose id is in <see cref="UnitStore.JobX"/> (ticks stalked in <see cref="UnitStore.JobY"/>, aim in
/// <see cref="UnitStore.JobTimer"/>), 2 walking to the kill at (<see cref="UnitStore.JobX"/>,
/// <see cref="UnitStore.JobY"/>, <see cref="UnitStore.JobZ"/>) whose unit type is in <see cref="UnitStore.JobTimer"/>.
/// All numbers are first guesses for playtesting.
/// </summary>
public sealed partial class EconomySystem
{
    /// <summary>How close (blocks) a hunter gets to its game before it aims.</summary>
    public static readonly Fix64 ShotRange = Fix64.FromInt(6);
    /// <summary>Game further than this past the lodge's reach is let go.</summary>
    public const int HuntSlack = 8;
    /// <summary>A hunter that has not got within shot of its game in this long gives it up.</summary>
    public const int StalkTicks = 45 * Tps;
    /// <summary>Hunters leave this many of a herd alone, so it grows back.</summary>
    public const int HerdKeep = 2;

    private const int AtLodge = 0, Stalking = 1, ToKill = 2;

    /// <summary>One tick of hunter i working lodge <paramref name="lodge"/> (it is at the lodge, or out on the hunt).</summary>
    private void StepHunt(int i, Building lodge)
    {
        var u = U;
        switch (u.JobMisses[i])
        {
            case Stalking:
                Stalk(i, lodge);
                return;
            case ToKill:
                ToTheKill(i);
                return;
        }
        if (WalkToBuilding(i, lodge, out bool there)) return;
        if (!there)
        {
            SetPhase(i, JobPhase.ToWork);
            return;
        }
        int prey = Prey(i, lodge);
        if (prey < 0)
        {
            Wait(i);
            return;
        }
        u.JobMisses[i] = Stalking;
        u.JobX[i] = u.Id[prey];
        u.JobY[i] = u.JobTimer[i] = 0;
    }

    /// <summary>
    /// The wild game nearest the middle of the lodge within its reach (ties to the lowest id) that no other hunter is
    /// after, in a herd with more than <see cref="HerdKeep"/> left besides those other hunters are after; or -1.
    /// </summary>
    private int Prey(int hunter, Building lodge)
    {
        var u = U;
        var monsters = _sim.Monsters;
        Fix64 cx = Fix64.FromInt(lodge.X) + Fix64.FromRatio(lodge.Def.SizeX, 2), cz = Fix64.FromInt(lodge.Z) + Fix64.FromRatio(lodge.Def.SizeZ, 2);
        Fix64 reach = Fix64.FromInt(lodge.Def.Hunts);
        int best = -1;
        Fix64 bestD = Fix64.MaxValue;
        foreach (var herd in monsters.Herds)
        {
            if (herd.Cleared || herd.PackIds.Count - Hunted(herd, hunter) <= HerdKeep) continue;
            foreach (int id in herd.PackIds)
            {
                int j = u.IndexOf(id);
                if (j < 0 || u.Hp[j] <= 0) continue;
                Fix64 dx = u.X[j] - cx, dz = u.Z[j] - cz;
                if (Fix64.Abs(dx) > reach || Fix64.Abs(dz) > reach) continue;
                Fix64 d = dx * dx + dz * dz;
                if (d > reach * reach || IsHunted(id, hunter)) continue;
                if (d < bestD || (d == bestD && id < u.Id[best]))
                {
                    best = j;
                    bestD = d;
                }
            }
        }
        return best;
    }

    /// <summary>How many of the herd's animals hunters other than <paramref name="hunter"/> are after.</summary>
    private int Hunted(Rts.Sim.Units.Lair herd, int hunter)
    {
        int n = 0;
        foreach (int id in herd.PackIds)
            if (IsHunted(id, hunter)) n++;
        return n;
    }

    /// <summary>True if a hunter other than <paramref name="hunter"/> is stalking the game with the id.</summary>
    private bool IsHunted(int id, int hunter)
    {
        var u = U;
        for (int k = 0; k < u.Count; k++)
            if (k != hunter && u.Job[k] == UnitJob.Quarry && u.JobMisses[k] == Stalking && u.JobX[k] == id) return true;
        return false;
    }

    /// <summary>Stalking: closes in on the game, aims once within <see cref="ShotRange"/>, and shoots.</summary>
    private void Stalk(int i, Building lodge)
    {
        var u = U;
        int p = u.IndexOf(u.JobX[i]);
        int reach = lodge.Def.Hunts + HuntSlack + Math.Max(lodge.Def.SizeX, lodge.Def.SizeZ) / 2;
        if (p < 0 || u.Hp[p] <= 0 || !_sim.Monsters.IsWild(p) || lodge.ColumnDistance(u.X[p].FloorToInt(), u.Z[p].FloorToInt()) > reach
            || ++u.JobY[i] > StalkTicks)
        {
            // Gone, out of reach or never caught up with: back to the lodge to pick another.
            BackToLodge(i);
            return;
        }
        Fix64 dx = u.X[p] - u.X[i], dz = u.Z[p] - u.Z[i];
        bool inShot = dx * dx + dz * dz <= ShotRange * ShotRange && Math.Abs(u.Y[p] - u.Y[i]) <= 4;
        if (!inShot)
        {
            u.JobTimer[i] = 0;
            // Walks at it, and again whenever it has moved off from where the walk was going.
            int px = u.X[p].FloorToInt(), pz = u.Z[p].FloorToInt();
            if (u.Order[i] != UnitOrder.Move || Math.Abs(u.OrderX[i] - px) > 2 || Math.Abs(u.OrderZ[i] - pz) > 2)
                _sim.SendUnit(i, px, pz, u.Y[p]);
            return;
        }
        if (u.Order[i] != UnitOrder.Idle) Halt(i);
        if (++u.JobTimer[i] < ByWork(i, QuarryTicks(u.Owner[i], lodge.Def))) return;
        _sim.Combat.Shoot(p, i);
        u.JobMisses[i] = ToKill;
        u.JobX[i] = u.X[p].FloorToInt();
        u.JobY[i] = u.Y[p];
        u.JobZ[i] = u.Z[p].FloorToInt();
        u.JobTimer[i] = u.Def[p];
        u.JobRetry[i] = 0;
    }

    /// <summary>Walking to the kill: there, takes up what it gives and carries it to storage.</summary>
    private void ToTheKill(int i)
    {
        var u = U;
        Fix64 dx = Fix64.FromInt(u.JobX[i]) + Fix64.Half - u.X[i], dz = Fix64.FromInt(u.JobZ[i]) + Fix64.Half - u.Z[i];
        if (dx * dx + dz * dz <= Fix64.FromInt(2) && Math.Abs(u.Y[i] - u.JobY[i]) <= 2)
        {
            Halt(i);
            var game = _sim.Content.Units[u.JobTimer[i]].Game;
            u.JobMisses[i] = AtLodge;
            u.JobX[i] = u.JobY[i] = u.JobZ[i] = u.JobTimer[i] = 0;
            if (game.Length > 0)
            {
                AddCarry(i, game[0].Good, game[0].Amount);
                GameHunted++;
            }
            GoToStorage(i);
            return;
        }
        if (u.Order[i] == UnitOrder.Move) return;
        if (u.JobRetry[i]++ >= MaxRetries)
        {
            // It cannot get there (fell in water, over a cliff): the kill is lost.
            BackToLodge(i);
            return;
        }
        _sim.SendUnit(i, u.JobX[i], u.JobZ[i], u.JobY[i]);
    }

    /// <summary>Gives up the hunt in hand and walks back to the lodge to look again.</summary>
    private void BackToLodge(int i)
    {
        var u = U;
        u.JobMisses[i] = AtLodge;
        u.JobX[i] = u.JobY[i] = u.JobZ[i] = u.JobTimer[i] = u.JobRetry[i] = 0;
        SetPhase(i, JobPhase.ToWork);
    }

    /// <summary>The wild game within the lodge's reach now (for the game's panel).</summary>
    public int GameInReach(Building lodge)
    {
        var u = U;
        Fix64 cx = Fix64.FromInt(lodge.X) + Fix64.FromRatio(lodge.Def.SizeX, 2), cz = Fix64.FromInt(lodge.Z) + Fix64.FromRatio(lodge.Def.SizeZ, 2);
        Fix64 reach = Fix64.FromInt(lodge.Def.Hunts);
        int n = 0;
        foreach (var herd in _sim.Monsters.Herds)
        {
            if (herd.Cleared) continue;
            foreach (int id in herd.PackIds)
            {
                int j = u.IndexOf(id);
                if (j < 0) continue;
                Fix64 dx = u.X[j] - cx, dz = u.Z[j] - cz;
                if (Fix64.Abs(dx) <= reach && Fix64.Abs(dz) <= reach && dx * dx + dz * dz <= reach * reach) n++;
            }
        }
        return n;
    }

    /// <summary>Game hunters have brought home this match (every player's).</summary>
    public int GameHunted { get; private set; }
}
