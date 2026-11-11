using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using MitCue.Core.Plans;

namespace MitCue.Core.Import;

/// <summary>How an assignee column header names its player.</summary>
public enum AssigneeKind
{
    /// <summary>A party slot only: "MT", "H2", "D3".</summary>
    Slot,

    /// <summary>A job only: "White Mage", "SCH", "WAR" (tank-pair tabs), or several jobs ("WHM/AST").</summary>
    Job,

    /// <summary>A slot and a job: "H2 SCH", "SCH (H2)", "H1 WHM/AST".</summary>
    SlotAndJob,

    /// <summary>A free name ending in a role word: "CM LB Tank", "Shield Healer", "Tanks".</summary>
    Custom,
}

/// <summary>
/// A parsed assignee column header (docs/DESIGN.md §6 step 2). <see cref="Key"/> is the header text with whitespace
/// normalised and is what a player picks as "their column" (it becomes <see cref="Assignee.Key"/>).
/// </summary>
public sealed partial record AssigneeHeader
{
    /// <summary>The header text, trimmed, inner whitespace collapsed ("H2 SCH", "CM LB Tank").</summary>
    public required string Key { get; init; }

    public required AssigneeKind Kind { get; init; }

    /// <summary>Standard slot name (MT, OT, H1, H2, M1, M2, R1, R2) or null.</summary>
    public string? Slot { get; init; }

    /// <summary>The job when the header names exactly one, else null.</summary>
    public Job? Job { get; init; }

    /// <summary>Every job the header names, in order ("H1 WHM/AST" → [WHM, AST]); empty when none.</summary>
    public IReadOnlyList<Job> Jobs { get; init; } = [];

    /// <summary>
    /// The role implied by the job, the slot or the role word (<see cref="Role.None"/> when unknown). A ranged slot
    /// alone (R1, R2, D3, D4) gives <see cref="Role.None"/>: it holds a physical ranged DPS or a caster.
    /// </summary>
    public Role Role { get; init; }

    // Separators between the parts of a header: "H2 SCH", "H2 - SCH", "SCH (H2)", "H1 WHM/AST", "H2: SCH".
    [GeneratedRegex(@"[\s/\-–—:()\[\],|&+]+")]
    private static partial Regex PartSeparator();

    [GeneratedRegex(@"^[\p{L}\p{N}\s/\-–—:()\[\],|&+.']+$")]
    private static partial Regex AllowedChars();

    // "#1" after a word: "Healer #1" → "Healer 1".
    [GeneratedRegex(@"(?<=\s|^)#\s*(\d)")]
    private static partial Regex NumberSign();

    private static readonly Dictionary<string, string> SlotPhrases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["main tank"] = "MT", ["maintank"] = "MT", ["tank 1"] = "MT", ["tank1"] = "MT",
        ["off tank"] = "OT", ["offtank"] = "OT", ["off-tank"] = "OT", ["tank 2"] = "OT", ["tank2"] = "OT",
        ["healer 1"] = "H1", ["healer1"] = "H1", ["healer 2"] = "H2", ["healer2"] = "H2",
        ["melee 1"] = "M1", ["melee1"] = "M1", ["melee 2"] = "M2", ["melee2"] = "M2",
        ["ranged 1"] = "R1", ["ranged1"] = "R1", ["ranged 2"] = "R2", ["ranged2"] = "R2",
        ["dps 1"] = "M1", ["dps 2"] = "M2", ["dps 3"] = "R1", ["dps 4"] = "R2",
    };

    private static readonly Dictionary<string, Role> RoleWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tank"] = Role.Tank, ["tanks"] = Role.Tank,
        ["healer"] = Role.Healer, ["healers"] = Role.Healer,
        ["melee"] = Role.Melee, ["melees"] = Role.Melee,
        ["ranged"] = Role.PhysicalRanged, ["ranges"] = Role.PhysicalRanged,
        ["caster"] = Role.Caster, ["casters"] = Role.Caster,
        ["dps"] = Role.None,
    };

    /// <summary>The plan model's assignee for this column.</summary>
    public Assignee ToAssignee() => new() { Key = this.Key, Slot = this.Slot, Job = this.Job };

    /// <summary>
    /// Parses a column header strictly: the whole text must be a slot, a job (abbreviation or English name), a slot
    /// with job(s), a slot phrase ("Main Tank", "Healer 2", "Healer #2") or a short name ending in a role word ("CM LB Tank").
    /// Anything else (titles like "TOP — White Mage", statistics like "Top WHMs (of 36)", plan text) is rejected.
    /// </summary>
    public static bool TryParse(string? header, [NotNullWhen(true)] out AssigneeHeader? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(header))
            return false;
        var key = Regex.Replace(header.Trim(), @"\s+", " ").TrimEnd(':').Trim();
        if (key.Length == 0 || key.Length > 40)
            return false;

        // "Healer #1", "Tank #2": a number sign before a slot number is only a numbering mark.
        var text = NumberSign().Replace(key, "$1");
        if (!AllowedChars().IsMatch(text))
            return false;

        // 1. A slot ("MT", "D3") or a slot phrase ("Main Tank", "Healer 2").
        if (!Slots.TryNormalize(text, out var slot) && SlotPhrases.TryGetValue(text, out var phraseSlot))
            slot = phraseSlot;
        if (slot.Length > 0)
        {
            result = new AssigneeHeader { Key = key, Kind = AssigneeKind.Slot, Slot = slot, Role = SlotRole(slot) };
            return true;
        }

        var parts = PartSeparator().Split(text).Where(p => p.Length > 0).ToList();
        if (parts.Count == 0)
            return false;

        // 2. Optional slot + one or more jobs ("SCH", "White Mage", "H2 SCH", "SCH (H2)", "H1 WHM/AST").
        string? foundSlot = null;
        var jobs = new List<Job>();
        var ok = true;
        for (var i = 0; i < parts.Count && ok; i++)
        {
            if (Slots.TryNormalize(parts[i], out var s))
            {
                ok = foundSlot is null;
                foundSlot = s;
                continue;
            }

            if (i + 1 < parts.Count && TryJob(parts[i] + " " + parts[i + 1], out var two))
            {
                jobs.Add(two);
                i++;
                continue;
            }

            if (TryJob(parts[i], out var one))
            {
                jobs.Add(one);
                continue;
            }

            ok = false;
        }

        if (ok && jobs.Count > 0 && jobs.Distinct().Count() == jobs.Count)
        {
            result = new AssigneeHeader
            {
                Key = key,
                Kind = foundSlot is null ? AssigneeKind.Job : AssigneeKind.SlotAndJob,
                Slot = foundSlot,
                Job = jobs.Count == 1 ? jobs[0] : null,
                Jobs = jobs,
                Role = MitCue.Core.Jobs.RoleOf(jobs[0]) is var r && jobs.All(j => MitCue.Core.Jobs.RoleOf(j) == r) ? r : (foundSlot is null ? Role.None : SlotRole(foundSlot)),
            };
            return true;
        }

        // 3. A short name ending in a role word: "CM LB Tank", "CM Rep Tank", "Shield Healer", "Tanks".
        var words = text.Split(' ');
        if (words.Length <= 4 && RoleWords.TryGetValue(words[^1], out var role) && words.All(w => w.All(c => char.IsLetter(c) || c is '-' or '\'' or '.')))
        {
            result = new AssigneeHeader { Key = key, Kind = AssigneeKind.Custom, Role = role };
            return true;
        }

        return false;
    }

    /// <summary>
    /// The role a slot implies. <see cref="Slots.RoleOf(string)"/> files the ranged slots under casters; a column
    /// headed only "R1" may as well be the physical ranged DPS (LPDU's R1 column assigns Dismantle), so it is unknown.
    /// </summary>
    private static Role SlotRole(string slot) => slot is "R1" or "R2" ? Role.None : Slots.RoleOf(slot);

    private static bool TryJob(string text, out Job job)
    {
        job = MitCue.Core.Job.None;
        // Jobs.TryParse also accepts numeric ClassJob ids; headers must name the job.
        return text.Any(char.IsLetter) && text.All(c => char.IsLetter(c) || c == ' ') && MitCue.Core.Jobs.TryParse(text, out job);
    }
}
