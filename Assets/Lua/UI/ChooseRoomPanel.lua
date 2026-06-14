-- 继承BasePanel
BasePanel:subClass("ChooseRoomPanel")

-- 面板名称（对应AB包中的预制体名称）
ChooseRoomPanel.panelName = "ChooseRoomPanel"

-- 单例引用
ChooseRoomPanel.instance = nil

-- 显示面板
function ChooseRoomPanel:Show()
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
function ChooseRoomPanel:Hide()
    self:DestroyPanel()
    self.instance = nil
end

-- 绑定所有按钮事件
function ChooseRoomPanel:BindEvents()
    -- 返回按钮：回到BeginPanel
    local btnBack = self:GetControl("btnBack", "Button")
    if btnBack ~= nil then
        btnBack.onClick:AddListener(function()
            self:Hide()
            BeginPanel:Show()
        end)
    end

    -- 创建房间按钮：跳转CreateRoomPanel
    local btnCreateRoom = self:GetControl("btnCreateRoom", "Button")
    if btnCreateRoom ~= nil then
        btnCreateRoom.onClick:AddListener(function()
            self:Hide()
            CreateRoomPanel:Show()
        end)
    end

    -- 加入房间按钮：跳转JoinRoomPanel
    local btnJoinRoom = self:GetControl("btnJoinRoom", "Button")
    if btnJoinRoom ~= nil then
        btnJoinRoom.onClick:AddListener(function()
            self:Hide()
            JoinRoomPanel:Show()
        end)
    end
end
