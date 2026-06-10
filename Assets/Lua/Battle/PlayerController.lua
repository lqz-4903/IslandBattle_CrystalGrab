-- =============================================
-- Battle/PlayerController.lua — 本地玩家控制器
-- =============================================
-- 【职责】
--   1. 采集 InputHandler 的输入，按帧同步节奏发送到服务端
--   2. 本地预测移动（CharacterController.Move + 重力）
--   3. 第一人称摄像机挂载与控制
--   4. 攻击/射击射线检测
--   5. 水晶拾取交互
--
-- 【双模式】
--   主机模式：通过 HostServer.SubmitHostInput 提交输入
--   客户端模式：通过 NetMgr.Send 发送 PlayerInput 消息
--
-- 【帧同步节奏】
--   输入采集：每 Unity 帧（60fps）
--   输入提交：每逻辑帧（15fps）
--   移动预测：每 Unity 帧（用最新输入）
-- =============================================

local GC = require("Core.GameConst")
local Fix64 = require("Fix64")
local Vec3  = require("Fix64Vector3")
local InputHandler = require("Battle.InputHandler")
local PlayerManager = require("Core.PlayerManager")

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

-- 摄像机引用
PlayerController._camera = nil
PlayerController._cameraTransform = nil
PlayerController._animator = nil   -- Animator 组件引用

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
    self._isJumpingAnim = false

    -- 初始化输入采集
    InputHandler:Init()

    -- 获取/创建摄像机
    self:_SetupCamera()

    -- 获取 Animator（从玩家 GameObject 的子对象中查找）
    local pm = PlayerManager.GetInstance()
    local player = pm:GetPlayer(self.playerId)
    if player ~= nil and player.gameObject ~= nil then
        self._animator = player.gameObject:GetComponentInChildren(typeof(CS.UnityEngine.Animator))
        if self._animator ~= nil then
            print("[PlayerController] Animator 已就绪")
        else
            print("[PlayerController] 警告：未找到 Animator")
        end
    end

    -- 注册每帧更新
    self._updateId = RegisterUpdate(function(dt)
        self:Update(dt)
    end)

    print("[PlayerController] 初始化完成 playerId=" .. playerId .. " isHost=" .. tostring(self.isHost))
end

--- 关闭
function PlayerController:Shutdown()
    if self._updateId ~= nil then
        UnregisterUpdate(self._updateId)
        self._updateId = nil
    end
    self.initialized = false
    InputHandler:UnlockCursor()
    print("[PlayerController] 已关闭")
end

-- ========== 每帧更新 ==========

function PlayerController:Update(dt)
    if not self.initialized then return end

    -- 1. 采集本帧输入
    InputHandler:Update(dt)

    -- 2. 本地预测移动（每帧执行，不等待服务端）
    self:_ApplyLocalMovement(dt)

    -- 3. 更新摄像机
    self:_UpdateCamera()

    -- 4. 更新动画参数
    self:_UpdateAnimator(dt)

    -- 5. 按逻辑帧率提交输入到服务端
    self._tickTimer = self._tickTimer + dt
    if self._tickTimer >= GC.TICK_INTERVAL then
        self._tickTimer = self._tickTimer - GC.TICK_INTERVAL
        self:_SubmitInput()
    end

    -- 5. 攻击检测
    if InputHandler.attackHeld then
        self:_ProcessAttack(dt)
    end

    -- 6. 交互检测（拾取水晶）
    if InputHandler.skillPressed then
        self:_ProcessInteract()
    end
end

-- ========== 本地移动预测 ==========

-- 翻滚状态
PlayerController._isRolling = false
PlayerController._rollTimer = 0
local ROLL_DURATION = 0.5        -- 翻滚持续秒数
local ROLL_SPEED = 12             -- 翻滚前冲速度（比走路快）

--- 在本地立即应用移动（客户端预测，不等服务端确认）
function PlayerController:_ApplyLocalMovement(dt)
    local pm = PlayerManager.GetInstance()
    local player = pm:GetPlayer(self.playerId)
    if player == nil then return end
    if not player.isAlive then return end

    local controller = player.controller
    if controller == nil or IsNull(controller) then return end

    local moveDir = InputHandler.moveDir
    local yaw = InputHandler.cameraYaw

    -- ==== 翻滚逻辑 ====
    if InputHandler.rollPressed and player.isGrounded and not self._isRolling then
        self._isRolling = true
        self._rollTimer = 0
        -- 翻滚方向：有移动输入则朝移动方向翻滚，否则朝摄像机前方翻滚
        if moveDir ~= GC.MOVE_NONE then
            self._rollYaw = yaw  -- 用当前移动朝向
        else
            self._rollYaw = yaw  -- 用摄像机朝向
        end
    end
    if self._isRolling then
        self._rollTimer = self._rollTimer + dt
        if self._rollTimer >= ROLL_DURATION then
            self._isRolling = false
        end
    end

    -- ==== 水平移动 ====
    local hSpeed = 0
    local hVelocity
    if self._isRolling then
        -- 翻滚：强制前冲
        local forward = CS.UnityEngine.Vector3(math.sin(self._rollYaw), 0, math.cos(self._rollYaw))
        hVelocity = forward * ROLL_SPEED
        hSpeed = ROLL_SPEED
    elseif moveDir ~= GC.MOVE_NONE then
        local moveVector = self:_DirToWorld(moveDir, yaw)
        hVelocity = CS.UnityEngine.Vector3(moveVector.x * GC.MOVE_SPEED, 0, moveVector.z * GC.MOVE_SPEED)
        hSpeed = GC.MOVE_SPEED
    else
        hVelocity = CS.UnityEngine.Vector3.zero
    end

    -- ==== 垂直移动（重力 + 跳跃）====
    local vertVelocity
    local justJumped = false
    if player.isGrounded then
        if InputHandler.jumpPressed and not self._isRolling then
            vertVelocity = GC.JUMP_FORCE
            player.isGrounded = false
            justJumped = true
        else
            -- 贴地时用较强负速度压住地面，防止起伏地形弹跳
            vertVelocity = -GC.GRAVITY * 0.5
        end
    else
        -- 空中：从存储的速度减去本帧重力
        vertVelocity = Fix64.toFloat(player.velocity.y) - GC.GRAVITY * dt
    end

    -- ==== 执行位移 ====
    local displacement = CS.UnityEngine.Vector3(
        hVelocity.x * dt,
        vertVelocity * dt,
        hVelocity.z * dt
    )

    local ok = pcall(function() controller:Move(displacement) end)
    if not ok then return end

    -- 更新着地状态
    -- ★ 刚刚起跳时不立即检测着地，防止首帧位移太小（<stepOffset）
    --    导致 CharacterController.isGrounded 仍为 true 而取消跳跃
    if not justJumped then
        local ok2, grounded = pcall(function() return controller.isGrounded end)
        if ok2 then player.isGrounded = grounded end
    end

    -- 同步位置
    if player.transform ~= nil then
        local pos = player.transform.position
        player.position = Vec3.new(Fix64.fromFloat(pos.x), Fix64.fromFloat(pos.y), Fix64.fromFloat(pos.z))
    end

    -- 存储速度（供下一帧重力计算和动画用）
    player._hSpeed = hSpeed
    player.velocity = Vec3.new(
        Fix64.fromFloat(hVelocity.x),
        Fix64.fromFloat(vertVelocity),
        Fix64.fromFloat(hVelocity.z)
    )
end

--- 将 moveDir 位掩码 + cameraYaw 转为世界空间方向向量
function PlayerController:_DirToWorld(moveDir, yaw)
    local forward = CS.UnityEngine.Vector3(math.sin(yaw), 0, math.cos(yaw))
    local right   = CS.UnityEngine.Vector3(math.cos(yaw), 0, -math.sin(yaw))

    local result = CS.UnityEngine.Vector3.zero

    if moveDir & GC.MOVE_FORWARD ~= 0 then
        result = result + forward
    end
    if moveDir & GC.MOVE_BACKWARD ~= 0 then
        result = result - forward
    end
    if moveDir & GC.MOVE_RIGHT ~= 0 then
        result = result + right
    end
    if moveDir & GC.MOVE_LEFT ~= 0 then
        result = result - right
    end

    -- 归一化（斜向移动时保持速度一致）
    if result.magnitude > 1 then
        result = result.normalized
    end

    return result
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

-- ========== 动画驱动 ==========

--- 每帧更新 Animator 参数
function PlayerController:_UpdateAnimator(dt)
    if self._animator == nil then return end
    if IsNull(self._animator) then self._animator = nil; return end

    local anim = self._animator
    local pm = PlayerManager.GetInstance()
    local player = pm:GetPlayer(self.playerId)

    -- 水平速度：实际速度归一化到 0~1（走路=1.0，停止=0）
    local hSpeed = 0
    if player ~= nil and player._hSpeed ~= nil then
        hSpeed = player._hSpeed / GC.MOVE_SPEED
    end

    -- 垂直速度：只有真正在空中时才非零，贴地时强制为 0
    local vSpeed = 0
    local isGrounded = true
    if player ~= nil then
        isGrounded = player.isGrounded
        if not isGrounded and not IsNull(player.controller) then
            -- 空中：读取存储的垂直速度
            vSpeed = Fix64.toFloat(player.velocity.y)
        end
        -- 贴地时 vSpeed 保持 0
    end

    anim:SetFloat("HSpeed", hSpeed)
    anim:SetFloat("VSpeed", vSpeed)

    -- 动作 bool
    anim:SetBool("Fire", InputHandler.attackHeld)
    anim:SetBool("Skill", InputHandler.skillPressed or false)

    -- ★ Jump 只在玩家主动按空格时触发，自动上台阶/起伏地形不触发
    if InputHandler.jumpPressed and isGrounded then
        self._isJumpingAnim = true
    elseif isGrounded then
        self._isJumpingAnim = false
    end
    anim:SetBool("Jump", self._isJumpingAnim)

    anim:SetBool("Roll", self._isRolling)
    anim:SetBool("Reload", InputHandler.reloadPressed)
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
        chargeRaw
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
    playerInput.Tick = 0
    -- ★ 翻滚编码到 MoveDir 的 bit 4（不修改 proto）
    local moveDir = input.moveDir
    if self._isRolling then
        moveDir = moveDir | GC.MOVE_ROLL
    end
    playerInput.MoveDir = moveDir
    playerInput.Jump = input.jump
    playerInput.Attack = input.attack
    playerInput.Skill = input.skill
    playerInput.CameraYaw = yawRaw        -- long (Fix64.Raw)
    playerInput.ChargeTime = chargeRaw    -- long (Fix64.Raw)

    -- 打包到 NetMessage 并发送
    local envelope = CS.GameProto.NetMessage()
    envelope.PlayerInput = playerInput
    netMgr:Send(envelope)
end

-- ========== 攻击处理 ==========

--- 处理攻击/射击（每帧调用，attackHeld 为 true 时）
function PlayerController:_ProcessAttack(dt)
    -- TODO: 实现射击逻辑
    -- 1. 从摄像机中心做射线检测
    -- 2. 命中其他玩家 → 上报 PlayerHit 到服务端
    -- 3. 播放枪口特效/音效

    -- 示例射线检测代码：
    -- local cam = self._camera
    -- if cam == nil then return end
    -- local ray = CS.UnityEngine.Ray(cam.transform.position, cam.transform.forward)
    -- local hitInfo = CS.UnityEngine.RaycastHit()
    -- if CS.UnityEngine.Physics.Raycast(ray, hitInfo, 100) then
    --     -- 检测是否命中玩家
    --     local hitGo = hitInfo.collider.gameObject
    --     -- 上报命中事件...
    -- end
end

-- ========== 交互处理 ==========

--- 处理交互/拾取（skillPressed 为 true 时）
function PlayerController:_ProcessInteract()
    -- TODO: 实现水晶拾取逻辑
    -- 1. 从摄像机中心做射线检测
    -- 2. 命中水晶 → 上报 CrystalPickup 到服务端
    -- 3. 服务端验证后广播权威结果

    -- 示例：
    -- local cam = self._camera
    -- if cam == nil then return end
    -- local ray = CS.UnityEngine.Ray(cam.transform.position, cam.transform.forward)
    -- local hitInfo = CS.UnityEngine.RaycastHit()
    -- if CS.UnityEngine.Physics.Raycast(ray, hitInfo, 3) then
    --     local hitGo = hitInfo.collider.gameObject
    --     -- 检测是否为水晶（通过 tag 或组件判断）
    --     if hitGo.CompareTag("Crystal") then
    --         self:_SendCrystalPickup(crystalId)
    --     end
    -- end
end

return PlayerController
