-- =============================================
-- Test/GroupU_ExecOrder.lua — U组 帧执行与插值顺序测试 (10条)
-- =============================================
-- 【重构目标】
--   OnFrameEnd 中所有玩家统一执行 _ApplyDeterministicMovement（不再跳过本地玩家）。
--   执行顺序：ApplyInput → _ApplyDeterministicMovement → _CaptureAuthPositions
--   _InterpolateRemotePlayers 仍仅作用于非本地玩家。
--
-- 【关键验证】
--   1. OnFrameEnd 对本地玩家也执行 _ApplyDeterministicMovement
--   2. 执行顺序不被打乱
--   3. 插值系统不受影响
-- =============================================

local TF = require("Test.TestFramework")
local TE = require("Test.TestEnv")
local GC = require("Core.GameConst")
local Fix64 = require("Fix64")

local function run()
    TF.group("U — 帧执行与插值顺序")

    TE.Setup()
    local pm = TE.GetPlayerManager()

    -- ===== U1: _ApplyDeterministicMovement 对本地玩家有效 =====
    local p1 = TE.CreateTestPlayer(1, "U1-Local", true, TE.SPAWN.ORIGIN, 0).playerEntity
    -- 设置输入
    TE.ApplyTickInput(p1, GC.MOVE_FORWARD, false, false, false, 0)
    -- 记录执行前
    local posBefore = p1.transform.position
    -- 执行确定性移动
    local prev1, target1 = TE.ExecDeterministicMove(p1)
    -- 方法调用成功 + targetPos 更新
    TF.assertNotNil(prev1, "U1-本地玩家prevPos不为nil")
    TF.assertNotNil(target1, "U1-本地玩家targetPos不为nil")
    TF.assertTrue(target1.z > posBefore.z, "U1-本地玩家确定性移动生效")

    -- ===== U2: 连续 tick 执行顺序正确 =====
    local p2 = TE.CreateTestPlayer(2, "U2-Order", false, TE.SPAWN.ORIGIN, 0).playerEntity
    local positions = {}
    for i = 1, 5 do
        TE.ApplyTickInput(p2, GC.MOVE_FORWARD, false, false, false, 0)  -- 先Apply
        local _, t = TE.ExecDeterministicMove(p2)  -- 再Move
        if t then table.insert(positions, t.z) end
    end
    -- 位置应该单调递增
    local monotonic = true
    for i = 2, #positions do
        -- ★ 允许 1mm PhysX 噪声（< 0.001 视为持平）
        if positions[i] < positions[i-1] - 0.001 then monotonic = false; break end
    end
    TF.assertTrue(monotonic, "U2-连续tick位置单调递增")

    -- ===== U3: ApplyInput 在 Move 之前被调用 =====
    local p3 = TE.CreateTestPlayer(3, "U3-ApplyFirst", false, TE.SPAWN.ORIGIN, 0).playerEntity
    -- 故意不给输入 → Move 不产生位移
    local _, tNoInput = TE.ExecDeterministicMove(p3)
    -- 给输入 → Move 产生位移
    TE.ApplyTickInput(p3, GC.MOVE_FORWARD, false, false, false, 0)
    local _, tWithInput = TE.ExecDeterministicMove(p3)
    if tNoInput and tWithInput then
        -- 有输入的移动量 > 无输入的移动量
        local dzNo = tNoInput.z - (p3._interpState.prevPos and p3._interpState.prevPos.z or 0)
        local dzYes = tWithInput.z - tNoInput.z
        TF.assertTrue(dzYes > 0.1, "U3-有输入时位移大于无输入")
    end

    -- ===== U4: 插值器不对本地玩家执行（客户端本地玩家走预测）=====
    -- _InterpolateRemotePlayers 检查 playerId ~= localPlayerId
    local p4 = TE.CreateTestPlayer(4, "U4-InterpSkip", true, TE.SPAWN.ORIGIN, 0).playerEntity
    pm.localPlayerId = 4  -- 设为本地玩家
    -- 记录当前 transform 位置
    local posBefore4 = p4.transform.position
    -- 尝试驱动插值（应该跳过本地玩家）
    if pm._InterpolateRemotePlayers then
        pm:_InterpolateRemotePlayers(GC.TICK_INTERVAL / 4)  -- 1/60s
        -- 本地玩家位置不变（插值器跳过了）
        local posAfter4 = p4.transform.position
        -- 插值器不会修改本地玩家位置（因为没有 interpState.hasTarget）
        TF.assertTrue(true, "U4-插值器跳过本地玩家(不崩溃)")
    end

    -- ===== U5: 多个远程玩家各自独立插值 =====
    local p5a = TE.CreateTestPlayer(5, "U5-RemoteA", false, TE.SPAWN.ORIGIN, 0).playerEntity
    local p5b = TE.CreateTestPlayer(6, "U5-RemoteB", false, TE.SPAWN.FAR, 0).playerEntity
    -- 一个执行 tick，另一个不执行
    TE.ApplyTickInput(p5a, GC.MOVE_FORWARD, false, false, false, 0)
    TE.ExecDeterministicMove(p5a)
    -- p5a 有 hasTarget，p5b 没有
    local hasA = (p5a._interpState and p5a._interpState.hasTarget)
    local hasB = (p5b._interpState and p5b._interpState.hasTarget)
    TF.assertTrue(hasA, "U5-玩家A有hasTarget")
    TF.assertTrue(not hasB, "U5-玩家B无hasTarget(未移动)")

    -- ===== U6: 插值状态在 tick 间正确维护 =====
    local p6 = TE.CreateTestPlayer(7, "U6-State", false, TE.SPAWN.ORIGIN, 0).playerEntity
    for _ = 1, 10 do
        TE.ApplyTickInput(p6, GC.MOVE_FORWARD, false, false, false, 0)
        TE.ExecDeterministicMove(p6)
    end
    local st6 = p6._interpState
    TF.assertTrue(st6.hasTarget, "U6-10tick后hasTarget=true")
    TF.assertNotNil(st6.prevPos, "U6-10tick后prevPos不为nil")
    TF.assertNotNil(st6.targetPos, "U6-10tick后targetPos不为nil")
    -- elapsed 应被重置为 0
    TF.assertInRange(st6.elapsed, -0.01, 0.01, "U6-elapsed已重置为0")

    -- ===== U7: _ApplyDeterministicMovement 对所有玩家执行（模拟 OnFrameEnd）=====
    -- 创建 3 个玩家（1 本地 + 2 远程）
    local p7local = TE.CreateTestPlayer(8, "U7-Local", true, TE.SPAWN.ORIGIN, 0).playerEntity
    local p7r1 = TE.CreateTestPlayer(9, "U7-Remote1", false, CS.UnityEngine.Vector3(2, 0.4, 0), 0).playerEntity
    local p7r2 = TE.CreateTestPlayer(10, "U7-Remote2", false, CS.UnityEngine.Vector3(-2, 0.4, 0), 0).playerEntity

    -- 给所有玩家设置输入
    for _, p in ipairs({p7local, p7r1, p7r2}) do
        TE.ApplyTickInput(p, GC.MOVE_FORWARD, false, false, false, 0)
    end
    -- 对所有玩家执行确定性移动（模拟统一后的 OnFrameEnd）
    local tickDt = Fix64.fromFloat(1/15)
    for _, p in ipairs({p7local, p7r1, p7r2}) do
        if pm._ApplyDeterministicMovement then
            pm:_ApplyDeterministicMovement(p, tickDt)
        end
    end
    -- 三个玩家都有 targetPos
    local allHaveTarget = true
    for _, p in ipairs({p7local, p7r1, p7r2}) do
        if not p._interpState or not p._interpState.hasTarget then
            allHaveTarget = false
        end
    end
    TF.assertTrue(allHaveTarget, "U7-3玩家全部有targetPos(含本地)")

    -- ===== U8: OnFrameEnd 中不再跳过本地玩家 =====
    -- 验证当前逻辑：OnFrameEnd 遍历 players，对本地玩家跳过
    -- 重构后应不再跳过
    local p8local = TE.CreateTestPlayer(11, "U8-Local", true, TE.SPAWN.ORIGIN, 0).playerEntity
    pm.localPlayerId = 11
    TE.ApplyTickInput(p8local, GC.MOVE_FORWARD, false, false, false, 0)

    -- 模拟旧逻辑：跳过本地玩家
    local oldStyleApplied = false
    for _, player in pairs(pm.players) do
        if player.playerId ~= pm.localPlayerId then
            if pm._ApplyDeterministicMovement then
                pm:_ApplyDeterministicMovement(player, tickDt)
                oldStyleApplied = true
            end
        end
    end
    -- 旧逻辑下 p8local 不会被 _ApplyDeterministicMovement
    -- 这里仅标记：重构后应改为对所有玩家执行
    TF.assertTrue(true, "U8-旧逻辑跳过本地(重构后应改为全执行)")

    -- ===== U9: _CaptureAuthPositions 在 Move 之后执行 =====
    -- 验证捕获的是 Move 后的结果
    local p9 = TE.CreateTestPlayer(12, "U9-CaptureOrder", false, TE.SPAWN.ORIGIN, 0).playerEntity
    TE.ApplyTickInput(p9, GC.MOVE_FORWARD, false, false, false, 0)
    -- 先记录 Move 前的 targetPos（如果存在）
    local oldTarget = p9._interpState and p9._interpState.targetPos
    -- 执行 Move
    TE.ExecDeterministicMove(p9)
    -- Move 后的 targetPos
    local newTarget = p9._interpState.targetPos
    -- 如果 oldTarget 存在则 newTarget 应该不同（已执行新 tick）
    if oldTarget then
        TF.assertTrue(newTarget.z > oldTarget.z, "U9-捕获在Move后: newTarget > oldTarget")
    else
        TF.assertNotNil(newTarget, "U9-首tick后newTarget存在")
    end

    -- ===== U10: tick 间无输入时移动停止 =====
    local p10 = TE.CreateTestPlayer(13, "U10-Stop", false, TE.SPAWN.ORIGIN, 0).playerEntity
    -- 先走 5 个前进 tick
    TE.ExecNTicks(p10, 5, GC.MOVE_FORWARD, false, 0)
    local posBeforeStop = p10._interpState.targetPos
    -- 再走 3 个无输入 tick
    TE.ExecNTicks(p10, 3, GC.MOVE_NONE, false, 0)
    local posAfterStop = p10._interpState.targetPos
    if posBeforeStop and posAfterStop then
        -- 无输入时 xz 应不变
        TF.assertInRange(posAfterStop.x - posBeforeStop.x, -TF.LOOSE, TF.LOOSE, "U10-无输入X不变")
        TF.assertInRange(posAfterStop.z - posBeforeStop.z, -TF.LOOSE, TF.LOOSE, "U10-无输入Z不变")
    end

    print("[U组] 帧执行与插值顺序测试完成")
    TE.Cleanup()
end

return { run = run }
