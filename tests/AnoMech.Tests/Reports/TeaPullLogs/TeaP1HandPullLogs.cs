using System.Numerics;
using static AnoMech.Scenarios.Tea.TeaP1Constants;

namespace AnoMech.Tests;

// The Liquid Hand: its form as it glows and what its slam does, Hand of Pain against the two bars, both bosses' cleaves,
// and whom each boss turns to.
internal sealed class TeaP1HandPullLogs : ITeaPullLogMeasures
{
    private const string Section = "P1 Liquid Hand and the cleaves";

    private static readonly string[] BossDebuffs = ["Reprisal", "Feint", "Addle", "Dismantle"];

    // The statuses that take damage off their holder, by the name the log gives them.
    private static readonly string[] Mitigations =
    [
        "Rampart", "Sentinel", "Guardian", "Bulwark", "Sheltron", "Holy Sheltron", "Intervention", "Knight's Resolve", "Divine Veil", "Passage of Arms",
        "Hallowed Ground", "Vengeance", "Damnation", "Raw Intuition", "Bloodwhetting", "Stem the Flow", "Thrill of Battle", "Shake It Off", "Nascent Glint",
        "Holmgang", "Shadow Wall", "Shadowed Vigil", "Dark Mind", "Blackest Night", "Oblation", "Dark Missionary", "Living Dead", "Walking Dead", "Nebula",
        "Great Nebula", "Camouflage", "Heart of Stone", "Heart of Corundum", "Clarity of Corundum", "Heart of Light", "Aurora", "Superbolide", "Brutal Shell",
        "Arm's Length", "Sacred Soil", "Galvanize", "Catalyze", "Seraphic Veil", "Fey Illumination", "Expedience", "Desperate Measures", "Temperance",
        "Divine Benison", "Aquaveil", "Divine Caress", "Collective Unconscious", "Exaltation", "Kerachole", "Taurochole", "Holos", "Panhaima", "Haima",
        "Eukrasian Diagnosis", "Eukrasian Prognosis", "Shield Samba", "Troubadour", "Tactician", "Magick Barrier", "Tempera Coat", "Tempera Grassa",
        "Riddle of Earth", "Third Eye", "Shade Shift", "Bloodbath", "Second Wind",
    ];
    private static readonly uint[] Invulnerabilities = [82, 409, 810, 1836];

    private sealed record Glow(string Pull, double Clock, uint State, double Apart, uint? Action, double? ApartAtTheSlam, int Hit, int Killed, double? ToTheSlam,
                                bool AfterTheLiquidFell, string Note);

    // MostTaken is the hardest hit's share of its target's maximum hit points. A Hand of Pain that fails takes most of a
    // player's health; one that passes takes little.
    private sealed record Pain(string Pull, double Clock, double GapAtCast, double GapAsItLands, int Hit, int Killed, double MostTaken)
    {
        public bool Fails => MostTaken >= 50 || Killed >= 4;
    }

    private readonly List<Glow> glows = [];
    private readonly List<Pain> pains = [];
    private readonly List<double> resets = [], cleaveAim = [], cleaveHitOff = [], cleaveMissedOff = [], handTurnsAfter = [];
    private readonly List<string> cleaves = [], targets = [], shields = [];
    private readonly Dictionary<string, int> cleaveLeaves = [];
    private readonly Dictionary<string, List<double>> provokes = [];
    private int cleaveVolleys, cleavesOnTheTarget, cleavesHittingOthers, handFirstOnTheOtherTank, handFirstTargets, cleavesUnderAnInvulnerability;
    private readonly Dictionary<string, List<double>> cleaveDamage = [];

    public void Observe(TeaPull pull)
    {
        if (pull.Liquid is not { } liquid) return;
        if (pull.Hand is { } hand)
        {
            ObserveGlows(pull, liquid, hand);
            ObservePain(pull, liquid, hand);
        }
        ObserveCleaves(pull, P1ActionId.FluidSwing, "Fluid Swing", 8f + 3.5f);
        ObserveCleaves(pull, P1ActionId.FluidStrike, "Fluid Strike", 10f + 1.6f);
        ObserveTargets(pull, liquid);
    }

    private static double? Apart(TeaPull pull, uint a, uint b, DateTimeOffset at)
        => pull.Where(a, at) is { } pa && pull.Where(b, at) is { } pb ? Vector2.Distance(pa, pb) : null;

    private void ObserveGlows(TeaPull pull, uint liquid, uint hand)
    {
        foreach (var state in pull.ModelStates.Where(m => m.Actor == hand && m.State != 0))
        {
            if (Apart(pull, liquid, hand, state.Time) is not { } apart) continue;
            var slam = pull.Slams.Where(s => PullLog.Seconds(state.Time, s.Time) is > 0 and < 8).Cast<ActHit?>().FirstOrDefault();
            var hits = slam is { } s1 ? pull.Hits.Where(h => h.Action == s1.Action && h.Time == s1.Time && h.HitSomeone).ToList() : [];
            var living = hits.Where(h => pull.Outcome(h) is HitOutcome.Killed or HitOutcome.Survived).ToList();
            var turned = pull.TargetChanges.Where(t => t.Actor == hand && t.Time <= state.Time && ActorIds.IsPlayer(t.Target)).Cast<ActTargetChange?>().LastOrDefault();
            var note = turned is not { } to ? ""
                : $"; the Hand turned to {pull.Who(to.Target)} {Spread.F(PullLog.Seconds(to.Time, state.Time))} s before"
                  + (to.Target == pull.TargetOf(liquid, state.Time) ? ", the Liquid's own target" : "")
                  + (pull.Where(to.Target, state.Time) is { } stands && pull.Where(liquid, state.Time) is { } liquidAt
                      ? $", who stood {Spread.F(Vector2.Distance(stands, liquidAt))} y from the Liquid" : "");
            glows.Add(new Glow(pull.Name, pull.Clock(state.Time), state.State, apart, slam?.Action,
                               slam is { } s2 ? Apart(pull, liquid, hand, s2.Time) : null, living.Count, living.Count(h => pull.Outcome(h) == HitOutcome.Killed),
                               slam is { } s3 ? PullLog.Seconds(state.Time, s3.Time) : null,
                               slam is { } s4 && pull.HitPoints.Any(h => h.Actor == liquid && h.Hp == 0 && h.Time <= s4.Time), note));
            if (slam is { } landed && pull.ModelStates.Where(m => m.Actor == hand && m.State == 0 && Math.Abs(PullLog.Seconds(landed.Time, m.Time)) < 2)
                                          .Select(m => (DateTimeOffset?)m.Time).FirstOrDefault() is { } reset)
                resets.Add(PullLog.Seconds(landed.Time, reset));
        }
    }

    private void ObservePain(TeaPull pull, uint liquid, uint hand)
    {
        if (pull.MaxHp(liquid) is not { } max) return;
        double? Gap(DateTimeOffset at) => pull.HpAt(liquid, at) is { } a && pull.HpAt(hand, at) is { } b ? Math.Abs(a - b) * 100.0 / max : null;
        foreach (var cast in pull.CastsOf(P1ActionId.HandOfPain))
        {
            var lands = pull.Hits.Where(h => h.Action == P1ActionId.HandOfPain && PullLog.Seconds(cast.Time, h.Time) is > 0 and < 5).ToList();
            if (lands.Count == 0 || Gap(cast.Time) is not { } atCast || Gap(lands[0].Time) is not { } asItLands) continue;
            var living = lands.Where(h => h.HitSomeone && pull.Outcome(h) is HitOutcome.Killed or HitOutcome.Survived).ToList();
            var most = living.Select(h => h.Damage is { } damage && pull.MaxHp(h.Target) is { } whole && whole > 0
                                         ? damage * 100.0 / whole : 0).DefaultIfEmpty(0).Max();
            pains.Add(new Pain(pull.Name, pull.Clock(cast.Time), atCast, asItLands, living.Count, living.Count(h => pull.Outcome(h) == HitOutcome.Killed), most));
        }
    }

    private void ObserveCleaves(TeaPull pull, uint action, string name, float reach)
    {
        foreach (var volley in pull.Volleys(action))
        {
            var (time, boss) = (volley[0].Time, volley[0].Source);
            if (volley[0].Rotation is not { } rotation || pull.Where(boss, time) is not { } from) continue;
            cleaveVolleys++;
            var hit = volley.Where(h => h.HitSomeone).ToList();
            var target = pull.TargetOf(boss, time);
            if (target is { } aimedAt && hit.Any(h => h.Target == aimedAt)) cleavesOnTheTarget++;
            if (hit.Count > 1) cleavesHittingOthers++;
            if (target is { } tank && pull.Where(tank, time) is { } tankAt && Vector2.Distance(tankAt, from) > 1.5f)
                cleaveAim.Add(TeaPull.Degrees(PullLog.AngleBetween(rotation, PullLog.HeadingTo(from, tankAt))));
            foreach (var player in pull.Living(time))
            {
                if (pull.Positions.At(player, time) is not { Gap.TotalSeconds: < 0.5 } pose) continue;
                var distance = Vector2.Distance(pose.Position, from);
                if (distance < 2.5f || distance > reach) continue;
                var off = TeaPull.Degrees(PullLog.AngleBetween(rotation, PullLog.HeadingTo(from, pose.Position)));
                (hit.Any(h => h.Target == player) ? cleaveHitOff : cleaveMissedOff).Add(off);
            }
            foreach (var one in hit)
            {
                foreach (var status in one.Applies)
                {
                    var left = $"{name}: {pull.StatusName(status)} ({status})";
                    cleaveLeaves[left] = cleaveLeaves.GetValueOrDefault(left) + 1;
                }
                if (pull.Invulnerability(one.Target, time) is not null) cleavesUnderAnInvulnerability++;
                else if (one.Damage is { } damage && pull.Outcome(one) != HitOutcome.AlreadyDead)
                {
                    if (!cleaveDamage.TryGetValue(name, out var taken)) cleaveDamage[name] = taken = [];
                    taken.Add(damage);
                }
                cleaves.Add($"{pull.Name} at {Spread.F(pull.Clock(time))} s: {name} on {pull.Who(one.Target)}"
                            + (one.Target == target ? "" : ", not the player its boss is turned to")
                            + $", {Damage(pull, one)}; {Cover(pull, one.Target, boss, time)}");
            }
        }
    }

    // What the hit took, from the hit points the target had as it went off to those its effect result left.
    internal static string Damage(TeaPull pull, ActHit hit)
        => pull.Invulnerability(hit.Target, hit.Time) is { } invulnerability ? $"under {invulnerability}"
            : (hit.Damage is { } damage ? $"for {damage}, " : "") + (pull.HpAt(hit.Target, hit.Time) is { } before && pull.HpAfter(hit) is { } after
                ? $"{before} to {after} HP of {pull.MaxHp(hit.Target)}{(after == 0 ? " (killed)" : "")}"
                : pull.Describe(hit));

    // The mitigation the target held as the hit went off, and the boss's own debuffs: what covered the hit.
    internal static string Cover(TeaPull pull, uint target, uint boss, DateTimeOffset at)
    {
        IEnumerable<string> Held(uint holder, Func<ActStatus, bool> counts)
            => pull.Statuses.Where(s => s.Gained && s.Target == holder && s.Time <= at && s.Time.AddSeconds(s.Duration) > at && counts(s)
                                        && !pull.Statuses.Any(l => !l.Gained && l.Target == holder && l.Status == s.Status && l.Time > s.Time && l.Time <= at))
                   .Select(s => pull.StatusName(s.Status)).Distinct().Order();
        var own = Held(target, s => Mitigations.Contains(pull.StatusName(s.Status))).ToList();
        var onTheBoss = Held(boss, s => BossDebuffs.Contains(pull.StatusName(s.Status))).ToList();
        return $"holding {(own.Count == 0 ? "no mitigation" : string.Join(", ", own))}; on the boss {(onTheBoss.Count == 0 ? "nothing" : string.Join(", ", onTheBoss))}";
    }

    private void ObserveTargets(TeaPull pull, uint liquid)
    {
        string Turns(uint boss)
            => string.Join(", ", pull.TargetChanges.Where(t => t.Actor == boss && ActorIds.IsPlayer(t.Target))
                                     .Select(t => $"{pull.Who(t.Target)} at {Spread.F(pull.Clock(t.Time), "0.0")}"));
        targets.Add($"{pull.Name}: the Liquid turns to {Turns(liquid)}" + (pull.Hand is { } theHand ? $"; the Hand to {Turns(theHand)}" : ""));
        if (pull.Hand is { } hand && pull.Combatants.Where(c => c.Added && c.Actor == hand).Select(c => (DateTimeOffset?)c.Time).FirstOrDefault() is { } appeared
            && pull.TargetChanges.Where(t => t.Actor == hand && ActorIds.IsPlayer(t.Target)).Cast<ActTargetChange?>().FirstOrDefault() is { } first)
        {
            handFirstTargets++;
            handTurnsAfter.Add(PullLog.Seconds(appeared, first.Time));
            if (pull.IsTank(first.Target) && first.Target != pull.TargetOf(liquid, first.Time)) handFirstOnTheOtherTank++;
        }
        var first1 = pull.Hits.Where(h => h.Action == TeaLogIds.LiquidAutoAttack && h.HitSomeone).Select(h => (uint?)h.Target).FirstOrDefault();
        foreach (var press in pull.Hits.Where(h => h.Action is TeaLogIds.Provoke or TeaLogIds.Shirk && ActorIds.IsPlayer(h.Source)))
        {
            if (!pull.LoggedFromItsStart) continue;
            var who = press.Source == first1 ? "the Liquid's first tank" : "the other tank";
            var what = press.Action == TeaLogIds.Shirk ? "Shirk"
                : pull.BaseOf(press.Target) == P1BNpcBaseId.LiquidHand ? "Provoke on the Hand" : "Provoke on the Liquid";
            var key = $"{who}: {what}";
            if (!provokes.TryGetValue(key, out var times)) provokes[key] = times = [];
            times.Add(pull.Clock(press.Time));
        }
        foreach (var gain in pull.Statuses.Where(s => s.Gained && Invulnerabilities.Contains(s.Status) && ActorIds.IsPlayer(s.Target)))
            shields.Add($"{pull.Name} at {Spread.F(pull.Clock(gain.Time), "0.0")} s: {pull.Who(gain.Target)} gains {pull.StatusName(gain.Status)} "
                        + $"for {Spread.F(gain.Duration, "0.0")} s");
    }

    public IEnumerable<LogFinding> Findings()
    {
        string Shown(uint state)
        {
            var apart = new Spread(glows.Where(g => g.State == state).Select(g => g.Apart));
            return apart.Count == 0 ? "never shown" : $"shown with the bosses {Spread.F(apart.Min)} to {Spread.F(apart.Max)} y apart (n={apart.Count})";
        }
        var fits = glows.Count(g => g.State == (g.Apart > P1Rules.HandShowsAPalmBeyond ? P1ModelState.HandPalm : P1ModelState.HandFist));
        var nearestPalm = glows.Where(g => g.State == P1ModelState.HandPalm).Select(g => (double?)g.Apart).Min();
        var farthestFist = glows.Where(g => g.State == P1ModelState.HandFist).Select(g => (double?)g.Apart).Max();
        yield return new LogFinding(Section, "The Hand's form as it glows: its model state against the bosses' distance then, centre to centre",
            SimSource.Line(TeaSource.Constants, "float HandShowsAPalmBeyond ="),
            $"state {P1ModelState.HandPalm} when they stand more than {P1Rules.HandShowsAPalmBeyond} y apart, else state {P1ModelState.HandFist}",
            glows.Count == 0 ? "no glow"
                : $"{fits} of {glows.Count} glows fit: state {P1ModelState.HandPalm} {Shown(P1ModelState.HandPalm)}; state {P1ModelState.HandFist} "
                  + Shown(P1ModelState.HandFist)
                  + (nearestPalm is { } palm && farthestFist is { } fist
                      ? palm > fist ? $". One line parts them, between {Spread.F(fist)} and {Spread.F(palm)} y" : ". No one distance parts them" : ""),
            glows.Count == 0 ? Verdict.NotInTheseLogs : fits == glows.Count ? Verdict.Matches : Verdict.Differs)
        {
            Details = glows.Select(g => $"{g.Pull} at {Spread.F(g.Clock)} s: state {g.State}, the bosses {Spread.F(g.Apart)} y apart{g.Note}").ToList(),
        };

        var slammed = glows.Where(g => g.Action is not null).ToList();
        var asTheSim = slammed.Count(g => g.Action == (g.State == P1ModelState.HandPalm ? P1ActionId.HandOfParting : P1ActionId.HandOfPrayer));
        yield return new LogFinding(Section, "Which action each form slams with",
            SimSource.Line(TeaSource.Mechanics, "hand.Cast(palm ? Actions.HandOfParting : Actions.HandOfPrayer);"),
            $"state {P1ModelState.HandPalm} slams with Hand of Parting ({P1ActionId.HandOfParting}), state {P1ModelState.HandFist} with Hand of Prayer ({P1ActionId.HandOfPrayer})",
            slammed.Count == 0 ? "no slam"
                : TeaP1ActorsPullLogs.Tally(slammed.Select(g => $"state {g.State} with {(g.Action == P1ActionId.HandOfPrayer ? "Hand of Prayer" : "Hand of Parting")} ({g.Action})")),
            slammed.Count == 0 ? Verdict.NotInTheseLogs : asTheSim == slammed.Count ? Verdict.Matches : Verdict.Differs);

        yield return Slam("The slam of state " + P1ModelState.HandPalm + ", shown with the bosses apart", P1ModelState.HandPalm, "float PalmNeedsThemWithin =",
                          $"kills everyone when the bosses' centres are more than {P1Rules.PalmNeedsThemWithin} y apart as it lands",
                          g => g.ApartAtTheSlam > P1Rules.PalmNeedsThemWithin);
        yield return Slam("The slam of state " + P1ModelState.HandFist + ", shown with the bosses together", P1ModelState.HandFist, "float FistNeedsThemBeyond =",
                          $"kills everyone when the bosses' centres are within {P1Rules.FistNeedsThemBeyond} y as it lands",
                          g => g.ApartAtTheSlam <= P1Rules.FistNeedsThemBeyond);

        var glowsAt = SimSource.PartTime(TeaSource.Scenario, "fight.TheHandGlows(start + ");
        var slamsAt = SimSource.PartTime(TeaSource.Scenario, "fight.TheHandSlams(start + ");
        var toTheSlam = new Spread(glows.Where(g => g.ToTheSlam is not null).Select(g => g.ToTheSlam!.Value));
        yield return new LogFinding(Section, "From the glow to the slam", slamsAt.Where,
            slamsAt.Seconds - glowsAt.Seconds is { } gap ? $"{Spread.F(gap)} s" : "not found in the scenario", toTheSlam.Describe(" s"),
            slamsAt.Seconds - glowsAt.Seconds is { } sim ? LogFinding.Compare(toTheSlam, sim, 0.25) : Verdict.Inconclusive);

        var reset = new Spread(resets);
        yield return new LogFinding(Section, "The Hand drops its form: its model state set back to 0, after the slam's ability",
            SimSource.Line(TeaSource.Mechanics, "hand.SetModelState(0);"), "0 s: as it slams", reset.Describe(" s"), LogFinding.Compare(reset, 0, 0.25));

        var passed = pains.Where(p => p.Hit > 0 && !p.Fails).ToList();
        var failed = pains.Where(p => p.Hit > 0 && p.Fails).ToList();
        yield return new LogFinding(Section, "Hand of Pain against the two bars: the gap between the bosses' hit points, as a share of their maximum",
            SimSource.Line(TeaSource.Actions, "public static readonly EnemyAction HandOfPain ="), "never fails: no health is played",
            pains.Count == 0 ? "no Hand of Pain"
                : (passed.Count == 0 ? "no cast took little"
                      : $"{passed.Count} casts took little, with gaps up to {Spread.F(passed.Max(p => p.GapAsItLands))} % as they land "
                        + $"({Spread.F(passed.Max(p => p.GapAtCast))} % as the cast starts)")
                  + (failed.Count == 0 ? "; none took most of a player's health"
                      : $"; {failed.Count} took most of a player's health, with gaps from {Spread.F(failed.Min(p => p.GapAsItLands))} % as they land "
                        + $"({Spread.F(failed.Min(p => p.GapAtCast))} % as the cast starts)")
                  + ". Hit points up to a second old",
            LogFinding.Unplayed(pains.Count))
        {
            Details = pains.Select(p => $"{p.Pull} at {Spread.F(p.Clock)} s: gap {Spread.F(p.GapAtCast)} % as the cast starts, {Spread.F(p.GapAsItLands)} % as it lands: "
                                        + $"killed {p.Killed} of {p.Hit}, its hardest hit {Spread.F(p.MostTaken, "0")} % of its target's maximum").ToList(),
        };

        var aim = new Spread(cleaveAim);
        yield return new LogFinding(Section, "Fluid Swing and Fluid Strike go at the boss's target",
            SimSource.Line(TeaSource.Mechanics, "enemy.Cast(cleave, target);"), "each boss cleaves at its one target",
            cleaveVolleys == 0 ? "no cleave"
                : $"{cleavesOnTheTarget} of {cleaveVolleys} cleaves hit the player their boss was turned to; the cleave's direction off that player's bearing: "
                  + $"{aim.Describe(" degrees", "0.0")} (client positions); {cleavesHittingOthers} cleaves hit someone else too",
            cleaveVolleys == 0 ? Verdict.NotInTheseLogs : cleavesOnTheTarget == cleaveVolleys ? Verdict.Matches : Verdict.Differs);

        var (hitOff, missedOff) = (new Spread(cleaveHitOff), new Spread(cleaveMissedOff));
        var half = TeaPull.Degrees(P1Geometry.CleaveHalfAngle);
        yield return new LogFinding(Section, "The cleaves' half-angle: how far off the cleave's axis the players in reach it hit and missed stood",
            SimSource.Line(TeaSource.Constants, "float CleaveHalfAngle ="), $"{Spread.F(half, "0.0")} degrees",
            hitOff.Count + missedOff.Count == 0 ? "no cleave with a player in reach"
                : $"hit: {(hitOff.Count == 0 ? "nobody" : $"19 in 20 within {Spread.F(hitOff.Quantile(0.95), "0.0")} degrees off, at most {Spread.F(hitOff.Max, "0.0")} (n={hitOff.Count})")}; "
                  + $"missed: {(missedOff.Count == 0 ? "nobody" : $"19 in 20 beyond {Spread.F(missedOff.Quantile(0.05), "0.0")} degrees off, at least {Spread.F(missedOff.Min, "0.0")} (n={missedOff.Count})")}. "
                  + "Client positions",
            hitOff.Count + missedOff.Count == 0 ? Verdict.NotInTheseLogs
            : hitOff.Count > 0 && hitOff.Quantile(0.95) > half + 20 ? Verdict.Differs
            : missedOff.Count > 0 && missedOff.Quantile(0.05) < half - 20 ? Verdict.Differs
            : Verdict.Inconclusive);

        yield return new LogFinding(Section, "What a cleave leaves on whoever it hits",
            SimSource.Line(TeaSource.Actions, "public static readonly EnemyAction FluidSwing ="), $"the wave debuff, status {P1StatusId.WaterResistanceDown}",
            cleaveVolleys == 0 ? "no cleave"
                : cleaveLeaves.Count == 0 ? "no cleave's hit names a status among its own effects"
                : string.Join("; ", cleaveLeaves.OrderByDescending(l => l.Value).Select(l => $"{l.Key} x{l.Value}")),
            cleaveVolleys == 0 ? Verdict.NotInTheseLogs
            : cleaveLeaves.Count > 0 && cleaveLeaves.Keys.All(k => k.Contains($"({P1StatusId.WaterResistanceDown})", StringComparison.Ordinal)) ? Verdict.Matches : Verdict.Differs);

        yield return new LogFinding(Section, "The cleaves' damage and what covered it",
            SimSource.Line(TeaSource.Constants, "float FluidSwingMitigation ="),
            $"a human tank on a tank's job needs {P1Rules.FluidSwingMitigation} of mitigation under Fluid Swing and {P1Rules.FluidStrikeMitigation} under Fluid Strike",
            cleaves.Count == 0 ? "no cleave hit anyone"
                : $"{cleaves.Count} hits: " + string.Join("; ", cleaveDamage.OrderBy(d => d.Key).Select(d => $"{d.Key} {new Spread(d.Value).Describe("", "0")}"))
                  + $"; {cleavesUnderAnInvulnerability} under an invulnerability left out. Each is below with the hit points it took and the statuses that covered it",
            cleaves.Count == 0 ? Verdict.NotInTheseLogs : Verdict.Inconclusive)
        {
            Details = cleaves,
        };

        var turns = new Spread(handTurnsAfter);
        yield return new LogFinding(Section, "Whom the bosses turn to through the opening",
            SimSource.Line(TeaSource.Mechanics, "public void TakeTargets()"),
            "not played: the Liquid on the main tank's seat and the Hand on the off tank's from the start of a part",
            targets.Count == 0 ? "no pull"
                : $"the Hand first turns to a player {turns.Describe(" s")} after it appears, in {handFirstOnTheOtherTank} of {handFirstTargets} pulls the tank the "
                  + "Liquid is not on. Presses, in seconds from the combat flag: "
                  + string.Join("; ", provokes.OrderBy(p => p.Value.Min()).Select(p => $"{p.Key} at {string.Join(", ", p.Value.Order().Select(t => Spread.F(t, "0.0")))}")),
            LogFinding.Unplayed(targets.Count))
        {
            Details = targets,
        };

        yield return new LogFinding(Section, "The invulnerabilities the tanks used", TeaSource.OpeningNotPlayed, "not played",
            shields.Count == 0 ? "none" : $"{shields.Count}, below", LogFinding.Unplayed(shields.Count))
        {
            Details = shields,
        };
    }

    private LogFinding Slam(string name, uint state, string constant, string sim, Func<Glow, bool> simKills)
    {
        // A slam that lands once the Living Liquid has fallen shows nothing: the fight is over.
        var slams = glows.Where(g => g.State == state && g.ApartAtTheSlam is not null && g.Hit > 0 && !g.AfterTheLiquidFell).ToList();
        var late = glows.Count(g => g.State == state && g.AfterTheLiquidFell);
        var asTheSim = slams.Count(g => simKills(g) ? 2 * g.Killed >= g.Hit : g.Killed == 0);
        return new LogFinding(Section, $"{name}: the bosses' distance as it lands, and whom it killed", SimSource.Line(TeaSource.Constants, constant), sim,
            (slams.Count == 0 ? "no such slam"
                : string.Join("; ", slams.Select(g => $"{Spread.F(g.ApartAtTheSlam!.Value)} y apart: killed {g.Killed} of {g.Hit}")))
            + (late == 0 ? "" : $"; {late} more landed after the Living Liquid had fallen and are left out"),
            slams.Count == 0 ? Verdict.NotInTheseLogs : asTheSim == slams.Count ? Verdict.Matches : Verdict.Differs)
        {
            Details = slams.Select(g => $"{g.Pull} at {Spread.F(g.Clock)} s: shown with the bosses {Spread.F(g.Apart)} y apart{g.Note}").ToList(),
        };
    }
}
