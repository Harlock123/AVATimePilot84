namespace AVATimePilot84.Engine;

public readonly record struct Vec(double X, double Y)
{
    public static Vec operator +(Vec a, Vec b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec operator -(Vec a, Vec b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec operator -(Vec a) => new(-a.X, -a.Y);
    public static Vec operator *(Vec a, double k) => new(a.X * k, a.Y * k);
    public double Length => Math.Sqrt(X * X + Y * Y);
    public double LengthSq => X * X + Y * Y;
    public Vec Unit => Length < 1e-9 ? new Vec(0, -1) : this * (1 / Length);

    /// <summary>A unit vector for a heading in radians: 0 is up the screen, turning clockwise.</summary>
    public static Vec FromAngle(double a) => new(Math.Sin(a), -Math.Cos(a));

    /// <summary>This vector's heading, in the same terms as <see cref="FromAngle"/>.</summary>
    public double Angle => Math.Atan2(X, -Y);

    // The generated one would print Unit, whose Unit it would print, and so on for ever.
    public override string ToString() => $"({X:0.#}, {Y:0.#})";
}

public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    /// <summary>The score bar along the top.</summary>
    public const double Hud = 60;
    /// <summary>The jet is always in the middle of the picture below the score bar; the world moves past it.</summary>
    public static readonly Vec Centre = new(Width / 2, (Hud + Height) / 2);
    /// <summary>The jet points one of sixteen ways.</summary>
    public const int Headings = 16;

    public static double HeadingAngle(int h) => h * (2 * Math.PI / Headings);

    /// <summary>The smallest signed difference from angle a to angle b, in -π..π.</summary>
    public static double AngleDiff(double a, double b)
    {
        double d = (b - a) % (2 * Math.PI);
        if (d > Math.PI) d -= 2 * Math.PI;
        if (d < -Math.PI) d += 2 * Math.PI;
        return d;
    }

    /// <summary>How far a point lies from a line segment.</summary>
    public static double SegmentDistance(Vec p, Vec a, Vec b)
    {
        var ab = b - a;
        double t = ab.LengthSq < 1e-9 ? 0 : Math.Clamp(((p - a).X * ab.X + (p - a).Y * ab.Y) / ab.LengthSq, 0, 1);
        return (p - (a + ab * t)).Length;
    }
}
