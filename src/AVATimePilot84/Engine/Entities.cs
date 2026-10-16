namespace AVATimePilot84.Engine;

/// <summary>The controls: the stick (each -1, 0 or 1; up the screen is -1) for the way to fly, the guns, and a missile.</summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Fire = false, bool Missile = false);

/// <summary>The player's jet: always in the middle of the screen, always flying, pointing one of sixteen ways.</summary>
public sealed class Jet
{
    public Vec Pos;
    public int Heading;
    public double TurnIn, FireIn, MissileIn, Safe, Burner;
    public bool Alive = true;
    public const double Speed = 210, Radius = 11;
    public Vec Dir => Vec.FromAngle(Field.HeadingAngle(Heading));
    public Vec Velocity => Dir * Speed;
}

public enum EnemyKind { Interceptor, Drone, Gunship }

public sealed class Enemy
{
    public EnemyKind Kind;
    public Vec Pos;
    public double Heading, Speed, FireIn, T, Flash;
    public int Hits = 1;
    /// <summary>Part of a formation: flies straight through instead of chasing.</summary>
    public int Formation = -1;
    /// <summary>Each aims a little off the jet, so they don't all pile into one point.</summary>
    public Vec AimOffset;
    /// <summary>Making a pass: holding course through and away before turning back.</summary>
    public bool Passing;
    public bool Dead;
    public Vec Velocity => Vec.FromAngle(Heading) * Speed;
    public double Radius => Kind switch { EnemyKind.Gunship => 24, EnemyKind.Drone => 12, _ => 16 };
}

public sealed class Shot
{
    public Vec Pos, Vel;
    public double Life;
    public bool Hostile, Dead;
}

/// <summary>A homing rocket: the jet's missiles chase what they were locked on to, the enemy's chase the jet.</summary>
public sealed class Rocket
{
    public Vec Pos;
    public double Heading, Speed, Turn, Life, Smoke;
    public bool Hostile, Dead;
    /// <summary>What a missile of the jet's was locked on to: an enemy, or a part of the carrier.</summary>
    public object? Target;
    public const double Radius = 6;
}

/// <summary>A gun turret on the carrier: knock them all out and the carrier's core is open to fire.</summary>
public sealed class Turret
{
    /// <summary>Where it sits on the carrier, nose up.</summary>
    public Vec Mount;
    public int Hits;
    public double FireIn, Aim, Flash;
    public bool Dead => Hits <= 0;
    public const int Toughness = 6;
    public const double Radius = 15;
}

/// <summary>The zone's carrier: a huge flying wing with turrets, launching drones, its core shielded until the turrets are gone.</summary>
public sealed class Carrier
{
    public Vec Pos;
    public double Heading, LaunchIn, Flash;
    public int Hits;
    public List<Turret> Turrets { get; } = [];
    public bool Dead;
    public bool Open => Turrets.All(t => t.Dead);
    public const double Speed = 70, HalfLength = 70, HalfSpan = 150, CoreRadius = 26;

    /// <summary>A point on the carrier, given nose up about its middle, where it is now.</summary>
    public Vec At(Vec local)
    {
        double a = Heading, c = Math.Cos(a), s = Math.Sin(a);
        return Pos + new Vec(local.X * c - local.Y * s, local.X * s + local.Y * c);
    }

    /// <summary>Whether a point is over the carrier's wing (a broad arrowhead).</summary>
    public bool Covers(Vec p, double pad = 0)
    {
        var d = p - Pos;
        double a = -Heading, c = Math.Cos(a), s = Math.Sin(a);
        double x = d.X * c - d.Y * s, y = d.X * s + d.Y * c;
        // Nose at y = -HalfLength, wingtips at y = +HalfLength - 10 either side.
        if (y < -HalfLength - pad || y > HalfLength + pad) return false;
        double half = (y + HalfLength) / (2 * HalfLength) * HalfSpan + 20;
        return Math.Abs(x) < half + pad;
    }
}

public enum EffectKind { Boom, BigBoom, Score, Smoke, Word }

public sealed class Effect
{
    public EffectKind Kind;
    public Vec Pos;
    public double Age, Life = 0.6, Size = 1;
    public string Text = "";
    public uint Colour = 0xFFFFE14A;
    public bool Done => Age >= Life;
}

/// <summary>Where the fighting is: each zone its own ground, and its own carrier at the end.</summary>
public sealed record Zone(string Name, uint Ground, uint Detail, uint Accent, int Turrets)
{
    public static readonly Zone[] All =
    [
        new("OCEAN", 0xFF1E4A7A, 0xFF2E6A9A, 0xFF5AAA5A, 4),
        new("DESERT", 0xFFC89A5A, 0xFFB0804A, 0xFF8A5A3A, 4),
        new("ICE FIELDS", 0xFF2A4A6A, 0xFFE8F2FA, 0xFFB0D0E8, 5),
        new("CITY", 0xFF3A3E46, 0xFF5A606A, 0xFFFFD27A, 5),
        new("ORBIT", 0xFF0A0A1A, 0xFF6A6A7A, 0xFF9AA0B0, 6),
    ];

    public static Zone Of(int wave) => All[(wave - 1) % All.Length];
}
