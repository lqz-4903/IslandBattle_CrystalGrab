-- 改名弹窗（弹出式面板，不销毁下层BeginPanel）
BasePanel:subClass("ChangeNamePanel")

ChangeNamePanel.panelName = "ChangeNamePanel"
ChangeNamePanel.instance = nil

-- 弹出显示（不隐藏下层面板）
function ChangeNamePanel:Popup()
    self.instance = self
    self:Init(self.panelName)
    self.panelObj:SetActive(true)

    -- 设置初始名字为当前玩家名
    local inputName = self:GetControl("inputName", "InputField")
    if inputName ~= nil then
        inputName.text = PlayerData.GetName()
    end

    if self.isInitEvent == false then
        self:BindEvents()
        self.isInitEvent = true
    end
end

-- 关闭弹窗（销毁自身）
function ChangeNamePanel:Close()
    self:DestroyPanel()
    self.instance = nil
end

-- 绑定按钮事件
function ChangeNamePanel:BindEvents()
    -- 确认按钮：保存名字，更新BeginPanel显示，关闭弹窗
    local btnSure = self:GetControl("btnSure", "Button")
    if btnSure ~= nil then
        btnSure.onClick:AddListener(function()
            local inputName = self:GetControl("inputName", "InputField")
            if inputName ~= nil then
                local newName = inputName.text
                if newName ~= nil and #newName > 0 then
                    -- 保存到JSON
                    PlayerData.SetName(newName)
                    -- 同步到BeginPanel的txtName
                    local txtName = BeginPanel:GetControl("txtName", "Text")
                    if txtName ~= nil then
                        txtName.text = newName
                    end
                end
            end
            self:Close()
        end)
    end

    -- 取消按钮：不保存，直接关闭
    local btnCancel = self:GetControl("btnCancel", "Button")
    if btnCancel ~= nil then
        btnCancel.onClick:AddListener(function()
            self:Close()
        end)
    end
end
