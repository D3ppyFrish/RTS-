using System;
using Rts.Sim.Units;

namespace Rts.Sim.Economy;

/// <summary>
/// Salvage (Craft guilds): a player with the <see cref="Content.TechEffectType.Salvage"/> tech marks one of their own
/// finished buildings to be taken down (see <see cref="SalvageCommand"/>). Workers sent to it, or automatic workers
/// taking it as building work, work beside it for half as long as it took to build; then it is gone, with no rubble,
/// and half its cost (in proportion to the hit points it has left) and every good it held go to the player's nearest
/// other storage. A town is moved, a forward storehouse stripped before raiders take it, or an outgrown wall cleared.
/// </summary>
public sealed partial class EconomySystem
{
    /// <summary>
    /// True if the player can mark the building for salvage: they have the tech, it is theirs and finished, and it is not
    /// a city-state's totem.
    /// </summary>
    public bool Salvageable(int player, Building b) =>
        b.Owner == player && b.Active && b.Def.Kind != BuildingKind.Totem && player >= 0 && player < Simulation.WildPlayer
        && CanSalvage(player);

    /// <summary>Work (ticks at the usual work speed, times 100) taking the building down takes: half what building it did.</summary>
    public static int SalvageWork(Building b) => Math.Max(Tps, b.Def.BuildTicks / 2) * 100;

    /// <summary>How far along taking the building down is, in percent.</summary>
    public static int SalvagePercent(Building b) => (int)Math.Min(100L, (long)b.Salvaged * 100 / SalvageWork(b));

    /// <summary>
    /// What taking the building down brings back: half its cost, in proportion to the hit points it has left (rounded
    /// down), and the goods it holds (indexed by <see cref="Good"/>).
    /// </summary>
    public int[] SalvageReturn(Building b)
    {
        var back = new int[b.Stock.Length];
        int max = Math.Max(1, b.MaxHealth);
        foreach (var c in b.Def.Cost)
            back[(int)c.Good] += (int)((long)c.Amount * b.Health / (2L * max));
        for (int g = 0; g < back.Length; g++) back[g] += b.Stock[g];
        return back;
    }

    /// <summary>Marks the building for salvage (or with Stop takes the mark off) and sends the command's workers to it.</summary>
    internal void Apply(SalvageCommand c)
    {
        var b = Buildings.Get(c.Building);
        if (b == null || b.Owner != c.PlayerId) return;
        if (c.Stop)
        {
            b.Salvaging = false;
            foreach (int i in Workers(c))
                if (U.Job[i] == UnitJob.Salvage && U.JobTarget[i] == b.Id) EndJob(i);
            return;
        }
        if (!Salvageable(c.PlayerId, b)) return;
        b.Salvaging = true;
        foreach (int i in Workers(c))
        {
            if (U.Job[i] == UnitJob.Salvage && U.JobTarget[i] == b.Id) continue;
            StartJob(i, UnitJob.Salvage, b.Id);
        }
    }

    private void StepSalvage(int i)
    {
        var u = U;
        var b = Buildings.Get(u.JobTarget[i]);
        if (b == null || b.Owner != u.Owner[i] || !b.Salvaging || !b.Active)
        {
            EndJob(i);
            return;
        }
        if (WalkToBuilding(i, b, out bool there)) return;
        if (!there)
        {
            SetPhase(i, JobPhase.ToWork);
            return;
        }
        b.Salvaged += WorkOf(i);
        if ((_sim.Tick + u.Id[i]) % Tps == 0) _sim.Evolution.Built(i);
        if (b.Salvaged < SalvageWork(b)) return;
        FinishSalvage(b);
        EndJob(i);
    }

    /// <summary>The building is down: what it gives back goes to the nearest other storage of its owner, and it is gone.</summary>
    private void FinishSalvage(Building b)
    {
        var back = SalvageReturn(b);
        Building? store = null;
        long bestD = long.MaxValue;
        int cx = b.X + b.Def.SizeX / 2, cz = b.Z + b.Def.SizeZ / 2;
        foreach (var s in Buildings.All)
        {
            if (s == b || s.Owner != b.Owner || !s.Active || !s.Def.Storage) continue;
            long d = s.CentreDistance2(cx, cz);
            if (d >= bestD) continue;
            bestD = d;
            store = s;
        }
        if (store != null)
            for (int g = 0; g < back.Length; g++) store.Stock[g] += back[g];
        Demolish(b);
    }
}
