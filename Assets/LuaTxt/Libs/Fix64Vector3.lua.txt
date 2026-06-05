-- =============================================
-- Lua/Fix64Vector3.lua
-- =============================================

local Fix64 = require("Fix64")
local Vec3 = {}
Vec3.__index = Vec3

function Vec3.new(x, y, z)
    return setmetatable({ x = x, y = y, z = z }, Vec3)
end

Vec3.ZERO = Vec3.new(Fix64.ZERO, Fix64.ZERO, Fix64.ZERO)

function Vec3.__add(a, b) return Vec3.new(a.x + b.x, a.y + b.y, a.z + b.z) end
function Vec3.__sub(a, b) return Vec3.new(a.x - b.x, a.y - b.y, a.z - b.z) end
function Vec3.__mul(a, s) return Vec3.new(a.x * s, a.y * s, a.z * s) end -- 向量乘标量

function Vec3.sqrMagnitudeXZ(v)
    return (v.x * v.x) + (v.z * v.z)
end

function Vec3.distanceXZ(a, b)
    return Fix64.sqrt(Vec3.sqrMagnitudeXZ(a - b))
end

function Vec3.normalizedXZ(v)
    local mag = Fix64.sqrt(Vec3.sqrMagnitudeXZ(v))
    if mag.raw == 0 then return Vec3.ZERO end
    return Vec3.new(v.x / mag, Fix64.ZERO, v.z / mag)
end

-- 用于转回Unity坐标
function Vec3.toUnity(v)
    return CS.UnityEngine.Vector3(Fix64.toFloat(v.x), Fix64.toFloat(v.y), Fix64.toFloat(v.z))
end

return Vec3
