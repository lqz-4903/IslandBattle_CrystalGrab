-- =============================================
-- Lua/SplitTools.lua — 字符串分割扩展
-- =============================================

function string.split(input, delimiter)
    input = tostring(input)
    delimiter = tostring(delimiter)
    if delimiter == "" then return {} end

    local arr = {}
    local pos = 1

    -- 使用迭代器闭包：每次调用返回下一对 (st, sp)，结束时返回 nil
    local function iterator()
        if pos > #input then return nil end
        local st, sp = string.find(input, delimiter, pos, true)
        if st == nil then return nil end
        return st, sp
    end

    for st, sp in iterator do
        table.insert(arr, string.sub(input, pos, st - 1))
        pos = sp + 1
    end
    -- 最后一个片段
    table.insert(arr, string.sub(input, pos))
    return arr
end
