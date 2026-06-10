-- 利用面向对象
Object:subClass("BasePanel")

BasePanel.panelObj = nil
-- 相当于模拟一个字典 键为 控件名 值为控件本身
BasePanel.controls = {}
-- 事件监听标识
BasePanel.isInitEvent = false
-- 淡入淡出所需的CanvasGroup组件
BasePanel.canvasGroup = nil
-- 淡入淡出动画状态（Update驱动，纯Lua实现）
BasePanel.fadeUpdateId = nil
BasePanel.fadeType = nil      -- "in" 或 "out"
BasePanel.fadeElapsed = 0
BasePanel.fadeDuration = 0.2
BasePanel.fadeStartAlpha = 0
BasePanel.fadeEndAlpha = 1

function BasePanel:Init(name)
    if IsNull(self.panelObj) then
        -- ★ 每个面板实例必须有自己独立的 controls 表，防止弹窗面板污染底层面板
        self.controls = {}

        -- 公共的实例化对象的方法
        self.panelObj = ABMgr:LoadRes("ui", name, typeof(GameObject))
        if IsNull(self.panelObj) then
            print("错误：AB包中找不到预制体 " .. name)
            return
        end
        local canvasGo = GameObject.Find("Canvas")
        if canvasGo ~= nil then
            self.panelObj.transform:SetParent(canvasGo.transform, false)
        end

        -- 添加CanvasGroup组件（如果还没有）
        self.canvasGroup = self.panelObj:GetComponent(typeof(CanvasGroup))
        if IsNull(self.canvasGroup) then
            self.canvasGroup = self.panelObj:AddComponent(typeof(CanvasGroup))
        end

        -- GetComponentsInChildren
        -- 找所有UI控件 存起来
        local allControls = self.panelObj:GetComponentsInChildren(typeof(UIBehaviour))
        -- 优化：关闭不需要交互的控件的RaycastTarget
        for i = 0, allControls.Length - 1 do
            local ctrl = allControls[i]
            local typeName = ctrl:GetType().Name
            local ctrlName = ctrl.name
            if typeName == "Text" then
                -- Text一律关掉
                ctrl.raycastTarget = false
            elseif typeName == "Image" then
                -- 保留可交互控件自身的Image（btn/tog/sld/input前缀）
                local keepRaycast = string.find(ctrlName, "btn") ~= nil or
                                    string.find(ctrlName, "tog") ~= nil or
                                    string.find(ctrlName, "sld") ~= nil or
                                    string.find(ctrlName, "input") ~= nil
                -- 也保留Toggle/Slider等交互控件的子Image（如Background、Checkmark、Handle）
                if not keepRaycast then
                    local parent = ctrl.transform.parent
                    if parent ~= nil then
                        local toggle = parent:GetComponent(typeof(CS.UnityEngine.UI.Toggle))
                        local slider = parent:GetComponent(typeof(CS.UnityEngine.UI.Slider))
                        if toggle ~= nil or slider ~= nil then
                            keepRaycast = true
                        end
                    end
                end
                if not keepRaycast then
                    ctrl.raycastTarget = false
                end
            end
        end
        -- 如果存入一些对于我们来说没用UI控件
        -- 为了避免 找各种无用控件 定一个规则 拼面板时 控件命名一定按规范来
        -- Button btnName
        -- Toggle togName
        -- Image imgName
        -- ScrollRect svName
        -- Slider sldName
        -- InputField inputName
        for i = 0, allControls.Length - 1 do
            local controlName = allControls[i].name
            if string.find(controlName, "btn") ~= nil or
               string.find(controlName, "tog") ~= nil or
               string.find(controlName, "img") ~= nil or
               string.find(controlName, "sv") ~= nil or
               string.find(controlName, "txt") ~= nil or
               string.find(controlName, "sld") ~= nil or
               string.find(controlName, "input") ~= nil
            then
                -- 为了让我们在得的时候 能够 确定得的控件类型 所以我们需要存储类型
                -- 利用反射 Type 得到 控件的类名
                local typeName = allControls[i]:GetType().Name

                -- 避免出现一个对象上 挂载多个UI控件 出现覆盖的问题
                -- 都会被存到一个容器中 相当于像列表数组的形式
                -- 最终存储形式
                -- {
                --    btnRole = { Image = 控件, Button = 控件},
                --    togItem = { Toggle = 控件 }
                -- }
                if self.controls[controlName] ~= nil then
                    -- 通过自定义索引的形式 去加一个新的 "成员变量"
                    self.controls[controlName][typeName] = allControls[i]
                else
                    self.controls[controlName] = {[typeName] = allControls[i]}
                end
            end
        end
    end
end

-- 得到控件 根据 控件依附对象的名字 和 控件的类型字符串名字 Button Image Toggle
function BasePanel:GetControl(name, typeName)
    if self.controls[name] ~= nil then
        local sameNameControls = self.controls[name]
        if sameNameControls[typeName] ~= nil then
            return sameNameControls[typeName]
        end
    end
    return nil
end

-- 带淡入效果的显示
function BasePanel:ShowMe(name, fadeDuration)
    self:Init(name)
    self.panelObj:SetActive(true)

    fadeDuration = fadeDuration or 0.2

    -- 停止之前的淡入淡出动画
    self:StopFade()

    -- 确保canvasGroup存在且初始alpha为0
    if self.canvasGroup then
        self.canvasGroup.alpha = 0
        -- 注册Update回调，驱动淡入
        self.fadeType = "in"
        self.fadeElapsed = 0
        self.fadeDuration = fadeDuration
        self.fadeStartAlpha = 0
        self.fadeEndAlpha = 1
        self.fadeUpdateId = RegisterUpdate(function(dt)
            self:OnFadeUpdate(dt)
        end)
    end
end

-- 带淡出效果的隐藏
function BasePanel:HideMe(fadeDuration)
    fadeDuration = fadeDuration or 0.2

    if self.canvasGroup and self.panelObj and self.panelObj.activeSelf then
        -- 停止之前的淡入淡出动画
        self:StopFade()

        -- 注册Update回调，驱动淡出
        self.fadeType = "out"
        self.fadeElapsed = 0
        self.fadeDuration = fadeDuration
        self.fadeStartAlpha = self.canvasGroup.alpha
        self.fadeEndAlpha = 0
        self.fadeUpdateId = RegisterUpdate(function(dt)
            self:OnFadeUpdate(dt)
        end)
    else
        if self.panelObj then
            self.panelObj:SetActive(false)
        end
    end
end

-- 淡入淡出的每帧回调（由全局Update驱动）
function BasePanel:OnFadeUpdate(dt)
    -- 面板已销毁（切场景等），停止回调
    if not self.canvasGroup then
        UnregisterUpdate(self.fadeUpdateId)
        self.fadeUpdateId = nil
        return
    end
    self.fadeElapsed = self.fadeElapsed + dt
    local t = self.fadeElapsed / self.fadeDuration
    if t >= 1 then
        t = 1
    end
    -- 使用平滑曲线
    t = t * t * (3 - 2 * t)
    self.canvasGroup.alpha = Mathf.Lerp(self.fadeStartAlpha, self.fadeEndAlpha, t)

    -- 动画结束
    if self.fadeElapsed >= self.fadeDuration then
        self.canvasGroup.alpha = self.fadeEndAlpha
        UnregisterUpdate(self.fadeUpdateId)
        self.fadeUpdateId = nil
        self.fadeType = nil
        -- 淡出完成后隐藏物体
        if self.fadeEndAlpha == 0 and self.panelObj then
            self.panelObj:SetActive(false)
        end
    end
end

-- 立即显示（无动画）
function BasePanel:ShowImmediate(name)
    -- 停止正在播放的淡入淡出动画（如果有）
    self:StopFade()

    -- 首次使用或面板被销毁后重新加载
    if IsNull(self.panelObj) then
        self:Init(name)
    end

    if IsNull(self.canvasGroup) and self.panelObj then
        self.canvasGroup = self.panelObj:GetComponent(typeof(CanvasGroup))
        if IsNull(self.canvasGroup) then
            self.canvasGroup = self.panelObj:AddComponent(typeof(CanvasGroup))
        end
    end

    if self.canvasGroup then
        self.canvasGroup.alpha = 1
    else
        print("错误：无法获取 CanvasGroup，请检查面板预制体 " .. name)
    end

    if self.panelObj then
        self.panelObj:SetActive(true)
    end
end

-- 立即隐藏（无动画）
function BasePanel:HideImmediate()
    self:StopFade()
    if self.panelObj then
        self.panelObj:SetActive(false)
    end
end

-- 设置透明度（0-1）
function BasePanel:SetAlpha(alpha)
    if self.canvasGroup then
        self.canvasGroup.alpha = Mathf.Clamp01(alpha)
    end
end

-- 获取当前透明度
function BasePanel:GetAlpha()
    if self.canvasGroup then
        return self.canvasGroup.alpha
    end
    return 0
end

-- 判断是否正在播放动画
function BasePanel:IsFading()
    return self.fadeUpdateId ~= nil
end

-- 停止当前动画
function BasePanel:StopFade()
    if self.fadeUpdateId then
        UnregisterUpdate(self.fadeUpdateId)
        self.fadeUpdateId = nil
        self.fadeType = nil
    end
end
