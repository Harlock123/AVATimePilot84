using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVATimePilot84.Engine;
using AVATimePilot84.Views;
using Controls = AVATimePilot84.Engine.Controls;

namespace AVATimePilot84.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set TIMEPILOT84_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avatimepilot84-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("TIMEPILOT84_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVATimePilot84-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150, bool holdFire = false)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            var move = g.Pilot.Decide(g);
            g.Tick(holdFire ? move with { Fire = false } : move);
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }
    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("ACE", 98500, 7);
        g.HighScores.Insert("AVA", 62300, 4);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 30 && g.State != GameState.HighScores; i++) g.Idle(1);
        Shot(w, c, "high-scores");
    }

    /// <summary>A zone, and the computer at work in it until something worth showing.</summary>
    private static void Fly(int wave, string name, Func<GameEngine, bool> busy, int seed = 3, int seconds = 300, int kills = 0)
    {
        var (w, c, g) = Open(seed);
        g.Go(wave);
        g.SetLives(30);
        g.SetKills(kills);
        Assert.True(PlayUntil(g, c, busy, seconds));
        Shot(w, c, name);
    }

    private static bool Flying(GameEngine g) => g.Phase == Phase.Play && g.Player.Alive && g.Player.Safe <= 0;

    private static int Near(GameEngine g, double r) => g.Enemies.Count(e => (e.Pos - g.Player.Pos).Length < r);

    [AvaloniaFact]
    public void Ocean() => Fly(1, "ocean", g => Flying(g) && Near(g, 420) >= 3 && g.Shots.Count(s => !s.Hostile) >= 3 && g.Shots.Any(s => s.Hostile));

    [AvaloniaFact]
    public void A_missile_locked_on() => Fly(2, "missile", g => Flying(g) && g.Locked is Enemy && g.Rockets.Any(r => !r.Hostile && r.Life < GameEngine.MissileLife - 0.3) && Near(g, 450) >= 2);

    [AvaloniaFact]
    public void A_drone_formation() => Fly(3, "formation", g => Flying(g) && g.Enemies.Count(e => e.Formation >= 0 && (e.Pos - g.Player.Pos).Length < 380) >= 5);

    [AvaloniaFact]
    public void The_carrier() => Fly(4, "carrier", g => Flying(g) && g.Carrier is { Dead: false } k && (k.Pos - g.Player.Pos).Length < 330 && k.Turrets.Count(t => t.Dead) is >= 1 and <= 3 && g.Shots.Count(s => !s.Hostile) >= 2, kills: GameEngine.KillQuota);

    [AvaloniaFact]
    public void The_core_open() => Fly(5, "core", g => Flying(g) && g.Carrier is { Dead: false, Open: true } k && (k.Pos - g.Player.Pos).Length < 330 && g.Locked is Carrier, kills: GameEngine.KillQuota, seconds: 400);

    [AvaloniaFact]
    public void A_gunship_rocket() => Fly(2, "gunship", g => Flying(g) && g.Enemies.Any(e => e.Kind == EnemyKind.Gunship && (e.Pos - g.Player.Pos).Length < 400) && g.Rockets.Any(r => r.Hostile && (r.Pos - g.Player.Pos).Length < 350), seconds: 400);

    [AvaloniaFact]
    public void The_city() => Fly(4, "city", g => Flying(g) && Near(g, 420) >= 3 && g.Shots.Any(s => s.Hostile));

    [AvaloniaFact]
    public void Zone_clear() => Fly(1, "clear", g => g.Phase == Phase.Clear && g.PhaseTime > 0.8, kills: GameEngine.KillQuota);
}
