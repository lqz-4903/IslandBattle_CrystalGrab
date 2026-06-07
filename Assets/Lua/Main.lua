print("准备就绪")
-- 初始化所有准备好的类别名
require("InitClass")

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

-- 场景初始化（由GameEntry.Start每场景调用，也在此首次执行）
function OnSceneLoaded()
    local sceneName = CS.UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
    print("OnSceneLoaded: " .. sceneName)
    if sceneName == "BeginScene" then
        BeginBKPanel:Show()
        BeginPanel:Show()
    elseif sceneName == "GameScene" then
        GamePanel:Show()
    else
        print("未知场景: " .. sceneName)
    end
end

OnSceneLoaded()


BeginBKPanel:Show()
BeginPanel:Show()








