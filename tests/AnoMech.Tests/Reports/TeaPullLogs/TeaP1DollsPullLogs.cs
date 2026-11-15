using System.Numerics;
using AnoMech.Scenarios.Tea;
using static AnoMech.Scenarios.Tea.TeaP1Constants;

namespace AnoMech.Tests;

// The opening's Jagd Dolls, which no part plays yet, and the Emboli of both Pressurizes.
internal sealed class TeaP1DollsPullLogs : ITeaPullLogMeasures
{
    private const string Section = "P1 Jagd Dolls and Emboli";
    private const float ExhaustReach = 8f + 0.8f;

    private readonly List<double> dollRadii = [], tetherAfterTargetable = [], exhaustAfterTargetable = [], exhaustAgain = [], exhaustHit = [], exhaustMissed = [];
    private readonly List<double> aetheroplasm = [], fedAfterTether = [], fedApart = [], dollSpeeds = [];
    private readonly List<string> dollBearings = [], dollBearingsFromTheOpenQuarter = [], tetherDetails = [], feeds = [], stacks = [];
    private readonly List<(double Share, int Hit, int Killed)> fed = [];
    private readonly LethalHits dollKilled = new(), exploded = new();
    private int dollTethers, tethersToTheFirstToActOnIt, oneExhaust, oneExhaustExploded, twoExhausts, twoExhaustsExploded;

    private readonly List<string> pressurizes = [], emboli = [], bursts = [];
    private readonly Dictionary<string, int> leftOut = [];
    private readonly List<double> embolusAppears = [], embolusOffItsTornado = [], embolusSetsOff = [], embolusSetsOffAfterPressurize = [], embolusSpeeds = [], embolusAim = [], embolusPastItsSpot = [];
    private readonly List<double> embolusFirstMoveLine = [], embolusFirstMoveLineOut = [];
    private readonly List<double> burstNearestPlayer = [], burstFromTheLiquid = [], burstFromTheHand = [], embolusStopsOut = [], embolusLasts = [];
    private int pressurizeVolleys, pressurizesOfTwo, pairs, pairsOneForEachBoss;

    // Each fed doll's share of its hit points with the hardest hit of its Reducible Complexity. Two hits of one second
    // both read as the kill, so what a feed did is told by its damage.
    private readonly List<(double Share, int Hardest)> fedHardest = [];
    private const int AHeavyHit = 20000;
    private readonly LethalHits outbursts = new();

    public void Observe(TeaPull pull)
    {
        ObserveDolls(pull);
        ObserveEmboli(pull);
    }

    private void ObserveDolls(TeaPull pull)
    {
        var dolls = pull.Combatants.Where(c => c.Added && c.BNpcBase == TeaLogIds.JagdDoll).GroupBy(c => c.Actor).Select(g => g.First()).ToList();
        if (dolls.Count == 0) return;
        var open = pull.TornadoesAt(dolls[0].Time)?.OpenBearing;
        foreach (var doll in dolls.Where(d => d.Position is not null))
        {
            dollRadii.Add(Vector2.Distance(doll.Position!.Value, TeaPull.Centre));
            var bearing = TeaPull.Bearing(doll.Position.Value);
            dollBearings.Add(Spread.F(bearing, "0.0"));
            if (open is { } quarter) dollBearingsFromTheOpenQuarter.Add(Spread.F((bearing - quarter + 360) % 360, "0.0"));
        }
        foreach (var doll in dolls)
        {
            var targetable = pull.Targetables.Where(t => t.Actor == doll.Actor && t.Targetable).Select(t => (DateTimeOffset?)t.Time).FirstOrDefault();
            if (pull.Tethers.Where(t => t.Source == doll.Actor && t.Id == TeaLogIds.DollTether).Cast<ActTether?>().FirstOrDefault() is { } tether)
            {
                dollTethers++;
                if (targetable is { } shown) tetherAfterTargetable.Add(PullLog.Seconds(shown, tether.Time));
                var first = pull.Hits.Where(h => h.Target == doll.Actor && ActorIds.IsPlayer(h.Source)).Cast<ActHit?>().FirstOrDefault();
                if (first?.Source == tether.Target) tethersToTheFirstToActOnIt++;
                tetherDetails.Add($"{pull.Name}: a doll tethers {pull.Who(tether.Target)} at {Spread.F(pull.Clock(tether.Time))} s; the first player's action on it: "
                                  + (first is { } f ? $"{pull.Who(f.Source)}'s {pull.ActionName(f.Action)} at {Spread.F(pull.Clock(f.Time))} s" : "none"));
            }
            var pulses = pull.Hits.Where(h => h.Source == doll.Actor && h.Action == TeaLogIds.Exhaust).GroupBy(h => h.Time).OrderBy(g => g.Key).ToList();
            if (targetable is { } since && pulses.Count > 0) exhaustAfterTargetable.Add(PullLog.Seconds(since, pulses[0].Key));
            exhaustAgain.AddRange(pulses.Zip(pulses.Skip(1), (a, b) => PullLog.Seconds(a.Key, b.Key)));
            foreach (var pulse in pulses)
            {
                if (pull.Where(doll.Actor, pulse.Key) is not { } from) continue;
                foreach (var player in pull.Living(pulse.Key))
                    if (pull.Positions.At(player, pulse.Key) is { Gap.TotalSeconds: < 0.5 } pose)
                        (pulse.Any(h => h.Target == player) ? exhaustHit : exhaustMissed).Add(Vector2.Distance(pose.Position, from));
            }
            ObserveItsEnd(pull, doll.Actor);
            dollSpeeds.AddRange(Speeds(pull, doll.Actor));
        }
        aetheroplasm.AddRange(pull.Statuses.Where(s => s.Gained && s.Status == TeaLogIds.LuminousAetheroplasm).Select(s => (double)s.Duration));
        // A player takes a stack from each doll whose Exhaust reaches it: two dolls at once are two stacks.
        foreach (var taken in pull.Hits.Where(h => h.Action == TeaLogIds.Exhaust && h.HitSomeone).GroupBy(h => h.Target)
                                  .SelectMany(g => PullLog.Clusters(g, h => h.Time, 0.2)))
        {
            var (player, time) = (taken[0].Target, taken[0].Time);
            var explosion = pull.Hits.Where(h => h.Action == TeaLogIds.ExhaustFromAPlayer && h.Source == player && PullLog.Seconds(time, h.Time) is >= -0.2 and < 3)
                                .Select(h => (DateTimeOffset?)h.Time).FirstOrDefault();
            if (taken.Count == 1)
            {
                oneExhaust++;
                if (explosion is not null) oneExhaustExploded++;
                continue;
            }
            twoExhausts++;
            if (explosion is not null) twoExhaustsExploded++;
            stacks.Add($"{pull.Name} at {Spread.F(pull.Clock(time))} s: {pull.Who(player)} is hit by {taken.Count} dolls' Exhausts at once: "
                       + (explosion is { } burst ? $"explodes {Spread.F(PullLog.Seconds(time, burst))} s later" : "no explosion follows"));
        }
        exploded.Observe(pull, pull.Hits.Where(h => h.Action == TeaLogIds.ExhaustFromAPlayer), h => $"a player's explosion at {Spread.F(pull.Clock(h.Time))} s");
        dollKilled.Observe(pull, pull.Hits.Where(h => h.Action == TeaLogIds.ReducibleComplexityKilled),
                           h => $"a dead doll's Reducible Complexity at {Spread.F(pull.Clock(h.Time))} s");
    }

    // A doll is fed when its feed tether goes out to a boss, and its Reducible Complexity follows.
    private void ObserveItsEnd(TeaPull pull, uint doll)
    {
        if (pull.Tethers.Where(t => t.Source == doll && t.Id == TeaLogIds.FeedTether).Cast<ActTether?>().FirstOrDefault() is not { } feed) return;
        var share = pull.HpAt(doll, feed.Time) is { } hp && pull.MaxHp(doll) is { } max && max > 0 ? hp * 100.0 / max : double.NaN;
        var apart = pull.Where(doll, feed.Time) is { } at && pull.Where(feed.Target, feed.Time) is { } bossAt ? Vector2.Distance(at, bossAt) : float.NaN;
        var burst = pull.Hits.Where(h => h.Source == doll && h.Action is TeaLogIds.ReducibleComplexityFed or TeaLogIds.ReducibleComplexityKilled && h.Time >= feed.Time).ToList();
        var living = burst.Where(h => h.HitSomeone && pull.Outcome(h) is HitOutcome.Killed or HitOutcome.Survived).ToList();
        var killed = living.Count(h => pull.Outcome(h) == HitOutcome.Killed);
        if (burst.Count > 0) fedAfterTether.Add(PullLog.Seconds(feed.Time, burst[0].Time));
        if (!float.IsNaN(apart)) fedApart.Add(apart);
        if (!double.IsNaN(share) && burst.Count > 0 && burst[0].Action == TeaLogIds.ReducibleComplexityFed && living.Count > 0)
        {
            fed.Add((share, living.Count, killed));
            fedHardest.Add((share, living.Max(h => h.Damage ?? 0)));
        }
        var eater = pull.BaseOf(feed.Target) switch
        {
            P1BNpcBaseId.LivingLiquid => "the Living Liquid", P1BNpcBaseId.LiquidHand => "the Liquid Hand", P1BNpcBaseId.LiquidRage => "a Liquid Rage",
            _ => "something else",
        };
        feeds.Add($"{pull.Name} at {Spread.F(pull.Clock(feed.Time))} s: a doll at {(double.IsNaN(share) ? "unknown hit points" : $"{Spread.F(share, "0.0")} % of its hit points")}, "
                  + $"{(float.IsNaN(apart) ? "an unknown distance" : $"{Spread.F(apart)} y")} from {eater}, "
                  + $"is fed to it; " + (burst.Count == 0 ? "no Reducible Complexity follows"
                      : $"Reducible Complexity ({burst[0].Action}) {Spread.F(PullLog.Seconds(feed.Time, burst[0].Time))} s later kills {killed} of {living.Count}"));
    }

    // An actor's speed at full pace: over the steps between two of its own move lines that come within a tenth of its
    // fastest. A walk's first and last steps are part steps and read slow.
    internal static IEnumerable<double> Speeds(TeaPull pull, uint actor)
    {
        var moves = pull.Moves.Where(m => m.Actor == actor && !m.Placed).ToList();
        var steps = moves.Zip(moves.Skip(1), (a, b) => (Seconds: PullLog.Seconds(a.Time, b.Time), Length: (double)Vector2.Distance(a.Position, b.Position)))
                         .Where(s => s.Seconds is > 0.25 and < 0.4 && s.Length > 0.3).Select(s => s.Length / s.Seconds).ToList();
        return steps.Count < 3 ? [] : steps.Where(s => s >= 0.9 * steps.Max());
    }

    private void ObserveEmboli(TeaPull pull)
    {
        if (pull.Liquid is not { } liquid || pull.Hand is not { } hand) return;
        foreach (var volley in pull.Volleys(P1ActionId.Pressurize))
        {
            var time = volley[0].Time;
            var set = pull.TornadoesAt(time);
            var casters = volley.Select(h => h.Source).Distinct().ToList();
            pressurizeVolleys++;
            if (casters.Count == 2) pressurizesOfTwo++;
            var named = casters.Select(c => pull.Where(c, time) is { } at ? set?.Which(at) : null).ToList();
            var without = Enum.GetValues<TeaP1Tornado>().Where(t => !named.Contains(t)).ToList();
            if (without.Count == 1 && named.All(n => n is not null))
                leftOut[without[0].ToString()] = leftOut.GetValueOrDefault(without[0].ToString()) + 1;
            pressurizes.Add($"{pull.Name} at {Spread.F(pull.Clock(time))} s: Pressurize from {string.Join(" and ", named.Select(n => n?.ToString() ?? "a tornado"))} "
                            + $"({set?.Layout ?? "no set"})");

            var orbs = pull.Combatants.Where(c => c.Added && c.BNpcBase == P1BNpcBaseId.Embolus && PullLog.Seconds(time, c.Time) is >= 0 and < 3
                                                  && c.Position is not null && c.Heading is not null).ToList();
            if (pull.Where(liquid, time) is not { } liquidAt || pull.Where(hand, time) is not { } handAt) continue;
            var aims = new List<TeaP1Boss>();
            foreach (var orb in orbs)
            {
                var (at, heading) = (orb.Position!.Value, (double)orb.Heading!.Value);
                embolusAppears.Add(PullLog.Seconds(time, orb.Time));
                embolusOffItsTornado.Add(casters.Select(c => pull.Where(c, time) is { } tornado ? Vector2.Distance(tornado, at) : float.MaxValue).Min());
                var toTheLiquid = TeaPull.Degrees(PullLog.AngleBetween(heading, PullLog.HeadingTo(at, liquidAt)));
                var toTheHand = TeaPull.Degrees(PullLog.AngleBetween(heading, PullLog.HeadingTo(at, handAt)));
                var aimsAt = toTheLiquid <= toTheHand ? TeaP1Boss.LivingLiquid : TeaP1Boss.LiquidHand;
                aims.Add(aimsAt);
                embolusAim.Add(Math.Min(toTheLiquid, toTheHand));
                var spot = aimsAt == TeaP1Boss.LivingLiquid ? liquidAt : handAt;
                var moves = pull.Moves.Where(m => m.Actor == orb.Actor && !m.Placed && Vector2.Distance(m.Position, at) > 0.01f).ToList();
                var path = "never moves";
                if (moves.Count >= 2)
                {
                    embolusFirstMoveLine.Add(PullLog.Seconds(time, moves[0].Time));
                    embolusFirstMoveLineOut.Add(Vector2.Distance(at, moves[0].Position));
                    var seconds = PullLog.Seconds(moves[0].Time, moves[^1].Time);
                    var speed = seconds > 1 ? Vector2.Distance(moves[0].Position, moves[^1].Position) / seconds : double.NaN;
                    if (!double.IsNaN(speed))
                    {
                        embolusSpeeds.Add(speed);
                        // Its first move line shows it already on its way: it set off that stretch earlier.
                        var underWay = Vector2.Distance(at, moves[0].Position) / speed;
                        embolusSetsOff.Add(PullLog.Seconds(orb.Time, moves[0].Time) - underWay);
                        embolusSetsOffAfterPressurize.Add(PullLog.Seconds(time, moves[0].Time) - underWay);
                    }
                    var went = Vector2.Distance(at, moves[^1].Position);
                    var past = went - Vector2.Distance(at, spot);
                    var stillMoving = pull.End is not { } end || PullLog.Seconds(moves[^1].Time, end) < 1.5;
                    if (!stillMoving || past > 0) embolusPastItsSpot.Add(past);
                    if (!stillMoving) embolusStopsOut.Add(Vector2.Distance(moves[^1].Position, TeaPull.Centre));
                    path = $"its first move line {Spread.F(PullLog.Seconds(orb.Time, moves[0].Time))} s after appearing, "
                           + $"{(double.IsNaN(speed) ? "too few move lines for its speed" : $"{Spread.F(speed)} y/s")}, {Spread.F(went)} y by its last move line "
                           + $"at {Spread.F(pull.Clock(moves[^1].Time))} s ({(stillMoving ? "the pull's end" : "then no more")}), at {TeaPull.Spot(moves[^1].Position)}, "
                           + $"{Spread.F(Vector2.Distance(moves[^1].Position, TeaPull.Centre))} y from the centre; the spot it left for was "
                           + $"{Spread.F(Vector2.Distance(at, spot))} y away";
                }
                var gone = pull.Combatants.Where(c => !c.Added && c.Actor == orb.Actor && c.Time > orb.Time).Select(c => (DateTimeOffset?)c.Time).FirstOrDefault();
                if (gone is { } left) embolusLasts.Add(PullLog.Seconds(orb.Time, left));
                path += gone is { } removed ? $"; removed {Spread.F(PullLog.Seconds(orb.Time, removed))} s after appearing" : "; still there as its pull ended";
                emboli.Add($"{pull.Name}: an Embolus appears {Spread.F(PullLog.Seconds(time, orb.Time))} s after Pressurize at {TeaPull.Spot(at)}, heading "
                           + $"{Spread.F(Math.Min(toTheLiquid, toTheHand), "0.0")} degrees off where {(aimsAt == TeaP1Boss.LivingLiquid ? "the Liquid" : "the Hand")} stood; {path}");
            }
            if (orbs.Count == 2)
            {
                pairs++;
                if (aims.Distinct().Count() == 2) pairsOneForEachBoss++;
            }
        }
        foreach (var burst in pull.Volleys(P1ActionId.Outburst))
        {
            var (time, orb) = (burst[0].Time, burst[0].Source);
            if (pull.Where(orb, time) is not { } at) continue;
            var player = pull.Living(time).Select(p => pull.Positions.At(p, time)).Where(p => p is { Gap.TotalSeconds: < 0.5 })
                             .Select(p => (double)Vector2.Distance(p!.Value.Position, at)).DefaultIfEmpty(double.NaN).Min();
            var fromTheLiquid = pull.Where(liquid, time) is { } liquidAt ? (double)Vector2.Distance(liquidAt, at) : double.NaN;
            var fromTheHand = pull.Where(hand, time) is { } handAt ? (double)Vector2.Distance(handAt, at) : double.NaN;
            if (!double.IsNaN(player)) burstNearestPlayer.Add(player);
            if (!double.IsNaN(fromTheLiquid)) burstFromTheLiquid.Add(fromTheLiquid);
            if (!double.IsNaN(fromTheHand)) burstFromTheHand.Add(fromTheHand);
            static string Away(double distance) => double.IsNaN(distance) ? "at no known distance" : $"{Spread.F(distance)} y from it";
            bursts.Add($"{pull.Name} at {Spread.F(pull.Clock(time))} s: Outburst from an Embolus at {TeaPull.Spot(at)}; "
                       + (double.IsNaN(player) ? "no living player near" : $"the nearest living player {Away(player)}")
                       + $", the Living Liquid's centre {Away(fromTheLiquid)}, the Liquid Hand's {Away(fromTheHand)}; {pull.Living(time).Count()} players alive");
        }
        outbursts.Observe(pull, pull.Hits.Where(h => h.Action == P1ActionId.Outburst), h => $"Outburst at {Spread.F(pull.Clock(h.Time))} s");
    }

    // A tally of hits on what the sim does not play is a measurement, not a match.
    private static LogFinding Unplayed(LogFinding finding)
        => finding.Verdict == Verdict.NotInTheseLogs ? finding : finding with { Verdict = Verdict.NotInTheSim };

    public IEnumerable<LogFinding> Findings()
    {
        var radii = new Spread(dollRadii);
        yield return new LogFinding(Section, "The Jagd Dolls' spawn spots", TeaSource.OpeningNotPlayed, "not played",
            radii.Count == 0 ? "no dolls"
                : $"{radii.Describe(" y")} from the centre, on bearings {TeaP1ActorsPullLogs.Tally(dollBearings)}; from the tornadoes' open quarter: "
                  + TeaP1ActorsPullLogs.Tally(dollBearingsFromTheOpenQuarter),
            LogFinding.Unplayed(radii.Count));

        var tethers = new Spread(tetherAfterTargetable);
        yield return new LogFinding(Section, "Whom a doll tethers", TeaSource.OpeningNotPlayed, "not played",
            dollTethers == 0 ? "no doll's tether"
                : $"{tethersToTheFirstToActOnIt} of {dollTethers} tethers go to the first player whose action hit the doll, {tethers.Describe(" s")} after the dolls "
                  + "turn targetable",
            LogFinding.Unplayed(dollTethers))
        {
            Details = tetherDetails,
        };

        var (hit, missed) = (new Spread(exhaustHit), new Spread(exhaustMissed));
        yield return new LogFinding(Section, "Exhaust: when, and how far it reaches", TeaSource.OpeningNotPlayed,
            $"not played; the sheet's reach is {ExhaustReach} y from the doll's centre",
            exhaustAfterTargetable.Count == 0 ? "no Exhaust"
                : $"first {new Spread(exhaustAfterTargetable).Describe(" s")} after the doll turns targetable, the next {new Spread(exhaustAgain).Describe(" s")} later; "
                  + $"hit players stood up to {Spread.F(hit.Max)} y from the doll (n={hit.Count}), missed ones from {Spread.F(missed.Min)} y (n={missed.Count}), by client positions",
            LogFinding.Unplayed(exhaustAfterTargetable.Count));

        var plasm = new Spread(aetheroplasm);
        yield return new LogFinding(Section, "Luminous Aetheroplasm, the status an Exhaust leaves", TeaSource.OpeningNotPlayed, "not played",
            plasm.Count == 0 ? "never gained"
                : $"status {TeaLogIds.LuminousAetheroplasm} for {plasm.Describe(" s")}; {twoExhaustsExploded} of {twoExhausts} players hit by two or more dolls' "
                  + $"Exhausts at once exploded (Exhaust from the player, {TeaLogIds.ExhaustFromAPlayer}); {oneExhaustExploded} of {oneExhaust} hit by one did",
            LogFinding.Unplayed(plasm.Count))
        {
            Details = stacks,
        };
        yield return Unplayed(exploded.Finding(Section, "A player's explosion (Exhaust from the player) kills whoever it hits",
                                               TeaSource.OpeningNotPlayed, "not played"));

        var light = fedHardest.Where(f => f.Hardest < AHeavyHit).ToList();
        var heavy = fedHardest.Where(f => f.Hardest >= AHeavyHit).ToList();
        yield return new LogFinding(Section, "Feeding a doll: its hit points as its tether goes to a boss, and what its Reducible Complexity does",
            TeaSource.OpeningNotPlayed, "not played; a doll fed under a quarter of its hit points is said to be survived",
            feeds.Count == 0 ? "no doll fed"
                : $"{feeds.Count} dolls fed, {new Spread(fedApart).Describe(" y")} from the boss's centre as the tether goes out; Reducible Complexity "
                  + $"{new Spread(fedAfterTether).Describe(" s")} later. "
                  + (light.Count == 0 ? "None hit lightly" : $"With the doll at {Spread.F(light.Min(f => f.Share), "0.0")} to {Spread.F(light.Max(f => f.Share), "0.0")} % "
                                                             + $"its hardest hit was {light.Max(f => f.Hardest)} at the most (n={light.Count})")
                  + (heavy.Count == 0 ? "; none hit hard" : $"; at {Spread.F(heavy.Min(f => f.Share), "0.0")} % and over it was {heavy.Min(f => f.Hardest)} at the least "
                                                            + $"(n={heavy.Count})"),
            LogFinding.Unplayed(feeds.Count))
        {
            Details = feeds,
        };

        yield return Unplayed(dollKilled.Finding(Section, "A doll killed: its Reducible Complexity kills everyone", TeaSource.OpeningNotPlayed, "not played"));

        var speeds = new Spread(dollSpeeds);
        yield return new LogFinding(Section, "The dolls' walking speed", TeaSource.OpeningNotPlayed, "not played", speeds.Describe(" y/s"),
            LogFinding.Unplayed(speeds.Count));

        yield return new LogFinding(Section, "Pressurize: which tornadoes cast it",
            SimSource.Line(TeaSource.State, "var pressurizing = AllBut((TeaP1Tornado)rng.NextInt(3));"), "two of the three, the third rolled evenly",
            pressurizeVolleys == 0 ? "no Pressurize"
                : $"{pressurizesOfTwo} of {pressurizeVolleys} come from two tornadoes; left out: "
                  + string.Join(", ", leftOut.OrderBy(l => l.Key).Select(l => $"{l.Key} x{l.Value}")) + ". Too few to tell the odds",
            pressurizeVolleys == 0 ? Verdict.NotInTheseLogs : pressurizesOfTwo == pressurizeVolleys ? Verdict.Matches : Verdict.Differs)
        {
            Details = pressurizes,
        };

        var appears = new Spread(embolusAppears);
        yield return new LogFinding(Section, "An Embolus appears on its tornado, after Pressurize",
            SimSource.Line(TeaSource.Constants, "float EmbolusAfterPressurize ="), $"{P1Duration.EmbolusAfterPressurize} s, on the tornado's centre",
            appears.Count == 0 ? "no Embolus"
                : $"{appears.Describe(" s")}, {new Spread(embolusOffItsTornado).Describe(" y")} from the tornado's centre (the client's first sight of it, good to "
                  + "a quarter second)",
            LogFinding.Compare(appears, P1Duration.EmbolusAfterPressurize, 0.3));

        var setsOff = new Spread(embolusSetsOff);
        var afterPressurize = new Spread(embolusSetsOffAfterPressurize);
        yield return new LogFinding(Section, "An Embolus sets off, after Pressurize",
            SimSource.Line(TeaSource.Constants, "float EmbolusSetsOffAfterPressurize ="), $"{P1Duration.EmbolusSetsOffAfterPressurize} s",
            setsOff.Count == 0 ? "no Embolus seen to move"
                : $"{afterPressurize.Describe(" s")}; after it appears: {setsOff.Describe(" s")}. Its first move line comes "
                  + $"{new Spread(embolusFirstMoveLine).Describe(" s")} after Pressurize with it already {new Spread(embolusFirstMoveLineOut).Describe(" y")} out, "
                  + "which at its own speed puts the set-off that much earlier",
            LogFinding.Compare(afterPressurize, P1Duration.EmbolusSetsOffAfterPressurize, 0.05));

        var speed = new Spread(embolusSpeeds);
        yield return new LogFinding(Section, "An Embolus's speed, from its first move line to its last",
            SimSource.Line(TeaSource.Constants, "float EmbolusSpeed ="), $"{P1Rules.EmbolusSpeed} y/s", speed.Describe(" y/s"),
            LogFinding.Compare(speed, P1Rules.EmbolusSpeed, 0.1));

        var aim = new Spread(embolusAim);
        yield return new LogFinding(Section, "Where an Embolus heads: its facing off the bearing of the nearer boss's spot as Pressurize is cast",
            SimSource.Line(TeaSource.Mechanics, "sent.Add((centre, Position(roll.AimsAt)));"),
            "0 degrees: one of a pair for where the Liquid stood at the cast, the other for where the Hand stood",
            aim.Count == 0 ? "no Embolus"
                : $"median {Spread.F(aim.Median, "0.0")} degrees ({aim.Describe(" degrees", "0.0")}); in {pairsOneForEachBoss} of {pairs} pairs one heads for each boss (bosses standing together cannot be told apart)",
            aim.Count == 0 ? Verdict.NotInTheseLogs : aim.Median < 3 ? Verdict.Matches : Verdict.Differs);

        var past = new Spread(embolusPastItsSpot);
        yield return new LogFinding(Section, "An Embolus goes on past the spot it left for: how far past it its last move line lies",
            SimSource.Line(TeaSource.Mechanics, "private static Vector3 WhereItLeavesTheArena(Vector3 from, Vector3 through)"),
            "straight on through the spot and out of the arena",
            past.Count == 0 ? "no Embolus seen to stop or to pass its spot"
                : $"{past.Describe(" y")} past the spot (an Embolus still short of its spot as its pull ended is left out)",
            past.Count == 0 ? Verdict.NotInTheseLogs : past.Median > 1 ? Verdict.Matches : Verdict.Differs)
        {
            Details = emboli,
        };

        var stops = new Spread(embolusStopsOut);
        var lasts = new Spread(embolusLasts);
        yield return new LogFinding(Section, "Where an Embolus's walk ends, and when it goes",
            SimSource.Line(TeaSource.Constants, "float EmbolusLeavesBeyond ="), $"{P1Geometry.EmbolusLeavesBeyond} y from the centre, where it is gone",
            stops.Count == 0 ? "no Embolus seen to come to rest"
                : $"its last move line lies {stops.Describe(" y")} from the centre; removed {(lasts.Count == 0 ? "in no pull before its end" : lasts.Describe(" s") + " after appearing")}"
                  + " (how long it lasts is its path's length, and one its pull's end removed reads short)",
            LogFinding.Compare(stops, P1Geometry.EmbolusLeavesBeyond, 0.5));

        const float liquidHitbox = 3.5f, handHitbox = 1.6f;
        var bossTouched = burstFromTheLiquid.Count(d => d <= liquidHitbox + P1Geometry.EmbolusRadius + 0.1)
                          + burstFromTheHand.Count(d => d <= handHitbox + P1Geometry.EmbolusRadius + 0.1);
        yield return new LogFinding(Section, "A touched Embolus bursts: how close the nearest player and boss stood as Outburst went off",
            SimSource.Line(TeaSource.Constants, "float EmbolusRadius ="),
            $"a player or a boss whose hitbox comes within {P1Geometry.EmbolusRadius} y of its centre bursts it: the Living Liquid's centre within "
            + $"{liquidHitbox + P1Geometry.EmbolusRadius} y, the Liquid Hand's within {handHitbox + P1Geometry.EmbolusRadius} y, a player's within {0.5f + P1Geometry.EmbolusRadius} y",
            bursts.Count == 0 ? "no Outburst"
                : $"the nearest living player {new Spread(burstNearestPlayer).Describe(" y")} from the Embolus, the Living Liquid's centre "
                  + $"{new Spread(burstFromTheLiquid).Describe(" y")}, the Liquid Hand's {new Spread(burstFromTheHand).Describe(" y")}",
            bursts.Count == 0 ? Verdict.NotInTheseLogs : bossTouched == bursts.Count ? Verdict.Matches
            : burstNearestPlayer.Any(d => d <= 0.5f + P1Geometry.EmbolusRadius + 1) ? Verdict.Inconclusive : Verdict.Differs)
        {
            Details = bursts,
        };
        yield return outbursts.Finding(Section, "Outburst kills everyone", SimSource.Line(TeaSource.Mechanics, "internal const string EmbolusTouched ="),
                                       "every living player dies");
    }
}
