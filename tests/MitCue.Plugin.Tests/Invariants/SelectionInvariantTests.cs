using System.Text;
using System.Text.Json;
using Dalamud.Plugin.Services;
using MitCue.Core;
using MitCue.Core.Import;
using MitCue.Core.Plans;
using MitCue.Import;
using MitCue.Plans;
using MitCue.PluginTests.Support;
using MitCue.Runtime;

namespace MitCue.PluginTests.Invariants;

/// <summary>
/// The selection subsystem judged as a whole (review round 5, trimmed from the reviewers' harness): for bundled plans,
/// importer output and small sheets of each shape, and each player (job, usual slot, party), the calls the overlay shows,
/// the partner rows, the Plans tab, the import window and the CLI keep these invariants:
/// I1 every own call is the job's, or a job-less call of the player's own seat; I2 no seat or call twice; I3 every call
/// of the player's seat is shown; I4 the partner rows hold the role partners' calls only; I5 every surface shows the
/// overlay's calls; I6 the static's flows (White Mage NoirMit, Scholar LPDU, Carry the Clear - SCH) are unchanged.
/// Known, documented limits are left out where noted (a general tank tab next to several pair tabs, a usual slot that
/// conflicts with the job's column's slot, partners whose seat neither the slot nor the party tells).
/// </summary>
public class SelectionInvariantTests
{
    private const string NoirMit = "335b6c4f-3684-55e0-a8d0-3e78724ef200";
    private const string CtcSch = "5b622e32-04ae-53de-a4f8-f66e4c0aae0c";
    private const string Lpdu = "1f75e1e8-7321-566c-ae86-8568019fe914";
    private const int Level = 90;

    private static readonly string?[] UsualSlots = [null, "MT", "OT", "H1", "H2", "M1", "M2", "R1", "R2"];

    /// <summary>Every healer and tank, and the DPS whose calls differ on the plans swept (one melee: LPDU's are job-less).</summary>
    private static readonly Job[] SweptJobs =
        [Job.WHM, Job.AST, Job.SCH, Job.SGE, Job.WAR, Job.DRK, Job.GNB, Job.PLD, Job.SAM, Job.BRD, Job.MCH, Job.RDM, Job.PCT, Job.BLM, Job.SMN];

    private static readonly Job[] BaseA = [Job.WAR, Job.DRK, Job.WHM, Job.SCH, Job.SAM, Job.RPR, Job.DNC, Job.PCT];
    private static readonly Job[] BaseB = [Job.GNB, Job.PLD, Job.AST, Job.SGE, Job.MNK, Job.DRG, Job.BRD, Job.RDM];

    internal sealed record Party(string Name, IReadOnlyList<Job> Jobs);

    /// <summary>A plan under test, as the import window builds it (not normalized), with what the oracle needs to know.</summary>
    internal sealed class Case
    {
        public required string Name { get; init; }

        public required MitPlan Raw { get; init; }

        /// <summary>Column keys holding the same seat's calls twice: either one of a group is the seat.</summary>
        public List<string[]> SameSeat { get; init; } = [];

        /// <summary>Columns for a role without a seat (Cosmo Memory's LB and Rep tank): no one's automatically.</summary>
        public HashSet<string> RoleOnly { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>A general tank tab next to several pair tabs: the documented one-column limit (I3 and the Save step's warning).</summary>
        public bool GeneralNextToPairs { get; init; }
    }

    internal sealed record Surface(PlayerMapping Mapping, PlayerContext Me, List<PlanEntry> Own, List<PlanEntry> Partners);

    /// <summary>The player's party: unknown, two full parties (the player's job in it), and a party without a role partner.</summary>
    internal static IReadOnlyList<Party> PartiesFor(Job job) =>
    [
        new("unknown", []),
        new("A", Replace(BaseA, job)),
        new("B", Replace(BaseB, job)),
        new("alone", [job, Job.SAM, Job.RPR, Job.DNC]),
    ];

    private static Job[] Replace(Job[] party, Job job)
    {
        if (party.Contains(job))
            return party;
        var role = Jobs.RoleOf(job);
        var h1 = job is Job.WHM or Job.AST;
        var index = role == Role.Healer
            ? Array.FindIndex(party, j => Jobs.RoleOf(j) == Role.Healer && (j is Job.WHM or Job.AST) == h1)
            : Array.FindIndex(party, j => Jobs.RoleOf(j) == role);
        if (index < 0)
            index = party.Length - 1;
        var copy = party.ToArray();
        copy[index] = job;
        return copy;
    }

    // ---------------------------------------------------------------- the sweep

    [Fact]
    public void EverySurfaceKeepsTheInvariants()
    {
        var problems = new Problems();
        var parts = SessionManagerTests.NewManager(new Configuration { ShowOtherAssignees = true });
        parts.Data.EnsureLoaded();
        parts.Plans.EnsureLoaded();
        var cases = new List<Case>();
        cases.AddRange(BundledCases(parts.Plans));
        cases.AddRange(Synthetic());
        cases.Add(PerPhaseCompositions(parts.Data));

        var cliOut = new StringWriter();
        var workDir = Kit.TempDirectory().FullName;
        var cli = new MitCue.Cli.CliContext(cliOut, cliOut, new MitCue.Cli.CliEnvironment { CurrentDirectory = workDir, ProgramDirectory = AppContext.BaseDirectory, Input = () => new StringReader(string.Empty) });
        var config = new Configuration { ShowOtherAssignees = true };
        var store = new PlanStore(Kit.TempDirectory(), config, () => { }, Kit.Errors());

        foreach (var c in cases)
        {
            // The plan as the store holds it (normalized on every read), as the import window has it (the bundled plans are
            // imports of their sheets too), and as the CLI reads the plan file.
            var plan = Normalized(c.Raw);
            var raw = Clone(c.Raw);
            var file = Path.Combine(workDir, c.Name + ".yaml");
            File.WriteAllText(file, PlanSerializer.ToYaml(c.Raw));
            var cliPlan = cli.ReadPlan(file);

            foreach (var slot in UsualSlots)
            {
                config.DefaultSlot = slot;
                foreach (var job in SweptJobs)
                {
                    foreach (var party in PartiesFor(job))
                    {
                        var where = $"{c.Name} {job} slot={slot ?? "-"} party={party.Name}";
                        var s = Overlay(store, config, plan, job, party.Jobs);
                        CheckOwn(problems, c, plan, job, slot, party, s, where);
                        CheckPartners(problems, c, plan, job, party, s, where);

                        // I5: the Plans tab's suggestion (the current job's row follows the party) is the overlay's choice.
                        var suggestion = PlanStore.SuggestedMapping(plan, job, slot, party.Jobs);
                        if (suggestion.AssigneeKey != s.Mapping.AssigneeKey || suggestion.Slot != s.Mapping.Slot)
                            problems.Add("I5 Plans tab", where, $"{suggestion.AssigneeKey}/{suggestion.Slot} overlay {s.Mapping.AssigneeKey}/{s.Mapping.Slot}");

                        CheckImport(problems, c, raw, plan, job, slot, party, s, suggestion, where);
                        if (party.Jobs.Count == 0)
                            CheckCli(problems, cli, file, cliPlan, plan, job, slot, s, where);
                    }
                }
            }
        }

        Assert.True(problems.Count == 0, problems.ToString());
    }

    private static void CheckOwn(Problems problems, Case c, MitPlan plan, Job job, string? slot, Party party, Surface s, string where)
    {
        foreach (var e in s.Own)
        {
            var ej = JobOf(plan, e);
            var es = SlotOf(plan, e);
            if (ej != null && ej != job)
                problems.Add("I1 another job's call", where, Who(e));
            else if (ej == null && es != null && (es != s.Me.Slot || !Slots.Fits(es, job)))
                problems.Add("I1 job-less call of another seat", where, Who(e));
            if (c.RoleOnly.Contains(e.Who.Assignee ?? string.Empty))
                problems.Add("I1 role-only column shown", where, Who(e));
        }

        var seats = s.Own.Where(e => JobOf(plan, e) == job).Select(e => SlotOf(plan, e)).OfType<string>().Distinct().ToList();
        if (seats.Count > 1)
            problems.Add("I1/I2 own calls of two seats", where, string.Join(",", seats));
        var compositions = s.Own.Select(e => string.Join("+", CompositionOf(plan, e).Order())).Where(k => k.Length > 0).Distinct().ToList();
        if (compositions.Count > 1)
            problems.Add("I2 own calls of two compositions", where, string.Join(" ; ", compositions));
        foreach (var twice in Twice(s.Own))
            problems.Add("I2 call twice", where, twice);
        foreach (var group in c.SameSeat.Where(g => g.Count(k => s.Own.Any(e => string.Equals(e.Who.Assignee, k, StringComparison.OrdinalIgnoreCase))) > 1))
            problems.Add("I2 same seat twice", where, string.Join(" / ", group));

        // I3, where the overlay's slot is the player's seat (a usual slot that conflicts with the job's column's slot is
        // the column's; a tank without a tank slot gets their pair's column only: both documented).
        var seat = SeatSlot(plan, job, slot);
        if (c.GeneralNextToPairs || seat == null || seat != s.Me.Slot)
            return;
        var composition = ChosenComposition(plan, job, party.Jobs);
        var own = s.Own.ToHashSet();
        var expected = plan.Entries.Where(e =>
        {
            var ej = JobOf(plan, e);
            var es = SlotOf(plan, e);
            if (c.RoleOnly.Contains(e.Who.Assignee ?? string.Empty) || (ej == null && es == null))
                return false;
            var mine = ej == job ? es == null || es == seat : ej == null && es == seat && Slots.Fits(es, job);
            var ec = CompositionOf(plan, e);
            return mine && (ec.Count == 0 || (composition != null && ec.Order().SequenceEqual(composition.Order())));
        }).ToList();
        var sameSeat = c.SameSeat.SelectMany(g => g).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var e in expected.Where(e => !sameSeat.Contains(e.Who.Assignee ?? string.Empty) && !own.Contains(e)))
            problems.Add("I3 seat call not shown", where, Who(e));
        foreach (var group in c.SameSeat)
        {
            var options = group.Select(k => expected.Where(e => string.Equals(e.Who.Assignee, k, StringComparison.OrdinalIgnoreCase)).ToList()).Where(l => l.Count > 0).ToList();
            if (options.Count > 0 && !options.Any(o => o.All(own.Contains)))
                problems.Add("I3 same-seat group not shown", where, string.Join(" / ", group));
        }
    }

    private static void CheckPartners(Problems problems, Case c, MitPlan plan, Job job, Party party, Surface s, string where)
    {
        var own = s.Own.ToHashSet();
        var mineIsRole = s.Me.AssigneeKey != null && c.RoleOnly.Contains(s.Me.AssigneeKey);
        foreach (var p in s.Partners)
        {
            var pj = JobOf(plan, p);
            var ps = SlotOf(plan, p);
            if (own.Contains(p) || PlanSelection.Applies(p, s.Me))
                problems.Add("I4 own call as partner", where, Who(p));
            if (pj == job)
                problems.Add("I4 partner of the player's job", where, Who(p));
            if (ps != null && ps == s.Me.Slot)
                problems.Add("I4 partner in the player's slot", where, Who(p));
            if (party.Jobs.Count > 0 && pj != null && !party.Jobs.Contains(pj.Value))
                problems.Add("I4 partner job not in the party", where, Who(p));
            if (!mineIsRole && ps == null && c.RoleOnly.Contains(p.Who.Assignee ?? string.Empty))
                problems.Add("I4 partner in a role-only column", where, Who(p));
            var group = pj is { } partnerJob ? GroupOf(Jobs.RoleOf(partnerJob)) : ps != null ? GroupOf(Slots.RoleOf(ps)) : 0;
            if (group != GroupOf(Jobs.RoleOf(job)))
                problems.Add("I4 partner of another role", where, Who(p));
        }

        // With the party known, a partner job is not in a slot that the party gives another job (the Bard's R1).
        var seats = ColumnSuggestion.PartySeats(plan, party.Jobs);
        foreach (var p in s.Partners)
        {
            if (JobOf(plan, p) is { } pj && SlotOf(plan, p) is { } ps && seats.TryGetValue(ps, out var holder) && holder != pj)
                problems.Add("I4 partner in another job's seat", where, Who(p));
        }
    }

    /// <summary>I5: the import window's Save step and review (with the party, as the window passes it) match the overlay after the save.</summary>
    private static void CheckImport(Problems problems, Case c, MitPlan raw, MitPlan plan, Job job, string? slot, Party party, Surface s, PlayerMapping suggestion, string where)
    {
        var choice = ImportColumns.Choose(raw, job, Level, slot, party: party.Jobs);

        // After the save: the plan normalized, with the choice saved when the import saves one, else the suggestion.
        var mapping = choice.Save ? choice.Mapping : suggestion;
        var after = PlanSelection.For(plan, ImportColumns.Context(mapping, job, Level, slot)).Select(e => e.Id).ToHashSet();
        var review = raw.Entries.Where(e => PlanSelection.Applies(e, ImportColumns.Context(choice.Mapping, job, Level, slot))).Select(e => e.Id).ToHashSet();
        var overlay = s.Own.Select(e => e.Id).ToHashSet();
        if (choice.Mine != after.Count)
            problems.Add("I5 import Mine != overlay after the save", where, $"Mine={choice.Mine} overlay={after.Count}");
        if (!review.SetEquals(after))
            problems.Add("I5 review's 'My calls' != overlay after the save", where, $"review-only {string.Join(",", review.Except(after))} overlay-only {string.Join(",", after.Except(review))}");
        if (!after.SetEquals(overlay))
            problems.Add("I5 overlay after the import != automatic overlay", where, $"import-only {string.Join(",", after.Except(overlay))} automatic-only {string.Join(",", overlay.Except(after))}");
        if (choice.Missed > 0 && !c.GeneralNextToPairs)
            problems.Add("I5 import says calls are left out", where, $"Missed={choice.Missed} in {string.Join(",", choice.MissedIn)}");
    }

    /// <summary>I5: the CLI (no party) selects the overlay's calls, and with --assignee what the Plans tab saves for that column.</summary>
    private static void CheckCli(Problems problems, MitCue.Cli.CliContext cli, string file, MitPlan cliPlan, MitPlan plan, Job job, string? slot, Surface s, string where)
    {
        var tokens = new List<string> { file, "--job", job.ToString() };
        if (slot != null)
            tokens.AddRange(["--slot", slot]);
        var overlay = s.Own.Select(e => e.Id).ToHashSet();
        var auto = MitCue.Cli.PlayerSelection.Select(cli, MitCue.Cli.ArgParser.Parse(MitCue.Cli.Commands.PlanCommand.Spec, tokens), cliPlan).Entries.Select(e => e.Id).ToHashSet();
        if (!auto.SetEquals(overlay))
            problems.Add("I5 CLI != overlay", where, $"cli-only {string.Join(",", auto.Except(overlay))} overlay-only {string.Join(",", overlay.Except(auto))}");

        if (s.Mapping.AssigneeKey is not { } key)
            return;
        var column = plan.Assignees.First(a => string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase));
        var saved = new PlayerMapping { AssigneeKey = column.Key, Slot = Slots.TryNormalize(column.Slot, out var cs) ? cs : null }; // ConfigWindow.DrawColumnCombo
        var tab = PlanSelection.For(plan, ImportColumns.Context(saved, job, Level, slot)).Select(e => e.Id).ToHashSet();
        var picked = MitCue.Cli.PlayerSelection.Select(cli, MitCue.Cli.ArgParser.Parse(MitCue.Cli.Commands.PlanCommand.Spec, [.. tokens, "--assignee", key]), cliPlan).Entries.Select(e => e.Id).ToHashSet();
        if (!picked.SetEquals(tab))
            problems.Add("I5 CLI --assignee != Plans tab pick", where, $"--assignee \"{key}\": cli {picked.Count} plugin {tab.Count}");
    }

    // ---------------------------------------------------------------- the real runtime and the static's flows

    [Fact]
    public void TheRuntimeSelectsWhatTheSweepChecksAndTheStaticsFlowsAreUnchanged()
    {
        var problems = new List<string>();
        var now = 1.0;

        // I6: the static's automatic plans and columns, for every usual slot and party.
        {
            var config = new Configuration { ShowOtherAssignees = true };
            var parts = SessionManagerTests.NewManager(config);
            foreach (var slot in UsualSlots)
            {
                config.DefaultSlot = slot;
                config.Touch();
                foreach (var (job, planId, column, count) in new[] { (Job.WHM, NoirMit, "White Mage", 57), (Job.SCH, Lpdu, "H2 SCH", 31) })
                {
                    foreach (var party in PartiesFor(job))
                    {
                        parts.Manager.Update(Frame(job, now++, party.Jobs));
                        var m = parts.Manager;
                        var own = m.Session?.Scheduler.All.Select(r => r.Entry).Distinct().Count() ?? -1;
                        if (m.Plan?.Id != planId || !m.PlanIsAutomatic || m.Mapping?.AssigneeKey != column || own != count)
                            problems.Add($"I6 {job} slot={slot ?? "-"} party={party.Name}: plan {m.Plan?.DisplayName} column {m.Mapping?.AssigneeKey} own {own} (want {column} {count})");
                    }
                }

                parts.Plans.SetActive("top", Job.SCH, CtcSch); // Carry the Clear - SCH, chosen
                foreach (var party in PartiesFor(Job.SCH))
                {
                    parts.Manager.Update(Frame(Job.SCH, now++, party.Jobs));
                    var m = parts.Manager;
                    var own = m.Session?.Scheduler.All.Select(r => r.Entry).Distinct().Count() ?? -1;
                    if (m.Plan?.Id != CtcSch || m.Mapping?.AssigneeKey != "Scholar" || own != 69)
                        problems.Add($"I6 SCH Carry the Clear slot={slot ?? "-"} party={party.Name}: plan {m.Plan?.DisplayName} column {m.Mapping?.AssigneeKey} own {own}");
                }

                parts.Plans.SetActive("top", Job.SCH, null);
            }
        }

        // SessionManager and PreviewDriver select what the sweep's formula does (Overlay).
        {
            var config = new Configuration { ShowOtherAssignees = true };
            var parts = SessionManagerTests.NewManager(config);
            parts.Data.EnsureLoaded();
            parts.Plans.EnsureLoaded();
            var preview = new PreviewDriver(config, parts.Data, Kit.GameData(), parts.Plans, parts.Errors, ServiceFake.Of<IChatGui>(), () => { });
            Job[] jobs = [Job.WHM, Job.SCH, Job.AST, Job.WAR, Job.DRK, Job.PLD, Job.SAM, Job.BRD, Job.RDM, Job.PCT];
            foreach (var c in Synthetic().Where(c => c.Name is "role-sheet" or "tank-pairs" or "combined-first" or "per-phase").Prepend(BundledCases(parts.Plans).Single(c => c.Name == "lpdu")))
            {
                var plan = parts.Plans.Get(c.Raw.Id)?.Plan;
                if (plan == null)
                {
                    var copy = Clone(c.Raw);
                    Assert.True(parts.Plans.Save(copy, out var error), error);
                    plan = parts.Plans.Get(copy.Id)!.Plan;
                }

                foreach (var job in jobs)
                    parts.Plans.SetActive("top", job, plan.Id);
                foreach (var slot in UsualSlots)
                {
                    config.DefaultSlot = slot;
                    config.Touch();
                    foreach (var job in jobs)
                    {
                        foreach (var party in PartiesFor(job))
                        {
                            var where = $"{c.Name} {job} slot={slot ?? "-"} party={party.Name}";
                            var f = Overlay(parts.Plans, config, plan, job, party.Jobs);
                            parts.Manager.Update(Frame(job, now++, party.Jobs));
                            var session = parts.Manager.Session;
                            var own = session?.Scheduler.All.Select(r => r.Entry).Distinct().ToHashSet() ?? [];
                            var others = session?.Others?.All.Select(r => r.Entry).Distinct().ToHashSet() ?? [];
                            if (session == null || !own.SetEquals(f.Own) || !others.SetEquals(f.Partners))
                                problems.Add($"runtime {where}: session own {own.Count} others {others.Count}, formula own {f.Own.Count} others {f.Partners.Count}; {parts.Manager.Status}");

                            if (party.Name is "unknown" or "B")
                            {
                                Assert.True(preview.Start("top", null, 1122, job, party.Jobs, 0, out var message), message);
                                var previewOwn = preview.Session!.Scheduler.All.Select(r => r.Entry).Distinct().ToHashSet();
                                var previewOthers = preview.Session.Others?.All.Select(r => r.Entry).Distinct().ToHashSet() ?? [];
                                if (!previewOwn.SetEquals(f.Own) || !previewOthers.SetEquals(f.Partners))
                                    problems.Add($"preview {where}: own {previewOwn.Count} others {previewOthers.Count}, formula own {f.Own.Count} others {f.Partners.Count}; {message}");
                                preview.Stop();
                            }
                        }
                    }
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems.Take(50)));
    }

    // ---------------------------------------------------------------- surfaces

    /// <summary>The overlay: SessionManager.ComputeShape and Rebuild (PreviewDriver.Start for the current job).</summary>
    internal static Surface Overlay(PlanStore store, Configuration config, MitPlan plan, Job job, IReadOnlyList<Job> party)
    {
        var mapping = store.MappingFor(plan, job, party);
        var me = new PlayerContext(job, Level, mapping.Slot ?? Slots.ForJob(config.DefaultSlot, job), mapping.AssigneeKey);
        return new Surface(mapping, me, PlanSelection.For(plan, me).ToList(), PartnerSelection.For(plan, me, party));
    }

    private static FrameContext Frame(Job job, double now, IReadOnlyList<Job> party)
    {
        var frame = SessionManagerTests.InTop(now);
        frame.Job = job;
        frame.ClassJobId = (uint)job;
        frame.PartyJobMask = PartyJobs.Mask(party);
        return frame;
    }

    // ---------------------------------------------------------------- plans

    private static PlanEntry E(string id, string mechanic, string? column, Job? job, string? slot, uint action, string name) => new()
    {
        Id = id,
        Mechanic = mechanic,
        Offset = -3,
        Who = new WhoSpec { Assignee = column, Job = job, Slot = slot },
        Do = [PlanItem.ForAction(action, name)],
    };

    private static PlanEntry N(string id, string mechanic, string? column, Job? job, string? slot, string note) => new()
    {
        Id = id,
        Mechanic = mechanic,
        Offset = -3,
        Who = new WhoSpec { Assignee = column, Job = job, Slot = slot },
        Do = [PlanItem.ForNote(note)],
    };

    private static MitPlan P(string name, IEnumerable<Assignee> columns, IEnumerable<PlanEntry> entries) => new()
    {
        Id = "invariants-" + name,
        Name = name,
        Fight = "top",
        Assignees = columns.ToList(),
        Entries = entries.ToList(),
    };

    private static Assignee C(string key, string? slot = null, Job? job = null) => new() { Key = key, Slot = slot, Job = job };

    internal static List<Case> BundledCases(PlanStore store) =>
    [
        new Case { Name = "noirmit", Raw = Clone(store.Get(NoirMit)!.Plan) },
        new Case { Name = "ctc-sch", Raw = Clone(store.Get(CtcSch)!.Plan) },
        new Case { Name = "lpdu", Raw = Clone(store.Get(Lpdu)!.Plan), RoleOnly = new(["CM LB Tank", "CM Rep Tank"], StringComparer.OrdinalIgnoreCase) },
    ];

    internal static List<Case> Synthetic() =>
    [
        RoleSheet(), SlotJob(), PerPhase(), Combined(combinedFirst: true), Combined(combinedFirst: false), TankPairs(xlsx: false), TankPairs(xlsx: true), GeneralAndPairs(), AiYaml(),
    ];

    /// <summary>LPDU's P1/P2 shape: slot columns with job-less calls and calls per job, two jobs with calls in both ranged slots.</summary>
    private static Case RoleSheet() => new()
    {
        Name = "role-sheet",
        Raw = P(
            "role-sheet",
            [C("MT", "MT"), C("OT", "OT"), C("H1", "H1"), C("H2", "H2"), C("M1", "M1"), C("M2", "M2"), C("R1", "R1"), C("R2", "R2")],
            [
                E("mt-rep", "p1.loop_set1", "MT", null, "MT", 7535, "Reprisal"),
                E("mt-war", "p2.solar_ray", "MT", Job.WAR, "MT", 43, "Holmgang"),
                E("mt-drk", "p2.solar_ray", "MT", Job.DRK, "MT", 3638, "Living Dead"),
                E("ot-rep", "p1.loop_set3", "OT", null, "OT", 7535, "Reprisal"),
                E("ot-war", "p5.solar_ray_1", "OT", Job.WAR, "OT", 43, "Holmgang"),
                E("ot-drk", "p5.solar_ray_1", "OT", Job.DRK, "OT", 3638, "Living Dead"),
                N("h1-all", "p1.pantokrator", "H1", null, "H1", "Everything"),
                E("h1-whm", "p2.limitless", "H1", Job.WHM, "H1", 16536, "Temperance"),
                E("h1-ast", "p2.limitless", "H1", Job.AST, "H1", 16559, "Neutral Sect"),
                N("h2-all", "p1.pantokrator", "H2", null, "H2", "Everything"),
                E("h2-sch", "p2.limitless", "H2", Job.SCH, "H2", 25868, "Expedient"),
                E("h2-sge", "p2.limitless", "H2", Job.SGE, "H2", 24311, "Panhaima"),
                E("m1-feint", "p1.loop_set2", "M1", null, "M1", 7549, "Feint"),
                E("m2-feint", "p1.loop_set4", "M2", null, "M2", 7549, "Feint"),
                E("r1-brd", "p3.hello_world", "R1", Job.BRD, "R1", 7405, "Troubadour"),
                E("r1-rdm", "p1.pantokrator", "R1", Job.RDM, "R1", 25857, "Magick Barrier"),
                E("r1-pct", "p3.hello_world", "R1", Job.PCT, "R1", 34686, "Tempera Grassa"),
                E("r2-rdm", "p1.pantokrator", "R2", Job.RDM, "R2", 25857, "Magick Barrier"),
                E("r2-pct", "p3.critical_error", "R2", Job.PCT, "R2", 7560, "Addle"),
                E("r2-blm", "p3.critical_error", "R2", Job.BLM, "R2", 7560, "Addle"),
            ]),
    };

    private static Case SlotJob() => new()
    {
        Name = "slot-job",
        Raw = P(
            "slot-job",
            [C("MT WAR", "MT", Job.WAR), C("OT DRK", "OT", Job.DRK), C("H1 WHM", "H1", Job.WHM), C("H2 SCH", "H2", Job.SCH), C("M1 SAM", "M1", Job.SAM), C("R1 BRD", "R1", Job.BRD)],
            [
                E("mtwar-1", "p2.solar_ray", "MT WAR", Job.WAR, "MT", 43, "Holmgang"),
                E("otdrk-1", "p5.solar_ray_1", "OT DRK", Job.DRK, "OT", 3638, "Living Dead"),
                E("h1whm-1", "p2.limitless", "H1 WHM", Job.WHM, "H1", 16536, "Temperance"),
                E("h2sch-1", "p2.limitless", "H2 SCH", Job.SCH, "H2", 25868, "Expedient"),
                E("m1sam-1", "p1.loop_set2", "M1 SAM", Job.SAM, "M1", 7549, "Feint"),
                E("r1brd-1", "p3.hello_world", "R1 BRD", Job.BRD, "R1", 7405, "Troubadour"),
            ]),
    };

    /// <summary>Per-phase tabs headed differently for one seat ("SCH" in P1/P2, "H2 SCH" in P3), and the job-less H2 column.</summary>
    private static Case PerPhase() => new()
    {
        Name = "per-phase",
        Raw = P(
            "per-phase",
            [C("H2", "H2"), C("WHM", job: Job.WHM), C("SCH", job: Job.SCH), C("H1 WHM", "H1", Job.WHM), C("H2 SCH", "H2", Job.SCH), C("WAR", job: Job.WAR), C("MT WAR", "MT", Job.WAR)],
            [
                N("h2-all", "p1.pantokrator", "H2", null, "H2", "Everything"),
                E("p1-whm", "p1.pantokrator", "WHM", Job.WHM, null, 16536, "Temperance"),
                E("p1-sch", "p1.pantokrator", "SCH", Job.SCH, null, 188, "Sacred Soil"),
                E("p2-sch", "p2.limitless", "SCH", Job.SCH, null, 25868, "Expedient"),
                E("p3-whm", "p3.hello_world", "H1 WHM", Job.WHM, "H1", 16536, "Temperance"),
                E("p3-sch", "p3.hello_world", "H2 SCH", Job.SCH, "H2", 188, "Sacred Soil"),
                E("p1-war", "p1.loop_set1", "WAR", Job.WAR, null, 7535, "Reprisal"),
                E("p3-war", "p3.hello_world", "MT WAR", Job.WAR, "MT", 7535, "Reprisal"),
            ]),
    };

    /// <summary>Carry the Clear's shape: the job tabs and the combined tab (rekeyed), the combined tab first or last.</summary>
    private static Case Combined(bool combinedFirst)
    {
        Assignee[] perJob = [C("White Mage", job: Job.WHM), C("Scholar", job: Job.SCH)];
        Assignee[] combined = [C("TOP – WHM + SCH White Mage", job: Job.WHM), C("TOP – WHM + SCH Scholar", job: Job.SCH)];
        var entries = new List<PlanEntry>();
        foreach (var (key, job, prefix) in new[] { ("White Mage", Job.WHM, "w"), ("Scholar", Job.SCH, "s"), ("TOP – WHM + SCH White Mage", Job.WHM, "cw"), ("TOP – WHM + SCH Scholar", Job.SCH, "cs") })
        {
            var (action, actionName) = job == Job.WHM ? (16536u, "Temperance") : (188u, "Sacred Soil");
            entries.Add(E(prefix + "1", "p1.pantokrator", key, job, null, action, actionName));
            entries.Add(E(prefix + "2", "p2.limitless", key, job, null, action, actionName));
            entries.Add(E(prefix + "3", "p3.hello_world", key, job, null, action, actionName));
        }

        var name = combinedFirst ? "combined-first" : "combined-last";
        return new Case
        {
            Name = name,
            Raw = P(name, combinedFirst ? [.. combined, .. perJob] : [.. perJob, .. combined], entries),
            SameSeat = [["White Mage", "TOP – WHM + SCH White Mage"], ["Scholar", "TOP – WHM + SCH Scholar"]],
        };
    }

    /// <summary>LPDU's tank shape: MT/OT slot columns, tank pairs (slash or xlsx form), the Cosmo Memory role columns.</summary>
    private static Case TankPairs(bool xlsx)
    {
        string K(string pair, Job job) => (xlsx ? pair.Replace("/", string.Empty, StringComparison.Ordinal) : pair) + " " + job;
        var columns = new List<Assignee> { C("MT", "MT"), C("OT", "OT"), C("CM LB Tank"), C("CM Rep Tank") };
        var entries = new List<PlanEntry>
        {
            E("mt-rep", "p1.loop_set1", "MT", null, "MT", 7535, "Reprisal"),
            E("ot-rep", "p1.loop_set3", "OT", null, "OT", 7535, "Reprisal"),
            E("mt-war", "p1.pantokrator", "MT", Job.WAR, "MT", 7388, "Shake It Off"),
            E("ot-drk", "p1.pantokrator", "OT", Job.DRK, "OT", 16471, "Dark Missionary"),
        };
        var actions = new Dictionary<Job, (uint, string)> { [Job.WAR] = (43, "Holmgang"), [Job.DRK] = (3638, "Living Dead"), [Job.GNB] = (16152, "Superbolide"), [Job.PLD] = (30, "Hallowed Ground") };
        foreach (var (pair, a, b) in new[] { ("WAR/DRK", Job.WAR, Job.DRK), ("WAR/GNB", Job.WAR, Job.GNB), ("DRK/GNB", Job.DRK, Job.GNB), ("GNB/PLD", Job.GNB, Job.PLD) })
        {
            foreach (var job in new[] { a, b })
            {
                columns.Add(C(K(pair, job), job: job));
                entries.Add(E($"{pair}-{job}", "p5.solar_ray_3", K(pair, job), job, null, actions[job].Item1, actions[job].Item2));
            }
        }

        foreach (var job in new[] { Job.WAR, Job.DRK, Job.GNB, Job.PLD })
        {
            entries.Add(E($"cmlb-{job}", "p6.cosmo_memory", "CM LB Tank", job, null, 7531, "Rampart"));
            entries.Add(E($"cmrep-{job}", "p6.cosmo_memory", "CM Rep Tank", job, null, 7535, "Reprisal"));
        }

        var name = xlsx ? "tank-pairs-xlsx" : "tank-pairs";
        return new Case { Name = name, Raw = P(name, columns, entries), RoleOnly = new(["CM LB Tank", "CM Rep Tank"], StringComparer.OrdinalIgnoreCase) };
    }

    /// <summary>A general tank tab (WAR and DRK columns) next to two pair tabs.</summary>
    private static Case GeneralAndPairs() => new()
    {
        Name = "general-and-pairs",
        Raw = P(
            "general-and-pairs",
            [C("WAR", job: Job.WAR), C("DRK", job: Job.DRK), C("WAR/DRK WAR", job: Job.WAR), C("WAR/DRK DRK", job: Job.DRK), C("WAR/GNB WAR", job: Job.WAR), C("WAR/GNB GNB", job: Job.GNB)],
            [
                E("g-war", "p1.pantokrator", "WAR", Job.WAR, null, 7535, "Reprisal"),
                E("g-drk", "p1.pantokrator", "DRK", Job.DRK, null, 7535, "Reprisal"),
                E("wd-war", "p5.solar_ray_3", "WAR/DRK WAR", Job.WAR, null, 43, "Holmgang"),
                E("wd-drk", "p5.solar_ray_3", "WAR/DRK DRK", Job.DRK, null, 3638, "Living Dead"),
                E("wg-war", "p5.solar_ray_2", "WAR/GNB WAR", Job.WAR, null, 43, "Holmgang"),
                E("wg-gnb", "p5.solar_ray_2", "WAR/GNB GNB", Job.GNB, null, 16152, "Superbolide"),
            ]),
        GeneralNextToPairs = true,
    };

    /// <summary>AI YAML: job-less calls in job columns (given the column's job on read), slot synonyms.</summary>
    private static Case AiYaml() => new()
    {
        Name = "ai-yaml",
        Raw = ImportPipeline.ParseYaml("""
            format: mitcue-plan/1
            id: invariants-ai
            name: AI plan
            fight: top
            assignees:
              - {key: H1 WHM, slot: H1, job: WHM}
              - {key: H1 AST, slot: H1, job: AST}
              - {key: H2 SCH, slot: H2, job: SCH}
              - {key: D1, slot: D1}
              - {key: D3, slot: D3}
              - {key: T1, slot: T1}
            entries:
              - {id: a1, mechanic: p2.limitless, who: {slot: H1, assignee: H1 WHM}, do: [{action: 16536, name: Temperance}]}
              - {id: a2, mechanic: p2.limitless, who: {slot: H1, assignee: H1 AST}, do: [{action: 16559, name: Neutral Sect}]}
              - {id: a3, mechanic: p2.limitless, who: {slot: H2, assignee: H2 SCH}, do: [{action: 25868, name: Expedient}]}
              - {id: a4, mechanic: p1.loop_set2, who: {slot: D1, assignee: D1}, do: [{action: 7549, name: Feint}]}
              - {id: a5, mechanic: p3.hello_world, who: {slot: D3, job: BRD, assignee: D3}, do: [{action: 7405, name: Troubadour}]}
              - {id: a6, mechanic: p1.loop_set1, who: {slot: T1, assignee: T1}, do: [{action: 7535, name: Reprisal}]}
            """),
    };

    /// <summary>One composition split over per-phase tabs, through the importer ("WHM/SCH P1 WHM" and "WHM/SCH P3 WHM").</summary>
    internal static Case PerPhaseCompositions(MitCue.Data.CoreData data)
    {
        var document = new SheetDocument
        {
            Title = "Per-phase compositions",
            Tabs =
            [
                FixtureSheets.Tab("WHM/SCH P1", [["P1", "WHM", "SCH"], ["Pantokrator", "Temperance", "Soil"], ["Wave Cannon Kyrios", "Plenary", "Expedient"]]),
                FixtureSheets.Tab("WHM/SCH P3", [["P3", "WHM", "SCH"], ["Hello World", "Temperance", "Soil"], ["Critical Error", "Plenary", "Expedient"]]),
                FixtureSheets.Tab("WAR/DRK P5", [["P5", "WAR", "DRK"], ["Solar Ray 1", "Holmgang", "Living Dead"]]),
                FixtureSheets.Tab("WAR/DRK P6", [["P6", "WAR", "DRK"], ["Cosmo Memory", "Shake It Off", "Dark Missionary"]]),
                FixtureSheets.Tab("WAR/GNB P5", [["P5", "WAR", "GNB"], ["Solar Ray 2", "Holmgang", "Superbolide"]]),
                FixtureSheets.Tab("WAR/GNB P6", [["P6", "WAR", "GNB"], ["Cosmo Memory", "Shake It Off", "Heart of Light"]]),
            ],
        };
        var plan = ImportPipeline.Import(document, new ImportOptions { Fight = data.GetFight("top")!, Actions = data.Actions, Aliases = data.Aliases!, ImportedDate = "2026-11-11" }).Plan;
        plan.Fight = "top";
        return new Case { Name = "per-phase-compositions", Raw = plan };
    }

    private static MitPlan Clone(MitPlan plan) => JsonSerializer.Deserialize<MitPlan>(JsonSerializer.Serialize(plan))!;

    private static MitPlan Normalized(MitPlan plan)
    {
        var copy = Clone(plan);
        PlanNormalizer.Normalize(copy);
        return copy;
    }

    // ---------------------------------------------------------------- the oracle

    private static Assignee? ColumnOf(MitPlan plan, PlanEntry e) =>
        e.Who.Assignee is { } key ? plan.Assignees.FirstOrDefault(a => string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase)) : null;

    private static Job? JobOf(MitPlan plan, PlanEntry e) =>
        e.Who.Job is { } j && j != Job.None ? j : ColumnOf(plan, e)?.Job is { } cj && cj != Job.None ? cj : null;

    private static string? SlotOf(MitPlan plan, PlanEntry e) =>
        Slots.TryNormalize(e.Who.Slot, out var s) ? s : Slots.TryNormalize(ColumnOf(plan, e)?.Slot, out s) ? s : null;

    private static IReadOnlyList<Job> CompositionOf(MitPlan plan, PlanEntry e) =>
        ColumnOf(plan, e) is { } c && LayoutDetector.JobsFromTabName(c.Key) is { Count: >= 2 } jobs ? jobs : [];

    private static int GroupOf(Role role) => role switch
    {
        Role.Tank => 1,
        Role.Healer => 2,
        Role.Melee or Role.PhysicalRanged or Role.Caster => 3,
        _ => 0,
    };

    /// <summary>The player's seat: the usual slot of the job's role, else the one slot the plan gives the job's calls and columns.</summary>
    private static string? SeatSlot(MitPlan plan, Job job, string? usual)
    {
        if (Slots.ForJob(usual, job) is { } s)
            return s;
        var slots = plan.Entries.Where(e => JobOf(plan, e) == job).Select(e => SlotOf(plan, e))
            .Concat(plan.Assignees.Where(a => a.Job == job).Select(a => Slots.TryNormalize(a.Slot, out var cs) ? cs : null))
            .OfType<string>().Distinct().ToList();
        return slots.Count == 1 ? slots[0] : null;
    }

    /// <summary>The composition the player plays: the job's first composition column whose other jobs are in the party, else the first.</summary>
    private static IReadOnlyList<Job>? ChosenComposition(MitPlan plan, Job job, IReadOnlyList<Job> party)
    {
        var compositions = plan.Assignees.Where(a => a.Job == job).Select(a => LayoutDetector.JobsFromTabName(a.Key)).Where(j => j.Count >= 2).ToList();
        if (compositions.Count == 0)
            return null;
        return party.Count > 0 && compositions.FirstOrDefault(c => c.Where(j => j != job).All(party.Contains)) is { } played ? played : compositions[0];
    }

    private static IEnumerable<string> Twice(IEnumerable<PlanEntry> entries) =>
        entries.SelectMany(e => e.Do.SelectMany(i => i.ActionIds()).Distinct().Select(id => (e.Mechanic, Id: id, Entry: e)))
            .GroupBy(x => (x.Mechanic, x.Id))
            .Where(g => g.Select(x => x.Entry).Distinct().Count() > 1)
            .Select(g => $"{g.Key.Mechanic} {g.Key.Id}: {string.Join(" ", g.Select(x => Who(x.Entry)))}");

    private static string Who(PlanEntry e) => $"{e.Id}[{e.Who.Assignee ?? "-"}|{e.Who.Slot ?? "-"}|{e.Who.Job?.ToString() ?? "-"}]";

    /// <summary>Violations, grouped by kind for the failure message.</summary>
    private sealed class Problems
    {
        private readonly List<(string Kind, string Where, string Detail)> items = [];

        public int Count => this.items.Count;

        public void Add(string kind, string where, string detail) => this.items.Add((kind, where, detail));

        public override string ToString()
        {
            var text = new StringBuilder();
            foreach (var kind in this.items.GroupBy(i => i.Kind))
            {
                text.AppendLine($"== {kind.Key}: {kind.Count()}");
                foreach (var item in kind.Take(10))
                    text.AppendLine($"  {item.Where}: {item.Detail}");
            }

            return text.ToString();
        }
    }
}
