using AVATimePilot84.Engine;

namespace AVATimePilot84.Tests;

/// <summary>How the computer player gets on, zone by zone (set TIMEPILOT84_PROBE to run it, for tuning: "1" for the pilot, "lazy" for one that flies a slow circle with every gun going).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_plays()
    {
        string? mode = Environment.GetEnvironmentVariable("TIMEPILOT84_PROBE");
        if (mode is null) return;
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "timepilot84-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.StartGame();
            g.SetLives(99);
            var deaths = new Dictionary<string, int>();
            var levels = new List<string>();
            var doing = new Dictionary<string, int>();
            var last = g.Phase;
            int wave = 1, t = 0;
            double start = 0;
            for (; t < 60 * 900 && g.Wave <= 10 && g.State == GameState.Playing; t++)
            {
                var c = mode == "lazy" ? Circle(t) : g.Pilot.Decide(g);
                g.Tick(c);
                if (g.Phase == Phase.Play) doing[g.Pilot.Doing] = doing.GetValueOrDefault(g.Pilot.Doing) + 1;
                if (g.Phase == Phase.Dying && last != Phase.Dying) deaths[$"L{g.Wave} {g.Cause}"] = deaths.GetValueOrDefault($"L{g.Wave} {g.Cause}") + 1;
                if (g.Phase == Phase.Clear && last != Phase.Clear) levels.Add($"L{wave}:{(t - start) / 60:0}s");
                if (g.Wave != wave) { wave = g.Wave; start = t; }
                last = g.Phase;
            }
            output.WriteLine($"seed {seed}: zone {g.Wave} after {t / 60:0}s score {g.Score} (now {g.Kills} down{(g.Carrier != null ? ", carrier out" : "")}) | {string.Join(" ", levels)}");
            output.WriteLine("   deaths " + string.Join(", ", deaths.OrderBy(d => d.Key).Select(d => $"{d.Key} x{d.Value}")));
            output.WriteLine("   doing " + string.Join(", ", doing.OrderByDescending(d => d.Value).Select(d => $"{d.Key} {d.Value / 60}s")));
        }
    }

    /// <summary>A slow circle round to the right, a step every so often, every gun going.</summary>
    private static Controls Circle(int t)
    {
        var v = Vec.FromAngle((t / 45) * Math.PI / 4);
        return new Controls(Math.Sign(Math.Round(v.X)), Math.Sign(Math.Round(v.Y)), true, true);
    }
}
