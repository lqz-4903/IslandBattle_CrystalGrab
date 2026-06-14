-- =============================================
-- Battle/CrystalManager.lua — 水晶场景管理
-- =============================================
-- 【职责】
--   1. 接收服务端 CrystalSpawn 消息，创建水晶 GameObject
--   2. 接收 CrystalPickup 消息，回收水晶
--   3. 接收 CrystalDrop 消息，在死亡玩家位置创建掉落水晶
--   4. 提供距离查询，供 PlayerController 拾取检测使用
--
-- 【创建策略】三级降级
--   1. ObjectPoolMgr.GetObj_AB → 最优（预热 + 复用，零 GC）
--   2. ABMgr 直接加载 + Instantiate → 兼容（每次 Instantiate，有 GC）
--   3. 空 GameObject 占位 → 兜底（AB 未打包时）
--
-- 【依赖】
--   - ObjectPoolMgr（C#）：对象池，GetObj_AB / PushObj / Preload_AB
--   - 水晶 Prefab 需配置：Tag="Crystal" + CrystalComponent 组件
--   - 水晶 Prefab 需打包到 AB "crystal"，资源名 "Crystal"（路径 Assets/Resources/Crystal/Crystal.prefab）
-- =============================================

local GC = require("Core.GameConst")
local Fix64 = require("Fix64")

local POOL_AB_NAME = "crystal"
local POOL_RES_NAME = "Crystal"
local POOL_PRELOAD_COUNT = 15

local CrystalManager = {
    _activeCrystals = {},
    _crystalPrefab = nil,       -- 缓存的模板（ABMgr 直接加载路径用）
    _usePool = false,           -- 对象池是否可用
    _poolWarningDone = false,   -- 只打印一次对象池警告
    _initialized = false,
    _pickupRange = 0.8,
}

-- ========== 初始化 ==========

function CrystalManager:Init()
    if self._initialized then return end
    self._initialized = true

    -- 1. 加载模板 prefab（通过 ABMgr，已验证可用）
    --    注：ABMgr:LoadRes 返回的是已实例化的 GameObject，不是原始 prefab
    --       但 Unity 的 Instantiate 对实例化对象也能工作（会再次实例化）
    local ok, err = pcall(function()
        self._crystalPrefab = ABMgr:LoadRes(POOL_AB_NAME, POOL_RES_NAME,
            typeof(CS.UnityEngine.GameObject))
        if self._crystalPrefab ~= nil and not IsNull(self._crystalPrefab) then
            self._crystalPrefab:SetActive(false)  -- 模板保持不激活
        end
    end)
    if not ok then
        print("[CrystalManager] ABMgr 加载异常: " .. tostring(err))
    end

    -- 2. 尝试预热对象池（非致命，失败则走 ABMgr 直接路径）
    local poolOk, poolErr = pcall(function()
        CS.ObjectPoolMgr.Instance:Preload_AB(POOL_AB_NAME, POOL_RES_NAME, POOL_PRELOAD_COUNT)
    end)
    if poolOk then
        self._usePool = true
        print("[CrystalManager] 对象池已预热 " .. POOL_PRELOAD_COUNT .. " 个水晶")
    else
        self._usePool = false
        print("[CrystalManager] 对象池不可用，使用 ABMgr 直接加载路径 → " .. tostring(poolErr))
    end

    -- ★ 3. 注册水晶触发拾取回调（C# CrystalComponent.OnTriggerEnter → Lua）
    --    回调为无参 System.Action，数据通过静态字段 LastCrystalId / LastPlayerGo 传递
    CS.CrystalComponent.OnPlayerEnterTrigger = function()
        local crystalId = CS.CrystalComponent.LastCrystalId
        local playerGo = CS.CrystalComponent.LastPlayerGo
        self:_HandleTriggerEnter(crystalId, playerGo)
    end

    if self._crystalPrefab ~= nil and not IsNull(self._crystalPrefab) then
        print("[CrystalManager] 初始化完成（prefab 已加载 + 触发拾取已注册）")
    else
        print("[CrystalManager] 初始化完成（★ prefab 未加载，使用空占位对象 + 触发拾取已注册 — 请构建 crystal AB 包）")
    end
end

-- ========== 水晶生命周期 ==========

--- 内部：设置水晶 GameObject 的通用属性（位置/激活/触发碰撞体/组件ID）
local function _SetupCrystalGo(go, worldPos, crystalId)
    -- ★ 统一命名为池 key，确保 PushObj 能放入正确抽屉
    go.name = POOL_RES_NAME
    go.transform.position = worldPos
    go:SetActive(true)

    -- ★ 水晶触发范围检测（代替 PlayerController._ProcessInteract 距离遍历）
    --   角色 CharacterController 进入 SphereCollider 时 → OnTriggerEnter → Lua 拾取回调
    local sphere = go:GetComponent(typeof(CS.UnityEngine.SphereCollider))
    if sphere == nil or IsNull(sphere) then
        sphere = go:AddComponent(typeof(CS.UnityEngine.SphereCollider))
    end
    sphere.isTrigger = true
    sphere.radius = 0.8       -- 拾取范围（与 _pickupRange 一致）
    sphere.center = CS.UnityEngine.Vector3.zero

    -- 设置 CrystalComponent 的水晶 ID
    local comp = go:GetComponent(typeof(CS.CrystalComponent))
    if comp ~= nil and not IsNull(comp) then
        comp.CrystalId = crystalId
    end
end

--- 触发拾取回调处理（由 C# CrystalComponent.OnTriggerEnter 调用）
--- @param crystalId int — 触发的水晶 ID
--- @param playerGo GameObject — 进入触发范围的角色 GameObject
function CrystalManager:_HandleTriggerEnter(crystalId, playerGo)
    -- 只有本地玩家才能发起拾取请求（远程玩家的触发器事件被忽略）
    local pm = PlayerManager.GetInstance()
    local localPlayer = pm:GetLocalPlayer()
    if localPlayer == nil then return end
    if localPlayer.gameObject ~= playerGo then return end

    -- 检查水晶是否还存在（防止重复拾取）
    if self._activeCrystals[crystalId] == nil then return end

    -- ★ 立即回收水晶（客户端预测 — 不等服务端确认）
    self:RemoveCrystal(crystalId)

    -- 发送拾取请求到服务端（由服务端权威验证并广播结果）
    local PC = require("Battle.PlayerController")
    PC:_SendCrystalPickup(crystalId)
end

--- 在场景中创建水晶 GameObject
--- @param crystalId int
--- @param posXRaw long — Fix64.Raw (sfixed64)
--- @param posYRaw long
--- @param posZRaw long
--- @return UnityEngine.GameObject|nil
function CrystalManager:SpawnCrystal(crystalId, posXRaw, posYRaw, posZRaw)
    if self._activeCrystals[crystalId] ~= nil then
        return nil
    end

    local posX = Fix64.toFloat(Fix64.new(posXRaw or 0))
    local posY = Fix64.toFloat(Fix64.new(posYRaw or 0))
    local posZ = Fix64.toFloat(Fix64.new(posZRaw or 0))
    local worldPos = CS.UnityEngine.Vector3(posX, posY, posZ)

    local go = nil

    -- 尝试 1：对象池
    if self._usePool then
        local ok, err = pcall(function()
            go = CS.ObjectPoolMgr.Instance:GetObj_AB(POOL_AB_NAME, POOL_RES_NAME)
        end)
        if not ok then
            if not self._poolWarningDone then
                self._poolWarningDone = true
                print("[CrystalManager] 对象池获取失败: " .. tostring(err))
            end
            self._usePool = false  -- 后续走降级路径
        end
    end

    if go ~= nil and not IsNull(go) then
        _SetupCrystalGo(go, worldPos, crystalId)
    end

    -- 尝试 2：模板 Instantiate（ABMgr 已缓存 prefab）
    if go == nil or IsNull(go) then
        if self._crystalPrefab ~= nil and not IsNull(self._crystalPrefab) then
            local ok, err = pcall(function()
                go = CS.UnityEngine.Object.Instantiate(self._crystalPrefab, worldPos,
                    CS.UnityEngine.Quaternion.identity)
                if go ~= nil and not IsNull(go) then
                    -- ★ 重命名为池 key，确保 PushObj 能放入正确抽屉
                    go.name = POOL_RES_NAME
                    _SetupCrystalGo(go, worldPos, crystalId)
                end
            end)
            if not ok then
                print("[CrystalManager] Instantiate 异常: " .. tostring(err))
            end
        end
    end

    -- 尝试 3：空 GameObject 占位
    if go == nil or IsNull(go) then
        -- 只在第一个水晶时打印一次警告
        if not self._poolWarningDone then
            self._poolWarningDone = true
            print("[CrystalManager] ★ 所有路径失败，使用空占位对象。请确认 crystal AB 包已构建。")
        end
        go = CS.UnityEngine.GameObject("Crystal_" .. crystalId)
        _SetupCrystalGo(go, worldPos, crystalId)
    end

    self._activeCrystals[crystalId] = go
    return go
end

--- 回收水晶
--- @param crystalId int
function CrystalManager:RemoveCrystal(crystalId)
    local go = self._activeCrystals[crystalId]
    if go ~= nil and not IsNull(go) then
        -- ☆ 始终尝试归还对象池（名字已在 _SetupCrystalGo 中确保为 POOL_RES_NAME）
        --   即使 Preload_AB 失败，PushObj 也会在首次调用时自动创建抽屉
        pcall(function()
            CS.ObjectPoolMgr.Instance:PushObj(go)
        end)
    end
    self._activeCrystals[crystalId] = nil
end

--- 清除所有水晶
function CrystalManager:Clear()
    for cid, go in pairs(self._activeCrystals) do
        if go ~= nil and not IsNull(go) then
            -- ☆ 始终尝试归还对象池
            pcall(function()
                CS.ObjectPoolMgr.Instance:PushObj(go)
            end)
        end
    end
    self._activeCrystals = {}
end

-- ========== 查询接口 ==========

function CrystalManager:GetCrystalPosition(crystalId)
    local go = self._activeCrystals[crystalId]
    if go ~= nil and not IsNull(go) then
        return go.transform.position
    end
    return nil
end

function CrystalManager:ForEach()
    return pairs(self._activeCrystals)
end

function CrystalManager:GetPickupRange()
    return self._pickupRange
end

-- ========== 单例 ==========

local instance = nil

function CrystalManager.GetInstance()
    if instance == nil then
        instance = setmetatable({}, { __index = CrystalManager })
        instance._activeCrystals = {}
    end
    return instance
end

return CrystalManager
