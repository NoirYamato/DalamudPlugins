using Dalamud.Plugin.Services;
using MitCue.Core;
using MitCue.Core.Fights;
using MitCue.Core.Plans;
using MitCue.Core.Reminders;
using MitCue.Core.Timelines;
using MitCue.Data;
using MitCue.Game;
using MitCue.Plans;
using MitCue.Util;

namespace MitCue.Runtime;

/// <summary>
/// Owns the live <see cref="EncounterSession"/>: builds it when the current zone has a fight definition and
/// the player has an active plan for (fight, job), rebuilds it on zone, job, plan or settings changes
/// (settings/plan changes wait until the current pull is over), feeds it each frame, prints the pull
/// summary and requests the "due" sound. Framework thread only.
/// </summary>
internal sealed class SessionManager
{
    private readonly Configuration config;
    private readonly CoreData data;
    private readonly GameData gameData;
    private readonly PlanStore plans;
    private readonly CooldownProvider cooldowns;
    private readonly ErrorReporter errors;
    private readonly IChatGui chat;
    private readonly Action requestSound;
    private ContextKey lastContext;
    private bool invalidated = true;
    private string? shapeKey;
    private bool faulted;

    /// <summary>Creates the manager.</summary>
    public SessionManager(
        Configuration config,
        CoreData data,
        GameData gameData,
        PlanStore plans,
        CooldownProvider cooldowns,
        ErrorReporter errors,
        IChatGui chat,
        Action requestSound)
    {
        this.config = config;
        this.data = data;
        this.gameData = gameData;
        this.plans = plans;
        this.cooldowns = cooldowns;
        this.errors = errors;
        this.chat = chat;
        this.requestSound = requestSound;
    }

    /// <summary>The live session (null when there is none); may be faulted.</summary>
    public EncounterSession? Session { get; private set; }

    /// <summary>The live session when it is running normally, else null.</summary>
    public EncounterSession? ActiveSession => this.faulted ? null : this.Session;

    /// <summary>True when the session stopped after an exception (until rebuilt or reset).</summary>
    public bool Faulted => this.faulted;

    /// <summary>Why there is (or is not) a session, for the UI.</summary>
    public string Status { get; private set; } = "Waiting for the game...";

    /// <summary>The current zone's fight, if any.</summary>
    public FightDefinition? Fight { get; private set; }

    /// <summary>The plan the session uses.</summary>
    public StoredPlan? Plan { get; private set; }

    /// <summary>True when <see cref="Plan"/> was chosen automatically.</summary>
    public bool PlanIsAutomatic { get; private set; }

    /// <summary>The column mapping the session uses.</summary>
    public PlayerMapping? Mapping { get; private set; }

    /// <summary>A rebuild is waiting for the current pull to end.</summary>
    public bool RebuildPending { get; private set; }

    /// <summary>Incremented whenever <see cref="Session"/> is replaced.</summary>
    public int Generation { get; private set; }

    /// <summary>Forces a rebuild on the next frame.</summary>
    public void Invalidate() => this.invalidated = true;

    /// <summary>Ends the current pull without a summary and rebuilds (<c>/mitcue reset</c>).</summary>
    public void Reset()
    {
        try
        {
            this.Session?.ManualReset();
        }
        catch (Exception ex)
        {
            this.errors.Report("session.reset", "Resetting the session", ex);
        }

        this.faulted = false;
        this.shapeKey = null;
        this.invalidated = true;
    }

    /// <summary>Per-frame update: feed events, rebuild when needed, advance reminders.</summary>
    public void Update(FrameContext ctx)
    {
        // 0. Fight data and plans load on first use (framework thread); both calls return at once when loaded, and
        //    load again after Debug > Reload data.
        this.data.EnsureLoaded();
        this.plans.EnsureLoaded();

        // 1. Feed the existing session first, so wipes and zone changes end its pull before any rebuild.
        var session = this.Session;
        if (session != null && !this.faulted)
        {
            try
            {
                var events = ctx.Events;
                for (var i = 0; i < events.Count; i++)
                    session.Process(events[i]);
                foreach (var use in ctx.OwnUses)
                    session.OnOwnUse(use.ActionId, use.Clock);
                if (session.Others != null)
                {
                    var party = ctx.PartyUses;
                    for (var i = 0; i < party.Count; i++)
                        session.OnPartyUse(party[i].ActionId, party[i].Clock);
                }
            }
            catch (Exception ex)
            {
                this.Fault("session.events", "Feeding game events to the reminder session", ex);
            }
        }

        // 2. Rebuild when the context changed.
        var key = new ContextKey(
            ctx.LoggedIn,
            ctx.Territory,
            ctx.Job,
            ctx.Level,
            this.plans.Revision,
            this.config.Revision,
            this.data.Revision,
            ctx.ClientIsEnglish,
            this.config.ShowOtherAssignees ? ctx.PartyJobMask : 0);
        if (this.invalidated || key != this.lastContext)
        {
            var hardChange = this.invalidated
                             || key.LoggedIn != this.lastContext.LoggedIn
                             || key.Territory != this.lastContext.Territory
                             || key.Job != this.lastContext.Job;
            this.lastContext = key;
            this.invalidated = false;

            // Timing edits apply immediately, even mid-pull.
            if (this.Session != null)
                this.config.ApplyTo(this.Session.Options);

            var shape = this.ComputeShape(ctx);
            if (shape.Key != this.shapeKey)
            {
                if (this.Session is { IsBusy: true } && !this.faulted && !hardChange)
                    this.RebuildPending = true;
                else
                    this.Rebuild(shape, ctx);
            }
            else
            {
                this.RebuildPending = false;
                this.Status = shape.Status ?? this.Status;
            }
        }
        else if (this.RebuildPending && this.Session is not { IsBusy: true })
        {
            this.Rebuild(this.ComputeShape(ctx), ctx);
        }

        // 3. Advance.
        session = this.Session;
        if (session != null && !this.faulted)
        {
            try
            {
                session.Update(ctx.Now, ctx.Countdown, ctx.InCombat, this.cooldowns);
            }
            catch (Exception ex)
            {
                this.Fault("session.update", "Updating reminders", ex);
            }
        }
    }

    private void Fault(string key, string context, Exception ex)
    {
        this.errors.Report(key, context, ex);
        this.faulted = true;
        this.Status = "Stopped after an error: " + ErrorReporter.Describe(ex) + " Use /mitcue reset to retry.";

        // The overlay goes away with the session: tell the player once instead of silently dropping the calls.
        try
        {
            this.chat.PrintError("MitCue reminders stopped after an error (" + ErrorReporter.Describe(ex) + "). Type /mitcue reset to restart them.", "MitCue");
        }
        catch (Exception chatEx)
        {
            this.errors.Report("session.faultchat", "Reporting a stopped session in chat", chatEx);
        }
    }

    private Shape ComputeShape(FrameContext ctx)
    {
        if (!ctx.LoggedIn)
            return new Shape("none:logout", "Not logged in.");

        var fight = this.data.FightForTerritory(ctx.Territory);
        if (fight == null)
        {
            // Loading was attempted at the start of Update, so a missing library means it failed (and was reported).
            var reason = this.data.Fights == null
                ? "Fight data did not load (see Problems in /mitcue)."
                : this.data.FightProblems.Count > 0
                    ? $"No supported fight in this zone ({this.gameData.TerritoryName(ctx.Territory)}); some fight files did not load (see Problems in /mitcue)."
                    : $"No supported fight in this zone ({this.gameData.TerritoryName(ctx.Territory)}).";
            return new Shape("none:" + ctx.Territory, reason);
        }

        if (ctx.Job == Job.None)
            return new Shape($"none:{fight.Key}:nojob", $"{fight.Name}: switch to a combat job.") { Fight = fight };

        var plan = this.plans.ActivePlanFor(fight.Key, ctx.Job, out var automatic);
        if (plan == null)
        {
            return new Shape(
                $"none:{fight.Key}:{ctx.Job}:noplan:{this.plans.Revision}",
                $"{fight.Name}: no plan for {ctx.Job}. Choose one in /mitcue > Plans.")
            { Fight = fight };
        }

        var mapping = this.plans.MappingFor(plan.Plan, ctx.Job);
        var slot = mapping.Slot ?? this.config.DefaultSlot;
        var level = ctx.Level > 0 ? Math.Min(ctx.Level, fight.SyncLevel) : fight.SyncLevel;

        // The party's jobs choose the co-healer's column, so a party change rebuilds (after the pull) only while they are shown.
        var party = this.config.ShowOtherAssignees ? ctx.PartyJobMask : 0;
        var key = string.Join(
            '|',
            fight.Key,
            ctx.Job,
            level,
            plan.Id,
            this.plans.Revision,
            mapping.AssigneeKey ?? "*",
            slot ?? "*",
            this.config.ShowOtherAssignees,
            party,
            ctx.ClientIsEnglish);
        return new Shape(key, null)
        {
            Fight = fight,
            Plan = plan,
            Automatic = automatic,
            Mapping = mapping,
            Slot = slot,
            Level = level,
            PartyJobMask = party,
        };
    }

    private void Rebuild(Shape shape, FrameContext ctx)
    {
        this.DropSession();
        this.shapeKey = shape.Key;
        this.Fight = shape.Fight;
        this.Plan = shape.Plan;
        this.PlanIsAutomatic = shape.Automatic;
        this.Mapping = shape.Mapping;
        this.cooldowns.ResetTracking();
        this.errors.ClearPrefix("session.");

        if (shape.Fight is not { } fight || shape.Plan is not { } stored || shape.Mapping is not { } mapping)
        {
            this.Status = shape.Status ?? "Idle.";
            return;
        }

        var timeline = this.data.TryLoadTimeline(fight);
        if (timeline == null)
        {
            this.Status = $"{fight.Name}: the timeline did not load (see Problems).";
            return;
        }

        try
        {
            var plan = stored.Plan;
            var me = new PlayerContext(ctx.Job, shape.Level, shape.Slot, mapping.AssigneeKey);
            var own = PlanSelection.For(plan, me).ToList();
            var others = this.config.ShowOtherAssignees ? PartnerSelection.For(plan, me, PartyJobs.Of(shape.PartyJobMask)) : null;

            var session = new EncounterSession(new SessionSetup
            {
                Fight = fight,
                Timeline = timeline,
                Plan = plan,
                Me = me,
                OwnEntries = own,
                OtherEntries = others,
                Actions = this.data.Actions,
                Options = this.config.BuildReminderOptions(),
                SyncOptions = new SyncMatchOptions { MatchNames = ctx.ClientIsEnglish },
            });
            session.PullEnded += this.OnPullEnded;
            session.BecameDue += this.OnBecameDue;
            this.Session = session;
            this.Generation++;

            // Entries whose mechanic the fight does not have are dropped by the scheduler: say so (details in Debug).
            var dropped = session.Scheduler.Problems.Count;
            var droppedNote = dropped > 0
                ? $" {dropped} of them could not be scheduled (unknown mechanic, see the Debug tab)."
                : string.Empty;
            this.Status = own.Count > 0
                ? $"Ready: {stored.DisplayName} ({own.Count} of {plan.Entries.Count} entries are yours).{droppedNote}"
                : $"{stored.DisplayName}: none of its {plan.Entries.Count} entries match {ctx.Job}" +
                  (mapping.AssigneeKey != null ? $" / column \"{mapping.AssigneeKey}\"" : string.Empty) +
                  ". Check \"My column\" in /mitcue > Plans.";
        }
        catch (Exception ex)
        {
            this.errors.Report("session.build", "Starting the reminder session", ex);
            this.Status = "Could not start reminders: " + ErrorReporter.Describe(ex);
        }
    }

    private void DropSession()
    {
        if (this.Session != null)
        {
            this.Session.PullEnded -= this.OnPullEnded;
            this.Session.BecameDue -= this.OnBecameDue;
            this.Session = null;
            this.Generation++;
        }

        this.faulted = false;
        this.RebuildPending = false;
    }

    private void OnPullEnded(EncounterSession session, PullSummary summary, string reason)
    {
        try
        {
            if (!this.config.TrackDoneMissed || !this.config.PostPullSummary || summary.Done + summary.Missed + summary.Skipped == 0)
                return;
            this.chat.Print(ReminderText.Summary(session, summary, reason, this.gameData), "MitCue");
        }
        catch (Exception ex)
        {
            this.errors.Report("session.summary", "Printing the pull summary", ex);
        }
    }

    private void OnBecameDue(EncounterSession session, Reminder reminder) => this.requestSound();

    private readonly record struct ContextKey(
        bool LoggedIn,
        uint Territory,
        Job Job,
        int Level,
        int PlanRevision,
        int ConfigRevision,
        int DataRevision,
        bool English,
        ulong PartyJobs);

    private sealed class Shape(string key, string? status)
    {
        public string Key { get; } = key;

        public string? Status { get; } = status;

        public FightDefinition? Fight { get; init; }

        public StoredPlan? Plan { get; init; }

        public bool Automatic { get; init; }

        public PlayerMapping? Mapping { get; init; }

        public string? Slot { get; init; }

        public int Level { get; init; }

        public ulong PartyJobMask { get; init; }
    }
}
