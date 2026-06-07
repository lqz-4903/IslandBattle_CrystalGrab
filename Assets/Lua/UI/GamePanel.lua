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
end

-- 隐藏并销毁面板
function GamePanel:Hide()
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

-- 初始化血量条（首次进入时调用，不重置血量）
function GamePanel:InitBloodBar()
    if not self.bloodInited then
        self.curHP = self.maxHP
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
        rect.sizeDelta = CS.UnityEngine.Vector2(newW, rect.sizeDelta.y)
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
