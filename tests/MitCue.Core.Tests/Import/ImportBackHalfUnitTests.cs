using MitCue.Core.Fights;
using MitCue.Core.Import;
using MitCue.Core.Plans;
using MitCue.Core.Tests.Data;

namespace MitCue.Core.Tests.Import;

/// <summary>The back half's building blocks: label matching, action resolution, hints, row timing, validation.</summary>
public class ImportBackHalfUnitTests
{
    private static readonly MechanicResolver Mechanics = new(PlanImports.Top);
    private static readonly ActionResolver Resolver = new(RepoFiles.Actions, RepoFiles.Aliases, 90);

    private static ParsedToken Token(string text) => Assert.Single(CellParser.Default.Parse(text).Tokens);

    // ------------------------------------------------------------------ text

    [Theory]
    [InlineData("Atomic Ray (P1 enrage — push before)", "atomic ray (p1 enrage - push before)")]
    [InlineData("  Pantokrator  ", "pantokrator")]
    [InlineData("Solar Ray ×2 ★★★", "solar ray ×2")]
    [InlineData("Mitigation Key:", "mitigation key")]
    public void NormalizeLabel(string label, string expected) => Assert.Equal(expected, ImportText.NormalizeLabel(label));

    [Theory]
    [InlineData("Pantokrator: line stacks + missiles (65k) ★★★ (multi-hit)", "pantokrator line stacks missiles")]
    [InlineData("Unlimited Wave Cannon: proteans (90k ×2) + Wild Charge (100–220k) ★★★", "unlimited wave cannon proteans wild charge")]
    [InlineData("Diffuse Wave Cannon Kyrios x5", "diffuse wave cannon kyrios")]
    public void SimplifyLabel(string label, string expected) => Assert.Equal(expected, ImportText.SimplifyLabel(label));

    [Theory]
    [InlineData("Short CD to R1", 2, "to R1")]
    [InlineData("Nature's Minne now", 2, "now")]
    [InlineData("TBN Co-Tank", 1, "Co-Tank")]
    [InlineData("Soil", 1, null)]
    public void SkipNormalizedWords(string text, int words, string? rest) => Assert.Equal(rest, ImportText.SkipNormalizedWords(text, words));

    [Fact]
    public void NameGuid_IsAStableVersion5Guid()
    {
        var a = ImportText.NameGuid("x");
        Assert.Equal(a, ImportText.NameGuid("x"));
        Assert.NotEqual(a, ImportText.NameGuid("y"));
        Assert.Equal('5', a.ToString("D")[14]);
    }

    // ------------------------------------------------------------------ mechanics

    [Theory]
    [InlineData("Pantokrator ", "p1", "p1.pantokrator")]
    [InlineData("1st Stack", "p1", "p1.panto_stack1")]
    [InlineData("CD1", "p6", "p6.cosmo_dive_1")]
    [InlineData("Proteans", "p4", "p4.wave_cannon")]
    [InlineData("Run: ****mi* (enrage — top groups kill at 5:08–5:22)", "p6", "p6.run_mi_enrage")]
    public void Candidates_Unique(string label, string phase, string id) =>
        Assert.Equal([id], Mechanics.Candidates(label, phase).Mechanics.Select(m => m.Id));

    [Fact]
    public void Candidates_RepeatedLabels_AreInHitOrderAndPhaseBound()
    {
        Assert.Equal(["p5.delta_hello_world", "p5.sigma_hello_world", "p5.omega_hello_world_1", "p5.omega_hello_world_2"], Mechanics.Candidates("Hello World", "p5").Mechanics.Select(m => m.Id));
        Assert.Equal(["p3.hello_world"], Mechanics.Candidates("Hello World", "p3").Mechanics.Select(m => m.Id));
        Assert.Equal(MechanicMatch.OtherPhase, Mechanics.Candidates("Critical Error", "p5").Match);
        Assert.Equal(MechanicMatch.None, Mechanics.Candidates("Nothing like this", "p5").Match);
    }

    [Fact]
    public void PhaseEnd_IsTheEnrageElseTheLastMechanic()
    {
        Assert.Equal("p1.atomic_ray", Mechanics.PhaseEnd("p1")!.Id);
        Assert.Equal("p3.ion_efflux", Mechanics.PhaseEnd("p3")!.Id);
        Assert.Equal("p4.blue_screen", Mechanics.PhaseEnd("p4")!.Id); // P4 has no enrage
        Assert.Equal("p5.blind_faith", Mechanics.PhaseEnd("p5")!.Id);
    }

    // ------------------------------------------------------------------ actions

    [Theory]
    [InlineData("Kera", Job.SGE, "Kerachole")]
    [InlineData("Wings", Job.WHM, "Temperance")]
    [InlineData("Wings", Job.PLD, "Passage of Arms")]
    [InlineData("Lilybell pop after 1st Hit", Job.WHM, "Liturgy of the Bell")]
    [InlineData("Divine Benison", Job.WHM, "Divine Benison")]
    [InlineData("WHM Bell", Job.WHM, "Liturgy of the Bell")]
    [InlineData("Nascant Co-Tank", Job.WAR, "Nascent Flash")]
    [InlineData("Physis", Job.SGE, "Physis II")]
    [InlineData("Healer LB3", Job.SCH, "Angel Feathers")]
    public void Resolve_Actions(string text, Job job, string action)
    {
        var r = Resolver.Resolve(Token(text), job);
        Assert.Equal(TokenKind.Actions, r.Kind);
        Assert.Equal(action, Assert.Single(r.Alternatives).Actions[0].Name);
    }

    [Theory]
    [InlineData("Everything", Job.SCH, "NoteOnly")]
    [InlineData("Kera", Job.SCH, "OtherJob")]
    [InlineData("WHM Bell", Job.SCH, "OtherJob")]
    [InlineData("PLD Wings", Job.WAR, "OtherJob")]
    [InlineData("Seraphism", Job.SCH, "NotAvailable")]
    [InlineData("Xyzzy", Job.SCH, "Unresolved")]
    [InlineData("Co-Tank", Job.WAR, "Unresolved")] // 'co' is an AST alias; it must not resolve for a Warrior
    public void Resolve_NotAnActionOfTheJob(string text, Job job, string kind) => Assert.Equal(kind, Resolver.Resolve(Token(text), job).Kind.ToString());

    [Fact]
    public void Resolve_KeepsTheRestAndUsesTheTargetedForm()
    {
        var r = Resolver.Resolve(Token("Short CD to R1"), Job.WAR);
        Assert.Equal("Nascent Flash", r.Alternatives[0].Actions[0].Name);
        Assert.Equal("to R1", r.Rest);
        var self = Resolver.Resolve(Token("Short CD"), Job.WAR);
        Assert.Equal("Bloodwhetting", self.Alternatives[0].Actions[0].Name);
        var invuln = Resolver.Resolve(Token("Invuln Diffuse Wave Cannon"), Job.GNB);
        Assert.Equal(("Superbolide", "Diffuse Wave Cannon"), (invuln.Alternatives[0].Actions[0].Name, invuln.Rest));
    }

    // ------------------------------------------------------------------ hints

    [Theory]
    [InlineData("after 1st Hit", "p6.cosmo_meteor", null, 0, true, false)]
    [InlineData("pop after the 2nd hit", "p6.cosmo_meteor", null, 1, true, false)]
    [InlineData("as P1 dies", "p1.pantokrator", "p1.atomic_ray", null, false, true)]
    [InlineData("just before P3 ends", "p3.ion_efflux", "p3.ion_efflux", null, false, true)]
    [InlineData("after Solar Ray", "p5.solar_ray_1", null, null, true, false)]
    [InlineData("Diffuse Wave Cannon", "p1.pantokrator", "p1.wave_cannon_kyrios", null, false, false)]
    [InlineData("after 6th puddle", "p6.wave_cannon_1", null, null, false, false)]
    [InlineData("if P4 skip", "p5.delta_patch", null, null, false, false)]
    public void Hints(string hint, string row, string? anchor, int? hit, bool after, bool phaseEnd)
    {
        var mechanic = PlanImports.Top.GetMechanic(row)!;
        var timing = HintParser.Parse(hint, [], Mechanics, mechanic.Phase, mechanic, n => "p" + n);
        Assert.Equal(anchor, timing.Anchor?.Id); // null: the row's own mechanic
        Assert.Equal(hit, timing.HitIndex);
        Assert.Equal(after, timing.After);
        Assert.Equal(phaseEnd, timing.AtPhaseEnd);
    }

    [Fact]
    public void Hints_JobCondition()
    {
        var row = PlanImports.Top.GetMechanic("p4.stack_2")!;
        Assert.Equal(Job.MCH, HintParser.Parse("if MCH", [], Mechanics, "p4", row, n => "p" + n).JobCondition);
    }

    // ------------------------------------------------------------------ row timing

    [Fact]
    public void Align_MatchesListsRangesAndLabelTimes()
    {
        var detector = new LayoutDetector(PlanImports.Top);
        var layout = detector.Detect(Fixtures.Tab("carry-the-clear", "TOP – White Mage"));
        RowLayout Row(int sheetRow) => layout.Rows[sheetRow - 1];
        IReadOnlyList<RowRef> Align(int sheetRow, string id, double offset = 0) => RowTiming.Align(Row(sheetRow), PlanImports.Top.GetMechanic(id)!, offset);

        Assert.Equal([0, 1, 2, 3], Align(17, "p1.program_loop").Select(r => r.HitIndex)); // 0:28 / 0:37 / 0:46 / 0:55
        Assert.Equal([0, 1], Align(19, "p1.wave_cannon_kyrios").Select(r => r.HitIndex)); // range 1:47 – 1:56
        Assert.Equal([0], Align(32, "p3.latent_1").Select(r => r.HitIndex)); // 1:12 (rot explosions) is no hit
        Assert.Equal([0, 1, 2, 3, 4, 5], Align(40, "p4.wave_cannon").Select(r => r.HitIndex)); // six times in the label
        Assert.Equal([0, 9], Align(53, "p5.omega_all", 1.1).Select(r => r.HitIndex));
        Assert.Equal(928.9, Align(53, "p5.omega_all", 1.1)[0].Timeline, 3);
    }

    [Fact]
    public void ToTimeline_AndHitFor()
    {
        var refs = new[] { new RowRef(81, 0, 81.3), new RowRef(87, 1, 87.3) };
        Assert.Equal(78.3, RowTiming.ToTimeline(78, refs, null)!.Value, 3);
        Assert.Equal(91.3, RowTiming.ToTimeline(91, refs, null)!.Value, 3);
        Assert.Equal(80.0, RowTiming.ToTimeline(79, [], 1.0)!.Value, 3);
        Assert.Null(RowTiming.ToTimeline(79, [], null));

        var panto = PlanImports.Top.GetMechanic("p1.pantokrator")!;
        Assert.Equal(2, RowTiming.HitFor(panto, 91.3, afterHit: false));
        Assert.Equal(1, RowTiming.HitFor(panto, 91.3, afterHit: true));
        Assert.Equal(3, RowTiming.HitFor(panto, 120, afterHit: false));
        Assert.Equal(0, RowTiming.HitFor(panto, 60, afterHit: true));
        Assert.Equal(2.5, RowTiming.Median([1, 2, 3, 4]));
    }

    // ------------------------------------------------------------------ row offsets (fight data)

    [Fact]
    public void TopRowOffsets_AreTheResearchedCastEndCorrections()
    {
        // docs/research/mechanics.md §4.2 "Conclusions": what Carry the Clear's row times mark, against the anchors.
        var offsets = PlanImports.Top.RowOffsets["carry_the_clear"];
        Assert.Equal(new Dictionary<string, double> { ["p4.blue_screen"] = 1.0, ["p5.omega_all"] = 1.1, ["p5.blind_faith"] = -1.2, ["p6.run_mi_enrage"] = -14.0 }, offsets.ToDictionary());
        Assert.All(offsets.Keys, id => Assert.NotNull(PlanImports.Top.GetMechanic(id)));
    }

    [Fact]
    public void RowOffsets_LoadAndCheckMechanicIds()
    {
        const string head = "fight: x\nname: X\nterritory: 1\nsync_level: 90\ntimeline: x.txt\nphases: [{id: p1, start: 0}]\nmechanics: [{id: p1.a, hit: 10}]\n";
        var fight = FightLoader.Load(head + "row_offsets:\n  sheet: {p1.a: -1.5}\n  empty: ~\n");
        Assert.Equal(-1.5, fight.RowOffsets["SHEET"]["P1.A"]);
        Assert.False(fight.RowOffsets.ContainsKey("empty"));
        Assert.Empty(FightLoader.Load(head).RowOffsets);
        Assert.Contains("unknown mechanic 'p1.b'", Assert.Throws<FormatException>(() => FightLoader.Load(head + "row_offsets: {sheet: {p1.b: 1}}")).Message, StringComparison.Ordinal);
        Assert.Throws<FormatException>(() => FightLoader.Load(head + "row_offsets: [1]"));
        Assert.Throws<FormatException>(() => FightLoader.Load(head + "row_offsets: {sheet: [1]}"));
    }

    // ------------------------------------------------------------------ validation

    [Fact]
    public void Validator_FindsBrokenEntries()
    {
        var plan = new MitPlan
        {
            Fight = "top",
            Assignees = [new Assignee { Key = "H2", Slot = "H2" }],
            Entries =
            [
                new PlanEntry { Id = "a", Mechanic = "p9.nothing", Do = [PlanItem.ForAction(188, "Sacred Soil")] },
                new PlanEntry { Id = "a", Mechanic = "p1.pantokrator", HitIndex = 7, Do = [PlanItem.ForAction(188, "Sacred Soil")] },
                new PlanEntry { Id = "c", Mechanic = "p1.pantokrator", Who = new WhoSpec { Job = Job.WHM }, Do = [PlanItem.ForAction(188, "Sacred Soil")] },
                new PlanEntry { Id = "d", Mechanic = "p1.pantokrator", Who = new WhoSpec { Job = Job.SCH }, Do = [PlanItem.ForAction(37014, "Seraphism")] },
                new PlanEntry { Id = "e", Mechanic = "p1.pantokrator", Who = new WhoSpec { Slot = "H2", Assignee = "H2" }, Do = [PlanItem.ForAction(7531, "Rampart")] },
                new PlanEntry { Id = "f", Mechanic = "p1.pantokrator", Who = new WhoSpec { Slot = "H2", Assignee = "nobody" }, Do = [PlanItem.ForAction(999999, "?"), PlanItem.AnyOf([])] },
            ],
        };
        var codes = PlanValidator.Validate(plan, PlanImports.Top, RepoFiles.Actions).Select(d => d.Code).ToList();
        Assert.Equal(["unknown-mechanic", "duplicate-id", "hit-index", "wrong-job", "not-available-at-level", "wrong-job", "unknown-assignee", "unknown-action", "empty-any"], codes);

        var good = new MitPlan { Fight = "top", Entries = [new PlanEntry { Id = "a", Mechanic = "p1.pantokrator", Who = new WhoSpec { Slot = "H2" }, Do = [PlanItem.ForAction(188, "Sacred Soil")] }] };
        Assert.Empty(PlanValidator.Validate(good, PlanImports.Top, RepoFiles.Actions));
    }
}
