using MitCue.Core.Import;

namespace MitCue.Core.Plans;

/// <summary>A column choice for a plan: the player's column and slot.</summary>
/// <param name="AssigneeKey">The column (assignee key) that is the player's, or null for "any column for my job".</param>
/// <param name="Slot">The player's slot with this choice, or null for none.</param>
public sealed record ColumnPick(string? AssigneeKey, string? Slot);

/// <summary>
/// The automatic "which column is me" choice for a plan and job: the one the plugin uses when the player has saved none
/// (bundled plans, share codes, imported plans), the one an import saves, and the one <c>mitcue plan/simulate/check</c>
/// use without <c>--assignee</c>. It is the job's column (<see cref="JobColumn"/>), unless that column leaves out some of
/// the player's calls (<see cref="IntendedColumns"/>); then it is the narrowest choice that has them too and no call of
/// another column. A column shows the job-less calls of the player's slot too (<see cref="PlanSelection.Applies"/>), so
/// LPDU's "H2 SCH" has the Scholar's P1/P2 calls of the job-less "H2" column, and a tank-pair column such as
/// "WAR/DRK WAR" the job-less "MT" calls. Columns of one team composition each ("WAR/DRK WAR", "WAR/GNB WAR" and
/// "WAR/PLD WAR": <see cref="IsComposition"/>) are alternatives: the player plays one of them, the one with the
/// party's co-tank when the party is known (a composition split over per-phase tabs is one: <see cref="SameComposition"/>).
/// The job's other columns (a column per phase or per tab) are all the player's: a sheet whose P1/P2 tab is headed "SCH"
/// and whose P3 tab is headed "H2 SCH" gets "any column for SCH in slot H2". Without a column for the job and a slot,
/// the job's calls give the slot (<see cref="ForJob"/>).
/// </summary>
public static class ColumnSuggestion
{
    /// <summary>
    /// The job's column: the column whose job is <paramref name="job"/> (preferring the one in <paramref name="slot"/>
    /// when several match), else the column of <paramref name="slot"/>; null when there is none. When that column is
    /// one team composition (<see cref="IsComposition"/>) and the party is known, the job's column for a composition the
    /// party plays is taken instead: LPDU's "WAR/PLD WAR" for a Warrior whose co-tank is a Paladin. When the party plays
    /// none of them, the job's first column that is no composition is taken if there is one: Carry the Clear's "White
    /// Mage" tab (for any co-healer) for a White Mage next to a Sage, whichever tab comes first in the sheet.
    /// </summary>
    /// <param name="plan">The plan.</param>
    /// <param name="job">The player's job.</param>
    /// <param name="slot">
    /// The player's configured slot (e.g. "H2"), if any. A slot of another role than the job's is not the player's slot
    /// on this job (<see cref="Slots.ForJob"/>): a Warrior whose usual slot is M1 does not follow the "M1" column.
    /// </param>
    /// <param name="party">The jobs in the player's party, or null/empty when unknown.</param>
    public static Assignee? JobColumn(MitPlan plan, Job job, string? slot, IReadOnlyCollection<Job>? party = null)
    {
        slot = Slots.ForJob(slot, job);
        Assignee? byJob = null;
        Assignee? bySlot = null;
        foreach (var a in plan.Assignees)
        {
            if (a == null)
                continue;
            var slotMatches = slot != null && Slots.TryNormalize(a.Slot, out var s) && s == slot;
            if (a.Job == job && job != Job.None)
            {
                if (slotMatches)
                    return a;
                byJob ??= a;
            }
            else if (slotMatches)
            {
                bySlot ??= a;
            }
        }

        if (byJob != null && party is { Count: > 0 } && IsComposition(byJob) && !PlayedBy(byJob, job, party))
        {
            return plan.Assignees.FirstOrDefault(a => a != null && a.Job == job && IsComposition(a) && PlayedBy(a, job, party))
                   ?? plan.Assignees.FirstOrDefault(a => a != null && a.Job == job && !IsComposition(a))
                   ?? byJob;
        }

        return byJob ?? bySlot;
    }

    /// <summary>
    /// The job's column as a choice: its key and slot, else <paramref name="defaultSlot"/> when it is a slot of the job's
    /// role. Without either, a plan with no column for the job (a role sheet such as LPDU's ranged columns "R1" and
    /// "R2") gives the slot of the job's calls (<see cref="SeatOfCalls"/>), so the player gets one seat's calls, not those
    /// of every seat the job has calls in.
    /// </summary>
    public static ColumnPick ForJob(MitPlan plan, Job job, string? defaultSlot, IReadOnlyCollection<Job>? party = null)
    {
        var column = JobColumn(plan, job, defaultSlot, party);
        var slot = column?.Slot ?? Slots.ForJob(defaultSlot, job);
        if (column == null && slot == null)
            slot = SeatOfCalls(plan, job, party);
        return new ColumnPick(column?.Key, slot);
    }

    /// <summary>
    /// The seats the party settles: each slot in which one party job, and only one, has all of its calls that name a
    /// slot (LPDU's Bard, whose calls are all in R1). A party job with calls in several slots (LPDU's Red Mage: R1 and
    /// R2) does not play a slot that is another party job's seat. Empty when the party is unknown.
    /// </summary>
    /// <param name="plan">The plan.</param>
    /// <param name="party">The jobs in the player's party, or null/empty when unknown.</param>
    public static IReadOnlyDictionary<string, Job> PartySeats(MitPlan plan, IReadOnlyCollection<Job>? party)
    {
        var seats = new Dictionary<string, Job>(StringComparer.Ordinal);
        if (party is not { Count: > 0 })
            return seats;

        var slotsOf = new Dictionary<Job, HashSet<string>>();
        foreach (var e in plan.Entries)
        {
            if (e?.Who is { Job: { } job } who && job != Job.None && party.Contains(job) && Slots.TryNormalize(who.Slot, out var slot))
            {
                if (!slotsOf.TryGetValue(job, out var slots))
                    slotsOf[job] = slots = [];
                slots.Add(slot);
            }
        }

        foreach (var holders in slotsOf.Where(kv => kv.Value.Count == 1).GroupBy(kv => kv.Value.First()))
        {
            if (holders.Count() == 1)
                seats[holders.Key] = holders.First().Key;
        }

        return seats;
    }

    /// <summary>
    /// The player's seat when the plan has no column for their job and their slot is unknown: the first slot of the job's
    /// role (<see cref="Slots.Standard"/> order) that the job's calls name, as the first tank pair is taken outside a
    /// party, passing over a slot that is another party job's seat (<see cref="PartySeats"/>). LPDU's Red Mage has calls
    /// in R1 and R2: R1, or R2 next to a Bard. Null when the job's calls name no slot of its role.
    /// </summary>
    private static string? SeatOfCalls(MitPlan plan, Job job, IReadOnlyCollection<Job>? party)
    {
        if (job == Job.None)
            return null;
        var slots = Slots.Standard
            .Where(s => Slots.Fits(s, job) && plan.Entries.Exists(e => e?.Who is { } who && who.Job == job && Slots.TryNormalize(who.Slot, out var es) && es == s))
            .ToList();
        if (slots.Count < 2)
            return slots.FirstOrDefault();
        var seats = PartySeats(plan, party);
        return slots.FirstOrDefault(s => !seats.TryGetValue(s, out var holder) || holder == job) ?? slots[0];
    }

    /// <summary>The automatic choice: <see cref="Complete"/> when the job's column leaves out some of the player's calls, else the job's column.</summary>
    /// <param name="plan">The plan.</param>
    /// <param name="job">The player's job.</param>
    /// <param name="defaultSlot">The configured slot, if any.</param>
    /// <param name="party">The jobs in the player's party, or null/empty when unknown (<see cref="JobColumn"/>).</param>
    public static ColumnPick Suggest(MitPlan plan, Job job, string? defaultSlot, IReadOnlyCollection<Job>? party = null)
    {
        var column = ForJob(plan, job, defaultSlot, party);
        return Complete(plan, job, defaultSlot, column) ?? column;
    }

    /// <summary>
    /// The choice that shows every call of the player's columns (<see cref="IntendedColumns"/>) when
    /// <paramref name="column"/> (the job's column) does not; null when it does, when the player's columns name different
    /// slots (no guess is made), or when every choice that has them all also shows calls of another column. The result
    /// is the candidate with the fewest entries: "any column for my job in my slot", or one of the player's columns.
    /// </summary>
    /// <param name="plan">The plan.</param>
    /// <param name="job">The player's job.</param>
    /// <param name="defaultSlot">The configured slot, if any.</param>
    /// <param name="column">The job's column (<see cref="ForJob"/>).</param>
    public static ColumnPick? Complete(MitPlan plan, Job job, string? defaultSlot, ColumnPick column)
    {
        if (job == Job.None)
            return null;

        var columns = PlayerColumns(plan.Assignees, job, defaultSlot);
        var slot = PlayerSlot(columns, job, defaultSlot, out var consistent);
        if (!consistent)
            return null;

        var keys = IntendedColumns(plan, columns, column.AssigneeKey);
        var intended = Intended(plan, keys, job, slot);
        if (intended.Count == 0)
            return null;

        var shown = Selected(plan, Context(column, job, defaultSlot));
        if (intended.TrueForAll(shown.Contains))
            return null;

        // Candidates: any column for the job in the player's slot, then each of the player's columns. A candidate may show
        // entries without a column (every choice shows them), but no entry of another column: that would be an alternative
        // seat of the player's (another tank pair) or another player's call.
        var mine = intended.ToHashSet();
        var candidates = new List<ColumnPick> { new(null, slot) };
        candidates.AddRange(keys.Select(k => new ColumnPick(k, slot)));
        ColumnPick? best = null;
        var bestCount = int.MaxValue;
        foreach (var candidate in candidates)
        {
            var selection = Selected(plan, Context(candidate, job, defaultSlot));
            if (selection.Count >= bestCount || !intended.TrueForAll(selection.Contains))
                continue;
            if (selection.Any(e => !mine.Contains(e) && !string.IsNullOrWhiteSpace(e.Who.Assignee)))
                continue;
            best = candidate;
            bestCount = selection.Count;
        }

        return best;
    }

    /// <summary>
    /// The player context a column choice gives (the plugin's SessionManager and preview, the CLI): the choice's slot, else
    /// <paramref name="defaultSlot"/> when it is a slot of the job's role (<see cref="Slots.ForJob"/>).
    /// </summary>
    public static PlayerContext Context(ColumnPick pick, Job job, string? defaultSlot, int level = 0) =>
        new(job, level, pick.Slot ?? Slots.ForJob(defaultSlot, job), pick.AssigneeKey);

    /// <summary>The column key of an entry: its assignee, else "slot job".</summary>
    public static string ColumnKey(PlanEntry entry)
    {
        var who = entry.Who;
        if (who == null)
            return string.Empty;
        if (!string.IsNullOrWhiteSpace(who.Assignee))
            return who.Assignee.Trim();
        return string.Join(' ', new[] { who.Slot, who.Job?.ToString() }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    /// <summary>
    /// True for the column of one team composition: its key names two or more jobs, as the importer keys the columns of
    /// composition tabs ("WAR/DRK WAR", "TOP – WHM + SCH White Mage", also the xlsx form "WARDRK WAR":
    /// <see cref="LayoutDetector.JobsFromTabName"/>). Such columns of one job are alternatives of each other.
    /// </summary>
    public static bool IsComposition(Assignee column) => LayoutDetector.JobsFromTabName(column.Key).Count >= 2;

    /// <summary>
    /// True when both column keys name the same team composition (<see cref="IsComposition"/>): "WHM/SCH P1 WHM" and
    /// "WHM/SCH P3 WHM", one composition split over per-phase tabs whose headers the importer told apart by the tab
    /// name. A job's columns for one composition are one seat; "WAR/DRK WAR" and "WAR/GNB WAR" are not the same.
    /// </summary>
    public static bool SameComposition(string? a, string? b)
    {
        if (a == null || b == null)
            return false;
        var x = LayoutDetector.JobsFromTabName(a);
        if (x.Count < 2)
            return false;
        var y = LayoutDetector.JobsFromTabName(b);
        return x.Count == y.Count && x.All(y.Contains);
    }

    /// <summary>
    /// True when at least half of the mechanics with calls in <paramref name="column"/> have calls in
    /// <paramref name="others"/> too: the same seat written twice (Carry the Clear's combined "WHM + SCH" tab next to
    /// its White Mage tab), not more of the seat's calls (a general tab next to a tank-pair tab).
    /// </summary>
    public static bool Repeats(MitPlan plan, Assignee column, IEnumerable<Assignee> others)
    {
        var keys = others.Select(a => a.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var own = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in plan.Entries)
        {
            if (e == null)
                continue;
            var key = ColumnKey(e);
            if (string.Equals(key, column.Key, StringComparison.OrdinalIgnoreCase))
                own.Add(e.Mechanic ?? string.Empty);
            else if (keys.Contains(key))
                covered.Add(e.Mechanic ?? string.Empty);
        }

        return own.Count > 0 && own.Count(covered.Contains) * 2 >= own.Count;
    }

    /// <summary>
    /// The player's columns: those of their job, plus job-less columns of their slot. Without a configured slot (or with
    /// one of another role than the job's, <see cref="Slots.ForJob"/>) the slot is taken from a column of their job
    /// ("H2 SCH" → H2), so LPDU's "H2" (P1/P2) and "H2 SCH" (P3–P6) both match a Scholar, and a White Mage whose usual
    /// slot is MT does not get the "MT" column. Every column of the job is listed, also those that are alternatives of
    /// each other (LPDU's tank pairs): an import keeps them all, and <see cref="IntendedColumns"/> tells which are the
    /// player's calls.
    /// </summary>
    public static List<Assignee> PlayerColumns(IEnumerable<Assignee> assignees, Job job, string? slot)
    {
        var all = assignees.Where(a => a != null).ToList();
        var mine = new List<Assignee>();
        if (job != Job.None)
            mine.AddRange(all.Where(a => a.Job == job));

        var mySlot = Slots.ForJob(slot, job);
        if (mySlot == null)
        {
            foreach (var a in mine)
            {
                if (Slots.TryNormalize(a.Slot, out var inferred))
                {
                    mySlot = inferred;
                    break;
                }
            }
        }

        if (mySlot != null)
        {
            foreach (var a in all)
            {
                if ((a.Job == null || a.Job == Job.None) && Slots.TryNormalize(a.Slot, out var aSlot) && aSlot == mySlot && !mine.Contains(a))
                    mine.Add(a);
            }
        }

        return all.Where(mine.Contains).ToList();
    }

    /// <summary>
    /// The player's slot as <see cref="PlayerColumns"/> infers it: the configured one (when it is a slot of the job's
    /// role), else the slot of a column of their job. Not consistent when their columns name different slots (then no
    /// choice is guessed).
    /// </summary>
    public static string? PlayerSlot(List<Assignee> columns, Job job, string? defaultSlot, out bool consistent)
    {
        var slot = Slots.ForJob(defaultSlot, job);
        if (slot == null)
        {
            foreach (var column in columns.Where(a => a.Job == job))
            {
                if (Slots.TryNormalize(column.Slot, out var inferred))
                {
                    slot = inferred;
                    break;
                }
            }
        }

        consistent = columns.TrueForAll(a => !Slots.TryNormalize(a.Slot, out var s) || s == slot);
        return slot;
    }

    /// <summary>
    /// The columns whose calls are the player's, by key in column order: the job-less columns of the player's slot, the
    /// job's columns that are not a team composition (a column per phase or per tab: one seat), and one composition
    /// column (<see cref="IsComposition"/>): <paramref name="columnKey"/> (the job's column, or the column the player
    /// chose) when it is one, else the job's first composition column that does not repeat the calls of those other
    /// columns (<see cref="Repeats"/>: a general tab and one pair tab are one seat, Carry the Clear's White Mage tab and
    /// its combined tab are two). The job's other composition columns are alternatives (another tank pair). The same
    /// holds the other way round: when <paramref name="columnKey"/> is a composition column, a column of the job that
    /// repeats its calls is the same seat written twice and not counted (Carry the Clear's White Mage tab when the
    /// combined tab comes first in the sheet, or was chosen), whichever comes first. The job's other columns for the
    /// same composition (<see cref="SameComposition"/>: the composition split over per-phase tabs) are the player's too.
    /// </summary>
    /// <param name="plan">The plan (for <see cref="Repeats"/>).</param>
    /// <param name="playerColumns">The player's columns (<see cref="PlayerColumns"/>).</param>
    /// <param name="columnKey">The job's column or the chosen one; null for none.</param>
    public static List<string> IntendedColumns(MitPlan plan, IReadOnlyList<Assignee> playerColumns, string? columnKey)
    {
        var chosen = columnKey == null ? null : playerColumns.FirstOrDefault(a => string.Equals(a.Key, columnKey, StringComparison.OrdinalIgnoreCase));
        var intended = playerColumns.Where(a => ReferenceEquals(a, chosen) || !HasJob(a) || !IsComposition(a)).ToList();
        if (chosen == null || !HasJob(chosen) || !IsComposition(chosen))
        {
            var plain = intended.Where(HasJob).ToList();
            if (playerColumns.FirstOrDefault(a => HasJob(a) && IsComposition(a) && !Repeats(plan, a, plain)) is { } composition)
                intended.Add(composition);
        }
        else
        {
            intended.RemoveAll(a => !ReferenceEquals(a, chosen) && HasJob(a) && Repeats(plan, a, [chosen]));
        }

        if (intended.FirstOrDefault(a => HasJob(a) && IsComposition(a)) is { } seat)
            intended.AddRange(playerColumns.Where(a => !intended.Contains(a) && a.Job == seat.Job && SameComposition(a.Key, seat.Key)));

        return playerColumns.Where(intended.Contains).Select(a => a.Key).ToList();

        static bool HasJob(Assignee a) => a.Job is { } job && job != Job.None;
    }

    /// <summary>The entries of the given columns that are meant for the player's job and slot.</summary>
    public static List<PlanEntry> Intended(MitPlan plan, IReadOnlyCollection<string> columnKeys, Job job, string? slot, int level = 0)
    {
        if (columnKeys.Count == 0)
            return [];
        var keys = columnKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var me = new PlayerContext(job, level, slot);
        return plan.Entries.Where(e => e != null && keys.Contains(ColumnKey(e)) && PlanSelection.Applies(e, me)).ToList();
    }

    /// <summary>The entries a player context selects (PlanEntry has reference equality: the set holds these very entries).</summary>
    public static HashSet<PlanEntry> Selected(MitPlan plan, PlayerContext me) =>
        plan.Entries.Where(e => e != null && PlanSelection.Applies(e, me)).ToHashSet();

    /// <summary>True when the party plays the composition of <paramref name="column"/>: every other job its key names is in the party.</summary>
    private static bool PlayedBy(Assignee column, Job job, IReadOnlyCollection<Job> party)
    {
        var others = LayoutDetector.JobsFromTabName(column.Key).Where(j => j != job).ToList();
        return others.Count > 0 && others.TrueForAll(party.Contains);
    }
}
