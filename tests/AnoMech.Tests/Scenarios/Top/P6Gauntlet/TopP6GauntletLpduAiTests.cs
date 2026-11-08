using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.UserActions;
using AnoMech.Multiplayer;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top;
using AnoMech.Scenarios.Top.P6Gauntlet;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.P6Gauntlet.TopP6GauntletPart;

namespace AnoMech.Tests;

// The strat presses every limit break 3 the plan gives a seat no human presses for, at LPDU's times on the Gauntlet's
// clock, each with its job's real cast and once the gauge is full and nothing else is casting:
//    4.9   the Cosmo Memory tank, halfway through Cosmo Memory;
//   150.2 / 156.9   the first melee right after Cosmo Dive 2's bar, the second from the middle once the gauge refills;
//   178.3 / 188.8   the caster on the comets, the physical ranged through both meteors;
//   202.5 / 208.8   Magic Number 1's tank early in the cast and its healer a second after the mark;
//   218.6 / 224.9   Magic Number 2's;
//   233.0   the enrage holder, early in Run: ****mi*.
// The second melee's walk to the middle follows the plan's seat; everyone is back on the autos' spots for the Magic
// Numbers and the enrage. A press still waiting for the gauge once its mechanic has gone off is dropped.
public class TopP6GauntletLpduAiTests
{
    private const int LpduStrat = 0;

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

    private static uint Job(PartyRole seat) => PartyPresets.Standard[(int)seat].ClassJob;

    // With every seat's preset job: the warrior presses Cosmo Memory and Magic Number 1, the paladin Magic Number 2;
    // the dragoon goes first and takes the enrage, the monk second. A debug-driven player's seat is pressed for too.
    [Test]
    public void TheStratPressesEveryLimitBreakThePlanGivesTheSeats()
    {
        IReadOnlyList<TopP6LimitBreakPress> presses = [];
        var ids = new Dictionary<PartyRole, ulong>();
        ulong alphaOmega = 0;
        var run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = CasterDps,
            Probe = p =>
            {
                presses = TopP6GauntletRun.LimitBreaks(p).Presses;
                if (ids.Count == 0) foreach (var role in PerRole.All) ids[role] = (ulong)p.Member(role)!.GameObjectId;
                if (alphaOmega == 0 && TopP6GauntletRun.Scenario(p).AlphaOmega?.Enemy is { } boss) alphaOmega = (ulong)boss.GameObjectId;
            },
        });

        (PartyRole Seat, float Pressed, float Resolved)[] expected =
        [
            (MainTank, 4.9f, 4.9f),
            (MeleeDpsA, 150.2f, 154.7f),
            (MeleeDpsB, 156.9f, 161.4f),
            (CasterDps, 178.3f, 182.8f),
            (PhysRangedDps, 188.8f, 193.3f),
            (MainTank, 202.5f, 202.5f),
            (RegenHealer, 208.8f, 210.8f),
            (OffTank, 218.6f, 218.6f),
            (ShieldHealer, 224.9f, 226.9f),
            (MeleeDpsA, 233.0f, 237.5f),
        ];
        Assert.That(run.Passed, run.ToString);
        Assert.Multiple(() =>
        {
            Assert.That(presses.Select(r => r.Seat), Is.EqualTo(expected.Select(e => e.Seat)), "who presses, in order");
            Assert.That(presses.Select(r => r.CastStartedAt), Is.EqualTo(expected.Select(e => e.Pressed)).Within(0.05f), "when they press");
            Assert.That(presses.Select(r => r.ResolvedAt ?? float.NaN), Is.EqualTo(expected.Select(e => e.Resolved)).Within(0.05f), "when they land");
            Assert.That(presses.Select(r => r.ActionId), Is.EqualTo(expected.Select(e => LimitBreakHandler.ActionId(Job(e.Seat), 3))), "each its job's limit break 3");
            Assert.That(presses.Select(r => r.ByHuman), Has.All.False);
            Assert.That(presses.Select(r => (r.Seat, r.TargetId)),
                        Is.EqualTo(expected.Select(e => (e.Seat, TopP6LimitBreaks.KindOf(e.Seat) is TopP6LimitBreakKind.Tank or TopP6LimitBreakKind.Healer ? ids[e.Seat] : alphaOmega))),
                        "tanks and healers on themselves, the rest on Alpha Omega");
        });
    }

    // The human's own job decides the enrage holder against the bot melee's (SAM > DRG > MNK > VPR > RPR > NIN, a tie
    // going to the human): it goes first after Cosmo Dive 2 and takes the enrage, the other melee goes second and walks
    // to the middle for it. The debug-driven human's presses come from the strat too. The hold is SW, off the first melee's
// way back past the middle.
    [TestCase(MeleeDpsB, JobId.Samurai, MeleeDpsB, MeleeDpsA)]
    [TestCase(MeleeDpsB, JobId.Dragoon, MeleeDpsB, MeleeDpsA)]
    [TestCase(MeleeDpsB, JobId.Reaper, MeleeDpsA, MeleeDpsB)]
    [TestCase(MeleeDpsA, JobId.Reaper, MeleeDpsB, MeleeDpsA)]
    [TestCase(MeleeDpsA, JobId.Monk, MeleeDpsA, MeleeDpsB)]
    public void TheMeleeJobsDecideWhoGoesFirstAndWhoWalksToTheMiddle(PartyRole seat, JobId job, PartyRole first, PartyRole second)
    {
        IReadOnlyList<TopP6LimitBreakPress> presses = [];
        var inTheMiddle = new List<PartyRole>();
        var run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = seat,
            Overrides = o =>
            {
                ((FakeBattleCharas)Natives.BattleCharas).Player.ClassJob = (byte)job;
                TopP6GauntletExaflares2LpduAiTests.Pin((TopP6GauntletStateOverrides)o, 2, true, 1.1f);
            },
            Probe = p =>
            {
                presses = TopP6GauntletRun.LimitBreaks(p).Presses;
                if (p.Crossed(31.9f))
                    inTheMiddle.AddRange(PerRole.All.Where(r => Vector2.Distance(Flat(p.Member(r)!.Position), Vector2.Zero) < 0.45f));
            },
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(presses.Select(r => r.Seat), Is.EqualTo(new[] { first, second }));
        Assert.That(presses.Select(r => r.Job), Is.EqualTo(new[] { first == seat ? (uint)job : Job(first), second == seat ? (uint)job : Job(second) }));
        Assert.That(inTheMiddle, Is.EqualTo(new[] { second }), "the second melee waits in the middle");
    }

    [TestCase(MeleeDpsB, JobId.Samurai, MeleeDpsB)]
    [TestCase(MeleeDpsB, JobId.Ninja, MeleeDpsA)]
    public void TheEnrageHolderPressesDuringRunMi(PartyRole seat, JobId job, PartyRole enrage)
    {
        IReadOnlyList<TopP6LimitBreakPress> presses = [];
        var run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = seat,
            Overrides = o =>
            {
                ((FakeBattleCharas)Natives.BattleCharas).Player.ClassJob = (byte)job;
                TopP6GauntletRun.Only((TopP6GauntletStateOverrides)o, MagicNumber);
            },
            Probe = p => presses = TopP6GauntletRun.LimitBreaks(p).Presses,
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(presses.Where(r => r.Kind == TopP6LimitBreakKind.Melee).Select(r => (r.Seat, r.CastStartedAt)),
                    Is.EqualTo(new[] { (enrage, 33.1f) }).Using<(PartyRole, float)>((a, b) => a.Item1 == b.Item1 && MathF.Abs(a.Item2 - b.Item2) < 0.05f));
    }

    // A tank's job ranks it (WAR > DRK > PLD > GNB, a tie going to the human), and the human's own row overrides it;
    // healers go by seat unless the human's row swaps them. Each seat presses what the plan gives it.
    [TestCase(OffTank, JobId.Warrior, null, null, new[] { OffTank, OffTank, MainTank }, new[] { RegenHealer, ShieldHealer })]
    [TestCase(MainTank, JobId.Gunbreaker, null, null, new[] { OffTank, OffTank, MainTank }, new[] { RegenHealer, ShieldHealer })]
    [TestCase(MainTank, JobId.Warrior, TopP6TankLimitBreaks.MagicNumber1, null, new[] { OffTank, MainTank, OffTank }, new[] { RegenHealer, ShieldHealer })]
    [TestCase(RegenHealer, JobId.WhiteMage, null, TopP6HealerLimitBreaks.MagicNumber2, new[] { MainTank, MainTank, OffTank }, new[] { ShieldHealer, RegenHealer })]
    public void TheTanksAndHealersPressWhatThePlanGivesThem(PartyRole seat, JobId job, TopP6TankLimitBreaks? tank, TopP6HealerLimitBreaks? healer,
        PartyRole[] tanks, PartyRole[] healers)
    {
        IReadOnlyList<TopP6LimitBreakPress> fromCosmoMemory = [];
        IReadOnlyList<TopP6LimitBreakPress> fromTheMagicNumbers = [];
        foreach (var part in new[] { CosmoMemory, MagicNumber })
        {
            var run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
            {
                PlayerRole = seat,
                Overrides = o =>
                {
                    ((FakeBattleCharas)Natives.BattleCharas).Player.ClassJob = (byte)job;
                    var overrides = (TopP6GauntletStateOverrides)o;
                    TopP6GauntletRun.Only(overrides, part);
                    overrides.TankLimitBreaks = tank;
                    overrides.HealerLimitBreaks = healer;
                },
                Probe = p =>
                {
                    if (part == CosmoMemory) fromCosmoMemory = TopP6GauntletRun.LimitBreaks(p).Presses;
                    else fromTheMagicNumbers = TopP6GauntletRun.LimitBreaks(p).Presses;
                },
            });
            Assert.That(run.Passed, run.ToString);
        }

        Assert.Multiple(() =>
        {
            Assert.That(fromCosmoMemory.Select(r => r.Seat), Is.EqualTo(new[] { tanks[0] }), "Cosmo Memory");
            Assert.That(fromTheMagicNumbers.Where(r => r.Kind == TopP6LimitBreakKind.Tank).Select(r => r.Seat), Is.EqualTo(tanks[1..]), "the Magic Numbers' tanks");
            Assert.That(fromTheMagicNumbers.Where(r => r.Kind == TopP6LimitBreakKind.Healer).Select(r => r.Seat), Is.EqualTo(healers), "their healers");
        });
    }

    // The human melee's row puts it second: the other melee goes first and takes the enrage, and the human walks to the
    // middle for its own.
    [Test]
    public void TheMeleeRowPutsTheHumanSecond()
    {
        IReadOnlyList<TopP6LimitBreakPress> presses = [];
        var inTheMiddle = new List<PartyRole>();
        var run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = MeleeDpsA,
            Overrides = o =>
            {
                var overrides = (TopP6GauntletStateOverrides)o;
                TopP6GauntletExaflares2LpduAiTests.Pin(overrides, 2, true, 1.1f);
                overrides.MeleeLimitBreaks = TopP6MeleeLimitBreaks.Second;
            },
            Probe = p =>
            {
                presses = TopP6GauntletRun.LimitBreaks(p).Presses;
                if (p.Crossed(31.9f))
                    inTheMiddle.AddRange(PerRole.All.Where(r => Vector2.Distance(Flat(p.Member(r)!.Position), Vector2.Zero) < 0.45f));
            },
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(presses.Select(r => r.Seat), Is.EqualTo(new[] { MeleeDpsB, MeleeDpsA }));
        Assert.That(inTheMiddle, Is.EqualTo(new[] { MeleeDpsA }));
    }

    // The last exaflare east puts the party's hold (14, 0) 0.5 y from the off-tank's east spot: it tanks the autos from the
    // west instead, so a first melee still casting on the hold is clear of its Flash Gale; on the other rolls it stays east.
    [TestCase(5, false, -14.5f)]
    [TestCase(7, true, -14.5f)]
    [TestCase(4, true, 14.5f)]
    public void TheOffTankTanksTheAutosAfterCosmoDive2ClearOfTheHold(int first, bool clockwise, float x)
    {
        var seen = Vector2.Zero;
        var run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = CasterDps,
            Overrides = o => TopP6GauntletExaflares2LpduAiTests.Pin((TopP6GauntletStateOverrides)o, first, clockwise, 1.1f),
            Probe = p => { if (p.Crossed(35.2f)) seen = Flat(p.Member(OffTank)!.Position); },
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(Vector2.Distance(seen, new Vector2(x, 0f)), Is.LessThan(0.45f), $"the off-tank at {seen}");
    }

    // Once the flares have landed everyone goes back to the autos' spots, for both Magic Numbers and the enrage.
    [Test]
    public void EveryoneTakesTheAutosSpotsForTheMagicNumbersAndTheEnrage()
    {
        var magicNumber = TopP6GauntletParts.GauntletStart(MagicNumber) - TopP6GauntletParts.GauntletStart(CosmoMeteor);
        float[] times = [magicNumber + 1.8f, magicNumber + 17.9f, magicNumber + 32.0f];
        var problems = new List<string>();
        var run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = CasterDps,
            Overrides = o => ((TopP6GauntletStateOverrides)o).Parts = (CosmoMeteor, MagicNumber),
            Probe = p =>
            {
                foreach (var at in times.Where(p.Crossed))
                    foreach (var role in PerRole.All.Where(r => r != CasterDps))
                    {
                        var off = Vector2.Distance(Flat(p.Member(role)!.Position), TopP6LpduAi.AutoSpot(role));
                        if (off > 0.45f) problems.Add($"{role} {off:F2} y off its autos' spot at {at:F1}");
                    }
            },
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems));
    }

    // The human off-tank also presses Last Bastion at 3.0, before Brilliant Dynamis exists: the gauge is gone, so the
    // Cosmo Memory tank's press waits, and the human's covers Cosmo Memory. The bot's press is dropped once Cosmo Memory
    // has gone off, rather than going out when the gauge has rebuilt, after Cosmo Dive 2's bar.
    [Test]
    public void APressStillWaitingOnceItsMechanicHasGoneOffIsDropped()
    {
        IReadOnlyList<TopP6LimitBreakPress> presses = [];
        var run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = OffTank,
            Overrides = o =>
            {
                var overrides = (TopP6GauntletStateOverrides)o;
                overrides.Parts = (CosmoMemory, Exaflares2);
                overrides.PlayerPressesItsOwnLimitBreaks = true;
            },
            Probe = p =>
            {
                if (p.Crossed(0f)) TopP6HumanLimitBreak.Press(p.World, 3f, TopP6GauntletRun.LimitBreak3(OffTank));
                presses = TopP6GauntletRun.LimitBreaks(p).Presses;
            },
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(presses.Select(r => (r.Seat, r.ByHuman)), Is.EqualTo(new[] { (OffTank, true), (MeleeDpsA, false), (MeleeDpsB, false) }));
        Assert.That(presses[1].CastStartedAt, Is.EqualTo(150.2f).Within(0.05f));
    }

    private static Vector2 Flat(Vector3 position) => new(position.X, position.Z);
}
