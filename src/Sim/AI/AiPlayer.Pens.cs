using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.AI;

// Beast pens and stampedes (issue #152), Hard only. Once it can build a beast pen and has at least PenBeasts tamed beasts
// (wolves and boars it recruited), it lays out a pen near its town hall; it sends its free beasts (not out with a wave,
// not fighting) into its finished pen. When a pen holds PenBeasts or more and it sees an enemy army bunched up within
// StampedeReach columns of the pen (at least StampedeCrowd enemy soldiers within StampedeHuddle columns of one of them),
// it looses the stampede at them: along the line from the pen's side towards them, on through them.
public sealed partial class AiPlayer
{
    /// <summary>Tamed beasts it has before it builds a pen, and penned before it looses a stampede.</summary>
    public const int PenBeasts = 2;
    /// <summary>It looses a stampede at enemies this near (columns) its pen.</summary>
    public const int StampedeReach = 36;
    /// <summary>It looses one at this many enemy soldiers or more within <see cref="StampedeHuddle"/> columns of one of them.</summary>
    public const int StampedeCrowd = 4, StampedeHuddle = 4;
    /// <summary>The stampede runs on this many columns past the bunch it is aimed at.</summary>
    public const int StampedeBeyond = 8;

    /// <summary>The beast pen type it can build, or null.</summary>
    private BuildingDef? PenDef(Simulation sim)
    {
        foreach (var d in sim.Content.Buildings)
            if (d.Kind == BuildingKind.Pen && d.UpgradeOf == null && sim.Economy.Unlocked(Player, d)) return d;
        return null;
    }

    /// <summary>Hard: builds a pen for its tamed beasts, fills it, and looses stampedes at bunched enemies (see the top of this file).</summary>
    private void Pens(Simulation sim, Building hall, Action<Command> issue)
    {
        if (!Hard) return;
        var u = sim.Units;
        var free = new List<int>();
        int beasts = 0;
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Owner[i] != Player || u.Subdued[i] || !sim.Content.Units[u.Def[i]].Pennable) continue;
            beasts++;
            if (u.Penned[i] != 0 || u.PenFor[i] != 0 || u.RunStage[i] != 0 || u.Rout[i] > 0 || u.Target[i] != 0) continue;
            int id = u.Id[i];
            if (_wave.Contains(id) || _joining.Contains(id)) continue;
            free.Add(id);
        }
        if (beasts < PenBeasts) return;
        Building? pen = null;
        bool site = false;
        foreach (var b in sim.Buildings.All)
        {
            if (b.Owner != Player || b.Def.Kind != BuildingKind.Pen) continue;
            if (!b.Active) site = true;
            else if (pen == null || sim.Combat.PenCount(b.Id, coming: true) < sim.Combat.PenCount(pen.Id, coming: true)) pen = b;
        }
        if (pen == null)
        {
            if (!site && PenDef(sim) is { } def)
            {
                Want(sim, def, hall, HardTownRadius, false, issue);
                Note(sim, "lays out a beast pen for its tamed beasts");
            }
            return;
        }
        int room = pen.Def.Pen!.Keeps - sim.Combat.PenCount(pen.Id, coming: true);
        if (free.Count > 0 && room > 0)
        {
            if (free.Count > room) free.RemoveRange(room, free.Count - room);
            issue(new PenCommand(Player, free.ToArray(), pen.Id));
            Note(sim, $"sends {free.Count} beast{(free.Count == 1 ? "" : "s")} into its pen");
        }
        foreach (var b in sim.Buildings.All)
            if (b.Owner == Player && b.Active && b.Def.Kind == BuildingKind.Pen && sim.Combat.PenCount(b.Id) >= PenBeasts)
                Stampede(sim, b, issue);
    }

    /// <summary>Looses pen b's beasts at the biggest bunch of enemy soldiers it sees within <see cref="StampedeReach"/>, if one is big enough.</summary>
    private void Stampede(Simulation sim, Building b, Action<Command> issue)
    {
        var u = sim.Units;
        var (px, pz) = Middle(b);
        long reach2 = (long)StampedeReach * StampedeReach;
        var foes = new List<int>();
        for (int i = 0; i < u.Count; i++)
        {
            if (!Rival(u.Owner[i]) || u.Subdued[i] || u.Aboard[i] != 0 || u.Town[i] != 0) continue;
            var def = sim.Content.Units[u.Def[i]];
            if (!def.CanAttack || def.IsBoat || def.Role is UnitRole.Worker or UnitRole.Trader) continue;
            if (Dist2(u.X[i].FloorToInt(), u.Z[i].FloorToInt(), px, pz) > reach2 || !sim.Vision.SeesUnit(Player, i)) continue;
            foes.Add(i);
        }
        if (foes.Count < StampedeCrowd) return;
        int best = -1, most = 0;
        long huddle2 = (long)StampedeHuddle * StampedeHuddle;
        foreach (int i in foes)
        {
            int n = 0;
            foreach (int j in foes)
                if (Dist2(u.X[i].FloorToInt(), u.Z[i].FloorToInt(), u.X[j].FloorToInt(), u.Z[j].FloorToInt()) <= huddle2) n++;
            if (n > most) (best, most) = (i, n);
        }
        if (most < StampedeCrowd) return;
        int tx = u.X[best].FloorToInt(), tz = u.Z[best].FloorToInt();
        long dx = tx - px, dz = tz - pz;
        long len = Math.Max(1, IntMath.ISqrt(dx * dx + dz * dz));
        // From the pen's edge towards them, and on past them.
        int half = Math.Max(b.Def.SizeX, b.Def.SizeZ) / 2 + 1;
        int x1 = px + (int)(dx * half / len), z1 = pz + (int)(dz * half / len);
        int x2 = tx + (int)(dx * StampedeBeyond / len), z2 = tz + (int)(dz * StampedeBeyond / len);
        x2 = Math.Clamp(x2, 0, sim.World.SizeX - 1);
        z2 = Math.Clamp(z2, 0, sim.World.SizeZ - 1);
        issue(new StampedeCommand(Player, b.Id, x1, z1, x2, z2));
        Note(sim, $"looses a stampede of {sim.Combat.PenCount(b.Id)} beasts at {most} enemies by {tx},{tz}");
    }
}
