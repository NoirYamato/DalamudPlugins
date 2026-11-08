using System;
using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.P6Gauntlet.TopP6LimitBreakDuty;

namespace AnoMech.Scenarios.Top.P6Gauntlet;

// The phase's limit breaks 3, in the order they go off.
public enum TopP6LimitBreakDuty
{
    CosmoMemory,
    FirstDiveMelee,
    SecondDiveMelee,
    Comets,
    Meteors,
    MagicNumber1Tank,
    MagicNumber1Healer,
    MagicNumber2Tank,
    MagicNumber2Healer,
    Enrage,
}

// What the human's own tank seat presses, forced; the other tank presses the rest.
public enum TopP6TankLimitBreaks
{
    CosmoMemoryAndMagicNumber1,
    CosmoMemoryAndMagicNumber2,
    MagicNumber1,
    MagicNumber2,
}

// What the human's own melee seat presses after Cosmo Dive 2 and at the enrage, forced; the other melee the rest.
public enum TopP6MeleeLimitBreaks
{
    FirstAndTheEnrage,
    SecondAndTheEnrage,
    First,
    Second,
}

// Which Magic Number the human's own healer seat presses for, forced; the other healer the other.
public enum TopP6HealerLimitBreaks
{
    MagicNumber1,
    MagicNumber2,
}

// Who presses each of the phase's limit breaks 3, by LPDU's sheets. The tank higher on WAR > DRK > PLD > GNB takes Cosmo
// Memory and Magic Number 1, the other Magic Number 2. The melee higher on SAM > DRG > MNK > VPR > RPR > NIN (VPR's place
// UNVERIFIED) takes the first after Cosmo Dive 2 and the enrage's, the other the second. H1 takes Magic Number 1's healer
// limit break, H2 Magic Number 2's; R1 the meteors, R2 the comets. One job on both seats of a pair goes to the human,
// else to the earlier seat. A setting may force the human's own share, and the pair's other seat takes the rest. Each
// limit break pressed with Brilliant Dynamis spends that player's one refill, so every split keeps the two dive melees,
// the two Magic Number tanks and the two Magic Number healers different players.
//
// A human whose job can't press their seat's limit break is held to none of it: the seat counts as its bot's job, and
// the run presses for it.
//
// The one piece of strat the Gauntlet's scenario reads, for the human's melee windows and the start-of-run chat line.
public sealed class TopP6GauntletLimitBreakPlan
{
    private static readonly JobId[] TankPriority = [JobId.Warrior, JobId.DarkKnight, JobId.Paladin, JobId.Gunbreaker];
    private static readonly JobId[] MeleePriority = [JobId.Samurai, JobId.Dragoon, JobId.Monk, JobId.Viper, JobId.Reaper, JobId.Ninja];

    private readonly uint[] jobs;
    private readonly Dictionary<TopP6LimitBreakDuty, PartyRole> seats = [];

    public PartyRole HumanSeat { get; }
    public uint HumanJob { get; }

    // Whether the human's job can press the limit break their seat's duties need.
    public bool HumanFits { get; }

    // `seatJobs` is indexed by PartyRole.
    public TopP6GauntletLimitBreakPlan(IReadOnlyList<uint> seatJobs, PartyRole humanSeat,
        TopP6TankLimitBreaks? tank = null, TopP6MeleeLimitBreaks? melee = null, TopP6HealerLimitBreaks? healer = null)
    {
        HumanSeat = humanSeat;
        HumanJob = seatJobs[(int)humanSeat];
        HumanFits = Fits(humanSeat, HumanJob);
        jobs = PerRole.All.Select(seat => seat != humanSeat || HumanFits ? seatJobs[(int)seat] : BotJob(seat)).ToArray();

        var human = humanSeat;
        var other = PairedWith(humanSeat);

        var (cosmoMemoryTank, otherTank) = HigherFirst(MainTank, OffTank, TankPriority);
        switch (humanSeat is MainTank or OffTank ? tank : null)
        {
            case TopP6TankLimitBreaks.CosmoMemoryAndMagicNumber1: Assign(human, CosmoMemory, MagicNumber1Tank); Assign(other, MagicNumber2Tank); break;
            case TopP6TankLimitBreaks.CosmoMemoryAndMagicNumber2: Assign(human, CosmoMemory, MagicNumber2Tank); Assign(other, MagicNumber1Tank); break;
            case TopP6TankLimitBreaks.MagicNumber1: Assign(human, MagicNumber1Tank); Assign(other, CosmoMemory, MagicNumber2Tank); break;
            case TopP6TankLimitBreaks.MagicNumber2: Assign(human, MagicNumber2Tank); Assign(other, CosmoMemory, MagicNumber1Tank); break;
            default: Assign(cosmoMemoryTank, CosmoMemory, MagicNumber1Tank); Assign(otherTank, MagicNumber2Tank); break;
        }

        var (enrageMelee, otherMelee) = HigherFirst(MeleeDpsA, MeleeDpsB, MeleePriority);
        switch (humanSeat is MeleeDpsA or MeleeDpsB ? melee : null)
        {
            case TopP6MeleeLimitBreaks.FirstAndTheEnrage: Assign(human, FirstDiveMelee, Enrage); Assign(other, SecondDiveMelee); break;
            case TopP6MeleeLimitBreaks.SecondAndTheEnrage: Assign(human, SecondDiveMelee, Enrage); Assign(other, FirstDiveMelee); break;
            case TopP6MeleeLimitBreaks.First: Assign(human, FirstDiveMelee); Assign(other, SecondDiveMelee, Enrage); break;
            case TopP6MeleeLimitBreaks.Second: Assign(human, SecondDiveMelee); Assign(other, FirstDiveMelee, Enrage); break;
            default: Assign(enrageMelee, FirstDiveMelee, Enrage); Assign(otherMelee, SecondDiveMelee); break;
        }

        switch (humanSeat is RegenHealer or ShieldHealer ? healer : null)
        {
            case TopP6HealerLimitBreaks.MagicNumber1: Assign(human, MagicNumber1Healer); Assign(other, MagicNumber2Healer); break;
            case TopP6HealerLimitBreaks.MagicNumber2: Assign(human, MagicNumber2Healer); Assign(other, MagicNumber1Healer); break;
            default: Assign(RegenHealer, MagicNumber1Healer); Assign(ShieldHealer, MagicNumber2Healer); break;
        }

        Assign(PhysRangedDps, Meteors);
        Assign(CasterDps, Comets);
    }

    // The bots' jobs, and the human's as their client has it.
    public static TopP6GauntletLimitBreakPlan For(SimParty party,
        TopP6TankLimitBreaks? tank = null, TopP6MeleeLimitBreaks? melee = null, TopP6HealerLimitBreaks? healer = null)
        => new(PerRole.All.Select(seat => party.Get(seat) switch
        {
            SimPartyNpc bot => (uint)bot.ClassJob,
            SimPlayer => (uint)Natives.BattleCharas.LocalPlayer.ClassJob,
            _ => BotJob(seat),
        }).ToList(), party.PlayerRole, tank, melee, healer);

    public PartyRole SeatOf(TopP6LimitBreakDuty duty) => seats[duty];

    // In the order they go off.
    public IReadOnlyList<TopP6LimitBreakDuty> DutiesOf(PartyRole seat)
        => Enum.GetValues<TopP6LimitBreakDuty>().Where(duty => seats[duty] == seat).ToList();

    // The job whose limit break 3 the seat presses: its own, or its bot's for a human who can't press the seat's.
    public uint JobOf(PartyRole seat) => jobs[(int)seat];

    public bool HumanPresses(TopP6LimitBreakDuty duty) => HumanFits && seats[duty] == HumanSeat;

    // Who has already swapped Brilliant Dynamis for Spark of Dynamis when `duty` is the next to go off: everyone who
    // pressed one before it, but Cosmo Memory's tank, whose press came before Brilliant Dynamis did.
    public IReadOnlyList<PartyRole> SparkedBefore(TopP6LimitBreakDuty duty)
        => Enum.GetValues<TopP6LimitBreakDuty>().Where(d => d < duty && d != CosmoMemory).Select(d => seats[d]).Distinct().ToList();

    // The start-of-run chat line naming the human's duties.
    public string ChatLine
    {
        get
        {
            var duties = Describe(DutiesOf(HumanSeat));
            return HumanFits
                ? $"Your limit breaks: {duties}"
                : $"Your limit breaks: none, since your job can't press your seat's: the Gauntlet presses its {duties} for you";
        }
    }

    private static uint BotJob(PartyRole seat) => PartyPresets.Standard[(int)seat].ClassJob;

    private static bool Fits(PartyRole seat, uint job)
        => TopP6LimitBreaks.KindOf(job) == TopP6LimitBreaks.KindOf(seat) && LimitBreakHandler.ActionId(job, TopP6LimitBreaks.Bars) != 0;

    // The pair's seat whose job ranks higher first; one job on both ranks the human's seat first, else the earlier seat.
    private (PartyRole Higher, PartyRole Lower) HigherFirst(PartyRole first, PartyRole second, JobId[] priority)
    {
        int Rank(PartyRole seat) => Array.IndexOf(priority, (JobId)jobs[(int)seat]) is var rank and >= 0 ? rank : priority.Length;
        var secondFirst = Rank(second) < Rank(first) || (Rank(second) == Rank(first) && second == HumanSeat);
        return secondFirst ? (second, first) : (first, second);
    }

    private static PartyRole PairedWith(PartyRole seat) => seat switch
    {
        MainTank => OffTank,
        OffTank => MainTank,
        RegenHealer => ShieldHealer,
        ShieldHealer => RegenHealer,
        MeleeDpsA => MeleeDpsB,
        MeleeDpsB => MeleeDpsA,
        _ => seat,
    };

    private void Assign(PartyRole seat, params TopP6LimitBreakDuty[] duties)
    {
        foreach (var duty in duties) seats[duty] = seat;
    }

    private static string Describe(IReadOnlyList<TopP6LimitBreakDuty> duties)
    {
        var occasions = string.Join(" and ", duties.Select(Occasion));
        return KindOf(duties[0]) switch
        {
            TopP6LimitBreakKind.Tank => $"tank LB3 on {occasions}",
            TopP6LimitBreakKind.Healer => $"healer LB3 on {occasions}",
            TopP6LimitBreakKind.Melee => $"melee LB3 on Alpha Omega {occasions}",
            TopP6LimitBreakKind.PhysicalRanged => $"physical ranged LB3 {occasions}",
            _ => $"caster LB3 {occasions}",
        };
    }

    private static TopP6LimitBreakKind KindOf(TopP6LimitBreakDuty duty) => duty switch
    {
        CosmoMemory or MagicNumber1Tank or MagicNumber2Tank => TopP6LimitBreakKind.Tank,
        MagicNumber1Healer or MagicNumber2Healer => TopP6LimitBreakKind.Healer,
        FirstDiveMelee or SecondDiveMelee or Enrage => TopP6LimitBreakKind.Melee,
        Meteors => TopP6LimitBreakKind.PhysicalRanged,
        _ => TopP6LimitBreakKind.Caster,
    };

    private static string Occasion(TopP6LimitBreakDuty duty) => duty switch
    {
        CosmoMemory => "Cosmo Memory",
        MagicNumber1Tank or MagicNumber1Healer => "Magic Number 1",
        MagicNumber2Tank or MagicNumber2Healer => "Magic Number 2",
        FirstDiveMelee => "right after Cosmo Dive 2's cast bar",
        SecondDiveMelee => "from the middle once the first one's refill fills the gauge",
        Enrage => "during Run: ****mi*'s cast",
        Meteors => "through both Cosmo Meteors",
        _ => "on the Cosmo Comets",
    };
}
