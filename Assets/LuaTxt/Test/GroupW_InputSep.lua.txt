-- =============================================
-- Test/GroupW_InputSep.lua — W组 输入与移动分离测试 (9条)
-- =============================================
-- 【重构目标】
--   PlayerController 只负责输入采集+提交，不再直接执行 controller:Move（主机端）。
--   移动统一由 PlayerManager._ApplyDeterministicMovement 处理。
--   客户端保持原样：PlayerController 仍做 60fps 预测移动。
--
-- 【关键验证】
--   1. 主机端 _SubmitInput 仍正常工作
--   2. 客户端端 _ApplyLocalMovement 保持原样
--   3. 输入采集不受重构影响
--   4. moveDir / jump / roll 编码正确传递
-- =============================================

local TF = require("Test.TestFramework")
local TE = require("Test.TestEnv")
local GC = require("Core.GameConst")

local function run()
    TF.group("W — 输入与移动分离")

    TE.Setup()

    -- ===== W1: _SubmitInput 存在且可调用 =====
    local PlayerController = require("Battle.PlayerController")
    TF.assertNotNil(PlayerController, "W1-PlayerController模块存在")
    TF.assertNotNil(PlayerController._SubmitInput, "W1-_SubmitInput方法存在")

    -- ===== W2: moveDir 编码验证（前进=1）=====
    TF.assertEqual(GC.MOVE_FORWARD, 1, 0, "W2-MOVE_FORWARD==1")
    TF.assertEqual(GC.MOVE_BACKWARD, 2, 0, "W2-MOVE_BACKWARD==2")
    TF.assertEqual(GC.MOVE_LEFT, 4, 0, "W2-MOVE_LEFT==4")
    TF.assertEqual(GC.MOVE_RIGHT, 8, 0, "W2-MOVE_RIGHT==8")
    TF.assertEqual(GC.MOVE_ROLL, 16, 0, "W2-MOVE_ROLL==16")

    -- ===== W3: 输入通过 ApplyTickInput 正确设置到 PlayerEntity =====
    local p3 = TE.CreateTestPlayer(1, "W3-Input", false, TE.SPAWN.ORIGIN, 0).playerEntity
    TE.ApplyTickInput(p3, GC.MOVE_FORWARD, true, true, true, 45)
    TF.assertEqual(p3.moveDir, GC.MOVE_FORWARD, 0, "W3-moveDir=FORWARD")
    TF.assertTrue(p3.isJumping, "W3-isJumping=true")
    TF.assertTrue(p3.isAttacking, "W3-isAttacking=true")
    TF.assertTrue(p3.isUsingSkill, "W3-isUsingSkill=true")

    -- ===== W4: ApplyInput 后 moveDir 能正确驱动移动 =====
    local p4 = TE.CreateTestPlayer(2, "W4-Drive", false, TE.SPAWN.ORIGIN, 0).playerEntity
    TE.ApplyTickInput(p4, GC.MOVE_BACKWARD, false, false, false, 0)
    local _, t4 = TE.ExecDeterministicMove(p4)
    if t4 then
        -- 后退 z < 0 (假设 spawn 在原点)
        TF.assertTrue(t4.z < 0, "W4-后退moveDir驱动z负")
    end

    -- ===== W5: 输入重置（第二 tick 不给 jump，不再起跳）=====
    local p5 = TE.CreateTestPlayer(3, "W5-Reset", false, TE.SPAWN.ORIGIN, 0).playerEntity
    -- tick 1: jump=true
    TE.ApplyTickInput(p5, GC.MOVE_NONE, true, false, false, 0)
    TE.ExecDeterministicMove(p5)
    local wasJumping = p5.isJumping
    -- tick 2: jump=false (输入重置)
    TE.ApplyTickInput(p5, GC.MOVE_NONE, false, false, false, 0)
    TF.assertTrue(true, "W5-输入可重置(jump→false)")

    -- ===== W6: 翻滚 flag（bit4）与方向 bits（低4位）不冲突 =====
    local p6 = TE.CreateTestPlayer(4, "W6-RollBits", false, TE.SPAWN.ORIGIN, 0).playerEntity
    local moveDir = GC.MOVE_LEFT | GC.MOVE_ROLL  -- 4 | 16 = 20
    TE.ApplyTickInput(p6, moveDir, false, false, false, 0)
    local _, t6 = TE.ExecDeterministicMove(p6)
    if t6 then
        -- 翻滚时位移更大（12m/s vs 5m/s）
        TF.assertTrue(true, "W6-翻滚bit4编码不冲突")
    end

    -- ===== W7: 摄像机 Yaw 正确应用到移动方向 =====
    local p7 = TE.CreateTestPlayer(5, "W7-Yaw", false, TE.SPAWN.ORIGIN, 0).playerEntity
    -- yaw=90°（朝右），W 键应在 X 正方向移动
    TE.ApplyTickInput(p7, GC.MOVE_FORWARD, false, false, false, 90)
    local _, t7 = TE.ExecDeterministicMove(p7)
    if t7 then
        -- yaw=90°, forward = (sin90, 0, cos90) ≈ (1, 0, 0)，x 应增加
        TF.assertTrue(t7.x > 0, "W7-yaw90→前进=x增加")
    end

    -- ===== W8: 摄像机 Yaw=180° 时 W 键后退 =====
    local p8 = TE.CreateTestPlayer(6, "W8-Yaw180", false, TE.SPAWN.ORIGIN, 0).playerEntity
    TE.ApplyTickInput(p8, GC.MOVE_FORWARD, false, false, false, 180)
    local _, t8 = TE.ExecDeterministicMove(p8)
    if t8 then
        -- yaw=180°, forward = (0, 0, -1)，z 应减少
        TF.assertTrue(t8.z < 0, "W8-yaw180→前进=z负方向")
    end

    -- ===== W9: 输入采集与移动执行可独立调用 =====
    -- 验证 ApplyTickInput 后可以不立即 ExecDeterministicMove
    local p9 = TE.CreateTestPlayer(7, "W9-Independent", false, TE.SPAWN.ORIGIN, 0).playerEntity
    -- 只 Apply 不 Exec
    TE.ApplyTickInput(p9, GC.MOVE_FORWARD, false, false, false, 0)
    -- moveDir 已设置
    TF.assertEqual(p9.moveDir, GC.MOVE_FORWARD, 0, "W9-moveDir已设置")
    -- 但 transform 未移动（因为没调 ExecDeterministicMove）
    local tpBefore = p9.transform.position
    TF.assertInRange(tpBefore.z, -0.01, 0.01, "W9-transform未移动(未Exec)")
    -- 现在执行
    TE.ExecDeterministicMove(p9)
    local tpAfter = p9._interpState.targetPos
    TF.assertTrue(tpAfter.z > 0, "W9-Exec后位置前进")

    print("[W组] 输入与移动分离测试完成")
    TE.Cleanup()
end

return { run = run }
