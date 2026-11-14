using System;
using System.Collections.Generic;
using System.Numerics;
using AnoMech.Core.Game;

namespace AnoMech.Scenarios.Tea;

// Ids and tunables of TEA's first phase and the Limit Cut intermission that ends it. Every value nobody has
// measured is marked UNVERIFIED here, to be tuned in this one place.
public static class TeaP1Constants
{
    public const byte Level = 80;
    public static readonly byte? Weather = null;                    // UNVERIFIED: the zone's own until the phase's is known
    public const ushort Bgm = 706;                                  // UNVERIFIED when the next phase's track takes over

    public static IReadOnlyList<Waymark> EuWaymarks =>
    [
        new(WaymarkSlot.A,     new Vector3(  0.0f, 0f, -13.3f)),
        new(WaymarkSlot.B,     new Vector3( 12.8f, 0f,  -8.6f)),
        new(WaymarkSlot.C,     new Vector3(  0.0f, 0f,  13.1f)),
        new(WaymarkSlot.D,     new Vector3(-13.8f, 0f,   0.0f)),
        new(WaymarkSlot.One,   new Vector3(  7.4f, 0f,  -0.1f)),
        new(WaymarkSlot.Two,   new Vector3( 13.8f, 0f,  -0.1f)),
        new(WaymarkSlot.Three, new Vector3(  0.0f, 0f,   7.5f)),
        new(WaymarkSlot.Four,  new Vector3(  7.5f, 0f,   7.5f)),
    ];

    public static class P1BNpcBaseId
    {
        public const uint CruiseChaser = 11342;
        public const uint Helper = 9020;                            // the invisible helper the Hawk Blasters come from
    }

    public static class P1BNpcNameId
    {
        public const uint BruteJustice = 9216;
        public const uint CruiseChaser = 9218;
    }

    public static class P1ActionId
    {
        public const uint HawkBlaster = 18480;                      // helper at a location, instant, r10, no omen
        public const uint AlphaSword = 18484;                       // Cruise Chaser->self, instant, cone 25 + hitbox
        public const uint JKick = 18516;                            // Brute Justice, instant raidwide
        public const uint SuperBlasstyCharge = 19279;               // Cruise Chaser->self, instant, line 50 + hitbox x 10
    }

    public static class P1PlayerActionId
    {
        public const uint ArmsLength = 7548;
        public const uint Surecast = 7559;
        public const uint InnerRelease = 7389;                      // a warrior's; grants Inner Strength
    }

    public static class P1StatusId
    {
        // UNVERIFIED: which vulnerability Cruise Chaser's hits leave (Physical Vulnerability Up here).
        public const ushort CruiseChaserVulnerability = 695;
    }

    public static class P1LockonId
    {
        public static readonly uint[] LimitCutNumbers = [79, 80, 81, 82, 83, 84, 85, 86];
    }

    public static class P1Geometry
    {
        public const float ArenaRadius = 20f;                       // UNVERIFIED that the rim is a wall and not a death zone
        public const float HawkBlasterCardinalRadius = 14f;
        public const float HawkBlasterDiagonalOffset = 10f;         // (+-10, +-10)
        public const float CruiseChaserLandsBehind = 3f;            // UNVERIFIED; at most about 6 y
        public const float AlphaSwordHalfAngle = MathF.PI / 4f;     // not on the sheet
    }

    public static class P1Duration
    {
        public const float LimitCutIntoTheTrack = 192f;             // UNVERIFIED that the track starts with the pull
        public const float NumberSpacing = 0.05f;                   // UNVERIFIED: the eight show over about 0.4 s
        public const float CruiseChaserLandsBeforeAlphaSword = 0.5f; // UNVERIFIED
        public const float CruiseChaserVulnerability = 20f;         // UNVERIFIED; long enough that any second hit kills
        // UNVERIFIED that each holds from the press: Arm's Length and Surecast, and the Inner Strength a warrior's
        // Inner Release grants.
        public const float AntiKnockback = 6f;
        public const float InnerStrength = 15f;
        // UNVERIFIED, all three: how long after a hit lands its damage shows and whoever it killed falls. A number
        // killed by a blast still draws its own hit until it has fallen, so the blast's delay is also that window.
        public const float HawkBlasterDamageDelay = 1f;
        public const float CruiseChaserDamageDelay = 1f;
        public const float JKickDamageDelay = 1f;
        public const float HawkBlasterAnimationLock = 0.6f;         // UNVERIFIED; its helper is busy until then
    }

    public static class P1Rules
    {
        // UNVERIFIED, all four; that both push straight away from Cruise Chaser, and that facing him cancels neither.
        public const float AlphaSwordKnockback = 5f;
        public const float AlphaSwordKnockbackSpeed = 10f;
        public const float SuperBlasstyChargeKnockback = 20f;
        public const float SuperBlasstyChargeKnockbackSpeed = 40f;

        // Two blasts go off at once, and their helpers are free again before the next; the third is spare.
        public const int LimitCutHelpers = 3;
    }
}
