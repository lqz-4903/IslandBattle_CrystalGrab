-- =============================================
-- Core/NetworkEventMgr.lua — 网络游戏事件监听分发
-- =============================================
-- 【职责】
--   通过 C# LuaEventBridge 桥接，监听所有游戏网络事件，
--   并转发到对应的 Lua 侧处理器。
--
-- 【事件流向】
--   C# EventCenter → LuaEventBridge.OnXxx → 本模块 → PlayerManager / 其他模块
-- =============================================

local PlayerManager = require("Core.PlayerManager")
local GC = require("Core.GameConst")

local NetworkEventMgr = {}
NetworkEventMgr.__index = NetworkEventMgr

-- ========== 状态 ==========
NetworkEventMgr.initialized = false

-- ========== 初始化 ==========

--- 注册所有网络事件回调到 C# LuaEventBridge
function NetworkEventMgr:Init()
    if self.initialized then return end
    self.initialized = true

    -- 确保 C# 桥接已初始化
    CS.LuaEventBridge.Initialize()

    local bridge = CS.LuaEventBridge

    -- 游戏开始
    bridge.OnGameStart = function(msg)
        self:_OnGameStart(msg)
    end

    -- 水晶生成
    bridge.OnCrystalSpawn = function(msg)
        self:_OnCrystalSpawn(msg)
    end

    -- 水晶拾取
    bridge.OnCrystalPickup = function(msg)
        self:_OnCrystalPickup(msg)
    end

    -- 玩家受击
    bridge.OnPlayerHit = function(msg)
        self:_OnPlayerHit(msg)
    end

    -- 玩家坠落
    bridge.OnPlayerFall = function(msg)
        self:_OnPlayerFall(msg)
    end

    -- 玩家重生
    bridge.OnPlayerRespawn = function(msg)
        self:_OnPlayerRespawn(msg)
    end

    -- 游戏结束
    bridge.OnGameEnd = function(msg)
        self:_OnGameEnd(msg)
    end

    -- 玩家离线
    bridge.OnPlayerOffline = function(msg)
        self:_OnPlayerOffline(msg)
    end

    print("[NetworkEventMgr] 所有网络事件已注册")
end

--- 关闭并注销回调
function NetworkEventMgr:Shutdown()
    if not self.initialized then return end
    self.initialized = false

    CS.LuaEventBridge.Shutdown()
    print("[NetworkEventMgr] 已关闭")
end

-- ========== 事件处理器 ==========

--- 游戏开始（客户端路径）
--- ★ 注：真正的初始化在 Main.lua InitGame() 统一完成，
---    此回调仅用于日志记录和调试
--- @param msg GameStart（protobuf）
function NetworkEventMgr:_OnGameStart(msg)
    print("[NetworkEventMgr] 游戏开始 种子=" .. msg.RandomSeed .. " 时长=" .. msg.GameDuration ..
          " 帧率=" .. (msg.TickRate or 15))
    -- PlayerManager 初始化由 Main.lua InitGame() 统一处理，此处不重复操作
end

--- 水晶生成
function NetworkEventMgr:_OnCrystalSpawn(msg)
    local crystalId = msg.CrystalId
    local posX = msg.PosX
    local posY = msg.PosY
    local posZ = msg.PosZ

    -- TODO: 交由 CrystalManager 在场景中创建水晶 GameObject
    -- CrystalManager:SpawnCrystal(crystalId, posX, posY, posZ)
end

--- 水晶拾取
function NetworkEventMgr:_OnCrystalPickup(msg)
    local crystalId = msg.CrystalId
    local playerId = msg.PlayerId
    local newScore = msg.NewScore

    -- TODO: 交由 CrystalManager 移除水晶 GameObject
    -- CrystalManager:RemoveCrystal(crystalId)

    -- 更新玩家分数
    local pm = PlayerManager.GetInstance()
    pm:OnServerCrystalPickup(playerId, crystalId, newScore)
end

--- 玩家受击
function NetworkEventMgr:_OnPlayerHit(msg)
    local attackerId = msg.AttackerId
    local victimId   = msg.VictimId
    local droppedCount = msg.DroppedCount

    -- HP 由服务端权威维护，客户端根据事件扣减本地显示
    local pm = PlayerManager.GetInstance()
    -- 服务端不直接下发 newHp，客户端自行 -1
    pm:OnServerPlayerHit(attackerId, victimId, droppedCount)
end

--- 玩家坠落
function NetworkEventMgr:_OnPlayerFall(msg)
    local playerId     = msg.PlayerId
    local droppedCount = msg.DroppedCount

    local pm = PlayerManager.GetInstance()
    pm:OnServerPlayerFall(playerId, droppedCount)
end

--- 玩家重生
function NetworkEventMgr:_OnPlayerRespawn(msg)
    local playerId = msg.PlayerId
    local posX = msg.PosX
    local posY = msg.PosY
    local posZ = msg.PosZ

    local pm = PlayerManager.GetInstance()
    pm:OnServerPlayerRespawn(playerId, posX, posY, posZ)
end

--- 游戏结束
function NetworkEventMgr:_OnGameEnd(msg)
    local winnerId   = msg.WinnerId
    local winnerName = msg.WinnerName

    print("[NetworkEventMgr] 游戏结束 胜者=" .. winnerName .. " (ID=" .. winnerId .. ")")

    -- GameOverPanel 已在 GameTimerManager 监听 GameEnd 事件后自动弹出
    -- 这里只需清理本地状态
    local pm = PlayerManager.GetInstance()
    -- 不直接 Shutdown，保留数据供 UI 查询
end

--- 玩家离线
function NetworkEventMgr:_OnPlayerOffline(msg)
    local playerId   = msg.PlayerId
    local playerName = msg.PlayerName

    print("[NetworkEventMgr] 玩家离线 " .. playerName .. " (ID=" .. playerId .. ")")
    local pm = PlayerManager.GetInstance()
    pm:RemovePlayer(playerId)
end

return NetworkEventMgr
