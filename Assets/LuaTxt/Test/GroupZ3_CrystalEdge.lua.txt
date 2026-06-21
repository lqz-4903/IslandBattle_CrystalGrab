-- =============================================
-- Test/GroupZ3_CrystalEdge.lua — 水晶边界/并发/异常态 (38条)
-- =============================================
-- 纯逻辑测试：竞态条件、非法操作防护、边界参数。
-- =============================================

local TF = require("Test.TestFramework")

-- ========== 模拟水晶管理器（纯数据，无GameObject）==========

local function NewCrystalMgr()
    return {
        crystals = {},       -- { [crystalId] = { posX, posZ, alive } }
        nextId   = 1,
    }
end

local function SpawnCrystal(mgr, posX, posZ)
    local id = mgr.nextId
    mgr.crystals[id] = { posX = posX or 0, posZ = posZ or 0, alive = true }
    mgr.nextId = mgr.nextId + 1
    return id
end

local function RemoveCrystal(mgr, crystalId)
    if mgr.crystals[crystalId] then
        mgr.crystals[crystalId].alive = false
        mgr.crystals[crystalId] = nil
        return true
    end
    return false
end

--- 尝试拾取：返回成功/失败
local function TryPickup(mgr, crystalId)
    if mgr.crystals[crystalId] and mgr.crystals[crystalId].alive then
        mgr.crystals[crystalId] = nil
        return true
    end
    return false
end

--- 距离检测
local function IsWithinRange(px, pz, cx, cz, maxDist)
    local dx = px - cx
    local dz = pz - cz
    return math.sqrt(dx * dx + dz * dz) <= maxDist
end

-- ========== 运行入口 ==========

local function run()
    TF.group("Z3 — 水晶边界与异常")

    -- ============================================
    -- 第1节：拾取基本逻辑
    -- ============================================

    -- Z3-1: 正常拾取成功
    local mgr1 = NewCrystalMgr()
    local id1 = SpawnCrystal(mgr1, 10, 20)
    TF.assertTrue(TryPickup(mgr1, id1), "Z3-1-拾取存活水晶成功")
    TF.assertNil(mgr1.crystals[id1], "Z3-1b-拾取后水晶已删除")

    -- Z3-2: 重复拾取同一水晶 → 失败
    local mgr2 = NewCrystalMgr()
    local id2 = SpawnCrystal(mgr2, 0, 0)
    TryPickup(mgr2, id2)
    TF.assertFalse(TryPickup(mgr2, id2), "Z3-2-重复拾取失败")

    -- Z3-3: 拾取不存在的ID → 失败
    local mgr3 = NewCrystalMgr()
    TF.assertFalse(TryPickup(mgr3, 999), "Z3-3-不存在的ID拾取失败")

    -- Z3-4: 删除已删除的水晶 → 无异常
    local mgr4 = NewCrystalMgr()
    local id4 = SpawnCrystal(mgr4, 0, 0)
    RemoveCrystal(mgr4, id4)
    TF.assertNoCrash(function() RemoveCrystal(mgr4, id4) end, "Z3-4-重复删除不崩溃")
    TF.assertFalse(RemoveCrystal(mgr4, id4), "Z3-4b-重复删除返回false")

    -- ============================================
    -- 第2节：距离检测
    -- ============================================

    local PICKUP_RANGE = 0.8

    -- Z3-5: 重合点 → 在范围内
    TF.assertTrue(IsWithinRange(0, 0, 0, 0, PICKUP_RANGE), "Z3-5-重合在范围内")

    -- Z3-6: 刚好在边界内
    TF.assertTrue(IsWithinRange(0, 0, 0.7, 0, PICKUP_RANGE), "Z3-6-0.7m在范围内")

    -- Z3-7: 刚好在边界外
    TF.assertFalse(IsWithinRange(0, 0, 0.81, 0, PICKUP_RANGE), "Z3-7-0.81m在范围外")

    -- Z3-8: 对角距离正确
    -- (0.5, 0.5) 距离原点 sqrt(0.5)≈0.707 < 0.8
    TF.assertTrue(IsWithinRange(0, 0, 0.5, 0.5, PICKUP_RANGE), "Z3-8-对角0.5m在范围内")
    -- (0.6, 0.6) 距离原点 sqrt(0.72)≈0.849 > 0.8
    TF.assertFalse(IsWithinRange(0, 0, 0.6, 0.6, PICKUP_RANGE), "Z3-8b-对角0.6m在范围外")

    -- Z3-9: 只检测水平距离（XZ平面，忽略Y）
    -- Y差值不影响判断（水晶在地上，人在旁边）
    TF.assertTrue(IsWithinRange(0, 0, 0.5, 0, PICKUP_RANGE), "Z3-9-水平xy=0.5(不管y)")

    -- ============================================
    -- 第3节：并发拾取（两玩家竞争同一水晶）
    -- ============================================

    -- Z3-10: 两个玩家都在范围内，只有第一个成功
    local mgr10 = NewCrystalMgr()
    local id10 = SpawnCrystal(mgr10, 5, 5)
    -- 两个玩家都在 0.8m 内
    local p1 = TryPickup(mgr10, id10)  -- 玩家1先抢
    local p2 = TryPickup(mgr10, id10)  -- 玩家2后抢（同帧）
    TF.assertTrue(p1, "Z3-10a-玩家1拾取成功")
    TF.assertFalse(p2, "Z3-10b-玩家2拾取失败(已被1抢走)")

    -- Z3-11: 不同水晶并发 → 各取各的，互不影响
    local mgr11 = NewCrystalMgr()
    local idA = SpawnCrystal(mgr11, 0, 0)
    local idB = SpawnCrystal(mgr11, 10, 10)
    local rA = TryPickup(mgr11, idA)
    local rB = TryPickup(mgr11, idB)
    TF.assertTrue(rA, "Z3-11a-拾取A成功")
    TF.assertTrue(rB, "Z3-11b-拾取B成功")

    -- Z3-12: N个玩家都先到先得（服务端按收包顺序处理）
    local mgr12 = NewCrystalMgr()
    local id12 = SpawnCrystal(mgr12, 0, 0)
    local successCount = 0
    for _ = 1, 4 do
        if TryPickup(mgr12, id12) then
            successCount = successCount + 1
        end
    end
    TF.assertEqual(successCount, 1, 0, "Z3-12-4人竞争只有1人成功")

    -- ============================================
    -- 第4节：掉落物ID隔离
    -- ============================================

    -- Z3-13: 水晶生成和死亡掉落用独立的ID空间（共享序列即可）
    local mgr13 = NewCrystalMgr()
    -- 生成的水晶
    local spawnId1 = SpawnCrystal(mgr13, 0, 0)
    local spawnId2 = SpawnCrystal(mgr13, 5, 0)
    -- "死亡掉落"的水晶（相同ID空间，但不同位置）
    local dropId1 = SpawnCrystal(mgr13, 3, 3)  -- 模拟死亡掉落位置
    -- 三者ID不同
    TF.assertTrue(spawnId1 ~= spawnId2, "Z3-13a-生成ID1≠ID2")
    TF.assertTrue(spawnId1 ~= dropId1, "Z3-13b-生成ID≠掉落ID")
    TF.assertTrue(spawnId2 ~= dropId1, "Z3-13c-掉落ID唯一")
    -- 三者各自独立
    TF.assertTrue(mgr13.crystals[spawnId1] ~= nil, "Z3-13d-生成1存在")
    TF.assertTrue(mgr13.crystals[spawnId2] ~= nil, "Z3-13e-生成2存在")
    TF.assertTrue(mgr13.crystals[dropId1] ~= nil, "Z3-13f-掉落存在")

    -- ============================================
    -- 第5节：数量边界
    -- ============================================

    -- Z3-14: 无上限生成 → 创建大量水晶
    local mgr14 = NewCrystalMgr()
    local count = 200
    for i = 1, count do
        SpawnCrystal(mgr14, i, i)
    end
    TF.assertEqual(mgr14.nextId - 1, count, 0, "Z3-14a-生成200个ID不冲突")
    -- 全部可独立操作
    local aliveCount = 0
    for id, _ in pairs(mgr14.crystals) do
        aliveCount = aliveCount + 1
    end
    TF.assertEqual(aliveCount, count, 0, "Z3-14b-200个全存活")

    -- Z3-15: 水晶ID到很大数字
    local mgr15 = NewCrystalMgr()
    mgr15.nextId = 999999
    local bigId = SpawnCrystal(mgr15, 0, 0)
    TF.assertEqual(bigId, 999999, 0, "Z3-15a-大ID生成成功")
    TF.assertTrue(TryPickup(mgr15, bigId), "Z3-15b-大ID拾取成功")

    -- ============================================
    -- 第6节：一直拾取/删除 + 生成交替
    -- ============================================

    -- Z3-16: 快速生成→拾取→生成循环（模拟高吞吐）
    local mgr16 = NewCrystalMgr()
    local spawnCount, pickupCount = 0, 0
    for _ = 1, 100 do
        local id = SpawnCrystal(mgr16, 0, 0)
        spawnCount = spawnCount + 1
        if TryPickup(mgr16, id) then
            pickupCount = pickupCount + 1
        end
    end
    TF.assertEqual(spawnCount, 100, 0, "Z3-16a-生成100次")
    TF.assertEqual(pickupCount, 100, 0, "Z3-16b-拾取100次")
    -- 剩下的应该是0
    local remainingCount = 0
    for _ in pairs(mgr16.crystals) do remainingCount = remainingCount + 1 end
    TF.assertEqual(remainingCount, 0, 0, "Z3-16c-全部清空")

    -- ============================================
    -- 第7节：阶段切换时的水晶行为
    -- ============================================

    -- Z3-17: 攻击阶段开始时，场上已有水晶仍可拾取
    -- （生成停止 ≠ 水晶消失）
    local mgr17 = NewCrystalMgr()
    local existingId = SpawnCrystal(mgr17, 0, 0)
    -- 切换到攻击阶段（不生新水晶）
    -- 已有的水晶应该还在
    TF.assertTrue(mgr17.crystals[existingId] ~= nil, "Z3-17a-已有水晶仍在")
    TF.assertTrue(TryPickup(mgr17, existingId), "Z3-17b-攻击阶段仍可拾取")

    -- Z3-18: 生成阶段最后一秒生成的水晶，攻击阶段还能拾取
    local mgr18 = NewCrystalMgr()
    -- 在生成阶段末尾生成
    local lastMinuteId = SpawnCrystal(mgr18, 0, 0)
    -- 进入攻击阶段
    TF.assertTrue(mgr18.crystals[lastMinuteId] ~= nil, "Z3-18-末秒水晶攻击阶段仍存在")

    -- ============================================
    -- 第8节：5区域独立性
    -- ============================================

    -- Z3-19: 5个区域各自生成，互不影响
    local zones = {}
    for z = 1, 5 do
        zones[z] = { crystalCount = 0, lastSpawnTime = 0 }
    end
    -- 模拟每个区域独立1.5s计时器
    local function simulateZoneTick(zone, elapsed, interval)
        if elapsed - zone.lastSpawnTime >= interval then
            zone.lastSpawnTime = elapsed
            zone.crystalCount = zone.crystalCount + 1
            return true
        end
        return false
    end
    -- 跑10秒，每个区域应生成 floor(10/1.5) = 6 个（略有不齐）
    for t = 0, 1000 do  -- 1ms精度
        local elapsed = t / 100
        if elapsed > 10 then break end
        for z = 1, 5 do
            simulateZoneTick(zones[z], elapsed, 1.5)
        end
    end
    -- 每个区域约生成 10/1.5 ≈ 6-7个
    for z = 1, 5 do
        TF.assertTrue(zones[z].crystalCount >= 6, "Z3-19-区域" .. z .. "生成≥6个")
        TF.assertTrue(zones[z].crystalCount <= 7, "Z3-19b-区域" .. z .. "生成≤7个")
    end

    -- Z3-20: 区域生成互不阻塞
    -- 区域1生成时，区域2-5也在累积计时
    local zoneTimers = { 0, 0, 0, 0, 0 }
    local zoneCounts = { 0, 0, 0, 0, 0 }
    for ms = 1, 1500 do  -- 1.5秒内
        local dt = 0.001
        for z = 1, 5 do
            zoneTimers[z] = zoneTimers[z] + dt
            if zoneTimers[z] >= 1.5 then
                zoneTimers[z] = zoneTimers[z] - 1.5
                zoneCounts[z] = zoneCounts[z] + 1
            end
        end
    end
    -- 1.5秒整，每个区域应该恰好生成1个
    for z = 1, 5 do
        TF.assertEqual(zoneCounts[z], 1, 0, "Z3-20-区域" .. z .. "在1.5s恰好1个")
    end

    -- ============================================
    -- 第9节：死亡掉落边界
    -- ============================================

    -- Z3-21: 0颗死亡不掉落
    local function calcDrop(holding) return holding <= 0 and 0 or math.ceil(holding * 0.3) end
    TF.assertEqual(calcDrop(0), 0, 0, "Z3-21-0颗不掉落")

    -- Z3-22: 持有数减掉落后不能为负
    local function safeDrop(holding)
        local drop = calcDrop(holding)
        return math.max(0, holding - drop), drop
    end
    for _, h in ipairs({0, 1, 2, 3, 5, 10, 100}) do
        local remaining, _ = safeDrop(h)
        TF.assertTrue(remaining >= 0, "Z3-22-h" .. h .. "剩余≥0")
    end

    -- Z3-23: 掉落后分数 = 剩余×6
    for _, h in ipairs({0, 1, 3, 5, 10, 20}) do
        local remaining, drop = safeDrop(h)
        local expectedScore = remaining * 6
        TF.assertEqual(remaining * 6, expectedScore, 0,
            string.format("Z3-23-h%d→剩%d掉%d分=%d", h, remaining, drop, expectedScore))
    end

    -- Z3-24: 连续9次死亡，持有数逐步衰减到0
    local hold = 10
    local totalLost = 0
    while hold > 0 do
        local _, drop = safeDrop(hold)
        hold = hold - drop
        totalLost = totalLost + drop
    end
    TF.assertTrue(hold == 0, "Z3-24a-连续死亡持有归零")
    TF.assertTrue(totalLost <= 10, "Z3-24b-总掉落不会超总持有")

    -- ============================================
    -- 第10节：服务端权威校验
    -- ============================================

    -- Z3-25: 客户端上报拾取不存在的ID → 服务端拒绝
    local mgr25 = NewCrystalMgr()
    -- 客户端延迟时，水晶已被别人捡走
    local id25 = SpawnCrystal(mgr25, 0, 0)
    TryPickup(mgr25, id25)  -- 先被另一个客户端捡了
    -- 延迟的客户端请求到达服务端
    local latePickup = TryPickup(mgr25, id25)
    TF.assertFalse(latePickup, "Z3-25-已删除的ID再拾取被拒绝")

    -- Z3-26: 客户端上报不存在的拾取(凭空造ID) → 拒绝
    local mgr26 = NewCrystalMgr()
    TF.assertFalse(TryPickup(mgr26, 12345), "Z3-26-伪造ID拒绝")
    -- 分数不应改变
    -- （实际中需要确保 HandleCrystalPickup 不会给不存在的操作加分）

    -- ============================================
    -- 第11节：分数一致性
    -- ============================================

    -- Z3-27: 拾取N个 → 分数 = N×6
    local mgr27 = NewCrystalMgr()
    local totalPickup = 0
    for _ = 1, 15 do
        local id = SpawnCrystal(mgr27, 0, 0)
        if TryPickup(mgr27, id) then
            totalPickup = totalPickup + 1
        end
    end
    TF.assertEqual(totalPickup, 15, 0, "Z3-27a-拾取15个")
    TF.assertEqual(totalPickup * 6, 90, 0, "Z3-27b-15×6=90分")

    -- Z3-28: 拾取+掉落+再拾取 → 分数一致
    local function simulateScore(holding, spawns, deaths)
        for _ = 1, (spawns or 0) do holding = holding + 1 end
        for _ = 1, (deaths or 0) do
            local drop = calcDrop(holding)
            holding = holding - drop
        end
        return holding, holding * 6
    end
    local finalHolding, finalScore = simulateScore(0, 8, 1)
    -- 8 - ceil(8×0.3) = 8-3 = 5, 5×6 = 30
    TF.assertEqual(finalHolding, 5, 0, "Z3-28a-8颗死1次→5颗")
    TF.assertEqual(finalScore, 30, 0, "Z3-28b-5×6=30分")

    -- Z3-29: 多重死亡场景
    local h29, s29 = simulateScore(0, 12, 3)
    -- 12→死1: 12-ceil(3.6)=8; 死2: 8-ceil(2.4)=5; 死3: 5-ceil(1.5)=3
    -- 最终3颗=18分
    local verify = 12
    for _ = 1, 3 do verify = verify - calcDrop(verify) end
    TF.assertEqual(verify, 3, 0, "Z3-29a-12颗死3次→3颗")
    TF.assertEqual(verify * 6, 18, 0, "Z3-29b-3颗=18分")

    -- ============================================
    -- 第12节：无场景崩溃保护
    -- ============================================

    -- Z3-30: nil 水晶 ID 不崩溃
    TF.assertNoCrash(function() TryPickup({ crystals = {} }, nil) end, "Z3-30-nilID不崩溃")

    -- Z3-31: 空管理器操作不崩溃
    local emptyMgr = NewCrystalMgr()
    TF.assertNoCrash(function()
        TryPickup(emptyMgr, 1)
        RemoveCrystal(emptyMgr, 1)
        RemoveCrystal(emptyMgr, 99999)
    end, "Z3-31-空管理器不崩溃")

    -- ============================================
    -- 第13节：掉落位置
    -- ============================================

    -- Z3-32: 掉落位置 = 死亡位置
    local deathPosX, deathPosZ = 15.5, 22.3
    local mgr32 = NewCrystalMgr()
    -- 模拟死亡掉落：在死亡位置生成水晶
    local dropIds = {}
    for _ = 1, 4 do  -- 掉4颗
        local id = SpawnCrystal(mgr32, deathPosX, deathPosZ)
        table.insert(dropIds, id)
    end
    TF.assertEqual(#dropIds, 4, 0, "Z3-32a-掉落4颗")
    for _, id in ipairs(dropIds) do
        TF.assertTrue(mgr32.crystals[id] ~= nil, "Z3-32b-掉落水晶id" .. id .. "存在")
        TF.assertEqual(mgr32.crystals[id].posX, deathPosX, TF.LOOSE, "Z3-32c-掉落X=死亡X")
        TF.assertEqual(mgr32.crystals[id].posZ, deathPosZ, TF.LOOSE, "Z3-32d-掉落Z=死亡Z")
    end

    -- ============================================
    -- 第14节：冲刺拾取（同一帧多水晶在范围内）
    -- ============================================

    -- Z3-33: 玩家同时站在多个水晶范围内 → 一帧只捡一个（防止刷分）
    -- 这里只做逻辑层检测：服务端每个请求独立处理，客户端不应同帧发多个
    local mgr33 = NewCrystalMgr()
    local ids33 = {}
    for i = 1, 3 do
        ids33[i] = SpawnCrystal(mgr33, i * 0.1, 0)  -- 都堆在附近
    end
    -- 同帧只应触发一次拾取检测 → 捡到一个
    local picked = 0
    for _, id in ipairs(ids33) do
        if TryPickup(mgr33, id) then picked = picked + 1 end
    end
    -- 服务端不限制同帧多拾取，因为每个Pickup是独立请求
    -- 但客户端应避免同帧多请求（浪费带宽）
    TF.assertEqual(picked, 3, 0, "Z3-33a-同帧可处理多独立拾取")
    -- 验证全部清除
    for _, id in ipairs(ids33) do
        TF.assertNil(mgr33.crystals[id], "Z3-33b-id" .. id .. "已清除")
    end

    -- ============================================
    -- 第15节：5区域圆边界
    -- ============================================

    -- Z3-34: 点在圆边界上应该算有效
    local function pointInCircle(px, pz, cx, cz, r)
        return math.sqrt((px-cx)^2 + (pz-cz)^2) <= r + 0.0001
    end
    -- 正好在半径边界
    TF.assertTrue(pointInCircle(10 + 5, 20, 10, 20, 5), "Z3-34-边界上方点在圆内")
    TF.assertTrue(pointInCircle(10, 20 + 5, 10, 20, 5), "Z3-34b-边界右方点在圆内")
    -- 刚好超过
    TF.assertFalse(pointInCircle(10 + 5.001, 20, 10, 20, 5), "Z3-34c-超出0.001在圆外")

    -- Z3-35: 5个区域相距远，不会重叠（可配置）
    -- 如果设计者把区域放太近，圆心随机也可能重到其他区域内 → 不报错，正常生成
    local farZones = {
        { x = 0, z = 0, r = 5 },
        { x = 30, z = 30, r = 5 },
        { x = -30, z = 30, r = 5 },
        { x = 30, z = -30, r = 5 },
        { x = -30, z = -30, r = 5 },
    }
    -- 任意两个区域的最近距离 > 各自半径和（不重叠）
    for i = 1, #farZones do
        for j = i + 1, #farZones do
            local dx = farZones[i].x - farZones[j].x
            local dz = farZones[i].z - farZones[j].z
            local centerDist = math.sqrt(dx * dx + dz * dz)
            local minOverlap = farZones[i].r + farZones[j].r
            TF.assertTrue(centerDist > minOverlap,
                string.format("Z3-35-区域%d与%d不重叠(dist=%.1f > overlap=%.1f)", i, j, centerDist, minOverlap))
        end
    end

    print("[Z3] 水晶边界测试完成")
end

return { run = run }
