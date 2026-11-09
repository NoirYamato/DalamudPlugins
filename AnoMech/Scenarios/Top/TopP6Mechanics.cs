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

// Alpha Omega, at the arena centre all phase, and the attacks P6 plays. Each is scheduled from a time on the run's own
// clock, so a scenario's Run reads as the fight's timeline:
//
//     world.Events.Add(0f, alphaOmega.Spawn);
//     alphaOmega.CosmoMemory(1.9f, limitBreaks);
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
    internal const string CosmoDiveBusterWithoutBigCooldowns =
        "Took a Cosmo Dive buster without enough mitigation: each needs a tank's big cooldowns";

    internal static string TooFewInTheCosmoDiveStack(int count)
        => $"Cosmo Dive's stack on the player farthest from Alpha Omega had {count} in it: it needs {P6Rules.CosmoDiveStack}";

    internal const string CosmoMemoryNeedsATankLimitBreak3 = "it kills unless a tank's limit break 3 is up as it lands";
    internal const string MagicNumberNeedsATankLimitBreak3 = "it kills everyone unless a tank's limit break 3 is up as it lands";
    internal const string MagicNumberMarkRanOut =
        "its mark kills as it runs out unless a healer's limit break 3 lands while it is on everyone";
    internal const string RunMiLanded =
        "it kills everyone unless Alpha Omega has fallen: the Gauntlet's last melee limit break 3 finishes it, landing after the cast starts and before this damage";

    private const int LasersPerSet = 4;

    // The human's resolve reaches the run a frame or two after its cast bar ends.
    private const float AHumansResolveIsHeardWithin = 0.25f;

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

    // The most helpers this run has kept busy at once; never more than P6Rules.HelperPool.
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

    // What Cosmo Memory's hit leaves, for a run that starts after it: Code: ****mi* on Alpha Omega, and Brilliant
    // Dynamis on everyone but the `sparked` seats, whose limit break has spent it already: they carry Spark of Dynamis.
    // Each half also goes on alone, for a run that gives the party theirs before Alpha Omega can take its own.
    public void ApplyCosmoMemoryStatuses(IReadOnlyCollection<PartyRole>? sparked = null)
    {
        ApplyCodeMi();
        ApplyCosmoMemorysDynamis(sparked);
    }

    public void ApplyCodeMi() => Enemy?.AddStatus(StatusId.CodeMi);

    public void ApplyCosmoMemorysDynamis(IReadOnlyCollection<PartyRole>? sparked = null)
    {
        foreach (var role in PerRole.All)
            if (world.Party.Get(role) is { } member && member.IsAlive())
                member.AddStatus(sparked?.Contains(role) == true ? P6StatusId.SparkOfDynamis : StatusId.BrilliantDynamis);
    }

    // Cosmo Memory with every player's limit breaks counted: the gauge holds three bars from its cast, and as its bar
    // ends everyone without a tank's limit break 3 taken hold dies. Its hit gives everyone Brilliant Dynamis and Alpha
    // Omega Code: ****mi*, and the gauge rebuilds from it (GaugeRebuildAfterCosmoMemory).
    public void CosmoMemory(float castAt, TopP6LimitBreaks limitBreaks, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        At(castAt, () =>
        {
            limitBreaks.SetGauge(TopP6LimitBreaks.Bars);
            Cast(Actions.CosmoMemoryDecidedInCode);
        }, file, line);
        At(castAt + Actions.CosmoMemoryDecidedInCode.ResolvesAfter(),
            () => KillWhoeverLacksATankLimitBreak3(limitBreaks, ActionId.CosmoMemory, CosmoMemoryNeedsATankLimitBreak3), file, line);
        At(castAt + Actions.CosmoMemoryDecidedInCode.ReleasesAfter(), () => Enemy?.AddStatus(StatusId.CodeMi), file, line);
        var (from, fullBy) = GaugeRebuildAfterCosmoMemory(castAt);
        limitBreaks.Rebuild(from, fullBy);
    }

    // The party's own generation after Cosmo Memory cast at `castAt`: from its bar end, full again P6Duration.GaugeRebuild
    // later. A run starting after it sets TopP6LimitBreaks.RebuiltBars of it and rebuilds the rest.
    public static (float From, float FullBy) GaugeRebuildAfterCosmoMemory(float castAt)
    {
        var from = castAt + Actions.CosmoMemoryDecidedInCode.ResolvesAfter();
        return (from, from + P6Duration.GaugeRebuild);
    }

    // Magic Number: as its bar ends, everyone without a tank's limit break 3 taken hold dies. Its mark lands on everyone
    // left P6Duration.MagicNumberMarkAfterTheHit after the hit, and kills whoever still carries it as it runs out; a
    // healer's limit break 3 resolving while it is on lifts it from all.
    public void MagicNumber(float castAt, TopP6LimitBreaks limitBreaks, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        var mark = new MagicNumberMark(world, limitBreaks);
        var marksAt = castAt + Actions.MagicNumber.ReleasesAfter() + P6Duration.MagicNumberMarkAfterTheHit;
        At(castAt, () => Cast(Actions.MagicNumber), file, line);
        At(castAt + Actions.MagicNumber.ResolvesAfter(),
            () => KillWhoeverLacksATankLimitBreak3(limitBreaks, P6ActionId.MagicNumber, MagicNumberNeedsATankLimitBreak3), file, line);
        At(marksAt, mark.Land, file, line);
        At(marksAt + P6Duration.MagicNumberMark, mark.RunOut, file, line);
    }

    // Run: ****mi*, the enrage. A melee limit break 3 on Alpha Omega whose own cast bar ends from castAt until before the
    // damage (RunMiLandsAt) defeats Alpha Omega once it resolves, so one started before the cast can count; otherwise the
    // damage kills everyone. The Gauntlet's enrage window counts the same presses (TopP6MeleeWindow.LandsBy). The human's
    // resolve is heard a frame or two after it lands, so the damage waits that long for one that landed before it. The
    // damage is queued only if Alpha Omega still stands as the cast bar ends, so a run that finishes it in the cast ends
    // without waiting it out. Whether the gauge was short is read as the damage lands, while a press that gives up then is
    // still waiting.
    public void RunMi(float castAt, TopP6LimitBreaks limitBreaks, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        var landsAt = RunMiLandsAt(castAt);
        string? why = null;

        bool Finishes(TopP6LimitBreakPress press)
            => Enemy is { } enemy && press.LandsAMeleeLimitBreak3On((ulong)enemy.GameObjectId, castAt, landsAt);

        void Finish(TopP6LimitBreakPress press)
        {
            if (press.ResolvedAt is not null && Enemy is { } enemy && enemy.IsAlive() && Finishes(press))
                enemy.Defeat();
        }

        void Land(bool waited)
        {
            limitBreaks.CatchUp();
            why ??= RunMiLanded + (limitBreaks.StarvedOfGauge ? TopP6LimitBreaks.GaugeWasShort : "");
            if (!waited && Enemy.IsAlive() && limitBreaks.Presses.Any(press => press.IsCasting && Finishes(press)))
            {
                world.Events.Add(AHumansResolveIsHeardWithin, () => Land(waited: true), file, line);
                return;
            }
            limitBreaks.Resolved -= Finish;
            if (Enemy.IsAlive()) world.Party.WipeAllPlayers(P6ActionId.RunMi, why);
        }

        // A press the cast's own frame settled before this runs is judged as one resolving after it.
        At(castAt, () =>
        {
            Cast(Actions.RunMi);
            limitBreaks.CatchUp();
            foreach (var press in limitBreaks.Presses.ToList()) Finish(press);
            limitBreaks.Resolved += Finish;
        }, file, line);
        At(castAt + Actions.RunMi.ResolvesAfter(), () =>
        {
            if (Enemy.IsAlive()) At(landsAt, () => Land(waited: false), file, line);
            else limitBreaks.Resolved -= Finish;
        }, file, line);
    }

    // When the damage of Run: ****mi* cast at `castAt` lands: a melee limit break 3 must land on Alpha Omega before it.
    public static float RunMiLandsAt(float castAt) => castAt + P6Duration.RunMiLands;

    // Wave Cannon: Alpha Omega's visual, then a laser from it at each of four players and at the other four, and Wild
    // Charge at its target.
    public void WaveCannon(float castAt, IReadOnlyList<PartyRole> laserTargets, PartyRole wildChargeTarget,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        At(castAt, () => Cast(TopActions.AlphaOmegaWaveCannon, Position), file, line);
        for (var set = 0; set < P6Duration.LasersAfterWaveCannon.Count; set++)
        {
            var targets = laserTargets.Skip(set * LasersPerSet).Take(LasersPerSet).ToList();
            At(castAt + P6Duration.LasersAfterWaveCannon[set], () => FireLasers(targets), file, line);
        }
        At(castAt + P6Duration.WildChargeAfterWaveCannon, () => WildCharge(wildChargeTarget), file, line);
    }

    // Cosmo Meteor (TopP6CosmoMeteor): Alpha Omega's cast, its baits and adds, the spreads, the flares and the stack.
    public TopP6CosmoMeteor CosmoMeteor(float castAt, TopP6CosmoMeteorRolls rolls, TopP6LimitBreaks limitBreaks,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        var cosmoMeteor = new TopP6CosmoMeteor(world, this, limitBreaks);
        cosmoMeteor.Schedule(castAt, rolls, file, line);
        return cosmoMeteor;
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
    // to Alpha Omega and a stack on the farthest of the rest. A human tank under a buster needs its big cooldowns
    // (P6Rules.CosmoDiveBusterMitigation).
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

    // UNVERIFIED: that a buster kills a non-tank it targets, or anyone else it catches, and that the stack kills a buster
    // target in it, or everyone in it when fewer than P6Rules.CosmoDiveStack share it. A tank's mitigation is checked
    // last, so a tank in the wrong spot hears of its spot.
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
        foreach (var tank in busters.Where(IsTank).Where(tank => ShortOfMitigation(tank, ActionId.CosmoDive_7BA7, P6Rules.CosmoDiveBusterMitigation)))
            verdicts.Kill(tank, ActionId.CosmoDive_7BA7, CosmoDiveBusterWithoutBigCooldowns);
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

    // As an EnemyAction's MinMit checks it, against the hit's own damage type: the human's alone, only while
    // Mitigation.Required, and the shields it counts are spent.
    private bool ShortOfMitigation(SimCharacter tank, uint actionId, float required)
        => DamageCheck.LethalCause(tank, VulnSpec.None, Severity.TankBuster.MinMit(required),
            DamageKinds.FromAttackType(Natives.Data.Action(actionId)?.AttackType ?? 0), world.Party) is not null;

    private void KillWhoeverLacksATankLimitBreak3(TopP6LimitBreaks limitBreaks, uint actionId, string rule)
    {
        var tooLate = rule + string.Format(TopP6LimitBreaks.TakesHoldTooLate, P6Duration.TankLimitBreak3TakesHold);
        var missing = rule + (limitBreaks.StarvedOfGauge ? TopP6LimitBreaks.GaugeWasShort : "");
        var verdicts = new Verdicts();
        foreach (var member in world.Party.ActiveMembers().ToList())
        {
            switch (limitBreaks.TankLimitBreak3Cover(member))
            {
                case TopP6TankCover.TooLate:
                    verdicts.Kill(member, actionId, tooLate);
                    break;
                case TopP6TankCover.Missing:
                    verdicts.Kill(member, actionId, missing);
                    break;
            }
        }
        verdicts.Deal();
    }

    // Each from Alpha Omega toward where its target stands now.
    private void FireLasers(IEnumerable<PartyRole> targets)
    {
        foreach (var role in targets)
            if (world.Party.Get(role) is { } target && target.IsAlive())
                CastFromHelper(Actions.WaveCannonLaser, target);
    }

    private void WildCharge(PartyRole role)
    {
        if (world.Party.Get(role) is { } target && target.IsAlive())
            Cast(Actions.WildCharge, target);
    }

    // Who carries Magic Number's mark: kept here, since a status that runs out leaves before anything can see it go.
    private sealed class MagicNumberMark(SimWorld world, TopP6LimitBreaks limitBreaks)
    {
        private readonly List<SimCharacter> carriers = [];

        public void Land()
        {
            limitBreaks.CatchUp();
            carriers.AddRange(world.Party.ActiveMembers());
            foreach (var member in carriers)
                member.AddStatus(P6StatusId.MagicNumber, P6Duration.MagicNumberMark);
            limitBreaks.Resolved += Lift;
        }

        private void Lift(TopP6LimitBreakPress press)
        {
            if (!press.Is(TopP6LimitBreakKind.Healer)) return;
            limitBreaks.Resolved -= Lift;
            foreach (var member in carriers.Where(m => m.IsAlive()))
                member.RemoveStatus(P6StatusId.MagicNumber);
            carriers.Clear();
        }

        public void RunOut()
        {
            limitBreaks.CatchUp();
            limitBreaks.Resolved -= Lift;
            var why = MagicNumberMarkRanOut + (limitBreaks.StarvedOfGauge ? TopP6LimitBreaks.GaugeWasShort : "");
            var verdicts = new Verdicts();
            foreach (var member in carriers.Where(m => m.IsAlive()))
                verdicts.Kill(member, P6ActionId.MagicNumber, why);
            carriers.Clear();
            verdicts.Deal();
        }
    }

    // On the run's clock, whenever it is called.
    internal void At(float time, Action action, string file, int line)
        => world.Events.FromRunStart(() =>
        {
            world.Events.Add(time, action, file, line);
            return true;
        });
}

// One Cosmo Meteor's rolls: each spread set's first four (the other four are hit a second later), the three flares, and
// the unmarked stack's target.
public sealed record TopP6CosmoMeteorRolls(IReadOnlyList<PartyRole[]> FirstSpreads, IReadOnlyList<PartyRole> Flares, PartyRole StackTarget)
{
    // wave 0 is the set's first four, wave 1 the other four.
    public PartyRole[] SpreadWave(int set, int wave)
        => wave == 0 ? FirstSpreads[set] : PerRole.All.Except(FirstSpreads[set]).ToArray();
}

// One Cosmo Meteor: Alpha Omega's cast, a bait under everyone as it ends, the adds (P6Geometry.CosmoMeteorBearings and
// CosmoCometBearings), the spreads, then the flares and the stack. While the adds are targetable a caster's limit
// break 3 kills each comet its circle holds and a physical ranged limit break 3 each meteor its line holds; an add of a
// kind still alive at that kind's enrage casts it, and everyone dies. A mistake kills on the spot, with the reason, as
// the hit lands.
public sealed class TopP6CosmoMeteor(SimWorld world, TopP6AlphaOmega alphaOmega, TopP6LimitBreaks limitBreaks)
{
    internal const string InSomeoneElsesSpread =
        "Stood in another player's Cosmo Meteor spread: four land at once and the other four a second later, twice, each on everyone within 5 y of its target";
    internal const string ASecondSpread =
        "Hit by two Cosmo Meteor spreads: each hits everyone within 5 y of its target and leaves a vulnerability";
    internal const string FlareInTheStack =
        "Took Cosmo Meteor's stack holding a flare: the stack lands on a player without one, and every flare must stay out of it";
    internal static readonly string TooCloseToAFlare =
        $"Stood within {P6Geometry.FlareLethalWithin:F0} y of a flare: each lands on its marked player and hits everyone, harder the nearer they stand";
    internal const string CometsEnraged = "the Cosmo Comets enrage unless a caster's limit break 3 kills all six first";
    internal const string MeteorsEnraged = "the Cosmo Meteors enrage unless a physical ranged limit break 3's line runs through both first";
    internal const string YoursCaught = "; yours caught {0} of them";

    internal static string TooFewInTheStack(int count)
        => $"Cosmo Meteor's unmarked stack had {count} in it: it needs {P6Rules.CosmoMeteorStack}";

    private readonly List<SimEnemy?> meteors = [];
    private readonly List<SimEnemy?> comets = [];

    // How many of each kind the human's own limit breaks 3 have caught, once one has gone off while they showed.
    private int? cometsTheHumanCaught;
    private int? meteorsTheHumanCaught;

    public IReadOnlyList<SimEnemy> Meteors => meteors.OfType<SimEnemy>().ToList();
    public IReadOnlyList<SimEnemy> Comets => comets.OfType<SimEnemy>().ToList();

    internal void Schedule(float castAt, TopP6CosmoMeteorRolls rolls, string file, int line)
    {
        var party = world.Party;
        alphaOmega.At(castAt, () => alphaOmega.Cast(Actions.CosmoMeteor), file, line);
        alphaOmega.At(castAt + Actions.CosmoMeteor.ReleasesAfter(),
            () => BaitedPuddles.Drop(party, Actions.CosmoMeteorBait, alphaOmega.CastFromHelper), file, line);
        // UNVERIFIED: when the adds show, targetable.
        alphaOmega.At(castAt + P6Duration.CosmoMeteorAddsSpawn, SpawnTheAdds, file, line);
        alphaOmega.At(castAt + P6Duration.CosmoMeteorAddsShow, ShowTheAdds, file, line);
        for (var spreads = 0; spreads < P6Duration.CosmoMeteorSpreads.Count; spreads++)
        {
            var targets = rolls.SpreadWave(spreads / 2, spreads % 2);
            alphaOmega.At(castAt + P6Duration.CosmoMeteorSpreads[spreads], () => LandTheSpreads(targets), file, line);
        }
        // UNVERIFIED: when the flare markers show.
        alphaOmega.At(castAt + P6Duration.CosmoMeteorFlareMarkers, () => MarkTheFlares(rolls.Flares), file, line);
        alphaOmega.At(castAt + P6Duration.CosmoMeteorFlaresAndStack, () => LandTheFlaresAndTheStack(rolls), file, line);
        alphaOmega.At(castAt + P6Duration.CosmoMeteorFinish, () => alphaOmega.Cast(Actions.CosmoMeteorFinish), file, line);
        limitBreaks.Resolved += KillTheAddsItHits;
        alphaOmega.At(castAt + P6Duration.CometsEnrage, () => Enrage(comets, () => CometsEnraged + WhatTheHumanCaught(cometsTheHumanCaught)), file, line);
        alphaOmega.At(castAt + P6Duration.MeteorsEnrage, () =>
        {
            Enrage(meteors, () => MeteorsEnraged + WhatTheHumanCaught(meteorsTheHumanCaught));
            limitBreaks.Resolved -= KillTheAddsItHits;
        }, file, line);
    }

    private void SpawnTheAdds()
    {
        meteors.AddRange(P6Geometry.CosmoMeteorBearings.Select(bearing => SpawnAdd(P6BNpcBaseId.CosmoMeteor, P6BNpcNameId.CosmoMeteor, bearing, P6Geometry.CosmoMeteorDistance)));
        comets.AddRange(P6Geometry.CosmoCometBearings.Select(bearing => SpawnAdd(P6BNpcBaseId.CosmoComet, P6BNpcNameId.CosmoComet, bearing, P6Geometry.CosmoCometDistance)));
    }

    private SimEnemy? SpawnAdd(uint bnpcBaseId, uint nameId, float bearing, float distance)
        => world.SpawnEnemy(new EnemySpawnConfig(
            BNpcBaseId: bnpcBaseId,
            NameId: nameId,
            Level: Level,
            Targetable: false,
            EnemyList: EnemyListMode.Manual,
            Visibility: SpawnVisibility.HiddenUntilPopIn,
            Placement: new Placement(TopP6AlphaOmega.AtBearing(bearing, distance), 0f).Face(Vector3.Zero)));

    private void ShowTheAdds()
    {
        foreach (var add in meteors.Concat(comets).OfType<SimEnemy>())
        {
            add.ActorControl.PopIn();
            add.SetTargetable(true);
            add.SetVisibleInEnemyList(true);
        }
    }

    private static void Defeat(IEnumerable<SimEnemy?> adds)
    {
        foreach (var add in adds.OfType<SimEnemy>().Where(add => add.IsAlive()))
            add.Defeat();
    }

    private static IEnumerable<SimEnemy> Targetable(IEnumerable<SimEnemy?> adds)
        => adds.OfType<SimEnemy>().Where(add => add.IsAlive() && add.Targetable);

    // The circle goes down on its ground point and holds a comet any part of whose hitbox it reaches; one without a point
    // holds them all. The line runs from its user toward its target, as long as the sheet's plus the user's hitbox, and
    // holds a meteor any part of whose hitbox it reaches. The run's own press for the player's seat reaches every meteor
    // wherever the player stands: the run presses there for a human whose job can't, and holds that human to none of it.
    private void KillTheAddsItHits(TopP6LimitBreakPress press)
    {
        if (press.Is(TopP6LimitBreakKind.Caster))
            cometsTheHumanCaught = KillWhatItHolds(press, comets, comet => CircleHolds(press, comet), cometsTheHumanCaught);
        if (!press.Is(TopP6LimitBreakKind.PhysicalRanged)) return;
        if (!press.ByHuman && world.Party.Get(press.Seat) is SimPlayer)
        {
            Defeat(Targetable(meteors).ToList());
            return;
        }
        if (press.ResolvedFrom is not { } from) return;
        var line = new AoeQuery(press.ActionId, from, casterHitboxRadius: world.Party.Get(press.Seat)?.HitboxRadius ?? 0f);
        meteorsTheHumanCaught = KillWhatItHolds(press, meteors, meteor => line.SignedDistance(meteor.Position) <= meteor.HitboxRadius,
            meteorsTheHumanCaught);
    }

    // Kills the shown adds it holds, and returns how many the human's have caught so far, counting this one if it is the
    // human's and went off while they showed.
    private static int? KillWhatItHolds(TopP6LimitBreakPress press, IEnumerable<SimEnemy?> adds, Func<SimEnemy, bool> holds,
        int? theHumanCaught)
    {
        var shown = Targetable(adds).ToList();
        var held = shown.Where(holds).ToList();
        Defeat(held);
        return press.ByHuman && shown.Count > 0 ? (theHumanCaught ?? 0) + held.Count : theHumanCaught;
    }

    private static string WhatTheHumanCaught(int? caught) => caught is { } adds ? string.Format(YoursCaught, adds) : "";

    private static bool CircleHolds(TopP6LimitBreakPress press, SimEnemy comet)
        => press.GroundPoint is not { } point
           || new AoeQuery(press.ActionId, new Placement(point, 0f)).SignedDistance(comet.Position) is not { } outside
           || outside <= comet.HitboxRadius;

    // `why` is read once the limit breaks heard by then have settled.
    private void Enrage(IEnumerable<SimEnemy?> adds, Func<string> why)
    {
        limitBreaks.CatchUp();
        var enraged = adds.OfType<SimEnemy>().Where(add => add.IsAlive()).ToList();
        if (enraged.Count == 0) return;
        world.Party.WipeAllPlayers(P6ActionId.CosmoMeteorEnrage, why() + (limitBreaks.StarvedOfGauge ? TopP6LimitBreaks.GaugeWasShort : ""));
        foreach (var add in enraged)
            add.Cast(Actions.CosmoMeteorEnrage);
    }

    // Decided from where everyone stands as they land, then shown. In the game each hit leaves a vulnerability that makes
    // the next one lethal, and with every player hit once a set, a second apart, a player in someone else's spread always
    // takes a second. UNVERIFIED: that another's spread kills at once rather than at that second hit.
    private void LandTheSpreads(PartyRole[] targets)
    {
        var party = world.Party;
        var hit = targets.Select(role => party.Get(role)).OfType<SimCharacter>().Where(member => member.IsAlive()).ToList();
        var verdicts = new Verdicts();
        foreach (var target in hit)
            foreach (var member in party.Find.InsideActionAoe(P6ActionId.CosmoMeteorSpread, target.Placement()))
            {
                if (member != target) verdicts.Kill(member, P6ActionId.CosmoMeteorSpread, InSomeoneElsesSpread);
                else if (member.HasStatus(StatusId.MagicVulnerabilityUp)) verdicts.Kill(member, P6ActionId.CosmoMeteorSpread, ASecondSpread);
            }
        verdicts.Deal();
        foreach (var target in hit)
            alphaOmega.CastFromHelper(Actions.CosmoMeteorSpread, target);
    }

    private void MarkTheFlares(IEnumerable<PartyRole> flares)
    {
        foreach (var member in flares.Select(role => world.Party.Get(role)).OfType<SimCharacter>().Where(member => member.IsAlive()))
            member.AttachLockonVfx(P6LockonId.Flare);
    }

    // Decided from where everyone stands as they land, then shown. A player dead since the markers fires no flare, and a
    // dead stack target takes no stack. UNVERIFIED: every rule; the game's damage is unknown.
    private void LandTheFlaresAndTheStack(TopP6CosmoMeteorRolls rolls)
    {
        var party = world.Party;
        var flares = rolls.Flares.Select(role => party.Get(role)).OfType<SimCharacter>().Where(member => member.IsAlive()).ToList();
        var stack = party.Get(rolls.StackTarget) is { } target && target.IsAlive() ? target : null;
        IReadOnlyList<SimCharacter> soakers = stack is null ? [] : party.Find.InsideActionAoe(P6ActionId.CosmoMeteorStack, stack.Placement());

        var verdicts = new Verdicts();
        foreach (var flare in flares.Where(soakers.Contains))
            verdicts.Kill(flare, P6ActionId.CosmoMeteorStack, FlareInTheStack);
        if (stack is not null && soakers.Count < P6Rules.CosmoMeteorStack)
            foreach (var soaker in soakers)
                verdicts.Kill(soaker, P6ActionId.CosmoMeteorStack, TooFewInTheStack(soakers.Count));
        foreach (var flare in flares)
            foreach (var member in party.Find.InsideCircle(flare.Position, P6Geometry.FlareLethalWithin).Where(member => member != flare))
                verdicts.Kill(member, P6ActionId.CosmoMeteorFlare, TooCloseToAFlare);
        verdicts.Deal();

        foreach (var flare in flares)
            alphaOmega.CastFromHelper(Actions.CosmoMeteorFlare, flare);
        if (stack is not null)
            alphaOmega.CastFromHelper(Actions.CosmoMeteorStack, stack);
    }
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

    // A wave's roll, for the Gauntlet's State. Both are drawn whether or not they are forced, so forcing one leaves the
    // other as the seed had it. UNVERIFIED: that the game draws each evenly.
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
