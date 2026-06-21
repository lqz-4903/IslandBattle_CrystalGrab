-- =============================================
-- Test/TestEnv.lua — 测试环境搭建与销毁
-- =============================================
-- 在场景中创建隔离的测试环境（平面 + 玩家 GameObject）。
-- 所有测试对象挂在 TestEnv 父节点下，测试完一键清理。
-- =============================================

local PlayerEntity = require("Core.PlayerEntity")
local GC = require("Core.GameConst")
local Fix64 = require("Fix64")
local Vec3  = require("Fix64Vector3")

local TestEnv = {}

-- 根节点（所有测试对象挂载其下）
TestEnv.rootGO = nil

-- 测试地面
TestEnv.groundGO = nil

-- 创建的玩家列表（用于批量清理）
TestEnv._createdPlayers = {}

-- ========== 环境搭建 ==========

--- 创建测试根节点 + 地面
--- @return bool — 是否成功
function TestEnv.Setup()
    if TestEnv.rootGO ~= nil then
        TestEnv.Cleanup()
    end

    -- 根节点
    TestEnv.rootGO = CS.UnityEngine.GameObject("__TestEnv__")
    CS.UnityEngine.GameObject.DontDestroyOnLoad(TestEnv.rootGO)
    TestEnv.rootGO.transform.position = CS.UnityEngine.Vector3.zero

    -- 地面（BoxCollider，确保 CharacterController 不会穿透）
    --   ★ Plane 的 MeshCollider 与 CharacterController 配合不好，CC 会穿透
    --   BoxCollider 更可靠：顶部表面在 y=groundY
    --   ★ groundY = CC底部(0.35) - skinWidth(0.08) = 0.27
    --     这样 CC 有效底部恰好在地面表面，isGrounded=true，且不会嵌入
    local groundY = 0.27
    TestEnv.groundGO = CS.UnityEngine.GameObject("__TestGround__")
    TestEnv.groundGO.transform:SetParent(TestEnv.rootGO.transform)
    TestEnv.groundGO.transform.position = CS.UnityEngine.Vector3(0, groundY - 5, 0)
    local boxCol = TestEnv.groundGO:AddComponent(typeof(CS.UnityEngine.BoxCollider))
    boxCol.size = CS.UnityEngine.Vector3(200, 10, 200)

    TestEnv._createdPlayers = {}
    print("[TestEnv] 环境搭建完成")
    return true
end

--- 清理所有测试对象
function TestEnv.Cleanup()
    -- 先销毁玩家
    -- 先清理 pm.players 中的注册
    local PlayerManager = require("Core.PlayerManager")
    local pm = PlayerManager.GetInstance()
    for _, entry in ipairs(TestEnv._createdPlayers) do
        local pid = entry.playerEntity and entry.playerEntity.playerId
        if pid then pm.players[pid] = nil end
        if entry.playerEntity ~= nil then
            entry.playerEntity:Destroy()
        end
    end
    TestEnv._createdPlayers = {}

    -- 销毁根节点（含地面）
    if TestEnv.rootGO ~= nil and not IsNull(TestEnv.rootGO) then
        CS.UnityEngine.GameObject.Destroy(TestEnv.rootGO)
    end
    TestEnv.rootGO = nil
    TestEnv.groundGO = nil
    print("[TestEnv] 环境已清理")
end

-- ========== 预定义出生点 ==========

-- 常用测试出生点（避免所有测试挤在原点）
TestEnv.SPAWN = {
    -- ★ Y=0.35：CC 底端 = transform.y + center.y - height/2 = 0.35+0.9-0.9 = 0.35
    --   地面 y=0.27 = CC底部(0.35) - skinWidth(0.08)，CC 有效底部恰好在地面
    ORIGIN   = CS.UnityEngine.Vector3(0, 0.35, 0),
    FAR      = CS.UnityEngine.Vector3(50, 0.35, 50),
    AIR      = CS.UnityEngine.Vector3(0, 5, 0),           -- 空中（测试跳跃/坠落）
    NEGATIVE = CS.UnityEngine.Vector3(-30, 0.35, -30),
    LARGE    = CS.UnityEngine.Vector3(99999, 0.35, 99999), -- 大值（E3）
}

-- ========== 测试玩家创建 ==========

--- 创建测试用 PlayerEntity（带 GameObject + CharacterController）
--- @param playerId int
--- @param playerName string
--- @param isLocal bool
--- @param spawnPos UnityEngine.Vector3 — 世界坐标出生点（可选，默认 ORIGIN）
--- @param yawDeg number — 初始朝向角度（可选，默认 0）
--- @return table {playerEntity, gameObject, controller}
function TestEnv.CreateTestPlayer(playerId, playerName, isLocal, spawnPos, yawDeg)
    playerId = playerId or 99
    playerName = playerName or "TestPlayer"
    isLocal = isLocal or false
    spawnPos = spawnPos or TestEnv.SPAWN.ORIGIN
    yawDeg = yawDeg or 0

    -- 创建 GameObject
    local go = CS.UnityEngine.GameObject("TestPlayer_" .. playerId)
    go.transform:SetParent(TestEnv.rootGO.transform)
    go.transform.position = spawnPos
    go.transform.rotation = CS.UnityEngine.Quaternion.Euler(0, yawDeg, 0)

    -- 添加 CharacterController
    local cc = go:AddComponent(typeof(CS.UnityEngine.CharacterController))
    cc.height = 1.8
    cc.radius = 0.4
    cc.center = CS.UnityEngine.Vector3(0, 0.9, 0)
    cc.stepOffset = 0.3
    cc.slopeLimit = 45

    -- ★ 地面沉降：Move 足够大的负位移让 PhysX 将 CC 精确放置到地面上
    --   间距 0.05m + skinWidth 0.08m，需要 >0.05m 才能触发地面碰撞
    pcall(function() cc:Move(CS.UnityEngine.Vector3(0, -0.1, 0)) end)

    -- 创建 PlayerEntity
    local player = PlayerEntity.new(playerId, playerName, isLocal)
    player.gameObject = go
    player.transform = go.transform
    player.controller = cc
    player:SetPosition(Fix64.fromFloat(spawnPos.x), Fix64.fromFloat(spawnPos.y), Fix64.fromFloat(spawnPos.z))
    player:SetYaw(Fix64.fromFloat(math.rad(yawDeg)))
    -- ★ 着地状态从 PhysX 实际状态读取，不用静态启发式
    local okGnd, grounded = pcall(function() return cc.isGrounded end)
    player.isGrounded = okGnd and grounded or (spawnPos.y < 1.0)

    -- 记录以便清理
    local entry = { playerEntity = player, gameObject = go, controller = cc }
    table.insert(TestEnv._createdPlayers, entry)

    -- ★ 注册到 PlayerManager.players，让 _ApplyServerPositionCorrection 等能找到
    local PlayerManager = require("Core.PlayerManager")
    local pm = PlayerManager.GetInstance()
    pm.players[playerId] = player

    -- ★ 初始化其插值状态
    if pm._InitInterpState then
        pm:_InitInterpState(player)
    end

    return entry
end

--- 获取第一个测试玩家的 PlayerEntity（便捷函数）
--- @return PlayerEntity
function TestEnv.GetFirstPlayer()
    if #TestEnv._createdPlayers > 0 then
        return TestEnv._createdPlayers[1].playerEntity
    end
    return nil
end

--- 获取 PlayerManager 实例（单例）
function TestEnv.GetPlayerManager()
    local PlayerManager = require("Core.PlayerManager")
    return PlayerManager.GetInstance()
end

--- 给玩家设置输入并执行一个 tick 的确定性移动
--- @param player PlayerEntity
--- @param moveDir int — GC.MOVE_xxx 位掩码
--- @param jump bool
--- @param attack bool
--- @param skill bool
--- @param yawDeg number — 朝向角（度）
function TestEnv.ApplyTickInput(player, moveDir, jump, attack, skill, yawDeg)
    moveDir = moveDir or 0
    jump = jump or false
    attack = attack or false
    skill = skill or false
    yawDeg = yawDeg or 0

    -- 模拟 PlayerInput 数据
    local yawRaw = CS.Fix64.FromFloat(math.rad(yawDeg)).Raw
    local input = {
        PlayerId = player.playerId,
        Tick = 0,
        MoveDir = moveDir,
        Jump = jump,
        Attack = attack,
        Skill = skill,
        CameraYaw = yawRaw,
        ChargeTime = 0,
        ResultPosX = 0,
        ResultPosY = 0,
        ResultPosZ = 0,
    }
    player:ApplyInput(input)
end

--- 执行一个 tick 的确定性移动
--- @param player PlayerEntity
--- @return UnityEngine.Vector3 prevPos, UnityEngine.Vector3 targetPos
function TestEnv.ExecDeterministicMove(player)
    local PlayerManager = require("Core.PlayerManager")
    local pm = PlayerManager.GetInstance()
    local tickDt = Fix64.fromFloat(GC.TICK_INTERVAL)

    -- 确保插值状态已初始化
    if pm._InitInterpState then
        pm:_InitInterpState(player)
    end

    -- 执行确定性移动
    if pm._ApplyDeterministicMovement then
        pm:_ApplyDeterministicMovement(player, tickDt)
    end

    local st = player._interpState
    if st then
        return st.prevPos, st.targetPos
    end
    return nil, nil
end

--- 执行 N 个 tick（连续相同输入）
--- @param player PlayerEntity
--- @param tickCount int
--- @param moveDir int
--- @param jump bool
--- @param yawDeg number
--- @return table — 每 tick 后的 {prevPos, targetPos} 表
function TestEnv.ExecNTicks(player, tickCount, moveDir, jump, yawDeg)
    local results = {}
    for i = 1, tickCount do
        TestEnv.ApplyTickInput(player, moveDir, jump, false, false, yawDeg)
        local prev, target = TestEnv.ExecDeterministicMove(player)
        table.insert(results, { prev = prev, target = target, index = i })
    end
    return results
end

return TestEnv
