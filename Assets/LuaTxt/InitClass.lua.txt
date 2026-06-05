package.path = package.path .. ";Assets/Lua/Libs/?.lua"
package.path = package.path .. ";Assets/Lua/UI/?.lua"

----- 常用别名
-- 面向对象父类
require("Object")
-- 字符串拆分
require("SplitTools")
-- Json读取
require("JsonUtility")
-- 官方类名别名
require("Official_NickName")
-- 自定义别名
require("Custom_NickName")
-- 判空
require("IsNull")

----- 全局Update驱动（由C#侧GameEntry.Update每帧调用）
-- 回调表：{key = callbackFunc}
local updateCallbacks = {}
local nextId = 0

-- 注册一个每帧回调，返回id（用于注销）
function RegisterUpdate(callback)
    nextId = nextId + 1
    updateCallbacks[nextId] = callback
    return nextId
end

-- 注销一个每帧回调
function UnregisterUpdate(id)
    updateCallbacks[id] = nil
end

-- 全局Update，由C#侧调用，dt为Time.deltaTime
function Update(dt)
    for id, callback in pairs(updateCallbacks) do
        callback(dt)
    end
end