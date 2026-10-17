using System.Diagnostics;
using System.Globalization;
using AnoMech.Core.Game.Party;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P3HelloWorld;

namespace AnoMech.Tests;

public class ScratchHumanProbe
{
    [Explicit]
    [Test]
    public void Probe()
    {
        var walk = Enum.Parse<TopP3HelloWorldLpduHumanTests.Walk>(TestContext.Parameters.Get("Walk", "Straight"));
        float F(string name, float def) => TestContext.Parameters.Get(name) is { } s ? float.Parse(s, CultureInfo.InvariantCulture) : def;
        var late = F("Late", 0f);
        var curve = F("Curve", 2f);
        var stretch = F("Stretch", 1f);
        var seeds = TestContext.Parameters.Get("Seeds", 10);
        var firstSeed = TestContext.Parameters.Get("FirstSeed", 1);
        var kits = TestContext.Parameters.Get("Kits") is { } k ? k.Split(',').Select(Enum.Parse<HelloWorldKit>).ToArray() : Enum.GetValues<HelloWorldKit>();
        var human = new TopP3HelloWorldLpduHumanTests.Human(_ => late, walk, stretch, curve);
        var total = 0;
        var failed = 0;
        var lines = new List<string>();
        var sw = Stopwatch.StartNew();
        foreach (var kit in kits)
        {
            var kitFailed = 0;
            foreach (var seat in PerRole.All)
            foreach (var seed in Enumerable.Range(firstSeed, seeds).Select(i => 11 * i))
            {
                total++;
                var outcome = TopP3HelloWorldLpduHumanTests.Play(seat, kit, seed, human, artifacts: false);
                if (outcome.Problems.Count == 0) continue;
                failed++;
                kitFailed++;
                lines.AddRange(outcome.Problems.Take(3));
            }
            TestContext.Out.WriteLine($"{kit}: {kitFailed}/{8 * seeds}");
        }
        TestContext.Out.WriteLine($"TOTAL {walk} late {late} curve {curve} stretch {stretch}: {failed}/{total} failed, {sw.Elapsed.TotalSeconds / total:F3} s/run");
        foreach (var line in lines.Take(60)) TestContext.Out.WriteLine(line);
    }
}

public class ScratchHumanReplay
{
    [Explicit]
    [Test]
    public void Replay()
    {
        var walk = Enum.Parse<TopP3HelloWorldLpduHumanTests.Walk>(TestContext.Parameters.Get("Walk", "Straight"));
        float F(string name, float def) => TestContext.Parameters.Get(name) is { } s ? float.Parse(s, CultureInfo.InvariantCulture) : def;
        var late = F("Late", 0f);
        var human = new TopP3HelloWorldLpduHumanTests.Human(_ => late, walk, F("Stretch", 1f), F("Curve", 2f));
        var seat = Enum.Parse<PartyRole>(TestContext.Parameters.Get("Seat", "MainTank"));
        var kit = Enum.Parse<HelloWorldKit>(TestContext.Parameters.Get("Kit", "Defamation"));
        var outcome = TopP3HelloWorldLpduHumanTests.Play(seat, kit, TestContext.Parameters.Get("Seed", 11), human, alwaysArtifacts: true);
        foreach (var line in outcome.Problems) TestContext.Out.WriteLine(line);
    }
}

public class ScratchHumanTrace
{
    [Explicit]
    [Test]
    public void Trace()
    {
        var walk = Enum.Parse<TopP3HelloWorldLpduHumanTests.Walk>(TestContext.Parameters.Get("Walk", "Straight"));
        float F(string name, float def) => TestContext.Parameters.Get(name) is { } s ? float.Parse(s, CultureInfo.InvariantCulture) : def;
        var late = F("Late", 0f);
        var human = new TopP3HelloWorldLpduHumanTests.Human(_ => late, walk, F("Stretch", 1f), F("Curve", 2f));
        var seat = Enum.Parse<PartyRole>(TestContext.Parameters.Get("Seat", "MainTank"));
        var kit = Enum.Parse<HelloWorldKit>(TestContext.Parameters.Get("Kit", "Defamation"));
        var from = F("From", 0f);
        var until = F("Until", 0f);
        var every = F("Every", 0.1f);
        var next = from;
        TopP3HelloWorldLpduHumanTests.ScratchWatch = p =>
        {
            if (p.Time < next || p.Time > until) return;
            next += every;
            var parts = PerRole.All.Select(r =>
            {
                var m = p.Member(r)!;
                var rots = string.Concat(m.ActiveStatuses.Select(s => s.StatusId switch
                {
                    AnoMech.Scenarios.Top.TopP3Constants.P3StatusId.RedRot => "r",
                    AnoMech.Scenarios.Top.TopP3Constants.P3StatusId.BlueRot => "b",
                    AnoMech.Scenarios.Top.TopP3Constants.P3StatusId.UnderflowDebugger => "R",
                    AnoMech.Scenarios.Top.TopP3Constants.P3StatusId.PerformanceDebugger => "B",
                    _ => "",
                }));
                return $"{r.ToString()[..3]}{(r == seat ? "*" : "")}({m.Position.X:F1},{m.Position.Z:F1}){rots}";
            });
            TestContext.Out.WriteLine($"{p.Time:F2} " + string.Join(" ", parts));
        };
        try
        {
            var outcome = TopP3HelloWorldLpduHumanTests.Play(seat, kit, TestContext.Parameters.Get("Seed", 11), human, alwaysArtifacts: true);
            foreach (var line in outcome.Problems) TestContext.Out.WriteLine(line);
        }
        finally { TopP3HelloWorldLpduHumanTests.ScratchWatch = null; }
    }
}

public class ScratchFrameCost
{
    [Explicit]
    [Test]
    public void FrameCost()
    {
        var walk = Enum.Parse<TopP3HelloWorldLpduHumanTests.Walk>(TestContext.Parameters.Get("Walk", "Straight"));
        float F(string name, float def) => TestContext.Parameters.Get(name) is { } s ? float.Parse(s, CultureInfo.InvariantCulture) : def;
        var human = new TopP3HelloWorldLpduHumanTests.Human(_ => F("Late", 0f), walk, F("Stretch", 1f), F("Curve", 2f));
        var seeds = TestContext.Parameters.Get("Seeds", 5);
        var sw = new Stopwatch();
        var worst = new List<(double Ms, float At, string Run)>();
        var label = "";
        TopP3HelloWorldLpduHumanTests.ScratchWatch = p =>
        {
            if (sw.IsRunning && p.Time > 1f) worst.Add((sw.Elapsed.TotalMilliseconds, p.Time, label));
            sw.Restart();
        };
        try
        {
            foreach (var kit in Enum.GetValues<HelloWorldKit>())
            foreach (var seat in PerRole.All)
            foreach (var seed in Enumerable.Range(1, seeds).Select(i => 11 * i))
            {
                label = $"{seat} {kit} {seed}";
                sw.Reset();
                TopP3HelloWorldLpduHumanTests.Play(seat, kit, seed, human, artifacts: false);
            }
        }
        finally { TopP3HelloWorldLpduHumanTests.ScratchWatch = null; }
        var sorted = worst.OrderByDescending(w => w.Ms).ToList();
        TestContext.Out.WriteLine($"FRAMES {walk}: worst {sorted[0].Ms:F1} ms at t={sorted[0].At:F2} ({sorted[0].Run}); 99.9th pct {sorted[sorted.Count / 1000].Ms:F2} ms; over 16.7 ms: {sorted.Count(w => w.Ms > 16.7)} of {sorted.Count}");
        foreach (var w in sorted.Take(8)) TestContext.Out.WriteLine($"  {w.Ms:F1} ms at t={w.At:F2} ({w.Run})");
    }
}

public class ScratchBotsFrameCost
{
    [Explicit]
    [TestCase(typeof(TopP3HelloWorldScenario))]
    [TestCase(typeof(AnoMech.Scenarios.Top.P2PartySynergy.TopP2PartySynergyScenario))]
    public void BotsFrameCost(Type scenario)
    {
        var sw = new Stopwatch();
        var worst = new List<(double Ms, float At, int Run)>();
        for (var run = 0; run < 40; run++)
        {
            sw.Reset();
            var r = run;
            ScenarioRun.Execute(scenario, scenario == typeof(TopP3HelloWorldScenario) ? 0 : 1, 1000 + run, new ScenarioRunOptions
            {
                WriteArtifactsOnFailure = false,
                Probe = p =>
                {
                    if (sw.IsRunning && r > 0) worst.Add((sw.Elapsed.TotalMilliseconds, p.Time, r));
                    sw.Restart();
                },
            });
        }
        var sorted = worst.OrderByDescending(w => w.Ms).ToList();
        TestContext.Out.WriteLine($"FRAMES {scenario.Name}: worst {sorted[0].Ms:F1} ms at t={sorted[0].At:F2} (run {sorted[0].Run}); 99.9th pct {sorted[sorted.Count / 1000].Ms:F2} ms");
    }
}
