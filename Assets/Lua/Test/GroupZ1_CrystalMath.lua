-- =============================================
-- Test/GroupZ1_CrystalMath.lua — 水晶核心数学 (34条)
-- =============================================
-- 测试水晶系统的纯数学逻辑，无需场景环境。
-- 覆盖：分数计算、掉落计算(30%向上取整)、圆心随机生成。
-- =============================================

local TF = require("Test.TestFramework")
local Fix64 = require("Fix64")

-- ========== 水晶分数助手（与实现逻辑一致）==========

local CRYSTAL_SCORE = 6

local function HoldingToScore(holding)
    holding = holding or 0
    if holding < 0 then holding = 0 end
    return holding * CRYSTAL_SCORE
end

local function CalcDropCount(holding)
    holding = holding or 0
    if holding <= 0 then return 0 end
    return math.ceil(holding * 0.3)
end

local function CalcRemainingAfterDrop(holding)
    local drop = CalcDropCount(holding)
    return math.max(0, holding - drop)
end

local function CalcScoreAfterDrop(holding)
    return HoldingToScore(CalcRemainingAfterDrop(holding))
end

-- ========== 圆心随机（使用确定性随机）==========

--- 在圆心 (cx,cz) 半径 R 的圆内生成随机点
--- 面积均匀分布：angle=2π*rand1, dist=R*sqrt(rand2)
--- @param rng_table  — DeterministicRandom 实例（._cs 可调 C# NextFix64）
local function RandomPointInCircle(cx, cz, radius, rng)
    -- C# NextFix64 返回 Fix64 在 [0, 1)
    local randAngle = rng._cs:NextFix64():ToDouble()
    local randDist  = rng._cs:NextFix64():ToDouble()
    -- 面积均匀：用 sqrt
    local angle = 2 * math.pi * randAngle
    local dist  = radius * math.sqrt(randDist)
    local posX = cx + math.cos(angle) * dist
    local posZ = cz + math.sin(angle) * dist
    return posX, posZ
end

-- ========== 运行入口 ==========

local function run()
    TF.group("Z1 — 水晶核心数学")

    -- ============================================
    -- 第1节：分数计算 (HoldingToScore: N×6)
    -- ============================================

    TF.assertEqual(HoldingToScore(0), 0, 0, "Z1-1-持有0颗分数=0")
    TF.assertEqual(HoldingToScore(1), 6, 0, "Z1-2-持有1颗分数=6")
    TF.assertEqual(HoldingToScore(2), 12, 0, "Z1-2b-持有2颗分数=12")
    TF.assertEqual(HoldingToScore(5), 30, 0, "Z1-3-持有5颗分数=30")
    TF.assertEqual(HoldingToScore(10), 60, 0, "Z1-4-持有10颗分数=60")
    TF.assertEqual(HoldingToScore(-1), 0, 0, "Z1-5-负持有→0分")
    TF.assertEqual(HoldingToScore(nil), 0, 0, "Z1-6-nil→0分")
    TF.assertEqual(HoldingToScore(1000), 6000, 0, "Z1-7-1000颗=6000分")

    -- ============================================
    -- 第2节：掉落计算 ceil(N×0.3)
    -- ============================================

    -- 核心公式
    TF.assertEqual(CalcDropCount(0), 0, 0, "Z1-8-持有0→掉0")
    TF.assertEqual(CalcDropCount(1), 1, 0, "Z1-9-持有1→ceil(0.3)=1")
    TF.assertEqual(CalcDropCount(2), 1, 0, "Z1-10-持有2→ceil(0.6)=1")
    TF.assertEqual(CalcDropCount(3), 1, 0, "Z1-11-持有3→ceil(0.9)=1")
    TF.assertEqual(CalcDropCount(4), 2, 0, "Z1-12-持有4→ceil(1.2)=2")
    TF.assertEqual(CalcDropCount(7), 3, 0, "Z1-13-持有7→ceil(2.1)=3")
    TF.assertEqual(CalcDropCount(10), 3, 0, "Z1-14-持有10→ceil(3.0)=3")
    TF.assertEqual(CalcDropCount(11), 4, 0, "Z1-15-持有11→ceil(3.3)=4")
    TF.assertEqual(CalcDropCount(13), 4, 0, "Z1-16-持有13→ceil(3.9)=4")
    TF.assertEqual(CalcDropCount(14), 5, 0, "Z1-17-持有14→ceil(4.2)=5")
    TF.assertEqual(CalcDropCount(20), 6, 0, "Z1-18-持有20→ceil(6.0)=6")
    TF.assertEqual(CalcDropCount(100), 30, 0, "Z1-19-持有100→30")
    TF.assertEqual(CalcDropCount(-5), 0, 0, "Z1-20-负持有→0")

    -- 毛边界验证（30%恰好为整数的值）
    -- 10 → 3.0 → ceil = 3
    -- 20 → 6.0 → ceil = 6
    -- 这些值不会有浮动误差导致的错位
    TF.assertEqual(CalcDropCount(10), 3, 0, "Z1-20b-10边界=3.0→ceil=3")
    TF.assertEqual(CalcDropCount(20), 6, 0, "Z1-20c-20边界=6.0→ceil=6")

    -- ============================================
    -- 第3节：掉落后剩余
    -- ============================================

    TF.assertEqual(CalcRemainingAfterDrop(10), 7, 0, "Z1-21-10掉3剩7")
    TF.assertEqual(CalcRemainingAfterDrop(1), 0, 0, "Z1-22-1掉1剩0(清零)")
    TF.assertEqual(CalcRemainingAfterDrop(4), 2, 0, "Z1-23-4掉2剩2")
    TF.assertEqual(CalcRemainingAfterDrop(0), 0, 0, "Z1-24-0掉0剩0")

    -- 剩余不能为负
    for _, h in ipairs({0, 1, 2, 3, 5, 10, 50}) do
        local r = CalcRemainingAfterDrop(h)
        TF.assertTrue(r >= 0, "Z1-24b-h" .. h .. "剩" .. r .. "≥0")
    end

    -- ============================================
    -- 第4节：掉落后分数
    -- ============================================

    TF.assertEqual(CalcScoreAfterDrop(10), 42, 0, "Z1-25-10→7颗=42分")
    TF.assertEqual(CalcScoreAfterDrop(1), 0, 0, "Z1-26-1→0颗=0分")
    TF.assertEqual(CalcScoreAfterDrop(4), 12, 0, "Z1-27-4→2颗=12分")
    TF.assertEqual(CalcScoreAfterDrop(0), 0, 0, "Z1-28-0死后0分")

    -- ============================================
    -- 第5节：拾取增量
    -- ============================================

    local function simulatePickup(holding)
        holding = math.max(0, holding or 0)
        local newHolding = holding + 1
        return newHolding, HoldingToScore(newHolding)
    end

    local h, s = simulatePickup(0)
    TF.assertEqual(h, 1, 0, "Z1-29a-从0拾取持有=1")
    TF.assertEqual(s, 6, 0, "Z1-29b-从0拾取分数=6")

    h, s = simulatePickup(5)
    TF.assertEqual(h, 6, 0, "Z1-29c-从5拾取持有=6")
    TF.assertEqual(s, 36, 0, "Z1-29d-从5拾取分数=36")

    -- ============================================
    -- 第6节：死亡+拾取完整周期
    -- ============================================

    -- 模拟：拾取8颗 → 死亡掉落 → 复活后拾取3颗
    local function simulateLifecycle(initial, pickups1, pickups2)
        local holding = initial or 0
        for _ = 1, (pickups1 or 0) do holding = holding + 1 end
        local scoreBeforeDeath = HoldingToScore(holding)
        local dropped = CalcDropCount(holding)
        holding = holding - dropped
        local scoreAfterDeath = HoldingToScore(holding)
        for _ = 1, (pickups2 or 0) do holding = holding + 1 end
        local scoreFinal = HoldingToScore(holding)
        return scoreBeforeDeath, dropped, scoreAfterDeath, scoreFinal
    end

    local s1, drp, s2, s3 = simulateLifecycle(0, 8, 3)
    TF.assertEqual(drp, 3, 0, "Z1-30a-拾8颗死后掉ceil(2.4)=3")
    TF.assertEqual(s1, 48, 0, "Z1-30b-死前分数8×6=48")
    TF.assertEqual(s2, 30, 0, "Z1-30c-死后分数5×6=30(掉3颗)")
    TF.assertEqual(s3, 48, 0, "Z1-30d-复活捡3颗=8颗=48分")

    -- 模拟：不捡任何水晶就死了
    local s1b, drpb, s2b, s3b = simulateLifecycle(0, 0, 5)
    TF.assertEqual(drpb, 0, 0, "Z1-30e-0颗死掉0颗")
    TF.assertEqual(s2b, 0, 0, "Z1-30f-死后分数=0")
    TF.assertEqual(s3b, 30, 0, "Z1-30g-复活捡5颗=30分")

    -- ============================================
    -- 第7节：圆心随机生成
    -- ============================================

    local rng = DeterministicRandom.new(42)

    -- Z1-31: 1000点全在半径内
    local cx, cz, radius = 10, 20, 5
    local allInCircle = true
    local minDist, maxDist = math.huge, 0
    for _ = 1, 1000 do
        local px, pz = RandomPointInCircle(cx, cz, radius, rng)
        local d = math.sqrt((px - cx)^2 + (pz - cz)^2)
        if d > radius + 0.0001 then
            allInCircle = false
            break
        end
        if d < minDist then minDist = d end
        if d > maxDist then maxDist = d end
    end
    TF.assertTrue(allInCircle, "Z1-31a-1000点全在半径内")
    TF.assertTrue(minDist < radius * 0.15, "Z1-31b-有靠近圆心点")
    TF.assertTrue(maxDist > radius * 0.85, "Z1-31c-有靠近边界点")

    -- Z1-32: 同种子的序列完全相同（确定性）
    local rngA = DeterministicRandom.new(777)
    local rngB = DeterministicRandom.new(777)
    local allSame = true
    for _ = 1, 50 do
        local ax, az = RandomPointInCircle(0, 0, 10, rngA)
        local bx, bz = RandomPointInCircle(0, 0, 10, rngB)
        if math.abs(ax - bx) > 0.00001 or math.abs(az - bz) > 0.00001 then
            allSame = false
            break
        end
    end
    TF.assertTrue(allSame, "Z1-32-同种子50点全等(确定性)")

    -- Z1-33: 不同种子的序列不同
    local rngC = DeterministicRandom.new(777)
    local rngD = DeterministicRandom.new(888)
    local foundDiff = false
    for _ = 1, 10 do
        local cx2, cz2 = RandomPointInCircle(0, 0, 10, rngC)
        local dx, dz = RandomPointInCircle(0, 0, 10, rngD)
        if math.abs(cx2 - dx) > 0.00001 or math.abs(cz2 - dz) > 0.00001 then
            foundDiff = true
            break
        end
    end
    TF.assertTrue(foundDiff, "Z1-33-不同种子序列不同")

    -- Z1-34: 零半径 → 全部落在圆心
    local rngE = DeterministicRandom.new(99)
    local allAtCenter = true
    for _ = 1, 20 do
        local px, pz = RandomPointInCircle(5, 5, 0, rngE)
        if math.abs(px - 5) > 0.00001 or math.abs(pz - 5) > 0.00001 then
            allAtCenter = false
            break
        end
    end
    TF.assertTrue(allAtCenter, "Z1-34-零半径→全在圆心")

    print("[Z1] 水晶数学测试完成")
end

return { run = run }
