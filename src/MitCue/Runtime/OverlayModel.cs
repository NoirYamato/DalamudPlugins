using MitCue.Core.Reminders;
using MitCue.Data;

namespace MitCue.Runtime;

/// <summary>What the overlay is showing.</summary>
internal enum OverlaySource
{
    /// <summary>Nothing (overlay hidden).</summary>
    None,

    /// <summary>The live session.</summary>
    Live,

    /// <summary>A <c>/mitcue preview</c> simulation.</summary>
    Preview,

    /// <summary>Fake rows for positioning ("Always show overlay").</summary>
    Sample,

    /// <summary>The live session stopped after an error: one line saying so (<see cref="OverlayModel.Message"/>).</summary>
    Error,
}

/// <summary>One overlay row (value type; the list is reused every frame).</summary>
internal struct OverlayRow
{
    /// <summary>Cached display strings.</summary>
    public ReminderLabels Labels;

    /// <summary>Reminder state.</summary>
    public ReminderState State;

    /// <summary>Seconds until the planned press time (negative when past).</summary>
    public double Seconds;

    /// <summary>Seconds the action will be late because of its cooldown, or null.</summary>
    public double? ReadyIn;

    /// <summary>A required job resource is missing.</summary>
    public bool MissingResources;

    /// <summary>Another assignee's reminder (dimmed).</summary>
    public bool IsOther;

    /// <summary>Used before the useful window.</summary>
    public bool UsedEarly;
}

/// <summary>
/// The overlay's per-frame snapshot, built in <c>IFramework.Update</c> from the preview or the live session and
/// rendered by <see cref="UI.OverlayWindow"/> in Draw. Reuses its row list; strings are cached per reminder.
/// </summary>
internal sealed class OverlayModel
{
    private readonly ReminderLabelCache labels = new();
    private readonly List<OverlayRow> sample = [];
    private EncounterSession? headerSession;

    /// <summary>What is shown.</summary>
    public OverlaySource Source { get; private set; }

    /// <summary>True when the overlay should be drawn.</summary>
    public bool Visible { get; private set; }

    /// <summary>Rows ordered by press time (at most MaxRows).</summary>
    public List<OverlayRow> Rows { get; } = new(16);

    /// <summary>Fight short name ("TOP").</summary>
    public string FightShort { get; private set; } = string.Empty;

    /// <summary>Plan name.</summary>
    public string PlanName { get; private set; } = string.Empty;

    /// <summary>Current phase name, or null.</summary>
    public string? PhaseName { get; private set; }

    /// <summary>Session mode.</summary>
    public SessionMode Mode { get; private set; }

    /// <summary>Timeline time.</summary>
    public double TimelineTime { get; private set; }

    /// <summary>Remaining countdown seconds (Countdown mode).</summary>
    public float Countdown { get; private set; }

    /// <summary>Preview speed factor (Preview source).</summary>
    public float PreviewSpeed { get; private set; }

    /// <summary>What to tell the player instead of reminders (Error source), else null.</summary>
    public string? Message { get; private set; }

    /// <summary>Rebuilds the snapshot. Framework thread.</summary>
    /// <param name="config">Settings.</param>
    /// <param name="live">The live session when it runs normally, else null.</param>
    /// <param name="preview">The preview session, or null.</param>
    /// <param name="previewSpeed">Preview speed factor.</param>
    /// <param name="gameData">Names and icons.</param>
    /// <param name="resourceOf">Resource an action needs ("Lily").</param>
    /// <param name="liveError">Set when the live session stopped after an error: shown instead of vanishing mid-pull.</param>
    public void Build(
        Configuration config,
        EncounterSession? live,
        EncounterSession? preview,
        float previewSpeed,
        GameData gameData,
        Func<uint, string?> resourceOf,
        string? liveError = null)
    {
        this.Rows.Clear();
        this.PreviewSpeed = previewSpeed;
        this.Message = null;

        // Idle with reminders still listed is the hold between the countdown reaching 0 and the pull: those pre-pull
        // calls are real and must not give way to the sample rows.
        var liveActive = live != null
                         && (live.Mode != SessionMode.Idle || live.Visible.Count > 0 || live.VisibleOthers.Count > 0);
        if (preview != null)
        {
            this.Source = OverlaySource.Preview;
            this.Fill(config, preview, gameData, resourceOf);
            this.Visible = true;
        }
        else if (live != null && (liveActive || !config.AlwaysShowOverlay))
        {
            this.Source = OverlaySource.Live;
            this.Fill(config, live, gameData, resourceOf);
            this.Visible = this.Rows.Count > 0 || config.ShowHeader;
        }
        else if (liveError != null)
        {
            this.Source = OverlaySource.Error;
            this.Message = liveError;
            this.Mode = SessionMode.Idle;
            this.headerSession = null;
            this.Visible = true;
        }
        else if (config.AlwaysShowOverlay)
        {
            this.Source = OverlaySource.Sample;
            this.FillSample(config, gameData);
            this.Visible = true;
        }
        else
        {
            this.Source = OverlaySource.None;
            this.Visible = false;
            this.headerSession = null;
        }
    }

    private void Fill(Configuration config, EncounterSession session, GameData gameData, Func<uint, string?> resourceOf)
    {
        if (!ReferenceEquals(session, this.headerSession))
        {
            this.headerSession = session;
            this.FightShort = session.Fight.Key.ToUpperInvariant();
            this.PlanName = string.IsNullOrWhiteSpace(session.Plan.Name) ? session.Plan.Id : session.Plan.Name;
        }

        this.Mode = session.Mode;
        this.TimelineTime = session.TimelineTime;
        this.Countdown = session.CountdownRemaining;
        this.PhaseName = session.Mode == SessionMode.Running ? session.PhaseAt(session.TimelineTime)?.Name : null;

        var own = session.Visible;
        var others = session.VisibleOthers;
        var track = config.TrackDoneMissed;
        var max = Math.Max(1, config.MaxRows);
        var ownLeft = Math.Min(CountShown(own, track), max);
        var otherLeft = Math.Min(CountShown(others, track), max - ownLeft); // own rows are never pushed out by others
        var i = NextShown(own, 0, track);
        var j = NextShown(others, 0, track);
        var t = session.TimelineTime;
        while (ownLeft > 0 || otherLeft > 0)
        {
            var takeOwn = otherLeft == 0 || (ownLeft > 0 && own[i].PressAt <= others[j].PressAt);
            Reminder r;
            if (takeOwn)
            {
                r = own[i];
                i = NextShown(own, i + 1, track);
                ownLeft--;
            }
            else
            {
                r = others[j];
                j = NextShown(others, j + 1, track);
                otherLeft--;
            }

            this.Rows.Add(new OverlayRow
            {
                Labels = this.labels.Get(session, r, !takeOwn, gameData, resourceOf),
                State = r.State,
                Seconds = r.PressAt - t,
                ReadyIn = r.ReadyIn,
                MissingResources = r.MissingResources,
                IsOther = !takeOwn,
                UsedEarly = r.UsedEarly,
            });
        }
    }

    /// <summary>Done and missed rows are only listed while done/missed tracking is on.</summary>
    private static bool IsShown(Reminder r, bool trackDoneMissed) =>
        trackDoneMissed || r.State is not (ReminderState.Done or ReminderState.Missed);

    private static int CountShown(IReadOnlyList<Reminder> list, bool trackDoneMissed)
    {
        if (trackDoneMissed)
            return list.Count;
        var count = 0;
        for (var k = 0; k < list.Count; k++)
        {
            if (IsShown(list[k], trackDoneMissed))
                count++;
        }

        return count;
    }

    private static int NextShown(IReadOnlyList<Reminder> list, int from, bool trackDoneMissed)
    {
        while (from < list.Count && !IsShown(list[from], trackDoneMissed))
            from++;
        return from;
    }

    private void FillSample(Configuration config, GameData gameData)
    {
        if (this.sample.Count == 0)
        {
            // Fake rows so the overlay can be positioned; names and icons come from game data.
            this.sample.Add(SampleRow(gameData, 25862, "Program Loop", null, ReminderState.Done, -3.2));
            this.sample.Add(SampleRow(gameData, 3570, "Pantokrator", "on OT", ReminderState.Missed, -1.4));
            this.sample.Add(SampleRow(gameData, 16536, "Cosmo Memory", null, ReminderState.Due, 0.6));
            var plenary = SampleRow(gameData, 7433, "Cosmo Memory", null, ReminderState.Upcoming, 4.2);
            plenary.ReadyIn = 3;
            this.sample.Add(plenary);
            this.sample.Add(SampleRow(gameData, 7432, "Wave Cannon", "on MT", ReminderState.Upcoming, 11.0));
        }

        this.FightShort = "SAMPLE";
        this.PlanName = "Sample plan";
        this.PhaseName = "drag to position";
        this.Mode = SessionMode.Running;
        this.TimelineTime = 0;
        this.Countdown = 0;
        this.headerSession = null;
        var max = Math.Max(1, config.MaxRows);
        for (var k = 0; k < this.sample.Count && k < max; k++)
            this.Rows.Add(this.sample[k]);
    }

    private static OverlayRow SampleRow(GameData gameData, uint actionId, string mechanic, string? target, ReminderState state, double seconds) => new()
    {
        Labels = new ReminderLabels
        {
            Name = gameData.ActionName(actionId) ?? $"Action #{actionId}",
            IconId = gameData.ActionIcon(actionId),
            Mechanic = mechanic,
            TargetAndNote = target,
        },
        State = state,
        Seconds = seconds,
    };
}
