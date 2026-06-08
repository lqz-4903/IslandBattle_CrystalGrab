print("准备就绪")
-- 初始化所有准备好的类别名
require("InitClass")

-- 场景跳转标志
showCreateRoomPanel = false

-- 数据工具
require("Libs.PlayerData")

-- 核心面板逻辑
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

-- 场景初始化（由GameMgr.Start每场景调用，也在此首次执行）
function OnSceneLoaded()
    local sceneName = CS.UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
    print("OnSceneLoaded: " .. sceneName)
    if sceneName == "BeginScene" then
        -- BeginBKPanel 始终作为背景最先显示
        BeginBKPanel:Show()
        if showCreateRoomPanel then
            showCreateRoomPanel = false
            -- 从游戏返回，只显示 CreateRoomPanel（BeginBKPanel 已在上面显示）
            CreateRoomPanel:Show()
        else
            -- 正常进入，显示 BeginPanel
            BeginPanel:Show()
        end
    elseif sceneName == "GameScene" then
        -- 清理 BeginScene 残留面板
        BeginPanel:Hide()
        BeginBKPanel:Hide()
        CreateRoomPanel:Hide()
        TipPanel:Close()
        GamePanel:Show()
    else
        print("未知场景: " .. sceneName)
    end
end

OnSceneLoaded()



