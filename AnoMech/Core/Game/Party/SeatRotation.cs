using System.Collections.Generic;

namespace AnoMech.Core.Game.Party;

// The Role pick Rotate: tanks alternate between MT and OT and melee between M1 and M2 from one local run to the next,
// starting from the seat Auto gives their job. Healers and ranged keep their job's seat, as Auto does.
public sealed class SeatRotation
{
    private PartyRole? lastTank;
    private PartyRole? lastMelee;

    // The seat the next run gives a player on `job`.
    public PartyRole Next(uint job)
    {
        var auto = PartyPresets.SkipRoleForJob(job);
        return auto switch
        {
            PartyRole.MainTank or PartyRole.OffTank => lastTank is { } tank ? Partner(tank) : auto,
            PartyRole.MeleeDpsA or PartyRole.MeleeDpsB => lastMelee is { } melee ? Partner(melee) : auto,
            _ => auto,
        };
    }

    // A run started with the player in `seat`.
    public void Played(PartyRole seat)
    {
        if (seat.IsTank()) lastTank = seat;
        else if (seat is PartyRole.MeleeDpsA or PartyRole.MeleeDpsB) lastMelee = seat;
    }

    private static PartyRole Partner(PartyRole seat) => seat switch
    {
        PartyRole.MainTank => PartyRole.OffTank,
        PartyRole.OffTank => PartyRole.MainTank,
        PartyRole.MeleeDpsA => PartyRole.MeleeDpsB,
        _ => PartyRole.MeleeDpsA,
    };
}

// Single player in the rotation's next seat, asked again for every run started from it.
public sealed record RotatingPartyProvider(SeatRotation Rotation, bool Solo) : IPartyProvider
{
    public IReadOnlyList<PartySlot?> Roster(uint playerJob)
        => new LocalPartyProvider { RoleOverride = Rotation.Next(playerJob), Solo = Solo }.Roster(playerJob);
}
