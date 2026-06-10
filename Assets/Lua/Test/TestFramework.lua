-- =============================================
-- Test/TestFramework.lua — 轻量测试断言框架
-- =============================================
-- 不依赖任何外部库，纯 Lua 可运行。
-- =============================================

local TF = {}

-- 容差常量
TF.TIGHT = 0.001   -- 1mm（单 tick 确定性）
TF.LOOSE = 0.01    -- 1cm（多 tick 累积）

-- ========== 测试结果收集 ==========

TF.results = {}     -- { name, pass, actual, expected, msg }
TF.passCount = 0
TF.failCount = 0
TF.currentGroup = ""

--- 开始一个测试组
function TF.group(name)
    TF.currentGroup = name
    print("\n========== " .. name .. " ==========")
end

--- 记录单个测试结果
--- @param name string
--- @param pass bool
--- @param msg string|nil
local function record(name, pass, msg, actual, expected)
    local r = {
        group = TF.currentGroup,
        name = name,
        pass = pass,
        msg = msg or "",
        actual = actual,
        expected = expected,
    }
    table.insert(TF.results, r)
    if pass then
        TF.passCount = TF.passCount + 1
        print("  ✓ " .. name)
    else
        TF.failCount = TF.failCount + 1
        local extra = ""
        if actual ~= nil and expected ~= nil then
            extra = "  actual=" .. tostring(actual) .. " expected=" .. tostring(expected)
        end
        print("  ✗ FAIL " .. name .. "  " .. (msg or "") .. extra)
    end
end

-- ========== 断言 ==========

--- 断言条件为 true
function TF.assertTrue(condition, msg)
    if condition then
        record(msg or "assertTrue", true)
    else
        record(msg or "assertTrue", false, "expected true, got false", "false", "true")
    end
    return condition
end

--- 断言条件为 false
function TF.assertFalse(condition, msg)
    if not condition then
        record(msg or "assertFalse", true)
    else
        record(msg or "assertFalse", false, "expected false, got true", "true", "false")
    end
    return not condition
end

--- 断言两个数值相等（默认容差 TIGHT）
--- @param actual number
--- @param expected number
--- @param tolerance number|nil
--- @param msg string|nil
function TF.assertEqual(actual, expected, tolerance, msg)
    tolerance = tolerance or TF.TIGHT
    local diff = math.abs((actual or 0) - (expected or 0))
    local pass = diff <= tolerance
    record(msg or "assertEqual", pass, nil, actual, expected)
    return pass
end

--- 断言两个 Vector3 近似相等
--- @param actual UnityEngine.Vector3|table  -- {x,y,z} or Vector3
--- @param expected UnityEngine.Vector3|table
--- @param tolerance number|nil
--- @param msg string|nil
function TF.assertVec3Near(actual, expected, tolerance, msg)
    tolerance = tolerance or TF.TIGHT
    local ax, ay, az = actual.x or actual[1] or 0, actual.y or actual[2] or 0, actual.z or actual[3] or 0
    local ex, ey, ez = expected.x or expected[1] or 0, expected.y or expected[2] or 0, expected.z or expected[3] or 0
    local dx = math.abs(ax - ex)
    local dy = math.abs(ay - ey)
    local dz = math.abs(az - ez)
    local pass = dx <= tolerance and dy <= tolerance and dz <= tolerance
    local msgFull = (msg or "assertVec3Near") ..
        string.format(" (%.4f,%.4f,%.4f) vs (%.4f,%.4f,%.4f)", ax, ay, az, ex, ey, ez)
    record(msg or "assertVec3Near", pass, msgFull, nil, nil)
    return pass
end

--- 断言两个 Vector3 逐分量完全相等（== 比较）
function TF.assertVec3Exact(actual, expected, msg)
    local ax, ay, az = actual.x or actual[1] or 0, actual.y or actual[2] or 0, actual.z or actual[3] or 0
    local ex, ey, ez = expected.x or expected[1] or 0, expected.y or expected[2] or 0, expected.z or expected[3] or 0
    local pass = (ax == ex) and (ay == ey) and (az == ez)
    local msgFull = (msg or "assertVec3Exact") ..
        string.format(" (%.6f,%.6f,%.6f) vs (%.6f,%.6f,%.6f)", ax, ay, az, ex, ey, ez)
    record(msg or "assertVec3Exact", pass, msgFull, nil, nil)
    return pass
end

--- 断言值在指定范围内
function TF.assertInRange(actual, minVal, maxVal, msg)
    local pass = actual >= minVal and actual <= maxVal
    record(msg or "assertInRange", pass, nil, actual, "[" .. minVal .. ", " .. maxVal .. "]")
    return pass
end

--- 断言不为 nil
function TF.assertNotNil(value, msg)
    local pass = value ~= nil
    record(msg or "assertNotNil", pass, nil, tostring(value), "not nil")
    return pass
end

--- 断言为 nil
function TF.assertNil(value, msg)
    local pass = value == nil
    record(msg or "assertNil", pass, nil, tostring(value), "nil")
    return pass
end

--- 断言不崩溃（pcall 包装）
--- @param fn function — 要执行的函数
--- @param msg string|nil
--- @return bool pass
function TF.assertNoCrash(fn, msg)
    local ok, err = pcall(fn)
    if ok then
        record(msg or "assertNoCrash", true)
    else
        record(msg or "assertNoCrash", false, "崩溃: " .. tostring(err))
    end
    return ok
end

-- ========== 汇总 ==========

--- 打印汇总报告
function TF.summary()
    print("\n========================================")
    print(string.format("  测试完成: %d 通过 / %d 失败 / %d 总计",
        TF.passCount, TF.failCount, TF.passCount + TF.failCount))
    print("========================================")

    if TF.failCount == 0 then
        print("★ 全部通过！")
    else
        print("✗ 失败用例:")
        for _, r in ipairs(TF.results) do
            if not r.pass then
                print(string.format("  [%s] %s — %s", r.group, r.name, r.msg))
            end
        end
    end

    -- 分组统计
    local groupStats = {}
    for _, r in ipairs(TF.results) do
        if not groupStats[r.group] then
            groupStats[r.group] = { pass = 0, fail = 0 }
        end
        if r.pass then
            groupStats[r.group].pass = groupStats[r.group].pass + 1
        else
            groupStats[r.group].fail = groupStats[r.group].fail + 1
        end
    end
    print("\n分组统计:")
    for group, stats in pairs(groupStats) do
        local total = stats.pass + stats.fail
        local icon = stats.fail == 0 and "✓" or "✗"
        print(string.format("  %s %s: %d/%d", icon, group, stats.pass, total))
    end

    -- ★ 写入文件，供 Claude Code 读取
    TF.WriteToFile()

    return TF.failCount == 0
end

--- 将结果写入 Logs/test_results.txt
function TF.WriteToFile()
    local lines = {}
    table.insert(lines, "=== 帧同步确定性测试报告 ===")
    table.insert(lines, string.format("时间: %s", os.date("%Y-%m-%d %H:%M:%S")))
    table.insert(lines, string.format("结果: %d 通过 / %d 失败 / %d 总计",
        TF.passCount, TF.failCount, TF.passCount + TF.failCount))
    table.insert(lines, "")

    -- 失败的排前面
    for _, r in ipairs(TF.results) do
        if not r.pass then
            table.insert(lines, string.format("FAIL [%s] %s", r.group, r.name))
            if r.msg and r.msg ~= "" then
                table.insert(lines, string.format("     %s", r.msg))
            end
        end
    end
    if TF.failCount == 0 then
        table.insert(lines, "ALL PASS")
    end
    table.insert(lines, "")

    -- 分组统计
    local groupStats = {}
    for _, r in ipairs(TF.results) do
        if not groupStats[r.group] then
            groupStats[r.group] = { pass = 0, fail = 0 }
        end
        if r.pass then
            groupStats[r.group].pass = groupStats[r.group].pass + 1
        else
            groupStats[r.group].fail = groupStats[r.group].fail + 1
        end
    end
    for group, stats in pairs(groupStats) do
        local total = stats.pass + stats.fail
        local icon = stats.fail == 0 and "PASS" or "FAIL"
        table.insert(lines, string.format("%s %s: %d/%d", icon, group, stats.pass, total))
    end

    local content = table.concat(lines, "\n")
    -- ★ 区分主机/客户端文件，避免覆盖
    local role = "client"
    if CS.KcpMgr.Instance ~= nil and CS.KcpMgr.Instance.ClientConv == 0 then
        role = "host"
    end
    local filename = "test_results_" .. role .. ".txt"

    -- 确保 Logs 目录存在
    local dataPath = CS.UnityEngine.Application.dataPath

    -- ★ ParrelSync 克隆检测：如果 dataPath 含 "_clone_"，推导原项目路径
    --    例: D:/xxx/Unity_ProjectDemo_clone_0/Assets → D:/xxx/Unity_ProjectDemo
    local originalDataPath = dataPath:gsub("_clone_%d+", "")

    -- 写入克隆自己的 Logs
    local logsDir = dataPath .. "/../Logs"
    CS.System.IO.Directory.CreateDirectory(logsDir)
    local path = logsDir .. "/" .. filename

    -- 写入原项目的 Logs（克隆也写入这里，方便 Claude Code 统一读取）
    local origLogsDir = originalDataPath .. "/../Logs"
    CS.System.IO.Directory.CreateDirectory(origLogsDir)
    local origPath = origLogsDir .. "/" .. filename

    print("[TestFramework] dataPath=" .. dataPath)
    local ok, err = pcall(function()
        CS.System.IO.File.WriteAllText(path, content)
    end)
    if not ok then
        print("[TestFramework] 本地写入失败: " .. tostring(err))
    end

    -- ★ 克隆时额外写入原项目 Logs
    if originalDataPath ~= dataPath then
        print("[TestFramework] 检测到克隆，同步写入原项目: " .. origPath)
        local ok2, _ = pcall(function()
            CS.System.IO.File.WriteAllText(origPath, content)
        end)
        if ok2 then
            print("[TestFramework] 已同步到原项目 Logs")
        end
    end

    print("[TestFramework] 结果已写入: " .. path)
end

--- 重置所有状态
function TF.reset()
    TF.results = {}
    TF.passCount = 0
    TF.failCount = 0
    TF.currentGroup = ""
end

return TF
