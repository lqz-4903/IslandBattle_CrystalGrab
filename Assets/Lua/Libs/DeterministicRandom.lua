-- =============================================
-- Lua/DeterministicRandom.lua
-- 封装 C# DeterministicRandom，避免依赖 bit 库
-- =============================================

local Random = {}
Random.__index = Random

function Random.new(seed)
    local o = setmetatable({}, Random)
    o._cs = CS.DeterministicRandom(seed)
    return o
end

function Random:next(max)
    return self._cs:Next(max)
end

function Random:nextRange(min, max)
    return self._cs:Next(min, max)
end

return Random
