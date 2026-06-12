-- 继承BasePanel
BasePanel:subClass("GamePanel")

-- 面板名称（对应AB包中的预制体名称）
GamePanel.panelName = "GamePanel"

-- 单例引用
GamePanel.instance = nil

-- 血量相关
GamePanel.maxHP = 100
GamePanel.curHP = 100
GamePanel.hpW = 500

-- 倒计时相关（由服务端权威控制）
GamePanel.timerUpdateId = nil    -- 帧更新回调ID
GamePanel.isTimerRunning = false -- 计时器是否运行中

-- 显示面板
function GamePanel:Show()
    if self.instance == nil then
        self.instance = self
    end
    self:ShowImmediate(self.panelName)
    if self.isInitEvent == false then
        self:BindEvents()
        self.isInitEvent = true
    end
    self:InitBloodBar()
    self:StartTimer()
end

-- 隐藏并销毁面板
function GamePanel:Hide()
    self:StopTimer()
    if self.panelObj ~= nil then
        self:StopFade()
        GameObject.Destroy(self.panelObj)
        self.panelObj = nil
        self.canvasGroup = nil
        self.controls = {}
        self.isInitEvent = false
    end
    self.instance = nil
    self.bloodInited = false
end

-- 绑定按钮事件
function GamePanel:BindEvents()
    local btnSetting = self:GetControl("btnSetting", "Button")
    if btnSetting ~= nil then
        btnSetting.onClick:AddListener(function()
            SettingPanel:Popup(self)
        end)
    end
end

-- 初始化血量条（首次进入时调用）
-- ★ InitBloodBar 在 GamePanel:Show() 中调用，早于 NotifyUI，
--    此处初始化为满血作为占位，真正血量由 PlayerEntity:NotifyUI() 立即覆盖
function GamePanel:InitBloodBar()
    if not self.bloodInited then
        self.bloodInited = true
    end
    self:UpdateBloodDisplay()
end

-- 更新血量显示（只改sizeDelta的宽度，不动pivot/anchor/position）
function GamePanel:UpdateBloodDisplay()
    self.curHP = Mathf.Clamp(self.curHP, 0, self.maxHP)

    local txtBloodNum = self:GetControl("txtBloodNum", "Text")
    if txtBloodNum ~= nil then
        txtBloodNum.text = self.curHP .. "/" .. self.maxHP
    end

    local imgBlood = self:GetControl("imgBlood", "Image")
    if imgBlood ~= nil then
        local rect = imgBlood.rectTransform
        local newW = self.curHP / self.maxHP * self.hpW
        rect.sizeDelta = Vector2(newW, rect.sizeDelta.y)
    end
end

-- 扣血（外部调用：GamePanel:TakeDamage(10)）
function GamePanel:TakeDamage(amount)
    self.curHP = self.curHP - amount
    self:UpdateBloodDisplay()
end

-- 更新得分（外部调用：GamePanel:UpdateScore(500)）
function GamePanel:UpdateScore(score)
    local txtScoreNum = self:GetControl("txtScoreNum", "Text")
    if txtScoreNum ~= nil then
        txtScoreNum.text = tostring(score)
    end
end

-- 开始监听服务端倒计时
function GamePanel:StartTimer()
    self:StopTimer()
    self.isTimerRunning = true
    -- 确保 GameTimerManager 已初始化
    CS.GameTimerManager.Instance:Reset()
    -- 每帧从服务端权威数据读取剩余时间并显示
    self.timerUpdateId = RegisterUpdate(function(dt)
        self:OnTimerUpdate(dt)
    end)
end

-- 停止倒计时监听
function GamePanel:StopTimer()
    self.isTimerRunning = false
    if self.timerUpdateId ~= nil then
        UnregisterUpdate(self.timerUpdateId)
        self.timerUpdateId = nil
    end
end

-- 每帧从服务端读取倒计时并更新显示
function GamePanel:OnTimerUpdate(_dt)
    if not self.isTimerRunning then
        return
    end

    local timerMgr = CS.GameTimerManager.Instance

    -- 游戏结束时停止
    if timerMgr.IsGameEnd then
        self:StopTimer()
        self:OnTimerEnd()
        return
    end

    -- 收到服务端数据后更新显示
    if timerMgr.HasReceived then
        self:UpdateTimerDisplay(timerMgr.RemainingTime)
    end
end

-- 更新倒计时显示
-- ★ GC优化：缓存上次显示的时间值，只有秒数变化才更新文本（避免每帧 string.format）
function GamePanel:UpdateTimerDisplay(remainTime)
    local txtTimer = self:GetControl("txtTimer", "Text")
    if txtTimer ~= nil then
        local time = math.max(0, math.floor(remainTime))
        -- 仅当显示的秒数变化时才更新文本（60fps → 1fps 的 string.format 调用）
        if self._lastDisplayedTime ~= time then
            self._lastDisplayedTime = time
            local minutes = math.floor(time / 60)
            local seconds = time % 60
            txtTimer.text = string.format("游戏倒计时 %d:%02d", minutes, seconds)
        end
    else
        print("[GamePanel] 警告：找不到 txtTimer 控件")
    end
end

-- 倒计时结束回调
function GamePanel:OnTimerEnd()
    print("游戏时间结束！")
    -- 先实例化遮罩，阻止UI穿透到下层GamePanel
    GameOverPanel:CreateMask()
    -- 再弹出游戏结束界面，保证GameOverPanel最后渲染（在最上层）
    GameOverPanel:Popup()
end
