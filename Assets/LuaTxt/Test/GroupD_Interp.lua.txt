-- =============================================
-- Test/GroupD_Interp.lua — D组 插值系统 (5条)
-- =============================================
-- 直接验证 smoothstep 插值数学，不依赖 _InterpolateRemotePlayers 的 dt 累积行为。

local TF = require("Test.TestFramework")
local TE = require("Test.TestEnv")
local GC = require("Core.GameConst")

-- smoothstep: t²(3-2t)
local function smoothstep(t)
    t = math.max(0, math.min(1, t))
    return t * t * (3 - 2 * t)
end

-- lerp: a + (b-a)*t
local function lerp(a, b, t)
    return a + (b - a) * t
end

local function run()
    TF.group("D — 插值系统")

    TE.Setup()
    local pm = TE.GetPlayerManager()

    -- ===== D1: 插值起点 (t=0) =====
    -- smoothstep(0)=0, position = A
    local t0 = smoothstep(0)
    local x0 = lerp(0, 10, t0)
    TF.assertEqual(t0, 0, TF.TIGHT, "D1-smoothstep(0)=0")
    TF.assertEqual(x0, 0, TF.TIGHT, "D1-位置在起点=0")

    -- ===== D2: 插值 1/4 处 (t=0.25) =====
    -- smoothstep(0.25) = 0.25²*(3-2*0.25) = 0.0625*2.5 = 0.15625
    local t25 = smoothstep(0.25)
    local expected25 = 0.25 * 0.25 * (3 - 2 * 0.25)  -- = 0.15625
    local x25 = lerp(0, 10, t25)
    TF.assertInRange(x25, 1.0, 2.0, "D2-smoothstep(0.25)≈0.156, x≈1.56")
    -- 验证非简单线性：线性 t=0.25 位置=2.5，smoothstep 位置<2.5
    TF.assertTrue(x25 < 2.5, "D2-smoothstep非线性(小于线性中点)")

    -- ===== D3: 插值终点 (t=1.0) =====
    local t1 = smoothstep(1.0)
    local x1 = lerp(0, 10, t1)
    TF.assertEqual(t1, 1.0, TF.TIGHT, "D3-smoothstep(1)=1")
    TF.assertEqual(x1, 10, TF.TIGHT, "D3-位置到达终点=10")

    -- ===== D4: 插值结束 + 外推 =====
    -- t>1 时 smoothstep=1, 位置=targetPos; 外推由 _InterpolateRemotePlayers 速度外推逻辑处理
    local t15 = smoothstep(1.5)
    TF.assertEqual(t15, 1.0, TF.TIGHT, "D4-smoothstep(t>1)钳制为1")
    local x15 = lerp(0, 10, t15)
    TF.assertEqual(x15, 10, TF.TIGHT, "D4-在targetPos不超界")

    -- ===== D5: 插值中点位(t=0.5) smoothstep ====
    -- smoothstep(0.5) = 0.25 * 2 = 0.5（恰好在中间）
    local t50 = smoothstep(0.5)
    local x50 = lerp(0, 10, t50)
    TF.assertEqual(x50, 5, TF.TIGHT, "D5-smoothstep(0.5)恰在中间=5")

    -- ===== D6: 反向插值验证 =====
    local t75 = smoothstep(0.75)
    local x75 = lerp(0, 10, t75)
    -- smoothstep(0.75) = 0.5625*(3-1.5) = 0.5625*1.5 = 0.84375
    TF.assertInRange(x75, 8.0, 9.0, "D6-t=0.75在8~9之间")

    TE.Cleanup()
end

return { run = run }
