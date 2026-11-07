using AnoMech.Core.EnemyActions;

namespace AnoMech.Core.Game;

// One cast made for an enemy by an invisible helper standing at `at` (where the enemy stands by default): what a
// piece that needs many helpers at once is handed to cast with.
public delegate void HelperCast(EnemyAction action, CastTarget target = default, Placement? at = null, byte animationVariation = 0);
