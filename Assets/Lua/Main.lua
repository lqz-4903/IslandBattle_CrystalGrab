print("准备就绪")
-- 初始化所有准备好的类别名
require("InitClass")

-- 场景跳转标志
showCreateRoomPanel = false

-- 数据工具
require("Libs.PlayerData")

-- ========== 游戏核心模块 ==========
GameConst        = require("Core.GameConst")
PlayerEntity     = require("Core.PlayerEntity")
PlayerManager    = require("Core.PlayerManager")
NetworkEventMgr  = require("Core.NetworkEventMgr")

-- ========== 战斗模块 ==========
InputHandler     = require("Battle.InputHandler")
PlayerController = require("Battle.PlayerController")

-- ========== UI 面板 ==========
require("UI.BasePanel")
require("UI.BeginBKPanel")
require("UI.ChooseRoomPanel")
require("UI.SettingPanel")
require("UI.TipPanel")
require("UI.CreateRoomPanel")
require("UI.JoinRoomPanel")
require("UI.AboutPanel")
require("UI.ChangeNamePanel")
require("UI.PlayExplainPanel")
require("UI.BeginPanel")
require("UI.GamePanel")
require("UI.GameOverPanel")

-- ========== 游戏启动 ==========
-- 统一流程：房主/客户端都在切到 GameScene 后，由 OnSceneLoaded 触发 InitGame
-- 需要的数据通过 _G.pendingGameStart 和 _G.lastPlayerList 传递

--- 真正的游戏初始化（在 GameScene 加载完成后调用）
local function InitGame()
    local gameData = _G.pendingGameStart
    if gameData == nil then
        print("[Main] InitGame 跳过：无待处理的游戏数据")
        return
    end
    _G.pendingGameStart = nil

    local playerList = _G.lastPlayerList
    if playerList == nil or playerList.Players == nil or playerList.Players.Count == 0 then
        print("[Main] InitGame 警告：PlayerList 为空")
    end

    local localPlayerId = _G.localPlayerId or 1
    local localPlayerName = _G.localPlayerName or "Player"

    -- ★ 修复 Bug2（动画错位）：验证 localPlayerId 确实在 PlayerList 中
    --   若 _G.localPlayerId 因未知原因未设置或错误，客户端可能默认为 1（主机 ID），
    --   导致客户端将自身输入作为主机输入发送，主机模型被客户端操控，客户端自身模型不响应。
    if _G.localPlayerId == nil then
        print("[Main] ★ 警告：_G.localPlayerId 未设置，默认使用 1！这会导致客户端以主机身份运行！")
    end
    if playerList ~= nil and playerList.Players ~= nil and playerList.Players.Count > 0 then
        local found = false
        for i = 0, playerList.Players.Count - 1 do
            if playerList.Players[i].PlayerId == localPlayerId then
                found = true
                break
            end
        end
        if not found then
            print("[Main] ★ 严重错误：localPlayerId=" .. localPlayerId .. " 不在 PlayerList 中！")
        end
    end

    print("[Main] ====== 初始化游戏 ======")
    print("[Main] 种子=" .. (gameData.randomSeed or 0) ..
          " 时长=" .. (gameData.gameDuration or 0) ..
          " 本地玩家 ID=" .. localPlayerId .. " (" .. localPlayerName .. ")")

    -- 1. 判断是否主机模式（必须在创建 TickExecutor 之前确定）
    local isHost = (CS.KcpMgr.Instance.ClientConv == 0)

    -- 2. ★ 客户端模式：创建 TickExecutor 来处理网络帧（主机模式 HostServer 已创建）
    if not isHost then
        local go = CS.UnityEngine.GameObject("ClientTickExecutor")
        CS.UnityEngine.Object.DontDestroyOnLoad(go)
        go:AddComponent(typeof(CS.TickExecutor))
        -- 初始化为客户端模式
        local te = go:GetComponent(typeof(CS.TickExecutor))
        te:Init(false, gameData.tickRate or 30)
        print("[Main] 客户端 TickExecutor 已创建")
    end

    -- 3. 初始化网络事件监听（水晶、受击、坠落、阶段切换等）
    NetworkEventMgr:Init()

    -- 3.5 ★ 初始化水晶管理器（必须在 NetworkEventMgr 之后，因为 NetworkEventMgr 会触发水晶生成事件）
    local CrystalManager = require("Battle.CrystalManager")
    CrystalManager.GetInstance():Init()

    -- 4. 初始化玩家管理器 + 注册 TickExecutor 回调
    local pm = PlayerManager.GetInstance()
    pm:Init()

    -- 5. 根据 PlayerList 生成所有玩家（含出生点分配）
    if playerList ~= nil and playerList.Players ~= nil and playerList.Players.Count > 0 then
        pm:SpawnAllPlayers(playerList, localPlayerId)
    else
        print("[Main] 警告：无 PlayerList，仅生成本地玩家")
        pm:SpawnLocalOnly(localPlayerId, localPlayerName)
    end

    -- 6. 启动本地玩家控制器（输入采集 + 移动 + 摄像机）
    PlayerController:Init(localPlayerId, isHost)

    -- 7. 注册测试快捷键（T=全部测试, Y=阻塞性测试）
    require("Test.TestRunner").RegisterHotkey()

    print("[Main] ====== 游戏初始化完成 ======")
end

-- ========== 场景初始化 ==========

function OnSceneLoaded()
    local sceneName = CS.UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
    print("OnSceneLoaded: " .. sceneName)
    if sceneName == "BeginScene" then
        -- 清理游戏运行时
        if PlayerController.initialized then
            PlayerController:Shutdown()
            PlayerManager.GetInstance():Shutdown()
            NetworkEventMgr:Shutdown()
        end
        -- ★ 清除上一次游戏的数据，防止残留影响新游戏
        _G.pendingGameStart = nil
        _G.lastPlayerList = nil

        -- BeginBKPanel 始终作为背景最先显示
        BeginBKPanel:Show()
        if showCreateRoomPanel then
            showCreateRoomPanel = false
            CreateRoomPanel:Show()
        else
            BeginPanel:Show()
        end
    elseif sceneName == "GameScene" then
        -- 清理 BeginScene 残留面板
        BeginPanel:Hide()
        BeginBKPanel:Hide()
        CreateRoomPanel:Hide()
        TipPanel:Close()
        GamePanel:Show()

        -- ★ 统一入口：场景加载完成后初始化游戏
        InitGame()
    else
        print("未知场景: " .. sceneName)
    end
end

OnSceneLoaded()
