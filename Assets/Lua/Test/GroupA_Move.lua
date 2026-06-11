-- =============================================
-- Test/GroupA_Move.lua — A组 单tick基础移动 (13条)
-- =============================================

local TF = require("Test.TestFramework")
local TE = require("Test.TestEnv")
local GC = require("Core.GameConst")
local Fix64 = require("Fix64")

local MOVE_INTERVAL = GC.TICK_INTERVAL   -- 秒/tick
local WALK_SPEED = GC.MOVE_SPEED        -- m/s
local WALK_PER_TICK = WALK_SPEED * MOVE_INTERVAL
local ROLL_SPEED = 12
local ROLL_PER_TICK = ROLL_SPEED * MOVE_INTERVAL  -- ≈ 0.8m

local function run()
    TF.group("A — 单 tick 基础移动")

    TE.Setup()

    -- ===== A1: 静止无输入不移动 =====
    local p = TE.CreateTestPlayer(1, "A1", false, TE.SPAWN.ORIGIN, 0).playerEntity
    TE.ApplyTickInput(p, GC.MOVE_NONE, false)
    local prev, target = TE.ExecDeterministicMove(p)
    if prev and target then
        TF.assertVec3Near(prev, target, TF.TIGHT, "A1-静止 prevPos==targetPos")
        TF.assertEqual(prev.x, target.x, TF.TIGHT, "A1-x不变")
        TF.assertEqual(prev.z, target.z, TF.TIGHT, "A1-z不变")
    else
        TF.assertTrue(false, "A1-插值状态为空")
    end

    -- ===== A2: 向前移动 (yaw=0, W) =====
    local p2 = TE.CreateTestPlayer(2, "A2", false, TE.SPAWN.ORIGIN, 0).playerEntity
    TE.ApplyTickInput(p2, GC.MOVE_FORWARD, false, false, false, 0)
    local prev2, target2 = TE.ExecDeterministicMove(p2)
    if target2 and prev2 then
        TF.assertEqual(target2.z - prev2.z, WALK_PER_TICK, TF.TIGHT, "A2-向前约0.333m")
        TF.assertEqual(target2.x, prev2.x, TF.TIGHT, "A2-无侧移x")
        TF.assertEqual(target2.y, prev2.y, TF.TIGHT, "A2-高度不变")
    else
        TF.assertTrue(false, "A2-插值状态为空")
    end

    -- ===== A3: 向后移动 (yaw=0, S) =====
    local p3 = TE.CreateTestPlayer(3, "A3", false, TE.SPAWN.ORIGIN, 0).playerEntity
    TE.ApplyTickInput(p3, GC.MOVE_BACKWARD, false, false, false, 0)
    local prev3, target3 = TE.ExecDeterministicMove(p3)
    if target3 and prev3 then
        TF.assertInRange(target3.z - prev3.z, -WALK_PER_TICK - TF.TIGHT, -WALK_PER_TICK + TF.TIGHT, "A3-向后z减少")
        TF.assertEqual(target3.x, prev3.x, TF.TIGHT, "A3-无侧移")
    else
        TF.assertTrue(false, "A3-插值状态为空")
    end

    -- ===== A4: 向左移动 (yaw=0, A) =====
    local p4 = TE.CreateTestPlayer(4, "A4", false, TE.SPAWN.ORIGIN, 0).playerEntity
    TE.ApplyTickInput(p4, GC.MOVE_LEFT, false, false, false, 0)
    local prev4, target4 = TE.ExecDeterministicMove(p4)
    if target4 and prev4 then
        TF.assertInRange(target4.x - prev4.x, -WALK_PER_TICK - TF.TIGHT, -WALK_PER_TICK + TF.TIGHT, "A4-向左x减少")
        TF.assertEqual(target4.z, prev4.z, TF.TIGHT, "A4-z不变")
    else
        TF.assertTrue(false, "A4-插值状态为空")
    end

    -- ===== A5: 向右移动 (yaw=0, D) =====
    local p5 = TE.CreateTestPlayer(5, "A5", false, TE.SPAWN.ORIGIN, 0).playerEntity
    TE.ApplyTickInput(p5, GC.MOVE_RIGHT, false, false, false, 0)
    local prev5, target5 = TE.ExecDeterministicMove(p5)
    if target5 and prev5 then
        TF.assertInRange(target5.x - prev5.x, WALK_PER_TICK - TF.TIGHT, WALK_PER_TICK + TF.TIGHT, "A5-向右x增加")
        TF.assertEqual(target5.z, prev5.z, TF.TIGHT, "A5-z不变")
    else
        TF.assertTrue(false, "A5-插值状态为空")
    end

    -- ===== A6: 斜向移动 W+A =====
    local p6 = TE.CreateTestPlayer(6, "A6", false, TE.SPAWN.ORIGIN, 0).playerEntity
    TE.ApplyTickInput(p6, GC.MOVE_FORWARD + GC.MOVE_LEFT, false, false, false, 0)
    local prev6, target6 = TE.ExecDeterministicMove(p6)
    if target6 and prev6 then
        local dx = target6.x - prev6.x
        local dz = target6.z - prev6.z
        local dist = math.sqrt(dx*dx + dz*dz)
        TF.assertInRange(dist, WALK_PER_TICK - 0.002, WALK_PER_TICK + 0.002, "A6-斜向位移量≈0.333m")
        TF.assertTrue(dx < 0, "A6-斜向左(x负)")
        TF.assertTrue(dz > 0, "A6-斜向前(z正)")
    else
        TF.assertTrue(false, "A6-插值状态为空")
    end

    -- ===== A7: 斜向移动 S+A =====
    local p7 = TE.CreateTestPlayer(7, "A7", false, TE.SPAWN.ORIGIN, 0).playerEntity
    TE.ApplyTickInput(p7, GC.MOVE_BACKWARD + GC.MOVE_LEFT, false, false, false, 0)
    local prev7, target7 = TE.ExecDeterministicMove(p7)
    if target7 and prev7 then
        local dx = target7.x - prev7.x
        local dz = target7.z - prev7.z
        local dist = math.sqrt(dx*dx + dz*dz)
        TF.assertInRange(dist, WALK_PER_TICK - 0.002, WALK_PER_TICK + 0.002, "A7-斜向位移量≈0.333m")
        TF.assertTrue(dx < 0, "A7-左")
        TF.assertTrue(dz < 0, "A7-后")
    else
        TF.assertTrue(false, "A7-插值状态为空")
    end

    -- ===== A8: yaw=90°(朝右) 按 W =====
    local p8 = TE.CreateTestPlayer(8, "A8", false, TE.SPAWN.ORIGIN, 90).playerEntity
    TE.ApplyTickInput(p8, GC.MOVE_FORWARD, false, false, false, 90)
    local prev8, target8 = TE.ExecDeterministicMove(p8)
    if target8 and prev8 then
        TF.assertEqual(target8.x - prev8.x, WALK_PER_TICK, TF.LOOSE, "A8-朝右x增加≈0.333m")
        TF.assertEqual(target8.z, prev8.z, TF.LOOSE, "A8-z≈0")
    else
        TF.assertTrue(false, "A8-插值状态为空")
    end

    -- ===== A9: yaw=180°(朝后) 按 W =====
    local p9 = TE.CreateTestPlayer(9, "A9", false, TE.SPAWN.ORIGIN, 180).playerEntity
    TE.ApplyTickInput(p9, GC.MOVE_FORWARD, false, false, false, 180)
    local prev9, target9 = TE.ExecDeterministicMove(p9)
    if target9 and prev9 then
        TF.assertInRange(target9.z - prev9.z, -WALK_PER_TICK - TF.LOOSE, -WALK_PER_TICK + TF.LOOSE, "A9-朝后z减少≈0.333m")
        TF.assertEqual(target9.x, prev9.x, TF.LOOSE, "A9-x≈0")
    else
        TF.assertTrue(false, "A9-插值状态为空")
    end

    -- ===== A10: 翻滚 =====
    local p10 = TE.CreateTestPlayer(10, "A10", false, TE.SPAWN.ORIGIN, 0).playerEntity
    TE.ApplyTickInput(p10, GC.MOVE_FORWARD + GC.MOVE_ROLL, false, false, false, 0)
    local prev10, target10 = TE.ExecDeterministicMove(p10)
    if target10 and prev10 then
        local dist = math.abs(target10.z - prev10.z)
        TF.assertInRange(dist, ROLL_PER_TICK - 0.01, ROLL_PER_TICK + 0.01, "A10-翻滚位移≈0.8m")
        TF.assertTrue(dist > WALK_PER_TICK + 0.05, "A10-翻滚比走路快")
        TF.assertEqual(target10.x, prev10.x, TF.TIGHT, "A10-方向正确")
    else
        TF.assertTrue(false, "A10-插值状态为空")
    end

    -- ===== A11: 跳跃首帧 =====
    local p11 = TE.CreateTestPlayer(11, "A11", false, TE.SPAWN.ORIGIN, 0).playerEntity
    p11.isGrounded = true
    TE.ApplyTickInput(p11, GC.MOVE_FORWARD, true, false, false, 0)
    local prev11, target11 = TE.ExecDeterministicMove(p11)
    if target11 and prev11 then
        TF.assertTrue(target11.y > prev11.y, "A11-上升")
        TF.assertInRange(target11.z - prev11.z, WALK_PER_TICK - TF.LOOSE, WALK_PER_TICK + TF.LOOSE, "A11-水平移动不受影响")
        TF.assertFalse(p11.isGrounded, "A11-离地")
    else
        TF.assertTrue(false, "A11-插值状态为空")
    end

    -- ===== A12: 空中无输入（重力）=====
    local p12 = TE.CreateTestPlayer(12, "A12", false, TE.SPAWN.AIR, 0).playerEntity
    p12.isGrounded = false
    p12.velocity = require("Fix64Vector3").new(
        require("Fix64").ZERO,
        require("Fix64").fromFloat(8),  -- upward velocity
        require("Fix64").ZERO
    )
    TE.ApplyTickInput(p12, GC.MOVE_NONE, false, false, false, 0)
    local prev12, target12 = TE.ExecDeterministicMove(p12)
    if target12 and prev12 then
        -- 初始 y=5 且有 8m/s 向上速度，第一个 tick 先有向上的位移，但重力会把速度拉下来
        -- 这里只检查 xz 不变即可
        TF.assertEqual(target12.x, prev12.x, TF.TIGHT, "A12-xz不变(x)")
        TF.assertEqual(target12.z, prev12.z, TF.TIGHT, "A12-xz不变(z)")
    else
        TF.assertTrue(false, "A12-插值状态为空")
    end

    -- ===== A13: 空中可移动 =====
    local p13 = TE.CreateTestPlayer(13, "A13", false, TE.SPAWN.AIR, 0).playerEntity
    p13.isGrounded = false
    TE.ApplyTickInput(p13, GC.MOVE_FORWARD, false, false, false, 0)
    local prev13, target13 = TE.ExecDeterministicMove(p13)
    if target13 and prev13 then
        TF.assertTrue(target13.z > prev13.z, "A13-空中可水平移动")
        TF.assertEqual(target13.x, prev13.x, TF.TIGHT, "A13-无侧移")
    else
        TF.assertTrue(false, "A13-插值状态为空")
    end

    TE.Cleanup()
end

return { run = run }
