-- 继承BasePanel
BasePanel:subClass("PlayExplainPanel")

-- 面板名称（对应AB包中的预制体名称）
PlayExplainPanel.panelName = "PlayExplainPanel"

-- 单例引用
PlayExplainPanel.instance = nil

-- 显示面板
function PlayExplainPanel:Show()
    if self.instance == nil then
        self.instance = self
    end
    self:ShowMe(self.panelName)
    if self.isInitEvent == false then
        self:BindEvents()
        self.isInitEvent = true
    end
end

-- 隐藏并销毁面板
function PlayExplainPanel:Hide()
    if self.panelObj ~= nil then
        self:StopFade()
        GameObject.Destroy(self.panelObj)
        self.panelObj = nil
        self.canvasGroup = nil
        self.controls = {}
        self.isInitEvent = false
    end
    self.instance = nil
end

-- 绑定所有按钮事件
function PlayExplainPanel:BindEvents()
    -- 关闭按钮：回到BeginPanel
    local btnClose = self:GetControl("btnClose", "Button")
    if btnClose ~= nil then
        btnClose.onClick:AddListener(function()
            self:Hide()
            BeginPanel:Show()
        end)
    end
end
