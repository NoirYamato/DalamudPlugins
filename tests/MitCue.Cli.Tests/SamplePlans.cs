namespace MitCue.Cli.Tests;

/// <summary>Small hand-written TOP plans (real mechanic and action ids) for the command tests.</summary>
internal static class SamplePlans
{
    /// <summary>
    /// A White Mage column (9 entries: a pre-pull Medica II, Asylum, three Temperances (the last without a time), Bell, a
    /// Benediction late in P1 without a time, a choice on MT, an optional Plenary with a note) and one Scholar entry.
    /// Entries without a time use actions whose default timing is the same in every version of the rules (a party
    /// mitigation's 30 % of its duration, a plain heal's +1 s).
    /// </summary>
    public const string Whm = """
        format: mitcue-plan/1
        id: 00000000-0000-4000-8000-000000000001
        name: Test WHM
        fight: top
        assignees:
          - { key: White Mage, job: WHM }
          - { key: Scholar, job: SCH }
        entries:
          - id: e1
            mechanic: p1.pull
            offset: -5
            who: { job: WHM, assignee: White Mage }
            do: [ { action: 133, name: Medica II } ]
          - id: e2
            mechanic: p1.program_loop
            offset: 11
            who: { job: WHM, assignee: White Mage }
            do: [ { action: 3569, name: Asylum } ]
          - id: e3
            mechanic: p1.pantokrator
            offset: -3.2
            who: { job: WHM, assignee: White Mage }
            do: [ { action: 16536, name: Temperance } ]
            severity: 3
          - id: e4
            mechanic: p1.pantokrator
            offset: 1
            who: { job: WHM, assignee: White Mage }
            do: [ { action: 25862, name: Liturgy of the Bell } ]
            note: right after the 1st stack
          - id: e9
            mechanic: p1.wave_cannon_kyrios
            who: { job: WHM, assignee: White Mage }
            do: [ { action: 140, name: Benediction } ]
          - id: e5
            mechanic: p2.solar_ray
            offset: -7.5
            who: { job: WHM, assignee: White Mage }
            do: [ { any: [ { action: 7432, name: Divine Benison }, { action: 3570, name: Tetragrammaton } ] } ]
            target: MT
          - id: e6
            mechanic: p2.flares
            offset: -10
            who: { job: WHM, assignee: White Mage }
            do: [ { action: 16536, name: Temperance } ]
          - id: e7
            mechanic: p2.cosmo_memory
            offset: -3
            who: { job: WHM, assignee: White Mage }
            do: [ { action: 7433, name: Plenary Indulgence }, { note: Medica II + Rapture straight after } ]
            optional: true
          - id: e8
            mechanic: p3.hello_world
            who: { job: WHM, assignee: White Mage }
            do: [ { action: 16536, name: Temperance } ]
          - id: s1
            mechanic: p1.pantokrator
            offset: -5
            who: { job: SCH, assignee: Scholar }
            do: [ { action: 188, name: Sacred Soil } ]
        """;

    /// <summary>A White Mage plan with an unknown mechanic, another job's action, a recast conflict and a level problem.</summary>
    public const string Broken = """
        format: mitcue-plan/1
        id: 00000000-0000-4000-8000-000000000002
        name: Broken WHM
        fight: top
        entries:
          - id: b1
            mechanic: p2.solar_ray
            offset: -5
            who: { job: WHM }
            do: [ { action: 16536, name: Temperance } ]
          - id: b2
            mechanic: p2.party_synergy_spreads
            offset: 0
            who: { job: WHM }
            do: [ { action: 16536, name: Temperance } ]
          - id: b3
            mechanic: p2.cosmo_memry
            who: { job: WHM }
            do: [ { action: 24298, name: Kerachole } ]
          - id: b4
            mechanic: p3.hello_world
            offset: 0
            who: { job: WHM }
            do: [ { action: 37011, name: Divine Caress } ]
        """;
}
