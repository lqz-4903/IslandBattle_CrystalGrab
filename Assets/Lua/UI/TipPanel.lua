-- 通用提示弹窗（弹出式面板，不销毁下层面板）
BasePanel:subClass("TipPanel")

TipPanel.panelName = "TipPanel"
TipPanel.instance = nil

-- 弹出显示
-- tipText: 提示文字
-- onSure: 确认回调（可选）
function TipPanel:Popup(tipText, onSure)
    self.instance = self
    self:Init(self.panelName)
    self.panelObj:SetActive(true)

    -- 设置提示文字
    local txtTip = self:GetControl("txtTip", "Text")
    if txtTip ~= nil then
        txtTip.text = tipText or ""
    end

    -- 绑定按钮（每次弹出都重新绑定，确保回调是最新的）
    self:BindEvents(onSure)
end

-- 关闭弹窗（销毁自身）
function TipPanel:Close()
    self:DestroyPanel()
    self.instance = nil
end

-- 绑定按钮事件
function TipPanel:BindEvents(onSure)
    -- 确认按钮
    local btnSure = self:GetControl("btnSure", "Button")
    if btnSure ~= nil then
        -- 先移除旧监听，防止重复绑定
        btnSure.onClick:RemoveAllListeners()
        btnSure.onClick:AddListener(function()
            self:Close()
            if onSure then
                onSure()
            end
        end)
    end

    -- 取消按钮
    local btnCancel = self:GetControl("btnCancel", "Button")
    if btnCancel ~= nil then
        btnCancel.onClick:RemoveAllListeners()
        btnCancel.onClick:AddListener(function()
            self:Close()
        end)
    end
end
