-- 继承BasePanel
BasePanel:subClass("JoinRoomPanel")

-- 面板名称（对应AB包中的预制体名称）
JoinRoomPanel.panelName = "JoinRoomPanel"

-- 单例引用
JoinRoomPanel.instance = nil

-- 显示面板
function JoinRoomPanel:Show()
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
function JoinRoomPanel:Hide()
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
function JoinRoomPanel:BindEvents()
    -- 返回按钮：回到ChooseRoomPanel
    local btnBack = self:GetControl("btnBack", "Button")
    if btnBack ~= nil then
        btnBack.onClick:AddListener(function()
            self:Hide()
            ChooseRoomPanel:Show()
        end)
    end

    -- 确认按钮：输入房间号后确认加入
    local btnSure = self:GetControl("btnJoin", "Button")
    if btnSure ~= nil then
        btnSure.onClick:AddListener(function()
            local inputRoomNum = self:GetControl("inputRoomNum", "InputField")
            if inputRoomNum == nil then return end

            local roomNum = inputRoomNum.text
            if roomNum == nil or #roomNum == 0 then
                TipPanel:Popup("请输入房间号")
                return
            end

            -- 弹出确认提示
            TipPanel:Popup("你确定加入这个房间" .. roomNum .. "吗", function()
                -- TODO: 替换为真实网络请求，发送加入房间请求
                -- 模拟：本地判断房间号是否匹配
                local hostRoomID = CreateRoomPanel.roomID
                if hostRoomID ~= nil and roomNum == hostRoomID then
                    -- 房间号匹配，以非房主身份进入CreateRoomPanel
                    self:Hide()
                    CreateRoomPanel:Show(false)
                else
                    -- TODO: 替换为根据服务端Ack显示不同错误
                    -- 房间号不存在
                    TipPanel:Popup("您输入的房间号不存在")
                    -- 如果是满员，应显示：
                    -- TipPanel:Popup("你的加入房间已满员")
                end
            end)
        end)
    end
end
