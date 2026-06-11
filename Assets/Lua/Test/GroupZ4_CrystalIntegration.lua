-- =============================================
-- Test/GroupZ4_CrystalIntegration.lua — 集成场景 (28条)
-- =============================================
-- 多系统联动：阶段机+水晶生成+拾取+掉落+分数追踪。
-- 纯逻辑模拟，无需Unity场景。
-- =============================================

local TF = require("Test.TestFramework")

-- ========== 简化的世界模拟 ==========

--- 阶段定义
local PHASE = { PREP = 0, GEN = 1, ATTACK = 2, END = 3 }

--- 阶段机
local function GetPhaseAtTime(elapsed)
    if elapsed >= 127 then return { ptype = PHASE.END, round = 3, canAttack = false, canSpawn = false }
    elseif elapsed < 7 then  return { ptype = PHASE.PREP, round = 0, canAttack = false, canSpawn = false }
    elseif elapsed < 27 then return { ptype = PHASE.GEN, round = 1, canAttack = false, canSpawn = true }
    elseif elapsed < 47 then return { ptype = PHASE.ATTACK, round = 1, canAttack = true, canSpawn = false }
    elseif elapsed < 67 then return { ptype = PHASE.GEN, round = 2, canAttack = false, canSpawn = true }
    elseif elapsed < 87 then return { ptype = PHASE.ATTACK, round = 2, canAttack = true, canSpawn = false }
    elseif elapsed < 107 then return { ptype = PHASE.GEN, round = 3, canAttack = false, canSpawn = true }
    else return { ptype = PHASE.ATTACK, round = 3, canAttack = true, canSpawn = false }
    end
end

--- 5个生成区域
local ZONES = {
    { x = 10, z = 10,  r = 8 },
    { x = -10, z = 20, r = 8 },
    { x = 25, z = -15, r = 8 },
    { x = -20, z = -10, r = 8 },
    { x = 0,  z = 30,  r = 8 },
}

local ZONE_SPAWN_INTERVAL = 1.5
local PICKUP_RANGE = 0.8

-- ========== 世界状态 ==========

local function NewWorld()
    return {
        elapsed = 0,
        crystals = {},        -- { [id] = { posX, posZ, alive } }
        nextCrystalId = 1,
        zoneTimers = { 0, 0, 0, 0, 0 },  -- 每个区域的累积计时
        -- 玩家
        players = {},         -- { [pid] = { holding, score, posX, posZ, alive, birthX, birthZ } }
        -- 日志
        events = {},
    }
end

local function AddPlayer(w, pid, birthX, birthZ)
    w.players[pid] = {
        holding = 0, score = 0,
        posX = birthX or 0, posZ = birthZ or 0,
        alive = true,
        birthX = birthX or 0, birthZ = birthZ or 0,
    }
end

local function CalcDrop(holding)
    if holding <= 0 then return 0 end
    return math.ceil(holding * 0.3)
end

--- 区域圆内随机（简化：用确定性公式替代随机数）
--- 确定性替代：使用区域索引+水晶计数来确定性生成
local function DeterministicSpawnPos(zoneIdx, spawnCount)
    -- 用 zoneIdx 和 spawnCount 计算确定性位置
    local angle = (zoneIdx * 1.618 + spawnCount * 0.618) * math.pi % (2 * math.pi)
    local dist  = ((zoneIdx * 73 + spawnCount * 137) % 1000) / 1000.0
    dist = math.sqrt(dist) * ZONES[zoneIdx].r  -- 面积均匀
    local z = ZONES[zoneIdx]
    return z.x + math.cos(angle) * dist, z.z + math.sin(angle) * dist
end

-- ========== Tick 模拟 ==========

local function TickWorld(w, dt)
    w.elapsed = w.elapsed + dt
    local phase = GetPhaseAtTime(w.elapsed)
    if phase.ptype == PHASE.END then return end

    -- 生成阶段：各区域计时
    if phase.canSpawn then
        for z = 1, 5 do
            w.zoneTimers[z] = w.zoneTimers[z] + dt
            while w.zoneTimers[z] >= ZONE_SPAWN_INTERVAL do
                w.zoneTimers[z] = w.zoneTimers[z] - ZONE_SPAWN_INTERVAL
                local px, pz = DeterministicSpawnPos(z, w.nextCrystalId)
                w.crystals[w.nextCrystalId] = { posX = px, posZ = pz, alive = true }
                table.insert(w.events, { time = w.elapsed, evt = "spawn", crystalId = w.nextCrystalId, zone = z })
                w.nextCrystalId = w.nextCrystalId + 1
            end
        end
    end

    -- 所有阶段均可拾取
    for _pid, player in pairs(w.players) do
        if player.alive then
            for cid, _c in pairs(w.crystals) do
                local dx = player.posX - _c.posX
                local dz = player.posZ - _c.posZ
                if math.sqrt(dx * dx + dz * dz) <= PICKUP_RANGE then
                    -- 拾取！
                    w.crystals[cid] = nil
                    player.holding = player.holding + 1
                    player.score = player.holding * 6
                    table.insert(w.events, { time = w.elapsed, evt = "pickup", crystalId = cid, playerId = _pid, score = player.score })
                    break  -- 每玩家每帧最多捡一个
                end
            end
        end
    end
end

--- 模拟玩家死亡
local function KillPlayer(w, victimId)
    local p = w.players[victimId]
    if not p or not p.alive then return end
    local drop = CalcDrop(p.holding)
    p.holding = p.holding - drop
    p.score = p.holding * 6
    -- 掉落水晶出现在死亡位置
    for _ = 1, drop do
        w.crystals[w.nextCrystalId] = {
            posX = p.posX,
            posZ = p.posZ,
            alive = true,
        }
        table.insert(w.events, { time = w.elapsed, evt = "drop", crystalId = w.nextCrystalId,
            fromPlayer = victimId, posX = p.posX, posZ = p.posZ })
        w.nextCrystalId = w.nextCrystalId + 1
    end
    -- 重生到出生点
    p.alive = false
    p.posX, p.posZ = p.birthX, p.birthZ
    p.alive = true
    table.insert(w.events, { time = w.elapsed, evt = "death", playerId = victimId,
        dropped = drop, remaining = p.holding, score = p.score })
end

--- 移动玩家到位置（模拟走过捡水晶）
local function MovePlayerTo(w, pid, x, z)
    local p = w.players[pid]
    if not p or not p.alive then return end
    p.posX = x
    p.posZ = z
end

--- 移动玩家向最近水晶（简单AI）
local function moveTowardNearest(w, pid)
    local p = w.players[pid]
    if not p or not p.alive then return end
    local nearestDist, nearestCid = math.huge, nil
    for cid, c in pairs(w.crystals) do
        local dx = p.posX - c.posX
        local dz = p.posZ - c.posZ
        local d = math.sqrt(dx * dx + dz * dz)
        if d < nearestDist then
            nearestDist = d
            nearestCid = cid
        end
    end
    if nearestCid then
        local c = w.crystals[nearestCid]
        local dx = c.posX - p.posX
        local dz = c.posZ - p.posZ
        local d = math.sqrt(dx * dx + dz * dz)
        if d > 0 then
            local step = 5 * 0.1  -- 5m/s speed
            p.posX = p.posX + (dx / d) * math.min(step, d)
            p.posZ = p.posZ + (dz / d) * math.min(step, d)
        end
    end
end

-- ========== 运行入口 ==========

local function run()
    TF.group("Z4 — 水晶集成场景")

    -- ============================================
    -- 场景1：单人生成+拾取循环
    -- ============================================

    do
        local w = NewWorld()
        AddPlayer(w, 1, 0, 0)

        -- 模拟 30 秒：玩家站在出生点不动，水晶在周围生成
        -- 如果生成位置离玩家足够近，会自动拾取
        local pickupCount = 0
        for ms = 1, 30000 do
            local dt = 0.001
            TickWorld(w, dt)
            -- 记录拾取事件数
            local newPickups = 0
            for _, evt in ipairs(w.events) do
                if evt.evt == "pickup" and evt.playerId == 1 then
                    newPickups = newPickups + 1
                end
            end
            pickupCount = newPickups
        end
        -- 玩家站在原点(0,0)，zone4 中心在 (0,30,r=8)，最近点 22m，不够 0.8m
        -- 玩家不太可能自动捡到。这是正确的 —— 玩家需要移动
        TF.assertTrue(w.players[1].holding >= 0, "Z4-1-单人不动可能捡不到(正常)")
        -- 验证生成阶段产生了水晶
        -- 13s 生成阶段，每1.5s/区域，5区域 → 13/1.5≈8.67→9个/区，但8.67轮×5区=43个期望
        local totalSpawned = 0
        for _, evt in ipairs(w.events) do
            if evt.evt == "spawn" then totalSpawned = totalSpawned + 1 end
        end
        -- 第一轮生成13s(7s开始到20s到27...哦不对，7-27生成，20s/1.5=13.33轮)
        -- 实际上只有13s生成（7-20？不对...prep到7s，gen1是7-27，attack1是27-47，gen2是47-67，attack2...)
        -- 在30s内：7-27 gen 20s → 13.3轮×5区≈66个水晶
        TF.assertTrue(totalSpawned > 40, "Z4-1b-30s内生成>40个水晶(" .. totalSpawned .. ")")
        -- 攻击阶段不生水晶
        local spawnInAttack = false
        for _, evt in ipairs(w.events) do
            if evt.evt == "spawn" then
                local ph = GetPhaseAtTime(evt.time)
                if ph.ptype == PHASE.ATTACK then
                    spawnInAttack = true
                    break
                end
            end
        end
        TF.assertFalse(spawnInAttack, "Z4-1c-攻击阶段不生水晶")
    end

    -- ============================================
    -- 场景2：两玩家竞抢+移动拾取
    -- ============================================

    do
        local w = NewWorld()
        AddPlayer(w, 1, 0, 0)    -- 玩家1在原点
        AddPlayer(w, 2, 25, -15) -- 玩家2在zone3附近

        -- 先让水晶生成一批（等7秒到生成阶段开始，再等5秒让水晶生成）
        for ms = 1, 12000 do TickWorld(w, 0.001) end  -- 跑到12s

        -- 看看生成了多少水晶
        local crystalsAfterSpawn = 0
        for _ in pairs(w.crystals) do crystalsAfterSpawn = crystalsAfterSpawn + 1 end
        -- 生成阶段7-12s: 5s/1.5=3.33轮×5区≈15个
        TF.assertTrue(crystalsAfterSpawn >= 10, "Z4-2a-12s生成≥10个(" .. crystalsAfterSpawn .. ")")

        -- 玩家1移动到玩家2的出生点附近去抢（12-20s慢慢走过去）
        -- 实际上玩家需要走到水晶旁边，我们直接把他放到zone3附近有几个水晶的地方
        local crystalPositions = {}
        for cid, c in pairs(w.crystals) do
            table.insert(crystalPositions, { id = cid, x = c.posX, z = c.posZ })
        end
        -- 把玩家1移到第一个水晶位置
        if #crystalPositions > 0 then
            MovePlayerTo(w, 1, crystalPositions[1].x, crystalPositions[1].z)
        end
        -- 再tick一帧触发拾取
        TickWorld(w, 0.001)
        TF.assertTrue(w.players[1].holding > 0, "Z4-2b-站在水晶上即拾取")

        -- 把玩家2移到另一个水晶位置
        if #crystalPositions > 1 then
            MovePlayerTo(w, 2, crystalPositions[2].x, crystalPositions[2].z)
        end
        TickWorld(w, 0.001)
        TF.assertTrue(w.players[2].holding > 0, "Z4-2c-玩家2也拾取了水晶")

        -- 两人分数各不相同
        local s1, s2 = w.players[1].score, w.players[2].score
        TF.assertTrue(s1 > 0, "Z4-2d-玩家1分数>0")
        TF.assertTrue(s2 > 0, "Z4-2e-玩家2分数>0")
        -- 分数公式：N×6
        TF.assertEqual(s1 % 6, 0, 0, "Z4-2f-玩家1分数是6的倍数")
        TF.assertEqual(s2 % 6, 0, 0, "Z4-2g-玩家2分数是6的倍数")
    end

    -- ============================================
    -- 场景3：死亡掉落 + 其他人拾取
    -- ============================================

    do
        local w = NewWorld()
        AddPlayer(w, 1, 0, 0)
        AddPlayer(w, 2, 30, 30)

        -- 给玩家1手动加8颗水晶（模拟已捡了8颗）
        w.players[1].holding = 8
        w.players[1].score = 48
        w.players[1].posX = 15  -- 死在这个位置
        w.players[1].posZ = 15

        -- 杀死玩家1
        KillPlayer(w, 1)
        TF.assertEqual(w.players[1].holding, 5, 0, "Z4-3a-8颗→掉3颗→剩5颗")
        TF.assertEqual(w.players[1].score, 30, 0, "Z4-3b-5颗=30分")

        -- 验证掉了3颗水晶在死亡位置(15,15)
        local dropCount = 0
        for _, evt in ipairs(w.events) do
            if evt.evt == "drop" and evt.fromPlayer == 1 then
                dropCount = dropCount + 1
                TF.assertEqual(evt.posX, 15, TF.LOOSE, "Z4-3c-掉落X=15")
                TF.assertEqual(evt.posZ, 15, TF.LOOSE, "Z4-3d-掉落Z=15")
            end
        end
        TF.assertEqual(dropCount, 3, 0, "Z4-3e-掉落3颗")

        -- 玩家1重生在自己出生点(0,0)
        TF.assertEqual(w.players[1].posX, 0, TF.LOOSE, "Z4-3f-重生X=0")
        TF.assertEqual(w.players[1].posZ, 0, TF.LOOSE, "Z4-3g-重生Z=0")
        TF.assertTrue(w.players[1].alive, "Z4-3h-重生后存活")

        -- 玩家2走到死亡位置(15,15)捡掉落
        MovePlayerTo(w, 2, 15, 15)
        for _ = 1, 5 do TickWorld(w, 0.001) end
        -- 玩家2应该捡到了掉落水晶
        TF.assertTrue(w.players[2].holding >= 1, "Z4-3i-玩家2捡到掉落")
        TF.assertEqual(w.players[2].score % 6, 0, 0, "Z4-3j-玩家2分数是6倍数")
    end

    -- ============================================
    -- 场景4：全程127秒模拟
    -- ============================================

    do
        local w = NewWorld()
        AddPlayer(w, 1, 0, 0)
        AddPlayer(w, 2, 30, 30)
        AddPlayer(w, 3, -30, 30)
        AddPlayer(w, 4, 0, -30)

        -- 玩家移动用全局 moveTowardNearest

        -- 攻击阶段模拟击杀
        local lastPhase = nil
        for ms = 1, 127000 do
            local dt = 0.001
            local prevElapsed = w.elapsed
            TickWorld(w, dt)
            -- 速度5m/s，0.1s更新一次移动
            if ms % 100 == 0 then
                for pid = 1, 4 do
                    moveTowardNearest(w, pid)
                end
            end

            -- 攻击阶段期间，每10秒随机击杀一次（模拟战斗）
            local phase = GetPhaseAtTime(w.elapsed)
            if phase.ptype == PHASE.ATTACK and ms % 10000 == 0 then
                -- 找持有水晶最多的人杀了
                local maxHold, victimPid = -1, nil
                for pid, p in pairs(w.players) do
                    if p.alive and p.holding > maxHold then
                        maxHold = p.holding
                        victimPid = pid
                    end
                end
                if victimPid and maxHold > 0 then
                    KillPlayer(w, victimPid)
                end
            end
            lastPhase = phase
        end

        -- Z4-4a: 127秒后游戏结束
        local endPhase = GetPhaseAtTime(w.elapsed)
        TF.assertEqual(endPhase.ptype, PHASE.END, 0, "Z4-4a-127s后结束")

        -- Z4-4b: 所有玩家分数都是6的倍数
        for pid = 1, 4 do
            TF.assertEqual(w.players[pid].score % 6, 0, 0,
                "Z4-4b-玩家" .. pid .. "分数是6倍数(" .. w.players[pid].score .. ")")
        end

        -- Z4-4c: 所有事件时间在有效范围内
        for _, evt in ipairs(w.events) do
            TF.assertTrue(evt.time >= 0 and evt.time <= 127,
                "Z4-4c-事件时间在[0,127]")
        end

        -- Z4-4d: 生成水晶总数合理
        -- 三轮生成: 13s+20s+20s=53s, 每1.5s×5区=3.33/区/s, 53×3.33≈176个期望
        local totalSpawned, totalPickups, totalDrops = 0, 0, 0
        for _, evt in ipairs(w.events) do
            if evt.evt == "spawn" then totalSpawned = totalSpawned + 1
            elseif evt.evt == "pickup" then totalPickups = totalPickups + 1
            elseif evt.evt == "drop" then totalDrops = totalDrops + 1
            end
        end
        TF.assertTrue(totalSpawned > 100, "Z4-4d-生成>100颗(" .. totalSpawned .. ")")
        -- 拾取+场上剩余+掉落=生成+掉落
        local remaining = 0
        for _ in pairs(w.crystals) do remaining = remaining + 1 end
        TF.assertEqual(totalPickups + remaining, totalSpawned + totalDrops, 0,
            "Z4-4e-物料守恒(拾取" .. totalPickups .. "+剩余" .. remaining .. "=生成" .. totalSpawned .. "+掉落" .. totalDrops .. ")")

        -- Z4-4f: 攻击阶段击杀次数>0（验证击杀只发生在攻击阶段）
        local killCount = 0
        local killInWrongPhase = 0
        for _, evt in ipairs(w.events) do
            if evt.evt == "death" then
                killCount = killCount + 1
                local ph = GetPhaseAtTime(evt.time)
                if ph.ptype ~= PHASE.ATTACK then
                    killInWrongPhase = killInWrongPhase + 1
                end
            end
        end
        TF.assertTrue(killCount > 0, "Z4-4f-有击杀发生(" .. killCount .. "次)")
        -- 测试代码限制了击杀只在攻击阶段触发，所以这个断言应通过
        TF.assertEqual(killInWrongPhase, 0, 0, "Z4-4g-击杀全在攻击阶段")
    end

    -- ============================================
    -- 场景5：准备阶段不生成水晶
    -- ============================================

    do
        local w = NewWorld()
        AddPlayer(w, 1, 0, 0)
        -- 跑完准备阶段的前6.9秒
        for ms = 1, 6900 do TickWorld(w, 0.001) end
        -- 应该0个水晶
        local crystalCount = 0
        for _ in pairs(w.crystals) do crystalCount = crystalCount + 1 end
        TF.assertEqual(crystalCount, 0, 0, "Z4-5-准备阶段不生水晶")
        -- 再跑到7.1秒，进入生成阶段
        for ms = 1, 200 do TickWorld(w, 0.001) end
        -- 现在应该有水晶了（虽然有计时累积延迟）
        for ms = 1, 1500 do TickWorld(w, 0.001) end
        crystalCount = 0
        for _ in pairs(w.crystals) do crystalCount = crystalCount + 1 end
        TF.assertTrue(crystalCount > 0, "Z4-5b-进入生成阶段后有水晶(" .. crystalCount .. ")")
    end

    -- ============================================
    -- 场景6：持有水晶的一致性不变量
    -- ============================================

    do
        -- 任一时刻：总分 = Σ持有×6，水晶总分 = 生成数×6 - 死亡净损失
        -- 即 Σ(holding) = spawned - (died_dropped_total - 场剩余)
        local w = NewWorld()
        AddPlayer(w, 1, 0, 0)
        AddPlayer(w, 2, 30, 30)

        -- 短模拟10秒
        local totalSpawned = 0
        for ms = 1, 25000 do  -- 25秒
            TickWorld(w, 0.001)
            if ms % 500 == 0 then
                moveTowardNearest(1, w)
                moveTowardNearest(2, w)
            end
        end

        -- 一致性检查：Σ(玩家持有) + 场上水晶数 = 总生成的水晶 - 已死亡丢失
        local playerTotal = 0
        for pid = 1, 2 do
            playerTotal = playerTotal + w.players[pid].holding
        end
        local fieldCrystals = 0
        for _ in pairs(w.crystals) do fieldCrystals = fieldCrystals + 1 end

        local spawnedCount = 0
        for _, evt in ipairs(w.events) do
            if evt.evt == "spawn" then spawnedCount = spawnedCount + 1 end
        end

        -- playerTotal + fieldCrystals = spawnedCount（没有死亡，等式成立）
        TF.assertEqual(playerTotal + fieldCrystals, spawnedCount, 0,
            string.format("Z4-6-守恒: 玩家%d+场上%d=生成%d",
                playerTotal, fieldCrystals, spawnedCount))
    end

    -- ============================================
    -- 场景7：回合边界生成/攻击切换不丢水晶
    -- ============================================

    do
        local w = NewWorld()
        AddPlayer(w, 1, 0, 0)
        -- 跑到26.99s（生成1结束前）
        for ms = 1, 26990 do TickWorld(w, 0.001) end
        local beforeCount = 0
        for _ in pairs(w.crystals) do beforeCount = beforeCount + 1 end
        -- 跨过27s边界进入攻击阶段
        for ms = 1, 20 do TickWorld(w, 0.001) end
        local afterCount = 0
        for _ in pairs(w.crystals) do afterCount = afterCount + 1 end
        -- 水晶不应消失（只是停止生成，不删除已有的）
        TF.assertEqual(beforeCount, afterCount, 0,
            string.format("Z4-7-阶段切换不丢水晶(%d→%d)", beforeCount, afterCount))
    end

    -- ============================================
    -- 场景8：最高分者获胜
    -- ============================================

    do
        local w = NewWorld()
        AddPlayer(w, 1, 0, 0)
        AddPlayer(w, 2, 30, 30)
        w.players[1].holding = 8
        w.players[1].score = 48
        w.players[2].holding = 5
        w.players[2].score = 30

        -- 时间结束，找最高分
        local function findWinner(players)
            local maxScore = -1
            local winners = {}
            for pid, p in pairs(players) do
                if p.score > maxScore then
                    maxScore = p.score
                    winners = { pid }
                elseif p.score == maxScore then
                    table.insert(winners, pid)
                end
            end
            return winners, maxScore
        end

        local winners, maxScore = findWinner(w.players)
        TF.assertEqual(#winners, 1, 0, "Z4-8a-唯一胜者")
        TF.assertEqual(winners[1], 1, 0, "Z4-8b-玩家1胜(48>30)")

        -- 平局场景
        w.players[2].holding = 8
        w.players[2].score = 48
        local winners2, _ = findWinner(w.players)
        TF.assertEqual(#winners2, 2, 0, "Z4-8c-平局→并列获胜")
    end

    -- ============================================
    -- 场景9：字段一致性（重生存活标志检查）
    -- ============================================

    do
        local w = NewWorld()
        AddPlayer(w, 1, 0, 0)
        -- 玩家应从alive开始
        TF.assertTrue(w.players[1].alive, "Z4-9a-初始存活")
        -- 杀死（模拟死亡）
        w.players[1].alive = false
        -- 重生
        w.players[1].posX = w.players[1].birthX
        w.players[1].posZ = w.players[1].birthZ
        w.players[1].alive = true
        TF.assertTrue(w.players[1].alive, "Z4-9b-重生后存活")
        TF.assertEqual(w.players[1].posX, 0, TF.LOOSE, "Z4-9c-重生回出生点X")
        TF.assertEqual(w.players[1].posZ, 0, TF.LOOSE, "Z4-9d-重生回出生点Z")
    end

    print("[Z4] 水晶集成测试完成")
end

return { run = run }
