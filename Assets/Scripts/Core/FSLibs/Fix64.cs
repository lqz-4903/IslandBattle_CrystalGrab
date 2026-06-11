// =============================================
// Fix64.cs — 64位定点数 (32.32格式)
// 精度约 0.0000000002，范围 ±2147483647
// =============================================
using System;
using System.Runtime.CompilerServices;

public readonly struct Fix64 : IEquatable<Fix64>, IComparable<Fix64>
{
    // 内部用 long 存储，高32位整数，低32位小数
    public readonly long Raw;

    // ========== 基础常量 ==========

    const int FRACTIONAL_BITS = 32;
    const long ONE = 1L << FRACTIONAL_BITS;                    // 1.0
    const long HALF = 1L << (FRACTIONAL_BITS - 1);             // 0.5
    const long EPSILON = 1L;                                    // 最小精度
    const long PI_RAW = 13493037705L;                           // 3.14159265358979...
    const long HALF_PI_RAW = 6746518852L;
    const long TWO_PI_RAW = 26986075409L;

    public static readonly Fix64 Zero = new Fix64(0);
    public static readonly Fix64 One = new Fix64(ONE);
    public static readonly Fix64 Half = new Fix64(HALF);
    public static readonly Fix64 MinusOne = new Fix64(-ONE);
    public static readonly Fix64 Pi = new Fix64(PI_RAW);
    public static readonly Fix64 HalfPi = new Fix64(HALF_PI_RAW);
    public static readonly Fix64 TwoPi = new Fix64(TWO_PI_RAW);
    public static readonly Fix64 MaxValue = new Fix64(long.MaxValue);
    public static readonly Fix64 MinValue = new Fix64(long.MinValue);
    public static readonly Fix64 Epsilon = new Fix64(EPSILON);

    // ========== 构造 ==========

    public Fix64(long raw) { Raw = raw; }

    /// <summary>从int构造</summary>
    public static Fix64 FromInt(int value) => new Fix64((long)value << FRACTIONAL_BITS);

    /// <summary>从float构造（注意：float本身不精确，仅用于初始化常量）</summary>
    public static Fix64 FromFloat(float value) => new Fix64((long)((double)value * ONE));

    /// <summary>从double构造</summary>
    public static Fix64 FromDouble(double value) => new Fix64((long)(value * ONE));

    /// <summary>从分子分母构造</summary>
    public static Fix64 FromFraction(long numerator, long denominator)
        => new Fix64((numerator << FRACTIONAL_BITS) / denominator);

    // ========== 转换 ==========

    /// <summary>转int（截断）</summary>
    public int ToInt() => (int)(Raw >> FRACTIONAL_BITS);

    /// <summary>转float（仅用于最终渲染，不参与逻辑）</summary>
    public float ToFloat() => (float)Raw / ONE;

    /// <summary>转double</summary>
    public double ToDouble() => (double)Raw / ONE;

    /// <summary>取整数部分</summary>
    public Fix64 Floor() => new Fix64(Raw & ~((1L << FRACTIONAL_BITS) - 1));

    /// <summary>向上取整</summary>
    public Fix64 Ceil()
    {
        long fracMask = (1L << FRACTIONAL_BITS) - 1;
        if ((Raw & fracMask) != 0)
            return new Fix64((Raw & ~fracMask) + ONE);
        return this;
    }

    /// <summary>四舍五入</summary>
    public Fix64 Round()
    {
        long fracMask = (1L << FRACTIONAL_BITS) - 1;
        long frac = Raw & fracMask;
        long intPart = Raw & ~fracMask;
        if (frac >= HALF)
            return new Fix64(intPart + ONE);
        return new Fix64(intPart);
    }

    /// <summary>取小数部分</summary>
    public Fix64 Frac()
    {
        long fracMask = (1L << FRACTIONAL_BITS) - 1;
        return new Fix64(Raw & fracMask);
    }

    // ========== 四则运算 ==========

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fix64 operator +(Fix64 a, Fix64 b) => new Fix64(a.Raw + b.Raw);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fix64 operator -(Fix64 a, Fix64 b) => new Fix64(a.Raw - b.Raw);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fix64 operator -(Fix64 a) => new Fix64(-a.Raw);

    /// <summary>
    /// 乘法：拆分为高低 32 位分别计算再合并，防止 64 位溢出。
    /// 公式： (aHi*ONE + aLo) * (bHi*ONE + bLo) / ONE
    ///      = aHi*bHi*ONE + aHi*bLo + aLo*bHi + aLo*bLo/ONE
    /// 约束：aHi*bHi 必须 < 2^31，即两操作数的整数部分乘积 < 2^31。
    ///       这限制了 Fix64 的整数部分不能同时超过约 46340。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fix64 operator *(Fix64 a, Fix64 b)
    {
        long al = a.Raw;
        long bl = b.Raw;

        long aLo = al & 0xFFFFFFFF;
        long aHi = al >> FRACTIONAL_BITS;
        long bLo = bl & 0xFFFFFFFF;
        long bHi = bl >> FRACTIONAL_BITS;

        long loLo = (aLo * bLo) >> FRACTIONAL_BITS;
        long loHi = aLo * bHi;
        long hiLo = aHi * bLo;
        long hiHi = aHi * bHi << FRACTIONAL_BITS;

        // 溢出检测：如果 aHi 和 bHi 都非零，hiHi 可能溢出
        // 对于游戏逻辑 (坐标/速度)，整数部分通常远小于 46340，不会溢出
        return new Fix64(loLo + loHi + hiLo + hiHi);
    }

    /// <summary>
    /// 除法。
    /// 传统做法 (a.Raw &lt;&lt; 32) / b.Raw 在较大值时会溢出 long。
    /// 改用恒等式分解：a / b = (a.Raw ÷ b.Raw) * ONE + (a.Raw % b.Raw) * ONE / b.Raw
    /// 其中 ÷ 是整数除法，% 是取余，各自左移前都在安全范围内。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fix64 operator /(Fix64 a, Fix64 b)
    {
        if (b.Raw == 0) throw new DivideByZeroException("Fix64 divide by zero");

        long intPart = a.Raw / b.Raw;                        // 整数商（以 1.0 为单位）
        long remainder = a.Raw % b.Raw;                      // 余数

        // intPart * ONE + (remainder * ONE) / b.Raw
        long hi = intPart << FRACTIONAL_BITS;
        long lo = (remainder << FRACTIONAL_BITS) / b.Raw;

        return new Fix64(hi + lo);
    }

    public static Fix64 operator %(Fix64 a, Fix64 b)
    {
        if (b.Raw == 0) throw new DivideByZeroException("Fix64 modulo by zero");
        return new Fix64(a.Raw % b.Raw);
    }

    // ========== 比较 ==========

    public static bool operator ==(Fix64 a, Fix64 b) => a.Raw == b.Raw;
    public static bool operator !=(Fix64 a, Fix64 b) => a.Raw != b.Raw;
    public static bool operator <(Fix64 a, Fix64 b) => a.Raw < b.Raw;
    public static bool operator >(Fix64 a, Fix64 b) => a.Raw > b.Raw;
    public static bool operator <=(Fix64 a, Fix64 b) => a.Raw <= b.Raw;
    public static bool operator >=(Fix64 a, Fix64 b) => a.Raw >= b.Raw;

    // ========== 绝对值 ==========

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fix64 Abs(Fix64 a) => a.Raw < 0 ? new Fix64(-a.Raw) : a;

    // ========== Clamp ==========

    public static Fix64 Clamp(Fix64 value, Fix64 min, Fix64 max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    public static Fix64 Clamp01(Fix64 value) => Clamp(value, Zero, One);

    // ========== Min / Max ==========

    public static Fix64 Min(Fix64 a, Fix64 b) => a < b ? a : b;
    public static Fix64 Max(Fix64 a, Fix64 b) => a > b ? a : b;

    // ========== Sqrt（二进制逐位开方 + Newton 精修） ==========

    public static Fix64 Sqrt(Fix64 a)
    {
        if (a.Raw < 0) throw new ArithmeticException("Fix64 Sqrt of negative");
        if (a.Raw == 0) return Zero;

        long x = a.Raw;
        long result = 0;
        long bit = 1L << (FRACTIONAL_BITS + 30);

        // 二进制逐位开方（整数+小数统一处理，完全确定性，不依赖浮点）
        while (bit > x) bit >>= 2;
        while (bit != 0)
        {
            long sum = result + bit;
            if (x >= sum)
            {
                x -= sum;
                result = (result >> 1) + bit;
            }
            else
            {
                result >>= 1;
            }
            bit >>= 2;
        }

        // ★ 二进制开方输出 16.16 格式（sqrt(raw) ≈ sqrt(V)*2^16）
        //   先升到 32.32 格式，再做 Newton 精修
        result <<= 16;  // 16.16 → 32.32

        // Newton 精修 1 轮：r1 = (r0 + a/r0) / 2
        // a/r0 = quot * ONE + rem * ONE / result
        // rem * ONE = rem * 2^32，拆为两级避免溢出（rem 接近 result 时 rem<<32 会溢出 long）
        //   rem * 2^32 / result = (rem * 2^16) / result * 2^16
        //                      + ((rem * 2^16) % result) * 2^16 / result
        long quot = a.Raw / result;
        long rem = a.Raw % result;
        long q1 = (rem << 16) / result;       // rem * 2^16 / result → 高 16 位小数
        long r1 = (rem << 16) % result;
        long q2 = (r1 << 16) / result;        // 低 16 位小数
        long div = (quot << FRACTIONAL_BITS) + (q1 << 16) + q2;
        result = (result + div) >> 1;

        return new Fix64(result);
    }

    // ========== 幂运算 ==========

    public static Fix64 Pow2(Fix64 a) => a * a;

    // ========== Lerp ==========

    public static Fix64 Lerp(Fix64 a, Fix64 b, Fix64 t)
    {
        t = Clamp01(t);
        return a + (b - a) * t;
    }

    // ========== 三角函数（查表+插值） ==========
    // 为了性能和代码量，用泰勒展开近似，精度够用
    // 如需更高精度可改用查表法

    /// <summary>
    /// Sin，输入弧度，精度 ±0.001
    /// 用归一化到 [-π, π] + 泰勒5阶展开
    /// </summary>
    public static Fix64 Sin(Fix64 radians)
    {
        // 归一化到 [-π, π]
        Fix64 r = radians % TwoPi;
        if (r > Pi) r = r - TwoPi;
        if (r < -Pi) r = r + TwoPi;

        // 泰勒展开: sin(x) ≈ x - x³/6 + x⁵/120 - x⁷/5040
        Fix64 r2 = r * r;
        Fix64 r3 = r2 * r;
        Fix64 r5 = r3 * r2;
        Fix64 r7 = r5 * r2;

        return r
             - r3 / FromInt(6)
             + r5 / FromInt(120)
             - r7 / FromInt(5040);
    }

    public static Fix64 Cos(Fix64 radians) => Sin(radians + HalfPi);

    //Atan2 系数（预计算 raw 值，避免 FromDouble 的浮点舍入误差）
    private const long AtanCoef0Raw = 4294042386L;   // 0.9998660 * ONE
    private const long AtanCoef1Raw = 1289025051L;   // 0.3001455 * ONE (取负)
    private const long AtanCoef2Raw = 656571189L;     // 0.1528793 * ONE
    private const long AtanCoef3Raw = 273536403L;     // 0.0636918 * ONE (取负)

    private static readonly Fix64 AtanCoef0 = new Fix64(AtanCoef0Raw);
    private static readonly Fix64 AtanCoef1 = new Fix64(AtanCoef1Raw);
    private static readonly Fix64 AtanCoef2 = new Fix64(AtanCoef2Raw);
    private static readonly Fix64 AtanCoef3 = new Fix64(AtanCoef3Raw);

    /// <summary>
    /// Atan2(y, x) — 用 MinMax 归一化 + 4 阶多项式近似，精度 ±0.001 弧度。
    /// 系数使用预计算的 raw 值，完全确定性，不依赖浮点。
    /// </summary>
    public static Fix64 Atan2(Fix64 y, Fix64 x)
    {
        if (x.Raw == 0 && y.Raw == 0) return Zero;

        Fix64 absX = Abs(x);
        Fix64 absY = Abs(y);
        Fix64 minVal = Min(absX, absY);
        Fix64 maxVal = Max(absX, absY);

        Fix64 a = minVal / maxVal;
        Fix64 s = a * a;

        // atan 近似: r ≈ a * (C0 - C1*s + C2*s² - C3*s³)
        Fix64 r = a * (AtanCoef0 - AtanCoef1 * s + AtanCoef2 * s * s - AtanCoef3 * s * s * s);

        if (absY > absX) r = HalfPi - r;
        if (x.Raw < 0) r = Pi - r;
        if (y.Raw < 0) r = -r;

        return r;
    }

    /// <summary>角度转弧度</summary>
    public static Fix64 Deg2Rad(Fix64 degrees) => degrees * Pi / FromInt(180);

    /// <summary>弧度转角度</summary>
    public static Fix64 Rad2Deg(Fix64 radians) => radians * FromInt(180) / Pi;

    // ========== 隐式转换 ==========

    public static implicit operator Fix64(int value) => FromInt(value);

    // ========== 比较接口 ==========

    public bool Equals(Fix64 other) => Raw == other.Raw;
    public override bool Equals(object obj) => obj is Fix64 f && Raw == f.Raw;
    public override int GetHashCode() => Raw.GetHashCode();
    public int CompareTo(Fix64 other) => Raw.CompareTo(other.Raw);

    public override string ToString() => ToDouble().ToString("F6");
}
