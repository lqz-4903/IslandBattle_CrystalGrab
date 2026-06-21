-- =============================================
-- Test/GroupZ2_PhaseLogic.lua — 阶段状态机逻辑 (40条)
-- =============================================
-- 测试水晶系统的阶段切换逻辑，纯数学无场景依赖。
--
-- 时间轴：0→7准备→27生成1→47攻击1→67生成2→87攻击2→107生成3→127攻击3→结束
-- =============================================

local TF = require("Test.TestFramework")

-- ========== 阶段定义 ==========

local PHASE = {
    PREP   = 0,   -- 准备阶段：不生、不打
    GEN    = 1,   -- 生成阶段：生水晶、不能打
    ATTACK = 2,   -- 攻击阶段：不生水晶、可以打
    END    = 3,   -- 结束
}

-- 阶段切换时间点
local SWITCH_TIMES = {
    [0]  = { ptype = PHASE.PREP,   round = 0, endTime = 7 },
    [7]  = { ptype = PHASE.GEN,    round = 1, endTime = 27 },
    [27] = { ptype = PHASE.ATTACK, round = 1, endTime = 47 },
    [47] = { ptype = PHASE.GEN,    round = 2, endTime = 67 },
    [67] = { ptype = PHASE.ATTACK, round = 2, endTime = 87 },
    [87] = { ptype = PHASE.GEN,    round = 3, endTime = 107 },
    [107]= { ptype = PHASE.ATTACK, round = 3, endTime = 127 },
}

local GAME_END_TIME = 127

-- ========== 阶段机实现（模拟 GameEventHandler 逻辑）==========

--- 根据已过时间返回当前阶段信息
local function GetPhaseAtTime(elapsed)
    if elapsed >= GAME_END_TIME then
        return { phaseType = PHASE.END, round = 3, remaining = 0, canAttack = false, crystalsSpawn = false }
    end
    if elapsed < 7 then
        return { phaseType = PHASE.PREP, round = 0, remaining = 7 - elapsed, canAttack = false, crystalsSpawn = false }
    elseif elapsed < 27 then
        return { phaseType = PHASE.GEN, round = 1, remaining = 27 - elapsed, canAttack = false, crystalsSpawn = true }
    elseif elapsed < 47 then
        return { phaseType = PHASE.ATTACK, round = 1, remaining = 47 - elapsed, canAttack = true, crystalsSpawn = false }
    elseif elapsed < 67 then
        return { phaseType = PHASE.GEN, round = 2, remaining = 67 - elapsed, canAttack = false, crystalsSpawn = true }
    elseif elapsed < 87 then
        return { phaseType = PHASE.ATTACK, round = 2, remaining = 87 - elapsed, canAttack = true, crystalsSpawn = false }
    elseif elapsed < 107 then
        return { phaseType = PHASE.GEN, round = 3, remaining = 107 - elapsed, canAttack = false, crystalsSpawn = true }
    else
        return { phaseType = PHASE.ATTACK, round = 3, remaining = 127 - elapsed, canAttack = true, crystalsSpawn = false }
    end
end

--- 推进时间 dt 秒，返回是否发生了阶段切换、新阶段信息
local function TickPhase(prevElapsed, dt)
    local oldPhase = GetPhaseAtTime(prevElapsed)
    local newElapsed = prevElapsed + dt
    local newPhase = GetPhaseAtTime(newElapsed)
    local switched = (oldPhase.phaseType ~= newPhase.phaseType)
    return newElapsed, switched, newPhase
end

-- ========== 运行入口 ==========

local function run()
    TF.group("Z2 — 阶段状态机")

    -- ============================================
    -- 第1节：各时间点阶段类型
    -- ============================================

    local testPoints = {
        { t = 0,   expected = PHASE.PREP,   label = "Z2-1-t=0准备" },
        { t = 3,   expected = PHASE.PREP,   label = "Z2-2-t=3准备" },
        { t = 6.9, expected = PHASE.PREP,   label = "Z2-3-t=6.9准备(边界前)" },
        { t = 7,   expected = PHASE.GEN,    label = "Z2-4-t=7→生成1" },
        { t = 10,  expected = PHASE.GEN,    label = "Z2-5-t=10生成1" },
        { t = 26.9,expected = PHASE.GEN,    label = "Z2-6-t=26.9生成1(边界前)" },
        { t = 27,  expected = PHASE.ATTACK, label = "Z2-7-t=27→攻击1" },
        { t = 35,  expected = PHASE.ATTACK, label = "Z2-8-t=35攻击1" },
        { t = 47,  expected = PHASE.GEN,    label = "Z2-9-t=47→生成2" },
        { t = 55,  expected = PHASE.GEN,    label = "Z2-10-t=55生成2" },
        { t = 67,  expected = PHASE.ATTACK, label = "Z2-11-t=67→攻击2" },
        { t = 75,  expected = PHASE.ATTACK, label = "Z2-12-t=75攻击2" },
        { t = 87,  expected = PHASE.GEN,    label = "Z2-13-t=87→生成3" },
        { t = 95,  expected = PHASE.GEN,    label = "Z2-14-t=95生成3" },
        { t = 107, expected = PHASE.ATTACK, label = "Z2-15-t=107→攻击3" },
        { t = 115, expected = PHASE.ATTACK, label = "Z2-16-t=115攻击3" },
        { t = 126.9, expected = PHASE.ATTACK, label = "Z2-17-t=126.9攻击3末" },
        { t = 127, expected = PHASE.END,    label = "Z2-18-t=127结束" },
        { t = 200, expected = PHASE.END,    label = "Z2-19-t=200仍结束" },
    }

    for _, tp in ipairs(testPoints) do
        local p = GetPhaseAtTime(tp.t)
        TF.assertEqual(p.phaseType, tp.expected, 0, tp.label)
    end

    -- ============================================
    -- 第2节：canAttack 标志
    -- ============================================

    -- 准备阶段不能打
    for _, t in ipairs({0, 3, 6.9}) do
        TF.assertFalse(GetPhaseAtTime(t).canAttack, "Z2-20-准备t=" .. t .. "不能打")
    end
    -- 生成阶段不能打（三段生成都要验证）
    for _, t in ipairs({10, 20, 50, 60, 90, 100}) do
        TF.assertFalse(GetPhaseAtTime(t).canAttack, "Z2-21-生成t=" .. t .. "不能打")
    end
    -- 攻击阶段可以打
    for _, t in ipairs({30, 40, 70, 80, 110, 120}) do
        TF.assertTrue(GetPhaseAtTime(t).canAttack, "Z2-22-攻击t=" .. t .. "可以打")
    end
    -- 结束后不能打
    TF.assertFalse(GetPhaseAtTime(150).canAttack, "Z2-23-结束后不能打")

    -- ============================================
    -- 第3节：crystalsSpawn 标志
    -- ============================================

    -- 准备阶段不生成
    for _, t in ipairs({0, 5}) do
        TF.assertFalse(GetPhaseAtTime(t).crystalsSpawn, "Z2-24-准备不生")
    end
    -- 生成阶段生成
    for _, t in ipairs({10, 50, 90}) do
        TF.assertTrue(GetPhaseAtTime(t).crystalsSpawn, "Z2-25-生成t=" .. t .. "可生")
    end
    -- 攻击阶段不生成
    for _, t in ipairs({30, 70, 110}) do
        TF.assertFalse(GetPhaseAtTime(t).crystalsSpawn, "Z2-26-攻击不生")
    end

    -- ============================================
    -- 第4节：round 轮次追踪
    -- ============================================

    TF.assertEqual(GetPhaseAtTime(3).round, 0, 0, "Z2-27-准备轮0")
    TF.assertEqual(GetPhaseAtTime(15).round, 1, 0, "Z2-28-生成1轮1")
    TF.assertEqual(GetPhaseAtTime(35).round, 1, 0, "Z2-29-攻击1轮1")
    TF.assertEqual(GetPhaseAtTime(55).round, 2, 0, "Z2-30-生成2轮2")
    TF.assertEqual(GetPhaseAtTime(75).round, 2, 0, "Z2-31-攻击2轮2")
    TF.assertEqual(GetPhaseAtTime(95).round, 3, 0, "Z2-32-生成3轮3")
    TF.assertEqual(GetPhaseAtTime(115).round, 3, 0, "Z2-33-攻击3轮3")

    -- ============================================
    -- 第5节：阶段切换检测
    -- ============================================

    -- Z2-34: 正常推进不跳过阶段
    local elapsed = 0
    local switchCount = 0
    local switchedPhases = {}
    while elapsed < 130 do
        local newElapsed, switched, phase = TickPhase(elapsed, 0.5)
        if switched then
            switchCount = switchCount + 1
            table.insert(switchedPhases, { at = newElapsed, phaseType = phase.phaseType, round = phase.round })
        end
        elapsed = newElapsed
    end
    -- 应有 7 次切换: 0→7 prep→gen1, 7→27 gen1→att1, 27→47 att1→gen2, 47→67 gen2→att2, 67→87 att2→gen3, 87→107 gen3→att3, 107→127 att3→end
    TF.assertEqual(switchCount, 7, 0, "Z2-34-正常推进7次阶段切换")
    -- 验证切换顺序
    local expectedSwitches = {
        { phaseType = PHASE.GEN,    round = 1 },
        { phaseType = PHASE.ATTACK, round = 1 },
        { phaseType = PHASE.GEN,    round = 2 },
        { phaseType = PHASE.ATTACK, round = 2 },
        { phaseType = PHASE.GEN,    round = 3 },
        { phaseType = PHASE.ATTACK, round = 3 },
        { phaseType = PHASE.END,    round = 3 },
    }
    for i, exp in ipairs(expectedSwitches) do
        if switchedPhases[i] then
            local matchPhase = switchedPhases[i].phaseType == exp.phaseType
            local matchRound = switchedPhases[i].round == exp.round
            TF.assertTrue(matchPhase and matchRound,
                string.format("Z2-34%s-切%d→%s轮%d", string.char(96+i), i,
                    exp.phaseType == PHASE.GEN and "GEN" or exp.phaseType == PHASE.ATTACK and "ATTACK" or "END",
                    exp.round))
        end
    end

    -- Z2-35: 阶段切换在精确边界
    -- 在 t=26.999 应该是 GEN，t=27.001 应该是 ATTACK
    TF.assertEqual(GetPhaseAtTime(26.999).phaseType, PHASE.GEN, 0, "Z2-35a-t=26.999仍是GEN")
    TF.assertEqual(GetPhaseAtTime(27).phaseType, PHASE.ATTACK, 0, "Z2-35b-t=27切ATTACK")
    TF.assertEqual(GetPhaseAtTime(27.001).phaseType, PHASE.ATTACK, 0, "Z2-35c-t=27.001已是ATTACK")

    -- ============================================
    -- 第6节：总时长 = 127
    -- ============================================

    local phases = {}
    local curPhase = nil
    for t = 0, 126 do
        local p = GetPhaseAtTime(t)
        local key = p.phaseType .. "_" .. p.round
        if curPhase ~= key then
            table.insert(phases, { start = t, phaseType = p.phaseType, round = p.round })
            curPhase = key
        end
    end
    -- 各阶段持续时长
    local durations = {}
    for i = 1, #phases - 1 do
        local dur = phases[i + 1].start - phases[i].start
        table.insert(durations, dur)
    end
    -- 最后一段到 127
    table.insert(durations, 127 - phases[#phases].start)

    local totalDuration = 0
    for _, d in ipairs(durations) do totalDuration = totalDuration + d end
    TF.assertEqual(totalDuration, 127, 0, "Z2-36-总时长=127")

    -- 各段时长精确性
    TF.assertEqual(durations[1], 7, 0, "Z2-36b-准备=7s")
    -- 生成1 20s, 攻击1 20s, 生成2 20s, 攻击2 20s, 生成3 20s, 攻击3 20s = 120s gen+attack
    -- 加准备7s = 127s
    local genAttackTotal = 0
    for i = 2, #durations do genAttackTotal = genAttackTotal + durations[i] end
    TF.assertEqual(genAttackTotal, 120, 0, "Z2-36c-生成+攻击3轮=120s")

    -- ============================================
    -- 第7节：边界异常输入
    -- ============================================

    -- Z2-37: 负数时间 → 准备阶段
    local pNeg = GetPhaseAtTime(-1)
    TF.assertEqual(pNeg.phaseType, PHASE.PREP, 0, "Z2-37-负时间→准备")

    -- Z2-38: 极大时间 → 结束
    local pBig = GetPhaseAtTime(99999)
    TF.assertEqual(pBig.phaseType, PHASE.END, 0, "Z2-38-超大时间→结束")

    -- Z2-39: dt=0 不触发切换
    local midTime = 20  -- 在生成1期间
    local newElapsed, switched, _ = TickPhase(midTime, 0)
    TF.assertFalse(switched, "Z2-39-dt=0不切换")
    TF.assertEqual(newElapsed, midTime, 0, "Z2-39b-时间不变")

    -- Z2-40: 大dt跨多阶段（理论上不应发生，但需鲁棒）
    -- 从生成1中途(10s)加100s，应直接到攻击3
    local _, _, p = TickPhase(10, 100)
    TF.assertEqual(p.phaseType, PHASE.ATTACK, 0, "Z2-40-大跨步直接到攻击3")
    TF.assertEqual(p.round, 3, 0, "Z2-40b-大跨步轮次=3")

    print("[Z2] 阶段逻辑测试完成")
end

return { run = run }
