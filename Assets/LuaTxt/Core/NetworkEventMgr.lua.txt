-- =============================================
-- Core/NetworkEventMgr.lua — 网络游戏事件监听分发
-- =============================================
-- 【职责】
--   通过 C# LuaEventBridge 桥接，监听所有游戏网络事件，
--   并转发到对应的 Lua 侧处理器。
--
-- 【事件流向】
--   C# EventCenter → LuaEventBridge.OnXxx → 本模块 → PlayerManager / CrystalManager
-- =============================================

local PlayerManager = require("Core.PlayerManager")
local CrystalManager = require("Battle.CrystalManager")
local GC = require("Core.GameConst")
local Fix64 = require("Fix64")

local NetworkEventMgr = {}
NetworkEventMgr.__index = NetworkEventMgr

-- ========== 状态 ==========
NetworkEventMgr.initialized = false
NetworkEventMgr._canAttack = true       -- ★ 全程可攻击（无阶段限制）

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

    -- ★ 阶段切换（已弃用——全程无阶段限制，服务端不再广播 PhaseSwitch）
    bridge.OnPhaseSwitch = function()
        -- 保留接口兼容，不做任何处理
    end

    -- ★ 死亡掉落
    bridge.OnCrystalDrop = function(msg)
        self:_OnCrystalDrop(msg)
    end

    print("[NetworkEventMgr] 所有网络事件已注册（含水晶+阶段）")
end

--- 关闭并注销回调
function NetworkEventMgr:Shutdown()
    if not self.initialized then return end
    self.initialized = false
    self._canAttack = false
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
          " 帧率=" .. (msg.TickRate or 30))
end

--- ★ 死亡掉落
--- @param msg CrystalDrop（protobuf）：Count, PlayerId, NewScore
function NetworkEventMgr:_OnCrystalDrop(msg)
    local dropCount = msg.Count or 0
    local playerId = msg.PlayerId
    local newScore = msg.NewScore

    if dropCount <= 0 then return end

    -- ★ 掉落水晶由服务端 SpawnCrystalAt → CrystalSpawn 广播创建
    --   客户端在 _OnCrystalSpawn 中自动处理，此处只需更新分数
    local pm = PlayerManager.GetInstance()
    local player = pm.players[playerId]
    if player ~= nil then
        player:SetScore(newScore)
    end

    print(string.format("[NetworkEventMgr] 玩家%d掉落%d颗水晶 新分数=%d",
        playerId, dropCount, newScore))
end

--- 水晶生成
function NetworkEventMgr:_OnCrystalSpawn(msg)
    local crystalId = msg.CrystalId
    local posXRaw = msg.PosX
    local posYRaw = msg.PosY
    local posZRaw = msg.PosZ

    local cm = CrystalManager.GetInstance()
    cm:SpawnCrystal(crystalId, posXRaw, posYRaw, posZRaw)
end

--- 水晶拾取
function NetworkEventMgr:_OnCrystalPickup(msg)
    local crystalId = msg.CrystalId
    local playerId = msg.PlayerId
    local newScore = msg.NewScore

    -- 移除水晶
    local cm = CrystalManager.GetInstance()
    cm:RemoveCrystal(crystalId)

    -- 更新玩家分数
    local pm = PlayerManager.GetInstance()
    pm:OnServerCrystalPickup(playerId, crystalId, newScore)
end

--- 玩家受击
function NetworkEventMgr:_OnPlayerHit(msg)
    local attackerId   = msg.AttackerId
    local victimId     = msg.VictimId
    local damage       = msg.Damage or 0
    local newHp        = msg.NewHp or 0

    local pm = PlayerManager.GetInstance()
    pm:OnServerPlayerHit(attackerId, victimId, damage, newHp)
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

    -- 清理水晶
    CrystalManager.GetInstance():Clear()
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
