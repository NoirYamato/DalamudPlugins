using System.Numerics;
using System.Runtime.CompilerServices;
using AnoMech.Core.EnemyActions;

namespace AnoMech.Core.Game;

// One exaflare: Explosions circles going off one after another, each Advance further along Start's facing than the
// one before. The first is First, cast at CastAt, whose omen marks the lane and whose release is the first
// explosion; each later one is Rest, Interval after the one before, a cast of its own where it lands.
public sealed record ExaflareLane(Placement Start, float Advance, int Explosions, float CastAt, float Interval, EnemyAction First, EnemyAction Rest)
{
    public Vector3 Position(int step) => Start.MoveForward(Advance * step).Position;

    // When `step` goes off: its ActionEffect.
    public float ReleaseAt(int step) => CastAt + First.ReleasesAfter() + Interval * step;

    // When `step` decides who it hits: the first's bar end, a later one's own cast.
    public float HitAt(int step)
        => step == 0 ? CastAt + First.ResolvesAfter() : CastAtOf(step) + Rest.ResolvesAfter();

    // On the run's clock, so it is called while the run starts.
    public void Schedule(EventScheduler events, HelperCast cast, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        events.Add(CastAt, () => cast(First, at: Start), file, line);
        for (var step = 1; step < Explosions; step++)
        {
            var at = new Placement(Position(step), Start.Rotation);
            events.Add(CastAtOf(step), () => cast(Rest, at: at), file, line);
        }
    }

    private float CastAtOf(int step) => ReleaseAt(step) - Rest.ReleasesAfter();
}
