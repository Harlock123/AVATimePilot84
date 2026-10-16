namespace AVATimePilot84.Engine;

public sealed partial class GameEngine
{
    public const double TurnInterval = 0.055, FireGap = 0.1, ShotSpeed = 680, ShotLife = 0.75;
    public const double MissileGap = 0.9, MissileSpeed = 430, MissileTurn = 4.5, MissileLife = 2.4, LockRange = 650, LockCone = 0.55;
    public const double StartSeconds = 2.4, DyingSeconds = 2.4, SafeSeconds = 2, ClearSeconds = 3.6, Despawn = 1300;
    public const int MaxShots = 8, MaxMissiles = 2, MissileDamage = 3, KillQuota = 40;
    public const int InterceptorPoints = 100, DronePoints = 50, GunshipPoints = 300, RocketPoints = 50, FormationBonus = 1000;
    public const int TurretPoints = 500, CarrierPoints = 5000, ZoneBonus = 1000, ExtraLifeEvery = 30000, FormationSize = 6;

    /// <summary>The zone: 1 for the first. After the fifth the zones come round again, fiercer.</summary>
    public int Wave { get; private set; } = 1;
    public int Score { get; private set; }
    public int Lives { get; private set; }
    public Jet Player { get; private set; } = new();
    public List<Enemy> Enemies { get; } = [];
    public List<Shot> Shots { get; } = [];
    public List<Rocket> Rockets { get; } = [];
    public Carrier? Carrier { get; private set; }
    public Zone Zone => Zone.Of(Wave);
    public int Loop => (Wave - 1) / Zone.All.Length;
    /// <summary>Enemy aircraft downed this zone: at the quota, the carrier comes.</summary>
    public int Kills { get; private set; }
    /// <summary>What a missile fired now would chase: an enemy, a turret, or the carrier itself.</summary>
    public object? Locked { get; private set; }
    public JetPilot Pilot { get; } = new();
    public string Cause { get; private set; } = "";
    public int Bonus { get; private set; }
    /// <summary>For tests: nobody arrives unless a test puts them there, and those put there hold their fire.</summary>
    internal bool Calm { get; set; }

    private int _nextExtra, _nextFormation;
    private double _spawnIn, _formationIn;
    private readonly Dictionary<int, int> _formationHits = [];

    public double Fierce => Math.Min(2.2, 1 + 0.12 * (Wave - 1));
    public int EnemyLimit => Math.Min(9, 4 + Wave);
    public double InterceptorSpeed => Math.Min(290, 200 + 10 * (Wave - 1));
    public double EnemyFireGap => Math.Max(0.9, 2.2 - 0.15 * (Wave - 1));
    public double EnemyShotSpeed => Math.Min(390, 260 + 15 * (Wave - 1));
    public int GunshipLimit => Wave < 2 ? 0 : Math.Min(3, Wave / 2);
    public double RocketSpeed => Math.Min(300, 230 + 8 * (Wave - 1));

    private void NewGame()
    {
        _rng = new Random(_seed);
        Score = 0;
        Lives = 3;
        Wave = 1;
        _nextExtra = ExtraLifeEvery;
        Player = new Jet();
        Reset();
        SetPhase(Phase.Start);
    }

    /// <summary>A clear sky for a new zone: nobody about yet, the count back to nothing.</summary>
    private void Reset()
    {
        Kills = 0;
        Carrier = null;
        Locked = null;
        Enemies.Clear();
        Shots.Clear();
        Rockets.Clear();
        _formationHits.Clear();
        _spawnIn = 1;
        _formationIn = 9;
        Player.Alive = true;
        Player.Safe = SafeSeconds;
    }

    private void BeginWave(int wave)
    {
        Wave = wave;
        Reset();
        SetPhase(Phase.Start);
        Say($"ZONE {wave}: {Zone.Name}", $"DOWN {KillQuota} AND THE CARRIER COMES", StartSeconds + 0.3);
        Play(Sfx.LevelStart);
        Music(true);
    }

    private double Rand(double lo, double hi) => lo + _rng.NextDouble() * (hi - lo);

    private void Simulate(double dt, Controls c)
    {
        switch (Phase)
        {
            case Phase.Start:
                Steer(Player, c, dt);
                if (PhaseTime >= StartSeconds) SetPhase(Phase.Play);
                break;
            case Phase.Play:
                Fly(dt, c);
                if (!Calm) Spawn(dt);
                MoveEnemies(dt);
                MoveCarrier(dt);
                MoveShots(dt);
                Collide();
                break;
            case Phase.Dying:
                MoveEnemies(dt);
                MoveCarrier(dt);
                MoveShots(dt);
                if (PhaseTime >= DyingSeconds)
                {
                    if (Lives <= 0) { GameOver(); return; }
                    // Back in, with the sky round the jet cleared.
                    Shots.RemoveAll(s => s.Hostile);
                    Rockets.RemoveAll(r => r.Hostile);
                    Enemies.RemoveAll(e => (e.Pos - Player.Pos).Length < 500);
                    if (Carrier != null && (Carrier.Pos - Player.Pos).Length < 600) Carrier.Pos = Player.Pos + Vec.FromAngle(Rand(0, 2 * Math.PI)) * 900;
                    Player.Alive = true;
                    Player.Safe = SafeSeconds;
                    SetPhase(Phase.Play);
                }
                break;
            case Phase.Clear:
                // Away to the next zone at full throttle.
                Player.Pos += Player.Velocity * (dt * (1 + PhaseTime * 2));
                Player.Burner += dt;
                MoveShots(dt);
                if (PhaseTime >= 1.2 && PhaseTime - dt < 1.2 && Bonus > 0)
                {
                    Score += Bonus;
                    CheckExtra();
                }
                if (PhaseTime >= ClearSeconds) BeginWave(Wave + 1);
                break;
        }
        PhaseTime += dt;
    }

    // ───────────────────────────── the jet ─────────────────────────────

    /// <summary>The heading the stick asks for (one of sixteen, from eight stick ways), or null when it's centred.</summary>
    public static int? Wanted(Controls c)
    {
        if (c.Dx == 0 && c.Dy == 0) return null;
        double a = new Vec(c.Dx, c.Dy).Angle;
        int h = (int)Math.Round(a / (2 * Math.PI / Field.Headings));
        return ((h % Field.Headings) + Field.Headings) % Field.Headings;
    }

    /// <summary>One step of flying: banking a step at a time the short way round towards where the stick points, and always going forward.</summary>
    public static void Steer(Jet p, Controls c, double dt)
    {
        p.TurnIn -= dt;
        if (Wanted(c) is { } want && want != p.Heading && p.TurnIn <= 0)
        {
            int diff = ((want - p.Heading) % Field.Headings + Field.Headings) % Field.Headings;
            p.Heading = (p.Heading + (diff <= Field.Headings / 2 ? 1 : -1) + Field.Headings) % Field.Headings;
            p.TurnIn = TurnInterval;
        }
        p.Pos += p.Velocity * dt;
        p.Burner += dt;
    }

    private void Fly(double dt, Controls c)
    {
        var p = Player;
        p.Safe = Math.Max(0, p.Safe - dt);
        p.FireIn -= dt;
        p.MissileIn -= dt;
        Steer(p, c, dt);
        var was = Locked;
        Locked = FindLock();
        if (Locked != null && Locked != was) Play(Sfx.Lock);
        if (c.Fire && p.FireIn <= 0 && Shots.Count(s => !s.Hostile) < MaxShots)
        {
            Shots.Add(new Shot { Pos = p.Pos + p.Dir * 16, Vel = p.Dir * ShotSpeed + p.Velocity * 0.3, Life = ShotLife });
            p.FireIn = FireGap;
            Play(Sfx.Fire);
        }
        if (c.Missile && p.MissileIn <= 0 && Rockets.Count(r => !r.Hostile) < MaxMissiles)
        {
            Rockets.Add(new Rocket { Pos = p.Pos + p.Dir * 14, Heading = Field.HeadingAngle(p.Heading), Speed = MissileSpeed, Turn = MissileTurn, Life = MissileLife, Target = Locked });
            p.MissileIn = MissileGap;
            Play(Sfx.Launch);
        }
    }

    /// <summary>The best thing ahead for a missile: the nearest to the nose, within range and a cone either side of it.</summary>
    public object? FindLock()
    {
        var p = Player;
        double nose = Field.HeadingAngle(p.Heading), best = double.MaxValue;
        object? pick = null;
        foreach (var (thing, at) in Targets())
        {
            var d = at - p.Pos;
            double dist = d.Length, off = Math.Abs(Field.AngleDiff(nose, d.Angle));
            if (dist > LockRange || dist < 40 || off > LockCone) continue;
            double score = off * 400 + dist;
            if (score < best) { best = score; pick = thing; }
        }
        return pick;
    }

    /// <summary>Everything that can be shot at, and where it is.</summary>
    public IEnumerable<(object Thing, Vec At)> Targets()
    {
        foreach (var e in Enemies.Where(e => !e.Dead)) yield return (e, e.Pos);
        if (Carrier is { Dead: false } k)
        {
            foreach (var t in k.Turrets.Where(t => !t.Dead)) yield return (t, k.At(t.Mount));
            if (k.Open) yield return (k, k.Pos);
        }
    }

    /// <summary>Where a target is now, or null if it's gone.</summary>
    public Vec? Where(object? thing) => thing switch
    {
        Enemy { Dead: false } e when Enemies.Contains(e) => e.Pos,
        Turret { Dead: false } t when Carrier is { Dead: false } k && k.Turrets.Contains(t) => k.At(t.Mount),
        Carrier { Dead: false } k when k == Carrier => k.Pos,
        _ => null,
    };

    // ───────────────────────────── the enemy ─────────────────────────────

    /// <summary>More of the enemy, out of sight round the jet: interceptors (and gunships, from zone 2), and now and then a formation of drones.</summary>
    private void Spawn(double dt)
    {
        var p = Player;
        if ((_spawnIn -= dt) <= 0)
        {
            _spawnIn = 0.8;
            int limit = Carrier != null ? EnemyLimit / 2 : EnemyLimit;
            if (Enemies.Count(e => e.Formation < 0) < limit)
            {
                bool gunship = Enemies.Count(e => e.Kind == EnemyKind.Gunship) < GunshipLimit && _rng.NextDouble() < 0.25;
                var at = p.Pos + Vec.FromAngle(Rand(0, 2 * Math.PI)) * Rand(700, 850);
                Enemies.Add(new Enemy
                {
                    Kind = gunship ? EnemyKind.Gunship : EnemyKind.Interceptor, Pos = at, Heading = (p.Pos - at).Angle,
                    Speed = gunship ? 130 : InterceptorSpeed, Hits = gunship ? 3 + Loop : 1, FireIn = Rand(1, 2.5), AimOffset = Offset(),
                });
            }
        }
        if ((_formationIn -= dt) <= 0 && Carrier == null)
        {
            _formationIn = Rand(9, 12);
            double a = Rand(0, 2 * Math.PI);
            var start = p.Pos + Vec.FromAngle(a) * 820;
            double heading = (p.Pos - start).Angle + Rand(-0.25, 0.25);
            var across = Vec.FromAngle(heading + Math.PI / 2);
            var back = Vec.FromAngle(heading) * -1;
            int id = _nextFormation++;
            _formationHits[id] = 0;
            for (int i = 0; i < FormationSize; i++)
            {
                int side = i % 2 == 0 ? 1 : -1, rank = (i + 1) / 2;
                var at = start + across * (side * rank * 34) + back * (rank * 30);
                Enemies.Add(new Enemy { Kind = EnemyKind.Drone, Pos = at, Heading = heading, Speed = 250, Formation = id, FireIn = 99 });
            }
            Play(Sfx.Formation);
        }
        if (Carrier == null && Kills >= KillQuota) LaunchCarrier();
    }

    private Vec Offset() => Vec.FromAngle(Rand(0, Math.PI * 2)) * Rand(60, 150);

    private void LaunchCarrier()
    {
        var p = Player;
        var at = p.Pos + Vec.FromAngle(Field.HeadingAngle(p.Heading) + Rand(-1.2, 1.2)) * 850;
        var k = new Carrier { Pos = at, Heading = (p.Pos - at).Angle, Hits = 16 + 4 * Loop, LaunchIn = 5 };
        int n = Zone.Turrets;
        for (int i = 0; i < n; i++)
        {
            double f = n == 1 ? 0 : i / (double)(n - 1) * 2 - 1;
            k.Turrets.Add(new Turret { Mount = new Vec(f * 110, 30 - Math.Abs(f) * 0 + (i % 2 == 0 ? 10 : 34)), Hits = Turret.Toughness + Loop * 2, FireIn = 1 + i * 0.3 });
        }
        Carrier = k;
        Say("CARRIER!", "KNOCK OUT THE TURRETS, THEN THE CORE", 2.5);
        Play(Sfx.CarrierAlarm);
    }

    private void MoveEnemies(double dt)
    {
        var p = Player;
        foreach (var e in Enemies)
        {
            if (e.Dead) continue;
            e.T += dt;
            e.Flash = Math.Max(0, e.Flash - dt);
            if (!Calm)
            {
                var to = p.Pos - e.Pos;
                double dist = to.Length;
                switch (e.Kind)
                {
                    case EnemyKind.Interceptor:
                        if (e.Passing)
                        {
                            if (dist > 380) { e.Passing = false; e.AimOffset = Offset(); }
                        }
                        else
                        {
                            Turn(e, (p.Pos + e.AimOffset - e.Pos).Angle, 2.3 * Math.Sqrt(Fierce), dt);
                            if (dist < 110) e.Passing = true;
                        }
                        e.FireIn -= dt;
                        if (e.FireIn <= 0 && p.Alive && p.Safe <= 0 && Phase == Phase.Play && dist is > 120 and < 520 && Math.Abs(Field.AngleDiff(e.Heading, to.Angle)) < 0.4)
                        {
                            var aim = p.Pos + p.Velocity * (dist / EnemyShotSpeed * 0.7);
                            Shots.Add(new Shot { Pos = e.Pos, Vel = (aim - e.Pos).Unit * EnemyShotSpeed, Life = 2.2, Hostile = true });
                            e.FireIn = EnemyFireGap * Rand(0.8, 1.2);
                            Play(Sfx.EnemyFire);
                        }
                        break;
                    case EnemyKind.Gunship:
                        Turn(e, to.Angle, 0.8, dt);
                        e.FireIn -= dt;
                        if (e.FireIn <= 0 && p.Alive && p.Safe <= 0 && Phase == Phase.Play && dist < 650)
                        {
                            Rockets.Add(new Rocket { Pos = e.Pos, Heading = to.Angle, Speed = RocketSpeed, Turn = 1.6, Life = 4, Hostile = true });
                            e.FireIn = 4.5 / Fierce * Rand(0.8, 1.2);
                            Play(Sfx.Rocket);
                        }
                        break;
                }
                e.Pos += e.Velocity * dt;
            }
            if ((e.Pos - p.Pos).Length > Despawn) e.Dead = true;
        }
        Enemies.RemoveAll(e => e.Dead);
    }

    private static void Turn(Enemy e, double want, double rate, double dt) =>
        e.Heading += Math.Clamp(Field.AngleDiff(e.Heading, want), -rate * dt, rate * dt);

    /// <summary>The carrier comes round towards the jet, slowly; its turrets fire at it, and it sends out drones.</summary>
    private void MoveCarrier(double dt)
    {
        if (Carrier is not { Dead: false } k) return;
        var p = Player;
        k.Flash = Math.Max(0, k.Flash - dt);
        foreach (var t in k.Turrets) t.Flash = Math.Max(0, t.Flash - dt);
        if (Calm) return;
        var to = p.Pos - k.Pos;
        if (to.Length > 260) k.Heading += Math.Clamp(Field.AngleDiff(k.Heading, to.Angle), -0.3 * dt, 0.3 * dt);
        k.Pos += Vec.FromAngle(k.Heading) * Carrier.Speed * dt;
        foreach (var t in k.Turrets.Where(t => !t.Dead))
        {
            var at = k.At(t.Mount);
            var d = p.Pos - at;
            t.Aim = d.Angle;
            t.FireIn -= dt;
            if (t.FireIn <= 0 && p.Alive && p.Safe <= 0 && Phase == Phase.Play && d.Length < 540)
            {
                var aim = p.Pos + p.Velocity * (d.Length / EnemyShotSpeed * 0.6);
                Shots.Add(new Shot { Pos = at, Vel = (aim - at).Unit * EnemyShotSpeed, Life = 2.4, Hostile = true });
                t.FireIn = 2.8 / Fierce * Rand(0.8, 1.3);
                Play(Sfx.EnemyFire);
            }
        }
        if ((k.LaunchIn -= dt) <= 0 && Phase == Phase.Play)
        {
            k.LaunchIn = 7;
            for (int i = -1; i <= 1; i += 2)
            {
                var at = k.At(new Vec(i * 60, 40));
                Enemies.Add(new Enemy { Kind = EnemyKind.Interceptor, Pos = at, Heading = (p.Pos - at).Angle + i * 0.4, Speed = InterceptorSpeed, FireIn = 2, AimOffset = Offset() });
            }
        }
    }

    // ───────────────────────────── shots and rockets ─────────────────────────────

    private void MoveShots(double dt)
    {
        var p = Player;
        foreach (var s in Shots)
        {
            s.Pos += s.Vel * dt;
            if ((s.Life -= dt) <= 0) s.Dead = true;
        }
        foreach (var r in Rockets)
        {
            if (r.Dead) continue;
            Vec? goal = r.Hostile ? (p.Alive ? p.Pos : null) : Where(r.Target);
            if (goal is { } g) r.Heading += Math.Clamp(Field.AngleDiff(r.Heading, (g - r.Pos).Angle), -r.Turn * dt, r.Turn * dt);
            r.Pos += Vec.FromAngle(r.Heading) * r.Speed * dt;
            if ((r.Smoke -= dt) <= 0)
            {
                r.Smoke = 0.05;
                Effects.Add(new Effect { Kind = EffectKind.Smoke, Pos = r.Pos - Vec.FromAngle(r.Heading) * 8, Life = 0.5 });
            }
            if ((r.Life -= dt) <= 0) r.Dead = true;
        }
        Shots.RemoveAll(s => s.Dead);
        Rockets.RemoveAll(r => r.Dead);
    }

    private void Collide()
    {
        var p = Player;
        // The jet's guns and missiles.
        foreach (var s in Shots.Where(s => !s.Hostile && !s.Dead))
            if (Strike(s.Pos, 3, 1)) s.Dead = true;
        foreach (var r in Rockets.Where(r => !r.Hostile && !r.Dead))
            if (Strike(r.Pos, Rocket.Radius, MissileDamage))
            {
                r.Dead = true;
                Boom(r.Pos, 0.7);
            }
        // Bullets shoot down rockets coming at the jet.
        foreach (var s in Shots.Where(s => !s.Hostile && !s.Dead))
            if (Rockets.FirstOrDefault(r => r.Hostile && !r.Dead && (r.Pos - s.Pos).Length < Rocket.Radius + 5) is { } rocket)
            {
                s.Dead = rocket.Dead = true;
                Boom(rocket.Pos, 0.6);
                Award(RocketPoints, rocket.Pos);
            }
        Shots.RemoveAll(s => s.Dead);
        Rockets.RemoveAll(r => r.Dead);
        if (!p.Alive || p.Safe > 0 || Phase != Phase.Play) return;
        if (Shots.FirstOrDefault(s => s.Hostile && (s.Pos - p.Pos).Length < Jet.Radius + 3) is { } hit) { hit.Dead = true; Lose("shot down"); return; }
        if (Rockets.FirstOrDefault(r => r.Hostile && (r.Pos - p.Pos).Length < Jet.Radius + Rocket.Radius) is { } rk) { rk.Dead = true; Lose("hit by a rocket"); return; }
        if (Enemies.FirstOrDefault(e => !e.Dead && (e.Pos - p.Pos).Length < e.Radius + Jet.Radius - 4) is { } crash)
        {
            crash.Dead = true;
            Boom(crash.Pos, 1);
            Lose("collided");
            return;
        }
        if (Carrier is { Dead: false } k && k.Covers(p.Pos, -6)) Lose("hit the carrier");
    }

    /// <summary>Something of the jet's at a point: does it hit anything, and do the damage if so.</summary>
    private bool Strike(Vec at, double radius, int damage)
    {
        if (Enemies.FirstOrDefault(e => !e.Dead && (e.Pos - at).Length < e.Radius + radius) is { } e)
        {
            HitEnemy(e, damage);
            return true;
        }
        if (Carrier is not { Dead: false } k) return false;
        if (k.Turrets.FirstOrDefault(t => !t.Dead && (k.At(t.Mount) - at).Length < Turret.Radius + radius) is { } t)
        {
            HitTurret(k, t, damage);
            return true;
        }
        // Shots fly on over the wing; the core stops them, behind its shield until the turrets are gone.
        if ((k.Pos - at).Length >= Carrier.CoreRadius + radius) return false;
        if (k.Open) HitCarrier(k, damage);
        else { k.Flash = 0.05; Play(Sfx.Hit); }
        return true;
    }

    private void HitEnemy(Enemy e, int damage)
    {
        e.Hits -= damage;
        e.Flash = 0.1;
        if (e.Hits > 0) { Play(Sfx.Hit); return; }
        e.Dead = true;
        Kills++;
        Boom(e.Pos, e.Kind == EnemyKind.Gunship ? 1.5 : 1);
        Award(e.Kind switch { EnemyKind.Gunship => GunshipPoints, EnemyKind.Drone => DronePoints, _ => InterceptorPoints }, e.Pos);
        if (e.Formation >= 0 && _formationHits.TryGetValue(e.Formation, out int n))
        {
            _formationHits[e.Formation] = ++n;
            if (n == FormationSize)
            {
                Award(FormationBonus, e.Pos + new Vec(0, -30));
                Effects.Add(new Effect { Kind = EffectKind.Word, Pos = e.Pos + new Vec(0, -60), Text = "FORMATION!", Life = 1.4 });
            }
        }
    }

    private void HitTurret(Carrier k, Turret t, int damage)
    {
        t.Hits -= damage;
        t.Flash = 0.1;
        if (!t.Dead) { Play(Sfx.Hit); return; }
        var at = k.At(t.Mount);
        Boom(at, 1.3);
        Award(TurretPoints, at);
        Play(Sfx.TurretDown);
        if (k.Open) Effects.Add(new Effect { Kind = EffectKind.Word, Pos = k.Pos + new Vec(0, -100), Text = "CORE OPEN!", Life = 1.6, Colour = 0xFFFF6A5A });
    }

    private void HitCarrier(Carrier k, int damage)
    {
        k.Hits -= damage;
        k.Flash = 0.1;
        if (k.Hits > 0) { Play(Sfx.Hit); return; }
        k.Dead = true;
        Award(CarrierPoints, k.Pos);
        for (int i = 0; i < 6; i++) Effects.Add(new Effect { Kind = EffectKind.BigBoom, Pos = k.At(new Vec(Rand(-120, 120), Rand(-50, 60))), Life = 1 + i * 0.2, Size = Rand(1, 2) });
        Play(Sfx.BigBoom);
        Bonus = ZoneBonus * Wave;
        // Marked, not removed: this can happen while the shots are being gone through.
        foreach (var s in Shots.Where(s => s.Hostile)) s.Dead = true;
        foreach (var r in Rockets.Where(r => r.Hostile)) r.Dead = true;
        foreach (var e in Enemies) e.Dead = true;
        Locked = null;
        Play(Sfx.LevelClear);
        Say("ZONE CLEAR!", $"BONUS {Bonus}", ClearSeconds);
        SetPhase(Phase.Clear);
    }

    private void Lose(string cause)
    {
        var p = Player;
        Cause = cause;
        p.Alive = false;
        Lives--;
        Effects.Add(new Effect { Kind = EffectKind.BigBoom, Pos = p.Pos, Life = 1.2, Size = 1.4 });
        Play(Sfx.PlayerDown);
        Locked = null;
        SetPhase(Phase.Dying);
        if (Lives <= 0) Say("GAME OVER", null, DyingSeconds);
    }

    private void Boom(Vec at, double size)
    {
        Effects.Add(new Effect { Kind = EffectKind.Boom, Pos = at, Life = 0.6, Size = size });
        Play(Sfx.Boom);
    }

    private void Award(int points, Vec at)
    {
        Score += points;
        Effects.Add(new Effect { Kind = EffectKind.Score, Pos = at + new Vec(0, -18), Text = points.ToString(), Life = 0.8 });
        CheckExtra();
    }

    private void CheckExtra()
    {
        while (Score >= _nextExtra)
        {
            _nextExtra += ExtraLifeEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Straight into a zone, past its name.</summary>
    internal void Go(int wave = 1)
    {
        StartGame();
        if (wave != 1) BeginWave(wave);
        SetPhase(Phase.Play);
        Player.Safe = 0;
        Message = SubMessage = null;
    }

    /// <summary>The jet at the origin, pointing a way.</summary>
    internal void Face(int heading)
    {
        Player.Pos = new Vec(0, 0);
        Player.Heading = heading;
        Player.TurnIn = 0;
        Player.Safe = 0;
    }

    internal Enemy PutEnemy(EnemyKind kind, Vec at, double heading, double speed = 0)
    {
        var e = new Enemy { Kind = kind, Pos = at, Heading = heading, Speed = speed, Hits = kind == EnemyKind.Gunship ? 3 : 1, FireIn = 0 };
        Enemies.Add(e);
        return e;
    }

    internal Carrier PutCarrier(Vec at, double heading)
    {
        LaunchCarrier();
        Carrier!.Pos = at;
        Carrier.Heading = heading;
        Message = SubMessage = null;
        return Carrier;
    }

    internal void SetKills(int n) => Kills = n;
    internal void SetLives(int n) => Lives = n;
    internal void SetScore(int n) => Score = n;
}
