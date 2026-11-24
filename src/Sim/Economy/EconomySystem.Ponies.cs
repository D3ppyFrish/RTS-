using System;
using Rts.Sim.Units;

namespace Rts.Sim.Economy;

/// <summary>
/// Goods carried out to far sites, and pack ponies (Handcarts, #149). A site whose nearest storage is more than
/// <see cref="FarSite"/> columns away when it is started has its cost set aside at storage (paid as ever) but not
/// yet at the site: its work goes only as far as the goods carried out so far allow (<see cref="MaxWork"/>). Its
/// builders fetch what is missing themselves, a worker's load at a time, from the storage nearest the site, and a
/// pack pony (a hauler that <see cref="UnitDef.Runs"/>) runs it out for them, quicker and more at a time. Ponies also
/// bring workshops and smelters what they work, and carry what workshops, smelters, quarries, mines and orchards
/// have made back to storage; while a player has a pony, quarry and mine workers leave what they dig piled at the
/// building (up to <see cref="QuarryPile"/>) for it and keep working. Automatic ponies find this work on their own
/// (see <see cref="Automation.AutomationSystem"/>).
/// </summary>
public sealed partial class EconomySystem
{
    /// <summary>How far (columns, centre to centre) a site may be from the nearest storage and have its goods at hand.</summary>
    public const int FarSite = 32;

    /// <summary>Goods a quarry or mine piles up for pack ponies before its workers carry them in again.</summary>
    public const int QuarryPile = 20;

    /// <summary>True for a pack pony (a hauler that <see cref="UnitDef.Runs"/>).</summary>
    public bool IsPony(int i) => _sim.Content.Units[U.Def[i]].Runs;

    /// <summary>True if the player has a pack pony.</summary>
    public bool HasPony(int player)
    {
        var u = U;
        for (int i = 0; i < u.Count; i++)
            if (u.Owner[i] == player && IsPony(i)) return true;
        return false;
    }

    private static (int X, int Z) Centre(Building b) => (b.X + b.Def.SizeX / 2, b.Z + b.Def.SizeZ / 2);

    /// <summary>
    /// True if the owner's nearest storage is within <see cref="FarSite"/> columns of the site's middle, or they have
    /// no storage to carry from at all.
    /// </summary>
    private bool AtHand(Building b)
    {
        var (x, z) = Centre(b);
        var s = NearestStorage(b.Owner, x, z);
        return s == null || s.CentreDistance2(x, z) <= 4L * FarSite * FarSite;
    }

    /// <summary>A site just started (and paid for): far from storage, its goods have to be carried out to it.</summary>
    private void SetAside(Building b)
    {
        if (b.State != BuildingState.Site || AtHand(b)) return;
        foreach (var c in b.Def.Cost) b.Due[(int)c.Good] += c.Amount;
    }

    /// <summary>
    /// How much work site b can have done with the goods carried out to it so far: all of it once nothing is due,
    /// otherwise the share of its cost that is there.
    /// </summary>
    public int MaxWork(Building b)
    {
        if (!b.HasDue) return b.Def.BuildTicks;
        int total = 0, due = 0;
        foreach (var c in b.Def.Cost) total += c.Amount;
        foreach (int d in b.Due) due += d;
        if (total == 0) return b.Def.BuildTicks;
        return (int)((long)b.Def.BuildTicks * Math.Max(0, total - due) / total);
    }

    /// <summary>A site with goods still due: once storage stands near it (built since), they are at hand.</summary>
    private void StepDue(Building b)
    {
        if (b.HasDue && (_sim.Tick + b.Id) % Tps == 0 && AtHand(b)) Array.Clear(b.Due);
    }

    /// <summary>Goods of a kind on their way out to site b with its builders and ponies (not unit <paramref name="except"/>).</summary>
    private int OnTheWayTo(Building b, Good good, int except)
    {
        var u = U;
        int n = 0;
        for (int k = 0; k < u.Count; k++)
        {
            if (k == except || u.JobTarget[k] != b.Id || u.JobStore[k] == 0) continue;
            if (u.Job[k] != UnitJob.Bring && u.Job[k] != UnitJob.Build) continue;
            if (u.Phase[k] == JobPhase.Fetch) { if (u.JobX[k] == (int)good) n += LoadOf(k); }
            else if (u.CarryAmount[k] > 0 && u.Carry[k] == good) n += u.CarryAmount[k];
        }
        return n;
    }

    /// <summary>The good site b is shortest of once what is on its way is counted (not unit <paramref name="except"/>'s); false if none.</summary>
    private bool NeedOf(Building b, int except, out Good need)
    {
        need = Good.Food;
        int most = 0;
        foreach (var g in Goods.All)
        {
            int left = b.Due[(int)g];
            if (left <= most) continue;
            left -= OnTheWayTo(b, g, except);
            if (left <= most) continue;
            most = left;
            need = g;
        }
        return most > 0;
    }

    /// <summary>True if site b (the player's) still has goods due that nobody is bringing yet: work for a pony.</summary>
    public bool WantsGoods(Building b) => b.State == BuildingState.Site && b.HasDue && NeedOf(b, -1, out _);

    /// <summary>Starts unit i off to fetch site b's next good from the storage nearest b; false if there is nothing (or nowhere) to fetch.</summary>
    private bool StartFetch(int i, Building b)
    {
        var (x, z) = Centre(b);
        if (!NeedOf(b, i, out var need) || NearestStorage(U.Owner[i], x, z) is not { } s) return false;
        SetPhase(i, JobPhase.Fetch);
        U.JobStore[i] = s.Id;
        U.JobX[i] = (int)need;
        return true;
    }

    /// <summary>
    /// A builder or pony fetching for site b: walks to the storage, leaves there what it carried of its own, and
    /// takes up to a load of the site's goods set aside there, no more than is still missing. Then back to the site.
    /// </summary>
    private void StepFetchFor(int i, Building b)
    {
        var u = U;
        var good = (Good)u.JobX[i];
        var s = Buildings.Get(u.JobStore[i]);
        if (s == null || s.Owner != u.Owner[i] || !s.Active || !s.Def.Storage)
        {
            if (!StartFetch(i, b)) Fetched(i);
            return;
        }
        switch (Approach(i, AtBuilding(i, s)))
        {
            case Travel.Arrived:
                if (u.CarryAmount[i] > 0) s.Stock[(int)u.Carry[i]] += u.CarryAmount[i];
                u.CarryAmount[i] = 0;
                int take = Math.Min(LoadOf(i), b.Due[(int)good] - OnTheWayTo(b, good, i));
                if (take > 0)
                {
                    u.Carry[i] = good;
                    u.CarryAmount[i] = take;
                }
                Fetched(i);
                return;
            case Travel.NeedSpot:
                if (AccessSpot(s, out int x, out int z, out int y)) _sim.SendUnit(i, x, z, y);
                else Fetched(i);
                return;
            case Travel.Failed:
                Fetched(i);
                return;
        }
    }

    /// <summary>Back to the site from a fetch (marked as bringing its goods only if it got some).</summary>
    private void Fetched(int i)
    {
        int store = U.CarryAmount[i] > 0 ? U.JobStore[i] : 0;
        SetPhase(i, JobPhase.ToWork);
        U.JobStore[i] = store;
    }

    /// <summary>
    /// Unit i at site b: what it fetched for b goes into it. Anything more than b still needs it keeps (to unload
    /// at storage later).
    /// </summary>
    private void HandOver(int i, Building b)
    {
        var u = U;
        if (u.JobStore[i] == 0 || u.CarryAmount[i] == 0) return;
        int g = (int)u.Carry[i];
        int n = Math.Min(b.Due[g], u.CarryAmount[i]);
        b.Due[g] -= n;
        u.CarryAmount[i] -= n;
        u.JobStore[i] = 0;
    }

    /// <summary>
    /// A pony bringing goods out to site <see cref="UnitStore.JobTarget"/>: fetch, carry out, hand over, and again
    /// while the site needs more; then it is free for other work. If the site is gone (or finished) what it carries
    /// goes back to storage.
    /// </summary>
    internal void StepBring(int i)
    {
        var u = U;
        var b = Buildings.Get(u.JobTarget[i]);
        if (b == null || b.Owner != u.Owner[i] || b.State != BuildingState.Site)
        {
            ToStorageOrEnd(i);
            return;
        }
        switch (u.Phase[i])
        {
            case JobPhase.Fetch:
                StepFetchFor(i, b);
                return;
            case JobPhase.Wait:
                if (--u.JobTimer[i] <= 0) DecideBring(i, b);
                return;
        }
        if (u.CarryAmount[i] == 0 || u.JobStore[i] == 0)
        {
            DecideBring(i, b);
            return;
        }
        if (WalkToBuilding(i, b, out bool there)) return;
        if (!there)
        {
            SetPhase(i, JobPhase.ToWork);
            return;
        }
        HandOver(i, b);
        DecideBring(i, b);
    }

    private void DecideBring(int i, Building b)
    {
        var u = U;
        if (u.CarryAmount[i] > 0)
        {
            // Goods for this site out to it; anything else (left over, or its own) to storage first.
            if (u.JobStore[i] != 0 && b.Due[(int)u.Carry[i]] > 0) SetPhase(i, JobPhase.ToWork);
            else ToStorageOrEnd(i);
            return;
        }
        if (!StartFetch(i, b)) EndJob(i);
    }

    /// <summary>The job is over: what the unit carries goes to the nearest storage first.</summary>
    private void ToStorageOrEnd(int i)
    {
        bool carrying = U.CarryAmount[i] > 0;
        EndJob(i);
        if (!carrying) return;
        U.Job[i] = UnitJob.Deliver;
        GoToStorage(i);
    }

    /// <summary>
    /// The good waiting at building b for someone to carry to storage, or null: what a workshop, smelter or orchard
    /// made, or what a quarry or mine has piled up for ponies.
    /// </summary>
    public static Good? Piled(Building b)
    {
        if (b.Def.IsQuarry)
        {
            foreach (var w in b.Def.Works)
                if (b.Stock[(int)w.Good] > 0) return w.Good;
            return null;
        }
        return Makes(b.Def) is { } g && b.Stock[(int)g] > 0 ? g : null;
    }

    /// <summary>All the goods piled at quarry or mine b.</summary>
    private static int PileOf(Building b)
    {
        int n = 0;
        foreach (var w in b.Def.Works) n += b.Stock[(int)w.Good];
        return n;
    }

    /// <summary>True if the workshop or smelter would take more of what it works, and storage has some.</summary>
    private bool WantsFeed(Building b)
    {
        var (x, z) = Centre(b);
        switch (b.Def.Kind)
        {
            case BuildingKind.Workshop:
                return b.Stock[(int)b.Def.Takes] < WorkshopStock && WorkshopWants(b) && NearestStorage(b.Owner, x, z, b.Def.Takes) != null;
            case BuildingKind.Smelter:
                return b.Stock[(int)Good.IronOre] < 2 * OrePerIngot && NearestStorage(b.Owner, x, z, Good.IronOre) != null
                    || b.Stock[(int)Good.Coal] < 2 * CoalPerIngot && NearestStorage(b.Owner, x, z, Good.Coal) != null;
            default:
                return false;
        }
    }

    /// <summary>
    /// True if the player's finished building b has work for a pony and none is at it yet: goods made or piled
    /// there to carry to storage, or a workshop or smelter to feed.
    /// </summary>
    public bool WantsPony(Building b)
    {
        if (!b.Active) return false;
        bool kind = b.Def.Kind is BuildingKind.Workshop or BuildingKind.Smelter or BuildingKind.Orchard
            || b.Def.IsQuarry && b.Def.Hunts == 0 && b.Def.Plants == 0;
        if (!kind || (Piled(b) == null && !WantsFeed(b))) return false;
        var u = U;
        for (int k = 0; k < u.Count; k++)
            if (u.Job[k] == UnitJob.Haul && u.JobTarget[k] == b.Id && IsPony(k)) return false;
        return true;
    }
}
