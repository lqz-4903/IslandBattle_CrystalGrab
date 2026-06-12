-- =============================================
-- Battle/PlayerController.lua — 本地玩家控制器
-- =============================================
-- 【职责】
--   1. 采集 InputHandler 的输入（60fps），按帧同步节奏提交到服务端（30fps）
--   2. 翻滚状态管理（60fps 更新，编码到 moveDir bit 4）
--   3. 第一人称摄像机控制（LateUpdate 60fps，物理回退之后）
--   4. 攻击/射击射线检测
--   5. 水晶拾取交互
--   6. 客户端预测-校正：输入缓冲管理（reconciliation）
--
-- 【统一 30fps 物理】
--   所有玩家（本地+远程）统一走 PlayerManager:_ApplyDeterministicMovement（30fps）
--   渲染由 PlayerManager._InterpolateAllPlayers（LateUpdate 60fps 插值+动画）驱动
--   摄像机由本模块 LateUpdate 60fps 驱动
--
-- 【双模式】
--   主机模式：通过 HostServer.SubmitHostInput 提交输入
--   客户端模式：通过 NetMgr.Send 发送 PlayerInput 消息
-- =============================================

local GC = require("Core.GameConst")
local InputHandler = require("Battle.InputHandler")
local PlayerManager = require("Core.PlayerManager")
local NetworkEventMgr = require("Core.NetworkEventMgr")
local CrystalManager = require("Battle.CrystalManager")
local Arrow = require("Battle.Arrow")

-- ★ GC优化：预缓存 table.sort 比较函数，避免每次 reconciliation 创建闭包
local function _sortByTick(a, b) return a.tick < b.tick end

local PlayerController = {}
PlayerController.__index = PlayerController

-- ========== 状态 ==========

-- 本地玩家 ID（在 Init 时设置）
PlayerController.playerId = 0

-- 是否为主机模式
PlayerController.isHost = false

-- 是否已初始化
PlayerController.initialized = false

-- 逻辑帧计时器
PlayerController._tickTimer = 0

-- 蓄力时间（用于本地表现）
PlayerController._localChargeTime = 0

-- 客户端预测-校正：输入缓冲
PlayerController._inputBuffer = {}          -- tick → {moveDir, yaw, jump}
PlayerController._nextSendTick = 1          -- 下一个要分配的客户端 tick（1-based，0 = 服务端 stub）
PlayerController._lastAckedTick = 0         -- 最后被服务端确认的客户端 tick

-- 摄像机引用
PlayerController._camera = nil
PlayerController._cameraTransform = nil

-- Update 回调 ID
PlayerController._updateId = nil

-- ========== 初始化 ==========

--- 初始化玩家控制器
--- @param playerId  int — 本地玩家 ID
--- @param isHost    bool — 是否主机模式
function PlayerController:Init(playerId, isHost)
    if self.initialized then
        self:Shutdown()
    end

    self.playerId    = playerId
    self.isHost      = isHost or false
    self.initialized    = true
    self._tickTimer     = 0
    self._isRolling     = false
    self._rollTimer     = 0

    -- 客户端预测-校正：重置输入缓冲
    self._inputBuffer = {}
    self._bufPool = {}           -- ★ GC优化：table 复用池
    self._nextSendTick = 1
    self._lastAckedTick = 0

    -- 初始化输入采集
    InputHandler:Init()

    -- 获取/创建摄像机
    self:_SetupCamera()

    -- ★ 动画由 PlayerManager._UpdateRemoteAnimator 统一驱动（LateUpdate）
    --   不再由 PlayerController 单独管理 Animator

    -- 注册每帧更新（输入 + 翻滚状态）
    self._updateId = RegisterUpdate(function(dt)
        self:Update(dt)
    end)

    -- ★ 注册 LateUpdate 渲染层（摄像机，在物理回退之后）
    self._lateUpdateId = RegisterLateUpdate(function()
        self:_UpdateCamera()
    end)

    print("[PlayerController] 初始化完成 playerId=" .. playerId .. " isHost=" .. tostring(self.isHost))
end

--- 关闭
function PlayerController:Shutdown()
    if self._updateId ~= nil then
        UnregisterUpdate(self._updateId)
        self._updateId = nil
    end
    if self._lateUpdateId ~= nil then
        UnregisterLateUpdate(self._lateUpdateId)
        self._lateUpdateId = nil
    end
    self.initialized = false
    self._inputBuffer = {}
    self._bufPool = {}
    self._nextSendTick = 1
    self._lastAckedTick = 0
    InputHandler:UnlockCursor()
    print("[PlayerController] 已关闭")
end

-- ========== 每帧更新 ==========

function PlayerController:Update(dt)
    if not self.initialized then return end

    -- 1. 采集本帧输入（60fps）
    InputHandler:Update(dt)

    -- 2. 本地输入状态管理（60fps 即时反馈：翻滚 + 跳跃动画）
    self:_UpdateLocalInputState(dt)

    -- ★ 摄像机由 LateUpdate 驱动（在物理回退之后，读到正确的插值位置）
    -- ★ 动画由 PlayerManager._InterpolateAllPlayers 统一驱动（LateUpdate 60fps 平滑）
    --   不再由 PlayerController 单独更新，保证本地/远程玩家动画逻辑一致

    -- 4. 按逻辑帧率提交输入到服务端（30fps）
    self._tickTimer = self._tickTimer + dt
    if self._tickTimer >= GC.TICK_INTERVAL then
        self._tickTimer = self._tickTimer - GC.TICK_INTERVAL
        self:_SubmitInput()
    end

    -- 5. 攻击检测
    if InputHandler.attackHeld then
        self:_ProcessAttack(dt)
    end

    -- ★ 水晶拾取不再用距离遍历 — 由 CrystalComponent.OnTriggerEnter 触发
    --   角色进入水晶 SphereCollider(isTrigger) 时自动拾取，见 CrystalManager:_HandleTriggerEnter
end

-- ========== 本地输入状态管理（60fps 即时反馈）==========

--- 翻滚状态（60fps 更新，供 _SubmitInput 编码到 moveDir bit 4）
PlayerController._isRolling = false
PlayerController._rollTimer = 0
PlayerController._rollYaw = 0
local ROLL_DURATION = 0.5        -- 翻滚持续秒数

--- 每帧更新本地输入状态（60fps）
---   - 翻滚标志通过 moveDir bit 4 提交到服务端，由 _ApplyDeterministicMovement 执行物理
---   - 跳跃动画立即触发（不等 30fps tick），物理在下一个 tick 跟上
function PlayerController:_UpdateLocalInputState(_dt)
    local pm = PlayerManager.GetInstance()
    local player = pm:GetPlayer(self.playerId)
    if player == nil or not player.isAlive then return end

    -- 翻滚状态管理
    if InputHandler.rollPressed and player.isGrounded and not self._isRolling then
        self._isRolling = true
        self._rollTimer = 0
        self._rollYaw = InputHandler.cameraYaw
    end
    if self._isRolling then
        self._rollTimer = self._rollTimer + _dt
        if self._rollTimer >= ROLL_DURATION then
            self._isRolling = false
        end
    end

    -- ★ 跳跃即时视觉反馈：按跳跃键时立即触发动画，不等 30fps tick
    --   物理跳跃由 _ApplyDeterministicMovement 在下一个 tick 执行
    --   动画由 _UpdateRemoteAnimator 读取 _jumpInitiated 驱动
    if InputHandler.jumpPressed and player.isGrounded and not self._isRolling then
        player._jumpInitiated = true
    end
end

-- ========== 摄像机 ==========

--- 摄像机路径（相对玩家 Prefab 根节点）
local CAMERA_PATH = "CameraPoint/GameCamera"
--- GameCamera 相对于玩家根节点的恒定局部偏移
local CAMERA_LOCAL_POS = CS.UnityEngine.Vector3(-0.014, 1.283, 0.544)

--- 获取摄像机（优先使用预制体上 CameraPoint/GameCamera 预挂载的）
--- 远程玩家的摄像机会在 PlayerManager 中自动禁用。
function PlayerController:_SetupCamera()
    local pm = PlayerManager.GetInstance()
    local player = pm:GetPlayer(self.playerId)
    if player ~= nil and player.gameObject ~= nil and not IsNull(player.gameObject) then
        -- 按路径查找 CameraPoint/GameCamera
        local camPoint = player.transform:Find("CameraPoint")
        if camPoint ~= nil then
            local gameCamera = camPoint:Find("GameCamera")
            if gameCamera ~= nil then
                local playerCam = gameCamera:GetComponent(typeof(CS.UnityEngine.Camera))
                if playerCam ~= nil then
                    self:_ApplyCameraSettings(playerCam)
                    self._camera = playerCam
                    self._cameraTransform = gameCamera
                    -- 确保 AudioListener 存在
                    if gameCamera:GetComponent(typeof(CS.UnityEngine.AudioListener)) == nil then
                        gameCamera:AddComponent(typeof(CS.UnityEngine.AudioListener))
                    end
                    print("[PlayerController] 使用预制体摄像机: " .. CAMERA_PATH)
                    return
                end
            end
        end
    end

    -- 降级：场景 MainCamera 或创建新摄像机
    self._camera = CS.UnityEngine.Camera.main
    if self._camera ~= nil then
        self._cameraTransform = self._camera.transform
        self:_ApplyCameraSettings(self._camera)
        print("[PlayerController] 使用场景 MainCamera")
        return
    end

    -- 兜底创建
    local camGo = CS.UnityEngine.GameObject("MainCamera")
    self._camera = camGo:AddComponent(typeof(CS.UnityEngine.Camera))
    camGo:AddComponent(typeof(CS.UnityEngine.AudioListener))
    camGo.tag = "MainCamera"
    self._cameraTransform = camGo.transform
    self:_ApplyCameraSettings(self._camera)
    print("[PlayerController] 降级创建默认摄像机")
end

--- 设置摄像机参数（与 UICamera 配合：UICamera depth=0 渲染 UI 层在上）
function PlayerController:_ApplyCameraSettings(cam)
    -- 场景渲染（不渲染 UI 层，UI 层由 UICamera 单独渲染）
    cam.clearFlags = CS.UnityEngine.CameraClearFlags.Skybox
    cam.cullingMask = cam.cullingMask & ~CS.UnityEngine.LayerMask.GetMask("UI")
    -- Depth 为 -1，确保在 UICamera（depth=0）的下层
    cam.depth = -1
    cam.fieldOfView = 75
    cam.nearClipPlane = 0.1
    cam.farClipPlane = 1000
end

--- 每帧更新摄像机位置和旋转
--- 位置：根节点世界位置 + 由 yaw 旋转的局部偏移（始终在玩家"眼前"）
--- 旋转：世界空间（不受 Animator 根运动影响，完全由鼠标控制）
function PlayerController:_UpdateCamera()
    if self._cameraTransform == nil then return end

    local pm = PlayerManager.GetInstance()
    local player = pm:GetPlayer(self.playerId)
    if player == nil or player.transform == nil or IsNull(player.transform) then return end

    local yaw   = InputHandler.cameraYaw
    local pitch = InputHandler.cameraPitch
    local yawDeg   = math.deg(yaw)
    local pitchDeg = math.deg(pitch)

    -- 玩家根节点只转 yaw（身体朝向）
    player.transform.rotation = CS.UnityEngine.Quaternion.Euler(0, yawDeg, 0)

    -- 摄像机世界位置：将局部偏移按 yaw 旋转到世界空间，再加到根位置
    -- 这样摄像机始终在玩家脸前，转身时绕玩家旋转，不会穿模看到后脑勺
    local yawQuat = CS.UnityEngine.Quaternion.Euler(0, yawDeg, 0)
    local worldOffset = yawQuat * CAMERA_LOCAL_POS
    local rootPos = player.transform.position
    self._cameraTransform.position = CS.UnityEngine.Vector3(
        rootPos.x + worldOffset.x,
        rootPos.y + worldOffset.y,
        rootPos.z + worldOffset.z
    )

    -- 摄像机世界旋转（不受 Animator 根运动影响）
    self._cameraTransform.rotation = CS.UnityEngine.Quaternion.Euler(pitchDeg, yawDeg, 0)
end

-- ========== 输入提交 ==========

--- 按逻辑帧率提交输入到服务端
function PlayerController:_SubmitInput()
    local input = InputHandler:GetTickInput()

    if self.isHost then
        -- 主机模式：直接调用 C# HostServer
        self:_SubmitHostInput(input)
    else
        -- 客户端模式：发送 PlayerInput 网络消息
        self:_SubmitClientInput(input)
    end
end

--- 主机模式：通过 HostServer 提交（传 Fix64.Raw，不再经过 float）
function PlayerController:_SubmitHostInput(input)
    local hostServer = CS.HostServer.Instance
    if hostServer == nil or not hostServer.IsGameStarted then return end

    -- 将 Lua float 转为 Fix64.Raw（long），消除 float→Fix64→float 往返精度丢失
    local yawRaw   = CS.Fix64.FromFloat(input.cameraYaw).Raw
    local chargeRaw = CS.Fix64.FromFloat(input.chargeTime).Raw
    local pitchRaw = CS.Fix64.FromFloat(InputHandler.cameraPitch).Raw

    -- ★ 翻滚编码到 MoveDir 的 bit 4（不修改 proto）
    local moveDir = input.moveDir
    if self._isRolling then
        moveDir = moveDir | GC.MOVE_ROLL
    end

    hostServer:SubmitHostInput(
        moveDir,
        input.jump,
        input.attack,
        input.skill,
        yawRaw,
        chargeRaw,
        pitchRaw
    )
end

--- 客户端模式：发送网络消息（CameraYaw/ChargeTime 现为 sfixed64 → long）
function PlayerController:_SubmitClientInput(input)
    local netMgr = CS.NetMgr.Instance
    if netMgr == nil then return end

    -- 将 Lua float 转为 Fix64.Raw（long）
    local yawRaw   = CS.Fix64.FromFloat(input.cameraYaw).Raw
    local chargeRaw = CS.Fix64.FromFloat(input.chargeTime).Raw

    -- 构造 PlayerInput protobuf 消息
    local playerInput = CS.GameProto.PlayerInput()
    playerInput.PlayerId = self.playerId
    local clientTick = self._nextSendTick
    self._nextSendTick = clientTick + 1
    playerInput.Tick = clientTick

    -- ★ 翻滚编码到 MoveDir 的 bit 4（不修改 proto）
    local moveDir = input.moveDir
    if self._isRolling then
        moveDir = moveDir | GC.MOVE_ROLL
    end
    playerInput.MoveDir = moveDir

    -- ★ 客户端预测-校正：缓存输入供回滚重放（含翻滚标记）
    --    GC优化：复用 _bufPool 中的 table，避免每个 tick new {}
    -- ★ 修复 Bug2：key 应使用 clientTick（与 _inputBuffer 一致），而非已递增的 _nextSendTick。
    --   旧代码 _bufPool[_nextSendTick] 导致池 key 偏移 1，table 永远无法复用。
    local entry = self._bufPool[clientTick]
    if entry == nil then
        entry = {}
        self._bufPool[clientTick] = entry
    end
    entry.moveDir = moveDir
    entry.yaw     = input.cameraYaw
    entry.jump    = input.jump
    self._inputBuffer[clientTick] = entry
    self:_TrimInputBuffer()
    playerInput.Jump = input.jump
    playerInput.Attack = input.attack
    playerInput.Skill = input.skill
    playerInput.CameraYaw = yawRaw        -- long (Fix64.Raw)
    playerInput.ChargeTime = chargeRaw    -- long (Fix64.Raw)
    playerInput.CameraPitch = CS.Fix64.FromFloat(InputHandler.cameraPitch).Raw  -- long (Fix64.Raw)

    -- 打包到 NetMessage 并发送
    local envelope = CS.GameProto.NetMessage()
    envelope.PlayerInput = playerInput
    netMgr:Send(envelope)
end

-- ========== 攻击处理 ==========

--- 处理攻击/射击（每帧调用，attackHeld 为 true 时）
--- ★ Phase 1：箭矢发射（纯视觉飞行，无碰撞检测）
---   - firePressed 触发：从摄像机位置发射 ArrowDefault（本地即时预览）
---   - 远程玩家通过 PlayerEntity.ApplyInput 上升沿 → FireNetworked 生成
function PlayerController:_ProcessAttack(dt)
    -- 仅在攻击阶段可以攻击
    if not NetworkEventMgr._canAttack then return end

    -- ★ 死亡后不可攻击（问题 9）
    local pm = PlayerManager.GetInstance()
    local player = pm:GetPlayer(self.playerId)
    if player == nil or not player.isAlive then return end

    -- ★ firePressed 粘滞标记：每 click 发射一发箭矢
    if InputHandler.firePressed then
        -- ★ 本地：箭从摄像机位置射出，完美对准准星（FPS 体验）
        --   摄像机前方 0.3m offset 避免箭出现在视野正中心遮挡视线
        local spawnPos = self._cameraTransform.position + self._cameraTransform.forward * 0.3
        local forward = self._cameraTransform.forward
        Arrow.FireLocal(self.playerId, spawnPos, forward, GC.ARROW_SPEED, GC.ARROW_LIFETIME)
    end
end

-- ========== 交互处理 ==========

--- 处理水晶拾取（距离检测，不依赖按键，自动拾取最近的水晶）
--- ★ 全程可拾取（生成阶段 + 攻击阶段均可）
function PlayerController:_ProcessInteract()
    local pm = PlayerManager.GetInstance()
    local player = pm:GetPlayer(self.playerId)
    if player == nil or player.transform == nil then return end

    local cm = CrystalManager.GetInstance()
    local pickupRange = cm:GetPickupRange() or 0.8

    local myPos = player.transform.position

    -- 遍历所有水晶，找最近的在范围内的
    local nearestId, nearestDist = nil, math.huge
    for crystalId, go in cm:ForEach() do
        if go ~= nil and not IsNull(go) then
            -- Tag 检查（CompareTag 在已销毁对象上会抛异常，先判空）
            local ok, hasTag = pcall(function() return go.CompareTag("Crystal") end)
            if ok and hasTag then
                local crystalPos = go.transform.position
                local dx, dy, dz = myPos.x - crystalPos.x,
                                   myPos.y - crystalPos.y,
                                   myPos.z - crystalPos.z
                local dist = math.sqrt(dx * dx + dy * dy + dz * dz)
                if dist < pickupRange and dist < nearestDist then
                    nearestDist = dist
                    nearestId = crystalId
                end
            end
        end
    end

    -- 如果在范围内，发送拾取请求
    if nearestId ~= nil then
        self:_SendCrystalPickup(nearestId)
    end
end

--- 发送水晶拾取请求到服务端
function PlayerController:_SendCrystalPickup(crystalId)
    if crystalId == nil then return end

    local pickupMsg = CS.GameProto.CrystalPickup()
    pickupMsg.CrystalId = crystalId
    pickupMsg.PlayerId = self.playerId

    local envelope = CS.GameProto.NetMessage()
    envelope.CrystalPickup = pickupMsg

    if self.isHost then
        -- 主机模式：直接调服务端处理
        local hostServer = CS.HostServer.Instance
        if hostServer ~= nil then
            hostServer:SubmitHostCrystalPickup(crystalId, self.playerId)
        end
    else
        -- 客户端模式：发送网络消息
        local netMgr = CS.NetMgr.Instance
        if netMgr ~= nil then
            netMgr:Send(envelope)
        end
    end
end

-- ========== 客户端预测-校正：输入缓冲管理 ==========

--- 裁剪超出容量的旧输入
function PlayerController:_TrimInputBuffer()
    local maxBuf = GC.INPUT_BUFFER_MAX or 90
    local cutoff = self._nextSendTick - maxBuf
    if cutoff <= 0 then return end
    for tick, _ in pairs(self._inputBuffer) do
        if tick < cutoff then
            self._inputBuffer[tick] = nil
        end
    end
end

--- 确认服务端已消费到指定 tick，清除已确认的输入
--- @param tick int — 已确认的最大客户端 tick（含），之前的全部清除
function PlayerController:AcknowledgeUpTo(tick)
    if tick <= self._lastAckedTick then return end
    self._lastAckedTick = tick
    for t, _ in pairs(self._inputBuffer) do
        if t <= tick then
            self._inputBuffer[t] = nil
        end
    end
end

--- 获取未确认输入的数量（不创建任何 table，零 GC）
--- @return int
function PlayerController:GetUnackedCount()
    local count = 0
    for tick, _ in pairs(self._inputBuffer) do
        if tick > self._lastAckedTick then
            count = count + 1
        end
    end
    return count
end

--- 获取所有未被服务端确认的输入（按 tick 升序排列）
--- @return table[] — {{tick=int, data={moveDir,yaw,jump}}, ...}
function PlayerController:GetUnackedInputs()
    local result = {}
    for tick, data in pairs(self._inputBuffer) do
        if tick > self._lastAckedTick then
            result[#result + 1] = { tick = tick, data = data }
        end
    end
    table.sort(result, _sortByTick)
    return result
end

return PlayerController
