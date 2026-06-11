-- =============================================
-- Lua/Fix64.lua — 纯Lua定点数库
-- =============================================

local Fix64 = {}
Fix64.__index = Fix64

-- 配置：32位小数 (和C#保持一致)
local FRAC_BITS = 32
local ONE = 2^FRAC_BITS
local HALF = ONE / 2
local MASK = ONE - 1

-- ========== 构造 ==========
function Fix64.new(raw)
    return setmetatable({ raw = raw }, Fix64)
end

function Fix64.fromInt(n)
    return Fix64.new(n * ONE)
end

function Fix64.fromFloat(f)
    return Fix64.new(math.floor(f * ONE))
end

-- 常量（PI 使用预计算 raw 值，避免 fromFloat 的浮点舍入误差）
-- PI ≈ 3.14159265358979 → raw = 3.14159265358979 * 2^32 ≈ 13493037705
local PI_RAW = 13493037705

Fix64.ZERO = Fix64.new(0)
Fix64.ONE  = Fix64.fromInt(1)
Fix64.TWO  = Fix64.fromInt(2)
Fix64.HALF = Fix64.new(HALF)
Fix64.PI   = Fix64.new(PI_RAW)

-- ========== 运算符重载 ==========
function Fix64.__add(a, b)
    return Fix64.new(a.raw + b.raw)
end

function Fix64.__sub(a, b)
    return Fix64.new(a.raw - b.raw)
end

function Fix64.__mul(a, b)
    -- 拆分高低位防溢出（模拟C#逻辑）
    local a_lo = a.raw % ONE
    local a_hi = math.floor(a.raw / ONE)
    local b_lo = b.raw % ONE
    local b_hi = math.floor(b.raw / ONE)

    local res = (a_lo * b_lo / ONE) + (a_lo * b_hi) + (a_hi * b_lo) + (a_hi * b_hi * ONE)
    return Fix64.new(res)
end

-- 除法：用恒等式 (a ÷ b) * ONE + ((a % b) * ONE) / b 分解，避免 (a * ONE) 溢出 Lua 53 位整数精度
function Fix64.__div(a, b)
    if b.raw == 0 then error("Fix64 divide by zero") end
    local intPart = math.floor(a.raw / b.raw)
    local remainder = a.raw % b.raw
    return Fix64.new(intPart * ONE + math.floor(remainder * ONE / b.raw))
end

function Fix64.__mod(a, b)
    return Fix64.new(a.raw % b.raw)
end

function Fix64.__unm(a)
    return Fix64.new(-a.raw)
end

function Fix64.__eq(a, b) return a.raw == b.raw end
function Fix64.__lt(a, b) return a.raw < b.raw end
function Fix64.__le(a, b) return a.raw <= b.raw end

-- ========== 基础函数 ==========
function Fix64.floor(a)
    return Fix64.new(math.floor(a.raw / ONE) * ONE)
end

function Fix64.toInt(a)
    return math.floor(a.raw / ONE)
end

function Fix64.toFloat(a)
    return a.raw / ONE
end

-- 开方：二进制逐位开方（纯整数运算，跨平台完全确定性，不依赖 math.sqrt）
function Fix64.sqrt(a)
    if a.raw < 0 then error("Sqrt negative") end
    if a.raw == 0 then return Fix64.ZERO end

    local x = a.raw
    local result = 0

    -- 起始位：对齐到 x 的最高位附近（小数部分 32 位 + 整数 30 位余量）
    local bit = 2 ^ (FRAC_BITS + 30)
    while bit > x do
        bit = bit >> 2
    end

    while bit ~= 0 do
        local sum = result + bit
        if x >= sum then
            x = x - sum
            result = (result >> 1) + bit
        else
            result = result >> 1
        end
        bit = bit >> 2
    end

    -- ★ 二进制开方输出 16.16 格式（sqrt(raw) ≈ sqrt(V)*2^16）
    --   先升到 32.32 格式，再做 Newton 精修
    result = result * 65536  -- 16.16 → 32.32

    -- Newton 精修 1 轮：r' = (r + a/r) / 2
    -- ★ 使用 2 级分解避免 rem * ONE 溢出 Lua 53 位精度：
    --   floor(rem * ONE / result) 拆为 floor(rem * 2^16 / result) * 2^16
    --   + floor((rem * 2^16 % result) * 2^16 / result)
    --   每级乘积 ≤ result * 2^16 ≈ 6e9 * 65536 ≈ 3.9e14 < 2^53 (9e15)
    local quot = math.floor(a.raw / result)
    local rem = a.raw % result
    local S = 65536  -- 2^16
    local q1 = math.floor(rem * S / result)
    local r1 = (rem * S) % result
    local q2 = math.floor(r1 * S / result)
    local div = quot * ONE + q1 * S + q2
    result = (result + div) >> 1

    return Fix64.new(result)
end

function Fix64.abs(a)
    if a.raw < 0 then return -a else return a end
end

function Fix64.clamp(val, min, max)
    if val < min then return min end
    if val > max then return max end
    return val
end

function Fix64.lerp(a, b, t)
    t = Fix64.clamp(t, Fix64.ZERO, Fix64.ONE)
    return a + (b - a) * t
end

return Fix64
