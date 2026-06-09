-- =============================================
-- Core/PlayerManager.lua — 玩家生命周期管理器
-- =============================================
-- 【职责】
--   1. 管理所有 PlayerEntity 实例（生成/移除/查询）
--   2. 接收 TickExecutor 帧回调，分发输入到各玩家
--   3. 接收网络事件，同步服务端权威状态（HP/Score/Respawn）
--   4. 区分本地玩家 / 远程玩家
--
-- 【出生点规则】
--   场景中 PlayerSpawnPoint 下有 PS1/PS2/PS3/PS4 四个子对象
--   按房间 PlayerList 的顺序分配：第1人→PS1, 第2人→PS2, 第3人→PS3, 第4人→PS4
--   特殊规则：当房间只有 2 人时，第 2 人生成在 PS3（拉开距离，公平 1v1）
--
-- 【调用链】
--   Main.lua InitGame() → PlayerManager:SpawnAllPlayers(playerList)
--   C# TickExecutor.OnApplyPlayerInput → PlayerManager.ApplyFrameInput
--   C# TickExecutor.OnAfterTickExecuted → PlayerManager.OnFrameEnd
-- =============================================

local PlayerEntity = require("Core.PlayerEntity")
local GC = require("Core.GameConst")
local Fix64 = require("Fix64")
local Vec3  = require("Fix64Vector3")

local PlayerManager = {}
PlayerManager.__index = PlayerManager

-- ========== 状态 ==========

-- 所有玩家：playerId → PlayerEntity
PlayerManager.players = {}

-- 本地玩家 ID（0 表示尚未设置）
PlayerManager.localPlayerId = 0

-- 是否已初始化
PlayerManager.initialized = false

-- 帧执行计数（调试用）
PlayerManager.frameCount = 0

-- 出生点缓存（Unity Transform 引用）
PlayerManager._spawnPoints = {}   -- [1]=PS1, [2]=PS2, [3]=PS3, [4]=PS4

-- ========== 初始化 ==========

--- 初始化并注册 C# 回调桥接
function PlayerManager:Init()
    if self.initialized then return end
    self.initialized = true
    self.players = {}
    self.localPlayerId = 0
    self.frameCount = 0
    self._spawnPoints = {}

    -- 注册到 TickExecutor 的 Lua 回调
    local TickExecutor = CS.TickExecutor

    TickExecutor.OnApplyPlayerInput = function(input)
        self:ApplyFrameInput(input)
    end

    TickExecutor.OnAfterTickExecuted = function(tick)
        self:OnFrameEnd(tick)
    end

    -- ★ 注册 60fps 远程玩家插值（消除 15fps tick 的卡顿）
    if self._interpUpdateId == nil then
        self._interpUpdateId = RegisterUpdate(function(dt)
            self:_InterpolateRemotePlayers(dt)
        end)
    end

    print("[PlayerManager] 初始化完成，已注册 C# 回调")
end

--- 关闭并清理
function PlayerManager:Shutdown()
    for _, player in pairs(self.players) do
        player:Destroy()
    end
    self.players = {}
    self.localPlayerId = 0
    self._spawnPoints = {}
    self.initialized = false

    local TickExecutor = CS.TickExecutor
    TickExecutor.OnApplyPlayerInput = nil
    TickExecutor.OnAfterTickExecuted = nil

    if self._interpUpdateId ~= nil then
        UnregisterUpdate(self._interpUpdateId)
        self._interpUpdateId = nil
    end

    print("[PlayerManager] 已关闭")
end

-- ========== 出生点管理 ==========

--- 从场景中解析出生点（PlayerSpawnPoint → PS1 ~ PS4）
--- @return bool 是否成功找到所有出生点
function PlayerManager:_ResolveSpawnPoints()
    self._spawnPoints = {}

    -- 查找场景中的 PlayerSpawnPoint 父节点
    local spawnRoot = CS.UnityEngine.GameObject.Find("PlayerSpawnPoint")
    if spawnRoot == nil then
        print("[PlayerManager] 错误：场景中找不到 PlayerSpawnPoint")
        return false
    end

    local rootTransform = spawnRoot.transform
    for i = 1, 4 do
        local psName = "PS" .. i
        local ps = rootTransform:Find(psName)
        if ps == nil then
            -- 尝试在子对象中递归查找
            for j = 0, rootTransform.childCount - 1 do
                local child = rootTransform:GetChild(j)
                if child.name == psName then
                    ps = child
                    break
                end
            end
        end
        if ps ~= nil then
            self._spawnPoints[i] = ps
        else
            print("[PlayerManager] 警告：找不到出生点 " .. psName)
        end
    end

    local found = 0
    for _ in pairs(self._spawnPoints) do found = found + 1 end
    print("[PlayerManager] 出生点解析完成，找到 " .. found .. "/4 个")
    return found >= 2  -- 至少需要 2 个才能开始游戏
end

--- 根据玩家序号和总人数获取出生点
--- @param playerIndex int — 玩家在列表中的序号（1-based）
--- @param totalPlayers int — 总玩家数
--- @return UnityEngine.Transform | nil
function PlayerManager:_GetSpawnTransform(playerIndex, totalPlayers)
    -- 特殊规则：2 人时第 2 人用 PS3
    local psIndex = playerIndex
    if totalPlayers == 2 and playerIndex == 2 then
        psIndex = 3
    end

    return self._spawnPoints[psIndex]
end

-- ========== 统一玩家生成 ==========

--- 根据 PlayerList 生成所有玩家（由 Main.lua InitGame 调用）
--- @param playerList GameProto.PlayerList — protobuf 对象，含 Players 列表
--- @param localPlayerId int — 本地玩家的 playerId
function PlayerManager:SpawnAllPlayers(playerList, localPlayerId)
    if playerList == nil or playerList.Players == nil then
        print("[PlayerManager] 错误：PlayerList 为空，无法生成玩家")
        return
    end

    -- 解析出生点
    local hasSpawns = self:_ResolveSpawnPoints()
    if not hasSpawns then
        print("[PlayerManager] 错误：出生点不足，无法生成玩家")
        return
    end

    self.localPlayerId = localPlayerId

    local totalPlayers = playerList.Players.Count
    print("[PlayerManager] 开始生成 " .. totalPlayers .. " 名玩家，本地 ID=" .. localPlayerId)

    for i = 0, totalPlayers - 1 do
        local playerInfo = playerList.Players[i]
        local playerId   = playerInfo.PlayerId
        local playerName = playerInfo.PlayerName
        local isLocal    = (playerId == localPlayerId)
        local index      = i + 1  -- 1-based 序号

        -- 获取对应的出生点 Transform
        local spawnTransform = self:_GetSpawnTransform(index, totalPlayers)
        if spawnTransform == nil then
            print("[PlayerManager] 错误：玩家 " .. playerId .. " 无出生点，跳过")
            goto continue
        end

        -- 创建 Unity GameObject（挂载到出生点下）
        local go = self:_CreatePlayerGameObject(playerId, playerName, isLocal, spawnTransform)
        if go == nil then
            print("[PlayerManager] 错误：创建玩家 GameObject 失败，跳过 " .. playerId)
            goto continue
        end

        -- 从 Transform 读取实际位置和朝向
        local pos = go.transform.position
        local rot = go.transform.rotation
        local spawnPos = Vec3.new(
            Fix64.fromFloat(pos.x),
            Fix64.fromFloat(pos.y),
            Fix64.fromFloat(pos.z)
        )
        local spawnYaw = Fix64.fromFloat(rot.eulerAngles.y * 0.0174533)

        -- 创建 PlayerEntity
        local player = PlayerEntity.new(playerId, playerName, isLocal)
        player.gameObject = go
        player.transform  = go.transform
        -- ★ XLua 中 GetComponent 返回 C# null 时 Lua 侧不是 nil，用 IsNull 检测
        local cc = go:GetComponent(typeof(CS.UnityEngine.CharacterController))
        if IsNull(cc) then
            cc = go:AddComponent(typeof(CS.UnityEngine.CharacterController))
            print("[PlayerManager] CharacterController 已动态添加，玩家 " .. playerId)
        end
        -- 统一设置 CharacterController 参数（无论来自预制体还是动态添加）
        cc.height = 1.8
        cc.radius = 0.4
        cc.center = CS.UnityEngine.Vector3(0, 0.9, 0)
        cc.stepOffset = 0.3     -- 允许自动上小台阶（不触发跳跃动画）
        cc.slopeLimit = 45      -- 45° 斜坡限制
        player.controller = cc
        player:SetPosition(spawnPos.x, spawnPos.y, spawnPos.z)
        player:SetYaw(spawnYaw)

        self.players[playerId] = player

        local tag = isLocal and "[本地]" or "[远程]"
        local psName = ((totalPlayers == 2 and index == 2) and 3 or index)
        print("[PlayerManager] " .. tag .. " 玩家 " .. playerId .. " (" .. playerName ..
              ") 生成于 PS" .. psName .. " 父节点=" .. spawnTransform.name ..
              " pos=(" .. Fix64.toFloat(spawnPos.x) .. "," .. Fix64.toFloat(spawnPos.y) .. "," .. Fix64.toFloat(spawnPos.z) .. ")")

        ::continue::
    end

    print("[PlayerManager] 全部玩家生成完毕，有效玩家 " .. self:GetPlayerCount() .. " 人")
end

--- 创建玩家 Unity GameObject（从 AB 包加载 HeroDefault，挂载到出生点下）
--- ★ ABMgr:LoadRes 本身已实例化，不要再调 Instantiate，否则每个玩家生成两份！
--- @param playerId int
--- @param playerName string
--- @param isLocal bool
--- @param spawnTransform Transform — 出生点（PS1~PS4），GameObject 挂载为其子对象
--- @return UnityEngine.GameObject | nil
function PlayerManager:_CreatePlayerGameObject(playerId, playerName, isLocal, spawnTransform)
    -- ABMgr:LoadRes 加载并实例化，返回的就是场景中的 GameObject
    local go = ABMgr:LoadRes("player", "HeroDefault", typeof(CS.UnityEngine.GameObject))
    if go == nil then
        print("[PlayerManager] 错误：无法加载 player/HeroDefault")
        return nil
    end

    -- 挂载到出生点下，localPosition 归零（世界位置由出生点决定）
    go.transform:SetParent(spawnTransform, false)
    go.transform.localPosition = CS.UnityEngine.Vector3.zero
    go.transform.localRotation = CS.UnityEngine.Quaternion.identity
    go.name = "Player_" .. playerId .. "_" .. playerName

    -- 远程玩家：禁用 CameraPoint/GameCamera 上的摄像机
    if not isLocal then
        local camPoint = go.transform:Find("CameraPoint")
        if camPoint ~= nil then
            local gameCamera = camPoint:Find("GameCamera")
            if gameCamera ~= nil then
                local cam = gameCamera:GetComponent(typeof(CS.UnityEngine.Camera))
                if cam ~= nil then cam.enabled = false end
                local listener = gameCamera:GetComponent(typeof(CS.UnityEngine.AudioListener))
                if listener ~= nil then listener.enabled = false end
            end
        end
    end

    return go
end

--- 降级：无 PlayerList 时仅生成本地玩家
function PlayerManager:SpawnLocalOnly(playerId, playerName)
    self.localPlayerId = playerId

    if not self:_ResolveSpawnPoints() then
        print("[PlayerManager] 出生点解析失败，降级使用原点")
    end

    -- 本地玩家始终用 PS1
    local spawnTransform = self._spawnPoints[1]
    if spawnTransform == nil then
        print("[PlayerManager] 错误：无可用出生点")
        return nil
    end

    local go = self:_CreatePlayerGameObject(playerId, playerName, true, spawnTransform)
    if go == nil then return nil end

    local pos = go.transform.position
    local rot = go.transform.rotation

    local player = PlayerEntity.new(playerId, playerName, true)
    player.gameObject = go
    player.transform  = go.transform
    local cc = go:GetComponent(typeof(CS.UnityEngine.CharacterController))
    if IsNull(cc) then
        cc = go:AddComponent(typeof(CS.UnityEngine.CharacterController))
        cc.height = 1.8
        cc.radius = 0.4
        cc.center = CS.UnityEngine.Vector3(0, 0.9, 0)
    end
    player.controller = cc
    player:SetPosition(Fix64.fromFloat(pos.x), Fix64.fromFloat(pos.y), Fix64.fromFloat(pos.z))
    player:SetYaw(Fix64.fromFloat(rot.eulerAngles.y * 0.0174533))
    self.players[playerId] = player

    print("[PlayerManager] 降级模式：仅生成本地玩家 " .. playerId .. " (" .. playerName .. ")")
    return player
end

-- ========== 查询 ==========

function PlayerManager:GetPlayer(playerId)
    return self.players[playerId]
end

function PlayerManager:GetLocalPlayer()
    return self.players[self.localPlayerId]
end

function PlayerManager:GetAlivePlayers()
    local alive = {}
    for id, player in pairs(self.players) do
        if player.isAlive then
            alive[id] = player
        end
    end
    return alive
end

function PlayerManager:GetPlayerCount()
    local count = 0
    for _ in pairs(self.players) do count = count + 1 end
    return count
end

-- ========== 移除玩家 ==========

function PlayerManager:RemovePlayer(playerId)
    local player = self.players[playerId]
    if player == nil then return end

    print("[PlayerManager] 移除玩家 " .. playerId .. " (" .. (player.playerName or "?") .. ")")
    player:Destroy()
    self.players[playerId] = nil

    if self.localPlayerId == playerId then
        self.localPlayerId = 0
    end
end

-- ========== 帧输入处理 ==========

function PlayerManager:ApplyFrameInput(input)
    local playerId = input.PlayerId
    local player = self.players[playerId]

    if player == nil then return end
    if not player.isAlive then return end

    player:ApplyInput(input)
end

function PlayerManager:OnFrameEnd(tick)
    self.frameCount = self.frameCount + 1

    -- ★ 对远程玩家执行确定性移动（设置目标位置，由 60fps 插值平滑驱动）
    local tickDt = Fix64.fromFloat(GC.TICK_INTERVAL)
    for _, player in pairs(self.players) do
        if player.playerId ~= self.localPlayerId then
            if player.isAlive and player.controller ~= nil and not IsNull(player.controller) then
                self:_ApplyDeterministicMovement(player, tickDt)
            end
        end
    end

    if self.frameCount % 75 == 0 then
        print("[PlayerManager] 帧 " .. tick.Tick .. " 已执行，活跃玩家 " .. self:GetPlayerCount())
    end
end

-- 远程玩家插值更新 ID（在 Init 中注册，Shutdown 中注销）
PlayerManager._interpUpdateId = nil

--- 每帧插值远程玩家（60fps 平滑），消除 15fps tick 的卡顿感
function PlayerManager:_InterpolateRemotePlayers(dt)
    for _, player in pairs(self.players) do
        if player.playerId ~= self.localPlayerId and player.isAlive then
            if player._targetPos ~= nil and player.transform ~= nil then
                -- 位置插值：每帧向目标位置靠近
                local current = player.transform.position
                local target = player._targetPos
                local t = math.min(dt * 15, 1)  -- 插值因子，约 0.25/帧
                local newPos = CS.UnityEngine.Vector3.Lerp(current, target, t)
                player.transform.position = newPos
            end
            -- 每帧更新远程玩家动画（60fps 平滑）
            self:_UpdateRemoteAnimator(player, dt)
        end
    end
end

--- 确定性移动 + 动画同步（用于远程玩家）
function PlayerManager:_ApplyDeterministicMovement(player, dt)
    local controller = player.controller
    local dtFloat = Fix64.toFloat(dt)
    local moveDir = player.moveDir
    local yawFloat = Fix64.toFloat(player.yaw)

    -- 水平移动
    local hVelocity = CS.UnityEngine.Vector3.zero
    local hSpeed = 0
    if moveDir ~= GC.MOVE_NONE then
        local forward = CS.UnityEngine.Vector3(math.sin(yawFloat), 0, math.cos(yawFloat))
        local right   = CS.UnityEngine.Vector3(math.cos(yawFloat), 0, -math.sin(yawFloat))
        local dir = CS.UnityEngine.Vector3.zero
        if moveDir & GC.MOVE_FORWARD ~= 0 then dir = dir + forward end
        if moveDir & GC.MOVE_BACKWARD ~= 0 then dir = dir - forward end
        if moveDir & GC.MOVE_RIGHT ~= 0 then dir = dir + right end
        if moveDir & GC.MOVE_LEFT ~= 0 then dir = dir - right end
        if dir.magnitude > 1 then dir = dir.normalized end
        hVelocity = dir * GC.MOVE_SPEED
        hSpeed = GC.MOVE_SPEED
    end

    -- 垂直移动
    local vertVelocity
    if player.isGrounded then
        if player.isJumping then
            vertVelocity = GC.JUMP_FORCE
            player.isGrounded = false
        else
            vertVelocity = -GC.GRAVITY * 0.5
        end
    else
        vertVelocity = Fix64.toFloat(player.velocity.y) - GC.GRAVITY * dtFloat
    end

    -- 计算位移（用 CharacterController.Move 处理碰撞）
    local displacement = CS.UnityEngine.Vector3(
        hVelocity.x * dtFloat,
        vertVelocity * dtFloat,
        hVelocity.z * dtFloat
    )
    pcall(function() controller:Move(displacement) end)

    -- 更新着地
    local ok, grounded = pcall(function() return controller.isGrounded end)
    if ok then player.isGrounded = grounded end

    -- 存储速度（供动画用）
    player.velocity = Vec3.new(Fix64.fromFloat(hVelocity.x), Fix64.fromFloat(vertVelocity), Fix64.fromFloat(hVelocity.z))
    player._hSpeed = hSpeed

    -- ★ 存储目标位置（供 60fps 插值平滑），同时更新旋转
    player._targetPos = player.transform.position
    player.transform.rotation = CS.UnityEngine.Quaternion.Euler(0, math.deg(yawFloat), 0)
end

--- 更新远程玩家的动画参数（每帧 60fps 调用，由 _InterpolateRemotePlayers 驱动）
function PlayerManager:_UpdateRemoteAnimator(player, dt)
    if player._animatorCached == nil and player.gameObject ~= nil and not IsNull(player.gameObject) then
        player._animatorCached = player.gameObject:GetComponentInChildren(typeof(CS.UnityEngine.Animator))
    end
    local anim = player._animatorCached
    if anim == nil or IsNull(anim) then
        player._animatorCached = nil
        return
    end

    local hSpeed = (player._hSpeed or 0) / GC.MOVE_SPEED
    local vSpeed = 0
    if not player.isGrounded then
        vSpeed = Fix64.toFloat(player.velocity.y)
    end

    anim:SetFloat("HSpeed", hSpeed)
    anim:SetFloat("VSpeed", vSpeed)
    anim:SetBool("Fire", player.isAttacking)
    anim:SetBool("Skill", player.isUsingSkill)
    anim:SetBool("Jump", not player.isGrounded)
    anim:SetBool("Roll", player._isRolling or false)
end

-- ========== 服务端权威状态同步 ==========

function PlayerManager:OnServerPlayerHit(attackerId, victimId, droppedCount, newHp)
    local victim = self.players[victimId]
    if victim == nil then return end
    victim:TakeDamage(newHp or (victim.hp - 1))
    print("[PlayerManager] 玩家 " .. victimId .. " 受击 HP=" .. victim.hp)
end

function PlayerManager:OnServerPlayerFall(playerId, droppedCount, newHp)
    local player = self.players[playerId]
    if player == nil then return end
    player:TakeDamage(newHp or (player.hp - 1))
    print("[PlayerManager] 玩家 " .. playerId .. " 坠落 HP=" .. player.hp)
end

function PlayerManager:OnServerPlayerRespawn(playerId, posX, posY, posZ, hp)
    local player = self.players[playerId]
    if player == nil then return end
    player:Respawn(hp)
    -- posX/Y/Z 现在是 sfixed64 → long = Fix64.Raw，用 Fix64.new() 而非 fromFloat()
    player:SetPosition(
        Fix64.new(posX),
        Fix64.new(posY),
        Fix64.new(posZ)
    )
end

function PlayerManager:OnServerCrystalPickup(playerId, crystalId, newScore)
    local player = self.players[playerId]
    if player == nil then return end
    player:SetScore(newScore)
    print("[PlayerManager] 玩家 " .. playerId .. " 拾取水晶 " .. crystalId .. " 分数=" .. newScore)
end

-- ========== 单例 ==========

local instance = nil

function PlayerManager.GetInstance()
    if instance == nil then
        instance = setmetatable({}, PlayerManager)
        instance.players = {}
    end
    return instance
end

return PlayerManager
