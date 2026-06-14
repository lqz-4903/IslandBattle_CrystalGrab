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
GC.DEFAULT_HP         = 100     -- 默认生命值
GC.DAMAGE_MIN          = 7       -- ★ 最小伤害值
GC.DAMAGE_MAX          = 20      -- ★ 最大伤害值
GC.CRYSTAL_SCORE_VALUE = 6     -- 每颗水晶分数
GC.ZONE_SPAWN_INTERVAL = 1.5   -- 每区域水晶生成间隔（秒）
GC.SPAWN_ZONE_COUNT    = 5     -- 生成区域数
GC.DEFAULT_GAME_DURATION = 127 -- 默认游戏时长（秒）
GC.PICKUP_RANGE        = 0.8   -- 水晶拾取距离（米）
-- ★ 不再设胜利分数（时间结束按最高分判定）
-- ★ 不再设水晶同时在场上限（无限制）

-- ========== 客户端模拟参数 ==========
GC.TICK_RATE       = 30       -- 逻辑帧率（与服务端 TickSyncHandler 一致）
GC.TICK_INTERVAL   = 1 / 30   -- 每帧间隔（秒）
GC.MOVE_SPEED      = 5        -- 玩家移动速度（米/秒）
GC.JUMP_FORCE      = 8        -- 跳跃初速度
GC.GRAVITY         = 20       -- 重力加速度
GC.PHYSICS_SUBSTEPS = 8        -- 远程玩家每 tick 物理子步数（匹配 120fps 碰撞精度，缩小主机端远程玩家与客户端自视的差异）
GC.MOUSE_SENSITIVITY = 0.003 -- 鼠标灵敏度（每像素旋转弧度，0.003≈0.17°/像素）

-- ========== 网络插值参数（消除 30fps tick → 60fps 渲染的卡顿）==========
GC.INTERP_INTERVAL    = 1 / 30   -- 插值时间窗口（与 TICK_RATE 一致）
GC.INTERP_MAX_EXTRAP  = 2        -- 最大外推 tick 数（tick 延迟时短暂外推）
GC.INTERP_ROT_SPEED   = 720      -- 旋转插值速度（度/秒），使用 RotateTowards
GC.INPUT_BUFFER_MAX       = 180     -- 输入缓冲区最大容量（约 6 秒 @ 30fps）

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

-- ========== 箭矢参数 ==========
GC.ARROW_SPEED      = 80        -- 飞行速度 (m/s)
GC.ARROW_LIFETIME   = 2.0       -- 存活时间 (秒)
GC.ARROW_POOL_SIZE  = 20        -- 对象池预加载数量
GC.ARROW_POOL_AB    = "player"  -- AB 包名
GC.ARROW_POOL_RES   = "ArrowDefault"  -- 资源名

return GC
