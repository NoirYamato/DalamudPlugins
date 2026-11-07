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
        public const uint CosmoMeteor = 15726;                      // hitbox 3; two, north and south
        public const uint CosmoComet = 15727;                       // hitbox 1; six, on the other compass points
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
        public const uint CosmoMeteorEnrage = 31669;                // add->self, r100
        public const uint CosmoMeteorSpread = 32699;                // helper->player, r5
    }

    public static class P6LockonId
    {
        public const uint Flare = 346;                              // the flare lands 8.1s after it
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
    }

    public static class P6Rules
    {
        // A tank's limit break 3: Cosmo Memory kills anyone below it.
        public const float CosmoMemoryMitigation = 0.80f;
        public const int CosmoDiveStack = 6;
        public const int CosmoMeteorStack = 5;                      // UNVERIFIED

        // The helper pool TOP's zone spawns once helpers belong to the zone: a P6 block may never keep more busy.
        public const int HelperPool = 36;
    }
}
