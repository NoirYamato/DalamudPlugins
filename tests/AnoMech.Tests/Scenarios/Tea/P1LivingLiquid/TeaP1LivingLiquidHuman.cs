using System.Numerics;
using AnoMech.Core.Game;
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
using Roll = AnoMech.Tests.TeaP1LivingLiquidLpduAiTests.Roll;
using Stage = AnoMech.Tests.TeaP1LivingLiquidStage;

namespace AnoMech.Tests;

// A player's speeds; a sprinter sprints the long walks.
public enum TeaP1HumanPace { Walking, Sprinting }

// Where the human stands for a bait: on its spot, a yalm farther from the Living Liquid, or a yalm to the side; never
// in water it can see.
public enum TeaP1HumanStance { Exact, AYalmOut, AYalmAside }

// What a human in the stack does with a Drainage tether that starts on it.
public enum TeaP1HumanHolder { Stays, WalksToMeetItsTank, WalksAwayFromItsTank, WalksItAcrossTheOtherTank }

public enum TeaP1HumanCleanses { InItsOwnOrder, InTheOtherHealersOrder, Never }

// Where a tank takes its boss once the Liquid Hand shows a palm: through the middle and on until its boss stands on
// it, as the strat does; onto the middle and no farther; or beside the other tank, short of the middle.
public enum TeaP1HumanBringsItsBoss { ThroughTheMiddle, OntoTheMiddle, BesideTheOtherTank, Never }

// How one scripted human plays its seat; the defaults are the strat's.
internal sealed record TeaP1LivingLiquidHumanPlay(PartyRole Seat, TeaP1HumanPace Pace, float Late)
{
    public TeaP1HumanStance Stance { get; init; }

    // The main tank stacks this many degrees clockwise of south of the Living Liquid for Protean 1's telegraph.
    public float StacksOffSouth { get; init; }

    // The main tank stands where the guides draw it, at the edge of its reach: 7.4 y south-east of the middle from
    // Protean 1's second set through Drainage, and (-0.8, -7.4) for Protean 2's stack. The Living Liquid, which
    // reaches 7 y, walks up to 6 y from it.
    public bool MaxMelee { get; init; }

    // The main tank turns the Living Liquid this many degrees clockwise of north for Protean 2's telegraph.
    public float TurnsItOffNorth { get; init; }

    // The main tank never walks the Living Liquid back onto the middle: it turns it north where its fetch left it.
    public bool LeavesTheLiquidWhereItStands { get; init; }

    // The main tank leads the Living Liquid back onto the middle, and turns it north, this long after the strat's beat.
    public float LeadsItBackLate { get; init; }

    // The main tank never turns the Living Liquid: it waits for each telegraph's bar where the start put it, in the
    // Liquid's face, and plays its spots from there on.
    public bool NeverTurnsTheLiquid { get; init; }

    public bool NeverFetches { get; init; }

    // A tank takes its tether as far out along it as the tornado's water lets it, not where the line is nearest.
    public bool TakesItsTetherByItsTornado { get; init; }

    // On top of the reaction time, for the tether alone.
    public float FetchesLate { get; init; }

    // A tank that holds its own tether walks through the other one's line, ahead of its holder, and back.
    public bool WalksThroughTheOtherTether { get; init; }

    public TeaP1HumanHolder Holder { get; init; }

    public TeaP1HumanCleanses Cleanses { get; init; }

    // A healer's first Esuna comes this many of its 2.5 s later.
    public int GcdsLate { get; init; }

    // A healer or a ranged stands on its far spot from the start, alone in the one telegraphed wave aimed at it.
    public bool TakesItsOwnTelegraphedWave { get; init; }

    public bool PressesAntiKnockback { get; init; }

    // The off tank sets off for the middle this long after the palm shows; null: never.
    public float? BringsTheHandIn { get; init; } = 0f;

    public TeaP1HumanBringsItsBoss BringsItsBoss { get; init; }

    // The off tank waits for the glow in the middle with the six, the Liquid Hand beside it, which shows a fist.
    public bool WaitsForTheGlowInTheMiddle { get; init; }

    // Shown a fist, the off tank walks this far straight away from the Living Liquid; null: it stays.
    public float? StepsAwayOnAFist { get; init; }

    // It waits where the six stood for Drainage, and steps out of a new puddle once it sees one round it.
    public bool StaysOutForTheCascade { get; init; }

    // The player's job as its client has it; null leaves its seat's own.
    public JobId? Job { get; init; }
}

// The sunny-day soak drives the player's seat with the bots' own AI. Here the seat is a human from the first frame,
// who plays the strat from what a player can see: the tornadoes (the quarter without one is its north), where the
// Living Liquid stands, each telegraph as its cast bar starts, each unseen set as it lands, the Drainage tethers, the
// Throttle icons on the party list, and the Liquid Hand's palm. At each of those it sets off for its seat's next spot
// (TeaP1LivingLiquidStage's, hung off the Liquid as it stands then) the strat's own beat and a reaction time later, at
// a player's pace, round the water it can see.
//
// A tank takes its own tether out of its line, wherever it is, and walks it to its spot; a healer presses Esuna every
// 2.5 s on the next seat of its order that still shows Throttle. Nothing presses for it.
internal sealed class TeaP1LivingLiquidHuman
{
    internal const int LpduStrat = 0;

    private const float WalkSpeed = 6f, SprintSpeed = 7.8f, SprintsWalksOver = 12f;
    private const float KeepsClearOfTheWater = P1Geometry.PuddleRadius + 0.3f;
    private const float Gcd = 2.5f;
    private const float FirstCleanseAfterThrottle = 0.7f;
    private const float TetherBeat = 0.25f;
    private const float AheadOfItsHolder = 1.5f;
    private const float PressesBeforeTheTelegraphLands = 2f;
    private const float TheLiquidLooksOffTheMiddleBeyond = 0.7f;
    private const float TheLiquidStopsFromItsTank = 6.0f, FarEnoughToSetTheLiquidOff = 7.4f;

    private const string Start = "start", Thrown = "thrown", Telegraph = "telegraph", TelegraphFired = "telegraph fired", Set = "set";
    private const string Tethers = "tethers", Drainage = "drainage", NewTornadoes = "new tornadoes", Throttle = "throttle";
    private const string CascadeLands = "cascade lands";
    private const string TornadoAim = "tornado aim", UnseenTornadoWaves = "unseen tornado waves", Glow = "glow";

    internal static readonly PartyRole[] FarFour = [RegenHealer, ShieldHealer, PhysRangedDps, CasterDps];

    private sealed record Leg(string Cue, float Beat, Func<ScenarioProbe, Vector2?> To, bool Follows = false, bool Sprints = false, bool IfItComes = false)
    {
        public bool SetOff { get; set; }
    }

    private readonly Roll roll;
    private readonly TeaP1LivingLiquidHumanPlay play;
    private readonly PartyRole seat;
    private readonly float late;
    private readonly List<Leg> legs = [];
    private readonly Dictionary<string, float> seen = new() { [Start] = 0f };
    private readonly Dictionary<string, (int Count, float Last)> counted = [];

    private int rowsRead;
    private Vector2? goal;
    private float speed = WalkSpeed;
    private Vector2? put;
    private Vector2 previous;
    private bool sliding;
    private float freeAt;
    private float lastTick;
    private bool hadTornadoes;
    private Stage? stage;
    private int gcds;
    private bool pressedItsCooldown, pressedItsAntiKnockback, everHeld;
    private int crossing;

    public TeaP1OpenQuarter? Open1Read { get; private set; }
    public TeaP1OpenQuarter? Open2Read { get; private set; }
    public int Slides { get; private set; }
    public bool? SawAPalm { get; private set; }
    public List<PartyRole> Cleansed { get; } = [];
    public TeaP1Tornado? HeldAsDrainageWentOff { get; private set; }
    public bool HeldTwoAtOnce { get; private set; }
    public float FarthestTheLiquidStoodOffTheMiddle { get; private set; }
    public IEnumerable<string> WalksNeverSetOffOn => legs.Where(leg => !leg.SetOff && !leg.IfItComes).Select(leg => $"{leg.Cue} + {leg.Beat}");

    public TeaP1LivingLiquidHuman(Roll roll, TeaP1LivingLiquidHumanPlay play)
    {
        this.roll = roll;
        this.play = play;
        seat = play.Seat;
        late = play.Late;
        if (roll.Plays(Protean1)) legs.AddRange(ThroughProteanOne());
        if (roll.Plays(Cascade2)) legs.AddRange(FromCascadeTwo());
    }

    // One run with the human in its seat from the first frame, as the plugin has it: nothing but that tells the strat
    // to press nothing for it. A tank's own cooldowns count.
    public static (ScenarioRun Run, TeaP1LivingLiquidHuman Human) Play(Roll roll, TeaP1LivingLiquidHumanPlay play, int seed,
        bool mayLose = false, Action<TeaP1LivingLiquidStateOverrides>? pin = null, Action<ScenarioProbe>? probe = null, float? stopAt = null)
    {
        var human = new TeaP1LivingLiquidHuman(roll, play);
        var run = ScenarioRun.Execute(typeof(TeaP1LivingLiquidScenario), LpduStrat, seed, new ScenarioRunOptions
        {
            PlayerRole = play.Seat,
            StopAt = stopAt,
            UserActionsEnabled = play.Seat.IsTank(),
            WriteArtifactsOnFailure = !mayLose,
            Configure = play.Job is { } job ? _ => ((FakeBattleCharas)Natives.BattleCharas).Player.ClassJob = (byte)job : null,
            Overrides = o =>
            {
                var overrides = (TeaP1LivingLiquidStateOverrides)o;
                roll.Pin(overrides);
                pin?.Invoke(overrides);
            },
            Takeovers = [new Takeover(0f, null)],
            Probe = p =>
            {
                human.Tick(p);
                probe?.Invoke(p);
            },
        });
        return (run, human);
    }

    // -- The walks --

    private IEnumerable<Leg> ThroughProteanOne()
    {
        if (play.TakesItsOwnTelegraphedWave)
        {
            yield return new(Start, 1.5f, p => Spot(p, Protean1, 5.3f));
            yield return new(Thrown, 0f, p => Spot(p, Protean1, 5.3f), IfItComes: true);
        }
        else
        {
            if (seat != MainTank || !play.NeverTurnsTheLiquid)
                yield return new(Start, 1.5f, p => seat == MainTank && play.StacksOffSouth != 0f
                    ? Frame1.ToArena(Stage.Off(TheLiquidIn(p, Frame1), 3.3f, 180f + play.StacksOffSouth))
                    : Spot(p, Protean1, 1.5f));
            yield return new(Nth(Telegraph, Protean1), 0.5f, p => Bait(p, Spot(p, Protean1, 5.3f)));
        }
        yield return new(Nth(Set, Protean1), 0.05f, p => Bait(p, Spot(p, Protean1, 9.95f)));
        if (seat != MainTank || !play.MaxMelee)
            yield return new(Nth(Set, Protean1, 2), 0.05f, p => Spot(p, Protean1, 13.05f));
    }

    private IEnumerable<Leg> FromCascadeTwo()
    {
        if (play.StaysOutForTheCascade)
        {
            if (!roll.Plays(Protean1)) yield return new(Start, 0.5f, WhereItStoodForDrainage);
            yield return new(NewTornadoes, 0f, p => InTheWater(p) ? Spot(p, Cascade2, 0.9f) : null);
        }
        else if (roll.Plays(Protean1))
            yield return new(Drainage, 0.1f, p => Spot(p, Cascade2, 0.9f));

        if (seat != MainTank) yield return new(Throttle, 1.5f, p => Spot(p, Cascade2, 12.0f));
        else if (!play.NeverTurnsTheLiquid)
        {
            if (!play.LeavesTheLiquidWhereItStands)
                yield return new(CascadeLands, 0.5f + play.LeadsItBackLate, p => TheLiquidLooksOffTheMiddle(p) ? PastTheMiddleFromTheLiquid(p) : null);
            yield return new(CascadeLands, 3.6f + play.LeadsItBackLate, p => play.MaxMelee ? Frame2.ToArena(new Vector2(-0.8f, -7.4f))
                : play.TurnsItOffNorth != 0f ? Frame2.ToArena(Stage.Off(TheLiquidIn(p, Frame2), 3.9f, play.TurnsItOffNorth))
                : Spot(p, Cascade2, 9.5f), Follows: true);
        }

        yield return new(Nth(Telegraph, Cascade2), 0.5f, p => Bait(p, Spot(p, Cascade2, 18.7f)));
        if (seat == MeleeDpsA) yield return new(Nth(TelegraphFired, Cascade2), 0.1f, p => Bait(p, Spot(p, Cascade2, 21.3f)));
        if (seat == MainTank) yield return new(Nth(Set, Cascade2), 0.05f, p => Spot(p, Cascade2, 23.35f));
        if (FarFour.Contains(seat)) yield return new(TornadoAim, 0.05f, p => Bait(p, Spot(p, Cascade2, 24.25f)), Sprints: play.Pace == TeaP1HumanPace.Sprinting);
        if (seat is MeleeDpsA or MeleeDpsB) yield return new(TornadoAim, 0.05f, p => Bait(p, Spot(p, Cascade2, 24.25f)));
        if (seat == OffTank) yield return new(TornadoAim, 1.0f, p => Bait(p, Spot(p, Cascade2, 27.0f)));
        yield return new(UnseenTornadoWaves, 0.05f, p => seat == OffTank && play.WaitsForTheGlowInTheMiddle ? Vector2.Zero : Spot(p, Cascade2, 29.35f));
        if (seat == OffTank && play.StepsAwayOnAFist is { } steps)
            yield return new(Glow, 0.3f, p => SawAPalm == false ? Flat(p.Member(seat)!.Position) + AwayFromTheLiquid(p) * steps : null);
        if (play.BringsItsBoss == TeaP1HumanBringsItsBoss.Never) yield break;
        if (seat != OffTank) yield return new(Glow, 0.3f, p => SawAPalm == true ? TakesItsBossTo(p) : null);
        else if (play.BringsTheHandIn is { } bringsLate) yield return new(Glow, 0.3f + bringsLate, p => SawAPalm == true ? TakesItsBossTo(p) : null);
    }

    // The other tank's bot ends its boss's stopping distance past the middle; beside it is a yalm to its side.
    private Vector2? TakesItsBossTo(ScenarioProbe p)
    {
        if (!seat.IsTank() || play.BringsItsBoss == TeaP1HumanBringsItsBoss.ThroughTheMiddle) return Spot(p, Cascade2, 33.5f);
        if (play.BringsItsBoss == TeaP1HumanBringsItsBoss.OntoTheMiddle) return Vector2.Zero;
        var others = OtherTanksSpot(p);
        var aside = others.Length() < 1e-3f ? Vector2.UnitX : Vector2.Normalize(new Vector2(-others.Y, others.X));
        return others + aside;
    }

    private Vector2 OtherTanksSpot(ScenarioProbe p)
    {
        var last = TheStage.Cues().Single(cue => MathF.Abs(cue.At - TheStage.C2(33.5f)) < 0.001f);
        return TheStage.ArenaSpots(p, last)[seat == MainTank ? OffTank : MainTank];
    }

    private Vector2 AwayFromTheLiquid(ScenarioProbe p) => Vector2.Normalize(Flat(p.Member(seat)!.Position) - TheLiquid(p));

    // The n-th of a part's cues of one kind, as the run counts them from its start.
    private string Nth(string cue, TeaP1LivingLiquidPart part, int n = 1)
    {
        var perPart = cue == Set ? 2 : 1;
        return $"{cue} {(part == Cascade2 && roll.Plays(Protean1) ? perPart : 0) + n}";
    }

    public void Tick(ScenarioProbe p)
    {
        var delta = p.Time - lastTick;
        lastTick = p.Time;
        if (p.Member(seat) is not { } me || !me.IsAlive()) return;
        See(p);
        Press(p);
        var at = Flat(me.Position);
        if (StillSliding(p, at)) return;
        if (p.Time < freeAt) return;
        SetOffOnWhatItHasSeen(p, at);
        PlayItsTether(p, me, at);
        Walk(p, me, at, delta);
    }

    // -- What it sees --

    private void See(ScenarioProbe p)
    {
        var rows = p.Fake.Timeline.Rows;
        for (; rowsRead < rows.Count; rowsRead++)
        {
            var row = rows[rowsRead];
            if (row["kind"] is not ("cast" or "ability") || !uint.TryParse(row["id"], out var id)) continue;
            var cue = (row["kind"], id) switch
            {
                ("cast", P1ActionId.ProteanWaveFromTheLiquid) => Telegraph,
                ("ability", P1ActionId.ProteanWaveFromTheLiquid) => TelegraphFired,
                ("ability", P1ActionId.ProteanWaveStraightAhead) => Set,
                ("ability", P1ActionId.Cascade) => CascadeLands,
                ("cast", P1ActionId.TornadoWave) => TornadoAim,
                ("ability", P1ActionId.TornadoWaveUnseen) => UnseenTornadoWaves,
                _ => null,
            };
            if (cue is null) continue;
            var (count, last) = counted.GetValueOrDefault(cue, (0, float.NegativeInfinity));
            if (p.Time - last > 0.5f) count++;
            counted[cue] = (count, p.Time);
            seen.TryAdd($"{cue} {count}", p.Time);
            seen.TryAdd(cue, p.Time);
        }

        var fight = Stage.Fight(p);
        FarthestTheLiquidStoodOffTheMiddle = MathF.Max(FarthestTheLiquidStoodOffTheMiddle, TheLiquid(p).Length());
        if (fight.DrainageTethersOut.Count > 0) seen.TryAdd(Tethers, p.Time);
        var standing = fight.Tornadoes.Standing is not null;
        if (standing && !hadTornadoes)
        {
            if (roll.Plays(Protean1) && Open1Read is null) Open1Read = TheQuarterWithNoTornado(fight);
            else
            {
                Open2Read = TheQuarterWithNoTornado(fight);
                seen.TryAdd(NewTornadoes, p.Time);
            }
            stage = null;
        }
        if (!standing && hadTornadoes) seen.TryAdd(Drainage, p.Time);
        hadTornadoes = standing;
        if (PerRole.All.Any(role => p.Member(role) is { } member && member.HasStatus(P1StatusId.Throttle))) seen.TryAdd(Throttle, p.Time);
        if (fight.HandShowsAPalm is { } palm && seen.TryAdd(Glow, p.Time)) SawAPalm = palm;
    }

    // The two side tornadoes stand opposite each other, so the three centres sum to the middle one's, opposite the
    // open quarter.
    private static TeaP1OpenQuarter TheQuarterWithNoTornado(TeaP1LivingLiquid fight)
    {
        var sum = Enum.GetValues<TeaP1Tornado>().Aggregate(Vector2.Zero, (total, tornado) => total + Flat(fight.Tornadoes.Centre(tornado)!.Value));
        var bearing = MathF.Atan2(-sum.X, sum.Y) * 180f / MathF.PI;
        return (TeaP1OpenQuarter)(((int)MathF.Round(bearing / 45f) % 8 + 8) % 8);
    }

    private TeaP1TornadoLayout Frame1 => new(Open1Read ?? roll.Open1);
    private TeaP1TornadoLayout Frame2 => new(Open2Read ?? TeaP1OpenQuarter.North);

    private static Vector2 TheLiquid(ScenarioProbe p) => Flat(Stage.Fight(p).Position(TeaP1Boss.LivingLiquid));

    private static Vector2 TheLiquidIn(ScenarioProbe p, TeaP1TornadoLayout frame) => frame.ToRelative(TheLiquid(p));

    private static bool TheLiquidLooksOffTheMiddle(ScenarioProbe p) => TheLiquid(p).Length() > TheLiquidLooksOffTheMiddleBeyond;

    // Where the start's chat line sends a main tank to lead the Living Liquid back: 6 y past the middle on the far
    // side from it, or far enough from it to set it off.
    private static Vector2 PastTheMiddleFromTheLiquid(ScenarioProbe p)
    {
        var liquid = TheLiquid(p);
        return -Vector2.Normalize(liquid) * MathF.Max(TheLiquidStopsFromItsTank, FarEnoughToSetTheLiquidOff - liquid.Length());
    }

    private static List<Vector2> TheWater(ScenarioProbe p)
    {
        var tornadoes = Stage.Fight(p).Tornadoes;
        return tornadoes.Standing is null ? [] : Enum.GetValues<TeaP1Tornado>().Select(tornado => Flat(tornadoes.Centre(tornado)!.Value)).ToList();
    }

    private bool InTheWater(ScenarioProbe p)
    {
        var at = Flat(p.Member(seat)!.Position);
        return TheWater(p).Any(centre => Vector2.Distance(at, centre) < KeepsClearOfTheWater);
    }

    // -- Its spots --

    // Its seat's spot at one of the stage's cues, in the frame it has read off the tornadoes, with the bosses where
    // they stand now. Null: the cue moves another seat.
    private Vector2? Spot(ScenarioProbe p, TeaP1LivingLiquidPart part, float cueAt)
    {
        var at = part == Protean1 ? TheStage.P1(cueAt)
            : cueAt == 0.9f && !roll.Plays(Protean1) ? 0.05f
            : TheStage.C2(cueAt);
        var cue = TheStage.Cues().Single(cue => MathF.Abs(cue.At - at) < 0.001f);
        return TheStage.ArenaSpots(p, cue).TryGetValue(seat, out var spot) ? spot : null;
    }

    private Stage TheStage => stage ??= new Stage { First = roll.First, Last = roll.Last, Open1 = Frame1.Open, Open2 = Frame2.Open };

    private Vector2? Bait(ScenarioProbe p, Vector2? spot)
    {
        if (spot is not { } on || play.Stance == TeaP1HumanStance.Exact) return spot;
        var outward = Vector2.Normalize(on - TheLiquid(p));
        var stands = on + (play.Stance == TeaP1HumanStance.AYalmOut ? outward : new Vector2(-outward.Y, outward.X));
        foreach (var centre in TheWater(p))
            if (Vector2.Distance(stands, centre) < KeepsClearOfTheWater + 0.1f)
                stands = centre + Vector2.Normalize(stands - centre) * (KeepsClearOfTheWater + 0.1f);
        return stands;
    }

    private Vector2? WhereItStoodForDrainage(ScenarioProbe p)
    {
        var drainage = new Stage { First = Protean1, Last = Protean1, Open1 = roll.Open1 };
        var cue = drainage.Cues().Single(cue => MathF.Abs(cue.At - 13.05f) < 0.001f);
        return drainage.ArenaSpots(p, cue).TryGetValue(seat, out var spot) ? spot : null;
    }

    private Vector2 ItsDrainageSpot => Frame1.ToArena(seat == OffTank ? new Vector2(-5.4f, 5.4f)
        : play.MaxMelee ? new Vector2(5.0f, 5.5f)
        : Stage.Off(Vector2.Zero, 3.9f, 135f));

    // -- Its buttons --

    private void Press(ScenarioProbe p)
    {
        if (play.PressesAntiKnockback && !pressedItsAntiKnockback && seen.TryGetValue(Telegraph, out var shown)
            && p.Time >= shown + 2.7f - PressesBeforeTheTelegraphLands)
        {
            pressedItsAntiKnockback = true;
            var button = seat is RegenHealer or ShieldHealer or CasterDps ? P1PlayerActionId.Surecast : P1PlayerActionId.ArmsLength;
            p.Fake.UserActions.Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, button, 0));
        }

        if (seat is not (RegenHealer or ShieldHealer) || play.Cleanses == TeaP1HumanCleanses.Never || !seen.TryGetValue(Throttle, out var landed)) return;
        var plan = Stage.Scenario(p).State!.Plan;
        if (!plan.HumanCleanses) return;
        if (p.Time < landed + FirstCleanseAfterThrottle + late + Gcd * (play.GcdsLate + gcds)) return;
        gcds++;
        var order = plan.CleanseOrder(play.Cleanses == TeaP1HumanCleanses.InItsOwnOrder ? seat : seat == RegenHealer ? ShieldHealer : RegenHealer);
        var next = order.Select(p.Member).FirstOrDefault(member => member is not null && member.IsAlive() && member.HasStatus(P1StatusId.Throttle));
        if (next is null) return;
        Cleansed.Add(Seat(next)!.Value);
        p.Fake.UserActions.Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, P1PlayerActionId.Esuna, (ulong)next.GameObjectId));
    }

    // -- Its tether --

    private void PlayItsTether(ScenarioProbe p, SimCharacter me, Vector2 at)
    {
        if (!seen.TryGetValue(Tethers, out var shown) || seen.ContainsKey(Drainage)) return;
        var fight = Stage.Fight(p);
        var tethers = fight.DrainageTethersOut;
        var held = tethers.Where(tether => ReferenceEquals(fight.Holder(tether.Tether), me)).Select(tether => (TeaP1Tornado?)tether.From).ToList();
        if (held.Count > 1) HeldTwoAtOnce = true;
        HeldAsDrainageWentOff = held.FirstOrDefault();
        if (p.Time < shown + TetherBeat + late + play.FetchesLate) return;

        if (!seat.IsTank())
        {
            PlayATetherInTheStack(p, at, tethers, held.FirstOrDefault());
            return;
        }

        if (!pressedItsCooldown && p.Fake.UserActions.Enabled)
        {
            pressedItsCooldown = true;
            TopP6HumanMitigation.PressNow(p.World, [TopP6HumanMitigation.Rampart]);
        }
        if (play.NeverFetches) return;
        var itsOwn = Stage.Scenario(p).State!.Plan.TetherOf(seat, tethers.Select(tether => tether.From).ToList());
        if (held.Count > 0)
        {
            var other = tethers.FirstOrDefault(tether => tether.From != held[0]);
            if (play.WalksThroughTheOtherTether && crossing < 2 && other.Tether is not null && fight.Holder(other.Tether) is { } itsHolder)
            {
                var across = OnTheLine(at, Flat(itsHolder.Position), Flat(fight.Tornadoes.Centre(other.From)!.Value));
                if (crossing == 0 && Vector2.Distance(at, across) < 0.1f) crossing = 1;
                if (crossing == 1 && goal is null) crossing = 2;
                if (crossing == 0)
                {
                    WalkTo(at, across);
                    return;
                }
            }
            WalkTo(at, ItsDrainageSpot);
            return;
        }
        var mine = tethers.FirstOrDefault(tether => tether.From == itsOwn).Tether;
        if (mine is null || fight.Holder(mine) is not { } holder) return;
        var itsTornado = Flat(fight.Tornadoes.Centre(itsOwn!.Value)!.Value);
        WalkTo(at, OnTheLine(play.TakesItsTetherByItsTornado ? itsTornado : at, Flat(holder.Position), itsTornado));
    }

    // The nearest point of a tether's line to walk into: ahead of its holder, and short of its tornado's water.
    private static Vector2 OnTheLine(Vector2 at, Vector2 holder, Vector2 tornado)
    {
        var length = Vector2.Distance(holder, tornado);
        var along = (tornado - holder) / length;
        var farthest = MathF.Max(AheadOfItsHolder, length - KeepsClearOfTheWater - 0.3f);
        return holder + along * Math.Clamp(Vector2.Dot(at - holder, along), AheadOfItsHolder, farthest);
    }

    private void PlayATetherInTheStack(ScenarioProbe p, Vector2 at, IReadOnlyList<(TeaP1Tornado From, SimTether Tether)> tethers, TeaP1Tornado? held)
    {
        if (held is not { } from)
        {
            if (everHeld && WhereItStoodForDrainage(p) is { } back) WalkTo(at, back);
            return;
        }
        everHeld = true;
        if (play.Holder == TeaP1HumanHolder.Stays) return;
        var plan = Stage.Scenario(p).State!.Plan;
        var tethering = tethers.Select(tether => tether.From).ToList();
        var itsTank = plan.TetherOf(MainTank, tethering) == from ? MainTank : OffTank;
        var tank = Flat(p.Member(play.Holder == TeaP1HumanHolder.WalksItAcrossTheOtherTank ? (itsTank == MainTank ? OffTank : MainTank) : itsTank)!.Position);
        var tornado = Flat(Stage.Fight(p).Tornadoes.Centre(from)!.Value);
        switch (play.Holder)
        {
            case TeaP1HumanHolder.WalksToMeetItsTank:
                if (Vector2.Distance(at, tank) > 3f) WalkTo(at, tank + Vector2.Normalize(at - tank) * 3f);
                break;
            case TeaP1HumanHolder.WalksAwayFromItsTank:
                var away = at + Vector2.Normalize(at - tank) * 2f;
                if (away.Length() < 17f) WalkTo(at, away);
                break;
            case TeaP1HumanHolder.WalksItAcrossTheOtherTank:
                WalkTo(at, tank + Vector2.Normalize(tank - tornado) * 2f);
                break;
        }
    }

    // -- Walking --

    // Somewhere it did not put itself: a knockback has it. It can do nothing until that has let go.
    private bool StillSliding(ScenarioProbe p, Vector2 at)
    {
        if (!sliding && put is { } last && Vector2.Distance(at, last) > 0.02f)
        {
            sliding = true;
            Slides++;
        }
        else if (sliding && Vector2.Distance(at, previous) < 1e-4f)
        {
            sliding = false;
            put = at;
            freeAt = p.Time + late;
            seen[Thrown] = p.Time;
        }
        previous = at;
        return sliding;
    }

    private void SetOffOnWhatItHasSeen(ScenarioProbe p, Vector2 at)
    {
        foreach (var leg in legs)
        {
            if (leg.SetOff || !seen.TryGetValue(leg.Cue, out var cue) || p.Time < cue + leg.Beat + late) continue;
            if (leg.Follows && goal is not null) continue;
            leg.SetOff = true;
            if (leg.To(p) is not { } to) continue;
            WalkTo(at, to, leg.Sprints);
        }
    }

    private void WalkTo(Vector2 at, Vector2 to, bool sprints = false)
    {
        if (goal is { } already && Vector2.Distance(already, to) < 1e-3f) return;
        if (goal is null && Vector2.Distance(at, to) < 1e-3f) return;
        if (goal is null || sprints || Vector2.Distance(goal.Value, to) > 3f)
            speed = sprints || (play.Pace == TeaP1HumanPace.Sprinting && Vector2.Distance(at, to) > SprintsWalksOver) ? SprintSpeed : WalkSpeed;
        goal = to;
    }

    private void Walk(ScenarioProbe p, SimCharacter me, Vector2 at, float delta)
    {
        if (goal is not { } to) return;
        var step = speed * delta;
        Vector2 next;
        if (Vector2.Distance(at, to) <= step)
        {
            next = to;
            goal = null;
        }
        else next = at + RoundTheWater(at, to, TheWater(p)) * step;
        var heading = Vector2.Distance(next, at) > 1e-4f ? MathF.Atan2(next.X - at.X, next.Y - at.Y) : me.Rotation;
        me.SetPosition(new Placement(new Vector3(next.X, 0f, next.Y), heading));
        put = next;
        previous = next;
    }

    // Straight at its spot, unless a puddle lies across the way: then along that puddle's edge, on the shorter side.
    private static Vector2 RoundTheWater(Vector2 at, Vector2 to, List<Vector2> water)
    {
        var length = Vector2.Distance(at, to);
        var straight = (to - at) / length;
        foreach (var centre in water.OrderBy(centre => Vector2.Distance(at, centre)))
        {
            if (Vector2.Distance(to, centre) < KeepsClearOfTheWater) continue;
            var toCentre = centre - at;
            var along = Vector2.Dot(toCentre, straight);
            if (along <= 0f || along >= length) continue;
            if (MathF.Abs(Cross(straight, toCentre)) >= KeepsClearOfTheWater) continue;
            var distance = toCentre.Length();
            var inward = toCentre / distance;
            var side = Cross(inward, straight) >= 0f ? 1f : -1f;
            if (distance <= KeepsClearOfTheWater + 0.01f)
                return Vector2.Normalize(Turned(inward, side * MathF.PI / 2f) - 0.3f * inward);
            return Turned(inward, side * MathF.Asin(KeepsClearOfTheWater / distance));
        }
        return straight;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    private static Vector2 Turned(Vector2 v, float radians)
    {
        var (sin, cos) = MathF.SinCos(radians);
        return new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
    }
}

// The runs of one human case: which had a death, and what else went wrong in any.
internal sealed class TeaP1HumanRuns
{
    public int Runs { get; private set; }
    public List<string> Lost { get; } = [];
    public List<string> Problems { get; } = [];

    // A run that did not play to its end is always a problem; so is one with a death, unless the case may lose runs.
    public void Add(string context, ScenarioRun run, IEnumerable<string> faults, bool mayLose = false)
    {
        Runs++;
        if (run.Failure is not null) Problems.Add($"{context}: {run}");
        else if (!run.Passed)
        {
            Lost.Add($"{context}: {run}");
            if (!mayLose) Problems.Add($"{context}: {run}");
        }
        Problems.AddRange(faults.Select(fault => $"{context}: {fault}"));
    }

    public void AssertNoneLostBut(int mayLose = 0)
    {
        TestContext.Out.WriteLine($"{Runs} runs, {Lost.Count} with a death");
        foreach (var lost in Lost.Take(40)) TestContext.Out.WriteLine(lost);
        Assert.That(Problems, Is.Empty, () => string.Join(Environment.NewLine, Problems.Take(12)));
        Assert.That(Lost, Has.Count.LessThanOrEqualTo(mayLose), "runs with a death");
    }
}
