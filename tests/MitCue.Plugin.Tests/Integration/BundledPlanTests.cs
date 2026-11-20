using Dalamud.Plugin.Services;
using MitCue.Core;
using MitCue.Core.Import;
using MitCue.Core.Plans;
using MitCue.Import;
using MitCue.Plans;
using MitCue.PluginTests.Support;
using MitCue.Runtime;

namespace MitCue.PluginTests.Integration;

/// <summary>
/// The bundled plans as the plugin loads them (embedded in MitCue.Core; three for TOP, LPDU's for TEA): which plan and
/// column each player gets without choosing, share codes of bundled plans, and the checks of plans that skip the review.
/// Regression tests for the final review of the integrated system.
/// </summary>
public class BundledPlanTests
{
    private const string NoirMitWhm = "335b6c4f-3684-55e0-a8d0-3e78724ef200";
    private const string CarryTheClearSch = "5b622e32-04ae-53de-a4f8-f66e4c0aae0c";
    private const string CarryTheClearWhmSch = "ded79070-7479-5023-80ca-5e3ef257c99a"; // shipped in 0.1.0.1 only
    private const string Lpdu = "1f75e1e8-7321-566c-ae86-8568019fe914";
    private const string LpduTea = "a5c2ab3b-a8c7-53e7-8a49-74e85a0777d7";

    private static SessionManagerTests.Parts Live(Job job, string? slot, Configuration? config = null)
    {
        config ??= new Configuration();
        config.DefaultSlot = slot;
        var parts = SessionManagerTests.NewManager(config);
        parts.Manager.Update(Frame(job, 1));
        return parts;
    }

    private static FrameContext Frame(Job job, double now, ulong party = 0)
    {
        var frame = SessionManagerTests.InTop(now);
        frame.Job = job;
        frame.ClassJobId = (uint)job;
        frame.PartyJobMask = party;
        return frame;
    }

    private static List<string> OwnEntryIds(SessionManagerTests.Parts parts) =>
        parts.Manager.Session!.Scheduler.All.Select(r => r.Entry.Id).Distinct().ToList();

    /// <summary>The live session in TEA as <paramref name="job"/> with the usual slot <paramref name="slot"/>.</summary>
    private static SessionManagerTests.Parts LiveInTea(Job job, string? slot, Configuration? config = null)
    {
        config ??= new Configuration();
        config.DefaultSlot = slot;
        var parts = SessionManagerTests.NewManager(config);
        parts.Manager.Update(SessionManagerTests.InTea(job));
        return parts;
    }

    private static List<PlanEntry> OwnEntries(SessionManagerTests.Parts parts) =>
        parts.Manager.Session!.Scheduler.All.Select(r => r.Entry).Distinct().ToList();

    /// <summary>An entry's items: action names, notes as "note:text".</summary>
    private static List<string> Names(PlanEntry e) => e.Do.Select(i => i.Note is { } note ? "note:" + note : i.Name ?? "?").ToList();

    [Fact]
    public void TheFourPlansAreBundled()
    {
        var parts = SessionManagerTests.NewManager();
        parts.Plans.EnsureLoaded();
        (string, string)[] expected = [(NoirMitWhm, "NoirMit - WHM"), (CarryTheClearSch, "Carry the Clear - SCH"), (Lpdu, "LPDU TOP Mitigation"), (LpduTea, "LPDU TEA Heal/Mit")];
        Assert.Equal(expected.Order(), parts.Plans.All.Select(p => (p.Id, p.DisplayName)).Order());
        Assert.All(parts.Plans.All, p => Assert.Equal(PlanOrigin.Bundled, p.Origin));
        Assert.Empty(parts.Errors.Snapshot());
    }

    [Theory]
    [InlineData(Job.WHM, "H1", NoirMitWhm, "White Mage", 57)]
    [InlineData(Job.WHM, null, NoirMitWhm, "White Mage", 57)]
    [InlineData(Job.SCH, "H2", Lpdu, "H2 SCH", 31)]
    [InlineData(Job.SCH, null, Lpdu, "H2 SCH", 31)]
    public void EachHealerGetsTheirSheetWithoutChoosing(Job job, string? slot, string planId, string? column, int entries)
    {
        // Final review finding: DESIGN D2 has the H2 Scholar follow LPDU, but the automatic choice (the first plan by name
        // with a column for the job) gave them "Carry the Clear – WHM + SCH", whatever slot they set.
        var parts = Live(job, slot);

        Assert.True(parts.Manager.PlanIsAutomatic);
        Assert.Equal(planId, parts.Manager.Plan?.Id);
        Assert.Equal(column, parts.Manager.Mapping?.AssigneeKey);
        Assert.Equal(entries, OwnEntryIds(parts).Count);
        Assert.StartsWith("Ready:", parts.Manager.Status, StringComparison.Ordinal);
        Assert.Empty(parts.Manager.Session!.Scheduler.Problems);
    }

    [Fact]
    public void ThePreviewChoosesTheSamePlan()
    {
        var config = new Configuration { DefaultSlot = "H2" };
        var parts = SessionManagerTests.NewManager(config);
        var preview = new PreviewDriver(config, parts.Data, Kit.GameData(), parts.Plans, parts.Errors, ServiceFake.Of<IChatGui>(), () => { });

        Assert.True(preview.Start("top", null, 1122, Job.SCH, null, 0, out var message), message);
        Assert.Contains("\"LPDU TOP Mitigation\" as SCH", message, StringComparison.Ordinal);
        Assert.Contains("31 entries are yours", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, " Your slot is a guess: MT (the plan has WAR calls in MT and OT).")]
    [InlineData("OT", null)]
    public void ThePreviewSaysWhenTheSlotIsAGuess(string? slot, string? hint)
    {
        var config = new Configuration { DefaultSlot = slot };
        var parts = SessionManagerTests.NewManager(config);
        var preview = new PreviewDriver(config, parts.Data, Kit.GameData(), parts.Plans, parts.Errors, ServiceFake.Of<IChatGui>(), () => { });

        Assert.True(preview.Start("tea", null, 887, Job.WAR, null, 0, out var message), message);

        Assert.Contains("\"LPDU TEA Heal/Mit\" as WAR", message, StringComparison.Ordinal);
        if (hint != null)
            Assert.Contains(hint, message, StringComparison.Ordinal);
        else
            Assert.DoesNotContain("Your slot", message, StringComparison.Ordinal);
    }

    [Fact]
    public void CarryTheClearSchIsBundledButTheScholarKeepsLpduUntilTheyChooseIt()
    {
        // Carry the Clear - SCH sorts before LPDU and has a Scholar column, but BundledDefaults keeps the Scholar on LPDU.
        var parts = Live(Job.SCH, "H2");
        Assert.True(parts.Manager.PlanIsAutomatic);
        Assert.Equal(Lpdu, parts.Manager.Plan?.Id);

        parts.Plans.SetActive("top", Job.SCH, CarryTheClearSch);
        parts.Manager.Update(Frame(Job.SCH, 2));
        Assert.False(parts.Manager.PlanIsAutomatic);
        Assert.Equal(CarryTheClearSch, parts.Manager.Plan?.Id);
        Assert.Equal("Scholar", parts.Manager.Mapping?.AssigneeKey);
        Assert.Equal(69, OwnEntryIds(parts).Count);

        // No other job gets it automatically.
        foreach (var job in new[] { Job.WHM, Job.AST, Job.SGE, Job.WAR, Job.MCH })
            Assert.NotEqual(CarryTheClearSch, parts.Plans.ActivePlanFor("top", job)?.Id);
    }

    [Fact]
    public void AChoiceOfTheRemovedCombinedPlanFallsBackToTheBundledDefault()
    {
        // "Carry the Clear – WHM + SCH" shipped in 0.1.0.1 and is gone: a player who chose it gets the automatic plan.
        var config = new Configuration();
        config.ActivePlans[PlanStore.ActiveKey("top", Job.SCH)] = CarryTheClearWhmSch;
        config.ActivePlans[PlanStore.ActiveKey("top", Job.WHM)] = CarryTheClearWhmSch;
        var parts = Live(Job.SCH, "H2", config);

        Assert.True(parts.Manager.PlanIsAutomatic);
        Assert.Equal(Lpdu, parts.Manager.Plan?.Id);
        Assert.Equal(NoirMitWhm, parts.Plans.ActivePlanFor("top", Job.WHM, out var automatic)?.Id);
        Assert.True(automatic);
        Assert.Null(parts.Plans.Get(CarryTheClearWhmSch));
    }

    [Fact]
    public void AnImportedPlanReplacesTheBundledPlanOnlyWhenChosen()
    {
        // Docs review finding: an imported plan (or a share code) used to win the automatic choice over the bundled
        // plans, so saving one with "Make it my active plan" unticked, or taking the co-healer's share code to look at
        // it, silently switched the player's plan.
        var parts = SessionManagerTests.NewManager(new Configuration { DefaultSlot = "H2" });
        parts.Plans.EnsureLoaded();
        var own = new MitPlan { Id = "own", Name = "Aa my static's plan", Fight = "top", Entries = [Kit.Entry("p1.pantokrator", -3, Kit.SacredSoil, "Sacred Soil", Job.SCH)] };
        Assert.True(parts.Plans.Save(own, out var error), error);

        Assert.Equal(Lpdu, parts.Plans.ActivePlanFor("top", Job.SCH, out var automatic)?.Id);
        Assert.True(automatic);

        parts.Plans.SetActive("top", Job.SCH, "own");
        Assert.Equal("own", parts.Plans.ActivePlanFor("top", Job.SCH, out automatic)?.Id);
        Assert.False(automatic);
    }

    [Fact]
    public void WhereNoBundledPlanCoversTheJobTheImportedPlanIsTheAutomaticOne()
    {
        // DMU ships no plan; TEA's bundled plan covers the Scholar, so an imported TEA plan is theirs only when chosen.
        var parts = SessionManagerTests.NewManager();
        parts.Plans.EnsureLoaded();
        var dmu = new MitPlan { Id = "dmu-own", Name = "My DMU plan", Fight = "dmu", Entries = [Kit.Entry("p1.pull", 0, Kit.SacredSoil, "Sacred Soil", Job.SCH)] };
        var tea = new MitPlan { Id = "tea-own", Name = "Aa my TEA plan", Fight = "tea", Entries = [Kit.Entry("p1.pull", 0, Kit.SacredSoil, "Sacred Soil", Job.SCH)] };
        Assert.True(parts.Plans.Save(dmu, out var error), error);
        Assert.True(parts.Plans.Save(tea, out error), error);

        Assert.Equal("dmu-own", parts.Plans.ActivePlanFor("dmu", Job.SCH, out var automatic)?.Id);
        Assert.True(automatic);
        Assert.Equal(LpduTea, parts.Plans.ActivePlanFor("tea", Job.SCH, out automatic)?.Id);
        Assert.True(automatic);
    }

    [Fact]
    public void AnUpdateKeepsThePlanEachJobHadAutomatically()
    {
        // Review finding (C10): settings of MitCue 0.1.0.6 (version 1) with the static's TEA plan, imported by share code
        // with the box unticked: no TEA plan was bundled, so it was "your active plan for TEA as SCH". After the update LPDU
        // TEA took its place, unasked, for every job both name. Likewise TOP's Warrior: version 1 ranked a share code of a
        // bundled plan among the player's own plans, so "Aa my TOP plan" was theirs, not the copy of LPDU TOP.
        var dir = Kit.TempDirectory();
        var writer = new PlanStore(dir, new Configuration(), () => { }, Kit.Errors());
        writer.EnsureLoaded();
        MitPlan[] own =
        [
            new() { Id = "tea-own", Name = "Static TEA", Fight = "tea", Entries = [Kit.Entry("p1.pull", 0, Kit.SacredSoil, "Sacred Soil", Job.SCH), Kit.Entry("p1.pull", 0, Kit.Temperance, "Temperance", Job.WHM)] },
            new() { Id = "tea-zz", Name = "Zz other TEA plan", Fight = "tea", Entries = [Kit.Entry("p1.pull", 0, Kit.SacredSoil, "Sacred Soil", Job.SCH), Kit.Entry("p1.pull", 0, Kit.Reprisal, "Reprisal", Job.PLD)] },
            new() { Id = "top-own", Name = "Aa my TOP plan", Fight = "top", Entries = [Kit.Entry("p1.pantokrator", 0, Kit.Reprisal, "Reprisal", Job.WAR)] },
        ];
        foreach (var plan in own)
            Assert.True(writer.Save(plan, out var error), error);
        Assert.True(writer.ImportShareCode(PlanSerializer.ToShareCode(writer.Get(Lpdu)!.Plan), out _, out var message), message);

        var saves = 0;
        var config = new Configuration { Version = 1, ActivePlans = { ["tea/WHM"] = string.Empty, ["tea/SGE"] = "deleted-plan" } };
        var store = new PlanStore(dir, config, () => saves++, Kit.Errors());
        store.EnsureLoaded();

        Assert.Equal((2, 1), (config.Version, saves));
        (string Fight, Job Job, string? Plan, bool Automatic)[] expected =
        [
            ("tea", Job.SCH, "tea-own", false), // the first own plan by name
            ("tea", Job.PLD, "tea-zz", false),
            ("tea", Job.WHM, null, false), // "no plan" stays
            ("tea", Job.SGE, LpduTea, true), // no own plan names the Sage
            ("tea", Job.WAR, LpduTea, true),
            ("top", Job.WAR, "top-own", false),
            ("top", Job.WHM, NoirMitWhm, true),
            ("top", Job.SCH, Lpdu, true), // BundledDefaults: the copy of LPDU TOP, as before
        ];
        Assert.Equal(expected, expected.Select(e => (e.Fight, e.Job, store.ActivePlanFor(e.Fight, e.Job, out var automatic)?.Id, automatic)));

        // Once: the store saved version 2, so a reload pins nothing more, and new settings never pin (an import saved later
        // is active only when chosen).
        store.Reload();
        Assert.Equal(1, saves);
        var fresh = new PlanStore(dir, new Configuration(), () => { }, Kit.Errors());
        fresh.EnsureLoaded();
        Assert.Equal(LpduTea, fresh.ActivePlanFor("tea", Job.SCH)?.Id);
        Assert.Equal(Lpdu, fresh.ActivePlanFor("top", Job.WAR)?.Id);
    }

    [Fact]
    public void AnUpdateWaitsForAPlanFileItCannotReadYet()
    {
        // Review finding (F14): version 1 settings where the Scholar chose "Static TEA", also the White Mage's automatic
        // plan; "Zz other TEA plan" names the Scholar and the Paladin. On the first load after the update another program
        // holds Static TEA's file. Migrating then lost the White Mage's plan and replaced the Scholar's choice by "Zz", for
        // good. A file that never parses (0.1.0.6 could not use it either) is no reason to wait.
        var dir = Kit.TempDirectory();
        var writer = new PlanStore(dir, new Configuration(), () => { }, Kit.Errors());
        writer.EnsureLoaded();
        MitPlan[] own =
        [
            new() { Id = "tea-own", Name = "Static TEA", Fight = "tea", Entries = [Kit.Entry("p1.pull", 0, Kit.SacredSoil, "Sacred Soil", Job.SCH), Kit.Entry("p1.pull", 0, Kit.Temperance, "Temperance", Job.WHM)] },
            new() { Id = "tea-zz", Name = "Zz other TEA plan", Fight = "tea", Entries = [Kit.Entry("p1.pull", 0, Kit.SacredSoil, "Sacred Soil", Job.SCH), Kit.Entry("p1.pull", 0, Kit.Reprisal, "Reprisal", Job.PLD)] },
        ];
        foreach (var plan in own)
            Assert.True(writer.Save(plan, out var error), error);
        File.WriteAllText(Path.Combine(writer.PlansDirectory, "broken.json"), "{ not a plan");

        var saves = 0;
        var config = new Configuration { Version = 1, ActivePlans = { ["tea/SCH"] = "tea-own" } };
        var errors = Kit.Errors();
        var store = new PlanStore(dir, config, () => saves++, errors);
        using (new FileStream(Path.Combine(writer.PlansDirectory, "tea-own.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            store.EnsureLoaded();
            Assert.Equal((1, 0), (config.Version, saves));
            Assert.Equal(["tea/SCH=tea-own"], config.ActivePlans.Select(kv => $"{kv.Key}={kv.Value}"));
            Assert.Equal(2, errors.Count); // the locked file and the broken one are in Problems

            store.Reload(); // "Reload plans" while the file is still held: still waiting
            Assert.Equal((1, 0), (config.Version, saves));
        }

        var next = new PlanStore(dir, config, () => saves++, Kit.Errors()); // the next start: the file reads again
        next.EnsureLoaded();
        Assert.Equal((2, 1), (config.Version, saves));
        (Job Job, string Plan)[] expected = [(Job.SCH, "tea-own"), (Job.WHM, "tea-own"), (Job.PLD, "tea-zz")];
        Assert.Equal(expected, expected.Select(e => (e.Job, next.ActivePlanFor("tea", e.Job, out var automatic) is { } p && !automatic ? p.Id : "?")));
    }

    [Fact]
    public void WhileTheUpdateWaitsEachJobKeepsThePlanVersion1GaveIt()
    {
        // While the migration waits for "Zz other TEA plan" (Scholar, Paladin), which another program holds, the settings
        // are still version 1's: "Static TEA" (Scholar, White Mage) reads, and LPDU TEA must not take its place for these
        // starts, however many there are. The Paladin's plan cannot be read: as with version 1, the Paladin gets what is
        // left, LPDU TEA. Once the file reads, the migration pins each job's plan.
        var dir = Kit.TempDirectory();
        var writer = new PlanStore(dir, new Configuration(), () => { }, Kit.Errors());
        writer.EnsureLoaded();
        MitPlan[] own =
        [
            new() { Id = "tea-own", Name = "Static TEA", Fight = "tea", Entries = [Kit.Entry("p1.pull", 0, Kit.SacredSoil, "Sacred Soil", Job.SCH), Kit.Entry("p1.pull", 0, Kit.Temperance, "Temperance", Job.WHM)] },
            new() { Id = "tea-zz", Name = "Zz other TEA plan", Fight = "tea", Entries = [Kit.Entry("p1.pull", 0, Kit.SacredSoil, "Sacred Soil", Job.SCH), Kit.Entry("p1.pull", 0, Kit.Reprisal, "Reprisal", Job.PLD)] },
        ];
        foreach (var plan in own)
            Assert.True(writer.Save(plan, out var error), error);

        var config = new Configuration { Version = 1 };
        var store = new PlanStore(dir, config, () => { }, Kit.Errors());
        using (new FileStream(Path.Combine(writer.PlansDirectory, "tea-zz.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            store.EnsureLoaded();

        Assert.Equal(1, config.Version);
        (Job Job, string Plan)[] waiting = [(Job.SCH, "tea-own"), (Job.WHM, "tea-own"), (Job.PLD, LpduTea), (Job.SGE, LpduTea)];
        Assert.Equal(waiting, waiting.Select(e => (e.Job, store.ActivePlanFor("tea", e.Job, out var automatic) is { } p && automatic ? p.Id : "?")));

        store.Reload(); // "Reload plans": the file reads again
        Assert.Equal(2, config.Version);
        (Job Job, string Plan, bool Automatic)[] migrated = [(Job.SCH, "tea-own", false), (Job.WHM, "tea-own", false), (Job.PLD, "tea-zz", false), (Job.SGE, LpduTea, true)];
        Assert.Equal(migrated, migrated.Select(e => (e.Job, store.ActivePlanFor("tea", e.Job, out var automatic)?.Id ?? "?", automatic)));
    }

    [Theory]
    [InlineData(Job.WHM, "H1", "White Mage", "H1", 25)]
    [InlineData(Job.WHM, null, "White Mage", null, 25)]
    [InlineData(Job.SCH, "H2", "Scholar", "H2", 32)]
    [InlineData(Job.SCH, null, "Scholar", null, 32)]
    [InlineData(Job.SGE, "H2", "Sage", "H2", 30)]
    [InlineData(Job.AST, "H1", "Astrologian", "H1", 26)]
    [InlineData(Job.WAR, "MT", "Tank 1", "MT", 16)]
    [InlineData(Job.PLD, "MT", "Tank 1", "MT", 19)]
    [InlineData(Job.PLD, "OT", "Tank 2", "OT", 20)]
    [InlineData(Job.GNB, "OT", "Tank 2", "OT", 15)]
    [InlineData(Job.MNK, "M1", "Melee 1", "M1", 11)]
    [InlineData(Job.BRD, "R1", null, "R1", 10)]
    [InlineData(Job.MCH, null, null, null, 13)]
    [InlineData(Job.DNC, "R2", null, "R2", 7)]
    [InlineData(Job.RDM, "R2", null, "R2", 6)]
    [InlineData(Job.BLM, null, null, null, 6)]
    public void OnTeaEveryJobWithCallsGetsLpduTeaWithoutChoosing(Job job, string? slot, string? column, string? mappedSlot, int entries)
    {
        // LPDU's TEA sheet has a column per healer job, seats for the tanks and melee ("Tank 1" = MT), a column per ranged
        // role ("Phys Range", "Caster": calls per job) and "Extras" (calls for the jobs they name, in no column). It is
        // the only TEA plan, so it is the automatic one of every job it names, with no PlanStore.BundledDefaults entry.
        var parts = LiveInTea(job, slot);

        Assert.True(parts.Manager.PlanIsAutomatic);
        Assert.Equal(LpduTea, parts.Manager.Plan?.Id);
        var mapping = parts.Manager.Mapping!;
        Assert.True(mapping.IsSuggestion);
        Assert.Equal((column, mappedSlot), (mapping.AssigneeKey, mapping.Slot));
        Assert.Equal(entries, OwnEntryIds(parts).Count);
        Assert.StartsWith("Ready:", parts.Manager.Status, StringComparison.Ordinal);
        Assert.Empty(parts.Manager.Session!.Scheduler.Problems);
        Assert.All(OwnEntries(parts), e => Assert.True(e.Who.Job is null || e.Who.Job == job, $"{e.Id} is {e.Who.Job}'s"));

        // The core's suggestion and the import window's choice (an in-game import of the sheet) are the same column.
        var plan = parts.Manager.Plan!.Plan;
        var suggestion = ColumnSuggestion.Suggest(plan, job, slot);
        Assert.Equal((column, mappedSlot), (suggestion.AssigneeKey, suggestion.Slot));
        var choice = ImportColumns.Choose(plan, job, 80, slot);
        Assert.Equal((column, mappedSlot), (choice.Mapping.AssigneeKey, choice.Mapping.Slot));
        Assert.Equal(entries, choice.Mine);
        Assert.Equal(0, choice.Missed);
    }

    [Theory]
    [InlineData(Job.PLD, "MT")]
    [InlineData(Job.PLD, "OT")]
    [InlineData(Job.PLD, null)]
    public void OnTeaThePaladinGetsTheExtrasWingsInEitherTankSeat(Job job, string? slot)
    {
        // "PLD Wings" under "Extras" is Passage of Arms for whichever tank plays Paladin: an entry with no column.
        var wings = OwnEntries(LiveInTea(job, slot)).Where(e => Names(e).SequenceEqual(["Passage of Arms"])).ToList();

        Assert.Equal(["'LL/BJCC'!M11", "'LL/BJCC'!M36", "'LL/BJCC'!M54", "'Alex Prime/PA'!M23", "'Alex Prime/PA'!M47"], wings.Select(e => e.Src));
        Assert.All(wings, e => Assert.Equal((Job.PLD, (string?)null, (string?)null), (e.Who.Job!.Value, e.Who.Slot, e.Who.Assignee)));
    }

    [Fact]
    public void OnTeaTheExtrasCallsReachOnlyTheJobsTheyName()
    {
        var mch = OwnEntries(LiveInTea(Job.MCH, "R1"));
        var dismantle = mch.Where(e => Names(e).SequenceEqual(["Dismantle"])).ToList();
        Assert.Equal(
            [("'LL/BJCC'!M19", "on LL"), ("'LL/BJCC'!M50", "on BJ"), ("'Alex Prime/PA'!M15", null), ("'Alex Prime/PA'!M39", null), ("'Alex Prime/PA'!M45", null)],
            dismantle.Select(e => (e.Src, e.Note)));
        Assert.Contains(mch, e => Names(e).SequenceEqual(["Tactician"]) && e.Src == "'LL/BJCC'!K13" && e.Optional); // "(Party Mit)" with "(^ if MCH)" below it

        var brd = OwnEntries(LiveInTea(Job.BRD, "R1"));
        Assert.DoesNotContain(brd, e => Names(e).Contains("Dismantle") || e.Src == "'LL/BJCC'!K13");
        Assert.Equal(3, brd.Count(e => Names(e).SequenceEqual(["Nature's Minne"]) && e.Optional)); // "(Minne/Mantra)"
        Assert.Equal(3, OwnEntries(LiveInTea(Job.MNK, "M2")).Count(e => Names(e).SequenceEqual(["Mantra"]) && e.Optional));

        var war = OwnEntries(LiveInTea(Job.WAR, "MT"));
        Assert.DoesNotContain(war, e => Names(e).Contains("Passage of Arms") || Names(e).Contains("Dismantle"));

        // "Use Personals" is everyone's.
        foreach (var job in new[] { Job.WAR, Job.SGE, Job.MCH, Job.RDM, Job.MNK })
            Assert.Single(OwnEntries(LiveInTea(job, null)), e => e.Src == "'Alex Prime/PA'!M43" && Names(e).SequenceEqual(["note:Use Personals"]));
    }

    [Fact]
    public void OnTeaAMeleeJobWhoseCallsAreTheSeatsChoosesThePlan()
    {
        // DESIGN §7: a plan whose calls for the player's seat are only in job-less slot columns ("Melee 1") is not chosen
        // automatically. The Ninja chooses it; the seat's calls then come with the usual slot.
        var parts = LiveInTea(Job.NIN, "M1");
        Assert.Null(parts.Plans.ActivePlanFor("tea", Job.NIN, out var automatic));
        Assert.True(automatic);

        parts.Plans.SetActive("tea", Job.NIN, LpduTea);
        parts.Manager.Update(SessionManagerTests.InTea(Job.NIN, 2));
        Assert.Equal(LpduTea, parts.Manager.Plan?.Id);
        Assert.Equal(("Melee 1", "M1"), (parts.Manager.Mapping?.AssigneeKey, parts.Manager.Mapping?.Slot));
        var own = OwnEntries(parts);
        Assert.Equal(8, own.Count); // 7 Feints and "Use Personals"
        Assert.Equal(7, own.Count(e => Names(e).SequenceEqual(["Feint"]) && e.Who.Slot == "M1" && e.Who.Job is null));
    }

    /// <summary>A TEA frame as <paramref name="job"/> in a party with both tanks, a Monk and the casters.</summary>
    private static FrameContext InTeaParty(Job job, double now = 1)
    {
        var frame = SessionManagerTests.InTea(job, now);
        frame.PartyJobMask = PartyJobs.Mask([Job.WAR, Job.DRK, Job.WHM, Job.SCH, Job.MNK, Job.DRG, Job.BRD, Job.BLM]);
        return frame;
    }

    [Theory]
    [InlineData(Job.DRK, null, "set \"My usual party slot\" in /mitcue > General, or choose \"Tank 2\" under \"My column\" in /mitcue > Plans")]
    [InlineData(Job.WAR, null, "set \"My usual party slot\" in /mitcue > General, or choose \"Tank 2\" under \"My column\" in /mitcue > Plans")]
    [InlineData(Job.DRK, "H1", "choose \"Tank 2\" under \"My column\" in /mitcue > Plans")] // a healer main keeps H1
    public void OnTeaATankWithoutASlotIsToldTank1IsAGuess(Job job, string? slot, string how)
    {
        // Review findings B02/C11: every tank job has calls in "Tank 1" (MT) and "Tank 2" (OT), and the party cannot tell
        // the tanks apart, so two tanks without a usual slot both followed Tank 1 (both the MT's Reprisals, none of Tank
        // 2's), and nothing said so. The choice stays; the status and, once per plan and job, chat say it is a guess.
        var config = new Configuration { DefaultSlot = slot };
        var parts = SessionManagerTests.NewManager(config);
        parts.Manager.Update(InTeaParty(job));

        Assert.Equal(((string?)null, "MT"), (parts.Manager.Mapping?.AssigneeKey, parts.Manager.Mapping?.Slot));
        var own = OwnEntries(parts);
        Assert.Contains(own, e => e.Who.Assignee == "Tank 1" && e.Who.Job is null && Names(e).SequenceEqual(["Reprisal"])); // the MT's
        Assert.DoesNotContain(own, e => e.Who.Assignee == "Tank 2");
        var hint = $"Your slot is a guess: MT (the plan has {job} calls in MT and OT). If you play OT, {how}.";
        Assert.Equal($"Ready: LPDU TEA Heal/Mit ({own.Count} of {parts.Manager.Plan!.Plan.Entries.Count} entries are yours). {hint}", parts.Manager.Status);
        Assert.Equal([$"TEA, \"LPDU TEA Heal/Mit\" as {job}: {hint}"], parts.Chat.FirstArgs("Print"));

        // Once: the session built again (Debug > Reload data, a zone change) prints nothing more.
        parts.Manager.Invalidate();
        parts.Manager.Update(InTeaParty(job, 2));
        Assert.Equal(hint, parts.Manager.Status[^hint.Length..]);
        Assert.Single(parts.Chat.FirstArgs("Print"));

        // The usual slot OT (or "Tank 2" under "My column") gives Tank 2's calls, with no hint.
        config.DefaultSlot = "OT";
        config.Touch();
        parts.Manager.Update(InTeaParty(job, 3));
        Assert.Equal(("Tank 2", "OT"), (parts.Manager.Mapping?.AssigneeKey, parts.Manager.Mapping?.Slot));
        Assert.DoesNotContain("Your slot", parts.Manager.Status, StringComparison.Ordinal);
        Assert.Contains(OwnEntries(parts), e => e.Who.Assignee == "Tank 2" && e.Who.Job is null && Names(e).SequenceEqual(["Reprisal"]));
    }

    [Theory]
    [InlineData(null, "set \"My usual party slot\" in /mitcue > General, or choose \"Melee 1\" or \"Melee 2\" under \"My column\" in /mitcue > Plans")]
    [InlineData("H1", "choose \"Melee 1\" or \"Melee 2\" under \"My column\" in /mitcue > Plans")]
    public void OnTeaAMonkWithoutAMeleeSlotIsToldWhyTheSeatsFeintsAreMissing(string? slot, string how)
    {
        // Review finding D11: the plan is the Monk's automatic one (its Mantras), but the Feints are the job-less calls of
        // "Melee 1" and "Melee 2": without a melee slot the status said only "Ready", with none of them.
        var parts = LiveInTea(Job.MNK, slot);

        Assert.True(parts.Manager.PlanIsAutomatic);
        Assert.DoesNotContain(OwnEntries(parts), e => e.Who.Slot != null); // no Feint of a seat
        var hint = $"Your slot (M1 or M2) is not known, so its calls are not shown: {how}.";
        Assert.EndsWith(" entries are yours). " + hint, parts.Manager.Status, StringComparison.Ordinal);
        Assert.Equal([$"TEA, \"LPDU TEA Heal/Mit\" as MNK: {hint}"], parts.Chat.FirstArgs("Print"));
    }

    [Theory]
    [InlineData(Job.MNK, "M1")]
    [InlineData(Job.DRK, "OT")]
    [InlineData(Job.SCH, null)]
    [InlineData(Job.BLM, null)]
    [InlineData(Job.MCH, "H1")]
    public void OnTeaAKnownSeatGetsNoHint(Job job, string? slot)
    {
        var parts = LiveInTea(job, slot);

        Assert.Matches(@"^Ready: LPDU TEA Heal/Mit \(\d+ of \d+ entries are yours\)\.$", parts.Manager.Status);
        Assert.Empty(parts.Chat.FirstArgs("Print"));
    }

    [Fact]
    public void ATankWhoChoseTheirColumnGetsNoHint()
    {
        var parts = SessionManagerTests.NewManager();
        parts.Plans.EnsureLoaded();
        parts.Plans.SetMapping(parts.Plans.Get(LpduTea)!.Plan, Job.WAR, new PlayerMapping { AssigneeKey = "Tank 2", Slot = "OT" }); // as /mitcue > Plans saves it

        parts.Manager.Update(InTeaParty(Job.WAR));

        Assert.Equal(("Tank 2", "OT"), (parts.Manager.Mapping?.AssigneeKey, parts.Manager.Mapping?.Slot));
        Assert.DoesNotContain("Your slot", parts.Manager.Status, StringComparison.Ordinal);
        Assert.Empty(parts.Chat.FirstArgs("Print"));
    }

    [Fact]
    public void OnLpduATankWithoutASlotIsToldTheSlotsCallsAreMissing()
    {
        // LPDU TOP's job-less MT and OT calls (Reprisals, Ramparts) need the usual slot: the pair column has only the job's.
        var parts = Live(Job.WAR, null);

        Assert.Equal("WAR/DRK WAR", parts.Manager.Mapping?.AssigneeKey);
        Assert.EndsWith(" entries are yours). Your slot (MT or OT) is not known, so its calls are not shown: set \"My usual party slot\" in /mitcue > General.", parts.Manager.Status, StringComparison.Ordinal);
        Assert.Single(parts.Chat.FirstArgs("Print"));
    }

    [Fact]
    public void AWhiteMageWhoImportsTheCoHealersLpduCodeKeepsCarryTheClearUnlessTheyAskForIt()
    {
        var parts = SessionManagerTests.NewManager(new Configuration { DefaultSlot = "H1" });
        parts.Plans.EnsureLoaded();
        var code = PlanSerializer.ToShareCode(parts.Plans.Get(Lpdu)!.Plan);

        Assert.True(parts.Plans.ImportShareCode(code, Job.WHM, makeActive: false, out var copy, out var message), message);
        Assert.Equal(Lpdu, copy!.Id);
        Assert.Equal(NoirMitWhm, parts.Plans.ActivePlanFor("top", Job.WHM, out var automatic)?.Id);
        Assert.True(automatic);
        Assert.Contains("Your active plan for TOP as WHM stays \"NoirMit - WHM\"", message);

        Assert.True(parts.Plans.ImportShareCode(code, Job.WHM, makeActive: true, out _, out message), message);
        Assert.Equal(Lpdu, parts.Plans.ActivePlanFor("top", Job.WHM, out automatic)?.Id);
        Assert.False(automatic);
        Assert.Contains("It is your active plan for TOP as WHM.", message);

        // The Scholar's default is LPDU by id, so the copy of it stays the Scholar's automatic plan.
        Assert.Equal(Lpdu, parts.Plans.ActivePlanFor("top", Job.SCH, out automatic)?.Id);
        Assert.True(automatic);
    }

    [Theory]
    [InlineData("tea", Job.SCH, LpduTea, "p1.pull", Kit.SacredSoil, "Sacred Soil")]
    [InlineData("tea", Job.WAR, LpduTea, "p1.pull", Kit.Reprisal, "Reprisal")]
    [InlineData("top", Job.WAR, Lpdu, "p1.pantokrator", Kit.Reprisal, "Reprisal")]
    public void AShareCodeOfABundledPlanKeepsItsPlaceOverThePlayersOwnPlan(string fight, Job job, string bundledId, string mechanic, uint action, string name)
    {
        // Review finding (C13): the share code of a bundled plan replaces it with a user plan of the same id. With the box
        // unticked, the player's own unchosen plan for the fight that sorts first by name became the automatic one where no
        // BundledDefaults entry names the plan (every TEA job, TOP's Warrior), and the message said the plan stayed.
        var parts = SessionManagerTests.NewManager();
        parts.Plans.EnsureLoaded();
        var own = new MitPlan { Id = "own", Name = "Aa my own plan", Fight = fight, Entries = [Kit.Entry(mechanic, 0, action, name, job)] };
        Assert.True(parts.Plans.Save(own, out var error), error);
        Assert.Equal(bundledId, parts.Plans.ActivePlanFor(fight, job)?.Id);

        var code = PlanSerializer.ToShareCode(parts.Plans.Get(bundledId)!.Plan);
        Assert.True(parts.Plans.ImportShareCode(code, job, makeActive: false, out _, out var message), message);

        Assert.Equal(PlanOrigin.User, parts.Plans.Get(bundledId)!.Origin);
        Assert.Equal(bundledId, parts.Plans.ActivePlanFor(fight, job, out var automatic)?.Id);
        Assert.True(automatic);
        Assert.Contains($"It is your active plan for {fight.ToUpperInvariant()} as {job}.", message, StringComparison.Ordinal);
        parts.Plans.Reload(); // as after a restart
        Assert.Equal(bundledId, parts.Plans.ActivePlanFor(fight, job)?.Id);

        // Deleting the copy brings the bundled plan back in its place.
        Assert.True(parts.Plans.Delete(bundledId, out error), error);
        Assert.Equal((bundledId, PlanOrigin.Bundled), (parts.Plans.ActivePlanFor(fight, job)?.Id, parts.Plans.Get(bundledId)!.Origin));
    }

    [Fact]
    public void AShareCodeWithoutCallsForTheCurrentJobSaysWhereToChooseIt()
    {
        var parts = SessionManagerTests.NewManager();
        parts.Plans.EnsureLoaded();
        var plan = new MitPlan { Id = "sch-only", Name = "SCH notes", Fight = "top", Entries = [Kit.Entry("p1.pantokrator", -3, Kit.SacredSoil, "Sacred Soil", Job.SCH)] };

        Assert.True(parts.Plans.ImportShareCode(PlanSerializer.ToShareCode(plan), Job.WHM, makeActive: true, out _, out var message), message);
        Assert.Contains("It has no calls for WHM", message);
        Assert.Equal(NoirMitWhm, parts.Plans.ActivePlanFor("top", Job.WHM)?.Id);
        Assert.Equal(Lpdu, parts.Plans.ActivePlanFor("top", Job.SCH)?.Id);
    }

    [Fact]
    public void EveryRecommendedPlanIsABundledPlanWithAColumnForItsJob()
    {
        var parts = SessionManagerTests.NewManager();
        parts.Plans.EnsureLoaded();
        foreach (var (key, id) in PlanStore.BundledDefaults)
        {
            var stored = parts.Plans.Get(id);
            Assert.True(stored != null, $"{key}: no bundled plan {id}");
            var fight = key[..key.IndexOf('/')];
            var job = Enum.Parse<Job>(key[(key.IndexOf('/') + 1)..]);
            Assert.Equal(fight, stored.Plan.Fight);
            Assert.Equal(PlanOrigin.Bundled, stored.Origin);
            Assert.Contains(stored.Plan.Assignees, a => a.Job == job);
        }
    }

    [Theory]
    [InlineData(Job.SCH, "H2", new[] { "e017", "e063" })]
    [InlineData(Job.WHM, "H1", new[] { "e016", "e062" })]
    public void OnLpduTheSuggestedColumnKeepsTheSlotsJoblessCalls(Job job, string slot, string[] jobless)
    {
        // Final review finding: the bundled LPDU plan has no saved column choice, and the suggestion (the job's column,
        // "H2 SCH") left out the job-less "H2" calls ("Everything" on Pantokrator and Limitless Synergy) that an in-game
        // import of the same sheet saves a column choice for. The CLI's simulate (no --assignee) kept them. Since review
        // round 3 a column has the job-less calls of its slot, so the job's column is the suggestion again.
        var config = new Configuration { DefaultSlot = slot };
        config.ActivePlans[PlanStore.ActiveKey("top", job)] = Lpdu;
        var parts = Live(job, slot, config);
        var plan = parts.Manager.Plan!.Plan;

        var mapping = parts.Manager.Mapping!;
        Assert.True(mapping.IsSuggestion);
        Assert.Equal(slot + " " + job, mapping.AssigneeKey);
        Assert.Equal(slot, mapping.Slot);
        var own = OwnEntryIds(parts);
        Assert.All(jobless, id => Assert.Contains(id, own));

        // Every entry of the player's slot meant for their job or for no job: the import's choice selects the same.
        var expected = plan.Entries
            .Where(e => (e.Who.Slot == slot || e.Who.Assignee?.StartsWith(slot, StringComparison.Ordinal) == true) && (e.Who.Job == null || e.Who.Job == job))
            .Select(e => e.Id)
            .ToList();
        Assert.Equal(expected.Order(), own.Order());
        var choice = ImportColumns.Choose(plan, job, 90, slot);
        Assert.Equal((choice.Mapping.AssigneeKey, choice.Mapping.Slot), (mapping.AssigneeKey, mapping.Slot));
        Assert.Equal(own.Count, choice.Mine);
    }

    [Theory]
    [InlineData(Job.WAR, "MT", "WAR/DRK WAR", 35)]
    [InlineData(Job.WAR, "OT", "WAR/DRK WAR", 28)]
    [InlineData(Job.WAR, null, "WAR/DRK WAR", 9)]
    [InlineData(Job.DRK, "MT", "WAR/DRK DRK", 34)]
    [InlineData(Job.DRK, "OT", "WAR/DRK DRK", 27)]
    [InlineData(Job.DRK, null, "WAR/DRK DRK", 8)]
    public void OnLpduATankGetsOneTankPairColumn(Job job, string? slot, string column, int entries)
    {
        // Review finding (41d2893): the suggestion for a tank became "any column for my job", which has LPDU's job-less MT
        // calls but also every tank pair's column of the job (WAR/DRK, WAR/GNB and WAR/PLD WAR) and both Cosmo Memory
        // roles; without a slot even the MT and the OT calls. The job's column (one pair) stays the suggestion. Review
        // round 3: with it the slot's job-less calls (MT: 8 Reprisals, 3 Ramparts; OT: 5), which round 2 left out.
        var parts = Live(job, slot);

        Assert.True(parts.Manager.PlanIsAutomatic);
        Assert.Equal(Lpdu, parts.Manager.Plan?.Id);
        Assert.Equal(column, parts.Manager.Mapping?.AssigneeKey);
        Assert.True(parts.Manager.Mapping!.IsSuggestion);
        var own = parts.Manager.Session!.Scheduler.All.Select(r => r.Entry).Distinct().ToList();
        Assert.Equal(entries, own.Count);
        Assert.All(own, e => Assert.True(
            e.Who.Assignee == null || e.Who.Assignee == column || (slot != null && e.Who.Assignee == slot && (e.Who.Job == null || e.Who.Job == job)),
            $"{e.Id} {e.Who.Assignee} {e.Who.Slot}/{e.Who.Job}"));
        if (slot != null)
            Assert.Contains(own, e => e.Who.Assignee == slot && e.Who.Job == null);
    }

    [Theory]
    [InlineData("MT")]
    [InlineData(null)]
    public void OnLpduAWarriorsHolmgangAtSolarRay3IsCalledOnceAndDoneWhenPressed(string? slot)
    {
        // The reviewer's probe: three Holmgang reminders at Solar Ray 3 (one per tank pair); one press satisfies one
        // reminder, so two always ended Missed, and Land Waker (LB3) was called for both Cosmo Memory roles.
        const uint holmgang = 43;
        const uint landWaker = 4240;
        var parts = Live(Job.WAR, slot);
        var scheduler = parts.Manager.Session!.Scheduler;

        var reminder = Assert.Single(scheduler.All, r => r.Mechanic.Id == "p5.solar_ray_3" && r.Item.ActionIds().Contains(holmgang));
        Assert.DoesNotContain(scheduler.All, r => r.Item.ActionIds().Contains(landWaker));
        scheduler.Update(reminder.PressAt - 20);
        scheduler.Update(reminder.PressAt);
        scheduler.OnOwnAction(holmgang, reminder.PressAt);
        scheduler.Update(reminder.Latest + 1);
        Assert.Equal(MitCue.Core.Reminders.ReminderState.Done, reminder.State);
    }

    [Fact]
    public void OnLpduTheScholarsCoHealerCallsAreTheOtherHealerSlotsAndInAPartyTheWhiteMages()
    {
        // Final review finding: the dimmed "co-healer" calls were every other healer column of LPDU (60 entries): the H2
        // Sage column, the player's own job-less H2 notes, and both the H1 White Mage and H1 Astrologian columns.
        var config = new Configuration { ShowOtherAssignees = true };
        var parts = Live(Job.SCH, "H2", config);
        var others = parts.Manager.Session!.Others!.All.Select(r => r.Entry).Distinct().ToList();
        Assert.All(others, e => Assert.Equal("H1", e.Who.Slot));
        Assert.Contains(others, e => e.Who.Job == Job.AST); // the party is not known: every H1 column

        // In a party with a White Mage the session is rebuilt (no pull is running) with that co-healer's calls only.
        var generation = parts.Manager.Generation;
        parts.Manager.Update(Frame(Job.SCH, 2, PartyJobs.Mask([Job.WAR, Job.DRK, Job.WHM, Job.SCH, Job.SAM, Job.RPR, Job.DNC, Job.PCT])));
        Assert.True(parts.Manager.Generation > generation);
        others = parts.Manager.Session!.Others!.All.Select(r => r.Entry).Distinct().ToList();
        Assert.All(others, e => Assert.True(e.Who.Slot == "H1" && e.Who.Job is null or Job.WHM, $"{e.Id} {e.Who.Slot}/{e.Who.Job}/{e.Who.Assignee}"));
        Assert.Equal(11, others.Count);
    }

    [Theory]
    [InlineData(Job.WAR, "MT", Job.DRK, "WAR/DRK DRK")]
    [InlineData(Job.DRK, "OT", Job.WAR, "WAR/DRK WAR")]
    public void OnLpduATanksCoTankCallsAreThePairsColumn(Job job, string slot, Job coTank, string pairColumn)
    {
        // Review finding (41d2893): with a WAR/DRK party the co-tank rows held the Dark Knight's column of every tank pair
        // (e251 DRK/GNB DRK and e252 DRK/PLD DRK: Living Dead at Solar Ray 3, while this pair's is at Solar Ray 2).
        var config = new Configuration { ShowOtherAssignees = true, DefaultSlot = slot };
        var parts = SessionManagerTests.NewManager(config);
        parts.Manager.Update(Frame(job, 1, PartyJobs.Mask([Job.WAR, Job.DRK, Job.WHM, Job.SCH, Job.SAM, Job.RPR, Job.DNC, Job.PCT])));

        var others = parts.Manager.Session!.Others!.All.Select(r => r.Entry).Distinct().ToList();
        Assert.All(others, e => Assert.True(e.Who.Job == null || e.Who.Job == coTank, $"{e.Id} {e.Who.Assignee} {e.Who.Job}"));
        Assert.Equal(job == Job.WAR ? 27 : 35, others.Count); // 57 and 65 before the fix, 41 and 49 with both Cosmo Memory roles (round 5)
        var columns = others.Select(e => e.Who.Assignee).Distinct().ToList();
        Assert.Contains(pairColumn, columns);
        Assert.DoesNotContain(columns, c => c != null && c.Contains('/', StringComparison.Ordinal) && c != pairColumn);
        Assert.Contains(slot == "MT" ? "OT" : "MT", columns); // the co-tank's slot column
        Assert.DoesNotContain(columns, c => c != null && c.StartsWith("CM ", StringComparison.Ordinal)); // their Cosmo Memory role is not known
    }

    [Theory]
    [InlineData(Job.BRD, "R1", null)]
    [InlineData(Job.BRD, "R1", "Phys Range")]
    [InlineData(Job.MNK, "M1", null)]
    public void OnTeaTheOtherDpsRowsHaveTheRoleColumnsCallsWhateverColumnThePlayerFollows(Job job, string slot, string? column)
    {
        // Review finding C14: the Black Mage's Addles ("Caster") reached a Bard's rows only after the Bard picked "Phys Range"
        // in /mitcue > Plans, and a Monk in "Melee 1" never saw them.
        var config = new Configuration { ShowOtherAssignees = true, DefaultSlot = slot };
        var parts = SessionManagerTests.NewManager(config);
        parts.Plans.EnsureLoaded();
        if (column != null)
            parts.Plans.SetMapping(parts.Plans.Get(LpduTea)!.Plan, job, new PlayerMapping { AssigneeKey = column });
        var frame = SessionManagerTests.InTea(job);
        frame.PartyJobMask = PartyJobs.Mask([Job.PLD, Job.WAR, Job.WHM, Job.SCH, Job.MNK, Job.DRG, Job.BRD, Job.BLM]);
        parts.Manager.Update(frame);

        var others = parts.Manager.Session!.Others!.All.Select(r => r.Entry).Distinct().ToList();
        var addles = others.Where(e => e.Who.Assignee == "Caster").ToList();
        Assert.Equal(parts.Manager.Plan!.Plan.Entries.Where(e => e.Who.Assignee == "Caster" && e.Who.Job == Job.BLM).OrderBy(e => e.Id), addles.OrderBy(e => e.Id)); // the Black Mage's
        Assert.Contains(addles, e => e.Src == "'LL/BJCC'!L19" && Names(e).SequenceEqual(["Addle"]));
        Assert.DoesNotContain(others, e => e.Who.Job is { } j && j != Job.None && !new[] { Job.MNK, Job.DRG, Job.BRD, Job.BLM }.Contains(j)); // nobody else plays them
        if (job == Job.MNK)
            Assert.Contains(others, e => e.Who.Assignee == "Phys Range" && e.Who.Job == Job.BRD);
    }

    [Fact]
    public void APartyChangeRebuildsNothingWhileTheCoHealersCallsAreHidden()
    {
        var parts = Live(Job.SCH, "H2");
        var generation = parts.Manager.Generation;

        parts.Manager.Update(Frame(Job.SCH, 2, PartyJobs.Mask([Job.WHM, Job.SCH])));

        Assert.Equal(generation, parts.Manager.Generation);
        Assert.Null(parts.Manager.Session!.Others);
    }

    [Theory]
    [InlineData(NoirMitWhm)]
    [InlineData(CarryTheClearSch)]
    [InlineData(Lpdu)]
    [InlineData(LpduTea)]
    public void TheShareCodeOfABundledPlanReplacesItInTheList(string id)
    {
        // Final review finding: the imported copy and the bundled plan were both listed with the same id (and the same
        // ImGui ids) until "Reload plans".
        var parts = SessionManagerTests.NewManager();
        parts.Plans.EnsureLoaded();
        var code = parts.Plans.ExportShareCode(parts.Plans.Get(id)!.Plan, out var error);
        Assert.True(code != null, error);

        Assert.True(parts.Plans.ImportShareCode(code, out _, out var message), message);

        var listed = Assert.Single(parts.Plans.All, p => p.Id == id);
        Assert.Equal(PlanOrigin.User, listed.Origin);
        Assert.Equal(parts.Plans.All.Select(p => p.Id).Distinct().Count(), parts.Plans.All.Count);
        Assert.Equal(PlanOrigin.User, parts.Plans.All[0].Origin); // user plans first, as after a reload
        parts.Plans.Reload();
        Assert.Equal(PlanOrigin.User, Assert.Single(parts.Plans.All, p => p.Id == id).Origin);
    }

    [Fact]
    public void DeletingTheCopyOfABundledPlanKeepsItsSettings()
    {
        // Final review finding: deleting the copy also forgot the active-plan choice and column saved under the id,
        // although the bundled plan with that id came back.
        var config = new Configuration();
        var parts = SessionManagerTests.NewManager(config);
        parts.Plans.EnsureLoaded();
        var code = parts.Plans.ExportShareCode(parts.Plans.Get(Lpdu)!.Plan, out _)!;
        Assert.True(parts.Plans.ImportShareCode(code, out var copy, out _));
        parts.Plans.SetActive("top", Job.AST, Lpdu);
        parts.Plans.SetMapping(copy!, Job.AST, new PlayerMapping { AssigneeKey = "H1 AST", Slot = "H1" });

        Assert.True(parts.Plans.Delete(Lpdu, out var error), error);

        Assert.Equal(PlanOrigin.Bundled, Assert.Single(parts.Plans.All, p => p.Id == Lpdu).Origin);
        Assert.Equal(Lpdu, parts.Plans.ActivePlanFor("top", Job.AST, out var automatic)?.Id);
        Assert.False(automatic);
        Assert.Equal("H1 AST", parts.Plans.MappingFor(parts.Plans.Get(Lpdu)!.Plan, Job.AST).AssigneeKey);
    }

    [Fact]
    public void DeletingAnImportedPlanForgetsItsSettings()
    {
        var parts = SessionManagerTests.NewManager();
        parts.Plans.EnsureLoaded();
        var own = new MitPlan { Id = "own", Name = "Mine", Fight = "top", Entries = [Kit.Entry("p1.pantokrator", -3, Kit.Temperance, "Temperance")] };
        Assert.True(parts.Plans.Save(own, out _));
        parts.Plans.SetActive("top", Job.WHM, "own");
        parts.Plans.SetMapping(own, Job.WHM, new PlayerMapping { AssigneeKey = "x" });

        Assert.True(parts.Plans.Delete("own", out _));

        Assert.Null(parts.Plans.Get("own"));
        Assert.Equal(NoirMitWhm, parts.Plans.ActivePlanFor("top", Job.WHM, out var automatic)?.Id);
        Assert.True(automatic);
        Assert.Null(parts.Plans.SavedMapping(own, Job.WHM));
    }
}

/// <summary>Share codes and plan files skip the import window's review: the store checks them (DESIGN §1.3).</summary>
public class PlanCheckTests
{
    private static SessionManagerTests.Parts Checked()
    {
        var parts = SessionManagerTests.NewManager();
        parts.Data.EnsureLoaded();
        parts.Plans.Check = plan => PlanChecks.Find(plan, parts.Data);
        parts.Plans.EnsureLoaded();
        return parts;
    }

    /// <summary>A TEA (level 80) plan that asks a Scholar for Expedient (learned at 90), and an entry for an unknown mechanic.</summary>
    private static MitPlan TeaProbe() => new()
    {
        Id = "tea-probe",
        Name = "TEA probe",
        Fight = "tea",
        Entries =
        [
            new PlanEntry { Id = "e1", Mechanic = "p1.pull", Offset = 5, Who = new WhoSpec { Job = Job.SCH }, Do = [PlanItem.ForAction(25868, "Expedient")] },
            new PlanEntry { Id = "e2", Mechanic = "p9.nothing", Offset = 5, Who = new WhoSpec { Job = Job.SCH }, Do = [PlanItem.ForAction(25868, "Expedient")] },
        ],
    };

    [Fact]
    public void AShareCodeWithProblemsIsImportedAndTheProblemsAreShown()
    {
        // Final review finding: share codes were accepted without any check; the Expedient call could only end Missed.
        var parts = Checked();

        Assert.True(parts.Plans.ImportShareCode(PlanSerializer.ToShareCode(TeaProbe()), out _, out var message), message);

        Assert.Contains("Imported \"TEA probe\" (2 entries, fight tea).", message, StringComparison.Ordinal);
        Assert.Contains("problems:", message, StringComparison.Ordinal);
        Assert.Contains("Problems in /mitcue", message, StringComparison.Ordinal);
        var problem = Assert.Single(parts.Errors.Snapshot(), p => p.Key == "plans.load.check.tea-probe");
        Assert.Contains("Entry 'e1': Expedient is learned at level 90, above this fight's level 80, so the call can never be done", problem.Message, StringComparison.Ordinal);
        Assert.Contains("Entry 'e2': TEA has no mechanic \"p9.nothing\", so the call is never shown", problem.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A plan as the AI route writes it and the review accepts it: an action name in other letter case, an either/or of
    /// which the Scholar has one option, and a hit index past the mechanic's last hit (the nearest hit is used).
    /// </summary>
    private static MitPlan ReviewedPlan() => new()
    {
        Id = "ai-plan",
        Name = "AI plan",
        Fight = "top",
        Source = new PlanSource { Kind = "llm" },
        Assignees = [new Assignee { Key = "Scholar", Job = Job.SCH, Slot = "H2" }],
        Entries =
        [
            new PlanEntry { Id = "e1", Mechanic = "p1.loop_set1", Offset = -3, Who = new WhoSpec { Job = Job.SCH, Slot = "H2", Assignee = "Scholar" }, Do = [PlanItem.ForAction(Kit.SacredSoil, "Sacred soil")] },
            new PlanEntry { Id = "e2", Mechanic = "p1.pantokrator", Offset = -3, Who = new WhoSpec { Job = Job.SCH, Slot = "H2", Assignee = "Scholar" }, Do = [PlanItem.AnyOf([PlanItem.ForAction(Kit.SacredSoil, "Sacred Soil"), PlanItem.ForAction(24298, "Kerachole")])] },
            new PlanEntry { Id = "e3", Mechanic = "p1.pantokrator", HitIndex = 9, Offset = 1, Who = new WhoSpec { Job = Job.SCH, Slot = "H2", Assignee = "Scholar" }, Do = [PlanItem.ForAction(16538, "Fey Illumination")] },
        ],
    };

    [Fact]
    public void WhatTheReviewAcceptsIsNotAProblem()
    {
        // Review finding (41d2893): the checks were the core PlanValidator's, stricter than the review where the runtime
        // does not mind (names compared exactly, every either/or option for the job, hit indexes), so a reviewed plan was
        // listed in Problems after every restart and its share code said its calls could never be done, though they work.
        var parts = Checked();
        var plan = ReviewedPlan();
        var top = parts.Data.GetFight("top")!;
        // The review has no error for them (the hit index is a warning there: "the nearest one is used").
        Assert.All(plan.Entries, e => Assert.DoesNotContain(ReviewValidator.CheckEntry(e, plan, top, parts.Data.Actions, true), i => i.Severity >= DiagnosticSeverity.Error));

        Assert.Empty(PlanChecks.Find(plan, parts.Data));
        Assert.True(parts.Plans.ImportShareCode(PlanSerializer.ToShareCode(plan), out _, out var message), message);
        Assert.DoesNotContain("problem", message, StringComparison.Ordinal);
        parts.Plans.Reload();
        Assert.DoesNotContain(parts.Errors.Snapshot(), p => p.Key.StartsWith("plans.load.check.", StringComparison.Ordinal));
    }

    [Fact]
    public void SlotSynonymsAreStoredAsTheStandardSlots()
    {
        // The plan checks follow the review, which reads "D3" as R1; the runtime compares slots by name, so the store
        // writes the standard name: the R1 player sees the call with any column choice, not only the suggested one.
        var parts = Checked();
        var plan = new MitPlan
        {
            Id = "d3-plan",
            Name = "D3 plan",
            Fight = "top",
            Assignees = [new Assignee { Key = "D3", Slot = "D3" }],
            Entries = [new PlanEntry { Id = "e1", Mechanic = "p1.loop_set3", Offset = -3, Who = new WhoSpec { Slot = "D3", Assignee = "D3" }, Do = [PlanItem.ForAction(7560, "Addle")] }],
        };

        Assert.True(parts.Plans.ImportShareCode(PlanSerializer.ToShareCode(plan), out var imported, out var message), message);

        Assert.DoesNotContain("problem", message, StringComparison.Ordinal);
        Assert.Equal("R1", imported!.Assignees[0].Slot);
        Assert.Equal("R1", imported.Entries[0].Who.Slot);
        parts.Plans.SetMapping(imported, Job.BLM, new PlayerMapping { AssigneeKey = "D3", Slot = "R1" }); // as /mitcue > Plans saves it
        var mapping = parts.Plans.MappingFor(imported, Job.BLM);
        Assert.Single(PlanSelection.For(imported, new PlayerContext(Job.BLM, 90, mapping.Slot, mapping.AssigneeKey)));
    }

    [Fact]
    public void AnEitherOrWithNoOptionForTheJobIsAProblem()
    {
        var parts = Checked();
        var plan = ReviewedPlan();
        plan.Entries[1].Do = [PlanItem.AnyOf([PlanItem.ForAction(24298, "Kerachole"), PlanItem.ForAction(24310, "Holos")])];

        var problem = Assert.Single(PlanChecks.Find(plan, parts.Data));
        Assert.Equal("Entry 'e2': None of Kerachole / Holos is a SCH action, so the call can never be done.", problem);
    }

    [Fact]
    public void APlanFileWithProblemsIsListedInProblemsUntilItIsDeleted()
    {
        var parts = Checked();
        Assert.True(parts.Plans.Save(TeaProbe(), out var error), error);
        parts.Plans.Reload();
        Assert.Contains(parts.Errors.Snapshot(), p => p.Key == "plans.load.check.tea-probe");

        Assert.True(parts.Plans.Delete("tea-probe", out error), error);
        Assert.DoesNotContain(parts.Errors.Snapshot(), p => p.Key.StartsWith("plans.load.check.", StringComparison.Ordinal));
    }

    [Fact]
    public void TheBundledPlansAndTheirShareCodesHaveNoProblems()
    {
        var parts = Checked();
        foreach (var stored in parts.Plans.All.ToList())
            Assert.Empty(PlanChecks.Find(stored.Plan, parts.Data));

        var code = parts.Plans.ExportShareCode(parts.Plans.All[0].Plan, out _)!;
        Assert.True(parts.Plans.ImportShareCode(code, out _, out var message));
        Assert.DoesNotContain("problem", message, StringComparison.Ordinal);
        Assert.Empty(parts.Errors.Snapshot());
    }

    [Fact]
    public void APlanForAnUnknownFightSaysSo()
    {
        var parts = Checked();
        var plan = TeaProbe();
        plan.Fight = "xyz";
        var problem = Assert.Single(PlanChecks.Find(plan, parts.Data));
        Assert.Contains("no fight \"xyz\"", problem, StringComparison.Ordinal);
    }
}
