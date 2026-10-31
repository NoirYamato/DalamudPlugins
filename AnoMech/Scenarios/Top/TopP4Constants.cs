namespace AnoMech.Scenarios.Top;

// NoirMech's TOP P4 ids, kept out of TopConstants so upstream release merges stay clean. P4 reuses P3's boss
// (TopP3Constants.OmegaP3) and its Wave Repeater ids and radii.
public static class TopP4Constants
{
    public const byte Weather = TopP3Constants.Weather;             // UNVERIFIED: P3's weather carries on
    public const ushort Bgm = TopP3Constants.Bgm;                   // UNVERIFIED: P3's track carries on

    public static class P4ActionId
    {
        public const uint WaveCannonCast = 31617;                   // boss->self, 5s visual; round 1's only cast bar
        // UNVERIFIED which of these boss visuals plays at which moment.
        public const uint WaveCannonVisual = 32534;
        public const uint WaveCannonVisual2 = 31618;
        public const uint WaveCannonVisual3 = 31619;
        public const uint WaveCannonVisual4 = 31620;
        public const uint StackMarker = 22393;                      // helper->player, instant; marks a line stack's target
        public const uint WaveCannonBeam = 31614;                   // helper->self, instant, rect 100 x 6; one per player
        public const uint WaveCannonRepeat = 31616;                 // helper->self, 5.3s, rect 100 x 6; down the beams' lines
        public const uint LineStack = 31615;                        // helper->self, instant, rect 100 x 6
    }

    public static class P4Duration
    {
        // UNVERIFIED: the repeats' orange lines show just after the beams, which land 0.6 s into the cast.
        public const float RepeatOmenDelay = 1f;
    }

    public static class P4Rules
    {
        // UNVERIFIED: fewer kills everyone in the line, unless only tanks are in it.
        public const int LineStackMinimum = 4;
    }
}
