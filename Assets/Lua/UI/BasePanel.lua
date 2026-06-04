-- 利用面向对象
Object:subClass("BasePanel")

BasePanel.panelObj = nil
-- 相当于模拟一个字典 键为 控件名 值为控件本身
BasePanel.controls = {}
-- 事件监听标识
BasePanel.isInitEvent = false
-- 淡入淡出所需的CanvasGroup组件
BasePanel.canvasGroup = nil
-- 淡入淡出动画协程
BasePanel.fadeCoroutine = nil

function BasePanel:Init(name)
    if IsNull(self.panelObj) then
        -- 公共的实例化对象的方法
        self.panelObj = ABMgr:LoadRes("ui", name, typeof(GameObject))
        self.panelObj.transform:SetParent(Canvas, false)
        
        -- 添加CanvasGroup组件（如果还没有）
        self.canvasGroup = self.panelObj:GetComponent(typeof(CanvasGroup))
        if IsNull(self.canvasGroup) then
            self.canvasGroup = self.panelObj:AddComponent(typeof(CanvasGroup))
        end
        
        -- GetComponentsInChildren
        -- 找所有UI控件 存起来
        local allControls = self.panelObj:GetComponentsInChildren(typeof(UIBehaviour))
        -- 如果存入一些对于我们来说没用UI控件
        -- 为了避免 找各种无用控件 定一个规则 拼面板时 控件命名一定按规范来
        -- Button btnName
        -- Toggle togName
        -- Image imgName
        -- ScrollRect svName
        for i = 0, allControls.Length - 1 do
            local controlName = allControls[i].name
            if string.find(controlName, "btn") ~= nil or
               string.find(controlName, "tog") ~= nil or
               string.find(controlName, "img") ~= nil or
               string.find(controlName, "sv") ~= nil or
               string.find(controlName, "txt") ~= nil
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
                if self.controls then
                    print("self.controls[controlName]:", self.controls[controlName])
                end
                
                if self.controls[controlName] ~= nil then
                    -- 通过自定义索引的形式 去加一个新的 “成员变量”
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
    
    fadeDuration = fadeDuration or 0.2  -- 默认淡入时间0.2秒
    
    -- 停止之前的淡入淡出动画
    if self.fadeCoroutine then
        self:StopCoroutine(self.fadeCoroutine)
        self.fadeCoroutine = nil
    end
    
    -- 确保canvasGroup存在且初始alpha为0
    if self.canvasGroup then
        self.canvasGroup.alpha = 0
        self.fadeCoroutine = self:StartCoroutine(self.FadeIn(fadeDuration))
    end
end

-- 带淡出效果的隐藏
function BasePanel:HideMe(fadeDuration)
    fadeDuration = fadeDuration or 0.2  -- 默认淡出时间0.2秒
    
    if self.canvasGroup and self.panelObj and self.panelObj.activeSelf then
        -- 停止之前的淡入淡出动画
        if self.fadeCoroutine then
            self:StopCoroutine(self.fadeCoroutine)
            self.fadeCoroutine = nil
        end
        
        self.fadeCoroutine = self:StartCoroutine(self.FadeOut(fadeDuration))
    else
        -- 如果没有CanvasGroup或者面板已隐藏，直接隐藏
        if self.panelObj then
            self.panelObj:SetActive(false)
        end
    end
end

-- 立即显示（无动画）
function BasePanel:ShowImmediate(name)
    self:Init(name)
    if self.canvasGroup then
        self.canvasGroup.alpha = 1
    end
    self.panelObj:SetActive(true)
end

-- 立即隐藏（无动画）
function BasePanel:HideImmediate()
    -- 停止所有动画协程
    if self.fadeCoroutine then
        self:StopCoroutine(self.fadeCoroutine)
        self.fadeCoroutine = nil
    end
    if self.panelObj then
        self.panelObj:SetActive(false)
    end
end

-- 淡入协程
function BasePanel:FadeIn(duration)
    local elapsed = 0
    local startAlpha = self.canvasGroup.alpha
    local endAlpha = 1
    
    while elapsed < duration do
        elapsed = elapsed + Time.deltaTime
        local t = elapsed / duration
        -- 使用平滑曲线
        t = t * t * (3 - 2 * t)
        self.canvasGroup.alpha = Mathf.Lerp(startAlpha, endAlpha, t)
        coroutine.yield(0)
    end
    
    self.canvasGroup.alpha = endAlpha
    self.fadeCoroutine = nil
end

-- 淡出协程
function BasePanel:FadeOut(duration)
    local elapsed = 0
    local startAlpha = self.canvasGroup.alpha
    local endAlpha = 0
    
    while elapsed < duration do
        elapsed = elapsed + Time.deltaTime
        local t = elapsed / duration
        -- 使用平滑曲线
        t = t * t * (3 - 2 * t)
        self.canvasGroup.alpha = Mathf.Lerp(startAlpha, endAlpha, t)
        coroutine.yield(0)
    end
    
    self.canvasGroup.alpha = endAlpha
    -- 淡出完成后隐藏物体
    if self.panelObj then
        self.panelObj:SetActive(false)
    end
    self.fadeCoroutine = nil
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
    return self.fadeCoroutine ~= nil
end

-- 停止当前动画
function BasePanel:StopFade()
    if self.fadeCoroutine then
        self:StopCoroutine(self.fadeCoroutine)
        self.fadeCoroutine = nil
    end
end