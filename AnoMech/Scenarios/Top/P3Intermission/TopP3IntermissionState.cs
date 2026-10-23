using System;
using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.Top.P3Intermission;

public sealed class TopP3IntermissionState
{
    public const int Spreads = 4;
    public const int Stacks = 2;
    public const int Unmarked = 2;

    // [0-3] spread, [4, 5] stack, [6, 7] nothing, in no particular order within each.
    public RoleList Debuffs { get; }

    public FirstArms FirstArms { get; }

    public Automarkers Automarkers { get; }

    // Arm bearings, degrees clockwise from north. UNVERIFIED: the hexagon is assumed never to turn.
    public IReadOnlyList<float> FirstSetBearings => FirstArms == FirstArms.NorthTriangle ? NorthTriangle : SouthTriangle;
    public IReadOnlyList<float> SecondSetBearings => FirstArms == FirstArms.NorthTriangle ? SouthTriangle : NorthTriangle;

    private static readonly float[] NorthTriangle = [0f, 120f, 240f];
    private static readonly float[] SouthTriangle = [60f, 180f, 300f];

    public TopP3IntermissionState(Rng rng, SimParty party, TopP3IntermissionStateOverrides overrides)
    {
        // Every roll is drawn whether or not it is overridden, so pinning one leaves the others as
        // the seed had them. The arms are drawn first because a pinned debuff seat adds draws.
        // UNVERIFIED: even odds.
        var firstArms = rng.NextObj(FirstArms.NorthTriangle, FirstArms.SouthTriangle);
        FirstArms = overrides.FirstArms ?? firstArms;

        var debuffs = new RoleListBuilder
        {
            Slots = overrides.Debuff.Resolve(party.PlayerRole).ToDictionary(r => r.Role, r => SlotsOf(r.Value)),
        }.Build(rng, party);
        Debuffs = overrides.DebuffOrder is { } order ? new RoleList(party, order) : debuffs;

        Automarkers = overrides.Automarkers;
    }

    public SniperDebuff DebuffOf(PartyRole role)
    {
        var index = Array.IndexOf(Debuffs.List, role);
        return index < Spreads ? SniperDebuff.Spread : index < Spreads + Stacks ? SniperDebuff.Stack : SniperDebuff.Nothing;
    }

    public IReadOnlyList<PartyRole> With(SniperDebuff debuff) => Debuffs.List.Where(r => DebuffOf(r) == debuff).ToList();

    private static int[] SlotsOf(SniperDebuff debuff) => debuff switch
    {
        SniperDebuff.Spread => [0, 1, 2, 3],
        SniperDebuff.Stack => [4, 5],
        _ => [6, 7],
    };
}
