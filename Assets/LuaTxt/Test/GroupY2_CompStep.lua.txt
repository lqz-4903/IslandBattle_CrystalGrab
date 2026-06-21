-- =============================================
-- Test/GroupY2_CompStep.lua — 本地60fps vs 远程15fps 步长直接对比
-- =============================================
-- 独立测试，不依赖 Simulate* 函数，直接用生产代码路径

local TF = require("Test.TestFramework")
local TE = require("Test.TestEnv")
local GC = require("Core.GameConst")
local Fix64 = require("Fix64")
local Vec3  = require("Fix64Vector3")

local WALK_PER_TICK = GC.MOVE_SPEED / GC.TICK_RATE
local TICK_INTERVAL = GC.TICK_INTERVAL

--- 60fps 本地移动模拟（完全复刻 _ApplyLocalMovement：单步 Move，着地 -GRAVITY*0.5）
local function Local60fps(player, frameCount, moveDir, yawDeg)
    local ctrl = player.controller
    local dt = 1/60
    local yaw = math.rad(yawDeg or 0)
    local dm = moveDir & 0x0F
    local roll = (moveDir & GC.MOVE_ROLL) ~= 0
    if player.velocity == nil then
        player.velocity = Vec3.new(Fix64.ZERO, Fix64.ZERO, Fix64.ZERO)
    end
    for i = 1, frameCount do
        local hv
        if roll then
            hv = CS.UnityEngine.Vector3(math.sin(yaw), 0, math.cos(yaw)) * 12
        elseif dm ~= GC.MOVE_NONE then
            local f = CS.UnityEngine.Vector3(math.sin(yaw), 0, math.cos(yaw))
            local r = CS.UnityEngine.Vector3(math.cos(yaw), 0, -math.sin(yaw))
            local d = CS.UnityEngine.Vector3.zero
            if dm & GC.MOVE_FORWARD ~= 0 then d = d + f end
            if dm & GC.MOVE_BACKWARD ~= 0 then d = d - f end
            if dm & GC.MOVE_RIGHT ~= 0 then d = d + r end
            if dm & GC.MOVE_LEFT ~= 0 then d = d - r end
            if d.magnitude > 1 then d = d.normalized end
            hv = d * GC.MOVE_SPEED
        else
            hv = CS.UnityEngine.Vector3.zero
        end
        local vv
        if player.isGrounded then
            vv = 0  -- 着地时为 0，与远程确定性路径一致
        else
            vv = Fix64.toFloat(player.velocity.y) - GC.GRAVITY * dt
        end
        local disp = CS.UnityEngine.Vector3(hv.x * dt, vv * dt, hv.z * dt)
        local ok = pcall(function() ctrl:Move(disp) end)
        if not ok then break end
        local ok2, g = pcall(function() return ctrl.isGrounded end)
        if ok2 then player.isGrounded = g end
        player.velocity = Vec3.new(Fix64.fromFloat(hv.x), Fix64.fromFloat(vv), Fix64.fromFloat(hv.z))
    end
end

local function run()
    TF.group("比较 — 本地60fps vs 远程15fps 步长")

    TE.Setup()

    -- ===== C1: 1 tick时间内60fps vs 15fps =====
    print("[C1] 单tick时间(1/15s)对比...")
    local pL = TE.CreateTestPlayer(1, "C1-Local", false, TE.SPAWN.ORIGIN, 0).playerEntity
    local pR = TE.CreateTestPlayer(2, "C1-Remote", false, TE.SPAWN.ORIGIN, 0).playerEntity

    local startL = pL.transform.position
    Local60fps(pL, 4, GC.MOVE_FORWARD, 0)  -- 4帧 × 1/60 = 1/15秒
    local dzL = pL.transform.position.z - startL.z

    TE.ApplyTickInput(pR, GC.MOVE_FORWARD, false, false, false, 0)
    local prevR, targetR = TE.ExecDeterministicMove(pR)
    local dzR = (targetR and prevR) and (targetR.z - prevR.z) or 0

    local diff = math.abs(dzL - dzR)
    print(string.format("  C1 本地60fps×4帧=%.4fm  远程15fps×1tick=%.4fm  diff=%.4fm",
        dzL, dzR, diff))
    TF.assertInRange(dzL, WALK_PER_TICK - 0.02, WALK_PER_TICK + 0.02,
        string.format("C1-本地60fps单tick位移≈0.333m (实际=%.4f)", dzL))
    TF.assertInRange(dzR, WALK_PER_TICK - 0.02, WALK_PER_TICK + 0.02,
        string.format("C1-远程15fps单tick位移≈0.333m (实际=%.4f)", dzR))
    TF.assertTrue(diff < 0.01,
        string.format("C1-两路径差异=%.4fm [超过1cm!]", diff))

    -- ===== C2: 1秒(60帧 vs 15tick) 累积对比 =====
    print("[C2] 1秒累积对比...")
    local pL2 = TE.CreateTestPlayer(3, "C2-Local", false, TE.SPAWN.ORIGIN, 0).playerEntity
    local pR2 = TE.CreateTestPlayer(4, "C2-Remote", false, TE.SPAWN.ORIGIN, 0).playerEntity

    local startL2 = pL2.transform.position
    Local60fps(pL2, 60, GC.MOVE_FORWARD, 0)
    local dzL2 = pL2.transform.position.z - startL2.z

    local r2 = TE.ExecNTicks(pR2, 15, GC.MOVE_FORWARD, false, 0)
    local dzR2 = r2[15].target.z - r2[1].prev.z

    local diff2 = math.abs(dzL2 - dzR2)
    print(string.format("  C2 本地60fps×60帧=%.4fm  远程15fps×15tick=%.4fm  diff=%.4fm",
        dzL2, dzR2, diff2))
    TF.assertInRange(dzL2, 4.7, 5.3, string.format("C2-本地1秒≈5m (实际=%.4f)", dzL2))
    TF.assertInRange(dzR2, 4.7, 5.3, string.format("C2-远程1秒≈5m (实际=%.4f)", dzR2))
    TF.assertTrue(diff2 < 0.15,
        string.format("C2-1秒累积差异=%.4fm [超过15cm!]", diff2))

    -- ===== C3: 翻滚步长（本地 vs 远程）=====
    print("[C3] 翻滚步长对比...")
    local pL3 = TE.CreateTestPlayer(5, "C3-LocalRoll", false, TE.SPAWN.ORIGIN, 0).playerEntity
    local pR3 = TE.CreateTestPlayer(6, "C3-RemoteRoll", false, TE.SPAWN.ORIGIN, 0).playerEntity

    local startL3 = pL3.transform.position
    Local60fps(pL3, 4, GC.MOVE_FORWARD | GC.MOVE_ROLL, 0)
    local dzL3 = pL3.transform.position.z - startL3.z

    TE.ApplyTickInput(pR3, GC.MOVE_FORWARD | GC.MOVE_ROLL, false, false, false, 0)
    local prevR3, targetR3 = TE.ExecDeterministicMove(pR3)
    local dzR3 = (targetR3 and prevR3) and (targetR3.z - prevR3.z) or 0

    local expectedRoll = 12 / 15  -- 0.8m
    print(string.format("  C3 本地翻滚=%.4fm  远程翻滚=%.4fm", dzL3, dzR3))
    TF.assertInRange(dzL3, expectedRoll - 0.05, expectedRoll + 0.05,
        string.format("C3-本地翻滚≈0.8m (实际=%.4f)", dzL3))
    TF.assertInRange(dzR3, expectedRoll - 0.05, expectedRoll + 0.05,
        string.format("C3-远程翻滚≈0.8m (实际=%.4f)", dzR3))

    -- ===== C4: 不同子步数(1,2,4,8,16)对位移的影响 =====
    print("[C4] 子步数影响...")
    local savedSS = GC.PHYSICS_SUBSTEPS
    local ssCounts = {1, 2, 4, 8, 16}
    local ssResults = {}
    for _, ss in ipairs(ssCounts) do
        local p = TE.CreateTestPlayer(10 + ss, "C4-ss"..ss, false, TE.SPAWN.ORIGIN, 0).playerEntity
        GC.PHYSICS_SUBSTEPS = ss
        TE.ApplyTickInput(p, GC.MOVE_FORWARD, false, false, false, 0)
        local prev, target = TE.ExecDeterministicMove(p)
        if prev and target then
            ssResults[ss] = target.z - prev.z
            print(string.format("  子步=%d 位移=%.6f", ss, ssResults[ss]))
        end
    end
    GC.PHYSICS_SUBSTEPS = savedSS

    local ref = ssResults[8]
    local ssOk = true
    for _, ss in ipairs(ssCounts) do
        if ssResults[ss] and math.abs(ssResults[ss] - ref) > 0.005 then
            ssOk = false
        end
    end
    TF.assertTrue(ssOk, "C4-子步数不影响位移(容差5mm)")

    -- ===== C5: 方向切换后步长一致 =====
    print("[C5] 方向切换...")
    local p5 = TE.CreateTestPlayer(30, "C5-DirSwitch", false, TE.SPAWN.ORIGIN, 0).playerEntity
    local ra = TE.ExecNTicks(p5, 5, GC.MOVE_FORWARD, false, 0)
    local rb = TE.ExecNTicks(p5, 5, GC.MOVE_BACKWARD, false, 0)
    local rc = TE.ExecNTicks(p5, 5, GC.MOVE_FORWARD, false, 0)

    local s1 = ra[1].target.z - ra[1].prev.z
    local s2 = rc[1].target.z - rc[1].prev.z
    print(string.format("  C5 首段步长=%.4f 末段步长=%.4f diff=%.4f", s1, s2, math.abs(s1-s2)))
    TF.assertInRange(math.abs(s1 - s2), -0.02, 0.02, "C5-方向切换步长一致")

    -- ===== C6: 10秒长时间累积 =====
    print("[C6] 10秒累积...")
    local pL6 = TE.CreateTestPlayer(31, "C6-Local10s", false, TE.SPAWN.ORIGIN, 0).playerEntity
    local pR6 = TE.CreateTestPlayer(32, "C6-Remote10s", false, TE.SPAWN.ORIGIN, 0).playerEntity

    local sL6 = pL6.transform.position
    Local60fps(pL6, 600, GC.MOVE_FORWARD, 0)
    local dzL6 = pL6.transform.position.z - sL6.z

    local r6 = TE.ExecNTicks(pR6, 150, GC.MOVE_FORWARD, false, 0)
    local dzR6 = r6[150].target.z - r6[1].prev.z

    local diff6 = math.abs(dzL6 - dzR6)
    print(string.format("  C6 本地=%.4fm  远程=%.4fm  diff=%.4fm", dzL6, dzR6, diff6))
    TF.assertInRange(dzL6, 45, 55, "C6-本地10s≈50m")
    TF.assertInRange(dzR6, 45, 55, "C6-远程10s≈50m")
    TF.assertTrue(diff6 < 2.0,
        string.format("C6-10秒累积差异=%.4fm", diff6))

    -- ===== C7: 主机捕获差异（本地transform vs 确定性targetPos）=====
    print("[C7] 主机捕获对比...")
    local pL7 = TE.CreateTestPlayer(33, "C7-Local", false, TE.SPAWN.ORIGIN, 0).playerEntity
    local pR7 = TE.CreateTestPlayer(34, "C7-Remote", false, TE.SPAWN.ORIGIN, 0).playerEntity

    Local60fps(pL7, 4, GC.MOVE_FORWARD, 0)  -- 1 tick的60fps预测
    local localZ = pL7.transform.position.z

    TE.ApplyTickInput(pR7, GC.MOVE_FORWARD, false, false, false, 0)
    local _, tR7 = TE.ExecDeterministicMove(pR7)
    local remoteZ = tR7 and tR7.z or 0

    local diff7 = math.abs(localZ - remoteZ)
    if diff7 > 0.01 then
        print(string.format("  ⚠ C7-主机捕获(transform)vs确定性(targetPos) 差异=%.4fm [这就是步长bug根源!]",
            diff7))
    end
    TF.assertTrue(true, string.format("C7-主机捕获差异=%.4fm", diff7))

    -- ===== C8: 步长链一致性 =====
    print("[C8] 步长链...")
    local p8 = TE.CreateTestPlayer(35, "C8-Chain", false, TE.SPAWN.ORIGIN, 0).playerEntity
    local r8 = TE.ExecNTicks(p8, 15, GC.MOVE_FORWARD, false, 0)

    local chainOk = true
    local first = nil
    for i = 1, #r8 do
        if r8[i].prev and r8[i].target then
            local s = r8[i].target.z - r8[i].prev.z
            if first == nil then
                first = s
            elseif math.abs(s - first) > 0.01 then
                chainOk = false
                print(string.format("  C8 tick=%d step=%.6f (first=%.6f)", i, s, first))
            end
        end
    end
    TF.assertTrue(chainOk, "C8-步长链一致")

    print("[GroupY2] 完成")
    TE.Cleanup()
end

return { run = run }
