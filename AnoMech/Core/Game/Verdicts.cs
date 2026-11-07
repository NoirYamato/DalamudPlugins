using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.SimObjects;

namespace AnoMech.Core.Game;

// Deaths decided from where everyone stands as hits land, dealt once every check has run: a player caught by more than
// one dies of the first reason found, and godmode reports it once.
public sealed class Verdicts
{
    private readonly List<(SimCharacter Member, uint ActionId, string Why)> deaths = [];

    public void Kill(SimCharacter member, uint actionId, string why)
    {
        if (deaths.All(death => death.Member != member)) deaths.Add((member, actionId, why));
    }

    public void Deal()
    {
        foreach (var (member, actionId, why) in deaths)
            member.Die(actionId, why);
    }
}
