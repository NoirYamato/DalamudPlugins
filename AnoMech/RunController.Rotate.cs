using AnoMech.Core.Game.Party;

namespace AnoMech;

// The seat a local Start gives the player: the Role pick's, or with Rotate the next one of Seats.
internal sealed partial class RunController
{
    // The Role pick Rotate's seats, from one local run to the next.
    public SeatRotation Seats { get; } = new();

    // A rotating party is asked again for every run started from it, so Auto-restart and Drill's fresh rolls rotate too.
    private IPartyProvider PartyFor(PartyRole? roleOverride, bool rotateSeats, bool solo) => rotateSeats
        ? new RotatingPartyProvider(Seats, solo)
        : new LocalPartyProvider { RoleOverride = roleOverride, Solo = solo };
}
