-- 继承BasePanel
BasePanel:subClass("AboutPanel")

-- 面板名称（对应AB包中的预制体名称）
AboutPanel.panelName = "AboutPanel"

-- 单例引用
AboutPanel.instance = nil

-- 显示面板
function AboutPanel:Show()
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
function AboutPanel:Hide()
    self:DestroyPanel()
    self.instance = nil
end

-- 绑定所有按钮事件
function AboutPanel:BindEvents()
    -- 关闭按钮：回到BeginPanel
    local btnClose = self:GetControl("btnClose", "Button")
    if btnClose ~= nil then
        btnClose.onClick:AddListener(function()
            self:Hide()
            BeginPanel:Show()
        end)
    end
end
