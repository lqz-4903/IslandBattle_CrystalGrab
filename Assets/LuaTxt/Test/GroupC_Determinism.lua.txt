-- =============================================
-- Test/GroupC_Determinism.lua — C组 确定性验证 (4条)
-- =============================================
-- ★ 帧同步基石——相同输入必须产生相同结果。

local TF = require("Test.TestFramework")
local TE = require("Test.TestEnv")
local GC = require("Core.GameConst")
local Fix64 = require("Fix64")

local function run()
    TF.group("C — 确定性验证")

    TE.Setup()

    -- ===== C1: 相同输入跑两遍逐 tick 位置完全一致 =====
    local p1a = TE.CreateTestPlayer(1, "C1a", false, TE.SPAWN.ORIGIN, 0).playerEntity
    local p1b = TE.CreateTestPlayer(2, "C1b", false, TE.SPAWN.ORIGIN, 0).playerEntity
    -- 相同输入序列
    local inputSeq = {
        { GC.MOVE_FORWARD, false },
        { GC.MOVE_FORWARD + GC.MOVE_LEFT, false },
        { GC.MOVE_NONE, false },
        { GC.MOVE_RIGHT, false },
        { GC.MOVE_FORWARD, true },  -- jump
        { GC.MOVE_FORWARD, false },
        { GC.MOVE_BACKWARD, false },
        { GC.MOVE_FORWARD + GC.MOVE_ROLL, false },
        { GC.MOVE_NONE, false },
        { GC.MOVE_FORWARD, false },
    }
    local resultsA, resultsB = {}, {}
    for _, inp in ipairs(inputSeq) do
        TE.ApplyTickInput(p1a, inp[1], inp[2], false, false, 0)
        TE.ApplyTickInput(p1b, inp[1], inp[2], false, false, 0)
        local _, ta = TE.ExecDeterministicMove(p1a)
        local _, tb = TE.ExecDeterministicMove(p1b)
        table.insert(resultsA, ta)
        table.insert(resultsB, tb)
    end
    local allEqual = true
    for i = 1, #resultsA do
        if resultsA[i] and resultsB[i] then
            if resultsA[i].x ~= resultsB[i].x or
               resultsA[i].y ~= resultsB[i].y or
               resultsA[i].z ~= resultsB[i].z then
                allEqual = false
                print(string.format("  C1 差异 tick=%d: A(%.6f,%.6f,%.6f) B(%.6f,%.6f,%.6f)",
                    i, resultsA[i].x, resultsA[i].y, resultsA[i].z,
                    resultsB[i].x, resultsB[i].y, resultsB[i].z))
                break
            end
        end
    end
    TF.assertTrue(allEqual, "C1-相同输入逐tick完全一致")

    -- ===== C2: 插入空帧不影响有输入的 tick 结果 =====
    local p2a = TE.CreateTestPlayer(3, "C2a", false, TE.SPAWN.ORIGIN, 0).playerEntity
    local p2b = TE.CreateTestPlayer(4, "C2b", false, TE.SPAWN.ORIGIN, 0).playerEntity
    -- 序列A: [W, W, 空, W, W]
    local seqA = { GC.MOVE_FORWARD, GC.MOVE_FORWARD, GC.MOVE_NONE, GC.MOVE_FORWARD, GC.MOVE_FORWARD }
    for _, d in ipairs(seqA) do
        TE.ApplyTickInput(p2a, d, false, false, false, 0)
        TE.ExecDeterministicMove(p2a)
    end
    -- 序列B: [W, W, W, W]
    local seqB = { GC.MOVE_FORWARD, GC.MOVE_FORWARD, GC.MOVE_FORWARD, GC.MOVE_FORWARD }
    for _, d in ipairs(seqB) do
        TE.ApplyTickInput(p2b, d, false, false, false, 0)
        TE.ExecDeterministicMove(p2b)
    end
    -- A的第1,2,4,5 tick 结果 应等于 B的第1,2,3,4 tick
    local stA = p2a._interpState
    local stB = p2b._interpState
    if stA and stB and stA.targetPos and stB.targetPos then
        TF.assertVec3Near(stA.targetPos, stB.targetPos, TF.LOOSE, "C2-空帧不影响最终位置")
    end

    -- ===== C3: Fix64 往返精度 =====
    local testVals = {0, 1, -1, 5.0, 0.33333, -99.5, 1000.0}
    local allC3Pass = true
    for _, val in ipairs(testVals) do
        local f64 = Fix64.fromFloat(val)
        local raw = f64.raw
        local restored = Fix64.new(raw)
        local floatBack = Fix64.toFloat(restored)
        local err = math.abs(floatBack - val)
        if err > 0.0001 then
            allC3Pass = false
            print(string.format("  C3 fail: val=%.6f restored=%.6f err=%.6f", val, floatBack, err))
        end
    end
    TF.assertTrue(allC3Pass, "C3-Fix64往返精度≤0.0001")

    -- ===== C4: Lua/C# Fix64 一致性 =====
    -- ★ 使用 FromDouble 避免 XLua 把 Lua double→C# float 的精度截断
    local allC4Pass = true
    for _, val in ipairs(testVals) do
        local luaRaw = Fix64.fromFloat(val).raw
        local csRaw = CS.Fix64.FromDouble(val).Raw
        if luaRaw ~= csRaw then
            allC4Pass = false
            print(string.format("  C4 fail: val=%.6f LuaRaw=%d C#Raw=%d", val, luaRaw, csRaw))
        end
    end
    TF.assertTrue(allC4Pass, "C4-Lua/C# Fix64 Raw(Double)一致")

    TE.Cleanup()
end

return { run = run }
