-- =============================================
-- Battle/Arrow.lua — 箭矢投射物模块
-- =============================================
-- 【职责】
--   管理箭矢的完整生命周期：对象池获取 → 飞行 → 超时回收。
--   Phase 1：纯视觉飞行，无碰撞检测。
--   Phase 2：Trigger 碰撞 → OnTriggerEnterPlayer → 受击动画 + 回收。
--
-- 【对象池】
--   使用 ObjectPoolMgr（与 CrystalManager 同模式）。
--   池 key = "ArrowDefault"（go.name 与 GetObj_AB resName 一致）。
--
-- 【用法】
--   Arrow.Init()                -- 游戏开始时预加载池 + 注册 Update
--   Arrow.FireLocal(...)        -- 本地即时发射（摄像机位置）
--   Arrow.FireNetworked(...)    -- 网络同步发射（PlayerEntity 位置）
--   Arrow.ClearAll()            -- 游戏结束时回收全部 + 解绑回调
-- =============================================

local GC = require("Core.GameConst")

local Arrow = {}

-- ========== 活跃箭矢列表 ==========
-- { go, ownerId, forward, speed, elapsed, lifetime, isLocal }
local _activeArrows = {}

-- ========== 受击回调（Phase 2：Trigger 进入时调用）==========
-- function(arrow, targetPlayerId)
Arrow.onHitPlayer = nil

-- ========== 初始化 ==========

--- 预加载对象池 + 注册全局 Update。
--- 可多次调用安全（_poolInitialized 防止重复 Preload，_updateId 防止重复注册）。
function Arrow.Init()
    if not Arrow._poolInitialized then
        local ok, err = pcall(function()
            CS.ObjectPoolMgr.Instance:Preload_AB(GC.ARROW_POOL_AB, GC.ARROW_POOL_RES, GC.ARROW_POOL_SIZE)
        end)
        if ok then
            Arrow._poolInitialized = true
            print("[Arrow] 对象池预加载完成: " .. GC.ARROW_POOL_AB .. "/" .. GC.ARROW_POOL_RES .. " x" .. GC.ARROW_POOL_SIZE)
        else
            print("[Arrow] 对象池预加载失败: " .. tostring(err))
        end
    end
    if Arrow._updateId == nil then
        Arrow._updateId = RegisterUpdate(Arrow.OnUpdate)
        print("[Arrow] Update 已注册, id=" .. Arrow._updateId)
    end
end

-- ========== 发射接口 ==========

--- 本地即时发射（摄像机位置，零延迟视觉反馈）
--- @param ownerId int — 发射者玩家 ID
--- @param spawnPos UnityEngine.Vector3 — 发射点世界坐标
--- @param forward UnityEngine.Vector3 — 发射方向（单位向量）
--- @param speed number — 飞行速度（米/秒）
--- @param lifetime number — 存活时间（秒）
function Arrow.FireLocal(ownerId, spawnPos, forward, speed, lifetime)
    local arrow = _createArrow(ownerId, spawnPos, forward, speed, lifetime)
    if arrow then
        arrow.isLocal = true
    end
end

--- 网络同步发射（PlayerEntity 位置，InputTick 驱动，所有客户端一致）
--- @param ownerId int
--- @param spawnPos UnityEngine.Vector3
--- @param forward UnityEngine.Vector3
--- @param speed number
--- @param lifetime number
function Arrow.FireNetworked(ownerId, spawnPos, forward, speed, lifetime)
    _createArrow(ownerId, spawnPos, forward, speed, lifetime)
end

-- ========== 内部创建 ==========

--- @return table|nil — arrow 数据表，失败返回 nil
function _createArrow(ownerId, spawnPos, forward, speed, lifetime)
    local go = CS.ObjectPoolMgr.Instance:GetObj_AB(GC.ARROW_POOL_AB, GC.ARROW_POOL_RES)
    if IsNull(go) then
        return nil
    end

    -- ★ 对象池 key 一致性：PushObj 用 obj.name 做 key，必须与 GetObj_AB 的 resName 一致
    go.name = GC.ARROW_POOL_RES
    go.transform.position = spawnPos
    go.transform.forward = forward

    -- ★ 禁用所有 Collider，避免意外物理碰撞（问题 10）
    --    Phase 2 改为 IsTrigger=true 以支持 Trigger 受击检测
    local colliders = go:GetComponentsInChildren(typeof(CS.UnityEngine.Collider))
    for i = 0, colliders.Length - 1 do
        colliders[i].enabled = false
    end

    go:SetActive(true)

    local arrow = {
        go       = go,
        ownerId  = ownerId,
        forward  = forward,
        speed    = speed,
        elapsed  = 0,
        lifetime = lifetime,
        isLocal  = false,
    }
    table.insert(_activeArrows, arrow)
    return arrow
end

-- ========== 每帧更新 ==========

--- 由全局 Update 驱动（60fps）
--- @param dt float — Unity Time.deltaTime
function Arrow.OnUpdate(dt)
    for i = #_activeArrows, 1, -1 do
        local arrow = _activeArrows[i]

        -- 保护：GameObject 被意外销毁
        if IsNull(arrow.go) then
            table.remove(_activeArrows, i)
            goto continue
        end

        -- 飞行
        arrow.go.transform.position = arrow.go.transform.position + arrow.forward * arrow.speed * dt
        arrow.elapsed = arrow.elapsed + dt

        -- ★ Phase 2 预留：Trigger 碰撞检测入口
        --   if _checkHit(arrow) then
        --       _onTriggerHit(arrow)
        --       goto continue
        --   end

        -- 超时回收
        if arrow.elapsed >= arrow.lifetime then
            _recycleArrow(arrow)
            table.remove(_activeArrows, i)
        end

        ::continue::
    end
end

-- ========== Phase 2：Trigger 受击入口 ==========

--- 由 C# ArrowTrigger.OnTriggerEnter 回调调用。
--- 当前预留，后续在箭矢 GameObject 上挂 ArrowTrigger.cs 组件。
--- @param arrowGo GameObject — 箭矢 GameObject
--- @param targetPlayerId int — 被命中玩家 ID
function Arrow.OnTriggerEnterPlayer(arrowGo, targetPlayerId)
    -- 找到对应的 arrow 数据
    local arrow = nil
    local arrowIdx = nil
    for i, a in ipairs(_activeArrows) do
        if a.go == arrowGo then
            arrow = a
            arrowIdx = i
            break
        end
    end
    if arrow == nil then
        return
    end

    -- 1. 外部回调：角色播放受击动画
    if Arrow.onHitPlayer then
        Arrow.onHitPlayer(arrow, targetPlayerId)
    end

    -- 2. 回收箭矢到对象池
    _recycleArrow(arrow)
    table.remove(_activeArrows, arrowIdx)
end

-- ========== 清理 ==========

--- 回收单个箭矢到对象池
function _recycleArrow(arrow)
    if not IsNull(arrow.go) then
        arrow.go:SetActive(false)
        CS.ObjectPoolMgr.Instance:PushObj(arrow.go)
    end
end

--- 游戏结束时调用：回收所有活跃箭矢 + 注销 Update + 清除回调
function Arrow.ClearAll()
    if Arrow._updateId then
        UnregisterUpdate(Arrow._updateId)
        Arrow._updateId = nil
    end
    for i = #_activeArrows, 1, -1 do
        _recycleArrow(_activeArrows[i])
    end
    _activeArrows = {}
    Arrow.onHitPlayer = nil
    print("[Arrow] 已清理全部箭矢")
end

-- ========== 调试 ==========

--- 获取当前活跃箭矢数量（调试用）
function Arrow.GetActiveCount()
    return #_activeArrows
end

return Arrow
