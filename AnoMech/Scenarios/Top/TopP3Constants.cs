namespace AnoMech.Scenarios.Top;

// NoirMech's TOP P3 ids, kept out of TopConstants so upstream release merges stay clean.
public static class TopP3Constants
{
    public const uint OmegaP3 = 15717;                              // BNpcBase, ModelChara 3775, hitbox 12.5
    public const byte Weather = 79;                                 // UNVERIFIED
    public const ushort Bgm = 950;                                  // From the Heavens (Orchestral Version)

    public static class P3ActionId
    {
        public const uint HelloWorld = 31573;                       // boss->self, 5s raidwide that applies every debuff
        public const uint LatentDefect = 31599;                     // boss->self, 9s visual, one per tower set
        public const uint RedTower = 31583;                         // Cascading Latent Defect, helper 10s, r6, omen 443
        public const uint BlueTower = 31584;                        // Latent Performance Defect, helper 10s, r6, omen 454
        public const uint RedTowerUnsoaked = 31585;                 // range-100 wipe
        public const uint BlueTowerUnsoaked = 31586;
        public const uint RedTowerStatusExpired = 31581;            // range-100 wipe
        public const uint BlueTowerStatusExpired = 31582;
        public const uint Stack = 31574;                            // Critical Synchronization Bug, r5
        public const uint Defamation = 31575;                       // Critical Overflow Bug, r20
        public const uint RedRotExplosion = 31578;                  // Critical Underflow Bug, r5
        public const uint BlueRotExplosion = 31579;                 // Critical Performance Bug, r5
        public const uint LatentSynchronizationBugExpired = 31576;
        public const uint LatentDefectExpired = 31577;
        public const uint CriticalError = 31588;                    // boss->self, 8s raidwide
        // UNVERIFIED: the players' Oversampled Wave Cannon Loading statuses land with the cast's start.
        public const uint OversampledWaveCannonEast = 31595;        // boss->self, 10s; monitors on the boss's right
        public const uint OversampledWaveCannonWest = 31596;        // monitors on the boss's left

        // -- P3 Intermission --
        public const uint WaveRepeater = 31567;                     // helper at a location, 5s, r6; the first ring
        public const uint WaveRepeaterRing2 = 31568;                // instant donuts, outer 12 / 18 / 24
        public const uint WaveRepeaterRing3 = 31569;
        public const uint WaveRepeaterRing4 = 31570;
        public const uint ColossalBlow = 31566;                     // arm unit->self, 2s, r11
    }

    public static class P3StatusId
    {
        public const ushort SynchronizationCodeSmell = 3436;
        public const ushort OverflowCodeSmell = 3437;
        public const ushort UnderflowCodeSmell = 3438;              // becomes red rot
        public const ushort PerformanceCodeSmell = 3439;            // becomes blue rot
        public const ushort LocalCodeSmell = 3503;
        public const ushort RemoteCodeSmell = 3441;
        public const ushort CriticalSynchronizationBug = 3524;      // stack
        public const ushort CriticalOverflowBug = 3525;             // defamation
        public const ushort RedRot = 3526;                          // Critical Underflow Bug
        public const ushort BlueRot = 3429;                         // Critical Performance Bug
        public const ushort LocalRegression = 3529;                 // breaks when the pair comes together
        public const ushort RemoteRegression = 3530;                // breaks when the pair moves apart
        public const ushort LatentSynchronizationBug = 3434;        // cleansed by taking a stack
        public const ushort LatentDefect = 3527;                    // cleansed by taking a defamation
        public const ushort RedTowerDebuff = 3528;                  // cleansed by the holder's own red rot exploding
        public const ushort BlueTowerDebuff = 3435;
        public const ushort SynchronizationDebugger = 3430;
        public const ushort OverflowDebugger = 3431;
        public const ushort UnderflowDebugger = 3432;               // immune to red rot
        public const ushort PerformanceDebugger = 3433;             // immune to blue rot

        // -- P3 Intermission --
        public const ushort SniperCannonFodder = 3425;              // a spread when it runs out
        public const ushort HighPoweredSniperCannonFodder = 3426;   // a two-player stack when it runs out
    }

    public static class P3EObjId
    {
        public const uint VoidZone = 2013217;
        // UNVERIFIED: the void zone's real spawn args are unknown; these bind it to the duty director, as the
        // P5 towers are, with the low byte every real EObj spawn carries.
        public const uint VoidZoneEventId = TopConstants.EObjId.TowerEventId;
        public const uint VoidZoneArg2 = 3;
    }

    public static class P3TimelineId
    {
        public const ushort OmegaShow = 4561;                       // mon_sp/show/mon_sp001, Omega's slow appearance
    }

    public static class P3Geometry
    {
        public const float TowerRingRadius = 13.7f;                 // UNVERIFIED, measured from guide drawings
        public const float RotContactRadius = 1.5f;                 // UNVERIFIED, must stay under the 2.5y a stack partner stands off
        public const float RemoteRegressionBreakDistance = 12f;     // UNVERIFIED, breaks above
        public const float LocalRegressionBreakDistance = 9f;       // UNVERIFIED, breaks below

        // -- P3 Intermission --
        // UNVERIFIED: the arms stand on a hexagon at this radius, at N and every 60 degrees from it.
        public const float ArmRadius = 14f;
        public const float VoidZoneRadius = 6f;                     // UNVERIFIED
        // The donuts' inner safe radii; the sheet has only the outer ones.
        public const float WaveRepeaterInner2 = 6f;
        public const float WaveRepeaterInner3 = 12f;
        public const float WaveRepeaterInner4 = 18f;
    }

    public static class P3Duration
    {
        public const float CodeSmell = 3f;
        public const float Rot = 27f;
        public const float Regression = 10f;
        public const float LatentSynchronizationBug = 69f;
        public const float LatentDefect = 26f;                      // UNVERIFIED
        public const float TowerDebuff = 12f;                       // UNVERIFIED

        // -- P3 Intermission --
        public const float SniperCannonFodder = 19.1f;              // UNVERIFIED, inferred from when the spreads go off
        public const float HighPoweredSniperCannonFodder = 19.1f;   // UNVERIFIED, assumed to go off with the spreads
    }
}
