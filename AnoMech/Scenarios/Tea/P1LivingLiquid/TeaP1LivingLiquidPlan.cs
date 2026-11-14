using System;
using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Scenarios.Tea.P1LivingLiquid;

// What LPDU asks of each seat where the fight leaves a choice: which tank takes which Drainage tether, and the order
// the two healers cleanse Throttle in.
//
// Each tank takes the tether from the tornado on its own side, the main tank the east one's and the off tank the west
// one's; when the middle tornado tethers, its tether goes to the tank whose side tornado has none. Each healer cleanses
// itself first, then H1 works down the party list from the top and H2 up from the bottom.
//
// A human in a healer's seat on a job without Esuna is held to no cleanse: the run cleanses for the seat.
//
// The one piece of strat the scenario reads, for the start-of-run chat lines naming the human's duties. A human main
// tank's include bringing the Living Liquid back onto the middle: it follows its tank from 4 y, the bots' spots round
// it and beside the tornadoes only fit with it there, and no move of the strat's reaches a human.
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

    // The human sits in a healer's seat on a healer's job.
    public bool HumanCleanses => IsHealer(HumanSeat) && IsHealer(PartyPresets.SkipRoleForJob(HumanJob));

    public IReadOnlyList<PartyRole> CleanseOrder(PartyRole healer)
    {
        var others = healer == ShieldHealer ? Enumerable.Reverse(PartyListFromTheTop) : PartyListFromTheTop;
        return [healer, .. others.Where(seat => seat != healer)];
    }

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
        if (HumanSeat.IsTank() && plays(TeaP1LivingLiquidPart.Protean1))
        {
            var (side, otherTank) = HumanSeat == MainTank ? ("right", "off") : ("left", "main");
            lines.Add($"Your Drainage tether: from the tornado {side} of the open quarter, seen from the middle, or from the middle tornado when that one has none; the {otherTank} tank takes the other. "
                      + "If each of you starts with the other's, keep it on your own spot: a tether's holder can't take the second");
        }
        if (HumanSeat == MainTank && plays(TeaP1LivingLiquidPart.Cascade2))
        {
            lines.Add("Your Living Liquid: it walks after you whenever you are more than 4 y from it"
                      + (plays(TeaP1LivingLiquidPart.Protean1)
                          ? ", so fetching your tether drags it off the middle. After Drainage walk it back onto the middle"
                          : ". Keep it on the middle")
                      + ", and turn it to the open quarter before Protean 2: the party's spots are taken from where it stands");
        }
        if (IsHealer(HumanSeat) && plays(TeaP1LivingLiquidPart.Cascade2))
        {
            lines.Add(HumanCleanses
                ? $"Your Throttle cleanses: yourself first, then {string.Join(", ", CleanseOrder(HumanSeat).Skip(1).Select(SettingsGrid.RoleLabel))}, skipping whoever is clean already"
                : "Your Throttle cleanses: none, since your job has no Esuna: the run cleanses for your seat");
        }
        return lines;
    }

    private static bool IsHealer(PartyRole role) => role is RegenHealer or ShieldHealer;
}
