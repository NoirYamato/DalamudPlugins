using System;
using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.Top.TopP3Constants;

namespace AnoMech.Scenarios.Top.P3HelloWorld;

// A pair's job in one set, in the order each pair moves through them: a set's defamation pair
// takes the remote tether next, then the stack, then the local tether.
public enum HelloWorldRole { Defamation, Remote, Stack, Local }

public sealed record HelloWorldColour(
    int Index,
    ushort RotSmellStatusId,
    ushort RotStatusId,
    ushort TowerDebuffStatusId,
    ushort DebuggerStatusId,
    uint TowerActionId,
    uint TowerUnsoakedActionId,
    uint TowerDebuffExpiredActionId)
{
    public static readonly HelloWorldColour Red = new(0, P3StatusId.UnderflowCodeSmell, P3StatusId.RedRot,
        P3StatusId.RedTowerDebuff, P3StatusId.UnderflowDebugger, P3ActionId.RedTower, P3ActionId.RedTowerUnsoaked,
        P3ActionId.RedTowerStatusExpired);

    public static readonly HelloWorldColour Blue = new(1, P3StatusId.PerformanceCodeSmell, P3StatusId.BlueRot,
        P3StatusId.BlueTowerDebuff, P3StatusId.PerformanceDebugger, P3ActionId.BlueTower, P3ActionId.BlueTowerUnsoaked,
        P3ActionId.BlueTowerStatusExpired);

    public static readonly IReadOnlyList<HelloWorldColour> Both = [Red, Blue];

    public HelloWorldColour Other => Index == Red.Index ? Blue : Red;
}

public sealed class TopP3HelloWorldState
{
    public const int Sets = 4;

    // Pairs by kit: [0,1] defamation, [2,3] stack, [4,5] remote first, [6,7] local first.
    public RoleList Kits { get; }

    // The defamation pair's rot colour, which is also the colour of the towers it soaks.
    public HelloWorldColour DefamationColour { get; }
    public HelloWorldColour StackColour => DefamationColour.Other;

    // Per set: the intercardinal between the two defamation-colour towers.
    public IReadOnlyList<Direction> Layouts { get; }

    public TopP3HelloWorldState(Rng rng, SimParty party, TopP3HelloWorldStateOverrides overrides)
    {
        var kits = new RoleListBuilder
        {
            Slots = overrides.Kit.Resolve(party.PlayerRole)
                             .ToDictionary(r => r.Role, r => new[] { 2 * (int)r.Value, 2 * (int)r.Value + 1 }),
        }.Build(rng, party);
        Kits = overrides.KitOrder is { } order ? new RoleList(party, order) : kits;

        var colour = rng.NextObj(HelloWorldColour.Red, HelloWorldColour.Blue);
        DefamationColour = overrides.DefamationColour switch
        {
            RotColour.Red => HelloWorldColour.Red,
            RotColour.Blue => HelloWorldColour.Blue,
            _ => colour,
        };

        var layouts = Enumerable.Range(0, Sets).Select(_ => rng.NextIntercardinal()).ToList();
        if (overrides.SameLayoutEverySet == true) layouts = Enumerable.Repeat(layouts[0], Sets).ToList();
        Layouts = overrides.Layouts ?? layouts;
    }

    public Direction Layout(int set) => Layouts[set - 1];

    public HelloWorldKit KitOf(PartyRole role) => (HelloWorldKit)(Array.IndexOf(Kits.List, role) / 2);

    public IReadOnlyList<PartyRole> WithKit(HelloWorldKit kit) => [Kits[2 * (int)kit], Kits[2 * (int)kit + 1]];

    public HelloWorldRole RoleIn(int set, PartyRole role) => (HelloWorldRole)(((int)FirstRole(KitOf(role)) + set - 1) % 4);

    public IReadOnlyList<PartyRole> WithRole(int set, HelloWorldRole role)
        => WithKit(Enum.GetValues<HelloWorldKit>().Single(kit => RoleIn(set, WithKit(kit)[0]) == role));

    private static HelloWorldRole FirstRole(HelloWorldKit kit) => kit switch
    {
        HelloWorldKit.Defamation => HelloWorldRole.Defamation,
        HelloWorldKit.Stack => HelloWorldRole.Stack,
        HelloWorldKit.RemoteFirst => HelloWorldRole.Remote,
        _ => HelloWorldRole.Local,
    };
}
