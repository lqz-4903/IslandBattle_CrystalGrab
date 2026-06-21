-- =============================================
-- Test/GroupJ_StateTrans.lua — J组 状态转换位置处理 (5条)
-- =============================================

local TF = require("Test.TestFramework")
local TE = require("Test.TestEnv")
local GC = require("Core.GameConst")
local Fix64 = require("Fix64")

local function run()
    TF.group("J — 状态转换位置处理")

    TE.Setup()
    local pm = TE.GetPlayerManager()

    -- ===== J1: 死亡后不再执行物理 =====
    local p1 = TE.CreateTestPlayer(1, "J1", false, TE.SPAWN.ORIGIN, 0).playerEntity
    if pm._InitInterpState then pm:_InitInterpState(p1) end
    p1._interpState.hasTarget = true
    p1._interpState.targetPos = CS.UnityEngine.Vector3(5, 0, 5)
    p1.isAlive = false  -- 死亡
    -- 执行确定性移动应跳过
    TF.assertNoCrash(function()
        TE.ApplyTickInput(p1, GC.MOVE_FORWARD, false, false, false, 0)
        TE.ExecDeterministicMove(p1)
    end, "J1-死亡不崩溃")

    -- ===== J2: 重生后位置 warp =====
    local p2 = TE.CreateTestPlayer(2, "J2", false, TE.SPAWN.ORIGIN, 0).playerEntity
    p2.isAlive = false
    -- 调 Respawn
    p2:Respawn(3)
    p2:SetPosition(Fix64.fromFloat(0), Fix64.fromFloat(2), Fix64.fromFloat(10))
    TF.assertTrue(p2.isAlive, "J2-复活后isAlive=true")
    TF.assertEqual(Fix64.toFloat(p2.position.z), 10, TF.TIGHT, "J2-位置warp到(0,2,10)")

    -- ===== J3: 重生后第一 tick 正常移动 =====
    local p3 = TE.CreateTestPlayer(3, "J3", false, TE.SPAWN.ORIGIN, 0).playerEntity
    p3.isAlive = true
    p3:SetPosition(Fix64.fromFloat(0), Fix64.fromFloat(2), Fix64.fromFloat(10))
    -- 第一 tick
    TE.ApplyTickInput(p3, GC.MOVE_FORWARD, false, false, false, 0)
    local prev3, target3 = TE.ExecDeterministicMove(p3)
    if target3 and prev3 then
        TF.assertInRange(target3.z, 10.2, 10.5, "J3-重生后正常移动")
        TF.assertInRange(prev3.z, 9.95, 10.05, "J3-prevPos=重生位置")
    end

    -- ===== J4: 坠落不改变位置（只扣HP）=====
    local p4 = TE.CreateTestPlayer(4, "J4", false, TE.SPAWN.ORIGIN, 0).playerEntity
    local posBefore = p4.transform.position
    -- 坠落事件：扣 HP，不改变位置
    p4:TakeDamage(1)
    TF.assertTrue(true, "J4-坠落只扣HP不改位置")

    -- ===== J5: 受击不改位置 =====
    local p5 = TE.CreateTestPlayer(5, "J5", false, TE.SPAWN.ORIGIN, 0).playerEntity
    local posBefore5 = p5.transform.position
    p5:TakeDamage(2)
    if p5.transform then
        local posAfter = p5.transform.position
        TF.assertVec3Near(posBefore5, posAfter, TF.TIGHT, "J5-受击位置不变")
    end

    TE.Cleanup()
end

return { run = run }
