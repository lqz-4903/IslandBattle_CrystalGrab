-- =============================================
-- Test/GroupR_Fix64Serialize.lua — R组 Fix64网络序列化 (5条)
-- =============================================

local TF = require("Test.TestFramework")
local Fix64 = require("Fix64")

local function run()
    TF.group("R — Fix64 网络序列化")

    -- ===== R1: sfixed64 → Fix64.new(raw) 往返 =====
    local testVals = {0, 3.14159, -100.5, 0.33333, 2147483, 0.001, -0.001, 99999}
    local allR1 = true
    for _, val in ipairs(testVals) do
        -- 模拟网络流程: Float → Fix64 → Raw(long) → 网络 → Raw(long) → Fix64
        local f64 = Fix64.fromFloat(val)
        local raw = f64.raw  -- 这就是 sfixed64 传输的 long 值
        local restored = Fix64.new(raw)
        if restored.raw ~= raw then
            allR1 = false
            print(string.format("  R1 fail: val=%.6f orig raw=%d restored raw=%d", val, raw, restored.raw))
        end
    end
    TF.assertTrue(allR1, "R1-Fix64→Raw→Fix64往返(8值)")

    -- ===== R2: CameraYaw 序列化一致性 (Lua/C#) =====
    local yawFloat = 1.57  -- ~90° in radians
    local luaF64 = Fix64.fromFloat(yawFloat)
    local csRaw = CS.Fix64.FromDouble(yawFloat).Raw
    TF.assertTrue(luaF64.raw == csRaw, "R2-Lua/C# CameraYaw raw一致")

    -- ===== R3: ResultPos 通过网络不丢失精度 =====
    local testPositions = {
        {12.345, 2.0, -8.901},
        {0, 0, 0},
        {100.5, 50.25, -200.75},
        {0.001, 0.002, 0.003},
    }
    local allR3 = true
    for _, pos in ipairs(testPositions) do
        local xRaw = CS.Fix64.FromDouble(pos[1]).Raw
        local yRaw = CS.Fix64.FromDouble(pos[2]).Raw
        local zRaw = CS.Fix64.FromDouble(pos[3]).Raw
        -- 模拟客户端收到 raw 后重建
        local rx = Fix64.toFloat(Fix64.new(xRaw))
        local ry = Fix64.toFloat(Fix64.new(yRaw))
        local rz = Fix64.toFloat(Fix64.new(zRaw))
        if math.abs(rx - pos[1]) > 0.0001 or
           math.abs(ry - pos[2]) > 0.0001 or
           math.abs(rz - pos[3]) > 0.0001 then
            allR3 = false
            print(string.format("  R3 fail: (%.4f,%.4f,%.4f)→(%.6f,%.6f,%.6f)",
                pos[1], pos[2], pos[3], rx, ry, rz))
        end
    end
    TF.assertTrue(allR3, "R3-ResultPos往返精度(4位置)")

    -- ===== R4: chargeTime 蓄力时间 Fix64 精度 =====
    local chargeVals = {0, 0.5, 1.0, 2.5, 5.0, 0.0333, 4.999}
    local allR4 = true
    for _, val in ipairs(chargeVals) do
        local raw = CS.Fix64.FromDouble(val).Raw
        local restored = Fix64.toFloat(Fix64.new(raw))
        -- chargeTime 精度要求: 误差<1ms
        if math.abs(restored - val) > 0.001 then
            allR4 = false
            print(string.format("  R4 fail: chargeTime %.4f→%.6f 误差>1ms", val, restored))
        end
    end
    TF.assertTrue(allR4, "R4-chargeTime精度<1ms(7值)")

    -- ===== R5: 大量 Fix64 值打包不出错 =====
    -- 模拟 100 个随机 Fix64 往返
    local allR5 = true
    for i = 1, 100 do
        local randFloat = (i * 1.357) % 1000 - 500  -- 伪随机 [-500, 500]
        local raw = CS.Fix64.FromDouble(randFloat).Raw
        local restored = Fix64.toFloat(Fix64.new(raw))
        if math.abs(restored - randFloat) > 0.0001 then
            allR5 = false
            break
        end
    end
    TF.assertTrue(allR5, "R5-100个随机值序列化不丢失")

    print("[R组] Fix64序列化测试完成")
end

return { run = run }
