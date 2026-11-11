using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.JobGauge.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using MitCue.Core;
using MitCue.Core.Reminders;
using MitCue.Data;
using MitCue.Util;

namespace MitCue.Game;

/// <summary>
/// The local player's cooldowns, charges and job resources for the reminder scheduler (ActionManager +
/// job gauges; docs/research/clientstructs.md §3), plus a usage fallback: for the actions of the active
/// reminders it watches recast/charge changes each frame and reports them as own uses. That covers pet
/// commands and anything the ActionEffect path misses. All calls must run on the framework thread
/// (the scheduler calls back from <c>Update</c>, which runs in <c>IFramework.Update</c>).
/// </summary>
internal sealed unsafe class CooldownProvider : ICooldownProvider
{
    private readonly CoreData data;
    private readonly GameData gameData;
    private readonly IJobGauges gauges;
    private readonly ErrorReporter errors;
    private readonly Dictionary<uint, RecastTrack> tracks = [];
    private readonly List<uint> trackRemovals = [];
    private readonly Dictionary<uint, Resource> resources = [];
    private int frame;

    /// <summary>Creates the provider.</summary>
    public CooldownProvider(CoreData data, GameData gameData, IJobGauges gauges, ErrorReporter errors)
    {
        this.data = data;
        this.gameData = gameData;
        this.gauges = gauges;
        this.errors = errors;
    }

    private enum Resource
    {
        None,
        Unknown,
        Lily,
        BloodLily,
        Aetherflow,
        Addersgall,
        Addersting,
    }

    /// <summary>The local player's current job (set every frame).</summary>
    public Job Job { get; set; }

    /// <summary>True while the local player exists (set every frame); otherwise every answer is "unknown".</summary>
    public bool Available { get; set; }

    /// <summary>Clears the usage-fallback state (zone/job change).</summary>
    public void ResetTracking() => this.tracks.Clear();

    /// <inheritdoc/>
    public double? SecondsUntilReady(uint actionId)
    {
        if (!this.Available || actionId == 0)
            return null;
        var am = Manager();
        if (am == null)
            return null;

        try
        {
            // GCD spells share the global cooldown; a rolling GCD is not worth a warning.
            if (this.gameData.IsGcd(actionId))
                return 0;

            var seconds = Remaining(am, actionId);
            if (seconds <= 0)
            {
                // Upgrades/replacements: the button may currently fire another action with its own recast.
                var adjusted = am->GetAdjustedActionId(actionId);
                if (adjusted != 0 && adjusted != actionId && !this.gameData.IsGcd(adjusted))
                    seconds = Math.Max(seconds, Remaining(am, adjusted));
            }

            return seconds;
        }
        catch (Exception ex)
        {
            this.errors.Report("cooldowns.read", "Reading cooldowns", ex);
            return null;
        }
    }

    /// <inheritdoc/>
    public bool? HasResources(uint actionId)
    {
        if (!this.Available || actionId == 0)
            return null;

        try
        {
            var resource = this.ResourceOf(actionId, out var amount);
            if (resource == Resource.None)
                return true;

            // Gauge memory is shared between jobs: only read the gauge of the current job.
            return resource switch
            {
                Resource.Lily when this.Job == Job.WHM => this.gauges.Get<WHMGauge>().Lily >= amount,
                Resource.BloodLily when this.Job == Job.WHM => this.gauges.Get<WHMGauge>().BloodLily >= amount,
                Resource.Aetherflow when this.Job == Job.SCH => this.gauges.Get<SCHGauge>().Aetherflow >= amount,
                Resource.Addersgall when this.Job == Job.SGE => this.gauges.Get<SGEGauge>().Addersgall >= amount,
                Resource.Addersting when this.Job == Job.SGE => this.gauges.Get<SGEGauge>().Addersting >= amount,
                _ => null,
            };
        }
        catch (Exception ex)
        {
            this.errors.Report("cooldowns.gauge", "Reading job gauges", ex);
            return null;
        }
    }

    /// <summary>Name of the resource an action needs (for "no Lily"), or null.</summary>
    public string? ResourceName(uint actionId) => this.data.GetAction(actionId)?.Cost?.Resource;

    /// <summary>
    /// Usage fallback: reports a use when a tracked action's recast starts (inactive → active), its elapsed
    /// time resets, or its charges drop. GCD actions are skipped (their recast is the shared GCD).
    /// </summary>
    /// <param name="now">Current monotonic clock.</param>
    /// <param name="tracked">Action ids of the active reminders.</param>
    /// <param name="output">Receives detected uses.</param>
    public void DetectUses(double now, HashSet<uint> tracked, List<OwnUse> output)
    {
        this.frame++;
        if (!this.Available || tracked.Count == 0)
        {
            if (this.tracks.Count > 0)
                this.tracks.Clear();
            return;
        }

        var am = Manager();
        if (am == null)
            return;

        try
        {
            foreach (var id in tracked)
            {
                if (id == 0 || this.gameData.IsGcd(id))
                    continue;

                var active = am->IsRecastTimerActive(ActionType.Action, id);
                var elapsed = active ? am->GetRecastTimeElapsed(ActionType.Action, id) : 0f;
                var charges = am->GetCurrentCharges(id);

                ref var track = ref CollectionsMarshal.GetValueRefOrAddDefault(this.tracks, id, out var exists);
                if (exists && track.LastFrame == this.frame - 1)
                {
                    var used = (active && !track.Active)
                               || (active && track.Active && elapsed + 0.25f < track.Elapsed)
                               || charges < track.Charges;
                    if (used)
                        output.Add(new OwnUse(id, now, OwnUseSource.Recast));
                }

                track.Active = active;
                track.Elapsed = elapsed;
                track.Charges = charges;
                track.LastFrame = this.frame;
            }
        }
        catch (Exception ex)
        {
            this.errors.Report("cooldowns.fallback", "Watching cooldowns for action uses", ex);
        }

        if (this.tracks.Count > tracked.Count)
        {
            this.trackRemovals.Clear();
            foreach (var (id, track) in this.tracks)
            {
                if (track.LastFrame != this.frame)
                    this.trackRemovals.Add(id);
            }

            foreach (var id in this.trackRemovals)
                this.tracks.Remove(id);
        }
    }

    private static ActionManager* Manager() =>
        ActionManager.StaticAddressPointers.pInstance != null ? ActionManager.Instance() : null;

    /// <summary>Seconds until the action (or its next charge) is ready, from the game's adjusted recast.</summary>
    private static double Remaining(ActionManager* am, uint id)
    {
        var maxCharges = ActionManager.GetMaxCharges(id, 0); // 0 = current (synced) level
        if (maxCharges > 1)
        {
            if (am->GetCurrentCharges(id) >= 1)
                return 0;
            var total = am->GetRecastTime(ActionType.Action, id);
            if (total <= 0)
                return 0;
            var perCharge = total / maxCharges;
            var elapsed = am->GetRecastTimeElapsed(ActionType.Action, id);
            return Math.Max(0, perCharge - (elapsed % perCharge));
        }

        if (!am->IsRecastTimerActive(ActionType.Action, id))
            return 0;
        return Math.Max(0, am->GetRecastTime(ActionType.Action, id) - am->GetRecastTimeElapsed(ActionType.Action, id));
    }

    private Resource ResourceOf(uint actionId, out int amount)
    {
        amount = 0;
        var info = this.data.GetAction(actionId);
        var cost = info?.Cost;
        if (cost == null || cost.Amount <= 0)
            return info == null ? Resource.Unknown : Resource.None;

        amount = cost.Amount;
        if (this.resources.TryGetValue(actionId, out var cached))
            return cached;

        var key = new string(cost.Resource.Where(char.IsLetter).ToArray()).ToLowerInvariant();
        var resource = key switch
        {
            "lily" or "lilies" or "healinglily" => Resource.Lily,
            "bloodlily" or "bloodlilies" => Resource.BloodLily,
            "aetherflow" => Resource.Aetherflow,
            "addersgall" => Resource.Addersgall,
            "addersting" => Resource.Addersting,
            _ => Resource.Unknown,
        };
        this.resources[actionId] = resource;
        return resource;
    }

    private struct RecastTrack
    {
        public bool Active;
        public float Elapsed;
        public uint Charges;
        public int LastFrame;
    }
}
