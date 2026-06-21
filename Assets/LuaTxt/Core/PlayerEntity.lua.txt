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
local Arrow = require("Battle.Arrow")

local PlayerEntity = {}
PlayerEntity.__index = PlayerEntity

-- ========== 构造 ==========

function PlayerEntity.new(playerId, playerName, isLocal)
    local self = setmetatable({}, PlayerEntity)
    self.playerId   = playerId
    self.playerName = playerName
    self.isLocal    = isLocal or false
    self.isAlive    = true

    -- 确定性位置/朝向（Vec3.zero() 创建独立 Fix64.new(0) 分量，可安全 .raw 原地修改）
    self.position   = Vec3.zero()
    -- ★ 修复：yaw/pitch/chargeTime 设为 nil，让 ApplyInput 首次调用时自动创建新实例。
    --   若初始化为 Fix64.ZERO（模块级共享常量），ApplyInput 的 GC 优化路径会直接修改
    --   Fix64.ZERO.raw，污染所有模块的 Fix64 零点语义，导致位置/速度计算错误。
    self.yaw        = nil   -- 水平朝向角（弧度，由 ApplyInput 首次赋值）
    self.pitch      = nil   -- 垂直视角角（弧度，仅本地使用）

    -- 服务端权威状态（由网络事件同步）
    self.hp         = GC.DEFAULT_HP
    self.maxHp      = GC.DEFAULT_HP
    self.score      = 0

    -- 当前帧输入状态（由 ApplyInput 设置）
    self.moveDir    = GC.MOVE_NONE
    self.isJumping  = false
    self.isAttacking = false
    self.isUsingSkill  = false
    self.chargeTime = nil  -- ★ 修复：nil 初值，避免共享 Fix64.ZERO

    -- Unity GameObject 引用
    self.gameObject = nil
    self.transform  = nil
    self.controller = nil   -- CharacterController 组件引用

    -- 速度向量（用于本地物理模拟）
    -- ★★★ 关键修复：使用 Vec3.zero() 创建独立 Fix64.new(0) 分量。
    --   Vec3.new(Fix64.ZERO, Fix64.ZERO, Fix64.ZERO) 的分量是共享的 Fix64.ZERO，
    --   _ApplyDeterministicMovement 中 velocity.x.raw = ... 会直接污染 Fix64.ZERO.raw，
    --   导致所有玩家的 Fix64 零点语义崩溃（如 else 分支：Fix64.toFloat(player.velocity.y) 返回非零值）。
    self.velocity   = Vec3.zero()
    self.isGrounded = true  -- 由 SpawnAllPlayers 在 CC 就绪后覆盖为实际值

    -- 跳跃动画状态（远程玩家用）
    self._isJumpingAnim   = false
    self._jumpInitiated   = false
    self._wasGroundedLast = true

    -- ★ 攻击上升沿检测（false→true 触发箭矢发射，问题 1/3）
    self._wasAttackingLastTick = false
    self._isReplaying = false  -- 重连追帧期间跳过箭矢生成

    -- 箭矢发射点缓存（HeroDefault/root/ArrowPoint）
    self._arrowPointTransform = nil

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
    -- ★ GC优化：原地更新 raw 值，避免每次 tick new Fix64 table
    if self.yaw == nil then
        self.yaw = Fix64.new(input.CameraYaw)
    else
        self.yaw.raw = input.CameraYaw
    end

    -- 蓄力时间（同样为 sfixed64 → long → Fix64.Raw）
    if self.chargeTime == nil then
        self.chargeTime = Fix64.new(input.ChargeTime)
    else
        self.chargeTime.raw = input.ChargeTime
    end

    -- ★ 攻击上升沿检测：false→true 时发射箭矢
    --     仅远程玩家走此路径（本地玩家由 PlayerController._ProcessAttack → FireLocal 负责）
    --     重连追帧期间跳过（_isReplaying），避免幽灵箭矢
    if input.Attack and not self._wasAttackingLastTick then
        if not self.isLocal and not self._isReplaying then
            -- ★ 远程：箭从 ArrowPoint（武器位置）射出，但方向对准该玩家视线前方的目标点。
            --   第三人称视角下：箭从武器飞出，轨迹朝向瞄准方向，视觉自然。
            local spawnPos = self:_GetArrowPointPos()
            local forward = self:_ComputeForwardFromYawPitch(input.CameraYaw, input.CameraPitch)
            local eyePos = self:GetCameraPosition()                         -- 眼睛高度
            local targetPoint = eyePos + forward * 50.0                     -- 视线前方 50m 目标点
            local arrowDir = (targetPoint - spawnPos).normalized            -- ArrowPoint → 目标点
            Arrow.FireNetworked(self.playerId, spawnPos, arrowDir, GC.ARROW_SPEED, GC.ARROW_LIFETIME)
        end
    end
    self._wasAttackingLastTick = input.Attack
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
        -- ★ 播放死亡动画（所有客户端）
        self:_PlayDeadAnimation()
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
    -- ★ 重置死亡动画
    self:_SetDeadAnimation(false)
    -- ★ 使用 Vec3.zero() 替代 Vec3.ZERO：每个分量是独立的 Fix64.new(0)，后续 .raw 写入不会污染全局 Fix64.ZERO
    self.velocity = Vec3.zero()
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

-- ========== 箭矢相关 ==========

--- 从 CameraYaw/CameraPitch（Fix64.Raw → long）计算世界空间前方向量
--- @param yawRaw long — 水平朝向（Fix64.Raw，弧度）
--- @param pitchRaw long — 垂直视角（Fix64.Raw，弧度）
--- @return UnityEngine.Vector3 — 单位前方向量
function PlayerEntity:_ComputeForwardFromYawPitch(yawRaw, pitchRaw)
    local yaw   = CS.Fix64(yawRaw):ToFloat()
    local pitch = CS.Fix64(pitchRaw):ToFloat()
    local cosPitch = math.cos(pitch)
    return CS.UnityEngine.Vector3(
        cosPitch * math.sin(yaw),
        -math.sin(pitch),
        cosPitch * math.cos(yaw)
    )
end

--- 获取箭矢发射点世界坐标（HeroDefault/root/ArrowPoint）
--- 首次调用时查找并缓存 Transform，后续直接取 position。
--- @return UnityEngine.Vector3 — 发射点世界坐标；找不到则降级为 transform.position + (0, 1.2, 0)
function PlayerEntity:_GetArrowPointPos()
    if self._arrowPointTransform == nil then
        if self.transform ~= nil and not IsNull(self.transform) then
            local rootTrans = self.transform:Find("root")
            if rootTrans ~= nil then
                self._arrowPointTransform = rootTrans:Find("ArrowPoint")
            end
        end
    end
    if self._arrowPointTransform ~= nil and not IsNull(self._arrowPointTransform) then
        return self._arrowPointTransform.position
    end
    -- 降级：脚底 + 眼睛高度偏移
    return self.transform.position + CS.UnityEngine.Vector3(0, 1.2, 0)
end

--- 被箭矢命中时调用（Trigger 触发 → Arrow.onHitPlayer → 此处）
--- @param ownerId int — 攻击者玩家 ID
--- @param hitPoint UnityEngine.Vector3 — 命中点世界坐标（用于特效定位）
function PlayerEntity:OnHitByArrow(ownerId, hitPoint)
    -- 1. 播放受击动画（本地预览，不等服务端确认）
    self:_PlayHurtAnimation()

    -- 2. 发送 PlayerHit 到服务端（服务端权威判定伤害值）
    self:_SendPlayerHitToServer(ownerId)
end

-- 播放受击动画
function PlayerEntity:_PlayHurtAnimation()
    self:_GetOrCacheAnimator()
    local anim = self._animatorCached
    if anim ~= nil and not IsNull(anim) then
        anim:SetTrigger("IsHurt")
    end
end

-- 设置死亡动画状态（true=死亡, false=复活）
function PlayerEntity:_SetDeadAnimation(isDead)
    self:_GetOrCacheAnimator()
    local anim = self._animatorCached
    if anim ~= nil and not IsNull(anim) then
        if isDead then
            anim:SetBool("Dead", true)
        else
            -- ★ Dead 状态（KnockDown_RF01_Anim）没有退出过渡，SetBool(false) 不会让状态机离开 Dead。
            --   使用 CrossFadeInFixedTime 强制过渡到 Move（默认 locomotion 混合树状态）。
            --   0.1s 固定过渡时长确保复活动画不会突兀闪现。
            anim:SetBool("Dead", false)
            anim:CrossFadeInFixedTime("Move", 0.1, 0)
            anim:SetFloat("HSpeed", 0)
            anim:SetFloat("VSpeed", 0)
            -- ★ 重置所有动画 Bool/Trigger，避免残留状态（Jump/Roll/Fire/Skill/IsHurt）
            anim:SetBool("Jump", false)
            anim:SetBool("Roll", false)
            anim:SetBool("Fire", false)
            anim:SetBool("Skill", false)
            anim:ResetTrigger("IsHurt")
        end
    end
end

-- 播放死亡动画
function PlayerEntity:_PlayDeadAnimation()
    self:_SetDeadAnimation(true)
end

-- 获取或缓存 Animator 引用
function PlayerEntity:_GetOrCacheAnimator()
    if self._animatorCached == nil and self.gameObject ~= nil and not IsNull(self.gameObject) then
        self._animatorCached = self.gameObject:GetComponentInChildren(typeof(CS.UnityEngine.Animator))
    end
end

-- 发送受击消息到服务端
function PlayerEntity:_SendPlayerHitToServer(attackerId)
    local hitMsg = CS.GameProto.PlayerHit()
    hitMsg.AttackerId = attackerId
    hitMsg.VictimId = self.playerId
    hitMsg.DroppedCount = 0  -- 普通受击不掉水晶

    local envelope = CS.GameProto.NetMessage()
    envelope.PlayerHit = hitMsg

    -- 判断主机/客户端模式
    local hostServer = CS.HostServer.Instance
    if hostServer ~= nil and hostServer.IsGameStarted then
        -- 主机模式：直接调用服务端处理
        hostServer:SubmitHostPlayerHit(hitMsg)
    else
        -- 客户端模式：发送网络消息
        local netMgr = CS.NetMgr.Instance
        if netMgr ~= nil then
            netMgr:Send(envelope)
        end
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
