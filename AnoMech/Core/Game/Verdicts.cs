using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.SimObjects;

namespace AnoMech.Core.Game;

// Deaths decided from where everyone stands as hits land, dealt once every check has run: a player caught by more than
// one dies of the first reason found, and godmode reports it once.
public sealed class Verdicts
{
    private readonly List<(SimCharacter Member, uint ActionId, string Why, string? Name)> deaths = [];

    // `name` stands in for the action's own in the death message, for a row the sheet leaves unnamed.
    public void Kill(SimCharacter member, uint actionId, string why, string? name = null)
    {
        if (deaths.All(death => death.Member != member)) deaths.Add((member, actionId, why, name));
    }

    public void Deal()
    {
        foreach (var (member, actionId, why, name) in deaths)
        {
            if (name is null) member.Die(actionId, why);
            else if (member is ISimPartyMember partyMember) Plugin.Game.Kill(partyMember, $"{name} ({why})", actionId);
        }
    }
}
