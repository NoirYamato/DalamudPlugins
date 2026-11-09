using System.Collections.Generic;

namespace AnoMech.Scenarios.Top;

// NoirMech's TOP P6 ids and tunables, kept out of TopConstants so upstream release merges stay clean. P6 reuses
// upstream's Alpha Omega, its P6 action and status ids (Cosmo Memory, Cosmo Arrow, Cosmo Dive, the Wave Cannons,
// the autos) and TopZone.P6.
public static class TopP6Constants
{
    public static class P6BNpcBaseId
    {
        public const uint Helper = 9020;                            // the invisible helper that casts for Alpha Omega
        public const uint CosmoMeteor = 15726;                      // hitbox 3; two
        public const uint CosmoComet = 15727;                       // hitbox 1; six
    }

    public static class P6BNpcNameId
    {
        public const uint CosmoMeteor = 12259;
        public const uint CosmoComet = 12260;
    }

    public static class P6ActionId
    {
        public const uint CosmoMeteor = 31664;                      // boss->self, 5s
        public const uint CosmoMeteorFinish = 31665;                // boss->self, instant visual, 1s after the flares
        public const uint CosmoMeteorBait = 31666;                  // helper at a location, 4s, r10, omen
        public const uint CosmoMeteorStack = 31667;                 // helper->player, r6, unmarked
        public const uint CosmoMeteorFlare = 31668;                 // helper->player, r100, weaker further out
        // add->self, r100: the adds' enrage. UNVERIFIED that both kinds cast it.
        public const uint CosmoMeteorEnrage = 31669;
        public const uint CosmoMeteorSpread = 32699;                // helper->player, r5
        public const uint MagicNumber = 31670;                      // boss->self, 5s, r100
        public const uint RunMi = 31648;                            // Run: ****mi*, boss->self, 16s, r100: the enrage
    }

    public static class P6StatusId
    {
        // Each Magic Number's mark: its holder dies as it runs out, unless a healer's limit break 3 removes it.
        public const ushort MagicNumber = 3532;
        // Brilliant Dynamis becomes it once its holder's limit break has given the gauge back; it gives nothing back.
        public const ushort SparkOfDynamis = 3448;

        // Last Bastion, Land Waker, Dark Force, Gunmetal Soul: a tank's limit break 3 on everyone it reaches.
        public static readonly IReadOnlyList<ushort> TankLimitBreak3 = [196, 863, 864, 1931];
    }

    public static class P6LockonId
    {
        public const uint Flare = 346;                              // UNVERIFIED: the flare lands 8.1s after it
    }

    public static class P6Geometry
    {
        // Each exaflare starts on a compass point this far out, so its first explosion only clips the rim, then
        // goes off every ExaflareAdvance through the centre to the far side.
        public const float ExaflareStartRadius = 24f;
        public const float ExaflareAdvance = 8f;
        public const int ExaflareExplosions = 7;

        // Every exasquare line covers both axes: the cross at 0, the outer lines at ±15. Each set rolls on in 5 y
        // lines, the first ExasquareFirstRoll from its line, then one ExasquareRoll further every 2 s.
        public const float ExasquareOuterLine = 15f;
        public const float ExasquareFirstRoll = 7.5f;
        public const float ExasquareRoll = 5f;

        public const float CosmoMeteorDistance = 11f;               // UNVERIFIED
        public const float CosmoCometDistance = 13f;                // UNVERIFIED

        // Compass degrees: the meteors north and south. UNVERIFIED: the comets on the other six compass points.
        public static readonly IReadOnlyList<float> CosmoMeteorBearings = [0f, 180f];
        public static readonly IReadOnlyList<float> CosmoCometBearings = [45f, 90f, 135f, 225f, 270f, 315f];

        // UNVERIFIED: how the flare's damage falls off; inside this it kills everyone but its target.
        public const float FlareLethalWithin = 20f;
    }

    public static class P6Duration
    {
        // The autos: Flash Gale follows Alpha Omega's visual, and the second auto the first.
        public const float FlashGaleAfterAuto = 0.9f;
        public const float SecondAuto = 3.2f;

        public const float CosmoDiveHitsAfterCast = 2.4f;
        // UNVERIFIED: the hits take their targets as they land; the game may take them earlier, as the cast ends.
        public const float CosmoDiveTargetsBeforeHits = 0f;

        // From Cosmo Arrow's cast start: the second set of lines, and the first lines rolling on, every 2 s after.
        public const float SecondExasquareSet = 2.01f;
        public const float FirstRollingLines = 10.01f;
        public const float RollingLineInterval = 2f;

        // From Unlimited Wave Cannon's cast start: each puddle set's drop. UNVERIFIED: a puddle's blast follows its
        // circle by 2.7 s, against its 3 s cast.
        public static readonly IReadOnlyList<float> PuddleDrops = [10.0f, 11.8f, 13.7f, 15.6f, 17.5f, 19.5f];
        public const float ExaflaresApart = 1f;
        public const float ExaflareStep = 1.1f;                     // UNVERIFIED: 1.0 is possible
        public const float ExaflareOmenDelay = 4.5f;                // UNVERIFIED

        // UNVERIFIED, as is the status: the Magic Vulnerability Up each Cosmo Meteor spread leaves.
        public const float SpreadVulnerability = 3f;

        // From Wave Cannon's cast start: the lasers at four players, then at the other four, and Wild Charge.
        public static readonly IReadOnlyList<float> LasersAfterWaveCannon = [3.0f, 5.0f];
        public const float WildChargeAfterWaveCannon = 11.3f;

        // From Cosmo Meteor's cast start: the adds spawn hidden, then show, targetable (UNVERIFIED); the spreads, four
        // and the other four a second later, twice; the flare markers (UNVERIFIED); the flares and the stack; the last
        // visual.
        public const float CosmoMeteorAddsSpawn = 7.4f;
        public const float CosmoMeteorAddsShow = 8.2f;
        public static readonly IReadOnlyList<float> CosmoMeteorSpreads = [10.1f, 11.1f, 16.1f, 17.1f];
        public const float CosmoMeteorFlareMarkers = 20.0f;
        public const float CosmoMeteorFlaresAndStack = 28.1f;
        public const float CosmoMeteorFinish = 29.1f;

        // UNVERIFIED: from Cosmo Meteor's cast start, the comets' enrage and the meteors', each only while an add of its
        // kind lives.
        public const float CometsEnrage = 20.3f;
        public const float MeteorsEnrage = 30.4f;

        // UNVERIFIED: a tank's limit break 3 counts for a hit only if it went up this long before the hit decides.
        public const float TankLimitBreak3TakesHold = 1.0f;

        // UNVERIFIED: Magic Number's mark lands this long after the hit's ActionEffect, and lasts this long.
        public const float MagicNumberMarkAfterTheHit = 1.3f;
        public const float MagicNumberMark = 6.0f;

        // UNVERIFIED: Run: ****mi*'s damage, this long after its cast starts.
        public const float RunMiLands = 30.0f;

        // UNVERIFIED: the party's own limit break generation refills the gauge evenly from Cosmo Memory's hit (its bar
        // end) to Cosmo Dive 2's bar end; nothing else adds to it but Brilliant Dynamis.
        public const float GaugeRebuild = 142.2f;

        // UNVERIFIED, the Gauntlet's own windows for the human's melee limit breaks 3, from Cosmo Dive 2's bar end: the
        // second must start by SecondDiveMeleeLatestPress (it is then free to leave Cosmo Meteor's baits before they
        // decide) and the first leaves it a 4.5 s cast to do so. The enrage's must land while Run: ****mi*'s cast bar is
        // up.
        public const float FirstDiveMeleeLatestPress = 11.2f;
        public const float SecondDiveMeleeLatestPress = 15.7f;
    }

    public static class P6Rules
    {
        public const int CosmoDiveStack = 6;
        public const int CosmoMeteorStack = 5;                      // UNVERIFIED

        // The helper pool TOP's zone spawns once helpers belong to the zone: a P6 run may never keep more busy.
        public const int HelperPool = 36;
    }
}
