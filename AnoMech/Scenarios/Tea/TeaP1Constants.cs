using System;
using System.Collections.Generic;
using System.Numerics;
using AnoMech.Core.Game;

namespace AnoMech.Scenarios.Tea;

// Ids and tunables of TEA's first phase, Living Liquid, and the Limit Cut intermission that ends it. Every value nobody
// has measured is marked UNVERIFIED here, to be tuned in this one place.
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
        public const uint LivingLiquid = 11335;
        public const uint LiquidHand = 11336;
        public const uint LiquidRage = 11337;                       // a tornado
        public const uint Embolus = 11339;
        public const uint CruiseChaser = 11342;
        public const uint Helper = 9020;                            // the invisible helper the waves, Sluices and Hawk Blasters come from
    }

    public static class P1BNpcNameId
    {
        public const uint LivingLiquid = 9211;
        public const uint LiquidHand = 9212;
        public const uint LiquidRage = 9213;
        public const uint Embolus = 9215;
        public const uint BruteJustice = 9216;
        public const uint CruiseChaser = 9218;
    }

    public static class P1EObjId
    {
        // UNVERIFIED: that this object draws a Liquid Rage's water, and with no spawn arguments.
        public const uint LiquidRagePuddle = 2005400;
    }

    public static class P1ActionId
    {
        public const uint FluidSwing = 18864;                       // Living Liquid at its target, instant, cone 8 + hitbox
        public const uint FluidStrike = 18871;                      // Liquid Hand at its target, instant, cone 10 + hitbox
        public const uint Cascade = 18470;                          // Living Liquid, 4 s, raidwide
        public const uint HandOfPrayer = 18475;                     // Liquid Hand, instant, raidwide
        public const uint HandOfParting = 18476;                    // Liquid Hand, instant, raidwide
        public const uint HandOfPain = 18477;                       // Liquid Hand, 3 s, raidwide
        public const uint ProteanWaveFromTheLiquid = 18466;         // Living Liquid->self, 3 s, cone 40
        public const uint ProteanWaveStraightAhead = 18467;         // Living Liquid->self, instant, cone 40
        public const uint ProteanWaveAtAPlayer = 18468;             // helper->self, 3 s, cone 40
        public const uint ProteanWaveAtTheClosest = 18469;          // helper->self, instant; the sheet gives it no cone
        public const uint TornadoWave = 18869;                      // helper on a tornado, 3 s, cone 40
        public const uint TornadoWaveUnseen = 18870;                // helper on a tornado, instant; the sheet gives it no cone
        public const uint Sluice = 18865;                           // helper at a location, 3 s, r5
        public const uint Splash = 18866;                           // Living Liquid, instant, raidwide
        public const uint Drainage = 18471;                         // a tornado at its tether's holder, instant, r6
        public const uint LiquidGaol = 18472;
        public const uint Pressurize = 18473;
        public const uint Outburst = 18474;                         // an Embolus, instant, raidwide
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
        public const uint Berserk = 38;                             // the button Inner Release replaces from level 70
        public const uint Esuna = 7568;
        public const uint WardensPaean = 3561;
    }

    public static class P1StatusId
    {
        // UNVERIFIED: which vulnerability Cruise Chaser's hits leave (Physical Vulnerability Up here).
        public const ushort CruiseChaserVulnerability = 695;
        public const ushort Throttle = 700;
        // UNVERIFIED: which of the sheet's four "Water Resistance Down II" rows (1025, 1157, 2094, 2144) a wave leaves.
        public const ushort WaterResistanceDown = 2144;
        // Hallowed Ground, Holmgang, Living Dead, Superbolide.
        public static readonly ushort[] Invulnerabilities = [82, 409, 810, 1836];
    }

    public static class P1TetherId
    {
        public const ushort Drainage = 3;
    }

    // UNVERIFIED which of the Liquid Hand's two forms is which.
    public static class P1ModelState
    {
        public const byte HandFist = 19;
        public const byte HandPalm = 20;
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
        public const float TornadoRingRadius = 14.5f;               // UNVERIFIED; drawn between 14.3 and 15.0
        // UNVERIFIED; between 7.0 and 7.6. The larger, so a player learns the wider berth.
        public const float PuddleRadius = 7.6f;
        public const float ProteanWaveHalfAngle = MathF.PI / 12f;   // not on the sheet
        public const float CleaveHalfAngle = MathF.PI / 4f;         // not on the sheet
        public const float DrainageRadius = 6f;
        public const float EmbolusContact = 1f;                     // UNVERIFIED

        // Where a run puts the bosses at its start, on relative bearings (degrees clockwise of the open quarter): the
        // Living Liquid on the centre, turned to its tank; the Liquid Hand beside it, toward its own. UNVERIFIED: the
        // fight leaves them wherever their tanks took them.
        public const float LiquidStartsFacing = 135f;
        public const float HandStartsOnBearing = 225f;
        public const float HandStartsFromTheCentre = 2.1f;
    }

    public static class P1Duration
    {
        // The fight's clock 8 s before the first Hawk Blaster. UNVERIFIED: that the track starts with the pull, and
        // the kill it counts from, the Liquid's 125.5 s in, 5.2 s before that blaster.
        public const float LimitCutIntoTheTrack = 122.7f;
        public const float NumberSpacing = 0.05f;                   // UNVERIFIED: the eight show over about 0.4 s
        // UNVERIFIED. Over 0.1: his model is turned to its number again 0.1 s before each cleave, which has to come
        // after he lands.
        public const float CruiseChaserLandsBeforeAlphaSword = 0.5f;
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

        // The fight's clock as each Living Liquid part starts.
        public const float ProteanOneIntoTheTrack = 62f;
        public const float CascadeTwoIntoTheTrack = 85.8f;
        public const float WaterResistanceDown = 5f;                // UNVERIFIED; 3.1 to 6.0 by how the strats space the waves
        // UNVERIFIED ("about 9 s"). Six cleanses by two healers, one each per 2.5 s, take 5.7 s.
        public const float Throttle = 9f;
        public const float ThrottleAfterLiquidGaol = 0.4f;
        public const float WardensPaeanBarrier = 30f;               // on a target it found nothing to take off
        // UNVERIFIED: in the fight a puddle is a damage over time, so here its first second harms nobody.
        public const float PuddleKillsAfter = 1f;
        public const float LiquidHoldsItsFacingAfterTheLastWave = 1f;   // UNVERIFIED when it turns back to its target
        public const float EmbolusAfterPressurize = 0.6f;
        public const float HandLeavesAfterTheLiquid = 1.2f;
        public const float LiquidCorpseFade = 3f;
        public const float LivingLiquidDamageDelay = 1f;            // UNVERIFIED: for the waves, Sluice and Drainage
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

        public const float BossWalkSpeed = 6f;                      // UNVERIFIED
        // UNVERIFIED, both; that a telegraphed wave pushes straight away from where it came from.
        public const float ProteanWaveKnockback = 10f;
        public const float ProteanWaveKnockbackSpeed = 20f;
        // UNVERIFIED, all three, between the bosses' centres. Farther apart than the first as the Hand glows, it shows
        // a palm, else a fist. A palm's slam needs them within the second (their hitboxes touching), a fist's beyond
        // the third.
        public const float HandShowsAPalmBeyond = 12f;
        public const float PalmNeedsThemWithin = 5.1f;
        public const float FistNeedsThemBeyond = 12f;
        // UNVERIFIED, all three: one cooldown, in the engine's own terms.
        public const float DrainageMitigation = 0.2f;
        public const float FluidSwingMitigation = 0.2f;
        public const float FluidStrikeMitigation = 0.2f;
        public const float EmbolusSpeed = 1.5f;                     // UNVERIFIED ("slowly")
        public const int LivingLiquidHelpers = 17;                  // UNVERIFIED: the fight's own count
    }
}
