using System;
using System.Collections.Generic;
using Rts.Sim.Units;

namespace Rts.Sim.AI;

// Shield walls at Normal and Hard (issue #145): line infantry that can take up a shield wall (the Settlers' Drilled
// ranks) raise one when an enemy ranged unit it can see has it in range (plus WallShotSlack) and no enemy melee
// soldier is within WallMeleeClose; they lower it again once no shooter has them in range, or enemy foot soldiers
// close in (riders charging the wall are what it is for, so they do not count). Units in a wall are left out of the
// army's other orders (see NotFighting), which would lower it.
public sealed partial class AiPlayer
{
    /// <summary>A shooter this many blocks beyond its range of a line soldier still counts as shooting at it.</summary>
    public const int WallShotSlack = 2;
    /// <summary>An enemy foot soldier this close (blocks) to a line soldier keeps it out of a shield wall, or has it lower one.</summary>
    public const int WallMeleeClose = 6;
    /// <summary>Every how many ticks all its units in a shield wall are looked at, wherever they are (see <see cref="LowerIdleWalls"/>).</summary>
    public const int WallCheckTicks = 4 * ArmyTicks;

    /// <summary>
    /// Its units in a shield wall that the army's fighting no longer looks after (the fight moved on, the wave
    /// ended) lower it once no shooter it can see has them in range.
    /// </summary>
    private void LowerIdleWalls(Simulation sim, Action<Command> issue)
    {
        var u = sim.Units;
        var mine = new List<int>();
        for (int i = 0; i < u.Count; i++)
            if (u.Owner[i] == Player && u.ShieldWall[i]) mine.Add(u.Id[i]);
        if (mine.Count == 0) return;
        var enemies = new List<int>();
        for (int i = 0; i < u.Count; i++)
        {
            if (!Rival(u.Owner[i]) || u.Subdued[i]) continue;
            var def = sim.Content.Units[u.Def[i]];
            if (!def.CanAttack || def.Role is UnitRole.Worker or UnitRole.Trader || !sim.Vision.SeesUnit(Player, i)) continue;
            enemies.Add(i);
        }
        ShieldWalls(sim, mine, enemies, new List<int>(), issue);
    }

    /// <summary>
    /// Raises and lowers the shield walls of the army's line infantry (see the note above). The units in a wall
    /// after this, raised now or before, go into <paramref name="walled"/>.
    /// </summary>
    private void ShieldWalls(Simulation sim, List<int> ids, List<int> enemies, List<int> walled, Action<Command> issue)
    {
        var u = sim.Units;
        List<int>? raise = null, lower = null;
        foreach (int id in ids)
        {
            int i = u.IndexOf(id);
            if (i < 0 || u.Rout[i] > 0) continue;
            if (!u.ShieldWall[i] && !sim.Combat.CanShieldWall(i)) continue;
            int ux = u.X[i].FloorToInt(), uz = u.Z[i].FloorToInt();
            bool shot = false, close = false;
            foreach (int e in enemies)
            {
                var def = sim.Content.Units[u.Def[e]];
                long d = Dist2(ux, uz, u.X[e].FloorToInt(), u.Z[e].FloorToInt());
                if (def.Role is UnitRole.Siege or UnitRole.Caster || def.Machine) continue; // a wall does not stop these
                if (def.IsRanged)
                {
                    long reach = def.Range.FloorToInt() + WallShotSlack;
                    if (d <= reach * reach) shot = true;
                }
                else if (def.Role != UnitRole.Cavalry && d <= (long)WallMeleeClose * WallMeleeClose) close = true;
            }
            bool want = shot && !close;
            if (want) walled.Add(id);
            if (want && !u.ShieldWall[i]) (raise ??= new List<int>()).Add(id);
            else if (!want && u.ShieldWall[i]) (lower ??= new List<int>()).Add(id);
        }
        if (raise != null) issue(new ShieldWallCommand(Player, raise.ToArray(), true));
        if (lower != null) issue(new ShieldWallCommand(Player, lower.ToArray(), false));
    }
}
