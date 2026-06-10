-- =============================================
-- Core/GameConst.lua — 游戏常量定义
-- =============================================

local GC = {}

-- ========== 移动方向位掩码 ==========
-- 8 方向 WASD 输入编码，支持组合（如 W+A = 斜向前左）
GC.MOVE_NONE     = 0   -- 静止
GC.MOVE_FORWARD  = 1   -- W / bit 0
GC.MOVE_BACKWARD = 2   -- S / bit 1
GC.MOVE_LEFT     = 4   -- A / bit 2
GC.MOVE_RIGHT    = 8   -- D / bit 3
GC.MOVE_ROLL     = 16  -- ★ bit 4：翻滚状态（编码在 MoveDir 中，不修改 proto）

-- ========== 游戏参数（与服务端 GameEventHandler 保持一致）==========
GC.DEFAULT_HP       = 3       -- 默认生命值
GC.DEFAULT_WIN_SCORE = 10     -- 默认胜利分数
GC.CRYSTAL_INTERVAL  = 5      -- 水晶生成间隔（秒）
GC.MAX_CRYSTALS      = 5      -- 最大同时存在水晶数
GC.DEFAULT_GAME_DURATION = 120 -- 默认游戏时长（秒）

-- ========== 客户端模拟参数 ==========
GC.TICK_RATE       = 15       -- 逻辑帧率（与服务端 TickSyncHandler 一致）
GC.TICK_INTERVAL   = 1 / 15   -- 每帧间隔（秒）
GC.MOVE_SPEED      = 5        -- 玩家移动速度（米/秒）
GC.JUMP_FORCE      = 8        -- 跳跃初速度
GC.GRAVITY         = 20       -- 重力加速度
GC.PHYSICS_SUBSTEPS = 8        -- 远程玩家每 tick 物理子步数（匹配 120fps 碰撞精度，缩小主机端远程玩家与客户端自视的差异）
GC.MOUSE_SENSITIVITY = 0.003 -- 鼠标灵敏度（每像素旋转弧度，0.003≈0.17°/像素）

-- ========== 网络插值参数（消除 15fps tick → 60fps 渲染的卡顿）==========
GC.INTERP_INTERVAL    = 1 / 15   -- 插值时间窗口（与 TICK_RATE 一致）
GC.INTERP_MAX_EXTRAP  = 2        -- 最大外推 tick 数（tick 延迟时短暂外推）
GC.INTERP_ROT_SPEED   = 720      -- 旋转插值速度（度/秒），使用 RotateTowards

-- ========== 网络消息 ID ==========
GC.MSG_ID = {
    HEARTBEAT          = 1,
    KICK_OFF           = 2,
    CREATE_ROOM        = 10,
    CREATE_ROOM_ACK    = 11,
    JOIN_ROOM          = 12,
    JOIN_ROOM_ACK      = 13,
    PLAYER_LIST        = 14,
    START_GAME         = 15,
    GAME_START         = 16,
    REQUEST_PLAYER_LIST = 18,
    RETURN_TO_ROOM     = 19,
    INPUT_TICK         = 20,
    PLAYER_INPUT       = 21,
    CRYSTAL_SPAWN      = 30,
    CRYSTAL_PICKUP     = 31,
    PLAYER_HIT         = 32,
    PLAYER_FALL        = 33,
    GAME_END           = 34,
    PLAYER_RESPAWN     = 35,
    GAME_TIMER_UPDATE  = 36,
    PLAYER_OFFLINE     = 37,
    RECONNECT          = 40,
    RECONNECT_ACK      = 41,
    CATCH_UP_TICKS     = 42,
}

-- ========== 输入轴名称（Unity Input Manager）==========
GC.AXIS = {
    MOUSE_X = "Mouse X",
    MOUSE_Y = "Mouse Y",
    HORIZONTAL = "Horizontal",
    VERTICAL = "Vertical",
}

-- ========== 按键名（Unity Input Manager）==========
GC.KEY = {
    JUMP   = "Jump",    -- Space
    ATTACK = "Fire1",   -- 鼠标左键
    SKILL  = "Fire2",   -- 鼠标右键 / 交互/拾取
}

-- ========== 直接按键（KeyCode，不经过 Input Manager）==========
GC.KEYCODE = {
    ROLL   = CS.UnityEngine.KeyCode.F,
    RELOAD = CS.UnityEngine.KeyCode.R,
}

return GC
