-- 继承BasePanel
BasePanel:subClass("CreateRoomPanel")

-- 面板名称（对应AB包中的预制体名称）
CreateRoomPanel.panelName = "CreateRoomPanel"

-- 单例引用
CreateRoomPanel.instance = nil

-- 开始游戏按钮引用（用于置灰控制）
CreateRoomPanel.btnStartGame = nil
-- 是否为房主
CreateRoomPanel.isHost = true
-- 房间号（房主创建时生成，加入者输入时匹配用）
CreateRoomPanel.roomID = nil

-- 显示面板
-- isHost: true=房主（默认），false=加入者
function CreateRoomPanel:Show(isHost)
    if self.instance == nil then
        self.instance = self
    end
    -- 默认为房主
    self.isHost = (isHost ~= false)

    self:ShowMe(self.panelName)
    if self.isInitEvent == false then
        self:BindEvents()
        self.isInitEvent = true
    end

    if self.isHost then
        -- 房主：显示房间信息，添加自己为第一个玩家
        local txtRoomHost = self:GetControl("txtRoomHost", "Text")
        if txtRoomHost ~= nil then
            txtRoomHost.text = "房间号：" .. PlayerData.GetName()
        end
        self:AddPlayer(PlayerData.GetName())
        -- 测试用：模拟加入更多玩家
        self:AddPlayer("测试玩家2")
        self:AddPlayer("测试玩家3")
        self:AddPlayer("测试玩家4")
    else
        -- 加入者：隐藏开始按钮和解散按钮
        if self.btnStartGame ~= nil then
            self.btnStartGame.gameObject:SetActive(false)
        end
        local btnDisband = self:GetControl("btnDisband", "Button")
        if btnDisband ~= nil then
            btnDisband.gameObject:SetActive(false)
        end
    end
end

-- 添加一个玩家到imgPeoples下
function CreateRoomPanel:AddPlayer(playerName)
    local imgPeoples = self:GetControl("imgPeoples", "Image")
    if imgPeoples == nil then return end

    local parentRect = imgPeoples:GetComponent(typeof(CS.UnityEngine.RectTransform))
    if parentRect == nil then return end

    -- 加载txtPlayer预制体并实例化到imgPeoples下
    local txtPlayerObj = ABMgr:LoadRes("ui", "txtPlayer", typeof(GameObject))
    if txtPlayerObj ~= nil then
        txtPlayerObj.transform:SetParent(imgPeoples.transform, false)

        -- 设置文字内容
        local txt = txtPlayerObj:GetComponent(typeof(CS.UnityEngine.UI.Text))
        if txt ~= nil then
            txt.text = "玩家\t" .. playerName .. "\t已在房间"
        end
    end

    -- 重新计算所有txtPlayer的布局
    self:LayoutPlayers()

    -- 刷新开始按钮状态
    self:RefreshStartBtn()
end

-- 重新计算imgPeoples下所有txtPlayer的布局
-- <4个：从顶部排列；>=4个：整体居中
function CreateRoomPanel:LayoutPlayers()
    local imgPeoples = self:GetControl("imgPeoples", "Image")
    if imgPeoples == nil then return end

    local parentRect = imgPeoples:GetComponent(typeof(CS.UnityEngine.RectTransform))
    if parentRect == nil then return end

    local spacing = 85
    local itemHeight = 60
    local parentWidth = parentRect.rect.width
    local parentHeight = parentRect.rect.height
    local padding = parentWidth * 0.05

    -- 统计txtPlayer子对象
    local players = {}
    local transform = imgPeoples.transform
    for i = 0, transform.childCount - 1 do
        local child = transform:GetChild(i)
        if string.find(child.name, "txtPlayer") ~= nil then
            table.insert(players, child)
        end
    end

    local count = #players
    if count == 0 then return end

    -- 计算起始Y：>=4个居中，<4个从顶部留间距开始
    local totalSpan = (count - 1) * spacing + itemHeight
    local topPadding = 50
    local startY = topPadding
    if count >= 4 then
        startY = (parentHeight - totalSpan) / 2
    end

    -- 设置每个txtPlayer的位置
    for i, player in ipairs(players) do
        local rect = player:GetComponent(typeof(CS.UnityEngine.RectTransform))
        if rect ~= nil then
            rect.anchorMin = CS.UnityEngine.Vector2(0, 1)
            rect.anchorMax = CS.UnityEngine.Vector2(1, 1)
            rect.pivot = CS.UnityEngine.Vector2(0.5, 1)

            local index = i - 1
            rect.offsetMin = CS.UnityEngine.Vector2(padding, -(startY + index * spacing + itemHeight))
            rect.offsetMax = CS.UnityEngine.Vector2(-padding, -(startY + index * spacing))
        end
    end
end

-- 隐藏并销毁面板
function CreateRoomPanel:Hide()
    if self.panelObj ~= nil then
        self:StopFade()
        GameObject.Destroy(self.panelObj)
        self.panelObj = nil
        self.canvasGroup = nil
        self.controls = {}
        self.isInitEvent = false
    end
    self.btnStartGame = nil
    self.instance = nil
end

-- 检查imgPeoples下txtPlayer子对象数量，>=2才能开始游戏
function CreateRoomPanel:RefreshStartBtn()
    if self.btnStartGame == nil then return end

    local imgPeoples = self:GetControl("imgPeoples", "Image")
    if imgPeoples == nil then
        self.btnStartGame.interactable = false
        return
    end

    -- 遍历imgPeoples的子对象，统计txtPlayer数量
    local count = 0
    local transform = imgPeoples.transform
    for i = 0, transform.childCount - 1 do
        local child = transform:GetChild(i)
        if string.find(child.name, "txtPlayer") ~= nil then
            count = count + 1
        end
    end

    self.btnStartGame.interactable = (count >= 2)
end

-- 绑定所有按钮事件
function CreateRoomPanel:BindEvents()
    -- 解散按钮：回到ChooseRoomPanel
    local btnDisband = self:GetControl("btnDisband", "Button")
    if btnDisband ~= nil then
        btnDisband.onClick:AddListener(function()
            self:Hide()
            ChooseRoomPanel:Show()
        end)
    end

    -- 开始游戏按钮
    self.btnStartGame = self:GetControl("btnStartGame", "Button")
    if self.btnStartGame ~= nil then
        self.btnStartGame.onClick:AddListener(function()
            -- 弹出确认提示
            TipPanel:Popup("你确定开始游戏吗", function()
                -- 确认：销毁所有UI，切换到GameScene
                self:Hide()
                BeginPanel:Hide()
                BeginBKPanel:Hide()
                CS.SceneMgr.Instance:LoadScene("GameScene")
            end)
        end)
    end
end
