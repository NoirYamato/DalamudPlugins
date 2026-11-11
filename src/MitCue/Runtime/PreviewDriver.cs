using Dalamud.Plugin.Services;
using MitCue.Core;
using MitCue.Core.Events;
using MitCue.Core.Fights;
using MitCue.Core.Plans;
using MitCue.Core.Reminders;
using MitCue.Core.Simulation;
using MitCue.Core.Timelines;
using MitCue.Data;
using MitCue.Game;
using MitCue.Plans;
using MitCue.Util;

namespace MitCue.Runtime;

/// <summary>
/// <c>/mitcue preview [fight] [phase]</c>: simulates a pull with <see cref="FightSimulator"/> and plays it
/// through a separate <see cref="EncounterSession"/> (own timeline player and scheduler) at 1× or 4×.
/// Starting at a phase drops the earlier phases' events and lets the phase's start sync jump the timeline,
/// so earlier phases are skipped quickly. Only the event kinds the plugin sees in game are played
/// (<see cref="GameVisibleEvents"/>), so the preview follows the fight exactly as a live pull would. Simulated events
/// never reach the live session; the player's real action uses are forwarded so usage tracking can be tried on a
/// striking dummy. Framework thread only.
/// </summary>
internal sealed class PreviewDriver
{
    /// <summary>Simulated clock value of the pull.</summary>
    private const double PullClock = 1000;

    /// <summary>Simulated countdown before a full-fight preview (shows pre-pull reminders).</summary>
    private const double CountdownLeadIn = 10;

    /// <summary>Seconds after the pull at which the chosen phase starts.</summary>
    private const double PhaseLeadIn = 2;

    private readonly Configuration config;
    private readonly CoreData data;
    private readonly GameData gameData;
    private readonly PlanStore plans;
    private readonly ErrorReporter errors;
    private readonly IChatGui chat;
    private readonly Action requestSound;
    private List<GameEvent> events = [];
    private int nextEvent;
    private double simStart;
    private double realStart;
    private double lastEventTime;
    private bool simInCombat;
    private bool withCountdown;
    private int optionsRevision;

    /// <summary>Creates the driver.</summary>
    public PreviewDriver(
        Configuration config,
        CoreData data,
        GameData gameData,
        PlanStore plans,
        ErrorReporter errors,
        IChatGui chat,
        Action requestSound)
    {
        this.config = config;
        this.data = data;
        this.gameData = gameData;
        this.plans = plans;
        this.errors = errors;
        this.chat = chat;
        this.requestSound = requestSound;
    }

    /// <summary>The preview session while a preview runs.</summary>
    public EncounterSession? Session { get; private set; }

    /// <summary>True while a preview runs.</summary>
    public bool IsActive => this.Session != null;

    /// <summary>Speed factor (1 or 4).</summary>
    public float Speed { get; private set; } = 1;

    /// <summary>Simulated clock.</summary>
    public double SimNow { get; private set; }

    /// <summary>What is being previewed.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>Number of simulated events.</summary>
    public int EventCount => this.events.Count;

    /// <summary>Number of simulated events already played.</summary>
    public int EventsPlayed => this.nextEvent;

    /// <summary>Incremented whenever <see cref="Session"/> is replaced.</summary>
    public int Generation { get; private set; }

    /// <summary>Starts a preview (replacing a running one).</summary>
    /// <param name="fightKey">Fight key, or null for the current zone's fight (else "top").</param>
    /// <param name="phaseId">Phase to start at, or null for the whole fight.</param>
    /// <param name="territory">Current territory.</param>
    /// <param name="currentJob">Current job.</param>
    /// <param name="realNow">Current monotonic clock.</param>
    /// <param name="message">Result for the chat.</param>
    /// <returns>True when the preview started.</returns>
    public bool Start(string? fightKey, string? phaseId, uint territory, Job currentJob, double realNow, out string message)
    {
        this.Stop();
        this.data.EnsureLoaded();
        this.plans.EnsureLoaded();

        if (this.data.Fights == null)
        {
            message = "Fight data did not load (see Problems in /mitcue).";
            return false;
        }

        var fight = fightKey != null
            ? this.data.GetFight(fightKey)
            : this.data.FightForTerritory(territory) ?? this.data.GetFight("top");
        if (fight == null)
        {
            var available = string.Join(", ", this.data.FightList.Select(f => f.Key));
            var unloaded = this.data.FightProblems.Count > 0 ? " Some fight files did not load (see Problems in /mitcue)." : string.Empty;
            message = fightKey != null
                ? $"Unknown fight \"{fightKey}\". Available: {(available.Length > 0 ? available : "none")}.{unloaded}"
                : $"No fight to preview. Available: {(available.Length > 0 ? available : "none")}.{unloaded}";
            return false;
        }

        Phase? phase = null;
        if (!string.IsNullOrWhiteSpace(phaseId))
        {
            phase = fight.GetPhase(phaseId);
            if (phase == null)
            {
                message = $"Unknown phase \"{phaseId}\" for {fight.Key}. Phases: {string.Join(", ", fight.Phases.Select(p => p.Id))}.";
                return false;
            }
        }

        var timeline = this.data.TryLoadTimeline(fight);
        if (timeline == null)
        {
            message = $"The {fight.Key.ToUpperInvariant()} timeline did not load (see Problems in /mitcue).";
            return false;
        }

        var (stored, job) = this.ChoosePlan(fight, currentJob);
        if (stored == null)
        {
            message = $"No plan for {fight.Name}. Import one in /mitcue > Plans.";
            return false;
        }

        try
        {
            var plan = stored.Plan;
            var mapping = this.plans.MappingFor(plan, job);
            var me = new PlayerContext(job, fight.SyncLevel, mapping.Slot ?? this.config.DefaultSlot, mapping.AssigneeKey);
            var own = PlanSelection.For(plan, me).ToList();
            var others = this.config.ShowOtherAssignees ? PartnerSelection.For(plan, me) : null;

            var options = new SimulationOptions
            {
                PullClock = PullClock,
                Seed = Environment.TickCount,
            };
            if (phase != null)
                ConfigurePhaseStart(options, fight, phase);

            // Only the event kinds the live session gets in game, so the preview syncs (and follows phases) the way a pull does.
            var generated = FightSimulator.Generate(timeline, fight, options);
            var sorted = generated.Where(GameVisibleEvents.Contains).OrderBy(e => e.Time).ToList(); // stable

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
                SyncOptions = new SyncMatchOptions { MatchNames = true }, // simulated names come from the timeline
                IsPreview = true,
            });
            session.PullEnded += this.OnPullEnded;
            session.BecameDue += this.OnBecameDue;

            // Starting at a phase: the earlier phases' reminders were never shown, so they are skipped, not missed.
            if (phase != null)
                session.SkipBefore(phase.Start);

            this.events = sorted;
            this.nextEvent = 0;
            this.lastEventTime = sorted.Count > 0 ? sorted[^1].Time : PullClock;
            this.withCountdown = phase == null;
            this.simStart = PullClock - (this.withCountdown ? CountdownLeadIn : PhaseLeadIn);
            this.realStart = realNow;
            this.SimNow = this.simStart;
            this.simInCombat = false;
            this.Speed = this.config.PreviewFast ? 4 : 1;
            this.optionsRevision = this.config.Revision;
            this.Session = session;
            this.Generation++;

            var column = mapping.AssigneeKey != null ? $", column \"{mapping.AssigneeKey}\"" : string.Empty;
            this.Description = $"{fight.Key.ToUpperInvariant()}{(phase != null ? " from " + phase.Name : string.Empty)}, " +
                               $"\"{stored.DisplayName}\" as {job}{column}, {this.Speed:0}x";
            message = $"Preview started: {this.Description}; {own.Count} entries are yours. Type /mitcue preview again to stop.";
            if (own.Count == 0)
                message += " (No entries match: check \"My column\" in /mitcue > Plans.)";
            return true;
        }
        catch (Exception ex)
        {
            this.errors.Report("preview.start", "Starting the preview", ex);
            this.Stop();
            message = "Preview failed: " + ErrorReporter.Describe(ex);
            return false;
        }
    }

    /// <summary>Stops the preview (no summary).</summary>
    public void Stop()
    {
        if (this.Session != null)
        {
            this.Session.PullEnded -= this.OnPullEnded;
            this.Session.BecameDue -= this.OnBecameDue;
            this.Session = null;
            this.Generation++;
        }

        this.events = [];
        this.nextEvent = 0;
        this.Description = string.Empty;
    }

    /// <summary>Plays simulated events up to the simulated clock and advances the preview session.</summary>
    /// <param name="realNow">Current monotonic clock.</param>
    /// <param name="ownUses">The player's real action uses this frame.</param>
    public void Update(double realNow, List<OwnUse> ownUses)
    {
        var session = this.Session;
        if (session == null)
            return;

        try
        {
            // Timing edits apply immediately, as in the live session.
            if (this.config.Revision != this.optionsRevision)
            {
                this.optionsRevision = this.config.Revision;
                this.config.ApplyTo(session.Options);
            }

            this.SimNow = this.simStart + ((realNow - this.realStart) * this.Speed);
            while (this.nextEvent < this.events.Count && this.events[this.nextEvent].Time <= this.SimNow)
            {
                var e = this.events[this.nextEvent++];
                if (e is CombatStateChanged combat)
                    this.simInCombat = combat.InCombat;
                session.Process(e);
                if (this.Session == null)
                    return;
            }

            foreach (var use in ownUses)
                session.OnOwnUse(use.ActionId, this.SimNow);

            float? countdown = this.withCountdown && this.SimNow < PullClock ? (float)(PullClock - this.SimNow) : null;
            session.Update(this.SimNow, countdown, this.simInCombat, null);

            var finished = this.nextEvent >= this.events.Count
                           && this.SimNow > this.lastEventTime + 5
                           && (!session.Player.IsRunning || this.SimNow > this.lastEventTime + 60);
            if (finished)
            {
                if (session.PullActive)
                    session.EndPull("preview finished");
                this.chat.Print("Preview finished.", "MitCue");
                this.Stop();
            }
        }
        catch (Exception ex)
        {
            this.errors.Report("preview.update", "Running the preview", ex);
            this.chat.PrintError("MitCue preview stopped: " + ErrorReporter.Describe(ex), "MitCue");
            this.Stop();
        }
    }

    /// <summary>
    /// Skips to <paramref name="phase"/>: earlier phases keep only their very first events (combat start) and the
    /// chosen phase starts <see cref="PhaseLeadIn"/> s after the pull. Later phases are not listed, so they follow
    /// the chosen one as the timeline plays, through its jumps: a start computed from the phases' timeline
    /// spacing would come long after a jump (DMU's P2 → P3 skips 113.8 s) and the pull could not be followed.
    /// </summary>
    private static void ConfigurePhaseStart(SimulationOptions options, FightDefinition fight, Phase phase)
    {
        var ordered = fight.Phases.OrderBy(p => p.Start).ToList();
        var index = ordered.FindIndex(p => ReferenceEquals(p, phase));
        if (index < 0)
            return;

        for (var i = 0; i < index; i++)
        {
            var p = ordered[i];
            options.PhaseCutoff[p.Id] = i == 0 ? p.Start + 0.5 : p.Start - 0.001;
        }

        options.PhaseRealStart[phase.Id] = PhaseLeadIn;
    }

    private (StoredPlan? Plan, Job Job) ChoosePlan(FightDefinition fight, Job currentJob)
    {
        if (currentJob != Job.None && this.plans.ActivePlanFor(fight.Key, currentJob) is { } active)
            return (active, currentJob);

        var first = this.plans.ForFight(fight.Key).FirstOrDefault();
        if (first == null)
            return (null, currentJob);

        // Not playing a job of this plan: preview it as a job whose column the player picked, else its first job.
        var jobs = PlanStore.JobsOf(first.Plan).ToList();
        var job = jobs.FirstOrDefault(j => this.plans.SavedMapping(first.Plan, j)?.AssigneeKey != null);
        if (job == Job.None)
            job = jobs.FirstOrDefault();
        if (job == Job.None)
            job = currentJob;
        return (first, job);
    }

    private void OnPullEnded(EncounterSession session, PullSummary summary, string reason)
    {
        try
        {
            if (this.config.TrackDoneMissed && this.config.PostPullSummary && summary.Done + summary.Missed + summary.Skipped > 0)
                this.chat.Print(ReminderText.Summary(session, summary, reason, this.gameData), "MitCue");
        }
        catch (Exception ex)
        {
            this.errors.Report("preview.summary", "Printing the preview summary", ex);
        }
    }

    private void OnBecameDue(EncounterSession session, Reminder reminder) => this.requestSound();
}
