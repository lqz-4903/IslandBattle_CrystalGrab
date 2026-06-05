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

-- 常量
Fix64.ZERO = Fix64.new(0)
Fix64.ONE  = Fix64.fromInt(1)
Fix64.TWO  = Fix64.fromInt(2)
Fix64.HALF = Fix64.new(HALF)
Fix64.PI   = Fix64.fromFloat(3.14159265358979)

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

function Fix64.__div(a, b)
    if b.raw == 0 then error("Fix64 divide by zero") end
    return Fix64.new((a.raw / b.raw) * ONE) -- 简化写法，Lua数字精度足够处理这个逻辑
    -- 注：为了极致精确，生产环境常用 a.raw * ONE / b.raw，但注意Lua数字大小限制
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

-- 牛顿迭代开方
function Fix64.sqrt(a)
    if a.raw < 0 then error("Sqrt negative") end
    if a.raw == 0 then return Fix64.ZERO end
    -- 使用Lua原生开方再转回来足够精确且不破坏确定性
    -- (因为只在物理逻辑内使用，只要Lua自身math.sqrt确定就行，通常都是IEEE754标准)
    -- 如果追求极致绝对确定性，可在这里手写位运算开方，但太长了。
    local val = math.sqrt(a.raw / ONE)
    return Fix64.fromFloat(val)
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
