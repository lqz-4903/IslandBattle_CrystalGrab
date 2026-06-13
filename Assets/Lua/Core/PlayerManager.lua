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
local Arrow = require("Battle.Arrow")

-- ★ GC优化：预缓存常量 Fix64 值，避免每 tick Fix64.fromFloat(GC.TICK_INTERVAL) 创建新表
local TICK_DT_F64 = Fix64.fromFloat(GC.TICK_INTERVAL)

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

    -- ★ 注册 LateUpdate 渲染层（在所有 Update/TickExecutor 物理回退之后执行）
    --   确保插值读到正确的 prevPos/targetPos，不被物理回退覆盖
    if self._interpUpdateId == nil then
        self._interpUpdateId = RegisterLateUpdate(function(dt)
            self:_InterpolateAllPlayers(dt)
        end)
    end

    -- ★ 箭矢系统初始化（对象池预加载 + 注册 Update）
    Arrow.Init()

    -- ★ Phase 2 预留：绑定箭矢命中玩家回调
    Arrow.onHitPlayer = function(arrow, targetPlayerId)
        local target = self.players[targetPlayerId]
        if target and target.isAlive then
            local hitPoint = arrow.go.transform.position
            target:OnHitByArrow(arrow.ownerId, hitPoint)
        end
    end

    print("[PlayerManager] 初始化完成，已注册 C# 回调 + 箭矢系统")
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
        UnregisterLateUpdate(self._interpUpdateId)
        self._interpUpdateId = nil
    end

    -- ★ 清理箭矢系统
    Arrow.ClearAll()

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
        -- ★ 修复 Bug1（飞天）：先设置 yaw 再设置 position
        --   SetPosition → SyncTransform 内部读取 self.yaw 计算旋转，必须先初始化 yaw。
        player:SetYaw(spawnYaw)
        player:SetPosition(spawnPos.x, spawnPos.y, spawnPos.z)
        -- ★ 修复 Bug1（飞天）：读取 CharacterController 实际着地状态
        --   PlayerEntity.new 默认 isGrounded=true，但 CC 可能实际未着地（生成点高于地形）。
        --   不校正会导致首 tick 物理以错误着地状态执行：着地→垂直速度=0→不下降→浮空。
        if not IsNull(cc) then
            player.isGrounded = cc.isGrounded
        end

        -- ★ 通知服务端出生点（死亡重生用）
        local hostServer = CS.HostServer.Instance
        if hostServer ~= nil then
            hostServer:SetPlayerBirthPos(playerId, pos.x, pos.y, pos.z)
        end

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
        cc.stepOffset = 0.3
        cc.slopeLimit = 45
    end
    player.controller = cc
    -- ★ 修复 Bug1：先设置 yaw 再设置 position（SyncTransform 依赖 yaw）
    player:SetYaw(Fix64.fromFloat(rot.eulerAngles.y * 0.0174533))
    player:SetPosition(Fix64.fromFloat(pos.x), Fix64.fromFloat(pos.y), Fix64.fromFloat(pos.z))
    -- ★ 修复 Bug1：同步 CC 实际着地状态
    if not IsNull(cc) then
        player.isGrounded = cc.isGrounded
    end
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

    -- ★ 提取服务端权威位置（所有玩家 —— 含本地玩家的自校正）
    --   ResultPosX/Y/Z 由主机 TickSyncHandler.FinalizeTick 填入上一 tick 的物理终点
    --   客户端在下一帧 _ApplyServerPositionCorrection 中消费，校正漂移后丢弃
    --   ★ 本地玩家也需要校正：60fps _ApplyLocalMovement 的 PhysX 子步与主机 30fps
    --     确定性物理的 8 子步结果不同，累积漂移需服务端权威兜底
    local rpx = input.ResultPosX
    local rpy = input.ResultPosY
    local rpz = input.ResultPosZ
    -- ★ GC优化：复用 _serverAuthPos table，原地更新 raw 值
    --   避免每 tick 创建 {x=Fix64.new, y=Fix64.new, z=Fix64.new} (4 tables)
    if rpx ~= 0 or rpy ~= 0 or rpz ~= 0 then
        if player._serverAuthPos == nil then
            player._serverAuthPos = { x = Fix64.new(0), y = Fix64.new(0), z = Fix64.new(0) }
        end
        player._serverAuthPos.x.raw = rpx
        player._serverAuthPos.y.raw = rpy
        player._serverAuthPos.z.raw = rpz
        player._hasServerAuthPos = true   -- 标记有新的权威位置待消费
    end

    -- ★ 客户端预测-校正：记录服务端回传的客户端 tick 编号
    --   0 = 服务端超时生成的空输入（stub），>0 = 客户端实际 tick
    --   用于判断哪些输入已被服务端物理消费，哪些需要重放
    if player.playerId == self.localPlayerId then
        player._serverEchoedTick = input.Tick
        -- ★ 每 tick 清理已消费的输入（无论是否触发校正），避免缓冲区无限增长
        --   且防止 reconciliation 重放已被服务端执行的 tick 导致双倍位移
        if input.Tick > 0 then
            local PC = require("Battle.PlayerController")
            PC:AcknowledgeUpTo(input.Tick)
        end
    end
end

function PlayerManager:OnFrameEnd(tick)
    self.frameCount = self.frameCount + 1

    -- ★ 诊断：前 5 帧 + 每 30 帧输出，确认 OnFrameEnd 被执行
    if self.frameCount <= 5 or self.frameCount % 30 == 0 then
        local hostTag = (CS.HostServer.Instance ~= nil and CS.HostServer.Instance.IsGameStarted) and "HOST" or "CLIENT"
        print(string.format("[PM] OnFrameEnd #%d tick=%d %s players=%d",
            self.frameCount, tick.Tick, hostTag, self:GetPlayerCount()))
    end

    local isHost = (CS.HostServer.Instance ~= nil and CS.HostServer.Instance.IsGameStarted)

    -- ★ 客户端：先应用服务端权威位置校正，再执行物理
    --   校正 → 物理从正确位置出发 → 位移准确 → bug 2/3 一并解决
    if not isHost then
        self:_ApplyServerPositionCorrection(tick.Tick)
    end

    -- ★ 对所有玩家执行确定性移动（统一 30fps 物理）
    --   本地玩家 + 远程玩家：均由 _ApplyDeterministicMovement 驱动
    --   渲染由 _InterpolateAllPlayers 60fps 插值平滑
    -- ★ GC优化：使用预缓存的 TICK_DT_F64，避免每 tick Fix64.fromFloat 创建新表
    for _, player in pairs(self.players) do
        if not player.isAlive then goto continue_physics end
        if player.controller == nil or IsNull(player.controller) then goto continue_physics end

        self:_ApplyDeterministicMovement(player, TICK_DT_F64, isHost)
        ::continue_physics::
    end

    -- 主机端 — 捕获所有玩家的物理结果位置，供下一 tick 附带发送
    -- ★ 所有玩家统一从 _interpState.targetPos 读取（不再区分本地/远程）
    if isHost then
        self:_CaptureAuthPositions()
    end

    if self.frameCount % 75 == 0 then
        print("[PlayerManager] 帧 " .. tick.Tick .. " 已执行，活跃玩家 " .. self:GetPlayerCount())
    end
end

-- 远程玩家插值更新 ID（在 Init 中注册，Shutdown 中注销）
PlayerManager._interpUpdateId = nil

-- =============================================
-- 主机端权威位置捕获
-- =============================================
PlayerManager._capDebugCounter = 0   -- 主机端：捕获日志计数

--- 主机端：捕获所有玩家在 tick 执行后的物理位置。
--- 位置经 Fix64.Raw 转换后提交给 C# TickSyncHandler，附加到下一 tick 的 InputTick 中。
--- ★ 统一路径：所有玩家（含主机本地）从 _interpState.targetPos 读取（物理终点）。
---   transform.position 已被回退到 prevPos 供插值器渲染，不能直接读取。
function PlayerManager:_CaptureAuthPositions()
    local hostServer = CS.HostServer.Instance
    if hostServer == nil then return end

    local captureCount = 0
    for _, player in pairs(self.players) do
        local posX, posY, posZ

        -- ★ 所有玩家统一从 _interpState.targetPos 读取（同一条物理路径产出）
        if player._interpState ~= nil
            and player._interpState.targetPos ~= nil then
            posX = player._interpState.targetPos.x
            posY = player._interpState.targetPos.y
            posZ = player._interpState.targetPos.z
        elseif player.transform ~= nil and not IsNull(player.transform) then
            -- 降级：无插值状态时读 Transform 位置
            local pos = player.transform.position
            posX = pos.x
            posY = pos.y
            posZ = pos.z
        else
            goto continue_cap
        end

        local xRaw = CS.Fix64.FromFloat(posX).Raw
        local yRaw = CS.Fix64.FromFloat(posY).Raw
        local zRaw = CS.Fix64.FromFloat(posZ).Raw
        hostServer:SubmitAuthPosition(player.playerId, xRaw, yRaw, zRaw)
        -- ★ 同时报告位置给 GameEventHandler（死亡掉落定位用）
        hostServer:ReportPlayerPosition(player.playerId, posX, posY, posZ)
        captureCount = captureCount + 1

        ::continue_cap::
    end

    -- 调试：每秒（75 tick）打印一次捕获统计
    self._capDebugCounter = self._capDebugCounter + 1
    if self._capDebugCounter % 75 == 0 and captureCount > 0 then
        -- 打印第一个玩家的位置作为样本
        local samplePlayer = nil
        for _, p in pairs(self.players) do samplePlayer = p; break end
        if samplePlayer ~= nil and samplePlayer.transform ~= nil then
            local sp = samplePlayer.transform.position
            print(string.format("[PlayerManager] 捕获#%d 玩家数=%d 样本[玩家%d]=(%.2f,%.2f,%.2f)",
                self._capDebugCounter, captureCount,
                samplePlayer.playerId, sp.x, sp.y, sp.z))
        end
    end
end

-- =============================================
-- 客户端权威位置校正（硬回滚）
-- =============================================
--- ★ 在 _ApplyDeterministicMovement 之前调用，确保物理从正确位置出发。
---
--- 两种校正路径：
---   A) 远程玩家（有 _interpState）：校正 st.targetPos/st.prevPos，
---      后续 _ApplyDeterministicMovement 从校正位置出发执行物理。
---   B) 客户端本地玩家（无 _interpState，60fps 预测驱动）：
---      直接校正 transform.position，下一帧 _ApplyLocalMovement 从校正位置继续。
---
--- @param tick int — 当前 tick
function PlayerManager:_ApplyServerPositionCorrection(tick)
    -- 调用方已确保 not isHost，此处保留用于路径 B 的防护判断
    local isHost = false
    for _, player in pairs(self.players) do
        -- ★ GC优化：用标志位代替 nil，避免每次校正后重新分配 _serverAuthPos 表
        if not player._hasServerAuthPos then goto continue_corr end
        player._hasServerAuthPos = false

        local authPos = player._serverAuthPos

        local serverPos = CS.UnityEngine.Vector3(
            Fix64.toFloat(authPos.x),
            Fix64.toFloat(authPos.y),
            Fix64.toFloat(authPos.z)
        )

        local st = player._interpState

        if st ~= nil and st.targetPos ~= nil then
            -- ==== 路径 A：远程玩家（有插值状态）====
            -- 比较客户端的 tick N-1 物理结果（存于 targetPos）与服务端权威位置
            local drift = (st.targetPos - serverPos).magnitude
            local CORRECTION_THRESHOLD = 0.01  -- 1cm

            if drift > CORRECTION_THRESHOLD then
                st.targetPos = serverPos
                st.prevPos   = serverPos   -- 链条一致：下个 tick 的 prevPos 也正确
                st.elapsed   = 0           -- 重置插值计时器

                if player.transform ~= nil then
                    player.transform.position = serverPos
                end

                if tick % 75 == 0 then
                    print(string.format(
                        "[PlayerManager] 校正[远程] 玩家%d drift=%.3fm → 已同步到服务端位置",
                        player.playerId, drift))
                end
            end

        elseif player.transform ~= nil and not isHost then
            -- ==== 路径 B：客户端本地玩家 — 回滚 + 输入重放（Reconciliation）====
            -- 1. 确认服务端已消费的客户端输入
            -- 2. 回滚到服务端权威位置
            -- 3. 重放未确认的输入，从权威位置出发重新预测
            local curPos = player.transform.position
            local drift = (curPos - serverPos).magnitude
            local threshold = GC.RECONCILE_THRESHOLD or 0.02

            if drift > threshold then
                -- ★ 已消费的 tick 已在 ApplyFrameInput 中通过 AcknowledgeUpTo 清理。
                --   此处只需回滚 + 重放未确认的未来 tick（> echoTick），
                --   _ApplyDeterministicMovement 会应用当前 tick 的输入（= echoTick）。
                local echoTick = player._serverEchoedTick or 0
                local PC = require("Battle.PlayerController")

                -- ★ 回滚到服务端权威位置
                player.transform.position = serverPos
                -- ★ GC优化：原地更新 position，避免 new Vec3 + 3 Fix64.fromFloat
                if player.position == Vec3.ZERO then
                    player.position = Vec3.zero()
                end
                player.position.x.raw = CS.Fix64.FromFloat(serverPos.x).Raw
                player.position.y.raw = CS.Fix64.FromFloat(serverPos.y).Raw
                player.position.z.raw = CS.Fix64.FromFloat(serverPos.z).Raw

                -- ★ 回滚后同步着地状态（防止第一帧重放用错 isGrounded）
                if player.controller ~= nil and not IsNull(player.controller) then
                    player.isGrounded = player.controller.isGrounded
                end

                -- ★ 重放未确认的输入，从服务端位置出发重新预测
                self:_ReconcileReplayLocalInputs(player, PC)

                if tick % 75 == 0 then
                    -- ★ GC优化：用 GetUnackedCount 代替 #GetUnackedInputs()，避免仅为了调试日志创建完整 table
                    local unackedCount = PC:GetUnackedCount()
                    print(string.format(
                        "[PlayerManager] 校正[本地] 玩家%d drift=%.3fm → 回滚+重放%d帧 (echoTick=%d)",
                        player.playerId, drift, unackedCount, echoTick))
                end
            end
        end

        ::continue_corr::
    end
end

-- =============================================
-- 客户端本地玩家预测-校正：输入重放
-- =============================================
--- 从服务端权威位置出发，重放所有未确认的输入，重新预测当前位置。
--- 原理：服务端权威位置 = "正确的过去"，未确认输入 = "客户端已发送但服务端尚未处理的未来"，
---       重放这些输入 = 从正确的过去推导出最准确的当前预测位置。
--- @param player PlayerEntity — 本地玩家
--- @param pc table — PlayerController 模块引用
function PlayerManager:_ReconcileReplayLocalInputs(player, pc)
    local unackedInputs = pc:GetUnackedInputs()
    if #unackedInputs == 0 then return end

    -- ★ 保存当前 tick 的输入（由 ApplyFrameInput 设置），重放结束后恢复，
    --    避免 _ApplyDeterministicMovement 使用重放最后一帧的旧输入导致方向错误。
    local savedMoveDir = player.moveDir
    local savedYawRaw = player.yaw.raw
    local savedJumping = player.isJumping

    -- ★ GC优化：使用预缓存的 TICK_DT_F64

    -- 逐帧重放未确认的输入
    for _, entry in ipairs(unackedInputs) do
        local data = entry.data
        player.moveDir   = data.moveDir
        -- ★ GC优化：原地更新 raw 值，避免每重放帧 Fix64.fromFloat 创建新表
        player.yaw.raw = CS.Fix64.FromFloat(data.yaw).Raw
        player.isJumping = data.jump or false

        self:_ReplayDeterministicStep(player, TICK_DT_F64)
    end

    -- ★ 恢复当前 tick 的输入，供后续 _ApplyDeterministicMovement 使用
    player.moveDir = savedMoveDir
    player.yaw.raw = savedYawRaw
    player.isJumping = savedJumping

    -- 同步最终位置到 Lua position 记录
    if player.transform ~= nil and not IsNull(player.transform) then
        local pos = player.transform.position
        -- ★ GC优化：原地更新 position
        if player.position == Vec3.ZERO then
            player.position = Vec3.zero()
        end
        player.position.x.raw = CS.Fix64.FromFloat(pos.x).Raw
        player.position.y.raw = CS.Fix64.FromFloat(pos.y).Raw
        player.position.z.raw = CS.Fix64.FromFloat(pos.z).Raw

        -- ★ 修复：确保 _interpState 已初始化，防止本地玩家校正后插值状态缺失
        self:_InitInterpState(player)
        player._interpState.targetPos = player.transform.position
        player._interpState.prevPos   = player.transform.position
        player._interpState.elapsed   = 0
    end
end

--- 回滚重放专用的单帧确定性物理。
--- 与 _ApplyDeterministicMovement 相同的物理逻辑，但不管理插值状态、不回退 Transform ——
--- 因为本地玩家由 60fps _ApplyLocalMovement 驱动渲染。
--- @param player PlayerEntity
--- @param dt Fix64 — tick 时间间隔
function PlayerManager:_ReplayDeterministicStep(player, dt)
    local controller = player.controller
    if controller == nil or IsNull(controller) then return end

    local dtFloat = Fix64.toFloat(dt)
    local moveDir = player.moveDir
    -- ★ 防御：player.yaw 改为 nil 初值，需容错
    local yawFloat = player.yaw and Fix64.toFloat(player.yaw) or 0

    -- 水平移动
    local hVelocity = CS.UnityEngine.Vector3.zero
    local dirMask = moveDir & 0x0F
    local isRolling = (moveDir & GC.MOVE_ROLL) ~= 0

    if dirMask ~= GC.MOVE_NONE then
        local forward = CS.UnityEngine.Vector3(math.sin(yawFloat), 0, math.cos(yawFloat))
        local right   = CS.UnityEngine.Vector3(math.cos(yawFloat), 0, -math.sin(yawFloat))
        local dir = CS.UnityEngine.Vector3.zero
        if dirMask & GC.MOVE_FORWARD ~= 0 then dir = dir + forward end
        if dirMask & GC.MOVE_BACKWARD ~= 0 then dir = dir - forward end
        if dirMask & GC.MOVE_RIGHT ~= 0 then dir = dir + right end
        if dirMask & GC.MOVE_LEFT ~= 0 then dir = dir - right end
        if dir.magnitude > 1 then dir = dir.normalized end
        local speed = isRolling and 12 or GC.MOVE_SPEED
        hVelocity = dir * speed
    end

    -- 垂直移动
    local vertVelocity
    local justJumped = false
    if player.isGrounded then
        if player.isJumping then
            vertVelocity = GC.JUMP_FORCE
            player.isGrounded = false
            justJumped = true
            player._jumpInitiated = true
        else
            vertVelocity = 0
        end
    else
        vertVelocity = Fix64.toFloat(player.velocity.y) - GC.GRAVITY * dtFloat
    end

    -- 子步物理
    local subSteps = GC.PHYSICS_SUBSTEPS or 4
    local subDt = dtFloat / subSteps
    local subDisp = CS.UnityEngine.Vector3(
        hVelocity.x * subDt,
        vertVelocity * subDt,
        hVelocity.z * subDt
    )
    for _ = 1, subSteps do
        local ok = pcall(controller.Move, controller, subDisp)
        if not ok then break end
    end

    -- 更新着地状态（刚起跳时不检测，防止首帧取消跳跃）
    if not justJumped then
        if not IsNull(controller) then
            player.isGrounded = controller.isGrounded
        end
    end

    -- 更新速度
    -- ★ GC优化：原地更新 velocity
    if player.velocity == Vec3.ZERO then
        player.velocity = Vec3.zero()
    end
    player.velocity.x.raw = CS.Fix64.FromFloat(hVelocity.x).Raw
    player.velocity.y.raw = CS.Fix64.FromFloat(vertVelocity).Raw
    player.velocity.z.raw = CS.Fix64.FromFloat(hVelocity.z).Raw
end

-- =============================================
-- 远程玩家插值系统（消除 30fps tick → 60fps 渲染的卡顿）
-- =============================================
-- 【插值原理】
--   _ApplyDeterministicMovement 每 tick（1/15s）执行物理移动
--   → 保存 prevPos（上一 tick 物理终点）→ targetPos（本 tick 物理终点）
--   → 将 Transform 回退到 prevPos
--   _InterpolateRemotePlayers 每帧（1/60s）驱动渲染
--   → elapsed 累积 dt，t = elapsed / interval 归一化到 [0,1]
--   → smoothstep(t) = t²(3-2t)，起止点导数为 0，消除方向突变抖动
--   → 插值结束后用速度外推（最多 2 tick），避免 tick 延迟时僵住
--   → 旋转用 Quaternion.Slerp / RotateTowards（恒定角速度）
--   → 动画 float 参数做帧间平滑
-- =============================================

--- 初始化远程玩家的插值状态（首次调用自动创建）
function PlayerManager:_InitInterpState(player)
    if player._interpState == nil then
        player._interpState = {
            prevPos     = nil,    -- tick N-1 物理终点（插值起点）
            targetPos   = nil,    -- tick N 物理终点（插值终点）
            prevYaw     = nil,    -- tick N-1 的 yaw 角度（度）
            targetYaw   = nil,    -- tick N 的 yaw 角度（度）
            prevVelocity = nil,   -- tick N-1 的水平速度向量（用于外推）
            elapsed     = 0,      -- 当前插值已累积的时间（秒）
            interval    = GC.TICK_INTERVAL,  -- 每 tick 时间间隔
            hasTarget   = false,  -- 是否已收到至少一个完整 tick
            -- 动画参数平滑（当前显示值）
            displayHSpeed = 0,
            displayVSpeed = 0,
        }
    end
end

--- 每帧插值所有玩家位置（60fps 平滑），消除 30fps tick → 60fps 渲染的卡顿感
--- ★ 统一 30fps 物理后，本地玩家也走此插值路径（与远程玩家完全一致）
---   本地玩家旋转由 PlayerController._UpdateCamera 直接控制，插值系统跳过旋转
--- @param dt number — Unity Time.deltaTime（秒）
function PlayerManager:_InterpolateAllPlayers(dt)
    for _, player in pairs(self.players) do
        if player.isAlive then
            self:_InitInterpState(player)
            local st = player._interpState

            if st.hasTarget and st.prevPos ~= nil and st.targetPos ~= nil and player.transform ~= nil then
                -- ==== 1. 累积插值时间 ====
                st.elapsed = st.elapsed + dt

                -- ★ 本地玩家：跳过位置插值 — transform 已由 _ApplyDeterministicMovement
                --   保持在 targetPos（最新物理位置），无滞后，摄像机响应跟手
                if player.playerId ~= self.localPlayerId then
                    -- ==== 2. 时间归一化插值因子（钳制到 [0,1]，取消外推）====
                    --     外推会在 tick 迟到时产生位置偏移，下一 tick 到达时
                    --     因 elapsed 重置而跳回 prevPos，造成画面抖动。
                    --     钳制后 tick 短暂迟到时停在 targetPos，无抖动。
                    local t = math.min(st.elapsed / st.interval, 1.0)

                    -- ==== 3. smoothstep 缓动 ====
                    --      t²(3-2t) 在 t=0 和 t=1 处导数为 0，消除方向突变
                    local tSmooth = t * t * (3 - 2 * t)
                    local newPos = CS.UnityEngine.Vector3.Lerp(st.prevPos, st.targetPos, tSmooth)
                    player.transform.position = newPos
                end

                -- ==== 4. 旋转插值：恒定角速度 RotateTowards ====
                --      本地玩家旋转由摄像机直接控制（60fps 即时响应），跳过插值
                if st.prevYaw ~= nil and st.targetYaw ~= nil then
                    if player.playerId ~= self.localPlayerId then
                        local maxDegrees = GC.INTERP_ROT_SPEED * dt
                        local curRot = player.transform.rotation
                        local targetRot = CS.UnityEngine.Quaternion.Euler(0, st.targetYaw, 0)
                        player.transform.rotation = CS.UnityEngine.Quaternion.RotateTowards(
                            curRot, targetRot, maxDegrees
                        )
                    end
                end
            end

            -- ==== 5. 每帧更新动画（含参数平滑）====
            self:_UpdateRemoteAnimator(player, dt)
        end
    end
end

--- 确定性移动 + 动画同步（本地+远程玩家统一入口）
--- ★ 插值状态管理：
---   1. 上一 tick 的 targetPos → 本 tick 的 prevPos（保证位置链连续）
---   2. controller:Move() 执行后保存新位置 → 本 tick 的 targetPos
---   3. 远程玩家：Transform 回退到 prevPos（渲染滞后 1 tick，由插值器 60fps 驱动前进）
---      本地玩家：保持在 targetPos（最新物理位置），摄像机跟随即时位置，无滞后
---   4. 保存 yaw / velocity 供旋转插值使用
function PlayerManager:_ApplyDeterministicMovement(player, dt, isHost)
    local controller = player.controller
    local dtFloat = Fix64.toFloat(dt)
    local moveDir = player.moveDir
    -- ★ 防御：player.yaw 已改为 nil 初值，正常流程中 SetYaw 或 ApplyInput 会先设置。
    --   若因时序异常（tick 先于 spawn 到达）仍未初始化，退化为 0（朝 +Z 轴）。
    local yawFloat = player.yaw and Fix64.toFloat(player.yaw) or 0
    local yawDeg = math.deg(yawFloat)   -- ±180°

    -- ==== 插值状态初始化 ====
    self:_InitInterpState(player)
    local st = player._interpState

    -- ==== 保存插值起点 ====
    -- ★ 关键修复：使用上一 tick 的物理终点作为新起点，而非当前渲染位置
    --   这样插值链条保持连续，不会在 tick 边界产生视觉跳跃
    if st.targetPos ~= nil then
        -- ★ 主机端：始终使用正常链式传递（st.prevPos = st.targetPos）。
        --   主机端 tick 以稳定 30fps 生成，不存在追赶模式多 tick 场景，
        --   P1 软着陆不需要，且没有权威校正兜底，软着陆断链会累积位置偏差。
        -- ★ 客户端：软着陆保护（同帧双 tick / 追赶模式时从渲染位置起插，避免画面跳跃）。
        if not isHost and player.transform ~= nil then
            local renderPos = player.transform.position
            local drift = (renderPos - st.targetPos).magnitude
            if drift > 0.005 then
                st.prevPos = renderPos  -- 从当前渲染位置出发（软着陆）
            else
                st.prevPos = st.targetPos  -- 正常链式传递
            end
        else
            st.prevPos = st.targetPos
        end
        st.prevYaw = st.targetYaw           -- 旋转同理
        st.prevVelocity = st._rawVelocity    -- 速度同理（水平分量，用于外推）
    else
        -- 首次 tick：用当前 Transform 位置作为起点
        if player.transform ~= nil then
            st.prevPos = player.transform.position
        end
        st.prevYaw = yawDeg
        st.prevVelocity = CS.UnityEngine.Vector3.zero
    end

    -- ==== 水平移动 ====
    local hVelocity = CS.UnityEngine.Vector3.zero
    local hSpeed = 0
    local dirMask = moveDir & 0x0F  -- 低 4 位 = 移动方向
    local isRolling = (moveDir & GC.MOVE_ROLL) ~= 0  -- ★ bit 4 = 翻滚

    if dirMask ~= GC.MOVE_NONE then
        local forward = CS.UnityEngine.Vector3(math.sin(yawFloat), 0, math.cos(yawFloat))
        local right   = CS.UnityEngine.Vector3(math.cos(yawFloat), 0, -math.sin(yawFloat))
        local dir = CS.UnityEngine.Vector3.zero
        if dirMask & GC.MOVE_FORWARD ~= 0 then dir = dir + forward end
        if dirMask & GC.MOVE_BACKWARD ~= 0 then dir = dir - forward end
        if dirMask & GC.MOVE_RIGHT ~= 0 then dir = dir + right end
        if dirMask & GC.MOVE_LEFT ~= 0 then dir = dir - right end
        if dir.magnitude > 1 then dir = dir.normalized end
        local speed = isRolling and 12 or GC.MOVE_SPEED  -- ★ 翻滚速度 12
        hVelocity = dir * speed
        hSpeed = speed
    end

    -- ==== 垂直移动 ====
    local vertVelocity
    local justJumped = false
    if player.isGrounded then
        if player.isJumping then
            vertVelocity = GC.JUMP_FORCE
            player.isGrounded = false
            justJumped = true
            player._jumpInitiated = true   -- ★ 通知动画系统起跳
        else
            -- ★ 修复：着地且无跳跃时垂直速度为 0。
            --   CharacterController 内部处理地面接触，不需要额外向下的力。
            --   旧的 -GRAVITY*0.5 每 tick 把角色推入地面，PhysX 碰撞回推导致 Y 漂移，
            --   进而耦合影响水平位移精度（约 18 条测试失败）。
            vertVelocity = 0
        end
    else
        vertVelocity = Fix64.toFloat(player.velocity.y) - GC.GRAVITY * dtFloat
    end

    -- ==== 执行位移（CharacterController.Move）====
    -- ★ 先将 Transform 复位到上一 tick 的物理终点，确保物理模拟从正确位置开始
    --   避免因插值器修改了 Transform 导致物理位置累积漂移
    if st.targetPos ~= nil and player.transform ~= nil then
        player.transform.position = st.targetPos
    end

    -- ★ Phase 1: 子步物理 — 将 1/15s 大步长拆分为 N 次 ~1/60s 小步长
    --   匹配主机端 60fps 的碰撞精度，消除大步长穿透障碍物的问题
    --   总位移不变：subDisp × subSteps = hVelocity × dtFloat（完整步长）
    local subSteps = GC.PHYSICS_SUBSTEPS or 4
    local subDt = dtFloat / subSteps
    local subDisp = CS.UnityEngine.Vector3(
        hVelocity.x * subDt,
        vertVelocity * subDt,
        hVelocity.z * subDt
    )
    for step = 1, subSteps do
        local ok = pcall(controller.Move, controller, subDisp)
        if not ok then break end
    end

    -- 更新着地（所有子步完成后检测，避免中途状态变化）
    -- ★ 刚刚起跳时不立即检测着地，防止首帧位移太小（<stepOffset）
    --    导致 CharacterController.isGrounded 仍为 true 而取消跳跃
    if not justJumped then
        -- ★ GC优化：IsNull 已优化为无闭包版本，先判空再安全访问属性
        if not IsNull(controller) then
            player.isGrounded = controller.isGrounded
        end
    end

    -- ==== 存储速度（供动画/外推用）====
    -- ★ GC优化：原地更新 velocity，避免每 tick new Vec3 + 3 Fix64
    --   首次调用时 velocity 是共享的 Vec3.ZERO，需替换为新实例
    if player.velocity == Vec3.ZERO then
        player.velocity = Vec3.zero()
    end
    player.velocity.x.raw = CS.Fix64.FromFloat(hVelocity.x).Raw
    player.velocity.y.raw = CS.Fix64.FromFloat(vertVelocity).Raw
    player.velocity.z.raw = CS.Fix64.FromFloat(hVelocity.z).Raw
    player._hSpeed = hSpeed
    player._isRolling = isRolling

    -- ==== 保存插值终点 ====
    if player.transform ~= nil then
        st.targetPos = player.transform.position   -- 物理移动后的位置
        st.targetYaw = yawDeg                      -- 目标朝向（度）
        st._rawVelocity = CS.UnityEngine.Vector3(hVelocity.x, 0, hVelocity.z)  -- 水平速度（用于外推）
        st.hasTarget = true
        st.elapsed = 0  -- ★ 重置插值计时器

        -- ★★★ 诊断：前 5 tick + 每 30 tick 打印每个玩家的位置变化 ★★★
        if self.frameCount <= 5 or self.frameCount % 30 == 0 then
            local tag = (player.playerId == self.localPlayerId) and "LOCAL" or "REMOTE"
            local delta = (st.targetPos - st.prevPos).magnitude
            print(string.format("[PM] Move #%d tick=%d pid=%d %s moveDir=%d yaw=%.1fdeg isGnd=%s delta=%.3fm pos=(%.2f,%.2f,%.2f)",
                self.frameCount, self.frameCount, player.playerId, tag,
                moveDir, yawDeg, tostring(player.isGrounded), delta,
                st.targetPos.x, st.targetPos.y, st.targetPos.z))
        end

        -- ★ 本地玩家：保持在 targetPos（最新物理位置），不倒退
        --   跳过插值延迟，摄像机跟随即时位置，输入响应更跟手
        --   远程玩家：倒退到 prevPos，60fps 插值器在接下来 1/30s 内平滑驱动到 targetPos
        if player.playerId ~= self.localPlayerId then
            if st.prevPos ~= nil then
                player.transform.position = st.prevPos
                player.transform.rotation = CS.UnityEngine.Quaternion.Euler(0, st.prevYaw or 0, 0)
            end
        end
    end
end

--- 更新远程玩家的动画参数（每帧 60fps 调用，由 _InterpolateRemotePlayers 驱动）
--- ★ 动画 float 参数做帧间平滑，消除 30fps → 60fps 的数值跳变
function PlayerManager:_UpdateRemoteAnimator(player, dt)
    if player._animatorCached == nil and player.gameObject ~= nil and not IsNull(player.gameObject) then
        player._animatorCached = player.gameObject:GetComponentInChildren(typeof(CS.UnityEngine.Animator))
    end
    local anim = player._animatorCached
    if anim == nil or IsNull(anim) then
        player._animatorCached = nil
        return
    end

    -- 目标动画参数（由 tick 驱动，30fps 更新）
    local targetHSpeed = (player._hSpeed or 0) / GC.MOVE_SPEED
    local targetVSpeed = 0
    if not player.isGrounded then
        targetVSpeed = Fix64.toFloat(player.velocity.y)
    end

    -- ★ 动画参数平滑：display 值向 target 值靠拢，消除 30fps 的数值跳变
    local st = player._interpState
    if st ~= nil then
        local animSmooth = 1.0 - math.exp(-12 * dt)  -- 指数平滑，半衰期约 58ms
        st.displayHSpeed = st.displayHSpeed + (targetHSpeed - st.displayHSpeed) * animSmooth
        st.displayVSpeed = st.displayVSpeed + (targetVSpeed - st.displayVSpeed) * animSmooth
    else
        st = { displayHSpeed = targetHSpeed, displayVSpeed = targetVSpeed }
    end

    -- ★ 跳跃动画：仅在实际起跳时触发，与本地玩家行为一致
    --    player._jumpInitiated 由 _ApplyDeterministicMovement 设置
    if player._jumpInitiated then
        player._isJumpingAnim = true
        player._jumpInitiated = false
    elseif player.isGrounded then
        player._isJumpingAnim = false
    end

    anim:SetFloat("HSpeed", st.displayHSpeed)
    anim:SetFloat("VSpeed", st.displayVSpeed)
    anim:SetBool("Fire", player.isAttacking)
    anim:SetBool("Skill", player.isUsingSkill)
    anim:SetBool("Jump", player._isJumpingAnim or false)
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
    -- ★ 重置远程玩家的插值状态，避免从死亡位置 warp 到重生位置
    --   同时清除缓存的 Animator 引用，重生后重新查找（防御性）
    if player.playerId ~= self.localPlayerId then
        player._interpState = nil
        player._animatorCached = nil
    end
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
