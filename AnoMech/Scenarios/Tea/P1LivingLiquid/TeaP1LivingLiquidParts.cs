using static AnoMech.Scenarios.Tea.TeaP1Constants;

namespace AnoMech.Scenarios.Tea.P1LivingLiquid;

// The fight's parts, in order. Kept setups hold these as numbers.
public enum TeaP1LivingLiquidPart
{
    Pull = 0,
    Protean1 = 1,
    Cascade2 = 2,
}

// The parts a run plays, First to Last, each on its own clock: Start says when on the run's clock it begins.
public sealed record TeaP1LivingLiquidParts(TeaP1LivingLiquidPart First, TeaP1LivingLiquidPart Last)
{
    // What Start from offers: the fight from the pull.
    public static readonly TeaP1LivingLiquidParts Whole = new(TeaP1LivingLiquidPart.Pull, TeaP1LivingLiquidPart.Cascade2);

    public bool Plays(TeaP1LivingLiquidPart part) => part >= First && part <= Last;

    public float Start(TeaP1LivingLiquidPart part) => FightClock(part) - FightClock(First);

    // Where `part` starts on the fight's clock, which runs from the pull.
    public static float FightClock(TeaP1LivingLiquidPart part) => part switch
    {
        TeaP1LivingLiquidPart.Pull => 0f,
        TeaP1LivingLiquidPart.Cascade2 => P1Duration.CascadeTwoIntoTheTrack,
        _ => P1Duration.ProteanOneIntoTheTrack,
    };

    // What the settings ask for: the whole fight from Start from on, unless a headless run pins both ends.
    public static TeaP1LivingLiquidParts From(TeaP1LivingLiquidStateOverrides overrides)
    {
        var first = overrides.Parts?.First ?? overrides.StartFrom ?? Whole.First;
        var last = overrides.Parts?.Last ?? Whole.Last;
        return new TeaP1LivingLiquidParts(first, last < first ? first : last);
    }
}
