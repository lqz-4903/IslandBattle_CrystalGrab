-- 游戏结束弹窗（弹出式面板，不销毁下层面板）
BasePanel:subClass("GameOverPanel")

GameOverPanel.panelName = "GameOverPanel"
GameOverPanel.instance = nil
GameOverPanel.maskObj = nil   -- 遮罩对象（防止UI穿透）

-- 创建遮罩（在GameOverPanel之前实例化，阻止点击穿透到下层面板）
function GameOverPanel:CreateMask()
    if not IsNull(self.maskObj) then
        return  -- 遮罩已存在，避免重复创建
    end
    local maskPrefab = ABMgr:LoadRes("ui", "imgMask", typeof(GameObject))
    if IsNull(maskPrefab) then
        print("[GameOverPanel] 错误：找不到 imgMask 预制体")
        return
    end
    self.maskObj = GameObject.Instantiate(maskPrefab)
    local canvasGo = GameObject.Find("Canvas")
    if canvasGo ~= nil then
        self.maskObj.transform:SetParent(canvasGo.transform, false)
    end
    print("[GameOverPanel] imgMask 遮罩已创建")
end

-- 弹出显示
function GameOverPanel:Popup()
    self.instance = self
    self:Init(self.panelName)
    self.panelObj:SetActive(true)
    -- 确保GameOverPanel渲染在最上层（Canvas最后子节点）
    self.panelObj.transform:SetAsLastSibling()
    self:BindEvents()
    -- 显示胜者名字
    local txtWinner = self:GetControl("txtWinner", "Text")
    if txtWinner ~= nil then
        local winnerName = CS.GameTimerManager.Instance.WinnerName
        txtWinner.text = "The Winner is " .. (winnerName ~= "" and winnerName or "未知")
    end
end

-- 关闭弹窗
function GameOverPanel:Close()
    -- 先销毁遮罩
    if not IsNull(self.maskObj) then
        GameObject.Destroy(self.maskObj)
        self.maskObj = nil
        print("[GameOverPanel] imgMask 遮罩已销毁")
    end
    -- 再销毁面板
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

-- 绑定按钮事件
function GameOverPanel:BindEvents()
    -- 返回大厅按钮
    local btnBack = self:GetControl("btnBack", "Button")
    if btnBack ~= nil then
        btnBack.onClick:RemoveAllListeners()
        btnBack.onClick:AddListener(function()
            -- ★ 通知服务器本客户端已从游戏返回房间（标记状态，广播玩家列表）
            CS.NetMgr.NotifyReturnToRoom()
            self:Close()
            -- 关闭 GamePanel
            GamePanel:Hide()
            -- 设置标志，回到主场景后显示 CreateRoomPanel
            showCreateRoomPanel = true
            CS.SceneMgr.Instance:LoadScene("BeginScene")
        end)
    end
end
