-- =============================================
-- Lua/DeterministicRandom.lua
-- =============================================

local Random = {}
Random.__index = Random

function Random.new(seed)
    local o = setmetatable({}, Random)
    o._state = seed    if o._state == 0 then o._state = 1 end
    return o
end

-- lua 5.3
-- function Random:nextUInt()
--     local s = self._state
--     s = s ~ (s << 13)
--     s = s ~ (s >> 17)
--     s = s ~ (s << 5)
--     self._state = s
--     return s
-- end

-- Lua 5.1 兼容版（如果位运算报错就用这个）
local bit = require("bit") -- xlua自带function Random:nextUInt()
    local s = self._state
    s = bit.bxor(s, bit.lshift(s, 13))
    s = bit.bxor(s, bit.rshift(s, 17))
    s = bit.bxor(s, bit.lshift(s, 5))
    self._state = s
    return s
end


function Random:next(max)
    return self:nextUInt() % max
end

function Random:nextRange(min, max)
    return min + self:next(max - min)
end

return Random
