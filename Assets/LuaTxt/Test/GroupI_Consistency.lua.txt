-- =============================================
-- Test/GroupI_Consistency.lua — I组 双玩家位置一致性 (4条)
-- =============================================
-- ★ 帧同步的终极数学保证：同输入→同位置

local TF = require("Test.TestFramework")
local TE = require("Test.TestEnv")
local GC = require("Core.GameConst")

local function run()
    TF.group("I — 双玩家位置一致性")

    TE.Setup()

    -- ===== I1: 两个远程玩家同输入→同位置 =====
    local pA = TE.CreateTestPlayer(1, "I1-A", false, TE.SPAWN.ORIGIN, 0).playerEntity
    local pB = TE.CreateTestPlayer(2, "I1-B", false, TE.SPAWN.ORIGIN, 0).playerEntity
    local allEqual = true
    for i = 1, 60 do
        local moveDir = GC.MOVE_FORWARD
        if i > 20 and i <= 25 then moveDir = GC.MOVE_LEFT end
        if i > 40 then moveDir = GC.MOVE_FORWARD + GC.MOVE_RIGHT end

        TE.ApplyTickInput(pA, moveDir, false, false, false, 0)
        TE.ApplyTickInput(pB, moveDir, false, false, false, 0)
        local _, tA = TE.ExecDeterministicMove(pA)
        local _, tB = TE.ExecDeterministicMove(pB)

        if tA and tB then
                -- ★ 使用容差比较（PhysX 浮点允许 1mm 误差）
                local dx = math.abs(tA.x - tB.x)
                local dy = math.abs(tA.y - tB.y)
                local dz = math.abs(tA.z - tB.z)
                if dx > TF.TIGHT or dy > TF.TIGHT or dz > TF.TIGHT then
                allEqual = false
                print(string.format("  I1 差异 tick=%d: (%.6f,%.6f,%.6f) vs (%.6f,%.6f,%.6f)",
                    i, tA.x, tA.y, tA.z, tB.x, tB.y, tB.z))
                break
            end
        end
    end
    TF.assertTrue(allEqual, "I1-同输入→逐tick完全一致(60tick)")

    -- ===== I2: 不同输入→不同位置（独立性）=====
    local pC = TE.CreateTestPlayer(3, "I2-C", false, TE.SPAWN.ORIGIN, 0).playerEntity
    local pD = TE.CreateTestPlayer(4, "I2-D", false, TE.SPAWN.ORIGIN, 0).playerEntity
    local rC = TE.ExecNTicks(pC, 30, GC.MOVE_FORWARD, false, 0)
    local rD = TE.ExecNTicks(pD, 30, GC.MOVE_BACKWARD, false, 0)
    local lastC = rC[30].target
    local lastD = rD[30].target
    if lastC and lastD then
        TF.assertTrue(lastC.z > 0, "I2-C往前走(z>0)")
        TF.assertTrue(lastD.z < 0, "I2-D往后走(z<0)")
        TF.assertTrue(lastC.z ~= lastD.z, "I2-两者位置不同(独立)")
    end

    -- ===== I3: 主机模拟 vs 客户端模拟（同输入对比）=====
    -- 在同一进程中验证：两个完全相同的初始条件 + 相同输入 → 相同结果
    local pHost = TE.CreateTestPlayer(5, "I3-Host", false, TE.SPAWN.ORIGIN, 0).playerEntity
    local pClient = TE.CreateTestPlayer(6, "I3-Client", false, TE.SPAWN.ORIGIN, 0).playerEntity
    local i3Inputs = { GC.MOVE_FORWARD, GC.MOVE_FORWARD, GC.MOVE_LEFT, GC.MOVE_FORWARD,
                       GC.MOVE_NONE, GC.MOVE_RIGHT, GC.MOVE_FORWARD, GC.MOVE_BACKWARD,
                       GC.MOVE_FORWARD + GC.MOVE_LEFT, GC.MOVE_FORWARD }
    local i3Equal = true
    for _, dir in ipairs(i3Inputs) do
        TE.ApplyTickInput(pHost, dir, false, false, false, 0)
        TE.ApplyTickInput(pClient, dir, false, false, false, 0)
        local _, th = TE.ExecDeterministicMove(pHost)
        local _, tc = TE.ExecDeterministicMove(pClient)
        if th and tc then
                local dx = math.abs(th.x - tc.x)
                local dy = math.abs(th.y - tc.y)
                local dz = math.abs(th.z - tc.z)
                if dx > TF.TIGHT or dy > TF.TIGHT or dz > TF.TIGHT then i3Equal = false; break end
        end
    end
    TF.assertTrue(i3Equal, "I3-主机vs客户端模拟逐tick一致")

    -- ===== I4: 不同起点同位移量 =====
    local pE = TE.CreateTestPlayer(7, "I4-E", false, TE.SPAWN.ORIGIN, 0).playerEntity
    local pF = TE.CreateTestPlayer(8, "I4-F", false, TE.SPAWN.FAR, 0).playerEntity
    local rE = TE.ExecNTicks(pE, 10, GC.MOVE_FORWARD, false, 0)
    local rF = TE.ExecNTicks(pF, 10, GC.MOVE_FORWARD, false, 0)
    if rE[10].target and rE[1].prev and rF[10].target and rF[1].prev then
        local dzE = rE[10].target.z - rE[1].prev.z
        local dzF = rF[10].target.z - rF[1].prev.z
        TF.assertInRange(dzE, 3.0, 3.7, "I4-原点位移≈3.33m")
        TF.assertInRange(dzF, 3.0, 3.7, "I4-远处位移≈3.33m")
        TF.assertInRange(math.abs(dzE - dzF), 0, 0.1, "I4-不同起点→同位移量")
    end

    TE.Cleanup()
end

return { run = run }
