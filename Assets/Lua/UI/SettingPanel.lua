-- 继承BasePanel
BasePanel:subClass("SettingPanel")

-- 面板名称（对应AB包中的预制体名称）
SettingPanel.panelName = "SettingPanel"

-- 单例引用
SettingPanel.instance = nil

-- 调用者引用（Popup模式用，关闭后返回caller）
SettingPanel.caller = nil

-- 显示面板（从BeginPanel进入，关闭后回到BeginPanel）
function SettingPanel:Show()
    if self.instance == nil then
        self.instance = self
    end
    self.caller = nil
    self:ShowMe(self.panelName)
    self:LoadSettings()
    if self.isInitEvent == false then
        self:BindEvents()
        self.isInitEvent = true
    end
end

-- 弹出显示（从GamePanel等进入，不隐藏下层面板，关闭后返回caller）
function SettingPanel:Popup(caller)
    if self.instance == nil then
        self.instance = self
    end
    self.caller = caller
    self:Init(self.panelName)
    self.panelObj:SetActive(true)
    self:LoadSettings()
    if self.isInitEvent == false then
        self:BindEvents()
        self.isInitEvent = true
    end
end

-- 隐藏并销毁面板
function SettingPanel:Hide()
    if self.panelObj ~= nil then
        self:StopFade()
        GameObject.Destroy(self.panelObj)
        self.panelObj = nil
        self.canvasGroup = nil
        self.controls = {}
        self.isInitEvent = false
    end
    self.instance = nil
    self.caller = nil
end

-- 从JSON加载设置到控件
function SettingPanel:LoadSettings()
    local togMusic = self:GetControl("togMusic", "Toggle")
    if togMusic ~= nil then
        togMusic.isOn = PlayerData.GetMusicOn()
    end

    local togSound = self:GetControl("togSound", "Toggle")
    if togSound ~= nil then
        togSound.isOn = PlayerData.GetSoundOn()
    end

    local sldMusic = self:GetControl("sldMusic", "Slider")
    if sldMusic ~= nil then
        sldMusic.value = PlayerData.GetMusicVolume()
    end

    local sldSound = self:GetControl("sldSound", "Slider")
    if sldSound ~= nil then
        sldSound.value = PlayerData.GetSoundVolume()
    end
end

-- 从控件保存设置到JSON
function SettingPanel:SaveSettings()
    local togMusic = self:GetControl("togMusic", "Toggle")
    if togMusic ~= nil then
        PlayerData.SetMusicOn(togMusic.isOn)
    end

    local togSound = self:GetControl("togSound", "Toggle")
    if togSound ~= nil then
        PlayerData.SetSoundOn(togSound.isOn)
    end

    local sldMusic = self:GetControl("sldMusic", "Slider")
    if sldMusic ~= nil then
        PlayerData.SetMusicVolume(sldMusic.value)
    end

    local sldSound = self:GetControl("sldSound", "Slider")
    if sldSound ~= nil then
        PlayerData.SetSoundVolume(sldSound.value)
    end

    PlayerData.Save()
end

-- 绑定所有按钮事件
function SettingPanel:BindEvents()
    -- togMusic取消时，联动关闭togSound
    local togMusic = self:GetControl("togMusic", "Toggle")
    if togMusic ~= nil then
        togMusic.onValueChanged:AddListener(function(isOn)
            if not isOn then
                local togSound = self:GetControl("togSound", "Toggle")
                if togSound ~= nil then
                    togSound.isOn = false
                end
            end
        end)
    end

    -- 关闭按钮：不保存，回到调用者或BeginPanel
    local btnClose = self:GetControl("btnClose", "Button")
    if btnClose ~= nil then
        btnClose.onClick:AddListener(function()
            local prevCaller = self.caller
            self:Hide()
            if prevCaller ~= nil then
                prevCaller:Show()
            else
                BeginPanel:Show()
            end
        end)
    end

    -- 确认按钮：保存设置，回到调用者或BeginPanel
    local btnSure = self:GetControl("btnSure", "Button")
    if btnSure ~= nil then
        btnSure.onClick:AddListener(function()
            self:SaveSettings()
            local prevCaller = self.caller
            self:Hide()
            if prevCaller ~= nil then
                prevCaller:Show()
            else
                BeginPanel:Show()
            end
        end)
    end
end
