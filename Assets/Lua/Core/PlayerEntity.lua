-- =============================================
-- Core/PlayerEntity.lua — 玩家数据实体
-- =============================================
-- 封装单个玩家的所有运行时状态。
-- 本地玩家和远程玩家共用此类，通过 isLocal 区分。
--
-- 【确定性架构】
--   position / yaw 使用 Fix64 / Fix64Vector3 存储（确定性）
--   渲染时通过 toUnity() 转为 Unity Vector3
--
-- 【网络输入驱动】
--   每帧 TickExecutor 回调 → PlayerManager.ApplyInput → 本类 SetInput
--   本地玩家通过 InputHandler 采集输入，远程玩家通过帧同步接收
-- =============================================

local Fix64 = require("Fix64")
local Vec3  = require("Fix64Vector3")
local GC    = require("Core.GameConst")

local PlayerEntity = {}
PlayerEntity.__index = PlayerEntity

-- ========== 构造 ==========

function PlayerEntity.new(playerId, playerName, isLocal)
    local self = setmetatable({}, PlayerEntity)
    self.playerId   = playerId
    self.playerName = playerName
    self.isLocal    = isLocal or false
    self.isAlive    = true

    -- 确定性位置/朝向
    self.position   = Vec3.new(Fix64.ZERO, Fix64.ZERO, Fix64.ZERO)
    self.yaw        = Fix64.ZERO          -- 水平朝向角（弧度）
    self.pitch      = Fix64.ZERO          -- 垂直视角角（弧度，仅本地使用）

    -- 服务端权威状态（由网络事件同步）
    self.hp         = GC.DEFAULT_HP
    self.maxHp      = GC.DEFAULT_HP
    self.score      = 0

    -- 当前帧输入状态（由 ApplyInput 设置）
    self.moveDir    = GC.MOVE_NONE
    self.isJumping  = false
    self.isAttacking = false
    self.isUsingSkill  = false
    self.chargeTime = Fix64.ZERO

    -- Unity GameObject 引用
    self.gameObject = nil
    self.transform  = nil
    self.controller = nil   -- CharacterController 组件引用

    -- 速度向量（用于本地物理模拟）
    self.velocity   = Vec3.ZERO
    self.isGrounded = true

    -- 跳跃动画状态（远程玩家用）
    self._isJumpingAnim   = false
    self._jumpInitiated   = false
    self._wasGroundedLast = true

    return self
end

-- ========== 输入驱动 ==========

--- 从网络帧输入更新当前帧的状态
--- @param input GameProto.PlayerInput（C# protobuf 对象）
function PlayerEntity:ApplyInput(input)
    self.moveDir      = input.MoveDir
    self.isJumping    = input.Jump
    self.isAttacking  = input.Attack
    self.isUsingSkill = input.Skill

    -- 朝向：服务端权威 yaw（proto 中为 sfixed64，C# 属性为 long = Fix64.Raw）
    self.yaw = Fix64.new(input.CameraYaw)

    -- 蓄力时间（同样为 sfixed64 → long → Fix64.Raw）
    self.chargeTime = Fix64.new(input.ChargeTime)
end

-- ========== 位置/朝向 ==========

--- 设置服务端权威位置（重生/校正时使用）
function PlayerEntity:SetPosition(x, y, z)
    self.position = Vec3.new(x, y, z)
    self:SyncTransform()
end

--- 设置朝向
function PlayerEntity:SetYaw(yaw)
    self.yaw = yaw
end

--- 将 Fix64 位置同步到 Unity Transform（渲染用）
function PlayerEntity:SyncTransform()
    if self.transform ~= nil and not IsNull(self.transform) then
        self.transform.position = Vec3.toUnity(self.position)
        -- yaw → Unity rotation（仅绕 Y 轴）
        self.transform.rotation = CS.UnityEngine.Quaternion.Euler(0, Fix64.toFloat(self.yaw) * 57.29578, 0)
    end
end

--- 获取用于射线检测/拾取的视线方向
function PlayerEntity:GetLookDirection()
    local yawFloat = Fix64.toFloat(self.yaw)
    local pitchFloat = Fix64.toFloat(self.pitch)
    -- 从 yaw/pitch 计算方向向量
    local dir = CS.UnityEngine.Vector3(
        math.sin(yawFloat) * math.cos(pitchFloat),
        -math.sin(pitchFloat),
        math.cos(yawFloat) * math.cos(pitchFloat)
    )
    return dir
end

--- 获取 Unity 世界坐标（用于渲染/物理）
function PlayerEntity:GetUnityPosition()
    return Vec3.toUnity(self.position)
end

--- 获取摄像机应该所在的位置（眼睛高度）
function PlayerEntity:GetCameraPosition()
    local pos = self:GetUnityPosition()
    -- 眼睛高度偏移（1.7 米典型值）
    return CS.UnityEngine.Vector3(pos.x, pos.y + 1.7, pos.z)
end

-- ========== 血量/分数 ==========

--- 受击扣血（由服务端消息驱动，非本地判定）
function PlayerEntity:TakeDamage(newHp)
    self.hp = newHp
    if self.hp <= 0 then
        self.isAlive = false
    end
    -- 通知 UI 更新
    if self.isLocal then
        self:NotifyUI()
    end
end

--- 更新分数（由服务端消息驱动）
function PlayerEntity:SetScore(newScore)
    self.score = newScore
    if self.isLocal then
        self:NotifyUI()
    end
end

--- 重生
function PlayerEntity:Respawn(hp)
    self.hp = hp or GC.DEFAULT_HP
    self.isAlive = true
    self.velocity = Vec3.ZERO
    if self.isLocal then
        self:NotifyUI()
    end
end

-- ========== UI 通知 ==========

--- 将 HP/Score 变化推送到 GamePanel
function PlayerEntity:NotifyUI()
    if self.isLocal and GamePanel ~= nil and GamePanel.instance ~= nil then
        -- HP 映射：服务端 3 点 → UI 100 点显示
        local uiHp = math.floor(self.hp / self.maxHp * 100)
        GamePanel.curHP = uiHp
        GamePanel:UpdateBloodDisplay()
        GamePanel:UpdateScore(self.score)
    end
end

-- ========== 销毁 ==========

--- 清理 GameObject 和引用
function PlayerEntity:Destroy()
    self.isAlive = false
    if self.gameObject ~= nil and not IsNull(self.gameObject) then
        CS.UnityEngine.GameObject.Destroy(self.gameObject)
    end
    self.gameObject = nil
    self.transform  = nil
    self.controller = nil
end

return PlayerEntity
