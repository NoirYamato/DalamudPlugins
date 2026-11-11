namespace MitCue.Core.Plans;

/// <summary>A mitigation plan in MitCue's standard format (docs/DESIGN.md §3.2). Mutable for (de)serialization and editing.</summary>
public sealed class MitPlan
{
    public const string CurrentFormat = "mitcue-plan/1";

    public string Format { get; set; } = CurrentFormat;

    public string Id { get; set; } = Guid.NewGuid().ToString("D");

    public string Name { get; set; } = string.Empty;

    /// <summary>Fight key, e.g. "top".</summary>
    public string Fight { get; set; } = string.Empty;

    public PlanSource? Source { get; set; }

    /// <summary>Columns of the source (players/slots); a player picks theirs.</summary>
    public List<Assignee> Assignees { get; set; } = [];

    public List<PlanEntry> Entries { get; set; } = [];
}

public sealed class PlanSource
{
    /// <summary>"google-sheet", "google-doc", "paste", "file", "llm".</summary>
    public string Kind { get; set; } = "paste";

    public string? Url { get; set; }

    public List<string> Tabs { get; set; } = [];

    /// <summary>ISO date of the import.</summary>
    public string? Imported { get; set; }
}

/// <summary>A source column: e.g. key "H2 SCH" (slot H2, job SCH) or "White Mage" (job WHM).</summary>
public sealed class Assignee
{
    public string Key { get; set; } = string.Empty;

    public string? Slot { get; set; }

    public Job? Job { get; set; }
}

public sealed class WhoSpec
{
    public string? Slot { get; set; }

    public Job? Job { get; set; }

    /// <summary>Assignee key the entry came from.</summary>
    public string? Assignee { get; set; }
}

/// <summary>
/// One thing to press (or read): an action, an either-or group of actions, or a note.
/// Exactly one of <see cref="Action"/>, <see cref="Any"/>, <see cref="Note"/> is set.
/// </summary>
public sealed class PlanItem
{
    /// <summary>Game action id.</summary>
    public uint? Action { get; set; }

    /// <summary>Action display name (for readability of plan files; the id is authoritative).</summary>
    public string? Name { get; set; }

    /// <summary>Either-or alternatives (done when any is used).</summary>
    public List<PlanItem>? Any { get; set; }

    /// <summary>Free text that could not be resolved to actions.</summary>
    public string? Note { get; set; }

    public static PlanItem ForAction(uint id, string name) => new() { Action = id, Name = name };

    public static PlanItem ForNote(string text) => new() { Note = text };

    public static PlanItem AnyOf(IEnumerable<PlanItem> options) => new() { Any = options.ToList() };

    /// <summary>All action ids this item is satisfied by.</summary>
    public IEnumerable<uint> ActionIds()
    {
        if (this.Action is { } id)
            yield return id;
        if (this.Any != null)
        {
            foreach (var o in this.Any)
            {
                // Plans built in code (importer, review edits) may hold a null option; files are repaired on load.
                if (o == null)
                    continue;
                foreach (var a in o.ActionIds())
                    yield return a;
            }
        }
    }

    public bool IsNote => this.Note != null && this.Action == null && this.Any == null;

    public override string ToString() =>
        this.Note ?? (this.Any != null ? string.Join(" / ", this.Any) : this.Name ?? this.Action?.ToString() ?? "?");
}

public sealed class PlanEntry
{
    public string Id { get; set; } = string.Empty;

    /// <summary>Anchor mechanic id (fight catalog).</summary>
    public string Mechanic { get; set; } = string.Empty;

    /// <summary>Which hit of a multi-hit mechanic the offset is relative to (0-based; null = first).</summary>
    public int? HitIndex { get; set; }

    /// <summary>Seconds relative to the anchor hit; null = automatic from the action (DESIGN §5.1).</summary>
    public double? Offset { get; set; }

    public WhoSpec Who { get; set; } = new();

    public List<PlanItem> Do { get; set; } = [];

    /// <summary>Items are pressed in order.</summary>
    public bool Seq { get; set; }

    public string? Target { get; set; }

    public string? Note { get; set; }

    /// <summary>Row-level notes from the source.</summary>
    public string? Detail { get; set; }

    /// <summary>Optional in the source (never counted as missed).</summary>
    public bool Optional { get; set; }

    /// <summary>Press at the planned time or as soon as it comes off cooldown.</summary>
    public bool WhenReady { get; set; }

    /// <summary>
    /// The entry belongs to the end of its phase ("Deploy just before P3 ends", "Rampart as P1 dies"):
    /// if a phase push makes the timeline jump past it, it becomes Due at the jump instead of Skipped
    /// (DESIGN §5.2). Usually anchored to the phase's enrage/end mechanic.
    /// </summary>
    public bool AtPhaseEnd { get; set; }

    /// <summary>Source lethality (★ count), if any.</summary>
    public int? Severity { get; set; }

    /// <summary>Provenance, e.g. "'TOP – White Mage'!C18".</summary>
    public string? Src { get; set; }
}

/// <summary>Who the local player is, for selecting their entries.</summary>
public sealed record PlayerContext(Job Job, int Level, string? Slot = null, string? AssigneeKey = null);

public static class PlanSelection
{
    /// <summary>
    /// Entries that apply to the player: job matches (or unset), slot matches (or unset), and the
    /// assignee matches when the player picked one and the entry records one; an entry of another column still applies
    /// when it names the player's slot (see <see cref="Applies"/>).
    /// </summary>
    public static IEnumerable<PlanEntry> For(MitPlan plan, PlayerContext me) =>
        plan.Entries.Where(e => Applies(e, me));

    /// <summary>
    /// True when the entry is the player's. With a column chosen (<see cref="PlayerContext.AssigneeKey"/>), an entry of
    /// another column is the player's only when it names their slot, for their job, or for whoever plays the slot (no
    /// job) when the slot is one of their role (<see cref="Slots.Fits"/>): the slot is the player's seat whichever column
    /// they follow. LPDU's job-less "MT" calls (Reprisal, Rampart) belong to a Warrior in MT who follows the tank-pair
    /// column "WAR/DRK WAR", like the "MT" column's Warrior calls; a Warrior whose configured slot is M1 does not get
    /// the "M1" column's melee calls.
    /// </summary>
    public static bool Applies(PlanEntry e, PlayerContext me)
    {
        if (e.Who.Job is { } j && j != me.Job)
            return false;
        if (e.Who.Slot is { } s && me.Slot != null && !string.Equals(s, me.Slot, StringComparison.OrdinalIgnoreCase))
            return false;
        if (e.Who.Slot != null && me.Slot == null && e.Who.Job == null)
            return false;
        if (me.AssigneeKey != null && e.Who.Assignee != null && !string.Equals(e.Who.Assignee, me.AssigneeKey, StringComparison.OrdinalIgnoreCase))
        {
            // Another column: the player's only when it names their slot (its job, if any, is theirs: checked above).
            return e.Who.Slot != null && string.Equals(e.Who.Slot, me.Slot, StringComparison.OrdinalIgnoreCase)
                   && (e.Who.Job == me.Job || Slots.Fits(e.Who.Slot, me.Job));
        }
        return true;
    }
}

/// <summary>Clean-up of plans read from any source (plan files, share codes, AI YAML, plans built in code).</summary>
public static class PlanNormalizer
{
    /// <summary>
    /// Writes the slots of a plan's columns and entries that are slot synonyms ("D3", "T1", "st", " H2 ") as the standard
    /// slots ("R1", "MT", "OT", "H2"). <see cref="PlanSelection"/> compares slots by name, and the player's slot (the
    /// configured one, a column picked in /mitcue > Plans, the CLI's --slot) is a standard name, so a call for "D3" would
    /// otherwise never be shown to the R1 player. Slots that are neither are kept (the plugin's plan checks report a
    /// job-less call for one: it is never shown). Null columns, entries and who are left alone.
    /// </summary>
    public static void NormalizeSlots(MitPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        foreach (var a in plan.Assignees ?? [])
        {
            if (a != null && Slots.TryNormalize(a.Slot, out var slot))
                a.Slot = slot;
        }

        foreach (var e in plan.Entries ?? [])
        {
            if (e?.Who != null && Slots.TryNormalize(e.Who.Slot, out var slot))
                e.Who.Slot = slot;
        }
    }
}
