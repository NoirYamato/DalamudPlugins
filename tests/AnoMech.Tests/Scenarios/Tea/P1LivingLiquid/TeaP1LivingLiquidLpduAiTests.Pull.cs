using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Tea;
using AnoMech.Scenarios.Tea.P1LivingLiquid;
using FFXIVClientStructs.FFXIV.Client.Game;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Tea.P1LivingLiquid.TeaP1LivingLiquidPart;
using static AnoMech.Scenarios.Tea.TeaP1Constants;
using static AnoMech.Tests.TeaP1LivingLiquidScenarioTests;
using Stage = AnoMech.Tests.TeaP1LivingLiquidStage;

namespace AnoMech.Tests;

// The pull: the strat's own presses, and the steps it takes when it sees something happen rather than on a beat.
//
//   - the main tank Provokes the Liquid Hand at 30.0 if it shows a palm and turns invulnerable at 34.0; the off tank
//     Provokes it back at 38.0. The off tank leaves for the Liquid's flank once the Hand is no longer on it;
//   - from 34.25 each damage dealer hits the doll of its quarter once it stands in reach of it (3.5 y for a melee, 25
//     for a ranged) with both bosses 5.2 y or more from it; from 36.0 a bot whose doll someone else hit takes that
//     one's doll, a melee walking to it first. When a ranged took a melee's, that doll goes out to it, into reach of
//     its own: the melee waits out the first Exhaust and goes from 38.75. A melee takes no doll from 47.0 and goes
//     north without one;
//   - from 38.75 each melee whose doll is under a quarter draws it through the nearer boss, standing 2.5 y past that
//     boss on a spot clear of the water, and then walks north; the ranged do the same from 49.3 and walk back out;
//   - each tank crosses the middle at 42.5 and takes its hold from 46.0, once it and its boss have come to rest.
//
// For the player's seat the strat presses only when nobody else will: the seat is driven as a bot that presses
// nothing, or a human sits in a tank's seat on a job without a tank's buttons.
public partial class TeaP1LivingLiquidLpduAiTests
{
    private const uint HeavySwing = 31;

    private static Roll ThePull(TeaP1OpenQuarter open = TeaP1OpenQuarter.North) => new() { First = Pull, Last = Pull, Open1 = open };

    [Test]
    public void StandsOnItsSpotsThroughThePull([Values] TeaP1OpenQuarter open) => AssertStands(ThePull(open), PerRole.All[(int)open]);

    // -- The presses --

    private sealed record Provoked(PartyRole By, bool AtTheHand, float At, bool ByHuman);

    private static List<Provoked> ProvokesOf(ScenarioProbe p)
    {
        var hand = (ulong)Stage.Fight(p).Hand!.GameObjectId;
        return Stage.Scenario(p).Presses!.Provokes.Select(provoke => new Provoked(Seat(provoke.By)!.Value, provoke.TargetId == hand, provoke.At, provoke.ByHuman)).ToList();
    }

    private static bool Invulnerable(SimCharacter member) => P1StatusId.Invulnerabilities.Any(member.HasStatus);

    private static PartyRole? HandIsOn(ScenarioProbe p) => Seat(Stage.Fight(p).Target(TeaP1Boss.LiquidHand));

    // The player's seat, driven as a bot that presses nothing, is pressed for like any bot's.
    [Test]
    public void TheTanksPressOnTheirBeats([Values(MeleeDpsA, MainTank, OffTank)] PartyRole player,
        [Values(TeaP1OpenQuarter.North, TeaP1OpenQuarter.Southwest)] TeaP1OpenQuarter open)
    {
        var handIsOn = new List<PartyRole?>();
        var invulnerable = new List<bool>();
        List<Provoked> provokes = [];
        var run = Play(ThePull(open), player, Random.Shared.Next(), p =>
        {
            foreach (var at in new[] { 30.6f, 30.75f, 38.6f, 38.75f })
                if (p.Crossed(at)) handIsOn.Add(HandIsOn(p));
            foreach (var at in new[] { 33.95f, 34.05f, 43.9f, 44.1f })
                if (p.Crossed(at)) invulnerable.Add(Invulnerable(p.Member(MainTank)!));
            if (p.Crossed(39f)) provokes = ProvokesOf(p);
        });

        Assert.That(run.Passed, run.ToString);
        Assert.Multiple(() =>
        {
            Assert.That(provokes.Select(provoke => (provoke.By, provoke.AtTheHand, provoke.ByHuman)), Is.EqualTo(new[] { (MainTank, true, false), (OffTank, true, false) }));
            Assert.That(provokes.Select(provoke => provoke.At), Is.EqualTo(new[] { 30.0f, 38.0f }).Within(0.02f));
            Assert.That(handIsOn, Is.EqualTo(new PartyRole?[] { OffTank, MainTank, MainTank, OffTank }), "the Hand's target at 30.6, 30.75, 38.6 and 38.75");
            Assert.That(invulnerable, Is.EqualTo(new[] { false, true, true, false }), "the main tank's invulnerability at 33.95, 34.05, 43.9 and 44.1");
        });
    }

    [Test]
    public void EachDamageDealerHitsTheDollOfItsQuarterAsSoonAsItStandsInReach([Values] TeaP1OpenQuarter open)
    {
        var roll = ThePull(open);
        var player = PerRole.All[4 + (int)open % 4];
        var seed = Random.Shared.Next();
        var holders = new Dictionary<TeaP1Quarter, (PartyRole? Holder, float? HitAt, float? TetheredAt)>();
        var run = Play(roll, player, seed, p =>
        {
            if (!p.Crossed(36.5f)) return;
            foreach (var doll in Stage.Fight(p).Dolls.All) holders[doll.QuarterIn(roll.Frame1)] = (Seat(doll.Holder), doll.HitAt, doll.TetheredAt);
        });
        var context = $"{roll}, player {player}, seed {seed}";

        Assert.That(run.Passed, () => $"{run}{Environment.NewLine}{context}");
        Assert.Multiple(() =>
        {
            foreach (var (seat, quarter) in Stage.DollOf)
            {
                var (holder, hitAt, tetheredAt) = holders[quarter];
                Assert.That(holder, Is.EqualTo(seat), $"the {quarter} doll's ({context})");
                Assert.That(hitAt, seat == MeleeDpsB ? Is.InRange(34.25f, 35.05f) : Is.EqualTo(34.25f).Within(0.03f), $"{seat}'s hit ({context})");
                Assert.That(tetheredAt - hitAt, Is.EqualTo(P1Duration.DollTethersAfterItsFirstHit).Within(0.03f), $"{seat}'s tether ({context})");
            }
        });
    }

    [Test]
    public void TheStratPressesNoneOfATanksButtonsForAPlayerWhoPressesItsOwn()
    {
        var roll = ThePull() with { PlayerPressesItsOwnActions = true };
        List<Provoked> asMainTank = [], asOffTank = [];
        var invulnerable = true;
        var mainTanks = Play(roll, MainTank, Random.Shared.Next(), p =>
        {
            if (!p.Crossed(34.3f)) return;
            (asMainTank, invulnerable) = (ProvokesOf(p), Invulnerable(p.Member(MainTank)!));
        }, stopAt: 34.35f);
        var offTanks = Play(roll, OffTank, Random.Shared.Next(), p =>
        {
            if (p.Crossed(39f)) asOffTank = ProvokesOf(p);
        }, stopAt: 39.05f);

        Assert.Multiple(() =>
        {
            Assert.That(mainTanks.Passed, mainTanks.ToString);
            Assert.That(asMainTank, Is.Empty, "no Provoke for the player's main tank");
            Assert.That(invulnerable, Is.False, "no invulnerability for the player's main tank");
            Assert.That(offTanks.Passed, offTanks.ToString);
            Assert.That(asOffTank.Select(provoke => provoke.By), Is.EqualTo(new[] { MainTank }), "no Provoke back for the player's off tank");
        });
    }

    [Test]
    public void TheStratHitsNoDollForAPlayerWhoPressesItsOwn([Values(MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps)] PartyRole player)
    {
        var roll = ThePull() with { PlayerPressesItsOwnActions = true };
        var hit = new Dictionary<TeaP1Quarter, bool>();
        var run = Play(roll, player, Random.Shared.Next(), p =>
        {
            if (!p.Crossed(38.4f)) return;
            foreach (var doll in Stage.Fight(p).Dolls.All) hit[doll.QuarterIn(roll.Frame1)] = doll.HasBeenHit;
        }, stopAt: 38.45f);

        Assert.That(run.Passed, run.ToString);
        Assert.That(hit, Is.EquivalentTo(Stage.DollOf.ToDictionary(its => its.Value, its => its.Key != player)));
    }

    // It has neither Provoke nor an invulnerability, so it is held to none of them: the run presses the seat's, on the
    // bots' beats.
    [Test]
    public void TheRunPressesATanksButtonsForItsSeatOnAnotherJob([Values(MainTank, OffTank)] PartyRole seat, [Values(JobId.Monk, JobId.WhiteMage)] JobId job)
    {
        var roll = ThePull() with { PlayerPressesItsOwnActions = true, PlayersJob = job };
        List<Provoked> provokes = [];
        var invulnerable = false;
        IReadOnlyList<string> chat = [];
        var run = Play(roll, seat, Random.Shared.Next(), p =>
        {
            if (p.Crossed(1f)) chat = p.Chat.ToList();
            if (p.Crossed(34.05f)) invulnerable = Invulnerable(p.Member(MainTank)!);
            if (p.Crossed(39f)) provokes = ProvokesOf(p);
        });

        Assert.That(run.Passed, run.ToString);
        Assert.Multiple(() =>
        {
            Assert.That(provokes.Select(provoke => (provoke.By, provoke.ByHuman)), Is.EqualTo(new[] { (MainTank, false), (OffTank, false) }));
            Assert.That(invulnerable, Is.True);
            Assert.That(chat.Where(said => said.Contains(seat == MainTank
                            ? "Your Provoke and invulnerability: none, since your job has none: the run presses them for your seat"
                            : "the run Provokes it back for your seat at 38 s, since your job has no Provoke")), Has.Exactly(1).Items);
        });
    }

    // A human off tank who keeps the Hand in the middle has it 13 y from the Liquid at the rim as it glows: a fist,
    // which needs them apart.
    [Test]
    public void TheMainTankDoesNotProvokeAHandThatShowsAFist()
    {
        bool? palm = null;
        List<Provoked> provokes = [];
        var run = Play(ThePull(), OffTank, Random.Shared.Next(), p =>
        {
            if (p.Crossed(29.4f)) palm = Stage.Fight(p).HandShowsAPalm;
            if (p.Crossed(34.3f)) provokes = ProvokesOf(p);
        }, stopAt: 34.35f, new Takeover(23.9f, new Vector2(0f, 3f)));

        Assert.That(run.Passed, run.ToString);
        Assert.That(palm, Is.False);
        Assert.That(provokes, Is.Empty);
    }

    // -- The dolls --

    // A human M1 hits M2's doll, the south-east one, and stands by it. M2 leaves it alone, and from 36.0 walks to M1's
    // and hits that.
    [Test]
    public void AMeleeWhoseDollSomeoneElseHitWalksToThatOnesDollAndTakesIt()
    {
        var stage = new Stage { First = Pull, Last = Pull };
        var (southeast, southwest) = (stage.DollSpot(TeaP1Quarter.Southeast), stage.DollSpot(TeaP1Quarter.Southwest));
        (PartyRole? Southeast, PartyRole? Southwest) before = default, after = default;
        var secondMeleeFromItsNewSpot = float.NaN;
        var run = Play(ThePull(), MeleeDpsA, Random.Shared.Next(), p =>
        {
            var dolls = Stage.Fight(p).Dolls;
            if (p.Crossed(33.6f))
                p.Fake.UserActions.Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, HeavySwing, (ulong)dolls.In(stage.Frame1, TeaP1Quarter.Southeast)!.Enemy.GameObjectId));
            (PartyRole?, PartyRole?) Holders() => (Seat(dolls.In(stage.Frame1, TeaP1Quarter.Southeast)!.Holder), Seat(dolls.In(stage.Frame1, TeaP1Quarter.Southwest)!.Holder));
            if (p.Crossed(35.95f)) before = Holders();
            if (!p.Crossed(38.4f)) return;
            after = Holders();
            secondMeleeFromItsNewSpot = Vector2.Distance(Flat(p.Member(MeleeDpsB)!.Position), stage.BesideItsDoll(MeleeDpsA));
        }, stopAt: 38.45f, new Takeover(33.5f, stage.BesideItsDoll(MeleeDpsB)));

        Assert.That(run.Passed, run.ToString);
        Assert.Multiple(() =>
        {
            Assert.That(Vector2.Distance(southeast, southwest), Is.GreaterThan(9f));
            Assert.That(before, Is.EqualTo(((PartyRole?)MeleeDpsA, (PartyRole?)null)), "until 36.0 the bot leaves both alone");
            Assert.That(after, Is.EqualTo(((PartyRole?)MeleeDpsA, (PartyRole?)MeleeDpsB)));
            Assert.That(secondMeleeFromItsNewSpot, Is.LessThan(Tolerance), "half a yalm off the south-west doll, away from the tornado beside it");
        });
    }

    // A human R1 steps out of its tornado's telegraph, hits M2's doll, the south-east one, and steps back: that doll
    // walks to 3.3 y from it. Half a yalm off R1's own doll, the north-east one, M2 would then stand in reach of both
    // as they first Exhaust, at 38.5. It waits that out where it is, takes R1's own doll after it, feeds that once the
    // bosses are on the middle and is north of it for the second busters.
    [Test]
    public void AMeleeWhoseDollARangedTookWaitsOutTheFirstExhaustBeforeItTakesTheRangedsOwn()
    {
        var stage = new Stage { First = Pull, Last = Pull };
        var (itsOwn, theRangeds) = (stage.BesideItsDoll(MeleeDpsB), stage.DollSpot(TeaP1Quarter.Northeast));
        float fromItsOwnSpot = float.NaN, takenDollFromTheRangedsOwn = float.NaN, fromItsSpotNorth = float.NaN;
        (PartyRole? Southeast, PartyRole? Northeast) holders = default;
        (float? HitAt, TeaP1Boss? FedTo) theRangedsOwn = default;
        var run = Play(ThePull(), PhysRangedDps, Random.Shared.Next(), p =>
        {
            var dolls = Stage.Fight(p).Dolls;
            if (p.Time < 33.5f) return;
            var (southeast, northeast) = (dolls.In(stage.Frame1, TeaP1Quarter.Southeast)!, dolls.In(stage.Frame1, TeaP1Quarter.Northeast)!);
            if (p.Crossed(33.6f)) p.Fake.UserActions.Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, 97, (ulong)southeast.Enemy.GameObjectId));
            if (p.Crossed(38.4f))
            {
                fromItsOwnSpot = Vector2.Distance(Flat(p.Member(MeleeDpsB)!.Position), itsOwn);
                takenDollFromTheRangedsOwn = Vector2.Distance(Flat(southeast.Enemy.Position), theRangeds);
            }
            if (!p.Crossed(55f)) return;
            holders = (Seat(southeast.Holder), Seat(northeast.Holder));
            theRangedsOwn = (northeast.HitAt, northeast.FedTo);
            fromItsSpotNorth = Vector2.Distance(Flat(p.Member(MeleeDpsB)!.Position), new Vector2(0.4f, -4.3f));
        }, stopAt: 57f, new Takeover(32.5f, new Vector2(11.5f, -8.4f)), new Takeover(35.45f, Stage.FirstRangedsBait));

        Assert.That(run.Passed, run.ToString);
        Assert.Multiple(() =>
        {
            Assert.That(takenDollFromTheRangedsOwn, Is.LessThan(8.8f), "the doll R1 took rests in reach of whoever stands by R1's own");
            Assert.That(fromItsOwnSpot, Is.LessThan(Tolerance), "M2 waits where it stood through the first Exhaust");
            Assert.That(holders, Is.EqualTo(((PartyRole?)PhysRangedDps, (PartyRole?)MeleeDpsB)));
            Assert.That(theRangedsOwn.HitAt, Is.InRange(38.75f, 41.5f), "hit once M2 has walked to it from 38.75");
            Assert.That(theRangedsOwn.FedTo, Is.Not.Null, "and fed before the second Exhaust");
            Assert.That(fromItsSpotNorth, Is.LessThan(Tolerance), "M2 on its spot north of the middle by 55.0");
        });
    }

    // A human main tank keeps the Living Liquid 3 y from the south-west doll's spot until 46.9. M1 waits beside that
    // doll, and at 47.0 goes north without it: hit that late it would not be fed before the second busters, which cleave
    // over its spot. The tank then steps away, the Liquid after it: it is 5.2 y gone a moment later, with M1 still in
    // reach of the doll, and M1 leaves it alone.
    // A tank that far from the middle tornado leaves that tornado's waves to others, so nobody is let die here.
    [Test]
    public void AMeleeKeptFromItsDollUntilFortySevenLeavesItAlone()
    {
        var stage = new Stage { First = Pull, Last = Pull };
        var liquidParks = new Vector2(-8.0f, 5.3f);
        var tankStands = liquidParks + Stage.LiquidStopsFromItsTank * Vector2.Normalize(liquidParks - new Vector2(0f, -4f));
        var hit = new List<bool>();
        float fromTheDoll = float.NaN, liquidFromTheDoll = float.NaN;
        var roll = ThePull() with { FirstEmboli = [new(TeaP1Tornado.West, TeaP1Boss.LiquidHand), new(TeaP1Tornado.East, TeaP1Boss.LivingLiquid)] };
        var run = ScenarioRun.Execute(typeof(TeaP1LivingLiquidScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = MainTank,
            StopAt = 48.45f,
            GodMode = true,
            Takeovers = [new Takeover(24.0f, tankStands), new Takeover(46.9f, new Vector2(-11f, 15f))],
            Overrides = o => roll.Pin((TeaP1LivingLiquidStateOverrides)o),
            Probe = p =>
            {
                var fight = Stage.Fight(p);
                if (p.Crossed(30f))
                    p.Fake.UserActions.Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, P1PlayerActionId.Provoke, (ulong)fight.Hand!.GameObjectId));
                if (p.Time < 33.5f) return;
                var doll = fight.Dolls.In(stage.Frame1, TeaP1Quarter.Southwest)!;
                if (p.Crossed(46.85f) || p.Crossed(48.4f)) hit.Add(doll.HasBeenHit);
                if (!p.Crossed(47.3f)) return;
                fromTheDoll = Vector2.Distance(Flat(p.Member(MeleeDpsA)!.Position), Flat(doll.Enemy.Position));
                liquidFromTheDoll = Vector2.Distance(Flat(fight.Position(TeaP1Boss.LivingLiquid)), Flat(doll.Enemy.Position));
            },
        });

        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.Multiple(() =>
        {
            Assert.That(liquidFromTheDoll, Is.GreaterThan(5.2f), "the Liquid has left the doll by 47.3");
            Assert.That(fromTheDoll, Is.LessThan(3.5f), "with M1 still in reach of it");
            Assert.That(hit, Is.EqualTo(new[] { false, false }));
        });
    }

    // A human main tank parks the Living Liquid 3 y from the south-west doll's spot and Provokes the Hand onto it, 5 y
    // from that spot. A doll hit there would be fed at once, with all its health. At 34.5 the tank moves to the rim and
    // both bosses follow.
    [Test]
    public void ABotLeavesItsDollAloneWhileABossStandsByItAndHitsItOnceTheBossHasGone()
    {
        var stage = new Stage { First = Pull, Last = Pull };
        var doll = stage.DollSpot(TeaP1Quarter.Southwest);
        var liquidParks = new Vector2(-8.0f, 5.3f);
        var liquidWaited = new Vector2(0f, -4f);
        var tankStands = liquidParks + Stage.LiquidStopsFromItsTank * Vector2.Normalize(liquidParks - liquidWaited);
        float liquidFromTheDoll = float.NaN, handFromTheDoll = float.NaN;
        var hitBy3445 = new Dictionary<TeaP1Quarter, bool>();
        float? hitAt = null;
        var run = Play(ThePull(), MainTank, Random.Shared.Next(), p =>
        {
            var fight = Stage.Fight(p);
            if (p.Crossed(30f))
                p.Fake.UserActions.Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, P1PlayerActionId.Provoke, (ulong)fight.Hand!.GameObjectId));
            if (p.Crossed(34.45f))
            {
                (liquidFromTheDoll, handFromTheDoll) = (Vector2.Distance(Flat(fight.Position(TeaP1Boss.LivingLiquid)), doll), Vector2.Distance(Flat(fight.Position(TeaP1Boss.LiquidHand)), doll));
                foreach (var its in fight.Dolls.All) hitBy3445[its.QuarterIn(stage.Frame1)] = its.HasBeenHit;
            }
            if (p.Crossed(36.1f)) hitAt = fight.Dolls.In(stage.Frame1, TeaP1Quarter.Southwest)!.HitAt;
        }, stopAt: 36.15f, new Takeover(24.0f, tankStands), new Takeover(34.5f, Stage.MainTanksBait));

        Assert.That(run.Passed, run.ToString);
        Assert.Multiple(() =>
        {
            Assert.That(liquidFromTheDoll, Is.EqualTo(3.0f).Within(0.3f), "the Liquid from the south-west doll");
            Assert.That(handFromTheDoll, Is.LessThan(5.2f), "the Hand from the south-west doll");
            Assert.That(hitBy3445, Is.EquivalentTo(Stage.DollOf.ToDictionary(its => its.Value, its => its.Key != MeleeDpsA)), "at 34.45 only the south-west doll is untouched");
            Assert.That(hitAt, Is.InRange(34.5f, 35.8f), "hit once both bosses are 5.2 y from it");
        });
    }

    // -- The steps taken on sight --

    private static List<(float At, PartyRole Seat)> Sent(TraceLog trace)
        => trace.Lines
            .Select(line => Regex.Match(line, @"\[AiManager\] Move@([\d.]+): (\w+) "))
            .Where(match => match.Success)
            .Select(match => (float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), Enum.Parse<PartyRole>(match.Groups[2].Value)))
            .ToList();

    // A step that looks every quarter second sends its bot once per decision: a walk sent again while its bot is still
    // rounding a puddle would pick the side to round it by afresh each time. The log writes a move's time to a tenth.
    [Test]
    public void AStepThatLooksEveryQuarterSecondSendsItsBotOnce([Values] TeaP1OpenQuarter open)
    {
        var roll = ThePull(open);
        var seed = Random.Shared.Next();
        var player = PerRole.All[((int)open * 3 + 1) % 8];
        var context = $"{roll}, player {player}, seed {seed}";
        TraceLog? trace = null;
        var run = Play(roll, player, seed, _ => trace ??= (TraceLog)(object)Plugin.Log);

        Assert.That(run.Passed, () => $"{run}{Environment.NewLine}{context}");
        var sent = Sent(trace!);
        int Moves(PartyRole seat, float from, float to) => sent.Count(move => move.Seat == seat && move.At >= from - 0.06f && move.At <= to + 0.06f);
        Assert.Multiple(() =>
        {
            Assert.That(Moves(MainTank, 24.1f, 42.4f), Is.Zero, $"the main tank holds its bait ({context})");
            Assert.That(Moves(OffTank, 24.1f, 42.4f), Is.EqualTo(1), $"the off tank's walk to the Liquid's flank ({context})");
            foreach (var tank in Tanks)
            {
                Assert.That(Moves(tank, 42.5f, 42.5f), Is.EqualTo(1), $"{tank}'s walk across the middle ({context})");
                Assert.That(Moves(tank, 42.6f, 60.5f), Is.EqualTo(1), $"{tank}'s hold ({context})");
            }
            foreach (var melee in new[] { MeleeDpsA, MeleeDpsB })
            {
                Assert.That(Moves(melee, 33.6f, 38.7f), Is.Zero, $"{melee} stays by its doll ({context})");
                Assert.That(Moves(melee, 38.8f, 60.5f), Is.EqualTo(2), $"{melee}'s walk past a boss and its walk north ({context})");
            }
            foreach (var ranged in new[] { PhysRangedDps, CasterDps })
            {
                Assert.That(Moves(ranged, 42.6f, 49.2f), Is.Zero, $"{ranged} waits out the second Exhaust ({context})");
                Assert.That(Moves(ranged, 49.3f, 60.5f), Is.EqualTo(2), $"{ranged}'s walk past a boss and its walk back out ({context})");
            }
            foreach (var healer in new[] { RegenHealer, ShieldHealer })
                Assert.That(Moves(healer, 24.1f, 60.5f), Is.EqualTo(1), $"{healer} leaves its split spot once, at 42.5 ({context})");
        });
    }

    // Its boss follows it across the middle and stops there; the hold, hung off where the boss stands, is taken once
    // both have come to rest.
    [Test]
    public void EachTankTakesItsHoldBesideItsBossOnceBothHaveComeToRestOnTheMiddle([Values] TeaP1OpenQuarter open)
    {
        var roll = ThePull(open);
        var seed = Random.Shared.Next();
        var player = PerRole.All[((int)open + 5) % 8];
        var context = $"{roll}, player {player}, seed {seed}";
        var problems = new List<string>();
        var wasStill = new Dictionary<PartyRole, bool>();
        var read = 0;
        var run = Play(roll, player, seed, p =>
        {
            var fight = Stage.Fight(p);
            var bosses = new[] { (Seat: MainTank, Boss: fight.Liquid!, Which: TeaP1Boss.LivingLiquid), (Seat: OffTank, Boss: fight.Hand, Which: TeaP1Boss.LiquidHand) };
            if (p.Time >= 45.5f && p.Time <= 49.1f)
                foreach (var (seat, boss, _) in bosses)
                {
                    var still = p.Member(seat)!.MoveDestination is null;
                    if (wasStill.GetValueOrDefault(seat) && !still && boss is { MoveDestination: not null })
                        problems.Add($"{seat} left for its hold at {F(p.Time)} with its boss still walking");
                    wasStill[seat] = still;
                }
            if (!p.Crossed(50f)) return;
            read++;
            var liquid = roll.Frame1.ToRelative(fight.Position(TeaP1Boss.LivingLiquid));
            var holds = new Dictionary<PartyRole, Vector2> { [MainTank] = Stage.Off(liquid, 3.9f, 135f), [OffTank] = new(-3.0f, 3.0f) };
            foreach (var (seat, boss, which) in bosses)
            {
                var stands = roll.Frame1.ToRelative(p.Member(seat)!.Position);
                var bossStands = roll.Frame1.ToRelative(fight.Position(which));
                if (Vector2.Distance(stands, holds[seat]) >= Tolerance) problems.Add($"{seat} at {stands}, {F(Vector2.Distance(stands, holds[seat]))} y from its hold {holds[seat]}");
                if (bossStands.Length() > (which == TeaP1Boss.LivingLiquid ? OnTheMiddleWithin + 0.1f : P1Geometry.HandStartsFromTheCentre)) problems.Add($"the {which} {F(bossStands.Length())} y off the middle");
                var off = MathF.Abs(Turn(boss!.Rotation, Facing(Flat(boss.Position), Flat(p.Member(seat)!.Position)))) * 180f / MathF.PI;
                if (off > FacesItsTanksWithinDegrees) problems.Add($"the {which} faces {F(off)} degrees off {seat}");
            }
        }, stopAt: 50.1f);

        Assert.That(run.Passed, () => $"{run}{Environment.NewLine}{context}");
        Assert.That(problems, Is.Empty, context);
        Assert.That(read, Is.EqualTo(1), context);
    }
}
