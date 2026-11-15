using System.Numerics;
using AnoMech.Core.Game;
using static AnoMech.Scenarios.Tea.TeaP1Constants;

namespace AnoMech.Tests;

// Limit Cut, timed from its first Hawk Blaster: the numbers, the blasts and their spots, where Cruise Chaser lands and
// how he aims, what his hits leave, and J Kick.
internal sealed class TeaP1LimitCutPullLogs : ITeaPullLogMeasures
{
    private const string Section = "P1 Limit Cut";
    private const int Rounds = 4;
    private const uint NextTrack = 723;
    private const double Tolerance = 0.25;

    private static readonly string[] Anchors = ["Arm's Length", "Surecast", "Inner Strength"];
    private static readonly uint[] LaterAutoAttacks = [18810, 18811];

    private readonly Dictionary<string, List<double>> offsets = [];
    private readonly List<double> cardinalRadii = [], diagonalRadii = [], middleOff = [], turns = [], numberSpacing = [], firstTwoApart = [], blastHit = [], blastMissed = [];
    private readonly List<double> landsBefore = [], landsBehind = [], offBehind = [], landsFacing = [], turnsBeforeTheSword = [], swordOff = [];
    private readonly List<double> turnsBeforeTheCharge = [], chargesFrom = [], chargeOff = [], swordThrows = [], chargeThrows = [];
    private readonly List<double> liquidToBlaster = [], handAfterLiquid = [], firstAttacks = [], thrownOut = [];
    private readonly List<string> axes = [], numbers = [], landings = [], strikes = [], warps = [], kicks = [], seconds = [], thrown = [];
    private readonly Dictionary<string, int> leaves = [];
    private readonly LethalHits blasts = new();
    private int swords, swordsOnTheirNumber, charges, chargesOnTheirNumber, anchored, laterHits, laterHitsKilled, landingsOnASubstitute;
    private int thrownPastTheRim, diedPastTheRim, hitAgainPastTheRim;
    private readonly List<string> livedPastTheRim = [], notSeenThrown = [];

    // The Knockback sheet's rows his hits name, as distance and speed.
    private static readonly Dictionary<uint, (float Distance, float Speed)> KnockbackRows = new() { [2] = (5f, 10f), [3] = (20f, 40f) };

    // Per action: the row each hit names, or what the hit's target held when it named none.
    private readonly Dictionary<uint, List<string>> throwsNamed = new() { [P1ActionId.AlphaSword] = [], [P1ActionId.SuperBlasstyCharge] = [] };
    private readonly List<string> kickSpots = [];
    private int kicksWithTheNextTrack;

    public void Observe(TeaPull pull)
    {
        var all = pull.AbilitiesAt.Where(a => a.Action == P1ActionId.HawkBlaster).ToList();
        if (all.Count == 0) return;
        var first = all[0].Time;
        void At(string key, DateTimeOffset time)
        {
            if (!offsets.TryGetValue(key, out var list)) offsets[key] = list = [];
            list.Add(PullLog.Seconds(first, time));
        }
        ObserveBlasts(pull, all, At);
        ObserveNumbers(pull, first, At);
        ObserveCruiseChaser(pull, At);
        ObserveTheEnds(pull, first, At);
    }

    private void ObserveBlasts(TeaPull pull, List<ActAbilityAt> all, Action<string, DateTimeOffset> at)
    {
        var (pair, middle, lastAxis) = (0, 0, (double?)null);
        foreach (var round in PullLog.Clusters(all, b => b.Time, 0.3))
        {
            if (round.All(b => Vector2.Distance(b.Position, TeaPull.Centre) < 1f))
            {
                at($"middle {middle++}", round[0].Time);
                middleOff.AddRange(round.Select(b => (double)Vector2.Distance(b.Position, TeaPull.Centre)));
            }
            else
            {
                at($"pair {pair}", round[0].Time);
                var axis = TeaPull.Bearing(round[0].Position) % 180;
                if (pair == 0) axes.Add($"{TeaPull.Compass((int)Math.Round(axis))} and {TeaPull.Compass((int)Math.Round(axis) + 180)}");
                if (lastAxis is { } last) turns.Add((axis - last + 360) % 180);
                lastAxis = axis;
                pair++;
                foreach (var blast in round)
                    (Math.Round(TeaPull.Bearing(blast.Position)) % 90 == 0 ? cardinalRadii : diagonalRadii).Add(Vector2.Distance(blast.Position, TeaPull.Centre));
            }
            foreach (var blast in round)
            {
                var hit = pull.Hits.Where(h => h.Action == P1ActionId.HawkBlaster && h.Source == blast.Source && Math.Abs(PullLog.Seconds(blast.Time, h.Time)) < 0.1
                                               && h.HitSomeone).Select(h => h.Target).ToHashSet();
                foreach (var player in pull.Living(blast.Time))
                    if (pull.Positions.At(player, blast.Time) is { Gap.TotalSeconds: < 0.5 } pose)
                        (hit.Contains(player) ? blastHit : blastMissed).Add(Vector2.Distance(pose.Position, blast.Position));
            }
        }
        blasts.Observe(pull, pull.Hits.Where(h => h.Action == P1ActionId.HawkBlaster), h => $"a Hawk Blaster at {Spread.F(pull.Clock(h.Time))} s");
    }

    // The number a marker shows: the markers run from the first of the eight, whatever the instance adds to their ids.
    private static Dictionary<uint, int> Numbered(TeaPull pull, DateTimeOffset before)
    {
        var markers = pull.HeadMarkers.Where(m => PullLog.Seconds(m.Time, before) is > 0 and < 15).ToList();
        if (markers.Count == 0) return [];
        var lowest = markers.Min(m => m.Id);
        return markers.GroupBy(m => m.Target).ToDictionary(g => g.Key, g => (int)(g.First().Id - lowest) + 1);
    }

    private void ObserveNumbers(TeaPull pull, DateTimeOffset first, Action<string, DateTimeOffset> at)
    {
        var markers = pull.HeadMarkers.Where(m => PullLog.Seconds(m.Time, first) is > 0 and < 5).OrderBy(m => m.Id).ToList();
        if (markers.Count == 0) return;
        at("numbers", markers.Min(m => m.Time));
        var apart = markers.Zip(markers.Skip(1), (a, b) => PullLog.Seconds(a.Time, b.Time)).ToList();
        firstTwoApart.AddRange(apart.Take(1));
        numberSpacing.AddRange(apart.Skip(1));
        var living = pull.Living(markers[0].Time).Count();
        numbers.Add($"{pull.Name}: {markers.Count} numbers over {living} living players, lock-on ids {markers[0].Id} to {markers[^1].Id}, "
                    + $"over {Spread.F(PullLog.Seconds(markers.Min(m => m.Time), markers.Max(m => m.Time)))} s: "
                    + string.Join(", ", Numbered(pull, first).OrderBy(n => n.Value).Select(n => $"{n.Value} {pull.Who(n.Key)}")));
    }

    private void ObserveCruiseChaser(TeaPull pull, Action<string, DateTimeOffset> at)
    {
        var swings = pull.Volleys(P1ActionId.AlphaSword);
        var dashes = pull.Volleys(P1ActionId.SuperBlasstyCharge);
        if (swings.Count == 0) return;
        var chaser = swings[0][0].Source;
        var numbered = Numbered(pull, swings[0][0].Time);
        var placed = pull.Moves.Where(m => m.Actor == chaser && m.Placed).ToList();
        foreach (var hit in pull.Hits.Where(h => h.Source == chaser && h.HitSomeone && h.Action is P1ActionId.AlphaSword or P1ActionId.SuperBlasstyCharge))
        {
            foreach (var status in hit.Applies)
            {
                var seconds = pull.Statuses.Where(s => s.Gained && s.Status == status && s.Target == hit.Target && Math.Abs(PullLog.Seconds(hit.Time, s.Time)) < 0.2)
                                  .Select(s => (float?)s.Duration).FirstOrDefault();
                var left = $"{pull.StatusName(status)} ({status})" + (seconds is { } lasts ? $" for {Spread.F(lasts)} s" : "");
                leaves[left] = leaves.GetValueOrDefault(left) + 1;
            }
            var outcome = pull.Outcome(hit);
            if (outcome is HitOutcome.AlreadyDead) continue;
            var held = pull.Statuses.Where(s => s.Gained && s.Target == hit.Target && Anchors.Contains(pull.StatusName(s.Status)) && s.Time <= hit.Time
                                                && s.Time.AddSeconds(s.Duration) > hit.Time).Select(s => pull.StatusName(s.Status)).FirstOrDefault();
            // A hit that names no throw: under an anchor, or its target dead of the hit itself, or neither.
            throwsNamed[hit.Action].Add(hit.KnockbackRow is { } row ? $"row {row}" + (held is null ? "" : $" under {held}")
                : held is not null ? $"no throw, under {held}"
                : outcome == HitOutcome.Killed ? "no throw, the hit itself killed"
                : outcome == HitOutcome.Invulnerable ? "no throw, under an invulnerability"
                : "no throw, under nothing and alive");
        }
        for (var round = 0; round < Math.Max(swings.Count, dashes.Count); round++)
        {
            ActMove? landing = null;
            if (round < swings.Count)
            {
                var time = swings[round][0].Time;
                at($"sword {round}", time);
                swords++;
                var turned = placed.Where(m => m.Time <= time).Cast<ActMove?>().LastOrDefault();
                landing = turned is { } last
                    ? placed.Where(m => m.Time <= time && PullLog.Seconds(m.Time, time) < 4 && Vector2.Distance(m.Position, last.Position) < 0.05f).Cast<ActMove?>().FirstOrDefault()
                    : null;
                var aimed = Aimed(pull, swings[round][0], turned?.Position);
                var itsNumber = aimed is { } player && numbered.TryGetValue(player.Player, out var n) ? n : 0;
                if (itsNumber == 2 * round + 1) swordsOnTheirNumber++;
                if (aimed is { } a) swordOff.Add(a.Off);
                if (landing is { } landed && turned is { } faced)
                {
                    landsBefore.Add(PullLog.Seconds(landed.Time, time));
                    if (faced.Time > landed.Time) turnsBeforeTheSword.Add(PullLog.Seconds(faced.Time, time));
                    if (pull.Timelines.Where(t => t.Actor == chaser && Math.Abs(PullLog.Seconds(landed.Time, t.Time)) < 0.5).Cast<ActTimeline?>().FirstOrDefault() is { } warp)
                        warps.Add($"timeline {warp.Timeline}");
                    if (aimed is not null && itsNumber != 2 * round + 1) landingsOnASubstitute++;
                    if (aimed is { } odd && itsNumber == 2 * round + 1 && pull.Positions.At(odd.Player, landed.Time) is { } pose)
                    {
                        landsBehind.Add(Vector2.Distance(landed.Position, pose.Position));
                        landsFacing.Add(TeaPull.Degrees(PullLog.AngleBetween(landed.Heading, PullLog.HeadingTo(landed.Position, pose.Position))));
                        if (pose.Heading is { } facing)
                            offBehind.Add(TeaPull.Degrees(PullLog.AngleBetween(PullLog.HeadingTo(pose.Position, landed.Position), facing + Math.PI)));
                        landings.Add($"{pull.Name}, round {round + 1}: he lands {Spread.F(PullLog.Seconds(landed.Time, time))} s before Alpha Sword, "
                                     + $"{Spread.F(Vector2.Distance(landed.Position, pose.Position))} y from number {itsNumber} ({pull.Who(odd.Player)}{(odd.Player == pull.Self ? ", who logged the pull" : "")})"
                                     + (pose.Heading is { } f ? $", {Spread.F(TeaPull.Degrees(PullLog.AngleBetween(PullLog.HeadingTo(pose.Position, landed.Position), f + Math.PI)), "0")} degrees off straight behind it" : ""));
                    }
                }
                Strikes(pull, chaser, swings[round], "Alpha Sword", round, numbered, turned?.Position, swordThrows);
            }
            if (round >= dashes.Count) continue;
            var dash = dashes[round][0].Time;
            at($"charge {round}", dash);
            charges++;
            var aim = placed.Where(m => m.Time <= dash).Cast<ActMove?>().LastOrDefault();
            if (aim is { } turn)
            {
                turnsBeforeTheCharge.Add(PullLog.Seconds(turn.Time, dash));
                if (landing is { } from) chargesFrom.Add(Vector2.Distance(turn.Position, from.Position));
            }
            var even = Aimed(pull, dashes[round][0], aim?.Position);
            if (even is { } e)
            {
                chargeOff.Add(e.Off);
                if (numbered.TryGetValue(e.Player, out var its) && its == 2 * round + 2) chargesOnTheirNumber++;
            }
            Strikes(pull, chaser, dashes[round], "Super Blassty Charge", round, numbered, aim?.Position, chargeThrows);
        }
    }

    // The living player whose bearing from him the hit's direction lies nearest, and how far off it, in degrees.
    private static (uint Player, double Off)? Aimed(TeaPull pull, ActHit hit, Vector2? from)
    {
        if (hit.Rotation is not { } rotation || from is not { } origin) return null;
        return pull.Living(hit.Time).Select(p => (Player: p, At: pull.Where(p, hit.Time))).Where(p => p.At is not null && Vector2.Distance(p.At.Value, origin) > 0.5f)
                   .Select(p => ((uint Player, double Off)?)(p.Player, TeaPull.Degrees(PullLog.AngleBetween(rotation, PullLog.HeadingTo(origin, p.At!.Value)))))
                   .OrderBy(p => p!.Value.Off).FirstOrDefault();
    }

    // Whom one of his hits caught, what it took, and how far it threw them: the farthest along the line from him a
    // player is seen in the second and a half after, or the rim's distance for one the throw killed.
    private void Strikes(TeaPull pull, uint chaser, List<ActHit> volley, string name, int round, Dictionary<uint, int> numbered, Vector2? from, List<double> throws)
    {
        var caught = volley.Where(h => h.HitSomeone).ToList();
        strikes.Add($"{pull.Name} at {Spread.F(pull.Clock(volley[0].Time))} s: {name} {round + 1} catches "
                    + (caught.Count == 0 ? "nobody" : string.Join("; ", caught.Select(h =>
                        $"number {numbered.GetValueOrDefault(h.Target)} ({pull.Who(h.Target)}), {TeaP1HandPullLogs.Damage(pull, h)}"))));
        foreach (var hit in caught)
        {
            var earlier = pull.Hits.Where(h => h.Source == chaser && h.Target == hit.Target && h.Time < hit.Time && h.HitSomeone
                                               && h.Action is P1ActionId.AlphaSword or P1ActionId.SuperBlasstyCharge).Cast<ActHit?>().LastOrDefault();
            var outcome = pull.Outcome(hit);
            if (earlier is { } before && outcome is HitOutcome.Killed or HitOutcome.Survived && !pull.IsDying(hit.Target, hit.Time) && pull.Settled(hit.Time))
            {
                laterHits++;
                if (outcome == HitOutcome.Killed) laterHitsKilled++;
                seconds.Add($"{pull.Name} at {Spread.F(pull.Clock(hit.Time))} s: {pull.Who(hit.Target)} takes {name} {Spread.F(PullLog.Seconds(before.Time, hit.Time))} s "
                            + $"after {pull.ActionName(before.Action)}: {TeaP1HandPullLogs.Damage(pull, hit)}");
            }
            if (outcome != HitOutcome.Survived || from is not { } origin || hit.TargetPosition is not { } stood || Vector2.Distance(stood, origin) < 0.1f) continue;
            var away = Vector2.Normalize(stood - origin);
            var dead = pull.DiedWithin(hit.Target, hit.Time, 2);
            var hitAgain = dead is { } by && pull.Hits.Any(h => h.Target == hit.Target && pull.IsOfTheFight(h.Source) && h.Time > hit.Time && h.Time <= by);
            var rim = ToTheRim(stood - TeaPull.Centre, away);
            var who = $"{pull.Name} at {Spread.F(pull.Clock(hit.Time))} s: {name} on {pull.Who(hit.Target)}";
            // The farthest along the line from him the player is seen over the next second and a half.
            var seen = new[] { 0.3, 0.5, 0.7, 0.9, 1.1, 1.3, 1.5 }.Select(s => pull.Positions.At(hit.Target, hit.Time.AddSeconds(s)))
                                                                  .Where(p => p is { Gap.TotalSeconds: < 0.35 })
                                                                  .Select(p => (double)Vector2.Dot(p!.Value.Position - stood, away)).ToList();
            // Where the row the hit names ends its throw. A player's spot is good to a yalm, so only an end a yalm and
            // more past the rim counts as past it. Two kinds say nothing of the rim: a player something else of the
            // fight's hit before it died, and one that lived on without being seen to slide half the row's length or
            // as far as the rim, whom something the log does not show held where it stood.
            var endsPastTheRim = hit.KnockbackRow is { } named && KnockbackRows.TryGetValue(named, out var sheet)
                                 && (stood - TeaPull.Centre + away * sheet.Distance).Length() > P1Geometry.ArenaRadius + 1f ? sheet.Distance : (float?)null;
            if (endsPastTheRim is not null && hitAgain) hitAgainPastTheRim++;
            else if (endsPastTheRim is { } length && dead is null && (seen.Count == 0 || seen.Max() < Math.Min(length / 2, rim - 0.5)))
                notSeenThrown.Add($"{who}, {Spread.F(Vector2.Distance(stood, TeaPull.Centre))} y from the centre: lived on, seen "
                                  + (seen.Count == 0 ? "nowhere after" : $"no farther than {Spread.F(seen.Max())} y along the line from him"));
            else if (endsPastTheRim is not null)
            {
                thrownPastTheRim++;
                if (dead is not null) diedPastTheRim++;
                else livedPastTheRim.Add($"{who}, {Spread.F(Vector2.Distance(stood, TeaPull.Centre))} y from the centre, lived on");
            }
            // A player the hit's damage left alive who is at 0 HP within 2 s, with no other hit of the fight's between,
            // died of where the hit threw it.
            if (dead is { } died && !hitAgain)
            {
                if (endsPastTheRim is not null) thrownOut.Add(rim);
                thrown.Add($"{who}: left at {pull.HpAfter(hit)} HP, dead {Spread.F(PullLog.Seconds(hit.Time, died))} s later with no other hit; it stood "
                           + $"{Spread.F(Vector2.Distance(stood, TeaPull.Centre))} y from the centre, the rim {Spread.F(rim)} y on along the line from him"
                           + (hit.KnockbackRow is null ? "; its hit names no throw" : ""));
                continue;
            }
            if (seen.Count == 0) continue;
            var held = pull.Statuses.Where(s => s.Gained && s.Target == hit.Target && Anchors.Contains(pull.StatusName(s.Status)) && s.Time <= hit.Time
                                                && s.Time.AddSeconds(s.Duration) > hit.Time).Select(s => pull.StatusName(s.Status)).FirstOrDefault();
            thrown.Add($"{who}{(hit.Target == pull.Self ? ", who logged the pull" : "")}: seen up to {Spread.F(seen.Max())} y along the line from him"
                       + (held is not null ? $", under {held}" : hit.KnockbackRow is null ? "; its hit names no throw, and it held nothing against one" : ""));
            if (held is not null)
            {
                anchored++;
                continue;
            }
            if (hit.KnockbackRow is not null) throws.Add(seen.Max());
        }
    }

    // How far a point inside the arena lies from the rim along a direction.
    private static double ToTheRim(Vector2 local, Vector2 direction)
    {
        var along = Vector2.Dot(local, direction);
        return Math.Sqrt(Math.Max(0, along * along - local.LengthSquared() + P1Geometry.ArenaRadius * P1Geometry.ArenaRadius)) - along;
    }

    private void ObserveTheEnds(TeaPull pull, DateTimeOffset first, Action<string, DateTimeOffset> at)
    {
        var events = TeaP1TimelinePullLogs.Events(pull);
        if (events.TryGetValue("Liquid falls 1", out var fell))
        {
            liquidToBlaster.Add(PullLog.Seconds(fell, first));
            if (pull.Hand is { } hand && pull.Combatants.Where(c => !c.Added && c.Actor == hand && c.Time >= fell).Select(c => (DateTimeOffset?)c.Time).FirstOrDefault() is { } gone)
                handAfterLiquid.Add(PullLog.Seconds(fell, gone));
        }
        foreach (var kick in pull.Volleys(P1ActionId.JKick))
        {
            at("kick", kick[0].Time);
            var chaser = pull.Hits.Where(h => h.Action == P1ActionId.AlphaSword).Select(h => (uint?)h.Source).FirstOrDefault();
            kicks.Add($"{pull.Name}: J Kick from Brute Justice at {(pull.Where(kick[0].Source, kick[0].Time) is { } bj ? TeaPull.Spot(bj) : "an unknown spot")}"
                      + (pull.Positions.At(kick[0].Source, kick[0].Time)?.Heading is { } turned ? $", facing {Spread.F(turned)} rad" : "") + "; Cruise Chaser then at "
                      + (chaser is { } c && pull.Where(c, kick[0].Time) is { } cc ? TeaPull.Spot(cc) : "an unknown spot")
                      + $"; it hits {kick.Count(h => h.HitSomeone)} players and kills {kick.Count(h => h.HitSomeone && pull.Outcome(h) == HitOutcome.Killed)}");
            if (pull.Where(kick[0].Source, kick[0].Time) is { } lands) kickSpots.Add(TeaPull.Spot(lands));
            if (pull.Music.Any(m => Math.Abs(PullLog.Seconds(kick[0].Time, m.Time)) < 0.1 && m.Bgm == NextTrack)) kicksWithTheNextTrack++;
            if (pull.Hits.Where(h => LaterAutoAttacks.Contains(h.Action) && h.Time > kick[0].Time).Select(h => (DateTimeOffset?)h.Time).FirstOrDefault() is { } attack)
                firstAttacks.Add(PullLog.Seconds(first, attack));
        }
    }

    private Spread Offset(string key) => new(offsets.GetValueOrDefault(key) ?? []);

    public IEnumerable<LogFinding> Findings()
    {
        var firstPair = SimSource.EventTime(TeaSource.LimitCut, "() => HawkBlasterPair(0));");
        yield return Series("The Hawk Blaster pairs, after the first", Enumerable.Range(1, 7).Select(k => ($"pair {k}", SimSource.EventTime(TeaSource.LimitCut, $"() => HawkBlasterPair({k}));"))), firstPair.Seconds);
        yield return Series("The Hawk Blasters in the middle, after the first pair",
                            Enumerable.Range(0, 2).Select(k => ($"middle {k}", SimSource.EventTime(TeaSource.LimitCut, "HawkBlasterInTheMiddle);", k + 1))), firstPair.Seconds);
        yield return Series("Alpha Sword on the odd numbers, after the first pair",
                            Enumerable.Range(0, Rounds).Select(k => ($"sword {k}", SimSource.EventTime(TeaSource.LimitCut, "f, AlphaSword);", k + 1))), firstPair.Seconds);
        yield return Series("Super Blassty Charge on the even numbers, after the first pair",
                            Enumerable.Range(0, Rounds).Select(k => ($"charge {k}", SimSource.EventTime(TeaSource.LimitCut, "f, SuperBlasstyCharge);", k + 1))), firstPair.Seconds);
        yield return Series("J Kick, after the first pair", [("kick", SimSource.EventTime(TeaSource.LimitCut, "TeaP1Actions.JKick"))], firstPair.Seconds);
        yield return Series("The numbers show, the first before the first pair", [("numbers", SimSource.EventTime(TeaSource.LimitCut, "() => ShowNumber(number));"))], firstPair.Seconds);

        var (firstTwo, spacing) = (new Spread(firstTwoApart), new Spread(numberSpacing));
        yield return new LogFinding(Section, "The numbers' spacing: the first two, then each later marker after the one before",
            SimSource.Line(TeaSource.Constants, "float NumberSpacing ="), $"the first two together, then {P1Duration.NumberSpacing} s",
            firstTwo.Count == 0 ? "no samples" : $"the first two {firstTwo.Describe(" s", "0.000")} apart; each later one {spacing.Describe(" s", "0.000")} on",
            firstTwo.Count == 0 ? Verdict.NotInTheseLogs : firstTwo.Max > 0.02 ? Verdict.Differs
            : spacing.Count == 0 ? Verdict.Inconclusive : LogFinding.Compare(spacing, P1Duration.NumberSpacing, 0.01))
        {
            Details = numbers,
        };

        var fell = new Spread(liquidToBlaster);
        var kill = P1Duration.CascadeTwoIntoTheTrack + SimSource.PartTime(TeaSource.Scenario, "fight.TheLiquidFalls(start + ").Seconds;
        var blaster = P1Duration.LimitCutIntoTheTrack + firstPair.Seconds;
        yield return new LogFinding(Section, "The first Hawk Blaster, after the Living Liquid falls",
            SimSource.Line(TeaSource.Constants, "float LimitCutIntoTheTrack ="),
            blaster - kill is { } after ? $"{Spread.F(after, "0.0")} s: the fight's clock has the kill {Spread.F(kill!.Value, "0.0")} s in and the blaster {Spread.F(blaster!.Value, "0.0")} s in"
                : "not found in the scenarios",
            fell.Describe(" s"), blaster - kill is { } sim ? LogFinding.Compare(fell, sim, Tolerance) : Verdict.Inconclusive);
        var hand = new Spread(handAfterLiquid);
        yield return new LogFinding(Section, "The Liquid Hand is removed, after the Living Liquid falls",
            SimSource.Line(TeaSource.Constants, "float HandLeavesAfterTheLiquid ="), $"{P1Duration.HandLeavesAfterTheLiquid} s",
            hand.Describe(" s") + (hand.Count == 0 ? "" : " (the Liquid at 0 HP to the client dropping the Hand)"),
            LogFinding.Compare(hand, P1Duration.HandLeavesAfterTheLiquid, 0.5));

        var (cardinals, diagonals) = (new Spread(cardinalRadii), new Spread(diagonalRadii));
        yield return new LogFinding(Section, "A Hawk Blaster's spot on a cardinal: its distance from the centre",
            SimSource.Line(TeaSource.Constants, "float HawkBlasterRadius ="), $"{P1Geometry.HawkBlasterRadius} y", cardinals.Describe(" y"),
            LogFinding.Compare(cardinals, P1Geometry.HawkBlasterRadius, 0.1));
        yield return new LogFinding(Section, "A Hawk Blaster's spot on a diagonal: its distance from the centre",
            SimSource.Line(TeaSource.Constants, "float HawkBlasterRadius ="), $"{P1Geometry.HawkBlasterRadius} y, as on a cardinal", diagonals.Describe(" y"),
            LogFinding.Compare(diagonals, P1Geometry.HawkBlasterRadius, 0.1));
        var middles = new Spread(middleOff);
        yield return new LogFinding(Section, "The middle Hawk Blaster's spot: its distance from the centre",
            SimSource.Line(TeaSource.LimitCut, "private void HawkBlasterInTheMiddle()"), "0 y", middles.Describe(" y"), LogFinding.Compare(middles, 0, 0.1));

        var turn = new Spread(turns);
        yield return new LogFinding(Section, "Each pair stands an eighth of a turn clockwise of the last, the fifth on the first's spots",
            SimSource.Line(TeaSource.LimitCutState, "var bearing = FirstBearing + 45f * (index % 4);"), "45 degrees", turn.Describe(" degrees", "0.0"),
            turn.Count == 0 ? Verdict.NotInTheseLogs : turn.Min > 44 && turn.Max < 46 ? Verdict.Matches : Verdict.Differs);
        yield return new LogFinding(Section, "The first pair's spots",
            SimSource.Line(TeaSource.LimitCutState, "var axis = (TeaP1FirstBlasters)rng.NextInt(4);"), "one of four axes, each as likely",
            axes.Count == 0 ? "no Limit Cut" : TeaP1ActorsPullLogs.Tally(axes) + ". Too few to tell the odds",
            axes.Count == 0 ? Verdict.NotInTheseLogs : Verdict.Inconclusive);

        yield return blasts.Finding(Section, "A Hawk Blaster kills whoever it hits", SimSource.Line(TeaSource.Actions, "public static readonly EnemyAction HawkBlaster ="),
                                    "every hit kills");
        var (hit, missed) = (new Spread(blastHit), new Spread(blastMissed));
        yield return new LogFinding(Section, "Hawk Blaster's reach: how far from a blast's centre the players it hit and missed stood",
            SimSource.Line(TeaSource.Constants, "uint HawkBlaster ="), "10 y, the sheet's",
            hit.Count + missed.Count == 0 ? "no blast" : $"hit: {(hit.Count == 0 ? "nobody" : $"up to {Spread.F(hit.Max)} y (n={hit.Count})")}; "
                                                        + $"missed: {(missed.Count == 0 ? "nobody" : $"from {Spread.F(missed.Min)} y (n={missed.Count})")}. Client positions",
            hit.Count + missed.Count == 0 ? Verdict.NotInTheseLogs
            : hit.Count > 0 && hit.Max > 12 || missed.Count > 0 && missed.Min < 8 ? Verdict.Differs : Verdict.Inconclusive);

        var lands = new Spread(landsBefore);
        yield return new LogFinding(Section, "Cruise Chaser lands, before each Alpha Sword",
            SimSource.Line(TeaSource.Constants, "float CruiseChaserLandsBeforeAlphaSword ="), $"{P1Duration.CruiseChaserLandsBeforeAlphaSword} s", lands.Describe(" s"),
            LogFinding.Compare(lands, P1Duration.CruiseChaserLandsBeforeAlphaSword, 0.1))
        {
            Details = landings,
        };
        var behind = new Spread(landsBehind);
        var straight = new Spread(offBehind);
        yield return new LogFinding(Section, "How far from its odd number he lands, and how far off straight behind it",
            SimSource.Line(TeaSource.Constants, "float CruiseChaserLandsBehind ="), $"{P1Geometry.CruiseChaserLandsBehind} y, straight behind by the player's facing",
            behind.Count == 0 ? "no landing with its number's position"
                : $"{behind.Describe(" y")} from the player (median {Spread.F(behind.Median)}), a median {Spread.F(straight.Median, "0")} degrees off the reverse of the "
                  + $"player's facing ({straight.Describe(" degrees", "0")}): client positions of a player on the run, and the facing its last line carried. "
                  + $"{landingsOnASubstitute} landings on a stand-in for a dead number are left out",
            behind.Count == 0 ? Verdict.NotInTheseLogs
            : behind.Median > P1Geometry.CruiseChaserLandsBehind + 2 || behind.Median < P1Geometry.CruiseChaserLandsBehind - 2 ? Verdict.Differs : Verdict.Inconclusive);
        var facing = new Spread(landsFacing);
        yield return new LogFinding(Section, "He lands turned to his odd number: his facing off the player's bearing",
            SimSource.Line(TeaSource.LimitCut, "cruiseChaser.SetPosition(target.Placement().MoveForward(-state.CruiseChaserLandsBehind));"),
            "0 degrees: he takes the player's own facing from straight behind it", facing.Describe(" degrees", "0.0"),
            facing.Count == 0 ? Verdict.NotInTheseLogs : facing.Max < 15 ? Verdict.Matches : Verdict.Inconclusive);
        yield return new LogFinding(Section, "A warp-in shows him at every landing",
            SimSource.Line(TeaSource.LimitCut, "cruiseChaser.PlayActionTimeline(ActionTimelineId.WarpEnd);"), $"timeline {ActionTimelineId.WarpEnd} at each landing",
            warps.Count == 0 ? "no timeline logged at a landing" : $"{TeaP1ActorsPullLogs.Tally(warps)} over {landsBefore.Count} landings",
            warps.Count == 0 ? Verdict.NotInTheseLogs
            : warps.Count == landsBefore.Count && warps.All(w => w == $"timeline {ActionTimelineId.WarpEnd}") ? Verdict.Matches : Verdict.Differs);

        var again = new Spread(turnsBeforeTheSword);
        var swordAim = new Spread(swordOff);
        yield return new LogFinding(Section, "He turns to his odd number again before the cleave, and cleaves at it",
            SimSource.Line(TeaSource.LimitCut, "private void FaceWhomHeLandedBehind() => Face(landedBehind);"),
            "turned again 0.1 s before the cleave and as it goes off; the cleave goes at the player where it stands then",
            swords == 0 ? "no Alpha Sword"
                : $"placed again {again.Describe(" s", "0.000")} before the cleave; the cleave {swordAim.Describe(" degrees", "0.0")} off the nearest player's bearing; "
                  + $"{swordsOnTheirNumber} of {swords} cleaves are aimed at their round's odd number",
            swords == 0 ? Verdict.NotInTheseLogs : swordsOnTheirNumber == swords && again.Count > 0 ? Verdict.Matches : Verdict.Inconclusive);

        var chargeTurn = new Spread(turnsBeforeTheCharge);
        var stays = new Spread(chargesFrom);
        var chargeAim = new Spread(chargeOff);
        yield return new LogFinding(Section, "He charges from where he cleaved, turned to the even number",
            SimSource.Line(TeaSource.LimitCut, "He charges from where he cleaved and stays there."),
            "from the same spot, turned to the even number 0.1 s before the charge and as it goes off",
            charges == 0 ? "no Super Blassty Charge"
                : $"{stays.Describe(" y")} from where he landed; placed again {chargeTurn.Describe(" s", "0.000")} before the charge; the charge "
                  + $"{chargeAim.Describe(" degrees", "0.0")} off the nearest player's bearing; {chargesOnTheirNumber} of {charges} charges are aimed at their round's even number",
            charges == 0 ? Verdict.NotInTheseLogs : stays.Count > 0 && stays.Max < 0.1 ? Verdict.Matches : Verdict.Differs)
        {
            Details = strikes,
        };

        var simLeaves = new[]
        {
            $"({P1StatusId.MagicVulnerabilityUp}) for {Spread.F(P1Duration.MagicVulnerabilityUp)} s", $"({P1StatusId.PhysicalVulnerabilityUp}) for {Spread.F(P1Duration.PhysicalVulnerabilityUp)} s",
        };
        yield return new LogFinding(Section, "What a hit of his leaves: the statuses its own effects name, with the seconds their gain lines give",
            SimSource.Line(TeaSource.Constants, "ushort PhysicalVulnerabilityUp ="),
            $"status {P1StatusId.MagicVulnerabilityUp} for {P1Duration.MagicVulnerabilityUp} s and status {P1StatusId.PhysicalVulnerabilityUp} for {P1Duration.PhysicalVulnerabilityUp} s",
            leaves.Count == 0 ? "no status from him" : string.Join("; ", leaves.OrderByDescending(l => l.Value).Select(l => $"{l.Key} x{l.Value}")),
            leaves.Count == 0 ? Verdict.NotInTheseLogs
            : simLeaves.All(sim => leaves.Where(l => l.Key.Contains(sim, StringComparison.Ordinal)).Sum(l => l.Value) * 2 > leaves.Values.Sum() / 2) ? Verdict.Matches
            : Verdict.Differs);

        yield return new LogFinding(Section, "A second hit of his kills",
            SimSource.Line(TeaSource.Actions, "public const string SecondCruiseChaserHit ="), "a hit on a player who carries the vulnerability kills",
            laterHits == 0 ? "nobody took two of his hits" : $"{laterHitsKilled} of {laterHits} players hit again died of it",
            laterHits == 0 ? Verdict.NotInTheseLogs : laterHitsKilled == laterHits ? Verdict.Matches : Verdict.Differs)
        {
            Details = seconds,
        };

        var (swordThrown, chargeThrown) = (new Spread(swordThrows), new Spread(chargeThrows));
        yield return Throw("Alpha Sword", P1ActionId.AlphaSword, "float AlphaSwordKnockback =", P1Rules.AlphaSwordKnockback, P1Rules.AlphaSwordKnockbackSpeed, swordThrown) with
        {
            Details = thrown,
        };
        yield return Throw("Super Blassty Charge", P1ActionId.SuperBlasstyCharge, "float SuperBlasstyChargeKnockback =", P1Rules.SuperBlasstyChargeKnockback,
                           P1Rules.SuperBlasstyChargeKnockbackSpeed, chargeThrown);

        var atTheRim = new Spread(thrownOut);
        yield return new LogFinding(Section, "The rim: a player one of his hits throws past it",
            SimSource.Line(TeaSource.Zone, "world.EnforceArenaBoundary("), $"no wall: a throw goes its full length, and whoever ends beyond {P1Geometry.ArenaRadius} y dies",
            thrownPastTheRim == 0 ? "no player a hit left alive was seen thrown by a row that ends past the rim"
                                    + (notSeenThrown.Count == 0 ? "" : $"; {notSeenThrown.Count} whose hit named such a row lived on and were not seen to slide")
                : $"{diedPastTheRim} of {thrownPastTheRim} players a hit left alive, whose throw by the row it names ends a yalm and more past the rim, were dead within "
                  + "2 s with no other hit of the fight's" + (diedPastTheRim == 0 ? "" : $"; the rim lay {atTheRim.Describe(" y")} on along the line from him")
                  + (hitAgainPastTheRim == 0 ? "" : $"; {hitAgainPastTheRim} more, hit again before they died, are left out")
                  + (notSeenThrown.Count == 0 ? "" : $"; {notSeenThrown.Count} more lived on and were not seen to slide: something held them that the log does not show"),
            thrownPastTheRim == 0 ? Verdict.NotInTheseLogs : diedPastTheRim == thrownPastTheRim ? Verdict.Matches : Verdict.Differs)
        {
            Details = [.. livedPastTheRim, .. notSeenThrown],
        };

        var attacks = new Spread(firstAttacks);
        yield return new LogFinding(Section, "J Kick: where Brute Justice casts it from",
            SimSource.Line(TeaSource.LimitCut, "private static readonly Vector3 BruteJusticeLandsAt ="), "(-6.00, 0.00), from an unseen helper under his name",
            kicks.Count == 0 ? "no J Kick" : TeaP1ActorsPullLogs.Tally(kickSpots),
            kicks.Count == 0 ? Verdict.NotInTheseLogs : kickSpots.All(s => s == "(-6.00, 0.00)") ? Verdict.Matches : Verdict.Differs)
        {
            Details = kicks,
        };
        yield return new LogFinding(Section, "J Kick: the next phase's track, and the bosses' first auto-attacks",
            SimSource.Line(TeaSource.Constants, "ushort Bgm ="), "not played: the run ends with J Kick",
            kicks.Count == 0 ? "no J Kick"
                : $"track {NextTrack} is set with {kicksWithTheNextTrack} of {kicks.Count} J Kicks; the bosses' first auto-attacks come {attacks.Describe(" s")} after the first pair",
            LogFinding.Unplayed(kicks.Count));
    }

    // The Knockback sheet's row an action's hits name, against the sim's push.
    private LogFinding Throw(string name, uint action, string constant, float distance, float speed, Spread seen)
    {
        var named = throwsNamed[action];
        var rows = named.Where(n => n.StartsWith("row ", StringComparison.Ordinal)).Select(n => uint.Parse(n.Split(' ')[1])).Distinct().ToList();
        var asTheSim = rows.Count == 1 && KnockbackRows.TryGetValue(rows[0], out var row) && row.Distance == distance && row.Speed == speed;
        return new LogFinding(Section, $"{name}'s knockback: the Knockback sheet's row its hits name",
            SimSource.Line(TeaSource.Constants, constant), $"{distance} y at {speed} y/s straight away from him, held off by an anti-knockback",
            named.Count == 0 ? "no hit on a living player"
                : TeaP1ActorsPullLogs.Tally(named)
                  + (rows.Count == 1 && KnockbackRows.TryGetValue(rows[0], out var sheet) ? $": row {rows[0]} is {sheet.Distance} y at {sheet.Speed} y/s" : "")
                  + (seen.Count == 0 ? "" : $". The farthest along the line from him a thrown player is seen in the 1.5 s after: median {Spread.F(seen.Median)} y "
                                            + $"({seen.Describe(" y")}; client positions, and a walk adds to it)"),
            named.Count == 0 ? Verdict.NotInTheseLogs : rows.Count == 0 ? Verdict.Inconclusive : asTheSim ? Verdict.Matches : Verdict.Differs);
    }

    // The events of a kind against their literals, each as its time after the first pair's.
    // Every row's line is named, the first with its file, so a literal the scenario no longer holds shows as ":?".
    internal LogFinding Series(string name, IEnumerable<(string Key, (string Where, float? Seconds) Sim)> rows, float? anchor)
    {
        var list = rows.ToList();
        var sims = list.Select(r => r.Sim.Seconds - anchor).ToList();
        var measured = list.Select(r => Offset(r.Key)).ToList();
        var worst = list.Select((_, i) => measured[i].Count == 0 || sims[i] is not { } sim ? 0 : Math.Abs(measured[i].Mean - sim)).DefaultIfEmpty(0).Max();
        var lines = list[0].Sim.Where + string.Concat(list.Skip(1).Select(r => ", " + r.Sim.Where[r.Sim.Where.LastIndexOf(':')..]));
        return new LogFinding(Section, name, lines, string.Join(", ", sims.Select(s => s is { } value ? Spread.F(value) : "not found in the scenario")) + " s",
            measured.All(m => m.Count == 0) ? "no samples"
                : string.Join(", ", measured.Select(m => m.Count == 0 ? "none" : m.Count == 1 ? Spread.F(m.Mean) : $"{Spread.F(m.Mean)} ({Spread.F(m.Min)} to {Spread.F(m.Max)})"))
                  + $" s, over {measured.Max(m => m.Count)} pulls",
            measured.All(m => m.Count == 0) ? Verdict.NotInTheseLogs : sims.Any(s => s is null) ? Verdict.Inconclusive : worst <= Tolerance ? Verdict.Matches : Verdict.Differs);
    }
}
