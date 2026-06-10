-- =============================================
-- Test/GroupB_MultiTick.lua — B组 多tick累积一致性 (6条)
-- =============================================

local TF = require("Test.TestFramework")
local TE = require("Test.TestEnv")
local GC = require("Core.GameConst")

local WALK_PER_TICK = 5 / 15  -- ≈ 0.3333m

local function run()
    TF.group("B — 多 tick 累积一致性")

    TE.Setup()

    -- ===== B1: 连续 30 tick 匀速前进 =====
    local p1 = TE.CreateTestPlayer(1, "B1", false, TE.SPAWN.ORIGIN, 0).playerEntity
    local results = TE.ExecNTicks(p1, 30, GC.MOVE_FORWARD, false, 0)
    local firstPrev = results[1].prev   -- ★ 使用 tick1 之前的起点（而非 tick1 之后的目标）
    local lastTarget = results[30].target
    local totalDz = lastTarget.z - firstPrev.z
    -- ★ 30 tick 从起点到终点的位移
    local expectedDz = 30 * WALK_PER_TICK
    TF.assertInRange(totalDz, expectedDz - TF.LOOSE, expectedDz + TF.LOOSE, "B1-30tick总位移≈10m")
    -- 每 tick 位移一致
    local tickDists = {}
    for i = 2, 30 do
        local dz = results[i].target.z - results[i-1].target.z
        table.insert(tickDists, dz)
    end
    local allConsistent = true
    for _, d in ipairs(tickDists) do
        if math.abs(d - WALK_PER_TICK) > TF.LOOSE then allConsistent = false; break end
    end
    TF.assertTrue(allConsistent, "B1-每tick步长一致")

    -- ===== B2: 走5tick → 停5tick =====
    local p2 = TE.CreateTestPlayer(2, "B2", false, TE.SPAWN.ORIGIN, 0).playerEntity
    local r2a = TE.ExecNTicks(p2, 5, GC.MOVE_FORWARD, false, 0)
    local afterWalk = r2a[5].target.z
    local r2b = TE.ExecNTicks(p2, 5, GC.MOVE_NONE, false, 0)
    local afterStop = r2b[5].target.z
    TF.assertEqual(afterStop, afterWalk, TF.TIGHT, "B2-松手后位置不变")

    -- ===== B3: 前进5tick → 后退5tick =====
    local p3 = TE.CreateTestPlayer(3, "B3", false, TE.SPAWN.ORIGIN, 0).playerEntity
    TE.ExecNTicks(p3, 5, GC.MOVE_FORWARD, false, 0)
    local r3b = TE.ExecNTicks(p3, 5, GC.MOVE_BACKWARD, false, 0)
    local finalZ = r3b[5].target.z
    TF.assertInRange(finalZ, -0.05, 0.05, "B3-最终回到原点z≈0")

    -- ===== B4: 走路+跳跃+落地 =====
    local p4 = TE.CreateTestPlayer(4, "B4", false, TE.SPAWN.ORIGIN, 0).playerEntity
    p4.isGrounded = true
    TE.ExecNTicks(p4, 3, GC.MOVE_FORWARD, false, 0)
    -- 第4 tick跳跃
    TE.ApplyTickInput(p4, GC.MOVE_FORWARD, true, false, false, 0)
    local prev4, target4 = TE.ExecDeterministicMove(p4)
    TF.assertTrue(target4.y > prev4.y, "B4-跳跃上升")
    -- 等待落地（多 tick 无输入）
    local totalTicks = 0
    for _ = 1, 60 do  -- 最多等 4 秒
        TE.ApplyTickInput(p4, GC.MOVE_NONE, false, false, false, 0)
        local _, t = TE.ExecDeterministicMove(p4)
        totalTicks = totalTicks + 1
        if p4.isGrounded then break end
    end
    TF.assertTrue(p4.isGrounded, "B4-最终落地")
    TF.assertTrue(totalTicks < 60, "B4-落地未超时")

    -- ===== B5: 快速换向 W→S→A→D =====
    local p5 = TE.CreateTestPlayer(5, "B5", false, TE.SPAWN.ORIGIN, 0).playerEntity
    TE.ExecNTicks(p5, 3, GC.MOVE_FORWARD, false, 0)
    local r5b = TE.ExecNTicks(p5, 3, GC.MOVE_BACKWARD, false, 0)
    local r5c = TE.ExecNTicks(p5, 3, GC.MOVE_LEFT, false, 0)
    local r5d = TE.ExecNTicks(p5, 3, GC.MOVE_RIGHT, false, 0)
    -- 检查无 NaN
    local function checkNoNaN(v)
        return v and v.x == v.x and v.y == v.y and v.z == v.z
    end
    local allNoNaN = true
    for _, r in ipairs({r5b, r5c, r5d}) do
        for _, t in ipairs(r) do
            if t.target and not checkNoNaN(t.target) then allNoNaN = false; break end
        end
    end
    TF.assertTrue(allNoNaN, "B5-无NaN")

    -- ===== B6: 翻滚结束恢复正常速度 =====
    local p6 = TE.CreateTestPlayer(6, "B6", false, TE.SPAWN.ORIGIN, 0).playerEntity
    local r6a = TE.ExecNTicks(p6, 3, GC.MOVE_FORWARD + GC.MOVE_ROLL, false, 0)
    local r6b = TE.ExecNTicks(p6, 3, GC.MOVE_FORWARD, false, 0)
    -- 翻滚 tick 位移 ≈ 0.8m
    local rollDist = r6a[1].target.z - r6a[1].prev.z
    TF.assertInRange(rollDist, 0.65, 0.95, "B6-翻滚位移≈0.8m")
    -- 第4 tick（恢复正常）位移 ≈ 0.333m
    local afterRollFirst = r6b[1].target.z - r6a[3].target.z
    TF.assertInRange(afterRollFirst, WALK_PER_TICK - 0.03, WALK_PER_TICK + 0.03, "B6-恢复正常速度≈0.333m")

    TE.Cleanup()
end

return { run = run }
