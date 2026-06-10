-- =============================================
-- Test/GroupS_UnifiedPath.lua — S组 统一物理路径基础测试 (12条)
-- =============================================
-- 【重构目标】
--   主机本地玩家不再走 PlayerController._ApplyLocalMovement（60fps），
--   改为跟远程玩家一样走 _ApplyDeterministicMovement（15fps）。
--   本组测试验证 _ApplyDeterministicMovement 对所有类型玩家都正确。
--
-- 【失败即阻塞】
--   本组任何失败 = 不能联机，因为位置产出路径不一致。
-- =============================================

local TF = require("Test.TestFramework")
local TE = require("Test.TestEnv")
local GC = require("Core.GameConst")
local Fix64 = require("Fix64")

local function run()
    TF.group("S — 统一物理路径")

    TE.Setup()
    local pm = TE.GetPlayerManager()

    -- ===== S1: 本地玩家单 tick W 前进 =====
    -- 验证 _ApplyDeterministicMovement 对 isLocal=true 的玩家有效
    local p1 = TE.CreateTestPlayer(1, "S1-Local", true, TE.SPAWN.ORIGIN, 0).playerEntity
    TE.ApplyTickInput(p1, GC.MOVE_FORWARD, false, false, false, 0)
    local prev1, target1 = TE.ExecDeterministicMove(p1)
    if prev1 and target1 then
        -- 向前移动后 z > 0
        TF.assertTrue(target1.z > prev1.z, "S1-本地玩家前进(z增加)")
        TF.assertInRange(target1.x, -0.05, 0.05, "S1-前进时x不变")
    else
        TF.assertTrue(false, "S1-ExecDeterministicMove返回nil")
    end

    -- ===== S2: 本地玩家单 tick 不输入 =====
    local p2 = TE.CreateTestPlayer(2, "S2-Idle", true, TE.SPAWN.ORIGIN, 0).playerEntity
    TE.ApplyTickInput(p2, GC.MOVE_NONE, false, false, false, 0)
    local prev2, target2 = TE.ExecDeterministicMove(p2)
    if prev2 and target2 then
        -- 静止时 prevPos 和 targetPos 应非常接近
        -- (PhysX 可能微调 y，但 xz 应不变)
        TF.assertInRange(target2.x - prev2.x, -TF.LOOSE, TF.LOOSE, "S2-静止时x不变")
        TF.assertInRange(target2.z - prev2.z, -TF.LOOSE, TF.LOOSE, "S2-静止时z不变")
    else
        TF.assertTrue(false, "S2-ExecDeterministicMove返回nil")
    end

    -- ===== S3: 本地玩家多 tick 累积位移 ≈ 预期 =====
    local p3 = TE.CreateTestPlayer(3, "S3-MultiTick", true, TE.SPAWN.ORIGIN, 0).playerEntity
    local results3 = TE.ExecNTicks(p3, 30, GC.MOVE_FORWARD, false, 0)
    if results3[30] and results3[1] then
        local dz = results3[30].target.z - results3[1].prev.z
        -- 30 tick * 1/15s * 5m/s = 10m，允许 PhysX ±1m
        TF.assertInRange(dz, 6, 12, "S3-30tick前进位移≈10m")
    end

    -- ===== S4: 本地玩家跳跃 =====
    local p4 = TE.CreateTestPlayer(4, "S4-Jump", true, TE.SPAWN.ORIGIN, 0).playerEntity
    p4.isGrounded = true
    TE.ApplyTickInput(p4, GC.MOVE_NONE, true, false, false, 0)  -- jump=true
    local prev4, target4 = TE.ExecDeterministicMove(p4)
    if prev4 and target4 then
        -- 起跳后 y 应该升高（> spawnPos.y）
        TF.assertTrue(target4.y > 0.09, "S4-起跳后Y升高")
        TF.assertTrue(not p4.isGrounded, "S4-起跳后离地")
    end

    -- ===== S5: 本地玩家翻滚（bit4 编码）=====
    local p5 = TE.CreateTestPlayer(5, "S5-Roll", true, TE.SPAWN.ORIGIN, 0).playerEntity
    local rollDir = GC.MOVE_FORWARD | GC.MOVE_ROLL  -- bit0 + bit4
    TE.ApplyTickInput(p5, rollDir, false, false, false, 0)
    local prev5, target5 = TE.ExecDeterministicMove(p5)
    if prev5 and target5 then
        local dz5 = target5.z - prev5.z
        -- 翻滚速度 12m/s，单 tick 1/15s → 12/15 = 0.8m
        TF.assertInRange(dz5, 0.5, 1.2, "S5-翻滚单tick位移≈0.8m")
    end

    -- ===== S6: 本地玩家斜向移动 =====
    local p6 = TE.CreateTestPlayer(6, "S6-Diag", true, TE.SPAWN.ORIGIN, 0).playerEntity
    local diagDir = GC.MOVE_FORWARD | GC.MOVE_RIGHT
    TE.ApplyTickInput(p6, diagDir, false, false, false, 0)
    local prev6, target6 = TE.ExecDeterministicMove(p6)
    if prev6 and target6 then
        -- 斜向前进 (x>0, z>0)，归一化后速度仍为 5m/s
        TF.assertTrue(target6.x > prev6.x, "S6-斜向x>0")
        TF.assertTrue(target6.z > prev6.z, "S6-斜向z>0")
    end

    -- ===== S7: 本地玩家左移 =====
    local p7 = TE.CreateTestPlayer(7, "S7-Left", true, TE.SPAWN.ORIGIN, 0).playerEntity
    TE.ApplyTickInput(p7, GC.MOVE_LEFT, false, false, false, 0)
    local prev7, target7 = TE.ExecDeterministicMove(p7)
    if prev7 and target7 then
        TF.assertTrue(target7.x < prev7.x, "S7-左移x<0")
        TF.assertInRange(target7.z - prev7.z, -0.01, 0.01, "S7-左移时z不变")
    end

    -- ===== S8: 本地玩家后退 =====
    local p8 = TE.CreateTestPlayer(8, "S8-Back", true, TE.SPAWN.ORIGIN, 0).playerEntity
    TE.ApplyTickInput(p8, GC.MOVE_BACKWARD, false, false, false, 0)
    local prev8, target8 = TE.ExecDeterministicMove(p8)
    if prev8 and target8 then
        TF.assertTrue(target8.z < prev8.z, "S8-后退z<0")
    end

    -- ===== S9: 远程玩家前进（对照组）=====
    local p9 = TE.CreateTestPlayer(9, "S9-Remote", false, TE.SPAWN.ORIGIN, 0).playerEntity
    TE.ApplyTickInput(p9, GC.MOVE_FORWARD, false, false, false, 0)
    local prev9, target9 = TE.ExecDeterministicMove(p9)
    if prev9 and target9 then
        TF.assertTrue(target9.z > prev9.z, "S9-远程玩家前进(z增加)")
    end

    -- ===== S10: 本地+远程 同输入产生同位移 =====
    -- ★ 这是统一路径的核心保证：同一物理函数 + 同输入 = 同位置
    local pL = TE.CreateTestPlayer(10, "S10-Local", true, TE.SPAWN.ORIGIN, 0).playerEntity
    local pR = TE.CreateTestPlayer(11, "S10-Remote", false, TE.SPAWN.ORIGIN, 0).playerEntity
    local s10Equal = true
    for i = 1, 30 do
        local dir = GC.MOVE_FORWARD
        if i > 10 and i <= 15 then dir = GC.MOVE_LEFT end
        if i > 20 then dir = GC.MOVE_FORWARD | GC.MOVE_RIGHT end

        TE.ApplyTickInput(pL, dir, false, false, false, 0)
        TE.ApplyTickInput(pR, dir, false, false, false, 0)
        local _, tL = TE.ExecDeterministicMove(pL)
        local _, tR = TE.ExecDeterministicMove(pR)
        if tL and tR then
            -- ★ 使用容差比较（PhysX 浮点运算允许 1mm 内误差）
            local dx = math.abs(tL.x - tR.x)
            local dy = math.abs(tL.y - tR.y)
            local dz = math.abs(tL.z - tR.z)
            if dx > TF.TIGHT or dy > TF.TIGHT or dz > TF.TIGHT then
                s10Equal = false
                print(string.format("  S10 tick=%d: L(%.6f,%.6f,%.6f) vs R(%.6f,%.6f,%.6f)",
                    i, tL.x, tL.y, tL.z, tR.x, tR.y, tR.z))
                break
            end
        end
    end
    -- ★ 核心断言：同一进程内同输入必须产出一致位置
    TF.assertTrue(s10Equal, "S10-本地+远程30tick同输入→同位置")

    -- ===== S11: 本地玩家插值状态正确初始化和使用 =====
    local p11 = TE.CreateTestPlayer(12, "S11-Interp", true, TE.SPAWN.ORIGIN, 0).playerEntity
    if pm._InitInterpState then pm:_InitInterpState(p11) end
    local st11 = p11._interpState
    TF.assertNotNil(st11, "S11-本地玩家插值状态已创建")
    if st11 then
        -- 初始时 prevPos/targetPos 应为 nil（等待第一个 tick）
        TF.assertTrue(not st11.hasTarget, "S11-hasTarget初始=false")
        -- 执行一个 tick
        TE.ApplyTickInput(p11, GC.MOVE_FORWARD, false, false, false, 0)
        TE.ExecDeterministicMove(p11)
        TF.assertTrue(st11.hasTarget, "S11-首tick后hasTarget=true")
        TF.assertNotNil(st11.prevPos, "S11-首tick后prevPos不为nil")
        TF.assertNotNil(st11.targetPos, "S11-首tick后targetPos不为nil")
    end

    -- ===== S12: 本地玩家连续两 tick 的 prevPos→targetPos 链 =====
    local p12 = TE.CreateTestPlayer(13, "S12-Chain", true, TE.SPAWN.ORIGIN, 0).playerEntity
    TE.ApplyTickInput(p12, GC.MOVE_FORWARD, false, false, false, 0)
    local _, tA = TE.ExecDeterministicMove(p12)
    -- tick 2
    TE.ApplyTickInput(p12, GC.MOVE_FORWARD, false, false, false, 0)
    local prevB, tB = TE.ExecDeterministicMove(p12)
    if tA and prevB then
        -- tick1 的 targetPos 应该等于 tick2 的 prevPos（链式传递）
        TF.assertInRange(prevB.x - tA.x, -TF.TIGHT, TF.TIGHT, "S12-prevX链=tick1目标x")
        TF.assertInRange(prevB.y - tA.y, -TF.TIGHT, TF.TIGHT, "S12-prevY链=tick1目标y")
        TF.assertInRange(prevB.z - tA.z, -TF.TIGHT, TF.TIGHT, "S12-prevZ链=tick1目标z")
    end

    print("[S组] 统一物理路径测试完成")
    TE.Cleanup()
end

return { run = run }
