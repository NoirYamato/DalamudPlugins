using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using AnoMech.Multiplayer;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top;
using AnoMech.Scenarios.Top.P6Gauntlet;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.P6Gauntlet.TopP6GauntletPart;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Scenarios.Top.TopP6Constants;

namespace AnoMech.Tests;

// The whole Gauntlet on one clock, G = 0 1.9 s before Cosmo Memory's cast; the parts start at 0, 48.6, 90.9, 124.9,
// 164.8 and 199.9. Every boss event lands where the fight's timeline has it (within 0.1 s), the autos after Wave
// Cannon 2 included (122.4 / 125.6), then both Magic Numbers (201.8 / 217.9) with their marks and Run: ****mi* (232.0),
// which the enrage holder's melee limit break 3 ends by defeating Alpha Omega. A run started from a later part picks the
// fight up there: Alpha Omega, the party on its spots for the autos, the gauge as the party's own generation has it, and
// Brilliant or Spark of Dynamis as the plan's presses before then leave them.
public class TopP6GauntletScenarioTests
{
    private const int LpduStrat = 0;
    private static readonly PartyRole[] SupportsFirst = [MainTank, OffTank, RegenHealer, ShieldHealer, MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps];

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

    // Wave 2's last exaflare starts east, so the off-tank takes its west spot after Cosmo Dive 2.
    private static void PinTheRolls(TopP6GauntletStateOverrides o)
    {
        o.Arrow1InFirst = true;
        o.Exaflares1First = 2;
        o.Exaflares1Clockwise = true;
        o.Lasers1 = SupportsFirst;
        o.WildCharge1Target = MainTank;
        o.Arrow2InFirst = false;
        o.Lasers2 = SupportsFirst;
        o.WildCharge2Target = MainTank;
        o.Exaflares2First = 5;
        o.Exaflares2Clockwise = false;
        o.Flares = [CasterDps, MeleeDpsB, MainTank];
        o.StackTarget = ShieldHealer;
        o.FirstSpreads = [[MainTank, OffTank, RegenHealer, ShieldHealer], [MainTank, OffTank, RegenHealer, ShieldHealer]];
    }

    // ---- The whole phase

    [Test]
    public void TheWholeGauntletFollowsTheFightsTimeline()
    {
        IReadOnlyList<Dictionary<string, string>> rows = [];
        var marked = new Dictionary<float, int>();
        var alphaOmegaAlive = new Dictionary<float, bool>();
        var run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = MainTank,
            Overrides = o => PinTheRolls((TopP6GauntletStateOverrides)o),
            Probe = p =>
            {
                rows = p.Fake.Timeline.Rows;
                foreach (var at in new[] { 208.0f, 208.2f, 210.7f, 211.0f, 224.1f, 224.3f, 226.8f, 227.1f })
                    if (p.Crossed(at)) marked[at] = PerRole.All.Count(r => p.Member(r)!.HasStatus(P6StatusId.MagicNumber));
                foreach (var at in new[] { 237.4f, 237.7f })
                    if (p.Crossed(at)) alphaOmegaAlive[at] = TopP6GauntletRun.Scenario(p).AlphaOmega!.Enemy.IsAlive();
            },
        });

        Assert.That(run.Passed, run.ToString);
        Assert.Multiple(() =>
        {
            Expect(rows, "cast", ActionId.CosmoMemory, [1.9f], "Cosmo Memory's cast");
            Expect(rows, "ability", ActionId.CosmoMemory, [7.9f], "Cosmo Memory's hit");
            Expect(rows, "ability", ActionId.AlphaOmegaAutoAttack, [12.0f, 15.2f, 46.2f, 49.4f, 88.5f, 91.7f, 122.4f, 125.6f, 159.3f, 162.5f], "the autos");
            Expect(rows, "ability", ActionId.Unknown7ddf, Times(2, 12.9f, 16.1f, 47.1f, 50.3f, 89.4f, 92.6f, 123.3f, 126.5f, 160.2f, 163.4f), "Flash Gale");
            Expect(rows, "cast", ActionId.CosmoArrow, [16.3f, 92.8f], "Cosmo Arrow");
            Expect(rows, "cast", ActionId.CosmoDive, [31.4f, 144.5f], "Cosmo Dive's casts");
            Expect(rows, "ability", ActionId.CosmoDive, [37.0f, 150.1f], "Cosmo Dive's bar ends");
            Expect(rows, "ability", ActionId.CosmoDive_7BA7, Times(2, 39.4f, 152.5f), "the dives' busters");
            Expect(rows, "ability", ActionId.CosmoDive_7BA8, [39.4f, 152.5f], "the dives' stacks");
            Expect(rows, "cast", ActionId.UnlimitedWaveCannon, [50.5f, 126.8f], "Unlimited Wave Cannon");
            Expect(rows, "cast", ActionId.WaveCannon_7BAD, [50.5f, 51.5f, 52.5f, 53.5f, 126.8f, 127.8f, 128.8f, 129.8f], "the exaflares' casts");
            Expect(rows, "ability", ActionId.WaveCannon_7BAD, [62.5f, 63.5f, 64.5f, 65.5f, 138.8f, 139.8f, 140.8f, 141.8f], "the first explosions");
            Expect(rows, "cast", ActionId.WaveCannon_7BAF,
                   Times(8, 60.5f, 62.3f, 64.2f, 66.1f, 68.0f, 70.0f, 136.8f, 138.6f, 140.5f, 142.4f, 144.3f, 146.3f), "the puddles");
            Expect(rows, "cast", ActionId.WaveCannon_7BA9, [73.1f, 106.93f], "Wave Cannon");
            Expect(rows, "ability", ActionId.WaveCannonProtean, Times(4, 76.1f, 78.1f, 109.97f, 111.97f), "the lasers");
            Expect(rows, "ability", ActionId.WaveCannonWildCharge, [84.4f, 118.3f], "Wild Charge");
            Expect(rows, "cast", P6ActionId.CosmoMeteor, [166.7f], "Cosmo Meteor");
            Expect(rows, "cast", P6ActionId.CosmoMeteorBait, Times(8, 171.7f), "the baits drop");
            Expect(rows, "spawn", P6BNpcBaseId.CosmoMeteor, Times(2, 174.1f), "the meteors spawn");
            Expect(rows, "spawn", P6BNpcBaseId.CosmoComet, Times(6, 174.1f), "the comets spawn");
            Expect(rows, "ability", P6ActionId.CosmoMeteorSpread, Times(4, 176.8f, 177.8f, 182.8f, 183.8f), "the spreads");
            Expect(rows, "ability", P6ActionId.CosmoMeteorFlare, Times(3, 194.8f), "the flares");
            Expect(rows, "ability", P6ActionId.CosmoMeteorStack, [194.8f], "the stack");
            Expect(rows, "ability", P6ActionId.CosmoMeteorFinish, [195.8f], "Cosmo Meteor's last visual");
            Expect(rows, "cast", P6ActionId.CosmoMeteorEnrage, [], "no add enrages");
            Expect(rows, "cast", P6ActionId.MagicNumber, [201.8f, 217.9f], "Magic Number");
            Expect(rows, "ability", P6ActionId.MagicNumber, [206.8f, 222.9f], "Magic Number's hits");
            Expect(rows, "cast", P6ActionId.RunMi, [232.0f], "Run: ****mi*");
            Expect(rows, "ability", P6ActionId.RunMi, [], "Alpha Omega falls before Run: ****mi* lands");
            Assert.That(marked, Is.EqualTo(new Dictionary<float, int>
            {
                [208.0f] = 0, [208.2f] = 8, [210.7f] = 8, [211.0f] = 0,
                [224.1f] = 0, [224.3f] = 8, [226.8f] = 8, [227.1f] = 0,
            }), "each Magic Number's mark lands on everyone 1.3 s after the hit and the healer's limit break 3 lifts it");
            Assert.That(alphaOmegaAlive, Is.EqualTo(new Dictionary<float, bool> { [237.4f] = true, [237.7f] = false }),
                        "the enrage holder's melee limit break 3 defeats Alpha Omega as it lands");
            Assert.That(run.Elapsed, Is.InRange(249f, 251f), "the run ends a second after Run: ****mi*'s cast would have gone off");
            Assert.That(run.Warnings, Has.None.Contains("dropped"), "every helper cast found a free helper");
        });
    }

    // The last Flash Gale of each part lands on the autos' spots, before the next part's first move: 0.2-0.3 s before the
    // next cast, as in the fight. After Cosmo Dive 2 the second melee waits in the middle, the main tank steps back to
    // (0, -7) and, the hold being east, the off-tank stands west, out at r 16.
    [Test]
    public void EverySeamHoldsTheAutosSpotsThroughItsLastFlashGale()
    {
        Dictionary<PartyRole, Vector2> AutoSpots(bool afterTheDive) => PerRole.All.ToDictionary(r => r, r => r switch
        {
            MainTank => afterTheDive ? new Vector2(0f, -7f) : new Vector2(0f, -5f),
            OffTank => afterTheDive ? new Vector2(-16f, 0f) : new Vector2(14.5f, 0f),
            MeleeDpsB when afterTheDive => Vector2.Zero,
            _ => new Vector2(0f, 6f),
        });
        var seams = new[] { (50.25f, false), (92.55f, false), (126.45f, false), (163.35f, true) };
        var problems = new List<string>();
        var run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = MainTank,
            StopAt = 164f,
            Overrides = o => PinTheRolls((TopP6GauntletStateOverrides)o),
            Probe = p =>
            {
                foreach (var (at, afterTheDive) in seams.Where(s => p.Crossed(s.Item1)))
                    foreach (var (role, spot) in AutoSpots(afterTheDive).Where(s => s.Key != MainTank))
                    {
                        var member = p.Member(role)!;
                        var off = Vector2.Distance(new Vector2(member.Position.X, member.Position.Z), spot);
                        if (off > 0.45f || member.MoveDestination is not null)
                            problems.Add($"{role} at {at}: {off:F2} y off its spot{(member.MoveDestination is null ? "" : ", walking")}");
                    }
            },
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems));
    }

    // ---- Each part on its own, and Start from

    [TestCase(CosmoMemory)]
    [TestCase(Exaflares1)]
    [TestCase(CosmoArrow2)]
    [TestCase(Exaflares2)]
    [TestCase(CosmoMeteor)]
    [TestCase(MagicNumber)]
    public void EachPartClearsOnItsOwn(TopP6GauntletPart part)
    {
        var run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            Overrides = o => TopP6GauntletRun.Only((TopP6GauntletStateOverrides)o, part),
        });

        Assert.That(run.Passed, run.ToString);
    }

    // The gauge the party's own generation gives by then (full from Cosmo Dive 2's bar on), in the run and on the HUD the
    // client gates its presses on, from the first frame; Spark of Dynamis on the seats whose limit break the plan pressed
    // before (the dive melees, then the caster and the physical ranged), Brilliant Dynamis on the rest; Code: ****mi* on
    // Alpha Omega; everyone on the autos' spots from the start.
    [TestCase(Exaflares1, 0.865f, new PartyRole[0])]
    [TestCase(CosmoArrow2, 1.757f, new PartyRole[0])]
    [TestCase(Exaflares2, 2.475f, new PartyRole[0])]
    [TestCase(CosmoMeteor, 3f, new[] { MeleeDpsA, MeleeDpsB })]
    [TestCase(MagicNumber, 3f, new[] { MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps })]
    public void StartingFromALaterPartPicksTheFightUpThere(TopP6GauntletPart part, float bars, PartyRole[] sparked)
    {
        var gauge = float.NaN;
        var hudBars = new List<int>();
        var spark = new List<PartyRole>();
        var brilliant = new List<PartyRole>();
        var bossMarked = false;
        var offTheirSpots = new List<string>();
        var run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = MainTank,
            Overrides = o => ((TopP6GauntletStateOverrides)o).StartFrom = part,
            Probe = p =>
            {
                if (p.Time <= 1.2f) hudBars.Add(p.World.Party.LimitBreak.FilledBars);
                if (p.Crossed(0.05f))
                {
                    gauge = TopP6GauntletRun.LimitBreaks(p).Gauge;
                    foreach (var role in PerRole.All)
                    {
                        var at = new Vector2(p.Member(role)!.Position.X, p.Member(role)!.Position.Z);
                        if (Vector2.Distance(at, TopP6LpduAi.AutoSpot(role)) > 0.01f) offTheirSpots.Add($"{role} at {at}");
                    }
                }
                if (!p.Crossed(1.2f)) return;
                spark.AddRange(PerRole.All.Where(r => p.Member(r)!.HasStatus(P6StatusId.SparkOfDynamis)));
                brilliant.AddRange(PerRole.All.Where(r => p.Member(r)!.HasStatus(StatusId.BrilliantDynamis)));
                bossMarked = TopP6GauntletRun.Scenario(p).AlphaOmega!.Enemy!.HasStatus(StatusId.CodeMi);
            },
        });

        var (from, fullBy) = TopP6AlphaOmega.GaugeRebuildAfterCosmoMemory(1.9f);
        Assert.That(run.Passed, run.ToString);
        Assert.Multiple(() =>
        {
            Assert.That(gauge, Is.EqualTo(bars).Within(0.01f), "the gauge");
            Assert.That(gauge, Is.EqualTo(TopP6LimitBreaks.RebuiltBars(TopP6GauntletParts.GauntletStart(part), from, fullBy)).Within(0.01f), "the rebuild's");
            Assert.That(hudBars, Is.Not.Empty.And.All.EqualTo((int)bars), "the HUD's filled bars");
            Assert.That(spark, Is.EquivalentTo(sparked), "Spark of Dynamis");
            Assert.That(brilliant, Is.EquivalentTo(PerRole.All.Except(sparked)), "Brilliant Dynamis");
            Assert.That(bossMarked, Is.True, "Code: ****mi*");
            Assert.That(offTheirSpots, Is.Empty, "on the autos' spots");
        });
    }

    // ---- The run's start

    // The human's limit breaks in the parts the run plays.
    [TestCase(MainTank, CosmoMemory, "Your limit breaks: tank LB3 on Cosmo Memory and Magic Number 1")]
    [TestCase(RegenHealer, CosmoMemory, "Your limit breaks: healer LB3 on Magic Number 1")]
    [TestCase(MeleeDpsA, CosmoMemory, "Your limit breaks: melee LB3 on Alpha Omega right after Cosmo Dive 2's cast bar and during Run: ****mi*'s cast")]
    [TestCase(CasterDps, CosmoMemory, "Your limit breaks: caster LB3 on the Cosmo Comets")]
    [TestCase(MainTank, MagicNumber, "Your limit breaks: tank LB3 on Magic Number 1")]
    [TestCase(CasterDps, MagicNumber, "Your limit breaks: none in this run")]
    public void TheStartsChatLineNamesTheHumansLimitBreaks(PartyRole seat, TopP6GauntletPart from, string line)
    {
        IReadOnlyList<string> chat = [];
        var run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = seat,
            StopAt = 0.5f,
            Overrides = o => ((TopP6GauntletStateOverrides)o).StartFrom = from,
            Probe = p => chat = p.Chat,
        });

        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.That(chat, Has.Some.EndsWith(line), () => string.Join(Environment.NewLine, chat));
    }

    // A black mage in the main tank's seat can't press its limit break: the Gauntlet presses the seat's for it and holds the
    // human to none of them, so the run clears though the human presses nothing.
    [Test]
    public void AHumanWhoseJobCantPressItsSeatsLimitBreakIsHeldToNone()
    {
        IReadOnlyList<string> chat = [];
        var run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = MainTank,
            Overrides = o =>
            {
                ((FakeBattleCharas)AnoMech.Core.Native.Interfaces.Natives.BattleCharas).Player.ClassJob = (byte)JobId.BlackMage;
                var overrides = (TopP6GauntletStateOverrides)o;
                TopP6GauntletRun.Only(overrides, CosmoMemory);
                overrides.PlayerPressesItsOwnLimitBreaks = true;
            },
            Probe = p => chat = p.Chat,
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(chat, Has.Some.Contains("Your limit breaks: none, since your job can't press your seat's"), () => string.Join(Environment.NewLine, chat));
    }

    // "Resolve your own actions" carries the human's limit breaks; without it a start is refused, unless a debug driver
    // walks the player.
    [TestCase(true, false, false)]
    [TestCase(false, false, true)]
    [TestCase(false, true, false)]
    public void ItNeedsResolveYourOwnActions(bool userActions, bool debugDriven, bool refused)
    {
        var fake = FakeGame.Install();
        fake.UserActions.Enabled = userActions;
        var debugDriver = AnoMech.Core.SimObjects.DebugBotControl.Enabled;
        AnoMech.Core.SimObjects.DebugBotControl.Enabled = debugDriven;
        try
        {
            var conflicts = new TopP6GauntletScenario().SettingsConflicts;
            Assert.That(conflicts, refused ? Is.EqualTo(new[] { TopP6GauntletScenario.ResolveYourOwnActionsIsOff }) : Is.Empty);
        }
        finally
        {
            AnoMech.Core.SimObjects.DebugBotControl.Enabled = debugDriver;
        }
    }

    // ---- The limit breaks the fight asks for, beyond what the per-role suites (TopP6GauntletLimitBreak*Tests) cover

    // R1 presses from the east instead of A, its line running west through Alpha Omega between the meteors, then goes
    // back to A for the flares.
    [Test]
    public void APhysRangedLineThatMissesTheMeteorsLetsThemEnrage()
    {
        var run = HumanPresses(CosmoMeteor, PhysRangedDps, p =>
        {
            p.World.Events.Add(18.5f, () => TimelineRun.Place(p.World, PhysRangedDps, 13f, 0f));
            TopP6HumanLimitBreak.Press(p.World, 19f, TopP6GauntletRun.LimitBreak3(PhysRangedDps), () => TopP6GauntletRun.Scenario(p).AlphaOmega?.Enemy);
            p.World.Events.Add(24f, () => TimelineRun.Place(p.World, PhysRangedDps, 0f, -13f));
        });

        AssertEveryoneDied(run, P6ActionId.CosmoMeteorEnrage, $"Cosmo Meteor ({TopP6CosmoMeteor.MeteorsEnraged})", 32.3f, prefixOnly: true);
    }

    [Test]
    public void AHumanEnrageHolderWhoPressesDefeatsAlphaOmega()
    {
        var run = HumanPresses(MagicNumber, MeleeDpsA,
            p => TopP6HumanLimitBreak.Press(p.World, 33f, TopP6GauntletRun.LimitBreak3(MeleeDpsA), () => TopP6GauntletRun.Scenario(p).AlphaOmega?.Enemy));

        Assert.That(run.Passed, run.ToString);
        Assert.That(run.Elapsed, Is.LessThan(50f), "the run ends once Alpha Omega has fallen");
    }

    // The human in `seat` on the AI, pressing only what `press` schedules as the run starts; the run starts from `part`.
    private static ScenarioRun HumanPresses(TopP6GauntletPart part, PartyRole seat, Action<ScenarioProbe> press)
        => ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = seat,
            WriteArtifactsOnFailure = false,
            Overrides = o =>
            {
                var overrides = (TopP6GauntletStateOverrides)o;
                overrides.StartFrom = part;
                overrides.PlayerPressesItsOwnLimitBreaks = true;
            },
            Probe = p => { if (p.Crossed(0f)) press(p); },
        });

    private static void AssertEveryoneDied(ScenarioRun run, uint actionId, string cause, float at, bool prefixOnly = false)
    {
        Assert.That(run.Deaths.Select(d => d.Role), Is.EquivalentTo(PerRole.All), run.ToString);
        Assert.That(run.Deaths.Select(d => d.ActionId), Has.All.EqualTo(actionId), run.ToString);
        Assert.That(run.Deaths.Select(d => d.Cause), prefixOnly ? Has.All.EndsWith(cause[(cause.IndexOf('(') + 1)..]) : Has.All.EqualTo(cause), run.ToString);
        Assert.That(run.Deaths.Select(d => d.Time), Has.All.EqualTo(at).Within(0.05f), run.ToString);
    }

    private static void Expect(IReadOnlyList<Dictionary<string, string>> rows, string kind, uint id, float[] times, string what)
        => Assert.That(rows.Where(r => r["kind"] == kind && r["id"] == $"{id}").Select(r => float.Parse(r["t"], CultureInfo.InvariantCulture)).Order(),
                       Is.EqualTo(times.Order()).Within(0.1f), what);

    private static float[] Times(int each, params float[] times) => times.SelectMany(t => Enumerable.Repeat(t, each)).ToArray();
}
