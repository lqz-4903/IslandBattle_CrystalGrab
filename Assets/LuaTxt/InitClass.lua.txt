package.path = package.path .. ";Assets/Lua/Libs/?.lua"
package.path = package.path .. ";Assets/Lua/UI/?.lua"
package.path = package.path .. ";Assets/Lua/Core/?.lua"
package.path = package.path .. ";Assets/Lua/Battle/?.lua"
package.path = package.path .. ";Assets/Lua/Test/?.lua"

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
local updateCallbacks = {}     -- [id] = callback
local updateNextId = 0          -- 单调递增ID分配器
local updateMaxId = 0           -- 已分配过的最大ID（用于遍历上界）
local updateNeedCompact = false -- 是否需要压缩数组

function RegisterUpdate(callback)
    updateNextId = updateNextId + 1
    updateCallbacks[updateNextId] = callback
    updateMaxId = updateNextId
    return updateNextId
end

function UnregisterUpdate(id)
    if updateCallbacks[id] ~= nil then
        updateCallbacks[id] = nil
        updateNeedCompact = true
    end
end

function Update(dt)
    -- 使用 updateMaxId 作为遍历上界，避免压缩后 ID 越界
    for i = 1, updateMaxId do
        local callback = updateCallbacks[i]
        if callback ~= nil then
            callback(dt)
        end
    end

    -- 定期压缩稀疏数组（nil 槽位过多时）
    if updateNeedCompact then
        local nilCount = 0
        for i = 1, updateMaxId do
            if updateCallbacks[i] == nil then
                nilCount = nilCount + 1
            end
        end
        -- nil 槽位超过一半时重建数组
        if nilCount > updateMaxId / 2 then
            local newCallbacks = {}
            local newId = 0
            for i = 1, updateMaxId do
                local cb = updateCallbacks[i]
                if cb ~= nil then
                    newId = newId + 1
                    newCallbacks[newId] = cb
                end
            end
            updateCallbacks = newCallbacks
            -- ★ 修复：不修改 updateNextId（ID 分配器），只更新遍历上界
            updateMaxId = newId
        end
        updateNeedCompact = false
    end
end

----- 全局 LateUpdate 驱动（由 C# 侧 GameMgr.LateUpdate 每帧调用）
----- ★ 在所有 Update（含 TickExecutor 物理回退）之后执行，用于渲染层（插值/摄像机）
local lateUpdateCallbacks = {}
local lateUpdateNextId = 0
local lateUpdateMaxId = 0           -- 已分配过的最大ID（用于遍历上界）
local lateUpdateNeedCompact = false -- 是否需要压缩数组

function RegisterLateUpdate(callback)
    lateUpdateNextId = lateUpdateNextId + 1
    lateUpdateCallbacks[lateUpdateNextId] = callback
    lateUpdateMaxId = lateUpdateNextId
    return lateUpdateNextId
end

function UnregisterLateUpdate(id)
    if lateUpdateCallbacks[id] ~= nil then
        lateUpdateCallbacks[id] = nil
        lateUpdateNeedCompact = true
    end
end

function LateUpdate(dt)
    -- 使用 lateUpdateMaxId 作为遍历上界，避免压缩后 ID 越界
    for i = 1, lateUpdateMaxId do
        local callback = lateUpdateCallbacks[i]
        if callback ~= nil then
            callback(dt)
        end
    end

    -- 定期压缩稀疏数组（nil 槽位过多时）
    if lateUpdateNeedCompact then
        local nilCount = 0
        for i = 1, lateUpdateMaxId do
            if lateUpdateCallbacks[i] == nil then
                nilCount = nilCount + 1
            end
        end
        -- nil 槽位超过一半时重建数组
        if nilCount > lateUpdateMaxId / 2 then
            local newCallbacks = {}
            local newId = 0
            for i = 1, lateUpdateMaxId do
                local cb = lateUpdateCallbacks[i]
                if cb ~= nil then
                    newId = newId + 1
                    newCallbacks[newId] = cb
                end
            end
            lateUpdateCallbacks = newCallbacks
            lateUpdateMaxId = newId
        end
        lateUpdateNeedCompact = false
    end
end