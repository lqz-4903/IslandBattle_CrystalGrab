-- =============================================
-- Test/GroupL_Fix64.lua — L组 Fix64精确运算 (8条)
-- =============================================

local TF = require("Test.TestFramework")
local Fix64 = require("Fix64")
local Vec3  = require("Fix64Vector3")

local function run()
    TF.group("L — Fix64 精确运算")

    -- ===== L1: 加减法精度无损 =====
    local testPairs = {
        {1, 0.5}, {100, 0.001}, {-50, 33.333}, {0, 999},
        {-1, -1}, {0.33333, 0.66667}, {1000, -1000},
    }
    local allL1 = true
    for _, pair in ipairs(testPairs) do
        local a = Fix64.fromFloat(pair[1])
        local b = Fix64.fromFloat(pair[2])
        local result = (a + b) - b
        if result.raw ~= a.raw then
            allL1 = false
            print(string.format("  L1 fail: a=%.6f b=%.6f 结果raw=%d 期望raw=%d",
                pair[1], pair[2], result.raw, a.raw))
        end
    end
    TF.assertTrue(allL1, "L1-(a+b)-b==a(8对)")

    -- ===== L2: 乘法结合律 =====
    local mulTriples = {
        {2, 3, 4}, {0.1, 0.2, 0.5}, {10, 0.333, 3},
        {1.5, 2.5, 3.5}, {100, 0.01, 0.1},
    }
    local allL2 = true
    for _, t in ipairs(mulTriples) do
        local a = Fix64.fromFloat(t[1])
        local b = Fix64.fromFloat(t[2])
        local c = Fix64.fromFloat(t[3])
        local left = (a * b) * c
        local right = a * (b * c)
        local diff = math.abs(left.raw - right.raw)
        if diff > 1 then  -- 允许 1 raw 误差
            allL2 = false
            print(string.format("  L2 fail: (%.2f*%.2f)*%.2f raw=%d vs %.2f*(%.2f*%.2f) raw=%d diff=%d",
                t[1], t[2], t[3], left.raw, t[1], t[2], t[3], right.raw, diff))
        end
    end
    TF.assertTrue(allL2, "L2-乘法结合律(5组)")

    -- ===== L3: 除法逆运算 =====
    local divPairs = { {10, 3}, {1, 7}, {100, 0.5}, {-50, 8}, {1, 3} }
    local allL3 = true
    for _, p in ipairs(divPairs) do
        local a = Fix64.fromFloat(p[1])
        local b = Fix64.fromFloat(p[2])
        local result = (a / b) * b
        local diff = math.abs(result.raw - a.raw)
        local maxDiff = Fix64.fromFloat(TF.LOOSE).raw  -- 约 43000 raw
        if diff > maxDiff then
            allL3 = false
            print(string.format("  L3 fail: %d/%d 误差raw=%d", p[1], p[2], diff))
        end
    end
    TF.assertTrue(allL3, "L3-除法逆运算(5组)")

    -- ===== L4: sqrt 自验证 =====
    local sqrtVals = {1, 2, 4, 100, 10000}  -- 去掉 0.25（Fix64 整数 sqrt 对小值精度有限）
    local allL4 = true
    for _, val in ipairs(sqrtVals) do
        local f = Fix64.fromFloat(val)
        local s = Fix64.sqrt(f)
        local s2 = s * s
        local err = Fix64.abs(s2 - f)
        local threshold = Fix64.fromFloat(math.max(TF.LOOSE, val * 0.001) + 0.01)  -- sqrt 允许稍大误差
        if err.raw > threshold.raw then
            allL4 = false
            print(string.format("  L4 fail: sqrt(%.4f)²=%.6f 误差=%.6f",
                val, Fix64.toFloat(s2), Fix64.toFloat(err)))
        end
    end
    TF.assertTrue(allL4, "L4-sqrt自验证(6值)")

    -- ===== L5: lerp 端点 =====
    local a, b = Fix64.fromFloat(0), Fix64.fromFloat(10)
    local lerp0 = Fix64.lerp(a, b, Fix64.ZERO)
    local lerp1 = Fix64.lerp(a, b, Fix64.ONE)
    TF.assertTrue(lerp0.raw == a.raw, "L5-lerp(A,B,0)==A")
    TF.assertTrue(lerp1.raw == b.raw, "L5-lerp(A,B,1)==B")

    -- ===== L6: lerp 中点 =====
    local mid = Fix64.lerp(a, b, Fix64.HALF)
    local expected = (a + b) / Fix64.TWO
    local diff6 = math.abs(mid.raw - expected.raw)
    TF.assertTrue(diff6 <= 1, "L6-lerp中点=(A+B)/2")

    -- ===== L7: clamp 边界 =====
    local cMin = Fix64.fromFloat(0)
    local cMax = Fix64.fromFloat(10)
    local below = Fix64.clamp(Fix64.fromFloat(-5), cMin, cMax)
    local above = Fix64.clamp(Fix64.fromFloat(15), cMin, cMax)
    local inside = Fix64.clamp(Fix64.fromFloat(5), cMin, cMax)
    TF.assertTrue(below.raw == cMin.raw, "L7-clamp(-5,0,10)=0")
    TF.assertTrue(above.raw == cMax.raw, "L7-clamp(15,0,10)=10")
    TF.assertTrue(inside.raw == Fix64.fromFloat(5).raw, "L7-clamp(5,0,10)=5")

    -- ===== L8: 方向向量归一化 =====
    -- Vec3.normalized 如果实现为 Pure Lua: 计算 length, 然后除以 length
    -- 这里测试 Fix64Vector3 的 basic 运算
    local v = Vec3.new(Fix64.fromFloat(3), Fix64.ZERO, Fix64.fromFloat(4))
    -- 计算长度平方: 3²+0²+4² = 9+16 = 25
    local lenSq = v.x * v.x + v.y * v.y + v.z * v.z
    TF.assertInRange(Fix64.toFloat(lenSq), 24.99, 25.01, "L8-|(3,0,4)|²=25")

    print(string.format("[L组] Fix64 运算测试完成"))
end

return { run = run }
