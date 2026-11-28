using System;
using System.Collections.Generic;
using Rts.Sim.Units;

namespace Rts.Sim.AI;

// Shadow step (Clan Kage's shadow casters, see "Clan Kage" in docs/rules.md), at every level. A wave on its way to a
// target more than StepFar columns off, with a shadow caster of its own ready to step, steps through the shadows: the
// caster takes the wave's units round it to the furthest spot along the way, at least StepShort columns short of the
// target and within the caster's range, that the step can be made to (seen, out of the daylight, ground to stand on),
// so long as it saves at least StepSaves columns of walking. Then the whole wave sets out for the target again.
public sealed partial class AiPlayer
{
    /// <summary>A wave this far (columns) from its target steps through the shadows towards it, if it can.</summary>
    public const int StepFar = 40;

    /// <summary>The wave comes out of the shadows at least this far (columns) short of its target.</summary>
    public const int StepShort = 12;

    /// <summary>A step is worth making only if it saves the caster at least this many columns of walking.</summary>
    public const int StepSaves = 20;

    // Columns between the spots along the way that are tried.
    private const int StepTry = 3;

    /// <summary>
    /// The smart levels, before they march: steps the wave through the shadows towards the building it is after, if it
    /// knows of one and can (see <see cref="ShadowStep(Simulation, int, int, int, Action{Command})"/>); true if it did,
    /// and the march is over.
    /// </summary>
    private bool ShadowStepWave(Simulation sim, Action<Command> issue)
    {
        if (_wave.Count == 0 || _waveLair != 0 || Target(sim, _waveBuilding) is not { } foe) return false;
        var (cx, cz) = Centroid(sim, _wave);
        int far = (int)IntMath.ISqrt(Dist2(cx, cz, foe.AX, foe.AZ));
        if (!ShadowStep(sim, far, foe.AX, foe.AZ, issue)) return false;
        _stage = null;
        return true;
    }

    /// <summary>
    /// Has the wave's ready shadow caster step it through the shadows towards its target (tx, tz), <paramref name="far"/>
    /// columns off; true if it did.
    /// </summary>
    private bool ShadowStep(Simulation sim, int far, int tx, int tz, Action<Command> issue)
    {
        if (far <= StepFar) return false;
        var u = sim.Units;
        int caster = -1;
        foreach (int id in _wave)
        {
            int i = u.IndexOf(id);
            if (i >= 0 && sim.Combat.ReadyToStep(i))
            {
                caster = i;
                break;
            }
        }
        if (caster < 0) return false;
        int cx = u.X[caster].FloorToInt(), cz = u.Z[caster].FloorToInt();
        int dist = (int)IntMath.ISqrt(Dist2(cx, cz, tx, tz));
        int range = sim.Content.Combat.ShadowStep.Range;
        // From the furthest the step reaches towards the target back towards the caster, every few columns.
        for (int t = Math.Min(dist - StepShort, range - 1); t >= StepSaves; t -= StepTry)
        {
            int x = cx + (tx - cx) * t / dist, z = cz + (tz - cz) * t / dist;
            var why = sim.Combat.CanShadowStep(caster, x, z);
            if (why is StepResult.NoMana or StepResult.InTheLight or StepResult.NotReady or StepResult.NoCaster) return false;
            if (why != StepResult.Ok) continue;
            var ids = new List<int>(_wave.Count) { u.Id[caster] };
            foreach (int id in _wave)
                if (id != u.Id[caster]) ids.Add(id);
            issue(new ShadowStepCommand(Player, ids.ToArray(), x, z));
            issue(new AttackMoveCommand(Player, _wave.ToArray(), tx, tz));
            Note(sim, $"steps its wave through the shadows {t} columns nearer its target");
            return true;
        }
        return false;
    }
}
