using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using AnoMech.Core.EnemyActions;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Scenarios.Top.TopP6Constants;
using Actions = AnoMech.Scenarios.Top.TopP6Actions;

namespace AnoMech.Scenarios.Top;

// Alpha Omega, at the arena centre all phase, and the attacks more than one P6 block plays. Each is scheduled from a
// time on the block's own clock, so a block's Run reads as the fight's timeline:
//
//     world.Events.Add(0f, alphaOmega.Spawn);
//     alphaOmega.CosmoMemory(1.9f);
//     alphaOmega.Autos(12.0f);
//
// Every helper cast goes through CastFromHelper. A mistake kills on the spot, with the reason, as the hit lands.
public sealed class TopP6AlphaOmega(SimWorld world)
{
    internal const string FlashGaleOnANonTank =
        "Took Flash Gale as a non-tank: Alpha Omega's autos hit its target and the player farthest from it, so a tank must be the farthest";
    internal const string InSomeoneElsesFlashGale = "Stood in a Flash Gale aimed at someone else: each hits everyone within 5 y of its target";
    // The sheet leaves its row unnamed.
    internal const string FlashGaleName = "Flash Gale";
    internal const string CosmoDiveBusterOnANonTank =
        "Took a Cosmo Dive buster as a non-tank: one lands on each of the two players closest to Alpha Omega";
    internal const string InSomeoneElsesCosmoDiveBuster =
        "Stood in a Cosmo Dive buster aimed at someone else: each hits everyone within 8 y of its target";
    internal const string CosmoDiveBusterAndStack =
        "Took a Cosmo Dive buster and its stack: the two players closest to Alpha Omega must stay out of the farthest one's stack";

    internal static string TooFewInTheCosmoDiveStack(int count)
        => $"Cosmo Dive's stack on the player farthest from Alpha Omega had {count} in it: it needs {P6Rules.CosmoDiveStack}";

    private static readonly Placement FacingNorth = new(Vector3.Zero, -MathF.PI);

    private readonly SpawnedHelperPool helpers = new(world, new EnemySpawnConfig(
        BNpcBaseId: P6BNpcBaseId.Helper,
        NameId: BNpcNameId.AlphaOmega,
        Level: 1,
        Targetable: false,
        EnemyList: EnemyListMode.Never,
        Visibility: SpawnVisibility.InvisibleHelper,
        Placement: new Placement(Vector3.Zero, 0f)), P6Rules.HelperPool);

    public SimEnemy? Enemy { get; private set; }

    public Vector3 Position => Enemy?.Position ?? Vector3.Zero;

    // The scenario-local spot `radius` from the centre at compass `degrees` (0 north, clockwise).
    public static Vector3 AtBearing(float degrees, float radius)
    {
        var radians = degrees * MathF.PI / 180f;
        return new Vector3(MathF.Sin(radians), 0f, -MathF.Cos(radians)) * radius;
    }

    // The most helpers this block has kept busy at once; never more than P6Rules.HelperPool.
    public int BusyHelperPeak => helpers.PeakBusy;

    // At t 0: a cast on an actor spawned in its own frame plays no animation.
    public void Spawn()
    {
        Enemy = world.SpawnEnemy(new EnemySpawnConfig(
            BNpcBaseId: BNpcBaseId.AlphaOmega,
            NameId: BNpcNameId.AlphaOmega,
            Level: Level,
            Targetable: true,
            EnemyList: EnemyListMode.Always,
            Visibility: SpawnVisibility.Visible,
            LayoutId: BNpcLayoutId.AlphaOmega,
            Placement: FacingNorth));
        helpers.Spawn();
    }

    public void Cast(EnemyAction action, CastTarget target = default) => Enemy?.Cast(action, target);

    public void CastFromHelper(EnemyAction action, CastTarget target = default, Placement? at = null, byte animationVariation = 0)
        => helpers.Cast(action, target, at ?? Enemy?.Placement() ?? FacingNorth, animationVariation);

    // What Cosmo Memory's hit leaves, for a block that starts after it: Code: ****mi* on Alpha Omega and Brilliant
    // Dynamis on everyone.
    public void ApplyCosmoMemoryStatuses()
    {
        Enemy?.AddStatus(StatusId.CodeMi);
        foreach (var member in world.Party.ActiveMembers())
            member.AddStatus(StatusId.BrilliantDynamis);
    }

    // Cosmo Memory, which only a tank's limit break 3 lets the party live through: the gauge is full from its cast,
    // and its hit gives everyone Brilliant Dynamis and Alpha Omega Code: ****mi*.
    public void CosmoMemory(float castAt, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        At(castAt, () =>
        {
            world.Party.LimitBreak.Set(3f);
            Cast(Actions.CosmoMemory);
        }, file, line);
        At(castAt + Actions.CosmoMemory.ReleasesAfter(), () => Enemy?.AddStatus(StatusId.CodeMi), file, line);
    }

    // Two autos, P6Duration.SecondAuto apart: Alpha Omega's visual, then Flash Gale on its target and on the player
    // farthest from it. Nothing tracks enmity, so its target is always the main tank.
    public void Autos(float at, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        foreach (var visualAt in new[] { at, at + P6Duration.SecondAuto })
        {
            At(visualAt, () => Cast(Actions.AutoAttack), file, line);
            At(visualAt + P6Duration.FlashGaleAfterAuto, FlashGale, file, line);
        }
    }

    // Cosmo Arrow: Alpha Omega's cast and its exasquares (TopP6Exasquares).
    public void CosmoArrow(TopP6Exasquares exasquares, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        At(exasquares.CastAt, () => Cast(TopActions.CosmoArrow, Position), file, line);
        At(exasquares.FirstSetAt, () => CastExasquares(exasquares.FirstSet), file, line);
        At(exasquares.SecondSetAt, () => CastExasquares(exasquares.SecondSet), file, line);
        foreach (var roll in exasquares.Rolls)
            At(roll.At, () => CastRollingLines(roll.Lines), file, line);
    }

    // Cosmo Dive: P6Duration.CosmoDiveHitsAfterCast after its cast ends, a buster on each of the two players closest
    // to Alpha Omega and a stack on the farthest of the rest.
    public void CosmoDive(float castAt, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        At(castAt, () => Cast(Actions.CosmoDive), file, line);
        var hitsAt = castAt + Actions.CosmoDive.ReleasesAfter() + P6Duration.CosmoDiveHitsAfterCast;
        var targets = new CosmoDiveTargets([], null);
        At(hitsAt - P6Duration.CosmoDiveTargetsBeforeHits, () => targets = TakeCosmoDiveTargets(), file, line);
        At(hitsAt, () => LandCosmoDive(targets), file, line);
    }

    // Unlimited Wave Cannon (TopP6UnlimitedWaveCannon): Alpha Omega's cast, its exaflares and its puddle sets.
    public void UnlimitedWaveCannon(TopP6UnlimitedWaveCannon wave, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        At(wave.CastAt, () => Cast(Actions.UnlimitedWaveCannon), file, line);
        for (var exaflare = 0; exaflare < TopP6UnlimitedWaveCannon.Exaflares; exaflare++)
        {
            var lane = wave.Lane(exaflare);
            world.Events.FromRunStart(() =>
            {
                lane.Schedule(world.Events, CastFromHelper, file, line);
                return true;
            });
        }
        foreach (var set in wave.PuddleSets)
            At(set.DropAt, () => BaitedPuddles.Drop(world.Party, Actions.WaveCannonPuddle, CastFromHelper), file, line);
    }

    // UNVERIFIED: that Flash Gale kills a non-tank it targets, or anyone else it catches.
    private void FlashGale()
    {
        var party = world.Party;
        var targets = new[] { party.Get(PartyRole.MainTank), party.Find.Farest(Position) }
            .Where(target => target.IsAlive())
            .Select(target => target!)
            .ToList();
        var verdicts = new Verdicts();
        foreach (var target in targets.Where(target => !IsTank(target)))
            verdicts.Kill(target, ActionId.Unknown7ddf, FlashGaleOnANonTank, FlashGaleName);
        foreach (var target in targets)
            foreach (var other in party.Find.InsideActionAoe(ActionId.Unknown7ddf, target.Placement()).Where(hit => hit != target))
                verdicts.Kill(other, ActionId.Unknown7ddf, InSomeoneElsesFlashGale, FlashGaleName);
        verdicts.Deal();
        foreach (var target in targets)
            CastFromHelper(Actions.FlashGale, target);
    }

    private sealed record CosmoDiveTargets(IReadOnlyList<SimCharacter> Busters, SimCharacter? Stack);

    private CosmoDiveTargets TakeCosmoDiveTargets()
    {
        var byDistance = world.Party.Find.ClosestN(Position, PerRole.All.Length);
        return new CosmoDiveTargets(byDistance.Take(2).ToList(), byDistance.Count > 2 ? byDistance[^1] : null);
    }

    // UNVERIFIED: that a buster kills a non-tank it targets, or anyone else it catches.
    private void LandCosmoDive(CosmoDiveTargets targets)
    {
        var party = world.Party;
        var busters = targets.Busters.Where(target => target.IsAlive()).ToList();
        var stack = targets.Stack.IsAlive() ? targets.Stack : null;
        var verdicts = new Verdicts();
        foreach (var target in busters.Where(target => !IsTank(target)))
            verdicts.Kill(target, ActionId.CosmoDive_7BA7, CosmoDiveBusterOnANonTank);
        foreach (var target in busters)
            foreach (var other in party.Find.InsideActionAoe(ActionId.CosmoDive_7BA7, target.Placement()).Where(hit => hit != target))
                verdicts.Kill(other, ActionId.CosmoDive_7BA7, InSomeoneElsesCosmoDiveBuster);
        if (stack is not null)
        {
            var soakers = party.Find.InsideActionAoe(ActionId.CosmoDive_7BA8, stack.Placement());
            foreach (var target in busters.Where(soakers.Contains))
                verdicts.Kill(target, ActionId.CosmoDive_7BA8, CosmoDiveBusterAndStack);
            if (soakers.Count < P6Rules.CosmoDiveStack)
                foreach (var soaker in soakers)
                    verdicts.Kill(soaker, ActionId.CosmoDive_7BA8, TooFewInTheCosmoDiveStack(soakers.Count));
        }
        verdicts.Deal();
        foreach (var target in busters)
            CastFromHelper(Actions.CosmoDiveBuster, target);
        if (stack is not null)
            CastFromHelper(Actions.CosmoDiveStack, stack);
    }

    // Each line is cast as a row from the west edge and a column from the north edge, so it covers both axes; Inhale,
    // a visual, goes with it from both spots.
    private void CastExasquares(IReadOnlyList<float> lines)
    {
        foreach (var line in lines)
            foreach (var action in new[] { TopActions.CosmoArrowOmen, TopActions.Inhale })
            {
                CastFromHelper(action, at: Row(line));
                CastFromHelper(action, at: Column(line));
            }
    }

    private void CastRollingLines(IReadOnlyList<float> lines)
    {
        foreach (var line in lines)
        {
            CastFromHelper(TopActions.CosmoArrowLine, at: Row(line));
            CastFromHelper(TopActions.CosmoArrowLine, at: Column(line));
        }
    }

    private static Placement Row(float z) => new(new Vector3(-Geometry.ArenaRadius, 0f, z), MathF.PI / 2f);

    private static Placement Column(float x) => new(new Vector3(x, 0f, -Geometry.ArenaRadius), 0f);

    private static bool IsTank(SimCharacter member) => member is ISimPartyMember { Role: var role } && role.IsTank();

    // On the run's clock, whenever it is called.
    private void At(float time, Action action, string file, int line)
        => world.Events.FromRunStart(() =>
        {
            world.Events.Add(time, action, file, line);
            return true;
        });
}

// One Cosmo Arrow. Each line runs the arena's width along both axes, so a spot is hit when its x or its z lies within
// the line's half-width. One set of 10 y lines lands as its 8 s casts end, the other 2 s later; then each set rolls on
// in 5 y lines every 2 s, the cross outward on both sides and each outer line inward, on through the centre to the far
// edge. In first, the cross lands first.
public sealed record TopP6Exasquares(bool InFirst, float CastAt)
{
    private static readonly IReadOnlyList<float> Cross = [0f];
    private static readonly IReadOnlyList<float> OuterLines = [-P6Geometry.ExasquareOuterLine, P6Geometry.ExasquareOuterLine];

    public IReadOnlyList<float> FirstSet => InFirst ? Cross : OuterLines;
    public IReadOnlyList<float> SecondSet => InFirst ? OuterLines : Cross;

    // When each set's casts start.
    public float FirstSetAt => CastAt;
    public float SecondSetAt => CastAt + P6Duration.SecondExasquareSet;

    public IReadOnlyList<Roll> Rolls => RollOn(FirstSet, SecondSet, CastAt);

    // Every hit, in time order: the two sets, then the rolling lines.
    public IReadOnlyList<Wave> Waves =>
    [
        new(FirstSetAt + TopActions.CosmoArrowOmen.ResolvesAfter(), FirstSet, HalfWidth(TopActions.CosmoArrowOmen)),
        new(SecondSetAt + TopActions.CosmoArrowOmen.ResolvesAfter(), SecondSet, HalfWidth(TopActions.CosmoArrowOmen)),
        .. Rolls.Select(roll => new Wave(roll.At + TopActions.CosmoArrowLine.ResolvesAfter(), roll.Lines, HalfWidth(TopActions.CosmoArrowLine))),
    ];

    // The 5 y lines one step casts.
    public sealed record Roll(float At, IReadOnlyList<float> Lines);

    public sealed record Wave(float HitAt, IReadOnlyList<float> Lines, float HalfWidth)
    {
        public bool Hits(Vector3 spot) => Lines.Any(line => MathF.Abs(spot.X - line) <= HalfWidth || MathF.Abs(spot.Z - line) <= HalfWidth);
    }

    // The first set's lines start rolling with the first step, the second's a step later, each line from both sides.
    private static IReadOnlyList<Roll> RollOn(IReadOnlyList<float> firstSet, IReadOnlyList<float> secondSet, float castAt)
    {
        var rolls = new List<Roll>();
        var lines = new List<(float At, float Way)>();
        for (var step = 0; step == 0 || lines.Count > 0; step++)
        {
            lines = lines.Select(l => (l.At + P6Geometry.ExasquareRoll * l.Way, l.Way)).ToList();
            if (step <= 1)
                lines.AddRange((step == 0 ? firstSet : secondSet)
                    .SelectMany(line => new[] { (line + P6Geometry.ExasquareFirstRoll, 1f), (line - P6Geometry.ExasquareFirstRoll, -1f) }));
            lines.RemoveAll(l => MathF.Abs(l.At) > Geometry.ArenaRadius);
            if (lines.Count > 0)
                rolls.Add(new Roll(castAt + P6Duration.FirstRollingLines + step * P6Duration.RollingLineInterval, lines.Select(l => l.At).ToList()));
        }
        return rolls;
    }

    private static float HalfWidth(EnemyAction line) => (Natives.Data.Action(line.ActionId)?.XAxisModifier ?? 0) / 2f;
}

// One Unlimited Wave Cannon. Four exaflares start on neighbouring compass points, ExaflaresApart apart: the first on
// FirstPoint (0 north, then clockwise every 45°), the rest each a point further the way they turn. Each rolls from
// the rim through the centre to the far rim. Six sets of puddles drop under everyone meanwhile.
public sealed record TopP6UnlimitedWaveCannon(int FirstPoint, bool Clockwise, float CastAt, float ExaflareStep = P6Duration.ExaflareStep)
{
    public const int Exaflares = 4;
    public const int CompassPoints = 8;

    // A block's roll, for its State. Both are drawn whether or not they are forced, so forcing one leaves the other as
    // the seed had it. UNVERIFIED: that the game draws each evenly.
    public static (int FirstPoint, bool Clockwise) Roll(Rng rng, int? firstPoint, bool? clockwise)
    {
        var rolledPoint = rng.Next(CompassPoints);
        var rolledClockwise = rng.NextBool();
        return (firstPoint ?? rolledPoint, clockwise ?? rolledClockwise);
    }

    public int Turn => Clockwise ? 1 : -1;

    // Compass degrees of the exaflare's start, 0 north and clockwise.
    public float Bearing(int exaflare) => ((FirstPoint + Turn * exaflare) % CompassPoints + CompassPoints) % CompassPoints * 45f;

    // Facing the centre from its start.
    public ExaflareLane Lane(int exaflare)
    {
        var degrees = Bearing(exaflare);
        return new ExaflareLane(new Placement(TopP6AlphaOmega.AtBearing(degrees, P6Geometry.ExaflareStartRadius), -degrees * MathF.PI / 180f),
            P6Geometry.ExaflareAdvance, P6Geometry.ExaflareExplosions, CastAt + exaflare * P6Duration.ExaflaresApart, ExaflareStep,
            Actions.ExaflareFirstExplosion, Actions.ExaflareExplosion);
    }

    // The first exaflare's first explosion: the moment the dodge is timed from.
    public float FirstExplosionAt => Lane(0).ReleaseAt(0);

    public IReadOnlyList<Explosion> Explosions
        => Enumerable.Range(0, Exaflares)
            .SelectMany(exaflare => Enumerable.Range(0, P6Geometry.ExaflareExplosions)
                .Select(step => new Explosion(exaflare, step, Lane(exaflare).Position(step), Lane(exaflare).HitAt(step))))
            .OrderBy(explosion => explosion.HitAt)
            .ToList();

    public IReadOnlyList<PuddleSet> PuddleSets
        => P6Duration.PuddleDrops
            .Select((drop, index) => new PuddleSet(index, CastAt + drop, CastAt + drop + Actions.WaveCannonPuddle.ResolvesAfter()))
            .ToList();

    public static float ExplosionRadius => Natives.Data.Action(ActionId.WaveCannon_7BAE)?.EffectRange ?? 0f;
    public static float PuddleRadius => Natives.Data.Action(ActionId.WaveCannon_7BAF)?.EffectRange ?? 0f;

    // Step 0 goes off on the rim; HitAt is when it decides who it hits.
    public readonly record struct Explosion(int Exaflare, int Step, Vector3 Position, float HitAt);

    // Each puddle is centred where its player stood at DropAt.
    public readonly record struct PuddleSet(int Index, float DropAt, float HitAt);
}
