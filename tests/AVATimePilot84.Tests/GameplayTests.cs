using AVATimePilot84.Engine;

namespace AVATimePilot84.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avatimepilot84-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>Zone 1 in play, the jet at the origin pointing up, nobody about; calm (those put there hold still and hold fire) unless <paramref name="calm"/> is false.</summary>
    private static GameEngine Game(bool calm = true, int wave = 1, int seed = 7, int heading = 0)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = calm };
        g.Go(wave);
        g.Face(heading);
        return g;
    }

    private static readonly Controls Guns = new(Fire: true), Missile = new(Missile: true);

    // ───────────────────────────── the jet ─────────────────────────────

    [Fact]
    public void The_jet_always_flies_and_turns_a_step_at_a_time_the_short_way_round_to_where_the_stick_points()
    {
        var g = Game();
        g.Run(60, default);
        Assert.Equal(0, g.Player.Heading);
        Assert.Equal(-Jet.Speed, g.Player.Pos.Y, 1);
        g.Run(1, new Controls(1, 0));
        Assert.Equal(1, g.Player.Heading);
        g.Run((int)(GameEngine.TurnInterval * 60 * 4) + 4, new Controls(1, 0));
        Assert.Equal(4, g.Player.Heading);
        // From right, down-left is the long way clockwise but the short way back: it goes clockwise through down.
        g.Run(1, new Controls(-1, 1));
        Assert.Equal(5, g.Player.Heading);
        g.Face(1);
        g.Run(1, new Controls(-1, 0));
        Assert.Equal(0, g.Player.Heading);
    }

    [Fact]
    public void The_guns_fire_ahead_never_more_than_eight_in_the_air()
    {
        var g = Game(heading: 4);
        g.Run(1, Guns);
        var s = Assert.Single(g.Shots);
        Assert.True(s.Vel.X > GameEngine.ShotSpeed && Math.Abs(s.Vel.Y) < 1e-6);
        int most = 0;
        for (int i = 0; i < 120; i++) { g.Tick(Guns); most = Math.Max(most, g.Shots.Count); }
        Assert.InRange(most, 6, GameEngine.MaxShots);
    }

    [Fact]
    public void A_missile_locks_on_to_what_is_nearest_the_nose_within_range()
    {
        var g = Game();
        var ahead = g.PutEnemy(EnemyKind.Interceptor, new Vec(40, -400), 0);
        g.PutEnemy(EnemyKind.Interceptor, new Vec(0, 400), 0);
        g.PutEnemy(EnemyKind.Interceptor, new Vec(0, -900), 0);
        g.PutEnemy(EnemyKind.Interceptor, new Vec(300, -300), 0);
        g.Tick(default);
        Assert.Same(ahead, g.Locked);
        g.Enemies.Remove(ahead);
        g.Tick(default);
        Assert.Null(g.Locked);
    }

    [Fact]
    public void A_missile_chases_what_it_was_locked_on_to_and_takes_out_a_gunship_in_one()
    {
        var g = Game();
        var e = g.PutEnemy(EnemyKind.Gunship, new Vec(220, -420), Math.PI / 2, 100);
        g.Tick(Missile);
        Assert.Same(e, g.Rockets.Single().Target);
        g.Run(90, default);
        Assert.True(e.Dead || !g.Enemies.Contains(e));
        Assert.Equal(GameEngine.GunshipPoints, g.Score);
    }

    [Fact]
    public void Without_a_lock_a_missile_flies_straight_and_two_at_most_are_out()
    {
        var g = Game(heading: 4);
        g.Tick(Missile);
        var r = g.Rockets.Single();
        Assert.Null(r.Target);
        g.Run(30, default);
        Assert.Equal(Math.PI / 2, r.Heading, 6);
        g.Run((int)(GameEngine.MissileGap * 60), Missile);
        g.Run((int)(GameEngine.MissileGap * 60), Missile);
        Assert.Equal(GameEngine.MaxMissiles, g.Rockets.Count);
    }

    // ───────────────────────────── the enemy ─────────────────────────────

    [Fact]
    public void An_interceptor_comes_round_onto_the_jet_and_its_shot_brings_it_down()
    {
        var g = Game(calm: false);
        var e = g.PutEnemy(EnemyKind.Interceptor, new Vec(400, 300), 0, g.InterceptorSpeed);
        g.Run(90, default);
        Assert.True(Math.Abs(Field.AngleDiff(e.Heading, (g.Player.Pos - e.Pos).Angle)) < 0.8 || e.Passing);
        int lives = g.Lives;
        g.Enemies.Clear();
        g.Shots.Add(new Shot { Pos = g.Player.Pos + new Vec(60, 0), Vel = new Vec(-300, 0) * 1 + g.Player.Velocity, Life = 2, Hostile = true });
        g.Run(20, default);
        Assert.Equal((Phase.Dying, "shot down", lives - 1), (g.Phase, g.Cause, g.Lives));
        g.PutEnemy(EnemyKind.Interceptor, g.Player.Pos + new Vec(200, 0), 0);
        g.Run((int)(GameEngine.DyingSeconds * 60) + 2, default);
        Assert.Equal(Phase.Play, g.Phase);
        Assert.True(g.Player.Alive && g.Player.Safe > 0);
        Assert.DoesNotContain(g.Enemies, x => (x.Pos - g.Player.Pos).Length < 500);
    }

    [Fact]
    public void Flying_into_an_enemy_downs_both()
    {
        var g = Game();
        var e = g.PutEnemy(EnemyKind.Interceptor, new Vec(0, -40), 0);
        g.Run(10, default);
        Assert.Equal((Phase.Dying, "collided"), (g.Phase, g.Cause));
        Assert.DoesNotContain(e, g.Enemies);
    }

    [Fact]
    public void A_gunship_takes_three_hits_and_fires_homing_rockets_that_the_guns_can_shoot_down()
    {
        var g = Game();
        var e = g.PutEnemy(EnemyKind.Gunship, new Vec(0, -300), 0);
        g.Run(1, Guns);
        g.Run(40, default);
        Assert.Equal(2, e.Hits);
        g = Game(calm: false);
        g.PutEnemy(EnemyKind.Gunship, new Vec(500, 0), 0, 0);
        g.Tick(default);
        var r = g.Rockets.Single(r => r.Hostile);
        double was = Math.Abs(Field.AngleDiff(r.Heading, (g.Player.Pos - r.Pos).Angle));
        g.Run(30, default);
        Assert.True(Math.Abs(Field.AngleDiff(r.Heading, (g.Player.Pos - r.Pos).Angle)) <= was + 0.05);
        // Turn on it and shoot it down.
        g = Game(heading: 4);
        g.Rockets.Add(new Rocket { Pos = new Vec(300, 0), Heading = -Math.PI / 2, Speed = 0, Turn = 0, Life = 9, Hostile = true });
        g.Run(1, Guns);
        g.Run(30, default);
        Assert.Empty(g.Rockets);
        Assert.Equal(GameEngine.RocketPoints, g.Score);
    }

    [Fact]
    public void A_whole_formation_of_drones_downed_is_a_bonus()
    {
        var g = Game(calm: false);
        g.Calm = false;
        // Wait for a formation, then knock them all down by hand.
        for (int i = 0; i < 60 * 15 && !g.Enemies.Any(e => e.Formation >= 0); i++) g.Tick(default);
        var drones = g.Enemies.Where(e => e.Formation >= 0).ToList();
        Assert.Equal(GameEngine.FormationSize, drones.Count);
        Assert.All(drones, d => Assert.Equal(drones[0].Heading, d.Heading, 6));
        g.Calm = true;
        g.Enemies.RemoveAll(e => e.Formation < 0);
        int score = g.Score;
        foreach (var d in drones)
        {
            g.Player.Pos = d.Pos + new Vec(0, 120);
            g.Face(0);
            g.Player.Pos = d.Pos + new Vec(0, 120);
            g.Run(1, Guns);
            g.Run(20, default);
        }
        Assert.Equal(score + GameEngine.FormationSize * GameEngine.DronePoints + GameEngine.FormationBonus, g.Score);
    }

    // ───────────────────────────── the carrier ─────────────────────────────

    [Fact]
    public void Down_the_quota_and_the_carrier_comes()
    {
        var g = Game(calm: false);
        g.SetKills(GameEngine.KillQuota - 1);
        g.Run(10, default);
        Assert.Null(g.Carrier);
        g.PutEnemy(EnemyKind.Drone, new Vec(0, -150), 0);
        g.Calm = true;
        g.Run(1, Guns);
        g.Run(20, default);
        g.Calm = false;
        g.Run(60, default);
        Assert.NotNull(g.Carrier);
        Assert.Equal(g.Zone.Turrets, g.Carrier!.Turrets.Count);
    }

    [Fact]
    public void The_core_is_shielded_until_every_turret_is_knocked_out()
    {
        var g = Game();
        var k = g.PutCarrier(new Vec(0, -300), 0);
        int hits = k.Hits;
        g.Run(1, Guns);
        g.Run(40, default);
        Assert.Equal(hits, k.Hits);
        foreach (var t in k.Turrets.Skip(1)) t.Hits = 0;
        Assert.False(k.Open);
        k.Turrets[0].Hits = 1;
        g.Face(0);
        g.Player.Pos = k.At(k.Turrets[0].Mount) + new Vec(0, 100);
        g.Run(1, Guns);
        g.Run(20, default);
        Assert.True(k.Open);
        Assert.Equal(GameEngine.TurretPoints, g.Score);
        g.Face(0);
        g.Player.Pos = k.Pos + new Vec(0, 160);
        g.Run(1, Guns);
        g.Run(20, default);
        Assert.Equal(hits - 1, k.Hits);
    }

    [Fact]
    public void Shots_fly_on_over_the_wing_away_from_the_turrets_and_the_core()
    {
        var g = Game(heading: 4);
        var k = g.PutCarrier(new Vec(0, 0), 0);
        k.Turrets.Clear();
        k.Turrets.Add(new Turret { Mount = new Vec(0, 300), Hits = 5 });
        g.Player.Pos = new Vec(-260, 50);
        g.Run(1, Guns);
        var s = g.Shots.Single();
        g.Run(25, default);
        Assert.Contains(s, g.Shots);
        Assert.True(s.Pos.X > 40);
    }

    [Fact]
    public void Downing_the_carrier_clears_the_zone_for_a_bonus_and_on_to_the_next()
    {
        var g = Game(wave: 2);
        var k = g.PutCarrier(new Vec(0, -300), 0);
        foreach (var t in k.Turrets) t.Hits = 0;
        k.Hits = 1;
        g.Run(1, Guns);
        g.Run(40, default);
        Assert.Equal(Phase.Clear, g.Phase);
        Assert.Equal(GameEngine.ZoneBonus * 2, g.Bonus);
        g.Run((int)(GameEngine.ClearSeconds * 60) + 2, default);
        Assert.Equal((3, 0), (g.Wave, g.Kills));
        Assert.Null(g.Carrier);
        Assert.Equal(GameEngine.CarrierPoints + 2 * GameEngine.ZoneBonus, g.Score);
    }

    [Fact]
    public void Flying_into_the_carrier_brings_the_jet_down()
    {
        var g = Game();
        g.PutCarrier(new Vec(0, -60), 0);
        g.Run(10, default);
        Assert.Equal((Phase.Dying, "hit the carrier"), (g.Phase, g.Cause));
    }

    // ───────────────────────────── the game ─────────────────────────────

    [Fact]
    public void The_zones_come_round_in_turn_fiercer_each_time_with_more_turrets_on_the_later_carriers()
    {
        var g = Game();
        var names = new List<string>();
        var speeds = new List<double>();
        for (int w = 1; w <= 6; w++)
        {
            g.Go(w);
            names.Add(g.Zone.Name);
            speeds.Add(g.InterceptorSpeed);
        }
        Assert.Equal(["OCEAN", "DESERT", "ICE FIELDS", "CITY", "ORBIT", "OCEAN"], names);
        Assert.True(speeds.Zip(speeds.Skip(1)).All(p => p.Second > p.First));
        Assert.True(Zone.All[^1].Turrets > Zone.All[0].Turrets);
        g.Go(1);
        Assert.Equal(0, g.GunshipLimit);
        g.Go(2);
        Assert.True(g.GunshipLimit > 0);
    }

    [Fact]
    public void Every_thirty_thousand_is_an_extra_jet()
    {
        var g = Game();
        g.SetScore(GameEngine.ExtraLifeEvery - 50);
        int lives = g.Lives;
        g.PutEnemy(EnemyKind.Interceptor, new Vec(0, -200), 0);
        g.Run(1, Guns);
        g.Run(30, default);
        Assert.Equal(lives + 1, g.Lives);
    }

    [Fact]
    public void The_last_jet_lost_ends_the_game_and_a_high_score_asks_for_initials()
    {
        var g = Game();
        g.SetLives(1);
        g.SetScore(99999);
        g.PutEnemy(EnemyKind.Interceptor, new Vec(0, -40), 0);
        g.Idle(5);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 2);
        Assert.Equal(GameState.GameOver, g.State);
        g.Idle(60 * 4);
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (char ch in "ACE") g.Initials!.Type(ch);
        g.Initials!.Finish();
        g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal(("ACE", 99999), (g.HighScores.Entries[0].Initials, g.HighScores.Entries[0].Score));
    }

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores_and_the_demos_vary_the_zone()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        var waves = new List<int>();
        for (int round = 0; round < 2; round++)
        {
            g.Idle((int)(GameEngine.TitleIdleSeconds * 60) + 2);
            Assert.Equal(GameState.Attract, g.State);
            waves.Add(g.Wave);
            for (int i = 0; i < (int)(GameEngine.AttractSeconds * 60) + 2 && g.State == GameState.Attract; i++) g.Idle(1);
            Assert.Equal(GameState.HighScores, g.State);
            g.Idle((int)(GameEngine.HighScoreSeconds * 60) + 2);
            Assert.Equal(GameState.Title, g.State);
        }
        Assert.Equal([1, 2], waves);
    }

    [Fact]
    public void Enter_starts_a_game()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal((1, 3, 0, 0), (g.Wave, g.Lives, g.Score, g.Kills));
    }

    [Fact]
    public void The_up_key_points_the_jet_up_the_screen_and_the_down_key_down_it()
    {
        using var input = new InputHandler();
        var g = Game(heading: 4);
        input.OnKeyDown(Avalonia.Input.Key.Up);
        Assert.Equal(-1, input.Controls.Dy);
        g.Run(60, input.Controls);
        Assert.Equal(0, g.Player.Heading);
        input.OnKeyUp(Avalonia.Input.Key.Up);
        input.OnKeyDown(Avalonia.Input.Key.Down);
        g.Run(60, input.Controls);
        Assert.Equal(8, g.Player.Heading);
    }

    [Fact]
    public void The_computer_player_takes_the_first_zone()
    {
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.Go(1);
            for (int i = 0; i < 60 * 240 && g.Wave == 1 && g.State == GameState.Playing; i++) g.Tick(g.Pilot.Decide(g));
            Assert.True(g.Wave == 2, $"seed {seed}: still in zone 1 with {g.Kills} down");
        }
    }

    [Fact]
    public void Flying_a_circle_with_every_gun_going_soon_loses()
    {
        var g = new GameEngine(2, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(1);
        for (int t = 0; t < 60 * 300 && g.State == GameState.Playing; t++)
        {
            var v = Vec.FromAngle(t / 45 * Math.PI / 4);
            g.Tick(new Controls(Math.Sign(Math.Round(v.X)), Math.Sign(Math.Round(v.Y)), true, true));
        }
        Assert.NotEqual(GameState.Playing, g.State);
        Assert.Equal(1, g.Wave);
    }
}
