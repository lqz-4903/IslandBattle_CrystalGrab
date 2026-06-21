-- 继承BasePanel
BasePanel:subClass("BeginPanel")

-- 面板名称（对应AB包中的预制体名称）
BeginPanel.panelName = "BeginPanel"

-- 单例引用
BeginPanel.instance = nil

-- 显示面板
function BeginPanel:Show()
    if self.instance == nil then
        self.instance = self
    end
    self:ShowMe(self.panelName)
    -- 首次显示时绑定按钮事件
    if self.isInitEvent == false then
        self:BindEvents()
        self.isInitEvent = true
    end
    -- 加载并显示玩家名
    self:LoadPlayerName()
end

-- 隐藏并销毁面板
function BeginPanel:Hide()
    self:DestroyPanel()
    self.instance = nil
end

-- 从JSON加载玩家名并显示到txtName
function BeginPanel:LoadPlayerName()
    local txtName = self:GetControl("txtName", "Text")
    if txtName ~= nil then
        txtName.text = PlayerData.GetName()
    end
end

-- 绑定所有按钮事件
function BeginPanel:BindEvents()
    -- 按钮1 - 开始：跳转到选房面板
    local btn1 = self:GetControl("btnBegin", "Button")
    if btn1 ~= nil then
        btn1.onClick:AddListener(function()
            self:Hide()
            ChooseRoomPanel:Show()
        end)
    end

    -- 按钮2 - 玩法说明：跳转PlayExplainPanel
    local btn2 = self:GetControl("btnPlayExplain", "Button")
    if btn2 ~= nil then
        btn2.onClick:AddListener(function()
            self:Hide()
            PlayExplainPanel:Show()
        end)
    end

    -- 按钮3 - 设置：跳转到设置面板
    local btn3 = self:GetControl("btnSetting", "Button")
    if btn3 ~= nil then
        btn3.onClick:AddListener(function()
            self:Hide()
            SettingPanel:Show()
        end)
    end

    -- 按钮4 - 关于：跳转AboutPanel
    local btn4 = self:GetControl("btnAbout", "Button")
    if btn4 ~= nil then
        btn4.onClick:AddListener(function()
            self:Hide()
            AboutPanel:Show()
        end)
    end

    -- 按钮5 - 退出游戏
    local btn5 = self:GetControl("btnQuit", "Button")
    if btn5 ~= nil then
        btn5.onClick:AddListener(function()
            CS.UnityEngine.Application.Quit()
        end)
    end

    -- 改名按钮：弹出ChangeNamePanel
    local btnChange = self:GetControl("btnChange", "Button")
    if btnChange ~= nil then
        btnChange.onClick:AddListener(function()
            ChangeNamePanel:Popup()
        end)
    end
end
