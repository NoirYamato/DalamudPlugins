using System;
using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Scenarios.Tea.P1LivingLiquid;

// What LPDU asks of each seat where the fight leaves a choice: which tank holds which boss through the pull, which
// damage dealer takes which Jagd Doll, which tank takes which Drainage tether, and the order the two healers cleanse
// Throttle in.
//
// At the pull the main tank holds both bosses but for the first hand: the off tank takes the Liquid Hand away as it
// splits off, the main tank Provokes it once it shows its palm and the off tank Provokes it back after the busters.
// Each damage dealer takes the doll of its own quarter, with the open quarter north: R1 north-east, M2 south-east, M1
// south-west, R2 north-west. A human in a tank's seat on a job without Provoke is held to none of a tank's presses: the
// run presses them for the seat.
//
// Each tank takes the tether from the tornado on its own side, the main tank the east one's and the off tank the west
// one's; when the middle tornado tethers, its tether goes to the tank whose side tornado has none. Each healer cleanses
// itself first, then H1 works down the party list from the top and H2 up from the bottom.
//
// A human in a healer's seat on a job without Esuna is held to no cleanse: the run cleanses for the seat.
//
// The one piece of strat the scenario reads, for the start-of-run chat lines naming the human's duties. A human
// tank's include where it takes its boss, since no move of the strat's reaches a human and each boss walks after a
// tank out of its reach, the Living Liquid to 6 y from it and the Liquid Hand to 4 y: the main tank turns the Liquid
// for both Protean Waves and brings it back onto the middle, where the bots' spots round it and beside the tornadoes
// fit; for the last hand each tank walks on through the middle, since a tank that stops on it leaves its boss short of
// the other.
public sealed class TeaP1LivingLiquidPlan
{
    // UNVERIFIED: that the client's party list shows the seats in this order; it may sort each role by job.
    private static readonly PartyRole[] PartyListFromTheTop =
        [MainTank, OffTank, RegenHealer, ShieldHealer, MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps];

    public PartyRole HumanSeat { get; }
    public uint HumanJob { get; }

    public TeaP1LivingLiquidPlan(PartyRole humanSeat, uint humanJob)
    {
        HumanSeat = humanSeat;
        HumanJob = humanJob;
    }

    // The human's job as their client has it.
    public static TeaP1LivingLiquidPlan For(SimParty party)
        => new(party.PlayerRole, party.Player is null ? 0u : Natives.BattleCharas.LocalPlayer.ClassJob);

    // The human sits in a tank's seat on a tank's job.
    public bool HumanTanks => HumanSeat.IsTank() && PartyPresets.SkipRoleForJob(HumanJob).IsTank();

    // The human sits in a healer's seat on a healer's job.
    public bool HumanCleanses => IsHealer(HumanSeat) && IsHealer(PartyPresets.SkipRoleForJob(HumanJob));

    public IReadOnlyList<PartyRole> CleanseOrder(PartyRole healer)
    {
        var others = healer == ShieldHealer ? Enumerable.Reverse(PartyListFromTheTop) : PartyListFromTheTop;
        return [healer, .. others.Where(seat => seat != healer)];
    }

    // The quarter, in the frame the tornadoes name, whose Jagd Doll `seat` takes.
    public TeaP1Quarter? DollOf(PartyRole seat) => seat switch
    {
        PhysRangedDps => TeaP1Quarter.Northeast,
        MeleeDpsB => TeaP1Quarter.Southeast,
        MeleeDpsA => TeaP1Quarter.Southwest,
        CasterDps => TeaP1Quarter.Northwest,
        _ => null,
    };

    // The tornado whose tether `tank` takes, of the two that tether.
    public TeaP1Tornado? TetherOf(PartyRole tank, IReadOnlyCollection<TeaP1Tornado> tethering)
    {
        if (!tank.IsTank()) return null;
        var itsSide = tank == MainTank ? TeaP1Tornado.East : TeaP1Tornado.West;
        if (tethering.Contains(itsSide)) return itsSide;
        return tethering.Contains(TeaP1Tornado.Middle) ? TeaP1Tornado.Middle : null;
    }

    // The start-of-run chat lines naming the human's duties in the parts the run plays; none when it has none.
    public IReadOnlyList<string> ChatLines(Func<TeaP1LivingLiquidPart, bool> plays)
    {
        var lines = new List<string>();
        if (plays(TeaP1LivingLiquidPart.Pull)) lines.AddRange(ThePullsLines());
        if (HumanSeat.IsTank() && plays(TeaP1LivingLiquidPart.Protean1))
        {
            var (side, otherTank) = HumanSeat == MainTank ? ("right", "off") : ("left", "main");
            lines.Add($"Your Drainage tether: from the tornado {side} of the open quarter, seen from the middle, or from the middle tornado when that one has none; the {otherTank} tank takes the other. "
                      + "If each of you starts with the other's, keep it on your own spot: a tether's holder can't take the second");
        }
        if (HumanSeat == MainTank && plays(TeaP1LivingLiquidPart.Protean1))
        {
            lines.Add("Your Living Liquid in Protean 1: it faces you until its Protean Wave's bar shows, and its own wave goes the way it faces then. "
                      + "Stand on its side away from the open quarter before the bar, then step out of that line");
        }
        if (HumanSeat == MainTank && plays(TeaP1LivingLiquidPart.Cascade2))
        {
            lines.Add("Your Living Liquid: it stays put while you are within 7 y of it, and beyond that walks after you until it is 6 y from you"
                      + (plays(TeaP1LivingLiquidPart.Protean1)
                          ? ", so fetching your tether can drag it off the middle. After Drainage lead it back: stand 6 y past the middle on the far side from it"
                          : ". Keep it on the middle")
                      + ", and turn it to the open quarter 2 s or more before Protean 2's bar shows: the party stacks on the line it stands on then");
        }
        if (HumanSeat.IsTank() && plays(TeaP1LivingLiquidPart.Cascade2))
        {
            lines.Add(HumanSeat == MainTank
                ? "Your last hand: hold the Living Liquid more than 17 y from the Liquid Hand until the Hand shows its palm, "
                  + "then walk on through the middle until the Living Liquid stands on the Hand: it stops 6 y behind you"
                : "Your Liquid Hand: it stays put while you are within 5 y of it, and beyond that walks after you until it is 4 y from you. "
                  + "For the last hand hold it more than 17 y from the Living Liquid until it shows its palm, "
                  + "then walk on through the middle until it stands on the Living Liquid");
        }
        if (IsHealer(HumanSeat) && plays(TeaP1LivingLiquidPart.Cascade2))
        {
            lines.Add(HumanCleanses
                ? $"Your Throttle cleanses: yourself first, then {string.Join(", ", CleanseOrder(HumanSeat).Skip(1).Select(SettingsGrid.RoleLabel))}, skipping whoever is clean already"
                : "Your Throttle cleanses: none, since your job has no Esuna: the run cleanses for your seat");
        }
        return lines;
    }

    // Where a tank takes its boss is its seat's to do whatever its job; its presses are its job's.
    private IEnumerable<string> ThePullsLines()
    {
        if (HumanSeat == MainTank)
        {
            yield return "Your pull: the Living Liquid comes to 6 y from you and cleaves toward you at 10 s, so turn it away from the party. "
                         + "Be within 6 y of the middle when Cascade lands at 18 s: the tornadoes appear a moment later, on any three sides";
            yield return "Your first hand: take the Living Liquid to the rim beside the middle tornado, on the side of the tornado left of the open quarter, and be the middle tornado's closest player. "
                         + (HumanTanks
                             ? "Provoke the Liquid Hand as it shows its palm, at about 29 to 31 s: it sets off a moment after your Provoke and must reach the Living Liquid by 34 s. "
                             : "")
                         + $"That tornado's telegraphed wave throws you past the rim: step out of it and back, or hold your ground with {(HumanTanks ? "Arm's Length" : "your anti-knockback")}, and be its closest player again by 37 s"
                         + (HumanTanks
                             ? ". Your invulnerability between 28 and 37 s: both busters at 36 s leave Water Resistance Down, and the tornado's unseen wave on it kills at 37.5 s"
                             : "");
            yield return "Your Living Liquid from 42 s: lead it onto the middle only then, once the melee have fed their dolls: "
                         + "until the Jagd Dolls' first Exhaust at 38 s the way in is inside two dolls' reach, a doll the Living Liquid passes is fed to it, "
                         + "and at 40 s an Embolus sets its course for where the Living Liquid stands. "
                         + "Walk through the middle to 6 y past it, so that it stops on the middle, then stand within 4 y of it, "
                         + "between the middle tornado and the one right of the open quarter, for the next busters at 55 s: "
                         + "an Embolus from the tornado right of the open quarter passes 7 y from the middle on that side at about 52 s";
            if (!HumanTanks)
                yield return "Your Provoke and invulnerability: none, since your job has none: the run presses them for your seat. "
                             + "It Provokes the Liquid Hand at 30 s if it shows a palm, which it does with the Living Liquid at the rim, "
                             + "or as soon after as the Hand stands within Provoke's 25 y of you, and turns you invulnerable at 34 s";
        }
        if (HumanSeat == OffTank)
        {
            yield return "Your Liquid Hand at the pull: as it splits off, take it far out on the right-hand side of the open quarter, more than 17 y from the Living Liquid, until it shows its palm; the main tank Provokes it then, "
                         + "from the rim beside the middle tornado, and Provoke reaches 25 y: keep the Hand within about 12 y of the middle. "
                         + "Once it has left you, wait beside the Living Liquid on the side of the tornado left of the open quarter, not near the middle: "
                         + "until the Jagd Dolls' first Exhaust at 38 s the middle is inside two dolls' reach, and two Exhausts at once kill";
            yield return "Your Liquid Hand from 42 s: "
                         + (HumanTanks
                             ? "Provoke it back after the busters at 36 s and before the next at 55 s, and lead it onto the middle only from 42 s"
                             : "the run Provokes it back for your seat at 38 s, since your job has no Provoke; lead it onto the middle only from 42 s")
                         + ", once the melee have fed their dolls: walk through the middle to 4 y past it, so that the Hand stops on the middle, "
                         + "then stand between the middle tornado and the one left of the open quarter";
        }
        if (DollOf(HumanSeat) is { } quarter)
        {
            var where = quarter switch
            {
                TeaP1Quarter.Northeast => "north-east",
                TeaP1Quarter.Southeast => "south-east",
                TeaP1Quarter.Southwest => "south-west",
                _ => "north-west",
            };
            yield return $"Your Jagd Doll: the one {where} of the middle with the open quarter north. Hit it first, with a weaponskill or a spell, since no auto-attack is heard here, "
                         + "and stand where only its Exhaust reaches you: each reaches 8.8 y, and two at once kill. "
                         + (HumanSeat == MeleeDpsA ? "Hit it by 38 s: from 42 s the tanks lead the bosses past it to the middle, which feeds it. " : "")
                         + "Keep it more than 5 y from both bosses until it is under a quarter of its health: above a quarter, feeding it kills everyone. "
                         + "Then draw it through a boss: stand on the boss's far side from it, clear of the tornadoes' water, or feed it once the bosses are on the middle";
            yield return HumanSeat is PhysRangedDps or CasterDps
                ? $"Your tornado: the one {(HumanSeat == PhysRangedDps ? "right" : "left")} of the open quarter. Be its closest player for both of its waves, from the open quarter's side of it, at the rim, so that both run out of the arena along the rim: "
                  + "baited from its inner side they cross the arena, and whoever both hit dies. Step out of the telegraphed one and back"
                : "Your spot beside it: on your doll or a step away from the tornado beside it, not toward it: that tornado's waves go at its closest player, who should be the one baiting it";
        }
        if (IsHealer(HumanSeat))
        {
            var farOut = HumanSeat == RegenHealer ? "in the open quarter" : "between the middle tornado and the one right of the open quarter";
            yield return $"Your wait for the Jagd Dolls: stay far out, {farOut}, until their first Exhaust at 38 s: until then the middle is inside two dolls' reach, and two Exhausts at once kill. "
                         + "Then come in on the open quarter's side of the middle: the bosses stand on it from 46 s, and their busters at 55 s cleave toward the tanks, on the middle tornado's side";
        }
    }

    private static bool IsHealer(PartyRole role) => role is RegenHealer or ShieldHealer;
}
