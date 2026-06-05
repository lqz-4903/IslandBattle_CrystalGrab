-- 继承BasePanel
BasePanel:subClass("SettingPanel")

-- 面板名称（对应AB包中的预制体名称）
SettingPanel.panelName = "SettingPanel"

-- 单例引用
SettingPanel.instance = nil

-- 显示面板
function SettingPanel:Show()
    if self.instance == nil then
        self.instance = self
    end
    self:ShowMe(self.panelName)
    -- 加载设置到控件
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
    -- 关闭按钮：不保存，回到BeginPanel
    local btnClose = self:GetControl("btnClose", "Button")
    if btnClose ~= nil then
        btnClose.onClick:AddListener(function()
            self:Hide()
            BeginPanel:Show()
        end)
    end

    -- 确认按钮：保存设置，回到BeginPanel
    local btnSure = self:GetControl("btnSure", "Button")
    if btnSure ~= nil then
        btnSure.onClick:AddListener(function()
            self:SaveSettings()
            self:Hide()
            BeginPanel:Show()
        end)
    end
end
