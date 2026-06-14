-- 游戏结束弹窗（弹出式面板，不销毁下层面板）
BasePanel:subClass("GameOverPanel")

GameOverPanel.panelName = "GameOverPanel"
GameOverPanel.instance = nil

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
    self:DestroyPanel()
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
