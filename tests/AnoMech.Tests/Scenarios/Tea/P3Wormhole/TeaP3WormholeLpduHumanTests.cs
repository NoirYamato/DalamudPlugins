using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Tea.P3Wormhole;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Tea.TeaConstants;
using static AnoMech.Tests.TeaP3WormholeLpduAiTests;

namespace AnoMech.Tests;

// The sunny-day soak drives the player's seat with the bots' own AI. Here a human takes the seat as the Enumeration
// markers show, the strat having walked it until then on NAUR's moves, and plays the Enumerations by LPDU's priority
// from what a player sees: the two markers, then Incinerating Heat going off on the stack it stands in. A reaction time
// after that it walks straight to its spot at a player's pace: marked M1 or R1 east beside the tanks, marked M2 or R2
// west beside the healers, the melee of two marked on one side to the other ring, an unmarked DPS north where NAUR has
// it.
//
// Among LPDU's bots nobody dies, whatever the pair, the DPS seat, the layout and the reaction time. Among NAUR's, which
// send a lone marked melee east, the human and the other marked DPS share a ring wherever the two priorities part: M1
// and R1 marked, or M2 and R1, with the human one of them.
//
// Only the DPS seats are played, and only from the markers on: the tanks' and healers' spots and every move before 46.0
// are NAUR's, which TeaP3WormholeLpduAiTests holds frame by frame.
public class TeaP3WormholeLpduHumanTests
{
    private static readonly float[] ReactionTimes = [0f, 0.5f, 1f];

    private const float MarkersShow = 45.31f;
    private const float WalkSpeed = 6f;

    private static IEnumerable<TestCaseData> PairsAndSeats()
        => from pair in MarkedPairs
           from seat in TeaP3WormholeState.Dps
           select new TestCaseData(pair.First, pair.Second, pair.LpduEast, seat)
               .SetName($"{{m}}({Label(pair.First)} and {Label(pair.Second)} marked, the human {Label(seat)})");

    private static IEnumerable<TestCaseData> WhereThePrioritiesPart()
        => from pair in MarkedPairs
           where pair.LpduEast != pair.NaurEast
           from seat in new[] { pair.First, pair.Second }
           select new TestCaseData(pair.First, pair.Second, seat)
               .SetName($"{{m}}({Label(pair.First)} and {Label(pair.Second)} marked, the human {Label(seat)})");

    [TestCaseSource(nameof(PairsAndSeats))]
    public void AHumanTakingItsSpotByTheLpduPriorityLosesNobody(PartyRole first, PartyRole second, PartyRole east, PartyRole seat)
    {
        var problems = new List<string>();
        var runs = 0;
        foreach (var layout in Layouts)
        foreach (var late in ReactionTimes)
        {
            var seed = 7919 * runs++ + 17 * (int)seat + 3;
            var human = new Human(seat, late);
            var run = Play(LpduStrat, seed, layout, first, second, human);
            var spot = LpduSpot(seat, first, second, east);
            var why = $"seed {seed}, {layout}, {late:F2}s late";
            if (!run.Passed) problems.Add($"{why}: {run}");
            else if (!human.Marked.Order().SequenceEqual(new[] { first, second }.Order()))
                problems.Add($"{why}: the human read the markers on {string.Join(", ", human.Marked.Select(Label))}");
            else if (Vector2.Distance(human.Stood, spot) > 0.01f)
                problems.Add($"{why}: the human stood at {human.Stood} as the Enumerations went off, not on {spot}");
        }

        TestContext.Out.WriteLine($"{runs} runs, {problems.Count} problems");
        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems));
    }

    [TestCaseSource(nameof(WhereThePrioritiesPart))]
    public void AmongNaursBotsTheHumanSharesARingWithTheOtherMarked(PartyRole first, PartyRole second, PartyRole seat)
    {
        var other = seat == first ? second : first;
        var problems = new List<string>();
        foreach (var layout in Layouts)
        {
            var seed = Random.Shared.Next();
            var human = new Human(seat, late: 0f);
            var run = Play(NaStrat, seed, layout, first, second, human);
            if (run.Failure is not null || run.Deaths.All(death => death.ActionId != ActionId.Enumeration))
                problems.Add($"seed {seed}, {layout}: nobody died to the Enumerations: {run}");
            else if (Vector2.Distance(human.Others[other], human.Stood) > EnumerationRadius)
                problems.Add($"seed {seed}, {layout}: {Label(other)} at {human.Others[other]}, out of the human's ring round {human.Stood}");
        }

        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems));
    }

    private static ScenarioRun Play(int strat, int seed, Layout layout, PartyRole first, PartyRole second, Human human)
        => ScenarioRun.Execute(typeof(TeaP3WormholeScenario), strat, seed, new ScenarioRunOptions
        {
            PlayerRole = human.Seat,
            WriteArtifactsOnFailure = strat == LpduStrat,
            Overrides = o => Pin((TeaP3WormholeStateOverrides)o, layout, first, second),
            Takeovers = [new Takeover(MarkersShow, null)],
            Probe = human.Tick,
        });

    // Reads the markers and Incinerating Heat off what the sim sends, then walks straight to its spot.
    private sealed class Human(PartyRole seat, float late)
    {
        public PartyRole Seat => seat;
        public List<PartyRole> Marked { get; } = [];

        // Where it and everyone else stood just before the Enumerations went off.
        public Vector2 Stood { get; private set; } = new(float.NaN, float.NaN);
        public Dictionary<PartyRole, Vector2> Others { get; } = [];

        private int rowsRead;
        private float? stackWentOff;
        private (float Depart, Vector2 From, Vector2 To)? walking;

        public void Tick(ScenarioProbe p)
        {
            if (p.Time < MarkersShow || p.Member(seat) is not { } me || !me.IsAlive()) return;
            Read(p);
            if (walking is null && Marked.Count == 2 && stackWentOff is { } went && p.Time >= went + late)
                walking = (p.Time, Flat(me.Position), LpduSpot(seat, Marked[0], Marked[1], JoinsTheTanks(Marked[0], Marked[1])));
            if (walking is { } w)
            {
                var length = Vector2.Distance(w.From, w.To);
                var walked = (p.Time - w.Depart) * WalkSpeed;
                var at = walked >= length ? w.To : Vector2.Lerp(w.From, w.To, walked / length);
                var heading = length > 1e-3f ? MathF.Atan2(w.To.X - w.From.X, w.To.Y - w.From.Y) : me.Rotation;
                me.SetPosition(new Placement(new Vector3(at.X, 0f, at.Y), heading));
            }
            if (!p.Crossed(JustBeforeTheEnumerations)) return;
            Stood = Flat(me.Position);
            foreach (var role in PerRole.All.Where(role => role != seat)) Others[role] = Flat(p.Member(role)!.Position);
        }

        // M1 or R1 joins the tanks; of two marked on one side, the melee goes to the other.
        private static PartyRole JoinsTheTanks(PartyRole a, PartyRole b)
        {
            static bool ByTheTanks(PartyRole role) => role is MeleeDpsA or PhysRangedDps;
            if (ByTheTanks(a) != ByTheTanks(b)) return ByTheTanks(a) ? a : b;
            var melee = a is MeleeDpsA or MeleeDpsB ? a : b;
            return ByTheTanks(a) ? (melee == a ? b : a) : melee;
        }

        private void Read(ScenarioProbe p)
        {
            var rows = p.Fake.Timeline.Rows;
            var roleById = PerRole.All.ToDictionary(role => p.Member(role)!.EntityId.ToString("X8"), role => role);
            for (; rowsRead < rows.Count; rowsRead++)
            {
                var row = rows[rowsRead];
                if (row["kind"] is "ac" or "ac_self" && row["id"] == "0x22" && Convert.ToUInt32(row["p"].Split(',')[0], 16) == LockonId.Enumeration
                    && roleById.TryGetValue(row["actor"], out var marked) && !Marked.Contains(marked))
                    Marked.Add(marked);
                if (row["kind"] == "ability" && row["id"] == $"{ActionId.IncineratingHeat}")
                    stackWentOff ??= float.Parse(row["t"], CultureInfo.InvariantCulture);
            }
        }
    }
}
