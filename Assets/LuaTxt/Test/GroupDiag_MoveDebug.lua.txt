-- =============================================
-- Test/GroupDiag_MoveDebug.lua — controller:Move 位移诊断
-- =============================================
-- 只做最基本的 controller:Move 测试，排除一切干扰

local TF = require("Test.TestFramework")
local TE = require("Test.TestEnv")
local GC = require("Core.GameConst")
local Fix64 = require("Fix64")
local Vec3  = require("Fix64Vector3")

local function run()
    TF.group("诊断 — controller:Move 原始位移")

    TE.Setup()

    -- ===== D1: 单次大位移 Move(0, 0, 0.3333) =====
    local p1 = TE.CreateTestPlayer(1, "D1", false, TE.SPAWN.ORIGIN, 0).playerEntity
    local start1 = p1.transform.position
    local ok1 = pcall(function() p1.controller:Move(CS.UnityEngine.Vector3(0, 0, GC.MOVE_SPEED/GC.TICK_RATE)) end)
    local end1 = p1.transform.position
    local dz1 = end1.z - start1.z
    print(string.format("[D1] 单次Move(0,0,%.4f) → dz=%.6f pcall=%s",
        GC.MOVE_SPEED/GC.TICK_RATE, dz1, tostring(ok1)))
    TF.assertInRange(dz1, 0.30, 0.37, string.format("D1-大步Move=%.4f", dz1))

    -- ===== D2: 8子步 Move =====
    local p2 = TE.CreateTestPlayer(2, "D2", false, TE.SPAWN.ORIGIN, 0).playerEntity
    local start2 = p2.transform.position
    local subDisp = CS.UnityEngine.Vector3(0, 0, GC.MOVE_SPEED/(GC.TICK_RATE*8))
    local okCount = 0
    for i = 1, 8 do
        local ok = pcall(function() p2.controller:Move(subDisp) end)
        if ok then okCount = okCount + 1 else print("  D2 子步"..i.." FAIL!") end
    end
    local end2 = p2.transform.position
    local dz2 = end2.z - start2.z
    print(string.format("[D2] 8×Move(0,0,%.4f) → dz=%.6f ok=%d/8",
        GC.MOVE_SPEED/(GC.TICK_RATE*8), dz2, okCount))
    TF.assertInRange(dz2, 0.30, 0.37, string.format("D2-8子步Move=%.4f", dz2))

    -- ===== D3: 连续4帧 60fps Move（不操作interpState）=====
    local p3 = TE.CreateTestPlayer(3, "D3", false, TE.SPAWN.ORIGIN, 0).playerEntity
    local ctrl3 = p3.controller
    local start3 = p3.transform.position
    local dt = 1/60
    for i = 1, 4 do
        local disp = CS.UnityEngine.Vector3(0, 0, GC.MOVE_SPEED * dt)
        local ok = pcall(function() ctrl3:Move(disp) end)
        local curZ = p3.transform.position.z
        print(string.format("  D3 帧%d: Move(0,0,%.4f) → z=%.6f pcall=%s",
            i, GC.MOVE_SPEED*dt, curZ, tostring(ok)))
        if not ok then print("  D3 帧"..i.." pcall失败!") end
    end
    local end3 = p3.transform.position
    local dz3 = end3.z - start3.z
    print(string.format("[D3] 4帧60fps → dz=%.6f (期望0.333m)", dz3))

    -- ===== D4: 连续60帧 60fps Move =====
    local p4 = TE.CreateTestPlayer(4, "D4", false, TE.SPAWN.ORIGIN, 0).playerEntity
    local ctrl4 = p4.controller
    local start4 = p4.transform.position
    local dt4 = 1/60
    local failFrame = nil
    for i = 1, 60 do
        local disp = CS.UnityEngine.Vector3(0, 0, GC.MOVE_SPEED * dt4)
        local ok = pcall(function() ctrl4:Move(disp) end)
        if not ok and failFrame == nil then
            failFrame = i
            print(string.format("  D4 帧%d pcall失败!", i))
        end
        if i % 15 == 0 then
            print(string.format("  D4 帧%d: z=%.6f", i, p4.transform.position.z))
        end
    end
    local end4 = p4.transform.position
    local dz4 = end4.z - start4.z
    print(string.format("[D4] 60帧60fps → dz=%.6f pcall失败帧=%s", dz4, tostring(failFrame)))

    -- ===== D5: 连续10次 ExecDeterministicMove（生产代码路径）=====
    local p5 = TE.CreateTestPlayer(5, "D5", false, TE.SPAWN.ORIGIN, 0).playerEntity
    local prevPositions = {}
    for tick = 1, 10 do
        TE.ApplyTickInput(p5, GC.MOVE_FORWARD, false, false, false, 0)
        local prev, target = TE.ExecDeterministicMove(p5)
        if prev and target then
            table.insert(prevPositions, {tick=tick, prevZ=prev.z, targetZ=target.z, step=target.z-prev.z})
        end
        if tick <= 3 or tick == 10 then
            if prev and target then
                print(string.format("  D5 tick%d: prevZ=%.4f targetZ=%.4f step=%.4f",
                    tick, prev.z, target.z, target.z - prev.z))
            end
        end
    end
    if #prevPositions >= 10 then
        local totalDz = prevPositions[10].targetZ - prevPositions[1].prevZ
        print(string.format("[D5] 10tick → totalDz=%.6f (期望=%.4f) targetZ链: %.4f→%.4f",
            totalDz, 10*GC.MOVE_SPEED/GC.TICK_RATE,
            prevPositions[1].targetZ, prevPositions[10].targetZ))
    end

    -- ===== D6: 带地压的60帧 60fps（完全复刻 _ApplyLocalMovement）=====
    local p6 = TE.CreateTestPlayer(6, "D6", false, TE.SPAWN.ORIGIN, 0).playerEntity
    local ctrl6 = p6.controller
    local start6 = p6.transform.position
    p6.velocity = Vec3.new(Fix64.ZERO, Fix64.ZERO, Fix64.ZERO)
    local dt6 = 1/60
    for i = 1, 60 do
        local hv = CS.UnityEngine.Vector3(0, 0, GC.MOVE_SPEED)
        local vv
        if p6.isGrounded then
            vv = 0  -- 着地时为 0，与远程确定性路径一致
        else
            vv = Fix64.toFloat(p6.velocity.y) - GC.GRAVITY * dt6
        end
        local disp = CS.UnityEngine.Vector3(hv.x * dt6, vv * dt6, hv.z * dt6)
        local ok = pcall(function() ctrl6:Move(disp) end)
        if not ok then print("  D6 帧"..i.." pcall失败!") end
        local ok2, g = pcall(function() return ctrl6.isGrounded end)
        if ok2 then p6.isGrounded = g end
        p6.velocity = Vec3.new(Fix64.fromFloat(hv.x), Fix64.fromFloat(vv), Fix64.fromFloat(hv.z))
        if i % 15 == 0 then
            print(string.format("  D6 帧%d: z=%.6f grounded=%s", i, p6.transform.position.z, tostring(p6.isGrounded)))
        end
    end
    local end6 = p6.transform.position
    local dz6 = end6.z - start6.z
    print(string.format("[D6] 60帧地压 → dz=%.6f 最终z=%.6f", dz6, end6.z))

    print("[GroupDiag] 完成")
    TE.Cleanup()
end

return { run = run }
