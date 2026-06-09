-- =============================================
-- Battle/InputHandler.lua — 键盘鼠标输入采集
-- =============================================
-- 【职责】
--   每帧采集 Unity Input，累积鼠标视角旋转，
--   按帧同步节奏（15fps）提供标准化的输入数据。
--
-- 【输入编码】
--   moveDir: 4 位 WASD 位掩码（见 GameConst）
--   cameraYaw: 累计水平朝向角（弧度，float）
--   jump/attack/skill: 布尔动作标记
--   chargeTime: 攻击蓄力时间（秒）
--
-- 【用法】
--   每帧调用 InputHandler:Update(dt)
--   每逻辑帧调用 InputHandler:GetTickInput() 获取并清除输入状态
-- =============================================

local GC = require("Core.GameConst")
local Fix64 = require("Fix64")

local InputHandler = {}
InputHandler.__index = InputHandler

-- ========== 状态 ==========

-- 累计朝向角（弧度）
InputHandler.cameraYaw   = 0
InputHandler.cameraPitch = 0

-- 当前帧输入状态
InputHandler.moveDir     = GC.MOVE_NONE
InputHandler.jumpPressed   = false
InputHandler.attackHeld    = false
InputHandler.skillPressed  = false
InputHandler.rollPressed   = false
InputHandler.reloadPressed = false
InputHandler.chargeTime    = 0   -- 蓄力累计（秒）

-- 鼠标增量（本帧累计）
InputHandler._mouseDeltaX = 0
InputHandler._mouseDeltaY = 0

-- 蓄力中标记
InputHandler._isCharging = false

-- 是否已锁定光标（FPS 模式）
InputHandler.cursorLocked = false

-- ========== 初始化 ==========

function InputHandler:Init()
    self.cameraYaw   = 0
    self.cameraPitch = 0
    self.moveDir     = GC.MOVE_NONE
    self.jumpPressed   = false
    self.attackHeld    = false
    self.skillPressed  = false
    self.rollPressed   = false
    self.reloadPressed = false
    self.chargeTime    = 0
    self._mouseDeltaX = 0
    self._mouseDeltaY = 0
    self._isCharging  = false
    self:LockCursor()
    print("[InputHandler] 初始化完成")
end

-- ========== 每帧更新 ==========

--- 每 Unity 帧调用（由 RegisterUpdate 驱动）
--- @param dt float — Unity Time.deltaTime
function InputHandler:Update(dt)
    -- 读取鼠标增量（Input.GetAxis 返回本帧像素移动量，已是 delta，不能再乘 dt）
    self._mouseDeltaX = CS.UnityEngine.Input.GetAxis(GC.AXIS.MOUSE_X)
    self._mouseDeltaY = CS.UnityEngine.Input.GetAxis(GC.AXIS.MOUSE_Y)

    -- 更新朝向
    -- sensitivity = base * (slider值 * 0.1)，默认 slider=50 → 0.003*5=0.015（5倍于初始）
    local dpi = PlayerData.GetMouseDPI()
    local sensitivity = GC.MOUSE_SENSITIVITY * (dpi * 0.1)
    self.cameraYaw   = self.cameraYaw + self._mouseDeltaX * sensitivity
    self.cameraPitch = self.cameraPitch - self._mouseDeltaY * sensitivity

    -- 限制俯仰角（-89° ~ 89°，防止万向锁）
    local maxPitch = math.rad(89)
    self.cameraPitch = math.max(-maxPitch, math.min(maxPitch, self.cameraPitch))

    -- 读取移动按键
    self.moveDir = GC.MOVE_NONE
    if CS.UnityEngine.Input.GetKey(CS.UnityEngine.KeyCode.W) then
        self.moveDir = self.moveDir | GC.MOVE_FORWARD
    end
    if CS.UnityEngine.Input.GetKey(CS.UnityEngine.KeyCode.S) then
        self.moveDir = self.moveDir | GC.MOVE_BACKWARD
    end
    if CS.UnityEngine.Input.GetKey(CS.UnityEngine.KeyCode.A) then
        self.moveDir = self.moveDir | GC.MOVE_LEFT
    end
    if CS.UnityEngine.Input.GetKey(CS.UnityEngine.KeyCode.D) then
        self.moveDir = self.moveDir | GC.MOVE_RIGHT
    end

    -- 动作按键（攻击/技能用直接鼠标检测，绕过 InputManager 防止 Ctrl 等键盘误触发）
    self.jumpPressed   = CS.UnityEngine.Input.GetButtonDown(GC.KEY.JUMP)
    self.attackHeld    = CS.UnityEngine.Input.GetMouseButton(0)     -- 鼠标左键
    self.skillPressed  = CS.UnityEngine.Input.GetMouseButtonDown(1) -- 鼠标右键
    self.rollPressed   = CS.UnityEngine.Input.GetKeyDown(GC.KEYCODE.ROLL)
    self.reloadPressed = CS.UnityEngine.Input.GetKeyDown(GC.KEYCODE.RELOAD)

    -- 蓄力计时
    if self.attackHeld then
        if not self._isCharging then
            self._isCharging = true
            self.chargeTime = 0
        end
        self.chargeTime = self.chargeTime + dt
    else
        self._isCharging = false
        -- 松开攻击键时，chargeTime 保留本帧的值用于发送，下一帧清零
    end

    -- 光标锁定/释放
    -- Esc: 永久切换
    if CS.UnityEngine.Input.GetKeyDown(CS.UnityEngine.KeyCode.Escape) then
        self:ToggleCursor()
    end
    -- Alt: 按住临时释放，松开重新锁定
    if CS.UnityEngine.Input.GetKeyDown(CS.UnityEngine.KeyCode.LeftAlt) or
       CS.UnityEngine.Input.GetKeyDown(CS.UnityEngine.KeyCode.RightAlt) then
        self:UnlockCursor()
    end
    if CS.UnityEngine.Input.GetKeyUp(CS.UnityEngine.KeyCode.LeftAlt) or
       CS.UnityEngine.Input.GetKeyUp(CS.UnityEngine.KeyCode.RightAlt) then
        self:LockCursor()
    end
end

-- ========== 逻辑帧输入获取 ==========

--- 获取当前帧的标准化输入，并重置瞬发动作（jump/skill）
--- 调用频率：逻辑帧率（15fps）
--- @return table {moveDir, jump, attack, skill, cameraYaw, chargeTime}
function InputHandler:GetTickInput()
    local input = {
        moveDir    = self.moveDir,
        jump       = self.jumpPressed,
        attack     = self.attackHeld,
        skill      = self.skillPressed,
        cameraYaw  = self.cameraYaw,
        chargeTime = self.chargeTime,
    }

    -- 重置瞬发动作（避免同一动作被多帧重复发送）
    self.jumpPressed   = false
    self.skillPressed  = false
    self.rollPressed   = false
    self.reloadPressed = false

    -- 重置蓄力（松开攻击键后下一帧归零）
    if not self.attackHeld then
        self.chargeTime = 0
    end

    return input
end

-- ========== 光标控制 ==========

function InputHandler:LockCursor()
    CS.UnityEngine.Cursor.lockState = CS.UnityEngine.CursorLockMode.Locked
    CS.UnityEngine.Cursor.visible = false
    self.cursorLocked = true
end

function InputHandler:UnlockCursor()
    CS.UnityEngine.Cursor.lockState = CS.UnityEngine.CursorLockMode.None
    CS.UnityEngine.Cursor.visible = true
    self.cursorLocked = false
end

function InputHandler:ToggleCursor()
    if self.cursorLocked then
        self:UnlockCursor()
    else
        self:LockCursor()
    end
end

return InputHandler
