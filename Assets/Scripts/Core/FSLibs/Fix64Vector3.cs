// =============================================
// Fix64Vector3.cs — 确定性三维向量
// =============================================
using System;

public readonly struct Fix64Vector3 : IEquatable<Fix64Vector3>
{
    public readonly Fix64 X;
    public readonly Fix64 Y;
    public readonly Fix64 Z;

    // ========== 构造 ==========

    public Fix64Vector3(Fix64 x, Fix64 y, Fix64 z)
    {
        X = x; Y = y; Z = z;
    }

    public Fix64Vector3(int x, int y, int z)
    {
        X = Fix64.FromInt(x);
        Y = Fix64.FromInt(y);
        Z = Fix64.FromInt(z);
    }

    // ========== 常量 ==========

    public static readonly Fix64Vector3 Zero = new Fix64Vector3(Fix64.Zero, Fix64.Zero, Fix64.Zero);
    public static readonly Fix64Vector3 One = new Fix64Vector3(Fix64.One, Fix64.One, Fix64.One);
    public static readonly Fix64Vector3 Up = new Fix64Vector3(Fix64.Zero, Fix64.One, Fix64.Zero);
    public static readonly Fix64Vector3 Right = new Fix64Vector3(Fix64.One, Fix64.Zero, Fix64.Zero);
    public static readonly Fix64Vector3 Forward = new Fix64Vector3(Fix64.Zero, Fix64.Zero, Fix64.One);

    // ========== 四则运算 ==========

    public static Fix64Vector3 operator +(Fix64Vector3 a, Fix64Vector3 b)
        => new Fix64Vector3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static Fix64Vector3 operator -(Fix64Vector3 a, Fix64Vector3 b)
        => new Fix64Vector3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static Fix64Vector3 operator -(Fix64Vector3 a)
        => new Fix64Vector3(-a.X, -a.Y, -a.Z);

    public static Fix64Vector3 operator *(Fix64Vector3 a, Fix64 scalar)
        => new Fix64Vector3(a.X * scalar, a.Y * scalar, a.Z * scalar);

    public static Fix64Vector3 operator *(Fix64 scalar, Fix64Vector3 a)
        => new Fix64Vector3(a.X * scalar, a.Y * scalar, a.Z * scalar);

    public static Fix64Vector3 operator /(Fix64Vector3 a, Fix64 scalar)
        => new Fix64Vector3(a.X / scalar, a.Y / scalar, a.Z / scalar);

    // ========== 点乘 ==========

    public static Fix64 Dot(Fix64Vector3 a, Fix64Vector3 b)
        => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    // ========== 叉乘 ==========

    public static Fix64Vector3 Cross(Fix64Vector3 a, Fix64Vector3 b)
        => new Fix64Vector3(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X
        );

    // ========== 长度 ==========

    public Fix64 SqrMagnitude => X * X + Y * Y + Z * Z;

    public Fix64 Magnitude => Fix64.Sqrt(SqrMagnitude);

    /// <summary>XZ平面长度（忽略Y，用于地面碰撞判定）</summary>
    public Fix64 SqrMagnitudeXZ => X * X + Z * Z;

    public Fix64 MagnitudeXZ => Fix64.Sqrt(SqrMagnitudeXZ);

    // ========== 归一化 ==========

    public Fix64Vector3 Normalized
    {
        get
        {
            Fix64 mag = Magnitude;
            if (mag.Raw <= Fix64.Epsilon.Raw) return Zero;
            return this / mag;
        }
    }

    public Fix64Vector3 NormalizedXZ
    {
        get
        {
            Fix64 mag = MagnitudeXZ;
            if (mag.Raw <= Fix64.Epsilon.Raw) return Zero;
            return new Fix64Vector3(X / mag, Fix64.Zero, Z / mag);
        }
    }

    // ========== 距离 ==========

    public static Fix64 Distance(Fix64Vector3 a, Fix64Vector3 b)
        => (a - b).Magnitude;

    public static Fix64 DistanceXZ(Fix64Vector3 a, Fix64Vector3 b)
        => (a - b).MagnitudeXZ;

    public static Fix64 SqrDistance(Fix64Vector3 a, Fix64Vector3 b)
        => (a - b).SqrMagnitude;

    // ========== Lerp ==========

    public static Fix64Vector3 Lerp(Fix64Vector3 a, Fix64Vector3 b, Fix64 t)
    {
        t = Fix64.Clamp01(t);
        return new Fix64Vector3(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t,
            a.Z + (b.Z - a.Z) * t
        );
    }

    // ========== ClampMagnitude ==========

    public Fix64Vector3 ClampMagnitude(Fix64 maxLength)
    {
        Fix64 mag = Magnitude;
        if (mag <= maxLength) return this;
        return this / mag * maxLength;
    }

    // ========== 旋转（绕Y轴，用于WASD方向计算） ==========

    /// <summary>
    /// 绕Y轴旋转（输入弧度）
    /// </summary>
    public Fix64Vector3 RotateY(Fix64 radians)
    {
        Fix64 cos = Fix64.Cos(radians);
        Fix64 sin = Fix64.Sin(radians);
        return new Fix64Vector3(
            X * cos - Z * sin,
            Y,
            X * sin + Z * cos
        );
    }

    /// <summary>
    /// 根据yaw角度（度）算前方向量，用于WASD移动
    /// </summary>
    public static Fix64Vector3 DirectionFromYaw(Fix64 yawDegrees)
    {
        Fix64 rad = Fix64.Deg2Rad(yawDegrees);
        return new Fix64Vector3(Fix64.Sin(rad), Fix64.Zero, Fix64.Cos(rad));
    }

    // ========== 比较 ==========

    public bool Equals(Fix64Vector3 other)
        => X.Raw == other.X.Raw && Y.Raw == other.Y.Raw && Z.Raw == other.Z.Raw;

    public override bool Equals(object obj)
        => obj is Fix64Vector3 v && Equals(v);

    public override int GetHashCode()
        => X.Raw.GetHashCode() ^ Y.Raw.GetHashCode() << 2 ^ Z.Raw.GetHashCode() >> 2;

    public static bool operator ==(Fix64Vector3 a, Fix64Vector3 b) => a.Equals(b);
    public static bool operator !=(Fix64Vector3 a, Fix64Vector3 b) => !a.Equals(b);

    // ========== 与Unity Vector3互转（仅用于C#表现层） ==========

    public UnityEngine.Vector3 ToUnity()
        => new UnityEngine.Vector3(X.ToFloat(), Y.ToFloat(), Z.ToFloat());

    public static Fix64Vector3 FromUnity(UnityEngine.Vector3 v)
        => new Fix64Vector3(Fix64.FromFloat(v.x), Fix64.FromFloat(v.y), Fix64.FromFloat(v.z));

    public override string ToString() => $"({X}, {Y}, {Z})";
}
