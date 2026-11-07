using AnoMech.Core.EnemyActions;
using static AnoMech.Core.EnemyActions.EnemyActionEffects;
using static AnoMech.Core.EnemyActions.Severity;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Scenarios.Top.TopP6Constants;

namespace AnoMech.Scenarios.Top;

// NoirMech's TOP P6 actions, kept out of TopActions so upstream release merges stay clean; P6 also casts upstream's
// Cosmo Arrow and Wave Cannon actions. Death reasons describe the fight, never a strat's spots. No damage values are
// known: a hit that kills does so outright.
public static class TopP6Actions
{
    private static readonly VulnSpec MagicVulns = VulnerableTo(StatusId.MagicVulnerabilityUp);

    private const string HitByAnExaflare = "Hit by an exaflare: each goes off on the rim, then every 8 y through the centre to the far side";

    // -- Cosmo Memory --
    // Checks only a human's mitigation, and only while tank mitigation is required: bots always live.
    public static readonly EnemyAction CosmoMemory = new(ActionId.CosmoMemory)
    {
        Effects = [Damage(Normal.MinMit(P6Rules.CosmoMemoryMitigation)), ApplyStatus(StatusId.BrilliantDynamis, 0f)],
        DeathExplanation = "it kills unless a tank's limit break 3 is up as it lands",
    };

    // -- Autos --
    public static readonly EnemyAction AutoAttack = new(ActionId.AlphaOmegaAutoAttack);

    // TopP6AlphaOmega decides who Flash Gale kills; the action only shows the hit.
    public static readonly EnemyAction FlashGale = new(ActionId.Unknown7ddf) { Effects = [Damage()] };

    // -- Cosmo Dive --
    public static readonly EnemyAction CosmoDive = new(ActionId.CosmoDive);

    // TopP6AlphaOmega decides who these kill.
    public static readonly EnemyAction CosmoDiveBuster = new(ActionId.CosmoDive_7BA7) { Effects = [Damage()] };

    public static readonly EnemyAction CosmoDiveStack = new(ActionId.CosmoDive_7BA8) { Effects = [Damage()] };

    // -- Unlimited Wave Cannon --
    public static readonly EnemyAction UnlimitedWaveCannon = new(ActionId.UnlimitedWaveCannon);

    public static readonly EnemyAction ExaflareFirstExplosion = new(ActionId.WaveCannon_7BAD)
    {
        Cast = new() { OmenDelay = P6Duration.ExaflareOmenDelay, AnimationLock = 1.1f },
        Effects = [Damage(Lethal)],
        DeathExplanation = HitByAnExaflare,
    };

    public static readonly EnemyAction ExaflareExplosion = new(ActionId.WaveCannon_7BAE)
    {
        Cast = new() { AnimationLock = 1.1f },
        Effects = [Damage(Lethal)],
        DeathExplanation = HitByAnExaflare,
    };

    public static readonly EnemyAction WaveCannonPuddle = new(ActionId.WaveCannon_7BAF)
    {
        Effects = [Damage(Lethal)],
        DeathExplanation = "Hit by a Wave Cannon puddle: six times, one drops under every player and goes off 3 s later",
    };

    // -- Wave Cannon --
    // Upstream's lasers and Wild Charge, hits and rules unchanged, with what kills said in the fight's words.
    public static readonly EnemyAction WaveCannonLaser = TopActions.WaveCannonProtean with
    {
        DeathExplanation = "Hit by two Wave Cannon lasers: each fires from Alpha Omega down a line through its target and leaves a vulnerability",
    };

    public static readonly EnemyAction WildCharge = TopActions.WaveCannonWildCharge with
    {
        DeathExplanation = "Wild Charge's line from Alpha Omega needs all eight players in it, with the two tanks nearest Alpha Omega",
    };

    // -- Cosmo Meteor --
    public static readonly EnemyAction CosmoMeteor = new(P6ActionId.CosmoMeteor);

    public static readonly EnemyAction CosmoMeteorFinish = new(P6ActionId.CosmoMeteorFinish);

    // UNVERIFIED: whether it kills.
    public static readonly EnemyAction CosmoMeteorBait = new(P6ActionId.CosmoMeteorBait)
    {
        Effects = [Damage(Lethal)],
        DeathExplanation = "Hit by a Cosmo Meteor puddle: one drops under every player as the cast ends and goes off 4 s later",
    };

    public static readonly EnemyAction CosmoMeteorSpread = new(P6ActionId.CosmoMeteorSpread)
    {
        Effects = [Damage(MagicVulns), ApplyStatus(StatusId.MagicVulnerabilityUp, P6Duration.SpreadVulnerability)],
        DeathExplanation = "Hit by two Cosmo Meteor spreads: each hits everyone within 5 y of its target and leaves a vulnerability",
    };

    // The scenario decides who the stack and the flares kill; the actions only show the hits.
    public static readonly EnemyAction CosmoMeteorStack = new(P6ActionId.CosmoMeteorStack) { Effects = [Damage()] };

    public static readonly EnemyAction CosmoMeteorFlare = new(P6ActionId.CosmoMeteorFlare) { Effects = [Damage()] };
}
