using System.Globalization;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Multiplayer;
using AnoMech.Scenarios.Top;
using AnoMech.Scenarios.Top.P6Gauntlet;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.P6Gauntlet.TopP6LimitBreakDuty;
using static AnoMech.Scenarios.Top.TopP6Constants;

namespace AnoMech.Tests;

// The sunny-day soak drives the player's seat with the bots' own AI. Here the seat is a human who plays the Gauntlet's
// Magic Number part by LPDU from its spot for the autos, from what a player can see: Magic Number's cast bar, the mark on
// its own status bar, and Run: ****mi*'s cast bar. A tank presses its limit break 3 early in its Magic Number's cast, a
// healer a second after the mark shows, and the enrage holder a second into Run: ****mi*'s cast, the gauge refilled after
// the second healer's; each a fixed reaction time late, as its client plays it. Each seat plays every duty its rows can
// give it here (none, for R1 and R2). Nobody may die: every Magic Number and its mark pass, and Alpha Omega falls before
// Run: ****mi* lands.
public class TopP6GauntletMagicNumberLpduHumanTests
{
    private static readonly float[] ReactionTimes = [0f, 0.3f, 0.6f];
    private const float EarlyInTheCast = 0.5f, ASecondAfterTheMark = 1f, ASecondIntoRunMi = 1f;

    // Multiplayer fixtures leave their hosting session installed, which would make every seat real here too.
    private MultiplayerManager? leftInstalled;

    [SetUp]
    public void Solo()
    {
        leftInstalled = Plugin.Multiplayer;
        DalamudServices.Install(nameof(Plugin.Multiplayer), null!);
    }

    [TearDown]
    public void PutBackWhatWasInstalled() => DalamudServices.Install(nameof(Plugin.Multiplayer), leftInstalled!);

    public sealed record Setup(PartyRole Seat, TopP6TankLimitBreaks? Tank = null, TopP6HealerLimitBreaks? Healer = null,
        TopP6MeleeLimitBreaks? Melee = null)
    {
        public override string ToString()
            => (Tank?.ToString() ?? Healer?.ToString() ?? Melee?.ToString()) is { } row ? $"{Seat}, row {row}" : $"{Seat}";
    }

    private static readonly (Setup Setup, TopP6LimitBreakDuty[] Duties)[] Setups =
    [
        (new(MainTank), [MagicNumber1Tank]),
        (new(MainTank, Tank: TopP6TankLimitBreaks.MagicNumber2), [MagicNumber2Tank]),
        (new(OffTank), [MagicNumber2Tank]),
        (new(OffTank, Tank: TopP6TankLimitBreaks.MagicNumber1), [MagicNumber1Tank]),
        (new(RegenHealer), [MagicNumber1Healer]),
        (new(RegenHealer, Healer: TopP6HealerLimitBreaks.MagicNumber2), [MagicNumber2Healer]),
        (new(ShieldHealer), [MagicNumber2Healer]),
        (new(ShieldHealer, Healer: TopP6HealerLimitBreaks.MagicNumber1), [MagicNumber1Healer]),
        (new(MeleeDpsA), [Enrage]),
        (new(MeleeDpsA, Melee: TopP6MeleeLimitBreaks.Second), []),
        (new(MeleeDpsB), []),
        (new(MeleeDpsB, Melee: TopP6MeleeLimitBreaks.SecondAndTheEnrage), [Enrage]),
        (new(PhysRangedDps), []),
        (new(CasterDps), []),
    ];

    private static IEnumerable<TestCaseData> Matrix()
        => from setup in Setups
           from delay in ReactionTimes
           select new TestCaseData(setup.Setup, setup.Duties, delay).SetName($"{{m}}({setup.Setup}, {delay:F1}s late)");

    [TestCaseSource(nameof(Matrix))]
    public void AHumanPlayingLpduIsNeverCaught(Setup setup, TopP6LimitBreakDuty[] duties, float delay)
    {
        var guide = new Guide(setup.Seat, delay);
        IReadOnlyList<TopP6LimitBreakPress> presses = [];
        var alphaOmegaFell = float.NaN;
        var run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), 0, 1000 + (int)setup.Seat, new ScenarioRunOptions
        {
            PlayerRole = setup.Seat,
            UserActionsEnabled = true,
            Overrides = o =>
            {
                var overrides = (TopP6GauntletStateOverrides)o;
                overrides.StartFrom = TopP6GauntletPart.MagicNumber;
                overrides.TankLimitBreaks = setup.Tank;
                overrides.HealerLimitBreaks = setup.Healer;
                overrides.MeleeLimitBreaks = setup.Melee;
            },
            Takeovers = [new Takeover(0f, TopP6LpduAi.AutoSpot(setup.Seat))],
            Probe = p =>
            {
                guide.Tick(p);
                presses = TopP6GauntletRun.LimitBreaks(p).Presses;
                if (float.IsNaN(alphaOmegaFell) && TopP6GauntletRun.Scenario(p).AlphaOmega?.Enemy is { Dead: true }) alphaOmegaFell = p.Time;
            },
        });

        var described = TopP6GauntletHumanSeatRun.Describe(presses);
        Assert.That(run.Passed, () => $"{run}{Environment.NewLine}presses: {described}");
        Assert.Multiple(() =>
        {
            Assert.That(guide.Duties, Is.EqualTo(duties), "the human's duties here");
            Assert.That(presses.Where(p => p.ByHuman).Select(p => (p.Kind, p.Level, p.ResolvedAt is not null)),
                        Is.EqualTo(duties.Select(d => (KindOf(d), 3, true))), () => described);
            Assert.That(presses.Select(p => p.Kind), Is.EqualTo(new[]
            {
                TopP6LimitBreakKind.Tank, TopP6LimitBreakKind.Healer, TopP6LimitBreakKind.Tank, TopP6LimitBreakKind.Healer, TopP6LimitBreakKind.Melee,
            }), () => $"each limit break once: {described}");
            Assert.That(alphaOmegaFell, Is.LessThan(TopP6AlphaOmega.RunMiFinishedBy(32.1f)), "Alpha Omega falls before Run: ****mi*'s cast ends");
        });
    }

    private static TopP6LimitBreakKind KindOf(TopP6LimitBreakDuty duty) => duty switch
    {
        MagicNumber1Tank or MagicNumber2Tank => TopP6LimitBreakKind.Tank,
        MagicNumber1Healer or MagicNumber2Healer => TopP6LimitBreakKind.Healer,
        _ => TopP6LimitBreakKind.Melee,
    };

    // Reads its duties off the plan, as the start's chat line names them, and each cue off the casts the client is sent
    // and its own status bar; each press goes out a reaction time after its cue.
    private sealed class Guide(PartyRole seat, float late)
    {
        public IReadOnlyList<TopP6LimitBreakDuty> Duties { get; private set; } = [];

        private TopP6GauntletLimitBreakPlan? plan;
        private int rowsRead, magicNumbersSeen;
        private bool marked;
        private readonly List<(float At, TopP6LimitBreakDuty Duty)> due = [];

        public void Tick(ScenarioProbe p)
        {
            if (plan is null)
            {
                plan = TopP6GauntletRun.State(p).LimitBreakPlan;
                Duties = Enum.GetValues<TopP6LimitBreakDuty>().Where(d => d >= MagicNumber1Tank && plan.HumanPresses(d)).ToList();
            }
            var me = p.Member(seat)!;
            if (!me.IsAlive()) return;
            ReadTheCastBars(p);
            ReadTheMark(p, me);
            foreach (var press in due.Where(d => p.Time >= d.At).ToList())
            {
                due.Remove(press);
                TopP6HumanLimitBreak.Press(p.World, 0f, TopP6GauntletRun.LimitBreak3(seat),
                    press.Duty == Enrage ? () => TopP6GauntletRun.Scenario(p).AlphaOmega?.Enemy : null);
            }
        }

        private void ReadTheCastBars(ScenarioProbe p)
        {
            var rows = p.Fake.Timeline.Rows;
            for (; rowsRead < rows.Count; rowsRead++)
            {
                var row = rows[rowsRead];
                if (row["kind"] != "cast" || !uint.TryParse(row["id"], CultureInfo.InvariantCulture, out var id)) continue;
                if (id == P6ActionId.MagicNumber)
                    Due(p.Time + EarlyInTheCast, ++magicNumbersSeen == 1 ? MagicNumber1Tank : MagicNumber2Tank);
                else if (id == P6ActionId.RunMi)
                    Due(p.Time + ASecondIntoRunMi, Enrage);
            }
        }

        private void ReadTheMark(ScenarioProbe p, SimCharacter me)
        {
            var now = me.HasStatus(P6StatusId.MagicNumber);
            if (now && !marked) Due(p.Time + ASecondAfterTheMark, magicNumbersSeen == 1 ? MagicNumber1Healer : MagicNumber2Healer);
            marked = now;
        }

        private void Due(float cue, TopP6LimitBreakDuty duty)
        {
            if (Duties.Contains(duty)) due.Add((cue + late, duty));
        }
    }
}
