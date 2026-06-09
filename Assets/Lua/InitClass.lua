package.path = package.path .. ";Assets/Lua/Libs/?.lua"
package.path = package.path .. ";Assets/Lua/UI/?.lua"
package.path = package.path .. ";Assets/Lua/Core/?.lua"
package.path = package.path .. ";Assets/Lua/Battle/?.lua"

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
-- 确定性随机数
DeterministicRandom = require("DeterministicRandom")

----- 全局Update驱动（由C#侧GameMgr.Update每帧调用）
-- ★ 优化：使用数组+稀疏标记替代 pairs() 迭代，避免每帧分配迭代器
--   RegisterUpdate 返回的 id 是数组下标，UnregisterUpdate 将槽位置为 nil
--   Update 时用数值 for + rawget 跳过 nil 槽位（pairless iteration）
local updateCallbacks = {}     -- [id] = callback
local updateNextId = 0          -- 单调递增ID分配器
local updateNeedCompact = false -- 是否需要压缩数组

function RegisterUpdate(callback)
    updateNextId = updateNextId + 1
    updateCallbacks[updateNextId] = callback
    return updateNextId
end

function UnregisterUpdate(id)
    if updateCallbacks[id] ~= nil then
        updateCallbacks[id] = nil
        updateNeedCompact = true
    end
end

function Update(dt)
    -- 数值 for 循环（无迭代器分配）
    for i = 1, updateNextId do
        local callback = updateCallbacks[i]
        if callback ~= nil then
            callback(dt)
        end
    end

    -- 定期压缩稀疏数组（nil 槽位过多时）
    if updateNeedCompact then
        local nilCount = 0
        for i = 1, updateNextId do
            if updateCallbacks[i] == nil then
                nilCount = nilCount + 1
            end
        end
        -- nil 槽位超过一半时重建数组
        if nilCount > updateNextId / 2 then
            local newCallbacks = {}
            local newId = 0
            for i = 1, updateNextId do
                local cb = updateCallbacks[i]
                if cb ~= nil then
                    newId = newId + 1
                    newCallbacks[newId] = cb
                end
            end
            -- 替换全局表
            updateCallbacks = newCallbacks
            updateNextId = newId
        end
        updateNeedCompact = false
    end
end