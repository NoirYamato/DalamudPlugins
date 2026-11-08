using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P6Gauntlet;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Scenarios.Top.TopP6Constants;
using static AnoMech.Tests.NegativeRun;
using static AnoMech.Tests.TopP6GauntletCosmoMeteorLpduAiTests;

namespace AnoMech.Tests;

// The Gauntlet's Cosmo Meteor part on its own, from t 0 (Gauntlet time - 164.8). Unless a test says otherwise the rolls
// are pinned: R2, M2 and the main tank hold the flares (E, S and W), so the other five stack north on R1, the shield
// healer taking the stack; each spread set hits the tanks and healers first, then the DPS. Everyone stands on LPDU's
// spots (x east, z south): the meteor clock at r 13 from A, R1 R2 H2 M2 OT M1 H1 MT.
//
//   1.9   Cosmo Meteor (5 s). 6.9: a bait (r 10, 4 s) under each player, deciding at 10.6 and landing at 10.9.
//   9.3   the adds spawn; 10.1 they show. The caster's limit break 3 (13.5) kills the comets as it lands (18.0), the
//         physical ranged's (24.0) the meteors (28.5); an add of a kind still alive at 22.2 (comets) or 32.3 (meteors)
//         enrages and kills everyone.
//   12.0 / 13.0, 18.0 / 19.0  the spreads (r 5), four, then the other four.
//   21.9  the flare markers. 30.0 the flares and the stack (r 6). 31.0 Cosmo Meteor's last visual.
public class TopP6GauntletCosmoMeteorScenarioTests
{
    private static readonly PartyRole[] PinnedFlares = [CasterDps, MeleeDpsB, MainTank];
    private const PartyRole PinnedStack = ShieldHealer;
    private static readonly PartyRole[] SupportsFirst = [MainTank, OffTank, RegenHealer, ShieldHealer];
    private static readonly PartyRole[][] PinnedFirstSpreads = [SupportsFirst, SupportsFirst];
    private static readonly PartyRole[] NorthStackers = [PhysRangedDps, OffTank, RegenHealer, ShieldHealer, MeleeDpsA];

    // 8.06 y from the north stack, 20.5 y from the west flare: clear of both.
    private static readonly Vector2 AwayFromTheStackAndEveryFlare = new(-7f, -17f);

    private static NegativeRun<TopP6GauntletScenario> Meteor(PartyRole player)
        => Negative<TopP6GauntletScenario>(player)
            .Overrides<TopP6GauntletStateOverrides>(o => Pin(o, PinnedFlares, PinnedStack, PinnedFirstSpreads));

    [Test]
    public void StayingInTheMiddleAsTheBaitsGoOffKills()
        => Meteor(RegenHealer)
            .TeleportAt(8f, to: Vector2.Zero)
            .ShouldKill(P6ActionId.CosmoMeteorBait, RegenHealer);

    // 10.6 y from the middle at the bait's decision: every bait dropped within 0.3 y of it misses.
    [Test]
    public void StandingJustOutsideTheBaitsAsTheyGoOffIsSafe()
    {
        var run = Run(RegenHealer, stopAt: 11.8f, new Takeover(8f, new Vector2(-10.6f, 0f)));

        Assert.That(run.Passed, run.ToString);
    }

    // M1, hit second, stands 3 y from the main tank as the main tank's spread lands; the main tank lives.
    [Test]
    public void StandingInSomeoneElsesSpreadKills()
        => Meteor(MeleeDpsA)
            .TeleportAt(11.5f, to: Clock[MainTank] + new Vector2(3f, 0f))
            .ShouldKill(P6ActionId.CosmoMeteorSpread, MeleeDpsA);

    // The off tank and the main tank are both hit first: each stands in the other's spread.
    [Test]
    public void TwoTargetsOfOneWaveBesideEachOtherBothDie()
        => Meteor(OffTank)
            .TeleportAt(11.5f, to: Clock[MainTank] + new Vector2(3f, 0f))
            .ShouldKill(P6ActionId.CosmoMeteorSpread, OffTank, MainTank);

    // The main tank, hit first, steps beside M1 before M1's spread a second later.
    [Test]
    public void StandingInTheSecondWavesSpreadKills()
        => Meteor(MainTank)
            .TeleportAt(12.5f, to: Clock[MeleeDpsA] + new Vector2(0f, -3f))
            .ShouldKill(P6ActionId.CosmoMeteorSpread, MainTank);

    // Nobody standing apart can take a second spread while the first one's vulnerability lasts, so it is set by hand.
    [Test]
    public void ASpreadOnAPlayerStillVulnerableKills()
        => Meteor(MainTank)
            .At(11.9f, p => p.Member(MainTank)!.AddStatus(StatusId.MagicVulnerabilityUp, P6Duration.SpreadVulnerability))
            .ShouldKill(P6ActionId.CosmoMeteorSpread, MainTank);

    [Test]
    public void AStackOfFourKillsAllFour()
        => Meteor(RegenHealer)
            .TeleportAt(23f, to: AwayFromTheStackAndEveryFlare)
            .ShouldKill(P6ActionId.CosmoMeteorStack, PhysRangedDps, OffTank, ShieldHealer, MeleeDpsA);

    // The main tank brings its flare into the north stack: the stack kills it, and its flare the five.
    [Test]
    public void AFlareInTheStackDiesAndItsFlareKillsTheStack()
        => Meteor(MainTank)
            .TeleportAt(29f, to: NorthStack + new Vector2(1f, 0f))
            .ShouldKill(P6ActionId.CosmoMeteorStack, MainTank)
            .ShouldKill(P6ActionId.CosmoMeteorFlare, NorthStackers);

    // H1 waits 6.5 y from the main tank's flare in the west; the stack, a player short, kills its four as well.
    [Test]
    public void StandingNearAFlareKills()
        => Meteor(RegenHealer)
            .TeleportAt(29f, to: new Vector2(-12f, 0f))
            .ShouldKill(P6ActionId.CosmoMeteorFlare, RegenHealer);

    // The main tank's flare lands 7.1 y from the north stack, outside it: the five die, the main tank lives.
    [Test]
    public void AFlareTooNearTheStackKillsTheStack()
        => Meteor(MainTank)
            .TeleportAt(29f, to: new Vector2(-5f, -18f))
            .ShouldKill(P6ActionId.CosmoMeteorFlare, NorthStackers);

    // The main tank's flare lands 6.2 y from M2's in the south: each kills the other.
    [Test]
    public void TwoFlaresTooNearEachOtherBothDie()
        => Meteor(MainTank)
            .TeleportAt(29f, to: new Vector2(-6f, 17f))
            .ShouldKill(P6ActionId.CosmoMeteorFlare, MainTank, MeleeDpsB);

    [Test]
    public void LeavingTheArenaKills()
        => Meteor(MainTank)
            .TeleportAt(2f, to: new Vector2(0f, 21f))
            .ShouldKill(TheEnvironment, MainTank);

    // The main tank leaves the arena beside M2's southern flare spot after the markers: its flare would have killed M2.
    [Test]
    public void APlayerWhoDiedSinceTheMarkersFiresNoFlare()
    {
        var run = Run(MainTank, stopAt: 30.5f, new Takeover(26f, new Vector2(0f, 21f)));

        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.That(run.Deaths.Select(d => (d.Role, d.ActionId)), Is.EqualTo(new[] { (MainTank, (uint?)TheEnvironment) }), run.ToString);
    }

    // The stack target leaves the arena: no stack lands, so the four left in the north are not a player short.
    [Test]
    public void ADeadStackTargetTakesNoStack()
    {
        var run = Run(ShieldHealer, stopAt: 30.5f, new Takeover(26f, new Vector2(0f, -21f)));

        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.That(run.Deaths.Select(d => (d.Role, d.ActionId)), Is.EqualTo(new[] { (ShieldHealer, (uint?)TheEnvironment) }), run.ToString);
    }

    // H1 joins the north stack 18.2 y from the main tank's flare in the west: the flare kills H1, and the stack, five
    // strong as it landed, kills nobody.
    [Test]
    public void ADeathToAFlareInsideTheStackLeavesTheOthersAlive()
    {
        var run = Run(RegenHealer, stopAt: 33f, new Takeover(29f, new Vector2(-4f, -11f)));

        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.That(run.Deaths.Select(d => (d.Role, d.ActionId)), Is.EqualTo(new[] { (RegenHealer, (uint?)P6ActionId.CosmoMeteorFlare) }), run.ToString);
    }

    // The same mistakes in godmode, where nobody dies: each player is reported once, for its first reason. The off tank
    // stands beside the main tank as both their spreads land; the main tank, hit first, steps beside M1 and takes M1's
    // spread while still vulnerable from its own. Those runs last a second past the spread, as long as an action takes
    // to deal its kills.
    [TestCase(OffTank, 11.5f, -6.19f, -9.19f, 13.5f, 2)]
    [TestCase(MainTank, 12.5f, -9.19f, 6.19f, 14.5f, 1)]
    [TestCase(RegenHealer, 29f, -4f, -11f, 33f, 1)]
    [TestCase(MainTank, 29f, 1f, -13f, 33f, 6)]
    public void GodmodeReportsEachPlayerOnce(PartyRole player, float at, float x, float z, float stopAt, int reported)
    {
        IReadOnlyList<string> chat = [];
        var run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = player,
            GodMode = true,
            StopAt = stopAt,
            WriteArtifactsOnFailure = false,
            Overrides = o => Pin((TopP6GauntletStateOverrides)o, PinnedFlares, PinnedStack, PinnedFirstSpreads),
            Takeovers = [new Takeover(at, new Vector2(x, z))],
            Probe = p => chat = p.Chat,
        });

        var reports = chat.Where(line => line.Contains(" died: ")).ToList();
        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.That(reports, Has.Count.EqualTo(reported), () => string.Join(Environment.NewLine, reports));
        Assert.That(reports.Select(line => line[..line.IndexOf(" died: ")]).Distinct().Count(), Is.EqualTo(reported),
                    () => string.Join(Environment.NewLine, reports));
    }

    [TestCase(8f, 0f, 0f, RegenHealer, "Cosmo Meteor puddle")]
    [TestCase(11.5f, -6.19f, -9.19f, MeleeDpsA, "another player's Cosmo Meteor spread")]
    [TestCase(23f, -7f, -17f, RegenHealer, "had 4 in it: it needs 5")]
    [TestCase(29f, 1f, -13f, MainTank, "stack holding a flare")]
    [TestCase(29f, -6f, 17f, MainTank, "within 20 y of a flare")]
    public void EveryDeathSaysWhy(float at, float x, float z, PartyRole player, string because)
    {
        var run = Run(player, stopAt: null, new Takeover(at, new Vector2(x, z)));

        Assert.That(run.Deaths, Is.Not.Empty, run.ToString);
        Assert.That(run.Deaths[0].Cause, Does.Contain(because), run.ToString);
    }

    [Test]
    public void ASecondSpreadSaysWhy()
    {
        var run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = MainTank,
            WriteArtifactsOnFailure = false,
            Overrides = o => Pin((TopP6GauntletStateOverrides)o, PinnedFlares, PinnedStack, PinnedFirstSpreads),
            Probe = p =>
            {
                if (p.Crossed(11.9f)) p.Member(MainTank)!.AddStatus(StatusId.MagicVulnerabilityUp, P6Duration.SpreadVulnerability);
            },
        });

        Assert.That(run.Deaths, Is.Not.Empty, run.ToString);
        Assert.That(run.Deaths[0].Cause, Does.Contain("Hit by two Cosmo Meteor spreads"), run.ToString);
    }

    // Set by hand to wear off just before the first spreads: the main tank's own lands on it harmlessly.
    [Test]
    public void AVulnerabilityThatHasWornOffKillsNobody()
    {
        var run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = MainTank,
            StopAt = 12.5f,
            WriteArtifactsOnFailure = false,
            Overrides = o => Pin((TopP6GauntletStateOverrides)o, PinnedFlares, PinnedStack, PinnedFirstSpreads),
            Probe = p =>
            {
                if (p.Crossed(11.9f - P6Duration.SpreadVulnerability))
                    p.Member(MainTank)!.AddStatus(StatusId.MagicVulnerabilityUp, P6Duration.SpreadVulnerability);
            },
        });

        Assert.That(run.Passed, run.ToString);
    }

    [Test]
    public void FollowsTheFightsTimeline()
    {
        SimTimeline? timeline = null;
        string? alphaOmega = null;
        var run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = MainTank,
            Overrides = o => Pin((TopP6GauntletStateOverrides)o, PinnedFlares, PinnedStack, PinnedFirstSpreads),
            Probe = p =>
            {
                timeline ??= p.Fake.Timeline;
                alphaOmega ??= p.World.Children.OfType<SimEnemy>().SingleOrDefault(e => e.BNpcBaseId == BNpcBaseId.AlphaOmega)?.EntityId.ToString("X8");
            },
        });

        Assert.That(run.Passed, run.ToString);
        var rows = timeline!.Rows;
        Assert.Multiple(() =>
        {
            Assert.That(Times(rows, "cast", P6ActionId.CosmoMeteor, alphaOmega), Is.EqualTo(new[] { 1.9f }).Within(0.05f), "Cosmo Meteor's cast");
            Assert.That(Times(rows, "cast", P6ActionId.CosmoMeteorBait), Is.EqualTo(Repeat(8, 6.9f)).Within(0.05f), "the baits drop");
            Assert.That(Times(rows, "ability", P6ActionId.CosmoMeteorBait), Is.EqualTo(Repeat(8, 10.9f)).Within(0.05f), "the baits land");
            Assert.That(Times(rows, "spawn", P6BNpcBaseId.CosmoMeteor), Is.EqualTo(Repeat(2, 9.3f)).Within(0.05f), "the meteors spawn");
            Assert.That(Times(rows, "spawn", P6BNpcBaseId.CosmoComet), Is.EqualTo(Repeat(6, 9.3f)).Within(0.05f), "the comets spawn");
            Assert.That(ActorControlTimes(rows, PopIn), Is.EqualTo(Repeat(8, 10.1f)).Within(0.05f), "the adds show");
            Assert.That(Times(rows, "ability", P6ActionId.CosmoMeteorSpread),
                        Is.EqualTo(new[] { 12.0f, 13.0f, 18.0f, 19.0f }.SelectMany(t => Repeat(4, t))).Within(0.05f), "the spreads");
            Assert.That(ActorControlTimes(rows, HeadMarker), Is.EqualTo(Repeat(3, 21.9f)).Within(0.05f), "the flare markers");
            Assert.That(Times(rows, "ability", P6ActionId.CosmoMeteorFlare), Is.EqualTo(Repeat(3, 30.0f)).Within(0.05f), "the flares");
            Assert.That(Times(rows, "ability", P6ActionId.CosmoMeteorStack), Is.EqualTo(new[] { 30.0f }).Within(0.05f), "the stack");
            Assert.That(Times(rows, "ability", P6ActionId.CosmoMeteorFinish, alphaOmega), Is.EqualTo(new[] { 31.0f }).Within(0.05f), "the last visual");
            Assert.That(rows.Where(r => r["kind"] is "ability" or "cast").Select(Time).Max(), Is.EqualTo(31.0f).Within(0.05f),
                        "nothing is cast after the last visual");
            Assert.That(run.Warnings, Has.None.Contains("dropped"), "every helper cast found a free helper");
        });
    }

    // The meteors north and south 11 y out, the comets on the other compass points 13 y out, all facing Alpha Omega. The
    // adds die as the limit breaks that kill them land: the caster's (pressed 13.5, landing 18.0), then the physical
    // ranged's (24.0, 28.5).
    [Test]
    public void TheAddsShowAroundAlphaOmegaAndDieAsTheLimitBreaksLand()
    {
        var alive = new Dictionary<float, (int Meteors, int Comets, int Targetable)>();
        var spots = new List<(uint BNpcBaseId, Vector2 At, float Rotation)>();
        var run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = MainTank,
            Overrides = o => Pin((TopP6GauntletStateOverrides)o, PinnedFlares, PinnedStack),
            Probe = p =>
            {
                var adds = p.World.Children.OfType<SimEnemy>()
                            .Where(e => e.BNpcBaseId is P6BNpcBaseId.CosmoMeteor or P6BNpcBaseId.CosmoComet).ToList();
                if (p.Crossed(9.5f))
                    spots.AddRange(adds.Select(a => (a.BNpcBaseId, Flat(a.Position), a.Rotation)));
                foreach (var at in new[] { 10.0f, 10.2f, 17.9f, 18.1f, 28.4f, 28.6f })
                    if (p.Crossed(at))
                        alive[at] = (adds.Count(a => a.BNpcBaseId == P6BNpcBaseId.CosmoMeteor && a.IsAlive()),
                                     adds.Count(a => a.BNpcBaseId == P6BNpcBaseId.CosmoComet && a.IsAlive()),
                                     adds.Count(a => a.IsAlive() && a.Targetable));
            },
        });

        Assert.That(run.Passed, run.ToString);
        Assert.Multiple(() =>
        {
            Assert.That(spots.Where(s => s.BNpcBaseId == P6BNpcBaseId.CosmoMeteor).Select(s => Bearing(s.At)).Order(),
                        Is.EqualTo(new[] { 0f, 180f }).Within(0.5f), "the meteors' bearings");
            Assert.That(spots.Where(s => s.BNpcBaseId == P6BNpcBaseId.CosmoComet).Select(s => Bearing(s.At)).Order(),
                        Is.EqualTo(new[] { 45f, 90f, 135f, 225f, 270f, 315f }).Within(0.5f), "the comets' bearings");
            Assert.That(spots.Select(s => (s.BNpcBaseId, MathF.Round(s.At.Length(), 1))).Distinct(),
                        Is.EquivalentTo(new[] { (P6BNpcBaseId.CosmoMeteor, P6Geometry.CosmoMeteorDistance), (P6BNpcBaseId.CosmoComet, P6Geometry.CosmoCometDistance) }),
                        "how far out");
            foreach (var (_, at, rotation) in spots)
                Assert.That(Vector2.Dot(new Vector2(MathF.Sin(rotation), MathF.Cos(rotation)), -Vector2.Normalize(at)), Is.GreaterThan(0.999f),
                            $"the add at {at} faces Alpha Omega");
            Assert.That(alive[10.0f], Is.EqualTo((2, 6, 0)), "hidden until 10.1");
            Assert.That(alive[10.2f], Is.EqualTo((2, 6, 8)), "shown and targetable");
            Assert.That(alive[17.9f], Is.EqualTo((2, 6, 8)), "before the caster's limit break lands");
            Assert.That(alive[18.1f], Is.EqualTo((2, 0, 2)), "the comets die as the caster's limit break lands");
            Assert.That(alive[28.4f], Is.EqualTo((2, 0, 2)), "before the physical ranged's limit break lands");
            Assert.That(alive[28.6f], Is.EqualTo((0, 0, 0)), "the meteors die as the physical ranged's limit break lands");
        });
    }

    [Test]
    public void EachRollLandsOnItsTargets()
    {
        PartyRole[][] firstSpreads = [[MeleeDpsA, PhysRangedDps, OffTank, ShieldHealer], [MainTank, MeleeDpsB, CasterDps, RegenHealer]];
        PartyRole[] flares = [PhysRangedDps, RegenHealer, MeleeDpsB];
        SimTimeline? timeline = null;
        var roleById = new Dictionary<string, PartyRole>();
        var run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = OffTank,
            Overrides = o => Pin((TopP6GauntletStateOverrides)o, flares, MeleeDpsA, firstSpreads),
            Probe = p =>
            {
                timeline ??= p.Fake.Timeline;
                if (roleById.Count == 0)
                    foreach (var role in PerRole.All) roleById[p.Member(role)!.EntityId.ToString("X8")] = role;
            },
        });

        Assert.That(run.Passed, run.ToString);
        var rows = timeline!.Rows;
        PartyRole[] Targets(uint actionId, float at)
            => rows.Where(r => r["kind"] == "ability" && r["id"] == $"{actionId}" && MathF.Abs(Time(r) - at) < 0.1f)
                   .Select(r => roleById[r["target"]]).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(Targets(P6ActionId.CosmoMeteorSpread, 12.0f), Is.EquivalentTo(firstSpreads[0]), "set 1, first four");
            Assert.That(Targets(P6ActionId.CosmoMeteorSpread, 13.0f), Is.EquivalentTo(PerRole.All.Except(firstSpreads[0])), "set 1, the other four");
            Assert.That(Targets(P6ActionId.CosmoMeteorSpread, 18.0f), Is.EquivalentTo(firstSpreads[1]), "set 2, first four");
            Assert.That(Targets(P6ActionId.CosmoMeteorSpread, 19.0f), Is.EquivalentTo(PerRole.All.Except(firstSpreads[1])), "set 2, the other four");
            Assert.That(rows.Where(r => r["kind"] is "ac" or "ac_self" && r["id"] == HeadMarker && r["p"].StartsWith($"0x{P6LockonId.Flare:X},"))
                            .Select(r => roleById[r["actor"]]), Is.EquivalentTo(flares), "the flare markers");
            Assert.That(Targets(P6ActionId.CosmoMeteorFlare, 30.0f), Is.EquivalentTo(flares), "the flares");
            Assert.That(Targets(P6ActionId.CosmoMeteorStack, 30.0f), Is.EqualTo(new[] { MeleeDpsA }), "the stack");
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void EveryoneOnTheStratsSpotsSurvives(int pins)
    {
        (PartyRole[] Flares, PartyRole Stack)[] rolls =
        [
            (PinnedFlares, PinnedStack),
            ([PhysRangedDps, RegenHealer, MeleeDpsB], MainTank),
            ([ShieldHealer, OffTank, MeleeDpsA], PhysRangedDps),
            ([PhysRangedDps, CasterDps, ShieldHealer], OffTank),
        ];
        var run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = MeleeDpsA,
            Overrides = o => Pin((TopP6GauntletStateOverrides)o, rolls[pins].Flares, rolls[pins].Stack),
        });

        Assert.That(run.Passed, run.ToString);
    }

    private static ScenarioRun Run(PartyRole player, float? stopAt, params Takeover[] takeovers)
        => ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = player,
            StopAt = stopAt,
            WriteArtifactsOnFailure = false,
            Overrides = o => Pin((TopP6GauntletStateOverrides)o, PinnedFlares, PinnedStack, PinnedFirstSpreads),
            Takeovers = takeovers,
        });

    private const string PopIn = "0x24";
    private const string HeadMarker = "0x22";

    private static float[] Times(IEnumerable<Dictionary<string, string>> rows, string kind, uint id, string? actor = null)
        => rows.Where(r => r["kind"] == kind && r["id"] == $"{id}" && (actor is null || r["actor"] == actor))
               .Select(Time)
               .Order()
               .ToArray();

    private static float[] ActorControlTimes(IEnumerable<Dictionary<string, string>> rows, string category)
        => rows.Where(r => r["kind"] is "ac" or "ac_self" && r["id"] == category).Select(Time).Order().ToArray();

    private static float[] Repeat(int count, float time) => Enumerable.Repeat(time, count).ToArray();

    private static float Time(Dictionary<string, string> row) => float.Parse(row["t"], CultureInfo.InvariantCulture);

    private static float Bearing(Vector2 at) => (MathF.Atan2(at.X, -at.Y) * 180f / MathF.PI + 360f) % 360f;
}
