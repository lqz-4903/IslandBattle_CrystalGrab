-- =============================================
-- Test/GroupO_DetermRandom.lua — O组 确定性随机数 (4条)
-- =============================================

local TF = require("Test.TestFramework")
local Fix64 = require("Fix64")
-- DeterministicRandom 由 InitClass.lua require 为全局变量

local function run()
    TF.group("O — 确定性随机数")

    local DR = DeterministicRandom

    -- ===== O1: 同种子→同序列 =====
    local rng1 = DR:new(42)
    local rng2 = DR:new(42)
    local allEqual = true
    local seq1, seq2 = {}, {}
    for i = 1, 100 do
        local v1 = rng1:Next()
        local v2 = rng2:Next()
        table.insert(seq1, v1)
        table.insert(seq2, v2)
        if v1 ~= v2 then
            allEqual = false
            print(string.format("  O1 差异 i=%d: %d vs %d", i, v1, v2))
            break
        end
    end
    TF.assertTrue(allEqual, "O1-同种子前100次逐位相等")

    -- ===== O2: 不同种子→不同序列 =====
    local rng3 = DR:new(99)
    local diffFound = false
    for i = 1, 100 do
        if rng3:Next() ~= seq1[i] then
            diffFound = true
            break
        end
    end
    TF.assertTrue(diffFound, "O2-不同种子序列不同")

    -- ===== O3: 范围随机（整数）=====
    local rng4 = DR:new(12345)
    local minInt, maxInt = 0, 100
    local hits = {}; for i = minInt, maxInt do hits[i] = 0 end
    local allInRange = true
    for _ = 1, 1000 do
        local v = rng4:NextInt(minInt, maxInt)
        if v < minInt or v > maxInt then
            allInRange = false
            print(string.format("  O3 越界: v=%d", v))
        end
        hits[v] = (hits[v] or 0) + 1
    end
    TF.assertTrue(allInRange, "O3-1000次都在[0,100]内")
    -- 检查边界是否被覆盖
    local hitMin = hits[minInt] > 0
    local hitMax = hits[maxInt] > 0
    TF.assertTrue(hitMin, "O3-至少一次命中min=0")
    TF.assertTrue(hitMax, "O3-至少一次命中max=100")

    -- ===== O4: 范围随机（Fix64）=====
    local rng5 = DR:new(67890)
    local fMin, fMax = Fix64.fromFloat(-5), Fix64.fromFloat(5)
    local allFixInRange = true
    for _ = 1, 500 do
        local v = rng5:NextFix64(fMin, fMax)
        if v < fMin or v > fMax then
            allFixInRange = false
            break
        end
    end
    TF.assertTrue(allFixInRange, "O4-500次Fix64随机都在[-5,5]内")

    print("[O组] 随机数测试完成")
end

return { run = run }
