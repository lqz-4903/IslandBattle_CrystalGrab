// =============================================
// DeterministicRandom.cs — 确定性随机数
// 相同seed在任何平台产出完全相同的序列
// =============================================
public class DeterministicRandom
{
    private uint _state;

    public DeterministicRandom(int seed)
    {
        _state = (uint)seed;
        if (_state == 0) _state = 1; // 避免全零死循环
    }

    /// <summary>返回 [0, max) 的整数</summary>
    public int Next(int max)
    {
        if (max <= 0) return 0;
        return (int)(NextUInt() % (uint)max);
    }

    /// <summary>返回 [min, max) 的整数</summary>
    public int Next(int min, int max)
    {
        if (max <= min) return min;
        return min + Next(max - min);
    }

    /// <summary>返回 [0.0, 1.0) 的定点数</summary>
    public Fix64 NextFix64()
    {
        return Fix64.FromFraction(NextUInt(), uint.MaxValue);
    }

    private uint NextUInt()
    {
        // xorshift32，最快的确定性随机之一
        _state ^= _state << 13;
        _state ^= _state >> 17;
        _state ^= _state << 5;
        return _state;
    }
}
