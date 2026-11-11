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

    /// <summary>Party slots the user can pick as "my slot".</summary>
    public static readonly IReadOnlyList<string> SlotChoices = ["MT", "OT", "H1", "H2", "M1", "M2", "R1", "R2"];

    /// <inheritdoc/>
    public int Version { get; set; } = 1;

    // ---- Timing -------------------------------------------------------------------------

    /// <summary>Seconds before the press time a reminder appears.</summary>
    public float Lookahead { get; set; } = 15f;

    /// <summary>Seconds before the press time a reminder becomes due (NOW).</summary>
    public float DueLead { get; set; } = 1.5f;

    // ---- Sound / behaviour --------------------------------------------------------------

    /// <summary>Play a game sound when a reminder becomes due.</summary>
    public bool SoundEnabled { get; set; } = true;

    /// <summary>Chat sound effect number &lt;se.N&gt;, 1..16.</summary>
    public int SoundEffect { get; set; } = 6;

    /// <summary>Also show the calls of the plan's other columns (e.g. the co-healer), dimmed.</summary>
    public bool ShowOtherAssignees { get; set; }

    /// <summary>Keep reminders skipped by a phase push visible for a moment.</summary>
    public bool ShowSkipped { get; set; }

    /// <summary>Print a done/missed summary to chat after each pull.</summary>
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

    /// <summary>Per-plan choice of "my column" (assignee key) and slot, keyed by plan id.</summary>
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
        this.Lookahead = Math.Clamp(float.IsFinite(this.Lookahead) ? this.Lookahead : 15f, 3f, 60f);
        this.DueLead = Math.Clamp(float.IsFinite(this.DueLead) ? this.DueLead : 1.5f, 0f, 10f);
        this.SoundEffect = Math.Clamp(this.SoundEffect, MinSoundEffect, MaxSoundEffect);
        this.MaxRows = Math.Clamp(this.MaxRows, 1, 15);
        this.OverlayScale = Math.Clamp(float.IsFinite(this.OverlayScale) ? this.OverlayScale : 1f, 0.5f, 3f);
        this.OverlayOpacity = Math.Clamp(float.IsFinite(this.OverlayOpacity) ? this.OverlayOpacity : 0.85f, 0f, 1f);
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

    /// <summary>Builds the core scheduler options from these settings.</summary>
    public ReminderOptions BuildReminderOptions()
    {
        var options = new ReminderOptions();
        this.ApplyTo(options);
        return options;
    }

    /// <summary>Copies the timing settings into an existing <see cref="ReminderOptions"/> (used for live edits).</summary>
    public void ApplyTo(ReminderOptions options)
    {
        options.Lookahead = this.Lookahead;
        options.DueLead = this.DueLead;
        options.ShowSkipped = this.ShowSkipped;
    }
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
