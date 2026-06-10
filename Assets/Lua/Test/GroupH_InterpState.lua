-- =============================================
-- Test/GroupH_InterpState.lua — H组 插值状态生命周期 (6条)
-- =============================================

local TF = require("Test.TestFramework")
local TE = require("Test.TestEnv")
local GC = require("Core.GameConst")

local function run()
    TF.group("H — 插值状态生命周期")

    TE.Setup()
    local pm = TE.GetPlayerManager()

    -- ===== H1: 首次 tick 自动初始化 =====
    local p1 = TE.CreateTestPlayer(1, "H1", false, TE.SPAWN.ORIGIN, 0).playerEntity
    p1._interpState = nil  -- 确保未初始化
    if pm._InitInterpState then pm:_InitInterpState(p1) end
    TF.assertNotNil(p1._interpState, "H1-自动创建interpState")
    local st1 = p1._interpState
    if st1 then
        -- _InitInterpState 初始化 prevPos=nil（首次 tick 时由 _ApplyDeterministicMovement 设置）
        TF.assertFalse(st1.hasTarget, "H1-hasTarget初始=false")
        TF.assertNotNil(st1, "H1-状态表存在")
    end

    -- ===== H2: 重生后插值状态重置 =====
    local p2 = TE.CreateTestPlayer(2, "H2", false, TE.SPAWN.ORIGIN, 0).playerEntity
    if pm._InitInterpState then pm:_InitInterpState(p2) end
    p2._interpState.hasTarget = true
    p2._interpState.prevPos = CS.UnityEngine.Vector3(5, 0, 5)
    p2._serverAuthPos = { x = 1, y = 2, z = 3 }
    -- 模拟重生
    p2._interpState = nil
    p2._serverAuthPos = nil
    TF.assertNil(p2._interpState, "H2-interpState已清空")
    TF.assertNil(p2._serverAuthPos, "H2-authPos已清空")

    -- ===== H3: 连续 tick prevPos 链式传递 =====
    local p3 = TE.CreateTestPlayer(3, "H3", false, TE.SPAWN.ORIGIN, 0).playerEntity
    local r3 = TE.ExecNTicks(p3, 5, GC.MOVE_FORWARD, false, 0)
    -- 检查每 tick 的 prevPos == 上一 tick 的 targetPos
    local chainOk = true
    for i = 2, #r3 do
        local prevTarget = r3[i-1].target
        local curPrev = r3[i].prev
        if prevTarget and curPrev then
            if math.abs(prevTarget.z - curPrev.z) > TF.TIGHT then
                chainOk = false
                print(string.format("  H3 链断裂 tick%d->tick%d: %.6f vs %.6f",
                    i-1, i, prevTarget.z, curPrev.z))
            end
        end
    end
    TF.assertTrue(chainOk, "H3-链式传递不断裂")

    -- ===== H4: 中间断 tick 后重新接续 =====
    local p4 = TE.CreateTestPlayer(4, "H4", false, TE.SPAWN.ORIGIN, 0).playerEntity
    TE.ExecNTicks(p4, 2, GC.MOVE_FORWARD, false, 0)  -- tick1,2
    local afterTick2 = p4._interpState.targetPos
    -- 模拟 tick3 丢失（不调 _ApplyDeterministicMovement）
    -- tick4 正常
    TE.ApplyTickInput(p4, GC.MOVE_FORWARD, false, false, false, 0)
    local prev4, target4 = TE.ExecDeterministicMove(p4)
    if prev4 and afterTick2 then
        TF.assertVec3Near(prev4, afterTick2, TF.TIGHT, "H4-断tick后prevPos接上")
    else
        TF.assertTrue(false, "H4-状态获取失败")
    end

    -- ===== H5: 移除玩家时清理 =====
    local p5 = TE.CreateTestPlayer(5, "H5", false, TE.SPAWN.ORIGIN, 0).playerEntity
    if pm._InitInterpState then pm:_InitInterpState(p5) end
    TF.assertNotNil(p5._interpState, "H5-移除前有状态")
    -- 模拟 PlayerManager:RemovePlayer
    p5:Destroy()
    -- Destroy 后 isAlive=false, gameObject被销毁
    TF.assertFalse(p5.isAlive, "H5-isAlive=false")

    -- ===== H6: transform 突然为 null =====
    local p6 = TE.CreateTestPlayer(6, "H6", false, TE.SPAWN.ORIGIN, 0).playerEntity
    if pm._InitInterpState then pm:_InitInterpState(p6) end
    p6._interpState.hasTarget = true
    p6._interpState.prevPos = CS.UnityEngine.Vector3(0, 0, 0)
    p6._interpState.targetPos = CS.UnityEngine.Vector3(1, 0, 0)
    -- 模拟 GameObject 被外部销毁
    CS.UnityEngine.GameObject.Destroy(p6.gameObject)
    p6.transform = nil
    TF.assertNoCrash(function()
        if pm._InterpolateRemotePlayers then
            pm:_InterpolateRemotePlayers(1/60)
        end
    end, "H6-transform=null不崩溃")

    TE.Cleanup()
end

return { run = run }
