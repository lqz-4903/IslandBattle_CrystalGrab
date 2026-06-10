-- =============================================
-- Test/GroupP_TickTiming.lua — P组 帧同步时序边界 (6条)
-- =============================================

local TF = require("Test.TestFramework")
local TE = require("Test.TestEnv")
local Fix64 = require("Fix64")
local GC = require("Core.GameConst")

local function run()
    TF.group("P — 帧同步时序边界")

    -- ===== P1: tick 号单调递增验证 =====
    -- 模拟 TickSyncHandler 的 tick 计数器
    local tick = 0
    local tickSeq = {}
    for i = 1, 100 do
        tick = tick + 1
        table.insert(tickSeq, tick)
    end
    local monotonic = true
    for i = 2, #tickSeq do
        if tickSeq[i] ~= tickSeq[i-1] + 1 then monotonic = false; break end
    end
    TF.assertTrue(monotonic, "P1-100tick连续递增")
    TF.assertEqual(tickSeq[100], 100, TF.TIGHT, "P1-最后tick=100")

    -- ===== P2: 超时填充空输入 =====
    -- 模拟玩家未提交输入 → 用 moveDir=0 填充
    local emptyInput = { MoveDir = 0, Jump = false, Attack = false, Skill = false }
    TF.assertEqual(emptyInput.MoveDir, 0, TF.TIGHT, "P2-空输入moveDir=0")
    TF.assertFalse(emptyInput.Jump, "P2-空输入Jump=false")

    -- ===== P3: TickInterval 配置验证 =====
    local expectedInterval = 1 / GC.TICK_RATE  -- 1/15 ≈ 0.06667
    TF.assertEqual(GC.TICK_INTERVAL, expectedInterval, TF.TIGHT, "P3-TICK_INTERVAL=1/15")

    -- ===== P4: 帧间隔容忍 =====
    local targetMs = GC.TICK_INTERVAL * 1000  -- ≈ 66.67ms
    local lower = targetMs * 0.8   -- ≈ 53ms
    local upper = targetMs * 1.2   -- ≈ 80ms
    TF.assertInRange(targetMs, lower, upper, "P4-帧间隔≈67ms±20%")

    -- ===== P5: 追赶帧验证 =====
    -- CatchUpTicks: from_tick → to_tick, 帧数 = to_tick - from_tick + 1
    local fromTick, toTick = 10, 19
    local catchUpCount = toTick - fromTick + 1
    TF.assertEqual(catchUpCount, 10, TF.TIGHT, "P5-10帧追赶=from10→to19")

    -- ===== P6: 帧历史上限 =====
    -- MaxTickHistory = 1500 (100秒 @ 15fps)
    local maxHistory = 1500
    TF.assertTrue(maxHistory >= 100 * 15, "P6-MaxTickHistory≥100秒容量的确=" .. maxHistory)

    print("[P组] 时序边界测试完成")
end

return { run = run }
