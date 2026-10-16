using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVATimePilot84.Engine;
using Effect = AVATimePilot84.Engine.Effect;

namespace AVATimePilot84.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVATimePilot84/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Green = 0xFF6AF08A, Orange = 0xFFFF8A2A;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);

    private bool InGame => Engine.State is GameState.Attract or GameState.Playing or GameState.Paused or GameState.GameOver;

    /// <summary>World to screen: the jet always in the middle.</summary>
    private Point S(Vec w) => new(w.X - Engine.Player.Pos.X + Field.Centre.X, w.Y - Engine.Player.Pos.Y + Field.Centre.Y);

    private static bool OnScreen(Point p, double pad) => p.X > -pad && p.X < Field.Width + pad && p.Y > Field.Hud - pad && p.Y < Field.Height + pad;

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF101018), new Rect(0, 0, Field.Width, Field.Height));
        if (InGame)
        {
            using (dc.PushClip(new Rect(0, Field.Hud, Field.Width, Field.Height - Field.Hud)))
            {
                DrawGround(dc, g.Zone, g.Player.Pos, 1);
                if (g.Phase == Phase.Clear) DrawStreaks(dc, g.PhaseTime);
                if (g.Carrier is { } k && !k.Dead) DrawCarrier(dc, S(k.Pos), k, _clock, 1);
                foreach (var e in g.Effects.Where(e => e.Kind == EffectKind.Smoke)) DrawEffect(dc, e);
                foreach (var e in g.Enemies) DrawEnemy(dc, S(e.Pos), e.Kind, e.Heading, e.Flash > 0, _clock + e.T, 1);
                foreach (var r in g.Rockets) DrawRocket(dc, S(r.Pos), r.Heading, r.Hostile);
                foreach (var s in g.Shots) DrawShot(dc, s);
                DrawPlayer(dc);
                DrawLock(dc);
                DrawClouds(dc, g.Zone, g.Player.Pos);
                foreach (var e in g.Effects.Where(e => e.Kind != EffectKind.Smoke)) DrawEffect(dc, e);
                DrawPointer(dc);
            }
        }
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(Field.Width / 2, 360), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(Field.Width / 2, Field.Height - 22), 26, Yellow, center: true);
    }

    // ───────────────────────────── the ground and the clouds ─────────────────────────────

    /// <summary>The zone's ground going by below, in squares pinned to the world so it slides past as the jet flies.</summary>
    private static void DrawGround(DrawingContext dc, Zone z, Vec at, double speed)
    {
        dc.FillRectangle(Brush(z.Ground), new Rect(0, Field.Hud, Field.Width, Field.Height - Field.Hud));
        const double cell = 220;
        double ox = at.X * speed - Field.Centre.X, oy = at.Y * speed - Field.Centre.Y;
        int c0 = (int)Math.Floor(ox / cell) - 1, r0 = (int)Math.Floor(oy / cell) - 1;
        for (int cy = r0; cy < r0 + (int)(Field.Height / cell) + 3; cy++)
        for (int cx = c0; cx < c0 + (int)(Field.Width / cell) + 3; cx++)
        {
            uint q = Hash(cx, cy, z.Name.Length);
            double x = cx * cell - ox, y = cy * cell - oy;
            switch (z.Name)
            {
                case "OCEAN":
                    for (int i = 0; i < 4; i++)
                    {
                        uint w = Hash(cx, cy, i + 20);
                        double wx = x + w % 200, wy = y + (w >> 8) % 200;
                        dc.DrawLine(Pen(z.Detail, 2), new Point(wx, wy), new Point(wx + 18, wy - 3));
                        dc.DrawLine(Pen(z.Detail, 2), new Point(wx + 18, wy - 3), new Point(wx + 34, wy));
                    }
                    if (q % 9 == 0) Island(dc, x + 110, y + 110, 40 + q % 40, q, 0xFFE8D29A, z.Accent);
                    break;
                case "DESERT":
                    for (int i = 0; i < 3; i++)
                    {
                        uint w = Hash(cx, cy, i + 30);
                        double wx = x + w % 180, wy = y + (w >> 8) % 200;
                        var geo = new StreamGeometry();
                        using (var ctx = geo.Open())
                        {
                            ctx.BeginFigure(new Point(wx, wy), false);
                            ctx.QuadraticBezierTo(new Point(wx + 30, wy - 14), new Point(wx + 70, wy));
                        }
                        dc.DrawGeometry(null, Pen(z.Detail, 3), geo);
                    }
                    if (q % 7 == 0)
                    {
                        double mx = x + 60 + q % 100, my = y + 60 + (q >> 8) % 100;
                        Poly(dc, 0xFF7A4A2A, null, 0, new Point(mx - 30, my + 26), new Point(mx - 4, my + 30), new Point(mx + 40, my + 20), new Point(mx + 34, my - 4), new Point(mx - 24, my - 2));
                        Poly(dc, z.Accent, 0xFF5A3A22, 1.5, new Point(mx - 30, my), new Point(mx - 20, my - 24), new Point(mx + 24, my - 26), new Point(mx + 34, my - 4), new Point(mx - 4, my + 4));
                    }
                    break;
                case "ICE FIELDS":
                    for (int i = 0; i < 2; i++)
                    {
                        uint w = Hash(cx, cy, i + 40);
                        double fx = x + 50 + w % 120, fy = y + 50 + (w >> 8) % 120, r = 20 + w % 40;
                        var pts = Enumerable.Range(0, 7).Select(k => { double a = k * Math.PI * 2 / 7 + (w % 10) * 0.1; double rr = r * (0.7 + ((w >> (k * 3)) % 8) / 20.0); return new Point(fx + Math.Cos(a) * rr, fy + Math.Sin(a) * rr * 0.8); }).ToArray();
                        Poly(dc, z.Accent, null, 0, pts.Select(p => new Point(p.X + 5, p.Y + 6)).ToArray());
                        Poly(dc, z.Detail, 0xFFC8DCEA, 1, pts);
                    }
                    break;
                case "CITY":
                    for (int by = 0; by < 2; by++)
                    for (int bx = 0; bx < 2; bx++)
                    {
                        uint w = Hash(cx * 2 + bx, cy * 2 + by, 50);
                        double rx = x + bx * 110 + 14, ry = y + by * 110 + 14, h = 4 + w % 10;
                        dc.FillRectangle(Brush(0xFF2A2E36), new Rect(rx + h, ry + h, 82, 82));
                        dc.FillRectangle(Brush(z.Detail - (w % 3) * 0x00080808), new Rect(rx, ry, 82, 82));
                        for (int k = 0; k < 6; k++)
                        {
                            uint l = Hash((int)w, k, 51);
                            if (l % 3 == 0) dc.FillRectangle(Brush(z.Accent), new Rect(rx + 6 + l % 66, ry + 6 + (l >> 8) % 66, 4, 4));
                        }
                    }
                    break;
                default:
                    for (int i = 0; i < 6; i++)
                    {
                        uint w = Hash(cx, cy, i + 60);
                        dc.FillRectangle(Brush(i % 3 == 0 ? 0xFFFFFFFF : 0x90C0C8FF), new Rect(x + w % 220, y + (w >> 8) % 220, 2, 2));
                    }
                    if (q % 11 == 0)
                    {
                        double px = x + 110, py = y + 110, r = 30 + q % 50;
                        dc.DrawEllipse(Brush(z.Detail), null, new Point(px, py), r, r);
                        dc.DrawEllipse(Brush(0x40000000), null, new Point(px + r * 0.3, py + r * 0.3), r * 0.8, r * 0.8);
                        dc.DrawEllipse(Brush(z.Accent), null, new Point(px - r * 0.3, py - r * 0.2), r * 0.25, r * 0.2);
                    }
                    break;
            }
        }
    }

    private static void Island(DrawingContext dc, double x, double y, double r, uint q, uint sand, uint green)
    {
        var pts = Enumerable.Range(0, 9).Select(k => { double a = k * Math.PI * 2 / 9; double rr = r * (0.75 + ((q >> (k * 2)) % 6) / 20.0); return (a, rr); }).ToList();
        Poly(dc, sand, null, 0, pts.Select(p => new Point(x + Math.Cos(p.a) * (p.rr + 8), y + Math.Sin(p.a) * (p.rr + 8) * 0.8)).ToArray());
        Poly(dc, green, null, 0, pts.Select(p => new Point(x + Math.Cos(p.a) * p.rr, y + Math.Sin(p.a) * p.rr * 0.8)).ToArray());
        dc.DrawEllipse(Brush(0xFF3A7A3A), null, new Point(x - r * 0.2, y - r * 0.1), r * 0.3, r * 0.2);
    }

    /// <summary>Clouds above everything, rushing by faster than the ground, to give height.</summary>
    private static void DrawClouds(DrawingContext dc, Zone z, Vec at)
    {
        if (z.Name == "ORBIT") return;
        const double cell = 520;
        double ox = at.X * 1.4 - Field.Centre.X, oy = at.Y * 1.4 - Field.Centre.Y;
        int c0 = (int)Math.Floor(ox / cell) - 1, r0 = (int)Math.Floor(oy / cell) - 1;
        for (int cy = r0; cy < r0 + 4; cy++)
        for (int cx = c0; cx < c0 + 5; cx++)
        {
            uint q = Hash(cx, cy, 77);
            if (q % 3 != 0) continue;
            double x = cx * cell - ox + q % 300, y = cy * cell - oy + (q >> 9) % 300;
            for (int i = 0; i < 5; i++)
                dc.DrawEllipse(Brush(0x50FFFFFF), null, new Point(x + i * 26 - 52, y + Math.Sin(i * 1.7 + q) * 10), 34 + (i % 2) * 10, 22 + (i % 3) * 4);
        }
    }

    /// <summary>The rush between zones: streaks flying out from the middle.</summary>
    private static void DrawStreaks(DrawingContext dc, double t)
    {
        for (int i = 0; i < 40; i++)
        {
            uint q = Hash(i, 3, 9);
            double a = q % 628 / 100.0, r0 = (q >> 10) % 400 + t * 600 % 400, len = 30 + t * 60;
            var c = new Point(Field.Centre.X, Field.Centre.Y);
            dc.DrawLine(Pen(Alpha(0xFFFFFFFF, Math.Min(1, t)), 2), new Point(c.X + Math.Cos(a) * r0, c.Y + Math.Sin(a) * r0), new Point(c.X + Math.Cos(a) * (r0 + len), c.Y + Math.Sin(a) * (r0 + len)));
        }
    }

    // ───────────────────────────── aircraft ─────────────────────────────

    /// <summary>Points given nose up about (0, 0), turned to a heading and placed.</summary>
    private static Point[] Turned(Point at, double heading, double k, params (double X, double Y)[] pts)
    {
        double c = Math.Cos(heading), s = Math.Sin(heading);
        return pts.Select(p => new Point(at.X + (p.X * c - p.Y * s) * k, at.Y + (p.X * s + p.Y * c) * k)).ToArray();
    }

    private void DrawPlayer(DrawingContext dc)
    {
        var g = Engine;
        var p = g.Player;
        if (!p.Alive) return;
        if (p.Safe > 0 && g.Phase == Phase.Play && (int)(_clock * 12) % 2 == 0) return;
        DrawJet(dc, new Point(Field.Centre.X, Field.Centre.Y), Field.HeadingAngle(p.Heading), p.Burner, 1, g.Phase == Phase.Clear);
    }

    /// <summary>The player's jet: a delta with a bubble canopy and twin fins, its burner flickering.</summary>
    private static void DrawJet(DrawingContext dc, Point at, double heading, double burner, double k, bool boost = false, bool shadow = true)
    {
        double flick = 0.7 + 0.3 * Math.Sin(burner * 40);
        double flame = (boost ? 26 : 12) * flick;
        Poly(dc, 0xC0FF9A3A, null, 0, Turned(at, heading, k, (-4, 16), (0, 16 + flame), (4, 16)));
        Poly(dc, 0xFFFFE8A0, null, 0, Turned(at, heading, k, (-2, 16), (0, 16 + flame * 0.5), (2, 16)));
        // Shadow on the ground below.
        if (shadow) Poly(dc, 0x30000000, null, 0, Turned(new Point(at.X + 18 * k, at.Y + 26 * k), heading, k * 0.9, (0, -22), (18, 12), (6, 14), (-6, 14), (-18, 12)));
        Poly(dc, 0xFFD8DCE4, 0xFF4A5060, 1.5, Turned(at, heading, k, (0, -24), (5, -10), (20, 10), (20, 14), (6, 12), (5, 17), (-5, 17), (-6, 12), (-20, 14), (-20, 10), (-5, -10)));
        Poly(dc, 0xFF5A6A8A, null, 0, Turned(at, heading, k, (6, 10), (9, 16), (5, 16)));
        Poly(dc, 0xFF5A6A8A, null, 0, Turned(at, heading, k, (-6, 10), (-9, 16), (-5, 16)));
        Poly(dc, 0xFF4AA0E0, 0xFF1A3A5A, 1, Turned(at, heading, k, (0, -16), (3, -8), (2, -2), (-2, -2), (-3, -8)));
        Poly(dc, 0xFFE04A3A, null, 0, Turned(at, heading, k, (-14, 11), (-18, 11), (-18, 13), (-14, 13)));
        Poly(dc, 0xFFE04A3A, null, 0, Turned(at, heading, k, (14, 11), (18, 11), (18, 13), (14, 13)));
    }

    private static void DrawEnemy(DrawingContext dc, Point at, EnemyKind kind, double heading, bool flash, double t, double k)
    {
        if (!OnScreen(at, 60)) return;
        switch (kind)
        {
            case EnemyKind.Interceptor:
                Poly(dc, 0x30000000, null, 0, Turned(new Point(at.X + 14, at.Y + 20), heading, k * 0.8, (0, -18), (14, 10), (-14, 10)));
                Poly(dc, flash ? White : 0xFFC0383A, 0xFF5A1214, 1.5, Turned(at, heading, k, (0, -20), (4, -6), (16, 8), (16, 12), (4, 10), (6, 16), (-6, 16), (-4, 10), (-16, 12), (-16, 8), (-4, -6)));
                Poly(dc, 0xFF2A2A2A, null, 0, Turned(at, heading, k, (0, -12), (2, -5), (-2, -5)));
                Poly(dc, 0xC0FFB04A, null, 0, Turned(at, heading, k, (-3, 16), (0, 22 + Math.Sin(t * 30) * 2), (3, 16)));
                break;
            case EnemyKind.Drone:
                dc.DrawEllipse(Brush(0x30000000), null, new Point(at.X + 10, at.Y + 14), 11 * k, 7 * k);
                Poly(dc, flash ? White : 0xFFE88A2A, 0xFF7A3A0A, 1, Turned(at, heading, k, (0, -12), (12, 6), (6, 4), (0, 8), (-6, 4), (-12, 6)));
                dc.DrawEllipse(Brush(0xFFFFE06A), null, at, 4 * k, 4 * k);
                break;
            default:
                Poly(dc, 0x30000000, null, 0, Turned(new Point(at.X + 18, at.Y + 26), heading, k * 0.9, (0, -24), (30, 6), (-30, 6)));
                Poly(dc, flash ? White : 0xFF5A6A4A, 0xFF2A3220, 1.5, Turned(at, heading, k, (0, -28), (6, -18), (32, -2), (32, 6), (8, 6), (6, 22), (14, 26), (-14, 26), (-6, 22), (-8, 6), (-32, 6), (-32, -2), (-6, -18)));
                foreach (double ex in new[] { -22.0, -12, 12, 22 })
                    Poly(dc, 0xFF3A4430, null, 0, Turned(at, heading, k, (ex - 3, -6), (ex + 3, -6), (ex + 3, 4), (ex - 3, 4)));
                Poly(dc, 0xFFC8D040, null, 0, Turned(at, heading, k, (0, -22), (3, -16), (-3, -16)));
                break;
        }
    }

    /// <summary>The carrier: a dark arrowhead of a flying wing, its turrets turning to follow the jet, its core shielded until they're gone.</summary>
    private static void DrawCarrier(DrawingContext dc, Point at, Carrier k, double clock, double scale)
    {
        if (!OnScreen(at, 260)) return;
        double h = k.Heading, L = Carrier.HalfLength, W = Carrier.HalfSpan;
        Poly(dc, k.Flash > 0 ? 0xFF8A8A9A : 0xFF3A3E4A, 0xFF1A1C24, 2, Turned(at, h, scale, (0, -L - 10), (40, -L + 30), (W + 20, L - 6), (W + 10, L + 6), (60, L - 6), (24, L + 14), (-24, L + 14), (-60, L - 6), (-W - 10, L + 6), (-W - 20, L - 6), (-40, -L + 30)));
        // Panel lines and engine glow.
        foreach (double x in new[] { -100.0, -50, 50, 100 })
            dc.DrawLine(Pen(0xFF2A2E38, 1.5), Turned(at, h, scale, (x * 0.4, -L + 30))[0], Turned(at, h, scale, (x, L - 4))[0]);
        foreach (double x in new[] { -16.0, 16 })
            dc.DrawEllipse(Brush(Alpha(0xFFFF8A3A, 0.6 + 0.3 * Math.Sin(clock * 20 + x))), null, Turned(at, h, scale, (x, L + 18))[0], 7 * scale, 7 * scale);
        // The core.
        var core = at;
        if (k.Open)
        {
            double pulse = 0.6 + 0.4 * Math.Sin(clock * 8);
            dc.DrawEllipse(Brush(Alpha(0xFFFF6A2A, pulse)), Pen(0xFFFFD06A, 2), core, Carrier.CoreRadius * scale, Carrier.CoreRadius * scale);
            dc.DrawEllipse(Brush(0xFFFFF0B0), null, core, 9 * scale, 9 * scale);
        }
        else
        {
            dc.DrawEllipse(Brush(0xFF6A1A1A), Pen(0xFF2A0A0A, 2), core, Carrier.CoreRadius * scale * 0.8, Carrier.CoreRadius * scale * 0.8);
            dc.DrawEllipse(null, Pen(Alpha(0xFF6AC8FF, 0.5 + 0.3 * Math.Sin(clock * 5)), 3), core, Carrier.CoreRadius * scale, Carrier.CoreRadius * scale);
        }
        foreach (var t in k.Turrets)
        {
            var tp = Turned(at, h, scale, (t.Mount.X, t.Mount.Y))[0];
            if (t.Dead)
            {
                dc.DrawEllipse(Brush(0xFF1A1A1A), Pen(0xFF4A2A1A, 2), tp, 11 * scale, 11 * scale);
                continue;
            }
            dc.DrawEllipse(Brush(t.Flash > 0 ? White : 0xFF6A7080), Pen(0xFF1A1C24, 2), tp, Turret.Radius * scale, Turret.Radius * scale);
            dc.DrawLine(Pen(0xFF1A1C24, 5 * scale), tp, new Point(tp.X + Math.Sin(t.Aim) * 22 * scale, tp.Y - Math.Cos(t.Aim) * 22 * scale));
            dc.DrawEllipse(Brush(0xFFE04A3A), null, tp, 4 * scale, 4 * scale);
        }
    }

    private static void DrawRocket(DrawingContext dc, Point at, double heading, bool hostile)
    {
        if (!OnScreen(at, 20)) return;
        Poly(dc, hostile ? 0xFFE8D8C8 : 0xFFF2F2F2, 0xFF3A3A3A, 1, Turned(at, heading, 1, (0, -9), (2.5, -5), (2.5, 7), (-2.5, 7), (-2.5, -5)));
        Poly(dc, hostile ? 0xFFE03A2A : 0xFF3A8AE0, null, 0, Turned(at, heading, 1, (0, -9), (2.5, -5), (-2.5, -5)));
        Poly(dc, 0xD0FFB04A, null, 0, Turned(at, heading, 1, (-2, 7), (0, 13 + Math.Sin(at.X) * 2), (2, 7)));
    }

    private void DrawShot(DrawingContext dc, Shot s)
    {
        var at = S(s.Pos);
        if (!OnScreen(at, 10)) return;
        if (s.Hostile)
        {
            dc.DrawEllipse(Brush(0x80FF5A8A), null, at, 6, 6);
            dc.DrawEllipse(Brush(0xFFFFD0E0), null, at, 3, 3);
            return;
        }
        var back = s.Vel.Unit * -10;
        dc.DrawLine(Pen(0x80FFE080, 5), new Point(at.X + back.X, at.Y + back.Y), at);
        dc.DrawLine(Pen(0xFFFFFFC0, 2), new Point(at.X + back.X * 0.8, at.Y + back.Y * 0.8), at);
    }

    /// <summary>Brackets closing round whatever a missile would chase.</summary>
    private void DrawLock(DrawingContext dc)
    {
        var g = Engine;
        if (g.Where(g.Locked) is not { } w) return;
        var at = S(w);
        double r = 26 + Math.Sin(_clock * 10) * 3, a = _clock * 2;
        uint c = g.Player.MissileIn <= 0 ? Green : 0xFF3A8A4A;
        for (int i = 0; i < 4; i++)
        {
            double b = a + i * Math.PI / 2;
            var p = new Point(at.X + Math.Cos(b) * r, at.Y + Math.Sin(b) * r);
            dc.DrawLine(Pen(c, 2.5), p, new Point(p.X + Math.Cos(b + 2.4) * 10, p.Y + Math.Sin(b + 2.4) * 10));
            dc.DrawLine(Pen(c, 2.5), p, new Point(p.X + Math.Cos(b - 2.4) * 10, p.Y + Math.Sin(b - 2.4) * 10));
        }
        Text(dc, "LOCK", new Point(at.X, at.Y + r + 14), 16, c, center: true);
    }

    /// <summary>An arrow at the edge of the screen pointing to the carrier when it's out of sight.</summary>
    private void DrawPointer(DrawingContext dc)
    {
        var g = Engine;
        if (g.Carrier is not { Dead: false } k || g.Phase != Phase.Play) return;
        var at = S(k.Pos);
        if (OnScreen(at, -40)) return;
        var d = (k.Pos - g.Player.Pos).Unit;
        double cx = Field.Centre.X, cy = Field.Centre.Y, tx = d.X != 0 ? (Field.Width / 2 - 30) / Math.Abs(d.X) : 1e9, ty = d.Y != 0 ? ((Field.Height - Field.Hud) / 2 - 30) / Math.Abs(d.Y) : 1e9;
        double t = Math.Min(tx, ty);
        var p = new Point(cx + d.X * t, cy + d.Y * t);
        double a = d.Angle;
        if ((int)(_clock * 4) % 2 == 0)
            Poly(dc, Red, 0xFF5A1010, 1.5, Turned(p, a, 1, (0, -16), (11, 6), (0, 1), (-11, 6)));
    }

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life;
        if (!InGame) return;
        var c = S(e.Pos);
        if (!OnScreen(c, 80)) return;
        switch (e.Kind)
        {
            case EffectKind.Boom:
            case EffectKind.BigBoom:
            {
                double r = (e.Kind == EffectKind.BigBoom ? 18 + 50 * t : 8 + 26 * t) * e.Size;
                dc.DrawEllipse(Brush(Alpha(0xFF3A3A3A, 0.5 * (1 - t))), null, new Point(c.X + t * 10, c.Y + t * 14), r * 1.1, r);
                dc.DrawEllipse(Brush(Alpha(0xFFFF8A2A, 1 - t)), null, c, r * 0.8, r * 0.8);
                dc.DrawEllipse(Brush(Alpha(0xFFFFF0A0, 1 - t)), null, c, r * 0.4, r * 0.4);
                break;
            }
            case EffectKind.Smoke:
                dc.DrawEllipse(Brush(Alpha(0xFFD0D0D0, 0.5 * (1 - t))), null, c, 3 + 6 * t, 3 + 6 * t);
                break;
            case EffectKind.Score:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 24), 22, e.Colour, center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 30), 30, e.Colour, center: true);
                break;
        }
    }

    // ───────────────────────────── the score bar, and the screens ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF12141C), new Rect(0, 0, Field.Width, Field.Hud));
        dc.FillRectangle(Brush(0xFF3A5A8A), new Rect(0, Field.Hud - 3, Field.Width, 3));
        Text(dc, "SCORE", new Point(16, 16), 20, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(86, 16), 30, Yellow);
        Text(dc, "HI", new Point(Field.Width - 140, 16), 20, Dim, right: true);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(Field.Width - 16, 16), 28, Dim, right: true);
        if (!InGame) return;
        int spare = Math.Max(0, g.Lives - (g.Player.Alive ? 1 : 0));
        for (int i = 0; i < Math.Min(spare, 6); i++) DrawJet(dc, new Point(28 + i * 26, 44), 0, 0, 0.55, shadow: false);
        Text(dc, $"ZONE {g.Wave}  {g.Zone.Name}", new Point(512, 16), 24, 0xFFB0C8F0, center: true);
        // How many more until the carrier comes, then the carrier's own state.
        double bx = 380, bw = 264;
        dc.FillRectangle(Brush(0xFF0A0A10), new Rect(bx, 36, bw, 14), 3);
        if (g.Carrier is { } k)
        {
            int left = k.Turrets.Count(t => !t.Dead);
            Text(dc, k.Open ? "CORE OPEN" : $"TURRETS {left}", new Point(bx + bw / 2, 43), 18, Red, center: true);
        }
        else
        {
            dc.FillRectangle(Brush(0xFF3A8AE0), new Rect(bx + 2, 38, (bw - 4) * Math.Min(1, g.Kills / (double)GameEngine.KillQuota), 10), 2);
            Text(dc, $"{g.Kills}/{GameEngine.KillQuota}", new Point(bx + bw + 10, 43), 18, Dim);
        }
        bool ready = g.Player.MissileIn <= 0;
        Text(dc, "MISSILE", new Point(Field.Width - 16, 44), 18, ready ? Green : 0xFF3A6A4A, right: true);
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2, cy = 230;
        var (w, h) = (Math.Max(msg.Length * 26, (g.SubMessage?.Length ?? 0) * 15) + 60, g.SubMessage != null ? 106.0 : 70.0);
        dc.FillRectangle(Brush(0xC0101420), new Rect(cx - w / 2, cy - 46, w, h), 6);
        dc.DrawRectangle(null, Pen(0xFF3A5A8A, 1.5), new Rect(cx - w / 2, cy - 46, w, h), 6);
        Text(dc, msg, new Point(cx, cy - 12), msg.Length > 14 ? 42 : 54, g.Phase == Phase.Dying ? Red : Yellow, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 34), 26, White, center: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        using (dc.PushClip(new Rect(0, Field.Hud, Field.Width, Field.Height - Field.Hud)))
        {
            DrawGround(dc, Zone.All[0], new Vec(_clock * 60, -_clock * 140), 1);
            DrawClouds(dc, Zone.All[0], new Vec(_clock * 60, -_clock * 140));
        }
        dc.FillRectangle(Brush(0x70000010), new Rect(0, Field.Hud, Field.Width, Field.Height - Field.Hud));
        string title = "AVA TIME PILOT '84";
        for (int i = 0; i < title.Length; i++)
        {
            double bob = Math.Sin(_clock * 3 + i * 0.5) * 4;
            Text(dc, title[i].ToString(), new Point(cx - title.Length * 22 + 22 + i * 44, 126 + bob), 92, i % 2 == 0 ? 0xFF8AC8FF : 0xFFE8F2FF, center: true);
        }
        Text(dc, "AFTER THE 1984 ARCADE CLASSIC", new Point(cx, 188), 28, 0xFFB0C8F0, center: true);
        // The jet with an interceptor on its tail.
        double a = _clock * 0.6;
        DrawJet(dc, new Point(cx + Math.Cos(a) * 80, 268 + Math.Sin(a) * 30), a + Math.PI, _clock, 1.6);
        DrawEnemy(dc, new Point(cx + Math.Cos(a - 0.9) * 80, 268 + Math.Sin(a - 0.9) * 30), EnemyKind.Interceptor, a - 0.9 + Math.PI, false, _clock, 1.3);
        Text(dc, "DOWN 40 OF THE ENEMY AND THE ZONE'S CARRIER COMES OUT TO FIGHT", new Point(cx, 340), 24, White, center: true);
        (string Name, string Points, Action<double> Draw)[] legend =
        [
            ("INTERCEPTOR", GameEngine.InterceptorPoints.ToString(), x => DrawEnemy(dc, new Point(x, 404), EnemyKind.Interceptor, 0, false, _clock, 1.5)),
            ("DRONE", $"{GameEngine.DronePoints}", x => DrawEnemy(dc, new Point(x, 404), EnemyKind.Drone, 0, false, _clock, 1.6)),
            ("GUNSHIP", GameEngine.GunshipPoints.ToString(), x => DrawEnemy(dc, new Point(x, 404), EnemyKind.Gunship, 0, false, _clock, 1.1)),
            ("ROCKET", GameEngine.RocketPoints.ToString(), x => DrawRocket(dc, new Point(x, 404), 0, true)),
            ("TURRET", GameEngine.TurretPoints.ToString(), x =>
            {
                dc.DrawEllipse(Brush(0xFF6A7080), Pen(0xFF1A1C24, 2), new Point(x, 404), 18, 18);
                dc.DrawLine(Pen(0xFF1A1C24, 6), new Point(x, 404), new Point(x + 14, 384));
                dc.DrawEllipse(Brush(0xFFE04A3A), null, new Point(x, 404), 5, 5);
            }),
            ("CARRIER", GameEngine.CarrierPoints.ToString(), x =>
            {
                Poly(dc, 0xFF3A3E4A, 0xFF1A1C24, 2, new Point(x, 380), new Point(x + 40, 422), new Point(x, 414), new Point(x - 40, 422));
                dc.DrawEllipse(Brush(0xFFFF6A2A), null, new Point(x, 406), 6, 6);
            }),
        ];
        for (int i = 0; i < legend.Length; i++)
        {
            double lx = cx - 375 + i * 150;
            legend[i].Draw(lx);
            Text(dc, legend[i].Name, new Point(lx, 450), 20, 0xFFB0C8F0, center: true);
            Text(dc, legend[i].Points, new Point(lx, 474), 22, Yellow, center: true);
        }
        Text(dc, $"ALL {GameEngine.FormationSize} DRONES IN A FORMATION: {GameEngine.FormationBonus} - KNOCK OUT THE TURRETS TO OPEN THE CORE", new Point(cx, 512), 21, 0xFFB0C8F0, center: true);
        if ((int)(_clock * 2) % 2 == 0)
            Text(dc, "PRESS ENTER OR 1 TO PLAY", new Point(cx, 580), 46, Yellow, center: true);
        Text(dc, "ARROWS: THE WAY TO FLY    SPACE: GUNS    X OR SHIFT: HOMING MISSILE (AT WHAT'S LOCKED)", new Point(cx, 640), 21, 0xFFB0C8F0, center: true);
        Text(dc, $"P: PAUSE    M: SOUND    EXTRA JET EVERY {GameEngine.ExtraLifeEvery}    GAMEPAD: STICK, A, B", new Point(cx, 670), 21, 0xFFB0C8F0, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xFF10141E), new Rect(0, Field.Hud, Field.Width, Field.Height - Field.Hud));
        Text(dc, "TOP ACES", new Point(cx, 140), 68, 0xFF8AC8FF, center: true);
        Text(dc, "RANK NAME    SCORE    ZONE", new Point(cx, 204), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 248 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 710), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xFF10141E), new Rect(0, Field.Hud, Field.Width, Field.Height - Field.Hud));
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
