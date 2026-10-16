namespace AVATimePilot84.Engine;

/// <summary>
/// The computer player, for the demonstration (and the tests). It picks something to shoot at (the carrier's
/// turrets first, then its core; otherwise whatever it can bring its nose round to soonest), swings round to
/// where that will be when its bullets get there, and fires when it's lined up; with a lock it lets a missile
/// go. If anything is about to hit it, it takes whichever way of flying keeps it clearest.
/// </summary>
public sealed class JetPilot
{
    /// <summary>What it is up to, for the probe.</summary>
    public string Doing { get; private set; } = "";

    public Controls Decide(GameEngine g)
    {
        var p = g.Player;
        if (g.Phase is not (Phase.Play or Phase.Start) || !p.Alive) { Doing = "waiting"; return default; }

        var target = Pick(g);
        Controls want;
        if (target is { } t)
        {
            // Lead it: where it'll be when a bullet gets there.
            var at = t.At;
            double dist = (at - p.Pos).Length, time = dist / GameEngine.ShotSpeed;
            var lead = at + t.Vel * time - p.Velocity * (time * 0.3);
            double angle = (lead - p.Pos).Angle, off = Math.Abs(Field.AngleDiff(Field.HeadingAngle(p.Heading), angle));
            want = Stick(angle) with
            {
                Fire = off < 0.16 && dist < 560,
                Missile = g.Locked != null && p.MissileIn <= 0,
            };
            Doing = t.Thing switch { Turret => "after a turret", Carrier => "after the core", Enemy { Kind: EnemyKind.Gunship } => "after a gunship", _ => "dogfighting" };
            // Not too close to the carrier's wing.
            if (g.Carrier is { Dead: false } k && (k.Pos - p.Pos).Length < 230)
            {
                want = Stick((p.Pos - k.Pos).Angle) with { Fire = want.Fire, Missile = want.Missile };
                Doing = "clear of the carrier";
            }
        }
        else
        {
            want = default;
            Doing = "cruising";
        }
        return Safe(g, want);
    }

    /// <summary>The stick way (of eight) nearest to an angle.</summary>
    private static Controls Stick(double angle)
    {
        int way = (int)Math.Round(angle / (Math.PI / 4));
        var v = Vec.FromAngle(way * Math.PI / 4);
        return new Controls(Math.Sign(Math.Round(v.X)), Math.Sign(Math.Round(v.Y)));
    }

    private static (object Thing, Vec At, Vec Vel)? Pick(GameEngine g)
    {
        var p = g.Player;
        if (g.Carrier is { Dead: false } k && (k.Pos - p.Pos).Length < 900)
        {
            var turret = k.Turrets.Where(t => !t.Dead).OrderBy(t => (k.At(t.Mount) - p.Pos).Length).FirstOrDefault();
            var kv = Vec.FromAngle(k.Heading) * Carrier.Speed;
            if (turret != null) return (turret, k.At(turret.Mount), kv);
            return (k, k.Pos, kv);
        }
        double nose = Field.HeadingAngle(p.Heading), best = double.MaxValue;
        (object, Vec, Vec)? pick = null;
        foreach (var e in g.Enemies.Where(e => !e.Dead))
        {
            var d = e.Pos - p.Pos;
            double dist = d.Length;
            if (dist > 750) continue;
            // How long to bring the nose round, and to get there.
            double cost = Math.Abs(Field.AngleDiff(nose, d.Angle)) * 260 + dist + (e.Kind == EnemyKind.Gunship ? -120 : 0);
            if (cost < best) { best = cost; pick = (e, e.Pos, e.Velocity); }
        }
        if (pick == null && g.Carrier is { Dead: false } far) return (far, far.Pos, Vec.FromAngle(far.Heading) * Carrier.Speed);
        return pick;
    }

    /// <summary>The way it wants, if that's clear enough; otherwise the clear way nearest to it, or failing that the clearest.</summary>
    private Controls Safe(GameEngine g, Controls want)
    {
        const double enough = 14;
        if (Clearance(g, want) > enough) return want;
        var p = g.Player;
        double wantAngle = GameEngine.Wanted(want) is { } h ? Field.HeadingAngle(h) : Field.HeadingAngle(p.Heading);
        Controls? near = null;
        double nearest = double.MaxValue, most = double.MinValue;
        var clearest = want;
        foreach (var c in Options(want))
        {
            double clear = Clearance(g, c);
            double angle = GameEngine.Wanted(c) is { } ch ? Field.HeadingAngle(ch) : Field.HeadingAngle(p.Heading);
            double off = Math.Abs(Field.AngleDiff(wantAngle, angle));
            if (clear > enough && off < nearest) { nearest = off; near = c; }
            if (clear > most) { most = clear; clearest = c; }
        }
        var pick = near ?? clearest;
        if (pick != want) Doing = "dodging";
        return pick;
    }

    private static IEnumerable<Controls> Options(Controls want)
    {
        yield return want with { Dx = 0, Dy = 0 };
        for (int w = 0; w < 8; w++)
        {
            var v = Vec.FromAngle(w * Math.PI / 4);
            yield return want with { Dx = Math.Sign(Math.Round(v.X)), Dy = Math.Sign(Math.Round(v.Y)) };
        }
    }

    /// <summary>How close the nearest danger comes over the next second or so, flying like this.</summary>
    private static double Clearance(GameEngine g, Controls c)
    {
        var p = g.Player;
        var me = new Jet { Pos = p.Pos, Heading = p.Heading, TurnIn = p.TurnIn };
        double least = double.MaxValue;
        var shots = g.Shots.Where(s => s.Hostile).ToList();
        var rockets = g.Rockets.Where(r => r.Hostile).ToList();
        var enemies = g.Enemies.Where(e => !e.Dead && (e.Pos - p.Pos).Length < 450).ToList();
        for (int t = 1; t <= 50; t++)
        {
            double dt = t * Field.Step;
            GameEngine.Steer(me, c, Field.Step);
            if (t % 2 != 0) continue;
            foreach (var s in shots)
                least = Math.Min(least, (s.Pos + s.Vel * dt - me.Pos).Length - Jet.Radius - 3);
            foreach (var r in rockets)
                least = Math.Min(least, (r.Pos + Vec.FromAngle(r.Heading) * (r.Speed * dt) - me.Pos).Length - Jet.Radius - Rocket.Radius);
            foreach (var e in enemies)
                least = Math.Min(least, (e.Pos + e.Velocity * dt - me.Pos).Length - Jet.Radius - e.Radius);
            if (g.Carrier is { Dead: false } k)
            {
                var kp = k.Pos + Vec.FromAngle(k.Heading) * (Carrier.Speed * dt);
                var ghost = new Carrier { Pos = kp, Heading = k.Heading };
                if (ghost.Covers(me.Pos, 20)) least = Math.Min(least, -20);
            }
        }
        return least;
    }
}
