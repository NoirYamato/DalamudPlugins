using Dalamud.Configuration;
using MitCue.Core.Reminders;
using Newtonsoft.Json;

namespace MitCue;

/// <summary>
/// MitCue settings (saved by Dalamud with Newtonsoft.Json under pluginConfigs/MitCue.json).
/// Defaults follow docs/DESIGN.md §7. Collections start empty (or use <see cref="ObjectCreationHandling.Replace"/>)
/// so Newtonsoft never duplicates default items on load.
/// </summary>
[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    /// <summary>Lowest and highest valid chat sound effect number (&lt;se.1&gt; .. &lt;se.16&gt;).</summary>
    public const int MinSoundEffect = 1;

    /// <inheritdoc cref="MinSoundEffect"/>
    public const int MaxSoundEffect = 16;

    /// <summary>Range of <see cref="Lookahead"/> in seconds.</summary>
    public const float MinLookahead = 3f;

    /// <inheritdoc cref="MinLookahead"/>
    public const float MaxLookahead = 60f;

    /// <summary>Upper limit of <see cref="DueLead"/> in seconds (it is also never more than <see cref="Lookahead"/>).</summary>
    public const float MaxDueLead = 10f;

    /// <summary>Range of <see cref="OverlayScale"/>.</summary>
    public const float MinOverlayScale = 0.5f;

    /// <inheritdoc cref="MinOverlayScale"/>
    public const float MaxOverlayScale = 3f;

    /// <summary>Range of <see cref="MaxRows"/>.</summary>
    public const int MinRows = 1;

    /// <inheritdoc cref="MinRows"/>
    public const int MaxRowsLimit = 15;

    /// <summary>Party slots the user can pick as "my slot".</summary>
    public static readonly IReadOnlyList<string> SlotChoices = ["MT", "OT", "H1", "H2", "M1", "M2", "R1", "R2"];

    /// <inheritdoc/>
    public int Version { get; set; } = 1;

    // ---- Timing -------------------------------------------------------------------------

    /// <summary>Seconds before the press time a reminder appears.</summary>
    public float Lookahead { get; set; } = 15f;

    /// <summary>Seconds before the press time a reminder becomes due (NOW); at most <see cref="Lookahead"/>.</summary>
    public float DueLead { get; set; } = 1.5f;

    // ---- Sound / behaviour --------------------------------------------------------------

    /// <summary>Play a game sound when a reminder becomes due.</summary>
    public bool SoundEnabled { get; set; } = true;

    /// <summary>Chat sound effect number &lt;se.N&gt;, 1..16.</summary>
    public int SoundEffect { get; set; } = 6;

    /// <summary>Also show the co-healer's calls (the plan's other columns of the player's role), dimmed.</summary>
    public bool ShowOtherAssignees { get; set; }

    /// <summary>Keep reminders skipped by a phase push visible for a moment.</summary>
    public bool ShowSkipped { get; set; }

    /// <summary>
    /// Track done/missed: done and missed reminders stay listed with a check or a cross for a moment, and the pull
    /// summary can be printed. When off, a reminder simply disappears once it is used or its time has passed.
    /// </summary>
    public bool TrackDoneMissed { get; set; } = true;

    /// <summary>Print a done/missed summary to chat after each pull (needs <see cref="TrackDoneMissed"/>).</summary>
    public bool PostPullSummary { get; set; } = true;

    /// <summary>Record the game events of each pull to ConfigDirectory/recordings (JSON lines).</summary>
    public bool RecordPulls { get; set; }

    /// <summary>Run <c>/mitcue preview</c> at 4× speed instead of 1×.</summary>
    public bool PreviewFast { get; set; }

    /// <summary>The player's usual party slot (MT/OT/H1/H2/M1/M2/R1/R2), used to suggest a plan column. Null = unknown.</summary>
    public string? DefaultSlot { get; set; }

    // ---- Overlay ------------------------------------------------------------------------

    /// <summary>Overlay shown at all (toggled by <c>/mitcue overlay</c>).</summary>
    public bool OverlayEnabled { get; set; } = true;

    /// <summary>The overlay cannot be moved.</summary>
    public bool OverlayLocked { get; set; }

    /// <summary>Mouse clicks pass through the overlay (implies locked).</summary>
    public bool OverlayClickThrough { get; set; }

    /// <summary>Overlay text and icon scale.</summary>
    public float OverlayScale { get; set; } = 1f;

    /// <summary>Overlay background opacity (0 = transparent).</summary>
    public float OverlayOpacity { get; set; } = 0.85f;

    /// <summary>Maximum reminder rows in the overlay.</summary>
    public int MaxRows { get; set; } = 5;

    /// <summary>Show action icons.</summary>
    public bool ShowIcons { get; set; } = true;

    /// <summary>Show the mechanic each reminder is for.</summary>
    public bool ShowMechanic { get; set; } = true;

    /// <summary>Show the entry's target and note.</summary>
    public bool ShowTargetAndNotes { get; set; } = true;

    /// <summary>Show "ready in N s" / "no Lily" warnings.</summary>
    public bool ShowCooldownWarnings { get; set; } = true;

    /// <summary>Show the one-line header (fight, phase, timeline clock).</summary>
    public bool ShowHeader { get; set; } = true;

    /// <summary>Keep the overlay visible (with sample rows when idle) so it can be positioned.</summary>
    public bool AlwaysShowOverlay { get; set; }

    /// <summary>True once the overlay position below has been saved.</summary>
    public bool HasOverlayPosition { get; set; }

    /// <summary>Saved overlay X position, relative to the main viewport.</summary>
    public float OverlayX { get; set; } = 100f;

    /// <summary>Saved overlay Y position, relative to the main viewport.</summary>
    public float OverlayY { get; set; } = 300f;

    // ---- Plans --------------------------------------------------------------------------

    /// <summary>
    /// Active plan per fight and job, keyed "&lt;fight&gt;/&lt;JOB&gt;" (e.g. "top/WHM"). A missing key means
    /// automatic choice; an empty value means "no plan" for that fight and job.
    /// </summary>
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public Dictionary<string, string> ActivePlans { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Choice of "my column" (assignee key) and slot per plan and job, keyed "&lt;planId&gt;/&lt;JOB&gt;" (see
    /// <c>PlanStore.MappingKey</c>). Keys without a job come from older versions and apply to every job whose column
    /// they can be.
    /// </summary>
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public Dictionary<string, PlayerMapping> PlayerMappings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Personal shorthand aliases (corrections made while reviewing imports).</summary>
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<UserAliasEntry> UserAliases { get; set; } = [];

    /// <summary>Incremented on every change (not saved); lets the runtime notice edits.</summary>
    [JsonIgnore]
    public int Revision { get; private set; }

    /// <summary>Marks the configuration as changed.</summary>
    public void Touch() => this.Revision++;

    /// <summary>Clamps values into their valid ranges and repairs null collections after loading.</summary>
    public void Sanitize()
    {
        this.ClampValues();
        if (!float.IsFinite(this.OverlayX) || !float.IsFinite(this.OverlayY))
        {
            this.OverlayX = 100f;
            this.OverlayY = 300f;
            this.HasOverlayPosition = false;
        }

        if (this.DefaultSlot != null && !SlotChoices.Contains(this.DefaultSlot))
            this.DefaultSlot = null;

        // Rebuild with a case-insensitive comparer (Newtonsoft creates plain dictionaries).
        this.ActivePlans = new Dictionary<string, string>(this.ActivePlans ?? [], StringComparer.OrdinalIgnoreCase);
        var mappings = new Dictionary<string, PlayerMapping>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in this.PlayerMappings ?? [])
        {
            if (value != null)
                mappings[key] = value;
        }

        this.PlayerMappings = mappings;
        this.UserAliases = (this.UserAliases ?? []).Where(a => a != null && !string.IsNullOrWhiteSpace(a.Token)).ToList();
    }

    /// <summary>
    /// Clamps the numeric settings into their ranges (used after loading and after every edit, since ImGui accepts any
    /// typed value). The due lead never exceeds the lookahead: a reminder must not turn NOW before uses count for it.
    /// </summary>
    public void ClampValues()
    {
        this.Lookahead = Clamp(this.Lookahead, 15f, MinLookahead, MaxLookahead);
        this.DueLead = Clamp(this.DueLead, 1.5f, 0f, Math.Min(MaxDueLead, this.Lookahead));
        this.SoundEffect = Math.Clamp(this.SoundEffect, MinSoundEffect, MaxSoundEffect);
        this.MaxRows = Math.Clamp(this.MaxRows, MinRows, MaxRowsLimit);
        this.OverlayScale = Clamp(this.OverlayScale, 1f, MinOverlayScale, MaxOverlayScale);
        this.OverlayOpacity = Clamp(this.OverlayOpacity, 0.85f, 0f, 1f);
    }

    /// <summary>Builds the core scheduler options from these settings.</summary>
    public ReminderOptions BuildReminderOptions()
    {
        var options = new ReminderOptions();
        this.ApplyTo(options);
        return options;
    }

    /// <summary>
    /// Copies the timing settings into an existing <see cref="ReminderOptions"/> (used for live edits), clamped the
    /// same way as <see cref="ClampValues"/> so the scheduler never gets a due lead longer than the lookahead.
    /// </summary>
    public void ApplyTo(ReminderOptions options)
    {
        var lookahead = Clamp(this.Lookahead, 15f, MinLookahead, MaxLookahead);
        options.Lookahead = lookahead;
        options.DueLead = Clamp(this.DueLead, 1.5f, 0f, Math.Min(MaxDueLead, lookahead));
        options.ShowSkipped = this.ShowSkipped;
    }

    private static float Clamp(float value, float fallback, float min, float max) =>
        Math.Clamp(float.IsFinite(value) ? value : fallback, min, max);
}

/// <summary>Which column of a plan is "me": the assignee key and the party slot.</summary>
[Serializable]
public sealed class PlayerMapping
{
    /// <summary>Assignee key (a source column) or null for "every entry for my job/slot".</summary>
    public string? AssigneeKey { get; set; }

    /// <summary>Party slot (MT/OT/H1/...), or null to use <see cref="Configuration.DefaultSlot"/>.</summary>
    public string? Slot { get; set; }

    /// <summary>True when chosen by the user (false = automatic suggestion, not saved).</summary>
    [JsonIgnore]
    public bool IsSuggestion { get; set; }
}

/// <summary>A personal shorthand alias, applied to the core alias dictionary as a user alias.</summary>
[Serializable]
public sealed class UserAliasEntry
{
    /// <summary>The shorthand as typed in sheets (normalized by the core when applied).</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Action names the token stands for, in order.</summary>
    public List<string> Actions { get; set; } = [];

    /// <summary>Job abbreviation the alias is limited to, or null for any job.</summary>
    public string? Job { get; set; }

    /// <summary>The actions are pressed in order.</summary>
    public bool Sequence { get; set; }

    /// <summary>Show as a note instead of resolving to actions.</summary>
    public bool NoteOnly { get; set; }

    /// <summary>Note text for note-only aliases.</summary>
    public string? Note { get; set; }
}
