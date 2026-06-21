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
-- 遮罩对象（防止UI穿透到下层面板）
BasePanel.maskObj = nil
-- 是否使用遮罩（BeginBKPanel和GamePanel不需要，设为false）
BasePanel.useMask = true
-- 画布层级："Static" | "Mid" | "Dynamic"，子类按需覆盖，未设置默认 Mid
BasePanel.canvasLayer = "Mid"

-- 根据 canvasLayer 返回对应 Canvas 的 Transform（替代 GameObject.Find 硬编码）
function BasePanel:GetCanvasTransform()
    local layer = self.canvasLayer or "Mid"
    if layer == "Static" then
        return CS.SceneMgr.Instance.Canvas_Static.transform
    elseif layer == "Dynamic" then
        return CS.SceneMgr.Instance.Canvas_Dynamic.transform
    else
        return CS.SceneMgr.Instance.Canvas_Mid.transform
    end
end

function BasePanel:Init(name)
    -- ★ 先创建遮罩，再加载面板（保证遮罩在面板下层，阻止点击穿透）
    self:CreateMask()

    if IsNull(self.panelObj) then
        -- ★ 每个面板实例必须有自己独立的 controls 表，防止弹窗面板污染底层面板
        self.controls = {}

        -- 公共的实例化对象的方法
        self.panelObj = ABMgr:LoadRes("ui", name, typeof(GameObject))
        if IsNull(self.panelObj) then
            print("错误：AB包中找不到预制体 " .. name)
            return
        end
        local canvasTransform = self:GetCanvasTransform()
        if canvasTransform ~= nil then
            self.panelObj.transform:SetParent(canvasTransform, false)
        end

        -- 添加CanvasGroup组件（如果还没有）
        self.canvasGroup = self.panelObj:GetComponent(typeof(CanvasGroup))
        if IsNull(self.canvasGroup) then
            self.canvasGroup = self.panelObj:AddComponent(typeof(CanvasGroup))
        end

        -- GetComponentsInChildren
        -- 找所有UI控件 存起来（单次遍历：RaycastTarget 优化 + 控件收集合并）
        local allControls = self.panelObj:GetComponentsInChildren(typeof(UIBehaviour))
        for i = 0, allControls.Length - 1 do
            local ctrl = allControls[i]
            local typeName = ctrl:GetType().Name
            local ctrlName = ctrl.name

            -- Part A: 关闭不需要交互的控件的 RaycastTarget
            if typeName == "Text" then
                ctrl.raycastTarget = false
            elseif typeName == "Image" then
                local keepRaycast = string.find(ctrlName, "btn") ~= nil or
                                    string.find(ctrlName, "tog") ~= nil or
                                    string.find(ctrlName, "sld") ~= nil or
                                    string.find(ctrlName, "input") ~= nil
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

            -- Part B: 按命名规范收集控件（btn/tog/img/sv/txt/sld/input 前缀）
            if string.find(ctrlName, "btn") ~= nil or
               string.find(ctrlName, "tog") ~= nil or
               string.find(ctrlName, "img") ~= nil or
               string.find(ctrlName, "sv") ~= nil or
               string.find(ctrlName, "txt") ~= nil or
               string.find(ctrlName, "sld") ~= nil or
               string.find(ctrlName, "input") ~= nil
            then
                if self.controls[ctrlName] ~= nil then
                    self.controls[ctrlName][typeName] = ctrl
                else
                    self.controls[ctrlName] = {[typeName] = ctrl}
                end
            end
        end
    end
end

-- 创建遮罩（在面板之前实例化，阻止点击穿透到下层面板）
function BasePanel:CreateMask()
    if not self.useMask then
        return
    end
    if not IsNull(self.maskObj) then
        return  -- 遮罩已存在，避免重复创建
    end
    self.maskObj = ABMgr:LoadRes("ui", "imgMask", typeof(GameObject))
    if IsNull(self.maskObj) then
        print("[BasePanel] 错误：找不到 imgMask 预制体")
        return
    end
    local canvasTransform = self:GetCanvasTransform()
    if canvasTransform ~= nil then
        self.maskObj.transform:SetParent(canvasTransform, false)
    end
end

-- 销毁遮罩
function BasePanel:DestroyMask()
    if not IsNull(self.maskObj) then
        GameObject.Destroy(self.maskObj)
        self.maskObj = nil
    end
end

-- 统一销毁面板和遮罩（替代各面板手动Destroy的重复代码）
function BasePanel:DestroyPanel()
    self:DestroyMask()
    if self.panelObj ~= nil then
        self:StopFade()
        GameObject.Destroy(self.panelObj)
        self.panelObj = nil
        self.canvasGroup = nil
        self.controls = {}
        self.isInitEvent = false
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

-- 停止当前动画
function BasePanel:StopFade()
    if self.fadeUpdateId then
        UnregisterUpdate(self.fadeUpdateId)
        self.fadeUpdateId = nil
        self.fadeType = nil
    end
end
