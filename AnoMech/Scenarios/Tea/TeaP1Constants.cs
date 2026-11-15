using System;
using System.Collections.Generic;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.Tea;

// Ids and tunables of TEA's first phase, Living Liquid, and the Limit Cut intermission that ends it. Every value nobody
// has measured is marked UNVERIFIED here, to be tuned in this one place.
public static class TeaP1Constants
{
    public const byte Level = 80;
    public static readonly byte? Weather = null;                    // UNVERIFIED: the zone's own until the phase's is known
    public const ushort Bgm = 706;                                  // the next phase's track, 723, takes over with J Kick

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
        public const uint JagdDoll = 11338;
        public const uint Embolus = 11339;
        public const uint CruiseChaser = 11342;
        public const uint Helper = 9020;                            // the invisible helper the waves, Sluices and Hawk Blasters come from
    }

    public static class P1BNpcNameId
    {
        public const uint LivingLiquid = 9211;
        public const uint LiquidHand = 9212;
        public const uint LiquidRage = 9213;
        public const uint JagdDoll = 9214;
        public const uint Embolus = 9215;
        public const uint BruteJustice = 9216;
        public const uint CruiseChaser = 9218;
    }

    public static class P1EObjId
    {
        // Added on each Liquid Rage's spot as it appears. UNVERIFIED: that it takes no spawn arguments.
        public const uint LiquidRagePuddle = 2005400;
    }

    // The spawn packet's parts for the phase's one demihuman, which draws nothing without them: its NpcEquip row's.
    // UNVERIFIED until a Jagd Doll is seen drawn.
    public static class P1Appearance
    {
        public static readonly NpcAppearance JagdDoll = new(MainHand: 0x100020200, Equipment: [0x0, 0x10006]);
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
        public const uint Exhaust = 18462;                          // a Jagd Doll->self, instant, r8 + hitbox
        public const uint ExhaustFromAPlayer = 18463;               // round a player two Exhausts hit at once, r8 + hitbox
        public const uint ReducibleComplexity = 18464;              // a fed Jagd Doll, instant, raidwide
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
        public const uint Provoke = 7533;
        public const uint HallowedGround = 30;
        public const uint Holmgang = 43;
        public const uint LivingDead = 3638;
        public const uint Superbolide = 16152;

        // Buttons that turn into an attack on the target while their own Action row is aimed at their user: Ninjutsu,
        // Iaijutsu, Tsubame-gaeshi, Masterful Blitz, Continuation, Serpent's Tail, Twinfang, Twinblood, Astral Flow
        // and Living Muse.
        public static readonly IReadOnlySet<uint> BecomeAHitOnTheTarget =
            new HashSet<uint> { 2260, 7867, 16483, 25764, 16155, 35920, 35921, 35922, 25822, 35347 };
    }

    public static class P1StatusId
    {
        // Each of Cruise Chaser's hits leaves both.
        public const ushort MagicVulnerabilityUp = 1138;
        public const ushort PhysicalVulnerabilityUp = 695;
        public const ushort Throttle = 700;
        public const ushort WaterResistanceDown = 1025;             // Water Resistance Down II
        public const ushort LuminousAetheroplasm = 706;             // an Exhaust leaves it
        public const ushort HallowedGround = 82;
        public const ushort Holmgang = 409;
        public const ushort LivingDead = 810;
        public const ushort Superbolide = 1836;
        public static readonly ushort[] Invulnerabilities = [HallowedGround, Holmgang, LivingDead, Superbolide];
    }

    public static class P1TetherId
    {
        public const ushort Drainage = 3;
        public const ushort JagdDoll = 17;                          // from a doll to the player it follows
        public const ushort Fed = 41;                               // from a doll to the boss it is fed to
    }

    // The Liquid Hand shows the first with the bosses close as it glows and the second with them far apart. UNVERIFIED
    // which of the two looks a fist and which a palm.
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
        public const float ArenaRadius = 20f;                       // no wall: whoever goes past it dies
        public const float HawkBlasterRadius = 14f;                 // on the cardinals and the diagonals alike
        public const float CruiseChaserLandsBehind = 3f;            // UNVERIFIED; between about 2 and 4 y
        public const float AlphaSwordHalfAngle = MathF.PI / 4f;     // not on the sheet
        public const float TornadoRingRadius = 15f;
        public const float PuddleRadius = 7.6f;                     // UNVERIFIED; at least about 7.4 y and under 9.0
        // UNVERIFIED, both, and not on the sheet: a wave's between about 10 and 30 degrees, a cleave's between 19 and 54.
        public const float ProteanWaveHalfAngle = MathF.PI / 12f;
        public const float CleaveHalfAngle = MathF.PI / 4f;
        public const float DrainageRadius = 6f;
        // An Embolus bursts on whoever's hitbox comes within this of its centre: on the Living Liquid, 3.5 y round,
        // from 4.5 y between their centres. UNVERIFIED for the Liquid Hand and for a player.
        public const float EmbolusRadius = 1f;
        public const float EmbolusLeavesBeyond = 21.5f;             // it goes on out of the arena and is gone there

        // Where the Living Liquid waits for the pull and where the Liquid Hand splits off it, both facing south.
        public static readonly Vector3 LiquidBeforeThePull = new(0f, 0f, -15f);
        public static readonly Vector3 HandAppearsOn = Vector3.Zero;
        // The four Jagd Dolls' spots, the same whatever the tornadoes' layout: degrees clockwise of true north.
        public const float JagdDollRing = 7f;
        public static readonly float[] JagdDollBearings = [67.5f, 157.5f, 247.5f, 337.5f];
        // Between a doll's centre and a boss's. UNVERIFIED where the line lies: dolls walking in were fed from 3.26 to
        // 4.57 y, most of them near the far end, so it is at least the farthest.
        public const float DollIsFedWithin = 4.6f;

        // Where a run that starts after the pull puts the bosses, on relative bearings (degrees clockwise of the open
        // quarter): the Living Liquid on the centre, turned to its tank; the Liquid Hand beside it, toward its own.
        // UNVERIFIED: the fight has them wherever their tanks took them.
        public const float LiquidStartsFacing = 135f;
        public const float HandStartsOnBearing = 225f;
        public const float HandStartsFromTheCentre = 2.1f;
    }

    public static class P1Duration
    {
        // The fight's clock 8 s before the first Hawk Blaster, which comes 5.2 s after the Living Liquid falls: here
        // P1LivingLiquid's own kill, 124.5 s in. The track itself plays on from before the pull, so where it stands
        // at any point of a pull is the party's doing.
        public const float LimitCutIntoTheTrack = 121.7f;
        public const float NumberSpacing = 0.045f;
        // Over 0.1: his model is turned to its number again 0.1 s before each cleave, which has to come after he
        // lands.
        public const float CruiseChaserLandsBeforeAlphaSword = 1.17f;
        public const float MagicVulnerabilityUp = 9.96f;
        public const float PhysicalVulnerabilityUp = 16.96f;
        // Arm's Length and Surecast hold off both of his throws. UNVERIFIED: that each holds from the press, and
        // that the Inner Strength a warrior's Inner Release grants does too.
        public const float AntiKnockback = 6f;
        public const float InnerStrength = 15f;
        // UNVERIFIED, all three: how long after a hit lands its damage shows and whoever it killed falls. A number
        // killed by a blast still draws its own hit until it has fallen, so the blast's delay is also that window.
        public const float HawkBlasterDamageDelay = 1f;
        public const float CruiseChaserDamageDelay = 1f;
        public const float JKickDamageDelay = 1f;
        public const float HawkBlasterAnimationLock = 0.6f;         // UNVERIFIED; its helper is busy until then

        // The fight's clock as each Living Liquid part starts, counted from the first hit on the Living Liquid.
        public const float ProteanOneIntoTheTrack = 60.9f;
        public const float CascadeTwoIntoTheTrack = 84.8f;
        public const float WaterResistanceDown = 3.96f;
        // Six cleanses by two healers, one each per 2.5 s, take 5.7 s.
        public const float Throttle = 10f;
        public const float ThrottleAfterLiquidGaol = 0.45f;
        public const float WardensPaeanBarrier = 30f;               // on a target it found nothing to take off
        // UNVERIFIED: in the fight a puddle is a damage over time, so here its first second harms nobody.
        public const float PuddleKillsAfter = 1f;
        public const float LiquidHoldsItsFacingAfterTheLastWave = 3f;   // it turns back as its next auto-attack goes off
        // The client is shown a new set of Liquid Rages 1.2 to 1.6 s after the Cascade that brings it lands.
        public const float TornadoesAppearAfterCascade = 1.4f;
        public const float EmbolusAfterPressurize = 0.7f;
        public const float EmbolusSetsOffAfterPressurize = 5.55f;
        public const float HandLeavesAfterTheLiquid = 1.3f;
        public const float LiquidCorpseFade = 3f;
        public const float LivingLiquidDamageDelay = 1f;            // UNVERIFIED: for the waves, Sluice and Drainage

        // A boss sets off for whoever Provoked it this long after the press. UNVERIFIED for the Living Liquid: measured
        // on the Liquid Hand only.
        public const float ProvokeTakesHold = 0.67f;
        public const float TankInvulnerability = 10f;               // Hallowed Ground, Holmgang, Living Dead, Superbolide
        public const float DollTethersAfterItsFirstHit = 0.6f;      // 0.35 to 1.5 s, by the action
        // UNVERIFIED: how fast a doll's health falls is the party's doing. Here it is a quarter this long after its
        // tether shows.
        public const float DollUnderAQuarterAfter = 4f;
        public const float ExhaustEvery = 10.64f;
        public const float LuminousAetheroplasm = 4.96f;
        public const float ReducibleComplexityAfterTheFeed = 1.07f;
    }

    public static class P1Rules
    {
        // The Knockback sheet's rows 2 and 3, which his two hits name: straight away from him.
        public const float AlphaSwordKnockback = 5f;
        public const float AlphaSwordKnockbackSpeed = 10f;
        public const float SuperBlasstyChargeKnockback = 20f;
        public const float SuperBlasstyChargeKnockbackSpeed = 40f;

        // Two blasts go off at once, and their helpers are free again before the next; the third is spare.
        public const int LimitCutHelpers = 3;

        public const float BossWalkSpeed = 9f;
        // Provoke's Range on the Action sheet, past the two hitboxes.
        public const float ProvokeReach = 25f;
        // A boss stands still while its target is within the second of these past their two hitboxes, centre to centre,
        // and then walks until it is within the first: the Liquid Hand stops 4.1 y from its target. UNVERIFIED: that the
        // Living Liquid stops so too, between 5.8 and 6.0 y from its own, and both set-offs, taken for the reach of an
        // auto-attack: at least 4.4 y for the Liquid Hand and 7.0 y for the Living Liquid.
        public const float BossStopsPastTheHitboxes = 2f;
        public const float BossSetsOffPastTheHitboxes = 3f;
        // UNVERIFIED, both, and that a telegraphed wave pushes straight away from where it came from.
        public const float ProteanWaveKnockback = 10f;
        public const float ProteanWaveKnockbackSpeed = 20f;
        // Between the bosses' centres. Farther apart than the first as the Hand glows, it shows a palm, else a fist:
        // UNVERIFIED where between 15.15 and 16.13 y. A palm's slam needs them within the second: UNVERIFIED where
        // between 2.9 and 7.1 y, taken for their hitboxes touching. A fist's needs them beyond the third: UNVERIFIED,
        // anywhere over 2.4 y.
        public const float HandShowsAPalmBeyond = 15.6f;
        public const float PalmNeedsThemWithin = 5.1f;
        public const float FistNeedsThemBeyond = 12f;
        // UNVERIFIED: one cooldown, in the engine's own terms.
        public const float DrainageMitigation = 0.2f;
        public const float EmbolusSpeed = 1.92f;

        public const float DollWalkSpeed = 9f;
        // A doll stands a median 3.3 y from the player it follows, the first of these past their two hitboxes.
        // UNVERIFIED: the second, past which it sets off again, taken from the bosses'.
        public const float DollStopsPastTheHitboxes = 2f;
        public const float DollSetsOffPastTheHitboxes = 3f;
        // Fed to a boss with less than this share of its health left, a doll harms nobody: seen harmless at 24.5 % and
        // deadly at 25.3 %.
        public const float DollIsReadyUnder = 0.25f;
        public const float DollHealthFloor = 0.1f;                  // UNVERIFIED: where the scripted health stops falling
        public const int LivingLiquidHelpers = 17;                  // the fight's own count
    }
}
